using System.ComponentModel;

namespace Horizon.IM.Message.Enums
{
    /// <summary>
    /// IM消息类型枚举
    /// </summary>
    public enum IMMessageType : ushort
    {
        #region 私聊/熟人聊天消息 (1-99)

        /// <summary>
        /// 私聊消息发送
        /// </summary>
        [Description("私聊消息发送")]
        PrivateChatSend = 1,

        /// <summary>
        /// 私聊消息通知（服务器推送给接收方）
        /// </summary>
        [Description("私聊消息通知")]
        PrivateChatNotify = 2,

        /// <summary>
        /// 消息送达/已读回执
        /// </summary>
        [Description("消息回执")]
        ChatAck = 3,

        /// <summary>
        /// 消息撤回
        /// </summary>
        [Description("消息撤回")]
        ChatRecall = 4,

        /// <summary>
        /// 已读回执
        /// </summary>
        [Description("已读回执")]
        ChatReadReceipt = 5,

        /// <summary>
        /// 正在输入指示
        /// </summary>
        [Description("正在输入")]
        TypingIndicator = 6,

        #endregion

        #region 群聊消息 (100-199)

        /// <summary>
        /// 群聊消息发送
        /// </summary>
        [Description("群聊消息发送")]
        GroupChatSend = 100,

        /// <summary>
        /// 群聊消息通知
        /// </summary>
        [Description("群聊消息通知")]
        GroupChatNotify = 101,

        /// <summary>
        /// 群聊消息撤回
        /// </summary>
        [Description("群聊消息撤回")]
        GroupChatRecall = 102,

        /// <summary>
        /// 创建群组请求
        /// </summary>
        [Description("创建群组请求")]
        GroupCreateRequest = 103,

        /// <summary>
        /// 创建群组响应
        /// </summary>
        [Description("创建群组响应")]
        GroupCreateResponse = 104,

        /// <summary>
        /// 加入群组请求
        /// </summary>
        [Description("加入群组请求")]
        GroupJoinRequest = 105,

        /// <summary>
        /// 加入群组响应
        /// </summary>
        [Description("加入群组响应")]
        GroupJoinResponse = 106,

        /// <summary>
        /// 退出群组请求
        /// </summary>
        [Description("退出群组请求")]
        GroupLeaveRequest = 107,

        /// <summary>
        /// 退出群组响应
        /// </summary>
        [Description("退出群组响应")]
        GroupLeaveResponse = 108,

        /// <summary>
        /// 解散群组请求
        /// </summary>
        [Description("解散群组请求")]
        GroupDisbandRequest = 109,

        /// <summary>
        /// 解散群组响应
        /// </summary>
        [Description("解散群组响应")]
        GroupDisbandResponse = 110,

        /// <summary>
        /// 群组信息更新
        /// </summary>
        [Description("群组信息更新")]
        GroupInfoUpdate = 111,

        /// <summary>
        /// 群成员列表请求
        /// </summary>
        [Description("群成员列表请求")]
        GroupMemberListRequest = 112,

        /// <summary>
        /// 群成员列表响应
        /// </summary>
        [Description("群成员列表响应")]
        GroupMemberListResponse = 113,

        /// <summary>
        /// 邀请用户加入群组请求
        /// </summary>
        [Description("邀请用户加入群组请求")]
        GroupInviteRequest = 114,

        /// <summary>
        /// 被邀请者响应邀请（接受/拒绝）
        /// </summary>
        [Description("被邀请者响应邀请")]
        GroupInviteResponse = 115,

        /// <summary>
        /// 管理员/群主审核加群申请
        /// </summary>
        [Description("审核加群申请")]
        GroupJoinApplyReview = 116,

        /// <summary>
        /// 推送：被邀请者收到入群邀请通知
        /// </summary>
        [Description("入群邀请通知")]
        GroupInviteNotify = 117,

        /// <summary>
        /// 推送：管理员/群主收到新的加群申请通知
        /// </summary>
        [Description("加群申请通知")]
        GroupJoinApplyNotify = 118,

