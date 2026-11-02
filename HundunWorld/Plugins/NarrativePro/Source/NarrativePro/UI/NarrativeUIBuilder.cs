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

        /// <summary>
        /// 提供当前可选项的对话（缺陷3）：选项文本/提示文本的变量替换需要 dialogue 上下文
        /// （UE5 <c>GetOptionText(UDialogue*)</c> / <c>GetHintText(UDialogue*)</c>），
        /// 由 <see cref="OnDialogueRepliesAvailable"/> 事件参数带入，对话结束时清空。
        /// </summary>
        private DialogueClass _currentReplyDialogue;

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
            _notificationPanels.Clear();

            _dialogueBgPanel = null;
            _questLogBgPanel = null;
            _notificationContainer = null;
            _dialoguePanel = null;
            _questLogPanel = null;
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
                // UE5 按钮文本走 GetOptionText(UDialogue*)（DialogueSM.cpp:443-454）：OptionText 为空回退行文本，
                // 且两者都要过字符串变量替换；直接读 OptionText 会显示空按钮。
                // 缺陷3：此前这里不带 dialogue 实参（唯一的活调用点），ReplaceStringVariables 分支永不执行，
                // 占位符原样显示。dialogue 由 OnDialogueRepliesAvailable 事件带过来并缓存在
                // _currentReplyDialogue，这里传进去。
                string optionLabel = reply.GetOptionText(_currentReplyDialogue);

                // 缺陷3：提示文本改走 GetHintText(dialogue)（DialogueSM.cpp:457-486）——
                // 手动 HintText 优先，否则按事件拼接 auto-hint；旧代码只读裸 HintText 字段，
                // 事件侧的提示（说服/威吓等）永远显示不出来。
                string hintText = reply.GetHintText(_currentReplyDialogue);
                if (!string.IsNullOrEmpty(hintText))
                {
                    optionLabel = string.IsNullOrEmpty(optionLabel)
                        ? hintText
                        : $"{optionLabel}  <color=#9a9a9a>{hintText}</color>";
                }
                var btn = CreateStyledButton(optionLabel, 0, y, 1092, 28);
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
            _currentReplyDialogue = dialogue;   // 同一对话内的后续可选项也要能取到替换上下文

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
            // 缺陷3：把事件自带的 dialogue 缓存下来，供 PopulateReplyButtons 做变量替换/事件提示。
            _currentReplyDialogue = dialogue;
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
            _currentReplyDialogue = null;   // 缺陷3：对话结束即丢弃替换上下文，避免残留引用

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

        private static T FindScript<T>() where T : Script
        {
            // 之前是恒 null 的占位实现，导致 BuildAll 找不到任何面板、整套面板视图从未被装配。
            // Flax 的等价物是 Level.FindScript<T>()（在已加载场景中取第一个该类型脚本）。
            return Level.FindScript<T>();
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
