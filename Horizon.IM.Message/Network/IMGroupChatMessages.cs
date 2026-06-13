using Horizon.IM.Message.Enums;
using MemoryPack;
using Orleans;
using System;
using System.Collections.Generic;

namespace Horizon.IM.Message.Network
{
    #region 群聊消息

    /// <summary>
    /// 群聊消息发送
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class IMGroupChatSendMessage : IMMessageUnion, IIMNetworkMessage
    {
        /// <summary>
        /// 发送者用户ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong SenderId { get; set; }

        /// <summary>
        /// 发送者昵称
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string SenderName { get; set; } = "";

        /// <summary>
        /// 发送者头像URL
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public string SenderAvatar { get; set; } = "";

        /// <summary>
        /// 群组ID
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public ulong GroupId { get; set; }

        /// <summary>
        /// 消息内容
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public string Content { get; set; } = "";

        /// <summary>
        /// 消息内容类型
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public IMContentType ContentType { get; set; } = IMContentType.Text;

        /// <summary>
        /// 客户端消息ID
        /// </summary>
        [MemoryPackOrder(6)]
        [Id(6)]
        public string ClientMessageId { get; set; } = "";

        /// <summary>
        /// 发送时间戳
        /// </summary>
        [MemoryPackOrder(7)]
        [Id(7)]
        public long Timestamp { get; set; }

        /// <summary>
        /// @提及的用户ID列表
        /// </summary>
        [MemoryPackOrder(8)]
        [Id(8)]
        public List<ulong> MentionedUserIds { get; set; } = new();

        /// <summary>
        /// 是否@所有人
        /// </summary>
        [MemoryPackOrder(9)]
        [Id(9)]
        public bool MentionAll { get; set; }

        /// <summary>
        /// 附件URL列表
        /// </summary>
        [MemoryPackOrder(10)]
        [Id(10)]
        public List<string> Attachments { get; set; } = new();

        [MemoryPackOrder(11)]
        [Id(11)]
        public new IMMessageType Type { get; set; } = IMMessageType.GroupChatSend;

        [MemoryPackOrder(12)]
        [Id(12)]
        public new IMServiceType ServiceType { get; set; } = IMServiceType.Group;
    }

    /// <summary>
    /// 群聊消息通知（服务器推送给群成员）
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class IMGroupChatNotifyMessage : IMMessageUnion, IIMNetworkMessage
    {
        /// <summary>
        /// 服务器消息ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public string ServerMessageId { get; set; } = "";

        /// <summary>
        /// 发送者用户ID
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public ulong SenderId { get; set; }

        /// <summary>
        /// 发送者昵称
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public string SenderName { get; set; } = "";

        /// <summary>
        /// 发送者头像URL
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public string SenderAvatar { get; set; } = "";

        /// <summary>
        /// 群组ID
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public ulong GroupId { get; set; }

        /// <summary>
        /// 群组名称
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public string GroupName { get; set; } = "";

        /// <summary>
        /// 消息内容
        /// </summary>
        [MemoryPackOrder(6)]
        [Id(6)]
        public string Content { get; set; } = "";

        /// <summary>
        /// 消息内容类型
        /// </summary>
        [MemoryPackOrder(7)]
        [Id(7)]
        public IMContentType ContentType { get; set; } = IMContentType.Text;

        /// <summary>
        /// 服务器时间戳
        /// </summary>
        [MemoryPackOrder(8)]
        [Id(8)]
        public long Timestamp { get; set; }

        /// <summary>
        /// @提及的用户ID列表
        /// </summary>
        [MemoryPackOrder(9)]
        [Id(9)]
        public List<ulong> MentionedUserIds { get; set; } = new();

        /// <summary>
        /// 是否@所有人
        /// </summary>
        [MemoryPackOrder(10)]
        [Id(10)]
        public bool MentionAll { get; set; }

        /// <summary>
        /// 附件URL列表
        /// </summary>
        [MemoryPackOrder(11)]
        [Id(11)]
        public List<string> Attachments { get; set; } = new();

        [MemoryPackOrder(12)]
        [Id(12)]
        public new IMMessageType Type { get; set; } = IMMessageType.GroupChatNotify;

        [MemoryPackOrder(13)]
        [Id(13)]
        public new IMServiceType ServiceType { get; set; } = IMServiceType.Group;
    }

