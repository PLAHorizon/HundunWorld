using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;
using FlaxEngine;
using FlaxEngine.GUI;
using FlaxEditor;
using FlaxEditor.Windows;

namespace NpcEditor
{
    /// <summary>
    /// NPC 编辑器主窗口：编号与基础、服务位图、商人货架、技能教学与配方、对话菜单、作息、任务关系与击杀经验。
    /// <para>
    /// <b>数据权威在 SQL Server</b>，窗口经 <see cref="NpcCli"/> 子进程读写；窗口里的即时提示只是预览，
    /// <c>validate/save</c> 的回执才是结论。
    /// </para>
    /// <para>
    /// <b>为什么控件全部绝对定位</b>：Flax 构造期布局会在父尺寸仍为 0 时按 0 重算位置，
    /// 非 TopLeft 锚的控件会"凭空不显示"（本项目已踩过）。所有动态排布集中在 <see cref="LayoutNow"/>。
    /// </para>
    /// </summary>
    public sealed class NpcEditorWindow : EditorWindow
    {
        private const float ToolbarHeight = 64f;
        private const float LeftWidth = 230f;
        private const float TabBarHeight = 26f;
        private const float ConsoleHeight = 150f;
        private const float RowHeight = 24f;
        private const int MaxListRows = 40;
        private const int VendorRows = 4;
        private const int TrainerRows = 4;
        private const int CastRows = 4;
        private const int GossipRows = 4;
        private const int ScheduleRows = 3;
        private const int QuestRows = 3;

        private static NpcEditorWindow _instance;

        private static readonly string[] TabTitles = { "基础·外观", "服务位图", "商人", "教学·配方", "对话·作息", "任务·经验", "掉落", "任务链", "刷点·路线" };

        private const int LootRows = 4;

        /// <summary>路线点编辑行数（够画一条巡街方框；更长的路线走 CLI/导入）。</summary>
        private const int RoutePoints = 6;
        private static readonly string[] ReactionNames = { "友好（绿名）", "中立（黄名）", "敌对（红名）" };
        private static readonly string[] TierNames =
        {
            "0 站桩", "1 会转身", "2 会说话", "3 会踱步", "4 走路线", "5 自卫/追击", "6 呼叫增援", "7 精英（施法/残血跑）",
        };
        private static readonly string[] TriNames = { "跟立场", "强制不可打", "强制可打" };
        private static readonly string[] DomainNames = { "未定域", "金", "木", "水", "火", "土" };
        private static readonly string[] RankNames = { "普通", "精英", "稀有精英", "首领", "世界BOSS" };
        private static readonly string[] YesNoNames = { "否", "是" };
        private static readonly string[] ActionNames = { "仅文本", "开商店", "开训练", "开制作", "交任务", "下一层菜单" };
        private static readonly string[] PostureNames = { "站立", "坐", "工作", "持械" };
        private static readonly string[] RoleNames = { "发布者", "交付者" };
        private static readonly string[] ServiceKindNames = { "技能", "配方" };

        private readonly List<int> _listedIds = new List<int>();
        private readonly Button[] _listButtons = new Button[MaxListRows];
        private readonly Button[] _tabButtons = new Button[TabTitles.Length];

        private Panel _toolbar;
        private Panel _leftPanel;
        private Panel _tabBar;
        private Panel _formHost;
        private Panel _consolePanel;

        private Panel _formBase;
        private Panel _formService;
        private Panel _formVendor;
        private Panel _formTrainer;
        private Panel _formTalk;
        private Panel _formQuest;
        private Panel _formLoot;

        /// <summary>任务链页：只读文本（拓扑序 + 前置 + 交付者）。</summary>
        private Panel _formChain;
        private TextBox _chainBox;

        /// <summary>刷点·路线页（位置、刷新、巡逻线）。</summary>
        private Panel _formSpawn;
        private TextBox _spawnX, _spawnY, _spawnZ, _spawnYaw, _spawnRoute, _spawnAlive, _spawnRespawn;
        private TextBox _routeId, _routeName, _routePause;
        private Dropdown _routeMode, _routeNav;
        private readonly TextBox[] _nodeSeq = new TextBox[RoutePoints];
        private readonly TextBox[] _nodeX = new TextBox[RoutePoints];
        private readonly TextBox[] _nodeY = new TextBox[RoutePoints];
        private readonly TextBox[] _nodeZ = new TextBox[RoutePoints];
        private readonly TextBox[] _nodePause = new TextBox[RoutePoints];

        // 多行内容的行选择：刷点/路线/配方各一个行号 + 上一下一/新增。
        // 索引 == 行数 表示"新增"（Apply 时清空该段、Commit 时追加）。
        private TextBox _spawnIndexBox, _routeIndexBox, _recipeIndexBox;
        private Label _spawnRowsLabel, _routeRowsLabel, _recipeRowsLabel;

        private TextBox _cliPathBox;
        private TextBox _idBox;
        private TextBox _consoleBox;
        private Label _statusLabel;

        // 基础
        private TextBox _nameBox;
        private TextBox _titleBox;
        private TextBox _modelKeyBox;
        private TextBox _prefabBox;
        private TextBox _scaleBox;
        private TextBox _levelBox;
        private TextBox _levelVarBox;
        private TextBox _walkSpeedBox;
        private TextBox _runSpeedBox;
        private TextBox _alertBox;
        private TextBox _gossipGroupBox;
        private TextBox _killCreditBox;
        private Dropdown _reactionDrop;
        private Dropdown _tierDrop;
        private Dropdown _triDrop;
        private Dropdown _domainDrop;
        private Dropdown _rankDrop;
        private readonly Dropdown[] _aiOn = new Dropdown[NpcAiBitsUi.Names.Length];
        private readonly Dropdown[] _aiOff = new Dropdown[NpcAiBitsUi.Names.Length];

        /// <summary>回填草稿期间为 true：此时不执行两列互斥（库里已有的冲突要原样显示，由校验报出来）。</summary>
        private bool _applyingAiBits;

        // 服务位图
        private readonly Dropdown[] _serviceDrops = new Dropdown[NpcServiceBitsUi.Names.Length];
        private Label _serviceSummary;

        // 商人
        private readonly TextBox[] _vendorSlot = new TextBox[VendorRows];
        private readonly TextBox[] _vendorItem = new TextBox[VendorRows];
        private readonly TextBox[] _vendorBuy = new TextBox[VendorRows];
        private readonly TextBox[] _vendorSell = new TextBox[VendorRows];
        private readonly TextBox[] _vendorStock = new TextBox[VendorRows];
        private readonly TextBox[] _vendorRestock = new TextBox[VendorRows];

        // 教学
        private readonly TextBox[] _trainerSkill = new TextBox[TrainerRows];
        private readonly Dropdown[] _trainerKind = new Dropdown[TrainerRows];
        private readonly TextBox[] _trainerCost = new TextBox[TrainerRows];
        private readonly TextBox[] _trainerSeq = new TextBox[TrainerRows];

        /// <summary>配方编辑（第一条配方；多配方走 CLI/导入）。</summary>
        private const int RecipeMaterialRows = 3;
        private TextBox _recipeIdBox, _recipeNameBox, _recipeProductBox, _recipeCostBox, _recipeStationBox, _recipeSkillBox;
        private readonly TextBox[] _recipeItem = new TextBox[RecipeMaterialRows];
        private readonly TextBox[] _recipeCount = new TextBox[RecipeMaterialRows];
        private readonly TextBox[] _recipeChance = new TextBox[4];
        private readonly TextBox[] _recipeGap = new TextBox[4];

        // 技能挂载（施法）：面板只编前六列，其余字段保存时沿用载入值。
        private readonly TextBox[] _castSkill = new TextBox[CastRows];
        private readonly Dropdown[] _castSlot = new Dropdown[CastRows];
        private readonly TextBox[] _castChance = new TextBox[CastRows];
        private readonly TextBox[] _castMs = new TextBox[CastRows];
        private readonly TextBox[] _castCd = new TextBox[CastRows];
        private readonly TextBox[] _castRange = new TextBox[CastRows];
        private Label _castHiddenLabel;
        // 最后一档在运行期还没有"是否受控"的判据（内核只判在战斗中），标签里写明，别让作者以为是 CC 触发。
        private static readonly string[] CastSlotNames = { "战斗", "非战斗", "残血", "受控(≈战斗)" };

        // 对话
        private readonly TextBox[] _gossipGroup = new TextBox[GossipRows];
        private readonly TextBox[] _gossipSeq = new TextBox[GossipRows];
        private readonly Dropdown[] _gossipAction = new Dropdown[GossipRows];
        private readonly TextBox[] _gossipArg = new TextBox[GossipRows];
        private readonly TextBox[] _gossipMin = new TextBox[GossipRows];
        private readonly TextBox[] _gossipMax = new TextBox[GossipRows];
        private readonly TextBox[] _gossipText = new TextBox[GossipRows];

        // 作息
        private readonly TextBox[] _schedFrom = new TextBox[ScheduleRows];
        private readonly TextBox[] _schedTo = new TextBox[ScheduleRows];
        private readonly TextBox[] _schedRoute = new TextBox[ScheduleRows];
        private readonly Dropdown[] _schedPosture = new Dropdown[ScheduleRows];

        // 任务 + 经验
        private readonly TextBox[] _questId = new TextBox[QuestRows];
        private readonly Dropdown[] _questRole = new Dropdown[QuestRows];
        // 掉落（一行绑定 + 至多 4 件）
        private TextBox _lootRollBox;
        private TextBox _lootMoneyMinBox;
        private TextBox _lootMoneyMaxBox;

        /// <summary>绑定级掉落条件与任务 id（值域见 <see cref="NpcLootConditionUi"/>；0 = 无条件）。</summary>
        private Dropdown _lootBindingCondition;
        private TextBox _lootBindingQuest;
        private readonly TextBox[] _lootItem = new TextBox[LootRows];
        private readonly TextBox[] _lootChance = new TextBox[LootRows];
        private readonly TextBox[] _lootMin = new TextBox[LootRows];
        private readonly TextBox[] _lootMax = new TextBox[LootRows];

        /// <summary>逐件掉落条件与任务 id（值域见 <see cref="NpcLootConditionUi"/>；-1 = 继承绑定）。</summary>
        private readonly Dropdown[] _lootCondition = new Dropdown[LootRows];
        private readonly TextBox[] _lootQuest = new TextBox[LootRows];
        private Label _lootHiddenLabel;

        private TextBox _xpNpcLevelBox;
        private TextBox _xpPlayerLevelBox;
        private Label _xpResult;

        private int _activeTab;
        private bool _busy;
        private NpcDraftModel _draft = new NpcDraftModel();

        /// <summary>打开或聚焦窗口（必须主线程调用）。</summary>
        public static void ShowWindow()
        {
            var editor = FlaxEditor.Editor.Instance;
            if (_instance != null)
            {
                _instance.FocusOrShow(FlaxEditor.GUI.Docking.DockState.DockRight);
                return;
            }

            _instance = new NpcEditorWindow(editor);
            editor.Windows.Open(_instance);
            _instance.Show(FlaxEditor.GUI.Docking.DockState.DockRight);
        }

        public NpcEditorWindow(FlaxEditor.Editor editor)
            : base(editor, false, ScrollBars.None)
        {
            Title = "NPC 编辑器";

            BuildToolbar();
            BuildList();
            BuildTabBar();
            BuildForms();
            BuildConsole();

            SelectTab(0);
            SizeChanged += control => LayoutNow();
            LayoutNow();

            Log("NPC 编辑器就绪。先编一次 Horizon.NpcCli（dotnet build Horizon.NpcCli/Horizon.NpcCli.csproj），"
                + "并确认 SQL 迁移 021 已执行；然后点『刷新目录』。");
        }