        /// <summary>
        /// 查询当前用户待处理入群邀请列表请求
        /// </summary>
        [Description("待处理入群邀请列表请求")]
        GroupPendingInviteListRequest = 119,

        /// <summary>
        /// 查询当前用户待处理入群邀请列表响应
        /// </summary>
        [Description("待处理入群邀请列表响应")]
        GroupPendingInviteListResponse = 120,

        /// <summary>
        /// 推送：群主收到由非群主成员发起的入群邀请审批请求
        /// </summary>
        [Description("入群邀请审批通知")]
        GroupInviteApprovalNotify = 121,

        /// <summary>
        /// 群主审核由非群主成员发起的入群邀请
        /// </summary>
        [Description("入群邀请审批")]
        GroupInviteApprovalReview = 122,

        /// <summary>
        /// 推送：群组被解散通知（服务端→成员）
        /// </summary>
        [Description("群组解散通知")]
        GroupDisbandNotify = 123,

        /// <summary>
        /// 群主查询待审批邀请列表请求（客户端→服务端）
        /// </summary>
        [Description("待审批邀请列表请求")]
        GroupPendingApprovalListRequest = 124,

        /// <summary>
        /// 群主查询待审批邀请列表响应（服务端→客户端）
        /// </summary>
        [Description("待审批邀请列表响应")]
        GroupPendingApprovalListResponse = 125,

        /// <summary>
        /// 推送：原邀请人收到群主对其邀请的审批结果通知（同意或拒绝）
        /// </summary>
        [Description("邀请审批结果通知")]
        GroupInviteResultNotify = 126,

        #endregion

        #region 陌生人聊天消息 (200-299)

        /// <summary>
        /// 陌生人聊天请求（需验证实名认证与风险等级）
        /// </summary>
        [Description("陌生人聊天请求")]
        StrangerChatRequest = 200,

        /// <summary>
        /// 陌生人聊天响应
        /// </summary>
        [Description("陌生人聊天响应")]
        StrangerChatResponse = 201,

        /// <summary>
        /// 陌生人聊天消息发送
        /// </summary>
        [Description("陌生人聊天消息发送")]
        StrangerChatSend = 202,

        /// <summary>
        /// 陌生人聊天消息通知
        /// </summary>
        [Description("陌生人聊天消息通知")]
        StrangerChatNotify = 203,

        #endregion

        #region 联系人管理消息 (300-399)

        /// <summary>
        /// 添加联系人请求
        /// </summary>
        [Description("添加联系人请求")]
        ContactAddRequest = 300,

        /// <summary>
        /// 添加联系人响应
        /// </summary>
        [Description("添加联系人响应")]
        ContactAddResponse = 301,

        /// <summary>
        /// 删除联系人请求
        /// </summary>
        [Description("删除联系人请求")]
        ContactRemoveRequest = 302,

        /// <summary>
        /// 删除联系人响应
        /// </summary>
        [Description("删除联系人响应")]
        ContactRemoveResponse = 303,

        /// <summary>
        /// 屏蔽联系人请求
        /// </summary>
        [Description("屏蔽联系人请求")]
        ContactBlockRequest = 304,

        /// <summary>
        /// 屏蔽联系人响应
        /// </summary>
        [Description("屏蔽联系人响应")]
        ContactBlockResponse = 305,

        /// <summary>
        /// 联系人列表请求
        /// </summary>
        [Description("联系人列表请求")]
        ContactListRequest = 306,

        /// <summary>
        /// 联系人列表响应
        /// </summary>
        [Description("联系人列表响应")]
        ContactListResponse = 307,

        /// <summary>
        /// 联系人搜索请求
        /// </summary>
        [Description("联系人搜索请求")]
        ContactSearchRequest = 308,

        /// <summary>
        /// 联系人搜索响应
        /// </summary>
        [Description("联系人搜索响应")]
        ContactSearchResponse = 309,

