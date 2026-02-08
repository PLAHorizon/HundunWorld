using Horizon.Orleans.Interface;
using Horizon.Orleans.Grains;

namespace Horizon.Game.Gateway.Tests
{
    /// <summary>
    /// 事件消费者、事件驱动架构增强、Grain版本管理滚动升级测试
    /// 覆盖GameEventConsumerGrain状态模型、事件处理统计、CI/CD增强验证
    /// </summary>
    public class EventConsumerVersioningTests
    {
        #region EventConsumerState Tests

        [Fact]
        public void EventConsumerState_DefaultValues_AreCorrect()
        {
            var state = new EventConsumerState();

            Assert.NotNull(state.Stats);
            Assert.NotNull(state.RecentEvents);
            Assert.Empty(state.RecentEvents);
            Assert.Equal(0, state.Stats.TotalEventsProcessed);
            Assert.Equal(0, state.Stats.FailedEvents);
        }

        [Fact]
        public void EventConsumerState_Stats_CanBeUpdated()
        {
            var state = new EventConsumerState();
            state.Stats.TotalEventsProcessed = 100;
            state.Stats.FailedEvents = 5;
            state.Stats.Namespace = "CombatEvents";
            state.Stats.StatsStartTimestamp = DateTime.UtcNow.Ticks;

            Assert.Equal(100, state.Stats.TotalEventsProcessed);
            Assert.Equal(5, state.Stats.FailedEvents);
            Assert.Equal("CombatEvents", state.Stats.Namespace);
            Assert.True(state.Stats.StatsStartTimestamp > 0);
        }

        [Fact]
        public void EventConsumerState_RecentEvents_CanAddEntries()
        {
            var state = new EventConsumerState();
            var summary = new ProcessedEventSummary
            {
                EventId = "test123",
                EventType = GameEventType.CombatDamageDealt,
                CharacterId = 12345,
                ProcessedTimestamp = DateTime.UtcNow.Ticks,
                Success = true,
                Description = "测试事件"
            };

            state.RecentEvents.Add(summary);

            Assert.Single(state.RecentEvents);
            Assert.Equal("test123", state.RecentEvents[0].EventId);
            Assert.Equal(GameEventType.CombatDamageDealt, state.RecentEvents[0].EventType);
        }

        #endregion

        #region EventProcessingStats Tests

        [Fact]
        public void EventProcessingStats_DefaultValues_AreCorrect()
        {
            var stats = new EventProcessingStats();

            Assert.Equal(0, stats.TotalEventsProcessed);
            Assert.NotNull(stats.EventTypeCounters);
            Assert.Empty(stats.EventTypeCounters);
            Assert.Equal(0, stats.FailedEvents);
            Assert.Equal(0, stats.LastProcessedTimestamp);
            Assert.Equal(string.Empty, stats.Namespace);
            Assert.Equal(0, stats.StatsStartTimestamp);
        }

        [Fact]
        public void EventProcessingStats_EventTypeCounters_CanTrackMultipleTypes()
        {
            var stats = new EventProcessingStats();

            stats.EventTypeCounters[(int)GameEventType.CombatDamageDealt] = 50;
            stats.EventTypeCounters[(int)GameEventType.CombatPlayerKill] = 10;
            stats.EventTypeCounters[(int)GameEventType.CombatPlayerDeath] = 8;
            stats.EventTypeCounters[(int)GameEventType.CharacterLogin] = 200;

            Assert.Equal(4, stats.EventTypeCounters.Count);
            Assert.Equal(50, stats.EventTypeCounters[(int)GameEventType.CombatDamageDealt]);
            Assert.Equal(10, stats.EventTypeCounters[(int)GameEventType.CombatPlayerKill]);
            Assert.Equal(200, stats.EventTypeCounters[(int)GameEventType.CharacterLogin]);
        }

        [Fact]
        public void EventProcessingStats_Namespace_CanBeSetToAllNamespaces()
        {
            var namespaces = new[]
            {
                GameStreamNamespaces.CharacterEvents,
                GameStreamNamespaces.CombatEvents,
                GameStreamNamespaces.SocialEvents,
                GameStreamNamespaces.SystemEvents
            };

            foreach (var ns in namespaces)
            {
                var stats = new EventProcessingStats { Namespace = ns };
                Assert.Equal(ns, stats.Namespace);
            }
        }

