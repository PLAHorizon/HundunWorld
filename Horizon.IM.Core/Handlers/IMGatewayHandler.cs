using Horizon.IM.Core.Adapters;
using Horizon.IM.Message.Enums;
using Horizon.IM.Message.Network;

using Microsoft.Extensions.Logging;

using Orleans;

namespace Horizon.IM.Core.Handlers;

public class IMGatewayHandler : IMMessageHandlerBase
{
    public IMGatewayHandler(
        ILogger<IMMessageHandlerBase> logger,
        IClusterClient clusterClient,
        IMMessageAdapter adapter)
        : base(logger, clusterClient, adapter)
    {
    }

    public override List<IMMessageType> MessageTypes { get; } = new()
    {
        IMMessageType.Heartbeat
    };

    public override IMServiceType ServiceType => IMServiceType.Gateway;

    public override Task<(bool IsSuccess, IMMessagePacket? MessagePacket)> RouteHandlerAsync(IMMessagePacket message)
    {
        if (message.Body is not IMHeartbeatMessage heartbeat)
        {
            return Task.FromResult<(bool, IMMessagePacket?)>((false, CreateErrorPacket(message, IMErrorCode.Unknown, "无效的心跳消息")));
        }

        var response = new IMHeartbeatResponse
        {
            ServerTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            PendingMessageCount = 0
        };

        return Task.FromResult<(bool, IMMessagePacket?)>((true, CreateResponsePacket(message, response)));
    }
}