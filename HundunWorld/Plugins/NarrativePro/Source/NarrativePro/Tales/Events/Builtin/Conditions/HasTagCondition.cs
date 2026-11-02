using FlaxEngine;
using NarrativePro.Core;
using NarrativePro.GAS;
using NarrativePro.Items;

namespace NarrativePro.Tales.Events.Builtin.Conditions
{
    /// <summary>
    /// 标签条件。对应 UE5 <c>UNarrativeCondition_HasTag</c>：读档时若角色持有（或不再持有）指定 gameplay 标签则成立。
    /// </summary>
    public class HasTagCondition : NarrativeCondition
    {
        /// <summary>要检查的标签。对应 UE5 <c>Tag</c>。</summary>
        public string Tag { get; set; } = "";

        /// <summary>为 true 时要求标签不存在（“没有该标签”才成立）。对应 UE5 <c>bHasTag</c> 语义位。</summary>
        public bool bTagShouldBePresent { get; set; } = true;

        public override bool CheckCondition(object target, object controller, TalesComponent narrativeComponent)
        {
            var tag = new GameplayTag(Tag);
            if (!tag.IsValid())
            {
                NarrativeLog.LogWarning("HasTagCondition: Tag 为空，条件按失败处理");
                return false;
            }

            // UE5 依次尝试：条件自带的 owner ASC -> pawn ASC -> controller ASC。
            var asc = ResolveAsc(target, controller);
            if (asc == null)
                return !bTagShouldBePresent;

            bool hasTag = asc.HasMatchingGameplayTag(tag);
            return hasTag == bTagShouldBePresent;
        }

        public override string GetGraphDisplayText() => $"{(bTagShouldBePresent ? "Has" : "Lacks")} Tag: {Tag}";

        private static NarrativeAbilitySystemComponent ResolveAsc(object target, object controller)
        {
            var asc = ArsenalStatics.GetNarrativeAbilitySystemComponent(target as Actor);
            if (asc == null)
                asc = ArsenalStatics.GetNarrativeAbilitySystemComponent(controller as Actor);
            return asc;
        }
    }
}
