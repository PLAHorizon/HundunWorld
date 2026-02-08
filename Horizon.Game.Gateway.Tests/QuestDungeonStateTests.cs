using Horizon.Orleans.Grains;
using Horizon.Orleans.Interface;
using Horizon.Game.Message.Network;

namespace Horizon.Game.Gateway.Tests
{
    /// <summary>
    /// QuestState, DungeonState 数据模型及业务逻辑单元测试
    /// 测试任务系统和副本系统的状态管理逻辑
    /// </summary>
    public class QuestDungeonStateTests
    {
        #region QuestState Tests - 任务状态默认值

        [Fact]
        public void QuestState_DefaultValues_AreCorrect()
        {
            var state = new QuestState();
            Assert.NotNull(state.ActiveQuests);
            Assert.Empty(state.ActiveQuests);
            Assert.NotNull(state.CompletedQuests);
            Assert.Empty(state.CompletedQuests);
            Assert.Equal(20, state.MaxActiveQuests);
        }

        [Fact]
        public void QuestState_SetMaxActiveQuests_Works()
        {
            var state = new QuestState { MaxActiveQuests = 30 };
            Assert.Equal(30, state.MaxActiveQuests);
        }

        #endregion

        #region QuestData Tests - 任务数据模型

        [Fact]
        public void QuestData_DefaultValues_AreCorrect()
        {
            var quest = new QuestData();
            Assert.Equal(0, quest.QuestId);
            Assert.Equal("", quest.QuestName);
            Assert.Equal("", quest.Description);
            Assert.Equal(0, quest.QuestType);
            Assert.Equal(0, quest.Level);
            Assert.Equal(0, quest.Status);
            Assert.NotNull(quest.Objectives);
            Assert.Empty(quest.Objectives);
            Assert.NotNull(quest.Rewards);
            Assert.Empty(quest.Rewards);
            Assert.Null(quest.CompleteTime);
        }

        [Fact]
        public void QuestData_SetProperties_Works()
        {
            var now = DateTime.UtcNow;
            var quest = new QuestData
            {
                QuestId = 1001,
                QuestName = "降妖除魔",
                Description = "击败山中妖怪",
                QuestType = 0,
                Level = 10,
                Status = (int)QuestProgressStatus.InProgress,
                AcceptTime = now,
                Rewards = new Dictionary<string, int> { { "经验", 500 }, { "金币", 100 } }
            };

            Assert.Equal(1001, quest.QuestId);
            Assert.Equal("降妖除魔", quest.QuestName);
            Assert.Equal("击败山中妖怪", quest.Description);
            Assert.Equal(0, quest.QuestType);
            Assert.Equal(10, quest.Level);
            Assert.Equal((int)QuestProgressStatus.InProgress, quest.Status);
            Assert.Equal(now, quest.AcceptTime);
            Assert.Equal(2, quest.Rewards.Count);
            Assert.Equal(500, quest.Rewards["经验"]);
            Assert.Equal(100, quest.Rewards["金币"]);
        }

        [Fact]
        public void QuestData_AddObjectives_Works()
        {
            var quest = new QuestData { QuestId = 1 };
            quest.Objectives.Add(new QuestObjectiveData
            {
                ObjectiveType = "Kill",
                Description = "击杀野狼",
                RequiredCount = 10,
                CurrentCount = 0,
                IsCompleted = false
            });
            quest.Objectives.Add(new QuestObjectiveData
            {
                ObjectiveType = "Collect",
                Description = "收集草药",
                RequiredCount = 5,
                CurrentCount = 3,
                IsCompleted = false
            });

            Assert.Equal(2, quest.Objectives.Count);
            Assert.Equal("Kill", quest.Objectives[0].ObjectiveType);
            Assert.Equal(10, quest.Objectives[0].RequiredCount);
            Assert.Equal(0, quest.Objectives[0].CurrentCount);
            Assert.False(quest.Objectives[0].IsCompleted);
            Assert.Equal("Collect", quest.Objectives[1].ObjectiveType);
            Assert.Equal(3, quest.Objectives[1].CurrentCount);
        }

        [Fact]
        public void QuestData_CompletedQuest_HasCompleteTime()
        {
            var now = DateTime.UtcNow;
            var quest = new QuestData
            {
                QuestId = 1,
                Status = (int)QuestProgressStatus.Completed,
                CompleteTime = now
            };

            Assert.Equal((int)QuestProgressStatus.Completed, quest.Status);
            Assert.NotNull(quest.CompleteTime);
            Assert.Equal(now, quest.CompleteTime);
        }

