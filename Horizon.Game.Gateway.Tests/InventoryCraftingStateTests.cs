using Horizon.Orleans.Grains;
using Horizon.Orleans.Interface;
using Horizon.Game.Message.Network;

namespace Horizon.Game.Gateway.Tests
{
    /// <summary>
    /// InventoryState, CraftingState 及相关数据模型的单元测试
    /// 测试背包系统和合成系统的状态管理与业务逻辑
    /// </summary>
    public class InventoryCraftingStateTests
    {
        #region InventoryState Tests - 背包状态默认值

        [Fact]
        public void InventoryState_DefaultValues_AreCorrect()
        {
            var state = new InventoryState();
            Assert.NotNull(state.Items);
            Assert.Empty(state.Items);
            Assert.Equal(50, state.Capacity);
            Assert.Equal(1, state.NextItemId);
            Assert.NotNull(state.EquippedItems);
            Assert.Empty(state.EquippedItems);
        }

        [Fact]
        public void InventoryState_SetCapacity_Works()
        {
            var state = new InventoryState { Capacity = 100 };
            Assert.Equal(100, state.Capacity);
        }

        [Fact]
        public void InventoryState_SetNextItemId_Works()
        {
            var state = new InventoryState { NextItemId = 999 };
            Assert.Equal(999, state.NextItemId);
        }

        #endregion

        #region ItemInfo Tests - 物品数据模型

        [Fact]
        public void ItemInfo_DefaultValues_AreCorrect()
        {
            var item = new ItemInfo();
            Assert.Equal(0, item.ItemId);
            Assert.Equal(0, item.TemplateId);
            Assert.Equal("", item.Name);
            Assert.Equal("", item.Description);
            Assert.Equal(0, item.ItemType);
            Assert.Equal(0, item.Count);
            Assert.Equal(0, item.Level);
            Assert.Equal(0, item.Quality);
            Assert.False(item.IsBound);
            Assert.Equal(0, item.ExpireTime);
            Assert.NotNull(item.Attributes);
            Assert.Empty(item.Attributes);
        }

        [Fact]
        public void ItemInfo_SetAllProperties_Works()
        {
            var item = new ItemInfo
            {
                ItemId = 1001,
                TemplateId = 5001,
                Name = "赤焰剑",
                Description = "一把燃烧着烈焰的宝剑",
                ItemType = 1,
                Count = 1,
                Level = 30,
                Quality = 3,
                IsBound = true,
                ExpireTime = 1700000000,
                Attributes = new Dictionary<string, object> { { "攻击力", 150 }, { "暴击率", 0.1 } }
            };

            Assert.Equal(1001, item.ItemId);
            Assert.Equal(5001, item.TemplateId);
            Assert.Equal("赤焰剑", item.Name);
            Assert.Equal("一把燃烧着烈焰的宝剑", item.Description);
            Assert.Equal(1, item.ItemType);
            Assert.Equal(1, item.Count);
            Assert.Equal(30, item.Level);
            Assert.Equal(3, item.Quality);
            Assert.True(item.IsBound);
            Assert.Equal(1700000000, item.ExpireTime);
            Assert.Equal(2, item.Attributes.Count);
        }

        #endregion

        #region InventoryState Logic Tests - 背包业务逻辑

        [Fact]
        public void InventoryState_AddItem_Works()
        {
            var state = new InventoryState();
            var itemId = state.NextItemId++;

            state.Items[itemId] = new ItemInfo
            {
                ItemId = itemId,
                TemplateId = 1001,
                Name = "回春丹",
                Count = 10
            };

            Assert.Single(state.Items);
            Assert.Equal("回春丹", state.Items[itemId].Name);
            Assert.Equal(2, state.NextItemId);
        }

        [Fact]
        public void InventoryState_RemoveItem_Works()
        {
            var state = new InventoryState();
            state.Items[1] = new ItemInfo { ItemId = 1, Name = "铁矿石", Count = 5 };
            state.Items[2] = new ItemInfo { ItemId = 2, Name = "铜矿石", Count = 3 };

            state.Items.Remove(1);

            Assert.Single(state.Items);
            Assert.False(state.Items.ContainsKey(1));
            Assert.True(state.Items.ContainsKey(2));
        }

