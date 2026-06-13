using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;

namespace Horizon.Model.Flower
{
    [Table("Flower_Order")]
    [EntityStorage("Flower")]
    public class FlowerOrder : BaseIdentityAggregateRootModel<long>
    {
        [StringLength(32), Column(TypeName = "varchar(32)")]
        [Comment("订单号")]
        public string OrderNo { get; set; }

        [Comment("买家ID")]
        public Guid BuyerId { get; set; }

        [Comment("商户ID")]
        public long MerchantId { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        [Comment("总金额")]
        public decimal TotalAmount { get; set; }

        [Comment("订单状态")]
        public int Status { get; set; }

        [StringLength(32), Column(TypeName = "varchar(32)")]
        [Comment("支付方式")]
        public string PaymentMethod { get; set; }

        [Comment("支付时间")]
        public DateTime? PaymentTime { get; set; }

        [StringLength(256), Column(TypeName = "varchar(256)")]
        [Comment("收货地址")]
        public string ShippingAddress { get; set; }

        [Comment("是否预售")]
        public bool IsPresale { get; set; }

        [Comment("预售发货日期")]
        public DateTime? PresaleDeliveryDate { get; set; }

        [Comment("关联种植批次ID")]
        public long? RelatedBatchId { get; set; }

        [Comment("预售就绪通知时间")]
        public DateTime? PresaleReadyNotifiedAt { get; set; }

        [StringLength(32), Column(TypeName = "varchar(32)")]
        [Comment("收货人")]
        public string ShipTo { get; set; }

        [StringLength(20), Column(TypeName = "varchar(20)")]
        [Comment("收货手机")]
        public string CellPhone { get; set; }

        [Comment("地区ID")]
        public int? RegionId { get; set; }

        [StringLength(256), Column(TypeName = "varchar(256)")]
        [Comment("详细地址")]
        public string Address { get; set; }

        [StringLength(64), Column(TypeName = "varchar(64)")]
        [Comment("物流公司")]
        public string ExpressCompanyName { get; set; }

        [StringLength(64), Column(TypeName = "varchar(64)")]
        [Comment("物流单号")]
        public string ShipOrderNumber { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        [Comment("运费")]
        public decimal Freight { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        [Comment("商品总金额")]
        public decimal ProductTotalAmount { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        [Comment("订单实付金额")]
        public decimal OrderTotalAmount { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        [Comment("优惠金额")]
        public decimal DiscountAmount { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        [Comment("满减优惠")]
        public decimal FullDiscount { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        [Comment("积分抵扣")]
        public decimal IntegralDiscount { get; set; }

        [StringLength(128), Column(TypeName = "varchar(128)")]
        [Comment("发票抬头")]
        public string InvoiceTitle { get; set; }

        [StringLength(64), Column(TypeName = "varchar(64)")]
        [Comment("发票税号")]
        public string InvoiceCode { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        [Comment("税费")]
        public decimal Tax { get; set; }

        [Comment("退款状态: 0=无, 1=退款中, 2=已退款")]
        public int RefundStatus { get; set; }

        [StringLength(256), Column(TypeName = "varchar(256)")]
        [Comment("卖家备注")]
        public string SellerRemark { get; set; }

        [Comment("发货时间")]
        public DateTime? ShippingDate { get; set; }

        [Comment("收货时间")]
        public DateTime? CompletionTime { get; set; }

        [Comment("确认收货时间")]
        public DateTime? DeliveredAt { get; set; }

        [Comment("下单平台: 0=PC, 1=移动, 2=小程序")]
        public int Platform { get; set; }

        [StringLength(32), Column(TypeName = "varchar(32)")]
        [Comment("发货人姓名")]
        public string SenderName { get; set; }

        [StringLength(20), Column(TypeName = "varchar(20)")]
        [Comment("发货人电话")]
        public string SenderPhone { get; set; }

        [StringLength(256), Column(TypeName = "varchar(256)")]
        [Comment("发货人地址")]
        public string SenderAddress { get; set; }
    }
}
