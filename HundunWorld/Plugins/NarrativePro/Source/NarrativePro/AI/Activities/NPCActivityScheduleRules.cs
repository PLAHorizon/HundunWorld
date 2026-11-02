// 模块关系说明：
// 本文件属于 NarrativePro.AI.Activities，是「NPC 活动调度绑定与调度目标」的纯接缝：
//   - 一条调度行为要不要绑定（bDisabled / 空行为）；
//   - 到点后交给组件的那个目标该怎么准备（ScoreOverride、bSaveGoal、IntendedTODStartTime、
//     已有 ActiveGoal 时不再重复启动）；
//   - 出点时要不要移除目标；
//   - 移除某份调度时，活跃行为列表里哪些属于它（CreatedFromSchedule 归属判定）。
// 规则本体在这里，**不引用 FlaxEngine 类型**；实时侧（ScheduledBehavior_AddNPCGoal.HandleStarted/
// HandleEnded 与 NPCActivityComponent.AddActivitySchedule/RemoveActivitySchedule）与 harness
// 调的是同一份实现。
//
// UE5 依据（只读参考工程 …/NarrativeArsenal）：
//   - Private/AI/Activities/NPCActivitySchedule.cpp:12-35  HandleStarted_Implementation
//       （:16 OwnerActivityComponent 非空 → :17 ProvideGoal() 非空 → :19-22 ScoreOverride > 0 才覆盖
//         DefaultScore → :25 bSaveGoal = false → :28 IntendedTODStartTime = EventTime
//         → :31-34 `if (!ActiveGoal) ActiveGoal = AddGoal(...)`）
//   - Private/AI/Activities/NPCActivitySchedule.cpp:43-54  HandleEnded_Implementation
//   - Private/AI/Activities/NPCActivitySchedule.cpp:56-58  ScoreOverride 默认 -1（不大于 0 → 不覆盖）
//   - Private/AI/Activities/NPCActivityComponent.cpp:568-591 AddActivitySchedule
//       （:577 `if (NPCScheduledBehavior && !NPCScheduledBehavior->bDisabled)`、:581 DuplicateObject、
//         :582 SetOwner、:583 BindBehavior(World, /*bFireIfAlreadyStarted*/true)、
//         :584 CreatedFromSchedule = Schedule —— 这个字段在 UE5 里**只写不读**，
//         见 Private/AI/Activities/NPCActivityComponent.cpp:593-599 的 RemoveActivitySchedule 循环体是空的）
//
// 端口差异（刻意保留，不是修 UE5 的怪癖）：
//   RemoveActivitySchedule 在端口里做了真实收尾（派发结束 / 解注册时辰事件 / 移出活跃表），
//   因此 CreatedFromSchedule 在端口**有了消费方** —— 归属判定住在 BelongsToSchedule。

using System;
using System.Collections.Generic;

namespace NarrativePro.AI.Activities
{
    /// <summary>
    /// 调度到点后「那份目标该怎么准备」的计划结果。
    /// 实时侧按本计划写回目标实例，因此写序与判定都只有一份实现。
    /// </summary>
    public struct ScheduledGoalPlan
    {
        /// <summary>ProvideGoal() 是否给出了目标（UE5 :17 的空指针早退）。</summary>
        public bool bHasGoal;

        /// <summary>计划后的最终默认评分（ScoreOverride &gt; 0 时覆盖，否则原样）。</summary>
        public float DefaultScore;

        /// <summary>调度目标永不入存档（UE5 :25 恒为 false）。</summary>
        public bool bSaveGoal;

        /// <summary>预定起始时辰（UE5 :28 取 EventTime，即窗口起点，不是实际派发时刻）。</summary>
        public float IntendedTODStartTime;

        /// <summary>是否真的要把目标交给组件添加（UE5 :31-34 的 <c>if (!ActiveGoal)</c>）。</summary>
        public bool bShouldAddGoal;
    }

    /// <summary>
    /// 活动调度绑定与调度目标的纯规则集。
    /// </summary>
    public static class NPCActivityScheduleRules
    {
        /// <summary>
        /// 这条调度行为要不要绑定。对应 UE5 <c>AddActivitySchedule</c>（NPCActivityComponent.cpp:577）：
        /// 空行为与 <c>bDisabled</c> 的行为都不注册时辰事件、不进活跃表。
        /// </summary>
        public static bool ShouldBindScheduledBehavior(bool behaviorExists, bool bDisabled)
        {
            return behaviorExists && !bDisabled;
        }

        /// <summary>
        /// 准备调度目标。对应 UE5 <c>UScheduledBehavior_AddNPCGoal::HandleStarted_Implementation</c>
        /// （NPCActivitySchedule.cpp:12-35）。
        /// 注意 UE5 的写序：三条改写发生在 <c>if (!ActiveGoal)</c> **之前**，
        /// 所以即便因为已有 ActiveGoal 而不再添加，传进来的那份目标也已经被改写过 —— 原样保留。
        /// </summary>
        /// <param name="goalProvided">ProvideGoal() 是否给出目标。</param>
        /// <param name="goalDefaultScore">目标自身的默认评分。</param>
        /// <param name="scoreOverride">调度行为的评分覆盖（UE5 默认 -1，即「不覆盖」）。</param>
        /// <param name="eventTime">窗口起点（UE5 的 EventTime）。</param>
        /// <param name="hasActiveGoal">本行为是否已经有一个在跑的目标。</param>
        public static ScheduledGoalPlan PlanScheduledGoalStart(bool goalProvided, float goalDefaultScore,
            float scoreOverride, float eventTime, bool hasActiveGoal)
        {
            var plan = new ScheduledGoalPlan
            {
                bHasGoal = goalProvided,
                DefaultScore = goalDefaultScore,
                // Scheduled goals should never save - when we re-open the game the schedule will add the goal again
                bSaveGoal = false,
                IntendedTODStartTime = eventTime,
                bShouldAddGoal = !hasActiveGoal
            };

            if (scoreOverride > 0f)
            {
                plan.DefaultScore = scoreOverride;
            }

            if (!goalProvided)
            {
                // UE5 :17：ProvideGoal() 为空时整段都不执行，计划里的改写值也不会被应用
                plan.bShouldAddGoal = false;
            }

            return plan;
        }

        /// <summary>
        /// 出点时要不要移除调度目标。对应 UE5 <c>HandleEnded_Implementation</c>
        /// （NPCActivitySchedule.cpp:43-54）：Owner 与 ActiveGoal 都在才移除。
        /// </summary>
        public static bool ShouldRemoveScheduledGoal(bool hasOwner, bool hasActiveGoal)
        {
            return hasOwner && hasActiveGoal;
        }

        /// <summary>
        /// 活跃行为归属哪份调度资产。对应端口 <c>RemoveActivitySchedule</c> 的过滤（UE5 侧该循环体是空的，
        /// 见 NPCActivityComponent.cpp:593-599）：按 <c>CreatedFromSchedule</c> 记录的路径**序数精确**比对。
        /// 两侧路径都为空时视为同一份（端口既有口径，抽取时不改判定，只把规则搬进可断言的位置）。
        /// </summary>
        public static bool BelongsToSchedule(string createdFromSchedulePath, string scheduleAssetPath)
        {
            return string.Equals(createdFromSchedulePath, scheduleAssetPath, StringComparison.Ordinal);
        }
    }
}
