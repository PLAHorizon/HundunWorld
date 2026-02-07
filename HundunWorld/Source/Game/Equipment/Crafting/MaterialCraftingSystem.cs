using FlaxEngine;
using FlaxEngine.Utilities;
using Game.Character.Attributes;
using Game.Equipment.Material;
using System.Collections.Generic;

namespace Game.Equipment.Crafting
{
    /// <summary>
    /// 材料合成配方
    /// </summary>
    public class CraftingRecipe
    {
        /// <summary>配方ID</summary>
        public int RecipeId;

        /// <summary>配方名称</summary>
        public string RecipeName;

        /// <summary>所需材料ID列表</summary>
        public List<int> RequiredMaterialIds = new List<int>();

        /// <summary>所需材料数量列表</summary>
        public List<int> RequiredMaterialCounts = new List<int>();

        /// <summary>产出材料ID</summary>
        public int OutputMaterialId;

        /// <summary>产出数量</summary>
        public int OutputCount = 1;

        /// <summary>货币消耗</summary>
        public int CurrencyCost = 0;

        /// <summary>成功率（0-100）</summary>
        public float SuccessRate = 100f;
    }

    /// <summary>
    /// 材料合成系统
    /// 管理材料的逐级合成
    /// </summary>
    public class MaterialCraftingSystem : Script
    {
        #region 合成配置

        [Header("合成规则")]
        [Tooltip("合成比例（优化后）")]
        public int CraftingRatio = 5; // 5:1 合成比例

        [Tooltip("高级材料合成比例")]
        public int AdvancedCraftingRatio = 3; // 高级→仙级 3:1

        #endregion

        private static MaterialCraftingSystem instance;
        private Dictionary<int, CraftingRecipe> recipeDatabase = new Dictionary<int, CraftingRecipe>();

        public override void OnAwake()
        {
            instance = this;
            InitializeRecipes();
        }

        /// <summary>
        /// 初始化合成配方
        /// </summary>
        private void InitializeRecipes()
        {
            // 初级→中级材料配方（5:1）
            AddTierUpgradeRecipe(
                recipeId: 1001,
                recipeName: "精炼铁矿石",
                inputMaterialId: 10001, // 铁矿石
                inputCount: 5,
                outputMaterialId: 20001, // 精铁
                currencyCost: 50
            );

            AddTierUpgradeRecipe(
                recipeId: 1002,
                recipeName: "提炼青竹精华",
                inputMaterialId: 10002, // 青竹
                inputCount: 5,
                outputMaterialId: 20002, // 紫檀木（假设ID）
                currencyCost: 50
            );

            // TODO: 添加更多合成配方
            // 中级→高级（5:1）
            // 高级→仙级（3:1）
            // 仙级→神级（3:1）

            Debug.Log($"材料合成系统初始化完成，加载了 {recipeDatabase.Count} 个配方");
        }

        /// <summary>
        /// 添加等级提升配方
        /// </summary>
        private void AddTierUpgradeRecipe(int recipeId, string recipeName, int inputMaterialId, 
            int inputCount, int outputMaterialId, int currencyCost)
        {
            var recipe = new CraftingRecipe
            {
                RecipeId = recipeId,
                RecipeName = recipeName,
                RequiredMaterialIds = new List<int> { inputMaterialId },
                RequiredMaterialCounts = new List<int> { inputCount },
                OutputMaterialId = outputMaterialId,
                OutputCount = 1,
                CurrencyCost = currencyCost,
                SuccessRate = 100f // 100%成功率
            };

            recipeDatabase[recipeId] = recipe;
        }

        /// <summary>
        /// 尝试合成材料
        /// </summary>
        /// <param name="recipeId">配方ID</param>
        /// <param name="playerInventory">玩家背包（TODO：实现背包系统）</param>
        /// <param name="playerCurrency">玩家货币</param>
        /// <returns>是否合成成功</returns>
        public bool TryCraftMaterial(int recipeId, object playerInventory, ref int playerCurrency)
        {
            if (!recipeDatabase.ContainsKey(recipeId))
            {
                Debug.LogWarning($"配方 {recipeId} 不存在");
                return false;
            }

            var recipe = recipeDatabase[recipeId];

            // 检查货币
            if (playerCurrency < recipe.CurrencyCost)
            {
                Debug.LogWarning($"货币不足，需要 {recipe.CurrencyCost} 金，当前仅有 {playerCurrency} 金");
                return false;
            }

            // TODO: 检查材料是否足够（需要实现背包系统）

            // 执行合成
            playerCurrency -= recipe.CurrencyCost;

            // 判断成功率
            float roll = RandomUtil.Random.NextFloat() * 100f;
            if (roll <= recipe.SuccessRate)
            {
                // 合成成功
                Debug.Log($"合成成功：{recipe.RecipeName}，获得材料ID {recipe.OutputMaterialId} × {recipe.OutputCount}");
                // TODO: 添加材料到背包
                return true;
            }
            else
            {
                // 合成失败（当前所有配方都是100%成功率）
                Debug.LogWarning($"合成失败：{recipe.RecipeName}");
                return false;
            }
        }

        /// <summary>
        /// 获取配方信息
        /// </summary>
        public CraftingRecipe GetRecipe(int recipeId)
        {
            return recipeDatabase.ContainsKey(recipeId) ? recipeDatabase[recipeId] : null;
        }

        /// <summary>
        /// 获取所有配方
        /// </summary>
        public List<CraftingRecipe> GetAllRecipes()
        {
            return new List<CraftingRecipe>(recipeDatabase.Values);
        }

        /// <summary>
        /// 快速合成（批量合成）
        /// </summary>
        public int QuickCraft(int recipeId, int craftCount, object playerInventory, ref int playerCurrency)
        {
            int successCount = 0;

            for (int i = 0; i < craftCount; i++)
            {
                if (TryCraftMaterial(recipeId, playerInventory, ref playerCurrency))
                {
                    successCount++;
                }
                else
                {
                    break; // 材料或货币不足，停止合成
                }
            }

            Debug.Log($"快速合成完成，成功合成 {successCount}/{craftCount} 次");
            return successCount;
        }

        /// <summary>
        /// 获取单例实例
        /// </summary>
        public static MaterialCraftingSystem Instance => instance;
    }
}
