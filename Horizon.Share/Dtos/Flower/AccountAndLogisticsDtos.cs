using System;

namespace Horizon.Share.Dtos.Flower;

/// <summary>结算账户 API DTO（对应 SettlementAccountState，纯 POCO）。</summary>
public class SettlementAccountDto
{
    public long Id { get; set; }
    public long MerchantId { get; set; }
    public string BankName { get; set; } = "";
    public string AccountNo { get; set; } = "";
    public string AccountName { get; set; } = "";
    public bool IsDefault { get; set; }
}

/// <summary>物流轨迹 API DTO（对应 LogisticsTrackState，纯 POCO）。</summary>
public class LogisticsTrackDto
{
    public long Id { get; set; }
    public long OrderId { get; set; }
    public string ExpressCompanyName { get; set; } = "";
    public string ShipOrderNumber { get; set; } = "";
    public string TrackData { get; set; } = "";
    public DateTime? LastQueriedAt { get; set; }
    public int LogisticsStatus { get; set; }
    public bool IsReturn { get; set; }
    public long? RefundId { get; set; }
    public string OriginCity { get; set; } = "";
    public string DestinationCity { get; set; } = "";
    public string CurrentLocation { get; set; } = "";
}

/// <summary>结算明细 API DTO（对应 SettlementDetailState，纯 POCO）。</summary>
public class SettlementDetailDto
{
    public long Id { get; set; }
    public long SettlementBillId { get; set; }
    public long OrderId { get; set; }
    public string OrderNo { get; set; } = "";
    public decimal OrderAmount { get; set; }
    public decimal PlatformCommission { get; set; }
    public decimal RefundAmount { get; set; }
    public decimal SettleableAmount { get; set; }
}
