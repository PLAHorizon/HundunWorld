using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Core;
using NarrativePro.Items;
using NarrativePro.SaveSystem;
using NarrativePro.UnrealFramework;

namespace NarrativePro.AI.Activities
{
    /// <summary>
    /// NPC 活动组件。对应 UE5 UNPCActivityComponent（实现 INarrativeSavableComponent）。
    /// 挂载到 NPC 角色上，管理活动、目标生成器、目标和当前活动。
    /// 负责：评分活动并选择最佳活动、管理目标生命周期、处理活动调度、
    /// 并通过 PrepareForSave/Load 与存档状态提供者完成 目标/活动/生成器 的往返（对应 UE5 :794/:853）。
    /// </summary>
    public class NPCActivityComponent : Script, INarrativeSavableComponent
    {
        /// <summary>拥有的 NPC 控制器（缓存）。对应 UE5 <c>OwnerController</c>（NPCActivityComponent.h:178-179）。</summary>
        [NonSerialized]
        public NarrativeNPCController OwnerController;

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
            // 缓存所属控制器。对应 UE5 BeginPlay 的 OwnerController = CastChecked(GetOwner())。
            if (OwnerController == null)
            {
                OwnerController = Actor != null ? Actor.GetScript<NarrativeNPCController>() : null;
            }
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
        /// 评分活动并选择最佳活动。评选/停启的裁决本体在纯层
        /// <see cref="NPCActivitySelectionRules.Decide"/>（对应 UE5 NPCActivityComponent.cpp:215-320），
        /// 本方法只做两件事：把引擎侧事实（能否运行、评分、目标容器、失效目标清理）取好，
        /// 以及把裁决动作落到活动上。
        /// </summary>
        /// <param name="bCheckNew">是否强制检查新活动（为 false 且当前活动仍有效时保留当前活动）</param>
        /// <returns>是否选择了新活动</returns>
        public bool PerformActivitySelection(bool bCheckNew = false)
        {
            var candidates = new List<ActivityCandidate>(Activities?.Count ?? 0);

            // 当前活动本轮重评（UE5:240-253 只在「当前活动带着目标」时才做，且会清理失效目标）。
            float currentScore = ActivitySelectionDecision.NoScore;
            if (CurrentActivity != null && CurrentActivity.ActivityGoal != null)
            {
                currentScore = ScoreAndCleanupInvalid(CurrentActivity, out _);
            }

            CollectScoredCandidates(candidates);

            var decision = NPCActivitySelectionRules.Decide(
                candidates,
                CurrentActivity,
                CurrentActivity != null ? CurrentActivity.ActivityGoal : null,
                CurrentActivity != null ? CurrentActivity.LastScore : 0f,
                currentScore,
                bCheckNew);

            return ApplySelectionDecision(decision, candidates);
        }

        /// <summary>
        /// 逐个活动做门控 + 评分，折成纯层候选列表。
        /// 未通过门控的活动*不参与评分*（与 UE5 一致：UE5 在别处门控，端口在评选里门控），
        /// 但仍登记为不可跑候选，让「BlockTags 挡住高分活动」这条规则留在纯层裁决里。
        /// </summary>
        private void CollectScoredCandidates(List<ActivityCandidate> outCandidates)
        {
            if (Activities == null) return;

            foreach (var activity in Activities)
            {
                if (activity == null) continue;

                if (!CanRunActivity(activity, null, out _))
                {
                    outCandidates.Add(new ActivityCandidate
                    {
                        ActivityKey = activity,
                        GoalKey = null,
                        Score = 0f,
                        bRunnable = false,
                    });
                    continue;
                }

                float score = ScoreAndCleanupInvalid(activity, out var goal);
                outCandidates.Add(new ActivityCandidate
                {
                    ActivityKey = activity,
                    GoalKey = goal,
                    Score = score,
                    bRunnable = true,
                });
            }
        }

        /// <summary>
        /// 对一个活动取目标容器、评分并清理它报告失效的目标。
        /// 对应 UE5 循环里的 <c>ScoreActivity(GetGoals(Activity->SupportedGoalType), BestGoal, InvalidGoals)</c>
        /// + 逐个 <c>RemoveGoal</c>。
        /// </summary>
        private float ScoreAndCleanupInvalid(NPCActivity activity, out NPCGoalItem outGoal)
        {
            NPCGoalContainer container = null;
            if (!string.IsNullOrEmpty(activity.SupportedGoalType))
            {
                Goals.TryGetValue(activity.SupportedGoalType, out container);
            }

            var invalidGoals = new List<NPCGoalItem>();
            float score = activity.ScoreActivity(container, out var goal, invalidGoals);
            outGoal = goal;

            if (invalidGoals != null)
            {
                foreach (var invalid in invalidGoals)
                {
                    RemoveGoal(invalid);
                }
            }

            return score;
        }

