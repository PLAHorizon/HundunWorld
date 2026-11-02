using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using FlaxEngine;
using FlaxEngine.GUI;
using FlaxEditor;
using FlaxEditor.Windows;

namespace QuestEditor
{
    /// <summary>
    /// 编辑器内的任务窗口：可视化编辑任务内容 / 完成条件 / 奖励，
    /// 可关联 NPC、怪物、物品、场景物件作为发布者与触发者，奖励可指向任何物品模板，
    /// 并提供实时发布 / 下线 / 作废 / 删除（只删已作废的）四个操作。
    /// <para>数据权威在 SQL Server（<c>Game_HunduShijie_QuestTemplate</c>），本窗口经
    /// <see cref="QuestCli"/> 子进程读写，校验规则只在服务端那一份。</para>
    /// </summary>
    public sealed class QuestEditorWindow : EditorWindow
    {
        private const float ToolbarHeight = 62f;
        private const float LeftWidth = 300f;
        private const float ConsoleHeight = 130f;
        private const int MaxQuestRows = 80;
        private const int MaxObjectiveRows = 6;
        private const int MaxItemRows = 6;
        private const int MaxFavorRows = 5;
        private const float RowHeight = 26f;

        private static QuestEditorWindow _instance;

        private readonly string[] _questTypeNames = { "主线", "支线", "日常", "周常" };
        private readonly string[] _domainNames = { "通用", "金", "木", "水", "火", "土" };
        private readonly string[] _publishNames = { "下线（草稿）", "已发布" };
        private readonly string[] _entityKindNames = { "npc", "monster", "item", "prop", "zone" };
        private readonly string[] _objectiveKindNames = { "kill_monster", "kill_domain", "talk_npc", "collect_item", "use_item", "reach_zone", "interact_prop" };

        private readonly List<NpcEntry> _npcs = new List<NpcEntry>();
        private readonly List<NameEntry> _monsters = new List<NameEntry>();
        private readonly List<ItemEntry> _items = new List<ItemEntry>();
        private readonly List<QuestRow> _quests = new List<QuestRow>();

        private Panel _toolbar;
        private Panel _leftPanel;
        private Panel _formA;
        private Panel _formB;
        private Panel _inspector;
        private Label _inspectorHeader;
        private readonly Panel[] _objectiveCards = new Panel[MaxObjectiveRows];
        private readonly Panel[] _objectiveStrips = new Panel[MaxObjectiveRows];
        private readonly Panel[] _itemCards = new Panel[MaxItemRows];
        private readonly Panel[] _favorCards = new Panel[MaxFavorRows];
        private string _highlightSection = "";
        private Label _highlightLabel;
        private Panel _pickerPanel;
        private readonly Button[] _pickerRows = new Button[12];
        private TextBox _pickerTarget;
        private Func<List<(int Id, string Name)>> _pickerSource;
        private bool _pickerWriteId;
        private bool _pickerAppend;

        private sealed class PickerAnchor
        {
            public TextBox Box;
            public Button Toggle;
            public ContainerControl Container;
            public Func<List<(int Id, string Name)>> Source;
            public bool WriteId;
            public bool Append;
        }

        private readonly List<PickerAnchor> _pickerAnchors = new List<PickerAnchor>();
        private Button _addObjectiveButton;
        private Button _addItemButton;
        private Button _addFavorButton;
        private Button _searchItemButton;
        private Button _refreshHintButton;
        private Panel _consolePanel;

        private TextBox _cliPathBox;
        private Label _statusLabel;
        private TextBox _consoleBox;

        private readonly Button[] _questRowButtons = new Button[MaxQuestRows];

        private TextBox _idBox;
        private Label _versionLabel;
        private Label _narrativeHint;

        /// <summary>当前编辑的是"只住在 Redis 里的草稿"（未落库）。用来把版本号显示成"草稿"而不是沿用旧版本。</summary>
        private bool _isDraftOnly;

        // --- 草稿级撤销/重做（叙事资产工作流的那一环；命令式动作入栈，自由文本编辑暂不入栈）---
        private readonly System.Collections.Generic.List<string> _undoJson = new System.Collections.Generic.List<string>();
        private readonly System.Collections.Generic.List<string> _redoJson = new System.Collections.Generic.List<string>();
        private string _lastCommittedJson = "";

        /// <summary>正在跑的那个 CLI 操作（null = 空闲）。同一时刻只允许一个：回调会改同一批控件。</summary>
        private string _busyOperation;
        private TextBox _nameBox;
        private TextBox _descBox;
        private Dropdown _typeDrop;
        private TextBox _levelBox;
        private Dropdown _domainDrop;
        private TextBox _repeatBox;
        private Dropdown _publishDrop;
        private TextBox _prereqBox;

        private Dropdown _giverKindDrop;
        private TextBox _giverIdBox;
        private TextBox _giverZoneBox;
        private TextBox _giverSpawnBox;
        private Label _giverHint;

        private CheckBox _turnInSameCheck;
        private Dropdown _turnInKindDrop;
        private TextBox _turnInIdBox;
        private Label _turnInHint;

        private TextBox _expBox;
        private TextBox _goldBox;
        private TextBox _searchBox;
        private Label _searchHint;

        private readonly ObjectiveRow[] _objectiveRows = new ObjectiveRow[MaxObjectiveRows];
        private readonly ItemRow[] _itemRows = new ItemRow[MaxItemRows];
        private readonly FavorRow[] _favorRows = new FavorRow[MaxFavorRows];

        private QuestDraftModel _draft = new QuestDraftModel();
        private bool _suppress;
        private int _selectedQuestId;

        // --- 图优先：任务流程图（中心）+ 叙事图（同区域另一页）+ 检查器（右侧原表单）---
        private Panel _canvasHost;
        private QuestFlowView _flow;

        // --- 内嵌叙事图（NarrativePro 插件里的同一份面板，反射装配）---
        private Control _narrativeHost;
        private object _narrativePanel;
        private Button _modeButton;
        private Button _undoButton;
        private Button _redoButton;
        private bool _narrativeMode;
        private string _lastLayoutSignature = "";
        private string _lastInspectorSignature = "";
        private int _narrativeQuestId;

        /// <summary>模板 id → 叙事资产名（目录刷新时一并取；只用来在清单上标"已有叙事图"）。</summary>
        private Dictionary<int, string> _narrativeByTemplate = new Dictionary<int, string>();

        /// <summary>叙事编辑器要求选中的模板 id（目录还没刷完时挂起，刷新回调里补选）。</summary>
        private int _pendingSelectQuestId;

        /// <summary>一行完成条件的控件组。</summary>
        private sealed class ObjectiveRow
        {
            public Dropdown Kind;
            public TextBox Target;
            public TextBox Count;
            public TextBox Description;
            public Button Remove;
            public Button MoveUp;
            public Button MoveDown;
            public Label Hint;
        }

        /// <summary>一行物品奖励的控件组。</summary>
        private sealed class ItemRow
        {
            public TextBox Target;
            public TextBox Count;
            public Label Hint;
            public Button Remove;
        }

        /// <summary>一行好感奖励的控件组。</summary>
        private sealed class FavorRow
        {
            public Dropdown Domain;
            public TextBox Points;
            public Button Remove;
        }

        private sealed class NpcEntry { public int TemplateId; public string Name = ""; public string Title = ""; }
        private sealed class NameEntry { public int TemplateId; public string Name = ""; }
        private sealed class ItemEntry { public int TemplateId; public string Name = ""; public int ItemType; public string TypeName = ""; public int Quality; }
        /// <summary>清单里的一行。<c>Draft</c> = Redis 里有未落库的草稿（可能是新草稿，也可能是已发布任务的未发布改动）。</summary>
        private sealed class QuestRow
        {
            public int QuestId;
            public string QuestName = "";
            public int PublishState;
            public bool Valid = true;
            public bool Draft;

            /// <summary>草稿剩余有效期（小时）。只有"Draft 且库里还没有这一行"时才有意义。</summary>
            public double DraftTtlHours;
        }

        /// <summary>打开或聚焦任务窗口（必须在主线程调用）。</summary>
        public static void ShowWindow()
        {
            var editor = FlaxEditor.Editor.Instance;

            if (_instance != null)
            {
                _instance.FocusOrShow(FlaxEditor.GUI.Docking.DockState.DockRight);
                return;
            }

            _instance = new QuestEditorWindow(editor);
            editor.Windows.Open(_instance);
            _instance.Show(FlaxEditor.GUI.Docking.DockState.DockRight);
        }

        public QuestEditorWindow(FlaxEditor.Editor editor)
            : base(editor, false, ScrollBars.None)
        {
            Title = "任务编辑器";

            BuildToolbar();
            BuildQuestList();
            BuildFormA();
            BuildFormB();
            // 先建下拉弹层：BuildInspector 里的 AttachPicker 依赖它
            BuildPicker();
            BuildInspector();
            BuildConsole();

            // 图优先：中心是任务流程图，右侧是检查器（原表单），叙事图作为同一区域的另一页。
            BuildCanvas();

            SizeChanged += control => LayoutNow();
            LayoutNow();

            Log("任务编辑器就绪。数据权威在 SQL Server（Game_HunduShijie_QuestTemplate），读写走 Horizon.QuestCli 子进程。");
            Log("数据分层：**草稿只住 Redis**（72 小时、每次保存续期），**发布才落库到 SQL Server**。");
            Log("步骤：①刷新目录 ②左侧选一条任务或点\"新建\" ③改内容/条件/奖励 ④校验 ⑤保存（存草稿）⑥发布（落库并上线）。");

            RefreshCatalog();
        }

        // ── 构建 ────────────────────────────────────────────────

        private void BuildToolbar()
        {
            _toolbar = new Panel(ScrollBars.None) { Parent = this };

            _cliPathBox = new TextBox(false, 6f, 6f, 300f)
            {
                WatermarkText = "Horizon.QuestCli.exe 路径（留空=自动探测）",
                Parent = _toolbar,
            };

            var detectButton = new Button(312f, 4f, 60f, 24f) { Text = "探测", Parent = _toolbar };
            detectButton.Clicked += () =>
            {
                QuestEditor.QuestCli.ExePathOverride = _cliPathBox.Text?.Trim() ?? string.Empty;
                QuestEditor.QuestCli.ResetCache();
                var path = QuestEditor.QuestCli.DetectExePath();
                Log(string.IsNullOrEmpty(path) ? "没探测到 CLI，请先编一次 Horizon.QuestCli。" : "CLI: " + path);
            };

            var refreshButton = new Button(378f, 4f, 70f, 24f) { Text = "刷新目录", Parent = _toolbar };
            refreshButton.Clicked += RefreshCatalog;

            var newButton = new Button(454f, 4f, 56f, 24f) { Text = "新建", Parent = _toolbar };
            newButton.Clicked += CreateNewQuest;

            var validateButton = new Button(516f, 4f, 56f, 24f) { Text = "校验", Parent = _toolbar };
            validateButton.Clicked += ValidateDraft;

            var saveButton = new Button(578f, 4f, 56f, 24f) { Text = "保存", Parent = _toolbar };
            saveButton.Clicked += SaveDraft;

            var publishButton = new Button(640f, 4f, 56f, 24f) { Text = "发布", Parent = _toolbar };
            publishButton.Clicked += () => SetPublish(true);

            var offlineButton = new Button(702f, 4f, 56f, 24f) { Text = "下线", Parent = _toolbar };
            offlineButton.Clicked += () => SetPublish(false);

            var retireButton = new Button(764f, 4f, 56f, 24f) { Text = "作废", Parent = _toolbar };
            retireButton.Clicked += RetireQuest;

            // 删除：草稿（从未发布过）可直接删；库里已发布的**有效**行会先被拒（要求先作废）。
            // 这里不做置灰/分支判断，把理由交给服务端那句话，两边规则不会分叉。
            var deleteButton = new Button(826f, 4f, 56f, 24f) { Text = "删除", Parent = _toolbar };
            deleteButton.Clicked += DeleteQuest;

            // 与 NarrativePro 叙事资产编辑器互通：打开（没有则一键生成）这条模板对应的叙事图。
            var narrativeButton = new Button(888f, 4f, 64f, 24f) { Text = "叙事图", Parent = _toolbar };
            narrativeButton.Clicked += OpenNarrativeGraph;

            // 融合：任务表单与叙事图在同一个窗口里切换（叙事面板是 NarrativePro 的同一份实现）。
            _modeButton = new Button(958f, 4f, 110f, 24f) { Text = "切到叙事图", Parent = _toolbar };
            _modeButton.Clicked += ToggleNarrativeMode;

            // 保留原有能力：把叙事图放到独立窗口里编辑。
            _undoButton = new Button(1142f, 4f, 56f, 24f) { Text = "撤销", Parent = _toolbar };
            _undoButton.Clicked += UndoDraft;

            _redoButton = new Button(1204f, 4f, 56f, 24f) { Text = "重做", Parent = _toolbar };
            _redoButton.Clicked += RedoDraft;

            // 独立窗口入口已收掉：叙事图就在本窗口中心区（「叙事图 / 任务流程图」切换）。

            _statusLabel = new Label(6, 34, 820, 20) { Text = "未连接", Parent = _toolbar };
        }

        private void BuildQuestList()
        {
            _leftPanel = new Panel(ScrollBars.Vertical) { Parent = this };

            new Label(6f, 4f, 260f, 20f) { Text = "任务清单（●=已发布 ▣=有叙事图）", Parent = _leftPanel };

            for (var index = 0; index < MaxQuestRows; index++)
            {
                var captured = index;
                var button = new Button(6f, 28f + index * 26f, 270f, 24f)
                {
                    Text = string.Empty,
                    Parent = _leftPanel,
                    Visible = false,
                };
                button.Clicked += () => SelectQuestByRow(captured);
                _questRowButtons[index] = button;
            }
        }

