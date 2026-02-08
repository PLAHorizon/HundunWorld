using Horizon.Orleans.Interface;
using Horizon.Orleans.Grains;
using Horizon.Game.Message.Network;
using GameEventType = Horizon.Orleans.Interface.GameEventType;

namespace Horizon.Game.Gateway.Tests
{
    /// <summary>
    /// 组队副本入口、队伍状态同步、事件驱动架构扩展、Grain接口版本管理单元测试
    /// </summary>
    public class TeamDungeonEventVersionTests
    {
        #region TeamState StateVersion Tests - 队伍状态版本同步

        [Fact]
        public void TeamState_StateVersion_DefaultIsZero()
        {
            var state = new TeamState();
            Assert.Equal(0, state.StateVersion);
        }

        [Fact]
        public void TeamState_StateVersion_CanBeIncremented()
        {
            var state = new TeamState();
            state.StateVersion++;
            Assert.Equal(1, state.StateVersion);
        }

        [Fact]
        public void TeamState_StateVersion_IncrementOnMemberChange()
        {
            var state = new TeamState();
            var initialVersion = state.StateVersion;

            // Simulate member join
            var memberId = Guid.NewGuid();
            state.Members[memberId] = new TeamMemberState { MemberId = memberId };
            state.StateVersion++;

            Assert.Equal(initialVersion + 1, state.StateVersion);
        }

        [Fact]
        public void TeamState_StateVersion_IncrementOnMultipleChanges()
        {
            var state = new TeamState();

            // Simulate create
            state.IsCreated = true;
            state.StateVersion++;

            // Simulate member join
            state.Members[Guid.NewGuid()] = new TeamMemberState();
            state.StateVersion++;

            // Simulate member leave
            var member = state.Members.Keys.First();
            state.Members.Remove(member);
            state.StateVersion++;

            Assert.Equal(3, state.StateVersion);
        }

        [Fact]
        public void TeamState_StateVersion_IncrementOnLeaderTransfer()
        {
            var state = new TeamState();
            var oldLeaderId = Guid.NewGuid();
            var newLeaderId = Guid.NewGuid();

            state.LeaderId = oldLeaderId;
            state.StateVersion++;

            // Transfer
            state.LeaderId = newLeaderId;
            state.StateVersion++;

            Assert.Equal(2, state.StateVersion);
            Assert.Equal(newLeaderId, state.LeaderId);
        }

        [Fact]
        public void TeamState_StateVersion_IncrementOnDisband()
        {
            var state = new TeamState();
            state.IsCreated = true;
            state.StateVersion++;

            // Disband
            state.IsCreated = false;
            state.Members.Clear();
            state.StateVersion++;

            Assert.Equal(2, state.StateVersion);
            Assert.False(state.IsCreated);
        }

        #endregion

        #region TeamState CurrentDungeonId Tests - 队伍副本关联

        [Fact]
        public void TeamState_CurrentDungeonId_DefaultIsNull()
        {
            var state = new TeamState();
            Assert.Null(state.CurrentDungeonId);
        }

        [Fact]
        public void TeamState_CurrentDungeonId_CanBeSet()
        {
            var state = new TeamState();
            var dungeonId = Guid.NewGuid();
            state.CurrentDungeonId = dungeonId;

            Assert.Equal(dungeonId, state.CurrentDungeonId);
        }

        [Fact]
        public void TeamState_CurrentDungeonId_CanBeCleared()
        {
            var state = new TeamState();
            state.CurrentDungeonId = Guid.NewGuid();
            state.CurrentDungeonId = null;

            Assert.Null(state.CurrentDungeonId);
        }

        [Fact]
        public void TeamState_Disband_ClearsDungeonId()
        {
            var state = new TeamState
            {
                IsCreated = true,
                CurrentDungeonId = Guid.NewGuid()
            };

            // Simulate disband
            state.IsCreated = false;
            state.Members.Clear();
            state.CurrentDungeonId = null;

            Assert.Null(state.CurrentDungeonId);
            Assert.False(state.IsCreated);
        }

        #endregion

