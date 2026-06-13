using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;

namespace Horizon.Model.Flower
{
    /// <summary>
    /// 花卉市场
    /// </summary>
    [Table("Flower_Market")]
    [EntityStorage("Flower")]
    public class FlowerMarket : BaseIdentityAggregateRootModel<long>, ISoftDeleted
    {
        /// <summary>
        /// 市场编码
        /// </summary>
        [StringLength(32), Column(TypeName = "varchar(32)")]
        [Comment("市场编码")]
        public string MarketCode { get; set; }

        /// <summary>
        /// 市场名称
        /// </summary>
        [StringLength(128), Column(TypeName = "varchar(128)")]
        [Comment("市场名称")]
        public string Name { get; set; }

        /// <summary>
        /// 地区
        /// </summary>
        [StringLength(64), Column(TypeName = "varchar(64)")]
        [Comment("地区")]
        public string Region { get; set; }

        /// <summary>
        /// 纬度
        /// </summary>
        [Comment("纬度")]
        public double Latitude { get; set; }

        /// <summary>
        /// 经度
        /// </summary>
        [Comment("经度")]
        public double Longitude { get; set; }

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
