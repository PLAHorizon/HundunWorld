using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;
using FlaxEngine;
using FlaxEngine.GUI;
using FlaxEditor.Windows;

namespace PetEditor
{
    /// <summary>
    /// 宠物编辑器主窗口：宠物编号与重复副本、资质与成长、技能学习与打书、行为与 AI 档位、
    /// 默契四轴与孵化蜕变、装备穿戴、辅助反哺、合成/合体/临时变异、跟随与自动寻路。
    /// <para>
    /// <b>数据权威在 SQL Server</b>，本窗口经 <see cref="PetCli"/> 子进程读写；
    /// 校验规则只在服务端那一份 —— 窗口里的即时提示只是预览，<c>validate/save</c> 的回执才是结论。
    /// </para>
    /// <para>
    /// <b>为什么控件全部绝对定位</b>：Flax 的构造期布局会在父尺寸仍为 0 时按 0 重算位置，
    /// 于是非 TopLeft 锚的控件"凭空不显示"（本工程已踩过多次）。所有动态排布集中在
    /// <see cref="LayoutNow"/>，由 <c>SizeChanged</c> 触发。
    /// </para>
    /// <para>
    /// <b>为什么即时预览走 <see cref="PetPreviewMath"/> 而按钮走 CLI</b>：滑杆拖动要立刻出数，
    /// 不能每格起一个子进程；但"结论性"的数字（发布前那一次）必须由内核算，
    /// 所以试算按钮的回执会与本地预览交叉核对，不一致就在控制台报红。
    /// </para>
    /// </summary>
    public sealed class PetEditorWindow : EditorWindow
    {
        private const float ToolbarHeight = 88f;
        private const float LeftWidth = 250f;
        private const float ConsoleHeight = 130f;
        private const float TabBarHeight = 26f;
        private const float RowHeight = 26f;
        private const int MaxSpeciesRows = 80;

        private static PetEditorWindow _instance;

        private static readonly string[] TabTitles =
        {
            "基础·外观", "资质·成长", "技能·打书", "行为·AI",
            "默契·孵化", "装备·辅助", "合成·变异", "跟随·寻路",
        };

        private readonly string[] _rarityNames = { "未知（号段非法）", "普通", "变异/稀有", "神兽", "活动限定" };
        private readonly string[] _originNames = { "野生", "宝宝", "成年", "变异", "二代", "神兽" };
        private readonly string[] _growthTierNames = { "普通", "优秀", "杰出", "卓越", "完美" };
        private readonly string[] _captureNames = { "左键瞬捕", "战斗残血", "引导驯服", "对战收服", "不可捕捉" };
        private readonly string[] _triNames = { "跟档位", "强制关", "强制开" };
        private readonly string[] _yesNoNames = { "否", "是" };
        private readonly string[] _intellectNames =
        {
            "0 木讷（只跟不主动）", "1 本能（会放技不选目标）", "2 训练有素（索敌+按级释放）",
            "3 聪颖（+避让+护主）", "4 通灵（+保留控制技）",
        };
        private readonly string[] _stanceNames = { "畏缩", "被动", "防御", "攻击" };
        private readonly string[] _commandNames = { "攻击", "跟随", "待命", "防御", "撤退" };
        private readonly string[] _personalityNames = { "勇猛", "精明", "胆小", "谨慎", "忠诚" };
        private readonly string[] _slotKindNames = { "可学", "天生必带", "领悟候选" };
        private readonly string[] _assistKindNames =
        {
            "力量（借力）", "灵气（凝神）", "体力（强身）", "身法（瞬影）", "定力（移魂）",
            "生命转化（共生）", "内力回复（血迹）", "替挡会心（忠心）", "吸敌内力（吸气）", "伤害转化",
        };
        private readonly string[] _equipSlotNames =
        {
            "护腕", "项圈", "躯干", "头部", "爪", "护符", "兽环", "兽铃",
        };
        private readonly string[] _mergeModeNames = { "炼妖（合成）", "繁殖（合体）" };

        private readonly Button[] _listButtons = new Button[MaxSpeciesRows];
        private readonly List<string> _listLabels = new List<string>();
        private readonly Button[] _tabButtons = new Button[TabTitles.Length];

        private Panel _toolbar;
        private Panel _leftPanel;
        private Panel _tabBar;
        private Panel _formHost;
        private Panel _viewHost;
        private Panel _consolePanel;
        private TextBox _consoleBox;
        private Label _statusLabel;
        private TextBox _cliPathBox;

        private readonly Panel[] _forms = new Panel[TabTitles.Length];

        // ---- 工具条 ----
        private Button _btnDetect;
        private Button _btnRefresh;
        private Button _btnRunDraftAction;
        private Button _btnRunDbAction;
        private Button _btnRunPreview;
        private Button _btnExportVisuals;

        /// <summary>草稿下拉：新建/读草稿/校验/存草稿。</summary>
        private Dropdown _draftDrop;

        /// <summary>库操作下拉：把"保存到库/发布/停用/删除"收进一个选择器。
        /// 少 4 个平铺按钮只是表面收益，真正的收益是**删除与停用不再是一下就点到的东西</b>。</summary>
        private Dropdown _dbActionDrop;

        /// <summary>试算项下拉：六项试算共用一个执行按钮（参数都来自当前页签的表单）。</summary>
        private Dropdown _previewDrop;

        // 条目带组名前缀：Dropdown 只显示"当前选中项"，不带组名时这一栏看起来就是三个普通按钮
        // （编辑器截图里出现过"保存到库 | 执行库操作"读成重复的例子）。加上前缀后，
        // "草稿：新建"既说明分组，也暗示它是可以换的。
        private static readonly string[] DraftActionNames = { "草稿：新建", "草稿：读草稿", "草稿：校验", "草稿：存草稿" };
        private static readonly string[] DbActionNames = { "库操作：保存到库", "库操作：发布", "库操作：停用", "库操作：删除" };
        private static readonly string[] PreviewNames = { "试算：属性", "试算：打书", "试算：合成/繁殖", "试算：反哺", "试算：捕捉", "试算：默契" };

        // ---- ① 基础·外观 ----
        private TextBox _idBox;
        private TextBox _nameBox;
        private TextBox _modelKeyBox;
        private TextBox _prefabPathBox;
        private TextBox _baseColorBox;
        private TextBox _scaleBox;
        private TextBox _standBox;
        private TextBox _carryMinBox;
        private TextBox _carryMaxBox;
        private TextBox _hatchSecondsBox;
        private TextBox _hatchLevelBox;
        private TextBox _ownerSlackBox;
        private TextBox _potentialBox;
        private Dropdown _originDrop;
        private Dropdown _captureDrop;
        private Dropdown _immortalDrop;
        private Label _rarityLabel;
        private Label _versionLabel;
        private TextBox _captureBaseBox;
        private TextBox _captureGuidanceBox;
        private TextBox _mutationChanceBox;

        // ---- ② 资质·成长 ----
        private TextBox _atkMinBox, _atkMaxBox, _defMinBox, _defMaxBox, _staMinBox, _staMaxBox;
        private TextBox _manaMinBox, _manaMaxBox, _spdMinBox, _spdMaxBox;
        private TextBox _growthMinBox, _growthMaxBox;
        private TextBox _comprehensionMaxBox, _spiritMaxBox, _mutationTierMaxBox;
        private Dropdown _superSpiritDrop;
        private Dropdown _growthTierDrop;

        // ---- ③ 技能·打书 ----
        private TextBox _baseSlotsBox, _maxSlotsBox, _slotsByGradeBox;
        private TextBox _unlockABox, _unlockBBox, _growLevelBox, _growExistingBox;
        private TextBox _growChanceBox, _overwriteBox, _lockableBox, _poolBox, _equippedBox, _innerCoreBox;
        private TextBox _innateSkillBox, _learnableSkillBox, _epiphanySkillBox, _epiphanyChanceBox;
        private TextBox _conflictBox;
        private Dropdown _slotKindDrop;
        private TextBox _previewSkillBox, _previewSeedBox;

        // ---- ④ 行为·AI ----
        private Dropdown _intellectDrop, _stanceDrop, _commandDrop;
        private Dropdown _flagAcquireDrop, _flagAutoCastDrop, _flagEvadeDrop, _flagGuardDrop, _flagHoldDrop;
        private TextBox _personalityBox;

        // ---- ⑤ 默契·孵化 ----
        private TextBox _loyaltyBox, _happinessBox, _lifespanBox, _intimacyBox, _happinessScaleBox;

        // ---- ⑥ 装备·辅助 ----
        private TextBox _equipSlotsBox, _maxStarBox, _awakeningPiecesBox;
        private Dropdown _gemDrop, _appraisalDrop, _awakeningDrop, _setFamilyDrop, _ownershipDrop;
        private TextBox _assistSkillBox, _assistKindBox, _assistLevelsBox;

        // ---- ⑦ 合成·变异 ----
        private Dropdown _mergeModeDrop, _fuseEnabledDrop, _breedEnabledDrop;
        private TextBox _fuseMinLevelBox, _fuseSuccessBox, _fuseQualityBox, _fuseGrowthBox;
        private TextBox _breedGapBox, _breedHappinessBox, _breedLifespanBox, _breedCountBox, _breedClaimBox;
        private TextBox _mutationKeyBox, _mutationDurationBox, _mutationCapBox;

        // ---- ⑧ 跟随·寻路 ----
        private TextBox _followDistanceBox, _leashBox, _teleportBox, _repathBox, _followSpeedBox;
        private TextBox _arriveBox, _corpseBox, _reviveBox, _summonCdBox, _startDelayBox;
        private Button _btnCaptureFollowPoint;

        // ---- 视图 ----
        private SkillSlotView _slotView;
        private GrowthCurveView _growthView;
        private StatRadarView _radarView;
        private FollowRadiusView _followView;
        private PedigreeView _pedigreeView;

        private PetDraftModel _draft = PetDraftModel.CreateNew(500_000);
        private bool _busy;

        /// <summary>打开或聚焦窗口（必须主线程调用）。</summary>
        public static void ShowWindow()
        {
            var editor = FlaxEditor.Editor.Instance;
            if (_instance != null)
            {
                _instance.FocusOrShow(FlaxEditor.GUI.Docking.DockState.DockRight);
                return;
            }
            _instance = new PetEditorWindow(editor);
            editor.Windows.Open(_instance);
            _instance.Show(FlaxEditor.GUI.Docking.DockState.DockRight);
        }

