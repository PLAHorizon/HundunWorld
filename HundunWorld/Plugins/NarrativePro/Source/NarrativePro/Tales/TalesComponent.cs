using System;
using System.Collections.Generic;
using System.Linq;
using FlaxEngine;
using NarrativePro.Core;
using NarrativePro.Items;
using NarrativePro.Save;
using NarrativePro.Tales.Data;
using NarrativePro.Tales.Nodes;
using NarrativePro.Tales.Quest;
using NarrativePro.Tales.Tasks;
using NarrativePro.Tales.Dialogue;
using QuestClass = NarrativePro.Tales.Quest.Quest;
using DialogueClass = NarrativePro.Tales.Dialogue.Dialogue;

namespace NarrativePro.Tales
{
    /// <summary>
    /// 叙事系统组件（任务 + 对话运行时）。对应 UE5 <c>UTalesComponent</c>
    /// （TalesComponent.h / TalesComponent.cpp）。
    /// UE5 中本组件实现 INarrativeSavableComponent，且 QuestList/MasterTaskList/CurrentDialogue
    /// 均为 UPROPERTY(SaveGame)（TalesComponent.h:240,256,261），由存档系统自动收集。
    /// Flax 端口对齐方式：实现 <see cref="NarrativePro.SaveSystem.INarrativeSavableComponent"/>，
    /// PrepareForSave 把任务/对话进度快照进 <see cref="SavedNarrativeJson"/>（可被反射归档），
    /// Load 从快照还原；同时 NarrativeSaveBootstrap/子系统在存盘点位同步落业务 JSON 文件。
    /// </summary>
    public class TalesComponent : Script, NarrativePro.SaveSystem.INarrativeSavableComponent
    {
        private readonly List<QuestClass> _questList = new List<QuestClass>();
        private DialogueClass _currentDialogue;
        private readonly Dictionary<string, int> _masterTaskList = new Dictionary<string, int>();
        private readonly List<NarrativeUpdate> _pendingUpdateList = new List<NarrativeUpdate>();
        private bool _bIsLoading = false;

        /// <summary>
        /// 正在落地远端更新中（由 <c>NarrativeSyncManager</c> 设定）。
        /// 为 true 时 <see cref="SendNarrativeUpdate"/> 直接返回，防止回环重发。
        /// </summary>
        private bool _bApplyingRemoteUpdate = false;

        /// <summary>同步管理器专用开关；业务代码不应自行置位。</summary>
        internal void SetApplyingRemoteUpdate(bool value)
        {
            _bApplyingRemoteUpdate = value;
        }

        /// <summary>业务存档 IO（NarrativeSaveManager 的真实调用方：OnUpdate 自动存档 / 场景退出 / 世界存档）。</summary>
        private static readonly NarrativeSaveManager _narrativeSaveIO = new NarrativeSaveManager();

        /// <summary>业务级自动存档间隔（秒）。对应 UE5 SaveGame 侧的周期落盘。</summary>
        private const float BusinessAutosaveIntervalSeconds = 120f;

        private float _businessAutosaveTimer;

        /// <summary>
        /// 是否为远端代理组件。Flax 无 Actor 角色枚举：本地组件默认拥有权威，
        /// 只有被明确标记为远端代理时才交由服务器决定状态。对应 UE5 <c>GetOwnerRole() >= ROLE_Authority</c>。
        /// </summary>
        public bool bIsRemoteProxy { get; set; } = false;

        /// <summary>本组件所属的队伍组件（solo 组件加入队伍后写入）。对应 UE5 <c>PartyComponent</c>。</summary>
        public NarrativePartyComponent PartyComponent { get; protected set; }

        /// <summary>按标签记录的对话冷却时长。对应 UE5 <c>TaggedDialogueCooldowns</c>。</summary>
        public Dictionary<GameplayTag, float> TaggedDialogueCooldowns { get; set; } = new Dictionary<GameplayTag, float>();

        /// <summary>按标签记录的上次播放时间。对应 UE5 <c>TaggedDialogueLastPlayTimes</c>。</summary>
        public Dictionary<GameplayTag, float> TaggedDialogueLastPlayTimes { get; set; } = new Dictionary<GameplayTag, float>();

        /// <summary>冷却表里查不到标签时使用的兜底冷却。对应 UE5 <c>FallbackCooldown</c>（构造默认 10 秒）。</summary>
        public float FallbackCooldown { get; set; } = 10f;

        public List<QuestClass> QuestList => _questList;
        public DialogueClass CurrentDialogue => _currentDialogue;
        public Dictionary<string, int> MasterTaskList => _masterTaskList;
        public List<NarrativeUpdate> PendingUpdateList => _pendingUpdateList;
        public bool IsInDialogue => _currentDialogue != null && _currentDialogue.IsPlaying();

        /// <summary>读档标记：为 true 时禁止事件重放、委托广播与任务进度重置。</summary>
        public bool bIsLoading { get => _bIsLoading; set => _bIsLoading = value; }

        public event Action<TalesComponent, QuestClass> OnQuestStarted;
        public event Action<TalesComponent, QuestClass> OnQuestSucceeded;
        public event Action<TalesComponent, QuestClass> OnQuestFailed;
        public event Action<TalesComponent, QuestClass> OnQuestForgotten;
        public event Action<TalesComponent, QuestClass> OnQuestRestarted;
        public event Action<TalesComponent, QuestClass, QuestState> OnQuestNewState;
        public event Action<TalesComponent, QuestClass, NarrativeTask, QuestBranch, int, int> OnQuestTaskProgressChanged;
        public event Action<TalesComponent, QuestClass, NarrativeTask, QuestBranch> OnQuestTaskCompleted;
        public event Action<TalesComponent, QuestClass, QuestBranch> OnQuestBranchCompleted;

        public event Action<TalesComponent, DialogueClass> OnDialogueBegan;
        public event Action<TalesComponent, DialogueClass, EExitDialogueReason> OnDialogueFinished;
        public event Action<TalesComponent, DialogueClass, DialogueNode_NPC, DialogueLine, SpeakerInfo> OnNPCDialogueLineStarted;
        public event Action<TalesComponent, DialogueClass, DialogueNode_NPC, DialogueLine, SpeakerInfo> OnNPCDialogueLineFinished;
        public event Action<TalesComponent, DialogueClass, DialogueNode_Player, DialogueLine> OnPlayerDialogueLineStarted;
        public event Action<TalesComponent, DialogueClass, DialogueNode_Player, DialogueLine> OnPlayerDialogueLineFinished;
        public event Action<TalesComponent, DialogueClass, List<DialogueNode_Player>> OnDialogueRepliesAvailable;

