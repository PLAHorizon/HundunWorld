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
        /// 角色上线：将 characterId 添加到持久化在线列表 OnlinePlayers。<br/>
        /// 修复 BUG（角色离线后未能从服务端移除）：原签名使用 Guid playerId 但
        /// 业务层从未调用，导致 OnlinePlayers 持久化列表从未被维护。
        /// 现改为 long characterId，由 CharacterGrain.EnterGameAsync 调用。
        /// </summary>
        Task<bool> PlayerOnlineAsync(long characterId);

        /// <summary>
        /// 角色下线：将 characterId 从持久化在线列表 OnlinePlayers 移除并持久化。<br/>
        /// 修复 BUG（角色离线后未能从服务端移除）：原签名使用 Guid playerId 但
        /// 业务层从未调用，导致角色离线后持久化在线信息未更新、角色永久残留。
        /// 现改为 long characterId，由 CharacterGrain.GoOfflineAsync 和
        /// PlayerDespawnScheduler.DespawnImmediatelyAsync（兜底）调用，确保离线立即移除。
        /// </summary>
        Task<bool> PlayerOfflineAsync(long characterId);

        /// <summary>
        /// 获取在线玩家数量
        /// </summary>
        Task<int> GetOnlinePlayerCountAsync();
    }
}
