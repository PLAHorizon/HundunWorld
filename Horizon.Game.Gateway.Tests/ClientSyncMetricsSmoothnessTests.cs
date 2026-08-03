using System;
using HundunWorld.Game.Network;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 任务 10.5 — 平滑度评分滑动窗口单元测试。
/// 验证 ClientSyncMetrics.RecordSmoothnessSample 维护 60 帧滑动窗口，
/// 匀速移动评分高、卡顿移动评分低，窗口外样本不参与计算。
/// 被测代码：ClientSyncMetrics.cs:195（SmoothnessScore 与 RecordSmoothnessSample）。
/// </summary>
public class ClientSyncMetricsSmoothnessTests : IDisposable
{
    public ClientSyncMetricsSmoothnessTests()
    {
        ClientSyncMetrics.Reset();
    }

    public void Dispose()
    {
        ClientSyncMetrics.Reset();
    }

    // ─── 匀速移动评分高 ───

    [Fact]
    public void Smoothness_ConstantDelta_HighScore()
    {
        // 60 帧匀速移动：每帧位移 delta=0.1m，帧时间=1/60s 恒定
        // 标准差≈0 → 评分接近 100
        const float delta = 0.1f;
        const float frameTime = 1f / 60f;

        for (int i = 0; i < 60; i++)
        {
            ClientSyncMetrics.RecordSmoothnessSample(delta, frameTime);
        }

        var score = ClientSyncMetrics.SmoothnessScore;
        // 匀速移动评分应很高（> 90）
        Assert.True(score > 90f,
            $"匀速移动评分应 > 90，实际 {score:F2}");
    }

    // ─── 卡顿移动评分低 ───

    [Fact]
    public void Smoothness_JitteryDelta_LowerScoreThanConstant()
    {
        // 卡顿移动：位移 delta 交替 0.01m / 0.5m（大幅波动）
        const float frameTime = 1f / 60f;

        for (int i = 0; i < 60; i++)
        {
            var delta = i % 2 == 0 ? 0.01f : 0.5f;
            ClientSyncMetrics.RecordSmoothnessSample(delta, frameTime);
        }

        var jitteryScore = ClientSyncMetrics.SmoothnessScore;

        // 对比匀速移动评分
        ClientSyncMetrics.Reset();
        for (int i = 0; i < 60; i++)
        {
            ClientSyncMetrics.RecordSmoothnessSample(0.25f, frameTime); // 匀速 0.25m
        }
        var constantScore = ClientSyncMetrics.SmoothnessScore;

        Assert.True(jitteryScore < constantScore,
            $"卡顿移动评分 ({jitteryScore:F2}) 应低于匀速移动评分 ({constantScore:F2})");
        // 卡顿移动评分应明显偏低
        Assert.True(jitteryScore < 80f,
            $"卡顿移动评分应 < 80，实际 {jitteryScore:F2}");
    }

    [Fact]
    public void Smoothness_FrameTimeJitter_LowersScore()
    {
        // 帧时间抖动：位移恒定但帧时间大幅波动
        const float delta = 0.1f;

        for (int i = 0; i < 60; i++)
        {
            var frameTime = i % 2 == 0 ? 1f / 60f : 1f / 20f; // 16.7ms / 50ms 交替
            ClientSyncMetrics.RecordSmoothnessSample(delta, frameTime);
        }

        var jitteryScore = ClientSyncMetrics.SmoothnessScore;

        // 对比帧时间稳定的评分
        ClientSyncMetrics.Reset();
        for (int i = 0; i < 60; i++)
        {
            ClientSyncMetrics.RecordSmoothnessSample(delta, 1f / 60f);
        }
        var stableScore = ClientSyncMetrics.SmoothnessScore;

        Assert.True(jitteryScore < stableScore,
            $"帧时间抖动评分 ({jitteryScore:F2}) 应低于帧时间稳定评分 ({stableScore:F2})");
    }

