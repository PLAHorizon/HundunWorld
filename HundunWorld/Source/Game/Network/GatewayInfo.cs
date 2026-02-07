using System;

namespace HundunWorld.Game.Network
{
    /// <summary>
    /// 网关信息
    /// </summary>
    public class GatewayInfo
    {
        /// <summary>
        /// IP地址
        /// </summary>
        public string IP { get; set; } = string.Empty;

        /// <summary>
        /// 端口号
        /// </summary>
        public int Port { get; set; }

        /// <summary>
        /// 区域
        /// </summary>
        public string Region { get; set; } = string.Empty;

        /// <summary>
        /// 服务器负载
        /// </summary>
        public int Load { get; set; }

        /// <summary>
        /// 是否可用
        /// </summary>
        public bool IsAvailable { get; set; } = true;

        /// <summary>
        /// 失败次数
        /// </summary>
        public int FailureCount { get; set; }

        /// <summary>
        /// 最后测试时间
        /// </summary>
        public DateTime LastTestTime { get; set; } = DateTime.MinValue;

        /// <summary>
        /// 延迟（毫秒）
        /// </summary>
        public long Latency { get; set; } = long.MaxValue;

        public override string ToString()
        {
            return $"GatewayInfo[IP={IP}, Port={Port}, Region={Region}, Load={Load}, Latency={Latency}ms]";
        }
    }
}