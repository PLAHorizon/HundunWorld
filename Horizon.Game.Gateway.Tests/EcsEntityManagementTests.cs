using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;

namespace Horizon.Game.Gateway.Tests
{
    /// <summary>
    /// ECS实体管理集成测试
    /// 测试支持ECS系统的网络实体同步消息DTO和数据结构
    /// </summary>
    public class EcsEntityManagementTests
    {
        #region NetworkEntityType Tests - 网络实体类型

        [Fact]
        public void NetworkEntityType_HasExpectedValues()
        {
            Assert.Equal(0, (int)NetworkEntityType.Unknown);
            Assert.Equal(1, (int)NetworkEntityType.LocalPlayer);
            Assert.Equal(2, (int)NetworkEntityType.RemotePlayer);
            Assert.Equal(3, (int)NetworkEntityType.Npc);
            Assert.Equal(4, (int)NetworkEntityType.Monster);
            Assert.Equal(5, (int)NetworkEntityType.Projectile);
            Assert.Equal(6, (int)NetworkEntityType.Item);
        }

        [Fact]
        public void NetworkEntityType_HasSevenValues()
        {
            var values = Enum.GetValues<NetworkEntityType>();
            Assert.Equal(7, values.Length);
        }

        [Theory]
        [InlineData(NetworkEntityType.Unknown, "Unknown")]
        [InlineData(NetworkEntityType.LocalPlayer, "LocalPlayer")]
        [InlineData(NetworkEntityType.RemotePlayer, "RemotePlayer")]
        [InlineData(NetworkEntityType.Npc, "Npc")]
        [InlineData(NetworkEntityType.Monster, "Monster")]
        [InlineData(NetworkEntityType.Projectile, "Projectile")]
        [InlineData(NetworkEntityType.Item, "Item")]
        public void NetworkEntityType_HasCorrectName(NetworkEntityType type, string expectedName)
        {
            Assert.Equal(expectedName, type.ToString());
        }

        #endregion

        #region DespawnReason Tests - 实体销毁原因

        [Fact]
        public void DespawnReason_HasExpectedValues()
        {
            Assert.Equal(0, (int)DespawnReason.OutOfRange);
            Assert.Equal(1, (int)DespawnReason.Death);
            Assert.Equal(2, (int)DespawnReason.Teleport);
            Assert.Equal(3, (int)DespawnReason.Logout);
        }

        [Fact]
        public void DespawnReason_HasFourValues()
        {
            var values = Enum.GetValues<DespawnReason>();
            Assert.Equal(4, values.Length);
        }

        #endregion

        #region EntitySpawnMessage Tests - 实体生成消息

        [Fact]
        public void EntitySpawnMessage_DefaultMessageType_IsEntitySpawn()
        {
            var msg = new EntitySpawnMessage();
            Assert.Equal(MessageType.EntitySpawn, msg.Type);
        }

        [Fact]
        public void EntitySpawnMessage_DefaultServiceType_IsGame()
        {
            var msg = new EntitySpawnMessage();
            Assert.Equal(ServiceType.Game, msg.ServiceType);
        }

        [Fact]
        public void EntitySpawnMessage_CanSetEntityId()
        {
            var msg = new EntitySpawnMessage { EntityId = 12345UL };
            Assert.Equal(12345UL, msg.EntityId);
        }

        [Fact]
        public void EntitySpawnMessage_CanSetEntityType()
        {
            var msg = new EntitySpawnMessage { EntityType = NetworkEntityType.Monster };
            Assert.Equal(NetworkEntityType.Monster, msg.EntityType);
        }

        [Fact]
        public void EntitySpawnMessage_CanSetEntityName()
        {
            var msg = new EntitySpawnMessage { EntityName = "火焰巨龙" };
            Assert.Equal("火焰巨龙", msg.EntityName);
        }

        [Fact]
        public void EntitySpawnMessage_CanSetLevel()
        {
            var msg = new EntitySpawnMessage { Level = 50 };
            Assert.Equal(50, msg.Level);
        }

        [Fact]
        public void EntitySpawnMessage_CanSetSpawnPosition()
        {
            var msg = new EntitySpawnMessage
            {
                SpawnPosition = new Position { X = 100.5f, Y = 20.3f, Z = -50.0f }
            };
            Assert.Equal(100.5f, msg.SpawnPosition.X);
            Assert.Equal(20.3f, msg.SpawnPosition.Y);
            Assert.Equal(-50.0f, msg.SpawnPosition.Z);
        }

