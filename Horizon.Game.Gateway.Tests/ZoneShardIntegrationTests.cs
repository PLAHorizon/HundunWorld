using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
/// ZoneShardGrain Orleans.TestingHost 集成测试。
/// 在真实 Orleans Silo 运行时中验证多客户端同步修复方案的有效性。
///
/// 与 mock 单元测试（MultiClientSyncPerformanceTests）的互补关系：
/// - mock 测试：验证降频逻辑、心跳节奏、基线合并等"逻辑层面"的正确性（FanoutObserver 为同步内存调用）
/// - 集成测试：验证真实 Orleans 运行时下的"端到端"行为，包括：
///   * 真实 grain 激活与状态管理
///   * 真实序列化（WorldChunkDiffPacket / InputPacket 跨 grain 边界）
///   * 真实 IGrainObserver 回调机制（CreateObjectReference → 跨运行时回调）
///   * 真实 Orleans 调度器与计时器
///
/// TestCluster 默认使用 in-memory transport（silo 与 client 同进程），
/// 仍走完整 Orleans 运行时（序列化/消息路由/调度），比 mock 测试更接近生产环境。
/// </summary>
public class ZoneShardIntegrationTests : IAsyncLifetime
{
    private TestCluster? _cluster;
    private readonly ITestOutputHelper _output;

