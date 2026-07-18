using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.AI.Activities;
using NarrativePro.Core;
using NarrativePro.Tales.Events;

namespace NarrativePro.AI
{
    /// <summary>
    /// 向 NPC 添加目标的事件。对应 UE5 UNarrativeEvent_AddGoalToNPC。
    /// 将目标发送给指定 NPC。建议使用 BP 事件绑定以获取创建的目标引用，便于后续更新或移除。
    /// </summary>
    public class NarrativeEvent_AddGoalToNPC : NarrativeEvent
    {
        /// <summary>要发送给 NPC 的目标（实例化引用）</summary>
        public NPCGoalItem GoalToAdd;

        /// <summary>当前活跃的目标（运行时设置）</summary>
        [NonSerialized]
        public NPCGoalItem ActiveGoal;

        /// <summary>
        /// 执行事件：向目标 NPC 添加目标。
        /// </summary>
        /// <param name="target">目标对象（Actor/Pawn）</param>
        /// <param name="controller">控制器</param>
        /// <param name="narrativeComponent">叙事组件</param>
        public override void ExecuteEvent(object target, object controller, object narrativeComponent)
        {
            if (GoalToAdd == null)
            {
                NarrativeLog.LogWarning("NarrativeEvent_AddGoalToNPC: GoalToAdd 为空，无法添加目标");
                return;
            }

            // 尝试从目标获取活动组件
            var actor = target as Actor;
            var activityComp = actor?.GetScript<NPCActivityComponent>();
            if (activityComp == null)
            {
                NarrativeLog.LogWarning("NarrativeEvent_AddGoalToNPC: 未找到 NPCActivityComponent");
                return;
            }

            ActiveGoal = activityComp.AddGoal(GoalToAdd, true);
            NarrativeLog.Log($"NarrativeEvent_AddGoalToNPC: 已向 NPC 添加目标 {GoalToAdd.GetGoalKey()}");
        }

        /// <summary>事件激活时调用</summary>
        public override void OnActivate(object target, object controller, object narrativeComponent)
        {
            // 激活时无额外逻辑（ExecuteEvent 负责添加目标）
        }

        /// <summary>事件停用时调用</summary>
        public override void OnDeactivate(object target, object controller, object narrativeComponent)
        {
            // 停用时移除已添加的目标
            if (ActiveGoal != null)
            {
                var actor = target as Actor;
                var activityComp = actor?.GetScript<NPCActivityComponent>();
                activityComp?.RemoveGoal(ActiveGoal);
                ActiveGoal = null;
            }
        }

        /// <summary>获取图表显示文本</summary>
        public override string GetGraphDisplayText()
        {
            string goalName = GoalToAdd != null ? GoalToAdd.GetType().Name : "无";
            return $"添加目标到 NPC: {goalName}";
        }
    }

    /// <summary>
    /// 多目标添加的目标项。对应 UE5 FAddGoalMultiTarget。
    /// 表示多目标添加事件中的一个 NPC 及其对应目标。
    /// </summary>
    [Serializable]
    public class FAddGoalMultiTarget
    {
        /// <summary>要运行此目标的 NPC 定义</summary>
        public NPCDefinition NPCDefinition;

        /// <summary>在指定时间运行的目标</summary>
        public NPCGoalItem Goal;

        /// <summary>
        /// 若为 false，即使此 NPC 未能启动活动，添加目标事件仍可继续运行。
        /// </summary>
        public bool bRequireSucceed = true;

        /// <summary>跟踪目标是否已成功完成</summary>
        [NonSerialized]
        public bool bGoalSucceeded = false;

        public FAddGoalMultiTarget()
        {
            bRequireSucceed = true;
            bGoalSucceeded = false;
        }
    }

    /// <summary>
    /// 向多个 NPC 添加目标的事件。对应 UE5 UNarrativeEvent_AddGoalMulti。
    /// 处理向多个 NPC 添加目标的特殊版本。覆盖 OnGoalsCompleted 以在所有目标完成时执行操作。
    /// </summary>
    public class NarrativeEvent_AddGoalMulti : NarrativeEvent
    {
        /// <summary>已发出的活跃目标列表</summary>
        [NonSerialized]
        public List<NPCGoalItem> IssuedGoals = new List<NPCGoalItem>();

