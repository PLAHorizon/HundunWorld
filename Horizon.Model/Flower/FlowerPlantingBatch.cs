using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;

namespace Horizon.Model.Flower
{
    [Table("Flower_PlantingBatch")]
    [EntityStorage("Flower")]
    public class FlowerPlantingBatch : BaseIdentityAggregateRootModel<long>
    {
        [StringLength(128)]
        [Comment("批次名称")]
        public string BatchName { get; set; }

        [StringLength(64), Column(TypeName = "varchar(64)")]
        [Comment("品种ID")]
        public string SpeciesId { get; set; }

        [StringLength(128)]
        [Comment("品种名称")]
        public string SpeciesName { get; set; }

        [StringLength(64), Column(TypeName = "varchar(64)")]
        [Comment("温室ID")]
        public string GreenhouseId { get; set; }

        [Comment("种植日期")]
        public DateTime PlantingDate { get; set; }

        [Comment("预计采收日期")]
        public DateTime? ExpectedHarvestDate { get; set; }

        [Comment("实际采收日期")]
        public DateTime? ActualHarvestDate { get; set; }

        [StringLength(32), Column(TypeName = "varchar(32)")]
        [Comment("批次状态(Planted/Growing/Harvesting/Completed/Abandoned)")]
        public string Status { get; set; }

        [Comment("种植数量")]
        public int PlantingQuantity { get; set; }

        [StringLength(256)]
        [Comment("备注")]
        public string Remark { get; set; }

        [Comment("用户ID")]
        public Guid UserId { get; set; }

        [Comment("是否软删除")]
        public bool IsDeleted { get; set; }
    }
}