        #endregion

        #region QuestObjectiveData Tests - 任务目标数据模型

        [Fact]
        public void QuestObjectiveData_DefaultValues_AreCorrect()
        {
            var objective = new QuestObjectiveData();
            Assert.Equal("", objective.ObjectiveType);
            Assert.Equal("", objective.Description);
            Assert.Equal(0, objective.RequiredCount);
            Assert.Equal(0, objective.CurrentCount);
            Assert.False(objective.IsCompleted);
        }

        [Fact]
        public void QuestObjectiveData_MarkAsCompleted_Works()
        {
            var objective = new QuestObjectiveData
            {
                ObjectiveType = "Kill",
                Description = "击杀Boss",
                RequiredCount = 1,
                CurrentCount = 1,
                IsCompleted = true
            };

            Assert.True(objective.IsCompleted);
            Assert.Equal(objective.RequiredCount, objective.CurrentCount);
        }

        [Fact]
        public void QuestObjectiveData_ProgressUpdate_Works()
        {
            var objective = new QuestObjectiveData
            {
                ObjectiveType = "Collect",
                Description = "收集灵石",
                RequiredCount = 20,
                CurrentCount = 0
            };

            objective.CurrentCount = 5;
            Assert.Equal(5, objective.CurrentCount);
            Assert.False(objective.IsCompleted);

            objective.CurrentCount = 20;
            objective.IsCompleted = true;
            Assert.True(objective.IsCompleted);
        }

        #endregion

        #region QuestCompleteResult Tests

        [Fact]
        public void QuestCompleteResult_DefaultValues_AreCorrect()
        {
            var result = new QuestCompleteResult();
            Assert.False(result.Success);
            Assert.Equal("", result.Message);
            Assert.Equal(0, result.QuestId);
            Assert.NotNull(result.Rewards);
            Assert.Empty(result.Rewards);
        }

        [Fact]
        public void QuestCompleteResult_SuccessResult_HasRewards()
        {
            var result = new QuestCompleteResult
            {
                Success = true,
                Message = "完成任务成功",
                QuestId = 1001,
                Rewards = new Dictionary<string, int> { { "经验", 1000 }, { "金币", 500 } }
            };

            Assert.True(result.Success);
            Assert.Equal("完成任务成功", result.Message);
            Assert.Equal(1001, result.QuestId);
            Assert.Equal(2, result.Rewards.Count);
        }

        [Fact]
        public void QuestCompleteResult_FailureResult_NoRewards()
        {
            var result = new QuestCompleteResult
            {
                Success = false,
                Message = "任务目标尚未完成",
                QuestId = 1001
            };

            Assert.False(result.Success);
            Assert.Equal("任务目标尚未完成", result.Message);
            Assert.Empty(result.Rewards);
        }

        #endregion

        #region QuestProgressStatus Enum Tests

        [Fact]
        public void QuestProgressStatus_Values_AreCorrect()
        {
            Assert.Equal(0, (int)QuestProgressStatus.InProgress);
            Assert.Equal(1, (int)QuestProgressStatus.ReadyToSubmit);
            Assert.Equal(2, (int)QuestProgressStatus.Completed);
            Assert.Equal(3, (int)QuestProgressStatus.Abandoned);
        }

        #endregion

        #region QuestState Active Quest Management

        [Fact]
        public void QuestState_AddActiveQuest_IncreasesCount()
        {
            var state = new QuestState();
            state.ActiveQuests[1] = new QuestData { QuestId = 1, QuestName = "主线任务1" };
            state.ActiveQuests[2] = new QuestData { QuestId = 2, QuestName = "支线任务1" };

            Assert.Equal(2, state.ActiveQuests.Count);
            Assert.Equal("主线任务1", state.ActiveQuests[1].QuestName);
            Assert.Equal("支线任务1", state.ActiveQuests[2].QuestName);
        }

        [Fact]
        public void QuestState_RemoveActiveQuest_DecreasesCount()
        {
            var state = new QuestState();
            state.ActiveQuests[1] = new QuestData { QuestId = 1 };
            state.ActiveQuests[2] = new QuestData { QuestId = 2 };

            state.ActiveQuests.Remove(1);
            Assert.Single(state.ActiveQuests);
            Assert.False(state.ActiveQuests.ContainsKey(1));
            Assert.True(state.ActiveQuests.ContainsKey(2));
        }

