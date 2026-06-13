using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;

namespace Horizon.Model.Flower
{
    /// <summary>
    /// 行情快照分区表
    /// </summary>
    [Table("Flower_MarketSnapshot")]
    [EntityStorage("Flower")]
    public class FlowerMarketSnapshot : BaseIdentityModel<long>
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
        /// 均价
        /// </summary>
        [Column(TypeName = "decimal(18,4)")]
        [Comment("均价")]
        public decimal AvgPrice { get; set; }

        /// <summary>
        /// 最低价
        /// </summary>
        [Column(TypeName = "decimal(18,4)")]
        [Comment("最低价")]
        public decimal MinPrice { get; set; }

        /// <summary>
        /// 最高价
        /// </summary>
        [Column(TypeName = "decimal(18,4)")]
        [Comment("最高价")]
        public decimal MaxPrice { get; set; }

        /// <summary>
        /// 成交量
        /// </summary>
        [Comment("成交量")]
        public int Volume { get; set; }

        /// <summary>
        /// 成交笔数
        /// </summary>
        [Comment("成交笔数")]
        public int TradeCount { get; set; }

        /// <summary>
        /// 快照时间
        /// </summary>
        [Comment("快照时间")]
        public DateTime SnapshotTime { get; set; }

        /// <summary>
        /// 数据来源
        /// </summary>
        [Comment("数据来源")]
        public int DataSource { get; set; }
    }
}
