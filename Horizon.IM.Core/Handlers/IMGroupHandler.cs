using Horizon.IM.Core.Adapters;
using Horizon.IM.Message.Enums;
using Horizon.IM.Message.Network;

using Microsoft.Extensions.Logging;

using Orleans;

namespace Horizon.IM.Core.Handlers;

public class IMGroupHandler : IMMessageHandlerBase
{
    public IMGroupHandler(
        ILogger<IMMessageHandlerBase> logger,
        IClusterClient clusterClient,
        IMMessageAdapter adapter)
        : base(logger, clusterClient, adapter)
    {
    }

    public override List<IMMessageType> MessageTypes { get; } = new()
    {
        IMMessageType.GroupChatSend,
        IMMessageType.GroupCreateRequest,
        IMMessageType.GroupJoinRequest,
        IMMessageType.GroupLeaveRequest,
        IMMessageType.GroupDisbandRequest,
        IMMessageType.GroupInfoUpdate,
        IMMessageType.GroupMemberListRequest,
        IMMessageType.GroupInviteRequest,
        IMMessageType.GroupInviteResponse,
        IMMessageType.GroupJoinApplyReview,
        IMMessageType.GroupInviteApprovalReview,
        IMMessageType.GroupPendingInviteListRequest,
        IMMessageType.GroupPendingApprovalListRequest
    };

    public override IMServiceType ServiceType => IMServiceType.Group;

    public override async Task<(bool IsSuccess, IMMessagePacket? MessagePacket)> RouteHandlerAsync(IMMessagePacket message)
    {
        switch (message.Header.MessageType)
        {
            case IMMessageType.GroupChatSend:
                return await HandleSendGroupMessageAsync(message).ConfigureAwait(false);
            case IMMessageType.GroupCreateRequest:
                return await HandleCreateGroupAsync(message).ConfigureAwait(false);
            case IMMessageType.GroupJoinRequest:
                return await HandleJoinGroupAsync(message).ConfigureAwait(false);
            case IMMessageType.GroupLeaveRequest:
                return await HandleLeaveGroupAsync(message).ConfigureAwait(false);
            case IMMessageType.GroupDisbandRequest:
                return await HandleDisbandGroupAsync(message).ConfigureAwait(false);
            case IMMessageType.GroupInfoUpdate:
                return await HandleUpdateGroupInfoAsync(message).ConfigureAwait(false);
            case IMMessageType.GroupMemberListRequest:
                return await HandleMemberListAsync(message).ConfigureAwait(false);
            case IMMessageType.GroupInviteRequest:
                return await HandleInviteUserAsync(message).ConfigureAwait(false);
            case IMMessageType.GroupInviteResponse:
                return await HandleRespondToInviteAsync(message).ConfigureAwait(false);
            case IMMessageType.GroupJoinApplyReview:
                return await HandleReviewJoinApplicationAsync(message).ConfigureAwait(false);
            case IMMessageType.GroupInviteApprovalReview:
                return await HandleReviewInviteApprovalAsync(message).ConfigureAwait(false);
            case IMMessageType.GroupPendingInviteListRequest:
                return await HandleGetPendingGroupInvitesAsync(message).ConfigureAwait(false);
            case IMMessageType.GroupPendingApprovalListRequest:
                return await HandleGetPendingApprovalListAsync(message).ConfigureAwait(false);
            default:
                return (false, CreateErrorPacket(message, IMErrorCode.Unknown, "不支持的群组消息类型"));
        }
    }

