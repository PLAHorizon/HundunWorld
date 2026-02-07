using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Core.Abstract.Enums
{
    /// <summary>
    /// 微信活动奖品类型
    /// </summary>
    public enum WeiActivityAwardType
    {
        /// <summary>
        /// 积分
        /// </summary>
        [Description("积分")]
        longegral = 0,

        /// <summary>
        /// 红包
        /// </summary>
        [Description("红包")]
        Bonus = 1,

        /// <summary>
        /// 优惠卷
        /// </summary>
        [Description("优惠卷")]
        Coupon = 2,
    }

    /// <summary>
    /// 微信活动参与类型
    /// </summary>
    public enum WeiParticipateType : long
    {
        /// <summary>
        /// 活动总次数
        /// </summary>
        [Description("活动总次数")]
        CommonCount = 0,

        /// <summary>
        /// 活动天次数
        /// </summary>
        [Description("活动天次数")]
        DayCount = 1,

        /// <summary>
        /// 无限制
        /// </summary>
        [Description("无限制")]
        Unlimited = 2,
    }

    /// <summary>
    /// 微信活动类型
    /// </summary>
    public enum WeiActivityType : long
    {
        /// <summary>
        /// 刮刮卡
        /// </summary>
        [Description("刮刮卡")]
        ScratchCard = 0,

        /// <summary>
        /// 大转盘
        /// </summary>
        [Description("大转盘")]
        Roulette = 1

    }
}
