using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;
using FlaxEngine;
using FlaxEngine.GUI;
using FlaxEditor;
using FlaxEditor.Windows;

namespace MonsterEditor
{
    /// <summary>
    /// 怪物编辑器主窗口：编号、属性公式、行为与 AI 档位、技能挂载、巡逻路线与寻路、
    /// 掉落与逐件概率、击杀经验（含随玩家等级的反比例衰减曲线）。
    /// <para>
    /// <b>数据权威在 SQL Server</b>，本窗口经 <see cref="MonsterCli"/> 子进程读写；
    /// 校验规则只在服务端那一份 —— 窗口里的即时提示只是预览，<c>validate/save</c> 的回执才是结论。
    /// </para>
    /// <para>
    /// <b>为什么控件全部绝对定位</b>：Flax 的构造期布局会在父尺寸仍为 0 时按 0 重算位置，
    /// 于是非 TopLeft 锚的控件"凭空不显示"。所有动态排布集中在 <see cref="LayoutNow"/>。
    /// </para>
    /// </summary>
    public sealed class MonsterEditorWindow : EditorWindow
    {
        private const float ToolbarHeight = 62f;
        private const float LeftWidth = 260f;
        private const float ConsoleHeight = 120f;
        private const float TabBarHeight = 26f;
        private const float RowHeight = 26f;
        private const int MaxMonsterRows = 120;
        private const int MaxSkillRows = 8;
        private const int MaxBindingRows = 3;
        private const int MaxDropRows = 10;
        private const int MaxSpawnRows = 4;

        private static MonsterEditorWindow _instance;

        private readonly string[] _rankNames = { "普通", "精英", "稀有精英", "首领", "世界 BOSS" };
        private readonly string[] _speciesNames = { "人形", "野兽", "元素", "龙类", "巨人", "亡灵", "机械", "昆虫" };
        private readonly string[] _domainNames = { "未定域", "金", "木", "水", "火", "土" };
        private readonly string[] _standNames = { "站立", "坐姿", "卧姿", "潜水" };
        private readonly string[] _profileNames = { "0 笨（全关）", "1 战（威胁表）", "2 精（+技能+走图+求援）", "3 王（全开+战术）" };
        private readonly string[] _triNames = { "跟档位", "强制关", "强制开" };
        private readonly string[] _yesNoNames = { "否", "是" };
        private readonly string[] _followNames = { "步行（受图）", "直线（穿墙）", "飞行", "站桩" };
        private readonly string[] _slotNames = { "战斗中", "非战斗", "残血应急", "受控时" };
        private readonly string[] _targetNames = { "当前威胁", "仇恨内随机", "最近", "血最低", "AOE 以目标为心" };
        private readonly string[] _modeNames = { "往返", "循环", "随机下一点" };
        private readonly string[] _conditionNames = { "无条件", "任务持有者", "组队", "稀有度门槛" };

        private readonly List<MonsterRow> _monsterRows = new List<MonsterRow>();
        private readonly Button[] _listButtons = new Button[MaxMonsterRows];
        private readonly SkillRow[] _skillRows = new SkillRow[MaxSkillRows];
        private readonly BindingRow[] _bindingRows = new BindingRow[MaxBindingRows];
        private readonly DropRow[] _dropRows = new DropRow[MaxDropRows];
        private readonly SpawnRow[] _spawnRows = new SpawnRow[MaxSpawnRows];

        private Panel _toolbar;
        private Panel _leftPanel;
        private Panel _tabBar;
        private Panel _formHost;
        private Panel _viewHost;
        private Panel _consolePanel;
        private TextBox _consoleBox;
        private Label _statusLabel;
        private TextBox _cliPathBox;

        private Panel _formBase;
        private Panel _formBehavior;
        private Panel _formSkills;
        private Panel _formPatrol;
        private Panel _formLoot;
        private Panel _formXp;

        // 基础面板
        private TextBox _idBox;
        private TextBox _nameBox;
        private Dropdown _rankDrop;
        private Dropdown _speciesDrop;
        private Dropdown _domainDrop;
        private TextBox _modelKeyBox;
        private TextBox _prefabGuidBox;
        private TextBox _prefabPathBox;
        private Dropdown _standDrop;
        private TextBox _scaleBox;
        private TextBox _levelBaseBox;
        private TextBox _levelMinBox;
        private TextBox _levelMaxBox;
        private TextBox _hpBaseBox;
        private TextBox _hpPerBox;
        private TextBox _atkBaseBox;
        private TextBox _atkPerBox;
        private TextBox _defBaseBox;
        private TextBox _defPerBox;
        private TextBox _hpOvrBox;
        private TextBox _atkOvrBox;
        private TextBox _xpBaseBox;
        private TextBox _xpPerBox;
        private TextBox _xpModBox;
        private TextBox _healthModBox;
        private TextBox _respawnBaseBox;
        private TextBox _respawnPerBox;
        private TextBox _corpseBox;
        private TextBox _lootWindowBox;
        private TextBox _pickupDistanceBox;
        private TextBox _sayBox;
        /// <summary>
        /// 喊话文案本体。它**不在库里**（库里只有 <c>aggro_say_id</c> 那一格数字），
        /// 而在旁挂源文件 <c>Data/Monsters/monster_texts.json</c> 里，读写都走 CLI 的 set-text/get-texts。
        /// </summary>
        private TextBox _sayText;
        private Label _sayTextHint;
        /// <summary>
        /// 台词组号（AC <c>creature_text.group</c>）：同一组里的多句由运行期按 tick 挑一句。
        /// 没有这一格时"多句轮换"只能靠手改旁挂文件 —— 运行期那层（`MonsterContent.ResolveSayId`）
        /// 已经接好，而作者侧没有入口，等于这条玩法对策划不存在。
        /// </summary>
        private TextBox _sayGroupBox;
        /// <summary>上次从源文件读到的组号（用来判"组到底改没改"，与文案那份各记各的）。</summary>
        private int _sayGroupLoaded;
        /// <summary>这只怪现有的全部文本行（"同组再加一句"要用它算下一个号、组现状也由它描述）。</summary>
        private readonly List<MonsterTextGroupMath.Row> _sayRows = new List<MonsterTextGroupMath.Row>();
        /// <summary>组现状那一行提示（"组 7：第1句 #77 / 第2句 #78"）。空标签与"没接上"同形，所以永远写点什么。</summary>
        private Label _sayGroupInfo;
        /// <summary>
        /// 临终台词（组 <see cref="MonsterTextGroupMath.DeathGroup"/>）。它**没有指针**：运行期按"落在那一组"挑，
        /// 所以面板只需要一个格子 + 那一号的号；没有这一格时这条玩法只能手改旁挂文件。
        /// </summary>
        private TextBox _deathText;
        private Label _deathTextHint;
        /// <summary>上次从源文件读到的临终台词（判"改没改"）。</summary>
        private string _deathTextLoaded = string.Empty;
        /// <summary>临终台词现在挂在哪个号上（0 = 源里还没有，保存时从 `say=2` 起）。</summary>
        private int _deathSayId;
        /// <summary>应援台词（组 <see cref="MonsterTextGroupMath.AssistGroup"/>）：被同族求援叫起来时念一句。</summary>
        private TextBox _assistText;
        private Label _assistTextHint;
        private string _assistTextLoaded = string.Empty;
        private int _assistSayId;
        /// <summary>上次从源文件读到的那句（用来判"这次到底改没改"，没改就不落盘）。</summary>
        private string _sayTextLoaded = string.Empty;
        /// <summary>那句文案属于哪个号：号被改过时基线就不成立了，不能拿它判"没改"。</summary>
        private int _sayTextLoadedSayId;
        private TextBox _remarkBox;
        private Label _statsPreview;
        private Label _versionLabel;

        // 行为面板
        private Dropdown _profileDrop;
        private Dropdown _flagThreat;
        private Dropdown _flagSkills;
        private Dropdown _flagNav;
        private Dropdown _flagAssist;
        private Dropdown _flagTactics;
        private Dropdown _aggressiveDrop;
        private TextBox _aggroBox;
        private TextBox _coneBox;
        private TextBox _leashBox;
        private TextBox _evadeImmBox;
        private TextBox _assistBox;
        private TextBox _detectBox;
        private TextBox _wanderBox;
        private TextBox _idleBox;
        private TextBox _walkBox;
        private TextBox _moveBox;
        private Dropdown _followDrop;
        private TextBox _swingBox;
        private TextBox _rMinBox;
        private TextBox _rMaxBox;
        private TextBox _fleeBox;
        private Label _flagsLabel;

        // 巡逻面板
        private TextBox _routeNameBox;
        private Dropdown _modeDrop;
        private TextBox _pauseBox;
        /// <summary>选中那一个路径点的停留秒（留空 = 跟路线默认）。它覆盖 <see cref="_pauseBox"/> 那一份。</summary>
        private TextBox _pointPauseBox;
        private Dropdown _useNavDrop;
        private TextBox _pointXBox;
        private TextBox _pointYBox;
        private TextBox _pointZBox;
        private Label _routeInfo;

        // 导航烘焙（把引擎的可行走判定烘成服务端那份 MONSTERNAV 网格）
        private Dropdown _navModeDrop;
        private TextBox _navCellBox;
        private TextBox _navWidthBox;
        private TextBox _navHeightBox;
        private TextBox _navOriginXBox;
        private TextBox _navOriginYBox;
        private TextBox _navSlopeBox;
        private Label _navInfo;

        private static string[] _navModeNames()
        {
            return new[] { "NavMesh 投影", "物理射线+坡度" };
        }
        private int _pickedPointIndex = -1;

        // 经验面板
        private TextBox _previewPlayerBox;
        private Label _xpResultLabel;

        // 视图
        private BehaviorGraphView _behaviorView;
        private PatrolMapView _patrolView;
        private XpCurveView _xpView;
        private MonsterCastBarPreview _castPreview;
        private Label _castPreviewNote;

        private int _activeTab;
        private readonly Button[] _tabButtons = new Button[6];
        private MonsterDraftModel _draft = new MonsterDraftModel();
        private bool _busy;
        private List<int> _skillIds = new List<int>();
        private List<int> _itemIds = new List<int>();

        private static readonly string[] TabTitles = { "基础·外观", "行为·AI", "技能", "巡逻·寻路", "掉落", "经验" };

        /// <summary>打开或聚焦窗口（必须主线程调用）。</summary>
        public static void ShowWindow()
        {
            var editor = FlaxEditor.Editor.Instance;
            if (_instance != null)
            {
                _instance.FocusOrShow(FlaxEditor.GUI.Docking.DockState.DockRight);
                return;
            }
            _instance = new MonsterEditorWindow(editor);
            editor.Windows.Open(_instance);
            _instance.Show(FlaxEditor.GUI.Docking.DockState.DockRight);
        }

        public MonsterEditorWindow(FlaxEditor.Editor editor)
            : base(editor, false, ScrollBars.None)
        {
            Title = "怪物编辑器";

            BuildToolbar();
            BuildList();
            BuildTabBar();
            BuildFormBase();
            BuildFormBehavior();
            BuildFormSkills();
            BuildFormPatrol();
            BuildFormLoot();
            BuildFormXp();
            BuildViews();
            BuildConsole();

            SelectTab(0);
            SizeChanged += control => LayoutNow();
            LayoutNow();

            ApplyDraftToUi();
            Log("怪物编辑器就绪。先点“刷新目录”（需要已编出 Horizon.MonsterCli，且 SQL 迁移 019 已执行）。");
        }

        // ---------- 构造 ----------

        private void BuildToolbar()
        {
            _toolbar = new Panel(ScrollBars.None) { Parent = this };

            _cliPathBox = new TextBox(false, 6f, 6f, 280f)
            {
                WatermarkText = "Horizon.MonsterCli.exe 路径（留空=自动探测）",
                Parent = _toolbar,
            };

            var detect = new Button(292f, 4f, 54f, 24f) { Text = "探测", Parent = _toolbar };
            detect.Clicked += () =>
            {
                MonsterCli.ExePathOverride = _cliPathBox.Text == null ? string.Empty : _cliPathBox.Text.Trim();
                MonsterCli.ResetCache();
                var path = MonsterCli.DetectExePath();
                Log(string.IsNullOrEmpty(path) ? "没探测到 CLI，请先编一次 Horizon.MonsterCli。" : "CLI: " + path);
            };

            AddToolbarButton(352f, "刷新目录", 70f, RefreshCatalog);
            AddToolbarButton(428f, "新建", 54f, CreateNewMonster);
            AddToolbarButton(488f, "读草稿", 66f, () => LoadRedisDraft(CurrentMonsterId()));
            AddToolbarButton(560f, "校验", 54f, () => RunValidate());
            AddToolbarButton(620f, "存草稿", 66f, () => RunDraftSave());
            AddToolbarButton(692f, "保存到库", 78f, () => RunSave(false));
            AddToolbarButton(776f, "发布", 54f, () => RunPublish());
            AddToolbarButton(836f, "下线", 54f, () => RunSetState(0, "下线"));
            AddToolbarButton(896f, "作废", 54f, () => RunRetire());
            AddToolbarButton(956f, "删除", 54f, () => RunDelete());
            AddToolbarButton(1016f, "试算经验", 82f, () => RunPreviewXp(true));
            AddToolbarButton(1104f, "试算掉落", 82f, () => RunPreviewDrop());
            AddToolbarButton(1192f, "导出外观表", 82f, RunExportVisuals);
            // 文案与外观是同一族的两份"跨域边界的表"：库里/文件里改了而没重导，表现都是游戏里静默用旧的那份。
            // 少这一格，面板上那句"已写回源文件"就只写到一半（源文件新了，客户端表还是旧的）。
            AddToolbarButton(1280f, "导出文本表", 82f, RunExportTexts);
            // 保存/发布只到库；**游戏读的是内容包**（DsHost 的 --monster-content）。
            // 没有这一格，最后一个动作只能离开面板去敲 CLI——而"库里有、包里没有"的表现是世界里根本没这只怪。
            AddToolbarButton(1368f, "导出内容包", 82f, RunExportContent);
            // 内容普查（`audit`）：不选任何怪、对着**整库**问"哪一格还是零/哪条配置是死的"。
            // 它读的是 CLI 顶层 warnings（那九条"发现"的正文），所以面板能把每一条念出来。
            AddToolbarButton(1456f, "内容普查", 78f, RunContentAudit);

            _statusLabel = new Label(6f, 34f, 700f, 22f) { Text = "未载入目录", Parent = _toolbar };
        }

        private void AddToolbarButton(float x, string text, float width, Action action)
        {
            var button = new Button(x, 4f, width, 24f) { Text = text, Parent = _toolbar };
            button.Clicked += () => action();
        }

        private void BuildList()
        {
            _leftPanel = new Panel(ScrollBars.Vertical) { Parent = this };
            for (var i = 0; i < MaxMonsterRows; i++)
            {
                var row = new Button(4f, 4f + i * RowHeight, LeftWidth - 20f, RowHeight - 2f)
                {
                    Text = string.Empty,
                    Parent = _leftPanel,
                    Visible = false,
                };
                var captured = i;
                row.Clicked += () => OnPickRow(captured);
                _listButtons[i] = row;
            }
        }

        private void BuildTabBar()
        {
            _tabBar = new Panel(ScrollBars.None) { Parent = this };
            for (var i = 0; i < TabTitles.Length; i++)
            {
                var captured = i;
                var button = new Button(4f + i * 92f, 2f, 88f, TabBarHeight - 4f) { Text = TabTitles[i], Parent = _tabBar };
                button.Clicked += () => SelectTab(captured);
                _tabButtons[i] = button;
            }
        }

        private Panel NewForm()
        {
            return new Panel(ScrollBars.Vertical) { Parent = _formHost ?? (_formHost = new Panel(ScrollBars.None) { Parent = this }) };
        }

        private Label Caption(Panel host, float x, ref float y, string text, float width = 76f)
        {
            var label = new Label(x, y, width, 20f) { Text = text, Parent = host };
            return label;
        }

        private TextBox Number(Panel host, ref float y, string captionText, string watermark, float captionWidth = 76f, float boxWidth = 66f)
        {
            var box = new TextBox(false, captionWidth + 2f + 0f, y, boxWidth) { Parent = host, WatermarkText = watermark };
            new Label(2f, y, captionWidth, 20f) { Text = captionText, Parent = host };
            y += RowHeight;
            return box;
        }

        private TextBox NumberPair(Panel host, ref float y, string leftCaption, TextBox leftBox, string rightCaption, float rightX, float rightWidth)
        {
            new Label(rightX, y, rightWidth, 20f) { Text = rightCaption, Parent = host };
            var rightBox = new TextBox(false, rightX, y + 20f, rightWidth) { Parent = host };
            return rightBox;
        }

        private Dropdown Drop(Panel host, ref float y, string captionText, string[] items, float captionWidth = 76f, float boxWidth = 120f)
        {
            new Label(2f, y, captionWidth, 20f) { Text = captionText, Parent = host };
            var dropdown = new Dropdown
            {
                Parent = host,
                Bounds = new Rectangle(captionWidth + 2f, y, boxWidth, 22f),
            };
            for (var i = 0; i < items.Length; i++) dropdown.AddItem(items[i]);
            dropdown.SelectedIndex = 0;
            y += RowHeight;
            return dropdown;
        }

