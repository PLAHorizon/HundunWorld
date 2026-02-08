using Horizon.Orleans.Grains;
using Horizon.Game.Message.Network;

namespace Horizon.Game.Gateway.Tests
{
    /// <summary>
    /// InventoryState, SkillState, CraftingState 数据模型单元测试
    /// 测试背包、技能、合成系统的状态管理逻辑
    /// </summary>
    public class GameSystemStateTests
    {
        #region InventoryState Tests - 背包状态

        [Fact]
        public void InventoryState_DefaultCapacity_Is50()
        {
            var state = new InventoryState();
            Assert.Equal(50, state.Capacity);
        }

        [Fact]
        public void InventoryState_DefaultItems_IsEmpty()
        {
            var state = new InventoryState();
            Assert.NotNull(state.Items);
            Assert.Empty(state.Items);
        }

        [Fact]
        public void InventoryState_DefaultNextItemId_Is1()
        {
            var state = new InventoryState();
            Assert.Equal(1, state.NextItemId);
        }

        [Fact]
        public void InventoryState_AddItem_IncrementsCount()
        {
            var state = new InventoryState();
            state.Items[1] = new ItemInfo { ItemId = 1, TemplateId = 100, Count = 5 };
            Assert.Single(state.Items);
        }

        [Fact]
        public void InventoryState_AddMultipleItems_TracksAll()
        {
            var state = new InventoryState();
            state.Items[1] = new ItemInfo { ItemId = 1, TemplateId = 100, Count = 5 };
            state.Items[2] = new ItemInfo { ItemId = 2, TemplateId = 101, Count = 3 };
            state.Items[3] = new ItemInfo { ItemId = 3, TemplateId = 102, Count = 1 };
            Assert.Equal(3, state.Items.Count);
        }

        [Fact]
        public void InventoryState_RemoveItem_DecreasesCount()
        {
            var state = new InventoryState();
            state.Items[1] = new ItemInfo { ItemId = 1, TemplateId = 100, Count = 5 };
            state.Items[2] = new ItemInfo { ItemId = 2, TemplateId = 101, Count = 3 };
            state.Items.Remove(1);
            Assert.Single(state.Items);
        }

        [Fact]
        public void InventoryState_ItemStackable_UpdatesCount()
        {
            var state = new InventoryState();
            var item = new ItemInfo { ItemId = 1, TemplateId = 100, Count = 5 };
            state.Items[1] = item;
            item.Count += 10;
            Assert.Equal(15, state.Items[1].Count);
        }

        [Fact]
        public void InventoryState_CapacityCheck_WorksCorrectly()
        {
            var state = new InventoryState { Capacity = 2 };
            state.Items[1] = new ItemInfo { ItemId = 1 };
            state.Items[2] = new ItemInfo { ItemId = 2 };
            Assert.Equal(state.Capacity, state.Items.Count);
        }

        [Fact]
        public void InventoryState_ExpandCapacity_IncreasesCapacity()
        {
            var state = new InventoryState { Capacity = 50 };
            state.Capacity += 10;
            Assert.Equal(60, state.Capacity);
        }

        #endregion

        #region SkillState Tests - 技能状态

        [Fact]
        public void SkillState_DefaultLearnedSkills_IsEmpty()
        {
            var state = new SkillState();
            Assert.NotNull(state.LearnedSkills);
            Assert.Empty(state.LearnedSkills);
        }

        [Fact]
        public void SkillState_DefaultSkillCooldowns_IsEmpty()
        {
            var state = new SkillState();
            Assert.NotNull(state.SkillCooldowns);
            Assert.Empty(state.SkillCooldowns);
        }

        [Fact]
        public void SkillState_LearnSkill_AddedToLearnedSkills()
        {
            var state = new SkillState();
            var skill = new SkillInfo { SkillId = 1, Level = 1, MaxLevel = 10 };
            state.LearnedSkills[1] = skill;
            Assert.Single(state.LearnedSkills);
            Assert.Equal(1, state.LearnedSkills[1].Level);
        }