        public PetEditorWindow(FlaxEditor.Editor editor)
            : base(editor, false, ScrollBars.None)
        {
            Title = "宠物编辑器";

            BuildToolbar();
            BuildList();
            BuildTabBar();
            BuildFormBase();
            BuildFormStats();
            BuildFormSkills();
            BuildFormBehavior();
            BuildFormBond();
            BuildFormEquipAssist();
            BuildFormMergeMutation();
            BuildFormFollow();
            BuildViews();
            BuildConsole();

            SelectTab(0);
            SizeChanged += control => LayoutNow();
            LayoutNow();

            ApplyDraftToUi();
            Log("宠物编辑器就绪。刷新目录需要已编出 Horizon.PetCli（dotnet build Horizon.PetCli/Horizon.PetCli.csproj）"
                + "且 021/022 已执行；两者都没到位时，号段与即时预览仍可用（走本地 PetPreviewMath）。");
        }

        // ---------- 构造：工具条 ----------

        private void BuildToolbar()
        {
            _toolbar = new Panel(ScrollBars.None) { Parent = this };

            _cliPathBox = new TextBox(false, 6f, 6f, 300f)
            {
                Parent = _toolbar,
                WatermarkText = "Horizon.PetCli.exe 路径（留空自动探测）",
            };

            _btnDetect = MakeToolButton(312f, 6f, "探测", () =>
            {
                var path = PetCli.DetectExePath();
                Log(string.IsNullOrEmpty(path) ? "没探测到 Horizon.PetCli.exe：先在仓库根编一次" : "CLI = " + path);
            });
            _btnRefresh = MakeToolButton(368f, 6f, "刷新目录", RunCatalog);
            // 草稿四件事收成一对：新建/读草稿/校验/存草稿在下拉里选，右边一个执行。
            _draftDrop = MakeToolDrop(452f, 6f, 150f, DraftActionNames);
            _btnRunDraftAction = MakeToolButton(608f, 6f, "执行草稿操作", RunDraftAction);

            // 第二行只留"选一个 + 执行"两对，外加一个导出：原来这里平铺七个按钮，
            // 窗口窄一点就互相挤，而"试算"这一族本来就要先看页签再点，平铺并不省步数。
            // 间距按"按钮宽度随文字自动撑开"留：执行库操作 5 字 ≈ 110px，
            // 挤在 142→248 之间会压住下一个下拉的边框（第一行的 56~84px 间距是 2~4 字的经验值）。
            _dbActionDrop = MakeToolDrop(6f, 38f, 150f, DbActionNames);
            _btnRunDbAction = MakeToolButton(162f, 38f, "执行库操作", RunDbAction);
            _previewDrop = MakeToolDrop(278f, 38f, 122f, PreviewNames);
            _btnRunPreview = MakeToolButton(406f, 38f, "运行试算", RunPreviewAction);
            _btnExportVisuals = MakeToolButton(510f, 38f, "导出外观表", RunExportVisuals);

            _statusLabel = new Label(6f, 66f, 900f, 18f) { Parent = _toolbar, Text = "未探测：点 [探测] 确认 Horizon.PetCli 是否已编出" };
        }

        private Button MakeToolButton(float x, float y, string text, Action action)
        {
            var button = new Button(x, y, 0f, 24f) { Text = text, Parent = _toolbar };
            button.Clicked += () => action();
            return button;
        }

        private Dropdown MakeToolDrop(float x, float y, float width, string[] items)
        {
            var dropdown = new Dropdown
            {
                Parent = _toolbar,
                Bounds = new Rectangle(x, y, width, 24f),
            };
            for (var i = 0; i < items.Length; i++) dropdown.AddItem(items[i]);
            dropdown.SelectedIndex = 0;
            return dropdown;
        }

        /// <summary>草稿派发（新建/读草稿/校验/存草稿）。</summary>
        private void RunDraftAction()
        {
            switch (_draftDrop.SelectedIndex)
            {
                case 0: RunNew(); break;
                case 1: RunReadDraft(); break;
                case 2: RunValidate(); break;
                case 3: RunSaveDraft(); break;
                default: Log("草稿下拉没有选中项"); break;
            }
        }

        /// <summary>库操作派发：下拉只决定"做哪件"，真正的执行仍是原来那几个入口——
        /// 收布局不收走判据，校验与回执都还在原处。</summary>
        private void RunDbAction()
        {
            switch (_dbActionDrop.SelectedIndex)
            {
                case 0: RunSave(); break;
                case 1: RunSetState("publish"); break;
                case 2: RunSetState("retire"); break;
                case 3: RunDelete(); break;
                default: Log("库操作下拉没有选中项"); break;
            }
        }

        /// <summary>试算派发（六项都读当前页签的表单，参数与原来逐个点按钮时一致）。</summary>
        private void RunPreviewAction()
        {
            switch (_previewDrop.SelectedIndex)
            {
                case 0: RunPreviewStats(); break;
                case 1: RunPreviewLearn(); break;
                case 2: RunPreviewMerge(); break;
                case 3: RunPreviewBonus(); break;
                case 4: RunPreviewCapture(); break;
                case 5: RunPreviewBond(); break;
                default: Log("试算下拉没有选中项"); break;
            }
        }

        // ---------- 构造：左侧列表 ----------

        private void BuildList()
        {
            _leftPanel = new Panel(ScrollBars.Vertical) { Parent = this };
            for (var i = 0; i < _listButtons.Length; i++)
            {
                var captured = i;
                var button = new Button(4f, 4f + i * 24f, LeftWidth - 16f, 22f)
                {
                    Parent = _leftPanel,
                    Text = string.Empty,
                    Visible = false,
                };
                button.Clicked += () => SelectSpeciesRow(captured);
                _listButtons[i] = button;
            }
        }

        private void BuildTabBar()
        {
            _tabBar = new Panel(ScrollBars.None) { Parent = this };
            for (var i = 0; i < TabTitles.Length; i++)
            {
                var captured = i;
                var button = new Button(2f + i * 108f, 2f, 104f, TabBarHeight - 4f)
                {
                    Text = TabTitles[i],
                    Parent = _tabBar,
                };
                button.Clicked += () => SelectTab(captured);
                _tabButtons[i] = button;
            }
        }

        // ---------- 行助手（全部 TopLeft + 显式推进 y）----------

        private TextBox Num(Panel host, ref float y, string caption, string watermark,
            float captionWidth = 92f, float boxWidth = 72f)
        {
            new Label(2f, y, captionWidth, 20f) { Text = caption, Parent = host };
            var box = new TextBox(false, captionWidth + 4f, y, boxWidth)
            {
                Parent = host,
                WatermarkText = watermark,
            };
            y += RowHeight;
            return box;
        }

        private Dropdown Drop(Panel host, ref float y, string caption, string[] items,
            float captionWidth = 92f, float boxWidth = 150f)
        {
            new Label(2f, y, captionWidth, 20f) { Text = caption, Parent = host };
            var dropdown = new Dropdown
            {
                Parent = host,
                Bounds = new Rectangle(captionWidth + 4f, y, boxWidth, 22f),
            };
            for (var i = 0; i < items.Length; i++) dropdown.AddItem(items[i]);
            dropdown.SelectedIndex = 0;
            y += RowHeight;
            return dropdown;
        }

        private void Section(Panel host, ref float y, string title)
        {
            new Label(2f, y, 320f, 20f) { Text = title, Parent = host };
            y += 24f;
        }

        private Panel NewForm(int index)
        {
            if (_formHost == null) _formHost = new Panel(ScrollBars.None) { Parent = this };
            var panel = new Panel(ScrollBars.Vertical) { Parent = _formHost };
            _forms[index] = panel;
            return panel;
        }

        // ---------- 八个面板 ----------

        private void BuildFormBase()
        {
            var host = NewForm(0);
            var y = 6f;
            Section(host, ref y, "① 宠物编号、外观与获得方式");

            _idBox = new TextBox(false, 96f, y, 120f) { Parent = host, WatermarkText = "宠物编号 500000+" };
            new Label(2f, y, 92f, 20f) { Text = "编号", Parent = host };
            _rarityLabel = new Label(224f, y, 220f, 20f) { Text = "稀有度：-", Parent = host };
            y += RowHeight;

            _versionLabel = new Label(96f, y, 240f, 20f) { Text = "v-  状态：-", Parent = host };
            new Label(2f, y, 92f, 20f) { Text = "版本/状态", Parent = host };
            y += RowHeight;

            _nameBox = new TextBox(false, 96f, y, 220f) { Parent = host, WatermarkText = "显示名（只做展示，不参与判等）" };
            new Label(2f, y, 92f, 20f) { Text = "名称", Parent = host };
            y += RowHeight;

            _modelKeyBox = Num(host, ref y, "模型键", "model_key", 92f, 200f);
            _prefabPathBox = Num(host, ref y, "预制体路径", "Content/…/X.prefab（空=美术未就位）", 92f, 220f);
            _baseColorBox = Num(host, ref y, "基色键", "base", 92f, 120f);
            _standBox = Num(host, ref y, "待机状态", "Idle", 92f, 120f);
            _scaleBox = Num(host, ref y, "体型缩放", "1.00", 92f, 72f);

            _originDrop = Drop(host, ref y, "出身", _originNames, 92f, 120f);
            _captureDrop = Drop(host, ref y, "捕捉方式", _captureNames, 92f, 160f);
            _immortalDrop = Drop(host, ref y, "寿命无限", _yesNoNames, 92f, 90f);

            _carryMinBox = Num(host, ref y, "携带段下界", "0", 92f, 66f);
            _carryMaxBox = Num(host, ref y, "携带段上界", "175", 92f, 66f);
            _hatchSecondsBox = Num(host, ref y, "孵化秒", "0=捕捉即得", 92f, 72f);
            _hatchLevelBox = Num(host, ref y, "出壳等级", "0", 92f, 66f);
            _ownerSlackBox = Num(host, ref y, "可超主级数", "天龙10 / WoW0", 92f, 90f);
            _potentialBox = Num(host, ref y, "每级潜能点", "5", 92f, 66f);
            _captureBaseBox = Num(host, ref y, "捕捉基准bp", "3000", 92f, 72f);
            _captureGuidanceBox = Num(host, ref y, "驯服引导秒", "20", 92f, 72f);
            _mutationChanceBox = Num(host, ref y, "变异概率bp", "50", 92f, 72f);
        }

