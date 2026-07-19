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

            // 修复 BUG：CustomGrainStorageSerializer 反序列化失败时返回 default(T) = null，
            // 导致 _serverState.State == null，后续访问 .OnlinePlayers 抛 NRE，grain 激活失败。
            // 一旦 grain 激活失败，所有 PlayerOnlineAsync/PlayerOfflineAsync 调用都会失败，
            // OnlinePlayers 永远不被修改，Redis 中残留旧数据，角色"无法正常离线"。
            // 参考 CharacterGrain.OnActivateAsync 的 State == null 检查逻辑。
            if (_serverState.State == null)
            {
                _logger.LogError(
                    "GameServerGrain {GrainKey} 激活时 State 为 null（持久化反序列化失败或新部署未初始化），" +
                    "初始化为默认实例并持久化覆盖可能损坏的旧数据",
                    this.GetPrimaryKeyLong());
                _serverState.State = new GameServerState();
                _serverState.State.OnlinePlayers = new HashSet<long>();
                await _serverState.WriteStateAsync();
            }
            else if (_serverState.State.OnlinePlayers == null)
            {
                // 兜底初始化：旧版本持久化数据中 OnlinePlayers 可能缺失（或为 null）。
                // 修复 BUG：原实现只重置内存字段而不调用 WriteStateAsync，导致 Redis 中仍残留
                // 旧的 OnlinePlayers 数据。PlayerOfflineAsync 的 Remove 会失败（幂等返回 true 不持久化），
                // 形成"OnlinePlayers 永远残留 stale characterId"的死锁。
                _logger.LogWarning(
                    "GameServerGrain {GrainKey} 激活时 OnlinePlayers 为 null，已兜底初始化为空集合并持久化覆盖旧数据",
                    this.GetPrimaryKeyLong());
                _serverState.State.OnlinePlayers = new HashSet<long>();
                await _serverState.WriteStateAsync();
            }
            else
            {
                _logger.LogInformation(
                    "GameServerGrain {GrainKey} 激活时持久化在线列表大小: OnlineCount={OnlineCount}",
                    this.GetPrimaryKeyLong(), _serverState.State.OnlinePlayers.Count);
            }

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

                // OnlineCount kept in sync with OnlinePlayers.Count as authoritative source
                _serverState.State.OnlineCount = _serverState.State.OnlinePlayers.Count;
                _serverState.State.LastUpdateTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                await _serverState.WriteStateAsync();

                _logger.LogDebug("同步在线人数: OnlineCount={OnlineCount}", _serverState.State.OnlinePlayers.Count);
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

        public async Task<bool> PlayerOnlineAsync(long characterId)
        {
            // 防御性校验：拒绝非法 characterId（防止 0 或负数污染 OnlinePlayers）
            if (characterId <= 0)
            {
                _logger.LogWarning("拒绝非法角色 ID 上线: CharacterId={CharacterId}", characterId);
                return false;
            }

            try
            {
                var state = _serverState.State;

                if (state.Status == (int)ServerStatus.Maintenance)
                {
                    _logger.LogWarning("服务器维护中，无法登录: CharacterId={CharacterId}", characterId);
                    return false;
                }

                if (state.OnlinePlayers.Count >= state.MaxOnlineCount)
                {
                    _logger.LogWarning("服务器已满: CharacterId={CharacterId}, OnlineCount={OnlineCount}, MaxOnlineCount={MaxOnlineCount}",
                        characterId, state.OnlinePlayers.Count, state.MaxOnlineCount);
                    return false;
                }

                if (!state.OnlinePlayers.Add(characterId))
                {
                    // 幂等：角色已在在线列表中，直接返回成功
                    _logger.LogDebug("角色已在持久化在线列表中（幂等上线）: CharacterId={CharacterId}, OnlineCount={OnlineCount}",
                        characterId, state.OnlinePlayers.Count);
                    return true;
                }

                var previousOnlineCount = state.OnlinePlayers.Count - 1;
                state.LastUpdateTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                await _serverState.WriteStateAsync();

                _logger.LogInformation(
                    "角色上线已持久化: CharacterId={CharacterId}, PreviousOnlineCount={PreviousOnlineCount}, OnlineCount={OnlineCount}, MaxOnlineCount={MaxOnlineCount}",
                    characterId, previousOnlineCount, state.OnlinePlayers.Count, state.MaxOnlineCount);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "角色上线失败: CharacterId={CharacterId}", characterId);
                throw;
            }
        }

        public async Task<bool> PlayerOfflineAsync(long characterId)
        {
            // 防御性校验：拒绝非法 characterId
            if (characterId <= 0)
            {
                _logger.LogWarning("拒绝非法角色 ID 下线: CharacterId={CharacterId}", characterId);
                return false;
            }

            try
            {
                var state = _serverState.State;

                if (!state.OnlinePlayers.Remove(characterId))
                {
                    // 幂等：角色已不在持久化在线列表中。
                    // 返回 true（最终状态一致），避免兜底调用方（DespawnScheduler/Monitor）
                    // 因重复调用而误判失败、触发不必要的重试或告警。
                    _logger.LogDebug("角色未在持久化在线列表中（幂等下线）: CharacterId={CharacterId}, OnlineCount={OnlineCount}",
                        characterId, state.OnlinePlayers.Count);
                    return true;
                }

                state.LastUpdateTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                await _serverState.WriteStateAsync();

                _logger.LogInformation(
                    "角色下线已持久化: CharacterId={CharacterId}, OnlineCount={OnlineCount}, MaxOnlineCount={MaxOnlineCount}",
                    characterId, state.OnlinePlayers.Count, state.MaxOnlineCount);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "角色下线失败: CharacterId={CharacterId}", characterId);
                throw;
            }
        }

        public Task<int> GetOnlinePlayerCountAsync()
        {
            return Task.FromResult(_serverState.State.OnlinePlayers.Count);
        }
    }
}
