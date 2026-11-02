using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Threading;
using System.Threading.Tasks;
using Horizon.Game.Gateway.Configuration;
using Horizon.Game.Gateway.Monitoring;
using Horizon.Game.Gateway.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 阶段 2.5 — DSRegistryMaintenanceHostedService（路由表自愈维护）测试。
/// <para>
/// 覆盖：UseDS 关闭零副作用 / 周期 PruneStale 摘除过期 DS / 存活数 gauge 与累计摘除数指标上报 /
/// 存活数跟随注册变化 / 取消令牌干净退出。时钟注入（可变时钟盒）驱动过期语义，修剪间隔注入为毫秒级加速。
/// </para>
/// <para>
/// 指标断言通过 <see cref="MeterListener.EnableMeasurementEvents"/> 直接挂接 GatewayMetrics 的静态仪表
/// （不依赖仪表创建时序，确定性捕获）。
/// </para>
/// </summary>
public class DSRegistryMaintenanceHostedServiceTests
{
    /// <summary>可变时钟盒：测试通过推进 <see cref="Now"/> 驱动过期语义。</summary>
    private sealed class Clock
    {
        public DateTimeOffset Now = new(2026, 8, 11, 0, 0, 0, TimeSpan.Zero);
    }

    /// <summary>捕获 GatewayMetrics 中 DS 仪表的上报值（UpDownCounter/Counter 的 Add 增量）。</summary>
    private sealed class MetricCapture : IDisposable
    {
        private readonly MeterListener _listener = new();
        public readonly List<(string Name, long Value)> Measurements = new();

        public MetricCapture()
        {
            _listener.SetMeasurementEventCallback<long>((instrument, value, _, _) =>
            {
                lock (Measurements) Measurements.Add((instrument.Name, value));
            });
            _listener.Start();
            // 直接挂接已创建的静态仪表（无论 GatewayMetrics 类型何时初始化，均确定性捕获）。
            _listener.EnableMeasurementEvents(GatewayMetrics.ActiveDsServers);
            _listener.EnableMeasurementEvents(GatewayMetrics.DsServersPrunedTotal);
        }

        public void Dispose() => _listener.Dispose();
    }

    private static readonly TimeSpan FastInterval = TimeSpan.FromMilliseconds(20);

    /// <summary>内存版快照存储（fake）：记录保存调用、可注入恢复内容。</summary>
    private sealed class FakeSnapshotStore : IDSnapshotStore
    {
        public IReadOnlyList<DsEndpointInfo>? RestorePayload;
        public readonly List<IReadOnlyList<DsEndpointInfo>> SavedSnapshots = new();
        public int SaveCount;

        public Task<IReadOnlyList<DsEndpointInfo>?> RestoreSnapshotAsync()
            => Task.FromResult(RestorePayload);

        public Task SaveSnapshotAsync(IReadOnlyList<DsEndpointInfo> entries)
        {
            lock (SavedSnapshots) SavedSnapshots.Add(entries);
            Interlocked.Increment(ref SaveCount);
            return Task.CompletedTask;
        }

        /// <summary>
        /// 最新一份快照里有几条端点；一份都还没有时返回 -1
        /// （返回 0 会让"等到有 3 条"这种条件在空表上误判成没满足但也不报错）。
        /// </summary>
        public int LatestCount()
        {
            lock (SavedSnapshots) return SavedSnapshots.Count == 0 ? -1 : SavedSnapshots[^1].Count;
        }
    }

    private static (DSRegistry Registry, Clock Clock) CreateRegistry()
    {
        var clock = new Clock();
        return (new DSRegistry(staleAfter: TimeSpan.FromSeconds(90), clock: () => clock.Now), clock);
    }

    private static DSRegistryMaintenanceHostedService CreateService(
        DSRegistry registry, bool useDS, TimeSpan? interval = null, IDSnapshotStore? store = null)
        => CreateService(registry, new Mock<IOptionsMonitor<GatewayOptions>>(), useDS, interval, store);