    /// <summary>
    /// 创建群组请求
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class IMGroupCreateRequest : IMMessageUnion, IIMNetworkMessage
    {
        /// <summary>
        /// 创建者用户ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong CreatorId { get; set; }

        /// <summary>
        /// 群组名称
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string GroupName { get; set; } = "";

        /// <summary>
        /// 群组头像URL
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public string GroupAvatar { get; set; } = "";

        /// <summary>
        /// 群组公告
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public string Announcement { get; set; } = "";

        /// <summary>
        /// 初始成员ID列表
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public List<ulong> InitialMemberIds { get; set; } = new();

        /// <summary>
        /// 群最大人数
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public int MaxMembers { get; set; } = 500;

        [MemoryPackOrder(6)]
        [Id(6)]
        public new IMMessageType Type { get; set; } = IMMessageType.GroupCreateRequest;

        [MemoryPackOrder(7)]
        [Id(7)]
        public new IMServiceType ServiceType { get; set; } = IMServiceType.Group;
    }

    /// <summary>
    /// 创建群组响应
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class IMGroupCreateResponse : IMMessageUnion, IIMNetworkMessage
    {
        /// <summary>
        /// 是否成功
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public bool Success { get; set; }

        /// <summary>
        /// 消息
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string Message { get; set; } = "";

        /// <summary>
        /// 群组ID（创建成功时返回）
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public ulong GroupId { get; set; }

        /// <summary>
        /// 群组信息
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public IMGroupInfo? GroupInfo { get; set; }

        [MemoryPackOrder(4)]
        [Id(4)]
        public new IMMessageType Type { get; set; } = IMMessageType.GroupCreateResponse;

        [MemoryPackOrder(5)]
        [Id(5)]
        public new IMServiceType ServiceType { get; set; } = IMServiceType.Group;
    }

    /// <summary>
    /// 加入群组请求
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class IMGroupJoinRequest : IMMessageUnion, IIMNetworkMessage
    {
        /// <summary>
        /// 用户ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong UserId { get; set; }

        /// <summary>
        /// 群组ID
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public ulong GroupId { get; set; }

        /// <summary>
        /// 申请理由
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public string Reason { get; set; } = "";

        [MemoryPackOrder(3)]
        [Id(3)]
        public new IMMessageType Type { get; set; } = IMMessageType.GroupJoinRequest;

        [MemoryPackOrder(4)]
        [Id(4)]
        public new IMServiceType ServiceType { get; set; } = IMServiceType.Group;
    }

    /// <summary>
    /// 加入群组响应
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class IMGroupJoinResponse : IMMessageUnion, IIMNetworkMessage
    {
        /// <summary>
        /// 是否成功
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public bool Success { get; set; }

        /// <summary>
        /// 消息
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string Message { get; set; } = "";

        /// <summary>
        /// 群组ID
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public ulong GroupId { get; set; }

        /// <summary>
        /// 是否需要被邀请者确认入群
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public bool InviteConsentRequired { get; set; }

        [MemoryPackOrder(3)]
        [Id(3)]
        public new IMMessageType Type { get; set; } = IMMessageType.GroupJoinResponse;

        [MemoryPackOrder(4)]
        [Id(4)]
        public new IMServiceType ServiceType { get; set; } = IMServiceType.Group;
    }

    /// <summary>
    /// 退出群组请求
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class IMGroupLeaveRequest : IMMessageUnion, IIMNetworkMessage
    {
        /// <summary>
        /// 用户ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong UserId { get; set; }

        /// <summary>
        /// 群组ID
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public ulong GroupId { get; set; }

        [MemoryPackOrder(2)]
        [Id(2)]
        public new IMMessageType Type { get; set; } = IMMessageType.GroupLeaveRequest;

        [MemoryPackOrder(3)]
        [Id(3)]
        public new IMServiceType ServiceType { get; set; } = IMServiceType.Group;
    }

    /// <summary>
    /// 退出群组响应
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class IMGroupLeaveResponse : IMMessageUnion, IIMNetworkMessage
    {
        /// <summary>
        /// 是否成功
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public bool Success { get; set; }

        /// <summary>
        /// 消息
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string Message { get; set; } = "";

        /// <summary>
        /// 群组ID
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public ulong GroupId { get; set; }

        [MemoryPackOrder(3)]
        [Id(3)]
        public new IMMessageType Type { get; set; } = IMMessageType.GroupLeaveResponse;

        [MemoryPackOrder(4)]
        [Id(4)]
        public new IMServiceType ServiceType { get; set; } = IMServiceType.Group;
    }

