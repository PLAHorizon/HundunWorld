using Horizon.Orleans.Grains;
using Horizon.Orleans.Interface;

namespace Horizon.Game.Gateway.Tests
{
    /// <summary>
    /// 五行属性加成、五行协同、能量恢复、GCD、战斗日志、炼丹系统 单元测试
    /// </summary>
    public class WuxingAlchemyCombatLogTests
    {
        #region WuxingAttributeBonus Tests - 五行属性加成

        [Fact]
        public void GetWuxingAttributeBonus_Metal_SetsCritAndPhysical()
        {
            var bonus = CombatCalculator.GetWuxingAttributeBonus(1, 10f); // 金
            Assert.Equal(10f * 0.05f, bonus.CritRateBonus);
            Assert.Equal(10f * 0.15f, bonus.PhysicalDamageBonus);
            Assert.Equal(0f, bonus.HealthRegenRate);
            Assert.Equal(0f, bonus.DodgeRateBonus);
            Assert.Equal(0f, bonus.DefenseBonus);
            Assert.Equal(0f, bonus.ShieldAmount);
            Assert.Equal(0f, bonus.BurnDamagePerTick);
            Assert.Equal(0f, bonus.FreezeChance);
        }

        [Fact]
        public void GetWuxingAttributeBonus_Wood_SetsHealthRegen()
        {
            var bonus = CombatCalculator.GetWuxingAttributeBonus(2, 10f); // 木
            Assert.Equal(0f, bonus.CritRateBonus);
            Assert.Equal(10f * 0.02f, bonus.HealthRegenRate);
            Assert.Equal(0f, bonus.DodgeRateBonus);
            Assert.Equal(0f, bonus.BurnDamagePerTick);
        }

        [Fact]
        public void GetWuxingAttributeBonus_Water_SetsDodgeAndFreeze()
        {
            var bonus = CombatCalculator.GetWuxingAttributeBonus(3, 10f); // 水
            Assert.Equal(10f * 0.04f, bonus.DodgeRateBonus);
            Assert.Equal(10f * 0.03f, bonus.FreezeChance);
            Assert.Equal(0f, bonus.CritRateBonus);
            Assert.Equal(0f, bonus.BurnDamagePerTick);
        }

        [Fact]
        public void GetWuxingAttributeBonus_Fire_SetsBurnDamage()
        {
            var bonus = CombatCalculator.GetWuxingAttributeBonus(4, 10f); // 火
            Assert.Equal(10f * 0.10f, bonus.BurnDamagePerTick);
            Assert.Equal(0f, bonus.CritRateBonus);
            Assert.Equal(0f, bonus.DodgeRateBonus);
        }

        [Fact]
        public void GetWuxingAttributeBonus_Earth_SetsDefenseAndShield()
        {
            var bonus = CombatCalculator.GetWuxingAttributeBonus(5, 10f); // 土
            Assert.Equal(10f * 0.12f, bonus.DefenseBonus);
            Assert.Equal(10f * 0.08f, bonus.ShieldAmount);
            Assert.Equal(0f, bonus.CritRateBonus);
            Assert.Equal(0f, bonus.BurnDamagePerTick);
        }

        [Fact]
        public void GetWuxingAttributeBonus_NoElement_ReturnsZeros()
        {
            var bonus = CombatCalculator.GetWuxingAttributeBonus(0, 10f);
            Assert.Equal(0f, bonus.CritRateBonus);
            Assert.Equal(0f, bonus.PhysicalDamageBonus);
            Assert.Equal(0f, bonus.HealthRegenRate);
            Assert.Equal(0f, bonus.DodgeRateBonus);
            Assert.Equal(0f, bonus.DefenseBonus);
            Assert.Equal(0f, bonus.ShieldAmount);
            Assert.Equal(0f, bonus.BurnDamagePerTick);
            Assert.Equal(0f, bonus.FreezeChance);
        }

        [Fact]
        public void GetWuxingAttributeBonus_InvalidElement_ReturnsZeros()
        {
            var bonus = CombatCalculator.GetWuxingAttributeBonus(99, 10f);
            Assert.Equal(0f, bonus.CritRateBonus);
            Assert.Equal(0f, bonus.PhysicalDamageBonus);
        }

