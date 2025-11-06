using SCMS.Domain;
using SCMS.Domain.DTOs;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace SCMS.WebApp.Services
{
    public class OperationResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public class PlaceOrderResult : OperationResult
    {
        public Order? CreatedOrder { get; set; }
    }

    public class UpdateOrderResult : OperationResult
    {
        public Order? UpdatedOrder { get; set; }
    }

    public class ErrorResponse
    {
        public string Message { get; set; } = string.Empty;
    }


    public class OrderService
    {
        private readonly HttpClient _httpClient;
        private readonly NotificationService _notificationService;

        public OrderService(HttpClient httpClient, NotificationService notificationService)
        {
            _httpClient = httpClient;
            _notificationService = notificationService;
        }


        public async Task<PlaceOrderResult> PlaceOrderAsync(PlaceOrderRequestDto orderDto)
        {
            try
            {
                var response = await _httpClient.PostAsJsonAsync("api/Orders", orderDto);

                if (response.IsSuccessStatusCode)
                {
                    await _notificationService.RefreshUnreadCountAsync();
                    var createdOrder = await response.Content.ReadFromJsonAsync<Order>();
                    return new PlaceOrderResult { Success = true, CreatedOrder = createdOrder, Message = "Đặt hàng thành công!" };
                }
                else
                {
                    var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
                    return new PlaceOrderResult { Success = false, Message = error?.Message ?? "Đặt hàng thất bại." };
                }
            }
            catch (Exception ex)
            {
                return new PlaceOrderResult { Success = false, Message = $"Lỗi hệ thống: {ex.Message}" };
            }
        }

        public async Task<UpdateOrderResult> UpdateOrderAsync(int orderId, PlaceOrderRequestDto orderDto)
        {
            try
            {
                var response = await _httpClient.PutAsJsonAsync($"api/Orders/{orderId}", orderDto);

                if (response.IsSuccessStatusCode)
                {
                    var updatedOrder = await response.Content.ReadFromJsonAsync<Order>();
                    return new UpdateOrderResult { Success = true, UpdatedOrder = updatedOrder, Message = "Cập nhật đơn hàng thành công!" };
                }
                else
                {
                    var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
                    return new UpdateOrderResult { Success = false, Message = error?.Message ?? "Cập nhật thất bại." };
                }
            }
            catch (Exception ex)
            {
                return new UpdateOrderResult { Success = false, Message = $"Lỗi hệ thống: {ex.Message}" };
            }
        }

        public async Task<OperationResult> ConfirmOrderPaymentAsync(int orderId)
        {
            try
            {
                var response = await _httpClient.PostAsync($"api/Orders/{orderId}/confirm-payment", null);

                var result = await response.Content.ReadFromJsonAsync<ErrorResponse>(); 
                if (response.IsSuccessStatusCode)
                {
                    await _notificationService.RefreshUnreadCountAsync();
                }
                return new OperationResult
                {
                    Success = response.IsSuccessStatusCode,
                    Message = result?.Message ?? "Thao tác không nhận được phản hồi."
                };
            }
            catch (Exception ex)
            {
                return new OperationResult { Success = false, Message = $"Lỗi hệ thống: {ex.Message}" };
            }
        }

        public async Task<OperationResult> CancelOrderAsync(int orderId)
        {
            try
            {
                var response = await _httpClient.PostAsync($"api/Orders/{orderId}/cancel", null);

                var result = await response.Content.ReadFromJsonAsync<ErrorResponse>();
                if (response.IsSuccessStatusCode)
                {
                    await _notificationService.RefreshUnreadCountAsync();
                }
                return new OperationResult
                {
                    Success = response.IsSuccessStatusCode,
                    Message = result?.Message ?? "Thao tác không nhận được phản hồi."
                };
            }
            catch (Exception ex)
            {
                return new OperationResult { Success = false, Message = $"Lỗi hệ thống: {ex.Message}" };
            }
        }



        public async Task<List<Order>?> GetMyOrdersAsync()
        {
            return await _httpClient.GetFromJsonAsync<List<Order>>("api/orders/my-orders");
        }

        public async Task<List<Order>?> GetProcessableOrdersAsync()
        {
            return await _httpClient.GetFromJsonAsync<List<Order>>("api/Orders");
        }

        public async Task<List<Order>?> GetOrderHistoryAsync()
        {
            return await _httpClient.GetFromJsonAsync<List<Order>>("api/Orders/history");
        }

        public async Task<(bool Success, string Message, Order? Order)> ProgressOrderAsync(int orderId)
        {
            try
            {
                var response = await _httpClient.PostAsync($"api/orders/{orderId}/progress", null);
                if (response.IsSuccessStatusCode)
                {
                    var updatedOrder = await response.Content.ReadFromJsonAsync<Order>();
                    return (true, "Cập nhật thành công", updatedOrder);
                }
                var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
                return (false, error?.Message ?? "Cập nhật thất bại.", null);
            }
            catch (Exception ex)
            {
                return (false, $"Lỗi hệ thống: {ex.Message}", null);
            }
        }

        public async Task<OperationResult> RejectOrderAsync(int orderId, string reason)
        {
            try
            {
                var dto = new UpdateOrderStatusDto { Status = "Cancelled", RejectionReason = reason };
                var response = await _httpClient.PostAsJsonAsync($"api/orders/{orderId}/reject", dto);

                var result = await response.Content.ReadFromJsonAsync<ErrorResponse>();
                return new OperationResult
                {
                    Success = response.IsSuccessStatusCode,
                    Message = result?.Message ?? "Thao tác không nhận được phản hồi."
                };
            }
            catch (Exception ex)
            {
                return new OperationResult { Success = false, Message = $"Lỗi hệ thống: {ex.Message}" };
            }
        }

    }
}