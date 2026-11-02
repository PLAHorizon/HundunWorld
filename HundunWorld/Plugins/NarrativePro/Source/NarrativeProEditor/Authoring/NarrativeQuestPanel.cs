#if FLAX_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using FlaxEngine;
using FlaxEngine.GUI;
using NarrativePro.Data;
using NarrativePro.Tales.Quest;

namespace NarrativePro.EditorTools
{
    /// <summary>
    /// 可内嵌的"叙事任务面板"：把任务图、检查器、校验面板与保存/撤销/审计这一套装进一个普通 Control，
    /// 供「任务编辑器」把叙事编辑能力并进自己的窗口（基座仍是任务编辑器，DB 功能一个不少）。
    /// <para>
    /// 面板自带一套轻量会话状态（当前资产 / 撤销栈 / 脏标记），做的事与独立窗口一致，
    /// 但共用同一批 pane 与存储层——不复制任何编辑逻辑。
    /// </para>
    /// </summary>
    public sealed class NarrativeQuestPanel : ContainerControl, INarrativeEditorHost
    {
        // --- 供任务编辑器反射创建（返回 Control，避免跨插件编译依赖）---
        public static Control Create() => new NarrativeQuestPanel();

        private readonly Panel _toolbar;
        private readonly NarrativeGraphView _graph;
        private readonly NarrativeInspectorPane _inspector;
        private readonly NarrativeIssuePane _issues;
        private readonly Label _status;
        private readonly List<(Button Button, float Width)> _buttons = new List<(Button, float)>();

        private QuestData _quest;
        private string _path = "";
        private bool _dirty;
        private string _statusText = "（未载入叙事资产）";
        private NarrativeSelection _selection = NarrativeSelection.None;
        private readonly NarrativeHistory _history = new NarrativeHistory(40);
        private string _savedSnapshot = "";
        private string _emptyHint = "选中左侧任务模板后，这里显示它对应的叙事图；没有就点「生成叙事图」。";
        private int _lastLoggedUndo = -1;
        private int _lastLoggedRedo = -1;

        public NarrativeQuestPanel()
        {
            NarrativeAssetStore.WarmLogicRegistry();

            _toolbar = new Panel { BackgroundColor = NarrativeEditorTheme.Panel };
            AddChild(_toolbar);

            ToolbarButton("保存", Save);
            ToolbarButton("放弃修改", Discard);
            ToolbarButton("校验", () => Report("校验：" + CurrentAssetName + " 发现 " + ValidateNow() + " 项问题"));
            ToolbarButton("审计", Audit);
            ToolbarButton("大纲", DumpOutline);
            ToolbarButton("撤销", Undo);
            ToolbarButton("重做", Redo);
            ToolbarButton("自动布局", () =>
            {
                if (_quest == null) { Report("还没有载入叙事图"); return; }
                _graph.AutoLayout();
                Mutated("已按依赖关系自动布局（内存）。");
            });
            ToolbarButton("适配视图", () => _graph.FitToView());

            _graph = new NarrativeGraphView { EmptyHint = _emptyHint };
            _graph.SelectionChanged += OnGraphSelection;
            _graph.NodeMoved += OnNodeMoved;
            _graph.DeleteRequested += OnGraphDeleteRequested;
            _graph.ContextMenuRequested += OnGraphContextMenu;
            _graph.LinkRequested += OnGraphLinkRequested;
            AddChild(_graph);

            _inspector = new NarrativeInspectorPane(this) { BackgroundColor = NarrativeEditorTheme.Panel };
            AddChild(_inspector);

            _issues = new NarrativeIssuePane();
            _issues.IssueActivated += OnIssueActivated;
            AddChild(_issues);

            _status = new Label
            {
                Text = _statusText,
                TextColor = NarrativeEditorTheme.Text,
                VerticalAlignment = TextAlignment.Center,
                ClipText = true
            };
            AddChild(_status);

            SizeChanged += _ => Arrange();
        }