        private void BuildFormBase()
        {
            EnsureFormHost();
            _formBase = new Panel(ScrollBars.Vertical) { Parent = _formHost };
            var y = 6f;
            new Label(2f, y, 260f, 20f) { Text = "① 编号、外观与公式", Parent = _formBase };
            y += 24f;

            _idBox = new TextBox(false, 78f, y, 110f) { Parent = _formBase, WatermarkText = "怪物编号" };
            new Label(2f, y, 74f, 20f) { Text = "编号", Parent = _formBase };
            _versionLabel = new Label(196f, y, 150f, 20f) { Text = "v-", Parent = _formBase };
            y += RowHeight;

            _nameBox = new TextBox(false, 78f, y, 240f) { Parent = _formBase, WatermarkText = "显示名（空名会退回 Monster_<id>）" };
            new Label(2f, y, 74f, 20f) { Text = "名称", Parent = _formBase };
            y += RowHeight;

            _rankDrop = Drop(_formBase, ref y, "档位 rank", _rankNames);
            _speciesDrop = Drop(_formBase, ref y, "种族", _speciesNames);
            _domainDrop = Drop(_formBase, ref y, "五行域", _domainNames);
            _standDrop = Drop(_formBase, ref y, "待机姿态", _standNames);

            _modelKeyBox = new TextBox(false, 78f, y, 240f) { Parent = _formBase, WatermarkText = "model_key" };
            new Label(2f, y, 74f, 20f) { Text = "模型键", Parent = _formBase };
            y += RowHeight;
            _prefabGuidBox = new TextBox(false, 78f, y, 240f) { Parent = _formBase, WatermarkText = "prefab Guid（意图，跨打包不稳定）" };
            new Label(2f, y, 74f, 20f) { Text = "预制体", Parent = _formBase };
            y += RowHeight;
            _prefabPathBox = new TextBox(false, 78f, y, 240f) { Parent = _formBase, WatermarkText = "Content/…/X.prefab（外观表按此路径导出）" };
            new Label(2f, y, 74f, 20f) { Text = "路径", Parent = _formBase };
            y += RowHeight;
            _scaleBox = Number(_formBase, ref y, "体型缩放", "1.0");

            _levelBaseBox = Number(_formBase, ref y, "基准等级", "12");
            _levelMinBox = Number(_formBase, ref y, "等级下界", "12");
            _levelMaxBox = Number(_formBase, ref y, "等级上界", "14");

            new Label(2f, y, 260f, 20f) { Text = "属性公式（Hp = 截距 + 每级 × 等级 × 档位倍率）", Parent = _formBase };
            y += 22f;
            _hpBaseBox = Number(_formBase, ref y, "生命截距", "100");
            _hpPerBox = Number(_formBase, ref y, "生命每级", "0");
            _atkBaseBox = Number(_formBase, ref y, "攻击截距", "10");
            _atkPerBox = Number(_formBase, ref y, "攻击每级", "0");
            _defBaseBox = Number(_formBase, ref y, "防御截距", "0");
            _defPerBox = Number(_formBase, ref y, "防御每级", "0");
            _hpOvrBox = Number(_formBase, ref y, "生命覆盖bp", "0=不覆盖");
            _atkOvrBox = Number(_formBase, ref y, "攻击覆盖bp", "0=不覆盖");
            _statsPreview = new Label(2f, y, 320f, 36f) { Text = "属性预览：-", Parent = _formBase };
            y += 40f;

            new Label(2f, y, 260f, 20f) { Text = "经验与节奏", Parent = _formBase };
            y += 22f;
            _xpBaseBox = Number(_formBase, ref y, "经验截距", "45");
            _xpPerBox = Number(_formBase, ref y, "经验每级", "5");
            // 两个逐模板倍率（魔兽的 ModExperience / ModHealth）。没有格子就等于这条语义对策划不可达：
            // "剧情怪三倍经验"、"血薄洞穴怪给三成"只能靠 CLI 改库。缺省都填 10000=不开。
            _xpModBox = Number(_formBase, ref y, "经验倍率bp", "10000");
            _healthModBox = Number(_formBase, ref y, "血薄削减bp", "10000");
            _respawnBaseBox = Number(_formBase, ref y, "刷新截距秒", "15");
            _respawnPerBox = Number(_formBase, ref y, "刷新每级秒", "0.5");
            _corpseBox = Number(_formBase, ref y, "尸体秒", "60");
            // 拾取窗口两格（2026-10-28 拍板：缺省取 AC 的 60 秒 / 5.5 米，但怪物可逐只改）。
            _lootWindowBox = Number(_formBase, ref y, "拾取窗口秒", "60");
            _pickupDistanceBox = Number(_formBase, ref y, "拾取距离米", "5.5");
            _sayBox = Number(_formBase, ref y, "索敌喊话id", "0");
            // 台词组：同一组里写多句，运行期按 tick 挑一句（AC creature_text 的 group）。
            // 0 = 不成组（只有这一句），负数没有含义。
            _sayGroupBox = Number(_formBase, ref y, "台词组号", "0=不成组（同组多句每轮挑一句）");
            // 一次只编一条台词，所以"再加一句"必须有一个按下去就换号的动作，
            // 否则要手抄一个没用过的号、还要绕过"号改了而格子里是旧那句"的守卫。
            var addSayLine = new Button(150f, y - RowHeight, 150f, 22f) { Text = "同组再加一句", Parent = _formBase };
            addSayLine.Clicked += AddSayLineToGroup;
            // 文案本体**不在库里**（上面那一格只存号），住在旁挂源文件 Data/Monsters/monster_texts.json。
            // 没有这一格，"进战喊话"对策划就是半条功能：号填了、进游戏一句都不念，
            // 而他会先去查聊天频道为什么不出现（这条工程里真的走过一遍）。
            _sayText = new TextBox(true, 78f, y, 300f) { Parent = _formBase, Wrapping = TextWrapping.WrapWords, Height = 52f };
            new Label(2f, y, 74f, 20f) { Text = "喊话文案", Parent = _formBase };
            y += 56f;
            _sayTextHint = new Label(2f, y, 320f, 30f) { Text = "文案：- （在旁挂文件里，不在库中）", Parent = _formBase };
            y += 32f;
            _sayGroupInfo = new Label(2f, y, 320f, 20f) { Text = "台词组：未成组（组号 0 只有这一句）", Parent = _formBase };
            y += 24f;
            // 临终台词：跑在"组 2"上，没有行为表指针（管理它的只有旁挂源文件那一组）。
            _deathText = new TextBox(true, 78f, y, 300f) { Parent = _formBase, Wrapping = TextWrapping.WrapWords, Height = 52f };
            new Label(2f, y, 74f, 20f) { Text = "临终台词", Parent = _formBase };
            y += 56f;
            _deathTextHint = new Label(2f, y, 330f, 30f)
            {
                Text = "临终台词：- （组 " + MonsterTextGroupMath.DeathGroup + "：怪死时随机念一句；空 = 不念）",
                Wrapping = TextWrapping.WrapWords,
                Parent = _formBase,
            };
            y += 32f;
            // 应援台词：跑在"组 3"上（被同族求援叫起来的那一拍），同样没有行为表指针。
            _assistText = new TextBox(true, 78f, y, 300f) { Parent = _formBase, Wrapping = TextWrapping.WrapWords, Height = 52f };
            new Label(2f, y, 74f, 20f) { Text = "应援台词", Parent = _formBase };
            y += 56f;
            _assistTextHint = new Label(2f, y, 330f, 30f)
            {
                Text = "应援台词：- （组 " + MonsterTextGroupMath.AssistGroup + "：被同族叫起来时念一句；空 = 不念）",
                Wrapping = TextWrapping.WrapWords,
                Parent = _formBase,
            };
            y += 32f;
            _remarkBox = new TextBox(true, 78f, y, 300f) { Parent = _formBase, Wrapping = TextWrapping.WrapWords, Height = 52f };
            new Label(2f, y, 74f, 20f) { Text = "备注", Parent = _formBase };
            y += 56f;
        }

        private void BuildFormBehavior()
        {
            EnsureFormHost();
            _formBehavior = new Panel(ScrollBars.Vertical) { Parent = _formHost };
            var y = 6f;
            new Label(2f, y, 300f, 20f) { Text = "② AI 档位与行为（档位=预设，覆盖=真开关）", Parent = _formBehavior };
            y += 24f;

            _profileDrop = Drop(_formBehavior, ref y, "智能档位", _profileNames, 76f, 190f);
            _flagThreat = Drop(_formBehavior, ref y, "威胁表", _triNames);
            _flagSkills = Drop(_formBehavior, ref y, "技能", _triNames);
            _flagNav = Drop(_formBehavior, ref y, "走图", _triNames);
            _flagAssist = Drop(_formBehavior, ref y, "求援", _triNames);
            _flagTactics = Drop(_formBehavior, ref y, "战术", _triNames);
            _aggressiveDrop = Drop(_formBehavior, ref y, "主动索敌", _yesNoNames);

            _flagsLabel = new Label(2f, y, 320f, 36f) { Text = "展开：-", Parent = _formBehavior };
            y += 40f;

            _aggroBox = Number(_formBehavior, ref y, "索敌半径", "10");
            _coneBox = Number(_formBehavior, ref y, "朝向锥(度)", "360");
            _leashBox = Number(_formBehavior, ref y, "脱战半径", "30");
            _evadeImmBox = Number(_formBehavior, ref y, "回位免疫秒", "3");
            _assistBox = Number(_formBehavior, ref y, "求援半径", "10");
            _detectBox = Number(_formBehavior, ref y, "检测位", "1=视觉");
            _wanderBox = Number(_formBehavior, ref y, "踱步半径", "6");
            _idleBox = Number(_formBehavior, ref y, "转巡逻秒", "3");
            _walkBox = Number(_formBehavior, ref y, "巡逻速度", "2.5");
            _moveBox = Number(_formBehavior, ref y, "追击速度", "4");
            _followDrop = Drop(_formBehavior, ref y, "移动方式", _followNames);
            _swingBox = Number(_formBehavior, ref y, "挥击间隔", "1.5");
            _rMinBox = Number(_formBehavior, ref y, "保持距离下界", "0（需战术位）");
            _rMaxBox = Number(_formBehavior, ref y, "保持距离上界", "0（需战术位）");
            _fleeBox = Number(_formBehavior, ref y, "逃跑阈值%", "0（需战术位）");
        }

        private void BuildFormSkills()
        {
            EnsureFormHost();
            _formSkills = new Panel(ScrollBars.Vertical) { Parent = _formHost };
            var y = 6f;
            new Label(2f, y, 340f, 20f) { Text = "③ 技能挂载（复用玩家技能 id；上限 16 行）", Parent = _formSkills };
            y += 22f;

            var add = new Button(2f, y, 90f, 24f) { Text = "加一行", Parent = _formSkills };
            add.Clicked += () =>
            {
                CommitAll();
                if (_draft.Skills.Count >= MaxSkillRows)
                {
                    Log("窗口一次只摆 " + MaxSkillRows + " 行（服务端上限 16）。先删一行再加。");
                    return;
                }
                _draft.Skills.Add(new MonsterSkillModel { SkillId = _skillIds.Count > 0 ? _skillIds[0] : 0, SlotSeq = _draft.Skills.Count });
                ApplySkillsToUi();
            };
            var clear = new Button(98f, y, 70f, 24f) { Text = "清空", Parent = _formSkills };
            clear.Clicked += () =>
            {
                _draft.Skills.Clear();
                ApplySkillsToUi();
                Log("已清空技能挂载（未保存）。");
            };
            y += 30f;

            for (var i = 0; i < MaxSkillRows; i++)
            {
                var row = new SkillRow(_formSkills, y, i);
                _skillRows[i] = row;
                y += SkillRow.Height;
            }

            // 施法条预览：让玩家"改完就看见后果"。判据不在这里，色表/取位在 MonsterPreviewMath 的镜像里，
            // 与游戏内真值由 MonsterEditorContractTests 逐值对账。
            y += 8f;
            var preview = new Button(2f, y, 150f, 24f) { Text = "预览第 1 行的条子", Parent = _formSkills };
            preview.Clicked += RefreshCastBarPreview;
            _castPreviewNote = new Label(158f, y, 210f, 22f) { Text = "未预览", Parent = _formSkills };
            y += 28f;
            _castPreview = new MonsterCastBarPreview { Parent = _formSkills, Bounds = new Rectangle(2f, y, 366f, 96f) };
            y += 100f;
        }

        /// <summary>把第一行（或唯一一行）技能挂载喂给施法条预览控件。</summary>
        private void RefreshCastBarPreview()
        {
            CommitAll();
            if (_castPreview == null) return;

            if (_draft.Skills.Count == 0)
            {
                // "没挂技能"与"挂了但条子看不见"是两件事，必须说清是哪一件。
                _castPreview.SetData(0, 0, true, false, 1, 0f, string.Empty);
                if (_castPreviewNote != null) _castPreviewNote.Text = "这只怪没挂技能：不会有条子";
                Log("施法条预览：技能挂载为空。");
                return;
            }

            var s = _draft.Skills[0];
            _castPreview.SetData(s.CastMs, s.SchoolMask, s.Interruptible != 0, s.AllowMovement != 0,
                                s.TargetCount, (float)s.AoeRadius,
                                s.CastMs <= 0 ? "这一行是瞬发（castMs=0）：游戏里不显示条子" : string.Empty);
            if (_castPreviewNote != null)
                _castPreviewNote.Text = string.Format("预览第 1 行：技能 {0}", s.SkillId);
            Log(string.Format("施法条预览：技能 {0}｜{1}ms｜系别位 {2}｜可打断 {3}",
                s.SkillId, s.CastMs, s.SchoolMask, s.Interruptible != 0 ? "是" : "否"));
        }

        private void BuildFormPatrol()
        {
            EnsureFormHost();
            _formPatrol = new Panel(ScrollBars.Vertical) { Parent = _formHost };
            var y = 6f;
            new Label(2f, y, 340f, 20f) { Text = "④ 巡逻路线与刷怪点（服务端米制：X=右，Y=深度，Z=高）", Parent = _formPatrol };
            y += 22f;

            _routeNameBox = new TextBox(false, 78f, y, 200f) { Parent = _formPatrol, WatermarkText = "路线名（留空自动）" };
            new Label(2f, y, 74f, 20f) { Text = "路线名", Parent = _formPatrol };
            y += RowHeight;
            _modeDrop = Drop(_formPatrol, ref y, "行进方式", _modeNames);
            _pauseBox = Number(_formPatrol, ref y, "每点停留秒", "2");
            // 点级停留：上面那一格是"每点默认"，这一格覆盖**选中的那一个点**。
            // 这格在数据面早就通了（库列 pause_seconds → 草稿 pauseSeconds → 快照 → 内核那条覆盖式子），
            // 面板里却只在选中提示上**读**得到、写不进去 ⇒ "瞭望点停 7 秒而路上不停"这种形状
            // 对策划不可达（AC 实测：97.8% 的点不停，非零 delay 的中位是 7 秒，正是"个别点停"）。
            _pointPauseBox = Number(_formPatrol, ref y, "选中点停留秒", "留空=跟路线");
            var applyPointPause = new Button(2f, y, 150f, 24f) { Text = "写入选中点", Parent = _formPatrol };
            applyPointPause.Clicked += ApplyPointPause;
            var clearPointPause = new Button(158f, y, 90f, 24f) { Text = "改为跟路线", Parent = _formPatrol };
            clearPointPause.Clicked += () =>
            {
                if (_pointPauseBox != null) _pointPauseBox.Text = string.Empty;
                ApplyPointPause();
            };
            y += 30f;
            _useNavDrop = Drop(_formPatrol, ref y, "走图连段", _yesNoNames);

            var pick = new Button(2f, y, 150f, 24f) { Text = "拾取选中 Actor 为点", Parent = _formPatrol };
            pick.Clicked += CapturePointFromSelection;
            var clearPoint = new Button(158f, y, 80f, 24f) { Text = "删选中点", Parent = _formPatrol };
            clearPoint.Clicked += RemovePickedPoint;
            var clearAll = new Button(244f, y, 70f, 24f) { Text = "清空点", Parent = _formPatrol };
            clearAll.Clicked += () =>
            {
                if (_draft.PatrolRoute == null) return;
                _draft.PatrolRoute.Points.Clear();
                _pickedPointIndex = -1;
                ApplyRouteToUi();
                Log("已清空路径点（未保存）。");
            };
            y += 30f;

            new Label(2f, y, 74f, 20f) { Text = "手加一点", Parent = _formPatrol };
            _pointXBox = new TextBox(false, 78f, y, 60f) { Parent = _formPatrol, WatermarkText = "X" };
            _pointYBox = new TextBox(false, 142f, y, 60f) { Parent = _formPatrol, WatermarkText = "Y" };
            _pointZBox = new TextBox(false, 206f, y, 60f) { Parent = _formPatrol, WatermarkText = "Z" };
            var addManual = new Button(270f, y, 60f, 22f) { Text = "添加", Parent = _formPatrol };
            addManual.Clicked += AddManualPoint;
            y += RowHeight;

            _routeInfo = new Label(2f, y, 330f, 20f) { Text = "点数：0", Parent = _formPatrol };
            y += 24f;

            new Label(2f, y, 340f, 20f) { Text = "刷怪点（上行：位置/数量/等级覆盖；中行：朝向/刷新覆盖/启停；下行：只在哪一片刷）", Parent = _formPatrol };
            y += 22f;
            for (var i = 0; i < MaxSpawnRows; i++)
            {
                _spawnRows[i] = new SpawnRow(_formPatrol, y, i);
                y += SpawnRow.Height;
            }

            // ---- 导航网格烘焙（把"这片地方能不能走"从引擎问出来，交给 CLI 装配成服务端那份 MONSTERNAV）----
            y += 10f;
            new Label(2f, y, 360f, 20f) { Text = "导航烘焙（自动寻路的真实网格；掩码交 CLI 装配）", Parent = _formPatrol };
            y += 22f;

            _navModeDrop = Drop(_formPatrol, ref y, "判据", _navModeNames(), 62f, 150f);
            _navCellBox = Number(_formPatrol, ref y, "格长(米)", "4");
            _navWidthBox = Number(_formPatrol, ref y, "宽(格)", "64");
            _navHeightBox = Number(_formPatrol, ref y, "高(格)", "64");
            _navOriginXBox = Number(_formPatrol, ref y, "原点X", "-128");
            _navOriginYBox = Number(_formPatrol, ref y, "原点Y", "-128");
            _navSlopeBox = Number(_formPatrol, ref y, "坡度上限°", "45");

            var bake = new Button(2f, y, 150f, 24f) { Text = "烘焙并写文件", Parent = _formPatrol };
            bake.Clicked += BakeNavGridFromScene;
            _navInfo = new Label(158f, y, 200f, 20f) { Text = "未烘焙", Parent = _formPatrol };
            y += RowHeight;
        }

        /// <summary>烘焙用的格数上限（单边）与总格数上限：超了会把编辑器主线程卡住太久，宁可让人分块烘。</summary>
        private const int MaxNavSide = 512;
        private const int MaxNavCells = 65536;

        /// <summary>
        /// 射线探针的垂直跨度（Flax 单位）：从足够高处打到足够低处，
        /// 这样地形不管当前海拔是多少都能被穿到；只测"这一段里有没有可站的地面"。
        /// </summary>
        private const float NavProbeUp = 100000f;
        private const float NavProbeDown = -100000f;

        private void BuildFormLoot()
        {
            EnsureFormHost();
            _formLoot = new Panel(ScrollBars.Vertical) { Parent = _formHost };
            var y = 6f;
            new Label(2f, y, 340f, 20f) { Text = "⑤ 掉落：一怪 N 表 + 逐件概率（组>0 同组互斥；表级件数区间=这张表的组重掷几轮）", Parent = _formLoot };
            y += 22f;

            var addBinding = new Button(2f, y, 90f, 24f) { Text = "加一张表", Parent = _formLoot };
            addBinding.Clicked += () =>
            {
                CommitAll();
                if (_draft.LootBindings.Count >= MaxBindingRows)
                {
                    Log("窗口一次只摆 " + MaxBindingRows + " 张表（服务端上限 8）。");
                    return;
                }
                _draft.LootBindings.Add(new MonsterLootBindingModel
                {
                    LootTableId = _draft.WuxingDomain > 0 ? _draft.WuxingDomain : 1,
                    Entries = new List<MonsterDropModel>(),
                });
                ApplyLootToUi();
            };
            y += 30f;

            for (var i = 0; i < MaxBindingRows; i++)
            {
                _bindingRows[i] = new BindingRow(_formLoot, y, i);
                y += BindingRow.Height;
            }

            new Label(2f, y, 340f, 34f)
            {
                Text = "逐件条目（“所属表”定归属；组=0 每件独立掷，组>0 同组最多出一件、组内填 0 是平分剩余预算；条件默认“继承表”，可逐件覆盖成“需持有任务”）",
                Wrapping = TextWrapping.WrapWords,
                Parent = _formLoot,
            };
            y += 34f;
            for (var i = 0; i < MaxDropRows; i++)
            {
                _dropRows[i] = new DropRow(_formLoot, y, i);
                y += DropRow.Height;
            }
        }

        private void BuildFormXp()
        {
            EnsureFormHost();
            _formXp = new Panel(ScrollBars.Vertical) { Parent = _formHost };
            var y = 6f;
            new Label(2f, y, 340f, 20f) { Text = "⑥ 击杀经验（定基=怪物等级，玩家等级通过让分衰减）", Parent = _formXp };
            y += 24f;
            new Label(2f, y, 340f, 36f)
            {
                Text = "同级 ×1.0；高于我：×(1+0.05×差) 上限 1.2；低于我：×(1-差/ZD)；灰名直接断供 0。",
                Wrapping = TextWrapping.WrapWords,
                Parent = _formXp,
            };
            y += 40f;

            _previewPlayerBox = Number(_formXp, ref y, "假设我等级", "20");
            var preview = new Button(2f, y, 110f, 24f) { Text = "本地重画曲线", Parent = _formXp };
            preview.Clicked += () => { CommitAll(); RefreshViews(); };
            var server = new Button(118f, y, 130f, 24f) { Text = "服务端试算(对拍)", Parent = _formXp };
            server.Clicked += () => RunPreviewXp(false);
            y += 30f;

            _xpResultLabel = new Label(2f, y, 330f, 60f)
            {
                Text = "结论：-",
                Wrapping = TextWrapping.WrapWords,
                Parent = _formXp,
            };
            y += 66f;
            new Label(2f, y, 330f, 54f)
            {
                Text = "参与：最多 5 人分享；每人按**自己等级**独立算一份（服务端 tag 窗口内摸过就算）。",
                Wrapping = TextWrapping.WrapWords,
                Parent = _formXp,
            };
        }

