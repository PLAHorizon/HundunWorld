using System.Collections.Generic;
using Horizon.Game.ECS.Arch.Diagnostics;

using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 任务组 4.2 — SyncScaleController 客户端规模档位控制器单元测试。
/// 验证档位状态机（20/100/1000/5000）、最远优先降级与档位切换诊断事件（spec 5.5.1.3）。
/// </summary>
public class SyncScaleControllerTests
{
    private sealed class TestSink : ISyncDiagnosticsSink
    {
        public List<(int EntityCount, SyncScaleTier From, SyncScaleTier To)> TierChanges = new();
        public List<(ulong EntityId, float Distance, string Reason)> Degrades = new();

        public void OnTeleportJump(ulong entityId, float distance, long serverTick) { }
        public void OnCorrectionStormTriggered(ulong entityId, int recentCount, float windowSeconds) { }
        public void OnStaleCorrectionSkipped(ulong entityId, long lastProcessedTick, long lastAckedTick) { }
        public void OnAdaptiveWindowAdjusted(float oldDelaySeconds, float newDelaySeconds, float rttSeconds, float jitterSeconds) { }
        public void OnBaselineResyncRequested(long expectedBaselineTick, long receivedBaselineTick) { }
        public void OnConfigInvalid(string fieldName, float configuredValue, float fallbackValue, bool isWarningOnly) { }
        public void OnInvalidSnapshotSkipped(ulong entityId, long serverTick) { }
        public void OnMultiEntityDegraded(int remoteEntityCount, string reason) { }
        public void OnBandwidthThrottled(long sessionId, double kbps, int fromHz, int toHz) { }
        public void OnBandwidthRecovered(long sessionId, double kbps, int fromHz, int toHz) { }
        public void OnScaleTierChanged(int entityCount, SyncScaleTier from, SyncScaleTier to)
            => TierChanges.Add((entityCount, from, to));
        public void OnScaleDegrade(ulong entityId, float distanceMeters, string reason)
            => Degrades.Add((entityId, distanceMeters, reason));
    }

    private static SyncScaleController CreateController(TestSink? sink = null)
    {
        return new SyncScaleController
        {
            TierThresholds = new[] { 20, 100, 1000, 5000 },
            Diagnostics = sink,
        };
    }

    // ── 档位状态机验收 ─────────────────────────────────────────────────

    [Fact]
    public void OnRemoteEntityCountChanged_TierMapping_IsCorrect()
    {
        var controller = CreateController();

        controller.OnRemoteEntityCountChanged(10);
        Assert.Equal(SyncScaleTier.Tier0, controller.CurrentTier);

        controller.OnRemoteEntityCountChanged(50);
        Assert.Equal(SyncScaleTier.Tier1, controller.CurrentTier);

        controller.OnRemoteEntityCountChanged(500);
        Assert.Equal(SyncScaleTier.Tier2, controller.CurrentTier);

        controller.OnRemoteEntityCountChanged(3000);
        Assert.Equal(SyncScaleTier.Tier3, controller.CurrentTier);

        controller.OnRemoteEntityCountChanged(6000);
        Assert.Equal(SyncScaleTier.OverLimit, controller.CurrentTier);

        // 回落：6000 → 4000 → Tier3。
        controller.OnRemoteEntityCountChanged(4000);
        Assert.Equal(SyncScaleTier.Tier3, controller.CurrentTier);
    }