        public bool IsDirty => _dirty;
        public string LastStatus => _statusText;
        public string CurrentPath => _path;

        public string CurrentAssetName =>
            string.IsNullOrEmpty(_path) ? "(未打开)" : Path.GetFileNameWithoutExtension(_path);

        // ===================================================================
        // 对外：任务编辑器调这些（反射）
        // ===================================================================

        /// <summary>打开某条 DB 模板对应的叙事图；没有就返回 false（由调用方决定是否生成）。</summary>
        public bool OpenForTemplate(int templateId)
        {
            var paths = NarrativeAssetStore.QuestAssetsForTemplate(templateId);
            if (paths.Count > 0)
            {
                OpenQuest(paths[0]);
                return true;
            }

            ClearForTemplate(templateId);
            return false;
        }

        /// <summary>没有叙事图就一键生成骨架再打开；成功返回 null。</summary>
        public string EnsureForTemplate(int templateId)
        {
            var paths = NarrativeAssetStore.QuestAssetsForTemplate(templateId);
            if (paths.Count > 0)
            {
                OpenQuest(paths[0]);
                return null;
            }

            string error = NarrativeAssetStore.CreateQuestAssetWithTemplate(templateId, "Q" + templateId, out string path, out string _);
            if (error != null) return error;

            OpenQuest(path);
            return null;
        }

        /// <summary>模板没有叙事图时的空态文案。</summary>
        public void ClearForTemplate(int templateId)
        {
            Clear($"模板 {templateId} 还没有叙事图；点「生成叙事图」会建一个 开始→完成 的骨架并挂上模板 id。");
        }

        public void OpenQuest(string path)
        {
            try
            {
                _quest = NarrativeAssetStore.LoadQuestData(path);
                _path = NarrativeDataPaths.Resolve(path);
                _selection = NarrativeSelection.None;

                _savedSnapshot = NarrativeAssetStore.SnapshotQuest(_quest) ?? "";
                _history.OpenDocument(_savedSnapshot);

                // 打开时的自动排布不算"待保存的改动"（位置能从数据重算），别让面板一打开就是脏的。
                if (NarrativeAssetStore.IsQuestLayoutDegenerate(_quest) || NarrativeAssetStore.HasStackedQuestNodes(_quest))
                {
                    NarrativeAssetStore.LayoutQuest(_quest);
                    _savedSnapshot = NarrativeAssetStore.SnapshotQuest(_quest) ?? "";
                }

                RefreshDirty();
                _graph.ShowQuest(_quest, "叙事图 · " + CurrentAssetName, QuestSubtitle());
                PushInspector();
                ValidateNow();
                Report("已载入叙事图 " + CurrentAssetName + (_dirty ? "（内存版本未保存）" : ""));
            }
            catch (Exception ex)
            {
                Clear("载入叙事图失败：" + ex.Message);
            }
        }

        public void Save()
        {
            if (_quest == null || string.IsNullOrEmpty(_path))
            {
                Report("还没有载入叙事图，无法保存");
                return;
            }

            try
            {
                NarrativeAssetStore.WriteQuest(_path, _quest);
            }
            catch (Exception ex)
            {
                Report("保存失败：" + ex.Message);
                return;
            }

            _savedSnapshot = NarrativeAssetStore.SnapshotQuest(_quest) ?? "";
            RefreshDirty();
            int problems = ValidateNow();
            Report(problems == 0 ? "已保存叙事图 " + CurrentAssetName : "已保存，但校验发现 " + problems + " 项问题（见底部）");
        }

        /// <summary>重新从磁盘载入（放弃修改）。</summary>
        public void Discard()
        {
            if (string.IsNullOrEmpty(_path))
            {
                Report("还没有载入叙事图");
                return;
            }
            OpenQuest(_path);
        }

