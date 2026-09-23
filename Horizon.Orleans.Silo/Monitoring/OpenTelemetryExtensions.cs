using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Horizon.Orleans.Silo.Monitoring
{
    /// <summary>
    /// OpenTelemetry配置扩展方法
    /// 为Orleans Silo提供APM集成、Prometheus指标导出和分布式追踪
    /// </summary>
    public static class OpenTelemetryExtensions
    {
        /// <summary>
        /// 默认Prometheus指标端口
        /// </summary>
        private const int DefaultPrometheusPort = 9464;

        /// <summary>
        /// 添加OpenTelemetry监控到服务集合
        /// </summary>
        /// <param name="services">服务集合</param>
        /// <param name="serviceName">服务名称</param>
        /// <param name="serviceVersion">服务版本</param>
        /// <param name="prometheusPort">Prometheus HTTP Listener端口</param>
        /// <param name="otlpEndpoint">OTLP collector 端点（阶段 6.3 P2⑩+）；null/空 = 不启用 OTLP 推送
        /// （仅 Prometheus scrape）。示例：<c>http://localhost:4318</c>（OTLP/HTTP）。</param>
        /// <param name="otlpExportIntervalSeconds">OTLP 指标导出间隔（秒，默认 60；测试可调小加速验证）。</param>
        /// <returns>服务集合</returns>
        public static IServiceCollection AddHorizonOpenTelemetry(
            this IServiceCollection services,
            string serviceName = "HundunWorld.Silo",
            string serviceVersion = "1.0.0",
            int prometheusPort = DefaultPrometheusPort,
            string? otlpEndpoint = null,
            int otlpExportIntervalSeconds = 60)
        {
            services.AddOpenTelemetry()
                .ConfigureResource(resource => resource
                    .AddService(
                        serviceName: serviceName,
                        serviceVersion: serviceVersion,
                        serviceInstanceId: Environment.MachineName))
                .WithMetrics(metrics =>
                {
                    // 添加.NET运行时指标（GC、线程池、进程等）
                    metrics.AddRuntimeInstrumentation();

                    // 添加自定义游戏指标
                    metrics.AddMeter(HorizonMetrics.MeterName);

                    // 阶段 6.3 P2⑩+：同步链路指标（horizon.sync.*：corrections/input.lag/AOI/diff/sessions）——
                    // 此前 Silo 侧从未挂接 exporter（死指标），现补入：corrections 与 input.lag 出进程后，
                    // 与网关 horizon.sync.gray.window.*（P2⑨ 面板）汇入同一时序库，跨进程对比面板闭环。
                    metrics.AddMeter(Horizon.Game.Core.Observability.SyncMetrics.Meter.Name);

                    // 导出到Prometheus HTTP Listener端点
                    metrics.AddPrometheusHttpListener(options =>
                    {
                        options.UriPrefixes = new[] { $"http://localhost:{prometheusPort}/" };
                    });

                    // 阶段 6.3 P2⑩+：OTLP exporter（生产 collector 推送）——与 Prometheus scrape 双轨并存：
                    // 配置了 Monitoring:Otlp:Endpoint 才启用；未配置时零行为变化（仅 Prometheus）。
                    // 手动 PeriodicExportingMetricReader + OtlpMetricExporter：导出间隔显式可控（默认 60s，测试可调小）。
                    if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                    {
                        metrics.AddReader(new PeriodicExportingMetricReader(
                            new OtlpMetricExporter(new OtlpExporterOptions
                            {
                                Endpoint = new Uri(otlpEndpoint!),
                                Protocol = OtlpExportProtocol.HttpProtobuf,
                            }),
                            exportIntervalMilliseconds: Math.Max(100, otlpExportIntervalSeconds * 1000)));
                    }
                })
                .WithTracing(tracing =>
                {
                    // 添加自定义活动源
                    tracing.AddSource(HorizonMetrics.ActivitySourceName);

                    // 导出到控制台（开发环境调试）已注释，避免巨量日志刷屏
                    // tracing.AddConsoleExporter();
                });

            return services;
        }
    }
}
