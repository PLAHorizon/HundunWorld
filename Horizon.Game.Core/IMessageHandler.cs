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
    /// 验证消息和路由确认
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
            if (!IsSuccess || tem == null) return (IsSuccess, null);//无需响应客户端的请求
            tem.Header.GameId = message.Header.GameId;
            tem.Header.ZoneId = message.Header.ZoneId;
            tem.Header.ServerId = message.Header.ServerId;
            var buff = _adapter.PackMessage(tem.Body, tem.Header.MessageType);
            //HorizonMessageInfo horizonMessageInfo = new HorizonMessageInfo
            //{
            //    Packet = tem,
            //    Body = buff,
            //    BodyLength = buff.Length,
            //};
            await client.SendAsync(buff);
            return (IsSuccess, tem.Body);
        }
        catch (Exception ex)
        {
            return (false, null);
        }
    }

    public abstract Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> RouteHandlerAsync(HorizonMessagePacket message);
    /// <summary>
    /// 验证消息
    /// </summary>
    public virtual bool ValidateMessage(HorizonMessagePacket message)
    {
        if (message == null)
        {
            Logger.LogError("Message is null");
            return false;
        }

        if (!MessageTypes.Contains(message.Header.MessageType))
        {
            Logger.LogError($"Invalid message type. Expected: [{string.Join(", ", MessageTypes)}], Actual: {message.Header.MessageType}");
            return false;
        }

        if (message.ServiceType != ServiceType)
        {
            Logger.LogError($"Invalid service type. Expected: {ServiceType}, Actual: {message.ServiceType}");
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
            IsResponse = false, // 默认不是响应消息
            Timestamp = DateTime.UtcNow.Ticks,
            GameId = 1,    // 设置默认GameId
            ZoneId = 1,    // 设置默认ZoneId
            ServerId = 1,  // 设置默认ServerId
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
