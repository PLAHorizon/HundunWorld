using System;
using System.Collections.Generic;
using FlaxEngine;
using FlaxEngine.GUI;
using NarrativePro.Core;

namespace NarrativePro.Editor
{
    public class NarrativeEditorUI : Script
    {
        public NarrativeEditorWindow Editor { get; private set; } = new NarrativeEditorWindow();

        private ContainerControl _rootPanel;
        private Panel _mainPanel;
        private Label _titleLabel;
        private Label _statusLabel;
        private Panel _contentPanel;
        private Panel _toolbarPanel;
        private bool _isVisible;

        public bool IsVisible => _isVisible;

        public void ShowEditor(ContainerControl parent)
        {
            if (_isVisible) return;
            _rootPanel = parent;
            BuildEditorUI();
            _isVisible = true;
        }

        public void HideEditor()
        {
            if (!_isVisible) return;
            if (_mainPanel != null && _rootPanel != null)
            {
                _rootPanel.RemoveChild(_mainPanel);
                _mainPanel.Dispose();
                _mainPanel = null;
            }
            _isVisible = false;
        }

        public void ToggleEditor(ContainerControl parent)
        {
            if (_isVisible) HideEditor();
            else ShowEditor(parent);
        }

        private void BuildEditorUI()
        {
            if (_rootPanel == null) return;

            _mainPanel = new Panel
            {
                AnchorPreset = AnchorPresets.StretchAll,
                Offsets = new Margin(50, -50, 50, -50),
                BackgroundColor = new Color(0.1f, 0.1f, 0.15f, 0.95f),
            };

            var titleBar = new Panel
            {
                AnchorPreset = AnchorPresets.HorizontalStretchTop,
                Offsets = new Margin(0, 0, 0, 40),
                BackgroundColor = new Color(0.15f, 0.15f, 0.2f, 1f),
            };

            _titleLabel = new Label
            {
                AnchorPreset = AnchorPresets.StretchAll,
                Offsets = new Margin(10, -80, 5, -5),
                Text = "NarrativePro 编辑器",
                TextColor = Color.Gold,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };

            var closeButton = new Button
            {
                AnchorPreset = AnchorPresets.TopRight,
                Offsets = new Margin(-70, 60, 5, 30),
                Text = "关闭",
            };
            closeButton.Clicked += HideEditor;

            titleBar.AddChild(_titleLabel);
            titleBar.AddChild(closeButton);

            _toolbarPanel = new Panel
            {
                AnchorPreset = AnchorPresets.HorizontalStretchTop,
                Offsets = new Margin(0, 0, 40, 80),
                BackgroundColor = new Color(0.12f, 0.12f, 0.18f, 1f),
            };

            var newQuestBtn = new Button { Offsets = new Margin(10, 100, 5, 35), Text = "新建任务" };
            newQuestBtn.Clicked += () => { Editor.NewQuest(); RefreshContent(); };

            var newDialogueBtn = new Button { Offsets = new Margin(110, 200, 5, 35), Text = "新建对话" };
            newDialogueBtn.Clicked += () => { Editor.NewDialogue(); RefreshContent(); };

            var saveBtn = new Button { Offsets = new Margin(210, 290, 5, 35), Text = "保存" };
            saveBtn.Clicked += () => { Editor.Save(); RefreshStatus(); };

            var validateBtn = new Button { Offsets = new Margin(300, 390, 5, 35), Text = "验证" };
            validateBtn.Clicked += () =>
            {
                string error;
                bool valid = Editor.Validate(out error);
                Editor.StatusMessage = valid ? "验证通过" : $"验证失败: {error}";
                RefreshStatus();
            };

            _toolbarPanel.AddChild(newQuestBtn);
            _toolbarPanel.AddChild(newDialogueBtn);
            _toolbarPanel.AddChild(saveBtn);
            _toolbarPanel.AddChild(validateBtn);

            _contentPanel = new Panel
            {
                AnchorPreset = AnchorPresets.StretchAll,
                Offsets = new Margin(0, 0, 80, -30),
                BackgroundColor = new Color(0.08f, 0.08f, 0.12f, 0.9f),
            };

            _statusLabel = new Label
            {
                AnchorPreset = AnchorPresets.HorizontalStretchBottom,
                Offsets = new Margin(10, -10, -30, 25),
                Text = "",
                TextColor = Color.LightGray,
                HorizontalAlignment = TextAlignment.Near,
            };

            _mainPanel.AddChild(titleBar);
            _mainPanel.AddChild(_toolbarPanel);
            _mainPanel.AddChild(_contentPanel);
            _mainPanel.AddChild(_statusLabel);
            _rootPanel.AddChild(_mainPanel);

            RefreshContent();
        }