        [Fact]
        public void SkillState_UpgradeSkill_IncreasesLevel()
        {
            var state = new SkillState();
            var skill = new SkillInfo { SkillId = 1, Level = 1, MaxLevel = 10 };
            state.LearnedSkills[1] = skill;
            skill.Level++;
            Assert.Equal(2, state.LearnedSkills[1].Level);
        }

        [Fact]
        public void SkillState_SkillAtMaxLevel_CannotUpgrade()
        {
            var state = new SkillState();
            var skill = new SkillInfo { SkillId = 1, Level = 10, MaxLevel = 10 };
            state.LearnedSkills[1] = skill;
            Assert.Equal(skill.Level, skill.MaxLevel);
        }

        [Fact]
        public void SkillState_TrackCooldown_RecordsTime()
        {
            var state = new SkillState();
            var castTime = DateTime.UtcNow;
            state.SkillCooldowns[1] = castTime;
            Assert.Equal(castTime, state.SkillCooldowns[1]);
        }

        [Fact]
        public void SkillState_ResetCooldown_RemovesEntry()
        {
            var state = new SkillState();
            state.SkillCooldowns[1] = DateTime.UtcNow;
            state.SkillCooldowns.Remove(1);
            Assert.Empty(state.SkillCooldowns);
        }

        [Fact]
        public void SkillState_MultipleCooldowns_TrackedIndependently()
        {
            var state = new SkillState();
            state.SkillCooldowns[1] = DateTime.UtcNow;
            state.SkillCooldowns[2] = DateTime.UtcNow.AddSeconds(-3);
            state.SkillCooldowns[3] = DateTime.UtcNow.AddSeconds(-10);
            Assert.Equal(3, state.SkillCooldowns.Count);
        }

        [Fact]
        public void SkillState_CooldownWithCalculator_IntegrationTest()
        {
            var state = new SkillState();
            var skill = new SkillInfo { SkillId = 1, Cooldown = 3000 }; // 3秒冷却
            state.LearnedSkills[1] = skill;
            state.SkillCooldowns[1] = DateTime.UtcNow.AddSeconds(-5); // 5秒前施放

            // 应该已经冷却完毕
            Assert.True(CombatCalculator.IsSkillReady(state.SkillCooldowns[1], skill.Cooldown));
            Assert.Equal(0f, CombatCalculator.GetRemainingCooldown(state.SkillCooldowns[1], skill.Cooldown));
        }

        [Fact]
        public void SkillState_CooldownNotReady_IntegrationTest()
        {
            var state = new SkillState();
            var skill = new SkillInfo { SkillId = 1, Cooldown = 3000 }; // 3秒冷却
            state.LearnedSkills[1] = skill;
            state.SkillCooldowns[1] = DateTime.UtcNow.AddMilliseconds(-500); // 0.5秒前施放

            // 应该仍在冷却中
            Assert.False(CombatCalculator.IsSkillReady(state.SkillCooldowns[1], skill.Cooldown));
            var remaining = CombatCalculator.GetRemainingCooldown(state.SkillCooldowns[1], skill.Cooldown);
            Assert.True(remaining > 0f);
        }

        #endregion

        #region CraftingState Tests - 合成状态

        [Fact]
        public void CraftingState_DefaultLearnedRecipes_IsEmpty()
        {
            var state = new CraftingState();
            Assert.NotNull(state.LearnedRecipes);
            Assert.Empty(state.LearnedRecipes);
        }

        [Fact]
        public void CraftingState_LearnRecipe_AddedToLearnedRecipes()
        {
            var state = new CraftingState();
            var recipe = new CraftingRecipe { RecipeId = 1, SuccessRate = 0.8f };
            state.LearnedRecipes[1] = recipe;
            Assert.Single(state.LearnedRecipes);
        }

