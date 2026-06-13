using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;

namespace Horizon.Model.Flower
{
    /// <summary>
    /// 数据分析池
    /// </summary>
    [Table("Flower_DataPool")]
    [EntityStorage("Flower")]
    public class FlowerDataPool : BaseIdentityAggregateRootModel<long>
    {
        /// <summary>
        /// 数据类型
        /// </summary>
        [Comment("数据类型")]
        public int DataType { get; set; }

        /// <summary>
        /// 数据来源
        /// </summary>
        [Comment("数据来源")]
        public int DataSource { get; set; }

        /// <summary>
        /// 原始数据
        /// </summary>
        [Column(TypeName = "nvarchar(max)")]
        [Comment("原始数据")]
        public string RawPayload { get; set; }

        /// <summary>
        /// 时间戳
        /// </summary>
        [Comment("时间戳")]
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// 关联实体ID
        /// </summary>
        [StringLength(128), Column(TypeName = "varchar(128)")]
        [Comment("关联实体ID")]
        public string RelatedEntityId { get; set; }

        /// <summary>
        /// 模型版本
        /// </summary>
        [StringLength(32), Column(TypeName = "varchar(32)")]
        [Comment("模型版本")]
        public string ModelVersion { get; set; }

        /// <summary>
        /// 置信度
        /// </summary>
        [Comment("置信度")]
        public double? Confidence { get; set; }
    }
}
