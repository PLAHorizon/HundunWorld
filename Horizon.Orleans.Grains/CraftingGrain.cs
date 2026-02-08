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
    /// 合成系统状态
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class CraftingState
    {
        /// <summary>
        /// 已学习配方（配方ID -> 配方信息）
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public Dictionary<int, CraftingRecipe> LearnedRecipes { get; set; } = new();

        /// <summary>
        /// 合成历史记录
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public List<CraftingHistoryEntry> CraftingHistory { get; set; } = new();
    }

    /// <summary>
    /// 材料合成系统Grain实现
    /// </summary>
    public class CraftingGrain : Grain, ICraftingGrain
    {
        private readonly ILogger<CraftingGrain> _logger;
        private readonly IPersistentState<CraftingState> _craftingState;

        public CraftingGrain(
            ILogger<CraftingGrain> logger,
            [PersistentState("crafting", "GameStore")] IPersistentState<CraftingState> craftingState)
        {
            _logger = logger;
            _craftingState = craftingState;
        }

        public override async Task OnActivateAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("CraftingGrain {GrainKey} activating.", this.GetPrimaryKey());

            if (_craftingState.State.LearnedRecipes == null)
                _craftingState.State.LearnedRecipes = new Dictionary<int, CraftingRecipe>();

            if (_craftingState.State.CraftingHistory == null)
                _craftingState.State.CraftingHistory = new List<CraftingHistoryEntry>();

            await base.OnActivateAsync(cancellationToken);
        }

        public Task<List<CraftingRecipe>> GetRecipesAsync()
        {
            try
            {
                var recipes = _craftingState.State.LearnedRecipes.Values.ToList();
                return Task.FromResult(recipes);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取配方列表失败");
                throw;
            }
        }

        public async Task<bool> LearnRecipeAsync(int recipeId)
        {
            try
            {
                var state = _craftingState.State;

                if (state.LearnedRecipes.ContainsKey(recipeId))
                {
                    _logger.LogWarning("配方已学习: RecipeId={RecipeId}", recipeId);
                    return false;
                }

                var newRecipe = new CraftingRecipe
                {
                    RecipeId = recipeId,
                    SuccessRate = 1.0f,
                    IsRepeatable = true
                };

                state.LearnedRecipes[recipeId] = newRecipe;
                await _craftingState.WriteStateAsync();

                _logger.LogInformation("学习配方成功: RecipeId={RecipeId}", recipeId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "学习配方失败: RecipeId={RecipeId}", recipeId);
                throw;
            }
        }

        public Task<bool> CheckMaterialsAsync(int recipeId)
        {
            try
            {
                var state = _craftingState.State;

                if (!state.LearnedRecipes.ContainsKey(recipeId))
                {
                    _logger.LogWarning("未学习该配方: RecipeId={RecipeId}", recipeId);
                    return Task.FromResult(false);
                }

                // 配方已学习，材料检查需要配合InventoryGrain
                // 此处返回true表示配方存在
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "检查材料失败: RecipeId={RecipeId}", recipeId);
                throw;
            }
        }

        public async Task<CraftingResult> CraftItemAsync(int recipeId)
        {
            try
            {
                var state = _craftingState.State;

                if (!state.LearnedRecipes.TryGetValue(recipeId, out var recipe))
                {
                    _logger.LogWarning("未学习该配方: RecipeId={RecipeId}", recipeId);
                    return new CraftingResult
                    {
                        Success = false,
                        RecipeId = recipeId,
                        Message = "未学习该配方",
                        OutputItemId = 0
                    };
                }

                // Determine quality based on success rate and randomness
                int quality = CalculateCraftingQuality(recipe.SuccessRate);
                bool success = Random.Shared.NextDouble() <= recipe.SuccessRate;

                // Generate a unique output item ID using recipe and history count
                long outputItemId = success ? recipeId * 1000L + state.CraftingHistory.Count : 0;

                var entry = new CraftingHistoryEntry
                {
                    RecipeId = recipeId,
                    Success = success,
                    Timestamp = DateTime.UtcNow,
                    OutputItemId = outputItemId,
                    Quality = success ? quality : 0
                };

                state.CraftingHistory.Add(entry);

                // Limit history to last 100 entries
                if (state.CraftingHistory.Count > 100)
                {
                    state.CraftingHistory.RemoveRange(0, state.CraftingHistory.Count - 100);
                }

                await _craftingState.WriteStateAsync();

                _logger.LogInformation("合成完成: RecipeId={RecipeId}, Success={Success}, Quality={Quality}",
                    recipeId, success, quality);

                return new CraftingResult
                {
                    Success = success,
                    RecipeId = recipeId,
                    Message = success ? $"合成成功（品质：{quality}）" : "合成失败",
                    OutputItemId = outputItemId,
                    Quality = success ? quality : 0
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "合成失败: RecipeId={RecipeId}", recipeId);
                throw;
            }
        }

        public Task<List<CraftingHistoryEntry>> GetCraftingHistoryAsync()
        {
            try
            {
                return Task.FromResult(new List<CraftingHistoryEntry>(_craftingState.State.CraftingHistory));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取合成历史失败");
                throw;
            }
        }

        /// <summary>
        /// 计算制作品质 (0-4: 普通、精良、稀有、史诗、传说)
        /// 成功率越高的配方，产出高品质物品的概率越低（需要更难的配方才能出好东西）
        /// </summary>
        public static int CalculateCraftingQuality(float successRate)
        {
            double roll = Random.Shared.NextDouble();
            // Higher-difficulty recipes (lower success rate) have better quality chances
            float difficultyBonus = Math.Max(0, 1.0f - successRate);

            if (roll < 0.01 + difficultyBonus * 0.04)       // 1-5% 传说
                return 4;
            if (roll < 0.05 + difficultyBonus * 0.10)       // 5-15% 史诗
                return 3;
            if (roll < 0.15 + difficultyBonus * 0.15)       // 15-30% 稀有
                return 2;
            if (roll < 0.35 + difficultyBonus * 0.15)       // 35-50% 精良
                return 1;

            return 0; // 普通
        }
    }
}