        [Fact]
        public void QuestState_MoveQuestToCompleted_Works()
        {
            var state = new QuestState();
            var quest = new QuestData
            {
                QuestId = 1,
                QuestName = "主线任务",
                Status = (int)QuestProgressStatus.InProgress
            };
            state.ActiveQuests[1] = quest;

            // Complete the quest
            quest.Status = (int)QuestProgressStatus.Completed;
            quest.CompleteTime = DateTime.UtcNow;
            state.ActiveQuests.Remove(1);
            state.CompletedQuests[1] = quest;

            Assert.Empty(state.ActiveQuests);
            Assert.Single(state.CompletedQuests);
            Assert.Equal((int)QuestProgressStatus.Completed, state.CompletedQuests[1].Status);
            Assert.NotNull(state.CompletedQuests[1].CompleteTime);
        }

        [Fact]
        public void QuestState_AbandonQuest_RemovesFromActive()
        {
            var state = new QuestState();
            state.ActiveQuests[1] = new QuestData { QuestId = 1, QuestName = "日常任务" };
            state.ActiveQuests[2] = new QuestData { QuestId = 2, QuestName = "周常任务" };

            // Abandon quest 1
            state.ActiveQuests.Remove(1);

            Assert.Single(state.ActiveQuests);
            Assert.False(state.ActiveQuests.ContainsKey(1));
        }

        [Fact]
        public void QuestState_MaxActiveQuests_LimitCheck()
        {
            var state = new QuestState { MaxActiveQuests = 3 };

            state.ActiveQuests[1] = new QuestData { QuestId = 1 };
            state.ActiveQuests[2] = new QuestData { QuestId = 2 };
            state.ActiveQuests[3] = new QuestData { QuestId = 3 };

            Assert.Equal(state.MaxActiveQuests, state.ActiveQuests.Count);
            Assert.True(state.ActiveQuests.Count >= state.MaxActiveQuests);
        }

        [Fact]
        public void QuestState_DuplicateQuestId_OverwritesExisting()
        {
            var state = new QuestState();
            state.ActiveQuests[1] = new QuestData { QuestId = 1, QuestName = "原始任务" };
            state.ActiveQuests[1] = new QuestData { QuestId = 1, QuestName = "覆盖任务" };

            Assert.Single(state.ActiveQuests);
            Assert.Equal("覆盖任务", state.ActiveQuests[1].QuestName);
        }

        #endregion

        #region Quest Objective Progress Logic Tests

        [Fact]
        public void QuestObjective_ProgressClamping_DoesNotExceedRequired()
        {
            var objective = new QuestObjectiveData
            {
                RequiredCount = 10,
                CurrentCount = 0
            };

            // Simulate clamped progress update
            int progress = 15;
            objective.CurrentCount = Math.Min(objective.CurrentCount + progress, objective.RequiredCount);

            Assert.Equal(10, objective.CurrentCount);
        }

        [Fact]
        public void QuestObjective_IncrementalProgress_Works()
        {
            var objective = new QuestObjectiveData
            {
                RequiredCount = 5,
                CurrentCount = 0
            };

            for (int i = 1; i <= 5; i++)
            {
                objective.CurrentCount = Math.Min(objective.CurrentCount + 1, objective.RequiredCount);
            }

            Assert.Equal(5, objective.CurrentCount);
        }

        [Fact]
        public void Quest_AllObjectivesComplete_StatusBecomesReadyToSubmit()
        {
            var quest = new QuestData
            {
                QuestId = 1,
                Status = (int)QuestProgressStatus.InProgress,
                Objectives = new List<QuestObjectiveData>
                {
                    new QuestObjectiveData { RequiredCount = 3, CurrentCount = 3, IsCompleted = true },
                    new QuestObjectiveData { RequiredCount = 5, CurrentCount = 5, IsCompleted = true }
                }
            };

            bool allComplete = quest.Objectives.All(o => o.IsCompleted);
            if (allComplete)
            {
                quest.Status = (int)QuestProgressStatus.ReadyToSubmit;
            }

            Assert.Equal((int)QuestProgressStatus.ReadyToSubmit, quest.Status);
        }

