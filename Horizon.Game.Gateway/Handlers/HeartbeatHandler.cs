using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Horizon.Game.Core;
using Horizon.Game.Core.Sim.Server;
using Horizon.Game.Gateway.Services;
using Horizon.Game.Message;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using Microsoft.Extensions.Logging;
using Orleans;
using TouchSocket.Sockets;

namespace Horizon.Game.Gateway.Handlers
{
    /// <summary>
    /// 心跳消息处理器（双轨制架构的保活核心）。<br/>
    /// 处理客户端每 30 秒发送的 <see cref="HeartbeatMessage"/>，刷新 Redis presence key 的 TTL，
    /// 保持角色在线状态不过期，并返回 <see cref="HeartbeatResponse"/> 携带服务器时间和延迟。<br/>
    /// <para>
    /// <b>处理流程</b>：<br/>
    /// 1. 从 <c>MessageHeader.CharacterId</c> 提取角色 ID（心跳消息体本身不含 CharacterId）<br/>
    /// 2. 若 Header.CharacterId 为 0，从 <see cref="IConnectionManager"/> 反查连接绑定的角色<br/>
    /// 3. 调用 <see cref="ICharacterPresenceStore.RefreshHeartbeatAsync"/> 刷新 Redis presence TTL（90 秒）<br/>
    /// 4. 返回 <see cref="HeartbeatResponse"/>（ServerTime + Latency）<br/>
    /// </para>
    /// <para>
    /// <b>降级策略</b>：<see cref="ICharacterPresenceStore.RefreshHeartbeatAsync"/> 返回 false 有两种情况：<br/>
    /// 1. <c>presence key</c> 不存在（角色已通过 <c>GoOfflineAsync</c> 正确下线）—— 不重建 presence，避免已下线角色在 Redis 中"复活"；<br/>
    /// 2. Redis 不可用（连接故障降级）—— 通过 <see cref="IConnectionManager.GetConnectionByCharacterId"/> 二次确认连接仍存活后，重建 presence 作为兜底。<br/>
    /// 心跳响应始终正常返回（不影响客户端体验）。
    /// </para>
    /// <para>
    /// <b>TODO（安全级别心跳）</b>：角色处于交易、支付等高安全级别环节时，心跳需提升为每 5 秒检活，
    /// 不活则终止交易/支付流程并下发风险提示。待实际功能开发时完善此处的 SecurityLevel 分支。
    /// </para>
    /// </summary>
    public class HeartbeatHandler : MessageHandlerBase
    {
        private readonly ICharacterPresenceStore _presenceStore;
        private readonly IConnectionManager _connectionManager;

        public HeartbeatHandler(
            ILogger<MessageHandlerBase> logger,
            IClusterClient clusterClient,
            HorizonMessageAdapter adapter,
            ICharacterPresenceStore presenceStore,
            IConnectionManager connectionManager)
            : base(logger, clusterClient, adapter)
        {
            _presenceStore = presenceStore ?? throw new ArgumentNullException(nameof(presenceStore));
            _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
        }

        /// <inheritdoc />
        public override List<MessageType> MessageTypes => new()
        {
            MessageType.Heartbeat
        };

        /// <inheritdoc />
        public override ServiceType ServiceType => ServiceType.System;