        private void EnsureFormHost()
        {
            if (_formHost != null) return;
            _formHost = new Panel(ScrollBars.None) { Parent = this };
        }

        private void BuildViews()
        {
            _viewHost = new Panel(ScrollBars.None) { Parent = this };
            _behaviorView = new BehaviorGraphView { Parent = _viewHost, Visible = false };
            _patrolView = new PatrolMapView { Parent = _viewHost, Visible = false };
            _xpView = new XpCurveView { Parent = _viewHost, Visible = false };
            _patrolView.PointPicked += index =>
            {
                _pickedPointIndex = index;
                var p = _draft.PatrolRoute != null && index < _draft.PatrolRoute.Points.Count ? _draft.PatrolRoute.Points[index] : null;
                // 选中就把这一格的现值灌进去：**不灌的话格子显示的是上一个点的值**，
                // 按下"写入选中点"就会把上一个点的秒数盖到这一个点上（一格改动串到另一格）。
                if (_pointPauseBox != null)
                    _pointPauseBox.Text = p != null && p.PauseSeconds.HasValue
                        ? p.PauseSeconds.Value.ToString("0.##", CultureInfo.InvariantCulture)
                        : string.Empty;
                Log(p == null
                    ? "点 " + (index + 1) + " 已不在草稿里"
                    : string.Format("选中点 {0}：({1:0.##}, {2:0.##}, {3:0.##}) 停留 {4}",
                        index + 1, p.X, p.Y, p.Z, p.PauseSeconds.HasValue ? p.PauseSeconds.Value.ToString("0.#") : "默认"));
            };
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

        // ---------- 布局 ----------

        private void LayoutNow()
        {
            if (_toolbar == null || _statusLabel == null) return;

            var width = Width;
            var height = Height;
            var bodyTop = ToolbarHeight + TabBarHeight;
            var bodyHeight = Math.Max(160f, height - bodyTop - ConsoleHeight - 8f);
            var formWidth = Math.Max(240f, width * 0.34f);
            var viewWidth = Math.Max(200f, width - LeftWidth - formWidth - 30f);

            _toolbar.Bounds = new Rectangle(0f, 0f, width, ToolbarHeight);
            _leftPanel.Bounds = new Rectangle(0f, ToolbarHeight, LeftWidth, bodyHeight);
            _tabBar.Bounds = new Rectangle(LeftWidth + 6f, ToolbarHeight, Math.Max(0f, width - LeftWidth - 12f), TabBarHeight);
            if (_formHost != null)
            {
                _formHost.Bounds = new Rectangle(LeftWidth + 6f, bodyTop, formWidth, bodyHeight - TabBarHeight);
                if (_formBase != null) _formBase.Bounds = new Rectangle(0f, 0f, formWidth, bodyHeight - TabBarHeight);
                if (_formBehavior != null) _formBehavior.Bounds = new Rectangle(0f, 0f, formWidth, bodyHeight - TabBarHeight);
                if (_formSkills != null) _formSkills.Bounds = new Rectangle(0f, 0f, formWidth, bodyHeight - TabBarHeight);
                if (_formPatrol != null) _formPatrol.Bounds = new Rectangle(0f, 0f, formWidth, bodyHeight - TabBarHeight);
                if (_formLoot != null) _formLoot.Bounds = new Rectangle(0f, 0f, formWidth, bodyHeight - TabBarHeight);
                if (_formXp != null) _formXp.Bounds = new Rectangle(0f, 0f, formWidth, bodyHeight - TabBarHeight);
            }
            if (_viewHost != null)
            {
                _viewHost.Bounds = new Rectangle(LeftWidth + formWidth + 18f, bodyTop, viewWidth, bodyHeight - TabBarHeight);
                if (_behaviorView != null) _behaviorView.Bounds = new Rectangle(0f, 0f, viewWidth, bodyHeight - TabBarHeight);
                if (_patrolView != null) _patrolView.Bounds = new Rectangle(0f, 0f, viewWidth, bodyHeight - TabBarHeight);
                if (_xpView != null) _xpView.Bounds = new Rectangle(0f, 0f, viewWidth, bodyHeight - TabBarHeight);
            }
            _consolePanel.Bounds = new Rectangle(0f, height - ConsoleHeight, width, ConsoleHeight);
            _consoleBox.Bounds = new Rectangle(4f, 4f, width - 8f, ConsoleHeight - 8f);
        }

        private void SelectTab(int index)
        {
            _activeTab = index;
            if (_formBase != null) _formBase.Visible = index == 0;
            if (_formBehavior != null) _formBehavior.Visible = index == 1;
            if (_formSkills != null) _formSkills.Visible = index == 2;
            if (_formPatrol != null) _formPatrol.Visible = index == 3;
            if (_formLoot != null) _formLoot.Visible = index == 4;
            if (_formXp != null) _formXp.Visible = index == 5;

            if (_behaviorView != null) _behaviorView.Visible = index == 1 || index == 2;
            if (_patrolView != null) _patrolView.Visible = index == 3;
            if (_xpView != null) _xpView.Visible = index == 5 || index == 0;

            RefreshViews();
        }

        // ---------- 草稿 ↔ UI ----------

        private int CurrentMonsterId()
        {
            CommitAll();
            return _draft.MonsterId;
        }

        private void CommitAll()
        {
            if (_draft == null) return;
            var d = _draft;

            d.MonsterId = ParseInt(_idBox == null ? null : _idBox.Text, d.MonsterId);
            d.Name = _nameBox == null ? d.Name : (_nameBox.Text ?? string.Empty);
            d.Rank = IndexOf(_rankDrop);
            d.Species = IndexOf(_speciesDrop);
            d.WuxingDomain = IndexOf(_domainDrop);
            d.ModelKey = _modelKeyBox == null ? d.ModelKey : (_modelKeyBox.Text ?? string.Empty).Trim();
            d.PrefabGuid = _prefabGuidBox == null ? d.PrefabGuid : (_prefabGuidBox.Text ?? string.Empty).Trim();
            d.PrefabPath = _prefabPathBox == null ? d.PrefabPath : (_prefabPathBox.Text ?? string.Empty).Trim();
            d.StandState = IndexOf(_standDrop);
            d.ModelScale = ParseDouble(_scaleBox, d.ModelScale);
            d.LevelBase = ParseInt(_levelBaseBox, d.LevelBase);
            d.LevelMin = ParseInt(_levelMinBox, d.LevelMin);
            d.LevelMax = ParseInt(_levelMaxBox, d.LevelMax);

            d.Stats.HpBase = ParseInt(_hpBaseBox, d.Stats.HpBase);
            d.Stats.HpPerLevel = ParseInt(_hpPerBox, d.Stats.HpPerLevel);
            d.Stats.AtkBase = ParseInt(_atkBaseBox, d.Stats.AtkBase);
            d.Stats.AtkPerLevel = ParseInt(_atkPerBox, d.Stats.AtkPerLevel);
            d.Stats.DefBase = ParseInt(_defBaseBox, d.Stats.DefBase);
            d.Stats.DefPerLevel = ParseInt(_defPerBox, d.Stats.DefPerLevel);
            d.Stats.HpOverrideBps = ParseInt(_hpOvrBox, d.Stats.HpOverrideBps);
            d.Stats.AtkOverrideBps = ParseInt(_atkOvrBox, d.Stats.AtkOverrideBps);

            d.Xp.XpBase = ParseInt(_xpBaseBox, d.Xp.XpBase);
            d.Xp.XpPerLevel = ParseInt(_xpPerBox, d.Xp.XpPerLevel);
            // 空格按 10000（=不开）而不是 0：留一格空白不该把这只怪的经验归零。
            d.Xp.XpModBps = ParseInt(_xpModBox, 10000);
            d.Xp.HealthModBps = ParseInt(_healthModBox, 10000);
            d.RespawnBaseSeconds = ParseDouble(_respawnBaseBox, d.RespawnBaseSeconds);
            d.RespawnPerLevelSeconds = ParseDouble(_respawnPerBox, d.RespawnPerLevelSeconds);
            d.CorpseSeconds = ParseDouble(_corpseBox, d.CorpseSeconds);
            d.LootWindowSeconds = ParseDouble(_lootWindowBox, d.LootWindowSeconds);
            d.PickupDistanceMeters = ParseDouble(_pickupDistanceBox, d.PickupDistanceMeters);
            d.AggroSayId = ParseInt(_sayBox, d.AggroSayId);
            d.Remark = _remarkBox == null ? d.Remark : (_remarkBox.Text ?? string.Empty);

            var b = d.Behavior;
            b.AiProfile = IndexOf(_profileDrop);
            b.FlagUseThreat = TriOf(_flagThreat);
            b.FlagUseSkills = TriOf(_flagSkills);
            b.FlagUseNavMesh = TriOf(_flagNav);
            b.FlagUseAssist = TriOf(_flagAssist);
            b.FlagUseTactics = TriOf(_flagTactics);
            b.IsAggressive = IndexOf(_aggressiveDrop);
            b.AggroRadius = ParseDouble(_aggroBox, b.AggroRadius);
            b.AggroConeDegrees = ParseDouble(_coneBox, b.AggroConeDegrees);
            b.LeashRadius = ParseDouble(_leashBox, b.LeashRadius);
            b.EvadeImmunitySeconds = ParseDouble(_evadeImmBox, b.EvadeImmunitySeconds);
            b.AssistRadius = ParseDouble(_assistBox, b.AssistRadius);
            b.DetectMask = ParseInt(_detectBox, b.DetectMask);
            b.WanderRadius = ParseDouble(_wanderBox, b.WanderRadius);
            b.IdleToPatrolSeconds = ParseDouble(_idleBox, b.IdleToPatrolSeconds);
            b.WalkSpeed = ParseDouble(_walkBox, b.WalkSpeed);
            b.MoveSpeed = ParseDouble(_moveBox, b.MoveSpeed);
            b.FollowType = IndexOf(_followDrop);
            b.MeleeSwingSeconds = ParseDouble(_swingBox, b.MeleeSwingSeconds);
            b.RangedMinDistance = ParseDouble(_rMinBox, b.RangedMinDistance);
            b.RangedMaxDistance = ParseDouble(_rMaxBox, b.RangedMaxDistance);
            b.FleeHpPercent = ParseInt(_fleeBox, b.FleeHpPercent);

            // 技能行：只把"可见且填了技能 id"的行收进草稿（空行不算挂载，避免 0 号技能）
            var kept = new List<MonsterSkillModel>();
            for (var i = 0; i < MaxSkillRows; i++)
            {
                var row = _skillRows[i];
                if (row == null || !row.IsUsed()) continue;
                row.Commit(kept.Count);
                kept.Add(row.Model);
            }
            if (kept.Count > 0 || _draft.Skills.Count <= MaxSkillRows) _draft.Skills = kept;

            // 掉落：表绑定 + 逐件条目（按"所属表"序号归属）
            var bindings = new List<MonsterLootBindingModel>();
            for (var i = 0; i < MaxBindingRows; i++)
            {
                var row = _bindingRows[i];
                if (row == null || !row.IsUsed()) continue;
                row.Commit();
                bindings.Add(row.Model);
            }
            var dropped = new List<(MonsterLootBindingModel Binding, MonsterDropModel Entry)>();
            for (var i = 0; i < MaxDropRows; i++)
            {
                var row = _dropRows[i];
                if (row == null || !row.IsUsed()) continue;
                row.Commit(bindings.Count);
                dropped.Add((row.OwnerBinding, row.Model));
            }
            foreach (var pair in dropped)
            {
                if (pair.Binding != null && !pair.Binding.Entries.Contains(pair.Entry))
                    pair.Binding.Entries.Add(pair.Entry);
            }
            // 逐件顺序必须等于**屏幕上这张表里的行序**：内核把条目下标当掷骰盐值的一部分，
            // 库里 seq 列的注释写的就是「掷骰顺序」。行控件各自带一个只增不减的计数器，
            // 于是每保存一次所有 seq 一起 +1（同一件物品每存一次换一颗骰子），
            // 跨表还互相撞值（撞了就退化成按物品 id 排，与看到的行序无关）。
            // 这里按绑定重新编号，把"看到第几行"和"用第几颗骰子"钉成同一件事。
            foreach (var binding in bindings)
            {
                if (binding.Entries == null) continue;
                for (var i = 0; i < binding.Entries.Count; i++) binding.Entries[i].Seq = i + 1;
            }
            if (bindings.Count > 0 || _draft.LootBindings.Count <= MaxBindingRows) _draft.LootBindings = bindings;

            // 刷怪点
            var spawns = new List<MonsterSpawnModel>();
            for (var i = 0; i < MaxSpawnRows; i++)
            {
                var row = _spawnRows[i];
                if (row == null || !row.IsUsed()) continue;
                row.Commit();
                spawns.Add(row.Model);
            }
            if (spawns.Count > 0 || _draft.SpawnPoints.Count <= MaxSpawnRows) _draft.SpawnPoints = spawns;

            var route = d.PatrolRoute;
            if (route != null)
            {
                route.Name = _routeNameBox == null ? route.Name : (_routeNameBox.Text ?? string.Empty);
                route.Mode = IndexOf(_modeDrop);
                route.PauseAtPointSeconds = ParseDouble(_pauseBox, route.PauseAtPointSeconds);
                route.UseNavMesh = IndexOf(_useNavDrop);
                b.PatrolRouteId = route.RouteId;
            }
            else if (b.PatrolRouteId > 0)
            {
                b.PatrolRouteId = 0;
            }
        }

        private void ApplyDraftToUi()
        {
            var d = _draft;
            if (_idBox != null) _idBox.Text = d.MonsterId.ToString(CultureInfo.InvariantCulture);
            if (_nameBox != null) _nameBox.Text = d.Name;
            SetIndex(_rankDrop, d.Rank);
            SetIndex(_speciesDrop, d.Species);
            SetIndex(_domainDrop, d.WuxingDomain);
            SetIndex(_standDrop, d.StandState);
            if (_modelKeyBox != null) _modelKeyBox.Text = d.ModelKey;
            if (_prefabGuidBox != null) _prefabGuidBox.Text = d.PrefabGuid ?? string.Empty;
            if (_prefabPathBox != null) _prefabPathBox.Text = d.PrefabPath ?? string.Empty;
            SetText(_scaleBox, d.ModelScale);
            SetText(_levelBaseBox, d.LevelBase);
            SetText(_levelMinBox, d.LevelMin);
            SetText(_levelMaxBox, d.LevelMax);
            SetText(_hpBaseBox, d.Stats.HpBase);
            SetText(_hpPerBox, d.Stats.HpPerLevel);
            SetText(_atkBaseBox, d.Stats.AtkBase);
            SetText(_atkPerBox, d.Stats.AtkPerLevel);
            SetText(_defBaseBox, d.Stats.DefBase);
            SetText(_defPerBox, d.Stats.DefPerLevel);
            SetText(_hpOvrBox, d.Stats.HpOverrideBps);
            SetText(_atkOvrBox, d.Stats.AtkOverrideBps);
            SetText(_xpBaseBox, d.Xp.XpBase);
            SetText(_xpPerBox, d.Xp.XpPerLevel);
            SetText(_xpModBox, d.Xp.XpModBps);
            SetText(_healthModBox, d.Xp.HealthModBps);
            SetText(_respawnBaseBox, d.RespawnBaseSeconds);
            SetText(_respawnPerBox, d.RespawnPerLevelSeconds);
            SetText(_corpseBox, d.CorpseSeconds);
            SetText(_lootWindowBox, d.LootWindowSeconds);
            SetText(_pickupDistanceBox, d.PickupDistanceMeters);
            SetText(_sayBox, d.AggroSayId);
            LoadSayTextFromSource(d.MonsterId, d.AggroSayId);
            if (_remarkBox != null) _remarkBox.Text = d.Remark;
            if (_versionLabel != null) _versionLabel.Text = "v" + d.ContentVersion + (d.PublishState == 1 ? " 已发布" : (d.PublishState == 2 ? " 已停用" : " 草稿"));

            var b = d.Behavior;
            SetIndex(_profileDrop, b.AiProfile);
            SetTri(_flagThreat, b.FlagUseThreat);
            SetTri(_flagSkills, b.FlagUseSkills);
            SetTri(_flagNav, b.FlagUseNavMesh);
            SetTri(_flagAssist, b.FlagUseAssist);
            SetTri(_flagTactics, b.FlagUseTactics);
            SetIndex(_aggressiveDrop, b.IsAggressive);
            SetText(_aggroBox, b.AggroRadius);
            SetText(_coneBox, b.AggroConeDegrees);
            SetText(_leashBox, b.LeashRadius);
            SetText(_evadeImmBox, b.EvadeImmunitySeconds);
            SetText(_assistBox, b.AssistRadius);
            SetText(_detectBox, b.DetectMask);
            SetText(_wanderBox, b.WanderRadius);
            SetText(_idleBox, b.IdleToPatrolSeconds);
            SetText(_walkBox, b.WalkSpeed);
            SetText(_moveBox, b.MoveSpeed);
            SetIndex(_followDrop, b.FollowType);
            SetText(_swingBox, b.MeleeSwingSeconds);
            SetText(_rMinBox, b.RangedMinDistance);
            SetText(_rMaxBox, b.RangedMaxDistance);
            SetText(_fleeBox, b.FleeHpPercent);

            ApplySkillsToUi();
            ApplyLootToUi();
            ApplyRouteToUi();
            ApplySpawnsToUi();
            if (_previewPlayerBox != null && string.IsNullOrEmpty(_previewPlayerBox.Text))
                _previewPlayerBox.Text = Math.Max(1, d.LevelBase).ToString(CultureInfo.InvariantCulture);

            RefreshViews();
        }

        private void ApplySkillsToUi()
        {
            if (_skillRows[0] == null) return;
            for (var i = 0; i < MaxSkillRows; i++)
            {
                _skillRows[i].Model = i < _draft.Skills.Count ? _draft.Skills[i] : null;
                _skillRows[i].Apply(_skillIds);
            }
        }

        private void ApplyLootToUi()
        {
            if (_bindingRows[0] == null) return;
            for (var i = 0; i < MaxBindingRows; i++)
            {
                _bindingRows[i].Model = i < _draft.LootBindings.Count ? _draft.LootBindings[i] : null;
                _bindingRows[i].Apply();
            }

            // 逐件条目按顺序摊平到行池，"所属表"由行的下拉决定
            var flat = new List<MonsterLootBindingModel>();
            foreach (var binding in _draft.LootBindings) flat.Add(binding);
            for (var i = 0; i < MaxDropRows; i++)
            {
                _dropRows[i].Bind(flat, i < flat.Count ? flat[0] : null);
                _dropRows[i].Model = null;
            }

            var slot = 0;
            for (var bi = 0; bi < _draft.LootBindings.Count; bi++)
            {
                var binding = _draft.LootBindings[bi];
                for (var ei = 0; ei < binding.Entries.Count && slot < MaxDropRows; ei++)
                {
                    _dropRows[slot].Bind(flat, binding);
                    _dropRows[slot].Model = binding.Entries[ei];
                    _dropRows[slot].SetOwnerIndex(bi);
                    _dropRows[slot].Apply(_itemIds);
                    slot++;
                }
            }
            for (; slot < MaxDropRows; slot++)
            {
                _dropRows[slot].Apply(_itemIds);
            }
        }

        private void ApplyRouteToUi()
        {
            if (_routeNameBox == null) return;
            var route = _draft.PatrolRoute;
            if (route == null)
            {
                _routeNameBox.Text = string.Empty;
                SetIndex(_modeDrop, 1);
                SetText(_pauseBox, 2.0);
                SetIndex(_useNavDrop, 1);
                _routeInfo.Text = "点数：0（未建路线）";
                return;
            }
            _routeNameBox.Text = route.Name;
            SetIndex(_modeDrop, route.Mode);
            SetText(_pauseBox, route.PauseAtPointSeconds);
            SetIndex(_useNavDrop, route.UseNavMesh);
            _routeInfo.Text = string.Format("点数：{0}{1}", route.Points.Count,
                route.RouteId > 0 ? "（库内 route_id=" + route.RouteId + "）" : "（新路线，保存后分配 id）");
            RefreshViews();
        }

        private void ApplySpawnsToUi()
        {
            if (_spawnRows[0] == null) return;
            for (var i = 0; i < MaxSpawnRows; i++)
            {
                _spawnRows[i].Model = i < _draft.SpawnPoints.Count ? _draft.SpawnPoints[i] : null;
                _spawnRows[i].Apply();
            }
        }

        private void RefreshViews()
        {
            var d = _draft;
            var b = d.Behavior;
            MonsterPreviewMath.ExpandProfile(b.AiProfile, b.FlagUseThreat, b.FlagUseSkills, b.FlagUseNavMesh,
                b.FlagUseAssist, b.FlagUseTactics,
                out var threat, out var skills, out var nav, out var assist, out var tactics);

            var hasRoute = d.PatrolRoute != null && d.PatrolRoute.Points.Count >= 2;
            var note = string.Empty;
            if (skills && d.Skills.Count == 0) note = "技能位开着但一行都没挂：进战后什么都不会放。";
            else if (!skills && d.Skills.Count > 0) note = "挂了 " + d.Skills.Count + " 行但技能位关着：这些行不会生效。";

            if (_flagsLabel != null)
                _flagsLabel.Text = string.Format("展开：威胁表 {0} 技能 {1} 走图 {2} 求援 {3} 战术 {4}",
                    On(threat), On(skills), On(nav), On(assist), On(tactics));

            if (_statsPreview != null)
            {
                var hp = (d.Stats.HpBase + (long)d.Stats.HpPerLevel * Math.Max(1, d.LevelBase)) * RankHpScale(d.Rank);
                var atk = (d.Stats.AtkBase + (long)d.Stats.AtkPerLevel * Math.Max(1, d.LevelBase)) * RankDamageScale(d.Rank);
                _statsPreview.Text = string.Format("Lv{0} → 生命 {1}｜攻击 {2}（含档位倍率；覆盖 bp 另乘）",
                    d.LevelBase, (long)Math.Round(hp, 0), (long)Math.Round(atk, 0));
            }

            if (_behaviorView != null)
                _behaviorView.SetData(threat, skills, nav, assist, tactics, hasRoute, d.Skills.Count, note);

            if (_patrolView != null)
            {
                float sx = 0f, sy = 0f;
                if (d.SpawnPoints.Count > 0)
                {
                    sx = (float)d.SpawnPoints[0].X;
                    sy = (float)d.SpawnPoints[0].Y;
                }
                var points = d.PatrolRoute != null ? d.PatrolRoute.Points : null;
                var viewNote = hasRoute ? string.Empty : "少于 2 点谈不上巡逻，会退化成踱步";
                _patrolView.SetData(points, sx, sy,
                    (float)b.AggroRadius, (float)b.LeashRadius, (float)b.WanderRadius,
                    d.SpawnPoints.Count > 0 ? d.SpawnPoints[0].MaxAlive : 3,
                    d.SpawnPoints.Count > 0 ? (float)d.SpawnPoints[0].SlotSpacing : 8f,
                    d.PatrolRoute != null ? d.PatrolRoute.Mode : 1,
                    d.PatrolRoute != null && d.PatrolRoute.UseNavMesh != 0,
                    viewNote);
            }

            if (_xpView != null)
            {
                var playerLevel = ParseInt(_previewPlayerBox == null ? null : _previewPlayerBox.Text, d.LevelBase);
                _xpView.SetData(d.LevelBase, playerLevel, d.Rank, d.Xp.XpBase, d.Xp.XpPerLevel,
                    "点曲线可挪读数游标；本地曲线仅预览，结论以服务端 preview-xp 为准。",
                    d.Xp.XpModBps, d.Xp.HealthModBps);
            }
        }

        // ---------- 动作 ----------

        private void RefreshCatalog()
        {
            GuardBusy(() =>
            {
                MonsterCli.RunAsync("catalog", null, result =>
                {
                    if (!result.Ok)
                    {
                        Log(result.Display);
                        return;
                    }
                    _monsterRows.Clear();
                    if (result.HasData && result.Data.TryGetProperty("monsters", out var monsters) && monsters.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in monsters.EnumerateArray())
                        {
                            _monsterRows.Add(new MonsterRow
                            {
                                Id = ReadInt(item, "id"),
                                Name = ReadString(item, "name"),
                                Rank = ReadInt(item, "rank"),
                                Level = ReadInt(item, "levelBase"),
                                Domain = ReadInt(item, "wuxingDomain"),
                                Published = true,
                            });
                        }
                    }
                    if (result.HasData && result.Data.TryGetProperty("codeMonsters", out var codeList) && codeList.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in codeList.EnumerateArray())
                        {
                            var id = ReadInt(item, "TemplateId");
                            var known = _monsterRows.Exists(r => r.Id == id);
                            if (known) continue;
                            _monsterRows.Add(new MonsterRow
                            {
                                Id = id,
                                Name = ReadString(item, "Name") + "（仅代码目录）",
                                Level = ReadInt(item, "Level"),
                                Domain = ReadInt(item, "WuxingDomain"),
                                Published = false,
                            });
                        }
                    }
                    _skillIds = ReadIdArray(result, "skills");
                    _itemIds = ReadIdArray(result, "items");

                    RebuildList();
                    var warnings = result.HasData && result.Data.TryGetProperty("dbReachable", out var reach) && reach.ValueKind == JsonValueKind.False;
                    _statusLabel.Text = string.Format("目录：{0} 只怪｜技能 id {1} 条｜物品 id {2} 条{3}",
                        _monsterRows.Count, _skillIds.Count, _itemIds.Count, warnings ? "｜⚠ 库不可达" : string.Empty);
                    Log(result.Display);
                    ApplySkillsToUi();
                });
            });
        }