        public int ValidateNow()
        {
            if (_quest == null)
            {
                _issues.SetIssues(null, "尚未载入叙事图。");
                return 0;
            }

            var problems = NarrativeAssetStore.ValidateQuestData(_quest);
            var audit = NarrativeAssetStore.AuditQuest(_quest);
            audit.AddRange(NarrativeAssetStore.AuditTemplateLink(_quest, _path));

            var issues = new List<NarrativeIssue>();
            foreach (var problem in problems)
            {
                issues.Add(new NarrativeIssue { File = _path, Message = problem, NodeId = ExtractQuoted(problem), IsError = true, IsQuest = true });
            }
            foreach (var finding in audit)
            {
                issues.Add(new NarrativeIssue { File = _path, Message = finding.Message, NodeId = finding.NodeId, IsError = finding.IsError, IsQuest = true });
            }

            int errors = problems.Count;
            int warnings = audit.Count;
            foreach (var finding in audit) if (finding.IsError) { errors++; warnings--; }

            string warnNote = warnings > 0 ? "，另有 " + warnings + " 条提醒" : "";
            _issues.SetIssues(issues, errors == 0
                ? "校验通过：" + CurrentAssetName + warnNote + (_dirty ? "（内存版本，尚未写盘）" : "")
                : "校验：" + CurrentAssetName + " 发现 " + errors + " 项问题" + warnNote + "（内存版本）");
            return errors;
        }

        public void Undo()
        {
            string target = _history.Undo(NarrativeAssetStore.SnapshotQuest(_quest) ?? "");
            if (target == null) { Report("没有可撤销的修改"); return; }
            ApplySnapshot(target);
            Report($"已撤销（剩 {_history.UndoCount} 步可撤销，{_history.RedoCount} 步可重做）");
        }

        public void Redo()
        {
            string target = _history.Redo(NarrativeAssetStore.SnapshotQuest(_quest) ?? "");
            if (target == null) { Report("没有可重做的修改"); return; }
            ApplySnapshot(target);
            Report($"已重做（剩 {_history.UndoCount} 步可撤销，{_history.RedoCount} 步可重做）");
        }

        // ===================================================================
        // INarrativeEditorHost
        // ===================================================================

        public void Report(string message)
        {
            _statusText = message ?? "";
            if (_status != null) _status.Text = _statusText;
            Debug.Log("[NarrativePro][内嵌] " + _statusText);
            UpdateHistoryButtons();
        }

        public void Mutated(string message)
        {
            if (_quest == null) return;

            string current = NarrativeAssetStore.SnapshotQuest(_quest) ?? "";
            _history.Commit(current);
            RefreshDirty();
            _graph.RefreshNodes();
            _graph.SetSubtitle(QuestSubtitle());
            PushInspector();
            int problems = ValidateNow();
            Report(message + (problems > 0 ? $" · 校验发现 {problems} 项问题" : "") + "（Ctrl+Z 撤销）");
        }

        void INarrativeEditorHost.Mutated(string message) => Mutated(message);

        public void SelectNode(NarrativeSelectionKind kind, string id) => _graph.SelectNode(kind, id);

        void INarrativeEditorHost.SelectNode(NarrativeSelectionKind kind, string id) => SelectNode(kind, id);

        public void ClearSelection()
        {
            _selection = NarrativeSelection.None;
            _graph.Select(NarrativeSelection.None);
            PushInspector();
        }

        void INarrativeEditorHost.ClearSelection() => ClearSelection();

        /// <summary>任务头里的"关联对话"：对话编辑在独立窗口里做，这里把它带过去。</summary>
        public void ShowDialogue(string path)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            bool opened = NarrativeProAuthoringEditorPlugin.OpenAssetByName(name);
            Report(opened ? "已在「叙事资产」窗口打开对话 " + name : "对话 " + name + " 未能打开（独立窗口里可能有未保存修改）");
        }