        private void BuildFormA()
        {
            _formA = new Panel(ScrollBars.Vertical) { Parent = this };
            var y = 6f;

            new Label(6f, y, 220f, 20f) { Text = "① 基本信息", Parent = _formA };
            y += 24f;

            new Label(6f, y, 40f, 20f) { Text = "id", Parent = _formA };
            _idBox = new TextBox(false, 46f, y, 90f) { Parent = _formA, WatermarkText = "任务 id" };
            _versionLabel = new Label(142f, y, 150f, 20f) { Text = "v-", Parent = _formA };
            // 互通提示：这条模板到底有没有叙事图，写在 id 行下面，别等点按钮才知道。
            _narrativeHint = new Label(6f, y + 20f, 320f, 18f) { Text = "叙事图：—", Parent = _formA };
            y += 42f;

            new Label(6f, y, 44f, 20f) { Text = "名称", Parent = _formA };
            _nameBox = new TextBox(false, 52f, y, 250f) { Parent = _formA, WatermarkText = "任务名" };
            y += 26f;

            new Label(6f, y, 44f, 20f) { Text = "描述", Parent = _formA };
            _descBox = new TextBox(true, 52f, y, 300f) { Parent = _formA, Wrapping = TextWrapping.WrapWords, Height = 58f };
            y += 64f;

            new Label(6f, y, 44f, 20f) { Text = "类型", Parent = _formA };
            _typeDrop = NewDropdown(_formA, 52f, y, 110f, _questTypeNames);
            new Label(172f, y, 60f, 20f) { Text = "等级", Parent = _formA };
            _levelBox = new TextBox(false, 204f, y, 56f) { Parent = _formA, WatermarkText = "0" };
            y += 26f;

            new Label(6f, y, 54f, 20f) { Text = "五行域", Parent = _formA };
            _domainDrop = NewDropdown(_formA, 62f, y, 90f, _domainNames);
            new Label(162f, y, 66f, 20f) { Text = "可重复", Parent = _formA };
            _repeatBox = new TextBox(false, 228f, y, 56f) { Parent = _formA, WatermarkText = "0" };
            y += 26f;

            new Label(6f, y, 54f, 20f) { Text = "状态", Parent = _formA };
            _publishDrop = NewDropdown(_formA, 62f, y, 140f, _publishNames);
            y += 28f;

            new Label(6f, y, 300f, 20f) { Text = "② 发布者（人 / 物品 / 场景物件）", Parent = _formA };
            y += 24f;

            new Label(6f, y, 44f, 20f) { Text = "类别", Parent = _formA };
            _giverKindDrop = NewDropdown(_formA, 52f, y, 90f, _entityKindNames);
            new Label(150f, y, 44f, 20f) { Text = "模板", Parent = _formA };
            _giverIdBox = new TextBox(false, 196f, y, 66f) { Parent = _formA, WatermarkText = "模板id" };
            _giverHint = new Label(6f, y + 22f, 300f, 18f) { Text = "", Parent = _formA };
            y += 44f;

            new Label(6f, y, 44f, 20f) { Text = "区域", Parent = _formA };
            _giverZoneBox = new TextBox(false, 52f, y, 66f) { Parent = _formA, WatermarkText = "T12暂无消费方" };
            new Label(130f, y, 56f, 20f) { Text = "生成点", Parent = _formA };
            _giverSpawnBox = new TextBox(false, 188f, y, 66f) { Parent = _formA, WatermarkText = "T12暂无消费方" };
            y += 30f;

            new Label(6f, y, 300f, 20f) { Text = "③ 交付者", Parent = _formA };
            y += 24f;

            // Flax 的 CheckBox 没有 Text（它只是一个方框），文字要另配 Label。
            _turnInSameCheck = new CheckBox
            {
                Parent = _formA,
                Bounds = new Rectangle(6f, y, 22f, 20f),
            };
            new Label(34f, y, 160f, 20f) { Text = "沿用发布者", Parent = _formA };
            _turnInSameCheck.StateChanged += box =>
            {
                if (_suppress) return;
                var same = box.Checked;
                _turnInKindDrop.Enabled = !same;
                _turnInIdBox.Enabled = !same;
            };
            y += 26f;

            new Label(6f, y, 44f, 20f) { Text = "类别", Parent = _formA };
            _turnInKindDrop = NewDropdown(_formA, 52f, y, 90f, _entityKindNames);
            new Label(150f, y, 44f, 20f) { Text = "模板", Parent = _formA };
            _turnInIdBox = new TextBox(false, 196f, y, 66f) { Parent = _formA, WatermarkText = "模板id" };
            _turnInHint = new Label(6f, y + 22f, 300f, 18f) { Text = "", Parent = _formA };
            y += 44f;

            new Label(6f, y, 300f, 20f) { Text = "④ 前置任务（逗号分隔 id）", Parent = _formA };
            y += 22f;
            _prereqBox = new TextBox(false, 6f, y, 210f) { Parent = _formA, WatermarkText = "如 10001,10002" };

            var hintButton = new Button(222f, y - 22f, 84f, 22f) { Text = "刷新提示", Parent = _formA };
            hintButton.Clicked += RefreshAllHints;
        }

        /// <summary>
        /// 重算所有"这个 id 到底是谁"的提示文本。
        /// 刻意做成按钮而不是字段改变事件：Flax GUI 的 TextChanged/SelectedIndexChanged 委托签名
        /// 在这一版引擎里没有稳定文档，猜错就是编译失败——按钮这条路径不需要任何猜测。
        /// </summary>
        private void RefreshAllHints()
        {
            UpdateGiverHint();
            UpdateTurnInHint();

            for (var index = 0; index < MaxObjectiveRows; index++)
            {
                var row = _objectiveRows[index];
                if (row == null || !row.Kind.Visible) continue;
                var kind = _objectiveKindNames[Clamp(row.Kind.SelectedIndex, 0, _objectiveKindNames.Length - 1)];
                var entityKind = KindOfObjectiveForEntity(kind);
                var objectiveNote = string.Empty;
                var objectiveId = ResolveTemplateId(entityKind, row.Target.Text, out objectiveNote);
                row.Hint.Text = ObjectiveHint(kind) + " ｜ " + DescribeEntity(entityKind, objectiveId) +
                                (string.IsNullOrEmpty(objectiveNote) ? string.Empty : " ｜ " + objectiveNote);
            }

            for (var index = 0; index < MaxItemRows; index++)
            {
                var row = _itemRows[index];
                if (row == null || !row.Target.Visible) continue;
                var itemNote = string.Empty;
                var itemId = ResolveTemplateId("item", row.Target.Text, out itemNote);
                row.Hint.Text = DescribeItem(itemId) + (string.IsNullOrEmpty(itemNote) ? string.Empty : " ｜ " + itemNote);
            }

            AutoFillObjectiveDescriptions();
            RefreshFlow();
        }

        private static string KindOfObjectiveForEntity(string objectiveKind)
        {
            switch (objectiveKind)
            {
                case "kill_monster": return "monster";
                case "talk_npc": return "npc";
                case "collect_item":
                case "use_item": return "item";
                case "reach_zone": return "zone";
                case "interact_prop": return "prop";
                default: return "monster";
            }
        }

        private void BuildFormB()
        {
            _formB = new Panel(ScrollBars.Vertical) { Parent = this };
            var y = 6f;

            new Label(6f, y, 320f, 20f) { Text = "⑤ 完成条件（只列有事件源的类型会被放行）", Parent = _formB };
            y += 24f;

            for (var index = 0; index < MaxObjectiveRows; index++)
            {
                var row = new ObjectiveRow
                {
                    Kind = NewDropdown(_formB, 6f, y, 120f, _objectiveKindNames),
                    Target = new TextBox(false, 132f, y, 60f) { Parent = _formB, WatermarkText = "对象id" },
                    Count = new TextBox(false, 198f, y, 40f) { Parent = _formB, WatermarkText = "数量" },
                    Description = new TextBox(false, 244f, y, 210f) { Parent = _formB, WatermarkText = "给玩家看的文本" },
                };
                row.Hint = new Label(6f, y + 22f, 470f, 16f) { Parent = _formB, TextColor = new Color(0.75f, 0.75f, 0.7f), Text = "" };

                var captured = index;
                row.Remove = new Button(460f, y, 24f, 20f) { Text = "×", Parent = _formB };
                row.Remove.Clicked += () => RemoveObjective(captured);

                _objectiveRows[index] = row;
                row.Kind.Bounds = new Rectangle(6f, y, 120f, 22f);
                y += 42f;
            }

            var addObjective = new Button(6f, y, 90f, 22f) { Text = "＋添加目标", Parent = _formB };
            addObjective.Clicked += () => Grow(_objectiveRows, MaxObjectiveRows);
            y += 32f;

            new Label(6f, y, 320f, 20f) { Text = "⑥ 奖励：经验 / 铜币", Parent = _formB };
            y += 24f;
            new Label(6f, y, 44f, 20f) { Text = "经验", Parent = _formB };
            _expBox = new TextBox(false, 52f, y, 90f) { Parent = _formB, WatermarkText = "0" };
            new Label(152f, y, 44f, 20f) { Text = "铜币", Parent = _formB };
            _goldBox = new TextBox(false, 198f, y, 90f) { Parent = _formB, WatermarkText = "0" };
            y += 28f;

            new Label(6f, y, 320f, 20f) { Text = "物品奖励（可指向任何物品：装备/丹药/材料/宝石/任务物品/秘籍）", Parent = _formB };
            y += 22f;

            new Label(6f, y, 60f, 20f) { Text = "搜物品", Parent = _formB };
            _searchBox = new TextBox(false, 62f, y, 180f) { Parent = _formB, WatermarkText = "名称关键字（回车查 id）" };
            _searchBox.KeyDown += key =>
            {
                if (key == KeyboardKeys.Return) SearchItem();
            };
            var searchButton = new Button(250f, y, 50f, 22f) { Text = "查", Parent = _formB };
            searchButton.Clicked += SearchItem;
            _searchHint = new Label(6f, y + 22f, 470f, 16f) { Parent = _formB, TextColor = new Color(0.7f, 0.85f, 0.75f), Text = "" };
            y += 42f;

            for (var index = 0; index < MaxItemRows; index++)
            {
                var row = new ItemRow
                {
                    Target = new TextBox(false, 6f, y, 80f) { Parent = _formB, WatermarkText = "物品id" },
                    Count = new TextBox(false, 92f, y, 50f) { Parent = _formB, WatermarkText = "数量" },
                };
                row.Hint = new Label(150f, y, 300f, 20f) { Parent = _formB, TextColor = new Color(0.75f, 0.75f, 0.7f), Text = "-" };
                var captured = index;
                row.Remove = new Button(460f, y, 24f, 20f) { Text = "×", Parent = _formB };
                row.Remove.Clicked += () => RemoveItem(captured);
                _itemRows[index] = row;
                y += 26f;
            }

            var addItem = new Button(6f, y, 90f, 22f) { Text = "＋添加物品", Parent = _formB };
            addItem.Clicked += () => Grow(_itemRows, MaxItemRows);
            y += 32f;

            new Label(6f, y, 320f, 20f) { Text = "五行域好感奖励", Parent = _formB };
            y += 24f;

            for (var index = 0; index < MaxFavorRows; index++)
            {
                var row = new FavorRow
                {
                    Domain = NewDropdown(_formB, 6f, y, 80f, _domainNames),
                    Points = new TextBox(false, 96f, y, 60f) { Parent = _formB, WatermarkText = "点数" },
                };
                var captured = index;
                row.Remove = new Button(460f, y, 24f, 20f) { Text = "×", Parent = _formB };
                row.Remove.Clicked += () => RemoveFavor(captured);
                _favorRows[index] = row;
                row.Domain.Bounds = new Rectangle(6f, y, 80f, 22f);
                y += 26f;
            }

            var addFavor = new Button(6f, y, 90f, 22f) { Text = "＋添加好感", Parent = _formB };
            addFavor.Clicked += () => Grow(_favorRows, MaxFavorRows);
        }

