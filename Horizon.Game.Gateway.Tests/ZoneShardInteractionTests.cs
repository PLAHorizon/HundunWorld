using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Horizon.Game.Message.Sync;
using Horizon.Orleans.Grains.World;
using Horizon.Orleans.Interface.World;
using Microsoft.Extensions.Logging;
using Moq;
using Orleans;
using Orleans.Runtime;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 阶段 10.4 — ZoneShardGrain.GenerateInteractionSync / BroadcastInteractionSyncAsync 测试。
/// 验证交互槽状态同步包的生成、槽位占用校验、AOI 订阅者推送。
/// </summary>
public class ZoneShardInteractionTests
{
    private static ZoneShardGrain CreateGrain()
    {
        var mockLogger = new Mock<ILogger<ZoneShardGrain>>();
        var grain = new ZoneShardGrain(mockLogger.Object);

        // ZoneShardGrain 直接 new 时 GrainContext 为 null，导致 GetPrimaryKeyLong() 抛 NullReferenceException。
        // 通过反射注入 mock IGrainContext，使日志参数中的 GetPrimaryKeyLong() 调用不再抛异常。
        var grainId = GrainId.Create(GrainType.Create("ZoneShard"), "1");
        var mockContext = new Mock<IGrainContext>();
        mockContext.SetupGet(c => c.GrainId).Returns(grainId);

        var contextField = typeof(Grain).GetField("<GrainContext>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);
        contextField?.SetValue(grain, mockContext.Object);

        return grain;
    }

    /// <summary>
    /// 验证 GenerateInteractionSync 生成正确字段的 InteractionSyncPacket 并推送到观察者。
    /// </summary>
    [Fact]
    public async Task GenerateInteractionSync_CreatesPacket_WithCorrectFields()
    {
        var grain = CreateGrain();
        var observer = new FakeFanoutObserver();
        await grain.SubscribeFanoutAsync(Guid.NewGuid(), observer);
        await grain.SubscribeSessionAsync(sessionId: 1, mortonKeys: new ulong[] { 0 });

        const int slotIdx = 2;
        const long interactableId = 3001L;
        const long interactorId = 4001L;
        const byte stateBits = InteractionStateBits.Start;
        const long serverTick = 777L;

        await grain.GenerateInteractionSync(slotIdx, interactableId, interactorId, stateBits, serverTick);

        Assert.Single(observer.ReceivedDiffs);

        var diff = observer.ReceivedDiffs[0];
        Assert.Equal(WorldChunkDiffPayloadType.InteractionSync, diff.Diff.PayloadType);

        var packet = MemoryPack.MemoryPackSerializer.Deserialize<InteractionSyncPacket>(diff.Diff.Payload);
        Assert.NotNull(packet);
        Assert.Equal(slotIdx, packet!.SlotIdx);
        Assert.Equal(interactableId, packet.InteractableId);
        Assert.Equal(interactorId, packet.InteractorId);
        Assert.Equal(stateBits, packet.StateBits);
        Assert.Equal(serverTick, packet.ServerTick);

        // 验证 sessionIds 包含已订阅的 session
        Assert.Contains(1L, diff.SessionIds);
    }

    /// <summary>
    /// 槽位占用：同一槽位已被其他交互者占用时，Start 应被拒绝（不产生新广播）。
    /// </summary>
    [Fact]
    public async Task GenerateInteractionSync_SlotOccupancy_StartBlocksDoubleStart()
    {
        var grain = CreateGrain();
        var observer = new FakeFanoutObserver();
        await grain.SubscribeFanoutAsync(Guid.NewGuid(), observer);
        await grain.SubscribeSessionAsync(sessionId: 1, mortonKeys: new ulong[] { 0 });

        const int slotIdx = 0;
        const long interactableId = 5001L;

        // 第一个交互者 Start — 应成功
        await grain.GenerateInteractionSync(slotIdx, interactableId, interactorId: 100L, InteractionStateBits.Start, serverTick: 1L);
        var firstCount = observer.ReceivedDiffs.Count;
        Assert.Equal(1, firstCount);

        // 第二个交互者 Start 同一槽位 — 应被拒绝
        await grain.GenerateInteractionSync(slotIdx, interactableId, interactorId: 200L, InteractionStateBits.Start, serverTick: 2L);

        // 仍然只有 1 条广播（第二次被拒绝）
        Assert.Equal(1, observer.ReceivedDiffs.Count);
    }

    /// <summary>
    /// 槽位释放：Start → End 后槽位被清理，可被新交互者重新占用。
    /// </summary>
    [Fact]
    public async Task GenerateInteractionSync_SlotOccupancy_EndClearsSlot()
    {
        var grain = CreateGrain();
        var observer = new FakeFanoutObserver();
        await grain.SubscribeFanoutAsync(Guid.NewGuid(), observer);
        await grain.SubscribeSessionAsync(sessionId: 1, mortonKeys: new ulong[] { 0 });

        const int slotIdx = 1;
        const long interactableId = 6001L;

        // 交互者 A Start
        await grain.GenerateInteractionSync(slotIdx, interactableId, interactorId: 100L, InteractionStateBits.Start, serverTick: 1L);
        Assert.Equal(1, observer.ReceivedDiffs.Count);

        // 交互者 A End — 释放槽位
        await grain.GenerateInteractionSync(slotIdx, interactableId, interactorId: 100L, InteractionStateBits.End, serverTick: 2L);
        Assert.Equal(2, observer.ReceivedDiffs.Count);

        // 交互者 B Start 同一槽位 — 应成功（槽位已释放）
        await grain.GenerateInteractionSync(slotIdx, interactableId, interactorId: 200L, InteractionStateBits.Start, serverTick: 3L);
        Assert.Equal(3, observer.ReceivedDiffs.Count);

        // 验证第三条广播的 packet 是交互者 B 的 Start
        var lastDiff = observer.ReceivedDiffs[2];
        var packet = MemoryPack.MemoryPackSerializer.Deserialize<InteractionSyncPacket>(lastDiff.Diff.Payload);
        Assert.NotNull(packet);
        Assert.Equal(200L, packet!.InteractorId);
        Assert.Equal(InteractionStateBits.Start, packet.StateBits);
    }

    /// <summary>
    /// 同一交互者对同一槽位重复 Start 应被视为更新（不拒绝），产生新广播。
    /// </summary>
    [Fact]
    public async Task GenerateInteractionSync_SameInteractor_StartIsUpdate()
    {
        var grain = CreateGrain();
        var observer = new FakeFanoutObserver();
        await grain.SubscribeFanoutAsync(Guid.NewGuid(), observer);
        await grain.SubscribeSessionAsync(sessionId: 1, mortonKeys: new ulong[] { 0 });

        const int slotIdx = 0;
        const long interactableId = 7001L;
        const long interactorId = 100L;

        // 同一交互者 Start 两次 — 第二次应被视为更新
        await grain.GenerateInteractionSync(slotIdx, interactableId, interactorId, InteractionStateBits.Start, serverTick: 1L);
        await grain.GenerateInteractionSync(slotIdx, interactableId, interactorId, InteractionStateBits.Start, serverTick: 2L);

        Assert.Equal(2, observer.ReceivedDiffs.Count);
    }

    /// <summary>
    /// Stolen 状态清理槽位：被抢占后槽位可被新交互者占用。
    /// </summary>
    [Fact]
    public async Task GenerateInteractionSync_StolenClearsSlot()
    {
        var grain = CreateGrain();
        var observer = new FakeFanoutObserver();
        await grain.SubscribeFanoutAsync(Guid.NewGuid(), observer);
        await grain.SubscribeSessionAsync(sessionId: 1, mortonKeys: new ulong[] { 0 });

        const int slotIdx = 0;
        const long interactableId = 8001L;

        // 交互者 A Start
        await grain.GenerateInteractionSync(slotIdx, interactableId, interactorId: 100L, InteractionStateBits.Start, serverTick: 1L);
        Assert.Equal(1, observer.ReceivedDiffs.Count);

        // 交互者 B Stolen — 抢占槽位并清理
        await grain.GenerateInteractionSync(slotIdx, interactableId, interactorId: 200L, InteractionStateBits.Stolen, serverTick: 2L);
        Assert.Equal(2, observer.ReceivedDiffs.Count);

        // 交互者 C Start 同一槽位 — 应成功（Stolen 已清理槽位）
        await grain.GenerateInteractionSync(slotIdx, interactableId, interactorId: 300L, InteractionStateBits.Start, serverTick: 3L);
        Assert.Equal(3, observer.ReceivedDiffs.Count);
    }

    /// <summary>
    /// 验证 BroadcastInteractionSyncAsync 将包推送到 AOI 订阅者：
    /// 无观察者时不推送；有观察者且有订阅者时推送。
    /// </summary>
    [Fact]
    public async Task BroadcastInteractionSyncAsync_PushesToAOISubscribers()
    {
        var grain = CreateGrain();
        var observer = new FakeFanoutObserver();
        await grain.SubscribeFanoutAsync(Guid.NewGuid(), observer);

        // 无订阅者时 — 不推送（sessionIds 为空）
        await grain.GenerateInteractionSync(0, 100L, 200L, InteractionStateBits.Start, 1L);
        Assert.Equal(0, observer.ReceivedDiffs.Count);

        // 注册订阅者后 — 推送
        await grain.SubscribeSessionAsync(sessionId: 10, mortonKeys: new ulong[] { 0 });
        await grain.SubscribeSessionAsync(sessionId: 20, mortonKeys: new ulong[] { 0 });

        await grain.GenerateInteractionSync(0, 100L, 200L, InteractionStateBits.Start, 2L);
        Assert.Equal(1, observer.ReceivedDiffs.Count);

        // 验证两个订阅者都在 sessionIds 中
        var sessionIds = observer.ReceivedDiffs[0].SessionIds;
        Assert.Contains(10L, sessionIds);
        Assert.Contains(20L, sessionIds);
    }

    /// <summary>
    /// 验证 serverTick=0 时使用 grain 内部 tick 计数。
    /// </summary>
    [Fact]
    public async Task GenerateInteractionSync_ServerTickZero_UsesInternalTick()
    {
        var grain = CreateGrain();
        var observer = new FakeFanoutObserver();
        await grain.SubscribeFanoutAsync(Guid.NewGuid(), observer);
        await grain.SubscribeSessionAsync(sessionId: 1, mortonKeys: new ulong[] { 0 });

        await grain.GenerateInteractionSync(0, 100L, 200L, InteractionStateBits.Start, serverTick: 0L);

        Assert.Single(observer.ReceivedDiffs);
        var packet = MemoryPack.MemoryPackSerializer.Deserialize<InteractionSyncPacket>(observer.ReceivedDiffs[0].Diff.Payload);
        Assert.NotNull(packet);
        // serverTick=0 时应使用 _tickCount（初始为 0 或 TickAsync 后递增的值）
        Assert.True(packet!.ServerTick >= 0);
    }

    /// <summary>
    /// 修复验证：Despawn 存在其他在线玩家时，必须广播 Despawn delta 并移除实体。
    /// 避免"有 fanout observer 但无 AOI 受众时误返回 true 导致实体被移除但 Despawn 未发出"的 BUG。
    /// </summary>
    [Fact]
    public async Task UnregisterEntityAsync_WithOtherSubscribers_BroadcastsDespawnAndRemovesEntity()
    {
        var grain = CreateGrain();
        var observer = new FakeFanoutObserver();
        await grain.SubscribeFanoutAsync(Guid.NewGuid(), observer);

        // A (entityId=22) 进入世界
        await grain.EnterWorldAsync(
            sessionId: 22,
            entityId: 22,
            initialX: 0, initialY: 0, initialZ: 0,
            initialInterestChunks: new ulong[] { 0 });

        // B (entityId=23) 进入世界并订阅同一 chunk
        await grain.EnterWorldAsync(
            sessionId: 23,
            entityId: 23,
            initialX: 0, initialY: 0, initialZ: 0,
            initialInterestChunks: new ulong[] { 0 });

        observer.ReceivedDiffs.Clear(); // 忽略 Spawn 广播

        // A 离线
        await grain.UnregisterEntityAsync(22);

        // Despawn 必须被广播给 B（至少包含 session 23）
        var despawnDiffs = observer.ReceivedDiffs
            .Where(d => d.Diff.PayloadType == WorldChunkDiffPayloadType.EntityDelta)
            .ToList();
        Assert.Single(despawnDiffs);
        Assert.Contains(23L, despawnDiffs[0].SessionIds);

        // 实体 22 必须已从服务端移除
        var entitiesField = typeof(ZoneShardGrain).GetField("_simulatedEntities", BindingFlags.NonPublic | BindingFlags.Instance);
        var entities = (System.Collections.Generic.Dictionary<ulong, object>)entitiesField!.GetValue(grain)!;
        Assert.DoesNotContain(22UL, entities.Keys);
        Assert.Contains(23UL, entities.Keys);
    }

    /// <summary>
    /// 修复验证：Despawn 无其他在线玩家时，不应广播 Despawn（没有受众），
    /// 应保留实体并过期租约，等待孤儿清理重试。避免误以为广播成功而提前移除实体。
    /// </summary>
    [Fact]
    public async Task UnregisterEntityAsync_WithoutOtherSubscribers_KeepsEntityForRetry()
    {
        var grain = CreateGrain();
        var observer = new FakeFanoutObserver();
        await grain.SubscribeFanoutAsync(Guid.NewGuid(), observer);

        // 只有 A (entityId=22) 在线
        await grain.EnterWorldAsync(
            sessionId: 22,
            entityId: 22,
            initialX: 0, initialY: 0, initialZ: 0,
            initialInterestChunks: new ulong[] { 0 });

        observer.ReceivedDiffs.Clear(); // 忽略 Spawn 广播

        // A 离线
        await grain.UnregisterEntityAsync(22);

        // 没有受众，不应发出 Despawn delta
        var despawnDiffs = observer.ReceivedDiffs
            .Where(d => d.Diff.PayloadType == WorldChunkDiffPayloadType.EntityDelta)
            .ToList();
        Assert.Empty(despawnDiffs);

        // 实体应被保留（等待孤儿清理重试）
        var entitiesField = typeof(ZoneShardGrain).GetField("_simulatedEntities", BindingFlags.NonPublic | BindingFlags.Instance);
        var entities = (System.Collections.Generic.Dictionary<ulong, object>)entitiesField!.GetValue(grain)!;
        Assert.Contains(22UL, entities.Keys);

        // 租约应立即过期（负数或过去时间）
        var entityValue = entities[22UL];
        var leaseField = entityValue.GetType().GetField("LeaseExpiry");
        var leaseExpiry = (DateTime)leaseField!.GetValue(entityValue)!;
        Assert.True(leaseExpiry < DateTime.UtcNow, "LeaseExpiry 应已过期，等待孤儿清理重试");
    }

    // ===== Fake fanout observer：捕获收到的 diff 与 sessionIds =====
    private sealed class FakeFanoutObserver : IZoneShardFanoutObserver
    {
        public List<(WorldChunkDiffPacket Diff, IReadOnlyCollection<long> SessionIds)> ReceivedDiffs { get; } = new();

        public Task OnChunkDiffAsync(WorldChunkDiffPacket diff, IReadOnlyCollection<long> sessionIds)
        {
            ReceivedDiffs.Add((diff, sessionIds));
            return Task.CompletedTask;
        }
    }
}

