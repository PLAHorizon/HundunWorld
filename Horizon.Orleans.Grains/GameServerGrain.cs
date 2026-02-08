using Horizon.Game.Message.Network;
using Horizon.Orleans.Interface;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;
using MemoryPack;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Horizon.Orleans.Grains
{
    /// <summary>
    /// 游戏服务器状态
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class GameServerState
    {
        /// <summary>
        /// 服务器名称
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public string ServerName { get; set; } = "";

        /// <summary>
        /// 服务器是否已初始化
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public bool IsInitialized { get; set; }

        /// <summary>
        /// 服务器状态
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public int Status { get; set; } = (int)ServerStatus.Normal;

        /// <summary>
        /// 在线人数
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public int OnlineCount { get; set; }

        /// <summary>
        /// 最大在线人数
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public int MaxOnlineCount { get; set; } = 5000;

        /// <summary>
        /// CPU使用率
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public float CpuUsage { get; set; }

        /// <summary>
        /// 内存使用率
        /// </summary>
        [MemoryPackOrder(6)]
        [Id(6)]
        public float MemoryUsage { get; set; }

        /// <summary>
        /// 网络延迟(ms)
        /// </summary>
        [MemoryPackOrder(7)]
        [Id(7)]
        public long NetworkLatency { get; set; }

        /// <summary>
        /// 维护原因
        /// </summary>
        [MemoryPackOrder(8)]
        [Id(8)]
        public string MaintenanceReason { get; set; } = "";

        /// <summary>
        /// 最后更新时间
        /// </summary>
        [MemoryPackOrder(9)]
        [Id(9)]
        public long LastUpdateTime { get; set; }

        /// <summary>
        /// 在线玩家ID集合
        /// </summary>
        [MemoryPackOrder(10)]
        [Id(10)]
        public HashSet<Guid> OnlinePlayers { get; set; } = new();
    }

    /// <summary>
    /// 游戏服务器状态管理Grain实现
    /// </summary>
    public class GameServerGrain : Grain, IGameServerGrain
    {
        private readonly ILogger<GameServerGrain> _logger;
        private readonly IPersistentState<GameServerState> _serverState;

        public GameServerGrain(
            ILogger<GameServerGrain> logger,
            [PersistentState("gameServer", "GameStore")] IPersistentState<GameServerState> serverState)
        {
            _logger = logger;
            _serverState = serverState;
        }

        public override async Task OnActivateAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("GameServerGrain {GrainKey} activating.", this.GetPrimaryKeyLong());

            if (_serverState.State.OnlinePlayers == null)
                _serverState.State.OnlinePlayers = new HashSet<Guid>();

            await base.OnActivateAsync(cancellationToken);
        }

        public Task<ServerStatusMessage> GetServerStatusAsync()
        {
            try
            {
                var state = _serverState.State;

                var statusMessage = new ServerStatusMessage
                {
                    ServerId = (int)this.GetPrimaryKeyLong(),
                    ServerName = state.ServerName,
                    Status = (ServerStatus)state.Status,
                    OnlineCount = state.OnlinePlayers.Count,
                    MaxOnlineCount = state.MaxOnlineCount,
                    CpuUsage = state.CpuUsage,
                    MemoryUsage = state.MemoryUsage,
                    NetworkLatency = state.NetworkLatency,
                    UpdateTime = state.LastUpdateTime
                };

                return Task.FromResult(statusMessage);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取服务器状态失败");
                throw;
            }
        }

        public async Task<bool> UpdateOnlineCountAsync(int onlineCount)
        {
            try
            {
                if (onlineCount < 0)
                {
                    _logger.LogWarning("在线人数不能为负数: OnlineCount={OnlineCount}", onlineCount);
                    return false;
                }

                _serverState.State.OnlineCount = onlineCount;
                _serverState.State.LastUpdateTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                await _serverState.WriteStateAsync();

                _logger.LogDebug("更新在线人数: OnlineCount={OnlineCount}", onlineCount);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新在线人数失败");
                throw;
            }
        }

        public async Task<bool> SetMaintenanceAsync(bool isMaintenance, string reason)
        {
            try
            {
                var state = _serverState.State;

                if (isMaintenance)
                {
                    state.Status = (int)ServerStatus.Maintenance;
                    state.MaintenanceReason = reason ?? "";
                    _logger.LogInformation("服务器进入维护状态: Reason={Reason}", reason);
                }
                else
                {
                    state.Status = (int)ServerStatus.Normal;
                    state.MaintenanceReason = "";
                    _logger.LogInformation("服务器退出维护状态");
                }

                state.LastUpdateTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                await _serverState.WriteStateAsync();

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "设置维护状态失败");
                throw;
            }
        }

        public async Task<bool> InitializeServerAsync(string serverName, int maxOnlineCount)
        {
            try
            {
                var state = _serverState.State;

                if (state.IsInitialized)
                {
                    _logger.LogWarning("服务器已初始化: ServerName={ServerName}", state.ServerName);
                    return false;
                }

                if (string.IsNullOrWhiteSpace(serverName))
                {
                    _logger.LogWarning("服务器名称无效");
                    return false;
                }

                if (maxOnlineCount <= 0)
                {
                    _logger.LogWarning("最大在线人数无效: MaxOnlineCount={MaxOnlineCount}", maxOnlineCount);
                    return false;
                }

                state.ServerName = serverName.Trim();
                state.MaxOnlineCount = maxOnlineCount;
                state.IsInitialized = true;
                state.Status = (int)ServerStatus.Normal;
                state.LastUpdateTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                await _serverState.WriteStateAsync();

                _logger.LogInformation("初始化服务器成功: ServerName={ServerName}, MaxOnline={MaxOnline}",
                    serverName, maxOnlineCount);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "初始化服务器失败: ServerName={ServerName}", serverName);
                throw;
            }
        }

        public async Task<bool> UpdateServerLoadAsync(float cpuUsage, float memoryUsage, long networkLatency)
        {
            try
            {
                var state = _serverState.State;

                state.CpuUsage = Math.Clamp(cpuUsage, 0f, 100f);
                state.MemoryUsage = Math.Clamp(memoryUsage, 0f, 100f);
                state.NetworkLatency = Math.Max(0, networkLatency);
                state.LastUpdateTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                // Auto-update server status based on load
                if (state.Status != (int)ServerStatus.Maintenance)
                {
                    if (state.OnlinePlayers.Count >= state.MaxOnlineCount)
                    {
                        state.Status = (int)ServerStatus.Full;
                    }
                    else if (cpuUsage > 80 || memoryUsage > 85)
                    {
                        state.Status = (int)ServerStatus.Busy;
                    }
                    else
                    {
                        state.Status = (int)ServerStatus.Normal;
                    }
                }

                await _serverState.WriteStateAsync();

                _logger.LogDebug("更新服务器负载: CPU={CpuUsage}%, Memory={MemoryUsage}%, Latency={Latency}ms",
                    cpuUsage, memoryUsage, networkLatency);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新服务器负载失败");
                throw;
            }
        }

        public async Task<bool> PlayerOnlineAsync(Guid playerId)
        {
            try
            {
                var state = _serverState.State;

                if (state.Status == (int)ServerStatus.Maintenance)
                {
                    _logger.LogWarning("服务器维护中，无法登录: PlayerId={PlayerId}", playerId);
                    return false;
                }

                if (state.OnlinePlayers.Count >= state.MaxOnlineCount)
                {
                    _logger.LogWarning("服务器已满: PlayerId={PlayerId}", playerId);
                    return false;
                }

                if (!state.OnlinePlayers.Add(playerId))
                {
                    _logger.LogDebug("玩家已在线: PlayerId={PlayerId}", playerId);
                    return true;
                }

                state.LastUpdateTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                await _serverState.WriteStateAsync();

                _logger.LogDebug("玩家上线: PlayerId={PlayerId}, OnlineCount={OnlineCount}",
                    playerId, state.OnlinePlayers.Count);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "玩家上线失败: PlayerId={PlayerId}", playerId);
                throw;
            }
        }

        public async Task<bool> PlayerOfflineAsync(Guid playerId)
        {
            try
            {
                var state = _serverState.State;

                if (!state.OnlinePlayers.Remove(playerId))
                {
                    _logger.LogDebug("玩家未在线: PlayerId={PlayerId}", playerId);
                    return false;
                }

                state.LastUpdateTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                await _serverState.WriteStateAsync();

                _logger.LogDebug("玩家下线: PlayerId={PlayerId}, OnlineCount={OnlineCount}",
                    playerId, state.OnlinePlayers.Count);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "玩家下线失败: PlayerId={PlayerId}", playerId);
                throw;
            }
        }

        public Task<int> GetOnlinePlayerCountAsync()
        {
            return Task.FromResult(_serverState.State.OnlinePlayers.Count);
        }
    }
}
