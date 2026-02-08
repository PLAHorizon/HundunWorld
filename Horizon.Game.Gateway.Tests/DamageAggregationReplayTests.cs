using Horizon.Orleans.Grains;
using Horizon.Orleans.Interface;
using Horizon.Game.Message.Network;

namespace Horizon.Game.Gateway.Tests
{
    /// <summary>
    /// 伤害统计聚合、战斗回放、组队五行匹配加成 单元测试
    /// </summary>
    public class DamageAggregationReplayTests
    {
        #region AggregateDamageStats Tests

        [Fact]
        public void AggregateDamageStats_EmptyLog_ReturnsDefaults()
        {
            var stats = CombatCalculator.AggregateDamageStats(new List<CombatLogEntry>(), 1);
            Assert.Equal(0f, stats.TotalDamageDealt);
            Assert.Equal(0f, stats.TotalDamageReceived);
            Assert.Equal(0, stats.TotalAttacks);
            Assert.Equal(0, stats.TotalHits);
            Assert.Equal(0, stats.CriticalHits);
            Assert.Equal(0, stats.DodgedAttacks);
            Assert.Equal(0, stats.BlockedAttacks);
            Assert.Equal(0, stats.KillCount);
            Assert.Equal(0, stats.DeathCount);
            Assert.Equal(0f, stats.MaxSingleDamage);
            Assert.Equal(0f, stats.AverageDamagePerHit);
            Assert.Equal(0f, stats.DPS);
        }

        [Fact]
        public void AggregateDamageStats_SingleAttack_CountsCorrectly()
        {
            var log = new List<CombatLogEntry>
            {
                new CombatLogEntry
                {
                    Timestamp = DateTime.UtcNow,
                    AttackerId = 1,
                    DefenderId = 2,
                    DamageDealt = 100f,
                    LogType = CombatLogType.Attack
                }
            };
            var stats = CombatCalculator.AggregateDamageStats(log, 1);
            Assert.Equal(100f, stats.TotalDamageDealt);
            Assert.Equal(1, stats.TotalAttacks);
            Assert.Equal(1, stats.TotalHits);
            Assert.Equal(100f, stats.MaxSingleDamage);
            Assert.Equal(100f, stats.AverageDamagePerHit);
            Assert.Equal(100f, stats.DPS); // single entry, duration=0
        }

        [Fact]
        public void AggregateDamageStats_MultipleAttacks_VerifiesTotals()
        {
            var baseTime = DateTime.UtcNow;
            var log = new List<CombatLogEntry>
            {
                new CombatLogEntry { Timestamp = baseTime, AttackerId = 1, DefenderId = 2, DamageDealt = 50f, LogType = CombatLogType.Attack },
                new CombatLogEntry { Timestamp = baseTime.AddSeconds(1), AttackerId = 1, DefenderId = 2, DamageDealt = 150f, LogType = CombatLogType.Attack, IsCritical = true },
                new CombatLogEntry { Timestamp = baseTime.AddSeconds(2), AttackerId = 1, DefenderId = 2, DamageDealt = 0f, LogType = CombatLogType.Attack, IsDodged = true },
                new CombatLogEntry { Timestamp = baseTime.AddSeconds(3), AttackerId = 1, DefenderId = 2, DamageDealt = 40f, LogType = CombatLogType.Attack, IsBlocked = true },
            };
            var stats = CombatCalculator.AggregateDamageStats(log, 1);
            Assert.Equal(4, stats.TotalAttacks);
            Assert.Equal(3, stats.TotalHits); // dodged doesn't count as hit
            Assert.Equal(1, stats.CriticalHits);
            Assert.Equal(1, stats.BlockedAttacks);
            Assert.Equal(240f, stats.TotalDamageDealt); // 50+150+40
        }

