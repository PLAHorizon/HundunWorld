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
    /// 活动系统Grain实现 - 负责定时活动调度、奖励发放、参与记录
    /// </summary>
    public class ActivityGrain : Grain, IActivityGrain
    {
        private readonly ILogger<ActivityGrain> _logger;
        private readonly IPersistentState<ActivityState> _activityState;

        public ActivityGrain(
            ILogger<ActivityGrain> logger,
            [PersistentState("activity", "GameStore")] IPersistentState<ActivityState> activityState)
        {
            _logger = logger;
            _activityState = activityState;
        }

        public override async Task OnActivateAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("ActivityGrain {GrainKey} activating.", this.GetPrimaryKeyLong());

            if (_activityState.State.Participants == null)
                _activityState.State.Participants = new Dictionary<Guid, ActivityParticipation>();

            await base.OnActivateAsync(cancellationToken);
        }

        public async Task<bool> CreateActivityAsync(string name, string description, DateTime startTime, DateTime endTime, int maxParticipants)
        {
            try
            {
                var state = _activityState.State;

                if (state.IsCreated)
                {
                    _logger.LogWarning("活动已创建: Name={Name}", state.Name);
                    return false;
                }

                if (string.IsNullOrWhiteSpace(name))
                {
                    _logger.LogWarning("活动名称无效");
                    return false;
                }

                if (endTime <= startTime)
                {
                    _logger.LogWarning("活动结束时间必须晚于开始时间");
                    return false;
                }

                if (maxParticipants <= 0)
                {
                    _logger.LogWarning("最大参与人数无效: MaxParticipants={MaxParticipants}", maxParticipants);
                    return false;
                }

                state.Name = name.Trim();
                state.Description = description ?? "";
                state.StartTime = startTime;
                state.EndTime = endTime;
                state.MaxParticipants = maxParticipants;
                state.IsCreated = true;

                // Auto-determine status based on current time
                var now = DateTime.UtcNow;
                if (now >= startTime && now < endTime)
                    state.Status = (int)ActivityStatus.Active;
                else if (now >= endTime)
                    state.Status = (int)ActivityStatus.Ended;
                else
                    state.Status = (int)ActivityStatus.NotStarted;

                await _activityState.WriteStateAsync();

                _logger.LogInformation("创建活动成功: Name={Name}, Start={StartTime}, End={EndTime}, MaxParticipants={MaxParticipants}",
                    name, startTime, endTime, maxParticipants);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建活动失败: Name={Name}", name);
                throw;
            }
        }

        public async Task<ActivityInfo> GetActivityInfoAsync()
        {
            try
            {
                var state = _activityState.State;

                // Auto-update status and persist if changed
                int oldStatus = state.Status;
                UpdateActivityStatus(state);
                if (state.Status != oldStatus)
                {
                    await _activityState.WriteStateAsync();
                }

                var info = new ActivityInfo
                {
                    ActivityId = (int)this.GetPrimaryKeyLong(),
                    Name = state.Name,
                    Description = state.Description,
                    StartTime = state.StartTime,
                    EndTime = state.EndTime,
                    MaxParticipants = state.MaxParticipants,
                    CurrentParticipants = state.Participants.Count(p => p.Value.IsActive),
                    Status = state.Status,
                    IsCreated = state.IsCreated
                };

                return info;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取活动信息失败");
                throw;
            }
        }

        public async Task<bool> JoinActivityAsync(Guid playerId)
        {
            try
            {
                var state = _activityState.State;

                if (!state.IsCreated)
                {
                    _logger.LogWarning("活动未创建");
                    return false;
                }

                UpdateActivityStatus(state);

                if (state.Status != (int)ActivityStatus.Active)
                {
                    _logger.LogWarning("活动不在进行中: Status={Status}", (ActivityStatus)state.Status);
                    return false;
                }

                if (state.Participants.TryGetValue(playerId, out var existing) && existing.IsActive)
                {
                    _logger.LogWarning("玩家已参与活动: PlayerId={PlayerId}", playerId);
                    return false;
                }

                var activeCount = state.Participants.Count(p => p.Value.IsActive);
                if (activeCount >= state.MaxParticipants)
                {
                    _logger.LogWarning("活动参与人数已满: PlayerId={PlayerId}", playerId);
                    return false;
                }

                state.Participants[playerId] = new ActivityParticipation
                {
                    PlayerId = playerId,
                    JoinTime = DateTime.UtcNow,
                    IsActive = true,
                    Rewards = new List<RewardRecord>()
                };

                await _activityState.WriteStateAsync();

                _logger.LogInformation("玩家参与活动: PlayerId={PlayerId}", playerId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "玩家参与活动失败: PlayerId={PlayerId}", playerId);
                throw;
            }
        }

        public async Task<bool> LeaveActivityAsync(Guid playerId)
        {
            try
            {
                var state = _activityState.State;

                if (!state.Participants.TryGetValue(playerId, out var participation) || !participation.IsActive)
                {
                    _logger.LogWarning("玩家未参与活动: PlayerId={PlayerId}", playerId);
                    return false;
                }

                participation.IsActive = false;
                await _activityState.WriteStateAsync();

                _logger.LogInformation("玩家退出活动: PlayerId={PlayerId}", playerId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "玩家退出活动失败: PlayerId={PlayerId}", playerId);
                throw;
            }
        }

        public async Task<bool> DistributeRewardAsync(Guid playerId, int rewardTemplateId, int quantity)
        {
            try
            {
                var state = _activityState.State;

                if (!state.Participants.TryGetValue(playerId, out var participation))
                {
                    _logger.LogWarning("玩家未参与活动: PlayerId={PlayerId}", playerId);
                    return false;
                }

                if (rewardTemplateId <= 0 || quantity <= 0)
                {
                    _logger.LogWarning("奖励参数无效: RewardTemplateId={RewardTemplateId}, Quantity={Quantity}",
                        rewardTemplateId, quantity);
                    return false;
                }

                var reward = new RewardRecord
                {
                    RewardTemplateId = rewardTemplateId,
                    Quantity = quantity,
                    DistributedTime = DateTime.UtcNow
                };

                participation.Rewards.Add(reward);
                await _activityState.WriteStateAsync();

                _logger.LogInformation("发放奖励: PlayerId={PlayerId}, RewardId={RewardTemplateId}, Quantity={Quantity}",
                    playerId, rewardTemplateId, quantity);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "发放奖励失败: PlayerId={PlayerId}", playerId);
                throw;
            }
        }

        public Task<ActivityParticipation> GetParticipationAsync(Guid playerId)
        {
            try
            {
                _activityState.State.Participants.TryGetValue(playerId, out var participation);
                return Task.FromResult(participation);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取参与记录失败: PlayerId={PlayerId}", playerId);
                throw;
            }
        }

        public Task<List<ActivityParticipation>> GetAllParticipantsAsync()
        {
            try
            {
                var participants = _activityState.State.Participants.Values.ToList();
                return Task.FromResult(participants);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取所有参与者失败");
                throw;
            }
        }

        public async Task<bool> EndActivityAsync()
        {
            try
            {
                var state = _activityState.State;

                if (!state.IsCreated)
                {
                    _logger.LogWarning("活动未创建");
                    return false;
                }

                if (state.Status == (int)ActivityStatus.Ended)
                {
                    _logger.LogWarning("活动已结束");
                    return false;
                }

                state.Status = (int)ActivityStatus.Ended;
                await _activityState.WriteStateAsync();

                _logger.LogInformation("活动结束: Name={Name}", state.Name);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "结束活动失败");
                throw;
            }
        }

        public async Task<bool> IsActiveAsync()
        {
            try
            {
                var state = _activityState.State;
                int oldStatus = state.Status;
                UpdateActivityStatus(state);
                if (state.Status != oldStatus)
                {
                    await _activityState.WriteStateAsync();
                }
                return state.Status == (int)ActivityStatus.Active;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "检查活动状态失败");
                throw;
            }
        }

        /// <summary>
        /// 根据当前时间自动更新活动状态
        /// </summary>
        private void UpdateActivityStatus(ActivityState state)
        {
            if (!state.IsCreated || state.Status == (int)ActivityStatus.Ended || state.Status == (int)ActivityStatus.Cancelled)
                return;

            var now = DateTime.UtcNow;

            if (now >= state.EndTime)
            {
                state.Status = (int)ActivityStatus.Ended;
            }
            else if (now >= state.StartTime)
            {
                state.Status = (int)ActivityStatus.Active;
            }
            else
            {
                state.Status = (int)ActivityStatus.NotStarted;
            }
        }
    }
}