        /// <summary>
        /// 把原 formA/formB 的字段搬进一个单列可滚动检查器。
        /// 旧布局把条件行按 490px 宽写死（描述框到 454、× 在 484），放进 400 宽的右栏就会被裁；
        /// 这里改为"标签 + 输入"紧凑成行、宽度自适应，控件与事件绑定一律保留。
        /// </summary>
        private void BuildInspector()
        {
            // 固定表头：滚动检查器时"当前是哪条任务"始终可见
            _inspectorHeader = new Label
            {
                Parent = this,
                Text = "（未选中任务）",
                TextColor = new Color(0.90f, 0.91f, 0.93f),
                VerticalAlignment = TextAlignment.Center,
                ClipText = true
            };

            _highlightLabel = new Label
            {
                Parent = this,
                Text = "",
                TextColor = new Color(0.98f, 0.78f, 0.38f),
                VerticalAlignment = TextAlignment.Center,
                ClipText = true
            };

            _inspector = new Panel(ScrollBars.Vertical) { Parent = this };

            Move(_idBox); Move(_versionLabel); Move(_narrativeHint);
            Move(_nameBox); Move(_descBox);
            Move(_typeDrop); Move(_levelBox);
            Move(_domainDrop); Move(_repeatBox);
            Move(_publishDrop); Move(_prereqBox);

            Move(_giverKindDrop); Move(_giverIdBox); Move(_giverHint);
            Move(_giverZoneBox); Move(_giverSpawnBox);
            Move(_turnInSameCheck); Move(_turnInKindDrop); Move(_turnInIdBox); Move(_turnInHint);

            // 条件 / 物品 / 好感各自装进一张"卡片"（有底色的容器），排版与点击区域更像卡片
            for (var index = 0; index < MaxObjectiveRows; index++)
            {
                var row = _objectiveRows[index];
                if (row == null) continue;
                var card = NewCard();
                _objectiveCards[index] = card;
                _objectiveStrips[index] = new Panel { Parent = card }; // 左侧类型色条
                Into(card, row.Kind); Into(card, row.Target); Into(card, row.Count);
                Into(card, row.Description); Into(card, row.Hint); Into(card, row.Remove);

                // 条件顺序 = 运行时判定顺序：给每张卡加上移/下移
                int capturedIndex = index;
                row.MoveUp = new Button { Parent = card, Text = "↑" };
                row.MoveUp.Clicked += () => MoveObjective(capturedIndex, -1);
                row.MoveDown = new Button { Parent = card, Text = "↓" };
                row.MoveDown.Clicked += () => MoveObjective(capturedIndex, 1);
            }
            Move(_expBox); Move(_goldBox);
            Move(_searchBox); Move(_searchHint);
            for (var index = 0; index < MaxItemRows; index++)
            {
                var row = _itemRows[index];
                if (row == null) continue;
                var card = NewCard();
                _itemCards[index] = card;
                Into(card, row.Target); Into(card, row.Count); Into(card, row.Hint); Into(card, row.Remove);
            }
            for (var index = 0; index < MaxFavorRows; index++)
            {
                var row = _favorRows[index];
                if (row == null) continue;
                var card = NewCard();
                _favorCards[index] = card;
                Into(card, row.Domain); Into(card, row.Points); Into(card, row.Remove);
            }

            _addObjectiveButton = MakeButton("+ 添加目标", () => Grow(_objectiveRows, MaxObjectiveRows));
            _addItemButton = MakeButton("+ 添加物品", () => Grow(_itemRows, MaxItemRows));
            _addFavorButton = MakeButton("+ 添加好感", () => Grow(_favorRows, MaxFavorRows));
            _forceTemplateText = new CheckBox { Parent = _inspector };
            _forceTemplateText.StateChanged += box =>
            {
                if (box.Checked)
                {
                    AutoFillObjectiveDescriptions();
                    RefreshFlow();
                    Log("已开启「强制按模板重写描述」：条件文本会按 类型+目标+数量 重写。");
                }
            };

            _searchItemButton = MakeButton("查", SearchItem);
            _refreshHintButton = MakeButton("刷新提示", RefreshAllHints);

            // 可搜索下拉：id 字段都能按中文名挑（选中后写名称，CollectDraftFromForm 会解析回 id）
            AttachPicker(_giverIdBox, _inspector, () => CatalogForKind(_entityKindDropValue(_giverKindDrop)));
            AttachPicker(_turnInIdBox, _inspector, () => CatalogForKind(_entityKindDropValue(_turnInKindDrop)));
            AttachPicker(_prereqBox, _inspector, () => _quests.Select(q => (q.QuestId, q.QuestName)).ToList(),
                writeId: true, append: true);

            for (var index = 0; index < MaxObjectiveRows; index++)
            {
                var row = _objectiveRows[index];
                if (row == null) continue;
                var capturedRow = row;
                AttachPicker(row.Target, _objectiveCards[index], () =>
                {
                    string kindName = _objectiveKindNames[Clamp(capturedRow.Kind.SelectedIndex, 0, _objectiveKindNames.Length - 1)];
                    return CatalogForKind(KindOfObjectiveForEntity(kindName));
                });
            }
            for (var index = 0; index < MaxObjectiveRows; index++)
            {
                var row = _objectiveRows[index];
                if (row == null) continue;
                AttachPicker(row.Count, _objectiveCards[index], CountChoices);
            }
            for (var index = 0; index < MaxItemRows; index++)
            {
                var row = _itemRows[index];
                if (row == null) continue;
                AttachPicker(row.Target, _itemCards[index], () => CatalogForKind("item"));
            }

            foreach (var card in _objectiveCards) if (card != null) card.Visible = false;
            foreach (var card in _itemCards) if (card != null) card.Visible = false;
            foreach (var card in _favorCards) if (card != null) card.Visible = false;

            // 没载入任务前不要把 6/6/5 行空模板摊开（检查器会被撑到 1300+px）
            foreach (var row in _objectiveRows) if (row != null) SetRowVisible(false, row.Kind, row.Target, row.Count, row.Description, row.Hint, row.Remove);
            foreach (var row in _itemRows) if (row != null) SetRowVisible(false, row.Target, row.Count, row.Hint, row.Remove);
            foreach (var row in _favorRows) if (row != null) SetRowVisible(false, row.Domain, row.Points, row.Remove);

            // 旧容器里的控件已搬空，只剩匿名标签；丢弃它们
            if (_formA != null) { _formA.Dispose(); _formA = null; }
            if (_formB != null) { _formB.Dispose(); _formB = null; }
        }

        private void Move(Control control)
        {
            if (control != null) control.Parent = _inspector;
        }

        private Panel NewCard()
        {
            return new Panel
            {
                Parent = _inspector,
                BackgroundColor = new Color(0.155f, 0.165f, 0.18f)
            };
        }

        private static void Into(Panel card, Control control)
        {
            if (control != null) control.Parent = card;
        }

        private Button MakeButton(string text, Action action)
        {
            var button = new Button { Parent = _inspector, Text = text };
            button.Clicked += action;
            return button;
        }

        /// <summary>控件 → 上一次自动生成的文本：作者手改过（文本≠这个值）就不再覆盖。</summary>
        /// <summary>强制按模板重写描述：默认关（只填空/只覆盖自己生成的），打开后手写文本也会被覆盖。</summary>
        private CheckBox _forceTemplateText;

        private readonly System.Collections.Generic.Dictionary<Control, string> _autoDescriptions =
            new System.Collections.Generic.Dictionary<Control, string>();

        private readonly System.Collections.Generic.Dictionary<string, Label> _captions =
            new System.Collections.Generic.Dictionary<string, Label>();

        private Label Caption(string key, string text)
        {
            if (!_captions.TryGetValue(key, out var label))
            {
                label = new Label
                {
                    Parent = _inspector,
                    TextColor = new Color(0.62f, 0.64f, 0.67f),
                    VerticalAlignment = TextAlignment.Center,
                    ClipText = true
                };
                _captions[key] = label;
            }

            label.Text = text;
            return label;
        }

        private sealed class SectionUi
        {
            public Label Title;
            public Panel Line;
        }

        private readonly System.Collections.Generic.Dictionary<string, SectionUi> _sections =
            new System.Collections.Generic.Dictionary<string, SectionUi>();

        private void SectionLabel(string key, string text, float x, float y, float width)
        {
            if (!_sections.TryGetValue(key, out var ui))
            {
                ui = new SectionUi
                {
                    Title = new Label
                    {
                        Parent = _inspector,
                        TextColor = new Color(0.98f, 0.78f, 0.38f),
                        VerticalAlignment = TextAlignment.Center,
                        ClipText = true
                    },
                    Line = new Panel
                    {
                        Parent = _inspector,
                        BackgroundColor = new Color(0.24f, 0.25f, 0.27f)
                    }
                };
                _sections[key] = ui;
            }

            bool highlighted = _highlightSection == key;
            ui.Title.Text = (highlighted ? "▶ " : "") + text;
            ui.Title.TextColor = highlighted ? new Color(0.98f, 0.78f, 0.38f) : new Color(0.80f, 0.64f, 0.34f);
            ui.Title.Bounds = new Rectangle(x, y, width, 20f);
            ui.Line.Bounds = new Rectangle(x, y + 21f, width, 1f);
        }

        /// <summary>按当前可见行重排检查器；设置尺寸或增删行后调用。</summary>
        public void ArrangeInspector()
        {
            if (_inspector == null) return;

            float width = _inspector.Width;
            if (width < 220f) return;

            float x = 10f;
            float w = width - 20f - 14f; // 给竖向滚动条留位置，右侧 ×/▾ 才不会被压住
            float y = 8f;
            const float lineH = 24f;
            const float gap = 3f;
            const float captionW = 62f;
            float fieldW = w - captionW - 6f;
            float halfW = (fieldW - 8f) / 2f;

            void Section(string key, string text)
            {
                y += 6f;
                SectionLabel(key, text, x, y, w);
                y += 26f;
            }

            void Row(string key, string caption, Control c, float h = lineH)
            {
                if (c == null) return;
                var label = Caption(key, caption);
                label.Bounds = new Rectangle(x, y, captionW, h);
                c.Bounds = new Rectangle(x + captionW + 6f, y, fieldW, h);
                c.Visible = true;
                y += h + gap;
            }

            void Wide(Control c, float h)
            {
                if (c == null) return;
                c.Bounds = new Rectangle(x, y, w, h);
                c.Visible = true;
                y += h + gap;
            }

            void PairLeft(string key, string caption, Control c)
            {
                if (c == null) return;
                var label = Caption(key, caption);
                label.Bounds = new Rectangle(x, y, captionW, lineH);
                c.Bounds = new Rectangle(x + captionW + 6f, y, halfW - captionW - 6f, lineH);
                c.Visible = true;
            }

            void PairRight(string key, string caption, Control c)
            {
                if (c == null) return;
                float startX = x + halfW + 8f;
                var label = Caption(key, caption);
                label.Bounds = new Rectangle(startX, y, captionW, lineH);
                c.Bounds = new Rectangle(startX + captionW + 6f, y, halfW - captionW - 6f, lineH);
                c.Visible = true;
            }

            void EndPair()
            {
                y += lineH + gap;
            }

            Section("basic", "① 基本信息");
            Row("id", "id", _idBox);
            if (_versionLabel != null)
            {
                _versionLabel.Bounds = new Rectangle(x + captionW + 106f, y - lineH - gap, 120f, lineH);
                _versionLabel.Visible = true;
            }
            if (_narrativeHint != null) Wide(_narrativeHint, 18f);
            Row("名称", "名称", _nameBox);
            Row("描述", "描述", _descBox, 54f);
            PairLeft("类型", "类型", _typeDrop);
            PairRight("等级", "等级", _levelBox);
            EndPair();
            PairLeft("五行域", "五行域", _domainDrop);
            PairRight("可重复", "可重复", _repeatBox);
            EndPair();
            Row("状态", "状态", _publishDrop);
            float prereqRowY = y;
            Row("前置", "前置任务", _prereqBox);
            if (_prereqBox != null)
            {
                _prereqBox.Bounds = new Rectangle(_prereqBox.Bounds.X, _prereqBox.Bounds.Y,
                    System.Math.Max(60f, _prereqBox.Bounds.Width - 24f - 88f), _prereqBox.Bounds.Height);
                SetToggle(_prereqBox, _prereqBox.Bounds);
            }
            if (_refreshHintButton != null)
            {
                _refreshHintButton.Bounds = new Rectangle(x + w - 84f, prereqRowY, 84f, lineH);
                _refreshHintButton.Visible = true;
            }

            Section("giver", "② 发布者（人 / 物品 / 场景物件）");
            PairLeft("发布者.类别", "类别", _giverKindDrop);
            PairRight("发布者.模板", "模板", _giverIdBox);
            EndPair();
            if (_giverIdBox != null)
            {
                // ▾ 贴在模板框右侧（框宽已扣掉 ▾ 的位置）
                _giverIdBox.Bounds = new Rectangle(_giverIdBox.Bounds.X, _giverIdBox.Bounds.Y, _giverIdBox.Bounds.Width - 24f, _giverIdBox.Bounds.Height);
                SetToggle(_giverIdBox, _giverIdBox.Bounds);
            }
            if (_giverHint != null) Wide(_giverHint, 18f);
            PairLeft("发布者.区域", "区域", _giverZoneBox);
            PairRight("发布者.生成点", "生成点", _giverSpawnBox);
            EndPair();

            Section("turnin", "③ 交还");
            if (_turnInSameCheck != null)
            {
                _turnInSameCheck.Bounds = new Rectangle(x, y, 22f, 20f);
                _turnInSameCheck.Visible = true;
                var sameLabel = Caption("交还.沿用", "沿用发布者");
                sameLabel.Bounds = new Rectangle(x + 28f, y, 160f, 20f);
                y += 24f;
            }
            PairLeft("交还.类别", "类别", _turnInKindDrop);
            PairRight("交还.模板", "模板", _turnInIdBox);
            EndPair();
            if (_turnInIdBox != null)
            {
                _turnInIdBox.Bounds = new Rectangle(_turnInIdBox.Bounds.X, _turnInIdBox.Bounds.Y, _turnInIdBox.Bounds.Width - 24f, _turnInIdBox.Bounds.Height);
                SetToggle(_turnInIdBox, _turnInIdBox.Bounds);
            }
            if (_turnInHint != null) Wide(_turnInHint, 18f);

            Section("objectives", "⑤ 完成条件");
            if (_forceTemplateText != null)
            {
                _forceTemplateText.Bounds = new Rectangle(x, y, 18f, 18f);
                _forceTemplateText.Visible = true;
                var forceLabel = Caption("objective.force", "强制按模板重写描述");
                forceLabel.Bounds = new Rectangle(x + 24f, y, 220f, 18f);
                y += 24f;
            }
            for (var index = 0; index < MaxObjectiveRows; index++)
            {
                var row = _objectiveRows[index];
                var card = _objectiveCards[index];
                if (card == null) continue;
                if (row?.Kind == null || !row.Kind.Visible)
                {
                    card.Visible = false;
                    continue;
                }

                const float cardHeight = 64f;
                const float removeW = 22f;
                const float kindW = 96f;
                const float targetW = 88f;

                card.Visible = true;
                card.Bounds = new Rectangle(x, y, w, cardHeight);

                // 列宽按"框 + 自己的 ▾"配对排：类型 96 · 对象 84+▾ · 数量 34+▾ · ×
                float targetX = 6f + kindW + 4f;
                float countX = targetX + targetW + 4f;
                row.Kind.Bounds = new Rectangle(6f, 4f, kindW, 22f);
                row.Target.Bounds = new Rectangle(targetX, 4f, targetW - 24f, 22f);
                row.Count.Bounds = new Rectangle(countX, 4f, 34f, 22f);
                row.Remove.Bounds = new Rectangle(w - removeW - 4f, 4f, removeW, 22f);
                if (row.MoveDown != null) row.MoveDown.Bounds = new Rectangle(w - removeW - 28f, 4f, 20f, 22f);
                if (row.MoveUp != null) row.MoveUp.Bounds = new Rectangle(w - removeW - 52f, 4f, 20f, 22f);
                row.Description.Bounds = new Rectangle(6f, 28f, w - 12f, 18f);
                row.Hint.Bounds = new Rectangle(6f, 47f, w - 12f, 15f);
                SetToggle(row.Target, row.Target.Bounds);
                SetToggle(row.Count, row.Count.Bounds);

                string kindName = _objectiveKindNames[Clamp(row.Kind.SelectedIndex, 0, _objectiveKindNames.Length - 1)];
                var strip = _objectiveStrips[index];
                if (strip != null)
                {
                    strip.Bounds = new Rectangle(0f, 0f, 3f, cardHeight);
                    strip.BackgroundColor = ObjectiveTone(kindName);
                }
                bool missingTarget = ResolveTemplateId(KindOfObjectiveForEntity(kindName), row.Target.Text, out _) <= 0;
                // 未填目标只是"警告"（黄），不是错误（红）
                row.Hint.TextColor = missingTarget ? new Color(0.95f, 0.75f, 0.30f) : new Color(0.75f, 0.75f, 0.7f);
                if (missingTarget) row.Hint.Text = "⚠ 未填目标 id：运行时不会放行";

                y += cardHeight + 6f;
            }
            if (_addObjectiveButton != null)
            {
                _addObjectiveButton.Bounds = new Rectangle(x, y, 96f, 22f);
                _addObjectiveButton.Visible = true;
                y += 28f;
            }

            Section("rewards", "⑥ 奖励：经验 / 铜币");
            PairLeft("奖励.经验", "经验", _expBox);
            PairRight("奖励.铜币", "铜币", _goldBox);
            EndPair();

            Section("items", "物品奖励");
            if (_searchBox != null)
            {
                _searchBox.Bounds = new Rectangle(x, y, System.Math.Max(80f, w - 60f), 22f);
                _searchBox.Visible = true;
                if (_searchItemButton != null)
                {
                    _searchItemButton.Bounds = new Rectangle(x + w - 54f, y, 54f, 22f);
                    _searchItemButton.Visible = true;
                }
                y += 26f;
            }
            if (_searchHint != null) Wide(_searchHint, 16f);
            // 物品奖励两列并排（一行一张卡太费纵向）
            float itemCellW = (w - 8f) / 2f;
            int itemPlaced = 0;
            float itemStartY = y;
            for (var index = 0; index < MaxItemRows; index++)
            {
                var row = _itemRows[index];
                var card = _itemCards[index];
                if (card == null) continue;
                if (row?.Target == null || !row.Target.Visible)
                {
                    card.Visible = false;
                    continue;
                }

                int column = itemPlaced % 2;
                int lineIndex = itemPlaced / 2;
                card.Visible = true;
                card.Bounds = new Rectangle(x + column * (itemCellW + 8f), itemStartY + lineIndex * 34f, itemCellW, 32f);

                row.Target.Bounds = new Rectangle(6f, 3f, 40f, 22f);
                row.Count.Bounds = new Rectangle(70f, 3f, 38f, 22f);
                row.Hint.Bounds = new Rectangle(112f, 3f, System.Math.Max(30f, itemCellW - 140f), 22f);
                row.Remove.Bounds = new Rectangle(itemCellW - 26f, 3f, 22f, 22f);
                SetToggle(row.Target, row.Target.Bounds);

                bool missingItem = ResolveTemplateId("item", row.Target.Text, out _) <= 0;
                row.Hint.TextColor = missingItem ? new Color(0.95f, 0.75f, 0.30f) : new Color(0.75f, 0.75f, 0.7f);

                itemPlaced++;
            }
            y = itemStartY + ((itemPlaced + 1) / 2) * 34f;
            if (_addItemButton != null)
            {
                _addItemButton.Bounds = new Rectangle(x, y, 96f, 22f);
                _addItemButton.Visible = true;
                y += 28f;
            }

            Section("favor", "五行域好感奖励");
            float favorCellW = (w - 8f) / 2f;
            int favorPlaced = 0;
            float favorStartY = y;
            for (var index = 0; index < MaxFavorRows; index++)
            {
                var row = _favorRows[index];
                if (row?.Domain == null || !row.Domain.Visible) continue;

                int column = favorPlaced % 2;
                int lineIndex = favorPlaced / 2;
                var favorCard = _favorCards[index];
                if (favorCard != null)
                {
                    favorCard.Visible = true;
                    favorCard.Bounds = new Rectangle(x + column * (favorCellW + 8f), favorStartY + lineIndex * 34f, favorCellW, 32f);
                }
                row.Domain.Bounds = new Rectangle(6f, 3f, 66f, 22f);
                row.Points.Bounds = new Rectangle(76f, 3f, 48f, 22f);
                row.Remove.Bounds = new Rectangle(favorCellW - 26f, 3f, 22f, 22f);
                favorPlaced++;
            }
            y = favorStartY + ((favorPlaced + 1) / 2) * 34f;
            if (_addFavorButton != null)
            {
                _addFavorButton.Bounds = new Rectangle(x, y, 96f, 22f);
                _addFavorButton.Visible = true;
                y += 28f;
            }

            // 布局自证：宽/内容高/可见行数（判"字段没被裁"靠它 + 截图）
            int visibleObjectives = 0, visibleItems = 0, visibleFavors = 0;
            foreach (var row in _objectiveRows) if (row?.Kind != null && row.Kind.Visible) visibleObjectives++;
            foreach (var row in _itemRows) if (row?.Target != null && row.Target.Visible) visibleItems++;
            foreach (var row in _favorRows) if (row?.Domain != null && row.Domain.Visible) visibleFavors++;

            int missingTargets = 0;
            foreach (var row in _objectiveRows)
            {
                if (row?.Kind == null || !row.Kind.Visible) continue;
                string kindName = _objectiveKindNames[Clamp(row.Kind.SelectedIndex, 0, _objectiveKindNames.Length - 1)];
                if (ResolveTemplateId(KindOfObjectiveForEntity(kindName), row.Target.Text, out _) <= 0) missingTargets++;
            }

            if (_highlightLabel != null)
            {
                _highlightLabel.Text = _highlightSection switch
                {
                    "giver" => "▶ 当前分组：② 发布者",
                    "turnin" => "▶ 当前分组：③ 交还",
                    "objectives" => "▶ 当前分组：⑤ 完成条件",
                    "rewards" => "▶ 当前分组：⑥ 奖励",
                    _ => ""
                };
            }

            string signature = $"{width:0}x{y:0}|obj={visibleObjectives}|item={visibleItems}|fav={visibleFavors}|miss={missingTargets}|hl={_highlightSection}";
            if (signature != _lastInspectorSignature)
            {
                _lastInspectorSignature = signature;
                Log("检查器：宽 " + width.ToString("0") + "px · 内容高 " + y.ToString("0") + "px · 条件 " +
                    visibleObjectives + " / 物品 " + visibleItems + " / 好感 " + visibleFavors +
                    " · 缺目标 " + missingTargets + " · 高亮=" + (_highlightSection.Length == 0 ? "无" : _highlightSection));
            }
        }

