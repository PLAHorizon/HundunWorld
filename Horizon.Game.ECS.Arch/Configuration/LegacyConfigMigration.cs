using System;
using Microsoft.Extensions.Logging;

namespace Horizon.Game.ECS.Arch.Configuration;

/// <summary>
/// 旧同步配置键 → 新权威链路配置键的平滑迁移（spec 4.5.2 / design.md 2.2.2.5）。
/// </summary>
/// <remarks>
/// <para>
/// 同步链路归一重构后，旧脚本组件（<c>NetworkSyncManager</c>/<c>NpcSyncManager</c>）被物理删除，
/// 其配置键随旧文件一并收敛。本迁移类在配置加载层识别旧键并转换为新配置语义，
/// 输出迁移日志供运维核对（迁移完成后旧键不再产生新的配置来源）。
/// </para>
/// <para>
/// 迁移映射：
/// </para>
/// <list type="bullet">
///   <item><c>NetworkSyncManager.InterpolationDelay</c> → <c>SnapshotApplySystem.AdaptiveDelayMinSeconds/MaxSeconds</c>（自适应窗口下限/上限语义组）。</item>
///   <item><c>NetworkSyncManager.PositionCorrectionThreshold</c> → <c>ReconciliationSystem.CorrectionThreshold</c>（同值迁移，默认 0.5m）。</item>
///   <item><c>NetworkSyncManager.NetworkUpdateRate</c> → <c>BandwidthBudgetOptions.NormalSnapshotHz</c>（客户端 20Hz 上行与 20Hz 下发对齐）。</item>
///   <item><c>NpcSyncManager.*SyncInterval</c> → <c>InterestGradeOptions.*SnapshotHz</c>（按 NPC 分类映射近/中/远档）。</item>
/// </list>
/// </remarks>
public static class LegacyConfigMigration
{
    /// <summary>
    /// 尝试将旧配置键迁移为新键语义。
    /// </summary>
    /// <param name="oldKey">旧配置键（如 "PositionCorrectionThreshold"）。</param>
    /// <param name="value">旧配置值。</param>
    /// <param name="newKey">迁移后的新键（未识别时为空）。</param>
    /// <param name="migratedValue">迁移后的新值（未识别时为原值）。</param>
    /// <param name="logger">迁移日志（可为 null）。</param>
    /// <returns>识别到旧键并完成迁移返回 true；未知旧键返回 false（不抛异常）。</returns>
    public static bool TryMigrateLegacyKey(string oldKey, object? value, out string newKey, out object? migratedValue, ILogger? logger = null)
    {
        newKey = string.Empty;
        migratedValue = value;

        if (string.IsNullOrEmpty(oldKey))
        {
            return false;
        }

        // 键名归一化：同时支持"裸键名"（PositionCorrectionThreshold）与"完整旧键名"
        // （NetworkSyncManager.PositionCorrectionThreshold / NpcSyncManager.NearSyncInterval）。
        var bareKey = oldKey
            .Replace("NetworkSyncManager.", string.Empty)
            .Replace("NpcSyncManager.", string.Empty);

        switch (bareKey)
        {
            case "InterpolationDelay":
                // 旧插值延迟（秒）→ 自适应窗口上下限语义组（以旧值为窗口中心，上下各 50% 带宽）。
                newKey = "SnapshotApplySystem.AdaptiveDelayMinSeconds/AdaptiveDelayMaxSeconds";
                if (value is float delay && delay > 0f)
                {
                    var min = Math.Max(0.05f, delay * 0.5f);
                    var max = Math.Max(min + 0.01f, delay * 1.5f);
                    migratedValue = (min, max);
                }
                else if (value is double d && d > 0d)
                {
                    var delayD = (float)d;
                    var min = Math.Max(0.05f, delayD * 0.5f);
                    var max = Math.Max(min + 0.01f, delayD * 1.5f);
                    migratedValue = (min, max);
                }
                else
                {
                    return false;
                }
                LogMigration(logger, oldKey, newKey, value, migratedValue);
                return true;

            case "PositionCorrectionThreshold":
                // 同值迁移（默认 0.5m）。
                newKey = "ReconciliationSystem.CorrectionThreshold";
                if (value is float threshold && threshold > 0f)
                {
                    migratedValue = threshold;
                }
                else if (value is double td && td > 0d)
                {
                    migratedValue = (float)td;
                }
                else
                {
                    return false;
                }
                LogMigration(logger, oldKey, newKey, value, migratedValue);
                return true;

            case "NetworkUpdateRate":
                // 客户端上行频率 → 服务端下发正常频率对齐。
                newKey = "BandwidthBudgetOptions.NormalSnapshotHz";
                if (value is int hz && hz > 0)
                {
                    migratedValue = hz;
                }
                else if (value is long lhz && lhz > 0)
                {
                    migratedValue = (int)lhz;
                }
                else
                {
                    return false;
                }
                LogMigration(logger, oldKey, newKey, value, migratedValue);
                return true;

            case "NearSyncInterval":
                newKey = "InterestGradeOptions.NearSnapshotHz";
                migratedValue = ConvertToHz(value);
                return migratedValue is not null && LogOrReturn(logger, oldKey, newKey, value, migratedValue);

            case "MidSyncInterval":
                newKey = "InterestGradeOptions.MidSnapshotHz";
                migratedValue = ConvertToHz(value);
                return migratedValue is not null && LogOrReturn(logger, oldKey, newKey, value, migratedValue);

            case "FarSyncInterval":
                newKey = "InterestGradeOptions.FarSnapshotHz";
                migratedValue = ConvertToHz(value);
                return migratedValue is not null && LogOrReturn(logger, oldKey, newKey, value, migratedValue);

            default:
                // 未知旧键：返回 false，不抛异常（配置层忽略）。
                return false;
        }
    }

    private static object? ConvertToHz(object? value)
    {
        // 旧键语义为"间隔毫秒"→ 新键语义为"频率 Hz"。间隔 50ms → 20Hz。
        if (value is int ms && ms > 0) return Math.Max(1, 1000 / ms);
        if (value is long lms && lms > 0) return Math.Max(1, (int)(1000 / lms));
        if (value is float fms && fms > 0f) return Math.Max(1, (int)(1000f / fms));
        if (value is double dms && dms > 0d) return Math.Max(1, (int)(1000d / dms));
        return null;
    }

    private static bool LogOrReturn(ILogger? logger, string oldKey, string newKey, object? value, object? migratedValue)
    {
        if (migratedValue is not null)
        {
            LogMigration(logger, oldKey, newKey, value, migratedValue);
            return true;
        }
        return false;
    }

    private static void LogMigration(ILogger? logger, string oldKey, string newKey, object? oldValue, object? newValue)
    {
        logger?.LogInformation(
            "[LegacyConfigMigration] 旧配置键已迁移：{OldKey}={OldValue} → {NewKey}={NewValue}",
            oldKey, oldValue, newKey, newValue);
    }
}