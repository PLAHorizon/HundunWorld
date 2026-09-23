using System;
using System.Collections.Generic;
using System.Linq;
using Horizon.Game.Gateway.Monitoring;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 阶段 2.5 — DS 存活指标 OTEL 导出接入验证。
/// <para>
/// 网关监控在 <see cref="OpenTelemetryExtensions.AddGatewayOpenTelemetry"/> 内以
/// <c>metrics.AddMeter(GatewayMetrics.MeterName)</c> 注册全部自定义仪表（含阶段 2.5 新增的
/// <c>hundunworld.gateway.ds.servers.alive</c> / <c>.pruned.total</c>）。本测试用同一
/// <c>AddMeter</c> 路径构建真实 MeterProvider + 导出 reader，记录 DS 存活指标后强制 flush，
/// 断言指标确实经 OpenTelemetry 管道到达导出器——证明只要网关启用 OTEL，DS 路由表自愈
/// 指标即可被 Prometheus/OTLP 采集，无需额外注册。
/// </para>
/// </summary>
public class GatewayMetricsOtelExportTests
{
    /// <summary>内存导出器：接收 flush 后的 Metric 批次。</summary>
    private sealed class InMemoryMetricExporter : BaseExporter<Metric>
    {
        public List<Metric> Exported { get; } = new();

        public override ExportResult Export(in Batch<Metric> batch)
        {
            foreach (var metric in batch)
            {
                Exported.Add(metric);
            }
            return ExportResult.Success;
        }

        protected override bool OnShutdown(int timeoutMilliseconds) => true;
    }

    private static bool HasMetricPoint(Metric metric)
    {
        foreach (var _ in metric.GetMetricPoints())
        {
            return true;
        }
        return false;
    }

    [Fact]
    public void DsMetrics_AreDefined_UnderGatewayMeter()
    {
        // 仪表定义归属 GatewayMetrics.MeterName（AddGatewayOpenTelemetry 注册的正是该 meter）。
        Assert.Equal(GatewayMetrics.MeterName, GatewayMetrics.ActiveDsServers.Meter.Name);
        Assert.Equal(GatewayMetrics.MeterName, GatewayMetrics.DsServersPrunedTotal.Meter.Name);

        // 名称/单位遵循指标命名规范（与 Prometheus 采集名一致）。
        Assert.Equal("hundunworld.gateway.ds.servers.alive", GatewayMetrics.ActiveDsServers.Name);
        Assert.Equal("{servers}", GatewayMetrics.ActiveDsServers.Unit);
        Assert.Equal("hundunworld.gateway.ds.servers.pruned.total", GatewayMetrics.DsServersPrunedTotal.Name);
        Assert.Equal("{servers}", GatewayMetrics.DsServersPrunedTotal.Unit);
    }

    [Fact]
    public void DsAliveMetric_FlowsThroughOtelPipeline_ToExporter()
    {
        var exporter = new InMemoryMetricExporter();
        // 导出周期拉大（1 分钟）使导出仅由下方 ForceFlush 驱动：若用短周期（如 100ms），
        // 后台周期导出可能与 ForceFlush 竞态、把同名 metric 导出两次，Assert.Single 随机失败
        // （完整套件并行时概率放大）。周期内数据点由 ForceFlush 显式下发，确定性不受影响。
        var reader = new PeriodicExportingMetricReader(exporter, 60_000);
        using var provider = Sdk.CreateMeterProviderBuilder()
            .AddMeter(GatewayMetrics.MeterName) // 与 AddGatewayOpenTelemetry 相同的注册路径
            .AddReader(reader)
            .Build();

        // 记录一次存活数上报（DSRegistryMaintenanceHostedService 每周期执行的动作）。
        GatewayMetrics.ActiveDsServers.Add(1);

        provider.ForceFlush();

        var metric = Assert.Single(exporter.Exported, m => m.Name == "hundunworld.gateway.ds.servers.alive");
        // 数据点已到达导出器即证明链路通；绝对值由 DSRegistryMaintenanceHostedServiceTests
        // 确定性验证（本测试的静态仪表可能被并行测试的 ±delta 上报抵消，不在此断言数值）。
        Assert.True(HasMetricPoint(metric), "DS 存活指标应携带数据点到达导出器。");
    }

    [Fact]
    public void DsPrunedMetric_FlowsThroughOtelPipeline_ToExporter()
    {
        var exporter = new InMemoryMetricExporter();
        // 同上：长周期 + ForceFlush 驱动，避免周期导出与 ForceFlush 竞态造成重复导出。
        var reader = new PeriodicExportingMetricReader(exporter, 60_000);
        using var provider = Sdk.CreateMeterProviderBuilder()
            .AddMeter(GatewayMetrics.MeterName)
            .AddReader(reader)
            .Build();

        GatewayMetrics.DsServersPrunedTotal.Add(1);

        provider.ForceFlush();

        var metric = Assert.Single(exporter.Exported, m => m.Name == "hundunworld.gateway.ds.servers.pruned.total");
        Assert.True(HasMetricPoint(metric), "DS 摘除累计指标应携带数据点到达导出器。");
    }
}
