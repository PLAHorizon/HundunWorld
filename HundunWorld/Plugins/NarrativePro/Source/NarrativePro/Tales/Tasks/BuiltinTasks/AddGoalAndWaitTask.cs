using System;
using FlaxEngine;
using NarrativePro.AI;
using NarrativePro.AI.Activities;
using NarrativePro.Core;

namespace NarrativePro.Tales.Tasks
{
    /// <summary>
    /// 「给 NPC 加一条目标并等它做完」的任务条目。对应 UE5 蓝图任务
    /// <c>BPT_AddGoalAndWait</c>（Content/Pro/Core/Tales/Tasks/BPT_AddGoalAndWait.uasset）。
    /// <para>
    /// 二进制证据：图里 <c>K2Node_GetSubsystem</c> → <c>CallFunc_GetWorldSubsystem_ReturnValue</c>
    /// （<c>NarrativeCharacterSubsystem</c>）→ <c>CallFunc_FindNPC_ReturnValue</c> 找 NPC，
    /// <c>CallFunc_GetActivityComponent_ReturnValue</c> 取 <c>NPCActivityComponent</c>，
    /// 再用 <c>K2Node_AsyncAction</c>（<c>AsyncAction_AddGoalAndWait</c>，属性
    /// <c>GoalToAdd</c>/<c>bTriggerReselect</c>/<c>bPersistant</c>，输出
    /// <c>CallFunc_AddGoalAndWait_Goal</c>）等 <c>GoalSucceeded</c> / <c>GoalRemoved</c>
    /// 两个委托（<c>K2Node_CreateDelegate_OutputDelegate</c>），成功分支走
    /// <c>CompleteTask</c>，另配 <c>GoalToRemove</c> 变量与 <c>K2_EndTask</c> 里的
    /// <c>RemoveGoal</c>；<c>MarkerClass</c>/<c>BP_ObjectiveMark</c> 说明会挂目标标记。
    /// </para>
    /// <para>
    /// 端口的异步动作与目标项均已存在：<see cref="AsyncAction_AddGoalAndWait"/> 与
    /// <see cref="NPCActivityFactory.CreateGoal"/>。UE5 资产里 Instanced 的
    /// <c>UNPCGoalItem</c> 在端口换成「目标 CLR 类型名 + 少量可配字段」，
    /// 因为 <c>NPCGoalItem</c> 是抽象类（NPCGoalItem.cs:22），JSON 无法直接实例化。
    /// </para>
    /// </summary>
    [Serializable]
    public class AddGoalAndWaitTask : NarrativeTask
    {
        /// <summary>目标 NPC 的提供者（UE5 图里是 FindNPC，端口优先用提供者）。</summary>
        public NarrativePro.Core.NarrativeActorProvider NpcProvider { get; set; }

        /// <summary>按 NPC 定义 ID 找目标。对应图里的 <c>FindNPC</c> 入参。</summary>
        public string NpcId { get; set; } = "";

        /// <summary>目标项类型名（如 <c>NPCGoal_Location</c>）。对应 Instanced 的 <c>UNPCGoalItem</c>。</summary>
        public string GoalClassPath { get; set; } = "NPCGoal_Location";

        /// <summary>目标键（同键不重复添加）。对应 <c>NPCGoalItem.GoalKey</c>。</summary>
        public string GoalKey { get; set; } = "";

        /// <summary>目标关注位置。对应 <c>NPCGoalItem.GoalLocation</c>。</summary>
        public Vector3 GoalLocation { get; set; } = Vector3.Zero;

        /// <summary>目标评分。对应 <c>NPCGoalItem.DefaultScore</c>。</summary>
        public float GoalScore { get; set; } = 1f;

        /// <summary>添加目标后是否立刻让组件重选行为。对应 <c>bTriggerReselect</c>。</summary>
        public bool bTriggerReselect { get; set; } = true;

        /// <summary>任务结束时要把哪条目标摘掉（按类型名 + 键）。对应 <c>GoalToRemove</c>。</summary>
        public string GoalToRemoveClassPath { get; set; } = "";