    /// <summary>
    /// 解散群组请求
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class IMGroupDisbandRequest : IMMessageUnion, IIMNetworkMessage
    {
        /// <summary>
        /// 群主用户ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong OwnerId { get; set; }

        /// <summary>
        /// 群组ID
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public ulong GroupId { get; set; }

        [MemoryPackOrder(2)]
        [Id(2)]
        public new IMMessageType Type { get; set; } = IMMessageType.GroupDisbandRequest;

        [MemoryPackOrder(3)]
        [Id(3)]
        public new IMServiceType ServiceType { get; set; } = IMServiceType.Group;
    }

    /// <summary>
    /// 解散群组响应
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class IMGroupDisbandResponse : IMMessageUnion, IIMNetworkMessage
    {
        /// <summary>
        /// 是否成功
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public bool Success { get; set; }

        /// <summary>
        /// 消息
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string Message { get; set; } = "";

        /// <summary>
        /// 群组ID
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public ulong GroupId { get; set; }

        [MemoryPackOrder(3)]
        [Id(3)]
        public new IMMessageType Type { get; set; } = IMMessageType.GroupDisbandResponse;

        [MemoryPackOrder(4)]
        [Id(4)]
        public new IMServiceType ServiceType { get; set; } = IMServiceType.Group;
    }

    /// <summary>
    /// 群组信息更新消息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class IMGroupInfoUpdateMessage : IMMessageUnion, IIMNetworkMessage
    {
        /// <summary>
        /// 群组ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong GroupId { get; set; }

        /// <summary>
        /// 群组信息
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public IMGroupInfo? GroupInfo { get; set; }

        /// <summary>
        /// 操作者用户ID
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public ulong OperatorId { get; set; }

        [MemoryPackOrder(3)]
        [Id(3)]
        public new IMMessageType Type { get; set; } = IMMessageType.GroupInfoUpdate;

        [MemoryPackOrder(4)]
        [Id(4)]
        public new IMServiceType ServiceType { get; set; } = IMServiceType.Group;
    }

    /// <summary>
    /// 群成员列表请求
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class IMGroupMemberListRequest : IMMessageUnion, IIMNetworkMessage
    {
        /// <summary>
        /// 群组ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong GroupId { get; set; }

        /// <summary>
        /// 请求者用户ID
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public ulong UserId { get; set; }

        /// <summary>
        /// 分页偏移量
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public int Offset { get; set; }

        /// <summary>
        /// 每页数量
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public int Limit { get; set; } = 50;

        [MemoryPackOrder(4)]
        [Id(4)]
        public new IMMessageType Type { get; set; } = IMMessageType.GroupMemberListRequest;

        [MemoryPackOrder(5)]
        [Id(5)]
        public new IMServiceType ServiceType { get; set; } = IMServiceType.Group;
    }

    /// <summary>
    /// 群成员列表响应
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class IMGroupMemberListResponse : IMMessageUnion, IIMNetworkMessage
    {
        /// <summary>
        /// 群组ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong GroupId { get; set; }

        /// <summary>
        /// 成员列表
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public List<IMGroupMemberInfo> Members { get; set; } = new();

        /// <summary>
        /// 总成员数
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public int TotalCount { get; set; }

        /// <summary>
        /// 是否还有更多
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public bool HasMore { get; set; }

        [MemoryPackOrder(4)]
        [Id(4)]
        public new IMMessageType Type { get; set; } = IMMessageType.GroupMemberListResponse;

        [MemoryPackOrder(5)]
        [Id(5)]
        public new IMServiceType ServiceType { get; set; } = IMServiceType.Group;
    }

