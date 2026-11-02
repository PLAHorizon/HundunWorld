using Horizon.Game.Core.Chat;
using Horizon.Game.Core.World;
using Horizon.Game.Message;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using Horizon.Orleans.Interface;
using Horizon.Orleans.Interface.Chat;
using Horizon.Orleans.Interface.World;
using MemoryPack;
using Microsoft.Extensions.Logging;
using Orleans;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TouchSocket.Sockets;

namespace Horizon.Game.Core.Handlers
{
    /// <summary>
    /// 游戏内聊天处理器（运行于 Gateway 进程）。
    /// 链路：校验 → 经 <see cref="IChatGrain"/> 落 Game 库（与 IM 隔离）→ 解析收件人 →
    /// 经 <see cref="IPlayerPushService"/> 推送给目标连接；同时把本条消息回执给发送者。
    /// </summary>
    public class ChatHandler : MessageHandlerBase
    {
        /// <summary>单条消息最大长度（与 ChatMessageEntity.content 的 nvarchar(500) 对齐）。</summary>
        private const int MaxContentLength = 500;
        /// <summary>「附近」频道水平半径（与分片位置同单位）。</summary>
        private const float NearbyRadius = 30f;
        /// <summary>「附近」频道单次最多收件人（防超大分片扇出）。</summary>
        private const int NearbyMaxRecipients = 256;

        private readonly IClusterClient _clusterClient;
        private readonly IPlayerPushService _pushService;
        private readonly IShardRouter _shardRouter;

        public ChatHandler(
            ILogger<MessageHandlerBase> logger,
            IClusterClient clusterClient,
            HorizonMessageAdapter adapter,
            IPlayerPushService pushService,
            IShardRouter shardRouter) : base(logger, clusterClient, adapter)
        {
            _clusterClient = clusterClient;
            _pushService = pushService;
            _shardRouter = shardRouter;
        }

        public override List<MessageType> MessageTypes { get; } = new List<MessageType>
        {
            MessageType.ChatSend,
            MessageType.ChatHistory,
            MessageType.ChatRecallRequest,
            MessageType.ChatReportRequest,
        };

        public override ServiceType ServiceType => ServiceType.Chat;

        public override async Task<(bool IsSuccess, MessageUnion? Response)> HandleAsync(ITcpSessionClient client, HorizonMessagePacket message)
        {
            return await base.HandleAsync(client, message);
        }

        public override async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> RouteHandlerAsync(HorizonMessagePacket message)
        {
            switch (message.Header.MessageType)
            {
                case MessageType.ChatHistory:
                    return await HandleChatHistoryAsync(message);
                case MessageType.ChatRecallRequest:
                    return await HandleChatRecallAsync(message);
                case MessageType.ChatReportRequest:
                    return await HandleChatReportAsync(message);
                default:
                case MessageType.ChatSend:
                    return await HandleChatSendAsync(message);
            }
        }

        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleChatSendAsync(HorizonMessagePacket message)
        {
            try
            {
                var req = message.Body as ChatSendMessage;
                if (req == null)
                    return Fail("聊天消息非法");

                var content = (req.Content ?? string.Empty).Trim();
                if (content.Length == 0)
                    return Fail("消息内容为空");
                if (content.Length > MaxContentLength)
                    content = content.Substring(0, MaxContentLength);

                if (!IsSendableChannel(req.Channel))
                    return Fail("该频道不可发送");
                if (req.Channel == ChatChannel.Private && req.TargetId == 0)
                    return Fail("密聊目标为空");

                long nowTicks = DateTime.UtcNow.Ticks;
                ulong receiverId = req.Channel == ChatChannel.Private ? req.TargetId : 0;

                // 服务端敏感词过滤：命中则仅留档（Status=2 已屏蔽），不推送、不回显——任何客户端（含发送方）都不显示。
                bool hasSensitive = SensitiveWordFilter.Contains(content);

                var grain = _clusterClient.GetGrain<IChatGrain>((int)req.Channel);
                long messageId = await grain.AppendAsync(new ChatAppendRequest
                {
                    SenderId = req.SenderId,
                    SenderName = req.SenderName ?? string.Empty,
                    SenderLevel = 0,
                    ReceiverId = receiverId,
                    Content = content,
                    ClientMsgId = req.ClientMsgId ?? string.Empty,
                    ServerId = 0,
                    Timestamp = nowTicks,
                    HasSensitive = hasSensitive,
                    FilteredContent = hasSensitive ? SensitiveWordFilter.Mask(content) : content,
                });

                if (hasSensitive)
                {
                    Logger.LogInformation("[ChatHandler] 拦截敏感词发言: channel={Channel}, sender={Sender}",
                        req.Channel, req.SenderId);
                    return Fail("发言包含敏感词，已被屏蔽");
                }

                var notify = new ChatNotifyMessage
                {
                    SenderId = req.SenderId,
                    SenderName = req.SenderName ?? string.Empty,
                    Channel = req.Channel,
                    Content = content,
                    Timestamp = nowTicks,
                    ChatMessageId = messageId > 0 ? messageId.ToString() : string.Empty,
                    IsSystemMessage = req.Channel == ChatChannel.System,
                    ReceiverId = receiverId,
                };

                var targets = await ResolveAudienceAsync(req.Channel, req.SenderId, req.TargetId);
                if (targets.Count > 0)
                    await _pushService.PushAsync(notify, targets);

                // 回执：发送者本地据此即时上屏；广播已排除发送者，避免重复。
                return (true, CreateHorizonMessage(notify));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理聊天消息失败");
                return Fail("处理聊天消息失败");
            }
        }

