using FlaxEngine;
using NarrativePro.Core;

namespace NarrativePro.Vehicles.Mass
{
    /// <summary>
    /// Mass 载具日志类别。对应 UE5 LogMassVehicle（MassVehicle.h）。
    /// UE5: DECLARE_LOG_CATEGORY_EXTERN(LogMassVehicle, Log, All)。
    /// Flax 中用静态类封装日志辅助。
    /// </summary>
    public static class MassVehicleLog
    {
        /// <summary>日志类别名。</summary>
        public const string Category = "MassVehicle";

        public static void Log(object message)
        {
            NarrativeLog.Log($"[{Category}] {message}");
        }

        public static void LogWarning(object message)
        {
            NarrativeLog.LogWarning($"[{Category}] {message}");
        }

        public static void LogError(object message)
        {
            NarrativeLog.LogError($"[{Category}] {message}");
        }
    }
}