    [Fact]
    public void OnRemoteEntityCountChanged_EachSwitchFiresTierChangedAndDiagnostic()
    {
        var sink = new TestSink();
        var controller = CreateController(sink);
        var tierChangedEvents = new List<(SyncScaleTier From, SyncScaleTier To)>();
        controller.TierChanged += (from, to) => tierChangedEvents.Add((from, to));

        controller.OnRemoteEntityCountChanged(10);   // Tier0（初始即 Tier0，无切换）
        controller.OnRemoteEntityCountChanged(50);   // Tier0→Tier1
        controller.OnRemoteEntityCountChanged(500);  // Tier1→Tier2
        controller.OnRemoteEntityCountChanged(3000); // Tier2→Tier3
        controller.OnRemoteEntityCountChanged(6000); // Tier3→OverLimit
        controller.OnRemoteEntityCountChanged(4000); // OverLimit→Tier3

        Assert.Equal(5, tierChangedEvents.Count);
        Assert.Equal((SyncScaleTier.Tier0, SyncScaleTier.Tier1), tierChangedEvents[0]);
        Assert.Equal((SyncScaleTier.Tier3, SyncScaleTier.OverLimit), tierChangedEvents[3]);
        Assert.Equal((SyncScaleTier.OverLimit, SyncScaleTier.Tier3), tierChangedEvents[4]);

        Assert.Equal(5, sink.TierChanges.Count);
        Assert.Equal((50, SyncScaleTier.Tier0, SyncScaleTier.Tier1), sink.TierChanges[0]);
        Assert.Equal((6000, SyncScaleTier.Tier3, SyncScaleTier.OverLimit), sink.TierChanges[3]);
    }

    // ── 最远优先降级 ───────────────────────────────────────────────────

    [Fact]
    public void ApplyDegradeTo_OverLimit_DegradesFarthestEntities()
    {
        var sink = new TestSink();
        var controller = CreateController(sink);
        controller.OnRemoteEntityCountChanged(6000); // OverLimit，cap = 5000

        // 5500 个实体，cap=5000 → 降级 500 个最远实体。
        var farEntities = new List<(ulong EntityId, float Distance)>();
        for (int i = 0; i < 5500; i++)
        {
            farEntities.Add(((ulong)i, 100f + i)); // 距离递增，i 越大越远
        }
        controller.ApplyDegradeTo(farEntities);

        Assert.Equal(500, controller.DegradedEntityIds.Count);
        // 最远的 500 个（id 5000..5499）被降级。
        Assert.Contains((ulong)5499, controller.DegradedEntityIds);
        Assert.Contains((ulong)5000, controller.DegradedEntityIds);
        Assert.DoesNotContain((ulong)4999, controller.DegradedEntityIds);
        Assert.Equal(500, sink.Degrades.Count);
    }

    [Fact]
    public void ApplyDegradeTo_UnderCap_NoDegrade()
    {
        var controller = CreateController();
        controller.OnRemoteEntityCountChanged(50); // Tier1，cap=100

        var farEntities = new List<(ulong EntityId, float Distance)>();
        for (int i = 0; i < 80; i++)
        {
            farEntities.Add(((ulong)i, 1000f + i));
        }
        controller.ApplyDegradeTo(farEntities);

        Assert.Empty(controller.DegradedEntityIds);
    }

    [Fact]
    public void Restore_RemovesDegradedEntities()
    {
        var controller = CreateController();
        controller.OnRemoteEntityCountChanged(6000); // OverLimit
        var farEntities = new List<(ulong EntityId, float Distance)>();
        for (int i = 0; i < 5500; i++)
        {
            farEntities.Add(((ulong)i, 100f + i));
        }
        controller.ApplyDegradeTo(farEntities);
        Assert.Equal(500, controller.DegradedEntityIds.Count);

        controller.Restore(new[] { (ulong)5499, (ulong)5498 });
        Assert.Equal(498, controller.DegradedEntityIds.Count);

        controller.ClearDegraded();
        Assert.Empty(controller.DegradedEntityIds);
    }

    [Fact]
    public void InvalidTierThresholds_AreRejected()
    {
        var controller = CreateController();
        controller.TierThresholds = new[] { 5000, 100, 20 }; // 非递增：应被拒绝
        Assert.Equal(new[] { 20, 100, 1000, 5000 }, controller.TierThresholds);

        controller.TierThresholds = new[] { 10, -5, 30 }; // 含非正：应被拒绝
        Assert.Equal(new[] { 20, 100, 1000, 5000 }, controller.TierThresholds);
    }
}