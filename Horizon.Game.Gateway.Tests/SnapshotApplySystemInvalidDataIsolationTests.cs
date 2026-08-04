using System;
using System.Collections.Generic;
using Arch.Core;
using Horizon.Game.ECS.Arch.Components;
using Horizon.Game.ECS.Arch.Diagnostics;
using Horizon.Game.ECS.Arch.Network;
using Horizon.Game.ECS.Arch.Systems;
using Horizon.Game.Message.Sync;
using Horizon.Game.Message.Sync.Components;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 任务 7.4 — SnapshotApplySystem 异常数据隔离单元测试。
/// 验证 HandleUpdate 写入 Target 前对 NaN/Infinity 位置数据的有限值校验：
/// 非法数据跳过该实体 Target 写入、输出诊断事件与计数，其余角色完全正常、进程不崩溃
/// （spec 5.3.1 规则 7 的 a、DFX 4.2.4）。
/// </summary>
public class SnapshotApplySystemInvalidDataIsolationTests : IDisposable
{
    private readonly World _world;
    private readonly SnapshotApplySystem _system;

    private sealed class TestSink : ISyncDiagnosticsSink
    {
        public List<(ulong EntityId, long ServerTick)> InvalidSkipped = new();

        public void OnTeleportJump(ulong entityId, float distance, long serverTick) { }
        public void OnCorrectionStormTriggered(ulong entityId, int recentCount, float windowSeconds) { }
        public void OnStaleCorrectionSkipped(ulong entityId, long lastProcessedTick, long lastAckedTick) { }
        public void OnAdaptiveWindowAdjusted(float oldDelaySeconds, float newDelaySeconds, float rttSeconds, float jitterSeconds) { }
        public void OnBaselineResyncRequested(long expectedBaselineTick, long receivedBaselineTick) { }
        public void OnConfigInvalid(string fieldName, float configuredValue, float fallbackValue, bool isWarningOnly) { }
        public void OnInvalidSnapshotSkipped(ulong entityId, long serverTick) => InvalidSkipped.Add((entityId, serverTick));
        public void OnMultiEntityDegraded(int remoteEntityCount, string reason) { }
        public void OnBandwidthThrottled(long sessionId, double kbps, int fromHz, int toHz) { }
        public void OnBandwidthRecovered(long sessionId, double kbps, int fromHz, int toHz) { }
        public void OnScaleTierChanged(int entityCount, SyncScaleTier from, SyncScaleTier to) { }
        public void OnScaleDegrade(ulong entityId, float distanceMeters, string reason) { }
    }

    public SnapshotApplySystemInvalidDataIsolationTests()
    {
        ResetState();
        _world = World.Create();
        _system = new SnapshotApplySystem();
        SnapshotApplySystem.ResetLastAppliedSnapshot();
        SnapshotApplySystem.ResetAdaptiveDelayStats();
        SnapshotApplySystem.UseAdaptiveDelay = true;
        SnapshotApplySystem.Diagnostics = new TestSink();
    }

    public void Dispose()
    {
        World.Destroy(_world);
        ResetState();
    }

    private static void ResetState()
    {
        SnapshotApplySystem.ResetAdaptiveDelayStats();
        SnapshotApplySystem.Diagnostics = null;
    }

    /// <summary>清空快照接收缓冲（避免测试间串扰）。</summary>
    private void DrainQueue()
    {
        while (SnapshotReceiveBuffer.Instance.TryDequeue(out _)) { }
        SnapshotReceiveBuffer.Instance.ClearQueue();
    }

    private static AuthTransformComponent MakeTransform(float x, float y, float z)
    {
        return new AuthTransformComponent { X = x, Y = y, Z = z, Pitch = 0f, Yaw = 0f, Roll = 0f, ServerTick = 0 };
    }

    private static EntityDelta MakeSpawnDelta(ulong entityId, AuthTransformComponent transform)
    {
        return new EntityDelta
        {
            EntityId = entityId,
            Kind = EntityDeltaKind.Spawn,
            Identity = new NetworkIdentityAuthComponent
            {
                NetworkId = entityId,
                EntityType = 1,
                OwnerId = entityId, // 非本地玩家 OwnerId
            },
            Transform = transform,
        };
    }

    private static EntityDelta MakeUpdateDelta(ulong entityId, AuthTransformComponent transform)
    {
        return new EntityDelta
        {
            EntityId = entityId,
            Kind = EntityDeltaKind.Update,
            Identity = null,
            Transform = transform,
        };
    }

