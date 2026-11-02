using System;
using System.Collections.Generic;
using System.Linq;
using FlaxEngine;
using NarrativePro.Core;
using NarrativePro.Items;
using NarrativePro.SaveSystem;
using NarrativePro.Tales.Data;
using NarrativePro.Tales.Nodes;
using NarrativePro.Tales.Tasks;
using DialogueClass = NarrativePro.Tales.Dialogue.Dialogue;

namespace NarrativePro.Tales.Quest
{
    /// <summary>
    /// 运行时任务对象。对应 UE5 <c>UQuest</c>（Quest.h / Quest.cpp）。
    /// <para>
    /// 状态机推进链（严格照抄 UE5）：
    /// <c>BeginQuest → EnterState_Internal → QuestState.Activate → QuestBranch.Activate → NarrativeTask.BeginTask</c>；
    /// 进度侧
    /// <c>SetProgressInternal → OwningQuest.OnQuestTaskProgressChanged → 达标 → QuestBranch.OnQuestTaskComplete
    /// → AreTasksComplete → Quest.TakeBranch → Branch.Deactivate + OnQuestBranchCompleted + EnterState_Internal(DestinationState)</c>。
    /// </para>
    /// </summary>
    public class Quest
    {
        public string QuestName { get; set; } = "";
        public string QuestDescription { get; set; } = "";

        /// <summary>是否被追踪：追踪时导航标记才会在地图上显示。对应 UE5 <c>bTracked</c>。</summary>
        public bool bTracked { get; set; } = true;

        /// <summary>读档后是否自动续播任务对话。对应 UE5 <c>bResumeDialogueAfterLoad</c>（默认 true）。</summary>
        public bool bResumeDialogueAfterLoad { get; set; } = true;

        public List<QuestState> States { get; set; } = new List<QuestState>();
        public List<QuestBranch> Branches { get; set; } = new List<QuestBranch>();

        /// <summary>
        /// 可继承状态。子任务不会继承任务图节点，但可以把父任务里像「RanOutOfTime」这样的状态共享给子任务。
        /// 对应 UE5 <c>InheritableStates</c>，<c>Initialize</c> 时会并入 <see cref="States"/>。
        /// </summary>
        public List<QuestState> InheritableStates { get; set; } = new List<QuestState>();

        public QuestState QuestStartState { get; set; }
        public QuestState CurrentState { get; set; }
        public EQuestCompletion QuestCompletion { get; set; } = EQuestCompletion.NotStarted;
        public List<QuestState> ReachedStates { get; set; } = new List<QuestState>();
        public List<QuestRequirement> QuestRequirements { get; set; } = new List<QuestRequirement>();

        /// <summary>本任务生成的 Actor 列表，任务结束时统一销毁。对应 UE5 <c>QuestActors</c>。</summary>
        public List<StableActor> QuestActors { get; set; } = new List<StableActor>();

        /// <summary>任务关联的对话类 ID。对应 UE5 <c>QuestDialogue</c>（TSubclassOf&lt;UDialogue&gt;）。</summary>
        public string QuestDialogueClassId { get; set; } = "";

        /// <summary>
        /// 任务对话的播放参数。对应 UE5 <c>QuestDialoguePlayParams</c>。
        /// UE5 构造函数（Quest.cpp:26-31）默认覆盖：自由移动 = true、不可跳过 = true、不可退出 = false。
        /// </summary>
        public DialoguePlayParams QuestDialoguePlayParams { get; set; } = CreateDefaultQuestDialoguePlayParams();

        /// <summary>拥有本任务的叙事组件。对应 UE5 <c>OwningComp</c>。</summary>
        public TalesComponent OwningComp { get; set; }

        /// <summary>拥有本任务的角色。对应 UE5 <c>OwningPawn</c>。</summary>
        public object OwningPawn { get; set; }

        /// <summary>拥有本任务的控制器。对应 UE5 <c>OwningController</c>。</summary>
        public object OwningController { get; set; }

