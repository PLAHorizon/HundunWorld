using System;

namespace Horizon.Core.Options
{
    /// <summary>
    /// Socket 链接终结点信息
    /// </summary>
    public class SocketEndpoint
    {
        /// <summary>
        /// 主机IP
        /// </summary>
        public string? Host { get; set; }
        /// <summary>
        /// 主机开发端口
        /// </summary>
        public int Port { get; set; }
        /// <summary>
        /// 主机连接方案
        /// </summary>
        public string? Scheme { get; set; }
        /// <summary>
        /// 过期时间
        /// </summary>
        public int ExpireIn { get; set; }
    }
}
