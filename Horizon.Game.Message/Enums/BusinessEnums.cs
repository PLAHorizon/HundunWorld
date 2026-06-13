using System;

namespace Horizon.Game.Message.Enums
{
    /// <summary>
    /// 性别枚举
    /// </summary>
    public enum Gender
    {
        /// <summary>
        /// 未知
        /// </summary>
        Unknown,
        
        /// <summary>
        /// 男性
        /// </summary>
        Male,
        
        /// <summary>
        /// 女性
        /// </summary>
        Female
    }

    /// <summary>
    /// 角色类型枚举
    /// </summary>
    public enum RoleType
    {
        /// <summary>
        /// 普通用户
        /// </summary>
        User,
        
        /// <summary>
        /// 管理员
        /// </summary>
        Admin,
        
        /// <summary>
        /// 超级管理员
        /// </summary>
        SuperAdmin,
        
        /// <summary>
        /// 版主
        /// </summary>
        Moderator,
        
        /// <summary>
        /// 编辑
        /// </summary>
        Editor
    }

    /// <summary>
    /// 消息状态枚举
    /// </summary>
    public enum MessageState
    {
        /// <summary>
        /// 草稿
        /// </summary>
        Draft,
        
        /// <summary>
        /// 已发送
        /// </summary>
        Sent,
        
        /// <summary>
        /// 已读
        /// </summary>
        Read,
        
        /// <summary>
        /// 已删除
        /// </summary>
        Deleted,
        
        /// <summary>
        /// 已归档
        /// </summary>
        Archived
    }

    /// <summary>
        /// 用户类型枚举
    /// </summary>
    public enum UserTypeEnum
    {
        /// <summary>
        /// 普通用户
        /// </summary>
        Normal,
        
        /// <summary>
        /// VIP用户
        /// </summary>
        VIP,
        
        /// <summary>
        /// 企业用户
        /// </summary>
        Enterprise,
        
        /// <summary>
        /// 测试用户
        /// </summary>
        Test
    }

    /// <summary>
    /// 关系状态枚举
    /// </summary>
    public enum RelationshipStatus
    {
        /// <summary>
        /// 单身
        /// </summary>
        Single,
        
        /// <summary>
        /// 恋爱中
        /// </summary>
        InRelationship,
        
        /// <summary>
        /// 已婚
        /// </summary>
        Married,
        
        /// <summary>
        /// 离异
        /// </summary>
        Divorced,
        
        /// <summary>
        /// 丧偶
        /// </summary>
        Widowed
    }

    /// <summary>
    /// 星座枚举
    /// </summary>
    public enum Constellation
    {
        /// <summary>
        /// 白羊座
        /// </summary>
        Aries,
        
        /// <summary>
        /// 金牛座
        /// </summary>
        Taurus,
        
        /// <summary>
        /// 双子座
        /// </summary>
        Gemini,
        
        /// <summary>
        /// 巨蟹座
        /// </summary>
        Cancer,
        
        /// <summary>
        /// 狮子座
        /// </summary>
        Leo,
        
        /// <summary>
        /// 处女座
        /// </summary>
        Virgo,
        
        /// <summary>
        /// 天秤座
        /// </summary>
        Libra,
        
        /// <summary>
        /// 天蝎座
        /// </summary>
        Scorpio,
        
        /// <summary>
        /// 射手座
        /// </summary>
        Sagittarius,
        
        /// <summary>
        /// 摩羯座
        /// </summary>
        Capricorn,
        
        /// <summary>
        /// 水瓶座
        /// </summary>
        Aquarius,
        
        /// <summary>
        /// 双鱼座
        /// </summary>
        Pisces
    }

    /// <summary>
    /// 平台类型枚举
    /// </summary>
    public enum PlatformType
    {
        /// <summary>
        /// Web平台
        /// </summary>
        Web,
        
        /// <summary>
        /// 移动端
        /// </summary>
        Mobile,
        
        /// <summary>
        /// 桌面端
        /// </summary>
        Desktop,
        
        /// <summary>
        /// 游戏主机
        /// </summary>
        Console,
        
        /// <summary>
        /// 嵌入式设备
        /// </summary>
        Embedded
    }

   

    /// <summary>
    /// 事件状态枚举
    /// </summary>
    public enum EventStatus
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
        /// 已结束
        /// </summary>
        Completed,
        
        /// <summary>
        /// 已取消
        /// </summary>
        Cancelled,
        
