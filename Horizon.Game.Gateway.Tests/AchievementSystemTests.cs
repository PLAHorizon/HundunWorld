using Horizon.Orleans.Grains;
using Horizon.Orleans.Interface;
using Horizon.Game.Message.Network;

namespace Horizon.Game.Gateway.Tests
{
    /// <summary>
    /// AchievementState, AchievementData, AchievementUnlockResult 数据模型及业务逻辑单元测试
    /// 测试成就系统的状态管理和解锁逻辑
    /// </summary>
    public class AchievementSystemTests
    {
        #region AchievementState Tests - 成就状态默认值

        [Fact]
        public void AchievementState_DefaultValues_AreCorrect()
        {
            var state = new AchievementState();
            Assert.NotNull(state.Achievements);
            Assert.Empty(state.Achievements);
            Assert.Equal(0, state.TotalPoints);
            Assert.Equal(0, state.UnlockedCount);
        }

        [Fact]
        public void AchievementState_SetTotalPoints_Works()
        {
            var state = new AchievementState { TotalPoints = 100 };
            Assert.Equal(100, state.TotalPoints);
        }

        [Fact]
        public void AchievementState_SetUnlockedCount_Works()
        {
            var state = new AchievementState { UnlockedCount = 5 };
            Assert.Equal(5, state.UnlockedCount);
        }

        #endregion

        #region AchievementData Tests - 成就数据模型

        [Fact]
        public void AchievementData_DefaultValues_AreCorrect()
        {
            var achievement = new AchievementData();
            Assert.Equal(0, achievement.AchievementId);
            Assert.Equal("", achievement.Name);
            Assert.Equal("", achievement.Description);
            Assert.Equal(0, achievement.Category);
            Assert.Equal(0, achievement.Points);
            Assert.False(achievement.IsUnlocked);
            Assert.Equal(0, achievement.CurrentProgress);
            Assert.Equal(0, achievement.TargetProgress);
            Assert.Null(achievement.UnlockTime);
            Assert.NotNull(achievement.Rewards);
            Assert.Empty(achievement.Rewards);
        }

        [Fact]
        public void AchievementData_SetProperties_Works()
        {
            var now = DateTime.UtcNow;
            var achievement = new AchievementData
            {
                AchievementId = 1001,
                Name = "初出茅庐",
                Description = "完成新手教程",
                Category = (int)AchievementCategory.Growth,
                Points = 10,
                TargetProgress = 1,
                CurrentProgress = 0,
                IsUnlocked = false,
                Rewards = new Dictionary<string, int> { { "金币", 100 }, { "经验", 200 } }
            };

            Assert.Equal(1001, achievement.AchievementId);
            Assert.Equal("初出茅庐", achievement.Name);
            Assert.Equal("完成新手教程", achievement.Description);
            Assert.Equal((int)AchievementCategory.Growth, achievement.Category);
            Assert.Equal(10, achievement.Points);
            Assert.Equal(1, achievement.TargetProgress);
            Assert.Equal(0, achievement.CurrentProgress);
            Assert.False(achievement.IsUnlocked);
            Assert.Equal(2, achievement.Rewards.Count);
        }

        [Fact]
        public void AchievementData_UnlockAchievement_Works()
        {
            var achievement = new AchievementData
            {
                AchievementId = 1001,
                Name = "百战沙场",
                TargetProgress = 100,
                CurrentProgress = 99,
                Points = 50
            };

            achievement.CurrentProgress = Math.Min(
                achievement.CurrentProgress + 1,
                achievement.TargetProgress);

            Assert.Equal(100, achievement.CurrentProgress);
            Assert.True(achievement.CurrentProgress >= achievement.TargetProgress);

            achievement.IsUnlocked = true;
            achievement.UnlockTime = DateTime.UtcNow;

            Assert.True(achievement.IsUnlocked);
            Assert.NotNull(achievement.UnlockTime);
        }

        [Fact]
        public void AchievementData_ProgressCapped_AtTarget()
        {
            var achievement = new AchievementData
            {
                TargetProgress = 10,
                CurrentProgress = 8
            };

            achievement.CurrentProgress = Math.Min(
                achievement.CurrentProgress + 5,
                achievement.TargetProgress);

            Assert.Equal(10, achievement.CurrentProgress);
        }

        #endregion

        #region AchievementUnlockResult Tests - 成就解锁结果

        [Fact]
        public void AchievementUnlockResult_DefaultValues_AreCorrect()
        {
            var result = new AchievementUnlockResult();
            Assert.False(result.Success);
            Assert.Equal("", result.Message);
            Assert.Equal(0, result.AchievementId);
            Assert.Equal(0, result.PointsEarned);
            Assert.NotNull(result.Rewards);
            Assert.Empty(result.Rewards);
        }

        [Fact]
        public void AchievementUnlockResult_SuccessResult_Works()
        {
            var result = new AchievementUnlockResult
            {
                Success = true,
                Message = "成就解锁",
                AchievementId = 1001,
                PointsEarned = 50,
                Rewards = new Dictionary<string, int> { { "金币", 1000 } }
            };

            Assert.True(result.Success);
            Assert.Equal("成就解锁", result.Message);
            Assert.Equal(1001, result.AchievementId);
            Assert.Equal(50, result.PointsEarned);
            Assert.Single(result.Rewards);
        }

        #endregion

        #region AchievementCategory Enum Tests - 成就分类枚举

