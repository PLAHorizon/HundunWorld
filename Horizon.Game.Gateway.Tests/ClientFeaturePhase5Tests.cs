using Horizon.Game.Message;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;

namespace Horizon.Game.Gateway.Tests
{
    /// <summary>
    /// 客户端功能集成测试 - 第五阶段
    /// 测试背包拖拽消息、输入配置同步消息、新增MessageType枚举和DTO
    /// </summary>
    public class ClientFeaturePhase5Tests
    {
        #region MessageType Tests - 新增消息类型

        [Fact]
        public void MessageType_InventoryDragDrop_HasCorrectValue()
        {
            Assert.Equal(1350, (int)MessageType.InventoryDragDrop);
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
                (int)MessageType.InventoryDragDrop,
                (int)MessageType.InputConfigSync
            };

            Assert.Equal(values.Length, values.Distinct().Count());
        }

        [Fact]
        public void MessageType_Phase5Types_DoNotConflictWithPreviousPhases()
        {
            var phase4Max = (int)MessageType.BuffDisplay; // 1349
            Assert.True((int)MessageType.InventoryDragDrop > phase4Max);
            Assert.True((int)MessageType.InputConfigSync > phase4Max);
        }

        [Fact]
        public void MessageType_Phase5Types_AreSequential()
        {
            Assert.Equal((int)MessageType.InventoryDragDrop + 1, (int)MessageType.InputConfigSync);
        }

        [Fact]
        public void MessageType_Phase5_FollowsPhase4Sequence()
        {
            // 确保Phase 5消息类型紧跟Phase 4
            Assert.Equal((int)MessageType.BuffDisplay + 1, (int)MessageType.InventoryDragDrop);
            Assert.Equal((int)MessageType.BuffDisplay + 2, (int)MessageType.InputConfigSync);
        }

        #endregion

        #region InventoryDragDropMessage Tests

        [Fact]
        public void InventoryDragDropMessage_DefaultValues_AreCorrect()
        {
            var msg = new InventoryDragDropMessage();
            Assert.Equal(0UL, msg.CharacterId);
            Assert.Equal(0, msg.SourceSlotIndex);
            Assert.Equal(0, msg.TargetSlotIndex);
            Assert.Equal(DragDropOperation.Swap, msg.Operation);
            Assert.Equal(0, msg.SplitCount);
            Assert.Equal(MessageType.InventoryDragDrop, msg.Type);
            Assert.Equal(ServiceType.Game, msg.ServiceType);
        }

        [Fact]
        public void InventoryDragDropMessage_SetProperties_WorkCorrectly()
        {
            var msg = new InventoryDragDropMessage
            {
                CharacterId = 12345,
                SourceSlotIndex = 3,
                TargetSlotIndex = 7,
                Operation = DragDropOperation.Move,
                SplitCount = 10
            };

            Assert.Equal(12345UL, msg.CharacterId);
            Assert.Equal(3, msg.SourceSlotIndex);
            Assert.Equal(7, msg.TargetSlotIndex);
            Assert.Equal(DragDropOperation.Move, msg.Operation);
            Assert.Equal(10, msg.SplitCount);
        }

        [Fact]
        public void InventoryDragDropMessage_ImplementsINetworkMessage()
        {
            var msg = new InventoryDragDropMessage();
            Assert.IsAssignableFrom<INetworkMessage>(msg);
        }

        [Fact]
        public void InventoryDragDropMessage_SwapOperation_ScenarioTest()
        {
            // 模拟交换两个槽位的物品
            var msg = new InventoryDragDropMessage
            {
                CharacterId = 10001,
                SourceSlotIndex = 0,
                TargetSlotIndex = 5,
                Operation = DragDropOperation.Swap
            };

            Assert.Equal(DragDropOperation.Swap, msg.Operation);
            Assert.Equal(0, msg.SplitCount);
        }

        [Fact]
        public void InventoryDragDropMessage_MoveOperation_ScenarioTest()
        {
            // 模拟移动物品到空槽位
            var msg = new InventoryDragDropMessage
            {
                CharacterId = 10002,
                SourceSlotIndex = 2,
                TargetSlotIndex = 10,
                Operation = DragDropOperation.Move
            };

            Assert.Equal(DragDropOperation.Move, msg.Operation);
        }

        [Fact]
        public void InventoryDragDropMessage_SplitOperation_ScenarioTest()
        {
            // 模拟拆分物品
            var msg = new InventoryDragDropMessage
            {
                CharacterId = 10003,
                SourceSlotIndex = 1,
                TargetSlotIndex = 8,
                Operation = DragDropOperation.Split,
                SplitCount = 25
            };

            Assert.Equal(DragDropOperation.Split, msg.Operation);
            Assert.Equal(25, msg.SplitCount);
        }

        [Fact]
        public void InventoryDragDropMessage_MergeOperation_ScenarioTest()
        {
            // 模拟合并相同物品
            var msg = new InventoryDragDropMessage
            {
                CharacterId = 10004,
                SourceSlotIndex = 3,
                TargetSlotIndex = 6,
                Operation = DragDropOperation.Merge
            };

            Assert.Equal(DragDropOperation.Merge, msg.Operation);
        }

