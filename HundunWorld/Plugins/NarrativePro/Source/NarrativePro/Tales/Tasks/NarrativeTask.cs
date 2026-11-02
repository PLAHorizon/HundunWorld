using System;
using FlaxEngine;
using NarrativePro.Items;
using NarrativePro.Navigation;
using NarrativePro.Tales.Nodes;
using NarrativePro.Tales.Quest;
using QuestClass = NarrativePro.Tales.Quest.Quest;

namespace NarrativePro.Tales.Tasks
{
    /// <summary>
    /// 任务条目基类。对应 UE5 <c>UNarrativeTask</c>（QuestTask.h / QuestTask.cpp）。
    /// <para>
    /// 关键链路（UE5 QuestTask.cpp:132-190）：
    /// <c>AddProgress/SetProgress → SetProgressInternal → 权限/加载/激活三重门 → OwningQuest.OnQuestTaskProgressChanged
    /// → 达标时 OwningBranch.OnQuestTaskComplete → Quest.TakeBranch → 进入目标状态</c>。
    /// 状态机能否推进完全取决于这条链是否完整。
    /// </para>
    /// </summary>
    public abstract class NarrativeTask
    {
        /// <summary>完成本任务所需的进度总量。对应 UE5 <c>RequiredQuantity</c>。</summary>
        public int RequiredQuantity { get; set; } = 1;

        /// <summary>当前进度。对应 UE5 <c>CurrentProgress</c>。</summary>
        public int CurrentProgress { get; protected set; } = 0;

        /// <summary>覆盖任务描述。对应 UE5 <c>DescriptionOverride</c>。</summary>
        public string DescriptionOverride { get; set; } = "";

        /// <summary>可选任务：不完成也不阻塞分支。对应 UE5 <c>bOptional</c>。</summary>
        public bool bOptional { get; set; } = false;

        /// <summary>是否在 UI 中隐藏。对应 UE5 <c>bHidden</c>。</summary>
        public bool bHidden { get; set; } = false;

        /// <summary>
        /// TickTask 的间隔秒数；&lt;=0 表示不周期 tick。对应 UE5 <c>TickInterval</c>。
        /// Flax 无 World Timer，由 <see cref="QuestClass.TickTasks"/> 逐帧累积驱动。
        /// </summary>
        public float TickInterval { get; set; } = 0f;

        /// <summary>任务当前是否处于活动状态。对应 UE5 <c>bIsActive</c>。</summary>
        public bool bIsActive { get; protected set; } = false;

        /// <summary>拥有本任务的叙事组件。对应 UE5 <c>OwningComp</c>。</summary>
        public object OwningComp { get; set; }

        /// <summary>拥有本任务的角色。对应 UE5 <c>OwningPawn</c>。</summary>
        public object OwningPawn { get; set; }

        /// <summary>拥有本任务的控制器。对应 UE5 <c>OwningController</c>。</summary>
        public object OwningController { get; set; }

        /// <summary>所属分支。对应 UE5 <c>GetOwningBranch()</c>（由 Outer 推导，Flax 无 Outer 故显式持有）。</summary>
        public QuestBranch OwningBranch { get; set; }

        /// <summary>所属任务。对应 UE5 <c>OwningQuest</c>。</summary>
        public QuestClass OwningQuest { get; set; }

        /// <summary>导航/地图标记设置。对应 UE5 <c>MarkerSettings</c>。</summary>
        public TaskNavigationMarker MarkerSettings { get; set; } = new TaskNavigationMarker();

        /// <summary>任务描述（UI）。对应 UE5 <c>Description</c> 在任务侧的等价物。</summary>
        public string Description { get; set; } = "";

        /// <summary>已生成的导航标记。对应 UE5 <c>SpawnedMarker</c>。</summary>
        [NonSerialized]
        public MapMarker SpawnedMarker;

        // 周期 tick 累积器（替代 UE5 World Timer）
        [NonSerialized] private float _tickAccumulator;

        // 标记附着 Actor 尚未就绪时的重试计时器（UE5 QuestTask.cpp:226 用 1 秒 World Timer）
        [NonSerialized] private float _markerRetryAccumulator;
        [NonSerialized] private bool _bMarkerRetryPending;

        // 标记附着 Actor 的异步索取动作（对应 UE5 UAsyncAction_ProvideActor，见 StartAttachActorPolling）
        [NonSerialized] private NarrativePro.Tales.Async.AsyncAction_ProvideActor _provideActorAction;