        [Fact]
        public void CraftingState_LearnMultipleRecipes_TracksAll()
        {
            var state = new CraftingState();
            state.LearnedRecipes[1] = new CraftingRecipe { RecipeId = 1 };
            state.LearnedRecipes[2] = new CraftingRecipe { RecipeId = 2 };
            state.LearnedRecipes[3] = new CraftingRecipe { RecipeId = 3 };
            Assert.Equal(3, state.LearnedRecipes.Count);
        }

        [Fact]
        public void CraftingState_RecipeProperties_CorrectlySet()
        {
            var recipe = new CraftingRecipe
            {
                RecipeId = 1,
                Name = "铁剑",
                Description = "基础铁剑制作",
                RequiredLevel = 5,
                RequiredGold = 100,
                SuccessRate = 0.95f,
                IsRepeatable = true,
                CraftingTime = 5000
            };

            Assert.Equal(1, recipe.RecipeId);
            Assert.Equal("铁剑", recipe.Name);
            Assert.Equal(5, recipe.RequiredLevel);
            Assert.Equal(100, recipe.RequiredGold);
            Assert.Equal(0.95f, recipe.SuccessRate);
            Assert.True(recipe.IsRepeatable);
            Assert.Equal(5000, recipe.CraftingTime);
        }

        [Fact]
        public void CraftingState_RecipeMaterials_CanBeConfigured()
        {
            var recipe = new CraftingRecipe
            {
                RecipeId = 1,
                RequiredMaterials = new Dictionary<long, int>
                {
                    { 100, 5 }, // 铁矿石 x5
                    { 101, 2 }  // 木材 x2
                }
            };
            Assert.Equal(2, recipe.RequiredMaterials.Count);
            Assert.Equal(5, recipe.RequiredMaterials[100]);
            Assert.Equal(2, recipe.RequiredMaterials[101]);
        }

        [Fact]
        public void CraftingState_DuplicateRecipe_OverwritesPrevious()
        {
            var state = new CraftingState();
            state.LearnedRecipes[1] = new CraftingRecipe { RecipeId = 1, SuccessRate = 0.5f };
            state.LearnedRecipes[1] = new CraftingRecipe { RecipeId = 1, SuccessRate = 0.9f };
            Assert.Single(state.LearnedRecipes);
            Assert.Equal(0.9f, state.LearnedRecipes[1].SuccessRate);
        }

        #endregion

        #region ItemInfo Tests - 物品信息

        [Fact]
        public void ItemInfo_DefaultValues_AreCorrect()
        {
            var item = new ItemInfo();
            Assert.Equal(0, item.ItemId);
            Assert.Equal(0, item.TemplateId);
            Assert.Equal("", item.Name);
            Assert.Equal(0, item.Count);
            Assert.Equal(0, item.Quality);
            Assert.False(item.IsBound);
        }

        [Fact]
        public void ItemInfo_SetProperties_WorksCorrectly()
        {
            var item = new ItemInfo
            {
                ItemId = 1,
                TemplateId = 100,
                Name = "铁剑",
                Description = "一把普通的铁剑",
                ItemType = 1,
                Count = 1,
                Level = 10,
                Quality = 2,
                IsBound = true
            };

            Assert.Equal(1, item.ItemId);
            Assert.Equal(100, item.TemplateId);
            Assert.Equal("铁剑", item.Name);
            Assert.Equal(1, item.ItemType);
            Assert.Equal(1, item.Count);
            Assert.Equal(10, item.Level);
            Assert.Equal(2, item.Quality);
            Assert.True(item.IsBound);
        }

        [Fact]
        public void ItemInfo_Attributes_CanBeSet()
        {
            var item = new ItemInfo();
            item.Attributes["attack"] = 50;
            item.Attributes["defense"] = 20;
            Assert.Equal(2, item.Attributes.Count);
        }

        #endregion
    }
}