        #endregion

        #region DragDropOperation Tests

        [Fact]
        public void DragDropOperation_AllValues_AreDefined()
        {
            Assert.Equal(0, (int)DragDropOperation.Swap);
            Assert.Equal(1, (int)DragDropOperation.Move);
            Assert.Equal(2, (int)DragDropOperation.Split);
            Assert.Equal(3, (int)DragDropOperation.Merge);
        }

        [Fact]
        public void DragDropOperation_HasFourValues()
        {
            var values = Enum.GetValues(typeof(DragDropOperation));
            Assert.Equal(4, values.Length);
        }

        [Fact]
        public void DragDropOperation_AllValues_AreUnique()
        {
            var values = Enum.GetValues(typeof(DragDropOperation)).Cast<int>().ToArray();
            Assert.Equal(values.Length, values.Distinct().Count());
        }

        #endregion

        #region InputConfigSyncMessage Tests

        [Fact]
        public void InputConfigSyncMessage_DefaultValues_AreCorrect()
        {
            var msg = new InputConfigSyncMessage();
            Assert.Equal(0UL, msg.CharacterId);
            Assert.NotNull(msg.SkillBindings);
            Assert.Empty(msg.SkillBindings);
            Assert.Equal(1.0f, msg.MouseSensitivity);
            Assert.False(msg.AutoAttackEnabled);
            Assert.Equal(10.0f, msg.CameraDistance);
            Assert.Equal(MessageType.InputConfigSync, msg.Type);
            Assert.Equal(ServiceType.Game, msg.ServiceType);
        }

        [Fact]
        public void InputConfigSyncMessage_SetProperties_WorkCorrectly()
        {
            var msg = new InputConfigSyncMessage
            {
                CharacterId = 99999,
                MouseSensitivity = 1.5f,
                AutoAttackEnabled = true,
                CameraDistance = 15.0f,
                SkillBindings = new List<SkillSlotBinding>
                {
                    new SkillSlotBinding { SlotIndex = 0, SkillId = 1001, KeyName = "Q" },
                    new SkillSlotBinding { SlotIndex = 1, SkillId = 1002, KeyName = "W" }
                }
            };

            Assert.Equal(99999UL, msg.CharacterId);
            Assert.Equal(1.5f, msg.MouseSensitivity);
            Assert.True(msg.AutoAttackEnabled);
            Assert.Equal(15.0f, msg.CameraDistance);
            Assert.Equal(2, msg.SkillBindings.Count);
        }

        [Fact]
        public void InputConfigSyncMessage_ImplementsINetworkMessage()
        {
            var msg = new InputConfigSyncMessage();
            Assert.IsAssignableFrom<INetworkMessage>(msg);
        }

        [Fact]
        public void InputConfigSyncMessage_MultipleBindings_ScenarioTest()
        {
            // 模拟完整技能栏配置同步
            var msg = new InputConfigSyncMessage
            {
                CharacterId = 20001,
                MouseSensitivity = 0.8f,
                AutoAttackEnabled = false,
                CameraDistance = 12.0f,
                SkillBindings = new List<SkillSlotBinding>
                {
                    new SkillSlotBinding { SlotIndex = 0, SkillId = 101, KeyName = "1" },
                    new SkillSlotBinding { SlotIndex = 1, SkillId = 102, KeyName = "2" },
                    new SkillSlotBinding { SlotIndex = 2, SkillId = 103, KeyName = "3" },
                    new SkillSlotBinding { SlotIndex = 3, SkillId = 104, KeyName = "4" },
                    new SkillSlotBinding { SlotIndex = 4, SkillId = 201, KeyName = "Q" },
                    new SkillSlotBinding { SlotIndex = 5, SkillId = 202, KeyName = "E" },
                    new SkillSlotBinding { SlotIndex = 6, SkillId = 301, KeyName = "R" },
                    new SkillSlotBinding { SlotIndex = 7, SkillId = 302, KeyName = "F" }
                }
            };

            Assert.Equal(8, msg.SkillBindings.Count);
            Assert.Equal("1", msg.SkillBindings[0].KeyName);
            Assert.Equal(101, msg.SkillBindings[0].SkillId);
            Assert.Equal("F", msg.SkillBindings[7].KeyName);
            Assert.Equal(302, msg.SkillBindings[7].SkillId);
        }

        [Fact]
        public void InputConfigSyncMessage_EmptyBindings_IsValid()
        {
            var msg = new InputConfigSyncMessage
            {
                CharacterId = 30001,
                SkillBindings = new List<SkillSlotBinding>()
            };

            Assert.Empty(msg.SkillBindings);
        }

        #endregion

        #region SkillSlotBinding Tests

        [Fact]
        public void SkillSlotBinding_DefaultValues_AreCorrect()
        {
            var binding = new SkillSlotBinding();
            Assert.Equal(0, binding.SlotIndex);
            Assert.Equal(0, binding.SkillId);
            Assert.Equal("", binding.KeyName);
        }

