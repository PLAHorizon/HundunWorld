using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;

namespace Horizon.Model.Flower
{
    /// <summary>
    /// 预警日志
    /// </summary>
    [Table("Flower_AlertLog")]
    [EntityStorage("Flower")]
    public class FlowerAlertLog : BaseIdentityModel<long>
    {
        /// <summary>
        /// 规则ID
        /// </summary>
        [Comment("规则ID")]
        public long RuleId { get; set; }

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
        /// 预警类型
        /// </summary>
        [Comment("预警类型")]
        public int AlertType { get; set; }

        /// <summary>
        /// 预警消息
        /// </summary>
        [StringLength(512), Column(TypeName = "varchar(512)")]
        [Comment("预警消息")]
        public string AlertMessage { get; set; }

        /// <summary>
        /// 触发值
        /// </summary>
        [Column(TypeName = "decimal(18,4)")]
        [Comment("触发值")]
        public decimal TriggeredValue { get; set; }

        /// <summary>
        /// 阈值
        /// </summary>
        [Column(TypeName = "decimal(18,4)")]
        [Comment("阈值")]
        public decimal ThresholdValue { get; set; }

        /// <summary>
        /// 是否已读
        /// </summary>
        [Comment("是否已读")]
        public bool IsRead { get; set; }

        /// <summary>
        /// 创建时间
        /// </summary>
        [Comment("创建时间")]
        public DateTime CreatedAt { get; set; }
    }
}
