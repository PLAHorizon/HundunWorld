using System;
using System.Reflection;
using Horizon.Game.ECS.Arch.Diagnostics;
using Horizon.Game.ECS.Arch.Systems;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 任务 10.3 — 网络质量滞回切换单元测试。
/// 验证 SnapshotApplySystem 中 NetworkQualityLevel 滞回切换逻辑：
/// Strong→Medium(RTT>50ms)、Medium→Strong(RTT<30ms)、Medium→Weak(RTT>200ms)、Weak→Medium(RTT<150ms)。
/// 被测代码：SnapshotApplySystem.cs:274-288（AdaptiveInterpolationDelaySeconds getter 内滞回切换）。
/// </summary>
public class SnapshotApplySystemNetworkQualityHysteresisTests : IDisposable
{
    public SnapshotApplySystemNetworkQualityHysteresisTests()
    {
        ResetState();
        SnapshotApplySystem.UseAdaptiveDelay = true;
        SnapshotApplySystem.Diagnostics = null;
    }

    public void Dispose()
    {
        ResetState();
    }

    private static void ResetState()
    {
        SnapshotApplySystem.ResetAdaptiveDelayStats();
    }

    /// <summary>
    /// 注入一次 RTT 样本并触发 AdaptiveInterpolationDelaySeconds getter 以驱动滞回切换。
    /// 第一次 RecordRttSample 后 _adaptiveRttSeconds = rttMs/1000（EWMA 初始为 0）。
    /// </summary>
    private static NetworkQualityLevel ApplyRttAndReadLevel(float rttMs)
    {
        ResetState();
        SnapshotApplySystem.RecordRttSample(rttMs);
        // 循环读取 getter 直到等级稳定（滞回 switch 每次只执行一个分支，Strong→Medium→Weak 需多次）
        for (int i = 0; i < 4; i++)
        {
            _ = SnapshotApplySystem.AdaptiveInterpolationDelaySeconds;
        }
        return SnapshotApplySystem.CurrentNetworkQualityLevel;
    }

    /// <summary>
    /// 在当前状态下追加一次 RTT 样本并读取等级（不重置，用于连续切换测试）。
    /// </summary>
    private static NetworkQualityLevel ApplyRttAppendAndReadLevel(float rttMs)
    {
        SnapshotApplySystem.RecordRttSample(rttMs);
        for (int i = 0; i < 4; i++)
        {
            _ = SnapshotApplySystem.AdaptiveInterpolationDelaySeconds;
        }
        return SnapshotApplySystem.CurrentNetworkQualityLevel;
    }

    // ─── Strong → Medium 边界（RTT > 50ms）───

    [Fact]
    public void Hysteresis_StrongToMedium_WhenRttAbove50ms()
    {
        // RTT=51ms > 50 → Strong→Medium
        var level = ApplyRttAndReadLevel(51f);
        Assert.Equal(NetworkQualityLevel.Medium, level);
    }

    [Fact]
    public void Hysteresis_StrongRemains_WhenRttAt50ms()
    {
        // RTT=50ms 不 > 50 → 保持 Strong（严格大于边界）
        var level = ApplyRttAndReadLevel(50f);
        Assert.Equal(NetworkQualityLevel.Strong, level);
    }

    // ─── Medium → Strong 边界（需 RTT < 30ms 才回 Strong）───

    [Fact]
    public void Hysteresis_MediumToStrong_RequiresRttBelow30ms()
    {
        // 先到 Medium
        ApplyRttAndReadLevel(51f);
        Assert.Equal(NetworkQualityLevel.Medium, SnapshotApplySystem.CurrentNetworkQualityLevel);

        // RTT=40ms（介于 30~50 之间）→ 不回 Strong（滞回）
        ResetRttOnly();
        var level = ApplyRttAppendAndReadLevel(40f);
        Assert.Equal(NetworkQualityLevel.Medium, level);

        // RTT=29ms < 30 → 回 Strong
        ResetRttOnly();
        level = ApplyRttAppendAndReadLevel(29f);
        Assert.Equal(NetworkQualityLevel.Strong, level);
    }

    [Fact]
    public void Hysteresis_MediumRemains_WhenRttAt30ms()
    {
        // 先到 Medium
        ApplyRttAndReadLevel(51f);
        // RTT=30ms 不 < 30 → 保持 Medium
        ResetRttOnly();
        var level = ApplyRttAppendAndReadLevel(30f);
        Assert.Equal(NetworkQualityLevel.Medium, level);
    }

    // ─── Medium → Weak 边界（RTT > 200ms）───

    [Fact]
    public void Hysteresis_MediumToWeak_WhenRttAbove200ms()
    {
        // 先到 Medium
        ApplyRttAndReadLevel(51f);
        // RTT=201ms > 200 → Weak
        ResetRttOnly();
        var level = ApplyRttAppendAndReadLevel(201f);
        Assert.Equal(NetworkQualityLevel.Weak, level);
    }

    [Fact]
    public void Hysteresis_MediumRemains_WhenRttAt200ms()
    {
        ApplyRttAndReadLevel(51f);
        // RTT=200ms 不 > 200 → 保持 Medium
        ResetRttOnly();
        var level = ApplyRttAppendAndReadLevel(200f);
        Assert.Equal(NetworkQualityLevel.Medium, level);
    }

    // ─── Weak → Medium 边界（需 RTT < 150ms 才回 Medium）───