        [Fact]
        public void EventProcessingStats_FailureRate_CanBeCalculated()
        {
            var stats = new EventProcessingStats
            {
                TotalEventsProcessed = 1000,
                FailedEvents = 5
            };

            double failureRate = stats.TotalEventsProcessed > 0
                ? (double)stats.FailedEvents / stats.TotalEventsProcessed * 100
                : 0;

            Assert.Equal(0.5, failureRate);
        }

        [Fact]
        public void EventProcessingStats_ZeroEvents_FailureRateIsZero()
        {
            var stats = new EventProcessingStats
            {
                TotalEventsProcessed = 0,
                FailedEvents = 0
            };

            double failureRate = stats.TotalEventsProcessed > 0
                ? (double)stats.FailedEvents / stats.TotalEventsProcessed * 100
                : 0;

            Assert.Equal(0, failureRate);
        }

        #endregion

        #region ProcessedEventSummary Tests

        [Fact]
        public void ProcessedEventSummary_DefaultValues_AreCorrect()
        {
            var summary = new ProcessedEventSummary();

            Assert.Equal(string.Empty, summary.EventId);
            Assert.Equal(default(GameEventType), summary.EventType);
            Assert.Equal(0UL, summary.CharacterId);
            Assert.Equal(0, summary.ProcessedTimestamp);
            Assert.False(summary.Success);
            Assert.Equal(string.Empty, summary.Description);
        }

        [Fact]
        public void ProcessedEventSummary_CombatEvent_CanBeCreated()
        {
            var summary = new ProcessedEventSummary
            {
                EventId = Guid.NewGuid().ToString("N"),
                EventType = GameEventType.CombatDamageDealt,
                CharacterId = 99999,
                ProcessedTimestamp = DateTime.UtcNow.Ticks,
                Success = true,
                Description = "攻击造成150点伤害"
            };

            Assert.Equal(32, summary.EventId.Length);
            Assert.Equal(GameEventType.CombatDamageDealt, summary.EventType);
            Assert.Equal(99999UL, summary.CharacterId);
            Assert.True(summary.Success);
            Assert.Equal("攻击造成150点伤害", summary.Description);
        }

        [Fact]
        public void ProcessedEventSummary_FailedEvent_MarkedAsFailed()
        {
            var summary = new ProcessedEventSummary
            {
                EventId = "failed_event_001",
                EventType = GameEventType.CombatSkillCast,
                Success = false,
                Description = "技能施放事件处理失败"
            };

            Assert.False(summary.Success);
            Assert.Equal("技能施放事件处理失败", summary.Description);
        }

        [Theory]
        [InlineData(GameEventType.CombatDamageDealt)]
        [InlineData(GameEventType.CombatPlayerKill)]
        [InlineData(GameEventType.CombatPlayerDeath)]
        [InlineData(GameEventType.CombatPlayerResurrect)]
        [InlineData(GameEventType.CombatSkillCast)]
        [InlineData(GameEventType.CharacterLogin)]
        [InlineData(GameEventType.CharacterLogout)]
        [InlineData(GameEventType.GuildCreated)]
        [InlineData(GameEventType.TeamCreated)]
        [InlineData(GameEventType.ServerStatusChanged)]
        [InlineData(GameEventType.DungeonCompleted)]
        [InlineData(GameEventType.QuestCompleted)]
        public void ProcessedEventSummary_AllEventTypes_CanBeAssigned(GameEventType eventType)
        {
            var summary = new ProcessedEventSummary
            {
                EventType = eventType,
                Success = true
            };

            Assert.Equal(eventType, summary.EventType);
        }

        #endregion

        #region IGameEventConsumerGrain Interface Tests

        [Fact]
        public void IGameEventConsumerGrain_InterfaceExists()
        {
            var interfaceType = typeof(IGameEventConsumerGrain);
            Assert.True(interfaceType.IsInterface);
        }

