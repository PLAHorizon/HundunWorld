using System;

namespace Horizon.Game.Message.Enums
{
    /// <summary>
    /// 角色属性类型枚举
    /// </summary>
    public enum AttributeType
    {
        // 基础属性
        Strength = 0,    // 力量
        Agility = 1,     // 敏捷
        Intelligence = 2, // 智力
        Constitution = 3, // 体质
        Luck = 4,        // 幸运

        // 战斗属性
        Attack = 10,     // 攻击力
        Defense = 11,    // 防御力
        MagicAttack = 12,// 魔法攻击
        MagicDefense = 13,// 魔法防御
        CriticalRate = 14,// 暴击率
        CriticalDamage = 15,// 暴击伤害
        HitRate = 16,    // 命中率
        DodgeRate = 17,  // 闪避率

        // 生存属性
        MaxHealth = 20,  // 最大生命值
        MaxMana = 21,    // 最大魔法值
        HealthRegen = 22,// 生命恢复
        ManaRegen = 23,  // 魔法恢复

        // 移动属性
        MoveSpeed = 30,  // 移动速度
        AttackSpeed = 31,// 攻击速度
        CastSpeed = 32,  // 施法速度

        // 特殊属性
        Experience = 40, // 经验值
        Gold = 41,       // 金币
        Fame = 42,       // 声望
        Energy = 43,      // 精力
        Health,
        Mana,
    }

    /// <summary>
    /// 属性修改器类型
    /// </summary>
    public enum AttributeModifierType
    {
        BaseValue,       // 基础值
        FlatBonus,       // 固定加成
        PercentBonus,    // 百分比加成
        FinalBonus       // 最终加成
    }
    /// <summary>
    /// 经验类型枚举
    /// </summary>
    public enum ExperienceType
    {
        Combat = 0,       // 战斗经验
        Quest = 1,        // 任务经验
        Exploration = 2,  // 探索经验
        Crafting = 3,     // 制作经验
        Social = 4,       // 社交经验
        Achievement = 5,  // 成就经验
        Bonus = 6,        // 奖励经验
        Penalty = 7       // 惩罚经验
    }

    /// <summary>
    /// 等级奖励类型
    /// </summary>
    public enum LevelRewardType
    {
        AttributePoints = 0,  // 属性点
        SkillPoints = 1,      // 技能点
        Health = 2,           // 生命值
        Mana = 3,             // 魔法值
        Ability = 4,          // 特殊能力
        Item = 5,             // 物品奖励
        Gold = 6,             // 金币
        Reputation = 7        // 声望
    }

    /// <summary>
    /// 战斗状态枚举
    /// </summary>
    public enum CombatState
    {
        Idle = 0,         // 空闲
        InCombat = 1,     // 战斗中
        Attacking = 2,    // 攻击中
        Casting = 3,      // 施法中
        Stunned = 4,      // 眩晕
        Dead = 5,         // 死亡
        Invulnerable = 6  // 无敌
    }

    /// <summary>
    /// 伤害类型枚举
    /// </summary>
    public enum DamageType
    {
        Physical = 0,     // 物理伤害
        Magic = 1,        // 魔法伤害
        Fire = 2,         // 火焰伤害
        Ice = 3,          // 冰霜伤害
        Lightning = 4,    // 闪电伤害
        Poison = 5,       // 毒素伤害
        Holy = 6,         // 神圣伤害
        Dark = 7,         // 暗黑伤害
        True = 8          // 真实伤害（无视防御）
    }

    /// <summary>
    /// 效果类型枚举
    /// </summary>
    public enum EffectType
    {
        // 正面效果
        Heal = 0,         // 治疗
        Shield = 1,       // 护盾
        Regeneration = 2, // 再生
        Haste = 3,        // 加速
        Strength = 4,     // 力量提升
        Protection = 5,   // 保护

        // 负面效果
        Damage = 10,      // 持续伤害
        Poison = 11,      // 中毒
        Burn = 12,        // 燃烧
        Freeze = 13,      // 冰冻
        Slow = 14,        // 减速
        Weakness = 15,    // 虚弱
        Silence = 16,     // 沉默
        Stun = 17,        // 眩晕
        Root = 18,        // 定身
        Blind = 19,       // 致盲

        // 特殊效果
        Invisibility = 20,// 隐身
        Teleport = 21,    // 传送
        Transform = 22,   // 变形
        Summon = 23       // 召唤
    }
    /// <summary>
    /// 装备类型枚举
    /// </summary>
    public enum EquipmentType
    {
        /// <summary>
        /// 武器
        /// </summary>
        Weapon,

        /// <summary>
        /// 头盔
        /// </summary>
        Helmet,

        /// <summary>
        /// 胸甲
        /// </summary>
        Chest,

        /// <summary>
        /// 护腿
        /// </summary>
        Legs,

        /// <summary>
        /// 手套
        /// </summary>
        Gloves,

        /// <summary>
        /// 靴子
        /// </summary>
        Boots,

        /// <summary>
        /// 饰品
        /// </summary>
        Accessory,

        /// <summary>
        /// 特殊装备
        /// </summary>
        Special
    }

    /// <summary>
    /// 装备品质枚举
    /// </summary>
    public enum EquipmentQuality
    {
        /// <summary>
        /// 普通
        /// </summary>
        Common,

        /// <summary>
        /// 优秀
        /// </summary>
        Uncommon,

        /// <summary>
        /// 稀有
        /// </summary>
        Rare,

