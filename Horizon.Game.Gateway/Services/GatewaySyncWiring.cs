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
        private readonly int _capacity;
        private readonly ILogger<GatewayZoneShardFanoutSource>? _logger;

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
            _capacity = capacity;
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

            EnqueueEvent(diff, sessionIds);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        /// <remarks>
        /// P-F1：grain 一轮广播（快照 delta + correction + InputAck）合并为单条 observer 消息，
        /// gateway 在此处拆回逐条 FanoutEvent 入队，保持 dispatcher 逐事件分发语义不变；
        /// 跨进程 RPC 次数从 O(chunk数) 降为 O(1)，显著降低 silo→gateway 推送延迟。
        /// </remarks>
        public Task OnChunkDiffBatchAsync(FanoutBatchItem[] items)
        {
            if (items is null || items.Length == 0)
                return Task.CompletedTask;

            foreach (var item in items)
            {
                if (item?.Diff is null || item.SessionIds is null || item.SessionIds.Length == 0)
                    continue;
                EnqueueEvent(item.Diff, item.SessionIds);
            }
            return Task.CompletedTask;
        }

        /// <summary>把单条 (diff, 受众) 封装为 FanoutEvent 写入有界队列（含反压探针）。</summary>
        private void EnqueueEvent(WorldChunkDiffPacket diff, IReadOnlyCollection<long> sessionIds)
        {
            var evt = new FanoutEvent
            {
                Packet = diff,
                TargetSessionIds = sessionIds,
            };

            ReceivedEventCount++;

            if (ReceivedEventCount % 60 == 1)
            {
                _logger?.LogDebug(
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

            // 反压预警：DropOldest 模式下 TryWrite 永远返回 true，旧事件被静默丢弃。
            // 当队列使用率 >80% 时记录警告，便于在真实环境中及时发现 dispatch 跟不上 fanout 速率的问题。
            var pending = PendingCount;
            if (pending > _capacity * 0.8 && ReceivedEventCount % 60 == 1)
            {
                _logger?.LogWarning(
                    "[FanoutBackpressure] 队列使用率过高：Pending={Pending}/{Capacity} ({Pct:F1}%), ReceivedCount={Received}, Dropped={Dropped}",
                    pending, _capacity, (double)pending / _capacity * 100, ReceivedEventCount, DroppedByBackpressureCount);
            }
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

            if (endpoint is not IGameConnection conn)
            {
                if (attempt % 60 == 1)
                {
                    _logger?.LogDebug(
                        "GameConnectionPacketSink：endpoint 不是 IGameConnection，静默丢弃（Attempt={Attempt}, EndpointType={Type}, PacketKind={Kind}）",
                        attempt, endpoint?.GetType().Name ?? "null", packet.Kind);
                }
                return;
            }

            if (!conn.IsConnected)
            {
                if (attempt % 60 == 1)
                {
                    _logger?.LogDebug(
                        "GameConnectionPacketSink：连接已断开，静默丢弃（Attempt={Attempt}, ConnectionId={Id}, PacketKind={Kind}）",
                        attempt, conn.ConnectionId, packet.Kind);
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

                if (attempt % 60 == 1)
                {
                    _logger?.LogDebug(
                        "GameConnectionPacketSink：已发送 SyncPacket 到客户端（Attempt={Attempt}, ConnectionId={Id}, PacketKind={Kind}, WireBytes={Bytes}）",
                        attempt, conn.ConnectionId, packet.Kind, wireBytes.Length);
                }
            }
            catch (Exception ex)
            {
                FailedSendCount++;
                _logger?.LogDebug(ex, "GameConnectionPacketSink 写回失败（ConnectionId={Id}）", conn.ConnectionId);
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
            // 方案 1（plan.md §5 根因 #1）：此处 new byte[frameLength] 产生 Gen0 分配。
            // 不能改用 ArrayPool<byte>.Shared.Rent(frameLength)，原因：
            //   - ArrayPool.Rent 返回的 buffer.Length 可能 > frameLength（池按 2 的幂次对齐）。
            //   - 本 payload 被赋给 syncFrame.Frame，随后 MemoryPackSerializer.Serialize(syncFrame) 会
            //     把 payload.Length 写入序列化流作为长度前缀。若用 Rent 的 buffer，Length 为池化容量
            //     而非 frameLength，导致长度前缀错误、反序列化越界/截断。
            //   - 且 wireBytes（PackMessage 返回值）被 dispatcher 传给多 session 的 fire-and-forget
            //     conn.SendAsync（见 Send(object, byte[], int)），异步生命周期超出本方法作用域，
            //     无法在 Encode 内安全 Return。
            // 保持 new byte[frameLength]，接受单次精确长度分配。
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

            if (endpoint is not IGameConnection conn)
            {
                if (attempt % 60 == 1)
                {
                    _logger?.LogDebug(
                        "GameConnectionPacketSink：endpoint 不是 IGameConnection，静默丢弃（Attempt={Attempt}, EndpointType={Type}）",
                        attempt, endpoint?.GetType().Name ?? "null");
                }
                return;
            }

            if (!conn.IsConnected)
            {
                if (attempt % 60 == 1)
                {
                    _logger?.LogDebug(
                        "GameConnectionPacketSink：连接已断开，静默丢弃（Attempt={Attempt}, ConnectionId={Id}）",
                        attempt, conn.ConnectionId);
                }
                return;
            }

            try
            {
                var sendTask = conn.SendAsync(wireBytes);
                if (sendTask.IsCompleted)
                {
                    if (sendTask.IsFaulted)
                    {
                        FailedSendCount++;
                        _ = sendTask.Exception;
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

                if (attempt % 60 == 1)
                {
                    _logger?.LogDebug(
                        "GameConnectionPacketSink：已发送预编码 SyncPacket 到客户端（Attempt={Attempt}, ConnectionId={Id}, WireBytes={Bytes}）",
                        attempt, conn.ConnectionId, length);
                }
            }
            catch (Exception ex)
            {
                FailedSendCount++;
                _logger?.LogDebug(ex, "GameConnectionPacketSink 写回失败（ConnectionId={Id}）", conn.ConnectionId);
            }
        }
    }
}
