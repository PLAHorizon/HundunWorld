using Horizon.IM.Core.Adapters;
using Horizon.IM.Message.Enums;
using Horizon.IM.Message.Network;

using Microsoft.Extensions.Logging;

using Orleans;

namespace Horizon.IM.Core.Handlers;

public class IMChatHandler : IMMessageHandlerBase
{
    public IMChatHandler(
        ILogger<IMMessageHandlerBase> logger,
        IClusterClient clusterClient,
        IMMessageAdapter adapter)
        : base(logger, clusterClient, adapter)
    {
    }

    public override List<IMMessageType> MessageTypes { get; } = new()
    {
        IMMessageType.PrivateChatSend,
        IMMessageType.ChatAck,
        IMMessageType.ChatRecall,
        IMMessageType.ChatReadReceipt,
        IMMessageType.StrangerChatRequest,
        IMMessageType.StrangerChatSend,
        IMMessageType.ConversationListRequest,
        IMMessageType.ConversationDelete,
        IMMessageType.ConversationPin,
        IMMessageType.ConversationMute,
        IMMessageType.ChatHistoryQuery,
        IMMessageType.ChatHistoryClear
    };

    public override IMServiceType ServiceType => IMServiceType.Chat;

    public override async Task<(bool IsSuccess, IMMessagePacket? MessagePacket)> RouteHandlerAsync(IMMessagePacket message)
    {
        switch (message.Header.MessageType)
        {
            case IMMessageType.PrivateChatSend:
                return await HandlePrivateChatAsync(message).ConfigureAwait(false);
            case IMMessageType.ChatAck:
                return await HandleAckAsync(message).ConfigureAwait(false);
            case IMMessageType.ChatRecall:
                return await HandleRecallAsync(message).ConfigureAwait(false);
            case IMMessageType.ChatReadReceipt:
                return await HandleReadReceiptAsync(message).ConfigureAwait(false);
            case IMMessageType.StrangerChatRequest:
                return await HandleStrangerRequestAsync(message).ConfigureAwait(false);
            case IMMessageType.StrangerChatSend:
                return await HandleStrangerSendAsync(message).ConfigureAwait(false);
            case IMMessageType.ConversationListRequest:
                return await HandleConversationListAsync(message).ConfigureAwait(false);
            case IMMessageType.ConversationDelete:
                return await HandleConversationDeleteAsync(message).ConfigureAwait(false);
            case IMMessageType.ConversationPin:
                return await HandleConversationPinAsync(message).ConfigureAwait(false);
            case IMMessageType.ConversationMute:
                return await HandleConversationMuteAsync(message).ConfigureAwait(false);
            case IMMessageType.ChatHistoryQuery:
                return await HandleChatHistoryQueryAsync(message).ConfigureAwait(false);
            case IMMessageType.ChatHistoryClear:
                return await HandleChatHistoryClearAsync(message).ConfigureAwait(false);
            default:
                return (false, CreateErrorPacket(message, IMErrorCode.Unknown, "不支持的聊天消息类型"));
        }
    }

    private async Task<(bool, IMMessagePacket?)> HandlePrivateChatAsync(IMMessagePacket packet)
    {
        if (packet.Body is not IMPrivateChatSendMessage request)
        {
            return (false, CreateErrorPacket(packet, IMErrorCode.Unknown, "无效的私聊消息"));
        }

        var serverMessageId = await GetUserGrain(request.SenderId).SendPrivateMessageAsync(request).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(serverMessageId))
        {
            return (false, CreateErrorPacket(packet, IMErrorCode.Unknown, "私聊消息发送失败"));
        }

        var ack = new IMChatAckMessage
        {
            AckedMessageId = serverMessageId,
            Status = IMMessageStatus.Sent,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            UserId = request.SenderId
        };

