using Horizon.Strategy.Storage.Redis;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

namespace Horizon.Game.Gateway.Services
{
    public class RedisClusterStorage
    {
        private readonly Lazy<RedisCache> _redisCacheLazy;
        private readonly ILogger<RedisClusterStorage> _logger;
        private readonly string _clusterId;
        private readonly string _gatewayInstancesKey;
        private readonly string _connectionDistributionKey;
        private readonly TimeSpan _instanceExpiration;
        private readonly int _maxRetryAttempts = 3;
        private readonly TimeSpan _retryDelay = TimeSpan.FromMilliseconds(500);

        private RedisCache RedisCache => _redisCacheLazy.Value;

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
            _instanceExpiration = TimeSpan.FromMinutes(5);

            _redisCacheLazy = new Lazy<RedisCache>(() =>
            {
                try
                {
                    var cache = new RedisCache(connectionString, db);
                    _logger.LogInformation("Redis缓存延迟初始化成功，集群ID: {ClusterId}", _clusterId);
                    return cache;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Redis缓存延迟初始化失败，集群ID: {ClusterId}", _clusterId);
                    throw;
                }
            });
        }

        public async Task RegisterGatewayInstanceAsync(GatewayInstanceInfo instanceInfo)
        {
            for (int attempt = 1; attempt <= _maxRetryAttempts; attempt++)
            {
                try
                {
                    var instanceKey = GetInstanceKey(instanceInfo.InstanceId);
                    var jsonData = JsonSerializer.Serialize(instanceInfo);

                    await RedisCache.SetAsync(instanceKey, jsonData, _instanceExpiration);
                    await RedisCache.AddItemToSetAsync(_gatewayInstancesKey, instanceInfo.InstanceId);

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

        public async Task UpdateGatewayInstanceHeartbeatAsync(string instanceId)
        {
            for (int attempt = 1; attempt <= _maxRetryAttempts; attempt++)
            {
                try
                {
                    var instanceKey = GetInstanceKey(instanceId);
                    await RedisCache.ExpireEntryInAsync(instanceKey, _instanceExpiration);
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

        public async Task<List<GatewayInstanceInfo>> GetAllGatewayInstancesAsync()
        {
            var instances = new List<GatewayInstanceInfo>();

            for (int attempt = 1; attempt <= _maxRetryAttempts; attempt++)
            {
                try
                {
                    var instanceIds = await RedisCache.GetAllItemsFromSetAsync(_gatewayInstancesKey);

                    foreach (var instanceId in instanceIds)
                    {
                        var instanceKey = GetInstanceKey(instanceId);
                        var jsonData = await RedisCache.GetAsync(instanceKey);

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
                                await RemoveGatewayInstanceAsync(instanceId);
                            }
                        }
                        else
                        {
                            await RedisCache.RemoveItemFromSetAsync(_gatewayInstancesKey, instanceId);
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

        public async Task<GatewayInstanceInfo> GetGatewayInstanceAsync(string instanceId)
        {
            for (int attempt = 1; attempt <= _maxRetryAttempts; attempt++)
            {
                try
                {
                    var instanceKey = GetInstanceKey(instanceId);
                    var jsonData = await RedisCache.GetAsync(instanceKey);

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

        public async Task RemoveGatewayInstanceAsync(string instanceId)
        {
            for (int attempt = 1; attempt <= _maxRetryAttempts; attempt++)
            {
                try
                {
                    var instanceKey = GetInstanceKey(instanceId);
                    await RedisCache.RemoveAsync(instanceKey);
                    await RedisCache.RemoveItemFromSetAsync(_gatewayInstancesKey, instanceId);
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

        public async Task UpdateConnectionDistributionAsync(ConnectionDistributionInfo distributionInfo)
        {
            for (int attempt = 1; attempt <= _maxRetryAttempts; attempt++)
            {
                try
                {
                    var jsonData = JsonSerializer.Serialize(distributionInfo);
                    await RedisCache.SetAsync(_connectionDistributionKey, jsonData, TimeSpan.FromMinutes(10));
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

        public async Task<ConnectionDistributionInfo> GetConnectionDistributionAsync()
        {
            for (int attempt = 1; attempt <= _maxRetryAttempts; attempt++)
            {
                try
                {
                    var jsonData = await RedisCache.GetAsync(_connectionDistributionKey);

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

        public async Task SaveClusterSnapshotAsync(ClusterState clusterState)
        {
            for (int attempt = 1; attempt <= _maxRetryAttempts; attempt++)
            {
                try
                {
                    var snapshotKey = $"cluster:{_clusterId}:snapshot";
                    var jsonData = JsonSerializer.Serialize(clusterState);
                    await RedisCache.SetAsync(snapshotKey, jsonData, TimeSpan.FromHours(24));
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

        public async Task<ClusterState> RestoreClusterSnapshotAsync()
        {
            for (int attempt = 1; attempt <= _maxRetryAttempts; attempt++)
            {
                try
                {
                    var snapshotKey = $"cluster:{_clusterId}:snapshot";
                    var jsonData = await RedisCache.GetAsync(snapshotKey);

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

        public async Task CleanupExpiredInstancesAsync()
        {
            for (int attempt = 1; attempt <= _maxRetryAttempts; attempt++)
            {
                try
                {
                    var instanceIds = await RedisCache.GetAllItemsFromSetAsync(_gatewayInstancesKey);

                    foreach (var instanceId in instanceIds)
                    {
                        var instanceKey = GetInstanceKey(instanceId);
                        var exists = await RedisCache.ExistsAsync(instanceKey);

                        if (!exists)
                        {
                            await RedisCache.RemoveItemFromSetAsync(_gatewayInstancesKey, instanceId);
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

        private string GetInstanceKey(string instanceId)
        {
            return $"cluster:{_clusterId}:instance:{instanceId}";
        }
    }
}
