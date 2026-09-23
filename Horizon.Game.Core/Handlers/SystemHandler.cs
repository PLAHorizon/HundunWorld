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

        // 修复 BUG（心跳双处理器竞争路由）：MessageType.Heartbeat 由 Horizon.Game.Gateway 的
        // HeartbeatHandler 独占（刷新 Redis presence TTL + 离线角色防复活兜底 + 延迟回显）。
        // 此前此处同时认领 Heartbeat，依赖 DI 注册顺序决定路由——Core 程序集先注册使本处理器
        // 抢先处理心跳，HeartbeatHandler 的 presence 续期逻辑成为死代码，且两处延迟计算语义
        // 不一致（ticks vs Unix 毫秒）。此处移除 Heartbeat，仅保留响应/错误/系统消息。
        public override List<MessageType> MessageTypes => new()
        {
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