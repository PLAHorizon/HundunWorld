using Horizon.Game.Message.Enums;
using MemoryPack;
using Orleans;
using System;
using System.Collections.Generic;

namespace Horizon.Game.Message.Network
{
    #region 武侠游戏特有消息

    /// <summary>
    /// 门派技能信息（扩展版）
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class SectSkillInfo : MessageUnion, INetworkMessage
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
        /// 技能等级
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public int Level { get; set; }

        /// <summary>
        /// 技能描述
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public string Description { get; set; } = "";

        /// <summary>
        /// 学习条件
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public SkillPrerequisites Prerequisites { get; set; } = new();

        [MemoryPackOrder(5)]
        [Id(5)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    /// <summary>
    /// 技能前置条件
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class SkillPrerequisites : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 需要等级
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public int RequiredLevel { get; set; }

        /// <summary>
        /// 需要内力
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public int RequiredInternalForce { get; set; }

        /// <summary>
        /// 需要金币
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public long RequiredGold { get; set; }

        /// <summary>
        /// 前置技能
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public List<int> RequiredSkills { get; set; } = new();

        /// <summary>
        /// 需要门派声望
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public int RequiredSectPrestige { get; set; }

        [MemoryPackOrder(5)]
        [Id(5)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    /// <summary>
    /// 武功秘籍消息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class MartialArtsManualMessage : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 秘籍ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public int ManualId { get; set; }

        /// <summary>
        /// 秘籍名称
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string ManualName { get; set; } = "";

        /// <summary>
        /// 秘籍品质
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public ItemQuality Quality { get; set; }

        /// <summary>
        /// 秘籍类型
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public MartialArtType Type { get; set; }

        /// <summary>
        /// 所属门派
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public int SectId { get; set; }

        /// <summary>
        /// 所属门派名称
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public string SectName { get; set; } = "";

        /// <summary>
        /// 威力系数
        /// </summary>
        [MemoryPackOrder(6)]
        [Id(6)]
        public float PowerCoefficient { get; set; }

        /// <summary>
        /// 修炼难度
        /// </summary>
        [MemoryPackOrder(7)]
        [Id(7)]
        public int Difficulty { get; set; }

        /// <summary>
        /// 修炼效果描述
        /// </summary>
        [MemoryPackOrder(8)]
        [Id(8)]
        public string EffectDescription { get; set; } = "";

        /// <summary>
        /// 特殊效果
        /// </summary>
        [MemoryPackOrder(9)]
        [Id(9)]
        public List<MartialArtsEffect> SpecialEffects { get; set; } = new();

        [MemoryPackOrder(10)]
        [Id(10)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    /// <summary>
    /// 武功效果
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class MartialArtsEffect : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 效果名称
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public string EffectName { get; set; } = "";

        /// <summary>
        /// 效果类型
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public MartialArtsEffectType EffectType { get; set; }

        /// <summary>
        /// 效果值
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public float Value { get; set; }

        /// <summary>
        /// 持续时间
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public long Duration { get; set; }

        [MemoryPackOrder(4)]
        [Id(4)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    /// <summary>
    /// 物品品质
    /// </summary>
    public enum ItemQuality : byte
    {
        /// <summary>
        /// 普通
        /// </summary>
        Common = 0,

        /// <summary>
        /// 精良
        /// </summary>
        Fine = 1,

        /// <summary>
        /// 稀有
        /// </summary>
        Rare = 2,

        /// <summary>
        /// 史诗
        /// </summary>
        Epic = 3,

        /// <summary>
        /// 传说
        /// </summary>
        Legendary = 4,

        /// <summary>
        /// 神话
        /// </summary>
        Mythical = 5
    }

    /// <summary>
    /// 武功类型
    /// </summary>
    public enum MartialArtType : byte
    {
        /// <summary>
        /// 内功
        /// </summary>
        Internal = 0,

        /// <summary>
        /// 外功
        /// </summary>
        External = 1,

        /// <summary>
        /// 轻功
        /// </summary>
        QingGong = 2,

        /// <summary>
        /// 毒功
        /// </summary>
        Poison = 3,

        /// <summary>
        /// 医术
        /// </summary>
        Medicine = 4,

        /// <summary>
        /// 剑法
        /// </summary>
        Swordsmanship = 5,

        /// <summary>
        /// 刀法
        /// </summary>
        SaberTechnique = 6,

        /// <summary>
        /// 棍法
        /// </summary>
        StaffTechnique = 7,

        /// <summary>
        /// 拳法
        /// </summary>
        FistTechnique = 8,

        /// <summary>
        /// 奇门
        /// </summary>
        Oddity = 9
    }

    /// <summary>
    /// 武功效果类型
    /// </summary>
    public enum MartialArtsEffectType : byte
    {
        /// <summary>
        /// 攻击力提升
        /// </summary>
        AttackBoost = 0,

        /// <summary>
        /// 防御力提升
        /// </summary>
        DefenseBoost = 1,

        /// <summary>
        /// 生命值提升
        /// </summary>
        HpBoost = 2,

        /// <summary>
        /// 法力值提升
        /// </summary>
        MpBoost = 3,

        /// <summary>
        /// 移动速度提升
        /// </summary>
        SpeedBoost = 4,

        /// <summary>
        /// 暴击率提升
        /// </summary>
        CriticalRateBoost = 5,

        /// <summary>
        /// 闪避率提升
        /// </summary>
        DodgeRateBoost = 6,

        /// <summary>
        /// 内力回复提升
        /// </summary>
        InternalForceRegenBoost = 7,

        /// <summary>
        /// 技能冷却缩减
        /// </summary>
        CooldownReduction = 8,

        /// <summary>
        /// 伤害减免
        /// </summary>
        DamageReduction = 9,

        /// <summary>
        /// 免疫控制
        /// </summary>
        ControlImmunity = 10,

        /// <summary>
        /// 吸血效果
        /// </summary>
        LifeSteal = 11,

        /// <summary>
        /// 反弹伤害
        /// </summary>
        DamageReflect = 12
    }

    /// <summary>
    /// 五行系统消息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class WuXingSystemMessage : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 玩家ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong PlayerId { get; set; }

        /// <summary>
        /// 金属性
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public int Metal { get; set; }

        /// <summary>
        /// 木属性
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public int Wood { get; set; }

        /// <summary>
        /// 水属性
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public int Water { get; set; }

        /// <summary>
        /// 火属性
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public int Fire { get; set; }

        /// <summary>
        /// 土属性
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public int Earth { get; set; }

        /// <summary>
        /// 五行总和
        /// </summary>
        [MemoryPackOrder(6)]
        [Id(6)]
        public int Total { get; set; }

        /// <summary>
        /// 五行平衡度
        /// </summary>
        [MemoryPackOrder(7)]
        [Id(7)]
        public float Balance { get; set; }

        /// <summary>
        /// 五行相生相克效果
        /// </summary>
        [MemoryPackOrder(8)]
        [Id(8)]
        public Dictionary<WuXingRelation, int> Relations { get; set; } = new();

        [MemoryPackOrder(9)]
        [Id(9)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    /// <summary>
    /// 五行关系
    /// </summary>
    public enum WuXingRelation : byte
    {
        /// <summary>
        /// 金生水
        /// </summary>
        MetalGeneratesWater = 0,

        /// <summary>
        /// 水生木
        /// </summary>
        WaterGeneratesWood = 1,

        /// <summary>
        /// 木生火
        /// </summary>
        WoodGeneratesFire = 2,

        /// <summary>
        /// 火生土
        /// </summary>
        FireGeneratesEarth = 3,

        /// <summary>
        /// 土生金
        /// </summary>
        EarthGeneratesMetal = 4,

        /// <summary>
        /// 金克木
        /// </summary>
        MetalControlsWood = 5,

        /// <summary>
        /// 木克土
        /// </summary>
        WoodControlsEarth = 6,

        /// <summary>
        /// 土克水
        /// </summary>
        EarthControlsWater = 7,

        /// <summary>
        /// 水克火
        /// </summary>
        WaterControlsFire = 8,

        /// <summary>
        /// 火克金
        /// </summary>
        FireControlsMetal = 9
    }

    #endregion
}