using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Orleans;

using Horizon.IM.Message;
using Horizon.IM.Message.Enums;
using Horizon.IM.Message.Network;

namespace Horizon.Orleans.Interface
{
    #region IM用户Grain

    /// <summary>
    /// IM用户Grain接口 - 每个用户一个实例，管理该用户的私聊、联系人、会话列表
    /// Key: 用户ID (ulong → Guid映射)
    /// </summary>
    public interface IIMUserGrain : IGrainWithGuidKey
    {
        #region 私聊/熟人聊天

        /// <summary>
        /// 发送私聊消息（熟人）
        /// </summary>
        /// <param name="message">私聊消息</param>
        /// <returns>服务器分配的消息ID；空串表示发送失败</returns>
        Task<string> SendPrivateMessageAsync(IMPrivateChatSendMessage message);

        /// <summary>
        /// 接收私聊消息（由发送方的Grain调用，将消息投递到接收方Grain）
        /// </summary>
        /// <param name="notify">服务器生成的通知消息</param>
        /// <returns>是否接收成功</returns>
        Task<bool> ReceivePrivateMessageAsync(IMPrivateChatNotifyMessage notify);

        /// <summary>
        /// 接收群聊消息（由群组Grain调用，将消息推送给群成员的网关订阅）
        /// </summary>
        /// <param name="notify">服务器生成的群聊通知消息</param>
        /// <returns>是否处理成功</returns>
        Task<bool> ReceiveGroupMessageAsync(IMGroupChatNotifyMessage notify);

        /// <summary>
        /// 接收群组系统通知（邀请入群、加群申请审核结果等），直接推送到客户端
        /// </summary>
        Task ReceiveGroupSystemMessageAsync(IMMessageUnion message);

        /// <summary>
        /// 处理消息回执（送达/已读）
        /// </summary>
        Task<bool> ProcessChatAckAsync(IMChatAckMessage ack);

        /// <summary>
        /// 撤回消息
        /// </summary>
        Task<bool> RecallMessageAsync(IMChatRecallMessage recall);

        /// <summary>
        /// 发送已读回执
        /// </summary>
        Task<bool> SendReadReceiptAsync(IMChatReadReceiptMessage receipt);

        #endregion

        #region 陌生人聊天

        /// <summary>
        /// 发起陌生人聊天请求
        /// 校验规则：
        /// 1. 发送方必须已实名认证
        /// 2. 发送方风险等级必须为Normal
        /// </summary>
        /// <param name="request">陌生人聊天请求</param>
        /// <returns>陌生人聊天响应</returns>
        Task<IMStrangerChatResponse> RequestStrangerChatAsync(IMStrangerChatRequest request);

        /// <summary>
        /// 接收陌生人聊天请求（被请求方Grain调用）
        /// </summary>
        Task<bool> ReceiveStrangerChatRequestAsync(IMStrangerChatRequest request);

        /// <summary>
        /// 发送陌生人聊天消息
        /// </summary>
        Task<string> SendStrangerMessageAsync(IMStrangerChatSendMessage message);

        /// <summary>
        /// 接收陌生人聊天消息
        /// </summary>
        Task<bool> ReceiveStrangerMessageAsync(IMStrangerChatNotifyMessage notify);

        /// <summary>
        /// 获取用户实名认证状态
        /// </summary>
        Task<IdentityVerificationStatus> GetVerificationStatusAsync();

        /// <summary>
        /// 设置用户实名认证状态
        /// </summary>
        Task SetVerificationStatusAsync(IdentityVerificationStatus status);

        /// <summary>
        /// 获取用户风险等级
        /// </summary>
        Task<UserRiskLevel> GetRiskLevelAsync();

        /// <summary>
        /// 设置用户风险等级
        /// </summary>
        Task SetRiskLevelAsync(UserRiskLevel level);

        /// <summary>
        /// 获取用户是否允许陌生人消息
        /// </summary>
        Task<bool> GetAllowStrangerMessageAsync();

        /// <summary>
        /// 设置用户是否允许陌生人消息
        /// </summary>
        Task SetAllowStrangerMessageAsync(bool allow);

        #endregion

        #region 联系人管理

        /// <summary>
        /// 添加联系人（发送好友请求）
        /// </summary>
        Task<IMContactAddResponse> AddContactAsync(IMContactAddRequest request);

        /// <summary>
        /// 接收好友请求（被请求方Grain调用）
        /// </summary>
        Task<bool> ReceiveContactRequestAsync(IMContactAddRequest request);

