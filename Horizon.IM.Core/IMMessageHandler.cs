using System.Net.Sockets;

using Horizon.IM.Core.Adapters;
using Horizon.IM.Message;
using Horizon.IM.Message.Enums;
using Horizon.IM.Message.Network;
using Horizon.Orleans.Interface;

using Microsoft.Extensions.Logging;

using Orleans;

using TouchSocket.Sockets;

namespace Horizon.IM.Core;

public interface IIMMessageHandler
{
    IClusterClient OrleansClient { get; }

    /// <summary>
    /// 已弃用。Handler 不再保留单个客户端引用，请改用 HandleAsync 的 client 参数。
    /// </summary>
    [Obsolete("Handler 不再保留单个客户端引用，请使用 HandleAsync 的 client 参数。")]
    ITcpSessionClient? CurrentClient { get; }

    List<IMMessageType> MessageTypes { get; }

    IMServiceType ServiceType { get; }

    bool ValidateMessage(IMMessagePacket message);

    Task<(bool IsSuccess, IMMessageUnion? Response)> HandleAsync(ITcpSessionClient client, IMMessagePacket message);

    Task<(bool IsSuccess, IMMessagePacket? MessagePacket)> RouteHandlerAsync(IMMessagePacket message);
}

public abstract class IMMessageHandlerBase : IIMMessageHandler
{
    protected readonly ILogger<IMMessageHandlerBase> Logger;
    protected readonly IClusterClient ClusterClient;
    protected readonly IMMessageAdapter Adapter;

    protected IMMessageHandlerBase(
        ILogger<IMMessageHandlerBase> logger,
        IClusterClient clusterClient,
        IMMessageAdapter adapter)
    {
        Logger = logger;
        ClusterClient = clusterClient;
        Adapter = adapter;
    }

    public abstract List<IMMessageType> MessageTypes { get; }

    public abstract IMServiceType ServiceType { get; }

    public IClusterClient OrleansClient => ClusterClient;

    /// <inheritdoc />
    [Obsolete("Handler 不再保留单个客户端引用，请使用 HandleAsync 的 client 参数。")]
    public ITcpSessionClient? CurrentClient => null;

    public virtual async Task<(bool IsSuccess, IMMessageUnion? Response)> HandleAsync(ITcpSessionClient client, IMMessagePacket message)
    {
        try
        {
            var (isSuccess, responsePacket) = await RouteHandlerAsync(message).ConfigureAwait(false);
            if (responsePacket != null)
            {
                await client.SendAsync(Adapter.PackPacket(responsePacket)).ConfigureAwait(false);
            }

            return (isSuccess, responsePacket?.Body);
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionAborted ||
                                         ex.SocketErrorCode == SocketError.ConnectionReset ||
                                         ex.SocketErrorCode == SocketError.NotConnected)
        {
            Logger.LogWarning(ex, "客户端已断开连接，跳过响应: ServiceType={ServiceType}, MessageType={MessageType}",
                message.ServiceType, message.Header.MessageType);
            return (false, null);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "处理 IM 消息失败: ServiceType={ServiceType}, MessageType={MessageType}",
                message.ServiceType, message.Header.MessageType);

            try
            {
                var errorPacket = CreateErrorPacket(message, IMErrorCode.Unknown, "处理 IM 消息失败", ex.Message);
                await client.SendAsync(Adapter.PackPacket(errorPacket)).ConfigureAwait(false);
                return (false, errorPacket.Body);
            }
            catch (SocketException)
            {
                Logger.LogWarning("发送错误响应时客户端已断开连接: ServiceType={ServiceType}, MessageType={MessageType}",
                    message.ServiceType, message.Header.MessageType);
                return (false, null);
            }
        }
    }

    public abstract Task<(bool IsSuccess, IMMessagePacket? MessagePacket)> RouteHandlerAsync(IMMessagePacket message);

    public virtual bool ValidateMessage(IMMessagePacket message)
    {
        if (message == null || message.Header == null || message.Body == null)
        {
            Logger.LogError("IM 消息为空或结构不完整");
            return false;
        }

        if (!MessageTypes.Contains(message.Header.MessageType))
        {
            Logger.LogError("IM 消息类型无效。期望: {Expected}, 实际: {Actual}", string.Join(", ", MessageTypes), message.Header.MessageType);
            return false;
        }

        if (message.ServiceType != ServiceType)
        {
            Logger.LogError("IM 服务类型无效。期望: {Expected}, 实际: {Actual}", ServiceType, message.ServiceType);
            return false;
        }

        return true;
    }

    protected IMMessagePacket CreateResponsePacket<T>(IMMessagePacket request, T response)
        where T : IMMessageUnion, IIMNetworkMessage
    {
        return Adapter.CreatePacket(
            response,
            request.Header.UserId,
            isResponse: true,
            responseToMessageId: request.Header.MessageId);
    }

    protected IMMessagePacket CreateErrorPacket(
        IMMessagePacket request,
        IMErrorCode errorCode,
        string message,
        string details = "")
    {
        var error = new IMErrorMessage
        {
            ErrorCode = errorCode,
            Message = message,
            RelatedMessageId = request.Header.MessageId,
            Details = details,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        return CreateResponsePacket(request, error);
    }

    protected IIMUserGrain GetUserGrain(ulong userId)
    {
        return ClusterClient.GetGrain<IIMUserGrain>(IMGrainKey.ToGuid(userId));
    }

    protected IIMGroupGrain GetGroupGrain(ulong groupId)
    {
        return ClusterClient.GetGrain<IIMGroupGrain>(IMGrainKey.ToGuid(groupId));
    }
}