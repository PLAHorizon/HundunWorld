using System;

namespace Horizon.Game.Core.Configuration
{
    /// <summary>
    /// 网络配置管理类 - 集中管理所有网络相关的配置参数
    /// 避免硬编码常量，提高系统的可配置性和维护性
    /// </summary>
    public class NetworkConfiguration
    {
        /// <summary>
        /// 最大消息长度 (字节)
        /// </summary>
        public int MaxMessageLength { get; set; } = 1024 * 1024; // 1MB

        /// <summary>
        /// 最小消息长度 (字节)
        /// </summary>
        public int MinMessageLength { get; set; } = 5; // 4字节长度 + 1字节最小数据

        /// <summary>
        /// 慢操作阈值 (毫秒)
        /// </summary>
        public int SlowOperationThresholdMs { get; set; } = 100;

        /// <summary>
        /// 客户端缓冲区清理间隔 (毫秒)
        /// </summary>
        public int ClientBufferCleanupIntervalMs { get; set; } = 60000; // 1分钟

        /// <summary>
        /// 客户端缓冲区超时时间 (毫秒)
        /// </summary>
        public int ClientBufferTimeoutMs { get; set; } = 300000; // 5分钟

        /// <summary>
        /// 最大并发客户端数量
        /// </summary>
        public int MaxConcurrentClients { get; set; } = 10000;

        /// <summary>
        /// 消息处理重试次数
        /// </summary>
        public int MessageProcessingRetryCount { get; set; } = 3;

        /// <summary>
        /// 性能报告间隔 (毫秒)
        /// </summary>
        public int PerformanceReportIntervalMs { get; set; } = 60000; // 1分钟

        /// <summary>
        /// TCP连接超时时间 (毫秒)
        /// </summary>
        public int TcpConnectionTimeoutMs { get; set; } = 30000; // 30秒

        /// <summary>
        /// 心跳间隔 (毫秒)
        /// </summary>
        public int HeartbeatIntervalMs { get; set; } = 10000; // 10秒        /// <summary>
        /// 客户端请求频率限制 (请求/秒)
        /// </summary>
        public int ClientRequestRateLimit { get; set; } = 100;

        /// <summary>
        /// 每分钟最大请求数
        /// </summary>
        public int MaxRequestsPerMinute { get; set; } = 1000;

        /// <summary>
        /// DOS攻击检测阈值 (错误请求/分钟)
        /// </summary>
        public int DosDetectionThreshold { get; set; } = 50;

        /// <summary>
        /// 协议版本兼容性检查
        /// </summary>
        public bool EnableProtocolVersionCheck { get; set; } = true;

        /// <summary>
        /// 详细日志记录（性能模式下可关闭）
        /// </summary>
        public bool EnableDetailedLogging { get; set; } = true;

        /// <summary>
        /// 性能监控开关
        /// </summary>
        public bool EnablePerformanceMonitoring { get; set; } = true;

        /// <summary>
        /// 验证配置参数的有效性
        /// </summary>
        public void Validate()
        {
            if (MaxMessageLength <= MinMessageLength)
                throw new InvalidOperationException("MaxMessageLength must be greater than MinMessageLength");

            if (MaxMessageLength > 50 * 1024 * 1024) // 50MB
                throw new InvalidOperationException("MaxMessageLength is too large (>50MB)");

            if (SlowOperationThresholdMs <= 0)
                throw new InvalidOperationException("SlowOperationThresholdMs must be positive");

            if (MaxConcurrentClients <= 0)
                throw new InvalidOperationException("MaxConcurrentClients must be positive");

            if (ClientRequestRateLimit <= 0)
                throw new InvalidOperationException("ClientRequestRateLimit must be positive");

            if (DosDetectionThreshold <= 0)
                throw new InvalidOperationException("DosDetectionThreshold must be positive");
        }

        /// <summary>
        /// 获取优化后的配置（用于高性能场景）
        /// </summary>
        public static NetworkConfiguration GetPerformanceOptimized()
        {
            return new NetworkConfiguration
            {
                MaxMessageLength = 512 * 1024, // 减少到512KB
                SlowOperationThresholdMs = 50, // 更严格的阈值
                ClientBufferCleanupIntervalMs = 30000, // 更频繁的清理
                EnableDetailedLogging = false, // 关闭详细日志
                ClientRequestRateLimit = 200, // 更高的请求限制
                PerformanceReportIntervalMs = 30000 // 更频繁的性能报告
            };
        }

        /// <summary>
        /// 获取开发调试配置
        /// </summary>
        public static NetworkConfiguration GetDevelopmentDebug()
        {
            return new NetworkConfiguration
            {
                SlowOperationThresholdMs = 10, // 非常敏感的性能监控
                EnableDetailedLogging = true, // 启用详细日志
                ClientRequestRateLimit = 10, // 较低的请求限制便于测试
                DosDetectionThreshold = 10, // 更敏感的DOS检测
                PerformanceReportIntervalMs = 10000 // 更频繁的报告
            };
        }
    }
}
