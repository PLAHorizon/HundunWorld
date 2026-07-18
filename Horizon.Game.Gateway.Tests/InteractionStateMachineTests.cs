using System.Collections.Generic;
using Horizon.Game.Core.Sim.Client;
using Horizon.Game.Message.Sync;
using Moq;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 阶段 10.7 — Task 6.4 拆分：C# 侧状态机 CI 回归测试。
/// 验证 InteractionStateBits 的状态位语义（Start/End/Stolen）及终态触发 despawn 的行为。
/// </summary>
/// <remarks>
/// 真实 InteractionApplySystem 定义在 UE5 托管脚本中，测试项目无法引用。
/// 终态 despawn 测试使用 FakeInteractionApplyConsumer（与 InteractionSyncChainTests 相同的测试桩）
/// 忠实复刻 InteractionApplySystem 的终态销毁逻辑。
/// </remarks>
public class InteractionStateMachineTests
{
    // ===== 状态位语义验证 =====

    /// <summary>
    /// Start (0x01) 状态位代表占用。
    /// </summary>
    [Fact]
    public void StateTransition_Occupy_StartStateBit()
    {
        Assert.Equal(0x01, InteractionStateBits.Start);
        Assert.True(InteractionStateBits.IsStart(InteractionStateBits.Start));
        Assert.False(InteractionStateBits.IsEnd(InteractionStateBits.Start));
        Assert.False(InteractionStateBits.IsStolen(InteractionStateBits.Start));
        Assert.False(InteractionStateBits.IsTerminal(InteractionStateBits.Start));

        // 组合位：Start | End 仍包含 Start
        Assert.True(InteractionStateBits.IsStart(InteractionStateBits.Start | InteractionStateBits.End));

        // StateMask 覆盖低 3 位
        Assert.Equal(0x07, InteractionStateBits.StateMask);
    }

    /// <summary>
    /// End (0x02) 状态位代表释放/结束。
    /// </summary>
    [Fact]
    public void StateTransition_Release_EndStateBit()
    {
        Assert.Equal(0x02, InteractionStateBits.End);
        Assert.True(InteractionStateBits.IsEnd(InteractionStateBits.End));
        Assert.False(InteractionStateBits.IsStart(InteractionStateBits.End));
        Assert.False(InteractionStateBits.IsStolen(InteractionStateBits.End));
        Assert.True(InteractionStateBits.IsTerminal(InteractionStateBits.End));
    }

    /// <summary>
    /// Stolen (0x04) 状态位代表被抢占。
    /// </summary>
    [Fact]
    public void StateTransition_Steal_StolenStateBit()
    {
        Assert.Equal(0x04, InteractionStateBits.Stolen);
        Assert.True(InteractionStateBits.IsStolen(InteractionStateBits.Stolen));
        Assert.False(InteractionStateBits.IsStart(InteractionStateBits.Stolen));
        Assert.False(InteractionStateBits.IsEnd(InteractionStateBits.Stolen));
        Assert.True(InteractionStateBits.IsTerminal(InteractionStateBits.Stolen));
    }

    /// <summary>
    /// 终态（End | Stolen）触发实体 despawn。
    /// 验证 InteractionApplySystem 的终态销毁逻辑：End 和 Stolen 状态应销毁实体。
    /// </summary>
    [Fact]
    public void StateTransition_TerminalStates_TriggerDespawn()
    {
        // 验证 IsTerminal 对所有终态组合返回 true
        Assert.True(InteractionStateBits.IsTerminal(InteractionStateBits.End));
        Assert.True(InteractionStateBits.IsTerminal(InteractionStateBits.Stolen));
        Assert.True(InteractionStateBits.IsTerminal(InteractionStateBits.End | InteractionStateBits.Stolen));

        // 验证非终态
        Assert.False(InteractionStateBits.IsTerminal(InteractionStateBits.Start));
        Assert.False(InteractionStateBits.IsTerminal((byte)0));
        Assert.False(InteractionStateBits.IsTerminal(InteractionStateBits.RequestStartFlag));

        // 使用 FakeInteractionApplyConsumer 验证终态触发 despawn 的行为
        var inbox = new SyncPacketInbox();
        var mockSink = new Mock<IInteractionNotifySinkStub>();
        var consumer = new FakeInteractionApplyConsumer(inbox, mockSink.Object);

        const long interactableId = 9001L;
        const long interactorId = 9002L;

        // Start — 创建实体
        inbox.InteractionEvents.Enqueue(new InteractionSyncPacket
        {
            SlotIdx = 0,
            InteractableId = interactableId,
            InteractorId = interactorId,
            StateBits = InteractionStateBits.Start,
            ServerTick = 1L,
        });
        consumer.Update();

        Assert.Equal(1, consumer.TotalEntitiesCreated);
        Assert.Equal(0, consumer.TotalEntitiesDestroyed);
        Assert.True(consumer.IsEntityAlive(interactableId));

        // End — 终态，销毁实体
        inbox.InteractionEvents.Enqueue(new InteractionSyncPacket
        {
            SlotIdx = 0,
            InteractableId = interactableId,
            InteractorId = interactorId,
            StateBits = InteractionStateBits.End,
            ServerTick = 2L,
        });
        consumer.Update();

        Assert.Equal(1, consumer.TotalEntitiesCreated);
        Assert.Equal(1, consumer.TotalEntitiesDestroyed);
        Assert.False(consumer.IsEntityAlive(interactableId));
    }

