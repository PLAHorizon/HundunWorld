using System;

namespace NarrativePro.Interaction
{
    /// <summary>
    /// 默认槽位行为：由能力驱动的交互（对应 UE5 挂在 <c>FInteractionSlotConfig.SlotInteractBehavior</c>
    /// 上的那些内联实例——上游把 <c>UInteractionSlotBehavior</c> 声明成可被子类化的基类，
    /// 具体行为由内容侧子类化并填 <c>SlotInteractBehavior</c>（InteractableComponent.h:79-92））。
    /// <para>
    /// 端口基类 <see cref="InteractionSlotBehavior"/> 是 abstract，而此前全仓没有任何子类，
    /// 于是这个作者面根本构造不出来：内容想让某个槽位"按下→授予并激活能力→能力结束时释放槽位"
    /// 无路可走，只能一律走事件广播（UE5 的载具座位、制作台这类交互正走能力通路）。
    /// 本类就是那条通路的可实例化落点：填 <see cref="InteractionSlotBehavior.SlotInteractBehaviorId"/>
    /// （GAS 能力 id）即可，类型由 <see cref="NarrativePro.GAS.NarrativeGASAssetRegistry"/> 换取。
    /// </para>
    /// </summary>
    [Serializable]
    public class AbilityInteractionSlotBehavior : InteractionSlotBehavior
    {
    }
}
