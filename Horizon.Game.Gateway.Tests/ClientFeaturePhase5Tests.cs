using Horizon.Game.Message;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;

namespace Horizon.Game.Gateway.Tests
{
    /// <summary>
    /// 客户端功能集成测试 - 第五阶段
    /// 测试快捷栏操作消息、输入配置同步消息、新增MessageType枚举和DTO
    /// </summary>
    public class ClientFeaturePhase5Tests
    {
        #region MessageType Tests - 新增消息类型

        [Fact]
        public void MessageType_HotbarAction_HasCorrectValue()
        {
            Assert.Equal(1350, (int)MessageType.HotbarAction);
        }

        [Fact]
        public void MessageType_InputConfigSync_HasCorrectValue()
        {
            Assert.Equal(1351, (int)MessageType.InputConfigSync);
        }

        [Fact]
        public void MessageType_NewPhase5Types_AreUnique()
        {
            var values = new[]
            {
                (int)MessageType.HotbarAction,
                (int)MessageType.InputConfigSync
            };

            Assert.Equal(values.Length, values.Distinct().Count());
        }

        [Fact]
        public void MessageType_Phase5Types_DoNotConflictWithPreviousPhases()
        {
            var phase4Max = (int)MessageType.BuffDisplay; // 1349
            Assert.True((int)MessageType.HotbarAction > phase4Max);
            Assert.True((int)MessageType.InputConfigSync > phase4Max);
        }

        [Fact]
        public void MessageType_Phase5Types_AreSequential()
        {
            Assert.Equal((int)MessageType.HotbarAction + 1, (int)MessageType.InputConfigSync);
        }

        #endregion

        #region HotbarActionMessage Tests

        [Fact]
        public void HotbarActionMessage_DefaultValues_AreCorrect()
        {
            var msg = new HotbarActionMessage();
            Assert.Equal(0UL, msg.CharacterId);
            Assert.Equal(HotbarActionType.Use, msg.ActionType);
            Assert.Equal(0, msg.SlotIndex);
            Assert.Equal(0, msg.SkillId);
            Assert.Equal(0, msg.TargetSlotIndex);
            Assert.Equal(MessageType.HotbarAction, msg.Type);
            Assert.Equal(ServiceType.Game, msg.ServiceType);
        }

        [Fact]
        public void HotbarActionMessage_SetProperties_WorkCorrectly()
        {
            var msg = new HotbarActionMessage
            {
                CharacterId = 12345,
                ActionType = HotbarActionType.Assign,
                SlotIndex = 3,
                SkillId = 101,
                TargetSlotIndex = 5
            };

            Assert.Equal(12345UL, msg.CharacterId);
            Assert.Equal(HotbarActionType.Assign, msg.ActionType);
            Assert.Equal(3, msg.SlotIndex);
            Assert.Equal(101, msg.SkillId);
            Assert.Equal(5, msg.TargetSlotIndex);
        }

        [Fact]
        public void HotbarActionMessage_UseAction_WorksCorrectly()
        {
            var msg = new HotbarActionMessage
            {
                CharacterId = 1,
                ActionType = HotbarActionType.Use,
                SlotIndex = 0,
                SkillId = 200
            };

            Assert.Equal(HotbarActionType.Use, msg.ActionType);
            Assert.Equal(0, msg.SlotIndex);
            Assert.Equal(200, msg.SkillId);
        }

        [Fact]
        public void HotbarActionMessage_SwapAction_WorksCorrectly()
        {
            var msg = new HotbarActionMessage
            {
                CharacterId = 1,
                ActionType = HotbarActionType.Swap,
                SlotIndex = 2,
                TargetSlotIndex = 7
            };

            Assert.Equal(HotbarActionType.Swap, msg.ActionType);
            Assert.Equal(2, msg.SlotIndex);
            Assert.Equal(7, msg.TargetSlotIndex);
        }

        [Fact]
        public void HotbarActionMessage_ClearAction_WorksCorrectly()
        {
            var msg = new HotbarActionMessage
            {
                CharacterId = 1,
                ActionType = HotbarActionType.Clear,
                SlotIndex = 5
            };

            Assert.Equal(HotbarActionType.Clear, msg.ActionType);
            Assert.Equal(5, msg.SlotIndex);
        }

        #endregion

        #region HotbarActionType Enum Tests

        [Fact]
        public void HotbarActionType_HasExpectedValues()
        {
            Assert.Equal(0, (int)HotbarActionType.Use);
            Assert.Equal(1, (int)HotbarActionType.Assign);
            Assert.Equal(2, (int)HotbarActionType.Clear);
            Assert.Equal(3, (int)HotbarActionType.Swap);
        }

        [Fact]
        public void HotbarActionType_HasFourValues()
        {
            var values = Enum.GetValues(typeof(HotbarActionType));
            Assert.Equal(4, values.Length);
        }

        #endregion

        #region InputConfigSyncMessage Tests

        [Fact]
        public void InputConfigSyncMessage_DefaultValues_AreCorrect()
        {
            var msg = new InputConfigSyncMessage();
            Assert.Equal(0UL, msg.CharacterId);
            Assert.Equal("", msg.ConfigData);
            Assert.False(msg.IsUpload);
            Assert.Equal(0, msg.ConfigVersion);
            Assert.Equal(MessageType.InputConfigSync, msg.Type);
            Assert.Equal(ServiceType.Game, msg.ServiceType);
        }

