using Horizon.Orleans.Interface;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;
using MemoryPack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Horizon.Orleans.Grains
{
    /// <summary>
    /// 区域管理状态
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class AreaState
    {
        /// <summary>
        /// 区域名称
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public string AreaName { get; set; } = "";

        /// <summary>
        /// 区域类型（如：野外、副本、城镇）
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string AreaType { get; set; } = "";

        /// <summary>
        /// 区域最大玩家数
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public int MaxPlayers { get; set; } = 100;

        /// <summary>
        /// 是否已初始化
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public bool IsInitialized { get; set; }

        /// <summary>
        /// 场景实例列表
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public Dictionary<long, SceneInstanceInfo> Instances { get; set; } = new();

        /// <summary>
        /// 下一个实例ID
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public long NextInstanceId { get; set; } = 1;
    }

    /// <summary>
    /// 区域管理Grain实现 - 负责场景实例创建/销毁、跨服传送、副本入口
    /// </summary>
    public class AreaGrain : Grain, IAreaGrain
    {
        private readonly ILogger<AreaGrain> _logger;
        private readonly IPersistentState<AreaState> _areaState;

        public AreaGrain(
            ILogger<AreaGrain> logger,
            [PersistentState("area", "GameStore")] IPersistentState<AreaState> areaState)
        {
            _logger = logger;
            _areaState = areaState;
        }

        public override async Task OnActivateAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("AreaGrain {GrainKey} activating.", this.GetPrimaryKeyLong());

            if (_areaState.State.Instances == null)
                _areaState.State.Instances = new Dictionary<long, SceneInstanceInfo>();

            await base.OnActivateAsync(cancellationToken);
        }

        public async Task<bool> InitializeAreaAsync(string areaName, string areaType, int maxPlayers)
        {
            try
            {
                var state = _areaState.State;

                if (state.IsInitialized)
                {
                    _logger.LogWarning("区域已初始化: AreaName={AreaName}", state.AreaName);
                    return false;
                }

                if (string.IsNullOrWhiteSpace(areaName))
                {
                    _logger.LogWarning("区域名称无效");
                    return false;
                }

                if (maxPlayers <= 0)
                {
                    _logger.LogWarning("最大玩家数无效: MaxPlayers={MaxPlayers}", maxPlayers);
                    return false;
                }

                state.AreaName = areaName.Trim();
                state.AreaType = areaType ?? "";
                state.MaxPlayers = maxPlayers;
                state.IsInitialized = true;

                await _areaState.WriteStateAsync();

                _logger.LogInformation("初始化区域成功: AreaName={AreaName}, Type={AreaType}, MaxPlayers={MaxPlayers}",
                    areaName, areaType, maxPlayers);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "初始化区域失败: AreaName={AreaName}", areaName);
                throw;
            }
        }

        public async Task<SceneInstanceInfo> CreateSceneInstanceAsync(string sceneName, int maxPlayers)
        {
            try
            {
                var state = _areaState.State;

                if (string.IsNullOrWhiteSpace(sceneName))
                {
                    _logger.LogWarning("场景名称无效");
                    return null;
                }

                if (maxPlayers <= 0)
                {
                    _logger.LogWarning("场景最大玩家数无效: MaxPlayers={MaxPlayers}", maxPlayers);
                    return null;
                }

                var instanceId = state.NextInstanceId++;
                var instance = new SceneInstanceInfo
                {
                    InstanceId = instanceId,
                    SceneName = sceneName.Trim(),
                    MaxPlayers = maxPlayers,
                    CurrentPlayers = 0,
                    Players = new HashSet<Guid>(),
                    CreatedTime = DateTime.UtcNow,
                    IsActive = true
                };

                state.Instances[instanceId] = instance;
                await _areaState.WriteStateAsync();

                _logger.LogInformation("创建场景实例: InstanceId={InstanceId}, SceneName={SceneName}",
                    instanceId, sceneName);
                return instance;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建场景实例失败: SceneName={SceneName}", sceneName);
                throw;
            }
        }

        public async Task<bool> DestroySceneInstanceAsync(long instanceId)
        {
            try
            {
                var state = _areaState.State;

                if (!state.Instances.TryGetValue(instanceId, out var instance))
                {
                    _logger.LogWarning("场景实例不存在: InstanceId={InstanceId}", instanceId);
                    return false;
                }

                instance.IsActive = false;
                instance.Players.Clear();
                instance.CurrentPlayers = 0;
                state.Instances.Remove(instanceId);

                await _areaState.WriteStateAsync();

                _logger.LogInformation("销毁场景实例: InstanceId={InstanceId}", instanceId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "销毁场景实例失败: InstanceId={InstanceId}", instanceId);
                throw;
            }
        }

        public async Task<bool> PlayerEnterInstanceAsync(long instanceId, Guid playerId)
        {
            try
            {
                var state = _areaState.State;

                if (!state.Instances.TryGetValue(instanceId, out var instance))
                {
                    _logger.LogWarning("场景实例不存在: InstanceId={InstanceId}", instanceId);
                    return false;
                }

                if (!instance.IsActive)
                {
                    _logger.LogWarning("场景实例已关闭: InstanceId={InstanceId}", instanceId);
                    return false;
                }

                if (instance.Players.Count >= instance.MaxPlayers)
                {
                    _logger.LogWarning("场景实例已满: InstanceId={InstanceId}", instanceId);
                    return false;
                }

                if (!instance.Players.Add(playerId))
                {
                    _logger.LogDebug("玩家已在场景中: PlayerId={PlayerId}, InstanceId={InstanceId}", playerId, instanceId);
                    return true;
                }

                instance.CurrentPlayers = instance.Players.Count;
                await _areaState.WriteStateAsync();

                _logger.LogDebug("玩家进入场景: PlayerId={PlayerId}, InstanceId={InstanceId}", playerId, instanceId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "玩家进入场景失败: PlayerId={PlayerId}, InstanceId={InstanceId}", playerId, instanceId);
                throw;
            }
        }

        public async Task<bool> PlayerLeaveInstanceAsync(long instanceId, Guid playerId)
        {
            try
            {
                var state = _areaState.State;

                if (!state.Instances.TryGetValue(instanceId, out var instance))
                {
                    _logger.LogWarning("场景实例不存在: InstanceId={InstanceId}", instanceId);
                    return false;
                }

                if (!instance.Players.Remove(playerId))
                {
                    _logger.LogDebug("玩家不在场景中: PlayerId={PlayerId}, InstanceId={InstanceId}", playerId, instanceId);
                    return false;
                }

                instance.CurrentPlayers = instance.Players.Count;
                await _areaState.WriteStateAsync();

                _logger.LogDebug("玩家离开场景: PlayerId={PlayerId}, InstanceId={InstanceId}", playerId, instanceId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "玩家离开场景失败: PlayerId={PlayerId}, InstanceId={InstanceId}", playerId, instanceId);
                throw;
            }
        }

        public Task<SceneInstanceInfo> GetSceneInstanceAsync(long instanceId)
        {
            try
            {
                _areaState.State.Instances.TryGetValue(instanceId, out var instance);
                return Task.FromResult(instance);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取场景实例失败: InstanceId={InstanceId}", instanceId);
                throw;
            }
        }

        public Task<List<SceneInstanceInfo>> GetAllInstancesAsync()
        {
            try
            {
                var instances = _areaState.State.Instances.Values.ToList();
                return Task.FromResult(instances);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取所有场景实例失败");
                throw;
            }
        }

        public Task<TeleportResult> RequestTeleportAsync(Guid playerId, int targetAreaId, long targetInstanceId)
        {
            try
            {
                // Validate the player is in this area
                var state = _areaState.State;
                bool playerFound = state.Instances.Values.Any(i => i.Players.Contains(playerId));

                if (!playerFound)
                {
                    return Task.FromResult(new TeleportResult
                    {
                        Success = false,
                        Message = "玩家不在当前区域",
                        TargetAreaId = targetAreaId,
                        TargetInstanceId = targetInstanceId
                    });
                }

                // Remove player from current instances
                foreach (var instance in state.Instances.Values)
                {
                    if (instance.Players.Remove(playerId))
                    {
                        instance.CurrentPlayers = instance.Players.Count;
                    }
                }

                _logger.LogInformation("传送请求: PlayerId={PlayerId}, TargetArea={TargetAreaId}, TargetInstance={TargetInstanceId}",
                    playerId, targetAreaId, targetInstanceId);

                return Task.FromResult(new TeleportResult
                {
                    Success = true,
                    Message = "传送请求已提交",
                    TargetAreaId = targetAreaId,
                    TargetInstanceId = targetInstanceId
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "传送请求失败: PlayerId={PlayerId}", playerId);
                throw;
            }
        }

        public Task<AreaInfo> GetAreaInfoAsync()
        {
            try
            {
                var state = _areaState.State;
                var totalPlayers = state.Instances.Values.Sum(i => i.CurrentPlayers);

                var info = new AreaInfo
                {
                    AreaId = (int)this.GetPrimaryKeyLong(),
                    AreaName = state.AreaName,
                    AreaType = state.AreaType,
                    MaxPlayers = state.MaxPlayers,
                    TotalPlayers = totalPlayers,
                    InstanceCount = state.Instances.Count,
                    IsInitialized = state.IsInitialized
                };

                return Task.FromResult(info);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取区域信息失败");
                throw;
            }
        }
    }
}
