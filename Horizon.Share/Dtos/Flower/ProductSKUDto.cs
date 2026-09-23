namespace Horizon.Share.Dtos.Flower;

/// <summary>商品 SKU API DTO（对应 ProductSKUState，纯 POCO）。</summary>
public class ProductSKUDto
{
    public long Id { get; set; }
    public long ProductId { get; set; }
    public string SkuCode { get; set; } = "";
    public string Color { get; set; } = "";
    public string Size { get; set; } = "";
    public string Version { get; set; } = "";
    public decimal SalePrice { get; set; }
    public decimal CostPrice { get; set; }
    public long Stock { get; set; }
    public long? SafeStock { get; set; }
    public string ShowPic { get; set; } = "";
}