        [Fact]
        public void AggregateDamageStats_DodgedAttacks_CountedForDefender()
        {
            var log = new List<CombatLogEntry>
            {
                new CombatLogEntry { Timestamp = DateTime.UtcNow, AttackerId = 2, DefenderId = 1, DamageDealt = 0f, LogType = CombatLogType.Attack, IsDodged = true },
                new CombatLogEntry { Timestamp = DateTime.UtcNow, AttackerId = 2, DefenderId = 1, DamageDealt = 0f, LogType = CombatLogType.Attack, IsDodged = true },
            };
            var stats = CombatCalculator.AggregateDamageStats(log, 1);
            Assert.Equal(2, stats.DodgedAttacks);
            Assert.Equal(0f, stats.TotalDamageReceived);
        }

        [Fact]
        public void AggregateDamageStats_KillsAndDeaths()
        {
            var log = new List<CombatLogEntry>
            {
                new CombatLogEntry { Timestamp = DateTime.UtcNow, AttackerId = 1, DefenderId = 2, LogType = CombatLogType.Death },
                new CombatLogEntry { Timestamp = DateTime.UtcNow, AttackerId = 1, DefenderId = 3, LogType = CombatLogType.Death },
                new CombatLogEntry { Timestamp = DateTime.UtcNow, AttackerId = 3, DefenderId = 1, LogType = CombatLogType.Death },
            };
            var stats = CombatCalculator.AggregateDamageStats(log, 1);
            Assert.Equal(2, stats.KillCount);
            Assert.Equal(1, stats.DeathCount);
        }

        [Fact]
        public void AggregateDamageStats_DPS_Calculation()
        {
            var baseTime = DateTime.UtcNow;
            var log = new List<CombatLogEntry>
            {
                new CombatLogEntry { Timestamp = baseTime, AttackerId = 1, DefenderId = 2, DamageDealt = 100f, LogType = CombatLogType.Attack },
                new CombatLogEntry { Timestamp = baseTime.AddSeconds(10), AttackerId = 1, DefenderId = 2, DamageDealt = 100f, LogType = CombatLogType.Attack },
            };
            var stats = CombatCalculator.AggregateDamageStats(log, 1);
            Assert.Equal(200f / 10f, stats.DPS, 0.01f);
        }

        [Fact]
        public void AggregateDamageStats_MaxSingleDamage()
        {
            var log = new List<CombatLogEntry>
            {
                new CombatLogEntry { Timestamp = DateTime.UtcNow, AttackerId = 1, DefenderId = 2, DamageDealt = 30f, LogType = CombatLogType.Attack },
                new CombatLogEntry { Timestamp = DateTime.UtcNow, AttackerId = 1, DefenderId = 2, DamageDealt = 200f, LogType = CombatLogType.Attack },
                new CombatLogEntry { Timestamp = DateTime.UtcNow, AttackerId = 1, DefenderId = 2, DamageDealt = 80f, LogType = CombatLogType.Attack },
            };
            var stats = CombatCalculator.AggregateDamageStats(log, 1);
            Assert.Equal(200f, stats.MaxSingleDamage);
        }

        [Fact]
        public void AggregateDamageStats_AverageDamagePerHit()
        {
            var log = new List<CombatLogEntry>
            {
                new CombatLogEntry { Timestamp = DateTime.UtcNow, AttackerId = 1, DefenderId = 2, DamageDealt = 60f, LogType = CombatLogType.Attack },
                new CombatLogEntry { Timestamp = DateTime.UtcNow, AttackerId = 1, DefenderId = 2, DamageDealt = 120f, LogType = CombatLogType.Attack },
            };
            var stats = CombatCalculator.AggregateDamageStats(log, 1);
            Assert.Equal(90f, stats.AverageDamagePerHit);
        }

        [Fact]
        public void AggregateDamageStats_DamageReceived()
        {
            var log = new List<CombatLogEntry>
            {
                new CombatLogEntry { Timestamp = DateTime.UtcNow, AttackerId = 2, DefenderId = 1, DamageDealt = 75f, LogType = CombatLogType.Attack },
                new CombatLogEntry { Timestamp = DateTime.UtcNow, AttackerId = 3, DefenderId = 1, DamageDealt = 25f, LogType = CombatLogType.Attack },
            };
            var stats = CombatCalculator.AggregateDamageStats(log, 1);
            Assert.Equal(100f, stats.TotalDamageReceived);
        }

