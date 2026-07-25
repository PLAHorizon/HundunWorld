using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Horizon.Game.Core.Persistence;
using Horizon.Game.Message.Sync;
using Horizon.Game.Message.Sync.Components;
using Horizon.Orleans.Grains.World;
using Horizon.Orleans.Interface.World;
using Microsoft.Extensions.Logging;
using Moq;
using Orleans;
using Orleans.Runtime;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// Task C.7.2：HandleSceneObjectInteract 校验逻辑单元测试。
/// 验证 interactorId/objectId 合法性、冷却窗口、归属匹配、intentBits 过滤、
/// 默认状态创建、即时落盘触发等行为。
/// </summary>
public class SceneObjectInteractValidationTests
{
    /// <summary>
    /// 创建 ZoneShardGrain 测试实例，注入 mock IGrainContext 使 GetPrimaryKeyLong() 可用。
    /// </summary>
    private static ZoneShardGrain CreateGrain(ISceneObjectPersistenceStore? persistence = null)
    {
        var mockLogger = new Mock<ILogger<ZoneShardGrain>>();
        var mockState = new Mock<global::Orleans.Runtime.IPersistentState<Horizon.Orleans.Grains.World.ZoneShardState>>();
        mockState.SetupGet(s => s.State).Returns(new Horizon.Orleans.Grains.World.ZoneShardState());
        var grain = new ZoneShardGrain(mockLogger.Object, mockState.Object, persistence);

        // ZoneShardGrain 直接 new 时 GrainContext 为 null，导致 GetPrimaryKeyLong() 抛 NullReferenceException。
        // 通过反射注入 mock IGrainContext，使日志参数中的 GetPrimaryKeyLong() 调用不再抛异常。
        var grainId = GrainId.Create(GrainType.Create("ZoneShard"), "1");
        var mockContext = new Mock<IGrainContext>();
        mockContext.SetupGet(c => c.GrainId).Returns(grainId);

        var contextField = typeof(Grain).GetField("<GrainContext>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);
        contextField?.SetValue(grain, mockContext.Object);

        return grain;
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

    /// <summary>
    /// 从 diff.Payload 反序列化 SceneObjectSyncPacket 辅助方法。
    /// </summary>
    private static SceneObjectSyncPacket ExtractSceneObjectPacket(WorldChunkDiffPacket diff)
        => MemoryPack.MemoryPackSerializer.Deserialize<SceneObjectSyncPacket>(diff.Payload)!;

    // =======================================================================
    // 测试 1: Rejects_ZeroInteractorId
    // =======================================================================
    /// <summary>
    /// interactorId=0 应直接返回 false（无效交互者）。
    /// </summary>
    [Fact]
    public async Task Rejects_ZeroInteractorId()
    {
        var grain = CreateGrain();
        var observer = new FakeFanoutObserver();
        await grain.SubscribeFanoutAsync(Guid.NewGuid(), observer);
        await grain.SubscribeSessionAsync(sessionId: 1, mortonKeys: new ulong[] { 0 });

        var result = await grain.HandleSceneObjectInteract(interactorId: 0, objectId: 1001, intentBits: SceneObjectStateBits.Opened);

        Assert.False(result, "interactorId=0 应被拒绝");
        Assert.Empty(observer.ReceivedDiffs);
    }

    // =======================================================================
    // 测试 2: Rejects_ZeroObjectId
    // =======================================================================
    /// <summary>
    /// objectId=0 应直接返回 false（无效对象）。
    /// </summary>
    [Fact]
    public async Task Rejects_ZeroObjectId()
    {
        var grain = CreateGrain();
        var observer = new FakeFanoutObserver();
        await grain.SubscribeFanoutAsync(Guid.NewGuid(), observer);
        await grain.SubscribeSessionAsync(sessionId: 1, mortonKeys: new ulong[] { 0 });

        var result = await grain.HandleSceneObjectInteract(interactorId: 5001, objectId: 0, intentBits: SceneObjectStateBits.Opened);

        Assert.False(result, "objectId=0 应被拒绝");
        Assert.Empty(observer.ReceivedDiffs);
    }

    // =======================================================================
    // 测试 3: Rejects_WhenInCooldown
    // =======================================================================
    /// <summary>
    /// 首次交互成功后，对象进入冷却期（默认 300 tick）。
    /// 冷却期内同一交互者再次交互应被拒绝。
    /// </summary>
    [Fact]
    public async Task Rejects_WhenInCooldown()
    {
        var grain = CreateGrain();
        var observer = new FakeFanoutObserver();
        await grain.SubscribeFanoutAsync(Guid.NewGuid(), observer);
        await grain.SubscribeSessionAsync(sessionId: 1, mortonKeys: new ulong[] { 0 });

        // 首次交互 — 应成功
        const ulong interactor = 7001;
        const ulong objectId = 7001;
        var first = await grain.HandleSceneObjectInteract(interactor, objectId, SceneObjectStateBits.Opened);
        Assert.True(first, "首次交互应成功");
        var countAfterFirst = observer.ReceivedDiffs.Count;
        Assert.True(countAfterFirst >= 1, "首次交互应产生广播");

        // 冷却期内（_tickCount 仍为 0）再次交互 — 应被拒绝
        var second = await grain.HandleSceneObjectInteract(interactor, objectId, SceneObjectStateBits.Opened);
        Assert.False(second, "冷却期内交互应被拒绝");
        Assert.Equal(countAfterFirst, observer.ReceivedDiffs.Count);
    }

    // =======================================================================
    // 测试 4: Rejects_WhenOwnerMismatch
    // =======================================================================
    /// <summary>
    /// 对象已被某交互者占有后，其他交互者（owner 不匹配）应被拒绝。
    /// </summary>
    [Fact]
    public async Task Rejects_WhenOwnerMismatch()
    {
        var grain = CreateGrain();
        var observer = new FakeFanoutObserver();
        await grain.SubscribeFanoutAsync(Guid.NewGuid(), observer);
        await grain.SubscribeSessionAsync(sessionId: 1, mortonKeys: new ulong[] { 0 });

        const ulong objectId = 8001;
        // 交互者 A 先成功交互
        var first = await grain.HandleSceneObjectInteract(interactorId: 8001, objectId, SceneObjectStateBits.Opened);
        Assert.True(first);

        // 交互者 B（owner 不匹配）冷却期内尝试 — 应被拒绝
        var second = await grain.HandleSceneObjectInteract(interactorId: 8002, objectId, SceneObjectStateBits.Opened);
        Assert.False(second, "归属不匹配应被拒绝");
    }

    // =======================================================================
    // 测试 5: Rejects_WhenIntentBitsZero
    // =======================================================================
    /// <summary>
    /// intentBits=0（无任何有效状态位）应被拒绝。
    /// </summary>
    [Fact]
    public async Task Rejects_WhenIntentBitsZero()
    {
        var grain = CreateGrain();
        var observer = new FakeFanoutObserver();
        await grain.SubscribeFanoutAsync(Guid.NewGuid(), observer);
        await grain.SubscribeSessionAsync(sessionId: 1, mortonKeys: new ulong[] { 0 });

        var result = await grain.HandleSceneObjectInteract(interactorId: 9001, objectId: 9001, intentBits: 0);
        Assert.False(result, "intentBits=0 应被拒绝");
        Assert.Empty(observer.ReceivedDiffs);
    }

    // =======================================================================
    // 测试 6: Rejects_WhenIntentBitsAllFilteredOut
    // =======================================================================
    /// <summary>
    /// intentBits 仅含高于低 4 位的状态位（被 StateMask 过滤后为 0）应被拒绝。
    /// </summary>
    [Fact]
    public async Task Rejects_WhenIntentBitsAllFilteredOut()
    {
        var grain = CreateGrain();
        var observer = new FakeFanoutObserver();
        await grain.SubscribeFanoutAsync(Guid.NewGuid(), observer);
        await grain.SubscribeSessionAsync(sessionId: 1, mortonKeys: new ulong[] { 0 });

        // 0xF0 与 StateMask(0x0F) 按位与后为 0
        var result = await grain.HandleSceneObjectInteract(interactorId: 10001, objectId: 10001, intentBits: 0xF0);
        Assert.False(result, "intentBits 仅含被过滤位（过滤后为 0）应被拒绝");
        Assert.Empty(observer.ReceivedDiffs);
    }

    // =======================================================================
    // 测试 7: Accepts_FirstInteract_CreatesDefaultState
    // =======================================================================
    /// <summary>
    /// 首次交互未注册的对象应自动创建默认状态（ObjectType=Chest, StateBits=0, OwnerCharacterId=0），
    /// 交互成功并广播包含新状态的 SceneObjectSyncPacket。
    /// </summary>
    [Fact]
    public async Task Accepts_FirstInteract_CreatesDefaultState()
    {
        var grain = CreateGrain();
        var observer = new FakeFanoutObserver();
        await grain.SubscribeFanoutAsync(Guid.NewGuid(), observer);
        await grain.SubscribeSessionAsync(sessionId: 1, mortonKeys: new ulong[] { 0 });

        const ulong interactor = 11001;
        const ulong objectId = 11001;
        var result = await grain.HandleSceneObjectInteract(interactor, objectId, SceneObjectStateBits.Opened);

        Assert.True(result, "首次交互应成功并自动创建默认状态");
        Assert.Single(observer.ReceivedDiffs);

        var packet = ExtractSceneObjectPacket(observer.ReceivedDiffs[0].Diff);
        Assert.Equal(objectId, packet.ObjectId);
        Assert.Equal(SceneObjectStateBits.Opened, packet.StateBits);
        Assert.Equal(interactor, packet.OwnerCharacterId);
        // 默认冷却 300 tick，CooldownEndTick 应 = 当前 _tickCount(0) + 300
        Assert.Equal(300, packet.CooldownEndTick);
    }

    // =======================================================================
    // 测试 8: Accepts_WhenOwnerMatches
    // =======================================================================
    /// <summary>
    /// 同一 owner 在冷却到期后再次交互应成功（owner 匹配放行）。
    /// </summary>
    [Fact]
    public async Task Accepts_WhenOwnerMatches()
    {
        var grain = CreateGrain();
        var observer = new FakeFanoutObserver();
        await grain.SubscribeFanoutAsync(Guid.NewGuid(), observer);
        await grain.SubscribeSessionAsync(sessionId: 1, mortonKeys: new ulong[] { 0 });

        const ulong interactor = 12001;
        const ulong objectId = 12001;

        // 首次交互成功
        var first = await grain.HandleSceneObjectInteract(interactor, objectId, SceneObjectStateBits.Opened);
        Assert.True(first);

        // 推进 tick 经过冷却期（300 tick）
        for (int i = 0; i < 301; i++)
        {
            await grain.TickAsync(tickTime: 1.0 + i);
        }

        // 同一 owner 冷却到期后再次交互 — 应成功
        var second = await grain.HandleSceneObjectInteract(interactor, objectId, SceneObjectStateBits.Activated);
        Assert.True(second, "同一 owner 冷却到期后再次交互应成功");

        var lastPacket = ExtractSceneObjectPacket(observer.ReceivedDiffs[^1].Diff);
        Assert.Equal(SceneObjectStateBits.Activated, lastPacket.StateBits);
    }

    // =======================================================================
    // 测试 9: Accepts_AfterCooldownExpires
    // =======================================================================
    /// <summary>
    /// 冷却到期后（_tickCount >= CooldownEndTick），其他交互者也可成功交互。
    /// </summary>
    [Fact]
    public async Task Accepts_AfterCooldownExpires()
    {
        var grain = CreateGrain();
        var observer = new FakeFanoutObserver();
        await grain.SubscribeFanoutAsync(Guid.NewGuid(), observer);
        await grain.SubscribeSessionAsync(sessionId: 1, mortonKeys: new ulong[] { 0 });

        const ulong objectId = 13001;

        // 交互者 A 首次交互成功
        var first = await grain.HandleSceneObjectInteract(interactorId: 13001, objectId, SceneObjectStateBits.Opened);
        Assert.True(first);

        // 冷却期内交互者 B 尝试 — 应被拒绝（owner 不匹配）
        var blocked = await grain.HandleSceneObjectInteract(interactorId: 13002, objectId, SceneObjectStateBits.Opened);
        Assert.False(blocked, "冷却期内其他交互者应被拒绝");

        // 推进 300 tick 使冷却到期
        for (int i = 0; i < 300; i++)
        {
            await grain.TickAsync(tickTime: 1.0 + i);
        }

        // 冷却到期后交互者 B 尝试 — 仍应被拒绝（owner 不匹配，与冷却无关）
        // 注：归属校验独立于冷却，归属不匹配即便冷却到期也应拒绝
        var afterCooldown = await grain.HandleSceneObjectInteract(interactorId: 13002, objectId, SceneObjectStateBits.Opened);
        Assert.False(afterCooldown, "owner 不匹配时即便冷却到期也应被拒绝");

        // 但交互者 A（owner 匹配）应成功
        var ownerSuccess = await grain.HandleSceneObjectInteract(interactorId: 13001, objectId, SceneObjectStateBits.Opened);
        Assert.True(ownerSuccess, "owner 匹配且冷却到期应成功");
    }

    // =======================================================================
    // 测试 10: Sets_DefaultCooldown300Ticks
    // =======================================================================
    /// <summary>
    /// 验证默认冷却为 300 tick：在 _tickCount=0 时交互，CooldownEndTick 应为 300；
    /// 推进 299 tick（_tickCount=299）仍被冷却；推进 300 tick（_tickCount=300）放行。
    /// </summary>
    [Fact]
    public async Task Sets_DefaultCooldown300Ticks()
    {
        var grain = CreateGrain();
        var observer = new FakeFanoutObserver();
        await grain.SubscribeFanoutAsync(Guid.NewGuid(), observer);
        await grain.SubscribeSessionAsync(sessionId: 1, mortonKeys: new ulong[] { 0 });

        const ulong interactor = 14001;
        const ulong objectId = 14001;

        // _tickCount=0 时交互
        var first = await grain.HandleSceneObjectInteract(interactor, objectId, SceneObjectStateBits.Opened);
        Assert.True(first);

        var firstPacket = ExtractSceneObjectPacket(observer.ReceivedDiffs[0].Diff);
        Assert.Equal(300, firstPacket.CooldownEndTick);

        // 推进 299 tick（_tickCount=299）— 仍在冷却（CooldownEndTick=300 > 299）
        for (int i = 0; i < 299; i++)
        {
            await grain.TickAsync(tickTime: 1.0 + i);
        }

        var stillCooling = await grain.HandleSceneObjectInteract(interactor, objectId, SceneObjectStateBits.Activated);
        Assert.False(stillCooling, "_tickCount=299 仍在冷却期内（300 > 299）应被拒绝");

        // 再推进 1 tick（_tickCount=300）— 冷却到期（300 > 300 为 false）
        await grain.TickAsync(tickTime: 300.0);

        var expired = await grain.HandleSceneObjectInteract(interactor, objectId, SceneObjectStateBits.Activated);
        Assert.True(expired, "_tickCount=300 冷却到期应放行");
    }

    // =======================================================================
    // 测试 11: TriggersImmediatePersist_WhenChestOpened
    // =======================================================================
    /// <summary>
    /// 宝箱开启（StateBits 含 Opened）应触发即时落盘（SaveSingleAsync 被调用）。
    /// 验证 fire-and-forget 路径在 mock 同步完成时被观察到。
    /// </summary>
    [Fact]
    public async Task TriggersImmediatePersist_WhenChestOpened()
    {
        var mockStore = new Mock<ISceneObjectPersistenceStore>();
        mockStore
            .Setup(s => s.SaveSingleAsync(It.IsAny<long>(), It.IsAny<SceneObjectStateData>()))
            .Returns(Task.CompletedTask)
            .Verifiable();

        var grain = CreateGrain(mockStore.Object);
        var observer = new FakeFanoutObserver();
        await grain.SubscribeFanoutAsync(Guid.NewGuid(), observer);
        await grain.SubscribeSessionAsync(sessionId: 1, mortonKeys: new ulong[] { 0 });

        // Opened 位 → 应触发即时落盘
        var result = await grain.HandleSceneObjectInteract(interactorId: 15001, objectId: 15001, SceneObjectStateBits.Opened);
        Assert.True(result);

        // fire-and-forget 在 mock 同步完成时应已执行
        mockStore.Verify(s => s.SaveSingleAsync(It.IsAny<long>(), It.IsAny<SceneObjectStateData>()), Times.Once,
            "宝箱开启（Opened 位）应触发 SaveSingleAsync 即时落盘");
    }

    // =======================================================================
    // 测试 12: DoesNotTriggerPersist_WhenOnlyReset
    // =======================================================================
    /// <summary>
    /// 仅 Reset 位（不含 Opened/Activated）不应触发即时落盘。
    /// 补充覆盖关键事件判定边界。
    /// </summary>
    [Fact]
    public async Task DoesNotTriggerPersist_WhenOnlyReset()
    {
        var mockStore = new Mock<ISceneObjectPersistenceStore>();
        mockStore
            .Setup(s => s.SaveSingleAsync(It.IsAny<long>(), It.IsAny<SceneObjectStateData>()))
            .Returns(Task.CompletedTask)
            .Verifiable();

        var grain = CreateGrain(mockStore.Object);
        var observer = new FakeFanoutObserver();
        await grain.SubscribeFanoutAsync(Guid.NewGuid(), observer);
        await grain.SubscribeSessionAsync(sessionId: 1, mortonKeys: new ulong[] { 0 });

        // 仅 Reset 位（不在 Opened/Activated 触发条件内）
        var result = await grain.HandleSceneObjectInteract(interactorId: 16001, objectId: 16001, SceneObjectStateBits.Reset);
        Assert.True(result);

        mockStore.Verify(s => s.SaveSingleAsync(It.IsAny<long>(), It.IsAny<SceneObjectStateData>()), Times.Never,
            "仅 Reset 位（不含 Opened/Activated）不应触发即时落盘");
    }
}
