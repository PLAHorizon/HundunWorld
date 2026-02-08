using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;

namespace Horizon.Game.Gateway.Tests
{
    /// <summary>
    /// 客户端功能集成测试
    /// 测试客户端相关的消息类型和DTO：效果同步、AOI视野更新、移动速度验证
    /// </summary>
    public class ClientFeatureTests
    {
        #region EffectSyncAction Tests - 效果同步操作类型

        [Fact]
        public void EffectSyncAction_HasExpectedValues()
        {
            Assert.Equal(0, (int)EffectSyncAction.Apply);
            Assert.Equal(1, (int)EffectSyncAction.Remove);
            Assert.Equal(2, (int)EffectSyncAction.Refresh);
            Assert.Equal(3, (int)EffectSyncAction.Stack);
        }

        [Fact]
        public void EffectSyncAction_HasFourValues()
        {
            var values = Enum.GetValues<EffectSyncAction>();
            Assert.Equal(4, values.Length);
        }

        [Theory]
        [InlineData(EffectSyncAction.Apply, "Apply")]
        [InlineData(EffectSyncAction.Remove, "Remove")]
        [InlineData(EffectSyncAction.Refresh, "Refresh")]
        [InlineData(EffectSyncAction.Stack, "Stack")]
        public void EffectSyncAction_HasCorrectName(EffectSyncAction action, string expectedName)
        {
            Assert.Equal(expectedName, action.ToString());
        }

        [Fact]
        public void EffectSyncAction_AllValues_AreDistinct()
        {
            var values = Enum.GetValues<EffectSyncAction>();
            var uniqueValues = values.Select(v => (int)v).Distinct().Count();
            Assert.Equal(values.Length, uniqueValues);
        }

        #endregion

        #region EffectSyncMessage Tests - 效果同步消息

        [Fact]
        public void EffectSyncMessage_DefaultMessageType_IsEffectSync()
        {
            var msg = new EffectSyncMessage();
            Assert.Equal(MessageType.EffectSync, msg.Type);
        }

        [Fact]
        public void EffectSyncMessage_DefaultServiceType_IsCombat()
        {
            var msg = new EffectSyncMessage();
            Assert.Equal(ServiceType.Combat, msg.ServiceType);
        }

        [Fact]
        public void EffectSyncMessage_DefaultEffectName_IsEmpty()
        {
            var msg = new EffectSyncMessage();
            Assert.Equal("", msg.EffectName);
        }

        [Fact]
        public void EffectSyncMessage_DefaultValues_AreZeroOrDefault()
        {
            var msg = new EffectSyncMessage();
            Assert.Equal(0UL, msg.TargetId);
            Assert.Equal(0UL, msg.SourceId);
            Assert.Equal(0, msg.EffectId);
            Assert.Equal(EffectSyncAction.Apply, msg.Action);
            Assert.Equal(0f, msg.RemainingDuration);
            Assert.Equal(0, msg.Stacks);
            Assert.Equal(0f, msg.Value);
            Assert.False(msg.IsPercentage);
        }

        [Fact]
        public void EffectSyncMessage_CanSetTargetId()
        {
            var msg = new EffectSyncMessage { TargetId = 5001UL };
            Assert.Equal(5001UL, msg.TargetId);
        }

        [Fact]
        public void EffectSyncMessage_CanSetSourceId()
        {
            var msg = new EffectSyncMessage { SourceId = 6001UL };
            Assert.Equal(6001UL, msg.SourceId);
        }

        [Fact]
        public void EffectSyncMessage_CanSetEffectId()
        {
            var msg = new EffectSyncMessage { EffectId = 1001 };
            Assert.Equal(1001, msg.EffectId);
        }

        [Fact]
        public void EffectSyncMessage_CanSetEffectName()
        {
            var msg = new EffectSyncMessage { EffectName = "烈火焚身" };
            Assert.Equal("烈火焚身", msg.EffectName);
        }

        [Fact]
        public void EffectSyncMessage_CanSetAction()
        {
            var msg = new EffectSyncMessage { Action = EffectSyncAction.Stack };
            Assert.Equal(EffectSyncAction.Stack, msg.Action);
        }