        public event Action<Quest> QuestStarted;
        public event Action<Quest> QuestSucceeded;
        public event Action<Quest> QuestFailed;
        public event Action<Quest> QuestForgotten;
        public event Action<Quest> QuestRestarted;
        public event Action<Quest, QuestState> QuestNewState;
        public event Action<Quest, NarrativeTask, QuestBranch, int, int> QuestTaskProgressChanged;
        public event Action<Quest, NarrativeTask, QuestBranch> QuestTaskCompleted;
        public event Action<Quest, QuestBranch> QuestBranchCompleted;

        /// <summary>读档完成回调。对应 UE5 <c>OnQuestPostLoad</c>。</summary>
        public event Action<Quest> OnQuestPostLoad;

        /// <summary>
        /// OnEnteredFuncName 派发回调。对应 UE5 的 <c>FindFunction + ProcessEvent</c>（QuestSM.cpp:195-198）。
        /// Flax 无 UFunction，任务子类/外部监听者通过此委托响应节点进入/离开。
        /// </summary>
        public event Action<QuestNode, bool> OnNodeEnteredFunc;

        /// <summary>UE5 Quest.cpp:26-31 —— 任务对话播放参数的默认覆盖值。</summary>
        private static DialoguePlayParams CreateDefaultQuestDialoguePlayParams()
        {
            return new DialoguePlayParams
            {
                bOverrideFreeMovement = true,
                bFreeMovement = true,
                bOverrideUnskippable = true,
                bUnskippable = true,
                bOverrideCanBeExited = true,
                bCanBeExited = false
            };
        }

        /// <summary>
        /// 初始化任务。对应 UE5 <c>UQuest::Initialize</c>（Quest.cpp:66-112）：
        /// 并入可继承状态、缓存 Pawn/Controller、把 OwningQuest 写回所有节点。
        /// </summary>
        /// <returns>图是否合法（有状态、有分支、有起始状态）。</returns>
        public bool Initialize(TalesComponent comp, string startID = "")
        {
            // UE5 的顺序是「先验图（Quest.cpp:81-85），再并入可继承状态（:90）」：
            // 只有 States/Branches/起始状态齐备的图才有资格继续，靠 InheritableStates 把空图补全是上游不允许的。
            if (States.Count == 0 || Branches.Count == 0 || QuestStartState == null)
            {
                NarrativeLog.LogError($"任务 '{QuestName}' 图不完整（状态 {States.Count}、分支 {Branches.Count}、起始状态 {(QuestStartState == null ? "无" : "有")}），初始化失败");
                return false;
            }

            if (InheritableStates != null && InheritableStates.Count > 0)
            {
                foreach (var inheritable in InheritableStates)
                {
                    if (inheritable != null && !States.Contains(inheritable))
                    {
                        States.Add(inheritable);
                    }
                }
            }

            OwningComp = comp;

            // UE5:94-95 —— 漏掉这两行会让所有条件看到空 pawn。
            if (comp != null)
            {
                OwningPawn = comp.GetOwningPawn();
                OwningController = comp.GetOwningController();
            }

            foreach (var node in GetNodes())
            {
                if (node != null)
                {
                    node.OwningQuest = this;
                }
            }

            // Flax 无 Outer：任务需要显式记住自己所在的分支，GetOwningBranch() 才有值。
            foreach (var branch in Branches)
            {
                if (branch == null) continue;
                branch.OwningQuest = this;
                foreach (var task in branch.QuestTasks)
                {
                    if (task != null)
                    {
                        task.OwningBranch = branch;
                        task.OwningQuest = this;
                    }
                }
            }

            foreach (var req in QuestRequirements)
            {
                req?.OnAdded(this);
            }

            return true;
        }

