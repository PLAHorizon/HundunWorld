using Horizon.IM.Message.Enums;
using Horizon.IM.Message.Network;
using MemoryPack;
using Orleans;
using System;
using System.Collections.Generic;

namespace Horizon.IM.Message
{
    #region IM用户Grain状态

    /// <summary>
    /// IM用户Grain持久化状态
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class IMUserState
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
        /// 实名认证状态
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public IdentityVerificationStatus VerificationStatus { get; set; } = IdentityVerificationStatus.Unverified;

        /// <summary>
        /// 用户风险等级
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public UserRiskLevel RiskLevel { get; set; } = UserRiskLevel.Normal;

        /// <summary>
        /// 是否允许陌生人消息
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public bool AllowStrangerMessage { get; set; } = true;

        /// <summary>
        /// 在线状态
        /// </summary>
        [MemoryPackOrder(6)]
        [Id(6)]
        public IMOnlineStatus OnlineStatus { get; set; } = IMOnlineStatus.Offline;

        /// <summary>
        /// 联系人列表（联系人UserID → 联系人信息）
        /// </summary>
        [MemoryPackOrder(7)]
        [Id(7)]
        public Dictionary<ulong, IMContactEntry> Contacts { get; set; } = new();

        /// <summary>
        /// 待处理的好友请求列表（请求者UserID → 请求信息）
        /// </summary>
        [MemoryPackOrder(8)]
        [Id(8)]
        public Dictionary<ulong, IMPendingContactRequest> PendingContactRequests { get; set; } = new();

        /// <summary>
        /// 屏蔽列表
        /// </summary>
        [MemoryPackOrder(9)]
        [Id(9)]
        public HashSet<ulong> BlockedUsers { get; set; } = new();

        /// <summary>
        /// 会话列表（会话ID → 会话信息）
        /// </summary>
        [MemoryPackOrder(10)]
        [Id(10)]
        public Dictionary<string, IMConversationEntry> Conversations { get; set; } = new();

        /// <summary>
        /// 私聊消息历史（对话方UserID → 消息列表）
        /// </summary>
        [MemoryPackOrder(11)]
        [Id(11)]
        public Dictionary<ulong, List<IMChatRecord>> PrivateChatHistory { get; set; } = new();

        /// <summary>
        /// 陌生人聊天消息历史（对话方UserID → 消息列表）
        /// </summary>
        [MemoryPackOrder(12)]
        [Id(12)]
        public Dictionary<ulong, List<IMChatRecord>> StrangerChatHistory { get; set; } = new();

        /// <summary>
        /// 待处理的陌生人聊天请求列表（请求者UserID → 请求信息）
        /// </summary>
        [MemoryPackOrder(13)]
        [Id(13)]
        public Dictionary<ulong, IMStrangerChatRequestEntry> PendingStrangerRequests { get; set; } = new();

        /// <summary>
        /// 最大联系人数量
        /// </summary>
        [MemoryPackOrder(14)]
        [Id(14)]
        public int MaxContacts { get; set; } = 500;

        /// <summary>
        /// 每个对话最大缓存消息数量
        /// </summary>
        [MemoryPackOrder(15)]
        [Id(15)]
        public int MaxChatHistoryPerConversation { get; set; } = 200;

        /// <summary>
        /// 最大会话数量
        /// </summary>
        [MemoryPackOrder(16)]
        [Id(16)]
        public int MaxConversations { get; set; } = 500;

        /// <summary>
        /// 待处理的入群邀请列表（群组ID → 邀请信息）
        /// </summary>
        [MemoryPackOrder(17)]
        [Id(17)]
        public Dictionary<ulong, IMUserPendingGroupInviteEntry> PendingGroupInvites { get; set; } = new();

        /// <summary>
        /// 该用户作为群主创建的群组名称集合（群名称 → 群组ID）。
        /// 用于服务端防重名校验，确保同一用户不能创建同名群组。
        /// </summary>
        [MemoryPackOrder(18)]
        [Id(18)]
        public Dictionary<string, ulong> OwnedGroupNames { get; set; } = new();

