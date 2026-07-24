using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Horizon.Game.Core;
using Horizon.Game.Core.Sim.Server;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using Horizon.Game.Message.Sync;
using Horizon.Orleans.Interface.World;
using Microsoft.Extensions.Logging;

namespace Horizon.Game.Gateway.Services
{
    /// <summary>
    /// 同时实现两个角色（P6-b 运行时连线）：<br/>
    /// 1. <see cref="IZoneShardFanoutObserver"/> — 注册到 <c>IZoneShardGrain</c>，从 grain 侧收推送；<br/>
    /// 2. <see cref="IZoneShardFanoutSource"/> — 被 <see cref="GatewaySyncDispatcher"/> drain。<br/>
    /// 中间用一个有界 <see cref="Channel{T}"/> 做无锁队列，解耦 grain 回调线程与 dispatcher 工作线程。
    /// </summary>
    /// <remarks>
    /// 队列满时选用 <see cref="BoundedChannelFullMode.DropOldest"/> 策略：单机 gateway 一旦被 world-diff
    /// 洪水淹没，保证最新状态优先流动，而非卡死 grain；丢弃计数通过 <see cref="DroppedByBackpressureCount"/>
    /// 暴露供 <c>SyncMetrics</c> 汇报。
    /// </remarks>
    public sealed class GatewayZoneShardFanoutSource : IZoneShardFanoutObserver, IZoneShardFanoutSource, IAsyncDisposable
    {
        private readonly Channel<FanoutEvent> _channel;
        private readonly ILogger<GatewayZoneShardFanoutSource>? _logger;
        private int _diagCount;

        /// <summary>累计收到的 fanout 事件数（来自 grain 推送）。</summary>
        public long ReceivedEventCount { get; private set; }

        /// <summary>因队列满被丢弃的旧事件数（反压指标）。</summary>
        public long DroppedByBackpressureCount { get; private set; }

        /// <summary>当前排队中的事件数（诊断）。</summary>
        public int PendingCount => _channel.Reader.Count;

