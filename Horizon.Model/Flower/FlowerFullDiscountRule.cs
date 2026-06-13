using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;

namespace Horizon.Model.Flower
{
    [Table("Flower_FullDiscountRule")]
    [EntityStorage("Flower")]
    public class FlowerFullDiscountRule : BaseIdentityAggregateRootModel<long>, ISoftDeleted
    {
        [Comment("店铺ID")]
        public long ShopId { get; set; }

        [StringLength(64), Column(TypeName = "varchar(64)")]
        [Comment("规则名称")]
        public string RuleName { get; set; }

        [Comment("开始日期")]
        public DateTime StartDate { get; set; }

        [Comment("结束日期")]
        public DateTime EndDate { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        [Comment("满X元")]
        public decimal LimitValue { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        [Comment("减Y元")]
        public decimal DiscountValue { get; set; }

        [Comment("是否启用")]
        public bool IsActive { get; set; }

        public bool IsDeleted { get; set; }
    }
}
