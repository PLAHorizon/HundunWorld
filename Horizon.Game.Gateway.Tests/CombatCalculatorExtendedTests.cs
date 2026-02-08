using Horizon.Orleans.Grains;
using Horizon.Game.Message.Network;

namespace Horizon.Game.Gateway.Tests
{
    /// <summary>
    /// CombatCalculator扩展功能测试
    /// 测试闪避系统、格挡系统、技能冷却管理
    /// </summary>
    public class CombatCalculatorExtendedTests
    {
        #region RollDodge Tests - 闪避判定

        [Fact]
        public void RollDodge_ZeroDodgeRate_ReturnsFalse()
        {
            var result = CombatCalculator.RollDodge(0f);
            Assert.False(result);
        }

        [Fact]
        public void RollDodge_NegativeDodgeRate_ReturnsFalse()
        {
            var result = CombatCalculator.RollDodge(-0.5f);
            Assert.False(result);
        }

        [Fact]
        public void RollDodge_FullDodgeRate_ReturnsTrue()
        {
            var result = CombatCalculator.RollDodge(1.0f);
            Assert.True(result);
        }

        [Fact]
        public void RollDodge_OverOneDodgeRate_ReturnsTrue()
        {
            var result = CombatCalculator.RollDodge(1.5f);
            Assert.True(result);
        }

        [Fact]
        public void RollDodge_HalfRate_ReturnsVariedResults()
        {
            // 以50%概率测试，多次测试确保不是总是一样
            int dodgeCount = 0;
            int iterations = 1000;
            for (int i = 0; i < iterations; i++)
            {
                if (CombatCalculator.RollDodge(0.5f))
                    dodgeCount++;
            }
            // 应该在300-700之间（非常宽松的范围）
            Assert.InRange(dodgeCount, 300, 700);
        }

        #endregion

        #region ApplyBlock Tests - 格挡系统

        [Fact]
        public void ApplyBlock_ZeroBlockRate_NeverBlocks()
        {
            var (damage, isBlocked) = CombatCalculator.ApplyBlock(100f, 0f);
            Assert.Equal(100f, damage);
            Assert.False(isBlocked);
        }

        [Fact]
        public void ApplyBlock_NegativeBlockRate_NeverBlocks()
        {
            var (damage, isBlocked) = CombatCalculator.ApplyBlock(100f, -0.5f);
            Assert.Equal(100f, damage);
            Assert.False(isBlocked);
        }

        [Fact]
        public void ApplyBlock_FullBlockRate_AlwaysBlocks()
        {
            var (damage, isBlocked) = CombatCalculator.ApplyBlock(100f, 1.0f);
            Assert.Equal(50f, damage); // 默认50%减免
            Assert.True(isBlocked);
        }

        [Fact]
        public void ApplyBlock_FullBlockRate_CustomReduction()
        {
            var (damage, isBlocked) = CombatCalculator.ApplyBlock(100f, 1.0f, 0.3f);
            Assert.Equal(70f, damage); // 30%减免
            Assert.True(isBlocked);
        }

        [Fact]
        public void ApplyBlock_FullBlockRate_FullReduction()
        {
            var (damage, isBlocked) = CombatCalculator.ApplyBlock(100f, 1.0f, 1.0f);
            Assert.Equal(0f, damage); // 100%减免
            Assert.True(isBlocked);
        }

        [Fact]
        public void ApplyBlock_FullBlockRate_ZeroReduction()
        {
            var (damage, isBlocked) = CombatCalculator.ApplyBlock(100f, 1.0f, 0f);
            Assert.Equal(100f, damage); // 0%减免
            Assert.True(isBlocked);
        }

        [Fact]
        public void ApplyBlock_ZeroDamage_Returns_Zero()
        {
            var (damage, isBlocked) = CombatCalculator.ApplyBlock(0f, 1.0f);
            Assert.Equal(0f, damage);
            Assert.True(isBlocked);
        }

        #endregion

