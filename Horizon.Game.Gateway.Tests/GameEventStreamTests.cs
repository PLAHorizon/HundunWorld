using Horizon.Orleans.Interface;
using Horizon.Orleans.Grains;

namespace Horizon.Game.Gateway.Tests
{
    /// <summary>
    /// 游戏事件流和事件驱动架构单元测试
    /// 测试GameEvent数据模型、GameStreamNamespaces常量、GameEventType枚举
    /// </summary>
    public class GameEventStreamTests
    {
        #region GameStreamNamespaces Tests

        [Fact]
        public void GameStreamNamespaces_CharacterEvents_HasCorrectValue()
        {
            Assert.Equal("CharacterEvents", GameStreamNamespaces.CharacterEvents);
        }

        [Fact]
        public void GameStreamNamespaces_CombatEvents_HasCorrectValue()
        {
            Assert.Equal("CombatEvents", GameStreamNamespaces.CombatEvents);
        }

        [Fact]
        public void GameStreamNamespaces_SocialEvents_HasCorrectValue()
        {
            Assert.Equal("SocialEvents", GameStreamNamespaces.SocialEvents);
        }

        [Fact]
        public void GameStreamNamespaces_SystemEvents_HasCorrectValue()
        {
            Assert.Equal("SystemEvents", GameStreamNamespaces.SystemEvents);
        }

        #endregion

        #region GameEventType Tests

        [Theory]
        [InlineData(GameEventType.CharacterLogin, 100)]
        [InlineData(GameEventType.CharacterLogout, 101)]
        [InlineData(GameEventType.CharacterLevelUp, 102)]
        [InlineData(GameEventType.CharacterCreated, 103)]
        [InlineData(GameEventType.CombatDamageDealt, 200)]
        [InlineData(GameEventType.CombatPlayerKill, 201)]
        [InlineData(GameEventType.CombatPlayerDeath, 202)]
        [InlineData(GameEventType.CombatPlayerResurrect, 203)]
        [InlineData(GameEventType.CombatSkillCast, 204)]
        [InlineData(GameEventType.GuildCreated, 300)]
        [InlineData(GameEventType.GuildMemberJoined, 301)]
        [InlineData(GameEventType.TeamCreated, 302)]
        [InlineData(GameEventType.FriendAdded, 303)]
        [InlineData(GameEventType.ServerStatusChanged, 400)]
        [InlineData(GameEventType.ActivityStarted, 401)]
        [InlineData(GameEventType.ActivityEnded, 402)]
        [InlineData(GameEventType.DungeonCompleted, 403)]
        [InlineData(GameEventType.QuestCompleted, 404)]
        public void GameEventType_HasCorrectIntValue(GameEventType eventType, int expectedValue)
        {
            Assert.Equal(expectedValue, (int)eventType);
        }

        [Fact]
        public void GameEventType_CharacterEventsStartAt100()
        {
            Assert.Equal(100, (int)GameEventType.CharacterLogin);
        }

        [Fact]
        public void GameEventType_CombatEventsStartAt200()
        {
            Assert.Equal(200, (int)GameEventType.CombatDamageDealt);
        }

        [Fact]
        public void GameEventType_SocialEventsStartAt300()
        {
            Assert.Equal(300, (int)GameEventType.GuildCreated);
        }

        [Fact]
        public void GameEventType_SystemEventsStartAt400()
        {
            Assert.Equal(400, (int)GameEventType.ServerStatusChanged);
        }

        #endregion

        #region GameEvent Default Values Tests

        [Fact]
        public void GameEvent_DefaultValues_AreCorrect()
        {
            var gameEvent = new GameEvent();

            Assert.NotNull(gameEvent.EventId);
            Assert.NotEmpty(gameEvent.EventId);
            Assert.Equal(32, gameEvent.EventId.Length); // GUID "N" format is 32 chars
            Assert.Equal(default(GameEventType), gameEvent.EventType);
            Assert.True(gameEvent.Timestamp > 0);
            Assert.Equal(0UL, gameEvent.CharacterId);
            Assert.Equal(string.Empty, gameEvent.Description);
            Assert.NotNull(gameEvent.Metadata);
            Assert.Empty(gameEvent.Metadata);
        }

        [Fact]
        public void GameEvent_EventId_IsUniquePerInstance()
        {
            var event1 = new GameEvent();
            var event2 = new GameEvent();

            Assert.NotEqual(event1.EventId, event2.EventId);
        }

        [Fact]
        public void GameEvent_Timestamp_IsRecentUtc()
        {
            var before = DateTime.UtcNow.Ticks;
            var gameEvent = new GameEvent();
            var after = DateTime.UtcNow.Ticks;

            Assert.InRange(gameEvent.Timestamp, before, after);
        }