        private void BuildFormStats()
        {
            var host = NewForm(1);
            var y = 6f;
            Section(host, ref y, "② 五维资质区间与成长（裸资；加成走悟性/灵性/变异）");

            _atkMinBox = Num(host, ref y, "攻击资质下界", "1000");
            _atkMaxBox = Num(host, ref y, "攻击资质上界", "1500");
            _defMinBox = Num(host, ref y, "防御资质下界", "1000");
            _defMaxBox = Num(host, ref y, "防御资质上界", "1500");
            _staMinBox = Num(host, ref y, "体力资质下界", "1000");
            _staMaxBox = Num(host, ref y, "体力资质上界", "1500");
            _manaMinBox = Num(host, ref y, "法力资质下界", "1000");
            _manaMaxBox = Num(host, ref y, "法力资质上界", "1500");
            _spdMinBox = Num(host, ref y, "速度资质下界", "1000");
            _spdMaxBox = Num(host, ref y, "速度资质上界", "1500");

            Section(host, ref y, "成长与提升上限");
            _growthMinBox = Num(host, ref y, "成长下界bp", "10000=1.000", 92f, 90f);
            _growthMaxBox = Num(host, ref y, "成长上界bp", "12000=1.200", 92f, 90f);
            _growthTierDrop = Drop(host, ref y, "预览用档位", _growthTierNames, 92f, 120f);
            _comprehensionMaxBox = Num(host, ref y, "悟性上限", "10（0=不可提）", 92f, 72f);
            _spiritMaxBox = Num(host, ref y, "灵性上限", "10", 92f, 72f);
            _superSpiritDrop = Drop(host, ref y, "超灵档", _yesNoNames, 92f, 90f);
            _mutationTierMaxBox = Num(host, ref y, "变异最高档", "8", 92f, 72f);
        }

        private void BuildFormSkills()
        {
            var host = NewForm(2);
            var y = 6f;
            Section(host, ref y, "③ 技能格与打书规则");

            _baseSlotsBox = Num(host, ref y, "基础格数", "4");
            _maxSlotsBox = Num(host, ref y, "绝对上限", "8");
            _slotsByGradeBox = Num(host, ref y, "品级格数表", "4,6,8,11,13（空=不分品级）", 92f, 180f);
            _unlockABox = Num(host, ref y, "悟性开格甲", "4（0=关闭）");
            _unlockBBox = Num(host, ref y, "悟性开格乙", "7（0=关闭）");
            _growLevelBox = Num(host, ref y, "顶书等级门", "125");
            _growExistingBox = Num(host, ref y, "顶书已有门", "3");
            _growChanceBox = Num(host, ref y, "顶书开格bp", "1500");
            _overwriteBox = Num(host, ref y, "顶替概率bp", "7000");
            _lockableBox = Num(host, ref y, "可锁定格数", "2");
            _poolBox = Num(host, ref y, "技能池上限", "6（WoW BP）");
            _equippedBox = Num(host, ref y, "同时装配数", "3");
            _innerCoreBox = Num(host, ref y, "内丹格数", "1-6");

            Section(host, ref y, "技能清单（CSV 技能号；800000+技能id）");
            _innateSkillBox = Num(host, ref y, "天生必带", "800901", 92f, 200f);
            _learnableSkillBox = Num(host, ref y, "可学池", "800901,800903", 92f, 200f);
            _epiphanySkillBox = Num(host, ref y, "领悟候选", "800913", 92f, 200f);
            _epiphanyChanceBox = Num(host, ref y, "领悟率bp", "1000");
            _slotKindDrop = Drop(host, ref y, "清单种类", _slotKindNames, 92f, 140f);
            _conflictBox = Num(host, ref y, "互斥技能", "与上一种类同槽的互斥 id", 92f, 200f);

            Section(host, ref y, "打书试算（只调内核，不写库）");
            _previewSkillBox = Num(host, ref y, "要学的技能", "800007", 92f, 120f);
            _previewSeedBox = Num(host, ref y, "确定性种子", "1", 92f, 90f);
            new Label(2f, y, 420f, 20f)
            {
                Text = "试算结果画在右侧技能格图：蓝=天生 绿=打书 紫=领悟 黄=锁定",
                Parent = host,
            };
            y += RowHeight;
        }

        private void BuildFormBehavior()
        {
            var host = NewForm(3);
            var y = 6f;
            Section(host, ref y, "④ AI 智能档位（档位给默认，能力位只做微调）");

            _intellectDrop = Drop(host, ref y, "智能档位", _intellectNames, 92f, 260f);
            _flagAcquireDrop = Drop(host, ref y, "自主索敌", _triNames, 92f, 110f);
            _flagAutoCastDrop = Drop(host, ref y, "自动施法", _triNames, 92f, 110f);
            _flagEvadeDrop = Drop(host, ref y, "低血避让", _triNames, 92f, 110f);
            _flagGuardDrop = Drop(host, ref y, "护主", _triNames, 92f, 110f);
            _flagHoldDrop = Drop(host, ref y, "保留控制技", _triNames, 92f, 110f);

            Section(host, ref y, "默认姿态与性格");
            _stanceDrop = Drop(host, ref y, "默认姿态", _stanceNames, 92f, 110f);
            _commandDrop = Drop(host, ref y, "默认指令", _commandNames, 92f, 110f);
            _personalityBox = Num(host, ref y, "允许性格", "0勇猛1精明2胆小3谨慎4忠诚", 92f, 200f);

            new Label(2f, y, 460f, 40f)
            {
                Text = "出战位每玩家只有一个（天龙口径）；附体位 3 个且不占出战位，75 级解锁。",
                Parent = host,
            };
            y += 44f;
        }

        private void BuildFormBond()
        {
            var host = NewForm(4);
            var y = 6f;
            Section(host, ref y, "⑤ 默契四轴（忠诚/快乐/寿命/亲密）与孵化");

            _loyaltyBox = Num(host, ref y, "初始忠诚", "0-100", 92f, 72f);
            _happinessBox = Num(host, ref y, "初始快乐", "0-1000", 92f, 72f);
            _lifespanBox = Num(host, ref y, "初始寿命", "点数", 92f, 72f);
            _intimacyBox = Num(host, ref y, "初始亲密", "0", 92f, 72f);
            _happinessScaleBox = Num(host, ref y, "快乐刻度", "1000 或 100", 92f, 72f);

            new Label(2f, y, 440f, 60f)
            {
                Text = "门槛：忠诚≤70 概率拒绝出战；快乐<600 硬拦；寿命<51 硬拦（永生豁免）；宠级>主级+余量 硬拦。"
                       + "\n快乐三档伤害：红75% / 黄100% / 绿125%（分界 334 与 667）。",
                Parent = host,
            };
            y += 64f;
        }

        private void BuildFormEquipAssist()
        {
            var host = NewForm(5);
            var y = 6f;
            Section(host, ref y, "⑥ 装备部位集合（空集合 = 该物种没有装备系统，WoW 档）");

            _equipSlotsBox = Num(host, ref y, "开放部位", "0护腕1项圈2躯干3头4爪5护符6环7铃", 92f, 220f);
            _gemDrop = Drop(host, ref y, "允许灵石", _yesNoNames, 92f, 90f);
            _maxStarBox = Num(host, ref y, "最高星级", "5 或 7");
            _setFamilyDrop = Drop(host, ref y, "套装族", new[] { "无", "按性格", "按部位" }, 92f, 120f);
            _appraisalDrop = Drop(host, ref y, "资质鉴定", _yesNoNames, 92f, 90f);
            _awakeningDrop = Drop(host, ref y, "觉醒格", _yesNoNames, 92f, 90f);
            _awakeningPiecesBox = Num(host, ref y, "觉醒件数", "3");
            _ownershipDrop = Drop(host, ref y, "装备归属", new[] { "绑宠物", "绑角色可跨宠" }, 92f, 160f);

            Section(host, ref y, "辅助（反哺主人）");
            _assistSkillBox = Num(host, ref y, "辅助技能", "800901,800913", 92f, 200f);
            _assistKindBox = Num(host, ref y, "类型码", "0力量1灵气2体力3身法4定力5生命6回复7替挡8吸敌9转化",
                92f, 220f);
            _assistLevelsBox = Num(host, ref y, "技能等级", "5,8（与技能同序）", 92f, 160f);
        }

        private void BuildFormMergeMutation()
        {
            var host = NewForm(6);
            var y = 6f;
            Section(host, ref y, "⑦ 合成（炼妖）与合体（繁殖）");

            _mergeModeDrop = Drop(host, ref y, "试算模式", _mergeModeNames, 92f, 160f);
            _fuseEnabledDrop = Drop(host, ref y, "可炼妖", _yesNoNames, 92f, 90f);
            _breedEnabledDrop = Drop(host, ref y, "可繁殖", _yesNoNames, 92f, 90f);
            _fuseMinLevelBox = Num(host, ref y, "炼妖最低级", "30");
            _fuseSuccessBox = Num(host, ref y, "炼妖成功bp", "6000");
            _fuseQualityBox = Num(host, ref y, "资质带宽", "7000-11000", 92f, 120f);
            _fuseGrowthBox = Num(host, ref y, "成长带宽", "9600-10200", 92f, 120f);
            _breedGapBox = Num(host, ref y, "繁殖等级差", "5");
            _breedHappinessBox = Num(host, ref y, "繁殖快乐门", "100");
            _breedLifespanBox = Num(host, ref y, "繁殖寿命门", "1000");
            _breedCountBox = Num(host, ref y, "产仔数", "2");
            _breedClaimBox = Num(host, ref y, "领取时限秒", "172800=48h");

            Section(host, ref y, "临时变异（带有效期，到期整体回退）");
            _mutationKeyBox = Num(host, ref y, "变异键", "dye_common", 92f, 160f);
            _mutationDurationBox = Num(host, ref y, "时长tick", "36000=1小时", 92f, 110f);
            _mutationCapBox = Num(host, ref y, "资质上限bp", "10500", 92f, 90f);
        }

