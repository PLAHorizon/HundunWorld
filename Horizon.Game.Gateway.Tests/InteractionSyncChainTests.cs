using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Horizon.Game.Core.Sim.Client;
using Horizon.Game.Message.Sync;
using Moq;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 阶段 10.3 — C# 闭环集成测试。
/// 链路：SyncPacketDispatcher → SyncPacketInbox → InteractionApplySystem（模拟） → mock IInteractionNotifySink。
/// </summary>
/// <remarks>
/// 设计说明：
/// 真实的 InteractionApplySystem 与 IInteractionNotifySink 定义在 UE5 托管脚本
/// （HundunWorld/Script/ManagedHundunWorld/）中，未被任何 csproj 引用，测试项目无法直接访问。
/// 因此本文件使用一个测试内部的 FakeInteractionApplyConsumer 忠实复刻
/// InteractionApplySystem 的核心行为（dequeue → 更新组件 → 回调 sink → 终态销毁），
/// 以验证 SyncPacketDispatcher → SyncPacketInbox → 消费者 → Sink 的完整闭环。
/// </remarks>
public class InteractionSyncChainTests
{
    [Fact]
    public void FullChain_Downlink_PacketRoutedToInboxToSystemToSink()
    {
        var inbox = new SyncPacketInbox();
        var dispatcher = new SyncPacketDispatcher(inbox);
        var mockSink = new Mock<IInteractionNotifySinkStub>();
        var consumer = new FakeInteractionApplyConsumer(inbox, mockSink.Object);

        var packet = new InteractionSyncPacket
        {
            SlotIdx = 3,
            InteractableId = 1001L,
            InteractorId = 2002L,
            StateBits = InteractionStateBits.Start,
            ServerTick = 42L,
        };

        dispatcher.Dispatch(packet);
        consumer.Update();

        mockSink.Verify(
            s => s.NotifyInteractionStateChanged(
                packet.SlotIdx,
                packet.StateBits,
                packet.InteractableId,
                packet.InteractorId),
            Times.Once);

        Assert.Equal(0, inbox.Snapshot().PendingInteractionEventCount);
        Assert.Equal(1, dispatcher.InteractionSyncCount);
        Assert.Equal(1, consumer.TotalSyncPacketsProcessed);
    }

    [Fact]
    public void FullChain_MultiplePackets_ProcessedInOrder()
    {
        var calls = new List<(int slotIdx, long interactableId)>();
        var inbox = new SyncPacketInbox();
        var dispatcher = new SyncPacketDispatcher(inbox);
        var mockSink = new Mock<IInteractionNotifySinkStub>();

        mockSink.Setup(s => s.NotifyInteractionStateChanged(It.IsAny<int>(), It.IsAny<byte>(), It.IsAny<long>(), It.IsAny<long>()))
            .Callback<int, byte, long, long>((slot, _, interactable, _) =>
                calls.Add((slot, interactable)));

        var consumer = new FakeInteractionApplyConsumer(inbox, mockSink.Object);

        var packets = new[]
        {
            new InteractionSyncPacket { SlotIdx = 1, InteractableId = 10L, InteractorId = 100L, StateBits = InteractionStateBits.Start, ServerTick = 1L },
            new InteractionSyncPacket { SlotIdx = 2, InteractableId = 20L, InteractorId = 200L, StateBits = InteractionStateBits.Start, ServerTick = 2L },
            new InteractionSyncPacket { SlotIdx = 3, InteractableId = 30L, InteractorId = 300L, StateBits = InteractionStateBits.Start, ServerTick = 3L },
        };

        foreach (var p in packets)
        {
            dispatcher.Dispatch(p);
        }
        consumer.Update();

        Assert.Equal(3, calls.Count);
        Assert.Equal((1, 10L), calls[0]);
        Assert.Equal((2, 20L), calls[1]);
        Assert.Equal((3, 30L), calls[2]);
        Assert.Equal(3, dispatcher.InteractionSyncCount);
        Assert.Equal(3, consumer.TotalSyncPacketsProcessed);
    }

