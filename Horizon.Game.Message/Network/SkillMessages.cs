using Horizon.Game.Message.Enums;
using MemoryPack;
using Orleans;
using System;
using System.Collections.Generic;

namespace Horizon.Game.Message.Network
{
    #region 技能基础消息

    /// <summary>
    /// 技能信息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class SkillInfo : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 技能ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public int SkillId { get; set; }

        /// <summary>
        /// 技能名称
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string SkillName { get; set; } = "";

        /// <summary>
        /// 技能描述
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public string Description { get; set; } = "";

        /// <summary>
        /// 技能图标
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public string Icon { get; set; } = "";

        /// <summary>
        /// 技能类型
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public int SkillType { get; set; }

        /// <summary>
        /// 技能等级
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public int Level { get; set; }

        /// <summary>
        /// 最大等级
        /// </summary>
        [MemoryPackOrder(6)]
        [Id(6)]
        public int MaxLevel { get; set; }

        /// <summary>
        /// 消耗内力
        /// </summary>
        [MemoryPackOrder(7)]
        [Id(7)]
        public int NeiLiCost { get; set; }

        /// <summary>
        /// 冷却时间（毫秒）
        /// </summary>
        [MemoryPackOrder(8)]
        [Id(8)]
        public long Cooldown { get; set; }

        /// <summary>
        /// 施法时间（毫秒）
        /// </summary>
        [MemoryPackOrder(9)]
        [Id(9)]
        public long CastTime { get; set; }

        /// <summary>
        /// 技能范围
        /// </summary>
        [MemoryPackOrder(10)]
        [Id(10)]
        public float Range { get; set; }

        /// <summary>
        /// 技能属性
        /// </summary>
        [MemoryPackOrder(11)]
        [Id(11)]
        public Dictionary<string, object> Attributes { get; set; } = new();