        [Fact]
        public void InventoryState_ItemStacking_BySameTemplateAndQuality()
        {
            var state = new InventoryState();
            state.Items[1] = new ItemInfo { ItemId = 1, TemplateId = 100, Name = "灵草", Count = 5, Quality = 1 };
            state.Items[2] = new ItemInfo { ItemId = 2, TemplateId = 100, Name = "灵草", Count = 3, Quality = 1 };

            // 模拟堆叠：合并同模板同品质的物品
            var stackable = state.Items.Values
                .Where(i => i.TemplateId == 100 && i.Quality == 1)
                .ToList();
            var totalCount = stackable.Sum(i => i.Count);

            Assert.Equal(2, stackable.Count);
            Assert.Equal(8, totalCount);
        }

        [Fact]
        public void InventoryState_CapacityCheck_PreventOverflow()
        {
            var state = new InventoryState { Capacity = 3 };

            for (int i = 1; i <= 3; i++)
            {
                state.Items[i] = new ItemInfo { ItemId = i, Name = $"物品{i}" };
            }

            bool canAdd = state.Items.Count < state.Capacity;
            Assert.False(canAdd);
            Assert.Equal(3, state.Items.Count);
        }

        [Fact]
        public void InventoryState_EquipmentSlot_SetAndGet()
        {
            var state = new InventoryState();
            // 装备槽位: 0=武器, 1=头盔, 2=铠甲, 3=护腿, 4=鞋子
            state.EquippedItems[0] = 1001;
            state.EquippedItems[1] = 1002;

            Assert.Equal(2, state.EquippedItems.Count);
            Assert.Equal(1001, state.EquippedItems[0]);
            Assert.Equal(1002, state.EquippedItems[1]);
        }

        [Fact]
        public void InventoryState_EquipmentSlots_FullRange()
        {
            var state = new InventoryState();
            // 共14个装备槽位（0-13）
            for (int slot = 0; slot <= 13; slot++)
            {
                state.EquippedItems[slot] = 1000 + slot;
            }

            Assert.Equal(14, state.EquippedItems.Count);
            Assert.Equal(1000, state.EquippedItems[0]);
            Assert.Equal(1013, state.EquippedItems[13]);
        }

        [Fact]
        public void InventoryState_NextItemId_AutoIncrement()
        {
            var state = new InventoryState();
            var ids = new List<long>();

            for (int i = 0; i < 5; i++)
            {
                var id = state.NextItemId++;
                ids.Add(id);
                state.Items[id] = new ItemInfo { ItemId = id, Name = $"物品{id}" };
            }

            Assert.Equal(new long[] { 1, 2, 3, 4, 5 }, ids);
            Assert.Equal(6, state.NextItemId);
            Assert.Equal(5, state.Items.Count);
        }

        [Fact]
        public void InventoryState_FilterByItemType_Works()
        {
            var state = new InventoryState();
            state.Items[1] = new ItemInfo { ItemId = 1, Name = "赤焰剑", ItemType = 1 };
            state.Items[2] = new ItemInfo { ItemId = 2, Name = "回春丹", ItemType = 3 };
            state.Items[3] = new ItemInfo { ItemId = 3, Name = "玄铁盾", ItemType = 2 };
            state.Items[4] = new ItemInfo { ItemId = 4, Name = "疗伤药", ItemType = 3 };

            var consumables = state.Items.Values.Where(i => i.ItemType == 3).ToList();
            Assert.Equal(2, consumables.Count);
        }

        [Fact]
        public void InventoryState_FilterByQuality_Works()
        {
            var state = new InventoryState();
            // 品质: 0=普通, 1=精良, 2=稀有, 3=史诗, 4=传说
            state.Items[1] = new ItemInfo { ItemId = 1, Name = "铁剑", Quality = 0 };
            state.Items[2] = new ItemInfo { ItemId = 2, Name = "精钢剑", Quality = 1 };
            state.Items[3] = new ItemInfo { ItemId = 3, Name = "龙纹剑", Quality = 3 };
            state.Items[4] = new ItemInfo { ItemId = 4, Name = "天命剑", Quality = 4 };

            var rareOrAbove = state.Items.Values.Where(i => i.Quality >= 2).ToList();
            Assert.Equal(2, rareOrAbove.Count);
        }

