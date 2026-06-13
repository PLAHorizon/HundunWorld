using System.ComponentModel.DataAnnotations;

namespace Horizon.Game.Gateway.Configuration
{
    /// <summary>
    /// Orleans配置选项
    /// </summary>
    public class OrleansOptions
    {
        /// <summary>
        /// 集群ID
        /// </summary>
        [Required]
        public string ClusterId { get; set; } = "Dev";

        /// <summary>
        /// 服务ID
        /// </summary>
        [Required]
        public string ServiceId { get; set; } = "BaseService";

        /// <summary>
        /// Silo连接字符串
        /// </summary>
        public string[] SiloEndpoints { get; set; } = new[] { "localhost:11111" };

        /// <summary>
        /// 网关端口
        /// </summary>
        [Range(1024, 65535)]
        public int GatewayPort { get; set; } = 30000;

        /// <summary>
        /// 连接重试次数
        /// </summary>
        [Range(0, 100000)]
        public int RetryCount { get; set; } = 0;

        /// <summary>
        /// 连接重试间隔（毫秒）
        /// </summary>
        [Range(100, 60000)]
        public int RetryInterval { get; set; } = 1000;

        /// <summary>
        /// 响应超时时间（毫秒）
        /// </summary>
        [Range(1000, 300000)]
        public int ResponseTimeout { get; set; } = 30000;

        /// <summary>
        /// 是否启用统计
        /// </summary>
        public bool EnableStatistics { get; set; } = true;

        /// <summary>
        /// 是否启用性能计数器
        /// </summary>
        public bool EnablePerformanceCounters { get; set; } = true;
    }
}
