namespace Horizon.Share.Dtos.Flower;

/// <summary>店铺等级 API DTO（对应 ShopGradeState，纯 POCO）。</summary>
public class ShopGradeDto
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public int ProductLimit { get; set; }
    public int ImageLimit { get; set; }
    public int TemplateLimit { get; set; }
    public decimal ChargeStandard { get; set; }
    public string Remark { get; set; } = "";
}
