using System;
using System.Collections.Generic;

namespace NarrativePro.AI.Activities
{
    /// <summary>
    /// 调度行为基类。对应 UE5 UScheduledBehavior_NPC。
    /// 由 NPC 活动调度在特定时间触发，可启动/结束活动或目标。
    /// </summary>
    [Serializable]
    public abstract class ScheduledBehavior_NPC
    {
        /// <summary>拥有此调度行为的活动组件</summary>
        [NonSerialized]
        public NPCActivityComponent OwnerActivityComponent;

        /// <summary>创建此行为的调度资产路径</summary>
        public string CreatedFromSchedulePath = "";

        /// <summary>调度开始时间（一天内时间，小时）</summary>
        public float StartTime = 0f;

        /// <summary>调度结束时间（一天内时间，小时）</summary>
        public float EndTime = 24f;

        /// <summary>设置拥有此调度行为的活动组件</summary>
        public void SetOwner(NPCActivityComponent inOwner)
        {
            OwnerActivityComponent = inOwner;
        }

        /// <summary>调度开始时调用</summary>
        /// <param name="eventTime">预定时间</param>
        /// <param name="actualTime">实际触发时间</param>
        /// <param name="timePassedDelta">已过去的时间</param>
        /// <param name="bFiredFromAdvancedTime">是否由时间推进触发</param>
        public virtual void HandleStarted(float eventTime, float actualTime, float timePassedDelta, bool bFiredFromAdvancedTime) { }

        /// <summary>调度结束时调用</summary>
        public virtual void HandleEnded(float eventTime, float actualTime, float timePassedDelta, bool bFiredFromAdvancedTime) { }
    }

    /// <summary>
    /// 添加 NPC 目标的调度行为。对应 UE5 UScheduledBehavior_AddNPCGoal。
    /// 在调度开始时创建并添加目标，结束时移除。
    /// </summary>
    [Serializable]
    public abstract class ScheduledBehavior_AddNPCGoal : ScheduledBehavior_NPC
    {
        /// <summary>当前活动的目标（结束时用于移除）</summary>
        [NonSerialized]
        protected NPCGoalItem ActiveGoal;

        /// <summary>若大于 0，使用此评分覆盖目标的默认评分</summary>
        public float ScoreOverride = -1f;

        /// <summary>是否在添加目标后触发重新选择活动</summary>
        public bool bReselect = true;

        public override void HandleStarted(float eventTime, float actualTime, float timePassedDelta, bool bFiredFromAdvancedTime)
        {
            ActiveGoal = ProvideGoal();
            if (ActiveGoal != null)
            {
                ActiveGoal.IntendedTODStartTime = eventTime;
                ActiveGoal.TODCreationTime = NPCGoalItem.NarrativeTimeOfDay;
                ActiveGoal.CreationTime = NPCGoalItem.NarrativeRunTime;
                if (ScoreOverride > 0f)
                {
                    ActiveGoal.DefaultScore = ScoreOverride;
                }
                if (OwnerActivityComponent != null)
                {
                    OwnerActivityComponent.AddGoal(ActiveGoal, bReselect);
                }
            }
        }

        public override void HandleEnded(float eventTime, float actualTime, float timePassedDelta, bool bFiredFromAdvancedTime)
        {
            if (ActiveGoal != null && OwnerActivityComponent != null)
            {
                OwnerActivityComponent.RemoveGoal(ActiveGoal);
                ActiveGoal = null;
            }
        }

        /// <summary>构造并提供目标（子类覆盖以返回具体目标）</summary>
        public abstract NPCGoalItem ProvideGoal();
    }

    /// <summary>
    /// NPC 活动调度。对应 UE5 UNPCActivitySchedule。
    /// 数据资产，包含 NPC 一天中应执行的活动列表。
    /// </summary>
    [Serializable]
    public class NPCActivitySchedule
    {
        /// <summary>活动调度行为列表</summary>
        public List<ScheduledBehavior_NPC> Activities = new List<ScheduledBehavior_NPC>();
    }
}
