using Horizon.Game.Message.Enums;
using MemoryPack;
using Orleans;
using System;
using System.Collections.Generic;

namespace Horizon.Game.Message.Network
{
    #region 门派系统消息

    /// <summary>
    /// 门派信息消息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class SectInfoMessage : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 门派ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public int SectId { get; set; }

        /// <summary>
        /// 门派名称
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string SectName { get; set; } = "";

        /// <summary>
        /// 门派掌门
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public string SectLeader { get; set; } = "";

        /// <summary>
        /// 门派成员数量
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public int MemberCount { get; set; }

        /// <summary>
        /// 门派等级
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public int SectLevel { get; set; }

        /// <summary>
        /// 门派声望
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public int Reputation { get; set; }

        /// <summary>
        /// 门派资源
        /// </summary>
        [MemoryPackOrder(6)]
        [Id(6)]
        public Dictionary<string, int> Resources { get; set; } = new();

        [MemoryPackOrder(7)]
        [Id(7)]
        public MessageType Type { get; set; } = MessageType.SectInfo;
        [MemoryPackOrder(8)]
        [Id(8)]
        public ServiceType ServiceType { get; set; } = ServiceType.Social;
    }

    /// <summary>
    /// 加入门派请求
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class JoinSectRequest : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 用户ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong UserId { get; set; }

        /// <summary>
        /// 角色ID
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public ulong CharacterId { get; set; }

        /// <summary>
        /// 门派ID
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public int SectId { get; set; }

        /// <summary>
        /// 申请理由
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public string ApplicationReason { get; set; } = "";

        [MemoryPackOrder(4)]
        [Id(4)]
        public MessageType Type { get; set; } = MessageType.JoinSect;
        [MemoryPackOrder(5)]
        [Id(5)]
        public ServiceType ServiceType { get; set; } = ServiceType.Social;
    }

    /// <summary>
    /// 加入门派响应
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class JoinSectResponse : MessageUnion, INetworkMessage
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
        /// 门派ID
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public int SectId { get; set; }

        /// <summary>
        /// 角色职位
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public string Position { get; set; } = "";

        [MemoryPackOrder(4)]
        [Id(4)]
        public MessageType Type { get; set; } = MessageType.JoinSect;
        [MemoryPackOrder(5)]
        [Id(5)]
        public ServiceType ServiceType { get; set; } = ServiceType.Social;
    }

    /// <summary>
    /// 门派技能消息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class SectSkillMessage : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 门派ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public int SectId { get; set; }

        /// <summary>
        /// 技能ID
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public int SkillId { get; set; }

        /// <summary>
        /// 技能名称
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public string SkillName { get; set; } = "";

        /// <summary>
        /// 技能描述
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public string Description { get; set; } = "";

        /// <summary>
        /// 技能等级
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public int Level { get; set; }

        /// <summary>
        /// 学习条件
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public Dictionary<string, object> LearningConditions { get; set; } = new();

        [MemoryPackOrder(6)]
        [Id(6)]
        public MessageType Type { get; set; } = MessageType.SectSkill;
        [MemoryPackOrder(7)]
        [Id(7)]
        public ServiceType ServiceType { get; set; } = ServiceType.Social;
    }

    /// <summary>
    /// 门派任务消息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class SectQuestMessage : MessageUnion, INetworkMessage
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
        /// 任务奖励
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public Dictionary<string, int> Rewards { get; set; } = new();

        /// <summary>
        /// 任务要求
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public Dictionary<string, object> Requirements { get; set; } = new();

        /// <summary>
        /// 门派ID
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public int SectId { get; set; }

        [MemoryPackOrder(6)]
        [Id(6)]
        public MessageType Type { get; set; } = MessageType.SectQuest;
        [MemoryPackOrder(7)]
        [Id(7)]
        public ServiceType ServiceType { get; set; } = ServiceType.Quest;
    }

    #endregion

    #region 声望与侠义值消息

    /// <summary>
    /// 声望更新消息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class ReputationUpdateMessage : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 角色ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong CharacterId { get; set; }

        /// <summary>
        /// 声望类型
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string ReputationType { get; set; } = "";

        /// <summary>
        /// 声望变化值
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public int ChangeValue { get; set; }

        /// <summary>
        /// 当前声望值
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public int CurrentValue { get; set; }

        /// <summary>
        /// 变化原因
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public string Reason { get; set; } = "";

        [MemoryPackOrder(5)]
        [Id(5)]
        public MessageType Type { get; set; } = MessageType.Reputation;
        [MemoryPackOrder(6)]
        [Id(6)]
        public ServiceType ServiceType { get; set; } = ServiceType.Social;
    }

    /// <summary>
    /// 侠义值更新消息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class ChivalryPointUpdateMessage : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 角色ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong CharacterId { get; set; }

        /// <summary>
        /// 侠义值变化
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public int ChangeValue { get; set; }

        /// <summary>
        /// 当前侠义值
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public int CurrentValue { get; set; }

        /// <summary>
        /// 变化原因
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public string Reason { get; set; } = "";

        [MemoryPackOrder(4)]
        [Id(4)]
        public MessageType Type { get; set; } = MessageType.ChivalryPoint;
        [MemoryPackOrder(5)]
        [Id(5)]
        public ServiceType ServiceType { get; set; } = ServiceType.Social;
    }

    #endregion

    #region 社交互动消息

    /// <summary>
    /// 比武切磋请求
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class DuelRequest : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 挑战者ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong ChallengerId { get; set; }

        /// <summary>
        /// 被挑战者ID
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public ulong OpponentId { get; set; }

        /// <summary>
        /// 比武规则
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public string Rules { get; set; } = "";

        /// <summary>
        /// 赌注
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public Dictionary<string, int> Stakes { get; set; } = new();

        [MemoryPackOrder(4)]
        [Id(4)]
        public MessageType Type { get; set; } = MessageType.Duel;
        [MemoryPackOrder(5)]
        [Id(5)]
        public ServiceType ServiceType { get; set; } = ServiceType.Social;
    }

    /// <summary>
    /// 比武切磋响应
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class DuelResponse : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 是否接受
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public bool Accepted { get; set; }

        /// <summary>
        /// 消息
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string Message { get; set; } = "";

        /// <summary>
        /// 比武ID
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public ulong DuelId { get; set; }

        [MemoryPackOrder(3)]
        [Id(3)]
        public MessageType Type { get; set; } = MessageType.Duel;
        [MemoryPackOrder(4)]
        [Id(4)]
        public ServiceType ServiceType { get; set; } = ServiceType.Social;
    }

    /// <summary>
    /// 结拜请求
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class SwornBrotherRequest : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 发起者ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong InitiatorId { get; set; }

        /// <summary>
        /// 结拜对象ID列表
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public List<ulong> BrotherIds { get; set; } = new();

        /// <summary>
        /// 结拜称号
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public string Title { get; set; } = "";

        /// <summary>
        /// 结拜誓言
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public string Oath { get; set; } = "";

        [MemoryPackOrder(4)]
        [Id(4)]
        public MessageType Type { get; set; } = MessageType.SwornBrother;
        [MemoryPackOrder(5)]
        [Id(5)]
        public ServiceType ServiceType { get; set; } = ServiceType.Social;
    }

    /// <summary>
    /// 结拜响应
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class SwornBrotherResponse : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 是否同意
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public bool Agreed { get; set; }

        /// <summary>
        /// 消息
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string Message { get; set; } = "";

        /// <summary>
        /// 结拜关系ID
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public ulong BrotherhoodId { get; set; }

        [MemoryPackOrder(3)]
        [Id(3)]
        public MessageType Type { get; set; } = MessageType.SwornBrother;
        [MemoryPackOrder(4)]
        [Id(4)]
        public ServiceType ServiceType { get; set; } = ServiceType.Social;
    }

    /// <summary>
    /// 师徒关系请求
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class MasterApprenticeRequest : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 师父ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong MasterId { get; set; }

        /// <summary>
        /// 徒弟ID
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public ulong ApprenticeId { get; set; }

        /// <summary>
        /// 关系类型（0=拜师，1=收徒）
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public int RelationType { get; set; }

        /// <summary>
        /// 请求理由
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public string Reason { get; set; } = "";

        [MemoryPackOrder(4)]
        [Id(4)]
        public MessageType Type { get; set; } = MessageType.MasterApprentice;
        [MemoryPackOrder(5)]
        [Id(5)]
        public ServiceType ServiceType { get; set; } = ServiceType.Social;
    }

    /// <summary>
    /// 师徒关系响应
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class MasterApprenticeResponse : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 是否同意
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public bool Agreed { get; set; }

        /// <summary>
        /// 消息
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string Message { get; set; } = "";

        /// <summary>
        /// 师徒关系ID
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public ulong RelationshipId { get; set; }

        /// <summary>
        /// 师徒关系等级
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public int RelationshipLevel { get; set; }

        [MemoryPackOrder(4)]
        [Id(4)]
        public MessageType Type { get; set; } = MessageType.MasterApprentice;
        [MemoryPackOrder(5)]
        [Id(5)]
        public ServiceType ServiceType { get; set; } = ServiceType.Social;
    }

    #endregion
}