using System.Collections.Generic;
using System.Threading.Tasks;

namespace Horizon.Game.Core.Chat
{
    /// <summary>聊天来源标识（跨来源聚合的判别键）。</summary>
    public enum ChatOrigin
    {
        /// <summary>游戏内聊天（本实现，落 Game 库）。</summary>
        Game = 0,

        /// <summary>专用 IM 聊天（耕地 PC/移动端，落 IM 体系；物理隔离，当前不实现）。</summary>
        IM = 1,
    }

    /// <summary>
    /// 跨来源聊天历史提供者——为后续 PC 端付费功能"聊天消息串联浏览"预留的接缝。
    /// <para>
    /// 设计约束：两套聊天数据物理隔离，聚合层只通过本接口按来源分别取数，
    /// 不在任何一侧引入对方的类型/表依赖。<see cref="ChatOrigin.Game"/> 侧已实现；
    /// <see cref="ChatOrigin.IM"/> 侧仅留空实现（抛 NotImplementedException），
    /// 待该付费功能真正开发时再补。
    /// </para>
    /// </summary>
    public interface IChatHistoryProvider
    {
        /// <summary>本提供者负责的来源。</summary>
        ChatOrigin Origin { get; }

        /// <summary>按统一游标查询一页消息。</summary>
        Task<ChatHistoryPage> QueryAsync(ChatHistoryQuery query);
    }

    /// <summary>跨来源历史查询条件（统一游标，避免暴露各方内部主键语义）。</summary>
    public sealed class ChatHistoryQuery
    {
        public ulong CharacterId { get; set; }
        public ulong PeerId { get; set; }
        /// <summary>频道整型值（GameChatChannel/ChatChannel）。</summary>
        public int ChannelValue { get; set; }
        /// <summary>分页游标：返回该消息ID之前的数据（0=最新）。</summary>
        public long BeforeMessageId { get; set; }
        public int Count { get; set; } = 50;
    }

    /// <summary>跨来源历史页。</summary>
    public sealed class ChatHistoryPage
    {
        public ChatOrigin Origin { get; set; }
        public List<ChatHistoryItem> Items { get; set; } = new();
        public bool HasMore { get; set; }
    }

    /// <summary>跨来源历史条目（来源中立的最小字段集）。</summary>
    public sealed class ChatHistoryItem
    {
        public long MessageId { get; set; }
        public ulong SenderId { get; set; }
        public string SenderName { get; set; } = "";
        public string Content { get; set; } = "";
        public int ChannelValue { get; set; }
        public long Timestamp { get; set; }
    }
}