        [Fact]
        public void GetWuxingAttributeBonus_ZeroPower_ReturnsZeros()
        {
            var bonus = CombatCalculator.GetWuxingAttributeBonus(1, 0f);
            Assert.Equal(0f, bonus.CritRateBonus);
            Assert.Equal(0f, bonus.PhysicalDamageBonus);
        }

        [Fact]
        public void GetWuxingAttributeBonus_HighPower_ScalesLinearly()
        {
            var bonus = CombatCalculator.GetWuxingAttributeBonus(1, 100f);
            Assert.Equal(100f * 0.05f, bonus.CritRateBonus);
            Assert.Equal(100f * 0.15f, bonus.PhysicalDamageBonus);
        }

        #endregion

        #region WuxingSynergy Tests - 五行相生协同

        [Theory]
        [InlineData(1, 3)] // 金生水
        [InlineData(3, 2)] // 水生木
        [InlineData(2, 4)] // 木生火
        [InlineData(4, 5)] // 火生土
        [InlineData(5, 1)] // 土生金
        public void GetWuxingSynergyMultiplier_SynergyPairs_Returns115(int e1, int e2)
        {
            float result = CombatCalculator.GetWuxingSynergyMultiplier(e1, e2);
            Assert.Equal(1.15f, result);
        }

        [Theory]
        [InlineData(3, 1)] // 水 <- 金
        [InlineData(2, 3)] // 木 <- 水
        [InlineData(4, 2)] // 火 <- 木
        [InlineData(5, 4)] // 土 <- 火
        [InlineData(1, 5)] // 金 <- 土
        public void GetWuxingSynergyMultiplier_ReverseSynergyPairs_Returns115(int e1, int e2)
        {
            float result = CombatCalculator.GetWuxingSynergyMultiplier(e1, e2);
            Assert.Equal(1.15f, result);
        }

        [Theory]
        [InlineData(1)] // 金金
        [InlineData(2)] // 木木
        [InlineData(3)] // 水水
        [InlineData(4)] // 火火
        [InlineData(5)] // 土土
        public void GetWuxingSynergyMultiplier_SameElement_Returns110(int element)
        {
            float result = CombatCalculator.GetWuxingSynergyMultiplier(element, element);
            Assert.Equal(1.10f, result);
        }

        [Fact]
        public void GetWuxingSynergyMultiplier_NoRelation_Returns100()
        {
            float result = CombatCalculator.GetWuxingSynergyMultiplier(1, 2); // 金 和 木 (相克不是相生)
            Assert.Equal(1.0f, result);
        }

        [Fact]
        public void GetWuxingSynergyMultiplier_ZeroElement_Returns100()
        {
            float result = CombatCalculator.GetWuxingSynergyMultiplier(0, 0);
            Assert.Equal(1.0f, result);
        }

        [Fact]
        public void GetWuxingSynergyMultiplier_OneZero_Returns100()
        {
            float result = CombatCalculator.GetWuxingSynergyMultiplier(1, 0);
            Assert.Equal(1.0f, result);
        }

        #endregion

        #region EnergyRecovery Tests - 能量恢复

        [Fact]
        public void CalculateEnergyRecovery_DefaultRate_RecoversCorrectly()
        {
            float result = CombatCalculator.CalculateEnergyRecovery(100f, 50f);
            // 100 * 0.02 = 2, so 50 + 2 = 52
            Assert.Equal(52f, result);
        }

        [Fact]
        public void CalculateEnergyRecovery_CustomRate_RecoversCorrectly()
        {
            float result = CombatCalculator.CalculateEnergyRecovery(100f, 50f, 0.10f);
            // 100 * 0.10 = 10, so 50 + 10 = 60
            Assert.Equal(60f, result);
        }

        [Fact]
        public void CalculateEnergyRecovery_FullEnergy_CapsAtMax()
        {
            float result = CombatCalculator.CalculateEnergyRecovery(100f, 99f, 0.10f);
            // 100 * 0.10 = 10, so 99 + 10 = 109, capped at 100
            Assert.Equal(100f, result);
        }