        // ---------------- 构造 ----------------

        private void BuildToolbar()
        {
            _toolbar = new Panel(ScrollBars.None) { Parent = this };

            _cliPathBox = new TextBox(false, 6f, 6f, 300f)
            {
                Parent = _toolbar,
                WatermarkText = "Horizon.NpcCli.exe 路径（留空=自动探测）",
            };

            var detect = MakeButton(_toolbar, 312f, 4f, 54f, "探测", () =>
            {
                NpcCli.ResetCache();
                NpcCli.ExePathOverride = _cliPathBox.Text ?? string.Empty;
                var path = NpcCli.DetectExePath();
                Log(path == null ? "没探测到 Horizon.NpcCli.exe。" : "CLI: " + path);
            });

            var refresh = MakeButton(_toolbar, 372f, 4f, 82f, "刷新目录", RefreshCatalog);
            var load = MakeButton(_toolbar, 460f, 4f, 54f, "加载", LoadNpc);
            var validate = MakeButton(_toolbar, 520f, 4f, 54f, "校验", ValidateDraft);
            var save = MakeButton(_toolbar, 580f, 4f, 54f, "保存", SaveDraft);
            var publish = MakeButton(_toolbar, 640f, 4f, 54f, "发布", PublishNpc);
            var indicator = MakeButton(_toolbar, 706f, 4f, 96f, "头顶指示器", PreviewIndicator);
            var xp = MakeButton(_toolbar, 808f, 4f, 76f, "经验试算", PreviewXp);

            _idBox = new TextBox(false, 6f, 34f, 100f) { Parent = _toolbar, WatermarkText = "NPC 编号" };
            _statusLabel = new Label(544f, 36f, 400f, 20f) { Parent = _toolbar, Text = "未加载" };

            // 新建：先向 CLI 要一个号段内的空号（1–3 保留，不给分配）。
            // 两个按钮而不是"新建 + 立场下拉"：新建时就要决定"这是服务型还是敌对型"，
            // 因为它决定了号段、默认立场与默认 AI 档，事后改号会让任务/掉落引用一起搬。
            var newService = MakeButton(_toolbar, 360f, 32f, 84f, "新建服务", () => NewNpc(false));
            var newHostile = MakeButton(_toolbar, 450f, 32f, 84f, "新建敌对", () => NewNpc(true));

            // 让编译器看见引用（按钮建出来就要挂在工具条上，否则布局期找不到父）。
            detect.Parent = _toolbar;
            refresh.Parent = _toolbar;
            load.Parent = _toolbar;
            validate.Parent = _toolbar;
            save.Parent = _toolbar;
            publish.Parent = _toolbar;
            indicator.Parent = _toolbar;
            xp.Parent = _toolbar;
            newService.Parent = _toolbar;
            newHostile.Parent = _toolbar;
        }

        private void BuildList()
        {
            _leftPanel = new Panel(ScrollBars.Vertical) { Parent = this };
        }

        private void BuildTabBar()
        {
            _tabBar = new Panel(ScrollBars.None) { Parent = this };
            for (var i = 0; i < TabTitles.Length; i++)
            {
                var index = i;
                _tabButtons[i] = MakeButton(_tabBar, 4f + i * 104f, 2f, 100f, TabTitles[i], () => SelectTab(index));
            }
        }

