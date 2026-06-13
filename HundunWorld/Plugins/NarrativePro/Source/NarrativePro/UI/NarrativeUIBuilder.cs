using System;
using System.Collections.Generic;
using FlaxEngine;
using FlaxEngine.GUI;
using NarrativePro.Core;
using NarrativePro.Tales;
using NarrativePro.Tales.Data;
using NarrativePro.Tales.Nodes;
using NarrativePro.Tales.Tasks;
using DialogueClass = NarrativePro.Tales.Dialogue.Dialogue;
using QuestClass = NarrativePro.Tales.Quest.Quest;

namespace NarrativePro.UI
{
    public class NarrativeUIBuilder
    {
        private static readonly Color BgColor = new Color(0, 0, 0, 0.75f);
        private static readonly Color SpeakerNameColor = Color.Gold;
        private static readonly Color DialogueTextColor = Color.White;
        private static readonly Color QuestActiveColor = Color.White;
        private static readonly Color QuestCompletedColor = Color.Green;
        private static readonly Color QuestFailedColor = Color.Red;
        private static readonly Color ButtonNormalColor = new Color(0.2f, 0.2f, 0.2f, 0.9f);
        private static readonly Color ButtonHoverColor = new Color(0.3f, 0.3f, 0.5f, 0.9f);

        private readonly List<Control> _createdControls = new List<Control>();
        private Panel _dialogueBgPanel;
        private Label _speakerNameLabel;
        private Label _dialogueTextLabel;
        private ContainerControl _replyButtonsContainer;
        private Button _skipButton;
        private readonly List<Button> _replyButtons = new List<Button>();
        private DialoguePanel _dialoguePanel;

        private Panel _questLogBgPanel;
        private ContainerControl _questListContainer;
        private ContainerControl _questDetailContainer;
        private Label _questDetailTitleLabel;
        private Label _questDetailDescLabel;
        private Label _questDetailStateLabel;
        private ContainerControl _questDetailTasksContainer;
        private readonly List<Button> _questListButtons = new List<Button>();
        private QuestLogPanel _questLogPanel;

        private Panel _trackerBgPanel;
        private Label _trackerTitleLabel;
        private ContainerControl _trackerEntriesContainer;
        private readonly List<ContainerControl> _trackerEntryControls = new List<ContainerControl>();
        private QuestTracker _questTracker;

        private ContainerControl _notificationContainer;
        private readonly List<Panel> _notificationPanels = new List<Panel>();
        private NarrativeNotification _narrativeNotification;

        private TalesComponent _talesComponent;

        private string _currentSpeakerName = "";
        private string _currentDialogueText = "";
        private string _displayedText = "";
        private int _typewriterIndex;
        private bool _isTyping;
        private float _typewriterSpeed = 30f;
        private readonly List<DialogueNode_Player> _availableReplies = new List<DialogueNode_Player>();

        public void BuildDialoguePanelUI(DialoguePanel logicPanel, ContainerControl parent)
        {
            _dialoguePanel = logicPanel;
            _talesComponent = logicPanel.TalesComponentRef;

            _dialogueBgPanel = new Panel
            {
                AnchorPreset = AnchorPresets.BottomCenter,
                Offsets = new Margin(-576, 1152, -250, 250),
                BackgroundColor = BgColor,
                Visible = false,
            };

            _speakerNameLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Offsets = new Margin(20, 800, 10, 30),
                TextColor = SpeakerNameColor,
                HorizontalAlignment = TextAlignment.Near,
                Text = "",
            };

            _dialogueTextLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Offsets = new Margin(20, 1112, 45, 95),
                TextColor = DialogueTextColor,
                HorizontalAlignment = TextAlignment.Near,
                Wrapping = TextWrapping.WrapWords,
                Text = "",
            };