        #region TeamDungeonResult Tests - 组队副本结果

        [Fact]
        public void TeamDungeonResult_DefaultValues_AreCorrect()
        {
            var result = new TeamDungeonResult();

            Assert.False(result.Success);
            Assert.Equal("", result.Message);
            Assert.Equal(Guid.Empty, result.DungeonInstanceId);
            Assert.NotNull(result.EnteredMembers);
            Assert.Empty(result.EnteredMembers);
            Assert.Equal(0, result.DungeonTemplateId);
            Assert.Equal(0, result.Difficulty);
        }

        [Fact]
        public void TeamDungeonResult_SuccessResult_HasAllProperties()
        {
            var dungeonId = Guid.NewGuid();
            var members = new List<Guid> { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };

            var result = new TeamDungeonResult
            {
                Success = true,
                Message = "组队进入副本成功",
                DungeonInstanceId = dungeonId,
                EnteredMembers = members,
                DungeonTemplateId = 1001,
                Difficulty = 2
            };

            Assert.True(result.Success);
            Assert.Equal("组队进入副本成功", result.Message);
            Assert.Equal(dungeonId, result.DungeonInstanceId);
            Assert.Equal(3, result.EnteredMembers.Count);
            Assert.Equal(1001, result.DungeonTemplateId);
            Assert.Equal(2, result.Difficulty);
        }

        [Fact]
        public void TeamDungeonResult_FailureResult_HasMessage()
        {
            var result = new TeamDungeonResult
            {
                Success = false,
                Message = "仅队长可发起组队副本"
            };

            Assert.False(result.Success);
            Assert.Equal("仅队长可发起组队副本", result.Message);
            Assert.Empty(result.EnteredMembers);
        }

        [Fact]
        public void TeamDungeonResult_HasSerializableAttribute()
        {
            var type = typeof(TeamDungeonResult);
            Assert.True(type.IsSerializable);
        }

        [Fact]
        public void TeamDungeonResult_HasGenerateSerializerAttribute()
        {
            var type = typeof(TeamDungeonResult);
            var attributes = type.GetCustomAttributes(false);
            Assert.Contains(attributes, a => a.GetType().Name == "GenerateSerializerAttribute");
        }

        [Fact]
        public void TeamDungeonResult_HasMemoryPackableAttribute()
        {
            var type = typeof(TeamDungeonResult);
            var attributes = type.GetCustomAttributes(false);
            Assert.Contains(attributes, a => a.GetType().Name == "MemoryPackableAttribute");
        }

        #endregion

        #region DungeonState TeamId Tests - 副本队伍关联

        [Fact]
        public void DungeonState_TeamId_DefaultIsNull()
        {
            var state = new DungeonState();
            Assert.Null(state.TeamId);
        }

        [Fact]
        public void DungeonState_TeamId_CanBeSet()
        {
            var state = new DungeonState();
            var teamId = Guid.NewGuid();
            state.TeamId = teamId;

            Assert.Equal(teamId, state.TeamId);
        }

        [Fact]
        public void DungeonData_TeamId_DefaultIsNull()
        {
            var data = new DungeonData();
            Assert.Null(data.TeamId);
        }

        [Fact]
        public void DungeonData_TeamId_CanBeSet()
        {
            var data = new DungeonData();
            var teamId = Guid.NewGuid();
            data.TeamId = teamId;

            Assert.Equal(teamId, data.TeamId);
        }

        #endregion

        #region New GameEventType Tests - 新增事件类型

        [Theory]
        [InlineData(GameEventType.TeamMemberJoined, 304)]
        [InlineData(GameEventType.TeamMemberLeft, 305)]
        [InlineData(GameEventType.TeamDisbanded, 306)]
        [InlineData(GameEventType.TeamDungeonEntered, 307)]
        public void GameEventType_NewSocialEvents_HaveCorrectValues(GameEventType eventType, int expectedValue)
        {
            Assert.Equal(expectedValue, (int)eventType);
        }

