using System;

namespace Horizon.Share.Dtos.Flower;

/// <summary>退款单 API DTO（对应 OrderRefundState，纯 POCO）。</summary>
public class OrderRefundDto
{
    public long Id { get; set; }
    public long OrderId { get; set; }
    public long OrderItemId { get; set; }
    public string RefundNo { get; set; } = "";
    public decimal RefundAmount { get; set; }
    public string Reason { get; set; } = "";
    public int Status { get; set; }
    public int RefundMode { get; set; }
    public string SellerAuditRemark { get; set; } = "";
    public DateTime? SellerAuditTime { get; set; }
    public string PlatformRemark { get; set; } = "";
    public DateTime? PlatformAuditTime { get; set; }
    public Guid BuyerId { get; set; }
    public long MerchantId { get; set; }
    public decimal EnabledRefundAmount { get; set; }
    public int ReturnQuantity { get; set; }
}