        [Fact]
        public void SkillSlotBinding_SetProperties_WorkCorrectly()
        {
            var binding = new SkillSlotBinding
            {
                SlotIndex = 3,
                SkillId = 2001,
                KeyName = "E"
            };

            Assert.Equal(3, binding.SlotIndex);
            Assert.Equal(2001, binding.SkillId);
            Assert.Equal("E", binding.KeyName);
        }

        [Fact]
        public void SkillSlotBinding_UniqueSlotIndices_InCollection()
        {
            var bindings = new List<SkillSlotBinding>
            {
                new SkillSlotBinding { SlotIndex = 0, SkillId = 1, KeyName = "1" },
                new SkillSlotBinding { SlotIndex = 1, SkillId = 2, KeyName = "2" },
                new SkillSlotBinding { SlotIndex = 2, SkillId = 3, KeyName = "3" },
                new SkillSlotBinding { SlotIndex = 3, SkillId = 4, KeyName = "4" }
            };

            var indices = bindings.Select(b => b.SlotIndex).ToArray();
            Assert.Equal(indices.Length, indices.Distinct().Count());
        }

        #endregion

        #region Integration Tests

        [Fact]
        public void MessageType_AllPhase5Types_HaveGameServiceType()
        {
            var dragMsg = new InventoryDragDropMessage();
            var configMsg = new InputConfigSyncMessage();

            Assert.Equal(ServiceType.Game, dragMsg.ServiceType);
            Assert.Equal(ServiceType.Game, configMsg.ServiceType);
        }

        [Theory]
        [InlineData(DragDropOperation.Swap)]
        [InlineData(DragDropOperation.Move)]
        [InlineData(DragDropOperation.Split)]
        [InlineData(DragDropOperation.Merge)]
        public void InventoryDragDropMessage_CanSetAllOperations(DragDropOperation operation)
        {
            var msg = new InventoryDragDropMessage { Operation = operation };
            Assert.Equal(operation, msg.Operation);
        }

        [Fact]
        public void InventoryDragDropMessage_LargeCharacterId_WorksCorrectly()
        {
            var msg = new InventoryDragDropMessage { CharacterId = ulong.MaxValue };
            Assert.Equal(ulong.MaxValue, msg.CharacterId);
        }

        [Fact]
        public void InputConfigSyncMessage_SensitivityRange_AcceptsValid()
        {
            var msg = new InputConfigSyncMessage { MouseSensitivity = 0.1f };
            Assert.Equal(0.1f, msg.MouseSensitivity);

            msg.MouseSensitivity = 3.0f;
            Assert.Equal(3.0f, msg.MouseSensitivity);
        }

        [Fact]
        public void InputConfigSyncMessage_CameraDistanceRange_AcceptsValid()
        {
            var msg = new InputConfigSyncMessage { CameraDistance = 5.0f };
            Assert.Equal(5.0f, msg.CameraDistance);

            msg.CameraDistance = 30.0f;
            Assert.Equal(30.0f, msg.CameraDistance);
        }

        [Fact]
        public void InventoryDragDropMessage_NegativeSlotIndex_AcceptsValue()
        {
            // 负数槽位索引应该在业务逻辑层验证，DTO应接受任意值
            var msg = new InventoryDragDropMessage { SourceSlotIndex = -1 };
            Assert.Equal(-1, msg.SourceSlotIndex);
        }

        [Fact]
        public void InventoryDragDropMessage_SameSourceAndTarget_AcceptsValue()
        {
            // DTO层不做业务验证
            var msg = new InventoryDragDropMessage
            {
                SourceSlotIndex = 5,
                TargetSlotIndex = 5
            };

            Assert.Equal(msg.SourceSlotIndex, msg.TargetSlotIndex);
        }

        #endregion

        #region Cross-Phase Integration Tests

        [Fact]
        public void MessageType_AllPhasesContiguous()
        {
            // 验证所有阶段的消息类型是连续的
            var allTypes = new[]
            {
                (int)MessageType.EquipmentComparison,  // 1343 Phase 3
                (int)MessageType.GuildManagement,       // 1344
                (int)MessageType.TeamInvite,            // 1345
                (int)MessageType.KillCam,               // 1346
                (int)MessageType.HotkeyConfig,          // 1347
                (int)MessageType.AudioPlayback,         // 1348 Phase 4
                (int)MessageType.BuffDisplay,           // 1349
                (int)MessageType.InventoryDragDrop,     // 1350 Phase 5
                (int)MessageType.InputConfigSync        // 1351
            };

            for (int i = 1; i < allTypes.Length; i++)
            {
                Assert.Equal(allTypes[i - 1] + 1, allTypes[i]);
            }
        }

        [Fact]
        public void AllPhase5Messages_ImplementINetworkMessage()
        {
            var messages = new INetworkMessage[]
            {
                new InventoryDragDropMessage(),
                new InputConfigSyncMessage()
            };

            foreach (var msg in messages)
            {
                Assert.IsAssignableFrom<INetworkMessage>(msg);
                Assert.Equal(ServiceType.Game, msg.ServiceType);
            }
        }

        #endregion
    }
}
