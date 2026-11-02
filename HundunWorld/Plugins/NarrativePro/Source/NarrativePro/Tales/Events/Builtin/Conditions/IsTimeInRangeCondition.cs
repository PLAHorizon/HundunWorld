using NarrativePro.Core;

namespace NarrativePro.Tales.Events.Builtin.Conditions
{
    /// <summary>
    /// 游戏内时间区间条件。对应 UE5 蓝图资产 <c>NC_IsTimeInRange</c>。
    /// <para>
    /// 二进制证据：uasset 的 CheckCondition 图为 <c>CallFunc_GetTimeOfDay</c> →
    /// <c>CallFunc_FMin</c>/<c>CallFunc_FMax</c> 归一 → <c>CallFunc_InRange_FloatFloat</c>，
    /// 蓝图变量为 <c>RangeMin</c>/<c>RangeMax</c>；这与 C++ <c>UArsenalStatics::IsTimeInRange</c>
    /// （ArsenalStatics.cpp，支持跨天回绕）语义一致，端口复用
    /// <see cref="ArsenalStatics.IsTimeInRange"/> 而非重复实现区间分支。
    /// </para>
    /// </summary>
    public class IsTimeInRangeCondition : NarrativeCondition
    {
        /// <summary>区间起点（一天内时间，0-2400 记时制）。对应 UE5 蓝图变量 <c>RangeMin</c>。</summary>
        public float RangeMin { get; set; } = 0f;

        /// <summary>区间终点（一天内时间，0-2400 记时制）。对应 UE5 蓝图变量 <c>RangeMax</c>。</summary>
        public float RangeMax { get; set; } = 0f;

        public override bool CheckCondition(object target, object controller, TalesComponent narrativeComponent)
        {
            float time = ArsenalStatics.GetTimeOfDay(target);
            return ArsenalStatics.IsTimeInRange(time, RangeMin, RangeMax);
        }

        public override string GetGraphDisplayText() => $"Time In Range [{RangeMin}, {RangeMax}]";
    }
}
