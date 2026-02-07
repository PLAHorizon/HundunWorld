using Horizon.Strategy.Storage.Redis;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

namespace Horizon.Game.Gateway.Services
{
    /// <summary>
    /// Redis集群协调存储服务
    /// 用于存储网关实例信息和连接分布数据，支持容灾恢复
    /// </summary>
    public class RedisClusterStorage
    {
        private readonly RedisCache _redisCache;
        private readonly ILogger<RedisClusterStorage> _logger;
        private readonly string _clusterId;
        private readonly string _gatewayInstancesKey;
        private readonly string _connectionDistributionKey;
        private readonly TimeSpan _instanceExpiration;
        private readonly int _maxRetryAttempts = 3;
        private readonly TimeSpan _retryDelay = TimeSpan.FromMilliseconds(500);

        public RedisClusterStorage(
            ILogger<RedisClusterStorage> logger,
            string connectionString,
            string clusterId,
            int db = -1)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _clusterId = clusterId ?? throw new ArgumentNullException(nameof(clusterId));
            _gatewayInstancesKey = $"cluster:{_clusterId}:gateway_instances";
            _connectionDistributionKey = $"cluster:{_clusterId}:connection_distribution";
            _instanceExpiration = TimeSpan.FromMinutes(5); // 实例信息5分钟过期，用于检测失效实例

            try
            {
                _redisCache = new RedisCache(connectionString, db);
                _logger.LogInformation("Redis缓存初始化成功，集群ID: {ClusterId}", _clusterId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Redis缓存初始化失败，集群ID: {ClusterId}", _clusterId);
                throw;
            }
        }

        // ... rest of the existing code remains the same ...
        
        /// <summary>
        /// 注册网关实例
        /// </summary>
        public async Task RegisterGatewayInstanceAsync(GatewayInstanceInfo instanceInfo)
        {
            for (int attempt = 1; attempt <= _maxRetryAttempts; attempt++)
            {
                try
                {
                    var instanceKey = GetInstanceKey(instanceInfo.InstanceId);
                    var jsonData = JsonSerializer.Serialize(instanceInfo);
                    
                    // 存储实例详细信息
                    await _redisCache.SetAsync(instanceKey, jsonData, _instanceExpiration);
                    
                    // 将实例ID添加到实例列表集合中
                    await _redisCache.AddItemToSetAsync(_gatewayInstancesKey, instanceInfo.InstanceId);
                    
                    _logger.LogDebug("网关实例注册成功: {InstanceId}", instanceInfo.InstanceId);
                    return;
                }
                catch (Exception ex) when (attempt < _maxRetryAttempts)
                {
                    _logger.LogWarning(ex, "注册网关实例失败，正在进行第 {Attempt} 次重试: {InstanceId}", attempt, instanceInfo.InstanceId);
                    await Task.Delay(_retryDelay);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "注册网关实例失败，已达到最大重试次数: {InstanceId}", instanceInfo.InstanceId);
                    throw;
                }
            }
        }