            _replyButtonsContainer = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Offsets = new Margin(20, 1112, 150, 90),
            };

            _skipButton = CreateStyledButton("跳过", 0, 0, 70, 30);
            _skipButton.AnchorPreset = AnchorPresets.TopRight;
            _skipButton.Offsets = new Margin(-90, 70, 8, 30);
            _skipButton.Clicked += OnSkipClicked;

            _dialogueBgPanel.AddChild(_speakerNameLabel);
            _dialogueBgPanel.AddChild(_dialogueTextLabel);
            _dialogueBgPanel.AddChild(_replyButtonsContainer);
            _dialogueBgPanel.AddChild(_skipButton);
            parent.AddChild(_dialogueBgPanel);

            _createdControls.Add(_dialogueBgPanel);

            if (_talesComponent != null)
            {
                _talesComponent.OnNPCDialogueLineStarted += OnNPCDialogueLineStarted;
                _talesComponent.OnNPCDialogueLineFinished += OnNPCDialogueLineFinished;
                _talesComponent.OnDialogueRepliesAvailable += OnDialogueRepliesAvailable;
                _talesComponent.OnDialogueFinished += OnDialogueFinished;
            }
        }

        public void BuildQuestLogPanelUI(QuestLogPanel logicPanel, ContainerControl parent)
        {
            _questLogPanel = logicPanel;
            if (_talesComponent == null)
                _talesComponent = logicPanel.TalesComponentRef;

            _questLogBgPanel = new Panel
            {
                AnchorPreset = AnchorPresets.MiddleCenter,
                Offsets = new Margin(-672, 1344, -378, 756),
                BackgroundColor = BgColor,
                Visible = false,
            };

            var titleLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Offsets = new Margin(20, 300, 10, 40),
                Text = "任务日志",
                TextColor = SpeakerNameColor,
                HorizontalAlignment = TextAlignment.Near,
            };

            var closeButton = CreateStyledButton("关闭", 0, 0, 70, 30);
            closeButton.AnchorPreset = AnchorPresets.TopRight;
            closeButton.Offsets = new Margin(-90, 70, 10, 30);
            closeButton.Clicked += OnQuestLogCloseClicked;

            _questListContainer = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Offsets = new Margin(10, 440, 55, 690),
            };

            var divider = new Panel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Offsets = new Margin(450, 2, 55, 690),
                BackgroundColor = new Color(0.5f, 0.5f, 0.5f, 0.5f),
            };

            _questDetailContainer = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Offsets = new Margin(465, 869, 55, 690),
            };

            _questDetailTitleLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Offsets = new Margin(10, 849, 0, 30),
                TextColor = SpeakerNameColor,
                HorizontalAlignment = TextAlignment.Near,
                Text = "",
            };

            _questDetailDescLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Offsets = new Margin(10, 849, 35, 60),
                TextColor = Color.LightGray,
                HorizontalAlignment = TextAlignment.Near,
                Text = "",
            };

            _questDetailStateLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Offsets = new Margin(10, 849, 100, 25),
                TextColor = QuestActiveColor,
                HorizontalAlignment = TextAlignment.Near,
                Text = "",
            };

            _questDetailTasksContainer = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Offsets = new Margin(10, 849, 130, 550),
            };

            _questDetailContainer.AddChild(_questDetailTitleLabel);
            _questDetailContainer.AddChild(_questDetailDescLabel);
            _questDetailContainer.AddChild(_questDetailStateLabel);
            _questDetailContainer.AddChild(_questDetailTasksContainer);

            _questLogBgPanel.AddChild(titleLabel);
            _questLogBgPanel.AddChild(closeButton);
            _questLogBgPanel.AddChild(_questListContainer);
            _questLogBgPanel.AddChild(divider);
            _questLogBgPanel.AddChild(_questDetailContainer);
            parent.AddChild(_questLogBgPanel);

            _createdControls.Add(_questLogBgPanel);

            if (_talesComponent != null)
            {
                _talesComponent.OnQuestStarted += OnQuestDataChanged;
                _talesComponent.OnQuestSucceeded += OnQuestDataChanged;
                _talesComponent.OnQuestFailed += OnQuestDataChanged;
                _talesComponent.OnQuestNewState += OnQuestDataChangedWithState;
                _talesComponent.OnQuestTaskProgressChanged += OnQuestTaskProgressChanged;
            }
        }

        public void BuildQuestTrackerUI(QuestTracker logicPanel, ContainerControl parent)
        {
            _questTracker = logicPanel;
            if (_talesComponent == null)
                _talesComponent = logicPanel.TalesComponentRef;

            _trackerBgPanel = new Panel
            {
                AnchorPreset = AnchorPresets.TopRight,
                Offsets = new Margin(-260, 250, 10, 300),
                BackgroundColor = BgColor,
            };

            _trackerTitleLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Offsets = new Margin(10, 230, 5, 25),
                Text = "当前任务",
                TextColor = SpeakerNameColor,
                HorizontalAlignment = TextAlignment.Near,
            };

            _trackerEntriesContainer = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Offsets = new Margin(5, 240, 35, 260),
            };

            _trackerBgPanel.AddChild(_trackerTitleLabel);
            _trackerBgPanel.AddChild(_trackerEntriesContainer);
            parent.AddChild(_trackerBgPanel);

            _createdControls.Add(_trackerBgPanel);

            if (_talesComponent != null)
            {
                _talesComponent.OnQuestTaskProgressChanged += OnTrackerQuestChanged;
                _talesComponent.OnQuestNewState += OnTrackerQuestChangedWithState;
                _talesComponent.OnQuestSucceeded += OnTrackerQuestChangedSimple;
                _talesComponent.OnQuestFailed += OnTrackerQuestChangedSimple;
            }

            RefreshTrackerUI();
        }

        public void BuildNarrativeNotificationUI(NarrativeNotification logicPanel, ContainerControl parent)
        {
            _narrativeNotification = logicPanel;

            _notificationContainer = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopCenter,
                Offsets = new Margin(-200, 400, 10, 250),
            };
            parent.AddChild(_notificationContainer);

            _createdControls.Add(_notificationContainer);
        }

        public void BuildAll(TalesComponent talesComponent, ContainerControl root)
        {
            _talesComponent = talesComponent;

            var dialoguePanel = FindScript<DialoguePanel>();
            if (dialoguePanel != null)
                BuildDialoguePanelUI(dialoguePanel, root);

            var questLogPanel = FindScript<QuestLogPanel>();
            if (questLogPanel != null)
                BuildQuestLogPanelUI(questLogPanel, root);

            var questTracker = FindScript<QuestTracker>();
            if (questTracker != null)
                BuildQuestTrackerUI(questTracker, root);

            var notification = FindScript<NarrativeNotification>();
            if (notification != null)
                BuildNarrativeNotificationUI(notification, root);
        }

        public void DisposeAll()
        {
            if (_talesComponent != null)
            {
                _talesComponent.OnNPCDialogueLineStarted -= OnNPCDialogueLineStarted;
                _talesComponent.OnNPCDialogueLineFinished -= OnNPCDialogueLineFinished;
                _talesComponent.OnDialogueRepliesAvailable -= OnDialogueRepliesAvailable;
                _talesComponent.OnDialogueFinished -= OnDialogueFinished;
                _talesComponent.OnQuestStarted -= OnQuestDataChanged;
                _talesComponent.OnQuestSucceeded -= OnQuestDataChanged;
                _talesComponent.OnQuestFailed -= OnQuestDataChanged;
                _talesComponent.OnQuestNewState -= OnQuestDataChangedWithState;
                _talesComponent.OnQuestTaskProgressChanged -= OnQuestTaskProgressChanged;
                _talesComponent.OnQuestTaskProgressChanged -= OnTrackerQuestChanged;
                _talesComponent.OnQuestNewState -= OnTrackerQuestChangedWithState;
                _talesComponent.OnQuestSucceeded -= OnTrackerQuestChangedSimple;
                _talesComponent.OnQuestFailed -= OnTrackerQuestChangedSimple;
            }

            for (int i = _createdControls.Count - 1; i >= 0; i--)
            {
                var control = _createdControls[i];
                if (control.Parent != null)
                    control.Parent.RemoveChild(control);
                control.Dispose();
            }
            _createdControls.Clear();

            _replyButtons.Clear();
            _questListButtons.Clear();
            _trackerEntryControls.Clear();
            _notificationPanels.Clear();

            _dialogueBgPanel = null;
            _questLogBgPanel = null;
            _trackerBgPanel = null;
            _notificationContainer = null;
            _dialoguePanel = null;
            _questLogPanel = null;
            _questTracker = null;
            _narrativeNotification = null;
            _talesComponent = null;
        }

        public void Update()
        {
            UpdateDialogueTypewriter();
            UpdateNotificationDisplay();
        }

        public void ShowQuestLog()
        {
            if (_questLogPanel != null)
                _questLogPanel.Show();
            if (_questLogBgPanel != null)
            {
                _questLogBgPanel.Visible = true;
                RefreshQuestListUI();
            }
        }

        public void HideQuestLog()
        {
            if (_questLogPanel != null)
                _questLogPanel.Hide();
            if (_questLogBgPanel != null)
                _questLogBgPanel.Visible = false;
        }

        private void UpdateDialogueTypewriter()
        {
            if (!_isTyping || string.IsNullOrEmpty(_currentDialogueText)) return;

            _typewriterIndex += (int)(_typewriterSpeed * Time.DeltaTime);
            if (_typewriterIndex >= _currentDialogueText.Length)
            {
                _typewriterIndex = _currentDialogueText.Length;
                _displayedText = _currentDialogueText;
                _isTyping = false;
            }
            else
            {
                _displayedText = _currentDialogueText.Substring(0, _typewriterIndex);
            }

            if (_dialogueTextLabel != null)
                _dialogueTextLabel.Text = _displayedText;
        }

        private void UpdateNotificationDisplay()
        {
            if (_narrativeNotification == null || _notificationContainer == null) return;

            var activeNotifications = _narrativeNotification.GetActiveNotifications();
            if (activeNotifications == null) return;

            for (int i = _notificationPanels.Count - 1; i >= 0; i--)
            {
                if (i >= activeNotifications.Count)
                {
                    var panel = _notificationPanels[i];
                    _notificationContainer.RemoveChild(panel);
                    panel.Dispose();
                    _notificationPanels.RemoveAt(i);
                }
            }

            for (int i = 0; i < activeNotifications.Count; i++)
            {
                var entry = activeNotifications[i];
                float yOffset = i * 54f;

                if (i < _notificationPanels.Count)
                {
                    var existingPanel = _notificationPanels[i];
                    existingPanel.Offsets = new Margin(0, 400, yOffset, 50);

                    float opacity = 1f;
                    if (entry.bFading && entry.TimeRemaining > 0f && _narrativeNotification.FadeOutDuration > 0f)
                        opacity = entry.TimeRemaining / _narrativeNotification.FadeOutDuration;

                    var bg = existingPanel.BackgroundColor;
                    existingPanel.BackgroundColor = new Color(bg.R, bg.G, bg.B, 0.75f * opacity);

                    var titleLabel = existingPanel.GetChild(0) as Label;
                    var msgLabel = existingPanel.GetChild(1) as Label;
                    if (titleLabel != null)
                    {
                        titleLabel.Text = entry.Title;
                        var tc = SpeakerNameColor;
                        titleLabel.TextColor = new Color(tc.R, tc.G, tc.B, opacity);
                    }
                    if (msgLabel != null)
                    {
                        msgLabel.Text = entry.Message;
                        msgLabel.TextColor = new Color(1, 1, 1, opacity);
                    }
                }
                else
                {
                    var panel = new Panel
                    {
                        AnchorPreset = AnchorPresets.TopLeft,
                        Offsets = new Margin(0, 400, yOffset, 50),
                        BackgroundColor = BgColor,
                    };

                    var titleLabel = new Label
                    {
                        AnchorPreset = AnchorPresets.TopLeft,
                        Offsets = new Margin(10, 380, 5, 20),
                        Text = entry.Title,
                        TextColor = SpeakerNameColor,
                        HorizontalAlignment = TextAlignment.Near,
                    };

                    var msgLabel = new Label
                    {
                        AnchorPreset = AnchorPresets.TopLeft,
                        Offsets = new Margin(10, 380, 25, 20),
                        Text = entry.Message,
                        TextColor = Color.White,
                        HorizontalAlignment = TextAlignment.Near,
                    };

                    panel.AddChild(titleLabel);
                    panel.AddChild(msgLabel);
                    _notificationContainer.AddChild(panel);
                    _notificationPanels.Add(panel);
                }
            }
        }

        private void RefreshQuestListUI()
        {
            if (_questListContainer == null || _talesComponent == null) return;

            foreach (var btn in _questListButtons)
            {
                _questListContainer.RemoveChild(btn);
                btn.Dispose();
            }
            _questListButtons.Clear();

            float y = 0;
            int index = 0;
            foreach (var quest in _talesComponent.QuestList)
            {
                Color textColor = QuestActiveColor;
                if (quest.QuestCompletion == EQuestCompletion.Succeeded)
                    textColor = QuestCompletedColor;
                else if (quest.QuestCompletion == EQuestCompletion.Failed)
                    textColor = QuestFailedColor;

                var btn = CreateStyledButton(quest.QuestName, 0, y, 420, 30);
                btn.TextColor = textColor;

                int capturedIndex = index;
                btn.Clicked += () => OnQuestSelected(capturedIndex);

                _questListContainer.AddChild(btn);
                _questListButtons.Add(btn);
                y += 35;
                index++;
            }
        }

        private void RefreshQuestDetailUI()
        {
            if (_questLogPanel == null) return;

            var detail = _questLogPanel.GetSelectedQuestDetail();
            if (detail == null)
            {
                if (_questDetailTitleLabel != null) _questDetailTitleLabel.Text = "";
                if (_questDetailDescLabel != null) _questDetailDescLabel.Text = "";
                if (_questDetailStateLabel != null) _questDetailStateLabel.Text = "";
                ClearQuestDetailTasks();
                return;
            }

            if (_questDetailTitleLabel != null)
                _questDetailTitleLabel.Text = detail.QuestName;

            if (_questDetailDescLabel != null)
                _questDetailDescLabel.Text = detail.QuestDescription;

            if (_questDetailStateLabel != null)
            {
                _questDetailStateLabel.Text = detail.CurrentStateDescription;
                if (detail.CompletionStatus == EQuestCompletion.Succeeded)
                    _questDetailStateLabel.TextColor = QuestCompletedColor;
                else if (detail.CompletionStatus == EQuestCompletion.Failed)
                    _questDetailStateLabel.TextColor = QuestFailedColor;
                else
                    _questDetailStateLabel.TextColor = QuestActiveColor;
            }

            ClearQuestDetailTasks();

            if (_questDetailTasksContainer != null)
            {
                float y = 0;
                foreach (var taskText in detail.TaskProgressTexts)
                {
                    var taskLabel = new Label
                    {
                        AnchorPreset = AnchorPresets.TopLeft,
                        Offsets = new Margin(0, 839, y, 20),
                        Text = taskText,
                        TextColor = Color.LightGray,
                        HorizontalAlignment = TextAlignment.Near,
                    };
                    _questDetailTasksContainer.AddChild(taskLabel);
                    y += 25;
                }
            }
        }

        private void ClearQuestDetailTasks()
        {
            if (_questDetailTasksContainer == null) return;

            var children = new List<Control>(_questDetailTasksContainer.Children);
            foreach (var child in children)
            {
                _questDetailTasksContainer.RemoveChild(child);
                child.Dispose();
            }
        }

        private void RefreshTrackerUI()
        {
            if (_questTracker == null || _trackerEntriesContainer == null) return;

            foreach (var entryControl in _trackerEntryControls)
            {
                _trackerEntriesContainer.RemoveChild(entryControl);
                entryControl.Dispose();
            }
            _trackerEntryControls.Clear();

            var entries = _questTracker.GetTrackerEntries();
            if (entries == null) return;

            float y = 0;
            foreach (var entry in entries)
            {
                var entryPanel = new ContainerControl
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Offsets = new Margin(0, 230, y, 50),
                };

                var nameLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Offsets = new Margin(5, 220, 0, 20),
                    Text = entry.QuestName,
                    TextColor = entry.IsCompleted ? QuestCompletedColor : QuestActiveColor,
                    HorizontalAlignment = TextAlignment.Near,
                };
                entryPanel.AddChild(nameLabel);

                float taskY = 20;
                foreach (var taskDesc in entry.TaskDescriptions)
                {
                    var taskLabel = new Label
                    {
                        AnchorPreset = AnchorPresets.TopLeft,
                        Offsets = new Margin(15, 210, taskY, 16),
                        Text = taskDesc,
                        TextColor = Color.LightGray,
                        HorizontalAlignment = TextAlignment.Near,
                    };
                    entryPanel.AddChild(taskLabel);
                    taskY += 20;
                }

                float entryHeight = Math.Max(50, taskY + 5);
                entryPanel.Offsets = new Margin(0, 230, y, entryHeight);

                _trackerEntriesContainer.AddChild(entryPanel);
                _trackerEntryControls.Add(entryPanel);
                y += entryHeight + 5;
            }
        }

        private void ClearReplyButtons()
        {
            if (_replyButtonsContainer == null) return;

            foreach (var btn in _replyButtons)
            {
                _replyButtonsContainer.RemoveChild(btn);
                btn.Dispose();
            }
            _replyButtons.Clear();
        }

        private void PopulateReplyButtons()
        {
            ClearReplyButtons();

            if (_replyButtonsContainer == null) return;

            float y = 0;
            for (int i = 0; i < _availableReplies.Count; i++)
            {
                var reply = _availableReplies[i];
                var btn = CreateStyledButton(reply.OptionText, 0, y, 1092, 28);
                btn.TextColor = DialogueTextColor;
                btn.HorizontalAlignment = TextAlignment.Near;

                int capturedIndex = i;
                btn.Clicked += () => OnReplyClicked(capturedIndex);

                _replyButtonsContainer.AddChild(btn);
                _replyButtons.Add(btn);
                y += 32;
            }
        }

        private void OnNPCDialogueLineStarted(TalesComponent tales, DialogueClass dialogue, DialogueNode_NPC node, DialogueLine line, SpeakerInfo speaker)
        {
            _currentSpeakerName = speaker != null ? speaker.DisplayName : "";
            _currentDialogueText = line != null ? line.Text : "";
            _displayedText = "";
            _typewriterIndex = 0;
            _isTyping = true;
            _availableReplies.Clear();

            if (_dialogueBgPanel != null)
                _dialogueBgPanel.Visible = true;

            if (_speakerNameLabel != null)
                _speakerNameLabel.Text = _currentSpeakerName;

            if (_dialogueTextLabel != null)
                _dialogueTextLabel.Text = "";

            ClearReplyButtons();
        }

        private void OnNPCDialogueLineFinished(TalesComponent tales, DialogueClass dialogue, DialogueNode_NPC node, DialogueLine line, SpeakerInfo speaker)
        {
            if (_isTyping)
            {
                _displayedText = _currentDialogueText;
                _isTyping = false;
                if (_dialogueTextLabel != null)
                    _dialogueTextLabel.Text = _displayedText;
            }
        }

        private void OnDialogueRepliesAvailable(TalesComponent tales, DialogueClass dialogue, List<DialogueNode_Player> replies)
        {
            _availableReplies.Clear();
            if (replies != null)
                _availableReplies.AddRange(replies);
            PopulateReplyButtons();
        }

        private void OnDialogueFinished(TalesComponent tales, DialogueClass dialogue, EExitDialogueReason reason)
        {
            _currentSpeakerName = "";
            _currentDialogueText = "";
            _displayedText = "";
            _isTyping = false;
            _availableReplies.Clear();

            if (_dialogueBgPanel != null)
                _dialogueBgPanel.Visible = false;

            if (_speakerNameLabel != null)
                _speakerNameLabel.Text = "";
            if (_dialogueTextLabel != null)
                _dialogueTextLabel.Text = "";

            ClearReplyButtons();
        }

        private void OnSkipClicked()
        {
            if (_dialoguePanel != null)
                _dialoguePanel.SkipTypewriter();

            if (_isTyping)
            {
                _displayedText = _currentDialogueText;
                _isTyping = false;
                if (_dialogueTextLabel != null)
                    _dialogueTextLabel.Text = _displayedText;
            }
        }

        private void OnReplyClicked(int index)
        {
            if (_dialoguePanel != null)
                _dialoguePanel.OnReplySelected(index);
        }

        private void OnQuestSelected(int index)
        {
            if (_questLogPanel != null)
                _questLogPanel.SelectQuest(index);
            RefreshQuestDetailUI();
        }

        private void OnQuestLogCloseClicked()
        {
            HideQuestLog();
        }

        private void OnQuestDataChanged(TalesComponent tales, QuestClass quest)
        {
            if (_questLogBgPanel != null && _questLogBgPanel.Visible)
                RefreshQuestListUI();
        }

        private void OnQuestDataChangedWithState(TalesComponent tales, QuestClass quest, QuestState state)
        {
            if (_questLogBgPanel != null && _questLogBgPanel.Visible)
                RefreshQuestListUI();
        }

        private void OnQuestTaskProgressChanged(TalesComponent tales, QuestClass quest, NarrativeTask task, QuestBranch branch, int oldProgress, int newProgress)
        {
            if (_questLogBgPanel != null && _questLogBgPanel.Visible)
                RefreshQuestListUI();
        }

        private void OnTrackerQuestChanged(TalesComponent tales, QuestClass quest, NarrativeTask task, QuestBranch branch, int oldProgress, int newProgress)
        {
            RefreshTrackerUI();
        }

        private void OnTrackerQuestChangedWithState(TalesComponent tales, QuestClass quest, QuestState state)
        {
            RefreshTrackerUI();
        }

        private void OnTrackerQuestChangedSimple(TalesComponent tales, QuestClass quest)
        {
            RefreshTrackerUI();
        }

        private static T FindScript<T>() where T : Script
        {
            return null;
        }

        private static Button CreateStyledButton(string text, float x, float y, float width, float height)
        {
            var btn = new Button
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Offsets = new Margin(x, width, y, height),
                Text = text,
                BackgroundColor = ButtonNormalColor,
                BackgroundColorHighlighted = ButtonHoverColor,
            };
            return btn;
        }
    }
}
