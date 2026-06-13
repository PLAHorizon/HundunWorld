using Horizon.Game.Message.Enums;
using MemoryPack;
using Orleans;
using System;
using System.Collections.Generic;

namespace Horizon.Game.Message.Network
{
    #region 战斗相关消息

    /// <summary>
    /// 攻击消息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class AttackMessage : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 攻击者ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong AttackerId { get; set; }

        /// <summary>
        /// 目标ID
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public ulong TargetId { get; set; }

        /// <summary>
        /// 攻击类型
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public int AttackType { get; set; }

        /// <summary>
        /// 伤害值
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public int Damage { get; set; }

        /// <summary>
        /// 是否暴击
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public bool IsCritical { get; set; }

        /// <summary>
        /// 元素类型
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public int ElementType { get; set; }

        /// <summary>
        /// 起始位置
        /// </summary>
        [MemoryPackOrder(6)]
        [Id(6)]
        public Position StartPosition { get; set; } = new();

        /// <summary>
        /// 冲击位置
        /// </summary>
        [MemoryPackOrder(7)]
        [Id(7)]
        public Position ImpactPosition { get; set; } = new();

        /// <summary>
        /// 技能ID
        /// </summary>
        [MemoryPackOrder(8)]
        [Id(8)]
        public int SkillId { get; set; }

