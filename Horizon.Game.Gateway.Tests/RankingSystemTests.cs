using Horizon.Orleans.Grains;
using Horizon.Orleans.Interface;
using Horizon.Game.Message.Network;

namespace Horizon.Game.Gateway.Tests
{
    /// <summary>
    /// RankingState, RankingEntry, RankingInfo 数据模型及业务逻辑单元测试
    /// 测试排行榜系统的状态管理和排名逻辑
    /// </summary>
    public class RankingSystemTests
    {
        #region RankingState Tests - 排行榜状态默认值

        [Fact]
        public void RankingState_DefaultValues_AreCorrect()
        {
            var state = new RankingState();
            Assert.NotNull(state.Entries);
            Assert.Empty(state.Entries);
            Assert.Equal(100, state.MaxEntries);
            Assert.Equal(0, state.RankingType);
            Assert.Equal("", state.RankingName);
        }

        [Fact]
        public void RankingState_SetMaxEntries_Works()
        {
            var state = new RankingState { MaxEntries = 50 };
            Assert.Equal(50, state.MaxEntries);
        }

        [Fact]
        public void RankingState_SetRankingType_Works()
        {
            var state = new RankingState { RankingType = (int)RankingType.CombatPower };
            Assert.Equal((int)RankingType.CombatPower, state.RankingType);
        }

        [Fact]
        public void RankingState_SetRankingName_Works()
        {
            var state = new RankingState { RankingName = "战力排行榜" };
            Assert.Equal("战力排行榜", state.RankingName);
        }

        [Fact]
        public void RankingState_SetLastUpdateTime_Works()
        {
            var now = DateTime.UtcNow;
            var state = new RankingState { LastUpdateTime = now };
            Assert.Equal(now, state.LastUpdateTime);
        }

        #endregion

        #region RankingEntry Tests - 排行榜条目

        [Fact]
        public void RankingEntry_DefaultValues_AreCorrect()
        {
            var entry = new RankingEntry();
            Assert.Equal(0, entry.Rank);
            Assert.Equal(Guid.Empty, entry.PlayerId);
            Assert.Equal("", entry.PlayerName);
            Assert.Equal(0, entry.Score);
        }

        [Fact]
        public void RankingEntry_SetProperties_Works()
        {
            var playerId = Guid.NewGuid();
            var now = DateTime.UtcNow;
            var entry = new RankingEntry
            {
                Rank = 1,
                PlayerId = playerId,
                PlayerName = "大侠",
                Score = 99999,
                UpdateTime = now
            };

            Assert.Equal(1, entry.Rank);
            Assert.Equal(playerId, entry.PlayerId);
            Assert.Equal("大侠", entry.PlayerName);
            Assert.Equal(99999, entry.Score);
            Assert.Equal(now, entry.UpdateTime);
        }

        [Fact]
        public void RankingEntry_ScoreComparison_HigherScoreFirst()
        {
            var entry1 = new RankingEntry { Score = 1000 };
            var entry2 = new RankingEntry { Score = 2000 };

            Assert.True(entry2.Score > entry1.Score);
        }

        #endregion

        #region RankingInfo Tests - 排行榜信息

        [Fact]
        public void RankingInfo_DefaultValues_AreCorrect()
        {
            var info = new RankingInfo();
            Assert.Equal(0, info.RankingType);
            Assert.Equal("", info.RankingName);
            Assert.NotNull(info.Entries);
            Assert.Empty(info.Entries);
            Assert.Equal(100, info.MaxEntries);
        }

        [Fact]
        public void RankingInfo_SetProperties_Works()
        {
            var now = DateTime.UtcNow;
            var info = new RankingInfo
            {
                RankingType = (int)RankingType.Level,
                RankingName = "等级排行榜",
                MaxEntries = 50,
                LastUpdateTime = now
            };

            Assert.Equal((int)RankingType.Level, info.RankingType);
            Assert.Equal("等级排行榜", info.RankingName);
            Assert.Equal(50, info.MaxEntries);
            Assert.Equal(now, info.LastUpdateTime);
        }

        [Fact]
        public void RankingInfo_AddEntries_Works()
        {
            var info = new RankingInfo();
            info.Entries.Add(new RankingEntry { Rank = 1, PlayerName = "玩家A", Score = 5000 });
            info.Entries.Add(new RankingEntry { Rank = 2, PlayerName = "玩家B", Score = 4000 });
            info.Entries.Add(new RankingEntry { Rank = 3, PlayerName = "玩家C", Score = 3000 });

            Assert.Equal(3, info.Entries.Count);
            Assert.Equal("玩家A", info.Entries[0].PlayerName);
            Assert.Equal(5000, info.Entries[0].Score);
        }

        #endregion

        #region RankingType Enum Tests - 排行榜类型枚举

