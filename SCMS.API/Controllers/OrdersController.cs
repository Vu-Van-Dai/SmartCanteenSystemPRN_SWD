using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SCMS.Application;
using SCMS.Domain.DTOs;
using System.Threading.Tasks;

namespace SCMS.API.Controllers
{
    [AllowAnonymous]
    [ApiController]
    [Route("api/[controller]")]
    public class OrdersController : ControllerBase
    {
        private readonly OrderService _orderService;

        public OrdersController(OrderService orderService)
        {
            _orderService = orderService;
        }

        [HttpPost]
        public async Task<IActionResult> PlaceOrder(PlaceOrderRequestDto orderDto)
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userIdString))
            {
                return Unauthorized();
            }
            var userId = int.Parse(userIdString);
            var order = await _orderService.PlaceOrderAsync(orderDto, userId);
            return CreatedAtAction(nameof(PlaceOrder), new { id = order.OrderId }, order);
        }

        [HttpGet]
        public async Task<IActionResult> GetProcessableOrders()
        {
            var orders = await _orderService.GetProcessableOrdersAsync();
            return Ok(orders);
        }

        [HttpPost("{orderId}/progress")]
        public async Task<IActionResult> ProgressOrder(int orderId)
        {
            var result = await _orderService.ProgressOrderStatusAsync(orderId);
            if (!result.Success)
            {
                return BadRequest(new { message = result.Message });
            }
            return Ok(result.Order);
        }

        [HttpPost("{orderId}/reject")]
        public async Task<IActionResult> RejectOrder(int orderId, [FromBody] UpdateOrderStatusDto dto)
        {
            if (dto == null || string.IsNullOrWhiteSpace(dto.RejectionReason))
            {
                return BadRequest(new { message = "Vui lòng cung cấp lý do từ chối." });
            }

            var result = await _orderService.RejectOrderAsync(orderId, dto.RejectionReason);

            if (!result.Success)
            {
                return BadRequest(new { message = result.Message });
            }
            return Ok(new { message = result.Message });
        }

        [HttpGet("my-orders")]
        public async Task<IActionResult> GetMyOrders()
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userIdString))
            {
                return Unauthorized();
            }
            var userId = int.Parse(userIdString);
            var orders = await _orderService.GetOrdersByUserIdAsync(userId);
            return Ok(orders);
        }

        [HttpPost("{orderId}/confirm-payment")]
        public async Task<IActionResult> ConfirmPayment(int orderId)
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            var result = await _orderService.ConfirmOrderPaymentAsync(orderId, userId);

            if (!result.Success)
            {
                return BadRequest(new { message = result.Message });
            }

            return Ok(new { message = result.Message });
        }

        [HttpPost("{orderId}/cancel")]
        public async Task<IActionResult> CancelOrder(int orderId)
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var result = await _orderService.CancelOrderAsync(orderId, userId);

            if (!result.Success)
            {
                return BadRequest(new { message = result.Message });
            }
            return Ok(new { message = result.Message });
        }

        [HttpGet("history")]
        public async Task<IActionResult> GetOrderHistory()
        {
            var orders = await _orderService.GetAllOrdersAsync();
            return Ok(orders);
        }
        [HttpPut("{orderId}")]
        public async Task<IActionResult> UpdateOrder(int orderId, PlaceOrderRequestDto updatedOrderDto)
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var result = await _orderService.UpdatePendingOrderAsync(orderId, userId, updatedOrderDto);

            if (!result.Success)
            {
                switch (result.ErrorCode)
                {
                    case "NOT_FOUND":
                        return NotFound(new { message = result.Message });
                    case "INVALID_STATUS":
                    case "OUT_OF_STOCK":
                        return BadRequest(new { message = result.Message });
                    default:
                        return StatusCode(500, new { message = result.Message });
                }
            }

            return Ok(result.UpdatedOrder); // Trả về đơn hàng đã được cập nhật
        }
    }
}