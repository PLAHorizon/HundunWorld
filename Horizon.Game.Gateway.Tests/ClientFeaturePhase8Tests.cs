using Horizon.Game.Message;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;

namespace Horizon.Game.Gateway.Tests
{
    /// <summary>
    /// 客户端功能集成测试 - 第八阶段
    /// 测试角色管理网络集成、合成金币同步、技能目标验证、截图通知、属性刷新消息
    /// </summary>
    public class ClientFeaturePhase8Tests
    {
        #region MessageType Tests - 新增消息类型

        [Fact]
        public void MessageType_CharacterStateSync_HasCorrectValue()
        {
            Assert.Equal(1363, (int)MessageType.CharacterStateSync);
        }

        [Fact]
        public void MessageType_CraftingGoldSync_HasCorrectValue()
        {
            Assert.Equal(1364, (int)MessageType.CraftingGoldSync);
        }

        [Fact]
        public void MessageType_SkillTargetValidation_HasCorrectValue()
        {
            Assert.Equal(1365, (int)MessageType.SkillTargetValidation);
        }

        [Fact]
        public void MessageType_ScreenshotNotify_HasCorrectValue()
        {
            Assert.Equal(1366, (int)MessageType.ScreenshotNotify);
        }

        [Fact]
        public void MessageType_CharacterAttributeRefresh_HasCorrectValue()
        {
            Assert.Equal(1367, (int)MessageType.CharacterAttributeRefresh);
        }

        [Fact]
        public void MessageType_Phase8Types_AreUnique()
        {
            var values = new[]
            {
                (int)MessageType.CharacterStateSync,
                (int)MessageType.CraftingGoldSync,
                (int)MessageType.SkillTargetValidation,
                (int)MessageType.ScreenshotNotify,
                (int)MessageType.CharacterAttributeRefresh
            };

            Assert.Equal(values.Length, values.Distinct().Count());
        }

        [Fact]
        public void MessageType_Phase8Types_DoNotConflictWithPreviousPhases()
        {
            var phase7Max = (int)MessageType.InventoryExpand; // 1362
            Assert.True((int)MessageType.CharacterStateSync > phase7Max);
            Assert.True((int)MessageType.CraftingGoldSync > phase7Max);
            Assert.True((int)MessageType.SkillTargetValidation > phase7Max);
            Assert.True((int)MessageType.ScreenshotNotify > phase7Max);
            Assert.True((int)MessageType.CharacterAttributeRefresh > phase7Max);
        }

        [Fact]
        public void MessageType_Phase8Types_AreSequential()
        {
            Assert.Equal((int)MessageType.CharacterStateSync + 1, (int)MessageType.CraftingGoldSync);
            Assert.Equal((int)MessageType.CraftingGoldSync + 1, (int)MessageType.SkillTargetValidation);
            Assert.Equal((int)MessageType.SkillTargetValidation + 1, (int)MessageType.ScreenshotNotify);
            Assert.Equal((int)MessageType.ScreenshotNotify + 1, (int)MessageType.CharacterAttributeRefresh);
        }

        #endregion

        #region CharacterStateSyncRequest / Response Tests

        [Fact]
        public void CharacterStateSyncRequest_DefaultValues_AreCorrect()
        {
            var request = new CharacterStateSyncRequest();
            Assert.Equal(MessageType.CharacterStateSync, request.Type);
            Assert.Equal(ServiceType.Game, request.ServiceType);
            Assert.Equal(0UL, request.CharacterId);
            Assert.Equal(0, request.SyncType);
        }

        [Fact]
        public void CharacterStateSyncRequest_SetValues_RetainCorrectly()
        {
            var request = new CharacterStateSyncRequest
            {
                CharacterId = 12345UL,
                SyncType = 1
            };

            Assert.Equal(12345UL, request.CharacterId);
            Assert.Equal(1, request.SyncType);
        }

