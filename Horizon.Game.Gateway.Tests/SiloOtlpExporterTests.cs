using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Horizon.Game.Core.Observability;
using Horizon.Orleans.Silo.Monitoring;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 阶段 6.3 P2⑩+ — Silo 侧 OTLP exporter 测试。
/// <para>
/// 覆盖：①行为——<c>horizon.sync.corrections</c> / <c>horizon.sync.input.lag</c> 经 OTLP exporter
/// 真实推送到 collector（mock OTLP/HTTP 端点收 POST /v1/metrics，protobuf 体含明文指标名）；
/// ②接线——<see cref="OpenTelemetryExtensions.AddHorizonOpenTelemetry"/> 配置了 <c>Monitoring:Otlp:Endpoint</c>
/// 才注册 OTLP reader，未配置零行为变化。与网关 <c>OtlpExporterTests</c> 共用 <see cref="OtlpTestCollector"/>。
/// </para>
/// <para>
/// 目的：Silo 的 corrections/input.lag 出进程后，与网关 <c>horizon.sync.gray.window.*</c>（P2⑨ 面板）
/// 汇入同一时序库——跨进程对比面板（OPS_RUNBOOK §5.4）的数据闭环前置。
/// </para>
/// <para>
/// 本类挂 <see cref="SyncMetricsIsolatedCollection"/>：MeterProvider 会 EnableMeasurementEvents 到
/// SyncMetrics 静态仪表，与其它记录同一仪表的测试并行会串扰（串行化保护）。
/// </para>
/// </summary>
[Collection(SyncMetricsIsolatedCollection.Name)]
public class SiloOtlpExporterTests
{
    /// <summary>
    /// 行为验证：Silo 侧 horizon.sync.*（corrections + input.lag）经 OTLP exporter 真实推送到 collector。
    /// 打真实静态仪表，导出间隔 500ms，等待后断言 collector 收到且 protobuf 体含指标名（明文 ASCII）。
    /// </summary>
    [Fact]
    public async Task OtlpExport_PushesCorrectionsAndInputLag_ToCollector()
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

        // 打指标（真实静态仪表）：corrections（按 reason）+ input.lag（直方图）。
        SyncMetrics.Corrections.Add(4, new KeyValuePair<string, object?>("reason", "AccelerationExceeded"));
        SyncMetrics.InputLagMs.Record(42);

        // 等 ≥2 个导出周期（500ms）+ 网络余量。
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(6);
        while (DateTime.UtcNow < deadline && !collector.AnyBodyContains("horizon.sync"))
        {
            await Task.Delay(100);
        }

        Assert.True(collector.RequestCount > 0, "collector 应至少收到一次导出请求");
        Assert.True(collector.AnyBodyContains("horizon.sync.corrections"),
            "导出体应含 horizon.sync.corrections（修正率信号出进程）");
        Assert.True(collector.AnyBodyContains("horizon.sync.input.lag"),
            "导出体应含 horizon.sync.input.lag（InputLagMs 出进程）");
    }

    /// <summary>
    /// 接线（行为级）：经 <see cref="OpenTelemetryExtensions.AddHorizonOpenTelemetry"/> 构建的 provider——
    /// 配置了 <c>Monitoring:Otlp:Endpoint</c> 时指标真实推送到 collector；未配置时 collector 收不到任何请求
    /// （零行为变化，仅 Prometheus scrape）。不依赖反射，与生产接线同路径。
    /// </summary>
    [Fact]
    public async Task AddHorizonOpenTelemetry_EndpointConfigured_PushesMetrics_OtherwiseSilent()
    {
        // ── 配置 endpoint：指标应推送到 collector ──
        await using (var collector = new OtlpTestCollector())
        {
            using (BuildProvider(otlpEndpoint: $"http://127.0.0.1:{collector.Port}/v1/metrics"))
            {
                SyncMetrics.Corrections.Add(3);
                var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(6);
                while (DateTime.UtcNow < deadline && !collector.AnyBodyContains("horizon.sync"))
                {
                    await Task.Delay(100);
                }
            }
            Assert.True(collector.AnyBodyContains("horizon.sync.corrections"),
                "配置 OTLP endpoint 后应真实推送 horizon.sync.* 到 collector");
        }

        // ── 未配置 endpoint：同一 provider 构建路径，collector 收不到任何请求（零行为变化）──
        await using (var silent = new OtlpTestCollector())
        {
            using (BuildProvider(otlpEndpoint: null))
            {
                SyncMetrics.Corrections.Add(5);
                await Task.Delay(2000); // 覆盖 ≥1 个导出周期（若误注册 exporter 会发出请求）
            }
            Assert.Equal(0, silent.RequestCount);
        }
    }

    /// <summary>
    /// 取一个空闲端口用于 Prometheus listener。
    /// <para>
    /// 默认端口 <b>9464</b> 是 HTTP.sys 全局注册：本机跑着 Silo 时，测试再注册同一前缀必然失败
    /// （<c>conflicts with an existing registration</c>）。动态端口消除这一"服务在跑就假失败"的来源。
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
        services.AddHorizonOpenTelemetry(
            prometheusPort: GetFreePrometheusPort(),
            otlpEndpoint: otlpEndpoint,
            otlpExportIntervalSeconds: 1);
        // 注意：不能 using dispose ServiceProvider——会连带停掉 MeterProvider 的导出线程。
        // MeterProvider 的生命周期由调用方的 using 块管理（Dispose 时内部停 reader）。
        var sp = services.BuildServiceProvider();
        return sp.GetRequiredService<MeterProvider>();
    }
}