        [Fact]
        public void CalculateEnergyRecovery_AlreadyFull_StaysAtMax()
        {
            float result = CombatCalculator.CalculateEnergyRecovery(100f, 100f);
            Assert.Equal(100f, result);
        }

        [Fact]
        public void CalculateEnergyRecovery_ZeroEnergy_RecoversFromZero()
        {
            float result = CombatCalculator.CalculateEnergyRecovery(100f, 0f);
            Assert.Equal(2f, result); // 100 * 0.02 = 2
        }

        [Fact]
        public void CalculateEnergyRecovery_ZeroMaxEnergy_StaysZero()
        {
            float result = CombatCalculator.CalculateEnergyRecovery(0f, 0f);
            Assert.Equal(0f, result);
        }

        [Fact]
        public void CalculateEnergyRecovery_ZeroRate_NoRecovery()
        {
            float result = CombatCalculator.CalculateEnergyRecovery(100f, 50f, 0f);
            Assert.Equal(50f, result);
        }

        #endregion

        #region GCD Tests - 全局冷却

        [Fact]
        public void IsGlobalCooldownReady_ZeroGCD_ReturnsTrue()
        {
            var result = CombatCalculator.IsGlobalCooldownReady(DateTime.UtcNow, 0);
            Assert.True(result);
        }

        [Fact]
        public void IsGlobalCooldownReady_NegativeGCD_ReturnsTrue()
        {
            var result = CombatCalculator.IsGlobalCooldownReady(DateTime.UtcNow, -100);
            Assert.True(result);
        }

        [Fact]
        public void IsGlobalCooldownReady_ExpiredGCD_ReturnsTrue()
        {
            var lastAction = DateTime.UtcNow.AddSeconds(-2);
            var result = CombatCalculator.IsGlobalCooldownReady(lastAction, 1000); // 1 sec GCD, 2 sec ago
            Assert.True(result);
        }

        [Fact]
        public void IsGlobalCooldownReady_ActiveGCD_ReturnsFalse()
        {
            var lastAction = DateTime.UtcNow.AddMilliseconds(-200);
            var result = CombatCalculator.IsGlobalCooldownReady(lastAction, 1000); // 1 sec GCD, only 0.2 sec ago
            Assert.False(result);
        }

        [Fact]
        public void IsGlobalCooldownReady_DefaultGCD_Is1000ms()
        {
            var lastAction = DateTime.UtcNow.AddMilliseconds(-500);
            var result = CombatCalculator.IsGlobalCooldownReady(lastAction); // default 1000ms
            Assert.False(result);
        }

        [Fact]
        public void IsGlobalCooldownReady_DefaultGCD_Expired_ReturnsTrue()
        {
            var lastAction = DateTime.UtcNow.AddSeconds(-2);
            var result = CombatCalculator.IsGlobalCooldownReady(lastAction); // default 1000ms
            Assert.True(result);
        }

        #endregion

        #region CombatLogEntry Tests - 战斗日志条目

        [Fact]
        public void CombatLogEntry_DefaultValues_AreCorrect()
        {
            var entry = new CombatLogEntry();
            Assert.Equal(default(DateTime), entry.Timestamp);
            Assert.Equal(0UL, entry.AttackerId);
            Assert.Equal(0UL, entry.DefenderId);
            Assert.Equal(0f, entry.DamageDealt);
            Assert.Equal(0, entry.SkillId);
            Assert.Equal(0, entry.ElementType);
            Assert.False(entry.IsCritical);
            Assert.False(entry.IsDodged);
            Assert.False(entry.IsBlocked);
            Assert.Equal(CombatLogType.Attack, entry.LogType);
        }

        [Fact]
        public void CombatLogEntry_SetAttackLog_WorksCorrectly()
        {
            var now = DateTime.UtcNow;
            var entry = new CombatLogEntry
            {
                Timestamp = now,
                AttackerId = 100,
                DefenderId = 200,
                DamageDealt = 50.5f,
                SkillId = 0,
                ElementType = 1,
                IsCritical = true,
                IsDodged = false,
                IsBlocked = false,
                LogType = CombatLogType.Attack
            };

            Assert.Equal(now, entry.Timestamp);
            Assert.Equal(100UL, entry.AttackerId);
            Assert.Equal(200UL, entry.DefenderId);
            Assert.Equal(50.5f, entry.DamageDealt);
            Assert.Equal(1, entry.ElementType);
            Assert.True(entry.IsCritical);
            Assert.Equal(CombatLogType.Attack, entry.LogType);
        }

