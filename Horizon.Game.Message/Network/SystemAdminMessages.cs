using Horizon.Game.Message.Enums;
using MemoryPack;
using Orleans;
using System;
using System.Collections.Generic;

namespace Horizon.Game.Message.Network
{
    #region 系统管理消息

    /// <summary>
    /// 系统命令消息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class SystemCommandMessage : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 命令ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public string CommandId { get; set; } = "";

        /// <summary>
        /// 命令名称
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string CommandName { get; set; } = "";

        /// <summary>
        /// 命令参数
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public Dictionary<string, string> Parameters { get; set; } = new();

        /// <summary>
        /// 执行者ID
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public ulong ExecutorId { get; set; }

        /// <summary>
        /// 执行者权限等级
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public int PermissionLevel { get; set; }

        /// <summary>
        /// 执行时间
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public long ExecutionTime { get; set; }

        /// <summary>
        /// 是否需要返回结果
        /// </summary>
        [MemoryPackOrder(6)]
        [Id(6)]
        public bool NeedResult { get; set; }

        [MemoryPackOrder(7)]
        [Id(7)]
        public MessageType Type { get; set; } = MessageType.System;
        [MemoryPackOrder(8)]
        [Id(8)]
        public ServiceType ServiceType { get; set; } = ServiceType.System;
    }

    /// <summary>
    /// 系统命令执行结果消息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class SystemCommandResultMessage : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 关联的命令ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public string CommandId { get; set; } = "";

        /// <summary>
        /// 执行结果
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public bool Success { get; set; }

        /// <summary>
        /// 返回消息
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public string ResultMessage { get; set; } = "";

        /// <summary>
        /// 返回数据
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public Dictionary<string, object> Data { get; set; } = new();

        /// <summary>
        /// 执行耗时（毫秒）
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public long ExecutionTimeMs { get; set; }

        [MemoryPackOrder(5)]
        [Id(5)]
        public MessageType Type { get; set; } = MessageType.System;
        [MemoryPackOrder(6)]
        [Id(6)]
        public ServiceType ServiceType { get; set; } = ServiceType.System;
    }

    /// <summary>
    /// 服务器管理消息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class ServerManagementMessage : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 管理操作类型
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ServerManagementOperation Operation { get; set; }

        /// <summary>
        /// 目标服务器ID
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public int TargetServerId { get; set; }

        /// <summary>
        /// 操作参数
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public Dictionary<string, string> Parameters { get; set; } = new();

        /// <summary>
        /// 操作发起者ID
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public ulong OperatorId { get; set; }

        /// <summary>
        /// 操作时间
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public long OperationTime { get; set; }

        [MemoryPackOrder(5)]
        [Id(5)]
        public MessageType Type { get; set; } = MessageType.ServerManagement;
        [MemoryPackOrder(6)]
        [Id(6)]
        public ServiceType ServiceType { get; set; } = ServiceType.System;
    }

    /// <summary>
    /// 服务器管理操作类型
    /// </summary>
    public enum ServerManagementOperation : byte
    {
        /// <summary>
        /// 重启服务器
        /// </summary>
        Restart = 0,

        /// <summary>
        /// 关闭服务器
        /// </summary>
        Shutdown = 1,

        /// <summary>
        /// 启动服务器
        /// </summary>
        Startup = 2,

        /// <summary>
        /// 维护模式切换
        /// </summary>
        ToggleMaintenance = 3,

        /// <summary>
        /// 配置更新
        /// </summary>
        UpdateConfig = 4,

        /// <summary>
        /// 热更新
        /// </summary>
        HotUpdate = 5,

        /// <summary>
        /// 玩家踢出
        /// </summary>
        KickPlayer = 6,

        /// <summary>
        /// 玩家封禁
        /// </summary>
        BanPlayer = 7,

        /// <summary>
        /// 玩家解封
        /// </summary>
        UnbanPlayer = 8,

        /// <summary>
        /// 世界广播
        /// </summary>
        WorldBroadcast = 9,

        /// <summary>
        /// 数据备份
        /// </summary>
        BackupData = 10,

        /// <summary>
        /// 数据恢复
        /// </summary>
        RestoreData = 11
    }

    /// <summary>
    /// 玩家管理消息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class PlayerManagementMessage : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 管理操作类型
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public PlayerManagementOperation Operation { get; set; }

        /// <summary>
        /// 目标玩家ID
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public ulong TargetPlayerId { get; set; }

        /// <summary>
        /// 操作参数
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public Dictionary<string, string> Parameters { get; set; } = new();

        /// <summary>
        /// 操作发起者ID
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public ulong OperatorId { get; set; }

        /// <summary>
        /// 操作原因
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public string Reason { get; set; } = "";

        /// <summary>
        /// 操作时间
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public long OperationTime { get; set; }

        [MemoryPackOrder(6)]
        [Id(6)]
        public MessageType Type { get; set; } = MessageType.PlayerManagement;
        [MemoryPackOrder(7)]
        [Id(7)]
        public ServiceType ServiceType { get; set; } = ServiceType.System;
    }

    /// <summary>
    /// 玩家管理操作类型
    /// </summary>
    public enum PlayerManagementOperation : byte
    {
        /// <summary>
        /// 封禁玩家
        /// </summary>
        Ban = 0,

        /// <summary>
        /// 解封玩家
        /// </summary>
        Unban = 1,

        /// <summary>
        /// 踢出玩家
        /// </summary>
        Kick = 2,

        /// <summary>
        /// 设置权限
        /// </summary>
        SetPermission = 3,

        /// <summary>
        /// 修改属性
        /// </summary>
        ModifyAttribute = 4,

        /// <summary>
        /// 发送邮件
        /// </summary>
        SendMail = 5,

        /// <summary>
        /// 赠送物品
        /// </summary>
        GiveItem = 6,

        /// <summary>
        /// 查看玩家信息
        /// </summary>
        ViewPlayerInfo = 7,

        /// <summary>
        /// 传送玩家
        /// </summary>
        TeleportPlayer = 8,

        /// <summary>
        /// 添加称号
        /// </summary>
        AddTitle = 9,

        /// <summary>
        /// 移除称号
        /// </summary>
        RemoveTitle = 10,

        /// <summary>
        /// 修改昵称
        /// </summary>
        ChangeNickname = 11,

        /// <summary>
        /// 重置角色
        /// </summary>
        ResetCharacter = 12
    }

    /// <summary>
    /// 游戏事件消息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class GameEventMessage : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 事件ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public string EventId { get; set; } = "";

        /// <summary>
        /// 事件类型
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public ClientGameEventType EventType { get; set; }

        /// <summary>
        /// 事件名称
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public string EventName { get; set; } = "";

        /// <summary>
        /// 事件描述
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public string EventDescription { get; set; } = "";

        /// <summary>
        /// 事件参数
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public Dictionary<string, object> Parameters { get; set; } = new();

        /// <summary>
        /// 事件触发时间
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public long TriggerTime { get; set; }

        /// <summary>
        /// 事件持续时间（毫秒）
        /// </summary>
        [MemoryPackOrder(6)]
        [Id(6)]
        public long Duration { get; set; }

        /// <summary>
        /// 参与者列表
        /// </summary>
        [MemoryPackOrder(7)]
        [Id(7)]
        public List<ulong> Participants { get; set; } = new();

        /// <summary>
        /// 事件状态
        /// </summary>
        [MemoryPackOrder(8)]
        [Id(8)]
        public GameEventStatus Status { get; set; }

        [MemoryPackOrder(9)]
        [Id(9)]
        public MessageType Type { get; set; } = MessageType.GameEvent;
        [MemoryPackOrder(10)]
        [Id(10)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    /// <summary>
    /// 游戏事件类型
    /// </summary>
    public enum ClientGameEventType : byte
    {
        /// <summary>
        /// 玩家登录
        /// </summary>
        PlayerLogin = 0,

        /// <summary>
        /// 玩家登出
        /// </summary>
        PlayerLogout = 1,

        /// <summary>
        /// 玩家死亡
        /// </summary>
        PlayerDeath = 2,

        /// <summary>
        /// 玩家升级
        /// </summary>
        PlayerLevelUp = 3,

        /// <summary>
        /// 玩家获得成就
        /// </summary>
        AchievementUnlocked = 4,

        /// <summary>
        /// 玩家加入门派
        /// </summary>
        JoinedSect = 5,

        /// <summary>
        /// 玩家创建帮派
        /// </summary>
        CreatedGuild = 6,

        /// <summary>
        /// 玩家结婚
        /// </summary>
        PlayerMarried = 7,

        /// <summary>
        /// 玩家获得称号
        /// </summary>
        TitleAcquired = 8,

        /// <summary>
        /// 玩家获得稀有物品
        /// </summary>
        RareItemAcquired = 9,

        /// <summary>
        /// NPC死亡
        /// </summary>
        NpcKilled = 10,

        /// <summary>
        /// 世界BOSS击杀
        /// </summary>
        WorldBossKilled = 11,

        /// <summary>
        /// 副本完成
        /// </summary>
        DungeonCompleted = 12,

        /// <summary>
        /// PVP胜利
        /// </summary>
        PvpVictory = 13,

        /// <summary>
        /// 交易完成
        /// </summary>
        TradeCompleted = 14,

        /// <summary>
        /// 技能学习
        /// </summary>
        SkillLearned = 15,

        /// <summary>
        /// 技能升级
        /// </summary>
        SkillUpgraded = 16,

        /// <summary>
        /// 装备强化成功
        /// </summary>
        EquipmentEnhanced = 17,

        /// <summary>
        /// 装备合成
        /// </summary>
        EquipmentCrafted = 18,

        /// <summary>
        /// 任务完成
        /// </summary>
        QuestCompleted = 19
    }

    /// <summary>
    /// 游戏事件状态
    /// </summary>
    public enum GameEventStatus : byte
    {
        /// <summary>
        /// 待处理
        /// </summary>
        Pending = 0,

        /// <summary>
        /// 进行中
        /// </summary>
        Ongoing = 1,

        /// <summary>
        /// 已完成
        /// </summary>
        Completed = 2,

        /// <summary>
        /// 已取消
        /// </summary>
        Cancelled = 3,

        /// <summary>
        /// 失败
        /// </summary>
        Failed = 4,

        /// <summary>
        /// 过期
        /// </summary>
        Expired = 5
    }

    #endregion
}