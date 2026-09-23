using System;

namespace Horizon.Share.Dtos.Flower;

/// <summary>发货人 API DTO（对应 ShopShipperState，纯 POCO）。</summary>
public class ShopShipperDto
{
    public long Id { get; set; }
    public long ShopId { get; set; }
    public string ShipperTag { get; set; } = "";
    public string ShipperName { get; set; } = "";
    public int RegionId { get; set; }
    public string Address { get; set; } = "";
    public string TelPhone { get; set; } = "";
    public bool IsDefaultSendGoods { get; set; }
    public float? Longitude { get; set; }
    public float? Latitude { get; set; }
}

/// <summary>品牌申请 API DTO（对应 ShopBrandApplyState，纯 POCO）。</summary>
public class ShopBrandApplyDto
{
    public long Id { get; set; }
    public long ShopId { get; set; }
    public string BrandName { get; set; } = "";
    public string ProofMaterial { get; set; } = "";
    public int AuditStatus { get; set; }
    public string AuditRemark { get; set; } = "";
}

/// <summary>复购记录 API DTO（对应 RepurchaseState，纯 POCO）。</summary>
public class RepurchaseDto
{
    public long Id { get; set; }
    public Guid BuyerId { get; set; }
    public long OriginalOrderId { get; set; }
    public long? NewOrderId { get; set; }
    public DateTime RepurchaseTime { get; set; }
}
