using Horizon.Orleans.Grains;
using Horizon.Orleans.Interface;
using Horizon.Game.Message.Network;

namespace Horizon.Game.Gateway.Tests
{
    /// <summary>
    /// AreaState, ActivityState 数据模型与逻辑单元测试
    /// 测试区域管理系统和活动系统的状态管理逻辑
    /// </summary>
    public class AreaActivityStateTests
    {
        #region AreaState Tests - 区域状态

        [Fact]
        public void AreaState_DefaultValues_AreCorrect()
        {
            var state = new AreaState();
            Assert.Equal("", state.AreaName);
            Assert.Equal("", state.AreaType);
            Assert.Equal(100, state.MaxPlayers);
            Assert.False(state.IsInitialized);
            Assert.NotNull(state.Instances);
            Assert.Empty(state.Instances);
            Assert.Equal(1, state.NextInstanceId);
        }

        [Fact]
        public void AreaState_Initialize_SetsProperties()
        {
            var state = new AreaState();
            state.AreaName = "龙虎山";
            state.AreaType = "野外";
            state.MaxPlayers = 200;
            state.IsInitialized = true;

            Assert.Equal("龙虎山", state.AreaName);
            Assert.Equal("野外", state.AreaType);
            Assert.Equal(200, state.MaxPlayers);
            Assert.True(state.IsInitialized);
        }

        [Fact]
        public void AreaState_AddInstance_IncrementsCount()
        {
            var state = new AreaState();
            var instance = new SceneInstanceInfo
            {
                InstanceId = state.NextInstanceId++,
                SceneName = "副本1",
                MaxPlayers = 10,
                IsActive = true,
                CreatedTime = DateTime.UtcNow
            };
            state.Instances[instance.InstanceId] = instance;
            Assert.Single(state.Instances);
        }

        [Fact]
        public void AreaState_AddMultipleInstances_TracksAll()
        {
            var state = new AreaState();
            for (int i = 0; i < 3; i++)
            {
                var instanceId = state.NextInstanceId++;
                state.Instances[instanceId] = new SceneInstanceInfo
                {
                    InstanceId = instanceId,
                    SceneName = $"副本{i + 1}",
                    MaxPlayers = 10
                };
            }
            Assert.Equal(3, state.Instances.Count);
        }

        [Fact]
        public void AreaState_RemoveInstance_DecreasesCount()
        {
            var state = new AreaState();
            var id1 = state.NextInstanceId++;
            var id2 = state.NextInstanceId++;
            state.Instances[id1] = new SceneInstanceInfo { InstanceId = id1 };
            state.Instances[id2] = new SceneInstanceInfo { InstanceId = id2 };
            state.Instances.Remove(id1);
            Assert.Single(state.Instances);
        }

        [Fact]
        public void AreaState_NextInstanceId_Increments()
        {
            var state = new AreaState();
            Assert.Equal(1, state.NextInstanceId);
            state.NextInstanceId++;
            Assert.Equal(2, state.NextInstanceId);
            state.NextInstanceId++;
            Assert.Equal(3, state.NextInstanceId);
        }

        #endregion

        #region SceneInstanceInfo Tests - 场景实例信息

        [Fact]
        public void SceneInstanceInfo_DefaultValues_AreCorrect()
        {
            var instance = new SceneInstanceInfo();
            Assert.Equal(0, instance.InstanceId);
            Assert.Equal("", instance.SceneName);
            Assert.Equal(0, instance.MaxPlayers);
            Assert.Equal(0, instance.CurrentPlayers);
            Assert.NotNull(instance.Players);
            Assert.Empty(instance.Players);
            Assert.True(instance.IsActive);
        }

        [Fact]
        public void SceneInstanceInfo_PlayerEnter_AddsToPlayers()
        {
            var instance = new SceneInstanceInfo { MaxPlayers = 10 };
            var playerId = Guid.NewGuid();
            instance.Players.Add(playerId);
            instance.CurrentPlayers = instance.Players.Count;

            Assert.Single(instance.Players);
            Assert.Equal(1, instance.CurrentPlayers);
            Assert.Contains(playerId, instance.Players);
        }

