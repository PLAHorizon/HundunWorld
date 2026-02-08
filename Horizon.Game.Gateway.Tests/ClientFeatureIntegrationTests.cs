using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;

namespace Horizon.Game.Gateway.Tests
{
    /// <summary>
    /// 客户端功能集成测试
    /// 测试支持客户端功能的消息DTO和网络同步相关数据结构
    /// </summary>
    public class ClientFeatureIntegrationTests
    {
        #region MoveRequest Tests - 移动请求消息

        [Fact]
        public void MoveRequest_DefaultMessageType_IsMovement()
        {
            var request = new MoveRequest();
            Assert.Equal(MessageType.Movement, request.Type);
        }

        [Fact]
        public void MoveRequest_DefaultServiceType_IsGame()
        {
            var request = new MoveRequest();
            Assert.Equal(ServiceType.Game, request.ServiceType);
        }

        [Fact]
        public void MoveRequest_CanSetPosition()
        {
            var request = new MoveRequest
            {
                TargetX = 10.5f,
                TargetY = 20.3f,
                TargetZ = 30.1f
            };
            Assert.Equal(10.5f, request.TargetX);
            Assert.Equal(20.3f, request.TargetY);
            Assert.Equal(30.1f, request.TargetZ);
        }

        [Fact]
        public void MoveRequest_CanSetSpeed()
        {
            var request = new MoveRequest { Speed = 5.0f };
            Assert.Equal(5.0f, request.Speed);
        }

        [Fact]
        public void MoveRequest_CanSetTimestamp()
        {
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var request = new MoveRequest { Timestamp = timestamp };
            Assert.Equal(timestamp, request.Timestamp);
        }

        [Fact]
        public void MoveRequest_CanSetCharacterId()
        {
            var request = new MoveRequest { CharacterId = 12345UL };
            Assert.Equal(12345UL, request.CharacterId);
        }

        #endregion

        #region MoveResponse Tests - 移动响应消息

        [Fact]
        public void MoveResponse_DefaultMessageType_IsMovement()
        {
            var response = new MoveResponse();
            Assert.Equal(MessageType.Movement, response.Type);
        }

        [Fact]
        public void MoveResponse_CanSetSuccess()
        {
            var response = new MoveResponse { Success = true };
            Assert.True(response.Success);
        }

        [Fact]
        public void MoveResponse_CanSetCharacterId()
        {
            var response = new MoveResponse { CharacterId = 67890UL };
            Assert.Equal(67890UL, response.CharacterId);
        }

        #endregion

        #region SkillCooldownUpdateMessage Tests - 技能冷却更新消息

        [Fact]
        public void SkillCooldownUpdateMessage_DefaultMessageType_IsSkillCooldown()
        {
            var msg = new SkillCooldownUpdateMessage();
            Assert.Equal(MessageType.SkillCooldown, msg.Type);
        }

        [Fact]
        public void SkillCooldownUpdateMessage_DefaultServiceType_IsGame()
        {
            var msg = new SkillCooldownUpdateMessage();
            Assert.Equal(ServiceType.Game, msg.ServiceType);
        }

        [Fact]
        public void SkillCooldownUpdateMessage_CanSetSkillId()
        {
            var msg = new SkillCooldownUpdateMessage { SkillId = 101 };
            Assert.Equal(101, msg.SkillId);
        }

        [Fact]
        public void SkillCooldownUpdateMessage_CanSetCooldownTime()
        {
            var msg = new SkillCooldownUpdateMessage { CooldownTime = 5000 };
            Assert.Equal(5000, msg.CooldownTime);
        }

        [Fact]
        public void SkillCooldownUpdateMessage_CanSetCharacterId()
        {
            var msg = new SkillCooldownUpdateMessage { CharacterId = 999UL };
            Assert.Equal(999UL, msg.CharacterId);
        }

        [Fact]
        public void SkillCooldownUpdateMessage_CanSetUpdateTime()
        {
            var updateTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var msg = new SkillCooldownUpdateMessage { UpdateTime = updateTime };
            Assert.Equal(updateTime, msg.UpdateTime);
        }

        #endregion

        #region SkillCooldownQueryResponse Tests - 技能冷却查询响应

        [Fact]
        public void SkillCooldownQueryResponse_DefaultMessageType_IsSkillCooldown()
        {
            var response = new SkillCooldownQueryResponse();
            Assert.Equal(MessageType.SkillCooldown, response.Type);
        }

        [Fact]
        public void SkillCooldownQueryResponse_DefaultCooldowns_IsEmpty()
        {
            var response = new SkillCooldownQueryResponse();
            Assert.NotNull(response.SkillCooldowns);
            Assert.Empty(response.SkillCooldowns);
        }

        [Fact]
        public void SkillCooldownQueryResponse_CanTrackMultipleSkillCooldowns()
        {
            var response = new SkillCooldownQueryResponse();
            response.SkillCooldowns[1] = 3000;  // 技能1 冷却3秒
            response.SkillCooldowns[2] = 5000;  // 技能2 冷却5秒
            response.SkillCooldowns[3] = 10000; // 技能3 冷却10秒

            Assert.Equal(3, response.SkillCooldowns.Count);
            Assert.Equal(3000, response.SkillCooldowns[1]);
            Assert.Equal(5000, response.SkillCooldowns[2]);
            Assert.Equal(10000, response.SkillCooldowns[3]);
        }

        #endregion

        #region SkillCooldownQueryRequest Tests - 技能冷却查询请求