    // ─── 窗口外样本不参与计算 ───

    [Fact]
    public void Smoothness_WindowSize60_OldSamplesExcluded()
    {
        // 先填入 60 帧卡顿样本（低评分）
        const float frameTime = 1f / 60f;
        for (int i = 0; i < 60; i++)
        {
            var delta = i % 2 == 0 ? 0.01f : 0.5f;
            ClientSyncMetrics.RecordSmoothnessSample(delta, frameTime);
        }
        var lowScore = ClientSyncMetrics.SmoothnessScore;
        Assert.True(lowScore < 80f, $"前置卡顿评分应 < 80，实际 {lowScore:F2}");

        // 再填入 60 帧匀速样本（高评分）→ 旧卡顿样本应被完全挤出窗口
        for (int i = 0; i < 60; i++)
        {
            ClientSyncMetrics.RecordSmoothnessSample(0.1f, frameTime);
        }
        var recoveredScore = ClientSyncMetrics.SmoothnessScore;

        // 窗口外样本不参与计算 → 评分应恢复到匀速高分
        Assert.True(recoveredScore > 90f,
            $"60 帧匀速后旧卡顿样本应被挤出窗口，评分应 > 90，实际 {recoveredScore:F2}");
    }

    [Fact]
    public void Smoothness_WindowSize60_PartialWindowStillValid()
    {
        // 少于 60 帧时仍按实际样本数计算
        for (int i = 0; i < 30; i++)
        {
            ClientSyncMetrics.RecordSmoothnessSample(0.1f, 1f / 60f);
        }
        var score = ClientSyncMetrics.SmoothnessScore;
        // 30 帧匀速也应高分
        Assert.True(score > 90f,
            $"30 帧匀速评分应 > 90，实际 {score:F2}");
    }

    // ─── 少于 2 样本返回 100 ───

    [Fact]
    public void Smoothness_LessThanTwoSamples_Returns100()
    {
        ClientSyncMetrics.RecordSmoothnessSample(0.1f, 1f / 60f);
        Assert.Equal(100f, ClientSyncMetrics.SmoothnessScore, 0.01f);
    }

    [Fact]
    public void Smoothness_ZeroSamples_AfterReset_ReturnsZero()
    {
        // Reset 后评分为 0（未采样）
        Assert.Equal(0f, ClientSyncMetrics.SmoothnessScore, 0.01f);
    }

    // ─── Reset 清零窗口 ───

    [Fact]
    public void Smoothness_Reset_ClearsWindow()
    {
        // 填入卡顿样本
        for (int i = 0; i < 60; i++)
        {
            ClientSyncMetrics.RecordSmoothnessSample(i % 2 == 0 ? 0.01f : 0.5f, 1f / 60f);
        }
        Assert.True(ClientSyncMetrics.SmoothnessScore < 80f);

        ClientSyncMetrics.Reset();

        // Reset 后评分归零，重新填入匀速样本应得高分（窗口已清空）
        Assert.Equal(0f, ClientSyncMetrics.SmoothnessScore, 0.01f);
        for (int i = 0; i < 60; i++)
        {
            ClientSyncMetrics.RecordSmoothnessSample(0.1f, 1f / 60f);
        }
        Assert.True(ClientSyncMetrics.SmoothnessScore > 90f,
            $"Reset 后重新匀速采样应得高分，实际 {ClientSyncMetrics.SmoothnessScore:F2}");
    }

    // ─── 评分范围 [0, 100] ───

    [Fact]
    public void Smoothness_ScoreAlwaysInRange_0To100()
    {
        // 极端卡顿场景
        for (int i = 0; i < 60; i++)
        {
            ClientSyncMetrics.RecordSmoothnessSample(i * 0.1f, 1f / 60f);
        }
        var score = ClientSyncMetrics.SmoothnessScore;
        Assert.True(score >= 0f && score <= 100f,
            $"评分应在 [0, 100] 范围内，实际 {score:F2}");
    }
}