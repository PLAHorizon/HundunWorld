using System.Collections.Generic;
using System.Threading.Tasks;
using Horizon.Game.Message.Network;
using Orleans;

namespace Horizon.Orleans.Interface.Chat
{
    /// <summary>
    /// 一次聊天发送请求（由 Gateway 的 ChatHandler 构造后交给 <see cref="IChatGrain"/> 持久化）。
    /// </summary>
    [GenerateSerializer]
    public sealed class ChatAppendRequest
    {
        /// <summary>发送者角色ID。</summary>
        [Id(0)]
        public ulong SenderId { get; set; }

        /// <summary>发送者名称。</summary>
        [Id(1)]
        public string SenderName { get; set; } = "";

        /// <summary>发送者等级。</summary>
        [Id(2)]
        public int SenderLevel { get; set; }

        /// <summary>接收者角色ID（密聊使用，0 表示频道消息）。</summary>
        [Id(3)]
        public ulong ReceiverId { get; set; }

        /// <summary>接收者名称（密聊使用）。</summary>
        [Id(4)]
        public string ReceiverName { get; set; } = "";

        /// <summary>消息内容。</summary>
        [Id(5)]
        public string Content { get; set; } = "";

        /// <summary>客户端消息ID（原样回带，用于回执关联）。</summary>
        [Id(6)]
        public string ClientMsgId { get; set; } = "";

        /// <summary>服务器ID（分服隔离）。</summary>
        [Id(7)]
        public int ServerId { get; set; }

        /// <summary>发送时间（UTC Ticks）。</summary>
        [Id(8)]
        public long Timestamp { get; set; }

        /// <summary>是否命中敏感词（命中则仅留档、不推送、不回显给任何人）。</summary>
        [Id(9)]
        public bool HasSensitive { get; set; }

        /// <summary>过滤后内容（命中词以 * 等长替换），仅留档用。</summary>
        [Id(10)]
        public string FilteredContent { get; set; } = "";
    }

    /// <summary>
    /// 游戏内聊天持久化/历史 Grain（与 IM 聊天物理隔离，仅写 Game 库）。
    /// <para>
    /// 主键 = <see cref="Horizon.Game.Message.Network.ChatChannel"/> 的整型值（按频道分片），
    /// 密聊使用 <c>Private</c> 频道对应的主键，历史按收发双方过滤。
    /// </para>
    /// </summary>
    [global::Orleans.CodeGeneration.Version(1)]
    public interface IChatGrain : IGrainWithIntegerKey
    {
        /// <summary>
        /// 追加一条聊天消息并落库，返回消息ID（&lt;=0 表示失败）。
        /// </summary>
        Task<long> AppendAsync(ChatAppendRequest request);

        /// <summary>
        /// 查询历史消息（按频道主键）。密聊频道时仅返回 <paramref name="characterId"/> 与
        /// <paramref name="peerId"/> 之间的消息；<paramref name="beforeMessageId"/>=0 表示从最新开始。
        /// </summary>
        Task<List<ChatNotifyMessage>> GetHistoryAsync(
            ulong characterId, ulong peerId, long beforeMessageId, int count);

        /// <summary>
        /// 撤回一条消息：校验消息存在、Status==0、且 <paramref name="operatorId"/> 为原发送者；
        /// 通过则置 Status=1（已撤回）。返回 true 表示已撤回（调用方据此广播撤回通知）。
        /// </summary>
        Task<bool> RecallAsync(long messageId, ulong operatorId);

        /// <summary>
        /// 记录一条举报（写入游戏侧 <c>Game_HunduShijie_ChatReport</c>），返回举报记录ID（&lt;=0 表示失败）。
        /// 频道由 Grain 主键确定，被举报者取自原消息的发送者。
        /// </summary>
        Task<long> ReportAsync(long messageId, ulong reporterId, string reason);
    }
}