        /// <summary>
        /// 收件人解析（不含发送者）。
        /// World → 全部在线；Nearby → 同分片水平半径内实体；Private/System → 指定目标；
        /// Sect/Party/Raid → 占位：服务端暂无门派/队伍名册实现（GuildGrain/TeamGrain 仅有接口无实现），
        /// 因而无法解析远端收件人（仅发送者回执上屏）。
        /// </summary>
        private async Task<IReadOnlyCollection<ulong>> ResolveAudienceAsync(ChatChannel channel, ulong senderId, ulong targetId)
        {
            switch (channel)
            {
                case ChatChannel.World:
                    var online = await _pushService.GetOnlineCharacterIdsAsync();
                    return online.Where(id => id != senderId).ToList();

                case ChatChannel.Nearby:
                    return await ResolveNearbyAsync(senderId);

                case ChatChannel.Private:
                case ChatChannel.System:
                    return targetId != 0 && targetId != senderId
                        ? new List<ulong> { targetId }
                        : new List<ulong>();

                default:
                    return Array.Empty<ulong>();
            }
        }

        /// <summary>解析「附近」收件人：同分片内水平半径内的其它实体（分片位置；玩家 entityId==characterId）。</summary>
        private async Task<IReadOnlyCollection<ulong>> ResolveNearbyAsync(ulong senderId)
        {
            try
            {
                long shardId = _shardRouter.Resolve((long)senderId);
                var shard = _clusterClient.GetGrain<IZoneShardGrain>(shardId);
                var ids = await shard.GetNearbyEntityIdsAsync(senderId, NearbyRadius, NearbyMaxRecipients);
                return ids.Where(id => id != senderId).ToList();
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "[ChatHandler] 附近频道收件人解析失败，降级为仅发送者回执");
                return Array.Empty<ulong>();
            }
        }

        private static bool IsSendableChannel(ChatChannel channel)
            => channel != ChatChannel.System;   // 系统频道仅由服务端写入

        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleChatHistoryAsync(HorizonMessagePacket message)
        {
            try
            {
                var req = message.Body as ChatHistoryRequest;
                if (req == null)
                    return Fail("聊天历史请求非法");

                int count = req.Count > 0 ? req.Count : 50;
                var grain = _clusterClient.GetGrain<IChatGrain>((int)req.ChannelType);
                var list = await grain.GetHistoryAsync(req.CharacterId, req.PeerId, req.BeforeMessageId, count);

                var response = new ChatHistoryResponse
                {
                    Messages = list.Select(ToChatMessage).ToList(),
                    HasMore = list.Count >= count,
                };
                return (true, CreateHorizonMessage(response));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理聊天历史消息失败");
                return Fail("处理聊天历史消息失败");
            }
        }

        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleChatRecallAsync(HorizonMessagePacket message)
        {
            try
            {
                var req = message.Body as ChatRecallRequest;
                if (req == null) return Fail("撤回请求非法");
                if (req.MessageId <= 0) return Fail("撤回消息ID非法");

                var grain = _clusterClient.GetGrain<IChatGrain>((int)req.Channel);
                bool ok = await grain.RecallAsync(req.MessageId, req.SenderId);
                if (!ok) return Fail("撤回失败（无权或消息不可撤回）");

                var notify = new ChatRecallNotify
                {
                    Channel = req.Channel,
                    MessageId = req.MessageId,
                    SenderId = req.SenderId,
                    ReceiverId = req.ReceiverId,
                };

                // 广播给频道收件人；撤回者经回执在本地移除（两处都由该通知驱动）
                var targets = await ResolveAudienceAsync(req.Channel, req.SenderId, req.ReceiverId);
                if (targets.Count > 0)
                    await _pushService.PushAsync(notify, targets);

                return (true, CreateHorizonMessage(notify));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理消息撤回失败");
                return Fail("处理消息撤回失败");
            }
        }

        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleChatReportAsync(HorizonMessagePacket message)
        {
            try
            {
                var req = message.Body as ChatReportRequest;
                if (req == null) return Fail("举报请求非法");
                if (req.MessageId <= 0 || req.ReporterId == 0) return Fail("举报参数非法");

                var grain = _clusterClient.GetGrain<IChatGrain>((int)req.Channel);
                long reportId = await grain.ReportAsync(req.MessageId, req.ReporterId, req.Reason ?? string.Empty);
                if (reportId <= 0) return Fail("举报提交失败");

                Logger.LogInformation("[ChatHandler] 举报已记录: reportId={ReportId}, messageId={MessageId}, reporter={Reporter}",
                    reportId, req.MessageId, req.ReporterId);

                // 仅回执给举报者，不广播（避免暴露举报行为）
                return (true, CreateHorizonMessage(req));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理聊天举报失败");
                return Fail("处理聊天举报失败");
            }
        }

        private static ChatMessage ToChatMessage(ChatNotifyMessage n) => new ChatMessage
        {
            SenderId = n.SenderId,
            SenderName = n.SenderName,
            ReceiverId = n.ReceiverId,
            ChannelType = n.Channel,
            Content = n.Content,
            Timestamp = n.Timestamp,
            MessageId = n.ChatMessageId,
            IsSystemMessage = n.IsSystemMessage,
        };

        private (bool IsSuccess, HorizonMessagePacket MessagePacket) Fail(string reason)
            => (false, CreateHorizonMessage(new ErrorMessage { ErrorCode = 500, Message = reason }));
    }
}
