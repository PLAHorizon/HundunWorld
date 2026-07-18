using System;
using NarrativePro.Items;

namespace NarrativePro.AI.Activities
{
    /// <summary>
    /// 目标完成签名回调。对应 UE5 FOnGoalSignature。
    /// </summary>
    /// <param name="activity">触发回调的活动</param>
    /// <param name="goal">完成的目标</param>
    public delegate void OnGoalSignature(NPCActivity activity, NPCGoalItem goal);

    /// <summary>
    /// 目标项基类。对应 UE5 UNPCGoalItem。
    /// 目标项描述一个可被 AI 活动操作的具体目标，例如攻击目标、交互对象等。
    /// 由目标生成器创建并添加到活动组件，活动通过评分选择最佳目标项执行。
    /// </summary>
    [Serializable]
    public abstract class NPCGoalItem
    {
        /// <summary>拥有此目标的 NPC 控制器（字符串引用，运行时由系统解析）</summary>
        public string OwnerControllerId = "";

        /// <summary>目标的键对象路径，相同键的目标不会被重复添加</summary>
        public string GoalKey = "";

        /// <summary>活动执行此目标时赋予 NPC 的标签</summary>
        public GameplayTagContainer OwnedTags = new GameplayTagContainer();

        /// <summary>拥有这些标签时强制低分，目标不会被处理</summary>
        public GameplayTagContainer BlockTags = new GameplayTagContainer();

        /// <summary>执行此目标要求 NPC 拥有的标签</summary>
        public GameplayTagContainer RequireTags = new GameplayTagContainer();

        /// <summary>活动完成目标后是否自动移除该目标</summary>
        public bool bRemoveOnSucceeded = true;

        /// <summary>默认评分（活动未覆盖 ScoreGoal 时使用）</summary>
        public float DefaultScore = 1f;

        /// <summary>当前评分（由最近一次活动评分更新缓存）</summary>
        public float CurrentScore = 0f;

        /// <summary>是否将此目标保存到磁盘</summary>
        public bool bSaveGoal = false;

        /// <summary>目标预定的开始时间（一天中的时间，0-24 小时制）</summary>
        public float IntendedTODStartTime = -1f;

        /// <summary>目标生命周期（秒）。小于 0 表示永不超时</summary>
        public float GoalLifetime = -1f;

        /// <summary>目标创建时间（游戏运行时间秒）</summary>
        public float CreationTime = 0f;

        /// <summary>目标创建时的一天内时间</summary>
        public float TODCreationTime = 0f;

        /// <summary>目标成功完成时触发</summary>
        public event OnGoalSignature OnGoalSucceeded;

        /// <summary>目标被移除时触发</summary>
        public event OnGoalSignature OnGoalRemoved;

        /// <summary>拥有此目标的活动组件（运行时设置）</summary>
        [NonSerialized]
        public NPCActivityComponent OwnerActivityComponent;

        /// <summary>
        /// 返回目标迟到的追赶时间（秒）。
        /// 例如预定 12:00 开始，但游戏在 15:00 加载，则追赶时间为 3 小时。
        /// </summary>
        public virtual float GetCatchupTime()
        {
            if (IntendedTODStartTime < 0f) return 0f;
            float currentTOD = NarrativeTimeOfDay;
            if (currentTOD >= IntendedTODStartTime) return currentTOD - IntendedTODStartTime;
            // 跨天处理
            return (24f - IntendedTODStartTime) + currentTOD;
        }

        /// <summary>返回目标从创建至今的真实时间（秒）</summary>
        public virtual float GetGoalAgeSeconds()
        {
            return NarrativeRunTime - CreationTime;
        }

        /// <summary>返回调试字符串描述目标</summary>
        public virtual string GetDebugString()
        {
            return $"{GetType().Name}(Key={GoalKey}, Score={CurrentScore})";
        }

        /// <summary>返回目标键。相同键的目标不会被重复添加</summary>
        public virtual string GetGoalKey()
        {
            return GoalKey;
        }

        /// <summary>返回目标评分（用于活动选择最佳目标）</summary>
        public virtual float GetGoalScore()
        {
            // 若有阻止标签，强制返回 0 分
            if (BlockTags != null && BlockTags.Count > 0)
            {
                return 0f;
            }
            return DefaultScore;
        }

        /// <summary>返回目标是否已失效应被清理（如攻击目标已死亡）</summary>
        public virtual bool ShouldCleanup()
        {
            // 超时处理
            if (GoalLifetime > 0f && GetGoalAgeSeconds() > GoalLifetime)
            {
                return true;
            }
            return false;
        }

        /// <summary>准备保存（如存储 Actor 的 GUID 以便加载后查找）</summary>
        public virtual void PrepareForSave() { }

        /// <summary>目标被添加或从磁盘加载后调用</summary>
        public virtual void Initialize() { }

        /// <summary>目标被移除时调用</summary>
        public virtual void OnRemoved() { }

        /// <summary>从拥有它的活动组件中移除此目标</summary>
        public virtual void RemoveGoal()
        {
            if (OwnerActivityComponent != null)
            {
                OwnerActivityComponent.RemoveGoal(this);
            }
        }

        /// <summary>返回此目标是否是当前活动正在操作的目标</summary>
        public bool IsActiveGoal()
        {
            if (OwnerActivityComponent == null) return false;
            var current = OwnerActivityComponent.GetCurrentActivityGoal();
            return current == this;
        }

        /// <summary>内部触发目标成功完成事件</summary>
        internal void FireOnGoalSucceeded(NPCActivity activity)
        {
            OnGoalSucceeded?.Invoke(activity, this);
        }

        /// <summary>内部触发目标移除事件</summary>
        internal void FireOnGoalRemoved(NPCActivity activity)
        {
            OnGoalRemoved?.Invoke(activity, this);
        }

        /// <summary>当前游戏内一天中的时间（0-24 小时）。由子系统提供，默认 0</summary>
        public static float NarrativeTimeOfDay = 0f;

        /// <summary>当前游戏运行时间（秒）。由子系统提供</summary>
        public static float NarrativeRunTime = 0f;
    }

    /// <summary>
    /// 目标项容器。对应 UE5 FNPCGoalContainer。
    /// 存储一组目标项，并提供按 GoalKey 查询。
    /// </summary>
    [Serializable]
    public class NPCGoalContainer
    {
        public System.Collections.Generic.List<NPCGoalItem> Goals = new System.Collections.Generic.List<NPCGoalItem>();

        /// <summary>按 GoalKey 索引的目标映射</summary>
        public System.Collections.Generic.Dictionary<string, NPCGoalItem> GoalUniqueObjectMap = new System.Collections.Generic.Dictionary<string, NPCGoalItem>();

        public bool IsEmpty()
        {
            return Goals.Count == 0;
        }
    }

    /// <summary>
    /// 已保存的目标项。对应 UE5 FSavedGoalItem。
    /// </summary>
    [Serializable]
    public class SavedGoalItem
    {
        public string ClassPath = "";
        public byte[] Data = new byte[0];
    }
}