        [Fact]
        public void InputConfigSyncMessage_UploadConfig_WorksCorrectly()
        {
            var configJson = "{\"MoveForward\":{\"Keys\":[\"W\"]}}";
            var msg = new InputConfigSyncMessage
            {
                CharacterId = 12345,
                ConfigData = configJson,
                IsUpload = true,
                ConfigVersion = 3
            };

            Assert.Equal(12345UL, msg.CharacterId);
            Assert.Equal(configJson, msg.ConfigData);
            Assert.True(msg.IsUpload);
            Assert.Equal(3, msg.ConfigVersion);
        }

        [Fact]
        public void InputConfigSyncMessage_DownloadConfig_WorksCorrectly()
        {
            var msg = new InputConfigSyncMessage
            {
                CharacterId = 99999,
                ConfigData = "",
                IsUpload = false,
                ConfigVersion = 0
            };

            Assert.Equal(99999UL, msg.CharacterId);
            Assert.False(msg.IsUpload);
            Assert.Equal(0, msg.ConfigVersion);
        }

        [Fact]
        public void InputConfigSyncMessage_EmptyConfigData_IsValid()
        {
            var msg = new InputConfigSyncMessage
            {
                CharacterId = 1,
                ConfigData = ""
            };

            Assert.Equal("", msg.ConfigData);
        }

        [Fact]
        public void InputConfigSyncMessage_LargeConfigData_IsAccepted()
        {
            var largeConfig = new string('x', 10000);
            var msg = new InputConfigSyncMessage
            {
                ConfigData = largeConfig
            };

            Assert.Equal(10000, msg.ConfigData.Length);
        }

        #endregion

        #region Cross-Phase Compatibility Tests

        [Fact]
        public void AllPhase5MessageTypes_DoNotConflictWithAnyExistingType()
        {
            var allValues = Enum.GetValues(typeof(MessageType)).Cast<MessageType>().Select(v => (int)v).ToList();
            var phase5Values = new[] { (int)MessageType.HotbarAction, (int)MessageType.InputConfigSync };

            foreach (var val in phase5Values)
            {
                Assert.Equal(1, allValues.Count(v => v == val));
            }
        }

        [Fact]
        public void Phase5_ContinuesFromPhase4()
        {
            var phase4Max = (int)MessageType.BuffDisplay; // 1349
            var phase5Min = (int)MessageType.HotbarAction; // 1350

            Assert.Equal(phase4Max + 1, phase5Min);
        }

        #endregion

        #region HotbarAction Scenario Tests

        [Fact]
        public void HotbarAction_FullSlotAssignmentWorkflow()
        {
            // 分配10个技能到快捷栏
            var messages = new List<HotbarActionMessage>();
            for (int i = 0; i < 10; i++)
            {
                messages.Add(new HotbarActionMessage
                {
                    CharacterId = 1,
                    ActionType = HotbarActionType.Assign,
                    SlotIndex = i,
                    SkillId = 100 + i
                });
            }

            Assert.Equal(10, messages.Count);
            Assert.All(messages, m => Assert.Equal(HotbarActionType.Assign, m.ActionType));
            for (int i = 0; i < 10; i++)
            {
                Assert.Equal(i, messages[i].SlotIndex);
                Assert.Equal(100 + i, messages[i].SkillId);
            }
        }

        [Fact]
        public void HotbarAction_SwapThenUseWorkflow()
        {
            // 交换槽位0和1
            var swapMsg = new HotbarActionMessage
            {
                CharacterId = 1,
                ActionType = HotbarActionType.Swap,
                SlotIndex = 0,
                TargetSlotIndex = 1
            };
            Assert.Equal(HotbarActionType.Swap, swapMsg.ActionType);

            // 使用槽位0
            var useMsg = new HotbarActionMessage
            {
                CharacterId = 1,
                ActionType = HotbarActionType.Use,
                SlotIndex = 0
            };
            Assert.Equal(HotbarActionType.Use, useMsg.ActionType);
            Assert.Equal(0, useMsg.SlotIndex);
        }

        [Fact]
        public void HotbarAction_ClearAllSlotsWorkflow()
        {
            var clearMessages = Enumerable.Range(0, 10)
                .Select(i => new HotbarActionMessage
                {
                    CharacterId = 1,
                    ActionType = HotbarActionType.Clear,
                    SlotIndex = i
                })
                .ToList();

            Assert.Equal(10, clearMessages.Count);
            Assert.All(clearMessages, m => Assert.Equal(HotbarActionType.Clear, m.ActionType));
        }

        #endregion

        #region Edge Case Tests

        [Fact]
        public void HotbarActionMessage_MaxSlotIndex_IsAccepted()
        {
            var msg = new HotbarActionMessage { SlotIndex = 9 };
            Assert.Equal(9, msg.SlotIndex);
        }

        [Fact]
        public void HotbarActionMessage_ZeroCharacterId_IsAccepted()
        {
            var msg = new HotbarActionMessage { CharacterId = 0 };
            Assert.Equal(0UL, msg.CharacterId);
        }

        [Fact]
        public void InputConfigSyncMessage_VersionIncrement_WorksCorrectly()
        {
            var msg = new InputConfigSyncMessage { ConfigVersion = int.MaxValue };
            Assert.Equal(int.MaxValue, msg.ConfigVersion);
        }

        #endregion
    }
}
