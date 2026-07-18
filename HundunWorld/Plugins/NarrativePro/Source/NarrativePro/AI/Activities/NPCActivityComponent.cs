using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Core;
using NarrativePro.Items;

namespace NarrativePro.AI.Activities
{
    /// <summary>
    /// NPC 活动组件。对应 UE5 UNPCActivityComponent。
    /// 挂载到 NPC 角色上，管理活动、目标生成器、目标和当前活动。
    /// 负责：评分活动并选择最佳活动、管理目标生命周期、处理活动调度。
    /// </summary>
    public class NPCActivityComponent : Script
    {
        /// <summary>拥有的 NPC 控制器 ID（运行时由控制器设置）</summary>
        [NonSerialized]
        public string OwnerControllerId = "";

        /// <summary>重新评分目标的间隔（秒）</summary>
        public float RescoreInterval = 1.0f;

        /// <summary>活动配置路径</summary>
        public string ActivityConfigurationPath = "";

        /// <summary>活动组列表（从定义加载）</summary>
        public List<ActivityGroup> ActivityGroups = new List<ActivityGroup>();

        /// <summary>活动实例列表</summary>
        [NonSerialized]
        public List<NPCActivity> Activities = new List<NPCActivity>();

        /// <summary>目标生成器列表</summary>
        [NonSerialized]
        public List<NPCGoalGenerator> GoalGenerators = new List<NPCGoalGenerator>();

        /// <summary>当前目标映射（按目标类型路径分组）</summary>
        [NonSerialized]
        public Dictionary<string, NPCGoalContainer> Goals = new Dictionary<string, NPCGoalContainer>();

        /// <summary>活跃的调度活动列表</summary>
        [NonSerialized]
        public List<ScheduledBehavior_NPC> ActiveScheduledActivites = new List<ScheduledBehavior_NPC>();

        /// <summary>已保存的目标项（序列化用）</summary>
        public List<SavedGoalItem> SavedGoals = new List<SavedGoalItem>();

        /// <summary>已保存的活动（序列化用）</summary>
        public List<SavedNPCActivity> SavedActivities = new List<SavedNPCActivity>();

        /// <summary>已保存的目标生成器（序列化用）</summary>
        public List<SavedNPCGoalGenerator> SavedGoalGenerators = new List<SavedNPCGoalGenerator>();

        /// <summary>当前正在执行的活动</summary>
        [NonSerialized]
        public NPCActivity CurrentActivity;

        private float _rescoreTimer = 0f;

        public override void OnEnable()
        {
            base.OnEnable();
            _rescoreTimer = 0f;
            // 初始化活动组
            foreach (var group in ActivityGroups)
            {
                group?.SetOwner(OwnerControllerId, this);
            }
        }

        public override void OnDisable()
        {
            StopCurrentActivity();
            RemoveAllGoals();
            base.OnDisable();
        }

        public override void OnUpdate()
        {
            float dt = Time.DeltaTime;

            // 更新当前活动
            if (CurrentActivity != null)
            {
                CurrentActivity.TickActivity(dt);
            }

            // 定时重新评分
            _rescoreTimer += dt;
            if (_rescoreTimer >= RescoreInterval)
            {
                _rescoreTimer = 0f;
                RescoreGoals();
                PerformActivitySelection(true);
            }
        }

