using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;

namespace Horizon.Model.Flower
{
    /// <summary>
    /// 花卉用户扩展
    /// </summary>
    [Table("Flower_User")]
    [EntityStorage("Flower")]
    public class FlowerUser : BaseIdentityAggregateRootModel<long>, ISoftDeleted
    {
        /// <summary>
        /// 用户ID
        /// </summary>
        [Comment("用户ID")]
        public Guid UserId { get; set; }

        /// <summary>
        /// 用户类型
        /// </summary>
        [Comment("用户类型")]
        public int UserType { get; set; }

        /// <summary>
        /// 商户ID
        /// </summary>
        [Comment("商户ID")]
        public long? MerchantId { get; set; }

        /// <summary>
        /// 显示名称
        /// </summary>
        [StringLength(64), Column(TypeName = "varchar(64)")]
        [Comment("显示名称")]
        public string DisplayName { get; set; }

        /// <summary>
        /// 手机号
        /// </summary>
        [StringLength(20), Column(TypeName = "varchar(20)")]
        [Comment("手机号")]
        public string Phone { get; set; }

        /// <summary>
        /// 地区
        /// </summary>
        [StringLength(64), Column(TypeName = "varchar(64)")]
        [Comment("地区")]
        public string Region { get; set; }

        /// <summary>
        /// 订阅等级
        /// </summary>
        [Comment("订阅等级")]
        public int SubscriptionLevel { get; set; }

        /// <summary>
        /// 通行证号
        /// </summary>
        [StringLength(64), Column(TypeName = "varchar(64)")]
        [Comment("通行证号")]
        public string Passport { get; set; }

        /// <summary>
        /// 创建时间
        /// </summary>
        [Comment("创建时间")]
        public DateTime CreateTime { get; set; }

        /// <summary>
        /// 是否已删除，true : 已删除，false : 未删除
        /// </summary>
        [Comment("是否已删除，true : 已删除，false : 未删除")]
        public bool IsDeleted { get; set; }
    }
}
