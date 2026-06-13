using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;

namespace Horizon.Model.Flower
{
    /// <summary>
    /// 预测模型
    /// </summary>
    [Table("Flower_PredictionModel")]
    [EntityStorage("Flower")]
    public class FlowerPredictionModel : BaseIdentityAggregateRootModel<long>, ISoftDeleted
    {
        /// <summary>
        /// 品类ID
        /// </summary>
        [Comment("品类ID")]
        public long SpeciesId { get; set; }

        /// <summary>
        /// 模型类型
        /// </summary>
        [StringLength(32), Column(TypeName = "varchar(32)")]
        [Comment("模型类型")]
        public string ModelType { get; set; }

        /// <summary>
        /// 模型版本
        /// </summary>
        [StringLength(32), Column(TypeName = "varchar(32)")]
        [Comment("模型版本")]
        public string ModelVersion { get; set; }

        /// <summary>
        /// 模型参数JSON
        /// </summary>
        [Column(TypeName = "nvarchar(max)")]
        [Comment("模型参数JSON")]
        public string ModelParams { get; set; }

        /// <summary>
        /// 训练数据范围
        /// </summary>
        [StringLength(128), Column(TypeName = "varchar(128)")]
        [Comment("训练数据范围")]
        public string TrainingDataRange { get; set; }

        /// <summary>
        /// 准确度
        /// </summary>
        [Comment("准确度")]
        public double Accuracy { get; set; }

        /// <summary>
        /// 是否启用
        /// </summary>
        [Comment("是否启用")]
        public bool IsActive { get; set; }

        /// <summary>
        /// 是否已删除，true : 已删除，false : 未删除
        /// </summary>
        [Comment("是否已删除，true : 已删除，false : 未删除")]
        public bool IsDeleted { get; set; }
    }
}