        private void RebuildList()
        {
            var count = Math.Min(_monsterRows.Count, MaxMonsterRows);
            for (var i = 0; i < MaxMonsterRows; i++)
            {
                var button = _listButtons[i];
                if (button == null) continue;
                if (i >= count)
                {
                    button.Visible = false;
                    continue;
                }
                var row = _monsterRows[i];
                button.Visible = true;
                var difficulty = MonsterPreviewMath.Of(row.Level, row.Level, row.Rank);
                button.Text = string.Format("{0} {1} Lv{2}{3}{4}",
                    row.Published ? " " : "△", row.Name, row.Level,
                    row.Rank > 0 ? "[" + _rankNames[Math.Min(row.Rank, _rankNames.Length - 1)] + "]" : string.Empty,
                    row.Domain > 0 ? "·" + _domainNames[row.Domain] : string.Empty);
            }
            if (_monsterRows.Count > MaxMonsterRows)
                Log("列表只显示前 " + MaxMonsterRows + " 行（共 " + _monsterRows.Count + " 行）——用编号载入可直接寻址。");
        }

        private void OnPickRow(int index)
        {
            if (index >= _monsterRows.Count) return;
            LoadMonster(_monsterRows[index].Id);
        }

        private void CreateNewMonster()
        {
            CommitAll();
            var next = NextMonsterId();
            _draft = MonsterDraftModel.CreateNew(next);
            _draft.PatrolRoute = new MonsterRouteModel { Points = new List<MonsterPointModel>() };
            _draft.SpawnPoints.Add(new MonsterSpawnModel
            {
                SpawnPointId = next,
                X = 20,
                MaxAlive = 3,
                SlotSpacing = 8,
            });
            ApplyDraftToUi();
            Log("新建编号 " + next + "（号段规则：≥100000 精英、≥200000 世界 BOSS、≥300000 副本）。");
        }

        /// <summary>
        /// 下一个可用编号：默认走精英段 100000+，因为 1–99999 的前段已被代码目录占用。
        /// <para>刻意不复用"最大编号 + 1"跨段取值：那会让新建号落在随机段里，号段语义就废了。</para>
        /// </summary>
        private int NextMonsterId()
        {
            var max = 0;
            foreach (var row in _monsterRows)
            {
                if (row.Id >= 100000 && row.Id < 200000 && row.Id > max) max = row.Id;
            }
            return max >= 100000 ? max + 1 : 100000;
        }

        private void LoadMonster(int monsterId)
        {
            if (monsterId <= 0)
            {
                Log("请先填编号，或从左侧列表选一只怪。");
                return;
            }
            GuardBusy(() => MonsterCli.RunAsync("get " + monsterId.ToString(CultureInfo.InvariantCulture), null, result =>
            {
                if (!result.Ok || !result.HasData)
                {
                    Log(result.Display);
                    return;
                }
                if (!result.Data.TryGetProperty("draft", out var draftElement))
                {
                    Log("回执里没有 draft 字段（CLI 版本对不上？）。");
                    return;
                }
                var loaded = MonsterDraftModel.FromJson(draftElement.GetRawText());
                if (loaded == null)
                {
                    Log("draft 反序列化失败——两侧字段名可能已漂移，请核对 MonsterDraft.KnownTopLevelKeys。");
                    return;
                }
                _draft = loaded;
                if (_draft.PatrolRoute == null) _draft.PatrolRoute = new MonsterRouteModel { Points = new List<MonsterPointModel>() };
                ApplyDraftToUi();
                Log(string.Format("已载入 {0}（v{1}，{2} 技能 / {3} 掉落表 / {4} 点 / {5} 刷怪点）",
                    _draft.Name, _draft.ContentVersion, _draft.Skills.Count, _draft.LootBindings.Count,
                    _draft.PatrolRoute.Points.Count, _draft.SpawnPoints.Count));
            }));
        }

        private void LoadRedisDraft(int monsterId)
        {
            if (monsterId <= 0) return;
            GuardBusy(() => MonsterCli.RunAsync("draft get " + monsterId.ToString(CultureInfo.InvariantCulture), null, result =>
            {
                if (!result.Ok || !result.HasData || !result.Data.TryGetProperty("json", out var jsonElement))
                {
                    Log(result.Display);
                    return;
                }
                var loaded = MonsterDraftModel.FromJson(jsonElement.GetString());
                if (loaded == null)
                {
                    Log("草稿 JSON 解析失败。");
                    return;
                }
                _draft = loaded;
                if (_draft.PatrolRoute == null) _draft.PatrolRoute = new MonsterRouteModel { Points = new List<MonsterPointModel>() };
                ApplyDraftToUi();
                Log("已载入 Redis 草稿 " + monsterId + "。");
            }));
        }

        private void RunValidate()
        {
            CommitAll();
            var json = _draft.ToJson();
            var keyCheck = SelfCheckKeys(json);
            if (!string.IsNullOrEmpty(keyCheck))
            {
                Log("键名自证失败：" + keyCheck);
                return;
            }
            GuardBusy(() => MonsterCli.RunAsync("validate -", json, result =>
            {
                Log(result.Display);
                if (result.HasData && result.Data.TryGetProperty("errorPaths", out var paths) && paths.ValueKind == JsonValueKind.Array)
                {
                    foreach (var path in paths.EnumerateArray()) Log("  ✗ " + path.GetString());
                }
                if (result.HasData && result.Data.TryGetProperty("skippedChecks", out var skipped)
                    && skipped.ValueKind == JsonValueKind.Number && skipped.GetInt32() > 0)
                {
                    Log("  ⚠ 本次跳过了 " + skipped.GetInt32() + " 条 id 对账（库不可达）——“校验通过”不等于“引用都对”。");
                }
            }));
        }

        private void RunDraftSave()
        {
            CommitAll();
            GuardBusy(() => MonsterCli.RunAsync("draft save -", _draft.ToJson(), result =>
            {
                Log(result.Display);
                // 库里那格是"号"，源文件里那句是"文案"：一次"保存草稿"按下要两件事都落地，
                // 否则策划得到的是号已经改了而台词还是上一版——那是最难查的一种半改状态。
                if (result.Ok) WriteSayTextsIfChanged();
            }));
        }

        private void RunSave(bool thenPublish)
        {
            CommitAll();
            var json = _draft.ToJson();
            var keyCheck = SelfCheckKeys(json);
            if (!string.IsNullOrEmpty(keyCheck))
            {
                Log("键名自证失败：" + keyCheck);
                return;
            }
            GuardBusy(() => MonsterCli.RunAsync("save -", json, result =>
            {
                Log(result.Display);
                if (!result.Ok) return;
                if (result.HasData && result.Data.TryGetProperty("writtenRows", out var rows) && rows.ValueKind == JsonValueKind.Object)
                {
                    var text = new StringBuilder("落库行数：");
                    foreach (var prop in rows.EnumerateObject())
                        text.Append(prop.Name).Append('=').Append(prop.Value.ToString()).Append(' ');
                    Log(text.ToString());
                }
                if (result.HasData && result.Data.TryGetProperty("contentVersion", out var version) && version.ValueKind == JsonValueKind.Number)
                {
                    _draft.ContentVersion = version.GetInt32();
                    if (_versionLabel != null) _versionLabel.Text = "v" + _draft.ContentVersion;
                }
                // 文案写回排在发布之前：这一步会起一个子进程，而窗口一次只容一个请求，
                // 直接往下走 RunPublish 会被 GuardBusy 挡成"上一条还在跑"（静默没发布）。
                WriteSayTextsIfChanged(() => { if (thenPublish) RunPublish(); });
            }));
        }

        private void RunPublish()
        {
            CommitAll();
            if (_draft.MonsterId <= 0)
            {
                Log("先保存再发布（发布按编号操作，不接受未落库的草稿）。");
                return;
            }
            GuardBusy(() => MonsterCli.RunAsync("publish " + _draft.MonsterId.ToString(CultureInfo.InvariantCulture), null, result =>
            {
                Log(result.Display);
                if (result.Ok) Log("提示：Silo 侧要重启或热刷装配才会读到新内容。");
            }));
        }

        private void RunSetState(int state, string label)
        {
            CommitAll();
            if (_draft.MonsterId <= 0) return;
            // 下线的动词名沿用库侧语义（unpublish），label 只用于给用户一句话
            GuardBusy(() => MonsterCli.RunAsync("retire " + _draft.MonsterId.ToString(CultureInfo.InvariantCulture), null, result => Log(label + "：" + result.Display)));
        }

        private void RunRetire()
        {
            CommitAll();
            if (_draft.MonsterId <= 0) return;
            GuardBusy(() => MonsterCli.RunAsync("retire " + _draft.MonsterId.ToString(CultureInfo.InvariantCulture), null, result => Log(result.Display)));
        }

        private void RunDelete()
        {
            CommitAll();
            if (_draft.MonsterId <= 0) return;
            GuardBusy(() => MonsterCli.RunAsync("delete " + _draft.MonsterId.ToString(CultureInfo.InvariantCulture), null, result => Log(result.Display)));
        }

        private void RunPreviewXp(bool logOnly)
        {
            CommitAll();
            var playerLevel = ParseInt(_previewPlayerBox == null ? null : _previewPlayerBox.Text, _draft.LevelBase);
            var args = string.Format("preview-xp --mob {0} --player {1} --rank {2} --xpBase {3} --xpPer {4} --xpModBps {5} --healthModBps {6} --sweep",
                _draft.LevelBase, playerLevel, _draft.Rank, _draft.Xp.XpBase, _draft.Xp.XpPerLevel,
                _draft.Xp.XpModBps, _draft.Xp.HealthModBps);
            GuardBusy(() => MonsterCli.RunAsync(args, null, result =>
            {
                if (!result.Ok || !result.HasData)
                {
                    Log(result.Display);
                    return;
                }
                var serverFinal = ReadInt(result.Data, "final");
                var localFinal = MonsterPreviewMath.Xp(_draft.LevelBase, playerLevel, _draft.Rank,
                    _draft.Xp.XpBase, _draft.Xp.XpPerLevel, _draft.Xp.XpModBps, _draft.Xp.HealthModBps);
                var same = serverFinal == localFinal;

                if (_xpResultLabel != null)
                {
                    _xpResultLabel.Text = string.Format("Lv{0} 打 Lv{1}：服务端 {2}｜本地 {3}｜{4}",
                        playerLevel, _draft.LevelBase, serverFinal, localFinal, same ? "一致" : "不一致（两份实现漂移了）");
                }
                Log(string.Format("经验对拍：服务端 {0} vs 本地 {1} → {2}", serverFinal, localFinal, same ? "一致" : "★不一致，必须查 MonsterPreviewMath 与内核的分段表"));

                // 曲线逐点对拍：只比"当前值"会漏掉分段边界漂移，那才是最容易漂的地方。
                if (result.Data.TryGetProperty("curve", out var curve) && curve.ValueKind == JsonValueKind.Array)
                {
                    var mismatches = 0;
                    foreach (var point in curve.EnumerateArray())
                    {
                        var diff = ReadInt(point, "diff");
                        var mobLevel = playerLevel + diff;
                        if (mobLevel < 1) continue;
                        var serverPoint = ReadInt(point, "final");
                        var localPoint = MonsterPreviewMath.Xp(mobLevel, playerLevel, _draft.Rank,
                            _draft.Xp.XpBase, _draft.Xp.XpPerLevel, _draft.Xp.XpModBps, _draft.Xp.HealthModBps);
                        if (serverPoint != localPoint)
                        {
                            mismatches++;
                            if (mismatches <= 4)
                                Log(string.Format("  曲线漂移 @差{0}（怪Lv{1}）：服务端 {2} vs 本地 {3}", diff, mobLevel, serverPoint, localPoint));
                        }
                    }
                    Log(mismatches == 0
                        ? "曲线逐点比对：全部一致（" + curve.GetArrayLength() + " 点）"
                        : "★曲线有 " + mismatches + " 个点不一致（共 " + curve.GetArrayLength() + " 点）——不要信这条预览曲线");
                }
                if (logOnly) return;
            }));
        }

        private void RunPreviewDrop()
        {
            CommitAll();
            if (_draft.MonsterId <= 0) return;
            GuardBusy(() => MonsterCli.RunAsync("preview-drop --mob " + _draft.MonsterId.ToString(CultureInfo.InvariantCulture), null, result =>
            {
                Log(result.Display);
                if (!result.HasData) return;
                if (result.Data.TryGetProperty("expectedItemsPerKill", out var expected)) Log("  每次击杀期望件数：" + expected.ToString());
                if (result.Data.TryGetProperty("lootTableIdFromDomain", out var derived)) Log("  域推导兜底表号：" + derived.ToString());
                if (result.Data.TryGetProperty("note", out var note)) Log("  " + note.GetString());
            }));
        }

        /// <summary>
        /// 导出客户端外观表（<c>model_key → prefab 路径 / 缩放 / 姿态</c>）。
        /// <para>
        /// 输出路径与客户端读取路径是**同一个字符串的两份副本**：读侧在
        /// <c>Source/Game/Selection/MonsterVisualCatalog.cs</c> 的 <c>RelativeTablePath</c>
        /// （脚本域与插件域互相引用不到，没法共用常量）。改一处必须改另一处，
        /// 否则表现是"导出成功、编辑器里没问题，进游戏所有怪还是占位人形"。
        /// </para>
        /// </summary>
        private void RunExportVisuals()
        {
            var root = FlaxEngine.Globals.ProjectFolder;
            if (string.IsNullOrEmpty(root))
            {
                Log("拿不到工程目录（Globals.ProjectFolder 为空），外观表不知道往哪写。");
                return;
            }

            var outPath = System.IO.Path.Combine(root, "Data", "Monsters", "client_visuals.json");
            GuardBusy(() => MonsterCli.RunAsync("export-visuals --out \"" + outPath + "\"", null, result =>
            {
                Log(result.Display);
                LogReceiptDatabase(result);
                if (!result.HasData) return;
                if (result.Data.TryGetProperty("count", out var count)) Log("  进表条目：" + count.ToString());
                if (result.Data.TryGetProperty("problems", out var problems) && problems.ValueKind == JsonValueKind.Array)
                {
                    // 漏配必须逐条念出来：只报"有 N 条问题"等于让策划以为不影响上线。
                    foreach (var problem in problems.EnumerateArray())
                        Log("  外观问题：" + problem.GetString());
                }
            }));
        }

