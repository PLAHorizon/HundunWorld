using MemoryPack;
using Orleans;
using System;
using System.Collections.Generic;

namespace Horizon.Game.Message.Network
{
    #region 区域管理数据模型

    /// <summary>
    /// 场景实例信息
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class SceneInstanceInfo
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public long InstanceId { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public string SceneName { get; set; } = "";

        [MemoryPackOrder(2)]
        [Id(2)]
        public int MaxPlayers { get; set; }

        [MemoryPackOrder(3)]
        [Id(3)]
        public int CurrentPlayers { get; set; }

        [MemoryPackOrder(4)]
        [Id(4)]
        public HashSet<Guid> Players { get; set; } = new();

        [MemoryPackOrder(5)]
        [Id(5)]
        public DateTime CreatedTime { get; set; }

        [MemoryPackOrder(6)]
        [Id(6)]
        public bool IsActive { get; set; } = true;
    }

    /// <summary>
    /// 传送结果
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class TeleportResult
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public bool Success { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public string Message { get; set; } = "";

        [MemoryPackOrder(2)]
        [Id(2)]
        public int TargetAreaId { get; set; }

        [MemoryPackOrder(3)]
        [Id(3)]
        public long TargetInstanceId { get; set; }
    }

    /// <summary>
    /// 区域信息
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class AreaInfo
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public int AreaId { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public string AreaName { get; set; } = "";

        [MemoryPackOrder(2)]
        [Id(2)]
        public string AreaType { get; set; } = "";

        [MemoryPackOrder(3)]
        [Id(3)]
        public int MaxPlayers { get; set; }

        [MemoryPackOrder(4)]
        [Id(4)]
        public int TotalPlayers { get; set; }

        [MemoryPackOrder(5)]
        [Id(5)]
        public int InstanceCount { get; set; }

        [MemoryPackOrder(6)]
        [Id(6)]
        public bool IsInitialized { get; set; }
    }

    #endregion

    #region 活动系统数据模型

    /// <summary>
    /// 活动信息
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class ActivityInfo
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public int ActivityId { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public string Name { get; set; } = "";

        [MemoryPackOrder(2)]
        [Id(2)]
        public string Description { get; set; } = "";

        [MemoryPackOrder(3)]
        [Id(3)]
        public DateTime StartTime { get; set; }

        [MemoryPackOrder(4)]
        [Id(4)]
        public DateTime EndTime { get; set; }

        [MemoryPackOrder(5)]
        [Id(5)]
        public int MaxParticipants { get; set; }

        [MemoryPackOrder(6)]
        [Id(6)]
        public int CurrentParticipants { get; set; }

        [MemoryPackOrder(7)]
        [Id(7)]
        public int Status { get; set; }

        [MemoryPackOrder(8)]
        [Id(8)]
        public bool IsCreated { get; set; }
    }

    /// <summary>
    /// 活动参与记录
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class ActivityParticipation
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public Guid PlayerId { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public DateTime JoinTime { get; set; }

        [MemoryPackOrder(2)]
        [Id(2)]
        public List<RewardRecord> Rewards { get; set; } = new();

        [MemoryPackOrder(3)]
        [Id(3)]
        public bool IsActive { get; set; } = true;
    }

    /// <summary>
    /// 奖励记录
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class RewardRecord
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public int RewardTemplateId { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public int Quantity { get; set; }

        [MemoryPackOrder(2)]
        [Id(2)]
        public DateTime DistributedTime { get; set; }
    }

    #endregion

    #region 交易系统数据模型

    /// <summary>
    /// 交易信息
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class TradeInfo
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public Guid TradeId { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public Guid SellerId { get; set; }

        [MemoryPackOrder(2)]
        [Id(2)]
        public Guid BuyerId { get; set; }

        [MemoryPackOrder(3)]
        [Id(3)]
        public List<TradeItem> SellerItems { get; set; } = new();

        [MemoryPackOrder(4)]
        [Id(4)]
        public List<TradeItem> BuyerItems { get; set; } = new();

        [MemoryPackOrder(5)]
        [Id(5)]
        public long SellerCurrency { get; set; }

        [MemoryPackOrder(6)]
        [Id(6)]
        public long BuyerCurrency { get; set; }

        [MemoryPackOrder(7)]
        [Id(7)]
        public bool SellerConfirmed { get; set; }

        [MemoryPackOrder(8)]
        [Id(8)]
        public bool BuyerConfirmed { get; set; }

        [MemoryPackOrder(9)]
        [Id(9)]
        public int Status { get; set; }

        [MemoryPackOrder(10)]
        [Id(10)]
        public DateTime CreatedTime { get; set; }
    }

