using Horizon.Game.Message.Enums;
using MemoryPack;
using Orleans;
using System;
using System.Collections.Generic;

namespace Horizon.Game.Message.Network
{
    #region 战斗系统状态

    /// <summary>
    /// 战斗状态
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class CombatState
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public Dictionary<ulong, CombatInfo> CombatParticipants { get; set; } = new Dictionary<ulong, CombatInfo>();

        [MemoryPackOrder(1)]
        [Id(1)]
        public Dictionary<ulong, EffectInfo> ActiveEffects { get; set; } = new Dictionary<ulong, EffectInfo>();

        [MemoryPackOrder(2)]
        [Id(2)]
        public List<CombatLogEntry> CombatLog { get; set; } = new List<CombatLogEntry>();
    }

    /// <summary>
    /// 战斗信息
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class CombatInfo
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong CharacterId { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public bool IsInCombat { get; set; }

        [MemoryPackOrder(2)]
        [Id(2)]
        public ulong TargetId { get; set; }

        [MemoryPackOrder(3)]
        [Id(3)]
        public DateTime LastActionTime { get; set; }

        [MemoryPackOrder(4)]
        [Id(4)]
        public float Health { get; set; }

        [MemoryPackOrder(5)]
        [Id(5)]
        public float MaxHealth { get; set; }

        [MemoryPackOrder(6)]
        [Id(6)]
        public float AttackPower { get; set; }

        [MemoryPackOrder(7)]
        [Id(7)]
        public float Defense { get; set; }

        [MemoryPackOrder(8)]
        [Id(8)]
        public int WuxingElement { get; set; } // 0=无, 1=金, 2=木, 3=水, 4=火, 5=土

        [MemoryPackOrder(9)]
        [Id(9)]
        public float Energy { get; set; }

        [MemoryPackOrder(10)]
        [Id(10)]
        public float MaxEnergy { get; set; }

        [MemoryPackOrder(11)]
        [Id(11)]
        public float DodgeRate { get; set; }

        [MemoryPackOrder(12)]
        [Id(12)]
        public float BlockRate { get; set; }

        [MemoryPackOrder(13)]
        [Id(13)]
        public float CritRate { get; set; } = 0.1f;

        [MemoryPackOrder(14)]
        [Id(14)]
        public float CritDamageMultiplier { get; set; } = 1.5f;

        /// <summary>
        /// 技能冷却记录（技能ID -> 上次施放时间）
        /// </summary>
        [MemoryPackOrder(15)]
        [Id(15)]
        public Dictionary<int, DateTime> SkillCooldowns { get; set; } = new();
    }

    /// <summary>
    /// 效果信息
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class EffectInfo
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public int EffectId { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public string EffectName { get; set; }

        [MemoryPackOrder(2)]
        [Id(2)]
        public ulong TargetId { get; set; }

        [MemoryPackOrder(3)]
        [Id(3)]
        public ulong SourceId { get; set; }

        [MemoryPackOrder(4)]
        [Id(4)]
        public float Duration { get; set; }

        [MemoryPackOrder(5)]
        [Id(5)]
        public float RemainingDuration { get; set; }

        [MemoryPackOrder(6)]
        [Id(6)]
        public float Intensity { get; set; }

        [MemoryPackOrder(7)]
        [Id(7)]
        public int StackCount { get; set; }

        [MemoryPackOrder(8)]
        [Id(8)]
        public EffectType Type { get; set; }
    }

    #endregion

    #region 角色状态

    /// <summary>
    /// Represents the state of a character.
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class CharacterState
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public CharacterInfo CharacterInfo { get; set; }

        // 注意：IsOnline 已从持久化状态中移除。
        // 角色在线状态不再由 Orleans GrainStorage 持久化，而是使用 Redis presence key（TTL 90 秒）
        // 作为权威源，避免离线后因持久化状态残留导致角色永远显示在线的严重 BUG。
        // 如需内存缓存，请在 CharacterGrain 中使用私有字段。
    }

    #endregion

    #region 背包状态

    /// <summary>
    /// 背包状态
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class InventoryState
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public Dictionary<long, ItemInfo> Items { get; set; } = new();

        [MemoryPackOrder(1)]
        [Id(1)]
        public int Capacity { get; set; } = 50;

        [MemoryPackOrder(2)]
        [Id(2)]
        public long NextItemId { get; set; } = 1;

        [MemoryPackOrder(3)]
        [Id(3)]
        public Dictionary<int, long> EquippedItems { get; set; } = new();
    }

    #endregion

    #region 技能状态

    /// <summary>
    /// 技能状态
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class SkillState
    {
        /// <summary>
        /// 已学习技能列表（技能ID -> 技能信息）
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public Dictionary<int, SkillInfo> LearnedSkills { get; set; } = new();

        /// <summary>
        /// 技能冷却记录（技能ID -> 上次施放时间）
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public Dictionary<int, DateTime> SkillCooldowns { get; set; } = new();

        /// <summary>
        /// 可用技能点
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public int SkillPoints { get; set; } = 0;

        /// <summary>
        /// 已使用技能点总数
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public int TotalSkillPointsUsed { get; set; } = 0;

        /// <summary>
        /// 技能前置依赖（技能ID -> 前置技能ID列表）
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public Dictionary<int, List<int>> SkillDependencies { get; set; } = new();
    }

    #endregion

    #region 合成系统状态

    /// <summary>
    /// 合成系统状态
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class CraftingState
    {
        /// <summary>
        /// 已学习配方（配方ID -> 配方信息）
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public Dictionary<int, CraftingRecipe> LearnedRecipes { get; set; } = new();

        /// <summary>
        /// 合成历史记录
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public List<CraftingHistoryEntry> CraftingHistory { get; set; } = new();
    }

    #endregion

    #region 炼丹系统状态

    /// <summary>
    /// 炼丹系统状态
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class AlchemyState
    {
        /// <summary>
        /// 已学习配方（配方ID -> 配方信息）
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public Dictionary<int, AlchemyRecipe> LearnedRecipes { get; set; } = new();

        /// <summary>
        /// 炼丹熟练度
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public float Proficiency { get; set; }

        /// <summary>
        /// 炼丹历史记录
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public List<AlchemyHistoryEntry> AlchemyHistory { get; set; } = new();
    }

    #endregion

    #region 社交系统状态

    /// <summary>
    /// 社交系统状态
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class SocialState
    {
        /// <summary>
        /// 好友列表（好友ID -> 好友信息）
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public Dictionary<Guid, FriendInfo> Friends { get; set; } = new();

        /// <summary>
        /// 好友申请列表（申请ID -> 申请信息）
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public Dictionary<Guid, FriendRequest> FriendRequests { get; set; } = new();

        /// <summary>
        /// 聊天历史（频道类型 -> 消息列表）
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public Dictionary<int, List<ChatMessage>> ChatHistory { get; set; } = new();

        /// <summary>
        /// 黑名单
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public HashSet<Guid> BlockedPlayers { get; set; } = new();

        /// <summary>
        /// 最大好友数量
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public int MaxFriends { get; set; } = 100;

        /// <summary>
        /// 每个频道最大缓存消息数量
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public int MaxChatHistoryPerChannel { get; set; } = 200;
    }

    /// <summary>
    /// 好友申请信息
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class FriendRequest
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public Guid RequestId { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public Guid RequesterId { get; set; }

        [MemoryPackOrder(2)]
        [Id(2)]
        public Guid TargetId { get; set; }

        [MemoryPackOrder(3)]
        [Id(3)]
        public string Message { get; set; } = "";

        [MemoryPackOrder(4)]
        [Id(4)]
        public long Timestamp { get; set; }
    }

    #endregion

    #region 公会状态

    /// <summary>
    /// 公会状态
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class GuildState
    {
        /// <summary>
        /// 公会名称
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public string GuildName { get; set; } = "";

        /// <summary>
        /// 创建者/帮主ID
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public Guid LeaderId { get; set; }

        /// <summary>
        /// 公会是否已创建
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public bool IsCreated { get; set; }

        /// <summary>
        /// 公会等级
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public int Level { get; set; } = 1;

        /// <summary>
        /// 最大成员数
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public int MaxMembers { get; set; } = 50;

        /// <summary>
        /// 公会宣言
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public string Declaration { get; set; } = "";

        /// <summary>
        /// 成员列表（成员ID -> 成员信息）
        /// </summary>
        [MemoryPackOrder(6)]
        [Id(6)]
        public Dictionary<Guid, GuildMemberState> Members { get; set; } = new();

        /// <summary>
        /// 入会申请列表（申请ID -> 申请信息）
        /// </summary>
        [MemoryPackOrder(7)]
        [Id(7)]
        public Dictionary<Guid, GuildApplication> Applications { get; set; } = new();

        /// <summary>
        /// 公会资源
        /// </summary>
        [MemoryPackOrder(8)]
        [Id(8)]
        public Dictionary<string, int> Resources { get; set; } = new();

        /// <summary>
        /// 创建时间
        /// </summary>
        [MemoryPackOrder(9)]
        [Id(9)]
        public long CreateTime { get; set; }
    }

    /// <summary>
    /// 公会成员状态
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class GuildMemberState
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public Guid MemberId { get; set; }

        /// <summary>
        /// 职位: 0=帮主, 1=副帮主, 2=长老, 3=精英, 4=普通成员
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public int Position { get; set; } = 4;

        [MemoryPackOrder(2)]
        [Id(2)]
        public int Contribution { get; set; }

        [MemoryPackOrder(3)]
        [Id(3)]
        public long JoinTime { get; set; }
    }

    /// <summary>
    /// 公会入会申请
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class GuildApplication
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public Guid ApplicationId { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public Guid PlayerId { get; set; }

        [MemoryPackOrder(2)]
        [Id(2)]
        public string Message { get; set; } = "";

        [MemoryPackOrder(3)]
        [Id(3)]
        public long Timestamp { get; set; }
    }

    #endregion

    #region 队伍状态

    /// <summary>
    /// 队伍状态
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class TeamState
    {
        /// <summary>
        /// 队伍名称
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public string TeamName { get; set; } = "";

        /// <summary>
        /// 队长ID
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public Guid LeaderId { get; set; }

        /// <summary>
        /// 队伍是否已创建
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public bool IsCreated { get; set; }

        /// <summary>
        /// 队伍目标描述
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public string TeamGoal { get; set; } = "";

        /// <summary>
        /// 最大成员数
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public int MaxMembers { get; set; } = 5;

        /// <summary>
        /// 成员列表（成员ID -> 成员信息）
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public Dictionary<Guid, TeamMemberState> Members { get; set; } = new();

        /// <summary>
        /// 创建时间
        /// </summary>
        [MemoryPackOrder(6)]
        [Id(6)]
        public long CreateTime { get; set; }

        /// <summary>
        /// 状态版本号（每次状态变更时递增，用于队伍状态同步）
        /// </summary>
        [MemoryPackOrder(7)]
        [Id(7)]
        public long StateVersion { get; set; }

        /// <summary>
        /// 当前关联的副本实例ID（队伍正在进行的副本）
        /// </summary>
        [MemoryPackOrder(8)]
        [Id(8)]
        public Guid? CurrentDungeonId { get; set; }
    }

    /// <summary>
    /// 队伍成员状态
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class TeamMemberState
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public Guid MemberId { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public bool IsLeader { get; set; }

        [MemoryPackOrder(2)]
        [Id(2)]
        public long JoinTime { get; set; }
    }

    #endregion

    #region 交易系统状态

    /// <summary>
    /// 交易系统状态
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class TradeState
    {
        /// <summary>
        /// 卖方ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public Guid SellerId { get; set; }

        /// <summary>
        /// 买方ID
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public Guid BuyerId { get; set; }

        /// <summary>
        /// 卖方物品列表
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public List<TradeItem> SellerItems { get; set; } = new();

        /// <summary>
        /// 买方物品列表
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public List<TradeItem> BuyerItems { get; set; } = new();

        /// <summary>
        /// 卖方出价货币
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public long SellerCurrency { get; set; }

        /// <summary>
        /// 买方出价货币
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public long BuyerCurrency { get; set; }

        /// <summary>
        /// 卖方是否确认
        /// </summary>
        [MemoryPackOrder(6)]
        [Id(6)]
        public bool SellerConfirmed { get; set; }

        /// <summary>
        /// 买方是否确认
        /// </summary>
        [MemoryPackOrder(7)]
        [Id(7)]
        public bool BuyerConfirmed { get; set; }

        /// <summary>
        /// 交易状态
        /// </summary>
        [MemoryPackOrder(8)]
        [Id(8)]
        public int Status { get; set; } = (int)TradeStatus.Created;

        /// <summary>
        /// 创建时间
        /// </summary>
        [MemoryPackOrder(9)]
        [Id(9)]
        public DateTime CreatedTime { get; set; }

        /// <summary>
        /// 是否已创建
        /// </summary>
        [MemoryPackOrder(10)]
        [Id(10)]
        public bool IsCreated { get; set; }
    }

    /// <summary>
    /// 市场系统状态
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class MarketState
    {
        /// <summary>
        /// 商品列表（ListingId -> 商品信息）
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public Dictionary<long, MarketListing> Listings { get; set; } = new();

        /// <summary>
        /// 下一个商品ID
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public long NextListingId { get; set; } = 1;

        /// <summary>
        /// 总交易次数
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public long TotalTransactions { get; set; }

        /// <summary>
        /// 总交易额
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public long TotalVolume { get; set; }
    }

    #endregion

    #region 任务系统状态

    /// <summary>
    /// 任务系统状态
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class QuestState
    {
        /// <summary>
        /// 进行中的任务 (QuestId -> QuestData)
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public Dictionary<int, QuestData> ActiveQuests { get; set; } = new();

        /// <summary>
        /// 已完成的任务 (QuestId -> QuestData)
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public Dictionary<int, QuestData> CompletedQuests { get; set; } = new();

        /// <summary>
        /// 最大同时接受任务数
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public int MaxActiveQuests { get; set; } = 20;
    }

    #endregion

    #region 副本系统状态

    /// <summary>
    /// 副本系统状态
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class DungeonState
    {
        /// <summary>
        /// 副本模板ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public int DungeonTemplateId { get; set; }

        /// <summary>
        /// 副本名称
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string DungeonName { get; set; } = "";

        /// <summary>
        /// 难度 (0=普通, 1=困难, 2=英雄, 3=地狱)
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public int Difficulty { get; set; }

        /// <summary>
        /// 最大玩家数
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public int MaxPlayers { get; set; } = 5;

        /// <summary>
        /// 副本状态
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public int Status { get; set; } = (int)DungeonStatus.Waiting;

        /// <summary>
        /// 时间限制（分钟）
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public int TimeLimitMinutes { get; set; } = 30;

        /// <summary>
        /// 副本开始时间
        /// </summary>
        [MemoryPackOrder(6)]
        [Id(6)]
        public DateTime? StartTime { get; set; }

        /// <summary>
        /// 是否已创建
        /// </summary>
        [MemoryPackOrder(7)]
        [Id(7)]
        public bool IsCreated { get; set; }

        /// <summary>
        /// 当前玩家列表
        /// </summary>
        [MemoryPackOrder(8)]
        [Id(8)]
        public HashSet<Guid> Players { get; set; } = new();

        /// <summary>
        /// Boss列表 (BossId -> BossData)
        /// </summary>
        [MemoryPackOrder(9)]
        [Id(9)]
        public Dictionary<int, DungeonBossData> Bosses { get; set; } = new();

        /// <summary>
        /// 关联的队伍ID（组队副本使用）
        /// </summary>
        [MemoryPackOrder(10)]
        [Id(10)]
        public Guid? TeamId { get; set; }
    }

    #endregion

    #region 区域管理状态

    /// <summary>
    /// 区域管理状态
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class AreaState
    {
        /// <summary>
        /// 区域名称
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public string AreaName { get; set; } = "";

        /// <summary>
        /// 区域类型（如：野外、副本、城镇）
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string AreaType { get; set; } = "";

        /// <summary>
        /// 区域最大玩家数
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public int MaxPlayers { get; set; } = 100;

        /// <summary>
        /// 是否已初始化
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public bool IsInitialized { get; set; }

        /// <summary>
        /// 场景实例列表
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public Dictionary<long, SceneInstanceInfo> Instances { get; set; } = new();

        /// <summary>
        /// 下一个实例ID
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public long NextInstanceId { get; set; } = 1;
    }

    #endregion

    #region 活动系统状态

    /// <summary>
    /// 活动系统状态
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class ActivityState
    {
        /// <summary>
        /// 活动名称
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public string Name { get; set; } = "";

        /// <summary>
        /// 活动描述
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string Description { get; set; } = "";

        /// <summary>
        /// 活动开始时间
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public DateTime StartTime { get; set; }

        /// <summary>
        /// 活动结束时间
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public DateTime EndTime { get; set; }

        /// <summary>
        /// 最大参与人数
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public int MaxParticipants { get; set; }

        /// <summary>
        /// 活动状态
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public int Status { get; set; } = (int)ActivityStatus.NotStarted;

        /// <summary>
        /// 是否已创建
        /// </summary>
        [MemoryPackOrder(6)]
        [Id(6)]
        public bool IsCreated { get; set; }

        /// <summary>
        /// 参与者列表（玩家ID -> 参与记录）
        /// </summary>
        [MemoryPackOrder(7)]
        [Id(7)]
        public Dictionary<Guid, ActivityParticipation> Participants { get; set; } = new();
    }

    #endregion

    #region 游戏服务器状态

    /// <summary>
    /// 游戏服务器状态
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class GameServerState
    {
        /// <summary>
        /// 服务器名称
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public string ServerName { get; set; } = "";

        /// <summary>
        /// 服务器是否已初始化
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public bool IsInitialized { get; set; }

        /// <summary>
        /// 服务器状态
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public int Status { get; set; } = (int)ServerStatus.Normal;

        /// <summary>
        /// 在线人数（已弃用，使用OnlinePlayers.Count代替）
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public int OnlineCount { get; set; }

        /// <summary>
        /// 最大在线人数
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public int MaxOnlineCount { get; set; } = 5000;

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
        /// 网络延迟(ms)
        /// </summary>
        [MemoryPackOrder(7)]
        [Id(7)]
        public long NetworkLatency { get; set; }

        /// <summary>
        /// 维护原因
        /// </summary>
        [MemoryPackOrder(8)]
        [Id(8)]
        public string MaintenanceReason { get; set; } = "";

        /// <summary>
        /// 最后更新时间
        /// </summary>
        [MemoryPackOrder(9)]
        [Id(9)]
        public long LastUpdateTime { get; set; }

        /// <summary>
        /// 在线角色ID集合（持久化）。<br/>
        /// 修复 BUG（角色离线后未能从服务端移除）：原字段类型为 HashSet&lt;Guid&gt;，
        /// 但业务层从未调用 PlayerOnlineAsync/PlayerOfflineAsync 维护此列表，
        /// 导致角色离线后持久化在线信息未更新、角色永久残留。<br/>
        /// 现改为 HashSet&lt;long&gt; 存储 characterId，由 CharacterGrain.EnterGameAsync
        /// 和 GoOfflineAsync 维护，PlayerDespawnScheduler 兜底调用，确保离线立即移除。
        /// </summary>
        [MemoryPackOrder(10)]
        [Id(10)]
        public HashSet<long> OnlinePlayers { get; set; } = new();
    }

    #endregion

    #region 消息频道状态

    /// <summary>
    /// 消息频道状态
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class MessageChannelState
    {
        /// <summary>
        /// 订阅者列表
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public HashSet<long> Subscribers { get; set; } = new();

        /// <summary>
        /// 最近消息缓存
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public List<ChatMessage> RecentMessages { get; set; } = new();

        /// <summary>
        /// 最大缓存消息数
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public int MaxCachedMessages { get; set; } = 100;

        /// <summary>
        /// 消息总数
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public long TotalMessageCount { get; set; }
    }

    /// <summary>
    /// 群组频道状态（公会/队伍共用）
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class GroupChannelState
    {
        /// <summary>
        /// 成员列表
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public HashSet<long> Members { get; set; } = new();

        /// <summary>
        /// 最近消息缓存
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public List<ChatMessage> RecentMessages { get; set; } = new();

        /// <summary>
        /// 最大缓存消息数
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public int MaxCachedMessages { get; set; } = 100;
    }

    /// <summary>
    /// 系统频道状态
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class SystemChannelState
    {
        /// <summary>
        /// 订阅者列表
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public HashSet<long> Subscribers { get; set; } = new();

        /// <summary>
        /// 系统消息缓存
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public List<ChatMessage> SystemMessages { get; set; } = new();

        /// <summary>
        /// 最大缓存消息数
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public int MaxCachedMessages { get; set; } = 50;
    }

    /// <summary>
    /// 消息路由器状态
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class MessageRouterState
    {
        /// <summary>
        /// 已路由消息总数
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public long TotalRoutedMessages { get; set; }

        /// <summary>
        /// 路由失败消息总数
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public long FailedRoutedMessages { get; set; }
    }

    #endregion

    #region 事件消费者状态

    /// <summary>
    /// 事件消费者Grain状态
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class EventConsumerState
    {
        /// <summary>已处理事件统计</summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public EventProcessingStats Stats { get; set; } = new();

        /// <summary>最近处理的事件摘要（最多保留100条）</summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public List<ProcessedEventSummary> RecentEvents { get; set; } = new();
    }

    #endregion

    #region 排行榜状态

    /// <summary>
    /// 排行榜Grain状态
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class RankingState
    {
        /// <summary>排行榜类型</summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public int RankingType { get; set; }

        /// <summary>排行榜名称</summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string RankingName { get; set; } = "";

        /// <summary>排行榜条目（玩家ID -> 条目）</summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public Dictionary<Guid, RankingEntry> Entries { get; set; } = new();

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

    #region 邮箱状态

    /// <summary>
    /// 邮箱Grain状态
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class MailBoxState
    {
        /// <summary>邮件列表（邮件ID -> 邮件数据）</summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public Dictionary<long, MailData> Mails { get; set; } = new();

        /// <summary>下一个邮件ID</summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public long NextMailId { get; set; } = 1;

        /// <summary>最大邮件数</summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public int MaxMails { get; set; } = 100;

        /// <summary>未读邮件数</summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public int UnreadCount { get; set; }
    }

    #endregion

    #region 成就状态

    /// <summary>
    /// 成就Grain状态
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class AchievementState
    {
        /// <summary>已解锁成就（成就ID -> 成就数据）</summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public Dictionary<int, AchievementData> Achievements { get; set; } = new();

        /// <summary>总成就点数</summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public int TotalPoints { get; set; }

        /// <summary>已解锁成就数</summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public int UnlockedCount { get; set; }
    }

    #endregion

    #region 社交系统监控状态

    /// <summary>
    /// 社交系统监控状态
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class SocialSystemMonitorState
    {
        /// <summary>总消息路由数</summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public long TotalMessagesRouted { get; set; }

        /// <summary>总频道数</summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public int TotalChannels { get; set; }

        /// <summary>总活跃用户数</summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public int ActiveUsers { get; set; }

        /// <summary>统计重置时间</summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public long LastResetTime { get; set; }
    }

    #endregion

    #region 数据池状态

    /// <summary>
    /// 数据池状态
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class DataPoolState
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public long LastEntryId { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public DateTime LastWriteTime { get; set; }

        [MemoryPackOrder(2)]
        [Id(2)]
        public int TotalEntries { get; set; }

        [MemoryPackOrder(3)]
        [Id(3)]
        public Dictionary<int, int> EntriesByType { get; set; } = new();
    }

    #endregion

    #region 花卉市场预测状态

    /// <summary>
    /// 花卉市场状态
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class FlowerMarketState
    {
        /// <summary>最新价格快照（品种ID -> 快照）</summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public Dictionary<long, FlowerPriceSnapshot> LatestSnapshots { get; set; } = new();

        /// <summary>最后更新时间</summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public DateTime LastUpdateTime { get; set; }

        /// <summary>活跃品种数量</summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public int ActiveSpeciesCount { get; set; }
    }

    /// <summary>
    /// 花卉品种状态
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class FlowerSpeciesState
    {
        /// <summary>品种ID</summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public long SpeciesId { get; set; }

        /// <summary>品种编码</summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string SpeciesCode { get; set; } = "";

        /// <summary>价格历史（保留最近365天）</summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public List<FlowerPriceSnapshot> PriceHistory { get; set; } = new();

        /// <summary>当前预测</summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public FlowerPriceForecast CurrentForecast { get; set; }

        /// <summary>最后预测时间</summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public DateTime LastPredictionTime { get; set; }
    }

    /// <summary>
    /// 区域需求状态
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class RegionDemandState
    {
        /// <summary>区域ID</summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public int RegionId { get; set; }

        /// <summary>品种需求指数（品种ID -> 搜索指数）</summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public Dictionary<int, double> SpeciesDemandIndex { get; set; } = new();

        /// <summary>最后更新时间</summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public DateTime LastUpdateTime { get; set; }
    }

    /// <summary>
    /// 预警规则状态
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class AlertRuleState
    {
        /// <summary>规则ID</summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public long RuleId { get; set; }

        /// <summary>用户ID（对应PassportId）</summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public Guid UserId { get; set; }

        /// <summary>品种ID</summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public int SpeciesId { get; set; }

        /// <summary>条件类型</summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public AlertConditionType ConditionType { get; set; }

        /// <summary>阈值</summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public decimal ThresholdValue { get; set; }

        /// <summary>是否启用</summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public bool IsEnabled { get; set; }

        /// <summary>最后触发时间</summary>
        [MemoryPackOrder(6)]
        [Id(6)]
        public DateTime LastTriggeredAt { get; set; }

        /// <summary>前一价格快照</summary>
        [MemoryPackOrder(7)]
        [Id(7)]
        public decimal PreviousPrice { get; set; }
    }

    /// <summary>
    /// 通知状态
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class NotificationState
    {
        /// <summary>用户ID（对应PassportId）</summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public Guid UserId { get; set; }

        /// <summary>订阅列表（品种ID -> 渠道列表）</summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public Dictionary<int, List<NotifyChannel>> Subscriptions { get; set; } = new();

        /// <summary>待处理预警</summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public List<AlertMessage> PendingAlerts { get; set; } = new();

        /// <summary>最后推送时间</summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public DateTime LastPushTime { get; set; }

        [MemoryPackOrder(4)]
        [Id(4)]
        public Dictionary<long, DateTime> LastRuleTriggerTime { get; set; } = new();

        [MemoryPackOrder(5)]
        [Id(5)]
        public int SilencePeriodMinutes { get; set; } = 30;

        [MemoryPackOrder(6)]
        [Id(6)]
        public Dictionary<int, bool> EnabledChannels { get; set; } = new();
    }

    /// <summary>
    /// IoT设备状态
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class IoTDeviceState
    {
        /// <summary>设备ID</summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public string DeviceId { get; set; } = "";

        /// <summary>温室ID</summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string GreenhouseId { get; set; } = "";

        /// <summary>最新读数</summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public SensorReading LatestReading { get; set; }

        /// <summary>阈值配置（指标名称 -> 阈值）</summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public Dictionary<string, double> Thresholds { get; set; } = new();

        /// <summary>是否在线</summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public bool IsOnline { get; set; }

        /// <summary>最后心跳时间</summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public DateTime LastHeartbeat { get; set; }

        [MemoryPackOrder(6)]
        [Id(6)]
        public Dictionary<string, string> DesiredProperties { get; set; } = new();

        [MemoryPackOrder(7)]
        [Id(7)]
        public Dictionary<string, string> ReportedProperties { get; set; } = new();
    }

    /// <summary>
    /// 预测调度状态
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class ForecastSchedulerState
    {
        /// <summary>最后每日预测时间</summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public DateTime LastDailyForecastTime { get; set; }

        /// <summary>最后每小时聚合时间</summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public DateTime LastHourlyAggregationTime { get; set; }

        /// <summary>活跃任务数</summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public int ActiveTaskCount { get; set; }

        /// <summary>任务历史（保留最近100条）</summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public List<string> TaskHistory { get; set; } = new();
    }

    #endregion

    #region AI智能分析状态

    /// <summary>
    /// AI分析师状态
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class AIAnalystState
    {
        /// <summary>对话历史（会话ID -> 消息列表）</summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public Dictionary<string, List<AIChatMessage>> Conversations { get; set; } = new();
    }

    /// <summary>
    /// RAG检索状态
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class RAGRetrieverState
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public DateTime LastSearchTime { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public long TotalSearches { get; set; }

        [MemoryPackOrder(2)]
        [Id(2)]
        public bool EnsureIndexInitialized { get; set; }
    }

    /// <summary>
    /// 嵌入状态
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class EmbeddingState
    {
        /// <summary>已嵌入文档总数</summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public long TotalEmbedded { get; set; }

        /// <summary>最后嵌入时间</summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public DateTime LastEmbedTime { get; set; }
    }

    /// <summary>
    /// 知识库状态
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class KnowledgeBaseState
    {
        /// <summary>文档总数</summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public long TotalDocuments { get; set; }

        /// <summary>已索引文档数</summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public long IndexedDocuments { get; set; }

        /// <summary>最后索引时间</summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public DateTime LastIndexTime { get; set; }
    }

    /// <summary>
    /// 报告生成状态
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class ReportGeneratorState
    {
        /// <summary>最后报告日期</summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public DateTime LastReportDate { get; set; }

        /// <summary>总报告数</summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public long TotalReports { get; set; }
    }

    #endregion

    #region 电商系统状态

    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class ProductState
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public long ProductId { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public long MerchantId { get; set; }

        [MemoryPackOrder(2)]
        [Id(2)]
        public int SpeciesId { get; set; }

        [MemoryPackOrder(3)]
        [Id(3)]
        public string ProductName { get; set; } = "";

        [MemoryPackOrder(4)]
        [Id(4)]
        public string Description { get; set; } = "";

        [MemoryPackOrder(5)]
        [Id(5)]
        public decimal Price { get; set; }

        [MemoryPackOrder(6)]
        [Id(6)]
        public int Stock { get; set; }

        [MemoryPackOrder(7)]
        [Id(7)]
        public string Unit { get; set; } = "";

        [MemoryPackOrder(8)]
        [Id(8)]
        public string Images { get; set; } = "";

        [MemoryPackOrder(9)]
        [Id(9)]
        public bool IsActive { get; set; }

        [MemoryPackOrder(10)]
        [Id(10)]
        public int Version { get; set; }

        [MemoryPackOrder(11)]
        [Id(11)]
        public long? CategoryId { get; set; }

        [MemoryPackOrder(12)]
        [Id(12)]
        public long? TypeId { get; set; }

        [MemoryPackOrder(13)]
        [Id(13)]
        public long? BrandId { get; set; }

        [MemoryPackOrder(14)]
        [Id(14)]
        public long? FreightTemplateId { get; set; }

        [MemoryPackOrder(15)]
        [Id(15)]
        public decimal? Weight { get; set; }

        [MemoryPackOrder(16)]
        [Id(16)]
        public decimal? Volume { get; set; }

        [MemoryPackOrder(17)]
        [Id(17)]
        public int MaxBuyCount { get; set; }

        [MemoryPackOrder(18)]
        [Id(18)]
        public bool IsOpenLadder { get; set; }

        [MemoryPackOrder(19)]
        [Id(19)]
        public int ProductType { get; set; }

        [MemoryPackOrder(20)]
        [Id(20)]
        public decimal? MarketPrice { get; set; }

        [MemoryPackOrder(21)]
        [Id(21)]
        public decimal MinSalePrice { get; set; }

        [MemoryPackOrder(22)]
        [Id(22)]
        public int AuditStatus { get; set; }

        [MemoryPackOrder(23)]
        [Id(23)]
        public bool IsPresale { get; set; }

        [MemoryPackOrder(24)]
        [Id(24)]
        public DateTime? PresaleDeliveryDate { get; set; }

        [MemoryPackOrder(25)]
        [Id(25)]
        public long? RelatedBatchId { get; set; }
    }

    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class OrderState
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public long OrderId { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public string OrderNo { get; set; } = "";

        [MemoryPackOrder(2)]
        [Id(2)]
        public Guid BuyerId { get; set; }

        [MemoryPackOrder(3)]
        [Id(3)]
        public long MerchantId { get; set; }

        [MemoryPackOrder(4)]
        [Id(4)]
        public decimal TotalAmount { get; set; }

        [MemoryPackOrder(5)]
        [Id(5)]
        public OrderStatus Status { get; set; }

        [MemoryPackOrder(6)]
        [Id(6)]
        public string PaymentMethod { get; set; } = "";

        [MemoryPackOrder(7)]
        [Id(7)]
        public DateTime? PaymentTime { get; set; }

        [MemoryPackOrder(8)]
        [Id(8)]
        public string ShippingAddress { get; set; } = "";

        [MemoryPackOrder(9)]
        [Id(9)]
        public bool IsPresale { get; set; }

        [MemoryPackOrder(10)]
        [Id(10)]
        public DateTime? PresaleDeliveryDate { get; set; }

        [MemoryPackOrder(11)]
        [Id(11)]
        public List<OrderItemState> Items { get; set; } = new();

        [MemoryPackOrder(12)]
        [Id(12)]
        public string ShipTo { get; set; } = "";

        [MemoryPackOrder(13)]
        [Id(13)]
        public string CellPhone { get; set; } = "";

        [MemoryPackOrder(14)]
        [Id(14)]
        public string ExpressCompanyName { get; set; } = "";

        [MemoryPackOrder(15)]
        [Id(15)]
        public string ShipOrderNumber { get; set; } = "";

        [MemoryPackOrder(16)]
        [Id(16)]
        public decimal Freight { get; set; }

        [MemoryPackOrder(17)]
        [Id(17)]
        public decimal OrderTotalAmount { get; set; }

        [MemoryPackOrder(18)]
        [Id(18)]
        public int RefundStatus { get; set; }

        [MemoryPackOrder(19)]
        [Id(19)]
        public string SellerRemark { get; set; } = "";

        [MemoryPackOrder(20)]
        [Id(20)]
        public decimal DiscountAmount { get; set; }

        [MemoryPackOrder(21)]
        [Id(21)]
        public decimal FullDiscount { get; set; }

        [MemoryPackOrder(22)]
        [Id(22)]
        public string Address { get; set; } = "";

        [MemoryPackOrder(23)]
        [Id(23)]
        public string Platform { get; set; } = "";

        [MemoryPackOrder(24)]
        [Id(24)]
        public decimal ProductTotalAmount { get; set; }

        [MemoryPackOrder(25)]
        [Id(25)]
        public long? RelatedBatchId { get; set; }

        [MemoryPackOrder(26)]
        [Id(26)]
        public DateTime? PresaleReadyNotifiedAt { get; set; }

        [MemoryPackOrder(27)]
        [Id(27)]
        public DateTime? DeliveredAt { get; set; }

        [MemoryPackOrder(28)]
        [Id(28)]
        public string SenderName { get; set; } = "";

        [MemoryPackOrder(29)]
        [Id(29)]
        public string SenderPhone { get; set; } = "";

        [MemoryPackOrder(30)]
        [Id(30)]
        public string SenderAddress { get; set; } = "";

        [MemoryPackOrder(31)]
        [Id(31)]
        public DateTime CreatedAt { get; set; }
    }

    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class OrderItemState
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public long ProductId { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public int SpeciesId { get; set; }

        [MemoryPackOrder(2)]
        [Id(2)]
        public string ProductName { get; set; } = "";

        [MemoryPackOrder(3)]
        [Id(3)]
        public decimal Price { get; set; }

        [MemoryPackOrder(4)]
        [Id(4)]
        public int Quantity { get; set; }

        [MemoryPackOrder(5)]
        [Id(5)]
        public decimal Subtotal { get; set; }
    }

    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class CartState
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public Guid UserId { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public List<CartItemState> Items { get; set; } = new();
    }

    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class CartItemState
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public long ProductId { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public int Quantity { get; set; }

        [MemoryPackOrder(2)]
        [Id(2)]
        public DateTime AddedTime { get; set; }
    }

    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class PaymentState
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public long TransactionId { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public long OrderId { get; set; }

        [MemoryPackOrder(2)]
        [Id(2)]
        public string TransactionNo { get; set; } = "";

        [MemoryPackOrder(3)]
        [Id(3)]
        public PaymentChannel Channel { get; set; }

        [MemoryPackOrder(4)]
        [Id(4)]
        public decimal Amount { get; set; }

        [MemoryPackOrder(5)]
        [Id(5)]
        public int Status { get; set; }

        [MemoryPackOrder(6)]
        [Id(6)]
        public string PrepayId { get; set; } = "";

        [MemoryPackOrder(7)]
        [Id(7)]
        public string ChannelTransactionNo { get; set; } = "";

        [MemoryPackOrder(8)]
        [Id(8)]
        public DateTime? PaidAt { get; set; }

        [MemoryPackOrder(9)]
        [Id(9)]
        public DateTime? ExpiredAt { get; set; }

        [MemoryPackOrder(10)]
        [Id(10)]
        public string IdempotencyKey { get; set; } = "";

        [MemoryPackOrder(11)]
        [Id(11)]
        public Guid BuyerId { get; set; }

        [MemoryPackOrder(12)]
        [Id(12)]
        public string CallbackLockKey { get; set; } = "";

        [MemoryPackOrder(13)]
        [Id(13)]
        public DateTime? CallbackLockedAt { get; set; }

        [MemoryPackOrder(14)]
        [Id(14)]
        public bool NeedsOrderSync { get; set; }

        [MemoryPackOrder(15)]
        [Id(15)]
        public decimal TotalRefundedAmount { get; set; }
    }

    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class MerchantState
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public long MerchantId { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public Guid UserId { get; set; }

        [MemoryPackOrder(9)]
        [Id(9)]
        public string Passport { get; set; } = "";

        [MemoryPackOrder(2)]
        [Id(2)]
        public MerchantType MerchantType { get; set; }

        [MemoryPackOrder(3)]
        [Id(3)]
        public string ShopName { get; set; } = "";

        [MemoryPackOrder(4)]
        [Id(4)]
        public string ShopDescription { get; set; } = "";

        [MemoryPackOrder(5)]
        [Id(5)]
        public string ContactPhone { get; set; } = "";

        [MemoryPackOrder(6)]
        [Id(6)]
        public string BusinessLicense { get; set; } = "";

        [MemoryPackOrder(7)]
        [Id(7)]
        public bool IsVerified { get; set; }

        [MemoryPackOrder(8)]
        [Id(8)]
        public DateTime? VerifiedAt { get; set; }

        [MemoryPackOrder(10)]
        [Id(10)]
        public int AuditStatus { get; set; }
    }

    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class SettlementState
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public long Id { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public long MerchantId { get; set; }

        [MemoryPackOrder(2)]
        [Id(2)]
        public DateTime PeriodStart { get; set; }

        [MemoryPackOrder(3)]
        [Id(3)]
        public DateTime PeriodEnd { get; set; }

        [MemoryPackOrder(4)]
        [Id(4)]
        public decimal TotalAmount { get; set; }

        [MemoryPackOrder(5)]
        [Id(5)]
        public decimal PlatformFee { get; set; }

        [MemoryPackOrder(6)]
        [Id(6)]
        public decimal SettledAmount { get; set; }

        [MemoryPackOrder(7)]
        [Id(7)]
        public int Status { get; set; }

        [MemoryPackOrder(8)]
        [Id(8)]
        public DateTime? SettledAt { get; set; }
    }

    #endregion

    [MemoryPackable]
    [GenerateSerializer]
    [Serializable]
    public partial class ShopGradeState
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public long Id { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public string Name { get; set; } = "";

        [MemoryPackOrder(2)]
        [Id(2)]
        public int ProductLimit { get; set; }

        [MemoryPackOrder(3)]
        [Id(3)]
        public int ImageLimit { get; set; }

        [MemoryPackOrder(4)]
        [Id(4)]
        public int TemplateLimit { get; set; }

        [MemoryPackOrder(5)]
        [Id(5)]
        public decimal ChargeStandard { get; set; }

        [MemoryPackOrder(6)]
        [Id(6)]
        public string Remark { get; set; } = "";
    }

    [MemoryPackable]
    [GenerateSerializer]
    [Serializable]
    public partial class ProductSKUState
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public long Id { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public long ProductId { get; set; }

        [MemoryPackOrder(2)]
        [Id(2)]
        public string SkuCode { get; set; } = "";

        [MemoryPackOrder(3)]
        [Id(3)]
        public string Color { get; set; } = "";

        [MemoryPackOrder(4)]
        [Id(4)]
        public string Size { get; set; } = "";

        [MemoryPackOrder(5)]
        [Id(5)]
        public string Version { get; set; } = "";

        [MemoryPackOrder(6)]
        [Id(6)]
        public decimal SalePrice { get; set; }

        [MemoryPackOrder(7)]
        [Id(7)]
        public decimal CostPrice { get; set; }

        [MemoryPackOrder(8)]
        [Id(8)]
        public long Stock { get; set; }

        [MemoryPackOrder(9)]
        [Id(9)]
        public long? SafeStock { get; set; }

        [MemoryPackOrder(10)]
        [Id(10)]
        public string ShowPic { get; set; } = "";
    }

    [MemoryPackable]
    [GenerateSerializer]
    [Serializable]
    public partial class ProductCategoryState
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public long Id { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public string Name { get; set; } = "";

        [MemoryPackOrder(2)]
        [Id(2)]
        public int Depth { get; set; }

        [MemoryPackOrder(3)]
        [Id(3)]
        public string Path { get; set; } = "";

        [MemoryPackOrder(4)]
        [Id(4)]
        public long ParentCategoryId { get; set; }

        [MemoryPackOrder(5)]
        [Id(5)]
        public long DisplaySequence { get; set; }

        [MemoryPackOrder(6)]
        [Id(6)]
        public string Icon { get; set; } = "";

        [MemoryPackOrder(7)]
        [Id(7)]
        public string Image { get; set; } = "";
    }

    [MemoryPackable]
    [GenerateSerializer]
    [Serializable]
    public partial class FreightTemplateState
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public long Id { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public long MerchantId { get; set; }

        [MemoryPackOrder(2)]
        [Id(2)]
        public string Name { get; set; } = "";

        [MemoryPackOrder(3)]
        [Id(3)]
        public int ValuationMethod { get; set; }

        [MemoryPackOrder(4)]
        [Id(4)]
        public bool IsFree { get; set; }

        [MemoryPackOrder(5)]
        [Id(5)]
        public decimal FirstUnit { get; set; }

        [MemoryPackOrder(6)]
        [Id(6)]
        public decimal FirstPrice { get; set; }

        [MemoryPackOrder(7)]
        [Id(7)]
        public decimal ContinueUnit { get; set; }

        [MemoryPackOrder(8)]
        [Id(8)]
        public decimal ContinuePrice { get; set; }

        [MemoryPackOrder(9)]
        [Id(9)]
        public decimal? FreeConditionAmount { get; set; }

        [MemoryPackOrder(10)]
        [Id(10)]
        public string AreaRules { get; set; } = "";
    }

    [MemoryPackable]
    [GenerateSerializer]
    [Serializable]
    public partial class OrderRefundState
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public long Id { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public long OrderId { get; set; }

        [MemoryPackOrder(2)]
        [Id(2)]
        public long OrderItemId { get; set; }

        [MemoryPackOrder(3)]
        [Id(3)]
        public string RefundNo { get; set; } = "";

        [MemoryPackOrder(4)]
        [Id(4)]
        public decimal RefundAmount { get; set; }

        [MemoryPackOrder(5)]
        [Id(5)]
        public string Reason { get; set; } = "";

        [MemoryPackOrder(6)]
        [Id(6)]
        public int Status { get; set; }

        [MemoryPackOrder(7)]
        [Id(7)]
        public int RefundMode { get; set; }

        [MemoryPackOrder(8)]
        [Id(8)]
        public string SellerAuditRemark { get; set; } = "";

        [MemoryPackOrder(9)]
        [Id(9)]
        public DateTime? SellerAuditTime { get; set; }

        [MemoryPackOrder(10)]
        [Id(10)]
        public string PlatformRemark { get; set; } = "";

        [MemoryPackOrder(11)]
        [Id(11)]
        public DateTime? PlatformAuditTime { get; set; }

        [MemoryPackOrder(12)]
        [Id(12)]
        public Guid BuyerId { get; set; }

        [MemoryPackOrder(13)]
        [Id(13)]
        public long MerchantId { get; set; }

        [MemoryPackOrder(14)]
        [Id(14)]
        public decimal EnabledRefundAmount { get; set; }

        [MemoryPackOrder(15)]
        [Id(15)]
        public int ReturnQuantity { get; set; }
    }

    [MemoryPackable]
    [GenerateSerializer]
    [Serializable]
    public partial class ProductCommentState
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public long Id { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public long ProductId { get; set; }

        [MemoryPackOrder(2)]
        [Id(2)]
        public long OrderId { get; set; }

        [MemoryPackOrder(3)]
        [Id(3)]
        public Guid UserId { get; set; }

        [MemoryPackOrder(4)]
        [Id(4)]
        public int Rank { get; set; }

        [MemoryPackOrder(5)]
        [Id(5)]
        public string Content { get; set; } = "";

        [MemoryPackOrder(6)]
        [Id(6)]
        public string Images { get; set; } = "";

        [MemoryPackOrder(7)]
        [Id(7)]
        public string ReplyContent { get; set; } = "";

        [MemoryPackOrder(8)]
        [Id(8)]
        public DateTime? ReplyTime { get; set; }

        [MemoryPackOrder(9)]
        [Id(9)]
        public bool IsAnonymous { get; set; }
    }

    [MemoryPackable]
    [GenerateSerializer]
    [Serializable]
    public partial class ShopShipperState
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public long Id { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public long ShopId { get; set; }

        [MemoryPackOrder(2)]
        [Id(2)]
        public string ShipperTag { get; set; } = "";

        [MemoryPackOrder(3)]
        [Id(3)]
        public string ShipperName { get; set; } = "";

        [MemoryPackOrder(4)]
        [Id(4)]
        public int RegionId { get; set; }

        [MemoryPackOrder(5)]
        [Id(5)]
        public string Address { get; set; } = "";

        [MemoryPackOrder(6)]
        [Id(6)]
        public string TelPhone { get; set; } = "";

        [MemoryPackOrder(7)]
        [Id(7)]
        public bool IsDefaultSendGoods { get; set; }

        [MemoryPackOrder(8)]
        [Id(8)]
        public float? Longitude { get; set; }

        [MemoryPackOrder(9)]
        [Id(9)]
        public float? Latitude { get; set; }
    }

    [MemoryPackable]
    [GenerateSerializer]
    [Serializable]
    public partial class ProductLadderPriceState
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public long Id { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public long ProductId { get; set; }

        [MemoryPackOrder(2)]
        [Id(2)]
        public int MinBatch { get; set; }

        [MemoryPackOrder(3)]
        [Id(3)]
        public int MaxBatch { get; set; }

        [MemoryPackOrder(4)]
        [Id(4)]
        public decimal Price { get; set; }
    }

    [MemoryPackable]
    [GenerateSerializer]
    [Serializable]
    public partial class SettledConfigState
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public long Id { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public int BusinessType { get; set; }

        [MemoryPackOrder(2)]
        [Id(2)]
        public int SettlementAccountType { get; set; }

        [MemoryPackOrder(3)]
        [Id(3)]
        public int TrialDays { get; set; }

        [MemoryPackOrder(4)]
        [Id(4)]
        public bool IsCity { get; set; }

        [MemoryPackOrder(5)]
        [Id(5)]
        public bool IsPeopleNumber { get; set; }

        [MemoryPackOrder(6)]
        [Id(6)]
        public bool IsAddress { get; set; }

        [MemoryPackOrder(7)]
        [Id(7)]
        public bool IsBusinessLicenseCode { get; set; }

        [MemoryPackOrder(8)]
        [Id(8)]
        public bool IsBusinessScope { get; set; }

        [MemoryPackOrder(9)]
        [Id(9)]
        public bool IsBusinessLicense { get; set; }
    }

    [MemoryPackable]
    [GenerateSerializer]
    [Serializable]
    public partial class BrandState
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public long Id { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public string Name { get; set; } = "";

        [MemoryPackOrder(2)]
        [Id(2)]
        public string Logo { get; set; } = "";

        [MemoryPackOrder(3)]
        [Id(3)]
        public string Description { get; set; } = "";

        [MemoryPackOrder(4)]
        [Id(4)]
        public long DisplaySequence { get; set; }

        [MemoryPackOrder(5)]
        [Id(5)]
        public bool IsRecommend { get; set; }

        [MemoryPackOrder(6)]
        [Id(6)]
        public int AuditStatus { get; set; }
    }

    [MemoryPackable]
    [GenerateSerializer]
    [Serializable]
    public partial class ShopBrandApplyState
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public long Id { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public long ShopId { get; set; }

        [MemoryPackOrder(2)]
        [Id(2)]
        public string BrandName { get; set; } = "";

        [MemoryPackOrder(3)]
        [Id(3)]
        public string ProofMaterial { get; set; } = "";

        [MemoryPackOrder(4)]
        [Id(4)]
        public int AuditStatus { get; set; }

        [MemoryPackOrder(5)]
        [Id(5)]
        public string AuditRemark { get; set; } = "";
    }

    [MemoryPackable]
    [GenerateSerializer]
    [Serializable]
    public partial class CouponState
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public long Id { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public long ShopId { get; set; }

        [MemoryPackOrder(2)]
        [Id(2)]
        public string CouponName { get; set; } = "";

        [MemoryPackOrder(3)]
        [Id(3)]
        public int CouponType { get; set; }

        [MemoryPackOrder(4)]
        [Id(4)]
        public decimal Denomination { get; set; }

        [MemoryPackOrder(5)]
        [Id(5)]
        public decimal UseCondition { get; set; }

        [MemoryPackOrder(6)]
        [Id(6)]
        public DateTime StartDate { get; set; }

        [MemoryPackOrder(7)]
        [Id(7)]
        public DateTime EndDate { get; set; }

        [MemoryPackOrder(8)]
        [Id(8)]
        public int TotalCount { get; set; }

        [MemoryPackOrder(9)]
        [Id(9)]
        public int ReceivedCount { get; set; }

        [MemoryPackOrder(10)]
        [Id(10)]
        public int UsedCount { get; set; }

        [MemoryPackOrder(11)]
        [Id(11)]
        public bool IsActive { get; set; }
    }

    [MemoryPackable]
    [GenerateSerializer]
    [Serializable]
    public partial class CouponRecordState
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public long Id { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public long CouponId { get; set; }

        [MemoryPackOrder(2)]
        [Id(2)]
        public Guid UserId { get; set; }

        [MemoryPackOrder(3)]
        [Id(3)]
        public int Status { get; set; }

        [MemoryPackOrder(4)]
        [Id(4)]
        public long? UsedOrderId { get; set; }

        [MemoryPackOrder(5)]
        [Id(5)]
        public DateTime ReceivedAt { get; set; }

        [MemoryPackOrder(6)]
        [Id(6)]
        public DateTime? UsedAt { get; set; }
    }

    [MemoryPackable]
    [GenerateSerializer]
    [Serializable]
    public partial class FullDiscountRuleState
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public long Id { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public long ShopId { get; set; }

        [MemoryPackOrder(2)]
        [Id(2)]
        public string RuleName { get; set; } = "";

        [MemoryPackOrder(3)]
        [Id(3)]
        public DateTime StartDate { get; set; }

        [MemoryPackOrder(4)]
        [Id(4)]
        public DateTime EndDate { get; set; }

        [MemoryPackOrder(5)]
        [Id(5)]
        public decimal LimitValue { get; set; }

        [MemoryPackOrder(6)]
        [Id(6)]
        public decimal DiscountValue { get; set; }

        [MemoryPackOrder(7)]
        [Id(7)]
        public bool IsActive { get; set; }
    }

    [MemoryPackable]
    [GenerateSerializer]
    [Serializable]
    public partial class CashDepositState
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public long Id { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public long ShopId { get; set; }

        [MemoryPackOrder(2)]
        [Id(2)]
        public long CategoryId { get; set; }

        [MemoryPackOrder(3)]
        [Id(3)]
        public decimal Amount { get; set; }

        [MemoryPackOrder(4)]
        [Id(4)]
        public int Status { get; set; }

        [MemoryPackOrder(5)]
        [Id(5)]
        public DateTime? PaidAt { get; set; }

        [MemoryPackOrder(6)]
        [Id(6)]
        public DateTime? DeductedAt { get; set; }

        [MemoryPackOrder(7)]
        [Id(7)]
        public bool NoReasonReturn { get; set; }
    }

    [MemoryPackable]
    [GenerateSerializer]
    [Serializable]
    public partial class BusinessCategoryState
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public long Id { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public long ShopId { get; set; }

        [MemoryPackOrder(2)]
        [Id(2)]
        public long CategoryId { get; set; }

        [MemoryPackOrder(3)]
        [Id(3)]
        public decimal CommissionRate { get; set; }

        [MemoryPackOrder(4)]
        [Id(4)]
        public int AuditStatus { get; set; }

        [MemoryPackOrder(5)]
        [Id(5)]
        public string AuditRemark { get; set; } = "";
    }

    [MemoryPackable]
    [GenerateSerializer]
    [Serializable]
    public partial class ProductDescriptionTemplateState
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public long Id { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public long ShopId { get; set; }

        [MemoryPackOrder(2)]
        [Id(2)]
        public string TemplateName { get; set; } = "";

        [MemoryPackOrder(3)]
        [Id(3)]
        public string TopContent { get; set; } = "";

        [MemoryPackOrder(4)]
        [Id(4)]
        public string BottomContent { get; set; } = "";
    }

    [MemoryPackable]
    [GenerateSerializer]
    [Serializable]
    public partial class ProductRelationState
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public long Id { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public long ProductId { get; set; }

        [MemoryPackOrder(2)]
        [Id(2)]
        public long RelatedProductId { get; set; }

        [MemoryPackOrder(3)]
        [Id(3)]
        public int DisplaySequence { get; set; }
    }

    [MemoryPackable]
    [GenerateSerializer]
    [Serializable]
    public partial class OrderComplaintState
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public long Id { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public long OrderId { get; set; }

        [MemoryPackOrder(2)]
        [Id(2)]
        public Guid UserId { get; set; }

        [MemoryPackOrder(3)]
        [Id(3)]
        public long ShopId { get; set; }

        [MemoryPackOrder(4)]
        [Id(4)]
        public string ComplaintReason { get; set; } = "";

        [MemoryPackOrder(5)]
        [Id(5)]
        public string ComplaintContent { get; set; } = "";

        [MemoryPackOrder(6)]
        [Id(6)]
        public int Status { get; set; }

        [MemoryPackOrder(7)]
        [Id(7)]
        public string ReplyContent { get; set; } = "";

        [MemoryPackOrder(8)]
        [Id(8)]
        public DateTime CreatedAt { get; set; }

        [MemoryPackOrder(9)]
        [Id(9)]
        public DateTime? ResolvedAt { get; set; }
    }

    [MemoryPackable]
    [GenerateSerializer]
    [Serializable]
    public partial class TradeCommentState
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public long Id { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public long OrderId { get; set; }

        [MemoryPackOrder(2)]
        [Id(2)]
        public Guid UserId { get; set; }

        [MemoryPackOrder(3)]
        [Id(3)]
        public long ShopId { get; set; }

        [MemoryPackOrder(4)]
        [Id(4)]
        public int DescriptionScore { get; set; }

        [MemoryPackOrder(5)]
        [Id(5)]
        public int ServiceScore { get; set; }

        [MemoryPackOrder(6)]
        [Id(6)]
        public int LogisticsScore { get; set; }

        [MemoryPackOrder(7)]
        [Id(7)]
        public string Content { get; set; } = "";

        [MemoryPackOrder(8)]
        [Id(8)]
        public bool IsAnonymous { get; set; }
    }

    [MemoryPackable]
    [GenerateSerializer]
    [Serializable]
    public partial class PendingSettlementState
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public long Id { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public long OrderId { get; set; }

        [MemoryPackOrder(2)]
        [Id(2)]
        public long ShopId { get; set; }

        [MemoryPackOrder(3)]
        [Id(3)]
        public decimal OrderAmount { get; set; }

        [MemoryPackOrder(4)]
        [Id(4)]
        public decimal PlatformCommission { get; set; }

        [MemoryPackOrder(5)]
        [Id(5)]
        public decimal RefundAmount { get; set; }

        [MemoryPackOrder(6)]
        [Id(6)]
        public decimal SettleableAmount { get; set; }

        [MemoryPackOrder(7)]
        [Id(7)]
        public int Status { get; set; }

        [MemoryPackOrder(8)]
        [Id(8)]
        public long? SettlementId { get; set; }

        [MemoryPackOrder(9)]
        [Id(9)]
        public DateTime CreatedAt { get; set; }

        [MemoryPackOrder(10)]
        [Id(10)]
        public DateTime? SettledAt { get; set; }
    }

    [MemoryPackable]
    [GenerateSerializer]
    [Serializable]
    public partial class ShopWithdrawState
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public long Id { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public long ShopId { get; set; }

        [MemoryPackOrder(2)]
        [Id(2)]
        public decimal Amount { get; set; }

        [MemoryPackOrder(3)]
        [Id(3)]
        public string BankName { get; set; } = "";

        [MemoryPackOrder(4)]
        [Id(4)]
        public string AccountNo { get; set; } = "";

        [MemoryPackOrder(5)]
        [Id(5)]
        public string AccountName { get; set; } = "";

        [MemoryPackOrder(6)]
        [Id(6)]
        public int Status { get; set; }

        [MemoryPackOrder(7)]
        [Id(7)]
        public string AuditRemark { get; set; } = "";

        [MemoryPackOrder(8)]
        [Id(8)]
        public DateTime CreatedAt { get; set; }

        [MemoryPackOrder(9)]
        [Id(9)]
        public DateTime? AuditedAt { get; set; }

        [MemoryPackOrder(10)]
        [Id(10)]
        public DateTime? PaidAt { get; set; }
    }

    [MemoryPackable]
    [GenerateSerializer]
    [Serializable]
    public partial class ShopAccountItemState
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public long Id { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public long ShopId { get; set; }

        [MemoryPackOrder(2)]
        [Id(2)]
        public int AccountType { get; set; }

        [MemoryPackOrder(3)]
        [Id(3)]
        public decimal Amount { get; set; }

        [MemoryPackOrder(4)]
        [Id(4)]
        public decimal BalanceAfter { get; set; }

        [MemoryPackOrder(5)]
        [Id(5)]
        public string Description { get; set; } = "";

        [MemoryPackOrder(6)]
        [Id(6)]
        public long RelatedId { get; set; }

        [MemoryPackOrder(7)]
        [Id(7)]
        public DateTime CreatedAt { get; set; }
    }

    [MemoryPackable]
    [GenerateSerializer]
    [Serializable]
    public partial class SettlementAccountState
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public long Id { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public long MerchantId { get; set; }

        [MemoryPackOrder(2)]
        [Id(2)]
        public string BankName { get; set; } = "";

        [MemoryPackOrder(3)]
        [Id(3)]
        public string AccountNo { get; set; } = "";

        [MemoryPackOrder(4)]
        [Id(4)]
        public string AccountName { get; set; } = "";

        [MemoryPackOrder(5)]
        [Id(5)]
        public bool IsDefault { get; set; }
    }

    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class ShippingAddressState
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public long Id { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public Guid UserId { get; set; }

        [MemoryPackOrder(2)]
        [Id(2)]
        public string ShipTo { get; set; } = "";

        [MemoryPackOrder(3)]
        [Id(3)]
        public string Phone { get; set; } = "";

        [MemoryPackOrder(4)]
        [Id(4)]
        public int? ProvinceId { get; set; }

        [MemoryPackOrder(5)]
        [Id(5)]
        public string ProvinceName { get; set; } = "";

        [MemoryPackOrder(6)]
        [Id(6)]
        public int? CityId { get; set; }

        [MemoryPackOrder(7)]
        [Id(7)]
        public string CityName { get; set; } = "";

        [MemoryPackOrder(8)]
        [Id(8)]
        public int? DistrictId { get; set; }

        [MemoryPackOrder(9)]
        [Id(9)]
        public string DistrictName { get; set; } = "";

        [MemoryPackOrder(10)]
        [Id(10)]
        public string Address { get; set; } = "";

        [MemoryPackOrder(11)]
        [Id(11)]
        public bool IsDefault { get; set; }

        [MemoryPackOrder(12)]
        [Id(12)]
        public double? Latitude { get; set; }

        [MemoryPackOrder(13)]
        [Id(13)]
        public double? Longitude { get; set; }

        [MemoryPackOrder(14)]
        [Id(14)]
        public string Passport { get; set; } = "";
    }

    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class ReturnShipmentState
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public long Id { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public long RefundId { get; set; }

        [MemoryPackOrder(2)]
        [Id(2)]
        public string ExpressCompanyName { get; set; } = "";

        [MemoryPackOrder(3)]
        [Id(3)]
        public string ShipOrderNumber { get; set; } = "";

        [MemoryPackOrder(4)]
        [Id(4)]
        public string ReturnAddress { get; set; } = "";

        [MemoryPackOrder(5)]
        [Id(5)]
        public DateTime? ShippedAt { get; set; }

        [MemoryPackOrder(6)]
        [Id(6)]
        public DateTime? ReceivedAt { get; set; }

        [MemoryPackOrder(7)]
        [Id(7)]
        public int Status { get; set; }
    }

    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class LogisticsTrackState
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public long Id { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public long OrderId { get; set; }

        [MemoryPackOrder(2)]
        [Id(2)]
        public string ExpressCompanyName { get; set; } = "";

        [MemoryPackOrder(3)]
        [Id(3)]
        public string ShipOrderNumber { get; set; } = "";

        [MemoryPackOrder(4)]
        [Id(4)]
        public string TrackData { get; set; } = "";

        [MemoryPackOrder(5)]
        [Id(5)]
        public DateTime? LastQueriedAt { get; set; }

        [MemoryPackOrder(6)]
        [Id(6)]
        public int LogisticsStatus { get; set; }

        [MemoryPackOrder(7)]
        [Id(7)]
        public bool IsReturn { get; set; }

        [MemoryPackOrder(8)]
        [Id(8)]
        public long? RefundId { get; set; }

        [MemoryPackOrder(9)]
        [Id(9)]
        public string OriginCity { get; set; } = "";

        [MemoryPackOrder(10)]
        [Id(10)]
        public string DestinationCity { get; set; } = "";

        [MemoryPackOrder(11)]
        [Id(11)]
        public string CurrentLocation { get; set; } = "";
    }

    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class SettlementDetailState
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public long Id { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public long SettlementBillId { get; set; }

        [MemoryPackOrder(2)]
        [Id(2)]
        public long OrderId { get; set; }

        [MemoryPackOrder(3)]
        [Id(3)]
        public string OrderNo { get; set; } = "";

        [MemoryPackOrder(4)]
        [Id(4)]
        public decimal OrderAmount { get; set; }

        [MemoryPackOrder(5)]
        [Id(5)]
        public decimal PlatformCommission { get; set; }

        [MemoryPackOrder(6)]
        [Id(6)]
        public decimal RefundAmount { get; set; }

        [MemoryPackOrder(7)]
        [Id(7)]
        public decimal SettleableAmount { get; set; }
    }

    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class RepurchaseState
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public long Id { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public Guid BuyerId { get; set; }

        [MemoryPackOrder(2)]
        [Id(2)]
        public long OriginalOrderId { get; set; }

        [MemoryPackOrder(3)]
        [Id(3)]
        public long? NewOrderId { get; set; }

        [MemoryPackOrder(4)]
        [Id(4)]
        public DateTime RepurchaseTime { get; set; }
    }

    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class SettlementAccountSummaryState
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public long MerchantId { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public decimal TotalSettled { get; set; }

        [MemoryPackOrder(2)]
        [Id(2)]
        public decimal TotalWithdrawn { get; set; }

        [MemoryPackOrder(3)]
        [Id(3)]
        public decimal AvailableBalance { get; set; }

        [MemoryPackOrder(4)]
        [Id(4)]
        public decimal PendingSettlement { get; set; }

        [MemoryPackOrder(5)]
        [Id(5)]
        public decimal FrozenAmount { get; set; }
    }

    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class BatchShipRequest
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public List<long> OrderIds { get; set; } = new();

        [MemoryPackOrder(1)]
        [Id(1)]
        public string ExpressCompanyName { get; set; } = "";

        [MemoryPackOrder(2)]
        [Id(2)]
        public string ShipOrderNumberPrefix { get; set; } = "";

        [MemoryPackOrder(3)]
        [Id(3)]
        public long? ShipperId { get; set; }
    }

    public class LogisticsMapData
    {
        public long OrderId { get; set; }
        public string ExpressCompanyName { get; set; } = "";
        public string ShipOrderNumber { get; set; } = "";
        public string OriginCity { get; set; } = "";
        public string DestinationCity { get; set; } = "";
        public int LogisticsStatus { get; set; }
        public List<LogisticsMapNode> Nodes { get; set; } = new();
    }

    public class LogisticsMapNode
    {
        public DateTime Time { get; set; }
        public string Description { get; set; } = "";
        public string Location { get; set; } = "";
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
    }
}