        private void BuildFormFollow()
        {
            var host = NewForm(7);
            var y = 6f;
            Section(host, ref y, "⑧ 跟随与自动寻路（数值形状取自 WoW：唯一有 leash 的即时制样本）");

            _followDistanceBox = Num(host, ref y, "跟随距离m", "3");
            _leashBox = Num(host, ref y, "leash半径m", "12");
            _teleportBox = Num(host, ref y, "硬传送m", "40");
            _repathBox = Num(host, ref y, "重算间隔tick", "10");
            _followSpeedBox = Num(host, ref y, "跟随移速", "5.5");
            _arriveBox = Num(host, ref y, "到点距离m", "0.6");
            _corpseBox = Num(host, ref y, "尸体滞留秒", "0=不留");
            _reviveBox = Num(host, ref y, "复活施法秒", "4");
            _summonCdBox = Num(host, ref y, "召唤冷却秒", "0=瞬召");
            _startDelayBox = Num(host, ref y, "起步延迟ms", "300");

            _btnCaptureFollowPoint = new Button(2f, y, 240f, 24f)
            {
                Text = "从场景选点取宠物相对位",
                Parent = host,
            };
            _btnCaptureFollowPoint.Clicked += CaptureFollowOffset;
            y += RowHeight + 6f;

            new Label(2f, y, 460f, 60f)
            {
                Text = "客户端一侧走 NarrativePro 的 NavigatorStatics（IsNavigationReady / TryFindPath / ResamplePath），"
                       + "导航不可用时降级直线；服务端一侧复用 MonsterPathCore 的确定性 A*。"
                       + "跨坐标轴记得 Y/Z 交换。",
                Parent = host,
            };
            y += 64f;
        }

        // ---------- 视图与控制台 ----------

        private void BuildViews()
        {
            _viewHost = new Panel(ScrollBars.None) { Parent = this };

            _radarView = new StatRadarView { Parent = _viewHost };
            _growthView = new GrowthCurveView { Parent = _viewHost };
            _slotView = new SkillSlotView { Parent = _viewHost };
            _followView = new FollowRadiusView { Parent = _viewHost };
            _pedigreeView = new PedigreeView { Parent = _viewHost };
        }

        private void BuildConsole()
        {
            _consolePanel = new Panel(ScrollBars.None) { Parent = this };
            _consoleBox = new TextBox(true, 4f, 4f, 10f)
            {
                Parent = _consolePanel,
                WatermarkText = "回执与控制台",
                IsReadOnly = true,
            };
        }

        private void LayoutNow()
        {
            var width = Math.Max(200f, Width);
            var height = Math.Max(200f, Height);

            var viewWidth = Math.Min(420f, Math.Max(0f, width - LeftWidth - 760f));
            var formWidth = width - LeftWidth - viewWidth - 12f;

            _toolbar.Bounds = new Rectangle(LeftWidth, 0f, width - LeftWidth, ToolbarHeight);
            _tabBar.Bounds = new Rectangle(LeftWidth, ToolbarHeight, formWidth, TabBarHeight);

            var top = ToolbarHeight + TabBarHeight + 2f;
            var bottom = height - ConsoleHeight;
            var midHeight = Math.Max(40f, bottom - top);

            if (_formHost != null)
                _formHost.Bounds = new Rectangle(LeftWidth, top, formWidth, midHeight);
            for (var i = 0; i < _forms.Length; i++)
            {
                if (_forms[i] != null) _forms[i].Bounds = new Rectangle(0f, 0f, formWidth, midHeight);
            }

            if (_viewHost != null && viewWidth > 20f)
            {
                _viewHost.Bounds = new Rectangle(LeftWidth + formWidth + 4f, top, viewWidth, midHeight);
                var slice = (midHeight - 16f) / 5f;
                if (_radarView != null) _radarView.Bounds = new Rectangle(0f, 0f, viewWidth, slice);
                if (_growthView != null) _growthView.Bounds = new Rectangle(0f, slice + 4f, viewWidth, slice);
                if (_slotView != null) _slotView.Bounds = new Rectangle(0f, (slice + 4f) * 2f, viewWidth, slice);
                if (_followView != null) _followView.Bounds = new Rectangle(0f, (slice + 4f) * 3f, viewWidth, slice);
                if (_pedigreeView != null) _pedigreeView.Bounds = new Rectangle(0f, (slice + 4f) * 4f, viewWidth, slice);
            }
            else if (_viewHost != null)
            {
                _viewHost.Bounds = new Rectangle(0f, 0f, 0f, 0f);
            }

            _leftPanel.Bounds = new Rectangle(0f, 0f, LeftWidth, height - ConsoleHeight);
            _consolePanel.Bounds = new Rectangle(0f, height - ConsoleHeight, width, ConsoleHeight);
            if (_consoleBox != null)
                _consoleBox.Bounds = new Rectangle(4f, 4f, width - 8f, ConsoleHeight - 8f);

            for (var i = 0; i < _tabButtons.Length; i++)
            {
                var w = Math.Max(60f, (formWidth - 8f) / _tabButtons.Length);
                _tabButtons[i].Bounds = new Rectangle(2f + i * w, 2f, w - 4f, TabBarHeight - 4f);
            }
        }

        private void SelectTab(int index)
        {
            for (var i = 0; i < _forms.Length; i++)
            {
                if (_forms[i] != null) _forms[i].Visible = i == index;
            }
            RefreshViews();
        }

        // ---------- 草稿 ↔ 界面 ----------

        private void CommitAll()
        {
            var d = _draft;
            d.SpeciesId = Int(_idBox);
            d.Name = Str(_nameBox);
            d.ModelKey = Str(_modelKeyBox);
            d.PrefabPath = Str(_prefabPathBox);
            d.BaseColorKey = Str(_baseColorBox);
            d.StandState = Str(_standBox);
            d.ModelScale = Dec(_scaleBox, 1.0m);
            d.AllowedOrigins = CsvOr(Index(_originDrop), "0,1");
            d.CaptureMode = (byte)Math.Max(0, Index(_captureDrop));
            d.ImmortalLifespan = Index(_immortalDrop) == 1;
            d.CarryLevelMin = Int(_carryMinBox);
            d.CarryLevelMax = Int(_carryMaxBox);
            d.HatchSeconds = Int(_hatchSecondsBox);
            d.HatchLevel = Int(_hatchLevelBox);
            d.OwnerLevelSlack = Int(_ownerSlackBox);
            d.PotentialPerLevel = Int(_potentialBox);
            d.CaptureBaseBps = Int(_captureBaseBox);
            d.CaptureGuidanceSeconds = Int(_captureGuidanceBox);
            d.MutationChanceBps = Int(_mutationChanceBox);

            var s = d.Stats;
            s.AttackMin = Int(_atkMinBox); s.AttackMax = Int(_atkMaxBox);
            s.DefenseMin = Int(_defMinBox); s.DefenseMax = Int(_defMaxBox);
            s.StaminaMin = Int(_staMinBox); s.StaminaMax = Int(_staMaxBox);
            s.ManaMin = Int(_manaMinBox); s.ManaMax = Int(_manaMaxBox);
            s.SpeedMin = Int(_spdMinBox); s.SpeedMax = Int(_spdMaxBox);
            s.GrowthMinBps = Int(_growthMinBox); s.GrowthMaxBps = Int(_growthMaxBox);
            s.ComprehensionMax = Int(_comprehensionMaxBox); s.SpiritMax = Int(_spiritMaxBox);
            s.SuperSpirit = Index(_superSpiritDrop) == 1;
            s.MutationTierMax = Int(_mutationTierMaxBox);

            var r = d.SkillRule;
            r.BaseSlots = Int(_baseSlotsBox); r.AbsoluteMaxSlots = Int(_maxSlotsBox);
            r.SlotsByGrade = Str(_slotsByGradeBox);
            r.ComprehensionUnlockA = Int(_unlockABox); r.ComprehensionUnlockB = Int(_unlockBBox);
            r.SlotGrowMaxLevel = Int(_growLevelBox); r.SlotGrowMaxExisting = Int(_growExistingBox);
            r.SlotGrowChanceBps = Int(_growChanceBox); r.OverwriteChanceBps = Int(_overwriteBox);
            r.MaxLockableSlots = Int(_lockableBox); r.LearnedPoolMax = Int(_poolBox);
            r.EquippedMax = Int(_equippedBox); r.InnerCoreSlots = Int(_innerCoreBox);

            RebuildSkillEntries(d);

            var b = d.Behavior;
            b.IntellectTier = (byte)Math.Max(0, Index(_intellectDrop));
            b.FlagUseAcquire = Tri(_flagAcquireDrop);
            b.FlagUseAutoCast = Tri(_flagAutoCastDrop);
            b.FlagUseEvade = Tri(_flagEvadeDrop);
            b.FlagUseGuard = Tri(_flagGuardDrop);
            b.FlagUseHold = Tri(_flagHoldDrop);
            b.DefaultStance = (byte)Math.Max(0, Index(_stanceDrop));
            b.DefaultCommand = (byte)Math.Max(0, Index(_commandDrop));
            b.AllowedPersonalities = Str(_personalityBox);
            b.EpiphanyChanceBps = Int(_epiphanyChanceBox);
            b.InitialLoyalty = Int(_loyaltyBox);
            b.InitialHappiness = Int(_happinessBox);
            b.InitialLifespan = Int(_lifespanBox);
            b.InitialIntimacy = Int(_intimacyBox);
            b.FollowDistance = Flt(_followDistanceBox, 3f);
            b.LeashRadius = Flt(_leashBox, 12f);
            b.TeleportBeyondRadius = Flt(_teleportBox, 40f);
            b.RepathIntervalTicks = Int(_repathBox);
            b.FollowSpeed = Flt(_followSpeedBox, 5.5f);
            b.ArriveEpsilon = Flt(_arriveBox, 0.6f);
            b.CorpseSeconds = Int(_corpseBox);
            b.ReviveCastSeconds = Int(_reviveBox);
            b.SummonCooldownSeconds = Int(_summonCdBox);
            b.FollowStartDelayMs = Int(_startDelayBox);

            d.HappinessScale = Int(_happinessScaleBox);

            var e = d.Equip;
            e.AllowedSlots = Str(_equipSlotsBox);
            e.GemAllowed = Index(_gemDrop) == 1;
            e.MaxStar = Int(_maxStarBox);
            e.SetFamily = (byte)Math.Max(0, Index(_setFamilyDrop));
            e.AppraisalEnabled = Index(_appraisalDrop) == 1;
            e.AwakeningEnabled = Index(_awakeningDrop) == 1;
            e.AwakeningPieceCount = Int(_awakeningPiecesBox);
            e.Ownership = (byte)Math.Max(0, Index(_ownershipDrop));

            RebuildAssistEntries(d);

            var m = d.Merge;
            m.FuseEnabled = Index(_fuseEnabledDrop) == 1;
            m.BreedEnabled = Index(_breedEnabledDrop) == 1;
            m.FuseMinLevel = Int(_fuseMinLevelBox);
            m.FuseSuccessBps = Int(_fuseSuccessBox);
            // out 只能接字段/局部变量，不能接属性：先落到局部再赋回去。
            ParseBand(Str(_fuseQualityBox), out var qMin, out var qMax);
            m.FuseQualityMinBps = qMin;
            m.FuseQualityMaxBps = qMax;
            ParseBand(Str(_fuseGrowthBox), out var gMin, out var gMax);
            m.FuseGrowthMinBps = gMin;
            m.FuseGrowthMaxBps = gMax;
            m.BreedMaxLevelGap = Int(_breedGapBox);
            m.BreedRequiredHappiness = Int(_breedHappinessBox);
            m.BreedRequiredLifespan = Int(_breedLifespanBox);
            m.BreedOffspringCount = Int(_breedCountBox);
            m.BreedClaimWindowSeconds = Int(_breedClaimBox);

            d.Mutations.Clear();
            if (!string.IsNullOrWhiteSpace(Str(_mutationKeyBox)))
            {
                d.Mutations.Add(new PetMutationEntryModel
                {
                    MutationKey = Str(_mutationKeyBox),
                    DurationTicks = Int(_mutationDurationBox),
                    QualityCapBps = Int(_mutationCapBox),
                    ColorOverride = "gold",
                    Enabled = true,
                });
            }
        }

