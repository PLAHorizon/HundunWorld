using MemoryPack;
using System.ComponentModel;

namespace Horizon.Game.Message.Enums
{
    /// <summary>
    /// 服务类型枚举
    /// </summary>
    public enum ServiceType : byte
    {
        /// <summary>
        /// 网关服务器
        /// </summary>
        [Description("网关服务器")]
        Gateway = 1,

        /// <summary>
        /// 游戏服务器
        /// </summary>
        [Description("游戏服务器")]
        Game = 2,

        /// <summary>
        /// 账号服务器
        /// </summary>
        [Description("账号服务器")]
        Account = 3,

        /// <summary>
        /// 聊天服务器
        /// </summary>
        [Description("聊天服务器")]
        Chat = 4,

        /// <summary>
        /// 社交服务器
        /// </summary>
        [Description("社交服务器")]
        Social = 5,

        /// <summary>
        /// 交易服务器
        /// </summary>
        [Description("交易服务器")]
        Trade = 6,

        /// <summary>
        /// 系统服务器
        /// </summary>
        [Description("系统服务器")]
        System = 7,

        /// <summary>
        /// 战斗服务器
        /// </summary>
        [Description("战斗服务器")]
        Combat = 8,

        /// <summary>
        /// 任务服务器
        /// </summary>
        [Description("任务服务器")]
        Quest = 9
    }
}