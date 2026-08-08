using System.Collections.Generic;
using Horizon.Game.ECS.Arch.Configuration;
using Horizon.Game.ECS.Arch.Diagnostics;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 任务 7.2 — 远程同步阈值配置校验器单元测试。
/// 验证非法配置回退默认、混合时长过短仅警告不回退、null 配置返回默认配置、合法配置原样返回
/// （spec 5.1.3 异常场景 3、5.2.1 规则 7）。
/// </summary>
public class RemoteSyncThresholdValidatorTests
{
    private sealed class TestSink : ISyncDiagnosticsSink
    {
        public List<(string Field, float Configured, float Fallback, bool WarningOnly)> ConfigInvalid = new();

        public void OnTeleportJump(ulong entityId, float distance, long serverTick) { }
        public void OnCorrectionStormTriggered(ulong entityId, int recentCount, float windowSeconds) { }
        public void OnStaleCorrectionSkipped(ulong entityId, long lastProcessedTick, long lastAckedTick) { }
        public void OnAdaptiveWindowAdjusted(float oldDelaySeconds, float newDelaySeconds, float rttSeconds, float jitterSeconds) { }
        public void OnBaselineResyncRequested(long expectedBaselineTick, long receivedBaselineTick) { }

        public void OnConfigInvalid(string fieldName, float configuredValue, float fallbackValue, bool isWarningOnly)
        {
            ConfigInvalid.Add((fieldName, configuredValue, fallbackValue, isWarningOnly));
        }

        public void OnInvalidSnapshotSkipped(ulong entityId, long serverTick) { }
        public void OnMultiEntityDegraded(int remoteEntityCount, string reason) { }
        public void OnBandwidthThrottled(long sessionId, double kbps, int fromHz, int toHz) { }
        public void OnBandwidthRecovered(long sessionId, double kbps, int fromHz, int toHz) { }
        public void OnScaleTierChanged(int entityCount, SyncScaleTier from, SyncScaleTier to) { }
        public void OnScaleDegrade(ulong entityId, float distanceMeters, string reason) { }
    }

    [Fact]
    public void Validate_InvalidSmoothThreshold_FallsBackTo100()
    {
        var sink = new TestSink();
        var opt = new RemoteSyncThresholdOptions { SmoothThresholdMeters = 0f };

        var result = RemoteSyncThresholdValidator.Validate(opt, sink);

        // 默认平滑区阈值已随实测日志修复提至 200m（100m 级跳变走平滑 Lerp 而非加速冲刺）。
        Assert.Equal(200f, result.SmoothThresholdMeters);
        Assert.Contains(sink.ConfigInvalid, e => e.Field == "SmoothThresholdMeters" && !e.WarningOnly);
    }

    [Fact]
    public void Validate_SmoothGreaterThanHardSnap_FallsBackSmoothTo200()
    {
        var sink = new TestSink();
        // 平滑区 800 > 硬跳 500：非法，回退平滑区为 200
        var opt = new RemoteSyncThresholdOptions
        {
            SmoothThresholdMeters = 800f,
            HardSnapThresholdMeters = 500f,
        };

        var result = RemoteSyncThresholdValidator.Validate(opt, sink);

        Assert.Equal(200f, result.SmoothThresholdMeters);
        Assert.Contains(sink.ConfigInvalid, e => e.Field == "SmoothThresholdMeters" && !e.WarningOnly);
    }

    [Fact]
    public void Validate_HardSnapBelowSmooth_FallsBackHardSnapTo500()
    {
        var sink = new TestSink();
        // 硬跳 50 < 平滑区 100：非法，回退硬跳为 500
        var opt = new RemoteSyncThresholdOptions
        {
            SmoothThresholdMeters = 100f,
            HardSnapThresholdMeters = 50f,
        };

        var result = RemoteSyncThresholdValidator.Validate(opt, sink);

        Assert.Equal(500f, result.HardSnapThresholdMeters);
        Assert.Contains(sink.ConfigInvalid, e => e.Field == "HardSnapThresholdMeters" && !e.WarningOnly);
    }

    [Fact]
    public void Validate_ZeroBlendDuration_FallsBackTo0_2()
    {
        var sink = new TestSink();
        var opt = new RemoteSyncThresholdOptions { BlendDurationSeconds = 0f };

        var result = RemoteSyncThresholdValidator.Validate(opt, sink);

        Assert.Equal(0.2f, result.BlendDurationSeconds);
        Assert.Contains(sink.ConfigInvalid, e => e.Field == "BlendDurationSeconds" && !e.WarningOnly);
    }

    [Fact]
    public void Validate_ShortBlendDuration_WarningOnly_NoFallback()
    {
        var sink = new TestSink();
        // 0.05s < 0.1s：保留配置值但输出警告级诊断（spec 5.2.1 规则 7 的 a）
        var opt = new RemoteSyncThresholdOptions { BlendDurationSeconds = 0.05f };

        var result = RemoteSyncThresholdValidator.Validate(opt, sink);

        Assert.Equal(0.05f, result.BlendDurationSeconds);
        Assert.Contains(sink.ConfigInvalid, e => e.Field == "BlendDurationSeconds" && e.WarningOnly);
    }