        /// <summary>
        /// 联系人在线状态变更
        /// </summary>
        [Description("联系人在线状态变更")]
        ContactOnlineStatus = 310,

        /// <summary>
        /// 待处理好友申请列表请求
        /// </summary>
        [Description("待处理好友申请列表请求")]
        ContactPendingListRequest = 311,

        /// <summary>
        /// 待处理好友申请列表响应
        /// </summary>
        [Description("待处理好友申请列表响应")]
        ContactPendingListResponse = 312,

        /// <summary>
        /// 处理好友申请请求
        /// </summary>
        [Description("处理好友申请请求")]
        ContactRequestHandleRequest = 313,

        /// <summary>
        /// 处理好友申请响应
        /// </summary>
        [Description("处理好友申请响应")]
        ContactRequestHandleResponse = 314,

        /// <summary>
        /// 好友资料变更推送（服务器 → 客户端）
        /// </summary>
        [Description("好友资料变更推送")]
        ContactProfileUpdate = 315,

        /// <summary>
        /// 广播个人资料变更请求（客户端 → 网关）
        /// </summary>
        [Description("广播个人资料变更请求")]
        ContactProfileBroadcastRequest = 316,

        /// <summary>
        /// 联系人分组更新请求
        /// </summary>
        [Description("联系人分组更新请求")]
        ContactGroupUpdateRequest = 317,

        /// <summary>
        /// 联系人分组更新响应
        /// </summary>
        [Description("联系人分组更新响应")]
        ContactGroupUpdateResponse = 318,

        #endregion

        #region 会话管理消息 (400-499)

        /// <summary>
        /// 会话列表请求
        /// </summary>
        [Description("会话列表请求")]
        ConversationListRequest = 400,

        /// <summary>
        /// 会话列表响应
        /// </summary>
        [Description("会话列表响应")]
        ConversationListResponse = 401,

        /// <summary>
        /// 删除会话
        /// </summary>
        [Description("删除会话")]
        ConversationDelete = 402,

        /// <summary>
        /// 置顶会话
        /// </summary>
        [Description("置顶会话")]
        ConversationPin = 403,

        /// <summary>
        /// 会话免打扰
        /// </summary>
        [Description("会话免打扰")]
        ConversationMute = 404,

        #endregion

        #region 聊天记录消息 (500-599)

        /// <summary>
        /// 聊天记录查询请求
        /// </summary>
        [Description("聊天记录查询请求")]
        ChatHistoryQuery = 500,

        /// <summary>
        /// 聊天记录查询响应
        /// </summary>
        [Description("聊天记录查询响应")]
        ChatHistoryQueryResponse = 501,

        /// <summary>
        /// 清空聊天记录
        /// </summary>
        [Description("清空聊天记录")]
        ChatHistoryClear = 502,

        #endregion

        #region 通话消息 (600-699)

        /// <summary>
        /// 通话信令（发起/接听/拒绝/取消/忙线/挂断/媒体就绪/保活等）
        /// </summary>
        [Description("通话信令")]
        CallSignal = 600,

        /// <summary>
        /// 通话信令应答（服务端对信令的确认，含忙线/失败原因）
        /// </summary>
        [Description("通话信令应答")]
        CallSignalAck = 601,

        #endregion

        #region 系统消息 (900-999)

        /// <summary>
        /// IM心跳
        /// </summary>
        [Description("IM心跳")]
        Heartbeat = 900,

        /// <summary>
        /// IM心跳响应
        /// </summary>
        [Description("IM心跳响应")]
        HeartbeatResponse = 901,

        /// <summary>
        /// IM错误消息
        /// </summary>
        [Description("IM错误消息")]
        Error = 902,

        /// <summary>
        /// IM系统通知
        /// </summary>
        [Description("IM系统通知")]
        SystemNotification = 903,

        /// <summary>
        /// 未知消息
        /// </summary>
        [Description("未知消息")]
        Unknown = 0

        #endregion
    }
}