        [Fact]
        public void GameEventType_NewSocialEventsAreInSocialRange()
        {
            Assert.InRange((int)GameEventType.TeamMemberJoined, 300, 399);
            Assert.InRange((int)GameEventType.TeamMemberLeft, 300, 399);
            Assert.InRange((int)GameEventType.TeamDisbanded, 300, 399);
            Assert.InRange((int)GameEventType.TeamDungeonEntered, 300, 399);
        }

        [Fact]
        public void GameEventType_AllValues_AreUnique()
        {
            var values = Enum.GetValues<GameEventType>().Select(e => (int)e).ToList();
            Assert.Equal(values.Count, values.Distinct().Count());
        }

        [Fact]
        public void GameEvent_TeamDungeonEnteredEvent_CanBeCreated()
        {
            var gameEvent = new GameEvent
            {
                EventType = GameEventType.TeamDungeonEntered,
                CharacterId = 12345,
                Description = "组队进入副本",
                Metadata = new Dictionary<string, string>
                {
                    { "DungeonName", "火焰山副本" },
                    { "Difficulty", "Heroic" },
                    { "TeamSize", "5" },
                    { "DungeonInstanceId", Guid.NewGuid().ToString() }
                }
            };

            Assert.Equal(GameEventType.TeamDungeonEntered, gameEvent.EventType);
            Assert.Equal(12345UL, gameEvent.CharacterId);
            Assert.Equal(4, gameEvent.Metadata.Count);
            Assert.Equal("火焰山副本", gameEvent.Metadata["DungeonName"]);
        }

        [Fact]
        public void GameEvent_TeamMemberJoinedEvent_CanBeCreated()
        {
            var gameEvent = new GameEvent
            {
                EventType = GameEventType.TeamMemberJoined,
                CharacterId = 11111,
                Description = "成员加入队伍",
                Metadata = new Dictionary<string, string>
                {
                    { "TeamName", "龙虎队" },
                    { "MemberCount", "3" }
                }
            };

            Assert.Equal(GameEventType.TeamMemberJoined, gameEvent.EventType);
            Assert.Equal("龙虎队", gameEvent.Metadata["TeamName"]);
        }

        [Fact]
        public void GameEvent_TeamDisbandedEvent_CanBeCreated()
        {
            var gameEvent = new GameEvent
            {
                EventType = GameEventType.TeamDisbanded,
                CharacterId = 22222,
                Description = "队伍解散"
            };

            Assert.Equal(GameEventType.TeamDisbanded, gameEvent.EventType);
        }

        #endregion

        #region ITeamGrain Interface Tests - 接口方法验证

        [Fact]
        public void ITeamGrain_HasEnterDungeonAsTeamAsyncMethod()
        {
            var interfaceType = typeof(ITeamGrain);
            var method = interfaceType.GetMethod("EnterDungeonAsTeamAsync");

            Assert.NotNull(method);
            Assert.Equal(typeof(System.Threading.Tasks.Task<TeamDungeonResult>), method.ReturnType);

            var parameters = method.GetParameters();
            Assert.Equal(5, parameters.Length);
            Assert.Equal(typeof(Guid), parameters[0].ParameterType);     // leaderId
            Assert.Equal(typeof(int), parameters[1].ParameterType);      // dungeonTemplateId
            Assert.Equal(typeof(string), parameters[2].ParameterType);   // dungeonName
            Assert.Equal(typeof(int), parameters[3].ParameterType);      // difficulty
            Assert.Equal(typeof(int), parameters[4].ParameterType);      // timeLimitMinutes
        }

        [Fact]
        public void ITeamGrain_HasGetTeamStateVersionAsyncMethod()
        {
            var interfaceType = typeof(ITeamGrain);
            var method = interfaceType.GetMethod("GetTeamStateVersionAsync");

            Assert.NotNull(method);
            Assert.Equal(typeof(System.Threading.Tasks.Task<long>), method.ReturnType);

            var parameters = method.GetParameters();
            Assert.Empty(parameters);
        }