        [Fact]
        public void InventoryState_SortByLevel_Works()
        {
            var state = new InventoryState();
            state.Items[1] = new ItemInfo { ItemId = 1, Name = "新手剑", Level = 1 };
            state.Items[2] = new ItemInfo { ItemId = 2, Name = "精钢剑", Level = 30 };
            state.Items[3] = new ItemInfo { ItemId = 3, Name = "龙纹剑", Level = 20 };

            var sorted = state.Items.Values.OrderByDescending(i => i.Level).ToList();
            Assert.Equal("精钢剑", sorted[0].Name);
            Assert.Equal("龙纹剑", sorted[1].Name);
            Assert.Equal("新手剑", sorted[2].Name);
        }

        #endregion

        #region CraftingState Tests - 合成状态默认值

        [Fact]
        public void CraftingState_DefaultValues_AreCorrect()
        {
            var state = new CraftingState();
            Assert.NotNull(state.LearnedRecipes);
            Assert.Empty(state.LearnedRecipes);
            Assert.NotNull(state.CraftingHistory);
            Assert.Empty(state.CraftingHistory);
        }

        [Fact]
        public void CraftingState_SetLearnedRecipes_Works()
        {
            var state = new CraftingState();
            state.LearnedRecipes[1] = new CraftingRecipe { RecipeId = 1, Name = "铁剑配方" };

            Assert.Single(state.LearnedRecipes);
            Assert.Equal("铁剑配方", state.LearnedRecipes[1].Name);
        }

        #endregion

        #region CraftingRecipe Tests - 合成配方数据模型

        [Fact]
        public void CraftingRecipe_DefaultValues_AreCorrect()
        {
            var recipe = new CraftingRecipe();
            Assert.Equal(0, recipe.RecipeId);
            Assert.Equal("", recipe.Name);
            Assert.Equal("", recipe.Description);
            Assert.NotNull(recipe.RequiredMaterials);
            Assert.Empty(recipe.RequiredMaterials);
            Assert.Equal(0, recipe.RequiredGold);
            Assert.Equal(0, recipe.RequiredLevel);
            Assert.NotNull(recipe.RequiredSkills);
            Assert.Empty(recipe.RequiredSkills);
            Assert.NotNull(recipe.OutputItems);
            Assert.Empty(recipe.OutputItems);
            Assert.Equal(0, recipe.CraftingTime);
            Assert.Equal(1.0f, recipe.SuccessRate);
            Assert.True(recipe.IsRepeatable);
            Assert.Equal("", recipe.RecipeType);
        }

        [Fact]
        public void CraftingRecipe_SetAllProperties_Works()
        {
            var recipe = new CraftingRecipe
            {
                RecipeId = 2001,
                Name = "玄铁剑配方",
                Description = "锻造一把玄铁剑所需的配方",
                RequiredMaterials = new Dictionary<long, int> { { 100, 5 }, { 101, 3 } },
                RequiredGold = 10000,
                RequiredLevel = 20,
                RequiredSkills = new Dictionary<string, int> { { "锻造", 5 } },
                OutputItems = new Dictionary<long, int> { { 200, 1 } },
                CraftingTime = 5000,
                SuccessRate = 0.8f,
                IsRepeatable = false,
                RecipeType = "武器"
            };

            Assert.Equal(2001, recipe.RecipeId);
            Assert.Equal("玄铁剑配方", recipe.Name);
            Assert.Equal("锻造一把玄铁剑所需的配方", recipe.Description);
            Assert.Equal(2, recipe.RequiredMaterials.Count);
            Assert.Equal(5, recipe.RequiredMaterials[100]);
            Assert.Equal(10000, recipe.RequiredGold);
            Assert.Equal(20, recipe.RequiredLevel);
            Assert.Single(recipe.RequiredSkills);
            Assert.Single(recipe.OutputItems);
            Assert.Equal(5000, recipe.CraftingTime);
            Assert.Equal(0.8f, recipe.SuccessRate);
            Assert.False(recipe.IsRepeatable);
            Assert.Equal("武器", recipe.RecipeType);
        }