        [Fact]
        public void SceneInstanceInfo_PlayerLeave_RemovesFromPlayers()
        {
            var instance = new SceneInstanceInfo();
            var playerId = Guid.NewGuid();
            instance.Players.Add(playerId);
            instance.Players.Remove(playerId);
            instance.CurrentPlayers = instance.Players.Count;

            Assert.Empty(instance.Players);
            Assert.Equal(0, instance.CurrentPlayers);
        }

        [Fact]
        public void SceneInstanceInfo_MultiplePlayersEnter_TracksAll()
        {
            var instance = new SceneInstanceInfo { MaxPlayers = 50 };
            for (int i = 0; i < 5; i++)
            {
                instance.Players.Add(Guid.NewGuid());
            }
            instance.CurrentPlayers = instance.Players.Count;

            Assert.Equal(5, instance.Players.Count);
            Assert.Equal(5, instance.CurrentPlayers);
        }

        [Fact]
        public void SceneInstanceInfo_CapacityCheck_WorksCorrectly()
        {
            var instance = new SceneInstanceInfo { MaxPlayers = 3 };
            for (int i = 0; i < 3; i++)
            {
                instance.Players.Add(Guid.NewGuid());
            }
            Assert.Equal(instance.MaxPlayers, instance.Players.Count);
        }

        [Fact]
        public void SceneInstanceInfo_DuplicatePlayer_NotAdded()
        {
            var instance = new SceneInstanceInfo();
            var playerId = Guid.NewGuid();
            instance.Players.Add(playerId);
            bool added = instance.Players.Add(playerId);
            Assert.False(added);
            Assert.Single(instance.Players);
        }

        [Fact]
        public void SceneInstanceInfo_Deactivate_SetsInactive()
        {
            var instance = new SceneInstanceInfo { IsActive = true };
            instance.IsActive = false;
            instance.Players.Clear();
            instance.CurrentPlayers = 0;

            Assert.False(instance.IsActive);
            Assert.Empty(instance.Players);
        }

        #endregion

        #region TeleportResult Tests - 传送结果

        [Fact]
        public void TeleportResult_DefaultValues_AreCorrect()
        {
            var result = new TeleportResult();
            Assert.False(result.Success);
            Assert.Equal("", result.Message);
            Assert.Equal(0, result.TargetAreaId);
            Assert.Equal(0, result.TargetInstanceId);
        }

        [Fact]
        public void TeleportResult_SuccessfulTeleport_SetsProperties()
        {
            var result = new TeleportResult
            {
                Success = true,
                Message = "传送成功",
                TargetAreaId = 5,
                TargetInstanceId = 10
            };

            Assert.True(result.Success);
            Assert.Equal("传送成功", result.Message);
            Assert.Equal(5, result.TargetAreaId);
            Assert.Equal(10, result.TargetInstanceId);
        }

        #endregion

        #region AreaInfo Tests - 区域信息

        [Fact]
        public void AreaInfo_DefaultValues_AreCorrect()
        {
            var info = new AreaInfo();
            Assert.Equal(0, info.AreaId);
            Assert.Equal("", info.AreaName);
            Assert.Equal("", info.AreaType);
            Assert.Equal(0, info.MaxPlayers);
            Assert.Equal(0, info.TotalPlayers);
            Assert.Equal(0, info.InstanceCount);
            Assert.False(info.IsInitialized);
        }

        [Fact]
        public void AreaInfo_SetProperties_WorksCorrectly()
        {
            var info = new AreaInfo
            {
                AreaId = 1,
                AreaName = "龙虎山",
                AreaType = "野外",
                MaxPlayers = 200,
                TotalPlayers = 50,
                InstanceCount = 3,
                IsInitialized = true
            };

            Assert.Equal(1, info.AreaId);
            Assert.Equal("龙虎山", info.AreaName);
            Assert.Equal(200, info.MaxPlayers);
            Assert.Equal(50, info.TotalPlayers);
            Assert.Equal(3, info.InstanceCount);
            Assert.True(info.IsInitialized);
        }

        #endregion

        #region ActivityState Tests - 活动状态

