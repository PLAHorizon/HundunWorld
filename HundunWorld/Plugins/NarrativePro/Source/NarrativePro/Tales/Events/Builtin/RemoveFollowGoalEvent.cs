using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.AI.Activities;
using NarrativePro.Core;

namespace NarrativePro.Tales.Events.Builtin
{
    /// <summary>
    /// 移除 NPC 跟随目标事件。
    /// <para>
    /// UE5 依据（Content/Pro/Core/Tales/Events/BPE_RemoveFollowGoal.uasset 符号表）：
    /// CallFunc 含 <c>GetActivityComponent</c>、<c>GetGoals</c>、<c>Array_Get</c>/<c>Array_Length</c>/
    /// <c>Less_IntInt</c>（倒序循环）、<c>EqualEqual_ObjectObject</c> + <c>BooleanAND</c>/<c>Not_PreBool</c>
    /// （比较每个跟随目标的跟随对象），BP 变量 <c>FollowTargetToRemove</c>，
    /// EventFilter=<c>EF_OnlyNPCs</c>，目标类型为示例内容里的 <c>Goal_FollowCharacter</c>
    /// （UE5 侧 UNPCActivityComponent::GetGoals/RemoveGoal，NPCActivityComponent.h:149-161）。
    /// </para>
    /// <para>
    /// 端口差异：Follow 目标类是 UE5 示例内容资产，端口没有内建跟随目标类型
    /// （端口具体目标类仅 AI/Activities/NPCGoalItem.cs:261 的 NPCGoal_Location），
    /// 因此按端口真实 API 实现：以 <see cref="GoalTypePath"/>（AddGoal 用的类型 FullName 键，
    /// 见 NPCActivityComponent.cs:543-548）限定目标容器，空则扫全部容器；
    /// <see cref="FollowTargetName"/> 匹配 <c>NPCGoalItem.GoalActor</c> 的 Actor 名，空则该类型目标全清。
    /// </para>
    /// </summary>
    public class RemoveFollowGoalEvent : NarrativeEvent
    {
        /// <summary>目标 CLR 类型路径；空 = 扫描所有类型。对应 UE5 GetGoals(Goal_FollowCharacter)。</summary>
        public string GoalTypePath { get; set; } = "";

        /// <summary>要解除跟随的 Actor 名；空 = 移除该类型全部跟随目标。对应 UE5 <c>FollowTargetToRemove</c>。</summary>
        public string FollowTargetName { get; set; } = "";

        /// <summary>对应 UE5 资产 EventFilter 默认 EF_OnlyNPCs（事件跑在 NPC 身上）。</summary>
        public RemoveFollowGoalEvent()
        {
            EventFilter = EEventFilter.OnlyNPCs;
        }

        public override void ExecuteEvent(object target, object controller, object narrativeComponent)
        {
            var activity = ResolveActivityComponent(target, controller);
            if (activity == null)
            {
                NarrativeLog.LogWarning("RemoveFollowGoalEvent: 目标 NPC 没有 NPCActivityComponent，跳过");
                return;
            }

            // GetGoals 返回的是活动组件内的活容器，边遍历边 RemoveGoal 会破坏迭代，先快照再删
            var toRemove = new List<NPCGoalItem>();
            foreach (var goal in EnumerateGoals(activity))
            {
                if (goal == null) continue;
                if (string.IsNullOrEmpty(FollowTargetName) ||
                    string.Equals(goal.GoalActor?.Name, FollowTargetName, System.StringComparison.Ordinal))
                {
                    toRemove.Add(goal);
                }
            }

            foreach (var goal in toRemove)
                activity.RemoveGoal(goal);

            if (toRemove.Count > 0)
                NarrativeLog.Log($"RemoveFollowGoalEvent: 已移除 {toRemove.Count} 个跟随目标（类型 '{GoalTypePath}'，跟随对象 '{FollowTargetName}'）");
            else
                NarrativeLog.LogWarning($"RemoveFollowGoalEvent: 未找到匹配的跟随目标（类型 '{GoalTypePath}'，跟随对象 '{FollowTargetName}'）");
        }

        private IEnumerable<NPCGoalItem> EnumerateGoals(NPCActivityComponent activity)
        {
            if (!string.IsNullOrEmpty(GoalTypePath))
            {
                var container = activity.GetGoals(GoalTypePath);
                return container?.Goals ?? new List<NPCGoalItem>();
            }

            var all = new List<NPCGoalItem>();
            foreach (var pair in activity.Goals)
            {
                if (pair.Value?.Goals != null)
                    all.AddRange(pair.Value.Goals);
            }
            return all;
        }

        private static NPCActivityComponent ResolveActivityComponent(object target, object controller)
        {
            // 端口 NPC 的活动组件直接挂在 NPC Actor 上（AI/NarrativeCharacterSubsystem.cs:425-428 的装配口径）
            if (target is Actor targetActor)
            {
                var comp = targetActor.GetScript<NPCActivityComponent>();
                if (comp != null) return comp;
            }
            if (controller is Actor controllerActor)
                return controllerActor.GetScript<NPCActivityComponent>();
            return null;
        }

        public override string GetGraphDisplayText() => $"Remove Follow Goal: {FollowTargetName}";
        public override string GetHintText() => "解除跟随";
    }
}