        [Fact]
        public void IGameEventConsumerGrain_HasRequiredMethods()
        {
            var interfaceType = typeof(IGameEventConsumerGrain);
            var methods = interfaceType.GetMethods();

            Assert.Contains(methods, m => m.Name == "InitializeAsync");
            Assert.Contains(methods, m => m.Name == "GetProcessingStatsAsync");
            Assert.Contains(methods, m => m.Name == "GetRecentEventsAsync");
            Assert.Contains(methods, m => m.Name == "ResetStatsAsync");
        }

        [Fact]
        public void IGameEventConsumerGrain_InitializeAsync_ReturnsTask()
        {
            var method = typeof(IGameEventConsumerGrain).GetMethod("InitializeAsync");
            Assert.NotNull(method);
            Assert.Equal(typeof(System.Threading.Tasks.Task), method.ReturnType);
        }

        [Fact]
        public void IGameEventConsumerGrain_GetProcessingStatsAsync_ReturnsEventProcessingStats()
        {
            var method = typeof(IGameEventConsumerGrain).GetMethod("GetProcessingStatsAsync");
            Assert.NotNull(method);
            Assert.Equal(typeof(Task<EventProcessingStats>), method.ReturnType);
        }

        [Fact]
        public void IGameEventConsumerGrain_GetRecentEventsAsync_HasDefaultCountParameter()
        {
            var method = typeof(IGameEventConsumerGrain).GetMethod("GetRecentEventsAsync");
            Assert.NotNull(method);

            var parameters = method.GetParameters();
            Assert.Single(parameters);
            Assert.Equal("count", parameters[0].Name);
            Assert.True(parameters[0].HasDefaultValue);
            Assert.Equal(20, parameters[0].DefaultValue);
        }

        [Fact]
        public void IGameEventConsumerGrain_IsStringKeyGrain()
        {
            var interfaceType = typeof(IGameEventConsumerGrain);
            Assert.True(typeof(IGrainWithStringKey).IsAssignableFrom(interfaceType));
        }

        [Fact]
        public void IGameEventConsumerGrain_HasVersionAttribute()
        {
            var interfaceType = typeof(IGameEventConsumerGrain);
            var versionAttr = interfaceType.GetCustomAttributes(false)
                .FirstOrDefault(a => a.GetType().Name.Contains("Version"));
            Assert.NotNull(versionAttr);
        }

        #endregion

        #region GameEventConsumerGrain Implementation Tests

        [Fact]
        public void GameEventConsumerGrain_ClassExists()
        {
            var grainType = typeof(GameEventConsumerGrain);
            Assert.True(grainType.IsClass);
            Assert.False(grainType.IsAbstract);
        }

        [Fact]
        public void GameEventConsumerGrain_ImplementsInterface()
        {
            var grainType = typeof(GameEventConsumerGrain);
            Assert.True(typeof(IGameEventConsumerGrain).IsAssignableFrom(grainType));
        }

        [Fact]
        public void GameEventConsumerGrain_ExtendsGrainWithState()
        {
            var grainType = typeof(GameEventConsumerGrain);
            var baseType = grainType.BaseType;

            Assert.NotNull(baseType);
            Assert.True(baseType.IsGenericType);
            Assert.Equal(typeof(EventConsumerState), baseType.GetGenericArguments()[0]);
        }

        #endregion

        #region EventConsumerState Serialization Attributes Tests

        [Fact]
        public void EventConsumerState_HasGenerateSerializerAttribute()
        {
            var type = typeof(EventConsumerState);
            var attributes = type.GetCustomAttributes(false);
            Assert.Contains(attributes, a => a.GetType().Name == "GenerateSerializerAttribute");
        }

        [Fact]
        public void EventConsumerState_HasMemoryPackableAttribute()
        {
            var type = typeof(EventConsumerState);
            var attributes = type.GetCustomAttributes(false);
            Assert.Contains(attributes, a => a.GetType().Name == "MemoryPackableAttribute");
        }

        [Fact]
        public void EventConsumerState_HasSerializableAttribute()
        {
            var type = typeof(EventConsumerState);
            Assert.True(type.IsSerializable);
        }

        [Fact]
        public void EventProcessingStats_HasGenerateSerializerAttribute()
        {
            var type = typeof(EventProcessingStats);
            var attributes = type.GetCustomAttributes(false);
            Assert.Contains(attributes, a => a.GetType().Name == "GenerateSerializerAttribute");
        }

