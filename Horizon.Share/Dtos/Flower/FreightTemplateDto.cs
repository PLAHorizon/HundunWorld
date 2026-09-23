namespace Horizon.Share.Dtos.Flower;

/// <summary>运费模板 API DTO（对应 FreightTemplateState，纯 POCO）。</summary>
public class FreightTemplateDto
{
    public long Id { get; set; }
    public long MerchantId { get; set; }
    public string Name { get; set; } = "";
    public int ValuationMethod { get; set; }
    public bool IsFree { get; set; }
    public decimal FirstUnit { get; set; }
    public decimal FirstPrice { get; set; }
    public decimal ContinueUnit { get; set; }
    public decimal ContinuePrice { get; set; }
    public decimal? FreeConditionAmount { get; set; }
    public string AreaRules { get; set; } = "";
}
