namespace Horizon.Game.Message.Network
{
    /// <summary>
    /// 效果类型
    /// </summary>
    public enum EffectType
    {
        Buff,
        Debuff,
        DamageOverTime,
        HealOverTime,
        Control
    }

    /// <summary>
    /// 战斗日志类型
    /// </summary>
    public enum CombatLogType
    {
        Attack,
        SkillCast,
        Death,
        Resurrect,
        EffectApplied
    }

    /// <summary>
    /// 活动状态枚举
    /// </summary>
    public enum ActivityStatus
    {
        /// <summary>
        /// 未开始
        /// </summary>
        NotStarted = 0,

        /// <summary>
        /// 进行中
        /// </summary>
        Active = 1,

        /// <summary>
        /// 已结束
        /// </summary>
        Ended = 2,

        /// <summary>
        /// 已取消
        /// </summary>
        Cancelled = 3
    }

    /// <summary>
    /// 交易状态枚举
    /// </summary>
    public enum TradeStatus
    {
        /// <summary>
        /// 已创建
        /// </summary>
        Created = 0,

        /// <summary>
        /// 双方确认
        /// </summary>
        BothConfirmed = 1,

        /// <summary>
        /// 已完成
        /// </summary>
        Completed = 2,

        /// <summary>
        /// 已取消
        /// </summary>
        Cancelled = 3,

        /// <summary>
        /// 失败
        /// </summary>
        Failed = 4
    }

    /// <summary>
    /// 市场商品状态枚举
    /// </summary>
    public enum MarketListingStatus
    {
        /// <summary>
        /// 上架中
        /// </summary>
        Active = 0,

        /// <summary>
        /// 已售出
        /// </summary>
        Sold = 1,

        /// <summary>
        /// 已下架
        /// </summary>
        Delisted = 2,

        /// <summary>
        /// 已过期
        /// </summary>
        Expired = 3
    }

    /// <summary>
    /// 任务状态枚举
    /// </summary>
    public enum QuestProgressStatus
    {
        /// <summary>
        /// 进行中
        /// </summary>
        InProgress = 0,

        /// <summary>
        /// 可提交（所有目标已完成）
        /// </summary>
        ReadyToSubmit = 1,

        /// <summary>
        /// 已完成
        /// </summary>
        Completed = 2,

        /// <summary>
        /// 已放弃
        /// </summary>
        Abandoned = 3
    }

    /// <summary>
    /// 副本状态枚举
    /// </summary>
    public enum DungeonStatus
    {
        /// <summary>
        /// 等待中
        /// </summary>
        Waiting = 0,

        /// <summary>
        /// 进行中
        /// </summary>
        InProgress = 1,

        /// <summary>
        /// 已完成
        /// </summary>
        Completed = 2,

        /// <summary>
        /// 失败
        /// </summary>
        Failed = 3
    }

    /// <summary>
    /// 副本难度枚举
    /// </summary>
    public enum DungeonDifficulty
    {
        /// <summary>
        /// 普通
        /// </summary>
        Normal = 0,

        /// <summary>
        /// 困难
        /// </summary>
        Hard = 1,

        /// <summary>
        /// 英雄
        /// </summary>
        Heroic = 2,

        /// <summary>
        /// 地狱
        /// </summary>
        Hell = 3
    }

    /// <summary>
    /// 游戏事件流命名空间常量
    /// </summary>
    public static class GameStreamNamespaces
    {
        /// <summary>角色事件（登录、登出、升级等）</summary>
        public const string CharacterEvents = "CharacterEvents";

        /// <summary>战斗事件（攻击、死亡、复活等）</summary>
        public const string CombatEvents = "CombatEvents";

        /// <summary>社交事件（好友、公会、组队等）</summary>
        public const string SocialEvents = "SocialEvents";

        /// <summary>系统事件（服务器状态、活动等）</summary>
        public const string SystemEvents = "SystemEvents";
    }

