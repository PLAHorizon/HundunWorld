#if FLAX_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using FlaxEngine;
using FlaxEngine.GUI;
using NarrativePro.Core;
using NarrativePro.Data;
using NarrativePro.Tales.Dialogue;
using NarrativePro.Tales.Quest;

namespace NarrativePro.EditorTools
{
    /// <summary>
    /// 右侧检查器：按选中对象（资产头 / 状态 / 分支 / 对话节点 / 说话者）生成可编辑表单，
    /// 同时承担结构编辑——加删事件、条件、分支任务与连线。所有写入都过 <see cref="NarrativeAssetStore"/>。
    /// </summary>
    internal sealed class NarrativeInspectorPane : Panel
    {
        private readonly INarrativeEditorHost _owner;

        private NarrativeSelection _selection = NarrativeSelection.None;
        private bool _isQuest;
        private QuestData _quest;
        private DialogueData _dialogue;
        private float _y;
        private float _lastWidth = -1f;

        // 主表单字段（按选中类型复用）。值域可枚举的字段一律用下拉：手打 id / 手打 0-1 是编辑期最常见的错源。
        private TextBox _questName;
        private TextBox _questDescription;
        private Dropdown _questDialogue;
        private TextBox _questTemplate;
        private TextBox _stateDescription;
        private Dropdown _stateType;
        private TextBox _branchDescription;
        private Dropdown _branchFrom;
        private Dropdown _branchTo;
        private Dropdown _branchHidden;
        private Dropdown _npcSpeaker;
        private Dropdown _npcShot;
        private Dropdown _npcCutscene;
        private Dropdown _npcWaitCutscene;
        private Dropdown _playerShot;
        private Dropdown _playerCutscene;
        private Dropdown _playerWaitCutscene;
        private Dropdown _speakerShot;
        private Dropdown _speakerReplyShot;
        private Dropdown _dlgBars;
        private Dropdown _dlgUnskippable;
        private Dropdown _dlgFreeMovement;
        private Dropdown _dlgCanExit;
        private TextBox _dlgPriority;
        private TextBox _dlgEndDistance;
        private Dropdown _dlgDefaultShot;
        private List<(string Display, string Value)> _shotChoices = new List<(string Display, string Value)>();
        private TextBox _npcText;
        private Dropdown _npcSkippable;
        private Dropdown _npcRoot;
        private TextBox _playerOption;
        private TextBox _playerText;
        private Dropdown _playerAuto;
        private Dropdown _playerAutoOnly;
        private TextBox _speakerName;
        private Dropdown _speakerPlayer;

        // 结构编辑字段
        private Dropdown _taskType;
        private TextBox _taskTarget;
        private TextBox _taskQuantity;
        private TextBox _taskDescription;
        private TextBox _taskParams;
        private Dropdown _linkTarget;

        public NarrativeInspectorPane(INarrativeEditorHost owner)
            : base(ScrollBars.Vertical, false)
        {
            _owner = owner;
            ClipChildren = true;
        }

        public void SetContext(bool isQuest, QuestData quest, DialogueData dialogue, NarrativeSelection selection)
        {
            _isQuest = isQuest;
            _quest = quest;
            _dialogue = dialogue;
            _selection = selection;
            _deletePending = false;
            // 由点击回调里触发的重建要等这一帧结束，避免在控件自己的 Clicked 里把它 Dispose 掉。
            _pendingRebuild = true;
        }

