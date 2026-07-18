using System;
using FlaxEngine;
using NarrativePro.Core;
using NarrativePro.Items;

namespace NarrativePro.Settings
{
    /// <summary>
    /// 游戏内时间值。对应 UE5 FTimeOfDay。
    /// 取值范围 0.0 ~ 2400.0，支持与 float 的隐式转换。
    /// </summary>
    [Serializable]
    public struct FTimeOfDay : IEquatable<FTimeOfDay>, IComparable<FTimeOfDay>
    {
        /// <summary>时间值，范围 0.0 ~ 2400.0。</summary>
        public float Time;

        /// <summary>时间范围最小值。</summary>
        public const float RangeMin = 0.0f;

        /// <summary>时间范围最大值。</summary>
        public const float RangeMax = 2400.0f;

        public FTimeOfDay(float inTime)
        {
            Time = inTime;
        }

        /// <summary>允许从 float 隐式转换为 FTimeOfDay。</summary>
        public static implicit operator FTimeOfDay(float v) => new FTimeOfDay(v);

        /// <summary>允许从 FTimeOfDay 隐式转换为 float。</summary>
        public static implicit operator float(FTimeOfDay v) => v.Time;

        public bool Equals(FTimeOfDay other) => Time == other.Time;
        public override bool Equals(object obj) => obj is FTimeOfDay t && Equals(t);
        public override int GetHashCode() => Time.GetHashCode();
        public int CompareTo(FTimeOfDay other) => Time.CompareTo(other.Time);
        public override string ToString() => Time.ToString();

        public static bool operator ==(FTimeOfDay a, FTimeOfDay b) => a.Time == b.Time;
        public static bool operator !=(FTimeOfDay a, FTimeOfDay b) => a.Time != b.Time;
        public static bool operator <(FTimeOfDay a, FTimeOfDay b) => a.Time < b.Time;
        public static bool operator >(FTimeOfDay a, FTimeOfDay b) => a.Time > b.Time;
        public static bool operator <=(FTimeOfDay a, FTimeOfDay b) => a.Time <= b.Time;
        public static bool operator >=(FTimeOfDay a, FTimeOfDay b) => a.Time >= b.Time;
    }

    /// <summary>
    /// Narrative 昼夜系统设置。对应 UE5 UNarrativeTimeOfDaySettings。
    /// UE5 中使用 UCLASS(config=Game, defaultconfig)，由 GameState 使用。
    /// Flax 中以 [Serializable] 类 + 静态 Instance 单例实现。
    /// </summary>
    [Serializable]
    public class NarrativeTimeOfDaySettings
    {
        /// <summary>若为 true，将基于 DayLengthMinutes/NightLengthMinutes 在 Tick 中动态更新时间。</summary>
        public bool bDynamicTimeOfDay = true;

        /// <summary>GameState 初始化时的默认时间。</summary>
        public FTimeOfDay DefaultTimeOfDay = new FTimeOfDay(800.0f);

        /// <summary>
        /// 白天时长（分钟），仅当场景中存在 BP_NarrativeSky 时生效。最小 0.01。
        /// 仅在 bDynamicTimeOfDay 为 true 时有效。
        /// </summary>
        public float DayLengthMinutes = 10.0f;

        /// <summary>
        /// 夜晚时长（分钟），仅当场景中存在 BP_NarrativeSky 时生效。最小 0.01。
        /// 仅在 bDynamicTimeOfDay 为 true 时有效。
        /// </summary>
        public float NightLengthMinutes = 10.0f;

        /// <summary>日出时间（场景中存在 NarrativeSky 时使用）。仅在 bDynamicTimeOfDay 为 true 时有效。</summary>
        public FTimeOfDay SunriseTime = new FTimeOfDay(600.0f);

        /// <summary>日落时间（场景中存在 NarrativeSky 时使用）。仅在 bDynamicTimeOfDay 为 true 时有效。</summary>
        public FTimeOfDay SunsetTime = new FTimeOfDay(1800.0f);

        /// <summary>单例实例。</summary>
        public static NarrativeTimeOfDaySettings Instance { get; set; } = LoadDefault();

        private static NarrativeTimeOfDaySettings LoadDefault()
        {
            // TODO [需接入设置加载系统]: 从 Flax 游戏配置或 JSON 文件加载持久化设置。暂时返回默认实例。
            var settings = new NarrativeTimeOfDaySettings();
            NarrativeLog.Log("NarrativeTimeOfDaySettings 已使用默认值初始化。");
            return settings;
        }
    }
}
