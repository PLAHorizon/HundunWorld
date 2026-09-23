using System;

namespace Horizon.Share.Dtos.Flower;

/// <summary>支付单 API DTO（对应 PaymentState，纯 POCO）。Channel 以 int 承载（PaymentChannel 枚举数值）。</summary>
public class PaymentDto
{
    public long TransactionId { get; set; }
    public long OrderId { get; set; }
    public string TransactionNo { get; set; } = "";
    /// <summary>支付渠道（int，对应 PaymentChannel 枚举数值）。</summary>
    public int Channel { get; set; }
    public decimal Amount { get; set; }
    public int Status { get; set; }
    public string PrepayId { get; set; } = "";
    public string ChannelTransactionNo { get; set; } = "";
    public DateTime? PaidAt { get; set; }
    public DateTime? ExpiredAt { get; set; }
    public string IdempotencyKey { get; set; } = "";
    public Guid BuyerId { get; set; }
    public string CallbackLockKey { get; set; } = "";
    public DateTime? CallbackLockedAt { get; set; }
    public bool NeedsOrderSync { get; set; }
    public decimal TotalRefundedAmount { get; set; }
    /// <summary>客户端拉起支付参数（JSON：微信=appId/partnerId/prepayId/nonceStr/timeStamp/package/sign；支付宝=orderInfo）。仅预下单窗口内有效。</summary>
    public string PayParams { get; set; } = "";
}