        #endregion

        #region BuildReplayData Tests

        [Fact]
        public void BuildReplayData_EmptyLog_ReturnsDefaults()
        {
            var replay = CombatCalculator.BuildReplayData(new List<CombatLogEntry>());
            Assert.Empty(replay.Frames);
            Assert.Empty(replay.Participants);
            Assert.Equal(0f, replay.TotalDuration);
        }

        [Fact]
        public void BuildReplayData_WithEntries_VerifyFrameOrdering()
        {
            var baseTime = DateTime.UtcNow;
            var log = new List<CombatLogEntry>
            {
                new CombatLogEntry { Timestamp = baseTime, AttackerId = 1, DefenderId = 2, DamageDealt = 50f, LogType = CombatLogType.Attack },
                new CombatLogEntry { Timestamp = baseTime.AddSeconds(1), AttackerId = 2, DefenderId = 1, DamageDealt = 30f, LogType = CombatLogType.Attack },
                new CombatLogEntry { Timestamp = baseTime.AddSeconds(2), AttackerId = 1, DefenderId = 3, DamageDealt = 70f, LogType = CombatLogType.SkillCast },
            };
            var replay = CombatCalculator.BuildReplayData(log);
            Assert.Equal(3, replay.Frames.Count);
            Assert.Equal(0, replay.Frames[0].FrameIndex);
            Assert.Equal(1, replay.Frames[1].FrameIndex);
            Assert.Equal(2, replay.Frames[2].FrameIndex);
            Assert.Contains((ulong)1, replay.Participants);
            Assert.Contains((ulong)2, replay.Participants);
            Assert.Contains((ulong)3, replay.Participants);
        }

        [Fact]
        public void BuildReplayData_DurationCalculation()
        {
            var baseTime = DateTime.UtcNow;
            var log = new List<CombatLogEntry>
            {
                new CombatLogEntry { Timestamp = baseTime, AttackerId = 1, DefenderId = 2, LogType = CombatLogType.Attack },
                new CombatLogEntry { Timestamp = baseTime.AddSeconds(5), AttackerId = 1, DefenderId = 2, LogType = CombatLogType.Attack },
            };
            var replay = CombatCalculator.BuildReplayData(log);
            Assert.Equal(5f, replay.TotalDuration, 0.01f);
        }

        [Fact]
        public void BuildReplayData_FrameFieldsMatch()
        {
            var log = new List<CombatLogEntry>
            {
                new CombatLogEntry
                {
                    Timestamp = DateTime.UtcNow,
                    AttackerId = 10,
                    DefenderId = 20,
                    DamageDealt = 99f,
                    SkillId = 5,
                    ElementType = 3,
                    IsCritical = true,
                    IsDodged = false,
                    IsBlocked = true,
                    LogType = CombatLogType.SkillCast
                }
            };
            var replay = CombatCalculator.BuildReplayData(log);
            var frame = replay.Frames[0];
            Assert.Equal((ulong)10, frame.ActorId);
            Assert.Equal((ulong)20, frame.TargetId);
            Assert.Equal(99f, frame.DamageDealt);
            Assert.Equal(5, frame.SkillId);
            Assert.Equal(3, frame.ElementType);
            Assert.True(frame.IsCritical);
            Assert.False(frame.IsDodged);
            Assert.True(frame.IsBlocked);
            Assert.Equal(CombatLogType.SkillCast, frame.ActionType);
        }

        #endregion

        #region CalculateTeamWuxingSynergy Tests

        [Fact]
        public void CalculateTeamWuxingSynergy_NoElements_ReturnsZero()
        {
            Assert.Equal(0f, CombatCalculator.CalculateTeamWuxingSynergy(new List<int>()));
        }

        [Fact]
        public void CalculateTeamWuxingSynergy_SingleElement_ReturnsZero()
        {
            Assert.Equal(0f, CombatCalculator.CalculateTeamWuxingSynergy(new List<int> { 1 }));
        }