        /// <param name="capacity">有界队列容量；默认 8192 约等于 1 秒内 1k sessions × 8 diff/tick。</param>
        public GatewayZoneShardFanoutSource(int capacity = 8192, ILogger<GatewayZoneShardFanoutSource>? logger = null)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            _channel = Channel.CreateBounded<FanoutEvent>(new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = false,  // Task 18：多 worker 模式，多个 worker 并发 TryRead
                SingleWriter = false,
            });
            _logger = logger;
        }

        // --- IZoneShardFanoutObserver（grain 回调） ---

        /// <inheritdoc />
        public Task OnChunkDiffAsync(WorldChunkDiffPacket diff, IReadOnlyCollection<long> sessionIds)
        {
            if (diff is null || sessionIds is null || sessionIds.Count == 0)
                return Task.CompletedTask;

            var evt = new FanoutEvent
            {
                Packet = diff,
                TargetSessionIds = sessionIds,
            };

            ReceivedEventCount++;

            // 诊断：前 5 次无条件输出，确认 fanout 推送链路联通
            _diagCount++;
            if (_diagCount <= 5)
            {
                _logger?.LogWarning(
                    "[GatewayFanoutSource 诊断#{N}] 收到 fanout 推送。ReceivedCount={Count}, Sessions={SessionCount}, PendingCount={Pending}, PayloadType={PayloadType}, ChunkKey=0x{ChunkKey:X16}",
                    _diagCount, ReceivedEventCount, sessionIds.Count, PendingCount, diff.PayloadType, diff.ChunkMortonKey);
            }
            else if (ReceivedEventCount % 60 == 1)
            {
                _logger?.LogInformation(
                    "GatewayZoneShardFanoutSource：收到 fanout 推送。ReceivedCount={Count}, Sessions={SessionCount}, PendingCount={Pending}, PayloadType={PayloadType}, ChunkKey=0x{ChunkKey:X16}",
                    ReceivedEventCount, sessionIds.Count, PendingCount, diff.PayloadType, diff.ChunkMortonKey);
            }

            if (!_channel.Writer.TryWrite(evt))
            {
                // BoundedChannelFullMode.DropOldest 下 TryWrite 不会失败（会直接挤掉旧项），
                // 但为未来切换策略留探针：如果真写失败，记录为反压丢弃。
                DroppedByBackpressureCount++;
                _logger?.LogWarning("GatewayZoneShardFanoutSource 队列写入失败（PendingCount={Count}，反压）", PendingCount);
            }
            return Task.CompletedTask;
        }

        // --- IZoneShardFanoutSource（dispatcher drain） ---

        /// <inheritdoc />
        public async Task<FanoutEvent?> TryDequeueAsync(CancellationToken ct)
        {
            var reader = _channel.Reader;
            if (reader.TryRead(out var evt)) return evt;

            // 无事件时短暂挂起等待生产端；ct 取消或通道关闭返回 null。
            try
            {
                if (!await reader.WaitToReadAsync(ct).ConfigureAwait(false))
                    return null;
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            return reader.TryRead(out evt) ? evt : null;
        }

        public ValueTask DisposeAsync()
        {
            _channel.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>
    /// 把 <see cref="GatewaySyncDispatcher"/> 所需的 <see cref="ISessionRegistry"/> 契约
    /// 适配到 gateway 的 <see cref="IConnectionManager"/>。<br/>
    /// fanout 推送使用 <c>characterId</c> 作为 sessionId，因此优先按 characterId 反查
    /// <see cref="IGameConnection"/>；找不到时回退到按 <see cref="IGameConnection.UserId"/> 查找（兼容旧路径）。
    /// </summary>
    public sealed class ConnectionManagerSessionRegistry : ISessionRegistry
    {
        private readonly IConnectionManager _connections;

        public ConnectionManagerSessionRegistry(IConnectionManager connections)
        {
            _connections = connections ?? throw new ArgumentNullException(nameof(connections));
        }

        /// <inheritdoc />
        public bool TryGetEndpoint(long sessionId, out object? endpoint)
        {
            var conn = _connections.GetConnectionByCharacterId(sessionId);
            if (conn is { IsConnected: true })
            {
                endpoint = conn;
                return true;
            }

            var conn2 = _connections.GetConnectionByUserId(sessionId);
            if (conn2 is { IsConnected: true })
            {
                endpoint = conn2;
                return true;
            }

            endpoint = null;
            return false;
        }
    }

    /// <summary>
    /// 把 <see cref="SyncPacket"/> 通过 <see cref="SyncPacketCodec"/> 编码后，
    /// 包装为 <see cref="SyncFrameMessage"/> → <see cref="HorizonMessageAdapter"/> 帧格式，
    /// 写回 <see cref="IGameConnection"/>。<br/>
    /// 发送是 fire-and-forget：dispatcher 是同步分派，真实 IO 走 TouchSocket 异步栈。<br/>
    /// 异常被吞并计入 <see cref="FailedSendCount"/>，以免一个坏连接拖垮整轮广播。
    /// </summary>
    public sealed class GameConnectionPacketSink : IClientPacketSink
    {
        private readonly ILogger<GameConnectionPacketSink>? _logger;
        private readonly HorizonMessageAdapter _adapter;
        private long _sendAttemptCount;
        private int _diagCount;

        /// <summary>累计发送失败次数（连接异常、已关闭、序列化失败等）。</summary>
        public long FailedSendCount { get; private set; }

        public GameConnectionPacketSink(HorizonMessageAdapter adapter, ILogger<GameConnectionPacketSink>? logger = null)
        {
            _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
            _logger = logger;
        }

        /// <inheritdoc />
        public void Send(object endpoint, SyncPacket packet)
        {
            var attempt = Interlocked.Increment(ref _sendAttemptCount);
            _diagCount++;

            if (endpoint is not IGameConnection conn)
            {
                if (_diagCount <= 5 || attempt % 60 == 1)
                {
                    _logger?.LogWarning(
                        "[GameConnectionPacketSink 诊断#{N}] endpoint 不是 IGameConnection，静默丢弃（Attempt={Attempt}, EndpointType={Type}, PacketKind={Kind}）",
                        _diagCount, attempt, endpoint?.GetType().Name ?? "null", packet.Kind);
                }
                return;
            }

            if (!conn.IsConnected)
            {
                if (_diagCount <= 5 || attempt % 60 == 1)
                {
                    _logger?.LogWarning(
                        "[GameConnectionPacketSink 诊断#{N}] 连接已断开，静默丢弃（Attempt={Attempt}, ConnectionId={Id}, PacketKind={Kind}）",
                        _diagCount, attempt, conn.ConnectionId, packet.Kind);
                }
                return;
            }

            try
            {
                // 1. 用 SyncPacketCodec 编码内部帧（6 字节同步帧头 + payload）
                SyncPacketCodec.Encode(packet, out var frame, out var frameLength);
                var payload = new byte[frameLength];
                Buffer.BlockCopy(frame, 0, payload, 0, frameLength);
                System.Buffers.ArrayPool<byte>.Shared.Return(frame);

                // 2. 包装为 SyncFrameMessage（与 SyncPacketHandler.CreateSyncResponse 一致）
                var syncFrame = new SyncFrameMessage
                {
                    Frame = payload,
                    PacketKind = (byte)packet.Kind,
                    ProtocolVersion = packet.ProtocolVersion,
                };

                // 3. 通过 HorizonMessageAdapter 打包为 8 字节线路帧（客户端期望的格式）
                // 不压缩：同步包对延迟敏感，且通常较小
                var wireBytes = _adapter.PackMessage(syncFrame, MessageType.SyncPacket, compress: false);

                // 4. 发送到连接
                // 修复 BUG：原实现 _ = conn.SendAsync(wireBytes) 是 fire-and-forget，
                // SendAsync 内部 catch 块虽然会 MarkAsBroken，但 rethrow 的异常会成为 UnobservedTaskException。
                // 改为 ContinueWith 显式观察异常（SendAsync 内部已完成 MarkAsBroken/计数，无需再次处理）。
                var sendTask = conn.SendAsync(wireBytes);
                if (sendTask.IsCompleted)
                {
                    if (sendTask.IsFaulted)
                    {
                        FailedSendCount++;
                        _ = sendTask.Exception; // 观察异常，避免 UnobservedTaskException
                    }
                }
                else
                {
                    sendTask.ContinueWith(
                        t =>
                        {
                            FailedSendCount++;
                            _ = t.Exception;
                        },
                        TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
                }

                if (_diagCount <= 5)
                {
                    _logger?.LogWarning(
                        "[GameConnectionPacketSink 诊断#{N}] 已发送 SyncPacket 到客户端（ConnectionId={Id}, PacketKind={Kind}, WireBytes={Bytes}）",
                        _diagCount, conn.ConnectionId, packet.Kind, wireBytes.Length);
                }
                else if (attempt % 60 == 1)
                {
                    _logger?.LogInformation(
                        "GameConnectionPacketSink：已发送 SyncPacket 到客户端（Attempt={Attempt}, ConnectionId={Id}, PacketKind={Kind}, WireBytes={Bytes}）",
                        attempt, conn.ConnectionId, packet.Kind, wireBytes.Length);
                }
            }
            catch (Exception ex)
            {
                FailedSendCount++;
                // 提升日志级别：前 5 次用 Warning，确保异常不被吞掉
                if (_diagCount <= 5)
                {
                    _logger?.LogWarning(ex,
                        "[GameConnectionPacketSink 诊断#{N}] 写回失败（ConnectionId={Id}, PacketKind={Kind}）",
                        _diagCount, conn.ConnectionId, packet.Kind);
                }
                else
                {
                    _logger?.LogDebug(ex, "GameConnectionPacketSink 写回失败（ConnectionId={Id}）", conn.ConnectionId);
                }
            }
        }

        /// <summary>
        /// Task 15：一次性编码 SyncPacket 为 wireBytes，供所有 session 复用。
        /// dispatcher 在并行分发前调用此方法编码一次，然后通过 <see cref="Send(object, byte[], int)"/> 复用。
        /// </summary>
        public byte[] Encode(SyncPacket packet, out int length)
        {
            // 1. 用 SyncPacketCodec 编码内部帧（6 字节同步帧头 + payload）
            SyncPacketCodec.Encode(packet, out var frame, out var frameLength);
            var payload = new byte[frameLength];
            Buffer.BlockCopy(frame, 0, payload, 0, frameLength);
            System.Buffers.ArrayPool<byte>.Shared.Return(frame);

            // 2. 包装为 SyncFrameMessage（与 SyncPacketHandler.CreateSyncResponse 一致）
            var syncFrame = new SyncFrameMessage
            {
                Frame = payload,
                PacketKind = (byte)packet.Kind,
                ProtocolVersion = packet.ProtocolVersion,
            };

            // 3. 通过 HorizonMessageAdapter 打包为 8 字节线路帧（客户端期望的格式）
            // 不压缩：同步包对延迟敏感，且通常较小
            var wireBytes = _adapter.PackMessage(syncFrame, MessageType.SyncPacket, compress: false);
            length = wireBytes.Length;
            return wireBytes;
        }

        /// <summary>
        /// Task 16：使用预编码的 wireBytes 直接发送，跳过 SyncPacketCodec.Encode + PackMessage。
        /// 由 GatewaySyncDispatcher.Dispatch 在并行分发前一次性编码，所有 session 复用。
        /// </summary>
        public void Send(object endpoint, byte[] wireBytes, int length)
        {
            var attempt = Interlocked.Increment(ref _sendAttemptCount);
            _diagCount++;

            if (endpoint is not IGameConnection conn)
            {
                if (_diagCount <= 5 || attempt % 60 == 1)
                {
                    _logger?.LogWarning(
                        "[GameConnectionPacketSink 诊断#{N}] endpoint 不是 IGameConnection，静默丢弃（Attempt={Attempt}, EndpointType={Type}）",
                        _diagCount, attempt, endpoint?.GetType().Name ?? "null");
                }
                return;
            }

            if (!conn.IsConnected)
            {
                if (_diagCount <= 5 || attempt % 60 == 1)
                {
                    _logger?.LogWarning(
                        "[GameConnectionPacketSink 诊断#{N}] 连接已断开，静默丢弃（Attempt={Attempt}, ConnectionId={Id}）",
                        _diagCount, attempt, conn.ConnectionId);
                }
                return;
            }

            try
            {
                // 修复 BUG：原实现 _ = conn.SendAsync(wireBytes) 是 fire-and-forget，
                // SendAsync 内部 catch 块虽然会 MarkAsBroken，但 rethrow 的异常会成为 UnobservedTaskException，
                // 在 GC 时延迟触发，可能导致进程级未观察任务异常事件被错误地记录。
                // 改为 ContinueWith 显式观察并吞掉异常（SendAsync 内部已完成 MarkAsBroken/计数，无需再次处理），
                // 同时使用 TaskContinuationOptions.OnlyOnFaulted 避免无谓的延续分配。
                var sendTask = conn.SendAsync(wireBytes);
                if (sendTask.IsCompleted) // 同步完成（失败/成功）的快速路径，避免不必要的 ContinueWith 分配
                {
                    if (sendTask.IsFaulted)
                    {
                        FailedSendCount++;
                        // 异常已在 SendAsync 内部处理（MarkAsBroken/计数），这里只观察，不 rethrow
                        _ = sendTask.Exception;
                    }
                }
                else
                {
                    sendTask.ContinueWith(
                        t =>
                        {
                            FailedSendCount++;
                            // 异常已在 SendAsync 内部处理（MarkAsBroken/计数），这里只观察，避免 UnobservedTaskException
                            _ = t.Exception;
                        },
                        TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
                }

                // 修复 BUG（诊断盲区）：原实现在预编码 wireBytes 版本中缺少发送成功的诊断日志，
                // 导致前5次诊断后无法确认同步包是否被正确下发到客户端。
                // 添加与旧方法一致的成功诊断日志（前5次 WRN + 每60次 INF）。
                if (_diagCount <= 5)
                {
                    _logger?.LogWarning(
                        "[GameConnectionPacketSink 诊断#{N}] 已发送预编码 SyncPacket 到客户端（ConnectionId={Id}, WireBytes={Bytes}）",
                        _diagCount, conn.ConnectionId, length);
                }
                else if (attempt % 60 == 1)
                {
                    _logger?.LogInformation(
                        "GameConnectionPacketSink：已发送预编码 SyncPacket 到客户端（Attempt={Attempt}, ConnectionId={Id}, WireBytes={Bytes}）",
                        attempt, conn.ConnectionId, length);
                }
            }
            catch (Exception ex)
            {
                FailedSendCount++;
                if (_diagCount <= 5)
                {
                    _logger?.LogWarning(ex,
                        "[GameConnectionPacketSink 诊断#{N}] 写回失败（ConnectionId={Id}）",
                        _diagCount, conn.ConnectionId);
                }
                else
                {
                    _logger?.LogDebug(ex, "GameConnectionPacketSink 写回失败（ConnectionId={Id}）", conn.ConnectionId);
                }
            }
        }
    }
}
