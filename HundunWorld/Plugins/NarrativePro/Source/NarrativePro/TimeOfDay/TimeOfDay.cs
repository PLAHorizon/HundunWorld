using System;
using FlaxEngine;
using NarrativePro.Core;
using NarrativePro.Settings;

namespace NarrativePro.TimeOfDay
{
    /// <summary>
    /// 时间范围结构体。对应 UE5 FTimeOfDayRange，带属性自定义化显示。
    /// 取值范围 0.0 ~ 2400.0。
    /// 注：FTimeOfDay 已定义于 NarrativePro.Settings 命名空间（见 NarrativeTimeOfDaySettings.cs），
    /// 含完整的隐式 float 转换与比较/算术运算符，此处不重复定义以避免类型冲突。
    /// </summary>
    [Serializable]
    public struct FTimeOfDayRange
    {
        /// <summary>该范围的最小时间。</summary>
        public float TimeMin;

        /// <summary>该范围的最大时间。</summary>
        public float TimeMax;

        /// <summary>时间值允许的最小范围。</summary>
        public const float RangeMin = 0.0f;

        /// <summary>时间值允许的最大范围。</summary>
        public const float RangeMax = 2400.0f;

        public FTimeOfDayRange()
        {
            TimeMin = RangeMin;
            TimeMax = RangeMax;
        }

        public FTimeOfDayRange(float inTimeMin, float inTimeMax)
        {
            TimeMin = inTimeMin;
            TimeMax = inTimeMax;
        }
    }

    /// <summary>
    /// FTimeOfDay 与 FTimeOfDayRange 的静态工具函数库。对应 UE5 UTimeOfDayStatics（UBlueprintFunctionLibrary）。
    /// 提供 FTimeOfDay 与数值之间的比较、算术、转换，以及随机范围、12 小时制等便捷函数。
    /// 由于 FTimeOfDay 在 C# 中已具备运算符重载与隐式转换，部分方法为 API 对等的命名包装。
    /// </summary>
    public static class TimeOfDayStatics
    {
        // 全局随机数生成器（对应 UE5 FMath::RandRange 使用的全局随机流）
        private static readonly Random _globalRandom = new Random();

        /// <summary>在 FTimeOfDayRange 的 [TimeMin, TimeMax] 范围内返回一个随机时间值。</summary>
        public static float GetRandomTimeInRange(FTimeOfDayRange timeOfDay)
        {
            lock (_globalRandom)
            {
                return (float)(_globalRandom.NextDouble() * (timeOfDay.TimeMax - timeOfDay.TimeMin)) + timeOfDay.TimeMin;
            }
        }

        /// <summary>使用指定随机流在 FTimeOfDayRange 范围内返回一个随机时间值。对应 UE5 FRandomStream 版本。</summary>
        public static float GetRandomTimeInRangeFromStream(FTimeOfDayRange timeOfDay, Random stream)
        {
            if (stream == null)
            {
                NarrativeLog.LogWarning("[TimeOfDay] GetRandomTimeInRangeFromStream 收到空随机流，回退到全局随机。");
                return GetRandomTimeInRange(timeOfDay);
            }
            return (float)(stream.NextDouble() * (timeOfDay.TimeMax - timeOfDay.TimeMin)) + timeOfDay.TimeMin;
        }

        /// <summary>将 FTimeOfDay 转为 double。</summary>
        public static double Conv_TimeOfDayToDouble(FTimeOfDay inTimeOfDay) => (float)inTimeOfDay;

        /// <summary>将 FTimeOfDay 转为 float。</summary>
        public static float Conv_TimeOfDayToFloat(FTimeOfDay inTimeOfDay) => (float)inTimeOfDay;

        /// <summary>将 double 转为 FTimeOfDay。</summary>
        public static FTimeOfDay Conv_DoubleToTimeOfDay(double inValue) => new FTimeOfDay((float)inValue);

        /// <summary>将 float 转为 FTimeOfDay。</summary>
        public static FTimeOfDay Conv_FloatToTimeOfDay(float inValue) => new FTimeOfDay(inValue);

