using System;

namespace Horizon.Game.Message.Enums
{
    /// <summary>
    /// 物品类型枚举
    /// </summary>
    public enum ItemType
    {
        /// <summary>
        /// 武器
        /// </summary>
        Weapon,

        /// <summary>
        /// 护甲
        /// </summary>
        Armor,

        /// <summary>
        /// 消耗品
        /// </summary>
        Consumable,

        /// <summary>
        /// 材料
        /// </summary>
        Material,

        /// <summary>
        /// 任务物品
        /// </summary>
        Quest,

        /// <summary>
        /// 货币
        /// </summary>
        Currency,
        Potion,
        Food,
        Scroll,
    }

    /// <summary>
    /// 物品品质枚举
    /// </summary>
    public enum ItemQuality
    {
        /// <summary>
        /// 普通
        /// </summary>
        Common,

        /// <summary>
        /// 良好
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
        Legendary
    }

    /// <summary>
    /// 物品分类枚举
    /// </summary>
    public enum ItemCategory
    {
        /// <summary>
        /// 全部
        /// </summary>
        All,

        /// <summary>
        /// 武器
        /// </summary>
        Weapon,

        /// <summary>
        /// 护甲
        /// </summary>
        Armor,

        /// <summary>
        /// 消耗品
        /// </summary>
        Consumable,

        /// <summary>
        /// 材料
        /// </summary>
        Material,

        /// <summary>
        /// 任务物品
        /// </summary>
        Quest
    }

    /// <summary>
    /// 成就分类枚举
    /// </summary>
    public enum AchievementCategory
    {
        /// <summary>
        /// 战斗
        /// </summary>
        Combat,

        /// <summary>
        /// 探索
        /// </summary>
        Exploration,

        /// <summary>
        /// 社交
        /// </summary>
        Social,

        /// <summary>
        /// 收集
        /// </summary>
        Collection,

        /// <summary>
        /// 故事
        /// </summary>
        Story
    }

    /// <summary>
    /// 任务分类枚举
    /// </summary>
    public enum QuestCategory
    {
        /// <summary>
        /// 主线任务
        /// </summary>
        Main,

        /// <summary>
        /// 支线任务
        /// </summary>
        Side,

        /// <summary>
        /// 日常任务
        /// </summary>
        Daily,

        /// <summary>
        /// 周常任务
        /// </summary>
        Weekly,

        /// <summary>
        /// 活动任务
        /// </summary>
        Event
    }

    /// <summary>
    /// 商店分类枚举
    /// </summary>
    public enum ShopCategory
    {
        /// <summary>
        /// 武器
        /// </summary>
        Weapon,

        /// <summary>
        /// 护甲
        /// </summary>
        Armor,

        /// <summary>
        /// 消耗品
        /// </summary>
        Consumable,

        /// <summary>
        /// 材料
        /// </summary>
        Material,

        /// <summary>
        /// 特殊物品
        /// </summary>
        Special
    }

    /// <summary>
    /// 货币类型枚举
    /// </summary>
    public enum CurrencyType
    {
        /// <summary>
        /// 金币
        /// </summary>
        Gold,

        /// <summary>
        /// 银币
        /// </summary>
        Silver,

        /// <summary>
        /// 钻石
        /// </summary>
        Diamond,

        /// <summary>
        /// 荣誉
        /// </summary>
        Honor
    }

    /// <summary>
    /// 排行榜分类枚举
    /// </summary>
    public enum LeaderboardCategory
    {
        /// <summary>
        /// 等级
        /// </summary>
        Level,

        /// <summary>
        /// 战斗
        /// </summary>
        Combat,

        /// <summary>
        /// 财富
        /// </summary>
        Wealth
    }

    /// <summary>
    /// 时间范围枚举
    /// </summary>
    public enum TimeRange
    {
        /// <summary>
        /// 每日
        /// </summary>
        Daily,

        /// <summary>
        /// 每周
        /// </summary>
        Weekly,

        /// <summary>
        /// 每月
        /// </summary>
        Monthly,

        /// <summary>
        /// 全部时间
        /// </summary>
        AllTime
    }

    /// <summary>
    /// 地图标记类型枚举
    /// </summary>
    public enum MarkerType
    {
        /// <summary>
        /// 玩家
        /// </summary>
        Player,

        /// <summary>
        /// NPC
        /// </summary>
        NPC,

        /// <summary>
        /// 怪物
        /// </summary>
        Monster,

        /// <summary>
        /// 宝藏
        /// </summary>
        Treasure,

        /// <summary>
        /// 任务
        /// </summary>
        Quest,

        /// <summary>
        /// 路径点
        /// </summary>
        Waypoint
    }

    /// <summary>
    /// 门派枚举
    /// </summary>
    public enum Profession
    {
        /// <summary>
        /// 无门派
        /// </summary>
        None,

        /// <summary>
        /// 少林寺
        /// </summary>
        Shaolin,

        /// <summary>
        /// 武当派
        /// </summary>
        Wudang,

        /// <summary>
        /// 峨眉派
        /// </summary>
        Emei,

        /// <summary>
        /// 华山派
        /// </summary>
        Huashan,

        /// <summary>
        /// 逍遥派
        /// </summary>
        Xiaoyao,

        /// <summary>
        /// 丐帮
        /// </summary>
        Gaibang,

        /// <summary>
        /// 明教
        /// </summary>
        Mingjiao,

        /// <summary>
        /// 唐门
        /// </summary>
        Tangmen,