        [Fact]
        public void CraftingRecipe_RequiredMaterials_MultipleEntries()
        {
            var recipe = new CraftingRecipe
            {
                RequiredMaterials = new Dictionary<long, int>
                {
                    { 100, 10 },  // 铁矿石 x10
                    { 101, 5 },   // 木材 x5
                    { 102, 2 }    // 宝石 x2
                }
            };

            Assert.Equal(3, recipe.RequiredMaterials.Count);
            Assert.Equal(10, recipe.RequiredMaterials[100]);
            Assert.Equal(5, recipe.RequiredMaterials[101]);
            Assert.Equal(2, recipe.RequiredMaterials[102]);
        }

        #endregion

        #region CraftingResult Tests - 合成结果

        [Fact]
        public void CraftingResult_DefaultValues_AreCorrect()
        {
            var result = new CraftingResult();
            Assert.False(result.Success);
            Assert.Equal(0, result.RecipeId);
            Assert.Equal("", result.Message);
            Assert.Equal(0, result.OutputItemId);
            Assert.Equal(0, result.Quality);
        }

        [Fact]
        public void CraftingResult_SuccessResult_Works()
        {
            var result = new CraftingResult
            {
                Success = true,
                RecipeId = 2001,
                Message = "合成成功",
                OutputItemId = 5001,
                Quality = 3
            };

            Assert.True(result.Success);
            Assert.Equal(2001, result.RecipeId);
            Assert.Equal("合成成功", result.Message);
            Assert.Equal(5001, result.OutputItemId);
            Assert.Equal(3, result.Quality);
        }

        [Fact]
        public void CraftingResult_FailureResult_Works()
        {
            var result = new CraftingResult
            {
                Success = false,
                RecipeId = 2001,
                Message = "材料不足，合成失败",
                OutputItemId = 0,
                Quality = 0
            };

            Assert.False(result.Success);
            Assert.Equal("材料不足，合成失败", result.Message);
            Assert.Equal(0, result.OutputItemId);
        }

        #endregion

        #region CraftingHistoryEntry Tests - 合成历史记录

        [Fact]
        public void CraftingHistoryEntry_DefaultValues_AreCorrect()
        {
            var entry = new CraftingHistoryEntry();
            Assert.Equal(0, entry.RecipeId);
            Assert.False(entry.Success);
            Assert.Equal(default(DateTime), entry.Timestamp);
            Assert.Equal(0, entry.OutputItemId);
            Assert.Equal(0, entry.Quality);
        }

        [Fact]
        public void CraftingHistoryEntry_SetProperties_Works()
        {
            var now = DateTime.UtcNow;
            var entry = new CraftingHistoryEntry
            {
                RecipeId = 2001,
                Success = true,
                Timestamp = now,
                OutputItemId = 5001,
                Quality = 2
            };

            Assert.Equal(2001, entry.RecipeId);
            Assert.True(entry.Success);
            Assert.Equal(now, entry.Timestamp);
            Assert.Equal(5001, entry.OutputItemId);
            Assert.Equal(2, entry.Quality);
        }

        #endregion

        #region CraftingState Logic Tests - 合成系统业务逻辑

        [Fact]
        public void CraftingState_LearnMultipleRecipes_Works()
        {
            var state = new CraftingState();

            state.LearnedRecipes[1] = new CraftingRecipe { RecipeId = 1, Name = "铁剑配方", RecipeType = "武器" };
            state.LearnedRecipes[2] = new CraftingRecipe { RecipeId = 2, Name = "皮甲配方", RecipeType = "防具" };
            state.LearnedRecipes[3] = new CraftingRecipe { RecipeId = 3, Name = "回春丹配方", RecipeType = "药品" };

            Assert.Equal(3, state.LearnedRecipes.Count);
            Assert.True(state.LearnedRecipes.ContainsKey(1));
            Assert.True(state.LearnedRecipes.ContainsKey(2));
            Assert.True(state.LearnedRecipes.ContainsKey(3));
        }

        [Fact]
        public void CraftingState_AddCraftingHistory_Works()
        {
            var state = new CraftingState();
            var now = DateTime.UtcNow;

            state.CraftingHistory.Add(new CraftingHistoryEntry
            {
                RecipeId = 1,
                Success = true,
                Timestamp = now,
                OutputItemId = 5001,
                Quality = 2
            });

            Assert.Single(state.CraftingHistory);
            Assert.Equal(1, state.CraftingHistory[0].RecipeId);
            Assert.True(state.CraftingHistory[0].Success);
        }

