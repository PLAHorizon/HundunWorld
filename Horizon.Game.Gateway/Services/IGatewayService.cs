using System;
using System.Threading;
using System.Threading.Tasks;

namespace Horizon.Game.Gateway.Services
{
    /// <summary>
    /// 网关服务接口
    /// </summary>
    public interface IGatewayService
    {
        /// <summary>
        /// 启动网关服务
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        Task StartAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 停止网关服务
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        Task StopAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 获取网关状态
        /// </summary>
        GatewayStatus GetStatus();

        /// <summary>
        /// 获取连接统计信息
        /// </summary>
        ConnectionStatistics GetConnectionStatistics();

        /// <summary>
        /// 获取性能指标
        /// </summary>
        PerformanceMetrics GetPerformanceMetrics();

        /// <summary>
        /// 状态变更事件
        /// </summary>
        event EventHandler<GatewayStatusChangedEventArgs>? StatusChanged;
    }

    /// <summary>
    /// 网关状态
    /// </summary>
    public enum GatewayStatus
    {
        /// <summary>
        /// 已停止
        /// </summary>
        Stopped,

        /// <summary>
        /// 正在启动
        /// </summary>
        Starting,

        /// <summary>
        /// 运行中
        /// </summary>
        Running,

        /// <summary>
        /// 正在停止
        /// </summary>
        Stopping,

        /// <summary>
        /// 错误状态
        /// </summary>
        Error
    }

    /// <summary>
    /// 连接统计信息
    /// </summary>
    public class ConnectionStatistics
    {
        /// <summary>
        /// 当前连接数
        /// </summary>
        public int CurrentConnections { get; set; }

        /// <summary>
        /// 总连接数
        /// </summary>
        public long TotalConnections { get; set; }

        /// <summary>
        /// 总断开连接数
        /// </summary>
        public long TotalDisconnections { get; set; }

        /// <summary>
        /// 峰值连接数
        /// </summary>
        public int PeakConnections { get; set; }

        /// <summary>
        /// 错误连接数
        /// </summary>
        public long ErrorConnections { get; set; }

        /// <summary>
        /// 统计开始时间
        /// </summary>
        public DateTime StartTime { get; set; }

        /// <summary>
        /// 最后更新时间
        /// </summary>
        public DateTime LastUpdateTime { get; set; }
    }

    /// <summary>
    /// 性能指标
    /// </summary>
    public class PerformanceMetrics
    {
        /// <summary>
        /// CPU使用率 (%)
        /// </summary>
        public double CpuUsage { get; set; }

        /// <summary>
        /// 内存使用量 (MB)
        /// </summary>
        public long MemoryUsage { get; set; }

        /// <summary>
        /// 网络入站流量 (bytes/sec)
        /// </summary>
        public long NetworkInbound { get; set; }

        /// <summary>
        /// 网络出站流量 (bytes/sec)
        /// </summary>
        public long NetworkOutbound { get; set; }

        /// <summary>
        /// 消息处理速率 (msg/sec)
        /// </summary>
        public int MessageProcessingRate { get; set; }

        /// <summary>
        /// 平均响应时间 (ms)
        /// </summary>
        public double AverageResponseTime { get; set; }

        /// <summary>
        /// 错误率 (%)
        /// </summary>
        public double ErrorRate { get; set; }
    }

    /// <summary>
    /// 网关状态变更事件参数
    /// </summary>
    public class GatewayStatusChangedEventArgs : EventArgs
    {
        /// <summary>
        /// 旧状态
        /// </summary>
        public GatewayStatus OldStatus { get; }

        /// <summary>
        /// 新状态
        /// </summary>
        public GatewayStatus NewStatus { get; }

        /// <summary>
        /// 变更时间
        /// </summary>
        public DateTime Timestamp { get; }

        /// <summary>
        /// 变更原因
        /// </summary>
        public string? Reason { get; }

        public GatewayStatusChangedEventArgs(GatewayStatus oldStatus, GatewayStatus newStatus, string? reason = null)
        {
            OldStatus = oldStatus;
            NewStatus = newStatus;
            Timestamp = DateTime.UtcNow;
            Reason = reason;
        }
    }
}
