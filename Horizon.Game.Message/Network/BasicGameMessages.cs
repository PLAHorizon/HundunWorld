using Horizon.Game.Message.Enums;
using MemoryPack;
using Orleans;
using System;
using System.Collections.Generic;

namespace Horizon.Game.Message.Network
{
    #region 基础游戏消息

    /// <summary>
    /// 玩家同步消息 - 用于同步玩家的基础状态信息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class PlayerSyncMessage : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 玩家ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong PlayerId { get; set; }

        /// <summary>
        /// 玩家名称
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string PlayerName { get; set; } = "";

        /// <summary>
        /// 当前位置
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public Position CurrentPosition { get; set; } = new();

        /// <summary>
        /// 当前旋转角度
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public float Rotation { get; set; }

        /// <summary>
        /// 当前生命值
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public int CurrentHp { get; set; }

        /// <summary>
        /// 最大生命值
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public int MaxHp { get; set; }

        /// <summary>
        /// 当前法力值
        /// </summary>
        [MemoryPackOrder(6)]
        [Id(6)]
        public int CurrentMp { get; set; }

        /// <summary>
        /// 最大法力值
        /// </summary>
        [MemoryPackOrder(7)]
        [Id(7)]
        public int MaxMp { get; set; }

        /// <summary>
        /// 玩家等级
        /// </summary>
        [MemoryPackOrder(8)]
        [Id(8)]
        public int Level { get; set; }

        /// <summary>
        /// 玩家职业
        /// </summary>
        [MemoryPackOrder(9)]
        [Id(9)]
        public int Profession { get; set; }

        /// <summary>
        /// 玩家状态
        /// </summary>
        [MemoryPackOrder(10)]
        [Id(10)]
        public PlayerStatus Status { get; set; }

        /// <summary>
        /// 同步时间戳
        /// </summary>
        [MemoryPackOrder(11)]
        [Id(11)]
        public long Timestamp { get; set; }