        private void BuildForms()
        {
            _formHost = new Panel(ScrollBars.None) { Parent = this };

            _formBase = NewFormPanel();
            _nameBox = MakeText(_formBase, 90f, 8f, 220f, "显示名");
            _titleBox = MakeText(_formBase, 90f, 36f, 220f, "头衔（名字下方）");
            _modelKeyBox = MakeText(_formBase, 90f, 64f, 220f, "模型键");
            _prefabBox = MakeText(_formBase, 90f, 92f, 220f, "prefab GUID（空=未配美术）");
            _scaleBox = MakeText(_formBase, 90f, 120f, 80f, "体型");
            _levelBox = MakeText(_formBase, 240f, 120f, 80f, "等级");
            _levelVarBox = MakeText(_formBase, 390f, 120f, 80f, "等级浮动");
            _alertBox = MakeText(_formBase, 90f, 148f, 80f, "增援半径");
            _gossipGroupBox = MakeText(_formBase, 240f, 148f, 80f, "台词组");
            _killCreditBox = MakeText(_formBase, 390f, 148f, 120f, "击杀归属 NPC");
            _walkSpeedBox = MakeText(_formBase, 90f, 232f, 80f, "行走 m/s");
            _runSpeedBox = MakeText(_formBase, 240f, 232f, 80f, "奔跑 m/s");
            _reactionDrop = MakeDrop(_formBase, 90f, 176f, 140f, ReactionNames);
            _tierDrop = MakeDrop(_formBase, 90f, 204f, 220f, TierNames);
            _triDrop = MakeDrop(_formBase, 90f, 232f, 140f, TriNames);
            _domainDrop = MakeDrop(_formBase, 390f, 176f, 120f, DomainNames);
            _rankDrop = MakeDrop(_formBase, 390f, 204f, 120f, RankNames);

            // AI 覆盖：逐位"强制开 / 强制关"。两列在输入侧互斥（勾一边清另一边）；
            // 装配器的 ai_force_conflict 仍保留作兜底——库里已有或外部导入的冲突行要能被报出来。
            MakeLabel(_formBase, 8f, 234f, 120f, "AI 覆盖（强制开）");
            MakeLabel(_formBase, 200f, 234f, 120f, "强制关");
            for (var i = 0; i < NpcAiBitsUi.Names.Length; i++)
            {
                var y = 258f + i * 26f;
                var index = i;
                MakeLabel(_formBase, 8f, y + 2f, 90f, NpcAiBitsUi.Names[i]);
                _aiOn[index] = MakeDrop(_formBase, 100f, y, 70f, YesNoNames);
                _aiOff[index] = MakeDrop(_formBase, 210f, y, 70f, YesNoNames);
                _aiOn[index].SelectedIndexChanged += _ => SyncAiOverride(index, onSideChanged: true);
                _aiOff[index].SelectedIndexChanged += _ => SyncAiOverride(index, onSideChanged: false);
            }

            _formService = NewFormPanel();
            for (var i = 0; i < NpcServiceBitsUi.Names.Length; i++)
            {
                _serviceDrops[i] = MakeDrop(_formService, 90f, 8f + i * 28f, 80f, YesNoNames);
                MakeLabel(_formService, 8f, 10f + i * 28f, 78f, NpcServiceBitsUi.Names[i]);
            }

            _serviceSummary = MakeLabel(_formService, 8f, 8f + NpcServiceBitsUi.Names.Length * 28f + 8f, 420f, "");
            MakeButton(_formService, 300f, 8f, 110f, "入口预览", PreviewServices);

            _formVendor = NewFormPanel();
            MakeLabel(_formVendor, 8f, 8f, 40f, "槽");
            MakeLabel(_formVendor, 56f, 8f, 60f, "物品");
            MakeLabel(_formVendor, 150f, 8f, 60f, "买入");
            MakeLabel(_formVendor, 238f, 8f, 60f, "收购");
            MakeLabel(_formVendor, 326f, 8f, 60f, "库存");
            MakeLabel(_formVendor, 414f, 8f, 60f, "补货秒");
            for (var i = 0; i < VendorRows; i++)
            {
                var y = 34f + i * 28f;
                _vendorSlot[i] = MakeText(_formVendor, 8f, y, 40f, "0");
                _vendorItem[i] = MakeText(_formVendor, 56f, y, 86f, "物品编号");
                _vendorBuy[i] = MakeText(_formVendor, 150f, y, 80f, "0");
                _vendorSell[i] = MakeText(_formVendor, 238f, y, 80f, "0");
                _vendorStock[i] = MakeText(_formVendor, 326f, y, 80f, "0");
                _vendorRestock[i] = MakeText(_formVendor, 414f, y, 80f, "0");
            }

            _formTrainer = NewFormPanel();
            MakeLabel(_formTrainer, 8f, 8f, 60f, "技能/配方");
            MakeLabel(_formTrainer, 140f, 8f, 60f, "种类");
            MakeLabel(_formTrainer, 260f, 8f, 60f, "学费");
            MakeLabel(_formTrainer, 350f, 8f, 60f, "排序");
            for (var i = 0; i < TrainerRows; i++)
            {
                var y = 34f + i * 28f;
                _trainerSkill[i] = MakeText(_formTrainer, 8f, y, 120f, "编号");
                _trainerKind[i] = MakeDrop(_formTrainer, 140f, y, 100f, ServiceKindNames);
                _trainerCost[i] = MakeText(_formTrainer, 260f, y, 80f, "0");
                _trainerSeq[i] = MakeText(_formTrainer, 350f, y, 60f, "0");
            }

            // 配方：产物 / 材料 / 成功率曲线（由易到难：橙黄绿灰）。
            var recipeY = 34f + TrainerRows * 28f + 16f;
            MakeLabel(_formTrainer, 8f, recipeY, 240f, "配方（行号可切换，尾行=新增）");
            _recipeIndexBox = MakeText(_formTrainer, 300f, recipeY, 40f, "0");
            MakeButton(_formTrainer, 344f, recipeY - 4f, 30f, "◀", () =>
            {
                if (_draft == null) return;
                StepRow(_recipeIndexBox, -1, _draft.Recipes?.Count ?? 0);
                ApplyRecipeRow();
            ApplySkillMountRows();
            });
            MakeButton(_formTrainer, 378f, recipeY - 4f, 30f, "▶", () =>
            {
                if (_draft == null) return;
                StepRow(_recipeIndexBox, 1, _draft.Recipes?.Count ?? 0);
                ApplyRecipeRow();
            });
            MakeButton(_formTrainer, 412f, recipeY - 4f, 50f, "新增", () =>
            {
                if (_draft == null) return;
                _recipeIndexBox.Text = (_draft.Recipes?.Count ?? 0).ToString(CultureInfo.InvariantCulture);
                ApplyRecipeRow();
            });
            _recipeRowsLabel = MakeLabel(_formTrainer, 466f, recipeY, 200f, "");
            var r1 = recipeY + 24f;
            MakeLabel(_formTrainer, 8f, r1, 40f, "编号");
            _recipeIdBox = MakeText(_formTrainer, 46f, r1, 60f, "0");
            MakeLabel(_formTrainer, 112f, r1, 30f, "名");
            _recipeNameBox = MakeText(_formTrainer, 140f, r1, 150f, "配方名");
            MakeLabel(_formTrainer, 296f, r1, 40f, "产物");
            _recipeProductBox = MakeText(_formTrainer, 334f, r1, 70f, "物品 id");
            MakeLabel(_formTrainer, 410f, r1, 40f, "工费");
            _recipeCostBox = MakeText(_formTrainer, 448f, r1, 60f, "0");
            MakeLabel(_formTrainer, 514f, r1, 50f, "设施NPC");
            _recipeStationBox = MakeText(_formTrainer, 566f, r1, 50f, "0");
            MakeLabel(_formTrainer, 622f, r1, 50f, "技能要求");
            _recipeSkillBox = MakeText(_formTrainer, 674f, r1, 50f, "0");

            var r2 = r1 + 28f;
            MakeLabel(_formTrainer, 8f, r2, 200f, "材料（物品 / 数量）");
            for (var i = 0; i < RecipeMaterialRows; i++)
            {
                _recipeItem[i] = MakeText(_formTrainer, 8f + i * 140f, r2 + 22f, 80f, "物品 id");
                _recipeCount[i] = MakeText(_formTrainer, 92f + i * 140f, r2 + 22f, 44f, "1");
            }

            var r3 = r2 + 52f;
            MakeLabel(_formTrainer, 8f, r3, 240f, "成功率 万分比（橙/黄/绿/灰）");
            for (var i = 0; i < 4; i++)
                _recipeChance[i] = MakeText(_formTrainer, 8f + i * 70f, r3 + 22f, 60f, "0");
            MakeLabel(_formTrainer, 300f, r3, 260f, "技能差上限（橙/黄/绿/灰）");
            for (var i = 0; i < 4; i++)
                _recipeGap[i] = MakeText(_formTrainer, 300f + i * 70f, r3 + 22f, 60f, "0");

            // 技能挂载（施法）：需同时勾选 AI 覆盖里的 Casts，否则运行期不读这些行。
            var m0 = r3 + 78f;
            MakeLabel(_formTrainer, 8f, m0, 460f, "技能挂载（施法；需勾选 AI 覆盖里的 Casts）");
            MakeLabel(_formTrainer, 8f, m0 + 22f, 60f, "技能");
            MakeLabel(_formTrainer, 120f, m0 + 22f, 60f, "时机");
            MakeLabel(_formTrainer, 230f, m0 + 22f, 70f, "概率万分");
            MakeLabel(_formTrainer, 330f, m0 + 22f, 60f, "吟唱ms");
            MakeLabel(_formTrainer, 430f, m0 + 22f, 60f, "冷却ms");
            MakeLabel(_formTrainer, 530f, m0 + 22f, 60f, "射程m");
            for (var i = 0; i < CastRows; i++)
            {
                var y = m0 + 44f + i * 28f;
                _castSkill[i] = MakeText(_formTrainer, 8f, y, 100f, "编号");
                _castSlot[i] = MakeDrop(_formTrainer, 120f, y, 100f, CastSlotNames);
                _castChance[i] = MakeText(_formTrainer, 230f, y, 80f, "10000");
                _castMs[i] = MakeText(_formTrainer, 330f, y, 80f, "0");
                _castCd[i] = MakeText(_formTrainer, 430f, y, 80f, "5000");
                _castRange[i] = MakeText(_formTrainer, 530f, y, 80f, "20");
            }

            // 面板只有四行，库里可能更多：保存是整组替换，必须让作者看见"还有几条没显示"。
            _castHiddenLabel = MakeLabel(_formTrainer, 8f, m0 + 44f + CastRows * 28f + 4f, 460f, "");

            _formTalk = NewFormPanel();
            MakeLabel(_formTalk, 8f, 8f, 60f, "组");
            MakeLabel(_formTalk, 70f, 8f, 50f, "序");
            MakeLabel(_formTalk, 130f, 8f, 90f, "动作");
            MakeLabel(_formTalk, 240f, 8f, 60f, "参数");
            MakeLabel(_formTalk, 310f, 8f, 60f, "最低级");
            MakeLabel(_formTalk, 380f, 8f, 60f, "最高级");
            for (var i = 0; i < GossipRows; i++)
            {
                // 每行两排：上排是动作列，下排是台词正文（023 的 text 列）。
                var y = 34f + i * 52f;
                _gossipGroup[i] = MakeText(_formTalk, 8f, y, 54f, "组");
                _gossipSeq[i] = MakeText(_formTalk, 70f, y, 50f, "0");
                _gossipAction[i] = MakeDrop(_formTalk, 130f, y, 100f, ActionNames);
                _gossipArg[i] = MakeText(_formTalk, 240f, y, 60f, "0");
                _gossipMin[i] = MakeText(_formTalk, 310f, y, 60f, "0");
                _gossipMax[i] = MakeText(_formTalk, 380f, y, 60f, "0");
                _gossipText[i] = MakeText(_formTalk, 8f, y + 26f, 432f, "台词（空=客户端按动作合成）");
            }

            MakeLabel(_formTalk, 8f, 34f + GossipRows * 52f + 10f, 120f, "作息（周期内秒）");
            for (var i = 0; i < ScheduleRows; i++)
            {
                var y = 34f + GossipRows * 52f + 38f + i * 28f;
                _schedFrom[i] = MakeText(_formTalk, 8f, y, 60f, "起");
                _schedTo[i] = MakeText(_formTalk, 76f, y, 60f, "止");
                _schedRoute[i] = MakeText(_formTalk, 144f, y, 60f, "路线");
                _schedPosture[i] = MakeDrop(_formTalk, 212f, y, 90f, PostureNames);
            }

            _formQuest = NewFormPanel();
            MakeLabel(_formQuest, 8f, 8f, 80f, "任务编号");
            MakeLabel(_formQuest, 150f, 8f, 80f, "角色");
            for (var i = 0; i < QuestRows; i++)
            {
                var y = 34f + i * 28f;
                _questId[i] = MakeText(_formQuest, 8f, y, 120f, "任务 id");
                _questRole[i] = MakeDrop(_formQuest, 150f, y, 100f, RoleNames);
            }

            _formLoot = NewFormPanel();
            MakeLabel(_formLoot, 8f, 8f, 120f, "掉率（万分比）");
            _lootRollBox = MakeText(_formLoot, 140f, 8f, 80f, "10000");
            MakeLabel(_formLoot, 240f, 8f, 80f, "金币下限");
            _lootMoneyMinBox = MakeText(_formLoot, 320f, 8f, 70f, "0");
            MakeLabel(_formLoot, 400f, 8f, 80f, "金币上限");
            _lootMoneyMaxBox = MakeText(_formLoot, 480f, 8f, 70f, "0");
            // 绑定级条件：下拉值域与逐件同一份（0 = 无条件；这里没有"继承"这一档）。
            MakeLabel(_formLoot, 566f, 8f, 60f, "条件");
            _lootBindingCondition = MakeDrop(_formLoot, 612f, 8f, 114f, NpcLootConditionUi.BindingNames);
            MakeLabel(_formLoot, 736f, 8f, 60f, "任务 id");
            _lootBindingQuest = MakeText(_formLoot, 782f, 8f, 70f, "0");

            MakeLabel(_formLoot, 8f, 40f, 60f, "物品");
            MakeLabel(_formLoot, 96f, 40f, 80f, "概率万分比");
            MakeLabel(_formLoot, 206f, 40f, 50f, "最少");
            MakeLabel(_formLoot, 266f, 40f, 50f, "最多");
            MakeLabel(_formLoot, 330f, 40f, 110f, "条件（逐件）");
            MakeLabel(_formLoot, 452f, 40f, 60f, "任务 id");
            for (var i = 0; i < LootRows; i++)
            {
                var y = 66f + i * 28f;
                _lootItem[i] = MakeText(_formLoot, 8f, y, 80f, "物品 id");
                _lootChance[i] = MakeText(_formLoot, 96f, y, 100f, "1000");
                _lootMin[i] = MakeText(_formLoot, 206f, y, 50f, "1");
                _lootMax[i] = MakeText(_formLoot, 266f, y, 50f, "1");
                // 逐件条件：默认"继承（跟绑定）"。库值 -1 与下拉索引 0 的映射只在 NpcLootConditionUi 一份。
                _lootCondition[i] = MakeDrop(_formLoot, 330f, y, 114f, NpcLootConditionUi.Names);
                _lootQuest[i] = MakeText(_formLoot, 452f, y, 70f, "0");
            }

            // 面板只露四件、只编第 0 份绑定：还有多少没显示必须让作者看见（保存是整组替换）。
            _lootHiddenLabel = MakeLabel(_formLoot, 8f, 66f + LootRows * 28f + 6f, 560f, "");

            _formChain = NewFormPanel();
            MakeLabel(_formChain, 8f, 8f, 260f, "任务链（按前置排序，含传递前置）");
            MakeButton(_formChain, 280f, 4f, 120f, "刷新任务链", RefreshQuestChain);
            _chainBox = new TextBox(true, 8f, 34f, 520f)
            {
                Parent = _formChain,
                Bounds = new Rectangle(8f, 34f, 520f, 280f),
                Text = "",
            };

            _formSpawn = NewFormPanel();
            MakeLabel(_formSpawn, 8f, 8f, 160f, "刷点（行号可切换，尾行=新增）");
            _spawnIndexBox = MakeText(_formSpawn, 236f, 8f, 40f, "0");
            MakeButton(_formSpawn, 280f, 4f, 30f, "◀", () =>
            {
                if (_draft == null) return;
                StepRow(_spawnIndexBox, -1, _draft.SpawnPoints?.Count ?? 0);
                ApplySpawnAndRoute();
            });
            MakeButton(_formSpawn, 314f, 4f, 30f, "▶", () =>
            {
                if (_draft == null) return;
                StepRow(_spawnIndexBox, 1, _draft.SpawnPoints?.Count ?? 0);
                ApplySpawnAndRoute();
            });
            MakeButton(_formSpawn, 348f, 4f, 50f, "新增", () =>
            {
                if (_draft == null) return;
                _spawnIndexBox.Text = (_draft.SpawnPoints?.Count ?? 0).ToString(CultureInfo.InvariantCulture);
                ApplySpawnAndRoute();
            });
            // 删除当前刷点：保存是"按 NPC 整组替换刷点"，删掉的行会从库里消失。
            // 路线/配方没有这个按钮——它们跨 NPC 共享，删除是另一套退役流程。
            MakeButton(_formSpawn, 566f, 4f, 70f, "删除本行", () =>
            {
                if (_draft == null) return;
                var count = _draft.SpawnPoints?.Count ?? 0;
                var index = NpcRowIndex.Clamp(ParseInt(_spawnIndexBox.Text, 0), count);
                if (index >= count) return;   // 尾行 = 新增档，没有可删的
                _draft.RemoveSpawn(index);
                var left = _draft.SpawnPoints?.Count ?? 0;
                _spawnIndexBox.Text = Math.Min(index, left).ToString(CultureInfo.InvariantCulture);
                ApplySpawnAndRoute();
            });
            _spawnRowsLabel = MakeLabel(_formSpawn, 404f, 8f, 156f, "");
            MakeLabel(_formSpawn, 8f, 34f, 20f, "X");
            _spawnX = MakeText(_formSpawn, 30f, 34f, 60f, "0");
            MakeLabel(_formSpawn, 96f, 34f, 20f, "Y");
            _spawnY = MakeText(_formSpawn, 118f, 34f, 60f, "0");
            MakeLabel(_formSpawn, 184f, 34f, 20f, "Z");
            _spawnZ = MakeText(_formSpawn, 206f, 34f, 60f, "0");
            MakeLabel(_formSpawn, 272f, 34f, 40f, "朝向");
            _spawnYaw = MakeText(_formSpawn, 314f, 34f, 60f, "0");
            MakeLabel(_formSpawn, 380f, 34f, 40f, "路线");
            _spawnRoute = MakeText(_formSpawn, 422f, 34f, 60f, "0");
            MakeLabel(_formSpawn, 488f, 34f, 60f, "同时存活");
            _spawnAlive = MakeText(_formSpawn, 552f, 34f, 50f, "1");
            MakeLabel(_formSpawn, 608f, 34f, 60f, "刷新秒");
            _spawnRespawn = MakeText(_formSpawn, 670f, 34f, 60f, "60");

            MakeLabel(_formSpawn, 8f, 70f, 200f, "路线（编号 0 = 新建）");
            MakeLabel(_formSpawn, 220f, 70f, 50f, "路线行");
            _routeIndexBox = MakeText(_formSpawn, 272f, 70f, 40f, "0");
            MakeButton(_formSpawn, 316f, 66f, 30f, "◀", () =>
            {
                if (_draft == null) return;
                StepRow(_routeIndexBox, -1, _draft.Routes?.Count ?? 0);
                ApplySpawnAndRoute();
            });
            MakeButton(_formSpawn, 350f, 66f, 30f, "▶", () =>
            {
                if (_draft == null) return;
                StepRow(_routeIndexBox, 1, _draft.Routes?.Count ?? 0);
                ApplySpawnAndRoute();
            });
            MakeButton(_formSpawn, 384f, 66f, 50f, "新增", () =>
            {
                if (_draft == null) return;
                _routeIndexBox.Text = (_draft.Routes?.Count ?? 0).ToString(CultureInfo.InvariantCulture);
                ApplySpawnAndRoute();
            });
            _routeRowsLabel = MakeLabel(_formSpawn, 440f, 70f, 200f, "");
            MakeButton(_formSpawn, 648f, 66f, 70f, "路线试算", PreviewRoute);
            MakeLabel(_formSpawn, 8f, 96f, 30f, "编号");
            _routeId = MakeText(_formSpawn, 40f, 96f, 50f, "0");
            MakeLabel(_formSpawn, 96f, 96f, 30f, "名称");
            _routeName = MakeText(_formSpawn, 128f, 96f, 180f, "巡街路线");
            MakeLabel(_formSpawn, 314f, 96f, 40f, "走法");
            _routeMode = MakeDrop(_formSpawn, 356f, 96f, 90f, new[] { "往返", "循环", "随机" });
            MakeLabel(_formSpawn, 452f, 96f, 40f, "走图");
            _routeNav = MakeDrop(_formSpawn, 494f, 96f, 60f, YesNoNames);
            _routeNav.SelectedIndex = 1;
            MakeLabel(_formSpawn, 560f, 96f, 60f, "点停留");
            _routePause = MakeText(_formSpawn, 622f, 96f, 50f, "0");

            MakeLabel(_formSpawn, 8f, 128f, 300f, "路线点（序号留空 = 这一行不存在）");
            MakeLabel(_formSpawn, 8f, 152f, 30f, "序");
            MakeLabel(_formSpawn, 44f, 152f, 20f, "X");
            MakeLabel(_formSpawn, 110f, 152f, 20f, "Y");
            MakeLabel(_formSpawn, 176f, 152f, 20f, "Z");
            MakeLabel(_formSpawn, 242f, 152f, 60f, "停留秒");
            for (var i = 0; i < RoutePoints; i++)
            {
                var y = 176f + i * 28f;
                _nodeSeq[i] = MakeText(_formSpawn, 8f, y, 30f, "");
                _nodeX[i] = MakeText(_formSpawn, 44f, y, 60f, "0");
                _nodeY[i] = MakeText(_formSpawn, 110f, y, 60f, "0");
                _nodeZ[i] = MakeText(_formSpawn, 176f, y, 60f, "0");
                _nodePause[i] = MakeText(_formSpawn, 242f, y, 50f, "0");
            }

            var questBottom = 34f + QuestRows * 28f + 16f;
            MakeLabel(_formQuest, 8f, questBottom, 120f, "经验试算：NPC 等级");
            _xpNpcLevelBox = MakeText(_formQuest, 150f, questBottom, 70f, "30");
            MakeLabel(_formQuest, 240f, questBottom, 60f, "玩家等级");
            _xpPlayerLevelBox = MakeText(_formQuest, 310f, questBottom, 70f, "30");
            _xpResult = MakeLabel(_formQuest, 8f, questBottom + 30f, 520f, "");
        }

