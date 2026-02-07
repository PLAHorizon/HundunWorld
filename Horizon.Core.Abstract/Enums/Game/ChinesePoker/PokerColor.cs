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
    /// 扑克牌花色
    /// </summary>

    public enum PokerColor
    {
        /// <summary>
        /// 大王
        /// </summary>
        [EnumMember, Description("大王")]
        BigKing = 0,
        /// <summary>
        /// 小王
        /// </summary>
        [EnumMember, Description("小王")]
        SmallKing = 1,
        /// <summary>
        /// 红桃
        /// </summary>
        [EnumMember, Description("红桃")]
        Heart = 2,
        /// <summary>
        /// 方块
        /// </summary>
        [EnumMember, Description("方块")]
        Block = 3,
        /// <summary>
        /// 黑桃
        /// </summary>
        [EnumMember, Description("黑桃")]
        Spade = 4,
        /// <summary>
        /// 梅花
        /// </summary>
        [EnumMember, Description("梅花")]
        Plum = 5,
        /// <summary>
        /// 无
        /// </summary>
        [EnumMember, Description("无")]
        Null = 6
    }
}
