using Horizon.Game.Message.Enums;
using System;
using System.Collections.Generic;

namespace HundunWorld.Game.Network
{
    /// <summary>
    /// 重连上下文信息
    /// 包含重连过程中的各种状态和配置信息
    /// </summary>
    public class ReconnectContext
    {
        /// <summary>
        /// 网络状态
        /// </summary>
        public NetworkStatus NetworkStatus { get; set; }

        /// <summary>
        /// 当前网关信息
        /// </summary>
        public GatewayInfo CurrentGateway { get; set; }

        /// <summary>
        /// 可用网关列表
        /// </summary>
        public List<GatewayInfo> AvailableGateways { get; set; }

        /// <summary>
        /// 重连尝试次数
        /// </summary>
        public int ReconnectAttempts { get; set; }

        /// <summary>
        /// 上次重连时间
        /// </summary>
        public DateTime LastReconnectTime { get; set; }

        /// <summary>
        /// 重连策略
        /// </summary>
        public ReconnectStrategy Strategy { get; set; }

        /// <summary>
        /// 重连原因
        /// </summary>
        public ReconnectReason Reason { get; set; }

        /// <summary>
        /// 是否正在切换网关
        /// </summary>
        public bool IsSwitchingGateway { get; set; }

        /// <summary>
        /// 重连开始时间
        /// </summary>
        public DateTime ReconnectStartTime { get; set; }

        /// <summary>
        /// 重连配置
        /// </summary>
        public ReconnectConfig ReconnectConfig { get; set; }

        /// <summary>
        /// 构造函数
        /// </summary>
        public ReconnectContext()
        {
            AvailableGateways = new List<GatewayInfo>();
            ReconnectStartTime = DateTime.UtcNow;
            LastReconnectTime = DateTime.MinValue;
            ReconnectAttempts = 0;
            NetworkStatus = NetworkStatus.Unknown;
            IsSwitchingGateway = false;
        }

        /// <summary>
        /// 重置重连上下文
        /// </summary>
        public void Reset()
        {
            ReconnectAttempts = 0;
            LastReconnectTime = DateTime.MinValue;
            IsSwitchingGateway = false;
            ReconnectStartTime = DateTime.UtcNow;
        }

        /// <summary>
        /// 更新重连尝试次数
        /// </summary>
        public void IncrementReconnectAttempts()
        {
            ReconnectAttempts++;
            LastReconnectTime = DateTime.UtcNow;
        }

        /// <summary>
        /// 获取已用时间（秒）
        /// </summary>
        /// <returns>已用时间（秒）</returns>
        public int GetElapsedSeconds()
        {
            return (int)(DateTime.UtcNow - ReconnectStartTime).TotalSeconds;
        }

        /// <summary>
        /// 检查是否超过最大重连持续时间
        /// </summary>
        /// <returns>是否超过最大重连持续时间</returns>
        public bool IsMaxReconnectDurationExceeded()
        {
            if (ReconnectConfig == null)
                return false;

            return (DateTime.UtcNow - ReconnectStartTime) > ReconnectConfig.MaxReconnectDuration;
        }

        /// <summary>
        /// 检查是否超过最大重连尝试次数
        /// </summary>
        /// <returns>是否超过最大重连尝试次数</returns>
        public bool IsMaxReconnectAttemptsExceeded()
        {
            if (ReconnectConfig == null || ReconnectConfig.MaxReconnectAttempts <= 0)
                return false;

            return ReconnectAttempts >= ReconnectConfig.MaxReconnectAttempts;
        }
    }
}