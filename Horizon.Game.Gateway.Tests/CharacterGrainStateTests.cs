using Horizon.Game.Message.Network;
using Horizon.Orleans.Interface;

namespace Horizon.Game.Gateway.Tests
{
    /// <summary>
    /// CharacterGrain 状态模型和事件流接口单元测试
    /// 测试角色状态管理、DateTime类型属性、事件流状态模型、输入验证增强
    /// </summary>
    public class CharacterGrainStateTests
    {
        #region CharacterInfo DateTime Properties Tests - 角色时间属性类型验证

        [Fact]
        public void CharacterInfo_LastLoginTime_IsDateTimeType()
        {
            var info = new CharacterInfo();
            var property = typeof(CharacterInfo).GetProperty("LastLoginTime");

            Assert.NotNull(property);
            Assert.Equal(typeof(DateTime), property!.PropertyType);
        }

        [Fact]
        public void CharacterInfo_LastDamageTime_IsDateTimeType()
        {
            var info = new CharacterInfo();
            var property = typeof(CharacterInfo).GetProperty("LastDamageTime");

            Assert.NotNull(property);
            Assert.Equal(typeof(DateTime), property!.PropertyType);
        }

        [Fact]
        public void CharacterInfo_LastDeathTime_IsDateTimeType()
        {
            var info = new CharacterInfo();
            var property = typeof(CharacterInfo).GetProperty("LastDeathTime");

            Assert.NotNull(property);
            Assert.Equal(typeof(DateTime), property!.PropertyType);
        }

        [Fact]
        public void CharacterInfo_CreatedTime_IsDateTimeType()
        {
            var info = new CharacterInfo();
            var property = typeof(CharacterInfo).GetProperty("CreatedTime");

            Assert.NotNull(property);
            Assert.Equal(typeof(DateTime), property!.PropertyType);
        }

        [Fact]
        public void CharacterInfo_LastLoginTime_CanBeSetToUtcNow()
        {
            var info = new CharacterInfo();
            var before = DateTime.UtcNow;
            info.LastLoginTime = DateTime.UtcNow;
            var after = DateTime.UtcNow;

            Assert.True(info.LastLoginTime >= before);
            Assert.True(info.LastLoginTime <= after);
        }

        [Fact]
        public void CharacterInfo_LastDamageTime_CanBeSetToUtcNow()
        {
            var info = new CharacterInfo();
            var now = DateTime.UtcNow;
            info.LastDamageTime = now;

            Assert.Equal(now, info.LastDamageTime);
        }

        [Fact]
        public void CharacterInfo_LastDeathTime_CanBeSetToUtcNow()
        {
            var info = new CharacterInfo();
            var now = DateTime.UtcNow;
            info.LastDeathTime = now;

            Assert.Equal(now, info.LastDeathTime);
        }

        [Fact]
        public void CharacterInfo_DateTimeProperties_DefaultToMinValue()
        {
            var info = new CharacterInfo();

            Assert.Equal(default(DateTime), info.LastLoginTime);
            Assert.Equal(default(DateTime), info.LastDamageTime);
            Assert.Equal(default(DateTime), info.LastDeathTime);
        }

        #endregion

        #region CharacterInfo Basic Properties Tests - 角色基础属性

        [Fact]
        public void CharacterInfo_DefaultValues_AreCorrect()
        {
            var info = new CharacterInfo();

            Assert.Equal(0UL, info.CharacterId);
            Assert.Equal("", info.CharacterName);
            Assert.Equal(0, info.Level);
            Assert.Equal(0, info.Gender);
            Assert.True(info.IsAlive);
            Assert.Equal(0, info.DeathCount);
            Assert.Equal(0, info.ResurrectionCount);
            Assert.Equal(0L, info.Experience);
            Assert.Equal(0L, info.Gold);
        }

