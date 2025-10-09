using Microsoft.EntityFrameworkCore;
using SCMS.Domain.DTOs;
using SCMS.Domain;
using SCMS.Infrastructure;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace SCMS.Application
{
    public class OrderService
    {
        private readonly ApplicationDbContext _context;
        private readonly IWalletService _walletService;
        private readonly NotificationService _notificationService;

        public OrderService(ApplicationDbContext context, IWalletService walletService, NotificationService notificationService)
        {
            _context = context;
            _walletService = walletService;
            _notificationService = notificationService;
        }

        public async Task<Order> PlaceOrderAsync(PlaceOrderRequestDto orderDto, int userId)
        {
            var menuItemIds = orderDto.Items.Select(item => item.MenuItemId).ToList();
            var menuItems = await _context.MenuItems
                .Where(mi => menuItemIds.Contains(mi.ItemId))
                .ToDictionaryAsync(mi => mi.ItemId);

            foreach (var item in orderDto.Items)
            {
                if (!menuItems.TryGetValue(item.MenuItemId, out var menuItem) || menuItem.InventoryQuantity < item.Quantity)
                {
                    throw new InvalidOperationException($"Not enough stock for menu item ID {item.MenuItemId}.");
                }
            }

            DateTime? pickupTimeToSave = null;
            if (orderDto.PickupTime.HasValue)
            {
                pickupTimeToSave = orderDto.PickupTime.Value.ToUniversalTime();

                if (pickupTimeToSave.Value <= DateTime.UtcNow)
                {
                    throw new InvalidOperationException("Pickup time must be in the future.");
                }
            }

            decimal totalPrice = orderDto.Items.Sum(item => menuItems[item.MenuItemId].Price * item.Quantity);

            var order = new Order
            {
                UserId = userId,
                OrderDate = DateTime.UtcNow, 
                TotalPrice = totalPrice,
                Status = "Pending Payment",
                PickupTime = pickupTimeToSave
            };

            order.OrderItems = orderDto.Items.Select(item => new OrderItem
            {
                ItemId = item.MenuItemId,
                Quantity = item.Quantity,
                PriceAtTimeOfOrder = menuItems[item.MenuItemId].Price
            }).ToList();

            foreach (var item in orderDto.Items)
            {
                menuItems[item.MenuItemId].InventoryQuantity -= item.Quantity;
            }

            _context.Orders.Add(order);
            await _context.SaveChangesAsync();

            return order;
        }

        
        public async Task<(bool Success, string Message, Order? Order)> ProgressOrderStatusAsync(int orderId)
        {
            var order = await _context.Orders
                .Include(o => o.User)
                .Include(o => o.OrderItems).ThenInclude(oi => oi.MenuItem)
                .FirstOrDefaultAsync(o => o.OrderId == orderId);

            if (order == null)
            {
                return (false, "Không tìm thấy đơn hàng.", null);
            }

            string nextStatus = order.Status switch
            {
                "Paid" => "Preparing",
                "Preparing" => "Ready for Pickup",
                "Ready for Pickup" => "Completed",
                _ => ""
            };

            if (string.IsNullOrEmpty(nextStatus))
            {
                return (false, $"Không thể chuyển tiếp đơn hàng từ trạng thái '{order.Status}'.", null);
            }

            order.Status = nextStatus;
            await _context.SaveChangesAsync();
            string notificationMessage = nextStatus switch
            {
                "Preparing" => $"Đơn hàng #{order.OrderId} của bạn đang được chuẩn bị.",
                "Ready for Pickup" => $"Đơn hàng #{order.OrderId} đã sẵn sàng! Mời bạn đến nhận.",
                "Completed" => $"Đơn hàng #{order.OrderId} đã hoàn tất. Cảm ơn bạn!",
                _ => ""
            };

            if (!string.IsNullOrEmpty(notificationMessage))
            {
                await _notificationService.CreateNotificationAsync(order.UserId, notificationMessage, "/my-orders/");
            }

            return (true, $"Đã cập nhật đơn hàng sang trạng thái '{nextStatus}'.", order);
        }

       
        public async Task<List<Order>> GetOrdersByUserIdAsync(int userId)
        {
            return await _context.Orders
                .Where(o => o.UserId == userId)
                .Include(o => o.OrderItems)
                .ThenInclude(oi => oi.MenuItem)
                .OrderByDescending(o => o.OrderDate)
                .ToListAsync();
        }
        public async Task<(bool Success, string Message)> ConfirmOrderPaymentAsync(int orderId, int userId)
        {
            var order = await _context.Orders
                .FirstOrDefaultAsync(o => o.OrderId == orderId && o.UserId == userId);

            if (order == null)
            {
                return (false, "Không tìm thấy đơn hàng hoặc bạn không có quyền thực hiện thao tác này.");
            }

            if (order.Status != "Pending Payment")
            {
                return (false, $"Đơn hàng đang ở trạng thái '{order.Status}', không thể thanh toán.");
            }

            var paymentSuccessful = await _walletService.ProcessPaymentAsync(userId, orderId, order.TotalPrice);

            if (!paymentSuccessful)
            {
                return (false, "Thanh toán thất bại. Số dư trong ví không đủ.");
            }

            order.Status = "Paid";
            await _context.SaveChangesAsync();
            await _notificationService.CreateNotificationAsync(userId, $"Bạn đã thanh toán thành công cho đơn hàng #{order.OrderId}.", "/my-orders/");

            return (true, "Thanh toán đơn hàng thành công.");
        }
        public async Task<(bool Success, string Message)> CancelOrderAsync(int orderId, int userId)
        {
            var order = await _context.Orders
                .Include(o => o.OrderItems)
                .FirstOrDefaultAsync(o => o.OrderId == orderId);

            if (order == null) return (false, "Không tìm thấy đơn hàng.");
            if (order.UserId != userId) return (false, "Bạn không có quyền hủy đơn hàng này.");

            if (order.Status == "Ready for Pickup")
            {
                return (false, "Không thể hủy vì đơn hàng đã sẵn sàng để nhận.");
            }

            if (order.Status != "Paid")
            {
                return (false, $"Không thể hủy đơn hàng với trạng thái '{order.Status}'. Đơn hàng chỉ có thể hủy khi đã thanh toán thành công.");
            }

            decimal refundPercentage;
            string successMessage;

            if (order.PickupTime.HasValue)
            {
                var timeUntilPickup = order.PickupTime.Value - DateTime.UtcNow;

                if (timeUntilPickup.TotalMinutes < 30)
                {
                    refundPercentage = 0.9m; 
                    successMessage = "Hủy đơn hàng thành công. Vì bạn hủy trong vòng 30 phút trước giờ nhận, phí hủy 10% đã được áp dụng. 90% giá trị đơn đã được hoàn vào ví.";
                }
                else
                {
                    refundPercentage = 1.0m; 
                    successMessage = "Hủy đơn hàng thành công. Toàn bộ giá trị đơn hàng đã được hoàn vào ví của bạn.";
                }
            }
            else
            {
                var timeSinceOrder = DateTime.UtcNow - order.OrderDate;

                if (timeSinceOrder.TotalMinutes <= 10)
                {
                    refundPercentage = 1.0m; 
                    successMessage = "Hủy đơn hàng thành công trong 10 phút đầu, đã hoàn lại 100% giá trị đơn hàng.";
                }
                else if (timeSinceOrder.TotalMinutes <= 15) 
                {
                    refundPercentage = 0.9m; 
                    successMessage = "Hủy đơn hàng thành công. Phí hủy 10% đã được áp dụng, 90% giá trị đơn hàng đã được hoàn vào ví.";
                }
                else if (timeSinceOrder.TotalMinutes <= 20) 
                {
                    refundPercentage = 0.2m; 
                    successMessage = "Hủy đơn hàng thành công. Phí hủy 80% đã được áp dụng, 20% giá trị đơn hàng đã được hoàn vào ví.";
                }
                else 
                {
                    return (false, "Đã quá 20 phút kể từ lúc đặt hàng, không thể hủy đơn.");
                }
            }

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                decimal refundAmount = order.TotalPrice * refundPercentage;
                string refundMessage = $"Hoàn tiền {refundPercentage:P0} cho đơn hàng bị hủy #{orderId}.";

                if (refundAmount > 0)
                {
                    var refundSuccess = await _walletService.RefundAsync(order.OrderId, refundAmount, refundMessage);
                    if (!refundSuccess)
                    {
                        await transaction.RollbackAsync();
                        return (false, "Hoàn tiền thất bại, vui lòng thử lại.");
                    }
                }

                var menuItemIds = order.OrderItems.Select(oi => oi.ItemId).ToList();
                var menuItemsToUpdate = await _context.MenuItems
                                                      .Where(mi => menuItemIds.Contains(mi.ItemId))
                                                      .ToListAsync();
                foreach (var orderItem in order.OrderItems)
                {
                    var menuItem = menuItemsToUpdate.FirstOrDefault(mi => mi.ItemId == orderItem.ItemId);
                    if (menuItem != null)
                    {
                        menuItem.InventoryQuantity += orderItem.Quantity;
                    }
                }

                order.Status = "Cancelled";

                await _context.SaveChangesAsync();
                await _notificationService.CreateNotificationAsync(userId, successMessage, "/my-orders/");
                await transaction.CommitAsync();

                return (true, successMessage);
            }
            catch (Exception)
            {
                await transaction.RollbackAsync();
                return (false, "Đã xảy ra lỗi hệ thống khi hủy đơn.");
            }
        }

        public async Task<List<Order>> GetProcessableOrdersAsync()
        {
            var processableStatuses = new[] { "Paid", "Preparing", "Ready for Pickup" };

            return await _context.Orders
                .Where(o => processableStatuses.Contains(o.Status))
                .Include(o => o.User)
                .Include(o => o.OrderItems)
                .ThenInclude(oi => oi.MenuItem)
                .OrderBy(o => o.OrderDate)
                .ToListAsync();
        }

        
        public class UpdateOrderResult
        {
            public bool Success { get; set; }
            public string Message { get; set; }
            public string ErrorCode { get; set; } 
            public Order UpdatedOrder { get; set; }
        }

        public async Task<UpdateOrderResult> UpdatePendingOrderAsync(int orderId, int userId, PlaceOrderRequestDto updatedDto)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                var order = await _context.Orders
                    .Include(o => o.OrderItems)
                    .FirstOrDefaultAsync(o => o.OrderId == orderId && o.UserId == userId);

                if (order == null)
                {
                    return new UpdateOrderResult { Success = false, Message = "Không tìm thấy đơn hàng.", ErrorCode = "NOT_FOUND" };
                }

                if (order.Status != "Pending Payment")
                {
                    return new UpdateOrderResult { Success = false, Message = "Chỉ có thể chỉnh sửa đơn hàng ở trạng thái 'Pending Payment'.", ErrorCode = "INVALID_STATUS" };
                }

                var oldMenuItemIds = order.OrderItems.Select(oi => oi.ItemId).ToList();
                var oldMenuItems = await _context.MenuItems
                                                 .Where(mi => oldMenuItemIds.Contains(mi.ItemId))
                                                 .ToListAsync();
                foreach (var oldItem in order.OrderItems)
                {
                    var menuItem = oldMenuItems.FirstOrDefault(mi => mi.ItemId == oldItem.ItemId);
                    if (menuItem != null)
                    {
                        menuItem.InventoryQuantity += oldItem.Quantity;
                    }
                }

                _context.OrderItems.RemoveRange(order.OrderItems);
                await _context.SaveChangesAsync(); 

                var newMenuItemIds = updatedDto.Items.Select(i => i.MenuItemId).ToList();
                var newMenuItemsDict = await _context.MenuItems
                                                     .Where(mi => newMenuItemIds.Contains(mi.ItemId))
                                                     .ToDictionaryAsync(mi => mi.ItemId);

                decimal newTotalPrice = 0;
                var newOrderItems = new List<OrderItem>();

                foreach (var newItemDto in updatedDto.Items)
                {
                    if (!newMenuItemsDict.TryGetValue(newItemDto.MenuItemId, out var menuItem))
                    {
                        await transaction.RollbackAsync();
                        return new UpdateOrderResult { Success = false, Message = $"Món ăn với ID {newItemDto.MenuItemId} không tồn tại.", ErrorCode = "NOT_FOUND" };
                    }

                    if (menuItem.InventoryQuantity < newItemDto.Quantity)
                    {
                        await transaction.RollbackAsync();
                        return new UpdateOrderResult { Success = false, Message = $"Không đủ số lượng cho món '{menuItem.Name}'.", ErrorCode = "OUT_OF_STOCK" };
                    }

                    menuItem.InventoryQuantity -= newItemDto.Quantity;

                    newTotalPrice += menuItem.Price * newItemDto.Quantity;
                    newOrderItems.Add(new OrderItem
                    {
                        ItemId = newItemDto.MenuItemId,
                        Quantity = newItemDto.Quantity,
                        PriceAtTimeOfOrder = menuItem.Price
                    });
                }

                order.TotalPrice = newTotalPrice;
                order.OrderItems = newOrderItems;
                order.OrderDate = DateTime.UtcNow; 

                await _context.SaveChangesAsync();
                await transaction.CommitAsync(); 

                return new UpdateOrderResult { Success = true, UpdatedOrder = order };
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return new UpdateOrderResult { Success = false, Message = "Đã xảy ra lỗi hệ thống khi cập nhật đơn hàng.", ErrorCode = "SERVER_ERROR" };
            }
        }
        }
    }
}