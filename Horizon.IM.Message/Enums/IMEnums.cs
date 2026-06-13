using System.ComponentModel;

namespace Horizon.IM.Message.Enums
{
    /// <summary>
    /// 聊天关系类型（熟人/陌生人/群聊）
    /// </summary>
    public enum IMChatRelationType
    {
        /// <summary>
        /// 熟人/好友聊天
        /// </summary>
        [Description("熟人聊天")]
        Friend = 0,

        /// <summary>
        /// 陌生人聊天
        /// </summary>
        [Description("陌生人聊天")]
        Stranger = 1,

        /// <summary>
        /// 群组聊天
        /// </summary>
        [Description("群组聊天")]
        Group = 2
    }

    /// <summary>
    /// IM消息内容类型
    /// </summary>
    public enum IMContentType
    {
        /// <summary>
        /// 文本消息
        /// </summary>
        [Description("文本消息")]
        Text = 0,

        /// <summary>
        /// 图片消息
        /// </summary>
        [Description("图片消息")]
        Image = 1,

        /// <summary>
        /// 语音消息
        /// </summary>
        [Description("语音消息")]
        Audio = 2,

        /// <summary>
        /// 视频消息
        /// </summary>
        [Description("视频消息")]
        Video = 3,

        /// <summary>
        /// 文件消息
        /// </summary>
        [Description("文件消息")]
        File = 4,

        /// <summary>
        /// 位置消息
        /// </summary>
        [Description("位置消息")]
        Location = 5,

        /// <summary>
        /// 名片消息
        /// </summary>
        [Description("名片消息")]
        ContactCard = 6,

        /// <summary>
        /// 表情消息
        /// </summary>
        [Description("表情消息")]
        Emoji = 7,

        /// <summary>
        /// 红包消息
        /// </summary>
        [Description("红包消息")]
        RedPacket = 8,

        /// <summary>
        /// 系统消息
        /// </summary>
        [Description("系统消息")]
        System = 9,

        /// <summary>
        /// 自定义消息
        /// </summary>
        [Description("自定义消息")]
        Custom = 99
    }

    /// <summary>
    /// 实名认证状态
    /// 未实名认证的用户不能向陌生人发起聊天请求
    /// </summary>
    public enum IdentityVerificationStatus
    {
        /// <summary>
        /// 未实名认证（不能向陌生人发起聊天请求）
        /// </summary>
        [Description("未实名认证")]
        Unverified = 0,

        /// <summary>
        /// 已实名认证
        /// </summary>
        [Description("已实名认证")]
        Verified = 1,

        /// <summary>
        /// 已实名认证但受限制（被研判为风险人员）
        /// </summary>
        [Description("已认证受限")]
        VerifiedRestricted = 2
    }

    /// <summary>
    /// 用户风险等级
    /// 实名认证后被研判为失信人员、诈骗嫌疑人或犯罪分子的不允许发起陌生人聊天请求
    /// </summary>
    public enum UserRiskLevel
    {
        /// <summary>
        /// 正常用户
        /// </summary>
        [Description("正常")]
        Normal = 0,

        /// <summary>
        /// 失信人员
        /// </summary>
        [Description("失信人员")]
        Dishonest = 1,

        /// <summary>
        /// 诈骗嫌疑人
        /// </summary>
        [Description("诈骗嫌疑人")]
        FraudSuspect = 2,

        /// <summary>
        /// 犯罪分子
        /// </summary>
        [Description("犯罪分子")]
        Criminal = 3
    }

    /// <summary>
    /// 陌生人聊天拒绝原因
    /// </summary>
    public enum StrangerChatDeniedReason
    {
        /// <summary>
        /// 无拒绝原因（允许聊天）
        /// </summary>
        [Description("无")]
        None = 0,

        /// <summary>
        /// 未实名认证
        /// </summary>
        [Description("未实名认证")]
        NotVerified = 1,

        /// <summary>
        /// 发送方为失信人员
        /// </summary>
        [Description("失信人员")]
        SenderDishonest = 2,

        /// <summary>
        /// 发送方为诈骗嫌疑人
        /// </summary>
        [Description("诈骗嫌疑人")]
        SenderFraudSuspect = 3,

        /// <summary>
        /// 发送方为犯罪分子
        /// </summary>
        [Description("犯罪分子")]
        SenderCriminal = 4,

        /// <summary>
        /// 接收方已屏蔽陌生人消息
        /// </summary>
        [Description("接收方已屏蔽")]
        ReceiverBlocked = 5,

        /// <summary>
        /// 接收方关闭了陌生人消息功能
        /// </summary>
        [Description("接收方已关闭陌生人消息")]
        ReceiverDisabledStranger = 6,

        /// <summary>
        /// 发送频率超限
        /// </summary>
        [Description("频率限制")]
        RateLimited = 7,

        /// <summary>
        /// 接收方不存在
        /// </summary>
        [Description("用户不存在")]
        ReceiverNotFound = 8
    }