        [Fact]
        public void EntitySpawnMessage_CanSetHealth()
        {
            var msg = new EntitySpawnMessage
            {
                CurrentHealth = 800f,
                MaxHealth = 1000f
            };
            Assert.Equal(800f, msg.CurrentHealth);
            Assert.Equal(1000f, msg.MaxHealth);
        }

        [Fact]
        public void EntitySpawnMessage_DefaultEntityName_IsEmpty()
        {
            var msg = new EntitySpawnMessage();
            Assert.Equal("", msg.EntityName);
        }

        [Fact]
        public void EntitySpawnMessage_DefaultSpawnPosition_IsNotNull()
        {
            var msg = new EntitySpawnMessage();
            Assert.NotNull(msg.SpawnPosition);
        }

        [Fact]
        public void EntitySpawnMessage_FullEntitySetup()
        {
            var msg = new EntitySpawnMessage
            {
                EntityId = 999UL,
                EntityType = NetworkEntityType.Npc,
                EntityName = "铁匠",
                Level = 1,
                SpawnPosition = new Position { X = 10, Y = 0, Z = 20 },
                CurrentHealth = 500f,
                MaxHealth = 500f
            };
            Assert.Equal(999UL, msg.EntityId);
            Assert.Equal(NetworkEntityType.Npc, msg.EntityType);
            Assert.Equal("铁匠", msg.EntityName);
            Assert.Equal(1, msg.Level);
            Assert.Equal(10f, msg.SpawnPosition.X);
            Assert.Equal(500f, msg.CurrentHealth);
            Assert.Equal(500f, msg.MaxHealth);
        }

        #endregion

        #region EntityDespawnMessage Tests - 实体销毁消息

        [Fact]
        public void EntityDespawnMessage_DefaultMessageType_IsEntityDespawn()
        {
            var msg = new EntityDespawnMessage();
            Assert.Equal(MessageType.EntityDespawn, msg.Type);
        }

        [Fact]
        public void EntityDespawnMessage_DefaultServiceType_IsGame()
        {
            var msg = new EntityDespawnMessage();
            Assert.Equal(ServiceType.Game, msg.ServiceType);
        }

        [Fact]
        public void EntityDespawnMessage_CanSetEntityId()
        {
            var msg = new EntityDespawnMessage { EntityId = 67890UL };
            Assert.Equal(67890UL, msg.EntityId);
        }

        [Fact]
        public void EntityDespawnMessage_CanSetReason_OutOfRange()
        {
            var msg = new EntityDespawnMessage { Reason = DespawnReason.OutOfRange };
            Assert.Equal(DespawnReason.OutOfRange, msg.Reason);
        }

        [Fact]
        public void EntityDespawnMessage_CanSetReason_Death()
        {
            var msg = new EntityDespawnMessage { Reason = DespawnReason.Death };
            Assert.Equal(DespawnReason.Death, msg.Reason);
        }

        [Fact]
        public void EntityDespawnMessage_CanSetReason_Teleport()
        {
            var msg = new EntityDespawnMessage { Reason = DespawnReason.Teleport };
            Assert.Equal(DespawnReason.Teleport, msg.Reason);
        }

        [Fact]
        public void EntityDespawnMessage_CanSetReason_Logout()
        {
            var msg = new EntityDespawnMessage { Reason = DespawnReason.Logout };
            Assert.Equal(DespawnReason.Logout, msg.Reason);
        }

        [Fact]
        public void EntityDespawnMessage_FullDespawnSetup()
        {
            var msg = new EntityDespawnMessage
            {
                EntityId = 42UL,
                Reason = DespawnReason.Death
            };
            Assert.Equal(42UL, msg.EntityId);
            Assert.Equal(DespawnReason.Death, msg.Reason);
            Assert.Equal(MessageType.EntityDespawn, msg.Type);
        }

        #endregion

        #region MessageType Entity Extension Tests - 消息类型实体扩展

        [Fact]
        public void MessageType_EntitySpawn_HasCorrectValue()
        {
            Assert.Equal(1332, (ushort)MessageType.EntitySpawn);
        }

        [Fact]
        public void MessageType_EntityDespawn_HasCorrectValue()
        {
            Assert.Equal(1333, (ushort)MessageType.EntityDespawn);
        }

        [Fact]
        public void MessageType_EntitySpawn_IsDefined()
        {
            Assert.True(Enum.IsDefined(typeof(MessageType), MessageType.EntitySpawn));
        }

        [Fact]
        public void MessageType_EntityDespawn_IsDefined()
        {
            Assert.True(Enum.IsDefined(typeof(MessageType), MessageType.EntityDespawn));
        }

        #endregion

        #region Entity Spawn/Despawn Workflow Tests - 实体生成/销毁工作流