        /// <summary>
        /// 已暂停
        /// </summary>
        Paused
    }

    /// <summary>
    /// 反馈类型枚举
    /// </summary>
    public enum FeedbackType
    {
        /// <summary>
        /// 功能建议
        /// </summary>
        FeatureSuggestion,
        
        /// <summary>
        /// 错误报告
        /// </summary>
        BugReport,
        
        /// <summary>
        /// 用户体验反馈
        /// </summary>
        UserExperience,
        
        /// <summary>
        /// 性能反馈
        /// </summary>
        Performance,
        
        /// <summary>
        /// 其他反馈
        /// </summary>
        Other
    }

    /// <summary>
    /// 审核状态枚举
    /// </summary>
    public enum AuditStatus
    {
        /// <summary>
        /// 待审核
        /// </summary>
        Pending,
        
        /// <summary>
        /// 审核通过
        /// </summary>
        Approved,
        
        /// <summary>
        /// 审核拒绝
        /// </summary>
        Rejected,
        
        /// <summary>
        /// 审核中
        /// </summary>
        InReview
    }

    /// <summary>
    /// 文章状态枚举
    /// </summary>
    public enum ArticleStatus
    {
        /// <summary>
        /// 草稿
        /// </summary>
        Draft,
        
        /// <summary>
        /// 已发布
        /// </summary>
        Published,
        
        /// <summary>
        /// 已删除
        /// </summary>
        Deleted,
        
        /// <summary>
        /// 已归档
        /// </summary>
        Archived,
        
        /// <summary>
        /// 待审核
        /// </summary>
        PendingReview
    }

    /// <summary>
    /// 订单状态枚举
    /// </summary>
    public enum OrderStatus
    {
        /// <summary>
        /// 待支付
        /// </summary>
        PendingPayment,
        
        /// <summary>
        /// 已支付
        /// </summary>
        Paid,
        
        /// <summary>
        /// 已发货
        /// </summary>
        Shipped,
        
        /// <summary>
        /// 已完成
        /// </summary>
        Completed,
        
        /// <summary>
        /// 已取消
        /// </summary>
        Cancelled,
        
        /// <summary>
        /// 已退款
        /// </summary>
        Refunded
    }

    /// <summary>
    /// 订单类型枚举
    /// </summary>
    public enum OrderType
    {
        /// <summary>
        /// 商品订单
        /// </summary>
        Product,
        
        /// <summary>
        /// 服务订单
        /// </summary>
        Service,
        
        /// <summary>
        /// 订阅订单
        /// </summary>
        Subscription,
        
        /// <summary>
        /// 数字商品订单
        /// </summary>
        Digital,
        
        /// <summary>
        /// 虚拟商品订单
        /// </summary>
        Virtual
    }

    /// <summary>
    /// 评论类型枚举
    /// </summary>
    public enum CommentType
    {
        /// <summary>
        /// 文章评论
        /// </summary>
        Article,
        
        /// <summary>
        /// 产品评论
        /// </summary>
        Product,
        
        /// <summary>
        /// 服务评论
        /// </summary>
        Service,
        
        /// <summary>
        /// 用户评论
        /// </summary>
        User,
        
        /// <summary>
        /// 系统评论
        /// </summary>
        System
    }

    /// <summary>
    /// 收藏类型枚举
    /// </summary>
    public enum CollectionType
    {
        /// <summary>
        /// 文章收藏
        /// </summary>
        Article,
        
        /// <summary>
        /// 产品收藏
        /// </summary>
        Product,
        
        /// <summary>
        /// 用户收藏
        /// </summary>
        User,
        
        /// <summary>
        /// 标签收藏
        /// </summary>
        Tag,
        
        /// <summary>
        /// 分类收藏
        /// </summary>
        Category
    }

    public enum FlowerUserType
    {
        Normal = 0,
        Merchant = 1,
        Admin = 2
    }

    public enum SubscriptionLevel
    {
        Free = 0,
        Basic = 1,
        Premium = 2,
        VIP = 3
    }

    public enum ShopAuditStatus
    {
        Unusable = 0,
        Pending = 1,
        Approved = 2,
        Refused = 3,
        Opened = 4,
        Frozen = 5,
        Expired = 6
    }

    public enum ProductAuditStatus
    {
        Pending = 0,
        Approved = 1,
        Refused = 2
    }

    public enum OrderOperateStatus
    {
        PendingPayment = 0,
        PendingDelivery = 1,
        Shipped = 2,
        Completed = 3,
        Closed = 4
    }

    public enum RefundStatus
    {
        PendingAudit = 0,
        SellerAgreed = 1,
        SellerRefused = 2,
        Refunding = 3,
        RefundCompleted = 4,
        RefundClosed = 5
    }

    public enum FreightValuationMethod
    {
        ByPiece = 0,
        ByWeight = 1,
        ByVolume = 2
    }

    public enum RefundMode
    {
        RefundOnly = 0,
        ReturnAndRefund = 1
    }

    public enum MerchantStage
    {
        Agreement = 0,
        CompanyInfo = 1,
        BankAccount = 2,
        ShopInfo = 3,
        Finished = 4
    }

    public enum CouponType
    {
        CashCoupon = 0,
        DiscountCoupon = 1
    }

    public enum CouponRecordStatus
    {
        Unused = 0,
        Used = 1,
        Expired = 2
    }

    public enum FullDiscountStatus
    {
        Inactive = 0,
        Active = 1
    }

    public enum CashDepositStatus
    {
        Pending = 0,
        Paid = 1,
        Deducted = 2,
        Refunded = 3
    }

    public enum BusinessCategoryAuditStatus
    {
        Pending = 0,
        Approved = 1,
        Refused = 2
    }

    public enum ComplaintStatus
    {
        Pending = 0,
        Processing = 1,
        Resolved = 2,
        Closed = 3
    }

    public enum WithdrawStatus
    {
        PendingAudit = 0,
        Approved = 1,
        Refused = 2,
        Paid = 3
    }

    public enum ShopAccountType
    {
        Income = 0,
        Expense = 1
    }

    public enum PendingSettlementStatus
    {
        Pending = 0,
        Settled = 1
    }

    public enum BrandAuditStatus
    {
        Pending = 0,
        Approved = 1,
        Refused = 2
    }

    public enum ReturnShipmentStatus
    {
        WaitingReturn = 0,
        Shipped = 1,
        Received = 2
    }

    public enum LogisticsStatus
    {
        NoTrack = 0,
        Collected = 1,
        InTransit = 2,
        Delivering = 3,
        Signed = 4,
        Abnormal = 5
    }

    public enum SettlementDetailStatus
    {
        Normal = 0,
        RefundDeducted = 1
    }

    public enum OrderRefundStatus
    {
        None = 0,
        Refunding = 1,
        PartialRefunded = 2,
        Refunded = 3
    }
}