    /// <summary>
    /// Stolen 终态同样触发实体 despawn。
    /// </summary>
    [Fact]
    public void StateTransition_StolenTerminal_TriggersDespawn()
    {
        var inbox = new SyncPacketInbox();
        var mockSink = new Mock<IInteractionNotifySinkStub>();
        var consumer = new FakeInteractionApplyConsumer(inbox, mockSink.Object);

        const long interactableId = 9003L;

        // Start — 创建实体
        inbox.InteractionEvents.Enqueue(new InteractionSyncPacket
        {
            SlotIdx = 0,
            InteractableId = interactableId,
            InteractorId = 100L,
            StateBits = InteractionStateBits.Start,
            ServerTick = 1L,
        });
        consumer.Update();

        Assert.True(consumer.IsEntityAlive(interactableId));

        // Stolen — 终态，销毁实体
        inbox.InteractionEvents.Enqueue(new InteractionSyncPacket
        {
            SlotIdx = 0,
            InteractableId = interactableId,
            InteractorId = 200L,
            StateBits = InteractionStateBits.Stolen,
            ServerTick = 2L,
        });
        consumer.Update();

        Assert.False(consumer.IsEntityAlive(interactableId));
        Assert.Equal(1, consumer.TotalEntitiesDestroyed);
    }

    /// <summary>
    /// 非终态（Start）不触发 despawn，实体保持存活。
    /// </summary>
    [Fact]
    public void StateTransition_NonTerminal_KeepsEntityAlive()
    {
        var inbox = new SyncPacketInbox();
        var mockSink = new Mock<IInteractionNotifySinkStub>();
        var consumer = new FakeInteractionApplyConsumer(inbox, mockSink.Object);

        const long interactableId = 9004L;

        // 多次 Start 更新 — 实体始终存活
        for (int i = 0; i < 5; i++)
        {
            inbox.InteractionEvents.Enqueue(new InteractionSyncPacket
            {
                SlotIdx = 0,
                InteractableId = interactableId,
                InteractorId = 100L,
                StateBits = InteractionStateBits.Start,
                ServerTick = i,
            });
            consumer.Update();
        }

        Assert.True(consumer.IsEntityAlive(interactableId));
        Assert.Equal(1, consumer.TotalEntitiesCreated);
        Assert.Equal(0, consumer.TotalEntitiesDestroyed);
    }

    /// <summary>
    /// 上行意图位（RequestStartFlag/RequestStopFlag）不触发终态判定。
    /// </summary>
    [Fact]
    public void StateTransition_IntentBits_NotTerminal()
    {
        Assert.False(InteractionStateBits.IsTerminal(InteractionStateBits.RequestStartFlag));
        Assert.False(InteractionStateBits.IsTerminal(InteractionStateBits.RequestStopFlag));
        Assert.False(InteractionStateBits.IsTerminal(
            InteractionStateBits.RequestStartFlag | InteractionStateBits.RequestStopFlag));

        // 意图位 + Start 也不是终态
        Assert.False(InteractionStateBits.IsTerminal(
            InteractionStateBits.RequestStartFlag | InteractionStateBits.Start));

        // IntentMask 覆盖高 2 位
        Assert.Equal(0xC0, InteractionStateBits.IntentMask);
    }

    /// <summary>
    /// 终态后实体可被重新创建（新 Start 创建新实体）。
    /// </summary>
    [Fact]
    public void StateTransition_AfterDespawn_CanRespawn()
    {
        var inbox = new SyncPacketInbox();
        var mockSink = new Mock<IInteractionNotifySinkStub>();
        var consumer = new FakeInteractionApplyConsumer(inbox, mockSink.Object);

        const long interactableId = 9005L;

        // Start → End → Start（重新创建）
        inbox.InteractionEvents.Enqueue(new InteractionSyncPacket
        {
            InteractableId = interactableId,
            StateBits = InteractionStateBits.Start,
            ServerTick = 1L,
        });
        consumer.Update();
        Assert.Equal(1, consumer.TotalEntitiesCreated);

        inbox.InteractionEvents.Enqueue(new InteractionSyncPacket
        {
            InteractableId = interactableId,
            StateBits = InteractionStateBits.End,
            ServerTick = 2L,
        });
        consumer.Update();
        Assert.Equal(1, consumer.TotalEntitiesDestroyed);
        Assert.False(consumer.IsEntityAlive(interactableId));

        inbox.InteractionEvents.Enqueue(new InteractionSyncPacket
        {
            InteractableId = interactableId,
            StateBits = InteractionStateBits.Start,
            ServerTick = 3L,
        });
        consumer.Update();
        Assert.Equal(2, consumer.TotalEntitiesCreated);
        Assert.True(consumer.IsEntityAlive(interactableId));
    }

    // ===== 测试桩（与 InteractionSyncChainTests 一致） =====

    public interface IInteractionNotifySinkStub
    {
        void NotifyInteractionStateChanged(int slotIdx, byte newState, long interactableId, long interactorId);
        void NotifyInteractionEvent(int eventKind, object? payload, long interactableId, long interactorId);
    }

    private sealed class FakeInteractionApplyConsumer
    {
        private readonly SyncPacketInbox _inbox;
        private readonly IInteractionNotifySinkStub _sink;
        private readonly Dictionary<long, bool> _entityAlive = new();

        public int TotalSyncPacketsProcessed { get; private set; }
        public int TotalEntitiesCreated { get; private set; }
        public int TotalEntitiesDestroyed { get; private set; }

        public FakeInteractionApplyConsumer(SyncPacketInbox inbox, IInteractionNotifySinkStub sink)
        {
            _inbox = inbox;
            _sink = sink;
        }

        public bool IsEntityAlive(long interactableId) => _entityAlive.ContainsKey(interactableId);

        public void Update()
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
    }
}
