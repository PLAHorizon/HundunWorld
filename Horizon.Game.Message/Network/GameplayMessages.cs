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
}