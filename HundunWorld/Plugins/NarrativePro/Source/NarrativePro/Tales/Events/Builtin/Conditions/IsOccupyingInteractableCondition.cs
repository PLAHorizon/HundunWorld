using FlaxEngine;
using NarrativePro.Core;

namespace NarrativePro.Tales.Events.Builtin.Conditions
{
    /// <summary>
    /// NPC 是否正占用某可交互对象条件。对应 UE5 蓝图资产 <c>BPC_IsOccupyingInteractable</c>。
    /// <para>
    /// 二进制证据：CheckCondition 图为 DynamicCast 到 Narrative_NPCCharacter →
    /// <c>CallFunc_GetNPCController</c> → <c>CallFunc_GetInteractionComponent</c> →
    /// 读 <c>OccupiedInteractable</c>，再用 <c>CallFunc_EqualEqual_ObjectObject</c> 与蓝图变量
    /// <c>InteractableToFind</c> 比对。ConditionFilter 默认 CF_OnlyNPCs。
    /// <c>端口差异</c>：端口 <c>NarrativeInteractionComponent.OccupiedInteractable</c> 是
    /// <c>NarrativeInteractableComponent</c>（挂在 Actor 上），端口按占用对象所属 Actor 名匹配
    /// <c>InteractableToFind</c>；<c>InteractableToFind</c> 为空时退化为「是否占用任意交互物」。
    /// </para>
    /// </summary>
    public class IsOccupyingInteractableCondition : NarrativeCondition
    {
        /// <summary>目标交互对象 Actor 名称。对应 UE5 蓝图变量 <c>InteractableToFind</c>。</summary>
        public string InteractableToFind { get; set; } = "";

        public IsOccupyingInteractableCondition()
        {
            ConditionFilter = EConditionFilter.OnlyNPCs;
        }

        public override bool CheckCondition(object target, object controller, TalesComponent narrativeComponent)
        {
            var character = ArsenalStatics.ResolveCharacterFromContext(target, controller);
            var interaction = character?.GetInteractionComponent();
            if (interaction == null)
                return false;

            var occupied = interaction.OccupiedInteractable;
            if (occupied == null)
                return false;

            if (string.IsNullOrEmpty(InteractableToFind))
                return true;

            return occupied.Actor != null && occupied.Actor.Name == InteractableToFind;
        }

        public override string GetGraphDisplayText() => $"Is Occupying Interactable: {InteractableToFind}";
    }
}