        [Fact]
        public void EventProcessingStats_HasMemoryPackableAttribute()
        {
            var type = typeof(EventProcessingStats);
            var attributes = type.GetCustomAttributes(false);
            Assert.Contains(attributes, a => a.GetType().Name == "MemoryPackableAttribute");
        }

        [Fact]
        public void ProcessedEventSummary_HasGenerateSerializerAttribute()
        {
            var type = typeof(ProcessedEventSummary);
            var attributes = type.GetCustomAttributes(false);
            Assert.Contains(attributes, a => a.GetType().Name == "GenerateSerializerAttribute");
        }

        [Fact]
        public void ProcessedEventSummary_HasMemoryPackableAttribute()
        {
            var type = typeof(ProcessedEventSummary);
            var attributes = type.GetCustomAttributes(false);
            Assert.Contains(attributes, a => a.GetType().Name == "MemoryPackableAttribute");
        }

        #endregion

        #region Event Processing Flow Tests

        [Fact]
        public void EventProcessingStats_SimulateProcessingFlow()
        {
            var stats = new EventProcessingStats
            {
                Namespace = "CombatEvents",
                StatsStartTimestamp = DateTime.UtcNow.Ticks
            };

            // Simulate processing 100 combat events
            var eventTypes = new[]
            {
                GameEventType.CombatDamageDealt,
                GameEventType.CombatPlayerKill,
                GameEventType.CombatPlayerDeath,
                GameEventType.CombatSkillCast,
                GameEventType.CombatPlayerResurrect
            };

            for (int i = 0; i < 100; i++)
            {
                var eventType = eventTypes[i % eventTypes.Length];
                var key = (int)eventType;

                stats.TotalEventsProcessed++;
                if (!stats.EventTypeCounters.ContainsKey(key))
                    stats.EventTypeCounters[key] = 0;
                stats.EventTypeCounters[key]++;
            }

            stats.LastProcessedTimestamp = DateTime.UtcNow.Ticks;

            Assert.Equal(100, stats.TotalEventsProcessed);
            Assert.Equal(5, stats.EventTypeCounters.Count);
            Assert.Equal(20, stats.EventTypeCounters[(int)GameEventType.CombatDamageDealt]);
            Assert.Equal(20, stats.EventTypeCounters[(int)GameEventType.CombatPlayerKill]);
        }

        [Fact]
        public void RecentEvents_LimitEnforcement_WorksCorrectly()
        {
            var recentEvents = new List<ProcessedEventSummary>();
            int maxRecentEvents = 100;

            // Add 150 events
            for (int i = 0; i < 150; i++)
            {
                recentEvents.Add(new ProcessedEventSummary
                {
                    EventId = $"event_{i}",
                    EventType = GameEventType.CombatDamageDealt,
                    Success = true
                });

                // Trim to max
                if (recentEvents.Count > maxRecentEvents)
                {
                    recentEvents.RemoveRange(0, recentEvents.Count - maxRecentEvents);
                }
            }

            Assert.Equal(maxRecentEvents, recentEvents.Count);
            Assert.Equal("event_50", recentEvents[0].EventId);
            Assert.Equal("event_149", recentEvents[^1].EventId);
        }