        /// <summary>把纯层裁决落成实际动作（停 / 启 / 保留）。</summary>
        private bool ApplySelectionDecision(ActivitySelectionDecision decision, List<ActivityCandidate> candidates)
        {
            switch (decision.Action)
            {
                case EActivitySelectionAction.KeepCurrent:
                case EActivitySelectionAction.NoChange:
                    return false;

                case EActivitySelectionAction.StopCurrent:
                    // 没有任何活动得分：停掉当前活动（UE5:298-303）。
                    StopCurrentActivity();
                    return false;

                case EActivitySelectionAction.StartCandidate:
                {
                    if (candidates == null ||
                        decision.CandidateIndex < 0 ||
                        decision.CandidateIndex >= candidates.Count)
                    {
                        return false;
                    }

                    var winner = candidates[decision.CandidateIndex];
                    var bestActivity = winner.ActivityKey as NPCActivity;
                    var bestGoal = winner.GoalKey as NPCGoalItem;

                    // 换了别的活动才先停旧的（同名活动由 StartActivity 自行收尾）。
                    if (CurrentActivity != null && !ReferenceEquals(CurrentActivity, bestActivity))
                    {
                        StopActivity_Internal(CurrentActivity);
                    }

                    string failReason;
                    return StartActivity(bestActivity, bestGoal, out failReason);
                }
            }

            return false;
        }

