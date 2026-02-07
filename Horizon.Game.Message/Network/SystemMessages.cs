using Horizon.Game.Message.Enums;
using MemoryPack;
using Orleans;
using System;
using System.Collections.Generic;

namespace Horizon.Game.Message.Network
{
    #region 心跳消息

    /// <summary>
    /// 心跳消息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class HeartbeatMessage : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 时间戳
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public long Timestamp { get; set; }

        /// <summary>
        /// 客户端时间
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public long ClientTime { get; set; }

        /// <summary>
        /// 服务器时间
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public long ServerTime { get; set; }

        [MemoryPackOrder(3)]
        [Id(3)]
        public MessageType Type { get; set; } = MessageType.Heartbeat;
        [MemoryPackOrder(4)]
        [Id(4)]
        public ServiceType ServiceType { get; set; } = ServiceType.System;
    }

    /// <summary>
    /// 心跳响应消息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class HeartbeatResponse : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 时间戳
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public long Timestamp { get; set; }

        /// <summary>
        /// 服务器时间
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public long ServerTime { get; set; }

        /// <summary>
        /// 延迟时间（毫秒）
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public long Latency { get; set; }

        [MemoryPackOrder(3)]
        [Id(3)]
        public MessageType Type { get; set; } = MessageType.HeartbeatResponse;
        [MemoryPackOrder(4)]
        [Id(4)]
        public ServiceType ServiceType { get; set; } = ServiceType.System;
    }

    #endregion

    #region 系统通知消息

    /// <summary>
    /// 系统消息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class SystemNotificationMessage : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 消息ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public string MessageId { get; set; } = "";

        /// <summary>
        /// 消息类型
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public SystemMessageType SystemMessageType { get; set; }

        /// <summary>
        /// 消息标题
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public string Title { get; set; } = "";

        /// <summary>
        /// 消息内容
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public string Content { get; set; } = "";

        /// <summary>
        /// 优先级
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public int Priority { get; set; }

        /// <summary>
        /// 发送时间
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public long SendTime { get; set; }

        /// <summary>
        /// 过期时间
        /// </summary>
        [MemoryPackOrder(6)]
        [Id(6)]
        public long ExpireTime { get; set; }

        /// <summary>
        /// 目标用户（空表示所有用户）
        /// </summary>
        [MemoryPackOrder(7)]
        [Id(7)]
        public List<ulong> TargetUsers { get; set; } = new();

        /// <summary>
        /// 目标等级范围（最小等级）
        /// </summary>
        [MemoryPackOrder(8)]
        [Id(8)]
        public int MinLevel { get; set; }

        /// <summary>
        /// 目标等级范围（最大等级）
        /// </summary>
        [MemoryPackOrder(9)]
        [Id(9)]
        public int MaxLevel { get; set; }

        [MemoryPackOrder(10)]
        [Id(10)]
        public MessageType Type { get; set; } = MessageType.System;
        [MemoryPackOrder(11)]
        [Id(11)]
        public ServiceType ServiceType { get; set; } = ServiceType.System;
    }



    /// <summary>
    /// 系统公告消息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class SystemAnnouncementMessage : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 公告ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public string AnnouncementId { get; set; } = "";

        /// <summary>
        /// 公告标题
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string Title { get; set; } = "";

        /// <summary>
        /// 公告内容
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public string Content { get; set; } = "";

        /// <summary>
        /// 发布时间
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public long PublishTime { get; set; }

        /// <summary>
        /// 开始显示时间
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public long StartTime { get; set; }

        /// <summary>
        /// 结束显示时间
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public long EndTime { get; set; }

        /// <summary>
        /// 显示位置
        /// </summary>
        [MemoryPackOrder(6)]
        [Id(6)]
        public AnnouncementPosition Position { get; set; }

        /// <summary>
        /// 是否滚动显示
        /// </summary>
        [MemoryPackOrder(7)]
        [Id(7)]
        public bool IsScrolling { get; set; }

        /// <summary>
        /// 滚动速度
        /// </summary>
        [MemoryPackOrder(8)]
        [Id(8)]
        public int ScrollSpeed { get; set; }

        [MemoryPackOrder(9)]
        [Id(9)]
        public MessageType Type { get; set; } = MessageType.System;
        [MemoryPackOrder(10)]
        [Id(10)]
        public ServiceType ServiceType { get; set; } = ServiceType.System;
    }

    /// <summary>
    /// 公告显示位置
    /// </summary>
    public enum AnnouncementPosition
    {
        /// <summary>
        /// 顶部
        /// </summary>
        Top = 1,

        /// <summary>
        /// 中部
        /// </summary>
        Middle = 2,

        /// <summary>
        /// 底部
        /// </summary>
        Bottom = 3,

        /// <summary>
        /// 弹窗
        /// </summary>
        Popup = 4,

        /// <summary>
        /// 聊天框
        /// </summary>
        Chat = 5
    }

    #endregion

    #region 错误消息

    /// <summary>
    /// 错误消息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class ErrorMessage : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 错误代码
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public int ErrorCode { get; set; }

        /// <summary>
        /// 错误消息
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string Message { get; set; } = "";

        /// <summary>
        /// 详细错误信息
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public string Details { get; set; } = "";

        /// <summary>
        /// 发生时间
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public long Timestamp { get; set; }

        /// <summary>
        /// 相关消息ID
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public string RelatedMessageId { get; set; } = "";

        /// <summary>
        /// 是否需要重试
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public bool ShouldRetry { get; set; }

        /// <summary>
        /// 重试次数
        /// </summary>
        [MemoryPackOrder(6)]
        [Id(6)]
        public int RetryCount { get; set; }

        [MemoryPackOrder(7)]
        [Id(7)]
        public MessageType Type { get; set; } = MessageType.Error;
        [MemoryPackOrder(8)]
        [Id(8)]
        public ServiceType ServiceType { get; set; } = ServiceType.System;
    }

    /// <summary>
    /// 服务器状态消息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class ServerStatusMessage : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 服务器ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public int ServerId { get; set; }

        /// <summary>
        /// 服务器名称
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string ServerName { get; set; } = "";

        /// <summary>
        /// 服务器状态
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public ServerStatus Status { get; set; }

        /// <summary>
        /// 在线人数
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public int OnlineCount { get; set; }

        /// <summary>
        /// 最大在线人数
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public int MaxOnlineCount { get; set; }

        /// <summary>
        /// CPU使用率
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public float CpuUsage { get; set; }

        /// <summary>
        /// 内存使用率
        /// </summary>
        [MemoryPackOrder(6)]
        [Id(6)]
        public float MemoryUsage { get; set; }

        /// <summary>
        /// 网络延迟
        /// </summary>
        [MemoryPackOrder(7)]
        [Id(7)]
        public long NetworkLatency { get; set; }

        /// <summary>
        /// 更新时间
        /// </summary>
        [MemoryPackOrder(8)]
        [Id(8)]
        public long UpdateTime { get; set; }

        [MemoryPackOrder(9)]
        [Id(9)]
        public MessageType Type { get; set; } = MessageType.System;
        [MemoryPackOrder(10)]
        [Id(10)]
        public ServiceType ServiceType { get; set; } = ServiceType.System;
    }

    /// <summary>
    /// 服务器状态
    /// </summary>
    public enum ServerStatus
    {
        /// <summary>
        /// 正常
        /// </summary>
        Normal = 1,

        /// <summary>
        /// 繁忙
        /// </summary>
        Busy = 2,

        /// <summary>
        /// 维护中
        /// </summary>
        Maintenance = 3,

        /// <summary>
        /// 故障
        /// </summary>
        Fault = 4,

        /// <summary>
        /// 满载
        /// </summary>
        Full = 5
    }

    #endregion

    #region 配置更新消息

    /// <summary>
    /// 配置更新消息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class ConfigUpdateMessage : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 配置键
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public string ConfigKey { get; set; } = "";

        /// <summary>
        /// 配置值
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string ConfigValue { get; set; } = "";

        /// <summary>
        /// 配置类型
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public string ConfigType { get; set; } = "";

        /// <summary>
        /// 更新时间
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public long UpdateTime { get; set; }

        /// <summary>
        /// 是否需要重启生效
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public bool RequireRestart { get; set; }

        [MemoryPackOrder(5)]
        [Id(5)]
        public MessageType Type { get; set; } = MessageType.System;
        [MemoryPackOrder(6)]
        [Id(6)]
        public ServiceType ServiceType { get; set; } = ServiceType.System;
    }

    /// <summary>
    /// 版本更新消息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class VersionUpdateMessage : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 当前版本
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public string CurrentVersion { get; set; } = "";

        /// <summary>
        /// 最新版本
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string LatestVersion { get; set; } = "";

        /// <summary>
        /// 更新内容
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public string UpdateContent { get; set; } = "";

        /// <summary>
        /// 下载地址
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public string DownloadUrl { get; set; } = "";

        /// <summary>
        /// 强制更新
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public bool ForceUpdate { get; set; }

        /// <summary>
        /// 更新时间
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public long UpdateTime { get; set; }

        [MemoryPackOrder(6)]
        [Id(6)]
        public MessageType Type { get; set; } = MessageType.System;
        [MemoryPackOrder(7)]
        [Id(7)]
        public ServiceType ServiceType { get; set; } = ServiceType.System;
    }

    #endregion

    #region 日志消息

    /// <summary>
    /// 日志消息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class LogMessage : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 日志级别
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public LogLevel Level { get; set; }

        /// <summary>
        /// 日志来源
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string Source { get; set; } = "";

        /// <summary>
        /// 日志内容
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public string Content { get; set; } = "";

        /// <summary>
        /// 时间戳
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public long Timestamp { get; set; }

        /// <summary>
        /// 相关用户ID
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public ulong UserId { get; set; }

        /// <summary>
        /// 相关角色ID
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public ulong CharacterId { get; set; }

        /// <summary>
        /// 额外数据
        /// </summary>
        [MemoryPackOrder(6)]
        [Id(6)]
        public Dictionary<string, object> ExtraData { get; set; } = new();

        [MemoryPackOrder(7)]
        [Id(7)]
        public MessageType Type { get; set; } = MessageType.System;
        [MemoryPackOrder(8)]
        [Id(8)]
        public ServiceType ServiceType { get; set; } = ServiceType.System;
    }

    /// <summary>
    /// 日志级别
    /// </summary>
    public enum LogLevel
    {
        /// <summary>
        /// 调试
        /// </summary>
        Debug = 1,

        /// <summary>
        /// 信息
        /// </summary>
        Info = 2,

        /// <summary>
        /// 警告
        /// </summary>
        Warning = 3,

        /// <summary>
        /// 错误
        /// </summary>
        Error = 4,

        /// <summary>
        /// 严重错误
        /// </summary>
        Fatal = 5
    }

    #endregion
}