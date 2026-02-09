using Horizon.Game.Message;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;

namespace Horizon.Game.Gateway.Tests
{
    /// <summary>
    /// 客户端功能集成测试 - 第七阶段
    /// 测试背包管理消息类型和DTO：排序、拆分、丢弃、锁定、扩容
    /// </summary>
    public class ClientFeaturePhase7Tests
    {
        #region MessageType Tests - 新增消息类型

        [Fact]
        public void MessageType_InventorySort_HasCorrectValue()
        {
            Assert.Equal(1358, (int)MessageType.InventorySort);
        }

        [Fact]
        public void MessageType_ItemSplit_HasCorrectValue()
        {
            Assert.Equal(1359, (int)MessageType.ItemSplit);
        }

        [Fact]
        public void MessageType_ItemDiscard_HasCorrectValue()
        {
            Assert.Equal(1360, (int)MessageType.ItemDiscard);
        }

        [Fact]
        public void MessageType_ItemLock_HasCorrectValue()
        {
            Assert.Equal(1361, (int)MessageType.ItemLock);
        }

        [Fact]
        public void MessageType_InventoryExpand_HasCorrectValue()
        {
            Assert.Equal(1362, (int)MessageType.InventoryExpand);
        }

        [Fact]
        public void MessageType_Phase7Types_AreUnique()
        {
            var values = new[]
            {
                (int)MessageType.InventorySort,
                (int)MessageType.ItemSplit,
                (int)MessageType.ItemDiscard,
                (int)MessageType.ItemLock,
                (int)MessageType.InventoryExpand
            };

            Assert.Equal(values.Length, values.Distinct().Count());
        }

        [Fact]
        public void MessageType_Phase7Types_DoNotConflictWithPreviousPhases()
        {
            var phase6Max = (int)MessageType.MessageCompressionConfig; // 1357
            Assert.True((int)MessageType.InventorySort > phase6Max);
            Assert.True((int)MessageType.ItemSplit > phase6Max);
            Assert.True((int)MessageType.ItemDiscard > phase6Max);
            Assert.True((int)MessageType.ItemLock > phase6Max);
            Assert.True((int)MessageType.InventoryExpand > phase6Max);
        }

        [Fact]
        public void MessageType_Phase7Types_AreSequential()
        {
            Assert.Equal((int)MessageType.InventorySort + 1, (int)MessageType.ItemSplit);
            Assert.Equal((int)MessageType.ItemSplit + 1, (int)MessageType.ItemDiscard);
            Assert.Equal((int)MessageType.ItemDiscard + 1, (int)MessageType.ItemLock);
            Assert.Equal((int)MessageType.ItemLock + 1, (int)MessageType.InventoryExpand);
        }

        #endregion

        #region InventorySortRequest Tests

        [Fact]
        public void InventorySortRequest_DefaultValues_AreCorrect()
        {
            var request = new InventorySortRequest();
            Assert.Equal(MessageType.InventorySort, request.Type);
            Assert.Equal(ServiceType.Game, request.ServiceType);
            Assert.Equal(0UL, request.CharacterId);
            Assert.Equal(0, request.SortMode);
            Assert.Equal(0, request.SortDirection);
        }

        [Fact]
        public void InventorySortRequest_SetValues_RetainCorrectly()
        {
            var request = new InventorySortRequest
            {
                CharacterId = 12345UL,
                SortMode = 1,
                SortDirection = 1
            };

            Assert.Equal(12345UL, request.CharacterId);
            Assert.Equal(1, request.SortMode);
            Assert.Equal(1, request.SortDirection);
        }

