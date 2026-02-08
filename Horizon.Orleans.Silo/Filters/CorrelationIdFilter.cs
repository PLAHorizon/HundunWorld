using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;
using Horizon.Orleans.Silo.Monitoring;

namespace Horizon.Orleans.Silo.Filters
{
    /// <summary>
    /// Orleans Grain调用过滤器，用于传播和管理CorrelationId
    /// 实现分布式追踪中的请求关联，确保跨Grain调用的日志可追溯
    /// </summary>
    public class CorrelationIdFilter : IIncomingGrainCallFilter
    {
        /// <summary>
        /// CorrelationId在RequestContext中的键名
        /// </summary>
        public const string CorrelationIdKey = "X-Correlation-Id";

        /// <summary>
        /// CausationId在RequestContext中的键名（用于追踪因果链）
        /// </summary>
        public const string CausationIdKey = "X-Causation-Id";

        private readonly ILogger<CorrelationIdFilter> _logger;

        public CorrelationIdFilter(ILogger<CorrelationIdFilter> logger)
        {
            _logger = logger;
        }

        public async Task Invoke(IIncomingGrainCallContext context)
        {
            // 获取或生成CorrelationId
            var correlationId = RequestContext.Get(CorrelationIdKey) as string;
            var isNew = false;

            if (string.IsNullOrEmpty(correlationId))
            {
                correlationId = GenerateCorrelationId();
                RequestContext.Set(CorrelationIdKey, correlationId);
                isNew = true;
            }

            // 设置CausationId为当前Grain调用的标识
            var grainType = context.Grain?.GetType().Name ?? "Unknown";
            var methodName = context.ImplementationMethod?.Name ?? "Unknown";
            var causationId = $"{grainType}.{methodName}";
            RequestContext.Set(CausationIdKey, causationId);

            // 使用结构化日志附带CorrelationId
            using (_logger.BeginScope(new Dictionary<string, object>
            {
                ["CorrelationId"] = correlationId,
                ["CausationId"] = causationId,
                ["GrainType"] = grainType,
                ["MethodName"] = methodName
            }))
            {
                if (isNew)
                {
                    _logger.LogDebug("新建CorrelationId {CorrelationId} - {GrainType}.{MethodName}",
                        correlationId, grainType, methodName);
                }

                await context.Invoke();
            }
        }

        /// <summary>
        /// 生成CorrelationId，格式: 时间戳前缀 + 短GUID
        /// 例如: "20260208-a1b2c3d4"
        /// </summary>
        internal static string GenerateCorrelationId()
        {
            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd");
            var guid = Guid.NewGuid().ToString("N")[..8];
            return $"{timestamp}-{guid}";
        }

        /// <summary>
        /// 获取当前请求上下文中的CorrelationId
        /// </summary>
        public static string? GetCurrentCorrelationId()
        {
            return RequestContext.Get(CorrelationIdKey) as string;
        }

        /// <summary>
        /// 设置当前请求上下文中的CorrelationId（供外部调用方使用）
        /// </summary>
        public static void SetCorrelationId(string correlationId)
        {
            RequestContext.Set(CorrelationIdKey, correlationId);
        }
    }
}
