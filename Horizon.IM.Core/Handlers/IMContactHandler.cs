using Horizon.IM.Core.Adapters;
using Horizon.IM.Message.Enums;
using Horizon.IM.Message.Network;

using Microsoft.Extensions.Logging;

using Orleans;

namespace Horizon.IM.Core.Handlers;

public class IMContactHandler : IMMessageHandlerBase
{
    public IMContactHandler(
        ILogger<IMMessageHandlerBase> logger,
        IClusterClient clusterClient,
        IMMessageAdapter adapter)
        : base(logger, clusterClient, adapter)
    {
    }

    public override List<IMMessageType> MessageTypes { get; } = new()
    {
        IMMessageType.ContactAddRequest,
        IMMessageType.ContactRequestHandleRequest,
        IMMessageType.ContactPendingListRequest,
        IMMessageType.ContactRemoveRequest,
        IMMessageType.ContactBlockRequest,
        IMMessageType.ContactListRequest,
        IMMessageType.ContactSearchRequest,
        IMMessageType.ContactProfileBroadcastRequest,
        IMMessageType.ContactGroupUpdateRequest,
    };

    public override IMServiceType ServiceType => IMServiceType.Contact;

    public override async Task<(bool IsSuccess, IMMessagePacket? MessagePacket)> RouteHandlerAsync(IMMessagePacket message)
    {
        switch (message.Header.MessageType)
        {
            case IMMessageType.ContactAddRequest:
                return await HandleAddAsync(message).ConfigureAwait(false);
            case IMMessageType.ContactRequestHandleRequest:
                return await HandleRequestAsync(message).ConfigureAwait(false);
            case IMMessageType.ContactPendingListRequest:
                return await HandlePendingListAsync(message).ConfigureAwait(false);
            case IMMessageType.ContactRemoveRequest:
                return await HandleRemoveAsync(message).ConfigureAwait(false);
            case IMMessageType.ContactBlockRequest:
                return await HandleBlockAsync(message).ConfigureAwait(false);
            case IMMessageType.ContactListRequest:
                return await HandleListAsync(message).ConfigureAwait(false);
            case IMMessageType.ContactSearchRequest:
                return await HandleSearchAsync(message).ConfigureAwait(false);
            case IMMessageType.ContactProfileBroadcastRequest:
                return await HandleProfileBroadcastAsync(message).ConfigureAwait(false);
            case IMMessageType.ContactGroupUpdateRequest:
                return await HandleGroupUpdateAsync(message).ConfigureAwait(false);
            default:
                return (false, CreateErrorPacket(message, IMErrorCode.Unknown, "不支持的联系人消息类型"));
        }
    }

    private async Task<(bool, IMMessagePacket?)> HandleAddAsync(IMMessagePacket packet)
    {
        if (packet.Body is not IMContactAddRequest request)
        {
            return (false, CreateErrorPacket(packet, IMErrorCode.Unknown, "无效的添加联系人请求"));
        }

        var response = await GetUserGrain(request.UserId).AddContactAsync(request).ConfigureAwait(false);
        return (true, CreateResponsePacket(packet, response));
    }

    private async Task<(bool, IMMessagePacket?)> HandleRequestAsync(IMMessagePacket packet)
    {
        if (packet.Body is not IMContactRequestHandleRequest request)
        {
            return (false, CreateErrorPacket(packet, IMErrorCode.Unknown, "无效的处理好友申请请求"));
        }

        var success = await GetUserGrain(request.UserId)
            .HandleContactRequestAsync(request.RequesterId, request.Accept)
            .ConfigureAwait(false);

        IMContactInfo? newContact = null;
        if (success && request.Accept)
        {
            newContact = await GetUserGrain(request.UserId)
                .GetContactInfoAsync(request.RequesterId)
                .ConfigureAwait(false);
        }

        var response = new IMContactRequestHandleResponse
        {
            Success = success,
            Message = success
                ? request.Accept ? "好友申请已接受" : "好友申请已拒绝"
                : "处理好友申请失败",
            RequesterId = request.RequesterId,
            Accepted = request.Accept,
            NewContact = newContact
        };

        return (true, CreateResponsePacket(packet, response));
    }

