using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;

namespace Horizon.Model.Flower
{
    [Table("Flower_OrderRefund")]
    [EntityStorage("Flower")]
    public class FlowerOrderRefund : BaseIdentityAggregateRootModel<long>
    {
        [Comment("订单ID")]
        public long OrderId { get; set; }

        [Comment("订单明细ID")]
        public long OrderItemId { get; set; }

        [StringLength(32), Column(TypeName = "varchar(32)")]
        [Comment("退款号")]
        public string RefundNo { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        [Comment("退款金额")]
        public decimal RefundAmount { get; set; }

        [StringLength(512), Column(TypeName = "varchar(512)")]
        [Comment("退款原因")]
        public string Reason { get; set; }

        [Comment("退款状态: 0=待审核, 1=商户同意, 2=商户拒绝, 3=退款中, 4=退款完成, 5=退款关闭")]
        public int Status { get; set; }

        [Comment("退款类型: 0=仅退款, 1=退货退款")]
        public int RefundMode { get; set; }

        [StringLength(128), Column(TypeName = "varchar(128)")]
        [Comment("商户审核备注")]
        public string SellerAuditRemark { get; set; }

        [Comment("商户审核时间")]
        public DateTime? SellerAuditTime { get; set; }

        [StringLength(128), Column(TypeName = "varchar(128)")]
        [Comment("平台处理备注")]
        public string PlatformRemark { get; set; }

        [Comment("平台处理时间")]
        public DateTime? PlatformAuditTime { get; set; }

        [Comment("买家ID")]
        public Guid BuyerId { get; set; }

        [Comment("商户ID")]
        public long MerchantId { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        [Comment("可退金额")]
        public decimal EnabledRefundAmount { get; set; }

        [Comment("退货数量")]
        public int ReturnQuantity { get; set; }

        [Comment("退货物流ID")]
        public long? ReturnShipmentId { get; set; }

        [Comment("买家退货截止时间")]
        public DateTime? ReturnDeadline { get; set; }

        [Comment("商户确认收货截止时间")]
        public DateTime? SellerConfirmDeadline { get; set; }

        [StringLength(512), Column(TypeName = "varchar(512)")]
        [Comment("退货地址JSON")]
        public string ReturnAddress { get; set; }
    }
}
