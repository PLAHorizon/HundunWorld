using Horizon.Orleans.Grains;
using Horizon.Game.Message.Network;
using Horizon.Game.Message.Enums;

namespace Horizon.Game.Gateway.Tests
{
    /// <summary>
    /// CombatCalculator 和 CombatInfo 单元测试
    /// 测试五行相克计算、防御减免、伤害计算、暴击、复活等核心战斗逻辑
    /// </summary>
    public class CombatCalculatorTests
    {
        #region GetWuxingMultiplier Tests - 五行相克乘数

        [Theory]
        [InlineData(1, 2, 1.25f)] // 金克木
        [InlineData(2, 5, 1.25f)] // 木克土
        [InlineData(5, 3, 1.25f)] // 土克水
        [InlineData(3, 4, 1.25f)] // 水克火
        [InlineData(4, 1, 1.25f)] // 火克金
        public void GetWuxingMultiplier_ElementAdvantage_Returns125(int attacker, int defender, float expected)
        {
            var result = CombatCalculator.GetWuxingMultiplier(attacker, defender);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(1, 4, 0.8f)] // 金被火克
        [InlineData(4, 2, 0.8f)] // 火被木克
        [InlineData(2, 3, 0.8f)] // 木被水克
        [InlineData(3, 5, 0.8f)] // 水被土克
        [InlineData(5, 1, 0.8f)] // 土被金克
        public void GetWuxingMultiplier_ElementDisadvantage_Returns08(int attacker, int defender, float expected)
        {
            var result = CombatCalculator.GetWuxingMultiplier(attacker, defender);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(0, 1)] // 攻击者无属性
        [InlineData(1, 0)] // 防御者无属性
        [InlineData(0, 0)] // 双方无属性
        [InlineData(1, 1)] // 同属性
        [InlineData(2, 2)] // 同属性
        public void GetWuxingMultiplier_NeutralOrSame_Returns10(int attacker, int defender)
        {
            var result = CombatCalculator.GetWuxingMultiplier(attacker, defender);
            Assert.Equal(1.0f, result);
        }

        #endregion

        #region CalculateDefenseReduction Tests - 防御减免

        [Fact]
        public void CalculateDefenseReduction_ZeroDefense_ReturnsZero()
        {
            var result = CombatCalculator.CalculateDefenseReduction(0);
            Assert.Equal(0f, result);
        }

        [Fact]
        public void CalculateDefenseReduction_NegativeDefense_ReturnsZero()
        {
            var result = CombatCalculator.CalculateDefenseReduction(-10);
            Assert.Equal(0f, result);
        }

        [Fact]
        public void CalculateDefenseReduction_100Defense_Returns50Percent()
        {
            // 100 / (100 + 100) = 0.5
            var result = CombatCalculator.CalculateDefenseReduction(100);
            Assert.Equal(0.5f, result, 0.001f);
        }

        [Fact]
        public void CalculateDefenseReduction_20Defense_ReturnsCorrectValue()
        {
            // 20 / (20 + 100) = 0.1667
            var result = CombatCalculator.CalculateDefenseReduction(20);
            Assert.Equal(20f / 120f, result, 0.001f);
        }

        [Fact]
        public void CalculateDefenseReduction_HighDefense_ApproachesButNeverReachesOne()
        {
            var result = CombatCalculator.CalculateDefenseReduction(10000);
            Assert.True(result < 1.0f);
            Assert.True(result > 0.99f);
        }

        #endregion

        #region CalculateWuxingDamage Tests - 五行伤害计算

        [Fact]
        public void CalculateWuxingDamage_NoElementNoDefense_ReturnsBaseDamage()
        {
            var result = CombatCalculator.CalculateWuxingDamage(100f, 0, 0, 0);
            Assert.Equal(100f, result, 0.01f);
        }

        [Fact]
        public void CalculateWuxingDamage_WithAdvantage_IncreasedDamage()
        {
            // 金克木, no defense: 100 * 1.25 = 125
            var result = CombatCalculator.CalculateWuxingDamage(100f, 1, 2, 0);
            Assert.Equal(125f, result, 0.01f);
        }

        [Fact]
        public void CalculateWuxingDamage_WithDisadvantage_ReducedDamage()
        {
            // 金被火克, no defense: 100 * 0.8 = 80
            var result = CombatCalculator.CalculateWuxingDamage(100f, 1, 4, 0);
            Assert.Equal(80f, result, 0.01f);
        }

