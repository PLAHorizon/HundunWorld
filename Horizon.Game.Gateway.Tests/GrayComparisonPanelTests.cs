using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Threading.Tasks;
using Horizon.Game.Core.Observability;
using Horizon.Game.Gateway.Configuration;
using Horizon.Game.Gateway.Services;
using Horizon.Strategy.Storage.Redis;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 阶段 6.3 P2⑨ — 灰度对比面板测试。
/// <para>
/// 覆盖：①发布器（Silo 侧）按 reason 捕获并双通道上送（总数 + 按 reason 哈希），通道失败加回重试；
/// ②面板窗口采样——attempts 按 result / fallback 按 reason 的仪表增量聚合 + 按 reason 修正计数
/// 跨进程增量（首读基线化）；③RecordWindow 输出到三个面板 gauge（可被 MeterListener 捕获断言）；
/// ④Redis 按 reason 键格式固化（Silo 写与网关读必须一致）。
/// </para>
/// <para>
/// 本类记录 <c>horizon.sync.corrections</c> / <c>horizon.sync.ds.route.*</c> / 面板 gauge 静态仪表，
/// 挂 <see cref="SyncMetricsIsolatedCollection"/> 串行集合。
/// </para>
/// </summary>
[Collection(SyncMetricsIsolatedCollection.Name)]
public class GrayComparisonPanelTests
{
    /// <summary>内存版修正计数存储（总数 + 按 reason 双通道；仅测试用）。</summary>
    private class MemoryStore : ICorrectionCounterStore
    {
        private long _total;
        private readonly Dictionary<string, long> _byReason = new();

        public long Total => _total;
        public IReadOnlyDictionary<string, long> ByReason => new Dictionary<string, long>(_byReason);

        public Task<long> GetCorrectionsTotalAsync() => Task.FromResult(_total);

        public Task IncrementCorrectionsAsync(long delta)
        {
            if (delta > 0) _total += delta;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyDictionary<string, long>> GetCorrectionsByReasonAsync()
            => Task.FromResult<IReadOnlyDictionary<string, long>>(new Dictionary<string, long>(_byReason));

        public virtual Task IncrementCorrectionsByReasonAsync(IReadOnlyDictionary<string, long> deltas)
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

    /// <summary>按 reason 写侧可注入失败的存储（验证发布器通道失败加回重试）。</summary>
    private sealed class ReasonWriteFlakyStore : MemoryStore
    {
        public bool FailNextReasonWrite;

        public override Task IncrementCorrectionsByReasonAsync(IReadOnlyDictionary<string, long> deltas)
        {
            if (FailNextReasonWrite)
            {
                FailNextReasonWrite = false;
                throw new InvalidOperationException("模拟 Redis HINCRBY 故障");
            }
            return base.IncrementCorrectionsByReasonAsync(deltas);
        }
    }

    private static GrayComparisonHostedService CreatePanel(ICorrectionCounterStore? store, int windowSeconds = 30)
        => new(
            new MutableOptionsMonitor(new GatewayOptions
            {
                UseDS = true,
                DsRollback = new GatewayOptions.DsRollbackOptions { WindowSeconds = windowSeconds },
            }),
            NullLogger<GrayComparisonHostedService>.Instance,
            store);

    // ── ① 发布器按 reason ──────────────────────────────────────────────

    [Fact]
    public async Task Publisher_WithReasonTags_FlushesPerReasonAndTotal()
    {
        var store = new MemoryStore();
        using var publisher = new CorrectionPublisher(store, NullLogger.Instance);
        publisher.Start();

        SyncMetrics.Corrections.Add(1, new KeyValuePair<string, object?>("reason", "AccelerationExceeded"));
        SyncMetrics.Corrections.Add(1, new KeyValuePair<string, object?>("reason", "AccelerationExceeded"));
        SyncMetrics.Corrections.Add(1, new KeyValuePair<string, object?>("reason", "JumpCountExceeded"));

        Assert.Equal(3, publisher.Pending);
        Assert.Equal(3, await publisher.FlushAsync());

        Assert.Equal(3, store.Total); // 总数通道完整
        Assert.Equal(2, store.ByReason["AccelerationExceeded"]); // 按 reason 通道完整
        Assert.Equal(1, store.ByReason["JumpCountExceeded"]);
        Assert.Equal(0, publisher.Pending);
        Assert.Empty(publisher.PendingByReason);
    }

    [Fact]
    public async Task Publisher_UntaggedCorrection_CountsTotalOnly()
    {
        var store = new MemoryStore();
        using var publisher = new CorrectionPublisher(store, NullLogger.Instance);
        publisher.Start();

        // 无 reason 标签的修正：只进总数，不进按 reason 哈希（两条通道各自完整，总数 ≥ 按 reason 求和）。
        SyncMetrics.Corrections.Add(5);

        Assert.Equal(5, await publisher.FlushAsync());
        Assert.Equal(5, store.Total);
        Assert.Empty(store.ByReason);
    }

    [Fact]
    public async Task Publisher_ReasonChannelFailure_ReaddsBothChannels_RetrySucceeds()
    {
        var store = new ReasonWriteFlakyStore();
        using var publisher = new CorrectionPublisher(store, NullLogger.Instance);
        publisher.Start();

        SyncMetrics.Corrections.Add(1, new KeyValuePair<string, object?>("reason", "AccelerationExceeded"));
        SyncMetrics.Corrections.Add(2, new KeyValuePair<string, object?>("reason", "JumpCountExceeded"));
        store.FailNextReasonWrite = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => publisher.FlushAsync());
        // 按 reason 通道失败：本批已排空但两条通道都未落盘——总数增量与按 reason 增量一并加回，下轮重试。
        Assert.Equal(0, store.Total);
        Assert.Equal(3, publisher.Pending);
        Assert.Equal(1, publisher.PendingByReason["AccelerationExceeded"]);
        Assert.Equal(2, publisher.PendingByReason["JumpCountExceeded"]);

        // 重试：两条通道都落盘，且不丢不重。
        Assert.Equal(3, await publisher.FlushAsync());
        Assert.Equal(3, store.Total);
        Assert.Equal(1, store.ByReason["AccelerationExceeded"]);
        Assert.Equal(2, store.ByReason["JumpCountExceeded"]);
    }

