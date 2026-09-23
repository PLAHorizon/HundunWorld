using Horizon.Game.ECS.Arch.Configuration;
using Horizon.Game.ECS.Arch.Systems;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 阶段 5.3 — 阈值配置注入（<see cref="RemoteSyncThresholdOptions"/> → 两系统）接线测试：
/// <see cref="SnapshotApplySystem.ApplyOptions"/> 与 <see cref="InterpolationSystem.ApplyOptions"/>
/// 把校验后的配置写入系统属性；非法值经 <see cref="RemoteSyncThresholdValidator"/> 回退默认。
/// <para>
/// 注意：<see cref="SnapshotApplySystem"/> 的阈值是静态属性，且与其他测试类共享
/// （<c>AdaptiveInterpolationTests</c>/<c>SnapshotApplySystemNetworkQualityHysteresisTests</c> 等并行运行）。
/// 本类所有测试在 finally 中还原被改写的静态值，避免并行污染。
/// </para>
/// </summary>
public class RemoteSyncApplyOptionsTests
{
    // SnapshotApplySystem 中 ApplyOptions 会改写的全部静态属性快照/还原。
    private static (float Min, float Max, float Fixed, float Step, float RttA, float RttJ,
        float ArrA, float ArrJ, float StopD, float StopV, int Orphan, int Stale) Snapshot()
        => (SnapshotApplySystem.AdaptiveDelayMinSeconds,
            SnapshotApplySystem.AdaptiveDelayMaxSeconds,
            SnapshotApplySystem.FixedInterpolationDelaySeconds,
            SnapshotApplySystem.MaxDelayChangePerSecond,
            SnapshotApplySystem.RttEwmaAlpha,
            SnapshotApplySystem.RttJitterBeta,
            SnapshotApplySystem.SnapshotArrivalEwmaAlpha,
            SnapshotApplySystem.SnapshotArrivalJitterBeta,
            SnapshotApplySystem.StopDisplacementThreshold,
            SnapshotApplySystem.StopVelocityThreshold,
            SnapshotApplySystem.OrphanCleanupInterval,
            SnapshotApplySystem.StaleEntityCleanupInterval);

    private static void Restore((float Min, float Max, float Fixed, float Step, float RttA, float RttJ,
        float ArrA, float ArrJ, float StopD, float StopV, int Orphan, int Stale) s)
    {
        SnapshotApplySystem.AdaptiveDelayMinSeconds = s.Min;
        SnapshotApplySystem.AdaptiveDelayMaxSeconds = s.Max;
        SnapshotApplySystem.FixedInterpolationDelaySeconds = s.Fixed;
        SnapshotApplySystem.MaxDelayChangePerSecond = s.Step;
        SnapshotApplySystem.RttEwmaAlpha = s.RttA;
        SnapshotApplySystem.RttJitterBeta = s.RttJ;
        SnapshotApplySystem.SnapshotArrivalEwmaAlpha = s.ArrA;
        SnapshotApplySystem.SnapshotArrivalJitterBeta = s.ArrJ;
        SnapshotApplySystem.StopDisplacementThreshold = s.StopD;
        SnapshotApplySystem.StopVelocityThreshold = s.StopV;
        SnapshotApplySystem.OrphanCleanupInterval = s.Orphan;
        SnapshotApplySystem.StaleEntityCleanupInterval = s.Stale;
    }

    [Fact]
    public void SnapshotApplySystem_ApplyOptions_WiresAllFields()
    {
        var saved = Snapshot();
        try
        {
            var system = new SnapshotApplySystem();
            var options = new RemoteSyncThresholdOptions
            {
                MaxSnapshotsPerFrame = 64,
                StaleEntityTimeoutSeconds = 120f,
                AdaptiveDelayMinSeconds = 0.08f,
                AdaptiveDelayMaxSeconds = 0.25f,
                FixedInterpolationDelaySeconds = 0.15f,
                MaxDelayChangePerSecond = 0.5f,
                RttEwmaAlpha = 0.2f,
                RttJitterBeta = 0.3f,
                SnapshotArrivalEwmaAlpha = 0.15f,
                SnapshotArrivalJitterBeta = 0.2f,
                StopDisplacementThreshold = 0.1f,
                StopVelocityThreshold = 0.8f,
                OrphanCleanupInterval = 600,
                StaleEntityCleanupInterval = 30,
            };

            system.ApplyOptions(options);

            Assert.Equal(64, system.MaxSnapshotsPerFrame);
            Assert.Equal(120f, system.StaleEntityTimeoutSeconds);
            Assert.Equal(0.08f, SnapshotApplySystem.AdaptiveDelayMinSeconds);
            Assert.Equal(0.25f, SnapshotApplySystem.AdaptiveDelayMaxSeconds);
            Assert.Equal(0.15f, SnapshotApplySystem.FixedInterpolationDelaySeconds);
            Assert.Equal(0.5f, SnapshotApplySystem.MaxDelayChangePerSecond);
            Assert.Equal(0.2f, SnapshotApplySystem.RttEwmaAlpha);
            Assert.Equal(0.3f, SnapshotApplySystem.RttJitterBeta);
            Assert.Equal(0.15f, SnapshotApplySystem.SnapshotArrivalEwmaAlpha);
            Assert.Equal(0.2f, SnapshotApplySystem.SnapshotArrivalJitterBeta);
            Assert.Equal(0.1f, SnapshotApplySystem.StopDisplacementThreshold);
            Assert.Equal(0.8f, SnapshotApplySystem.StopVelocityThreshold);
            Assert.Equal(600, SnapshotApplySystem.OrphanCleanupInterval);
            Assert.Equal(30, SnapshotApplySystem.StaleEntityCleanupInterval);
        }
        finally
        {
            Restore(saved);
        }
    }

