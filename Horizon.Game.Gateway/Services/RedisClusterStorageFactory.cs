using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;

namespace Horizon.Game.Gateway.Services
{
    /// <summary>
    /// Redis集群存储工厂类
    /// </summary>
    public class RedisClusterStorageFactory
    {
        private readonly ILogger<RedisClusterStorage> _logger;
        private readonly IOptionsMonitor<Configuration.GatewayOptions> _gatewayOptions;

        public RedisClusterStorageFactory(
            ILogger<RedisClusterStorage> logger,
            IOptionsMonitor<Configuration.GatewayOptions> gatewayOptions)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _gatewayOptions = gatewayOptions ?? throw new ArgumentNullException(nameof(gatewayOptions));
        }

        /// <summary>
        /// 创建Redis集群存储实例
        /// </summary>
        /// <param name="clusterId">集群ID</param>
        /// <param name="db">数据库编号</param>
        /// <returns>Redis集群存储实例</returns>
        public RedisClusterStorage CreateStorage(string clusterId = null, int db = -1)
        {
            var gatewayConfig = _gatewayOptions.CurrentValue;
            var connectionString = gatewayConfig.RedisConnectionString ?? "localhost:6379";
            var actualClusterId = clusterId ?? gatewayConfig.ClusterId ?? "default_cluster";

            return new RedisClusterStorage(_logger, connectionString, actualClusterId, db);
        }
    }
}