    /// <summary>
    /// IM消息状态
    /// </summary>
    public enum IMMessageStatus
    {
        /// <summary>
        /// 发送中
        /// </summary>
        [Description("发送中")]
        Sending = 0,

        /// <summary>
        /// 已发送（服务器已收到）
        /// </summary>
        [Description("已发送")]
        Sent = 1,

        /// <summary>
        /// 已送达（对方已收到）
        /// </summary>
        [Description("已送达")]
        Delivered = 2,

        /// <summary>
        /// 已读
        /// </summary>
        [Description("已读")]
        Read = 3,

        /// <summary>
        /// 发送失败
        /// </summary>
        [Description("发送失败")]
        Failed = 4,

        /// <summary>
        /// 已撤回
        /// </summary>
        [Description("已撤回")]
        Recalled = 5
    }

    /// <summary>
    /// 用户在线状态
    /// </summary>
    public enum IMOnlineStatus
    {
        /// <summary>
        /// 离线
        /// </summary>
        [Description("离线")]
        Offline = 0,

        /// <summary>
        /// 在线
        /// </summary>
        [Description("在线")]
        Online = 1,

        /// <summary>
        /// 离开
        /// </summary>
        [Description("离开")]
        Away = 2,

        /// <summary>
        /// 忙碌
        /// </summary>
        [Description("忙碌")]
        Busy = 3,

        /// <summary>
        /// 隐身
        /// </summary>
        [Description("隐身")]
        Invisible = 4
    }

    /// <summary>
    /// 联系人关系状态
    /// </summary>
    public enum IMContactRelation
    {
        /// <summary>
        /// 无关系
        /// </summary>
        [Description("无关系")]
        None = 0,

        /// <summary>
        /// 待验证（已发送好友请求）
        /// </summary>
        [Description("待验证")]
        PendingRequest = 1,

        /// <summary>
        /// 好友
        /// </summary>
        [Description("好友")]
        Friend = 2,

        /// <summary>
        /// 已屏蔽
        /// </summary>
        [Description("已屏蔽")]
        Blocked = 3,

        /// <summary>
        /// 已删除
        /// </summary>
        [Description("已删除")]
        Deleted = 4
    }

    /// <summary>
    /// 群组成员角色
    /// </summary>
    public enum IMGroupMemberRole
    {
        /// <summary>
        /// 普通成员
        /// </summary>
        [Description("普通成员")]
        Member = 0,

        /// <summary>
        /// 管理员
        /// </summary>
        [Description("管理员")]
        Admin = 1,

        /// <summary>
        /// 群主
        /// </summary>
        [Description("群主")]
        Owner = 2
    }

    /// <summary>
    /// IM错误码
    /// </summary>
    public enum IMErrorCode
    {
        /// <summary>
        /// 成功
        /// </summary>
        [Description("成功")]
        Success = 0,

        /// <summary>
        /// 未知错误
        /// </summary>
        [Description("未知错误")]
        Unknown = 1,

        /// <summary>
        /// 未实名认证，不允许向陌生人发起聊天
        /// </summary>
        [Description("未实名认证")]
        IdentityNotVerified = 100,

        /// <summary>
        /// 失信人员，不允许发起陌生人聊天
        /// </summary>
        [Description("失信人员限制")]
        DishonestRestricted = 101,

        /// <summary>
        /// 诈骗嫌疑人，不允许发起陌生人聊天
        /// </summary>
        [Description("诈骗嫌疑限制")]
        FraudSuspectRestricted = 102,

        /// <summary>
        /// 犯罪分子，不允许发起陌生人聊天
        /// </summary>
        [Description("犯罪限制")]
        CriminalRestricted = 103,

        /// <summary>
        /// 用户不存在
        /// </summary>
        [Description("用户不存在")]
        UserNotFound = 200,

        /// <summary>
        /// 已被对方屏蔽
        /// </summary>
        [Description("已被屏蔽")]
        BlockedByReceiver = 201,

        /// <summary>
        /// 对方关闭了陌生人消息
        /// </summary>
        [Description("陌生人消息已关闭")]
        StrangerChatDisabled = 202,

        /// <summary>
        /// 消息发送频率超限
        /// </summary>
        [Description("频率超限")]
        RateLimitExceeded = 300,

        /// <summary>
        /// 消息内容违规
        /// </summary>
        [Description("内容违规")]
        ContentViolation = 301,

        /// <summary>
        /// 群组不存在
        /// </summary>
        [Description("群组不存在")]
        GroupNotFound = 400,

        /// <summary>
        /// 非群组成员
        /// </summary>
        [Description("非群组成员")]
        NotGroupMember = 401,

        /// <summary>
        /// 群组已满
        /// </summary>
        [Description("群组已满")]
        GroupFull = 402,

        /// <summary>
        /// 权限不足
        /// </summary>
        [Description("权限不足")]
        PermissionDenied = 500
    }
}
