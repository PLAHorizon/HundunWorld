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
    public class QuestTracker : Script
    {
        public TalesComponent TalesComponentRef;
        public int MaxTrackedQuests = 5;

        private HashSet<string> _trackedQuestIds = new HashSet<string>();
        private List<TrackerEntry> _trackerEntries = new List<TrackerEntry>();

        public class TrackerEntry
        {
            public string QuestName;
            public List<string> TaskDescriptions = new List<string>();
            public bool IsCompleted;
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
        /// 绑定（或切换）叙事组件；由 <c>NarrativeRuntimeBootstrap</c> 在运行期调用，幂等。
        /// </summary>
        public void Bind(TalesComponent tales)
        {
            if (ReferenceEquals(_boundTales, tales) && tales != null) return;

            Unbind();
            TalesComponentRef = tales;
            _boundTales = tales;
            if (tales == null) return;

            tales.OnQuestTaskProgressChanged += OnQuestTaskProgressChanged;
            tales.OnQuestNewState += OnQuestNewState;
            tales.OnQuestSucceeded += OnQuestSucceeded;
            tales.OnQuestFailed += OnQuestFailed;

            // 面板可能晚于任务开始才装配：立即取一次现有追踪项
            RefreshTracker();
        }

        /// <summary>解除当前叙事组件订阅。</summary>
        public void Unbind()
        {
            var tales = _boundTales;
            if (tales != null)
            {
                tales.OnQuestTaskProgressChanged -= OnQuestTaskProgressChanged;
                tales.OnQuestNewState -= OnQuestNewState;
                tales.OnQuestSucceeded -= OnQuestSucceeded;
                tales.OnQuestFailed -= OnQuestFailed;
            }
            _boundTales = null;
        }

        private TalesComponent _boundTales;

        /// <summary>登记某任务为追踪项（首次追踪时面板立即可见）。</summary>
        public void TrackQuest(string questName)
        {
            SetTracked(questName, true);
        }

        public void SetTracked(string questClassId, bool tracked)
        {
            if (tracked)
            {
                if (_trackedQuestIds.Count >= MaxTrackedQuests) return;
                _trackedQuestIds.Add(questClassId);
            }
            else
            {
                _trackedQuestIds.Remove(questClassId);
            }
            RefreshTracker();
        }

        public void RefreshTracker()
        {
            _trackerEntries.Clear();

            if (TalesComponentRef == null) return;

            foreach (var quest in TalesComponentRef.QuestList)
            {
                if (!_trackedQuestIds.Contains(quest.QuestName)) continue;

                bool isCompleted = quest.QuestCompletion == EQuestCompletion.Succeeded ||
                                   quest.QuestCompletion == EQuestCompletion.Failed;

                var entry = new TrackerEntry
                {
                    QuestName = quest.QuestName,
                    IsCompleted = isCompleted
                };

                foreach (var branch in quest.Branches)
                {
                    if (branch.bHidden) continue;
                    foreach (var task in branch.QuestTasks)
                    {
                        if (task.bHidden) continue;
                        string desc = task.GetTaskDescription();
                        string progress = task.GetTaskProgressText();
                        if (!string.IsNullOrEmpty(progress))
                        {
                            desc += " " + progress;
                        }
                        entry.TaskDescriptions.Add(desc);
                    }
                }

                _trackerEntries.Add(entry);
            }
        }

        public List<TrackerEntry> GetTrackerEntries()
        {
            return _trackerEntries;
        }

        private void OnQuestTaskProgressChanged(TalesComponent tales, QuestClass quest, NarrativeTask task, QuestBranch branch, int oldProgress, int newProgress)
        {
            if (_trackedQuestIds.Contains(quest.QuestName))
            {
                RefreshTracker();
            }
        }

        private void OnQuestNewState(TalesComponent tales, QuestClass quest, QuestState state)
        {
            if (_trackedQuestIds.Contains(quest.QuestName))
            {
                RefreshTracker();
            }
        }

        private void OnQuestSucceeded(TalesComponent tales, QuestClass quest)
        {
            if (_trackedQuestIds.Contains(quest.QuestName))
            {
                RefreshTracker();
            }
        }

        private void OnQuestFailed(TalesComponent tales, QuestClass quest)
        {
            if (_trackedQuestIds.Contains(quest.QuestName))
            {
                RefreshTracker();
            }
        }
    }
}