        /// <summary>
        /// 反初始化。对应 UE5 <c>UQuest::Deinitialize</c>（Quest.cpp:114-154）：
        /// 要把每一个分支和状态都失活（不只当前状态），否则任务会继续 tick。
        /// </summary>
        public void Deinitialize()
        {
            foreach (var branch in Branches)
            {
                branch?.Deactivate();
            }

            foreach (var state in States)
            {
                state?.Deactivate();
            }

            // 销毁任务生成的 Actor（UE5:133-143）
            foreach (var questActor in QuestActors)
            {
                bool bSucceeded;
                var actor = StableActorStatics.GetStableActor(questActor, out bSucceeded);
                if (bSucceeded && actor != null)
                {
                    // Flax 的 Object 与 System.Object 同名，必须全限定
                    FlaxEngine.Object.Destroy(actor);
                }
            }

            QuestActors.Clear();

            foreach (var req in QuestRequirements)
            {
                req?.OnRemoved(this);
            }

            QuestRequirements.Clear();

            CurrentState = null;
        }

        /// <summary>开始任务。对应 UE5 <c>UQuest::BeginQuest</c>（Quest.cpp:156-172）。</summary>
        public void BeginQuest(string startFromID = "")
        {
            // UE5 允许从任意状态进入；空起始状态时不再静默返回，而是给出可诊断的错误。
            var startState = string.IsNullOrEmpty(startFromID)
                ? QuestStartState
                : (GetState(startFromID) ?? QuestStartState);

            if (startState == null)
            {
                NarrativeLog.LogError($"任务 '{QuestName}' 没有可用的起始状态，BeginQuest 取消");
                return;
            }

            QuestCompletion = EQuestCompletion.Started;

            // 先广播 Started，再进状态：进状态可能立刻级联推进并广播后续状态变化。
            QuestStarted?.Invoke(this);

            EnterState_Internal(startState);
        }

        /// <summary>
        /// 任务对话的开始入口。对应 UE5 <c>UQuest::HandleBeginDialogue</c>（Quest.cpp:174-189）。
        /// </summary>
        public void HandleBeginDialogue()
        {
            if (string.IsNullOrEmpty(QuestDialogueClassId) || OwningComp == null)
            {
                return;
            }

            var playParams = QuestDialoguePlayParams != null ? QuestDialoguePlayParams.Clone() : new DialoguePlayParams();
            playParams.StartFromID = CurrentState != null ? CurrentState.GetID() : "";

            if (OwningComp.HasDialogueAvailable(QuestDialogueClassId, playParams))
            {
                OwningComp.BeginDialogue(QuestDialogueClassId, playParams);
            }
        }

        /// <summary>
        /// 取走分支。对应 UE5 <c>UQuest::TakeBranch</c>（Quest.cpp:191-206）：
        /// 分支失活（跑 End 事件）→ 广播分支完成 → 进入目标状态。
        /// </summary>
        public virtual void TakeBranch(QuestBranch branch)
        {
            if (branch == null) return;

            // UE5 对齐结论：UQuest::TakeBranch（Quest.cpp:191-206）没有条件门控——
            // Conditions 的判定发生在节点/事件层（UNarrativeNodeBase::AreConditionsMet 在
            // 状态机自动推进与事件触发处检查，见 NarrativeNodeBase.cs:132-144），
            // TakeBranch 是显式脚本化取分支，必须成功。本会话此前在此加的门控会在
            // 任务完成后把「仅用于门控自动推进」的条件误当拦截器，导致状态机永久停摆。
            // 故移除拦截，仅保留诊断日志。
            if (branch.Conditions != null && branch.Conditions.Count > 0
                && !branch.AreConditionsMet(OwningPawn, OwningController, OwningComp))
            {
                NarrativeLog.Log($"Quest {QuestName}: 显式取分支 {branch.GetID()} 时分支条件未成立（UE5 同样放行，条件只门控自动推进）");
            }

            branch.Deactivate();

            OnQuestBranchCompleted(branch);

            EnterState_Internal(branch.DestinationState);
        }

        /// <summary>
        /// 显式进入状态。对应 UE5 <c>UQuest::EnterState</c>（Quest.cpp:246-258）：
        /// 除推进状态外还要把 QuestNewState 更新发给网络侧。
        /// </summary>
        public void EnterState(QuestState newState)
        {
            if (newState == null || !States.Contains(newState)) return;

            EnterState_Internal(newState);

            // 对应 UE5:255 —— 手动切状态时必须通知其他端。
            OwningComp?.SendNarrativeUpdate(NarrativeUpdate.QuestNewState(QuestName, newState.GetID()));
        }