        [Fact]
        public void CharacterInfo_CanSetAllProperties()
        {
            var now = DateTime.UtcNow;
            var info = new CharacterInfo
            {
                CharacterId = 12345,
                CharacterName = "TestCharacter",
                Level = 50,
                Gender = 1,
                CurrentHealth = 1000f,
                MaxHealth = 1500f,
                IsAlive = true,
                DeathCount = 3,
                ResurrectionCount = 2,
                Experience = 50000,
                Gold = 10000,
                CreatedTime = now,
                LastLoginTime = now,
                LastDamageTime = now,
                LastDeathTime = now
            };

            Assert.Equal(12345UL, info.CharacterId);
            Assert.Equal("TestCharacter", info.CharacterName);
            Assert.Equal(50, info.Level);
            Assert.Equal(1, info.Gender);
            Assert.Equal(1000f, info.CurrentHealth);
            Assert.Equal(1500f, info.MaxHealth);
            Assert.True(info.IsAlive);
            Assert.Equal(3, info.DeathCount);
            Assert.Equal(2, info.ResurrectionCount);
            Assert.Equal(50000, info.Experience);
            Assert.Equal(10000, info.Gold);
            Assert.Equal(now, info.CreatedTime);
            Assert.Equal(now, info.LastLoginTime);
            Assert.Equal(now, info.LastDamageTime);
            Assert.Equal(now, info.LastDeathTime);
        }

        [Fact]
        public void CharacterInfo_Position_DefaultIsNotNull()
        {
            var info = new CharacterInfo();
            Assert.NotNull(info.Position);
        }

        [Fact]
        public void CharacterInfo_Appearance_DefaultIsNotNull()
        {
            var info = new CharacterInfo();
            Assert.NotNull(info.Appearance);
        }

        #endregion

        #region CharacterState Tests - 角色状态

        [Fact]
        public void CharacterState_DefaultValues_AreCorrect()
        {
            var state = new CharacterState();

            Assert.Null(state.CharacterInfo);
            Assert.False(state.IsOnline);
        }

        [Fact]
        public void CharacterState_CanSetCharacterInfo()
        {
            var state = new CharacterState
            {
                CharacterInfo = new CharacterInfo
                {
                    CharacterId = 999,
                    CharacterName = "TestHero",
                    Level = 10
                },
                IsOnline = true
            };

            Assert.NotNull(state.CharacterInfo);
            Assert.Equal(999UL, state.CharacterInfo.CharacterId);
            Assert.Equal("TestHero", state.CharacterInfo.CharacterName);
            Assert.True(state.IsOnline);
        }

        [Fact]
        public void CharacterState_LoginFlow_UpdatesTimestamp()
        {
            var state = new CharacterState
            {
                CharacterInfo = new CharacterInfo
                {
                    CharacterId = 1,
                    CharacterName = "Player1"
                },
                IsOnline = false
            };

            // Simulate login
            state.IsOnline = true;
            state.CharacterInfo.LastLoginTime = DateTime.UtcNow;

            Assert.True(state.IsOnline);
            Assert.True(state.CharacterInfo.LastLoginTime > DateTime.MinValue);
        }

        [Fact]
        public void CharacterState_DeathFlow_UpdatesCountAndTime()
        {
            var state = new CharacterState
            {
                CharacterInfo = new CharacterInfo
                {
                    CharacterId = 1,
                    IsAlive = true,
                    CurrentHealth = 1000f,
                    MaxHealth = 1000f
                }
            };

            // Simulate death
            state.CharacterInfo.IsAlive = false;
            state.CharacterInfo.CurrentHealth = 0;
            state.CharacterInfo.DeathCount++;
            state.CharacterInfo.LastDeathTime = DateTime.UtcNow;

            Assert.False(state.CharacterInfo.IsAlive);
            Assert.Equal(0f, state.CharacterInfo.CurrentHealth);
            Assert.Equal(1, state.CharacterInfo.DeathCount);
            Assert.True(state.CharacterInfo.LastDeathTime > DateTime.MinValue);
        }

