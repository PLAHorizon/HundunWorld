using Horizon.IM.Message;
using Horizon.IM.Message.Network;

namespace Horizon.IM.Core;

public static class IMPacketUserResolver
{
    public static ulong ResolveUserId(IMMessagePacket packet)
    {
        if (packet == null)
        {
            return 0;
        }

        if (packet.Header?.UserId > 0)
        {
            return packet.Header.UserId;
        }

        return ResolveUserId(packet.Body);
    }

    public static ulong ResolveUserId(IMMessageUnion? body)
    {
        return body switch
        {
            IMHeartbeatMessage message => message.UserId,
            IMPrivateChatSendMessage message => message.SenderId,
            IMChatAckMessage message => message.UserId,
            IMChatRecallMessage message => message.UserId,
            IMChatReadReceiptMessage message => message.UserId,
            IMStrangerChatRequest message => message.SenderId,
            IMStrangerChatSendMessage message => message.SenderId,
            IMConversationListRequest message => message.UserId,
            IMConversationDeleteMessage message => message.UserId,
            IMConversationPinMessage message => message.UserId,
            IMConversationMuteMessage message => message.UserId,
            IMChatHistoryQueryRequest message => message.UserId,
            IMChatHistoryClearMessage message => message.UserId,
            IMContactAddRequest message => message.UserId,
            IMContactRequestHandleRequest message => message.UserId,
            IMPendingContactRequestListRequest message => message.UserId,
            IMContactRemoveRequest message => message.UserId,
            IMContactBlockRequest message => message.UserId,
            IMContactListRequest message => message.UserId,
            IMContactSearchRequest message => message.UserId,
            IMGroupChatSendMessage message => message.SenderId,
            IMGroupCreateRequest message => message.CreatorId,
            IMGroupJoinRequest message => message.UserId,
            IMGroupLeaveRequest message => message.UserId,
            IMGroupDisbandRequest message => message.OwnerId,
            IMGroupInfoUpdateMessage message => message.OperatorId,
            IMGroupMemberListRequest message => message.UserId,
            _ => 0
        };
    }
}