        /// <summary>
        /// 更新网关实例心跳
        /// </summary>
        public async Task UpdateGatewayInstanceHeartbeatAsync(string instanceId)
        {
            for (int attempt = 1; attempt <= _maxRetryAttempts; attempt++)
            {
                try
                {
                    var instanceKey = GetInstanceKey(instanceId);
                    
                    // 延长实例信息的过期时间
                    await _redisCache.ExpireEntryInAsync(instanceKey, _instanceExpiration);
                    
                    _logger.LogDebug("网关实例心跳更新成功: {InstanceId}", instanceId);
                    return;
                }
                catch (Exception ex) when (attempt < _maxRetryAttempts)
                {
                    _logger.LogWarning(ex, "更新网关实例心跳失败，正在进行第 {Attempt} 次重试: {InstanceId}", attempt, instanceId);
                    await Task.Delay(_retryDelay);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "更新网关实例心跳失败，已达到最大重试次数: {InstanceId}", instanceId);
                    throw;
                }
            }
        }

        /// <summary>
        /// 获取所有有效的网关实例
        /// </summary>
        public async Task<List<GatewayInstanceInfo>> GetAllGatewayInstancesAsync()
        {
            var instances = new List<GatewayInstanceInfo>();

            for (int attempt = 1; attempt <= _maxRetryAttempts; attempt++)
            {
                try
                {
                    var instanceIds = await _redisCache.GetAllItemsFromSetAsync(_gatewayInstancesKey);

                    foreach (var instanceId in instanceIds)
                    {
                        var instanceKey = GetInstanceKey(instanceId);
                        var jsonData = await _redisCache.GetAsync(instanceKey);
                        
                        if (!string.IsNullOrEmpty(jsonData))
                        {
                            try
                            {
                                var instanceInfo = JsonSerializer.Deserialize<GatewayInstanceInfo>(jsonData);
                                instances.Add(instanceInfo);
                            }
                            catch (JsonException jsonEx)
                            {
                                _logger.LogWarning(jsonEx, "反序列化网关实例信息失败，将移除无效数据: {InstanceId}", instanceId);
                                // 如果反序列化失败，移除无效数据
                                await RemoveGatewayInstanceAsync(instanceId);
                            }
                        }
                        else
                        {
                            // 如果实例信息已过期，从实例列表中移除
                            await _redisCache.RemoveItemFromSetAsync(_gatewayInstancesKey, instanceId);
                        }
                    }

                    _logger.LogDebug("获取到 {Count} 个网关实例", instances.Count);
                    return instances;
                }
                catch (Exception ex) when (attempt < _maxRetryAttempts)
                {
                    _logger.LogWarning(ex, "获取网关实例列表失败，正在进行第 {Attempt} 次重试", attempt);
                    await Task.Delay(_retryDelay);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "获取网关实例列表失败，已达到最大重试次数");
                    throw;
                }
            }

            return instances;
        }

        /// <summary>
        /// 获取指定网关实例
        /// </summary>
        public async Task<GatewayInstanceInfo> GetGatewayInstanceAsync(string instanceId)
        {
            for (int attempt = 1; attempt <= _maxRetryAttempts; attempt++)
            {
                try
                {
                    var instanceKey = GetInstanceKey(instanceId);
                    var jsonData = await _redisCache.GetAsync(instanceKey);
                    
                    if (!string.IsNullOrEmpty(jsonData))
                    {
                        return JsonSerializer.Deserialize<GatewayInstanceInfo>(jsonData);
                    }
                    
                    return null;
                }
                catch (Exception ex) when (attempt < _maxRetryAttempts)
                {
                    _logger.LogWarning(ex, "获取网关实例失败，正在进行第 {Attempt} 次重试: {InstanceId}", attempt, instanceId);
                    await Task.Delay(_retryDelay);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "获取网关实例失败，已达到最大重试次数: {InstanceId}", instanceId);
                    throw;
                }
            }

            return null;
        }

        /// <summary>
        /// 移除网关实例
        /// </summary>
        public async Task RemoveGatewayInstanceAsync(string instanceId)
        {
            for (int attempt = 1; attempt <= _maxRetryAttempts; attempt++)
            {
                try
                {
                    var instanceKey = GetInstanceKey(instanceId);
                    
                    // 删除实例详细信息
                    await _redisCache.RemoveAsync(instanceKey);
                    
                    // 从实例列表中移除
                    await _redisCache.RemoveItemFromSetAsync(_gatewayInstancesKey, instanceId);
                    
                    _logger.LogDebug("网关实例移除成功: {InstanceId}", instanceId);
                    return;
                }
                catch (Exception ex) when (attempt < _maxRetryAttempts)
                {
                    _logger.LogWarning(ex, "移除网关实例失败，正在进行第 {Attempt} 次重试: {InstanceId}", attempt, instanceId);
                    await Task.Delay(_retryDelay);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "移除网关实例失败，已达到最大重试次数: {InstanceId}", instanceId);
                    throw;
                }
            }
        }

        /// <summary>
        /// 更新连接分布信息
        /// </summary>
        public async Task UpdateConnectionDistributionAsync(ConnectionDistributionInfo distributionInfo)
        {
            for (int attempt = 1; attempt <= _maxRetryAttempts; attempt++)
            {
                try
                {
                    var jsonData = JsonSerializer.Serialize(distributionInfo);
                    await _redisCache.SetAsync(_connectionDistributionKey, jsonData, TimeSpan.FromMinutes(10));
                    
                    _logger.LogDebug("连接分布信息更新成功");
                    return;
                }
                catch (Exception ex) when (attempt < _maxRetryAttempts)
                {
                    _logger.LogWarning(ex, "更新连接分布信息失败，正在进行第 {Attempt} 次重试", attempt);
                    await Task.Delay(_retryDelay);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "更新连接分布信息失败，已达到最大重试次数");
                    throw;
                }
            }
        }

        /// <summary>
        /// 获取连接分布信息
        /// </summary>
        public async Task<ConnectionDistributionInfo> GetConnectionDistributionAsync()
        {
            for (int attempt = 1; attempt <= _maxRetryAttempts; attempt++)
            {
                try
                {
                    var jsonData = await _redisCache.GetAsync(_connectionDistributionKey);
                    
                    if (!string.IsNullOrEmpty(jsonData))
                    {
                        return JsonSerializer.Deserialize<ConnectionDistributionInfo>(jsonData);
                    }
                    
                    return new ConnectionDistributionInfo
                    {
                        TotalConnections = 0,
                        InstanceConnections = new Dictionary<string, int>()
                    };
                }
                catch (Exception ex) when (attempt < _maxRetryAttempts)
                {
                    _logger.LogWarning(ex, "获取连接分布信息失败，正在进行第 {Attempt} 次重试", attempt);
                    await Task.Delay(_retryDelay);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "获取连接分布信息失败，已达到最大重试次数");
                    throw;
                }
            }

            return new ConnectionDistributionInfo
            {
                TotalConnections = 0,
                InstanceConnections = new Dictionary<string, int>()
            };
        }

        /// <summary>
        /// 保存集群状态快照（用于容灾恢复）
        /// </summary>
        public async Task SaveClusterSnapshotAsync(ClusterState clusterState)
        {
            for (int attempt = 1; attempt <= _maxRetryAttempts; attempt++)
            {
                try
                {
                    var snapshotKey = $"cluster:{_clusterId}:snapshot";
                    var jsonData = JsonSerializer.Serialize(clusterState);
                    
                    // 保存快照，设置较长的过期时间
                    await _redisCache.SetAsync(snapshotKey, jsonData, TimeSpan.FromHours(24));
                    
                    _logger.LogInformation("集群状态快照保存成功");
                    return;
                }
                catch (Exception ex) when (attempt < _maxRetryAttempts)
                {
                    _logger.LogWarning(ex, "保存集群状态快照失败，正在进行第 {Attempt} 次重试", attempt);
                    await Task.Delay(_retryDelay);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "保存集群状态快照失败，已达到最大重试次数");
                    throw;
                }
            }
        }

        /// <summary>
        /// 恢复集群状态快照
        /// </summary>
        public async Task<ClusterState> RestoreClusterSnapshotAsync()
        {
            for (int attempt = 1; attempt <= _maxRetryAttempts; attempt++)
            {
                try
                {
                    var snapshotKey = $"cluster:{_clusterId}:snapshot";
                    var jsonData = await _redisCache.GetAsync(snapshotKey);
                    
                    if (!string.IsNullOrEmpty(jsonData))
                    {
                        return JsonSerializer.Deserialize<ClusterState>(jsonData);
                    }
                    
                    return null;
                }
                catch (Exception ex) when (attempt < _maxRetryAttempts)
                {
                    _logger.LogWarning(ex, "恢复集群状态快照失败，正在进行第 {Attempt} 次重试", attempt);
                    await Task.Delay(_retryDelay);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "恢复集群状态快照失败，已达到最大重试次数");
                    throw;
                }
            }

            return null;
        }

        /// <summary>
        /// 清理过期的实例信息
        /// </summary>
        public async Task CleanupExpiredInstancesAsync()
        {
            for (int attempt = 1; attempt <= _maxRetryAttempts; attempt++)
            {
                try
                {
                    var instanceIds = await _redisCache.GetAllItemsFromSetAsync(_gatewayInstancesKey);
                    
                    foreach (var instanceId in instanceIds)
                    {
                        var instanceKey = GetInstanceKey(instanceId);
                        var exists = await _redisCache.ExistsAsync(instanceKey);
                        
                        if (!exists)
                        {
                            // 实例信息已过期，从实例列表中移除
                            await _redisCache.RemoveItemFromSetAsync(_gatewayInstancesKey, instanceId);
                        }
                    }
                    
                    _logger.LogDebug("过期实例清理完成");
                    return;
                }
                catch (Exception ex) when (attempt < _maxRetryAttempts)
                {
                    _logger.LogWarning(ex, "清理过期实例失败，正在进行第 {Attempt} 次重试", attempt);
                    await Task.Delay(_retryDelay);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "清理过期实例失败，已达到最大重试次数");
                    throw;
                }
            }
        }

        /// <summary>
        /// 获取实例详细信息的Redis键
        /// </summary>
        private string GetInstanceKey(string instanceId)
        {
            return $"cluster:{_clusterId}:instance:{instanceId}";
        }
    }
}