        [Fact]
        public void CalculateWuxingDamage_WithDefense_ReducesDamage()
        {
            // neutral element, 100 defense: 100 * 1.0 * (1 - 0.5) = 50
            var result = CombatCalculator.CalculateWuxingDamage(100f, 0, 0, 100);
            Assert.Equal(50f, result, 0.01f);
        }

        [Fact]
        public void CalculateWuxingDamage_AdvantageWithDefense_CombinedEffect()
        {
            // 金克木 + 100 defense: 100 * 1.25 * (1 - 0.5) = 62.5
            var result = CombatCalculator.CalculateWuxingDamage(100f, 1, 2, 100);
            Assert.Equal(62.5f, result, 0.01f);
        }

        #endregion

        #region CalculateBaseDamage Tests - 基础伤害

        [Fact]
        public void CalculateBaseDamage_ValidInput_ReturnsCorrectDamage()
        {
            // 50 * 0.5 + 30 = 55
            var result = CombatCalculator.CalculateBaseDamage(50f, 30f);
            Assert.Equal(55f, result, 0.01f);
        }

        [Fact]
        public void CalculateBaseDamage_ZeroAttackPower_ReturnsRawDamage()
        {
            var result = CombatCalculator.CalculateBaseDamage(0f, 30f);
            Assert.Equal(30f, result, 0.01f);
        }

        [Fact]
        public void CalculateBaseDamage_ZeroRawDamage_ReturnsHalfAttackPower()
        {
            var result = CombatCalculator.CalculateBaseDamage(100f, 0f);
            Assert.Equal(50f, result, 0.01f);
        }

        #endregion

        #region ApplyCriticalDamage Tests - 暴击伤害

        [Fact]
        public void ApplyCriticalDamage_NotCritical_ReturnsSameDamage()
        {
            var result = CombatCalculator.ApplyCriticalDamage(100f, false);
            Assert.Equal(100f, result);
        }

        [Fact]
        public void ApplyCriticalDamage_Critical_Returns150Percent()
        {
            var result = CombatCalculator.ApplyCriticalDamage(100f, true);
            Assert.Equal(150f, result);
        }

        [Fact]
        public void ApplyCriticalDamage_CustomMultiplier_AppliesCorrectly()
        {
            var result = CombatCalculator.ApplyCriticalDamage(100f, true, 2.0f);
            Assert.Equal(200f, result);
        }

        #endregion

        #region ClampHealth Tests - 生命值钳制

        [Fact]
        public void ClampHealth_NormalDamage_ReducesHealth()
        {
            var result = CombatCalculator.ClampHealth(100f, 30f);
            Assert.Equal(70f, result);
        }

        [Fact]
        public void ClampHealth_LethalDamage_ClampsToZero()
        {
            var result = CombatCalculator.ClampHealth(50f, 999f);
            Assert.Equal(0f, result);
        }

        [Fact]
        public void ClampHealth_ExactKill_ReturnsZero()
        {
            var result = CombatCalculator.ClampHealth(50f, 50f);
            Assert.Equal(0f, result);
        }

        [Fact]
        public void ClampHealth_ZeroDamage_ReturnsSameHealth()
        {
            var result = CombatCalculator.ClampHealth(100f, 0f);
            Assert.Equal(100f, result);
        }

        #endregion

        #region CalculateResurrectHealth Tests - 复活生命值

        [Fact]
        public void CalculateResurrectHealth_FullResurrect_ReturnsMaxHealth()
        {
            var result = CombatCalculator.CalculateResurrectHealth(200f, 1);
            Assert.Equal(200f, result);
        }

        [Fact]
        public void CalculateResurrectHealth_PartialResurrect_ReturnsHalfHealth()
        {
            var result = CombatCalculator.CalculateResurrectHealth(200f, 0);
            Assert.Equal(100f, result);
        }

        [Fact]
        public void CalculateResurrectHealth_OtherType_ReturnsHalfHealth()
        {
            var result = CombatCalculator.CalculateResurrectHealth(200f, 2);
            Assert.Equal(100f, result);
        }

        #endregion

        #region CombatInfo Model Tests - 数据模型

        [Fact]
        public void CombatInfo_HasEnergyProperties()
        {
            var info = new CombatInfo
            {
                Energy = 80,
                MaxEnergy = 100
            };

            Assert.Equal(80, info.Energy);
            Assert.Equal(100, info.MaxEnergy);
        }

