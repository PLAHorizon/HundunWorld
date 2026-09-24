using Horizon.Core.Abstract;
using Horizon.Entities;
using Horizon.Game.Message.Network;
using Horizon.Model.GameModel;
using Horizon.Orleans.Interface.Chat;
using Microsoft.Extensions.Logging;
using Orleans;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Horizon.Orleans.Grains
{
    /// <summary>
    /// 游戏内聊天持久化 / 历史 Grain（仅读写 Game 库，与 IM 聊天物理隔离）。
    /// 主键 = <see cref="ChatChannel"/> 整型值（按频道分片）。落库走 <c>IDataContext</c>，
    /// 对应表 <c>Game_HunduShijie_ChatMessage</c> / <c>Game_HunduShijie_ChatPrivateMessage</c>。
    /// </summary>
    public class ChatGrain : Grain, IChatGrain
    {
        private readonly ILogger<ChatGrain> _logger;
        private readonly IDataContext<GameEntityContext, ChatMessageEntity, long> _messages;
        private readonly IDataContext<GameEntityContext, ChatPrivateMessageEntity, long> _privateMessages;
        private readonly IDataContext<GameEntityContext, ChatReportEntity, long> _reports;

        public ChatGrain(
            ILogger<ChatGrain> logger,
            IDataContext<GameEntityContext, ChatMessageEntity, long> messages,
            IDataContext<GameEntityContext, ChatPrivateMessageEntity, long> privateMessages,
            IDataContext<GameEntityContext, ChatReportEntity, long> reports)
        {
            _logger = logger;
            _messages = messages;
            _privateMessages = privateMessages;
            _reports = reports;
        }

        private ChatChannel Channel => (ChatChannel)this.GetPrimaryKeyLong();

        /// <inheritdoc />
        public async Task<long> AppendAsync(ChatAppendRequest request)
        {
            try
            {
                var timestamp = request.Timestamp > 0
                    ? new DateTime(request.Timestamp, DateTimeKind.Utc)
                    : DateTime.UtcNow;

                var entity = new ChatMessageEntity
                {
                    SenderId = (long)request.SenderId,
                    SenderName = Truncate(request.SenderName, 20),
                    SenderLevel = request.SenderLevel,
                    Channel = (int)Channel,
                    ReceiverId = request.ReceiverId != 0 ? (long)request.ReceiverId : null,
                    ReceiverName = Truncate(request.ReceiverName, 20),
                    Content = Truncate(request.Content, 500),
                    ContentType = 0,
                    SendTime = timestamp,
                    // 0-正常；2-已屏蔽（命中敏感词）：仅留档，绝不推送/回显
                    Status = request.HasSensitive ? 2 : 0,
                    HasSensitive = request.HasSensitive,
                    FilteredContent = Truncate(request.FilteredContent, 500),
                    ServerId = request.ServerId,
                };

                var saved = await _messages.AddAsync(entity);
                long messageId = saved?.Id ?? 0;

                if (Channel == ChatChannel.Private && request.ReceiverId != 0 && !request.HasSensitive)
                {
                    await _privateMessages.AddAsync(new ChatPrivateMessageEntity
                    {
                        MessageId = messageId,
                        SenderId = (long)request.SenderId,
                        ReceiverId = (long)request.ReceiverId,
                        SessionId = SessionId(request.SenderId, request.ReceiverId),
                        IsRead = false,
                        CreateTime = DateTime.UtcNow,
                    });
                }

                return messageId;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ChatGrain] 聊天落库失败: channel={Channel}, sender={Sender}", Channel, request.SenderId);
                return 0;
            }
        }

        /// <inheritdoc />
        public async Task<List<ChatNotifyMessage>> GetHistoryAsync(
            ulong characterId, ulong peerId, long beforeMessageId, int count)
        {
            if (count <= 0) count = 50;
            if (count > 200) count = 200;

            try
            {
                if (Channel == ChatChannel.Private)
                {
                    long me = (long)characterId;
                    long peer = (long)peerId;
                    var query = await _messages.QueryAsync(m =>
                        m.Status == 0 &&
                        m.Channel == (int)ChatChannel.Private &&
                        ((m.SenderId == me && m.ReceiverId == peer) ||
                         (m.SenderId == peer && m.ReceiverId == me)) &&
                        (beforeMessageId <= 0 || m.Id < beforeMessageId));

                    return query.OrderByDescending(m => m.Id).Take(count).ToList()
                        .Select(ToNotify).OrderBy(m => m.Timestamp).ToList();
                }
                else
                {
                    var query = await _messages.QueryAsync(m =>
                        m.Status == 0 &&
                        m.Channel == (int)Channel &&
                        (beforeMessageId <= 0 || m.Id < beforeMessageId));

                    return query.OrderByDescending(m => m.Id).Take(count).ToList()
                        .Select(ToNotify).OrderBy(m => m.Timestamp).ToList();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ChatGrain] 聊天历史查询失败: channel={Channel}, character={CharacterId}", Channel, characterId);
                return new List<ChatNotifyMessage>();
            }
        }

        /// <inheritdoc />
        public async Task<bool> RecallAsync(long messageId, ulong operatorId)
        {
            try
            {
                var entity = await _messages.QueryFirstOrDefaultAsync(m => m.Id == messageId);
                if (entity == null) return false;
                if (entity.Status != 0) return false;                     // 仅正常消息可撤回
                if (entity.SenderId != (long)operatorId) return false;    // 仅发送者本人

                entity.Status = 1;   // 已撤回（历史查询只返回 Status==0，故撤回后即不可见）
                return await _messages.UpdateAsync(entity, entity.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ChatGrain] 撤回消息失败: messageId={MessageId}", messageId);
                return false;
            }
        }

        /// <inheritdoc />
        public async Task<long> ReportAsync(long messageId, ulong reporterId, string reason)
        {
            try
            {
                var msg = await _messages.QueryFirstOrDefaultAsync(m => m.Id == messageId);
                if (msg == null) return 0;

                var saved = await _reports.AddAsync(new ChatReportEntity
                {
                    MessageId = messageId,
                    Channel = (int)Channel,
                    ReporterId = (long)reporterId,
                    TargetId = msg.SenderId,
                    Reason = Truncate(reason, 200),
                    Status = 0,
                    CreateTime = DateTime.UtcNow,
                });
                return saved?.Id ?? 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ChatGrain] 记录举报失败: messageId={MessageId}", messageId);
                return 0;
            }
        }

        private static ChatNotifyMessage ToNotify(ChatMessageEntity e) => new ChatNotifyMessage
        {
            SenderId = (ulong)e.SenderId,
            SenderName = e.SenderName ?? "",
            SenderLevel = e.SenderLevel,
            ReceiverId = e.ReceiverId.HasValue ? (ulong)e.ReceiverId.Value : 0ul,
            Channel = (ChatChannel)e.Channel,
            Content = e.Content ?? "",
            Timestamp = e.SendTime.Ticks,
            ChatMessageId = e.Id.ToString(),
            IsSystemMessage = e.Channel == (int)ChatChannel.System,
        };

        private static string SessionId(ulong a, ulong b)
            => a < b ? $"{a}_{b}" : $"{b}_{a}";

        private static string Truncate(string value, int max)
            => string.IsNullOrEmpty(value) || value.Length <= max ? value : value.Substring(0, max);
    }
}
