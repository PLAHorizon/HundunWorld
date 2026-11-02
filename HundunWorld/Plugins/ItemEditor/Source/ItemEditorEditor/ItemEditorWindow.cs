using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;
using FlaxEngine;
using FlaxEngine.GUI;
using FlaxEditor;
using FlaxEditor.Windows;

namespace ItemEditor
{
    /// <summary>
    /// 物品编辑器主窗口：左侧目录、顶部工具栏、12 个页签、右侧预览视图、底部控制台。
    /// <para>骨架逐文件镜像 <c>PetEditorWindow</c>；语义差异只在"物品有哪些字段"。</para>
    /// </summary>
    public sealed class ItemEditorWindow : EditorWindow
    {
        private const float ToolbarHeight = 88f;
        private const float LeftWidth = 250f;
        private const float ConsoleHeight = 130f;
        private const float TabBarHeight = 26f;
        private const float RowHeight = 26f;
        private const int MaxListRows = 80;

        private static ItemEditorWindow _instance;

        private static readonly string[] TabTitles =
        {
            "基础", "品质", "需求", "属性", "战斗", "经济",
            "图标", "使用", "插槽", "词缀", "套装", "出处",
        };

        private readonly Button[] _listButtons = new Button[MaxListRows];
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

        private Button _btnDetect;
        private Button _btnRefresh;
        private Button _btnNew;
        private Button _btnRead;
        private Button _btnValidate;
        private Button _btnSaveDraft;
        private Button _btnSave;
        private Button _btnPublish;
        private Button _btnRetire;
        private Button _btnDelete;
        private Button _btnTooltip;
        private Button _btnBudget;
        private Button _btnAffix;
        private Button _btnExportClient;
        private Button _btnReferences;

        // 页签 0：基础
        private TextBox _idBox, _nameBox, _descBox, _itemLevelBox, _maxStackBox, _validDaysBox, _durationSecondsBox;
        private TextBox _subTypeBox, _flagsBox;
        private Dropdown _classDrop, _bindDrop, _uniqueDrop, _tradeDrop, _destroyDrop;

        // 页签 1：品质
        private Dropdown _qualityDrop, _rarityDrop, _elementDrop;
        private Label _qualityHint;

        // 页签 2：需求
        private TextBox _levelRequireBox, _realmRequireBox, _allowClassBox, _allowRaceBox;
        private TextBox _requiredSkillBox, _requiredSkillRankBox, _professionLimitBox;

        // 页签 3：属性
        private TextBox _statTypesBox, _statValuesBox;
        private Label _budgetLabel;
        private Button _btnFillStats;

        // 页签 4：战斗
        private Dropdown _inventoryDrop, _resistElementDrop;
        private TextBox _armorBox, _blockBox, _resistValueBox, _delayBox, _durabilityBox;
        private TextBox _dmgTypesBox, _dmgMinsBox, _dmgMaxsBox;
        private Label _dpsLabel;
        private Button _btnFillArmor;

        // 页签 5：经济
        private TextBox _buyPriceBox, _sellPriceBox, _maxCountBox, _containerSlotsBox;
        private TextBox _minMoneyBox, _maxMoneyBox, _foodTypeBox, _disenchantBox, _bagFamilyBox;
        private Button _btnDerivePrice;

        // 页签 6：图标
        private TextBox _iconBox, _modelBox, _scriptBox, _remarkBox, _useEffectBox;

        // 页签 7：使用效果
        private TextBox _spellIdsBox, _spellTriggersBox, _spellChargesBox, _spellPpmBox, _spellCooldownBox;
        private TextBox _startQuestBox, _pageTextBox, _languageBox;

        // 页签 8：插槽
        private TextBox _socketColorsBox, _socketBonusesBox, _socketBonusBox, _gemPropertiesBox;

        // 页签 9：词缀
        private TextBox _affixIdBox, _affixNameBox, _affixDescBox, _affixMinBox, _affixMaxBox;
        private TextBox _affixStatTypesBox, _affixStatValuesBox;
        private Dropdown _affixKindDrop, _affixStateDrop;

        // 页签 10：套装
        private TextBox _setIdBox, _setNameBox, _setDescBox, _setItemLevelBox;
        private TextBox _tierPiecesBox, _tierSpellBox, _tierDescBox, _tierStatsBox, _wornPiecesBox;
        private Dropdown _setStateDrop;

        // 页签 11：出处
        private Dropdown _sourceTypeDrop;
        private TextBox _sourceTypesBox, _refTypesBox, _refIdsBox, _sourceNotesBox;
        private TextBox _materialGradeBox, _gatherTypeBox, _sourceDetailBox;

        private ItemTooltipView _tooltipView;
        private ItemBudgetCurveView _budgetView;
        private ItemSetTierView _setTierView;

        private ItemDraftModel _draft = ItemDraftModel.CreateNew(10001);
        private bool _busy;
        private DateTime _busySince;
        private int _lastSetPieces;

        public static void ShowWindow()
        {
            var editor = FlaxEditor.Editor.Instance;
            if (_instance != null)
            {
                _instance.FocusOrShow(FlaxEditor.GUI.Docking.DockState.DockRight);
                return;
            }
            _instance = new ItemEditorWindow(editor);
            editor.Windows.Open(_instance);
            _instance.Show(FlaxEditor.GUI.Docking.DockState.DockRight);
        }

        public ItemEditorWindow(FlaxEditor.Editor editor)
            : base(editor, false, ScrollBars.None)
        {
            Title = "物品编辑器";

            BuildToolbar();
            BuildList();
            BuildTabBar();
            BuildFormBase();
            BuildFormQuality();
            BuildFormRequirements();
            BuildFormStats();
            BuildFormCombat();
            BuildFormEconomy();
            BuildFormIcon();
            BuildFormUse();
            BuildFormSockets();
            BuildFormAffix();
            BuildFormSet();
            BuildFormSource();
            BuildViews();
            BuildConsole();

            SelectTab(0);
            SizeChanged += control => LayoutNow();
            LayoutNow();

            ApplyDraftToUi();
            Log("物品编辑器就绪。刷新目录需要已编出 Horizon.ItemCli（dotnet build Horizon.ItemCli/Horizon.ItemCli.csproj）"
                + "且 023 已执行；两者都没到位时，号段/预算/tooltip 仍可用（走本地 ItemPreviewMath）。");
        }

        // ---------- 构造：工具条 ----------

        private void BuildToolbar()
        {
            _toolbar = new Panel(ScrollBars.None) { Parent = this };

            _cliPathBox = new TextBox(false, 6f, 6f, 300f)
            {
                Parent = _toolbar,
                WatermarkText = "Horizon.ItemCli.exe 路径（留空自动探测）",
            };

            // 按钮宽度必须显式给：Flax 的 Button(x,y,0,h) 不会按文字自动撑开，
            // 固定 x + 0 宽会让"刷新目录/保存到库"这类长标签压到下一个按钮上（已实测）。
            var x = 312f;
            _btnDetect = MakeToolButton(ref x, 6f, 44f, "探测", () =>
            {
                var path = ItemCli.DetectExePath();
                if (string.IsNullOrEmpty(path))
                    path = "（没探测到）";
                ItemCli.ExePathOverride = (_cliPathBox.Text ?? string.Empty).Trim();
                ItemCli.ResetCache();
                Log("CLI = " + path);
            });
            _btnRefresh = MakeToolButton(ref x, 6f, 76f, "刷新目录", RunList);
            _btnNew = MakeToolButton(ref x, 6f, 44f, "新建", RunNew);
            _btnRead = MakeToolButton(ref x, 6f, 62f, "读草稿", RunRead);
            _btnValidate = MakeToolButton(ref x, 6f, 44f, "校验", RunValidate);
            _btnSaveDraft = MakeToolButton(ref x, 6f, 62f, "存草稿", RunSaveDraft);
            _btnSave = MakeToolButton(ref x, 6f, 76f, "保存到库", RunSave);
            _btnPublish = MakeToolButton(ref x, 6f, 44f, "发布", () => RunSetState("publish"));
            _btnRetire = MakeToolButton(ref x, 6f, 44f, "停用", () => RunSetState("retire"));
            _btnDelete = MakeToolButton(ref x, 6f, 44f, "删除", RunDelete);

            x = 6f;
            _btnTooltip = MakeToolButton(ref x, 38f, 92f, "试算 Tooltip", RunPreviewTooltip);
            _btnBudget = MakeToolButton(ref x, 38f, 76f, "试算预算", RunPreviewBudget);
            _btnAffix = MakeToolButton(ref x, 38f, 76f, "试算词缀", RunPreviewAffix);
            _btnExportClient = MakeToolButton(ref x, 38f, 104f, "导出客户端表", RunExportClient);
            _btnReferences = MakeToolButton(ref x, 38f, 76f, "反查引用", RunReferences);

            _statusLabel = new Label(6f, 66f, 900f, 18f) { Parent = _toolbar, Text = "未连接" };
        }