        [Fact]
        public void ITeamGrain_HasAllExpectedMethods()
        {
            var interfaceType = typeof(ITeamGrain);
            var expectedMethods = new[]
            {
                "CreateTeamAsync",
                "JoinTeamAsync",
                "LeaveTeamAsync",
                "KickMemberAsync",
                "TransferLeaderAsync",
                "GetTeamInfoAsync",
                "GetMembersAsync",
                "DisbandTeamAsync",
                "EnterDungeonAsTeamAsync",
                "GetTeamStateVersionAsync"
            };

            var methods = interfaceType.GetMethods().Select(m => m.Name).ToList();
            foreach (var expectedMethod in expectedMethods)
            {
                Assert.Contains(expectedMethod, methods);
            }
        }

        #endregion

        #region Grain Interface Versioning Tests - 接口版本管理

        [Theory]
        [InlineData(typeof(ICombatGrain))]
        [InlineData(typeof(IQuestGrain))]
        [InlineData(typeof(IDungeonGrain))]
        [InlineData(typeof(ISocialGrain))]
        [InlineData(typeof(IGuildGrain))]
        [InlineData(typeof(IMapGrain))]
        [InlineData(typeof(ITeamGrain))]
        [InlineData(typeof(IInventoryGrain))]
        [InlineData(typeof(ISkillGrain))]
        [InlineData(typeof(ICraftingGrain))]
        [InlineData(typeof(IWuxingAlchemyGrain))]
        [InlineData(typeof(ITradeGrain))]
        [InlineData(typeof(IMarketGrain))]
        [InlineData(typeof(IAreaGrain))]
        [InlineData(typeof(IActivityGrain))]
        [InlineData(typeof(IMessageRouterGrain))]
        [InlineData(typeof(IMessageChannelGrain))]
        [InlineData(typeof(IGuildChannelGrain))]
        [InlineData(typeof(ITeamChannelGrain))]
        [InlineData(typeof(ISystemChannelGrain))]
        [InlineData(typeof(ISocialSystemMonitorGrain))]
        [InlineData(typeof(IPassportGrain))]
        [InlineData(typeof(ICharacterGrain))]
        [InlineData(typeof(IGameServerGrain))]
        [InlineData(typeof(IGameGrain))]
        public void GrainInterface_HasVersionAttribute(Type grainInterfaceType)
        {
            var versionAttr = grainInterfaceType.GetCustomAttributes(false)
                .FirstOrDefault(a => a.GetType().FullName == "Orleans.CodeGeneration.VersionAttribute");

            Assert.NotNull(versionAttr);
        }

        [Fact]
        public void ITeamGrain_HasVersion2()
        {
            var type = typeof(ITeamGrain);
            var versionAttr = type.GetCustomAttributes(false)
                .FirstOrDefault(a => a.GetType().FullName == "Orleans.CodeGeneration.VersionAttribute");

            Assert.NotNull(versionAttr);
            var versionProperty = versionAttr!.GetType().GetProperty("Version");
            Assert.NotNull(versionProperty);
            var version = versionProperty!.GetValue(versionAttr);
            Assert.Equal((ushort)2, version);
        }

        [Theory]
        [InlineData(typeof(ICombatGrain), (ushort)1)]
        [InlineData(typeof(IQuestGrain), (ushort)1)]
        [InlineData(typeof(IDungeonGrain), (ushort)1)]
        [InlineData(typeof(ISocialGrain), (ushort)1)]
        [InlineData(typeof(ITeamGrain), (ushort)2)]
        public void GrainInterface_HasExpectedVersion(Type grainType, ushort expectedVersion)
        {
            var versionAttr = grainType.GetCustomAttributes(false)
                .FirstOrDefault(a => a.GetType().FullName == "Orleans.CodeGeneration.VersionAttribute");

            Assert.NotNull(versionAttr);
            var versionProperty = versionAttr!.GetType().GetProperty("Version");
            Assert.NotNull(versionProperty);
            var version = versionProperty!.GetValue(versionAttr);
            Assert.Equal(expectedVersion, version);
        }

        #endregion

        #region Event-Driven Architecture Integration Tests - 事件驱动架构

        [Fact]
        public void GameEventType_TotalEventCount_Is22()
        {
            var count = Enum.GetValues<GameEventType>().Length;
            Assert.Equal(22, count);
        }