        #region IsSkillReady Tests - 技能冷却检测

        [Fact]
        public void IsSkillReady_ZeroCooldown_ReturnsTrue()
        {
            var result = CombatCalculator.IsSkillReady(DateTime.UtcNow, 0);
            Assert.True(result);
        }

        [Fact]
        public void IsSkillReady_NegativeCooldown_ReturnsTrue()
        {
            var result = CombatCalculator.IsSkillReady(DateTime.UtcNow, -100);
            Assert.True(result);
        }

        [Fact]
        public void IsSkillReady_CooldownExpired_ReturnsTrue()
        {
            var lastCast = DateTime.UtcNow.AddSeconds(-5);
            var result = CombatCalculator.IsSkillReady(lastCast, 3000); // 3秒冷却，已过5秒
            Assert.True(result);
        }

        [Fact]
        public void IsSkillReady_StillOnCooldown_ReturnsFalse()
        {
            var lastCast = DateTime.UtcNow.AddMilliseconds(-500);
            var result = CombatCalculator.IsSkillReady(lastCast, 3000); // 3秒冷却，只过了0.5秒
            Assert.False(result);
        }

        #endregion

        #region GetRemainingCooldown Tests - 剩余冷却时间

        [Fact]
        public void GetRemainingCooldown_ZeroCooldown_ReturnsZero()
        {
            var result = CombatCalculator.GetRemainingCooldown(DateTime.UtcNow, 0);
            Assert.Equal(0f, result);
        }

        [Fact]
        public void GetRemainingCooldown_NegativeCooldown_ReturnsZero()
        {
            var result = CombatCalculator.GetRemainingCooldown(DateTime.UtcNow, -100);
            Assert.Equal(0f, result);
        }

        [Fact]
        public void GetRemainingCooldown_CooldownExpired_ReturnsZero()
        {
            var lastCast = DateTime.UtcNow.AddSeconds(-5);
            var result = CombatCalculator.GetRemainingCooldown(lastCast, 3000); // 3秒冷却，已过5秒
            Assert.Equal(0f, result);
        }

        [Fact]
        public void GetRemainingCooldown_StillOnCooldown_ReturnsPositive()
        {
            var lastCast = DateTime.UtcNow.AddMilliseconds(-500);
            var result = CombatCalculator.GetRemainingCooldown(lastCast, 3000); // 3秒冷却
            Assert.True(result > 0f);
            Assert.True(result <= 3.0f);
        }

        #endregion

        #region CombatInfo Extended Properties Tests

        [Fact]
        public void CombatInfo_DefaultCritRate_Is01()
        {
            var info = new CombatInfo();
            Assert.Equal(0.1f, info.CritRate);
        }

        [Fact]
        public void CombatInfo_DefaultCritDamageMultiplier_Is15()
        {
            var info = new CombatInfo();
            Assert.Equal(1.5f, info.CritDamageMultiplier);
        }

        [Fact]
        public void CombatInfo_DodgeRate_CanBeSet()
        {
            var info = new CombatInfo { DodgeRate = 0.2f };
            Assert.Equal(0.2f, info.DodgeRate);
        }

        [Fact]
        public void CombatInfo_BlockRate_CanBeSet()
        {
            var info = new CombatInfo { BlockRate = 0.15f };
            Assert.Equal(0.15f, info.BlockRate);
        }

        [Fact]
        public void CombatInfo_SkillCooldowns_InitializedEmpty()
        {
            var info = new CombatInfo();
            Assert.NotNull(info.SkillCooldowns);
            Assert.Empty(info.SkillCooldowns);
        }

        [Fact]
        public void CombatInfo_SkillCooldowns_CanTrackMultipleSkills()
        {
            var info = new CombatInfo();
            info.SkillCooldowns[1] = DateTime.UtcNow;
            info.SkillCooldowns[2] = DateTime.UtcNow.AddSeconds(-5);
            Assert.Equal(2, info.SkillCooldowns.Count);
        }

        #endregion
    }
}