        private void BuildConsole()
        {
            _consolePanel = new Panel(ScrollBars.None) { Parent = this };
            _consoleBox = new TextBox(true, 4f, 4f, 400f)
            {
                Parent = _consolePanel,
                WatermarkText = "CLI 回执（JSON）",
            };
        }

        // ---------------- 布局 ----------------

        private void LayoutNow()
        {
            var width = Width;
            var height = Height;

            if (_toolbar != null) _toolbar.Bounds = new Rectangle(0, 0, width, ToolbarHeight);
            if (_tabBar != null) _tabBar.Bounds = new Rectangle(LeftWidth, ToolbarHeight, Math.Max(0, width - LeftWidth), TabBarHeight);
            if (_formHost != null)
                _formHost.Bounds = new Rectangle(LeftWidth, ToolbarHeight + TabBarHeight,
                    Math.Max(0, width - LeftWidth), Math.Max(0, height - ToolbarHeight - TabBarHeight - ConsoleHeight));
            if (_leftPanel != null)
                _leftPanel.Bounds = new Rectangle(0, ToolbarHeight, LeftWidth, Math.Max(0, height - ToolbarHeight - ConsoleHeight));
            if (_consolePanel != null) _consolePanel.Bounds = new Rectangle(0, height - ConsoleHeight, width, ConsoleHeight);
            if (_consoleBox != null) _consoleBox.Bounds = new Rectangle(4f, 4f, Math.Max(40f, width - 8f), ConsoleHeight - 8f);

            // 窗体面板叠在同一个 host 里，只显示当前页。
            var forms = new[] { _formBase, _formService, _formVendor, _formTrainer, _formTalk, _formQuest, _formLoot, _formChain, _formSpawn };
            for (var i = 0; i < forms.Length; i++)
            {
                if (forms[i] == null) continue;
                forms[i].Bounds = new Rectangle(0, 0, Math.Max(0, width - LeftWidth), Math.Max(0, height - ToolbarHeight - TabBarHeight - ConsoleHeight));
                forms[i].Visible = i == _activeTab;
            }
        }

        private void SelectTab(int index)
        {
            _activeTab = index;
            for (var i = 0; i < _tabButtons.Length; i++)
            {
                if (_tabButtons[i] != null) _tabButtons[i].Enabled = i != index;
            }

            LayoutNow();
        }

        private Panel NewFormPanel() => new Panel(ScrollBars.Vertical) { Parent = _formHost, Visible = false };

        private static Button MakeButton(Panel host, float x, float y, float w, string text, Action onClick)
        {
            var button = new Button(x, y, w, RowHeight) { Text = text, Parent = host };
            button.Clicked += onClick;
            return button;
        }

        private static TextBox MakeText(Panel host, float x, float y, float w, string watermark)
        {
            return new TextBox(false, x, y, w)
            {
                Parent = host,
                WatermarkText = watermark,
                Height = RowHeight - 4f,
            };
        }

        private static Dropdown MakeDrop(Panel host, float x, float y, float w, string[] items)
        {
            var drop = new Dropdown { Parent = host, Bounds = new Rectangle(x, y, w, RowHeight - 4f) };
            for (var i = 0; i < items.Length; i++) drop.AddItem(items[i]);
            drop.SelectedIndex = 0;
            return drop;
        }

        private static Label MakeLabel(Panel host, float x, float y, float w, string text)
        {
            return new Label(x, y, w, RowHeight) { Parent = host, Text = text };
        }

        /// <summary>
        /// 行号解析：夹在 <c>[0, count]</c>。**count 本身是合法值**——表示"新增一行"，
        /// 所以这里不能钳到 count-1（那样就没有"新建"这一档了）。规则住在 <see cref="NpcRowIndex"/>。
        /// </summary>
        private static int ParseRowIndex(string text, int count)
            => NpcRowIndex.Clamp(ParseInt(text, 0), count);

        /// <summary>行选择按钮：上一/下一（夹在 [0, count]），"新增"由调用方直接写 count。</summary>
        private static void StepRow(TextBox box, int delta, int count)
        {
            var index = NpcRowIndex.Step(ParseRowIndex(box.Text, count), delta, count);
            box.Text = index.ToString(CultureInfo.InvariantCulture);
        }

        // ---------------- 动作 ----------------

        private void RefreshCatalog()
        {
            if (_busy) return;
            _busy = true;

            var result = NpcCli.Run("catalog");
            _busy = false;

            Log(result.Display + "\n" + result.Raw);
            if (!result.Ok || !result.HasData || result.Data.ValueKind != JsonValueKind.Array) return;

            _listedIds.Clear();
            var index = 0;
            foreach (var item in result.Data.EnumerateArray())
            {
                if (index >= MaxListRows) break;
                var id = item.TryGetProperty("npcId", out var idProp) ? idProp.GetInt32() : 0;
                var name = item.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : "";
                var segment = item.TryGetProperty("segment", out var segProp) ? segProp.GetString() : "";

                _listedIds.Add(id);
                if (_listButtons[index] == null)
                {
                    var buttonIndex = index;
                    _listButtons[index] = MakeButton(_leftPanel, 4f, 6f + index * 26f, LeftWidth - 10f, "", () => PickListed(buttonIndex));
                }

                _listButtons[index].Text = id + "  " + name + (segment == "hostile" ? "（敌对）" : "");
                _listButtons[index].Visible = true;
                index++;
            }

            for (var i = index; i < MaxListRows; i++)
            {
                if (_listButtons[i] != null) _listButtons[i].Visible = false;
            }

            _statusLabel.Text = "目录 " + _listedIds.Count + " 条（其中已装配内容见回执）";
        }

        private void PickListed(int index)
        {
            if (index < 0 || index >= _listedIds.Count) return;
            _idBox.Text = _listedIds[index].ToString(CultureInfo.InvariantCulture);
            LoadNpc();
        }

        private void LoadNpc()
        {
            if (_busy) return;
            var id = ParseInt(_idBox.Text, 0);
            if (id <= 0)
            {
                Log("先填 NPC 编号（或用左侧目录选一个）。");
                return;
            }

            _busy = true;
            var result = NpcCli.Run("get " + id.ToString(CultureInfo.InvariantCulture));
            _busy = false;

            Log(result.Display + "\n" + result.Raw);
            if (!result.Ok || !result.HasData) return;

            var draft = NpcDraftModel.FromJson(result.Data.GetRawText());
            if (draft == null)
            {
                Log("CLI 返回的草稿无法反序列化（字段名漂了？）。");
                return;
            }

            _draft = draft;
            ApplyDraftToUi();
            _statusLabel.Text = "已加载 NPC " + id + "（content_version=" + _draft.ContentVersion + "）";
        }

        /// <summary>
        /// 新建：先向 CLI 要一个号段内的空号，再在窗口里摆一份空白草稿。
        /// <para>
        /// <b>不自动落库</b>：新建只是开始编辑；保存仍走同一个 <c>save</c>（未发布态），
        /// "点了新建就在库里多一条"会让取消编辑变成一次隐式写库。
        /// </para>
        /// <para>
        /// 敌对型默认 <c>Reaction=敌对</c> + AI 档 5（自卫/追击）、服务型默认友好 + 档 2（会说话）：
        /// 号段与立场必须同时对上（装载器会拒绝"敌对号 + 友好立场"），档位只是起点，随时可在页里改。
        /// </para>
        /// </summary>
        private void NewNpc(bool hostile)
        {
            if (_busy) return;
            _busy = true;
            var result = NpcCli.Run("allocate-id --segment " + (hostile ? "hostile" : "service"));
            _busy = false;

            Log(result.Display + "\n" + result.Raw);
            if (!result.Ok || !result.HasData) return;
            if (!result.Data.TryGetProperty("npcId", out var idProp)) return;

            var id = idProp.GetInt32();
            if (id <= 0) return;

            var draft = new NpcDraftModel();
            draft.Template.NpcId = id;
            draft.Template.Reaction = hostile ? (byte)2 : (byte)0;
            draft.Template.AiTier = hostile ? (byte)5 : (byte)2;
            draft.Template.Name = (hostile ? "新敌人 " : "新 NPC ") + id;

            _draft = draft;
            ApplyDraftToUi();
            _statusLabel.Text = "新建 NPC " + id + "（尚未保存；保存后为未发布态）";
        }