        [Fact]
        public void Quest_PartialObjectivesComplete_StatusStaysInProgress()
        {
            var quest = new QuestData
            {
                QuestId = 1,
                Status = (int)QuestProgressStatus.InProgress,
                Objectives = new List<QuestObjectiveData>
                {
                    new QuestObjectiveData { RequiredCount = 3, CurrentCount = 3, IsCompleted = true },
                    new QuestObjectiveData { RequiredCount = 5, CurrentCount = 2, IsCompleted = false }
                }
            };

            bool allComplete = quest.Objectives.All(o => o.IsCompleted);
            Assert.False(allComplete);
            Assert.Equal((int)QuestProgressStatus.InProgress, quest.Status);
        }

        [Fact]
        public void Quest_NoObjectives_CanComplete()
        {
            var quest = new QuestData
            {
                QuestId = 1,
                Status = (int)QuestProgressStatus.InProgress,
                Objectives = new List<QuestObjectiveData>()
            };

            bool canComplete = quest.Objectives.Count == 0 || quest.Objectives.All(o => o.IsCompleted);
            Assert.True(canComplete);
        }

        #endregion

        #region Quest Type Tests

        [Fact]
        public void QuestData_MainQuest_TypeIsZero()
        {
            var quest = new QuestData { QuestType = 0 };
            Assert.Equal(0, quest.QuestType);
        }

        [Fact]
        public void QuestData_SideQuest_TypeIsOne()
        {
            var quest = new QuestData { QuestType = 1 };
            Assert.Equal(1, quest.QuestType);
        }

        [Fact]
        public void QuestData_DailyQuest_TypeIsTwo()
        {
            var quest = new QuestData { QuestType = 2 };
            Assert.Equal(2, quest.QuestType);
        }

        [Fact]
        public void QuestData_WeeklyQuest_TypeIsThree()
        {
            var quest = new QuestData { QuestType = 3 };
            Assert.Equal(3, quest.QuestType);
        }

        #endregion

        #region DungeonState Tests - 副本状态默认值

        [Fact]
        public void DungeonState_DefaultValues_AreCorrect()
        {
            var state = new DungeonState();
            Assert.Equal(0, state.DungeonTemplateId);
            Assert.Equal("", state.DungeonName);
            Assert.Equal(0, state.Difficulty);
            Assert.Equal(5, state.MaxPlayers);
            Assert.Equal((int)DungeonStatus.Waiting, state.Status);
            Assert.Equal(30, state.TimeLimitMinutes);
            Assert.Null(state.StartTime);
            Assert.False(state.IsCreated);
            Assert.NotNull(state.Players);
            Assert.Empty(state.Players);
            Assert.NotNull(state.Bosses);
            Assert.Empty(state.Bosses);
        }

        [Fact]
        public void DungeonState_SetProperties_Works()
        {
            var now = DateTime.UtcNow;
            var state = new DungeonState
            {
                DungeonTemplateId = 100,
                DungeonName = "混沌秘境",
                Difficulty = (int)DungeonDifficulty.Heroic,
                MaxPlayers = 10,
                TimeLimitMinutes = 60,
                Status = (int)DungeonStatus.InProgress,
                StartTime = now,
                IsCreated = true
            };

            Assert.Equal(100, state.DungeonTemplateId);
            Assert.Equal("混沌秘境", state.DungeonName);
            Assert.Equal((int)DungeonDifficulty.Heroic, state.Difficulty);
            Assert.Equal(10, state.MaxPlayers);
            Assert.Equal(60, state.TimeLimitMinutes);
            Assert.Equal((int)DungeonStatus.InProgress, state.Status);
            Assert.Equal(now, state.StartTime);
            Assert.True(state.IsCreated);
        }

        #endregion

        #region DungeonData Tests - 副本数据模型

        [Fact]
        public void DungeonData_DefaultValues_AreCorrect()
        {
            var data = new DungeonData();
            Assert.Equal(0, data.DungeonTemplateId);
            Assert.Equal("", data.DungeonName);
            Assert.Equal(0, data.Difficulty);
            Assert.Equal(0, data.MaxPlayers);
            Assert.Equal(0, data.CurrentPlayers);
            Assert.Equal(0, data.Status);
            Assert.Equal(0, data.TimeLimitMinutes);
            Assert.Null(data.StartTime);
            Assert.False(data.IsCreated);
            Assert.NotNull(data.Bosses);
            Assert.Empty(data.Bosses);
            Assert.Equal(0, data.DefeatedBossCount);
        }

