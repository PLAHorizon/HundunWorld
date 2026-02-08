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

namespace Horizon.Orleans.Silo
{
    // 在Grain接口增加重试策略 
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
                for (var i = 0; i < maxRetries; i++)
                {
                    try
                    {
                        // 调用下一个过滤器或实际的 Grain 方法 
                        await context.Invoke();
                        return;
                    }
                    catch (Exception) when (i < maxRetries - 1)
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
                Console.ForegroundColor = ConsoleColor.Green;
                System.Console.WriteLine($"Grain {grainType}.{methodName} executed in {executionTime} ms.");
                Console.ForegroundColor = ConsoleColor.White;
            }

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