    private async Task<(bool, IMMessagePacket?)> HandleRemoveAsync(IMMessagePacket packet)
    {
        if (packet.Body is not IMContactRemoveRequest request)
        {
            return (false, CreateErrorPacket(packet, IMErrorCode.Unknown, "无效的删除联系人请求"));
        }

        var response = await GetUserGrain(request.UserId).RemoveContactAsync(request).ConfigureAwait(false);
        return (true, CreateResponsePacket(packet, response));
    }

    private async Task<(bool, IMMessagePacket?)> HandlePendingListAsync(IMMessagePacket packet)
    {
        if (packet.Body is not IMPendingContactRequestListRequest request)
        {
            return (false, CreateErrorPacket(packet, IMErrorCode.Unknown, "无效的待处理好友申请列表请求"));
        }

        var response = await GetUserGrain(request.UserId).GetPendingContactRequestsAsync(request).ConfigureAwait(false);
        return (true, CreateResponsePacket(packet, response));
    }

    private async Task<(bool, IMMessagePacket?)> HandleBlockAsync(IMMessagePacket packet)
    {
        if (packet.Body is not IMContactBlockRequest request)
        {
            return (false, CreateErrorPacket(packet, IMErrorCode.Unknown, "无效的屏蔽联系人请求"));
        }

        var response = await GetUserGrain(request.UserId).BlockContactAsync(request).ConfigureAwait(false);
        return (true, CreateResponsePacket(packet, response));
    }

    private async Task<(bool, IMMessagePacket?)> HandleListAsync(IMMessagePacket packet)
    {
        if (packet.Body is not IMContactListRequest request)
        {
            return (false, CreateErrorPacket(packet, IMErrorCode.Unknown, "无效的联系人列表请求"));
        }

        var response = await GetUserGrain(request.UserId).GetContactListAsync(request).ConfigureAwait(false);
        return (true, CreateResponsePacket(packet, response));
    }

    private async Task<(bool, IMMessagePacket?)> HandleSearchAsync(IMMessagePacket packet)
    {
        if (packet.Body is not IMContactSearchRequest request)
        {
            return (false, CreateErrorPacket(packet, IMErrorCode.Unknown, "无效的联系人搜索请求"));
        }

        var response = await GetUserGrain(request.UserId).SearchContactAsync(request).ConfigureAwait(false);
        return (true, CreateResponsePacket(packet, response));
    }

    private async Task<(bool, IMMessagePacket?)> HandleProfileBroadcastAsync(IMMessagePacket packet)
    {
        if (packet.Body is not IMContactProfileBroadcastRequest request)
        {
            return (false, CreateErrorPacket(packet, IMErrorCode.Unknown, "无效的资料广播请求"));
        }

        // 防止客户端伪造请求体中的 UserId，冒充其他用户广播资料变更
        if (packet.Header.UserId != request.UserId)
        {
            return (false, CreateErrorPacket(packet, IMErrorCode.Unknown, "无权广播其他用户的资料变更"));
        }

        await GetUserGrain(request.UserId)
            .BroadcastProfileUpdateAsync(request.Nickname, request.Avatar, request.Bio)
            .ConfigureAwait(false);

        // 此消息为单向广播，不需要向发起方返回响应
        return (true, null);
    }

    private async Task<(bool, IMMessagePacket?)> HandleGroupUpdateAsync(IMMessagePacket packet)
    {
        if (packet.Body is not IMContactGroupUpdateRequest request)
        {
            return (false, CreateErrorPacket(packet, IMErrorCode.Unknown, "无效的分组更新请求"));
        }

        var response = await GetUserGrain(request.UserId).UpdateContactGroupAsync(request).ConfigureAwait(false);
        return (true, CreateResponsePacket(packet, response));
    }
}