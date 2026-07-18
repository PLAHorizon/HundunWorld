using Horizon.Game.Core.Sim.Client;
using Horizon.Game.Message.Sync;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// Task 10.8: SyncPacketDispatcher 边界测试。
/// 覆盖 null 包、未知 Kind、ReconnectResume 客户端侧计数、跨队列隔离等边界场景。
/// </summary>
public class SyncPacketDispatcherBoundaryTests
{
    /// <summary>
    /// 测试专用 SyncPacket 子类：模拟未识别的包类型，触发 Dispatch 的 default 分支。
    /// </summary>
    private sealed class UnknownTestPacket : SyncPacket
    {
        public UnknownTestPacket()
        {
            Kind = (SyncPacketKind)99;
        }
    }

    [Fact]
    public void Dispatch_NullPacket_ThrowsArgumentNullException()
    {
        var inbox = new SyncPacketInbox();
        var dispatcher = new SyncPacketDispatcher(inbox);

        Assert.Throws<ArgumentNullException>(() => dispatcher.Dispatch(null!));
    }

    [Fact]
    public void UnknownPacketCount_Increments_OnUnknownKind()
    {
        var inbox = new SyncPacketInbox();
        var dispatcher = new SyncPacketDispatcher(inbox);
        var initialCount = dispatcher.UnknownPacketCount;

        dispatcher.Dispatch(new UnknownTestPacket());

        Assert.Equal(initialCount + 1, dispatcher.UnknownPacketCount);
    }

    [Fact]
    public void ReconnectResume_CountedAsUnknown_OnClient()
    {
        var inbox = new SyncPacketInbox();
        var dispatcher = new SyncPacketDispatcher(inbox);
        var initialCount = dispatcher.UnknownPacketCount;

        // ReconnectResumePacket 是客户端→服务器方向的包，客户端不该收到。
        // Dispatch 应将其计入 UnknownPacketCount。
        dispatcher.Dispatch(new ReconnectResumePacket());

        Assert.Equal(initialCount + 1, dispatcher.UnknownPacketCount);
        // 同时不应入队到任何业务队列
        Assert.Empty(inbox.InteractionEvents);
        Assert.Empty(inbox.ChunkDiffs);
        Assert.Empty(inbox.PatchManifests);
    }

    [Fact]
    public void CrossQueue_Isolation_InteractionSyncDoesNotPolluteChunkDiffs()
    {
        var inbox = new SyncPacketInbox();
        var dispatcher = new SyncPacketDispatcher(inbox);

        dispatcher.Dispatch(new InteractionSyncPacket
        {
            SlotIdx = 1,
            InteractableId = 100L,
            InteractorId = 200L,
            StateBits = InteractionStateBits.Start,
            ServerTick = 999L,
        });

        // InteractionSyncPacket 应仅入队到 InteractionEvents，不污染 ChunkDiffs
        Assert.Single(inbox.InteractionEvents);
        Assert.Empty(inbox.ChunkDiffs);
    }

    [Fact]
    public void CrossQueue_Isolation_InteractionSyncDoesNotPolluteManifests()
    {
        var inbox = new SyncPacketInbox();
        var dispatcher = new SyncPacketDispatcher(inbox);

        dispatcher.Dispatch(new InteractionSyncPacket
        {
            SlotIdx = 2,
            InteractableId = 300L,
            InteractorId = 400L,
            StateBits = InteractionStateBits.End,
            ServerTick = 888L,
        });

        // InteractionSyncPacket 应仅入队到 InteractionEvents，不污染 PatchManifests
        Assert.Single(inbox.InteractionEvents);
        Assert.Empty(inbox.PatchManifests);
    }

    [Fact]
    public void CrossQueue_Isolation_InteractionSyncDoesNotPolluteLatestAck()
    {
        var inbox = new SyncPacketInbox();
        var dispatcher = new SyncPacketDispatcher(inbox);

        dispatcher.Dispatch(new InteractionSyncPacket
        {
            SlotIdx = 0,
            InteractableId = 1L,
            InteractorId = 2L,
            StateBits = InteractionStateBits.Stolen,
            ServerTick = 100L,
        });

        // InteractionSyncPacket 不应更新 LatestAck（仅 InputAckPacket 应更新）
        Assert.Null(inbox.LatestAck);
    }

    [Fact]
    public void UnknownPacket_DoesNotEnqueue_ToAnyQueue()
    {
        var inbox = new SyncPacketInbox();
        var dispatcher = new SyncPacketDispatcher(inbox);

        dispatcher.Dispatch(new UnknownTestPacket());
        dispatcher.Dispatch(new UnknownTestPacket());

        // 未知包应仅递增 UnknownPacketCount，不入队任何业务队列
        Assert.Equal(2, dispatcher.UnknownPacketCount);
        Assert.Empty(inbox.InteractionEvents);
        Assert.Empty(inbox.ChunkDiffs);
        Assert.Empty(inbox.PatchManifests);
        Assert.Null(inbox.LatestAck);
    }
}
