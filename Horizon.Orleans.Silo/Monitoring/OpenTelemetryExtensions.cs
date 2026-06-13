using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
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
        /// <returns>服务集合</returns>
        public static IServiceCollection AddHorizonOpenTelemetry(
            this IServiceCollection services,
            string serviceName = "HundunWorld.Silo",
            string serviceVersion = "1.0.0",
            int prometheusPort = DefaultPrometheusPort)
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

                    // 导出到Prometheus HTTP Listener端点
                    metrics.AddPrometheusHttpListener(options =>
                    {
                        options.UriPrefixes = new[] { $"http://localhost:{prometheusPort}/" };
                    });
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
