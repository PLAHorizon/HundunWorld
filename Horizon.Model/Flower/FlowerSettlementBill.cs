using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;

namespace Horizon.Model.Flower
{
    [Table("Flower_SettlementBill")]
    [EntityStorage("Flower")]
    public class FlowerSettlementBill : BaseIdentityAggregateRootModel<long>
    {
        [Comment("商户ID")]
        public long MerchantId { get; set; }

        [Comment("结算周期开始")]
        public DateTime PeriodStart { get; set; }

        [Comment("结算周期结束")]
        public DateTime PeriodEnd { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        [Comment("总金额")]
        public decimal TotalAmount { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        [Comment("平台手续费")]
        public decimal PlatformFee { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        [Comment("结算金额")]
        public decimal SettledAmount { get; set; }

        [Comment("状态")]
        public int Status { get; set; }

        [Comment("结算时间")]
        public DateTime? SettledAt { get; set; }
    }
}
