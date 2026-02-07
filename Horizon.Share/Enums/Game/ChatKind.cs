using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Share.Enums.Game
{
    /// <summary>
    /// 游戏内聊天种类
    /// </summary>
    public enum ChatKind : sbyte
    {
        /// <summary>
        /// 聊天/私聊
        /// </summary>
        [Description("聊天/私聊")] ChatP2P = 1,
        /// <summary>
        /// 聊天/团队聊天
        /// </summary>
        [Description("聊天/团队聊天")] ChatP2G = 2,
        /// <summary>
        /// 聊天/团队聊天
        /// </summary>
        [Description("聊天/团队聊天")] ChatP2SG = 3,
        /// <summary>
        /// 聊天/世界范围
        /// </summary>
        [Description("聊天/世界范围")] ChatWorld = 4,
        /// <summary>
        /// 聊天/本地
        /// </summary>
        [Description("聊天/本地")] ChatLocal = 5,
    }
}
