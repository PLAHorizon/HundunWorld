using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Horizon.Game.Core.World;
using Horizon.Game.ECS.Arch.Systems;
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
/// Task D.7.3：Snapshot 增量编码往返测试。
/// 验证 ZoneShardGrain 的全量/增量快照切换、EntityDeltaChanged 阈值检测、
/// 客户端 SnapshotApplySystem 增量重建逻辑。
/// </summary>
public class SnapshotDeltaEncodingTests
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

    /// <summary>
    /// 构造 EntityDelta 辅助方法（含 Transform）。
    /// </summary>
    private static EntityDelta MakeDelta(ulong entityId, float x, float y, float z,
        int health = 100, int mana = 50, int level = 1, uint stateBits = 0,
        EntityDeltaKind kind = EntityDeltaKind.Update)
    {
        var delta = new EntityDelta
        {
            EntityId = entityId,
            Kind = kind,
            Transform = new AuthTransformComponent
            {
                X = x, Y = y, Z = z,
                Pitch = 0f, Yaw = 0f, Roll = 0f,
                ServerTick = 0,
            },
            State = new EntityStateAuthComponent
            {
                Health = health,
                MaxHealth = 100,
                StateBits = stateBits,
                Mana = mana,
                MaxMana = 100,
                Level = level,
                Exp = 0,
                Stamina = 100,
                MaxStamina = 100,
            },
        };
        return delta;
    }

    // ===== FakeFanoutObserver：捕获收到的 diff 与 sessionIds =====
    private sealed class FakeFanoutObserver : IZoneShardFanoutObserver
    {
        public List<(WorldChunkDiffPacket Diff, IReadOnlyCollection<long> SessionIds)> ReceivedDiffs { get; } = new();

        public Task OnChunkDiffAsync(WorldChunkDiffPacket diff, IReadOnlyCollection<long> sessionIds)
        {
            ReceivedDiffs.Add((diff, sessionIds));
            return Task.CompletedTask;
        }
    }

    // =======================================================================
    // 测试 1: FullSnapshot_ForcedOnFirstTick
    // =======================================================================
    /// <summary>
    /// 首次 tick 必须强制全量快照（BaselineTick=0），所有已注册实体都应被推送。
    /// </summary>
    [Fact]
    public async Task FullSnapshot_ForcedOnFirstTick()
    {
        var grain = CreateGrain();
        var observer = new FakeFanoutObserver();
        await grain.SubscribeFanoutAsync(Guid.NewGuid(), observer);
        // 订阅 chunk 0（实体位于原点，chunk key=0）
        // 注意：实体位于 (0,0,0)，其 chunk Morton 键由 MortonCodec 编码（含 AxisBias 偏移），
        // 不等于 0。必须订阅正确的 chunk 键才能收到 AOI 过滤后的广播。
        var chunkKey0 = WorldCoord.ToChunkMortonKey(0, 0, 0);
        await grain.SubscribeSessionAsync(sessionId: 1, mortonKeys: new[] { chunkKey0 });

        // 注册 2 个实体
        await grain.RegisterEntityAsync(entityId: 1001, initialX: 0, initialY: 0, initialZ: 0);
        await grain.RegisterEntityAsync(entityId: 1002, initialX: 0, initialY: 0, initialZ: 0);

        // 首次 TickAsync → 应为全量快照，所有实体都推送
        await grain.TickAsync(tickTime: 1.0);

        // 验证：2 个实体都产生 diff（全量快照包含所有实体）
        Assert.True(observer.ReceivedDiffs.Count >= 2,
            $"首次 tick 应至少推送 2 个 diff（全量快照），实际 {observer.ReceivedDiffs.Count}");
    }

    // =======================================================================
    // 测试 2: FullSnapshot_ForcedEvery60Ticks
    // =======================================================================
    /// <summary>
    /// 每 60 tick（1 秒 @ 60Hz）必须强制全量快照。
    /// 验证：tick 0 全量 → tick 1-59 增量（无变化则 0 diff）→ tick 60 全量。
    /// </summary>
    [Fact]
    public async Task FullSnapshot_ForcedEvery60Ticks()
    {
        var grain = CreateGrain();
        var observer = new FakeFanoutObserver();
        await grain.SubscribeFanoutAsync(Guid.NewGuid(), observer);
        // 注意：实体位于 (0,0,0)，其 chunk Morton 键由 MortonCodec 编码（含 AxisBias 偏移），
        // 不等于 0。必须订阅正确的 chunk 键才能收到 AOI 过滤后的广播。
        var chunkKey0 = WorldCoord.ToChunkMortonKey(0, 0, 0);
        await grain.SubscribeSessionAsync(sessionId: 1, mortonKeys: new[] { chunkKey0 });

        await grain.RegisterEntityAsync(entityId: 2001, initialX: 0, initialY: 0, initialZ: 0);

        // tick 0：全量快照 → 1 diff
        await grain.TickAsync(tickTime: 1.0);
        var countAfterFirst = observer.ReceivedDiffs.Count;
        Assert.True(countAfterFirst >= 1, "首次 tick 应推送全量快照");

        // tick 1-59：增量快照，实体无变化 → 0 diff
        for (int i = 0; i < 59; i++)
        {
            await grain.TickAsync(tickTime: 2.0 + i);
        }
        var countBefore60 = observer.ReceivedDiffs.Count;
        Assert.Equal(countAfterFirst, countBefore60);

        // tick 60：强制全量快照 → 1 diff
        await grain.TickAsync(tickTime: 61.0);
        var countAfter60 = observer.ReceivedDiffs.Count;
        Assert.True(countAfter60 > countBefore60,
            $"第 61 次 tick（tickCount=60）应强制全量快照，diff 数应增加。" +
            $"before={countBefore60}, after={countAfter60}");
    }

    // =======================================================================
    // 测试 3: DeltaSnapshot_OnlyContainsChangedEntities
    // =======================================================================
    /// <summary>
    /// 增量快照仅包含位置/属性变化的实体，未变化的实体不推送。
    /// </summary>
    [Fact]
    public async Task DeltaSnapshot_OnlyContainsChangedEntities()
    {
        var grain = CreateGrain();
        var observer = new FakeFanoutObserver();
        await grain.SubscribeFanoutAsync(Guid.NewGuid(), observer);
        // 注意：实体初始 Z=8（chunk cell=16m 的中部），避免重力使 Z 产生微小负偏移
        // 导致 Floor(Z/16) 从 0 变为 -1 跨出订阅 chunk，AOI 过滤后广播被跳过。
        // chunk Morton 键由 MortonCodec 编码（含 AxisBias 偏移），不等于 0。
        const float initialZ = 8f;
        var chunkKey = WorldCoord.ToChunkMortonKey(0, 0, initialZ);
        await grain.SubscribeSessionAsync(sessionId: 1, mortonKeys: new[] { chunkKey });

        // 注册 2 个实体
        await grain.RegisterEntityAsync(entityId: 3001, initialX: 0, initialY: 0, initialZ: initialZ);
        await grain.RegisterEntityAsync(entityId: 3002, initialX: 0, initialY: 0, initialZ: initialZ);

        // tick 0：全量快照 → 2 diffs
        await grain.TickAsync(tickTime: 1.0);
        var countAfterFull = observer.ReceivedDiffs.Count;
        Assert.True(countAfterFull >= 2, "全量快照应包含所有实体");

        // 为实体 3001 提交输入使其移动（位置变化 > 0.01）。
        // InputPacket.MaxSpeed 未填（默认 0），服务端兜底用 DefaultMaxSpeed=6 m/s，
        // reportedEndX=0.1f 与权威回放一致（6 m/s × 1/60s = 0.1m），不触发 correction。
        var input = new InputPacket { ClientTick = 1, MoveX = 1.0f, MoveY = 0 };
        await grain.SubmitInputAsync(entityId: 3001, input,
            reportedEndX: 0.1f, reportedEndY: 0, reportedEndZ: initialZ);

        // tick 1：增量快照 → 仅 1 diff（实体 3001 移动，3002 不变）
        await grain.TickAsync(tickTime: 2.0);
        var deltaDiffCount = observer.ReceivedDiffs.Count - countAfterFull;

        // 增量快照应只包含变化的实体（3001），不包含未变化的实体（3002）
        Assert.True(deltaDiffCount >= 1, $"增量快照应至少包含 1 个变化的实体，实际 {deltaDiffCount}");
        Assert.True(deltaDiffCount <= 1, $"增量快照不应包含未变化的实体，实际 {deltaDiffCount}");
    }

    // =======================================================================
    // 测试 4: DeltaSnapshot_BaselineTickMatchesLastSnapshot
    // =======================================================================
    /// <summary>
    /// 增量快照的 BaselineTick 应等于 baseline 的 ServerTick。
    /// 直接测试 BuildDeltaSnapshot 方法。
    /// </summary>
    [Fact]
    public void DeltaSnapshot_BaselineTickMatchesLastSnapshot()
    {
        var grain = CreateGrain();

        // 构造 baseline 快照（ServerTick=42, BaselineTick=0）
        var baseline = new SnapshotPacket
        {
            ServerTick = 42,
            BaselineTick = 0,
            Deltas = new[]
            {
                MakeDelta(entityId: 4001, x: 0, y: 0, z: 0),
                MakeDelta(entityId: 4002, x: 10, y: 10, z: 10),
            },
        };

        // 构造当前 deltas（实体 4001 位置变化）
        var currentDeltas = new List<EntityDelta>
        {
            MakeDelta(entityId: 4001, x: 5, y: 0, z: 0), // 位置变化 > 0.01
            MakeDelta(entityId: 4002, x: 10, y: 10, z: 10), // 位置不变
        };

        var delta = grain.BuildDeltaSnapshot(baseline, currentDeltas);

        // BaselineTick 应等于 baseline.ServerTick
        Assert.Equal(42, delta.BaselineTick);
        // 仅包含变化的实体
        Assert.Single(delta.Deltas);
        Assert.Equal(4001UL, delta.Deltas[0].EntityId);
    }

    // =======================================================================
    // 测试 5: EntityDeltaChanged_DetectsPositionChange
    // =======================================================================
    /// <summary>
    /// 位置变化超过阈值（>0.01f）应被检测为变化。
    /// </summary>
    [Fact]
    public void EntityDeltaChanged_DetectsPositionChange()
    {
        var baseline = MakeDelta(entityId: 5001, x: 0, y: 0, z: 0);
        var current = MakeDelta(entityId: 5001, x: 0.02f, y: 0, z: 0); // X 变化 0.02 > 0.01

        Assert.True(ZoneShardGrain.EntityDeltaChanged(baseline, current),
            "位置变化 0.02f（>0.01 阈值）应被检测为变化");

        // Y 轴变化
        var currentY = MakeDelta(entityId: 5001, x: 0, y: -0.5f, z: 0);
        Assert.True(ZoneShardGrain.EntityDeltaChanged(baseline, currentY),
            "Y 位置变化 0.5f 应被检测为变化");

        // Z 轴变化
        var currentZ = MakeDelta(entityId: 5001, x: 0, y: 0, z: 100f);
        Assert.True(ZoneShardGrain.EntityDeltaChanged(baseline, currentZ),
            "Z 位置变化 100f 应被检测为变化");

        // 旋转变化
        var baselineRot = MakeDelta(entityId: 5001, x: 0, y: 0, z: 0);
        var currentRot = new EntityDelta
        {
            EntityId = 5001,
            Kind = EntityDeltaKind.Update,
            Transform = new AuthTransformComponent
            {
                X = 0, Y = 0, Z = 0,
                Pitch = 0.5f, Yaw = 0, Roll = 0, // Pitch 变化
                ServerTick = 0,
            },
        };
        Assert.True(ZoneShardGrain.EntityDeltaChanged(baselineRot, currentRot),
            "Pitch 旋转变化 0.5f 应被检测为变化");
    }

    // =======================================================================
    // 测试 6: EntityDeltaChanged_IgnoresTinyPositionChange
    // =======================================================================
    /// <summary>
    /// 位置变化不超过阈值（≤0.01f）应被忽略。
    /// </summary>
    [Fact]
    public void EntityDeltaChanged_IgnoresTinyPositionChange()
    {
        var baseline = MakeDelta(entityId: 6001, x: 0, y: 0, z: 0);
        var current = MakeDelta(entityId: 6001, x: 0.005f, y: 0.01f, z: 0); // 均 ≤ 0.01

        Assert.False(ZoneShardGrain.EntityDeltaChanged(baseline, current),
            "位置变化 ≤0.01f 应被忽略（不触发增量）");

        // 完全相同
        var sameDelta = MakeDelta(entityId: 6001, x: 0, y: 0, z: 0);
        Assert.False(ZoneShardGrain.EntityDeltaChanged(baseline, sameDelta),
            "完全相同的 EntityDelta 不应被检测为变化");

        // 恰好等于阈值 0.01f（不大于阈值 → 不变化）
        var threshold = MakeDelta(entityId: 6001, x: 0.01f, y: 0, z: 0);
        Assert.False(ZoneShardGrain.EntityDeltaChanged(baseline, threshold),
            "位置变化恰好等于 0.01f 阈值应被忽略（严格大于才触发）");
    }

    // =======================================================================
    // 测试 7: EntityDeltaChanged_DetectsAttributeChange
    // =======================================================================
    /// <summary>
    /// 属性变化（Health/Mana/Level/StateBits 等）应被检测为变化。
    /// </summary>
    [Fact]
    public void EntityDeltaChanged_DetectsAttributeChange()
    {
        var baseline = MakeDelta(entityId: 7001, x: 0, y: 0, z: 0, health: 100, mana: 50, level: 1);

        // Health 变化
        var healthChanged = MakeDelta(entityId: 7001, x: 0, y: 0, z: 0, health: 90);
        Assert.True(ZoneShardGrain.EntityDeltaChanged(baseline, healthChanged),
            "Health 变化应被检测");

        // Mana 变化
        var manaChanged = MakeDelta(entityId: 7001, x: 0, y: 0, z: 0, mana: 30);
        Assert.True(ZoneShardGrain.EntityDeltaChanged(baseline, manaChanged),
            "Mana 变化应被检测");

        // Level 变化
        var levelChanged = MakeDelta(entityId: 7001, x: 0, y: 0, z: 0, level: 2);
        Assert.True(ZoneShardGrain.EntityDeltaChanged(baseline, levelChanged),
            "Level 变化应被检测");

        // StateBits 变化
        var stateChanged = MakeDelta(entityId: 7001, x: 0, y: 0, z: 0, stateBits: 0x01);
        Assert.True(ZoneShardGrain.EntityDeltaChanged(baseline, stateChanged),
            "StateBits 变化应被检测");
    }

    // =======================================================================
    // 测试 8: ClientRebuildsFullState_FromDeltaSnapshot
    // =======================================================================
    /// <summary>
    /// 客户端从增量快照重建完整状态：
    /// 1. 设置 _lastAppliedSnapshot（全量快照）
    /// 2. 发送增量快照（仅含变化实体）
    /// 3. 验证重建后的全量快照包含所有实体（baseline + delta 合并）
    /// </summary>
    [Fact]
    public void ClientRebuildsFullState_FromDeltaSnapshot()
    {
        // 清理上次测试的静态状态
        SnapshotApplySystem.ResetLastAppliedSnapshot();

        // 1. 构造全量快照（BaselineTick=0），含 3 个实体
        var fullSnapshot = new SnapshotPacket
        {
            ServerTick = 100,
            BaselineTick = 0,
            Deltas = new[]
            {
                MakeDelta(entityId: 8001, x: 0, y: 0, z: 0, health: 100),
                MakeDelta(entityId: 8002, x: 10, y: 0, z: 0, health: 80),
                MakeDelta(entityId: 8003, x: 20, y: 0, z: 0, health: 60),
            },
        };

        // 模拟网络层应用全量快照后通知 SnapshotApplySystem
        SnapshotApplySystem.OnFullSnapshotApplied(fullSnapshot);

        // 2. 构造增量快照（BaselineTick=100），仅含实体 8001 的变化
        var deltaSnapshot = new SnapshotPacket
        {
            ServerTick = 101,
            BaselineTick = 100, // 指向全量快照的 ServerTick
            Deltas = new[]
            {
                MakeDelta(entityId: 8001, x: 1.5f, y: 0, z: 0, health: 90), // 位置和 HP 变化
            },
        };

        // 3. 尝试重建
        var rebuilt = SnapshotApplySystem.TryRebuildFromDelta(deltaSnapshot);

        // 4. 验证重建结果
        Assert.NotNull(rebuilt);
        Assert.Equal(0, rebuilt!.BaselineTick); // 重建后视为全量
        Assert.Equal(101, rebuilt.ServerTick);

        // 应包含所有 3 个实体（2 个来自 baseline + 1 个来自 delta 覆盖）
        Assert.Equal(3, rebuilt.Deltas.Length);

        // 实体 8001 应使用 delta 中的新值
        var d8001 = rebuilt.Deltas.First(d => d.EntityId == 8001);
        Assert.Equal(1.5f, d8001.Transform!.Value.X);
        Assert.Equal(90, d8001.State!.Value.Health);

        // 实体 8002/8003 应保持 baseline 值
        var d8002 = rebuilt.Deltas.First(d => d.EntityId == 8002);
        Assert.Equal(10f, d8002.Transform!.Value.X);
        Assert.Equal(80, d8002.State!.Value.Health);

        var d8003 = rebuilt.Deltas.First(d => d.EntityId == 8003);
        Assert.Equal(20f, d8003.Transform!.Value.X);
        Assert.Equal(60, d8003.State!.Value.Health);

        // 清理
        SnapshotApplySystem.ResetLastAppliedSnapshot();
    }

    // =======================================================================
    // 补充测试 9: ClientRebuildsFullState_RejectsMismatchedBaseline
    // =======================================================================
    /// <summary>
    /// 增量快照 BaselineTick 与 _lastAppliedSnapshot.ServerTick 不匹配时，
    /// 应返回 null（触发全量重传请求）。
    /// </summary>
    [Fact]
    public void ClientRebuildsFullState_RejectsMismatchedBaseline()
    {
        SnapshotApplySystem.ResetLastAppliedSnapshot();

        var fullSnapshot = new SnapshotPacket
        {
            ServerTick = 100,
            BaselineTick = 0,
            Deltas = new[] { MakeDelta(entityId: 9001, x: 0, y: 0, z: 0) },
        };
        SnapshotApplySystem.OnFullSnapshotApplied(fullSnapshot);

        // BaselineTick=200（不匹配 100）→ 应返回 null
        var mismatchedDelta = new SnapshotPacket
        {
            ServerTick = 201,
            BaselineTick = 200, // 不匹配
            Deltas = new[] { MakeDelta(entityId: 9001, x: 5, y: 0, z: 0) },
        };

        var result = SnapshotApplySystem.TryRebuildFromDelta(mismatchedDelta);
        Assert.Null(result);

        SnapshotApplySystem.ResetLastAppliedSnapshot();
    }

    // =======================================================================
    // 补充测试 10: ClientRebuildsFullState_NullBaselineRejectsDelta
    // =======================================================================
    /// <summary>
    /// _lastAppliedSnapshot 为 null 时（未收到过全量快照），
    /// 增量快照应被拒绝（返回 null）。
    /// </summary>
    [Fact]
    public void ClientRebuildsFullState_NullBaselineRejectsDelta()
    {
        SnapshotApplySystem.ResetLastAppliedSnapshot();

        var delta = new SnapshotPacket
        {
            ServerTick = 50,
            BaselineTick = 49, // 非 0 → 增量快照
            Deltas = new[] { MakeDelta(entityId: 10001, x: 1, y: 0, z: 0) },
        };

        var result = SnapshotApplySystem.TryRebuildFromDelta(delta);
        Assert.Null(result);

        SnapshotApplySystem.ResetLastAppliedSnapshot();
    }
}
