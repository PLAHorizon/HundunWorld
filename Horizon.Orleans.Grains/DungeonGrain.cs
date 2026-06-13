using Horizon.Game.Message.Network;
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
    /// 副本系统Grain实现 - 负责副本创建、进入、通关、奖励发放
    /// </summary>
    public class DungeonGrain : Grain, IDungeonGrain
    {
        private readonly ILogger<DungeonGrain> _logger;
        private readonly IPersistentState<DungeonState> _dungeonState;

        public DungeonGrain(
            ILogger<DungeonGrain> logger,
            [PersistentState("dungeon", "GameStore")] IPersistentState<DungeonState> dungeonState)
        {
            _logger = logger;
            _dungeonState = dungeonState;
        }

        public override async Task OnActivateAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("DungeonGrain {GrainKey} activating.", this.GetPrimaryKey());

            if (_dungeonState.State.Players == null)
                _dungeonState.State.Players = new HashSet<Guid>();
            if (_dungeonState.State.Bosses == null)
                _dungeonState.State.Bosses = new Dictionary<int, DungeonBossData>();

            await base.OnActivateAsync(cancellationToken);
        }

        public async Task<bool> CreateDungeonAsync(int dungeonTemplateId, string dungeonName, int difficulty, int maxPlayers, int timeLimitMinutes)
        {
            try
            {
                var state = _dungeonState.State;

                if (state.IsCreated)
                {
                    _logger.LogWarning("副本已创建: Name={Name}", state.DungeonName);
                    return false;
                }

                if (dungeonTemplateId <= 0)
                {
                    _logger.LogWarning("副本模板ID无效: TemplateId={TemplateId}", dungeonTemplateId);
                    return false;
                }

                if (string.IsNullOrWhiteSpace(dungeonName))
                {
                    _logger.LogWarning("副本名称无效");
                    return false;
                }

                if (maxPlayers <= 0)
                {
                    _logger.LogWarning("最大玩家数无效: MaxPlayers={MaxPlayers}", maxPlayers);
                    return false;
                }

                if (timeLimitMinutes <= 0)
                {
                    _logger.LogWarning("时间限制无效: TimeLimitMinutes={TimeLimitMinutes}", timeLimitMinutes);
                    return false;
                }

                if (difficulty < (int)DungeonDifficulty.Normal || difficulty > (int)DungeonDifficulty.Hell)
                {
                    _logger.LogWarning("难度无效: Difficulty={Difficulty}", difficulty);
                    return false;
                }

                state.DungeonTemplateId = dungeonTemplateId;
                state.DungeonName = dungeonName.Trim();
                state.Difficulty = difficulty;
                state.MaxPlayers = maxPlayers;
                state.TimeLimitMinutes = timeLimitMinutes;
                state.Status = (int)DungeonStatus.Waiting;
                state.IsCreated = true;

                await _dungeonState.WriteStateAsync();

                _logger.LogInformation("创建副本成功: TemplateId={TemplateId}, Name={Name}, Difficulty={Difficulty}, MaxPlayers={MaxPlayers}",
                    dungeonTemplateId, dungeonName, difficulty, maxPlayers);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建副本失败: TemplateId={TemplateId}", dungeonTemplateId);
                throw;
            }
        }

        public async Task<bool> AddBossAsync(int bossId, string bossName)
        {
            try
            {
                var state = _dungeonState.State;

                if (!state.IsCreated)
                {
                    _logger.LogWarning("副本未创建");
                    return false;
                }

                if (bossId <= 0)
                {
                    _logger.LogWarning("Boss ID无效: BossId={BossId}", bossId);
                    return false;
                }

                if (string.IsNullOrWhiteSpace(bossName))
                {
                    _logger.LogWarning("Boss名称无效");
                    return false;
                }

                if (state.Bosses.ContainsKey(bossId))
                {
                    _logger.LogWarning("Boss已存在: BossId={BossId}", bossId);
                    return false;
                }

                state.Bosses[bossId] = new DungeonBossData
                {
                    BossId = bossId,
                    BossName = bossName.Trim(),
                    IsDefeated = false
                };

                await _dungeonState.WriteStateAsync();

                _logger.LogInformation("添加Boss: BossId={BossId}, Name={BossName}", bossId, bossName);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "添加Boss失败: BossId={BossId}", bossId);
                throw;
            }
        }

        public async Task<bool> EnterDungeonAsync(Guid playerId)
        {
            try
            {
                var state = _dungeonState.State;

                if (!state.IsCreated)
                {
                    _logger.LogWarning("副本未创建");
                    return false;
                }

                if (state.Status == (int)DungeonStatus.Completed || state.Status == (int)DungeonStatus.Failed)
                {
                    _logger.LogWarning("副本已结束: Status={Status}", (DungeonStatus)state.Status);
                    return false;
                }

                if (state.Players.Contains(playerId))
                {
                    _logger.LogWarning("玩家已在副本中: PlayerId={PlayerId}", playerId);
                    return false;
                }

                if (state.Players.Count >= state.MaxPlayers)
                {
                    _logger.LogWarning("副本人数已满: PlayerId={PlayerId}", playerId);
                    return false;
                }

                state.Players.Add(playerId);

                // First player entering starts the dungeon
                if (state.Status == (int)DungeonStatus.Waiting)
                {
                    state.Status = (int)DungeonStatus.InProgress;
                    state.StartTime = DateTime.Now;
                }

                await _dungeonState.WriteStateAsync();

                _logger.LogInformation("玩家进入副本: PlayerId={PlayerId}, DungeonName={DungeonName}", playerId, state.DungeonName);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "玩家进入副本失败: PlayerId={PlayerId}", playerId);
                throw;
            }
        }

        public async Task<bool> LeaveDungeonAsync(Guid playerId)
        {
            try
            {
                var state = _dungeonState.State;

                if (!state.Players.Contains(playerId))
                {
                    _logger.LogWarning("玩家不在副本中: PlayerId={PlayerId}", playerId);
                    return false;
                }

                state.Players.Remove(playerId);
                await _dungeonState.WriteStateAsync();

                _logger.LogInformation("玩家离开副本: PlayerId={PlayerId}", playerId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "玩家离开副本失败: PlayerId={PlayerId}", playerId);
                throw;
            }
        }

        public async Task<bool> DefeatBossAsync(int bossId)
        {
            try
            {
                var state = _dungeonState.State;

                if (!state.IsCreated)
                {
                    _logger.LogWarning("副本未创建");
                    return false;
                }

                if (state.Status != (int)DungeonStatus.InProgress)
                {
                    _logger.LogWarning("副本不在进行中: Status={Status}", (DungeonStatus)state.Status);
                    return false;
                }

                if (!state.Bosses.TryGetValue(bossId, out var boss))
                {
                    _logger.LogWarning("Boss不存在: BossId={BossId}", bossId);
                    return false;
                }

                if (boss.IsDefeated)
                {
                    _logger.LogWarning("Boss已被击败: BossId={BossId}", bossId);
                    return false;
                }

                boss.IsDefeated = true;
                boss.DefeatTime = DateTime.Now;

                await _dungeonState.WriteStateAsync();

                _logger.LogInformation("击败Boss: BossId={BossId}, BossName={BossName}", bossId, boss.BossName);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "击败Boss失败: BossId={BossId}", bossId);
                throw;
            }
        }

        public async Task<DungeonCompleteResult> CompleteDungeonAsync()
        {
            try
            {
                var state = _dungeonState.State;

                if (!state.IsCreated)
                {
                    return new DungeonCompleteResult
                    {
                        Success = false,
                        Message = "副本未创建"
                    };
                }

                if (state.Status != (int)DungeonStatus.InProgress)
                {
                    return new DungeonCompleteResult
                    {
                        Success = false,
                        Message = "副本不在进行中"
                    };
                }

                // Check if all bosses are defeated
                if (state.Bosses.Count > 0 && state.Bosses.Values.Any(b => !b.IsDefeated))
                {
                    return new DungeonCompleteResult
                    {
                        Success = false,
                        Message = "尚有Boss未被击败",
                        DungeonTemplateId = state.DungeonTemplateId,
                        Difficulty = state.Difficulty,
                        TotalBosses = state.Bosses.Count,
                        DefeatedBosses = state.Bosses.Values.Count(b => b.IsDefeated)
                    };
                }

                double clearTimeSeconds = 0;
                if (state.StartTime.HasValue)
                {
                    clearTimeSeconds = (DateTime.Now - state.StartTime.Value).TotalSeconds;
                }

                state.Status = (int)DungeonStatus.Completed;
                await _dungeonState.WriteStateAsync();

                _logger.LogInformation("副本通关: DungeonName={DungeonName}, ClearTime={ClearTime}s",
                    state.DungeonName, clearTimeSeconds);

                return new DungeonCompleteResult
                {
                    Success = true,
                    Message = "副本通关成功",
                    DungeonTemplateId = state.DungeonTemplateId,
                    Difficulty = state.Difficulty,
                    TotalBosses = state.Bosses.Count,
                    DefeatedBosses = state.Bosses.Values.Count(b => b.IsDefeated),
                    ClearTimeSeconds = clearTimeSeconds
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "完成副本失败");
                throw;
            }
        }

        public Task<DungeonData> GetDungeonInfoAsync()
        {
            try
            {
                var state = _dungeonState.State;

                var data = new DungeonData
                {
                    DungeonTemplateId = state.DungeonTemplateId,
                    DungeonName = state.DungeonName,
                    Difficulty = state.Difficulty,
                    MaxPlayers = state.MaxPlayers,
                    CurrentPlayers = state.Players.Count,
                    Status = state.Status,
                    TimeLimitMinutes = state.TimeLimitMinutes,
                    StartTime = state.StartTime,
                    IsCreated = state.IsCreated,
                    Bosses = state.Bosses.Values.ToList(),
                    DefeatedBossCount = state.Bosses.Values.Count(b => b.IsDefeated),
                    TeamId = state.TeamId
                };

                return Task.FromResult(data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取副本信息失败");
                throw;
            }
        }

        public Task<bool> IsTimedOutAsync()
        {
            try
            {
                var state = _dungeonState.State;

                if (!state.IsCreated || !state.StartTime.HasValue)
                    return Task.FromResult(false);

                if (state.Status != (int)DungeonStatus.InProgress)
                    return Task.FromResult(false);

                var elapsed = DateTime.Now - state.StartTime.Value;
                return Task.FromResult(elapsed.TotalMinutes >= state.TimeLimitMinutes);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "检查副本超时失败");
                throw;
            }
        }

        public Task<List<Guid>> GetPlayersAsync()
        {
            try
            {
                var players = _dungeonState.State.Players.ToList();
                return Task.FromResult(players);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取副本玩家列表失败");
                throw;
            }
        }
    }
}
