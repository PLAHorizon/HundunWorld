using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.UnrealFramework;

namespace NarrativePro.AI.Activities
{
    /// <summary>
    /// 调度行为基类。对应 UE5 UScheduledBehavior_NPC（继承 UScheduledBehavior，NPCActivitySchedule.h:13）。
    /// Flax 端口继承 <see cref="NarrativePro.UnrealFramework.ScheduledBehavior"/>，
    /// 由基类 BindBehavior 在 NarrativeGameState 的时辰事件上注册回调，
    /// 到点自动派发 HandleStarted/HandleEnded（对应 UE5 NPCActivitySchedule.cpp 的绑定路径）。
    /// </summary>
    [Serializable]
    public class ScheduledBehavior_NPC : ScheduledBehavior
    {
        /// <summary>拥有此调度行为的活动组件</summary>
        [NonSerialized]
        public NPCActivityComponent OwnerActivityComponent;

        /// <summary>创建此行为的调度资产路径。对应 UE5 <c>CreatedFromSchedule</c>（:24，TSoftObjectPtr 以路径表达）。</summary>
        public string CreatedFromSchedulePath = "";

        /// <summary>设置拥有此调度行为的活动组件。对应 UE5 <c>SetOwner</c>（NPCActivitySchedule.cpp:66-69）。</summary>
        public void SetOwner(NPCActivityComponent inOwner)
        {
            OwnerActivityComponent = inOwner;
        }

        /// <summary>
        /// 复制一份供某个活动组件独占使用。对应 UE5 <c>DuplicateObject&lt;UScheduledBehavior_NPC&gt;</c>
        /// （NPCActivityComponent.cpp:581）——每个组件需要自己的实例（含各自的 ActiveGoal 状态）。
        /// </summary>
        public virtual ScheduledBehavior_NPC CopyForComponent()
        {
            var copy = (ScheduledBehavior_NPC)MemberwiseClone();
            copy.OwnerActivityComponent = null;
            copy._startHandle = 0;
            copy._endHandle = 0;
            return copy;
        }

        /// <summary>由时间事件派发开始（外部移除调度等场景可用）。对应 UE5 DispatchHandleStarted。</summary>
        public void DispatchStartedNow(float eventTime, float actualTime, float timePassedDelta, bool bFiredFromAdvancedTime)
        {
            DispatchHandleStarted(eventTime, actualTime, timePassedDelta, bFiredFromAdvancedTime);
        }

        /// <summary>由时间事件派发结束。对应 UE5 DispatchHandleEnded。</summary>
        public void DispatchEndedNow(float eventTime, float actualTime, float timePassedDelta, bool bFiredFromAdvancedTime)
        {
            DispatchHandleEnded(eventTime, actualTime, timePassedDelta, bFiredFromAdvancedTime);
        }

        /// <summary>解绑时辰事件注册（对应 UE5 行为对象销毁时委托自动失效；Flax 需显式反注册时由此入口驱动）。</summary>
        public virtual void Unbind()
        {
            var gameState = NarrativeGameState.GetCurrent();
            if (gameState == null) return;
            // 委托按「目标+方法」相等移除，重新构造的等价委托可以命中注册时那份
            if (_startHandle != 0) { gameState.UnregisterTimeOfDayEvent(_startHandle, new TimeOfDayEventDelegate(DispatchHandleStarted)); _startHandle = 0; }
            if (_endHandle != 0) { gameState.UnregisterTimeOfDayEvent(_endHandle, new TimeOfDayEventDelegate(DispatchHandleEnded)); _endHandle = 0; }
        }
    }

    /// <summary>
    /// 添加 NPC 目标的调度行为。对应 UE5 UScheduledBehavior_AddNPCGoal。
    /// 在调度开始时创建并添加目标，结束时移除。
    /// </summary>
    [Serializable]
    public class ScheduledBehavior_AddNPCGoal : ScheduledBehavior_NPC
    {
        /// <summary>当前活动的目标（结束时用于移除）</summary>
        [NonSerialized]
        protected NPCGoalItem ActiveGoal;

        /// <summary>若大于 0，使用此评分覆盖目标的默认评分</summary>
        public float ScoreOverride = -1f;

        /// <summary>是否在添加目标后触发重新选择活动</summary>
        public bool bReselect = true;

        public ScheduledBehavior_AddNPCGoal()
        {
            ScoreOverride = -1f;
        }

