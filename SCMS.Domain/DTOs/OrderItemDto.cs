namespace SCMS.Domain.DTOs
{
    public class OrderItemDto
    {
        public int MenuItemId { get; set; }
        public int Quantity { get; set; }
        public decimal Price { get; set; }      
        public string? MenuItemName { get; set; } 
    }
}