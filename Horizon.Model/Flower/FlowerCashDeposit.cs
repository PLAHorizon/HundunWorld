using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;

namespace Horizon.Model.Flower
{
    [Table("Flower_CashDeposit")]
    [EntityStorage("Flower")]
    public class FlowerCashDeposit : BaseIdentityAggregateRootModel<long>
    {
        [Comment("店铺ID")]
        public long ShopId { get; set; }

        [Comment("类目ID")]
        public long CategoryId { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        [Comment("保证金金额")]
        public decimal Amount { get; set; }

        [Comment("状态0=待缴纳1=已缴纳2=已扣罚3=已退还")]
        public int Status { get; set; }

        [Comment("缴纳时间")]
        public DateTime? PaidAt { get; set; }

        [Comment("扣罚时间")]
        public DateTime? DeductedAt { get; set; }

        [Comment("七天无理由退换标识")]
        public bool NoReasonReturn { get; set; }
    }
}
