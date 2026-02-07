using System.ComponentModel.DataAnnotations;

namespace Horizon.Game.Gateway.Configuration
{
    /// <summary>
    /// 网关配置选项
    /// </summary>
    public class GatewayOptions
    {
        /// <summary>
        /// 网关名称
        /// </summary>
        [Required]
        public string Name { get; set; } = "混沌世界游戏网关";

        /// <summary>
        /// 网关ID
        /// </summary>
        [Required]
        public string GatewayId { get; set; } = "TYMYD-Gateway-001";

        /// <summary>
        /// 集群ID
        /// </summary>
        public string ClusterId { get; set; } = "TYMYD-Cluster-001";

        /// <summary>
        /// Redis连接字符串
        /// </summary>
        public string RedisConnectionString { get; set; } = "localhost:6379";

        /// <summary>
        /// 服务器区域
        /// </summary>
        public string Region { get; set; } = "华东";

        /// <summary>
        /// 最大并发连接数
        /// </summary>
        [Range(1, 100000)]
        public int MaxConnections { get; set; } = 10000;

        /// <summary>
        /// 连接超时时间（秒）
        /// </summary>
        [Range(10, 3600)]
        public int ConnectionTimeout { get; set; } = 300;

        /// <summary>
        /// 心跳间隔（秒）
        /// </summary>
        [Range(10, 300)]
        public int HeartbeatInterval { get; set; } = 30;

        /// <summary>
        /// 是否启用消息压缩
        /// </summary>
        public bool EnableCompression { get; set; } = true;

        /// <summary>
        /// 是否启用消息加密
        /// </summary>
        public bool EnableEncryption { get; set; } = true;

        /// <summary>
        /// 消息缓冲区大小
        /// </summary>
        [Range(1024, 1048576)]
        public int BufferSize { get; set; } = 8192;

        /// <summary>
        /// 统计信息更新间隔（秒）
        /// </summary>
        [Range(1, 300)]
        public int StatisticsInterval { get; set; } = 60;

        /// <summary>
        /// 是否启用详细日志
        /// </summary>
        public bool EnableVerboseLogging { get; set; } = false;
    }
}