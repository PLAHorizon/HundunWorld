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
    /// 技能状态
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class SkillState
    {
        /// <summary>
        /// 已学习技能列表（技能ID -> 技能信息）
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public Dictionary<int, SkillInfo> LearnedSkills { get; set; } = new();

        /// <summary>
        /// 技能冷却记录（技能ID -> 上次施放时间）
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public Dictionary<int, DateTime> SkillCooldowns { get; set; } = new();

        /// <summary>
        /// 可用技能点
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public int SkillPoints { get; set; } = 0;

        /// <summary>
        /// 已使用技能点总数
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public int TotalSkillPointsUsed { get; set; } = 0;

        /// <summary>
        /// 技能前置依赖（技能ID -> 前置技能ID列表）
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public Dictionary<int, List<int>> SkillDependencies { get; set; } = new();
    }

    /// <summary>
    /// 技能系统Grain实现 - 负责技能学习、释放、冷却管理
    /// </summary>
    public class SkillGrain : Grain, ISkillGrain
    {
        private readonly ILogger<SkillGrain> _logger;
        private readonly IPersistentState<SkillState> _skillState;

        public SkillGrain(
            ILogger<SkillGrain> logger,
            [PersistentState("skill", "GameStore")] IPersistentState<SkillState> skillState)
        {
            _logger = logger;
            _skillState = skillState;
        }

        public override async Task OnActivateAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("SkillGrain {GrainKey} activating.", this.GetPrimaryKey());

            if (_skillState.State.LearnedSkills == null)
                _skillState.State.LearnedSkills = new Dictionary<int, SkillInfo>();

            if (_skillState.State.SkillCooldowns == null)
                _skillState.State.SkillCooldowns = new Dictionary<int, DateTime>();

            if (_skillState.State.SkillDependencies == null)
                _skillState.State.SkillDependencies = new Dictionary<int, List<int>>();

            await base.OnActivateAsync(cancellationToken);
        }

        public async Task<SkillCastMessage> CastSkillAsync(SkillCastMessage request)
        {
            try
            {
                _logger.LogInformation("处理技能施放: SkillId={SkillId}", request.SkillId);

                var state = _skillState.State;

                // 检查是否已学习该技能
                if (!state.LearnedSkills.ContainsKey(request.SkillId))
                {
                    return new SkillCastMessage
                    {
                        CasterId = request.CasterId,
                        SkillId = request.SkillId,
                        Success = false,
                        Message = "未学习该技能"
                    };
                }

                var skill = state.LearnedSkills[request.SkillId];

                // 检查冷却
                if (state.SkillCooldowns.TryGetValue(request.SkillId, out var lastCast))
                {
                    if (!CombatCalculator.IsSkillReady(lastCast, skill.Cooldown))
                    {
                        var remaining = CombatCalculator.GetRemainingCooldown(lastCast, skill.Cooldown);
                        return new SkillCastMessage
                        {
                            CasterId = request.CasterId,
                            SkillId = request.SkillId,
                            Success = false,
                            Message = $"技能冷却中，剩余{remaining:F1}秒"
                        };
                    }
                }

                // 记录冷却
                state.SkillCooldowns[request.SkillId] = DateTime.UtcNow;

                await _skillState.WriteStateAsync();

                _logger.LogInformation("技能施放成功: SkillId={SkillId}", request.SkillId);

                return new SkillCastMessage
                {
                    CasterId = request.CasterId,
                    SkillId = request.SkillId,
                    Success = true,
                    Message = "技能施放成功",
                    EnergyCost = skill.NeiLiCost,
                    CastTime = skill.CastTime,
                    Range = skill.Range
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "技能施放失败: SkillId={SkillId}", request.SkillId);
                throw;
            }
        }

        public async Task<bool> LearnSkillAsync(int skillId)
        {
            try
            {
                var state = _skillState.State;

                if (state.LearnedSkills.ContainsKey(skillId))
                {
                    _logger.LogWarning("技能已学习: SkillId={SkillId}", skillId);
                    return false;
                }

                if (state.SkillPoints <= 0)
                {
                    _logger.LogWarning("技能点不足: SkillId={SkillId}, SkillPoints={SkillPoints}", skillId, state.SkillPoints);
                    return false;
                }

                // Check prerequisites
                if (state.SkillDependencies.TryGetValue(skillId, out var prereqs))
                {
                    foreach (var prereqId in prereqs)
                    {
                        if (!state.LearnedSkills.ContainsKey(prereqId))
                        {
                            _logger.LogWarning("前置技能未学习: SkillId={SkillId}, PrereqId={PrereqId}", skillId, prereqId);
                            return false;
                        }
                    }
                }

                var newSkill = new SkillInfo
                {
                    SkillId = skillId,
                    Level = 1,
                    MaxLevel = 10,
                    Cooldown = 3000,
                    CastTime = 500,
                    NeiLiCost = 10,
                    Range = 5.0f
                };

                state.LearnedSkills[skillId] = newSkill;
                state.SkillPoints--;
                state.TotalSkillPointsUsed++;
                await _skillState.WriteStateAsync();

                _logger.LogInformation("学习技能成功: SkillId={SkillId}, RemainingPoints={SkillPoints}", skillId, state.SkillPoints);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "学习技能失败: SkillId={SkillId}", skillId);
                throw;
            }
        }

        public async Task<bool> UpgradeSkillAsync(int skillId)
        {
            try
            {
                var state = _skillState.State;

                if (!state.LearnedSkills.TryGetValue(skillId, out var skill))
                {
                    _logger.LogWarning("技能未学习: SkillId={SkillId}", skillId);
                    return false;
                }

                if (skill.Level >= skill.MaxLevel)
                {
                    _logger.LogWarning("技能已达最大等级: SkillId={SkillId}, Level={Level}", skillId, skill.Level);
                    return false;
                }

                skill.Level++;
                // 升级后改善技能属性
                skill.NeiLiCost = (int)(skill.NeiLiCost * 1.1f);
                skill.Range += 0.5f;

                await _skillState.WriteStateAsync();

                _logger.LogInformation("技能升级成功: SkillId={SkillId}, NewLevel={Level}", skillId, skill.Level);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "技能升级失败: SkillId={SkillId}", skillId);
                throw;
            }
        }

        public Task<List<SkillInfo>> GetSkillsAsync()
        {
            try
            {
                var skills = _skillState.State.LearnedSkills.Values.ToList();
                return Task.FromResult(skills);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取技能列表失败");
                throw;
            }
        }

        public Task<float> GetSkillCooldownAsync(int skillId)
        {
            try
            {
                var state = _skillState.State;

                if (!state.LearnedSkills.TryGetValue(skillId, out var skill))
                {
                    return Task.FromResult(0f);
                }

                if (!state.SkillCooldowns.TryGetValue(skillId, out var lastCast))
                {
                    return Task.FromResult(0f);
                }

                var remaining = CombatCalculator.GetRemainingCooldown(lastCast, skill.Cooldown);
                return Task.FromResult(remaining);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "查询技能冷却失败: SkillId={SkillId}", skillId);
                throw;
            }
        }

        public async Task<bool> ResetSkillCooldownAsync(int skillId)
        {
            try
            {
                var state = _skillState.State;

                if (state.SkillCooldowns.Remove(skillId))
                {
                    await _skillState.WriteStateAsync();
                    _logger.LogInformation("重置技能冷却: SkillId={SkillId}", skillId);
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "重置技能冷却失败: SkillId={SkillId}", skillId);
                throw;
            }
        }

        public async Task<bool> ResetAllSkillsAsync()
        {
            try
            {
                var state = _skillState.State;

                // Refund 1 point per skill level
                int refundedPoints = 0;
                foreach (var skill in state.LearnedSkills.Values)
                {
                    refundedPoints += skill.Level;
                }

                state.LearnedSkills.Clear();
                state.SkillCooldowns.Clear();
                state.SkillPoints += refundedPoints;
                state.TotalSkillPointsUsed -= refundedPoints;
                if (state.TotalSkillPointsUsed < 0)
                {
                    _logger.LogWarning("技能点统计异常: TotalSkillPointsUsed={TotalSkillPointsUsed}", state.TotalSkillPointsUsed);
                    state.TotalSkillPointsUsed = 0;
                }

                await _skillState.WriteStateAsync();
                _logger.LogInformation("重置所有技能成功: RefundedPoints={RefundedPoints}", refundedPoints);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "重置所有技能失败");
                throw;
            }
        }

        public async Task<bool> SetSkillDependencyAsync(int skillId, List<int> prerequisites)
        {
            try
            {
                _skillState.State.SkillDependencies[skillId] = prerequisites ?? new List<int>();
                await _skillState.WriteStateAsync();
                _logger.LogInformation("设置技能依赖: SkillId={SkillId}, Prerequisites={Prerequisites}", skillId, string.Join(",", prerequisites ?? new List<int>()));
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "设置技能依赖失败: SkillId={SkillId}", skillId);
                throw;
            }
        }

        public Task<int> GetSkillPointsAsync()
        {
            try
            {
                return Task.FromResult(_skillState.State.SkillPoints);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取技能点失败");
                throw;
            }
        }

        public async Task<bool> AddSkillPointsAsync(int points)
        {
            try
            {
                if (points <= 0)
                {
                    _logger.LogWarning("添加技能点数无效: {Points}", points);
                    return false;
                }

                _skillState.State.SkillPoints += points;
                await _skillState.WriteStateAsync();
                _logger.LogInformation("添加技能点成功: Points={Points}, Total={Total}", points, _skillState.State.SkillPoints);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "添加技能点失败: Points={Points}", points);
                throw;
            }
        }
    }
}
