using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;

namespace Horizon.Model.Flower
{
    /// <summary>
    /// 订阅
    /// </summary>
    [Table("Flower_Subscription")]
    [EntityStorage("Flower")]
    public class FlowerSubscription : BaseIdentityAggregateRootModel<long>, ISoftDeleted
    {
        /// <summary>
        /// 用户ID
        /// </summary>
        [Comment("用户ID")]
        public Guid UserId { get; set; }

        /// <summary>
        /// 订阅等级
        /// </summary>
        [Comment("订阅等级")]
        public int Level { get; set; }

        /// <summary>
        /// 开始日期
        /// </summary>
        [Comment("开始日期")]
        public DateTime StartDate { get; set; }

        /// <summary>
        /// 结束日期
        /// </summary>
        [Comment("结束日期")]
        public DateTime EndDate { get; set; }

        /// <summary>
        /// 自动续费
        /// </summary>
        [Comment("自动续费")]
        public bool AutoRenew { get; set; }

        /// <summary>
        /// 支付方式
        /// </summary>
        [StringLength(32), Column(TypeName = "varchar(32)")]
        [Comment("支付方式")]
        public string PaymentMethod { get; set; }

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
