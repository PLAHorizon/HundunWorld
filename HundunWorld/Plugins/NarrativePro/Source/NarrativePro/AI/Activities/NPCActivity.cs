using System;
using System.Collections.Generic;
using NarrativePro.Core;
using NarrativePro.Items;

namespace NarrativePro.AI.Activities
{
    /// <summary>
    /// 已保存的 NPC 活动。对应 UE5 FSavedNPCActivity。
    /// </summary>
    [Serializable]
    public class SavedNPCActivity
    {
        public string ClassPath = "";
        public byte[] Data = new byte[0];
    }

    /// <summary>
    /// NPC 活动类。对应 UE5 UNPCActivity。
    /// 包装行为树，操作目标项。活动组件通过 PerformActivitySelection 选择评分最高的活动执行。
    /// 注：Flax 无行为树系统，BehaviourTree 改为活动 Tick 逻辑占位。
    /// </summary>
    [Serializable]
    public abstract class NPCActivity : NarrativeActivityBase
    {
        /// <summary>活动是否可被打断</summary>
        public bool bIsInterruptable = true;

        /// <summary>最近一次评分（缓存）</summary>
        [NonSerialized]
        public float LastScore = 0f;

        /// <summary>行为树路径占位（Flax 无行为树，保留字段以便未来扩展）</summary>
        public string BehaviourTreePath = "";

        /// <summary>此活动支持的目标类型路径（可为空，如待机活动无需目标）</summary>
        public string SupportedGoalType = "";

        /// <summary>是否将此活动保存到磁盘（由 AddActivity 设置）</summary>
        [NonSerialized]
        public bool bSaveActivity = false;

        /// <summary>最近一次激活时间（游戏运行时间秒）。float.MaxValue 表示从未激活</summary>
        [NonSerialized]
        public float LastActivateTime = float.MaxValue;

        /// <summary>拥有此活动的 NPC 控制器 ID</summary>
        [NonSerialized]
        public string OwnerControllerId = "";

        /// <summary>拥有此活动的活动组件</summary>
        [NonSerialized]
        public NPCActivityComponent OwnerActivityComponent;

        /// <summary>此活动正在操作的目标（如攻击活动的攻击目标项）</summary>
        [NonSerialized]
        public NPCGoalItem ActivityGoal;

        /// <summary>活动目标成功完成时触发</summary>
        public event OnGoalSignature OnActivityGoalSucceeded;

        /// <summary>返回活动是否可被打断</summary>
        public bool IsInterruptable()
        {
            return bIsInterruptable;
        }

        /// <summary>
        /// 返回活动是否处于活跃状态。
        /// </summary>
        /// <param name="outActiveTime">活跃时长（活跃时为从激活至今的时间，非活跃时为从上次结束至今的时间）</param>
        public virtual bool IsActivityActive(out float outActiveTime)
        {
            if (LastActivateTime == float.MaxValue)
            {
                outActiveTime = float.MaxValue;
                return false;
            }
            outActiveTime = NPCGoalItem.NarrativeRunTime - LastActivateTime;
            return outActiveTime >= 0f;
        }

        /// <summary>设置拥有者</summary>
        public void SetOwner(string ownerControllerId, NPCActivityComponent ownerComp)
        {
            OwnerControllerId = ownerControllerId;
            OwnerActivityComponent = ownerComp;
        }

        /// <summary>
        /// 设置黑板（UE5 概念）。Flax 无黑板系统，此方法保留为占位。
        /// 子类可覆盖以初始化活动所需的数据。
        /// </summary>
        /// <returns>返回 true 表示黑板设置成功，活动可执行</returns>
        protected virtual bool SetupBlackboard()
        {
            return true;
        }

        /// <summary>
        /// 评分活动，选择最佳目标。
        /// 默认实现：从容器中选择评分最高的目标。
        /// </summary>
        /// <param name="goalContainer">目标容器</param>
        /// <param name="outBestGoal">最佳目标（输出）</param>
        /// <param name="outInvalidGoals">失效目标列表（输出，用于清理）</param>
        /// <returns>活动评分</returns>
        public virtual float ScoreActivity(NPCGoalContainer goalContainer, out NPCGoalItem outBestGoal, List<NPCGoalItem> outInvalidGoals)
        {
            outBestGoal = null;
            if (outInvalidGoals == null) outInvalidGoals = new List<NPCGoalItem>();
            float bestScore = -1f;

            if (goalContainer == null || goalContainer.IsEmpty())
            {
                return 0f;
            }

            foreach (var goal in goalContainer.Goals)
            {
                if (goal == null) continue;

                // 检查目标是否失效
                if (goal.ShouldCleanup())
                {
                    outInvalidGoals.Add(goal);
                    continue;
                }

                float score = ScoreGoalItem(goal);
                goal.CurrentScore = score;

                if (score > bestScore)
                {
                    bestScore = score;
                    outBestGoal = goal;
                }
            }

            return bestScore;
        }

        /// <summary>
        /// 评分目标项。默认返回目标的 GetGoalScore()。
        /// 子类可覆盖以提供自定义评分逻辑（如低体力时坐下的目标评分更高）。
        /// </summary>
        public virtual float ScoreGoalItem(NPCGoalItem goal)
        {
            if (goal == null) return 0f;
            return goal.GetGoalScore();
        }

        /// <summary>启动活动</summary>
        public override bool RunActivity()
        {
            if (!SetupBlackboard())
            {
                return false;
            }
            LastActivateTime = NPCGoalItem.NarrativeRunTime;
            return true;
        }

        /// <summary>结束活动</summary>
        public override bool EndActivity()
        {
            StopBehaviorTree();
            return true;
        }

        /// <summary>停止行为树（Flax 占位实现，仅记录日志）</summary>
        public virtual void StopBehaviorTree()
        {
            // Flax-不兼容: UE5 的 BehaviorTree 在 Flax 无对应物，保留占位。原文 TODO: 未来可接入状态机或行为树插件
        }

        /// <summary>
        /// 通知活动已成功完成（如攻击目标已死亡、到达目的地）。
        /// 若目标的 bRemoveOnSucceeded 为 true，则移除目标。
        /// </summary>
        public virtual void NotifySucceeded()
        {
            OnActivityGoalSucceeded?.Invoke(this, ActivityGoal);
            if (ActivityGoal != null)
            {
                ActivityGoal.FireOnGoalSucceeded(this);
                if (ActivityGoal.bRemoveOnSucceeded)
                {
                    RemoveActivityGoal();
                }
            }
        }

        /// <summary>移除当前活动的目标</summary>
        public virtual void RemoveActivityGoal()
        {
            if (ActivityGoal != null && OwnerActivityComponent != null)
            {
                var goalToRemove = ActivityGoal;
                ActivityGoal = null;
                OwnerActivityComponent.RemoveGoal(goalToRemove);
            }
        }

        /// <summary>活动每帧更新（由活动组件调用）。子类覆盖以实现持续逻辑</summary>
        public virtual void TickActivity(float deltaTime) { }
    }
}
