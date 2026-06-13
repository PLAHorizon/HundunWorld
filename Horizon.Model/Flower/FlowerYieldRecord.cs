using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;

namespace Horizon.Model.Flower
{
    [Table("Flower_YieldRecord")]
    [EntityStorage("Flower")]
    public class FlowerYieldRecord : BaseIdentityAggregateRootModel<long>
    {
        [Comment("关联批次ID")]
        public long BatchId { get; set; }

        [StringLength(64), Column(TypeName = "varchar(64)")]
        [Comment("品种ID")]
        public string SpeciesId { get; set; }

        [StringLength(128)]
        [Comment("品种名称")]
        public string SpeciesName { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        [Comment("采收数量")]
        public decimal Quantity { get; set; }

        [StringLength(16), Column(TypeName = "varchar(16)")]
        [Comment("数量单位(Stems/Kg)")]
        public string Unit { get; set; }

        [StringLength(8), Column(TypeName = "varchar(8)")]
        [Comment("等级(A/B/C)")]
        public string Grade { get; set; }

        [Comment("采收日期")]
        public DateTime HarvestDate { get; set; }

        [StringLength(256)]
        [Comment("备注")]
        public string Remark { get; set; }

        [Comment("是否软删除")]
        public bool IsDeleted { get; set; }
    }
}