    [Fact]
    public void SnapshotApplySystem_ApplyOptions_InvalidValuesFallBackToDefaults()
    {
        var saved = Snapshot();
        try
        {
            var system = new SnapshotApplySystem();
            var options = new RemoteSyncThresholdOptions
            {
                MaxSnapshotsPerFrame = 0,
                StaleEntityTimeoutSeconds = -1f,
                AdaptiveDelayMinSeconds = 0.3f,
                AdaptiveDelayMaxSeconds = 0.1f, // 上限 < 下限：必须修正（否则 Math.Clamp 抛异常）
                RttEwmaAlpha = 2f,
                StopDisplacementThreshold = 0f,
            };

            system.ApplyOptions(options);

            Assert.Equal(32, system.MaxSnapshotsPerFrame);
            Assert.Equal(90f, system.StaleEntityTimeoutSeconds);
            Assert.Equal(0.3f, SnapshotApplySystem.AdaptiveDelayMinSeconds);
            Assert.True(SnapshotApplySystem.AdaptiveDelayMaxSeconds >= SnapshotApplySystem.AdaptiveDelayMinSeconds);
            Assert.Equal(0.125f, SnapshotApplySystem.RttEwmaAlpha);
            Assert.Equal(0.05f, SnapshotApplySystem.StopDisplacementThreshold);
        }
        finally
        {
            Restore(saved);
        }
    }

    [Fact]
    public void SnapshotApplySystem_ApplyOptions_NullOptions_UsesDefaults()
    {
        var saved = Snapshot();
        try
        {
            var system = new SnapshotApplySystem();

            system.ApplyOptions(null);

            Assert.Equal(32, system.MaxSnapshotsPerFrame);
            Assert.Equal(90f, system.StaleEntityTimeoutSeconds);
            Assert.Equal(0.125f, SnapshotApplySystem.RttEwmaAlpha);
            Assert.Equal(0.2f, SnapshotApplySystem.SnapshotArrivalEwmaAlpha);
            Assert.Equal(300, SnapshotApplySystem.OrphanCleanupInterval);
        }
        finally
        {
            Restore(saved);
        }
    }

    [Fact]
    public void InterpolationSystem_ApplyOptions_WiresThresholds()
    {
        var system = new InterpolationSystem();
        var options = new RemoteSyncThresholdOptions
        {
            SmoothThresholdMeters = 250f,
            HardSnapThresholdMeters = 600f,
            BlendDurationSeconds = 0.3f,
            DeadReckoningMaxExtrapolationSeconds = 0.4f,
        };

        system.ApplyOptions(options);

        Assert.Equal(250f, system.TeleportThresholdMeters);
        Assert.Equal(600f, system.HardSnapThresholdMeters);
        Assert.Equal(0.3f, system.TeleportBlendDurationSeconds);
        Assert.Equal(0.4f, system.DeadReckoningMaxExtrapolationSeconds);
    }

    [Fact]
    public void InterpolationSystem_ApplyOptions_InvalidValuesFallBack()
    {
        var system = new InterpolationSystem();
        var options = new RemoteSyncThresholdOptions
        {
            SmoothThresholdMeters = 0f,                       // → 200
            HardSnapThresholdMeters = 50f,                    // 50 <= Smooth(200) → 500
            BlendDurationSeconds = 0f,                        // → 0.2
            DeadReckoningMaxExtrapolationSeconds = -1f,       // → 0.25
        };

        system.ApplyOptions(options);

        Assert.Equal(200f, system.TeleportThresholdMeters);
        Assert.Equal(500f, system.HardSnapThresholdMeters);
        Assert.Equal(0.2f, system.TeleportBlendDurationSeconds);
        Assert.Equal(0.25f, system.DeadReckoningMaxExtrapolationSeconds);
    }
}
