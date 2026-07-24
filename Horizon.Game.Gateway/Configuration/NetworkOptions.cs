using System.ComponentModel.DataAnnotations;

namespace Horizon.Game.Gateway.Configuration
{
    /// <summary>
    /// 网络配置选项
    /// </summary>
    public class NetworkOptions
    {
        /// <summary>
        /// TCP监听端口
        /// </summary>
        [Range(1024, 65535)]
        public int TcpPort { get; set; } = 7789;

        /// <summary>
        /// UDP监听端口
        /// </summary>
        [Range(1024, 65535)]
        public int UdpPort { get; set; } = 8889;

        /// <summary>
        /// WebSocket监听端口
        /// </summary>
        [Range(1024, 65535)]
        public int WebSocketPort { get; set; } = 8890;

        /// <summary>
        /// 监听IP地址
        /// </summary>
        public string IpAddress { get; set; } = "0.0.0.0";

        /// <summary>
        /// 是否启用Nagle算法
        /// </summary>
        public bool NoDelay { get; set; } = true;

        /// <summary>
        /// Socket发送缓冲区大小
        /// </summary>
        [Range(1024, 1048576)]
        public int SendBufferSize { get; set; } = 32768;

        /// <summary>
        /// Socket接收缓冲区大小
        /// </summary>
        [Range(1024, 1048576)]
        public int ReceiveBufferSize { get; set; } = 32768;

        /// <summary>
        /// 监听队列长度
        /// </summary>
        [Range(1, 1000)]
        public int Backlog { get; set; } = 100;

        /// <summary>
        /// Keep-Alive间隔（毫秒）
        /// </summary>
        [Range(1000, 300000)]
        public int KeepAliveInterval { get; set; } = 30000;

        /// <summary>
        /// Keep-Alive超时时间（毫秒）
        /// </summary>
        [Range(1000, 300000)]
        public int KeepAliveTimeout { get; set; } = 5000;

        /// <summary>
        /// 连接空闲超时时间（秒）。<br/>
        /// 当连接超过此时间未收到任何数据（含心跳）时，判定为客户端非正常断开并主动清理。<br/>
        /// 这是检测客户端非正常断开（关进程/断网）的最可靠机制，不依赖 TCP KeepAlive 和 TouchSocket Online 属性。<br/>
        /// 客户端心跳间隔约 20 秒，建议设置为心跳间隔的 3 倍（默认 60 秒）。
        /// </summary>
        [Range(10, 600)]
        public int IdleTimeoutSeconds { get; set; } = 60;

        /// <summary>
        /// 首包超时时间（秒）。<br/>
        /// 连接建立后，客户端必须在此时间内发送第一个数据包（登录/握手请求）。<br/>
        /// 超时未收到任何数据的连接将被判定为幽灵连接（探测/错误连接/客户端崩溃）并立即清理。<br/>
        /// 这区别于 <see cref="IdleTimeoutSeconds"/>：首包超时针对"从未发送数据"的新连接，<br/>
        /// 空闲超时针对"曾发送数据但长时间无活动"的已认证连接。<br/>
        /// 正常客户端连接后会立即发送登录请求，建议设置较短（默认 10 秒）。
        /// </summary>
        [Range(3, 60)]
        public int FirstPacketTimeoutSeconds { get; set; } = 10;

        /// <summary>
        /// 是否启用SSL/TLS
        /// </summary>
        public bool EnableSsl { get; set; } = false;

        /// <summary>
        /// SSL证书路径
        /// </summary>
        public string? SslCertificatePath { get; set; }

        /// <summary>
        /// SSL证书密码
        /// </summary>
        public string? SslCertificatePassword { get; set; }
    }
}
