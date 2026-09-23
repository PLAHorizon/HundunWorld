using System;

namespace Horizon.Share.Dtos.Flower;

/// <summary>优惠券 API DTO（对应 CouponState，纯 POCO）。</summary>
public class CouponDto
{
    public long Id { get; set; }
    public long ShopId { get; set; }
    public string CouponName { get; set; } = "";
    public int CouponType { get; set; }
    public decimal Denomination { get; set; }
    public decimal UseCondition { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public int TotalCount { get; set; }
    public int ReceivedCount { get; set; }
    public int UsedCount { get; set; }
    public bool IsActive { get; set; }
}

/// <summary>优惠券领取记录 API DTO（对应 CouponRecordState，纯 POCO）。</summary>
public class CouponRecordDto
{
    public long Id { get; set; }
    public long CouponId { get; set; }
    public Guid UserId { get; set; }
    public int Status { get; set; }
    public long? UsedOrderId { get; set; }
    public DateTime ReceivedAt { get; set; }
    public DateTime? UsedAt { get; set; }
}
