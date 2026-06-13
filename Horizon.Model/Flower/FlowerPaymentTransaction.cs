using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;

namespace Horizon.Model.Flower
{
    [Table("Flower_PaymentTransaction")]
    [EntityStorage("Flower")]
    public class FlowerPaymentTransaction : BaseIdentityAggregateRootModel<long>
    {
        [Comment("订单ID")]
        public long OrderId { get; set; }

        [StringLength(64), Column(TypeName = "varchar(64)")]
        [Comment("交易号")]
        public string TransactionNo { get; set; }

        [Comment("支付渠道")]
        public int Channel { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        [Comment("金额")]
        public decimal Amount { get; set; }

        [Comment("状态")]
        public int Status { get; set; }

        [StringLength(64), Column(TypeName = "varchar(64)")]
        [Comment("预支付ID")]
        public string PrepayId { get; set; } = "";

        [StringLength(64), Column(TypeName = "varchar(64)")]
        [Comment("渠道交易号")]
        public string ChannelTransactionNo { get; set; } = "";

        [Comment("支付时间")]
        public DateTime? PaidAt { get; set; }

        [Comment("过期时间")]
        public DateTime? ExpiredAt { get; set; }
    }
}
