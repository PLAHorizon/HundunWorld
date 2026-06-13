using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;

namespace Horizon.Model.Flower
{
    /// <summary>
    /// 预警规则
    /// </summary>
    [Table("Flower_AlertRule")]
    [EntityStorage("Flower")]
    public class FlowerAlertRule : BaseIdentityAggregateRootModel<long>, ISoftDeleted
    {
        /// <summary>
        /// 用户ID
        /// </summary>
        [Comment("用户ID")]
        public Guid UserId { get; set; }

        /// <summary>
        /// 品类ID
        /// </summary>
        [Comment("品类ID")]
        public long SpeciesId { get; set; }

        /// <summary>
        /// 市场ID
        /// </summary>
        [Comment("市场ID")]
        public long MarketId { get; set; }

        /// <summary>
        /// 条件类型
        /// </summary>
        [Comment("条件类型")]
        public int ConditionType { get; set; }

        /// <summary>
        /// 阈值
        /// </summary>
        [Column(TypeName = "decimal(18,4)")]
        [Comment("阈值")]
        public decimal ThresholdValue { get; set; }

        /// <summary>
        /// 是否启用
        /// </summary>
        [Comment("是否启用")]
        public bool IsEnabled { get; set; }

        /// <summary>
        /// 上次触发时间
        /// </summary>
        [Comment("上次触发时间")]
        public DateTime? LastTriggeredAt { get; set; }

        /// <summary>
        /// 是否已删除，true : 已删除，false : 未删除
        /// </summary>
        [Comment("是否已删除，true : 已删除，false : 未删除")]
        public bool IsDeleted { get; set; }
    }
}