        [Fact]
        public void EffectSyncMessage_CanSetRemainingDuration()
        {
            var msg = new EffectSyncMessage { RemainingDuration = 15.5f };
            Assert.Equal(15.5f, msg.RemainingDuration);
        }

        [Fact]
        public void EffectSyncMessage_CanSetStacks()
        {
            var msg = new EffectSyncMessage { Stacks = 3 };
            Assert.Equal(3, msg.Stacks);
        }

        [Fact]
        public void EffectSyncMessage_CanSetPercentageValue()
        {
            var msg = new EffectSyncMessage { Value = 0.25f, IsPercentage = true };
            Assert.Equal(0.25f, msg.Value);
            Assert.True(msg.IsPercentage);
        }

        [Fact]
        public void EffectSyncMessage_CompleteBuffSyncWorkflow()
        {
            var msg = new EffectSyncMessage
            {
                TargetId = 100UL,
                SourceId = 200UL,
                EffectId = 3001,
                EffectName = "寒冰护盾",
                Action = EffectSyncAction.Apply,
                RemainingDuration = 30.0f,
                Stacks = 1,
                Value = 500f,
                IsPercentage = false
            };

            Assert.Equal(100UL, msg.TargetId);
            Assert.Equal(200UL, msg.SourceId);
            Assert.Equal(3001, msg.EffectId);
            Assert.Equal("寒冰护盾", msg.EffectName);
            Assert.Equal(EffectSyncAction.Apply, msg.Action);
            Assert.Equal(30.0f, msg.RemainingDuration);
            Assert.Equal(1, msg.Stacks);
            Assert.Equal(500f, msg.Value);
            Assert.False(msg.IsPercentage);
            Assert.Equal(MessageType.EffectSync, msg.Type);
        }

        #endregion

        #region AoiEntityInfo Tests - AOI实体信息

        [Fact]
        public void AoiEntityInfo_DefaultValues_AreZeroOrDefault()
        {
            var info = new AoiEntityInfo();
            Assert.Equal(0UL, info.EntityId);
            Assert.Equal(NetworkEntityType.Unknown, info.EntityType);
            Assert.Equal("", info.Name);
            Assert.NotNull(info.Position);
            Assert.Equal(0, info.Level);
            Assert.Equal(0f, info.CurrentHealth);
            Assert.Equal(0f, info.MaxHealth);
        }

        [Fact]
        public void AoiEntityInfo_CanSetEntityId()
        {
            var info = new AoiEntityInfo { EntityId = 7777UL };
            Assert.Equal(7777UL, info.EntityId);
        }

        [Fact]
        public void AoiEntityInfo_CanSetEntityType()
        {
            var info = new AoiEntityInfo { EntityType = NetworkEntityType.Monster };
            Assert.Equal(NetworkEntityType.Monster, info.EntityType);
        }

        [Fact]
        public void AoiEntityInfo_CanSetName()
        {
            var info = new AoiEntityInfo { Name = "赤焰狼王" };
            Assert.Equal("赤焰狼王", info.Name);
        }

        [Fact]
        public void AoiEntityInfo_CanSetPosition()
        {
            var info = new AoiEntityInfo
            {
                Position = new Position { X = 50.0f, Y = 10.0f, Z = -30.0f }
            };
            Assert.Equal(50.0f, info.Position.X);
            Assert.Equal(10.0f, info.Position.Y);
            Assert.Equal(-30.0f, info.Position.Z);
        }

        [Fact]
        public void AoiEntityInfo_CanSetAllProperties()
        {
            var info = new AoiEntityInfo
            {
                EntityId = 888UL,
                EntityType = NetworkEntityType.Npc,
                Name = "药师",
                Position = new Position { X = 5, Y = 0, Z = 10 },
                Level = 20,
                CurrentHealth = 3000f,
                MaxHealth = 3000f
            };

            Assert.Equal(888UL, info.EntityId);
            Assert.Equal(NetworkEntityType.Npc, info.EntityType);
            Assert.Equal("药师", info.Name);
            Assert.Equal(5f, info.Position.X);
            Assert.Equal(20, info.Level);
            Assert.Equal(3000f, info.CurrentHealth);
            Assert.Equal(3000f, info.MaxHealth);
        }