        private void RefreshContent()
        {
            if (_contentPanel == null) return;

            foreach (var child in _contentPanel.Children)
            {
                _contentPanel.RemoveChild(child);
                child.Dispose();
            }

            if (Editor.CurrentMode == NarrativeEditorWindow.EditorMode.Quest && Editor.QuestData != null)
            {
                BuildQuestEditor();
            }
            else if (Editor.CurrentMode == NarrativeEditorWindow.EditorMode.Dialogue && Editor.DialogueData != null)
            {
                BuildDialogueEditor();
            }

            RefreshStatus();
        }

        private void BuildQuestEditor()
        {
            var quest = Editor.QuestData;
            float y = 10;

            var idLabel = new Label
            {
                Offsets = new Margin(10, 300, y, y + 20),
                Text = $"任务ID: {quest.QuestId}  |  名称: {quest.QuestName}",
                TextColor = Color.White,
            };
            _contentPanel.AddChild(idLabel);
            y += 30;

            var descLabel = new Label
            {
                Offsets = new Margin(10, 600, y, y + 20),
                Text = $"描述: {quest.QuestDescription}",
                TextColor = Color.LightGray,
            };
            _contentPanel.AddChild(descLabel);
            y += 30;

            var startLabel = new Label
            {
                Offsets = new Margin(10, 300, y, y + 20),
                Text = $"起始状态: {quest.StartStateId}",
                TextColor = Color.Gold,
            };
            _contentPanel.AddChild(startLabel);
            y += 30;

            var statesHeader = new Label
            {
                Offsets = new Margin(10, 300, y, y + 20),
                Text = "--- 状态列表 ---",
                TextColor = Color.Cyan,
            };
            _contentPanel.AddChild(statesHeader);
            y += 25;

            foreach (var state in quest.States)
            {
                string typeStr = state.StateType.ToString();
                var stateLabel = new Label
                {
                    Offsets = new Margin(20, 400, y, y + 18),
                    Text = $"[{state.Id}] {state.Description} ({typeStr})",
                    TextColor = state.StateType == EStateNodeType.Success ? Color.Green :
                                state.StateType == EStateNodeType.Failure ? Color.Red : Color.White,
                };
                _contentPanel.AddChild(stateLabel);
                y += 22;
            }

            y += 10;
            var branchesHeader = new Label
            {
                Offsets = new Margin(10, 300, y, y + 20),
                Text = "--- 分支列表 ---",
                TextColor = Color.Cyan,
            };
            _contentPanel.AddChild(branchesHeader);
            y += 25;

            foreach (var branch in quest.Branches)
            {
                var branchLabel = new Label
                {
                    Offsets = new Margin(20, 500, y, y + 18),
                    Text = $"[{branch.Id}] {branch.FromStateId} -> {branch.ToStateId}: {branch.Description}",
                    TextColor = Color.Yellow,
                };
                _contentPanel.AddChild(branchLabel);
                y += 22;

                foreach (var task in branch.Tasks)
                {
                    var taskLabel = new Label
                    {
                        Offsets = new Margin(40, 500, y, y + 16),
                        Text = $"  Task: {task.Type}({task.TargetId}) x{task.RequiredQuantity} - {task.Description}",
                        TextColor = Color.LightGray,
                    };
                    _contentPanel.AddChild(taskLabel);
                    y += 20;
                }
            }
        }