        #endregion

        #region GameEvent Property Assignment Tests

        [Fact]
        public void GameEvent_CharacterLoginEvent_CanBeCreated()
        {
            var gameEvent = new GameEvent
            {
                EventType = GameEventType.CharacterLogin,
                CharacterId = 12345,
                Description = "角色登录",
                Metadata = new Dictionary<string, string>
                {
                    { "CharacterName", "TestHero" },
                    { "Level", "50" }
                }
            };

            Assert.Equal(GameEventType.CharacterLogin, gameEvent.EventType);
            Assert.Equal(12345UL, gameEvent.CharacterId);
            Assert.Equal("角色登录", gameEvent.Description);
            Assert.Equal(2, gameEvent.Metadata.Count);
            Assert.Equal("TestHero", gameEvent.Metadata["CharacterName"]);
            Assert.Equal("50", gameEvent.Metadata["Level"]);
        }

        [Fact]
        public void GameEvent_CombatDeathEvent_CanBeCreated()
        {
            var gameEvent = new GameEvent
            {
                EventType = GameEventType.CombatPlayerDeath,
                CharacterId = 99999,
                Description = "角色战斗死亡",
                Metadata = new Dictionary<string, string>
                {
                    { "KillerId", "88888" },
                    { "Cause", "战斗死亡" },
                    { "DamageType", "Fire" }
                }
            };

            Assert.Equal(GameEventType.CombatPlayerDeath, gameEvent.EventType);
            Assert.Equal(99999UL, gameEvent.CharacterId);
            Assert.Contains("KillerId", gameEvent.Metadata.Keys);
            Assert.Equal("88888", gameEvent.Metadata["KillerId"]);
        }

        [Fact]
        public void GameEvent_SystemEvent_CanBeCreatedWithZeroCharacterId()
        {
            var gameEvent = new GameEvent
            {
                EventType = GameEventType.ServerStatusChanged,
                CharacterId = 0,
                Description = "服务器维护",
                Metadata = new Dictionary<string, string>
                {
                    { "ServerName", "Server1" },
                    { "Status", "Maintenance" }
                }
            };

            Assert.Equal(GameEventType.ServerStatusChanged, gameEvent.EventType);
            Assert.Equal(0UL, gameEvent.CharacterId);
            Assert.Equal("服务器维护", gameEvent.Description);
        }

        [Fact]
        public void GameEvent_DungeonCompletedEvent_IncludesCompletionDetails()
        {
            var gameEvent = new GameEvent
            {
                EventType = GameEventType.DungeonCompleted,
                CharacterId = 55555,
                Description = "副本通关",
                Metadata = new Dictionary<string, string>
                {
                    { "DungeonName", "火焰山" },
                    { "Difficulty", "Hell" },
                    { "CompletionTimeSeconds", "300" },
                    { "PartySize", "4" }
                }
            };

            Assert.Equal(GameEventType.DungeonCompleted, gameEvent.EventType);
            Assert.Equal(4, gameEvent.Metadata.Count);
            Assert.Equal("Hell", gameEvent.Metadata["Difficulty"]);
        }

        [Fact]
        public void GameEvent_QuestCompletedEvent_CanBeCreated()
        {
            var gameEvent = new GameEvent
            {
                EventType = GameEventType.QuestCompleted,
                CharacterId = 11111,
                Description = "任务完成",
                Metadata = new Dictionary<string, string>
                {
                    { "QuestId", "Q001" },
                    { "RewardGold", "1000" }
                }
            };

            Assert.Equal(GameEventType.QuestCompleted, gameEvent.EventType);
            Assert.Equal(11111UL, gameEvent.CharacterId);
        }

        #endregion

        #region GameEvent Metadata Edge Cases

        [Fact]
        public void GameEvent_Metadata_EmptyByDefault()
        {
            var gameEvent = new GameEvent();
            Assert.NotNull(gameEvent.Metadata);
            Assert.Empty(gameEvent.Metadata);
        }

        [Fact]
        public void GameEvent_Metadata_CanAddMultipleEntries()
        {
            var gameEvent = new GameEvent();
            gameEvent.Metadata["Key1"] = "Value1";
            gameEvent.Metadata["Key2"] = "Value2";
            gameEvent.Metadata["Key3"] = "Value3";

            Assert.Equal(3, gameEvent.Metadata.Count);
        }

        [Fact]
        public void GameEvent_Metadata_CanOverwriteExistingEntry()
        {
            var gameEvent = new GameEvent();
            gameEvent.Metadata["Key1"] = "Value1";
            gameEvent.Metadata["Key1"] = "UpdatedValue";

            Assert.Single(gameEvent.Metadata);
            Assert.Equal("UpdatedValue", gameEvent.Metadata["Key1"]);
        }