        /// <summary>按顺序排按钮：宽度显式给，间距 6px；返回创建好的按钮。</summary>
        private Button MakeToolButton(ref float x, float y, float width, string text, Action action)
        {
            var button = new Button(x, y, width, 24f) { Text = text, Parent = _toolbar };
            button.Clicked += () => action();
            x += width + 6f;
            return button;
        }

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
                button.Clicked += () => SelectListRow(captured);
                _listButtons[i] = button;
            }
        }

        private void BuildTabBar()
        {
            _tabBar = new Panel(ScrollBars.None) { Parent = this };
            for (var i = 0; i < TabTitles.Length; i++)
            {
                var captured = i;
                var button = new Button(2f + i * 74f, 2f, 70f, TabBarHeight - 4f)
                {
                    Text = TabTitles[i],
                    Parent = _tabBar,
                };
                button.Clicked += () => SelectTab(captured);
                _tabButtons[i] = button;
            }
        }

        // ---------- 行助手（全部 TopLeft + 显式推进 y） ----------

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

        private TextBox Wide(Panel host, ref float y, string caption, string watermark,
            float captionWidth = 92f, float boxWidth = 300f)
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
            new Label(2f, y, 420f, 20f) { Text = title, Parent = host };
            y += 24f;
        }

        private void Hint(Panel host, ref float y, string text, float height = 40f)
        {
            new Label(2f, y, 560f, height) { Text = text, Parent = host };
            y += height + 4f;
        }

        private Panel NewForm(int index)
        {
            if (_formHost == null) _formHost = new Panel(ScrollBars.None) { Parent = this };
            var panel = new Panel(ScrollBars.Vertical) { Parent = _formHost };
            _forms[index] = panel;
            return panel;
        }

        // ---------- 十二个面板 ----------

        private void BuildFormBase()
        {
            var host = NewForm(0);
            var y = 6f;
            Section(host, ref y, "① 编号、名称与大类（编号决定号段，见设计文档 §3.2）");

            _idBox = Num(host, ref y, "物品编号", "装备 10001+", 92f, 120f);
            _nameBox = Wide(host, ref y, "名称", "显示名（≤50 字）", 92f, 300f);
            _descBox = Wide(host, ref y, "描述", "风味文本（≤500 字）", 92f, 360f);

            _classDrop = Drop(host, ref y, "大类", BuildClassNames(), 92f, 160f);
            _subTypeBox = Num(host, ref y, "子类型", "0..99（武器/防具各有含义）", 92f, 90f);
            _itemLevelBox = Num(host, ref y, "物品等级", "1..300（预算输入）", 92f, 90f);
            _maxStackBox = Num(host, ref y, "叠加上限", "1..9999", 92f, 90f);
            _uniqueDrop = Drop(host, ref y, "唯一", new[] { "否", "是" }, 92f, 90f);
            _bindDrop = Drop(host, ref y, "绑定", new[] { "不绑定", "拾取绑定", "装备绑定", "使用绑定" }, 92f, 130f);
            _tradeDrop = Drop(host, ref y, "可交易", new[] { "否", "是" }, 92f, 90f);
            _destroyDrop = Drop(host, ref y, "可销毁", new[] { "否", "是" }, 92f, 90f);
            _validDaysBox = Num(host, ref y, "有效期(天)", "0=永久", 92f, 90f);
            _durationSecondsBox = Num(host, ref y, "有效期(秒)", ">0 时优先于天数", 92f, 90f);
            _flagsBox = Num(host, ref y, "行为位", "ItemFlags 位掩码（0=默认）", 92f, 110f);
        }

        private void BuildFormQuality()
        {
            var host = NewForm(1);
            var y = 6f;
            Section(host, ref y, "② 品质与稀有度（两个字段，不要混用）");

            _qualityDrop = Drop(host, ref y, "品质 base_quality", ItemPreviewMath.QualityNames, 110f, 150f);
            _rarityDrop = Drop(host, ref y, "掉落稀有度 rarity",
                new[] { "常见", "少见", "稀少", "罕见", "极其罕见", "绝世珍稀" }, 110f, 150f);
            _elementDrop = Drop(host, ref y, "五行元素", new[] { "金", "木", "水", "火", "土", "无" }, 110f, 120f);
            _qualityHint = new Label(2f, y, 560f, 40f) { Parent = host, Text = "品质色：-" };
            y += 44f;

            Hint(host, ref y, "· 品质决定属性预算系数与名字颜色；掉落稀有度只影响「这有多难掉」，两者语义不同。\n"
                              + "· 老数据里 item_type=4 的外观物品是历史缺陷（材料大类），编辑器只读兼容，新建一律走 910000+。", 56f);
        }

        private void BuildFormRequirements()
        {
            var host = NewForm(2);
            var y = 6f;
            Section(host, ref y, "③ 使用需求与限制（0 = 不限）");

            _levelRequireBox = Num(host, ref y, "等级需求", "0..175", 92f, 90f);
            _realmRequireBox = Num(host, ref y, "境界需求", "0", 92f, 90f);
            _allowClassBox = Num(host, ref y, "职业掩码", "位掩码，0=不限", 92f, 110f);
            _allowRaceBox = Num(host, ref y, "种族掩码", "位掩码，0=不限", 92f, 110f);
            _requiredSkillBox = Num(host, ref y, "所需专业", "0=无", 92f, 90f);
            _requiredSkillRankBox = Num(host, ref y, "专业等级", "0", 92f, 90f);
            _professionLimitBox = Wide(host, ref y, "职业限制CSV", "历史字段（建议改用掩码）", 92f, 220f);
        }

        private void BuildFormStats()
        {
            var host = NewForm(3);
            var y = 6f;
            Section(host, ref y, "④ 属性词条（最多 10 条；类型与数值按下标同序对应）");

            _statTypesBox = Wide(host, ref y, "属性类型", "如 6,7,14（6攻击 7防御 14最大生命）", 92f, 300f);
            _statValuesBox = Wide(host, ref y, "属性数值", "如 20,15,100", 92f, 300f);
            _budgetLabel = new Label(2f, y, 560f, 20f) { Parent = host, Text = "预算：-" };
            y += RowHeight;

            _btnFillStats = new Button(2f, y, 220f, 24f) { Parent = host, Text = "按预算填充（平均分到当前类型）" };
            _btnFillStats.Clicked += FillStatsFromBudget;
            y += RowHeight + 4f;

            Hint(host, ref y, "属性名对照：1力量 2敏捷 3智力 4体质 5幸运 6攻击 7防御 8魔法攻击 9魔法防御 "
                              + "10暴击 11暴伤 12命中 13闪避 14最大生命 15最大魔法 16生命回复 17魔法回复 "
                              + "21..25 五行攻击 26..30 五行防御 31..35 五行穿透 36..40 五行亲和 41精力。", 56f);
        }

        private void BuildFormCombat()
        {
            var host = NewForm(4);
            var y = 6f;
            Section(host, ref y, "⑤ 战斗形态：部位、护甲、伤害段");

            _inventoryDrop = Drop(host, ref y, "装备部位", ItemPreviewMath.InventoryTypeNames, 92f, 150f);
            _armorBox = Num(host, ref y, "护甲", "0", 92f, 90f);
            _blockBox = Num(host, ref y, "格挡", "盾牌用", 92f, 90f);
            _resistElementDrop = Drop(host, ref y, "抗性元素", new[] { "金", "木", "水", "火", "土", "无" }, 92f, 110f);
            _resistValueBox = Num(host, ref y, "抗性值", "0", 92f, 90f);
            _delayBox = Num(host, ref y, "攻击间隔ms", "如 2600", 92f, 90f);
            _durabilityBox = Num(host, ref y, "耐久上限", "0=无耐久", 92f, 90f);

            Section(host, ref y, "伤害段（最多两段：主手/副手）");
            _dmgTypesBox = Wide(host, ref y, "伤害类型", "0物理 1金 2木 3水 4火 5土", 92f, 240f);
            _dmgMinsBox = Wide(host, ref y, "最小伤害", "如 12,8", 92f, 240f);
            _dmgMaxsBox = Wide(host, ref y, "最大伤害", "如 18,12", 92f, 240f);
            _dpsLabel = new Label(2f, y, 420f, 20f) { Parent = host, Text = "DPS：-" };
            y += RowHeight;

            _btnFillArmor = new Button(2f, y, 240f, 24f) { Parent = host, Text = "按物品等级与品质填护甲" };
            _btnFillArmor.Clicked += FillArmorFromBudget;
            y += RowHeight + 4f;
        }

        private void BuildFormEconomy()
        {
            var host = NewForm(5);
            var y = 6f;
            Section(host, ref y, "⑥ 经济与容量");

            _buyPriceBox = Num(host, ref y, "买价(铜)", "1金=10000铜", 92f, 110f);
            _sellPriceBox = Num(host, ref y, "售价(铜)", "默认 = 买价/4", 92f, 110f);
            _btnDerivePrice = new Button(2f, y, 200f, 24f) { Parent = host, Text = "按买价推售价（/4）" };
            _btnDerivePrice.Clicked += () =>
            {
                var buy = Int(_buyPriceBox);
                _sellPriceBox.Text = ItemPreviewMath.DefaultSellPrice(buy).ToString(CultureInfo.InvariantCulture);
            };
            y += RowHeight + 4f;

            _maxCountBox = Num(host, ref y, "持有上限", "0=不限", 92f, 90f);
            _containerSlotsBox = Num(host, ref y, "背包格数", "0..36", 92f, 90f);
            _minMoneyBox = Num(host, ref y, "钱袋下限", "铜", 92f, 90f);
            _maxMoneyBox = Num(host, ref y, "钱袋上限", "铜", 92f, 90f);
            _foodTypeBox = Num(host, ref y, "食物类型", "0=非食物", 92f, 90f);
            _disenchantBox = Num(host, ref y, "分解产出", "0=不可分解（系统未落地）", 92f, 110f);
            _bagFamilyBox = Num(host, ref y, "背包族", "位掩码，0=通用", 92f, 110f);
        }

        private void BuildFormIcon()
        {
            var host = NewForm(6);
            var y = 6f;
            Section(host, ref y, "⑦ 图标、模型与脚本钩子");

            _iconBox = Wide(host, ref y, "图标路径", "Content/Icons/...", 92f, 360f);
            _modelBox = Wide(host, ref y, "模型路径", "Content/...（可空）", 92f, 360f);
            _scriptBox = Wide(host, ref y, "脚本名", "按名字派发（本作不加载程序集）", 92f, 240f);
            _remarkBox = Wide(host, ref y, "策划备注", "≤200 字", 92f, 360f);
            _useEffectBox = Wide(host, ref y, "使用效果JSON", "{\"hp\":300} / {\"mp\":200}（消耗品回血/回气）", 92f, 360f);

            Hint(host, ref y, "图标路径是客户端 ItemCatalogOverlay 读取的值；导出客户端表后才生效。"
                              + "未导出时客户端仍走硬编码目录（Glyph 字根）。", 44f);
        }

        private void BuildFormUse()
        {
            var host = NewForm(7);
            var y = 6f;
            Section(host, ref y, "⑧ 使用 / 装备 / 命中触发（最多 5 行，按下标同序）");

            _spellIdsBox = Wide(host, ref y, "法术 id", "如 800901,800913", 92f, 260f);
            _spellTriggersBox = Wide(host, ref y, "触发方式", "0使用 1装备 2命中 3灵魂石 4无延迟 5学习", 92f, 300f);
            _spellChargesBox = Wide(host, ref y, "充能", "-1=无限", 92f, 200f);
            _spellPpmBox = Wide(host, ref y, "PPM×100", "命中触发用", 92f, 200f);
            _spellCooldownBox = Wide(host, ref y, "冷却ms", "如 60000", 92f, 200f);
            _startQuestBox = Num(host, ref y, "开启任务", "使用后接任务（0=无）", 92f, 110f);
            _pageTextBox = Num(host, ref y, "书页文本", "page_text id（0=无）", 92f, 110f);
            _languageBox = Num(host, ref y, "语言 id", "0=通用", 92f, 90f);

            Hint(host, ref y, "legacy：库里的 use_effect JSON 不在这里编辑（本编辑器不改它）。"
                              + "消耗品的效果请通过法术触发表达，保证客户端/服务端读同一条。", 44f);
        }

        private void BuildFormSockets()
        {
            var host = NewForm(8);
            var y = 6f;
            Section(host, ref y, "⑨ 固定插槽（与 gem_slot_rates 随机孔并存）");

            _socketColorsBox = Wide(host, ref y, "槽颜色CSV", "0多彩 1红 2黄 3蓝 4无色（如 1,2,3）", 92f, 300f);
            _socketBonusesBox = Wide(host, ref y, "逐槽奖励CSV", "与颜色同序（如 0,0,0）", 92f, 260f);
            _socketBonusBox = Num(host, ref y, "插槽奖励", "整件奖励 id", 92f, 90f);
            _gemPropertiesBox = Num(host, ref y, "宝石属性", "GemProperties id", 92f, 90f);

            Hint(host, ref y, "配对规则（内核 ItemCore.CanSocket）：多彩只收多彩；普通槽收同色或无色；无色槽收任意。", 30f);
        }

        private void BuildFormAffix()
        {
            var host = NewForm(9);
            var y = 6f;
            Section(host, ref y, "⑩ 随机词缀（前缀/后缀，保存时一并写入词缀表）");

            _affixIdBox = Num(host, ref y, "词缀 id", "900001+", 92f, 100f);
            _affixKindDrop = Drop(host, ref y, "类型", new[] { "前缀", "后缀" }, 92f, 100f);
            _affixNameBox = Wide(host, ref y, "词缀名", "如 锋锐的", 92f, 200f);
            _affixDescBox = Wide(host, ref y, "说明", "≤200 字", 92f, 300f);
            _affixMinBox = Num(host, ref y, "roll 下界", "80 (=0.8)", 92f, 90f);
            _affixMaxBox = Num(host, ref y, "roll 上界", "120 (=1.2)", 92f, 90f);
            _affixStateDrop = Drop(host, ref y, "状态", new[] { "草稿", "已发布", "停用" }, 92f, 110f);
            _affixStatTypesBox = Wide(host, ref y, "词缀属性类型", "如 6,7", 92f, 220f);
            _affixStatValuesBox = Wide(host, ref y, "词缀属性权重", "如 100,100（最终值=权重×roll/100）", 92f, 300f);
        }

        private void BuildFormSet()
        {
            var host = NewForm(10);
            var y = 6f;
            Section(host, ref y, "⑪ 套装（setId>0 时启用；保存时一并写入套装表）");

            _setIdBox = Num(host, ref y, "套装 id", "920000+", 92f, 100f);
            _setNameBox = Wide(host, ref y, "套装名", "如 玄墨战甲", 92f, 240f);
            _setDescBox = Wide(host, ref y, "套装说明", "≤300 字", 92f, 340f);
            _setItemLevelBox = Num(host, ref y, "套装物品等级", "显示用", 92f, 90f);
            _setStateDrop = Drop(host, ref y, "状态", new[] { "草稿", "已发布", "停用" }, 92f, 110f);

            Section(host, ref y, "加成档");
            _tierPiecesBox = Wide(host, ref y, "件数CSV", "如 2,4", 92f, 200f);
            _tierSpellBox = Wide(host, ref y, "法术id CSV", "与件数同序，0=纯属性", 92f, 200f);
            _tierDescBox = Wide(host, ref y, "说明CSV", "用 ; 分隔，如 2件:+20攻击;4件:+40攻击", 92f, 360f);
            _tierStatsBox = Wide(host, ref y, "属性CSV", "格式 件数:类型=值|类型=值;常例 2:6=20;4:6=40", 92f, 380f);
            _wornPiecesBox = Num(host, ref y, "预览已穿件数", "只影响 tooltip/档位图，不写库", 110f, 90f);
        }

        private void BuildFormSource()
        {
            var host = NewForm(11);
            var y = 6f;
            Section(host, ref y, "⑫ 出处（结构化行会同步写 source_detail JSON）");

            _sourceTypeDrop = Drop(host, ref y, "主出处类型",
                new[] { "怪物掉落", "采集", "任务奖励", "副本产出", "商店购买", "合成产出", "活动奖励" }, 100f, 150f);
            _materialGradeBox = Num(host, ref y, "材料品阶", "0..9", 92f, 90f);
            _gatherTypeBox = Num(host, ref y, "采集类型", "0矿物 1草药 2木材 3兽皮 4其他", 92f, 90f);

            _sourceTypesBox = Wide(host, ref y, "行-出处类型", "如 0,4", 92f, 220f);
            _refTypesBox = Wide(host, ref y, "行-引用类型", "0怪 1掉落表 2任务 3NPC 4物品 5地图", 92f, 300f);
            _refIdsBox = Wide(host, ref y, "行-引用id", "如 1001,50001", 92f, 220f);
            _sourceNotesBox = Wide(host, ref y, "行-备注", "用 ; 分隔", 92f, 360f);
            _sourceDetailBox = Wide(host, ref y, "source_detail(只读)", "保存时由结构化行派生", 92f, 360f);
        }

        // ---------- 视图与控制台 ----------

        private void BuildViews()
        {
            _viewHost = new Panel(ScrollBars.None) { Parent = this };

            _tooltipView = new ItemTooltipView { Parent = _viewHost };
            _budgetView = new ItemBudgetCurveView { Parent = _viewHost };
            _setTierView = new ItemSetTierView { Parent = _viewHost };
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

            // 表单区按内容封顶：字段最右到 ~476px，给它 680 已经富余；剩下的都给右侧预览。
            // 之前表单和视图按"视图最多 440"反推，宽窗口下表单会拉出 700px 空白。
            var usable = Math.Max(200f, width - LeftWidth - 12f);
            var formWidth = Math.Max(420f, Math.Min(680f, usable - 320f));
            var viewWidth = usable - formWidth;
            if (viewWidth < 240f)
            {
                formWidth = usable;
                viewWidth = 0f;
            }

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
                // 三段不等高：tooltip 行数少但要留白，预算曲线与套装档位按内容分配。
                var tooltipHeight = midHeight * 0.40f;
                var budgetHeight = midHeight * 0.34f;
                var setHeight = Math.Max(80f, midHeight - tooltipHeight - budgetHeight - 12f);
                if (_tooltipView != null) _tooltipView.Bounds = new Rectangle(0f, 0f, viewWidth, tooltipHeight);
                if (_budgetView != null)
                    _budgetView.Bounds = new Rectangle(0f, tooltipHeight + 6f, viewWidth, budgetHeight);
                if (_setTierView != null)
                    _setTierView.Bounds = new Rectangle(0f, tooltipHeight + budgetHeight + 12f, viewWidth, setHeight);
            }
            else if (_viewHost != null)
            {
                _viewHost.Bounds = new Rectangle(0f, 0f, 0f, 0f);
            }

            _leftPanel.Bounds = new Rectangle(0f, 0f, LeftWidth, height - ConsoleHeight);
            _consolePanel.Bounds = new Rectangle(0f, height - ConsoleHeight, width, ConsoleHeight);
            if (_statusLabel != null)
                _statusLabel.Bounds = new Rectangle(6f, 66f, 420f, 18f);
            if (_consoleBox != null)
                _consoleBox.Bounds = new Rectangle(4f, 4f, width - 8f, ConsoleHeight - 8f);

            for (var i = 0; i < _tabButtons.Length; i++)
            {
                var w = Math.Max(40f, (formWidth - 8f) / _tabButtons.Length);
                _tabButtons[i].Bounds = new Rectangle(2f + i * w, 2f, w - 4f, TabBarHeight - 4f);
            }
        }

        private void SelectTab(int index)
        {
            for (var i = 0; i < _forms.Length; i++)
            {
                if (_forms[i] != null) _forms[i].Visible = i == index;
            }
        }

        // ---------- 草稿 ↔ 界面 ----------

        private void CommitAll()
        {
            var d = _draft;
            d.TemplateId = Int(_idBox);
            d.Name = Str(_nameBox);
            d.Description = Str(_descBox);
            d.ItemClass = Index(_classDrop);
            d.SubType = Int(_subTypeBox);
            d.Flags = Int(_flagsBox);
            d.Quality = Index(_qualityDrop);
            d.Rarity = Index(_rarityDrop);
            d.ItemLevel = Int(_itemLevelBox);
            d.MaxStack = Int(_maxStackBox);
            d.IsUnique = Index(_uniqueDrop) == 1;
            d.BindType = Index(_bindDrop);
            d.CanTrade = Index(_tradeDrop) == 1;
            d.CanDestroy = Index(_destroyDrop) == 1;
            d.ValidDays = Int(_validDaysBox);
            d.DurationSeconds = Int(_durationSecondsBox);
            d.Element = Index(_elementDrop);

            d.LevelRequire = Int(_levelRequireBox);
            d.RealmRequire = Int(_realmRequireBox);
            d.AllowableClass = Int(_allowClassBox);
            d.AllowableRace = Int(_allowRaceBox);
            d.RequiredSkill = Int(_requiredSkillBox);
            d.RequiredSkillRank = Int(_requiredSkillRankBox);
            d.ProfessionLimit = Str(_professionLimitBox);

            d.Stats = ParsePairs(Str(_statTypesBox), Str(_statValuesBox), (t, v) => new ItemStatModel { StatType = t, StatValue = v });

            d.InventoryType = Index(_inventoryDrop);
            d.Armor = Int(_armorBox);
            d.Block = Int(_blockBox);
            d.ResistanceElement = Index(_resistElementDrop);
            d.ResistanceValue = Int(_resistValueBox);
            d.AttackDelayMs = Int(_delayBox);
            d.Durability = Int(_durabilityBox);
            d.Damage = ParseDamage();

            d.BuyPrice = Int(_buyPriceBox);
            d.SellPrice = Int(_sellPriceBox);
            d.MaxCount = Int(_maxCountBox);
            d.ContainerSlots = Int(_containerSlotsBox);
            d.MinMoneyLoot = Int(_minMoneyBox);
            d.MaxMoneyLoot = Int(_maxMoneyBox);
            d.FoodType = Int(_foodTypeBox);
            d.DisenchantId = Int(_disenchantBox);
            d.BagFamily = Int(_bagFamilyBox);

            d.IconPath = Str(_iconBox);
            d.ModelPath = Str(_modelBox);
            d.ScriptName = Str(_scriptBox);
            d.Remark = Str(_remarkBox);
            d.UseEffect = Str(_useEffectBox);

            d.Spells = ParseSpells();
            d.StartQuest = Int(_startQuestBox);
            d.PageText = Int(_pageTextBox);
            d.Language = Int(_languageBox);

            d.Sockets = ParsePairs(Str(_socketColorsBox), Str(_socketBonusesBox),
                (t, v) => new ItemSocketModel { SocketColor = t, SocketBonus = v });
            d.SocketBonus = Int(_socketBonusBox);
            d.GemProperties = Int(_gemPropertiesBox);

            var affixId = Int(_affixIdBox);
            d.Affix = new ItemAffixModel
            {
                AffixId = affixId,
                Kind = Index(_affixKindDrop),
                Name = Str(_affixNameBox),
                Description = Str(_affixDescBox),
                ValueMin = Int(_affixMinBox),
                ValueMax = Int(_affixMaxBox),
                PublishState = (byte)Index(_affixStateDrop),
                Stats = ParsePairs(Str(_affixStatTypesBox), Str(_affixStatValuesBox),
                    (t, v) => new ItemStatModel { StatType = t, StatValue = v }),
            };
            // 主表只存一个引用：前缀进 random_property，后缀进 random_suffix。
            d.RandomProperty = affixId > 0 && d.Affix.Kind == 0 ? affixId : 0;
            d.RandomSuffix = affixId > 0 && d.Affix.Kind == 1 ? affixId : 0;

            d.SetId = Int(_setIdBox);
            if (d.SetId > 0)
            {
                d.Set = new ItemSetModel
                {
                    SetId = d.SetId,
                    Name = Str(_setNameBox),
                    Description = Str(_setDescBox),
                    ItemLevel = Int(_setItemLevelBox),
                    PublishState = (byte)Index(_setStateDrop),
                    Tiers = ParseTiers(),
                };
            }
            else
            {
                d.Set = new ItemSetModel { SetId = 0 };
            }

            d.SourceType = Index(_sourceTypeDrop);
            d.MaterialGrade = Int(_materialGradeBox);
            d.GatherType = Int(_gatherTypeBox);
            d.Sources = ParseSources();
            d.SourceDetail = Str(_sourceDetailBox);

            // 预览专用值：不进草稿协议（它描述"玩家身上穿了几件"，不是物品属性）。
            _lastSetPieces = Int(_wornPiecesBox);
        }

        private List<T> ParsePairs<T>(string typesCsv, string valuesCsv, Func<int, int, T> factory)
        {
            var result = new List<T>();
            var types = ParseIds(typesCsv);
            var values = ParseInts(valuesCsv);
            for (var i = 0; i < types.Count && i < values.Count; i++)
            {
                if (types[i] <= 0 && values[i] == 0) continue;
                result.Add(factory(types[i], values[i]));
            }
            return result;
        }

        private List<ItemDamageModel> ParseDamage()
        {
            var result = new List<ItemDamageModel>();
            var types = ParseInts(Str(_dmgTypesBox));
            var mins = ParseInts(Str(_dmgMinsBox));
            var maxs = ParseInts(Str(_dmgMaxsBox));
            var count = Math.Max(types.Count, Math.Max(mins.Count, maxs.Count));
            for (var i = 0; i < count; i++)
            {
                var min = i < mins.Count ? mins[i] : 0;
                var max = i < maxs.Count ? maxs[i] : 0;
                if (min == 0 && max == 0) continue;
                result.Add(new ItemDamageModel
                {
                    DamageType = i < types.Count ? types[i] : 0,
                    DamageMin = min,
                    DamageMax = max,
                });
            }
            return result;
        }

        private List<ItemSpellModel> ParseSpells()
        {
            var result = new List<ItemSpellModel>();
            var ids = ParseInts(Str(_spellIdsBox));
            var triggers = ParseInts(Str(_spellTriggersBox));
            var charges = ParseInts(Str(_spellChargesBox));
            var ppms = ParseInts(Str(_spellPpmBox));
            var cooldowns = ParseInts(Str(_spellCooldownBox));
            for (var i = 0; i < ids.Count; i++)
            {
                if (ids[i] <= 0) continue;
                result.Add(new ItemSpellModel
                {
                    SpellId = ids[i],
                    SpellTrigger = i < triggers.Count ? triggers[i] : 0,
                    Charges = i < charges.Count ? charges[i] : 0,
                    PpmRate = i < ppms.Count ? ppms[i] : 0,
                    CooldownMs = i < cooldowns.Count ? cooldowns[i] : 0,
                });
            }
            return result;
        }

        /// <summary>套装档位解析：件数/法术/说明三列 CSV + 属性列 <c>件数:类型=值|类型=值;…</c>。</summary>
        private List<ItemSetTierModel> ParseTiers()
        {
            var tiers = new List<ItemSetTierModel>();
            var pieces = ParseInts(Str(_tierPiecesBox));
            var spells = ParseInts(Str(_tierSpellBox));
            var descriptions = Str(_tierDescBox).Split(new[] { ';', '；' }, StringSplitOptions.None);
            var statsByPieces = ParseTierStats(Str(_tierStatsBox));

            for (var i = 0; i < pieces.Count; i++)
            {
                if (pieces[i] <= 0) continue;
                tiers.Add(new ItemSetTierModel
                {
                    Pieces = pieces[i],
                    SpellId = i < spells.Count ? spells[i] : 0,
                    Description = i < descriptions.Length ? descriptions[i].Trim() : string.Empty,
                    Stats = statsByPieces.TryGetValue(pieces[i], out var stats) ? stats : new List<ItemStatModel>(),
                });
            }
            return tiers;
        }

        private static Dictionary<int, List<ItemStatModel>> ParseTierStats(string text)
        {
            var result = new Dictionary<int, List<ItemStatModel>>();
            if (string.IsNullOrWhiteSpace(text)) return result;
            foreach (var chunk in text.Split(new[] { ';', '；' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var colon = chunk.IndexOf(':');
                if (colon <= 0) continue;
                if (!int.TryParse(chunk.Substring(0, colon).Trim(), NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out var pieces)) continue;
                var list = new List<ItemStatModel>();
                foreach (var pair in chunk.Substring(colon + 1).Split(new[] { '|', ',' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var eq = pair.IndexOf('=');
                    if (eq <= 0) continue;
                    if (!int.TryParse(pair.Substring(0, eq).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var type)) continue;
                    if (!int.TryParse(pair.Substring(eq + 1).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)) continue;
                    list.Add(new ItemStatModel { StatType = type, StatValue = value });
                }
                result[pieces] = list;
            }
            return result;
        }

        private List<ItemSourceModel> ParseSources()
        {
            var result = new List<ItemSourceModel>();
            var types = ParseInts(Str(_sourceTypesBox));
            var refTypes = ParseInts(Str(_refTypesBox));
            var refIds = ParseInts(Str(_refIdsBox));
            var notes = Str(_sourceNotesBox).Split(new[] { ';', '；' }, StringSplitOptions.None);
            for (var i = 0; i < refIds.Count; i++)
            {
                if (refIds[i] <= 0) continue;
                result.Add(new ItemSourceModel
                {
                    SourceType = i < types.Count ? types[i] : Index(_sourceTypeDrop),
                    RefType = i < refTypes.Count ? refTypes[i] : 0,
                    RefId = refIds[i],
                    Note = i < notes.Length ? notes[i].Trim() : string.Empty,
                });
            }
            return result;
        }

        private void ApplyDraftToUi()
        {
            if (_idBox == null) return;
            var d = _draft;
            _idBox.Text = d.TemplateId.ToString(CultureInfo.InvariantCulture);
            _nameBox.Text = d.Name;
            _descBox.Text = d.Description;
            SetIndex(_classDrop, d.ItemClass);
            _subTypeBox.Text = Num(d.SubType);
            _flagsBox.Text = Num(d.Flags);
            _itemLevelBox.Text = Num(d.ItemLevel);
            _maxStackBox.Text = Num(d.MaxStack);
            SetIndex(_uniqueDrop, d.IsUnique ? 1 : 0);
            SetIndex(_bindDrop, d.BindType);
            SetIndex(_tradeDrop, d.CanTrade ? 1 : 0);
            SetIndex(_destroyDrop, d.CanDestroy ? 1 : 0);
            _validDaysBox.Text = Num(d.ValidDays);
            _durationSecondsBox.Text = Num(d.DurationSeconds);

            SetIndex(_qualityDrop, d.Quality);
            SetIndex(_rarityDrop, d.Rarity);
            SetIndex(_elementDrop, d.Element);

            _levelRequireBox.Text = Num(d.LevelRequire);
            _realmRequireBox.Text = Num(d.RealmRequire);
            _allowClassBox.Text = Num(d.AllowableClass);
            _allowRaceBox.Text = Num(d.AllowableRace);
            _requiredSkillBox.Text = Num(d.RequiredSkill);
            _requiredSkillRankBox.Text = Num(d.RequiredSkillRank);
            _professionLimitBox.Text = d.ProfessionLimit;

            _statTypesBox.Text = JoinTypes(d.Stats);
            _statValuesBox.Text = JoinValues(d.Stats);

            SetIndex(_inventoryDrop, d.InventoryType);
            _armorBox.Text = Num(d.Armor);
            _blockBox.Text = Num(d.Block);
            SetIndex(_resistElementDrop, d.ResistanceElement);
            _resistValueBox.Text = Num(d.ResistanceValue);
            _delayBox.Text = Num(d.AttackDelayMs);
            _durabilityBox.Text = Num(d.Durability);
            _dmgTypesBox.Text = JoinDamage(d.Damage, 0);
            _dmgMinsBox.Text = JoinDamage(d.Damage, 1);
            _dmgMaxsBox.Text = JoinDamage(d.Damage, 2);

            _buyPriceBox.Text = Num(d.BuyPrice);
            _sellPriceBox.Text = Num(d.SellPrice);
            _maxCountBox.Text = Num(d.MaxCount);
            _containerSlotsBox.Text = Num(d.ContainerSlots);
            _minMoneyBox.Text = Num(d.MinMoneyLoot);
            _maxMoneyBox.Text = Num(d.MaxMoneyLoot);
            _foodTypeBox.Text = Num(d.FoodType);
            _disenchantBox.Text = Num(d.DisenchantId);
            _bagFamilyBox.Text = Num(d.BagFamily);

            _iconBox.Text = d.IconPath;
            _modelBox.Text = d.ModelPath;
            _scriptBox.Text = d.ScriptName;
            _remarkBox.Text = d.Remark;
            _useEffectBox.Text = d.UseEffect;

            _spellIdsBox.Text = JoinSpells(d.Spells, 0);
            _spellTriggersBox.Text = JoinSpells(d.Spells, 1);
            _spellChargesBox.Text = JoinSpells(d.Spells, 2);
            _spellPpmBox.Text = JoinSpells(d.Spells, 3);
            _spellCooldownBox.Text = JoinSpells(d.Spells, 4);
            _startQuestBox.Text = Num(d.StartQuest);
            _pageTextBox.Text = Num(d.PageText);
            _languageBox.Text = Num(d.Language);

            _socketColorsBox.Text = string.Join(",", d.Sockets.ConvertAll(s => s.SocketColor.ToString(CultureInfo.InvariantCulture)));
            _socketBonusesBox.Text = string.Join(",", d.Sockets.ConvertAll(s => s.SocketBonus.ToString(CultureInfo.InvariantCulture)));
            _socketBonusBox.Text = Num(d.SocketBonus);
            _gemPropertiesBox.Text = Num(d.GemProperties);

            var affix = d.Affix ?? new ItemAffixModel();
            _affixIdBox.Text = Num(affix.AffixId);
            SetIndex(_affixKindDrop, affix.Kind);
            _affixNameBox.Text = affix.Name;
            _affixDescBox.Text = affix.Description;
            _affixMinBox.Text = Num(affix.ValueMin);
            _affixMaxBox.Text = Num(affix.ValueMax);
            SetIndex(_affixStateDrop, affix.PublishState);
            _affixStatTypesBox.Text = JoinTypes(affix.Stats);
            _affixStatValuesBox.Text = JoinValues(affix.Stats);

            var set = d.Set ?? new ItemSetModel();
            _setIdBox.Text = Num(set.SetId);
            _setNameBox.Text = set.Name;
            _setDescBox.Text = set.Description;
            _setItemLevelBox.Text = Num(set.ItemLevel);
            SetIndex(_setStateDrop, set.PublishState);
            _tierPiecesBox.Text = string.Join(",", set.Tiers.ConvertAll(t => t.Pieces.ToString(CultureInfo.InvariantCulture)));
            _tierSpellBox.Text = string.Join(",", set.Tiers.ConvertAll(t => t.SpellId.ToString(CultureInfo.InvariantCulture)));
            _tierDescBox.Text = string.Join(";", set.Tiers.ConvertAll(t => t.Description));
            _tierStatsBox.Text = JoinTierStats(set.Tiers);
            _wornPiecesBox.Text = Num(_lastSetPieces);

            SetIndex(_sourceTypeDrop, d.SourceType);
            _materialGradeBox.Text = Num(d.MaterialGrade);
            _gatherTypeBox.Text = Num(d.GatherType);
            _sourceTypesBox.Text = string.Join(",", d.Sources.ConvertAll(s => s.SourceType.ToString(CultureInfo.InvariantCulture)));
            _refTypesBox.Text = string.Join(",", d.Sources.ConvertAll(s => s.RefType.ToString(CultureInfo.InvariantCulture)));
            _refIdsBox.Text = string.Join(",", d.Sources.ConvertAll(s => s.RefId.ToString(CultureInfo.InvariantCulture)));
            _sourceNotesBox.Text = string.Join(";", d.Sources.ConvertAll(s => s.Note ?? string.Empty));
            _sourceDetailBox.Text = d.SourceDetail;

            RefreshViews();
        }

        private void RefreshViews()
        {
            var d = _draft;
            var budget = ItemPreviewMath.StatBudget(d.ItemLevel, d.Quality);
            _budgetLabel.Text = string.Format("预算：{0}（ilvl {1}／品质 {2}）", budget, d.ItemLevel,
                ItemPreviewMath.QualityName(d.Quality));
            _qualityHint.Text = string.Format("品质色：{0}　预算系数：{1}‰",
                ItemPreviewMath.QualityColorHex(d.Quality), ItemPreviewMath.QualityMultiplierBps(d.Quality) / 10);

            var dps = d.Damage != null && d.Damage.Count > 0
                ? ItemPreviewMath.WeaponDps(d.Damage[0].DamageMin, d.Damage[0].DamageMax, d.AttackDelayMs)
                : 0;
            _dpsLabel.Text = "DPS：" + dps.ToString(CultureInfo.InvariantCulture);

            if (_tooltipView != null)
            {
                var lines = ItemPreviewMath.BuildTooltip(d, _lastSetPieces, 0);
                _tooltipView.SetData(lines, d.Quality,
                    "本地预览（CLI 可用时点「试算 Tooltip」以内核结果为准）");
            }
            if (_budgetView != null) _budgetView.SetData(Math.Max(1, d.ItemLevel), d.Quality, budget);
            if (_setTierView != null)
            {
                var pieces = new List<int>();
                var labels = new List<string>();
                if (d.Set?.Tiers != null)
                {
                    foreach (var tier in d.Set.Tiers)
                    {
                        pieces.Add(tier.Pieces);
                        labels.Add(tier.Description ?? string.Empty);
                    }
                }
                _setTierView.SetData(pieces, labels, _lastSetPieces);
            }
        }

        // ---------- 按钮：本地填充 ----------

        private void FillStatsFromBudget()
        {
            CommitAll();
            var types = ParseIds(Str(_statTypesBox));
            if (types.Count == 0)
            {
                Log("先在「属性类型」里填至少一个类型（如 6,7）");
                return;
            }
            var budget = ItemPreviewMath.StatBudget(_draft.ItemLevel, _draft.Quality);
            var weights = new int[types.Count];
            for (var i = 0; i < weights.Length; i++) weights[i] = 10_000;
            var distributed = DistributeBudget(budget, types, weights);
            _statTypesBox.Text = string.Join(",", types.ConvertAll(t => t.ToString(CultureInfo.InvariantCulture)));
            _statValuesBox.Text = string.Join(",", Array.ConvertAll(distributed, v => v.ToString(CultureInfo.InvariantCulture)));
            Log(string.Format("已按预算 {0} 平均分配到 {1} 条属性（总和精确等于预算）", budget, types.Count));
            RefreshViews();
        }

        /// <summary>与内核 <c>ItemCore.DistributeBudget</c> 同算法（先各 1 点，余量按权重，余数补第一项）。</summary>
        private static int[] DistributeBudget(int budget, List<int> types, int[] weights)
        {
            if (budget <= 0 || types.Count == 0) return Array.Empty<int>();
            if (budget < types.Count)
            {
                var limited = new int[budget];
                for (var i = 0; i < budget; i++) limited[i] = 1;
                return limited;
            }

            long total = 0;
            for (var i = 0; i < types.Count; i++) total += Math.Max(1, i < weights.Length ? weights[i] : 10_000);

            var remaining = budget - types.Count;
            var result = new int[types.Count];
            var distributed = 0;
            for (var i = 0; i < types.Count; i++)
            {
                var weight = Math.Max(1, i < weights.Length ? weights[i] : 10_000);
                var extra = (int)(remaining * weight / total);
                result[i] = 1 + extra;
                distributed += extra;
            }
            result[0] += remaining - distributed;
            return result;
        }

        private void FillArmorFromBudget()
        {
            CommitAll();
            var budget = ItemPreviewMath.StatBudget(_draft.ItemLevel, _draft.Quality);
            var shield = _draft.SubType == 4 && _draft.ItemClass == 1;
            var armor = ItemPreviewMath.ArmorFromBudget(budget, shield);
            _armorBox.Text = armor.ToString(CultureInfo.InvariantCulture);
            Log(string.Format("按预算 {0} 填护甲 {1}（{2}）", budget, armor, shield ? "盾牌" : "普通防具"));
            RefreshViews();
        }

        // ---------- CLI 动作 ----------

        private void ApplyCliOverride()
        {
            var path = (_cliPathBox.Text ?? string.Empty).Trim();
            if (path != ItemCli.ExePathOverride)
            {
                ItemCli.ExePathOverride = path;
                ItemCli.ResetCache();
            }
        }

        private void RunList()
        {
            ApplyCliOverride();
            GuardBusy("刷新目录", () =>
            {
                ItemCli.RunAsync("list", null, result =>
                {
                    if (!result.Ok)
                    {
                        Log("目录失败：" + result.Display);
                        _statusLabel.Text = "目录不可用（" + result.ExitCode + "）";
                        EndBusy("刷新目录 失败");
                        return;
                    }
                    _statusLabel.Text = "目录已装载";
                    Log("目录：" + result.Message);
                    PopulateList(result);
                    EndBusy("刷新目录 成功");
                });
            });
        }

        private void PopulateList(ItemCliResult result)
        {
            _listLabels.Clear();
            if (result.HasData && result.Data.ValueKind == JsonValueKind.Object
                && result.Data.TryGetProperty("rows", out var rows)
                && rows.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in rows.EnumerateArray())
                {
                    var id = item.TryGetProperty("templateId", out var p) ? p.GetInt32() : 0;
                    var name = item.TryGetProperty("name", out var n) ? n.GetString() : string.Empty;
                    var quality = item.TryGetProperty("quality", out var q) ? q.GetInt32() : 0;
                    _listLabels.Add(id + "｜" + name + "｜" + ItemPreviewMath.QualityName(quality));
                }
            }
            for (var i = 0; i < _listButtons.Length; i++)
            {
                var has = i < _listLabels.Count;
                _listButtons[i].Visible = has;
                if (has) _listButtons[i].Text = _listLabels[i];
            }
        }

        private void SelectListRow(int index)
        {
            if (index >= _listLabels.Count) return;
            var split = _listLabels[index].Split('｜');
            if (split.Length == 0 || !int.TryParse(split[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var templateId))
                return;
            _idBox.Text = templateId.ToString(CultureInfo.InvariantCulture);
            Log("选中物品 " + templateId + "（从库读取请点「读草稿」）");
            RunRead();
        }

        private void RunNew()
        {
            var next = NextTemplateId();
            _draft = ItemDraftModel.CreateNew(next);
            ApplyDraftToUi();
            SelectTab(0);
            Log("已新建草稿：物品 " + next + "（号段 " + SegmentName(next) + "）");
        }

        private int NextTemplateId()
        {
            var used = new HashSet<int>();
            foreach (var label in _listLabels)
            {
                var split = label.Split('｜');
                if (int.TryParse(split[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)) used.Add(id);
            }
            var classIndex = Index(_classDrop);
            var (min, max) = RangeOfClass(classIndex);
            for (var candidate = min; candidate <= max; candidate++)
            {
                if (!used.Contains(candidate)) return candidate;
            }
            return min;
        }

        private static (int Min, int Max) RangeOfClass(int itemClass)
        {
            switch (itemClass)
            {
                case 0:
                case 1:
                case 2: return (10001, 49999);
                case 3: return (50001, 59999);
                case 4: return (700001, 799999);
                case 5: return (1, 9999);
                case 6: return (800001, 899999);
                case 7: return (800001, 899999);
                case 8: return (49999, 49999);
                case 9: return (91000, 91999);
                default: return (10001, 49999);
            }
        }

        private static string SegmentName(int templateId)
        {
            // 精确区间，不靠级联阈值：910000+ 是五行灵材/信物，外观是 91000..91999，两者只差一位。
            if (templateId <= 0) return "号段外";
            if (templateId <= 9999) return "历史/杂项";
            if (templateId >= 10001 && templateId <= 49999) return "装备";
            if (templateId >= 50001 && templateId <= 59999) return "消耗品";
            if (templateId >= 60001 && templateId <= 69999) return "宝石/镶嵌";
            if (templateId >= 91000 && templateId <= 91999) return "外观";
            if (templateId >= 700001 && templateId <= 799999) return "采集材料";
            if (templateId >= 800000 && templateId <= 899999) return "秘籍/配方";
            if (templateId >= 900001 && templateId <= 909999) return "随机词缀";
            if (templateId >= 910000 && templateId <= 919999) return "五行灵材/信物";
            if (templateId >= 920000 && templateId <= 929999) return "套装";
            return "号段外";
        }

        private void RunRead()
        {
            CommitAll();
            var id = _draft.TemplateId;
            ApplyCliOverride();
            GuardBusy("读草稿", () =>
            {
                ItemCli.RunAsync("draft get " + id.ToString(CultureInfo.InvariantCulture), null, result =>
                {
                    try
                    {
                        if (result.Ok && result.HasData && result.Data.TryGetProperty("draftJson", out var json))
                        {
                            var loaded = ItemDraftModel.FromJson(json.GetString());
                            if (loaded != null)
                            {
                                _draft = loaded;
                                ApplyDraftToUi();
                                Log("已载入 Redis 草稿：物品 " + loaded.TemplateId);
                                return;
                            }
                        }
                        // 草稿没有时退到库里已存的行（而不是把空草稿留在界面上让人误以为"没读到"）
                        ItemCli.RunAsync("get " + id.ToString(CultureInfo.InvariantCulture), null, fromDb =>
                        {
                            try
                            {
                                if (fromDb.Ok && fromDb.HasData && fromDb.Data.TryGetProperty("draftJson", out var dbJson))
                                {
                                    var fromDbDraft = ItemDraftModel.FromJson(dbJson.GetString());
                                    if (fromDbDraft != null)
                                    {
                                        _draft = fromDbDraft;
                                        ApplyDraftToUi();
                                        Log("已载入库中物品 " + id);
                                        return;
                                    }
                                }
                                Log("读草稿与库都失败：" + result.Display + " / " + fromDb.Display);
                            }
                            catch (Exception ex)
                            {
                                Log("读物品回调异常：" + ex);
                            }
                            finally
                            {
                                EndBusy("读草稿 结束");
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        Log("读草稿回调异常：" + ex);
                        // 外层 finally 收尾：内层若没起来，这里保证按钮不被锁死。
                        EndBusy("读草稿 异常");
                    }
                });
            });
        }

        private void RunValidate()
        {
            ApplyCliOverride();
            CommitAll();
            GuardBusy("校验", () => ItemCli.RunAsync("validate -", _draft.ToJson(), r => Report(r, "校验")));
        }

        private void RunSaveDraft()
        {
            ApplyCliOverride();
            CommitAll();
            GuardBusy("存草稿", () => ItemCli.RunAsync("draft save -", _draft.ToJson(), r => Report(r, "存草稿")));
        }

        private void RunSave()
        {
            ApplyCliOverride();
            CommitAll();
            GuardBusy("保存到库", () =>
            {
                ItemCli.RunAsync("validate -", _draft.ToJson(), check =>
                {
                    if (!check.Ok)
                    {
                        Report(check, "校验（已挡下保存）");
                        return;
                    }
                    ItemCli.RunAsync("save -", _draft.ToJson(), saved =>
                    {
                        Report(saved, "保存");
                        if (saved.Ok) DescribeWrittenRows(saved);
                    });
                });
            });
        }

        private void RunSetState(string verb)
        {
            ApplyCliOverride();
            CommitAll();
            var id = _draft.TemplateId.ToString(CultureInfo.InvariantCulture);
            GuardBusy(verb, () => ItemCli.RunAsync(verb + " " + id, null, r => Report(r, verb)));
        }

        private void RunDelete()
        {
            ApplyCliOverride();
            CommitAll();
            var id = _draft.TemplateId.ToString(CultureInfo.InvariantCulture);
            GuardBusy("删除", () => ItemCli.RunAsync("delete " + id, null, r =>
            {
                Report(r, "删除");
                Log("提示：玩家背包里的实例不会被删；若回执说「仍被持有」，那是刻意的拦阻。");
            }));
        }

        private void RunPreviewTooltip()
        {
            ApplyCliOverride();
            CommitAll();
            var args = string.Format(CultureInfo.InvariantCulture,
                "preview-tooltip - --level {0} --setPieces {1}", Math.Max(0, _draft.LevelRequire), _lastSetPieces);
            GuardBusy("试算 Tooltip", () => ItemCli.RunAsync(args, _draft.ToJson(), r =>
            {
                Report(r, "Tooltip 试算");
                if (!r.Ok || !r.HasData) return;
                if (r.Data.TryGetProperty("lines", out var lines) && lines.ValueKind == JsonValueKind.Array)
                {
                    var preview = new List<ItemTooltipPreviewLine>();
                    foreach (var line in lines.EnumerateArray())
                    {
                        preview.Add(new ItemTooltipPreviewLine
                        {
                            Kind = line.TryGetProperty("kind", out var k) ? k.GetString() : string.Empty,
                            Text = line.TryGetProperty("text", out var t) ? t.GetString() : string.Empty,
                        });
                    }
                    if (_tooltipView != null)
                        _tooltipView.SetData(preview, _draft.Quality, "来源：内核 preview-tooltip（权威）");
                    Log("内核行数：" + preview.Count);
                }
                // 与本地镜像交叉核对：两边行文本必须逐行相同，否则镜像表已漂。
                var local = ItemPreviewMath.BuildTooltip(_draft, _lastSetPieces, Math.Max(0, _draft.LevelRequire));
                var remote = ExtractTooltipLines(r);
                if (remote != null && remote.Count != local.Count)
                    Log(string.Format("【报黄】本地预览 {0} 行 / 内核 {1} 行——镜像可能与内核漂了", local.Count, remote.Count));
            }));
        }

        private static List<string> ExtractTooltipLines(ItemCliResult result)
        {
            if (!result.HasData || !result.Data.TryGetProperty("lines", out var lines)
                || lines.ValueKind != JsonValueKind.Array) return null;
            var list = new List<string>();
            foreach (var line in lines.EnumerateArray())
            {
                list.Add(line.TryGetProperty("text", out var t) ? t.GetString() : string.Empty);
            }
            return list;
        }

        private void RunPreviewBudget()
        {
            ApplyCliOverride();
            CommitAll();
            var args = string.Format(CultureInfo.InvariantCulture,
                "preview-budget --ilvl {0} --quality {1} --sweep", _draft.ItemLevel, _draft.Quality);
            GuardBusy("试算预算", () => ItemCli.RunAsync(args, null, r =>
            {
                Report(r, "预算试算");
                if (!r.Ok || !r.HasData) return;
                if (r.Data.TryGetProperty("single", out var single)
                    && single.TryGetProperty("budget", out var budget))
                {
                    var remote = budget.GetInt32();
                    var local = ItemPreviewMath.StatBudget(_draft.ItemLevel, _draft.Quality);
                    if (remote != local)
                        Log(string.Format("【报红】预算本地 {0} / 内核 {1}——镜像公式已漂", local, remote));
                    else
                        Log("预算与内核一致：" + remote);
                }
                if (r.Data.TryGetProperty("strictlyIncreasing", out var strict) && !strict.GetBoolean())
                    Log("【报红】内核预算曲线不是严格递增——数值口径有洞");
            }));
        }

        private void RunPreviewAffix()
        {
            ApplyCliOverride();
            CommitAll();
            var seed = 1;
            GuardBusy("试算词缀", () => ItemCli.RunAsync(
                "preview-random - --seed " + seed.ToString(CultureInfo.InvariantCulture), _draft.ToJson(), r =>
                {
                    Report(r, "词缀试算");
                    if (!r.Ok || !r.HasData) return;
                    if (r.Data.TryGetProperty("roll", out var roll))
                        Log("roll = " + roll.GetInt32() + "（同种子永远同结果）");
                    if (r.Data.TryGetProperty("stats", out var stats) && stats.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var stat in stats.EnumerateArray())
                        {
                            var name = stat.TryGetProperty("name", out var n) ? n.GetString() : "?";
                            var value = stat.TryGetProperty("value", out var v) ? v.GetInt32() : 0;
                            Log("  " + name + " +" + value);
                        }
                    }
                }));
        }

        private void RunExportClient()
        {
            ApplyCliOverride();
            var outPath = System.IO.Path.Combine(Globals.ProjectFolder, "Data", "Items", "client_items.json");
            GuardBusy("导出客户端表", () => ItemCli.RunAsync("export-client --out \"" + outPath + "\"", null, r =>
            {
                Report(r, "导出客户端表");
                if (!r.HasData) return;
                if (r.Data.TryGetProperty("path", out var outPath))
                    Log("已写入：" + outPath.GetString() + "（客户端 1 秒内自动重载）");
                if (r.Data.TryGetProperty("problems", out var problems) && problems.ValueKind == JsonValueKind.Array)
                {
                    foreach (var p in problems.EnumerateArray()) Log("问题：" + p.GetString());
                }
                var count = r.Data.TryGetProperty("count", out var c) ? c.GetInt32() : 0;
                if (count == 0)
                    Log("导出 0 条是如实状态：没有任何物品处于 publish_state=1（先发布再导出）。");
            }));
        }

        private void RunReferences()
        {
            ApplyCliOverride();
            CommitAll();
            var id = _draft.TemplateId.ToString(CultureInfo.InvariantCulture);
            GuardBusy("反查引用", () => ItemCli.RunAsync("references " + id, null, r =>
            {
                Report(r, "反查引用");
                if (!r.Ok || !r.HasData) return;
                if (r.Data.TryGetProperty("rows", out var rows) && rows.ValueKind == JsonValueKind.Array)
                {
                    foreach (var row in rows.EnumerateArray())
                    {
                        var kind = row.TryGetProperty("kind", out var k) ? k.GetString() : "?";
                        var detail = row.TryGetProperty("detail", out var d) ? d.GetString() : string.Empty;
                        Log("  [" + kind + "] " + detail);
                    }
                }
            }));
        }

        // ---------- 通用 ----------

        private void GuardBusy(string label, Action start)
        {
            if (_busy)
            {
                // 看门狗：CLI 自己 120 秒超时，超过 180 秒还没回执一定是回调漏了复位。
                // 不解除的话后面每个按钮都会被"上一次子进程还没结束"永久挡掉（已实测）。
                if ((DateTime.UtcNow - _busySince).TotalSeconds > 180)
                {
                    _busy = false;
                    Log("上一次子进程 180 秒未回执，已解除占用（" + label + " 继续）");
                }
                else
                {
                    Log("上一次子进程还没结束，先等它（" + label + " 被忽略）");
                    return;
                }
            }
            _busy = true;
            _busySince = DateTime.UtcNow;
            _statusLabel.Text = label + " 中…";
            try
            {
                start();
            }
            catch (Exception ex)
            {
                _busy = false;
                _statusLabel.Text = label + " 异常";
                Log(label + " 抛异常：" + ex);
            }
        }

        /// <summary>结束一次异步动作。**每个回调的每条出口都要调**，否则按钮会被永久锁住。</summary>
        private void EndBusy(string label)
        {
            _busy = false;
            if (_statusLabel != null && !string.IsNullOrEmpty(label)) _statusLabel.Text = label;
        }

        private void Report(ItemCliResult result, string label)
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

        private static int Index(Dropdown drop)
        {
            if (drop == null) return 0;
            var i = drop.SelectedIndex;
            return i < 0 ? 0 : i;
        }

        private static void SetIndex(Dropdown drop, int index)
        {
            if (drop == null) return;
            drop.SelectedIndex = index < 0 ? 0 : index;
        }

        private static string Num(int value) => value.ToString(CultureInfo.InvariantCulture);

        private static List<int> ParseIds(string csv)
        {
            var list = new List<int>();
            if (string.IsNullOrWhiteSpace(csv)) return list;
            foreach (var token in csv.Split(new[] { ',', '，', ' ', '|' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (int.TryParse(token.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) && v > 0)
                    list.Add(v);
            }
            return list;
        }

        private static List<int> ParseInts(string csv)
        {
            var list = new List<int>();
            if (string.IsNullOrWhiteSpace(csv)) return list;
            foreach (var token in csv.Split(new[] { ',', '，', ' ', '|' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (int.TryParse(token.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
                    list.Add(v);
            }
            return list;
        }

        private static string JoinTypes(List<ItemStatModel> stats)
            => stats == null ? string.Empty
                : string.Join(",", stats.ConvertAll(s => s.StatType.ToString(CultureInfo.InvariantCulture)));

        private static string JoinValues(List<ItemStatModel> stats)
            => stats == null ? string.Empty
                : string.Join(",", stats.ConvertAll(s => s.StatValue.ToString(CultureInfo.InvariantCulture)));

        private static string JoinDamage(List<ItemDamageModel> damage, int column)
        {
            if (damage == null) return string.Empty;
            var values = new List<string>();
            foreach (var item in damage)
            {
                values.Add((column == 0 ? item.DamageType : column == 1 ? item.DamageMin : item.DamageMax)
                    .ToString(CultureInfo.InvariantCulture));
            }
            return string.Join(",", values);
        }

        private static string JoinSpells(List<ItemSpellModel> spells, int column)
        {
            if (spells == null) return string.Empty;
            var values = new List<string>();
            foreach (var spell in spells)
            {
                var value = column == 0 ? spell.SpellId
                    : column == 1 ? spell.SpellTrigger
                    : column == 2 ? spell.Charges
                    : column == 3 ? spell.PpmRate
                    : spell.CooldownMs;
                values.Add(value.ToString(CultureInfo.InvariantCulture));
            }
            return string.Join(",", values);
        }

        private static string JoinTierStats(List<ItemSetTierModel> tiers)
        {
            if (tiers == null) return string.Empty;
            var chunks = new List<string>();
            foreach (var tier in tiers)
            {
                if (tier.Stats == null || tier.Stats.Count == 0) continue;
                var pairs = new List<string>();
                foreach (var stat in tier.Stats)
                {
                    pairs.Add(stat.StatType.ToString(CultureInfo.InvariantCulture) + "="
                              + stat.StatValue.ToString(CultureInfo.InvariantCulture));
                }
                chunks.Add(tier.Pieces.ToString(CultureInfo.InvariantCulture) + ":" + string.Join("|", pairs));
            }
            return string.Join(";", chunks);
        }

        private static string[] BuildClassNames()
        {
            var names = new string[ItemPreviewMath.ClassNames.Length];
            Array.Copy(ItemPreviewMath.ClassNames, names, names.Length);
            return names;
        }

        private void DescribeWrittenRows(ItemCliResult result)
        {
            if (result.HasData && result.Data.TryGetProperty("writtenRows", out var rows)
                && rows.ValueKind == JsonValueKind.Object)
            {
                var sb = new StringBuilder();
                foreach (var prop in rows.EnumerateObject())
                {
                    sb.Append(prop.Name).Append('=').Append(prop.Value.GetInt32()).Append(' ');
                }
                Log(sb.ToString().TrimEnd());
            }
        }
    }
}
