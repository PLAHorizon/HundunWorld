using System;
using Microsoft.Extensions.Logging;

namespace Horizon.Game.Core.Configuration;

/// <summary>
/// 带宽预算配置校验器：非法值回退默认并输出配置异常诊断（服务端带宽治理，spec 5.5.1.1 / DFX 4.3.1）。
/// 在启动阶段调用一次，保证运行时预算与频率恒合法。
/// </summary>
public static class BandwidthBudgetValidator
{
    /// <summary>
    /// 校验并修正配置。
    /// </summary>
    /// <param name="options">待校验配置（可能为 null 或非法值）。</param>
    /// <param name="logger">诊断日志（可为 null）。</param>
    /// <returns>合法配置；非法字段回退默认值（100/50/20/10/5/3/1.0）。</returns>
    /// <remarks>
    /// 回退规则：
    /// <list type="bullet">
    /// <item><c>options</c> 为 null → 返回全新默认配置。</item>
    /// <item><c>BudgetKbps &lt;= 0</c> → 回退 100。</item>
    /// <item><c>UltraScaleBudgetKbps &lt;= 0</c> → 回退 50。</item>
    /// <item><c>NormalSnapshotHz &lt;= ThrottledSnapshotHz</c> → 回退 20。</item>
    /// <item><c>ThrottledSnapshotHz &lt;= DegradedSnapshotHz</c> → 回退 10。</item>
    /// <item><c>DegradedSnapshotHz &lt; 1</c> → 回退 5。</item>
    /// <item><c>RecoverySeconds &lt; 1</c> → 回退 3。</item>
    /// <item><c>WindowSeconds &lt;= 0</c> → 回退 1.0。</item>
    /// </list>
    /// </remarks>
    public static BandwidthBudgetOptions Validate(BandwidthBudgetOptions? options, ILogger? logger = null)
    {
        var result = options ?? new BandwidthBudgetOptions();

        // 预算 > 0
        if (result.BudgetKbps <= 0)
        {
            Notify(logger, "BudgetKbps", result.BudgetKbps, 100.0);
            result.BudgetKbps = 100.0;
        }

        // 超大规模预算 > 0
        if (result.UltraScaleBudgetKbps <= 0)
        {
            Notify(logger, "UltraScaleBudgetKbps", result.UltraScaleBudgetKbps, 50.0);
            result.UltraScaleBudgetKbps = 50.0;
        }

        // NormalHz > ThrottledHz > DegradedHz >= 1（级联回退保证三档严格递减）
        if (result.DegradedSnapshotHz < 1)
        {
            Notify(logger, "DegradedSnapshotHz", result.DegradedSnapshotHz, 5);
            result.DegradedSnapshotHz = 5;
        }

        if (result.ThrottledSnapshotHz <= result.DegradedSnapshotHz)
        {
            Notify(logger, "ThrottledSnapshotHz", result.ThrottledSnapshotHz, 10);
            result.ThrottledSnapshotHz = 10;
            if (result.ThrottledSnapshotHz <= result.DegradedSnapshotHz)
            {
                Notify(logger, "DegradedSnapshotHz", result.DegradedSnapshotHz, 5);
                result.DegradedSnapshotHz = 5;
            }
        }

        if (result.NormalSnapshotHz <= result.ThrottledSnapshotHz)
        {
            Notify(logger, "NormalSnapshotHz", result.NormalSnapshotHz, 20);
            result.NormalSnapshotHz = 20;
            if (result.NormalSnapshotHz <= result.ThrottledSnapshotHz)
            {
                Notify(logger, "ThrottledSnapshotHz", result.ThrottledSnapshotHz, 10);
                result.ThrottledSnapshotHz = 10;
            }
        }

        // RecoverySeconds >= 1
        if (result.RecoverySeconds < 1)
        {
            Notify(logger, "RecoverySeconds", result.RecoverySeconds, 3);
            result.RecoverySeconds = 3;
        }

        // WindowSeconds > 0
        if (result.WindowSeconds <= 0)
        {
            Notify(logger, "WindowSeconds", result.WindowSeconds, 1.0);
            result.WindowSeconds = 1.0;
        }

        return result;
    }

    private static void Notify(ILogger? logger, string fieldName, double configuredValue, double fallbackValue)
    {
        logger?.LogWarning(
            "[BandwidthBudget] 配置非法回退：Field={Field} Configured={Configured} Fallback={Fallback}",
            fieldName, configuredValue, fallbackValue);
    }
}