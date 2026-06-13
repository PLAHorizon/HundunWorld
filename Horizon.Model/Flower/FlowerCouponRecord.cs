using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;

namespace Horizon.Model.Flower
{
    [Table("Flower_CouponRecord")]
    [EntityStorage("Flower")]
    public class FlowerCouponRecord : BaseIdentityAggregateRootModel<long>
    {
        [Comment("优惠券ID")]
        public long CouponId { get; set; }

        [Comment("用户ID")]
        public Guid UserId { get; set; }

        [Comment("状态0=未使用1=已使用2=已过期")]
        public int Status { get; set; }

        [Comment("使用的订单ID")]
        public long? UsedOrderId { get; set; }

        [Comment("领取时间")]
        public DateTime ReceivedAt { get; set; }

        [Comment("使用时间")]
        public DateTime? UsedAt { get; set; }
    }
}