        // ── 可搜索下拉（中文名 ↔ 模板 id）────────────────────────

        private void BuildPicker()
        {
            _pickerPanel = new Panel
            {
                Parent = this,
                Visible = false,
                BackgroundColor = new Color(0.10f, 0.11f, 0.125f)
            };

            for (var index = 0; index < _pickerRows.Length; index++)
            {
                var captured = index;
                var button = new Button { Parent = _pickerPanel, Visible = false };
                button.Clicked += () => ChoosePickerRow(captured);
                _pickerRows[index] = button;
            }
        }

        /// <summary>给一个 id 输入框加"▾"：点开按当前文字过滤的中文名清单，选中即填名称（或 id）。</summary>
        private void AttachPicker(TextBox box, ContainerControl container, Func<List<(int Id, string Name)>> source,
            bool writeId = false, bool append = false)
        {
            if (box == null || _pickerPanel == null) return;

            var toggle = new Button { Parent = container ?? _inspector, Text = "▾" };
            toggle.Clicked += () => TogglePicker(box);
            _pickerAnchors.Add(new PickerAnchor
            {
                Box = box, Toggle = toggle, Container = container, Source = source,
                WriteId = writeId, Append = append
            });
        }

        private void TogglePicker(TextBox box)
        {
            if (_pickerPanel == null) return;

            if (_pickerPanel.Visible && _pickerTarget == box)
            {
                ClosePicker();
                return;
            }

            var anchor = _pickerAnchors.Find(item => item.Box == box);
            if (anchor == null) return;

            _pickerTarget = box;
            _pickerSource = anchor.Source;
            _pickerWriteId = anchor.WriteId;
            _pickerAppend = anchor.Append;

            RefreshPickerRows();

            int visible = 0;
            foreach (var row in _pickerRows) if (row.Visible) visible++;
            if (visible == 0) visible = 1;

            Float2 at = _inspector.Bounds.Location + _inspector.ViewOffset + anchor.Toggle.Bounds.Location;
            if (anchor.Container != null && anchor.Container != _inspector)
                at += anchor.Container.Bounds.Location;

            float panelWidth = System.Math.Max(260f, anchor.Box.Bounds.Width + 120f);
            float panelHeight = visible * 24f + 8f;
            _pickerPanel.Bounds = new Rectangle(at.X, at.Y + anchor.Toggle.Bounds.Height + 2f, panelWidth, panelHeight);

            // 弹层必须画在最上层：构造期它是父容器的第一个 child，会被后面的画布/检查器盖住，
            // 每次打开时把它挪到子列表末尾（IndexInParent 可写）。
            if (_pickerPanel.Parent is ContainerControl parent)
            {
                try { _pickerPanel.IndexInParent = parent.ChildrenCount - 1; }
                catch { /* 层级调整失败不影响内容 */ }
            }

            _pickerPanel.Visible = true;
        }