        /// <summary>重新评分所有目标并清理失效目标</summary>
        public void RescoreGoals()
        {
            var invalidGoals = new List<NPCGoalItem>();
            foreach (var kvp in Goals)
            {
                var container = kvp.Value;
                invalidGoals.Clear();
                if (container != null)
                {
                    for (int i = container.Goals.Count - 1; i >= 0; i--)
                    {
                        var goal = container.Goals[i];
                        if (goal == null)
                        {
                            container.Goals.RemoveAt(i);
                            continue;
                        }
                        if (goal.ShouldCleanup())
                        {
                            invalidGoals.Add(goal);
                            container.Goals.RemoveAt(i);
                            if (!string.IsNullOrEmpty(goal.GoalKey))
                            {
                                container.GoalUniqueObjectMap.Remove(goal.GoalKey);
                            }
                            goal.FireOnGoalRemoved(CurrentActivity);
                            goal.OnRemoved();
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 评分活动并选择最佳活动。
        /// </summary>
        /// <param name="bCheckNew">是否检查新活动（若当前活动有效且 bCheckNew 为 false，保留当前活动）</param>
        /// <returns>是否选择了新活动</returns>
        public bool PerformActivitySelection(bool bCheckNew = false)
        {
            // 若当前有活动且不检查新活动，且当前活动仍可运行，则保留
            if (!bCheckNew && CurrentActivity != null)
            {
                return false;
            }

            NPCActivity bestActivity = null;
            NPCGoalItem bestGoal = null;
            float bestScore = -1f;

            foreach (var activity in Activities)
            {
                if (activity == null) continue;
                if (!CanRunActivity(activity, null, out _)) continue;

                // 查找此活动支持的目标类型
                NPCGoalContainer container = null;
                if (!string.IsNullOrEmpty(activity.SupportedGoalType))
                {
                    Goals.TryGetValue(activity.SupportedGoalType, out container);
                }

                var invalidGoals = new List<NPCGoalItem>();
                float score = activity.ScoreActivity(container, out var goal, invalidGoals);

                // 清理失效目标
                foreach (var invalid in invalidGoals)
                {
                    RemoveGoal(invalid);
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    bestActivity = activity;
                    bestGoal = goal;
                }
            }

            if (bestActivity != null && (CurrentActivity != bestActivity || bestScore > CurrentActivity.LastScore))
            {
                // 停止当前活动
                if (CurrentActivity != null && CurrentActivity != bestActivity)
                {
                    StopActivity_Internal(CurrentActivity);
                }

                // 启动新活动
                string failReason;
                if (StartActivity(bestActivity, bestGoal, out failReason))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>检查活动是否可运行</summary>
        public bool CanRunActivity(NPCActivity activityTemplate, NPCGoalItem goal, out string failReason)
        {
            failReason = "";
            if (activityTemplate == null)
            {
                failReason = "Activity template is null";
                return false;
            }
            // 标签检查（ownerTags 暂时传 null，由子类或控制器提供实际标签）
            return activityTemplate.CanRunActivity(out failReason, null);
        }

        /// <summary>通过类型路径查找活动</summary>
        public NPCActivity GetActivity(string activityClassPath)
        {
            foreach (var activity in Activities)
            {
                if (activity != null && activity.GetType().FullName == activityClassPath)
                {
                    return activity;
                }
            }
            return null;
        }

        /// <summary>通过类型查找活动</summary>
        public T GetActivity<T>() where T : NPCActivity
        {
            foreach (var activity in Activities)
            {
                if (activity is T t) return t;
            }
            return null;
        }

        /// <summary>添加活动实例</summary>
        public NPCActivity AddActivity(NPCActivity activity, bool bSaveActivityFlag)
        {
            if (activity == null) return null;
            if (Activities.Contains(activity)) return activity;
            activity.SetOwner(OwnerControllerId, this);
            activity.bSaveActivity = bSaveActivityFlag;
            Activities.Add(activity);
            return activity;
        }

        /// <summary>移除活动</summary>
        public bool RemoveActivity(string activityClassPath)
        {
            for (int i = 0; i < Activities.Count; i++)
            {
                if (Activities[i] != null && Activities[i].GetType().FullName == activityClassPath)
                {
                    if (CurrentActivity == Activities[i])
                    {
                        StopCurrentActivity();
                    }
                    Activities.RemoveAt(i);
                    return true;
                }
            }
            return false;
        }

        /// <summary>通过类型路径查找目标生成器</summary>
        public NPCGoalGenerator GetGoalGenerator(string generatorClassPath)
        {
            foreach (var gen in GoalGenerators)
            {
                if (gen != null && gen.GetType().FullName == generatorClassPath)
                {
                    return gen;
                }
            }
            return null;
        }

        /// <summary>添加目标生成器</summary>
        public NPCGoalGenerator AddGoalGenerator(NPCGoalGenerator generator, bool bSaveGoalGenerator)
        {
            if (generator == null) return null;
            if (GoalGenerators.Contains(generator)) return generator;
            generator.bSaveGoalGenerator = bSaveGoalGenerator;
            generator.Initialize(OwnerControllerId, this);
            GoalGenerators.Add(generator);
            return generator;
        }

        /// <summary>移除目标生成器</summary>
        public bool RemoveGoalGenerator(string generatorClassPath)
        {
            for (int i = 0; i < GoalGenerators.Count; i++)
            {
                if (GoalGenerators[i] != null && GoalGenerators[i].GetType().FullName == generatorClassPath)
                {
                    GoalGenerators.RemoveAt(i);
                    return true;
                }
            }
            return false;
        }

        /// <summary>启动指定活动并传入目标</summary>
        public bool RunActivity(NPCActivity activityTemplate, NPCGoalItem goal, out string failReason)
        {
            failReason = "";
            if (activityTemplate == null)
            {
                failReason = "Activity template is null";
                return false;
            }
            if (CurrentActivity != null)
            {
                StopActivity_Internal(CurrentActivity);
            }
            return StartActivity(activityTemplate, goal, out failReason);
        }

        /// <summary>添加活动调度</summary>
        public void AddActivitySchedule(NPCActivitySchedule schedule)
        {
            if (schedule?.Activities == null) return;
            foreach (var behavior in schedule.Activities)
            {
                if (behavior != null)
                {
                    behavior.SetOwner(this);
                    ActiveScheduledActivites.Add(behavior);
                }
            }
        }

        /// <summary>移除活动调度</summary>
        public void RemoveActivitySchedule(NPCActivitySchedule schedule)
        {
            if (schedule?.Activities == null) return;
            foreach (var behavior in schedule.Activities)
            {
                if (behavior != null)
                {
                    behavior.HandleEnded(NPCGoalItem.NarrativeTimeOfDay, NPCGoalItem.NarrativeTimeOfDay, 0f, false);
                    ActiveScheduledActivites.Remove(behavior);
                }
            }
        }

        /// <summary>设置活动配置</summary>
        public void SetActivityConfiguration(NPCActivityConfiguration config)
        {
            if (config == null) return;
            RescoreInterval = config.RescoreInterval;
        }

        /// <summary>停止当前活动</summary>
        public void StopCurrentActivity()
        {
            if (CurrentActivity != null)
            {
                StopActivity_Internal(CurrentActivity);
                CurrentActivity = null;
            }
        }

        /// <summary>获取当前活动</summary>
        public NPCActivity GetCurrentActivity()
        {
            return CurrentActivity;
        }

        /// <summary>获取当前活动的目标</summary>
        public NPCGoalItem GetCurrentActivityGoal()
        {
            return CurrentActivity?.ActivityGoal;
        }

        /// <summary>添加目标到目标映射</summary>
        /// <param name="newGoal">要添加的目标</param>
        /// <param name="bTriggerReselect">是否触发活动重新选择</param>
        public NPCGoalItem AddGoal(NPCGoalItem newGoal, bool bTriggerReselect = false)
        {
            if (newGoal == null) return null;

            string typeKey = newGoal.GetType().FullName;
            if (!Goals.TryGetValue(typeKey, out var container))
            {
                container = new NPCGoalContainer();
                Goals[typeKey] = container;
            }

            // 检查是否已存在相同键的目标
            string key = newGoal.GetGoalKey();
            if (!string.IsNullOrEmpty(key) && container.GoalUniqueObjectMap.ContainsKey(key))
            {
                return container.GoalUniqueObjectMap[key];
            }

            container.Goals.Add(newGoal);
            if (!string.IsNullOrEmpty(key))
            {
                container.GoalUniqueObjectMap[key] = newGoal;
            }
            newGoal.OwnerActivityComponent = this;
            newGoal.CreationTime = NPCGoalItem.NarrativeRunTime;
            newGoal.TODCreationTime = NPCGoalItem.NarrativeTimeOfDay;
            newGoal.Initialize();

            if (bTriggerReselect)
            {
                PerformActivitySelection(true);
            }

            return newGoal;
        }

        /// <summary>移除指定目标</summary>
        public void RemoveGoal(NPCGoalItem goalToRemove)
        {
            if (goalToRemove == null) return;
            string typeKey = goalToRemove.GetType().FullName;
            if (Goals.TryGetValue(typeKey, out var container))
            {
                if (container.Goals.Remove(goalToRemove))
                {
                    string key = goalToRemove.GetGoalKey();
                    if (!string.IsNullOrEmpty(key))
                    {
                        container.GoalUniqueObjectMap.Remove(key);
                    }
                    goalToRemove.FireOnGoalRemoved(CurrentActivity);
                    goalToRemove.OnRemoved();
                }
            }
        }

        /// <summary>移除所有目标</summary>
        public void RemoveAllGoals()
        {
            foreach (var kvp in Goals)
            {
                var container = kvp.Value;
                if (container != null)
                {
                    foreach (var goal in container.Goals)
                    {
                        if (goal != null)
                        {
                            goal.FireOnGoalRemoved(CurrentActivity);
                            goal.OnRemoved();
                        }
                    }
                }
            }
            Goals.Clear();
        }

        /// <summary>获取指定类型的所有目标</summary>
        public NPCGoalContainer GetGoals(string goalTypePath)
        {
            if (Goals.TryGetValue(goalTypePath, out var container))
            {
                return container;
            }
            return new NPCGoalContainer();
        }

        /// <summary>是否拥有指定类型的目标</summary>
        public bool HasGoal(string goalTypePath)
        {
            if (Goals.TryGetValue(goalTypePath, out var container))
            {
                return !container.IsEmpty();
            }
            return false;
        }

        /// <summary>通过键查找目标</summary>
        public NPCGoalItem GetGoalByKey(string goalTypePath, string key, out bool outSucceeded)
        {
            outSucceeded = false;
            if (Goals.TryGetValue(goalTypePath, out var container))
            {
                if (container.GoalUniqueObjectMap.TryGetValue(key, out var goal))
                {
                    outSucceeded = true;
                    return goal;
                }
            }
            return null;
        }

        /// <summary>内部停止活动实现</summary>
        protected void StopActivity_Internal(NPCActivity activity, bool bCleanupBT = true)
        {
            if (activity == null) return;
            activity.EndActivity();
            if (bCleanupBT)
            {
                activity.StopBehaviorTree();
            }
            if (CurrentActivity == activity)
            {
                CurrentActivity = null;
            }
        }

        /// <summary>内部启动活动实现</summary>
        protected bool StartActivity(NPCActivity activity, NPCGoalItem goal, out string failReason)
        {
            failReason = "";
            if (activity == null)
            {
                failReason = "Activity is null";
                return false;
            }
            if (!CanRunActivity(activity, goal, out failReason))
            {
                return false;
            }
            activity.ActivityGoal = goal;
            CurrentActivity = activity;
            LastScore = activity.LastScore;
            return true;
        }

        private float LastScore;
    }
}