        [Fact]
        public void CombatLogEntry_SetSkillCastLog_WorksCorrectly()
        {
            var entry = new CombatLogEntry
            {
                Timestamp = DateTime.UtcNow,
                AttackerId = 100,
                SkillId = 5,
                LogType = CombatLogType.SkillCast
            };

            Assert.Equal(CombatLogType.SkillCast, entry.LogType);
            Assert.Equal(5, entry.SkillId);
        }

        [Fact]
        public void CombatLogEntry_DeathLog_WorksCorrectly()
        {
            var entry = new CombatLogEntry
            {
                Timestamp = DateTime.UtcNow,
                AttackerId = 100,
                DefenderId = 200,
                LogType = CombatLogType.Death
            };

            Assert.Equal(CombatLogType.Death, entry.LogType);
        }

        [Fact]
        public void CombatLogEntry_ResurrectLog_WorksCorrectly()
        {
            var entry = new CombatLogEntry
            {
                LogType = CombatLogType.Resurrect
            };

            Assert.Equal(CombatLogType.Resurrect, entry.LogType);
        }

        [Fact]
        public void CombatLogEntry_EffectAppliedLog_WorksCorrectly()
        {
            var entry = new CombatLogEntry
            {
                LogType = CombatLogType.EffectApplied
            };

            Assert.Equal(CombatLogType.EffectApplied, entry.LogType);
        }

        #endregion

        #region CombatLogType Tests - 战斗日志类型枚举

        [Fact]
        public void CombatLogType_Values_AreCorrect()
        {
            Assert.Equal(0, (int)CombatLogType.Attack);
            Assert.Equal(1, (int)CombatLogType.SkillCast);
            Assert.Equal(2, (int)CombatLogType.Death);
            Assert.Equal(3, (int)CombatLogType.Resurrect);
            Assert.Equal(4, (int)CombatLogType.EffectApplied);
        }

        #endregion

        #region CombatState CombatLog Tests - 战斗状态日志

        [Fact]
        public void CombatState_CombatLog_DefaultIsEmpty()
        {
            var state = new CombatState();
            Assert.NotNull(state.CombatLog);
            Assert.Empty(state.CombatLog);
        }

        [Fact]
        public void CombatState_CombatLog_CanAddEntries()
        {
            var state = new CombatState();
            state.CombatLog.Add(new CombatLogEntry
            {
                Timestamp = DateTime.UtcNow,
                AttackerId = 1,
                DefenderId = 2,
                DamageDealt = 100f,
                LogType = CombatLogType.Attack
            });
            Assert.Single(state.CombatLog);
        }

        [Fact]
        public void CombatState_CombatLog_MultipleEntries_TrackedCorrectly()
        {
            var state = new CombatState();
            for (int i = 0; i < 5; i++)
            {
                state.CombatLog.Add(new CombatLogEntry
                {
                    Timestamp = DateTime.UtcNow,
                    AttackerId = (ulong)i,
                    LogType = CombatLogType.Attack
                });
            }
            Assert.Equal(5, state.CombatLog.Count);
        }

        #endregion

        #region AlchemyRecipe Tests - 炼丹配方

        [Fact]
        public void AlchemyRecipe_DefaultValues_AreCorrect()
        {
            var recipe = new AlchemyRecipe();
            Assert.Equal(0, recipe.RecipeId);
            Assert.Equal("", recipe.Name);
            Assert.Equal(0, recipe.RequiredPrimaryElement);
            Assert.Equal(0, recipe.RequiredSecondaryElement);
            Assert.Equal(0L, recipe.OutputItemId);
            Assert.Equal(0f, recipe.BaseProficiencyGain);
            Assert.Equal(0f, recipe.MinProficiency);
        }

