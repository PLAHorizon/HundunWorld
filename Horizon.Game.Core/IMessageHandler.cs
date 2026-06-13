using Horizon.Game.Message;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using K4os.Compression.LZ4;
using MemoryPack;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Logging;
using TouchSocket.Sockets;

namespace Horizon.Game.Core;

/// <summary>
/// 消息处理器接口
/// </summary>
public interface IMessageHandler
{
    IClusterClient OrleansClient { get; }
    ITcpSessionClient GameClient { get; }
    /// <summary>
    /// 获取处理器支持的消息类型
    /// </summary>
    List<MessageType> MessageTypes { get; }

    /// <summary>
    /// 获取处理器支持的服务类型
    /// </summary>
    ServiceType ServiceType { get; }
    /// <summary>
    /// 静默检查处理器是否能处理该消息（不输出日志，用于路由分发）
    /// </summary>
    bool CanHandle(HorizonMessagePacket message);
    /// <summary>
    /// 验证消息（会输出日志，仅对已匹配的处理器调用）
    /// </summary>
    /// <param name="message"></param>
    /// <returns></returns>
    bool ValidateMessage(HorizonMessagePacket message);
    /// <summary>
    /// 处理消息
    /// </summary>
    /// <param name="message">消息对象</param>
    /// <returns>处理结果</returns>
    Task<(bool IsSuccess, MessageUnion? Response)> HandleAsync(ITcpSessionClient client, HorizonMessagePacket message); // Changed to MessageUnion?
    /// <summary>
    /// 消息处理器内部路由处理
    /// </summary>
    /// <param name="message">网络消息</param>
    /// <returns>消息处理结果</returns>
    Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> RouteHandlerAsync(HorizonMessagePacket message);
    HorizonMessagePacket CreateHorizonMessage<T>(T message) where T : MessageUnion, INetworkMessage;
}

/// <summary>
/// 抽象消息处理器基类
/// </summary>
public abstract class MessageHandlerBase : IMessageHandler
{
    protected readonly ILogger<MessageHandlerBase> Logger;
    protected readonly IClusterClient _clusterClient;
    protected readonly HorizonMessageAdapter _adapter;
    protected MessageHandlerBase(ILogger<MessageHandlerBase> logger, IClusterClient clusterClient, HorizonMessageAdapter adapter)
    {
        Logger = logger;
        _clusterClient = clusterClient;
        _adapter = adapter;
    }

    public abstract List<MessageType> MessageTypes { get; }

    public abstract ServiceType ServiceType { get; }
    public IClusterClient OrleansClient => _clusterClient;

    public ITcpSessionClient GameClient => _gameClient;
    public ITcpSessionClient _gameClient;

    public virtual async Task<(bool IsSuccess, MessageUnion? Response)> HandleAsync(ITcpSessionClient client, HorizonMessagePacket message)
    {
        _gameClient = client;
        try
        {
            (bool IsSuccess, HorizonMessagePacket tem) = await RouteHandlerAsync(message);
            if (tem == null)
            {
                return (IsSuccess, null);
            }

            PrepareResponsePacket(tem, message);
            var buff = _adapter.PackPacket(tem);
            await client.SendAsync(buff);
            return (IsSuccess, tem.Body);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex,
                "处理消息失败。ServiceType={ServiceType}, MessageType={MessageType}, MessageId={MessageId}",
                ServiceType,
                message.Header.MessageType,
                message.Header.MessageId);

            try
            {
                var errorPacket = CreateUnhandledErrorResponse(message, ex);
                var errorBuffer = _adapter.PackPacket(errorPacket);
                await client.SendAsync(errorBuffer);
                return (false, errorPacket.Body);
            }
            catch (Exception responseEx)
            {
                Logger.LogError(responseEx,
                    "发送异常回退响应失败。ServiceType={ServiceType}, MessageType={MessageType}, MessageId={MessageId}",
                    ServiceType,
                    message.Header.MessageType,
                    message.Header.MessageId);
                return (false, null);
            }
        }
    }

    private static void PrepareResponsePacket(HorizonMessagePacket responsePacket, HorizonMessagePacket request)
    {
        responsePacket.Header.GameId = request.Header.GameId;
        responsePacket.Header.ZoneId = request.Header.ZoneId;
        responsePacket.Header.ServerId = request.Header.ServerId;
        responsePacket.Header.UserId = request.Header.UserId;
        responsePacket.Header.AuthToken = request.Header.AuthToken;
        responsePacket.Header.MachineId = request.Header.MachineId;
        responsePacket.Header.IsResponse = true;
        responsePacket.Header.ResponseToMessageId = request.Header.MessageId;
        responsePacket.Header.RequireResponse = false;
    }

    private HorizonMessagePacket CreateUnhandledErrorResponse(HorizonMessagePacket request, Exception ex)
    {
        var errorResponse = new ErrorMessage
        {
            ErrorCode = 500,
            Message = "服务器处理请求时发生异常",
            Details = ex.Message,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            RelatedMessageId = request.Header.MessageId,
            ShouldRetry = false,
            RetryCount = 0,
        };

        var errorPacket = CreateHorizonMessage(errorResponse);
        PrepareResponsePacket(errorPacket, request);
        return errorPacket;
    }

    public abstract Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> RouteHandlerAsync(HorizonMessagePacket message);
    /// <summary>
    /// 静默检查处理器是否能处理该消息（不输出日志，用于路由分发）
    /// </summary>
    public virtual bool CanHandle(HorizonMessagePacket message)
    {
        return message != null
            && MessageTypes.Contains(message.Header.MessageType)
            && message.ServiceType == ServiceType;
    }
    /// <summary>
    /// 验证消息（会输出日志，仅对已匹配的处理器调用）
    /// </summary>
    public virtual bool ValidateMessage(HorizonMessagePacket message)
    {
        if (message == null)
        {
            Logger.LogError("消息对象为空");
            return false;
        }

        if (!MessageTypes.Contains(message.Header.MessageType))
        {
            Logger.LogError($"消息类型无效。期望: [{string.Join(", ", MessageTypes)}], 实际: {message.Header.MessageType}");
            return false;
        }

        if (message.ServiceType != ServiceType)
        {
            Logger.LogError($"服务类型无效。期望: {ServiceType}, 实际: {message.ServiceType}");
            return false;
        }

        return true;
    }

    /// <summary>
    /// 生成网络消息包
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="message"></param>
    /// <returns></returns>
    public HorizonMessagePacket CreateHorizonMessage<T>(T message) where T : MessageUnion, INetworkMessage
    {
        if (message == null)
        {
            throw new ArgumentNullException(nameof(message), "网络消息不能为空");
        }

        // 创建默认的消息头
        var header = new MessageHeader
        {
            MessageType = ((INetworkMessage)message).Type,
            ServiceType = ((INetworkMessage)message).ServiceType,
            IsResponse = false,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            GameId = 1,
            ZoneId = 1,
            ServerId = 1,
        };

        HorizonMessagePacket messagePacket = new HorizonMessagePacket
        {
            Header = header,
            ServiceType = ((INetworkMessage)message).ServiceType,
            Body = message,
            RawData = MemoryPackSerializer.Serialize(message),
        };

        messagePacket.Header.SequenceId = CRC32.Compute(messagePacket.RawData);
        return messagePacket;
    }

}
