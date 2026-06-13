using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Horizon.Game.Gateway.Configuration;
using Horizon.Game.Gateway.Network;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Horizon.Game.Gateway.Services
{
    /// <summary>
    /// 网关服务实现
    /// </summary>
    public class GatewayService : IGatewayService
    {
        private readonly ILogger<GatewayService> _logger;
        private readonly IOptionsMonitor<GatewayOptions> _gatewayOptions;
        private readonly IConnectionManager _connectionManager;
        private readonly GameNetworkServer _networkServer;
        private readonly ILoadBalancer _loadBalancer;
        private readonly ISessionManager _sessionManager;

        private GatewayStatus _status = GatewayStatus.Stopped;
        private readonly object _statusLock = new();
        private readonly ConnectionStatistics _connectionStats = new();
        private readonly PerformanceMetrics _performanceMetrics = new();
        private Timer? _statisticsTimer;
        private readonly Stopwatch _uptimeStopwatch = new();

        /// <summary>
        /// 状态变更事件
        /// </summary>
        public event EventHandler<GatewayStatusChangedEventArgs>? StatusChanged;

        public GatewayService(
            ILogger<GatewayService> logger,
            IOptionsMonitor<GatewayOptions> gatewayOptions,
            IConnectionManager connectionManager,
            GameNetworkServer networkServer,
            ILoadBalancer loadBalancer,
            ISessionManager sessionManager)
        {
            _logger = logger;
            _gatewayOptions = gatewayOptions;
            _connectionManager = connectionManager;
            _networkServer = networkServer;
            _loadBalancer = loadBalancer;
            _sessionManager = sessionManager;

            // 初始化统计信息
            _connectionStats.StartTime = DateTime.UtcNow;
            _connectionStats.LastUpdateTime = DateTime.UtcNow;
        }

        /// <summary>
        /// 启动网关服务
        /// </summary>
        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogInformation("正在启动混沌世界游戏网关...");
                ChangeStatus(GatewayStatus.Starting, "服务启动中");

                await _networkServer.StartAsync(cancellationToken);
                _logger.LogInformation("网络服务器启动成功");

                _logger.LogInformation("消息路由器启动成功");

                try
                {
                    await _loadBalancer.StartAsync(cancellationToken);
                    _logger.LogInformation("负载均衡器启动成功");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "负载均衡器启动失败，回滚已启动的组件");
                    await RollbackStartedComponentsAsync(cancellationToken);
                    throw;
                }

                try
                {
                    await _sessionManager.StartAsync(cancellationToken);
                    _logger.LogInformation("会话管理器启动成功");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "会话管理器启动失败，回滚已启动的组件");
                    await RollbackStartedComponentsAsync(cancellationToken);
                    throw;
                }

                StartStatisticsTimer();
                _uptimeStopwatch.Start();

                ChangeStatus(GatewayStatus.Running, "服务启动完成");
                _logger.LogInformation("混沌世界游戏网关启动成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "网关启动失败");
                ChangeStatus(GatewayStatus.Error, $"启动失败: {ex.Message}");
                throw;
            }
        }

        private async Task RollbackStartedComponentsAsync(CancellationToken cancellationToken)
        {
            try
            {
                await _sessionManager.StopAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "回滚会话管理器时发生错误");
            }

            try
            {
                await _loadBalancer.StopAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "回滚负载均衡器时发生错误");
            }

            try
            {
                await _networkServer.StopAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "回滚网络服务器时发生错误");
            }
        }

        /// <summary>
        /// 停止网关服务
        /// </summary>
        public async Task StopAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogInformation("正在停止混沌世界游戏网关...");
                ChangeStatus(GatewayStatus.Stopping, "服务停止中");

                _statisticsTimer?.Dispose();
                _statisticsTimer = null;

                _uptimeStopwatch.Stop();

                try
                {
                    await _sessionManager.StopAsync(cancellationToken);
                    _logger.LogInformation("会话管理器已停止");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "停止会话管理器时发生错误，继续停止其他组件");
                }

                try
                {
                    await _loadBalancer.StopAsync(cancellationToken);
                    _logger.LogInformation("负载均衡器已停止");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "停止负载均衡器时发生错误，继续停止其他组件");
                }

                _logger.LogInformation("消息路由器已停止");

                try
                {
                    await _networkServer.StopAsync(cancellationToken);
                    _logger.LogInformation("网络服务器已停止");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "停止网络服务器时发生错误");
                }

                ChangeStatus(GatewayStatus.Stopped, "服务停止完成");
                _logger.LogInformation("混沌世界游戏网关停止成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "网关停止时发生错误");
                ChangeStatus(GatewayStatus.Error, $"停止失败: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 获取网关状态
        /// </summary>
        public GatewayStatus GetStatus()
        {
            lock (_statusLock)
            {
                return _status;
            }
        }

        /// <summary>
        /// 获取连接统计信息
        /// </summary>
        public ConnectionStatistics GetConnectionStatistics()
        {
            UpdateConnectionStatistics();
            return new ConnectionStatistics
            {
                CurrentConnections = _connectionStats.CurrentConnections,
                TotalConnections = _connectionStats.TotalConnections,
                TotalDisconnections = _connectionStats.TotalDisconnections,
                PeakConnections = _connectionStats.PeakConnections,
                ErrorConnections = _connectionStats.ErrorConnections,
                StartTime = _connectionStats.StartTime,
                LastUpdateTime = _connectionStats.LastUpdateTime
            };
        }

        /// <summary>
        /// 获取性能指标
        /// </summary>
        public PerformanceMetrics GetPerformanceMetrics()
        {
            UpdatePerformanceMetrics();
            return new PerformanceMetrics
            {
                CpuUsage = _performanceMetrics.CpuUsage,
                MemoryUsage = _performanceMetrics.MemoryUsage,
                NetworkInbound = _performanceMetrics.NetworkInbound,
                NetworkOutbound = _performanceMetrics.NetworkOutbound,
                MessageProcessingRate = _performanceMetrics.MessageProcessingRate,
                AverageResponseTime = _performanceMetrics.AverageResponseTime,
                ErrorRate = _performanceMetrics.ErrorRate
            };
        }

        /// <summary>
        /// 更改网关状态
        /// </summary>
        private void ChangeStatus(GatewayStatus newStatus, string? reason = null)
        {
            GatewayStatus oldStatus;
            lock (_statusLock)
            {
                oldStatus = _status;
                _status = newStatus;
            }

            if (oldStatus != newStatus)
            {
                _logger.LogInformation("网关状态变更: {OldStatus} -> {NewStatus}, 原因: {Reason}",
                    oldStatus, newStatus, reason ?? "无");

                StatusChanged?.Invoke(this, new GatewayStatusChangedEventArgs(oldStatus, newStatus, reason));
            }
        }

        /// <summary>
        /// 启动统计定时器
        /// </summary>
        private void StartStatisticsTimer()
        {
            var interval = TimeSpan.FromSeconds(_gatewayOptions.CurrentValue.StatisticsInterval);
            _statisticsTimer = new Timer(UpdateStatistics, null, interval, interval);
        }

        /// <summary>
        /// 更新统计信息
        /// </summary>
        private void UpdateStatistics(object? state)
        {
            try
            {
                UpdateConnectionStatistics();
                UpdatePerformanceMetrics();

                if (_gatewayOptions.CurrentValue.EnableVerboseLogging)
                {
                    _logger.LogDebug("统计信息更新: 连接数={CurrentConnections}, CPU={CpuUsage:F1}%, 内存={MemoryUsage}MB",
                        _connectionStats.CurrentConnections,
                        _performanceMetrics.CpuUsage,
                        _performanceMetrics.MemoryUsage);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "更新统计信息时发生错误");
            }
        }

        /// <summary>
        /// 更新连接统计信息
        /// </summary>
        private void UpdateConnectionStatistics()
        {
            var connStats = _connectionManager.GetStatistics();
            _connectionStats.CurrentConnections = connStats.ActiveConnections;
            _connectionStats.TotalConnections = connStats.TotalConnections;
            _connectionStats.TotalDisconnections = connStats.TotalDisconnections;
            _connectionStats.PeakConnections = Math.Max(_connectionStats.PeakConnections, connStats.ActiveConnections);
            _connectionStats.ErrorConnections = connStats.ErrorConnections;
            _connectionStats.LastUpdateTime = DateTime.UtcNow;
        }

        /// <summary>
        /// 更新性能指标
        /// </summary>
        private void UpdatePerformanceMetrics()
        {
            var process = Process.GetCurrentProcess();
            
            // CPU使用率（简化计算）
            _performanceMetrics.CpuUsage = process.TotalProcessorTime.TotalMilliseconds / Environment.TickCount * 100;
            
            // 内存使用量
            _performanceMetrics.MemoryUsage = process.WorkingSet64 / 1024 / 1024;
            
            // 网络流量统计（从连接管理器获取）
            var networkStats = _connectionManager.GetNetworkStatistics();
            _performanceMetrics.NetworkInbound = networkStats.BytesReceived;
            _performanceMetrics.NetworkOutbound = networkStats.BytesSent;
            
            // 消息处理速率（从消息路由器获取）
            //var routerStats = _messageRouter.GetStatistics();
            //_performanceMetrics.MessageProcessingRate = routerStats.MessagesPerSecond;
            //_performanceMetrics.AverageResponseTime = routerStats.AverageResponseTime;
            //_performanceMetrics.ErrorRate = routerStats.ErrorRate;
        }
    }
}
