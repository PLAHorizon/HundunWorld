using Horizon.Game.Message.Enums;
using Orleans;

namespace Horizon.Game.Message.Network
{
   

    /// <summary>
    /// 战斗日志类型
    /// </summary>
    public enum CombatLogType
    {
        Attack,
        SkillCast,
        Death,
        Resurrect,
        EffectApplied,
        Damage,
        Critical,
        Skill,
        Info
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
    /// 数据池数据类型枚举
    /// </summary>
    [GenerateSerializer]
    public enum DataPoolDataType
    {
        [Id(0)] MarketSnapshot = 0,
        [Id(1)] PricePrediction = 1,
        [Id(2)] TradeRecord = 2,
        [Id(3)] UserBehavior = 3,
        [Id(4)] AIChat = 4,
        [Id(5)] SensorData = 5,
        [Id(6)] AIOutput = 6,
        [Id(7)] AlertEvent = 7,
        [Id(8)] CollectionFailure = 8,
        [Id(9)] WeatherData = 9
    }

    /// <summary>
    /// 花卉类别枚举
    /// </summary>
    [GenerateSerializer]
    public enum FlowerCategory
    {
        [Id(0)] CutFlower = 0,
        [Id(1)] PottedPlant = 1,
        [Id(2)] Seedling = 2,
        [Id(3)] Bulb = 3,
        [Id(4)] DriedFlower = 4
    }

    /// <summary>
    /// 预警条件类型枚举
    /// </summary>
    [GenerateSerializer]
    public enum AlertConditionType
    {
        [Id(0)] PriceAbove = 0,
        [Id(1)] PriceBelow = 1,
        [Id(2)] PriceChangeAbove = 2,
        [Id(3)] PriceChangeBelow = 3
    }

    /// <summary>
    /// 预测时间尺度枚举
    /// </summary>
    [GenerateSerializer]
    public enum ForecastTimeScale
    {
        [Id(0)] ShortTerm = 0,
        [Id(1)] MediumTerm = 1
    }

    /// <summary>
    /// 通知渠道枚举
    /// </summary>
    [GenerateSerializer]
    public enum NotifyChannel
    {
        [Id(0)] WebSocket = 0,
        [Id(1)] SMS = 1,
        [Id(2)] WeChat = 2,
        [Id(3)] Email = 3
    }

    /// <summary>
    /// 数据源类型枚举
    /// </summary>
    [GenerateSerializer]
    public enum DataSourceType
    {
        [Id(0)] AuctionMarket = 0,
        [Id(1)] ECommerce = 1,
        [Id(2)] WeatherApi = 2,
        [Id(3)] SearchEngine = 3,
        [Id(4)] Manual = 4
    }

    /// <summary>
    /// 订单状态枚举
    /// </summary>
    [GenerateSerializer]
    public enum OrderStatus
    {
        [Id(0)] Pending = 0,
        [Id(1)] Paid = 1,
        [Id(2)] Shipped = 2,
        [Id(3)] Delivered = 3,
        [Id(4)] Completed = 4,
        [Id(5)] Cancelled = 5,
        [Id(6)] Refunding = 6
    }

    /// <summary>
    /// 支付渠道枚举
    /// </summary>
    [GenerateSerializer]
    public enum PaymentScene
    {
        [Id(0)] Native = 0,
        [Id(1)] JsApi = 1,
        [Id(2)] H5 = 2,
        [Id(3)] App = 3,
        [Id(4)] Page = 4,
        [Id(5)] Wap = 5
    }

    [GenerateSerializer]
    public enum PaymentChannel
    {
        [Id(0)] WechatPay = 0,
        [Id(1)] Alipay = 1
    }

    /// <summary>
    /// 退款状态枚举
    /// </summary>
    [GenerateSerializer]
    public enum RefundStatus
    {
        [Id(0)] Pending = 0,
        [Id(1)] Approved = 1,
        [Id(2)] Processing = 2,
        [Id(3)] Completed = 3,
        [Id(4)] Rejected = 4
    }

    /// <summary>
    /// 商户类型枚举
    /// </summary>
    [GenerateSerializer]
    public enum MerchantType
    {
        [Id(0)] Individual = 0,
        [Id(1)] Enterprise = 1
    }
}
