using Horizon.IM.Message.Enums;
using MemoryPack;
using Orleans;
using System;
using System.Collections.Generic;

namespace Horizon.IM.Message.Network
{
    #region 联系人管理消息

    /// <summary>
    /// 添加联系人请求
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class IMContactAddRequest : IMMessageUnion, IIMNetworkMessage
    {
        /// <summary>
        /// 请求者用户ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong UserId { get; set; }

        /// <summary>
        /// 目标用户ID
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public ulong TargetUserId { get; set; }

        /// <summary>
        /// 验证消息
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public string VerifyMessage { get; set; } = "";

        /// <summary>
        /// 备注名
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public string RemarkName { get; set; } = "";

        /// <summary>
        /// 请求来源（搜索、群组、附近等）
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public string Source { get; set; } = "";

        /// <summary>
        /// 请求者昵称
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public string RequesterName { get; set; } = "";

        /// <summary>
        /// 请求者头像
        /// </summary>
        [MemoryPackOrder(6)]
        [Id(6)]
        public string RequesterAvatar { get; set; } = "";

        [MemoryPackOrder(7)]
        [Id(7)]
        public new IMMessageType Type { get; set; } = IMMessageType.ContactAddRequest;

        [MemoryPackOrder(8)]
        [Id(8)]
        public new IMServiceType ServiceType { get; set; } = IMServiceType.Contact;
    }

    /// <summary>
    /// 添加联系人响应
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class IMContactAddResponse : IMMessageUnion, IIMNetworkMessage
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
        /// 请求者用户ID
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public ulong UserId { get; set; }

        /// <summary>
        /// 目标用户ID
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public ulong TargetUserId { get; set; }

        /// <summary>
        /// 联系人关系状态
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public IMContactRelation Relation { get; set; }

        [MemoryPackOrder(5)]
        [Id(5)]
        public new IMMessageType Type { get; set; } = IMMessageType.ContactAddResponse;