        [Fact]
        public void GameEvent_CombatResultNotificationEvent_CanBeCreated()
        {
            // 战斗结果通知解耦 — 通过事件流异步通知
            var combatResultEvent = new GameEvent
            {
                EventType = GameEventType.CombatDamageDealt,
                CharacterId = 12345,
                Description = "战斗结果通知",
                Metadata = new Dictionary<string, string>
                {
                    { "TargetId", "67890" },
                    { "Damage", "500" },
                    { "IsCritical", "true" },
                    { "Element", "Fire" }
                }
            };

            Assert.Equal(GameEventType.CombatDamageDealt, combatResultEvent.EventType);
            Assert.Contains("TargetId", combatResultEvent.Metadata.Keys);
            Assert.Contains("Damage", combatResultEvent.Metadata.Keys);
            Assert.Contains("IsCritical", combatResultEvent.Metadata.Keys);
        }

        [Fact]
        public void GameEvent_AsyncStatisticsEvent_CanBeCreated()
        {
            // 异步处理非关键路径（统计）
            var statsEvent = new GameEvent
            {
                EventType = GameEventType.CombatPlayerKill,
                CharacterId = 11111,
                Description = "击杀统计",
                Metadata = new Dictionary<string, string>
                {
                    { "VictimId", "22222" },
                    { "TotalKills", "100" },
                    { "SessionDuration", "3600" }
                }
            };

            Assert.Equal(GameEventType.CombatPlayerKill, statsEvent.EventType);
            Assert.Equal(3, statsEvent.Metadata.Count);
        }

        [Fact]
        public void GameStreamNamespaces_AllFourNamespaces_Defined()
        {
            Assert.Equal("CharacterEvents", GameStreamNamespaces.CharacterEvents);
            Assert.Equal("CombatEvents", GameStreamNamespaces.CombatEvents);
            Assert.Equal("SocialEvents", GameStreamNamespaces.SocialEvents);
            Assert.Equal("SystemEvents", GameStreamNamespaces.SystemEvents);
        }

        #endregion

        #region TeamDungeonResult Complete Workflow Tests - 完整工作流

        [Fact]
        public void TeamDungeonWorkflow_CreateTeamAndEnterDungeon_DataModel()
        {
            // Step 1: Create team state
            var state = new TeamState();
            var leaderId = Guid.NewGuid();
            var member1 = Guid.NewGuid();
            var member2 = Guid.NewGuid();

            state.TeamName = "副本挑战队";
            state.LeaderId = leaderId;
            state.IsCreated = true;
            state.StateVersion++;

            state.Members[leaderId] = new TeamMemberState { MemberId = leaderId, IsLeader = true };
            state.StateVersion++;
            state.Members[member1] = new TeamMemberState { MemberId = member1, IsLeader = false };
            state.StateVersion++;
            state.Members[member2] = new TeamMemberState { MemberId = member2, IsLeader = false };
            state.StateVersion++;

            Assert.Equal(3, state.Members.Count);
            Assert.Equal(4, state.StateVersion);

            // Step 2: Enter dungeon
            var dungeonId = Guid.NewGuid();
            state.CurrentDungeonId = dungeonId;
            state.StateVersion++;

            var result = new TeamDungeonResult
            {
                Success = true,
                Message = "组队进入副本成功",
                DungeonInstanceId = dungeonId,
                EnteredMembers = new List<Guid> { leaderId, member1, member2 },
                DungeonTemplateId = 2001,
                Difficulty = (int)DungeonDifficulty.Heroic
            };

            Assert.True(result.Success);
            Assert.Equal(3, result.EnteredMembers.Count);
            Assert.Equal(dungeonId, state.CurrentDungeonId);
            Assert.Equal(5, state.StateVersion);
        }

