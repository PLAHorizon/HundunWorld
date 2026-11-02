using FlaxEngine;
using NarrativePro.AI;
using NarrativePro.AI.Activities;
using NarrativePro.Core;

namespace NarrativePro.Tales.Events.Builtin.Conditions
{
    /// <summary>
    /// NPC 是否已接到「跟随某角色」目标条件。对应 UE5 蓝图资产 <c>BPC_HasFollowGoalFor</c>。
    /// <para>
    /// 二进制证据：CheckCondition 图为 <c>CallFunc_GetActivityComponent</c> →
    /// <c>CallFunc_GetGoals</c> → 遍历数组 <c>CallFunc_Array_Get_Item</c> 并
    /// DynamicCast 成 <c>Goal_FollowCharacter</c>（AsGoal），再用
    /// <c>CallFunc_EqualEqual_ObjectObject</c> 比较其跟随角色与蓝图变量
    /// <c>RequiredFollowGoalCharacter</c>/<c>FollowCharacter</c>；ConditionFilter 默认 CF_OnlyNPCs。
    /// <c>端口差异</c>：Flax 端口未建模 <c>Goal_FollowCharacter</c> 子类型（仅有 <c>NPCGoalItem.GoalActor</c>），
    /// 端口在 NPC 活动组件的全部目标里比对 <c>GoalActor</c> 是否等于被跟随角色的 Actor。
    /// </para>
    /// </summary>
    public class HasFollowGoalForCondition : NarrativeCondition
    {
        /// <summary>被跟随角色的 NPC ID。对应 UE5 蓝图变量 <c>RequiredFollowGoalCharacter</c>（CharacterDefinition，端口按 ID 解析）。</summary>
        public string FollowCharacterNpcId { get; set; } = "";

        public HasFollowGoalForCondition()
        {
            ConditionFilter = EConditionFilter.OnlyNPCs;
        }

        public override bool CheckCondition(object target, object controller, TalesComponent narrativeComponent)
        {
            if (string.IsNullOrEmpty(FollowCharacterNpcId))
            {
                NarrativeLog.LogWarning("HasFollowGoalForCondition: FollowCharacterNpcId 为空，条件按失败处理");
                return false;
            }

            var npc = target as Actor;
            if (npc == null)
                return false;

            var followed = NarrativeCharacterSubsystem.Instance?.FindNPCByID(FollowCharacterNpcId);
            if (followed == null)
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
                    if (goal != null && goal.GoalActor == followed)
                        return true;
                }
            }
            return false;
        }

        public override string GetGraphDisplayText() => $"Has Follow Goal For: {FollowCharacterNpcId}";
    }
}