    // ── ② 面板窗口采样 ─────────────────────────────────────────────────

    [Fact]
    public async Task Panel_Window_AggregatesRouteByTags_AndCorrectionsDelta()
    {
        var store = new MemoryStore();
        var panel = CreatePanel(store);
        panel.StartListening();

        // 路由仪表：记录带维度标签的 attempts/fallback（真实 DsSyncDataPlane 语义）。
        SyncMetrics.DsRouteAttempts.Add(1, new KeyValuePair<string, object?>("result", "success"));
        SyncMetrics.DsRouteAttempts.Add(1, new KeyValuePair<string, object?>("result", "no_ds"));
        SyncMetrics.DsRouteAttempts.Add(1, new KeyValuePair<string, object?>("result", "no_ds"));
        SyncMetrics.DsRouteFallback.Add(1, new KeyValuePair<string, object?>("reason", "no_ds"));
        SyncMetrics.DsRouteFallback.Add(2, new KeyValuePair<string, object?>("reason", "timeout"));

        // 修正计数：预置历史总量 → 首个窗口基线化（增量 0）→ 再增长 → 下一窗口按 reason 增量。
        await store.IncrementCorrectionsByReasonAsync(new Dictionary<string, long> { ["AccelerationExceeded"] = 10 });
        var first = await panel.SampleWindowAsync();
        Assert.Empty(first.CorrectionsByReason); // 首读基线化：历史总量不当窗口增量

        await store.IncrementCorrectionsByReasonAsync(new Dictionary<string, long>
        {
            ["AccelerationExceeded"] = 3,
            ["JumpCountExceeded"] = 2,
        });
        var second = await panel.SampleWindowAsync();
        Assert.Equal(3, second.CorrectionsByReason["AccelerationExceeded"]); // 窗口增量（13−10）
        Assert.Equal(2, second.CorrectionsByReason["JumpCountExceeded"]);    // 新出现 reason 记全量

        // 路由聚合：attempts 按 result、fallback 按 reason 分桶求和（首个窗口即消费，第二个窗口已空）。
        var attempts = first.AttemptsByResult;
        Assert.Equal(1, attempts["success"]);
        Assert.Equal(2, attempts["no_ds"]);
        var fallback = first.FallbackByReason;
        Assert.Equal(1, fallback["no_ds"]);
        Assert.Equal(2, fallback["timeout"]);
        Assert.Empty(second.AttemptsByResult); // 消费后清空
        Assert.Empty(second.FallbackByReason);
        panel.StopListening();
    }

    [Fact]
    public async Task Panel_Window_ReadFailure_EmptyCorrections_KeepsBaseline()
    {
        // 读失败：本窗口修正信号置空，且基线不更新——下次成功读取仍能算出差量（不丢信号）。
        var failing = new FlakyReadStore();
        var panel = CreatePanel(failing);
        panel.StartListening();

        await failing.SeedAsync(5);
        _ = await panel.SampleWindowAsync(); // 基线化（正常读）

        await failing.SeedAsync(7); // 新增 2
        failing.FailNextRead = true;
        var failedWindow = await panel.SampleWindowAsync();
        Assert.Empty(failedWindow.CorrectionsByReason); // 读失败 → 空

        var next = await panel.SampleWindowAsync();
        Assert.Equal(2, next.CorrectionsByReason["AccelerationExceeded"]); // 基线未动，差量不丢
        panel.StopListening();
    }

