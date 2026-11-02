#if FLAX_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using FlaxEngine;
using FlaxEngine.GUI;
using NarrativePro.Data;
using NarrativePro.Tales.Dialogue;
using NarrativePro.Tales.Quest;

namespace NarrativePro.EditorTools
{
    /// <summary>
    /// NarrativePro 叙事资产编辑器（对应 UE5 NarrativePro 的 Quest / Dialogue 编辑器）。
    /// <para>
    /// 窗口分四块：左侧资产浏览器（搜索 + 分组）、中间任务/对话图（节点可拖、可缩放、位置写回资产）、
    /// 右侧检查器（属性与事件/条件/任务/连线结构编辑）、底部校验面板（逐条问题，点条目定位节点）。
    /// </para>
    /// <para>
    /// 所有读写都过 <see cref="NarrativeAssetStore"/>：写的是运行时同款 DTO，校验调的是运行时同款加载器；
    /// 保存前会先用"内存校验"跑一遍（临时文件 + 运行时装载器），保证落盘的东西运行时读得懂。
    /// </para>
    /// </summary>
    public class NarrativeProAuthoringWindow : FlaxEditor.Windows.EditorWindow, INarrativeEditorHost
    {
        private enum EditorMode
        {
            None,
            Quest,
            Dialogue
        }

        private const float Pad = 8f;
        private const float Gap = 6f;
        private const float StatusHeight = 26f;

        // --- 常驻控件 ---
        private Panel _toolbar;
        private NarrativeAssetBrowserPane _browser;
        private NarrativeGraphView _graph;
        private NarrativeInspectorPane _inspector;
        private NarrativeIssuePane _issues;
        private Panel _statusBar;
        private Label _statusLabel;
        private Label _statusInfo;

        // --- 文档状态 ---
        private EditorMode _mode = EditorMode.None;
        private QuestData _questData;
        private DialogueData _dialogueData;
        private string _currentPath = "";
        private bool _dirty;
        private string _status = "就绪：从左侧选择资产，或点「新建任务 / 新建对话」。";
        private NarrativeSelection _selection = NarrativeSelection.None;
        private FlaxEditor.GUI.ContextMenu.ContextMenu _openMenu;
        private int _lastLoggedUndo = -1;
        private int _lastLoggedRedo = -1;
        private readonly Dictionary<string, int> _assetProblems = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        // --- 撤销/重做：存的是内存快照串（与写盘同口径），绝不落盘 ---
        private readonly NarrativeHistory _history = new NarrativeHistory(40);
        private string _savedSnapshot = "";

        public NarrativeProAuthoringWindow(FlaxEditor.Editor editor)
            : base(editor, true, ScrollBars.None)
        {
            Title = "NarrativePro 叙事资产";
            NarrativeAssetStore.WarmLogicRegistry();
            try
            {
                NarrativeAssetStore.EnsureDirectories();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[NarrativePro] 叙事数据目录创建失败：{ex.Message}");
            }

            BuildChrome();
            RefreshAssets();
            PushInspector();
            LayoutChrome();
        }

        // --- INarrativeEditorHost：把窗口已有的内部动作以接口形式暴露给面板（保持公开面不扩大） ---
        string INarrativeEditorHost.CurrentAssetName => CurrentAssetName;
        void INarrativeEditorHost.Report(string message) => Report(message);
        void INarrativeEditorHost.Mutated(string message) => Mutated(message);
        void INarrativeEditorHost.SelectNode(NarrativeSelectionKind kind, string id) => SelectNode(kind, id);
        void INarrativeEditorHost.ClearSelection() => ClearSelection();
        string INarrativeEditorHost.RenameCurrentAsset(string newName) => RenameCurrentAsset(newName);
        string INarrativeEditorHost.DuplicateCurrentAsset(string newName) => DuplicateCurrentAsset(newName);
        string INarrativeEditorHost.DeleteCurrentAsset() => DeleteCurrentAsset();

        /// <summary>当前正在编辑的资产文件绝对路径（供菜单命令回显）。</summary>
        public string CurrentPath => _currentPath;

        /// <summary>最近一次状态行（跨插件回显用）。</summary>
        public string CurrentStatus => _status;

        /// <summary>首次打开时窗口是在编辑器的加载阶段创建的，那时 MasterPanel 尺寸还没定下来。</summary>
        public bool NeedsInitialPlacement { get; private set; } = true;

        /// <summary>
        /// 在编辑器布局稳定后把浮动窗口铺到编辑器可用区域（ClientSize/ClientPosition 都是逻辑像素）。
        /// 只做一次；之后用户的拖拽/停靠/缩放都保持原样。
        /// </summary>
        public void EnsureFloatingRect()
        {
            var floatPanel = ParentDockPanel as FlaxEditor.GUI.Docking.FloatWindowDockPanel;
            var native = floatPanel?.Window?.Window;
            var master = Editor?.UI?.MasterPanel;
            if (native == null || master == null || master.Width < 200f || master.Height < 120f) return;

            // ClientSize 用的是逻辑像素：再乘一次 DpiScale 会把窗口撑到屏幕外
            // （实测 2048 宽、1.25 缩放的屏上，要 2024 实际渲染出约 2530，右侧检查器整块看不见）。
            float width = master.Width - 48f;
            float height = master.Height - 48f;
            native.ClientSize = new Float2(width, height);
            native.ClientPosition = new Float2(24f, 28f);
            NeedsInitialPlacement = false;
            if (_graph != null && _graph.HasContent) _graph.RequestFit();

            // 回读：单位口径靠猜会反复错，所以把设定值、引擎读回来的值与 DpiScale 一起写日志。
            var got = native.ClientSize;
            Debug.Log($"[NarrativePro] NP_RECT master={master.Width:0}x{master.Height:0} dpi={native.DpiScale:0.##} " +
                      $"want={width:0}x{height:0} client={got.X:0}x{got.Y:0}");
        }

        internal string CurrentAssetName =>
            string.IsNullOrEmpty(_currentPath) ? "(未打开)" : Path.GetFileNameWithoutExtension(_currentPath);

        // ===================================================================
        // 对外入口（菜单插件调用）
        // ===================================================================

        public void ShowQuest(string path)
        {
            if (!ConfirmDiscard()) return;
            LoadQuest(path);
        }

        public void ShowDialogue(string path)
        {
            if (!ConfirmDiscard()) return;
            LoadDialogue(path);
        }

        /// <summary>自动化验收命令：直接调用窗口自己的按钮动作，并把结果写日志（不经过鼠标）。</summary>
        internal void RunAutomationCommand(string command)
        {
            switch ((command ?? "").Trim().ToLowerInvariant())
            {
                case "validate":
                    ValidateCurrent();
                    break;
                case "validateall":
                    ValidateAll();
                    break;
                case "layout":
                    if (_mode != EditorMode.None)
                    {
                        _graph.AutoLayout();
                        Mutated("自动布局（自动化验收）");
                    }
                    break;
                case "undo":
                    Undo();
                    break;
                case "redo":
                    Redo();
                    break;
                case "discard":
                    ReloadCurrent();
                    break;
                case "delete":
                    OnGraphDeleteRequested();
                    break;
                case "menu":
                    OnGraphContextMenu(NarrativeSelection.None, new Float2(120f, 120f));
                    break;
                case "menuhide":
                    CloseContextMenu();
                    Report("已关掉右键菜单");
                    break;
                case "outline":
                    DumpOutline();
                    break;
                case "ui":
                    if (_inspector != null) _inspector.DumpUiSummary();
                    else Report("检查器尚未创建");
                    break;
                case "data":
                    DumpDataSummary();
                    break;
                case "graph":
                    if (_graph != null)
                    {
                        _graph.DumpNodes(System.IO.Path.Combine(FlaxEngine.Globals.ProjectFolder, "Saved", "NarrativeProEditorGraph.txt"));
                        Report("图节点已写入 Saved/NarrativeProEditorGraph.txt");
                    }
                    else
                    {
                        Report("图视图尚未创建");
                    }
                    break;
                case "openquesteditor":
                    OpenQuestEditor();
                    break;
                case "catalog":
                {
                    var catalog = QuestEditorLink.TryGetCatalog(out string catalogError);
                    if (catalog == null)
                    {
                        Report("任务模板目录未取到：" + catalogError);
                    }
                    else
                    {
                        var sample = new List<string>();
                        for (int i = 0; i < catalog.Count && i < 6; i++)
                        {
                            var row = catalog[i];
                            sample.Add(row.Id + (string.IsNullOrEmpty(row.Name) ? "" : " " + row.Name) +
                                       (row.PublishState == 1 ? "·已发布" : "·草稿") +
                                       (row.Valid ? "" : "·作废"));
                        }
                        Report($"任务模板目录可用，共 {catalog.Count} 条：" + string.Join("，", sample) +
                               (catalog.Count > 6 ? " …" : ""));
                    }
                    break;
                }
                case "audit":
                {
                    if (_mode == EditorMode.None)
                    {
                        Report("没有打开的资产，无法审计");
                        break;
                    }

                    var findings = _mode == EditorMode.Quest
                        ? NarrativeAssetStore.AuditQuest(_questData)
                        : NarrativeAssetStore.AuditDialogue(_dialogueData);

                    // 与任务编辑器目录交叉核对模板 id。CLI 不可用时必须把"这次没查"写出来，不能当成"没有这条模板"。
                    int templateId = _mode == EditorMode.Quest ? (_questData?.QuestTemplateId ?? 0) : 0;
                    if (templateId > 0)
                    {
                        var catalog = QuestEditorLink.TryGetCatalog(out string catalogError);
                        if (catalog == null)
                        {
                            findings.Add(new NarrativeAssetStore.NarrativeAuditIssue
                            {
                                IsError = false,
                                Message = "模板 " + templateId + " 未能对照任务编辑器目录（" + catalogError + "）。"
                            });
                        }
                        else
                        {
                            var row = catalog.Find(item => item.Id == templateId);
                            string label = row != null && !string.IsNullOrEmpty(row.Name) ? "（" + row.Name + "）" : "";
                            string status = NarrativeAssetStore.DescribeTemplateLinkStatus(
                                templateId, true, row != null, row?.Valid ?? false, row?.PublishState ?? 0);

                            if (row == null)
                            {
                                findings.Add(new NarrativeAssetStore.NarrativeAuditIssue
                                {
                                    Message = "模板 " + templateId + " 在任务编辑器目录里不存在（目录共 " + catalog.Count + " 条）。"
                                });
                            }
                            else if (!row.Valid)
                            {
                                findings.Add(new NarrativeAssetStore.NarrativeAuditIssue
                                {
                                    Message = "模板 " + templateId + label + " 已作废。"
                                });
                            }
                            else if (row.PublishState != 1)
                            {
                                findings.Add(new NarrativeAssetStore.NarrativeAuditIssue
                                {
                                    IsError = false,
                                    Message = "模板 " + templateId + label + " 当前" + status + "：玩家那边还看不到这条任务。"
                                });
                            }
                            else
                            {
                                Debug.Log($"[NarrativePro] NP_TEMPLATE {templateId} {row.Name} {status}");
                            }
                        }
                    }

                    int auditErrors = 0;
                    foreach (var finding in findings) if (finding.IsError) auditErrors++;

                    var auditIssues = new List<NarrativeIssue>();
                    AppendAudit(auditIssues, _currentPath, _mode == EditorMode.Quest, findings);
                    _issues.SetIssues(auditIssues, auditErrors == 0
                        ? $"引用审计通过：{CurrentAssetName}" + (findings.Count > 0 ? $"（{findings.Count} 条提醒）" : "")
                        : $"引用审计：{CurrentAssetName} {auditErrors} 项错误、{findings.Count - auditErrors} 条提醒");

                    Debug.Log($"[NarrativePro] NP_AUDIT {CurrentAssetName} errors={auditErrors} warnings={findings.Count - auditErrors}");
                    Report($"引用审计：{auditErrors} 项错误、{findings.Count - auditErrors} 条提醒（见底部校验面板）");
                    break;
                }
                default:
                    if (HandleSelectCommand(command)) break;
                    if (HandleMenuCommand(command)) break;
                    if (HandleActionCommand(command)) break;
                    if (HandleFileOpCommand(command)) break;
                    if (HandleSearchCommand(command)) break;
                    if (HandleLinkCommand(command)) break;
                    if (HandleNarrativeLinkProbe(command)) break;
                    if (HandleFusionProbe(command)) break;
                    if (HandleQuestActProbe(command)) break;
                    Report("未知的自动化命令：" + command);
                    break;
            }

            Debug.Log("[NarrativePro] NP_CMD " + command + " -> " + _status);
        }

