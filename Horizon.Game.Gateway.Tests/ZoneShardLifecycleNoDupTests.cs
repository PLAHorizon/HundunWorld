using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Horizon.Game.Core.World;
using Horizon.Game.Message.Sync;
using Horizon.Orleans.Interface.World;
using MemoryPack;
using Orleans.TestingHost;
using Xunit;
using Xunit.Abstractions;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 网关下行去重回归测试（修复 A + 修复 B，见 .codeartsdoer/plans/fix-gateway-downlink-dup/plan.md §4/§6.3）。
/// 在真实 Orleans Silo 中驱动 ZoneShardGrain 生命周期广播，断言：
/// <list type="number">
///   <item>RegisterEntityAsync（Spawn）只下发一次 —— 不再出现"AOI chunk 广播 + 全广播"双重入队；</item>
///   <item>UnregisterEntityAsync（Despawn）只下发一次；</item>
///   <item>多订阅者场景下每个订阅 session 各自只收到一次 Spawn/Despawn；</item>
///   <item>纯周期全量快照恢复为 Update Kind（仅首次/强制全量携带 Spawn）。</item>
/// </list>
/// 背景：修复 A 前，BroadcastEntityLifecycleAsync 中同一 triggerDiff 对 AOI chunk 订阅者与全部订阅者
/// 各入队一次（受众重叠），视野内客户端每次 Spawn/Despawn 收到两条字节完全相同的数据。
/// </summary>
public class ZoneShardLifecycleNoDupTests : IAsyncLifetime
{
    private TestCluster? _cluster;
    private readonly ITestOutputHelper _output;

    public ZoneShardLifecycleNoDupTests(ITestOutputHelper output)
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
    /// 记录全部 (diff, 受众 sessionIds) 的 fanout observer，按 (session, entity, kind) 维度统计 delta。
    /// </summary>
    private sealed class NoDupObserver : IZoneShardFanoutObserver
    {
        private readonly ConcurrentQueue<(WorldChunkDiffPacket Diff, IReadOnlyCollection<long> SessionIds)> _items = new();

        public int ItemCount => _items.Count;

        public Task OnChunkDiffAsync(WorldChunkDiffPacket diff, IReadOnlyCollection<long> sessionIds)
        {
            _items.Enqueue((diff, sessionIds));
            return Task.CompletedTask;
        }

        public void Reset()
        {
            while (_items.TryDequeue(out _)) { }
        }

        /// <summary>
        /// 统计指定 session 作为受众时，收到的含 <paramref name="entityId"/> 且 Kind=<paramref name="kind"/>
        /// 的 EntityDelta 条数（跨多条 diff 累加）。
        /// </summary>
        public int CountDeltasForSession(long sessionId, ulong entityId, EntityDeltaKind kind)
        {
            int count = 0;
            foreach (var (diff, sessionIds) in _items)
            {
                if (sessionIds == null || !sessionIds.Contains(sessionId))
                    continue;
                if (diff.PayloadType != WorldChunkDiffPayloadType.EntityDelta
                    || diff.Payload == null
                    || diff.Payload.Length == 0)
                    continue;

                EntityDelta[] deltas;
                try
                {
                    deltas = MemoryPackSerializer.Deserialize<EntityDelta[]>(diff.Payload)!;
                }
                catch
                {
                    continue;
                }
                if (deltas == null)
                    continue;
                count += deltas.Count(d => d.EntityId == entityId && d.Kind == kind);
            }
            return count;
        }
    }

    /// <summary>建立 cluster 连接 + fanout observer + 指定 session 订阅 AOI chunk。</summary>
    private async Task<(IZoneShardGrain Grain, NoDupObserver Observer, Guid SubId)> SetupAsync(
        long grainKey, params long[] sessionIds)
    {
        var zoneShard = _cluster!.GrainFactory.GetGrain<IZoneShardGrain>(grainKey);
        var observer = new NoDupObserver();
        var observerRef = _cluster.Client.CreateObjectReference<IZoneShardFanoutObserver>(observer);
        var subscriptionId = Guid.NewGuid();
        await zoneShard.SubscribeFanoutAsync(subscriptionId, observerRef);

        const float ecsZ = 8f; // Flax Y-up: y=8 → ECS Z-up: z=8
        var chunkKey = WorldCoord.ToChunkMortonKey(0, 0, ecsZ);
        foreach (var sid in sessionIds)
        {
            await zoneShard.SubscribeSessionAsync(sid, new[] { chunkKey });
        }
        return (zoneShard, observer, subscriptionId);
    }

