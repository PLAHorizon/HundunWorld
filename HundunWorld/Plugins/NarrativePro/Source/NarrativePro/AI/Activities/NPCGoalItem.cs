using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using FlaxEngine;
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
        /// <summary>拥有此目标的 NPC 控制器 ID（序列化用，运行时由 <see cref="OwnerController"/> 提供实体）</summary>
        public string OwnerControllerId = "";

        /// <summary>目标的键对象路径，相同键的目标不会被重复添加</summary>
        public string GoalKey = "";

        /// <summary>活动执行此目标时赋予 NPC 的标签</summary>
        [JsonIgnore]
        public GameplayTagContainer OwnedTags = new GameplayTagContainer();

        /// <summary>拥有这些标签时强制低分，目标不会被处理</summary>
        [JsonIgnore]
        public GameplayTagContainer BlockTags = new GameplayTagContainer();

        /// <summary>执行此目标要求 NPC 拥有的标签</summary>
        [JsonIgnore]
        public GameplayTagContainer RequireTags = new GameplayTagContainer();

        // 标签容器本身不可 JSON 序列化（内部是私有集合），这里给出 SaveGame 用的名字表。
        // 对应 UE5 中 FGameplayTagContainer 作为 SaveGame 属性的归档行为。

        /// <summary><see cref="OwnedTags"/> 的序列化代理</summary>
        public List<string> OwnedTagNames = new List<string>();

        /// <summary><see cref="BlockTags"/> 的序列化代理</summary>
        public List<string> BlockTagNames = new List<string>();

        /// <summary><see cref="RequireTags"/> 的序列化代理</summary>
        public List<string> RequireTagNames = new List<string>();

        /// <summary>活动完成目标后是否自动移除该目标</summary>
        public bool bRemoveOnSucceeded = true;

        /// <summary>默认评分（活动未覆盖 ScoreGoal 时使用）</summary>
        public float DefaultScore = 1f;

        /// <summary>当前评分（由最近一次活动评分更新缓存）。运行时值，不入存档。</summary>
        [JsonIgnore]
        public float CurrentScore = 0f;

        /// <summary>是否将此目标保存到磁盘</summary>
        public bool bSaveGoal = false;

        /// <summary>目标预定的开始时间（一天中的时间，0-2400）。小于 0 表示未指定</summary>
        public float IntendedTODStartTime = -1f;

        /// <summary>目标生命周期（秒）。小于 0 表示永不超时</summary>
        public float GoalLifetime = -1f;

        /// <summary>目标创建时间（游戏运行时间秒）</summary>
        public float CreationTime = 0f;

        /// <summary>目标创建时的一天内时间</summary>
        public float TODCreationTime = 0f;

        /// <summary>目标关注的世界位置（供黑板/行为节点使用）</summary>
        public Vector3 GoalLocation = Vector3.Zero;

        /// <summary>目标关注的 Actor（如攻击目标、交互对象）</summary>
        [JsonIgnore]
        public Actor GoalActor;

        /// <summary>目标成功完成时触发</summary>
        public event OnGoalSignature OnGoalSucceeded;

        /// <summary>目标被移除时触发</summary>
        public event OnGoalSignature OnGoalRemoved;

        /// <summary>拥有此目标的活动组件（运行时设置）</summary>
        [JsonIgnore]
        public NPCActivityComponent OwnerActivityComponent;

        /// <summary>拥有此目标的 NPC 控制器（运行时设置）。对应 UE5 <c>OwnerController</c>。</summary>
        [JsonIgnore]
        public NarrativeNPCController OwnerController;

        /// <summary>
        /// 返回目标迟到的追赶时间（秒）。
        /// 例如预定 12:00 开始，但游戏在 15:00 加载，则追赶时间为 3 小时。
        /// </summary>
        public virtual float GetCatchupTime()
        {
            if (IntendedTODStartTime < 0f) return 0f;
            float currentTOD = NarrativeTimeOfDay;
            if (currentTOD >= IntendedTODStartTime) return currentTOD - IntendedTODStartTime;
            // 跨天处理（0-2400 记时制）
            return (2400f - IntendedTODStartTime) + currentTOD;
        }

        /// <summary>返回目标从创建至今的真实时间（秒）</summary>
        public virtual float GetGoalAgeSeconds()
        {
            return NarrativeRunTime - CreationTime;
        }

        /// <summary>返回调试字符串描述目标</summary>
        public virtual string GetDebugString()
        {
            return $"{GetType().Name}(Key={GetGoalKey()}, Score={GetGoalScore()})";
        }

        /// <summary>返回目标键。相同键的目标不会被重复添加</summary>
        public virtual string GetGoalKey()
        {
            return GoalKey;
        }

        /// <summary>
        /// 返回目标评分（用于活动选择最佳目标）。
        /// 标签的强/弱约束由 <see cref="NPCActivity.ScoreGoalItem"/> 依据控制器标签判定，
        /// 这里只处理目标自身的评分与位置刷新。
        /// </summary>
        public virtual float GetGoalScore()
        {
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

        /// <summary>
        /// 准备保存（如存储 Actor 的 GUID 以便加载后查找）。对应 UE5 <c>PrepareForSave</c>。
        /// 基类实现把标签容器同步进序列化代理字段。
        /// </summary>
        public virtual void PrepareForSave()
        {
            SyncTagsToSaveFields();
        }

        /// <summary>读档后把标签名字表还原成容器。对应 UE5 归档恢复 FGameplayTagContainer。</summary>
        public virtual void PostLoad()
        {
            OwnedTags = BuildContainer(OwnedTagNames);
            BlockTags = BuildContainer(BlockTagNames);
            RequireTags = BuildContainer(RequireTagNames);
        }

        /// <summary>把运行时标签容器写回存档用的名字表。</summary>
        protected void SyncTagsToSaveFields()
        {
            OwnedTagNames = ToNames(OwnedTags);
            BlockTagNames = ToNames(BlockTags);
            RequireTagNames = ToNames(RequireTags);
        }

        private static List<string> ToNames(GameplayTagContainer container)
        {
            var names = new List<string>();
            if (container == null) return names;
            foreach (var tag in container.GetTags())
            {
                names.Add(tag);
            }
            return names;
        }

        private static GameplayTagContainer BuildContainer(List<string> names)
        {
            var container = new GameplayTagContainer();
            if (names == null) return container;
            foreach (var name in names)
            {
                if (!string.IsNullOrEmpty(name)) container.AddTag(new GameplayTag(name));
            }
            return container;
        }

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

        /// <summary>
        /// 当前游戏内一天中的时间（0-2400 记时制）。
        /// 对应 UE5 <c>UArsenalStatics::GetTimeOfDay(this)</c>：实时从 <c>NarrativeGameState</c> 取，
        /// 无 GameState 时退回 0。
        /// </summary>
        public static float NarrativeTimeOfDay
        {
            get
            {
                var gameState = NarrativePro.UnrealFramework.NarrativeGameState.GetCurrent();
                return gameState != null ? gameState.TimeOfDay : 0f;
            }
        }

        /// <summary>
        /// 当前游戏运行时间（秒）。对应 UE5 <c>UWorld::GetTimeSeconds()</c>。
        /// </summary>
        public static float NarrativeRunTime => Time.GameTime;
    }

    /// <summary>
    /// 位置目标项。<see cref="NPCGoalItem"/> 是抽象类（对应 UE5 UNPCGoalItem 的 BP 基类），
    /// Flax 无蓝图，需要至少一个可直接被配置/调度 JSON 实例化的具体目标类型：
    /// 承载一个世界位置（如工作站点、集会点），评分沿用 <see cref="NPCGoalItem.DefaultScore"/>，
    /// 生命周期与标签门控由基类既有逻辑处理。
    /// </summary>
    [Serializable]
    public class NPCGoal_Location : NPCGoalItem
    {
        /// <summary>调试显示名。</summary>
        public string DisplayName = "";

        public override string GetDebugString()
        {
            return $"{GetType().Name}({DisplayName})(Key={GetGoalKey()}, Score={GetGoalScore()}, Location={GoalLocation})";
        }
    }

    /// <summary>
    /// 目标项容器。对应 UE5 FNPCGoalContainer。
    /// 存储一组目标项，并提供按 GoalKey 查询。
    /// </summary>
    [Serializable]
    public class NPCGoalContainer
    {
        public List<NPCGoalItem> Goals = new List<NPCGoalItem>();

        /// <summary>按 GoalKey 索引的目标映射</summary>
        public Dictionary<string, NPCGoalItem> GoalUniqueObjectMap = new Dictionary<string, NPCGoalItem>();

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
        /// <summary>目标的 CLR 类型路径（对应 UE5 <c>TSubclassOf&lt;UNPCGoalItem&gt; Class</c>）</summary>
        public string ClassPath = "";

        /// <summary>目标 SaveGame 状态的字节快照</summary>
        public byte[] Data = Array.Empty<byte>();
    }
}
