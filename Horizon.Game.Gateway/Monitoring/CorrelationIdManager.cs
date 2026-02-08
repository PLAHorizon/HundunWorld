using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Horizon.Game.Gateway.Monitoring
{
    /// <summary>
    /// 网关CorrelationId管理器
    /// 为每个客户端请求生成或传播CorrelationId，实现端到端分布式追踪
    /// </summary>
    public class CorrelationIdManager
    {
        /// <summary>
        /// CorrelationId在请求头/上下文中的键名
        /// </summary>
        public const string CorrelationIdKey = "X-Correlation-Id";

        /// <summary>
        /// 来源标识键名（区分请求来源）
        /// </summary>
        public const string SourceKey = "X-Source";

        private readonly ILogger<CorrelationIdManager> _logger;

        public CorrelationIdManager(ILogger<CorrelationIdManager> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// 为客户端请求获取或创建CorrelationId
        /// </summary>
        /// <param name="clientId">客户端标识</param>
        /// <param name="existingCorrelationId">现有的CorrelationId（如果有）</param>
        /// <returns>CorrelationId</returns>
        public string GetOrCreateCorrelationId(string clientId, string? existingCorrelationId = null)
        {
            if (!string.IsNullOrEmpty(existingCorrelationId))
            {
                return existingCorrelationId;
            }

            var correlationId = GenerateCorrelationId();
            _logger.LogDebug("为客户端 {ClientId} 生成新CorrelationId: {CorrelationId}",
                clientId, correlationId);
            return correlationId;
        }

        /// <summary>
        /// 创建包含CorrelationId的日志作用域（低分配实现）
        /// </summary>
        /// <param name="correlationId">关联ID</param>
        /// <param name="source">请求来源</param>
        /// <returns>日志作用域</returns>
        public IDisposable? CreateLogScope(string correlationId, string source = "Gateway")
        {
            return _logger.BeginScope(new CorrelationLogScope(correlationId, source));
        }

        /// <summary>
        /// 生成CorrelationId，格式: gw-时间戳-短GUID
        /// 使用"gw-"前缀区分网关生成的ID
        /// </summary>
        internal static string GenerateCorrelationId()
        {
            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd");
            var guid = Guid.NewGuid().ToString("N")[..8];
            return $"gw-{timestamp}-{guid}";
        }

        /// <summary>
        /// 低分配的日志作用域实现
        /// </summary>
        private readonly struct CorrelationLogScope : IReadOnlyList<KeyValuePair<string, object>>
        {
            private readonly string _correlationId;
            private readonly string _source;

            public CorrelationLogScope(string correlationId, string source)
            {
                _correlationId = correlationId;
                _source = source;
            }

            public int Count => 2;

            public KeyValuePair<string, object> this[int index] => index switch
            {
                0 => new KeyValuePair<string, object>("CorrelationId", _correlationId),
                1 => new KeyValuePair<string, object>("Source", _source),
                _ => throw new IndexOutOfRangeException(nameof(index))
            };

            public IEnumerator<KeyValuePair<string, object>> GetEnumerator()
            {
                for (int i = 0; i < Count; i++)
                    yield return this[i];
            }

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

            public override string ToString() =>
                $"CorrelationId={_correlationId}, Source={_source}";
        }
    }
}