        /// <summary>
        /// 状态推进的内部实现。对应 UE5 <c>UQuest::EnterState_Internal</c>（Quest.cpp:260-306）。
        /// 读档期间不跑对话、不广播委托（委托多用于 UI），但一定要 Activate 让分支任务起来。
        /// </summary>
        public virtual void EnterState_Internal(QuestState newState)
        {
            if (newState == null) return;

            if (CurrentState != null)
            {
                CurrentState.Deactivate();
            }

            CurrentState = newState;

            if (!ReachedStates.Contains(newState))
            {
                ReachedStates.Add(newState);
            }

            bool bIsLoading = OwningComp != null && OwningComp.bIsLoading;

            if (!bIsLoading)
            {
                HandleBeginDialogue();
            }

            if (newState.StateNodeType == EStateNodeType.Success)
            {
                SucceedQuest(newState.Description);
            }
            else if (newState.StateNodeType == EStateNodeType.Failure)
            {
                FailQuest(newState.Description);
            }

            if (!bIsLoading)
            {
                QuestNewState?.Invoke(this, newState);
            }

            // 最后激活新状态，从而激活它的分支，任务得以推进
            newState.OwningQuest = this;
            newState.Activate();
        }

        /// <summary>对应 UE5 <c>GetState(FName)</c>。</summary>
        public QuestState GetState(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            return States.FirstOrDefault(s => s != null && s.ID == id);
        }

        /// <summary>对应 UE5 <c>GetBranch(FName)</c>。</summary>
        public QuestBranch GetBranch(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            return Branches.FirstOrDefault(b => b != null && b.ID == id);
        }

        public string GetQuestName() => QuestName;
        public string GetQuestDescription() => QuestDescription;
        public void SetQuestName(string newName) { QuestName = newName ?? ""; }
        public void SetQuestDescription(string newDescription) { QuestDescription = newDescription ?? ""; }

        public QuestState GetCurrentState() => CurrentState;
        public EQuestCompletion GetQuestCompletion() => QuestCompletion;
        public TalesComponent GetOwningComp() => OwningComp;
        public object GetOwningPawn() => OwningPawn;
        public object GetOwningController() => OwningController;
        public string GetQuestDialogueClassId() => QuestDialogueClassId;

        /// <summary>
        /// 任务成功。对应 UE5 <c>UQuest::SucceedQuest</c>（Quest.cpp:456-475）：
        /// 读档期间只置状态不广播，最后统一 Deinitialize。
        /// </summary>
        public void SucceedQuest(string message = "")
        {
            QuestCompletion = EQuestCompletion.Succeeded;

            if (OwningComp != null && !OwningComp.bIsLoading)
            {
                QuestSucceeded?.Invoke(this);
            }

            Deinitialize();
        }

        /// <summary>任务失败。对应 UE5 <c>UQuest::FailQuest</c>（Quest.cpp:437-455）。</summary>
        public void FailQuest(string message = "")
        {
            QuestCompletion = EQuestCompletion.Failed;

            if (OwningComp != null && !OwningComp.bIsLoading)
            {
                QuestFailed?.Invoke(this);
            }

            Deinitialize();
        }

        /// <summary>
        /// 遗忘任务：广播 <see cref="QuestForgotten"/> 并反初始化。
        /// 对应 UE5 里 <c>UTalesComponent::ForgetQuest</c> 中的 Deinitialize + OnQuestForgotten 广播。
        /// </summary>
        public void ForgetQuest()
        {
            Deinitialize();
            QuestForgotten?.Invoke(this);
        }

        /// <summary>
        /// 重启任务：广播 <see cref="QuestRestarted"/>。对应 UE5 <c>UTalesComponent::RestartQuest</c>（TalesComponent.cpp:269）。
        /// </summary>
        public void RestartQuest()
        {
            QuestRestarted?.Invoke(this);
        }