        public string GoalToRemoveKey { get; set; } = "";

        private NPCGoalItem _addedGoal;
        private NPCActivityComponent _activityComponent;
        private AsyncAction_AddGoalAndWait _action;

        protected override void OnBeginTask()
        {
            var npc = BuiltinTaskSupport.ResolveTargetActor(NpcProvider, NpcId, Guid.Empty);
            if (npc == null)
            {
                NarrativeLog.LogWarning($"AddGoalAndWaitTask: 找不到 NPC（NpcId='{NpcId}'），无法添加目标");
                return;
            }

            var controller = npc.GetScript<NarrativeNPCController>();
            _activityComponent = controller != null ? controller.GetActivityComponent() : npc.GetScript<NPCActivityComponent>();
            if (_activityComponent == null)
            {
                NarrativeLog.LogWarning($"AddGoalAndWaitTask: NPC '{npc.Name}' 没有 NPCActivityComponent");
                return;
            }

            var goal = NPCActivityFactory.CreateGoal(GoalClassPath);
            if (goal == null)
            {
                NarrativeLog.LogWarning($"AddGoalAndWaitTask: 无法创建目标项 '{GoalClassPath}'");
                return;
            }

            if (!string.IsNullOrEmpty(GoalKey)) goal.GoalKey = GoalKey;
            goal.GoalLocation = GoalLocation;
            goal.DefaultScore = GoalScore;

            var action = AsyncAction_AddGoalAndWait.AddGoalAndWait(_activityComponent, goal, bTriggerReselect, false, out _addedGoal);
            if (action == null)
            {
                NarrativeLog.LogWarning("AddGoalAndWaitTask: AddGoalAndWait 未能启动（组件或目标为空）");
                return;
            }

            _action = action;
            _action.GoalSucceeded += HandleGoalSucceeded;
            _action.GoalRemoved += HandleGoalRemoved;
        }

        private void HandleGoalSucceeded(NPCActivity activity)
        {
            if (!bIsActive) return;
            CompleteTask();
        }

        private void HandleGoalRemoved(NPCActivity activity)
        {
            if (!bIsActive) return;
            // UE5 的 GoalRemoved 分支不推进进度：目标被别的活动/清理摘走时本任务只能继续等下一条。
            NarrativeLog.Log("AddGoalAndWaitTask: 目标在完成前被移除");
        }

        protected override void OnEndTask()
        {
            if (_action != null)
            {
                _action.GoalSucceeded -= HandleGoalSucceeded;
                _action.GoalRemoved -= HandleGoalRemoved;
                _action.EndTask();
                _action = null;
            }

            // 对应图里 K2_EndTask 的 RemoveGoal：任务结束不该把目标留在 NPC 身上。
            if (_activityComponent != null)
            {
                if (_addedGoal != null)
                    _activityComponent.RemoveGoal(_addedGoal);

                if (!string.IsNullOrEmpty(GoalToRemoveClassPath))
                {
                    var typePath = NPCActivityFactory.ResolveGoalType(GoalToRemoveClassPath)?.FullName ?? GoalToRemoveClassPath;
                    var goals = _activityComponent.GetGoals(typePath);

                    // RemoveGoal 会改容器里的列表，先快照再摘。
                    var snapshot = goals?.Goals != null ? new System.Collections.Generic.List<NPCGoalItem>(goals.Goals) : null;
                    if (snapshot != null)
                    {
                        foreach (var goal in snapshot)
                        {
                            if (goal != null && goal.GetGoalKey() == GoalToRemoveKey)
                                _activityComponent.RemoveGoal(goal);
                        }
                    }
                }
            }

            _addedGoal = null;
            _activityComponent = null;
        }

        public override string GetTaskDescription()
        {
            if (!string.IsNullOrEmpty(DescriptionOverride)) return DescriptionOverride;
            return string.IsNullOrEmpty(NpcId) ? "让 NPC 完成一条目标" : $"让 {NpcId} 完成一条目标";
        }
    }
}