        private void RefreshPickerRows()
        {
            if (_pickerTarget == null || _pickerSource == null) return;

            string needle = (_pickerTarget.Text ?? string.Empty).Trim();
            var entries = _pickerSource() ?? new List<(int Id, string Name)>();

            if (needle.Length > 0)
            {
                var filtered = entries.Where(entry =>
                        entry.Id.ToString().Contains(needle) ||
                        (entry.Name ?? string.Empty).IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToList();
                if (filtered.Count > 0) entries = filtered;
            }

            int shown = 0;
            foreach (var row in _pickerRows)
            {
                if (shown >= entries.Count)
                {
                    row.Visible = false;
                    row.Text = "";
                    continue;
                }

                var entry = entries[shown];
                string label = string.IsNullOrWhiteSpace(entry.Name) || entry.Name == entry.Id.ToString()
                    ? entry.Id.ToString()
                    : entry.Name + " (" + entry.Id + ")";
                row.Text = label;
                row.Visible = true;
                shown++;
            }

            if (shown == 0)
            {
                _pickerRows[0].Text = "（目录为空，先点「刷新目录」，或直接填 id）";
                _pickerRows[0].Visible = true;
                shown = 1;
            }

            for (var index = 0; index < shown; index++)
                _pickerRows[index].Bounds = new Rectangle(4f, 4f + index * 24f, _pickerPanel.Width - 8f, 22f);
        }

        private void ChoosePickerRow(int index)
        {
            if (_pickerTarget == null) return;
            if (index < 0 || index >= _pickerRows.Length || !_pickerRows[index].Visible) return;

            string picked = _pickerRows[index].Text ?? string.Empty;
            if (picked.StartsWith("（", StringComparison.Ordinal))
            {
                ClosePicker();
                return;
            }

            // 列表显示中文，写回的是真实 id：取末尾括号里的数字；没有括号就整串当 id
            string idText = picked.Trim();
            int open = idText.LastIndexOf('(');
            int close = idText.LastIndexOf(')');
            if (open >= 0 && close > open)
                idText = idText.Substring(open + 1, close - open - 1).Trim();
            if (!int.TryParse(idText, out int parsedId))
            {
                ClosePicker();
                return;
            }
            idText = parsedId.ToString();

            if (_pickerAppend)
            {
                string existing = (_pickerTarget.Text ?? string.Empty).Trim().Trim(',');
                _pickerTarget.Text = existing.Length == 0 ? idText : existing + "," + idText;
            }
            else
            {
                _pickerTarget.Text = idText;
            }

            ClosePicker();
            AutoFillObjectiveDescriptions();
            RefreshAllHints();
        }

        private void ClosePicker()
        {
            if (_pickerPanel != null) _pickerPanel.Visible = false;
            _pickerTarget = null;
        }

        private void SetToggle(TextBox box, Rectangle boxBounds)
        {
            var anchor = _pickerAnchors.Find(item => item.Box == box);
            if (anchor != null) ArrangePickerToggle(anchor, boxBounds);
        }

        private void ArrangePickerToggle(PickerAnchor anchor, Rectangle boxBounds)
        {
            if (anchor?.Toggle == null) return;
            anchor.Toggle.Bounds = new Rectangle(boxBounds.Right + 4f, boxBounds.Y + 1f, 20f, 20f);
            anchor.Toggle.Visible = boxBounds.Width > 24f;
        }

        private void BuildConsole()
        {
            _consolePanel = new Panel(ScrollBars.None) { Parent = this };
            _consoleBox = new TextBox(true, 0f, 0f, 10f)
            {
                IsReadOnly = true,
                Wrapping = TextWrapping.WrapWords,
                Parent = _consolePanel,
            };
        }

        private Dropdown NewDropdown(ContainerControl parent, float x, float y, float width, string[] items)
        {
            var dropdown = new Dropdown
            {
                Parent = parent,
                Bounds = new Rectangle(x, y, width, 22f),
            };
            for (var index = 0; index < items.Length; index++)
                dropdown.AddItem(items[index]);
            dropdown.SelectedIndex = 0;
            return dropdown;
        }

        private void LayoutNow()
        {
            if (_toolbar == null || _statusLabel == null)
                return;

            var width = Width;
            var height = Height;
            var bodyHeight = System.Math.Max(160f, height - ToolbarHeight - ConsoleHeight - 8f);

            // 图优先：左侧任务清单 | 中间流程图/叙事图 | 右侧单列检查器（可滚动）
            var inspectorWidth = Mathf.Clamp(width * 0.32f, 420f, 560f);
            var canvasWidth = System.Math.Max(300f, width - LeftWidth - inspectorWidth - 30f);

            _toolbar.Bounds = new Rectangle(0f, 0f, width, ToolbarHeight);
            _leftPanel.Bounds = new Rectangle(0f, ToolbarHeight, LeftWidth, bodyHeight);

            if (_canvasHost != null)
            {
                _canvasHost.Bounds = new Rectangle(LeftWidth + 6f, ToolbarHeight, canvasWidth, bodyHeight);
                if (_flow != null) _flow.Bounds = new Rectangle(0f, 0f, canvasWidth, bodyHeight);
            }
            if (_narrativeHost != null && _narrativeHost.Parent == _canvasHost)
                _narrativeHost.Bounds = new Rectangle(0f, 0f, canvasWidth, bodyHeight);

            if (_inspectorHeader != null)
                _inspectorHeader.Bounds = new Rectangle(width - inspectorWidth - 6f, ToolbarHeight, inspectorWidth, 24f);
            if (_highlightLabel != null)
                _highlightLabel.Bounds = new Rectangle(width - inspectorWidth + 4f, ToolbarHeight + 24f, inspectorWidth - 10f, 18f);

            // 检查器吃满高度（控制台只占"清单 + 画布"那一行，不再吃掉检查器的竖向空间）
            float inspectorHeight = System.Math.Max(200f, height - ToolbarHeight - 44f - 6f);
            if (_inspector != null)
            {
                _inspector.Bounds = new Rectangle(width - inspectorWidth - 6f, ToolbarHeight + 44f, inspectorWidth, inspectorHeight);
                ArrangeInspector();
            }

            float consoleWidth = System.Math.Max(200f, width - inspectorWidth - 12f);
            _consolePanel.Bounds = new Rectangle(0f, height - ConsoleHeight, consoleWidth, ConsoleHeight);
            _consoleBox.Bounds = new Rectangle(4f, 4f, consoleWidth - 8f, ConsoleHeight - 8f);
            _statusLabel.Width = System.Math.Max(120f, width - 20f);

            string layoutSignature = $"{canvasWidth:0}x{bodyHeight:0}|inspector={inspectorWidth:0}|narrative={_narrativeMode}";
            if (layoutSignature != _lastLayoutSignature)
            {
                _lastLayoutSignature = layoutSignature;
                Log("布局：流程图 " + canvasWidth.ToString("0") + "px + 检查器 " + inspectorWidth.ToString("0") +
                    "px（中心区=" + (_narrativeMode ? "叙事图" : "任务流程图") + "）");
            }
        }

        // ── 图优先：流程图页与叙事图页 ─────────────────────────

        private void BuildCanvas()
        {
            _canvasHost = new Panel(ScrollBars.None) { Parent = this };
            _flow = new QuestFlowView { Parent = _canvasHost };
            _flow.NodeSelected += OnFlowNodeSelected;

            EnsureNarrativeHost();
            if (_narrativeHost != null)
            {
                _narrativeHost.Visible = false;
                _narrativeMode = false;
                if (_modeButton != null) _modeButton.Text = "切到叙事图";
            }
        }

        /// <summary>把当前表单内容投影成流程图（接取 → 条件 → 交还 → 奖励）；CollectDraftFromForm 是纯读。</summary>
        public void RefreshFlow()
        {
            if (_flow == null) return;

            int questId = ParseInt(_idBox.Text, 0);
            if (questId <= 0)
            {
                _flow.ShowFlow(0, "（未选中任务）", "左侧选一条任务，或点工具栏「新建」", "", "", "",
                    Array.Empty<(string, string, bool, Color)>(), "", "", "", "");
                if (_inspectorHeader != null) _inspectorHeader.Text = "（未选中任务）";
                return;
            }

            var draft = CollectDraftFromForm();
            string name = string.IsNullOrWhiteSpace(draft.QuestName) ? "（未命名）" : draft.QuestName;
            string state = draft.PublishState == 1 ? "已发布" : "草稿/下线";
            string subtitle = _questTypeNames[Clamp(draft.QuestType, 0, _questTypeNames.Length - 1)] + " · " +
                              _domainNames[Clamp(draft.WuxingDomain, 0, _domainNames.Length - 1)] + " · " + state +
                              " · Lv" + draft.LevelRequire +
                              (draft.PrerequisiteQuests != null && draft.PrerequisiteQuests.Count > 0
                                  ? " · 前置 " + string.Join(",", draft.PrerequisiteQuests)
                                  : "");

            string giver = DescribeEntity(draft.Giver.Kind, draft.Giver.TemplateId) +
                           (draft.Giver.ZoneId > 0 ? " ｜ 区域 " + draft.Giver.ZoneId : "");

            var objectives = new List<(string, string, bool, Color)>();
            foreach (var objective in draft.Objectives)
            {
                bool missing = objective.TemplateId <= 0;
                string target = missing ? "?" : DescribeEntity(KindOfObjectiveForEntity(objective.Kind), objective.TemplateId);
                string line = target + (objective.Count > 0 ? " ×" + objective.Count : "") +
                              (objective.Mandatory ? " · 必选" : " · 可选") +
                              (string.IsNullOrEmpty(objective.Description) ? "" : " · " + objective.Description);
                objectives.Add((objective.Kind, line, missing, ObjectiveTone(objective.Kind)));
            }

            string turnIn = draft.TurnIn == null
                ? "沿用发布者"
                : draft.TurnIn.Kind + " " + DescribeEntity(draft.TurnIn.Kind, draft.TurnIn.TemplateId);

            int items = draft.Rewards?.Items?.Count ?? 0;
            int favors = draft.Rewards?.Favor?.Count ?? 0;
            string rewards = "经验 " + (draft.Rewards?.Exp ?? 0) + " · 铜币 " + (draft.Rewards?.Gold ?? 0) +
                             (items > 0 ? " · 物品 " + items : "") + (favors > 0 ? " · 五行好感 " + favors : "");

            if (_inspectorHeader != null)
            {
                _inspectorHeader.Text = "● #" + questId + "  " + name + "   ·   " + state;
                // 状态色：已发布=绿，草稿/下线=黄，作废=红
                _inspectorHeader.TextColor = !draft.IsValid
                    ? new Color(0.95f, 0.42f, 0.42f)
                    : draft.PublishState == 1 ? new Color(0.55f, 0.85f, 0.60f) : new Color(0.95f, 0.78f, 0.35f);
            }

            _flow.ShowFlow(questId, "#" + questId + "  " + name, subtitle,
                "接取 · " + draft.Giver.Kind, giver, "接取",
                objectives,
                "交还 · " + turnIn, "", "奖励", rewards);
        }

        /// <summary>点流程图节点：只高亮检查器里的对应分组，不滚动（滚动会让整块 UI 跟着跳）。</summary>
        private void OnFlowNodeSelected(string key)
        {
            if (string.IsNullOrEmpty(key)) return;

            _highlightSection = key == "accept" ? "giver"
                : key == "turnin" ? "turnin"
                : key == "reward" ? "rewards"
                : key.StartsWith("obj:") ? "objectives"
                : "";

            ArrangeInspector();
            SetStatus(_highlightSection switch
            {
                "giver" => "接取：检查器已高亮 ② 发布者",
                "turnin" => "交还：检查器已高亮 ③ 交还",
                "objectives" => "条件：检查器已高亮 ⑤ 完成条件",
                "rewards" => "奖励：检查器已高亮 ⑥ 奖励",
                _ => "已选中节点"
            });
        }

        // ── 目录 ────────────────────────────────────────────────

        /// <summary>
        /// 跑一次 CLI 并把结果回灌到 UI：**发起在后台线程，处理在主线程**。
        /// <para>
        /// 同一时刻只允许一个调用在跑：并发启动多个子进程时"哪个结果先回来"不确定，
        /// 而这里的回调会去改同一批控件。第二次点击不排队、直接提示，避免作者以为点了没反应。
        /// </para>
        /// </summary>
        private void RunCli(string arguments, string stdin, Action<QuestEditor.QuestCliResult> onCompleted, string busyLabel = null)
        {
            if (_busyOperation != null)
            {
                Log("上一个操作还在进行（" + _busyOperation + "），等它结束再点。");
                SetStatus("执行中：" + _busyOperation + " …");
                return;
            }

            _busyOperation = string.IsNullOrEmpty(busyLabel) ? arguments : busyLabel;
            SetStatus("执行中：" + _busyOperation + " …");
            QuestEditor.QuestCli.RunAsync(arguments, stdin, result =>
            {
                _busyOperation = null;
                onCompleted(result);
            });
        }

        /// <summary>
        /// 刷新目录 = **一次异步子进程**同时取回"目录 + 草稿"。
        /// 原来是两次同步调用（`catalog` 再 `draft list`），每次都把编辑器主线程按住一遍；
        /// 现在合成一次、全程在后台线程，主线程只等一个回调。
        /// </summary>
        private void RefreshCatalog()
        {
            RunCli("catalog", null, ApplyCatalog, "刷新目录");
        }

        private void ApplyCatalog(QuestEditor.QuestCliResult result)
        {
            if (!result.Ok || !result.HasData)
            {
                SetStatus(result.Display);
                Log(result.Display);
                return;
            }

            _npcs.Clear();
            _monsters.Clear();
            _items.Clear();
            _quests.Clear();

            ReadArray(result.Data, "npcs", element =>
            {
                _npcs.Add(new NpcEntry
                {
                    TemplateId = ReadInt(element, "templateId"),
                    Name = ReadString(element, "name"),
                    Title = ReadString(element, "title"),
                });
            });

            ReadArray(result.Data, "monsters", element =>
                _monsters.Add(new NameEntry { TemplateId = ReadInt(element, "templateId"), Name = ReadString(element, "name") }));

            ReadArray(result.Data, "items", element =>
                _items.Add(new ItemEntry
                {
                    TemplateId = ReadInt(element, "templateId"),
                    Name = ReadString(element, "name"),
                    ItemType = ReadInt(element, "itemType"),
                    TypeName = ReadString(element, "typeName"),
                    Quality = ReadInt(element, "quality"),
                }));

            ReadArray(result.Data, "quests", element =>
                _quests.Add(new QuestRow
                {
                    QuestId = ReadInt(element, "questId"),
                    QuestName = ReadString(element, "questName"),
                    PublishState = ReadInt(element, "publishState"),
                    Valid = ReadBool(element, "valid", true),
                }));

            // 草稿并入清单：只住 Redis 的那些 id 在库里根本查不到，不并进来作者就是"存完就找不到"。
            // 数据和目录**同一次子进程**回（CLI 的 catalog 里带了 drafts），所以这里不再多发一次调用。
            var draftCount = 0;
            ReadArray(result.Data, "drafts", element =>
            {
                var draftId = ReadInt(element, "questId");
                var draftName = ReadString(element, "questName");
                var draftTtl = element.TryGetProperty("ttlHours", out var ttlElement) && ttlElement.TryGetDouble(out var parsedTtl)
                    ? parsedTtl : 0.0;
                draftCount++;

                var existing = _quests.FirstOrDefault(row => row.QuestId == draftId);
                if (existing != null)
                {
                    // 库里已有这条、Redis 里还有草稿 = 改了但没发布。标记出来，别让人以为改动已经生效。
                    existing.Draft = true;
                    return;
                }

                _quests.Add(new QuestRow
                {
                    QuestId = draftId,
                    QuestName = draftName,
                    PublishState = 0,
                    Valid = true,
                    Draft = true,
                    DraftTtlHours = draftTtl,
                });
            });

            var draftHint = ReadString(result.Data, "draftsHint");
            if (!string.IsNullOrEmpty(draftHint) && !draftHint.StartsWith("草稿层可用", StringComparison.Ordinal))
                Log("草稿层：" + draftHint);

            _quests.Sort((left, right) => left.QuestId.CompareTo(right.QuestId));
            // 清单上要标"已有叙事图"：叙事插件那一侧的映射，一次反射取回（纯文件扫描，不起 CLI）。
            _narrativeByTemplate = NarrativeLink.TryGetAssetMap();
            RebuildQuestRows();
            if (_narrativeByTemplate.Count > 0)
                Log("清单标记 ▣：" + _narrativeByTemplate.Count + " 条任务已有叙事图标记。");
            var text = "目录已刷新：" + _quests.Count + " 条任务 / " + _npcs.Count + " 个 NPC / " +
                       _monsters.Count + " 个怪物 / " + _items.Count + " 件物品；其中草稿 " + draftCount + " 条（仅 Redis，未落库）。";
            SetStatus(text);
            Log(text);
            RefreshFlow();
            ApplyPendingTemplateSelection();
        }

        /// <summary>叙事编辑器跳过来时目录可能还没刷完：把待选 id 记下，目录一到就补选。</summary>
        private void ApplyPendingTemplateSelection()
        {
            if (_pendingSelectQuestId <= 0) return;

            int wanted = _pendingSelectQuestId;
            _pendingSelectQuestId = 0;
            if (!SelectQuestById(wanted))
                Log("目录刷新后仍没有 id=" + wanted + " 的任务模板。");
        }

        private void RebuildQuestRows()
        {
            for (var index = 0; index < _questRowButtons.Length; index++)
            {
                var button = _questRowButtons[index];
                if (index >= _quests.Count)
                {
                    button.Visible = false;
                    continue;
                }

                var row = _quests[index];
                button.Visible = true;
                // ✎ = 有未落库的草稿（新草稿，或已发布任务的未发布改动）；● = 已发布；○ = 库里存在但下线。
                var mark = row.Draft ? "✎" : (row.PublishState == 1 ? "●" : "○");
                // ▣ = 叙事编辑器里已有对应叙事图（互通标记）
                if (_narrativeByTemplate.ContainsKey(row.QuestId)) mark = "▣" + mark;
                var suffix = row.Valid ? string.Empty : " [作废]";
                if (row.Draft && row.PublishState != 1)
                    suffix += "（草稿 " + row.DraftTtlHours.ToString("0.#") + "h）";
                button.Text = mark + " " + row.QuestId + " " + row.QuestName + suffix;
                button.Height = 24f;
                button.Y = 28f + index * 26f;
            }
        }

        private void SelectQuestByRow(int rowIndex)
        {
            if (rowIndex < 0 || rowIndex >= _quests.Count) return;
            var row = _quests[rowIndex];
            var questId = row.QuestId;

            // 有草稿就先看草稿——那才是作者手上正在改的东西；只库里有的才走 get。
            // 两个动词的 data 形状刻意做成一样（都回 {questId, json}），所以载入路径不用分叉。
            RunCli((row.Draft ? "draft get " : "get ") + questId, null, result =>
            {
                if (!result.Ok || !result.HasData)
                {
                    Log(result.Display);
                    return;
                }

                var json = ReadString(result.Data, "json");
                _draft = QuestDraftModel.FromJson(json);
                // 草稿还没落库，所以不能把它当成"库里这条"（否则后续下线/作废会打到一个不存在或过期的目标上）。
                _selectedQuestId = row.Draft ? 0 : questId;
                _isDraftOnly = row.Draft;
                LoadFormFrom(_draft);
                ResetUndoBaseline();
                SyncNarrativePanelForQuest(questId);
                if (row.Draft) _versionLabel.Text = "草稿（未落库）";
                Log("已载入" + (row.Draft ? "草稿 " : "任务 ") + questId + "（" + _draft.QuestName + "）" +
                    (row.Draft ? "，发布后才落库" : " v" + _draft.ContentVersion) + "。");
                SetStatus("已载入" + (row.Draft ? "草稿 " : "任务 ") + questId);
            }, "载入任务 " + questId);
        }

        /// <summary>供 NarrativePro 侧反射调用：按模板 id 选中一条任务；目录没加载完就先挂起、刷完再选。</summary>
        public bool SelectQuestById(int questId)
        {
            for (int index = 0; index < _quests.Count; index++)
            {
                if (_quests[index].QuestId != questId) continue;
                SelectQuestByRow(index);
                return true;
            }

            if (_quests.Count == 0)
            {
                // 首次唤起时目录是异步在刷的：记下目标，刷新回调里补选。
                _pendingSelectQuestId = questId;
                Log("任务目录还在加载，已记下待选 id=" + questId + "，刷新完成后自动选中。");
                SetStatus("等待目录加载后选中任务 " + questId);
                return true;
            }

            Log("任务目录里没有 id=" + questId + "（先点「刷新目录」；或它还没建）。");
            SetStatus("目录里没有任务 " + questId);
            return false;
        }

        /// <summary>把"这条模板有没有叙事图"写到 id 行下面（查的是叙事插件里的资产，不起 CLI 子进程）。</summary>
        private void UpdateNarrativeHint(int questId)
        {
            if (_narrativeHint == null) return;

            if (questId <= 0)
            {
                _narrativeHint.Text = "叙事图：先填 id";
                return;
            }

            string asset = NarrativeLink.TryGetAssetName(questId);
            _narrativeHint.Text = asset == null
                ? "叙事图：尚未创建（点「叙事图」一键生成骨架）"
                : "叙事图：" + asset + "（点「叙事图」打开）";
            Log("叙事图提示 " + questId + " -> " + _narrativeHint.Text);
        }

        // ── 融合：任务表单 ↔ 内嵌叙事图 ───────────────────────────

        /// <summary>中心区切页：任务流程图（默认）↔ 叙事图。右侧检查器（原表单）始终可见。</summary>
        private void ToggleNarrativeMode()
        {
            if (_narrativeHost == null && !EnsureNarrativeHost()) return;

            bool showNarrative = !(_narrativeHost.Visible);
            _narrativeHost.Visible = showNarrative;
            _narrativeMode = showNarrative;
            if (_flow != null) _flow.Visible = !showNarrative;
            if (_modeButton != null) _modeButton.Text = showNarrative ? "切到任务流程图" : "切到叙事图";

            LayoutNow();
            if (showNarrative) SyncNarrativePanelForQuest(ParseInt(_idBox.Text, 0));
            SetStatus(showNarrative ? "中心区：叙事图（同一模板的 JSON 流程）" : "中心区：任务流程图（接取→条件→交还→奖励）");
        }

        private bool EnsureNarrativeHost()
        {
            if (_narrativeHost != null) return true;

            var control = NarrativePanelLink.Create(out string error);
            if (control == null)
            {
                Log("叙事图不可用：" + error);
                SetStatus(error);
                return false;
            }

            _narrativeHost = control;
            _narrativePanel = control;
            _narrativeHost.Visible = false;
            if (_canvasHost != null) _canvasHost.AddChild(_narrativeHost);
            else AddChild(_narrativeHost);
            Log("叙事图已内嵌：图表/检查器/校验/撤销都来自 NarrativePro 的同一份实现。");
            return true;
        }

        /// <summary>把选中模板的叙事图载入内嵌面板；没有就显示"点生成"的空态。</summary>
        private void SyncNarrativePanelForQuest(int questId)
        {
            _narrativeQuestId = questId;
            if (_narrativeHost == null || !_narrativeHost.Visible) return;

            // 0 = 还没选中模板：别往面板里塞"模板 0"这种噪音，保持它当前内容即可
            if (questId <= 0) return;

            object opened = NarrativePanelLink.Invoke(_narrativePanel, "OpenForTemplate", out string error, questId);
            if (error != null)
            {
                Log("叙事图载入失败：" + error);
                return;
            }

            bool hasGraph = opened is bool value && value;
            Log(hasGraph ? "已载入模板 " + questId + " 的叙事图。" : "模板 " + questId + " 还没有叙事图（点「叙事图」生成骨架）。");
        }

        /// <summary>给当前模板生成/打开叙事图，并切到叙事图模式。</summary>
        private void OpenNarrativeGraph() => OpenNarrativeGraphFor(ParseInt(_idBox.Text, 0));

        /// <summary>按指定模板打开/生成叙事图（按钮与自动化验收共用同一实现）。</summary>
        public void OpenNarrativeGraphFor(int questId)
        {
            if (questId <= 0)
            {
                Log("先填任务 id（或从左侧选一条任务），再打开叙事图。");
                return;
            }

            if (!EnsureNarrativeHost()) return;

            object result = NarrativePanelLink.Invoke(_narrativePanel, "EnsureForTemplate", out string error, questId);
            if (error != null)
            {
                Log("生成叙事图失败：" + error);
                SetStatus(error);
                return;
            }
            if (result is string message && message.Length > 0)
            {
                Log("生成叙事图失败：" + message);
                SetStatus(message);
                return;
            }

            _narrativeByTemplate = NarrativeLink.TryGetAssetMap();
            RebuildQuestRows();
            UpdateNarrativeHint(questId);

            if (_narrativeHost == null) return;

            // 打开叙事图 = 中心区切到"叙事图"页（流程图让位，检查器不动）
            _narrativeHost.Visible = true;
            _narrativeMode = true;
            if (_flow != null) _flow.Visible = false;
            if (_modeButton != null) _modeButton.Text = "切到任务流程图";
            LayoutNow();
            SyncNarrativePanelForQuest(questId);

            Log("已为模板 " + questId + " 打开叙事图（没有就新建了骨架）。");
            SetStatus("叙事图 " + questId);
        }

        private void CreateNewQuest()
        {
            var suggested = 10000;
            foreach (var quest in _quests)
            {
                if (quest.QuestId >= suggested) suggested = quest.QuestId + 1;
            }

            _draft = QuestDraftModel.CreateNew(suggested);
            _selectedQuestId = 0;
            LoadFormFrom(_draft);
            ResetUndoBaseline();
            SyncNarrativePanelForQuest(suggested);
            Log("新建草稿 id=" + suggested + "（保存时按此 id 入库；IDENTITY 列由 CLI 开 IDENTITY_INSERT 写入）。");
        }

        // ── 表单 ↔ 草稿 ─────────────────────────────────────────

        private void LoadFormFrom(QuestDraftModel draft)
        {
            _suppress = true;

            _idBox.Text = draft.QuestId.ToString();
            _versionLabel.Text = "v" + draft.ContentVersion;
            UpdateNarrativeHint(draft.QuestId);
            _nameBox.Text = draft.QuestName;
            _descBox.Text = draft.Description;
            _typeDrop.SelectedIndex = Clamp(draft.QuestType, 0, _questTypeNames.Length - 1);
            _levelBox.Text = draft.LevelRequire.ToString();
            _domainDrop.SelectedIndex = Clamp(draft.WuxingDomain, 0, _domainNames.Length - 1);
            _repeatBox.Text = draft.RepeatLimit.ToString();
            _publishDrop.SelectedIndex = Clamp(draft.PublishState, 0, _publishNames.Length - 1);
            _prereqBox.Text = string.Join(",", draft.PrerequisiteQuests.ToArray());

            _giverKindDrop.SelectedIndex = IndexOfKind(draft.Giver.Kind);
            _giverIdBox.Text = draft.Giver.TemplateId.ToString();
            _giverZoneBox.Text = draft.Giver.ZoneId.ToString();
            _giverSpawnBox.Text = draft.Giver.SpawnPointId.ToString();
            UpdateGiverHint();

            var hasTurnIn = draft.TurnIn != null && draft.TurnIn.TemplateId > 0;
            _turnInSameCheck.Checked = !hasTurnIn;
            _turnInKindDrop.Enabled = hasTurnIn;
            _turnInIdBox.Enabled = hasTurnIn;
            _turnInKindDrop.SelectedIndex = IndexOfKind(draft.TurnIn?.Kind ?? "npc");
            _turnInIdBox.Text = (draft.TurnIn?.TemplateId ?? 0).ToString();
            UpdateTurnInHint();

            _expBox.Text = draft.Rewards.Exp.ToString();
            _goldBox.Text = draft.Rewards.Gold.ToString();

            for (var index = 0; index < MaxObjectiveRows; index++)
            {
                var row = _objectiveRows[index];
                var has = index < draft.Objectives.Count;
                SetRowVisible(has, row.Kind, row.Target, row.Count, row.Description, row.Remove, row.Hint);
                if (!has) continue;

                var objective = draft.Objectives[index];
                row.Kind.SelectedIndex = IndexOfObjectiveKind(objective.Kind);
                row.Target.Text = objective.TemplateId.ToString();
                row.Count.Text = objective.Count.ToString();
                row.Description.Text = objective.Description;
                row.Hint.Text = ObjectiveHint(objective.Kind);
            }

            for (var index = 0; index < MaxItemRows; index++)
            {
                var row = _itemRows[index];
                var has = index < draft.Rewards.Items.Count;
                SetRowVisible(has, row.Target, row.Count, row.Remove, row.Hint);
                if (!has) continue;

                var item = draft.Rewards.Items[index];
                row.Target.Text = item.TemplateId.ToString();
                row.Count.Text = item.Count.ToString();
                row.Hint.Text = DescribeItem(item.TemplateId);
            }

            for (var index = 0; index < MaxFavorRows; index++)
            {
                var row = _favorRows[index];
                var has = index < draft.Rewards.Favor.Count;
                SetRowVisible(has, row.Domain, row.Points, row.Remove);
                if (!has) continue;

                var favor = draft.Rewards.Favor[index];
                row.Domain.SelectedIndex = Clamp(favor.Domain, 0, _domainNames.Length - 1);
                row.Points.Text = favor.Points.ToString();
            }

            _suppress = false;
            ClosePicker();
            if (_inspector != null) _inspector.ViewOffset = Float2.Zero; // 切任务回到顶部，别让标题/首行落在视野外
            RefreshFlow();
            ArrangeInspector();
        }

        private QuestDraftModel CollectDraftFromForm()
        {
            var draft = new QuestDraftModel
            {
                QuestId = ParseInt(_idBox.Text, 0),
                QuestName = (_nameBox.Text ?? string.Empty).Trim(),
                Description = _descBox.Text ?? string.Empty,
                QuestType = _typeDrop.SelectedIndex,
                LevelRequire = ParseInt(_levelBox.Text, 0),
                WuxingDomain = _domainDrop.SelectedIndex,
                RepeatLimit = ParseInt(_repeatBox.Text, 0),
                PublishState = _publishDrop.SelectedIndex,
                IsValid = _draft != null ? _draft.IsValid : true,
                ContentVersion = _draft != null ? _draft.ContentVersion : 0,
                Giver = new EntityRefModel
                {
                    Kind = _entityKindDropValue(_giverKindDrop),
                    TemplateId = ResolveTemplateId(_entityKindDropValue(_giverKindDrop), _giverIdBox.Text, out _),
                    ZoneId = ParseInt(_giverZoneBox.Text, 0),
                    SpawnPointId = ParseInt(_giverSpawnBox.Text, 0),
                },
                PrerequisiteQuests = ParseIdList(_prereqBox.Text),
                Rewards = new RewardModel
                {
                    Exp = ParseLong(_expBox.Text, 0),
                    Gold = ParseLong(_goldBox.Text, 0),
                },
            };

            if (_turnInSameCheck.Checked == false)
            {
                var turnInKind = _entityKindDropValue(_turnInKindDrop);
                var turnInId = ResolveTemplateId(turnInKind, _turnInIdBox.Text, out _);
                int turnInZone;
                int turnInSpawn;
                InheritedTurnInScope(turnInKind, turnInId, out turnInZone, out turnInSpawn);
                draft.TurnIn = new EntityRefModel
                {
                    Kind = turnInKind,
                    TemplateId = turnInId,
                    // 这两格界面里没有控件：以前保存时新建一个 ref 就把 CLI/种子写过的范围限定清零了。
                    ZoneId = turnInZone,
                    SpawnPointId = turnInSpawn,
                };
            }

            for (var index = 0; index < MaxObjectiveRows; index++)
            {
                var row = _objectiveRows[index];
                if (!row.Kind.Visible) continue;
                var objectiveKind = _objectiveKindNames[Clamp(row.Kind.SelectedIndex, 0, _objectiveKindNames.Length - 1)];
                var objectiveTarget = ResolveTemplateId(KindOfObjectiveForEntity(objectiveKind), row.Target.Text, out _);
                draft.Objectives.Add(new ObjectiveModel
                {
                    Kind = objectiveKind,
                    TemplateId = objectiveTarget,
                    Count = ParseInt(row.Count.Text, 0),
                    Description = row.Description.Text ?? string.Empty,
                    // 界面上没有"可选目标"这个开关，所以不许把库里/CLI 写过的 mandatory=false 抹成 true：
                    // 同一条目标（类型 + 模板 id 没变）就沿用它原来的值，只有真新增的条目才按默认必填。
                    Mandatory = InheritedMandatory(objectiveKind, objectiveTarget),
                });
            }

            for (var index = 0; index < MaxItemRows; index++)
            {
                var row = _itemRows[index];
                if (!row.Target.Visible) continue;
                draft.Rewards.Items.Add(new RewardItemModel
                {
                    TemplateId = ResolveTemplateId("item", row.Target.Text, out _),
                    Count = ParseInt(row.Count.Text, 0),
                });
            }

            for (var index = 0; index < MaxFavorRows; index++)
            {
                var row = _favorRows[index];
                if (!row.Domain.Visible) continue;
                draft.Rewards.Favor.Add(new RewardFavorModel
                {
                    Domain = row.Domain.SelectedIndex,
                    Points = (int)ParseLong(row.Points.Text, 0),
                });
            }

            return draft;
        }

        private string _entityKindDropValue(Dropdown dropdown)
            => _entityKindNames[Clamp(dropdown.SelectedIndex, 0, _entityKindNames.Length - 1)];

        // ── 动作 ────────────────────────────────────────────────

        private void ValidateDraft()
        {
            var draft = CollectDraftFromForm();
            var problems = CheckKeyNamesSelfTest(draft);
            if (problems.Count > 0)
            {
                foreach (var problem in problems) Log("自检：" + problem);
            }

            RunCli("validate -", draft.ToJson(), result =>
            {
                Log(result.Display);
                if (result.HasData && result.Data.TryGetProperty("issues", out var issues) && issues.ValueKind == JsonValueKind.Array)
                {
                    foreach (var issue in issues.EnumerateArray())
                    {
                        Log("  [" + ReadString(issue, "code") + "/" + ReadString(issue, "level") + "] " +
                            ReadString(issue, "path") + "：" + ReadString(issue, "message"));
                    }
                }
                SetStatus(result.Ok ? "校验通过。" : "校验有错误，看下方明细。");
            }, "校验");
        }

        /// <summary>保存 = **只写 Redis 草稿**（SQL Server 一行都不写）。落库是"发布"那一下的事。</summary>
        private void SaveDraft()
        {
            var draft = CollectDraftFromForm();
            RunCli("draft save -", draft.ToJson(), result =>
            {
                Log(result.Display);

                if (result.Ok)
                {
                    _draft = draft;
                    _idBox.Text = _draft.QuestId.ToString();
                    // 版本号是**库**里的概念：草稿还没落库，所以这里只能标"草稿"，不能沿用上一个版本号，
                    // 否则作者会以为"v3 已经生效了"。
                    _isDraftOnly = true;
                    _versionLabel.Text = "草稿（未落库）";
                    SetStatus("已存为草稿 " + _draft.QuestId + "（Redis，72 小时内可发布，每次保存续期）");
                    RefreshCatalog();
                }
                else
                {
                    SetStatus("保存草稿失败：" + result.Message);
                }
            }, "保存草稿");
        }

        private void SetPublish(bool online)
        {
            var questId = ParseInt(_idBox.Text, 0);
            if (questId <= 0)
            {
                Log("先有 id 才能" + (online ? "发布" : "下线") + "（新草稿请先保存）。");
                return;
            }

            var draft = CollectDraftFromForm();
            draft.PublishState = online ? 1 : 0;

            if (online)
            {
                // 先把当前表单落成草稿再发布：否则"改完表格直接点发布"会去发 Redis 里那份**旧的**草稿，
                // 界面上看着是新的、线上跑的却是上一版。这一步把"所见"与"所发布"钉死。
                RunCli("draft save -", draft.ToJson(), staged =>
                {
                    if (!staged.Ok)
                    {
                        Log(staged.Display);
                        SetStatus("暂存草稿失败，未发布：" + staged.Message);
                        return;
                    }

                    PublishStagedDraft(draft, questId);
                }, "暂存草稿");
                return;
            }

            PublishStagedDraft(draft, questId);
        }

        /// <summary>发布/下线的共同后半段（此时草稿已确保在 Redis 里就位）。</summary>
        private void PublishStagedDraft(QuestDraftModel draft, int questId)
        {
            var online = draft.PublishState == 1;
            RunCli(online ? "publish " + questId : "unpublish " + questId, null, result =>
            {
                Log(result.Display);
                if (result.HasData && result.Data.TryGetProperty("warnings", out var warnings) && warnings.ValueKind == JsonValueKind.Array)
                {
                    foreach (var warning in warnings.EnumerateArray())
                        Log("  提示：" + warning.GetString());
                }

                if (result.Ok)
                {
                    _publishDrop.SelectedIndex = online ? 1 : 0;
                    _draft = draft;
                    if (online)
                    {
                        _isDraftOnly = false;
                        if (result.HasData) _draft.ContentVersion = ReadInt(result.Data, "contentVersion");
                        _versionLabel.Text = "v" + _draft.ContentVersion;
                    }
                    SetStatus(online ? "已落库并发布 " + questId + "（运行时最迟 10 秒生效，可调 IQuestManagerGrain.GetCatalogAsync(forceRefresh:true) 立刻生效；Redis 草稿已删除）"
                                     : "已实时关闭 " + questId + "：新接取当场停，已接的实例不受影响");
                    RefreshCatalog();
                }
                else
                {
                    SetStatus("操作被拒：" + result.Message);
                }
            }, online ? "发布" : "下线");
        }

        private void RetireQuest()
        {
            var questId = ParseInt(_idBox.Text, 0);
            if (questId <= 0)
            {
                Log("先有 id 才能作废。");
                return;
            }

            RunCli("retire " + questId, null, result =>
            {
                Log(result.Display);
                SetStatus(result.Message);
                if (result.Ok) RefreshCatalog();
            }, "作废");
        }

        /// <summary>
        /// 删除：**草稿（从未发布过）可以直接删**；库里已发布的有效行会被拒并要求先作废
        /// （拒绝与提示都由 CLI 给，这里只转发）。
        /// <para>
        /// 已接这条任务的角色不受影响：接取时名字/目标/奖励已抄进角色自己的状态，完成路径也不回查模板表。
        /// 受影响的只有"还没接的人"——他们从此接不到，这正是删除的意图。
        /// </para>
        /// </summary>
        private void DeleteQuest()
        {
            var questId = ParseInt(_idBox.Text, 0);
            if (questId <= 0)
            {
                Log("先有 id 才能删除。");
                return;
            }

            RunCli("delete " + questId, null, result =>
            {
                Log(result.Display);
                SetStatus(result.Message);
                RefreshCatalog();
            }, "删除");
        }

        private void SearchItem()
        {
            var keyword = (_searchBox.Text ?? string.Empty).Trim();
            if (keyword.Length == 0)
            {
                _searchHint.Text = "输入名称关键字再查。";
                return;
            }

            var builder = new StringBuilder();
            var hits = 0;
            foreach (var item in _items)
            {
                if (hits >= 6) break;
                if (item.Name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) < 0 &&
                    item.TypeName.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) < 0 &&
                    item.TemplateId.ToString() != keyword)
                    continue;
                builder.Append(item.TemplateId).Append(' ').Append(item.Name).Append('(').Append(item.TypeName).Append(")  ");
                hits++;
            }