        [Fact]
        public void CombatInfo_DefaultEnergyValues_AreZero()
        {
            var info = new CombatInfo();
            Assert.Equal(0, info.Energy);
            Assert.Equal(0, info.MaxEnergy);
        }

        [Fact]
        public void CombatInfo_AllPropertiesSettable()
        {
            var info = new CombatInfo
            {
                CharacterId = 1,
                IsInCombat = true,
                TargetId = 2,
                LastActionTime = DateTime.UtcNow,
                Health = 100,
                MaxHealth = 200,
                AttackPower = 50,
                Defense = 30,
                WuxingElement = 3,
                Energy = 80,
                MaxEnergy = 100
            };

            Assert.Equal(1UL, info.CharacterId);
            Assert.True(info.IsInCombat);
            Assert.Equal(2UL, info.TargetId);
            Assert.Equal(100f, info.Health);
            Assert.Equal(200f, info.MaxHealth);
            Assert.Equal(50f, info.AttackPower);
            Assert.Equal(30f, info.Defense);
            Assert.Equal(3, info.WuxingElement);
            Assert.Equal(80f, info.Energy);
            Assert.Equal(100f, info.MaxEnergy);
        }

        #endregion

        #region CombatState Model Tests

        [Fact]
        public void CombatState_InitializesWithEmptyCollections()
        {
            var state = new CombatState();
            Assert.NotNull(state.CombatParticipants);
            Assert.NotNull(state.ActiveEffects);
            Assert.Empty(state.CombatParticipants);
            Assert.Empty(state.ActiveEffects);
        }

        #endregion

        #region EffectInfo Model Tests

        [Fact]
        public void EffectInfo_AllPropertiesSettable()
        {
            var effect = new EffectInfo
            {
                EffectId = 1,
                EffectName = "燃烧",
                TargetId = 10,
                SourceId = 20,
                Duration = 5.0f,
                RemainingDuration = 3.0f,
                Intensity = 15.0f,
                StackCount = 2,
                Type = EffectType.DamageOverTime
            };

            Assert.Equal(1, effect.EffectId);
            Assert.Equal("燃烧", effect.EffectName);
            Assert.Equal(10UL, effect.TargetId);
            Assert.Equal(20UL, effect.SourceId);
            Assert.Equal(5.0f, effect.Duration);
            Assert.Equal(3.0f, effect.RemainingDuration);
            Assert.Equal(15.0f, effect.Intensity);
            Assert.Equal(2, effect.StackCount);
            Assert.Equal(EffectType.DamageOverTime, effect.Type);
        }

        #endregion

        #region Integration-style Calculation Tests

        [Fact]
        public void FullDamageCalculation_GoldVsWood_WithDefense()
        {
            // Simulate: 金系角色攻击木系角色
            // AttackPower=80, RawDamage=40, DefenderDefense=50
            float baseDamage = CombatCalculator.CalculateBaseDamage(80f, 40f); // 80*0.5 + 40 = 80
            float finalDamage = CombatCalculator.CalculateWuxingDamage(
                baseDamage, 1, 2, 50f); // 金克木: 80 * 1.25 * (1 - 50/150) = 80 * 1.25 * 0.6667 = 66.67

            Assert.True(finalDamage > 0);
            Assert.Equal(80f * 1.25f * (1f - 50f / 150f), finalDamage, 0.01f);
        }

        [Fact]
        public void FullDamageCalculation_NoCritical_ReducesHealth()
        {
            float baseDamage = CombatCalculator.CalculateBaseDamage(50f, 30f); // 55
            float withDefense = CombatCalculator.CalculateWuxingDamage(baseDamage, 0, 0, 20f);
            float withCrit = CombatCalculator.ApplyCriticalDamage(withDefense, false);
            float remainingHealth = CombatCalculator.ClampHealth(100f, withCrit);

            Assert.True(remainingHealth > 0);
            Assert.True(remainingHealth < 100f);
        }

        [Fact]
        public void FullDamageCalculation_WithCritical_MoreDamage()
        {
            float baseDamage = CombatCalculator.CalculateBaseDamage(50f, 30f);
            float withDefense = CombatCalculator.CalculateWuxingDamage(baseDamage, 0, 0, 0f);

            float noCrit = CombatCalculator.ApplyCriticalDamage(withDefense, false);
            float yesCrit = CombatCalculator.ApplyCriticalDamage(withDefense, true);

            Assert.True(yesCrit > noCrit);
            Assert.Equal(noCrit * 1.5f, yesCrit, 0.01f);
        }

        #endregion
    }
}