        /// <summary>
        /// 刷新任务链：读库拿到本 NPC 的任务与**传递前置**，按前置拓扑排序后画成文本。
        /// <para>
        /// 排序/连边全在 <c>QuestChainCore</c>（服务端同一份内核），这里只负责显示——
        /// 编辑器另排一次序会让"面板上的 1→2→3"和运行期的前置判定分叉。
        /// </para>
        /// </summary>
        private void RefreshQuestChain()
        {
            if (_busy) return;
            var id = ParseInt(_idBox.Text, 0);
            if (id <= 0)
            {
                Log("先填 NPC 编号（或用左侧目录选一个）。");
                return;
            }

            _busy = true;
            var result = NpcCli.Run("quest-chain " + id.ToString(CultureInfo.InvariantCulture));
            _busy = false;

            Log(result.Display);
            if (_chainBox == null) return;

            if (!result.Ok || !result.HasData)
            {
                _chainBox.Text = result.Display + "\n" + result.Raw;
                return;
            }

            var builder = new StringBuilder();
            var data = result.Data;
            builder.Append("NPC ").Append(id).Append("（来源 ")
                .Append(data.TryGetProperty("source", out var src) ? src.GetString() : "?")
                .Append("）\n");

            if (data.TryGetProperty("nodes", out var nodes))
            {
                var index = 0;
                foreach (var node in nodes.EnumerateArray())
                {
                    index++;
                    var questId = node.GetProperty("questId").GetInt32();
                    var name = node.TryGetProperty("name", out var n) ? n.GetString() : "";
                    var turnIn = node.GetProperty("turnInTemplateId").GetInt32();
                    var giver = node.GetProperty("giverTemplateId").GetInt32();

                    builder.Append(index.ToString(CultureInfo.InvariantCulture)).Append(". #").Append(questId)
                        .Append(' ').Append(name)
                        .Append("  发布 ").Append(giver > 0 ? "NPC#" + giver : "?")
                        .Append("  交付 ").Append(turnIn > 0 ? "NPC#" + turnIn : "（沿用发布者）")
                        .Append("  前置[");

                    if (node.TryGetProperty("prerequisites", out var prereqs))
                    {
                        var first = true;
                        foreach (var p in prereqs.EnumerateArray())
                        {
                            if (!first) builder.Append(',');
                            builder.Append(p.GetInt32());
                            first = false;
                        }
                    }

                    builder.Append("]\n");

                    // 描述与目标：文本由 CLI 拼好（QuestChainText 一份规则），这里只逐行打印。
                    var description = node.TryGetProperty("description", out var descProp) ? descProp.GetString() : "";
                    if (!string.IsNullOrEmpty(description))
                    {
                        builder.Append("     描述：").Append(description).Append('\n');
                    }

                    if (node.TryGetProperty("objectives", out var objectivesProp)
                        && objectivesProp.ValueKind == JsonValueKind.Array)
                    {
                        var first = true;
                        foreach (var objective in objectivesProp.EnumerateArray())
                        {
                            builder.Append(first ? "     目标：" : "、").Append(objective.GetString());
                            first = false;
                        }

                        if (!first) builder.Append('\n');
                    }
                }
            }

            if (data.TryGetProperty("problems", out var problems))
            {
                foreach (var problem in problems.EnumerateArray())
                    builder.Append("问题：").Append(problem.GetString()).Append('\n');
            }

            _chainBox.Text = builder.ToString();
        }

        /// <summary>
        /// 服务入口预览：调用 CLI 同一套判据，回答"这名玩家会看到哪些入口、其余为什么不可见"。
        /// <para>作者勾的是位图，玩家看到的是入口；中间那一跳受内容（货架/教学行为空）与玩家态影响。</para>
        /// </summary>
        private void PreviewServices()
        {
            CommitUiToDraft();
            var player = ParseInt(_levelBox.Text, 1);
            var result = NpcCli.Run("preview-services - --player " + player.ToString(CultureInfo.InvariantCulture),
                _draft.ToJson());
            Log("入口预览 → " + result.Display + "\n" + result.Raw);
        }

        private void ValidateDraft()
        {
            CommitUiToDraft();
            var result = NpcCli.Run("validate -", _draft.ToJson());
            Log("校验 → " + result.Display + "\n" + result.Raw);
            LogPrunedServices(result);
            _statusLabel.Text = result.Ok ? result.Message : "校验未通过（见下方回执）";
        }

        /// <summary>
        /// 把"会被装配期摘掉的服务位"念出来。
        /// <para>位开着但内容为空时，客户端不会有入口、保存也不会报错——不说这一句，
        /// 作者只会看到"我勾了商人，游戏里没有"。</para>
        /// </summary>
        private void LogPrunedServices(NpcCliResult result)
        {
            if (!result.HasData || result.Data.ValueKind != JsonValueKind.Object) return;
            if (!result.Data.TryGetProperty("prunedServices", out var pruned)
                || pruned.ValueKind != JsonValueKind.Array)
                return;

            foreach (var item in pruned.EnumerateArray())
                Log("服务位将被摘除（内容为空）：" + item.GetString());
        }

        private void SaveDraft()
        {
            CommitUiToDraft();

            // 保存前先校验：摘位不是错误（保存照常），但作者要当场知道哪一位不会生效。
            LogPrunedServices(NpcCli.Run("validate -", _draft.ToJson()));

            var result = NpcCli.Run("save - --expect-version " + _draft.ContentVersion.ToString(CultureInfo.InvariantCulture), _draft.ToJson());
            Log("保存 → " + result.Display + "\n" + result.Raw);
            if (result.Ok)
            {
                _draft.ContentVersion += 1;
                _statusLabel.Text = "已保存（仍为草稿，点『发布』才会上线）";
            }
        }

        private void PublishNpc()
        {
            var id = ParseInt(_idBox.Text, 0);
            if (id <= 0)
            {
                Log("先填 NPC 编号。");
                return;
            }

            var result = NpcCli.Run("publish " + id.ToString(CultureInfo.InvariantCulture));
            Log("发布 → " + result.Display + "\n" + result.Raw);
            if (result.Ok) _statusLabel.Text = "已发布 NPC " + id;
        }

        private void PreviewIndicator()
        {
            CommitUiToDraft();
            var player = ParseInt(_xpPlayerLevelBox.Text, 1);
            var result = NpcCli.Run("preview-indicator - --player " + player.ToString(CultureInfo.InvariantCulture) + " --accepted --done", _draft.ToJson());
            Log("头顶指示器 → " + result.Display + "\n" + result.Raw);
        }

        private void PreviewXp()
        {
            var npcLevel = ParseInt(_xpNpcLevelBox.Text, ParseInt(_levelBox.Text, 1));
            var playerLevel = ParseInt(_xpPlayerLevelBox.Text, 1);
            var result = NpcCli.Run("preview-xp --npc-level " + npcLevel.ToString(CultureInfo.InvariantCulture)
                                    + " --player-level " + playerLevel.ToString(CultureInfo.InvariantCulture));
            Log("经验 → " + result.Display + "\n" + result.Raw);

            if (result.Ok && result.HasData)
            {
                var xp = result.Data.TryGetProperty("xp", out var xpProp) ? xpProp.GetInt64() : 0;
                var diff = result.Data.TryGetProperty("difficulty", out var diffProp) ? diffProp.GetString() : "";
                var reason = result.Data.TryGetProperty("reason", out var reasonProp) ? reasonProp.GetString() : "";
                if (_xpResult != null) _xpResult.Text = "经验 " + xp + "（" + diff + " / " + reason + "）";
            }
        }

        // ---------------- 草稿 ↔ UI ----------------

        private void ApplyDraftToUi()
        {
            var t = _draft.Template ?? new NpcTemplateModel();
            _idBox.Text = t.NpcId > 0 ? t.NpcId.ToString(CultureInfo.InvariantCulture) : _idBox.Text;
            _nameBox.Text = t.Name;
            _titleBox.Text = t.Title;
            _modelKeyBox.Text = t.ModelKey;
            _prefabBox.Text = t.PrefabGuid;
            _scaleBox.Text = t.Scale.ToString("0.##", CultureInfo.InvariantCulture);
            _levelBox.Text = t.Level.ToString(CultureInfo.InvariantCulture);
            _levelVarBox.Text = t.LevelVariance.ToString(CultureInfo.InvariantCulture);
            _alertBox.Text = t.AlertRadiusMeters.ToString(CultureInfo.InvariantCulture);
            _walkSpeedBox.Text = t.SpeedWalk.ToString("0.##", CultureInfo.InvariantCulture);
            _runSpeedBox.Text = t.SpeedRun.ToString("0.##", CultureInfo.InvariantCulture);
            _gossipGroupBox.Text = t.GossipGroupId.ToString(CultureInfo.InvariantCulture);
            _killCreditBox.Text = t.KillCreditNpcId.ToString(CultureInfo.InvariantCulture);
            _reactionDrop.SelectedIndex = Math.Min(2, Math.Max(0, (int)t.Reaction));
            _tierDrop.SelectedIndex = Math.Min(7, Math.Max(0, (int)t.AiTier));
            _triDrop.SelectedIndex = t.AttackableOverride < 0 ? 0 : (t.AttackableOverride == 0 ? 1 : 2);
            _domainDrop.SelectedIndex = Math.Min(5, Math.Max(0, t.WuxingDomain));
            _rankDrop.SelectedIndex = Math.Min(4, Math.Max(0, (int)t.Rank));

            // 回填时关掉互斥：已冲突的草稿要原样显示给作者（validate 会念出来），不在打开时静默改掉一半。
            _applyingAiBits = true;
            try
            {
                for (var i = 0; i < NpcAiBitsUi.Names.Length; i++)
                {
                    _aiOn[i].SelectedIndex = (t.AiBitsForceOn & NpcAiBitsUi.Values[i]) != 0 ? 1 : 0;
                    _aiOff[i].SelectedIndex = (t.AiBitsForceOff & NpcAiBitsUi.Values[i]) != 0 ? 1 : 0;
                }
            }
            finally
            {
                _applyingAiBits = false;
            }

            for (var i = 0; i < _serviceDrops.Length; i++)
            {
                _serviceDrops[i].SelectedIndex = (t.ServiceFlags & NpcServiceBitsUi.Values[i]) != 0 ? 1 : 0;
            }

            ApplyVendorRows();
            ApplyTrainerRows();
            ApplyTalkRows();
            ApplyScheduleRows();
            ApplyQuestRows();
            ApplyLootRows();
            ApplySpawnAndRoute();
            UpdateServiceSummary();
        }

        /// <summary>
        /// AI 覆盖两列互斥：某一边改成"是"时，把同行的另一边清回"否"。
        /// <para>内核的 <c>ai_force_conflict</c> 仍保留：它兜的是"库里已有/外部导入"的坏数据，不是编辑器自己造出来的。</para>
        /// </summary>
        private void SyncAiOverride(int index, bool onSideChanged)
        {
            if (_applyingAiBits) return;

            var (on, off) = NpcAiOverrideEditing.Exclusive(
                _aiOn[index].SelectedIndex == 1,
                _aiOff[index].SelectedIndex == 1,
                onSideChanged);

            // 写回被清的那一侧会再触发本方法：挡住它，免得两次回写互相覆盖。
            _applyingAiBits = true;
            _aiOn[index].SelectedIndex = on ? 1 : 0;
            _aiOff[index].SelectedIndex = off ? 1 : 0;
            _applyingAiBits = false;
        }

        private void ApplyVendorRows()
        {
            for (var i = 0; i < VendorRows; i++)
            {
                var row = i < _draft.VendorRows.Count ? _draft.VendorRows[i] : null;
                _vendorSlot[i].Text = row?.Slot.ToString(CultureInfo.InvariantCulture) ?? i.ToString(CultureInfo.InvariantCulture);
                _vendorItem[i].Text = row?.ItemId.ToString(CultureInfo.InvariantCulture) ?? "";
                _vendorBuy[i].Text = row?.BuyPriceOverride.ToString(CultureInfo.InvariantCulture) ?? "";
                _vendorSell[i].Text = row?.SellPriceOverride.ToString(CultureInfo.InvariantCulture) ?? "";
                _vendorStock[i].Text = row?.StockMax.ToString(CultureInfo.InvariantCulture) ?? "";
                _vendorRestock[i].Text = row?.RestockSeconds.ToString(CultureInfo.InvariantCulture) ?? "";
            }
        }

