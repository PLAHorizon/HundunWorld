using Horizon.Game.Gateway.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Horizon.Game.Gateway.Services
{
    /// <summary>
    /// 集群协调服务实现
    /// </summary>
    public class ClusterCoordinationService : IClusterCoordinationService
    {
        private readonly ILogger<ClusterCoordinationService> _logger;
        private readonly IConnectionManager _connectionManager;
        private readonly RedisClusterStorage _redisStorage;
        private readonly string _instanceId;
        private readonly string _address;
        private readonly int _port;
        private readonly string _clusterId;
        private Timer? _heartbeatTimer;
        private Timer? _cleanupTimer;
        private bool _isRunning = false;

        public ClusterCoordinationService(
            ILogger<ClusterCoordinationService> logger,
            IConnectionManager connectionManager,
            RedisClusterStorage redisStorage,
            IOptionsMonitor<Configuration.GatewayOptions> gatewayOptions,
            IOptionsMonitor<NetworkOptions> networkOptions)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
            _redisStorage = redisStorage ?? throw new ArgumentNullException(nameof(redisStorage));
            
            var gatewayConfig = gatewayOptions.CurrentValue;
            var networkConfig = networkOptions.CurrentValue;
            
            _instanceId = gatewayConfig.GatewayId ?? Guid.NewGuid().ToString();
            _address = networkConfig.IpAddress ?? "localhost";
            _port = networkConfig.TcpPort;
            _clusterId = gatewayConfig.ClusterId ?? "default_cluster";
        }

        /// <summary>
        /// 注册网关实例
        /// </summary>
        public async Task RegisterGatewayInstanceAsync(GatewayInstanceInfo instanceInfo)
        {
            try
            {
                await _redisStorage.RegisterGatewayInstanceAsync(instanceInfo);
                _logger.LogInformation("网关实例已注册: {InstanceId}", instanceInfo.InstanceId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "注册网关实例失败: {InstanceId}", instanceInfo.InstanceId);
                throw;
            }
        }

        /// <summary>
        /// 获取所有网关实例
        /// </summary>
        public async Task<List<GatewayInstanceInfo>> GetAllGatewayInstancesAsync()
        {
            try
            {
                var instances = await _redisStorage.GetAllGatewayInstancesAsync();
                _logger.LogDebug("获取到 {Count} 个网关实例", instances.Count);
                return instances;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取网关实例列表失败");
                throw;
            }
        }

        /// <summary>
        /// 更新网关实例状态
        /// </summary>
        public async Task UpdateGatewayInstanceStateAsync(string instanceId, GatewayInstanceState state)
        {
            try
            {
                var instanceInfo = await _redisStorage.GetGatewayInstanceAsync(instanceId);
                if (instanceInfo != null)
                {
                    instanceInfo.State = state;
                    instanceInfo.LastUpdate = DateTime.UtcNow;
                    await _redisStorage.RegisterGatewayInstanceAsync(instanceInfo);
                    _logger.LogInformation("网关实例状态已更新: {InstanceId}, 状态: {State}", instanceId, state);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新网关实例状态失败: {InstanceId}", instanceId);
                throw;
            }
        }

        /// <summary>
        /// 获取连接分布信息
        /// </summary>
        public async Task<ConnectionDistributionInfo> GetConnectionDistributionAsync()
        {
            try
            {
                return await _redisStorage.GetConnectionDistributionAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取连接分布信息失败");
                throw;
            }
        }

        /// <summary>
        /// 更新连接分布信息
        /// </summary>
        public async Task UpdateConnectionDistributionAsync()
        {
            try
            {
                var instances = await GetAllGatewayInstancesAsync();
                var distribution = new ConnectionDistributionInfo
                {
                    TotalConnections = instances.Sum(i => i.ConnectionCount),
                    InstanceConnections = instances.ToDictionary(i => i.InstanceId, i => i.ConnectionCount)
                };

                await _redisStorage.UpdateConnectionDistributionAsync(distribution);
                _logger.LogDebug("连接分布信息已更新，总连接数: {TotalConnections}", distribution.TotalConnections);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新连接分布信息失败");
            }
        }

        /// <summary>
        /// 启动服务
        /// </summary>
        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            if (_isRunning)
                return;

            _isRunning = true;
            _logger.LogInformation("集群协调服务启动，实例ID: {InstanceId}, 集群ID: {ClusterId}", _instanceId, _clusterId);

            try
            {
                // 注册当前实例
                var instanceInfo = new GatewayInstanceInfo
                {
                    InstanceId = _instanceId,
                    Address = _address,
                    Port = _port,
                    ConnectionCount = _connectionManager.GetStatistics().ActiveConnections,
                    State = GatewayInstanceState.Running,
                    LastUpdate = DateTime.UtcNow
                };
                
                await RegisterGatewayInstanceAsync(instanceInfo);

                // 启动心跳定时器，定期更新实例状态
                _heartbeatTimer = new Timer(HeartbeatCallback, null, TimeSpan.Zero, TimeSpan.FromMinutes(1));
                
                // 启动清理定时器，定期清理过期实例
                _cleanupTimer = new Timer(CleanupCallback, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "集群协调服务启动失败");
                _isRunning = false;
                throw;
            }
        }

        /// <summary>
        /// 停止服务
        /// </summary>
        public async Task StopAsync(CancellationToken cancellationToken = default)
        {
            if (!_isRunning)
                return;

            _isRunning = false;
            _logger.LogInformation("集群协调服务停止");

            try
            {
                // 停止定时器
                _heartbeatTimer?.Dispose();
                _heartbeatTimer = null;
                
                _cleanupTimer?.Dispose();
                _cleanupTimer = null;

                // 更新实例状态为维护中
                await UpdateGatewayInstanceStateAsync(_instanceId, GatewayInstanceState.Maintenance);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "集群协调服务停止时发生错误");
            }
        }

        /// <summary>
        /// 心跳回调函数
        /// </summary>
        private async void HeartbeatCallback(object? state)
        {
            try
            {
                var stats = _connectionManager.GetStatistics();
                _logger.LogDebug("发送心跳，当前连接数: {ConnectionCount}", stats.ActiveConnections);

                // 更新实例信息
                var instanceInfo = new GatewayInstanceInfo
                {
                    InstanceId = _instanceId,
                    Address = _address,
                    Port = _port,
                    ConnectionCount = stats.ActiveConnections,
                    State = GatewayInstanceState.Running,
                    LastUpdate = DateTime.UtcNow
                };

                await RegisterGatewayInstanceAsync(instanceInfo);
                
                // 更新连接分布信息
                await UpdateConnectionDistributionAsync();
                
                // 更新心跳
                await _redisStorage.UpdateGatewayInstanceHeartbeatAsync(_instanceId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "心跳回调执行失败");
            }
        }

        /// <summary>
        /// 清理回调函数
        /// </summary>
        private async void CleanupCallback(object? state)
        {
            try
            {
                await _redisStorage.CleanupExpiredInstancesAsync();
                _logger.LogDebug("过期实例清理完成");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "清理过期实例失败");
            }
        }

        /// <summary>
        /// 保存集群状态快照（用于容灾恢复）
        /// </summary>
        public async Task SaveClusterSnapshotAsync()
        {
            try
            {
                var instances = await GetAllGatewayInstancesAsync();
                var clusterState = new ClusterState
                {
                    ClusterId = _clusterId,
                    Instances = instances,
                    TotalConnections = instances.Sum(i => i.ConnectionCount),
                    LastUpdate = DateTime.UtcNow
                };

                await _redisStorage.SaveClusterSnapshotAsync(clusterState);
                _logger.LogInformation("集群状态快照已保存");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "保存集群状态快照失败");
                throw;
            }
        }

        /// <summary>
        /// 恢复集群状态快照
        /// </summary>
        public async Task<ClusterState> RestoreClusterSnapshotAsync()
        {
            try
            {
                var clusterState = await _redisStorage.RestoreClusterSnapshotAsync();
                if (clusterState != null)
                {
                    _logger.LogInformation("集群状态快照已恢复，集群ID: {ClusterId}", clusterState.ClusterId);
                }
                else
                {
                    _logger.LogWarning("未找到集群状态快照");
                }
                
                return clusterState;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "恢复集群状态快照失败");
                throw;
            }
        }
    }
}