        [Fact]
        public void TeamDungeonWorkflow_DungeonWithTeamId_DataModel()
        {
            // DungeonState should track team association
            var dungeonState = new DungeonState
            {
                DungeonTemplateId = 1001,
                DungeonName = "幽冥地宫",
                Difficulty = (int)DungeonDifficulty.Hard,
                MaxPlayers = 5,
                IsCreated = true,
                TeamId = Guid.NewGuid()
            };

            Assert.NotNull(dungeonState.TeamId);
            Assert.Equal("幽冥地宫", dungeonState.DungeonName);
            Assert.Equal((int)DungeonDifficulty.Hard, dungeonState.Difficulty);
        }

        [Fact]
        public void TeamDungeonWorkflow_AllDifficulties_AreValid()
        {
            foreach (DungeonDifficulty difficulty in Enum.GetValues<DungeonDifficulty>())
            {
                var result = new TeamDungeonResult
                {
                    Success = true,
                    Difficulty = (int)difficulty
                };

                Assert.InRange(result.Difficulty, 0, 3);
            }
        }

        [Fact]
        public void TeamDungeonWorkflow_EmptyTeam_FailureResult()
        {
            var result = new TeamDungeonResult
            {
                Success = false,
                Message = "组队副本至少需要2名队员"
            };

            Assert.False(result.Success);
            Assert.Equal("组队副本至少需要2名队员", result.Message);
            Assert.Empty(result.EnteredMembers);
        }

        [Fact]
        public void TeamDungeonWorkflow_NonLeader_FailureResult()
        {
            var result = new TeamDungeonResult
            {
                Success = false,
                Message = "仅队长可发起组队副本"
            };

            Assert.False(result.Success);
            Assert.Contains("队长", result.Message);
        }

        [Fact]
        public void TeamDungeonWorkflow_AlreadyInDungeon_FailureResult()
        {
            var state = new TeamState
            {
                CurrentDungeonId = Guid.NewGuid(),
                IsCreated = true
            };

            // Verify team is already in dungeon
            Assert.True(state.CurrentDungeonId.HasValue);

            var result = new TeamDungeonResult
            {
                Success = false,
                Message = "队伍已在副本中"
            };

            Assert.False(result.Success);
        }

        #endregion

        #region Event Sequence Tests - 事件序列

        [Fact]
        public void EventSequence_TeamDungeonFlow_HasCorrectOrder()
        {
            // Simulate full team dungeon event sequence
            var events = new List<GameEvent>
            {
                new GameEvent { EventType = GameEventType.TeamCreated, CharacterId = 1 },
                new GameEvent { EventType = GameEventType.TeamMemberJoined, CharacterId = 2 },
                new GameEvent { EventType = GameEventType.TeamMemberJoined, CharacterId = 3 },
                new GameEvent { EventType = GameEventType.TeamDungeonEntered, CharacterId = 1 },
                new GameEvent { EventType = GameEventType.CombatDamageDealt, CharacterId = 1 },
                new GameEvent { EventType = GameEventType.CombatPlayerKill, CharacterId = 1 },
                new GameEvent { EventType = GameEventType.DungeonCompleted, CharacterId = 1 },
                new GameEvent { EventType = GameEventType.TeamDisbanded, CharacterId = 1 }
            };

            // All events should have unique IDs
            var uniqueIds = events.Select(e => e.EventId).Distinct().Count();
            Assert.Equal(events.Count, uniqueIds);

            // Timestamps should be non-decreasing
            for (int i = 1; i < events.Count; i++)
            {
                Assert.True(events[i].Timestamp >= events[i - 1].Timestamp);
            }
        }

        [Fact]
        public void EventSequence_CombatResultDecoupling_Events()
        {
            // 战斗结果通知解耦 — 所有战斗事件都应可通过事件流发布
            var combatEvents = new[]
            {
                GameEventType.CombatDamageDealt,
                GameEventType.CombatPlayerKill,
                GameEventType.CombatPlayerDeath,
                GameEventType.CombatPlayerResurrect,
                GameEventType.CombatSkillCast
            };

            foreach (var eventType in combatEvents)
            {
                var evt = new GameEvent
                {
                    EventType = eventType,
                    CharacterId = 12345
                };
                Assert.True((int)evt.EventType >= 200 && (int)evt.EventType < 300);
            }
        }

        #endregion
    }
}
