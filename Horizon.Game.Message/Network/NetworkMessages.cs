using System;
using System.Collections.Generic;
using Horizon.Game.Message;
using Horizon.Game.Message.Enums;
using MemoryPack;
using Orleans;

namespace Horizon.Game.Message.Network
{
    [MemoryPackable]
    [GenerateSerializer]
    public partial class PlayerAnimationMessage : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 玩家ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong CharacterId { get; set; }

        /// <summary>
        /// 动画名称
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string AnimationName { get; set; } = "";

        /// <summary>
        /// 动画参数
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public Dictionary<string, object> Parameters { get; set; } = new();

        /// <summary>
        /// 动画时间
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public float AnimationTime { get; set; }

        [MemoryPackOrder(4)]
        [Id(4)]
        public MessageType Type { get; set; } = MessageType.PlayerAnimation;

        [MemoryPackOrder(5)]
        [Id(5)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    /// <summary>
    /// 系统消息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class SystemMessage : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 消息类型
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public SystemMessageType SystemMessageType { get; set; }

        /// <summary>
        /// 消息内容
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)] public string Content { get; set; } = "";

        /// <summary>
        /// 时间戳
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)] public long Timestamp { get; set; } = DateTime.UtcNow.Ticks;

        [MemoryPackOrder(3)]
        [Id(3)] public ServiceType ServiceType { get; set; } = ServiceType.Game;
        [MemoryPackOrder(4)]
        [Id(4)] public MessageType Type { get; set; } = MessageType.System;
    }

    /// <summary>
    /// 获取服务器列表请求
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class ServerListRequest : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 消息类型
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public MessageType Type { get; set; } = MessageType.ZoneAndServerInfo;

        /// <summary>
        /// 服务类型
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;

        /// <summary>
        /// 请求时间戳
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public long Timestamp { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        /// <summary>
        /// 客户端版本
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public int GameId { get; set; }


    }

    /// <summary>
    /// 服务器列表响应
    /// </summary>
    [MemoryPackable]
    public partial class ServerListResponse :MessageUnion, INetworkMessage, IGameHeader
    {
        /// <summary>
        /// 消息类型
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public MessageType Type { get; set; }= MessageType.ZoneAndServerInfo;

        /// <summary>
        /// 服务类型
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;

        /// <summary>
        /// 是否成功
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public bool Success { get; set; }

        /// <summary>
        /// 错误消息
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public string Message { get; set; }

        /// <summary>
        /// 服务器列表
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public List<ServerInfo> ServerList { get; set; } = new List<ServerInfo>();

        /// <summary>
        /// 响应时间戳
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public long Timestamp { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        [MemoryPackOrder(6)]
        [Id(6)]
        public int GameId { get; set; }
        [MemoryPackOrder(7)]
        [Id(7)]
        public int ZoneId { get; set; }
        [MemoryPackOrder(8)]
        [Id(8)]
        public int ServerId { get; set; }
    }
    /// <summary>
    /// 系统消息类型
    /// </summary>
    public enum SystemMessageType : byte
    {
        /// <summary>
        /// 信息
        /// </summary>
        Info = 1,

        /// <summary>
        /// 警告
        /// </summary>
        Warning = 2,

        /// <summary>
        /// 错误
        /// </summary>
        Error = 3,

        /// <summary>
        /// 成功
        /// </summary>
        Success = 4,

        /// <summary>
        /// 通知
        /// </summary>
        Notification = 5
    }

    
}


