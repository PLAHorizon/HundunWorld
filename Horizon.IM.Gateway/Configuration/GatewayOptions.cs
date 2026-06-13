namespace Horizon.IM.Gateway.Configuration;

/// <summary>
/// IM 网关注册/集群协调相关配置。
/// </summary>
public class GatewayOptions
{
    /// <summary>
    /// 网关实例 ID（集群内唯一）。
    /// </summary>
    public string GatewayId { get; set; } = "TYMYD-IMGateway-001";

    /// <summary>
    /// 集群 ID，默认沿用 Orleans.ClusterId。
    /// </summary>
    public string ClusterId { get; set; } = string.Empty;

    /// <summary>
    /// 对外公布的 IP 或主机名。为空时回退到 Network:IpAddress；若仍为 0.0.0.0 则使用本机主机名。
    /// </summary>
    public string PublicIpAddress { get; set; } = string.Empty;

    /// <summary>
    /// 对外公布的端口。为 0 时回退到 Network:TcpPort。
    /// </summary>
    public int PublicPort { get; set; } = 0;

    /// <summary>
    /// Redis 连接字符串，用于将网关实例信息写入共享注册中心。
    /// </summary>
    public string RedisConnectionString { get; set; } = "localhost:6379";

    /// <summary>
    /// 地域/区域标识。
    /// </summary>
    public string Region { get; set; } = string.Empty;

    /// <summary>
    /// 注册心跳间隔（秒）。
    /// </summary>
    public int RegistryHeartbeatIntervalSeconds { get; set; } = 30;
}