        [Fact]
        public void CharacterState_DamageFlow_UpdatesHealthAndTime()
        {
            var state = new CharacterState
            {
                CharacterInfo = new CharacterInfo
                {
                    CharacterId = 1,
                    IsAlive = true,
                    CurrentHealth = 1000f,
                    MaxHealth = 1000f
                }
            };

            // Simulate taking damage
            float damage = 300f;
            state.CharacterInfo.CurrentHealth -= damage;
            state.CharacterInfo.LastDamageTime = DateTime.UtcNow;

            Assert.Equal(700f, state.CharacterInfo.CurrentHealth);
            Assert.True(state.CharacterInfo.LastDamageTime > DateTime.MinValue);
        }

        #endregion

        #region DamageMessage Tests - 伤害消息

        [Fact]
        public void DamageMessage_DefaultValues_AreCorrect()
        {
            var msg = new DamageMessage();

            Assert.Equal(0UL, msg.VictimId);
            Assert.Equal(0UL, msg.AttackerId);
            Assert.Equal(0, msg.Damage);
            Assert.Equal(0, msg.RemainingHealth);
            Assert.False(msg.IsCritical);
            Assert.False(msg.IsDodged);
            Assert.False(msg.IsBlocked);
        }

        [Fact]
        public void DamageMessage_CanSetAllProperties()
        {
            var msg = new DamageMessage
            {
                VictimId = 100,
                AttackerId = 200,
                Damage = 500,
                RemainingHealth = 500,
                IsCritical = true,
                IsDodged = false,
                IsBlocked = false,
                ElementType = 1
            };

            Assert.Equal(100UL, msg.VictimId);
            Assert.Equal(200UL, msg.AttackerId);
            Assert.Equal(500, msg.Damage);
            Assert.Equal(500, msg.RemainingHealth);
            Assert.True(msg.IsCritical);
            Assert.Equal(1, msg.ElementType);
        }

        #endregion

        #region DeathMessage Tests - 死亡消息

        [Fact]
        public void DeathMessage_DefaultValues_AreCorrect()
        {
            var msg = new DeathMessage();

            Assert.Equal(0UL, msg.DeceasedId);
            Assert.Equal(0UL, msg.KillerId);
        }

        [Fact]
        public void DeathMessage_CanSetCauseAndPosition()
        {
            var msg = new DeathMessage
            {
                DeceasedId = 100,
                KillerId = 200,
                Cause = "战斗死亡",
                DeathPosition = new Position { X = 10, Y = 20, Z = 30 }
            };

            Assert.Equal(100UL, msg.DeceasedId);
            Assert.Equal(200UL, msg.KillerId);
            Assert.Equal("战斗死亡", msg.Cause);
            Assert.Equal(10f, msg.DeathPosition.X);
        }

        #endregion

        #region ResurrectMessage Tests - 复活消息

        [Fact]
        public void ResurrectMessage_DefaultValues_AreCorrect()
        {
            var msg = new ResurrectMessage();

            Assert.Equal(0UL, msg.ResurrectedId);
            Assert.Equal(0f, msg.RemainingHealth);
            Assert.Equal(0f, msg.MaxHealth);
        }

        [Fact]
        public void ResurrectMessage_CanSetHealthValues()
        {
            var msg = new ResurrectMessage
            {
                ResurrectedId = 100,
                RemainingHealth = 300f,
                MaxHealth = 1000f,
                ResurrectType = 1,
                ResurrectPosition = new Position { X = 5, Y = 10, Z = 15 }
            };

            Assert.Equal(100UL, msg.ResurrectedId);
            Assert.Equal(300f, msg.RemainingHealth);
            Assert.Equal(1000f, msg.MaxHealth);
            Assert.Equal(1, msg.ResurrectType);
        }

        #endregion

        #region EventStreamStatus Tests - 事件流状态

        [Fact]
        public void EventStreamStatus_DefaultValues_AreCorrect()
        {
            var status = new EventStreamStatus();

            Assert.Equal("", status.Namespace);
            Assert.False(status.IsActive);
            Assert.Equal(0L, status.TotalEventsPublished);
            Assert.Equal(0, status.SubscriberCount);
        }

