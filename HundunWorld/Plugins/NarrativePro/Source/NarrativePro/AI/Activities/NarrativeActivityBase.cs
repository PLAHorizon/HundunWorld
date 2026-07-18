using System;
using NarrativePro.Items;

namespace NarrativePro.AI.Activities
{
    /// <summary>
    /// 活动执行上下文。对应 UE5 FActivityExecutionContext。
    /// 活动可从多种来源触发，通过上下文传递触发原因和来源信息。
    /// </summary>
    [Serializable]
    public class ActivityExecutionContext
    {
        public ActivityExecutionContext() { }
    }

    /// <summary>
    /// 调度活动执行上下文。对应 UE5 FActivityExecutionContext_Scheduled。
    /// 由 NPC 活动调度触发时传递。
    /// </summary>
    [Serializable]
    public class ActivityExecutionContext_Scheduled : ActivityExecutionContext
    {
        public ActivityExecutionContext_Scheduled()
        {
            StartTime = 0f;
            TimeAtStart = 0f;
            bStartedFromAdvanceTime = false;
        }

        /// <summary>活动开始时间</summary>
        public float StartTime;

        /// <summary>活动开始时的一天内时间</summary>
        public float TimeAtStart;

        /// <summary>是否由时间推进触发</summary>
        public bool bStartedFromAdvanceTime;
    }

    /// <summary>
    /// 活动基类。对应 UE5 UNarrativeActivityBase。
    /// 定居点活动和 NPC 活动的公共基类，定义活动执行生命周期。
    /// 子类通过覆盖 RunActivity/EndActivity 实现具体行为。
    /// </summary>
    [Serializable]
    public abstract class NarrativeActivityBase
    {
        /// <summary>活动名称</summary>
        public string ActivityName = "";

        /// <summary>活动开始时赋予 NPC/定居点的标签</summary>
        public GameplayTagContainer OwnedTags = new GameplayTagContainer();

        /// <summary>拥有这些标签时阻止活动运行</summary>
        public GameplayTagContainer BlockTags = new GameplayTagContainer();

        /// <summary>运行活动前要求 NPC 拥有的标签</summary>
        public GameplayTagContainer RequireTags = new GameplayTagContainer();

        /// <summary>返回活动描述字符串（调试用）</summary>
        public virtual string DescribeActivity()
        {
            return string.IsNullOrEmpty(ActivityName) ? GetType().Name : ActivityName;
        }

        /// <summary>获取活动名称</summary>
        public virtual string GetActivityName()
        {
            return string.IsNullOrEmpty(ActivityName) ? GetType().Name : ActivityName;
        }

        /// <summary>
        /// 返回活动是否可运行。
        /// 默认实现：检查是否被 BlockTags 阻止，以及是否满足 RequireTags。
        /// </summary>
        /// <param name="failReason">失败原因（输出）</param>
        /// <param name="ownerTags">拥有者的当前标签集</param>
        public virtual bool CanRunActivity(out string failReason, GameplayTagContainer ownerTags = null)
        {
            failReason = "";
            if (BlockTags != null && BlockTags.Count > 0 && ownerTags != null)
            {
                if (ownerTags.HasAny(BlockTags))
                {
                    failReason = "Owner has blocking tag";
                    return false;
                }
            }
            if (RequireTags != null && RequireTags.Count > 0)
            {
                if (ownerTags == null || !ownerTags.HasAll(RequireTags))
                {
                    failReason = "Owner missing required tag";
                    return false;
                }
            }
            return true;
        }

        /// <summary>启动活动。返回是否启动成功</summary>
        public virtual bool RunActivity()
        {
            // 基类不实现具体逻辑，子类覆盖
            return true;
        }

        /// <summary>结束活动</summary>
        public virtual bool EndActivity()
        {
            return true;
        }
    }
}