        /// <summary>此添加目标事件影响的 NPC 及其目标列表</summary>
        public List<FAddGoalMultiTarget> NPCGoalTargets = new List<FAddGoalMultiTarget>();

        /// <summary>
        /// 活动完成时调用。对应 UE5 OnActivityCompleted。
        /// </summary>
        /// <param name="activity">完成的活动</param>
        /// <param name="goal">完成的目标</param>
        public virtual void OnActivityCompleted(NPCActivity activity, NPCGoalItem goal)
        {
            if (goal == null) return;

            // 标记对应目标为已成功
            foreach (var target in NPCGoalTargets)
            {
                if (target.Goal == goal)
                {
                    target.bGoalSucceeded = true;
                    break;
                }
            }

            // 检查是否所有需要成功的目标都已完成
            CheckAllGoalsCompleted();
        }

        /// <summary>
        /// 所有目标完成时调用。对应 UE5 OnGoalsCompleted。
        /// 覆盖此方法以在所有目标完成时执行操作。
        /// </summary>
        public virtual void OnGoalsCompleted()
        {
            NarrativeLog.Log("NarrativeEvent_AddGoalMulti: 所有目标已完成");
        }

        /// <summary>
        /// 执行事件：向多个 NPC 添加目标。
        /// </summary>
        public override void ExecuteEvent(object target, object controller, object narrativeComponent)
        {
            foreach (var goalTarget in NPCGoalTargets)
            {
                if (goalTarget?.Goal == null || goalTarget.NPCDefinition == null) continue;

                // 通过 NarrativeCharacterSubsystem 查找场景中的 NPC 并添加目标
                var npcActor = NarrativeCharacterSubsystem.Instance?.FindNPC(goalTarget.NPCDefinition);
                var activityComp = npcActor?.GetScript<NPCActivityComponent>();
                if (activityComp != null)
                {
                    var issuedGoal = activityComp.AddGoal(goalTarget.Goal, true);
                    if (issuedGoal != null)
                    {
                        IssuedGoals.Add(issuedGoal);
                    }
                    NarrativeLog.Log($"NarrativeEvent_AddGoalMulti: 向 NPC {goalTarget.NPCDefinition.NPCName} 添加目标");
                }
                else
                {
                    NarrativeLog.LogWarning($"NarrativeEvent_AddGoalMulti: 未找到 NPC {goalTarget.NPCDefinition.NPCName} 或其 NPCActivityComponent");
                }
            }
        }

        /// <summary>事件激活时调用</summary>
        public override void OnActivate(object target, object controller, object narrativeComponent)
        {
            // 激活时无额外逻辑（ExecuteEvent 负责添加目标）
        }

        /// <summary>事件停用时调用</summary>
        public override void OnDeactivate(object target, object controller, object narrativeComponent)
        {
            // 停用时移除所有已发出的目标
            foreach (var goalTarget in NPCGoalTargets)
            {
                if (goalTarget?.Goal == null || goalTarget.NPCDefinition == null) continue;
                var npcActor = NarrativeCharacterSubsystem.Instance?.FindNPC(goalTarget.NPCDefinition);
                var activityComp = npcActor?.GetScript<NPCActivityComponent>();
                activityComp?.RemoveGoal(goalTarget.Goal);
            }
            IssuedGoals.Clear();
        }

        /// <summary>获取图表显示文本</summary>
        public override string GetGraphDisplayText()
        {
            return $"添加多个目标 ({NPCGoalTargets?.Count ?? 0} 个 NPC)";
        }

        /// <summary>检查所有需要成功的目标是否已完成</summary>
        private void CheckAllGoalsCompleted()
        {
            foreach (var target in NPCGoalTargets)
            {
                if (target.bRequireSucceed && !target.bGoalSucceeded)
                {
                    return; // 仍有未完成的目标
                }
            }
            OnGoalsCompleted();
        }
    }
}