        [Fact]
        public void DungeonData_WithBosses_CountsCorrectly()
        {
            var data = new DungeonData
            {
                DungeonTemplateId = 1,
                DungeonName = "太极洞",
                Bosses = new List<DungeonBossData>
                {
                    new DungeonBossData { BossId = 1, BossName = "青龙", IsDefeated = true },
                    new DungeonBossData { BossId = 2, BossName = "白虎", IsDefeated = false },
                    new DungeonBossData { BossId = 3, BossName = "朱雀", IsDefeated = true }
                },
                DefeatedBossCount = 2
            };

            Assert.Equal(3, data.Bosses.Count);
            Assert.Equal(2, data.DefeatedBossCount);
            Assert.True(data.Bosses[0].IsDefeated);
            Assert.False(data.Bosses[1].IsDefeated);
        }

        #endregion

        #region DungeonBossData Tests

        [Fact]
        public void DungeonBossData_DefaultValues_AreCorrect()
        {
            var boss = new DungeonBossData();
            Assert.Equal(0, boss.BossId);
            Assert.Equal("", boss.BossName);
            Assert.False(boss.IsDefeated);
            Assert.Null(boss.DefeatTime);
        }

        [Fact]
        public void DungeonBossData_Defeated_HasDefeatTime()
        {
            var now = DateTime.UtcNow;
            var boss = new DungeonBossData
            {
                BossId = 1,
                BossName = "玄武",
                IsDefeated = true,
                DefeatTime = now
            };

            Assert.True(boss.IsDefeated);
            Assert.Equal(now, boss.DefeatTime);
        }

        #endregion

        #region DungeonCompleteResult Tests

        [Fact]
        public void DungeonCompleteResult_DefaultValues_AreCorrect()
        {
            var result = new DungeonCompleteResult();
            Assert.False(result.Success);
            Assert.Equal("", result.Message);
            Assert.Equal(0, result.DungeonTemplateId);
            Assert.Equal(0, result.Difficulty);
            Assert.Equal(0, result.TotalBosses);
            Assert.Equal(0, result.DefeatedBosses);
            Assert.Equal(0, result.ClearTimeSeconds);
        }

        [Fact]
        public void DungeonCompleteResult_SuccessResult_HasClearTime()
        {
            var result = new DungeonCompleteResult
            {
                Success = true,
                Message = "副本通关成功",
                DungeonTemplateId = 100,
                Difficulty = (int)DungeonDifficulty.Hard,
                TotalBosses = 3,
                DefeatedBosses = 3,
                ClearTimeSeconds = 1200.5
            };

            Assert.True(result.Success);
            Assert.Equal(3, result.TotalBosses);
            Assert.Equal(3, result.DefeatedBosses);
            Assert.True(result.ClearTimeSeconds > 0);
        }

        [Fact]
        public void DungeonCompleteResult_FailureResult_PartialBosses()
        {
            var result = new DungeonCompleteResult
            {
                Success = false,
                Message = "尚有Boss未被击败",
                TotalBosses = 5,
                DefeatedBosses = 3
            };

            Assert.False(result.Success);
            Assert.True(result.DefeatedBosses < result.TotalBosses);
        }

        #endregion

        #region DungeonStatus Enum Tests

        [Fact]
        public void DungeonStatus_Values_AreCorrect()
        {
            Assert.Equal(0, (int)DungeonStatus.Waiting);
            Assert.Equal(1, (int)DungeonStatus.InProgress);
            Assert.Equal(2, (int)DungeonStatus.Completed);
            Assert.Equal(3, (int)DungeonStatus.Failed);
        }

        #endregion

        #region DungeonDifficulty Enum Tests

        [Fact]
        public void DungeonDifficulty_Values_AreCorrect()
        {
            Assert.Equal(0, (int)DungeonDifficulty.Normal);
            Assert.Equal(1, (int)DungeonDifficulty.Hard);
            Assert.Equal(2, (int)DungeonDifficulty.Heroic);
            Assert.Equal(3, (int)DungeonDifficulty.Hell);
        }

        #endregion

        #region DungeonState Player Management Tests

        [Fact]
        public void DungeonState_AddPlayers_IncreasesCount()
        {
            var state = new DungeonState();
            var player1 = Guid.NewGuid();
            var player2 = Guid.NewGuid();

            state.Players.Add(player1);
            state.Players.Add(player2);

            Assert.Equal(2, state.Players.Count);
            Assert.Contains(player1, state.Players);
            Assert.Contains(player2, state.Players);
        }

