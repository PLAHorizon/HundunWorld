using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Horizon.Game.Core.Observability;
using Horizon.Game.Gateway.Monitoring;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 阶段 6.3 P2⑩ — OTLP exporter（生产 collector 推送）测试。
/// <para>
/// 覆盖：①行为——经 <see cref="Sdk.CreateMeterProviderBuilder"/> + <see cref="OtlpMetricExporter"/>
/// 推送的 <c>horizon.sync.*</c> 指标真实到达 collector（本地 mock OTLP/HTTP 端点收 POST /v1/metrics，
/// protobuf 体含明文指标名）；②接线——<see cref="OpenTelemetryExtensions.AddGatewayOpenTelemetry"/>
/// 配置了 <c>Monitoring:Otlp:Endpoint</c> 才注册 OTLP reader，未配置零行为变化。
/// </para>
/// <para>
/// 本类挂 <see cref="SyncMetricsIsolatedCollection"/>：MeterProvider 会 EnableMeasurementEvents 到
/// SyncMetrics 静态仪表，与其它记录同一仪表的测试并行会串扰（串行化保护）。
/// </para>
/// </summary>
[Collection(SyncMetricsIsolatedCollection.Name)]
public class OtlpExporterTests
{
    /// <summary>
    /// 行为验证：horizon.sync.* 静态仪表经 OTLP exporter 真实推送到 collector。
    /// 打 RollbacksTriggered / DsRouteAttempts，导出间隔 500ms，等待后断言 collector 收到
    /// POST /v1/metrics 且 protobuf 体含指标名（指标名在 protobuf 中为明文 ASCII）。
    /// </summary>
    [Fact]
    public async Task OtlpExport_PushesHorizonSyncMetrics_ToCollector()
    {
        await using var collector = new OtlpTestCollector();
        var endpoint = new Uri($"http://127.0.0.1:{collector.Port}/v1/metrics");

        using var provider = Sdk.CreateMeterProviderBuilder()
            .AddMeter(SyncMetrics.Meter.Name)
            .AddReader(new PeriodicExportingMetricReader(
                new OtlpMetricExporter(new OtlpExporterOptions
                {
                    Endpoint = endpoint,
                    Protocol = OtlpExportProtocol.HttpProtobuf,
                }),
                exportIntervalMilliseconds: 500))
            .Build();

        // 打指标（真实静态仪表）。
        SyncMetrics.RollbacksTriggered.Add(1);
        SyncMetrics.DsRouteAttempts.Add(7);

        // 等 ≥2 个导出周期（500ms）+ 网络余量。
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(6);
        while (DateTime.UtcNow < deadline && !collector.AnyBodyContains("horizon.sync"))
        {
            await Task.Delay(100);
        }

        Assert.True(collector.RequestCount > 0, "collector 应至少收到一次导出请求");
        Assert.True(collector.AnyBodyContains("horizon.sync.ds.rollbacks.triggered"),
            "导出体应含 horizon.sync.ds.rollbacks.triggered（指标名明文）");
        Assert.True(collector.AnyBodyContains("horizon.sync.ds.route.attempts"),
            "导出体应含 horizon.sync.ds.route.attempts");
    }

    /// <summary>
    /// 接线（行为级）：经 <see cref="OpenTelemetryExtensions.AddGatewayOpenTelemetry"/> 构建的 provider——
    /// 配置了 <c>Monitoring:Otlp:Endpoint</c> 时指标真实推送到 collector；未配置时 collector 收不到任何请求
    /// （零行为变化，仅 Prometheus）。不依赖反射，与生产接线同路径。</summary>
    [Fact]
    public async Task AddGatewayOpenTelemetry_EndpointConfigured_PushesMetrics_OtherwiseSilent()
    {
        // ── 配置 endpoint：指标应推送到 collector ──
        await using (var collector = new OtlpTestCollector())
        {
            using (BuildProvider(otlpEndpoint: $"http://127.0.0.1:{collector.Port}/v1/metrics"))
            {
                SyncMetrics.DsRouteAttempts.Add(3);
                var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(6);
                while (DateTime.UtcNow < deadline && !collector.AnyBodyContains("horizon.sync"))
                {
                    await Task.Delay(100);
                }
            }
            Assert.True(collector.AnyBodyContains("horizon.sync.ds.route.attempts"),
                "配置 OTLP endpoint 后应真实推送 horizon.sync.* 到 collector");
        }

        // ── 未配置 endpoint：同一 provider 构建路径，collector 收不到任何请求（零行为变化）──
        await using (var silent = new OtlpTestCollector())
        {
            using (BuildProvider(otlpEndpoint: null))
            {
                SyncMetrics.DsRouteAttempts.Add(5);
                await Task.Delay(2000); // 覆盖 ≥1 个导出周期（若误注册 exporter 会发出请求）
            }
            Assert.Equal(0, silent.RequestCount);
        }
    }

    /// <summary>
    /// 取一个空闲端口用于 Prometheus listener。
    /// <para>
    /// <b>为什么必须动态取端口</b>：<c>AddGatewayOpenTelemetry</c> 默认在 <b>9464</b> 上注册
    /// Prometheus HTTP listener，而该端口是 HTTP.sys 的**全局注册**——本机只要跑着 Silo
    /// （生产/开发环境常态），测试再注册同一前缀就会失败：
    /// <c>Failed to listen on prefix 'http://localhost:9464/metrics/' because it conflicts with
    /// an existing registration</c>。这是"本机服务在跑 = 测试假失败"的典型来源，
    /// 与代码质量无关。改为动态端口后，覆盖度不变且不再与任何常驻服务互踩。
    /// </para>
    /// </summary>
    private static int GetFreePrometheusPort()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static MeterProvider BuildProvider(string? otlpEndpoint)
    {
        var services = new ServiceCollection();
        services.AddGatewayOpenTelemetry(
            prometheusPort: GetFreePrometheusPort(),
            otlpEndpoint: otlpEndpoint,
            otlpExportIntervalSeconds: 1);
        // 注意：不能 using dispose ServiceProvider——会连带停掉 MeterProvider 的导出线程。
        // MeterProvider 的生命周期由调用方的 using 块管理（Dispose 时内部停 reader）。
        var sp = services.BuildServiceProvider();
        return sp.GetRequiredService<MeterProvider>();
    }
}
