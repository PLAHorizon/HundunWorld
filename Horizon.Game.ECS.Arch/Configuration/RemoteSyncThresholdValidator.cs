using Horizon.Game.ECS.Arch.Diagnostics;

namespace Horizon.Game.ECS.Arch.Configuration;

/// <summary>
/// 阈值配置校验器：非法值回退默认并输出诊断事件。
/// 在启动阶段调用一次，保证运行时阈值恒合法（spec 5.1.3 异常场景 3、DFX 4.3.1 配置越权防护配套）。
/// </summary>
public static class RemoteSyncThresholdValidator
{
    /// <summary>
    /// 校验并修正配置。
    /// </summary>
    /// <param name="options">待校验配置（可能为 null 或非法值）。</param>
    /// <param name="diagnostics">诊断事件汇（可为 null）。</param>
    /// <returns>合法配置；非法字段回退默认值（200/500/0.2/30/80/10/20）。</returns>
    /// <remarks>
    /// 回退规则：
    /// <list type="bullet">
    /// <item><c>SmoothThresholdMeters &lt;= 0</c> 或 <c>&gt; HardSnapThresholdMeters</c> → 回退 200。</item>
    /// <item><c>HardSnapThresholdMeters &lt;= SmoothThresholdMeters</c> → 回退 500。</item>
    /// <item><c>BlendDurationSeconds &lt;= 0</c> → 回退 0.2。</item>
    /// <item><c>BlendDurationSeconds &lt; 0.1</c> → 保留配置值但输出警告级提示（可能仍表现为闪跳，spec 5.2.1 规则 7）。</item>
    /// </list>
    /// </remarks>
    public static RemoteSyncThresholdOptions Validate(
        RemoteSyncThresholdOptions? options,
        ISyncDiagnosticsSink? diagnostics)
    {
        var result = options ?? new RemoteSyncThresholdOptions();

        // 平滑区阈值：(0, HardSnapThresholdMeters]
        if (result.SmoothThresholdMeters <= 0f || result.SmoothThresholdMeters > result.HardSnapThresholdMeters)
        {
            Notify(diagnostics, "SmoothThresholdMeters", result.SmoothThresholdMeters, 200f);
            result.SmoothThresholdMeters = 200f;
        }

        // 硬跳阈值：(SmoothThresholdMeters, +∞)
        if (result.HardSnapThresholdMeters <= result.SmoothThresholdMeters)
        {
            Notify(diagnostics, "HardSnapThresholdMeters", result.HardSnapThresholdMeters, 500f);
            result.HardSnapThresholdMeters = 500f;
        }

        // 混合时长：(0, +∞)
        if (result.BlendDurationSeconds <= 0f)
        {
            Notify(diagnostics, "BlendDurationSeconds", result.BlendDurationSeconds, 0.2f);
            result.BlendDurationSeconds = 0.2f;
        }
        // 混合时长 < 0.1s：仅警告不回退（spec 5.2.1 规则 7 的 a）
        else if (result.BlendDurationSeconds < 0.1f)
        {
            diagnostics?.OnConfigInvalid("BlendDurationSeconds", result.BlendDurationSeconds, result.BlendDurationSeconds, isWarningOnly: true);
        }

        // 分级距离：Near > 0
        if (result.NearDistanceMeters <= 0f)
        {
            Notify(diagnostics, "NearDistanceMeters", result.NearDistanceMeters, 30f);
            result.NearDistanceMeters = 30f;
        }

        // 分级距离：Mid > Near
        if (result.MidDistanceMeters <= result.NearDistanceMeters)
        {
            Notify(diagnostics, "MidDistanceMeters", result.MidDistanceMeters, 80f);
            result.MidDistanceMeters = 80f;
        }

        // 性能降档阈值：> 0
        if (result.PerformanceDegradeEntityCount <= 0)
        {
            Notify(diagnostics, "PerformanceDegradeEntityCount", result.PerformanceDegradeEntityCount, 10);
            result.PerformanceDegradeEntityCount = 10;
        }

        // 数量硬上限：>= PerformanceDegradeEntityCount
        if (result.MaxRemoteEntityCount < result.PerformanceDegradeEntityCount)
        {
            Notify(diagnostics, "MaxRemoteEntityCount", result.MaxRemoteEntityCount, 20);
            result.MaxRemoteEntityCount = 20;
        }

        // 规模档位阈值：严格递增且全部 > 0
        if (!IsStrictlyIncreasingPositive(result.TierThresholds))
        {
            Notify(diagnostics, "TierThresholds", result.TierThresholds?.Length ?? 0, 4);
            result.TierThresholds = new[] { 20, 100, 1000, 5000 };
        }

        // 超规模实体数上限：> 0
        if (result.UltraScaleEntityCap <= 0)
        {
            Notify(diagnostics, "UltraScaleEntityCap", result.UltraScaleEntityCap, 5000);
            result.UltraScaleEntityCap = 5000;
        }

        // ── 阶段 5.3.1 — SnapshotApplySystem 阈值 ──

        // 单帧消费上限：>= 1
        if (result.MaxSnapshotsPerFrame < 1)
        {
            Notify(diagnostics, "MaxSnapshotsPerFrame", result.MaxSnapshotsPerFrame, 32);
            result.MaxSnapshotsPerFrame = 32;
        }

        // 远程实体超时清理阈值：> 0
        if (result.StaleEntityTimeoutSeconds <= 0f)
        {
            Notify(diagnostics, "StaleEntityTimeoutSeconds", result.StaleEntityTimeoutSeconds, 90f);
            result.StaleEntityTimeoutSeconds = 90f;
        }

        // 自适应插值延迟下限：> 0
        if (result.AdaptiveDelayMinSeconds <= 0f)
        {
            Notify(diagnostics, "AdaptiveDelayMinSeconds", result.AdaptiveDelayMinSeconds, 0.1f);
            result.AdaptiveDelayMinSeconds = 0.1f;
        }

        // 自适应插值延迟上限：>= 下限（Math.Clamp 要求 min <= max，否则运行时抛异常）
        if (result.AdaptiveDelayMaxSeconds < result.AdaptiveDelayMinSeconds)
        {
            Notify(diagnostics, "AdaptiveDelayMaxSeconds", result.AdaptiveDelayMaxSeconds, 0.2f);
            result.AdaptiveDelayMaxSeconds = Math.Max(0.2f, result.AdaptiveDelayMinSeconds);
        }

        // 固定插值延迟：> 0
        if (result.FixedInterpolationDelaySeconds <= 0f)
        {
            Notify(diagnostics, "FixedInterpolationDelaySeconds", result.FixedInterpolationDelaySeconds, 0.1f);
            result.FixedInterpolationDelaySeconds = 0.1f;
        }

        // 平滑延迟变化速率：> 0
        if (result.MaxDelayChangePerSecond <= 0f)
        {
            Notify(diagnostics, "MaxDelayChangePerSecond", result.MaxDelayChangePerSecond, 0.3f);
            result.MaxDelayChangePerSecond = 0.3f;
        }

        // EWMA 平滑因子：必须在 (0, 1)
        if (result.RttEwmaAlpha <= 0f || result.RttEwmaAlpha >= 1f)
        {
            Notify(diagnostics, "RttEwmaAlpha", result.RttEwmaAlpha, 0.125f);
            result.RttEwmaAlpha = 0.125f;
        }
        if (result.RttJitterBeta <= 0f || result.RttJitterBeta >= 1f)
        {
            Notify(diagnostics, "RttJitterBeta", result.RttJitterBeta, 0.25f);
            result.RttJitterBeta = 0.25f;
        }
        if (result.SnapshotArrivalEwmaAlpha <= 0f || result.SnapshotArrivalEwmaAlpha >= 1f)
        {
            Notify(diagnostics, "SnapshotArrivalEwmaAlpha", result.SnapshotArrivalEwmaAlpha, 0.2f);
            result.SnapshotArrivalEwmaAlpha = 0.2f;
        }
        if (result.SnapshotArrivalJitterBeta <= 0f || result.SnapshotArrivalJitterBeta >= 1f)
        {
            Notify(diagnostics, "SnapshotArrivalJitterBeta", result.SnapshotArrivalJitterBeta, 0.25f);
            result.SnapshotArrivalJitterBeta = 0.25f;
        }

        // 停止判定阈值：> 0
        if (result.StopDisplacementThreshold <= 0f)
        {
            Notify(diagnostics, "StopDisplacementThreshold", result.StopDisplacementThreshold, 0.05f);
            result.StopDisplacementThreshold = 0.05f;
        }
        if (result.StopVelocityThreshold <= 0f)
        {
            Notify(diagnostics, "StopVelocityThreshold", result.StopVelocityThreshold, 0.5f);
            result.StopVelocityThreshold = 0.5f;
        }

        // 清理扫描间隔：>= 1
        if (result.OrphanCleanupInterval < 1)
        {
            Notify(diagnostics, "OrphanCleanupInterval", result.OrphanCleanupInterval, 300);
            result.OrphanCleanupInterval = 300;
        }
        if (result.StaleEntityCleanupInterval < 1)
        {
            Notify(diagnostics, "StaleEntityCleanupInterval", result.StaleEntityCleanupInterval, 60);
            result.StaleEntityCleanupInterval = 60;
        }

        // ── 阶段 5.3.2 — InterpolationSystem 阈值 ──

        // 前向预测最大外推时间：> 0
        if (result.DeadReckoningMaxExtrapolationSeconds <= 0f)
        {
            Notify(diagnostics, "DeadReckoningMaxExtrapolationSeconds", result.DeadReckoningMaxExtrapolationSeconds, 0.25f);
            result.DeadReckoningMaxExtrapolationSeconds = 0.25f;
        }

        return result;
    }

    private static bool IsStrictlyIncreasingPositive(int[]? thresholds)
    {
        if (thresholds is null || thresholds.Length == 0) return false;
        for (int i = 0; i < thresholds.Length; i++)
        {
            if (thresholds[i] <= 0) return false;
            if (i > 0 && thresholds[i] <= thresholds[i - 1]) return false;
        }
        return true;
    }

    private static void Notify(ISyncDiagnosticsSink? diagnostics, string fieldName, float configuredValue, float fallbackValue)
    {
        diagnostics?.OnConfigInvalid(fieldName, configuredValue, fallbackValue, isWarningOnly: false);
    }
}