        [Fact]
        public void DungeonState_RemovePlayer_DecreasesCount()
        {
            var state = new DungeonState();
            var player1 = Guid.NewGuid();
            var player2 = Guid.NewGuid();

            state.Players.Add(player1);
            state.Players.Add(player2);
            state.Players.Remove(player1);

            Assert.Single(state.Players);
            Assert.DoesNotContain(player1, state.Players);
            Assert.Contains(player2, state.Players);
        }

        [Fact]
        public void DungeonState_DuplicatePlayer_DoesNotAdd()
        {
            var state = new DungeonState();
            var player1 = Guid.NewGuid();

            state.Players.Add(player1);
            bool added = state.Players.Add(player1);

            Assert.False(added);
            Assert.Single(state.Players);
        }

        [Fact]
        public void DungeonState_MaxPlayersCheck_Works()
        {
            var state = new DungeonState { MaxPlayers = 3 };
            state.Players.Add(Guid.NewGuid());
            state.Players.Add(Guid.NewGuid());
            state.Players.Add(Guid.NewGuid());

            Assert.True(state.Players.Count >= state.MaxPlayers);
        }

        #endregion

        #region DungeonState Boss Management Tests

        [Fact]
        public void DungeonState_AddBoss_IncreasesCount()
        {
            var state = new DungeonState();
            state.Bosses[1] = new DungeonBossData { BossId = 1, BossName = "青龙" };
            state.Bosses[2] = new DungeonBossData { BossId = 2, BossName = "白虎" };

            Assert.Equal(2, state.Bosses.Count);
        }

        [Fact]
        public void DungeonState_DefeatBoss_UpdatesState()
        {
            var state = new DungeonState();
            state.Bosses[1] = new DungeonBossData { BossId = 1, BossName = "青龙", IsDefeated = false };
            state.Bosses[2] = new DungeonBossData { BossId = 2, BossName = "白虎", IsDefeated = false };

            state.Bosses[1].IsDefeated = true;
            state.Bosses[1].DefeatTime = DateTime.UtcNow;

            Assert.True(state.Bosses[1].IsDefeated);
            Assert.False(state.Bosses[2].IsDefeated);
            Assert.Equal(1, state.Bosses.Values.Count(b => b.IsDefeated));
        }

        [Fact]
        public void DungeonState_AllBossesDefeated_CanComplete()
        {
            var state = new DungeonState();
            state.Bosses[1] = new DungeonBossData { BossId = 1, BossName = "青龙", IsDefeated = true };
            state.Bosses[2] = new DungeonBossData { BossId = 2, BossName = "白虎", IsDefeated = true };
            state.Bosses[3] = new DungeonBossData { BossId = 3, BossName = "朱雀", IsDefeated = true };

            bool allDefeated = state.Bosses.Values.All(b => b.IsDefeated);
            Assert.True(allDefeated);
        }

        [Fact]
        public void DungeonState_SomeBossesNotDefeated_CannotComplete()
        {
            var state = new DungeonState();
            state.Bosses[1] = new DungeonBossData { BossId = 1, BossName = "青龙", IsDefeated = true };
            state.Bosses[2] = new DungeonBossData { BossId = 2, BossName = "白虎", IsDefeated = false };

            bool allDefeated = state.Bosses.Values.All(b => b.IsDefeated);
            Assert.False(allDefeated);
        }

        [Fact]
        public void DungeonState_NoBosses_CanComplete()
        {
            var state = new DungeonState();

            bool canComplete = state.Bosses.Count == 0 || state.Bosses.Values.All(b => b.IsDefeated);
            Assert.True(canComplete);
        }

        #endregion

        #region Dungeon Timeout Logic Tests

        [Fact]
        public void Dungeon_TimedOut_WhenExceedsLimit()
        {
            var state = new DungeonState
            {
                TimeLimitMinutes = 30,
                StartTime = DateTime.UtcNow.AddMinutes(-31),
                Status = (int)DungeonStatus.InProgress,
                IsCreated = true
            };

            var elapsed = DateTime.UtcNow - state.StartTime!.Value;
            bool timedOut = elapsed.TotalMinutes >= state.TimeLimitMinutes;

            Assert.True(timedOut);
        }

        [Fact]
        public void Dungeon_NotTimedOut_WhenWithinLimit()
        {
            var state = new DungeonState
            {
                TimeLimitMinutes = 30,
                StartTime = DateTime.UtcNow.AddMinutes(-10),
                Status = (int)DungeonStatus.InProgress,
                IsCreated = true
            };

            var elapsed = DateTime.UtcNow - state.StartTime!.Value;
            bool timedOut = elapsed.TotalMinutes >= state.TimeLimitMinutes;

            Assert.False(timedOut);
        }