        /// <summary>
        /// 读档后回调。对应 UE5 <c>UQuest::QuestPostLoad</c>（Quest.cpp:421-435）：
        /// 续播任务对话，并用「先翻转再设回」的技巧强制重新注册导航标记。
        /// </summary>
        public void QuestPostLoad()
        {
            if (bResumeDialogueAfterLoad && QuestCompletion == EQuestCompletion.Started)
            {
                HandleBeginDialogue();
            }

            bool bWasTracked = bTracked;
            bTracked = !bTracked;
            SetTracked(bWasTracked);

            OnQuestPostLoad?.Invoke(this);
        }

        /// <summary>
        /// 设置追踪状态。对应 UE5 <c>UQuest::SetTracked</c>（Quest.cpp:208-244）：
        /// 变化时把当前状态所有分支任务的标记注册/移除。
        /// </summary>
        public void SetTracked(bool tracked)
        {
            if (tracked == bTracked) return;

            bTracked = tracked;

            if (CurrentState != null && CurrentState.Branches != null)
            {
                foreach (var branch in CurrentState.Branches)
                {
                    if (branch?.QuestTasks == null) continue;
                    foreach (var task in branch.QuestTasks)
                    {
                        task?.UpdateMarkerTracking(bTracked);
                    }
                }
            }
        }

        /// <summary>对应 UE5 <c>IsTracked</c>。</summary>
        public bool IsTracked()
        {
            return bTracked;
        }

        /// <summary>对应 UE5 <c>UQuest::GetNodes</c>。</summary>
        public List<QuestNode> GetNodes()
        {
            var nodes = new List<QuestNode>();
            foreach (var state in States)
            {
                if (state != null) nodes.Add(state);
            }
            foreach (var branch in Branches)
            {
                if (branch != null) nodes.Add(branch);
            }
            return nodes;
        }

        /// <summary>
        /// 取做这个任务的所有控制器；队伍任务返回全体队员，单人任务返回拥有者控制器。
        /// 对应 UE5 <c>UQuest::GetGroupMembers</c>（Quest.cpp:606-626）。
        /// </summary>
        public List<object> GetGroupMembers()
        {
            var groupMembers = new List<object>();

            var party = OwningComp as NarrativePartyComponent;
            if (party != null)
            {
                foreach (var member in party.GetPartyMembers())
                {
                    if (member != null) groupMembers.Add(member.GetOwningController());
                }
            }
            else if (OwningController != null)
            {
                groupMembers.Add(OwningController);
            }

            return groupMembers;
        }

        /// <summary>
        /// 派发节点的 OnEnteredFuncName。对应 UE5 <c>FindFunction + ProcessEvent</c>（QuestSM.cpp:195-198）。
        /// </summary>
        public void DispatchNodeEntered(QuestNode node, bool bActivated)
        {
            OnNodeEnteredFunc?.Invoke(node, bActivated);
        }

        /// <summary>
        /// 生成归属本任务的 Actor。对应 UE5 <c>UQuest::SpawnQuestActor_Implementation</c>（Quest.cpp:513-544）。
        /// <para>
        /// 读档期间直接返回 false —— 需要复活的 Actor 应由存档子系统重建，而不是任务重刷。
        /// </para>
        /// </summary>
        public bool SpawnQuestActor(string prefabPath, Transform actorTransform, out StableActor spawnedActor)
        {
            spawnedActor = new StableActor();

            if (OwningComp != null && OwningComp.bIsLoading)
            {
                return false;
            }

            if (string.IsNullOrEmpty(prefabPath))
            {
                return false;
            }

            var prefab = Content.LoadAsync<Prefab>(prefabPath);
            if (prefab == null)
            {
                NarrativeLog.LogWarning($"SpawnQuestActor: 加载 Prefab 失败：{prefabPath}");
                return false;
            }

            var actor = PrefabManager.SpawnPrefab(prefab, actorTransform.Translation, actorTransform.Orientation);
            if (actor == null)
            {
                NarrativeLog.LogWarning($"SpawnQuestActor: 生成 Actor 失败：{prefabPath}");
                return false;
            }
            // SpawnPrefab 默认落 Scenes[0]（RootScene），任务对象属玩法世界对象，搬入玩法场景
            NarrativePro.CommonUI.NarrativeRuntimeBootstrap.MoveToGameplayScene(actor, prefabPath);

            actor.Scale = actorTransform.Scale;

            var stable = StableActorStatics.MakeStableActor(actor);
            if (stable == null)
            {
                return false;
            }

            spawnedActor = stable;
            QuestActors.Add(spawnedActor);
            return true;
        }