        [Fact]
        public void AoiEntityInfo_HealthPercentage_Calculable()
        {
            var info = new AoiEntityInfo
            {
                CurrentHealth = 600f,
                MaxHealth = 1200f
            };

            float healthPercentage = info.MaxHealth > 0 ? info.CurrentHealth / info.MaxHealth : 0;
            Assert.Equal(0.5f, healthPercentage);
        }

        [Fact]
        public void AoiEntityInfo_ZeroMaxHealth_SafePercentage()
        {
            var info = new AoiEntityInfo
            {
                CurrentHealth = 0f,
                MaxHealth = 0f
            };

            float healthPercentage = info.MaxHealth > 0 ? info.CurrentHealth / info.MaxHealth : 0;
            Assert.Equal(0f, healthPercentage);
        }

        #endregion

        #region AoiUpdateMessage Tests - AOI视野更新消息

        [Fact]
        public void AoiUpdateMessage_DefaultMessageType_IsAoiUpdate()
        {
            var msg = new AoiUpdateMessage();
            Assert.Equal(MessageType.AoiUpdate, msg.Type);
        }

        [Fact]
        public void AoiUpdateMessage_DefaultServiceType_IsGame()
        {
            var msg = new AoiUpdateMessage();
            Assert.Equal(ServiceType.Game, msg.ServiceType);
        }

        [Fact]
        public void AoiUpdateMessage_DefaultValues_AreZeroOrEmpty()
        {
            var msg = new AoiUpdateMessage();
            Assert.Equal(0UL, msg.PlayerId);
            Assert.NotNull(msg.EnteredEntities);
            Assert.Empty(msg.EnteredEntities);
            Assert.NotNull(msg.ExitedEntityIds);
            Assert.Empty(msg.ExitedEntityIds);
            Assert.Equal(0f, msg.ViewRange);
        }

        [Fact]
        public void AoiUpdateMessage_CanSetPlayerId()
        {
            var msg = new AoiUpdateMessage { PlayerId = 10001UL };
            Assert.Equal(10001UL, msg.PlayerId);
        }

        [Fact]
        public void AoiUpdateMessage_CanSetViewRange()
        {
            var msg = new AoiUpdateMessage { ViewRange = 150.0f };
            Assert.Equal(150.0f, msg.ViewRange);
        }

        [Fact]
        public void AoiUpdateMessage_CanAddEnteredEntities()
        {
            var msg = new AoiUpdateMessage();
            msg.EnteredEntities.Add(new AoiEntityInfo
            {
                EntityId = 501UL,
                EntityType = NetworkEntityType.RemotePlayer,
                Name = "远程玩家A"
            });
            msg.EnteredEntities.Add(new AoiEntityInfo
            {
                EntityId = 502UL,
                EntityType = NetworkEntityType.Monster,
                Name = "野猪"
            });

            Assert.Equal(2, msg.EnteredEntities.Count);
            Assert.Equal(501UL, msg.EnteredEntities[0].EntityId);
            Assert.Equal(502UL, msg.EnteredEntities[1].EntityId);
        }

        [Fact]
        public void AoiUpdateMessage_CanAddExitedEntityIds()
        {
            var msg = new AoiUpdateMessage();
            msg.ExitedEntityIds.Add(301UL);
            msg.ExitedEntityIds.Add(302UL);
            msg.ExitedEntityIds.Add(303UL);

            Assert.Equal(3, msg.ExitedEntityIds.Count);
            Assert.Contains(301UL, msg.ExitedEntityIds);
            Assert.Contains(302UL, msg.ExitedEntityIds);
            Assert.Contains(303UL, msg.ExitedEntityIds);
        }