        /// <summary>把 <see cref="OwningComp"/> 收窄成 <see cref="TalesComponent"/>。</summary>
        protected TalesComponent NarrativeComp => OwningComp as TalesComponent;

        /// <summary>
        /// 仅做初始化：置活动标记、重置进度（非读档）、缓存 Quest/组件/Pawn/Controller。
        /// 对应 UE5 <c>UNarrativeTask::BeginTaskInit</c>（QuestTask.cpp:42-68）。
        /// </summary>
        public void BeginTaskInit()
        {
            bIsActive = true;

            // 对应 UE5:46-50 —— 回到曾经到过的状态时任务要重做，因此非读档时清零进度。
            var comp = NarrativeComp;
            if (comp != null && !comp.bIsLoading)
            {
                CurrentProgress = 0;
            }

            // 缓存任务运行期要用的上下文值（UE5 QuestTask.cpp:52-67）
            var branch = GetOwningBranch();
            if (branch != null)
            {
                OwningQuest = branch.GetOwningQuest();

                if (OwningQuest != null)
                {
                    OwningComp = OwningQuest.GetOwningComp();

                    var cachedComp = NarrativeComp;
                    if (cachedComp != null)
                    {
                        OwningPawn = cachedComp.GetOwningPawn();
                        OwningController = cachedComp.GetOwningController();
                    }
                }
            }
        }

        /// <summary>
        /// 启动任务。对应 UE5 <c>UNarrativeTask::BeginTask</c>（QuestTask.cpp:70-98）：
        /// BeginTaskInit → 子类的开始逻辑 → 注册周期 tick → 立刻跑一次 tick → 未达标时生成导航标记。
        /// </summary>
        public void BeginTask()
        {
            BeginTaskInit();

            OnBeginTask();

            var comp = NarrativeComp;
            if (comp != null)
            {
                _tickAccumulator = 0f;

                // 对应 UE5:86-87 —— BeginTask 通常会初始化 TickTask 需要的数据，所以先手推一次。
                RunTaskTick();
            }

            // 对应 UE5:91 —— 这里刻意不用 IsComplete()，可选任务仍需显示标记。
            if (!(CurrentProgress >= RequiredQuantity))
            {
                if (MarkerSettings != null && MarkerSettings.bAddNavigationMarker)
                {
                    SpawnDefaultNavigationMarker();
                }
            }
        }

        /// <summary>子类任务逻辑入口。对应 UE5 蓝图事件 <c>K2_BeginTask</c>。</summary>
        protected virtual void OnBeginTask()
        {
        }

        /// <summary>任务周期逻辑。对应 UE5 <c>TickTask_Implementation</c>。</summary>
        protected virtual void TickTask()
        {
        }

        /// <summary>
        /// 由任务 tick 驱动调用的入口：处理导航标记重试并调用 <see cref="TickTask"/>。
        /// 对应 UE5 中 World Timer 回调 <c>&amp;UNarrativeTask::TickTask</c>。
        /// </summary>
        protected internal void RunTaskTick()
        {
            if (_bMarkerRetryPending)
            {
                _markerRetryAccumulator += TickInterval > 0f ? TickInterval : 0.1f;
                if (_markerRetryAccumulator >= 1f)
                {
                    _markerRetryAccumulator = 0f;
                    SpawnDefaultNavigationMarker();
                }
            }

            TickTask();
        }

        /// <summary>
        /// 由 <see cref="QuestClass.TickTasks"/> 逐帧调用，按 <see cref="TickInterval"/> 节流。
        /// 这是 Flax 里替代 UE5 World Timer 的任务 tick 驱动。
        /// </summary>
        public void AdvanceTaskTick(float deltaTime)
        {
            if (!bIsActive) return;

            if (TickInterval <= 0f)
            {
                // 无周期 tick 的任务也要能重试挂标记（UE5 用独立的一次性 Timer 处理）
                if (_bMarkerRetryPending) RunTaskTick();
                return;
            }

            _tickAccumulator += deltaTime;
            while (_tickAccumulator >= TickInterval)
            {
                _tickAccumulator -= TickInterval;
                RunTaskTick();
                if (!bIsActive) break;
            }
        }