        /// <inheritdoc />
        public override async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> RouteHandlerAsync(HorizonMessagePacket message)
        {
            try
            {
                // 反序列化心跳消息
                var heartbeat = message.Body as HeartbeatMessage;
                if (heartbeat == null && message.RawData != null && message.RawData.Length > 0)
                {
                    try
                    {
                        heartbeat = MemoryPack.MemoryPackSerializer.Deserialize<HeartbeatMessage>(message.RawData);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning(ex, "心跳消息反序列化失败，RawData 长度={Length}", message.RawData.Length);
                        heartbeat = new HeartbeatMessage();
                    }
                }
                heartbeat ??= new HeartbeatMessage();

                // 1. 提取 characterId（心跳消息体不含 CharacterId，依赖 MessageHeader）
                // MessageHeader.CharacterId 是 ulong，ICharacterPresenceStore 使用 long，需显式转换
                long characterId = (long)message.Header.CharacterId;

                // 2. 若 Header.CharacterId 为 0，从 ConnectionManager 反查
                if (characterId == 0 && _gameClient != null)
                {
                    var connectionId = _gameClient.Id;
                    if (!string.IsNullOrEmpty(connectionId))
                    {
                        var characterIds = _connectionManager.GetCharacterIdsByConnection(connectionId);
                        if (characterIds.Count > 0)
                        {
                            characterId = characterIds[0];
                        }
                    }
                }

                // 3. 刷新 Redis presence TTL（核心保活逻辑）
                if (characterId > 0)
                {
                    var refreshed = await _presenceStore.RefreshHeartbeatAsync(characterId).ConfigureAwait(false);
                    if (!refreshed)
                    {
                        // RefreshHeartbeatAsync 返回 false 有两种情况：
                        //   a) presence key 不存在（角色已通过 GoOfflineAsync 正确下线）—— 不应重建 presence
                        //   b) Redis 不可用（连接故障降级）—— 可重建 presence 作为兜底
                        // 修复 BUG（角色离线后 Redis 中仍残留在线信息）：
                        // 原实现盲目调用 SetOnlineAsync 重建 presence key，导致已下线角色在 Redis 中"复活"。
                        // 现采用与 CharacterPresenceMonitorHostedService 一致的二次确认模式：
                        // 仅当 ConnectionManager 中该角色仍绑定活动连接时，才判定为 Redis 故障降级并重建 presence。
                        var activeConnection = _connectionManager.GetConnectionByCharacterId(characterId);
                        if (activeConnection != null && activeConnection.IsConnected)
                        {
                            // 连接仍存活 → 判定为 Redis 故障降级，重建 presence key 作为兜底
                            Logger.LogWarning(
                                "角色 {CharacterId} 心跳续期失败（Redis 不可用），但连接仍存活，重建 presence 作为兜底",
                                characterId);
                            await _presenceStore
                                .SetOnlineAsync(characterId, "gateway", activeConnection.ConnectionId)
                                .ConfigureAwait(false);
                        }
                        else
                        {
                            // 连接已断开或未注册 → 判定为角色已正确下线后的残留心跳，不重建 presence key
                            // 否则会让已下线角色在 Redis 中"复活"（用户反复报告的 BUG 根因）
                            Logger.LogWarning(
                                "角色 {CharacterId} 心跳续期失败且无活动连接，判定为已下线后的残留心跳，不重建 presence key",
                                characterId);
                        }
                    }
                    else
                    {
                        Logger.LogDebug("角色 {CharacterId} 心跳续期成功", characterId);
                    }
                }
                else
                {
                    // 未登录角色的心跳（可能客户端刚连接还未进入游戏），仅记录调试日志
                    Logger.LogDebug("收到未绑定角色的心跳消息，跳过 presence 续期");
                }

                // 4. 构造心跳响应
                var serverTime = DateTime.UtcNow.Ticks;
                long latency = 0;
                if (heartbeat.ClientTime > 0)
                {
                    // 计算单向延迟（毫秒）。注意：依赖客户端与服务器时钟同步，仅供参考
                    latency = Math.Max(0, (serverTime - heartbeat.ClientTime) / TimeSpan.TicksPerMillisecond);
                }

                var response = new HeartbeatResponse
                {
                    Timestamp = serverTime,
                    ServerTime = serverTime,
                    Latency = latency
                };

                var responsePacket = CreateHorizonMessage(response);
                return (true, responsePacket);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理心跳消息时发生异常");
                // 心跳处理失败不应断开连接，返回失败响应让客户端重试
                var errorResponse = new HeartbeatResponse
                {
                    Timestamp = DateTime.UtcNow.Ticks,
                    ServerTime = DateTime.UtcNow.Ticks,
                    Latency = -1 // -1 表示延迟计算失败
                };
                var responsePacket = CreateHorizonMessage(errorResponse);
                return (false, responsePacket);
            }
        }
    }
}