        public event Action<TalesComponent, string, string, int> OnNarrativeDataTaskCompleted;

        public event Action<TalesComponent, DialogueClass, DialogueNode_Player> OnDialogueOptionSelected;

        // ===================================================================
        // 权威 / 网络形态
        // ===================================================================

        /// <summary>本地是否拥有权威。对应 UE5 <c>HasAuthority</c>。</summary>
        public virtual bool HasAuthority()
        {
            return !bIsRemoteProxy;
        }

        /// <summary>
        /// 是否单机（没有走网络）。对应 UE5 <c>GetNetMode() == NM_Standalone</c>：
        /// UE5 用它来抑制 RPC、抑制服务端拿不到的音频/序列结束事件。
        /// </summary>
        public virtual bool IsStandalone()
        {
            var sync = NarrativeProPlugin.Instance?.SyncManager;
            return sync == null || !sync.IsNetworked;
        }

        /// <summary>取所属队伍组件。对应 UE5 <c>GetParty</c>。</summary>
        public virtual NarrativePartyComponent GetParty()
        {
            return PartyComponent;
        }

        /// <summary>设置所属队伍组件（由 <see cref="NarrativePartyComponent"/> 调用）。</summary>
        public virtual void SetParty(NarrativePartyComponent party)
        {
            PartyComponent = party;
        }

        /// <summary>
        /// 把一条叙事更新交给同步管理器。对应 UE5 <c>SendNarrativeUpdate</c>（TalesComponent.cpp:907-920）。
        /// UE5 里它是 protected 且只有 UQuest 是 friend，Flax 需要公开给任务对象调用。
        /// </summary>
        public virtual void SendNarrativeUpdate(NarrativeUpdate update)
        {
            if (update == null) return;

            // 回环防护：正在落地远端更新时不得再次上行，否则两台权威端会就同一条更新互发不停。
            if (_bApplyingRemoteUpdate) return;

            // 只有「网络化 + 有权威」才需要排队复制（对应 UE5 的 HasAuthority() && NetMode != NM_Standalone）
            if (!HasAuthority() || IsStandalone()) return;

            _pendingUpdateList.Add(update);
            // Flax 的 Time 无 SystemTime：用引擎游戏时刻（对应 UE5 World->TimeSeconds）
            update.CreationTime = (float)Time.GameTime;
            update.bAcked = false;

            var sync = NarrativeProPlugin.Instance?.SyncManager;
            if (sync == null)
            {
                // 没有同步管理器接管：本地状态已生效，撤销入队并直接确认，
                // 否则该条目会永远留在 _pendingUpdateList（只增不减）。
                _pendingUpdateList.Remove(update);
                update.bAcked = true;
                return;
            }

            sync.SendNarrativeUpdate(update);
        }

        // ===================================================================
        // 任务
        // ===================================================================

        /// <summary>
        /// 开始任务。对应 UE5 <c>BeginQuest</c>（TalesComponent.cpp:197-222）：
        /// 先建实例并校验，再发网络更新，最后才 BeginQuest ——
        /// 顺序不能反，BeginQuest 自身会触发后续更新，必须排在 BeginQuest 更新之后。
        /// </summary>
        public QuestClass BeginQuest(string questClassId, string startFromId = "")
        {
            if (IsQuestStartedOrFinished(questClassId))
            {
                NarrativeLog.LogWarning($"任务 '{questClassId}' 已处于开始或结束状态，如需重玩请调用 RestartQuest");
                return GetQuestInstance(questClassId);
            }

            var quest = MakeQuestInstance(questClassId);
            if (quest == null)
            {
                NarrativeLog.LogError($"Failed to create quest instance for '{questClassId}'");
                return null;
            }

            // 对应 UE5:209-212 —— 读档期间不发更新（服务器会批量把任务发给客户端）
            if (!bIsLoading && HasAuthority() && !IsStandalone())
            {
                SendNarrativeUpdate(NarrativeUpdate.BeginQuest(questClassId, startFromId));
            }

            quest.BeginQuest(startFromId);

            return quest;
        }

