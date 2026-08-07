using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Tasks;
using Horizon.Game.Core.World;
using Horizon.Game.Message.Sync;
using Horizon.Game.Message.Sync.Components;
using Horizon.Orleans.Interface.World;
using Orleans;
using Orleans.TestingHost;
using Xunit;
using Xunit.Abstractions;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// TCP Transport 集成测试 — 验证真实跨进程 RPC 下的多客户端同步。
///
/// 与 ZoneShardIntegrationTests（in-memory transport）的差异：
/// - in-memory transport：silo 与 client 同进程，无真实网络序列化/传输开销
/// - TCP transport（本测试）：silo 与 client 通过 TCP socket 通信，走完整网络栈
///   * 真实 MemoryPack/Orleans 序列化 + 网络传输
///   * 真实连接管理 + 消息路由
///   * 验证 BUG_REPORT_MULTI_CLIENT_SYNC.md 第6点建议的"跨进程 RPC 性能验证"
///
/// 本测试填补了 in-memory transport 测试的验证缺口：
/// 如果 TCP transport 下测试仍通过，则证明优化方案在真实网络环境下有效，
/// 而非仅在 in-memory transport 下"碰巧"通过。
/// </summary>
public class TcpTransportSyncTests : IAsyncLifetime
{
    private TestCluster? _cluster;
    private readonly ITestOutputHelper _output;

    public TcpTransportSyncTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public async Task InitializeAsync()
    {
        var builder = new TestClusterBuilder();
        // 关键：设置非零端口启用 TCP transport（默认 0 = in-memory transport）
        // 使用 21111/30001 避免与运行中的生产 Silo（11111/30000）冲突
        builder.Options.BaseSiloPort = 21111;
        builder.Options.BaseGatewayPort = 30001;
        builder.Options.InitialSilosCount = 1;
        builder.AddSiloBuilderConfigurator<ZoneShardTestSiloConfigurator>();
        builder.AddClientBuilderConfigurator<ZoneShardTestClientConfigurator>();
        _cluster = builder.Build();
        await _cluster.DeployAsync();
    }

    public async Task DisposeAsync()
    {
        if (_cluster != null)
        {
            await _cluster.StopAllSilosAsync();
            _cluster.Dispose();
        }
    }

    private sealed class TcpFanoutObserver : IZoneShardFanoutObserver
    {
        public int ReceivedDiffCount => _diffs.Count;
        public int EntityDeltaDiffCount => _entityDeltas.Count;
        public int EventDiffCount => _events.Count;

        private readonly ConcurrentQueue<WorldChunkDiffPacket> _diffs = new();
        private readonly ConcurrentQueue<WorldChunkDiffPacket> _entityDeltas = new();
        private readonly ConcurrentQueue<WorldChunkDiffPacket> _events = new();

        public Task OnChunkDiffAsync(WorldChunkDiffPacket diff, IReadOnlyCollection<long> sessionIds)
        {
            _diffs.Enqueue(diff);
            if (diff.PayloadType == WorldChunkDiffPayloadType.EntityDelta)
                _entityDeltas.Enqueue(diff);
            else if (diff.PayloadType == WorldChunkDiffPayloadType.Event)
                _events.Enqueue(diff);
            return Task.CompletedTask;
        }

        public void Reset()
        {
            while (_diffs.TryDequeue(out _)) { }
            while (_entityDeltas.TryDequeue(out _)) { }
            while (_events.TryDequeue(out _)) { }
        }
    }