    [Fact]
    public void InvalidTransform_NanInUpdate_IsSkipped_OthersAffected()
    {
        DrainQueue();
        // 本地玩家 OwnerId 设为 0（无本地玩家），所有 Spawn 均按远程实体处理
        _system.LocalPlayerOwnerId = 0;

        // Spawn 两个远程实体（位置合法）
        var goodId = 100ul;
        var badId = 200ul;
        var spawnGood = new SnapshotPacket
        {
            ServerTick = 1,
            BaselineTick = 0,
            Deltas = new[] { MakeSpawnDelta(goodId, MakeTransform(10f, 0f, 10f)) },
        };
        var spawnBad = new SnapshotPacket
        {
            ServerTick = 1,
            BaselineTick = 0,
            Deltas = new[] { MakeSpawnDelta(badId, MakeTransform(20f, 0f, 20f)) },
        };
        SnapshotReceiveBuffer.Instance.Enqueue(spawnGood);
        SnapshotReceiveBuffer.Instance.Enqueue(spawnBad);
        _system.Update(_world, TimeSpan.FromSeconds(1.0 / 60.0));

        // 验证两个实体已 Spawn 且 Target 合法
        var goodEntity = FindEntityByNetId(goodId);
        var badEntity = FindEntityByNetId(badId);
        Assert.True(goodEntity.HasValue, "好实体应已 Spawn");
        Assert.True(badEntity.HasValue, "坏实体应已 Spawn");

        ref var goodInterp = ref _world.Get<InterpolatedTransformComponent>(goodEntity.Value);
        Assert.Equal(10f, goodInterp.TargetX);

        // 向坏实体下发 NaN 位置 Update
        var badUpdate = new SnapshotPacket
        {
            ServerTick = 2,
            BaselineTick = 0,
            Deltas = new[] { MakeUpdateDelta(badId, MakeTransform(float.NaN, 0f, 0f)) },
        };
        SnapshotReceiveBuffer.Instance.Enqueue(badUpdate);
        _system.Update(_world, TimeSpan.FromSeconds(1.0 / 60.0));

        // 坏实体：Target 保持最后合法位置（20m），不被 NaN 污染
        ref var badAfter = ref _world.Get<InterpolatedTransformComponent>(badEntity.Value);
        Assert.True(float.IsFinite(badAfter.TargetX), $"坏实体 Target 必须保持有限值，实际 {badAfter.TargetX}");
        Assert.Equal(20f, badAfter.TargetX);

        // 好实体：Target 不受影响，仍为 10m
        ref var goodAfter = ref _world.Get<InterpolatedTransformComponent>(goodEntity.Value);
        Assert.Equal(10f, goodAfter.TargetX);

        // 诊断事件与计数应记录（2 个快照消费，其中 1 个含非法数据）
        Assert.True(_system.InvalidSnapshotsSkipped >= 1, "应记录非法快照跳过计数");
        Assert.True(((TestSink)SnapshotApplySystem.Diagnostics!).InvalidSkipped.Count >= 1, "应输出 OnInvalidSnapshotSkipped 诊断");
    }

    [Fact]
    public void InvalidTransform_InfinityInUpdate_DoesNotCrash()
    {
        DrainQueue();
        _system.LocalPlayerOwnerId = 0;

        var badId = 300ul;
        var spawn = new SnapshotPacket
        {
            ServerTick = 1,
            BaselineTick = 0,
            Deltas = new[] { MakeSpawnDelta(badId, MakeTransform(5f, 0f, 5f)) },
        };
        SnapshotReceiveBuffer.Instance.Enqueue(spawn);
        _system.Update(_world, TimeSpan.FromSeconds(1.0 / 60.0));

        // 下发 Infinity 位置 Update：进程不崩溃、实体保持最后合法位置
        var badUpdate = new SnapshotPacket
        {
            ServerTick = 2,
            BaselineTick = 0,
            Deltas = new[] { MakeUpdateDelta(badId, MakeTransform(float.PositiveInfinity, 0f, 0f)) },
        };
        SnapshotReceiveBuffer.Instance.Enqueue(badUpdate);
        _system.Update(_world, TimeSpan.FromSeconds(1.0 / 60.0));

        var badEntity = FindEntityByNetId(badId);
        Assert.True(badEntity.HasValue, "实体应保持存活（不因非法数据销毁）");
        ref var interp = ref _world.Get<InterpolatedTransformComponent>(badEntity.Value);
        Assert.True(float.IsFinite(interp.TargetX), $"Target 必须为有限值，实际 {interp.TargetX}");
        Assert.Equal(5f, interp.TargetX);
    }

    /// <summary>按 NetworkIdentityComponent.EntityId 查找实体。</summary>
    private Entity? FindEntityByNetId(ulong netId)
    {
        Entity? result = null;
        var query = new QueryDescription().WithAll<NetworkIdentityComponent>();
        _world.Query(in query, (Entity e, ref NetworkIdentityComponent nid) =>
        {
            if (nid.EntityId == netId) result = e;
        });
        return result;
    }
}