        private bool _pendingRebuild;
        private bool _deletePending;

        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);
            if (!_pendingRebuild) return;
            _pendingRebuild = false;
            Rebuild();
        }

        /// <summary>父容器尺寸变化后重排一次（表单宽高都跟着窗口走）。</summary>
        public void Relayout()
        {
            if (Math.Abs(Width - _lastWidth) > 1f)
            {
                _lastWidth = Width;
                _pendingRebuild = true;
            }
        }

        private float ContentWidth => Math.Max(180f, Width - 26f);

        private void Clear()
        {
            ViewOffset = Float2.Zero;
            for (int i = ChildrenCount - 1; i >= 0; i--)
            {
                var child = Children[i];
                RemoveChild(child);
                child.Dispose();
            }
            _y = 8f;
        }

        private void Rebuild()
        {
            Clear();

            if (_quest == null && _dialogue == null)
            {
                AddNote("从左侧打开一个任务或对话后，这里显示它的属性与结构编辑。", NarrativeEditorTheme.TextDim, 40f);
                return;
            }

            BuildHeader();
            switch (_selection.Kind)
            {
                case NarrativeSelectionKind.QuestState: BuildQuestState(); break;
                case NarrativeSelectionKind.QuestBranch: BuildQuestBranch(); break;
                case NarrativeSelectionKind.DialogueNpc: BuildDialogueNpc(); break;
                case NarrativeSelectionKind.DialoguePlayer: BuildDialoguePlayer(); break;
                case NarrativeSelectionKind.Speaker: BuildSpeaker(); break;
                default: BuildOverview(); break;
            }
        }

        // -------------------------------------------------------------------
        // 资产头
        // -------------------------------------------------------------------

        private void BuildHeader()
        {
            Section("资产");
            if (_isQuest && _quest != null)
            {
                AddNote("任务 · " + _owner.CurrentAssetName + "   id=" + (_quest.QuestId ?? ""), NarrativeEditorTheme.TextDim, 20f);
                _questName = Field("questName", _quest.QuestName);
                _questDescription = Field("描述", _quest.QuestDescription);
                _questDialogue = DropdownField("对话类", NarrativeAssetStore.DialogueAssetNames(), _quest.QuestDialogueClass);
                _questTemplate = Field("模板 id", _quest.QuestTemplateId?.ToString() ?? "");

                string templateHint = DescribeTemplateLink();
                if (!string.IsNullOrEmpty(templateHint)) AddNote(templateHint, NarrativeEditorTheme.TextDim, 20f);

                string linkedDialogue = string.IsNullOrWhiteSpace(_quest.QuestDialogueClass)
                    ? null
                    : NarrativeAssetStore.DialoguePathFor(_quest.QuestDialogueClass);

                var headerButtons = new List<(string text, Action action)>
                {
                    ("应用头部", ApplyHeader),
                    ("在任务编辑器打开", OpenInQuestEditor)
                };
                if (!string.IsNullOrEmpty(linkedDialogue) && File.Exists(linkedDialogue))
                    headerButtons.Add(("打开关联对话", () => _owner.ShowDialogue(linkedDialogue)));

                Buttons(headerButtons.ToArray());
            }
            else if (_dialogue != null)
            {
                AddNote("对话 · " + _owner.CurrentAssetName + "   id=" + (_dialogue.DialogueId ?? ""), NarrativeEditorTheme.TextDim, 20f);

                // 对话级设置直接落到 DialogueConfigData，运行时由 DialogueFactory 读进 Dialogue；
                // 这一组就是"过场式沉浸"的开关（黑边 / 不可跳过 / 能否移动 / 默认镜头）。
                Section("对话级设置（沉浸式）");
                var config = _dialogue.Config;
                _dlgBars = BoolField("电影黑边", config?.ShowCinematicBars ?? false);
                _dlgUnskippable = BoolField("禁止跳过", config?.Unskippable ?? false);
                _dlgFreeMovement = BoolField("允许移动", config?.FreeMovement ?? false);
                _dlgCanExit = BoolField("可中途退出", config?.CanBeExited ?? true);
                _dlgPriority = Field("优先级", (config?.Priority ?? 0).ToString());
                _dlgEndDistance = Field("自动结束距离", (config?.EndDialogueDist ?? -1f).ToString("0.##"));
                RefreshShotChoices();
                _dlgDefaultShot = ShotField("默认镜头", config?.DefaultShotName, "（无默认镜头）");
                Buttons(("应用对话设置", ApplyDialogueConfig));
            }

            // --- 资产文件操作：文件名与内部 id 一起动 ---
            Section("资产文件");
            var assetRename = Field("新文件名", "");
            Buttons(("重命名资产", () =>
            {
                string error = _owner.RenameCurrentAsset(assetRename.Text);
                if (error != null) _owner.Report("操作被拒绝：" + error);
            }),
            ("复制资产", () =>
            {
                string error = _owner.DuplicateCurrentAsset(assetRename.Text);
                if (error != null) _owner.Report("操作被拒绝：" + error);
                else _owner.Report("已复制为 " + NarrativeAssetStore.SanitizeFileName(assetRename.Text) + "（当前编辑的仍是原资产）");
            }));

            var deleteButton = new Button
            {
                Bounds = new Rectangle(10f, _y, Math.Max(96f, ContentWidth - 98f), 24f),
                Text = "删除资产",
                TextColor = NarrativeEditorTheme.Failure
            };
            deleteButton.Clicked += () =>
            {
                if (!_deletePending)
                {
                    _deletePending = true;
                    deleteButton.Text = "再点一次，确认删除";
                    return;
                }
                _deletePending = false;
                string error = _owner.DeleteCurrentAsset();
                if (error != null) _owner.Report("操作被拒绝：" + error);
            };
            AddChild(deleteButton);
            _y += 30f;
        }

        private void ApplyHeader()
        {
            string error = ApplyHeaderFields();
            if (error != null) _owner.Report("操作被拒绝：" + error);
            else _owner.Mutated("已更新任务头部字段（内存），点「保存」写盘。");
        }

        /// <summary>应用对话级设置；数值字段不合法时拒绝而不是悄悄写成 0。</summary>
        private void ApplyDialogueConfig()
        {
            if (_dialogue == null) return;

            if (!int.TryParse((_dlgPriority?.Text ?? "0").Trim(), out int priority))
            {
                _owner.Report("操作被拒绝：优先级必须是整数。");
                return;
            }

            string distanceText = (_dlgEndDistance?.Text ?? "-1").Trim();
            if (!float.TryParse(distanceText, out float endDistance))
            {
                _owner.Report("操作被拒绝：自动结束距离必须是数字（负数＝不按距离结束）。");
                return;
            }

            string error = NarrativeAssetStore.SetDialogueConfig(_dialogue,
                BoolOf(_dlgBars, false) == "1",
                BoolOf(_dlgUnskippable, false) == "1",
                BoolOf(_dlgFreeMovement, false) == "1",
                BoolOf(_dlgCanExit, true) == "1",
                priority,
                ShotValueOf(_dlgDefaultShot),
                endDistance);

            if (error != null) _owner.Report("操作被拒绝：" + error);
            else _owner.Mutated("已更新对话级设置（内存），点「保存」写盘。");
        }

        /// <summary>把表单写回头部（含模板 id）；返回错误串，供"应用"与"在任务编辑器打开"共用。</summary>
        private string ApplyHeaderFields()
        {
            if (_quest == null) return "当前没有打开的任务";

            string error = NarrativeAssetStore.SetQuestHeader(_quest, _questName?.Text, _questDescription?.Text, TextOf(_questDialogue));
            if (error != null) return error;

            return NarrativeAssetStore.SetQuestTemplateId(_quest, _questTemplate?.Text);
        }

        private string DescribeTemplateLink()
        {
            int templateId = _quest?.QuestTemplateId ?? 0;
            if (templateId <= 0) return "模板 id 留空＝纯叙事任务；填 DB 任务编号即可与任务编辑器互跳。";

            int others = 0;
            foreach (var path in NarrativeAssetStore.QuestAssetsForTemplate(templateId))
            {
                if (!string.Equals(NarrativeDataPaths.Resolve(path), _owner.CurrentPath, StringComparison.OrdinalIgnoreCase)) others++;
            }

            return others > 0
                ? $"模板 {templateId}：另有 {others} 条叙事图也声明了它（一条模板通常只挂一条）。"
                : $"模板 {templateId}：当前只有这条叙事图声明。";
        }

        /// <summary>先把表单（含模板 id）落到内存，再唤起任务编辑器并选中那条模板。</summary>
        private void OpenInQuestEditor()
        {
            string error = ApplyHeaderFields();
            if (error != null)
            {
                _owner.Report("操作被拒绝：" + error);
                return;
            }

            int templateId = _quest?.QuestTemplateId ?? 0;
            if (templateId <= 0)
            {
                _owner.Report("先填「模板 id」（任务编辑器里那条 DB 任务的编号）再打开。");
                return;
            }

            string openError = QuestEditorLink.OpenTemplate(templateId);
            if (openError != null) _owner.Report(openError);
            else _owner.Mutated($"已打开任务编辑器并选中模板 {templateId}（内存里的模板 id 也一并记下了）。");
        }

        // -------------------------------------------------------------------
        // 任务：状态
        // -------------------------------------------------------------------

        private void BuildQuestState()
        {
            var state = NarrativeAssetStore.FindQuestState(_quest, _selection.Id);
            if (state == null)
            {
                AddNote("选中的状态已不存在。", NarrativeEditorTheme.Failure, 24f);
                return;
            }

            Section("状态 · " + state.Id);
            AddNote(StateTypeName(state.StateType) + (string.Equals(_quest.StartStateId, state.Id, StringComparison.OrdinalIgnoreCase) ? " · 起始状态" : ""),
                NarrativeEditorTheme.Regular, 20f);

            _stateDescription = Field("描述", state.Description);
            _stateType = StateTypeField("状态类型", state.StateType);
            var stateRename = Field("新 id", "");
            Buttons(("重命名", () => RenameState(state.Id, stateRename.Text)));
            Buttons(("应用修改", ApplyPending), ("复制", () => DuplicateState(state.Id)),
                    ("设为起始", () => SetStartState(state.Id)),
                    ("删除状态", () => DeleteState(state.Id)));

            BuildLogicList("事件", state.Events, NarrativeLogicKind.Event, state.Id);
            BuildLogicList("条件", state.Conditions, NarrativeLogicKind.Condition, state.Id);
        }

        private void RenameState(string oldId, string newId)
        {
            string trimmed = (newId ?? "").Trim();
            string error = NarrativeAssetStore.RenameQuestState(_quest, oldId, trimmed);
            if (error != null)
            {
                _owner.Report("操作被拒绝：" + error);
                return;
            }
            _owner.Mutated("已重命名状态 " + oldId + " → " + trimmed + "（引用已同步）。");
            _owner.SelectNode(NarrativeSelectionKind.QuestState, trimmed);
        }

        private void RenameBranch(string oldId, string newId)
        {
            string trimmed = (newId ?? "").Trim();
            string error = NarrativeAssetStore.RenameQuestBranch(_quest, oldId, trimmed);
            if (error != null)
            {
                _owner.Report("操作被拒绝：" + error);
                return;
            }
            _owner.Mutated("已重命名分支 " + oldId + " → " + trimmed + "（内存）。");
            _owner.SelectNode(NarrativeSelectionKind.QuestBranch, trimmed);
        }

        private void RenameNpc(string oldId, string newId)
        {
            string trimmed = (newId ?? "").Trim();
            string error = NarrativeAssetStore.RenameDialogueNode(_dialogue, oldId, trimmed);
            if (error != null)
            {
                _owner.Report("操作被拒绝：" + error);
                return;
            }
            _owner.Mutated("已重命名 NPC 节点 " + oldId + " → " + trimmed + "（连线已同步）。");
            _owner.SelectNode(NarrativeSelectionKind.DialogueNpc, trimmed);
        }

        private void RenamePlayer(string oldId, string newId)
        {
            string trimmed = (newId ?? "").Trim();
            string error = NarrativeAssetStore.RenameDialogueNode(_dialogue, oldId, trimmed);
            if (error != null)
            {
                _owner.Report("操作被拒绝：" + error);
                return;
            }
            _owner.Mutated("已重命名玩家节点 " + oldId + " → " + trimmed + "（连线已同步）。");
            _owner.SelectNode(NarrativeSelectionKind.DialoguePlayer, trimmed);
        }

        private void RenameSpeaker(string oldId, string newId)
        {
            string trimmed = (newId ?? "").Trim();
            string error = NarrativeAssetStore.RenameDialogueSpeaker(_dialogue, oldId, trimmed);
            if (error != null)
            {
                _owner.Report("操作被拒绝：" + error);
                return;
            }
            _owner.Mutated("已重命名说话者 " + oldId + " → " + trimmed + "（节点引用已同步）。");
            _owner.SelectNode(NarrativeSelectionKind.Speaker, trimmed);
        }

        private void DuplicateState(string id)
        {
            string error = NarrativeAssetStore.DuplicateQuestState(_quest, id, out string newId);
            if (error != null)
            {
                _owner.Report("操作被拒绝：" + error);
                return;
            }
            _owner.Mutated("已复制状态为 " + newId + "（内存）。");
            _owner.SelectNode(NarrativeSelectionKind.QuestState, newId);
        }

        private void DuplicateBranch(string id)
        {
            string error = NarrativeAssetStore.DuplicateQuestBranch(_quest, id, out string newId);
            if (error != null)
            {
                _owner.Report("操作被拒绝：" + error);
                return;
            }
            _owner.Mutated("已复制分支为 " + newId + "（内存）。");
            _owner.SelectNode(NarrativeSelectionKind.QuestBranch, newId);
        }

        private void DuplicateNpc(string id)
        {
            string error = NarrativeAssetStore.DuplicateDialogueNpc(_dialogue, id, out string newId);
            if (error != null)
            {
                _owner.Report("操作被拒绝：" + error);
                return;
            }
            _owner.Mutated("已复制 NPC 节点为 " + newId + "（内存）。");
            _owner.SelectNode(NarrativeSelectionKind.DialogueNpc, newId);
        }

        private void DuplicatePlayer(string id)
        {
            string error = NarrativeAssetStore.DuplicateDialoguePlayer(_dialogue, id, out string newId);
            if (error != null)
            {
                _owner.Report("操作被拒绝：" + error);
                return;
            }
            _owner.Mutated("已复制玩家选项为 " + newId + "（内存）。");
            _owner.SelectNode(NarrativeSelectionKind.DialoguePlayer, newId);
        }

        private void SetStartState(string id)
        {            string error = NarrativeAssetStore.SetQuestStartState(_quest, id);
            if (error != null) _owner.Report("操作被拒绝：" + error);
            else _owner.Mutated("已把 " + id + " 设为起始状态（内存）。");
        }

        private void DeleteState(string id)
        {
            string error = NarrativeAssetStore.RemoveQuestState(_quest, id);
            if (error != null) _owner.Report("操作被拒绝：" + error);
            else
            {
                _owner.ClearSelection();
                _owner.Mutated("已删除状态 " + id + "（连带删掉指向它的分支，内存）。");
            }
        }

        // -------------------------------------------------------------------
        // 任务：分支
        // -------------------------------------------------------------------

        private void BuildQuestBranch()
        {
            var branch = NarrativeAssetStore.FindQuestBranch(_quest, _selection.Id);
            if (branch == null)
            {
                AddNote("选中的分支已不存在。", NarrativeEditorTheme.Failure, 24f);
                return;
            }

            Section("分支 · " + branch.Id);
            _branchDescription = Field("描述", branch.Description);
            _branchFrom = DropdownField("起点状态", NarrativeAssetStore.QuestStateIds(_quest), branch.FromStateId);
            _branchTo = DropdownField("终点状态", NarrativeAssetStore.QuestStateIds(_quest), branch.ToStateId);
            _branchHidden = BoolField("隐藏分支", branch.Hidden);
            var branchRename = Field("新 id", "");
            Buttons(("重命名", () => RenameBranch(branch.Id, branchRename.Text)));
            Buttons(("应用修改", ApplyPending), ("复制", () => DuplicateBranch(branch.Id)),
                    ("删除分支", () => DeleteBranch(branch.Id)));

            Section("分支任务 · " + (branch.Tasks?.Count ?? 0) + " 条");
            if (branch.Tasks != null)
            {
                for (int i = 0; i < branch.Tasks.Count; i++)
                {
                    var task = branch.Tasks[i];
                    string text = "[" + i + "] " + task.Type + " · " + (task.TargetId ?? "-") + " x" + (task.RequiredQuantity?.ToString() ?? "-");
                    IndexRow(text, i, () => RemoveTask(branch.Id, i));
                }
            }
            else
            {
                AddNote("（无任务）", NarrativeEditorTheme.TextDim, 20f);
            }

            Section("加任务");
            _taskType = DropdownField("type", NarrativeAssetStore.RegisteredTaskNames());
            _taskTarget = Field("targetId", "");
            _taskQuantity = Field("数量", "");
            _taskDescription = Field("描述", "");
            _taskParams = Field("params", "");
            Buttons(("添加任务", AddTask));

            BuildLogicList("事件", branch.Events, NarrativeLogicKind.Event, branch.Id);
            BuildLogicList("条件", branch.Conditions, NarrativeLogicKind.Condition, branch.Id);
        }

        private void DeleteBranch(string id)
        {
            string error = NarrativeAssetStore.RemoveQuestBranch(_quest, id);
            if (error != null) _owner.Report("操作被拒绝：" + error);
            else
            {
                _owner.ClearSelection();
                _owner.Mutated("已删除分支 " + id + "（内存）。");
            }
        }

        private void AddTask()
        {
            string branchId = _selection.Id;
            var branch = NarrativeAssetStore.FindQuestBranch(_quest, branchId);
            if (branch == null) return;

            string error = ApplyPendingFields();
            if (error != null)
            {
                _owner.Report("操作被拒绝：" + error);
                return;
            }

            error = NarrativeAssetStore.AddQuestTask(_quest, branchId, _taskType?.SelectedItem ?? "", _taskTarget?.Text,
                _taskQuantity?.Text, _taskDescription?.Text, _taskParams?.Text);
            if (error != null) _owner.Report("操作被拒绝：" + error);
            else _owner.Mutated("已给分支 " + branchId + " 加一条任务（内存）。");
        }

        private void RemoveTask(string branchId, int index)
        {
            string error = NarrativeAssetStore.RemoveQuestTask(_quest, branchId, index.ToString());
            if (error != null) _owner.Report("操作被拒绝：" + error);
            else _owner.Mutated("已删除任务 #" + index + "（内存）。");
        }

        // -------------------------------------------------------------------
        // 对话：NPC / 玩家 / 说话者
        // -------------------------------------------------------------------

        private void BuildDialogueNpc()
        {
            var node = NarrativeAssetStore.FindNpcNode(_dialogue, _selection.Id);
            if (node == null)
            {
                AddNote("选中的 NPC 节点已不存在。", NarrativeEditorTheme.Failure, 24f);
                return;
            }

            Section("NPC 节点 · " + node.Id);
            AddNote(node.IsRoot ? "根节点（对话从这里开始）" : "普通节点", NarrativeEditorTheme.Accent, 20f);

            RefreshShotChoices();
            _npcSpeaker = DropdownField("speakerId", SpeakerIds(), node.SpeakerId);
            _npcText = Field("台词", node.Line?.Text ?? "");
            _npcShot = ShotField("镜头", node.Line?.ShotName);
            _npcCutscene = CutsceneField("CG 视频", node.Line?.CutscenePath);
            _npcWaitCutscene = BoolField("等 CG 播完", node.Line?.WaitForCutscene ?? false);
            _npcSkippable = BoolField("可跳过", node.IsSkippable);
            _npcRoot = BoolField("根节点", node.IsRoot);
            var npcRename = Field("新 id", "");
            Buttons(("重命名", () => RenameNpc(node.Id, npcRename.Text)));
            Buttons(("应用修改", ApplyPending), ("复制", () => DuplicateNpc(node.Id)),
                    ("设为根节点", () => SetRoot(node.Id)),
                    ("删除节点", () => DeleteNode(node.Id)));

            BuildLinkList(node.Id, node.NpcReplies, node.PlayerReplies);
            BuildLogicList("事件", node.Events, NarrativeLogicKind.Event, node.Id);
            BuildLogicList("条件", node.Conditions, NarrativeLogicKind.Condition, node.Id);
        }

        private void BuildDialoguePlayer()
        {
            var node = NarrativeAssetStore.FindPlayerNode(_dialogue, _selection.Id);
            if (node == null)
            {
                AddNote("选中的玩家节点已不存在。", NarrativeEditorTheme.Failure, 24f);
                return;
            }

            Section("玩家选项 · " + node.Id);
            RefreshShotChoices();
            _playerOption = Field("optionText", node.OptionText);
            _playerText = Field("台词", node.Line?.Text ?? "");
            _playerShot = ShotField("镜头", node.Line?.ShotName);
            _playerCutscene = CutsceneField("CG 视频", node.Line?.CutscenePath);
            _playerWaitCutscene = BoolField("等 CG 播完", node.Line?.WaitForCutscene ?? false);
            _playerAuto = BoolField("自动选择", node.AutoSelect);
            _playerAutoOnly = BoolField("仅唯一选项时自动", node.AutoSelectIfOnlyReply);
            var playerRename = Field("新 id", "");
            Buttons(("重命名", () => RenamePlayer(node.Id, playerRename.Text)));
            Buttons(("应用修改", ApplyPending), ("复制", () => DuplicatePlayer(node.Id)),
                    ("删除节点", () => DeleteNode(node.Id)));

            BuildLinkList(node.Id, node.NpcReplies, null);
            BuildLogicList("事件", node.Events, NarrativeLogicKind.Event, node.Id);
            BuildLogicList("条件", node.Conditions, NarrativeLogicKind.Condition, node.Id);
        }

        private void BuildSpeaker()
        {
            var speaker = NarrativeAssetStore.FindDialogueSpeaker(_dialogue, _selection.Id);
            if (speaker == null)
            {
                AddNote("选中的说话者已不存在。", NarrativeEditorTheme.Failure, 24f);
                return;
            }

            Section("说话者 · " + speaker.SpeakerId);
            RefreshShotChoices();
            _speakerName = Field("displayName", speaker.DisplayName);
            _speakerPlayer = BoolField("是玩家", speaker.IsPlayer);
            _speakerShot = ShotField("默认镜头", speaker.DefaultShotName);
            _speakerReplyShot = ShotField("选择回复镜头", speaker.SelectingReplyShotName);
            var speakerRename = Field("新 id", "");
            Buttons(("重命名", () => RenameSpeaker(speaker.SpeakerId, speakerRename.Text)));
            Buttons(("应用修改", ApplyPending), ("删除说话者", () => DeleteSpeaker(speaker.SpeakerId)));
        }

        private void SetRoot(string id)
        {
            string error = NarrativeAssetStore.SetDialogueRootNode(_dialogue, id);
            if (error != null) _owner.Report("操作被拒绝：" + error);
            else _owner.Mutated("已把 " + id + " 设为根节点（内存）。");
        }

        private void DeleteNode(string id)
        {
            string error = NarrativeAssetStore.RemoveDialogueNode(_dialogue, id);
            if (error != null) _owner.Report("操作被拒绝：" + error);
            else
            {
                _owner.ClearSelection();
                _owner.Mutated("已删除节点 " + id + "（连带断掉指向它的连线，内存）。");
            }
        }

        private void DeleteSpeaker(string id)
        {
            string error = NarrativeAssetStore.RemoveDialogueSpeaker(_dialogue, id);
            if (error != null) _owner.Report("操作被拒绝：" + error);
            else
            {
                _owner.ClearSelection();
                _owner.Mutated("已删除说话者 " + id + "（内存）。");
            }
        }

        /// <summary>图上按 Delete 时走这里：按选中项类型分派到各自的删除流程（含连带断链）。返回是否处理了。</summary>
        internal bool DeleteSelected()
        {
            switch (_selection.Kind)
            {
                case NarrativeSelectionKind.QuestState:
                    DeleteState(_selection.Id);
                    return true;
                case NarrativeSelectionKind.QuestBranch:
                    DeleteBranch(_selection.Id);
                    return true;
                case NarrativeSelectionKind.DialogueNpc:
                case NarrativeSelectionKind.DialoguePlayer:
                    DeleteNode(_selection.Id);
                    return true;
                case NarrativeSelectionKind.Speaker:
                    DeleteSpeaker(_selection.Id);
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>右键菜单「复制」：按选中类型走各自的复制流程。</summary>
        internal void CopySelected()
        {
            switch (_selection.Kind)
            {
                case NarrativeSelectionKind.QuestState: DuplicateState(_selection.Id); break;
                case NarrativeSelectionKind.QuestBranch: DuplicateBranch(_selection.Id); break;
                case NarrativeSelectionKind.DialogueNpc: DuplicateNpc(_selection.Id); break;
                case NarrativeSelectionKind.DialoguePlayer: DuplicatePlayer(_selection.Id); break;
            }
        }

        /// <summary>右键菜单「设为起始 / 设为根节点」。</summary>
        internal void SetStartOrRoot()
        {
            if (_selection.Kind == NarrativeSelectionKind.QuestState) SetStartState(_selection.Id);
            else if (_selection.Kind == NarrativeSelectionKind.DialogueNpc) SetRoot(_selection.Id);
        }

        /// <summary>右键菜单「反转方向」（只对分支有意义）。</summary>
        internal void FlipSelectedBranch()
        {
            if (_selection.Kind != NarrativeSelectionKind.QuestBranch) return;

            string error = NarrativeAssetStore.FlipQuestBranch(_quest, _selection.Id);
            if (error != null) _owner.Report("操作被拒绝：" + error);
            else _owner.Mutated("已反转分支 " + _selection.Id + " 的方向（内存）。");
        }

        /// <summary>右键菜单「新建后继并连接」：在选中节点右下方长出一个新节点并连好线。</summary>
        internal void ConnectNewNodeAfterSelected()
        {
            if (_selection.Kind == NarrativeSelectionKind.QuestState)
            {
                string error = NarrativeAssetStore.AddQuestStateConnected(_quest, _selection.Id, out string newId, out string branchId);
                if (error != null)
                {
                    _owner.Report("操作被拒绝：" + error);
                    return;
                }
                _owner.Mutated($"已新建状态 {newId}，并用分支 {branchId} 从 {_selection.Id} 连过去（内存）。");
                _owner.SelectNode(NarrativeSelectionKind.QuestState, newId);
                return;
            }

            if (_selection.Kind == NarrativeSelectionKind.DialogueNpc)
            {
                string error = NarrativeAssetStore.AddDialogueNpcReplyConnected(_dialogue, _selection.Id, out string newId);
                if (error != null)
                {
                    _owner.Report("操作被拒绝：" + error);
                    return;
                }
                _owner.Mutated($"已新建 NPC 节点 {newId} 并从 {_selection.Id} 连过去（内存）。");
                _owner.SelectNode(NarrativeSelectionKind.DialogueNpc, newId);
            }
        }

        // -------------------------------------------------------------------
        // 结构列表
        // -------------------------------------------------------------------

        private void BuildLogicList(string title, List<JsonElement> items, NarrativeLogicKind kind, string targetId)
        {
            Section(title + " · " + (items?.Count ?? 0) + " 条");
            if (items != null)
            {
                for (int i = 0; i < items.Count; i++)
                {
                    IndexRow(DescribeLogic(items[i]), i, () => RemoveLogic(targetId, kind, i));
                }
            }

            // 事件区先给一套「任务 / 对话」快捷表单：穿插任务与播对话是叙事主线，
            // 让作者手打 {"type":"StartQuest","questClassId":…} 才是这里最容易写错的地方。
            if (kind == NarrativeLogicKind.Event)
            {
                BuildStoryEventComposer(title, targetId);
                Section("自定义" + title + "（高级）");
            }

            var typeDropdown = DropdownField("类型", NarrativeAssetStore.Names(kind));
            var paramsBox = Field("params", "");
            var addButton = new Button
            {
                Bounds = new Rectangle(104f, _y, Math.Max(90f, ContentWidth - 98f), 24f),
                Text = "添加" + title
            };
            addButton.Clicked += () =>
            {
                string typeId = typeDropdown.SelectedItem ?? "";
                string error = _isQuest
                    ? NarrativeAssetStore.AddQuestLogic(_quest, targetId, kind, typeId, paramsBox.Text)
                    : NarrativeAssetStore.AddDialogueLogic(_dialogue, targetId, kind, typeId, paramsBox.Text);

                if (error != null) _owner.Report("操作被拒绝：" + error);
                else _owner.Mutated("已给 " + targetId + " 加一条" + NarrativeAssetStore.Label(kind) + "：" + typeId + "（内存）。");
            };
            AddChild(addButton);
            _y += 32f;
        }

        /// <summary>快捷事件的显示名与真实注册类型名的对照。作者看到中文，落盘写的永远是注册表里的类型名。</summary>
        private static readonly (string Display, string TypeId)[] StoryEventChoices =
        {
            ("开始任务（在叙事里插入任务）", "StartQuestEvent"),
            ("重开任务", "RestartQuestEvent"),
            ("结算任务（成功 / 失败）", "CompleteQuestEvent"),
            ("播放对话（镜头 / CG 随对话）", "BeginDialogueEvent"),
        };

        /// <summary>
        /// 任务 / 对话快捷事件：目标从磁盘资产里选，参数由表单拼成运行时认的 JSON，
        /// 不再要求作者记住 questClassId / dialogueClassId / startFromID 这些键名。
        /// </summary>
        private void BuildStoryEventComposer(string title, string targetId)
        {
            Section("任务 / 对话快捷事件");

            var choices = new List<string>();
            foreach (var choice in StoryEventChoices) choices.Add(choice.Display);

            var eventKind = DropdownField("事件", choices, StoryEventChoices[0].Display);
            var quest = DropdownField("任务资产", NarrativeAssetStore.QuestAssetNames());
            var startFrom = Field("起始状态 id", "");
            var succeed = BoolField("结算为成功", true);
            var message = Field("结算消息", "");
            var dialogue = DropdownField("对话资产", NarrativeAssetStore.DialogueAssetNames());
            var dialogueNode = Field("对话起始节点 id", "");

            Buttons(("插入事件", () =>
            {
                string typeId = StoryEventTypeId(eventKind);
                bool needsQuest = typeId != "BeginDialogueEvent";
                string target = needsQuest ? TextOf(quest) : TextOf(dialogue);
                if (string.IsNullOrWhiteSpace(target))
                {
                    _owner.Report("操作被拒绝：先选择要" + (needsQuest ? "穿插的任务资产" : "播放的对话资产") + "。");
                    return;
                }

                string paramsJson = BuildStoryEventParams(typeId, TextOf(quest), startFrom.Text,
                    BoolOf(succeed, true) == "1", message.Text, TextOf(dialogue), dialogueNode.Text);
                string error = _isQuest
                    ? NarrativeAssetStore.AddQuestLogic(_quest, targetId, NarrativeLogicKind.Event, typeId, paramsJson)
                    : NarrativeAssetStore.AddDialogueLogic(_dialogue, targetId, NarrativeLogicKind.Event, typeId, paramsJson);

                if (error != null) _owner.Report("操作被拒绝：" + error);
                else _owner.Mutated("已插入" + StoryEventDisplay(typeId) + "：" + target + "（内存）。");
            }));

            AddNote("任务事件写进状态 / 分支的 events，进入该节点就会执行；对话事件进入节点时播出。保存后运行时按资产名装载。",
                NarrativeEditorTheme.TextDim, 40f);
        }

        private static string StoryEventTypeId(Dropdown dropdown)
        {
            string text = dropdown?.SelectedItem ?? "";
            foreach (var choice in StoryEventChoices)
            {
                if (string.Equals(choice.Display, text, StringComparison.Ordinal)) return choice.TypeId;
            }
            return StoryEventChoices[0].TypeId;
        }

        private static string StoryEventDisplay(string typeId)
        {
            foreach (var choice in StoryEventChoices)
            {
                if (string.Equals(choice.TypeId, typeId, StringComparison.OrdinalIgnoreCase)) return choice.Display;
            }
            return typeId;
        }

        /// <summary>按运行时事件类的公共属性名拼参数；空的可选字段不落盘，保留运行时默认值。</summary>
        private static string BuildStoryEventParams(string typeId, string quest, string startFrom, bool succeed,
            string message, string dialogue, string dialogueNode)
        {
            var fields = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            switch (typeId)
            {
                case "StartQuestEvent":
                case "RestartQuestEvent":
                    fields["questClassId"] = (quest ?? "").Trim();
                    if (!string.IsNullOrWhiteSpace(startFrom)) fields["startFromId"] = startFrom.Trim();
                    break;
                case "CompleteQuestEvent":
                    fields["questClassId"] = (quest ?? "").Trim();
                    fields["bSucceed"] = succeed;
                    if (!string.IsNullOrWhiteSpace(message)) fields["message"] = message.Trim();
                    break;
                case "BeginDialogueEvent":
                    fields["dialogueClassId"] = (dialogue ?? "").Trim();
                    if (!string.IsNullOrWhiteSpace(dialogueNode)) fields["startFromID"] = dialogueNode.Trim();
                    break;
            }
            return JsonSerializer.Serialize(fields);
        }

        private void RemoveLogic(string targetId, NarrativeLogicKind kind, int index)
        {
            string error = _isQuest
                ? NarrativeAssetStore.RemoveQuestLogic(_quest, targetId, kind, index.ToString())
                : NarrativeAssetStore.RemoveDialogueLogic(_dialogue, targetId, kind, index.ToString());

            if (error != null) _owner.Report("操作被拒绝：" + error);
            else _owner.Mutated("已删除 " + NarrativeAssetStore.Label(kind) + " #" + index + "（内存）。");
        }

        /// <summary>
        /// 事件/条件行的显示文本：常见的任务与对话事件翻成中文摘要（带目标资产名），
        /// 其余类型保持"类型 + 原文"，避免为了好看而隐藏作者写进去的字段。
        /// </summary>
        private static string DescribeLogic(JsonElement element)
        {
            string type = ReadLogicString(element, "type");
            string normalized = NormalizeLogicType(type);
            string text = null;

            switch (normalized)
            {
                case "StartQuest":
                    text = "开始任务 → " + WithStartFrom(ReadLogicString(element, "questClassId"), ReadLogicString(element, "startFromId"));
                    break;
                case "RestartQuest":
                    text = "重开任务 → " + WithStartFrom(ReadLogicString(element, "questClassId"), ReadLogicString(element, "startFromId"));
                    break;
                case "CompleteQuest":
                    text = (ReadLogicBool(element, "bSucceed") == false ? "结算任务（失败）→ " : "结算任务（成功）→ ")
                           + WithStartFrom(ReadLogicString(element, "questClassId"), "");
                    break;
                case "BeginDialogue":
                    text = "播放对话 → " + WithStartFrom(ReadLogicString(element, "dialogueClassId"), ReadLogicString(element, "startFromID"));
                    break;
                case "GiveItem":
                    text = "给物品 → " + ReadLogicString(element, "itemId") + " ×" + ReadLogicString(element, "quantity");
                    break;
                case "SetTag":
                    text = "设置标签 → " + ReadLogicString(element, "tag")
                           + (ReadLogicBool(element, "bAdd") == false ? "（移除）" : "（添加）");
                    break;
                case "PlaySound":
                    text = "播放音效 → " + ReadLogicString(element, "soundPath");
                    break;
                case "Teleport":
                    text = "传送 → " + ReadLogicString(element, "targetLocationName");
                    break;
            }

            if (text != null) return NarrativeEditorTheme.Fit(text, 240f, NarrativeEditorTheme.FontSmall);

            string raw = element.ValueKind == JsonValueKind.Object ? element.ToString() : "";
            raw = raw.Replace("\r", " ").Replace("\n", " ").Trim();
            return NarrativeEditorTheme.Fit(type + "  " + raw, 240f, NarrativeEditorTheme.FontSmall);
        }

        /// <summary>资产里的类型多写成短别名（StartQuest）或类名（StartQuestEvent），两种都归一到短名。</summary>
        private static string NormalizeLogicType(string type)
        {
            string text = (type ?? "").Trim();
            if (text.EndsWith("Event", StringComparison.OrdinalIgnoreCase)) text = text.Substring(0, text.Length - 5);
            else if (text.EndsWith("Condition", StringComparison.OrdinalIgnoreCase)) text = text.Substring(0, text.Length - 9);
            return text;
        }

        private static string WithStartFrom(string target, string startFrom)
        {
            string text = string.IsNullOrWhiteSpace(target) ? "（未填）" : target;
            return string.IsNullOrWhiteSpace(startFrom) ? text : text + " @ " + startFrom;
        }

        private static string ReadLogicString(JsonElement element, string name)
        {
            try
            {
                if (element.ValueKind != JsonValueKind.Object) return "";
                foreach (var prop in element.EnumerateObject())
                {
                    if (!string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
                    return prop.Value.ValueKind == JsonValueKind.String ? (prop.Value.GetString() ?? "") : prop.Value.ToString();
                }
            }
            catch { }
            return "";
        }

        private static bool? ReadLogicBool(JsonElement element, string name)
        {
            try
            {
                if (element.ValueKind != JsonValueKind.Object) return null;
                foreach (var prop in element.EnumerateObject())
                {
                    if (!string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
                    if (prop.Value.ValueKind == JsonValueKind.True) return true;
                    if (prop.Value.ValueKind == JsonValueKind.False) return false;
                    return null;
                }
            }
            catch { }
            return null;
        }

        private void BuildLinkList(string fromId, List<string> npcTargets, List<string> playerTargets)
        {
            int total = (npcTargets?.Count ?? 0) + (playerTargets?.Count ?? 0);
            Section("连线 · " + total + " 条");

            if (npcTargets != null)
            {
                foreach (var id in npcTargets) LinkRow(fromId, id, "NPC");
            }
            if (playerTargets != null)
            {
                foreach (var id in playerTargets) LinkRow(fromId, id, "玩家");
            }

            var linkTargets = NarrativeAssetStore.DialogueNodeIds(_dialogue);
            linkTargets.RemoveAll(id => string.Equals(id, fromId, StringComparison.OrdinalIgnoreCase));
            _linkTarget = DropdownField("目标节点", linkTargets);
            Buttons(("连线", () =>
            {
                string error = NarrativeAssetStore.LinkDialogueAuto(_dialogue, fromId, TextOf(_linkTarget));
                if (error != null) _owner.Report("操作被拒绝：" + error);
                else _owner.Mutated("已连线 " + fromId + " → " + TextOf(_linkTarget) + "（内存）。");
            }));
        }

        private void LinkRow(string fromId, string toId, string kind)
        {
            var row = LabelRow("→ " + toId + "   " + kind, NarrativeEditorTheme.Text);
            float y = _y;
            var button = new Button
            {
                Bounds = new Rectangle(ContentWidth - 52f, y + 1f, 48f, 22f),
                Text = "断开"
            };
            button.Clicked += () =>
            {
                string error = NarrativeAssetStore.RemoveDialogueLink(_dialogue, fromId, toId);
                if (error != null) _owner.Report("操作被拒绝：" + error);
                else _owner.Mutated("已断开 " + fromId + " → " + toId + "（内存）。");
            };
            AddChild(button);
            _y += 24f;
        }

        // -------------------------------------------------------------------
        // 总览
        // -------------------------------------------------------------------

        private void BuildOverview()
        {
            if (_isQuest && _quest != null)
            {
                Section("任务总览");
                AddNote("状态 " + _quest.States.Count + " 个 · 分支 " + _quest.Branches.Count + " 个 · 起始状态 " +
                        (string.IsNullOrEmpty(_quest.StartStateId) ? "（未设置）" : _quest.StartStateId),
                    NarrativeEditorTheme.TextDim, 22f);

                Section("状态");
                foreach (var state in _quest.States)
                {
                    var row = LabelRow(state.Id + "  " + StateTypeName(state.StateType),
                        string.Equals(_quest.StartStateId, state.Id, StringComparison.OrdinalIgnoreCase)
                            ? NarrativeEditorTheme.Selection
                            : NarrativeEditorTheme.Text);
                    row.TooltipText = state.Description;
                    ClickRow(() => _owner.SelectNode(NarrativeSelectionKind.QuestState, state.Id));
                }

                Section("分支");
                foreach (var branch in _quest.Branches)
                {
                    LabelRow(branch.Id + "  " + branch.FromStateId + " → " + branch.ToStateId, NarrativeEditorTheme.Text);
                    ClickRow(() => _owner.SelectNode(NarrativeSelectionKind.QuestBranch, branch.Id));
                }
            }
            else if (_dialogue != null)
            {
                Section("对话总览");
                AddNote("说话者 " + (_dialogue.Speakers?.Count ?? 0) + " · NPC " + (_dialogue.NpcReplies?.Count ?? 0) +
                        " · 玩家选项 " + (_dialogue.PlayerReplies?.Count ?? 0), NarrativeEditorTheme.TextDim, 22f);

                Section("说话者");
                foreach (var speaker in _dialogue.Speakers ?? new List<SpeakerData>())
                {
                    LabelRow(speaker.SpeakerId + "  " + speaker.DisplayName + (speaker.IsPlayer ? "  （玩家）" : ""),
                        NarrativeEditorTheme.Text);
                    ClickRow(() => _owner.SelectNode(NarrativeSelectionKind.Speaker, speaker.SpeakerId));
                }

                Section("节点");
                foreach (var npc in _dialogue.NpcReplies ?? new List<NpcReplyData>())
                {
                    LabelRow((npc.IsRoot ? "★ " : "  ") + npc.Id + "  " + Truncate(npc.Line?.Text, 18),
                        npc.IsRoot ? NarrativeEditorTheme.Selection : NarrativeEditorTheme.Text);
                    ClickRow(() => _owner.SelectNode(NarrativeSelectionKind.DialogueNpc, npc.Id));
                }
                foreach (var player in _dialogue.PlayerReplies ?? new List<PlayerReplyData>())
                {
                    LabelRow("  " + player.Id + "  " + Truncate(player.OptionText, 18), NarrativeEditorTheme.Player);
                    ClickRow(() => _owner.SelectNode(NarrativeSelectionKind.DialoguePlayer, player.Id));
                }
            }

            AddNote("提示：在中间图里点节点也能选中；拖动节点可改布局，保存后写回资产。",
                NarrativeEditorTheme.TextDim, 40f);

            if (_isQuest) BuildQuestCreateForms();
            else BuildDialogueCreateForms();
        }

        private void BuildQuestCreateForms()
        {
            Section("新建状态");
            var stateId = Field("状态 id", "");
            var stateType = StateTypeField("状态类型", EStateNodeType.Regular);
            var stateDescription = Field("描述", "");
            Buttons(("添加状态", () =>
            {
                string error = NarrativeAssetStore.AddQuestState(_quest, stateId.Text, TextOf(stateType), stateDescription.Text);
                if (error != null)
                {
                    _owner.Report("操作被拒绝：" + error);
                    return;
                }
                _owner.Mutated("已添加状态 " + stateId.Text + "（内存）。");
                _owner.SelectNode(NarrativeSelectionKind.QuestState, stateId.Text);
            }));

            Section("新建分支");
            var branchId = Field("分支 id", "");
            var branchFrom = DropdownField("起点状态", NarrativeAssetStore.QuestStateIds(_quest), _quest.StartStateId);
            var branchTo = DropdownField("终点状态", NarrativeAssetStore.QuestStateIds(_quest));
            var branchDescription = Field("描述", "");
            Buttons(("添加分支", () =>
            {
                string error = NarrativeAssetStore.AddQuestBranch(_quest, branchId.Text, TextOf(branchFrom), TextOf(branchTo), branchDescription.Text);
                if (error != null)
                {
                    _owner.Report("操作被拒绝：" + error);
                    return;
                }
                _owner.Mutated("已添加分支 " + branchId.Text + "（内存）。");
                _owner.SelectNode(NarrativeSelectionKind.QuestBranch, branchId.Text);
            }));
        }

        private void BuildDialogueCreateForms()
        {
            Section("新建说话者");
            var speakerId = Field("speakerId", "");
            var speakerName = Field("displayName", "");
            var speakerPlayer = BoolField("是玩家", false);
            Buttons(("添加说话者", () =>
            {
                string error = NarrativeAssetStore.AddDialogueSpeaker(_dialogue, speakerId.Text, speakerName.Text,
                    BoolOf(speakerPlayer, false));
                if (error != null)
                {
                    _owner.Report("操作被拒绝：" + error);
                    return;
                }
                _owner.Mutated("已添加说话者 " + speakerId.Text + "（内存）。");
                _owner.SelectNode(NarrativeSelectionKind.Speaker, speakerId.Text);
            }));

            Section("新建 NPC 节点");
            var npcId = Field("节点 id", "");
            var npcSpeaker = DropdownField("speakerId", SpeakerIds());
            var npcText = Field("台词", "");
            var npcRoot = BoolField("根节点", false);
            Buttons(("添加 NPC 节点", () =>
            {
                string error = NarrativeAssetStore.AddDialogueNpcReply(_dialogue, npcId.Text, npcSpeaker.SelectedItem ?? "",
                    npcText.Text, BoolOf(npcRoot, false));
                if (error != null)
                {
                    _owner.Report("操作被拒绝：" + error);
                    return;
                }
                _owner.Mutated("已添加 NPC 节点 " + npcId.Text + "（内存）。");
                _owner.SelectNode(NarrativeSelectionKind.DialogueNpc, npcId.Text);
            }));

            Section("新建玩家选项");
            var playerId = Field("节点 id", "");
            var playerOption = Field("optionText", "");
            var playerText = Field("台词", "");
            Buttons(("添加玩家选项", () =>
            {
                string error = NarrativeAssetStore.AddDialoguePlayerReply(_dialogue, playerId.Text, playerOption.Text, playerText.Text);
                if (error != null)
                {
                    _owner.Report("操作被拒绝：" + error);
                    return;
                }
                _owner.Mutated("已添加玩家选项 " + playerId.Text + "（内存）。");
                _owner.SelectNode(NarrativeSelectionKind.DialoguePlayer, playerId.Text);
            }));
        }

        // -------------------------------------------------------------------
        // 表单取值 / 应用
        // -------------------------------------------------------------------

        private string ApplyPendingFields()
        {
            switch (_selection.Kind)
            {
                case NarrativeSelectionKind.QuestState:
                    return NarrativeAssetStore.SetQuestState(_quest, _selection.Id, _stateDescription?.Text, TextOf(_stateType));
                case NarrativeSelectionKind.QuestBranch:
                    return NarrativeAssetStore.SetQuestBranch(_quest, _selection.Id, _branchDescription?.Text,
                        TextOf(_branchFrom), TextOf(_branchTo), BoolOf(_branchHidden, false));
                case NarrativeSelectionKind.DialogueNpc:
                {
                    string error = NarrativeAssetStore.SetDialogueNpc(_dialogue, _selection.Id, _npcSpeaker?.SelectedItem ?? "",
                        _npcText?.Text, BoolOf(_npcSkippable, false), BoolOf(_npcRoot, false));
                    error ??= NarrativeAssetStore.SetDialogueLineShot(_dialogue, _selection.Id, ShotValueOf(_npcShot));
                    return error ?? NarrativeAssetStore.SetDialogueLineCutscene(_dialogue, _selection.Id,
                        CutsceneValueOf(_npcCutscene), BoolOf(_npcWaitCutscene, false) == "1");
                }
                case NarrativeSelectionKind.DialoguePlayer:
                {
                    string error = NarrativeAssetStore.SetDialoguePlayer(_dialogue, _selection.Id, _playerOption?.Text,
                        _playerText?.Text, BoolOf(_playerAuto, false), BoolOf(_playerAutoOnly, false));
                    error ??= NarrativeAssetStore.SetDialogueLineShot(_dialogue, _selection.Id, ShotValueOf(_playerShot));
                    return error ?? NarrativeAssetStore.SetDialogueLineCutscene(_dialogue, _selection.Id,
                        CutsceneValueOf(_playerCutscene), BoolOf(_playerWaitCutscene, false) == "1");
                }
                case NarrativeSelectionKind.Speaker:
                {
                    string error = NarrativeAssetStore.SetDialogueSpeaker(_dialogue, _selection.Id, _speakerName?.Text,
                        BoolOf(_speakerPlayer, false));
                    return error ?? NarrativeAssetStore.SetDialogueSpeakerShots(_dialogue, _selection.Id,
                        ShotValueOf(_speakerShot), ShotValueOf(_speakerReplyShot));
                }
                default:
                    return null;
            }
        }

        private void ApplyPending()
        {
            if (_selection.Kind == NarrativeSelectionKind.None) return;

            string error = ApplyPendingFields();
            if (error != null) _owner.Report("操作被拒绝：" + error);
            else _owner.Mutated("已应用修改（内存），点「保存」写盘。");
        }

        // -------------------------------------------------------------------
        // 控件工厂
        // -------------------------------------------------------------------

        private void RefreshShotChoices()
        {
            _shotChoices = NarrativeAssetStore.ShotChoices();
        }

        /// <summary>把镜头真名翻成下拉里的显示文本（找不到就原样显示，好让作者看见坏引用）。</summary>
        private string ShotDisplay(string shotValue, string noneLabel = "（说话者默认）")
        {
            if (string.IsNullOrWhiteSpace(shotValue)) return noneLabel;
            foreach (var choice in _shotChoices)
                if (string.Equals(choice.Value, shotValue, StringComparison.OrdinalIgnoreCase)) return choice.Display;
            return shotValue;
        }

        /// <summary>下拉显示文本 → 写进资产的镜头真名。</summary>
        private string ShotValueOf(Dropdown dropdown)
        {
            string selected = dropdown?.SelectedItem ?? "";
            foreach (var choice in _shotChoices)
                if (string.Equals(choice.Display, selected, StringComparison.OrdinalIgnoreCase)) return choice.Value;
            // 不在清单里（例如历史坏值）：原样写回，交给审计去喊
            return selected.StartsWith("（", StringComparison.Ordinal) ? "" : selected;
        }

        /// <summary>行内 CG 下拉：首项「（不播）」，其余是 Content 下的视频（工程相对路径）。</summary>
        private Dropdown CutsceneField(string caption, string currentPath)
        {
            var items = new List<string> { "（不播）" };
            foreach (var movie in NarrativeAssetStore.MovieChoices()) items.Add(movie);

            string selected = string.IsNullOrWhiteSpace(currentPath) ? "（不播）" : currentPath;
            return DropdownField(caption, items, selected);
        }

        private static string CutsceneValueOf(Dropdown dropdown)
        {
            string selected = dropdown?.SelectedItem ?? "";
            return selected.StartsWith("（", StringComparison.Ordinal) ? "" : selected;
        }

        private Dropdown ShotField(string caption, string currentShot, string noneLabel = "（说话者默认）")
        {
            var items = new List<string> { noneLabel };
            foreach (var choice in _shotChoices) items.Add(choice.Display);
            return DropdownField(caption, items, ShotDisplay(currentShot, noneLabel));
        }

        private void Section(string title)
        {
            _y += 8f;
            var label = new Label
            {
                Bounds = new Rectangle(10f, _y, ContentWidth, 20f),
                Text = title,
                TextColor = NarrativeEditorTheme.Accent,
                VerticalAlignment = TextAlignment.Center,
                ClipText = true
            };
            AddChild(label);
            _y += 22f;
            // 分隔线用真实控件画：构造期调用 Render2D 会被引擎拒绝（"Calling Render2D is only valid during rendering."）
            AddChild(new Panel
            {
                Bounds = new Rectangle(10f, _y - 3f, ContentWidth, 1f),
                BackgroundColor = NarrativeEditorTheme.Border
            });
        }

        private void AddNote(string text, Color color, float height)
        {
            var label = new Label
            {
                Bounds = new Rectangle(10f, _y, ContentWidth, height),
                Text = text,
                TextColor = color,
                Wrapping = TextWrapping.WrapWords,
                VerticalAlignment = TextAlignment.Near,
                ClipText = true
            };
            AddChild(label);
            _y += height + 2f;
        }

        private TextBox Field(string caption, string value)
        {
            float y = _y;
            var label = new Label
            {
                Bounds = new Rectangle(10f, y, 92f, 24f),
                Text = caption,
                TextColor = NarrativeEditorTheme.TextDim,
                VerticalAlignment = TextAlignment.Center,
                ClipText = true
            };
            AddChild(label);

            var box = new TextBox
            {
                Bounds = new Rectangle(104f, y, Math.Max(80f, ContentWidth - 98f), 24f),
                Text = value ?? ""
            };
            AddChild(box);
            _y += 28f;
            return box;
        }

        private Dropdown DropdownField(string caption, List<string> items, string value = null)
        {
            float y = _y;
            var label = new Label
            {
                Bounds = new Rectangle(10f, y, 92f, 24f),
                Text = caption,
                TextColor = NarrativeEditorTheme.TextDim,
                VerticalAlignment = TextAlignment.Center,
                ClipText = true
            };
            AddChild(label);

            var dropdown = new Dropdown
            {
                Bounds = new Rectangle(104f, y, Math.Max(80f, ContentWidth - 98f), 24f)
            };
            foreach (var item in items ?? new List<string>())
            {
                if (!string.IsNullOrEmpty(item)) dropdown.AddItem(item);
            }
            if (value != null && (items == null || !items.Contains(value))) dropdown.AddItem(value);
            if (value != null) dropdown.SelectedItem = value;

            AddChild(dropdown);
            _y += 28f;
            return dropdown;
        }

        private const string OffLabel = "否";
        private const string OnLabel = "是";
        private static readonly List<string> StateTypeValues = new List<string> { "Regular", "Success", "Failure" };

        /// <summary>布尔开关用下拉（否/是），不再让作者手打 0/1；写回时仍按运行时认的 "1"/"0"。</summary>
        private Dropdown BoolField(string caption, bool value)
        {
            return DropdownField(caption, new List<string> { OffLabel, OnLabel }, value ? OnLabel : OffLabel);
        }

        private static string BoolOf(Dropdown dropdown, bool fallback)
        {
            if (dropdown == null) return fallback ? "1" : "0";
            return dropdown.SelectedItem == OnLabel ? "1" : "0";
        }

        private static string TextOf(Dropdown dropdown)
        {
            return dropdown == null ? "" : (dropdown.SelectedItem ?? "");
        }

        /// <summary>状态类型只有三档，写成数字容易错；这里给名字，落盘仍由序列化器写数值。</summary>
        private Dropdown StateTypeField(string caption, EStateNodeType type)
        {
            return DropdownField(caption, StateTypeValues, type.ToString());
        }

        private void Buttons(params (string text, Action action)[] buttons)
        {
            float x = 10f;
            foreach (var (text, action) in buttons)
            {
                float width = Math.Max(72f, NarrativeEditorTheme.Measure(text, NarrativeEditorTheme.FontSmall) + 22f);
                if (x + width > ContentWidth + 10f)
                {
                    x = 10f;
                    _y += 28f;
                }

                var button = new Button
                {
                    Bounds = new Rectangle(x, _y, width, 24f),
                    Text = text,
                    TextColor = text.StartsWith("删除", StringComparison.Ordinal)
                        ? NarrativeEditorTheme.Failure
                        : NarrativeEditorTheme.Text
                };
                button.Clicked += action;
                AddChild(button);
                x += width + 6f;
            }
            _y += 30f;
        }

        private void IndexRow(string text, int index, Action remove)
        {
            var label = LabelRow("[" + index + "] " + text, NarrativeEditorTheme.Text);
            float y = _y - 24f;
            var button = new Button
            {
                Bounds = new Rectangle(ContentWidth - 50f, y + 1f, 46f, 22f),
                Text = "删",
                TextColor = NarrativeEditorTheme.Failure
            };
            button.Clicked += remove;
            AddChild(button);
            label.Bounds = new Rectangle(10f, y, ContentWidth - 60f, 24f);
        }

        private Label LabelRow(string text, Color color)
        {
            var label = new Label
            {
                Bounds = new Rectangle(10f, _y, ContentWidth, 24f),
                Text = text,
                TextColor = color,
                VerticalAlignment = TextAlignment.Center,
                ClipText = true
            };
            AddChild(label);
            _y += 24f;
            return label;
        }

        /// <summary>点整行选中（Label 本身不响应点击，用行内按钮铺满整行）。</summary>
        private void ClickRow(Action action)
        {
            var button = new Button
            {
                Bounds = new Rectangle(0f, _y - 24f, ContentWidth + 10f, 23f),
                Text = "",
                BackgroundColor = new Color(0f, 0f, 0f, 0f)
            };
            button.Clicked += action;
            AddChild(button);
        }

        /// <summary>
        /// 自动化验收：把检查器里已建出来的控件文字压成一行写日志。
        /// 新表单是不是真的出现在运行中的插件里，只能这样证明（截图不可靠）。
        /// </summary>
        internal void DumpUiSummary()
        {
            // 选择刚发生时重建还挂在下一帧的 Update 上；自动化命令不等下一帧，这里补一次。
            if (_pendingRebuild)
            {
                _pendingRebuild = false;
                Rebuild();
            }

            var parts = new List<string>();
            CollectUi(this, parts, 0);
            string text = string.Join(" | ", parts);
            Debug.Log("[NarrativePro] NP_INSPECTOR_UI " + text);

            // 日志文件有缓冲，验收窗口里可能还读不到；同时落一份回执文件，命令一跑就能查。
            try
            {
                string path = Path.Combine(Globals.ProjectFolder, "Saved", "NarrativeProEditorUi.txt");
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, text);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[NarrativePro] 检查器 UI 回执写入失败: " + ex.Message);
            }
        }

        private static void CollectUi(Control control, List<string> parts, int depth)
        {
            if (control == null || depth > 8 || parts.Count > 200) return;

            string text = null;
            if (control is Label label) text = label.Text;
            else if (control is Button button) text = button.Text;
            else if (control is TextBox box) text = box.Text;
            else if (control is Dropdown dropdown)
                text = "dropdown[" + dropdown.SelectedIndex + "]=" + (dropdown.SelectedItem ?? "");

            if (!string.IsNullOrWhiteSpace(text)) parts.Add(text);
            if (control is ContainerControl container)
            {
                for (int i = 0; i < container.ChildrenCount; i++) CollectUi(container.GetChild(i), parts, depth + 1);
            }
        }

        private List<string> SpeakerIds()
        {
            var ids = new List<string>();
            foreach (var speaker in _dialogue?.Speakers ?? new List<SpeakerData>())
            {
                if (!string.IsNullOrEmpty(speaker.SpeakerId)) ids.Add(speaker.SpeakerId);
            }
            return ids;
        }

        private static string StateTypeName(EStateNodeType type)
        {
            switch (type)
            {
                case EStateNodeType.Success: return "Success（成功终止）";
                case EStateNodeType.Failure: return "Failure（失败终止）";
                default: return "Regular（常规）";
            }
        }

        private static string Truncate(string text, int length)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.Length <= length ? text : text.Substring(0, length) + "…";
        }
    }
}
#endif