        private void ApplyTrainerRows()
        {
            for (var i = 0; i < TrainerRows; i++)
            {
                var row = i < _draft.TrainerRows.Count ? _draft.TrainerRows[i] : null;
                _trainerSkill[i].Text = row?.SkillId.ToString(CultureInfo.InvariantCulture) ?? "";
                _trainerKind[i].SelectedIndex = row == null || row.ServiceKind == 1 ? 0 : 1;
                _trainerCost[i].Text = row?.CostGold.ToString(CultureInfo.InvariantCulture) ?? "";
                _trainerSeq[i].Text = row?.Seq.ToString(CultureInfo.InvariantCulture) ?? i.ToString(CultureInfo.InvariantCulture);
            }

            ApplyRecipeRow();
        }

        /// <summary>把草稿里的第一条配方回填。没有配方时清空（避免把上一只 NPC 的配方留在框里）。</summary>
        private void ApplyRecipeRow()
        {
            var recipeCount = _draft.Recipes?.Count ?? 0;
            var recipeIndex = ParseRowIndex(_recipeIndexBox.Text, recipeCount);
            var recipe = recipeIndex < recipeCount ? _draft.Recipes[recipeIndex] : null;
            _recipeRowsLabel.Text = "共 " + recipeCount + " 行（当前 " + recipeIndex + "）";

            _recipeIdBox.Text = (recipe != null ? recipe.RecipeId : 0).ToString(CultureInfo.InvariantCulture);
            _recipeNameBox.Text = recipe != null ? recipe.Name : "";
            _recipeProductBox.Text = (recipe != null ? recipe.ProductItemId : 0).ToString(CultureInfo.InvariantCulture);
            _recipeCostBox.Text = (recipe != null ? recipe.CostGold : 0).ToString(CultureInfo.InvariantCulture);
            _recipeStationBox.Text = (recipe != null ? recipe.StationNpcId : 0).ToString(CultureInfo.InvariantCulture);
            _recipeSkillBox.Text = (recipe != null ? recipe.SkillReq : 0).ToString(CultureInfo.InvariantCulture);

            for (var i = 0; i < RecipeMaterialRows; i++)
            {
                var material = recipe != null && i < recipe.Materials.Count ? recipe.Materials[i] : null;
                _recipeItem[i].Text = material != null ? material.ItemId.ToString(CultureInfo.InvariantCulture) : "";
                _recipeCount[i].Text = material != null ? material.Count.ToString(CultureInfo.InvariantCulture) : "";
            }

            for (var i = 0; i < 4; i++)
            {
                _recipeChance[i].Text = recipe != null && i < recipe.QualityChanceBps.Count
                    ? recipe.QualityChanceBps[i].ToString(CultureInfo.InvariantCulture) : "0";
                _recipeGap[i].Text = recipe != null && i < recipe.QualityGapCaps.Count
                    ? recipe.QualityGapCaps[i].ToString(CultureInfo.InvariantCulture) : "0";
            }
        }

        private void ApplyTalkRows()
        {
            for (var i = 0; i < GossipRows; i++)
            {
                var row = i < _draft.GossipRows.Count ? _draft.GossipRows[i] : null;
                _gossipGroup[i].Text = row?.GroupId.ToString(CultureInfo.InvariantCulture) ?? "";
                _gossipSeq[i].Text = row?.Seq.ToString(CultureInfo.InvariantCulture) ?? i.ToString(CultureInfo.InvariantCulture);
                _gossipAction[i].SelectedIndex = row == null ? 0 : Math.Min(ActionNames.Length - 1, Math.Max(0, (int)row.ActionKind));
                _gossipArg[i].Text = row?.ActionArg.ToString(CultureInfo.InvariantCulture) ?? "0";
                _gossipMin[i].Text = row?.ReqMinLevel.ToString(CultureInfo.InvariantCulture) ?? "0";
                _gossipMax[i].Text = row?.ReqMaxLevel.ToString(CultureInfo.InvariantCulture) ?? "0";
                _gossipText[i].Text = row?.Text ?? "";
            }
        }

        private void ApplyScheduleRows()
        {
            for (var i = 0; i < ScheduleRows; i++)
            {
                var row = i < _draft.ScheduleRows.Count ? _draft.ScheduleRows[i] : null;
                _schedFrom[i].Text = row?.FromSeconds.ToString(CultureInfo.InvariantCulture) ?? "";
                _schedTo[i].Text = row?.ToSeconds.ToString(CultureInfo.InvariantCulture) ?? "";
                _schedRoute[i].Text = row?.RouteId.ToString(CultureInfo.InvariantCulture) ?? "0";
                _schedPosture[i].SelectedIndex = row == null ? 0 : Math.Min(PostureNames.Length - 1, Math.Max(0, (int)row.Posture));
            }
        }

        private void ApplyQuestRows()
        {
            for (var i = 0; i < QuestRows; i++)
            {
                var row = i < _draft.QuestLinks.Count ? _draft.QuestLinks[i] : null;
                _questId[i].Text = row?.QuestId.ToString(CultureInfo.InvariantCulture) ?? "";
                _questRole[i].SelectedIndex = row == null || row.Role == 1 ? 0 : 1;
            }
        }

        /// <summary>
        /// 技能挂载：面板只编前六列；其余字段（选敌/AOE/系别/状态/移动施法…）保存时**沿用载入值**。
        /// 面板没显示的列如果被重置，作者只会看到"保存一次之后射程和系别全变默认"。
        /// </summary>
        private void ApplySkillMountRows()
        {
            var rows = _draft?.SkillMounts ?? new List<NpcSkillMountModel>();
            for (var i = 0; i < CastRows; i++)
            {
                var row = i < rows.Count ? rows[i] : null;
                _castSkill[i].Text = row != null && row.SkillId > 0
                    ? row.SkillId.ToString(CultureInfo.InvariantCulture)
                    : "";
                _castSlot[i].SelectedIndex = row == null ? 0 : Math.Min(3, Math.Max(0, (int)row.SlotType));
                _castChance[i].Text = (row?.ChanceBps ?? 10000).ToString(CultureInfo.InvariantCulture);
                _castMs[i].Text = (row?.CastMs ?? 0).ToString(CultureInfo.InvariantCulture);
                _castCd[i].Text = (row?.CooldownMs ?? 5000).ToString(CultureInfo.InvariantCulture);
                _castRange[i].Text = (row?.MaxRange ?? 8f).ToString("0.##", CultureInfo.InvariantCulture);
            }

            if (_castHiddenLabel != null)
            {
                var extra = Math.Max(0, rows.Count - CastRows);
                _castHiddenLabel.Text = extra > 0
                    ? "另有 " + extra.ToString(CultureInfo.InvariantCulture) + " 条未显示（保存不改动）"
                    : "";
            }
        }

        /// <summary>
        /// 提交技能挂载：技能列为空 = 这一行不存在；未显示的字段按**技能 id** 回找上一份沿用。
        /// SlotSeq 取行序：顺序决定平局时先放哪个，编辑器的行序就是它的来源。
        /// </summary>
        private void CommitSkillMountRows()
        {
            var existing = _draft.SkillMounts ?? new List<NpcSkillMountModel>();
            var priorById = new Dictionary<int, NpcSkillMountModel>();
            foreach (var row in existing)
            {
                if (row == null || row.SkillId <= 0 || priorById.ContainsKey(row.SkillId)) continue;
                priorById[row.SkillId] = row;
            }

            var next = new List<NpcSkillMountModel>();
            for (var i = 0; i < CastRows; i++)
            {
                var skillId = ParseInt(_castSkill[i].Text, 0);
                if (skillId <= 0) continue;

                priorById.TryGetValue(skillId, out var prior);
                next.Add(new NpcSkillMountModel
                {
                    NpcId = _draft.Template?.NpcId ?? 0,
                    SkillId = skillId,
                    SlotSeq = i,
                    SlotType = (byte)Math.Max(0, _castSlot[i].SelectedIndex),
                    ChanceBps = ParseInt(_castChance[i].Text, 10000),
                    CastMs = ParseInt(_castMs[i].Text, 0),
                    CooldownMs = ParseInt(_castCd[i].Text, 5000),
                    MaxRange = ParseFloat(_castRange[i].Text, prior?.MaxRange ?? 8f),
                    MinRange = prior?.MinRange ?? 0f,
                    TargetSelect = prior?.TargetSelect ?? 0,
                    TargetCount = prior?.TargetCount ?? 1,
                    AoeRadius = prior?.AoeRadius ?? 0f,
                    SchoolMask = prior?.SchoolMask ?? 0,
                    Interruptible = prior?.Interruptible ?? 1,
                    AllowMovement = prior?.AllowMovement ?? 0,
                    DamageScaleBps = prior?.DamageScaleBps ?? 10000,
                    StatusId = prior?.StatusId ?? 0,
                    StatusDurationSeconds = prior?.StatusDurationSeconds ?? 0f,
                    StartEmote = prior?.StartEmote ?? 0,
                    EmergencyHpPercent = prior?.EmergencyHpPercent ?? 30,
                });
            }

            // 面板看不到的行不能在保存时被删掉（整组替换）：合并逻辑放模型侧，能被单测钉住。
            _draft.SkillMounts = NpcSkillMountEditing.MergeSkillMounts(existing, next, CastRows);
        }