        [Fact]
        public void AoiUpdateMessage_CompleteAoiUpdateWorkflow()
        {
            var msg = new AoiUpdateMessage
            {
                PlayerId = 1UL,
                ViewRange = 200.0f,
                EnteredEntities = new List<AoiEntityInfo>
                {
                    new AoiEntityInfo
                    {
                        EntityId = 10UL,
                        EntityType = NetworkEntityType.RemotePlayer,
                        Name = "侠客",
                        Position = new Position { X = 100, Y = 0, Z = 100 },
                        Level = 30,
                        CurrentHealth = 5000f,
                        MaxHealth = 5000f
                    },
                    new AoiEntityInfo
                    {
                        EntityId = 20UL,
                        EntityType = NetworkEntityType.Monster,
                        Name = "青龙",
                        Position = new Position { X = 120, Y = 0, Z = 80 },
                        Level = 50,
                        CurrentHealth = 20000f,
                        MaxHealth = 20000f
                    }
                },
                ExitedEntityIds = new List<ulong> { 5UL, 6UL }
            };

            Assert.Equal(1UL, msg.PlayerId);
            Assert.Equal(200.0f, msg.ViewRange);
            Assert.Equal(2, msg.EnteredEntities.Count);
            Assert.Equal(2, msg.ExitedEntityIds.Count);
            Assert.Equal(MessageType.AoiUpdate, msg.Type);
            Assert.Equal(ServiceType.Game, msg.ServiceType);
        }

        #endregion

        #region MovementSpeedValidationMessage Tests - 移动速度验证消息

        [Fact]
        public void MovementSpeedValidationMessage_DefaultMessageType_IsMovementSpeedValidation()
        {
            var msg = new MovementSpeedValidationMessage();
            Assert.Equal(MessageType.MovementSpeedValidation, msg.Type);
        }

        [Fact]
        public void MovementSpeedValidationMessage_DefaultServiceType_IsGame()
        {
            var msg = new MovementSpeedValidationMessage();
            Assert.Equal(ServiceType.Game, msg.ServiceType);
        }

        [Fact]
        public void MovementSpeedValidationMessage_DefaultValues_AreZeroOrDefault()
        {
            var msg = new MovementSpeedValidationMessage();
            Assert.Equal(0UL, msg.CharacterId);
            Assert.False(msg.IsValid);
            Assert.Equal(0f, msg.MeasuredSpeed);
            Assert.Equal(0f, msg.MaxAllowedSpeed);
            Assert.NotNull(msg.CorrectedPosition);
            Assert.Equal(0, msg.ViolationCount);
        }

        [Fact]
        public void MovementSpeedValidationMessage_CanSetCharacterId()
        {
            var msg = new MovementSpeedValidationMessage { CharacterId = 9999UL };
            Assert.Equal(9999UL, msg.CharacterId);
        }

        [Fact]
        public void MovementSpeedValidationMessage_ValidMovementScenario()
        {
            var msg = new MovementSpeedValidationMessage
            {
                CharacterId = 1001UL,
                IsValid = true,
                MeasuredSpeed = 5.0f,
                MaxAllowedSpeed = 10.0f,
                ViolationCount = 0
            };

            Assert.Equal(1001UL, msg.CharacterId);
            Assert.True(msg.IsValid);
            Assert.True(msg.MeasuredSpeed <= msg.MaxAllowedSpeed);
            Assert.Equal(0, msg.ViolationCount);
        }

        [Fact]
        public void MovementSpeedValidationMessage_SpeedViolationWithCorrection()
        {
            var msg = new MovementSpeedValidationMessage
            {
                CharacterId = 2002UL,
                IsValid = false,
                MeasuredSpeed = 50.0f,
                MaxAllowedSpeed = 10.0f,
                CorrectedPosition = new Position { X = 10.0f, Y = 0f, Z = 20.0f },
                ViolationCount = 1
            };

            Assert.Equal(2002UL, msg.CharacterId);
            Assert.False(msg.IsValid);
            Assert.True(msg.MeasuredSpeed > msg.MaxAllowedSpeed);
            Assert.Equal(10.0f, msg.CorrectedPosition.X);
            Assert.Equal(0f, msg.CorrectedPosition.Y);
            Assert.Equal(20.0f, msg.CorrectedPosition.Z);
            Assert.Equal(1, msg.ViolationCount);
        }

