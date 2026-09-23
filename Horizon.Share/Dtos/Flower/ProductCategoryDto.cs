namespace Horizon.Share.Dtos.Flower;

/// <summary>商品分类 API DTO（对应 ProductCategoryState，纯 POCO）。</summary>
public class ProductCategoryDto
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public int Depth { get; set; }
    public string Path { get; set; } = "";
    public long ParentCategoryId { get; set; }
    public long DisplaySequence { get; set; }
    public string Icon { get; set; } = "";
    public string Image { get; set; } = "";
}