        /// <summary>
        /// 结束任务。对应 UE5 <c>UNarrativeTask::EndTask</c>（QuestTask.cpp:105-130）：
        /// 必须靠 <see cref="bIsActive"/> 守卫，重复调用不再触发子类结束逻辑。
        /// </summary>
        public void EndTask()
        {
            if (!bIsActive) return;

            bIsActive = false;
            _tickAccumulator = 0f;
            _bMarkerRetryPending = false;
            _markerRetryAccumulator = 0f;
            CancelAttachActorPolling(); // 任务结束必须撤掉异步索取，否则轮询器会替死任务续命

            if (SpawnedMarker != null)
            {
                SpawnedMarker.RemoveMarker();
                SpawnedMarker = null;
            }

            OnEndTask();
        }

        /// <summary>子类清理钩子。对应 UE5 蓝图事件 <c>K2_EndTask</c>。</summary>
        protected virtual void OnEndTask()
        {
        }

        /// <summary>子类完成钩子。对应 UE5 蓝图事件 <c>K2_OnTaskCompleted</c>。</summary>
        protected virtual void OnTaskCompleted()
        {
        }

        /// <summary>对应 UE5 <c>UNarrativeTask::SetProgress</c>。</summary>
        public virtual void SetProgress(int newProgress)
        {
            SetProgressInternal(newProgress, false);
        }

        /// <summary>
        /// 进度设置核心。对应 UE5 <c>UNarrativeTask::SetProgressInternal</c>（QuestTask.cpp:137-185）。
        /// 三重门缺一不可：① 权限（<c>bFromReplication || OwningComp.HasAuthority()</c>）；
        /// ② 读档态只写数值不广播（否则每次读档都会重放 UI/任务事件）；③ 只有活动中的任务能推进。
        /// </summary>
        public void SetProgressInternal(int newProgress, bool bFromReplication = false)
        {
            var comp = NarrativeComp;

            // Flax 端口说明：UE5 强制要求 OwningComp 存在（它是网络权威判定入口）。
            // 这里把「没有组件」的裸任务视为单机本地权威，避免脱离 Quest 单独使用的任务失去推进能力。
            bool bHasAuth = bFromReplication || comp == null || comp.HasAuthority();
            if (!bHasAuth) return;

            // 读档：只还原数值，不触发任何任务/状态机广播（UE5:146-150）
            if (comp != null && comp.bIsLoading)
            {
                CurrentProgress = ClampProgress(newProgress);
                return;
            }

            if (!bIsActive || newProgress < 0) return;
            if (newProgress == CurrentProgress) return;

            int oldProgress = CurrentProgress;
            CurrentProgress = ClampProgress(newProgress);

            OwningQuest?.OnQuestTaskProgressChanged(this, GetOwningBranch(), oldProgress, CurrentProgress);

            // 对应 UE5:165 —— 刻意不用 IsComplete()，可选任务不应在此触发分支推进。
            if (CurrentProgress >= RequiredQuantity)
            {
                if (SpawnedMarker != null)
                {
                    SpawnedMarker.RemoveMarker();
                    SpawnedMarker = null;
                }

                OnTaskCompleted();

                var branch = GetOwningBranch();
                branch?.OnQuestTaskComplete(this);
            }
        }

        private int ClampProgress(int value)
        {
            // Mathf 无 Clamp 语义保证，直接用 System.Math（整数）
            return Math.Max(0, Math.Min(value, RequiredQuantity));
        }

        /// <summary>对应 UE5 <c>UNarrativeTask::AddProgress</c>。</summary>
        public virtual void AddProgress(int progressToAdd = 1)
        {
            SetProgressInternal(CurrentProgress + progressToAdd);
        }

        /// <summary>
        /// 是否完成。对应 UE5 <c>UNarrativeTask::IsComplete</c>（QuestTask.cpp:192-195）：
        /// <c>CurrentProgress >= RequiredQuantity || bOptional</c>。
        /// </summary>
        public virtual bool IsComplete()
        {
            return CurrentProgress >= RequiredQuantity || bOptional;
        }

        /// <summary>直接把进度打满。对应 UE5 <c>UNarrativeTask::CompleteTask</c>。</summary>
        public virtual void CompleteTask()
        {
            SetProgressInternal(RequiredQuantity);
        }

        /// <summary>对应 UE5 <c>GetOwningBranch</c>。Flax 中用显式 <see cref="OwningBranch"/>。</summary>
        public QuestBranch GetOwningBranch()
        {
            return OwningBranch;
        }

        /// <summary>任务描述。对应 UE5 <c>GetTaskDescription_Implementation</c>。</summary>
        public virtual string GetTaskDescription()
        {
            if (!string.IsNullOrWhiteSpace(DescriptionOverride)) return DescriptionOverride;
            if (!string.IsNullOrWhiteSpace(Description)) return Description;
            return "Task Description";
        }