        /// <summary>
        /// 处理好友请求（接受/拒绝）
        /// </summary>
        /// <param name="requesterId">请求者ID</param>
        /// <param name="accept">是否接受</param>
        Task<bool> HandleContactRequestAsync(ulong requesterId, bool accept);

        /// <summary>
        /// 删除联系人
        /// </summary>
        Task<IMContactRemoveResponse> RemoveContactAsync(IMContactRemoveRequest request);

        /// <summary>
        /// 屏蔽联系人
        /// </summary>
        Task<IMContactBlockResponse> BlockContactAsync(IMContactBlockRequest request);

        /// <summary>
        /// 获取联系人列表
        /// </summary>
        Task<IMContactListResponse> GetContactListAsync(IMContactListRequest request);

        /// <summary>
        /// 获取单个联系人信息
        /// </summary>
        Task<IMContactInfo?> GetContactInfoAsync(ulong contactId);

        /// <summary>
        /// 获取待处理的联系人请求列表
        /// </summary>
        Task<IMPendingContactRequestListResponse> GetPendingContactRequestsAsync(IMPendingContactRequestListRequest request);

        /// <summary>
        /// 获取待处理的入群邀请列表
        /// </summary>
        Task<IMGetPendingGroupInvitesResponse> GetPendingGroupInvitesAsync(IMGetPendingGroupInvitesRequest request);

        /// <summary>
        /// 添加待处理入群邀请（由 IMGroupGrain 调用，实现离线投递）
        /// </summary>
        Task AddPendingGroupInviteAsync(ulong groupId, string groupName, ulong inviterId, string inviterName, long timestamp, bool requiresConsent = true);

        /// <summary>
        /// 移除待处理入群邀请（用户同意/拒绝后由 IMGroupGrain 调用）
        /// </summary>
        Task RemovePendingGroupInviteAsync(ulong groupId);

        /// <summary>
        /// 原子性地检查该用户是否已拥有同名群组，若不重复则注册新群名。
        /// 返回 true 表示名称可用且已注册；返回 false 表示已存在同名群组。
        /// </summary>
        Task<bool> CheckAndRegisterGroupNameAsync(string groupName, ulong groupId);

        /// <summary>
        /// 注销已解散或不再属于该用户的群组名称注册（群解散时由 IMGroupGrain 调用）。
        /// </summary>
        Task UnregisterOwnedGroupNameAsync(ulong groupId);

        /// <summary>
        /// 搜索联系人
        /// </summary>
        Task<IMContactSearchResponse> SearchContactAsync(IMContactSearchRequest request);

        /// <summary>
        /// 更新联系人分组（创建/删除/重命名/分配/查询）
        /// </summary>
        Task<IMContactGroupUpdateResponse> UpdateContactGroupAsync(IMContactGroupUpdateRequest request);

        /// <summary>
        /// 联系人被添加回调（双向同步）
        /// </summary>
        Task OnContactAddedAsync(ulong contactId, string contactName, IMOnlineStatus contactOnlineStatus);

        /// <summary>
        /// 好友申请被拒绝回调（通知申请者）
        /// </summary>
        Task OnContactRequestRejectedAsync(ulong rejecterId, string rejecterName);

        /// <summary>
        /// 联系人被删除回调（双向同步）
        /// </summary>
        Task OnContactRemovedAsync(ulong contactId);

        /// <summary>
        /// 联系人在线状态变更回调
        /// </summary>
        Task OnContactOnlineStatusChangedAsync(ulong contactId, IMOnlineStatus onlineStatus);

        /// <summary>
        /// 好友资料（昵称/头像/简介）变更回调，由好友一侧广播调用
        /// </summary>
        Task OnContactProfileUpdatedAsync(ulong contactId, string nickname, string avatar, string bio);

        /// <summary>
        /// 向所有在线好友广播本人资料（昵称/头像/简介）变更
        /// </summary>
        Task BroadcastProfileUpdateAsync(string nickname, string avatar, string bio);

        #endregion

        #region 会话管理

        /// <summary>
        /// 获取会话列表
        /// </summary>
        Task<IMConversationListResponse> GetConversationListAsync(IMConversationListRequest request);

        /// <summary>
        /// 删除会话
        /// </summary>
        Task<bool> DeleteConversationAsync(IMConversationDeleteMessage message);

        /// <summary>
        /// 置顶/取消置顶会话
        /// </summary>
        Task<bool> PinConversationAsync(IMConversationPinMessage message);

        /// <summary>
        /// 设置会话免打扰
        /// </summary>
        Task<bool> MuteConversationAsync(IMConversationMuteMessage message);

        #endregion

        #region 聊天记录