        /// <summary>
        /// 从旁挂源文件读回"这一号的文案"并填进格子。
        /// <para>为什么必须读回来显示，而不是留空让策划自己记：号在库、文案在文件，两边分家存。
        /// 面板只给一格数字的话，"这只怪现在念的是哪句"就只存在于人脑子里；他改一次号，
        /// 旧那句在文件里悄悄变成没人念的孤儿，而下一个人看到的是空格外壳。</para>
        /// </summary>
        private void LoadSayTextFromSource(int monsterId, int sayId)
        {
            if (_sayText == null) return;
            _sayText.Text = string.Empty;
            _sayTextLoaded = string.Empty;
            _sayTextLoadedSayId = 0;
            _sayGroupLoaded = 0;
            _sayRows.Clear();
            if (_sayGroupBox != null) _sayGroupBox.Text = string.Empty;
            if (_sayGroupInfo != null) _sayGroupInfo.Text = "台词组：-（还没读到源文件）";
            _deathSayId = 0;
            _deathTextLoaded = string.Empty;
            if (_deathText != null) _deathText.Text = string.Empty;
            if (_deathTextHint != null)
                _deathTextHint.Text = "临终台词：- （组 " + MonsterTextGroupMath.DeathGroup + "：怪死时随机念一句；空 = 不念）";
            _assistSayId = 0;
            _assistTextLoaded = string.Empty;
            if (_assistText != null) _assistText.Text = string.Empty;
            if (_assistTextHint != null)
                _assistTextHint.Text = "应援台词：- （组 " + MonsterTextGroupMath.AssistGroup + "：被同族叫起来时念一句；空 = 不念）";
            if (monsterId <= 0)
            {
                SetSayTextHint("文案：这只怪还没落库（文本挂在编号名下，先保存再来填）");
                return;
            }
            if (sayId <= 0)
            {
                SetSayTextHint("文案：这一只没配喊话号（上面那格填 0 以上才会去源文件里找）");
                return;
            }

            GuardBusy(() => MonsterCli.RunAsync(
                "get-texts --monster=" + monsterId.ToString(CultureInfo.InvariantCulture), null, result =>
                {
                    if (!result.Ok)
                    {
                        SetSayTextHint("文案：源文件读不到——" + result.Message);
                        return;
                    }
                    var found = string.Empty;
                    var foundGroup = 0;
                    var matched = false;
                    _sayRows.Clear();
                    if (result.HasData && result.Data.TryGetProperty("rows", out var rows)
                        && rows.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var row in rows.EnumerateArray())
                        {
                            var rowSayId = ReadInt(row, "sayId");
                            var rowGroup = ReadInt(row, "group");
                            var rowSeq = ReadInt(row, "seq");
                            _sayRows.Add(new MonsterTextGroupMath.Row(rowSayId, rowGroup, rowSeq, ReadString(row, "text")));
                            if (rowSayId != sayId) continue;
                            matched = true;
                            found = ReadString(row, "text");
                            foundGroup = rowGroup;
                        }
                    }
                    _sayText.Text = found;
                    _sayTextLoaded = found;
                    _sayTextLoadedSayId = sayId;
                    _sayGroupLoaded = foundGroup;
                    if (_sayGroupBox != null)
                        _sayGroupBox.Text = foundGroup > 0 ? foundGroup.ToString(CultureInfo.InvariantCulture) : string.Empty;
                    if (_sayGroupInfo != null)
                        _sayGroupInfo.Text = MonsterTextGroupMath.DescribeGroup(_sayRows, foundGroup);

                    // 临终台词：按"落在组 2 里的那一号"回填（编辑已有行，不是每存一次新起一号）
                    _deathSayId = MonsterTextGroupMath.SayIdOfGroup(_sayRows, MonsterTextGroupMath.DeathGroup);
                    var deathLine = string.Empty;
                    if (_deathSayId > 0)
                    {
                        for (var i = 0; i < _sayRows.Count; i++)
                        {
                            if (_sayRows[i].SayId != _deathSayId || _sayRows[i].Group != MonsterTextGroupMath.DeathGroup) continue;
                            deathLine = _sayRows[i].Text;
                            break;
                        }
                    }
                    _deathTextLoaded = deathLine;
                    if (_deathText != null) _deathText.Text = deathLine;
                    if (_deathTextHint != null)
                        _deathTextHint.Text = _deathSayId > 0
                            ? "临终台词：第 " + _deathSayId + " 号（组 " + MonsterTextGroupMath.DeathGroup + "，改动随保存写回）"
                            : "临终台词：源里还没有（写一句，保存时按组 " + MonsterTextGroupMath.DeathGroup + " 新增；空 = 不念）";

                    _assistSayId = MonsterTextGroupMath.SayIdOfGroup(_sayRows, MonsterTextGroupMath.AssistGroup);
                    var assistLine = string.Empty;
                    if (_assistSayId > 0)
                    {
                        for (var i = 0; i < _sayRows.Count; i++)
                        {
                            if (_sayRows[i].SayId != _assistSayId || _sayRows[i].Group != MonsterTextGroupMath.AssistGroup) continue;
                            assistLine = _sayRows[i].Text;
                            break;
                        }
                    }
                    _assistTextLoaded = assistLine;
                    if (_assistText != null) _assistText.Text = assistLine;
                    if (_assistTextHint != null)
                        _assistTextHint.Text = _assistSayId > 0
                            ? "应援台词：第 " + _assistSayId + " 号（组 " + MonsterTextGroupMath.AssistGroup + "，改动随保存写回）"
                            : "应援台词：源里还没有（写一句，保存时按组 " + MonsterTextGroupMath.AssistGroup + " 新增；空 = 不念）";
                    SetSayTextHint(matched
                        ? "文案：已读到第 " + sayId + " 号" + (foundGroup > 0 ? "（组 " + foundGroup + "，同组多句每轮挑一句）" : string.Empty)
                          + "（改动随保存写回文件）"
                        : "文案：源文件里还没有第 " + sayId + " 号（在这里写，保存时新增一行）");
                }));
        }

        /// <summary>
        /// "同组再加一句"：把号换成这只怪**下一个没用过**的号、组号保持不变、文案格清空。
        /// <para>没有这个动作时，多句一组只能靠人记住"77/78 用过了"再手抄 79——
        /// 而抄重号会被 <c>set-text</c> 覆盖掉已有那一句（策划以为加了一句，其实改了一句）。</para>
        /// </summary>
        private void AddSayLineToGroup()
        {
            CommitAll();
            var monsterId = _draft == null ? 0 : _draft.MonsterId;
            if (monsterId <= 0)
            {
                Log("台词组：这只怪还没落库，文本挂在编号名下（先保存再来加句）。");
                return;
            }
            var group = _sayGroupBox == null ? 0 : ParseInt(_sayGroupBox, 0);
            if (group <= 0)
            {
                Log("台词组：组号是 0（不成组）。先填一个 >0 的组号，再加下一句。");
                return;
            }
            var sayId = _sayBox == null ? 0 : ParseInt(_sayBox, 0);
            if (sayId <= 0)
            {
                Log("台词组：先把「索敌喊话id」填成一个 >0 的号（它就是这一组里的一句）。");
                return;
            }

            var next = MonsterTextGroupMath.NextSayId(_sayRows);
            if (_sayRows.Count == 0)
            {
                // 没读到行时下一个号必然是 1：这只怪若其实已有句子就会撞号（撞号 = 覆盖已有那句）。
                // 不断言成错误（可能是第一句），但要把"这个号是从零行算出来的"说出来。
                Log("台词组：源文件里还没读到这只怪的行，新号从 1 起——这只怪若已有句子，先按「读草稿」刷新再按本键。");
            }
            if (_sayBox != null) _sayBox.Text = next.ToString(CultureInfo.InvariantCulture);
            if (_sayGroupBox != null) _sayGroupBox.Text = group.ToString(CultureInfo.InvariantCulture);
            // 新号在源文件里还不存在：基线一起挪过去，否则写完时会被"号改了而格子里是旧那句"的守卫挡下。
            _sayText.Text = string.Empty;
            _sayTextLoaded = string.Empty;
            _sayTextLoadedSayId = next;
            _sayGroupLoaded = group;
            if (_sayGroupInfo != null) _sayGroupInfo.Text = MonsterTextGroupMath.DescribeGroup(_sayRows, group);
            SetSayTextHint("台词组 " + group + "：现在编第 " + next + " 号（写好后随保存入组）");
            Log("台词组：已切到第 " + next + " 号（组 " + group + "）。写完这一句再按一次「同组再加一句」。");
        }

        /// <summary>
        /// 把格子里的文案写回旁挂源文件（没改就不写，写了要把 CLI 回执念出来）。
        /// <para><c>--text=-</c> 走标准输入而不是 argv：一句台词里带引号或逗号是常态，
        /// 而 Windows 上手工拼 argv 转义错一个字符的表现是"写进去半句"或"起不了进程"。</para>
        /// <para>清空那格 = 撤销这一号，用显式的 <c>--text=</c>：空 stdin 与漏传参数在管道上同形，
        /// 这里要把"我确实是想删"说出来。</para>
        /// </summary>
        private void WriteSayTextIfChanged(Action onCompleted = null)
        {
            if (_sayText == null)
            {
                if (onCompleted != null) onCompleted();
                return;
            }

            var monsterId = _draft == null ? 0 : _draft.MonsterId;
            var sayId = _sayBox == null ? 0 : ParseInt(_sayBox, _draft == null ? 0 : _draft.AggroSayId);
            var wanted = (_sayText.Text ?? string.Empty).Trim();
            // 组那一格与文案各记各的基线：只改组不动文案也是真改动（"把三句捆成一组"就是这个动作）。
            var wantedGroup = _sayGroupBox == null
                ? _sayGroupLoaded
                : ParseInt(_sayGroupBox, _sayGroupLoaded);
            if (wantedGroup < 0) wantedGroup = 0;
            var groupChanged = wantedGroup != _sayGroupLoaded;

            if (monsterId <= 0)
            {
                Log("  文案没写：这只怪还没落库，文本挂在编号名下（先保存）。");
                if (onCompleted != null) onCompleted();
                return;
            }
            if (sayId <= 0)
            {
                if (wanted.Length > 0 || _sayTextLoaded.Length > 0)
                    Log("  文案没写：喊话号是 0，这句没有可挂的号。要么把号填上，要么清空这一格。");
                if (onCompleted != null) onCompleted();
                return;
            }
            if (sayId != _sayTextLoadedSayId)
            {
                // 号被改过而格子里还是**旧号**那句：不自动搬到新号名下（那是替人复制台词，
                // 还会把旧号留成孤儿），也不静默跳过。让装配期那条"行为表指向文本 X 而包里没它"
                // 去点名——它本来就是为了这件事写的。
                Log("  文案没写：号从 " + _sayTextLoadedSayId + " 改成了 " + sayId +
                    "，而格子里是旧号那句。在新号下重新写一遍，导出内容时才会落进包里；" +
                    "否则装配回执会念「模板指向文本 " + sayId + " 而这份内容包里没有它」。");
                LoadSayTextFromSource(monsterId, sayId);
                if (onCompleted != null) onCompleted();
                return;
            }
            if (string.Equals(wanted, _sayTextLoaded, StringComparison.Ordinal) && !groupChanged)
            {
                // 一字未改、组也没动：不发子进程、不重写文件（重写会把读侧跳过的坏行一起带走，见 UpsertText）。
                if (onCompleted != null) onCompleted();
                return;
            }

            var idText = monsterId.ToString(CultureInfo.InvariantCulture);
            var sayText = sayId.ToString(CultureInfo.InvariantCulture);
            var groupArg = " --group=" + wantedGroup.ToString(CultureInfo.InvariantCulture);
            var arguments = wanted.Length == 0
                ? "set-text --monster=" + idText + " --say=" + sayText + " --text=" + groupArg
                : "set-text --monster=" + idText + " --say=" + sayText + " --text=-" + groupArg;
            GuardBusy(() => MonsterCli.RunAsync(arguments, wanted.Length == 0 ? null : wanted, result =>
            {
                Log("  " + result.Display);
                if (result.Ok)
                {
                    _sayTextLoaded = wanted;
                    _sayTextLoadedSayId = sayId;
                    _sayGroupLoaded = wantedGroup;
                    SetSayTextHint(wanted.Length == 0
                        ? "文案：这一号已从源文件撤销（按「导出文本表」才会把客户端那份一起改掉）"
                        : "文案：已写回源文件（按「导出文本表」才会进客户端表；进 DS 包要重导内容）"
                          + (wantedGroup > 0 ? "；组 " + wantedGroup + "：同组多句每轮挑一句" : string.Empty));
                }
                else
                {
                    // 没写成就要让人看见：库里那号已经改了，源文件却没这句——
                    // 半改状态由装配期兜底，但兜底的话该在这里先说。
                    SetSayTextHint("文案：这次没写进去，格子里留着你刚写的（重存或改号后再试一次）");
                }
                if (onCompleted != null) onCompleted();
            }));
        }

        private void SetSayTextHint(string text)
        {
            if (_sayTextHint != null) _sayTextHint.Text = text;
        }

        private void SetDeathTextHint(string text)
        {
            if (_deathTextHint != null) _deathTextHint.Text = text;
        }

        private void SetAssistTextHint(string text)
        {
            if (_assistTextHint != null) _assistTextHint.Text = text;
        }

        /// <summary>
        /// 把回执里的库名念出来。三个导出按钮与整库普查**读的是 CLI 连的那个库**（环境变量/缺省决定），
        /// 不念库名的话"以为在导排练库、其实导的是生产库"两份回执长得一模一样。
        /// </summary>
        private void LogReceiptDatabase(MonsterCliResult result)
        {
            if (result.HasData && result.Data.TryGetProperty("database", out var db) && db.ValueKind == JsonValueKind.String)
                Log("  库：" + db.GetString());
        }

        /// <summary>两次保存都走这里：先写进战那一句，再写临终台词（两格各自有改动才发子进程）。</summary>
        private void WriteSayTextsIfChanged(Action onCompleted = null)
        {
            WriteSayTextIfChanged(() => WriteDeathTextIfChanged(() => WriteAssistTextIfChanged(onCompleted)));
        }

        /// <summary>
        /// 把临终台词写回旁挂源文件（组 <see cref="MonsterTextGroupMath.DeathGroup"/>）。
        /// <para>它**不改草稿**：死亡台词没有行为表指针，服务端靠"落在这一组"挑（`MonsterContent.ResolveDeathSayId`）。
        /// 清空 = 撤销这一号（显式 <c>--text=</c>），所以"不念"也能表达。</para>
        /// <para>号沿用源里已有的那一号；源里还没有时取这只怪下一个没用过的号（`NextSayId`，不是行数 +1）——
        /// 撞号会把别的一句覆盖掉，而面板上看起来只是"我又写了一句临终台词"。</para>
        /// </summary>
        private void WriteDeathTextIfChanged(Action onCompleted = null)
        {
            if (_deathText == null)
            {
                if (onCompleted != null) onCompleted();
                return;
            }
            var monsterId = _draft == null ? 0 : _draft.MonsterId;
            var wanted = (_deathText.Text ?? string.Empty).Trim();
            if (monsterId <= 0)
            {
                if (wanted.Length > 0) Log("  临终台词没写：这只怪还没落库，文本挂在编号名下（先保存）。");
                if (onCompleted != null) onCompleted();
                return;
            }
            if (string.Equals(wanted, _deathTextLoaded, StringComparison.Ordinal))
            {
                if (onCompleted != null) onCompleted();
                return;
            }

            var sayId = _deathSayId > 0 ? _deathSayId : Math.Max(2, MonsterTextGroupMath.NextSayId(_sayRows));
            var sayText = sayId.ToString(CultureInfo.InvariantCulture);
            var groupArg = " --group=" + MonsterTextGroupMath.DeathGroup.ToString(CultureInfo.InvariantCulture);
            var arguments = wanted.Length == 0
                ? "set-text --monster=" + monsterId.ToString(CultureInfo.InvariantCulture) + " --say=" + sayText + " --text=" + groupArg
                : "set-text --monster=" + monsterId.ToString(CultureInfo.InvariantCulture) + " --say=" + sayText + " --text=-" + groupArg;
            GuardBusy(() => MonsterCli.RunAsync(arguments, wanted.Length == 0 ? null : wanted, result =>
            {
                Log("  " + result.Display);
                if (result.Ok)
                {
                    _deathTextLoaded = wanted;
                    if (wanted.Length == 0)
                    {
                        _deathSayId = 0;
                        SetDeathTextHint("临终台词：已撤销（怪死时不再出声；按「导出文本表」才会进客户端表）");
                    }
                    else
                    {
                        _deathSayId = sayId;
                        SetDeathTextHint("临终台词：已写回源文件（第 " + sayId + " 号，组 " + MonsterTextGroupMath.DeathGroup +
                                         "；按「导出文本表」才会进客户端表）");
                    }
                }
                else
                {
                    SetDeathTextHint("临终台词：这次没写进去，格子里留着你刚写的（重存或改号后再试一次）");
                }
                if (onCompleted != null) onCompleted();
            }));
        }

        /// <summary>
        /// 把应援台词写回旁挂源文件（组 <see cref="MonsterTextGroupMath.AssistGroup"/>）。
        /// <para>与临终那格同形：号沿用源里已有的那一号，没有时取这只怪下一个没用过的号；
        /// 清空 = 撤销这一号（显式 <c>--text=</c>）。它同样**不改草稿**（没有行为表指针）。</para>
        /// </summary>
        private void WriteAssistTextIfChanged(Action onCompleted = null)
        {
            if (_assistText == null)
            {
                if (onCompleted != null) onCompleted();
                return;
            }
            var monsterId = _draft == null ? 0 : _draft.MonsterId;
            var wanted = (_assistText.Text ?? string.Empty).Trim();
            if (monsterId <= 0)
            {
                if (wanted.Length > 0) Log("  应援台词没写：这只怪还没落库，文本挂在编号名下（先保存）。");
                if (onCompleted != null) onCompleted();
                return;
            }
            if (string.Equals(wanted, _assistTextLoaded, StringComparison.Ordinal))
            {
                if (onCompleted != null) onCompleted();
                return;
            }

            var sayId = _assistSayId > 0 ? _assistSayId : Math.Max(4, MonsterTextGroupMath.NextSayId(_sayRows));
            var sayText = sayId.ToString(CultureInfo.InvariantCulture);
            var groupArg = " --group=" + MonsterTextGroupMath.AssistGroup.ToString(CultureInfo.InvariantCulture);
            var arguments = wanted.Length == 0
                ? "set-text --monster=" + monsterId.ToString(CultureInfo.InvariantCulture) + " --say=" + sayText + " --text=" + groupArg
                : "set-text --monster=" + monsterId.ToString(CultureInfo.InvariantCulture) + " --say=" + sayText + " --text=-" + groupArg;
            GuardBusy(() => MonsterCli.RunAsync(arguments, wanted.Length == 0 ? null : wanted, result =>
            {
                Log("  " + result.Display);
                if (result.Ok)
                {
                    _assistTextLoaded = wanted;
                    if (wanted.Length == 0)
                    {
                        _assistSayId = 0;
                        SetAssistTextHint("应援台词：已撤销（被同族叫起来时不再出声；按「导出文本表」才会进客户端表）");
                    }
                    else
                    {
                        _assistSayId = sayId;
                        SetAssistTextHint("应援台词：已写回源文件（第 " + sayId + " 号，组 " + MonsterTextGroupMath.AssistGroup +
                                          "；按「导出文本表」才会进客户端表）");
                    }
                }
                else
                {
                    SetAssistTextHint("应援台词：这次没写进去，格子里留着你刚写的（重存或改号后再试一次）");
                }
                if (onCompleted != null) onCompleted();
            }));
        }

        /// <summary>
        /// 导出**客户端文本表**（<c>(modelKey, sayId) → 文案/动作</c>，<c>Data/Monsters/client_texts.json</c>）。
        /// <para>与 <see cref="RunExportVisuals"/> 同一族、同一风险：线上只发文本号，
        /// "这一号对应哪句"随客户端表发布。源文件写了而这张表没重导，表现是
        /// <b>怪照着号去查、查不到 ⇒ 一句都不念</b>，与"没配文本"在玩家那边同形。</para>
        /// <para>读侧路径在 <c>Source/Game/Selection/MonsterTextCatalog.cs</c>（脚本域，引用不到这里），
        /// 所以那一份字符串常量与本处是**同一件事的两份写法**：改一处必须改另一处。</para>
        /// </summary>
        private void RunExportTexts()
        {
            var root = FlaxEngine.Globals.ProjectFolder;
            if (string.IsNullOrEmpty(root))
            {
                Log("拿不到工程目录（Globals.ProjectFolder 为空），文本表不知道往哪写。");
                return;
            }

            var outPath = System.IO.Path.Combine(root, "Data", "Monsters", "client_texts.json");
            GuardBusy(() => MonsterCli.RunAsync("export-texts --out \"" + outPath + "\"", null, result =>
            {
                Log(result.Display);
                LogReceiptDatabase(result);
                if (result.HasData && result.Data.TryGetProperty("count", out var count))
                    Log("  进表条目：" + count.ToString());
                if (result.HasData && result.Data.TryGetProperty("problems", out var problems) && problems.ValueKind == JsonValueKind.Array)
                {
                    // 漏配必须逐条念出来：只报"有 N 条问题"等于让策划以为不影响上线。
                    foreach (var problem in problems.EnumerateArray())
                        Log("  文本问题：" + problem.GetString());
                }
                // 源文件行数与进表条目数分开念：两者不等就是"有一句文案配了而客户端拿不到"
                // （通常是那只怪没有 model_key 或没发布），这句话比"成功"有用得多。
                if (result.HasData && result.Data.TryGetProperty("sourceRows", out var sourceRows)
                    && result.Data.TryGetProperty("count", out var enteredRows)
                    && sourceRows.ToString() != enteredRows.ToString())
                    Log("  ⚠ 源文件 " + sourceRows.ToString() + " 行而表里只有 " + enteredRows.ToString() +
                        " 条：差的那些行上面逐条点了名。");
            }));
        }

        /// <summary>
        /// 内容普查（<c>audit</c>）：对**整库**问一遍"哪一格还是零 / 哪条配置是死的"。
        /// <para>与"校验"不同：校验只管当前这只草稿；普查管的是**已发布的那一批**——
        /// 机制就绪而内容为零、世界 BOSS 配了 leash、发布着的空巡逻线，这些都只在整库视角看得见。</para>
        /// <para>正文在 CLI 顶层 <c>warnings</c> 里（面板桥已收），所以这里逐条念；只报"发现 9 条"
        /// 等于把"为什么"藏起来。`npcTableReadable=false` 时孤儿行那一格是**降级读数**，必须明说，
        /// 否则人会把一个查不全的数字当成结论。</para>
        /// </summary>
        private void RunContentAudit()
        {
            GuardBusy(() => MonsterCli.RunAsync("audit", null, result =>
            {
                Log(result.Display);
                LogReceiptDatabase(result);
                if (result.HasData)
                {
                    var data = result.Data;
                    string N(string key) => data.TryGetProperty(key, out var v) ? v.ToString() : "?";
                    Log("  已发布=" + N("published") + " 校验不过=" + N("invalid") + " 技能位=" + N("skillCapable") +
                        " 走图=" + N("navCapable") + " 求援=" + N("assistCapable") + " 挂掉落=" + N("monstersWithLoot") +
                        " 配金币=" + N("moneyCapable") + " 挂路线=" + N("routeCapable") + " 配尸体=" + N("corpseCapable") +
                        " 配文本=" + N("textCapable") + " 孤儿行=" + N("orphanRows"));
                    if (data.TryGetProperty("npcTableReadable", out var readable)
                        && readable.ValueKind == JsonValueKind.False)
                        Log("  ⚠ NPC 模板表读不到：孤儿行这一格今天**降级**，读数当不得真（先把 021/022 建起来）。");
                }
                foreach (var warning in result.Warnings) Log("  普查：" + warning);
            }));
        }

        /// <summary>
        /// 导出**运行时内容包**（<c>DsHost --monster-content</c> 真正吃的那份）。
        /// <para>为什么必须在面板里：保存/发布只到库，而游戏读的是这一份文件。
        /// 少这一格，"库里有、世界没有"就只能发生在别人敲 CLI 的那一步，
        /// 而面板上每个按钮都刚回"成功"——最难查的一类。</para>
        /// <para>只导已发布的（<c>publish_state=1</c>），所以"发布了几只 / 包里几只"要一起念：
        /// 两个数不等就是"有怪发布了却进不了包"。</para>
        /// </summary>
        private void RunExportContent()
        {
            var root = FlaxEngine.Globals.ProjectFolder;
            if (string.IsNullOrEmpty(root))
            {
                Log("拿不到工程目录（Globals.ProjectFolder 为空），内容包不知道往哪写。");
                return;
            }

            var outPath = System.IO.Path.Combine(root, "Data", "Monsters", "monster_content.json");
            GuardBusy(() => MonsterCli.RunAsync("export-content --out \"" + outPath + "\"", null, result =>
            {
                Log(result.Display);
                if (!result.Ok) return;
                if (!result.HasData) return;
                LogReceiptDatabase(result);

                // 逐格念（只报"成功"等于没报）：模板/刷怪点/路线/掉落/挂载/文本各一格，
                // 任何一格为 0 都说明那一层的内容没进包。
                var data = result.Data;
                string N(string key) => data.TryGetProperty(key, out var v) ? v.ToString() : "?";
                Log("  内容包：" + outPath);
                Log("  模板=" + N("templates") + " 刷怪点=" + N("spawnPoints") + " 路线=" + N("routes") +
                    " 掉落绑定=" + N("lootBindings") + " 技能挂载=" + N("skillMounts") + " 文本=" + N("texts"));
                if (data.TryGetProperty("problems", out var problems) && problems.ValueKind == JsonValueKind.Array)
                {
                    foreach (var problem in problems.EnumerateArray())
                        Log("  内容问题：" + problem.GetString());
                }
            }));
        }

        /// <summary>
        /// 用引擎自己的可行走判定烘一张服务端导航网格，把掩码交给 CLI 装配成 <c>MONSTERNAV</c> 文件。
        /// <para>
        /// <b>为什么采样必须在编辑器里做</b>：只有引擎进程答得出"这一格能不能站"——
        /// NavMesh 由导航模块持有，物理射线要活的碰撞场景。CLI 只接掩码，
        /// 于是**文件格式的判据只有一份**（<c>MonsterNavBakeCore</c>，在 Sim 里可离线断言），
        /// 而"问引擎"这一段留在唯一有能力问的地方。
        /// </para>
        /// <para>
        /// <b>换轴口径</b>（全工程一致）：服务端 Z-up、水平面 = XY；Flax Y-up、地面平面 = XZ。
        /// 所以服务端 (x, y) → Flax (x, 探针高度, y)。写反的后果是整张网格转 90°，
        /// 而"怪贴着墙绕圈"这种表现没人会怀疑到换轴上（本工程的远程角色位置就错过一次）。
        /// </para>
        /// <para>
        /// <b>这一圈是同步跑的</b>：N×N 次引擎查询会占住主线程若干秒，所以规模有上限，
        /// 并在开始前把规模与判据念出来——烘焙是一次性动作，卡顿比静默诚实。
        /// </para>
        /// </summary>
        private void BakeNavGridFromScene()
        {
            var cell = ParseDouble(_navCellBox, 4.0);
            var width = ParseInt(_navWidthBox, 64);
            var height = ParseInt(_navHeightBox, 64);
            var originX = ParseDouble(_navOriginXBox, 0.0);
            var originY = ParseDouble(_navOriginYBox, 0.0);
            var slopeLimitDegrees = ParseDouble(_navSlopeBox, 45.0);
            var useNavMesh = _navModeDrop == null || _navModeDrop.SelectedIndex != 1;

            if (cell <= 0.0 || width <= 0 || height <= 0)
            {
                Log("格长/格数非法：格长必须 > 0 米，宽和高都必须是正整数。");
                return;
            }
            if (width > MaxNavSide || height > MaxNavSide || (long)width * height > MaxNavCells)
            {
                Log(string.Format("规模超限：{0}×{1}（单边 ≤ {2}、总格数 ≤ {3}）。分块烘，或把格长改大。",
                    width, height, MaxNavSide, MaxNavCells));
                return;
            }
            var root = FlaxEngine.Globals.ProjectFolder;
            if (string.IsNullOrEmpty(root))
            {
                Log("拿不到工程目录（Globals.ProjectFolder 为空），导航网格不知道往哪写。");
                return;
            }

            Log(string.Format("开始烘焙 {0}×{1}（cell={2} 米，判据={3}）：主线程会短暂无响应",
                width, height, cell.ToString(CultureInfo.InvariantCulture), useNavMesh ? "NavMesh 投影" : "物理射线+坡度"));

            // 坡度阈值：法线与"上"的夹角超过上限就当墙/悬崖。用点积而不是反三角——
            // 只比较 cos 值，避开 atan 的分支与精度问题。
            var walkCos = System.Math.Cos(slopeLimitDegrees * System.Math.PI / 180.0);
            // 难行带：坡度在 [上限, 上限×1.6] 之间标成 '2'（A* 会优先绕开，但真没路时还能走）。
            var hardCos = System.Math.Cos(slopeLimitDegrees * 1.6 * System.Math.PI / 180.0);

            var mask = new System.Text.StringBuilder(width * height + height + 8);
            var opened = 0;
            var navHits = 0;
            var rayHits = 0;
            for (var gy = 0; gy < height; gy++)
            {
                for (var gx = 0; gx < width; gx++)
                {
                    var serverX = originX + gx * cell;
                    var serverY = originY + gy * cell;
                    var mark = ProbeNavCell(serverX, serverY, (float)cell, useNavMesh, walkCos, hardCos,
                                            ref navHits, ref rayHits);
                    if (mark != '0') opened++;
                    mask.Append(mark);
                }
                mask.Append('\n');
            }

            if (_navInfo != null)
                _navInfo.Text = string.Format("采样：{0}/{1} 可走（NavMesh 命中 {2}，射线命中 {3}）",
                    opened, width * height, navHits, rayHits);

            // 两条通路都没命中时，写出去也只会得到一张"全阻塞"的图；先把这条判据摆在日志里，
            // 用户看到的就不是"CLI 拒写了"这种无头案，而是"这个场景里没有可问的东西"。
            if (opened == 0)
            {
                Log(useNavMesh
                    ? "全部不可走：NavMesh 一个都没命中。场景里可能还没生成导航网格（用 FlaxEngine.Navigation.BuildNavMesh 生成，或改用“物理射线+坡度”判据）。"
                    : "全部不可走：射线一个都没命中。编辑器域里若没有活的碰撞场景，这条通路问不到东西（改在运行态烘，或先确认地形有碰撞）。");
            }
            Log(string.Format("烘焙完成，交 CLI 装配：{0}×{1}，可走 {2} 格", width, height, opened));

            var outPath = System.IO.Path.Combine(root, "Data", "Monsters", "scene_nav.grid");
            var args = string.Format(
                "bake-nav --out \"{0}\" --width {1} --height {2} --cell {3} --origin {4} {5} --mask -",
                outPath, width, height,
                cell.ToString(CultureInfo.InvariantCulture),
                originX.ToString(CultureInfo.InvariantCulture),
                originY.ToString(CultureInfo.InvariantCulture));

            GuardBusy(() => MonsterCli.RunAsync(args, mask.ToString(), result =>
            {
                Log(result.Display);
                if (!result.HasData) return;
                if (result.Data.TryGetProperty("walkable", out var walkableEl))
                    Log("  CLI 记的可走格数：" + walkableEl.ToString());
                if (result.Data.TryGetProperty("problems", out var problems) && problems.ValueKind == JsonValueKind.Array)
                {
                    foreach (var problem in problems.EnumerateArray())
                        Log("  装配问题：" + problem.GetString());
                }
            }));
        }

        /// <summary>
        /// 问引擎"服务端坐标 (serverX, serverY) 这一格能不能站"，返回掩码字符（'1'/'2'/'0'）。
        /// <para>NavMesh 判据要求投影点**落在这一格附近**：引擎的 ProjectPoint 会把点吸到最近的导航面上，
        /// 不设这层距离上限的话，整张图都会被吸到远处那一小块导航面上，于是"全图可走"。</para>
        /// </summary>
        private static char ProbeNavCell(
            double serverX, double serverY, float cellSize, bool useNavMesh,
            double walkCos, double hardCos, ref int navHits, ref int rayHits)
        {
            var fx = (float)serverX;
            var fz = (float)serverY;   // 服务端 Y → Flax 的 Z（水平面另一轴）

            if (useNavMesh)
            {
                FlaxEngine.Vector3 snapped;
                if (!FlaxEngine.Navigation.ProjectPoint(new FlaxEngine.Vector3(fx, 0f, fz), out snapped)) return '0';
                var dx = snapped.X - fx;
                var dz = snapped.Z - fz;
                var limit = cellSize * 0.75f;
                if (dx * dx + dz * dz > limit * limit) return '0';
                navHits++;
                return '1';
            }

            FlaxEngine.RayCastHit hit;
            // 从足够高往下打一根射线：探针跨度取足够大的对称区间，地形当前海拔是多少都不影响判定。
            if (!FlaxEngine.Physics.RayCast(new FlaxEngine.Vector3(fx, NavProbeUp, fz),
                                            new FlaxEngine.Vector3(0f, -1f, 0f), out hit,
                                            NavProbeUp * 2f))
            {
                return '0';
            }
            rayHits++;
            if (hit.Normal.Y >= walkCos) return '1';
            if (hit.Normal.Y >= hardCos) return '2';
            return '0';
        }

        private void CapturePointFromSelection()
        {
            var editor = FlaxEditor.Editor.Instance;
            if (editor == null || editor.SceneEditing == null)
            {
                Log("场景编辑不可用。");
                return;
            }

            var picked = new List<Vector3>();
            foreach (var node in editor.SceneEditing.Selection)
            {
                var actorNode = node as FlaxEditor.SceneGraph.ActorNode;
                if (actorNode?.Actor == null) continue;
                picked.Add(actorNode.Actor.Transform.Translation);
            }
            if (picked.Count == 0)
            {
                Log("先在场景里选中一个 Actor（可多选，按层级顺序打点）。");
                return;
            }

            EnsureRoute();
            foreach (var t in picked)
            {
                // Flax 是 Y-up、服务端 Z-up（水平面 = XY）：这里做与 DS 快照同一口径的换轴。
                // 不换轴的后果历史上有过：远端角色 3D 位置错乱（记忆「跨坐标系3D位置同步需Y/Z轴交换」）。
                _draft.PatrolRoute.Points.Add(new MonsterPointModel
                {
                    Seq = _draft.PatrolRoute.Points.Count,
                    X = t.X,
                    Y = t.Z,
                    Z = t.Y,
                });
            }
            CommitAll();
            ApplyRouteToUi();
            Log(string.Format("已从选中 Actor 拾取 {0} 个点（当前共 {1}）。屏幕坐标只是近似，保存前请核对俯视图。",
                picked.Count, _draft.PatrolRoute.Points.Count));
        }

        private void AddManualPoint()
        {
            EnsureRoute();
            var x = ParseDouble(_pointXBox, 0.0);
            var y = ParseDouble(_pointYBox, 0.0);
            var z = ParseDouble(_pointZBox, 0.0);
            _draft.PatrolRoute.Points.Add(new MonsterPointModel
            {
                Seq = _draft.PatrolRoute.Points.Count,
                X = x,
                Y = y,
                Z = z,
            });
            ApplyRouteToUi();
        }

        /// <summary>
        /// 把"选中点停留秒"那一格写进**当前选中的那个路径点**（不动别的点）。
        /// <para>留空或负数都归成 <c>null</c> = "跟路线那一格"：校验器对"写 -1"是直接报错的
        /// （<c>MonsterValidator</c>：要默认值请留空而不是写 -1），所以这里在落草稿之前就把负数收掉，
        /// 而不是让人按了"保存到库"才在回执里看到一条红。</para>
        /// <para><c>0</c> 是**有效值**（这一站明确不停），与"没填"是两回事——
        /// AC 实测 97.8% 的点走的就是这一档。</para>
        /// </summary>
        private void ApplyPointPause()
        {
            var route = _draft == null ? null : _draft.PatrolRoute;
            if (route == null || _pickedPointIndex < 0 || _pickedPointIndex >= route.Points.Count)
            {
                Log("先在右侧俯视图里点一个路径点，再按“写入选中点”。");
                return;
            }

            var raw = _pointPauseBox == null ? string.Empty : (_pointPauseBox.Text ?? string.Empty).Trim();
            var point = route.Points[_pickedPointIndex];
            // 给初值：`raw.Length > 0 && double.TryParse(...)` 短路时编译器无法证明 parsed 被赋过值
            // （离线 csc 门当场报 CS0165）。空串那一支下面用不到它，但声明处必须能定值。
            double parsed = 0d;
            var hasValue = raw.Length > 0
                           && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed);
            if (!hasValue)
            {
                // 空着 = 没填；写不出来也按"没填"处理，而不是静默保留上一个值——
                // 那会让人以为这站配了 7 秒而库里躺着的还是旧的那一份。
                point.PauseSeconds = null;
                Log(string.Format("点 {0} 的停留改回跟路线（{1:0.##} 秒）。未保存。",
                    _pickedPointIndex + 1, route.PauseAtPointSeconds));
                ApplyRouteToUi();
                return;
            }
            if (parsed < 0d)
            {
                point.PauseSeconds = null;
                Log(string.Format("点 {0}：负数按“跟路线”记下了（写 {1:0.##} 会被校验器拒绝——" +
                                  "要默认值请留空）。未保存。", _pickedPointIndex + 1, parsed));
                ApplyRouteToUi();
                return;
            }
            point.PauseSeconds = parsed;
            Log(string.Format("点 {0} 的停留 = {1:0.##} 秒{2}。未保存。",
                _pickedPointIndex + 1, parsed,
                parsed.Equals(0d) ? "（这一站明确不停；不是“没填”）" : string.Empty));
            ApplyRouteToUi();
        }

        private void RemovePickedPoint()
        {
            EnsureRoute();
            if (_pickedPointIndex < 0 || _pickedPointIndex >= _draft.PatrolRoute.Points.Count)
            {
                Log("先在右侧俯视图里点一个路径点，再按“删选中点”。");
                return;
            }
            _draft.PatrolRoute.Points.RemoveAt(_pickedPointIndex);
            for (var i = 0; i < _draft.PatrolRoute.Points.Count; i++) _draft.PatrolRoute.Points[i].Seq = i;
            _pickedPointIndex = -1;
            ApplyRouteToUi();
            Log("已删除该点（未保存）。");
        }

        private void EnsureRoute()
        {
            if (_draft.PatrolRoute != null) return;
            _draft.PatrolRoute = new MonsterRouteModel { Points = new List<MonsterPointModel>(), Name = _draft.Name + " 巡逻" };
            _draft.Behavior.PatrolRouteId = 0;
        }

        // ---------- 工具 ----------

        /// <summary>
        /// 顶层那九张子清单：漏一张接进来，那一层里的键名漂移就没人拦。
        /// <para>以前这里只查 stats/behavior/xp 三层——而 <c>chanceBps</c>、<c>groupId</c>、
        /// <c>conditionKind</c> 这些真正会漂的键住在 skills/lootBindings/entries/points/spawnPoints 里，
        /// STJ 对不认识的键是**静默丢弃**：表现是"保存成功、库里全是默认值"。</para>
        /// </summary>
        private static readonly string[][] NestedKeyTables =
        {
            // [顶层键, 是否数组, 对应 Known*Keys 清单名（下面按名字取集合）]
            new[] { "stats", "0", "stats" },
            new[] { "xp", "0", "xp" },
            new[] { "behavior", "0", "behavior" },
            new[] { "patrolRoute", "0", "route" },
            new[] { "skills", "1", "skill" },
            new[] { "lootBindings", "1", "lootBinding" },
            new[] { "spawnPoints", "1", "spawn" },
        };

        private static HashSet<string> AllowedKeysFor(string table)
        {
            if (table == "stats") return MonsterDraftModel.KnownStatsKeys;
            if (table == "xp") return MonsterDraftModel.KnownXpKeys;
            if (table == "behavior") return MonsterDraftModel.KnownBehaviorKeys;
            if (table == "route") return MonsterDraftModel.KnownRouteKeys;
            if (table == "skill") return MonsterDraftModel.KnownSkillKeys;
            if (table == "lootBinding") return MonsterDraftModel.KnownLootBindingKeys;
            if (table == "spawn") return MonsterDraftModel.KnownSpawnKeys;
            // 认不出清单名时返回**空集**：那样这一层里每个键都会被判成未知并拦下发送，
            // 比"返回 null 然后跳过检查"诚实——漏配一张表必须响，不能安静放行。
            return new HashSet<string>(StringComparer.Ordinal);
        }

        private static string CheckFields(JsonElement obj, HashSet<string> allowed, string path)
        {
            foreach (var field in obj.EnumerateObject())
            {
                if (!allowed.Contains(field.Name)) return path + " 下未知键 " + field.Name;
            }
            return string.Empty;
        }

        /// <summary>按 <see cref="NestedKeyTables"/> 逐层查键名，并对掉落条目与路线点再往下走一层。</summary>
        private static string CheckNested(string name, JsonElement value)
        {
            foreach (var row in NestedKeyTables)
            {
                if (row[0] != name) continue;
                var allowed = AllowedKeysFor(row[2]);
                var isArray = row[1] == "1";
                if (!isArray)
                {
                    return value.ValueKind == JsonValueKind.Object ? CheckFields(value, allowed, name) : string.Empty;
                }
                if (value.ValueKind != JsonValueKind.Array) return string.Empty;
                foreach (var item in value.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object) continue;
                    var error = CheckFields(item, allowed, name);
                    if (error.Length > 0) return error;
                }
                return string.Empty;
            }
            return string.Empty;
        }

        /// <summary>
        /// 写请求前先把自己模型的键名与协议清单对一遍。
        /// <para><c>System.Text.Json</c> 对不认识的键**静默丢弃**，漂一个字母会得到一份
        /// "保存成功但字段全是默认值"的库数据 —— 所以这条自证必须存在，且失败要拦住发送。</para>
        /// </summary>
        private static string SelfCheckKeys(string json)
        {
            if (string.IsNullOrEmpty(json)) return "草稿为空";
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Object) return "根节点不是对象";
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (!MonsterDraftModel.KnownTopLevelKeys.Contains(prop.Name))
                        return "未知顶层键 " + prop.Name;
                    var nested = CheckNested(prop.Name, prop.Value);
                    if (nested.Length > 0) return nested;

                    // 两层深的这两处单独走：掉落绑定 → 逐件条目；巡逻路线 → 路线点。
                    if (prop.Name == "lootBindings" && prop.Value.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var binding in prop.Value.EnumerateArray())
                        {
                            if (binding.ValueKind != JsonValueKind.Object) continue;
                            if (!binding.TryGetProperty("entries", out var entries)) continue;
                            if (entries.ValueKind != JsonValueKind.Array) continue;
                            foreach (var entry in entries.EnumerateArray())
                            {
                                if (entry.ValueKind != JsonValueKind.Object) continue;
                                var e = CheckFields(entry, MonsterDraftModel.KnownDropKeys, "lootBindings.entries");
                                if (e.Length > 0) return e;
                            }
                        }
                    }
                    if (prop.Name == "patrolRoute" && prop.Value.ValueKind == JsonValueKind.Object
                        && prop.Value.TryGetProperty("points", out var points) && points.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var point in points.EnumerateArray())
                        {
                            if (point.ValueKind != JsonValueKind.Object) continue;
                            var e = CheckFields(point, MonsterDraftModel.KnownPointKeys, "patrolRoute.points");
                            if (e.Length > 0) return e;
                        }
                    }
                }
            }
            catch (JsonException ex)
            {
                return "JSON 不合法：" + ex.Message;
            }
            return string.Empty;
        }

        private void GuardBusy(Action action)
        {
            if (_busy)
            {
                Log("上一条操作还在跑（这个窗口一次只发一个子进程请求，避免回执串台）。");
                return;
            }
            _busy = true;
            try
            {
                action();
            }
            finally
            {
                // 异步回调里收尾；同步分支立刻释放。
                Scripting.InvokeOnUpdate(() => _busy = false);
            }
        }

        private void Log(string text)
        {
            if (_consoleBox == null || string.IsNullOrEmpty(text)) return;
            _consoleBox.Text = (_consoleBox.Text ?? string.Empty) + Environment.NewLine + text;
            Debug.Log("[MonsterEditor] " + text);
        }

        private static int IndexOf(Dropdown dropdown)
        {
            if (dropdown == null) return 0;
            var index = dropdown.SelectedIndex;
            return index < 0 ? 0 : index;
        }

        private static int? TriOf(Dropdown dropdown)
        {
            if (dropdown == null) return null;
            switch (dropdown.SelectedIndex)
            {
                case 1: return 0;
                case 2: return 1;
                default: return null;
            }
        }

        private static void SetIndex(Dropdown dropdown, int index)
        {
            if (dropdown == null) return;
            dropdown.SelectedIndex = index < 0 ? 0 : index;
        }

        private static void SetTri(Dropdown dropdown, int? value)
        {
            if (dropdown == null) return;
            dropdown.SelectedIndex = value.HasValue ? (value.Value == 0 ? 1 : 2) : 0;
        }

        private static void SetText(TextBox box, double value)
        {
            if (box == null) return;
            box.Text = value.ToString("0.####", CultureInfo.InvariantCulture);
        }

        private static void SetText(TextBox box, int value)
        {
            if (box == null) return;
            box.Text = value.ToString(CultureInfo.InvariantCulture);
        }

        private static int ParseInt(string text, int fallback)
        {
            if (string.IsNullOrWhiteSpace(text)) return fallback;
            int value;
            return int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) ? value : fallback;
        }

        private static int ParseInt(TextBox box, int fallback)
        {
            return box == null ? fallback : ParseInt(box.Text, fallback);
        }

        private static double ParseDouble(TextBox box, double fallback)
        {
            if (box == null || string.IsNullOrWhiteSpace(box.Text)) return fallback;
            double value;
            return double.TryParse(box.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value) ? value : fallback;
        }

        private static string ReadString(JsonElement element, string name)
        {
            JsonElement child;
            if (!element.TryGetProperty(name, out child) || child.ValueKind != JsonValueKind.String) return string.Empty;
            return child.GetString() ?? string.Empty;
        }

        private static int ReadInt(JsonElement element, string name)
        {
            JsonElement child;
            if (!element.TryGetProperty(name, out child)) return 0;
            if (child.ValueKind == JsonValueKind.Number && child.TryGetInt32(out var value)) return value;
            return 0;
        }

        private static List<int> ReadIdArray(MonsterCliResult result, string name)
        {
            var ids = new List<int>();
            if (!result.HasData) return ids;
            JsonElement array;
            if (!result.Data.TryGetProperty(name, out array) || array.ValueKind != JsonValueKind.Array) return ids;
            foreach (var item in array.EnumerateArray())
            {
                int value;
                if (item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out value)) ids.Add(value);
            }
            return ids;
        }

        private static double RankHpScale(int rank)
        {
            switch (rank)
            {
                case 0: return 1.0;
                case 1: return 2.0;
                case 2: return 2.0;
                case 3: return 4.0;
                default: return 6.0;
            }
        }

        private static double RankDamageScale(int rank)
        {
            switch (rank)
            {
                case 0: return 1.0;
                case 1: return 1.5;
                case 2: return 1.5;
                case 3: return 2.5;
                default: return 3.0;
            }
        }

        private static string On(bool value)
        {
            return value ? "开" : "关";
        }

        // ---------- 行控件 ----------

        private sealed class MonsterRow
        {
            public int Id;
            public string Name = string.Empty;
            public int Rank;
            public int Level;
            public int Domain;
            public bool Published;
        }

        /// <summary>技能行：一行 = 一个挂载。控件横向排，避免窗口纵向爆炸。</summary>
        private sealed class SkillRow
        {
            public const float Height = 90f;   // 四行：y / +22 / +44 / +66（第四行是控制状态三格）
            public MonsterSkillModel Model;

            private readonly TextBox _skillBox;
            private readonly TextBox _chanceBox;
            private readonly TextBox _castBox;
            private readonly TextBox _cdBox;
            private readonly TextBox _rangeBox;
            private readonly Dropdown _slotDrop;
            private readonly Dropdown _targetDrop;
            private readonly Dropdown _interruptDrop;
            private readonly Dropdown _moveDrop;
            private readonly Dropdown _schoolDrop;
            private readonly TextBox _aoeBox;
            private readonly TextBox _minRangeBox;
            private readonly TextBox _targetCountBox;
            private readonly TextBox _damageScaleBox;
            private readonly TextBox _emergencyBox;
            private readonly Dropdown _statusDrop;
            private readonly TextBox _statusSecondsBox;
            private readonly TextBox _statusMagnitudeBox;
            private readonly Label _header;

            public SkillRow(Panel host, float y, int index)
            {
                _header = new Label(2f, y, 60f, 18f) { Text = "#" + (index + 1) + " 技能", Parent = host };
                _skillBox = new TextBox(false, 60f, y, 70f) { Parent = host, WatermarkText = "技能id" };
                _chanceBox = new TextBox(false, 134f, y, 60f) { Parent = host, WatermarkText = "概率bp" };
                _castBox = new TextBox(false, 198f, y, 54f) { Parent = host, WatermarkText = "吟唱ms" };
                _cdBox = new TextBox(false, 256f, y, 54f) { Parent = host, WatermarkText = "冷却ms" };
                _rangeBox = new TextBox(false, 314f, y, 54f) { Parent = host, WatermarkText = "射程m" };

                var y2 = y + 22f;
                _slotDrop = MakeDrop(host, 60f, y2, 100f, _slotNamesLocal());
                _targetDrop = MakeDrop(host, 164f, y2, 120f, _targetNamesLocal());
                _interruptDrop = MakeDrop(host, 288f, y2, 40f, new[] { "不打断", "可打断" });
                _moveDrop = MakeDrop(host, 332f, y2, 40f, new[] { "站桩", "移动" });

                // 系别与 AOE 半径以前只能靠 CLI 写 JSON：库里两列都在、内核都读、客户端还拿它给施法条分色，
                // 唯独编辑器里没有格子——那正是"看起来能配、实际改不动"的那种洞，所以补在同一个技能行上。
                _schoolDrop = MakeDrop(host, 376f, y2, 56f, MonsterPreviewMath.SchoolNames);
                _aoeBox = new TextBox(false, 436f, y2, 46f) { Parent = host, WatermarkText = "AOE m" };

                // 第三行：这四项目前**服务端真的在读**（射程下界、目标数、伤害倍率、残血应急阈值），
                // 以前只能靠 CLI 写 JSON。
                // 第四行给控制状态三格：它们以前**没有消费者**，所以刻意不给输入框（给了就是承诺一件做不到的事）；
                // 切片 59/60/62 起 id/时长/强度都有了运行期消费者（施加到玩家/怪物 + 内核两条闸口 + 减速缩放），
                // 于是按当初那句话把格子开出来。
                var y3 = y + 44f;
                _minRangeBox = new TextBox(false, 60f, y3, 54f) { Parent = host, WatermarkText = "射程下界" };
                _targetCountBox = new TextBox(false, 118f, y3, 46f) { Parent = host, WatermarkText = "目标数" };
                _damageScaleBox = new TextBox(false, 168f, y3, 60f) { Parent = host, WatermarkText = "伤害倍bp" };
                _emergencyBox = new TextBox(false, 232f, y3, 52f) { Parent = host, WatermarkText = "残血%" };
                new Label(2f, y3, 56f, 18f) { Text = "补充", Parent = host };

                // 第四行：控制状态（0=无 1=眩晕 2=沉默 3=定身 4=减速）。下拉下标直接就是 status_id，
                // 这样"界面上选第几项"与"库里存什么号"是同一个数，不会多一层翻译表。
                var y4 = y + 66f;
                new Label(2f, y4, 56f, 18f) { Text = "状态", Parent = host };
                _statusDrop = MakeDrop(host, 60f, y4, 76f, new[] { "无", "眩晕", "沉默", "定身", "减速" });
                _statusSecondsBox = new TextBox(false, 140f, y4, 54f) { Parent = host, WatermarkText = "状态秒" };
                _statusMagnitudeBox = new TextBox(false, 198f, y4, 62f) { Parent = host, WatermarkText = "强度bp" };
            }

            /// <summary>系别下拉的取值表，与服务端 <c>MonsterSchoolBits</c> 由测试对账；下标与 <see cref="MonsterPreviewMath.SchoolNames"/> 一一对应。</summary>
            private static int[] SchoolMasks => MonsterPreviewMath.SchoolMaskOptions;

            /// <summary>掩码 → 下拉序号：库里存的若是多系组合，取<b>最低位</b>那一系（与客户端分色同一规则）。</summary>
            private static int SchoolIndexFor(int mask)
            {
                var masks = SchoolMasks;
                for (var i = 0; i < masks.Length; i++)
                {
                    if (masks[i] == mask) return i;
                }
                for (var i = 1; i < masks.Length; i++)
                {
                    if ((mask & masks[i]) != 0) return i;
                }
                return 0;
            }

            public bool IsUsed()
            {
                return _skillBox != null && !string.IsNullOrWhiteSpace(_skillBox.Text) && ParseIntLocal(_skillBox.Text) > 0;
            }

            public void Apply(List<int> skillIds)
            {
                var used = Model;
                _skillBox.Text = used != null ? used.SkillId.ToString(CultureInfo.InvariantCulture) : string.Empty;
                _chanceBox.Text = used != null ? used.ChanceBps.ToString(CultureInfo.InvariantCulture) : string.Empty;
                _castBox.Text = used != null ? used.CastMs.ToString(CultureInfo.InvariantCulture) : string.Empty;
                _cdBox.Text = used != null ? used.CooldownMs.ToString(CultureInfo.InvariantCulture) : string.Empty;
                _rangeBox.Text = used != null ? used.MaxRange.ToString("0.##", CultureInfo.InvariantCulture) : string.Empty;
                _slotDrop.SelectedIndex = used != null ? Math.Max(0, used.SlotType) : 0;
                _targetDrop.SelectedIndex = used != null ? Math.Max(0, used.TargetSelect) : 0;
                _interruptDrop.SelectedIndex = used == null ? 1 : (used.Interruptible != 0 ? 1 : 0);
                _moveDrop.SelectedIndex = used == null ? 0 : (used.AllowMovement != 0 ? 1 : 0);
                if (_schoolDrop != null) _schoolDrop.SelectedIndex = used == null ? 0 : SchoolIndexFor(used.SchoolMask);
                if (_aoeBox != null) _aoeBox.Text = used != null ? used.AoeRadius.ToString("0.##", CultureInfo.InvariantCulture) : string.Empty;
                if (_minRangeBox != null) _minRangeBox.Text = used != null ? used.MinRange.ToString("0.##", CultureInfo.InvariantCulture) : string.Empty;
                if (_targetCountBox != null) _targetCountBox.Text = used != null ? used.TargetCount.ToString(CultureInfo.InvariantCulture) : string.Empty;
                if (_damageScaleBox != null) _damageScaleBox.Text = used != null ? used.DamageScaleBps.ToString(CultureInfo.InvariantCulture) : string.Empty;
                if (_emergencyBox != null) _emergencyBox.Text = used != null ? used.EmergencyHpPercent.ToString(CultureInfo.InvariantCulture) : string.Empty;
                _statusDrop.SelectedIndex = used == null ? 0 : Math.Clamp(used.StatusId, 0, 4);
                _statusSecondsBox.Text = used != null ? used.StatusDurationSeconds.ToString("0.###", CultureInfo.InvariantCulture) : string.Empty;
                _statusMagnitudeBox.Text = used != null ? used.StatusMagnitudeBps.ToString(CultureInfo.InvariantCulture) : string.Empty;
                _header.Text = "#" + (_slotDrop.SelectedIndex >= 0 ? used != null ? "已用" : "空行" : "?");
            }

            public void Commit(int slotSeq)
            {
                if (Model == null) Model = new MonsterSkillModel();
                Model.SkillId = ParseIntLocal(_skillBox.Text);
                Model.ChanceBps = ParseIntLocal(_chanceBox.Text, 10000);
                Model.CastMs = ParseIntLocal(_castBox.Text);
                Model.CooldownMs = ParseIntLocal(_cdBox.Text, 5000);
                Model.MaxRange = ParseDoubleLocal(_rangeBox.Text, 8.0);
                Model.SlotType = _slotDrop.SelectedIndex;
                Model.TargetSelect = _targetDrop.SelectedIndex;
                Model.Interruptible = _interruptDrop.SelectedIndex == 1 ? 1 : 0;
                Model.AllowMovement = _moveDrop.SelectedIndex == 1 ? 1 : 0;
                Model.SchoolMask = _schoolDrop != null && _schoolDrop.SelectedIndex >= 0
                                   && _schoolDrop.SelectedIndex < SchoolMasks.Length
                    ? SchoolMasks[_schoolDrop.SelectedIndex] : Model.SchoolMask;
                Model.AoeRadius = ParseDoubleLocal(_aoeBox == null ? null : _aoeBox.Text, Model.AoeRadius);
                Model.SlotSeq = slotSeq;
                // 这四格以前只是"被 clamp 一下"（值恒为 0/1/10000/30），现在真的从输入框读。
                Model.MinRange = ParseDoubleLocal(_minRangeBox == null ? null : _minRangeBox.Text, Model.MinRange);
                if (Model.MinRange < 0) Model.MinRange = 0;
                Model.TargetCount = ParseIntLocal(_targetCountBox == null ? null : _targetCountBox.Text, Model.TargetCount);
                if (Model.TargetCount <= 0) Model.TargetCount = 1;
                Model.DamageScaleBps = ParseIntLocal(_damageScaleBox == null ? null : _damageScaleBox.Text, Model.DamageScaleBps);
                if (Model.DamageScaleBps <= 0) Model.DamageScaleBps = 10000;
                Model.EmergencyHpPercent = ParseIntLocal(_emergencyBox == null ? null : _emergencyBox.Text, Model.EmergencyHpPercent);
                // 控制状态三格：号直接取下拉下标（0..4 = 词表本身）；时长/强度负数一律收成 0（"没填"）。
                Model.StatusId = _statusDrop.SelectedIndex;
                Model.StatusDurationSeconds = ParseDoubleLocal(_statusSecondsBox.Text, Model.StatusDurationSeconds);
                if (Model.StatusDurationSeconds < 0) Model.StatusDurationSeconds = 0;
                Model.StatusMagnitudeBps = ParseIntLocal(_statusMagnitudeBox.Text, Model.StatusMagnitudeBps);
                if (Model.StatusMagnitudeBps < 0) Model.StatusMagnitudeBps = 0;
            }

            private static Dropdown MakeDrop(Panel host, float x, float y, float width, string[] items)
            {
                var drop = new Dropdown { Parent = host, Bounds = new Rectangle(x, y, width, 20f) };
                for (var i = 0; i < items.Length; i++) drop.AddItem(items[i]);
                drop.SelectedIndex = 0;
                return drop;
            }

            private static string[] _slotNamesLocal()
            {
                return new[] { "战斗中", "非战斗", "残血应急", "受控时" };
            }

            private static string[] _targetNamesLocal()
            {
                return new[] { "当前威胁", "仇恨内随机", "最近", "血最低", "AOE 以目标为心" };
            }

            private static int ParseIntLocal(string text, int fallback = 0)
            {
                int value;
                return int.TryParse((text ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) ? value : fallback;
            }

            private static double ParseDoubleLocal(string text, double fallback)
            {
                double value;
                return double.TryParse((text ?? string.Empty).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value) ? value : fallback;
            }
        }

        private sealed class BindingRow
        {
            public const float Height = 44f;
            public MonsterLootBindingModel Model;

            private readonly TextBox _tableBox;
            private readonly TextBox _chanceBox;
            private readonly TextBox _minBox;
            private readonly TextBox _maxBox;
            private readonly TextBox _moneyMinBox;
            private readonly TextBox _moneyMaxBox;
            private readonly Dropdown _conditionDrop;
            private readonly TextBox _questBox;
            private readonly Label _header;

            public BindingRow(Panel host, float y, int index)
            {
                _header = new Label(2f, y, 74f, 18f) { Text = "表 #" + index, Parent = host };
                _tableBox = new TextBox(false, 60f, y, 54f) { Parent = host, WatermarkText = "表id" };
                _chanceBox = new TextBox(false, 118f, y, 54f) { Parent = host, WatermarkText = "触发bp" };
                _minBox = new TextBox(false, 176f, y, 40f) { Parent = host, WatermarkText = "件下" };
                _maxBox = new TextBox(false, 220f, y, 40f) { Parent = host, WatermarkText = "件上" };
                _moneyMinBox = new TextBox(false, 264f, y, 40f) { Parent = host, WatermarkText = "金下" };
                _moneyMaxBox = new TextBox(false, 308f, y, 40f) { Parent = host, WatermarkText = "金上" };
                var y2 = y + 22f;
                _conditionDrop = MakeDrop(host, 60f, y2, 110f, new[] { "无条件", "任务持有者", "组队", "稀有度门槛" });
                _questBox = new TextBox(false, 176f, y2, 60f) { Parent = host, WatermarkText = "任务id" };
            }

            public bool IsUsed()
            {
                return !string.IsNullOrWhiteSpace(_tableBox.Text);
            }

            public void Apply()
            {
                var m = Model;
                _tableBox.Text = m != null ? m.LootTableId.ToString(CultureInfo.InvariantCulture) : string.Empty;
                _chanceBox.Text = m != null ? m.RollChanceBps.ToString(CultureInfo.InvariantCulture) : string.Empty;
                _minBox.Text = m != null ? m.MinCount.ToString(CultureInfo.InvariantCulture) : string.Empty;
                _maxBox.Text = m != null ? m.MaxCount.ToString(CultureInfo.InvariantCulture) : string.Empty;
                _moneyMinBox.Text = m != null ? m.MoneyMin.ToString(CultureInfo.InvariantCulture) : string.Empty;
                _moneyMaxBox.Text = m != null ? m.MoneyMax.ToString(CultureInfo.InvariantCulture) : string.Empty;
                _conditionDrop.SelectedIndex = m != null ? Math.Max(0, m.ConditionKind) : 0;
                _questBox.Text = m != null ? m.QuestId.ToString(CultureInfo.InvariantCulture) : string.Empty;
                _header.Text = m == null ? "表 #" + (_conditionDrop.SelectedIndex >= 0 ? "空" : "?") : "表 " + m.LootTableId + "（" + m.Entries.Count + " 件）";
            }

            public void Commit()
            {
                if (Model == null) Model = new MonsterLootBindingModel();
                Model.LootTableId = ParseIntLocal(_tableBox.Text);
                Model.RollChanceBps = ParseIntLocal(_chanceBox.Text);
                Model.MinCount = ParseIntLocal(_minBox.Text, 1);
                Model.MaxCount = ParseIntLocal(_maxBox.Text, Model.MinCount);
                Model.MoneyMin = ParseIntLocal(_moneyMinBox.Text);
                Model.MoneyMax = ParseIntLocal(_moneyMaxBox.Text);
                Model.ConditionKind = _conditionDrop.SelectedIndex;
                Model.QuestId = ParseIntLocal(_questBox.Text);
                if (Model.Entries == null) Model.Entries = new List<MonsterDropModel>();
            }

            private static Dropdown MakeDrop(Panel host, float x, float y, float width, string[] items)
            {
                var drop = new Dropdown { Parent = host, Bounds = new Rectangle(x, y, width, 20f) };
                for (var i = 0; i < items.Length; i++) drop.AddItem(items[i]);
                drop.SelectedIndex = 0;
                return drop;
            }

            private static int ParseIntLocal(string text, int fallback = 0)
            {
                int value;
                return int.TryParse((text ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) ? value : fallback;
            }
        }

        private sealed class DropRow
        {
            // 两行：第一行是"这件掉不掉、掉几件、属哪张表、进不进互斥组"，
            // 第二行是"这件自己的条件与任务 id"。魔兽把 QuestRequired 挂在每一行上
            // （AzerothCore LootMgr.h:127-149），所以"一张表里既有普通货又有一件任务物品"是常态配法；
            // 没这两格就只能整表同进同退，或者去用 CLI 手写 JSON。
            public const float Height = 44f;
            public MonsterDropModel Model;
            public MonsterLootBindingModel OwnerBinding;

            private readonly Dropdown _ownerDrop;
            private readonly TextBox _itemBox;
            private readonly TextBox _chanceBox;
            private readonly TextBox _minBox;
            private readonly TextBox _maxBox;
            private readonly TextBox _groupBox;
            private readonly Dropdown _conditionDrop;
            private readonly TextBox _questBox;
            private readonly Label _hint;
            private List<MonsterLootBindingModel> _bindings;
            private int _seq;

            public DropRow(Panel host, float y, int index)
            {
                _hint = new Label(2f, y, 30f, 20f) { Text = "件" + (index + 1), Parent = host };
                // 归属下拉**固定建 3 项**（表 1/2/3）：Dropdown 的"清空/计数"成员在本工程没有既有用法可依据，
                // 而绑定行上限也是 3，按位序建一次即可，不需要随目录刷新。
                _ownerDrop = new Dropdown { Parent = host, Bounds = new Rectangle(34f, y, 84f, 20f) };
                _ownerDrop.AddItem("表 1");
                _ownerDrop.AddItem("表 2");
                _ownerDrop.AddItem("表 3");
                _ownerDrop.SelectedIndex = 0;
                _itemBox = new TextBox(false, 122f, y, 70f) { Parent = host, WatermarkText = "物品id" };
                _chanceBox = new TextBox(false, 196f, y, 56f) { Parent = host, WatermarkText = "概率bp" };
                _minBox = new TextBox(false, 256f, y, 40f) { Parent = host, WatermarkText = "下" };
                _maxBox = new TextBox(false, 300f, y, 40f) { Parent = host, WatermarkText = "上" };
                // 组号格子必须有：group_id 是服务端真在读的数据（同组互斥抽一件），
                // 没格子就等于这条语义对策划不可达——只能靠 CLI 手写 JSON，历史上踩过一次。
                _groupBox = new TextBox(false, 344f, y, 36f) { Parent = host, WatermarkText = "组" };
                // 第二行：条件与任务 id。首项是「继承表」——对应 -1，也是所有旧数据的取值，
                // 新建一行不该悄悄把这件变成"无条件"（那会把表级的任务判定冲掉）。
                var y2 = y + 22f;
                _conditionDrop = new Dropdown { Parent = host, Bounds = new Rectangle(34f, y2, 120f, 20f) };
                _conditionDrop.AddItem("继承表条件");
                _conditionDrop.AddItem("无条件");
                _conditionDrop.AddItem("需持有任务");
                _conditionDrop.AddItem("组队");
                _conditionDrop.AddItem("稀有度门槛");
                _conditionDrop.SelectedIndex = 0;
                _questBox = new TextBox(false, 158f, y2, 60f) { Parent = host, WatermarkText = "任务id" };
                new Label(2f, y2, 30f, 20f) { Text = "条件", Parent = host };
            }

            public bool IsUsed()
            {
                return !string.IsNullOrWhiteSpace(_itemBox.Text);
            }

            /// <summary>记住本次提交的绑定清单（按位序归属，不重建下拉项）。</summary>
            public void Bind(List<MonsterLootBindingModel> bindings, MonsterLootBindingModel owner)
            {
                _bindings = bindings;
                OwnerBinding = owner;
                var found = bindings == null || owner == null ? -1 : bindings.IndexOf(owner);
                _ownerDrop.SelectedIndex = found < 0 ? 0 : found;
            }

            public void SetOwnerIndex(int index)
            {
                _ownerDrop.SelectedIndex = index < 0 ? 0 : (index >= 3 ? 2 : index);
            }

            public void Apply(List<int> itemIds)
            {
                var m = Model;
                _itemBox.Text = m != null ? m.ItemId.ToString(CultureInfo.InvariantCulture) : string.Empty;
                _chanceBox.Text = m != null ? m.ChanceBps.ToString(CultureInfo.InvariantCulture) : string.Empty;
                _minBox.Text = m != null ? m.CountMin.ToString(CultureInfo.InvariantCulture) : string.Empty;
                _maxBox.Text = m != null ? m.CountMax.ToString(CultureInfo.InvariantCulture) : string.Empty;
                _groupBox.Text = m != null ? m.GroupId.ToString(CultureInfo.InvariantCulture) : string.Empty;
                // 下拉首项 = 继承（-1），所以索引比条件值大 1。
                _conditionDrop.SelectedIndex = m != null ? Math.Clamp(m.ConditionKind + 1, 0, 4) : 0;
                _questBox.Text = m != null ? m.QuestId.ToString(CultureInfo.InvariantCulture) : string.Empty;
            }

            public void Commit(int bindingCount)
            {
                if (Model == null) Model = new MonsterDropModel();
                Model.ItemId = ParseIntLocal(_itemBox.Text);
                Model.ChanceBps = ParseIntLocal(_chanceBox.Text, 1000);
                Model.CountMin = Math.Max(1, ParseIntLocal(_minBox.Text, 1));
                Model.CountMax = Math.Max(Model.CountMin, ParseIntLocal(_maxBox.Text, Model.CountMin));
                Model.Seq = _seq;
                _seq++;

                // 组号留空按 0（逐件独立掷）：这是加组之前的行为，老草稿不该因为多了个格子而变语义。
                Model.GroupId = ParseIntLocal(_groupBox.Text, 0);
                // 条件：首项(0)=继承(-1)，其余索引-1 就是条件值；默认落在继承上，不悄悄改成"无条件"。
                Model.ConditionKind = _conditionDrop.SelectedIndex - 1;
                Model.QuestId = ParseIntLocal(_questBox.Text, 0);

                // 归属以**下拉的位序**为准（用户可以在窗口里把某件改挂到另一张表）
                var index = _ownerDrop.SelectedIndex;
                var owner = _bindings != null && index >= 0 && index < _bindings.Count ? _bindings[index] : OwnerBinding;
                if (owner == null) return;
                OwnerBinding = owner;
                if (owner.Entries == null) owner.Entries = new List<MonsterDropModel>();
                if (!owner.Entries.Contains(Model)) owner.Entries.Add(Model);
            }

            private static int ParseIntLocal(string text, int fallback = 0)
            {
                int value;
                return int.TryParse((text ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) ? value : fallback;
            }
        }

        private sealed class SpawnRow
        {
            // 三行：第一行是"这只怪在哪儿、几只"，第二行是"脸朝哪、几点复活、这个点还开不开"，
            // 第三行是"这条点只在哪一片 DS 上刷"。后两行里那三项以前只能靠 CLI 写 JSON，而它们都有真消费者：
            // `respawn_override_seconds` 覆盖刷新公式、`enabled=0` 的点在导出内容时就被筛掉、
            // `shard_id` 在装配处按会话分片挡点（MonsterSpawnPointBuilder，与 NPC 那条同一口径）。
            // 没格子等于功能对策划不可达。
            // `zone_id` 刻意不给格子：运行期**一次都不读**它（判据是 scripts/verify-monster-panel-columns.py
            // 那条"运行期 0 读"的活检查），给了格子就是承诺一件不会发生的事。
            public const float Height = 66f;
            public MonsterSpawnModel Model;

            private readonly TextBox _idBox;
            private readonly TextBox _xBox;
            private readonly TextBox _yBox;
            private readonly TextBox _zBox;
            private readonly TextBox _aliveBox;
            private readonly TextBox _spacingBox;
            private readonly TextBox _levelBox;
            private readonly TextBox _yawBox;
            private readonly TextBox _respawnBox;
            private readonly TextBox _routeBox;
            private readonly TextBox _shardBox;
            private readonly Dropdown _enabledDrop;
            private readonly Label _hint;

            public SpawnRow(Panel host, float y, int index)
            {
                _hint = new Label(2f, y, 34f, 20f) { Text = "点" + (index + 1), Parent = host };
                _idBox = new TextBox(false, 36f, y, 56f) { Parent = host, WatermarkText = "点位id" };
                _xBox = new TextBox(false, 96f, y, 52f) { Parent = host, WatermarkText = "X" };
                _yBox = new TextBox(false, 150f, y, 52f) { Parent = host, WatermarkText = "Y" };
                _zBox = new TextBox(false, 204f, y, 44f) { Parent = host, WatermarkText = "Z" };
                _aliveBox = new TextBox(false, 250f, y, 40f) { Parent = host, WatermarkText = "只数" };
                _spacingBox = new TextBox(false, 292f, y, 40f) { Parent = host, WatermarkText = "间距" };
                _levelBox = new TextBox(false, 334f, y, 44f) { Parent = host, WatermarkText = "等级覆" };

                var y2 = y + 22f;
                _yawBox = new TextBox(false, 36f, y2, 52f) { Parent = host, WatermarkText = "朝向°" };
                _respawnBox = new TextBox(false, 92f, y2, 66f) { Parent = host, WatermarkText = "刷新覆盖秒" };
                _enabledDrop = MakeDrop(host, 162f, y2, 74f, new[] { "启用", "停用" });
                // 路线覆盖：运行时按"刷怪点覆盖 → 行为表 patrol_route_id"取这条怪走哪条线
                // （DsMonster.PatrolRouteId），填 0 或留空 = 不覆盖 = 用行为表那条。
                _routeBox = new TextBox(false, 240f, y2, 74f) { Parent = host, WatermarkText = "路线覆盖id" };

                // 第三行：分片号。0 与"留空"同义 = 这片点在本 DS 集群的**每一片**上都刷
                // （DsWorldSession 的筛法是 `ShardId != 0 && ShardId != 本片 ⇒ 跳过`）。
                // 多片开世界时"这条线只刷在二区"是常态配法，没这格就只能靠 CLI 手写 JSON。
                var y3 = y + 44f;
                new Label(2f, y3, 34f, 20f) { Text = "分片", Parent = host };
                _shardBox = new TextBox(false, 36f, y3, 80f) { Parent = host, WatermarkText = "0=全片" };
            }

            public bool IsUsed()
            {
                return !string.IsNullOrWhiteSpace(_idBox.Text);
            }

            public void Apply()
            {
                var m = Model;
                _idBox.Text = m != null ? m.SpawnPointId.ToString(CultureInfo.InvariantCulture) : string.Empty;
                _xBox.Text = m != null ? m.X.ToString("0.##", CultureInfo.InvariantCulture) : string.Empty;
                _yBox.Text = m != null ? m.Y.ToString("0.##", CultureInfo.InvariantCulture) : string.Empty;
                _zBox.Text = m != null ? m.Z.ToString("0.##", CultureInfo.InvariantCulture) : string.Empty;
                _aliveBox.Text = m != null ? m.MaxAlive.ToString(CultureInfo.InvariantCulture) : string.Empty;
                _spacingBox.Text = m != null ? m.SlotSpacing.ToString("0.##", CultureInfo.InvariantCulture) : string.Empty;
                _levelBox.Text = m != null && m.LevelOverride.HasValue ? m.LevelOverride.Value.ToString(CultureInfo.InvariantCulture) : string.Empty;
                _yawBox.Text = m != null ? m.Yaw.ToString("0.##", CultureInfo.InvariantCulture) : string.Empty;
                _respawnBox.Text = m != null ? m.RespawnOverrideSeconds.ToString("0.##", CultureInfo.InvariantCulture) : string.Empty;
                // 0 与"没填"同义（不覆盖），所以回显时 0 留空——否则满屏的 0 会把"这只怪没覆盖路线"
                // 和"策划显式写了 0"混成一种看起来像误配的样子。
                _routeBox.Text = m != null && m.RouteOverrideId > 0
                    ? m.RouteOverrideId.ToString(CultureInfo.InvariantCulture)
                    : string.Empty;
                // 分片号同理：0 = 全片，回显留空；显式写 0 与留空同义（筛法只认非 0）。
                _shardBox.Text = m != null && m.ShardId != 0
                    ? m.ShardId.ToString(CultureInfo.InvariantCulture)
                    : string.Empty;
                if (_enabledDrop != null) _enabledDrop.SelectedIndex = m != null && m.Enabled == 0 ? 1 : 0;
            }

            public void Commit()
            {
                if (Model == null) Model = new MonsterSpawnModel();
                Model.SpawnPointId = ParseIntLocal(_idBox.Text);
                Model.X = ParseDoubleLocal(_xBox.Text);
                Model.Y = ParseDoubleLocal(_yBox.Text);
                Model.Z = ParseDoubleLocal(_zBox.Text);
                Model.MaxAlive = Math.Max(1, ParseIntLocal(_aliveBox.Text, 3));
                Model.SlotSpacing = ParseDoubleLocal(_spacingBox.Text, 8.0);
                Model.LevelOverride = string.IsNullOrWhiteSpace(_levelBox.Text) ? (int?)null : ParseIntLocal(_levelBox.Text);
                Model.Yaw = ParseDoubleLocal(_yawBox.Text);
                // 0 与"没填"必须同义（0 = 不覆盖，走模板的刷新公式），所以空串也写 0；负数按没填处理。
                var respawn = ParseDoubleLocal(_respawnBox.Text);
                Model.RespawnOverrideSeconds = respawn > 0 ? respawn : 0;
                // 路线覆盖同样把"空/负数"收成 0 = 不覆盖（与库里那一列的 0=用行为表 同义）
                var routeOverride = ParseIntLocal(_routeBox.Text);
                Model.RouteOverrideId = routeOverride > 0 ? routeOverride : 0;
                // 分片号：长整型（ShardId 是 long），空/非数/负数一律 0 = 全片
                Model.ShardId = ParseLongLocal(_shardBox.Text);
                Model.Enabled = _enabledDrop == null || _enabledDrop.SelectedIndex != 1 ? 1 : 0;
            }

            private static Dropdown MakeDrop(Panel host, float x, float y, float width, string[] items)
            {
                var drop = new Dropdown { Parent = host, Bounds = new Rectangle(x, y, width, 20f) };
                for (var i = 0; i < items.Length; i++) drop.AddItem(items[i]);
                drop.SelectedIndex = 0;
                return drop;
            }

            private static int ParseIntLocal(string text, int fallback = 0)
            {
                int value;
                return int.TryParse((text ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) ? value : fallback;
            }

            private static double ParseDoubleLocal(string text, double fallback = 0.0)
            {
                double value;
                return double.TryParse((text ?? string.Empty).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value) ? value : fallback;
            }

            private static long ParseLongLocal(string text)
            {
                long value;
                return long.TryParse((text ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value > 0
                    ? value
                    : 0L;
            }
        }
    }
}