    /// <summary>
    /// 交易物品
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class TradeItem
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public long ItemId { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public int Quantity { get; set; }

        [MemoryPackOrder(2)]
        [Id(2)]
        public string ItemName { get; set; } = "";
    }

    /// <summary>
    /// 交易结果
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class TradeResult
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public bool Success { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public string Message { get; set; } = "";

        [MemoryPackOrder(2)]
        [Id(2)]
        public Guid TradeId { get; set; }

        [MemoryPackOrder(3)]
        [Id(3)]
        public long TotalAmount { get; set; }

        [MemoryPackOrder(4)]
        [Id(4)]
        public long Tax { get; set; }
    }

    /// <summary>
    /// 市场商品信息
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class MarketListing
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public long ListingId { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public Guid SellerId { get; set; }

        [MemoryPackOrder(2)]
        [Id(2)]
        public string SellerName { get; set; } = "";

        [MemoryPackOrder(3)]
        [Id(3)]
        public long ItemId { get; set; }

        [MemoryPackOrder(4)]
        [Id(4)]
        public string ItemName { get; set; } = "";

        [MemoryPackOrder(5)]
        [Id(5)]
        public int Quantity { get; set; }

        [MemoryPackOrder(6)]
        [Id(6)]
        public long Price { get; set; }

        [MemoryPackOrder(7)]
        [Id(7)]
        public int CurrencyType { get; set; }

        [MemoryPackOrder(8)]
        [Id(8)]
        public DateTime ListTime { get; set; }

        [MemoryPackOrder(9)]
        [Id(9)]
        public int Status { get; set; }

        [MemoryPackOrder(10)]
        [Id(10)]
        public int Category { get; set; }
    }

    /// <summary>
    /// 市场统计信息
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class MarketStats
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public int TotalListings { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public long TotalTransactions { get; set; }

        [MemoryPackOrder(2)]
        [Id(2)]
        public long TotalVolume { get; set; }

        [MemoryPackOrder(3)]
        [Id(3)]
        public int ActiveListings { get; set; }
    }

    #endregion

    #region 合成系统数据模型

    /// <summary>
    /// 合成结果
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class CraftingResult
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public bool Success { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public int RecipeId { get; set; }

        [MemoryPackOrder(2)]
        [Id(2)]
        public string Message { get; set; } = "";

        [MemoryPackOrder(3)]
        [Id(3)]
        public long OutputItemId { get; set; }

        /// <summary>
        /// 品质 (0=普通, 1=精良, 2=稀有, 3=史诗, 4=传说)
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public int Quality { get; set; }
    }

    /// <summary>
    /// 合成历史记录
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class CraftingHistoryEntry
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public int RecipeId { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public bool Success { get; set; }

        [MemoryPackOrder(2)]
        [Id(2)]
        public DateTime Timestamp { get; set; }

        [MemoryPackOrder(3)]
        [Id(3)]
        public long OutputItemId { get; set; }

        /// <summary>
        /// 品质 (0=普通, 1=精良, 2=稀有, 3=史诗, 4=传说)
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public int Quality { get; set; }
    }

    /// <summary>
    /// 炼丹配方
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class AlchemyRecipe
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public int RecipeId { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public string Name { get; set; } = "";

        [MemoryPackOrder(2)]
        [Id(2)]
        public int RequiredPrimaryElement { get; set; }

        [MemoryPackOrder(3)]
        [Id(3)]
        public int RequiredSecondaryElement { get; set; }

        [MemoryPackOrder(4)]
        [Id(4)]
        public long OutputItemId { get; set; }

        [MemoryPackOrder(5)]
        [Id(5)]
        public float BaseProficiencyGain { get; set; }

        [MemoryPackOrder(6)]
        [Id(6)]
        public float MinProficiency { get; set; }
    }

    /// <summary>
    /// 炼丹结果
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class AlchemyResult
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public bool Success { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public int RecipeId { get; set; }

        [MemoryPackOrder(2)]
        [Id(2)]
        public string Message { get; set; } = "";

        [MemoryPackOrder(3)]
        [Id(3)]
        public long OutputItemId { get; set; }

        /// <summary>
        /// 品质 (0=普通, 1=精良, 2=稀有, 3=史诗, 4=传说)
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public int Quality { get; set; }

        [MemoryPackOrder(5)]
        [Id(5)]
        public float ProficiencyGain { get; set; }

        [MemoryPackOrder(6)]
        [Id(6)]
        public float ElementalHarmony { get; set; }
    }

