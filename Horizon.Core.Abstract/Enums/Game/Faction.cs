using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Core.Abstract.Enums
{
    /// <summary>
    /// 帮派等级
    /// </summary>

    public enum FactionLevel
    {
        /// <summary>
        /// 一级帮,50人
        /// </summary>
        [EnumMember]
        One = 50,
        /// <summary>
        /// 二级帮,150人
        /// </summary>
        [EnumMember]
        Two = 150,
        /// <summary>
        /// 三级帮,300人
        /// </summary>
        [EnumMember]
        Three = 300,
        /// <summary>
        /// 四级帮,1000人
        /// </summary>
        [EnumMember]
        Four = 1000,
        /// <summary>
        /// 五级帮,5000人
        /// </summary>
        [EnumMember]
        Five = 5000,

    }

    /*
     *  /// <summary>
        /// 天策(智勇仁信义)
        /// 盾，游击，高攻高防
        /// </summary>
        [EnumMember]
        ZYRXY = 0,
        /// <summary>
        /// 纯阳(太极八卦)
        /// 混元内功，远程控制兼具外功
        /// </summary>
        [EnumMember] ChunYang = 1,
        /// <summary>
        /// 峨眉(女护士)
        /// </summary>
        [EnumMember] EMei = 2,
        /// <summary>
        /// 少林(我不入地狱谁入地狱)
        /// 盾，减伤
        /// </summary>
        [EnumMember] ShaoLin = 3,
        /// <summary>
        /// 儒医，博学医术精湛
        /// </summary>
        [EnumMember] RuYi = 4,
        /// <summary>
        /// 猎人(擅长弓箭与捕杀)
        /// </summary>
        [EnumMember] LieRen = 5,
        /// <summary>
        /// 圣火(高攻速高暴击，续航能力低)
        /// </summary>
        [EnumMember] ShengHuo = 6,
        /// <summary>
        /// 魔医(精通医术召唤宠物)
        /// </summary>
        [EnumMember] MoYi = 7,
        /// <summary>
        /// 自然之子(强力控制)
        /// 全方位控制，低输出高续航，高生存
        /// </summary>
        [EnumMember] SunMoon = 8,
        /// <summary>
        /// 无职业
        /// </summary>
        [EnumMember] None = 999
     */
}
