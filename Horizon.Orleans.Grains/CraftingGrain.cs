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
    }
}