        /// <summary>
        /// 重启任务。对应 UE5 <c>RestartQuest</c>（TalesComponent.cpp:258-283）。
        /// </summary>
        public bool RestartQuest(string questClassId, string startFromId = "")
        {
            for (int i = 0; i < _questList.Count; i++)
            {
                if (_questList[i] != null && _questList[i].QuestName == questClassId)
                {
                    var oldQuest = _questList[i];

                    OnQuestRestarted?.Invoke(this, oldQuest);

                    UnsubscribeFromQuestEvents(oldQuest);
                    oldQuest.Deinitialize();
                    _questList.RemoveAt(i);

                    BeginQuest(questClassId, startFromId);

                    if (HasAuthority() && !IsStandalone())
                    {
                        SendNarrativeUpdate(NarrativeUpdate.RestartQuest(questClassId, startFromId));
                    }

                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 遗忘任务。对应 UE5 <c>ForgetQuest</c>（TalesComponent.cpp:285-310）。
        /// </summary>
        public bool ForgetQuest(string questClassId)
        {
            for (int i = 0; i < _questList.Count; i++)
            {
                if (_questList[i] != null && _questList[i].QuestName == questClassId)
                {
                    var quest = _questList[i];

                    quest.Deinitialize();

                    OnQuestForgotten?.Invoke(this, quest);

                    UnsubscribeFromQuestEvents(quest);
                    _questList.RemoveAt(i);

                    if (HasAuthority() && !IsStandalone())
                    {
                        SendNarrativeUpdate(NarrativeUpdate.ForgetQuest(questClassId));
                    }

                    return true;
                }
            }

            return false;
        }

        public bool IsQuestStartedOrFinished(string questClassId)
        {
            return _questList.Any(q => q != null && q.QuestName == questClassId);
        }

        public bool IsQuestInProgress(string questClassId)
        {
            var quest = GetQuestInstance(questClassId);
            return quest != null && quest.QuestCompletion == EQuestCompletion.Started;
        }

        public bool IsQuestSucceeded(string questClassId)
        {
            var quest = GetQuestInstance(questClassId);
            return quest != null && quest.QuestCompletion == EQuestCompletion.Succeeded;
        }

        public bool IsQuestFailed(string questClassId)
        {
            var quest = GetQuestInstance(questClassId);
            return quest != null && quest.QuestCompletion == EQuestCompletion.Failed;
        }

        /// <summary>任务是否已经结束（成功或失败）。对应 UE5 <c>IsQuestFinished</c>。</summary>
        public bool IsQuestFinished(string questClassId)
        {
            var quest = GetQuestInstance(questClassId);
            return quest != null &&
                   (quest.QuestCompletion == EQuestCompletion.Succeeded || quest.QuestCompletion == EQuestCompletion.Failed);
        }

        public QuestClass GetQuestInstance(string questClassId)
        {
            return _questList.FirstOrDefault(q => q != null && q.QuestName == questClassId);
        }

        /// <summary>对应 UE5 <c>GetAllQuests</c>（TalesComponent.cpp:1287-1290）。</summary>
        public List<QuestClass> GetAllQuests()
        {
            return new List<QuestClass>(_questList);
        }

        public List<QuestClass> GetFailedQuests()
        {
            return _questList.Where(q => q != null && q.QuestCompletion == EQuestCompletion.Failed).ToList();
        }

        public List<QuestClass> GetSucceededQuests()
        {
            return _questList.Where(q => q != null && q.QuestCompletion == EQuestCompletion.Succeeded).ToList();
        }

        public List<QuestClass> GetInProgressQuests()
        {
            return _questList.Where(q => q != null && q.QuestCompletion == EQuestCompletion.Started).ToList();
        }

        /// <summary>
        /// 任务图合法性校验。对应 UE5 <c>IsQuestValid</c>（TalesComponent.cpp:1017-1064）：
        /// 空任务、重复开始、状态无 ID、状态 ID 重复都会拒绝。
        /// </summary>
        public bool IsQuestValid(QuestClass quest, out string error)
        {
            if (quest == null)
            {
                error = "Narrative was given a null quest asset.";
                return false;
            }

            // 已经复制回客户端过、或本地已在跑同一实例时，不能再开始一次
            foreach (var questInProgress in _questList)
            {
                if (ReferenceEquals(questInProgress, quest))
                {
                    if (questInProgress.QuestCompletion == EQuestCompletion.Succeeded)
                    {
                        error = "Narrative was given a quest that has already been completed. Use RestartQuest to replay quests";
                    }
                    else if (questInProgress.QuestCompletion == EQuestCompletion.Failed)
                    {
                        error = "Narrative was given a quest that has already been failed. Use RestartQuest to replay quests";
                    }
                    else
                    {
                        error = $"任务 '{quest.QuestName}' 已在任务列表中。";
                    }

                    return false;
                }
            }

            var stateNames = new HashSet<string>(StringComparer.Ordinal);

            foreach (var state in quest.States)
            {
                if (state == null)
                {
                    error = $"任务 '{quest.QuestName}' 含空状态节点。";
                    return false;
                }

                if (string.IsNullOrEmpty(state.GetID()))
                {
                    error = $"Narrative was given a quest {quest.QuestName} that has a state with no name. Please ensure all states are given names.";
                    return false;
                }

                if (!stateNames.Add(state.GetID()))
                {
                    error = $"Narrative was given a quest {quest.QuestName} that has more then one state with the same name. Please ensure all states have a unique name.";
                    return false;
                }
            }

            error = "";
            return true;
        }

        /// <summary>
        /// 创建任务实例。对应 UE5 <c>MakeQuestInstance</c>（TalesComponent.cpp:682-705）：
        /// 已存在实例要给警告；初始化失败不入列表；成功后订阅组件侧委托（网络更新由委托驱动）。
        /// </summary>
        public virtual QuestClass MakeQuestInstance(string questClassId)
        {
            if (GetQuestInstance(questClassId) != null)
            {
                NarrativeLog.LogWarning("Narrative was asked to begin a quest the player is already doing. Use RestartQuest() to replay a started quest.");
                return null;
            }

            QuestClass quest = null;
            try
            {
                var settings = NarrativeProPlugin.Instance?.NarrativeSettings;
                // 相对路径必须先经 NarrativeDataPaths 归一为绝对路径（编辑器/PIE/打包工作目录不一致），
                // 缺失文件的 "Has it been renamed?" 诊断由 NarrativeDataLoader.LoadQuest 统一给出。
                string path = System.IO.Path.Combine(settings?.DefaultQuestDirectory ?? "Content/NarrativePro/Quests", questClassId + ".json");
                quest = NarrativePro.Data.NarrativeDataLoader.LoadQuest(path);
            }
            catch (Exception ex)
            {
                NarrativeLog.LogError($"加载任务 '{questClassId}' 失败：{ex.Message}");
                return null;
            }

            if (quest == null) return null;

            if (!IsQuestValid(quest, out string validationError))
            {
                NarrativeLog.LogError($"任务 '{questClassId}' 校验失败：{validationError}");
                return null;
            }

            if (!quest.Initialize(this))
            {
                NarrativeLog.LogError($"任务 '{questClassId}' 初始化失败");
                return null;
            }

            SubscribeToQuestEvents(quest);
            _questList.Add(quest);

            return quest;
        }

        /// <summary>
        /// 完成数据任务。对应 UE5 <c>CompleteNarrativeDataTask</c>（TalesComponent.cpp:646-680）：
        /// 只有权威端能改任务状态；客户端调用要转成网络更新上报。
        /// </summary>
        public bool CompleteNarrativeDataTask(string taskName, string argument, int quantity = 1)
        {
            if (!HasAuthority())
            {
                if (!IsStandalone())
                {
                    SendNarrativeUpdate(NarrativeUpdate.CompleteTask("", taskName, quantity));
                }
                NarrativeLog.Log("客户端调用了 CompleteNarrativeDataTask。任务是服务器权威的，客户端只能上报。");
                return false;
            }

            if (string.IsNullOrEmpty(taskName) || string.IsNullOrEmpty(argument))
            {
                NarrativeLog.LogWarning("Narrative tried to process an Task that was empty, or argument was empty.");
                return false;
            }

            // 对应 UE5:657-664 —— 先查任务资产（走 NarrativeTaskManager 缓存，不重复加载）再广播
            var taskAsset = NarrativeFunctionLibrary.GetTaskByName(this, taskName);
            if (taskAsset == null)
            {
                NarrativeLog.LogWarning($"Narrative tried finding the asset for Task {taskName}, but couldn't find it.");
            }

            // UE5: TaskString = (TaskName + '_' + Argument).ToLower() 去空格 —— 用传入名而非资产名，保证键一致
            string taskString = new NarrativeDataTask { TaskName = taskName, ArgumentName = argument }.MakeTaskString(argument);

            if (!_masterTaskList.ContainsKey(taskString))
            {
                _masterTaskList[taskString] = 0;
            }
            _masterTaskList[taskString] += quantity;

            // 把数据任务映射进正在进行的任务的 GenericTask 进度
            foreach (var quest in _questList)
            {
                if (quest == null || quest.QuestCompletion != EQuestCompletion.Started) continue;

                foreach (var branch in quest.Branches)
                {
                    if (branch?.QuestTasks == null) continue;
                    foreach (var task in branch.QuestTasks)
                    {
                        if (task is GenericTask gt && gt.TaskTypeId == taskName)
                        {
                            if (string.IsNullOrEmpty(gt.TargetId) || gt.TargetId.Equals(argument, StringComparison.OrdinalIgnoreCase))
                            {
                                gt.AddProgress(quantity);
                            }
                        }
                    }
                }
            }

            OnNarrativeDataTaskCompleted?.Invoke(this, taskName, argument, quantity);
            return true;
        }

        /// <summary>
        /// 客户端收到服务器进度后的本地应用。对应 UE5 <c>CompleteNarrativeTask_Internal</c>（TalesComponent.cpp:707-724）。
        /// </summary>
        public bool CompleteNarrativeTaskInternal(string rawTaskString, bool bFromReplication, int quantity = 1)
        {
            if (!HasAuthority() && !bFromReplication) return false;

            if (_masterTaskList.TryGetValue(rawTaskString, out int current))
            {
                _masterTaskList[rawTaskString] = current + quantity;
            }
            else
            {
                _masterTaskList[rawTaskString] = quantity;
            }

            return true;
        }

        public bool HasCompletedTask(string taskName, string argument, int quantity = 1)
        {
            string taskString = new NarrativeDataTask { TaskName = taskName, ArgumentName = argument }.MakeTaskString(argument);
            return _masterTaskList.TryGetValue(taskString, out int count) && count >= quantity;
        }

        public int GetNumberOfTimesTaskWasCompleted(string taskName, string argument)
        {
            string taskString = new NarrativeDataTask { TaskName = taskName, ArgumentName = argument }.MakeTaskString(argument);
            return _masterTaskList.TryGetValue(taskString, out int count) ? count : 0;
        }

        // ===================================================================
        // 对话
        // ===================================================================

        /// <summary>
        /// 开始对话。对应 UE5 <c>BeginDialogue</c>（TalesComponent.cpp:355-382）：
        /// 权威端负责构造对话并算出首个 chunk，再把节点 ID 复制给非权威端。
        /// </summary>
        public virtual bool BeginDialogue(string dialogueClassId, DialoguePlayParams playParams = null)
        {
            if (!HasAuthority())
            {
                // 客户端不自行开始对话，等服务端的 ClientBeginDialogue（TalesComponent.cpp:357）
                NarrativeLog.Log("客户端尝试 BeginDialogue，已忽略：对话由服务器构造并复制。");
                return false;
            }

            if (SetCurrentDialogue(dialogueClassId, playParams))
            {
                OnDialogueBegan?.Invoke(this, _currentDialogue);

                if (!IsStandalone() && _currentDialogue != null)
                {
                    ClientBeginDialogue(dialogueClassId,
                        _currentDialogue.MakeIDsFromNPCNodes(_currentDialogue.NPCReplyChain),
                        _currentDialogue.MakeIDsFromPlayerNodes(_currentDialogue.AvailableResponses));
                }

                if (_currentDialogue != null)
                {
                    _currentDialogue.Play();
                }

                return true;
            }

            return false;
        }

        /// <summary>
        /// 设置当前对话（构造 + 初始化，但不播放）。对应 UE5 <c>SetCurrentDialogue</c>（TalesComponent.cpp:322-353）。
        /// </summary>
        public bool SetCurrentDialogue(string dialogueClassId, DialoguePlayParams playParams = null)
        {
            if (string.IsNullOrEmpty(dialogueClassId)) return false;

            if (_currentDialogue != null)
            {
                var newDialogue = MakeDialogueInstance(dialogueClassId, playParams ?? new DialoguePlayParams(), false);
                int newPriority = playParams != null && playParams.Priority != -1
                    ? playParams.Priority
                    : (newDialogue != null ? newDialogue.Priority : 0);

                if (newPriority > _currentDialogue.Priority)
                {
                    DialogueClass discarded = newDialogue;
                    discarded?.Deinitialize();
                    return false;
                }

                ExitDialogue(EExitDialogueReason.NewDialogueStarted);
            }

            _currentDialogue = MakeDialogueInstance(dialogueClassId, playParams, true);
            return _currentDialogue != null;
        }

        /// <summary>
        /// 检查开始该对话是否真的会有内容可播。对应 UE5 <c>HasDialogueAvailable</c>（TalesComponent.cpp:312-320）：
        /// 只有权威端能判断（旧实现只要资产能加载就返回 true）。
        /// </summary>
        public virtual bool HasDialogueAvailable(string dialogueClassId, DialoguePlayParams playParams = null)
        {
            if (string.IsNullOrEmpty(dialogueClassId)) return false;

            var dialogue = MakeDialogueInstance(dialogueClassId, playParams ?? new DialoguePlayParams(), false);
            bool bAvailable = dialogue != null;

            dialogue?.Deinitialize();

            return bAvailable;
        }

        /// <summary>对应 UE5 <c>GetCurrentDialogue</c>：在队伍里时优先返回队伍的对话。</summary>
        public DialogueClass GetCurrentDialogue()
        {
            var party = GetParty();
            if (party != null && party.CurrentDialogue != null)
            {
                return party.CurrentDialogue;
            }

            return _currentDialogue;
        }

        /// <summary>
        /// 权威端把「开始对话」连同首个 chunk 复制给本地/远端客户端。
        /// 对应 UE5 <c>ClientBeginDialogue_Implementation</c>（TalesComponent.cpp:384-400）。
        /// </summary>
        public void ClientBeginDialogue(string dialogueClassId, List<string> npcReplyChainIDs, List<string> availableResponseIDs)
        {
            if (string.IsNullOrEmpty(dialogueClassId) || HasAuthority()) return;

            if (SetCurrentDialogue(dialogueClassId))
            {
                // 客户端新建的对话没有 chunk，用服务端权威算好的那份
                _currentDialogue?.ClientReceiveDialogueChunk(npcReplyChainIDs, availableResponseIDs);

                OnDialogueBegan?.Invoke(this, _currentDialogue);
            }
        }

        /// <summary>
        /// 把服务端算好的 chunk 交给当前对话播放。
        /// 对应 UE5 <c>ClientRecieveDialogueChunk_Implementation</c>（TalesComponent.cpp:454-463）。
        /// </summary>
        public void ClientReceiveDialogueChunk(List<string> npcReplyChainIDs, List<string> availableResponseIDs)
        {
            if (HasAuthority()) return;

            _currentDialogue?.ClientReceiveDialogueChunk(npcReplyChainIDs, availableResponseIDs);
        }

        /// <summary>对应 UE5 <c>ClientExitDialogue_Implementation</c>（TalesComponent.cpp:432-442）。</summary>
        public void ClientExitDialogue(EExitDialogueReason reason)
        {
            if (_currentDialogue == null) return;

            OnDialogueFinished?.Invoke(this, _currentDialogue, reason);

            UnsubscribeFromDialogueEvents(_currentDialogue);
            _currentDialogue.Deinitialize();
            _currentDialogue = null;
        }

        /// <summary>
        /// 尝试退出对话。对应 UE5 <c>TryExitDialogue</c>（TalesComponent.cpp:465-488）：
        /// 必须先看 <c>CurrentDialogue->bCanBeExited</c>，否则玩家能退出设计为不可退出的对话。
        /// </summary>
        public bool TryExitDialogue(EExitDialogueReason reason = EExitDialogueReason.PlayerExited)
        {
            if (_currentDialogue == null || !_currentDialogue.bCanBeExited) return false;

            if (HasAuthority())
            {
                _currentDialogue.OwningComp?.ExitDialogue(reason);
            }
            else
            {
                ServerTryExitDialogue(reason);
            }

            return _currentDialogue == null;
        }

        /// <summary>非权威端请求服务器退出对话。对应 UE5 <c>ServerTryExitDialogue</c>。</summary>
        public void ServerTryExitDialogue(EExitDialogueReason reason)
        {
            if (IsStandalone())
            {
                TryExitDialogue(reason);
                return;
            }

            SendNarrativeUpdate(NarrativeUpdate.DialogueExit("", (int)reason));
        }

        /// <summary>
        /// 退出对话。UE5 中为 public（TalesComponent.h），C# 端口由对话对象与外部共同调用，保持 public。
        /// </summary>
        public virtual void ExitDialogue(EExitDialogueReason reason)
        {
            if (_currentDialogue == null) return;

            var dialogue = _currentDialogue;

            UnsubscribeFromDialogueEvents(dialogue);
            dialogue.ExitDialogue(reason);
            _currentDialogue = null;

            OnDialogueFinished?.Invoke(this, dialogue, reason);
        }

        /// <summary>对应 UE5 <c>IsInDialogue</c>。</summary>
        public bool InDialogue()
        {
            return _currentDialogue != null;
        }

        /// <summary>
        /// 尝试选择对话选项。对应 UE5 <c>TrySelectDialogueOption</c>（TalesComponent.cpp:517-537）：
        /// 非权威端要转成上报服务器，不能在本地自行改状态。
        /// </summary>
        public void TrySelectDialogueOption(DialogueNode_Player option)
        {
            if (_currentDialogue == null || !_currentDialogue.CanSelectDialogueOption(option)) return;

            if (HasAuthority())
            {
                var selector = GetOwningController();
                _currentDialogue.OwningComp?.SelectDialogueOption(option, selector);
            }
            else
            {
                ServerSelectDialogueOption(option.GetID());
            }
        }

        /// <summary>
        /// 权威端选择选项。对应 UE5 <c>SelectDialogueOption</c>（TalesComponent.cpp:539-550）。
        /// </summary>
        public virtual void SelectDialogueOption(DialogueNode_Player option, Actor selector = null)
        {
            if (!HasAuthority() || option == null || _currentDialogue == null) return;

            bool bSelectAccepted = _currentDialogue.SelectDialogueOption(option);

            if (bSelectAccepted && !IsStandalone())
            {
                ClientSelectDialogueOption(option.GetID(), selector);
            }
        }

        /// <summary>对应 UE5 <c>ClientSelectDialogueOption_Implementation</c>（TalesComponent.cpp:552-573）。</summary>
        public void ClientSelectDialogueOption(string optionID, Actor selector = null)
        {
            if (_currentDialogue == null) return;

            var option = _currentDialogue.GetPlayerReplyByID(optionID);
            if (option == null) return;

            _currentDialogue.SetPartyCurrentSpeaker(selector);

            if (!_currentDialogue.SelectDialogueOption(option))
            {
                NarrativeLog.LogWarning($"SelectDialogueOption 对选项 {optionID} 返回 false");
            }
        }

        /// <summary>对应 UE5 <c>ServerSelectDialogueOption_Implementation</c>（TalesComponent.cpp:575-589）：按 ID 还原选项对象。</summary>
        public void ServerSelectDialogueOption(string optionID)
        {
            if (_currentDialogue == null || string.IsNullOrEmpty(optionID)) return;

            var optionNode = _currentDialogue.GetPlayerReplyByID(optionID);
            if (optionNode != null)
            {
                TrySelectDialogueOption(optionNode);
            }
            else
            {
                NarrativeLog.LogWarning($"UNarrativeComponent::ServerSelectDialogueOption 无法解析对话选项 {optionID}。");
            }
        }

        /// <summary>
        /// 尝试跳过当前行。对应 UE5 <c>TrySkipCurrentDialogueLine</c>（TalesComponent.cpp:591-609）。
        /// </summary>
        public bool TrySkipCurrentDialogueLine()
        {
            if (_currentDialogue != null && _currentDialogue.CanSkipCurrentLine())
            {
                if (HasAuthority())
                {
                    return _currentDialogue.OwningComp?.SkipCurrentDialogueLine() ?? false;
                }

                ServerTrySkipCurrentDialogueLine();
                return true;
            }

            return false;
        }

        /// <summary>
        /// 跳过当前对话行。对应 UE5 <c>SkipCurrentDialogueLine</c>（TalesComponent.cpp:611-623）：
        /// 服务器跳过时会把新的整段 chunk 重发给客户端，保证端与端看到完全一致的内容。
        /// </summary>
        public bool SkipCurrentDialogueLine()
        {
            if (HasAuthority() && _currentDialogue != null)
            {
                return _currentDialogue.SkipCurrentLine();
            }

            return false;
        }

        /// <summary>对应 UE5 <c>ServerTrySkipCurrentDialogueLine</c>（TalesComponent.cpp:625-629）。</summary>
        public void ServerTrySkipCurrentDialogueLine()
        {
            if (IsStandalone())
            {
                TrySkipCurrentDialogueLine();
                return;
            }

            SendNarrativeUpdate(NarrativeUpdate.DialogueSkip(""));
        }

        /// <summary>
        /// 设置/查询按标签的对话冷却。对应 UE5 <c>TaggedDialogueCooldowns</c> /
        /// <c>TaggedDialogueLastPlayTimes</c> / <c>FallbackCooldown</c>（TalesComponent.h:276-287）。
        /// </summary>
        public void SetTaggedDialogueCooldown(GameplayTag tag, float cooldownSeconds)
        {
            if (!tag.IsValid()) return;
            TaggedDialogueCooldowns[tag] = cooldownSeconds;
        }

        /// <summary>取该标签对话的冷却秒数，表里没有时用 <see cref="FallbackCooldown"/>。</summary>
        public float GetTaggedDialogueCooldown(GameplayTag tag)
        {
            if (tag.IsValid() && TaggedDialogueCooldowns.TryGetValue(tag, out float cooldown))
            {
                return cooldown;
            }

            return FallbackCooldown;
        }

        /// <summary>该标签对话现在是否允许播放（冷却是否已过）。</summary>
        public bool CanPlayTaggedDialogue(GameplayTag tag, float currentTime)
        {
            if (!tag.IsValid()) return false;

            if (!TaggedDialogueLastPlayTimes.TryGetValue(tag, out float lastPlayTime))
            {
                return true;
            }

            return currentTime - lastPlayTime >= GetTaggedDialogueCooldown(tag);
        }

        /// <summary>记录该标签对话的播放时间（用于冷却）。</summary>
        public void MarkTaggedDialoguePlayed(GameplayTag tag, float currentTime)
        {
            if (!tag.IsValid()) return;
            TaggedDialogueLastPlayTimes[tag] = currentTime;
        }

        // ===================================================================
        // 组件生命周期
        // ===================================================================

        /// <summary>获取拥有此组件的 Pawn（UE5 APawn）。Flax 中默认返回挂载此 Script 的 Actor。</summary>
        public virtual Actor GetOwningPawn()
        {
            return Actor;
        }

        /// <summary>获取拥有此组件的 Controller（UE5 APlayerController）。Flax 中优先取场景中的玩家控制器。</summary>
        public virtual Actor GetOwningController()
        {
            if (_ownerController == null)
            {
                _ownerController = Level.FindScript<UnrealFramework.NarrativePlayerController>()?.Actor;
            }

            return _ownerController;
        }

        private Actor _ownerController;

        // ===================================================================
        // 存档往返（对应 UE5 INarrativeSavableComponent + UPROPERTY(SaveGame) 自动收集）
        // ===================================================================

        /// <summary>
        /// 世界存档管道写入/回读的业务快照（JSON）。
        /// 对应 UE5 QuestList / MasterTaskList / CurrentDialogue 的 SaveGame 序列化，
        /// 由 NarrativeSaveSubsystem 经 NarrativeSaveStateArchive 反射归档（string 字段受支持）。
        /// </summary>
        public string SavedNarrativeJson = string.Empty;

        /// <summary>本组件的业务进度是否已被某条存档路径还原（防止 OnEnable 文件回读与世界档重复还原）。</summary>
        public bool bNarrativeSaveRestored { get; private set; }

        /// <summary>业务存档文件路径（与 PerformLoad 一致，供 Bootstrap/子系统退出与自动存档落盘）。</summary>
        public string GetNarrativeSaveFilePath()
        {
            var settings = NarrativeProPlugin.Instance?.NarrativeSettings;
            return NarrativePro.Data.NarrativeDataPaths.Resolve(System.IO.Path.Combine(
                settings?.DefaultQuestDirectory ?? "Content/NarrativePro",
                settings?.SaveSlotName ?? "NarrativeSaveData",
                "save.json"
            ));
        }

        /// <summary>INarrativeSavableComponent.PrepareForSave：快照进 SavedNarrativeJson（UE5 自动收集等价物）。</summary>
        void NarrativePro.SaveSystem.INarrativeSavableComponent.PrepareForSave()
        {
            SavedNarrativeJson = _narrativeSaveIO.SerializeToJson(PrepareForSave());
        }

        /// <summary>INarrativeSavableComponent.Load：从 SavedNarrativeJson 还原任务/对话进度。</summary>
        void NarrativePro.SaveSystem.INarrativeSavableComponent.Load()
        {
            if (string.IsNullOrEmpty(SavedNarrativeJson)) return;

            var saveData = _narrativeSaveIO.DeserializeFromJson(SavedNarrativeJson);
            if (saveData == null) return;

            _narrativeSaveIO.LoadNarrativeState(this, saveData);
            PostLoadQuests();
            bNarrativeSaveRestored = true;
        }

        /// <summary>业务存档是否有值得落盘的进度。</summary>
        public bool HasNarrativeStateToSave()
        {
            return _questList.Count > 0 || _masterTaskList.Count > 0 || bNarrativeSaveRestored;
        }

        /// <summary>把当前任务/对话进度写入业务 JSON 文件（NarrativeSaveManager.SaveToFile 的真实调用方之一）。</summary>
        public bool SaveNarrativeStateToJson()
        {
            // 远端代理组件不落盘：本地没有权威进度，落盘只会覆盖权威端数据
            if (bIsLoading || !HasAuthority() || !HasNarrativeStateToSave()) return false;
            return _narrativeSaveIO.SaveToFile(PrepareForSave(), GetNarrativeSaveFilePath());
        }

        public NarrativeSaveData PrepareForSave()
        {
            return _narrativeSaveIO.SaveNarrativeState(this);
        }

        public void PerformLoad()
        {
            var saveData = _narrativeSaveIO.LoadFromFile(GetNarrativeSaveFilePath());
            if (saveData == null) return;

            _narrativeSaveIO.LoadNarrativeState(this, saveData);
            PostLoadQuests();
            bNarrativeSaveRestored = true;
        }

        /// <summary>对应 UE5 <c>QuestPostLoad</c> 的组件侧入口：读档完成后让任务重新注册标记/续播对话。</summary>
        public void PostLoadQuests()
        {
            foreach (var quest in _questList)
            {
                quest?.QuestPostLoad();
            }
        }

        public override void OnEnable()
        {
            base.OnEnable();
            NarrativeProPlugin.Instance?.SetActiveTalesComponent(this);
            NarrativeLog.Log("TalesComponent enabled");

            // 业务存档回读钩子（PerformLoad 此前零调用）：下一帧尝试，
            // 若世界档管道已经通过 SavedNarrativeJson 还原过则跳过，避免重复还原。
            Scripting.InvokeOnUpdate(() =>
            {
                if (bNarrativeSaveRestored) return;
                if (NarrativeProPlugin.Instance?.ActiveTalesComponent != this) return;
                if (!HasAuthority()) return;
                if (System.IO.File.Exists(GetNarrativeSaveFilePath()))
                {
                    PerformLoad();
                }
            });
        }

        public override void OnDisable()
        {
            base.OnDisable();
            if (IsInDialogue)
            {
                ExitDialogue(EExitDialogueReason.PlayerExited);
            }
            // 组件停用（含场景退出）前把任务/对话进度落盘（NarrativeSaveManager.SaveToFile 真实调用方）
            SaveNarrativeStateToJson();
            // 从插件的激活组件注册表注销（仅当自己仍是激活项时生效）
            NarrativeProPlugin.Instance?.ClearActiveTalesComponent(this);
            NarrativeLog.Log("TalesComponent disabled");
        }

        public override void OnUpdate()
        {
            base.OnUpdate();

            if (_currentDialogue != null && _currentDialogue.IsPlaying())
            {
                _currentDialogue.TickDialogue(Time.DeltaTime);
            }

            // 任务 tick 驱动：UE5 由 World Timer 周期回调 TickTask，Flax 无该机制，在此逐帧推进
            float deltaTime = Time.DeltaTime;
            foreach (var quest in _questList)
            {
                quest?.TickTasks(deltaTime);
            }

            // 业务级自动存档：周期性把任务/对话进度落盘（缺陷 1 的退出/自动存档钩子之一）
            _businessAutosaveTimer += deltaTime;
            if (_businessAutosaveTimer >= BusinessAutosaveIntervalSeconds)
            {
                _businessAutosaveTimer = 0f;
                SaveNarrativeStateToJson();
            }

            // Tick NarrativeSyncManager（定期刷新待发送的网络更新）
            NarrativeProPlugin.Instance?.SyncManager?.TickSync(deltaTime);
        }

        // ===================================================================
        // 对话实例创建
        // ===================================================================

        /// <summary>
        /// 创建并初始化对话实例。对应 UE5 <c>MakeDialogueInstance</c>（TalesComponent.cpp:726-745）。
        /// </summary>
        /// <param name="bSubscribeEvents">是否订阅对话事件（探测可用性时不需要）。</param>
        public virtual DialogueClass MakeDialogueInstance(string dialogueClassId, DialoguePlayParams playParams = null, bool bSubscribeEvents = true)
        {
            if (string.IsNullOrEmpty(dialogueClassId)) return null;

            DialogueClass dialogue;
            try
            {
                var settings = NarrativeProPlugin.Instance?.NarrativeSettings;
                string path = System.IO.Path.Combine(settings?.DefaultDialogueDirectory ?? "Content/NarrativePro/Dialogues", dialogueClassId + ".json");
                dialogue = NarrativePro.Data.NarrativeDataLoader.LoadDialogue(path);
            }
            catch (Exception ex)
            {
                NarrativeLog.LogError($"加载对话 '{dialogueClassId}' 失败：{ex.Message}");
                return null;
            }

            if (dialogue == null) return null;

            if (!dialogue.Initialize(this, playParams ?? new DialoguePlayParams()))
            {
                return null;
            }

            if (bSubscribeEvents)
            {
                SubscribeToDialogueEvents(dialogue);
            }

            return dialogue;
        }

        /// <summary>兼容旧签名（不传播放参数）的对话实例创建。</summary>
        protected internal DialogueClass MakeDialogueInstance(string dialogueClassId)
        {
            return MakeDialogueInstance(dialogueClassId, null, true);
        }

        // ===================================================================
        // 事件订阅与转发
        // ===================================================================

        private void SubscribeToQuestEvents(QuestClass quest)
        {
            quest.QuestStarted += OnQuestStartedInternal;
            quest.QuestSucceeded += OnQuestSucceededInternal;
            quest.QuestFailed += OnQuestFailedInternal;
            quest.QuestForgotten += OnQuestForgottenInternal;
            quest.QuestRestarted += OnQuestRestartedInternal;
            quest.QuestNewState += OnQuestNewStateInternal;
            quest.QuestTaskProgressChanged += OnQuestTaskProgressChangedInternal;
            quest.QuestTaskCompleted += OnQuestTaskCompletedInternal;
            quest.QuestBranchCompleted += OnQuestBranchCompletedInternal;
        }

        private void UnsubscribeFromQuestEvents(QuestClass quest)
        {
            quest.QuestStarted -= OnQuestStartedInternal;
            quest.QuestSucceeded -= OnQuestSucceededInternal;
            quest.QuestFailed -= OnQuestFailedInternal;
            quest.QuestForgotten -= OnQuestForgottenInternal;
            quest.QuestRestarted -= OnQuestRestartedInternal;
            quest.QuestNewState -= OnQuestNewStateInternal;
            quest.QuestTaskProgressChanged -= OnQuestTaskProgressChangedInternal;
            quest.QuestTaskCompleted -= OnQuestTaskCompletedInternal;
            quest.QuestBranchCompleted -= OnQuestBranchCompletedInternal;
        }

        private void SubscribeToDialogueEvents(DialogueClass dialogue)
        {
            dialogue.OnNPCDialogueLineStarted += OnNPCDialogueLineStartedInternal;
            dialogue.OnNPCDialogueLineFinished += OnNPCDialogueLineFinishedInternal;
            dialogue.OnPlayerDialogueLineStarted += OnPlayerDialogueLineStartedInternal;
            dialogue.OnPlayerDialogueLineFinished += OnPlayerDialogueLineFinishedInternal;
            dialogue.OnDialogueRepliesAvailable += OnDialogueRepliesAvailableInternal;
            dialogue.OnPlayerDialogueLineStarted += OnDialogueOptionSelectedInternal;
            dialogue.OnEndDialogue += OnDialogueEndInternal;
        }

        private void UnsubscribeFromDialogueEvents(DialogueClass dialogue)
        {
            dialogue.OnNPCDialogueLineStarted -= OnNPCDialogueLineStartedInternal;
            dialogue.OnNPCDialogueLineFinished -= OnNPCDialogueLineFinishedInternal;
            dialogue.OnPlayerDialogueLineStarted -= OnPlayerDialogueLineStartedInternal;
            dialogue.OnPlayerDialogueLineFinished -= OnPlayerDialogueLineFinishedInternal;
            dialogue.OnDialogueRepliesAvailable -= OnDialogueRepliesAvailableInternal;
            dialogue.OnPlayerDialogueLineStarted -= OnDialogueOptionSelectedInternal;
            dialogue.OnEndDialogue -= OnDialogueEndInternal;
        }

        private void OnQuestStartedInternal(QuestClass quest)
        {
            OnQuestStarted?.Invoke(this, quest);
        }

        private void OnQuestSucceededInternal(QuestClass quest)
        {
            OnQuestSucceeded?.Invoke(this, quest);
        }

        private void OnQuestFailedInternal(QuestClass quest)
        {
            OnQuestFailed?.Invoke(this, quest);
        }

        /// <summary>对应 UE5 <c>QuestForgotten</c>（TalesComponent.cpp:1083-1089）。</summary>
        private void OnQuestForgottenInternal(QuestClass quest)
        {
            OnQuestForgotten?.Invoke(this, quest);
        }

        /// <summary>对应 UE5 <c>RestartQuest</c> 里先广播 OnQuestRestarted（TalesComponent.cpp:269）。</summary>
        private void OnQuestRestartedInternal(QuestClass quest)
        {
            OnQuestRestarted?.Invoke(this, quest);
        }

        /// <summary>对应 UE5 <c>QuestNewState</c>（TalesComponent.cpp:1108-1125）。</summary>
        private void OnQuestNewStateInternal(QuestClass quest, QuestState state)
        {
            if (quest == null) return;

            if (state != null)
            {
                NarrativeLog.Log($"Reached new state: {state.Description} in quest: {quest.QuestName}");
            }

            OnQuestNewState?.Invoke(this, quest, state);

            // UE5 这里刻意不发送：客户端可以由进度达标自行推进状态
        }

        /// <summary>
        /// 对应 UE5 <c>QuestTaskProgressMade</c>（TalesComponent.cpp:1127-1149）：
        /// 把「哪条分支的第几个任务到了多少进度」复制给其他端。
        /// </summary>
        private void OnQuestTaskProgressChangedInternal(QuestClass quest, NarrativeTask task, QuestBranch branch, int oldProgress, int newProgress)
        {
            if (quest == null || task == null || branch == null) return;

            NarrativeLog.Log($"Quest {quest.QuestName} made progress - task {task.GetTaskDescription()} is now {newProgress}/{task.RequiredQuantity}");

            OnQuestTaskProgressChanged?.Invoke(this, quest, task, branch, oldProgress, newProgress);

            byte taskIdx = 0;
            foreach (var questTask in branch.QuestTasks)
            {
                if (ReferenceEquals(task, questTask)) break;
                taskIdx++;
            }

            if (HasAuthority() && !IsStandalone())
            {
                // 进度按 UE5 走 byte 载荷（IntPayload），超界需钳制而不是回绕
                byte clampedProgress = newProgress < 0 ? (byte)0 : (newProgress > byte.MaxValue ? byte.MaxValue : (byte)newProgress);
                SendNarrativeUpdate(NarrativeUpdate.TaskProgressMade(quest.QuestName, taskIdx, clampedProgress, branch.GetID()));
            }
        }

        /// <summary>对应 UE5 <c>QuestTaskCompleted</c>（TalesComponent.cpp:1151-1155）。</summary>
        private void OnQuestTaskCompletedInternal(QuestClass quest, NarrativeTask task, QuestBranch branch)
        {
            OnQuestTaskCompleted?.Invoke(this, quest, task, branch);
        }

        /// <summary>对应 UE5 <c>QuestBranchCompleted</c>（TalesComponent.cpp:1156-1160）。</summary>
        private void OnQuestBranchCompletedInternal(QuestClass quest, QuestBranch branch)
        {
            OnQuestBranchCompleted?.Invoke(this, quest, branch);
        }

        private void OnNPCDialogueLineStartedInternal(DialogueClass dialogue, DialogueNode_NPC node, DialogueLine line, SpeakerInfo speaker)
        {
            OnNPCDialogueLineStarted?.Invoke(this, dialogue, node, line, speaker);
        }

        private void OnNPCDialogueLineFinishedInternal(DialogueClass dialogue, DialogueNode_NPC node, DialogueLine line, SpeakerInfo speaker)
        {
            OnNPCDialogueLineFinished?.Invoke(this, dialogue, node, line, speaker);
        }

        private void OnPlayerDialogueLineStartedInternal(DialogueClass dialogue, DialogueNode_Player node, DialogueLine line)
        {
            OnPlayerDialogueLineStarted?.Invoke(this, dialogue, node, line);
        }

        private void OnPlayerDialogueLineFinishedInternal(DialogueClass dialogue, DialogueNode_Player node, DialogueLine line)
        {
            OnPlayerDialogueLineFinished?.Invoke(this, dialogue, node, line);
        }

        private void OnDialogueRepliesAvailableInternal(DialogueClass dialogue, List<DialogueNode_Player> replies)
        {
            OnDialogueRepliesAvailable?.Invoke(this, dialogue, replies);
        }

        private void OnDialogueOptionSelectedInternal(DialogueClass dialogue, DialogueNode_Player node, DialogueLine line)
        {
            OnDialogueOptionSelected?.Invoke(this, dialogue, node);
        }

        private void OnDialogueEndInternal(DialogueClass dialogue)
        {
            if (_currentDialogue == dialogue)
            {
                UnsubscribeFromDialogueEvents(dialogue);
                _currentDialogue = null;
                OnDialogueFinished?.Invoke(this, dialogue, EExitDialogueReason.NoLines);
            }
        }
    }
}