        [Fact]
        public void EventConsumerState_FullWorkflow_StatsAccurate()
        {
            var state = new EventConsumerState();
            state.Stats.Namespace = "SocialEvents";
            state.Stats.StatsStartTimestamp = DateTime.UtcNow.Ticks;

            // Process social events
            var socialEvents = new[]
            {
                (GameEventType.GuildCreated, true),
                (GameEventType.GuildMemberJoined, true),
                (GameEventType.TeamCreated, true),
                (GameEventType.FriendAdded, false), // simulated failure
                (GameEventType.TeamMemberJoined, true),
                (GameEventType.TeamMemberLeft, true),
                (GameEventType.TeamDisbanded, true),
                (GameEventType.TeamDungeonEntered, true)
            };

            foreach (var (eventType, success) in socialEvents)
            {
                state.Stats.TotalEventsProcessed++;
                var key = (int)eventType;

                if (!state.Stats.EventTypeCounters.ContainsKey(key))
                    state.Stats.EventTypeCounters[key] = 0;
                state.Stats.EventTypeCounters[key]++;

                if (!success)
                    state.Stats.FailedEvents++;

                state.RecentEvents.Add(new ProcessedEventSummary
                {
                    EventId = Guid.NewGuid().ToString("N"),
                    EventType = eventType,
                    Success = success,
                    ProcessedTimestamp = DateTime.UtcNow.Ticks
                });
            }

            state.Stats.LastProcessedTimestamp = DateTime.UtcNow.Ticks;

            Assert.Equal(8, state.Stats.TotalEventsProcessed);
            Assert.Equal(1, state.Stats.FailedEvents);
            Assert.Equal(8, state.RecentEvents.Count);
            Assert.Equal(8, state.Stats.EventTypeCounters.Count);
            Assert.True(state.Stats.LastProcessedTimestamp > 0);
        }

        #endregion

        #region GrainKey Format Tests

        [Theory]
        [InlineData("CombatEvents:12345")]
        [InlineData("CharacterEvents:global")]
        [InlineData("SocialEvents:99999")]
        [InlineData("SystemEvents:server1")]
        public void GrainKey_ValidFormat_ParsesCorrectly(string grainKey)
        {
            var parts = grainKey.Split(':', 2);

            Assert.Equal(2, parts.Length);
            Assert.False(string.IsNullOrEmpty(parts[0]));
            Assert.False(string.IsNullOrEmpty(parts[1]));
        }

        [Theory]
        [InlineData("InvalidKey")]
        [InlineData("")]
        public void GrainKey_InvalidFormat_HasSinglePart(string grainKey)
        {
            var parts = grainKey.Split(':', 2);
            Assert.True(parts.Length < 2 || string.IsNullOrEmpty(parts[1]));
        }

        [Fact]
        public void GrainKey_AllNamespaces_HaveValidFormat()
        {
            var namespaces = new[]
            {
                GameStreamNamespaces.CharacterEvents,
                GameStreamNamespaces.CombatEvents,
                GameStreamNamespaces.SocialEvents,
                GameStreamNamespaces.SystemEvents
            };

            foreach (var ns in namespaces)
            {
                var grainKey = $"{ns}:global";
                var parts = grainKey.Split(':', 2);

                Assert.Equal(2, parts.Length);
                Assert.Equal(ns, parts[0]);
                Assert.Equal("global", parts[1]);
            }
        }

        #endregion

        #region Social Event Types Extension Tests

        [Theory]
        [InlineData(GameEventType.TeamMemberJoined, 304)]
        [InlineData(GameEventType.TeamMemberLeft, 305)]
        [InlineData(GameEventType.TeamDisbanded, 306)]
        [InlineData(GameEventType.TeamDungeonEntered, 307)]
        public void GameEventType_SocialEventExtensions_HaveCorrectValues(GameEventType eventType, int expectedValue)
        {
            Assert.Equal(expectedValue, (int)eventType);
        }

        [Fact]
        public void GameEventType_AllSocialEvents_InRange300to399()
        {
            var socialEvents = new[]
            {
                GameEventType.GuildCreated,
                GameEventType.GuildMemberJoined,
                GameEventType.TeamCreated,
                GameEventType.FriendAdded,
                GameEventType.TeamMemberJoined,
                GameEventType.TeamMemberLeft,
                GameEventType.TeamDisbanded,
                GameEventType.TeamDungeonEntered
            };

            foreach (var eventType in socialEvents)
            {
                var value = (int)eventType;
                Assert.InRange(value, 300, 399);
            }
        }

        [Fact]
        public void GameEventType_TotalCount_Is22()
        {
            var allValues = Enum.GetValues<GameEventType>();
            Assert.Equal(22, allValues.Length);
        }

        #endregion

        #region Event Consumer Pattern Tests

