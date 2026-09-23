using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Orleans;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Horizon.Orleans.Silo.Monitoring;
using Horizon.Orleans.Interface;

namespace Horizon.Orleans.Silo
{
    // 在Grain接口增加重试策略
    // 修复：全局 3× 重试会重放非幂等副作用（重复 CreateCharacter 插入、DeathCount++ 等）。
    // 现改为仅对显式标注 [Idempotent] 的方法重试；未标注的方法失败即传播（精确执行一次）。
    [GenerateSerializer]
    public class RetryFilter : IIncomingGrainCallFilter
    {
        public async Task Invoke(IIncomingGrainCallContext context)
        {
            // 前置处理：记录方法开始时间 
            var stopwatch = Stopwatch.StartNew();
            var grainType = context.Grain.GetType().Name;
            var methodName = context.ImplementationMethod.Name;

            using var activity = HorizonMetrics.StartGrainActivity(grainType, methodName);
            HorizonMetrics.GrainCallsTotal.Add(1, new KeyValuePair<string, object?>("grain_type", grainType));

            try
            {

                const int maxRetries = 3;
                var canRetry = IsIdempotent(context);
                for (var i = 0; i < maxRetries; i++)
                {
                    try
                    {
                        // 调用下一个过滤器或实际的 Grain 方法 
                        await context.Invoke();
                        return;
                    }
                    catch (Exception) when (i < maxRetries - 1 && canRetry)
                    {
                        await Task.Delay(100 * (i + 1));
                    }
                }
            }
            catch (Exception)
            {
                HorizonMetrics.GrainCallErrorsTotal.Add(1, new KeyValuePair<string, object?>("grain_type", grainType));
                throw;
            }
            finally
            {
                // 后置处理：记录方法执行时间 
                stopwatch.Stop();
                var executionTime = stopwatch.ElapsedMilliseconds;
                HorizonMetrics.GrainCallDuration.Record(executionTime, new KeyValuePair<string, object?>("grain_type", grainType));
            }

        }

        /// <summary>
        /// 仅当方法显式标注 <see cref="IdempotentAttribute"/> 时才允许重试。
        /// 幂等性是契约属性，优先看接口方法；缺失时回退到实现方法。
        /// </summary>
        private static bool IsIdempotent(IIncomingGrainCallContext context)
        {
            return HasIdempotentAttribute(context.InterfaceMethod)
                || HasIdempotentAttribute(context.ImplementationMethod);
        }

        private static bool HasIdempotentAttribute(System.Reflection.MethodInfo? method)
        {
            return method != null
                && method.GetCustomAttributes(typeof(IdempotentAttribute), inherit: true).Length > 0;
        }
    }


    public class LoggingHealthCheckPublisher : IHealthCheckPublisher
    {
        private readonly ILogger<LoggingHealthCheckPublisher> logger;

        public LoggingHealthCheckPublisher(ILogger<LoggingHealthCheckPublisher> logger)
        {
            this.logger = logger;
        }

        public Task PublishAsync(HealthReport report, CancellationToken cancellationToken)
        {
            var id = Guid.NewGuid();
            var now = DateTime.UtcNow;

            logger.Log(report.Status == HealthStatus.Healthy ? LogLevel.Information : LogLevel.Warning,
                "Service is {@ReportStatus} at {@ReportTime} after {@ElapsedTime}ms with CorrelationId {@CorrelationId}",
                report.Status, now, report.TotalDuration.TotalMilliseconds, id);

            foreach (var entry in report.Entries)
            {
                logger.Log(entry.Value.Status == HealthStatus.Healthy ? LogLevel.Information : LogLevel.Warning,
                    entry.Value.Exception,
                    "{@HealthCheckName} is {@ReportStatus} after {@ElapsedTime}ms with CorrelationId {@CorrelationId}",
                    entry.Key, entry.Value.Status, entry.Value.Duration.TotalMilliseconds, id);
            }

            return Task.CompletedTask;
        }
    }

    public class HealthCheckHostedServiceOptions
    {
        public string PathString { get; set; } = "/health";
        public int Port { get; set; } = 8880;
    }
}
