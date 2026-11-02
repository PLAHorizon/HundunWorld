using Horizon.Game.Message.Enums;
using MemoryPack;
using Orleans;
using MemoryPack;
using System;
using System.Collections.Generic;

namespace Horizon.Game.Message.Network
{
    #region 聊天系统消息

    /// <summary>
    /// 聊天消息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(29, Enums.ServiceType.Chat)]
    public partial class ChatMessage : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 发送者角色ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong SenderId { get; set; }

        /// <summary>
        /// 发送者角色名
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string SenderName { get; set; } = "";

        /// <summary>
        /// 接收者ID（私聊时使用）
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public ulong ReceiverId { get; set; }

        /// <summary>
        /// 接收者名称（私聊时使用）
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public string ReceiverName { get; set; } = "";

        /// <summary>
        /// 聊天频道类型
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public ChatChannel ChannelType { get; set; }

        /// <summary>
        /// 消息内容
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public string Content { get; set; } = "";

        /// <summary>
        /// 发送时间
        /// </summary>
        [MemoryPackOrder(6)]
        [Id(6)]
        public long Timestamp { get; set; }

        /// <summary>
        /// 消息ID
        /// </summary>
        [MemoryPackOrder(7)]
        [Id(7)]
        public string MessageId { get; set; } = "";
        [MemoryPackOrder(8)]
        [Id(8)]
        public bool IsSystemMessage { get; set; } = false;
        [MemoryPackOrder(9)]
        [Id(9)]
        public string Color { get; set; } = "White";
        [MemoryPackOrder(10)]
        [Id(10)]
        public MessageType Type { get; set; } = MessageType.Chat;
        [MemoryPackOrder(11)]
        [Id(11)]
        public ServiceType ServiceType { get; set; } = ServiceType.Chat;
    }

    /// <summary>
    /// 聊天频道类型
    /// </summary>
    public enum ChatChannel
    {
        /// <summary>
        /// 世界频道
        /// </summary>
        World = 0,

        /// <summary>
        /// 附近频道
        /// </summary>
        Nearby = 1,

        /// <summary>
        /// 门派频道
        /// </summary>
        Sect = 2,

        /// <summary>
        /// 帮派频道
        /// </summary>
        Guild = 3,

        /// <summary>
        /// 队伍频道
        /// </summary>
        Team = 4,

        /// <summary>
        /// 私聊频道
        /// </summary>
        Private = 5,

        /// <summary>
        /// 系统频道
        /// </summary>
        System = 6,

        /// <summary>
        /// 团队频道（团队/团队副本，与队伍区分）
        /// </summary>
        Raid = 7
    }

    /// <summary>
    /// 聊天历史消息请求
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(41, Enums.ServiceType.Chat)]
    public partial class ChatHistoryRequest : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 角色ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong CharacterId { get; set; }

        /// <summary>
        /// 聊天频道类型
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public ChatChannel ChannelType { get; set; }

        /// <summary>
        /// 时间范围起始时间
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public long StartTime { get; set; }

        /// <summary>
        /// 时间范围结束时间
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public long EndTime { get; set; }

        /// <summary>
        /// 请求的消息数量
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public int Count { get; set; }

        /// <summary>
        /// 分页游标：返回该消息ID之前的历史（0=从最新开始）
        /// </summary>
        [MemoryPackOrder(7)]
        [Id(7)]
        public long BeforeMessageId { get; set; }

        /// <summary>
        /// 密聊对端角色ID（仅 Private 频道历史使用；0 表示非密聊）
        /// </summary>
        [MemoryPackOrder(8)]
        [Id(8)]
        public ulong PeerId { get; set; }

        [MemoryPackOrder(5)]
        [Id(5)]
        public MessageType Type { get; set; } = MessageType.ChatHistory;
        [MemoryPackOrder(6)]
        [Id(6)]
        public ServiceType ServiceType { get; set; } = ServiceType.Chat;
    }

    /// <summary>
    /// 聊天历史消息响应
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(42, Enums.ServiceType.Chat)]
    public partial class ChatHistoryResponse : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 聊天消息列表
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public List<ChatMessage> Messages { get; set; } = new();

        /// <summary>
        /// 是否有更多消息
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public bool HasMore { get; set; }

        [MemoryPackOrder(2)]
        [Id(2)]
        public MessageType Type { get; set; } = MessageType.Chat;
        [MemoryPackOrder(3)]
        [Id(3)]
        public ServiceType ServiceType { get; set; } = ServiceType.Chat;
    }

    #endregion

    #region 好友系统消息

    /// <summary>
    /// 好友信息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(43, Enums.ServiceType.Social)]
    public partial class FriendInfo : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 好友角色ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong FriendId { get; set; }

        /// <summary>
        /// 好友角色名
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string FriendName { get; set; } = "";

        /// <summary>
        /// 好友等级
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public int Level { get; set; }

        /// <summary>
        /// 好友职业
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public string Profession { get; set; } = "";

        /// <summary>
        /// 是否在线
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public bool IsOnline { get; set; }

        /// <summary>
        /// 最后登录时间
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public long LastLoginTime { get; set; }

        /// <summary>
        /// 好友备注
        /// </summary>
        [MemoryPackOrder(6)]
        [Id(6)]
        public string Remark { get; set; } = "";

        /// <summary>
        /// 好友关系亲密度
        /// </summary>
        [MemoryPackOrder(7)]
        [Id(7)]
        public int Intimacy { get; set; }

        [MemoryPackOrder(8)]
        [Id(8)]
        public MessageType Type { get; set; } = MessageType.Friend;
        [MemoryPackOrder(9)]
        [Id(9)]
        public ServiceType ServiceType { get; set; } = ServiceType.Social;
    }

    /// <summary>
    /// 添加好友请求
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(44, Enums.ServiceType.Social)]
    public partial class AddFriendRequest : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 请求者角色ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong RequesterId { get; set; }

        /// <summary>
        /// 被请求者角色ID
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public ulong TargetId { get; set; }

        /// <summary>
        /// 添加好友的验证消息
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public string VerificationMessage { get; set; } = "";

        [MemoryPackOrder(3)]
        [Id(3)]
        public MessageType Type { get; set; } = MessageType.Friend;
        [MemoryPackOrder(4)]
        [Id(4)]
        public ServiceType ServiceType { get; set; } = ServiceType.Social;
    }

    /// <summary>
    /// 添加好友响应
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(45, Enums.ServiceType.Social)]
    public partial class AddFriendResponse : MessageUnion, INetworkMessage
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
        /// 好友信息
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public FriendInfo FriendInfo { get; set; } = new();

        [MemoryPackOrder(3)]
        [Id(3)]
        public MessageType Type { get; set; } = MessageType.Friend;
        [MemoryPackOrder(4)]
        [Id(4)]
        public ServiceType ServiceType { get; set; } = ServiceType.Social;
    }

    /// <summary>
    /// 好友列表更新消息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(46, Enums.ServiceType.Social)]
    public partial class FriendListUpdateMessage : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 角色ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong CharacterId { get; set; }

        /// <summary>
        /// 好友列表
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public List<FriendInfo> Friends { get; set; } = new();

        [MemoryPackOrder(2)]
        [Id(2)]
        public MessageType Type { get; set; } = MessageType.Friend;
        [MemoryPackOrder(3)]
        [Id(3)]
        public ServiceType ServiceType { get; set; } = ServiceType.Social;
    }

    /// <summary>
    /// 好友列表请求（客户端 → 网关）
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(243, Enums.ServiceType.Social)]
    public partial class FriendListRequestMessage : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 请求者角色ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong CharacterId { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public MessageType Type { get; set; } = MessageType.FriendList;
        [MemoryPackOrder(2)]
        [Id(2)]
        public ServiceType ServiceType { get; set; } = ServiceType.Social;
    }

    /// <summary>
    /// 删除好友请求（客户端 → 网关）
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(244, Enums.ServiceType.Social)]
    public partial class RemoveFriendRequest : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 操作者角色ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong CharacterId { get; set; }

        /// <summary>
        /// 要删除的好友角色ID
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public ulong TargetId { get; set; }

        [MemoryPackOrder(2)]
        [Id(2)]
        public MessageType Type { get; set; } = MessageType.FriendOperation;
        [MemoryPackOrder(3)]
        [Id(3)]
        public ServiceType ServiceType { get; set; } = ServiceType.Social;
    }

    /// <summary>
    /// 好友操作结果（网关 → 客户端）。Op 与协议里的好友操作码对应：1=添加 2=删除。
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(245, Enums.ServiceType.Social)]
    public partial class FriendOperationResponse : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 是否成功
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public bool Success { get; set; }

        /// <summary>
        /// 操作对象角色ID
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public ulong TargetId { get; set; }

        /// <summary>
        /// 操作码：1=添加 2=删除
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public int Op { get; set; }

        /// <summary>
        /// 结果说明（失败时给出原因，供 UI 直接展示）
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public string Message { get; set; } = "";

        [MemoryPackOrder(4)]
        [Id(4)]
        public MessageType Type { get; set; } = MessageType.FriendOperation;
        [MemoryPackOrder(5)]
        [Id(5)]
        public ServiceType ServiceType { get; set; } = ServiceType.Social;
    }

    #endregion

    #region 组队系统消息

    /// <summary>
    /// 队伍信息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(47, Enums.ServiceType.Social)]
    public partial class TeamInfo : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 队伍ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong TeamId { get; set; }

        /// <summary>
        /// 队长角色ID
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public ulong LeaderId { get; set; }

        /// <summary>
        /// 队员列表
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public List<TeamMemberInfo> Members { get; set; } = new();

        /// <summary>
        /// 队伍名称
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public string TeamName { get; set; } = "";

        /// <summary>
        /// 队伍目标
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public string TeamGoal { get; set; } = "";

        [MemoryPackOrder(5)]
        [Id(5)]
        public MessageType Type { get; set; } = MessageType.Team;
        [MemoryPackOrder(6)]
        [Id(6)]
        public ServiceType ServiceType { get; set; } = ServiceType.Social;
    }

    /// <summary>
    /// 队员信息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(48, Enums.ServiceType.Social)]
    public partial class TeamMemberInfo : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 角色ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong CharacterId { get; set; }

        /// <summary>
        /// 角色名
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string CharacterName { get; set; } = "";

        /// <summary>
        /// 角色等级
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public int Level { get; set; }

        /// <summary>
        /// 职业
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public string Profession { get; set; } = "";

        /// <summary>
        /// 是否在线
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public bool IsOnline { get; set; }

        /// <summary>
        /// 是否是队长
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public bool IsLeader { get; set; }

        /// <summary>
        /// 当前HP
        /// </summary>
        [MemoryPackOrder(6)]
        [Id(6)]
        public int CurrentHP { get; set; }

        /// <summary>
        /// 最大HP
        /// </summary>
        [MemoryPackOrder(7)]
        [Id(7)]
        public int MaxHP { get; set; }

        [MemoryPackOrder(8)]
        [Id(8)]
        public MessageType Type { get; set; } = MessageType.Team;
        [MemoryPackOrder(9)]
        [Id(9)]
        public ServiceType ServiceType { get; set; } = ServiceType.Social;
    }

    /// <summary>组队操作（复用 CreateTeamRequest/Response 的 union tag；tag 空间已用尽）。</summary>
    public enum TeamOperation : byte
    {
        Create = 0,
        Join = 1,
        Leave = 2,
        Kick = 3,
        Transfer = 4,
        Disband = 5,
    }

    /// <summary>
    /// 创建队伍请求
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(49, Enums.ServiceType.Social)]
    public partial class CreateTeamRequest : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 队长角色ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong LeaderId { get; set; }

        /// <summary>
        /// 队伍名称
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string TeamName { get; set; } = "";

        /// <summary>
        /// 队伍目标
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public string TeamGoal { get; set; } = "";

        [MemoryPackOrder(3)]
        [Id(3)]
        public TeamOperation Operation { get; set; } = TeamOperation.Create;

        [MemoryPackOrder(4)]
        [Id(4)]
        public ulong TeamId { get; set; }

        [MemoryPackOrder(5)]
        [Id(5)]
        public ulong TargetId { get; set; }

        [MemoryPackOrder(6)]
        [Id(6)]
        public MessageType Type { get; set; } = MessageType.Team;
        [MemoryPackOrder(7)]
        [Id(7)]
        public ServiceType ServiceType { get; set; } = ServiceType.Social;
    }

    /// <summary>
    /// 创建队伍响应
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(50, Enums.ServiceType.Social)]
    public partial class CreateTeamResponse : MessageUnion, INetworkMessage
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
        /// 队伍信息
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public TeamInfo TeamInfo { get; set; } = new();

        [MemoryPackOrder(3)]
        [Id(3)]
        public TeamOperation Operation { get; set; } = TeamOperation.Create;

        [MemoryPackOrder(4)]
        [Id(4)]
        public MessageType Type { get; set; } = MessageType.Team;
        [MemoryPackOrder(5)]
        [Id(5)]
        public ServiceType ServiceType { get; set; } = ServiceType.Social;
    }

    /// <summary>
    /// 加入队伍请求
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(51, Enums.ServiceType.Social)]
    public partial class JoinTeamRequest : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 请求者角色ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong RequesterId { get; set; }

        /// <summary>
        /// 队伍ID
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public ulong TeamId { get; set; }

        [MemoryPackOrder(2)]
        [Id(2)]
        public MessageType Type { get; set; } = MessageType.Team;
        [MemoryPackOrder(3)]
        [Id(3)]
        public ServiceType ServiceType { get; set; } = ServiceType.Social;
    }

    /// <summary>
    /// 加入队伍响应
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(52, Enums.ServiceType.Social)]
    public partial class JoinTeamResponse : MessageUnion, INetworkMessage
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
        /// 队伍信息
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public TeamInfo TeamInfo { get; set; } = new();

        [MemoryPackOrder(3)]
        [Id(3)]
        public MessageType Type { get; set; } = MessageType.Team;
        [MemoryPackOrder(4)]
        [Id(4)]
        public ServiceType ServiceType { get; set; } = ServiceType.Social;
    }

    #endregion

    #region 帮派系统消息

    /// <summary>
    /// 帮派信息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(53, Enums.ServiceType.Social)]
    public partial class GuildInfo : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 帮派ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public int GuildId { get; set; }

        /// <summary>
        /// 帮派名称
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string GuildName { get; set; } = "";

        /// <summary>
        /// 帮主角色ID
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public ulong LeaderId { get; set; }

        /// <summary>
        /// 帮主名称
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public string LeaderName { get; set; } = "";

        /// <summary>
        /// 帮派等级
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public int Level { get; set; }

        /// <summary>
        /// 帮派成员数量
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public int MemberCount { get; set; }

        /// <summary>
        /// 帮派最大成员数量
        /// </summary>
        [MemoryPackOrder(6)]
        [Id(6)]
        public int MaxMembers { get; set; }

        /// <summary>
        /// 帮派宣言
        /// </summary>
        [MemoryPackOrder(7)]
        [Id(7)]
        public string Declaration { get; set; } = "";

        /// <summary>
        /// 帮派资源
        /// </summary>
        [MemoryPackOrder(8)]
        [Id(8)]
        public Dictionary<string, int> Resources { get; set; } = new();

        [MemoryPackOrder(9)]
        [Id(9)]
        public MessageType Type { get; set; } = MessageType.Guild;
        [MemoryPackOrder(10)]
        [Id(10)]
        public ServiceType ServiceType { get; set; } = ServiceType.Social;
    }

    /// <summary>
    /// 创建帮派请求
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(54, Enums.ServiceType.Social)]
    public partial class CreateGuildRequest : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 创建者角色ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong CreatorId { get; set; }

        /// <summary>
        /// 帮派名称
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string GuildName { get; set; } = "";

        /// <summary>
        /// 帮派宣言
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public string Declaration { get; set; } = "";

        /// <summary>
        /// 消耗的金币
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public long ConsumedGold { get; set; }

        [MemoryPackOrder(4)]
        [Id(4)]
        public MessageType Type { get; set; } = MessageType.Guild;
        [MemoryPackOrder(5)]
        [Id(5)]
        public ServiceType ServiceType { get; set; } = ServiceType.Social;
    }

    /// <summary>
    /// 创建帮派响应
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(55, Enums.ServiceType.Social)]
    public partial class CreateGuildResponse : MessageUnion, INetworkMessage
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
        /// 帮派信息
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public GuildInfo GuildInfo { get; set; } = new();

        [MemoryPackOrder(3)]
        [Id(3)]
        public MessageType Type { get; set; } = MessageType.Guild;
        [MemoryPackOrder(4)]
        [Id(4)]
        public ServiceType ServiceType { get; set; } = ServiceType.Social;
    }

    /// <summary>
    /// 加入帮派请求
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(56, Enums.ServiceType.Social)]
    public partial class JoinGuildRequest : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 请求者角色ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong RequesterId { get; set; }

        /// <summary>
        /// 帮派ID
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public int GuildId { get; set; }

        /// <summary>
        /// 加入申请理由
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public string ApplicationReason { get; set; } = "";

        [MemoryPackOrder(3)]
        [Id(3)]
        public MessageType Type { get; set; } = MessageType.Guild;
        [MemoryPackOrder(4)]
        [Id(4)]
        public ServiceType ServiceType { get; set; } = ServiceType.Social;
    }

    /// <summary>
    /// 加入帮派响应
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(57, Enums.ServiceType.Social)]
    public partial class JoinGuildResponse : MessageUnion, INetworkMessage
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
        /// 帮派信息
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public GuildInfo GuildInfo { get; set; } = new();

        [MemoryPackOrder(3)]
        [Id(3)]
        public MessageType Type { get; set; } = MessageType.Guild;
        [MemoryPackOrder(4)]
        [Id(4)]
        public ServiceType ServiceType { get; set; } = ServiceType.Social;
    }

    #endregion

    #region 帮派成员消息

    /// <summary>
    /// 帮派成员信息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(121, Enums.ServiceType.Social)]
    public partial class GuildMember : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 角色ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong CharacterId { get; set; }

        /// <summary>
        /// 角色名称
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string CharacterName { get; set; } = "";

        /// <summary>
        /// 角色等级
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public int Level { get; set; }

        /// <summary>
        /// 职业
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public string Profession { get; set; } = "";

        /// <summary>
        /// 帮派职位
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public string GuildPosition { get; set; } = "";

        /// <summary>
        /// 贡献度
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public int Contribution { get; set; }

        /// <summary>
        /// 是否在线
        /// </summary>
        [MemoryPackOrder(6)]
        [Id(6)]
        public bool IsOnline { get; set; }

        /// <summary>
        /// 最后登录时间
        /// </summary>
        [MemoryPackOrder(7)]
        [Id(7)]
        public long LastLoginTime { get; set; }

        /// <summary>
        /// 当前所在地图
        /// </summary>
        [MemoryPackOrder(8)]
        [Id(8)]
        public string CurrentMap { get; set; } = "";

        /// <summary>
        /// 当前HP
        /// </summary>
        [MemoryPackOrder(9)]
        [Id(9)]
        public int CurrentHP { get; set; }

        /// <summary>
        /// 最大HP
        /// </summary>
        [MemoryPackOrder(10)]
        [Id(10)]
        public int MaxHP { get; set; }

        /// <summary>
        /// 当前MP
        /// </summary>
        [MemoryPackOrder(11)]
        [Id(11)]
        public int CurrentMP { get; set; }

        /// <summary>
        /// 最大MP
        /// </summary>
        [MemoryPackOrder(12)]
        [Id(12)]
        public int MaxMP { get; set; }

        [MemoryPackOrder(13)]
        [Id(13)]
        public MessageType Type { get; set; } = MessageType.GuildMember;
        [MemoryPackOrder(14)]
        [Id(14)]
        public ServiceType ServiceType { get; set; } = ServiceType.Social;
    }

    #endregion

    #region 任务系统消息

    /// <summary>
    /// 任务信息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(58, Enums.ServiceType.Quest)]
    public partial class QuestInfo : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 任务ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public int QuestId { get; set; }

        /// <summary>
        /// 任务名称
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string QuestName { get; set; } = "";

        /// <summary>
        /// 任务描述
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public string Description { get; set; } = "";

        /// <summary>
        /// 任务类型
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public int QuestType { get; set; }

        /// <summary>
        /// 任务等级
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public int Level { get; set; }

        /// <summary>
        /// 任务状态
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public QuestStatus Status { get; set; }

        /// <summary>
        /// 任务目标
        /// </summary>
        [MemoryPackOrder(6)]
        [Id(6)]
        public List<QuestObjective> Objectives { get; set; } = new();

        /// <summary>
        /// 任务奖励
        /// </summary>
        [MemoryPackOrder(7)]
        [Id(7)]
        public Dictionary<string, int> Rewards { get; set; } = new();

        [MemoryPackOrder(8)]
        [Id(8)]
        public MessageType Type { get; set; } = MessageType.QuestUpdate;
        [MemoryPackOrder(9)]
        [Id(9)]
        public ServiceType ServiceType { get; set; } = ServiceType.Quest;
    }

    /// <summary>
    /// 任务状态
    /// </summary>
    public enum QuestStatus
    {
        /// <summary>
        /// 未接受
        /// </summary>
        NotAccepted = 0,

        /// <summary>
        /// 进行中
        /// </summary>
        InProgress = 1,

        /// <summary>
        /// 已完成
        /// </summary>
        Completed = 2,

        /// <summary>
        /// 已提交
        /// </summary>
        Submitted = 3
    }

    /// <summary>
    /// 任务目标
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(59, Enums.ServiceType.Quest)]
    public partial class QuestObjective : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 目标类型
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public string ObjectiveType { get; set; } = "";

        /// <summary>
        /// 目标描述
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string Description { get; set; } = "";

        /// <summary>
        /// 需要完成的数量
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public int RequiredCount { get; set; }

        /// <summary>
        /// 当前完成的数量
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public int CurrentCount { get; set; }

        /// <summary>
        /// 是否完成
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public bool IsCompleted { get; set; }

        [MemoryPackOrder(5)]
        [Id(5)]
        public MessageType Type { get; set; } = MessageType.QuestUpdate;
        [MemoryPackOrder(6)]
        [Id(6)]
        public ServiceType ServiceType { get; set; } = ServiceType.Quest;
    }

    /// <summary>
    /// 任务更新消息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(60, Enums.ServiceType.Quest)]
    public partial class QuestUpdateMessage : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 角色ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong CharacterId { get; set; }

        /// <summary>
        /// 任务ID
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public int QuestId { get; set; }

        /// <summary>
        /// 更新后的任务信息
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public QuestInfo UpdatedQuest { get; set; } = new();

        [MemoryPackOrder(3)]
        [Id(3)]
        public MessageType Type { get; set; } = MessageType.QuestUpdate;
        [MemoryPackOrder(4)]
        [Id(4)]
        public ServiceType ServiceType { get; set; } = ServiceType.Quest;
    }

    /// <summary>
    /// 接受任务请求
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(61, Enums.ServiceType.Quest)]
    public partial class AcceptQuestRequest : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 角色ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong CharacterId { get; set; }

        /// <summary>
        /// 任务ID
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public int QuestId { get; set; }

        [MemoryPackOrder(2)]
        [Id(2)]
        public MessageType Type { get; set; } = MessageType.AcceptQuest;
        [MemoryPackOrder(3)]
        [Id(3)]
        public ServiceType ServiceType { get; set; } = ServiceType.Quest;

        /// <summary>
        /// 发布者实体 id（玩家正在交互的那个 NPC；0 = 没带）。
        /// <para>服务端用它核对"这个任务是不是这位发布的"——不核对时，直连一条 questId
        /// 就能在任何地方接走高等级任务，而可接列表是按发布者过滤过的。</para>
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public ulong EntityId { get; set; }
    }

    /// <summary>
    /// 接受任务响应
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(62, Enums.ServiceType.Quest)]
    public partial class AcceptQuestResponse : MessageUnion, INetworkMessage
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
        /// 接受的任务信息
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public QuestInfo AcceptedQuest { get; set; } = new();

        [MemoryPackOrder(3)]
        [Id(3)]
        public MessageType Type { get; set; } = MessageType.AcceptQuest;
        [MemoryPackOrder(4)]
        [Id(4)]
        public ServiceType ServiceType { get; set; } = ServiceType.Quest;
    }

    /// <summary>
    /// 完成任务请求
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(63, Enums.ServiceType.Quest)]
    public partial class CompleteQuestRequest : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 角色ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong CharacterId { get; set; }

        /// <summary>
        /// 任务ID
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public int QuestId { get; set; }

        [MemoryPackOrder(2)]
        [Id(2)]
        public MessageType Type { get; set; } = MessageType.CompleteQuest;
        [MemoryPackOrder(3)]
        [Id(3)]
        public ServiceType ServiceType { get; set; } = ServiceType.Quest;

        /// <summary>
        /// 正在交互的对象实体 id（0 = 未指定）。任务指定了交付者时，服务端用它核对"是不是该在这里交"。
        /// <para>客户端只报实体 id，类别与模板号由服务端用 DS 上行注册表解析——
        /// 与接取/交互同一条口径：客户端声明的身份一律不采信。</para>
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public ulong EntityId { get; set; }
    }

    /// <summary>
    /// 完成任务响应
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(64, Enums.ServiceType.Quest)]
    public partial class CompleteQuestResponse : MessageUnion, INetworkMessage
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
        /// 获得的奖励
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public Dictionary<string, int> Rewards { get; set; } = new();

        /// <summary>
        /// 完成的任务ID
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public int CompletedQuestId { get; set; }

        [MemoryPackOrder(4)]
        [Id(4)]
        public MessageType Type { get; set; } = MessageType.CompleteQuest;
        [MemoryPackOrder(5)]
        [Id(5)]
        public ServiceType ServiceType { get; set; } = ServiceType.Quest;
    }

    #endregion

    #region 帮派扩展消息

    /// <summary>
    /// 帮派详细信息消息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(135, Enums.ServiceType.Social)]
    public partial class GuildInfoMessage : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 帮派ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public int GuildId { get; set; }

        /// <summary>
        /// 帮派名称
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string GuildName { get; set; } = "";

        /// <summary>
        /// 帮主角色ID
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public ulong LeaderId { get; set; }

        /// <summary>
        /// 帮主名称
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public string LeaderName { get; set; } = "";

        /// <summary>
        /// 帮派等级
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public int Level { get; set; }

        /// <summary>
        /// 帮派成员数量
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public int MemberCount { get; set; }

        /// <summary>
        /// 帮派最大成员数量
        /// </summary>
        [MemoryPackOrder(6)]
        [Id(6)]
        public int MaxMembers { get; set; }

        /// <summary>
        /// 帮派宣言
        /// </summary>
        [MemoryPackOrder(7)]
        [Id(7)]
        public string Declaration { get; set; } = "";

        /// <summary>
        /// 帮派技能信息列表
        /// </summary>
        [MemoryPackOrder(8)]
        [Id(8)]
        public List<GuildSkillInfo> Skills { get; set; } = new();

        /// <summary>
        /// 帮派资源
        /// </summary>
        [MemoryPackOrder(9)]
        [Id(9)]
        public Dictionary<string, int> Resources { get; set; } = new();

        [MemoryPackOrder(10)]
        [Id(10)]
        public MessageType Type { get; set; } = MessageType.GuildInfo;
        [MemoryPackOrder(11)]
        [Id(11)]
        public ServiceType ServiceType { get; set; } = ServiceType.Social;
    }

    /// <summary>
    /// 帮派技能信息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(136, Enums.ServiceType.Social)]
    public partial class GuildSkillInfo : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 技能ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public int SkillId { get; set; }

        /// <summary>
        /// 技能名称
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string SkillName { get; set; } = "";

        /// <summary>
        /// 技能等级
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public int Level { get; set; }

        /// <summary>
        /// 技能描述
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public string Description { get; set; } = "";

        /// <summary>
        /// 学习所需条件
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public string Requirements { get; set; } = "";

        [MemoryPackOrder(5)]
        [Id(5)]
        public MessageType Type { get; set; } = MessageType.GuildSkillInfo;
        [MemoryPackOrder(6)]
        [Id(6)]
        public ServiceType ServiceType { get; set; } = ServiceType.Social;
    }

    #endregion

    #region 装备对比消息

    /// <summary>
    /// 装备对比请求消息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(184, Enums.ServiceType.Game)]
    public partial class EquipmentComparisonMessage : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 当前装备ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong CurrentEquipmentId { get; set; }

        /// <summary>
        /// 对比装备ID
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public ulong CompareEquipmentId { get; set; }

        /// <summary>
        /// 装备槽位（0=武器,1=头盔,2=衣服,3=护手,4=鞋子,5=饰品）
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public int SlotIndex { get; set; }

        /// <summary>
        /// 当前装备属性列表
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public List<EquipmentStatInfo> CurrentStats { get; set; } = new();

        /// <summary>
        /// 对比装备属性列表
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public List<EquipmentStatInfo> CompareStats { get; set; } = new();

        [MemoryPackOrder(5)]
        [Id(5)]
        public MessageType Type { get; set; } = MessageType.EquipmentComparison;
        [MemoryPackOrder(6)]
        [Id(6)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    /// <summary>
    /// 装备属性信息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class EquipmentStatInfo
    {
        /// <summary>
        /// 属性名称
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public string StatName { get; set; } = "";

        /// <summary>
        /// 属性值
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public float StatValue { get; set; }

        /// <summary>
        /// 差异值（正数表示提升，负数表示下降）
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public float DiffValue { get; set; }
    }

    #endregion

    #region 公会管理消息

    /// <summary>
    /// 公会管理操作类型
    /// </summary>
    public enum GuildManagementAction
    {
        /// <summary>申请加入</summary>
        Apply = 0,
        /// <summary>审批申请</summary>
        Approve = 1,
        /// <summary>拒绝申请</summary>
        Reject = 2,
        /// <summary>踢出成员</summary>
        Kick = 3,
        /// <summary>提升职位</summary>
        Promote = 4,
        /// <summary>降低职位</summary>
        Demote = 5,
        /// <summary>转让帮主</summary>
        TransferLeader = 6,
        /// <summary>修改公告</summary>
        UpdateAnnouncement = 7,
        /// <summary>解散公会</summary>
        Disband = 8
    }

    /// <summary>
    /// 公会管理操作消息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(185, Enums.ServiceType.Social)]
    public partial class GuildManagementMessage : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 操作类型
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public GuildManagementAction Action { get; set; }

        /// <summary>
        /// 公会ID
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public ulong GuildId { get; set; }

        /// <summary>
        /// 操作者角色ID
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public ulong OperatorId { get; set; }

        /// <summary>
        /// 目标角色ID（踢出/提升/降低/转让时使用）
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public ulong TargetId { get; set; }

        /// <summary>
        /// 附加文本（公告/申请理由等）
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public string ExtraText { get; set; } = "";

        /// <summary>
        /// 操作是否成功
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public bool Success { get; set; }

        /// <summary>
        /// 结果消息
        /// </summary>
        [MemoryPackOrder(6)]
        [Id(6)]
        public string ResultMessage { get; set; } = "";

        [MemoryPackOrder(7)]
        [Id(7)]
        public MessageType Type { get; set; } = MessageType.GuildManagement;
        [MemoryPackOrder(8)]
        [Id(8)]
        public ServiceType ServiceType { get; set; } = ServiceType.Social;
    }

    #endregion

    #region 组队邀请消息

    /// <summary>
    /// 组队邀请操作类型
    /// </summary>
    public enum TeamInviteAction
    {
        /// <summary>发送邀请</summary>
        Invite = 0,
        /// <summary>接受邀请</summary>
        Accept = 1,
        /// <summary>拒绝邀请</summary>
        Decline = 2,
        /// <summary>取消邀请</summary>
        Cancel = 3,
        /// <summary>申请加入</summary>
        Apply = 4
    }

    /// <summary>
    /// 组队邀请消息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(186, Enums.ServiceType.Social)]
    public partial class TeamInviteMessage : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 邀请操作类型
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public TeamInviteAction Action { get; set; }

        /// <summary>
        /// 邀请者角色ID
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public ulong InviterId { get; set; }

        /// <summary>
        /// 邀请者角色名
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public string InviterName { get; set; } = "";

        /// <summary>
        /// 被邀请者角色ID
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public ulong InviteeId { get; set; }

        /// <summary>
        /// 被邀请者角色名
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public string InviteeName { get; set; } = "";

        /// <summary>
        /// 队伍ID
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public ulong TeamId { get; set; }

        /// <summary>
        /// 邀请者等级
        /// </summary>
        [MemoryPackOrder(6)]
        [Id(6)]
        public int InviterLevel { get; set; }

        /// <summary>
        /// 操作是否成功
        /// </summary>
        [MemoryPackOrder(7)]
        [Id(7)]
        public bool Success { get; set; }

        /// <summary>
        /// 结果消息
        /// </summary>
        [MemoryPackOrder(8)]
        [Id(8)]
        public string ResultMessage { get; set; } = "";

        [MemoryPackOrder(9)]
        [Id(9)]
        public MessageType Type { get; set; } = MessageType.TeamInvite;
        [MemoryPackOrder(10)]
        [Id(10)]
        public ServiceType ServiceType { get; set; } = ServiceType.Social;
    }

    #endregion

    #region 击杀特写消息

    /// <summary>
    /// 击杀特写消息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(187, Enums.ServiceType.Game)]
    public partial class KillCamMessage : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 击杀者角色ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong KillerId { get; set; }

        /// <summary>
        /// 击杀者角色名
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string KillerName { get; set; } = "";

        /// <summary>
        /// 被击杀者角色ID
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public ulong VictimId { get; set; }

        /// <summary>
        /// 被击杀者角色名
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public string VictimName { get; set; } = "";

        /// <summary>
        /// 终结技能名称
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public string FinishingSkillName { get; set; } = "";

        /// <summary>
        /// 总伤害值
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public float TotalDamage { get; set; }

        /// <summary>
        /// 是否为暴击击杀
        /// </summary>
        [MemoryPackOrder(6)]
        [Id(6)]
        public bool IsCriticalKill { get; set; }

        /// <summary>
        /// 连杀数
        /// </summary>
        [MemoryPackOrder(7)]
        [Id(7)]
        public int KillStreak { get; set; }

        [MemoryPackOrder(8)]
        [Id(8)]
        public MessageType Type { get; set; } = MessageType.KillCam;
        [MemoryPackOrder(9)]
        [Id(9)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    #endregion

    #region 快捷键配置消息

    /// <summary>
    /// 快捷键配置消息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(188, Enums.ServiceType.Game)]
    public partial class HotkeyConfigMessage : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 角色ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong CharacterId { get; set; }

        /// <summary>
        /// 快捷键绑定列表
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public List<HotkeyBinding> Bindings { get; set; } = new();

        [MemoryPackOrder(2)]
        [Id(2)]
        public MessageType Type { get; set; } = MessageType.HotkeyConfig;
        [MemoryPackOrder(3)]
        [Id(3)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    /// <summary>
    /// 快捷键绑定信息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class HotkeyBinding
    {
        /// <summary>
        /// 技能栏槽位索引（0-9）
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public int SlotIndex { get; set; }

        /// <summary>
        /// 绑定的按键名称
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string KeyName { get; set; } = "";

        /// <summary>
        /// 绑定的技能ID
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public int SkillId { get; set; }
    }

    #endregion

    #region 音频播放消息

    /// <summary>
    /// 游戏音频类别
    /// </summary>
    public enum GameAudioCategory
    {
        /// <summary>技能音效</summary>
        Skill = 0,
        /// <summary>攻击音效</summary>
        Attack = 1,
        /// <summary>受击音效</summary>
        Hit = 2,
        /// <summary>死亡音效</summary>
        Death = 3,
        /// <summary>复活音效</summary>
        Resurrect = 4,
        /// <summary>环境音效</summary>
        Environment = 5,
        /// <summary>UI音效</summary>
        UI = 6
    }

    /// <summary>
    /// 音频播放消息 - 服务端通知客户端播放指定音效
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(189, Enums.ServiceType.Game)]
    public partial class AudioPlaybackMessage : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 音效资源路径
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public string SoundPath { get; set; } = "";

        /// <summary>
        /// 音效类别
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public GameAudioCategory Category { get; set; }

        /// <summary>
        /// 音量（0.0-1.0）
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public float Volume { get; set; } = 1.0f;

        /// <summary>
        /// 播放位置X
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public float PositionX { get; set; }

        /// <summary>
        /// 播放位置Y
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public float PositionY { get; set; }

        /// <summary>
        /// 播放位置Z
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public float PositionZ { get; set; }

        /// <summary>
        /// 是否3D空间音效
        /// </summary>
        [MemoryPackOrder(6)]
        [Id(6)]
        public bool Is3D { get; set; }

        /// <summary>
        /// 关联的技能ID（可选）
        /// </summary>
        [MemoryPackOrder(7)]
        [Id(7)]
        public int SkillId { get; set; }

        [MemoryPackOrder(8)]
        [Id(8)]
        public MessageType Type { get; set; } = MessageType.AudioPlayback;
        [MemoryPackOrder(9)]
        [Id(9)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    #endregion

    #region Buff/Debuff显示消息

    /// <summary>
    /// Buff/Debuff操作类型
    /// </summary>
    public enum BuffOperation
    {
        /// <summary>添加Buff</summary>
        Add = 0,
        /// <summary>刷新Buff持续时间</summary>
        Refresh = 1,
        /// <summary>移除Buff</summary>
        Remove = 2,
        /// <summary>叠加Buff层数</summary>
        Stack = 3
    }

    /// <summary>
    /// Buff/Debuff显示消息 - 通知客户端更新Buff/Debuff图标
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(190, Enums.ServiceType.Game)]
    public partial class BuffDisplayMessage : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 目标实体ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong TargetId { get; set; }

        /// <summary>
        /// 效果ID
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public int EffectId { get; set; }

        /// <summary>
        /// 效果名称
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public string EffectName { get; set; } = "";

        /// <summary>
        /// 效果图标路径
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public string IconPath { get; set; } = "";

        /// <summary>
        /// 剩余持续时间（秒）
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public float Duration { get; set; }

        /// <summary>
        /// 叠加层数
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public int StackCount { get; set; } = 1;

        /// <summary>
        /// 是否为增益效果（true=Buff, false=Debuff）
        /// </summary>
        [MemoryPackOrder(6)]
        [Id(6)]
        public bool IsBuff { get; set; } = true;

        /// <summary>
        /// 操作类型
        /// </summary>
        [MemoryPackOrder(7)]
        [Id(7)]
        public BuffOperation Operation { get; set; } = BuffOperation.Add;

        [MemoryPackOrder(8)]
        [Id(8)]
        public MessageType Type { get; set; } = MessageType.BuffDisplay;
        [MemoryPackOrder(9)]
        [Id(9)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    #endregion

    #region 背包拖拽消息

    /// <summary>
    /// 拖拽操作类型
    /// </summary>
    public enum DragDropOperation
    {
        /// <summary>交换两个槽位</summary>
        Swap = 0,
        /// <summary>移动到空槽位</summary>
        Move = 1,
        /// <summary>拆分物品</summary>
        Split = 2,
        /// <summary>合并相同物品</summary>
        Merge = 3
    }

    /// <summary>
    /// 背包物品拖拽消息 - 通知服务端物品拖拽操作
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(191, Enums.ServiceType.Game)]
    public partial class InventoryDragDropMessage : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 角色ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong CharacterId { get; set; }

        /// <summary>
        /// 源槽位索引
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public int SourceSlotIndex { get; set; }

        /// <summary>
        /// 目标槽位索引
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public int TargetSlotIndex { get; set; }

        /// <summary>
        /// 拖拽操作类型
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public DragDropOperation Operation { get; set; } = DragDropOperation.Swap;

        /// <summary>
        /// 拆分数量（仅在Split操作时使用）
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public int SplitCount { get; set; }

        [MemoryPackOrder(5)]
        [Id(5)]
        public MessageType Type { get; set; } = MessageType.InventoryDragDrop;
        [MemoryPackOrder(6)]
        [Id(6)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    #endregion

    #region 输入配置同步消息

    /// <summary>
    /// 输入配置同步消息 - 同步客户端输入配置到服务端
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(192, Enums.ServiceType.Game)]
    public partial class InputConfigSyncMessage : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 角色ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong CharacterId { get; set; }

        /// <summary>
        /// 技能槽位绑定列表
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public List<SkillSlotBinding> SkillBindings { get; set; } = new List<SkillSlotBinding>();

        /// <summary>
        /// 鼠标灵敏度
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public float MouseSensitivity { get; set; } = 1.0f;

        /// <summary>
        /// 是否启用自动攻击
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public bool AutoAttackEnabled { get; set; }

        /// <summary>
        /// 相机距离
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public float CameraDistance { get; set; } = 10.0f;

        [MemoryPackOrder(5)]
        [Id(5)]
        public MessageType Type { get; set; } = MessageType.InputConfigSync;
        [MemoryPackOrder(6)]
        [Id(6)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    /// <summary>
    /// 技能槽位绑定数据
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class SkillSlotBinding
    {
        /// <summary>
        /// 槽位索引
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public int SlotIndex { get; set; }

        /// <summary>
        /// 技能ID
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public int SkillId { get; set; }

        /// <summary>
        /// 绑定的按键名
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public string KeyName { get; set; } = "";
    }

    #endregion

    #region 动画状态同步消息

    /// <summary>
    /// 动画状态类型
    /// </summary>
    public enum AnimationStateType
    {
        /// <summary>待机</summary>
        Idle = 0,
        /// <summary>移动</summary>
        Moving = 1,
        /// <summary>攻击前摇</summary>
        AttackStartup = 2,
        /// <summary>攻击激活</summary>
        AttackActive = 3,
        /// <summary>攻击后摇</summary>
        AttackRecovery = 4,
        /// <summary>施法前摇</summary>
        CastStartup = 5,
        /// <summary>施法激活</summary>
        CastActive = 6,
        /// <summary>施法后摇</summary>
        CastRecovery = 7,
        /// <summary>受击</summary>
        Hit = 8,
        /// <summary>死亡</summary>
        Death = 9,
        /// <summary>蓄力</summary>
        Charging = 10,
        /// <summary>引导</summary>
        Channeling = 11
    }

    /// <summary>
    /// 动画状态同步消息
    /// 同步角色动画状态到其他客户端
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(193, Enums.ServiceType.Game)]
    public partial class AnimationSyncMessage : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 角色ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong CharacterId { get; set; }

        /// <summary>
        /// 动画状态
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public AnimationStateType AnimationState { get; set; }

        /// <summary>
        /// 动画名称
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public string AnimationName { get; set; } = "";

        /// <summary>
        /// 动画播放速度
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public float PlaybackSpeed { get; set; } = 1.0f;

        /// <summary>
        /// 动画进度（0-1）
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public float Progress { get; set; }

        /// <summary>
        /// 关联技能ID（如果有）
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public int SkillId { get; set; }

        /// <summary>
        /// 时间戳
        /// </summary>
        [MemoryPackOrder(6)]
        [Id(6)]
        public long Timestamp { get; set; }

        [MemoryPackOrder(7)]
        [Id(7)]
        public MessageType Type { get; set; } = MessageType.AnimationSync;
        [MemoryPackOrder(8)]
        [Id(8)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    #endregion

    #region 性能报告消息

    /// <summary>
    /// 性能报告消息
    /// 客户端向服务端上报性能数据
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(194, Enums.ServiceType.Game)]
    public partial class PerformanceReportMessage : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 角色ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong CharacterId { get; set; }

        /// <summary>
        /// 当前帧率
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public float CurrentFPS { get; set; }

        /// <summary>
        /// 平均帧率
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public float AverageFPS { get; set; }

        /// <summary>
        /// 网络延迟（毫秒）
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public int NetworkLatencyMs { get; set; }

        /// <summary>
        /// 内存使用量（MB）
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public float MemoryUsageMB { get; set; }

        /// <summary>
        /// 当前优化等级
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public int OptimizationLevel { get; set; }

        /// <summary>
        /// 时间戳
        /// </summary>
        [MemoryPackOrder(6)]
        [Id(6)]
        public long Timestamp { get; set; }

        [MemoryPackOrder(7)]
        [Id(7)]
        public MessageType Type { get; set; } = MessageType.PerformanceReport;
        [MemoryPackOrder(8)]
        [Id(8)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    #endregion

    #region 断线重连消息

    /// <summary>
    /// 重连状态
    /// </summary>
    public enum ReconnectionState
    {
        /// <summary>正在重连</summary>
        Reconnecting = 0,
        /// <summary>重连成功</summary>
        Reconnected = 1,
        /// <summary>重连失败</summary>
        Failed = 2,
        /// <summary>请求重新认证</summary>
        RequireReauth = 3
    }

    /// <summary>
    /// 断线重连消息
    /// 管理客户端断线重连流程
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(195, Enums.ServiceType.Game)]
    public partial class ReconnectionMessage : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 角色ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong CharacterId { get; set; }

        /// <summary>
        /// 会话令牌
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string SessionToken { get; set; } = "";

        /// <summary>
        /// 重连状态
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public ReconnectionState State { get; set; }

        /// <summary>
        /// 重连尝试次数
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public int AttemptCount { get; set; }

        /// <summary>
        /// 断线时间戳
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public long DisconnectTimestamp { get; set; }

        /// <summary>
        /// 最后确认的序列号（用于断点续传）
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public long LastAcknowledgedSequence { get; set; }

        [MemoryPackOrder(6)]
        [Id(6)]
        public MessageType Type { get; set; } = MessageType.Reconnection;
        [MemoryPackOrder(7)]
        [Id(7)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    #endregion

    #region LOD配置消息

    /// <summary>
    /// LOD配置消息
    /// 服务端下发或客户端请求LOD配置
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(196, Enums.ServiceType.Game)]
    public partial class LODConfigMessage : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 角色ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong CharacterId { get; set; }

        /// <summary>
        /// LOD等级数量
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public int LODLevelCount { get; set; } = 4;

        /// <summary>
        /// LOD切换距离列表
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public List<float> LODDistances { get; set; } = new List<float>();

        /// <summary>
        /// 是否启用遮挡剔除
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public bool EnableOcclusionCulling { get; set; } = true;

        /// <summary>
        /// 最大可见距离
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public float MaxViewDistance { get; set; } = 500.0f;

        /// <summary>
        /// 是否启用材质合批
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public bool EnableMaterialBatching { get; set; } = true;

        [MemoryPackOrder(6)]
        [Id(6)]
        public MessageType Type { get; set; } = MessageType.LODConfig;
        [MemoryPackOrder(7)]
        [Id(7)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    #endregion

    #region 粒子预算消息

    /// <summary>
    /// 粒子预算消息
    /// 控制客户端粒子系统的性能预算
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(197, Enums.ServiceType.Game)]
    public partial class ParticleBudgetMessage : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 角色ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong CharacterId { get; set; }

        /// <summary>
        /// 最大同时粒子数
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public int MaxParticleCount { get; set; } = 10000;

        /// <summary>
        /// 最大同时粒子发射器数
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public int MaxEmitterCount { get; set; } = 50;

        /// <summary>
        /// 粒子质量等级（0-3）
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public int QualityLevel { get; set; } = 2;

        /// <summary>
        /// 是否启用GPU粒子
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public bool EnableGPUParticles { get; set; } = true;

        /// <summary>
        /// 粒子可见距离
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public float ParticleViewDistance { get; set; } = 200.0f;

        [MemoryPackOrder(6)]
        [Id(6)]
        public MessageType Type { get; set; } = MessageType.ParticleBudget;
        [MemoryPackOrder(7)]
        [Id(7)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    #endregion

    #region 消息压缩配置消息

    /// <summary>
    /// 压缩算法类型
    /// </summary>
    public enum CompressionAlgorithm
    {
        /// <summary>不压缩</summary>
        None = 0,
        /// <summary>GZip压缩</summary>
        GZip = 1,
        /// <summary>Deflate压缩</summary>
        Deflate = 2,
        /// <summary>LZ4压缩</summary>
        LZ4 = 3
    }

    /// <summary>
    /// 消息压缩配置消息
    /// 配置网络消息的压缩策略
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(198, Enums.ServiceType.Game)]
    public partial class MessageCompressionConfigMessage : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 角色ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong CharacterId { get; set; }

        /// <summary>
        /// 是否启用压缩
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public bool EnableCompression { get; set; } = true;

        /// <summary>
        /// 压缩算法
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public CompressionAlgorithm Algorithm { get; set; } = CompressionAlgorithm.GZip;

        /// <summary>
        /// 最小压缩大小（字节，小于此大小不压缩）
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public int MinCompressionSize { get; set; } = 256;

        /// <summary>
        /// 批处理大小阈值
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public int BatchSizeThreshold { get; set; } = 10;

        /// <summary>
        /// 批处理时间阈值（毫秒）
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public int BatchTimeThresholdMs { get; set; } = 50;

        [MemoryPackOrder(6)]
        [Id(6)]
        public MessageType Type { get; set; } = MessageType.MessageCompressionConfig;
        [MemoryPackOrder(7)]
        [Id(7)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    #endregion

    #region Phase 9 - 聊天与好友系统客户端集成消息

    /// <summary>
    /// 聊天通知消息（服务器推送聊天消息到客户端）
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(199, Enums.ServiceType.Chat)]
    public partial class ChatNotifyMessage : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 发送者角色ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong SenderId { get; set; }

        /// <summary>
        /// 发送者名称
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string SenderName { get; set; } = "";

        /// <summary>
        /// 聊天频道
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public ChatChannel Channel { get; set; }

        /// <summary>
        /// 消息内容
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public string Content { get; set; } = "";

        /// <summary>
        /// 发送时间戳
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public long Timestamp { get; set; }

        /// <summary>
        /// 消息唯一ID
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public string ChatMessageId { get; set; } = "";

        /// <summary>
        /// 是否为系统消息
        /// </summary>
        [MemoryPackOrder(6)]
        [Id(6)]
        public bool IsSystemMessage { get; set; }

        [MemoryPackOrder(7)]
        [Id(7)]
        public MessageType Type { get; set; } = MessageType.ChatNotify;
        [MemoryPackOrder(8)]
        [Id(8)]
        public ServiceType ServiceType { get; set; } = ServiceType.Chat;

        /// <summary>
        /// 发送者等级（用于客户端展示）
        /// </summary>
        [MemoryPackOrder(9)]
        [Id(9)]
        public int SenderLevel { get; set; }

        /// <summary>
        /// 接收者角色ID（密聊时用于区分收发；0 表示频道消息）
        /// </summary>
        [MemoryPackOrder(10)]
        [Id(10)]
        public ulong ReceiverId { get; set; }
    }

    /// <summary>
    /// 好友状态更新消息（上线/下线通知）
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(200, Enums.ServiceType.Social)]
    public partial class FriendStatusUpdateMessage : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 好友角色ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong FriendId { get; set; }

        /// <summary>
        /// 好友名称
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string FriendName { get; set; } = "";

        /// <summary>
        /// 是否在线
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public bool IsOnline { get; set; }

        /// <summary>
        /// 状态变更时间戳
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public long Timestamp { get; set; }

        [MemoryPackOrder(4)]
        [Id(4)]
        public MessageType Type { get; set; } = MessageType.FriendStatusUpdate;
        [MemoryPackOrder(5)]
        [Id(5)]
        public ServiceType ServiceType { get; set; } = ServiceType.Social;
    }

    /// <summary>
    /// 好友请求通知消息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(201, Enums.ServiceType.Social)]
    public partial class FriendRequestNotifyMessage : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 请求者角色ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong RequesterId { get; set; }

        /// <summary>
        /// 请求者名称
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string RequesterName { get; set; } = "";

        /// <summary>
        /// 请求者等级
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public int RequesterLevel { get; set; }

        /// <summary>
        /// 验证消息
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public string VerificationMessage { get; set; } = "";

        /// <summary>
        /// 请求时间戳
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public long Timestamp { get; set; }

        [MemoryPackOrder(5)]
        [Id(5)]
        public MessageType Type { get; set; } = MessageType.FriendRequestNotify;
        [MemoryPackOrder(6)]
        [Id(6)]
        public ServiceType ServiceType { get; set; } = ServiceType.Social;
    }

    /// <summary>
    /// 加入聊天频道请求
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(202, Enums.ServiceType.Chat)]
    public partial class ChatChannelJoinRequest : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 角色ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong CharacterId { get; set; }

        /// <summary>
        /// 要加入的频道
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public ChatChannel Channel { get; set; }

        [MemoryPackOrder(2)]
        [Id(2)]
        public MessageType Type { get; set; } = MessageType.ChatChannelJoin;
        [MemoryPackOrder(3)]
        [Id(3)]
        public ServiceType ServiceType { get; set; } = ServiceType.Chat;
    }

    /// <summary>
    /// 离开聊天频道请求
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(203, Enums.ServiceType.Chat)]
    public partial class ChatChannelLeaveRequest : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 角色ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong CharacterId { get; set; }

        /// <summary>
        /// 要离开的频道
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public ChatChannel Channel { get; set; }

        [MemoryPackOrder(2)]
        [Id(2)]
        public MessageType Type { get; set; } = MessageType.ChatChannelLeave;
        [MemoryPackOrder(3)]
        [Id(3)]
        public ServiceType ServiceType { get; set; } = ServiceType.Chat;
    }

    /// <summary>
    /// 聊天消息撤回请求（客户端 → 服务器）。仅允许撤回自己发送的消息。
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(227, Enums.ServiceType.Chat)]
    public partial class ChatRecallRequest : MessageUnion, INetworkMessage
    {
        /// <summary>目标频道</summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ChatChannel Channel { get; set; }

        /// <summary>要撤回的消息ID</summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public long MessageId { get; set; }

        /// <summary>操作者（须为原发送者）</summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public ulong SenderId { get; set; }

        /// <summary>密聊对端ID（仅密聊撤回用于定位会话；0 表示频道消息）</summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public ulong ReceiverId { get; set; }

        [MemoryPackOrder(3)]
        [Id(3)]
        public MessageType Type { get; set; } = MessageType.ChatRecallRequest;
        [MemoryPackOrder(4)]
        [Id(4)]
        public ServiceType ServiceType { get; set; } = ServiceType.Chat;
    }

    /// <summary>
    /// 聊天消息撤回通知（服务器 → 频道内客户端）：客户端据此移除该条消息。
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(228, Enums.ServiceType.Chat)]
    public partial class ChatRecallNotify : MessageUnion, INetworkMessage
    {
        /// <summary>频道</summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ChatChannel Channel { get; set; }

        /// <summary>被撤回的消息ID</summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public long MessageId { get; set; }

        /// <summary>原发送者ID</summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public ulong SenderId { get; set; }

        /// <summary>接收者ID（密聊时用于定位会话；0 表示频道消息）</summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public ulong ReceiverId { get; set; }

        [MemoryPackOrder(4)]
        [Id(4)]
        public MessageType Type { get; set; } = MessageType.ChatRecallNotify;
        [MemoryPackOrder(5)]
        [Id(5)]
        public ServiceType ServiceType { get; set; } = ServiceType.Chat;
    }

    /// <summary>
    /// 举报请求（客户端 → 服务器）：举报某条聊天消息。
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(229, Enums.ServiceType.Chat)]
    public partial class ChatReportRequest : MessageUnion, INetworkMessage
    {
        /// <summary>频道</summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ChatChannel Channel { get; set; }

        /// <summary>被举报的消息ID</summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public long MessageId { get; set; }

        /// <summary>举报者角色ID</summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public ulong ReporterId { get; set; }

        /// <summary>举报原因（简短描述）</summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public string Reason { get; set; } = "";

        [MemoryPackOrder(4)]
        [Id(4)]
        public MessageType Type { get; set; } = MessageType.ChatReportRequest;
        [MemoryPackOrder(5)]
        [Id(5)]
        public ServiceType ServiceType { get; set; } = ServiceType.Chat;
    }

    #endregion

    #region Phase 10 - 交易、邮件、任务、副本、成就、排行榜客户端集成消息

    /// <summary>
    /// 交易请求消息（发起面对面交易）
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(140, Enums.ServiceType.Trade)]
    public partial class TradeRequestMessage : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 发起者角色ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong InitiatorId { get; set; }

        /// <summary>
        /// 目标角色ID
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public ulong TargetId { get; set; }

        /// <summary>
        /// 目标角色名称
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public string TargetName { get; set; } = "";

        [MemoryPackOrder(3)]
        [Id(3)]
        public MessageType Type { get; set; } = MessageType.TradeRequest;
        [MemoryPackOrder(4)]
        [Id(4)]
        public ServiceType ServiceType { get; set; } = ServiceType.Trade;
    }

    /// <summary>
    /// 交易响应消息（接受/拒绝交易）
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(141, Enums.ServiceType.Trade)]
    public partial class TradeResponseMessage : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 交易ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public string TradeId { get; set; } = "";

        /// <summary>
        /// 是否接受
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public bool Accepted { get; set; }

        /// <summary>
        /// 响应消息
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public string Message { get; set; } = "";

        [MemoryPackOrder(3)]
        [Id(3)]
        public MessageType Type { get; set; } = MessageType.TradeResponse;
        [MemoryPackOrder(4)]
        [Id(4)]
        public ServiceType ServiceType { get; set; } = ServiceType.Trade;
    }

    /// <summary>
    /// 交易更新通知消息（交易状态变更）
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(142, Enums.ServiceType.Trade)]
    public partial class TradeUpdateNotifyMessage : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 交易ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public string TradeId { get; set; } = "";

        /// <summary>
        /// 交易状态 (0=进行中, 1=已确认, 2=已完成, 3=已取消)
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public int Status { get; set; }

        /// <summary>
        /// 对方角色名称
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public string PartnerName { get; set; } = "";

        /// <summary>
        /// 交易货币金额
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public long CurrencyAmount { get; set; }

        /// <summary>
        /// 时间戳
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public long Timestamp { get; set; }

        [MemoryPackOrder(5)]
        [Id(5)]
        public MessageType Type { get; set; } = MessageType.TradeUpdateNotify;
        [MemoryPackOrder(6)]
        [Id(6)]
        public ServiceType ServiceType { get; set; } = ServiceType.Trade;
    }

    /// <summary>
    /// 市场上架请求消息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(143, Enums.ServiceType.Trade)]
    public partial class MarketListRequestMessage : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 卖家角色ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong SellerId { get; set; }

        /// <summary>
        /// 物品ID
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public long ItemId { get; set; }

        /// <summary>
        /// 物品数量
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public int Quantity { get; set; }

        /// <summary>
        /// 单价
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public long Price { get; set; }

        /// <summary>
        /// 货币类型
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public int CurrencyType { get; set; }

        [MemoryPackOrder(5)]
        [Id(5)]
        public MessageType Type { get; set; } = MessageType.MarketListRequest;
        [MemoryPackOrder(6)]
        [Id(6)]
        public ServiceType ServiceType { get; set; } = ServiceType.Trade;
    }

    /// <summary>
    /// 市场搜索请求消息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(144, Enums.ServiceType.Trade)]
    public partial class MarketSearchRequestMessage : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 搜索关键词
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public string Keyword { get; set; } = "";

        /// <summary>
        /// 物品分类
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public int Category { get; set; }

        /// <summary>
        /// 排序方式 (0=价格升序, 1=价格降序, 2=最新)
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public int SortBy { get; set; }

        [MemoryPackOrder(3)]
        [Id(3)]
        public MessageType Type { get; set; } = MessageType.MarketSearchRequest;
        [MemoryPackOrder(4)]
        [Id(4)]
        public ServiceType ServiceType { get; set; } = ServiceType.Trade;
    }

    /// <summary>
    /// 市场搜索响应消息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(145, Enums.ServiceType.Trade)]
    public partial class MarketSearchResponseMessage : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 搜索结果列表
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public List<MarketListingInfo> Listings { get; set; } = new();

        /// <summary>
        /// 总结果数
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public int TotalCount { get; set; }

        [MemoryPackOrder(2)]
        [Id(2)]
        public MessageType Type { get; set; } = MessageType.MarketSearchResponse;
        [MemoryPackOrder(3)]
        [Id(3)]
        public ServiceType ServiceType { get; set; } = ServiceType.Trade;
    }

    /// <summary>
    /// 市场商品信息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class MarketListingInfo
    {
        /// <summary>
        /// 商品列表ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public long ListingId { get; set; }

        /// <summary>
        /// 物品ID
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public long ItemId { get; set; }

        /// <summary>
        /// 物品名称
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public string ItemName { get; set; } = "";

        /// <summary>
        /// 数量
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public int Quantity { get; set; }

        /// <summary>
        /// 单价
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public long Price { get; set; }

        /// <summary>
        /// 卖家名称
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public string SellerName { get; set; } = "";
    }

    /// <summary>
    /// 邮件列表请求消息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(146, Enums.ServiceType.System)]
    public partial class MailListRequestMessage : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 角色ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong CharacterId { get; set; }

        /// <summary>
        /// 是否只获取未读邮件
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public bool UnreadOnly { get; set; }

        [MemoryPackOrder(2)]
        [Id(2)]
        public MessageType Type { get; set; } = MessageType.MailListRequest;
        [MemoryPackOrder(3)]
        [Id(3)]
        public ServiceType ServiceType { get; set; } = ServiceType.System;
    }

    /// <summary>
    /// 邮件列表响应消息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(147, Enums.ServiceType.System)]
    public partial class MailListResponseMessage : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 邮件列表
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public List<MailInfo> Mails { get; set; } = new();

        /// <summary>
        /// 未读数量
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public int UnreadCount { get; set; }

        [MemoryPackOrder(2)]
        [Id(2)]
        public MessageType Type { get; set; } = MessageType.MailListResponse;
        [MemoryPackOrder(3)]
        [Id(3)]
        public ServiceType ServiceType { get; set; } = ServiceType.System;
    }

    /// <summary>
    /// 邮件信息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class MailInfo
    {
        /// <summary>
        /// 邮件ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public long MailId { get; set; }

        /// <summary>
        /// 发件人名称
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string SenderName { get; set; } = "";

        /// <summary>
        /// 邮件标题
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public string Title { get; set; } = "";

        /// <summary>
        /// 邮件内容
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public string Content { get; set; } = "";

        /// <summary>
        /// 邮件类型
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public int MailType { get; set; }

        /// <summary>
        /// 是否已读
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public bool IsRead { get; set; }

        /// <summary>
        /// 是否有附件
        /// </summary>
        [MemoryPackOrder(6)]
        [Id(6)]
        public bool HasAttachment { get; set; }

        /// <summary>
        /// 附件是否已领取
        /// </summary>
        [MemoryPackOrder(7)]
        [Id(7)]
        public bool AttachmentClaimed { get; set; }

        /// <summary>
        /// 发送时间戳
        /// </summary>
        [MemoryPackOrder(8)]
        [Id(8)]
        public long Timestamp { get; set; }
    }

    /// <summary>
    /// 邮件操作消息（阅读/领取附件/删除）
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(148, Enums.ServiceType.System)]
    public partial class MailOperationMessage : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 角色ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong CharacterId { get; set; }

        /// <summary>
        /// 邮件ID
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public long MailId { get; set; }

        /// <summary>
        /// 操作类型 (0=阅读, 1=领取附件, 2=删除)
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public int OperationType { get; set; }

        /// <summary>
        /// 操作是否成功
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public bool Success { get; set; }

        [MemoryPackOrder(4)]
        [Id(4)]
        public MessageType Type { get; set; } = MessageType.MailOperation;
        [MemoryPackOrder(5)]
        [Id(5)]
        public ServiceType ServiceType { get; set; } = ServiceType.System;
    }

    /// <summary>
    /// 新邮件通知消息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(149, Enums.ServiceType.System)]
    public partial class MailNotifyMessage : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 邮件ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public long MailId { get; set; }

        /// <summary>
        /// 发件人名称
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string SenderName { get; set; } = "";

        /// <summary>
        /// 邮件标题
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public string Title { get; set; } = "";

        /// <summary>
        /// 是否有附件
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public bool HasAttachment { get; set; }

        /// <summary>
        /// 未读邮件总数
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public int UnreadCount { get; set; }

        [MemoryPackOrder(5)]
        [Id(5)]
        public MessageType Type { get; set; } = MessageType.MailNotify;
        [MemoryPackOrder(6)]
        [Id(6)]
        public ServiceType ServiceType { get; set; } = ServiceType.System;
    }

    /// <summary>
    /// 任务列表请求消息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(150, Enums.ServiceType.Quest)]
    public partial class QuestListRequestMessage : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 角色ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong CharacterId { get; set; }

        /// <summary>
        /// 是否只获取进行中的任务
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public bool ActiveOnly { get; set; }

        [MemoryPackOrder(2)]
        [Id(2)]
        public MessageType Type { get; set; } = MessageType.QuestListRequest;
        [MemoryPackOrder(3)]
        [Id(3)]
        public ServiceType ServiceType { get; set; } = ServiceType.Quest;

        /// <summary>
        /// 玩家"正对着的那个对象"的<b>运行时实体 id</b>（0 = 不按发布者收窄，只要自己身上的清单/总表）。
        /// <para>
        /// 这里刻意<b>不接受客户端声明的模板 id</b>：以前是 `giverKind + giverTemplateId` 两个整数，
        /// 谁都能填，服务端最多当显示过滤器用（也因此永远没法拿它做严格门禁）。
        /// 现在实体 id 由 DS 上行的身份注册表解析成权威的 (类别, 模板 id)，
        /// 并顺带核对"玩家离它够不够近"（<c>QuestInteractionAuthority.ResolveGiver</c>）——
        /// 于是"走近某个 NPC 才看到它能给的任务"这件事第一次变成服务端可判定的事实。
        /// </para>
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public ulong GiverEntityId { get; set; }
    }

    /// <summary>
    /// 任务列表响应消息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(151, Enums.ServiceType.Quest)]
    public partial class QuestListResponseMessage : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 任务列表
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public List<QuestSummary> Quests { get; set; } = new();

        [MemoryPackOrder(1)]
        [Id(1)]
        public MessageType Type { get; set; } = MessageType.QuestListResponse;
        [MemoryPackOrder(2)]
        [Id(2)]
        public ServiceType ServiceType { get; set; } = ServiceType.Quest;

        /// <summary>
        /// 该发布者当前能提供的新任务（服务端已按发布状态/等级/前置/是否接过筛过）。
        /// 只在请求带了发布者时非空。
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public List<QuestOfferInfo> Offers { get; set; } = new();

        /// <summary>任务目录版本号：客户端据此判断"我缓存的任务描述是不是旧的"。</summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public int CatalogVersion { get; set; }

        /// <summary>
        /// 本回包对应哪个发布者实体（0 = 全目录 / 请求未指定）。
        /// <para>为什么必须回带：头顶 <c>!</c>/<c>?</c> 要按 NPC 各自缓存清单。
        /// 以前客户端只有"最后一个被请求的发布者"这一个槽位，于是只能给"刚交互过的那一个"画符号；
        /// 并发问两个 NPC 时，晚到的回包还会把先到的清单覆盖掉（符号挂到错的 NPC 头上）。</para>
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public ulong GiverEntityId { get; set; }

        /// <summary>
        /// 这位发布者名下**当前接不了**的任务条数（等级或前置没到）。
        /// <para>服务端按发布态/等级/前置过滤可接清单，被挡下的那些客户端本来一无所知——
        /// 于是灰 <c>!</c>（"以后会有"）在客户端不可达。这里只回**条数**、不回名字，
        /// 既有 WoW 观感又不把未解锁内容泄露给客户端。</para>
        /// <para>请求未带发布者实体、或发布者未被采信时恒为 0。</para>
        /// </summary>
        [MemoryPackOrder(6)]
        [Id(6)]
        public int LockedOfferCount { get; set; }
    }

    /// <summary>
    /// 一条可接任务的摘要。与 <see cref="QuestSummary"/> 分列：那边是"我身上的任务"（带进度与状态机），
    /// 这边是"还没接的任务"（带描述与内容版本），两套字段混在一起会让客户端两种显示各猜一半。
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class QuestOfferInfo
    {
        /// <summary>任务模板 id（接取时只回传这个，奖励与条件一律服务端读模板）。</summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public int QuestId { get; set; }

        /// <summary>任务名。</summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string QuestName { get; set; } = "";

        /// <summary>任务描述。</summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public string Description { get; set; } = "";

        /// <summary>0=主线 1=支线 2=日常 3=周常。</summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public int QuestType { get; set; }

        /// <summary>内容版本号。</summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public int ContentVersion { get; set; }

        /// <summary>是否已经接在身上（画"进行中"角标，避免重复发接取请求）。</summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public bool AlreadyAccepted { get; set; }

        /// <summary>是否已交过但可重复（画"可重复"角标）。</summary>
        [MemoryPackOrder(6)]
        [Id(6)]
        public bool AlreadyFinished { get; set; }

        /// <summary>目标列表（未接取，所以 Current 恒为 0）。</summary>
        [MemoryPackOrder(7)]
        [Id(7)]
        public List<QuestObjectiveWire> Objectives { get; set; } = new();

        /// <summary>奖励预览。</summary>
        [MemoryPackOrder(8)]
        [Id(8)]
        public QuestRewardWire Reward { get; set; } = new();

        /// <summary>接取等级门槛（0=不限）。列表里就带上，玩家能看懂"为什么这条灰着"。</summary>
        [MemoryPackOrder(9)]
        [Id(9)]
        public int LevelRequire { get; set; }

        /// <summary>发布者显示名（服务端按模板目录解析）。空串表示未配置或该类别暂无目录可查。</summary>
        [MemoryPackOrder(10)]
        [Id(10)]
        public string GiverLabel { get; set; } = "";
    }

    /// <summary>
    /// 任务摘要信息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class QuestSummary
    {
        /// <summary>
        /// 任务ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public int QuestId { get; set; }

        /// <summary>
        /// 任务名称
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string QuestName { get; set; } = "";

        /// <summary>
        /// 任务类型 (0=主线, 1=支线, 2=日常, 3=周常)
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public int QuestType { get; set; }

        /// <summary>
        /// 任务状态 (0=进行中, 1=可完成, 2=已完成)
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public int Status { get; set; }

        /// <summary>
        /// 当前进度
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public int CurrentProgress { get; set; }

        /// <summary>
        /// 目标进度
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public int TargetProgress { get; set; }

        /// <summary>任务描述（服务端模板里的原文；界面上的详情栏要用，别让客户端自己编）。</summary>
        [MemoryPackOrder(6)]
        [Id(6)]
        public string Description { get; set; } = "";

        /// <summary>逐条目标（文本 + 进度）。进度条只画比值，"要做什么"必须能逐条看见。</summary>
        [MemoryPackOrder(7)]
        [Id(7)]
        public List<QuestObjectiveWire> Objectives { get; set; } = new();

        /// <summary>奖励（结构化，客户端按物品模板目录把 id 换成名字；服务端不预先拼字符串）。</summary>
        [MemoryPackOrder(8)]
        [Id(8)]
        public QuestRewardWire Reward { get; set; } = new();

        /// <summary>
        /// 本回包对应的发布者实体，是否同时是这条任务的**交付者**（交付者未单独配置时沿用发布者）。
        /// <para>头顶黄/灰 <c>?</c> 只该画在交付者头上：以前客户端拿"发布者清单里已接"当交付，
        /// 于是"发布者 ≠ 交付者"的任务会在发布者头上亮黄问号，点进去却被服务端按交付者校验拒掉。</para>
        /// <para>请求未带发布者实体时恒为 false（任务日志不需要指示器）。</para>
        /// </summary>
        [MemoryPackOrder(9)]
        [Id(9)]
        public bool TurnInHere { get; set; }
    }

    /// <summary>一条完成条件的线格式：文本来自模板，进度来自服务端状态。</summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class QuestObjectiveWire
    {
        /// <summary>给玩家看的目标文本。</summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public string Description { get; set; } = "";

        /// <summary>当前进度。</summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public int Current { get; set; }

        /// <summary>需要总数。</summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public int Required { get; set; }

        /// <summary>该目标是否已完成。</summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public bool Completed { get; set; }
    }

    /// <summary>任务奖励的线格式（与 <c>QuestRewardSpec</c> 同形；id 不预翻译成名字，客户端才有品质/图标可画）。</summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class QuestRewardWire
    {
        /// <summary>经验。</summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public long Exp { get; set; }

        /// <summary>铜币。</summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public long Gold { get; set; }

        /// <summary>物品奖励。</summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public List<QuestRewardItemWire> Items { get; set; } = new();

        /// <summary>五行域好感奖励。</summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public List<QuestFavorWire> Favor { get; set; } = new();
    }

    /// <summary>一条物品奖励（<paramref name="TemplateId"/> 可为任何物品：装备/丹药/材料/宝石/任务物品/秘籍）。</summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class QuestRewardItemWire
    {
        /// <summary>物品模板 id。</summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public int TemplateId { get; set; }

        /// <summary>数量。</summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public int Count { get; set; }
    }

    /// <summary>一条五行域好感奖励。</summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class QuestFavorWire
    {
        /// <summary>五行域 1金 2木 3水 4火 5土。</summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public int Domain { get; set; }

        /// <summary>点数。</summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public int Points { get; set; }
    }

    /// <summary>
    /// 任务进度更新通知消息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(152, Enums.ServiceType.Quest)]
    public partial class QuestProgressNotifyMessage : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 任务ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public int QuestId { get; set; }

        /// <summary>
        /// 任务名称
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string QuestName { get; set; } = "";

        /// <summary>
        /// 目标索引
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public int ObjectiveIndex { get; set; }

        /// <summary>
        /// 当前进度
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public int CurrentProgress { get; set; }

        /// <summary>
        /// 目标进度
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public int TargetProgress { get; set; }

        /// <summary>
        /// 是否已完成
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public bool IsCompleted { get; set; }

        [MemoryPackOrder(6)]
        [Id(6)]
        public MessageType Type { get; set; } = MessageType.QuestProgressNotify;
        [MemoryPackOrder(7)]
        [Id(7)]
        public ServiceType ServiceType { get; set; } = ServiceType.Quest;
    }

    /// <summary>
    /// 与世界里某个对象交互（对话 / 使用 / 触发）的请求——"与某 NPC 对话"这类完成条件唯一的事件源。
    /// <para>
    /// <b>刻意只带运行时实体 id，不带模板 id</b>：模板 id 由服务端从 DS 上行的实体身份注册表解出
    /// （设计文档 §11 方案 1）。"我和 NPC 3 说过话"客户端说了不算（一个整数就能伪造，且伪造后
    /// 校验器无从分辨）；"我和眼前这个实体 id 交互过"则可三重核对：
    /// 该 id 在本 shard 的世界里存在吗 → 它是不是可交互类别 → 它离玩家够近吗。
    /// 三条全过，推进任务目标的是<b>服务端认定的事实</b>，不是客户端的声明。
    /// </para>
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(241, Enums.ServiceType.Quest)]
    public partial class QuestInteractRequestMessage : MessageUnion, INetworkMessage
    {
        /// <summary>角色ID</summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong CharacterId { get; set; }

        /// <summary>
        /// 运行时实体 id（<c>(spawnPointId&lt;&lt;32)|(slot+1)</c>）。
        /// 玩家自己的实体 id 一定解不出可交互类别，因此自交互不可能成立。
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public ulong EntityId { get; set; }

        [MemoryPackOrder(2)]
        [Id(2)]
        public MessageType Type { get; set; } = MessageType.QuestInteractRequest;
        [MemoryPackOrder(3)]
        [Id(3)]
        public ServiceType ServiceType { get; set; } = ServiceType.Quest;
    }

    /// <summary>
    /// 交互裁决回执。<see cref="Reason"/> 用固定短码而非中文文案：客户端要按码分派表现
    /// （"太远了"要给距离提示、"对象不存在"要静默），文案改了不该拖垮表现分支。
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(242, Enums.ServiceType.Quest)]
    public partial class QuestInteractResultMessage : MessageUnion, INetworkMessage
    {
        /// <summary>角色ID（原样回填，客户端据此对齐是哪一次交互）。</summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong CharacterId { get; set; }

        /// <summary>被裁决的实体 id。</summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public ulong EntityId { get; set; }

        /// <summary>服务端是否采信这次交互。</summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public bool Ok { get; set; }

        /// <summary>不成立时的短码：<c>no_registry</c>/<c>unknown_shard</c>/<c>unknown_entity</c>/<c>not_interactable</c>/<c>no_position</c>/<c>too_far</c>；成立时为空串。</summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public string Reason { get; set; } = string.Empty;

        /// <summary>本次推进的任务目标条数（0 表示这个对象身上没有背着我的任务）。</summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public int AdvancedCount { get; set; }

        [MemoryPackOrder(5)]
        [Id(5)]
        public MessageType Type { get; set; } = MessageType.QuestInteractResult;
        [MemoryPackOrder(6)]
        [Id(6)]
        public ServiceType ServiceType { get; set; } = ServiceType.Quest;
    }

    /// <summary>
    /// 副本进入请求消息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(153, Enums.ServiceType.Game)]
    public partial class DungeonEnterRequestMessage : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 角色ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong CharacterId { get; set; }

        /// <summary>
        /// 副本模板ID
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public int DungeonTemplateId { get; set; }

        /// <summary>
        /// 难度 (0=普通, 1=困难, 2=英雄, 3=地狱)
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public int Difficulty { get; set; }

        [MemoryPackOrder(3)]
        [Id(3)]
        public MessageType Type { get; set; } = MessageType.DungeonEnterRequest;
        [MemoryPackOrder(4)]
        [Id(4)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    /// <summary>
    /// 副本状态通知消息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(154, Enums.ServiceType.Game)]
    public partial class DungeonStatusNotifyMessage : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 副本实例ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public string DungeonInstanceId { get; set; } = "";

        /// <summary>
        /// 副本名称
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string DungeonName { get; set; } = "";

        /// <summary>
        /// 副本状态 (0=准备中, 1=进行中, 2=Boss战, 3=已完成, 4=已超时)
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public int Status { get; set; }

        /// <summary>
        /// 当前玩家数
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public int CurrentPlayers { get; set; }

        /// <summary>
        /// 最大玩家数
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public int MaxPlayers { get; set; }

        /// <summary>
        /// 剩余时间（秒）
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public int RemainingSeconds { get; set; }

        [MemoryPackOrder(6)]
        [Id(6)]
        public MessageType Type { get; set; } = MessageType.DungeonStatusNotify;
        [MemoryPackOrder(7)]
        [Id(7)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    /// <summary>
    /// 成就解锁通知消息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(155, Enums.ServiceType.Game)]
    public partial class AchievementUnlockNotifyMessage : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 成就ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public int AchievementId { get; set; }

        /// <summary>
        /// 成就名称
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string AchievementName { get; set; } = "";

        /// <summary>
        /// 成就描述
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public string Description { get; set; } = "";

        /// <summary>
        /// 成就点数
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public int Points { get; set; }

        /// <summary>
        /// 成就分类
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public int Category { get; set; }

        /// <summary>
        /// 解锁时间戳
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public long UnlockTimestamp { get; set; }

        [MemoryPackOrder(6)]
        [Id(6)]
        public MessageType Type { get; set; } = MessageType.AchievementUnlockNotify;
        [MemoryPackOrder(7)]
        [Id(7)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    /// <summary>
    /// 成就列表响应消息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(156, Enums.ServiceType.Game)]
    public partial class AchievementListResponseMessage : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 成就列表
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public List<AchievementSummary> Achievements { get; set; } = new();

        /// <summary>
        /// 总成就点数
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public int TotalPoints { get; set; }

        /// <summary>
        /// 已解锁数量
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public int UnlockedCount { get; set; }

        [MemoryPackOrder(3)]
        [Id(3)]
        public MessageType Type { get; set; } = MessageType.AchievementListResponse;
        [MemoryPackOrder(4)]
        [Id(4)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    /// <summary>
    /// 成就摘要信息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class AchievementSummary
    {
        /// <summary>
        /// 成就ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public int AchievementId { get; set; }

        /// <summary>
        /// 成就名称
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string Name { get; set; } = "";

        /// <summary>
        /// 成就分类
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public int Category { get; set; }

        /// <summary>
        /// 成就点数
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public int Points { get; set; }

        /// <summary>
        /// 当前进度
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public int CurrentProgress { get; set; }

        /// <summary>
        /// 目标进度
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public int TargetProgress { get; set; }

        /// <summary>
        /// 是否已解锁
        /// </summary>
        [MemoryPackOrder(6)]
        [Id(6)]
        public bool IsUnlocked { get; set; }
    }

    /// <summary>
    /// 排行榜查询请求消息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(157, Enums.ServiceType.Game)]
    public partial class RankingQueryRequestMessage : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 排行榜类型 (0=战力, 1=等级, 2=财富, 3=成就, 4=PVP)
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public int RankingType { get; set; }

        /// <summary>
        /// 查询数量
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public int Count { get; set; }

        /// <summary>
        /// 查询者角色ID
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public ulong CharacterId { get; set; }

        [MemoryPackOrder(3)]
        [Id(3)]
        public MessageType Type { get; set; } = MessageType.RankingQueryRequest;
        [MemoryPackOrder(4)]
        [Id(4)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    /// <summary>
    /// 排行榜查询响应消息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(158, Enums.ServiceType.Game)]
    public partial class RankingQueryResponseMessage : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 排行榜类型
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public int RankingType { get; set; }

        /// <summary>
        /// 排行榜名称
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string RankingName { get; set; } = "";

        /// <summary>
        /// 排行榜条目
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public List<RankingEntryInfo> Entries { get; set; } = new();

        /// <summary>
        /// 查询者自身排名（-1表示未上榜）
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public int MyRank { get; set; } = -1;

        [MemoryPackOrder(4)]
        [Id(4)]
        public MessageType Type { get; set; } = MessageType.RankingQueryResponse;
        [MemoryPackOrder(5)]
        [Id(5)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    /// <summary>
    /// 排行榜条目信息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class RankingEntryInfo
    {
        /// <summary>
        /// 排名
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public int Rank { get; set; }

        /// <summary>
        /// 玩家名称
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string PlayerName { get; set; } = "";

        /// <summary>
        /// 分数
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public long Score { get; set; }

        /// <summary>
        /// 玩家等级
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public int Level { get; set; }
    }

    #endregion
}