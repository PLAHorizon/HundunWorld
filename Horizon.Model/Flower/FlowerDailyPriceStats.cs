using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;

namespace Horizon.Model.Flower
{
    /// <summary>
    /// 日统计
    /// </summary>
    [Table("Flower_DailyPriceStats")]
    [EntityStorage("Flower")]
    public class FlowerDailyPriceStats : BaseIdentityModel<long>
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
        /// 统计日期
        /// </summary>
        [Comment("统计日期")]
        public DateTime StatDate { get; set; }

        /// <summary>
        /// 开盘价
        /// </summary>
        [Column(TypeName = "decimal(18,4)")]
        [Comment("开盘价")]
        public decimal OpenPrice { get; set; }

        /// <summary>
        /// 收盘价
        /// </summary>
        [Column(TypeName = "decimal(18,4)")]
        [Comment("收盘价")]
        public decimal ClosePrice { get; set; }

        /// <summary>
        /// 最高价
        /// </summary>
        [Column(TypeName = "decimal(18,4)")]
        [Comment("最高价")]
        public decimal HighPrice { get; set; }

        /// <summary>
        /// 最低价
        /// </summary>
        [Column(TypeName = "decimal(18,4)")]
        [Comment("最低价")]
        public decimal LowPrice { get; set; }

        /// <summary>
        /// 均价
        /// </summary>
        [Column(TypeName = "decimal(18,4)")]
        [Comment("均价")]
        public decimal AvgPrice { get; set; }

        /// <summary>
        /// 总成交量
        /// </summary>
        [Comment("总成交量")]
        public int TotalVolume { get; set; }

        /// <summary>
        /// 总成交笔数
        /// </summary>
        [Comment("总成交笔数")]
        public int TotalTradeCount { get; set; }

        /// <summary>
        /// 涨跌额
        /// </summary>
        [Column(TypeName = "decimal(18,4)")]
        [Comment("涨跌额")]
        public decimal PriceChange { get; set; }

        /// <summary>
        /// 涨跌幅
        /// </summary>
        [Column(TypeName = "decimal(18,4)")]
        [Comment("涨跌幅")]
        public decimal PriceChangePercent { get; set; }

        /// <summary>
        /// 最低成交价
        /// </summary>
        [Column(TypeName = "decimal(18,4)")]
        [Comment("最低成交价")]
        public decimal MinPrice { get; set; }

        /// <summary>
        /// 最高成交价
        /// </summary>
        [Column(TypeName = "decimal(18,4)")]
        [Comment("最高成交价")]
        public decimal MaxPrice { get; set; }

        /// <summary>
        /// 价格标准差
        /// </summary>
        [Column(TypeName = "decimal(18,4)")]
        [Comment("价格标准差")]
        public decimal? PriceStdDev { get; set; }
    }
}