        /// <summary>检查活动是否可运行。对应 UE5 <c>CanRunActivity</c>（NPCActivityComponent.cpp:344-385）：
        /// 权威检查 + 拥有者（控制器）身上的 BlockTags/RequireTags 检查，最后询问模板自身。
        /// 判定次序与结论都在纯层 <see cref="NPCActivitySelectionRules.DecideTagGate"/>。</summary>
        public bool CanRunActivity(NPCActivity activityTemplate, NPCGoalItem goal, out string failReason)
        {
            bool bHasAuthority = OwnerController != null && OwnerController.HasAuthority();
            bool bTemplateNull = activityTemplate == null;

            bool bBlocked = false;
            bool bHasAllRequired = true;
            bool bTemplateAllows = false;
            string templateFailReason = "";

            // 只有前两道门都过了才去碰标签与模板（UE5 的判定次序，避免空模板解引用）。
            if (bHasAuthority && !bTemplateNull)
            {
                bBlocked = OwnerController.HasAnyMatchingGameplayTags(activityTemplate.BlockTags);
                bHasAllRequired = OwnerController.HasAllMatchingGameplayTags(activityTemplate.RequireTags);

                if (!bBlocked && bHasAllRequired)
                {
                    // 把拥有者当前标签交给模板做自定义判定
                    bTemplateAllows = activityTemplate.CanRunActivity(out templateFailReason, OwnerController.GetOwnedGameplayTags());
                }
            }

            var gate = NPCActivitySelectionRules.DecideTagGate(
                bHasAuthority, bTemplateNull, bBlocked, bHasAllRequired, bTemplateAllows);

            failReason = gate == EActivityGateResult.TemplateRefused && !string.IsNullOrEmpty(templateFailReason)
                ? templateFailReason
                : NPCActivitySelectionRules.GetFailReason(gate);
            return gate == EActivityGateResult.Allowed;
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

        /// <summary>
        /// 添加活动调度。对应 UE5 <c>AddActivitySchedule</c>（NPCActivityComponent.cpp:568-591）：
        /// 跳过 bDisabled 行为，为组件复制独立实例（DuplicateObject 等价），SetOwner →
        /// BindBehavior(GameState, bFireIfAlreadyStarted=true)（时辰事件绑定后 HandleStarted/Ended 自动派发）
        /// → 记录 CreatedFromSchedule → 加入 ActiveScheduledActivites。
        /// </summary>
        public void AddActivitySchedule(NPCActivitySchedule schedule)
        {
            if (schedule?.Activities == null) return;

            var gameState = NarrativeGameState.GetCurrent();

            foreach (var behavior in schedule.Activities)
            {
                // 绑定准入规则本体在纯接缝（UE5 NPCActivityComponent.cpp:577：空行为与 bDisabled 都不绑定）
                if (!NPCActivityScheduleRules.ShouldBindScheduledBehavior(behavior != null, behavior != null && behavior.bDisabled))
                {
                    continue;
                }

                var copy = behavior.CopyForComponent();
                copy.SetOwner(this);
                copy.BindBehavior(gameState, true);
                copy.CreatedFromSchedulePath = schedule.ScheduleAssetPath;

                ActiveScheduledActivites.Add(copy);
            }
        }

        /// <summary>
        /// 移除活动调度。对应 UE5 <c>RemoveActivitySchedule</c>（:593-599，UE5 循环体为空——调度目标不入存档、
        /// 行为实例随组件销毁）。端口做了更卫生的收尾：派发结束、解除时辰事件注册并从活跃列表移除。
        /// </summary>
        public void RemoveActivitySchedule(NPCActivitySchedule schedule)
        {
            if (schedule?.Activities == null) return;

            float tod = NPCGoalItem.NarrativeTimeOfDay;
            for (int i = ActiveScheduledActivites.Count - 1; i >= 0; i--)
            {
                var behavior = ActiveScheduledActivites[i];
                if (behavior == null) continue;
                if (behavior.CreatedFromSchedulePath != schedule.ScheduleAssetPath) continue;

                behavior.DispatchEndedNow(behavior.EndTime, tod, 0f, false);
                behavior.Unbind();
                behavior.SetOwner(null);
                ActiveScheduledActivites.RemoveAt(i);
            }
        }

        /// <summary>
        /// 设置活动配置。对应 UE5 <c>SetActivityConfiguration</c>（:601-622）：
        /// 实例化配置里的默认活动与目标生成器并加入组件；RescoreInterval 属存档数据，配置只提供默认值。
        /// </summary>
        public void SetActivityConfiguration(NPCActivityConfiguration config)
        {
            if (config == null) return;

            if (config.DefaultActivities != null)
            {
                foreach (var activityPath in config.DefaultActivities)
                {
                    if (string.IsNullOrEmpty(activityPath)) continue;
                    var activity = NPCActivityFactory.CreateActivity(activityPath);
                    if (activity != null)
                    {
                        AddActivity(activity, true);
                    }
                    else
                    {
                        NarrativeLog.LogWarning($"NPCActivityComponent: 无法实例化配置活动 {activityPath}");
                    }
                }
            }

            if (config.GoalGenerators != null)
            {
                foreach (var generatorPath in config.GoalGenerators)
                {
                    if (string.IsNullOrEmpty(generatorPath)) continue;
                    var generator = NPCActivityFactory.CreateGoalGenerator(generatorPath);
                    if (generator != null)
                    {
                        AddGoalGenerator(generator, true);
                    }
                    else
                    {
                        NarrativeLog.LogWarning($"NPCActivityComponent: 无法实例化配置生成器 {generatorPath}");
                    }
                }
            }

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
            newGoal.OwnerController = OwnerController; // 对应 UE5 AddGoal（NPCActivityComponent.cpp:646）
            newGoal.CreationTime = NPCGoalItem.NarrativeRunTime;
            newGoal.TODCreationTime = NPCGoalItem.NarrativeTimeOfDay;
            newGoal.Initialize();

            // UE5 AddGoal:686-694 的目标重选启发式（裁决在纯层 DecideGoalReselect）：
            // 没有当前活动/当前目标，或新目标得分*严格高于*当前目标得分时才强制重选一轮；
            // 否则只在调用方显式要求时做保守重选（当前活动仍有效则不动）。
            var currentGoal = CurrentActivity != null ? CurrentActivity.ActivityGoal : null;
            var reselect = NPCActivitySelectionRules.DecideGoalReselect(
                CurrentActivity != null,
                currentGoal != null,
                newGoal.GetGoalScore(),
                currentGoal != null ? currentGoal.GetGoalScore() : 0f,
                bTriggerReselect);

            switch (reselect)
            {
                case EGoalReselect.ReselectCheckNew:
                    PerformActivitySelection(true);
                    break;
                case EGoalReselect.ReselectKeepCurrentAllowed:
                    PerformActivitySelection(false);
                    break;
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

        /// <summary>内部启动活动实现。对应 UE5 <c>StartActivity</c>（NPCActivityComponent.cpp:507-540）：
        /// 通过 <c>CanRunActivity</c> 后必须真正 <c>RunActivity()</c>（跑树/挂标签/黑板），失败则停止并返回 false。</summary>
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

            activity.OwnerController = OwnerController;
            activity.ActivityGoal = goal;
            CurrentActivity = activity;

            // Can still fail after starting: blackboard init 或子类拒绝启动（UE5 :523-533）
            bool bSucceeded = activity.RunActivity();
            if (!bSucceeded)
            {
                StopCurrentActivity();
                return false;
            }

            return true;
        }

        // ===== 存档往返（对应 UE5 INarrativeSavableComponent 的 Load_/PrepareForSave_Implementation）=====

        /// <summary>
        /// 通知组件即将保存。对应 UE5 <c>PrepareForSave_Implementation</c>（NPCActivityComponent.cpp:853-918）：
        /// 把带 bSaveActivity / bSaveGoalGenerator / bSaveGoal 标记的运行时对象打包为
        /// 「CLR 类型路径 + SaveGame 状态字节」写入 Saved* 列表；这些列表是公开字段，
        /// 由 <c>NarrativeSaveStateArchive</c>（状态提供者）反射归档进存档记录。
        /// </summary>
        public void PrepareForSave()
        {
            SavedGoals.Clear();
            SavedActivities.Clear();
            SavedGoalGenerators.Clear();

            foreach (var activity in Activities)
            {
                if (activity == null || !activity.bSaveActivity) continue;
                SavedActivities.Add(new SavedNPCActivity
                {
                    ClassPath = activity.GetType().FullName,
                    Data = activity.GetSaveStateBytes(),
                });
            }

            foreach (var generator in GoalGenerators)
            {
                if (generator == null || !generator.bSaveGoalGenerator) continue;
                SavedGoalGenerators.Add(new SavedNPCGoalGenerator
                {
                    ClassPath = generator.GetType().FullName,
                    Data = NarrativeActivitySerializer.ToBytes(generator),
                });
            }

            foreach (var kvp in Goals)
            {
                var container = kvp.Value;
                if (container == null) continue;
                foreach (var goal in container.Goals)
                {
                    if (goal == null || !goal.bSaveGoal) continue;

                    // Let the goal store any savegame data it needs first（UE5 :895 PrepareForSave）
                    goal.PrepareForSave();

                    SavedGoals.Add(new SavedGoalItem
                    {
                        ClassPath = goal.GetType().FullName,
                        Data = NarrativeActivitySerializer.ToBytes(goal),
                    });
                }
            }
        }

        /// <summary>
        /// 通知组件已从存档加载。对应 UE5 <c>Load_Implementation</c>（NPCActivityComponent.cpp:794-851）：
        /// 按类型路径重建活动/生成器/目标并回灌 SaveGame 状态字节后加回组件。
        /// 没有任何人持有重建目标的指针是 UE5 已接受的行为（需要时按类型重新搜索目标）。
        /// </summary>
        public void Load()
        {
            // 先恢复活动（含 bSaveActivity 标记）
            foreach (var saved in SavedActivities)
            {
                if (saved == null || string.IsNullOrEmpty(saved.ClassPath)) continue;
                var activity = NPCActivityFactory.CreateActivity(saved.ClassPath);
                if (activity == null)
                {
                    NarrativeLog.LogWarning($"NPCActivityComponent: 存档活动类型无法解析：{saved.ClassPath}");
                    continue;
                }
                activity.ApplySaveStateBytes(saved.Data);
                AddActivity(activity, true);
            }

            // 目标生成器
            foreach (var saved in SavedGoalGenerators)
            {
                if (saved == null || string.IsNullOrEmpty(saved.ClassPath)) continue;
                var generator = NPCActivityFactory.CreateGoalGenerator(saved.ClassPath);
                if (generator == null)
                {
                    NarrativeLog.LogWarning($"NPCActivityComponent: 存档生成器类型无法解析：{saved.ClassPath}");
                    continue;
                }
                NarrativeActivitySerializer.OverwriteFrom(saved.Data, generator);
                AddGoalGenerator(generator, true);
            }

            // 目标（回灌状态 → PostLoad 重建标签容器 → AddGoal）
            foreach (var saved in SavedGoals)
            {
                if (saved == null || string.IsNullOrEmpty(saved.ClassPath)) continue;
                var goal = NPCActivityFactory.CreateGoal(saved.ClassPath);
                if (goal == null) continue;

                NarrativeActivitySerializer.OverwriteFrom(saved.Data, goal);
                goal.OwnerController = OwnerController;
                goal.PostLoad();
                AddGoal(goal);
            }
        }
    }
}