        [Fact]
        public void GameEvent_Description_CanBeSetToEmptyString()
        {
            var gameEvent = new GameEvent { Description = "" };
            Assert.Equal(string.Empty, gameEvent.Description);
        }

        [Fact]
        public void GameEvent_Description_CanContainUnicode()
        {
            var gameEvent = new GameEvent { Description = "五行共鸣·混沌觉醒" };
            Assert.Equal("五行共鸣·混沌觉醒", gameEvent.Description);
        }

        #endregion

        #region GameEventPublisher Interface Tests

        [Fact]
        public void IGameEventPublisher_InterfaceDefined_HasAllMethods()
        {
            var interfaceType = typeof(IGameEventPublisher);

            var methods = interfaceType.GetMethods();
            Assert.Contains(methods, m => m.Name == "PublishCharacterEventAsync");
            Assert.Contains(methods, m => m.Name == "PublishCombatEventAsync");
            Assert.Contains(methods, m => m.Name == "PublishSocialEventAsync");
            Assert.Contains(methods, m => m.Name == "PublishSystemEventAsync");
        }

        [Fact]
        public void IGameEventPublisher_AllMethods_ReturnTask()
        {
            var interfaceType = typeof(IGameEventPublisher);
            var methods = interfaceType.GetMethods();

            foreach (var method in methods)
            {
                Assert.Equal(typeof(System.Threading.Tasks.Task), method.ReturnType);
            }
        }

        [Fact]
        public void IGameEventPublisher_AllMethods_AcceptGameEventParameter()
        {
            var interfaceType = typeof(IGameEventPublisher);
            var methods = interfaceType.GetMethods();

            foreach (var method in methods)
            {
                var parameters = method.GetParameters();
                Assert.Single(parameters);
                Assert.Equal(typeof(GameEvent), parameters[0].ParameterType);
            }
        }

        #endregion

        #region GameEvent Serialization Attributes Tests

        [Fact]
        public void GameEvent_HasGenerateSerializerAttribute()
        {
            var type = typeof(GameEvent);
            var attributes = type.GetCustomAttributes(false);
            Assert.Contains(attributes, a => a.GetType().Name == "GenerateSerializerAttribute");
        }

        [Fact]
        public void GameEvent_HasMemoryPackableAttribute()
        {
            var type = typeof(GameEvent);
            var attributes = type.GetCustomAttributes(false);
            Assert.Contains(attributes, a => a.GetType().Name == "MemoryPackableAttribute");
        }

        [Fact]
        public void GameEvent_HasSerializableAttribute()
        {
            var type = typeof(GameEvent);
            Assert.True(type.IsSerializable);
        }

        #endregion

        #region Multiple Events Scenario Tests

        [Fact]
        public void GameEvent_MultipleEvents_HaveUniqueEventIds()
        {
            var events = Enumerable.Range(0, 100).Select(_ => new GameEvent()).ToList();
            var uniqueIds = events.Select(e => e.EventId).Distinct().ToList();

            Assert.Equal(events.Count, uniqueIds.Count);
        }

        [Fact]
        public void GameEvent_MultipleEvents_TimestampsAreNonDecreasing()
        {
            var events = new List<GameEvent>();
            for (int i = 0; i < 10; i++)
            {
                events.Add(new GameEvent());
            }

            for (int i = 1; i < events.Count; i++)
            {
                Assert.True(events[i].Timestamp >= events[i - 1].Timestamp);
            }
        }

        [Fact]
        public void GameEvent_CombatSequence_CanBeTracked()
        {
            var attackEvent = new GameEvent
            {
                EventType = GameEventType.CombatDamageDealt,
                CharacterId = 1,
                Metadata = new Dictionary<string, string> { { "TargetId", "2" }, { "Damage", "150" } }
            };

            var killEvent = new GameEvent
            {
                EventType = GameEventType.CombatPlayerKill,
                CharacterId = 1,
                Metadata = new Dictionary<string, string> { { "VictimId", "2" } }
            };

            var deathEvent = new GameEvent
            {
                EventType = GameEventType.CombatPlayerDeath,
                CharacterId = 2,
                Metadata = new Dictionary<string, string> { { "KillerId", "1" } }
            };

            Assert.Equal(GameEventType.CombatDamageDealt, attackEvent.EventType);
            Assert.Equal(GameEventType.CombatPlayerKill, killEvent.EventType);
            Assert.Equal(GameEventType.CombatPlayerDeath, deathEvent.EventType);
            Assert.Equal("2", attackEvent.Metadata["TargetId"]);
            Assert.Equal("2", killEvent.Metadata["VictimId"]);
            Assert.Equal("1", deathEvent.Metadata["KillerId"]);
        }

        #endregion
    }
}
