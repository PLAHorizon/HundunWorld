using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Core.Abstract.Enums
{
    /// <summary>
    /// 斗地主牌局状态
    /// </summary>

    public enum PaiJuStatus
    {
        /// <summary>
        /// 创建
        /// </summary>
        [EnumMember, Description("创建")]
        Create = -1,
        /// <summary>
        /// 正常
        /// </summary>
        [EnumMember, Description("正常")]
        Normal = 0,
        /// <summary>
        /// 未开始便结束
        /// </summary>
        [EnumMember, Description("未开始便结束")]
        NotStart = 1,
        /// <summary>
        /// 地主投降
        /// </summary>
        [EnumMember, Description("地主投降")]
        LandlordSurrender = 2,
        /// <summary>
        /// 农名投降
        /// </summary>
        [EnumMember, Description("农名投降")]
        FarmerSurrender = 3,
        /// <summary>
        /// 有人逃跑
        /// </summary>
        [EnumMember, Description("有人逃跑")]
        Escape = 4,
        /// <summary>
        /// 完结
        /// </summary>
        [EnumMember, Description("完结")]
        Complete = 5,
    }
}
