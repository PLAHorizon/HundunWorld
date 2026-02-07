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
    /// 斗地主牌型
    /// </summary>

    public enum ChinesePokerCard
    {
        /// <summary>
        /// 火箭
        /// </summary>
        [EnumMember, Description("火箭")]
        DoubleKing = 0,//双王，最大的牌
        /// <summary>
        /// 炸弹(AAAA)
        /// </summary>
        [EnumMember, Description("炸弹")]
        Bomb = 1,//四张同数值牌（如四个7）
        /// <summary>
        /// 单牌(A)
        /// </summary>
        [EnumMember, Description("单牌")]
        Single = 2,
        /// <summary>
        /// 对子(AA)
        /// </summary>
        [EnumMember, Description("对子")]
        Twins = 3,
        /// <summary>
        /// 三张牌（AAA）
        /// </summary>
        [EnumMember, Description("三张牌")]
        ThreeTwins = 4,//数值相同的三张牌（如三个J）
        /// <summary>
        /// 三带一（AAAB）
        /// </summary>
        [EnumMember, Description("三带一")]
        ThreeBandOne = 5,//数值相同的三张牌+一张单牌或一对牌
        /// <summary>
        /// 单顺（ABCDE....）
        /// </summary>
        [EnumMember, Description("单顺")]
        AlongSingle = 6,//五张或更多的连续单牌（如：45678 或 78910JQK）。不包括2点和双王
        /// <summary>
        /// 双顺（AABBCC....）
        /// </summary>
        [EnumMember, Description("双顺")]
        AlongDouble = 7,//三对或更多的连续对牌。（如：334455、7788991010JJ）不包括2点和双王
        /// <summary>
        /// 三顺（AAABBB....）
        /// </summary>
        [EnumMember, Description("三顺")]
        AlongThree = 8,//二个或更多的连续三张牌（如：333444555、777888999101010JJJ）。不包括2点和双王
        /// <summary>
        /// 飞机带翅膀（CAAABBBD或AAABBBCCC+DDEEFF）
        /// </summary>
        [EnumMember, Description("飞机带翅膀")]
        AircraftBandWings = 9,//三顺+同数量的单牌(或同数量的对牌)。如：444555+79 或 333444555+7799JJ
        /// <summary>
        /// 四带二（CAAAAD或CCAAAADD）
        /// </summary>
        [EnumMember, Description("四带二")]
        FourBandTwo = 10,//四张牌+两手牌(注：四带二不是炸弹)。 如：5555 ＋ 3 ＋ 8 或 4444 ＋ 55 ＋ 77
        /// <summary>
        /// 未知，不支持的出牌牌型
        /// </summary>
        [EnumMember, Description("未知")]
        Unknown = -99
    }
}