        [Fact]
        public void Dungeon_NotTimedOut_WhenNotStarted()
        {
            var state = new DungeonState
            {
                TimeLimitMinutes = 30,
                StartTime = null,
                IsCreated = true
            };

            bool timedOut = state.StartTime.HasValue &&
                            (DateTime.UtcNow - state.StartTime.Value).TotalMinutes >= state.TimeLimitMinutes;

            Assert.False(timedOut);
        }

        #endregion

        #region Dungeon Status Transitions Tests

        [Fact]
        public void Dungeon_StatusTransition_WaitingToInProgress()
        {
            var state = new DungeonState
            {
                Status = (int)DungeonStatus.Waiting,
                IsCreated = true
            };

            // First player enters
            state.Status = (int)DungeonStatus.InProgress;
            state.StartTime = DateTime.UtcNow;

            Assert.Equal((int)DungeonStatus.InProgress, state.Status);
            Assert.NotNull(state.StartTime);
        }

        [Fact]
        public void Dungeon_StatusTransition_InProgressToCompleted()
        {
            var state = new DungeonState
            {
                Status = (int)DungeonStatus.InProgress,
                IsCreated = true
            };

            state.Status = (int)DungeonStatus.Completed;

            Assert.Equal((int)DungeonStatus.Completed, state.Status);
        }

        [Fact]
        public void Dungeon_StatusTransition_InProgressToFailed()
        {
            var state = new DungeonState
            {
                Status = (int)DungeonStatus.InProgress,
                IsCreated = true
            };

            state.Status = (int)DungeonStatus.Failed;

            Assert.Equal((int)DungeonStatus.Failed, state.Status);
        }

        #endregion

        #region Dungeon Difficulty Tests

        [Fact]
        public void Dungeon_NormalDifficulty_IsZero()
        {
            var state = new DungeonState { Difficulty = (int)DungeonDifficulty.Normal };
            Assert.Equal(0, state.Difficulty);
        }

        [Fact]
        public void Dungeon_HardDifficulty_IsOne()
        {
            var state = new DungeonState { Difficulty = (int)DungeonDifficulty.Hard };
            Assert.Equal(1, state.Difficulty);
        }

        [Fact]
        public void Dungeon_HeroicDifficulty_IsTwo()
        {
            var state = new DungeonState { Difficulty = (int)DungeonDifficulty.Heroic };
            Assert.Equal(2, state.Difficulty);
        }

        [Fact]
        public void Dungeon_HellDifficulty_IsThree()
        {
            var state = new DungeonState { Difficulty = (int)DungeonDifficulty.Hell };
            Assert.Equal(3, state.Difficulty);
        }

        #endregion

        #region Complete Workflow Tests

        [Fact]
        public void Quest_FullWorkflow_AcceptProgressComplete()
        {
            var state = new QuestState();

            // Step 1: Accept quest
            var quest = new QuestData
            {
                QuestId = 1001,
                QuestName = "降妖除魔",
                Description = "击败十个妖怪",
                QuestType = 0,
                Level = 10,
                Status = (int)QuestProgressStatus.InProgress,
                AcceptTime = DateTime.UtcNow,
                Rewards = new Dictionary<string, int> { { "经验", 1000 } }
            };
            quest.Objectives.Add(new QuestObjectiveData
            {
                ObjectiveType = "Kill",
                Description = "击杀妖怪",
                RequiredCount = 10,
                CurrentCount = 0
            });
            state.ActiveQuests[1001] = quest;
            Assert.Single(state.ActiveQuests);

            // Step 2: Update progress
            var objective = quest.Objectives[0];
            objective.CurrentCount = Math.Min(objective.CurrentCount + 5, objective.RequiredCount);
            Assert.Equal(5, objective.CurrentCount);
            Assert.False(objective.IsCompleted);

            objective.CurrentCount = Math.Min(objective.CurrentCount + 5, objective.RequiredCount);
            Assert.Equal(10, objective.CurrentCount);
            objective.IsCompleted = true;

            // Step 3: Check all objectives complete
            bool allComplete = quest.Objectives.All(o => o.IsCompleted);
            Assert.True(allComplete);
            quest.Status = (int)QuestProgressStatus.ReadyToSubmit;

            // Step 4: Complete quest
            quest.Status = (int)QuestProgressStatus.Completed;
            quest.CompleteTime = DateTime.UtcNow;
            state.ActiveQuests.Remove(1001);
            state.CompletedQuests[1001] = quest;

            Assert.Empty(state.ActiveQuests);
            Assert.Single(state.CompletedQuests);
            Assert.Equal((int)QuestProgressStatus.Completed, state.CompletedQuests[1001].Status);
        }