        [Fact]
        public void ActivityState_DefaultValues_AreCorrect()
        {
            var state = new ActivityState();
            Assert.Equal("", state.Name);
            Assert.Equal("", state.Description);
            Assert.Equal(0, state.MaxParticipants);
            Assert.Equal((int)ActivityStatus.NotStarted, state.Status);
            Assert.False(state.IsCreated);
            Assert.NotNull(state.Participants);
            Assert.Empty(state.Participants);
        }

        [Fact]
        public void ActivityState_CreateActivity_SetsProperties()
        {
            var state = new ActivityState();
            var start = DateTime.UtcNow;
            var end = start.AddHours(2);

            state.Name = "武林大会";
            state.Description = "年度武林盛会";
            state.StartTime = start;
            state.EndTime = end;
            state.MaxParticipants = 100;
            state.IsCreated = true;
            state.Status = (int)ActivityStatus.Active;

            Assert.Equal("武林大会", state.Name);
            Assert.Equal("年度武林盛会", state.Description);
            Assert.Equal(start, state.StartTime);
            Assert.Equal(end, state.EndTime);
            Assert.Equal(100, state.MaxParticipants);
            Assert.True(state.IsCreated);
            Assert.Equal((int)ActivityStatus.Active, state.Status);
        }

        [Fact]
        public void ActivityState_AddParticipant_IncrementsCount()
        {
            var state = new ActivityState();
            var playerId = Guid.NewGuid();
            state.Participants[playerId] = new ActivityParticipation
            {
                PlayerId = playerId,
                JoinTime = DateTime.UtcNow,
                IsActive = true
            };
            Assert.Single(state.Participants);
        }

        [Fact]
        public void ActivityState_AddMultipleParticipants_TracksAll()
        {
            var state = new ActivityState();
            for (int i = 0; i < 5; i++)
            {
                var playerId = Guid.NewGuid();
                state.Participants[playerId] = new ActivityParticipation
                {
                    PlayerId = playerId,
                    JoinTime = DateTime.UtcNow,
                    IsActive = true
                };
            }
            Assert.Equal(5, state.Participants.Count);
        }

        [Fact]
        public void ActivityState_ParticipantLeave_SetsInactive()
        {
            var state = new ActivityState();
            var playerId = Guid.NewGuid();
            state.Participants[playerId] = new ActivityParticipation
            {
                PlayerId = playerId,
                JoinTime = DateTime.UtcNow,
                IsActive = true
            };

            state.Participants[playerId].IsActive = false;
            Assert.False(state.Participants[playerId].IsActive);
        }

        [Fact]
        public void ActivityState_ActiveParticipantCount_FiltersInactive()
        {
            var state = new ActivityState();
            var p1 = Guid.NewGuid();
            var p2 = Guid.NewGuid();
            var p3 = Guid.NewGuid();

            state.Participants[p1] = new ActivityParticipation { PlayerId = p1, IsActive = true };
            state.Participants[p2] = new ActivityParticipation { PlayerId = p2, IsActive = false };
            state.Participants[p3] = new ActivityParticipation { PlayerId = p3, IsActive = true };

            var activeCount = state.Participants.Count(p => p.Value.IsActive);
            Assert.Equal(2, activeCount);
        }

        [Fact]
        public void ActivityState_EndActivity_SetsEndedStatus()
        {
            var state = new ActivityState();
            state.IsCreated = true;
            state.Status = (int)ActivityStatus.Active;

            state.Status = (int)ActivityStatus.Ended;
            Assert.Equal((int)ActivityStatus.Ended, state.Status);
        }

        #endregion

        #region ActivityParticipation Tests - 活动参与记录

        [Fact]
        public void ActivityParticipation_DefaultValues_AreCorrect()
        {
            var participation = new ActivityParticipation();
            Assert.Equal(Guid.Empty, participation.PlayerId);
            Assert.True(participation.IsActive);
            Assert.NotNull(participation.Rewards);
            Assert.Empty(participation.Rewards);
        }

        [Fact]
        public void ActivityParticipation_AddReward_TracksReward()
        {
            var participation = new ActivityParticipation
            {
                PlayerId = Guid.NewGuid(),
                JoinTime = DateTime.UtcNow,
                IsActive = true
            };

            participation.Rewards.Add(new RewardRecord
            {
                RewardTemplateId = 100,
                Quantity = 5,
                DistributedTime = DateTime.UtcNow
            });

            Assert.Single(participation.Rewards);
            Assert.Equal(100, participation.Rewards[0].RewardTemplateId);
            Assert.Equal(5, participation.Rewards[0].Quantity);
        }