        /// <summary>
        /// 查询聊天记录
        /// </summary>
        Task<IMChatHistoryQueryResponse> QueryChatHistoryAsync(IMChatHistoryQueryRequest request);

        /// <summary>
        /// 清空聊天记录
        /// </summary>
        Task<bool> ClearChatHistoryAsync(IMChatHistoryClearMessage message);

        #endregion

        #region 在线状态

        /// <summary>
        /// 同步网关会话状态
        /// </summary>
        Task SyncSessionAsync(string nickname, string avatar, IMOnlineStatus onlineStatus);

        /// <summary>
        /// 订阅网关服务器
        /// </summary>
        Task SubscribeGatewayAsync(Guid subscriptionId, IIMGatewayObserver observer);

        /// <summary>
        /// 取消订阅网关服务器
        /// </summary>
        Task UnsubscribeGatewayAsync(Guid subscriptionId);

        /// <summary>
        /// 更新在线状态
        /// </summary>
        Task SetOnlineStatusAsync(IMOnlineStatus status);

        /// <summary>
        /// 获取在线状态
        /// </summary>
        Task<IMOnlineStatus> GetOnlineStatusAsync();

        /// <summary>
        /// 获取用户昵称
        /// </summary>
        Task<string> GetNicknameAsync();

        #endregion
    }

    #endregion

    #region IM群组Grain

    /// <summary>
    /// IM群组Grain接口 - 每个群一个实例，管理群元数据、成员、消息
    /// Key: 群组ID (Guid)
    /// </summary>
    public interface IIMGroupGrain : IGrainWithGuidKey
    {
        /// <summary>
        /// 创建群组
        /// </summary>
        Task<IMGroupCreateResponse> CreateGroupAsync(IMGroupCreateRequest request);

        /// <summary>
        /// 加入群组
        /// </summary>
        Task<IMGroupJoinResponse> JoinGroupAsync(IMGroupJoinRequest request);

        /// <summary>
        /// 退出群组
        /// </summary>
        Task<IMGroupLeaveResponse> LeaveGroupAsync(IMGroupLeaveRequest request);

        /// <summary>
        /// 解散群组
        /// </summary>
        Task<IMGroupDisbandResponse> DisbandGroupAsync(IMGroupDisbandRequest request);

        /// <summary>
        /// 发送群聊消息
        /// </summary>
        /// <param name="message">群聊消息</param>
        /// <returns>服务器分配的消息ID；空串表示发送失败</returns>
        Task<string> SendGroupMessageAsync(IMGroupChatSendMessage message);

        /// <summary>
        /// 获取群组信息
        /// </summary>
        Task<IMGroupInfo> GetGroupInfoAsync();

        /// <summary>
        /// 更新群组信息
        /// </summary>
        Task<bool> UpdateGroupInfoAsync(IMGroupInfoUpdateMessage update);

        /// <summary>
        /// 获取群成员列表
        /// </summary>
        Task<IMGroupMemberListResponse> GetMemberListAsync(IMGroupMemberListRequest request);

        /// <summary>
        /// 获取群聊消息历史
        /// </summary>
        /// <param name="count">获取数量</param>
        /// <param name="beforeTimestamp">获取此时间戳之前的消息（0=最新）</param>
        /// <returns>消息列表</returns>
        Task<List<IMGroupChatNotifyMessage>> GetGroupChatHistoryAsync(int count, long beforeTimestamp = 0);

        /// <summary>
        /// 邀请用户入群（直接加入或等待被邀请者同意，取决于群组设置）
        /// </summary>
        Task<IMGroupJoinResponse> InviteUserAsync(IMGroupInviteRequest request);

        /// <summary>
        /// 被邀请者响应入群邀请（接受/拒绝）
        /// </summary>
        Task<IMGroupJoinResponse> RespondToInviteAsync(IMGroupInviteResponse response);

        /// <summary>
        /// 管理员/群主审核加群申请（通过/拒绝）
        /// </summary>
        Task<IMGroupJoinResponse> ReviewJoinApplicationAsync(IMGroupJoinApplyReview review);

        /// <summary>
        /// 群主审核由非群主成员发起的入群邀请（通过/拒绝）。
        /// 通过后邀请消息才会送达被邀请者。
        /// </summary>
        Task<IMGroupJoinResponse> ReviewInviteApprovalAsync(IMGroupInviteApprovalReview review);

        /// <summary>
        /// 获取当前待群主审批的邀请列表（供群主重连后拉取，避免离线期间推送漏接）。
        /// </summary>
        Task<List<IMGroupInviteApprovalNotify>> GetPendingInviteApprovalsAsync();
    }

    #endregion
}