        [Fact]
        public void CharacterStateSyncRequest_ImplementsINetworkMessage()
        {
            var request = new CharacterStateSyncRequest();
            Assert.IsAssignableFrom<INetworkMessage>(request);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        public void CharacterStateSyncRequest_SyncTypes_AreAccepted(int syncType)
        {
            var request = new CharacterStateSyncRequest { SyncType = syncType };
            Assert.Equal(syncType, request.SyncType);
        }

        [Fact]
        public void CharacterStateSyncResponse_DefaultValues_AreCorrect()
        {
            var response = new CharacterStateSyncResponse();
            Assert.Equal(MessageType.CharacterStateSync, response.Type);
            Assert.Equal(ServiceType.Game, response.ServiceType);
            Assert.False(response.Success);
            Assert.Equal("", response.Message);
            Assert.Equal(0UL, response.CharacterId);
            Assert.Equal(0, response.Level);
            Assert.Equal(0L, response.Experience);
            Assert.Equal(0L, response.Gold);
            Assert.Equal(0f, response.Health);
        }

        [Fact]
        public void CharacterStateSyncResponse_SetValues_RetainCorrectly()
        {
            var response = new CharacterStateSyncResponse
            {
                CharacterId = 99UL,
                Success = true,
                Level = 50,
                Experience = 100000L,
                Gold = 50000L,
                Health = 8500.5f,
                Message = "同步成功"
            };

            Assert.Equal(99UL, response.CharacterId);
            Assert.True(response.Success);
            Assert.Equal(50, response.Level);
            Assert.Equal(100000L, response.Experience);
            Assert.Equal(50000L, response.Gold);
            Assert.Equal(8500.5f, response.Health);
            Assert.Equal("同步成功", response.Message);
        }

        #endregion

        #region CraftingGoldSyncRequest / Response Tests

        [Fact]
        public void CraftingGoldSyncRequest_DefaultValues_AreCorrect()
        {
            var request = new CraftingGoldSyncRequest();
            Assert.Equal(MessageType.CraftingGoldSync, request.Type);
            Assert.Equal(ServiceType.Game, request.ServiceType);
            Assert.Equal(0UL, request.CharacterId);
            Assert.Equal(0L, request.GoldCost);
            Assert.Equal(0, request.RecipeId);
            Assert.Equal(0, request.CraftCount);
        }

        [Fact]
        public void CraftingGoldSyncRequest_SetValues_RetainCorrectly()
        {
            var request = new CraftingGoldSyncRequest
            {
                CharacterId = 200UL,
                GoldCost = 500L,
                RecipeId = 42,
                CraftCount = 3
            };

            Assert.Equal(200UL, request.CharacterId);
            Assert.Equal(500L, request.GoldCost);
            Assert.Equal(42, request.RecipeId);
            Assert.Equal(3, request.CraftCount);
        }

        [Fact]
        public void CraftingGoldSyncRequest_ImplementsINetworkMessage()
        {
            var request = new CraftingGoldSyncRequest();
            Assert.IsAssignableFrom<INetworkMessage>(request);
        }

        [Fact]
        public void CraftingGoldSyncResponse_DefaultValues_AreCorrect()
        {
            var response = new CraftingGoldSyncResponse();
            Assert.Equal(MessageType.CraftingGoldSync, response.Type);
            Assert.Equal(ServiceType.Game, response.ServiceType);
            Assert.False(response.Success);
            Assert.Equal("", response.Message);
            Assert.Equal(0L, response.RemainingGold);
            Assert.Equal(0, response.OutputItemId);
            Assert.Equal(0, response.OutputCount);
        }

        [Fact]
        public void CraftingGoldSyncResponse_SetValues_RetainCorrectly()
        {
            var response = new CraftingGoldSyncResponse
            {
                Success = true,
                Message = "合成成功",
                RemainingGold = 9500L,
                OutputItemId = 1001,
                OutputCount = 1
            };

            Assert.True(response.Success);
            Assert.Equal("合成成功", response.Message);
            Assert.Equal(9500L, response.RemainingGold);
            Assert.Equal(1001, response.OutputItemId);
            Assert.Equal(1, response.OutputCount);
        }

        #endregion

        #region SkillTargetValidationRequest / Response Tests

        [Fact]
        public void SkillTargetValidationRequest_DefaultValues_AreCorrect()
        {
            var request = new SkillTargetValidationRequest();
            Assert.Equal(MessageType.SkillTargetValidation, request.Type);
            Assert.Equal(ServiceType.Game, request.ServiceType);
            Assert.Equal(0UL, request.CasterId);
            Assert.Equal(0UL, request.TargetNetworkId);
            Assert.Equal(0, request.SkillId);
            Assert.Equal(0f, request.CasterPositionX);
            Assert.Equal(0f, request.CasterPositionY);
            Assert.Equal(0f, request.CasterPositionZ);
        }

        [Fact]
        public void SkillTargetValidationRequest_SetValues_RetainCorrectly()
        {
            var request = new SkillTargetValidationRequest
            {
                CasterId = 100UL,
                TargetNetworkId = 200UL,
                SkillId = 5,
                CasterPositionX = 10.5f,
                CasterPositionY = 0.0f,
                CasterPositionZ = -5.3f
            };

            Assert.Equal(100UL, request.CasterId);
            Assert.Equal(200UL, request.TargetNetworkId);
            Assert.Equal(5, request.SkillId);
            Assert.Equal(10.5f, request.CasterPositionX);
            Assert.Equal(0.0f, request.CasterPositionY);
            Assert.Equal(-5.3f, request.CasterPositionZ);
        }

        [Fact]
        public void SkillTargetValidationRequest_ImplementsINetworkMessage()
        {
            var request = new SkillTargetValidationRequest();
            Assert.IsAssignableFrom<INetworkMessage>(request);
        }

        [Fact]
        public void SkillTargetValidationResponse_DefaultValues_AreCorrect()
        {
            var response = new SkillTargetValidationResponse();
            Assert.Equal(MessageType.SkillTargetValidation, response.Type);
            Assert.Equal(ServiceType.Game, response.ServiceType);
            Assert.False(response.IsValid);
            Assert.Equal("", response.Reason);
            Assert.Equal(0UL, response.TargetNetworkId);
            Assert.Equal(0, response.SkillId);
            Assert.Equal(0f, response.CorrectedPositionX);
            Assert.Equal(0f, response.CorrectedPositionY);
            Assert.Equal(0f, response.CorrectedPositionZ);
        }

        [Fact]
        public void SkillTargetValidationResponse_SetValues_RetainCorrectly()
        {
            var response = new SkillTargetValidationResponse
            {
                IsValid = true,
                Reason = "",
                TargetNetworkId = 200UL,
                SkillId = 5,
                CorrectedPositionX = 10.0f,
                CorrectedPositionY = 0.0f,
                CorrectedPositionZ = -5.0f
            };

            Assert.True(response.IsValid);
            Assert.Equal("", response.Reason);
            Assert.Equal(200UL, response.TargetNetworkId);
            Assert.Equal(5, response.SkillId);
            Assert.Equal(10.0f, response.CorrectedPositionX);
            Assert.Equal(0.0f, response.CorrectedPositionY);
            Assert.Equal(-5.0f, response.CorrectedPositionZ);
        }

        [Fact]
        public void SkillTargetValidationResponse_InvalidTarget_HasReason()
        {
            var response = new SkillTargetValidationResponse
            {
                IsValid = false,
                Reason = "目标超出技能范围",
                TargetNetworkId = 300UL,
                SkillId = 7
            };

            Assert.False(response.IsValid);
            Assert.Equal("目标超出技能范围", response.Reason);
        }

        #endregion

        #region ScreenshotNotifyMessage Tests

        [Fact]
        public void ScreenshotNotifyMessage_DefaultValues_AreCorrect()
        {
            var message = new ScreenshotNotifyMessage();
            Assert.Equal(MessageType.ScreenshotNotify, message.Type);
            Assert.Equal(ServiceType.Game, message.ServiceType);
            Assert.Equal(0UL, message.CharacterId);
            Assert.Equal("", message.FilePath);
            Assert.Equal(0L, message.Timestamp);
            Assert.Equal(0, message.ScreenshotType);
        }

        [Fact]
        public void ScreenshotNotifyMessage_SetValues_RetainCorrectly()
        {
            var message = new ScreenshotNotifyMessage
            {
                CharacterId = 999UL,
                FilePath = "Screenshots/Preview_20260209.png",
                Timestamp = 1739085600000L,
                ScreenshotType = 1
            };

            Assert.Equal(999UL, message.CharacterId);
            Assert.Equal("Screenshots/Preview_20260209.png", message.FilePath);
            Assert.Equal(1739085600000L, message.Timestamp);
            Assert.Equal(1, message.ScreenshotType);
        }

        [Fact]
        public void ScreenshotNotifyMessage_ImplementsINetworkMessage()
        {
            var message = new ScreenshotNotifyMessage();
            Assert.IsAssignableFrom<INetworkMessage>(message);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public void ScreenshotNotifyMessage_ScreenshotTypes_AreAccepted(int screenshotType)
        {
            var message = new ScreenshotNotifyMessage { ScreenshotType = screenshotType };
            Assert.Equal(screenshotType, message.ScreenshotType);
        }

        #endregion

        #region CharacterAttributeRefreshRequest / Response Tests

        [Fact]
        public void CharacterAttributeRefreshRequest_DefaultValues_AreCorrect()
        {
            var request = new CharacterAttributeRefreshRequest();
            Assert.Equal(MessageType.CharacterAttributeRefresh, request.Type);
            Assert.Equal(ServiceType.Game, request.ServiceType);
            Assert.Equal(0UL, request.CharacterId);
            Assert.Equal(0, request.RefreshReason);
        }

        [Fact]
        public void CharacterAttributeRefreshRequest_SetValues_RetainCorrectly()
        {
            var request = new CharacterAttributeRefreshRequest
            {
                CharacterId = 500UL,
                RefreshReason = 2
            };

            Assert.Equal(500UL, request.CharacterId);
            Assert.Equal(2, request.RefreshReason);
        }

        [Fact]
        public void CharacterAttributeRefreshRequest_ImplementsINetworkMessage()
        {
            var request = new CharacterAttributeRefreshRequest();
            Assert.IsAssignableFrom<INetworkMessage>(request);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        public void CharacterAttributeRefreshRequest_RefreshReasons_AreAccepted(int reason)
        {
            var request = new CharacterAttributeRefreshRequest { RefreshReason = reason };
            Assert.Equal(reason, request.RefreshReason);
        }

        [Fact]
        public void CharacterAttributeRefreshResponse_DefaultValues_AreCorrect()
        {
            var response = new CharacterAttributeRefreshResponse();
            Assert.Equal(MessageType.CharacterAttributeRefresh, response.Type);
            Assert.Equal(ServiceType.Game, response.ServiceType);
            Assert.False(response.Success);
            Assert.Equal(0UL, response.CharacterId);
            Assert.Equal(0f, response.Attack);
            Assert.Equal(0f, response.Defense);
            Assert.Equal(0f, response.MaxHealth);
            Assert.Equal(0f, response.MaxEnergy);
            Assert.Equal(0, response.CombatPower);
            Assert.Equal("", response.Message);
        }

        [Fact]
        public void CharacterAttributeRefreshResponse_SetValues_RetainCorrectly()
        {
            var response = new CharacterAttributeRefreshResponse
            {
                Success = true,
                CharacterId = 500UL,
                Attack = 1500.5f,
                Defense = 800.0f,
                MaxHealth = 10000.0f,
                MaxEnergy = 5000.0f,
                CombatPower = 25000,
                Message = "属性刷新成功"
            };

            Assert.True(response.Success);
            Assert.Equal(500UL, response.CharacterId);
            Assert.Equal(1500.5f, response.Attack);
            Assert.Equal(800.0f, response.Defense);
            Assert.Equal(10000.0f, response.MaxHealth);
            Assert.Equal(5000.0f, response.MaxEnergy);
            Assert.Equal(25000, response.CombatPower);
            Assert.Equal("属性刷新成功", response.Message);
        }

        #endregion

        #region Cross-Phase Compatibility Tests

        [Fact]
        public void AllPhase8MessageTypes_AreDefinedInEnum()
        {
            Assert.True(Enum.IsDefined(typeof(MessageType), MessageType.CharacterStateSync));
            Assert.True(Enum.IsDefined(typeof(MessageType), MessageType.CraftingGoldSync));
            Assert.True(Enum.IsDefined(typeof(MessageType), MessageType.SkillTargetValidation));
            Assert.True(Enum.IsDefined(typeof(MessageType), MessageType.ScreenshotNotify));
            Assert.True(Enum.IsDefined(typeof(MessageType), MessageType.CharacterAttributeRefresh));
        }

        [Fact]
        public void AllPhase8DTOs_InheritFromMessageUnion()
        {
            Assert.IsAssignableFrom<MessageUnion>(new CharacterStateSyncRequest());
            Assert.IsAssignableFrom<MessageUnion>(new CharacterStateSyncResponse());
            Assert.IsAssignableFrom<MessageUnion>(new CraftingGoldSyncRequest());
            Assert.IsAssignableFrom<MessageUnion>(new CraftingGoldSyncResponse());
            Assert.IsAssignableFrom<MessageUnion>(new SkillTargetValidationRequest());
            Assert.IsAssignableFrom<MessageUnion>(new SkillTargetValidationResponse());
            Assert.IsAssignableFrom<MessageUnion>(new ScreenshotNotifyMessage());
            Assert.IsAssignableFrom<MessageUnion>(new CharacterAttributeRefreshRequest());
            Assert.IsAssignableFrom<MessageUnion>(new CharacterAttributeRefreshResponse());
        }

        [Fact]
        public void Phase8MessageTypes_StartAfterPhase7()
        {
            int phase7Last = (int)MessageType.InventoryExpand; // 1362
            int phase8First = (int)MessageType.CharacterStateSync; // 1363
            Assert.Equal(phase7Last + 1, phase8First);
        }

        [Fact]
        public void AllPhase8DTOs_ImplementINetworkMessage()
        {
            Assert.IsAssignableFrom<INetworkMessage>(new CharacterStateSyncRequest());
            Assert.IsAssignableFrom<INetworkMessage>(new CharacterStateSyncResponse());
            Assert.IsAssignableFrom<INetworkMessage>(new CraftingGoldSyncRequest());
            Assert.IsAssignableFrom<INetworkMessage>(new CraftingGoldSyncResponse());
            Assert.IsAssignableFrom<INetworkMessage>(new SkillTargetValidationRequest());
            Assert.IsAssignableFrom<INetworkMessage>(new SkillTargetValidationResponse());
            Assert.IsAssignableFrom<INetworkMessage>(new ScreenshotNotifyMessage());
            Assert.IsAssignableFrom<INetworkMessage>(new CharacterAttributeRefreshRequest());
            Assert.IsAssignableFrom<INetworkMessage>(new CharacterAttributeRefreshResponse());
        }

        [Fact]
        public void Phase8MessageTypes_ContinuousWithPhase7()
        {
            // 验证Phase 7最后值 + 1 = Phase 8起始值
            Assert.Equal((int)MessageType.InventoryExpand + 1, (int)MessageType.CharacterStateSync);
        }

        #endregion
    }
}
