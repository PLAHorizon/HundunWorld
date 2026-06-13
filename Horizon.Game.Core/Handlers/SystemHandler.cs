using Horizon.Game.Message;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using Microsoft.Extensions.Logging;
using Orleans;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TouchSocket.Sockets;

namespace Horizon.Game.Core.Handlers
{
    /// <summary>
    /// 系统消息处理器
    /// 处理系统级消息，如心跳、错误等
    /// </summary>
    public class SystemHandler : MessageHandlerBase
    {
        public SystemHandler(ILogger<MessageHandlerBase> logger, IClusterClient clusterClient, HorizonMessageAdapter adapter) : base(logger, clusterClient, adapter)
        {
        }

        public override List<MessageType> MessageTypes => new()
        {
            MessageType.Heartbeat,
            MessageType.HeartbeatResponse,
            MessageType.Error,
            MessageType.System
        };

        public override ServiceType ServiceType => ServiceType.System ;

        public override async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> RouteHandlerAsync(HorizonMessagePacket message)
        {
            try
            {
                Logger.LogInformation("处理系统消息: {MessageType}", message.Header.MessageType);

                // 根据消息类型处理不同的系统消息
                switch (message.Header.MessageType)
                {
                    case MessageType.Heartbeat:
                        return await HandleHeartbeatRequestAsync(message);

                    case MessageType.HeartbeatResponse:
                        // 网关收到客户端的心跳响应，无需再次响应，直接返回成功
                        Logger.LogDebug("收到客户端心跳响应");
                        return (true, null);

                    case MessageType.Error:
                        return await HandleErrorMessageAsync(message);
                    case MessageType.System:
                        return await HandleSystemMessageAsync(message);
                    default:
                        Logger.LogWarning("不支持的系统消息类型: {MessageType}", message.Header.MessageType);
                        return (false, message);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理系统消息时发生错误");
                return (false, message);
            }
        }

        /// <summary>
        /// 处理心跳请求消息
        /// </summary>
        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleHeartbeatRequestAsync(HorizonMessagePacket request)
        {
            try
            {
                var heartbeatMessage = request.Body as HeartbeatMessage;
                if (heartbeatMessage == null)
                {
                    Logger.LogWarning("心跳消息体为空或类型不正确");
                    return (false, request);
                }

                // 创建心跳响应消息
                var response = new HeartbeatResponse
                {
                    Timestamp = DateTimeOffset.Now.ToUnixTimeMilliseconds(),
                    ServerTime = DateTimeOffset.Now.ToUnixTimeMilliseconds(),
                    Latency = DateTimeOffset.Now.ToUnixTimeMilliseconds() - heartbeatMessage.ClientTime
                };

                // 创建响应消息包
                var responsePacket = CreateHorizonMessage(response);

                // 设置响应消息的头部信息，保持与请求一致的GameId, ZoneId, ServerId
                responsePacket.Header.GameId = request.Header.GameId;
                responsePacket.Header.ZoneId = request.Header.ZoneId;
                responsePacket.Header.ServerId = request.Header.ServerId;
                responsePacket.Header.ResponseToMessageId = request.Header.MessageId;
                responsePacket.Header.IsResponse = true;
                responsePacket.Header.RequireResponse = false;

                Logger.LogDebug("心跳响应已创建: ServerTime={ServerTime}, Latency={Latency}ms",
                    response.ServerTime, response.Latency);

                return (true, responsePacket);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理心跳请求时发生错误");
                return (false, request);
            }
        }

        /// <summary>
        /// 处理心跳响应消息
        /// </summary>
        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleHeartbeatResponseAsync(HorizonMessagePacket response)
        {
            try
            {
                var heartbeatResponse = response.Body as HeartbeatResponse;
                if (heartbeatResponse == null)
                {
                    Logger.LogWarning("心跳响应消息体为空或类型不正确");
                    return (false, response);
                }

                // 记录心跳响应信息
                Logger.LogDebug("收到心跳响应: ServerTime={ServerTime}, Latency={Latency}ms",
                    heartbeatResponse.ServerTime, heartbeatResponse.Latency);

                // 心跳响应通常不需要进一步处理，直接返回成功
                // 但我们需要确保响应消息的头部字段正确
                response.Header.GameId = response.Header.GameId > 0 ? response.Header.GameId : (uint)1;
                response.Header.ZoneId = response.Header.ZoneId > 0 ? response.Header.ZoneId : (uint)1;
                response.Header.ServerId = response.Header.ServerId > 0 ? response.Header.ServerId : (uint)1;

                return (true, response);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理心跳响应时发生错误");
                return (false, response);
            }
        }

        /// <summary>
        /// 处理错误消息
        /// </summary>
        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleErrorMessageAsync(HorizonMessagePacket message)
        {
            try
            {
                var errorMessage = message.Body as ErrorMessage;
                if (errorMessage == null)
                {
                    Logger.LogWarning("错误消息体为空或类型不正确");
                    return (false, message);
                }

                // 记录错误信息
                Logger.LogError("收到错误消息: Code={ErrorCode}, Message={Message}",
                    errorMessage.ErrorCode, errorMessage.Message);

                return (true, message);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理错误消息时发生错误");
                return (false, message);
            }
        }

        /// <summary>
        /// 处理系统消息
        /// </summary>
        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleSystemMessageAsync(HorizonMessagePacket message)
        {
            try
            {
                var systemMessage = message.Body as SystemMessage;
                if (systemMessage == null)
                {
                    Logger.LogWarning("系统消息体为空或类型不正确");
                    return (false, message);
                }

                // 记录系统消息
                Logger.LogInformation("收到系统消息: {Content}", systemMessage.Content);

                return (true, message);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理系统消息时发生错误");
                return (false, message);
            }
        }
    }
}