            _searchHint.Text = hits == 0 ? "没查到：" + keyword : hits + " 命中：" + builder;
        }

        /// <summary>上移/下移一条完成条件：列表顺序就是运行时判定顺序。</summary>
        private void MoveObjective(int index, int delta)
        {
            var draft = CollectDraftFromForm();
            int target = index + delta;
            if (index < 0 || index >= draft.Objectives.Count || target < 0 || target >= draft.Objectives.Count) return;

            PushUndo();
            var item = draft.Objectives[index];
            draft.Objectives.RemoveAt(index);
            draft.Objectives.Insert(target, item);
            _draft = draft;
            LoadFormFrom(draft);
            CommitAfterMutation();
            Log("完成条件顺序：" + (index + 1) + " → " + (target + 1) + "（运行时按此顺序判定）");
        }

        private void RemoveObjective(int index)
        {
            PushUndo();
            var draft = CollectDraftFromForm();
            int before = draft.Objectives.Count;
            if (index < draft.Objectives.Count) draft.Objectives.RemoveAt(index);
            _draft = draft;
            LoadFormFrom(draft);
            CommitAfterMutation();
            Log("删除完成条件 #" + index + "：" + before + " → " + draft.Objectives.Count);
        }

        private void RemoveItem(int index)
        {
            PushUndo();
            var draft = CollectDraftFromForm();
            int before = draft.Rewards.Items.Count;
            if (index < draft.Rewards.Items.Count) draft.Rewards.Items.RemoveAt(index);
            _draft = draft;
            LoadFormFrom(draft);
            CommitAfterMutation();
            Log("删除奖励物品 #" + index + "：" + before + " → " + draft.Rewards.Items.Count);
        }