    /// <summary>
    /// 游戏事件类型枚举
    /// </summary>
    public enum GameEventType
    {
        // 角色事件
        CharacterLogin = 100,
        CharacterLogout = 101,
        CharacterLevelUp = 102,
        CharacterCreated = 103,

        // 战斗事件
        CombatDamageDealt = 200,
        CombatPlayerKill = 201,
        CombatPlayerDeath = 202,
        CombatPlayerResurrect = 203,
        CombatSkillCast = 204,

        // 社交事件
        GuildCreated = 300,
        GuildMemberJoined = 301,
        TeamCreated = 302,
        FriendAdded = 303,
        TeamMemberJoined = 304,
        TeamMemberLeft = 305,
        TeamDisbanded = 306,
        TeamDungeonEntered = 307,

        // 系统事件
        ServerStatusChanged = 400,
        ActivityStarted = 401,
        ActivityEnded = 402,
        DungeonCompleted = 403,
        QuestCompleted = 404,

        // 排行榜事件
        RankingUpdated = 500,
        RankingReset = 501,

        // 邮件事件
        MailSent = 600,
        MailReceived = 601,

        // 成就事件
        AchievementUnlocked = 700,
        AchievementProgressUpdated = 701
    }

    /// <summary>
    /// 排行榜类型枚举
    /// </summary>
    public enum RankingType
    {
        /// <summary>
        /// 战力排行
        /// </summary>
        CombatPower = 0,

        /// <summary>
        /// 等级排行
        /// </summary>
        Level = 1,

        /// <summary>
        /// 财富排行
        /// </summary>
        Wealth = 2,

        /// <summary>
        /// 成就点数排行
        /// </summary>
        AchievementPoints = 3,

        /// <summary>
        /// PVP积分排行
        /// </summary>
        PvpScore = 4
    }

    /// <summary>
    /// 邮件状态枚举
    /// </summary>
    public enum MailStatus
    {
        /// <summary>
        /// 未读
        /// </summary>
        Unread = 0,

        /// <summary>
        /// 已读
        /// </summary>
        Read = 1,

        /// <summary>
        /// 已领取附件
        /// </summary>
        Claimed = 2,

        /// <summary>
        /// 已删除
        /// </summary>
        Deleted = 3
    }

    /// <summary>
    /// 邮件类型枚举
    /// </summary>
    public enum MailType
    {
        /// <summary>
        /// 系统邮件
        /// </summary>
        System = 0,

        /// <summary>
        /// 玩家邮件
        /// </summary>
        Player = 1,

        /// <summary>
        /// 公会邮件
        /// </summary>
        Guild = 2,

        /// <summary>
        /// 活动奖励邮件
        /// </summary>
        ActivityReward = 3
    }

    /// <summary>
    /// 成就类型枚举
    /// </summary>
    public enum AchievementCategory
    {
        /// <summary>
        /// 战斗成就
        /// </summary>
        Combat = 0,

        /// <summary>
        /// 社交成就
        /// </summary>
        Social = 1,

        /// <summary>
        /// 探索成就
        /// </summary>
        Exploration = 2,

        /// <summary>
        /// 收集成就
        /// </summary>
        Collection = 3,

        /// <summary>
        /// 成长成就
        /// </summary>
        Growth = 4
    }

    /// <summary>
    /// 游戏事件发布器接口 — 用于向Orleans Stream发布游戏事件
    /// </summary>
    public interface IGameEventPublisher
    {
        /// <summary>发布角色事件</summary>
        Task PublishCharacterEventAsync(GameEvent gameEvent);

        /// <summary>发布战斗事件</summary>
        Task PublishCombatEventAsync(GameEvent gameEvent);

        /// <summary>发布社交事件</summary>
        Task PublishSocialEventAsync(GameEvent gameEvent);

        /// <summary>发布系统事件</summary>
        Task PublishSystemEventAsync(GameEvent gameEvent);
    }
}