    private static async Task WaitForDiffCountAsync(TcpFanoutObserver observer, int expectedMin, int timeoutMs)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (observer.ReceivedDiffCount >= expectedMin)
                return;
            await Task.Delay(50);
        }
    }

    /// <summary>
    /// TCP transport 下 5 客户端同步稳定性测试。
    /// 验证真实跨进程 RPC（TCP socket + 序列化）下：
    /// 1. observer 能收到 EntityDelta diff（序列化/网络传输正常）
    /// 2. 平均 tick 耗时不超过 16.7ms 帧预算（无 tick 堆积）
    /// 3. Event diff（InputAck）正常下发
    /// </summary>
    [Fact]
    public async Task TcpTransport_5Clients_SyncStableUnderRealNetwork()
    {
        Assert.NotNull(_cluster);
        var zoneShard = _cluster!.GrainFactory.GetGrain<IZoneShardGrain>(10);

        var observer = new TcpFanoutObserver();
        var observerRef = _cluster.Client.CreateObjectReference<IZoneShardFanoutObserver>(observer);
        var subscriptionId = Guid.NewGuid();
        await zoneShard.SubscribeFanoutAsync(subscriptionId, observerRef);

        const float ecsZ = 8f;
        var chunkKey = WorldCoord.ToChunkMortonKey(0, 0, ecsZ);
        await zoneShard.SubscribeSessionAsync(sessionId: 1, mortonKeys: new[] { chunkKey });

        // 注册 5 个实体（模拟 5 个客户端角色）
        for (ulong i = 1; i <= 5; i++)
        {
            await zoneShard.RegisterEntityAsync(entityId: i, initialX: 0, initialY: ecsZ, initialZ: 0);
        }

        // tick 0：全量快照
        await zoneShard.TickAsync(tickTime: 1.0);
        await WaitForDiffCountAsync(observer, expectedMin: 1, timeoutMs: 3000);
        observer.Reset();

        // 模拟 60 tick（1 秒）：5 个客户端每 tick 提交移动输入
        const int TotalTicks = 60;
        var sw = Stopwatch.StartNew();
        for (int tick = 1; tick <= TotalTicks; tick++)
        {
            for (ulong i = 1; i <= 5; i++)
            {
                var input = new InputPacket
                {
                    ClientTick = tick,
                    MoveX = 1.0f,
                    MoveY = 0f,
                    MaxSpeed = 6f,
                };
                await zoneShard.SubmitInputAsync(entityId: i, input,
                    reportedEndX: tick * 0.1f, reportedEndY: 0f, reportedEndZ: ecsZ);
            }
            await zoneShard.TickAsync(tickTime: 1.0 + tick * (1.0 / 60.0));
        }
        sw.Stop();

        await WaitForDiffCountAsync(observer, expectedMin: 1, timeoutMs: 3000);

        var entityDeltaDiffs = observer.EntityDeltaDiffCount;
        var eventDiffs = observer.EventDiffCount;
        var avgTickMs = sw.Elapsed.TotalMilliseconds / TotalTicks;

        _output.WriteLine(
            $"[TCP-5Client] 5 客户端 @ TCP Transport（端口 21111/30001）：总计 {sw.Elapsed.TotalMilliseconds:F2}ms，" +
            $"平均 {avgTickMs:F3}ms/tick，EntityDelta diff={entityDeltaDiffs}，Event diff(InputAck)={eventDiffs}");

        // 验证 1：TCP transport 下 observer 仍能收到 EntityDelta diff
        // （证明真实网络序列化/传输正常，优化方案不仅限于 in-memory transport）
        Assert.True(entityDeltaDiffs > 0,
            $"TCP transport 5 客户端测试：应收到至少 1 个 EntityDelta diff，实际 {entityDeltaDiffs}。" +
            "如失败，说明优化方案在真实网络环境下无效（仅在 in-memory transport 下通过）。");

        // 验证 2：TCP transport 下平均 tick 耗时不超过 16.7ms 帧预算
        // TCP transport 比 in-memory 有额外网络开销，但仍不应导致 tick 堆积
        Assert.True(avgTickMs <= 16.7,
            $"TCP transport 5 客户端测试：平均 tick 耗时 {avgTickMs:F3}ms 超过 16.7ms 帧预算（tick 堆积风险）。");

        // 验证 3：Event diff（InputAck）应正常下发
        Assert.True(eventDiffs > 0,
            $"TCP transport 5 客户端测试：应收到 Event diff(InputAck)，实际 {eventDiffs}。");

        // 验证 4：所有实体仍注册（无异常离线）
        for (ulong i = 1; i <= 5; i++)
        {
            Assert.True(await zoneShard.HasEntityAsync(i),
                $"TCP transport 测试后实体 {i} 应仍注册（无异常离线）。");
        }

        await zoneShard.UnsubscribeFanoutAsync(subscriptionId);
    }

    /// <summary>
    /// TCP transport 下位置驱动订阅验证。
    /// 验证真实网络环境下 UpdateSessionPositionAsync（位置驱动订阅）正常工作，
    /// 而非回退到 chunk 数组传输路径。
    /// </summary>
    [Fact]
    public async Task TcpTransport_PositionDrivenSubscription_WorksUnderRealNetwork()
    {
        Assert.NotNull(_cluster);
        var zoneShard = _cluster!.GrainFactory.GetGrain<IZoneShardGrain>(11);

        var observer = new TcpFanoutObserver();
        var observerRef = _cluster.Client.CreateObjectReference<IZoneShardFanoutObserver>(observer);
        var subscriptionId = Guid.NewGuid();
        await zoneShard.SubscribeFanoutAsync(subscriptionId, observerRef);

        const float ecsZ = 8f;
        const ulong entityId = 200;
        const long sessionId = 200;

        // EnterWorld + 位置驱动订阅
        await zoneShard.EnterWorldAsync(
            sessionId: sessionId,
            entityId: entityId,
            initialX: 0f,
            initialY: ecsZ,
            initialZ: 0f,
            initialInterestChunks: Array.Empty<ulong>());

        // 通过位置驱动订阅更新 AOI（仅传 3 个 float，不传 chunk 数组）
        var changed = await zoneShard.UpdateSessionPositionAsync(sessionId, x: 0f, y: ecsZ, z: 0f);

        Assert.True(changed >= 0,
            $"TCP transport 位置驱动订阅：UpdateSessionPositionAsync 应返回非负值，实际 {changed}。");

        await zoneShard.TickAsync(tickTime: 1.0);
        await WaitForDiffCountAsync(observer, expectedMin: 1, timeoutMs: 3000);

        Assert.True(observer.EntityDeltaDiffCount > 0,
            $"TCP transport 位置驱动订阅：应通过位置驱动订阅收到 EntityDelta diff，实际 {observer.EntityDeltaDiffCount}。");

        Assert.True(await zoneShard.HasEntityAsync(entityId),
            "TCP transport 位置驱动订阅：实体应已注册。");

        await zoneShard.UnsubscribeFanoutAsync(subscriptionId);
    }
}