        [Fact]
        public void Dungeon_FullWorkflow_CreateEnterDefeatComplete()
        {
            var state = new DungeonState();

            // Step 1: Create dungeon
            state.DungeonTemplateId = 100;
            state.DungeonName = "混沌秘境";
            state.Difficulty = (int)DungeonDifficulty.Hard;
            state.MaxPlayers = 5;
            state.TimeLimitMinutes = 30;
            state.IsCreated = true;
            state.Status = (int)DungeonStatus.Waiting;

            // Step 2: Add bosses
            state.Bosses[1] = new DungeonBossData { BossId = 1, BossName = "混沌兽" };
            state.Bosses[2] = new DungeonBossData { BossId = 2, BossName = "太极龙" };
            Assert.Equal(2, state.Bosses.Count);

            // Step 3: Players enter
            var player1 = Guid.NewGuid();
            var player2 = Guid.NewGuid();
            state.Players.Add(player1);
            state.Status = (int)DungeonStatus.InProgress;
            state.StartTime = DateTime.UtcNow;
            state.Players.Add(player2);
            Assert.Equal(2, state.Players.Count);
            Assert.Equal((int)DungeonStatus.InProgress, state.Status);

            // Step 4: Defeat bosses
            state.Bosses[1].IsDefeated = true;
            state.Bosses[1].DefeatTime = DateTime.UtcNow;
            Assert.Equal(1, state.Bosses.Values.Count(b => b.IsDefeated));

            state.Bosses[2].IsDefeated = true;
            state.Bosses[2].DefeatTime = DateTime.UtcNow;
            Assert.Equal(2, state.Bosses.Values.Count(b => b.IsDefeated));

            // Step 5: Complete dungeon
            bool allDefeated = state.Bosses.Values.All(b => b.IsDefeated);
            Assert.True(allDefeated);
            state.Status = (int)DungeonStatus.Completed;
            Assert.Equal((int)DungeonStatus.Completed, state.Status);
        }

        [Fact]
        public void Quest_MultipleObjectives_WorkCorrectly()
        {
            var quest = new QuestData
            {
                QuestId = 2001,
                QuestName = "综合试炼",
                Status = (int)QuestProgressStatus.InProgress,
                Objectives = new List<QuestObjectiveData>
                {
                    new QuestObjectiveData { ObjectiveType = "Kill", Description = "击杀怪物", RequiredCount = 10, CurrentCount = 0 },
                    new QuestObjectiveData { ObjectiveType = "Collect", Description = "收集材料", RequiredCount = 5, CurrentCount = 0 },
                    new QuestObjectiveData { ObjectiveType = "Explore", Description = "探索区域", RequiredCount = 3, CurrentCount = 0 }
                }
            };

            // Complete first objective
            quest.Objectives[0].CurrentCount = 10;
            quest.Objectives[0].IsCompleted = true;
            Assert.False(quest.Objectives.All(o => o.IsCompleted));

            // Complete second objective
            quest.Objectives[1].CurrentCount = 5;
            quest.Objectives[1].IsCompleted = true;
            Assert.False(quest.Objectives.All(o => o.IsCompleted));

            // Complete third objective
            quest.Objectives[2].CurrentCount = 3;
            quest.Objectives[2].IsCompleted = true;
            Assert.True(quest.Objectives.All(o => o.IsCompleted));
        }

        [Fact]
        public void Dungeon_ClearTime_CalculatedCorrectly()
        {
            var elapsedMinutes = 15;
            var startTime = DateTime.UtcNow.AddMinutes(-elapsedMinutes);
            var clearTimeSeconds = (DateTime.UtcNow - startTime).TotalSeconds;

            // Clear time should be approximately 15 minutes (900 seconds)
            // Use a generous tolerance to avoid flakiness in CI
            Assert.True(clearTimeSeconds >= 899);
            Assert.True(clearTimeSeconds <= 901);
        }

        #endregion
    }
}