    [Fact]
    public async Task Panel_NoCorrectionStore_OnlyRouteBreakdown()
    {
        var panel = CreatePanel(null);
        panel.StartListening();
        SyncMetrics.DsRouteAttempts.Add(1, new KeyValuePair<string, object?>("result", "success"));
        var window = await panel.SampleWindowAsync();
        Assert.Equal(1, window.AttemptsByResult["success"]);
        Assert.Empty(window.CorrectionsByReason); // 未注入 store → 修正信号缺位
        panel.StopListening();
    }

    // ── ③ 面板 gauge 输出 ──────────────────────────────────────────────

    [Fact]
    public void Panel_RecordWindow_EmitsGaugesWithTags()
    {
        // RecordWindow 是快照的纯函数：直接构造快照断言三路 gauge 输出（采样正确性由窗口聚合测试覆盖）。
        using var gaugeCapture = new GaugeCapture();

        GrayComparisonHostedService.RecordWindow(new GrayWindowSnapshot(
            new Dictionary<string, long> { ["success"] = 1 },
            new Dictionary<string, long> { ["no_ds"] = 2 },
            new Dictionary<string, long> { ["AccelerationExceeded"] = 3 }));

        lock (gaugeCapture.Records)
        {
            Assert.Contains(gaugeCapture.Records,
                r => r.Name == "horizon.sync.gray.window.attempts" && r.Value == 1 && r.Tags["result"] == "success");
            Assert.Contains(gaugeCapture.Records,
                r => r.Name == "horizon.sync.gray.window.fallback" && r.Value == 2 && r.Tags["reason"] == "no_ds");
            Assert.Contains(gaugeCapture.Records,
                r => r.Name == "horizon.sync.gray.window.corrections" && r.Value == 3 && r.Tags["reason"] == "AccelerationExceeded");
        }
    }

    // ── ④ Redis 键格式 ─────────────────────────────────────────────────

    [Fact]
    public void Redis_ReasonsKeyFormat_Fixed()
    {
        // Silo 写与网关读必须一致（漂移即面板断供）；总数键与按 reason 键同前缀风格。
        Assert.Equal("cluster:dev:sync:corrections:reasons", RedisCorrectionCounterStore.GetReasonsKey("dev"));
        Assert.Equal("cluster:TYMYD-Cluster-001:sync:corrections:reasons", RedisCorrectionCounterStore.GetReasonsKey("TYMYD-Cluster-001"));
        Assert.Equal("cluster:dev:sync:corrections:total", RedisCorrectionCounterStore.GetKey("dev"));
    }

    /// <summary>读侧可注入失败的存储（面板读失败 fail-open 语义）。</summary>
    private sealed class FlakyReadStore : ICorrectionCounterStore
    {
        private readonly Dictionary<string, long> _byReason = new();
        public bool FailNextRead;

        public Task SeedAsync(long v)
        {
            _byReason["AccelerationExceeded"] = v;
            return Task.CompletedTask;
        }

        public Task<long> GetCorrectionsTotalAsync() => Task.FromResult(_byReason.Values.Sum());

        public Task IncrementCorrectionsAsync(long delta) => Task.CompletedTask;

        public Task<IReadOnlyDictionary<string, long>> GetCorrectionsByReasonAsync()
        {
            if (FailNextRead)
            {
                FailNextRead = false;
                throw new InvalidOperationException("模拟 Redis 读故障");
            }
            return Task.FromResult<IReadOnlyDictionary<string, long>>(new Dictionary<string, long>(_byReason));
        }

        public Task IncrementCorrectionsByReasonAsync(IReadOnlyDictionary<string, long> deltas) => Task.CompletedTask;
    }

    /// <summary>面板 gauge 捕获助手（挂接三个 gray.window gauge 仪表）。</summary>
    private sealed class GaugeCapture : IDisposable
    {
        public readonly List<(string Name, long Value, IReadOnlyDictionary<string, string> Tags)> Records = new();
        private readonly MeterListener _listener = new();

        public GaugeCapture()
        {
            _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            {
                var dict = new Dictionary<string, string>();
                foreach (var t in tags)
                {
                    if (t.Value is not null) dict[t.Key] = t.Value.ToString()!;
                }
                lock (Records) Records.Add((instrument.Name, value, dict));
            });
            _listener.Start();
            _listener.EnableMeasurementEvents(SyncMetrics.GrayWindowAttempts);
            _listener.EnableMeasurementEvents(SyncMetrics.GrayWindowFallback);
            _listener.EnableMeasurementEvents(SyncMetrics.GrayWindowCorrections);
        }

        public void Dispose() => _listener.Dispose();
    }
}
