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
    /// 炼丹系统状态
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class AlchemyState
    {
        /// <summary>
        /// 已学习配方（配方ID -> 配方信息）
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public Dictionary<int, AlchemyRecipe> LearnedRecipes { get; set; } = new();

        /// <summary>
        /// 炼丹熟练度
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public float Proficiency { get; set; }

        /// <summary>
        /// 炼丹历史记录
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public List<AlchemyHistoryEntry> AlchemyHistory { get; set; } = new();
    }

    /// <summary>
    /// 五行炼丹系统Grain实现
    /// </summary>
    public class WuxingAlchemyGrain : Grain, IWuxingAlchemyGrain
    {
        private readonly ILogger<WuxingAlchemyGrain> _logger;
        private readonly IPersistentState<AlchemyState> _alchemyState;

        public WuxingAlchemyGrain(
            ILogger<WuxingAlchemyGrain> logger,
            [PersistentState("alchemy", "GameStore")] IPersistentState<AlchemyState> alchemyState)
        {
            _logger = logger;
            _alchemyState = alchemyState;
        }

        public override async Task OnActivateAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("WuxingAlchemyGrain {GrainKey} activating.", this.GetPrimaryKey());

            if (_alchemyState.State.LearnedRecipes == null)
                _alchemyState.State.LearnedRecipes = new Dictionary<int, AlchemyRecipe>();

            if (_alchemyState.State.AlchemyHistory == null)
                _alchemyState.State.AlchemyHistory = new List<AlchemyHistoryEntry>();

            await base.OnActivateAsync(cancellationToken);
        }

        public async Task<bool> LearnAlchemyRecipeAsync(int recipeId)
        {
            try
            {
                var state = _alchemyState.State;

                if (state.LearnedRecipes.ContainsKey(recipeId))
                {
                    _logger.LogWarning("炼丹配方已学习: RecipeId={RecipeId}", recipeId);
                    return false;
                }

                var newRecipe = new AlchemyRecipe
                {
                    RecipeId = recipeId,
                    Name = $"炼丹配方_{recipeId}",
                    RequiredPrimaryElement = 0,
                    RequiredSecondaryElement = 0,
                    OutputItemId = recipeId * 100L,
                    BaseProficiencyGain = 1.0f,
                    MinProficiency = 0f
                };

                state.LearnedRecipes[recipeId] = newRecipe;
                await _alchemyState.WriteStateAsync();

                _logger.LogInformation("学习炼丹配方成功: RecipeId={RecipeId}", recipeId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "学习炼丹配方失败: RecipeId={RecipeId}", recipeId);
                throw;
            }
        }

        public async Task<AlchemyResult> PerformAlchemyAsync(int recipeId, int primaryElement, int secondaryElement)
        {
            try
            {
                var state = _alchemyState.State;

                if (!state.LearnedRecipes.TryGetValue(recipeId, out var recipe))
                {
                    _logger.LogWarning("未学习该炼丹配方: RecipeId={RecipeId}", recipeId);
                    return new AlchemyResult
                    {
                        Success = false,
                        RecipeId = recipeId,
                        Message = "未学习该炼丹配方",
                        OutputItemId = 0
                    };
                }

                if (state.Proficiency < recipe.MinProficiency)
                {
                    _logger.LogWarning("炼丹熟练度不足: RecipeId={RecipeId}, Required={Required}, Current={Current}",
                        recipeId, recipe.MinProficiency, state.Proficiency);
                    return new AlchemyResult
                    {
                        Success = false,
                        RecipeId = recipeId,
                        Message = "炼丹熟练度不足"
                    };
                }

                // Calculate element synergy using CombatCalculator
                float synergyMultiplier = CombatCalculator.GetWuxingSynergyMultiplier(primaryElement, secondaryElement);
                float elementalHarmony = synergyMultiplier;

                // Higher proficiency improves quality chances
                float proficiencyBonus = Math.Min(state.Proficiency * 0.01f, 0.5f);

                // Determine quality: synergy and proficiency both improve quality
                int quality = CalculateAlchemyQuality(synergyMultiplier, proficiencyBonus);

                // Success rate influenced by synergy
                float baseSuccessRate = 0.7f + proficiencyBonus * 0.3f;
                float adjustedSuccessRate = Math.Min(baseSuccessRate * synergyMultiplier, 1.0f);
                bool success = Random.Shared.NextDouble() <= adjustedSuccessRate;

                long outputItemId = success ? recipe.OutputItemId + state.AlchemyHistory.Count : 0;
                float proficiencyGain = success ? recipe.BaseProficiencyGain * synergyMultiplier : recipe.BaseProficiencyGain * 0.25f;

                // Update proficiency
                state.Proficiency += proficiencyGain;

                var entry = new AlchemyHistoryEntry
                {
                    RecipeId = recipeId,
                    Success = success,
                    Timestamp = DateTime.UtcNow,
                    OutputItemId = outputItemId,
                    Quality = success ? quality : 0,
                    PrimaryElement = primaryElement,
                    SecondaryElement = secondaryElement
                };

                state.AlchemyHistory.Add(entry);

                // Limit history to last 100 entries
                if (state.AlchemyHistory.Count > 100)
                {
                    state.AlchemyHistory.RemoveRange(0, state.AlchemyHistory.Count - 100);
                }

                await _alchemyState.WriteStateAsync();

                _logger.LogInformation("炼丹完成: RecipeId={RecipeId}, Success={Success}, Quality={Quality}, Synergy={Synergy}",
                    recipeId, success, quality, synergyMultiplier);

                return new AlchemyResult
                {
                    Success = success,
                    RecipeId = recipeId,
                    Message = success ? $"炼丹成功（品质：{CraftingGrain.GetQualityName(quality)}）" : "炼丹失败",
                    OutputItemId = outputItemId,
                    Quality = success ? quality : 0,
                    ProficiencyGain = proficiencyGain,
                    ElementalHarmony = elementalHarmony
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "炼丹失败: RecipeId={RecipeId}", recipeId);
                throw;
            }
        }

        public Task<List<AlchemyRecipe>> GetAlchemyRecipesAsync()
        {
            try
            {
                var recipes = _alchemyState.State.LearnedRecipes.Values.ToList();
                return Task.FromResult(recipes);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取炼丹配方列表失败");
                throw;
            }
        }

        public Task<List<AlchemyHistoryEntry>> GetAlchemyHistoryAsync()
        {
            try
            {
                return Task.FromResult(new List<AlchemyHistoryEntry>(_alchemyState.State.AlchemyHistory));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取炼丹历史失败");
                throw;
            }
        }

        public Task<float> GetAlchemyProficiencyAsync()
        {
            try
            {
                return Task.FromResult(_alchemyState.State.Proficiency);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取炼丹熟练度失败");
                throw;
            }
        }

        /// <summary>
        /// 计算炼丹品质 (0-4: 普通、精良、稀有、史诗、传说)
        /// 五行协同和熟练度影响品质
        /// </summary>
        private static int CalculateAlchemyQuality(float synergyMultiplier, float proficiencyBonus)
        {
            double roll = Random.Shared.NextDouble();
            float synergyBonus = Math.Max(0, synergyMultiplier - 1.0f);

            if (roll < 0.01 + synergyBonus * 0.04 + proficiencyBonus * 0.02)   // 传说
                return 4;
            if (roll < 0.05 + synergyBonus * 0.10 + proficiencyBonus * 0.05)   // 史诗
                return 3;
            if (roll < 0.15 + synergyBonus * 0.15 + proficiencyBonus * 0.08)   // 稀有
                return 2;
            if (roll < 0.35 + synergyBonus * 0.15 + proficiencyBonus * 0.10)   // 精良
                return 1;

            return 0; // 普通
        }
    }
}