        /// <summary>将 24 小时制时间转换为 12 小时制。返回 12 小时值与是否为下午（PM）。</summary>
        public static void To12Hour(FTimeOfDay inTimeOfDay, out float timeOut, out bool bPM)
        {
            float time = (float)inTimeOfDay;
            if (time < 1f)
            {
                timeOut = 12f;
                bPM = false;
                return;
            }

            bPM = time > 12f;
            if (bPM)
            {
                timeOut = time - 12f;
                return;
            }

            timeOut = time;
        }

        // ===== FTimeOfDay 与 FTimeOfDay =====

        /// <summary>A &lt; B</summary>
        public static bool Less_TimeOfDayTimeOfDay(FTimeOfDay a, FTimeOfDay b) => a < b;

        /// <summary>A &gt; B</summary>
        public static bool Greater_TimeOfDayTimeOfDay(FTimeOfDay a, FTimeOfDay b) => a > b;

        /// <summary>A &lt;= B</summary>
        public static bool LessEqual_TimeOfDayTimeOfDay(FTimeOfDay a, FTimeOfDay b) => a <= b;

        /// <summary>A &gt;= B</summary>
        public static bool GreaterEqual_TimeOfDayTimeOfDay(FTimeOfDay a, FTimeOfDay b) => a >= b;

        /// <summary>A == B</summary>
        public static bool EqualEqual_TimeOfDayTimeOfDay(FTimeOfDay a, FTimeOfDay b) => a == b;

        /// <summary>A != B</summary>
        public static bool NotEqual_TimeOfDayTimeOfDay(FTimeOfDay a, FTimeOfDay b) => a != b;

        /// <summary>A - B</summary>
        public static FTimeOfDay Subtract_TimeOfDayTimeOfDay(FTimeOfDay a, FTimeOfDay b) => new FTimeOfDay((float)a - (float)b);

        /// <summary>A + B</summary>
        public static FTimeOfDay Add_TimeOfDayTimeOfDay(FTimeOfDay a, FTimeOfDay b) => new FTimeOfDay((float)a + (float)b);

        // ===== FTimeOfDay 与 double =====

        /// <summary>A &lt; B</summary>
        public static bool Less_TimeOfDayDouble(FTimeOfDay a, double b) => (float)a < b;

        /// <summary>A &gt; B</summary>
        public static bool Greater_TimeOfDayDouble(FTimeOfDay a, double b) => (float)a > b;

        /// <summary>A &lt;= B</summary>
        public static bool LessEqual_TimeOfDayDouble(FTimeOfDay a, double b) => (float)a <= b;

        /// <summary>A &gt;= B</summary>
        public static bool GreaterEqual_TimeOfDayDouble(FTimeOfDay a, double b) => (float)a >= b;

        /// <summary>A == B</summary>
        public static bool EqualEqual_TimeOfDayDouble(FTimeOfDay a, double b) => Math.Abs((float)a - b) < double.Epsilon;

        /// <summary>A != B</summary>
        public static bool NotEqual_TimeOfDayDouble(FTimeOfDay a, double b) => Math.Abs((float)a - b) >= double.Epsilon;

        /// <summary>A - B</summary>
        public static FTimeOfDay Subtract_TimeOfDayDouble(FTimeOfDay a, double b) => new FTimeOfDay((float)((float)a - b));

        /// <summary>A + B</summary>
        public static FTimeOfDay Add_TimeOfDayDouble(FTimeOfDay a, double b) => new FTimeOfDay((float)((float)a + b));

        // ===== double 与 FTimeOfDay =====
        // 注意：与 UE5 源一致，参数顺序为 (B, A)，逻辑体为 A op B。

        /// <summary>A &lt; B（参数顺序 B, A）</summary>
        public static bool Less_DoubleTimeOfDay(double b, FTimeOfDay a) => (float)a < b;

        /// <summary>A &gt; B（参数顺序 B, A）</summary>
        public static bool Greater_DoubleTimeOfDay(double b, FTimeOfDay a) => (float)a > b;

        /// <summary>A &lt;= B（参数顺序 B, A）</summary>
        public static bool LessEqual_DoubleTimeOfDay(double b, FTimeOfDay a) => (float)a <= b;

