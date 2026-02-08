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
        
        [MemoryPackOrder(1)]
        [Id(1)]
        public bool IsOnline { get; set; }
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
        /// 在线玩家ID集合
        /// </summary>
        [MemoryPackOrder(10)]
        [Id(10)]
        public HashSet<Guid> OnlinePlayers { get; set; } = new();
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
}