        [Fact]
        public void EventConsumer_CombatEventProcessing_TracksCorrectly()
        {
            var state = new EventConsumerState();
            state.Stats.Namespace = "CombatEvents";

            // Simulate combat sequence
            var combatSequence = new[]
            {
                new GameEvent { EventType = GameEventType.CombatSkillCast, CharacterId = 1, Description = "施放技能" },
                new GameEvent { EventType = GameEventType.CombatDamageDealt, CharacterId = 1, Description = "造成100点伤害", Metadata = new() { { "Damage", "100" } } },
                new GameEvent { EventType = GameEventType.CombatDamageDealt, CharacterId = 1, Description = "造成150点伤害", Metadata = new() { { "Damage", "150" } } },
                new GameEvent { EventType = GameEventType.CombatPlayerKill, CharacterId = 1, Description = "击杀角色2" },
                new GameEvent { EventType = GameEventType.CombatPlayerDeath, CharacterId = 2, Description = "被角色1击杀" },
                new GameEvent { EventType = GameEventType.CombatPlayerResurrect, CharacterId = 2, Description = "原地复活" }
            };

            foreach (var gameEvent in combatSequence)
            {
                state.Stats.TotalEventsProcessed++;
                var key = (int)gameEvent.EventType;
                if (!state.Stats.EventTypeCounters.ContainsKey(key))
                    state.Stats.EventTypeCounters[key] = 0;
                state.Stats.EventTypeCounters[key]++;

                state.RecentEvents.Add(new ProcessedEventSummary
                {
                    EventId = gameEvent.EventId,
                    EventType = gameEvent.EventType,
                    CharacterId = gameEvent.CharacterId,
                    Success = true,
                    Description = gameEvent.Description
                });
            }

            Assert.Equal(6, state.Stats.TotalEventsProcessed);
            Assert.Equal(2, state.Stats.EventTypeCounters[(int)GameEventType.CombatDamageDealt]);
            Assert.Equal(1, state.Stats.EventTypeCounters[(int)GameEventType.CombatPlayerKill]);
            Assert.Equal(1, state.Stats.EventTypeCounters[(int)GameEventType.CombatPlayerResurrect]);
        }

        [Fact]
        public void EventConsumer_SystemEventProcessing_TracksCorrectly()
        {
            var state = new EventConsumerState();
            state.Stats.Namespace = "SystemEvents";

            var systemEvents = new[]
            {
                new GameEvent { EventType = GameEventType.ServerStatusChanged, Description = "服务器状态变更" },
                new GameEvent { EventType = GameEventType.ActivityStarted, Description = "活动开始" },
                new GameEvent { EventType = GameEventType.DungeonCompleted, CharacterId = 55555, Description = "副本通关" },
                new GameEvent { EventType = GameEventType.QuestCompleted, CharacterId = 55555, Description = "任务完成" },
                new GameEvent { EventType = GameEventType.ActivityEnded, Description = "活动结束" }
            };

            foreach (var gameEvent in systemEvents)
            {
                state.Stats.TotalEventsProcessed++;
                var key = (int)gameEvent.EventType;
                if (!state.Stats.EventTypeCounters.ContainsKey(key))
                    state.Stats.EventTypeCounters[key] = 0;
                state.Stats.EventTypeCounters[key]++;
            }

            Assert.Equal(5, state.Stats.TotalEventsProcessed);
            Assert.Equal(5, state.Stats.EventTypeCounters.Count);
        }

        #endregion

        #region Stats Reset Tests

        [Fact]
        public void EventProcessingStats_Reset_PreservesNamespace()
        {
            var stats = new EventProcessingStats
            {
                Namespace = "CombatEvents",
                TotalEventsProcessed = 500,
                FailedEvents = 10,
                StatsStartTimestamp = DateTime.UtcNow.Ticks - 100000
            };

            stats.EventTypeCounters[(int)GameEventType.CombatDamageDealt] = 200;

            // Reset
            var resetStats = new EventProcessingStats
            {
                Namespace = stats.Namespace,
                StatsStartTimestamp = DateTime.UtcNow.Ticks
            };

            Assert.Equal("CombatEvents", resetStats.Namespace);
            Assert.Equal(0, resetStats.TotalEventsProcessed);
            Assert.Equal(0, resetStats.FailedEvents);
            Assert.Empty(resetStats.EventTypeCounters);
            Assert.True(resetStats.StatsStartTimestamp > stats.StatsStartTimestamp);
        }

