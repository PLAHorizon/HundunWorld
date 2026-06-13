using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;

namespace Horizon.Model.Flower
{
    /// <summary>
    /// 价格预测
    /// </summary>
    [Table("Flower_PricePrediction")]
    [EntityStorage("Flower")]
    public class FlowerPricePrediction : BaseIdentityModel<long>
    {
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
        /// 模型ID
        /// </summary>
        [Comment("模型ID")]
        public long ModelId { get; set; }

        /// <summary>
        /// 预测日期
        /// </summary>
        [Comment("预测日期")]
        public DateTime PredictDate { get; set; }

        /// <summary>
        /// 预测价格
        /// </summary>
        [Column(TypeName = "decimal(18,4)")]
        [Comment("预测价格")]
        public decimal PredictedPrice { get; set; }

        /// <summary>
        /// 预测下界
        /// </summary>
        [Column(TypeName = "decimal(18,4)")]
        [Comment("预测下界")]
        public decimal LowerBound { get; set; }

        /// <summary>
        /// 预测上界
        /// </summary>
        [Column(TypeName = "decimal(18,4)")]
        [Comment("预测上界")]
        public decimal UpperBound { get; set; }

        /// <summary>
        /// 置信度
        /// </summary>
        [Comment("置信度")]
        public double Confidence { get; set; }

        /// <summary>
        /// 时间尺度
        /// </summary>
        [Comment("时间尺度")]
        public int TimeScale { get; set; }

        /// <summary>
        /// 创建时间
        /// </summary>
        [Comment("创建时间")]
        public DateTime CreatedAt { get; set; }
    }
}
