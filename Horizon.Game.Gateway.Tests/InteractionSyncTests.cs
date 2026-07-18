using Horizon.Game.Core.Sim.Client;
using Horizon.Game.Message.Sync;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 交互同步协议层单元测试。
/// 覆盖 InteractionSyncPacket 的 MemoryPack 序列化往返、SyncPacketInbox 队列消费、
/// SyncPacketDispatcher 路由分支。
/// </summary>
public class InteractionSyncTests
{
    #region Task 6.1 - InteractionSyncPacket MemoryPack 序列化往返

    [Fact]
    public void InteractionSyncPacket_MemoryPack_RoundTrip_PreservesAllFields()
    {
        // Arrange
        var original = new InteractionSyncPacket
        {
            SlotIdx = 3,
            InteractableId = 0xABCDEF1234L,
            InteractorId = 0x567890ABCDEFL,
            StateBits = 0x0F,
            ServerTick = 999999L,
        };

        // Act - 序列化
        var bytes = MemoryPack.MemoryPackSerializer.Serialize<InteractionSyncPacket>(original);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        // Act - 反序列化
        var restored = MemoryPack.MemoryPackSerializer.Deserialize<InteractionSyncPacket>(bytes);

        // Assert
        Assert.NotNull(restored);
        Assert.Equal(original.SlotIdx, restored!.SlotIdx);
        Assert.Equal(original.InteractableId, restored.InteractableId);
        Assert.Equal(original.InteractorId, restored.InteractorId);
        Assert.Equal(original.StateBits, restored.StateBits);
        Assert.Equal(original.ServerTick, restored.ServerTick);
    }

    [Fact]
    public void InteractionSyncPacket_MemoryPack_RoundTrip_ZeroValues()
    {
        var original = new InteractionSyncPacket
        {
            SlotIdx = 0,
            InteractableId = 0,
            InteractorId = 0,
            StateBits = 0,
            ServerTick = 0,
        };

        var bytes = MemoryPack.MemoryPackSerializer.Serialize<InteractionSyncPacket>(original);
        var restored = MemoryPack.MemoryPackSerializer.Deserialize<InteractionSyncPacket>(bytes);

        Assert.NotNull(restored);
        Assert.Equal(0, restored!.SlotIdx);
        Assert.Equal(0L, restored.InteractableId);
        Assert.Equal(0L, restored.InteractorId);
        Assert.Equal((byte)0, restored.StateBits);
        Assert.Equal(0L, restored.ServerTick);
    }

    [Fact]
    public void InteractionSyncPacket_MemoryPack_RoundTrip_MaxValues()
    {
        var original = new InteractionSyncPacket
        {
            SlotIdx = int.MaxValue,
            InteractableId = long.MaxValue,
            InteractorId = long.MaxValue,
            StateBits = byte.MaxValue,
            ServerTick = long.MaxValue,
        };

        var bytes = MemoryPack.MemoryPackSerializer.Serialize<InteractionSyncPacket>(original);
        var restored = MemoryPack.MemoryPackSerializer.Deserialize<InteractionSyncPacket>(bytes);

        Assert.NotNull(restored);
        Assert.Equal(int.MaxValue, restored!.SlotIdx);
        Assert.Equal(long.MaxValue, restored.InteractableId);
        Assert.Equal(long.MaxValue, restored.InteractorId);
        Assert.Equal(byte.MaxValue, restored.StateBits);
        Assert.Equal(long.MaxValue, restored.ServerTick);
    }

    [Fact]
    public void InteractionSyncPacket_Kind_IsInteractionSync()
    {
        var packet = new InteractionSyncPacket();
        Assert.Equal(SyncPacketKind.InteractionSync, packet.Kind);
    }

    [Fact]
    public void SyncPacketKind_InteractionSync_HasExpectedValue()
    {
        // 确认枚举值为 9（原最大值 ReconnectResume=8 之后递增）
        Assert.Equal(9, (int)SyncPacketKind.InteractionSync);
    }

    [Fact]
    public void SyncEventKind_InteractionEvents_HaveExpectedValues()
    {
        // 确认交互事件类型存在且值正确（原最大值 Pickup=6 之后递增）
        Assert.Equal(7, (int)SyncEventKind.InteractStart);
        Assert.Equal(8, (int)SyncEventKind.InteractEnd);
        Assert.Equal(9, (int)SyncEventKind.InteractStolen);
    }

