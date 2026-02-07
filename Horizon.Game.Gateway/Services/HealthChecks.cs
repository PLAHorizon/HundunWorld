using Microsoft.Extensions.Diagnostics.HealthChecks;
using Horizon.Game.Gateway.Services;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Horizon.Game.Gateway.Services
{
    /// <summary>
    /// 网关健康检查
    /// </summary>
    public class GatewayHealthCheck : IHealthCheck
    {
        private readonly IGatewayService _gatewayService;

        public GatewayHealthCheck(IGatewayService gatewayService)
        {
            _gatewayService = gatewayService;
        }

        public Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context, 
            CancellationToken cancellationToken = default)
        {
            try
            {
                var status = _gatewayService.GetStatus();
                var connectionStats = _gatewayService.GetConnectionStatistics();
                var performanceMetrics = _gatewayService.GetPerformanceMetrics();

                // 检查网关状态
                if (status != GatewayStatus.Running)
                {
                    return Task.FromResult(HealthCheckResult.Unhealthy(
                        $"网关状态异常: {status}"));
                }

                // 检查连接数是否正常
                if (connectionStats.ErrorConnections > connectionStats.TotalConnections * 0.1)
                {
                    return Task.FromResult(HealthCheckResult.Degraded(
                        $"错误连接比例过高: {connectionStats.ErrorConnections}/{connectionStats.TotalConnections}"));
                }

                // 检查错误率
                if (performanceMetrics.ErrorRate > 5.0)
                {
                    return Task.FromResult(HealthCheckResult.Degraded(
                        $"错误率过高: {performanceMetrics.ErrorRate:F2}%"));
                }

                // 检查内存使用
                if (performanceMetrics.MemoryUsage > 1024) // 1GB
                {
                    return Task.FromResult(HealthCheckResult.Degraded(
                        $"内存使用过高: {performanceMetrics.MemoryUsage}MB"));
                }

                return Task.FromResult(HealthCheckResult.Healthy(
                    $"网关运行正常 - 连接数: {connectionStats.CurrentConnections}, " +
                    $"内存: {performanceMetrics.MemoryUsage}MB"));
            }
            catch (Exception ex)
            {
                return Task.FromResult(HealthCheckResult.Unhealthy(
                    "健康检查失败", ex));
            }
        }
    }

    /// <summary>
    /// 网络健康检查
    /// </summary>
    public class NetworkHealthCheck : IHealthCheck
    {
        private readonly IConnectionManager _connectionManager;

        public NetworkHealthCheck(IConnectionManager connectionManager)
        {
            _connectionManager = connectionManager;
        }

        public Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context, 
            CancellationToken cancellationToken = default)
        {
            try
            {
                var stats = _connectionManager.GetStatistics();
                var networkStats = _connectionManager.GetNetworkStatistics();

                // 检查网络错误率
                var totalMessages = networkStats.MessagesReceived + networkStats.MessagesSent;
                if (totalMessages > 0)
                {
                    var errorRate = (double)networkStats.Errors / totalMessages * 100;
                    if (errorRate > 1.0)
                    {
                        return Task.FromResult(HealthCheckResult.Degraded(
                            $"网络错误率过高: {errorRate:F2}%"));
                    }
                }

                // 检查平均延迟
                if (networkStats.AverageLatency > 1000) // 1秒
                {
                    return Task.FromResult(HealthCheckResult.Degraded(
                        $"网络延迟过高: {networkStats.AverageLatency:F0}ms"));
                }

                return Task.FromResult(HealthCheckResult.Healthy(
                    $"网络运行正常 - 延迟: {networkStats.AverageLatency:F0}ms, " +
                    $"消息: {totalMessages}, 错误: {networkStats.Errors}"));
            }
            catch (Exception ex)
            {
                return Task.FromResult(HealthCheckResult.Unhealthy(
                    "网络健康检查失败", ex));
            }
        }
    }
}
