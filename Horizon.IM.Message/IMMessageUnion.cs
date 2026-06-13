using Horizon.IM.Message.Enums;
using Horizon.IM.Message.Network;
using MemoryPack;
using Orleans;

namespace Horizon.IM.Message
{
    /// <summary>
    /// IM消息联合体基类
    /// 所有IM网络消息类型都应继承此类
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    // ===== 私聊/熟人聊天消息 =====
    [MemoryPackUnion(0, typeof(IMPrivateChatSendMessage))]
    [MemoryPackUnion(1, typeof(IMPrivateChatNotifyMessage))]
    [MemoryPackUnion(2, typeof(IMChatAckMessage))]
    [MemoryPackUnion(3, typeof(IMChatRecallMessage))]
    [MemoryPackUnion(4, typeof(IMChatReadReceiptMessage))]
    [MemoryPackUnion(5, typeof(IMTypingIndicatorMessage))]
    // ===== 群聊消息 =====
    [MemoryPackUnion(6, typeof(IMGroupChatSendMessage))]
    [MemoryPackUnion(7, typeof(IMGroupChatNotifyMessage))]
    [MemoryPackUnion(8, typeof(IMGroupCreateRequest))]
    [MemoryPackUnion(9, typeof(IMGroupCreateResponse))]
    [MemoryPackUnion(10, typeof(IMGroupJoinRequest))]
    [MemoryPackUnion(11, typeof(IMGroupJoinResponse))]
    [MemoryPackUnion(12, typeof(IMGroupLeaveRequest))]
    [MemoryPackUnion(13, typeof(IMGroupLeaveResponse))]
    [MemoryPackUnion(14, typeof(IMGroupDisbandRequest))]
    [MemoryPackUnion(15, typeof(IMGroupDisbandResponse))]
    [MemoryPackUnion(16, typeof(IMGroupInfoUpdateMessage))]
    [MemoryPackUnion(17, typeof(IMGroupMemberListRequest))]
    [MemoryPackUnion(18, typeof(IMGroupMemberListResponse))]
    // ===== 陌生人聊天消息 =====
    [MemoryPackUnion(19, typeof(IMStrangerChatRequest))]
    [MemoryPackUnion(20, typeof(IMStrangerChatResponse))]
    [MemoryPackUnion(21, typeof(IMStrangerChatSendMessage))]
    [MemoryPackUnion(22, typeof(IMStrangerChatNotifyMessage))]
    // ===== 联系人管理消息 =====
    [MemoryPackUnion(23, typeof(IMContactAddRequest))]
    [MemoryPackUnion(24, typeof(IMContactAddResponse))]
    [MemoryPackUnion(25, typeof(IMContactRemoveRequest))]
    [MemoryPackUnion(26, typeof(IMContactRemoveResponse))]
    [MemoryPackUnion(27, typeof(IMContactBlockRequest))]
    [MemoryPackUnion(28, typeof(IMContactBlockResponse))]
    [MemoryPackUnion(29, typeof(IMContactListRequest))]
    [MemoryPackUnion(30, typeof(IMContactListResponse))]
    [MemoryPackUnion(31, typeof(IMContactSearchRequest))]
    [MemoryPackUnion(32, typeof(IMContactSearchResponse))]
    [MemoryPackUnion(33, typeof(IMContactOnlineStatusMessage))]
    [MemoryPackUnion(51, typeof(IMContactRequestHandleRequest))]
    [MemoryPackUnion(52, typeof(IMContactRequestHandleResponse))]
    [MemoryPackUnion(53, typeof(IMPendingContactRequestListRequest))]
    [MemoryPackUnion(54, typeof(IMPendingContactRequestListResponse))]
    [MemoryPackUnion(55, typeof(IMContactProfileUpdateMessage))]
    [MemoryPackUnion(56, typeof(IMContactProfileBroadcastRequest))]
    // ===== 联系人分组管理消息 =====
    [MemoryPackUnion(70, typeof(IMContactGroupUpdateRequest))]
    [MemoryPackUnion(71, typeof(IMContactGroupUpdateResponse))]
    // ===== 群组邀请/加群审核消息 =====
    [MemoryPackUnion(57, typeof(IMGroupInviteRequest))]
    [MemoryPackUnion(58, typeof(IMGroupInviteResponse))]
    [MemoryPackUnion(59, typeof(IMGroupJoinApplyReview))]
    [MemoryPackUnion(60, typeof(IMGroupInviteNotify))]
    [MemoryPackUnion(61, typeof(IMGroupJoinApplyNotify))]
    [MemoryPackUnion(62, typeof(IMGetPendingGroupInvitesRequest))]
    [MemoryPackUnion(63, typeof(IMGetPendingGroupInvitesResponse))]
    [MemoryPackUnion(64, typeof(IMGroupInviteApprovalNotify))]
    [MemoryPackUnion(65, typeof(IMGroupInviteApprovalReview))]
    [MemoryPackUnion(66, typeof(IMGroupDisbandNotify))]
    [MemoryPackUnion(67, typeof(IMGetPendingApprovalListRequest))]
    [MemoryPackUnion(68, typeof(IMGetPendingApprovalListResponse))]
    [MemoryPackUnion(69, typeof(IMGroupInviteResultNotify))]
    // ===== 会话管理消息 =====
    [MemoryPackUnion(34, typeof(IMConversationListRequest))]
    [MemoryPackUnion(35, typeof(IMConversationListResponse))]
    [MemoryPackUnion(36, typeof(IMConversationDeleteMessage))]
    [MemoryPackUnion(37, typeof(IMConversationPinMessage))]
    [MemoryPackUnion(38, typeof(IMConversationMuteMessage))]
    // ===== 聊天记录消息 =====
    [MemoryPackUnion(39, typeof(IMChatHistoryQueryRequest))]
    [MemoryPackUnion(40, typeof(IMChatHistoryQueryResponse))]
    [MemoryPackUnion(41, typeof(IMChatHistoryClearMessage))]
    // ===== 系统消息 =====
    [MemoryPackUnion(42, typeof(IMHeartbeatMessage))]
    [MemoryPackUnion(43, typeof(IMHeartbeatResponse))]
    [MemoryPackUnion(44, typeof(IMErrorMessage))]
    [MemoryPackUnion(45, typeof(IMSystemNotificationMessage))]
    // ===== 数据模型 =====
    [MemoryPackUnion(46, typeof(IMContactInfo))]
    [MemoryPackUnion(47, typeof(IMGroupInfo))]
    [MemoryPackUnion(48, typeof(IMConversationInfo))]
    [MemoryPackUnion(49, typeof(IMServiceToClientMessage))]
    [MemoryPackUnion(50, typeof(IMGroupMemberInfo))]
    public abstract partial class IMMessageUnion : IIMNetworkMessage
    {
        /// <summary>
        /// IM服务类型
        /// </summary>
        [MemoryPackOrder(254)]
        [Id(254)]
        public IMServiceType ServiceType { get; set; }

        /// <summary>
        /// IM消息类型
        /// </summary>
        [MemoryPackOrder(255)]
        [Id(255)]
        public IMMessageType Type { get; set; }
    }
}
