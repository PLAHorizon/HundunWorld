using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Horizon.Game.Core.Sim.Server;
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
                SingleReader = true,
                SingleWriter = false,
            });
            _logger = logger;
        }

        // --- IZoneShardFanoutObserver（grain 回调） ---

        /// <inheritdoc />
        public Task OnChunkDiffAsync(WorldChunkDiffPacket diff, long[] sessionIds)
        {
            if (diff is null || sessionIds is null || sessionIds.Length == 0)
                return Task.CompletedTask;

            var evt = new FanoutEvent
            {
                Packet = diff,
                TargetSessionIds = sessionIds,
            };

            ReceivedEventCount++;
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
    /// 以 <see cref="IGameConnection.UserId"/>（即 sessionId）为 key 反查 <see cref="IGameConnection"/>。
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
            var conn = _connections.GetConnectionByUserId(sessionId);
            if (conn is { IsConnected: true })
            {
                endpoint = conn;
                return true;
            }
            endpoint = null;
            return false;
        }
    }

    /// <summary>
    /// 把 <see cref="SyncPacket"/> 通过 <see cref="SyncPacketCodec"/> 编码后写回 <see cref="IGameConnection"/>。<br/>
    /// 发送是 fire-and-forget：dispatcher 是同步分派，真实 IO 走 TouchSocket 异步栈。<br/>
    /// 异常被吞并计入 <see cref="FailedSendCount"/>，以免一个坏连接拖垮整轮广播。
    /// </summary>
    public sealed class GameConnectionPacketSink : IClientPacketSink
    {
        private readonly ILogger<GameConnectionPacketSink>? _logger;

        /// <summary>累计发送失败次数（连接异常、已关闭、序列化失败等）。</summary>
        public long FailedSendCount { get; private set; }

        public GameConnectionPacketSink(ILogger<GameConnectionPacketSink>? logger = null)
        {
            _logger = logger;
        }

        /// <inheritdoc />
        public void Send(object endpoint, SyncPacket packet)
        {
            if (endpoint is not IGameConnection conn || !conn.IsConnected) return;
            try
            {
                SyncPacketCodec.Encode(packet, out var frame, out var frameLength);
                // 拷贝到精确长度数组：IGameConnection.SendAsync 约定接收刚好帧长的字节流，
                // 且 Encode 的 frame 由 ArrayPool 借出，直接异步送出可能在 await 到达前被归还。
                var copy = new byte[frameLength];
                Buffer.BlockCopy(frame, 0, copy, 0, frameLength);
                System.Buffers.ArrayPool<byte>.Shared.Return(frame);
                _ = conn.SendAsync(copy);
            }
            catch (Exception ex)
            {
                FailedSendCount++;
                _logger?.LogDebug(ex, "GameConnectionPacketSink 写回失败（ConnectionId={Id}）", conn.ConnectionId);
            }
        }
    }
}