    /// <summary>
    /// 群组信息数据模型
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class IMGroupInfo : IMMessageUnion, IIMNetworkMessage
    {
        /// <summary>
        /// 群组ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong GroupId { get; set; }

        /// <summary>
        /// 群组名称
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string GroupName { get; set; } = "";

        /// <summary>
        /// 群组头像URL
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public string GroupAvatar { get; set; } = "";

        /// <summary>
        /// 群主用户ID
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public ulong OwnerId { get; set; }

        /// <summary>
        /// 群组公告
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public string Announcement { get; set; } = "";

        /// <summary>
        /// 当前成员数
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public int MemberCount { get; set; }

        /// <summary>
        /// 最大成员数
        /// </summary>
        [MemoryPackOrder(6)]
        [Id(6)]
        public int MaxMembers { get; set; }

        /// <summary>
        /// 创建时间戳
        /// </summary>
        [MemoryPackOrder(7)]
        [Id(7)]
        public long CreateTime { get; set; }

        [MemoryPackOrder(8)]
        [Id(8)]
        public new IMMessageType Type { get; set; } = IMMessageType.GroupInfoUpdate;

        [MemoryPackOrder(9)]
        [Id(9)]
        public new IMServiceType ServiceType { get; set; } = IMServiceType.Group;

        /// <summary>
        /// 加群是否需要管理员/群主审核
        /// </summary>
        [MemoryPackOrder(10)]
        [Id(10)]
        public bool JoinApprovalRequired { get; set; }

        /// <summary>
        /// 被邀请入群是否需要被邀请者同意
        /// </summary>
        [MemoryPackOrder(11)]
        [Id(11)]
        public bool InviteConsentRequired { get; set; }

        /// <summary>
        /// 群组是否已解散
        /// </summary>
        [MemoryPackOrder(12)]
        [Id(12)]
        public bool IsDisbanded { get; set; }

        /// <summary>
        /// 非群主成员发起邀请时是否需要群主审批。
        /// 默认为 <c>false</c>：普通成员可直接邀请好友入群，无需群主审批。
        /// </summary>
        [MemoryPackOrder(13)]
        [Id(13)]
        public bool MemberInviteRequiresApproval { get; set; }
    }

    /// <summary>
    /// 群成员信息数据模型
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class IMGroupMemberInfo : IMMessageUnion, IIMNetworkMessage
    {
        /// <summary>
        /// 用户ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong UserId { get; set; }

        /// <summary>
        /// 用户昵称
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string Nickname { get; set; } = "";

        /// <summary>
        /// 用户头像URL
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public string Avatar { get; set; } = "";

        /// <summary>
        /// 群内角色
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public IMGroupMemberRole Role { get; set; }

        /// <summary>
        /// 群内备注名
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public string GroupNickname { get; set; } = "";

        /// <summary>
        /// 加入时间戳
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public long JoinTime { get; set; }

        /// <summary>
        /// 在线状态
        /// </summary>
        [MemoryPackOrder(6)]
        [Id(6)]
        public IMOnlineStatus OnlineStatus { get; set; }

        [MemoryPackOrder(7)]
        [Id(7)]
        public new IMMessageType Type { get; set; } = IMMessageType.GroupMemberListResponse;

        [MemoryPackOrder(8)]
        [Id(8)]
        public new IMServiceType ServiceType { get; set; } = IMServiceType.Group;
    }

    /// <summary>
    /// 邀请用户加入群组请求（成员/管理员发起）
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class IMGroupInviteRequest : IMMessageUnion, IIMNetworkMessage
    {
        /// <summary>
        /// 邀请人ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong InviterId { get; set; }

        /// <summary>
        /// 群组ID
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public ulong GroupId { get; set; }

        /// <summary>
        /// 被邀请的用户ID列表
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public List<ulong> InviteeIds { get; set; } = new();

        [MemoryPackOrder(3)]
        [Id(3)]
        public new IMMessageType Type { get; set; } = IMMessageType.GroupInviteRequest;

        [MemoryPackOrder(4)]
        [Id(4)]
        public new IMServiceType ServiceType { get; set; } = IMServiceType.Group;
    }

    /// <summary>
    /// 被邀请者响应邀请（接受/拒绝）
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class IMGroupInviteResponse : IMMessageUnion, IIMNetworkMessage
    {
        /// <summary>
        /// 响应用户ID（被邀请者）
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong UserId { get; set; }

        /// <summary>
        /// 群组ID
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public ulong GroupId { get; set; }

        /// <summary>
        /// true=接受邀请，false=拒绝邀请
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public bool Accept { get; set; }

        [MemoryPackOrder(3)]
        [Id(3)]
        public new IMMessageType Type { get; set; } = IMMessageType.GroupInviteResponse;

        [MemoryPackOrder(4)]
        [Id(4)]
        public new IMServiceType ServiceType { get; set; } = IMServiceType.Group;
    }

