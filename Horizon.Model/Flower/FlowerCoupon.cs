using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;

namespace Horizon.Model.Flower
{
    [Table("Flower_Coupon")]
    [EntityStorage("Flower")]
    public class FlowerCoupon : BaseIdentityAggregateRootModel<long>, ISoftDeleted
    {
        [Comment("店铺ID，0=平台券")]
        public long ShopId { get; set; }

        [StringLength(64), Column(TypeName = "varchar(64)")]
        [Comment("优惠券名称")]
        public string CouponName { get; set; }

        [Comment("优惠券类型0=满减券1=折扣券")]
        public int CouponType { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        [Comment("面额/折扣率")]
        public decimal Denomination { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        [Comment("使用条件满X元")]
        public decimal UseCondition { get; set; }

        [Comment("开始日期")]
        public DateTime StartDate { get; set; }

        [Comment("结束日期")]
        public DateTime EndDate { get; set; }

        [Comment("发放总数")]
        public int TotalCount { get; set; }

        [Comment("已领取数")]
        public int ReceivedCount { get; set; }

        [Comment("已使用数")]
        public int UsedCount { get; set; }

        [Comment("是否启用")]
        public bool IsActive { get; set; }

        public bool IsDeleted { get; set; }
    }
}