        [Fact]
        public void SkillCooldownQueryRequest_DefaultMessageType_IsSkillCooldown()
        {
            var request = new SkillCooldownQueryRequest();
            Assert.Equal(MessageType.SkillCooldown, request.Type);
        }

        #endregion

        #region DamageMessage Tests - 伤害消息字段完整性

        [Fact]
        public void DamageMessage_CanSetImpactPosition()
        {
            var msg = new DamageMessage
            {
                ImpactPosition = new Position { X = 1.0f, Y = 2.0f, Z = 3.0f }
            };
            Assert.Equal(1.0f, msg.ImpactPosition.X);
            Assert.Equal(2.0f, msg.ImpactPosition.Y);
            Assert.Equal(3.0f, msg.ImpactPosition.Z);
        }

        [Fact]
        public void DamageMessage_CanSetCriticalAndDodge()
        {
            var msg = new DamageMessage
            {
                IsCritical = true,
                IsDodged = false,
                IsBlocked = true
            };
            Assert.True(msg.IsCritical);
            Assert.False(msg.IsDodged);
            Assert.True(msg.IsBlocked);
        }

        [Fact]
        public void DamageMessage_CanSetRemainingHealth()
        {
            var msg = new DamageMessage
            {
                Damage = 100,
                RemainingHealth = 900
            };
            Assert.Equal(100, msg.Damage);
            Assert.Equal(900, msg.RemainingHealth);
        }

        #endregion

        #region DeathMessage Tests - 死亡消息

        [Fact]
        public void DeathMessage_CanSetDeathPosition()
        {
            var msg = new DeathMessage
            {
                DeathPosition = new Position { X = 10.0f, Y = 0.0f, Z = 20.0f }
            };
            Assert.Equal(10.0f, msg.DeathPosition.X);
            Assert.Equal(0.0f, msg.DeathPosition.Y);
            Assert.Equal(20.0f, msg.DeathPosition.Z);
        }

        [Fact]
        public void DeathMessage_CanSetDeceasedAndKiller()
        {
            var msg = new DeathMessage
            {
                DeceasedId = 100UL,
                KillerId = 200UL
            };
            Assert.Equal(100UL, msg.DeceasedId);
            Assert.Equal(200UL, msg.KillerId);
        }

        #endregion

        #region ResurrectMessage Tests - 复活消息

        [Fact]
        public void ResurrectMessage_CanSetResurrectPosition()
        {
            var msg = new ResurrectMessage
            {
                ResurrectPosition = new Position { X = 5.0f, Y = 1.0f, Z = 15.0f }
            };
            Assert.Equal(5.0f, msg.ResurrectPosition.X);
            Assert.Equal(1.0f, msg.ResurrectPosition.Y);
            Assert.Equal(15.0f, msg.ResurrectPosition.Z);
        }

        [Fact]
        public void ResurrectMessage_CanSetRemainingHealth()
        {
            var msg = new ResurrectMessage
            {
                ResurrectedId = 300UL,
                RemainingHealth = 500
            };
            Assert.Equal(300UL, msg.ResurrectedId);
            Assert.Equal(500, msg.RemainingHealth);
        }

        #endregion

        #region SkillCastMessage Tests - 技能施放消息

        [Fact]
        public void SkillCastMessage_CanSetPositions()
        {
            var msg = new SkillCastMessage
            {
                StartPosition = new Position { X = 1, Y = 2, Z = 3 },
                TargetPosition = new Position { X = 4, Y = 5, Z = 6 }
            };
            Assert.Equal(1, msg.StartPosition.X);
            Assert.Equal(4, msg.TargetPosition.X);
        }

        [Fact]
        public void SkillCastMessage_CanSetSuccessAndMessage()
        {
            var msg = new SkillCastMessage
            {
                Success = false,
                Message = "能量不足"
            };
            Assert.False(msg.Success);
            Assert.Equal("能量不足", msg.Message);
        }

        [Fact]
        public void SkillCastMessage_CanSetEnergyCost()
        {
            var msg = new SkillCastMessage
            {
                SkillId = 5,
                EnergyCost = 30.0f
            };
            Assert.Equal(5, msg.SkillId);
            Assert.Equal(30.0f, msg.EnergyCost);
        }

        #endregion

        #region EffectMessage Tests - 效果消息

        [Fact]
        public void EffectMessage_CanSetEffectFields()
        {
            var msg = new EffectMessage
            {
                EffectId = 42,
                EffectName = "灼烧",
                EffectType = 2, // DamageOverTime
                TargetId = 100UL,
                RemainingDuration = 5.0f
            };
            Assert.Equal(42, msg.EffectId);
            Assert.Equal("灼烧", msg.EffectName);
            Assert.Equal(2, msg.EffectType);
            Assert.Equal(100UL, msg.TargetId);
            Assert.Equal(5.0f, msg.RemainingDuration);
        }

        #endregion

        #region Position DTO Tests - 位置数据

        [Fact]
        public void Position_DefaultValues_AreZero()
        {
            var pos = new Position();
            Assert.Equal(0, pos.X);
            Assert.Equal(0, pos.Y);
            Assert.Equal(0, pos.Z);
        }

        [Fact]
        public void Position_CanSetCoordinates()
        {
            var pos = new Position { X = 100.5f, Y = 50.2f, Z = -30.8f };
            Assert.Equal(100.5f, pos.X);
            Assert.Equal(50.2f, pos.Y);
            Assert.Equal(-30.8f, pos.Z);
        }

        #endregion
    }
}