    /// <summary>轮询等待条件满足或超时（真实 Orleans 回调经调度器异步送达）。</summary>
    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 3000)
    {
        var sw = Stopwatch.StartNew();
        while (!condition() && sw.ElapsedMilliseconds < timeoutMs)
        {
            await Task.Delay(10);
        }
    }

    /// <summary>
    /// 用例 1（AC1/根因 A）：AOI 订阅者注册实体后，同一 session 收到该实体 Spawn delta 恰好 1 条。
    /// 修复 A 前该 session 会收到 2 条（AOI chunk 广播 + 全广播，受众重叠）。
    /// </summary>
    [Fact]
    public async Task RegisterEntity_NoDuplicateSpawn_ForAoiSubscriber()
    {
        const float ecsZ = 8f;
        var (zoneShard, observer, subId) = await SetupAsync(8001, 1);

        // session 1 已订阅实体所在 chunk（SetupAsync）。注册实体 100 → 生命周期 Spawn 全广播。
        await zoneShard.RegisterEntityAsync(entityId: 100, initialX: 0, initialY: ecsZ, initialZ: 0);

        await WaitUntilAsync(() => observer.CountDeltasForSession(1, 100, EntityDeltaKind.Spawn) >= 1);
        var spawnCount = observer.CountDeltasForSession(1, 100, EntityDeltaKind.Spawn);

        _output.WriteLine($"[NoDup-Spawn] session 1 收到 EntityId=100 的 Spawn delta = {spawnCount}");

        Assert.Equal(1, spawnCount);

        await zoneShard.UnsubscribeFanoutAsync(subId);
    }

    /// <summary>
    /// 用例 2（AC2/根因 A）：注销实体后，AOI 订阅 session 收到该实体 Despawn delta 恰好 1 条。
    /// </summary>
    [Fact]
    public async Task UnregisterEntity_NoDuplicateDespawn()
    {
        const float ecsZ = 8f;
        var (zoneShard, observer, subId) = await SetupAsync(8002, 1);

        await zoneShard.RegisterEntityAsync(entityId: 100, initialX: 0, initialY: ecsZ, initialZ: 0);
        await WaitUntilAsync(() => observer.CountDeltasForSession(1, 100, EntityDeltaKind.Spawn) >= 1);
        observer.Reset();

        await zoneShard.UnregisterEntityAsync(entityId: 100);

        await WaitUntilAsync(() => observer.CountDeltasForSession(1, 100, EntityDeltaKind.Despawn) >= 1);
        var despawnCount = observer.CountDeltasForSession(1, 100, EntityDeltaKind.Despawn);

        _output.WriteLine($"[NoDup-Despawn] session 1 收到 EntityId=100 的 Despawn delta = {despawnCount}");

        Assert.Equal(1, despawnCount);

        await zoneShard.UnsubscribeFanoutAsync(subId);
    }

    /// <summary>
    /// 用例 3（§6.3）：两个 session 订阅同一 chunk，各自只收到 1 条 Spawn / 1 条 Despawn。
    /// </summary>
    [Fact]
    public async Task MultipleSubscribers_EachReceivesSingleSpawnAndDespawn()
    {
        const float ecsZ = 8f;
        var (zoneShard, observer, subId) = await SetupAsync(8003, 1, 2);

        // 注册实体 200 → 全广播 Spawn 给 session 1、2
        await zoneShard.RegisterEntityAsync(entityId: 200, initialX: 0, initialY: ecsZ, initialZ: 0);

        await WaitUntilAsync(
            () => observer.CountDeltasForSession(1, 200, EntityDeltaKind.Spawn) >= 1
                  && observer.CountDeltasForSession(2, 200, EntityDeltaKind.Spawn) >= 1);

        Assert.Equal(1, observer.CountDeltasForSession(1, 200, EntityDeltaKind.Spawn));
        Assert.Equal(1, observer.CountDeltasForSession(2, 200, EntityDeltaKind.Spawn));

        observer.Reset();

        // 注销实体 200 → 全广播 Despawn 给 session 1、2
        await zoneShard.UnregisterEntityAsync(entityId: 200);

        await WaitUntilAsync(
            () => observer.CountDeltasForSession(1, 200, EntityDeltaKind.Despawn) >= 1
                  && observer.CountDeltasForSession(2, 200, EntityDeltaKind.Despawn) >= 1);

        Assert.Equal(1, observer.CountDeltasForSession(1, 200, EntityDeltaKind.Despawn));
        Assert.Equal(1, observer.CountDeltasForSession(2, 200, EntityDeltaKind.Despawn));

        _output.WriteLine(
            $"[NoDup-MultiSub] Spawn(s1={observer.CountDeltasForSession(1, 200, EntityDeltaKind.Spawn)}," +
            $" s2={observer.CountDeltasForSession(2, 200, EntityDeltaKind.Spawn)}), " +
            $"Despawn(s1={observer.CountDeltasForSession(1, 200, EntityDeltaKind.Despawn)}," +
            $" s2={observer.CountDeltasForSession(2, 200, EntityDeltaKind.Despawn)})");

        await zoneShard.UnsubscribeFanoutAsync(subId);
    }

    /// <summary>
    /// 用例 4（修复 B）：纯周期全量快照恢复 Update Kind —— 首次全量携带 Spawn（自愈保留），
    /// 后续周期全量（FullSnapshotIntervalTicks=60）不再对已存在实体携带 Spawn，避免与生命周期 Spawn 叠加。
    /// </summary>
    [Fact]
    public async Task PeriodicFullSnapshot_ExistingEntity_NoExtraSpawn()
    {
        const float ecsZ = 8f;
        var (zoneShard, observer, subId) = await SetupAsync(8004, 1);

        // 注册实体 300 → 触发生命周期 Spawn + 下一 tick 强制全量（首次，Spawn）。
        await zoneShard.RegisterEntityAsync(entityId: 300, initialX: 0, initialY: ecsZ, initialZ: 0);
        await zoneShard.TickAsync(tickTime: 1.0); // 首次全量（_lastSnapshot == null → isFullSpawnTrigger=true）

        // 等待首次全量送达后清零计数，只统计后续周期全量。
        await WaitUntilAsync(() => observer.CountDeltasForSession(1, 300, EntityDeltaKind.Spawn) >= 1);
        observer.Reset();

        // 跑 70 tick（> FullSnapshotIntervalTicks=60），确保至少触发一次纯周期全量快照。
        for (int t = 1; t <= 70; t++)
        {
            await zoneShard.TickAsync(tickTime: 1.0 + t * (1.0 / 60.0));
        }

        // 等待收到该实体的 Update delta（周期全量应携带 Update Kind）。
        await WaitUntilAsync(() => observer.CountDeltasForSession(1, 300, EntityDeltaKind.Update) >= 1);

        var spawnAfterPeriodic = observer.CountDeltasForSession(1, 300, EntityDeltaKind.Spawn);
        var updateAfterPeriodic = observer.CountDeltasForSession(1, 300, EntityDeltaKind.Update);

        _output.WriteLine(
            $"[NoDup-PeriodicFull] 周期全量阶段：Spawn={spawnAfterPeriodic}, Update={updateAfterPeriodic}");

        // 修复 B：周期全量不得再次携带 Spawn（否则与生命周期 Spawn 叠加，触发"重复 Spawn 下发"）。
        Assert.True(spawnAfterPeriodic == 0,
            $"纯周期全量快照不应再对已存在实体携带 Spawn，实际 {spawnAfterPeriodic} 条（修复 B 未生效）。");
        Assert.True(updateAfterPeriodic >= 1,
            $"纯周期全量快照应以 Update Kind 下发该实体，实际 Update={updateAfterPeriodic} 条。");

        await zoneShard.UnsubscribeFanoutAsync(subId);
    }
}
