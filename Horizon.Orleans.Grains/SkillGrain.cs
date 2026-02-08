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

                var newSkill = new SkillInfo
                {
                    SkillId = skillId,
                    Level = 1,
                    MaxLevel = 10,
                    Cooldown = 3000, // 默认3秒冷却
                    CastTime = 500,  // 默认0.5秒施法
                    NeiLiCost = 10,
                    Range = 5.0f
                };

                state.LearnedSkills[skillId] = newSkill;
                await _skillState.WriteStateAsync();

                _logger.LogInformation("学习技能成功: SkillId={SkillId}", skillId);
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
    }
}