        private void RemoveFavor(int index)
        {
            PushUndo();
            var draft = CollectDraftFromForm();
            int before = draft.Rewards.Favor.Count;
            if (index < draft.Rewards.Favor.Count) draft.Rewards.Favor.RemoveAt(index);
            _draft = draft;
            LoadFormFrom(draft);
            CommitAfterMutation();
            Log("删除五行好感 #" + index + "：" + before + " → " + draft.Rewards.Favor.Count);
        }

        // ── 草稿撤销/重做 ───────────────────────────────────────

        private string SnapshotDraft()
        {
            try { return CollectDraftFromForm().ToJson(); }
            catch { return ""; }
        }

        /// <summary>载入/保存后重置基线：撤销只在这之后的结构改动里回退。</summary>
        private void ResetUndoBaseline()
        {
            _undoJson.Clear();
            _redoJson.Clear();
            _lastCommittedJson = SnapshotDraft();
            UpdateUndoButtons();
        }

        /// <summary>结构性改动（加/删条件、物品、好感）之前调用：把"改动前基线"压栈。</summary>
        private void PushUndo()
        {
            if (string.IsNullOrEmpty(_lastCommittedJson)) _lastCommittedJson = SnapshotDraft();
            if (string.IsNullOrEmpty(_lastCommittedJson))
            {
                Log("撤销基线建立失败（表单快照为空）");
                return;
            }

            _undoJson.Add(_lastCommittedJson);
            if (_undoJson.Count > 40) _undoJson.RemoveAt(0);
            _redoJson.Clear();
            UpdateUndoButtons();
            Log("撤销栈 +1（共 " + _undoJson.Count + " 步）");
        }

        /// <summary>结构改动完成后调用：基线推进到新状态（下次撤销就能回到这次之前）。</summary>
        private void CommitAfterMutation()
        {
            _lastCommittedJson = SnapshotDraft();
        }

        private void UndoDraft()
        {
            if (_undoJson.Count == 0)
            {
                Log("没有可撤销的改动（自由文本编辑暂不入栈）");
                return;
            }

            string target = _undoJson[_undoJson.Count - 1];
            _undoJson.RemoveAt(_undoJson.Count - 1);
            _redoJson.Add(SnapshotDraft());

            _draft = QuestDraftModel.FromJson(target);
            LoadFormFrom(_draft);
            _lastCommittedJson = target;
            UpdateUndoButtons();
            Log("已撤销（剩 " + _undoJson.Count + " 步可撤销）");
        }

        private void RedoDraft()
        {
            if (_redoJson.Count == 0)
            {
                Log("没有可重做的改动");
                return;
            }

            string target = _redoJson[_redoJson.Count - 1];
            _redoJson.RemoveAt(_redoJson.Count - 1);
            _undoJson.Add(SnapshotDraft());

            _draft = QuestDraftModel.FromJson(target);
            LoadFormFrom(_draft);
            _lastCommittedJson = target;
            UpdateUndoButtons();
            Log("已重做（剩 " + _redoJson.Count + " 步可重做）");
        }

        private void UpdateUndoButtons()
        {
            if (_undoButton != null) _undoButton.Enabled = _undoJson.Count > 0;
            if (_redoButton != null) _redoButton.Enabled = _redoJson.Count > 0;
        }

        private void Grow<T>(T[] rows, int max) where T : class
        {
            PushUndo();
            var draft = CollectDraftFromForm();
            var objectives = draft.Objectives;

            // 三个"＋添加"共用一条路径：谁在 _formB 里可见就往对应集合补一条空白行。
            if (rows == (object)_objectiveRows && objectives.Count < max)
                objectives.Add(new ObjectiveModel { Kind = "kill_monster", TemplateId = 0, Count = 1, Description = "" });
            else if (rows == (object)_itemRows && draft.Rewards.Items.Count < max)
                draft.Rewards.Items.Add(new RewardItemModel { TemplateId = 0, Count = 1 });
            else if (rows == (object)_favorRows && draft.Rewards.Favor.Count < max)
                draft.Rewards.Favor.Add(new RewardFavorModel { Domain = 1, Points = 10 });
            else
            {
                Log("这一类最多 " + max + " 行；要更多行请把它当结构问题反馈，而不是硬塞。");
                return;
            }

            _draft = draft;
            LoadFormFrom(draft);
            CommitAfterMutation();
            Log("新增一行后：条件 " + draft.Objectives.Count + " / 物品 " + draft.Rewards.Items.Count + " / 好感 " + draft.Rewards.Favor.Count);
        }

        // ── 小工具 ──────────────────────────────────────────────

        private void SetRowVisible(bool visible, params Control[] controls)
        {
            foreach (var control in controls)
            {
                if (control != null) control.Visible = visible;
            }
        }

        private void UpdateGiverHint()
        {
            string note;
            var giverKind = _entityKindDropValue(_giverKindDrop);
            var giverId = ResolveTemplateId(giverKind, _giverIdBox.Text, out note);
            _giverHint.Text = "→ " + DescribeEntity(giverKind, giverId) + (string.IsNullOrEmpty(note) ? string.Empty : " ｜ " + note);
        }