    /// <summary>
    /// 炼丹历史记录
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class AlchemyHistoryEntry
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public int RecipeId { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public bool Success { get; set; }

        [MemoryPackOrder(2)]
        [Id(2)]
        public DateTime Timestamp { get; set; }

        [MemoryPackOrder(3)]
        [Id(3)]
        public long OutputItemId { get; set; }

        /// <summary>
        /// 品质 (0=普通, 1=精良, 2=稀有, 3=史诗, 4=传说)
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public int Quality { get; set; }

        [MemoryPackOrder(5)]
        [Id(5)]
        public int PrimaryElement { get; set; }

        [MemoryPackOrder(6)]
        [Id(6)]
        public int SecondaryElement { get; set; }
    }

    /// <summary>
    /// 战斗日志条目
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class CombatLogEntry
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public DateTime Timestamp { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public ulong AttackerId { get; set; }

        [MemoryPackOrder(2)]
        [Id(2)]
        public ulong DefenderId { get; set; }

        [MemoryPackOrder(3)]
        [Id(3)]
        public float DamageDealt { get; set; }

        [MemoryPackOrder(4)]
        [Id(4)]
        public int SkillId { get; set; }

        [MemoryPackOrder(5)]
        [Id(5)]
        public int ElementType { get; set; }

        [MemoryPackOrder(6)]
        [Id(6)]
        public bool IsCritical { get; set; }

        [MemoryPackOrder(7)]
        [Id(7)]
        public bool IsDodged { get; set; }

        [MemoryPackOrder(8)]
        [Id(8)]
        public bool IsBlocked { get; set; }

        [MemoryPackOrder(9)]
        [Id(9)]
        public CombatLogType LogType { get; set; }
    }

    #endregion

    #region 任务系统数据模型

    /// <summary>
    /// 任务数据
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class QuestData
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public int QuestId { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public string QuestName { get; set; } = "";

        [MemoryPackOrder(2)]
        [Id(2)]
        public string Description { get; set; } = "";

        /// <summary>
        /// 任务类型 (0=主线, 1=支线, 2=日常, 3=周常)
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public int QuestType { get; set; }

        [MemoryPackOrder(4)]
        [Id(4)]
        public int Level { get; set; }

        /// <summary>
        /// 任务状态 (0=进行中, 1=可提交, 2=已完成, 3=已放弃)
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public int Status { get; set; }

        [MemoryPackOrder(6)]
        [Id(6)]
        public List<QuestObjectiveData> Objectives { get; set; } = new();

        [MemoryPackOrder(7)]
        [Id(7)]
        public Dictionary<string, int> Rewards { get; set; } = new();

        [MemoryPackOrder(8)]
        [Id(8)]
        public DateTime AcceptTime { get; set; }

        [MemoryPackOrder(9)]
        [Id(9)]
        public DateTime? CompleteTime { get; set; }
    }

    /// <summary>
    /// 任务目标数据
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class QuestObjectiveData
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public string ObjectiveType { get; set; } = "";

        [MemoryPackOrder(1)]
        [Id(1)]
        public string Description { get; set; } = "";

        [MemoryPackOrder(2)]
        [Id(2)]
        public int RequiredCount { get; set; }

        [MemoryPackOrder(3)]
        [Id(3)]
        public int CurrentCount { get; set; }