        [MemoryPackOrder(12)]
        [Id(12)]
        public MessageType Type { get; set; } = MessageType.PlayerSpawn;
        [MemoryPackOrder(13)]
        [Id(13)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    /// <summary>
    /// 玩家状态枚举
    /// </summary>
    public enum PlayerStatus : byte
    {
        /// <summary>
        /// 正常状态
        /// </summary>
        Normal = 0,

        /// <summary>
        /// 战斗状态
        /// </summary>
        InCombat = 1,

        /// <summary>
        /// 移动状态
        /// </summary>
        Moving = 2,

        /// <summary>
        /// 施法状态
        /// </summary>
        Casting = 3,

        /// <summary>
        /// 受伤状态
        /// </summary>
        Injured = 4,

        /// <summary>
        /// 死亡状态
        /// </summary>
        Dead = 5,

        /// <summary>
        /// 离线状态
        /// </summary>
        Offline = 6,

        /// <summary>
        /// 隐身状态
        /// </summary>
        Invisible = 7,

        /// <summary>
        /// 保护状态
        /// </summary>
        Protected = 8
    }

    /// <summary>
    /// 世界状态同步消息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class WorldStateSyncMessage : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 世界ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public int WorldId { get; set; }

        /// <summary>
        /// 世界时间
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public long WorldTime { get; set; }

        /// <summary>
        /// 世界天气
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public WeatherType Weather { get; set; }

        /// <summary>
        /// 事件列表
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public List<WorldEvent> Events { get; set; } = new();

        /// <summary>
        /// 玩家列表
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public List<PlayerSyncMessage> Players { get; set; } = new();

        /// <summary>
        /// NPC列表
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public List<NpcSyncMessage> Npcs { get; set; } = new();

        /// <summary>
        /// 物品掉落列表
        /// </summary>
        [MemoryPackOrder(6)]
        [Id(6)]
        public List<ItemDropMessage> ItemDrops { get; set; } = new();

        [MemoryPackOrder(7)]
        [Id(7)]
        public MessageType Type { get; set; } = MessageType.System;
        [MemoryPackOrder(8)]
        [Id(8)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    /// <summary>
    /// 世界天气类型
    /// </summary>
    public enum WeatherType : byte
    {
        /// <summary>
        /// 晴天
        /// </summary>
        Sunny = 0,

        /// <summary>
        /// 雨天
        /// </summary>
        Rainy = 1,

        /// <summary>
        /// 雪天
        /// </summary>
        Snowy = 2,

        /// <summary>
        /// 雾天
        /// </summary>
        Foggy = 3,

        /// <summary>
        /// 雷暴
        /// </summary>
        Storm = 4,

        /// <summary>
        /// 沙尘暴
        /// </summary>
        Sandstorm = 5,

        /// <summary>
        /// 彩虹
        /// </summary>
        Rainbow = 6
    }

    /// <summary>
    /// 世界事件
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class WorldEvent : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 事件ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public int EventId { get; set; }

        /// <summary>
        /// 事件类型
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public WorldEventType EventType { get; set; }

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
        /// 事件位置
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public Position EventPosition { get; set; } = new();

        /// <summary>
        /// 事件开始时间
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public long StartTime { get; set; }

        /// <summary>
        /// 事件结束时间
        /// </summary>
        [MemoryPackOrder(6)]
        [Id(6)]
        public long EndTime { get; set; }

        /// <summary>
        /// 事件参数
        /// </summary>
        [MemoryPackOrder(7)]
        [Id(7)]
        public Dictionary<string, object> Parameters { get; set; } = new();

        [MemoryPackOrder(8)]
        [Id(8)]
        public MessageType Type { get; set; } = MessageType.GameEvent;
        [MemoryPackOrder(9)]
        [Id(9)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    /// <summary>
    /// 世界事件类型
    /// </summary>
    public enum WorldEventType : byte
    {
        /// <summary>
        /// 节日活动
        /// </summary>
        Festival = 0,

        /// <summary>
        /// BOSS刷新
        /// </summary>
        BossSpawn = 1,

        /// <summary>
        /// PVP竞赛
        /// </summary>
        PvpCompetition = 2,

        /// <summary>
        /// 门派战
        /// </summary>
        SectWar = 3,

        /// <summary>
        /// 帮派战
        /// </summary>
        GuildWar = 4,

        /// <summary>
        /// 世界BOSS
        /// </summary>
        WorldBoss = 5,

        /// <summary>
        /// 奇遇事件
        /// </summary>
        AdventureEvent = 6,

        /// <summary>
        /// 副本开启
        /// </summary>
        DungeonOpening = 7,

        /// <summary>
        /// 新手活动
        /// </summary>
        NewbieEvent = 8,

        /// <summary>
        /// 特殊天气
        /// </summary>
        SpecialWeather = 9
    }

    /// <summary>
    /// NPC同步消息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class NpcSyncMessage : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// NPC ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public int NpcId { get; set; }

        /// <summary>
        /// NPC名称
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string NpcName { get; set; } = "";

        /// <summary>
        /// NPC类型
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public NpcType NpcType { get; set; }

        /// <summary>
        /// 当前位置
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public Position CurrentPosition { get; set; } = new();

        /// <summary>
        /// 当前生命值
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public int CurrentHp { get; set; }

        /// <summary>
        /// 最大生命值
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public int MaxHp { get; set; }

        /// <summary>
        /// NPC等级
        /// </summary>
        [MemoryPackOrder(6)]
        [Id(6)]
        public int Level { get; set; }

        /// <summary>
        /// 行为状态
        /// </summary>
        [MemoryPackOrder(7)]
        [Id(7)]
        public NpcBehavior Behavior { get; set; }

        /// <summary>
        /// 仇恨列表
        /// </summary>
        [MemoryPackOrder(8)]
        [Id(8)]
        public Dictionary<ulong, int> AggroList { get; set; } = new();

        /// <summary>
        /// 同步时间戳
        /// </summary>
        [MemoryPackOrder(9)]
        [Id(9)]
        public long Timestamp { get; set; }

        [MemoryPackOrder(10)]
        [Id(10)]
        public MessageType Type { get; set; } = MessageType.System;
        [MemoryPackOrder(11)]
        [Id(11)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    /// <summary>
    /// NPC类型
    /// </summary>
    public enum NpcType : byte
    {
        /// <summary>
        /// 普通怪物
        /// </summary>
        Monster = 0,

        /// <summary>
        /// BOSS
        /// </summary>
        Boss = 1,

        /// <summary>
        /// NPC商人
        /// </summary>
        Merchant = 2,

        /// <summary>
        /// 任务NPC
        /// </summary>
        QuestGiver = 3,

        /// <summary>
        /// 门派导师
        /// </summary>
        SectMaster = 4,

        /// <summary>
        /// 帮派领袖
        /// </summary>
        GuildMaster = 5,

        /// <summary>
        /// 村民
        /// </summary>
        Villager = 6,

        /// <summary>
        /// 宠物
        /// </summary>
        Pet = 7,

        /// <summary>
        /// 坐骑
        /// </summary>
        Mount = 8,

        /// <summary>
        /// 守卫
        /// </summary>
        Guard = 9,

        /// <summary>
        /// 船夫
        /// </summary>
        Ferryman = 10
    }

    /// <summary>
    /// NPC行为状态
    /// </summary>
    public enum NpcBehavior : byte
    {
        /// <summary>
        /// 闲逛
        /// </summary>
        Idle = 0,

        /// <summary>
        /// 巡逻
        /// </summary>
        Patrol = 1,

        /// <summary>
        /// 追击
        /// </summary>
        Chase = 2,

        /// <summary>
        /// 战斗
        /// </summary>
        Combat = 3,

        /// <summary>
        /// 逃跑
        /// </summary>
        Flee = 4,

        /// <summary>
        /// 休息
        /// </summary>
        Rest = 5,

        /// <summary>
        /// 服务
        /// </summary>
        Service = 6,

        /// <summary>
        /// 交谈
        /// </summary>
        Talk = 7,

        /// <summary>
        /// 传送
        /// </summary>
        Teleport = 8
    }

    /// <summary>
    /// 物品掉落消息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class ItemDropMessage : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 掉落ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public int DropId { get; set; }

        /// <summary>
        /// 物品ID
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public int ItemId { get; set; }

        /// <summary>
        /// 物品数量
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public int Quantity { get; set; }

        /// <summary>
        /// 掉落位置
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public Position DropPosition { get; set; } = new();

        /// <summary>
        /// 掉落时间
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public long DropTime { get; set; }

        /// <summary>
        /// 掉落来源
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public ulong SourceId { get; set; }

        /// <summary>
        /// 可拾取玩家列表
        /// </summary>
        [MemoryPackOrder(6)]
        [Id(6)]
        public List<ulong> EligiblePlayers { get; set; } = new();

        /// <summary>
        /// 拾取期限（毫秒）
        /// </summary>
        [MemoryPackOrder(7)]
        [Id(7)]
        public long ExpiryTime { get; set; }

        [MemoryPackOrder(8)]
        [Id(8)]
        public MessageType Type { get; set; } = MessageType.Inventory;
        [MemoryPackOrder(9)]
        [Id(9)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    #endregion
}