    public ZoneShardIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public async Task InitializeAsync()
    {
        var builder = new TestClusterBuilder();
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

    /// <summary>
    /// 真实 IZoneShardFanoutObserver 实现，通过 Orleans ObjectReference 接收 grain 回调。
    /// 使用 ConcurrentQueue 线程安全收集 diff（Orleans 回调可能在调度器线程执行）。
    /// </summary>
    private sealed class IntegrationFanoutObserver : IZoneShardFanoutObserver
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

    /// <summary>
    /// 集成测试：在真实 Orleans Silo 中验证 2 客户端同步。
    /// 通过 GrainFactory 激活 ZoneShardGrain，使用 CreateObjectReference 注册真实 observer。
    /// 验证：observer 能通过 Orleans 运行时收到 EntityDelta diff（序列化/消息路由正常）。
    /// </summary>
    [Fact]
    public async Task MultiClient_2Clients_RealOrleans_SyncReceivesDiffs()
    {
        Assert.NotNull(_cluster);
        var zoneShard = _cluster!.GrainFactory.GetGrain<IZoneShardGrain>(1);

        // 创建真实 observer 引用（通过 Orleans ObjectReference 机制）。
        // CreateObjectReference 是同步方法（Orleans 7+），返回 IZoneShardFanoutObserver 引用。
        var observer = new IntegrationFanoutObserver();
        var observerRef = _cluster.Client.CreateObjectReference<IZoneShardFanoutObserver>(observer);
        var subscriptionId = Guid.NewGuid();
        await zoneShard.SubscribeFanoutAsync(subscriptionId, observerRef);

        // 订阅 chunk（所有实体放在同一 chunk）
        const float ecsZ = 8f;
        var chunkKey = WorldCoord.ToChunkMortonKey(0, 0, ecsZ);
        await zoneShard.SubscribeSessionAsync(sessionId: 1, mortonKeys: new[] { chunkKey });

        // 注册 2 个实体（模拟 2 个客户端角色）
        for (ulong i = 1; i <= 2; i++)
        {
            await zoneShard.RegisterEntityAsync(entityId: i, initialX: 0, initialY: ecsZ, initialZ: 0);
        }

        // tick 0：全量快照（基线）
        await zoneShard.TickAsync(tickTime: 1.0);

        // 等待 Orleans 回调送达（真实运行时有调度延迟）
        await WaitForDiffCountAsync(observer, expectedMin: 1, timeoutMs: 2000);
        observer.Reset();

        // 模拟 60 tick（1 秒）：每个客户端每 tick 提交移动输入
        const int TotalTicks = 60;
        var sw = Stopwatch.StartNew();
        for (int tick = 1; tick <= TotalTicks; tick++)
        {
            for (ulong i = 1; i <= 2; i++)
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

        // 等待最后一批 diff 送达
        await WaitForDiffCountAsync(observer, expectedMin: 1, timeoutMs: 2000);

        var entityDeltaDiffs = observer.EntityDeltaDiffCount;
        var eventDiffs = observer.EventDiffCount;
        var avgTickMs = sw.Elapsed.TotalMilliseconds / TotalTicks;

        _output.WriteLine(
            $"[Integration-2Client] 2 客户端 @ 真实 Orleans Silo：总计 {sw.Elapsed.TotalMilliseconds:F2}ms，" +
            $"平均 {avgTickMs:F3}ms/tick，EntityDelta diff={entityDeltaDiffs}，Event diff(InputAck)={eventDiffs}");

        // 验证 1：observer 通过真实 Orleans 运行时收到了 EntityDelta diff
        // （证明序列化/消息路由/observer 回调机制正常工作）
        Assert.True(entityDeltaDiffs > 0,
            $"2 客户端集成测试：应通过 Orleans 运行时收到至少 1 个 EntityDelta diff，实际 {entityDeltaDiffs}。" +
            "可能原因：observer 未正确注册、序列化失败、grain 未广播。");

        // 验证 2：Event diff（InputAck）也应被收到（每 tick 下发，不受降频影响）
        Assert.True(eventDiffs > 0,
            $"2 客户端集成测试：应通过 Orleans 运行时收到至少 1 个 Event diff(InputAck)，实际 {eventDiffs}。");

        // 验证 3：平均 tick 耗时应在合理范围内（真实 Orleans 有调度/序列化开销，但不应堆积）
        // 真实 Orleans in-memory transport 下，单 tick RPC 耗时约 0.1~2ms，
        // 20Hz 降频后每 3 tick 才广播一次，平均耗时应远低于 16.7ms 帧预算。
        Assert.True(avgTickMs <= 16.7,
            $"2 客户端集成测试：平均 tick 耗时 {avgTickMs:F3}ms 超过 16.7ms 帧预算（tick 堆积风险）。");

        // 清理
        await zoneShard.UnsubscribeFanoutAsync(subscriptionId);
    }

    /// <summary>
    /// 集成测试：在真实 Orleans Silo 中验证 5 客户端同步。
    /// 这是修复前"几乎无法同步"的场景，修复后应能正常广播 diff。
    /// </summary>
    [Fact]
    public async Task MultiClient_5Clients_RealOrleans_SyncStableUnderLoad()
    {
        Assert.NotNull(_cluster);
        var zoneShard = _cluster!.GrainFactory.GetGrain<IZoneShardGrain>(2);

        var observer = new IntegrationFanoutObserver();
        var observerRef = _cluster.Client.CreateObjectReference<IZoneShardFanoutObserver>(observer);
        var subscriptionId = Guid.NewGuid();
        await zoneShard.SubscribeFanoutAsync(subscriptionId, observerRef);

        const float ecsZ = 8f;
        var chunkKey = WorldCoord.ToChunkMortonKey(0, 0, ecsZ);
        await zoneShard.SubscribeSessionAsync(sessionId: 1, mortonKeys: new[] { chunkKey });

        // 注册 5 个实体
        for (ulong i = 1; i <= 5; i++)
        {
            await zoneShard.RegisterEntityAsync(entityId: i, initialX: 0, initialY: ecsZ, initialZ: 0);
        }

        // tick 0：全量快照
        await zoneShard.TickAsync(tickTime: 1.0);
        await WaitForDiffCountAsync(observer, expectedMin: 1, timeoutMs: 2000);
        observer.Reset();

        // 模拟 60 tick：5 个客户端每 tick 提交移动输入
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

        await WaitForDiffCountAsync(observer, expectedMin: 1, timeoutMs: 2000);

        var entityDeltaDiffs = observer.EntityDeltaDiffCount;
        var eventDiffs = observer.EventDiffCount;
        var avgTickMs = sw.Elapsed.TotalMilliseconds / TotalTicks;

        _output.WriteLine(
            $"[Integration-5Client] 5 客户端 @ 真实 Orleans Silo：总计 {sw.Elapsed.TotalMilliseconds:F2}ms，" +
            $"平均 {avgTickMs:F3}ms/tick，EntityDelta diff={entityDeltaDiffs}，Event diff(InputAck)={eventDiffs}");

        // 验证 1：5 客户端场景下 observer 仍能收到 EntityDelta diff（修复前几乎无法同步）
        Assert.True(entityDeltaDiffs > 0,
            $"5 客户端集成测试：应通过 Orleans 运行时收到 EntityDelta diff，实际 {entityDeltaDiffs}。" +
            "修复前此场景几乎无法同步，修复后应能正常广播。");

        // 验证 2：平均 tick 耗时不应堆积（5 客户端 @ 20Hz 降频）
        Assert.True(avgTickMs <= 16.7,
            $"5 客户端集成测试：平均 tick 耗时 {avgTickMs:F3}ms 超过 16.7ms 帧预算（tick 堆积风险）。");

        // 验证 3：Event diff（InputAck）应正常下发（每 tick，不受降频影响）
        Assert.True(eventDiffs > 0,
            $"5 客户端集成测试：应收到 Event diff(InputAck)，实际 {eventDiffs}。");

        // 清理
        await zoneShard.UnsubscribeFanoutAsync(subscriptionId);
    }

    /// <summary>
    /// 集成测试：验证真实 Orleans Silo 中实体注册后能通过 GetRegisteredEntityIdsAsync 查询到。
    /// 确保跨进程 grain 调用与状态持久化正常工作。
    /// </summary>
    [Fact]
    public async Task RegisterEntity_RealOrleans_EntityQueryableAfterActivation()
    {
        Assert.NotNull(_cluster);
        var zoneShard = _cluster!.GrainFactory.GetGrain<IZoneShardGrain>(3);

        // 注册 3 个实体
        for (ulong i = 1; i <= 3; i++)
        {
            await zoneShard.RegisterEntityAsync(entityId: i, initialX: i * 10f, initialY: 0f, initialZ: 0f);
        }

        // 通过跨进程 RPC 查询已注册实体
        var entityIds = await zoneShard.GetRegisteredEntityIdsAsync();

        Assert.NotNull(entityIds);
        Assert.True(entityIds.Length >= 3,
            $"应查询到至少 3 个已注册实体，实际 {entityIds.Length}：[{string.Join(", ", entityIds)}]");

        // 验证 HasEntityAsync（跨进程 RPC）
        Assert.True(await zoneShard.HasEntityAsync(1), "实体 1 应存在");
        Assert.True(await zoneShard.HasEntityAsync(2), "实体 2 应存在");
        Assert.True(await zoneShard.HasEntityAsync(3), "实体 3 应存在");
        Assert.False(await zoneShard.HasEntityAsync(999), "实体 999 应不存在");

        // 验证 GetLoadMetricsAsync（跨进程 RPC）
        var metrics = await zoneShard.GetLoadMetricsAsync();
        Assert.True(metrics.EntityCount >= 3, $"负载指标实体数应 ≥ 3，实际 {metrics.EntityCount}");
    }

    /// <summary>
    /// 集成测试：验证 EnterWorldAsync 原子操作（建立 AOI 订阅 + 注册实体）。
    /// 确保新加入玩家能立即收到全量快照（_forceFullSnapshotNextTick 机制）。
    /// </summary>
    [Fact]
    public async Task EnterWorld_RealOrleans_NewPlayerReceivesFullSnapshot()
    {
        Assert.NotNull(_cluster);
        var zoneShard = _cluster!.GrainFactory.GetGrain<IZoneShardGrain>(4);

        var observer = new IntegrationFanoutObserver();
        var observerRef = _cluster.Client.CreateObjectReference<IZoneShardFanoutObserver>(observer);
        var subscriptionId = Guid.NewGuid();
        await zoneShard.SubscribeFanoutAsync(subscriptionId, observerRef);

        const float ecsZ = 8f;
        var chunkKey = WorldCoord.ToChunkMortonKey(0, 0, ecsZ);

        // 先注册 2 个已存在实体
        for (ulong i = 1; i <= 2; i++)
        {
            await zoneShard.RegisterEntityAsync(entityId: i, initialX: 0, initialY: ecsZ, initialZ: 0);
        }
        await zoneShard.TickAsync(tickTime: 1.0);
        await WaitForDiffCountAsync(observer, expectedMin: 1, timeoutMs: 2000);
        observer.Reset();

        // 新玩家通过 EnterWorldAsync 进入世界（原子操作：AOI 订阅 + 实体注册）
        await zoneShard.EnterWorldAsync(
            sessionId: 100,
            entityId: 3,
            initialX: 0f,
            initialY: ecsZ,
            initialZ: 0f,
            initialInterestChunks: new[] { chunkKey });

        // EnterWorldAsync 会设置 _forceFullSnapshotNextTick，下一次 tick 应下发全量快照
        await zoneShard.TickAsync(tickTime: 2.0);
        await WaitForDiffCountAsync(observer, expectedMin: 1, timeoutMs: 2000);

        var entityDeltaDiffs = observer.EntityDeltaDiffCount;
        Assert.True(entityDeltaDiffs > 0,
            $"新玩家 EnterWorldAsync 后应通过全量快照收到 EntityDelta diff，实际 {entityDeltaDiffs}。");

        // 验证新玩家实体已注册
        Assert.True(await zoneShard.HasEntityAsync(3), "新玩家实体 3 应已注册");

        await zoneShard.UnsubscribeFanoutAsync(subscriptionId);
    }

    /// <summary>
    /// 集成测试：验证租约续约机制在真实 Orleans Silo 中正常工作。
    /// 模拟网关每 20 秒续约一次，确保实体不会因租约过期被误清理。
    /// </summary>
    [Fact]
    public async Task RenewLease_RealOrleans_EntitiesRemainRegistered()
    {
        Assert.NotNull(_cluster);
        var zoneShard = _cluster!.GrainFactory.GetGrain<IZoneShardGrain>(5);

        // 注册 3 个实体
        for (ulong i = 1; i <= 3; i++)
        {
            await zoneShard.RegisterEntityAsync(entityId: i, initialX: 0, initialY: 0, initialZ: 0);
        }

        // 模拟 60 tick（1 秒），每 20 tick 续约一次
        for (int tick = 1; tick <= 60; tick++)
        {
            if (tick % 20 == 0)
            {
                var renewed = await zoneShard.RenewLeaseAsync(new ulong[] { 1, 2, 3 });
                Assert.True(renewed >= 3,
                    $"租约续约应返回 3（所有实体续约成功），实际 {renewed}（tick={tick}）");
            }
            await zoneShard.TickAsync(tickTime: tick * (1.0 / 60.0));
        }

        // 验证所有实体仍注册
        var entityIds = await zoneShard.GetRegisteredEntityIdsAsync();
        Assert.Contains<ulong>(1, entityIds);
        Assert.Contains<ulong>(2, entityIds);
        Assert.Contains<ulong>(3, entityIds);
    }

    /// <summary>
    /// 等待 observer 收到至少 expectedMin 个 diff，或超时返回。
    /// 真实 Orleans 运行时下，grain 回调通过调度器异步执行，需要等待回调送达。
    /// </summary>
    private static async Task WaitForDiffCountAsync(IntegrationFanoutObserver observer, int expectedMin, int timeoutMs)
    {
        var sw = Stopwatch.StartNew();
        while (observer.ReceivedDiffCount < expectedMin && sw.ElapsedMilliseconds < timeoutMs)
        {
            await Task.Delay(10);
        }
    }
}