        private void BuildDialogueEditor()
        {
            var dialogue = Editor.DialogueData;
            float y = 10;

            var idLabel = new Label
            {
                Offsets = new Margin(10, 300, y, y + 20),
                Text = $"对话ID: {dialogue.DialogueId}",
                TextColor = Color.White,
            };
            _contentPanel.AddChild(idLabel);
            y += 30;

            if (dialogue.Speakers != null)
            {
                var speakersHeader = new Label
                {
                    Offsets = new Margin(10, 300, y, y + 20),
                    Text = "--- 说话者 ---",
                    TextColor = Color.Cyan,
                };
                _contentPanel.AddChild(speakersHeader);
                y += 25;

                foreach (var speaker in dialogue.Speakers)
                {
                    var speakerLabel = new Label
                    {
                        Offsets = new Margin(20, 400, y, y + 18),
                        Text = $"[{speaker.SpeakerId}] {speaker.DisplayName} {(speaker.IsPlayer ? "(玩家)" : "")}",
                        TextColor = speaker.IsPlayer ? Color.Green : Color.White,
                    };
                    _contentPanel.AddChild(speakerLabel);
                    y += 22;
                }
            }

            y += 10;
            var npcHeader = new Label
            {
                Offsets = new Margin(10, 300, y, y + 20),
                Text = "--- NPC 对话节点 ---",
                TextColor = Color.Cyan,
            };
            _contentPanel.AddChild(npcHeader);
            y += 25;

            if (dialogue.NpcReplies != null)
            {
                foreach (var reply in dialogue.NpcReplies)
                {
                    string rootStr = reply.IsRoot ? " [ROOT]" : "";
                    var replyLabel = new Label
                    {
                        Offsets = new Margin(20, 600, y, y + 18),
                        Text = $"[{reply.Id}]{rootStr} ({reply.SpeakerId}): {reply.Line?.Text ?? ""}",
                        TextColor = reply.IsRoot ? Color.Gold : Color.White,
                    };
                    _contentPanel.AddChild(replyLabel);
                    y += 22;

                    if (reply.NpcReplies != null)
                    {
                        foreach (var link in reply.NpcReplies)
                        {
                            var linkLabel = new Label
                            {
                                Offsets = new Margin(40, 400, y, y + 16),
                                Text = $"-> NPC: {link}",
                                TextColor = Color.Gray,
                            };
                            _contentPanel.AddChild(linkLabel);
                            y += 18;
                        }
                    }
                    if (reply.PlayerReplies != null)
                    {
                        foreach (var link in reply.PlayerReplies)
                        {
                            var linkLabel = new Label
                            {
                                Offsets = new Margin(40, 400, y, y + 16),
                                Text = $"-> Player: {link}",
                                TextColor = Color.Gray,
                            };
                            _contentPanel.AddChild(linkLabel);
                            y += 18;
                        }
                    }
                }
            }

            y += 10;
            var playerHeader = new Label
            {
                Offsets = new Margin(10, 300, y, y + 20),
                Text = "--- 玩家回复节点 ---",
                TextColor = Color.Cyan,
            };
            _contentPanel.AddChild(playerHeader);
            y += 25;

            if (dialogue.PlayerReplies != null)
            {
                foreach (var reply in dialogue.PlayerReplies)
                {
                    var replyLabel = new Label
                    {
                        Offsets = new Margin(20, 600, y, y + 18),
                        Text = $"[{reply.Id}] \"{reply.OptionText}\": {reply.Line?.Text ?? ""}",
                        TextColor = Color.LightGreen,
                    };
                    _contentPanel.AddChild(replyLabel);
                    y += 22;

                    if (reply.NpcReplies != null)
                    {
                        foreach (var link in reply.NpcReplies)
                        {
                            var linkLabel = new Label
                            {
                                Offsets = new Margin(40, 400, y, y + 16),
                                Text = $"-> NPC: {link}",
                                TextColor = Color.Gray,
                            };
                            _contentPanel.AddChild(linkLabel);
                            y += 18;
                        }
                    }
                }
            }
        }

        private void RefreshStatus()
        {
            if (_statusLabel != null)
            {
                _statusLabel.Text = Editor.StatusMessage;
            }
        }

        public override void OnUpdate()
        {
            if (_isVisible && Input.GetKey(KeyboardKeys.Escape))
            {
                HideEditor();
            }
        }
    }
}