        [MemoryPackOrder(9)]
        [Id(9)]
        public MessageType Type { get; set; } = MessageType.Attack;
        [MemoryPackOrder(10)]
        [Id(10)]
        public ServiceType ServiceType { get; set; } = ServiceType.Combat;
    }

    /// <summary>
    /// 技能施放消息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class SkillCastMessage : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 施法者ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong CasterId { get; set; }

        /// <summary>
        /// 技能ID
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public int SkillId { get; set; }

        /// <summary>
        /// 目标ID列表
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public List<ulong> TargetIds { get; set; } = new();

        /// <summary>
        /// 施法位置
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public Position CastPosition { get; set; } = new();

        /// <summary>
        /// 起始位置
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public Position StartPosition { get; set; } = new();

        /// <summary>
        /// 目标位置
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public Position TargetPosition { get; set; } = new();

        /// <summary>
        /// 施法时间
        /// </summary>
        [MemoryPackOrder(6)]
        [Id(6)]
        public long CastTime { get; set; }

        /// <summary>
        /// 技能范围
        /// </summary>
        [MemoryPackOrder(7)]
        [Id(7)]
        public float Range { get; set; }

        /// <summary>
        /// 能量消耗
        /// </summary>
        [MemoryPackOrder(8)]
        [Id(8)]
        public float EnergyCost { get; set; }

        /// <summary>
        /// 是否为范围技能
        /// </summary>
        [MemoryPackOrder(9)]
        [Id(9)]
        public bool IsAreaSkill { get; set; }

        /// <summary>
        /// 是否成功
        /// </summary>
        [MemoryPackOrder(10)]
        [Id(10)]
        public bool Success { get; set; }

        /// <summary>
        /// 消息
        /// </summary>
        [MemoryPackOrder(11)]
        [Id(11)]
        public string Message { get; set; } = "";

        /// <summary>
        /// 目标实体ID
        /// </summary>
        [MemoryPackOrder(12)]
        [Id(12)]
        public ulong TargetEntityId { get; set; }

        [MemoryPackOrder(13)]
        [Id(13)]
        public MessageType Type { get; set; } = MessageType.SkillCast;
        [MemoryPackOrder(14)]
        [Id(14)]
        public ServiceType ServiceType { get; set; } = ServiceType.Combat;
    }

    /// <summary>
    /// 受伤消息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class DamageMessage : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 受伤者ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong VictimId { get; set; }

        /// <summary>
        /// 攻击者ID
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public ulong AttackerId { get; set; }

        /// <summary>
        /// 伤害值
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public int Damage { get; set; }

        /// <summary>
        /// 剩余血量
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public int RemainingHealth { get; set; }

        /// <summary>
        /// 是否暴击
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public bool IsCritical { get; set; }

        /// <summary>
        /// 是否闪避
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public bool IsDodged { get; set; }

        /// <summary>
        /// 是否格挡
        /// </summary>
        [MemoryPackOrder(6)]
        [Id(6)]
        public bool IsBlocked { get; set; }

        /// <summary>
        /// 冲击位置
        /// </summary>
        [MemoryPackOrder(7)]
        [Id(7)]
        public Position ImpactPosition { get; set; } = new();

        /// <summary>
        /// 元素类型
        /// </summary>
        [MemoryPackOrder(8)]
        [Id(8)]
        public int ElementType { get; set; }

        [MemoryPackOrder(9)]
        [Id(9)]
        public MessageType Type { get; set; } = MessageType.Damage;
        [MemoryPackOrder(10)]
        [Id(10)]
        public ServiceType ServiceType { get; set; } = ServiceType.Combat;
    }

    /// <summary>
    /// 死亡消息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class DeathMessage : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 死亡者ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong DeceasedId { get; set; }

        /// <summary>
        /// 杀手ID
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public ulong KillerId { get; set; }

        /// <summary>
        /// 死亡原因
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public string Cause { get; set; } = "";

        /// <summary>
        /// 死亡位置
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public Position DeathPosition { get; set; } = new();

        [MemoryPackOrder(4)]
        [Id(4)]
        public MessageType Type { get; set; } = MessageType.Death;
        [MemoryPackOrder(5)]
        [Id(5)]
        public ServiceType ServiceType { get; set; } = ServiceType.Combat;
    }

    /// <summary>
    /// 复活消息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class ResurrectMessage : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 复活者ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong ResurrectedId { get; set; }

        /// <summary>
        /// 复活位置
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public Position ResurrectPosition { get; set; } = new();

        /// <summary>
        /// 复活类型
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public int ResurrectType { get; set; }

        /// <summary>
        /// 剩余血量
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public float RemainingHealth { get; set; }

        /// <summary>
        /// 最大血量
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public float MaxHealth { get; set; }

        [MemoryPackOrder(5)]
        [Id(5)]
        public MessageType Type { get; set; } = MessageType.Resurrect;
        [MemoryPackOrder(6)]
        [Id(6)]
        public ServiceType ServiceType { get; set; } = ServiceType.Combat;
    }

    #endregion

    #region 轻功与内功消息

    /// <summary>
    /// 轻功消息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class QingGongMessage : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 角色ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong CharacterId { get; set; }

        /// <summary>
        /// 轻功技能ID
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public int QingGongSkillId { get; set; }

        /// <summary>
        /// 起点位置
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public Position StartPosition { get; set; } = new();

        /// <summary>
        /// 目标位置
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public Position TargetPosition { get; set; } = new();

        /// <summary>
        /// 移动轨迹点
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public List<Position> PathPoints { get; set; } = new();

        [MemoryPackOrder(5)]
        [Id(5)]
        public MessageType Type { get; set; } = MessageType.QingGong;
        [MemoryPackOrder(6)]
        [Id(6)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    /// <summary>
    /// 内功消息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class NeiGongMessage : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 角色ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong CharacterId { get; set; }

        /// <summary>
        /// 内功技能ID
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public int NeiGongSkillId { get; set; }

        /// <summary>
        /// 内力值变化
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public int NeiLiChange { get; set; }

        /// <summary>
        /// 当前内力值
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public int CurrentNeiLi { get; set; }

        /// <summary>
        /// 效果持续时间（毫秒）
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public long Duration { get; set; }

        [MemoryPackOrder(5)]
        [Id(5)]
        public MessageType Type { get; set; } = MessageType.NeiGong;
        [MemoryPackOrder(6)]
        [Id(6)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    #endregion

    #region 招式连击消息

    /// <summary>
    /// 招式连击消息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class ComboAttackMessage : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 攻击者ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong AttackerId { get; set; }

        /// <summary>
        /// 连击序列
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public List<int> ComboSequence { get; set; } = new();

        /// <summary>
        /// 目标ID
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public ulong TargetId { get; set; }

        /// <summary>
        /// 总伤害
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public int TotalDamage { get; set; }

        /// <summary>
        /// 连击倍数
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public float ComboMultiplier { get; set; }

        [MemoryPackOrder(5)]
        [Id(5)]
        public MessageType Type { get; set; } = MessageType.ComboAttack;
        [MemoryPackOrder(6)]
        [Id(6)]
        public ServiceType ServiceType { get; set; } = ServiceType.Combat;
    }

    #endregion

    #region 防御消息

    /// <summary>
    /// 防御消息（格挡/闪避）
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class DefenseMessage : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 防御者ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong DefenderId { get; set; }

        /// <summary>
        /// 攻击者ID
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public ulong AttackerId { get; set; }

        /// <summary>
        /// 防御类型（0=格挡，1=闪避）
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public int DefenseType { get; set; }

        /// <summary>
        /// 防御效果值
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public int DefenseValue { get; set; }

        /// <summary>
        /// 是否成功
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public bool IsSuccess { get; set; }

        [MemoryPackOrder(5)]
        [Id(5)]
        public MessageType Type { get; set; } = MessageType.Defense;
        [MemoryPackOrder(6)]
        [Id(6)]
        public ServiceType ServiceType { get; set; } = ServiceType.Combat;
    }

    #endregion

    #region 属性更新消息

    /// <summary>
    /// 属性更新消息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class AttributeUpdateMessage : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 角色ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong CharacterId { get; set; }

        /// <summary>
        /// 属性变化字典
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public Dictionary<string, object> AttributeChanges { get; set; } = new();

        /// <summary>
        /// 更新时间
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public long UpdateTime { get; set; }

        [MemoryPackOrder(3)]
        [Id(3)]
        public MessageType Type { get; set; } = MessageType.AttributeUpdate;
        [MemoryPackOrder(4)]
        [Id(4)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    #endregion

    #region 地图玩家消息

    /// <summary>
    /// 地图玩家信息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class MapPlayer : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 角色ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong CharacterId { get; set; }

        /// <summary>
        /// 角色名称
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string CharacterName { get; set; } = "";

        /// <summary>
        /// 角色等级
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public int Level { get; set; }

        /// <summary>
        /// 职业
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public string Profession { get; set; } = "";

        /// <summary>
        /// 当前位置
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public Position CurrentPosition { get; set; } = new();

        /// <summary>
        /// 朝向
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public float Rotation { get; set; }

        /// <summary>
        /// 移动速度
        /// </summary>
        [MemoryPackOrder(6)]
        [Id(6)]
        public float MoveSpeed { get; set; }

        /// <summary>
        /// 是否在线
        /// </summary>
        [MemoryPackOrder(7)]
        [Id(7)]
        public bool IsOnline { get; set; }

        /// <summary>
        /// 帮派ID
        /// </summary>
        [MemoryPackOrder(8)]
        [Id(8)]
        public int GuildId { get; set; }

        /// <summary>
        /// 帮派名称
        /// </summary>
        [MemoryPackOrder(9)]
        [Id(9)]
        public string GuildName { get; set; } = "";

        /// <summary>
        /// 战斗状态
        /// </summary>
        [MemoryPackOrder(10)]
        [Id(10)]
        public bool InCombat { get; set; }

        /// <summary>
        /// 骑乘状态
        /// </summary>
        [MemoryPackOrder(11)]
        [Id(11)]
        public bool IsRiding { get; set; }

        /// <summary>
        /// 可见性状态
        /// </summary>
        [MemoryPackOrder(12)]
        [Id(12)]
        public bool IsVisible { get; set; }

        [MemoryPackOrder(13)]
        [Id(13)]
        public MessageType Type { get; set; } = MessageType.MapPlayer;
        [MemoryPackOrder(14)]
        [Id(14)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    #endregion

    #region 实体同步消息

    /// <summary>
    /// 网络实体类型
    /// Note: 此枚举在Flax客户端 (HundunWorld.Game.ECS.Components.NetworkEntityType) 中也有定义，
    /// 因为客户端使用独立的构建系统，无法直接引用此共享库。两处定义需保持同步。
    /// </summary>
    public enum NetworkEntityType
    {
        Unknown = 0,
        LocalPlayer = 1,
        RemotePlayer = 2,
        Npc = 3,
        Monster = 4,
        Projectile = 5,
        Item = 6
    }

    /// <summary>
    /// 实体生成消息
    /// 当实体进入玩家视野时由服务端发送
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class EntitySpawnMessage : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 实体的网络ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong EntityId { get; set; }

        /// <summary>
        /// 实体类型
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public NetworkEntityType EntityType { get; set; }

        /// <summary>
        /// 实体名称
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public string EntityName { get; set; } = "";

        /// <summary>
        /// 等级
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public int Level { get; set; }

        /// <summary>
        /// 生成位置
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public Position SpawnPosition { get; set; } = new();

        /// <summary>
        /// 当前生命值
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public float CurrentHealth { get; set; }

        /// <summary>
        /// 最大生命值
        /// </summary>
        [MemoryPackOrder(6)]
        [Id(6)]
        public float MaxHealth { get; set; }

        [MemoryPackOrder(7)]
        [Id(7)]
        public MessageType Type { get; set; } = MessageType.EntitySpawn;
        [MemoryPackOrder(8)]
        [Id(8)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    /// <summary>
    /// 实体销毁消息
    /// 当实体离开玩家视野或被销毁时由服务端发送
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class EntityDespawnMessage : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 实体的网络ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong EntityId { get; set; }

        /// <summary>
        /// 销毁原因
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public DespawnReason Reason { get; set; }

        [MemoryPackOrder(2)]
        [Id(2)]
        public MessageType Type { get; set; } = MessageType.EntityDespawn;
        [MemoryPackOrder(3)]
        [Id(3)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    /// <summary>
    /// 实体销毁原因
    /// </summary>
    public enum DespawnReason
    {
        /// <summary>
        /// 离开视野
        /// </summary>
        OutOfRange = 0,

        /// <summary>
        /// 死亡
        /// </summary>
        Death = 1,

        /// <summary>
        /// 传送
        /// </summary>
        Teleport = 2,

        /// <summary>
        /// 登出
        /// </summary>
        Logout = 3
    }

    #endregion

    #region Buff/效果同步消息

    /// <summary>
    /// 效果同步操作类型
    /// </summary>
    public enum EffectSyncAction
    {
        /// <summary>
        /// 施加效果
        /// </summary>
        Apply = 0,

        /// <summary>
        /// 移除效果
        /// </summary>
        Remove = 1,

        /// <summary>
        /// 刷新效果
        /// </summary>
        Refresh = 2,

        /// <summary>
        /// 叠加效果
        /// </summary>
        Stack = 3
    }

    /// <summary>
    /// 效果同步消息
    /// 同步Buff/Debuff/控制效果状态
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class EffectSyncMessage : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 目标实体ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong TargetId { get; set; }

        /// <summary>
        /// 来源实体ID
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public ulong SourceId { get; set; }

        /// <summary>
        /// 效果模板ID
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public int EffectId { get; set; }

        /// <summary>
        /// 效果名称
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public string EffectName { get; set; } = "";

        /// <summary>
        /// 同步操作类型
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public EffectSyncAction Action { get; set; }

        /// <summary>
        /// 剩余持续时间（秒）
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public float RemainingDuration { get; set; }

        /// <summary>
        /// 当前叠加层数
        /// </summary>
        [MemoryPackOrder(6)]
        [Id(6)]
        public int Stacks { get; set; }

        /// <summary>
        /// 效果数值
        /// </summary>
        [MemoryPackOrder(7)]
        [Id(7)]
        public float Value { get; set; }

        /// <summary>
        /// 是否为百分比数值
        /// </summary>
        [MemoryPackOrder(8)]
        [Id(8)]
        public bool IsPercentage { get; set; }

        [MemoryPackOrder(9)]
        [Id(9)]
        public MessageType Type { get; set; } = MessageType.EffectSync;
        [MemoryPackOrder(10)]
        [Id(10)]
        public ServiceType ServiceType { get; set; } = ServiceType.Combat;
    }

    #endregion

    #region AOI视野更新消息

    /// <summary>
    /// AOI更新消息
    /// 批量通知客户端视野范围内的实体变化
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class AoiUpdateMessage : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 玩家ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong PlayerId { get; set; }

        /// <summary>
        /// 进入视野的实体列表
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public List<AoiEntityInfo> EnteredEntities { get; set; } = new();

        /// <summary>
        /// 离开视野的实体ID列表
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public List<ulong> ExitedEntityIds { get; set; } = new();

        /// <summary>
        /// 视野范围半径
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public float ViewRange { get; set; }

        [MemoryPackOrder(4)]
        [Id(4)]
        public MessageType Type { get; set; } = MessageType.AoiUpdate;
        [MemoryPackOrder(5)]
        [Id(5)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    /// <summary>
    /// AOI实体信息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class AoiEntityInfo
    {
        /// <summary>
        /// 实体ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong EntityId { get; set; }

        /// <summary>
        /// 实体类型
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public NetworkEntityType EntityType { get; set; }

        /// <summary>
        /// 实体名称
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public string Name { get; set; } = "";

        /// <summary>
        /// 位置
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public Position Position { get; set; } = new();

        /// <summary>
        /// 等级
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public int Level { get; set; }

        /// <summary>
        /// 当前生命值
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public float CurrentHealth { get; set; }

        /// <summary>
        /// 最大生命值
        /// </summary>
        [MemoryPackOrder(6)]
        [Id(6)]
        public float MaxHealth { get; set; }
    }

    #endregion

    #region 移动速度验证消息

    /// <summary>
    /// 移动速度验证消息
    /// 服务端对客户端移动速度的反外挂校验结果
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class MovementSpeedValidationMessage : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 被验证的角色ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong CharacterId { get; set; }

        /// <summary>
        /// 是否通过验证
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public bool IsValid { get; set; }

        /// <summary>
        /// 实际测量速度
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public float MeasuredSpeed { get; set; }

        /// <summary>
        /// 服务端允许的最大速度
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public float MaxAllowedSpeed { get; set; }

        /// <summary>
        /// 校正后的位置（如果验证失败）
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public Position CorrectedPosition { get; set; } = new();

        /// <summary>
        /// 违规计数
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public int ViolationCount { get; set; }

        [MemoryPackOrder(6)]
        [Id(6)]
        public MessageType Type { get; set; } = MessageType.MovementSpeedValidation;
        [MemoryPackOrder(7)]
        [Id(7)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    #endregion

    #region 技能打断消息

    /// <summary>
    /// 技能打断原因
    /// </summary>
    public enum SkillInterruptReason
    {
        /// <summary>眩晕</summary>
        Stunned = 0,
        /// <summary>沉默</summary>
        Silenced = 1,
        /// <summary>击退</summary>
        KnockedBack = 2,
        /// <summary>死亡</summary>
        Death = 3,
        /// <summary>手动取消</summary>
        ManualCancel = 4,
        /// <summary>距离超出</summary>
        OutOfRange = 5
    }

    /// <summary>
    /// 技能打断消息
    /// 当角色技能施放被中断时由服务端发送
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class SkillInterruptMessage : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 被打断的角色ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong CharacterId { get; set; }

        /// <summary>
        /// 被打断的技能ID
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public int SkillId { get; set; }

        /// <summary>
        /// 打断来源ID（施加打断的角色或效果）
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public ulong InterruptSourceId { get; set; }

        /// <summary>
        /// 打断原因
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public SkillInterruptReason Reason { get; set; }

        /// <summary>
        /// 技能冷却是否重置
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public bool ResetCooldown { get; set; }

        /// <summary>
        /// 打断发生时间戳
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public long Timestamp { get; set; }

        [MemoryPackOrder(6)]
        [Id(6)]
        public MessageType Type { get; set; } = MessageType.SkillInterrupt;
        [MemoryPackOrder(7)]
        [Id(7)]
        public ServiceType ServiceType { get; set; } = ServiceType.Combat;
    }

    #endregion

    #region 好友系统消息（扩展）

    /// <summary>
    /// 好友操作类型
    /// </summary>
    public enum FriendOperationType
    {
        /// <summary>添加好友</summary>
        Add = 0,
        /// <summary>删除好友</summary>
        Remove = 1,
        /// <summary>接受好友请求</summary>
        Accept = 2,
        /// <summary>拒绝好友请求</summary>
        Reject = 3,
        /// <summary>屏蔽好友</summary>
        Block = 4,
        /// <summary>取消屏蔽</summary>
        Unblock = 5
    }

    /// <summary>
    /// 好友在线状态
    /// </summary>
    public enum FriendOnlineStatus
    {
        Offline = 0,
        Online = 1,
        Away = 2,
        Busy = 3
    }

    /// <summary>
    /// 好友列表消息
    /// 服务端发送的完整好友列表信息（使用新MessageType区分于FriendListUpdateMessage）
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class FriendListMessage : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 好友列表
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public List<FriendInfo> Friends { get; set; } = new();

        /// <summary>
        /// 待处理的好友请求列表
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public List<FriendInfo> PendingRequests { get; set; } = new();

        /// <summary>
        /// 好友数量上限
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public int MaxFriendCount { get; set; } = 100;

        [MemoryPackOrder(3)]
        [Id(3)]
        public MessageType Type { get; set; } = MessageType.FriendList;
        [MemoryPackOrder(4)]
        [Id(4)]
        public ServiceType ServiceType { get; set; } = ServiceType.Social;
    }

    /// <summary>
    /// 好友操作消息
    /// 客户端发起的好友操作请求和服务端响应
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class FriendOperationMessage : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 操作类型
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public FriendOperationType Operation { get; set; }

        /// <summary>
        /// 目标角色ID
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public ulong TargetCharacterId { get; set; }

        /// <summary>
        /// 目标角色名称（添加好友时使用）
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public string TargetName { get; set; } = "";

        /// <summary>
        /// 操作是否成功（响应消息使用）
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public bool Success { get; set; }

        /// <summary>
        /// 操作结果消息
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public string ResultMessage { get; set; } = "";

        [MemoryPackOrder(5)]
        [Id(5)]
        public MessageType Type { get; set; } = MessageType.FriendOperation;
        [MemoryPackOrder(6)]
        [Id(6)]
        public ServiceType ServiceType { get; set; } = ServiceType.Social;
    }

    #endregion

    #region 小地图消息

    /// <summary>
    /// 地图标记类型
    /// </summary>
    public enum MapMarkerType
    {
        /// <summary>传送点</summary>
        TeleportPoint = 0,
        /// <summary>任务NPC</summary>
        QuestNpc = 1,
        /// <summary>任务目标</summary>
        QuestObjective = 2,
        /// <summary>队友</summary>
        TeamMember = 3,
        /// <summary>Boss</summary>
        Boss = 4,
        /// <summary>商人</summary>
        Merchant = 5,
        /// <summary>自定义标记</summary>
        Custom = 6
    }

    /// <summary>
    /// 地图标记信息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class MapMarkerInfo
    {
        /// <summary>
        /// 标记ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public int MarkerId { get; set; }

        /// <summary>
        /// 标记类型
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public MapMarkerType MarkerType { get; set; }

        /// <summary>
        /// 标记名称
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public string Name { get; set; } = "";

        /// <summary>
        /// X坐标
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public float X { get; set; }

        /// <summary>
        /// Y坐标（高度）
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public float Y { get; set; }

        /// <summary>
        /// Z坐标
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public float Z { get; set; }

        /// <summary>
        /// 是否可交互
        /// </summary>
        [MemoryPackOrder(6)]
        [Id(6)]
        public bool IsInteractable { get; set; }
    }

    /// <summary>
    /// 传送点消息
    /// 当前区域可用传送点信息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class TeleportPointMessage : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 传送点列表
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public List<MapMarkerInfo> TeleportPoints { get; set; } = new();

        /// <summary>
        /// 当前区域名称
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string AreaName { get; set; } = "";

        [MemoryPackOrder(2)]
        [Id(2)]
        public MessageType Type { get; set; } = MessageType.TeleportPoint;
        [MemoryPackOrder(3)]
        [Id(3)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    /// <summary>
    /// 小地图标记消息
    /// 服务端发送的小地图标记更新
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class MinimapMarkerMessage : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 标记列表
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public List<MapMarkerInfo> Markers { get; set; } = new();

        /// <summary>
        /// 是否全量更新（false表示增量更新）
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public bool IsFullUpdate { get; set; }

        /// <summary>
        /// 需要移除的标记ID列表（增量更新时使用）
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public List<int> RemovedMarkerIds { get; set; } = new();

        [MemoryPackOrder(3)]
        [Id(3)]
        public MessageType Type { get; set; } = MessageType.MinimapMarker;
        [MemoryPackOrder(4)]
        [Id(4)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    #endregion

    #region 聊天发送消息

    /// <summary>
    /// 聊天频道类型
    /// </summary>
    public enum ChatChannelType
    {
        /// <summary>世界频道</summary>
        World = 0,
        /// <summary>区域频道</summary>
        Area = 1,
        /// <summary>组队频道</summary>
        Team = 2,
        /// <summary>公会频道</summary>
        Guild = 3,
        /// <summary>私聊</summary>
        Whisper = 4,
        /// <summary>系统频道</summary>
        System = 5
    }

    /// <summary>
    /// 聊天消息发送
    /// 客户端发送的聊天消息请求
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class ChatSendMessage : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 发送者角色ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong SenderId { get; set; }

        /// <summary>
        /// 发送者名称
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string SenderName { get; set; } = "";

        /// <summary>
        /// 频道类型
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public ChatChannelType Channel { get; set; }

        /// <summary>
        /// 消息内容
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public string Content { get; set; } = "";

        /// <summary>
        /// 目标角色ID（私聊时使用）
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public ulong TargetId { get; set; }

        /// <summary>
        /// 发送时间戳
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public long Timestamp { get; set; }

        [MemoryPackOrder(6)]
        [Id(6)]
        public MessageType Type { get; set; } = MessageType.ChatSend;
        [MemoryPackOrder(7)]
        [Id(7)]
        public ServiceType ServiceType { get; set; } = ServiceType.Social;
    }

    #endregion


    #region NarrativePro
    /// <summary>
    /// NarrativePro Horizon消息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class NarrativeProHorizonMessage : MessageUnion,INetworkMessage
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public string JsonPayload { get; set; } = "";
        [MemoryPackOrder(1)]
        [Id(1)]
        public MessageType Type { get; set; } = MessageType.Quest;
        [MemoryPackOrder(2)]
        [Id(2)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;

        [MemoryPackConstructor]
        public NarrativeProHorizonMessage() : base()
        {
            Type = MessageType.Quest;
            ServiceType = ServiceType.Game;
        }

        public NarrativeProHorizonMessage(string jsonPayload) : this()
        {
            JsonPayload = jsonPayload;
        }
    }
    #endregion
}