    [Fact]
    public void FullChain_EntityLifecycle_SpawnUpdateDespawn()
    {
        var inbox = new SyncPacketInbox();
        var dispatcher = new SyncPacketDispatcher(inbox);
        var mockSink = new Mock<IInteractionNotifySinkStub>();
        var consumer = new FakeInteractionApplyConsumer(inbox, mockSink.Object);

        const long interactableId = 5001L;
        const long interactorId = 6001L;
        const int slotIdx = 0;

        dispatcher.Dispatch(new InteractionSyncPacket
        {
            SlotIdx = slotIdx,
            InteractableId = interactableId,
            InteractorId = interactorId,
            StateBits = InteractionStateBits.Start,
            ServerTick = 10L,
        });
        consumer.Update();

        Assert.Equal(1, consumer.TotalEntitiesCreated);
        Assert.Equal(0, consumer.TotalEntitiesDestroyed);
        Assert.True(consumer.IsEntityAlive(interactableId));
        Assert.Equal(1, consumer.AliveEntityCount);

        dispatcher.Dispatch(new InteractionSyncPacket
        {
            SlotIdx = slotIdx,
            InteractableId = interactableId,
            InteractorId = interactorId,
            StateBits = InteractionStateBits.Start,
            ServerTick = 11L,
        });
        consumer.Update();

        Assert.Equal(1, consumer.TotalEntitiesCreated);
        Assert.Equal(0, consumer.TotalEntitiesDestroyed);
        Assert.True(consumer.IsEntityAlive(interactableId));

        dispatcher.Dispatch(new InteractionSyncPacket
        {
            SlotIdx = slotIdx,
            InteractableId = interactableId,
            InteractorId = interactorId,
            StateBits = InteractionStateBits.End,
            ServerTick = 12L,
        });
        consumer.Update();

        Assert.Equal(1, consumer.TotalEntitiesCreated);
        Assert.Equal(1, consumer.TotalEntitiesDestroyed);
        Assert.False(consumer.IsEntityAlive(interactableId));
        Assert.Equal(0, consumer.AliveEntityCount);

        mockSink.Verify(
            s => s.NotifyInteractionStateChanged(It.IsAny<int>(), It.IsAny<byte>(), It.IsAny<long>(), It.IsAny<long>()),
            Times.Exactly(3));
    }

    [Fact]
    public void FullChain_WorldChunkDiffInteractionSyncPayload_RoutedCorrectly()
    {
        var inbox = new SyncPacketInbox();
        var dispatcher = new SyncPacketDispatcher(inbox);
        var mockSink = new Mock<IInteractionNotifySinkStub>();
        var consumer = new FakeInteractionApplyConsumer(inbox, mockSink.Object);

        var interactionPacket = new InteractionSyncPacket
        {
            SlotIdx = 5,
            InteractableId = 7777L,
            InteractorId = 8888L,
            StateBits = InteractionStateBits.Start,
            ServerTick = 99L,
        };

        var diff = new WorldChunkDiffPacket
        {
            ChunkMortonKey = 123,
            DiffSeqStart = 1,
            DiffSeqEnd = 1,
            Payload = MemoryPack.MemoryPackSerializer.Serialize(interactionPacket),
            PayloadType = WorldChunkDiffPayloadType.InteractionSync,
        };

        dispatcher.Dispatch(diff);
        consumer.Update();

        mockSink.Verify(
            s => s.NotifyInteractionStateChanged(
                interactionPacket.SlotIdx,
                interactionPacket.StateBits,
                interactionPacket.InteractableId,
                interactionPacket.InteractorId),
            Times.Once);

        Assert.Equal(1, dispatcher.InteractionSyncCount);
        Assert.Equal(1, consumer.TotalSyncPacketsProcessed);
    }