    /// <summary>
    /// 管理员/群主审核加群申请
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class IMGroupJoinApplyReview : IMMessageUnion, IIMNetworkMessage
    {
        /// <summary>
        /// 审核人ID（管理员或群主）
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong ReviewerId { get; set; }

        /// <summary>
        /// 群组ID
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public ulong GroupId { get; set; }

        /// <summary>
        /// 申请人ID
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public ulong ApplicantId { get; set; }

        /// <summary>
        /// true=通过，false=拒绝
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public bool Approve { get; set; }

        [MemoryPackOrder(4)]
        [Id(4)]
        public new IMMessageType Type { get; set; } = IMMessageType.GroupJoinApplyReview;

        [MemoryPackOrder(5)]
        [Id(5)]
        public new IMServiceType ServiceType { get; set; } = IMServiceType.Group;
    }

    /// <summary>
    /// 推送：被邀请者收到入群邀请通知（服务端→客户端）
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class IMGroupInviteNotify : IMMessageUnion, IIMNetworkMessage
    {
        /// <summary>
        /// 群组ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong GroupId { get; set; }

        /// <summary>
        /// 群组名称
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string GroupName { get; set; } = "";

        /// <summary>
        /// 邀请人ID
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public ulong InviterId { get; set; }

        /// <summary>
        /// 邀请人昵称
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public string InviterName { get; set; } = "";

        /// <summary>
        /// 是否需要被邀请者同意（true=需要同意；false=已直接加入）
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public bool RequiresConsent { get; set; }

        [MemoryPackOrder(5)]
        [Id(5)]
        public new IMMessageType Type { get; set; } = IMMessageType.GroupInviteNotify;

        [MemoryPackOrder(6)]
        [Id(6)]
        public new IMServiceType ServiceType { get; set; } = IMServiceType.Group;
    }

    /// <summary>
    /// 推送：管理员/群主收到新的加群申请通知（服务端→客户端）
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class IMGroupJoinApplyNotify : IMMessageUnion, IIMNetworkMessage
    {
        /// <summary>
        /// 群组ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong GroupId { get; set; }

        /// <summary>
        /// 群组名称
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string GroupName { get; set; } = "";

        /// <summary>
        /// 申请人ID
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public ulong ApplicantId { get; set; }

        /// <summary>
        /// 申请人昵称
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public string ApplicantName { get; set; } = "";

        /// <summary>
        /// 申请理由
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public string Reason { get; set; } = "";

        [MemoryPackOrder(5)]
        [Id(5)]
        public new IMMessageType Type { get; set; } = IMMessageType.GroupJoinApplyNotify;

        [MemoryPackOrder(6)]
        [Id(6)]
        public new IMServiceType ServiceType { get; set; } = IMServiceType.Group;
    }

    /// <summary>
    /// 查询当前用户待处理入群邀请列表请求（客户端→服务端）
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class IMGetPendingGroupInvitesRequest : IMMessageUnion, IIMNetworkMessage
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong UserId { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public int Offset { get; set; }

        [MemoryPackOrder(2)]
        [Id(2)]
        public int Limit { get; set; }

        [MemoryPackOrder(3)]
        [Id(3)]
        public new IMMessageType Type { get; set; } = IMMessageType.GroupPendingInviteListRequest;

        [MemoryPackOrder(4)]
        [Id(4)]
        public new IMServiceType ServiceType { get; set; } = IMServiceType.Group;
    }

    /// <summary>
    /// 查询当前用户待处理入群邀请列表响应（服务端→客户端）
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class IMGetPendingGroupInvitesResponse : IMMessageUnion, IIMNetworkMessage
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public List<IMUserPendingGroupInviteEntry> PendingInvites { get; set; } = new();

        [MemoryPackOrder(1)]
        [Id(1)]
        public new IMMessageType Type { get; set; } = IMMessageType.GroupPendingInviteListResponse;

