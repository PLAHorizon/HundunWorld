using System;
using Microsoft.Extensions.Logging;

namespace Horizon.Game.Core.Configuration;

/// <summary>
/// 兴趣分级配置校验器：非法值回退默认并输出配置异常诊断（spec 5.5.1.2 / DFX 4.3.1）。
/// 在启动阶段调用一次，保证运行时分级参数恒合法。
/// </summary>
public static class InterestGradeValidator
{
    /// <summary>
    /// 校验并修正配置。
    /// </summary>
    /// <param name="options">待校验配置（可能为 null 或非法值）。</param>
    /// <param name="logger">诊断日志（可为 null）。</param>
    /// <returns>合法配置；非法字段回退默认值（30/80/20/10/5/5）。</returns>
    /// <remarks>
    /// 回退规则：
    /// <list type="bullet">
    /// <item><c>options</c> 为 null → 返回全新默认配置。</item>
    /// <item><c>NearDistanceMeters &lt;= 0</c> → 回退 30。</item>
    /// <item><c>MidDistanceMeters &lt;= NearDistanceMeters</c> → 回退 80。</item>
    /// <item><c>NearSnapshotHz &lt; MidSnapshotHz</c> → 回退 20。</item>
    /// <item><c>MidSnapshotHz &lt; FarSnapshotHz</c> → 回退 10。</item>
    /// <item><c>FarSnapshotHz &lt; 1</c> → 回退 5。</item>
    /// <item><c>HysteresisMeters &lt;= 0</c> → 回退 5。</item>
    /// </list>
    /// </remarks>
    public static InterestGradeOptions Validate(InterestGradeOptions? options, ILogger? logger = null)
    {
        var result = options ?? new InterestGradeOptions();

        // 0 < Near < Mid
        if (result.NearDistanceMeters <= 0f)
        {
            Notify(logger, "NearDistanceMeters", result.NearDistanceMeters, 30f);
            result.NearDistanceMeters = 30f;
        }

        if (result.MidDistanceMeters <= result.NearDistanceMeters)
        {
            Notify(logger, "MidDistanceMeters", result.MidDistanceMeters, 80f);
            result.MidDistanceMeters = 80f;
        }

        // NearHz >= MidHz >= FarHz >= 1（级联回退保证频率档严格递减）
        if (result.FarSnapshotHz < 1)
        {
            Notify(logger, "FarSnapshotHz", result.FarSnapshotHz, 5);
            result.FarSnapshotHz = 5;
        }

        if (result.MidSnapshotHz < result.FarSnapshotHz)
        {
            Notify(logger, "MidSnapshotHz", result.MidSnapshotHz, 10);
            result.MidSnapshotHz = 10;
            if (result.MidSnapshotHz < result.FarSnapshotHz)
            {
                Notify(logger, "FarSnapshotHz", result.FarSnapshotHz, 5);
                result.FarSnapshotHz = 5;
            }
        }

        if (result.NearSnapshotHz < result.MidSnapshotHz)
        {
            Notify(logger, "NearSnapshotHz", result.NearSnapshotHz, 20);
            result.NearSnapshotHz = 20;
            if (result.NearSnapshotHz < result.MidSnapshotHz)
            {
                Notify(logger, "MidSnapshotHz", result.MidSnapshotHz, 10);
                result.MidSnapshotHz = 10;
            }
        }

        // 滞回 > 0
        if (result.HysteresisMeters <= 0f)
        {
            Notify(logger, "HysteresisMeters", result.HysteresisMeters, 5f);
            result.HysteresisMeters = 5f;
        }

        return result;
    }

    private static void Notify(ILogger? logger, string fieldName, float configuredValue, float fallbackValue)
    {
        logger?.LogWarning(
            "[InterestGrade] 配置非法回退：Field={Field} Configured={Configured} Fallback={Fallback}",
            fieldName, configuredValue, fallbackValue);
    }
}