        public string RenameCurrentAsset(string newName)
        {
            if (_quest == null) return "没有打开的叙事资产";
            if (_dirty) return "有未保存的修改：先保存或放弃，再重命名";

            string error = NarrativeAssetStore.RenameQuestAsset(_path, newName, out string newPath);
            if (error != null) return error;

            OpenQuest(newPath);
            return null;
        }

        public string DuplicateCurrentAsset(string newName)
        {
            if (_quest == null) return "没有打开的叙事资产";
            if (_dirty) return "有未保存的修改：先保存或放弃，再复制";
            return NarrativeAssetStore.DuplicateQuestAsset(_path, newName, out string _);
        }

        public string DeleteCurrentAsset()
        {
            if (_quest == null) return "没有打开的叙事资产";
            if (_dirty) return "有未保存的修改：先保存或放弃，再删除";

            string name = CurrentAssetName;
            string error = NarrativeAssetStore.DeleteAssetFile(_path);
            if (error != null) return error;

            Clear("已删除叙事图 " + name);
            return null;
        }

        // ===================================================================
        // 会话内部
        // ===================================================================

        private void Clear(string hint)
        {
            _quest = null;
            _path = "";
            _dirty = false;
            _selection = NarrativeSelection.None;
            _history.OpenDocument("");
            _savedSnapshot = "";
            _emptyHint = hint ?? "";
            _graph.Clear();
            _graph.EmptyHint = _emptyHint;
            _issues.SetIssues(null, "尚未载入叙事图。");
            PushInspector();
            Report(hint ?? "");
        }

        private void RefreshDirty()
        {
            _dirty = _quest != null && (NarrativeAssetStore.SnapshotQuest(_quest) ?? "") != _savedSnapshot;
        }

        private void ApplySnapshot(string snapshot)
        {
            _quest = NarrativeAssetStore.RestoreQuest(snapshot);
            if (_quest == null) { Clear("还原失败：快照为空"); return; }
            _graph.RebindQuest(_quest, QuestSubtitle());
            RefreshDirty();
            PruneSelection();
            PushInspector();
            ValidateNow();
        }

        private void PruneSelection()
        {
            if (_selection.IsNone) return;

            bool exists = _selection.Kind switch
            {
                NarrativeSelectionKind.QuestState => NarrativeAssetStore.FindQuestState(_quest, _selection.Id) != null,
                NarrativeSelectionKind.QuestBranch => NarrativeAssetStore.FindQuestBranch(_quest, _selection.Id) != null,
                _ => false
            };
            if (!exists) ClearSelection();
        }

        private string QuestSubtitle()
        {
            if (_quest == null) return "";
            string link = NarrativeAssetStore.QuestLinkLabel(_quest);
            return $"{_quest.States?.Count ?? 0} 状态 · {_quest.Branches?.Count ?? 0} 分支 · 起始 {_quest.StartStateId}" +
                   (string.IsNullOrEmpty(link) ? "" : " · " + link);
        }

        private void PushInspector()
        {
            _inspector.SetContext(true, _quest, null, _selection);
        }

        private void OnGraphSelection(NarrativeSelection selection)
        {
            _selection = selection;
            PushInspector();
            if (!selection.IsNone)
            {
                string kind = selection.Kind == NarrativeSelectionKind.QuestBranch ? "分支" : "状态";
                Report("已选中" + kind + " " + selection.Id);
            }
        }

        private void OnNodeMoved()
        {
            string current = NarrativeAssetStore.SnapshotQuest(_quest) ?? "";
            _history.Commit(current);
            RefreshDirty();
            Report(_dirty ? "节点位置已更新（内存），点「保存」写回" : "节点位置已回到磁盘版本");
        }

        private void OnGraphDeleteRequested()
        {
            if (_selection.IsNone) { Report("先在图上点一个节点，再按 Delete"); return; }
            if (!_inspector.DeleteSelected()) Report("选中项当前不可删除");
        }