        /// <summary>运行时添加任务要求。对应 UE5 <c>UQuest::AddQuestRequirement_Implementation</c>。</summary>
        public void AddQuestRequirement(QuestRequirement requirement)
        {
            if (requirement == null || QuestRequirements.Contains(requirement)) return;
            QuestRequirements.Add(requirement);
            requirement.OnAdded(this);
        }

        /// <summary>运行时移除任务要求。对应 UE5 <c>UQuest::RemoveQuestRequirement_Implementation</c>。</summary>
        public void RemoveQuestRequirement(QuestRequirement requirement)
        {
            if (requirement == null) return;
            if (!QuestRequirements.Contains(requirement)) return;
            QuestRequirements.Remove(requirement);
            requirement.OnRemoved(this);
        }

        public void AddState(QuestState state)
        {
            if (state == null) return;
            state.OwningQuest = this;
            States.Add(state);
        }

        public void AddBranch(QuestBranch branch)
        {
            if (branch == null) return;
            branch.OwningQuest = this;
            Branches.Add(branch);
        }

        public void RemoveState(QuestState state)
        {
            if (state != null) States.Remove(state);
        }

        public void RemoveBranch(QuestBranch branch)
        {
            if (branch != null) Branches.Remove(branch);
        }

        public void SetQuestStartState(QuestState state)
        {
            QuestStartState = state;
        }

        public List<QuestState> GetStates() => States;
        public List<QuestBranch> GetBranches() => Branches;

        /// <summary>
        /// 任务 tick 驱动。对应 UE5 在 <c>UNarrativeTask::BeginTask</c> 里注册的 World Timer：
        /// Flax 无 World Timer，由 <see cref="TalesComponent.OnUpdate"/> 每帧对进行中的任务调用本方法，
        /// 任务再按自己的 <see cref="NarrativeTask.TickInterval"/> 节流。
        /// </summary>
        public void TickTasks(float deltaTime)
        {
            if (QuestCompletion != EQuestCompletion.Started) return;

            foreach (var branch in Branches)
            {
                if (branch?.QuestTasks == null) continue;
                foreach (var task in branch.QuestTasks)
                {
                    if (task != null && task.bIsActive)
                    {
                        task.AdvanceTaskTick(deltaTime);
                    }
                }
            }
        }

        /// <summary>对应 UE5 <c>UQuest::OnQuestTaskProgressChanged</c>（Quest.cpp:477-487）。</summary>
        public void OnQuestTaskProgressChanged(NarrativeTask task, QuestBranch branch, int oldProgress, int newProgress)
        {
            QuestTaskProgressChanged?.Invoke(this, task, branch, oldProgress, newProgress);
        }

        /// <summary>对应 UE5 <c>UQuest::OnQuestTaskCompleted</c>（Quest.cpp:489-499）。</summary>
        public void OnQuestTaskCompleted(NarrativeTask task, QuestBranch branch)
        {
            QuestTaskCompleted?.Invoke(this, task, branch);
        }

        /// <summary>对应 UE5 <c>UQuest::OnQuestBranchCompleted</c>（Quest.cpp:501-511）。</summary>
        public void OnQuestBranchCompleted(QuestBranch branch)
        {
            QuestBranchCompleted?.Invoke(this, branch);
        }
    }
}