        [Fact]
        public void AlchemyRecipe_SetProperties_WorksCorrectly()
        {
            var recipe = new AlchemyRecipe
            {
                RecipeId = 1,
                Name = "回春丹",
                RequiredPrimaryElement = 2,  // 木
                RequiredSecondaryElement = 3, // 水
                OutputItemId = 100,
                BaseProficiencyGain = 1.5f,
                MinProficiency = 10f
            };

            Assert.Equal(1, recipe.RecipeId);
            Assert.Equal("回春丹", recipe.Name);
            Assert.Equal(2, recipe.RequiredPrimaryElement);
            Assert.Equal(3, recipe.RequiredSecondaryElement);
            Assert.Equal(100L, recipe.OutputItemId);
            Assert.Equal(1.5f, recipe.BaseProficiencyGain);
            Assert.Equal(10f, recipe.MinProficiency);
        }

        #endregion

        #region AlchemyResult Tests - 炼丹结果

        [Fact]
        public void AlchemyResult_DefaultValues_AreCorrect()
        {
            var result = new AlchemyResult();
            Assert.False(result.Success);
            Assert.Equal(0, result.RecipeId);
            Assert.Equal("", result.Message);
            Assert.Equal(0L, result.OutputItemId);
            Assert.Equal(0, result.Quality);
            Assert.Equal(0f, result.ProficiencyGain);
            Assert.Equal(0f, result.ElementalHarmony);
        }

        [Fact]
        public void AlchemyResult_SuccessfulAlchemy_SetsProperties()
        {
            var result = new AlchemyResult
            {
                Success = true,
                RecipeId = 1,
                Message = "炼丹成功（品质：史诗）",
                OutputItemId = 1000,
                Quality = 3,
                ProficiencyGain = 1.5f,
                ElementalHarmony = 1.15f
            };

            Assert.True(result.Success);
            Assert.Equal(1, result.RecipeId);
            Assert.Equal("炼丹成功（品质：史诗）", result.Message);
            Assert.Equal(1000L, result.OutputItemId);
            Assert.Equal(3, result.Quality);
            Assert.Equal(1.5f, result.ProficiencyGain);
            Assert.Equal(1.15f, result.ElementalHarmony);
        }

        [Fact]
        public void AlchemyResult_FailedAlchemy_SetsProperties()
        {
            var result = new AlchemyResult
            {
                Success = false,
                RecipeId = 1,
                Message = "炼丹失败",
                Quality = 0,
                ProficiencyGain = 0.25f
            };

            Assert.False(result.Success);
            Assert.Equal(0, result.Quality);
            Assert.True(result.ProficiencyGain > 0);
        }

        #endregion

        #region AlchemyHistoryEntry Tests - 炼丹历史

        [Fact]
        public void AlchemyHistoryEntry_DefaultValues_AreCorrect()
        {
            var entry = new AlchemyHistoryEntry();
            Assert.Equal(0, entry.RecipeId);
            Assert.False(entry.Success);
            Assert.Equal(0L, entry.OutputItemId);
            Assert.Equal(0, entry.Quality);
            Assert.Equal(0, entry.PrimaryElement);
            Assert.Equal(0, entry.SecondaryElement);
        }

        [Fact]
        public void AlchemyHistoryEntry_SetProperties_WorksCorrectly()
        {
            var now = DateTime.UtcNow;
            var entry = new AlchemyHistoryEntry
            {
                RecipeId = 1,
                Success = true,
                Timestamp = now,
                OutputItemId = 1000,
                Quality = 2,
                PrimaryElement = 1,
                SecondaryElement = 3
            };

            Assert.Equal(1, entry.RecipeId);
            Assert.True(entry.Success);
            Assert.Equal(now, entry.Timestamp);
            Assert.Equal(1000L, entry.OutputItemId);
            Assert.Equal(2, entry.Quality);
            Assert.Equal(1, entry.PrimaryElement);
            Assert.Equal(3, entry.SecondaryElement);
        }

        #endregion

        #region AlchemyState Tests - 炼丹状态