        private void UpdateTurnInHint()
        {
            string note;
            var turnInKind = _entityKindDropValue(_turnInKindDrop);
            var turnInId = ResolveTemplateId(turnInKind, _turnInIdBox.Text, out note);
            _turnInHint.Text = "→ " + DescribeEntity(turnInKind, turnInId) + (string.IsNullOrEmpty(note) ? string.Empty : " ｜ " + note);
        }

        // ---- 名字 → 模板 id 的输入便利层 ----------------------------------------------------
        // 规则与 Horizon.Game.Sim/Quests/QuestCatalogLookup.cs 一致：**数字优先 → 整名相等 → 唯一子串**，
        // 歧义一律不猜（返回 0 并把候选写进提示）。作者不必记住 700001 是哪块矿，但也不能因为
        // 打了"新手"就把奖励悄悄发成另一件东西。
        //
        // 为什么插件里再写一遍而不是直接引服务端程序集：Flax 插件的引用集由引擎决定，把服务端
        // 程序集拖进脚本域是这插件刻意避开的（见 QuestEditorEditor.Build.cs 的注释）。
        // 权威判定仍在服务端——这里解出来的 id 照旧过 Q013/Q014/Q015；两份规则的顺序由
        // Horizon.QuestContractTests 的名称解析漂移闸门守着。
        private int ResolveTemplateId(string kind, string text, out string note)
        {
            note = string.Empty;
            var needle = (text ?? string.Empty).Trim();
            if (needle.Length == 0) return 0;

            int direct;
            if (int.TryParse(needle, out direct) && direct > 0) return direct;

            var candidates = LookupCatalogFor(kind);
            var exact = candidates.Where(entry => string.Equals(entry.Name, needle, StringComparison.OrdinalIgnoreCase)).ToList();
            var matched = exact;
            if (matched.Count == 0)
            {
                matched = candidates
                    .Where(entry => (entry.Name ?? string.Empty).IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                    .OrderBy(entry => (entry.Name ?? string.Empty).Length)
                    .ThenBy(entry => entry.TemplateId)
                    .ToList();
            }

            if (matched.Count == 1) return matched[0].TemplateId;

            note = matched.Count == 0
                ? "『" + needle + "』在" + kind + "目录里没有匹配项（先点“刷新目录”，或直接填模板 id）"
                : "『" + needle + "』匹配到 " + matched.Count + " 项，填得更准或用 id：" +
                  string.Join("、", matched.Take(6).Select(entry => entry.Name + "(" + entry.TemplateId + ")"));
            return 0;
        }

        /// <summary>
        /// 按完成条件自动生成"给玩家看的文本"（击杀 3 只赤炎鹏 / 采集 2 个三七 / 与铁匠对话…）。
        /// 只在"空"或"还是上次自动生成的那句"时写；作者手改过的一律保留。
        /// </summary>
        private void AutoFillObjectiveDescriptions()
        {
            for (var index = 0; index < MaxObjectiveRows; index++)
            {
                var row = _objectiveRows[index];
                if (row?.Kind == null || row.Description == null || !row.Kind.Visible) continue;

                string kind = _objectiveKindNames[Clamp(row.Kind.SelectedIndex, 0, _objectiveKindNames.Length - 1)];
                int count = ParseInt(row.Count.Text, 0);

                string name;
                if (kind == "kill_domain")
                {
                    int domain = ResolveTemplateId("zone", row.Target.Text, out _);
                    if (domain <= 0) continue;
                    name = _domainNames[Clamp(domain - 1, 0, _domainNames.Length - 1)] + "行异兽";
                }
                else
                {
                    string entityKind = KindOfObjectiveForEntity(kind);
                    int id = ResolveTemplateId(entityKind, row.Target.Text, out _);
                    if (id <= 0) continue; // 还没选目标就不猜
                    name = DescribeEntity(entityKind, id);
                }

                string generated = ObjectiveTextTemplate(kind, name, count);
                string current = row.Description.Text ?? string.Empty;
                bool force = _forceTemplateText != null && _forceTemplateText.Checked;
                bool ours = force
                            || current.Trim().Length == 0
                            || (_autoDescriptions.TryGetValue(row.Description, out var last) && current == last);
                if (!ours) continue;

                row.Description.Text = generated;
                _autoDescriptions[row.Description] = generated;
                Log("自动生成描述（" + kind + "）：" + generated);
            }
        }

        private static string ObjectiveTextTemplate(string kind, string name, int count)
        {
            string times = count > 0 ? count.ToString() : "1";
            switch (kind)
            {
                case "kill_monster": return "击杀 " + times + " 只" + name;
                case "kill_domain": return "消灭 " + times + " 只" + name;
                case "talk_npc": return "与" + name + "对话";
                case "collect_item": return "采集 " + times + " 个" + name;
                case "use_item": return "使用 " + times + " 个" + name;
                case "reach_zone": return "抵达" + name;
                case "interact_prop": return "交互" + name;
                default: return name + " ×" + times;
            }
        }

        /// <summary>数量的常用取值：仍是数字输入框，▾ 只是给常见的 1..20 一个快选。</summary>
        private static List<(int Id, string Name)> CountChoices()
        {
            var result = new List<(int Id, string Name)>();
            for (int value = 1; value <= 20; value++) result.Add((value, "×" + value));
            return result;
        }

        /// <summary>条件卡左边一条 3px 类型色条：一眼分清击杀/采集/对话/区域…</summary>
        private void ApplyObjectiveTone(Panel card, string kindName)
        {
            if (card == null) return;
            card.BackgroundColor = new Color(0.155f, 0.165f, 0.18f);
        }

        private static Color ObjectiveTone(string kindName)
        {
            switch (kindName)
            {
                case "kill_monster": return new Color(0.90f, 0.45f, 0.42f);
                case "kill_domain": return new Color(0.45f, 0.62f, 0.92f);
                case "talk_npc": return new Color(0.76f, 0.58f, 0.94f);
                case "collect_item": return new Color(0.44f, 0.82f, 0.56f);
                case "use_item": return new Color(0.36f, 0.80f, 0.78f);
                case "reach_zone": return new Color(0.95f, 0.76f, 0.36f);
                default: return new Color(0.66f, 0.68f, 0.72f);
            }
        }

        private List<(int Id, string Name)> CatalogForKind(string kind)
        {
            var result = new List<(int Id, string Name)>();
            foreach (var entry in LookupCatalogFor(kind))
                result.Add((entry.TemplateId, DescribeEntity(kind, entry.TemplateId)));
            return result;
        }

        private List<NameEntry> LookupCatalogFor(string kind)
        {
            switch (kind)
            {
                case "npc":
                    return _npcs.Select(entry => new NameEntry { TemplateId = entry.TemplateId, Name = entry.Name }).ToList();
                case "monster":
                    return _monsters;
                case "item":
                    return _items.Select(entry => new NameEntry { TemplateId = entry.TemplateId, Name = entry.Name }).ToList();
                default:
                    // prop / zone 没有可枚举目录（物件与区域来自 DS 脚本生成），只能填 id。
                    return new List<NameEntry>();
            }
        }

        /// <summary>
        /// 目标条目的"必填"标记：界面没这个开关，所以**只能继承，不能改写**。
        /// 判据是"类型 + 模板 id 都没变"，即作者眼中还是同一条目标；真新增的条目才落到默认必填。
        /// </summary>
        private bool InheritedMandatory(string kind, int templateId)
        {
            var loaded = _draft == null ? null : _draft.Objectives;
            if (loaded == null) return true;
            foreach (var entry in loaded)
            {
                if (entry == null) continue;
                if (string.Equals(entry.Kind, kind, StringComparison.Ordinal) && entry.TemplateId == templateId) return entry.Mandatory;
            }
            return true;
        }

        /// <summary>交付者的区域/生成点限定：界面里没这两格控件，同一条交付者（类型 + 模板 id 未变）时原样带回去。</summary>
        private void InheritedTurnInScope(string kind, int templateId, out int zoneId, out int spawnPointId)
        {
            zoneId = 0;
            spawnPointId = 0;
            var loaded = _draft == null ? null : _draft.TurnIn;
            if (loaded == null) return;
            if (string.Equals(loaded.Kind, kind, StringComparison.Ordinal) && loaded.TemplateId == templateId)
            {
                zoneId = loaded.ZoneId;
                spawnPointId = loaded.SpawnPointId;
            }
        }

        private string DescribeEntity(string kind, int templateId)
        {
            if (templateId <= 0) return "未填模板 id";
            switch (kind)
            {
                case "npc":
                    foreach (var npc in _npcs)
                        if (npc.TemplateId == templateId) return npc.Name + (string.IsNullOrEmpty(npc.Title) ? "" : "·" + npc.Title);
                    return "NPC 目录里没有 " + templateId + "（保存会被拒）";
                case "monster":
                    foreach (var monster in _monsters)
                        if (monster.TemplateId == templateId) return monster.Name;
                    return "怪物目录里没有 " + templateId + "（保存会被拒）";
                case "item":
                    return DescribeItem(templateId);
                default:
                    return kind + ":" + templateId;
            }
        }

        private string DescribeItem(int templateId)
        {
            if (templateId <= 0) return "未填物品 id";
            foreach (var item in _items)
            {
                if (item.TemplateId == templateId) return item.Name + "·" + item.TypeName + "（品质 " + item.Quality + "）";
            }
            return "物品表里没有 " + templateId;
        }

        private static string ObjectiveHint(string kind)
        {
            switch (kind)
            {
                case "kill_monster": return "对象=怪物模板 id；有事件源";
                case "kill_domain": return "对象=五行域号 1..5；有事件源";
                case "talk_npc": return "对象=NPC 模板 id；玩家选中该 NPC 后按 G 触发，服务端按实体身份+距离裁决（校验会给 Q039 部署前提提示）";
                case "collect_item": return "尚无事件源：保存会被拒（等采集系统接上）";
                case "use_item": return "尚无事件源：保存会被拒（等使用物品的上报接上）";
                case "reach_zone": return "尚无事件源：保存会被拒（等区域到达上报接上）";
                case "interact_prop": return "对象=场景物件模板 id；与对话同一条服务端裁决（选中后按 G 触发），未重启到新链路时现场推不动";
                default: return "";
            }
        }

        /// <summary>
        /// 防"字段名漂移"的自检：把草稿序列化后，逐个键与已知的服务端键名对账。
        /// System.Text.Json 对不认识的键是静默丢弃的，改名字不会报错——只会让玩家看见空任务。
        /// </summary>
        private List<string> CheckKeyNamesSelfTest(QuestDraftModel draft)
        {
            var problems = new List<string>();
            try
            {
                using var document = JsonDocument.Parse(draft.ToJson());
                foreach (var property in document.RootElement.EnumerateObject())
                {
                    if (!QuestDraftModel.KnownTopLevelKeys.Contains(property.Name))
                        problems.Add("顶层键没人认识：" + property.Name);
                }

                if (document.RootElement.TryGetProperty("objectives", out var objectives) && objectives.ValueKind == JsonValueKind.Array)
                {
                    foreach (var objective in objectives.EnumerateArray())
                    {
                        if (objective.ValueKind != JsonValueKind.Object) continue;
                        foreach (var property in objective.EnumerateObject())
                        {
                            if (!QuestDraftModel.KnownObjectiveKeys.Contains(property.Name))
                                problems.Add("objectives 里键没人认识：" + property.Name);
                        }
                    }
                }
            }
            catch (JsonException ex)
            {
                problems.Add("序列化自检失败：" + ex.Message);
            }

            return problems;
        }

        private static int IndexOfKind(string kind)
        {
            for (var index = 0; index < _entityKindNameCache.Length; index++)
            {
                if (string.Equals(_entityKindNameCache[index], (kind ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase)) return index;
            }
            return 0;
        }

        private static readonly string[] _entityKindNameCache = { "npc", "monster", "item", "prop", "zone" };

        private static int IndexOfObjectiveKind(string kind)
        {
            for (var index = 0; index < _objectiveKindCache.Length; index++)
            {
                if (string.Equals(_objectiveKindCache[index], (kind ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase)) return index;
            }
            return 0;
        }

        private static readonly string[] _objectiveKindCache = { "kill_monster", "kill_domain", "talk_npc", "collect_item", "use_item", "reach_zone", "interact_prop" };

        private static int Clamp(int value, int min, int max) => value < min ? min : (value > max ? max : value);

        private static int ParseInt(string text, int fallback)
            => int.TryParse((text ?? string.Empty).Trim(), out var value) ? value : fallback;

        private static long ParseLong(string text, long fallback)
            => long.TryParse((text ?? string.Empty).Trim(), out var value) ? value : fallback;

        private static List<int> ParseIdList(string text)
        {
            var list = new List<int>();
            foreach (var part in (text ?? string.Empty).Split(','))
            {
                var trimmed = part.Trim();
                if (trimmed.Length == 0) continue;
                if (int.TryParse(trimmed, out var value) && value > 0 && !list.Contains(value)) list.Add(value);
            }
            return list;
        }

        private static void ReadArray(JsonElement root, string name, Action<JsonElement> handle)
        {
            if (!root.TryGetProperty(name, out var array) || array.ValueKind != JsonValueKind.Array) return;
            foreach (var element in array.EnumerateArray())
            {
                if (element.ValueKind == JsonValueKind.Object) handle(element);
            }
        }

        private static string ReadString(JsonElement element, string name)
            => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? (value.GetString() ?? string.Empty) : string.Empty;

        private static int ReadInt(JsonElement element, string name)
            => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var result) ? result : 0;

        private static bool ReadBool(JsonElement element, string name, bool fallback)
        {
            if (!element.TryGetProperty(name, out var value)) return fallback;
            if (value.ValueKind == JsonValueKind.True) return true;
            if (value.ValueKind == JsonValueKind.False) return false;
            return fallback;
        }

        private void SetStatus(string text)
        {
            if (_statusLabel != null) _statusLabel.Text = text;
        }

        private void Log(string text)
        {
            if (_consoleBox == null || string.IsNullOrEmpty(text)) return;
            _consoleBox.Text = (_consoleBox.Text ?? string.Empty) + Environment.NewLine + text;
            Debug.Log("[QuestEditor] " + text);
        }
    }
}
