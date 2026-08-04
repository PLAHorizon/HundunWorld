using System;
using Horizon.Game.Core.Configuration;
using Horizon.Game.Core.Sim.Server;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 任务组 3.4 — 兴趣分级策略单元测试。
/// 验证：
/// <list type="bullet">
///   <item>距离→近/中/远三档映射（spec 5.5.1.2）。</item>
///   <item>分级切换滞回保护：边界往复移动不频繁抖动（spec 5.5.3.2）。</item>
///   <item>非法配置兜底回退默认（DFX 4.3.1）。</item>
/// </list>
/// </summary>
public class InterestGradeStrategyTests
{
    private static DefaultSyncInterestGradeStrategy CreateDefault(float near = 30f, float mid = 80f, float hyst = 5f)
    {
        return new DefaultSyncInterestGradeStrategy(new InterestGradeOptions
        {
            NearDistanceMeters = near,
            MidDistanceMeters = mid,
            NearSnapshotHz = 20,
            MidSnapshotHz = 10,
            FarSnapshotHz = 5,
            HysteresisMeters = hyst,
        });
    }

    // ── 3.2：距离→档位映射 ─────────────────────────────────────────────

    [Fact]
    public void Classify_WithinNear_ReturnsNear()
    {
        var strategy = CreateDefault();
        Assert.Equal(InterestGrade.Near, strategy.Classify(10f));
        Assert.Equal(InterestGrade.Near, strategy.Classify(29f));
    }

    [Fact]
    public void Classify_WithinMid_ReturnsMid()
    {
        var strategy = CreateDefault();
        Assert.Equal(InterestGrade.Mid, strategy.Classify(50f));
        Assert.Equal(InterestGrade.Mid, strategy.Classify(79f));
    }

    [Fact]
    public void Classify_BeyondMid_ReturnsFar()
    {
        var strategy = CreateDefault();
        Assert.Equal(InterestGrade.Far, strategy.Classify(200f));
    }

    [Fact]
    public void ShouldSendFullFields_NearOnly()
    {
        var strategy = CreateDefault();
        Assert.True(strategy.ShouldSendFullFields(InterestGrade.Near));
        Assert.False(strategy.ShouldSendFullFields(InterestGrade.Mid));
        Assert.False(strategy.ShouldSendFullFields(InterestGrade.Far));
    }

    [Fact]
    public void GetSnapshotHz_ReturnsPerGrade()
    {
        var strategy = CreateDefault();
        Assert.Equal(20, strategy.GetSnapshotHz(InterestGrade.Near));
        Assert.Equal(10, strategy.GetSnapshotHz(InterestGrade.Mid));
        Assert.Equal(5, strategy.GetSnapshotHz(InterestGrade.Far));
    }

    // ── 3.4：滞回保护（边界往复不抖动） ────────────────────────────────

    [Fact]
    public void Classify_OscillateNearBoundary_NoFrequentSwitching()
    {
        var strategy = CreateDefault(near: 30f, mid: 80f, hyst: 5f);

        // 起始在近档（10m）。
        Assert.Equal(InterestGrade.Near, strategy.Classify(10f));

        // 29~31m 振荡：升档阈值 = 30+5=35，29/31 均未越过 → 保持 Near，0 次切换。
        int switchCount = 0;
        var last = strategy.Classify(29f);
        for (int i = 0; i < 20; i++)
        {
            var cur = strategy.Classify(i % 2 == 0 ? 31f : 29f);
            if (cur != last) switchCount++;
            last = cur;
        }
        Assert.Equal(0, switchCount);
    }

    [Fact]
    public void Classify_OscillateMidBoundary_NoFrequentSwitching()
    {
        var strategy = CreateDefault(near: 30f, mid: 80f, hyst: 5f);

        // 先进入中档（60m）。
        Assert.Equal(InterestGrade.Mid, strategy.Classify(60f));

        // 78~82m 振荡：升档阈值 = 80+5=85，降档阈值 = 30-5=25 → 保持 Mid，0 次切换。
        int switchCount = 0;
        var last = strategy.Classify(78f);
        for (int i = 0; i < 20; i++)
        {
            var cur = strategy.Classify(i % 2 == 0 ? 82f : 78f);
            if (cur != last) switchCount++;
            last = cur;
        }
        Assert.Equal(0, switchCount);
    }

    [Fact]
    public void Classify_BeyondHysteresisBoundary_SwitchesToFar()
    {
        var strategy = CreateDefault();
        // 中档 60m → 86m（> 80+5）→ Far。
        Assert.Equal(InterestGrade.Mid, strategy.Classify(60f));
        Assert.Equal(InterestGrade.Far, strategy.Classify(86f));
    }

    // ── 3.4：非法配置兜底 ──────────────────────────────────────────────

    [Fact]
    public void Validate_NullOptions_ReturnsDefaults()
    {
        var result = InterestGradeValidator.Validate(null);
        Assert.Equal(30f, result.NearDistanceMeters);
        Assert.Equal(80f, result.MidDistanceMeters);
        Assert.Equal(20, result.NearSnapshotHz);
        Assert.Equal(10, result.MidSnapshotHz);
        Assert.Equal(5, result.FarSnapshotHz);
        Assert.Equal(5f, result.HysteresisMeters);
    }

    [Fact]
    public void Validate_InvalidNearZero_FallsBackTo30()
    {
        var result = InterestGradeValidator.Validate(new InterestGradeOptions { NearDistanceMeters = 0f });
        Assert.Equal(30f, result.NearDistanceMeters);
    }

    [Fact]
    public void Validate_MidBelowNear_FallsBackToDefaults()
    {
        // Mid(10) <= Near(30)：非法，回退 Near/Mid 默认。
        var result = InterestGradeValidator.Validate(new InterestGradeOptions
        {
            NearDistanceMeters = 30f,
            MidDistanceMeters = 10f,
        });
        Assert.Equal(30f, result.NearDistanceMeters);
        Assert.Equal(80f, result.MidDistanceMeters);
    }

    [Fact]
    public void Validate_MidHzAboveNearHz_FallsBackToDefaults()
    {
        // MidHz(25) > NearHz(20)：非法，回退频率默认。
        var result = InterestGradeValidator.Validate(new InterestGradeOptions
        {
            NearSnapshotHz = 20,
            MidSnapshotHz = 25,
            FarSnapshotHz = 5,
        });
        Assert.Equal(20, result.NearSnapshotHz);
        Assert.Equal(10, result.MidSnapshotHz);
        Assert.Equal(5, result.FarSnapshotHz);
    }

    [Fact]
    public void Validate_HysteresisZero_FallsBackTo5()
    {
        var result = InterestGradeValidator.Validate(new InterestGradeOptions { HysteresisMeters = 0f });
        Assert.Equal(5f, result.HysteresisMeters);
    }
}