        private void RebuildSkillEntries(PetDraftModel d)
        {
            var kind = (byte)Math.Max(0, Index(_slotKindDrop));
            var ids = ParseIds(KindBoxValue(kind));
            var conflicts = Str(_conflictBox);

            // 只重建"当前下拉选中的那一种类"的条目，其余种类保持不动：
            // 否则在三类之间来回切会因为每次都覆盖而丢掉没在编辑的那两类。
            d.SkillEntries.RemoveAll(x => x.Kind == kind);
            for (var i = 0; i < ids.Count; i++)
            {
                d.SkillEntries.Add(new PetSkillEntryModel
                {
                    SkillId = ids[i],
                    Kind = kind,
                    InnateSlot = kind == 1 ? i : -1,
                    Conflicts = conflicts,
                    Seq = d.SkillEntries.Count + 1,
                });
            }
        }

        private string KindBoxValue(byte kind)
        {
            switch (kind)
            {
                case 1: return Str(_innateSkillBox);
                case 2: return Str(_epiphanySkillBox);
                default: return Str(_learnableSkillBox);
            }
        }

        private void RebuildAssistEntries(PetDraftModel d)
        {
            d.AssistEntries.Clear();
            var skills = ParseIds(Str(_assistSkillBox));
            var kinds = ParseIds(Str(_assistKindBox));
            var levels = ParseIds(Str(_assistLevelsBox));
            for (var i = 0; i < skills.Count; i++)
            {
                d.AssistEntries.Add(new PetAssistEntryModel
                {
                    SkillId = skills[i],
                    AssistKind = i < kinds.Count ? (byte)kinds[i] : (byte)0,
                    // 等级不进草稿（它是个体属性），这里只保证条目顺序与技能同序
                    Seq = levels.Count > i ? levels[i] : i + 1,
                    RequiresBattle = true,
                    Possessable = true,
                });
            }
        }

        private void ApplyDraftToUi()
        {
            if (_idBox == null) return;
            var d = _draft;
            _idBox.Text = d.SpeciesId.ToString(CultureInfo.InvariantCulture);
            _nameBox.Text = d.Name;
            _modelKeyBox.Text = d.ModelKey;
            _prefabPathBox.Text = d.PrefabPath ?? string.Empty;
            _baseColorBox.Text = d.BaseColorKey;
            _standBox.Text = d.StandState;
            _scaleBox.Text = d.ModelScale.ToString("0.00", CultureInfo.InvariantCulture);
            SetIndex(_originDrop, FirstOrigin(d.AllowedOrigins));
            SetIndex(_captureDrop, d.CaptureMode);
            SetIndex(_immortalDrop, d.ImmortalLifespan ? 1 : 0);
            _carryMinBox.Text = d.CarryLevelMin.ToString(CultureInfo.InvariantCulture);
            _carryMaxBox.Text = d.CarryLevelMax.ToString(CultureInfo.InvariantCulture);
            _hatchSecondsBox.Text = d.HatchSeconds.ToString(CultureInfo.InvariantCulture);
            _hatchLevelBox.Text = d.HatchLevel.ToString(CultureInfo.InvariantCulture);
            _ownerSlackBox.Text = d.OwnerLevelSlack.ToString(CultureInfo.InvariantCulture);
            _potentialBox.Text = d.PotentialPerLevel.ToString(CultureInfo.InvariantCulture);
            _captureBaseBox.Text = d.CaptureBaseBps.ToString(CultureInfo.InvariantCulture);
            _captureGuidanceBox.Text = d.CaptureGuidanceSeconds.ToString(CultureInfo.InvariantCulture);
            _mutationChanceBox.Text = d.MutationChanceBps.ToString(CultureInfo.InvariantCulture);
            _versionLabel.Text = string.Format("v{0}  状态：{1}", d.ContentVersion, PublishLabel(d.PublishState));

            var s = d.Stats;
            _atkMinBox.Text = Num(s.AttackMin); _atkMaxBox.Text = Num(s.AttackMax);
            _defMinBox.Text = Num(s.DefenseMin); _defMaxBox.Text = Num(s.DefenseMax);
            _staMinBox.Text = Num(s.StaminaMin); _staMaxBox.Text = Num(s.StaminaMax);
            _manaMinBox.Text = Num(s.ManaMin); _manaMaxBox.Text = Num(s.ManaMax);
            _spdMinBox.Text = Num(s.SpeedMin); _spdMaxBox.Text = Num(s.SpeedMax);
            _growthMinBox.Text = Num(s.GrowthMinBps); _growthMaxBox.Text = Num(s.GrowthMaxBps);
            _comprehensionMaxBox.Text = Num(s.ComprehensionMax); _spiritMaxBox.Text = Num(s.SpiritMax);
            SetIndex(_superSpiritDrop, s.SuperSpirit ? 1 : 0);
            _mutationTierMaxBox.Text = Num(s.MutationTierMax);

            var r = d.SkillRule;
            _baseSlotsBox.Text = Num(r.BaseSlots); _maxSlotsBox.Text = Num(r.AbsoluteMaxSlots);
            _slotsByGradeBox.Text = r.SlotsByGrade;
            _unlockABox.Text = Num(r.ComprehensionUnlockA); _unlockBBox.Text = Num(r.ComprehensionUnlockB);
            _growLevelBox.Text = Num(r.SlotGrowMaxLevel); _growExistingBox.Text = Num(r.SlotGrowMaxExisting);
            _growChanceBox.Text = Num(r.SlotGrowChanceBps); _overwriteBox.Text = Num(r.OverwriteChanceBps);
            _lockableBox.Text = Num(r.MaxLockableSlots); _poolBox.Text = Num(r.LearnedPoolMax);
            _equippedBox.Text = Num(r.EquippedMax); _innerCoreBox.Text = Num(r.InnerCoreSlots);
            _innateSkillBox.Text = JoinIds(d.SkillEntries, 1);
            _learnableSkillBox.Text = JoinIds(d.SkillEntries, 0);
            _epiphanySkillBox.Text = JoinIds(d.SkillEntries, 2);
            _epiphanyChanceBox.Text = Num(d.Behavior.EpiphanyChanceBps);

            var b = d.Behavior;
            SetIndex(_intellectDrop, b.IntellectTier);
            SetTri(_flagAcquireDrop, b.FlagUseAcquire);
            SetTri(_flagAutoCastDrop, b.FlagUseAutoCast);
            SetTri(_flagEvadeDrop, b.FlagUseEvade);
            SetTri(_flagGuardDrop, b.FlagUseGuard);
            SetTri(_flagHoldDrop, b.FlagUseHold);
            SetIndex(_stanceDrop, b.DefaultStance);
            SetIndex(_commandDrop, b.DefaultCommand);
            _personalityBox.Text = b.AllowedPersonalities;
            _loyaltyBox.Text = Num(b.InitialLoyalty);
            _happinessBox.Text = Num(b.InitialHappiness);
            _lifespanBox.Text = Num(b.InitialLifespan);
            _intimacyBox.Text = Num(b.InitialIntimacy);
            _happinessScaleBox.Text = Num(d.HappinessScale);
            _followDistanceBox.Text = FltText(b.FollowDistance);
            _leashBox.Text = FltText(b.LeashRadius);
            _teleportBox.Text = FltText(b.TeleportBeyondRadius);
            _repathBox.Text = Num(b.RepathIntervalTicks);
            _followSpeedBox.Text = FltText(b.FollowSpeed);
            _arriveBox.Text = FltText(b.ArriveEpsilon);
            _corpseBox.Text = Num(b.CorpseSeconds);
            _reviveBox.Text = Num(b.ReviveCastSeconds);
            _summonCdBox.Text = Num(b.SummonCooldownSeconds);
            _startDelayBox.Text = Num(b.FollowStartDelayMs);

            var e = d.Equip;
            _equipSlotsBox.Text = e.AllowedSlots;
            SetIndex(_gemDrop, e.GemAllowed ? 1 : 0);
            _maxStarBox.Text = Num(e.MaxStar);
            SetIndex(_setFamilyDrop, e.SetFamily);
            SetIndex(_appraisalDrop, e.AppraisalEnabled ? 1 : 0);
            SetIndex(_awakeningDrop, e.AwakeningEnabled ? 1 : 0);
            _awakeningPiecesBox.Text = Num(e.AwakeningPieceCount);
            SetIndex(_ownershipDrop, e.Ownership);
            _assistSkillBox.Text = JoinAssist(d.AssistEntries);
            _assistKindBox.Text = JoinAssistKinds(d.AssistEntries);

            var m = d.Merge;
            SetIndex(_fuseEnabledDrop, m.FuseEnabled ? 1 : 0);
            SetIndex(_breedEnabledDrop, m.BreedEnabled ? 1 : 0);
            _fuseMinLevelBox.Text = Num(m.FuseMinLevel);
            _fuseSuccessBox.Text = Num(m.FuseSuccessBps);
            _fuseQualityBox.Text = m.FuseQualityMinBps + "-" + m.FuseQualityMaxBps;
            _fuseGrowthBox.Text = m.FuseGrowthMinBps + "-" + m.FuseGrowthMaxBps;
            _breedGapBox.Text = Num(m.BreedMaxLevelGap);
            _breedHappinessBox.Text = Num(m.BreedRequiredHappiness);
            _breedLifespanBox.Text = Num(m.BreedRequiredLifespan);
            _breedCountBox.Text = Num(m.BreedOffspringCount);
            _breedClaimBox.Text = Num(m.BreedClaimWindowSeconds);

            if (d.Mutations.Count > 0)
            {
                _mutationKeyBox.Text = d.Mutations[0].MutationKey;
                _mutationDurationBox.Text = Num(d.Mutations[0].DurationTicks);
                _mutationCapBox.Text = Num(d.Mutations[0].QualityCapBps);
            }

            _rarityLabel.Text = "稀有度：" + RarityName(d.SpeciesId);
            RefreshViews();
        }

