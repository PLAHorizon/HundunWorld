using FlaxEngine;
using NarrativePro.AI.Activities;
using NarrativePro.Core;

namespace NarrativePro.Tales.Events.Builtin.Conditions
{
    /// <summary>
    /// NPC 是否已接到「与某对象交互」目标条件。对应 UE5 蓝图资产 <c>BPC_HasInteractGoalFor</c>。
    /// <para>
    /// 二进制证据：CheckCondition 图为 <c>CallFunc_GetActivityComponent</c> →
    /// <c>CallFunc_GetGoals</c> → 遍历并 DynamicCast 成 <c>Goal_Interact</c>（AsGoal），
    /// 比对目标交互物与蓝图变量 <c>InteractableToFind</c>/<c>Interactable</c>；
    /// 另见 <c>CallFunc_LoadAsset_Blocking</c>/<c>IsValidSoftObjectReference</c> 解析软引用。ConditionFilter 默认 CF_OnlyNPCs。
    /// <c>端口差异</c>：Flax 端口未建模 <c>Goal_Interact</c> 子类型，端口在 NPC 全部活动目标里
    /// 比对 <c>GoalActor</c> 名称是否等于 <c>InteractableToFind</c>（端口以 Actor 名替代软对象引用）。
    /// </para>
    /// </summary>
    public class HasInteractGoalForCondition : NarrativeCondition
    {
        /// <summary>目标交互对象 Actor 名称。对应 UE5 蓝图变量 <c>InteractableToFind</c>。</summary>
        public string InteractableToFind { get; set; } = "";

        public HasInteractGoalForCondition()
        {
            ConditionFilter = EConditionFilter.OnlyNPCs;
        }

        public override bool CheckCondition(object target, object controller, TalesComponent narrativeComponent)
        {
            if (string.IsNullOrEmpty(InteractableToFind))
            {
                NarrativeLog.LogWarning("HasInteractGoalForCondition: InteractableToFind 为空，条件按失败处理");
                return false;
            }

            var npc = target as Actor;
            if (npc == null)
                return false;

            var activity = npc.GetScript<NPCActivityComponent>();
            if (activity == null)
                return false;

            foreach (var container in activity.Goals.Values)
            {
                if (container?.Goals == null)
                    continue;

                foreach (var goal in container.Goals)
                {
                    if (goal?.GoalActor != null && goal.GoalActor.Name == InteractableToFind)
                        return true;
                }
            }
            return false;
        }

        public override string GetGraphDisplayText() => $"Has Interact Goal For: {InteractableToFind}";
    }
}
