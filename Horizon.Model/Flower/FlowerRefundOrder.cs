using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;

namespace Horizon.Model.Flower
{
    [Table("Flower_RefundOrder")]
    [EntityStorage("Flower")]
    public class FlowerRefundOrder : BaseIdentityAggregateRootModel<long>
    {
        [Comment("订单ID")]
        public long OrderId { get; set; }

        [Comment("支付交易ID")]
        public long PaymentTransactionId { get; set; }

        [StringLength(64), Column(TypeName = "varchar(64)")]
        [Comment("退款号")]
        public string RefundNo { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        [Comment("退款金额")]
        public decimal RefundAmount { get; set; }

        [StringLength(256), Column(TypeName = "varchar(256)")]
        [Comment("退款原因")]
        public string Reason { get; set; }

        [Comment("退款状态")]
        public int Status { get; set; }

        [StringLength(64), Column(TypeName = "varchar(64)")]
        [Comment("渠道退款号")]
        public string ChannelRefundNo { get; set; }

        [Comment("退款时间")]
        public DateTime? RefundedAt { get; set; }
    }
}