        private void CommitUiToDraft()
        {
            var t = _draft.Template ?? new NpcTemplateModel();
            t.NpcId = ParseInt(_idBox.Text, t.NpcId);
            t.Name = _nameBox.Text ?? "";
            t.Title = _titleBox.Text ?? "";
            t.ModelKey = _modelKeyBox.Text ?? "";
            t.PrefabGuid = _prefabBox.Text ?? "";
            t.Scale = ParseFloat(_scaleBox.Text, 1f);
            t.Level = ParseInt(_levelBox.Text, 1);
            t.LevelVariance = ParseInt(_levelVarBox.Text, 0);
            t.AlertRadiusMeters = ParseInt(_alertBox.Text, 20);
            // 速度单位是 m/s（运行期用米制换算）。空框保留原值，别把作者清除的动作当成"改成默认"。
            t.SpeedWalk = ParseFloat(_walkSpeedBox.Text, t.SpeedWalk > 0f ? t.SpeedWalk : 1.6f);
            t.SpeedRun = ParseFloat(_runSpeedBox.Text, t.SpeedRun > 0f ? t.SpeedRun : 4f);
            t.GossipGroupId = ParseInt(_gossipGroupBox.Text, 0);
            t.KillCreditNpcId = ParseInt(_killCreditBox.Text, 0);
            t.Reaction = (byte)Math.Max(0, _reactionDrop.SelectedIndex);
            t.AiTier = (byte)Math.Max(0, _tierDrop.SelectedIndex);
            t.AttackableOverride = _triDrop.SelectedIndex == 0 ? -1 : (_triDrop.SelectedIndex == 1 ? 0 : 1);
            t.WuxingDomain = Math.Max(0, _domainDrop.SelectedIndex);
            t.Rank = (byte)Math.Max(0, _rankDrop.SelectedIndex);

            uint forceOn = 0, forceOff = 0;
            for (var i = 0; i < NpcAiBitsUi.Names.Length; i++)
            {
                if (_aiOn[i].SelectedIndex == 1) forceOn |= NpcAiBitsUi.Values[i];
                if (_aiOff[i].SelectedIndex == 1) forceOff |= NpcAiBitsUi.Values[i];
            }

            t.AiBitsForceOn = forceOn;
            t.AiBitsForceOff = forceOff;

            uint flags = 0;
            for (var i = 0; i < _serviceDrops.Length; i++)
            {
                if (_serviceDrops[i].SelectedIndex == 1) flags |= NpcServiceBitsUi.Values[i];
            }

            t.ServiceFlags = flags;
            _draft.Template = t;

            var vendorRows = new List<NpcVendorRowModel>();
            for (var i = 0; i < VendorRows; i++)
            {
                var itemId = ParseInt(_vendorItem[i].Text, 0);
                if (itemId <= 0) continue;
                vendorRows.Add(new NpcVendorRowModel
                {
                    NpcId = t.NpcId,
                    Slot = ParseInt(_vendorSlot[i].Text, i),
                    ItemId = itemId,
                    BuyPriceOverride = ParseLong(_vendorBuy[i].Text, 0),
                    SellPriceOverride = ParseLong(_vendorSell[i].Text, 0),
                    StockMax = ParseInt(_vendorStock[i].Text, 0),
                    RestockSeconds = ParseInt(_vendorRestock[i].Text, 0),
                    Category = 0,
                });
            }

            // 货架可能比面板行多（随包商队就是 5 行）：看不见的不许被保存删掉；槽位是稳定键，不顺延。
            NpcRowListEditing.AppendHidden(vendorRows, _draft.VendorRows, VendorRows, r => r.ItemId, null);
            _draft.VendorRows = vendorRows;

            var trainerRows = new List<NpcTrainerRowModel>();
            for (var i = 0; i < TrainerRows; i++)
            {
                var skillId = ParseInt(_trainerSkill[i].Text, 0);
                if (skillId <= 0) continue;
                trainerRows.Add(new NpcTrainerRowModel
                {
                    NpcId = t.NpcId,
                    SkillId = skillId,
                    ServiceKind = (byte)(_trainerKind[i].SelectedIndex == 0 ? 1 : 2),
                    CostGold = ParseLong(_trainerCost[i].Text, 0),
                    Seq = ParseInt(_trainerSeq[i].Text, i),
                });
            }

            NpcRowListEditing.AppendHidden(trainerRows, _draft.TrainerRows, TrainerRows,
                r => r.SkillId, (r, seq) => r.Seq = seq);
            _draft.TrainerRows = trainerRows;

            CommitRecipeRow();
            CommitSkillMountRows();

            var gossipRows = new List<NpcGossipRowModel>();
            for (var i = 0; i < GossipRows; i++)
            {
                var groupId = ParseInt(_gossipGroup[i].Text, 0);
                if (groupId <= 0) continue;
                gossipRows.Add(new NpcGossipRowModel
                {
                    NpcId = t.NpcId,
                    GroupId = groupId,
                    Seq = ParseInt(_gossipSeq[i].Text, i),
                    ActionKind = (byte)Math.Max(0, _gossipAction[i].SelectedIndex),
                    ActionArg = ParseInt(_gossipArg[i].Text, 0),
                    ReqMinLevel = ParseInt(_gossipMin[i].Text, 0),
                    ReqMaxLevel = ParseInt(_gossipMax[i].Text, 0),
                    // 台词必须随保存带回去：保存是"删光重插"，漏这一格 = 每次保存清空台词。
                    Text = _gossipText[i].Text?.Trim() ?? "",
                });
            }

            // 随包台词是 7 行、面板只有 4 行：整组替换时必须把没显示的组/序原样带回。
            NpcRowListEditing.AppendHidden(gossipRows, _draft.GossipRows, GossipRows,
                r => ((long)r.GroupId * 1000000L) + r.Seq, (r, seq) => r.Seq = seq);
            _draft.GossipRows = gossipRows;

            var scheduleRows = new List<NpcScheduleRowModel>();
            for (var i = 0; i < ScheduleRows; i++)
            {
                var to = ParseInt(_schedTo[i].Text, -1);
                if (to < 0) continue; // 未填 = 这一档不存在（0 是合法的"全天兜底"，不能用 0 表示缺失）
                var from = ParseInt(_schedFrom[i].Text, 0);
                // 台词组是面板没露的列：按"起始秒"认同一档、沿用载入值。
                // 写死 0 会把已配的"白天/夜里两套菜单"静默清掉（库里实测出现过 1 被改成 0）。
                NpcScheduleRowModel priorSchedule = null;
                if (_draft.ScheduleRows != null)
                {
                    foreach (var prior in _draft.ScheduleRows)
                    {
                        if (prior != null && prior.FromSeconds == from) { priorSchedule = prior; break; }
                    }
                }

                scheduleRows.Add(new NpcScheduleRowModel
                {
                    NpcId = t.NpcId,
                    Seq = i,
                    FromSeconds = from,
                    ToSeconds = to,
                    RouteId = ParseInt(_schedRoute[i].Text, 0),
                    Posture = (byte)Math.Max(0, _schedPosture[i].SelectedIndex),
                    GossipGroupId = priorSchedule?.GossipGroupId ?? 0,
                });
            }

            // 作息段同样可能多于面板三行（随包是 5 段）：按"起始秒"认同一档，其余原样带回。
            NpcRowListEditing.AppendHidden(scheduleRows, _draft.ScheduleRows, ScheduleRows,
                r => r.FromSeconds, (r, seq) => r.Seq = seq);
            _draft.ScheduleRows = scheduleRows;

            var questLinks = new List<NpcQuestLinkModel>();
            for (var i = 0; i < QuestRows; i++)
            {
                var questId = ParseInt(_questId[i].Text, 0);
                if (questId <= 0) continue;
                questLinks.Add(new NpcQuestLinkModel
                {
                    NpcId = t.NpcId,
                    QuestId = questId,
                    Role = (byte)(_questRole[i].SelectedIndex == 0 ? 1 : 2),
                    OrderSeq = i,
                });
            }

            NpcRowListEditing.AppendHidden(questLinks, _draft.QuestLinks, QuestRows,
                r => r.QuestId, (r, seq) => r.OrderSeq = seq);
            _draft.QuestLinks = questLinks;

            CommitLootRows();
            CommitSpawnAndRoute();
            UpdateServiceSummary();
        }

        /// <summary>把草稿里的刷点（按行号）与路线（按行号）回填到界面；行号 == 行数表示"新增"档。</summary>
        private void ApplySpawnAndRoute()
        {
            var spawnCount = _draft.SpawnPoints?.Count ?? 0;
            var spawnIndex = ParseRowIndex(_spawnIndexBox.Text, spawnCount);
            var spawn = spawnIndex < spawnCount ? _draft.SpawnPoints[spawnIndex] : null;
            _spawnRowsLabel.Text = "共 " + spawnCount + " 行（当前 " + spawnIndex + "）";
            _spawnX.Text = (spawn != null ? spawn.X : 0f).ToString("0.##", CultureInfo.InvariantCulture);
            _spawnY.Text = (spawn != null ? spawn.Y : 0f).ToString("0.##", CultureInfo.InvariantCulture);
            _spawnZ.Text = (spawn != null ? spawn.Z : 0f).ToString("0.##", CultureInfo.InvariantCulture);
            _spawnYaw.Text = (spawn != null ? spawn.Yaw : 0f).ToString("0.##", CultureInfo.InvariantCulture);
            _spawnRoute.Text = (spawn != null ? spawn.RouteId : 0).ToString(CultureInfo.InvariantCulture);
            _spawnAlive.Text = (spawn != null ? spawn.MaxAlive : 1).ToString(CultureInfo.InvariantCulture);
            _spawnRespawn.Text = (spawn != null ? spawn.RespawnSeconds : 60f).ToString("0.##", CultureInfo.InvariantCulture);

            var routeCount = _draft.Routes?.Count ?? 0;
            var routeIndex = ParseRowIndex(_routeIndexBox.Text, routeCount);
            var route = routeIndex < routeCount ? _draft.Routes[routeIndex] : null;
            _routeRowsLabel.Text = "共 " + routeCount + " 行（当前 " + routeIndex + "）";
            _routeId.Text = (route != null ? route.PathId : 0).ToString(CultureInfo.InvariantCulture);
            _routeName.Text = route != null ? route.Name : "";
            _routeMode.SelectedIndex = Math.Min(2, Math.Max(0, route != null ? route.Mode : 1));
            _routeNav.SelectedIndex = route == null || route.UseNavMesh ? 1 : 0;
            _routePause.Text = (route != null ? route.PauseAtPointSeconds : 0f).ToString("0.##", CultureInfo.InvariantCulture);

            for (var i = 0; i < RoutePoints; i++)
            {
                var point = route != null && i < route.Points.Count ? route.Points[i] : null;
                _nodeSeq[i].Text = point != null ? point.Seq.ToString(CultureInfo.InvariantCulture) : "";
                _nodeX[i].Text = (point != null ? point.X : 0f).ToString("0.##", CultureInfo.InvariantCulture);
                _nodeY[i].Text = (point != null ? point.Y : 0f).ToString("0.##", CultureInfo.InvariantCulture);
                _nodeZ[i].Text = (point != null ? point.Z : 0f).ToString("0.##", CultureInfo.InvariantCulture);
                _nodePause[i].Text = (point != null ? point.PauseSeconds : 0f).ToString("0.##", CultureInfo.InvariantCulture);
            }
        }

        /// <summary>
        /// 路线试算：一个循环多远、多久、有没有问题。顺序与停留由服务端巡逻内核现算
        /// （CLI 侧 <c>preview-route</c>），面板只显示回执——自己再走一遍就会与运行期分叉。
        /// </summary>
        private void PreviewRoute()
        {
            if (_draft == null)
            {
                Log("先加载或新建一个 NPC。");
                return;
            }

            CommitUiToDraft();
            var index = ParseRowIndex(_routeIndexBox.Text, _draft.Routes?.Count ?? 0);
            var result = NpcCli.Run("preview-route - --route-index " + index.ToString(CultureInfo.InvariantCulture), _draft.ToJson());
            Log("路线试算 → " + result.Display + "\n" + result.Raw);
        }