        /// <summary>
        /// 支持 act:connect / act:copy / act:flip / act:start / act:delete——跑右键菜单里那几条动作本身，
        /// 这样菜单项的实现不用真鼠标也能在编辑器里走一遍。
        /// </summary>
        private bool HandleActionCommand(string command)
        {
            const string prefix = "act:";
            if (!command.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;

            if (_selection.IsNone)
            {
                Report("先选中一个节点再执行 act 命令");
                return true;
            }

            string verb = command.Substring(prefix.Length).Trim();

            // act:link:<目标id> —— 等价于 Shift+拖到那个节点
            int colon = verb.IndexOf(':');
            if (colon > 0 && verb.Substring(0, colon).Equals("link", StringComparison.OrdinalIgnoreCase))
            {
                string targetId = verb.Substring(colon + 1).Trim();
                if (string.IsNullOrEmpty(targetId))
                {
                    Report("act:link 需要目标节点 id");
                    return true;
                }
                var to = ResolveSelectionById(targetId);
                if (to.IsNone)
                {
                    Report("连线目标不存在：" + targetId);
                    return true;
                }
                OnGraphLinkRequested(_selection, to);
                return true;
            }

            switch (verb.ToLowerInvariant())
            {
                case "connect": _inspector.ConnectNewNodeAfterSelected(); return true;
                case "copy": _inspector.CopySelected(); return true;
                case "flip": _inspector.FlipSelectedBranch(); return true;
                case "start": _inspector.SetStartOrRoot(); return true;
                case "delete": _inspector.DeleteSelected(); return true;
                default: Report("未知的 act 命令：" + command); return true;
            }
        }

        /// <summary>支持 fileop:rename:&lt;新名&gt; / fileop:duplicate:&lt;新名&gt; / fileop:delete（检查器按钮的同一条实现）。</summary>
        private bool HandleFileOpCommand(string command)
        {
            const string prefix = "fileop:";
            if (!command.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;

            string rest = command.Substring(prefix.Length);
            int colon = rest.IndexOf(':');
            string verb = colon < 0 ? rest : rest.Substring(0, colon);
            string arg = colon < 0 ? "" : rest.Substring(colon + 1).Trim();

            switch (verb.Trim().ToLowerInvariant())
            {
                case "rename":
                {
                    string error = RenameCurrentAsset(arg);
                    Report(error == null ? "已重命名资产为 " + arg : "操作被拒绝：" + error);
                    return true;
                }
                case "duplicate":
                {
                    string error = DuplicateCurrentAsset(arg);
                    Report(error == null ? "已复制资产为 " + arg : "操作被拒绝：" + error);
                    return true;
                }
                case "delete":
                {
                    // 必须点名要删哪个资产：这条命令只用于自动化，不带名字就等于"删当前打开的"，
                    // 一旦上游打开的资产不是预期的那条，就把别的资产删了（实测踩过）。
                    if (string.IsNullOrEmpty(arg))
                    {
                        Report("fileop:delete 需要点名资产：fileop:delete:<资产名>");
                        return true;
                    }
                    if (!string.Equals(arg, CurrentAssetName, StringComparison.OrdinalIgnoreCase))
                    {
                        Report($"删除目标与当前资产不一致：参数 {arg} ≠ 当前 {CurrentAssetName}，已拒绝");
                        return true;
                    }

                    string error = DeleteCurrentAsset();
                    if (error != null) Report("操作被拒绝：" + error);
                    return true;
                }
                default:
                    Report("未知的 fileop 命令：" + command);
                    return true;
            }
        }

        /// <summary>支持 menu:state:&lt;id&gt; / menu:branch:… / menu:npc:… / menu:player:… / menu:speaker:…。</summary>
        private bool HandleMenuCommand(string command)
        {
            const string prefix = "menu:";
            if (!command.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;

            string rest = command.Substring(prefix.Length);
            int separator = rest.IndexOf(':');
            if (separator <= 0) return false;

            if (!TryParseKind(rest.Substring(0, separator), out var kind)) return false;
            string id = rest.Substring(separator + 1).Trim();
            if (string.IsNullOrEmpty(id)) return false;

            // 与真实右键一致：右键会先选中再弹菜单
            _graph.SelectNode(kind, id);
            OnGraphContextMenu(_selection, new Float2(160f, 140f));
            return true;
        }

        private static bool TryParseKind(string text, out NarrativeSelectionKind kind)
        {
            switch ((text ?? "").Trim().ToLowerInvariant())
            {
                case "state": kind = NarrativeSelectionKind.QuestState; return true;
                case "branch": kind = NarrativeSelectionKind.QuestBranch; return true;
                case "npc": kind = NarrativeSelectionKind.DialogueNpc; return true;
                case "player": kind = NarrativeSelectionKind.DialoguePlayer; return true;
                case "speaker": kind = NarrativeSelectionKind.Speaker; return true;
                default: kind = NarrativeSelectionKind.None; return false;
            }
        }

        /// <summary>支持 select:state:&lt;id&gt; / select:branch / select:npc / select:player / select:speaker。</summary>
        private bool HandleSelectCommand(string command)
        {
            const string prefix = "select:";
            if (!command.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;

            string rest = command.Substring(prefix.Length);
            int separator = rest.IndexOf(':');
            if (separator <= 0) return false;

            if (!TryParseKind(rest.Substring(0, separator), out var kind)) return false;
            string id = rest.Substring(separator + 1).Trim();
            if (string.IsNullOrEmpty(id)) return false;

            SelectNode(kind, id);
            return true;
        }

        /// <summary>按资产名打开（先当任务找，再当对话找）；自动化验收与菜单回显共用。</summary>
        public void TryOpenAsset(string assetName)
        {
            if (string.IsNullOrWhiteSpace(assetName)) return;

            string questPath = NarrativeAssetStore.QuestPathFor(assetName);
            if (File.Exists(questPath))
            {
                ShowQuest(questPath);
                return;
            }

            string dialoguePath = NarrativeAssetStore.DialoguePathFor(assetName);
            if (File.Exists(dialoguePath))
            {
                ShowDialogue(dialoguePath);
                return;
            }

            Report("找不到名为 " + assetName + " 的叙事资产");
        }

        /// <summary>刷新左侧资产列表与工具栏回显。</summary>
        public void RefreshAssets()
        {
            var quests = new List<NarrativeAssetEntry>();
            foreach (var path in NarrativeAssetStore.ListJsonFiles(NarrativeAssetStore.QuestsDirectory))
            {
                quests.Add(new NarrativeAssetEntry
                {
                    Path = path,
                    IsQuest = true,
                    Name = Path.GetFileNameWithoutExtension(path),
                    Subtitle = DescribeQuestAsset(path)
                });
            }

            var dialogues = new List<NarrativeAssetEntry>();
            foreach (var path in NarrativeAssetStore.ListJsonFiles(NarrativeAssetStore.DialoguesDirectory))
            {
                dialogues.Add(new NarrativeAssetEntry
                {
                    Path = path,
                    IsQuest = false,
                    Name = Path.GetFileNameWithoutExtension(path),
                    Subtitle = DescribeDialogueAsset(path)
                });
            }

            _browser.SetAssets(quests, dialogues);
            _browser.SetProblems(_assetProblems);
            _browser.SetCurrent(_currentPath);
            UpdateInfo();
        }

        /// <summary>新建任务模板并落盘、校验、在窗口里打开（文件名即资产 id：运行时按文件名索引 Quest）。</summary>
        public void NewQuest(string questId)
        {
            if (!ConfirmDiscard()) return;

            string path;
            try
            {
                NarrativeAssetStore.CreateQuestTemplate(questId, out path);
            }
            catch (Exception ex)
            {
                Report($"新建任务失败：{ex.Message}");
                return;
            }

            RefreshAssets();
            LoadQuest(path);
            ValidateCurrent();
        }

        public void NewDialogue(string dialogueId)
        {
            if (!ConfirmDiscard()) return;

            string path;
            try
            {
                NarrativeAssetStore.CreateDialogueTemplate(dialogueId, out path);
            }
            catch (Exception ex)
            {
                Report($"新建对话失败：{ex.Message}");
                return;
            }

            RefreshAssets();
            LoadDialogue(path);
            ValidateCurrent();
        }

        // ===================================================================
        // 打开 / 保存 / 放弃
        // ===================================================================

        private void LoadQuest(string path)
        {
            try
            {
                _questData = NarrativeAssetStore.LoadQuestData(path);
                _dialogueData = null;
                _mode = EditorMode.Quest;
                _currentPath = NarrativeDataPaths.Resolve(path);
                _selection = NarrativeSelection.None;
                OpenDocument();

                string hint = "已打开任务 " + CurrentAssetName;
                if (NarrativeAssetStore.IsQuestLayoutDegenerate(_questData) || NarrativeAssetStore.HasStackedQuestNodes(_questData))
                {
                    NarrativeAssetStore.LayoutQuest(_questData);
                    hint += "（节点位置重叠，已自动排布，保存后写回）";
                    // 自动排布不算"待保存的改动"：位置能从数据重算，标脏只会让重叠资产一打开就显示未保存，
                    // 还会把资产切换与自动化打开全挡在脏检查后面。要写回位置就点保存（那时才与磁盘不同）。
                    _savedSnapshot = SnapshotCurrent() ?? "";
                }

                AfterDocumentChanged();
                ResetBaseline();
                _graph.ShowQuest(_questData, "任务 · " + CurrentAssetName,
                    QuestSubtitle(_questData));
                _browser.SetCurrent(_currentPath);
                PushInspector();
                int problems = ValidateCurrentQuiet();
                Report(hint + (problems == 0 ? " · 校验通过" : $" · 校验发现 {problems} 项问题（见底部）"));
            }
            catch (Exception ex)
            {
                ResetDocument();
                Report($"打开任务失败：{ex.Message}");
            }
        }

        private void LoadDialogue(string path)
        {
            try
            {
                _dialogueData = NarrativeAssetStore.LoadDialogueData(path);
                _questData = null;
                _mode = EditorMode.Dialogue;
                _currentPath = NarrativeDataPaths.Resolve(path);
                _selection = NarrativeSelection.None;
                OpenDocument();

                string hint = "已打开对话 " + CurrentAssetName;
                if (NarrativeAssetStore.IsDialogueLayoutDegenerate(_dialogueData) || NarrativeAssetStore.HasStackedDialogueNodes(_dialogueData))
                {
                    NarrativeAssetStore.LayoutDialogue(_dialogueData);
                    hint += "（节点位置重叠，已自动排布，保存后写回）";
                    _savedSnapshot = SnapshotCurrent() ?? "";
                }

                AfterDocumentChanged();
                ResetBaseline();
                _graph.ShowDialogue(_dialogueData, "对话 · " + CurrentAssetName,
                    DialogueSubtitle(_dialogueData));
                _browser.SetCurrent(_currentPath);
                PushInspector();
                int problems = ValidateCurrentQuiet();
                Report(hint + (problems == 0 ? " · 校验通过" : $" · 校验发现 {problems} 项问题（见底部）"));
            }
            catch (Exception ex)
            {
                ResetDocument();
                Report($"打开对话失败：{ex.Message}");
            }
        }

        private void ResetDocument()
        {
            _mode = EditorMode.None;
            _questData = null;
            _dialogueData = null;
            _currentPath = "";
            _dirty = false;
            _selection = NarrativeSelection.None;
            _history.OpenDocument("");
            _savedSnapshot = "";
            _graph.Clear();
            _browser.SetCurrent("");
            PushInspector();
            UpdateTitle();
            UpdateHistoryButtons();
        }

        // ===================================================================
        // 资产级文件操作（重命名 / 复制 / 删除）
        // ===================================================================

        /// <summary>文件操作动的是磁盘版本，所以要求内存与磁盘一致（脏文档一律先拒绝）。</summary>
        private string GuardCleanForFileOp(string opName)
        {
            if (_mode == EditorMode.None) return "没有打开的资产，无法" + opName;
            if (_dirty) return $"有未保存的修改：先保存（Ctrl+S）或放弃，再{opName}";
            return null;
        }

        /// <summary>重命名当前资产（文件名 + 内部 id 一起改），成功后直接打开新文件。</summary>
        internal string RenameCurrentAsset(string newName)
        {
            string guard = GuardCleanForFileOp("重命名");
            if (guard != null) return guard;

            bool isQuest = _mode == EditorMode.Quest;
            string error = isQuest
                ? NarrativeAssetStore.RenameQuestAsset(_currentPath, newName, out string newPath)
                : NarrativeAssetStore.RenameDialogueAsset(_currentPath, newName, out newPath);
            if (error != null) return error;

            RefreshAssets();
            if (isQuest) LoadQuest(newPath);
            else LoadDialogue(newPath);
            return null;
        }

        /// <summary>复制当前资产；当前编辑的仍是原资产。</summary>
        internal string DuplicateCurrentAsset(string newName)
        {
            string guard = GuardCleanForFileOp("复制资产");
            if (guard != null) return guard;

            string error = _mode == EditorMode.Quest
                ? NarrativeAssetStore.DuplicateQuestAsset(_currentPath, newName, out string newPath)
                : NarrativeAssetStore.DuplicateDialogueAsset(_currentPath, newName, out newPath);
            if (error != null) return error;

            RefreshAssets();
            return null;
        }

        /// <summary>删除当前资产文件（检查器里两步确认过才进得来）。</summary>
        internal string DeleteCurrentAsset()
        {
            string guard = GuardCleanForFileOp("删除资产");
            if (guard != null) return guard;

            string name = CurrentAssetName;
            string error = NarrativeAssetStore.DeleteAssetFile(_currentPath);
            if (error != null) return error;

            _assetProblems.Remove(_currentPath);
            ResetDocument();
            RefreshAssets();
            Report("已删除资产 " + name);
            return null;
        }

        // ===================================================================
        // 撤销 / 重做
        // ===================================================================

        private string SnapshotCurrent()
        {
            if (_mode == EditorMode.None) return "";
            return _mode == EditorMode.Quest
                ? NarrativeAssetStore.SnapshotQuest(_questData)
                : NarrativeAssetStore.SnapshotDialogue(_dialogueData);
        }

        /// <summary>打开新文档：以磁盘版本作为"已保存"对照，历史栈归零。</summary>
        private void OpenDocument()
        {
            _savedSnapshot = SnapshotCurrent() ?? "";
            _history.OpenDocument(_savedSnapshot);
        }

        /// <summary>
        /// 打开时替你做的自动排布不算"用户的改动"：把基线抬到排布后的版本，
        /// 否则第一次撤销会莫名其妙把重叠布局还回来。
        /// </summary>
        private void ResetBaseline()
        {
            _history.OpenDocument(SnapshotCurrent());
        }

        /// <summary>
        /// 记一步可撤销的修改。历史栈存的是改动之前的基线快照，所以不需要在检查器动手前插桩；
        /// 内容没真变时不入栈（空点一次按钮不该占一层历史）。
        /// </summary>
        private void PushHistory()
        {
            if (_mode == EditorMode.None) return;
            _history.Commit(SnapshotCurrent());
        }

        /// <summary>按内存快照与磁盘快照是否一致重算脏标记（撤销回磁盘版本会自动变成"已保存"）。</summary>
        private void AfterDocumentChanged()
        {
            _dirty = _mode != EditorMode.None && (SnapshotCurrent() ?? "") != _savedSnapshot;
            UpdateTitle();
        }

        private void Undo()
        {
            string target = _history.Undo(SnapshotCurrent());
            if (target == null)
            {
                Report("没有可撤销的修改");
                return;
            }

            ApplySnapshot(target);
            Report($"已撤销（剩 {_history.UndoCount} 步可撤销，{_history.RedoCount} 步可重做）");
        }

        private void Redo()
        {
            string target = _history.Redo(SnapshotCurrent());
            if (target == null)
            {
                Report("没有可重做的修改");
                return;
            }

            ApplySnapshot(target);
            Report($"已重做（剩 {_history.UndoCount} 步可撤销，{_history.RedoCount} 步可重做）");
        }

        /// <summary>用快照串整份替换当前文档；只换数据，保留图的缩放与平移。</summary>
        private void ApplySnapshot(string snapshot)
        {
            if (_mode == EditorMode.Quest)
            {
                _questData = NarrativeAssetStore.RestoreQuest(snapshot);
                _graph.RebindQuest(_questData, QuestSubtitle(_questData));
            }
            else if (_mode == EditorMode.Dialogue)
            {
                _dialogueData = NarrativeAssetStore.RestoreDialogue(snapshot);
                _graph.RebindDialogue(_dialogueData, DialogueSubtitle(_dialogueData));
            }
            else
            {
                return;
            }

            AfterDocumentChanged();
            PruneSelection();
            PushInspector();
            ValidateCurrentQuiet();
        }

        /// <summary>撤销/重做后选中的节点可能已不存在，找不到就清空选中，避免检查器编辑一份不存在的对象。</summary>
        private void PruneSelection()
        {
            if (_selection.IsNone) return;

            bool stillThere;
            switch (_selection.Kind)
            {
                case NarrativeSelectionKind.QuestState:
                    stillThere = NarrativeAssetStore.FindQuestState(_questData, _selection.Id) != null;
                    break;
                case NarrativeSelectionKind.QuestBranch:
                    stillThere = NarrativeAssetStore.FindQuestBranch(_questData, _selection.Id) != null;
                    break;
                case NarrativeSelectionKind.DialogueNpc:
                    stillThere = NarrativeAssetStore.FindNpcNode(_dialogueData, _selection.Id) != null;
                    break;
                case NarrativeSelectionKind.DialoguePlayer:
                    stillThere = NarrativeAssetStore.FindPlayerNode(_dialogueData, _selection.Id) != null;
                    break;
                case NarrativeSelectionKind.Speaker:
                    stillThere = NarrativeAssetStore.FindDialogueSpeaker(_dialogueData, _selection.Id) != null;
                    break;
                default:
                    stillThere = false;
                    break;
            }

            if (!stillThere) ClearSelection();
        }

        private string QuestSubtitle(QuestData data)
        {
            if (data == null) return "";
            return $"{data.States?.Count ?? 0} 状态 · {data.Branches?.Count ?? 0} 分支 · 起始 {data.StartStateId}";
        }

        private string DialogueSubtitle(DialogueData data)
        {
            if (data == null) return "";
            return $"NPC {data.NpcReplies?.Count ?? 0} · 玩家选项 {data.PlayerReplies?.Count ?? 0} · 说话者 {data.Speakers?.Count ?? 0}";
        }

        private void Save()
        {
            if (_mode == EditorMode.None || string.IsNullOrEmpty(_currentPath))
            {
                Report("没有打开的资产，无法保存");
                return;
            }

            try
            {
                if (_mode == EditorMode.Quest) NarrativeAssetStore.WriteQuest(_currentPath, _questData);
                else NarrativeAssetStore.WriteDialogue(_currentPath, _dialogueData);
            }
            catch (Exception ex)
            {
                Report("保存失败：" + ex.Message);
                return;
            }

            _savedSnapshot = SnapshotCurrent() ?? "";
            _dirty = false;
            UpdateTitle();
            RefreshAssets();
            int problems = ValidateCurrentQuiet();
            Report(problems == 0
                ? $"已保存 {CurrentAssetName}，校验通过"
                : $"已保存 {CurrentAssetName}，但校验发现 {problems} 项问题（见底部）");
        }

        private void ReloadCurrent()
        {
            if (_mode == EditorMode.None || string.IsNullOrEmpty(_currentPath))
            {
                Report("没有打开的资产，无法重新载入");
                return;
            }

            string path = _currentPath;
            bool isQuest = _mode == EditorMode.Quest;
            if (isQuest) LoadQuest(path);
            else LoadDialogue(path);
        }

        /// <summary>有未保存修改时挡住切换；返回 false 表示用户应先保存或放弃。</summary>
        private bool ConfirmDiscard()
        {
            if (!_dirty) return true;
            Report("当前资产有未保存修改：先点「保存」，或点「放弃修改」再切换（也可以 Ctrl+Z 一步步撤销）。");
            return false;
        }

        // ===================================================================
        // 校验
        // ===================================================================

        private void ValidateCurrent()
        {
            if (_mode == EditorMode.None)
            {
                Report("没有打开的资产，无法校验");
                return;
            }

            int problems = ValidateCurrentQuiet();
            Report(problems == 0
                ? $"校验通过：{CurrentAssetName}" + (_dirty ? "（内存版本；记得保存）" : "")
                : $"校验发现 {problems} 项问题，详见底部校验面板");
        }

        /// <summary>校验内存里的当前资产并刷新底部面板，返回问题条数（打开/修改后自动调用）。</summary>
        private int ValidateCurrentQuiet()
        {
            if (_mode == EditorMode.None) return 0;

            var problems = _mode == EditorMode.Quest
                ? NarrativeAssetStore.ValidateQuestData(_questData)
                : NarrativeAssetStore.ValidateDialogueData(_dialogueData);
            var audit = _mode == EditorMode.Quest
                ? NarrativeAssetStore.AuditQuest(_questData)
                : NarrativeAssetStore.AuditDialogue(_dialogueData);
            if (_mode == EditorMode.Quest)
                audit.AddRange(NarrativeAssetStore.AuditTemplateLink(_questData, _currentPath));

            int auditErrors = 0;
            foreach (var finding in audit) if (finding.IsError) auditErrors++;
            int warnings = audit.Count - auditErrors;

            var issues = new List<NarrativeIssue>();
            foreach (var problem in problems)
            {
                issues.Add(new NarrativeIssue
                {
                    File = _currentPath,
                    Message = problem,
                    NodeId = ExtractNodeId(problem),
                    IsError = true,
                    IsQuest = _mode == EditorMode.Quest
                });
            }
            AppendAudit(issues, _currentPath, _mode == EditorMode.Quest, audit);

            int errorCount = problems.Count + auditErrors;
            string warnNote = warnings > 0 ? $"，另有 {warnings} 条提醒" : "";
            _issues.SetIssues(issues, errorCount == 0
                ? $"校验通过：{CurrentAssetName}" + warnNote + (_dirty ? "（内存版本，尚未写盘）" : "")
                : $"校验：{CurrentAssetName} 发现 {errorCount} 项问题" + warnNote + "（内存版本）");

            if (!string.IsNullOrEmpty(_currentPath))
            {
                _assetProblems[_currentPath] = errorCount;
                _browser.SetProblems(_assetProblems);
            }

            return errorCount;
        }

        private static void AppendAudit(List<NarrativeIssue> issues, string path, bool isQuest,
            List<NarrativeAssetStore.NarrativeAuditIssue> findings)
        {
            foreach (var finding in findings ?? new List<NarrativeAssetStore.NarrativeAuditIssue>())
            {
                issues.Add(new NarrativeIssue
                {
                    File = path,
                    Message = finding.Message,
                    NodeId = finding.NodeId,
                    IsError = finding.IsError,
                    IsQuest = isQuest
                });
            }
        }

        private void ValidateAll()
        {
            var issues = new List<NarrativeIssue>();
            int total = 0;

            foreach (var path in NarrativeAssetStore.ListJsonFiles(NarrativeAssetStore.QuestsDirectory))
            {
                total++;
                AppendIssues(issues, path, true, NarrativeAssetStore.ValidateQuestFile(path));
                AppendAudit(issues, path, true, NarrativeAssetStore.AuditQuest(TryLoadQuest(path)));
            }
            foreach (var path in NarrativeAssetStore.ListJsonFiles(NarrativeAssetStore.DialoguesDirectory))
            {
                total++;
                AppendIssues(issues, path, false, NarrativeAssetStore.ValidateDialogueFile(path));
                AppendAudit(issues, path, false, NarrativeAssetStore.AuditDialogue(TryLoadDialogue(path)));
            }

            int errors = 0;
            foreach (var issue in issues) if (issue.IsError) errors++;
            int warnings = issues.Count - errors;
            string warnNote = warnings > 0 ? $"，另有 {warnings} 条提醒" : "";

            string staleNote = _dirty ? "（当前资产有未保存修改，校验的是磁盘版本）" : "";
            _issues.SetIssues(issues, (errors == 0
                ? $"全量校验通过：{total} 个叙事资产都能被运行时装载" + warnNote
                : $"全量校验：{total} 个资产，发现 {errors} 项问题" + warnNote + "（点条目可跳转）") + staleNote);

            _assetProblems.Clear();
            foreach (var issue in issues)
            {
                if (!issue.IsError) continue;
                _assetProblems.TryGetValue(issue.File, out int count);
                _assetProblems[issue.File] = count + 1;
            }
            _browser.SetProblems(_assetProblems);

            Report((errors == 0
                ? $"全量校验通过：{total} 个资产" + warnNote
                : $"全量校验发现 {errors} 项问题" + warnNote + "，详见底部校验面板") + staleNote);
        }

        private static QuestData TryLoadQuest(string path)
        {
            try { return NarrativeAssetStore.LoadQuestData(path); }
            catch { return null; }
        }

        private static DialogueData TryLoadDialogue(string path)
        {
            try { return NarrativeAssetStore.LoadDialogueData(path); }
            catch { return null; }
        }

        private static void AppendIssues(List<NarrativeIssue> issues, string path, bool isQuest, List<string> problems)
        {
            foreach (var problem in problems)
            {
                issues.Add(new NarrativeIssue
                {
                    File = path,
                    Message = problem,
                    NodeId = ExtractNodeId(problem),
                    IsError = true,
                    IsQuest = isQuest
                });
            }
        }

        /// <summary>校验信息里带引号的 id 就是节点 id（如 "找不到状态 'x'"），用来做点击定位。</summary>
        private static string ExtractNodeId(string message)
        {
            if (string.IsNullOrEmpty(message)) return "";
            var match = Regex.Match(message, "'([^']+)'");
            return match.Success ? match.Groups[1].Value : "";
        }

        private void OnIssueActivated(NarrativeIssue issue)
        {
            if (issue == null) return;

            bool otherFile = !string.IsNullOrEmpty(issue.File) &&
                             !string.Equals(Path.GetFileName(issue.File), Path.GetFileName(_currentPath), StringComparison.OrdinalIgnoreCase);
            if (otherFile)
            {
                if (!ConfirmDiscard()) return;
                if (issue.IsQuest) LoadQuest(issue.File);
                else LoadDialogue(issue.File);
            }

            if (string.IsNullOrEmpty(issue.NodeId)) return;

            if (_mode == EditorMode.Quest)
            {
                if (NarrativeAssetStore.FindQuestState(_questData, issue.NodeId) != null)
                    SelectNode(NarrativeSelectionKind.QuestState, issue.NodeId);
                else if (NarrativeAssetStore.FindQuestBranch(_questData, issue.NodeId) != null)
                    SelectNode(NarrativeSelectionKind.QuestBranch, issue.NodeId);
            }
            else if (_mode == EditorMode.Dialogue)
            {
                if (NarrativeAssetStore.FindNpcNode(_dialogueData, issue.NodeId) != null)
                    SelectNode(NarrativeSelectionKind.DialogueNpc, issue.NodeId);
                else if (NarrativeAssetStore.FindPlayerNode(_dialogueData, issue.NodeId) != null)
                    SelectNode(NarrativeSelectionKind.DialoguePlayer, issue.NodeId);
                else if (NarrativeAssetStore.FindDialogueSpeaker(_dialogueData, issue.NodeId) != null)
                    SelectNode(NarrativeSelectionKind.Speaker, issue.NodeId);
            }
        }

        /// <summary>把当前资产的可读大纲写进日志（设计/文案核对流程用，不用在图里一行行数）。</summary>
        private void DumpOutline()
        {
            if (_mode == EditorMode.None)
            {
                Report("没有打开的资产，无法生成大纲");
                return;
            }

            string outline = _mode == EditorMode.Quest
                ? NarrativeAssetStore.DescribeQuestOutline(_questData)
                : NarrativeAssetStore.DescribeDialogueOutline(_dialogueData);
            Debug.Log("[NarrativePro] 大纲 " + CurrentAssetName + "\n" + outline);
            Report("大纲已写入日志（编辑器 窗口 → 日志 面板可查）");
        }

        /// <summary>
        /// 自动化验收：把事件向导与镜头/CG 下拉的数据源写到 Saved 回执。
        /// 日志有缓冲，验收时读文件才拿得到。
        /// </summary>
        private void DumpDataSummary()
        {
            var quests = NarrativeAssetStore.QuestAssetNames();
            var dialogues = NarrativeAssetStore.DialogueAssetNames();
            var shots = NarrativeAssetStore.ShotChoices();
            var movies = NarrativeAssetStore.MovieChoices();

            var lines = new System.Text.StringBuilder();
            lines.AppendLine("quests=" + quests.Count + " [" + string.Join(",", quests) + "]");
            lines.AppendLine("dialogues=" + dialogues.Count + " [" + string.Join(",", dialogues) + "]");
            lines.AppendLine("shots=" + shots.Count + " [" +
                             string.Join(",", shots.ConvertAll(s => s.Value)) + "]");
            lines.AppendLine("movies=" + movies.Count + " [" + string.Join(",", movies) + "]");

            string text = lines.ToString();
            Debug.Log("[NarrativePro] NP_DATA\n" + text);
            try
            {
                string path = System.IO.Path.Combine(FlaxEngine.Globals.ProjectFolder, "Saved", "NarrativeProEditorData.txt");
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                System.IO.File.WriteAllText(path, text);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[NarrativePro] 数据回执写入失败: " + ex.Message);
            }
            Report("数据源：" + quests.Count + " 任务 / " + dialogues.Count + " 对话 / " +
                   shots.Count + " 镜头 / " + movies.Count + " CG");
        }

        /// <summary>工具栏入口：有模板 id 就连带选中那条模板，没有就只把任务编辑器叫出来。</summary>
        private void OpenQuestEditor()
        {
            int templateId = _mode == EditorMode.Quest ? (_questData?.QuestTemplateId ?? 0) : 0;
            string error = templateId > 0 ? QuestEditorLink.OpenTemplate(templateId) : QuestEditorLink.Show();
            Report(error ?? (templateId > 0
                ? $"已打开任务编辑器并选中模板 {templateId}"
                : "已打开任务编辑器（当前叙事资产没挂模板 id，未联动选中）"));
        }

        /// <summary>支持 search:&lt;关键词&gt;：跑浏览器搜索框的同一条全文检索，命中写日志。</summary>
        private bool HandleSearchCommand(string command)
        {
            const string prefix = "search:";
            if (!command.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;

            string query = command.Substring(prefix.Length).Trim();
            if (query.Length < 2)
            {
                Report("搜索关键词至少 2 个字符");
                return true;
            }

            var hits = NarrativeAssetStore.SearchAllContent(query, 3);
            if (hits.Count == 0)
            {
                Report("全文搜索没有命中：" + query);
                return true;
            }

            var lines = new List<string>();
            foreach (var hit in hits)
            {
                lines.Add(Path.GetFileNameWithoutExtension(hit.Path) + "/" + hit.NodeId + ": " + hit.Snippet);
            }
            Debug.Log("[NarrativePro] NP_SEARCH " + query + " ->\n" + string.Join("\n", lines));
            Report($"全文搜索命中 {hits.Count} 处（前几条已写日志）：" + query);
            return true;
        }

        /// <summary>支持 link:&lt;模板id&gt;（空=清空）：把当前叙事资产挂到那条 DB 任务模板上。</summary>
        private bool HandleLinkCommand(string command)
        {
            const string prefix = "link:";
            if (!command.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;

            if (_mode != EditorMode.Quest)
            {
                Report("只有任务资产能挂 DB 任务模板");
                return true;
            }

            string error = NarrativeAssetStore.SetQuestTemplateId(_questData, command.Substring(prefix.Length));
            if (error != null)
            {
                Report("操作被拒绝：" + error);
                return true;
            }

            string label = _questData.QuestTemplateId?.ToString() ?? "(已清空)";
            Mutated($"已把叙事资产挂到模板 {label}（内存）。");
            return true;
        }

        /// <summary>
        /// 自动化验收用：narr:&lt;id&gt; 反射进 QuestEditor.NarrativeLink，跑"任务编辑器 → 叙事图"的同一实现
        /// （有则打开、无则生成骨架）。按钮回调走的就是这个方法，所以这里过了等于按钮也过。
        /// </summary>
        private bool HandleNarrativeLinkProbe(string command)
        {
            const string prefix = "narr:";
            if (!command.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;

            if (!int.TryParse(command.Substring(prefix.Length).Trim(), out int templateId) || templateId <= 0)
            {
                Report("narr: 需要一个正整数模板 id");
                return true;
            }

            var type = QuestEditorLink.Find("QuestEditor.NarrativeLink");
            var method = type?.GetMethod("OpenOrCreateForTemplate",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (method == null)
            {
                Report("任务编辑器插件未加载 NarrativeLink（版本过旧或未编译）。");
                return true;
            }

            string error;
            try
            {
                error = method.Invoke(null, new object[] { templateId }) as string;
            }
            catch (Exception ex)
            {
                var inner = ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
                error = "反射调用失败：" + inner.Message;
            }

            Debug.Log("[NarrativePro] NP_NARRLINK " + templateId + " -> " + (error ?? "ok"));
            Report(error ?? $"反向链路通过：模板 {templateId} 的叙事图已打开");
            return true;
        }

        /// <summary>
        /// 自动化验收用：fusion:&lt;id&gt; 唤起任务编辑器、按模板打开它的内嵌叙事图（走「叙事图」按钮的同一实现），
        /// 再回读面板是否真的内嵌 + 已切到叙事图模式。按钮点击本身没法脚本化，这里验的是它背后那条链。
        /// </summary>
        private bool HandleFusionProbe(string command)
        {
            const string prefix = "fusion:";
            if (!command.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;

            if (!int.TryParse(command.Substring(prefix.Length).Trim(), out int templateId) || templateId <= 0)
            {
                Report("fusion: 需要一个正整数模板 id");
                return true;
            }

            var type = QuestEditorLink.Find("QuestEditor.QuestEditorWindow");
            if (type == null)
            {
                Report("任务编辑器插件未加载（找不到 QuestEditorWindow）。");
                return true;
            }

            try
            {
                type.GetMethod("ShowWindow", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, null);
                object instance = type.GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null);
                if (instance == null)
                {
                    Report("任务编辑器已唤起但拿不到窗口实例。");
                    return true;
                }

                type.GetMethod("SelectQuestById", BindingFlags.Public | BindingFlags.Instance)
                    ?.Invoke(instance, new object[] { templateId });

                var open = type.GetMethod("OpenNarrativeGraphFor", BindingFlags.Public | BindingFlags.Instance);
                if (open == null)
                {
                    Report("任务编辑器版本过旧：缺少 OpenNarrativeGraphFor。");
                    return true;
                }
                open.Invoke(instance, new object[] { templateId });

                bool embedded = type.GetField("_narrativeHost", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(instance) != null;
                bool inMode = type.GetField("_narrativeMode", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(instance) is bool flag && flag;

                Debug.Log($"[NarrativePro] NP_FUSION {templateId} embedded={embedded} narrativeMode={inMode}");
                Report(embedded && inMode
                    ? $"融合模式已开：任务编辑器里内嵌了叙事图（模板 {templateId}）"
                    : "融合模式没打开（embedded=" + embedded + " mode=" + inMode + "）");
            }
            catch (Exception ex)
            {
                var inner = ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
                Report("fusion 失败：" + inner.Message);
            }

            return true;
        }

        /// <summary>
        /// 自动化验收用：questact:&lt;动作&gt;[:参数] 在任务编辑器窗口里跑一次真实动作
        /// （removeobj:&lt;i&gt; / undo / redo / layout），回执写日志。
        /// </summary>
        /// <summary>按名字取任务编辑器里的输入框（giver/turnin/prereq/obj0/item0），给自动化探针用。</summary>
        private static object ResolveQuestFieldBox(Type type, object instance, string fieldName)
        {
            string boxField = fieldName == "giver" ? "_giverIdBox"
                : fieldName == "turnin" ? "_turnInIdBox"
                : fieldName == "prereq" ? "_prereqBox"
                : "";

            if (boxField.Length > 0)
                return type.GetField(boxField, BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(instance);

            if (fieldName == "obj0" || fieldName == "objcount0" || fieldName == "item0")
            {
                string rowsName = fieldName == "item0" ? "_itemRows" : "_objectiveRows";
                string member = fieldName == "objcount0" ? "Count" : "Target";
                var rows = type.GetField(rowsName, BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(instance) as Array;
                object row = rows?.GetValue(0);
                return row?.GetType().GetField(member, BindingFlags.Public | BindingFlags.Instance)?.GetValue(row);
            }

            return null;
        }

        private bool HandleQuestActProbe(string command)
        {
            const string prefix = "questact:";
            if (!command.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;

            var type = QuestEditorLink.Find("QuestEditor.QuestEditorWindow");
            object instance = type?.GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null);
            if (instance == null)
            {
                Report("任务编辑器没打开，先 fusion:<id> 或从 Tools 菜单打开");
                return true;
            }

            string rest = command.Substring(prefix.Length).Trim();
            int colon = rest.IndexOf(':');
            string verb = (colon < 0 ? rest : rest.Substring(0, colon)).Trim().ToLowerInvariant();
            string argument = colon < 0 ? "" : rest.Substring(colon + 1).Trim();

            try
            {
                switch (verb)
                {
                    case "removeobj":
                        type.GetMethod("RemoveObjective", BindingFlags.NonPublic | BindingFlags.Instance)
                            ?.Invoke(instance, new object[] { int.TryParse(argument, out int index) ? index : 0 });
                        Report("已调用 RemoveObjective(" + argument + ")");
                        return true;
                    case "undo":
                        type.GetMethod("UndoDraft", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(instance, null);
                        Report("已调用撤销");
                        return true;
                    case "redo":
                        type.GetMethod("RedoDraft", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(instance, null);
                        Report("已调用重做");
                        return true;
                    case "page":
                        type.GetMethod("ToggleNarrativeMode", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(instance, null);
                        Report("已切换中心区页");
                        return true;
                    case "desc":
                    {
                        var rows = type.GetField("_objectiveRows", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(instance) as Array;
                        var lines = new List<string>();
                        if (rows != null)
                        {
                            foreach (var row in rows)
                            {
                                if (row == null) continue;
                                string kind = Convert.ToString(row.GetType().GetField("Kind", BindingFlags.Public | BindingFlags.Instance)?.GetValue(row)?.GetType().GetProperty("SelectedItem")?.GetValue(row.GetType().GetField("Kind", BindingFlags.Public | BindingFlags.Instance)?.GetValue(row)));
                                string target = Convert.ToString(row.GetType().GetField("Target", BindingFlags.Public | BindingFlags.Instance)?.GetValue(row)?.GetType().GetProperty("Text")?.GetValue(row.GetType().GetField("Target", BindingFlags.Public | BindingFlags.Instance)?.GetValue(row)));
                                string count = Convert.ToString(row.GetType().GetField("Count", BindingFlags.Public | BindingFlags.Instance)?.GetValue(row)?.GetType().GetProperty("Text")?.GetValue(row.GetType().GetField("Count", BindingFlags.Public | BindingFlags.Instance)?.GetValue(row)));
                                string desc = Convert.ToString(row.GetType().GetField("Description", BindingFlags.Public | BindingFlags.Instance)?.GetValue(row)?.GetType().GetProperty("Text")?.GetValue(row.GetType().GetField("Description", BindingFlags.Public | BindingFlags.Instance)?.GetValue(row)));
                                if (!string.IsNullOrEmpty(target) || !string.IsNullOrEmpty(desc))
                                    lines.Add(kind + " / " + target + " / x" + count + " / " + desc);
                            }
                        }
                        Debug.Log("[NarrativePro] NP_DESC -> " + string.Join(" || ", lines));
                        Report("条件行：" + lines.Count + "（见日志）");
                        return true;
                    }
                    case "pickchoose":
                    {
                        var parts = argument.Split(':');
                        string fieldName = parts[0].Trim().ToLowerInvariant();
                        int pickIndex = parts.Length > 1 && int.TryParse(parts[1], out int parsedIndex) ? parsedIndex : 0;

                        object box = ResolveQuestFieldBox(type, instance, fieldName);
                        if (box == null) { Report("找不到要测试的输入框：" + fieldName); return true; }

                        type.GetMethod("TogglePicker", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(instance, new[] { box });
                        type.GetMethod("ChoosePickerRow", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(instance, new object[] { pickIndex });

                        string value = box.GetType().GetProperty("Text")?.GetValue(box) as string ?? "";
                        Debug.Log("[NarrativePro] NP_PICKWRITE " + fieldName + " -> '" + value + "' (下标 " + pickIndex + ")");
                        Report("下拉选中第 " + pickIndex + " 项 → 写入 '" + value + "'");
                        return true;
                    }
                    case "pick":
                    {
                        string fieldName = argument.Trim().ToLowerInvariant();
                        object box = ResolveQuestFieldBox(type, instance, fieldName);
                        if (box == null) { Report("找不到要测试的输入框：" + argument); return true; }

                        type.GetMethod("TogglePicker", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(instance, new[] { box });

                        object panel = type.GetField("_pickerPanel", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(instance);
                        var rowsField = type.GetField("_pickerRows", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(instance) as Array;
                        var lines = new List<string>();
                        if (rowsField != null)
                        {
                            foreach (var row in rowsField)
                            {
                                object visible = row?.GetType().GetProperty("Visible")?.GetValue(row);
                                if (visible is bool v && v)
                                    lines.Add(Convert.ToString(row.GetType().GetProperty("Text")?.GetValue(row)));
                            }
                        }
                        bool panelVisible = panel?.GetType().GetProperty("Visible")?.GetValue(panel) is bool pv && pv;
                        Debug.Log("[NarrativePro] NP_PICKER " + fieldName + " visible=" + panelVisible + " rows=" + string.Join(" | ", lines));
                        Report("下拉：" + fieldName + " 共 " + lines.Count + " 项（见日志）");
                        return true;
                    }
                    case "pickclose":
                        type.GetMethod("ClosePicker", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(instance, null);
                        Report("已关闭下拉");
                        return true;
                    case "reload":
                    {
                        object idBox = type.GetField("_idBox", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(instance);
                        string text = idBox?.GetType().GetProperty("Text")?.GetValue(idBox) as string ?? "";
                        if (int.TryParse(text, out int questId) && questId > 0)
                        {
                            type.GetMethod("SelectQuestById", BindingFlags.Public | BindingFlags.Instance)
                                ?.Invoke(instance, new object[] { questId });
                            Report("已请求重新载入任务 " + questId + "（丢弃未保存改动）");
                        }
                        else
                        {
                            Report("当前没有任务可重载");
                        }
                        return true;
                    }
                    case "cleartarget":
                    {
                        var rowsField = type.GetField("_objectiveRows", BindingFlags.NonPublic | BindingFlags.Instance);
                        var rows = rowsField?.GetValue(instance) as Array;
                        if (rows == null || rows.Length == 0) { Report("没有条件行"); return true; }

                        object row = rows.GetValue(0);
                        object target = row?.GetType().GetField("Target", BindingFlags.Public | BindingFlags.Instance)?.GetValue(row);
                        var textProperty = target?.GetType().GetProperty("Text");
                        textProperty?.SetValue(target, "");

                        type.GetMethod("RefreshFlow", BindingFlags.Public | BindingFlags.Instance)?.Invoke(instance, null);
                        type.GetMethod("ArrangeInspector", BindingFlags.Public | BindingFlags.Instance)?.Invoke(instance, null);
                        Report("已清空条件 1 的目标 id（应标红提示）");
                        return true;
                    }
                    case "flownode":
                    {
                        object flow = type.GetField("_flow", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(instance);
                        object inspector = type.GetField("_inspector", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(instance);
                        if (flow == null || inspector == null) { Report("流程图/检查器还没建好"); return true; }

                        flow.GetType().GetMethod("SelectNode")?.Invoke(flow, new object[] { argument });
                        type.GetMethod("OnFlowNodeSelected", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(instance, new object[] { argument });

                        object offset = inspector.GetType().GetProperty("ViewOffset")?.GetValue(inspector);
                        Debug.Log("[NarrativePro] NP_FLOWNODE " + argument + " -> viewOffset=" + offset);
                        Report("已点流程图节点 " + argument + "（检查器不应滚动）");
                        return true;
                    }
                    default:
                        Report("未知 questact 动作：" + verb);
                        return true;
                }
            }
            catch (Exception ex)
            {
                var inner = ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
                Report("questact 失败：" + inner.Message);
                return true;
            }
        }

        // ===================================================================
        // 选择与变更
        // ===================================================================

        /// <summary>
        /// 图上右键菜单。菜单项按选中类型给：节点上给"长节点/复制/删除"，空白处给全局动作。
        /// 数据操作全部走检查器与存储层，窗口只负责把菜单拼出来。
        /// </summary>
        private void OnGraphContextMenu(NarrativeSelection selection, Float2 at)
        {
            if (_mode == EditorMode.None) return;

            var menu = new FlaxEditor.GUI.ContextMenu.ContextMenu();
            var labels = new List<string>();

            if (selection.IsNone)
            {
                AddMenuButton(menu, labels, "自动布局", () =>
                {
                    _graph.AutoLayout();
                    Mutated("已按依赖关系自动布局（内存）。");
                });
                AddMenuButton(menu, labels, "适配视图", () => _graph.FitToView());
                menu.AddSeparator();
                AddMenuButton(menu, labels, "校验当前", ValidateCurrent);
                AddMenuButton(menu, labels, "保存", Save);
                menu.AddSeparator();
                AddMenuButton(menu, labels, "撤销", Undo);
                AddMenuButton(menu, labels, "重做", Redo);
            }
            else
            {
                switch (selection.Kind)
                {
                    case NarrativeSelectionKind.QuestState:
                        AddMenuButton(menu, labels, "新建状态并连接", _inspector.ConnectNewNodeAfterSelected);
                        AddMenuButton(menu, labels, "设为起始状态", _inspector.SetStartOrRoot);
                        menu.AddSeparator();
                        AddMenuButton(menu, labels, "复制状态", _inspector.CopySelected);
                        AddMenuButton(menu, labels, "删除状态", () => _inspector.DeleteSelected());
                        break;
                    case NarrativeSelectionKind.QuestBranch:
                        AddMenuButton(menu, labels, "反转方向", _inspector.FlipSelectedBranch);
                        menu.AddSeparator();
                        AddMenuButton(menu, labels, "复制分支", _inspector.CopySelected);
                        AddMenuButton(menu, labels, "删除分支", () => _inspector.DeleteSelected());
                        break;
                    case NarrativeSelectionKind.DialogueNpc:
                        AddMenuButton(menu, labels, "新建 NPC 节点并连接", _inspector.ConnectNewNodeAfterSelected);
                        AddMenuButton(menu, labels, "设为根节点", _inspector.SetStartOrRoot);
                        menu.AddSeparator();
                        AddMenuButton(menu, labels, "复制节点", _inspector.CopySelected);
                        AddMenuButton(menu, labels, "删除节点", () => _inspector.DeleteSelected());
                        break;
                    case NarrativeSelectionKind.DialoguePlayer:
                        AddMenuButton(menu, labels, "复制玩家选项", _inspector.CopySelected);
                        AddMenuButton(menu, labels, "删除玩家选项", () => _inspector.DeleteSelected());
                        break;
                    case NarrativeSelectionKind.Speaker:
                        AddMenuButton(menu, labels, "删除说话者", () => _inspector.DeleteSelected());
                        break;
                }
            }

            if (labels.Count == 0) return;

            _openMenu = menu;
            menu.Show(_graph, at, null);
            Debug.Log("[NarrativePro] NP_MENU " + (selection.IsNone ? "画布" : KindName(selection.Kind) + " " + selection.Id) +
                      " -> " + string.Join(" | ", labels));
        }

        private static void AddMenuButton(FlaxEditor.GUI.ContextMenu.ContextMenu menu, List<string> labels, string text, Action action = null)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (action != null) menu.AddButton(text, action);
            labels.Add(text);
        }

        /// <summary>关掉上一次弹出的右键菜单（自动化验收拍完照就收掉，不留给下一个点击）。</summary>
        private void CloseContextMenu()
        {
            if (_openMenu == null) return;
            try
            {
                _openMenu.Visible = false;
                _openMenu.Dispose();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[NarrativePro] 关闭右键菜单失败：" + ex.Message);
            }
            _openMenu = null;
        }

        /// <summary>
        /// Shift+拖拽连线的落地。任务：只接 状态→状态（分支节点本身就是连线）；
        /// 对话：交给 LinkDialogueAuto 判定方向（npc→npc / npc→player / player→npc），它不认识的组合会给出明确报错。
        /// </summary>
        private void OnGraphLinkRequested(NarrativeSelection from, NarrativeSelection to)
        {
            if (_mode == EditorMode.Quest)
            {
                if (from.Kind != NarrativeSelectionKind.QuestState || to.Kind != NarrativeSelectionKind.QuestState)
                {
                    Report("任务图里只能从状态连到状态（分支节点本身就是连线）");
                    return;
                }

                string error = NarrativeAssetStore.AddQuestBranchConnected(_questData, from.Id, to.Id, out string branchId);
                if (error != null)
                {
                    Report("操作被拒绝：" + error);
                    return;
                }
                Mutated($"已用分支 {branchId} 连线 {from.Id} → {to.Id}（内存）。");
                SelectNode(NarrativeSelectionKind.QuestBranch, branchId);
                return;
            }

            if (_mode == EditorMode.Dialogue)
            {
                string error = NarrativeAssetStore.LinkDialogueAuto(_dialogueData, from.Id, to.Id);
                if (error != null)
                {
                    Report("操作被拒绝：" + error);
                    return;
                }
                Mutated($"已连线 {from.Id} → {to.Id}（内存）。");
                SelectNode(to.Kind, to.Id);
            }
        }

        /// <summary>自动化验收：act:link:<目标id> 等价于 Shift+拖到那个节点。</summary>
        private NarrativeSelection ResolveSelectionById(string id)
        {
            if (_mode == EditorMode.Quest)
            {
                if (NarrativeAssetStore.FindQuestState(_questData, id) != null) return new NarrativeSelection(NarrativeSelectionKind.QuestState, id);
                if (NarrativeAssetStore.FindQuestBranch(_questData, id) != null) return new NarrativeSelection(NarrativeSelectionKind.QuestBranch, id);
            }
            else if (_mode == EditorMode.Dialogue)
            {
                if (NarrativeAssetStore.FindNpcNode(_dialogueData, id) != null) return new NarrativeSelection(NarrativeSelectionKind.DialogueNpc, id);
                if (NarrativeAssetStore.FindPlayerNode(_dialogueData, id) != null) return new NarrativeSelection(NarrativeSelectionKind.DialoguePlayer, id);
                if (NarrativeAssetStore.FindDialogueSpeaker(_dialogueData, id) != null) return new NarrativeSelection(NarrativeSelectionKind.Speaker, id);
            }
            return NarrativeSelection.None;
        }

        internal void SelectNode(NarrativeSelectionKind kind, string id)
        {
            _graph.SelectNode(kind, id);
        }

        internal void ClearSelection()
        {
            _selection = NarrativeSelection.None;
            _graph.Select(NarrativeSelection.None);
            PushInspector();
        }

        private void OnGraphSelection(NarrativeSelection selection)
        {
            _selection = selection;
            PushInspector();
            if (!selection.IsNone) Report("已选中 " + KindName(selection.Kind) + " " + selection.Id);
        }

        /// <summary>图里拖动节点后：位置已写进 DTO，这里负责入撤销栈、标脏与提示。</summary>
        private void OnNodeMoved()
        {
            PushHistory();
            AfterDocumentChanged();
            Report("节点位置已更新（内存），点「保存」写回资产，Ctrl+Z 可撤销。");
        }

        /// <summary>图里按 Delete：删除动作由检查器执行（它才知道连带关系怎么断），这里只做兜底提示。</summary>
        private void OnGraphDeleteRequested()
        {
            if (_selection.IsNone)
            {
                Report("先在图上点一个节点，再按 Delete 删除");
                return;
            }

            if (!_inspector.DeleteSelected()) Report("选中项当前不可删除");
        }

        /// <summary>任何结构/字段修改后调用：入撤销栈、标脏、刷新图与检查器。</summary>
        internal void Mutated(string message)
        {
            PushHistory();
            AfterDocumentChanged();
            _graph.RefreshNodes();
            RefreshGraphSubtitle();
            PushInspector();
            int problems = ValidateCurrentQuiet();
            Report(message + (problems > 0 ? $" · 校验发现 {problems} 项问题" : "") + "（Ctrl+Z 撤销）");
        }

        /// <summary>增删节点后表头统计（"5 状态 · 4 分支"）要跟着变，否则一看就是旧数字。</summary>
        private void RefreshGraphSubtitle()
        {
            if (_mode == EditorMode.None) return;
            _graph.SetSubtitle(_mode == EditorMode.Quest ? QuestSubtitle(_questData) : DialogueSubtitle(_dialogueData));
        }

        internal void Report(string message)
        {
            _status = message ?? "";
            if (_statusLabel != null) _statusLabel.Text = _status;
            UpdateInfo();
            UpdateHistoryButtons();
        }

        private void PushInspector()
        {
            _inspector.SetContext(_mode == EditorMode.Quest, _questData, _dialogueData, _selection);
        }

        private static string KindName(NarrativeSelectionKind kind)
        {
            switch (kind)
            {
                case NarrativeSelectionKind.QuestState: return "状态";
                case NarrativeSelectionKind.QuestBranch: return "分支";
                case NarrativeSelectionKind.DialogueNpc: return "NPC 节点";
                case NarrativeSelectionKind.DialoguePlayer: return "玩家选项";
                case NarrativeSelectionKind.Speaker: return "说话者";
                default: return "节点";
            }
        }

        private void UpdateTitle()
        {
            Title = "NarrativePro 叙事资产" + (_dirty ? " *" : "");
            UpdateInfo();
        }

        private void UpdateInfo()
        {
            if (_statusInfo == null) return;

            string doc = _mode == EditorMode.None ? "未打开资产" : CurrentAssetName + (_dirty ? " · 未保存" : " · 已保存");
            string kind = _mode == EditorMode.None ? "—" : _mode == EditorMode.Quest ? "任务" : "对话";
            _statusInfo.Text = doc + " · " + kind + " · 缩放 " + (int)(_graph != null ? _graph.Zoom * 100f : 100f) + "%";
            _statusInfo.TextColor = _dirty ? NarrativeEditorTheme.Warning : NarrativeEditorTheme.TextDim;
        }

        // ===================================================================
        // 布局
        // ===================================================================

        private void BuildChrome()
        {
            _toolbar = new Panel { BackgroundColor = NarrativeEditorTheme.Panel };
            AddChild(_toolbar);            ToolbarButton("新建任务", () => NewQuest("Q_" + DateTime.Now.ToString("yyyyMMdd_HHmmss")));
            ToolbarButton("新建对话", () => NewDialogue("D_" + DateTime.Now.ToString("yyyyMMdd_HHmmss")));
            ToolbarButton("保存", Save);
            ToolbarButton("放弃修改", ReloadCurrent);
            _undoButton = ToolbarButton("撤销", Undo);
            _redoButton = ToolbarButton("重做", Redo);
            ToolbarButton("校验当前", ValidateCurrent);
            ToolbarButton("校验全部", ValidateAll);
            ToolbarButton("大纲", DumpOutline);
            ToolbarButton("任务编辑器", OpenQuestEditor);
            ToolbarButton("自动布局", () =>
            {
                if (_mode == EditorMode.None) { Report("没有打开的资产"); return; }
                _graph.AutoLayout();
                Mutated("已按依赖关系自动布局（内存），点「保存」写回位置。");
            });
            ToolbarButton("适配视图", () => _graph.FitToView());
            ToolbarButton("刷新列表", RefreshAssets);
            ToolbarButton("注册表", () =>
            {
                NarrativeAssetStore.WarmLogicRegistry();
                Debug.Log("[NarrativePro] 叙事逻辑注册表\n" + NarrativeAssetStore.DescribeRegistry());
                Report("注册表名单已写入日志（编辑器 窗口 → 日志 面板可查）");
            });

            _browser = new NarrativeAssetBrowserPane();
            _browser.AssetActivated += OnAssetActivated;
            AddChild(_browser);

            _graph = new NarrativeGraphView();
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

            _statusBar = new Panel { BackgroundColor = NarrativeEditorTheme.Panel };
            AddChild(_statusBar);

            _statusLabel = new Label
            {
                Text = _status,
                TextColor = NarrativeEditorTheme.Text,
                VerticalAlignment = TextAlignment.Center,
                ClipText = true
            };
            _statusBar.AddChild(_statusLabel);

            _statusInfo = new Label
            {
                Text = "",
                TextColor = NarrativeEditorTheme.TextDim,
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
                ClipText = true
            };
            _statusBar.AddChild(_statusInfo);
        }

        private readonly List<(Button Button, float Width)> _toolbarButtons = new List<(Button, float)>();
        private Button _undoButton;
        private Button _redoButton;

        private Button ToolbarButton(string text, Action action)
        {
            float width = Math.Max(58f, NarrativeEditorTheme.Measure(text, NarrativeEditorTheme.FontSmall) + 18f);
            var button = new Button { Text = text };
            button.Clicked += action;
            _toolbar.AddChild(button);
            _toolbarButtons.Add((button, width));
            return button;
        }

        /// <summary>撤销/重做按钮按历史深度置灰，Tooltip 报层数——不置灰的话点了只会看到"没有可撤销的修改"。</summary>
        private void UpdateHistoryButtons()
        {
            if (_undoButton != null)
            {
                _undoButton.Enabled = _history.UndoCount > 0;
                _undoButton.TooltipText = _history.UndoCount > 0
                    ? $"可撤销 {_history.UndoCount} 步（Ctrl+Z）"
                    : "没有可撤销的修改";
            }
            if (_redoButton != null)
            {
                _redoButton.Enabled = _history.RedoCount > 0;
                _redoButton.TooltipText = _history.RedoCount > 0
                    ? $"可重做 {_history.RedoCount} 步（Ctrl+Y）"
                    : "没有可重做的修改";
            }

            // 状态变化时打一条日志：按钮置灰这件事不截图就只能靠它自证
            if (_history.UndoCount != _lastLoggedUndo || _history.RedoCount != _lastLoggedRedo)
            {
                _lastLoggedUndo = _history.UndoCount;
                _lastLoggedRedo = _history.RedoCount;
                Debug.Log($"[NarrativePro] NP_HISTORY undo={_history.UndoCount} redo={_history.RedoCount}");
            }
        }

        private void OnAssetActivated(NarrativeAssetEntry entry)
        {
            if (entry == null) return;
            if (!ConfirmDiscard()) return;

            if (entry.IsQuest) LoadQuest(entry.Path);
            else LoadDialogue(entry.Path);

            // 内容命中行：打开资产后顺带把命中的节点选中，省得再去找
            if (!string.IsNullOrEmpty(entry.TargetId))
            {
                var kind = KindFromTag(entry.TargetKindTag);
                if (kind != NarrativeSelectionKind.None) SelectNode(kind, entry.TargetId);
            }
        }

        private static NarrativeSelectionKind KindFromTag(string tag)
        {
            switch ((tag ?? "").Trim().ToLowerInvariant())
            {
                case "state": return NarrativeSelectionKind.QuestState;
                case "branch": return NarrativeSelectionKind.QuestBranch;
                case "npc": return NarrativeSelectionKind.DialogueNpc;
                case "player": return NarrativeSelectionKind.DialoguePlayer;
                case "speaker": return NarrativeSelectionKind.Speaker;
                default: return NarrativeSelectionKind.None;
            }
        }

        protected override void OnSizeChanged()
        {
            base.OnSizeChanged();
            LayoutChrome();
        }

        /// <summary>快捷键：Ctrl+S 保存、Ctrl+Z 撤销、Ctrl+Y 或 Ctrl+Shift+Z 重做（焦点在文本框里时不抢键）。</summary>
        public override bool OnKeyDown(KeyboardKeys key)
        {
            if (Input.GetKey(KeyboardKeys.Control))
            {
                switch (key)
                {
                    case KeyboardKeys.S:
                        Save();
                        return true;
                    case KeyboardKeys.Z when Input.GetKey(KeyboardKeys.Shift):
                        Redo();
                        return true;
                    case KeyboardKeys.Z:
                        Undo();
                        return true;
                    case KeyboardKeys.Y:
                        Redo();
                        return true;
                }
            }

            return base.OnKeyDown(key);
        }

        private void LayoutChrome()
        {
            float width = Width;
            float height = Height;
            if (width < 40f || height < 40f) return;

            float toolbarWidth = width - Pad * 2f;
            int toolbarRows = ToolbarRowCount(toolbarWidth - 8f);
            float toolbarHeight = Math.Max(36f, toolbarRows * 30f + 8f);

            float issuesHeight = Mathf.Clamp(height * 0.16f, 76f, 140f);
            float browserWidth = Mathf.Clamp(width * 0.17f, 160f, 220f);
            float inspectorWidth = Mathf.Clamp(width * 0.25f, 220f, 300f);
            if (browserWidth + inspectorWidth + 260f > width)
            {
                browserWidth = Math.Max(130f, width * 0.13f);
                inspectorWidth = Math.Max(200f, width * 0.2f);
            }

            float contentHeight = Math.Max(80f, height - Pad * 2f - toolbarHeight - StatusHeight - issuesHeight - Gap * 3f);
            float graphWidth = Math.Max(120f, width - Pad * 2f - browserWidth - inspectorWidth - Gap * 2f);

            _toolbar.Bounds = new Rectangle(Pad, Pad, toolbarWidth, toolbarHeight);
            LayoutToolbar(toolbarRows, toolbarWidth - 8f);

            float contentTop = Pad + toolbarHeight + Gap;
            _browser.Bounds = new Rectangle(Pad, contentTop, browserWidth, contentHeight);
            _graph.Bounds = new Rectangle(Pad + browserWidth + Gap, contentTop, graphWidth, contentHeight);
            _inspector.Bounds = new Rectangle(width - Pad - inspectorWidth, contentTop, inspectorWidth, contentHeight);

            _issues.Bounds = new Rectangle(Pad, contentTop + contentHeight + Gap, width - Pad * 2f, issuesHeight);
            _statusBar.Bounds = new Rectangle(Pad, height - Pad - StatusHeight, width - Pad * 2f, StatusHeight);
            _statusLabel.Bounds = new Rectangle(8f, 0f, Math.Max(60f, _statusBar.Width - 340f), StatusHeight);
            _statusInfo.Bounds = new Rectangle(Math.Max(10f, _statusBar.Width - 330f), 0f, 320f, StatusHeight);

            _browser.Arrange();
            _inspector.Relayout();
            _graph.ConsumePendingFit();
        }

        private int ToolbarRowCount(float availableWidth)
        {
            float used = 8f;
            int rows = 1;
            foreach (var entry in _toolbarButtons)
            {
                if (used + entry.Width > availableWidth && used > 8f)
                {
                    rows++;
                    used = 8f;
                }
                used += entry.Width + 6f;
            }
            return rows;
        }

        private void LayoutToolbar(int rows, float availableWidth)
        {
            float x = 8f;
            float y = 6f;
            foreach (var entry in _toolbarButtons)
            {
                if (x + entry.Width > availableWidth && x > 8f)
                {
                    x = 8f;
                    y += 30f;
                }
                entry.Button.Bounds = new Rectangle(x, y, entry.Width, 26f);
                x += entry.Width + 6f;
            }
        }

        // ===================================================================
        // 资产摘要
        // ===================================================================

        private static string DescribeQuestAsset(string path)
        {
            try
            {
                var data = NarrativeAssetStore.LoadQuestData(path);
                string link = NarrativeAssetStore.QuestLinkLabel(data);
                return "状态 " + (data.States?.Count ?? 0) + " · 分支 " + (data.Branches?.Count ?? 0) +
                       (string.IsNullOrEmpty(link) ? "" : " · " + link);
            }
            catch
            {
                return "读取失败";
            }
        }

        private static string DescribeDialogueAsset(string path)
        {
            try
            {
                var data = NarrativeAssetStore.LoadDialogueData(path);
                return "NPC " + (data.NpcReplies?.Count ?? 0) + " · 选项 " + (data.PlayerReplies?.Count ?? 0);
            }
            catch
            {
                return "读取失败";
            }
        }
    }
}
#endif
