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

}