        [Fact]
        public void ActivityParticipation_MultipleRewards_TrackedIndependently()
        {
            var participation = new ActivityParticipation();
            participation.Rewards.Add(new RewardRecord { RewardTemplateId = 100, Quantity = 5 });
            participation.Rewards.Add(new RewardRecord { RewardTemplateId = 101, Quantity = 10 });
            participation.Rewards.Add(new RewardRecord { RewardTemplateId = 102, Quantity = 1 });

            Assert.Equal(3, participation.Rewards.Count);
        }

        #endregion

        #region RewardRecord Tests - 奖励记录

        [Fact]
        public void RewardRecord_DefaultValues_AreCorrect()
        {
            var record = new RewardRecord();
            Assert.Equal(0, record.RewardTemplateId);
            Assert.Equal(0, record.Quantity);
        }

        [Fact]
        public void RewardRecord_SetProperties_WorksCorrectly()
        {
            var time = DateTime.UtcNow;
            var record = new RewardRecord
            {
                RewardTemplateId = 200,
                Quantity = 10,
                DistributedTime = time
            };

            Assert.Equal(200, record.RewardTemplateId);
            Assert.Equal(10, record.Quantity);
            Assert.Equal(time, record.DistributedTime);
        }

        #endregion

        #region ActivityStatus Tests - 活动状态枚举

        [Fact]
        public void ActivityStatus_Values_AreCorrect()
        {
            Assert.Equal(0, (int)ActivityStatus.NotStarted);
            Assert.Equal(1, (int)ActivityStatus.Active);
            Assert.Equal(2, (int)ActivityStatus.Ended);
            Assert.Equal(3, (int)ActivityStatus.Cancelled);
        }

        #endregion

        #region SkillGrain Circular Dependency Tests - 技能循环依赖检测

        [Fact]
        public void HasCircularDependency_NoCircle_ReturnsFalse()
        {
            var deps = new Dictionary<int, List<int>>
            {
                { 1, new List<int>() },
                { 2, new List<int> { 1 } },
                { 3, new List<int> { 2 } }
            };

            Assert.False(SkillGrain.HasCircularDependency(3, deps));
        }

        [Fact]
        public void HasCircularDependency_DirectCircle_ReturnsTrue()
        {
            var deps = new Dictionary<int, List<int>>
            {
                { 1, new List<int> { 2 } },
                { 2, new List<int> { 1 } }
            };

            Assert.True(SkillGrain.HasCircularDependency(1, deps));
        }

        [Fact]
        public void HasCircularDependency_IndirectCircle_ReturnsTrue()
        {
            var deps = new Dictionary<int, List<int>>
            {
                { 1, new List<int> { 2 } },
                { 2, new List<int> { 3 } },
                { 3, new List<int> { 1 } }
            };

            Assert.True(SkillGrain.HasCircularDependency(1, deps));
        }

        [Fact]
        public void HasCircularDependency_SelfReference_ReturnsTrue()
        {
            var deps = new Dictionary<int, List<int>>
            {
                { 1, new List<int> { 1 } }
            };

            Assert.True(SkillGrain.HasCircularDependency(1, deps));
        }

        [Fact]
        public void HasCircularDependency_EmptyDependencies_ReturnsFalse()
        {
            var deps = new Dictionary<int, List<int>>();
            Assert.False(SkillGrain.HasCircularDependency(1, deps));
        }

        [Fact]
        public void HasCircularDependency_LinearChain_ReturnsFalse()
        {
            var deps = new Dictionary<int, List<int>>
            {
                { 1, new List<int>() },
                { 2, new List<int> { 1 } },
                { 3, new List<int> { 2 } },
                { 4, new List<int> { 3 } },
                { 5, new List<int> { 4 } }
            };

            Assert.False(SkillGrain.HasCircularDependency(5, deps));
        }