        private void OnGraphLinkRequested(NarrativeSelection from, NarrativeSelection to)
        {
            if (_quest == null) return;
            if (from.Kind != NarrativeSelectionKind.QuestState || to.Kind != NarrativeSelectionKind.QuestState)
            {
                Report("任务图里只能从状态连到状态");
                return;
            }

            string error = NarrativeAssetStore.AddQuestBranchConnected(_quest, from.Id, to.Id, out string branchId);
            if (error != null) { Report("操作被拒绝：" + error); return; }

            Mutated($"已用分支 {branchId} 连线 {from.Id} → {to.Id}（内存）。");
            SelectNode(NarrativeSelectionKind.QuestBranch, branchId);
        }

        private void OnIssueActivated(NarrativeIssue issue)
        {
            if (string.IsNullOrEmpty(issue.NodeId)) return;
            if (NarrativeAssetStore.FindQuestState(_quest, issue.NodeId) != null)
                SelectNode(NarrativeSelectionKind.QuestState, issue.NodeId);
            else if (NarrativeAssetStore.FindQuestBranch(_quest, issue.NodeId) != null)
                SelectNode(NarrativeSelectionKind.QuestBranch, issue.NodeId);
        }

        private void Audit()
        {
            if (_quest == null) { Report("还没有载入叙事图"); return; }

            var findings = NarrativeAssetStore.AuditQuest(_quest);
            findings.AddRange(NarrativeAssetStore.AuditTemplateLink(_quest, _path));

            int errors = 0;
            foreach (var finding in findings) if (finding.IsError) errors++;

            var issues = new List<NarrativeIssue>();
            foreach (var finding in findings)
                issues.Add(new NarrativeIssue { File = _path, Message = finding.Message, NodeId = finding.NodeId, IsError = finding.IsError, IsQuest = true });

            _issues.SetIssues(issues, errors == 0
                ? "引用审计通过：" + CurrentAssetName + (findings.Count > 0 ? $"（{findings.Count} 条提醒）" : "")
                : $"引用审计：{CurrentAssetName} {errors} 项错误、{findings.Count - errors} 条提醒");
            Report($"引用审计：{errors} 项错误、{findings.Count - errors} 条提醒（见底部）");
        }

        private void DumpOutline()
        {
            if (_quest == null) { Report("还没有载入叙事图"); return; }
            Debug.Log("[NarrativePro] 大纲 " + CurrentAssetName + "\n" + NarrativeAssetStore.DescribeQuestOutline(_quest));
            Report("大纲已写入日志");
        }

        private void OnGraphContextMenu(NarrativeSelection selection, Float2 at)
        {
            if (_quest == null) return;

            var menu = new FlaxEditor.GUI.ContextMenu.ContextMenu();
            if (selection.IsNone)
            {
                menu.AddButton("自动布局", () => { _graph.AutoLayout(); Mutated("自动布局（内存）。"); });
                menu.AddButton("适配视图", () => _graph.FitToView());
                menu.AddSeparator();
                menu.AddButton("撤销", Undo);
                menu.AddButton("重做", Redo);
                menu.AddButton("保存", Save);
            }
            else if (selection.Kind == NarrativeSelectionKind.QuestState)
            {
                menu.AddButton("新建状态并连接", () => _inspector.ConnectNewNodeAfterSelected());
                menu.AddButton("设为起始状态", () => _inspector.SetStartOrRoot());
                menu.AddSeparator();
                menu.AddButton("复制状态", () => _inspector.CopySelected());
                menu.AddButton("删除状态", () => _inspector.DeleteSelected());
            }
            else if (selection.Kind == NarrativeSelectionKind.QuestBranch)
            {
                menu.AddButton("反转方向", () => _inspector.FlipSelectedBranch());
                menu.AddSeparator();
                menu.AddButton("复制分支", () => _inspector.CopySelected());
                menu.AddButton("删除分支", () => _inspector.DeleteSelected());
            }
            else
            {
                return;
            }

            menu.Show(_graph, at, null);
        }