    [Fact]
    public void Validate_NullOptions_ReturnsDefaults()
    {
        var result = RemoteSyncThresholdValidator.Validate(null, null);

        // 默认平滑区阈值已随实测日志修复提至 200m。
        Assert.Equal(200f, result.SmoothThresholdMeters);
        Assert.Equal(500f, result.HardSnapThresholdMeters);
        Assert.Equal(0.2f, result.BlendDurationSeconds);
        Assert.Equal(30f, result.NearDistanceMeters);
        Assert.Equal(80f, result.MidDistanceMeters);
        Assert.Equal(10, result.PerformanceDegradeEntityCount);
        Assert.Equal(20, result.MaxRemoteEntityCount);
    }

    [Fact]
    public void Validate_ValidOptions_Unchanged()
    {
        var sink = new TestSink();
        var opt = new RemoteSyncThresholdOptions
        {
            SmoothThresholdMeters = 120f,
            HardSnapThresholdMeters = 600f,
            BlendDurationSeconds = 0.25f,
            NearDistanceMeters = 40f,
            MidDistanceMeters = 90f,
            PerformanceDegradeEntityCount = 8,
            MaxRemoteEntityCount = 24,
        };

        var result = RemoteSyncThresholdValidator.Validate(opt, sink);

        Assert.Equal(120f, result.SmoothThresholdMeters);
        Assert.Equal(600f, result.HardSnapThresholdMeters);
        Assert.Equal(0.25f, result.BlendDurationSeconds);
        Assert.Equal(40f, result.NearDistanceMeters);
        Assert.Equal(90f, result.MidDistanceMeters);
        Assert.Equal(8, result.PerformanceDegradeEntityCount);
        Assert.Equal(24, result.MaxRemoteEntityCount);
        Assert.Empty(sink.ConfigInvalid);
    }

    [Fact]
    public void Validate_InvalidNearMid_PerformanceParams_FallBackToDefaults()
    {
        var sink = new TestSink();
        // Near=0、Mid=10(< Near)、PerformanceDegrade=0、MaxRemoteEntityCount=5(< Degrade=10)
        var opt = new RemoteSyncThresholdOptions
        {
            NearDistanceMeters = 0f,
            MidDistanceMeters = 10f,
            PerformanceDegradeEntityCount = 0,
            MaxRemoteEntityCount = 5,
        };

        var result = RemoteSyncThresholdValidator.Validate(opt, sink);

        Assert.Equal(30f, result.NearDistanceMeters);
        Assert.Equal(80f, result.MidDistanceMeters);
        Assert.Equal(10, result.PerformanceDegradeEntityCount);
        Assert.Equal(20, result.MaxRemoteEntityCount);
        Assert.Contains(sink.ConfigInvalid, e => e.Field == "NearDistanceMeters");
        Assert.Contains(sink.ConfigInvalid, e => e.Field == "MidDistanceMeters");
        Assert.Contains(sink.ConfigInvalid, e => e.Field == "PerformanceDegradeEntityCount");
        Assert.Contains(sink.ConfigInvalid, e => e.Field == "MaxRemoteEntityCount");
    }

    // ── 4.1：规模档位配置校验 ──────────────────────────────────────────

    [Fact]
    public void Validate_DefaultTierThresholds_Are20205000()
    {
        var result = RemoteSyncThresholdValidator.Validate(null, null);
        Assert.Equal(new[] { 20, 100, 1000, 5000 }, result.TierThresholds);
        Assert.Equal(5000, result.UltraScaleEntityCap);
    }

    [Fact]
    public void Validate_NonIncreasingTierThresholds_FallsBackToDefaults()
    {
        var sink = new TestSink();
        // 非递增 {5000,100,20}：非法，回退默认 {20,100,1000,5000}。
        var opt = new RemoteSyncThresholdOptions { TierThresholds = new[] { 5000, 100, 20 } };

        var result = RemoteSyncThresholdValidator.Validate(opt, sink);

        Assert.Equal(new[] { 20, 100, 1000, 5000 }, result.TierThresholds);
        Assert.Contains(sink.ConfigInvalid, e => e.Field == "TierThresholds");
    }

    [Fact]
    public void Validate_InvalidUltraScaleCap_FallsBackTo5000()
    {
        var sink = new TestSink();
        var opt = new RemoteSyncThresholdOptions { UltraScaleEntityCap = 0 };

        var result = RemoteSyncThresholdValidator.Validate(opt, sink);

        Assert.Equal(5000, result.UltraScaleEntityCap);
        Assert.Contains(sink.ConfigInvalid, e => e.Field == "UltraScaleEntityCap");
    }

    [Fact]
    public void Validate_ValidTierThresholds_Unchanged()
    {
        var sink = new TestSink();
        var opt = new RemoteSyncThresholdOptions
        {
            TierThresholds = new[] { 30, 150, 2000, 8000 },
            UltraScaleEntityCap = 8000,
        };

        var result = RemoteSyncThresholdValidator.Validate(opt, sink);

        Assert.Equal(new[] { 30, 150, 2000, 8000 }, result.TierThresholds);
        Assert.Equal(8000, result.UltraScaleEntityCap);
        Assert.Empty(sink.ConfigInvalid);
    }
}