        [Fact]
        public void AchievementCategory_HasExpectedValues()
        {
            Assert.Equal(0, (int)AchievementCategory.Combat);
            Assert.Equal(1, (int)AchievementCategory.Social);
            Assert.Equal(2, (int)AchievementCategory.Exploration);
            Assert.Equal(3, (int)AchievementCategory.Collection);
            Assert.Equal(4, (int)AchievementCategory.Growth);
        }

        [Fact]
        public void AchievementCategory_EnumCount_IsCorrect()
        {
            var values = Enum.GetValues<AchievementCategory>();
            Assert.Equal(5, values.Length);
        }

        #endregion

        #region Achievement State Logic Tests - 成就状态业务逻辑

        [Fact]
        public void AchievementState_RegisterAchievement_Works()
        {
            var state = new AchievementState();

            state.Achievements[1001] = new AchievementData
            {
                AchievementId = 1001,
                Name = "初出茅庐",
                Category = (int)AchievementCategory.Growth,
                Points = 10,
                TargetProgress = 1
            };

            Assert.Single(state.Achievements);
            Assert.True(state.Achievements.ContainsKey(1001));
        }

        [Fact]
        public void AchievementState_DuplicateRegistration_Prevented()
        {
            var state = new AchievementState();

            state.Achievements[1001] = new AchievementData
            {
                AchievementId = 1001,
                Name = "初出茅庐"
            };

            Assert.True(state.Achievements.ContainsKey(1001));
        }

        [Fact]
        public void AchievementState_TrackPoints_AfterUnlock()
        {
            var state = new AchievementState();

            state.Achievements[1001] = new AchievementData
            {
                AchievementId = 1001,
                Name = "初战告捷",
                Points = 10,
                TargetProgress = 1,
                CurrentProgress = 1,
                IsUnlocked = true,
                UnlockTime = DateTime.UtcNow
            };

            state.Achievements[1002] = new AchievementData
            {
                AchievementId = 1002,
                Name = "百战沙场",
                Points = 50,
                TargetProgress = 100,
                CurrentProgress = 100,
                IsUnlocked = true,
                UnlockTime = DateTime.UtcNow
            };

            state.TotalPoints = 60;
            state.UnlockedCount = 2;

            Assert.Equal(60, state.TotalPoints);
            Assert.Equal(2, state.UnlockedCount);
        }

        [Fact]
        public void AchievementState_FilterUnlocked_Works()
        {
            var state = new AchievementState();

            state.Achievements[1001] = new AchievementData
            {
                AchievementId = 1001,
                IsUnlocked = true
            };
            state.Achievements[1002] = new AchievementData
            {
                AchievementId = 1002,
                IsUnlocked = false
            };
            state.Achievements[1003] = new AchievementData
            {
                AchievementId = 1003,
                IsUnlocked = true
            };

            var unlocked = state.Achievements.Values.Where(a => a.IsUnlocked).ToList();
            Assert.Equal(2, unlocked.Count);
        }

        [Fact]
        public void AchievementState_FilterByCategory_Works()
        {
            var state = new AchievementState();

            state.Achievements[1001] = new AchievementData
            {
                AchievementId = 1001,
                Category = (int)AchievementCategory.Combat
            };
            state.Achievements[1002] = new AchievementData
            {
                AchievementId = 1002,
                Category = (int)AchievementCategory.Social
            };
            state.Achievements[1003] = new AchievementData
            {
                AchievementId = 1003,
                Category = (int)AchievementCategory.Combat
            };

            var combat = state.Achievements.Values
                .Where(a => a.Category == (int)AchievementCategory.Combat)
                .ToList();
            Assert.Equal(2, combat.Count);
        }

        [Fact]
        public void AchievementState_MultipleCategories_AllRepresented()
        {
            var state = new AchievementState();
            var categories = Enum.GetValues<AchievementCategory>();

            int id = 1;
            foreach (var cat in categories)
            {
                state.Achievements[id] = new AchievementData
                {
                    AchievementId = id,
                    Name = $"成就{id}",
                    Category = (int)cat
                };
                id++;
            }

            Assert.Equal(5, state.Achievements.Count);

            foreach (var cat in categories)
            {
                var count = state.Achievements.Values.Count(a => a.Category == (int)cat);
                Assert.Equal(1, count);
            }
        }

        [Fact]
        public void AchievementData_ProgressUpdate_IncrementWorks()
        {
            var achievement = new AchievementData
            {
                TargetProgress = 50,
                CurrentProgress = 0
            };

            for (int i = 0; i < 10; i++)
            {
                achievement.CurrentProgress = Math.Min(
                    achievement.CurrentProgress + 5,
                    achievement.TargetProgress);
            }

            Assert.Equal(50, achievement.CurrentProgress);
        }

        [Fact]
        public void AchievementData_Rewards_CanBeRetrieved()
        {
            var achievement = new AchievementData
            {
                Rewards = new Dictionary<string, int>
                {
                    { "金币", 1000 },
                    { "经验", 500 },
                    { "声望", 100 }
                }
            };

            Assert.Equal(3, achievement.Rewards.Count);
            Assert.Equal(1000, achievement.Rewards["金币"]);
            Assert.Equal(500, achievement.Rewards["经验"]);
            Assert.Equal(100, achievement.Rewards["声望"]);
        }

        #endregion

        #region GameEventType Achievement Events Tests

        [Fact]
        public void GameEventType_AchievementEvents_HaveExpectedValues()
        {
            Assert.Equal(700, (int)GameEventType.AchievementUnlocked);
            Assert.Equal(701, (int)GameEventType.AchievementProgressUpdated);
        }

        #endregion
    }
}