        [Fact]
        public void MovementSpeedValidationMessage_ViolationCountTracking()
        {
            var msg = new MovementSpeedValidationMessage
            {
                CharacterId = 3003UL,
                IsValid = false,
                MeasuredSpeed = 100.0f,
                MaxAllowedSpeed = 10.0f,
                ViolationCount = 5
            };

            Assert.Equal(5, msg.ViolationCount);
            Assert.False(msg.IsValid);
            Assert.Equal(MessageType.MovementSpeedValidation, msg.Type);
        }

        #endregion

        #region MessageType Client Feature Extension Tests - 消息类型客户端功能扩展

        [Fact]
        public void MessageType_EffectSync_HasCorrectValue()
        {
            Assert.Equal(1334, (ushort)MessageType.EffectSync);
        }

        [Fact]
        public void MessageType_AoiUpdate_HasCorrectValue()
        {
            Assert.Equal(1335, (ushort)MessageType.AoiUpdate);
        }

        [Fact]
        public void MessageType_MovementSpeedValidation_HasCorrectValue()
        {
            Assert.Equal(1336, (ushort)MessageType.MovementSpeedValidation);
        }

        [Fact]
        public void MessageType_EffectSync_IsDefined()
        {
            Assert.True(Enum.IsDefined(typeof(MessageType), MessageType.EffectSync));
        }

        [Fact]
        public void MessageType_AoiUpdate_IsDefined()
        {
            Assert.True(Enum.IsDefined(typeof(MessageType), MessageType.AoiUpdate));
        }

        [Fact]
        public void MessageType_MovementSpeedValidation_IsDefined()
        {
            Assert.True(Enum.IsDefined(typeof(MessageType), MessageType.MovementSpeedValidation));
        }

        #endregion

        #region Cross-Feature Workflow Tests - 跨功能工作流

        [Fact]
        public void AoiUpdate_EnteredEntity_CanReceiveEffectSync()
        {
            var aoiEntity = new AoiEntityInfo
            {
                EntityId = 500UL,
                EntityType = NetworkEntityType.Monster,
                Name = "毒蛇"
            };

            var effectMsg = new EffectSyncMessage
            {
                TargetId = aoiEntity.EntityId,
                SourceId = 1UL,
                EffectId = 2001,
                EffectName = "剧毒",
                Action = EffectSyncAction.Apply,
                RemainingDuration = 10.0f,
                Stacks = 1,
                Value = 50f,
                IsPercentage = false
            };

            Assert.Equal(aoiEntity.EntityId, effectMsg.TargetId);
        }

        [Fact]
        public void EffectSyncMessage_StackThenRefresh_Workflow()
        {
            var stackMsg = new EffectSyncMessage
            {
                TargetId = 100UL,
                EffectId = 4001,
                EffectName = "中毒",
                Action = EffectSyncAction.Stack,
                Stacks = 3,
                Value = 30f
            };

            var refreshMsg = new EffectSyncMessage
            {
                TargetId = stackMsg.TargetId,
                EffectId = stackMsg.EffectId,
                EffectName = stackMsg.EffectName,
                Action = EffectSyncAction.Refresh,
                Stacks = stackMsg.Stacks,
                RemainingDuration = 15.0f,
                Value = stackMsg.Value
            };

            Assert.Equal(stackMsg.TargetId, refreshMsg.TargetId);
            Assert.Equal(stackMsg.EffectId, refreshMsg.EffectId);
            Assert.Equal(EffectSyncAction.Refresh, refreshMsg.Action);
            Assert.Equal(15.0f, refreshMsg.RemainingDuration);
        }

        [Fact]
        public void MovementSpeedValidation_WithAoiContext_ConsistentCharacterId()
        {
            var aoiMsg = new AoiUpdateMessage { PlayerId = 777UL };

            var validationMsg = new MovementSpeedValidationMessage
            {
                CharacterId = aoiMsg.PlayerId,
                IsValid = true,
                MeasuredSpeed = 8.0f,
                MaxAllowedSpeed = 10.0f
            };

            Assert.Equal(aoiMsg.PlayerId, validationMsg.CharacterId);
        }

        #endregion
    }
}
