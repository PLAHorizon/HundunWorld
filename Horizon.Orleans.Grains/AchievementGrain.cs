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
    /// 成就系统Grain实现 - 负责成就追踪与解锁管理
    /// </summary>
    public class AchievementGrain : Grain, IAchievementGrain
    {
        private readonly ILogger<AchievementGrain> _logger;
        private readonly IPersistentState<AchievementState> _achievementState;

        public AchievementGrain(
            ILogger<AchievementGrain> logger,
            [PersistentState("achievement", "GameStore")] IPersistentState<AchievementState> achievementState)
        {
            _logger = logger;
            _achievementState = achievementState;
        }

        public override async Task OnActivateAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("AchievementGrain {GrainKey} activating.", this.GetPrimaryKey());

            if (_achievementState.State.Achievements == null)
                _achievementState.State.Achievements = new Dictionary<int, AchievementData>();

            await base.OnActivateAsync(cancellationToken);
        }

        public async Task<bool> RegisterAchievementAsync(int achievementId, string name, string description, int category, int points,
            int targetProgress, Dictionary<string, int>? rewards = null)
        {
            try
            {
                if (achievementId <= 0)
                {
                    _logger.LogWarning("成就ID无效: AchievementId={AchievementId}", achievementId);
                    return false;
                }

                if (string.IsNullOrWhiteSpace(name))
                {
                    _logger.LogWarning("成就名称无效");
                    return false;
                }

                if (targetProgress <= 0)
                {
                    _logger.LogWarning("目标进度无效: TargetProgress={TargetProgress}", targetProgress);
                    return false;
                }

                if (points < 0)
                {
                    _logger.LogWarning("成就点数无效: Points={Points}", points);
                    return false;
                }

                var state = _achievementState.State;

                if (state.Achievements.ContainsKey(achievementId))
                {
                    _logger.LogWarning("成就已注册: AchievementId={AchievementId}", achievementId);
                    return false;
                }

                var achievement = new AchievementData
                {
                    AchievementId = achievementId,
                    Name = name.Trim(),
                    Description = description ?? "",
                    Category = category,
                    Points = points,
                    TargetProgress = targetProgress,
                    CurrentProgress = 0,
                    IsUnlocked = false,
                    Rewards = rewards ?? new Dictionary<string, int>()
                };

                state.Achievements[achievementId] = achievement;
                await _achievementState.WriteStateAsync();

                _logger.LogInformation("注册成就: AchievementId={AchievementId}, Name={Name}, Points={Points}",
                    achievementId, name, points);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "注册成就失败: AchievementId={AchievementId}", achievementId);
                throw;
            }
        }

        public async Task<AchievementUnlockResult?> UpdateProgressAsync(int achievementId, int progressIncrement)
        {
            try
            {
                var state = _achievementState.State;

                if (!state.Achievements.TryGetValue(achievementId, out var achievement))
                {
                    _logger.LogWarning("成就不存在: AchievementId={AchievementId}", achievementId);
                    return null;
                }

                if (achievement.IsUnlocked)
                {
                    _logger.LogWarning("成就已解锁: AchievementId={AchievementId}", achievementId);
                    return null;
                }

                if (progressIncrement <= 0)
                {
                    _logger.LogWarning("进度增量无效: ProgressIncrement={ProgressIncrement}", progressIncrement);
                    return null;
                }

                achievement.CurrentProgress = Math.Min(
                    achievement.CurrentProgress + progressIncrement,
                    achievement.TargetProgress);

                AchievementUnlockResult? result = null;

                if (achievement.CurrentProgress >= achievement.TargetProgress)
                {
                    achievement.IsUnlocked = true;
                    achievement.UnlockTime = DateTime.UtcNow;

                    state.UnlockedCount++;
                    state.TotalPoints += achievement.Points;

                    result = new AchievementUnlockResult
                    {
                        Success = true,
                        Message = "成就解锁",
                        AchievementId = achievementId,
                        PointsEarned = achievement.Points,
                        Rewards = achievement.Rewards
                    };

                    _logger.LogInformation("成就解锁: AchievementId={AchievementId}, Name={Name}, Points={Points}",
                        achievementId, achievement.Name, achievement.Points);
                }

                await _achievementState.WriteStateAsync();

                _logger.LogInformation("更新成就进度: AchievementId={AchievementId}, Progress={Current}/{Target}",
                    achievementId, achievement.CurrentProgress, achievement.TargetProgress);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新成就进度失败: AchievementId={AchievementId}", achievementId);
                throw;
            }
        }

        public Task<List<AchievementData>> GetAllAchievementsAsync()
        {
            try
            {
                var achievements = _achievementState.State.Achievements.Values.ToList();
                return Task.FromResult(achievements);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取成就列表失败");
                throw;
            }
        }

        public Task<List<AchievementData>> GetUnlockedAchievementsAsync()
        {
            try
            {
                var achievements = _achievementState.State.Achievements.Values
                    .Where(a => a.IsUnlocked)
                    .ToList();
                return Task.FromResult(achievements);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取已解锁成就失败");
                throw;
            }
        }

        public Task<List<AchievementData>> GetAchievementsByCategoryAsync(int category)
        {
            try
            {
                var achievements = _achievementState.State.Achievements.Values
                    .Where(a => a.Category == category)
                    .ToList();
                return Task.FromResult(achievements);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取分类成就失败: Category={Category}", category);
                throw;
            }
        }

        public Task<AchievementData?> GetAchievementAsync(int achievementId)
        {
            try
            {
                if (_achievementState.State.Achievements.TryGetValue(achievementId, out var achievement))
                {
                    return Task.FromResult<AchievementData?>(achievement);
                }

                return Task.FromResult<AchievementData?>(null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取成就详情失败: AchievementId={AchievementId}", achievementId);
                throw;
            }
        }

        public Task<int> GetTotalPointsAsync()
        {
            return Task.FromResult(_achievementState.State.TotalPoints);
        }

        public Task<int> GetUnlockedCountAsync()
        {
            return Task.FromResult(_achievementState.State.UnlockedCount);
        }
    }
}
