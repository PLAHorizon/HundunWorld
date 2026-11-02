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
            Bind(TalesComponentRef);
        }

        public override void OnDisable()
        {
            Unbind();
        }

        /// <summary>
        /// 绑定（或切换）叙事组件。运行期由 <c>NarrativeRuntimeBootstrap</c> 在 TalesComponent
        /// 就绪后调用，幂等；面板因此不再受「启用时组件引用尚未赋值」的时序限制。
        /// </summary>
        public void Bind(TalesComponent tales)
        {
            if (ReferenceEquals(_boundTales, tales) && tales != null) return;

            Unbind();
            TalesComponentRef = tales;
            _boundTales = tales;
            if (tales == null) return;

            tales.OnQuestStarted += OnQuestStarted;
            tales.OnQuestSucceeded += OnQuestSucceeded;
            tales.OnQuestFailed += OnQuestFailed;
            tales.OnQuestNewState += OnQuestNewState;
            tales.OnQuestTaskProgressChanged += OnQuestTaskProgressChanged;
        }

        /// <summary>解除当前叙事组件订阅。</summary>
        public void Unbind()
        {
            var tales = _boundTales;
            if (tales != null)
            {
                tales.OnQuestStarted -= OnQuestStarted;
                tales.OnQuestSucceeded -= OnQuestSucceeded;
                tales.OnQuestFailed -= OnQuestFailed;
                tales.OnQuestNewState -= OnQuestNewState;
                tales.OnQuestTaskProgressChanged -= OnQuestTaskProgressChanged;
            }
            _boundTales = null;
        }

        private TalesComponent _boundTales;

        /// <summary>当前展示的条目（供视图读取）。</summary>
        public List<QuestDisplayInfo> GetDisplayedQuests() => _displayedQuests;

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
