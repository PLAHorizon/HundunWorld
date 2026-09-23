using System;

namespace Horizon.Share.Dtos.Flower;

/// <summary>
/// 商品 API DTO（HTTP 响应用）。
/// <para>
/// 与 grain 状态类 <c>ProductState</c>（Horizon.Game.Message.Network）字段一一对应，但为纯 POCO：
/// 不含 MemoryPack/Orleans 序列化属性，使 HTTP 契约与持久化/线格式解耦——grain 状态演进
/// 不再必然改变 API 契约，也不向客户端泄露内部序列化元数据。
/// </para>
/// </summary>
public class ProductDto
{
    public long ProductId { get; set; }
    public long MerchantId { get; set; }
    public int SpeciesId { get; set; }
    public string ProductName { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal Price { get; set; }
    public int Stock { get; set; }
    public string Unit { get; set; } = "";
    public string Images { get; set; } = "";
    public bool IsActive { get; set; }
    public int Version { get; set; }
    public long? CategoryId { get; set; }
    public long? TypeId { get; set; }
    public long? BrandId { get; set; }
    public long? FreightTemplateId { get; set; }
    public decimal? Weight { get; set; }
    public decimal? Volume { get; set; }
    public int MaxBuyCount { get; set; }
    public bool IsOpenLadder { get; set; }
    public int ProductType { get; set; }
    public decimal? MarketPrice { get; set; }
    public decimal MinSalePrice { get; set; }
    public int AuditStatus { get; set; }
    public bool IsPresale { get; set; }
    public DateTime? PresaleDeliveryDate { get; set; }
    public long? RelatedBatchId { get; set; }
}