    [Fact]
    public void InteractionSyncPacket_Polymorphic_RoundTrip_AsSyncPacket()
    {
        // 验证作为基类 SyncPacket 的多态序列化往返
        var original = new InteractionSyncPacket
        {
            SlotIdx = 5,
            InteractableId = 42L,
            InteractorId = 100L,
            StateBits = 0x80, // 请求开始交互标志
            ServerTick = 12345L,
        };

        // 作为基类序列化（多态）
        var bytes = MemoryPack.MemoryPackSerializer.Serialize<SyncPacket>(original);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        // 作为基类反序列化
        var restored = MemoryPack.MemoryPackSerializer.Deserialize<SyncPacket>(bytes);

        // Assert
        Assert.NotNull(restored);
        Assert.IsType<InteractionSyncPacket>(restored);
        var typed = (InteractionSyncPacket)restored!;
        Assert.Equal(original.SlotIdx, typed.SlotIdx);
        Assert.Equal(original.InteractableId, typed.InteractableId);
        Assert.Equal(original.InteractorId, typed.InteractorId);
        Assert.Equal(original.StateBits, typed.StateBits);
        Assert.Equal(original.ServerTick, typed.ServerTick);
    }

    #endregion

    #region Task 6.2 - SyncPacketInbox 队列消费与 SyncPacketDispatcher 路由

    [Fact]
    public void SyncPacketInbox_InteractionEvents_EnqueueDequeue()
    {
        // Arrange
        var inbox = new SyncPacketInbox();
        var packet = new InteractionSyncPacket
        {
            SlotIdx = 2,
            InteractableId = 10L,
            InteractorId = 20L,
            StateBits = 0x01,
            ServerTick = 100L,
        };

        // Act - 入队
        inbox.InteractionEvents.Enqueue(packet);

        // Assert - 出队
        Assert.True(inbox.InteractionEvents.TryDequeue(out var restored));
        Assert.NotNull(restored);
        Assert.Equal(packet.SlotIdx, restored!.SlotIdx);
        Assert.Equal(packet.InteractableId, restored.InteractableId);
        Assert.Equal(packet.InteractorId, restored.InteractorId);
        Assert.Equal(packet.StateBits, restored.StateBits);
        Assert.Equal(packet.ServerTick, restored.ServerTick);
    }

    [Fact]
    public void SyncPacketInbox_InteractionEvents_EmptyQueue_ReturnsFalse()
    {
        var inbox = new SyncPacketInbox();
        Assert.False(inbox.InteractionEvents.TryDequeue(out _));
    }

    [Fact]
    public void SyncPacketInbox_InteractionEvents_FifoOrder()
    {
        var inbox = new SyncPacketInbox();
        var p1 = new InteractionSyncPacket { SlotIdx = 1, InteractableId = 100L };
        var p2 = new InteractionSyncPacket { SlotIdx = 2, InteractableId = 200L };
        var p3 = new InteractionSyncPacket { SlotIdx = 3, InteractableId = 300L };

        inbox.InteractionEvents.Enqueue(p1);
        inbox.InteractionEvents.Enqueue(p2);
        inbox.InteractionEvents.Enqueue(p3);

        Assert.True(inbox.InteractionEvents.TryDequeue(out var r1));
        Assert.Equal(1, r1!.SlotIdx);
        Assert.True(inbox.InteractionEvents.TryDequeue(out var r2));
        Assert.Equal(2, r2!.SlotIdx);
        Assert.True(inbox.InteractionEvents.TryDequeue(out var r3));
        Assert.Equal(3, r3!.SlotIdx);
    }

    [Fact]
    public void SyncPacketDispatcher_Routes_InteractionSyncPacket_ToInbox()
    {
        // Arrange
        var inbox = new SyncPacketInbox();
        var dispatcher = new SyncPacketDispatcher(inbox);
        var packet = new InteractionSyncPacket
        {
            SlotIdx = 7,
            InteractableId = 999L,
            InteractorId = 888L,
            StateBits = InteractionStateBits.End,
            ServerTick = 555L,
        };

        // Act - 分派
        dispatcher.Dispatch(packet);

        // Assert - 应路由到 InteractionEvents 队列
        Assert.True(inbox.InteractionEvents.TryDequeue(out var restored));
        Assert.NotNull(restored);
        Assert.Equal(packet.SlotIdx, restored!.SlotIdx);
        Assert.Equal(packet.InteractableId, restored.InteractableId);
        Assert.Equal(packet.InteractorId, restored.InteractorId);
        Assert.Equal(packet.StateBits, restored.StateBits);
        Assert.Equal(packet.ServerTick, restored.ServerTick);
    }

    [Fact]
    public void SyncPacketDispatcher_InteractionSyncCount_Increments()
    {
        var inbox = new SyncPacketInbox();
        var dispatcher = new SyncPacketDispatcher(inbox);
        var initialCount = dispatcher.InteractionSyncCount;

        dispatcher.Dispatch(new InteractionSyncPacket { SlotIdx = 1, InteractableId = 1L });
        dispatcher.Dispatch(new InteractionSyncPacket { SlotIdx = 2, InteractableId = 2L });
        dispatcher.Dispatch(new InteractionSyncPacket { SlotIdx = 3, InteractableId = 3L });

        Assert.Equal(initialCount + 3, dispatcher.InteractionSyncCount);
    }

    #endregion
}
