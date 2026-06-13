using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;

namespace Horizon.Model.Flower
{
    [Table("Flower_SettlementDetail")]
    [EntityStorage("Flower")]
    public class FlowerSettlementDetail : BaseIdentityModel<long>
    {
        [Comment("结算账单ID")]
        public long SettlementBillId { get; set; }

        [Comment("订单ID")]
        public long OrderId { get; set; }

        [StringLength(32), Column(TypeName = "varchar(32)")]
        [Comment("订单号")]
        public string OrderNo { get; set; }

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
    }
}