        [Fact]
        public void AlchemyState_DefaultValues_AreCorrect()
        {
            var state = new AlchemyState();
            Assert.NotNull(state.LearnedRecipes);
            Assert.Empty(state.LearnedRecipes);
            Assert.Equal(0f, state.Proficiency);
            Assert.NotNull(state.AlchemyHistory);
            Assert.Empty(state.AlchemyHistory);
        }

        [Fact]
        public void AlchemyState_LearnRecipe_AddsToCollection()
        {
            var state = new AlchemyState();
            state.LearnedRecipes[1] = new AlchemyRecipe { RecipeId = 1, Name = "回春丹" };
            Assert.Single(state.LearnedRecipes);
            Assert.Equal("回春丹", state.LearnedRecipes[1].Name);
        }

        [Fact]
        public void AlchemyState_ProficiencyGain_Accumulates()
        {
            var state = new AlchemyState();
            state.Proficiency += 1.5f;
            state.Proficiency += 2.0f;
            Assert.Equal(3.5f, state.Proficiency);
        }

        [Fact]
        public void AlchemyState_AddHistory_TracksEntries()
        {
            var state = new AlchemyState();
            state.AlchemyHistory.Add(new AlchemyHistoryEntry
            {
                RecipeId = 1,
                Success = true,
                PrimaryElement = 1,
                SecondaryElement = 3
            });
            Assert.Single(state.AlchemyHistory);
        }

        [Fact]
        public void AlchemyState_MultipleRecipes_TrackedIndependently()
        {
            var state = new AlchemyState();
            state.LearnedRecipes[1] = new AlchemyRecipe { RecipeId = 1, Name = "回春丹" };
            state.LearnedRecipes[2] = new AlchemyRecipe { RecipeId = 2, Name = "筑基丹" };
            state.LearnedRecipes[3] = new AlchemyRecipe { RecipeId = 3, Name = "破境丹" };
            Assert.Equal(3, state.LearnedRecipes.Count);
        }

        [Fact]
        public void AlchemyState_DuplicateRecipe_Overwrites()
        {
            var state = new AlchemyState();
            state.LearnedRecipes[1] = new AlchemyRecipe { RecipeId = 1, Name = "回春丹" };
            state.LearnedRecipes[1] = new AlchemyRecipe { RecipeId = 1, Name = "回春丹改良版" };
            Assert.Single(state.LearnedRecipes);
            Assert.Equal("回春丹改良版", state.LearnedRecipes[1].Name);
        }

        #endregion

        #region WuxingAttributeBonus Class Tests

        [Fact]
        public void WuxingAttributeBonus_DefaultValues_AllZero()
        {
            var bonus = new WuxingAttributeBonus();
            Assert.Equal(0f, bonus.CritRateBonus);
            Assert.Equal(0f, bonus.PhysicalDamageBonus);
            Assert.Equal(0f, bonus.HealthRegenRate);
            Assert.Equal(0f, bonus.DodgeRateBonus);
            Assert.Equal(0f, bonus.DefenseBonus);
            Assert.Equal(0f, bonus.ShieldAmount);
            Assert.Equal(0f, bonus.BurnDamagePerTick);
            Assert.Equal(0f, bonus.FreezeChance);
        }

        [Fact]
        public void WuxingAttributeBonus_SetAllProperties_WorksCorrectly()
        {
            var bonus = new WuxingAttributeBonus
            {
                CritRateBonus = 0.05f,
                PhysicalDamageBonus = 0.15f,
                HealthRegenRate = 0.02f,
                DodgeRateBonus = 0.04f,
                DefenseBonus = 0.12f,
                ShieldAmount = 0.08f,
                BurnDamagePerTick = 0.10f,
                FreezeChance = 0.03f
            };

            Assert.Equal(0.05f, bonus.CritRateBonus);
            Assert.Equal(0.15f, bonus.PhysicalDamageBonus);
            Assert.Equal(0.02f, bonus.HealthRegenRate);
            Assert.Equal(0.04f, bonus.DodgeRateBonus);
            Assert.Equal(0.12f, bonus.DefenseBonus);
            Assert.Equal(0.08f, bonus.ShieldAmount);
            Assert.Equal(0.10f, bonus.BurnDamagePerTick);
            Assert.Equal(0.03f, bonus.FreezeChance);
        }

        #endregion
    }
}
