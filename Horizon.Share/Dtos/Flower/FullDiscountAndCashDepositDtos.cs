using System;

namespace Horizon.Share.Dtos.Flower;

/// <summary>满减规则 API DTO（对应 FullDiscountRuleState，纯 POCO）。</summary>
public class FullDiscountRuleDto
{
    public long Id { get; set; }
    public long ShopId { get; set; }
    public string RuleName { get; set; } = "";
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public decimal LimitValue { get; set; }
    public decimal DiscountValue { get; set; }
    public bool IsActive { get; set; }
}

/// <summary>保证金 API DTO（对应 CashDepositState，纯 POCO）。</summary>
public class CashDepositDto
{
    public long Id { get; set; }
    public long ShopId { get; set; }
    public long CategoryId { get; set; }
    public decimal Amount { get; set; }
    public int Status { get; set; }
    public DateTime? PaidAt { get; set; }
    public DateTime? DeductedAt { get; set; }
    public bool NoReasonReturn { get; set; }
}

/// <summary>经营类目 API DTO（对应 BusinessCategoryState，纯 POCO）。</summary>
public class BusinessCategoryDto
{
    public long Id { get; set; }
    public long ShopId { get; set; }
    public long CategoryId { get; set; }
    public decimal CommissionRate { get; set; }
    public int AuditStatus { get; set; }
    public string AuditRemark { get; set; } = "";
}
