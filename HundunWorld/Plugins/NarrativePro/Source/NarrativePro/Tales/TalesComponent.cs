using System;
using System.Collections.Generic;
using System.Linq;
using FlaxEngine;
using NarrativePro.Core;
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
    public class TalesComponent : Script
    {
        private List<QuestClass> _questList = new List<QuestClass>();
        private DialogueClass _currentDialogue;
        private Dictionary<string, int> _masterTaskList = new Dictionary<string, int>();
        private List<NarrativeUpdate> _pendingUpdateList = new List<NarrativeUpdate>();
        private bool _bIsLoading = false;

        public List<QuestClass> QuestList => _questList;
        public DialogueClass CurrentDialogue => _currentDialogue;
        public Dictionary<string, int> MasterTaskList => _masterTaskList;
        public bool IsInDialogue => _currentDialogue != null && _currentDialogue.IsPlaying();
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

        public QuestClass BeginQuest(string questClassId, string startFromId = "")
        {
            var quest = MakeQuestInstance(questClassId);
            if (quest == null)
            {
                NarrativeLog.LogError($"Failed to create quest instance for '{questClassId}'");
                return null;
            }

            if (IsQuestStartedOrFinished(questClassId))
            {
                NarrativeLog.LogWarning($"Quest '{questClassId}' is already started or finished");
                return GetQuestInstance(questClassId);
            }

            quest.Initialize(this, startFromId);
            _questList.Add(quest);

            SubscribeToQuestEvents(quest);

            quest.BeginQuest(startFromId);
            OnQuestStarted?.Invoke(this, quest);

            return quest;
        }

        public bool RestartQuest(string questClassId, string startFromId = "")
        {
            var quest = GetQuestInstance(questClassId);
            if (quest == null) return false;

            quest.Deinitialize();
            quest.Initialize(this, startFromId);
            quest.BeginQuest(startFromId);
            OnQuestRestarted?.Invoke(this, quest);
            return true;
        }

        public bool ForgetQuest(string questClassId)
        {
            var quest = GetQuestInstance(questClassId);
            if (quest == null) return false;

            quest.Deinitialize();
            _questList.Remove(quest);
            OnQuestForgotten?.Invoke(this, quest);
            return true;
        }

        public bool IsQuestStartedOrFinished(string questClassId)
        {
            return _questList.Any(q => q.QuestName == questClassId);
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

        public QuestClass GetQuestInstance(string questClassId)
        {
            return _questList.FirstOrDefault(q => q.QuestName == questClassId);
        }

        public List<QuestClass> GetFailedQuests()
        {
            return _questList.Where(q => q.QuestCompletion == EQuestCompletion.Failed).ToList();
        }

        public List<QuestClass> GetSucceededQuests()
        {
            return _questList.Where(q => q.QuestCompletion == EQuestCompletion.Succeeded).ToList();
        }

        public List<QuestClass> GetInProgressQuests()
        {
            return _questList.Where(q => q.QuestCompletion == EQuestCompletion.Started).ToList();
        }

        public virtual bool BeginDialogue(string dialogueClassId, DialoguePlayParams playParams = null)
        {
            if (IsInDialogue)
            {
                if (_currentDialogue.Priority >= (playParams?.Priority ?? 0))
                {
                    NarrativeLog.LogWarning($"Cannot start dialogue '{dialogueClassId}': current dialogue has higher or equal priority");
                    return false;
                }
                ExitDialogue(EExitDialogueReason.NewDialogueStarted);
            }

            var dialogue = MakeDialogueInstance(dialogueClassId);
            if (dialogue == null)
            {
                NarrativeLog.LogError($"Failed to create dialogue instance for '{dialogueClassId}'");
                return false;
            }

            _currentDialogue = dialogue;
            if (playParams == null) playParams = new DialoguePlayParams();

            SubscribeToDialogueEvents(dialogue);

            dialogue.Initialize(this, playParams);
            dialogue.Play();

            OnDialogueBegan?.Invoke(this, dialogue);
            return true;
        }

        public bool HasDialogueAvailable(string dialogueClassId, DialoguePlayParams playParams = null)
        {
            var dialogue = MakeDialogueInstance(dialogueClassId);
            return dialogue != null;
        }

        public DialogueClass GetCurrentDialogue()
        {
            return _currentDialogue;
        }

        /// <summary>获取拥有此组件的 Pawn（UE5 APawn）。Flax 中默认返回挂载此 Script 的 Actor。</summary>
        public virtual Actor GetOwningPawn()
        {
            return Actor;
        }

        /// <summary>获取拥有此组件的 Controller（UE5 APlayerController）。Flax 中无 PlayerController 概念，默认返回 null。</summary>
        public virtual Actor GetOwningController()
        {
            return null;
        }

        public virtual void SelectDialogueOption(DialogueNode_Player option, Actor selector = null)
        {
            TrySelectDialogueOption(option);
        }

        public void TrySelectDialogueOption(DialogueNode_Player option)
        {
            if (_currentDialogue == null) return;
            _currentDialogue.SelectDialogueOption(option);
        }

        public bool TrySkipCurrentDialogueLine()
        {
            if (_currentDialogue == null) return false;
            return _currentDialogue.SkipCurrentLine();
        }

        public bool TryExitDialogue(EExitDialogueReason reason = EExitDialogueReason.PlayerExited)
        {
            if (_currentDialogue == null) return false;
            ExitDialogue(reason);
            return true;
        }

        /// <summary>
        /// 退出当前对话。protected virtual 以便 NarrativePartyComponent 等子类重写以同步给队伍成员。
        /// 子类应在调用 base.ExitDialogue 后执行自己的同步逻辑。
        /// </summary>
        protected virtual void ExitDialogue(EExitDialogueReason reason)
        {
            if (_currentDialogue == null) return;

            var dialogue = _currentDialogue;
            UnsubscribeFromDialogueEvents(dialogue);
            dialogue.ExitDialogue(reason);
            _currentDialogue = null;

            OnDialogueFinished?.Invoke(this, dialogue, reason);
        }

        public bool CompleteNarrativeDataTask(string taskName, string argument, int quantity = 1)
        {
            string taskString = new NarrativeDataTask { TaskName = taskName, ArgumentName = argument }.MakeTaskString(argument);

            if (!_masterTaskList.ContainsKey(taskString))
            {
                _masterTaskList[taskString] = 0;
            }
            _masterTaskList[taskString] += quantity;

            foreach (var quest in _questList)
            {
                if (quest.QuestCompletion != EQuestCompletion.Started) continue;

                foreach (var branch in quest.Branches)
                {
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

        protected virtual QuestClass MakeQuestInstance(string questClassId)
        {
            try
            {
                var settings = NarrativeProPlugin.Instance?.NarrativeSettings;
                string path = System.IO.Path.Combine(settings?.DefaultQuestDirectory ?? "Content/NarrativePro/Quests", questClassId + ".json");
                return QuestFactory.LoadQuest(path);
            }
            catch
            {
                return null;
            }
        }

        protected virtual DialogueClass MakeDialogueInstance(string dialogueClassId)
        {
            try
            {
                var settings = NarrativeProPlugin.Instance?.NarrativeSettings;
                string path = System.IO.Path.Combine(settings?.DefaultDialogueDirectory ?? "Content/NarrativePro/Dialogues", dialogueClassId + ".json");
                return DialogueFactory.LoadDialogue(path);
            }
            catch
            {
                return null;
            }
        }

        public NarrativeSaveData PrepareForSave()
        {
            var saveManager = new NarrativeSaveManager();
            return saveManager.SaveNarrativeState(this);
        }

        public void PerformLoad()
        {
            var settings = NarrativeProPlugin.Instance?.NarrativeSettings;
            string savePath = System.IO.Path.Combine(
                settings?.DefaultQuestDirectory ?? "Content/NarrativePro",
                settings?.SaveSlotName ?? "NarrativeSaveData",
                "save.json"
            );
            var saveManager = new NarrativeSaveManager();
            var saveData = saveManager.LoadFromFile(savePath);
            if (saveData != null)
            {
                saveManager.LoadNarrativeState(this, saveData);
            }
        }

        public override void OnEnable()
        {
            base.OnEnable();
            NarrativeLog.Log("TalesComponent enabled");
        }

        public override void OnDisable()
        {
            base.OnDisable();
            if (IsInDialogue)
            {
                ExitDialogue(EExitDialogueReason.PlayerExited);
            }
            NarrativeLog.Log("TalesComponent disabled");
        }

        public override void OnUpdate()
        {
            base.OnUpdate();
            if (_currentDialogue != null && _currentDialogue.IsPlaying())
            {
                _currentDialogue.TickDialogue(Time.DeltaTime);
            }

            // Tick NarrativeSyncManager（定期刷新待发送的网络更新）
            NarrativeProPlugin.Instance?.SyncManager?.TickSync(Time.DeltaTime);
        }

        private void SubscribeToQuestEvents(QuestClass quest)
        {
            quest.QuestSucceeded += OnQuestSucceededInternal;
            quest.QuestFailed += OnQuestFailedInternal;
            quest.QuestNewState += OnQuestNewStateInternal;
            quest.QuestTaskProgressChanged += OnQuestTaskProgressChangedInternal;
            quest.QuestTaskCompleted += OnQuestTaskCompletedInternal;
            quest.QuestBranchCompleted += OnQuestBranchCompletedInternal;
        }

        private void UnsubscribeFromQuestEvents(QuestClass quest)
        {
            quest.QuestSucceeded -= OnQuestSucceededInternal;
            quest.QuestFailed -= OnQuestFailedInternal;
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

        private void OnQuestSucceededInternal(QuestClass quest)
        {
            OnQuestSucceeded?.Invoke(this, quest);
        }

        private void OnQuestFailedInternal(QuestClass quest)
        {
            OnQuestFailed?.Invoke(this, quest);
        }

        private void OnQuestNewStateInternal(QuestClass quest, QuestState state)
        {
            OnQuestNewState?.Invoke(this, quest, state);
        }

        private void OnQuestTaskProgressChangedInternal(QuestClass quest, NarrativeTask task, QuestBranch branch, int oldProgress, int newProgress)
        {
            OnQuestTaskProgressChanged?.Invoke(this, quest, task, branch, oldProgress, newProgress);
        }

        private void OnQuestTaskCompletedInternal(QuestClass quest, NarrativeTask task, QuestBranch branch)
        {
            OnQuestTaskCompleted?.Invoke(this, quest, task, branch);
        }

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