    [Fact]
    public void FullChain_EventPacket_RoutedToInteractionEvents()
    {
        var inbox = new SyncPacketInbox();
        var mockSink = new Mock<IInteractionNotifySinkStub>();
        var interactionEvents = new ConcurrentQueue<SyncEvent>();
        var consumer = new FakeInteractionApplyConsumer(inbox, mockSink.Object, interactionEvents);

        interactionEvents.Enqueue(new SyncEvent
        {
            Kind = SyncEventKind.InteractStart,
            SourceEntityId = 100,
            TargetEntityId = 200,
            IntValue = 1,
        });
        interactionEvents.Enqueue(new SyncEvent
        {
            Kind = SyncEventKind.InteractEnd,
            SourceEntityId = 100,
            TargetEntityId = 200,
            IntValue = 2,
        });
        interactionEvents.Enqueue(new SyncEvent
        {
            Kind = SyncEventKind.InteractStolen,
            SourceEntityId = 300,
            TargetEntityId = 200,
            IntValue = 3,
        });

        consumer.Update();

        mockSink.Verify(
            s => s.NotifyInteractionEvent((int)SyncEventKind.InteractStart, It.IsAny<object?>(), 200L, 100L),
            Times.Once);
        mockSink.Verify(
            s => s.NotifyInteractionEvent((int)SyncEventKind.InteractEnd, It.IsAny<object?>(), 200L, 100L),
            Times.Once);
        mockSink.Verify(
            s => s.NotifyInteractionEvent((int)SyncEventKind.InteractStolen, It.IsAny<object?>(), 200L, 300L),
            Times.Once);

        Assert.Equal(3, consumer.TotalInteractionEventsProcessed);
    }

    public interface IInteractionNotifySinkStub
    {
        void NotifyInteractionStateChanged(int slotIdx, byte newState, long interactableId, long interactorId);
        void NotifyInteractionEvent(int eventKind, object? payload, long interactableId, long interactorId);
    }

    private sealed class FakeInteractionApplyConsumer
    {
        private readonly SyncPacketInbox _inbox;
        private readonly IInteractionNotifySinkStub _sink;
        private readonly ConcurrentQueue<SyncEvent> _interactionEvents;
        private readonly Dictionary<long, bool> _entityAlive = new();

        public int TotalSyncPacketsProcessed { get; private set; }
        public int TotalInteractionEventsProcessed { get; private set; }
        public int TotalEntitiesCreated { get; private set; }
        public int TotalEntitiesDestroyed { get; private set; }
        public int AliveEntityCount => _entityAlive.Count;

        public FakeInteractionApplyConsumer(
            SyncPacketInbox inbox,
            IInteractionNotifySinkStub sink,
            ConcurrentQueue<SyncEvent>? interactionEvents = null)
        {
            _inbox = inbox ?? throw new ArgumentNullException(nameof(inbox));
            _sink = sink ?? throw new ArgumentNullException(nameof(sink));
            _interactionEvents = interactionEvents ?? new ConcurrentQueue<SyncEvent>();
        }

        public bool IsEntityAlive(long interactableId) => _entityAlive.ContainsKey(interactableId);

        public void Update()
        {
            ProcessInteractionSyncPackets();
            ProcessInteractionEvents();
        }

        private void ProcessInteractionSyncPackets()
        {
            while (_inbox.InteractionEvents.TryDequeue(out var packet))
            {
                TotalSyncPacketsProcessed++;

                if (!_entityAlive.ContainsKey(packet.InteractableId))
                {
                    _entityAlive[packet.InteractableId] = true;
                    TotalEntitiesCreated++;
                }

                _sink.NotifyInteractionStateChanged(
                    packet.SlotIdx,
                    packet.StateBits,
                    packet.InteractableId,
                    packet.InteractorId);

                if (InteractionStateBits.IsTerminal(packet.StateBits))
                {
                    if (_entityAlive.Remove(packet.InteractableId))
                    {
                        TotalEntitiesDestroyed++;
                    }
                }
            }
        }

        private void ProcessInteractionEvents()
        {
            while (_interactionEvents.TryDequeue(out var evt))
            {
                TotalInteractionEventsProcessed++;

                _sink.NotifyInteractionEvent(
                    (int)evt.Kind,
                    null,
                    interactableId: (long)evt.TargetEntityId,
                    interactorId: (long)evt.SourceEntityId);
            }
        }
    }
}