    private async Task<(bool, IMMessagePacket?)> HandleSendGroupMessageAsync(IMMessagePacket packet)
    {
        if (packet.Body is not IMGroupChatSendMessage request)
        {
            return (false, CreateErrorPacket(packet, IMErrorCode.Unknown, "无效的群聊消息"));
        }

        var serverMessageId = await GetGroupGrain(request.GroupId).SendGroupMessageAsync(request).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(serverMessageId))
        {
            return (false, CreateErrorPacket(packet, IMErrorCode.GroupNotFound, "群聊消息发送失败"));
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

    private async Task<(bool, IMMessagePacket?)> HandleCreateGroupAsync(IMMessagePacket packet)
    {
        if (packet.Body is not IMGroupCreateRequest request)
        {
            return (false, CreateErrorPacket(packet, IMErrorCode.Unknown, "无效的创建群组请求"));
        }

        var groupId = IMGrainKey.NewUInt64Id();
        var grain = GetGroupGrain(groupId);
        var response = await grain.CreateGroupAsync(request).ConfigureAwait(false);

        response.GroupId = groupId;
        if (response.GroupInfo != null)
        {
            response.GroupInfo.GroupId = groupId;
        }

        return (true, CreateResponsePacket(packet, response));
    }

    private async Task<(bool, IMMessagePacket?)> HandleJoinGroupAsync(IMMessagePacket packet)
    {
        if (packet.Body is not IMGroupJoinRequest request)
        {
            return (false, CreateErrorPacket(packet, IMErrorCode.Unknown, "无效的加入群组请求"));
        }

        var response = await GetGroupGrain(request.GroupId).JoinGroupAsync(request).ConfigureAwait(false);
        return (true, CreateResponsePacket(packet, response));
    }

    private async Task<(bool, IMMessagePacket?)> HandleLeaveGroupAsync(IMMessagePacket packet)
    {
        if (packet.Body is not IMGroupLeaveRequest request)
        {
            return (false, CreateErrorPacket(packet, IMErrorCode.Unknown, "无效的退出群组请求"));
        }

        var response = await GetGroupGrain(request.GroupId).LeaveGroupAsync(request).ConfigureAwait(false);
        return (true, CreateResponsePacket(packet, response));
    }

    private async Task<(bool, IMMessagePacket?)> HandleDisbandGroupAsync(IMMessagePacket packet)
    {
        if (packet.Body is not IMGroupDisbandRequest request)
        {
            return (false, CreateErrorPacket(packet, IMErrorCode.Unknown, "无效的解散群组请求"));
        }

        var response = await GetGroupGrain(request.GroupId).DisbandGroupAsync(request).ConfigureAwait(false);
        return (true, CreateResponsePacket(packet, response));
    }

    private async Task<(bool, IMMessagePacket?)> HandleUpdateGroupInfoAsync(IMMessagePacket packet)
    {
        if (packet.Body is not IMGroupInfoUpdateMessage request)
        {
            return (false, CreateErrorPacket(packet, IMErrorCode.Unknown, "无效的更新群资料请求"));
        }

        var success = await GetGroupGrain(request.GroupId).UpdateGroupInfoAsync(request).ConfigureAwait(false);
        return success
            ? (true, null)
            : (false, CreateErrorPacket(packet, IMErrorCode.PermissionDenied, "更新群资料失败"));
    }

    private async Task<(bool, IMMessagePacket?)> HandleMemberListAsync(IMMessagePacket packet)
    {
        if (packet.Body is not IMGroupMemberListRequest request)
        {
            return (false, CreateErrorPacket(packet, IMErrorCode.Unknown, "无效的群成员列表请求"));
        }

        var response = await GetGroupGrain(request.GroupId).GetMemberListAsync(request).ConfigureAwait(false);
        return (true, CreateResponsePacket(packet, response));
    }

    private async Task<(bool, IMMessagePacket?)> HandleInviteUserAsync(IMMessagePacket packet)
    {
        if (packet.Body is not IMGroupInviteRequest request)
        {
            return (false, CreateErrorPacket(packet, IMErrorCode.Unknown, "无效的邀请入群请求"));
        }

        var response = await GetGroupGrain(request.GroupId).InviteUserAsync(request).ConfigureAwait(false);
        return (true, CreateResponsePacket(packet, response));
    }

    private async Task<(bool, IMMessagePacket?)> HandleRespondToInviteAsync(IMMessagePacket packet)
    {
        if (packet.Body is not IMGroupInviteResponse request)
        {
            return (false, CreateErrorPacket(packet, IMErrorCode.Unknown, "无效的入群邀请响应"));
        }

        var response = await GetGroupGrain(request.GroupId).RespondToInviteAsync(request).ConfigureAwait(false);
        return (true, CreateResponsePacket(packet, response));
    }

    private async Task<(bool, IMMessagePacket?)> HandleReviewJoinApplicationAsync(IMMessagePacket packet)
    {
        if (packet.Body is not IMGroupJoinApplyReview request)
        {
            return (false, CreateErrorPacket(packet, IMErrorCode.Unknown, "无效的审核加群申请请求"));
        }

        var response = await GetGroupGrain(request.GroupId).ReviewJoinApplicationAsync(request).ConfigureAwait(false);
        return (true, CreateResponsePacket(packet, response));
    }

    private async Task<(bool, IMMessagePacket?)> HandleReviewInviteApprovalAsync(IMMessagePacket packet)
    {
        if (packet.Body is not IMGroupInviteApprovalReview request)
        {
            return (false, CreateErrorPacket(packet, IMErrorCode.Unknown, "无效的审核入群邀请请求"));
        }

        var response = await GetGroupGrain(request.GroupId).ReviewInviteApprovalAsync(request).ConfigureAwait(false);
        return (true, CreateResponsePacket(packet, response));
    }

    private async Task<(bool, IMMessagePacket?)> HandleGetPendingGroupInvitesAsync(IMMessagePacket packet)
    {
        if (packet.Body is not IMGetPendingGroupInvitesRequest request)
        {
            return (false, CreateErrorPacket(packet, IMErrorCode.Unknown, "无效的待处理入群邀请列表请求"));
        }

        var response = await GetUserGrain(request.UserId).GetPendingGroupInvitesAsync(request).ConfigureAwait(false);
        return (true, CreateResponsePacket(packet, response));
    }

    private async Task<(bool, IMMessagePacket?)> HandleGetPendingApprovalListAsync(IMMessagePacket packet)
    {
        if (packet.Body is not IMGetPendingApprovalListRequest request)
        {
            return (false, CreateErrorPacket(packet, IMErrorCode.Unknown, "无效的待审批邀请列表请求"));
        }

        var pendingApprovals = await GetGroupGrain(request.GroupId)
            .GetPendingInviteApprovalsAsync().ConfigureAwait(false);

        var response = new IMGetPendingApprovalListResponse
        {
            PendingApprovals = pendingApprovals
        };
        return (true, CreateResponsePacket(packet, response));
    }
}