        [Fact]
        public void InventorySortRequest_ImplementsINetworkMessage()
        {
            var request = new InventorySortRequest();
            Assert.IsAssignableFrom<INetworkMessage>(request);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void InventorySortRequest_SortModes_AreAccepted(int sortMode)
        {
            var request = new InventorySortRequest { SortMode = sortMode };
            Assert.Equal(sortMode, request.SortMode);
        }

        #endregion

        #region ItemSplitRequest / Response Tests

        [Fact]
        public void ItemSplitRequest_DefaultValues_AreCorrect()
        {
            var request = new ItemSplitRequest();
            Assert.Equal(MessageType.ItemSplit, request.Type);
            Assert.Equal(ServiceType.Game, request.ServiceType);
            Assert.Equal(-1, request.TargetSlot);
        }

        [Fact]
        public void ItemSplitRequest_SetValues_RetainCorrectly()
        {
            var request = new ItemSplitRequest
            {
                CharacterId = 99UL,
                SourceSlot = 5,
                SplitCount = 10,
                TargetSlot = 15
            };

            Assert.Equal(99UL, request.CharacterId);
            Assert.Equal(5, request.SourceSlot);
            Assert.Equal(10, request.SplitCount);
            Assert.Equal(15, request.TargetSlot);
        }

        [Fact]
        public void ItemSplitResponse_DefaultValues_AreCorrect()
        {
            var response = new ItemSplitResponse();
            Assert.Equal(MessageType.ItemSplit, response.Type);
            Assert.Equal(ServiceType.Game, response.ServiceType);
            Assert.False(response.Success);
            Assert.Equal("", response.Message);
        }

        [Fact]
        public void ItemSplitResponse_SetValues_RetainCorrectly()
        {
            var response = new ItemSplitResponse
            {
                Success = true,
                Message = "拆分成功",
                SourceRemainingCount = 5,
                NewSlotCount = 10,
                NewSlotIndex = 20
            };

            Assert.True(response.Success);
            Assert.Equal("拆分成功", response.Message);
            Assert.Equal(5, response.SourceRemainingCount);
            Assert.Equal(10, response.NewSlotCount);
            Assert.Equal(20, response.NewSlotIndex);
        }

        #endregion

        #region ItemDiscardRequest / Response Tests

        [Fact]
        public void ItemDiscardRequest_DefaultValues_AreCorrect()
        {
            var request = new ItemDiscardRequest();
            Assert.Equal(MessageType.ItemDiscard, request.Type);
            Assert.Equal(ServiceType.Game, request.ServiceType);
            Assert.Equal(-1, request.DiscardCount);
        }

        [Fact]
        public void ItemDiscardRequest_SetValues_RetainCorrectly()
        {
            var request = new ItemDiscardRequest
            {
                CharacterId = 42UL,
                SlotIndex = 3,
                DiscardCount = 5
            };

            Assert.Equal(42UL, request.CharacterId);
            Assert.Equal(3, request.SlotIndex);
            Assert.Equal(5, request.DiscardCount);
        }

        [Fact]
        public void ItemDiscardResponse_DefaultValues_AreCorrect()
        {
            var response = new ItemDiscardResponse();
            Assert.Equal(MessageType.ItemDiscard, response.Type);
            Assert.Equal(ServiceType.Game, response.ServiceType);
            Assert.False(response.Success);
            Assert.Equal("", response.Message);
            Assert.NotNull(response.DiscardedItem);
        }

        [Fact]
        public void ItemDiscardResponse_SuccessWithItem_RetainCorrectly()
        {
            var response = new ItemDiscardResponse
            {
                Success = true,
                Message = "丢弃成功",
                DiscardedItem = new ItemInfo
                {
                    ItemId = 100,
                    Name = "铁矿石",
                    Count = 5
                }
            };

            Assert.True(response.Success);
            Assert.Equal("铁矿石", response.DiscardedItem.Name);
            Assert.Equal(5, response.DiscardedItem.Count);
        }

        #endregion

        #region ItemLockRequest Tests

        [Fact]
        public void ItemLockRequest_DefaultValues_AreCorrect()
        {
            var request = new ItemLockRequest();
            Assert.Equal(MessageType.ItemLock, request.Type);
            Assert.Equal(ServiceType.Game, request.ServiceType);
            Assert.False(request.IsLocked);
        }

        [Fact]
        public void ItemLockRequest_LockItem_RetainCorrectly()
        {
            var request = new ItemLockRequest
            {
                CharacterId = 77UL,
                SlotIndex = 10,
                IsLocked = true
            };

            Assert.Equal(77UL, request.CharacterId);
            Assert.Equal(10, request.SlotIndex);
            Assert.True(request.IsLocked);
        }

        [Fact]
        public void ItemLockRequest_UnlockItem_RetainCorrectly()
        {
            var request = new ItemLockRequest
            {
                CharacterId = 77UL,
                SlotIndex = 10,
                IsLocked = false
            };

            Assert.False(request.IsLocked);
        }

        #endregion

        #region InventoryExpandRequest / Response Tests

        [Fact]
        public void InventoryExpandRequest_DefaultValues_AreCorrect()
        {
            var request = new InventoryExpandRequest();
            Assert.Equal(MessageType.InventoryExpand, request.Type);
            Assert.Equal(ServiceType.Game, request.ServiceType);
            Assert.Equal(0UL, request.CharacterId);
        }

        [Fact]
        public void InventoryExpandRequest_SetValues_RetainCorrectly()
        {
            var request = new InventoryExpandRequest
            {
                CharacterId = 555UL,
                ExpandCount = 10,
                ExpandMethod = 1
            };

            Assert.Equal(555UL, request.CharacterId);
            Assert.Equal(10, request.ExpandCount);
            Assert.Equal(1, request.ExpandMethod);
        }

        [Fact]
        public void InventoryExpandResponse_DefaultValues_AreCorrect()
        {
            var response = new InventoryExpandResponse();
            Assert.Equal(MessageType.InventoryExpand, response.Type);
            Assert.Equal(ServiceType.Game, response.ServiceType);
            Assert.False(response.Success);
            Assert.Equal("", response.Message);
        }

        [Fact]
        public void InventoryExpandResponse_SetValues_RetainCorrectly()
        {
            var response = new InventoryExpandResponse
            {
                Success = true,
                Message = "扩容成功",
                NewCapacity = 80,
                ConsumedGold = 5000
            };

            Assert.True(response.Success);
            Assert.Equal("扩容成功", response.Message);
            Assert.Equal(80, response.NewCapacity);
            Assert.Equal(5000, response.ConsumedGold);
        }

        #endregion

        #region Cross-Phase Compatibility Tests

        [Fact]
        public void AllPhase7MessageTypes_AreDefinedInEnum()
        {
            Assert.True(Enum.IsDefined(typeof(MessageType), MessageType.InventorySort));
            Assert.True(Enum.IsDefined(typeof(MessageType), MessageType.ItemSplit));
            Assert.True(Enum.IsDefined(typeof(MessageType), MessageType.ItemDiscard));
            Assert.True(Enum.IsDefined(typeof(MessageType), MessageType.ItemLock));
            Assert.True(Enum.IsDefined(typeof(MessageType), MessageType.InventoryExpand));
        }

        [Fact]
        public void AllPhase7DTOs_InheritFromMessageUnion()
        {
            Assert.IsAssignableFrom<MessageUnion>(new InventorySortRequest());
            Assert.IsAssignableFrom<MessageUnion>(new ItemSplitRequest());
            Assert.IsAssignableFrom<MessageUnion>(new ItemSplitResponse());
            Assert.IsAssignableFrom<MessageUnion>(new ItemDiscardRequest());
            Assert.IsAssignableFrom<MessageUnion>(new ItemDiscardResponse());
            Assert.IsAssignableFrom<MessageUnion>(new ItemLockRequest());
            Assert.IsAssignableFrom<MessageUnion>(new InventoryExpandRequest());
            Assert.IsAssignableFrom<MessageUnion>(new InventoryExpandResponse());
        }

        [Fact]
        public void Phase7MessageTypes_StartAfterPhase6()
        {
            int phase6Last = (int)MessageType.MessageCompressionConfig; // 1357
            int phase7First = (int)MessageType.InventorySort; // 1358
            Assert.Equal(phase6Last + 1, phase7First);
        }

        #endregion
    }
}