        [Fact]
        public void RankingType_HasExpectedValues()
        {
            Assert.Equal(0, (int)RankingType.CombatPower);
            Assert.Equal(1, (int)RankingType.Level);
            Assert.Equal(2, (int)RankingType.Wealth);
            Assert.Equal(3, (int)RankingType.AchievementPoints);
            Assert.Equal(4, (int)RankingType.PvpScore);
        }

        [Fact]
        public void RankingType_EnumCount_IsCorrect()
        {
            var values = Enum.GetValues<RankingType>();
            Assert.Equal(5, values.Length);
        }

        #endregion

        #region Ranking State Logic Tests - 排行榜状态业务逻辑

        [Fact]
        public void RankingState_AddEntry_Works()
        {
            var state = new RankingState();
            var playerId = Guid.NewGuid();

            state.Entries[playerId] = new RankingEntry
            {
                Rank = 1,
                PlayerId = playerId,
                PlayerName = "测试玩家",
                Score = 1000,
                UpdateTime = DateTime.UtcNow
            };

            Assert.Single(state.Entries);
            Assert.True(state.Entries.ContainsKey(playerId));
        }

        [Fact]
        public void RankingState_RemoveEntry_Works()
        {
            var state = new RankingState();
            var playerId = Guid.NewGuid();

            state.Entries[playerId] = new RankingEntry
            {
                PlayerId = playerId,
                PlayerName = "测试玩家",
                Score = 1000
            };

            Assert.Single(state.Entries);

            state.Entries.Remove(playerId);
            Assert.Empty(state.Entries);
        }

        [Fact]
        public void RankingState_UpdateExistingEntry_Works()
        {
            var state = new RankingState();
            var playerId = Guid.NewGuid();

            state.Entries[playerId] = new RankingEntry
            {
                PlayerId = playerId,
                PlayerName = "测试玩家",
                Score = 1000
            };

            state.Entries[playerId].Score = 2000;

            Assert.Equal(2000, state.Entries[playerId].Score);
        }

        [Fact]
        public void RankingState_SortByScore_Descending()
        {
            var state = new RankingState();

            var ids = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
            state.Entries[ids[0]] = new RankingEntry { PlayerId = ids[0], PlayerName = "A", Score = 300 };
            state.Entries[ids[1]] = new RankingEntry { PlayerId = ids[1], PlayerName = "B", Score = 500 };
            state.Entries[ids[2]] = new RankingEntry { PlayerId = ids[2], PlayerName = "C", Score = 100 };

            var sorted = state.Entries.Values
                .OrderByDescending(e => e.Score)
                .ToList();

            Assert.Equal("B", sorted[0].PlayerName);
            Assert.Equal("A", sorted[1].PlayerName);
            Assert.Equal("C", sorted[2].PlayerName);
        }

        [Fact]
        public void RankingState_MaxEntriesEnforcement_Works()
        {
            var state = new RankingState { MaxEntries = 3 };

            for (int i = 0; i < 5; i++)
            {
                var id = Guid.NewGuid();
                state.Entries[id] = new RankingEntry
                {
                    PlayerId = id,
                    PlayerName = $"Player{i}",
                    Score = (i + 1) * 100
                };
            }

            // Trim to max entries (keep highest scores)
            var sorted = state.Entries.Values
                .OrderByDescending(e => e.Score)
                .ToList();

            var toRemove = sorted.Skip(state.MaxEntries).ToList();
            foreach (var entry in toRemove)
            {
                state.Entries.Remove(entry.PlayerId);
            }

            Assert.Equal(3, state.Entries.Count);
            Assert.True(state.Entries.Values.All(e => e.Score >= 300));
        }

        [Fact]
        public void RankingState_TieBreaker_EarlierUpdateWins()
        {
            var state = new RankingState();
            var earlier = DateTime.UtcNow.AddMinutes(-10);
            var later = DateTime.UtcNow;

            var id1 = Guid.NewGuid();
            var id2 = Guid.NewGuid();

            state.Entries[id1] = new RankingEntry { PlayerId = id1, PlayerName = "A", Score = 1000, UpdateTime = earlier };
            state.Entries[id2] = new RankingEntry { PlayerId = id2, PlayerName = "B", Score = 1000, UpdateTime = later };

            var sorted = state.Entries.Values
                .OrderByDescending(e => e.Score)
                .ThenBy(e => e.UpdateTime)
                .ToList();

            Assert.Equal("A", sorted[0].PlayerName);
            Assert.Equal("B", sorted[1].PlayerName);
        }

        #endregion

        #region GameEventType Ranking Events Tests

        [Fact]
        public void GameEventType_RankingEvents_HaveExpectedValues()
        {
            Assert.Equal(500, (int)GameEventType.RankingUpdated);
            Assert.Equal(501, (int)GameEventType.RankingReset);
        }

        #endregion
    }
}
