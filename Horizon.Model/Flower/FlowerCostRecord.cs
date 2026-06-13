using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;

namespace Horizon.Model.Flower
{
    [Table("Flower_CostRecord")]
    [EntityStorage("Flower")]
    public class FlowerCostRecord : BaseIdentityAggregateRootModel<long>
    {
        [Comment("关联批次ID")]
        public long BatchId { get; set; }

        [StringLength(32), Column(TypeName = "varchar(32)")]
        [Comment("成本分类(Seedling/Fertilizer/Pesticide/Labor/Utility/Depreciation/Other)")]
        public string Category { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        [Comment("金额")]
        public decimal Amount { get; set; }

        [Comment("日期")]
        public DateTime CostDate { get; set; }

        [StringLength(256)]
        [Comment("备注")]
        public string Remark { get; set; }

        [Comment("是否软删除")]
        public bool IsDeleted { get; set; }
    }
}
