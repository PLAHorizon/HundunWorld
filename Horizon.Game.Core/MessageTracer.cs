using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace Horizon.Game.Core
{
    /// <summary>
    /// 消息调试工具
    /// </summary>
    public class MessageTracer
    {
        private readonly ILogger<MessageTracer> _logger;

        public MessageTracer(ILogger<MessageTracer> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// 跟踪消息
        /// </summary>
        public void TraceMessage(string messageId, string messageContent, Dictionary<string, object> context = null)
        {
            using (_logger.BeginScope(context ?? new Dictionary<string, object>()))
            {
                _logger.LogInformation($"追踪消息: {messageId}，内容: {messageContent}");
            }
        }

        /// <summary>
        /// 跟踪错误
        /// </summary>
        public void TraceError(string messageId, Exception exception, Dictionary<string, object> context = null)
        {
            using (_logger.BeginScope(context ?? new Dictionary<string, object>()))
            {
                _logger.LogError(exception, $"追踪消息时发生错误: {messageId}");
            }
        }
    }
}