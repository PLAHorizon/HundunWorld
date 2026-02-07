using MemoryPack;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Game.Message.Enums
{
    /// <summary>
    /// 完成挑战的奖励类型
    /// </summary>
    public enum RewardType
    {
        /// <summary>
        /// 经验值
        /// </summary>
        [Description("经验值")]
        Experience = 0,
        
        /// <summary>
        /// 金币
        /// </summary>
        [Description("金币")]
        Gold = 1,
        
        /// <summary>
        /// 物品
        /// </summary>
        [Description("物品")]
        Item = 2,
        
        /// <summary>
        /// 成就点数
        /// </summary>
        [Description("成就点数")]
        AchievementPoints = 3,
        
        /// <summary>
        /// 金钱
        /// </summary>
        [Description("金钱")]
        Moeny = 4,
        
        /// <summary>
        /// 实物
        /// </summary>
        [Description("实物")]
        Entity = 5,
        
        /// <summary>
        /// 第三方提供服务
        /// </summary>
        [Description("第三方提供服务")]
        ThirdParty = 6,
    }
}