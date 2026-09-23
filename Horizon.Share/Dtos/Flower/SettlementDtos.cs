using System;

namespace Horizon.Share.Dtos.Flower;

/// <summary>结算单 API DTO（对应 SettlementState，纯 POCO）。</summary>
public class SettlementDto
{
    public long Id { get; set; }
    public long MerchantId { get; set; }
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal PlatformFee { get; set; }
    public decimal SettledAmount { get; set; }
    public int Status { get; set; }
    public DateTime? SettledAt { get; set; }
}

/// <summary>结算配置 API DTO（对应 SettledConfigState，纯 POCO）。</summary>
public class SettledConfigDto
{
    public long Id { get; set; }
    public int BusinessType { get; set; }
    public int SettlementAccountType { get; set; }
    public int TrialDays { get; set; }
    public bool IsCity { get; set; }
    public bool IsPeopleNumber { get; set; }
    public bool IsAddress { get; set; }
    public bool IsBusinessLicenseCode { get; set; }
    public bool IsBusinessScope { get; set; }
    public bool IsBusinessLicense { get; set; }
}

/// <summary>交易评价 API DTO（对应 TradeCommentState，纯 POCO）。</summary>
public class TradeCommentDto
{
    public long Id { get; set; }
    public long OrderId { get; set; }
    public Guid UserId { get; set; }
    public long ShopId { get; set; }
    public int DescriptionScore { get; set; }
    public int ServiceScore { get; set; }
    public int LogisticsScore { get; set; }
    public string Content { get; set; } = "";
    public bool IsAnonymous { get; set; }
}