        [MemoryPackOrder(2)]
        [Id(2)]
        public new IMServiceType ServiceType { get; set; } = IMServiceType.Group;
    }

    /// <summary>
    /// 推送：群主收到由非群主成员发起的入群邀请审批请求（服务端→群主）
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class IMGroupInviteApprovalNotify : IMMessageUnion, IIMNetworkMessage
    {
        /// <summary>
        /// 群组ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong GroupId { get; set; }

        /// <summary>
        /// 群组名称
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string GroupName { get; set; } = "";

        /// <summary>
        /// 发起邀请的成员ID（非群主）
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public ulong InviterId { get; set; }

        /// <summary>
        /// 发起邀请的成员昵称
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public string InviterName { get; set; } = "";

        /// <summary>
        /// 被邀请用户ID
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public ulong InviteeId { get; set; }

        /// <summary>
        /// 发起时间戳（毫秒）
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public long Timestamp { get; set; }

        [MemoryPackOrder(6)]
        [Id(6)]
        public new IMMessageType Type { get; set; } = IMMessageType.GroupInviteApprovalNotify;

        [MemoryPackOrder(7)]
        [Id(7)]
        public new IMServiceType ServiceType { get; set; } = IMServiceType.Group;
    }

    /// <summary>
    /// 群主审核由非群主成员发起的入群邀请（客户端→服务端）
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class IMGroupInviteApprovalReview : IMMessageUnion, IIMNetworkMessage
    {
        /// <summary>
        /// 审核人ID（群主）
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong ReviewerId { get; set; }

        /// <summary>
        /// 群组ID
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public ulong GroupId { get; set; }

        /// <summary>
        /// 被邀请用户ID（审核主键）
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public ulong InviteeId { get; set; }

        /// <summary>
        /// true=同意，false=拒绝
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public bool Approve { get; set; }

        [MemoryPackOrder(4)]
        [Id(4)]
        public new IMMessageType Type { get; set; } = IMMessageType.GroupInviteApprovalReview;

        [MemoryPackOrder(5)]
        [Id(5)]
        public new IMServiceType ServiceType { get; set; } = IMServiceType.Group;
    }

    /// <summary>
    /// 推送：群组被解散通知（服务端→成员）
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class IMGroupDisbandNotify : IMMessageUnion, IIMNetworkMessage
    {
        /// <summary>
        /// 群组ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong GroupId { get; set; }

        /// <summary>
        /// 群组名称
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string GroupName { get; set; } = "";

        /// <summary>
        /// 解散时间戳（毫秒）
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public long Timestamp { get; set; }

        [MemoryPackOrder(3)]
        [Id(3)]
        public new IMMessageType Type { get; set; } = IMMessageType.GroupDisbandNotify;

        [MemoryPackOrder(4)]
        [Id(4)]
        public new IMServiceType ServiceType { get; set; } = IMServiceType.Group;
    }

    /// <summary>
    /// 群主查询待审批邀请列表请求（客户端→服务端）
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class IMGetPendingApprovalListRequest : IMMessageUnion, IIMNetworkMessage
    {
        /// <summary>
        /// 发起请求的群主用户ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong OwnerId { get; set; }

        /// <summary>
        /// 群组ID
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public ulong GroupId { get; set; }

        [MemoryPackOrder(2)]
        [Id(2)]
        public new IMMessageType Type { get; set; } = IMMessageType.GroupPendingApprovalListRequest;

        [MemoryPackOrder(3)]
        [Id(3)]
        public new IMServiceType ServiceType { get; set; } = IMServiceType.Group;
    }

    /// <summary>
    /// 群主查询待审批邀请列表响应（服务端→客户端）
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class IMGetPendingApprovalListResponse : IMMessageUnion, IIMNetworkMessage
    {
        /// <summary>
        /// 待审批邀请列表
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public List<IMGroupInviteApprovalNotify> PendingApprovals { get; set; } = new();

        [MemoryPackOrder(1)]
        [Id(1)]
        public new IMMessageType Type { get; set; } = IMMessageType.GroupPendingApprovalListResponse;

        [MemoryPackOrder(2)]
        [Id(2)]
        public new IMServiceType ServiceType { get; set; } = IMServiceType.Group;
    }

    /// <summary>
    /// 推送：原邀请人收到群主对其邀请的审批结果通知（服务端→客户端）。
    /// 当 <c>MemberInviteRequiresApproval=true</c> 时，群主审批后通过此消息通知原邀请人。
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class IMGroupInviteResultNotify : IMMessageUnion, IIMNetworkMessage
    {
        /// <summary>
        /// 群组ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong GroupId { get; set; }

        /// <summary>
        /// 群组名称
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string GroupName { get; set; } = "";

        /// <summary>
        /// 被邀请的用户ID
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public ulong InviteeId { get; set; }

        /// <summary>
        /// true=群主已批准，false=群主已拒绝
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public bool Approved { get; set; }

        /// <summary>
        /// 审批时间戳（毫秒）
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public long Timestamp { get; set; }

        [MemoryPackOrder(5)]
        [Id(5)]
        public new IMMessageType Type { get; set; } = IMMessageType.GroupInviteResultNotify;

        [MemoryPackOrder(6)]
        [Id(6)]
        public new IMServiceType ServiceType { get; set; } = IMServiceType.Group;
    }

    #endregion
}