        [Fact]
        public void HasCircularDependency_DiamondShape_NoCircle_ReturnsFalse()
        {
            // 1 -> 2, 1 -> 3, 2 -> 4, 3 -> 4 (diamond shape, no cycle)
            var deps = new Dictionary<int, List<int>>
            {
                { 1, new List<int> { 2, 3 } },
                { 2, new List<int> { 4 } },
                { 3, new List<int> { 4 } },
                { 4, new List<int>() }
            };

            Assert.False(SkillGrain.HasCircularDependency(1, deps));
        }

        #endregion

        #region CraftingGrain Quality Tests - 合成品质系统

        [Fact]
        public void CalculateCraftingQuality_ReturnsValidRange()
        {
            // Run multiple times to ensure output is always within valid range
            for (int i = 0; i < 100; i++)
            {
                int quality = CraftingGrain.CalculateCraftingQuality(0.5f);
                Assert.InRange(quality, 0, 4);
            }
        }

        [Fact]
        public void CalculateCraftingQuality_HighSuccessRate_MostlyLowQuality()
        {
            // With high success rate (easy recipe), most outputs should be low quality
            int highQualityCount = 0;
            int totalTrials = 1000;

            for (int i = 0; i < totalTrials; i++)
            {
                int quality = CraftingGrain.CalculateCraftingQuality(1.0f);
                if (quality >= 3) highQualityCount++;
            }

            // High quality (epic + legendary) should be less than 20% for easy recipes
            Assert.True(highQualityCount < totalTrials * 0.20,
                $"High quality count {highQualityCount} exceeded 20% of {totalTrials}");
        }

        [Fact]
        public void CraftingResult_Quality_CanBeSet()
        {
            var result = new CraftingResult
            {
                Success = true,
                RecipeId = 1,
                Message = "合成成功（品质：3）",
                OutputItemId = 1000,
                Quality = 3
            };

            Assert.Equal(3, result.Quality);
        }

        [Fact]
        public void CraftingHistoryEntry_Quality_CanBeSet()
        {
            var entry = new CraftingHistoryEntry
            {
                RecipeId = 1,
                Success = true,
                Timestamp = DateTime.UtcNow,
                OutputItemId = 1000,
                Quality = 2
            };

            Assert.Equal(2, entry.Quality);
        }

        [Fact]
        public void CraftingResult_DefaultQuality_IsZero()
        {
            var result = new CraftingResult();
            Assert.Equal(0, result.Quality);
        }

        [Fact]
        public void CraftingHistoryEntry_DefaultQuality_IsZero()
        {
            var entry = new CraftingHistoryEntry();
            Assert.Equal(0, entry.Quality);
        }

        #endregion

        #region InventoryState Equipment Tests - 装备系统

        [Fact]
        public void InventoryState_DefaultEquippedItems_IsEmpty()
        {
            var state = new InventoryState();
            Assert.NotNull(state.EquippedItems);
            Assert.Empty(state.EquippedItems);
        }

        [Fact]
        public void InventoryState_EquipItem_AddsToEquippedItems()
        {
            var state = new InventoryState();
            state.EquippedItems[0] = 1; // slot 0 -> item 1
            Assert.Single(state.EquippedItems);
            Assert.Equal(1, state.EquippedItems[0]);
        }

        [Fact]
        public void InventoryState_UnequipItem_RemovesFromEquippedItems()
        {
            var state = new InventoryState();
            state.EquippedItems[0] = 1;
            state.EquippedItems.Remove(0);
            Assert.Empty(state.EquippedItems);
        }

        [Fact]
        public void InventoryState_MultipleSlots_TrackedIndependently()
        {
            var state = new InventoryState();
            state.EquippedItems[0] = 1;  // 武器
            state.EquippedItems[1] = 2;  // 头盔
            state.EquippedItems[2] = 3;  // 铠甲
            Assert.Equal(3, state.EquippedItems.Count);
        }

        [Fact]
        public void InventoryState_SwapEquipment_UpdatesSlot()
        {
            var state = new InventoryState();
            state.EquippedItems[0] = 1;
            state.EquippedItems[0] = 2; // Replace
            Assert.Equal(2, state.EquippedItems[0]);
        }

        #endregion
    }
}
