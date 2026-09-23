using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Horizon.Game.Core.Observability;
using Horizon.Game.Gateway.Configuration;
using Horizon.Game.Gateway.Services;
using Horizon.Strategy.Storage.Redis;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 阶段 6.3 P2 — 修正率信号跨进程汇聚（Silo→Redis→网关）测试。
/// <para>
/// 覆盖：①端到端——真实 <see cref="CorrectionPublisher"/>（Silo 侧逻辑）捕获真实
/// <c>horizon.sync.corrections</c> 仪表 → store（内存实现代理 Redis 汇聚）→ 真实
/// <see cref="RollbackWindowSampler"/>（网关侧窗口增量）→ 判定宿主连续坏窗口触发回滚；
/// ②采样器增量语义——首读基线化（不把历史总量当窗口增量）/跨窗 delta/重启重基线/
/// 读失败置 0 不丢信号；③Redis 存储键格式（Silo 写与网关读必须一致）。
/// </para>
/// <para>
/// 本类记录 <c>horizon.sync.corrections</c> / <c>horizon.sync.ds.rollbacks.*</c> 静态仪表，
/// 挂 <see cref="SyncMetricsIsolatedCollection"/> 串行集合。
/// </para>
/// </summary>
[Collection(SyncMetricsIsolatedCollection.Name)]
public class CorrectionSignalRollbackTests
{
    /// <summary>内存版修正计数存储（代理 Redis 汇聚；仅测试用）。</summary>
    private sealed class MemoryCorrectionStore : ICorrectionCounterStore
    {
        private long _total;
        private readonly Dictionary<string, long> _byReason = new();

        public Task<long> GetCorrectionsTotalAsync() => Task.FromResult(_total);

        public Task IncrementCorrectionsAsync(long delta)
        {
            if (delta > 0) _total += delta;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyDictionary<string, long>> GetCorrectionsByReasonAsync()
            => Task.FromResult<IReadOnlyDictionary<string, long>>(new Dictionary<string, long>(_byReason));

        public Task IncrementCorrectionsByReasonAsync(IReadOnlyDictionary<string, long> deltas)
        {
            foreach (var kv in deltas)
            {
                if (kv.Value <= 0) continue;
                _byReason.TryGetValue(kv.Key, out var cur);
                _byReason[kv.Key] = cur + kv.Value;
            }
            return Task.CompletedTask;
        }
    }

    /// <summary>读侧可注入失败的存储（验证采样器读失败 fail-open + 不丢差量）。</summary>
    private sealed class FlakyStore : ICorrectionCounterStore
    {
        private long _total;
        private readonly Dictionary<string, long> _byReason = new();
        public bool FailNextRead;

        public Task<long> GetCorrectionsTotalAsync()
        {
            if (FailNextRead)
            {
                FailNextRead = false;
                throw new InvalidOperationException("模拟 Redis 读故障");
            }
            return Task.FromResult(_total);
        }

        public Task IncrementCorrectionsAsync(long delta)
        {
            if (delta > 0) _total += delta;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyDictionary<string, long>> GetCorrectionsByReasonAsync()
        {
            if (FailNextRead)
            {
                FailNextRead = false;
                throw new InvalidOperationException("模拟 Redis 读故障");
            }
            return Task.FromResult<IReadOnlyDictionary<string, long>>(new Dictionary<string, long>(_byReason));
        }

        public Task IncrementCorrectionsByReasonAsync(IReadOnlyDictionary<string, long> deltas)
        {
            foreach (var kv in deltas)
            {
                if (kv.Value <= 0) continue;
                _byReason.TryGetValue(kv.Key, out var cur);
                _byReason[kv.Key] = cur + kv.Value;
            }
            return Task.CompletedTask;
        }
    }

    private sealed class Clock
    {
        public DateTimeOffset Now = new(2026, 8, 11, 0, 0, 0, TimeSpan.Zero);
    }

    private static GatewayOptions Options(long? correctionsPerWindow = null)
        => new()
        {
            UseDS = true,
            DsRollback = new GatewayOptions.DsRollbackOptions
            {
                WindowSeconds = 30,
                MinAttemptsPerWindow = 10,
                FallbackRateThreshold = 0.5,
                LatencyMsThreshold = 1000,
                CorrectionsPerWindowThreshold = correctionsPerWindow ?? 0,
                ConsecutiveBadWindowsToTrigger = 2,
                ConsecutiveHealthyWindowsToRecover = 3,
                OverrideCooldownSeconds = 60,
            },
        };

    // ── 端到端：Silo 发布 → store 汇聚 → 网关采样 → 门触发 ─────────────────

