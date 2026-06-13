using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;

namespace Horizon.Model.Flower
{
    /// <summary>
    /// 花卉品类
    /// </summary>
    [Table("Flower_Species")]
    [EntityStorage("Flower")]
    public class FlowerSpecies : BaseIdentityAggregateRootModel<long>, ISoftDeleted
    {
        /// <summary>
        /// 品类编码
        /// </summary>
        [StringLength(32), Column(TypeName = "varchar(32)")]
        [Comment("品类编码")]
        public string SpeciesCode { get; set; }

        /// <summary>
        /// 品类分类
        /// </summary>
        [Comment("品类分类")]
        public int Category { get; set; }

        /// <summary>
        /// 品类名称
        /// </summary>
        [StringLength(64), Column(TypeName = "varchar(64)")]
        [Comment("品类名称")]
        public string Name { get; set; }

        /// <summary>
        /// 显示名称
        /// </summary>
        [StringLength(128), Column(TypeName = "varchar(128)")]
        [Comment("显示名称")]
        public string DisplayName { get; set; }

        /// <summary>
        /// 产地
        /// </summary>
        [StringLength(64), Column(TypeName = "varchar(64)")]
        [Comment("产地")]
        public string OriginRegion { get; set; }

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
