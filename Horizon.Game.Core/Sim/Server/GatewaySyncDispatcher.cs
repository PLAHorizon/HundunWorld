using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Horizon.Game.Message.Sync;
using Microsoft.Extensions.Logging;

namespace Horizon.Game.Core.Sim.Server;

/// <summary>
/// Gateway 侧的 SyncPacket 广播分派器（P6-a / P6-b）。<br/>
/// 负责：
/// <list type="number">
///   <item>从 <see cref="IZoneShardFanoutSource"/>（对应 <c>IZoneShardGrain</c> 的订阅推送）拉取事件；</item>
///   <item>按 AOI Interest Set 查询 <see cref="ISessionRegistry"/>，把事件转发到每个相关玩家会话。</item>
/// </list>
/// </summary>
/// <remarks>
/// 纯 C# 逻辑，不依赖 Orleans / TouchSocket；通过接口注入实现解耦：
/// <list type="bullet">
///   <item><see cref="ISessionRegistry"/> — Gateway 持有的 sessionId → ClientEndpoint 映射。</item>
///   <item><see cref="IClientPacketSink"/> — 实际把字节写回客户端 TCP/QUIC 流的接收者。</item>
///   <item><see cref="IZoneShardFanoutSource"/> — Zone Shard 推送事件的来源。</item>
/// </list>
/// </remarks>
public sealed class GatewaySyncDispatcher
{
    private readonly IZoneShardFanoutSource _source;
    private readonly ISessionRegistry _registry;
    private readonly IClientPacketSink _sink;
    private readonly ILogger<GatewaySyncDispatcher>? _logger;
    private long _totalDispatchedCount;
    private long _totalFailedCount;

    /// <summary>灰度开关；false 时 <see cref="RunOnceAsync"/> 直接 no-op，保持旧路径。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>累计处理的 fanout 事件数。</summary>
    public long ProcessedEventCount { get; private set; }

    /// <summary>累计下发到客户端的包数。</summary>
    public long DeliveredPacketCount { get; private set; }

    /// <summary>因 session 下线/不在线而丢弃的包数。</summary>
    public long DroppedOfflineCount { get; private set; }

    public long TotalDispatchedCount => Interlocked.Read(ref _totalDispatchedCount);

    public long TotalFailedCount => Interlocked.Read(ref _totalFailedCount);

    public GatewaySyncDispatcher(
        IZoneShardFanoutSource source,
        ISessionRegistry registry,
        IClientPacketSink sink,
        ILogger<GatewaySyncDispatcher>? logger = null,
        bool enabled = true)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        _logger = logger;
        Enabled = enabled;
    }

    public Task EnableAsync()
    {
        Enabled = true;
        _logger?.LogInformation("GatewaySyncDispatcher 已启用。");
        return Task.CompletedTask;
    }

    public Task DisableAsync()
    {
        Enabled = false;
        _logger?.LogInformation("GatewaySyncDispatcher 已禁用。");
        return Task.CompletedTask;
    }

    /// <summary>处理一轮事件（适合从 gateway 主循环中每 tick 调用一次）。</summary>
    /// <returns>处理的事件条数。</returns>
    public async Task<int> RunOnceAsync(CancellationToken ct = default)
    {
        if (!Enabled) return 0;
        int processed = 0;
        while (!ct.IsCancellationRequested)
        {
            var evt = await _source.TryDequeueAsync(ct).ConfigureAwait(false);
            if (evt is null) break;
            Dispatch(evt);
            processed++;
            ProcessedEventCount++;
        }
        return processed;
    }

    /// <summary>同步分派单条事件（测试友好，也用于单元测试）。</summary>
    public void Dispatch(FanoutEvent evt)
    {
        if (evt is null) throw new ArgumentNullException(nameof(evt));
        if (evt.Packet is null) return;

        var sessionCount = evt.TargetSessionIds.Count;

        try
        {
            if (evt.Packet is SnapshotPacket snapshot)
            {
                _logger?.LogInformation(
                    "GatewaySyncDispatcher 分派快照：目标会话数={SessionCount}，快照大小={DeltaCount}",
                    sessionCount, snapshot.Deltas?.Length ?? 0);
            }

            foreach (var sessionId in evt.TargetSessionIds)
            {
                if (_registry.TryGetEndpoint(sessionId, out var endpoint) && endpoint is not null)
                {
                    _sink.Send(endpoint, evt.Packet);
                    DeliveredPacketCount++;
                }
                else
                {
                    DroppedOfflineCount++;
                }
            }

            Interlocked.Increment(ref _totalDispatchedCount);
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _totalFailedCount);
            _logger?.LogError(ex, "GatewaySyncDispatcher 分派失败：目标会话数={SessionCount}", sessionCount);
        }
    }
}

/// <summary>Zone Shard 推送的事件：一个 <see cref="SyncPacket"/> + 应当接收该包的 session 列表。</summary>
public sealed class FanoutEvent
{
    public SyncPacket Packet { get; init; } = null!;
    public IReadOnlyList<long> TargetSessionIds { get; init; } = Array.Empty<long>();
}

/// <summary>Zone Shard 的 fanout 来源。</summary>
public interface IZoneShardFanoutSource
{
    /// <summary>尝试取一个事件；无事件时返回 null（不抛）。</summary>
    Task<FanoutEvent?> TryDequeueAsync(CancellationToken ct);
}

/// <summary>Gateway 内部的 session → endpoint 注册表。</summary>
public interface ISessionRegistry
{
    bool TryGetEndpoint(long sessionId, out object? endpoint);
}

/// <summary>发到客户端 endpoint 的包 sink。</summary>
public interface IClientPacketSink
{
    void Send(object endpoint, SyncPacket packet);
}
