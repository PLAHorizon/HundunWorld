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
    [NetworkMessage(30, Enums.ServiceType.Game)]
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
    [NetworkMessage(84, Enums.ServiceType.Game)]
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

        /// <summary>
        /// 正在交互的 NPC 运行时实体 id；0 = 不走训练师（书/直接学）。
        /// <para>
        /// <b>服务端据此解析"谁在教"并核对邻近</b>（与任务交互、商人同一条权威路径）；
        /// 客户端<b>不许</b>直接报学费或 NPC 模板 id——价格来自服务端装配的训练师行。
        /// 追加在末尾（Order 5），旧发法不带此字段时按 0 处理。
        /// </para>
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public ulong EntityId { get; set; }
    }

    /// <summary>
    /// 学习技能响应
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(85, Enums.ServiceType.Game)]
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

        /// <summary>
        /// 这本秘籍是否已被耗尽（研读成功、或参悟掷签失败都算耗尽；
        /// 等级/亲和/前置未达标不算——玩家不该因为还没长级被罚款）。
        /// 由 InventoryGrain 据此决定扣不扣数量。
        /// </summary>
        [MemoryPackOrder(7)]
        [Id(7)]
        public bool BookConsumed { get; set; }

        /// <summary>
        /// 研读成功后要扣掉的特殊材料 id（§14）。SkillGrain 只做判定，扣减由持有背包的
        /// InventoryGrain 执行——所以门槛清单得随回执带回去，而不是让 InventoryGrain 再去读一次技能表。
        /// </summary>
        [MemoryPackOrder(8)]
        [Id(8)]
        public List<int> MaterialItemIds { get; set; } = new();

        /// <summary>与 <see cref="MaterialItemIds"/> 一一对应的数量。</summary>
        [MemoryPackOrder(9)]
        [Id(9)]
        public List<int> MaterialCounts { get; set; } = new();

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
    [NetworkMessage(86, Enums.ServiceType.Game)]
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
    [NetworkMessage(87, Enums.ServiceType.Game)]
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
    [NetworkMessage(88, Enums.ServiceType.Game)]
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
    [NetworkMessage(89, Enums.ServiceType.Game)]
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
    [NetworkMessage(90, Enums.ServiceType.Game)]
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
    [NetworkMessage(91, Enums.ServiceType.Game)]
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
    [NetworkMessage(92, Enums.ServiceType.Game)]
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
    [NetworkMessage(93, Enums.ServiceType.Game)]
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
    [NetworkMessage(94, Enums.ServiceType.Game)]
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
    [NetworkMessage(95, Enums.ServiceType.Game)]
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
    [NetworkMessage(96, Enums.ServiceType.Game)]
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
    [NetworkMessage(97, Enums.ServiceType.Game)]
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
    [NetworkMessage(221, Enums.ServiceType.Game)]
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

        // 五行三行载荷：Buff 要参与服务端权威结算就必须带数（只有 Intensity 的话结算层无从下手）。
        // 行号 1..5 = 金木水火土，与 ElementStatVector / 属性表 *_attack 列一致；平值，负数=减益。
        // 追加在 12 之后：已上线客户端按序反序列化，插在中间会让后面所有字段集体错位。

        /// <summary>该效果提供的五行攻击加成（求和侧）。</summary>
        [MemoryPackOrder(12)]
        [Id(12)]
        public Horizon.Game.Message.Combat.ElementStatVector AttackBoost { get; set; }

        /// <summary>该效果提供的五行防御加成（取高侧）。</summary>
        [MemoryPackOrder(13)]
        [Id(13)]
        public Horizon.Game.Message.Combat.ElementStatVector DefenseBoost { get; set; }

        /// <summary>该效果提供的五行破防加成（取高侧）。</summary>
        [MemoryPackOrder(14)]
        [Id(14)]
        public Horizon.Game.Message.Combat.ElementStatVector PenetrationBoost { get; set; }
    }

    /// <summary>
    /// 一条技能模板的服务端投影。<see cref="SkillInfo"/> 是"这个角色学了哪招、几级"，
    /// 本类型是"这一招本身是什么"——名字、五行、消耗、冷却、射程。
    /// 客户端此前自己硬编码了一份（SkillDatabase），两端只有靠人工同步才会一致；
    /// 下行打通后以服务端为准，硬编码只留作下行到达前的兜底。
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class SkillTemplateEntryInfo
    {
        /// <summary>技能模板 ID = 服务端 skill_id = 客户端上报施放时用的那个号。</summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public int SkillId { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public string SkillName { get; set; } = "";

        [MemoryPackOrder(2)]
        [Id(2)]
        public string Description { get; set; } = "";

        /// <summary>五行（1..5 = 金木水火土，0 = 无属性）。与战斗侧行号同一套约定。</summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public int Element { get; set; }

        /// <summary>技能类型（服务端 skill_type 原值）。</summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public int SkillType { get; set; }

        /// <summary>内力消耗（表里存的是公式串，下行前已折成数值）。</summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public float EnergyCost { get; set; }

        [MemoryPackOrder(6)]
        [Id(6)]
        public float Cooldown { get; set; }

        [MemoryPackOrder(7)]
        [Id(7)]
        public float CastTime { get; set; }

        [MemoryPackOrder(8)]
        [Id(8)]
        public float AttackRange { get; set; }

        [MemoryPackOrder(9)]
        [Id(9)]
        public int MaxLevel { get; set; }

        /// <summary>学习所需等级。</summary>
        [MemoryPackOrder(10)]
        [Id(10)]
        public int LearnLevelRequire { get; set; }

        /// <summary>该技能为施法者本行追加的五行攻击力。</summary>
        [MemoryPackOrder(11)]
        [Id(11)]
        public int SkillAttack { get; set; }

        /// <summary>该技能为施法者本行追加的五行破防值。</summary>
        [MemoryPackOrder(12)]
        [Id(12)]
        public int SkillPenetration { get; set; }

        /// <summary>
        /// 阶位（1白 2黄 3玄 4地 5天 6道 7混沌；0=不在五行阶梯上）。
        /// 客户端技能树按它排格子：库里新增了行而 skills.json 忘了重导时，
        /// 缺这个字段的那一式会直接从树上消失（树只画 Grade&gt;=1），而不是显示"未知行"。
        /// </summary>
        [MemoryPackOrder(13)]
        [Id(13)]
        public int SkillGrade { get; set; }

        /// <summary>阶内境界 1初 2中 3境（与 SkillGrade 一起定位树上那一格）。</summary>
        [MemoryPackOrder(14)]
        [Id(14)]
        public int GradeStage { get; set; }

        /// <summary>技系标识（如 Metal_4），技能树把同阶三式归一组用。</summary>
        [MemoryPackOrder(15)]
        [Id(15)]
        public string SeriesId { get; set; } = "";

        /// <summary>§3.2 阶梯伤害倍率。服务端结算用的就是它，下行后 tooltip 不再读本地旧值。</summary>
        [MemoryPackOrder(16)]
        [Id(16)]
        public double DamageMultiplier { get; set; }

        /// <summary>前置式 id（同阶上一境，或上一阶极境；0/空=无）。</summary>
        [MemoryPackOrder(17)]
        [Id(17)]
        public int PreSkillId { get; set; }

        /// <summary>
        /// 模板 <c>learn_cost</c> 原文（演化式的 consume / loopNeed / needPairsCompleted 都在这儿）。
        /// <para>
        /// 直接透传 JSON 而不是再拆成一堆列：门槛以后加一条（比如"需要几本残卷"）只要生成器写进 JSON，
        /// 契约不用动第二次。客户端自己会解析这份它本来就要展示的门禁表。
        /// </para>
        /// </summary>
        [MemoryPackOrder(18)]
        [Id(18)]
        public string LearnCostJson { get; set; } = "";
    }

    /// <summary>
    /// 技能目录下行：客户端只填 <see cref="CharacterId"/> 请求，服务端回填整份模板表
    /// 与该角色已学技能。<see cref="Success"/> 为 false 时客户端保留本地兜底表，不清空技能栏。
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(237, Enums.ServiceType.Game)]
    public partial class SkillCatalogSyncMessage : MessageUnion, INetworkMessage
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong CharacterId { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public bool Success { get; set; }

        [MemoryPackOrder(2)]
        [Id(2)]
        public string Message { get; set; } = "";

        /// <summary>全部技能模板（服务端技能表的镜像）。</summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public List<SkillTemplateEntryInfo> Templates { get; set; } = new();

        /// <summary>该角色已学技能（含等级）。</summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public List<SkillInfo> Learned { get; set; } = new();

        [MemoryPackOrder(5)]
        [Id(5)]
        public MessageType Type { get; set; } = MessageType.SkillCatalogSync;

        [MemoryPackOrder(6)]
        [Id(6)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;

        /// <summary>
        /// 权威周天状态的一次性快照：当前节数、上一式所属行、本轮圆满次数、镇岳护体行。
        /// <para>
        /// 为什么把这几项放进目录下行：客户端的循环指示器此前只靠"每一次施放的回执"累积认知，
        /// 重登或换端后指示器是空的，要等下一式打出去才慢慢长回来——而服务端一直知道全部事实。
        /// 破势更需要它：层被夺走之后，客户端若拿不到基线，指示器就会与真实层数长期不一致。
        /// </para>
        /// </summary>
        [MemoryPackOrder(7)]
        [Id(7)]
        public int LoopStack { get; set; }

        [MemoryPackOrder(8)]
        [Id(8)]
        public int LoopLastElement { get; set; }

        [MemoryPackOrder(9)]
        [Id(9)]
        public int LoopCompletionCount { get; set; }

        /// <summary>当前镇岳护体行（0 = 未指定，回落到"防御最高行"自动口径）。</summary>
        [MemoryPackOrder(10)]
        [Id(10)]
        public int GuardElement { get; set; }

        /// <summary>
        /// 分域好感度快照（1金…5土 → 当前值），由网关在读完目录后顺手向 FavorabilityGrain 取一次。
        /// <para>
        /// 为什么挂在目录下行而不是新查询接口：§14 的玄阶门槛要显示"火域好感 24/60"，
        /// 而技能目录本来就是"打开树页时拉一次"的那条通道；再开一个查询只是多一次往返，
        /// 还会与目录拉到不同步的两帧。<b>判定仍然只在服务端</b>，这里纯粹是给玩家看"还差多少"。
        /// </para>
        /// <para>
        /// <b>null 与空表是两件事</b>：null = 没取到（读失败，或 Silo 自己组的帧），客户端退回"只说要什么"；
        /// 空表 = 服务端确认这个人目前零好感，可以照实显示"火域好感 0/60"。把两者混成一个空字典，
        /// 新角色就会被显示成"读不到"，而读失败又会被显示成"你一点好感都没有"。
        /// </para>
        /// </summary>
        [MemoryPackOrder(11)]
        [Id(11)]
        public Dictionary<int, int>? FavorByDomain { get; set; }
    }

    /// <summary>
    /// 镇岳（§5.3）：请求把护体五行固定为某一行（0 = 取消固定，回到"防御最高行"自动口径）。
    /// <para>
    /// 请求与回执共用一个类：客户端填 <see cref="CharacterId"/> 与 <see cref="Element"/>，
    /// 服务端回填生效结果。<see cref="GuardElement"/> 回的是<b>生效后</b>的行——被冷却或
    /// "该行没练过"拒绝时它保持原值，客户端据此把界面回滚，而不是留下一个亮着却无效的选中态。
    /// </para>
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(240, Enums.ServiceType.Game)]
    public partial class SetGuardElementMessage : MessageUnion, INetworkMessage
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong CharacterId { get; set; }

        /// <summary>请求设定的镇岳行：1金 2木 3水 4火 5土，0=取消设定（自动）。</summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public int Element { get; set; }

        [MemoryPackOrder(2)]
        [Id(2)]
        public bool Success { get; set; }

        [MemoryPackOrder(3)]
        [Id(3)]
        public string Message { get; set; } = "";

        /// <summary>生效后的镇岳行（被拒时为原值）。</summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public int GuardElement { get; set; }

        /// <summary>距下次可切换还剩几秒（客户端据此灰按钮并倒计时）。</summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public float NextChangeInSeconds { get; set; }

        [MemoryPackOrder(6)]
        [Id(6)]
        public MessageType Type { get; set; } = MessageType.SetGuardElement;

        [MemoryPackOrder(7)]
        [Id(7)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    #endregion
}