        /// <summary>
        /// 五毒教
        /// </summary>
        Wudu,

        /// <summary>
        /// 天山派
        /// </summary>
        Tianshan,

        /// <summary>
        /// 慕容世家
        /// </summary>
        Murong,

        /// <summary>
        /// 段氏皇族
        /// </summary>
        Duan,

        /// <summary>
        /// 星宿派
        /// </summary>
        Xingxiu,

        /// <summary>
        /// 灵鹫宫
        /// </summary>
        Lingjiu,

        /// <summary>
        /// 日月神教
        /// </summary>
        Riyue
    }

    /// <summary>
    /// 技能类型枚举
    /// </summary>
    public enum SkillType
    {
        /// <summary>
        /// 主动技能
        /// </summary>
        Active,

        /// <summary>
        /// 被动技能
        /// </summary>
        Passive,

        /// <summary>
        /// 特殊技能
        /// </summary>
        Special,
        /// <summary>
        /// 防御/格挡 技能
        /// </summary>
        Toggle,
        /// <summary>
        /// 主动攻击技能
        /// </summary>
        ActiveAttack,
        /// <summary>
        /// 被动强化技能
        /// </summary>
        PassiveEnhancement,
        /// <summary>
        /// 控制技能
        /// </summary>
        Control,
        /// <summary>
        /// 位移技能
        /// </summary>
        Dash,
        /// <summary>
        /// 辅助技能
        /// </summary>
        Support,
        /// <summary>
        /// 终结技
        /// </summary>
        Ultimate,
    }

    /// <summary>
    /// 技能等级枚举
    /// </summary>
    public enum SkillLevel
    {
        /// <summary>
        /// 初级
        /// </summary>
        Beginner,

        /// <summary>
        /// 中级
        /// </summary>
        Intermediate,

        /// <summary>
        /// 高级
        /// </summary>
        Advanced,

        /// <summary>
        /// 大师
        /// </summary>
        Master
    }

    /// <summary>
    /// 装备槽位枚举
    /// </summary>
    public enum EquipmentSlot
    {
        /// <summary>
        /// Indicates that no options or flags are set.
        /// </summary>
        None = 0,
        /// <summary>
        /// 头部
        /// </summary>
        Head,

        /// <summary>
        /// 胸部
        /// </summary>
        Chest,

        /// <summary>
        /// 腿部
        /// </summary>
        Legs,

        /// <summary>
        /// 手部
        /// </summary>
        Hands,

        /// <summary>
        /// 脚部
        /// </summary>
        Feet,

        /// <summary>
        /// 武器
        /// </summary>
        Weapon,

        /// <summary>
        /// 饰品
        /// </summary>
        Accessory,
        MainHand,
        OffHand,
        Ring1,
        Ring2,
        Necklace,
        Earring1,
        Earring2,
    }

    /// <summary>
    /// 物品稀有度枚举
    /// </summary>
    public enum ItemRarity
    {
        /// <summary>
        /// 普通
        /// </summary>
        Common,

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
        Legendary
    }

    /// <summary>
    /// 任务状态枚举
    /// </summary>
    public enum QuestStatus
    {
        /// <summary>
        /// 未开始
        /// </summary>
        NotStarted,

        /// <summary>
        /// 进行中
        /// </summary>
        InProgress,

        /// <summary>
        /// 已完成
        /// </summary>
        Completed,

        /// <summary>
        /// 已失败
        /// </summary>
        Failed
    }

    /// <summary>
    /// 成就状态枚举
    /// </summary>
    public enum AchievementStatus
    {
        /// <summary>
        /// 未解锁
        /// </summary>
        Locked,

        /// <summary>
        /// 已解锁
        /// </summary>
        Unlocked,

        /// <summary>
        /// 已完成
        /// </summary>
        Completed
    }

    /// <summary>
    /// 成就类型枚举
    /// </summary>
    public enum AchievementType
    {
        /// <summary>
        /// 战斗成就
        /// </summary>
        Combat,

        /// <summary>
        /// 探索成就
        /// </summary>
        Exploration,

        /// <summary>
        /// 收集成就
        /// </summary>
        Collection,

        /// <summary>
        /// 社交成就
        /// </summary>
        Social
    }

    /// <summary>
    /// 增益类型枚举
    /// </summary>
    public enum BuffType
    {
        /// <summary>
        /// 增益效果
        /// </summary>
        Buff,

        /// <summary>
        /// 减益效果
        /// </summary>
        Debuff,

        /// <summary>
        /// 中性效果
        /// </summary>
        Neutral
    }

    /// <summary>
    /// 增益效果枚举
    /// </summary>
    public enum BuffEffect
    {
        /// <summary>
        /// 增加攻击力
        /// </summary>
        IncreaseAttack,

        /// <summary>
        /// 增加防御力
        /// </summary>
        IncreaseDefense,

        /// <summary>
        /// 增加生命值
        /// </summary>
        IncreaseHealth,

        /// <summary>
        /// 增加移动速度
        /// </summary>
        IncreaseSpeed,

        /// <summary>
        /// 减少攻击力
        /// </summary>
        DecreaseAttack,

        /// <summary>
        /// 减少防御力
        /// </summary>
        DecreaseDefense,

        /// <summary>
        /// 减少生命值
        /// </summary>
        DecreaseHealth,

        /// <summary>
        /// 减少移动速度
        /// </summary>
        DecreaseSpeed
    }
}