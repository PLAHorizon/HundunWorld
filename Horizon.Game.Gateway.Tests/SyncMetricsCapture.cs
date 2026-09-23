using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using Horizon.Game.Core.Observability;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 阶段 6.3 — 同步指标确定性捕获助手：用 <see cref="MeterListener"/> 直接挂接\nSyncMetrics 的阶段 6.3 仪表（DS 路由 attempts/fallback/latency + corrections），\n按 <c>reason</c> 维度捕获计数（沿 <c>DSRegistryMaintenanceHostedServiceTests.MetricCapture</c> 模式）。\n测试通过 <see cref="Counters"/> / <see cref="Latencies"/> 断言路由决策与修正上报，\n不依赖仪表创建时序与真实 exporter。
/// </summary>
public sealed class SyncMetricsCapture : IDisposable
{
    private readonly MeterListener _listener = new();

    /// <summary>Counter/UpDownCounter 捕获：(仪表名, 增量, reason 标签值；无标签为 null)。</summary>
    public readonly List<(string Name, long Value, string? Reason)> Counters = new();

    /// <summary>Counter 完整标签捕获（阶段 6.3 P1⑦：断言 shard_id/region/result/ds_id 维度）。</summary>
    public readonly List<(string Name, long Value, IReadOnlyDictionary<string, string> Tags)> Tagged = new();

    /// <summary>Histogram 捕获（DS 转发耗时，ms）。</summary>
    public readonly List<double> Latencies = new();

    public SyncMetricsCapture()
    {
        _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            string? reason = null;
            var allTags = new Dictionary<string, string>();
            foreach (var tag in tags)
            {
                if (tag.Key == "reason") reason = tag.Value?.ToString();
                if (tag.Value is not null) allTags[tag.Key] = tag.Value.ToString()!;
            }
            lock (Counters) Counters.Add((instrument.Name, value, reason));
            lock (Tagged) Tagged.Add((instrument.Name, value, allTags));
        });
        _listener.SetMeasurementEventCallback<double>((instrument, value, _, _) =>
        {
            lock (Latencies) Latencies.Add(value);
        });
        _listener.Start();
        _listener.EnableMeasurementEvents(SyncMetrics.DsRouteAttempts);
        _listener.EnableMeasurementEvents(SyncMetrics.DsRouteFallback);
        _listener.EnableMeasurementEvents(SyncMetrics.DsRouteLatencyMs);
        _listener.EnableMeasurementEvents(SyncMetrics.Corrections);
        _listener.EnableMeasurementEvents(SyncMetrics.RollbacksTriggered);
        _listener.EnableMeasurementEvents(SyncMetrics.HandshakeRejected);
    }

    public void Dispose() => _listener.Dispose();
}
