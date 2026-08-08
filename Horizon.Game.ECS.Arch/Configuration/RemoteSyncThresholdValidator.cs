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