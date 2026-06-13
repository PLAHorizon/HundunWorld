using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;

namespace Horizon.Model.Flower
{
    [Table("Flower_PlantingAdvice")]
    [EntityStorage("Flower")]
    public class FlowerPlantingAdvice : BaseIdentityAggregateRootModel<long>
    {
        [Comment("关联批次ID")]
        public long BatchId { get; set; }

        [StringLength(64), Column(TypeName = "varchar(64)")]
        [Comment("建议类型(Irrigation/Ventilation/Pest/Harvest/General)")]
        public string AdviceType { get; set; }

        [StringLength(256)]
        [Comment("建议标题")]
        public string Title { get; set; }

        [StringLength(2000)]
        [Comment("建议内容")]
        public string Content { get; set; }

        [StringLength(64), Column(TypeName = "varchar(64)")]
        [Comment("数据来源")]
        public string Source { get; set; }

        [StringLength(16), Column(TypeName = "varchar(16)")]
        [Comment("优先级(High/Normal/Low)")]
        public string Priority { get; set; }

        [StringLength(16), Column(TypeName = "varchar(16)")]
        [Comment("状态(Pending/Executed/Ignored)")]
        public string Status { get; set; }

        [Comment("生成时间")]
        public DateTime GeneratedTime { get; set; }

        [Comment("执行时间")]
        public DateTime? ExecutedTime { get; set; }

        [Comment("是否软删除")]
        public bool IsDeleted { get; set; }
    }
}
