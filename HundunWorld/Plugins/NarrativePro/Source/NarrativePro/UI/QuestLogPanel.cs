using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Core;
using NarrativePro.Tales;
using NarrativePro.Tales.Nodes;
using NarrativePro.Tales.Tasks;
using QuestClass = NarrativePro.Tales.Quest.Quest;

namespace NarrativePro.UI
{
    public class QuestLogPanel : Script
    {
        public TalesComponent TalesComponentRef;
        public bool bIsVisible;

        private List<QuestDisplayInfo> _displayedQuests = new List<QuestDisplayInfo>();
        private int _selectedQuestIndex = -1;

        public class QuestDisplayInfo
        {
            public string QuestName;
            public string QuestDescription;
            public EQuestCompletion CompletionStatus;
            public string CurrentStateDescription;
            public List<string> TaskProgressTexts = new List<string>();
        }

        public override void OnEnable()
        {
            if (TalesComponentRef != null)
            {
                TalesComponentRef.OnQuestStarted += OnQuestStarted;
                TalesComponentRef.OnQuestSucceeded += OnQuestSucceeded;
                TalesComponentRef.OnQuestFailed += OnQuestFailed;
                TalesComponentRef.OnQuestNewState += OnQuestNewState;
                TalesComponentRef.OnQuestTaskProgressChanged += OnQuestTaskProgressChanged;
            }
        }

        public override void OnDisable()
        {
            if (TalesComponentRef != null)
            {
                TalesComponentRef.OnQuestStarted -= OnQuestStarted;
                TalesComponentRef.OnQuestSucceeded -= OnQuestSucceeded;
                TalesComponentRef.OnQuestFailed -= OnQuestFailed;
                TalesComponentRef.OnQuestNewState -= OnQuestNewState;
                TalesComponentRef.OnQuestTaskProgressChanged -= OnQuestTaskProgressChanged;
            }
        }

        public void Show()
        {
            bIsVisible = true;
            RefreshQuestList();
        }

        public void Hide()
        {
            bIsVisible = false;
        }

        public void RefreshQuestList()
        {
            _displayedQuests.Clear();
            _selectedQuestIndex = -1;

            if (TalesComponentRef == null) return;

            foreach (var quest in TalesComponentRef.QuestList)
            {
                var info = new QuestDisplayInfo
                {
                    QuestName = quest.QuestName,
                    QuestDescription = quest.QuestDescription,
                    CompletionStatus = quest.QuestCompletion,
                    CurrentStateDescription = quest.CurrentState != null ? quest.CurrentState.Description : ""
                };

                foreach (var branch in quest.Branches)
                {
                    if (branch.bHidden) continue;
                    foreach (var task in branch.QuestTasks)
                    {
                        if (task.bHidden) continue;
                        string progressText = task.GetTaskDescription();
                        string progressSuffix = task.GetTaskProgressText();
                        if (!string.IsNullOrEmpty(progressSuffix))
                        {
                            progressText += " " + progressSuffix;
                        }
                        info.TaskProgressTexts.Add(progressText);
                    }
                }

                _displayedQuests.Add(info);
            }
        }

        public void SelectQuest(int index)
        {
            if (index < 0 || index >= _displayedQuests.Count)
            {
                _selectedQuestIndex = -1;
                return;
            }
            _selectedQuestIndex = index;
        }

        public QuestDisplayInfo GetSelectedQuestDetail()
        {
            if (_selectedQuestIndex < 0 || _selectedQuestIndex >= _displayedQuests.Count)
                return null;
            return _displayedQuests[_selectedQuestIndex];
        }

        private void OnQuestStarted(TalesComponent tales, QuestClass quest)
        {
            if (bIsVisible) RefreshQuestList();
        }

        private void OnQuestSucceeded(TalesComponent tales, QuestClass quest)
        {
            if (bIsVisible) RefreshQuestList();
        }

        private void OnQuestFailed(TalesComponent tales, QuestClass quest)
        {
            if (bIsVisible) RefreshQuestList();
        }

        private void OnQuestNewState(TalesComponent tales, QuestClass quest, QuestState state)
        {
            if (bIsVisible) RefreshQuestList();
        }

        private void OnQuestTaskProgressChanged(TalesComponent tales, QuestClass quest, NarrativeTask task, QuestBranch branch, int oldProgress, int newProgress)
        {
            if (bIsVisible) RefreshQuestList();
        }
    }
}
