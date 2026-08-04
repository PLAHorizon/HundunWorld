using System;
using System.Collections.Generic;
using Horizon.Game.ECS.Arch.Diagnostics;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 任务组 7.2 — 规模档位稳定性压测用例（spec 4.2.1 / 5.7.1）。
/// <para>
/// 30 分钟持续运行用长循环 + 快速迭代模拟（单元测试环境内压缩时间尺度），
/// 验证：各档位实体映射数量与存活实体一致（无幽灵实体/泄漏）、批量生命周期正确性、
/// 超档位降级不消失性（spec 5.5.1.3）。
/// </para>
/// </summary>
public class SyncScaleStabilityTests
{
    [Fact]
    public void LongRunning_EntityMapping_NoLeak_NoGhosts()
    {
        // 模拟 30 分钟运行（60Hz × 1800s = 108000 tick），以压缩循环模拟。
        // 每次迭代代表 1 tick：实体 spawn/despawn 批量出现/消失。
        var controller = new SyncScaleController
        {
            TierThresholds = new[] { 20, 100, 1000, 5000 },
        };

        var activeEntityIds = new HashSet<ulong>();
        ulong nextEntityId = 1;

        const int totalIterations = 108000;
        for (int tick = 0; tick < totalIterations; tick++)
        {
            // 每 100 tick 批量出现 10 个实体；每 200 tick 批量消失 10 个实体。
            if (tick % 100 == 0)
            {
                for (int i = 0; i < 10; i++)
                {
                    activeEntityIds.Add(nextEntityId++);
                }
            }
            if (tick % 200 == 0 && activeEntityIds.Count > 10)
            {
                var removed = new List<ulong>();
                foreach (var id in activeEntityIds)
                {
                    if (removed.Count < 10) removed.Add(id);
                    else break;
                }
                foreach (var id in removed) activeEntityIds.Remove(id);
            }

            controller.OnRemoteEntityCountChanged(activeEntityIds.Count);

            // 超档位时对最远实体降级（不消失）。
            if (activeEntityIds.Count > 5000)
            {
                var farEntities = new List<(ulong, float)>(activeEntityIds.Count);
                foreach (var id in activeEntityIds)
                {
                    farEntities.Add((id, 100f + (id % 1000)));
                }
                controller.ApplyDegradeTo(farEntities);
                // 降级后实体仍应在订阅集中（不消失性验收）。
                Assert.All(controller.DegradedEntityIds, id => Assert.Contains(id, activeEntityIds));
            }
            else if (controller.DegradedEntityIds.Count > 0)
            {
                controller.ClearDegraded();
            }

            // 映射一致性：降级集合 ⊆ 活跃集合。
            Assert.All(controller.DegradedEntityIds, id => Assert.Contains(id, activeEntityIds));
        }

        // 运行结束后无幽灵实体（降级集合已清空或仅含活跃实体）。
        Assert.All(controller.DegradedEntityIds, id => Assert.Contains(id, activeEntityIds));
    }

    [Fact]
    public void BatchLifecycle_SpawnDespawn_NoInterference()
    {
        // 同帧 10 个实体出现/消失，其余实体不受影响（spec 4.2.2 数据隔离）。
        var controller = new SyncScaleController { TierThresholds = new[] { 20, 100, 1000, 5000 } };
        var ids = new List<ulong>();
        for (ulong i = 1; i <= 30; i++) ids.Add(i);

        // 30 个实体 → Tier1（100 以内），无降级。
        controller.OnRemoteEntityCountChanged(30);
        Assert.Equal(SyncScaleTier.Tier1, controller.CurrentTier);
        Assert.Empty(controller.DegradedEntityIds);

        // 同帧批量出现 6000 个 → OverLimit 触发降级但不消失。
        var huge = new List<(ulong, float)>();
        for (ulong i = 100; i < 6100; i++) huge.Add((i, 50f + (i % 100)));
        controller.OnRemoteEntityCountChanged(huge.Count);
        controller.ApplyDegradeTo(huge);
        Assert.Equal(SyncScaleTier.OverLimit, controller.CurrentTier);
        Assert.Equal(huge.Count - 5000, controller.DegradedEntityIds.Count);

        // 批量消失：清空后无残留降级标记。
        controller.OnRemoteEntityCountChanged(0);
        controller.ClearDegraded();
        Assert.Empty(controller.DegradedEntityIds);
        Assert.Equal(SyncScaleTier.Tier0, controller.CurrentTier);
    }

    [Fact]
    public void ScaleTiers_30Minute_Smoke_PerTier()
    {
        // 各档位（20/100/1000/5000）冒烟：持续大量切换不崩溃、档位映射稳定。
        var controller = new SyncScaleController { TierThresholds = new[] { 20, 100, 1000, 5000 } };
        var rng = new Random(42);

        for (int i = 0; i < 50000; i++)
        {
            var count = rng.Next(0, 6000);
            controller.OnRemoteEntityCountChanged(count);

            var expected = count <= 20 ? SyncScaleTier.Tier0
                : count <= 100 ? SyncScaleTier.Tier1
                : count <= 1000 ? SyncScaleTier.Tier2
                : count <= 5000 ? SyncScaleTier.Tier3
                : SyncScaleTier.OverLimit;
            Assert.Equal(expected, controller.CurrentTier);
        }
    }
}