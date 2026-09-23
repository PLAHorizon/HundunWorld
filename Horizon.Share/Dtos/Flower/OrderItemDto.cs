namespace Horizon.Share.Dtos.Flower;

/// <summary>订单明细 API DTO（对应 OrderItemState，纯 POCO）。</summary>
public class OrderItemDto
{
    public long ProductId { get; set; }
    public int SpeciesId { get; set; }
    public string ProductName { get; set; } = "";
    public decimal Price { get; set; }
    public int Quantity { get; set; }
    public decimal Subtotal { get; set; }
}