        /// <summary>
        /// 对应 UE5 <c>HandleStarted_Implementation</c>（NPCActivitySchedule.cpp:14-41）：
        /// 调度目标永不入存档（重开游戏时调度会重新加目标）；记录预定起始时辰；已激活则不重复启动。
        /// 三条改写与「已有 ActiveGoal 就不再添加」的判据本体住在
        /// <see cref="NPCActivityScheduleRules.PlanScheduledGoalStart"/>。
        /// </summary>
        protected override void HandleStarted(float eventTime, float actualTime, float timePassedDelta, bool bFiredFromAdvancedTime)
        {
            if (OwnerActivityComponent == null) return;

            var ourGoal = ProvideGoal();
            var plan = NPCActivityScheduleRules.PlanScheduledGoalStart(
                ourGoal != null,
                ourGoal != null ? ourGoal.DefaultScore : 0f,
                ScoreOverride,
                eventTime,
                ActiveGoal != null);

            if (!plan.bHasGoal) return;

            ourGoal.DefaultScore = plan.DefaultScore;

            // Scheduled goals should never save - when we re-open the game the schedule will add the goal again
            ourGoal.bSaveGoal = plan.bSaveGoal;

            // Tell the goal when we intended for it to start, so it can catchup if it needs
            ourGoal.IntendedTODStartTime = plan.IntendedTODStartTime;

            // If we've already started this, dont start it again
            if (plan.bShouldAddGoal)
            {
                ActiveGoal = OwnerActivityComponent.AddGoal(ourGoal, bReselect);
            }
        }

        /// <summary>对应 UE5 <c>HandleEnded_Implementation</c>（:43-54）：移除活动中的调度目标。</summary>
        protected override void HandleEnded(float eventTime, float actualTime, float timePassedDelta, bool bFiredFromAdvancedTime)
        {
            if (NPCActivityScheduleRules.ShouldRemoveScheduledGoal(OwnerActivityComponent != null, ActiveGoal != null))
            {
                OwnerActivityComponent.RemoveGoal(ActiveGoal);
                ActiveGoal = null;
            }
        }

        public override ScheduledBehavior_NPC CopyForComponent()
        {
            var copy = (ScheduledBehavior_AddNPCGoal)base.CopyForComponent();
            copy.ActiveGoal = null;
            return copy;
        }

        /// <summary>构造并提供目标（子类覆盖以返回具体目标）。对应 UE5 <c>ProvideGoal_Implementation</c>（默认返回 null）。</summary>
        public virtual NPCGoalItem ProvideGoal()
        {
            return null;
        }
    }

    /// <summary>
    /// NPC 活动调度。对应 UE5 UNPCActivitySchedule（数据资产）。
    /// 包含 NPC 一天中应执行的活动列表。
    /// </summary>
    [Serializable]
    public class NPCActivitySchedule
    {
        /// <summary>调度资产的标识路径（JSON/资产系统里的逻辑名）。对应 UE5 资产对象自身。</summary>
        public string ScheduleAssetPath = "";

        /// <summary>活动调度行为列表。对应 UE5 <c>Schedule->Activities</c>。</summary>
        public List<ScheduledBehavior_NPC> Activities = new List<ScheduledBehavior_NPC>();
    }

    /// <summary>
    /// 添加「位置目标」的调度行为。对应 UE5 蓝图为 <c>UScheduledBehavior_AddNPCGoal</c> 子类、
    /// <c>ProvideGoal_Implementation</c> 返回位置目标的实现（NPCActivitySchedule.cpp:14-41 派发路径不变）。
    /// 目标载荷（位置/键/评分/生命周期）由 JSON 调度资产提供，到点自动 <c>AddGoal</c>、出点 <c>RemoveGoal</c>。
    /// </summary>
    [Serializable]
    public class ScheduledBehavior_AddLocationGoal : ScheduledBehavior_AddNPCGoal
    {
        /// <summary>目标键（相同键的目标不会重复添加）。</summary>
        public string GoalKey = "";

        /// <summary>目标世界位置。</summary>
        public Vector3 GoalLocation = Vector3.Zero;

        /// <summary>目标默认评分（可被 ScoreOverride 再覆盖）。</summary>
        public float GoalDefaultScore = 1f;

        /// <summary>目标生命周期（秒），小于 0 表示永不超时。</summary>
        public float GoalLifetime = -1f;

        /// <summary>目标显示名（调试用）。</summary>
        public string GoalDisplayName = "";

        public override NPCGoalItem ProvideGoal()
        {
            return new NPCGoal_Location
            {
                GoalKey = GoalKey,
                GoalLocation = GoalLocation,
                DefaultScore = GoalDefaultScore,
                GoalLifetime = GoalLifetime,
                DisplayName = GoalDisplayName,
            };
        }
    }
}
