using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Horizon.Game.Core.World;
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
/// Task C.7.3：BroadcastSceneObjectSyncAsync AOI 过滤正确性测试。
/// 验证：
/// - 无 fanout 观察者时不抛异常；
/// - 无 AOI 订阅者时不推送；
/// - 有 Transform 的对象仅推送给所在 chunk 的订阅者；
/// - 无 Transform 的对象回退广播到全部订阅者；
/// - 多个 fanout 观察者均收到推送；
/// - 推送的 PayloadType 与 SceneObjectSyncPacket 字段正确。
/// </summary>
public class SceneObjectBroadcastAoiTests
{
    /// <summary>
    /// 创建 ZoneShardGrain 测试实例，注入 mock IGrainContext 使 GetPrimaryKeyLong() 可用。
    /// </summary>
    private static ZoneShardGrain CreateGrain()
    {
        var mockLogger = new Mock<ILogger<ZoneShardGrain>>();
        var grain = new ZoneShardGrain(mockLogger.Object);

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
    // 测试 1: Broadcast_WithoutFanoutObservers_NoException
    // =======================================================================
    /// <summary>
    /// 无 fanout 观察者时 HandleSceneObjectInteract 不应抛异常，交互仍返回 true。
    /// 验证 BroadcastSceneObjectSyncAsync 的早退分支（_fanoutObservers.Count == 0）。
    /// </summary>
    [Fact]
    public async Task Broadcast_WithoutFanoutObservers_NoException()
    {
        var grain = CreateGrain();
        // 不订阅任何 fanout 观察者

        var result = await grain.HandleSceneObjectInteract(interactorId: 1001, objectId: 1001, SceneObjectStateBits.Opened);

        Assert.True(result, "无 fanout 观察者时交互仍应成功（仅跳过广播）");
    }

    // =======================================================================
    // 测试 2: Broadcast_WithoutSubscribers_NoDiff
    // =======================================================================
    /// <summary>
    /// 有 fanout 观察者但无 AOI 订阅者时不推送 diff（sessionIds.Length == 0 早退）。
    /// </summary>
    [Fact]
    public async Task Broadcast_WithoutSubscribers_NoDiff()
    {
        var grain = CreateGrain();
        var observer = new FakeFanoutObserver();
        await grain.SubscribeFanoutAsync(Guid.NewGuid(), observer);
        // 不订阅任何 session 到 chunk

        await grain.HandleSceneObjectInteract(interactorId: 2001, objectId: 2001, SceneObjectStateBits.Opened);

        Assert.Empty(observer.ReceivedDiffs);
    }

    // =======================================================================
    // 测试 3: Broadcast_NoTransform_FallbackToAllSubscribers
    // =======================================================================
    /// <summary>
    /// 场景对象无 Transform 数据（TransformX/Y/Z 全 0）时回退广播到全部订阅者。
    /// 验证 GetAllSubscribers 分支：多个不同 chunk 的订阅者都应收到。
    /// </summary>
    [Fact]
    public async Task Broadcast_NoTransform_FallbackToAllSubscribers()
    {
        var grain = CreateGrain();
        var observer = new FakeFanoutObserver();
        await grain.SubscribeFanoutAsync(Guid.NewGuid(), observer);

        // 订阅 session 1/2/3 到不同 chunk
        var chunkA = MortonCodec.Encode3D(1, 0, 0);
        var chunkB = MortonCodec.Encode3D(2, 0, 0);
        var chunkC = MortonCodec.Encode3D(3, 0, 0);
        await grain.SubscribeSessionAsync(sessionId: 1, mortonKeys: new[] { chunkA });
        await grain.SubscribeSessionAsync(sessionId: 2, mortonKeys: new[] { chunkB });
        await grain.SubscribeSessionAsync(sessionId: 3, mortonKeys: new[] { chunkC });

        // 首次交互创建的对象无 Transform → 回退广播
        await grain.HandleSceneObjectInteract(interactorId: 3001, objectId: 3001, SceneObjectStateBits.Opened);

        Assert.Single(observer.ReceivedDiffs);
        var (_, sessionIds) = observer.ReceivedDiffs[0];
        Assert.Contains(1L, sessionIds);
        Assert.Contains(2L, sessionIds);
        Assert.Contains(3L, sessionIds);
        Assert.Equal(3, sessionIds.Length);
    }

    // =======================================================================
    // 测试 4: Broadcast_WithTransform_OnlyChunkSubscribersReceive
    // =======================================================================
    /// <summary>
    /// 场景对象有 Transform 时仅推送给所在 chunk 的订阅者（GetSubscribers 分支）。
    /// 验证 AOI 过滤：同 chunk 订阅者收到，其他 chunk 订阅者不收到。
    /// </summary>
    [Fact]
    public async Task Broadcast_WithTransform_OnlyChunkSubscribersReceive()
    {
        var grain = CreateGrain();
        var observer = new FakeFanoutObserver();
        await grain.SubscribeFanoutAsync(Guid.NewGuid(), observer);

        // 计算目标 chunk：将场景对象放在 (16, 0, 0) → chunk (1,0,0)
        const float objX = 16f, objY = 0f, objZ = 0f;
        var targetChunk = WorldCoord.ToChunkMortonKey(objX, objY, objZ);

        // 订阅 session 1 到目标 chunk，session 2 到不同 chunk
        var otherChunk = MortonCodec.Encode3D(5, 5, 5);
        await grain.SubscribeSessionAsync(sessionId: 1, mortonKeys: new[] { targetChunk });
        await grain.SubscribeSessionAsync(sessionId: 2, mortonKeys: new[] { otherChunk });

        // 预注册场景对象（带 Transform），使其在交互时使用 chunk 过滤
        await grain.RegisterSceneObjectAsync(objectId: 4001, SceneObjectType.Chest, initialStateBits: 0,
            transformX: objX, transformY: objY, transformZ: objZ);

        await grain.HandleSceneObjectInteract(interactorId: 4001, objectId: 4001, SceneObjectStateBits.Opened);

        Assert.Single(observer.ReceivedDiffs);
        var (diff, sessionIds) = observer.ReceivedDiffs[0];

        // 仅 session 1 收到
        Assert.Contains(1L, sessionIds);
        Assert.DoesNotContain(2L, sessionIds);
        Assert.Single(sessionIds);

        // ChunkMortonKey 应为目标 chunk
        Assert.Equal(targetChunk, diff.ChunkMortonKey);
    }

    // =======================================================================
    // 测试 5: Broadcast_PayloadType_IsSceneObjectSync
    // =======================================================================
    /// <summary>
    /// 推送的 diff.PayloadType 应为 SceneObjectSync，Payload 可反序列化为 SceneObjectSyncPacket。
    /// </summary>
    [Fact]
    public async Task Broadcast_PayloadType_IsSceneObjectSync()
    {
        var grain = CreateGrain();
        var observer = new FakeFanoutObserver();
        await grain.SubscribeFanoutAsync(Guid.NewGuid(), observer);
        await grain.SubscribeSessionAsync(sessionId: 1, mortonKeys: new ulong[] { 0 });

        await grain.HandleSceneObjectInteract(interactorId: 5001, objectId: 5001, SceneObjectStateBits.Opened);

        Assert.Single(observer.ReceivedDiffs);
        var (diff, _) = observer.ReceivedDiffs[0];
        Assert.Equal(WorldChunkDiffPayloadType.SceneObjectSync, diff.PayloadType);

        var packet = ExtractSceneObjectPacket(diff);
        Assert.NotNull(packet);
        Assert.Equal(SyncPacketKind.SceneObjectSync, packet.Kind);
    }

    // =======================================================================
    // 测试 6: Broadcast_PacketFields_MatchInteract
    // =======================================================================
    /// <summary>
    /// 推送的 SceneObjectSyncPacket 字段应与 HandleSceneObjectInteract 的输入与状态变更一致。
    /// </summary>
    [Fact]
    public async Task Broadcast_PacketFields_MatchInteract()
    {
        var grain = CreateGrain();
        var observer = new FakeFanoutObserver();
        await grain.SubscribeFanoutAsync(Guid.NewGuid(), observer);
        await grain.SubscribeSessionAsync(sessionId: 1, mortonKeys: new ulong[] { 0 });

        const ulong interactor = 6001;
        const ulong objectId = 6001;
        const uint intent = SceneObjectStateBits.Opened;

        await grain.HandleSceneObjectInteract(interactor, objectId, intent);

        Assert.Single(observer.ReceivedDiffs);
        var packet = ExtractSceneObjectPacket(observer.ReceivedDiffs[0].Diff);

        Assert.Equal(objectId, packet.ObjectId);
        Assert.Equal(intent, packet.StateBits);
        Assert.Equal(interactor, packet.OwnerCharacterId);
        // 默认冷却 300 tick，_tickCount=0 时 CooldownEndTick=300
        Assert.Equal(300, packet.CooldownEndTick);
        // 无 Transform 注册 → HasTransform=false
        Assert.False(packet.HasTransform);
        // ServerTick 应为当前 _tickCount=0
        Assert.Equal(0, packet.ServerTick);
    }

    // =======================================================================
    // 测试 7: Broadcast_MultipleObservers_AllReceive
    // =======================================================================
    /// <summary>
    /// 多个 fanout 观察者订阅时，每个都应收到推送（观察者列表 ToArray 遍历）。
    /// </summary>
    [Fact]
    public async Task Broadcast_MultipleObservers_AllReceive()
    {
        var grain = CreateGrain();
        var observer1 = new FakeFanoutObserver();
        var observer2 = new FakeFanoutObserver();
        var observer3 = new FakeFanoutObserver();
        await grain.SubscribeFanoutAsync(Guid.NewGuid(), observer1);
        await grain.SubscribeFanoutAsync(Guid.NewGuid(), observer2);
        await grain.SubscribeFanoutAsync(Guid.NewGuid(), observer3);
        await grain.SubscribeSessionAsync(sessionId: 1, mortonKeys: new ulong[] { 0 });

        await grain.HandleSceneObjectInteract(interactorId: 7001, objectId: 7001, SceneObjectStateBits.Opened);

        Assert.Single(observer1.ReceivedDiffs);
        Assert.Single(observer2.ReceivedDiffs);
        Assert.Single(observer3.ReceivedDiffs);
    }

    // =======================================================================
    // 测试 8: Broadcast_WithTransform_SubscriberAtSameChunkReceives
    // =======================================================================
    /// <summary>
    /// 补充 AOI 过滤边界用例：场景对象位于 (16,16,16)（chunk (1,1,1)），
    /// 订阅该 chunk 的 session 收到，订阅相邻 chunk (2,1,1) 的 session 不收到。
    /// </summary>
    [Fact]
    public async Task Broadcast_WithTransform_SubscriberAtSameChunkReceives()
    {
        var grain = CreateGrain();
        var observer = new FakeFanoutObserver();
        await grain.SubscribeFanoutAsync(Guid.NewGuid(), observer);

        const float objX = 16f, objY = 16f, objZ = 16f;
        var sameChunk = WorldCoord.ToChunkMortonKey(objX, objY, objZ);
        var adjacentChunk = MortonCodec.Encode3D(2, 1, 1);

        await grain.SubscribeSessionAsync(sessionId: 10, mortonKeys: new[] { sameChunk });
        await grain.SubscribeSessionAsync(sessionId: 20, mortonKeys: new[] { adjacentChunk });

        await grain.RegisterSceneObjectAsync(objectId: 8001, SceneObjectType.Door, initialStateBits: 0,
            transformX: objX, transformY: objY, transformZ: objZ);

        await grain.HandleSceneObjectInteract(interactorId: 8001, objectId: 8001, SceneObjectStateBits.Activated);

        Assert.Single(observer.ReceivedDiffs);
        var (_, sessionIds) = observer.ReceivedDiffs[0];
        Assert.Contains(10L, sessionIds);
        Assert.DoesNotContain(20L, sessionIds);
    }
}