        [Fact]
        public void CraftingState_HistoryCap_At100Entries()
        {
            var state = new CraftingState();
            var baseTime = DateTime.UtcNow;

            for (int i = 0; i < 110; i++)
            {
                state.CraftingHistory.Add(new CraftingHistoryEntry
                {
                    RecipeId = i % 5,
                    Success = i % 2 == 0,
                    Timestamp = baseTime.AddMinutes(i)
                });
            }

            // 模拟历史记录上限：保留最近100条
            if (state.CraftingHistory.Count > 100)
            {
                state.CraftingHistory = state.CraftingHistory
                    .OrderByDescending(h => h.Timestamp)
                    .Take(100)
                    .ToList();
            }

            Assert.Equal(100, state.CraftingHistory.Count);
        }

        [Fact]
        public void CraftingState_FilterHistory_BySuccess()
        {
            var state = new CraftingState();
            var now = DateTime.UtcNow;

            state.CraftingHistory.Add(new CraftingHistoryEntry { RecipeId = 1, Success = true, Timestamp = now });
            state.CraftingHistory.Add(new CraftingHistoryEntry { RecipeId = 2, Success = false, Timestamp = now.AddMinutes(1) });
            state.CraftingHistory.Add(new CraftingHistoryEntry { RecipeId = 3, Success = true, Timestamp = now.AddMinutes(2) });
            state.CraftingHistory.Add(new CraftingHistoryEntry { RecipeId = 4, Success = false, Timestamp = now.AddMinutes(3) });
            state.CraftingHistory.Add(new CraftingHistoryEntry { RecipeId = 5, Success = true, Timestamp = now.AddMinutes(4) });

            var successes = state.CraftingHistory.Where(h => h.Success).ToList();
            var failures = state.CraftingHistory.Where(h => !h.Success).ToList();

            Assert.Equal(3, successes.Count);
            Assert.Equal(2, failures.Count);
        }

        [Fact]
        public void CraftingState_SortHistory_ByTimestamp()
        {
            var state = new CraftingState();
            var baseTime = DateTime.UtcNow;

            state.CraftingHistory.Add(new CraftingHistoryEntry { RecipeId = 3, Timestamp = baseTime.AddMinutes(3) });
            state.CraftingHistory.Add(new CraftingHistoryEntry { RecipeId = 1, Timestamp = baseTime.AddMinutes(1) });
            state.CraftingHistory.Add(new CraftingHistoryEntry { RecipeId = 2, Timestamp = baseTime.AddMinutes(2) });

            var sorted = state.CraftingHistory.OrderByDescending(h => h.Timestamp).ToList();

            Assert.Equal(3, sorted[0].RecipeId);
            Assert.Equal(2, sorted[1].RecipeId);
            Assert.Equal(1, sorted[2].RecipeId);
        }

        [Fact]
        public void CraftingState_LearnRecipe_DuplicateOverwrites()
        {
            var state = new CraftingState();

            state.LearnedRecipes[1] = new CraftingRecipe { RecipeId = 1, Name = "铁剑配方", SuccessRate = 0.5f };
            state.LearnedRecipes[1] = new CraftingRecipe { RecipeId = 1, Name = "铁剑配方（改良）", SuccessRate = 0.9f };

            Assert.Single(state.LearnedRecipes);
            Assert.Equal("铁剑配方（改良）", state.LearnedRecipes[1].Name);
            Assert.Equal(0.9f, state.LearnedRecipes[1].SuccessRate);
        }

        [Fact]
        public void CraftingState_FilterRecipes_ByType()
        {
            var state = new CraftingState();

            state.LearnedRecipes[1] = new CraftingRecipe { RecipeId = 1, Name = "铁剑配方", RecipeType = "武器" };
            state.LearnedRecipes[2] = new CraftingRecipe { RecipeId = 2, Name = "皮甲配方", RecipeType = "防具" };
            state.LearnedRecipes[3] = new CraftingRecipe { RecipeId = 3, Name = "钢剑配方", RecipeType = "武器" };
            state.LearnedRecipes[4] = new CraftingRecipe { RecipeId = 4, Name = "回春丹配方", RecipeType = "药品" };

            var weapons = state.LearnedRecipes.Values.Where(r => r.RecipeType == "武器").ToList();
            Assert.Equal(2, weapons.Count);
        }

        #endregion
    }
}
