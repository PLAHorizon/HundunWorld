using System;
using System.Collections.Generic;
using System.Linq;
using NarrativePro.Core;
using NarrativePro.Tales.Nodes;
using NarrativePro.Tales.Tasks;

namespace NarrativePro.Tales.Quest
{
    public class Quest
    {
        public string QuestName { get; set; } = "";
        public string QuestDescription { get; set; } = "";
        public List<QuestState> States { get; set; } = new List<QuestState>();
        public List<QuestBranch> Branches { get; set; } = new List<QuestBranch>();
        public QuestState QuestStartState { get; set; }
        public QuestState CurrentState { get; set; }
        public EQuestCompletion QuestCompletion { get; set; } = EQuestCompletion.NotStarted;
        public List<QuestState> ReachedStates { get; set; } = new List<QuestState>();
        public List<QuestRequirement> QuestRequirements { get; set; } = new List<QuestRequirement>();
        public bool bTracked { get; set; } = false;
        public string QuestDialogueClassId { get; set; } = "";
        public object OwningComp { get; set; }
        public object OwningPawn { get; set; }
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

        private QuestBranch _activeBranch;
        private Dictionary<NarrativeTask, QuestBranch> _taskToBranchMap = new Dictionary<NarrativeTask, QuestBranch>();

        public void Initialize(object comp, string startID = "")
        {
            OwningComp = comp;

            foreach (var state in States)
            {
                state.OwningQuest = this;
                foreach (var branch in state.Branches)
                {
                    branch.OwningQuest = this;
                }
            }

            foreach (var req in QuestRequirements)
            {
                req.OnAdded(this);
            }
        }

        public void Deinitialize()
        {
            if (CurrentState != null)
            {
                CurrentState.Deactivate();
                DeactivateCurrentBranch();
            }

            QuestCompletion = EQuestCompletion.NotStarted;
            CurrentState = null;
            ReachedStates.Clear();
            _taskToBranchMap.Clear();

            foreach (var req in QuestRequirements)
            {
                req.OnRemoved(this);
            }
        }

        public void BeginQuest(string startFromID = "")
        {
            if (QuestCompletion == EQuestCompletion.Started) return;

            QuestState startState;
            if (!string.IsNullOrEmpty(startFromID))
            {
                startState = GetState(startFromID);
                if (startState == null) startState = QuestStartState;
            }
            else
            {
                startState = QuestStartState;
            }

            if (startState == null) return;

            QuestCompletion = EQuestCompletion.Started;
            QuestStarted?.Invoke(this);
            EnterState(startState);
        }

        public void EnterState(QuestState newState)
        {
            if (newState == null) return;

            if (CurrentState != null)
            {
                CurrentState.Deactivate();
                DeactivateCurrentBranch();
            }

            CurrentState = newState;
            if (!ReachedStates.Contains(newState))
            {
                ReachedStates.Add(newState);
            }

            newState.OwningQuest = this;
            newState.Activate();
            QuestNewState?.Invoke(this, newState);

            if (newState.StateNodeType == EStateNodeType.Success)
            {
                SucceedQuest();
            }
            else if (newState.StateNodeType == EStateNodeType.Failure)
            {
                FailQuest();
            }
        }

        public void TakeBranch(QuestBranch branch)
        {
            if (branch == null) return;

            DeactivateCurrentBranch();
            _activeBranch = branch;
            _taskToBranchMap.Clear();

            foreach (var task in branch.QuestTasks)
            {
                _taskToBranchMap[task] = branch;
                task.OwningComp = OwningComp;
                task.OwningPawn = OwningPawn;
                task.OwningController = OwningController;
                task.ProgressChangedCallback = OnTaskProgressChangedCallback;
            }

            branch.Activate();

            if (branch.AreTasksComplete())
            {
                OnQuestBranchCompleted(branch);
            }
        }

        private void OnTaskProgressChangedCallback(NarrativeTask task, int oldProgress, int newProgress)
        {
            QuestBranch branch = null;
            if (task != null && _taskToBranchMap.TryGetValue(task, out var b))
            {
                branch = b;
            }

            OnQuestTaskProgressChanged(task, branch, oldProgress, newProgress);

            if (task.IsComplete())
            {
                OnQuestTaskCompleted(task, branch);
            }
        }

        private void DeactivateCurrentBranch()
        {
            if (_activeBranch != null)
            {
                foreach (var task in _activeBranch.QuestTasks)
                {
                    task.ProgressChangedCallback = null;
                }
                _activeBranch.Deactivate();
                _activeBranch = null;
            }
            _taskToBranchMap.Clear();
        }

        public void SucceedQuest(string message = "")
        {
            if (QuestCompletion != EQuestCompletion.Started) return;
            QuestCompletion = EQuestCompletion.Succeeded;
            DeactivateCurrentBranch();
            QuestSucceeded?.Invoke(this);
        }

        public void FailQuest(string message = "")
        {
            if (QuestCompletion != EQuestCompletion.Started) return;
            QuestCompletion = EQuestCompletion.Failed;
            DeactivateCurrentBranch();
            QuestFailed?.Invoke(this);
        }

        public QuestState GetState(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            return States.FirstOrDefault(s => s.ID == id);
        }

        public QuestBranch GetBranch(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            return Branches.FirstOrDefault(b => b.ID == id);
        }

        public List<NarrativeNodeBase> GetNodes()
        {
            var nodes = new List<NarrativeNodeBase>();
            nodes.AddRange(States);
            nodes.AddRange(Branches);
            return nodes;
        }

        public void SetTracked(bool tracked)
        {
            bTracked = tracked;
        }

        public bool IsTracked()
        {
            return bTracked;
        }

        internal void OnQuestTaskProgressChanged(NarrativeTask task, QuestBranch branch, int oldProgress, int newProgress)
        {
            QuestTaskProgressChanged?.Invoke(this, task, branch, oldProgress, newProgress);
        }

        internal void OnQuestTaskCompleted(NarrativeTask task, QuestBranch branch)
        {
            QuestTaskCompleted?.Invoke(this, task, branch);

            if (branch != null && branch.AreTasksComplete())
            {
                OnQuestBranchCompleted(branch);
            }
        }

        internal void OnQuestBranchCompleted(QuestBranch branch)
        {
            QuestBranchCompleted?.Invoke(this, branch);

            if (branch.DestinationState != null)
            {
                EnterState(branch.DestinationState);
            }
        }
    }
}