        return (true, CreateResponsePacket(packet, ack));
    }

    private async Task<(bool, IMMessagePacket?)> HandleAckAsync(IMMessagePacket packet)
    {
        if (packet.Body is not IMChatAckMessage request)
        {
            return (false, CreateErrorPacket(packet, IMErrorCode.Unknown, "无效的消息回执"));
        }

        var success = await GetUserGrain(request.UserId).ProcessChatAckAsync(request).ConfigureAwait(false);
        return success
            ? (true, null)
            : (false, CreateErrorPacket(packet, IMErrorCode.Unknown, "消息回执处理失败"));
    }

    private async Task<(bool, IMMessagePacket?)> HandleRecallAsync(IMMessagePacket packet)
    {
        if (packet.Body is not IMChatRecallMessage request)
        {
            return (false, CreateErrorPacket(packet, IMErrorCode.Unknown, "无效的撤回请求"));
        }

        var success = await GetUserGrain(request.UserId).RecallMessageAsync(request).ConfigureAwait(false);
        return success
            ? (true, null)
            : (false, CreateErrorPacket(packet, IMErrorCode.PermissionDenied, "撤回消息失败"));
    }

    private async Task<(bool, IMMessagePacket?)> HandleReadReceiptAsync(IMMessagePacket packet)
    {
        if (packet.Body is not IMChatReadReceiptMessage request)
        {
            return (false, CreateErrorPacket(packet, IMErrorCode.Unknown, "无效的已读回执"));
        }

        var success = await GetUserGrain(request.UserId).SendReadReceiptAsync(request).ConfigureAwait(false);
        return success
            ? (true, null)
            : (false, CreateErrorPacket(packet, IMErrorCode.Unknown, "已读回执处理失败"));
    }

    private async Task<(bool, IMMessagePacket?)> HandleStrangerRequestAsync(IMMessagePacket packet)
    {
        if (packet.Body is not IMStrangerChatRequest request)
        {
            return (false, CreateErrorPacket(packet, IMErrorCode.Unknown, "无效的陌生人聊天请求"));
        }

        var response = await GetUserGrain(request.SenderId).RequestStrangerChatAsync(request).ConfigureAwait(false);
        return (true, CreateResponsePacket(packet, response));
    }

    private async Task<(bool, IMMessagePacket?)> HandleStrangerSendAsync(IMMessagePacket packet)
    {
        if (packet.Body is not IMStrangerChatSendMessage request)
        {
            return (false, CreateErrorPacket(packet, IMErrorCode.Unknown, "无效的陌生人消息"));
        }

        var serverMessageId = await GetUserGrain(request.SenderId).SendStrangerMessageAsync(request).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(serverMessageId))
        {
            return (false, CreateErrorPacket(packet, IMErrorCode.Unknown, "陌生人消息发送失败"));
        }

        var ack = new IMChatAckMessage
        {
            AckedMessageId = serverMessageId,
            Status = IMMessageStatus.Sent,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            UserId = request.SenderId
        };

        return (true, CreateResponsePacket(packet, ack));
    }

    private async Task<(bool, IMMessagePacket?)> HandleConversationListAsync(IMMessagePacket packet)
    {
        if (packet.Body is not IMConversationListRequest request)
        {
            return (false, CreateErrorPacket(packet, IMErrorCode.Unknown, "无效的会话列表请求"));
        }

        var response = await GetUserGrain(request.UserId).GetConversationListAsync(request).ConfigureAwait(false);
        return (true, CreateResponsePacket(packet, response));
    }

    private async Task<(bool, IMMessagePacket?)> HandleConversationDeleteAsync(IMMessagePacket packet)
    {
        if (packet.Body is not IMConversationDeleteMessage request)
        {
            return (false, CreateErrorPacket(packet, IMErrorCode.Unknown, "无效的删除会话请求"));
        }

        var success = await GetUserGrain(request.UserId).DeleteConversationAsync(request).ConfigureAwait(false);
        return success
            ? (true, null)
            : (false, CreateErrorPacket(packet, IMErrorCode.Unknown, "删除会话失败"));
    }

    private async Task<(bool, IMMessagePacket?)> HandleConversationPinAsync(IMMessagePacket packet)
    {
        if (packet.Body is not IMConversationPinMessage request)
        {
            return (false, CreateErrorPacket(packet, IMErrorCode.Unknown, "无效的置顶会话请求"));
        }

        var success = await GetUserGrain(request.UserId).PinConversationAsync(request).ConfigureAwait(false);
        return success
            ? (true, null)
            : (false, CreateErrorPacket(packet, IMErrorCode.Unknown, "置顶会话失败"));
    }

    private async Task<(bool, IMMessagePacket?)> HandleConversationMuteAsync(IMMessagePacket packet)
    {
        if (packet.Body is not IMConversationMuteMessage request)
        {
            return (false, CreateErrorPacket(packet, IMErrorCode.Unknown, "无效的免打扰请求"));
        }

        var success = await GetUserGrain(request.UserId).MuteConversationAsync(request).ConfigureAwait(false);
        return success
            ? (true, null)
            : (false, CreateErrorPacket(packet, IMErrorCode.Unknown, "设置会话免打扰失败"));
    }

    private async Task<(bool, IMMessagePacket?)> HandleChatHistoryQueryAsync(IMMessagePacket packet)
    {
        if (packet.Body is not IMChatHistoryQueryRequest request)
        {
            return (false, CreateErrorPacket(packet, IMErrorCode.Unknown, "无效的聊天记录查询请求"));
        }

        if (request.ChatRelationType == IMChatRelationType.Group)
        {
            var count = request.Count > 0 ? request.Count : 20;
            var messages = await GetGroupGrain(request.PeerId)
                .GetGroupChatHistoryAsync(count, request.EndTime)
                .ConfigureAwait(false);

            var response = new IMChatHistoryQueryResponse
            {
                ConversationId = request.ConversationId,
                ChatRelationType = request.ChatRelationType,
                GroupMessages = messages,
                HasMore = messages.Count >= count
            };

            return (true, CreateResponsePacket(packet, response));
        }

        var userResponse = await GetUserGrain(request.UserId).QueryChatHistoryAsync(request).ConfigureAwait(false);
        return (true, CreateResponsePacket(packet, userResponse));
    }

    private async Task<(bool, IMMessagePacket?)> HandleChatHistoryClearAsync(IMMessagePacket packet)
    {
        if (packet.Body is not IMChatHistoryClearMessage request)
        {
            return (false, CreateErrorPacket(packet, IMErrorCode.Unknown, "无效的清空聊天记录请求"));
        }

        var success = await GetUserGrain(request.UserId).ClearChatHistoryAsync(request).ConfigureAwait(false);
        return success
            ? (true, null)
            : (false, CreateErrorPacket(packet, IMErrorCode.Unknown, "清空聊天记录失败"));
    }
}