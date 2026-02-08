using System;
using System.Collections;
using System.Collections.Generic;
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

            // 使用结构化日志附带CorrelationId（低分配方式）
            using (_logger.BeginScope(new CorrelationLogScope(correlationId, causationId, grainType, methodName)))
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

        /// <summary>
        /// 低分配的日志作用域实现，避免每次Grain调用创建Dictionary
        /// </summary>
        private readonly struct CorrelationLogScope : IReadOnlyList<KeyValuePair<string, object>>
        {
            private readonly string _correlationId;
            private readonly string _causationId;
            private readonly string _grainType;
            private readonly string _methodName;

            public CorrelationLogScope(string correlationId, string causationId, string grainType, string methodName)
            {
                _correlationId = correlationId;
                _causationId = causationId;
                _grainType = grainType;
                _methodName = methodName;
            }

            public int Count => 4;

            public KeyValuePair<string, object> this[int index] => index switch
            {
                0 => new KeyValuePair<string, object>("CorrelationId", _correlationId),
                1 => new KeyValuePair<string, object>("CausationId", _causationId),
                2 => new KeyValuePair<string, object>("GrainType", _grainType),
                3 => new KeyValuePair<string, object>("MethodName", _methodName),
                _ => throw new IndexOutOfRangeException(nameof(index))
            };

            public IEnumerator<KeyValuePair<string, object>> GetEnumerator()
            {
                for (int i = 0; i < Count; i++)
                    yield return this[i];
            }

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

            public override string ToString() =>
                $"CorrelationId={_correlationId}, CausationId={_causationId}, GrainType={_grainType}, MethodName={_methodName}";
        }
    }
}