    private static DSRegistryMaintenanceHostedService CreateService(
        DSRegistry registry, Mock<IOptionsMonitor<GatewayOptions>> monitorMock, bool useDS,
        TimeSpan? interval = null, IDSnapshotStore? store = null)
    {
        monitorMock.Setup(m => m.CurrentValue).Returns(new GatewayOptions { UseDS = useDS });
        return new DSRegistryMaintenanceHostedService(
            registry, monitorMock.Object, NullLogger<DSRegistryMaintenanceHostedService>.Instance,
            interval ?? FastInterval, store);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("等待条件超时。");
            await Task.Delay(10);
        }
    }

    [Fact]
    public async Task UseDS_False_DoesNotPrune_ZeroSideEffects()
    {
        var (registry, clock) = CreateRegistry();
        registry.RegisterOrUpdate("ds-1", new long[] { 0 }, "127.0.0.1", 5001);
        var service = CreateService(registry, useDS: false);

        var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);
        // 开关关闭 → ExecuteAsync 立即返回，不进入循环。
        await service.StopAsync(cts.Token);

        // 推进时钟越过过期阈值，但服务从未修剪 → 条目仍在快照中（未被摘除）、指标零上报。
        clock.Now = clock.Now.AddSeconds(200);
        Assert.Single(registry.Snapshot());
        Assert.Equal(0, registry.CountAlive()); // 已过期但残留（无自愈循环）
        Assert.Equal(0, service.LastAliveCount);
        Assert.Equal(0, service.TotalPruned);
    }

    [Fact]
    public async Task UseDS_True_PrunesStale_ReportsAliveAndPrunedMetrics()
    {
        using var capture = new MetricCapture();
        var (registry, clock) = CreateRegistry();
        registry.RegisterOrUpdate("ds-a", new long[] { 0 }, "127.0.0.1", 5001);
        clock.Now = clock.Now.AddSeconds(50);
        registry.RegisterOrUpdate("ds-b", new long[] { 1 }, "127.0.0.1", 5002);
        var service = CreateService(registry, useDS: true);

        var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);

        try
        {
            // 第一轮：2 个 DS 均存活 → gauge +2。
            await WaitUntilAsync(() => service.LastAliveCount == 2);

            // ds-a 过期（t+100 > 90s 阈值）→ 下一轮被摘除：gauge -1，累计摘除 +1。
            clock.Now = clock.Now.AddSeconds(50);
            await WaitUntilAsync(() => service.TotalPruned >= 1 && service.LastAliveCount == 1);

            Assert.False(registry.TryResolve(0, out _)); // ds-a 已摘除
            Assert.True(registry.TryResolve(1, out _));  // ds-b 仍存活
            Assert.Single(registry.Snapshot());

            lock (capture.Measurements)
            {
                Assert.Contains(("hundunworld.gateway.ds.servers.alive", 2L), capture.Measurements);
                Assert.Contains(("hundunworld.gateway.ds.servers.alive", -1L), capture.Measurements);
                Assert.Contains(("hundunworld.gateway.ds.servers.pruned.total", 1L), capture.Measurements);
            }
        }
        finally
        {
            cts.Cancel();
            await service.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task AliveCount_TracksNewRegistrations()
    {
        using var capture = new MetricCapture();
        var (registry, clock) = CreateRegistry();
        registry.RegisterOrUpdate("ds-1", new long[] { 0 }, "127.0.0.1", 5001);
        registry.RegisterOrUpdate("ds-2", new long[] { 1 }, "127.0.0.1", 5002);
        var service = CreateService(registry, useDS: true);

        var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);

        try
        {
            await WaitUntilAsync(() => service.LastAliveCount == 2);

            // 新 DS 注册（心跳新鲜）→ 下一轮存活数 3。
            registry.RegisterOrUpdate("ds-3", new long[] { 2 }, "127.0.0.1", 5003);
            await WaitUntilAsync(() => service.LastAliveCount == 3);
            Assert.Equal(3, registry.CountAlive());

            lock (capture.Measurements)
            {
                Assert.Contains(("hundunworld.gateway.ds.servers.alive", 2L), capture.Measurements);
                Assert.Contains(("hundunworld.gateway.ds.servers.alive", 1L), capture.Measurements);
            }
        }
        finally
        {
            cts.Cancel();
            await service.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Cancellation_ExitsCleanly()
    {
        var (registry, _) = CreateRegistry();
        var service = CreateService(registry, useDS: true);

        var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);
        await WaitUntilAsync(() => service.LastAliveCount == 0); // 已进入循环

        cts.Cancel();
        await service.StopAsync(CancellationToken.None); // 不抛异常即通过
    }

    [Fact]
    public void CountAlive_ExcludesStaleEntries()
    {
        var (registry, clock) = CreateRegistry();
        registry.RegisterOrUpdate("ds-a", new long[] { 0 }, "127.0.0.1", 5001);
        clock.Now = clock.Now.AddSeconds(50);
        registry.RegisterOrUpdate("ds-b", new long[] { 1 }, "127.0.0.1", 5002);

        clock.Now = clock.Now.AddSeconds(50); // t+100s：ds-a 过期、ds-b 存活
        Assert.Equal(1, registry.CountAlive());
        Assert.Equal(2, registry.Snapshot().Count); // 未修剪时快照仍含过期条目
    }

    [Fact]
    public async Task RestoreOnStart_MakesSnapshotEntriesRoutable_WithoutHeartbeat()
    {
        var (registry, clock) = CreateRegistry();
        var store = new FakeSnapshotStore
        {
            // 模拟重启前快照：DS 心跳 10s 前（仍新鲜）。恢复后应立即可路由，无需等下一轮心跳。
            RestorePayload = new[]
            {
                new DsEndpointInfo("ds-1", new long[] { 0 }, "127.0.0.1", 5001, clock.Now.AddSeconds(-10)),
            },
        };
        var service = CreateService(registry, useDS: true, store: store);

        var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);

        try
        {
            // 恢复发生在循环开始前：等待后台 ExecuteAsync 完成恢复后立即可路由
            // （期间未调用任何注册/心跳——快照是唯一来源）。
            await WaitUntilAsync(() => registry.TryResolve(0, out _));
            Assert.True(registry.TryResolve(0, out var ep));
            Assert.Equal("ds-1", ep.DsId);
            await WaitUntilAsync(() => service.LastAliveCount == 1);
        }
        finally
        {
            cts.Cancel();
            await service.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task PersistsSnapshot_EachMaintenanceCycle()
    {
        var (registry, clock) = CreateRegistry();
        var store = new FakeSnapshotStore();
        registry.RegisterOrUpdate("ds-a", new long[] { 0 }, "127.0.0.1", 5001);
        registry.RegisterOrUpdate("ds-b", new long[] { 1 }, "127.0.0.1", 5002);
        var service = CreateService(registry, useDS: true, store: store);

        var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);

        try
        {
            await WaitUntilAsync(() => store.SaveCount >= 2); // 已过 ≥2 个维护周期

            lock (store.SavedSnapshots)
            {
                var latest = store.SavedSnapshots[^1];
                Assert.Equal(2, latest.Count); // 快照内容 = 当前注册表
                Assert.Contains(latest, e => e.DsId == "ds-a" && e.ShardIds.Contains(0));
                Assert.Contains(latest, e => e.DsId == "ds-b" && e.ShardIds.Contains(1));
            }

            // 新注册 → 下一轮快照跟随。
            registry.RegisterOrUpdate("ds-c", new long[] { 2 }, "127.0.0.1", 5003);
            // 等的是"最新那份快照里真的有 3 条"，不是"存储又被调用过一次"。
            // 后者会随机红：注册 ds-c 之前那一轮可能已经读过注册表了，它落盘的那份只有 2 条，
            // 可 SaveCount 照样 +1 —— 于是断言读到的是"上一轮的产物"（实测并行跑 Expected 3 / Actual 2，单跑永远绿）。
            await WaitUntilAsync(() => store.LatestCount() >= 3);
            lock (store.SavedSnapshots)
            {
                Assert.Equal(3, store.SavedSnapshots[^1].Count);
            }
        }
        finally
        {
            cts.Cancel();
            await service.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task UseDS_False_NeverTouchesSnapshotStore()
    {
        var (registry, _) = CreateRegistry();
        var store = new FakeSnapshotStore();
        registry.RegisterOrUpdate("ds-1", new long[] { 0 }, "127.0.0.1", 5001);
        var service = CreateService(registry, useDS: false, store: store);

        var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);
        await service.StopAsync(cts.Token);

        Assert.Equal(0, store.SaveCount);
        Assert.Empty(store.SavedSnapshots);
    }

    [Fact]
    public async Task SnapshotStoreFailures_DegradeGracefully_WithoutBreakingMaintenanceLoop()
    {
        var (registry, clock) = CreateRegistry();
        var failingStore = new FailingSnapshotStore();
        registry.RegisterOrUpdate("ds-1", new long[] { 0 }, "127.0.0.1", 5001);
        var service = CreateService(registry, useDS: true, store: failingStore);

        var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);

        try
        {
            // 存储不可用（恢复抛异常 + 每周期保存抛异常）：维护循环照常运行，
            // 路由降级为「DS 心跳重建」（与无持久化时行为一致），不崩溃。
            await WaitUntilAsync(() => service.LastAliveCount == 1);
            Assert.True(registry.TryResolve(0, out _));

            // 过期摘除照常工作。
            clock.Now = clock.Now.AddSeconds(200);
            await WaitUntilAsync(() => service.TotalPruned >= 1);
            Assert.False(registry.TryResolve(0, out _));
        }
        finally
        {
            cts.Cancel();
            await service.StopAsync(CancellationToken.None);
        }
    }

    /// <summary>存储全故障 fake：恢复与保存均抛异常（模拟 Redis 不可用）。</summary>
    private sealed class FailingSnapshotStore : IDSnapshotStore
    {
        public Task<IReadOnlyList<DsEndpointInfo>?> RestoreSnapshotAsync()
            => throw new InvalidOperationException("Redis 不可用");

        public Task SaveSnapshotAsync(IReadOnlyList<DsEndpointInfo> entries)
            => throw new InvalidOperationException("Redis 不可用");
    }

    [Fact]
    public async Task HotSwitch_FalseToTrue_StartsMaintenance_TrueToFalse_Stops()
    {
        var (registry, clock) = CreateRegistry();
        // 可翻转 monitor 直构（MutableOptionsMonitor 实现 IOptionsMonitor，模拟配置热重载）。
        var monitor = new MutableOptionsMonitor(new GatewayOptions { UseDS = false });
        var hotService = new DSRegistryMaintenanceHostedService(
            registry, monitor, NullLogger<DSRegistryMaintenanceHostedService>.Instance, FastInterval);
        var cts = new CancellationTokenSource();
        await hotService.StartAsync(cts.Token);

        try
        {
            // ① 启动时 UseDS=false：空闲不修剪。
            registry.RegisterOrUpdate("ds-a", new long[] { 0 }, "127.0.0.1", 5001);
            await Task.Delay(FastInterval * 3); // 空转 ≥3 个周期
            clock.Now = clock.Now.AddSeconds(200); // 越过过期阈值
            await Task.Delay(FastInterval * 3);
            Assert.Equal(0, hotService.TotalPruned); // 空闲：未修剪
            Assert.Equal(0, hotService.LastAliveCount); // 未上报存活

            // ② 热开（仅改配置）：自动进入维护——修剪过期 DS、上报存活。
            monitor.CurrentValue = new GatewayOptions { UseDS = true };
            await WaitUntilAsync(() => hotService.TotalPruned >= 1 && hotService.LastAliveCount == 0);
            Assert.False(registry.TryResolve(0, out _)); // 过期条目已被摘除
            Assert.Equal(1, hotService.TotalPruned);

            // ③ 热关：维护停止——新注册的 DS 过期后不再被修剪。
            monitor.CurrentValue = new GatewayOptions { UseDS = false };
            await Task.Delay(FastInterval * 3); // 等到服务感知关闭
            registry.RegisterOrUpdate("ds-b", new long[] { 1 }, "127.0.0.1", 5002);
            clock.Now = clock.Now.AddSeconds(200); // ds-b 过期
            await Task.Delay(FastInterval * 3);
            Assert.Equal(1, hotService.TotalPruned); // 未再修剪（停止维护）
            // 过期条目残留（无自愈循环）：Snapshot 仍含 ds-b（TryResolve 对过期条目本就返回 false，此处以 Snapshot 为准）。
            var snapshot = registry.Snapshot();
            Assert.Single(snapshot); // ds-a 已在阶段②被修剪，此时仅剩未修剪的 ds-b
            Assert.Contains(snapshot, e => e.DsId == "ds-b");
        }
        finally
        {
            cts.Cancel();
            await hotService.StopAsync(CancellationToken.None);
        }
    }
}
