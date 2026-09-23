namespace Horizon.Share.Dtos.Flower;

/// <summary>品牌 API DTO（对应 BrandState，纯 POCO）。</summary>
public class BrandDto
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string Logo { get; set; } = "";
    public string Description { get; set; } = "";
    public long DisplaySequence { get; set; }
    public bool IsRecommend { get; set; }
    public int AuditStatus { get; set; }
}
