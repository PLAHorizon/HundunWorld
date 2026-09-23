using System;

namespace Horizon.Share.Dtos.Flower;

/// <summary>待结算单 API DTO（对应 PendingSettlementState，纯 POCO）。</summary>
public class PendingSettlementDto
{
    public long Id { get; set; }
    public long OrderId { get; set; }
    public long ShopId { get; set; }
    public decimal OrderAmount { get; set; }
    public decimal PlatformCommission { get; set; }
    public decimal RefundAmount { get; set; }
    public decimal SettleableAmount { get; set; }
    public int Status { get; set; }
    public long? SettlementId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? SettledAt { get; set; }
}

/// <summary>提现单 API DTO（对应 ShopWithdrawState，纯 POCO）。</summary>
public class ShopWithdrawDto
{
    public long Id { get; set; }
    public long ShopId { get; set; }
    public decimal Amount { get; set; }
    public string BankName { get; set; } = "";
    public string AccountNo { get; set; } = "";
    public string AccountName { get; set; } = "";
    public int Status { get; set; }
    public string AuditRemark { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime? AuditedAt { get; set; }
    public DateTime? PaidAt { get; set; }
}

/// <summary>店铺账户流水 API DTO（对应 ShopAccountItemState，纯 POCO）。</summary>
public class ShopAccountItemDto
{
    public long Id { get; set; }
    public long ShopId { get; set; }
    public int AccountType { get; set; }
    public decimal Amount { get; set; }
    public decimal BalanceAfter { get; set; }
    public string Description { get; set; } = "";
    public long RelatedId { get; set; }
    public DateTime CreatedAt { get; set; }
}