        [Fact]
        public void EventStreamStatus_CanSetAllProperties()
        {
            var now = DateTime.UtcNow;
            var status = new EventStreamStatus
            {
                Namespace = GameStreamNamespaces.CombatEvents,
                IsActive = true,
                TotalEventsPublished = 1500,
                SubscriberCount = 10,
                LastEventTime = now
            };

            Assert.Equal("CombatEvents", status.Namespace);
            Assert.True(status.IsActive);
            Assert.Equal(1500L, status.TotalEventsPublished);
            Assert.Equal(10, status.SubscriberCount);
            Assert.Equal(now, status.LastEventTime);
        }

        [Fact]
        public void EventStreamStatus_AllNamespacesValid()
        {
            Assert.Equal("CharacterEvents", GameStreamNamespaces.CharacterEvents);
            Assert.Equal("CombatEvents", GameStreamNamespaces.CombatEvents);
            Assert.Equal("SocialEvents", GameStreamNamespaces.SocialEvents);
            Assert.Equal("SystemEvents", GameStreamNamespaces.SystemEvents);
        }

        #endregion

        #region IGameEventStreamGrain Interface Tests - 事件流Grain接口验证

        [Fact]
        public void IGameEventStreamGrain_HasRequiredMethods()
        {
            var type = typeof(IGameEventStreamGrain);

            Assert.NotNull(type.GetMethod("GetSubscriberCountAsync"));
            Assert.NotNull(type.GetMethod("GetStreamStatusAsync"));
            Assert.NotNull(type.GetMethod("PublishEventAsync"));
        }

        [Fact]
        public void IGameEventStreamGrain_GetStreamStatusAsync_ReturnsEventStreamStatus()
        {
            var method = typeof(IGameEventStreamGrain).GetMethod("GetStreamStatusAsync");
            Assert.NotNull(method);
            Assert.Equal(typeof(Task<EventStreamStatus>), method!.ReturnType);
        }

        [Fact]
        public void IGameEventStreamGrain_PublishEventAsync_AcceptsGameEvent()
        {
            var method = typeof(IGameEventStreamGrain).GetMethod("PublishEventAsync");
            Assert.NotNull(method);
            var parameters = method!.GetParameters();
            Assert.Single(parameters);
            Assert.Equal(typeof(GameEvent), parameters[0].ParameterType);
        }

        [Fact]
        public void IGameEventStreamGrain_HasVersionAttribute()
        {
            var type = typeof(IGameEventStreamGrain);
            var versionAttr = type.GetCustomAttributes(typeof(global::Orleans.CodeGeneration.VersionAttribute), false);
            Assert.Single(versionAttr);
        }

        [Fact]
        public void IGameEventStreamGrain_IsGrainWithStringKey()
        {
            var type = typeof(IGameEventStreamGrain);
            Assert.True(typeof(global::Orleans.IGrainWithStringKey).IsAssignableFrom(type));
        }

        #endregion

        #region IGameEventObserver Interface Tests - 事件观察者接口验证

        [Fact]
        public void IGameEventObserver_HasRequiredMethods()
        {
            var type = typeof(IGameEventObserver);

            Assert.NotNull(type.GetMethod("OnGameEventReceivedAsync"));
            Assert.NotNull(type.GetMethod("OnErrorAsync"));
        }

        [Fact]
        public void IGameEventObserver_OnGameEventReceivedAsync_AcceptsGameEvent()
        {
            var method = typeof(IGameEventObserver).GetMethod("OnGameEventReceivedAsync");
            Assert.NotNull(method);
            var parameters = method!.GetParameters();
            Assert.Single(parameters);
            Assert.Equal(typeof(GameEvent), parameters[0].ParameterType);
        }

