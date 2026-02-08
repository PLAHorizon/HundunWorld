using Orleans;
using System;
using System.Threading.Tasks;
using Horizon.Game.Message.Network;

namespace Horizon.Orleans.Interface
{
    /// <summary>
    /// 游戏服务器状态管理Grain接口 - 负责服务器状态、在线人数、维护管理
    /// </summary>
    [global::Orleans.CodeGeneration.Version(1)]
    public interface IGameServerGrain : IGrainWithIntegerKey
    {
        /// <summary>
        /// 获取服务器状态信息
        /// </summary>
        Task<ServerStatusMessage> GetServerStatusAsync();

        /// <summary>
        /// 更新在线人数
        /// </summary>
        Task<bool> UpdateOnlineCountAsync(int onlineCount);

        /// <summary>
        /// 设置服务器维护状态
        /// </summary>
        Task<bool> SetMaintenanceAsync(bool isMaintenance, string reason);

        /// <summary>
        /// 初始化服务器信息
        /// </summary>
        Task<bool> InitializeServerAsync(string serverName, int maxOnlineCount);

        /// <summary>
        /// 更新服务器负载信息
        /// </summary>
        Task<bool> UpdateServerLoadAsync(float cpuUsage, float memoryUsage, long networkLatency);

        /// <summary>
        /// 玩家上线
        /// </summary>
        Task<bool> PlayerOnlineAsync(Guid playerId);

        /// <summary>
        /// 玩家下线
        /// </summary>
        Task<bool> PlayerOfflineAsync(Guid playerId);

        /// <summary>
        /// 获取在线玩家数量
        /// </summary>
        Task<int> GetOnlinePlayerCountAsync();
    }
}