        /// <summary>
        /// 史诗
        /// </summary>
        Epic,

        /// <summary>
        /// 传说
        /// </summary>
        Legendary,

        /// <summary>
        /// 神话
        /// </summary>
        Mythic
    }

    /// <summary>
    /// 游戏角色类型枚举
    /// </summary>
    public enum GameRoleKind
    {
        /// <summary>
        /// 战士
        /// </summary>
        Warrior,

        /// <summary>
        /// 法师
        /// </summary>
        Mage,

        /// <summary>
        /// 射手
        /// </summary>
        Archer,

        /// <summary>
        /// 刺客
        /// </summary>
        Assassin,

        /// <summary>
        /// 牧师
        /// </summary>
        Priest,

        /// <summary>
        /// 坦克
        /// </summary>
        Tank,

        /// <summary>
        /// 辅助
        /// </summary>
        Support
    }

    /// <summary>
    /// 聊天类型枚举
    /// </summary>
    public enum ChatKind
    {
        /// <summary>
        /// 世界聊天
        /// </summary>
        World = 0,

        /// <summary>
        /// 队伍聊天
        /// </summary>
        Party = 1,

        /// <summary>
        /// 公会聊天
        /// </summary>
        Guild = 2,

        /// <summary>
        /// 私聊
        /// </summary>
        Private = 3,

        /// <summary>
        /// 系统消息
        /// </summary>
        System = 4,

        /// <summary>
        /// 区域聊天
        /// </summary>
        Area = 5
    }

    /// <summary>
    /// 游戏消息代码枚举
    /// </summary>
    public enum GameMessageCode : ushort
    {
        /// <summary>
        /// 成功
        /// </summary>
        Success = 0,

        /// <summary>
        /// 通用错误
        /// </summary>
        GeneralError = 1,

        /// <summary>
        /// 网络错误
        /// </summary>
        NetworkError = 2,

        /// <summary>
        /// 认证错误
        /// </summary>
        AuthenticationError = 3,

        /// <summary>
        /// 权限错误
        /// </summary>
        PermissionError = 4,

        /// <summary>
        /// 数据错误
        /// </summary>
        DataError = 5,

        /// <summary>
        /// 逻辑错误
        /// </summary>
        LogicError = 6
    }

    /// <summary>
    /// 游戏区域服务器状态枚举
    /// </summary>
    public enum GameAreaServerStatus
    {
        /// <summary>
        /// 离线
        /// </summary>
        Offline,

        /// <summary>
        /// 在线
        /// </summary>
        Online,

        /// <summary>
        /// 维护中
        /// </summary>
        Maintenance,

        /// <summary>
        /// 拥挤
        /// </summary>
        Crowded,

        /// <summary>
        /// 爆满
        /// </summary>
        Full
    }

    /// <summary>
    /// 验证结果类型枚举
    /// </summary>
    public enum VerificationResultType
    {
        /// <summary>
        /// 验证成功
        /// </summary>
        Success,

        /// <summary>
        /// 验证失败
        /// </summary>
        Failed,

        /// <summary>
        /// 验证过期
        /// </summary>
        Expired,

        /// <summary>
        /// 验证码错误
        /// </summary>
        CodeError,

        /// <summary>
        /// 验证码已使用
        /// </summary>
        CodeUsed
    }

    /// <summary>
    /// 用户信息类型枚举
    /// </summary>
    public enum UserInfoType
    {
        /// <summary>
        /// 基本信息
        /// </summary>
        Basic,

        /// <summary>
        /// 扩展信息
        /// </summary>
        Extended,

        /// <summary>
        /// 隐私信息
        /// </summary>
        Private,

        /// <summary>
        /// 安全信息
        /// </summary>
        Security
    }

    /// <summary>
    /// 星星类型枚举
    /// </summary>
    public enum StarsType
    {
        /// <summary>
        /// 普通星星
        /// </summary>
        Normal,

        /// <summary>
        /// 稀有星星
        /// </summary>
        Rare,

        /// <summary>
        /// 史诗星星
        /// </summary>
        Epic,

        /// <summary>
        /// 传说星星
        /// </summary>
        Legendary
    }

    /// <summary>
    /// 装备附加属性类型枚举
    /// </summary>
    public enum EquipmentAttachAttributKind
    {
        /// <summary>
        /// 攻击力
        /// </summary>
        Attack,

        /// <summary>
        /// 防御力
        /// </summary>
        Defense,

        /// <summary>
        /// 生命值
        /// </summary>
        Health,

        /// <summary>
        /// 魔法值
        /// </summary>
        Mana,

        /// <summary>
        /// 暴击率
        /// </summary>
        CriticalRate,

        /// <summary>
        /// 暴击伤害
        /// </summary>
        CriticalDamage,

        /// <summary>
        /// 攻击速度
        /// </summary>
        AttackSpeed,

        /// <summary>
        /// 移动速度
        /// </summary>
        MovementSpeed
    }

    /// <summary>
    /// 装备附加槽位类型枚举
    /// </summary>
    public enum EquipmentAttachSlotKind
    {
        /// <summary>
        /// 主要槽位
        /// </summary>
        Primary,

        /// <summary>
        /// 次要槽位
        /// </summary>
        Secondary,

        /// <summary>
        /// 特殊槽位
        /// </summary>
        Special,

        /// <summary>
        /// 隐藏槽位
        /// </summary>
        Hidden
    }
}