        #endregion

        #region RecentEvents Pagination Tests

        [Fact]
        public void RecentEvents_GetLast20_ReturnsCorrectSubset()
        {
            var events = new List<ProcessedEventSummary>();
            for (int i = 0; i < 50; i++)
            {
                events.Add(new ProcessedEventSummary
                {
                    EventId = $"event_{i}",
                    EventType = GameEventType.CombatDamageDealt,
                    Success = true
                });
            }

            int count = 20;
            int skip = Math.Max(0, events.Count - count);
            var result = events.Skip(skip).Take(count).ToList();

            Assert.Equal(20, result.Count);
            Assert.Equal("event_30", result[0].EventId);
            Assert.Equal("event_49", result[^1].EventId);
        }

        [Fact]
        public void RecentEvents_GetMore_ThanAvailable_ReturnsAll()
        {
            var events = new List<ProcessedEventSummary>();
            for (int i = 0; i < 5; i++)
            {
                events.Add(new ProcessedEventSummary
                {
                    EventId = $"event_{i}",
                    Success = true
                });
            }

            int count = 20;
            int skip = Math.Max(0, events.Count - count);
            var result = events.Skip(skip).Take(count).ToList();

            Assert.Equal(5, result.Count);
        }

        #endregion

        #region Grain Versioning Tests

        [Fact]
        public void IGameEventConsumerGrain_HasVersionAttribute_Version1()
        {
            var interfaceType = typeof(IGameEventConsumerGrain);
            var versionAttr = interfaceType.GetCustomAttributes(false)
                .FirstOrDefault(a => a.GetType().FullName == "Orleans.CodeGeneration.VersionAttribute");

            Assert.NotNull(versionAttr);

            // Get the version value via reflection
            var versionProp = versionAttr.GetType().GetProperty("Version");
            if (versionProp != null)
            {
                var version = versionProp.GetValue(versionAttr);
                Assert.Equal((ushort)1, version);
            }
        }

        [Fact]
        public void AllGrainInterfaces_HaveVersionAttributes()
        {
            var grainInterfaces = new[]
            {
                typeof(ICombatGrain),
                typeof(IQuestGrain),
                typeof(IDungeonGrain),
                typeof(IGameEventConsumerGrain)
            };

            foreach (var grainInterface in grainInterfaces)
            {
                var versionAttr = grainInterface.GetCustomAttributes(false)
                    .FirstOrDefault(a => a.GetType().Name.Contains("Version"));

                Assert.NotNull(versionAttr);
            }
        }

        #endregion

        #region Event Consumer Edge Cases

        [Fact]
        public void EventProcessingStats_LargeVolume_HandlesCorrectly()
        {
            var stats = new EventProcessingStats();

            stats.TotalEventsProcessed = long.MaxValue - 1;
            stats.FailedEvents = 0;

            Assert.Equal(long.MaxValue - 1, stats.TotalEventsProcessed);
        }

        [Fact]
        public void ProcessedEventSummary_EmptyDescription_IsValid()
        {
            var summary = new ProcessedEventSummary
            {
                EventId = "test",
                Description = string.Empty,
                Success = true
            };

            Assert.Equal(string.Empty, summary.Description);
        }

        [Fact]
        public void ProcessedEventSummary_UnicodeDescription_Supported()
        {
            var summary = new ProcessedEventSummary
            {
                Description = "五行共鸣·混沌觉醒 — 造成巨额伤害"
            };

            Assert.Equal("五行共鸣·混沌觉醒 — 造成巨额伤害", summary.Description);
        }

        [Fact]
        public void EventConsumer_MultiNamespace_StatsIsolated()
        {
            var combatStats = new EventProcessingStats { Namespace = "CombatEvents" };
            var socialStats = new EventProcessingStats { Namespace = "SocialEvents" };

            combatStats.TotalEventsProcessed = 100;
            socialStats.TotalEventsProcessed = 50;

            Assert.NotEqual(combatStats.TotalEventsProcessed, socialStats.TotalEventsProcessed);
            Assert.NotEqual(combatStats.Namespace, socialStats.Namespace);
        }

        #endregion
    }
}
