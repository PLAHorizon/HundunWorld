using System;

namespace Horizon.Share.Dtos.Flower;

/// <summary>退货物流 API DTO（对应 ReturnShipmentState，纯 POCO）。</summary>
public class ReturnShipmentDto
{
    public long Id { get; set; }
    public long RefundId { get; set; }
    public string ExpressCompanyName { get; set; } = "";
    public string ShipOrderNumber { get; set; } = "";
    public string ReturnAddress { get; set; } = "";
    public DateTime? ShippedAt { get; set; }
    public DateTime? ReceivedAt { get; set; }
    public int Status { get; set; }
}

/// <summary>结算账户汇总 API DTO（对应 SettlementAccountSummaryState，纯 POCO）。</summary>
public class SettlementAccountSummaryDto
{
    public long MerchantId { get; set; }
    public decimal TotalSettled { get; set; }
    public decimal TotalWithdrawn { get; set; }
    public decimal AvailableBalance { get; set; }
    public decimal PendingSettlement { get; set; }
    public decimal FrozenAmount { get; set; }
}

/// <summary>商品阶梯价 API DTO（对应 ProductLadderPriceState，纯 POCO）。</summary>
public class ProductLadderPriceDto
{
    public long Id { get; set; }
    public long ProductId { get; set; }
    public int MinBatch { get; set; }
    public int MaxBatch { get; set; }
    public decimal Price { get; set; }
}
