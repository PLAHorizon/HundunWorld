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

        /// <summary>
        /// 是否在验证鉴权令牌时检查客户端机器ID与令牌中记录的机器ID是否一致。
        /// 默认启用，以确保令牌绑定的机器ID与请求中携带的机器ID一致。
        /// 仅在调试或特殊部署场景下才应设为 false。
        /// </summary>
        public bool ValidateTokenMachineId { get; set; } = true;

        /// <summary>
        /// 对外公布的网关 IP 或主机名（写入 Redis 供客户端通过 WebApi 发现）。
        /// 为空时回退到 Network:IpAddress；若仍为 0.0.0.0 / 空，则使用本机主机名。
        /// </summary>
        public string PublicIpAddress { get; set; } = string.Empty;

        /// <summary>
        /// 对外公布的网关端口（写入 Redis）。为 0 时回退到 Network:TcpPort。
        /// </summary>
        public int PublicPort { get; set; } = 0;

        /// <summary>
        /// 网关注册心跳间隔（秒）。
        /// </summary>
        [Range(5, 300)]
        public int RegistryHeartbeatIntervalSeconds { get; set; } = 30;

        /// <summary>
        /// 灰度开关（P6-a）：启用后，网关将把出站 <c>SyncPacket</c> 路由到新版
        /// <c>SyncDispatcher</c>（走 ZoneShard→Session fanout 路径），而不是直接广播到 AOI 旧流。
        /// 默认 <c>false</c>；在可控节点（单集群、低 CCU 区）先打开，观察指标稳定后再全量切换。
        /// </summary>
        public bool UseSyncPacketDispatch { get; set; } = false;
    }
}