        /// <summary>进度文本。对应 UE5 <c>GetTaskProgressText_Implementation</c>（(当前/需求)）。</summary>
        public virtual string GetTaskProgressText()
        {
            return $"({CurrentProgress}/{RequiredQuantity})";
        }

        /// <summary>图上节点用的描述。对应 UE5 <c>GetTaskNodeDescription_Implementation</c>。</summary>
        public virtual string GetTaskNodeDescription()
        {
            return GetTaskDescription();
        }

        /// <summary>
        /// 导航标记位置。对应 UE5 <c>GetNavigationMarkerLocation_Implementation</c>（QuestTask.cpp:317-320）：
        /// 默认返回 <c>MarkerSettings.MarkerLocation</c>，子类可重写提供动态位置。
        /// </summary>
        public virtual Vector3 GetNavigationMarkerLocation()
        {
            return MarkerSettings != null ? MarkerSettings.MarkerLocation : Vector3.Zero;
        }

        /// <summary>
        /// 标记附着的 Actor。对应 UE5 <c>GetNavigationMarkerAttachActor_Implementation</c>（默认 null）。
        /// </summary>
        public virtual Actor GetNavigationMarkerAttachActor()
        {
            return null;
        }

        /// <summary>
        /// 用任务的标记设置生成默认导航标记。
        /// 对应 UE5 <c>UNarrativeTask::SpawnDefaultNavigationMarker</c>（QuestTask.cpp:207-250）。
        /// </summary>
        public void SpawnDefaultNavigationMarker()
        {
            if (!bIsActive) return;

            Actor attachActor = null;
            Vector3 markerLoc = GetNavigationMarkerLocation();

            if (MarkerSettings != null && MarkerSettings.MarkerAttachActorOverride != null)
            {
                attachActor = MarkerSettings.MarkerAttachActorOverride.ProvideActor(null);

                // 提供者暂时取不到 Actor：先用回退位置（UE5:222-229），
                // 并改由 AsyncAction_ProvideActor 轮询（对应 UE5 UAsyncAction_ProvideActor 的
                // 「Actor 还没生成时异步等待并在就绪后广播」语义）；动作创建失败则退回旧的每秒重试标记。
                if (attachActor == null)
                {
                    markerLoc = MarkerSettings.ActorFallbackLocation;
                    StartAttachActorPolling();
                }
                else
                {
                    _bMarkerRetryPending = false;
                    CancelAttachActorPolling();
                }
            }
            else
            {
                attachActor = GetNavigationMarkerAttachActor();

                if (attachActor == null && OverridesAttachActorProvider())
                {
                    _bMarkerRetryPending = true;

                    // GoToLocation 这类任务本身就有位置，只在位置为零时才用回退值（UE5:242-245）
                    if (markerLoc.LengthSquared < 1e-6f)
                    {
                        markerLoc = MarkerSettings.ActorFallbackLocation;
                    }
                }
                else
                {
                    _bMarkerRetryPending = false;
                }
            }

            SpawnNavigationMarker(markerLoc, attachActor);
        }

        /// <summary>
        /// 提供者暂时无 Actor 时启动异步索取（对应 UE5 <c>UAsyncAction_ProvideActor</c>：
        /// 每 1 秒轮询提供者，Actor 就绪后把导航标记重新挂到该 Actor 上）。
        /// 动作创建失败时退回旧的 <c>_bMarkerRetryPending</c> 每秒重试路径，行为不减弱。
        /// </summary>
        private void StartAttachActorPolling()
        {
            CancelAttachActorPolling();

            var action = NarrativePro.Tales.Async.AsyncAction_ProvideActor.ProvideActor(
                this, MarkerSettings.MarkerAttachActorOverride, 1f);

            if (action == null)
            {
                _bMarkerRetryPending = true;
                return;
            }

            _provideActorAction = action;
            _bMarkerRetryPending = false; // 轮询由异步动作接管，避免双通道重复挂标记

            action.OnProvided += HandleAttachActorProvided;
            action.OnFailed += HandleAttachActorPollFailed;
        }

        /// <summary>提供者就绪：Actor 到位则重挂标记；Actor 已被销毁则维持回退位置（与 UE5 一致）。</summary>
        private void HandleAttachActorProvided(FlaxEngine.Actor providedActor)
        {
            _provideActorAction = null;
            if (!bIsActive) return;

            if (providedActor != null)
            {
                _bMarkerRetryPending = false;
                SpawnNavigationMarker(GetNavigationMarkerLocation(), providedActor);
            }
        }