        [MemoryPackOrder(4)]
        [Id(4)]
        public bool IsCompleted { get; set; }
    }

    /// <summary>
    /// 任务完成结果
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class QuestCompleteResult
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public bool Success { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public string Message { get; set; } = "";

        [MemoryPackOrder(2)]
        [Id(2)]
        public int QuestId { get; set; }

        [MemoryPackOrder(3)]
        [Id(3)]
        public Dictionary<string, int> Rewards { get; set; } = new();
    }

    #endregion

    #region 副本系统数据模型

    /// <summary>
    /// 副本数据
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class DungeonData
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public int DungeonTemplateId { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public string DungeonName { get; set; } = "";

        /// <summary>
        /// 难度 (0=普通, 1=困难, 2=英雄, 3=地狱)
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public int Difficulty { get; set; }

        [MemoryPackOrder(3)]
        [Id(3)]
        public int MaxPlayers { get; set; }

        [MemoryPackOrder(4)]
        [Id(4)]
        public int CurrentPlayers { get; set; }

        /// <summary>
        /// 副本状态 (0=等待中, 1=进行中, 2=已完成, 3=失败)
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public int Status { get; set; }

        [MemoryPackOrder(6)]
        [Id(6)]
        public int TimeLimitMinutes { get; set; }

        [MemoryPackOrder(7)]
        [Id(7)]
        public DateTime? StartTime { get; set; }

        [MemoryPackOrder(8)]
        [Id(8)]
        public bool IsCreated { get; set; }

        [MemoryPackOrder(9)]
        [Id(9)]
        public List<DungeonBossData> Bosses { get; set; } = new();

        [MemoryPackOrder(10)]
        [Id(10)]
        public int DefeatedBossCount { get; set; }

        /// <summary>
        /// 关联的队伍ID（组队副本使用）
        /// </summary>
        [MemoryPackOrder(11)]
        [Id(11)]
        public Guid? TeamId { get; set; }
    }

    /// <summary>
    /// 副本Boss数据
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class DungeonBossData
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public int BossId { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public string BossName { get; set; } = "";

        [MemoryPackOrder(2)]
        [Id(2)]
        public bool IsDefeated { get; set; }

        [MemoryPackOrder(3)]
        [Id(3)]
        public DateTime? DefeatTime { get; set; }
    }

    /// <summary>
    /// 副本完成结果
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class DungeonCompleteResult
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public bool Success { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public string Message { get; set; } = "";

        [MemoryPackOrder(2)]
        [Id(2)]
        public int DungeonTemplateId { get; set; }

        [MemoryPackOrder(3)]
        [Id(3)]
        public int Difficulty { get; set; }

        [MemoryPackOrder(4)]
        [Id(4)]
        public int TotalBosses { get; set; }

        [MemoryPackOrder(5)]
        [Id(5)]
        public int DefeatedBosses { get; set; }

        /// <summary>
        /// 通关时间（秒）
        /// </summary>
        [MemoryPackOrder(6)]
        [Id(6)]
        public double ClearTimeSeconds { get; set; }
    }

    #endregion

    #region 社交系统数据模型

    /// <summary>
    /// 组队副本入口结果
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class TeamDungeonResult
    {
        /// <summary>是否成功</summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public bool Success { get; set; }

        /// <summary>结果消息</summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string Message { get; set; } = "";

        /// <summary>副本实例ID（用于后续操作）</summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public Guid DungeonInstanceId { get; set; }

        /// <summary>进入副本的成员ID列表</summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public List<Guid> EnteredMembers { get; set; } = new();

        /// <summary>副本模板ID</summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public int DungeonTemplateId { get; set; }

        /// <summary>难度</summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public int Difficulty { get; set; }
    }

    #endregion

    #region 事件系统数据模型

    /// <summary>
    /// 事件处理统计信息
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class EventProcessingStats
    {
        /// <summary>总处理事件数</summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public long TotalEventsProcessed { get; set; }

        /// <summary>各事件类型处理计数</summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public Dictionary<int, long> EventTypeCounters { get; set; } = new();

        /// <summary>处理失败计数</summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public long FailedEvents { get; set; }

        /// <summary>最后处理事件时间（UTC Ticks）</summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public long LastProcessedTimestamp { get; set; }

        /// <summary>订阅的命名空间</summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public string Namespace { get; set; } = string.Empty;

        /// <summary>统计起始时间（UTC Ticks）</summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public long StatsStartTimestamp { get; set; }
    }

    /// <summary>
    /// 已处理事件摘要
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class ProcessedEventSummary
    {
        /// <summary>事件ID</summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public string EventId { get; set; } = string.Empty;

        /// <summary>事件类型</summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public GameEventType EventType { get; set; }

        /// <summary>触发角色ID</summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public ulong CharacterId { get; set; }

        /// <summary>处理时间（UTC Ticks）</summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public long ProcessedTimestamp { get; set; }

        /// <summary>是否处理成功</summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public bool Success { get; set; }

        /// <summary>事件描述</summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public string Description { get; set; } = string.Empty;
    }

    /// <summary>
    /// 游戏事件基类 — 通过Orleans Stream发布的事件消息
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class GameEvent
    {
        /// <summary>事件唯一ID</summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public string EventId { get; set; } = Guid.NewGuid().ToString("N");

        /// <summary>事件类型</summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public GameEventType EventType { get; set; }

        /// <summary>事件发生时间（UTC Ticks）</summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public long Timestamp { get; set; } = DateTime.UtcNow.Ticks;

        /// <summary>触发事件的角色ID（0表示系统事件）</summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public ulong CharacterId { get; set; }

        /// <summary>事件描述</summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public string Description { get; set; } = string.Empty;

        /// <summary>附加数据（JSON格式的扩展信息）</summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public Dictionary<string, string> Metadata { get; set; } = new();
    }

    #endregion

    #region 排行榜数据模型

    /// <summary>
    /// 排行榜条目
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class RankingEntry
    {
        /// <summary>排名</summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public int Rank { get; set; }

        /// <summary>玩家ID</summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public Guid PlayerId { get; set; }

        /// <summary>玩家名称</summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public string PlayerName { get; set; } = "";

        /// <summary>分数</summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public long Score { get; set; }

        /// <summary>更新时间</summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public DateTime UpdateTime { get; set; }
    }

    /// <summary>
    /// 排行榜信息
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class RankingInfo
    {
        /// <summary>排行榜类型</summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public int RankingType { get; set; }

        /// <summary>排行榜名称</summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string RankingName { get; set; } = "";

        /// <summary>排行榜条目列表</summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public List<RankingEntry> Entries { get; set; } = new();

        /// <summary>最大排名数</summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public int MaxEntries { get; set; } = 100;

        /// <summary>最后更新时间</summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public DateTime LastUpdateTime { get; set; }
    }

    #endregion

    #region 邮件数据模型

    /// <summary>
    /// 邮件数据
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class MailData
    {
        /// <summary>邮件ID</summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public long MailId { get; set; }

        /// <summary>发件人ID</summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public Guid SenderId { get; set; }

        /// <summary>发件人名称</summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public string SenderName { get; set; } = "";

        /// <summary>邮件标题</summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public string Title { get; set; } = "";

        /// <summary>邮件内容</summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public string Content { get; set; } = "";

        /// <summary>邮件类型</summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public int MailType { get; set; }

        /// <summary>邮件状态</summary>
        [MemoryPackOrder(6)]
        [Id(6)]
        public int Status { get; set; } = (int)Network.MailStatus.Unread;

        /// <summary>附件物品列表（模板ID -> 数量）</summary>
        [MemoryPackOrder(7)]
        [Id(7)]
        public Dictionary<int, int> Attachments { get; set; } = new();

        /// <summary>附件货币</summary>
        [MemoryPackOrder(8)]
        [Id(8)]
        public long AttachedCurrency { get; set; }

        /// <summary>发送时间</summary>
        [MemoryPackOrder(9)]
        [Id(9)]
        public DateTime SendTime { get; set; }

        /// <summary>过期时间</summary>
        [MemoryPackOrder(10)]
        [Id(10)]
        public DateTime ExpireTime { get; set; }
    }

    /// <summary>
    /// 发送邮件结果
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class SendMailResult
    {
        /// <summary>是否成功</summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public bool Success { get; set; }

        /// <summary>消息</summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string Message { get; set; } = "";

        /// <summary>邮件ID</summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public long MailId { get; set; }
    }

    #endregion

    #region 成就数据模型

    /// <summary>
    /// 成就定义
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class AchievementData
    {
        /// <summary>成就ID</summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public int AchievementId { get; set; }

        /// <summary>成就名称</summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string Name { get; set; } = "";

        /// <summary>成就描述</summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public string Description { get; set; } = "";

        /// <summary>成就分类</summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public int Category { get; set; }

        /// <summary>成就点数</summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public int Points { get; set; }

        /// <summary>是否已解锁</summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public bool IsUnlocked { get; set; }

        /// <summary>当前进度</summary>
        [MemoryPackOrder(6)]
        [Id(6)]
        public int CurrentProgress { get; set; }

        /// <summary>目标进度</summary>
        [MemoryPackOrder(7)]
        [Id(7)]
        public int TargetProgress { get; set; }

        /// <summary>解锁时间</summary>
        [MemoryPackOrder(8)]
        [Id(8)]
        public DateTime? UnlockTime { get; set; }

        /// <summary>奖励（物品模板ID -> 数量）</summary>
        [MemoryPackOrder(9)]
        [Id(9)]
        public Dictionary<string, int> Rewards { get; set; } = new();
    }

    /// <summary>
    /// 成就解锁结果
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class AchievementUnlockResult
    {
        /// <summary>是否成功</summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public bool Success { get; set; }

        /// <summary>消息</summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string Message { get; set; } = "";

        /// <summary>成就ID</summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public int AchievementId { get; set; }

        /// <summary>获得的成就点数</summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public int PointsEarned { get; set; }

        /// <summary>奖励</summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public Dictionary<string, int> Rewards { get; set; } = new();
    }

    #endregion
}