        [Fact]
        public void IGameEventObserver_OnErrorAsync_AcceptsException()
        {
            var method = typeof(IGameEventObserver).GetMethod("OnErrorAsync");
            Assert.NotNull(method);
            var parameters = method!.GetParameters();
            Assert.Single(parameters);
            Assert.Equal(typeof(Exception), parameters[0].ParameterType);
        }

        #endregion

        #region GameEvent with DateTime Tests - 事件与时间戳

        [Fact]
        public void GameEvent_Timestamp_UsesTicksFormat()
        {
            var evt = new GameEvent
            {
                EventType = GameEventType.CharacterLogin,
                CharacterId = 100,
                Description = "角色登录"
            };

            Assert.True(evt.Timestamp > 0);
            var eventTime = new DateTime(evt.Timestamp, DateTimeKind.Utc);
            Assert.True((DateTime.UtcNow - eventTime).TotalSeconds < 5);
        }

        [Fact]
        public void GameEvent_EventId_IsUniquePerInstance()
        {
            var evt1 = new GameEvent { EventType = GameEventType.CombatDamageDealt };
            var evt2 = new GameEvent { EventType = GameEventType.CombatDamageDealt };

            Assert.NotEqual(evt1.EventId, evt2.EventId);
        }

        [Fact]
        public void GameEvent_Metadata_CanStoreAdditionalData()
        {
            var evt = new GameEvent
            {
                EventType = GameEventType.CombatPlayerKill,
                CharacterId = 100,
                Metadata = new Dictionary<string, string>
                {
                    ["killerId"] = "200",
                    ["weapon"] = "sword",
                    ["criticalHit"] = "true"
                }
            };

            Assert.Equal(3, evt.Metadata.Count);
            Assert.Equal("200", evt.Metadata["killerId"]);
        }

        #endregion

        #region Character Lifecycle Simulation Tests - 角色生命周期模拟

        [Fact]
        public void CharacterLifecycle_CreateLoginDamageDeathResurrect()
        {
            // 1. 创建角色
            var info = new CharacterInfo
            {
                CharacterId = 1001,
                CharacterName = "TestWarrior",
                Level = 30,
                MaxHealth = 1000f,
                CurrentHealth = 1000f,
                IsAlive = true,
                CreatedTime = DateTime.UtcNow
            };

            Assert.Equal(1001UL, info.CharacterId);

            // 2. 登录
            info.LastLoginTime = DateTime.UtcNow;
            Assert.True(info.LastLoginTime > DateTime.MinValue);

            // 3. 受伤
            info.CurrentHealth -= 300f;
            info.LastDamageTime = DateTime.UtcNow;
            Assert.Equal(700f, info.CurrentHealth);
            Assert.True(info.LastDamageTime > DateTime.MinValue);

            // 4. 死亡
            info.CurrentHealth = 0;
            info.IsAlive = false;
            info.DeathCount++;
            info.LastDeathTime = DateTime.UtcNow;
            Assert.False(info.IsAlive);
            Assert.Equal(1, info.DeathCount);

            // 5. 复活
            info.IsAlive = true;
            info.CurrentHealth = info.MaxHealth * 0.3f;
            info.ResurrectionCount++;
            Assert.True(info.IsAlive);
            Assert.Equal(300f, info.CurrentHealth);
            Assert.Equal(1, info.ResurrectionCount);
        }

        [Fact]
        public void CharacterInfo_TimeSequence_IsChronological()
        {
            var info = new CharacterInfo();

            var createdTime = DateTime.UtcNow;
            info.CreatedTime = createdTime;

            var loginTime = createdTime.AddSeconds(1);
            info.LastLoginTime = loginTime;

            var damageTime = loginTime.AddSeconds(10);
            info.LastDamageTime = damageTime;

            var deathTime = damageTime.AddSeconds(5);
            info.LastDeathTime = deathTime;

            Assert.True(info.CreatedTime < info.LastLoginTime);
            Assert.True(info.LastLoginTime < info.LastDamageTime);
            Assert.True(info.LastDamageTime < info.LastDeathTime);
        }

        #endregion
    }
}