        // ===================================================================
        // 布局（任务编辑器里由外部调 Arrange）
        // ===================================================================

        private void ToolbarButton(string text, Action action)
        {
            float width = Math.Max(58f, NarrativeEditorTheme.Measure(text, NarrativeEditorTheme.FontSmall) + 18f);
            var button = new Button { Text = text };
            button.Clicked += action;
            _toolbar.AddChild(button);
            _buttons.Add((button, width));
        }

        private void UpdateHistoryButtons()
        {
            int undoIndex = -1, redoIndex = -1;
            for (int i = 0; i < _buttons.Count; i++)
            {
                if (_buttons[i].Button.Text == "撤销") undoIndex = i;
                if (_buttons[i].Button.Text == "重做") redoIndex = i;
            }

            if (undoIndex >= 0)
            {
                _buttons[undoIndex].Button.Enabled = _history.UndoCount > 0;
                _buttons[undoIndex].Button.TooltipText = _history.UndoCount > 0 ? $"可撤销 {_history.UndoCount} 步" : "没有可撤销的修改";
            }
            if (redoIndex >= 0)
            {
                _buttons[redoIndex].Button.Enabled = _history.RedoCount > 0;
                _buttons[redoIndex].Button.TooltipText = _history.RedoCount > 0 ? $"可重做 {_history.RedoCount} 步" : "没有可重做的修改";
            }

            if (_history.UndoCount != _lastLoggedUndo || _history.RedoCount != _lastLoggedRedo)
            {
                _lastLoggedUndo = _history.UndoCount;
                _lastLoggedRedo = _history.RedoCount;
                Debug.Log($"[NarrativePro][内嵌] NP_PANEL_HISTORY undo={_history.UndoCount} redo={_history.RedoCount}");
            }
        }

        public void Arrange()
        {
            float width = Width;
            float height = Height;
            if (width < 200f || height < 160f) return;

            const float statusHeight = 22f;
            const float issuesHeight = 96f;
            float inspectorWidth = Mathf.Clamp(width * 0.32f, 210f, 320f);
            float toolbarRows = (float)Math.Max(1.0, Math.Ceiling(_buttons.Count * 66.0 / Math.Max(1.0, width - 16.0)));
            float toolbarHeight = Math.Max(32f, toolbarRows * 30f + 6f);

            _toolbar.Bounds = new Rectangle(0f, 0f, width, toolbarHeight);

            float x = 6f, y = 5f;
            for (int i = 0; i < _buttons.Count; i++)
            {
                var (button, buttonWidth) = _buttons[i];
                if (x + buttonWidth > width - 6f) { x = 6f; y += 30f; }
                button.Bounds = new Rectangle(x, y, buttonWidth, 24f);
                x += buttonWidth + 6f;
            }

            float contentTop = toolbarHeight;
            float contentBottom = height - statusHeight;
            _graph.Bounds = new Rectangle(0f, contentTop, width - inspectorWidth, contentBottom - contentTop);
            _inspector.Bounds = new Rectangle(width - inspectorWidth, contentTop, inspectorWidth, contentBottom - contentTop - issuesHeight);
            _issues.Bounds = new Rectangle(width - inspectorWidth, contentBottom - issuesHeight, inspectorWidth, issuesHeight);
            _status.Bounds = new Rectangle(8f, contentBottom + 2f, width - 16f, statusHeight - 4f);

            _inspector.Relayout();
            _graph.ConsumePendingFit();
        }

        private static string ExtractQuoted(string message)
        {
            if (string.IsNullOrEmpty(message)) return "";
            int start = message.IndexOf('\'');
            if (start < 0) return "";
            int end = message.IndexOf('\'', start + 1);
            return end > start ? message.Substring(start + 1, end - start - 1) : "";
        }
    }
}
#endif