        /// <summary>
        /// 把界面写回草稿：刷点与路线都**只在真的填过**时才覆盖——
        /// 打开一个没有刷点的 NPC 只是看了一眼就保存，不该凭空在原点长出一个刷点。
        /// </summary>
        private void CommitSpawnAndRoute()
        {
            var npcId = _draft.Template?.NpcId ?? 0;
            var spawnCount = _draft.SpawnPoints?.Count ?? 0;
            var spawnIndex = ParseRowIndex(_spawnIndexBox.Text, spawnCount);
            var hadSpawn = spawnIndex < spawnCount;
            var fillSpawn =
                ParseFloat(_spawnX.Text, 0f) != 0f || ParseFloat(_spawnY.Text, 0f) != 0f
                || ParseFloat(_spawnZ.Text, 0f) != 0f || ParseInt(_spawnRoute.Text, 0) > 0;

            if (hadSpawn || fillSpawn)
            {
                var previous = hadSpawn ? _draft.SpawnPoints[spawnIndex] : null;
                // 只替换"正在编辑的这一个"（行号 == 行数时追加）：多刷点的 NPC 在面板里一次看一行，
                // 整表替换会把其余行静默删掉（保存成功、库里少行）。
                _draft.ReplaceSpawn(hadSpawn ? spawnIndex : -1, new NpcSpawnPointModel
                {
                    SpawnPointId = previous?.SpawnPointId ?? 0,
                    ZoneId = previous?.ZoneId ?? 0,
                    ShardId = previous?.ShardId ?? 0,
                    X = ParseFloat(_spawnX.Text, 0f),
                    Y = ParseFloat(_spawnY.Text, 0f),
                    Z = ParseFloat(_spawnZ.Text, 0f),
                    Yaw = ParseFloat(_spawnYaw.Text, 0f),
                    NpcId = npcId,
                    LevelOverride = previous?.LevelOverride ?? 0,
                    MaxAlive = Math.Max(1, ParseInt(_spawnAlive.Text, 1)),
                    RespawnSeconds = ParseFloat(_spawnRespawn.Text, 60f),
                    RouteId = ParseInt(_spawnRoute.Text, 0),
                    FacingMode = previous?.FacingMode ?? 0,
                });
            }

            var routeCount = _draft.Routes?.Count ?? 0;
            var routeIndex = ParseRowIndex(_routeIndexBox.Text, routeCount);
            var previousRoute = routeIndex < routeCount ? _draft.Routes[routeIndex] : null;

            var points = new List<NpcRoutePointModel>();
            for (var i = 0; i < RoutePoints; i++)
            {
                // 序号留空 = 这一行不存在（0 是合法序号，不能用 0 表示缺失）。
                if (string.IsNullOrWhiteSpace(_nodeSeq[i].Text)) continue;
                points.Add(new NpcRoutePointModel
                {
                    Seq = ParseInt(_nodeSeq[i].Text, i),
                    X = ParseFloat(_nodeX[i].Text, 0f),
                    Y = ParseFloat(_nodeY[i].Text, 0f),
                    Z = ParseFloat(_nodeZ[i].Text, 0f),
                    PauseSeconds = ParseFloat(_nodePause[i].Text, 0f),
                });
            }

            // 路线可能比面板 6 个点长：没显示的点原样带回（整条线替换）。序号是稳定键，不重排。
            NpcRowListEditing.AppendHidden(points, previousRoute?.Points, RoutePoints, p => p.Seq, null);

            var hadRoute = _draft.Routes != null && _draft.Routes.Count > 0;
            var pathId = ParseInt(_routeId.Text, 0);
            if (hadRoute || points.Count > 0 || pathId > 0)
            {
                // 按**面板行号**合并：新建路线在落库前 PathId 还是 0，只认 PathId 会每次提交都追加一条。
                _draft.UpsertRouteAt(routeIndex, new NpcRouteModel
                {
                    PathId = pathId,
                    Name = _routeName.Text ?? "",
                    Mode = (byte)Math.Max(0, _routeMode.SelectedIndex),
                    PauseAtPointSeconds = ParseFloat(_routePause.Text, 0f),
                    UseNavMesh = _routeNav.SelectedIndex == 1,
                    Points = points,
                });
            }
        }

        /// <summary>
        /// 把界面写回草稿的配方。判据：产物 id &gt; 0 才算"这条配方存在"——
        /// 打开一只不教制作的 NPC 看一眼就保存，不该凭空长出一条空配方。
        /// </summary>
        private void CommitRecipeRow()
        {
            var product = ParseInt(_recipeProductBox.Text, 0);
            var hadRecipe = _draft.Recipes != null && _draft.Recipes.Count > 0;
            if (product <= 0 && !hadRecipe)
            {
                if (_draft.Recipes == null) _draft.Recipes = new List<NpcRecipeModel>();
                return;
            }

            var recipeCount = _draft.Recipes?.Count ?? 0;
            var recipeIndex = ParseRowIndex(_recipeIndexBox.Text, recipeCount);
            var previousRecipe = recipeIndex < recipeCount ? _draft.Recipes[recipeIndex] : null;

            var materials = new List<NpcMaterialModel>();
            for (var i = 0; i < RecipeMaterialRows; i++)
            {
                var itemId = ParseInt(_recipeItem[i].Text, 0);
                if (itemId <= 0) continue;
                materials.Add(new NpcMaterialModel { ItemId = itemId, Count = Math.Max(1, ParseInt(_recipeCount[i].Text, 1)) });
            }

            // 材料可能比面板 3 行多：没显示的按原序带回（整张配方替换）。
            NpcRowListEditing.AppendHidden(materials, previousRecipe?.Materials, RecipeMaterialRows, m => m.ItemId, null);

            var chances = new List<int>(4);
            var gaps = new List<int>(4);
            for (var i = 0; i < 4; i++)
            {
                chances.Add(ParseInt(_recipeChance[i].Text, 0));
                gaps.Add(ParseInt(_recipeGap[i].Text, 0));
            }

            // 按配方号 + 面板行号合并：一只 NPC 可以教多张配方，面板只编辑一条；新建配方落库前 RecipeId 为 0。
            _draft.UpsertRecipeAt(recipeIndex, new NpcRecipeModel
            {
                RecipeId = ParseInt(_recipeIdBox.Text, 0),
                Name = _recipeNameBox.Text ?? "",
                ProductItemId = product,
                CostGold = ParseLong(_recipeCostBox.Text, 0),
                StationNpcId = ParseInt(_recipeStationBox.Text, 0),
                SkillReq = ParseInt(_recipeSkillBox.Text, 0),
                Materials = materials,
                QualityGapCaps = gaps,
                QualityChanceBps = chances,
                LearnSourceKind = 1,
            });
        }

        private void ApplyLootRows()
        {
            var binding = _draft.LootBindings != null && _draft.LootBindings.Count > 0 ? _draft.LootBindings[0] : null;

            _lootRollBox.Text = (binding != null ? binding.RollChanceBps : 10000).ToString(CultureInfo.InvariantCulture);
            _lootMoneyMinBox.Text = (binding != null ? binding.MoneyMin : 0).ToString(CultureInfo.InvariantCulture);
            _lootMoneyMaxBox.Text = (binding != null ? binding.MoneyMax : 0).ToString(CultureInfo.InvariantCulture);
            _lootBindingCondition.SelectedIndex = NpcLootConditionUi.BindingIndexOf(binding?.ConditionKind ?? 0);
            _lootBindingQuest.Text = (binding != null ? binding.QuestId : 0).ToString(CultureInfo.InvariantCulture);

            for (var i = 0; i < LootRows; i++)
            {
                var entry = binding != null && i < binding.Entries.Count ? binding.Entries[i] : null;
                _lootItem[i].Text = entry != null ? entry.ItemId.ToString(CultureInfo.InvariantCulture) : string.Empty;
                _lootChance[i].Text = (entry != null ? entry.ChanceBps : 1000).ToString(CultureInfo.InvariantCulture);
                _lootMin[i].Text = (entry != null ? entry.CountMin : 1).ToString(CultureInfo.InvariantCulture);
                _lootMax[i].Text = (entry != null ? entry.CountMax : 1).ToString(CultureInfo.InvariantCulture);
                _lootCondition[i].SelectedIndex = NpcLootConditionUi.IndexOf(entry != null ? entry.ConditionKind : -1);
                _lootQuest[i].Text = (entry != null ? entry.QuestId : 0).ToString(CultureInfo.InvariantCulture);
            }

            if (_lootHiddenLabel != null)
            {
                var extraEntries = Math.Max(0, (binding?.Entries?.Count ?? 0) - LootRows);
                var extraBindings = Math.Max(0, (_draft.LootBindings?.Count ?? 0) - 1);
                _lootHiddenLabel.Text = extraEntries > 0 || extraBindings > 0
                    ? "另有 " + extraEntries.ToString(CultureInfo.InvariantCulture) + " 件 / "
                      + extraBindings.ToString(CultureInfo.InvariantCulture) + " 份绑定未显示（保存不改动）"
                    : "";
            }
        }

        private void CommitLootRows()
        {
            var npcId = _draft.Template != null ? _draft.Template.NpcId : 0;
            var previous = _draft.LootBindings != null && _draft.LootBindings.Count > 0 ? _draft.LootBindings[0] : null;

            // 以库里的那一份为**底座**，只覆盖面板真的露出的几格（概率/金币/逐件四格）。
            // 面板没有的条件、任务 id、组号、人数区间若从零值重造，保存一次就会把它们静默改掉
            // （"任务持有者才掉"变人人可掉、同组互斥变逐件独立）。
            var binding = new NpcLootBindingModel
            {
                MonsterTemplateId = npcId,
                LootTableId = previous?.LootTableId ?? 1,
                MinCount = previous?.MinCount ?? 1,
                MaxCount = previous?.MaxCount ?? 1,
                ConditionKind = previous?.ConditionKind ?? 0,
                QuestId = previous?.QuestId ?? 0,
            };

            binding.RollChanceBps = ParseInt(_lootRollBox.Text, previous?.RollChanceBps ?? 10000);
            binding.MoneyMin = ParseInt(_lootMoneyMinBox.Text, previous?.MoneyMin ?? 0);
            binding.MoneyMax = ParseInt(_lootMoneyMaxBox.Text, previous?.MoneyMax ?? 0);
            // 绑定级条件现在面板里能改（下拉 + 任务 id）；值域换算与逐件同一份规则。
            binding.ConditionKind = NpcLootConditionUi.BindingValueAt(_lootBindingCondition.SelectedIndex);
            binding.QuestId = ParseInt(_lootBindingQuest.Text, previous?.QuestId ?? 0);

            for (var i = 0; i < LootRows; i++)
            {
                var itemId = ParseInt(_lootItem[i].Text, 0);
                if (itemId <= 0) continue;

                // 逐件条件/组号同样按物品号从旧行带回来（面板没有这两格）。
                NpcLootEntryModel keep = null;
                if (previous?.Entries != null)
                {
                    for (var k = 0; k < previous.Entries.Count; k++)
                    {
                        if (previous.Entries[k].ItemId != itemId) continue;
                        keep = previous.Entries[k];
                        break;
                    }
                }

                binding.Entries.Add(new NpcLootEntryModel
                {
                    ItemId = itemId,
                    ChanceBps = ParseInt(_lootChance[i].Text, 1000),
                    CountMin = ParseInt(_lootMin[i].Text, 1),
                    CountMax = ParseInt(_lootMax[i].Text, 1),
                    Seq = i,
                    GroupId = keep?.GroupId ?? 0,
                    // 条件/任务 id 现在面板里能改（下拉 + 文本框）；值域换算只在 NpcLootConditionUi 一份。
                    ConditionKind = NpcLootConditionUi.ValueAt(_lootCondition[i].SelectedIndex),
                    QuestId = ParseInt(_lootQuest[i].Text, keep?.QuestId ?? 0),
                });
            }

            // 面板只露四件：第 5 件起原样带回（整组替换语义，见 NpcLootEditing）。
            NpcLootEditing.AppendHiddenEntries(binding, previous, LootRows);

            // 面板只编第 0 份绑定：其余绑定不能因为"看不见"被保存删掉。
            _draft.LootBindings = NpcLootEditing.MergeBindings(binding, _draft.LootBindings);
        }

        private void UpdateServiceSummary()
        {
            if (_serviceSummary == null) return;
            uint flags = 0;
            for (var i = 0; i < _serviceDrops.Length; i++)
            {
                if (_serviceDrops[i].SelectedIndex == 1) flags |= NpcServiceBitsUi.Values[i];
            }

            _serviceSummary.Text = "位图 0x" + flags.ToString("X") + " → " + NpcServiceBitsUi.Describe(flags);
        }

        // ---------------- 工具 ----------------

        private void Log(string text)
        {
            if (_consoleBox == null) return;
            var line = "[" + DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + "] " + text + "\n";
            _consoleBox.Text = (_consoleBox.Text ?? "") + line;
            Debug.Log("[NpcEditor] " + text);
        }

        private static int ParseInt(string text, int fallback)
        {
            int value;
            return int.TryParse((text ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) ? value : fallback;
        }

        private static long ParseLong(string text, long fallback)
        {
            long value;
            return long.TryParse((text ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) ? value : fallback;
        }

        private static float ParseFloat(string text, float fallback)
        {
            float value;
            return float.TryParse((text ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value) ? value : fallback;
        }
    }
}
