using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;

namespace Horizon.Model.Flower
{
    [Table("Flower_PendingSettlement")]
    [EntityStorage("Flower")]
    public class FlowerPendingSettlement : BaseIdentityAggregateRootModel<long>
    {
        [Comment("订单ID")]
        public long OrderId { get; set; }

        [Comment("店铺ID")]
        public long ShopId { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        [Comment("订单金额")]
        public decimal OrderAmount { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        [Comment("平台佣金")]
        public decimal PlatformCommission { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        [Comment("退款金额")]
        public decimal RefundAmount { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        [Comment("可结算金额")]
        public decimal SettleableAmount { get; set; }

        [Comment("状态0=待结算1=已结算")]
        public int Status { get; set; }

        [Comment("结算单ID")]
        public long? SettlementId { get; set; }

        [Comment("创建时间")]
        public DateTime CreatedAt { get; set; }

        [Comment("结算时间")]
        public DateTime? SettledAt { get; set; }

        [Comment("退款是否已扣减")]
        public bool RefundDeducted { get; set; }
    }
}
