using Horizon.Game.Message.Network;
using Horizon.Game.Sim.Npcs;
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

        /// <summary>
        /// 学习配方。<b>定义由调用方给</b>（来自已装配内容的 <c>NpcContent.Recipe</c>）。
        /// <para>
        /// 以前这里会当场捏一个 <c>SuccessRate = 1.0</c> 的空配方——那让"任意 id 都能学会"，
        /// 而且成功率是编的。现在只做三件事：id 合法？没学过？存一份**调用方给的定义**。
        /// </para>
        /// <para>
        /// <b>SuccessRate 的语义</b>：本工程没有"制作技能点"这一权威输入，内容侧通常给 1.0
        /// （材料齐即可做）；这不是占位符，是"一期没有失败检定"的明确口径，见设计文档 §11.10。
        /// </para>
        /// </summary>
        public async Task<bool> LearnRecipeAsync(CraftingRecipe definition)
        {
            try
            {
                if (definition == null || definition.RecipeId <= 0)
                {
                    _logger.LogWarning("配方定义无效，拒绝学习");
                    return false;
                }

                var state = _craftingState.State;
                if (state.LearnedRecipes.ContainsKey(definition.RecipeId))
                {
                    _logger.LogWarning("配方已学习: RecipeId={RecipeId}", definition.RecipeId);
                    return false;
                }

                // 存副本：调用方之后改自己的对象，不应影响已学配方。
                state.LearnedRecipes[definition.RecipeId] = new CraftingRecipe
                {
                    RecipeId = definition.RecipeId,
                    Name = definition.Name ?? string.Empty,
                    Description = definition.Description ?? string.Empty,
                    RequiredMaterials = definition.RequiredMaterials != null
                        ? new Dictionary<long, int>(definition.RequiredMaterials)
                        : new Dictionary<long, int>(),
                    RequiredGold = definition.RequiredGold,
                    RequiredLevel = definition.RequiredLevel,
                    OutputItems = definition.OutputItems != null
                        ? new Dictionary<long, int>(definition.OutputItems)
                        : new Dictionary<long, int>(),
                    SuccessRate = definition.SuccessRate,
                    IsRepeatable = definition.IsRepeatable,
                    RecipeType = definition.RecipeType ?? string.Empty,
                };

                await _craftingState.WriteStateAsync();

                _logger.LogInformation("学习配方成功: RecipeId={RecipeId}", definition.RecipeId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "学习配方失败: RecipeId={RecipeId}", definition?.RecipeId ?? 0);
                throw;
            }
        }

        /// <summary>
        /// 查材料：逐件比对，回"缺哪样、缺几件"。
        /// <para>
        /// 判据调用 <see cref="NpcCraftCore.CheckMaterials"/>（纯函数，已有独立暴力参照对拍），
        /// 本方法只负责把背包快照与已学配方翻译成内核输入。
        /// <b>不得再出现"配方存在就返回 true"</b>——那是让玩家凭空造物的口子。
        /// </para>
        /// </summary>
        public Task<CraftMaterialCheck> CheckMaterialsAsync(int recipeId, Dictionary<long, int> held)
        {
            try
            {
                var check = new CraftMaterialCheck();

                if (!_craftingState.State.LearnedRecipes.TryGetValue(recipeId, out var recipe) || recipe == null)
                {
                    _logger.LogWarning("未学习该配方: RecipeId={RecipeId}", recipeId);
                    check.Ok = false;
                    check.Reject = (int)NpcCraftReject.RecipeNotLearned;
                    return Task.FromResult(check);
                }

                var needs = new List<NpcMaterialNeed>(recipe.RequiredMaterials.Count);
                foreach (var pair in recipe.RequiredMaterials)
                {
                    needs.Add(new NpcMaterialNeed((int)pair.Key, pair.Value));
                }

                var heldItems = new List<NpcHeldItem>(held?.Count ?? 0);
                if (held != null)
                {
                    foreach (var pair in held)
                    {
                        heldItems.Add(new NpcHeldItem((int)pair.Key, pair.Value));
                    }
                }

                var verdict = NpcCraftCore.CheckMaterials(needs, heldItems);

                check.Ok = verdict.IsOk;
                check.Reject = (int)verdict.Reason;
                check.MissingKinds = verdict.MissingKinds;
                check.MissingUnits = verdict.MissingUnits;

                var lines = new List<string>(verdict.Shortfalls.Count);
                for (var i = 0; i < verdict.Shortfalls.Count; i++) lines.Add(verdict.Shortfalls[i].Describe());
                check.Lines = lines.ToArray();

                return Task.FromResult(check);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "检查材料失败: RecipeId={RecipeId}", recipeId);
                throw;
            }
        }

        public async Task<CraftingResult> CraftItemAsync(int recipeId, Dictionary<long, int> held)
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

                // 材料必须先真查一遍：调用方把背包快照传进来，判据走 NpcCraftCore。
                var materials = await CheckMaterialsAsync(recipeId, held);
                if (!materials.Ok)
                {
                    _logger.LogInformation("合成被拒（材料不齐/配方异常）: RecipeId={RecipeId} reject={Reject}",
                        recipeId, materials.Reject);
                    return new CraftingResult
                    {
                        Success = false,
                        RecipeId = recipeId,
                        Message = "材料不足：" + string.Join("；", materials.Lines),
                        OutputItemId = 0,
                    };
                }

                int quality = CalculateCraftingQuality(recipe.SuccessRate);
                bool success = recipe.SuccessRate >= 1.0f || Random.Shared.NextDouble() <= recipe.SuccessRate;

                // 实例 id 由背包发放时决定，这里不再伪造（旧实现用 recipeId*1000+历史数 编了一个假 id）。
                long outputItemId = 0;

                var entry = new CraftingHistoryEntry
                {
                    RecipeId = recipeId,
                    Success = success,
                    Timestamp = DateTime.Now,
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
                    Message = success ? $"合成成功（品质：{GetQualityName(quality)}）" : "合成失败",
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
        /// 品质名称映射
        /// </summary>
        private static readonly string[] QualityNames = { "普通", "精良", "稀有", "史诗", "传说" };

        /// <summary>
        /// 获取品质名称
        /// </summary>
        public static string GetQualityName(int quality)
        {
            if (quality >= 0 && quality < QualityNames.Length)
                return QualityNames[quality];
            return QualityNames[0];
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