        [Fact]
        public void CalculateTeamWuxingSynergy_OneSynergyPair_ReturnsFivePercent()
        {
            // 金(1) and 水(3) - 金生水
            var result = CombatCalculator.CalculateTeamWuxingSynergy(new List<int> { 1, 3 });
            Assert.Equal(0.05f, result, 0.001f);
        }

        [Fact]
        public void CalculateTeamWuxingSynergy_TwoSynergyPairs()
        {
            // 金(1), 水(3), 木(2) - 金生水, 水生木
            var result = CombatCalculator.CalculateTeamWuxingSynergy(new List<int> { 1, 3, 2 });
            Assert.Equal(0.10f, result, 0.001f);
        }

        [Fact]
        public void CalculateTeamWuxingSynergy_AllFiveElements_FullBonus()
        {
            // All 5 synergy pairs + 20% bonus = 5*0.05 + 0.20 = 0.45
            var result = CombatCalculator.CalculateTeamWuxingSynergy(new List<int> { 1, 2, 3, 4, 5 });
            Assert.Equal(0.45f, result, 0.001f);
        }

        [Fact]
        public void CalculateTeamWuxingSynergy_DuplicateElements_NoDuplicateBonus()
        {
            // Duplicates should not add extra bonus
            var result = CombatCalculator.CalculateTeamWuxingSynergy(new List<int> { 1, 1, 3, 3 });
            Assert.Equal(0.05f, result, 0.001f);
        }

        [Fact]
        public void CalculateTeamWuxingSynergy_NullList_ReturnsZero()
        {
            Assert.Equal(0f, CombatCalculator.CalculateTeamWuxingSynergy(null!));
        }

        [Fact]
        public void CalculateTeamWuxingSynergy_InvalidElements_ReturnsZero()
        {
            Assert.Equal(0f, CombatCalculator.CalculateTeamWuxingSynergy(new List<int> { 0, 6, 99 }));
        }

        #endregion

        #region Default Value Tests

        [Fact]
        public void CombatReplayFrame_DefaultValues()
        {
            var frame = new CombatReplayFrame();
            Assert.Equal(0, frame.FrameIndex);
            Assert.Equal(default(DateTime), frame.Timestamp);
            Assert.Equal(default(CombatLogType), frame.ActionType);
            Assert.Equal(0UL, frame.ActorId);
            Assert.Equal(0UL, frame.TargetId);
            Assert.Equal(0, frame.SkillId);
            Assert.Equal(0f, frame.DamageDealt);
            Assert.Equal(0, frame.ElementType);
            Assert.False(frame.IsCritical);
            Assert.False(frame.IsDodged);
            Assert.False(frame.IsBlocked);
        }

        [Fact]
        public void CombatReplayData_DefaultValues()
        {
            var data = new CombatReplayData();
            Assert.Equal("", data.ReplayId);
            Assert.Equal(default(DateTime), data.StartTime);
            Assert.Equal(default(DateTime), data.EndTime);
            Assert.NotNull(data.Participants);
            Assert.Empty(data.Participants);
            Assert.NotNull(data.Frames);
            Assert.Empty(data.Frames);
            Assert.Equal(0f, data.TotalDuration);
        }

        [Fact]
        public void DamageAggregateStats_DefaultValues()
        {
            var stats = new DamageAggregateStats();
            Assert.Equal(0UL, stats.PlayerId);
            Assert.Equal(0f, stats.TotalDamageDealt);
            Assert.Equal(0f, stats.TotalDamageReceived);
            Assert.Equal(0, stats.TotalAttacks);
            Assert.Equal(0, stats.TotalHits);
            Assert.Equal(0, stats.CriticalHits);
            Assert.Equal(0, stats.DodgedAttacks);
            Assert.Equal(0, stats.BlockedAttacks);
            Assert.Equal(0, stats.KillCount);
            Assert.Equal(0, stats.DeathCount);
            Assert.Equal(0f, stats.MaxSingleDamage);
            Assert.Equal(0f, stats.AverageDamagePerHit);
            Assert.Equal(0f, stats.DPS);
        }

        #endregion
    }
}
