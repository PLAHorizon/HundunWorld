using System;
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
        /// 创建包含CorrelationId的日志作用域
        /// </summary>
        /// <param name="correlationId">关联ID</param>
        /// <param name="source">请求来源</param>
        /// <returns>日志作用域</returns>
        public IDisposable? CreateLogScope(string correlationId, string source = "Gateway")
        {
            return _logger.BeginScope(new Dictionary<string, object>
            {
                ["CorrelationId"] = correlationId,
                ["Source"] = source
            });
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
    }
}