        [MemoryPackOrder(6)]
        [Id(6)]
        public new IMServiceType ServiceType { get; set; } = IMServiceType.Contact;
    }

    /// <summary>
    /// 处理好友申请请求
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class IMContactRequestHandleRequest : IMMessageUnion, IIMNetworkMessage
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong UserId { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public ulong RequesterId { get; set; }

        [MemoryPackOrder(2)]
        [Id(2)]
        public bool Accept { get; set; }

        [MemoryPackOrder(3)]
        [Id(3)]
        public new IMMessageType Type { get; set; } = IMMessageType.ContactRequestHandleRequest;

        [MemoryPackOrder(4)]
        [Id(4)]
        public new IMServiceType ServiceType { get; set; } = IMServiceType.Contact;
    }

    /// <summary>
    /// 处理好友申请响应
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class IMContactRequestHandleResponse : IMMessageUnion, IIMNetworkMessage
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public bool Success { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public string Message { get; set; } = "";

        [MemoryPackOrder(2)]
        [Id(2)]
        public ulong RequesterId { get; set; }

        [MemoryPackOrder(3)]
        [Id(3)]
        public bool Accepted { get; set; }

        [MemoryPackOrder(6)]
        [Id(6)]
        public IMContactInfo? NewContact { get; set; }

        [MemoryPackOrder(4)]
        [Id(4)]
        public new IMMessageType Type { get; set; } = IMMessageType.ContactRequestHandleResponse;

        [MemoryPackOrder(5)]
        [Id(5)]
        public new IMServiceType ServiceType { get; set; } = IMServiceType.Contact;
    }

    /// <summary>
    /// 待处理好友申请列表请求
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class IMPendingContactRequestListRequest : IMMessageUnion, IIMNetworkMessage
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
        public new IMMessageType Type { get; set; } = IMMessageType.ContactPendingListRequest;

        [MemoryPackOrder(4)]
        [Id(4)]
        public new IMServiceType ServiceType { get; set; } = IMServiceType.Contact;
    }

    /// <summary>
    /// 待处理好友申请列表响应
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class IMPendingContactRequestListResponse : IMMessageUnion, IIMNetworkMessage
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public List<IMPendingContactRequest> PendingRequests { get; set; } = new();

        [MemoryPackOrder(1)]
        [Id(1)]
        public new IMMessageType Type { get; set; } = IMMessageType.ContactPendingListResponse;

        [MemoryPackOrder(2)]
        [Id(2)]
        public new IMServiceType ServiceType { get; set; } = IMServiceType.Contact;
    }

    /// <summary>
    /// 删除联系人请求
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class IMContactRemoveRequest : IMMessageUnion, IIMNetworkMessage
    {
        /// <summary>
        /// 用户ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong UserId { get; set; }

        /// <summary>
        /// 目标联系人用户ID
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public ulong TargetUserId { get; set; }

        [MemoryPackOrder(2)]
        [Id(2)]
        public new IMMessageType Type { get; set; } = IMMessageType.ContactRemoveRequest;

        [MemoryPackOrder(3)]
        [Id(3)]
        public new IMServiceType ServiceType { get; set; } = IMServiceType.Contact;
    }

    /// <summary>
    /// 删除联系人响应
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class IMContactRemoveResponse : IMMessageUnion, IIMNetworkMessage
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
        /// 被删除的联系人用户ID
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public ulong TargetUserId { get; set; }

        [MemoryPackOrder(3)]
        [Id(3)]
        public new IMMessageType Type { get; set; } = IMMessageType.ContactRemoveResponse;

        [MemoryPackOrder(4)]
        [Id(4)]
        public new IMServiceType ServiceType { get; set; } = IMServiceType.Contact;
    }

    /// <summary>
    /// 屏蔽联系人请求
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class IMContactBlockRequest : IMMessageUnion, IIMNetworkMessage
    {
        /// <summary>
        /// 用户ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong UserId { get; set; }

        /// <summary>
        /// 被屏蔽的用户ID
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public ulong TargetUserId { get; set; }

        /// <summary>
        /// 是否屏蔽（true=屏蔽，false=取消屏蔽）
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public bool IsBlock { get; set; }

        [MemoryPackOrder(3)]
        [Id(3)]
        public new IMMessageType Type { get; set; } = IMMessageType.ContactBlockRequest;

        [MemoryPackOrder(4)]
        [Id(4)]
        public new IMServiceType ServiceType { get; set; } = IMServiceType.Contact;
    }

    /// <summary>
    /// 屏蔽联系人响应
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class IMContactBlockResponse : IMMessageUnion, IIMNetworkMessage
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
        /// 目标用户ID
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public ulong TargetUserId { get; set; }

        /// <summary>
        /// 当前关系状态
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public IMContactRelation Relation { get; set; }

        [MemoryPackOrder(4)]
        [Id(4)]
        public new IMMessageType Type { get; set; } = IMMessageType.ContactBlockResponse;

        [MemoryPackOrder(5)]
        [Id(5)]
        public new IMServiceType ServiceType { get; set; } = IMServiceType.Contact;
    }

    /// <summary>
    /// 联系人列表请求
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class IMContactListRequest : IMMessageUnion, IIMNetworkMessage
    {
        /// <summary>
        /// 用户ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong UserId { get; set; }

        /// <summary>
        /// 分页偏移量
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public int Offset { get; set; }

        /// <summary>
        /// 每页数量
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public int Limit { get; set; } = 50;

        /// <summary>
        /// 是否只获取在线好友
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public bool OnlineOnly { get; set; }

        [MemoryPackOrder(4)]
        [Id(4)]
        public new IMMessageType Type { get; set; } = IMMessageType.ContactListRequest;

        [MemoryPackOrder(5)]
        [Id(5)]
        public new IMServiceType ServiceType { get; set; } = IMServiceType.Contact;
    }

    /// <summary>
    /// 联系人列表响应
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class IMContactListResponse : IMMessageUnion, IIMNetworkMessage
    {
        /// <summary>
        /// 联系人列表
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public List<IMContactInfo> Contacts { get; set; } = new();

        /// <summary>
        /// 总数
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public int TotalCount { get; set; }

        /// <summary>
        /// 是否还有更多
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public bool HasMore { get; set; }

        [MemoryPackOrder(3)]
        [Id(3)]
        public new IMMessageType Type { get; set; } = IMMessageType.ContactListResponse;

        [MemoryPackOrder(4)]
        [Id(4)]
        public new IMServiceType ServiceType { get; set; } = IMServiceType.Contact;
    }

    /// <summary>
    /// 联系人搜索请求
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class IMContactSearchRequest : IMMessageUnion, IIMNetworkMessage
    {
        /// <summary>
        /// 搜索关键词（昵称、ID等）
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public string Keyword { get; set; } = "";

        /// <summary>
        /// 搜索者用户ID
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
        public int Limit { get; set; } = 20;

        [MemoryPackOrder(4)]
        [Id(4)]
        public new IMMessageType Type { get; set; } = IMMessageType.ContactSearchRequest;

        [MemoryPackOrder(5)]
        [Id(5)]
        public new IMServiceType ServiceType { get; set; } = IMServiceType.Contact;
    }

    /// <summary>
    /// 联系人搜索响应
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class IMContactSearchResponse : IMMessageUnion, IIMNetworkMessage
    {
        /// <summary>
        /// 搜索结果
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public List<IMContactInfo> Results { get; set; } = new();

        /// <summary>
        /// 是否还有更多
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public bool HasMore { get; set; }

        [MemoryPackOrder(2)]
        [Id(2)]
        public new IMMessageType Type { get; set; } = IMMessageType.ContactSearchResponse;

        [MemoryPackOrder(3)]
        [Id(3)]
        public new IMServiceType ServiceType { get; set; } = IMServiceType.Contact;
    }

    /// <summary>
    /// 联系人在线状态变更通知
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class IMContactOnlineStatusMessage : IMMessageUnion, IIMNetworkMessage
    {
        /// <summary>
        /// 联系人用户ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong UserId { get; set; }

        /// <summary>
        /// 在线状态
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public IMOnlineStatus OnlineStatus { get; set; }

        /// <summary>
        /// 状态变更时间戳
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public long Timestamp { get; set; }

        [MemoryPackOrder(3)]
        [Id(3)]
        public new IMMessageType Type { get; set; } = IMMessageType.ContactOnlineStatus;

        [MemoryPackOrder(4)]
        [Id(4)]
        public new IMServiceType ServiceType { get; set; } = IMServiceType.Contact;
    }

    /// <summary>
    /// 联系人信息数据模型
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class IMContactInfo : IMMessageUnion, IIMNetworkMessage
    {
        /// <summary>
        /// 用户ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong UserId { get; set; }

        /// <summary>
        /// 昵称
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string Nickname { get; set; } = "";

        /// <summary>
        /// 头像URL
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public string Avatar { get; set; } = "";

        /// <summary>
        /// 备注名
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public string RemarkName { get; set; } = "";

        /// <summary>
        /// 关系状态
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public IMContactRelation Relation { get; set; }

        /// <summary>
        /// 在线状态
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public IMOnlineStatus OnlineStatus { get; set; }

        /// <summary>
        /// 个性签名
        /// </summary>
        [MemoryPackOrder(6)]
        [Id(6)]
        public string Signature { get; set; } = "";

        /// <summary>
        /// 实名认证状态
        /// </summary>
        [MemoryPackOrder(7)]
        [Id(7)]
        public IdentityVerificationStatus VerificationStatus { get; set; }

        /// <summary>
        /// 所属分组名称（空字符串表示默认分组）
        /// </summary>
        [MemoryPackOrder(10)]
        [Id(10)]
        public string GroupName { get; set; } = "";

        [MemoryPackOrder(8)]
        [Id(8)]
        public new IMMessageType Type { get; set; } = IMMessageType.ContactListResponse;

        [MemoryPackOrder(9)]
        [Id(9)]
        public new IMServiceType ServiceType { get; set; } = IMServiceType.Contact;
    }

    /// <summary>
    /// 好友资料变更推送消息（服务器 → 客户端）
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class IMContactProfileUpdateMessage : IMMessageUnion, IIMNetworkMessage
    {
        /// <summary>
        /// 发生资料变更的用户ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong UserId { get; set; }

        /// <summary>
        /// 最新昵称
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string Nickname { get; set; } = "";

        /// <summary>
        /// 最新头像地址
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public string Avatar { get; set; } = "";

        /// <summary>
        /// 最新个人简介
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public string Bio { get; set; } = "";

        /// <summary>
        /// 变更时间戳
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public long Timestamp { get; set; }

        [MemoryPackOrder(5)]
        [Id(5)]
        public new IMMessageType Type { get; set; } = IMMessageType.ContactProfileUpdate;

        [MemoryPackOrder(6)]
        [Id(6)]
        public new IMServiceType ServiceType { get; set; } = IMServiceType.Contact;
    }

    /// <summary>
    /// 广播个人资料变更请求（客户端 → 网关）
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class IMContactProfileBroadcastRequest : IMMessageUnion, IIMNetworkMessage
    {
        /// <summary>
        /// 发起广播的用户ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong UserId { get; set; }

        /// <summary>
        /// 最新昵称
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string Nickname { get; set; } = "";

        /// <summary>
        /// 最新头像地址
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public string Avatar { get; set; } = "";

        /// <summary>
        /// 最新个人简介
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public string Bio { get; set; } = "";

        [MemoryPackOrder(4)]
        [Id(4)]
        public new IMMessageType Type { get; set; } = IMMessageType.ContactProfileBroadcastRequest;

        [MemoryPackOrder(5)]
        [Id(5)]
        public new IMServiceType ServiceType { get; set; } = IMServiceType.Contact;
    }

    /// <summary>
    /// 联系人分组更新请求（客户端 → 服务端）
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class IMContactGroupUpdateRequest : IMMessageUnion, IIMNetworkMessage
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong UserId { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public string Action { get; set; } = "";

        [MemoryPackOrder(2)]
        [Id(2)]
        public string GroupName { get; set; } = "";

        [MemoryPackOrder(3)]
        [Id(3)]
        public string NewGroupName { get; set; } = "";

        [MemoryPackOrder(4)]
        [Id(4)]
        public List<ulong> ContactUserIds { get; set; } = new();

        [MemoryPackOrder(5)]
        [Id(5)]
        public new IMMessageType Type { get; set; } = IMMessageType.ContactGroupUpdateRequest;

        [MemoryPackOrder(6)]
        [Id(6)]
        public new IMServiceType ServiceType { get; set; } = IMServiceType.Contact;
    }

    /// <summary>
    /// 联系人分组更新响应（服务端 → 客户端）
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class IMContactGroupUpdateResponse : IMMessageUnion, IIMNetworkMessage
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public bool Success { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public string Message { get; set; } = "";

        [MemoryPackOrder(2)]
        [Id(2)]
        public Dictionary<string, int> ContactGroups { get; set; } = new();

        [MemoryPackOrder(3)]
        [Id(3)]
        public new IMMessageType Type { get; set; } = IMMessageType.ContactGroupUpdateResponse;

        [MemoryPackOrder(4)]
        [Id(4)]
        public new IMServiceType ServiceType { get; set; } = IMServiceType.Contact;
    }

    #endregion
}