        // ---------- 视图刷新 ----------

        private void RefreshViews()
        {
            var d = _draft;
            var s = d.Stats;

            var growth = PetPreviewMath.GrowthBpsOf(Index(_growthTierDrop) >= 0 ? Index(_growthTierDrop) : 2);
            if (growth < s.GrowthMinBps) growth = s.GrowthMinBps;
            if (growth > s.GrowthMaxBps) growth = s.GrowthMaxBps;

            var previewLevel = ClampLevel(Int(_hatchLevelBox));
            var mutationCap = d.Mutations.Count > 0 && d.Mutations[0].QualityCapBps > 0
                ? d.Mutations[0].QualityCapBps
                : PetPreviewMath.GrowthScale;

            if (_radarView != null)
            {
                _radarView.SetData(
                    Mid(s.AttackMin, s.AttackMax), Mid(s.DefenseMin, s.DefenseMax),
                    Mid(s.StaminaMin, s.StaminaMax), Mid(s.ManaMin, s.ManaMax), Mid(s.SpeedMin, s.SpeedMax),
                    s.ComprehensionMax, s.SpiritMax, s.SuperSpirit, mutationCap,
                    s.SuperSpirit ? "超灵档：灵性上限 34%" : null);
            }

            if (_growthView != null)
            {
                var stats = PetPreviewMath.ComputeStats(
                    Mid(s.AttackMin, s.AttackMax), Mid(s.DefenseMin, s.DefenseMax),
                    Mid(s.StaminaMin, s.StaminaMax), Mid(s.ManaMin, s.ManaMax), Mid(s.SpeedMin, s.SpeedMax),
                    previewLevel, growth, 0, 0, 0, 0, 0);
                _growthView.SetData(previewLevel, growth,
                    Mid(s.AttackMin, s.AttackMax), Mid(s.DefenseMin, s.DefenseMax),
                    Mid(s.StaminaMin, s.StaminaMax), Mid(s.ManaMin, s.ManaMax), Mid(s.SpeedMin, s.SpeedMax),
                    0, 0, 0, 0, 0,
                    string.Format("Lv{0} 气血 {1}", previewLevel, stats.Hp));
            }

            if (_followView != null)
            {
                var b = d.Behavior;
                _followView.SetData(b.FollowDistance, b.LeashRadius, b.TeleportBeyondRadius,
                    _followOffsetX, _followOffsetY, FollowStateCode(b, previewLevel), null);
            }
        }

        private float _followOffsetX = 6f;
        private float _followOffsetY;

        /// <summary>把当前草稿的跟随状态折算成视图状态码。</summary>
        private int FollowStateCode(PetBehaviorModel b, int petLevel)
        {
            var dist = (float)Math.Sqrt(_followOffsetX * _followOffsetX + _followOffsetY * _followOffsetY);
            if (dist > b.TeleportBeyondRadius) return 4;
            if (b.DefaultCommand == 2) return 2;
            if (b.DefaultCommand == 0 || b.DefaultCommand == 3) return 1;
            if (dist > b.LeashRadius) return 3;
            return 0;
        }

        /// <summary>从场景当前选中的 Actor 取一个世界坐标，换算成"相对主人"的平面偏移（Y/Z 交换）。</summary>
        private void CaptureFollowOffset()
        {
            var editor = FlaxEditor.Editor.Instance;
            if (editor == null || editor.SceneEditing == null)
            {
                Log("场景编辑不可用。");
                return;
            }
            var selection = editor.SceneEditing.Selection;
            if (selection == null || selection.Count == 0)
            {
                Log("先在场景里选中一个 Actor（它是宠物位置；主人位置请填上一次选的点）");
                return;
            }
            foreach (var node in selection)
            {
                // Selection 的元素是 SceneGraphNode；Actor 只在 ActorNode 上，必须显式转一次。
                var actorNode = node as FlaxEditor.SceneGraph.ActorNode;
                var actor = actorNode?.Actor;
                if (actor == null) continue;
                var t = actor.Transform.Translation;
                // Flax 是 Y-up，服务端水平面是 XY（Z 为高度）：第二轴取 Z。
                _followOffsetX = t.X;
                _followOffsetY = t.Z;
                Log(string.Format("已取点：Flax({0:0.##},{1:0.##},{2:0.##}) → 平面({0:0.##},{2:0.##})",
                    t.X, t.Y, t.Z));
                RefreshViews();
                return;
            }
        }

        // ---------- CLI 动作 ----------

        private void RunCatalog()
        {
            GuardBusy("刷新目录", () =>
            {
                PetCli.RunAsync("catalog", null, result =>
                {
                    if (!result.Ok)
                    {
                        Log("目录失败：" + result.Display);
                        _statusLabel.Text = "目录不可用（" + result.ExitCode + "）";
                        return;
                    }
                    _statusLabel.Text = "目录已装载";
                    Log("目录：" + result.Message);
                    PopulateList(result);
                });
            });
        }

        private void PopulateList(PetCliResult result)
        {
            _listLabels.Clear();
            if (result.HasData && result.Data.ValueKind == JsonValueKind.Object
                && result.Data.TryGetProperty("fallbackCatalog", out var catalog)
                && catalog.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in catalog.EnumerateArray())
                {
                    var id = item.TryGetProperty("speciesId", out var p) ? p.GetInt32() : 0;
                    var name = item.TryGetProperty("name", out var n) ? n.GetString() : string.Empty;
                    _listLabels.Add(id + "｜" + name);
                }
            }
            for (var i = 0; i < _listButtons.Length; i++)
            {
                var has = i < _listLabels.Count;
                _listButtons[i].Visible = has;
                if (has) _listButtons[i].Text = _listLabels[i];
            }
        }

