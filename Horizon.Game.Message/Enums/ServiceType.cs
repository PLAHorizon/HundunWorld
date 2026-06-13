using MemoryPack;
using System.ComponentModel;

namespace Horizon.Game.Message.Enums
{
    /// <summary>
    /// 服务类型枚举
    /// </summary>
    public enum ServiceType : byte
    {
        [Description("网关服务器")]
        Gateway = 1,

        [Description("游戏服务器")]
        Game = 2,

        [Description("账号服务器")]
        Account = 3,

        [Description("聊天服务器")]
        Chat = 4,

        [Description("社交服务器")]
        Social = 5,

        [Description("交易服务器")]
        Trade = 6,

        [Description("系统服务器")]
        System = 7,

        [Description("战斗服务器")]
        Combat = 8,

        [Description("任务服务器")]
        Quest = 9,

        [Description("竞技场服务器")]
        Arena = 10,

        [Description("跨服服务器")]
        CrossServer = 11,

        [Description("即时通讯服务")]
        IM = 12
    }
}