    [Fact]
    public async Task CorrectionSignal_EndToEnd_PublisherToStoreToSamplerToGate_TriggersRollback()
    {
        var store = new MemoryCorrectionStore();
        var clock = new Clock();
        var monitor = new MutableOptionsMonitor(Options(correctionsPerWindow: 50));
        var coordinator = new RollbackCoordinator(monitor, () => clock.Now);

        // ① Silo 侧：真实发布器捕获真实静态仪表（reason 维度增量也计入总量）。
        using var publisher = new CorrectionPublisher(store);
        publisher.Start();
        SyncMetrics.Corrections.Add(60, new KeyValuePair<string, object?>("reason", "AccelerationExceeded"));
        Assert.Equal(60, publisher.Pending);
        Assert.Equal(60, await publisher.FlushAsync());
        Assert.Equal(60, await store.GetCorrectionsTotalAsync());

        // ② 网关侧：真实采样器注入 store——首窗口基线化（历史总量不当窗口增量）。
        var sampler = new RollbackWindowSampler(NullLogger<RollbackWindowSampler>.Instance, store);
        sampler.EnsureWatching();
        var w1 = await sampler.SampleWindowAsync();
        Assert.Equal(0, w1.Corrections);

        // ③ 下一个采样窗口：Silo 又产生 60 条修正并发布 → 窗口增量 60（≥ 阈值 50）。
        SyncMetrics.Corrections.Add(60);
        Assert.Equal(60, await publisher.FlushAsync());
        var w2 = await sampler.SampleWindowAsync();
        Assert.Equal(60, w2.Corrections);
        Assert.Equal(0, w2.Attempts); // 修正信号独立于网关路由量

        SyncMetrics.Corrections.Add(60);
        await publisher.FlushAsync();
        var w3 = await sampler.SampleWindowAsync();
        Assert.Equal(60, w3.Corrections);

        // ④ 判定宿主：连续 2 个修正超阈窗口 → Arm（数据面强制回切）。
        var service = new RollbackGateHostedService(coordinator, sampler, monitor,
            NullLogger<RollbackGateHostedService>.Instance);
        service.EvaluateWindowForTest(w2, Options(correctionsPerWindow: 50));
        Assert.False(coordinator.IsOverrideArmed);
        Assert.StartsWith("bad(", service.LastDecision);

        service.EvaluateWindowForTest(w3, Options(correctionsPerWindow: 50));
        Assert.True(coordinator.IsOverrideArmed);
        Assert.True(coordinator.IsRollbackActive);

        // ⑤ 修正率回落 → 冷却到期 → 连续健康窗口自愈（完整闭环）。
        clock.Now = clock.Now.AddSeconds(61);
        var healthy = new RollbackWindow(Attempts: 100, Fallbacks: 0, Corrections: 10, LatencySumMs: 500, LatencyCount: 100);
        service.EvaluateWindowForTest(healthy, Options(correctionsPerWindow: 50));
        service.EvaluateWindowForTest(healthy, Options(correctionsPerWindow: 50));
        service.EvaluateWindowForTest(healthy, Options(correctionsPerWindow: 50));
        Assert.False(coordinator.IsOverrideArmed);
    }

    // ── 采样器增量语义 ───────────────────────────────────────────────────

    [Fact]
    public async Task Sampler_FirstReadBaselines_NoHistorySpike()
    {
        var store = new MemoryCorrectionStore();
        await store.IncrementCorrectionsAsync(1000); // Silo 早已在计数（历史总量）

        var sampler = new RollbackWindowSampler(NullLogger<RollbackWindowSampler>.Instance, store);
        sampler.EnsureWatching();

        var w1 = await sampler.SampleWindowAsync();
        Assert.Equal(0, w1.Corrections); // 首读基线化：1000 历史不算窗口增量

        await store.IncrementCorrectionsAsync(25);
        var w2 = await sampler.SampleWindowAsync();
        Assert.Equal(25, w2.Corrections);

        await store.IncrementCorrectionsAsync(75);
        var w3 = await sampler.SampleWindowAsync();
        Assert.Equal(75, w3.Corrections); // 跨窗 delta 各自独立
    }

    [Fact]
    public async Task Sampler_Restart_ResetsBaseline_NoOfflineAccumulationSpike()
    {
        var store = new MemoryCorrectionStore();
        var sampler = new RollbackWindowSampler(NullLogger<RollbackWindowSampler>.Instance, store);
        sampler.EnsureWatching();
        await sampler.SampleWindowAsync(); // 基线
        await store.IncrementCorrectionsAsync(30);
        Assert.Equal(30, (await sampler.SampleWindowAsync()).Corrections);

        sampler.Stop();
        // 重启（服务重启/热切）：离线期间 Silo 继续产生修正 → 总量暴增；重启后首窗口必须重新基线化。
        await store.IncrementCorrectionsAsync(10_000);
        sampler.EnsureWatching();
        var w = await sampler.SampleWindowAsync();
        Assert.Equal(0, w.Corrections);

        await store.IncrementCorrectionsAsync(5);
        Assert.Equal(5, (await sampler.SampleWindowAsync()).Corrections);
    }

    [Fact]
    public async Task Sampler_StoreReadFailure_CorrectionsZero_NextDeltaUncorrupted()
    {
        var store = new FlakyStore();
        var sampler = new RollbackWindowSampler(NullLogger<RollbackWindowSampler>.Instance, store);
        sampler.EnsureWatching();
        await sampler.SampleWindowAsync(); // 基线

        await store.IncrementCorrectionsAsync(50);
        store.FailNextRead = true;
        var w1 = await sampler.SampleWindowAsync();
        Assert.Equal(0, w1.Corrections); // 读故障 → 本窗口修正信号置 0（fail-open，不误触发）

        var w2 = await sampler.SampleWindowAsync();
        Assert.Equal(50, w2.Corrections); // 下次成功读取：差量未丢（基线未更新）
    }

    // ── Redis 存储 ───────────────────────────────────────────────────────

    [Fact]
    public void RedisStore_KeyFormat_MustMatchSiloWriter()
    {
        // Silo 写侧（RedisCorrectionCounterStore）与网关读侧共用同一键模板；
        // 键格式漂移即「网关读不到修正」——固化断言防静默漂移。
        Assert.Equal("cluster:dev:sync:corrections:total", RedisCorrectionCounterStore.GetKey("dev"));
        Assert.Equal("cluster:prod-1:sync:corrections:total", RedisCorrectionCounterStore.GetKey("prod-1"));
    }

    [Fact]
    public void RedisStore_EmptyConnectionString_Throws()
    {
        Assert.Throws<ArgumentException>(() => new RedisCorrectionCounterStore("", "dev"));
        Assert.Throws<ArgumentException>(() => new RedisCorrectionCounterStore(null!, "dev"));
    }
}