        [Fact]
        public void EntitySpawn_ThenDespawn_MaintainsEntityId()
        {
            var spawnMsg = new EntitySpawnMessage
            {
                EntityId = 1001UL,
                EntityType = NetworkEntityType.Monster,
                EntityName = "野猪"
            };

            var despawnMsg = new EntityDespawnMessage
            {
                EntityId = spawnMsg.EntityId,
                Reason = DespawnReason.Death
            };

            Assert.Equal(spawnMsg.EntityId, despawnMsg.EntityId);
        }

        [Fact]
        public void EntitySpawn_MultipleEntities_HaveUniqueIds()
        {
            var entities = new List<EntitySpawnMessage>();
            for (int i = 1; i <= 10; i++)
            {
                entities.Add(new EntitySpawnMessage
                {
                    EntityId = (ulong)i,
                    EntityType = i <= 3 ? NetworkEntityType.RemotePlayer : NetworkEntityType.Monster,
                    EntityName = $"Entity_{i}",
                    Level = i * 5
                });
            }

            var uniqueIds = entities.Select(e => e.EntityId).Distinct().Count();
            Assert.Equal(10, uniqueIds);
        }

        [Fact]
        public void EntitySpawn_PlayerEntities_CorrectTypes()
        {
            var localPlayer = new EntitySpawnMessage { EntityType = NetworkEntityType.LocalPlayer };
            var remotePlayer = new EntitySpawnMessage { EntityType = NetworkEntityType.RemotePlayer };
            var npc = new EntitySpawnMessage { EntityType = NetworkEntityType.Npc };

            Assert.NotEqual(localPlayer.EntityType, remotePlayer.EntityType);
            Assert.NotEqual(localPlayer.EntityType, npc.EntityType);
            Assert.NotEqual(remotePlayer.EntityType, npc.EntityType);
        }

        [Fact]
        public void DespawnReason_AllReasons_AreDistinct()
        {
            var reasons = Enum.GetValues<DespawnReason>();
            var uniqueValues = reasons.Select(r => (int)r).Distinct().Count();
            Assert.Equal(reasons.Length, uniqueValues);
        }

        [Fact]
        public void EntitySpawnMessage_HealthPercentage_Calculable()
        {
            var msg = new EntitySpawnMessage
            {
                CurrentHealth = 750f,
                MaxHealth = 1000f
            };
            
            float healthPercentage = msg.MaxHealth > 0 ? msg.CurrentHealth / msg.MaxHealth : 0;
            Assert.Equal(0.75f, healthPercentage);
        }

        [Fact]
        public void EntitySpawnMessage_ZeroMaxHealth_SafePercentage()
        {
            var msg = new EntitySpawnMessage
            {
                CurrentHealth = 0f,
                MaxHealth = 0f
            };
            
            float healthPercentage = msg.MaxHealth > 0 ? msg.CurrentHealth / msg.MaxHealth : 0;
            Assert.Equal(0f, healthPercentage);
        }

        #endregion

        #region Attack/Damage Message EntityId Consistency Tests - 攻击/伤害消息实体ID一致性

        [Fact]
        public void AttackMessage_EntityIds_MatchSpawnedEntities()
        {
            // 模拟两个已生成的实体
            var attacker = new EntitySpawnMessage { EntityId = 100UL };
            var target = new EntitySpawnMessage { EntityId = 200UL };

            // 创建攻击消息引用这些实体
            var attack = new AttackMessage
            {
                AttackerId = attacker.EntityId,
                TargetId = target.EntityId,
                Damage = 150
            };

            Assert.Equal(100UL, attack.AttackerId);
            Assert.Equal(200UL, attack.TargetId);
        }

        [Fact]
        public void DamageMessage_VictimId_MatchesSpawnedEntity()
        {
            var victim = new EntitySpawnMessage
            {
                EntityId = 300UL,
                EntityName = "测试目标",
                CurrentHealth = 1000f,
                MaxHealth = 1000f
            };

            var damage = new DamageMessage
            {
                VictimId = victim.EntityId,
                Damage = 250,
                RemainingHealth = 750
            };

            Assert.Equal(victim.EntityId, damage.VictimId);
            Assert.Equal(750, damage.RemainingHealth);
        }

        [Fact]
        public void DeathMessage_LeadsToDespawn()
        {
            var death = new DeathMessage
            {
                DeceasedId = 400UL,
                KillerId = 100UL
            };

            var despawn = new EntityDespawnMessage
            {
                EntityId = death.DeceasedId,
                Reason = DespawnReason.Death
            };

            Assert.Equal(death.DeceasedId, despawn.EntityId);
            Assert.Equal(DespawnReason.Death, despawn.Reason);
        }

        #endregion
    }
}
