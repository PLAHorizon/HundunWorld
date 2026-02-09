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
        System = 6
    }

    /// <summary>
    /// 聊天历史消息请求
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
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

        [MemoryPackOrder(5)]
        [Id(5)]
        public MessageType Type { get; set; } = MessageType.Chat;
        [MemoryPackOrder(6)]
        [Id(6)]
        public ServiceType ServiceType { get; set; } = ServiceType.Chat;
    }

    /// <summary>
    /// 聊天历史消息响应
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
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

    #endregion

    #region 组队系统消息

    /// <summary>
    /// 队伍信息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
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

    /// <summary>
    /// 创建队伍请求
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
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
        public MessageType Type { get; set; } = MessageType.Team;
        [MemoryPackOrder(4)]
        [Id(4)]
        public ServiceType ServiceType { get; set; } = ServiceType.Social;
    }

    /// <summary>
    /// 创建队伍响应
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
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
        public MessageType Type { get; set; } = MessageType.Team;
        [MemoryPackOrder(4)]
        [Id(4)]
        public ServiceType ServiceType { get; set; } = ServiceType.Social;
    }

    /// <summary>
    /// 加入队伍请求
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
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
    }

    /// <summary>
    /// 接受任务响应
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
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
    }

    /// <summary>
    /// 完成任务响应
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
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

    #region 快捷栏操作消息

    /// <summary>
    /// 快捷栏操作类型
    /// </summary>
    public enum HotbarActionType
    {
        /// <summary>使用快捷栏槽位</summary>
        Use = 0,
        /// <summary>分配技能到槽位</summary>
        Assign = 1,
        /// <summary>清空槽位</summary>
        Clear = 2,
        /// <summary>交换两个槽位</summary>
        Swap = 3
    }

    /// <summary>
    /// 快捷栏操作消息
    /// 用于客户端通知服务器快捷栏的使用和配置操作
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class HotbarActionMessage : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 角色ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong CharacterId { get; set; }

        /// <summary>
        /// 操作类型
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public HotbarActionType ActionType { get; set; } = HotbarActionType.Use;

        /// <summary>
        /// 槽位索引（0-9）
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public int SlotIndex { get; set; }

        /// <summary>
        /// 技能ID（用于Assign操作）
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public int SkillId { get; set; }

        /// <summary>
        /// 目标槽位索引（用于Swap操作）
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public int TargetSlotIndex { get; set; }

        [MemoryPackOrder(5)]
        [Id(5)]
        public MessageType Type { get; set; } = MessageType.HotbarAction;
        [MemoryPackOrder(6)]
        [Id(6)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    #endregion

    #region 输入配置同步消息

    /// <summary>
    /// 输入配置同步消息
    /// 用于客户端与服务器之间同步输入配置
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class InputConfigSyncMessage : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 角色ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong CharacterId { get; set; }

        /// <summary>
        /// 配置数据（JSON格式）
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string ConfigData { get; set; } = "";

        /// <summary>
        /// 是否为上传操作（true=上传到服务器, false=从服务器下载）
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public bool IsUpload { get; set; }

        /// <summary>
        /// 配置版本号
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public int ConfigVersion { get; set; }

        [MemoryPackOrder(4)]
        [Id(4)]
        public MessageType Type { get; set; } = MessageType.InputConfigSync;
        [MemoryPackOrder(5)]
        [Id(5)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    #endregion
}