        /// <summary>索取失败（提供者丢失/世界不可用）：退回旧的每秒重试标记路径。</summary>
        private void HandleAttachActorPollFailed()
        {
            _provideActorAction = null;
            if (bIsActive && MarkerSettings != null)
            {
                _bMarkerRetryPending = true;
            }
        }

        /// <summary>取消进行中的异步索取并解绑事件（幂等）。</summary>
        private void CancelAttachActorPolling()
        {
            if (_provideActorAction == null) return;
            _provideActorAction.OnProvided -= HandleAttachActorProvided;
            _provideActorAction.OnFailed -= HandleAttachActorPollFailed;
            _provideActorAction.EndTask();
            _provideActorAction = null;
        }

        /// <summary>
        /// 子类是否重写了 <see cref="GetNavigationMarkerAttachActor"/>。
        /// 对应 UE5 的 <c>GetClass()->IsFunctionImplementedInScript(GET_FUNCTION_NAME_CHECKED(...))</c>
        /// （QuestTask.cpp:231），Flax 用反射比对声明类型实现。
        /// </summary>
        private bool OverridesAttachActorProvider()        {
            if (MarkerSettings == null) return false;

            var method = GetType().GetMethod(
                nameof(GetNavigationMarkerAttachActor),
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);

            return method != null && method.DeclaringType != typeof(NarrativeTask);
        }

        /// <summary>
        /// 在指定位置生成导航标记。
        /// 对应 UE5 <c>UNarrativeTask::SpawnNavigationMarker</c>（QuestTask.cpp:252-315）。
        /// </summary>
        public void SpawnNavigationMarker(Vector3 markerLocation, Actor navigationMarkerAttachActor = null)
        {
            if (MarkerSettings == null) return;

            // 已完成或非活动的任务不挂标记（UE5:254）
            if (CurrentProgress >= RequiredQuantity || !bIsActive) return;

            var settings = MarkerSettings;
            var marker = SpawnedMarker;
            if (marker == null)
            {
                marker = new MapMarker();
                SpawnedMarker = marker;
            }

            if (marker == null) return;

            // 放在 POI 标记之后，保证玩家仍能快速传送到 POI
            marker.ZOrder = 1;
            marker.MarkerTransform = new Transform(navigationMarkerAttachActor != null
                ? (navigationMarkerAttachActor.Position + markerLocation)
                : markerLocation);
            marker.SetDefaultDomains(settings.MarkerDomains);

            if (!string.IsNullOrEmpty(settings.NavigationMarkerIconPath))
            {
                marker.DefaultMarkerSettings.LocationIconPath = settings.NavigationMarkerIconPath;
            }

            if (settings.bDrawBreadcrumbs)
            {
                marker.bDrawBreadcrumbs = true;
                marker.bWantsOnPaint = true;
            }

            // 附着到 Actor 时，位置视为相对偏移（UE5:290-295）
            if (navigationMarkerAttachActor != null)
            {
                marker.ActorOwner = navigationMarkerAttachActor;
                marker.DefaultMarkerSettings.IconOffset = markerLocation;
            }

            string taskDescription = !string.IsNullOrWhiteSpace(DescriptionOverride) ? DescriptionOverride : GetTaskDescription();

            marker.DefaultMarkerSettings.MarkerTitleText =
                !string.IsNullOrWhiteSpace(settings.MarkerDisplayText) ? settings.MarkerDisplayText : taskDescription;
            marker.DefaultMarkerSettings.MarkerSubtitleText = settings.MarkerSubtitleText;
            marker.DefaultMarkerSettings.IconTint = settings.MarkerColor;

            // 任务未被追踪时先不显示标记（UE5:304-311）
            if (OwningQuest != null && OwningQuest.IsTracked())
            {
                marker.RegisterMarker();
            }
        }

        /// <summary>
        /// 随任务追踪状态注册/移除已生成的标记。
        /// 对应 UE5 <c>UQuest::SetTracked</c> 里对 <c>Task->SpawnedMarker</c> 的处理（Quest.cpp:214-242）。
        /// </summary>
        public void UpdateMarkerTracking(bool bTracked)
        {
            if (SpawnedMarker == null) return;

            if (bTracked)
            {
                SpawnedMarker.RegisterMarker();
            }
            else
            {
                SpawnedMarker.RemoveMarker();
            }
        }
    }
}
