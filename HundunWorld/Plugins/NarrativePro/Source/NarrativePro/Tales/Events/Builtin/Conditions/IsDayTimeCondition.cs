using NarrativePro.Core;

namespace NarrativePro.Tales.Events.Builtin.Conditions
{
    /// <summary>
    /// 白天判定条件。对应 UE5 蓝图资产 <c>NC_IsDayTime</c>。
    /// <para>
    /// 二进制证据：uasset 的 CheckCondition 图为 <c>CallFunc_GetGameState</c>（DynamicCast 到
    /// NarrativeGameState）→ <c>CallFunc_IsDayTime</c>，即直接委托 C++
    /// <c>UArsenalStatics::IsDayTime</c>（内部用 <c>IsTimeInRange(GetTimeOfDay, SunriseTime, SunsetTime)</c>）。
    /// 端口复用 <see cref="ArsenalStatics.IsDayTime"/>。
    /// </para>
    /// </summary>
    public class IsDayTimeCondition : NarrativeCondition
    {
        public override bool CheckCondition(object target, object controller, TalesComponent narrativeComponent)
        {
            return ArsenalStatics.IsDayTime(target);
        }

        public override string GetGraphDisplayText() => "Is Day Time";
    }
}