        private void SelectSpeciesRow(int index)
        {
            if (index >= _listLabels.Count) return;
            var label = _listLabels[index];
            var split = label.Split('｜');
            if (split.Length == 0 || !int.TryParse(split[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var speciesId))
                return;
            _idBox.Text = speciesId.ToString(CultureInfo.InvariantCulture);
            Log("选中物种 " + speciesId + "（从库读取请点\"读草稿\"；库里没有时会退回硬编码目录）");
            RunReadDraft();
        }

        private void RunNew()
        {
            var next = NextSpeciesId();
            _draft = PetDraftModel.CreateNew(next);
            ApplyDraftToUi();
            SelectTab(0);
            Log("已新建草稿：物种 " + next + "（号段 " + RarityName(next) + "）");
        }

        private int NextSpeciesId()
        {
            var used = new HashSet<int>();
            foreach (var label in _listLabels)
            {
                var split = label.Split('｜');
                if (int.TryParse(split[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)) used.Add(id);
            }
            var candidate = PetPreviewMath.MinSpeciesId;
            while (candidate <= PetPreviewMath.MaxSpeciesId)
            {
                if (!used.Contains(candidate)) return candidate;
                candidate++;
            }
            return PetPreviewMath.MinSpeciesId;
        }

        private void RunReadDraft()
        {
            var id = _draft.SpeciesId;
            CommitAll();
            id = _draft.SpeciesId;
            GuardBusy("读草稿", () =>
            {
                PetCli.RunAsync("draft get " + id.ToString(CultureInfo.InvariantCulture), null, result =>
                {
                    if (result.Ok && result.HasData
                        && result.Data.TryGetProperty("draftJson", out var json))
                    {
                        var loaded = PetDraftModel.FromJson(json.GetString());
                        if (loaded != null)
                        {
                            _draft = loaded;
                            ApplyDraftToUi();
                            Log("已载入 Redis 草稿：物种 " + loaded.SpeciesId);
                            return;
                        }
                    }
                    // 草稿没有时退到库里已存的行（而不是把空草稿留在界面上让人误以为"没读到"）
                    PetCli.RunAsync("get " + id.ToString(CultureInfo.InvariantCulture), null, fromDb =>
                    {
                        if (fromDb.Ok && fromDb.HasData && fromDb.Data.TryGetProperty("draftJson", out var dbJson))
                        {
                            var fromDbDraft = PetDraftModel.FromJson(dbJson.GetString());
                            if (fromDbDraft != null)
                            {
                                _draft = fromDbDraft;
                                ApplyDraftToUi();
                                Log("已载入库中物种 " + id);
                                return;
                            }
                        }
                        Log("读草稿与库都失败：" + result.Display + " / " + fromDb.Display);
                    });
                });
            });
        }

        private void RunValidate()
        {
            CommitAll();
            GuardBusy("校验", () => PetCli.RunAsync("validate -", _draft.ToJson(), r => Report(r, "校验")));
        }

        private void RunSaveDraft()
        {
            CommitAll();
            GuardBusy("存草稿", () => PetCli.RunAsync("draft save -", _draft.ToJson(), r => Report(r, "存草稿")));
        }

        private void RunSave()
        {
            CommitAll();
            GuardBusy("保存到库", () =>
            {
                PetCli.RunAsync("validate -", _draft.ToJson(), check =>
                {
                    if (!check.Ok)
                    {
                        Report(check, "校验（已挡下保存）");
                        return;
                    }
                    PetCli.RunAsync("save -", _draft.ToJson(), saved =>
                    {
                        Report(saved, "保存");
                        CrossCheckPreview(saved);
                    });
                });
            });
        }

        private void RunSetState(string verb)
        {
            CommitAll();
            var id = _draft.SpeciesId.ToString(CultureInfo.InvariantCulture);
            GuardBusy(verb, () => PetCli.RunAsync(verb + " " + id, null, r =>
            {
                Report(r, verb);
                if (r.Ok) _versionLabel.Text = string.Format("v{0}  状态：{1}", _draft.ContentVersion,
                    verb == "publish" ? "已发布" : "已停用");
            }));
        }

        private void RunDelete()
        {
            CommitAll();
            var id = _draft.SpeciesId.ToString(CultureInfo.InvariantCulture);
            GuardBusy("删除", () => PetCli.RunAsync("delete " + id, null, r =>
            {
                Report(r, "删除");
                Log("提示：个体实例不会被删（宠物永久存在）；若库回执说\"仍有个体存在\"，那是刻意的拦阻。");
            }));
        }

        private void RunPreviewStats()
        {
            CommitAll();
            var s = _draft.Stats;
            var level = ClampLevel(Int(_hatchLevelBox));
            var growth = PetPreviewMath.GrowthBpsOf(Math.Max(0, Index(_growthTierDrop)));
            var args = string.Format(CultureInfo.InvariantCulture,
                "preview-stats --level {0} --growth {1} --comprehension {2} --spirit {3}{4} "
                + "--atk {5} --def {6} --sta {7} --mana {8} --spd {9} --str 0 --con 0 --end 0 --spiPts 0 --agi 0",
                level, growth, s.ComprehensionMax, s.SpiritMax, s.SuperSpirit ? " --superSpirit" : string.Empty,
                Mid(s.AttackMin, s.AttackMax), Mid(s.DefenseMin, s.DefenseMax),
                Mid(s.StaminaMin, s.StaminaMax), Mid(s.ManaMin, s.ManaMax), Mid(s.SpeedMin, s.SpeedMax));

            GuardBusy("试算属性", () => PetCli.RunAsync(args, null, r =>
            {
                Report(r, "属性试算");
                if (r.Ok && r.HasData && r.Data.TryGetProperty("stats", out var stats))
                {
                    var local = PetPreviewMath.ComputeStats(
                        Mid(s.AttackMin, s.AttackMax), Mid(s.DefenseMin, s.DefenseMax),
                        Mid(s.StaminaMin, s.StaminaMax), Mid(s.ManaMin, s.ManaMax), Mid(s.SpeedMin, s.SpeedMax),
                        level, growth, 0, 0, 0, 0, 0);
                    var remoteHp = stats.TryGetProperty("hp", out var hp) ? hp.GetInt32() : -1;
                    if (remoteHp != local.Hp)
                    {
                        Log(string.Format("【报红】本地预览与内核不一致：hp 本地 {0} / 内核 {1}"
                                          + " —— 请把 PetPreviewMath 的表与内核重新对账", local.Hp, remoteHp));
                    }
                    else
                    {
                        Log("本地预览与内核一致（hp=" + remoteHp + "）");
                    }
                }
                RefreshViews();
            }));
        }

        private void RunPreviewLearn()
        {
            CommitAll();
            var skill = Int(_previewSkillBox);
            var seed = Int(_previewSeedBox);
            if (seed <= 0) seed = 1;
            var owned = new List<string>();
            foreach (var entry in _draft.SkillEntries)
            {
                if (entry.SkillId > 0) owned.Add(entry.SkillId.ToString(CultureInfo.InvariantCulture));
            }

            var args = string.Format(CultureInfo.InvariantCulture,
                "preview-learn --skill {0} --seed {1} --level {2} --comprehension {3}{4}",
                skill, seed, ClampLevel(Int(_hatchLevelBox)), _draft.Stats.ComprehensionMax,
                owned.Count > 0 ? " --owned " + string.Join(",", owned) : string.Empty);

            GuardBusy("试算打书", () => PetCli.RunAsync(args, null, r =>
            {
                Report(r, "打书试算");
                if (!r.Ok || !r.HasData) return;
                var slots = new List<PetSkillSlotView>();
                if (r.Data.TryGetProperty("slotsAfter", out var arr) && arr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in arr.EnumerateArray())
                    {
                        var id = item.TryGetProperty("skillId", out var sid) ? sid.GetInt32() : 0;
                        var kindName = item.TryGetProperty("kind", out var k) ? k.GetString() : "Empty";
                        slots.Add(new PetSkillSlotView { SkillId = id, Kind = KindCode(kindName), Opened = true });
                    }
                }
                var openCap = r.Data.TryGetProperty("openCap", out var oc) ? oc.GetInt32() : slots.Count;
                var outcome = r.Data.TryGetProperty("outcome", out var o) ? o.GetString() : string.Empty;
                var roll = r.Data.TryGetProperty("roll", out var rl) ? rl.GetInt32() : 0;
                if (_slotView != null)
                {
                    _slotView.SetData(slots.ToArray(), openCap, _draft.SkillRule.AbsoluteMaxSlots,
                        outcome + "（掷值 " + roll + "）", "结果来自内核 preview-learn，不是本地近似");
                }
            }));
        }

        private void RunPreviewMerge()
        {
            CommitAll();
            var seed = Int(_previewSeedBox);
            if (seed <= 0) seed = 1;
            var mode = Index(_mergeModeDrop) == 1 ? "breed" : "fuse";
            var species = _draft.SpeciesId.ToString(CultureInfo.InvariantCulture);
            var args = string.Format(CultureInfo.InvariantCulture,
                "preview-merge --mode {0} --a {1} --b {1} --seed {2} --level {3}",
                mode, species, seed, Math.Max(PetMergeMinLevel(), 30));

            GuardBusy("试算合成/繁殖", () => PetCli.RunAsync(args, null, r =>
            {
                Report(r, "合成试算");
                if (!r.HasData) return;
                var d = r.Data;
                var count = d.TryGetProperty("offspringCount", out var c) ? c.GetInt32() : 0;
                var claim = d.TryGetProperty("claimWindowSeconds", out var cw) ? cw.GetInt32() : 0;
                var consumed = d.TryGetProperty("parentsConsumed", out var pc) && pc.GetBoolean();
                var inherited = 0;
                if (d.TryGetProperty("inheritedSkillIds", out var ids) && ids.ValueKind == JsonValueKind.Array)
                {
                    foreach (var _ in ids.EnumerateArray()) inherited++;
                }
                var blocked = d.TryGetProperty("blocked", out var bl) ? bl.GetString() : string.Empty;
                if (_pedigreeView != null)
                {
                    _pedigreeView.SetData(
                        mode == "breed" ? "繁殖（合体）：留父母、限时领取" : "炼妖（合成）：吃双亲、即时出",
                        "物种 " + species + " 父本", "物种 " + species + " 母本",
                        count > 0 ? "产出 " + count + " 只" : "未产出（" + blocked + "）",
                        count, inherited, claim, consumed,
                        consumed && count == 0 ? "失败但亲本已消耗——这是真实表现，不是 bug" : null);
                }
            }));
        }

        private int PetMergeMinLevel()
        {
            return _draft.Merge != null && _draft.Merge.FuseMinLevel > 0 ? _draft.Merge.FuseMinLevel : 30;
        }

        private void RunPreviewBonus()
        {
            CommitAll();
            var skills = new List<string>();
            var kinds = new List<string>();
            var levels = new List<string>();
            foreach (var entry in _draft.AssistEntries)
            {
                skills.Add(entry.SkillId.ToString(CultureInfo.InvariantCulture));
                kinds.Add(entry.AssistKind.ToString(CultureInfo.InvariantCulture));
                levels.Add(Math.Max(1, entry.Seq).ToString(CultureInfo.InvariantCulture));
            }
            var s = _draft.Stats;
            var args = string.Format(CultureInfo.InvariantCulture,
                "preview-bonus --level {0} --species {1} --atk {2} --def {3} --sta {4} --mana {5} --spd {6} --hp 1000{7}{8}",
                ClampLevel(Int(_hatchLevelBox)), _draft.SpeciesId,
                Mid(s.AttackMin, s.AttackMax), Mid(s.DefenseMin, s.DefenseMax),
                Mid(s.StaminaMin, s.StaminaMax), Mid(s.ManaMin, s.ManaMax), Mid(s.SpeedMin, s.SpeedMax),
                skills.Count > 0 ? " --skills " + string.Join(",", skills) + " --skillLevels " + string.Join(",", levels) : string.Empty,
                " --possessed");

            GuardBusy("试算反哺", () => PetCli.RunAsync(args, null, r =>
            {
                Report(r, "反哺试算");
                if (!r.HasData) return;
                if (r.Data.TryGetProperty("ownerBonus", out var bonus) && bonus.ValueKind == JsonValueKind.Object)
                {
                    var sb = new StringBuilder("反哺主人：");
                    foreach (var prop in bonus.EnumerateObject())
                    {
                        if (prop.Value.GetInt32() == 0) continue;
                        sb.Append(prop.Name).Append('=').Append(prop.Value.GetInt32()).Append(" ");
                    }
                    Log(sb.ToString().TrimEnd());
                    Log("（附体位给的部分不看技能等级——这是天龙的明文规则，别在本地预览里加上等级项）");
                }
            }));
        }

        private void RunPreviewCapture()
        {
            CommitAll();
            var seed = Int(_previewSeedBox);
            if (seed <= 0) seed = 1;
            var args = string.Format(CultureInfo.InvariantCulture,
                "preview-capture --species {0} --seed {1} --player 50 --targetHpBps 1000 --existing {2}",
                _draft.SpeciesId, seed, _capturedCount);

            GuardBusy("试算捕捉", () => PetCli.RunAsync(args, null, r =>
            {
                Report(r, "捕捉试算");
                if (r.Ok && r.HasData && r.Data.TryGetProperty("instanceId", out var id)
                    && id.GetInt64() != 0L)
                {
                    _capturedCount++;
                    Log(string.Format("个体编号 {0}（这是该物种的第 {1} 只；同物种多只是常态，不是重复数据）",
                        id.GetInt64(), _capturedCount));
                }
                else
                {
                    Log("这次没抓住或前置没过（种子换了就不同——同种子永远同结果）");
                }
            }));
        }

        /// <summary>本会话里"已经存在的同物种个体数"，用来验证重复副本能连续发号。</summary>
        private int _capturedCount;

        private void RunPreviewBond()
        {
            CommitAll();
            var b = _draft.Behavior;
            var args = string.Format(CultureInfo.InvariantCulture,
                "preview-bond --loyalty {0} --happiness {1} --lifespan {2} --intimacy {3} --level {4} --owner {4}",
                b.InitialLoyalty, b.InitialHappiness, b.InitialLifespan, b.InitialIntimacy,
                ClampLevel(Int(_hatchLevelBox)));

            GuardBusy("试算默契", () => PetCli.RunAsync(args, null, r =>
            {
                Report(r, "默契试算");
                if (!r.HasData) return;
                var d = r.Data;
                var deployable = d.TryGetProperty("deployable", out var dep) && dep.GetBoolean();
                var reason = d.TryGetProperty("blockReason", out var br) ? br.GetString() : string.Empty;
                var mood = d.TryGetProperty("mood", out var md) ? md.GetString() : string.Empty;
                var mult = d.TryGetProperty("damageMultiplierBps", out var ml) ? ml.GetInt32() : 10_000;
                var refuse = d.TryGetProperty("refusalChanceBps", out var rf) ? rf.GetInt32() : 0;
                Log(string.Format("出战：{0}{1}｜心情 {2} 伤害 {3}%｜低忠诚拒绝率 {4}%",
                    deployable ? "可" : "不可",
                    deployable ? string.Empty : "（" + reason + "）",
                    mood, mult / 100.0, refuse / 100.0));

                // 与本地预览交叉核对：判据不一致就是两边表漂了
                var localBlock = PetPreviewMath.CheckDeployable(b.InitialLoyalty, b.InitialHappiness,
                    b.InitialLifespan, ClampLevel(Int(_hatchLevelBox)), ClampLevel(Int(_hatchLevelBox)),
                    _draft.OwnerLevelSlack, _draft.ImmortalLifespan, true, true);
                if (localBlock != 0 && deployable)
                    Log("【报红】本地判不可出战（原因码 " + localBlock + "）而内核说可以——门槛表已漂");
            }));
        }

        private void RunExportVisuals()
        {
            var outPath = System.IO.Path.Combine(
                FlaxEngine.Globals.ProjectFolder, "Data", "Pets", "client_visuals.json");
            GuardBusy("导出外观表", () => PetCli.RunAsync("export-visuals --out \"" + outPath + "\"", null, r =>
            {
                Report(r, "导出外观表");
                if (!r.HasData) return;
                if (r.Data.TryGetProperty("problems", out var problems) && problems.ValueKind == JsonValueKind.Array)
                {
                    foreach (var p in problems.EnumerateArray())
                    {
                        Log("问题：" + p.GetString());
                    }
                }
                var count = r.Data.TryGetProperty("count", out var c) ? c.GetInt32() : 0;
                if (count == 0)
                    Log("导出 0 条是如实状态：这些物种还没有 prefab 路径，不是链路坏了。");
            }));
        }

        /// <summary>保存回执里若带试算字段，与本地预览交叉核对一次。</summary>
        private void CrossCheckPreview(PetCliResult saved)
        {
            if (!saved.HasData) return;
            Log("写库回执行数：" + DescribeWrittenRows(saved));
        }

        private static string DescribeWrittenRows(PetCliResult result)
        {
            if (!result.Data.TryGetProperty("writtenRows", out var rows)
                || rows.ValueKind != JsonValueKind.Object) return "（无行数回执）";
            var sb = new StringBuilder();
            foreach (var prop in rows.EnumerateObject())
            {
                sb.Append(prop.Name).Append('=').Append(prop.Value.GetInt32()).Append(" ");
            }
            return sb.ToString().TrimEnd();
        }

        // ---------- 通用 ----------

        private void GuardBusy(string label, Action start)
        {
            if (_busy)
            {
                Log("上一次子进程还没结束，先等它（" + label + " 被忽略）");
                return;
            }
            _busy = true;
            _statusLabel.Text = label + " 中…";
            try
            {
                start();
            }
            catch (Exception ex)
            {
                _busy = false;
                _statusLabel.Text = label + " 异常";
                Log(label + " 抛异常：" + ex.Message);
            }
        }

        private void Report(PetCliResult result, string label)
        {
            _busy = false;
            _statusLabel.Text = label + (result.Ok ? " 成功" : " 失败");
            Log("[" + label + "] " + result.Display);
            if (result.HasData && result.Data.TryGetProperty("errors", out var errors)
                && errors.ValueKind == JsonValueKind.Array)
            {
                List<string> paths = null;
                if (result.Data.TryGetProperty("errorPaths", out var ps) && ps.ValueKind == JsonValueKind.Array)
                {
                    paths = new List<string>();
                    foreach (var p in ps.EnumerateArray()) paths.Add(p.GetString());
                }
                var i = 0;
                foreach (var e in errors.EnumerateArray())
                {
                    var path = paths != null && i < paths.Count ? paths[i] + " → " : string.Empty;
                    Log("  ✗ " + path + e.GetString());
                    i++;
                }
            }
            if (result.HasData && result.Data.TryGetProperty("warnings", out var warnings)
                && warnings.ValueKind == JsonValueKind.Array)
            {
                foreach (var w in warnings.EnumerateArray()) Log("  ! " + w.GetString());
            }
        }

        private void Log(string text)
        {
            if (_consoleBox == null) return;
            _consoleBox.Text = (_consoleBox.Text ?? string.Empty) + Environment.NewLine + text;
        }

        // ---------- 取值助手 ----------

        private static string Str(TextBox box) => box == null ? string.Empty : (box.Text ?? string.Empty).Trim();

        private static int Int(TextBox box)
        {
            if (box == null) return 0;
            return int.TryParse((box.Text ?? string.Empty).Trim(), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var v) ? v : 0;
        }

        private static float Flt(TextBox box, float fallback)
        {
            if (box == null) return fallback;
            return float.TryParse((box.Text ?? string.Empty).Trim(), NumberStyles.Float,
                CultureInfo.InvariantCulture, out var v) ? v : fallback;
        }

        private static decimal Dec(TextBox box, decimal fallback)
        {
            if (box == null) return fallback;
            return decimal.TryParse((box.Text ?? string.Empty).Trim(), NumberStyles.Number,
                CultureInfo.InvariantCulture, out var v) ? v : fallback;
        }

        private static byte? Tri(Dropdown drop)
        {
            var i = Index(drop);
            if (i <= 0) return null;
            return (byte)(i - 1);
        }

        private static void SetTri(Dropdown drop, byte? value)
        {
            if (drop == null) return;
            drop.SelectedIndex = value.HasValue ? value.Value + 1 : 0;
        }

        private static int Index(Dropdown drop)
        {
            if (drop == null) return -1;
            var i = drop.SelectedIndex;
            return i < 0 ? 0 : i;
        }

        private static void SetIndex(Dropdown drop, int index)
        {
            if (drop == null) return;
            drop.SelectedIndex = index < 0 ? 0 : index;
        }

        private static string Num(int value) => value.ToString(CultureInfo.InvariantCulture);

        private static string FltText(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);

        private static int Mid(int min, int max) => max >= min ? (min + max) / 2 : min;

        private static int ClampLevel(int level)
        {
            if (level < 1) return 1;
            if (level > PetPreviewMath.MaxPetLevel) return PetPreviewMath.MaxPetLevel;
            return level;
        }

        private static string CsvOr(int value, string fallback)
        {
            if (value < 0) return fallback;
            return value.ToString(CultureInfo.InvariantCulture);
        }

        private static int FirstOrigin(string csv)
        {
            var ids = ParseIds(csv);
            return ids.Count > 0 ? Math.Max(0, Math.Min(5, ids[0])) : 0;
        }

        private static void ParseBand(string text, out int min, out int max)
        {
            min = 0;
            max = 0;
            if (string.IsNullOrWhiteSpace(text)) return;
            var parts = text.Split(new[] { '-', ',', '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return;
            int.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out min);
            if (parts.Length > 1)
                int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out max);
            else max = min;
        }

        private static List<int> ParseIds(string csv)
        {
            var list = new List<int>();
            if (string.IsNullOrWhiteSpace(csv)) return list;
            foreach (var token in csv.Split(new[] { ',', '，', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (int.TryParse(token.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) && v > 0)
                    list.Add(v);
            }
            return list;
        }

        private static string JoinIds(List<PetSkillEntryModel> entries, byte kind)
        {
            var sb = new StringBuilder();
            foreach (var entry in entries)
            {
                if (entry.Kind != kind || entry.SkillId <= 0) continue;
                if (sb.Length > 0) sb.Append(',');
                sb.Append(entry.SkillId.ToString(CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        private static string JoinAssist(List<PetAssistEntryModel> entries)
        {
            var sb = new StringBuilder();
            foreach (var entry in entries)
            {
                if (sb.Length > 0) sb.Append(',');
                sb.Append(entry.SkillId.ToString(CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        private static string JoinAssistKinds(List<PetAssistEntryModel> entries)
        {
            var sb = new StringBuilder();
            foreach (var entry in entries)
            {
                if (sb.Length > 0) sb.Append(',');
                sb.Append(entry.AssistKind.ToString(CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        private static int KindCode(string kindName)
        {
            switch (kindName)
            {
                case "Innate": return 1;
                case "Epiphany": return 3;
                case "Locked": return 4;
                case "Learned": return 2;
                default: return 0;
            }
        }

        private static string RarityName(int speciesId)
        {
            var tier = PetPreviewMath.RarityTierOf(speciesId);
            if (tier < 0 || tier >= _rarityNamesStatic.Length) return "未知";
            return _rarityNamesStatic[tier];
        }

        private static readonly string[] _rarityNamesStatic =
            { "未知（号段非法）", "普通", "变异/稀有", "神兽", "活动限定" };

        private static string PublishLabel(byte state)
        {
            switch (state)
            {
                case 1: return "已发布";
                case 2: return "已停用";
                default: return "草稿";
            }
        }
    }
}
