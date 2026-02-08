using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orleans;
using Horizon.Orleans.Silo.Monitoring;

namespace Horizon.Orleans.Silo.Filters
{
    /// <summary>
    /// 统一Grain异常处理过滤器
    /// 为所有Grain调用提供标准化的异常捕获、日志记录和指标上报
    /// 避免每个Grain方法重复try-catch-log模式
    /// </summary>
    public class GrainExceptionFilter : IIncomingGrainCallFilter
    {
        private readonly ILogger<GrainExceptionFilter> _logger;

        public GrainExceptionFilter(ILogger<GrainExceptionFilter> logger)
        {
            _logger = logger;
        }

        public async Task Invoke(IIncomingGrainCallContext context)
        {
            var grainType = context.Grain?.GetType().Name ?? "Unknown";
            var methodName = context.ImplementationMethod?.Name ?? "Unknown";
            var sw = Stopwatch.StartNew();

            try
            {
                // 记录Grain调用指标
                HorizonMetrics.GrainCallsTotal.Add(1,
                    new("grain.type", grainType),
                    new("grain.method", methodName));

                await context.Invoke();

                sw.Stop();

                // 记录调用时长
                HorizonMetrics.GrainCallDuration.Record(sw.Elapsed.TotalMilliseconds,
                    new("grain.type", grainType),
                    new("grain.method", methodName));

                // 慢调用告警
                if (sw.Elapsed.TotalMilliseconds > 1000)
                {
                    _logger.LogWarning("Grain调用耗时过长: {GrainType}.{MethodName} 耗时 {Duration}ms",
                        grainType, methodName, sw.Elapsed.TotalMilliseconds);
                }
            }
            catch (Exception ex)
            {
                sw.Stop();

                // 记录错误指标
                HorizonMetrics.GrainCallErrorsTotal.Add(1,
                    new("grain.type", grainType),
                    new("grain.method", methodName),
                    new("error.type", ex.GetType().Name));

                // 记录调用时长（包括失败的调用）
                HorizonMetrics.GrainCallDuration.Record(sw.Elapsed.TotalMilliseconds,
                    new("grain.type", grainType),
                    new("grain.method", methodName),
                    new("status", "error"));

                // 结构化日志记录异常
                _logger.LogError(ex,
                    "Grain调用异常: {GrainType}.{MethodName} 耗时 {Duration}ms - {ErrorType}: {ErrorMessage}",
                    grainType, methodName, sw.Elapsed.TotalMilliseconds,
                    ex.GetType().Name, ex.Message);

                // 重新抛出异常，让Orleans框架处理
                throw;
            }
        }
    }
}
