using System;
using System.Collections.Generic;

namespace Horizon.Share.Dtos.Flower;

/// <summary>
/// 订单 API DTO（对应 OrderState，纯 POCO）。枚举 Status 以 int 承载（对应 GrainEnums.OrderStatus 数值），
/// 嵌套 Items 用 <see cref="OrderItemDto"/>，使 HTTP 契约与 grain/线格式解耦。
/// </summary>
public class OrderDto
{
    public long OrderId { get; set; }
    public string OrderNo { get; set; } = "";
    public Guid BuyerId { get; set; }
    public long MerchantId { get; set; }
    public decimal TotalAmount { get; set; }
    /// <summary>订单状态（int，对应 OrderStatus 枚举数值）。</summary>
    public int Status { get; set; }
    public string PaymentMethod { get; set; } = "";
    public DateTime? PaymentTime { get; set; }
    public string ShippingAddress { get; set; } = "";
    public bool IsPresale { get; set; }
    public DateTime? PresaleDeliveryDate { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();
    public string ShipTo { get; set; } = "";
    public string CellPhone { get; set; } = "";
    public string ExpressCompanyName { get; set; } = "";
    public string ShipOrderNumber { get; set; } = "";
    public decimal Freight { get; set; }
    public decimal OrderTotalAmount { get; set; }
    public int RefundStatus { get; set; }
    public string SellerRemark { get; set; } = "";
    public decimal DiscountAmount { get; set; }
    public decimal FullDiscount { get; set; }
    public string Address { get; set; } = "";
    public string Platform { get; set; } = "";
    public decimal ProductTotalAmount { get; set; }
    public long? RelatedBatchId { get; set; }
    public DateTime? PresaleReadyNotifiedAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public string SenderName { get; set; } = "";
    public string SenderPhone { get; set; } = "";
    public string SenderAddress { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}