        /// <summary>A &gt;= B（参数顺序 B, A）</summary>
        public static bool GreaterEqual_DoubleTimeOfDay(double b, FTimeOfDay a) => (float)a >= b;

        /// <summary>A == B（参数顺序 B, A）</summary>
        public static bool EqualEqual_DoubleTimeOfDay(double b, FTimeOfDay a) => Math.Abs((float)a - b) < double.Epsilon;

        /// <summary>A != B（参数顺序 B, A）</summary>
        public static bool NotEqual_DoubleTimeOfDay(double b, FTimeOfDay a) => Math.Abs((float)a - b) >= double.Epsilon;

        /// <summary>A - B（参数顺序 B, A），返回 double</summary>
        public static double Subtract_DoubleTimeOfDay(double b, FTimeOfDay a) => (float)a - b;

        /// <summary>A + B（参数顺序 B, A），返回 double</summary>
        public static double Add_DoubleTimeOfDay(double b, FTimeOfDay a) => (float)a + b;

        // ===== FTimeOfDay 与 float =====

        /// <summary>A &lt; B</summary>
        public static bool Less_TimeOfDayFloat(FTimeOfDay a, float b) => (float)a < b;

        /// <summary>A &gt; B</summary>
        public static bool Greater_TimeOfDayFloat(FTimeOfDay a, float b) => (float)a > b;

        /// <summary>A &lt;= B</summary>
        public static bool LessEqual_TimeOfDayFloat(FTimeOfDay a, float b) => (float)a <= b;

        /// <summary>A &gt;= B</summary>
        public static bool GreaterEqual_TimeOfDayFloat(FTimeOfDay a, float b) => (float)a >= b;

        /// <summary>A == B</summary>
        public static bool EqualEqual_TimeOfDayFloat(FTimeOfDay a, float b) => Math.Abs((float)a - b) < float.Epsilon;

        /// <summary>A != B</summary>
        public static bool NotEqual_TimeOfDayFloat(FTimeOfDay a, float b) => Math.Abs((float)a - b) >= float.Epsilon;

        /// <summary>A - B</summary>
        public static FTimeOfDay Subtract_TimeOfDayFloat(FTimeOfDay a, float b) => new FTimeOfDay((float)a - b);

        /// <summary>A + B</summary>
        public static FTimeOfDay Add_TimeOfDayFloat(FTimeOfDay a, float b) => new FTimeOfDay((float)a + b);

        // ===== float 与 FTimeOfDay =====
        // 注意：与 UE5 源一致，参数顺序为 (B, A)，逻辑体为 A op B。

        /// <summary>A &lt; B（参数顺序 B, A）</summary>
        public static bool Less_FloatTimeOfDay(float b, FTimeOfDay a) => (float)a < b;

        /// <summary>A &gt; B（参数顺序 B, A）</summary>
        public static bool Greater_FloatTimeOfDay(float b, FTimeOfDay a) => (float)a > b;

        /// <summary>A &lt;= B（参数顺序 B, A）</summary>
        public static bool LessEqual_FloatTimeOfDay(float b, FTimeOfDay a) => (float)a <= b;

        /// <summary>A &gt;= B（参数顺序 B, A）</summary>
        public static bool GreaterEqual_FloatTimeOfDay(float b, FTimeOfDay a) => (float)a >= b;

        /// <summary>A == B（参数顺序 B, A）</summary>
        public static bool EqualEqual_FloatTimeOfDay(float b, FTimeOfDay a) => Math.Abs((float)a - b) < float.Epsilon;

        /// <summary>A != B（参数顺序 B, A）</summary>
        public static bool NotEqual_FloatTimeOfDay(float b, FTimeOfDay a) => Math.Abs((float)a - b) >= float.Epsilon;

        /// <summary>A - B（参数顺序 B, A），返回 float</summary>
        public static float Subtract_FloatTimeOfDay(float b, FTimeOfDay a) => (float)a - b;

        /// <summary>A + B（参数顺序 B, A），返回 float</summary>
        public static float Add_FloatTimeOfDay(float b, FTimeOfDay a) => (float)a + b;
    }
}
