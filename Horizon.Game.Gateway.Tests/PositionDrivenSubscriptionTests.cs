using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Horizon.Game.Core.Sim;
using Horizon.Game.Core.World;
using Horizon.Game.Message.Sync;
using MemoryPack;
using Horizon.Orleans.Grains.World;
using Horizon.Orleans.Interface;
using Horizon.Orleans.Interface.World;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Orleans;
using Orleans.Runtime;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 位置驱动订阅（UpdateSessionPositionAsync）单元测试。
/// 验证 Flax Y-up → ECS Z-up 坐标系转换、首次全量订阅、增量更新、幂等性等核心语义。
/// </summary>
public class PositionDrivenSubscriptionTests
{
    /// <summary>
    /// 创建 ZoneShardGrain 实例用于测试。
    /// 使用 NullLogger 避免长序列日志累积。
    /// </summary>
    private static ZoneShardGrain CreateGrain()
    {
        var logger = NullLogger<ZoneShardGrain>.Instance;
        var mockState = new Mock<IPersistentState<ZoneShardState>>();
        mockState.SetupGet(s => s.State).Returns(new ZoneShardState());
        var grain = new ZoneShardGrain(logger, mockState.Object);

        var grainId = GrainId.Create(GrainType.Create("ZoneShard"), "1");
        var mockContext = new Mock<IGrainContext>();
        mockContext.SetupGet(c => c.GrainId).Returns(grainId);

        var contextField = typeof(Grain).GetField("<GrainContext>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);
        contextField?.SetValue(grain, mockContext.Object);

        grain.SnapshotBroadcastIntervalTicks = 1;
        return grain;
    }

    /// <summary>
    /// 获取 ZoneShardGrain 内部的 SessionAoiRadiusChunks 常量值（通过反射）。
    /// </summary>
    private static int GetSessionAoiRadiusChunks()
    {
        var field = typeof(ZoneShardGrain).GetField("SessionAoiRadiusChunks",
            BindingFlags.NonPublic | BindingFlags.Static);
        return (int)field!.GetValue(null)!;
    }

    /// <summary>
    /// 首次调用 UpdateSessionPositionAsync 时，session 尚无任何订阅，
    /// 应全量订阅以玩家位置为中心、半径 R 的立方体内所有 chunk。
    /// 返回值 = 新增条数 = (2R+1)³。
    /// </summary>
    [Fact]
    public async Task UpdateSessionPosition_FirstCall_SubscribesAllChunks()
    {
        var grain = CreateGrain();
        var radius = GetSessionAoiRadiusChunks();
        const long sessionId = 1001;
        const float x = 0f, y = 0f, z = 0f;

        var changed = await grain.UpdateSessionPositionAsync(sessionId, x, y, z);
        var expectedCount = (2 * radius + 1) * (2 * radius + 1) * (2 * radius + 1);

        Assert.Equal(expectedCount, changed);

        // 验证内部订阅数与 GetStatsAsync 一致
        var (sessionCount, chunkCount) = await grain.GetStatsAsync();
        Assert.Equal(1, sessionCount);
        Assert.Equal(expectedCount, chunkCount);
    }

    /// <summary>
    /// 相同位置再次调用 UpdateSessionPositionAsync 时，
    /// 新视野与旧视野完全重叠，added=0, removed=0, 返回值=0。
    /// </summary>
    [Fact]
    public async Task UpdateSessionPosition_SamePosition_NoChange()
    {
        var grain = CreateGrain();
        const long sessionId = 1002;
        const float x = 100f, y = 50f, z = 200f;

        // 首次订阅
        var firstCall = await grain.UpdateSessionPositionAsync(sessionId, x, y, z);
        Assert.True(firstCall > 0);

        // 相同位置再次调用
        var secondCall = await grain.UpdateSessionPositionAsync(sessionId, x, y, z);
        Assert.Equal(0, secondCall);

        // 验证订阅数未变
        var (_, chunkCount) = await grain.GetStatsAsync();
        Assert.Equal(firstCall, chunkCount);
    }

    /// <summary>
    /// 玩家沿 X 轴移动一个 chunk（16 米）后，视野中心从 (0,0,0) 变为 (1,0,0)。
    /// 新增 = x=29 平面所有 chunk = (2R+1)² 个；移除 = x=-28 平面所有 chunk = (2R+1)² 个。
    /// 返回值 = 2 × (2R+1)²。
    /// </summary>
    [Fact]
    public async Task UpdateSessionPosition_MoveOneChunk_IncrementalUpdate()
    {
        var grain = CreateGrain();
        var radius = GetSessionAoiRadiusChunks();
        const long sessionId = 1003;

        // 初始位置 (0, 0, 0) → chunk (0, 0, 0)
        await grain.UpdateSessionPositionAsync(sessionId, 0f, 0f, 0f);

        // 沿 Flax X 轴移动 16 米（= 1 chunk）
        // Flax Y-up: (16, 0, 0) → GetChunksInView(16, 0, 0) → chunk (1, 0, 0)
        var changed = await grain.UpdateSessionPositionAsync(sessionId, 16f, 0f, 0f);

        // 移动 1 chunk：新增 x=29 平面，移除 x=-28 平面
        var planeSize = (2 * radius + 1) * (2 * radius + 1);
        Assert.Equal(2 * planeSize, changed);
    }

    /// <summary>
    /// 验证坐标系转换：UpdateSessionPositionAsync(x, y, z) 的订阅结果
    /// 必须与 WorldCoord.GetChunksInView(x, z, y, R) 一致（Flax Y-up → ECS Z-up：Y/Z 互换）。
    /// </summary>
    [Fact]
    public async Task UpdateSessionPosition_CoordinateConversion_FlaxYupToEcsZup()
    {
        var grain = CreateGrain();
        var radius = GetSessionAoiRadiusChunks();
        const long sessionId = 1004;
        const float x = 32f, y = 48f, z = 64f;

        await grain.UpdateSessionPositionAsync(sessionId, x, y, z);

        // Flax Y-up (x=左右, y=上下, z=前后) → ECS Z-up (X=左右, Y=前后, Z=上下)：Y/Z 互换
        // grain 内部调用 GetChunksInView(x, z, y, R)
        var expectedView = WorldCoord.GetChunksInView(x, z, y, radius);

        // 通过 GetSubscribersAsync 间接验证：expectedView 中的每个 chunk 都应有 sessionId 订阅
        // 抽样验证几个 chunk（避免遍历 185193 个）
        var sampleChunks = expectedView.Take(10).ToArray();
        foreach (var chunkKey in sampleChunks)
        {
            var subscribers = await grain.GetSubscribersAsync(chunkKey);
            Assert.Contains(sessionId, subscribers);
        }

        // 验证视野外的一个 chunk 不在订阅中
        // 玩家在 chunk (2, 4, 3)（ECS），半径 R=28，视野外 chunk (2+R+1, 4, 3) = (31, 4, 3)
        var ecsCenter = WorldCoord.ToChunk(x, z, y);
        var outsideKey = MortonCodec.Encode3D(ecsCenter.X + radius + 1, ecsCenter.Y, ecsCenter.Z);
        var outsideSubscribers = await grain.GetSubscribersAsync(outsideKey);
        Assert.DoesNotContain(sessionId, outsideSubscribers);
    }

    /// <summary>
    /// 对未注册的 session 调用 UpdateSessionPositionAsync，
    /// oldView 为空，所有 chunk 均为新增，返回值 = (2R+1)³。
    /// </summary>
    [Fact]
    public async Task UpdateSessionPosition_UnregisteredSession_SubscribesAllChunks()
    {
        var grain = CreateGrain();
        var radius = GetSessionAoiRadiusChunks();
        const long sessionId = 1005;

        // 直接调用，不经过 EnterWorldAsync / SubscribeSessionAsync
        var changed = await grain.UpdateSessionPositionAsync(sessionId, 1000f, 2000f, 3000f);
        var expectedCount = (2 * radius + 1) * (2 * radius + 1) * (2 * radius + 1);

        Assert.Equal(expectedCount, changed);
    }

    /// <summary>
    /// 多次连续移动后，订阅数始终等于 (2R+1)³（视野体积不变，只是平移）。
    /// 验证多次增量更新不会导致订阅泄漏或丢失。
    /// </summary>
    [Fact]
    public async Task UpdateSessionPosition_MultipleMoves_StableSubscriptionCount()
    {
        var grain = CreateGrain();
        var radius = GetSessionAoiRadiusChunks();
        const long sessionId = 1006;
        var expectedCount = (2 * radius + 1) * (2 * radius + 1) * (2 * radius + 1);

        // 连续移动 5 次
        var positions = new (float X, float Y, float Z)[]
        {
            (0f, 0f, 0f),
            (16f, 0f, 0f),      // +X 1 chunk
            (16f, 16f, 0f),     // +Y 1 chunk（Flax Y-up，Y=高度）
            (16f, 16f, 16f),    // +Z 1 chunk
            (32f, 32f, 32f),    // 各+1 chunk
        };

        foreach (var (x, y, z) in positions)
        {
            await grain.UpdateSessionPositionAsync(sessionId, x, y, z);

            var (_, chunkCount) = await grain.GetStatsAsync();
            Assert.Equal(expectedCount, chunkCount);
        }
    }

    /// <summary>
    /// 多个 session 各自独立订阅，互不干扰。
    /// 验证 UpdateSessionPositionAsync 按 sessionId 隔离。
    /// </summary>
    [Fact]
    public async Task UpdateSessionPosition_MultipleSessions_IndependentSubscriptions()
    {
        var grain = CreateGrain();
        var radius = GetSessionAoiRadiusChunks();
        var expectedCount = (2 * radius + 1) * (2 * radius + 1) * (2 * radius + 1);

        // 两个 session 在不同位置
        await grain.UpdateSessionPositionAsync(2001, 0f, 0f, 0f);
        await grain.UpdateSessionPositionAsync(2002, 1000f, 1000f, 1000f);

        var (sessionCount, chunkCount) = await grain.GetStatsAsync();
        Assert.Equal(2, sessionCount);

        // 两个 session 位置相距很远（1000m ≈ 62 chunks > 2R=56），视野不重叠
        // chunkCount 应为 2 × (2R+1)³
        Assert.Equal(2 * expectedCount, chunkCount);

        // 验证 session 2001 的视野 chunk 不包含 session 2002
        var view1 = WorldCoord.GetChunksInView(0f, 0f, 0f, radius);
        var sampleChunk = view1.First();
        var subscribers = await grain.GetSubscribersAsync(sampleChunk);
        Assert.Contains(2001L, subscribers);
        Assert.DoesNotContain(2002L, subscribers);
    }

    /// <summary>
    /// 验证位置驱动订阅与 BroadcastSnapshotAsync 中坐标系转换一致。
    /// 实体注册时用 Flax Y-up (x, y, z)，UpdateSessionPositionAsync 也用 Flax Y-up，
    /// 两者内部都做 Y/Z 互换为 ECS Z-up，确保实体所在 chunk 在订阅视野内。
    /// </summary>
    [Fact]
    public async Task UpdateSessionPosition_AlignsWithEntityRegistration()
    {
        var grain = CreateGrain();
        const long sessionId = 3001;
        const ulong entityId = 3001;
        const float x = 100f, y = 200f, z = 300f;

        // 用 EnterWorldAsync 注册实体（传空 chunk 数组，仅注册实体）
        await grain.EnterWorldAsync(sessionId, entityId, x, y, z, System.Array.Empty<ulong>());

        // 用 UpdateSessionPositionAsync 建立订阅
        await grain.UpdateSessionPositionAsync(sessionId, x, y, z);

        // 实体所在 chunk（ECS Z-up）必须在 session 的订阅视野内
        // EnterWorldAsync 内部用 (x, y, z) 注册实体位置
        // BroadcastSnapshotAsync 用 ToChunkMortonKey(t.X, t.Z, t.Y) 计算实体 chunk
        // UpdateSessionPositionAsync 用 GetChunksInView(x, z, y, R) 计算视野
        // 两者坐标系转换一致，实体 chunk 必在视野内
        var entityChunkKey = WorldCoord.ToChunkMortonKey(x, z, y); // 与 BroadcastSnapshotAsync 一致
        var subscribers = await grain.GetSubscribersAsync(entityChunkKey);
        Assert.Contains(sessionId, subscribers);

        // 验证实体已注册
        var hasEntity = await grain.HasEntityAsync(entityId);
        Assert.True(hasEntity);
    }

    /// <summary>
    /// 验证 CorrectionPacket 可以被 MemoryPack 正确序列化和反序列化。
    /// 修复 BUG：Horizon.Game.Core 缺少 MemoryPack.Generator 引用，导致 CorrectionPacket
    /// 的 formatter 未生成，BroadcastSnapshotAsync 序列化 correction 时抛出
    /// "CorrectionPacket is not registered in this provider" 异常，
    /// 进而导致快照广播失败 → 远程角色"离线" + tick 中断 → 本地角色卡顿。
    /// </summary>
    [Fact]
    public void CorrectionPacket_MemoryPackSerialization_RoundTrip()
    {
        var original = new CorrectionPacket
        {
            EntityId = 12345,
            ServerTick = 9999,
            CorrectedX = 100.5f,
            CorrectedY = 200.5f,
            CorrectedZ = 300.5f,
            CorrectedVz = 5.5f,
            DriftMeters = 1.23f,
            Reason = CorrectionReason.PredictionDrift,
            LastProcessedClientTick = 8888,
        };

        // 序列化（这里之前会抛出 MemoryPackSerializationException）
        var bytes = MemoryPackSerializer.Serialize(original);
        Assert.NotEmpty(bytes);

        // 反序列化
        var deserialized = MemoryPackSerializer.Deserialize<CorrectionPacket>(bytes)!;

        Assert.Equal(original.EntityId, deserialized.EntityId);
        Assert.Equal(original.ServerTick, deserialized.ServerTick);
        Assert.Equal(original.CorrectedX, deserialized.CorrectedX);
        Assert.Equal(original.CorrectedY, deserialized.CorrectedY);
        Assert.Equal(original.CorrectedZ, deserialized.CorrectedZ);
        Assert.Equal(original.CorrectedVz, deserialized.CorrectedVz);
        Assert.Equal(original.DriftMeters, deserialized.DriftMeters);
        Assert.Equal(original.Reason, deserialized.Reason);
        Assert.Equal(original.LastProcessedClientTick, deserialized.LastProcessedClientTick);
    }
}