        /// <summary>
        /// 联系人分组定义（分组名称 → 排序序号）。
        /// 序号越小越靠前，默认分组 "" 不在此字典中。
        /// </summary>
        [MemoryPackOrder(19)]
        [Id(19)]
        public Dictionary<string, int> ContactGroups { get; set; } = new();
    }

    /// <summary>
    /// 联系人条目
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class IMContactEntry
    {
        /// <summary>
        /// 联系人用户ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong UserId { get; set; }

        /// <summary>
        /// 联系人昵称
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string Nickname { get; set; } = "";

        /// <summary>
        /// 联系人头像URL
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public string Avatar { get; set; } = "";

        /// <summary>
        /// 备注名
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public string Remark { get; set; } = "";

        /// <summary>
        /// 关系状态
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public IMContactRelation Relation { get; set; } = IMContactRelation.Friend;

        /// <summary>
        /// 联系人在线状态
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public IMOnlineStatus OnlineStatus { get; set; } = IMOnlineStatus.Offline;

        /// <summary>
        /// 添加时间
        /// </summary>
        [MemoryPackOrder(6)]
        [Id(6)]
        public long AddTime { get; set; }

        /// <summary>
        /// 个人简介
        /// </summary>
        [MemoryPackOrder(7)]
        [Id(7)]
        public string Bio { get; set; } = "";

        /// <summary>
        /// 所属分组名称（空字符串表示默认分组/未分组）
        /// </summary>
        [MemoryPackOrder(8)]
        [Id(8)]
        public string GroupName { get; set; } = "";

        static partial void StaticConstructor()
        {
            MemoryPackFormatterProvider.Register(new CompatibleFormatter());
        }

        private sealed class CompatibleFormatter : MemoryPackFormatter<IMContactEntry>
        {
            public override void Serialize<TBufferWriter>(ref MemoryPackWriter<TBufferWriter> writer, scoped ref IMContactEntry? value)
            {
                if (value == null)
                {
                    writer.WriteNullObjectHeader();
                    return;
                }

                writer.WriteObjectHeader(9);
                writer.WriteUnmanaged(value.UserId);
                writer.WriteString(value.Nickname);
                writer.WriteString(value.Avatar);
                writer.WriteString(value.Remark);
                writer.WriteUnmanaged(value.Relation);
                writer.WriteUnmanaged(value.OnlineStatus, value.AddTime);
                writer.WriteString(value.Bio);
                writer.WriteString(value.GroupName);
            }

            public override void Deserialize(ref MemoryPackReader reader, scoped ref IMContactEntry? value)
            {
                if (!reader.TryReadObjectHeader(out var count))
                {
                    value = null;
                    return;
                }

                if (count > 9)
                {
                    MemoryPackSerializationException.ThrowInvalidPropertyCount(9, count);
                }

                value ??= new IMContactEntry();
                value.UserId = default;
                value.Nickname = string.Empty;
                value.Avatar = string.Empty;
                value.Remark = string.Empty;
                value.Relation = IMContactRelation.Friend;
                value.OnlineStatus = IMOnlineStatus.Offline;
                value.AddTime = default;
                value.Bio = string.Empty;
                value.GroupName = string.Empty;

                if (count >= 1)
                {
                    reader.ReadUnmanaged(out ulong userId);
                    value.UserId = userId;
                }

                if (count >= 2)
                {
                    value.Nickname = reader.ReadString() ?? string.Empty;
                }

                if (count >= 3)
                {
                    value.Avatar = reader.ReadString() ?? string.Empty;
                }

                if (count >= 4)
                {
                    value.Remark = reader.ReadString() ?? string.Empty;
                }

                if (count >= 5)
                {
                    reader.ReadUnmanaged(out IMContactRelation relation);
                    value.Relation = relation;
                }

                if (count == 6)
                {
                    reader.ReadUnmanaged(out long addTime);
                    value.AddTime = addTime;
                    return;
                }

                if (count >= 7)
                {
                    reader.ReadUnmanaged(out IMOnlineStatus onlineStatus, out long addTime);
                    value.OnlineStatus = onlineStatus;
                    value.AddTime = addTime;
                }

                if (count >= 8)
                {
                    value.Bio = reader.ReadString() ?? string.Empty;
                }

                if (count >= 9)
                {
                    value.GroupName = reader.ReadString() ?? string.Empty;
                }
            }
        }
    }

    /// <summary>
    /// 待处理的好友请求
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class IMPendingContactRequest
    {
        /// <summary>
        /// 请求者用户ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong RequesterId { get; set; }

        /// <summary>
        /// 请求者昵称
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string RequesterName { get; set; } = "";

        /// <summary>
        /// 验证消息
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public string Message { get; set; } = "";

        /// <summary>
        /// 请求时间
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public long Timestamp { get; set; }
    }

    /// <summary>
    /// 用户侧待处理的入群邀请条目（存储在 IMUserState 中，用于离线投递恢复）
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class IMUserPendingGroupInviteEntry
    {
        /// <summary>群组ID</summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong GroupId { get; set; }

        /// <summary>群组名称</summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string GroupName { get; set; } = "";

        /// <summary>邀请人ID</summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public ulong InviterId { get; set; }

        /// <summary>邀请人昵称</summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public string InviterName { get; set; } = "";

        /// <summary>邀请时间戳（Unix 毫秒）</summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public long Timestamp { get; set; }

        /// <summary>
        /// 是否需要被邀请者确认（true=需要同意；false=已被直接加入群组）。
        /// 默认 true，以便与旧数据兼容。
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public bool RequiresConsent { get; set; } = true;
    }

    /// <summary>
    /// 陌生人聊天请求条目
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class IMStrangerChatRequestEntry
    {
        /// <summary>
        /// 请求者用户ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong RequesterId { get; set; }

        /// <summary>
        /// 请求者昵称
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string RequesterName { get; set; } = "";

        /// <summary>
        /// 打招呼消息
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public string GreetingMessage { get; set; } = "";

        /// <summary>
        /// 请求时间
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public long Timestamp { get; set; }

        /// <summary>
        /// 是否已被接受
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public bool Accepted { get; set; }
    }

    /// <summary>
    /// 聊天记录条目
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class IMChatRecord
    {
        /// <summary>
        /// 服务器消息ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public string ServerMessageId { get; set; } = "";

        /// <summary>
        /// 客户端消息ID
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string ClientMessageId { get; set; } = "";

        /// <summary>
        /// 发送者ID
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public ulong SenderId { get; set; }

        /// <summary>
        /// 发送者昵称
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public string SenderName { get; set; } = "";

        /// <summary>
        /// 接收者ID
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public ulong ReceiverId { get; set; }

        /// <summary>
        /// 消息内容
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public string Content { get; set; } = "";

        /// <summary>
        /// 内容类型
        /// </summary>
        [MemoryPackOrder(6)]
        [Id(6)]
        public IMContentType ContentType { get; set; }

        /// <summary>
        /// 服务器时间戳
        /// </summary>
        [MemoryPackOrder(7)]
        [Id(7)]
        public long Timestamp { get; set; }

        /// <summary>
        /// 消息状态
        /// </summary>
        [MemoryPackOrder(8)]
        [Id(8)]
        public IMMessageStatus Status { get; set; } = IMMessageStatus.Sent;
    }

    /// <summary>
    /// 会话条目
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class IMConversationEntry
    {
        /// <summary>
        /// 会话ID（私聊用 "p_{userId}"，群聊用 "g_{groupId}"，陌生人用 "s_{userId}"）
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public string ConversationId { get; set; } = "";

        /// <summary>
        /// 聊天关系类型
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public IMChatRelationType ChatType { get; set; }

        /// <summary>
        /// 对方用户ID（私聊/陌生人）或群组ID对应的ulong表示
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public ulong TargetId { get; set; }

        /// <summary>
        /// 对方昵称或群组名称
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public string TargetName { get; set; } = "";

        /// <summary>
        /// 对方头像或群组头像
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public string TargetAvatar { get; set; } = "";

        /// <summary>
        /// 最后一条消息内容摘要
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public string LastMessage { get; set; } = "";

        /// <summary>
        /// 最后一条消息时间戳
        /// </summary>
        [MemoryPackOrder(6)]
        [Id(6)]
        public long LastMessageTime { get; set; }

        /// <summary>
        /// 未读消息数
        /// </summary>
        [MemoryPackOrder(7)]
        [Id(7)]
        public int UnreadCount { get; set; }

        /// <summary>
        /// 是否置顶
        /// </summary>
        [MemoryPackOrder(8)]
        [Id(8)]
        public bool IsPinned { get; set; }

        /// <summary>
        /// 是否免打扰
        /// </summary>
        [MemoryPackOrder(9)]
        [Id(9)]
        public bool IsMuted { get; set; }
    }

    #endregion

    #region IM群组Grain状态

    /// <summary>
    /// IM群组Grain持久化状态
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class IMGroupState
    {
        /// <summary>
        /// 群组ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public Guid GroupId { get; set; }

        /// <summary>
        /// 群组名称
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string GroupName { get; set; } = "";

        /// <summary>
        /// 群主用户ID
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public ulong OwnerId { get; set; }

        /// <summary>
        /// 群组头像URL
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public string Avatar { get; set; } = "";

        /// <summary>
        /// 群公告
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public string Announcement { get; set; } = "";

        /// <summary>
        /// 最大成员数
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public int MaxMembers { get; set; } = 200;

        /// <summary>
        /// 创建时间
        /// </summary>
        [MemoryPackOrder(6)]
        [Id(6)]
        public long CreateTime { get; set; }

        /// <summary>
        /// 群组是否已解散
        /// </summary>
        [MemoryPackOrder(7)]
        [Id(7)]
        public bool IsDisbanded { get; set; }

        /// <summary>
        /// 群成员列表（用户ID → 成员信息）
        /// </summary>
        [MemoryPackOrder(8)]
        [Id(8)]
        public Dictionary<ulong, IMGroupMemberEntry> Members { get; set; } = new();

        /// <summary>
        /// 群聊消息历史
        /// </summary>
        [MemoryPackOrder(9)]
        [Id(9)]
        public List<IMGroupChatRecord> ChatHistory { get; set; } = new();

        /// <summary>
        /// 最大缓存消息数量
        /// </summary>
        [MemoryPackOrder(10)]
        [Id(10)]
        public int MaxChatHistory { get; set; } = 500;

        /// <summary>
        /// 加群是否需要管理员/群主审核
        /// </summary>
        [MemoryPackOrder(11)]
        [Id(11)]
        public bool JoinApprovalRequired { get; set; }

        /// <summary>
        /// 被邀请入群是否需要被邀请者同意
        /// </summary>
        [MemoryPackOrder(12)]
        [Id(12)]
        public bool InviteConsentRequired { get; set; }

        /// <summary>
        /// 待审核的加群申请（用户ID → 申请条目）
        /// </summary>
        [MemoryPackOrder(13)]
        [Id(13)]
        public Dictionary<ulong, IMGroupJoinApplicationEntry> PendingJoinApplications { get; set; } = new();

        /// <summary>
        /// 待确认的邀请（用户ID → 邀请条目）
        /// </summary>
        [MemoryPackOrder(14)]
        [Id(14)]
        public Dictionary<ulong, IMGroupPendingInviteEntry> PendingInvites { get; set; } = new();

        /// <summary>
        /// 待群主审批的入群邀请（被邀请者用户ID → 审批条目）。
        /// 非群主成员发起邀请时先放入此队列，等待群主 <see cref="IMGroupMemberRole.Owner"/> 审批后才转为 <see cref="PendingInvites"/> 或直接加入。
        /// </summary>
        [MemoryPackOrder(15)]
        [Id(15)]
        public Dictionary<ulong, IMGroupPendingInviteApprovalEntry> PendingInviteApprovals { get; set; } = new();

        /// <summary>
        /// 非群主成员发起邀请时是否需要群主审批。
        /// 默认为 <c>false</c>：普通成员邀请好友时邀请通知直接送达被邀请者，无需群主介入。
        /// 设为 <c>true</c> 时，非群主发出的邀请先进入 <see cref="PendingInviteApprovals"/> 队列，由群主审批后才送达被邀请者。
        /// </summary>
        [MemoryPackOrder(16)]
        [Id(16)]
        public bool MemberInviteRequiresApproval { get; set; }
    }

    /// <summary>
    /// 群成员条目
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class IMGroupMemberEntry
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
        /// 用户头像
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public string Avatar { get; set; } = "";

        /// <summary>
        /// 群内昵称
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public string GroupNickname { get; set; } = "";

        /// <summary>
        /// 群角色
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public IMGroupMemberRole Role { get; set; } = IMGroupMemberRole.Member;

        /// <summary>
        /// 加入时间
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public long JoinTime { get; set; }
    }

    /// <summary>
    /// 群聊消息记录
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class IMGroupChatRecord
    {
        /// <summary>
        /// 服务器消息ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public string ServerMessageId { get; set; } = "";

        /// <summary>
        /// 发送者ID
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
        /// 消息内容
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public string Content { get; set; } = "";

        /// <summary>
        /// 内容类型
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public IMContentType ContentType { get; set; }

        /// <summary>
        /// 服务器时间戳
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public long Timestamp { get; set; }

        /// <summary>
        /// @的用户ID列表
        /// </summary>
        [MemoryPackOrder(6)]
        [Id(6)]
        public List<ulong> MentionedUserIds { get; set; } = new();

        /// <summary>
        /// 是否@所有人
        /// </summary>
        [MemoryPackOrder(7)]
        [Id(7)]
        public bool MentionAll { get; set; }

        /// <summary>
        /// 消息状态
        /// </summary>
        [MemoryPackOrder(8)]
        [Id(8)]
        public IMMessageStatus Status { get; set; } = IMMessageStatus.Sent;
    }

    /// <summary>
    /// 加群申请条目
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class IMGroupJoinApplicationEntry
    {
        /// <summary>
        /// 申请人ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong ApplicantId { get; set; }

        /// <summary>
        /// 申请人昵称（快照）
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string ApplicantName { get; set; } = "";

        /// <summary>
        /// 申请理由
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public string Reason { get; set; } = "";

        /// <summary>
        /// 申请时间戳
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public long ApplyTime { get; set; }
    }

    /// <summary>
    /// 待确认入群邀请条目
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class IMGroupPendingInviteEntry
    {
        /// <summary>
        /// 被邀请用户ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong InviteeId { get; set; }

        /// <summary>
        /// 邀请人ID
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public ulong InviterId { get; set; }

        /// <summary>
        /// 邀请时间戳
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public long InviteTime { get; set; }
    }

    /// <summary>
    /// 待群主审批的入群邀请条目（非群主成员发起的邀请在群主审批前停留在此处）。
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class IMGroupPendingInviteApprovalEntry
    {
        /// <summary>
        /// 被邀请用户ID（审批主键）
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong InviteeId { get; set; }

        /// <summary>
        /// 发起邀请的成员ID（非群主）
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public ulong InviterId { get; set; }

        /// <summary>
        /// 发起邀请的成员昵称快照
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public string InviterName { get; set; } = "";

        /// <summary>
        /// 发起时间戳（毫秒）
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public long RequestTime { get; set; }
    }

    #endregion
}