        [MemoryPackOrder(12)]
        [Id(12)]
        public MessageType Type { get; set; } = MessageType.LearnSkill;
        [MemoryPackOrder(13)]
        [Id(13)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    /// <summary>
    /// 学习技能请求
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class LearnSkillRequest : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 角色ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong CharacterId { get; set; }

        /// <summary>
        /// 技能ID
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public int SkillId { get; set; }

        /// <summary>
        /// 学习方式（0=普通学习，1=金币学习，2=道具学习）
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public int LearnMethod { get; set; }

        [MemoryPackOrder(3)]
        [Id(3)]
        public MessageType Type { get; set; } = MessageType.LearnSkill;
        [MemoryPackOrder(4)]
        [Id(4)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    /// <summary>
    /// 学习技能响应
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class LearnSkillResponse : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 是否成功
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public bool Success { get; set; }

        /// <summary>
        /// 消息
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string Message { get; set; } = "";

        /// <summary>
        /// 学习到的技能
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public SkillInfo LearnedSkill { get; set; } = new();

        /// <summary>
        /// 消耗的金币
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public long ConsumedGold { get; set; }

        /// <summary>
        /// 消耗的道具ID列表
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public List<long> ConsumedItems { get; set; } = new();

        [MemoryPackOrder(5)]
        [Id(5)]
        public MessageType Type { get; set; } = MessageType.LearnSkill;
        [MemoryPackOrder(6)]
        [Id(6)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    #endregion

    #region 技能冷却消息

    /// <summary>
    /// 技能冷却查询请求
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class SkillCooldownQueryRequest : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 角色ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong CharacterId { get; set; }

        /// <summary>
        /// 技能ID列表
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public List<int> SkillIds { get; set; } = new();

        [MemoryPackOrder(2)]
        [Id(2)]
        public MessageType Type { get; set; } = MessageType.SkillCooldown;
        [MemoryPackOrder(3)]
        [Id(3)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    /// <summary>
    /// 技能冷却查询响应
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class SkillCooldownQueryResponse : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 角色ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong CharacterId { get; set; }

        /// <summary>
        /// 技能冷却信息字典（技能ID -> 剩余冷却时间毫秒）
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public Dictionary<int, long> SkillCooldowns { get; set; } = new();

        [MemoryPackOrder(2)]
        [Id(2)]
        public MessageType Type { get; set; } = MessageType.SkillCooldown;
        [MemoryPackOrder(3)]
        [Id(3)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    /// <summary>
    /// 技能冷却更新消息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class SkillCooldownUpdateMessage : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 角色ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong CharacterId { get; set; }

        /// <summary>
        /// 技能ID
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public int SkillId { get; set; }

        /// <summary>
        /// 冷却时间（毫秒）
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public long CooldownTime { get; set; }

        /// <summary>
        /// 更新时间
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public long UpdateTime { get; set; }

        [MemoryPackOrder(4)]
        [Id(4)]
        public MessageType Type { get; set; } = MessageType.SkillCooldown;
        [MemoryPackOrder(5)]
        [Id(5)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    #endregion

    #region 技能熟练度消息

    /// <summary>
    /// 技能熟练度查询请求
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class SkillProficiencyQueryRequest : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 角色ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong CharacterId { get; set; }

        /// <summary>
        /// 技能ID列表
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public List<int> SkillIds { get; set; } = new();

        [MemoryPackOrder(2)]
        [Id(2)]
        public MessageType Type { get; set; } = MessageType.SkillProficiency;
        [MemoryPackOrder(3)]
        [Id(3)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    /// <summary>
    /// 技能熟练度查询响应
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class SkillProficiencyQueryResponse : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 角色ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong CharacterId { get; set; }

        /// <summary>
        /// 技能熟练度字典（技能ID -> 熟练度值）
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public Dictionary<int, int> SkillProficiencies { get; set; } = new();

        [MemoryPackOrder(2)]
        [Id(2)]
        public MessageType Type { get; set; } = MessageType.SkillProficiency;
        [MemoryPackOrder(3)]
        [Id(3)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    /// <summary>
    /// 技能熟练度更新消息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class SkillProficiencyUpdateMessage : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 角色ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong CharacterId { get; set; }

        /// <summary>
        /// 技能ID
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public int SkillId { get; set; }

        /// <summary>
        /// 熟练度变化值
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public int ProficiencyChange { get; set; }

        /// <summary>
        /// 当前熟练度值
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public int CurrentProficiency { get; set; }

        /// <summary>
        /// 更新原因
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public string Reason { get; set; } = "";

        [MemoryPackOrder(5)]
        [Id(5)]
        public MessageType Type { get; set; } = MessageType.SkillProficiency;
        [MemoryPackOrder(6)]
        [Id(6)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    #endregion

    #region 技能升级消息

    /// <summary>
    /// 升级技能请求
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class UpgradeSkillRequest : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 角色ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong CharacterId { get; set; }

        /// <summary>
        /// 技能ID
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public int SkillId { get; set; }

        /// <summary>
        /// 升级方式（0=普通升级，1=金币升级，2=道具升级）
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public int UpgradeMethod { get; set; }

        [MemoryPackOrder(3)]
        [Id(3)]
        public MessageType Type { get; set; } = MessageType.UpgradeSkill;
        [MemoryPackOrder(4)]
        [Id(4)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    /// <summary>
    /// 升级技能响应
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class UpgradeSkillResponse : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 是否成功
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public bool Success { get; set; }

        /// <summary>
        /// 消息
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string Message { get; set; } = "";

        /// <summary>
        /// 升级后的技能
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public SkillInfo UpgradedSkill { get; set; } = new();

        /// <summary>
        /// 消耗的金币
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public long ConsumedGold { get; set; }

        /// <summary>
        /// 消耗的道具ID列表
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public List<long> ConsumedItems { get; set; } = new();

        /// <summary>
        /// 消耗的经验值
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public long ConsumedExperience { get; set; }

        [MemoryPackOrder(6)]
        [Id(6)]
        public MessageType Type { get; set; } = MessageType.UpgradeSkill;
        [MemoryPackOrder(7)]
        [Id(7)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    /// <summary>
    /// 技能等级更新消息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class SkillLevelUpdateMessage : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 角色ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong CharacterId { get; set; }

        /// <summary>
        /// 技能ID
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public int SkillId { get; set; }

        /// <summary>
        /// 旧等级
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public int OldLevel { get; set; }

        /// <summary>
        /// 新等级
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public int NewLevel { get; set; }

        /// <summary>
        /// 升级时间
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public long UpgradeTime { get; set; }

        [MemoryPackOrder(5)]
        [Id(5)]
        public MessageType Type { get; set; } = MessageType.UpgradeSkill;
        [MemoryPackOrder(6)]
        [Id(6)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    #endregion

    #region 技能栏位消息

    /// <summary>
    /// 技能栏位信息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class SkillSlotInfo : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 栏位索引
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public int SlotIndex { get; set; }

        /// <summary>
        /// 技能ID
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public int SkillId { get; set; }

        /// <summary>
        /// 是否锁定
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public bool IsLocked { get; set; }

        [MemoryPackOrder(3)]
        [Id(3)]
        public MessageType Type { get; set; } = MessageType.LearnSkill;
        [MemoryPackOrder(4)]
        [Id(4)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    /// <summary>
    /// 更新技能栏位请求
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class UpdateSkillSlotRequest : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 角色ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong CharacterId { get; set; }

        /// <summary>
        /// 栏位索引
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public int SlotIndex { get; set; }

        /// <summary>
        /// 技能ID
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public int SkillId { get; set; }

        /// <summary>
        /// 是否锁定
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public bool IsLocked { get; set; }

        [MemoryPackOrder(4)]
        [Id(4)]
        public MessageType Type { get; set; } = MessageType.LearnSkill;
        [MemoryPackOrder(5)]
        [Id(5)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    /// <summary>
    /// 更新技能栏位响应
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class UpdateSkillSlotResponse : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 是否成功
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public bool Success { get; set; }

        /// <summary>
        /// 消息
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string Message { get; set; } = "";

        /// <summary>
        /// 更新后的技能栏位
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public SkillSlotInfo UpdatedSlot { get; set; } = new();

        [MemoryPackOrder(3)]
        [Id(3)]
        public MessageType Type { get; set; } = MessageType.LearnSkill;
        [MemoryPackOrder(4)]
        [Id(4)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    #endregion

    #region 技能效果消息

    /// <summary>
    /// 效果消息 - 用于传输战斗效果信息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class EffectMessage : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 效果ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public int EffectId { get; set; }

        /// <summary>
        /// 效果名称
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string EffectName { get; set; } = "";

        /// <summary>
        /// 目标ID(接受多目标)
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public ulong TargetId { get; set; }

        /// <summary>
        /// 来源ID
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public ulong SourceId { get; set; }

        /// <summary>
        /// 持续时间（秒）
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public float Duration { get; set; }

        /// <summary>
        /// 剩余持续时间（秒）
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public float RemainingDuration { get; set; }

        /// <summary>
        /// 效果强度
        /// </summary>
        [MemoryPackOrder(6)]
        [Id(6)]
        public float Intensity { get; set; }

        /// <summary>
        /// 叠加层数
        /// </summary>
        [MemoryPackOrder(7)]
        [Id(7)]
        public int StackCount { get; set; }

        /// <summary>
        /// 效果类型
        /// </summary>
        [MemoryPackOrder(8)]
        [Id(8)]
        public int EffectType { get; set; }

        /// <summary>
        /// 是否应用成功
        /// </summary>
        [MemoryPackOrder(9)]
        [Id(9)]
        public bool Applied { get; set; }

        [MemoryPackOrder(10)]
        [Id(10)]
        public MessageType Type { get; set; } = MessageType.Skill;
        
        [MemoryPackOrder(11)]
        [Id(11)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    #endregion
}