    [Fact]
    public void Hysteresis_WeakToMedium_RequiresRttBelow150ms()
    {
        // 先到 Medium 再到 Weak
        ApplyRttAndReadLevel(51f);
        ResetRttOnly();
        ApplyRttAppendAndReadLevel(201f);
        Assert.Equal(NetworkQualityLevel.Weak, SnapshotApplySystem.CurrentNetworkQualityLevel);

        // RTT=160ms（介于 150~200 之间）→ 不回 Medium（滞回）
        ResetRttOnly();
        var level = ApplyRttAppendAndReadLevel(160f);
        Assert.Equal(NetworkQualityLevel.Weak, level);

        // RTT=149ms < 150 → 回 Medium
        ResetRttOnly();
        level = ApplyRttAppendAndReadLevel(149f);
        Assert.Equal(NetworkQualityLevel.Medium, level);
    }

    // ─── 边界不反复切换（No Flapping）───

    [Fact]
    public void Hysteresis_NoFlapping_AtStrongMediumBoundary()
    {
        // 模拟 RTT 在 50ms 边界附近波动：51→40→51→40...
        // 由于滞回，40ms 不会回 Strong，所以不会反复 Strong↔Medium
        ApplyRttAndReadLevel(51f); // → Medium
        Assert.Equal(NetworkQualityLevel.Medium, SnapshotApplySystem.CurrentNetworkQualityLevel);

        ResetRttOnly();
        ApplyRttAppendAndReadLevel(40f); // 仍 Medium（滞回）
        Assert.Equal(NetworkQualityLevel.Medium, SnapshotApplySystem.CurrentNetworkQualityLevel);

        ResetRttOnly();
        ApplyRttAppendAndReadLevel(51f); // 仍 Medium
        Assert.Equal(NetworkQualityLevel.Medium, SnapshotApplySystem.CurrentNetworkQualityLevel);

        ResetRttOnly();
        ApplyRttAppendAndReadLevel(40f); // 仍 Medium
        Assert.Equal(NetworkQualityLevel.Medium, SnapshotApplySystem.CurrentNetworkQualityLevel);

        // 只有降到 30ms 以下才回 Strong
        ResetRttOnly();
        ApplyRttAppendAndReadLevel(29f);
        Assert.Equal(NetworkQualityLevel.Strong, SnapshotApplySystem.CurrentNetworkQualityLevel);
    }

    [Fact]
    public void Hysteresis_NoFlapping_AtMediumWeakBoundary()
    {
        // 先到 Medium
        ApplyRttAndReadLevel(51f);
        ResetRttOnly();
        ApplyRttAppendAndReadLevel(201f); // → Weak
        Assert.Equal(NetworkQualityLevel.Weak, SnapshotApplySystem.CurrentNetworkQualityLevel);

        // RTT 在 200ms 边界附近波动：160→201→160...
        // 160ms 不会回 Medium（滞回），不会反复 Medium↔Weak
        ResetRttOnly();
        ApplyRttAppendAndReadLevel(160f); // 仍 Weak
        Assert.Equal(NetworkQualityLevel.Weak, SnapshotApplySystem.CurrentNetworkQualityLevel);

        ResetRttOnly();
        ApplyRttAppendAndReadLevel(201f); // 仍 Weak
        Assert.Equal(NetworkQualityLevel.Weak, SnapshotApplySystem.CurrentNetworkQualityLevel);

        ResetRttOnly();
        ApplyRttAppendAndReadLevel(160f); // 仍 Weak
        Assert.Equal(NetworkQualityLevel.Weak, SnapshotApplySystem.CurrentNetworkQualityLevel);

        // 只有降到 150ms 以下才回 Medium
        ResetRttOnly();
        ApplyRttAppendAndReadLevel(149f);
        Assert.Equal(NetworkQualityLevel.Medium, SnapshotApplySystem.CurrentNetworkQualityLevel);
    }

    // ─── 重置后回到 Strong ───

    [Fact]
    public void Hysteresis_ResetAdaptiveStats_ReturnsToStrong()
    {
        ApplyRttAndReadLevel(201f); // 到 Weak（经 Medium）
        Assert.Equal(NetworkQualityLevel.Weak, SnapshotApplySystem.CurrentNetworkQualityLevel);

        SnapshotApplySystem.ResetAdaptiveDelayStats();
        Assert.Equal(NetworkQualityLevel.Strong, SnapshotApplySystem.CurrentNetworkQualityLevel);
    }

    /// <summary>
    /// 仅重置 RTT 样本（保留当前 NetworkQualityLevel 状态），用于连续切换测试。
    /// 通过反射置零 _adaptiveRttSeconds，使下一次 RecordRttSample 精确设置目标 RTT。
    /// </summary>
    private static void ResetRttOnly()
    {
        var lockField = typeof(SnapshotApplySystem)
            .GetField("_adaptiveLock", BindingFlags.NonPublic | BindingFlags.Static);
        var rttField = typeof(SnapshotApplySystem)
            .GetField("_adaptiveRttSeconds", BindingFlags.NonPublic | BindingFlags.Static);
        var rttJitterField = typeof(SnapshotApplySystem)
            .GetField("_adaptiveRttJitterSeconds", BindingFlags.NonPublic | BindingFlags.Static);

        var lockObj = lockField!.GetValue(null);
        lock (lockObj!)
        {
            rttField!.SetValue(null, 0f);
            rttJitterField!.SetValue(null, 0f);
        }
    }
}