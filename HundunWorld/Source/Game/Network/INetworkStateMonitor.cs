using Horizon.Game.Message.Enums;
using System;
using System.Threading.Tasks;

namespace HundunWorld.Game.Network
{
    /// <summary>
    /// 网络状态监控器接口
    /// </summary>
    public interface INetworkStateMonitor : IDisposable
    {
        /// <summary>
        /// 网络状态变化事件
        /// </summary>
        event Action<NetworkStatus> NetworkStatusChanged;

        /// <summary>
        /// 获取当前网络状态
        /// </summary>
        /// <returns>当前网络状态</returns>
        NetworkStatus GetCurrentStatus();

        /// <summary>
        /// 检查网络是否可用
        /// </summary>
        /// <returns>网络是否可用</returns>
        Task<bool> IsNetworkAvailableAsync();

        /// <summary>
        /// 检查网关是否可达
        /// </summary>
        /// <param name="ip">网关IP地址</param>
        /// <param name="port">网关端口</param>
        /// <returns>网关是否可达</returns>
        Task<bool> IsGatewayReachableAsync(string ip, int port);

        /// <summary>
        /// 开始监控网络状态变化
        /// </summary>
        void StartMonitoring();
    }
}