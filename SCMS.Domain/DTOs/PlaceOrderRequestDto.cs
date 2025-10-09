using System.Collections.Generic;

namespace SCMS.Domain.DTOs
{
    public class PlaceOrderRequestDto
    {
        public List<OrderItemDto> Items { get; set; }
        public DateTime? PickupTime { get; set; }
    }
}