using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.Ink;
using HundunWorld.Game.UI.StyleSystem;
using System;
using System.Collections.Generic;
using HundunWorld.Game.Equipment;

namespace HundunWorld.Game.UI.Ink.Pages.Character
{
    /// <summary>
    /// 装备管理菜单页面。
    /// 承载三列布局（对应 HTML 原型 <c>menu-equipment.html</c>）：
    /// <list type="bullet">
    ///   <item>SubTask 10.2 左列：装备背包列表（8-12 件 <see cref="EquipmentListItem"/>，
    ///     从 <see cref="EquipmentDatabase.GetEquipment"/> 查询名称/品质）</item>
    ///   <item>SubTask 10.2 中列：纸娃娃装备槽（8 槽 <see cref="InkCell"/>，
    ///     对应 <see cref="EquipmentSlot"/> 枚举全部 8 个值：
    ///     Head/Neck/Body/Back/RightHand/LeftHand/Waist/Face）</item>
    ///   <item>SubTask 10.2 右列：属性总览与装备对比（mock 数值 + <see cref="InkBar"/>）</item>
    /// </list>
    /// SubTask 10.3 排序筛选栏（品质筛选：全部/传世/史诗/精良/普通；类型筛选 mock）
    /// + 套装加成显示（mock 套装数据 2 套）。
    /// SubTask 10.4 点击背包物品可穿戴（mock 交互：点击后物品移动到对应装备槽，
    /// 属性总览更新）。
    /// 通过 <see cref="NavigationRequested"/> 事件向路由暴露导航请求。
    /// 全部数据为 mock，通过 <see cref="RefreshLayout"/> 支持屏幕尺寸变化。
    /// </summary>
    public class MenuEquipmentPage : ContainerControl, IInkPage
    {
        // ===================================================================
        // 布局常量
        // =======================================================================

        /// <summary>顶部标题栏高度（像素）</summary>
        private const float TitleHeight = 48f;

        /// <summary>内容区顶部 Y 坐标（标题栏下方留白）</summary>
        private const float ContentTop = 80f;

        /// <summary>内容区底部留白（像素）</summary>
        private const float ContentBottomMargin = 80f;

        /// <summary>屏幕通用边距</summary>
        private const float Margin = 40f;

        /// <summary>列间距</summary>
        private const float ColumnGap = 20f;

        /// <summary>左列装备背包 X 坐标</summary>
        private const float LeftColumnX = 40f;

        /// <summary>左列装备背包宽度</summary>
        private const float LeftColumnWidth = 440f;

        /// <summary>中列纸娃娃 X 坐标（= 左列右边 + 间距 20）</summary>
        private const float MiddleColumnX = 500f;

        /// <summary>右列属性总览宽度</summary>
        private const float RightColumnWidth = 440f;

        /// <summary>右列 X 坐标偏移（screenWidth - 右列宽 - 边距 40）</summary>
        private const float RightColumnRightOffset = 480f;

        /// <summary>列表项高度（像素）</summary>
        private const float ListItemHeight = 52f;

        /// <summary>列表项图标尺寸（正方形）</summary>
        private const float ListItemIconSize = 32f;

        /// <summary>列表项图标左边距</summary>
        private const float ListItemIconLeftMargin = 16f;

        /// <summary>列表项文本与图标间距</summary>
        private const float ListItemIconToTextGap = 12f;

        /// <summary>排序筛选栏高度</summary>
        private const float FilterBarHeight = 40f;

        /// <summary>筛选按钮间距</summary>
        private const float FilterButtonGap = 8f;

        /// <summary>筛选按钮内边距</summary>
        private const float FilterButtonPadding = 10f;

        /// <summary>背包标题栏高度</summary>
        private const float BackpackTitleHeight = 44f;

        /// <summary>装备槽尺寸（正方形）</summary>
        private const float EquipmentCellSize = 64f;

        /// <summary>装备槽垂直间距（含标签高度）</summary>
        private const float EquipmentCellVerticalGap = 24f;

        /// <summary>装备槽标签高度</summary>
        private const float EquipmentSlotLabelHeight = 16f;

        /// <summary>左/右装备槽列在面板内的水平边距</summary>
        private const float SlotColumnHorizontalMargin = 80f;

        /// <summary>装备槽起始 Y 坐标（中列面板内）</summary>
        private const float SlotStartY = 120f;

        /// <summary>纸娃娃预览区宽度</summary>
        private const float PaperDollWidth = 280f;

        /// <summary>已装备摘要栏高度</summary>
        private const float SummaryBarHeight = 40f;

        /// <summary>属性总览行高</summary>
        private const float AttrRowHeight = 24f;

        /// <summary>属性名 Label 宽度</summary>
        private const float AttrLabelWidth = 48f;

        /// <summary>属性值 Label 宽度</summary>
        private const float AttrValueWidth = 72f;

        /// <summary>属性差值 Label 宽度</summary>
        private const float AttrDeltaWidth = 48f;

        /// <summary>属性区水平内边距</summary>
        private const float AttrHorizontalPadding = 24f;

        /// <summary>套装加成卡片高度</summary>
        private const float SetBonusCardHeight = 72f;

        /// <summary>装备对比卡片高度</summary>
        private const float CompareCardHeight = 96f;

        /// <summary>更换装备按钮高度</summary>
        private const float SwapButtonHeight = 36f;

        // ===================================================================
        // 子控件引用
        // =======================================================================

        /// <summary>顶部标题栏</summary>
        private InkPanelTitle _title;

        /// <summary>左列装备背包面板</summary>
        private InkPanel _leftPanel;

        /// <summary>中列纸娃娃面板</summary>
        private InkPanel _middlePanel;

        /// <summary>右列属性总览面板</summary>
        private InkPanel _rightPanel;

        /// <summary>背包标题文本（数量统计）</summary>
        private Label _backpackCountLabel;

        /// <summary>品质筛选按钮组（全部/传世/史诗/精良/普通）</summary>
        private InkButton[] _qualityFilterButtons;

        /// <summary>类型筛选按钮组（全部/武器/防具/配饰）</summary>
        private InkButton[] _typeFilterButtons;

        /// <summary>背包物品列表项数组</summary>
        private EquipmentListItem[] _backpackItems_ui;

        /// <summary>8 个装备槽 InkCell</summary>
        private InkCell[] _equipmentCells;

        /// <summary>8 个装备槽标签</summary>
        private Label[] _equipmentSlotLabels;

        /// <summary>纸娃娃预览控件</summary>
        private PaperDollPreview _paperDoll;

        /// <summary>已装备数量摘要 Label</summary>
        private Label _summaryCountLabel;

        /// <summary>套装状态摘要 Label</summary>
        private Label _summarySetLabel;

        /// <summary>属性总览数值 Label 数组</summary>
        private Label[] _attrValueLabels;

        /// <summary>属性总览差值 Label 数组</summary>
        private Label[] _attrDeltaLabels;

        /// <summary>套装加成名称 Label 数组</summary>
        private Label[] _setNameLabels;

        /// <summary>套装加成件数 Label 数组</summary>
        private Label[] _setCountLabels;

        /// <summary>套装加成描述 Label 数组</summary>
        private Label[] _setDescLabels;

        /// <summary>对比卡片当前装备名称 Label</summary>
        private Label _compareCurrentName;

        /// <summary>对比卡片候选装备名称 Label</summary>
        private Label _compareCandidateName;

        /// <summary>对比卡片当前装备属性 Label</summary>
        private Label _compareCurrentAttr;

        /// <summary>对比卡片候选装备属性 Label</summary>
        private Label _compareCandidateAttr;

        /// <summary>对比卡片差值 Label</summary>
        private Label _compareDeltaLabel;

        /// <summary>更换装备按钮</summary>
        private InkButton _swapButton;

        // ===================================================================
        // 屏幕尺寸缓存
        // =======================================================================

        /// <summary>当前屏幕尺寸缓存，用于布局计算</summary>
        private Float2 _screenSize;

        // ===================================================================
        // mock 数据字段
        // =======================================================================

        /// <summary>背包物品 mock 数据（10 件）</summary>
        private MockEquipment[] _backpackData;

        /// <summary>当前选中的背包物品索引（-1 表示未选中）</summary>
        private int _selectedIndex = -1;

        /// <summary>已装备槽位字典（slot → 背包数据索引，-1 表示空）</summary>
        private Dictionary<EquipmentSlot, int> _equippedSlots;

        /// <summary>当前品质筛选（0=全部,1=传世,2=史诗,3=精良,4=普通）</summary>
        private int _qualityFilter = 0;

        /// <summary>当前类型筛选（0=全部,1=武器,2=防具,3=配饰）</summary>
        private int _typeFilter = 0;

        /// <summary>mock 属性总览数据（攻击/防御/气血/暴击/命中/闪避）</summary>
        private AttrRow[] _attrRows;

        /// <summary>mock 套装数据（2 套）</summary>
        private MockSet[] _sets;

        /// <summary>装备槽显示顺序（对应 8 个 InkCell，按左列上到下 + 右列上到下排列）</summary>
        private static readonly EquipmentSlot[] DisplayedSlots =
        {
            EquipmentSlot.Head,
            EquipmentSlot.Body,
            EquipmentSlot.RightHand,
            EquipmentSlot.Waist,
            EquipmentSlot.Neck,
            EquipmentSlot.Back,
            EquipmentSlot.LeftHand,
            EquipmentSlot.Face,
        };

        /// <summary>装备槽中文显示名</summary>
        private static readonly string[] SlotDisplayNames =
        {
            "头部",
            "身体",
            "右手",
            "腰部",
            "颈部",
            "背部",
            "左手",
            "面部",
        };

        // ===================================================================
        // 公共事件
        // =======================================================================

        /// <summary>
        /// 导航请求事件。
        /// 由页面交互（如点击装备槽查看详情）触发，参数为目标路由键。
        /// 由 <see cref="InkPageRouter"/> 订阅以执行页面跳转。
        /// </summary>
        public event Action<string> NavigationRequested;

        // ===================================================================
        // 构造函数
        // =======================================================================

        /// <summary>
        /// 构造函数：初始化全部三列子区域，使用 mock 数据填充。
        /// 构造时读取 <see cref="FlaxEngine.Screen.Size"/> 计算布局，
        /// 屏幕尺寸未就绪时使用 1920x1080 兜底。
        /// </summary>
        public MenuEquipmentPage()
        {
            // 1. 读取屏幕尺寸
            _screenSize = FlaxEngine.Screen.Size;
            if (_screenSize.X <= 0f || _screenSize.Y <= 0f)
            {
                _screenSize = new Float2(1920f, 1080f);
            }

            // 2. 外壳：全屏拉伸 + 透明背景 + 不裁剪子控件
            AnchorPreset = AnchorPresets.StretchAll;
            BackgroundColor = Color.Transparent;
            ClipChildren = false;
            AutoFocus = false;
            Location = Float2.Zero;
            Size = _screenSize;

            // 3. 初始化 mock 数据
            InitMockData();

            try
            {
                BuildTitle();
                BuildLeftBackpack();
                BuildMiddlePaperDoll();
                BuildRightOverview();

                // 应用初始布局
                ApplyLayout();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[MenuEquipmentPage] 初始化失败: {ex.Message}");
            }
        }

        // ===================================================================
        // mock 数据初始化
        // =======================================================================

        /// <summary>
        /// 初始化 mock 数据：背包物品、已装备槽位、属性总览、套装。
        /// 背包物品中真实 ID（10001/20001/30001）通过 <see cref="EquipmentDatabase.GetEquipment"/>
        /// 查询名称与品质，其余使用 mock 兜底值。
        /// </summary>
        private void InitMockData()
        {
            // mock 背包定义：(id, slot, fallbackName, fallbackQuality, level, type)
            var defs = new (int id, EquipmentSlot slot, string name, InkWashTheme.InkQuality quality, int level, string type)[]
            {
                (20001, EquipmentSlot.RightHand, "赤焰刀",   InkWashTheme.InkQuality.Legendary, 40, "长刀"),
                (10001, EquipmentSlot.Body,      "天蚕丝甲", InkWashTheme.InkQuality.Legendary, 38, "胸甲"),
                (30001, EquipmentSlot.Head,      "碧水剑",   InkWashTheme.InkQuality.Epic,      35, "长剑"),
                (20002, EquipmentSlot.RightHand, "玄龟盾",   InkWashTheme.InkQuality.Epic,      32, "副武器"),
                (20003, EquipmentSlot.RightHand, "青锋长枪", InkWashTheme.InkQuality.Rare,      30, "长枪"),
                (20004, EquipmentSlot.LeftHand,  "寒铁匕首", InkWashTheme.InkQuality.Rare,      28, "短刃"),
                (30002, EquipmentSlot.Head,      "粗布护腕", InkWashTheme.InkQuality.Common,   15, "护腕"),
                (40001, EquipmentSlot.Neck,      "铁头靴",   InkWashTheme.InkQuality.Common,   12, "靴"),
                (50001, EquipmentSlot.Waist,     "龙纹腰带", InkWashTheme.InkQuality.Legendary, 25, "腰带"),
                (60001, EquipmentSlot.Back,      "玄色披风", InkWashTheme.InkQuality.Epic,      22, "披风"),
            };

            _backpackData = new MockEquipment[defs.Length];
            for (int i = 0; i < defs.Length; i++)
            {
                var d = defs[i];
                var data = EquipmentDatabase.GetEquipment(d.id);
                _backpackData[i] = new MockEquipment
                {
                    Id = d.id,
                    Slot = d.slot,
                    Name = data?.Name ?? d.name,
                    Quality = data != null ? MapEquipmentQuality(data.Quality) : d.quality,
                    Level = d.level,
                    Type = d.type,
                };
            }

            // 已装备槽位 mock：Head/Body/RightHand 初始已装备（对应真实默认装备 ID）
            _equippedSlots = new Dictionary<EquipmentSlot, int>
            {
                { EquipmentSlot.Head, 2 },      // 碧水剑（mock 索引 2）
                { EquipmentSlot.Body, 1 },      // 天蚕丝甲（mock 索引 1）
                { EquipmentSlot.RightHand, 0 }, // 赤焰刀（mock 索引 0）
            };

            // mock 属性总览：攻击/防御/气血/暴击/命中/闪避
            _attrRows = new AttrRow[]
            {
                new AttrRow { Name = "攻击", Value = "856",  Delta = "+42",  DeltaUp = true },
                new AttrRow { Name = "防御", Value = "642",  Delta = "+18",  DeltaUp = true },
                new AttrRow { Name = "气血", Value = "3200", Delta = "+200", DeltaUp = true },
                new AttrRow { Name = "暴击", Value = "23%", Delta = "+3%",  DeltaUp = true },
                new AttrRow { Name = "命中", Value = "97%", Delta = "--",   DeltaUp = null },
                new AttrRow { Name = "闪避", Value = "12%", Delta = "-1%",  DeltaUp = false },
            };

            // mock 套装数据（2 套）
            _sets = new MockSet[]
            {
                new MockSet
                {
                    Name = "龙纹套装",
                    Current = 2,
                    Total = 4,
                    Bonus2 = "2件：攻击+5%",
                    Bonus4 = "4件：暴击伤害+15%",
                },
                new MockSet
                {
                    Name = "玄铁套装",
                    Current = 1,
                    Total = 3,
                    Bonus2 = "2件：防御+5%",
                    Bonus4 = "3件：气血+300",
                },
            };
        }

        // ===================================================================
        // SubTask 构造方法
        // =======================================================================

        /// <summary>
        /// SubTask 10.1：顶部标题栏。
        /// <see cref="InkPanelTitle"/> 文本"装备管理"，位置 (0, 0)，宽度铺满，高度 48。
        /// 返回按钮由 <see cref="InkPageShell"/> 自动添加，本页面不自建。
        /// </summary>
        private void BuildTitle()
        {
            _title = new InkPanelTitle
            {
                Title = "装备管理",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = Float2.Zero,
                Height = TitleHeight,
            };
            AddChild(_title);
        }

        /// <summary>
        /// SubTask 10.2 + 10.3：左列装备背包列表。
        /// <see cref="InkPanel"/> 容器位置 (40, 80)，尺寸 (440, screenHeight - 160)。
        /// 内含：背包标题栏（"装备背包" + 数量统计）+ 排序筛选栏
        /// （品质筛选 5 按钮 + 类型筛选 4 按钮）+ 物品列表（10 个 <see cref="EquipmentListItem"/>）。
        /// 每项布局为左侧品质色图标 + 装备名 + 品质标签 + 等级/类型说明。
        /// </summary>
        private void BuildLeftBackpack()
        {
            _leftPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
            };
            AddChild(_leftPanel);

            BuildBackpackTitle();
            BuildFilterBar();
            BuildBackpackList();
        }

        /// <summary>
        /// 构建背包标题栏（"装备背包" + 数量统计如 "10/24"）。
        /// </summary>
        private void BuildBackpackTitle()
        {
            // 标题容器
            var titleContainer = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, 0f),
                Size = new Float2(LeftColumnWidth, BackpackTitleHeight),
            };
            _leftPanel.AddChild(titleContainer);

            var titleLabel = new Label
            {
                Text = "装备背包",
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), 15f),
                TextColor = InkWashTheme.GoldPrimary,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(24f, 0f),
                Size = new Float2(160f, BackpackTitleHeight),
            };
            titleContainer.AddChild(titleLabel);

            _backpackCountLabel = new Label
            {
                Text = "10/24",
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 11f),
                TextColor = InkWashTheme.PaperAged,
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(LeftColumnWidth - 88f, 0f),
                Size = new Float2(64f, BackpackTitleHeight),
            };
            titleContainer.AddChild(_backpackCountLabel);
        }

        /// <summary>
        /// SubTask 10.3：构建排序筛选栏。
        /// 品质筛选 5 按钮（全部/传世/史诗/精良/普通）+ 类型筛选 4 按钮（全部/武器/防具/配饰）。
        /// 点击按钮切换 active 态并应用筛选。
        /// </summary>
        private void BuildFilterBar()
        {
            var filterContainer = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, BackpackTitleHeight),
                Size = new Float2(LeftColumnWidth, FilterBarHeight * 2f),
            };
            _leftPanel.AddChild(filterContainer);

            // 品质筛选按钮（固定宽度，每按钮文字均为 2 字）
            const float FilterBtnWidth = 56f;
            string[] qualityLabels = { "全部", "传世", "史诗", "精良", "普通" };
            _qualityFilterButtons = new InkButton[qualityLabels.Length];
            float btnX = 16f;
            for (int i = 0; i < qualityLabels.Length; i++)
            {
                int capturedIndex = i;
                var btn = new InkButton
                {
                    Text = qualityLabels[i],
                    Variant = i == 0 ? InkButtonVariant.Primary : InkButtonVariant.Ghost,
                    ButtonSize = InkButtonSize.Sm,
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(btnX, 4f),
                    Size = new Float2(FilterBtnWidth, InkWashTheme.ControlHSm),
                };
                btn.ButtonClicked += _ => OnQualityFilterClicked(capturedIndex);
                filterContainer.AddChild(btn);
                _qualityFilterButtons[i] = btn;
                btnX += FilterBtnWidth + FilterButtonGap;
            }

            // 类型筛选按钮（固定宽度，每按钮文字均为 2 字）
            string[] typeLabels = { "全部", "武器", "防具", "配饰" };
            _typeFilterButtons = new InkButton[typeLabels.Length];
            btnX = 16f;
            for (int i = 0; i < typeLabels.Length; i++)
            {
                int capturedIndex = i;
                var btn = new InkButton
                {
                    Text = typeLabels[i],
                    Variant = i == 0 ? InkButtonVariant.Primary : InkButtonVariant.Ghost,
                    ButtonSize = InkButtonSize.Sm,
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(btnX, FilterBarHeight + 4f),
                    Size = new Float2(FilterBtnWidth, InkWashTheme.ControlHSm),
                };
                btn.ButtonClicked += _ => OnTypeFilterClicked(capturedIndex);
                filterContainer.AddChild(btn);
                _typeFilterButtons[i] = btn;
                btnX += FilterBtnWidth + FilterButtonGap;
            }
        }

        /// <summary>
        /// 构建背包物品列表（10 个 <see cref="EquipmentListItem"/>）。
        /// 每项包含：左侧品质色图标方块 + 装备名（品质色）+ 等级与类型说明。
        /// 点击触发选中与对比更新。
        /// </summary>
        private void BuildBackpackList()
        {
            float listStartY = BackpackTitleHeight + FilterBarHeight * 2f;

            _backpackItems_ui = new EquipmentListItem[_backpackData.Length];
            for (int i = 0; i < _backpackData.Length; i++)
            {
                var data = _backpackData[i];
                var item = new EquipmentListItem
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(0f, listStartY + i * ListItemHeight),
                    Size = new Float2(LeftColumnWidth, ListItemHeight),
                    EquipmentIndex = i,
                };
                item.Clicked += () => OnItemClicked(i);

                // 左侧品质色图标方块
                var iconBox = new ContainerControl
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(ListItemIconLeftMargin, (ListItemHeight - ListItemIconSize) * 0.5f),
                    Size = new Float2(ListItemIconSize, ListItemIconSize),
                    BackgroundColor = new Color(
                        InkWashTheme.QualityColor(data.Quality).R,
                        InkWashTheme.QualityColor(data.Quality).G,
                        InkWashTheme.QualityColor(data.Quality).B, 0.12f),
                };
                item.AddChild(iconBox);

                // 装备名 Label（品质色）
                float textX = ListItemIconLeftMargin + ListItemIconSize + ListItemIconToTextGap;
                var nameLabel = new Label
                {
                    Text = data.Name,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), 13f),
                    TextColor = InkWashTheme.QualityTextColor(data.Quality),
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(textX, 2f),
                    Size = new Float2(LeftColumnWidth - textX - 24f, ListItemHeight * 0.5f),
                };
                item.AddChild(nameLabel);

                // 等级与类型说明 Label
                var descLabel = new Label
                {
                    Text = $"Lv.{data.Level} · {data.Type}",
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 11f),
                    TextColor = InkWashTheme.PaperAged,
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(textX, ListItemHeight * 0.5f),
                    Size = new Float2(LeftColumnWidth - textX - 24f, ListItemHeight * 0.5f),
                };
                item.AddChild(descLabel);

                _leftPanel.AddChild(item);
                _backpackItems_ui[i] = item;
            }
        }

        /// <summary>
        /// SubTask 10.2：中列纸娃娃装备槽。
        /// <see cref="InkPanel"/> 容器位置 (500, 80)，尺寸 (动态宽度, screenHeight - 160)。
        /// 内含：标题栏（"装备槽位"）+ <see cref="PaperDollPreview"/> 角色轮廓占位 +
        /// 8 个 <see cref="InkCell"/> 装备槽（左 4 右 4 分布）+ 底部已装备摘要栏。
        /// </summary>
        private void BuildMiddlePaperDoll()
        {
            _middlePanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
            };
            AddChild(_middlePanel);

            BuildMiddleTitle();
            BuildEquipmentSlots();
            BuildSummaryBar();
        }

        /// <summary>
        /// 构建中列标题栏（"装备槽位"）。
        /// </summary>
        private void BuildMiddleTitle()
        {
            var titleLabel = new Label
            {
                Text = "装备槽位",
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), 18f),
                TextColor = InkWashTheme.PaperBright,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, 0f),
                Size = new Float2(920f, BackpackTitleHeight),
            };
            _middlePanel.AddChild(titleLabel);
        }

        /// <summary>
        /// 构建 8 个装备槽（左 4 右 4 分布）与中央纸娃娃预览。
        /// 左列：Head/Body/RightHand/Waist；右列：Neck/Back/LeftHand/Face。
        /// 每个装备槽含 <see cref="InkCell"/>（品质色边框）+ 下方中文标签 Label。
        /// </summary>
        private void BuildEquipmentSlots()
        {
            // 中央纸娃娃预览
            _paperDoll = new PaperDollPreview
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(320f, SlotStartY),
                Size = new Float2(PaperDollWidth, 400f),
            };
            _middlePanel.AddChild(_paperDoll);

            _equipmentCells = new InkCell[DisplayedSlots.Length];
            _equipmentSlotLabels = new Label[DisplayedSlots.Length];

            for (int i = 0; i < DisplayedSlots.Length; i++)
            {
                var slot = DisplayedSlots[i];
                int col = i / 4; // 0=左列, 1=右列
                int row = i % 4;

                float x = col == 0
                    ? SlotColumnHorizontalMargin
                    : 920f - SlotColumnHorizontalMargin - EquipmentCellSize;
                float y = SlotStartY + row * (EquipmentCellSize + EquipmentCellVerticalGap + EquipmentSlotLabelHeight);

                var cell = new InkCell
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(x, y),
                    Size = new Float2(EquipmentCellSize, EquipmentCellSize),
                    Quality = GetSlotQuality(slot),
                };
                _middlePanel.AddChild(cell);
                _equipmentCells[i] = cell;

                var slotLabel = new Label
                {
                    Text = SlotDisplayNames[i],
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), 11f),
                    TextColor = InkWashTheme.PaperAged,
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(x - 8f, y + EquipmentCellSize + 2f),
                    Size = new Float2(EquipmentCellSize + 16f, EquipmentSlotLabelHeight),
                };
                _middlePanel.AddChild(slotLabel);
                _equipmentSlotLabels[i] = slotLabel;
            }
        }

        /// <summary>
        /// 构建底部已装备摘要栏（"已装备 X/8" + 套装状态）。
        /// </summary>
        private void BuildSummaryBar()
        {
            var summaryContainer = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, 0f),
                Size = new Float2(920f, SummaryBarHeight),
                BackgroundColor = InkWashTheme.BaseTertiary,
            };
            _middlePanel.AddChild(summaryContainer);

            var equippedLabel = new Label
            {
                Text = "已装备",
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 11f),
                TextColor = InkWashTheme.PaperAged,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(24f, 0f),
                Size = new Float2(56f, SummaryBarHeight),
            };
            summaryContainer.AddChild(equippedLabel);

            _summaryCountLabel = new Label
            {
                Text = "3/8",
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 13f),
                TextColor = InkWashTheme.PaperBright,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(80f, 0f),
                Size = new Float2(48f, SummaryBarHeight),
            };
            summaryContainer.AddChild(_summaryCountLabel);

            _summarySetLabel = new Label
            {
                Text = "套装 2/4 · 龙纹之力",
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 11f),
                TextColor = InkWashTheme.GoldPrimary,
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(600f, 0f),
                Size = new Float2(296f, SummaryBarHeight),
            };
            summaryContainer.AddChild(_summarySetLabel);
        }

        /// <summary>
        /// SubTask 10.2 + 10.3：右列属性总览与装备对比。
        /// <see cref="InkPanel"/> 容器位置 (screenWidth - 480, 80)，尺寸 (440, screenHeight - 160)。
        /// 内含：属性总览标题 + 6 行属性总览（带差值）+ 2 套套装加成卡片 +
        /// 装备对比卡片（当前 vs 候选）+ 更换装备按钮。
        /// </summary>
        private void BuildRightOverview()
        {
            _rightPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
            };
            AddChild(_rightPanel);

            BuildAttrOverview();
            BuildSetBonus();
            BuildComparePanel();
        }

        /// <summary>
        /// 构建属性总览区（6 行属性 + 差值）。
        /// </summary>
        private void BuildAttrOverview()
        {
            float y = 0f;

            var titleLabel = new Label
            {
                Text = "属性总览",
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), 15f),
                TextColor = InkWashTheme.GoldPrimary,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(AttrHorizontalPadding, y),
                Size = new Float2(RightColumnWidth - AttrHorizontalPadding * 2f, BackpackTitleHeight),
            };
            _rightPanel.AddChild(titleLabel);
            y += BackpackTitleHeight;

            _attrValueLabels = new Label[_attrRows.Length];
            _attrDeltaLabels = new Label[_attrRows.Length];

            for (int i = 0; i < _attrRows.Length; i++)
            {
                var attr = _attrRows[i];
                float rowY = y + i * AttrRowHeight;

                var nameLabel = new Label
                {
                    Text = attr.Name,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), 11f),
                    TextColor = InkWashTheme.PaperAged,
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(AttrHorizontalPadding, rowY),
                    Size = new Float2(AttrLabelWidth, AttrRowHeight),
                };
                _rightPanel.AddChild(nameLabel);

                var valueLabel = new Label
                {
                    Text = attr.Value,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 13f),
                    TextColor = InkWashTheme.PaperBright,
                    HorizontalAlignment = TextAlignment.Far,
                    VerticalAlignment = TextAlignment.Center,
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(RightColumnWidth - AttrHorizontalPadding - AttrDeltaWidth - AttrValueWidth, rowY),
                    Size = new Float2(AttrValueWidth, AttrRowHeight),
                };
                _rightPanel.AddChild(valueLabel);
                _attrValueLabels[i] = valueLabel;

                Color deltaColor;
                if (attr.DeltaUp == null)
                    deltaColor = InkWashTheme.TextTertiary;
                else if (attr.DeltaUp.Value)
                    deltaColor = InkWashTheme.JadeBright;
                else
                    deltaColor = InkWashTheme.VermilionBright;

                var deltaLabel = new Label
                {
                    Text = attr.Delta,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 10f),
                    TextColor = deltaColor,
                    HorizontalAlignment = TextAlignment.Far,
                    VerticalAlignment = TextAlignment.Center,
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(RightColumnWidth - AttrHorizontalPadding - AttrDeltaWidth, rowY),
                    Size = new Float2(AttrDeltaWidth, AttrRowHeight),
                };
                _rightPanel.AddChild(deltaLabel);
                _attrDeltaLabels[i] = deltaLabel;
            }
        }

        /// <summary>
        /// SubTask 10.3：构建套装加成卡片（2 套 mock 套装数据）。
        /// </summary>
        private void BuildSetBonus()
        {
            float startY = BackpackTitleHeight + _attrRows.Length * AttrRowHeight + 16f;

            _setNameLabels = new Label[_sets.Length];
            _setCountLabels = new Label[_sets.Length];
            _setDescLabels = new Label[_sets.Length];

            for (int i = 0; i < _sets.Length; i++)
            {
                var set = _sets[i];
                float cardY = startY + i * (SetBonusCardHeight + 8f);

                var card = new ContainerControl
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(AttrHorizontalPadding, cardY),
                    Size = new Float2(RightColumnWidth - AttrHorizontalPadding * 2f, SetBonusCardHeight),
                    BackgroundColor = new Color(
                        InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G,
                        InkWashTheme.GoldPrimary.B, 0.06f),
                };
                _rightPanel.AddChild(card);

                var nameLabel = new Label
                {
                    Text = set.Name,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), 11f),
                    TextColor = InkWashTheme.GoldBright,
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(12f, 6f),
                    Size = new Float2(160f, 20f),
                };
                card.AddChild(nameLabel);
                _setNameLabels[i] = nameLabel;

                var countLabel = new Label
                {
                    Text = $"{set.Current}/{set.Total}",
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 11f),
                    TextColor = InkWashTheme.PaperAged,
                    HorizontalAlignment = TextAlignment.Far,
                    VerticalAlignment = TextAlignment.Center,
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(card.Width - 72f, 6f),
                    Size = new Float2(60f, 20f),
                };
                card.AddChild(countLabel);
                _setCountLabels[i] = countLabel;

                var descLabel = new Label
                {
                    Text = $"{set.Bonus2}  {set.Bonus4}",
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 11f),
                    TextColor = InkWashTheme.TextTertiary,
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(12f, 30f),
                    Size = new Float2(card.Width - 24f, 36f),
                };
                card.AddChild(descLabel);
                _setDescLabels[i] = descLabel;
            }
        }

        /// <summary>
        /// 构建装备对比面板（当前装备 vs 候选装备）+ 更换装备按钮。
        /// </summary>
        private void BuildComparePanel()
        {
            float startY = BackpackTitleHeight + _attrRows.Length * AttrRowHeight + 16f +
                           _sets.Length * (SetBonusCardHeight + 8f) + 16f;

            // 对比标题
            var titleLabel = new Label
            {
                Text = "装备对比",
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), 15f),
                TextColor = InkWashTheme.GoldPrimary,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(AttrHorizontalPadding, startY),
                Size = new Float2(RightColumnWidth - AttrHorizontalPadding * 2f, BackpackTitleHeight),
            };
            _rightPanel.AddChild(titleLabel);
            startY += BackpackTitleHeight;

            // 当前装备卡片
            var currentCard = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(AttrHorizontalPadding, startY),
                Size = new Float2(RightColumnWidth - AttrHorizontalPadding * 2f, CompareCardHeight),
                BackgroundColor = new Color(InkWashTheme.BaseTertiary.R, InkWashTheme.BaseTertiary.G, InkWashTheme.BaseTertiary.B, 0.4f),
            };
            _rightPanel.AddChild(currentCard);

            _compareCurrentName = new Label
            {
                Text = "当前·赤焰刀",
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), 13f),
                TextColor = InkWashTheme.GoldBright,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, 8f),
                Size = new Float2(currentCard.Width - 24f, 20f),
            };
            currentCard.AddChild(_compareCurrentName);

            _compareCurrentAttr = new Label
            {
                Text = "攻击 +186\n暴击 +8%",
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 11f),
                TextColor = InkWashTheme.PaperBright,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Near,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, 32f),
                Size = new Float2(currentCard.Width - 24f, 56f),
            };
            currentCard.AddChild(_compareCurrentAttr);

            startY += CompareCardHeight + 8f;

            // 候选装备卡片
            var candidateCard = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(AttrHorizontalPadding, startY),
                Size = new Float2(RightColumnWidth - AttrHorizontalPadding * 2f, CompareCardHeight),
                BackgroundColor = new Color(
                    InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G,
                    InkWashTheme.GoldPrimary.B, 0.05f),
            };
            _rightPanel.AddChild(candidateCard);

            _compareCandidateName = new Label
            {
                Text = "候选·（未选择）",
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), 13f),
                TextColor = InkWashTheme.GoldBright,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, 8f),
                Size = new Float2(candidateCard.Width - 24f, 20f),
            };
            candidateCard.AddChild(_compareCandidateName);

            _compareCandidateAttr = new Label
            {
                Text = "--",
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 11f),
                TextColor = InkWashTheme.PaperBright,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Near,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, 32f),
                Size = new Float2(candidateCard.Width - 24f, 40f),
            };
            candidateCard.AddChild(_compareCandidateAttr);

            _compareDeltaLabel = new Label
            {
                Text = "",
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 11f),
                TextColor = InkWashTheme.JadeBright,
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Near,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, 72f),
                Size = new Float2(candidateCard.Width - 24f, 20f),
            };
            candidateCard.AddChild(_compareDeltaLabel);

            startY += CompareCardHeight + 16f;

            // 更换装备按钮
            _swapButton = new InkButton
            {
                Text = "更换装备",
                // 设计方案：朱红仅限战斗/危险/扣减场景；更换装备为常规操作，用Primary
                Variant = InkButtonVariant.Primary,
                ButtonSize = InkButtonSize.Md,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(AttrHorizontalPadding, startY),
                Size = new Float2(RightColumnWidth - AttrHorizontalPadding * 2f, SwapButtonHeight),
            };
            _swapButton.ButtonClicked += OnSwapClicked;
            _rightPanel.AddChild(_swapButton);
        }

        // ===================================================================
        // 布局计算
        // =======================================================================

        /// <summary>
        /// 根据当前 <see cref="_screenSize"/> 重新计算所有子控件的位置与尺寸。
        /// 由构造函数与 <see cref="RefreshLayout"/> 调用。
        /// </summary>
        private void ApplyLayout()
        {
            float sw = _screenSize.X;
            float sh = _screenSize.Y;

            // 顶部标题栏
            if (_title != null)
            {
                _title.Location = Float2.Zero;
                _title.Size = new Float2(sw, TitleHeight);
            }

            // 左列装备背包面板
            if (_leftPanel != null)
            {
                _leftPanel.Location = new Float2(LeftColumnX, ContentTop);
                _leftPanel.Size = new Float2(LeftColumnWidth, sh - ContentTop - ContentBottomMargin);
            }

            // 中列纸娃娃面板
            if (_middlePanel != null)
            {
                float middleWidth = sw - MiddleColumnX - RightColumnWidth - Margin;
                _middlePanel.Location = new Float2(MiddleColumnX, ContentTop);
                _middlePanel.Size = new Float2(middleWidth, sh - ContentTop - ContentBottomMargin);
            }

            // 右列属性总览面板
            if (_rightPanel != null)
            {
                _rightPanel.Location = new Float2(sw - RightColumnRightOffset, ContentTop);
                _rightPanel.Size = new Float2(RightColumnWidth, sh - ContentTop - ContentBottomMargin);
            }
        }

        /// <summary>
        /// 在屏幕尺寸变化时重新布局所有子控件。
        /// 外部（如 <see cref="InkPageShell"/> 或屏幕大小变更监听器）应调用此方法。
        /// </summary>
        public void RefreshLayout()
        {
            // 优先使用控件实际尺寸（已由 InkPageShell.LoadPage 的 StretchAll 锚点填充父容器）
            float w = Width;
            float h = Height;
            if (w <= 0f || h <= 0f)
            {
                // 控件尚未布局，回退到屏幕尺寸
                var screen = FlaxEngine.Screen.Size;
                w = screen.X;
                h = screen.Y;
            }
            if (w <= 0f || h <= 0f)
            {
                // 仍然为 0，使用 1920x1080 兜底
                w = 1920f;
                h = 1080f;
            }
            _screenSize = new Float2(w, h);
            ApplyLayout();
        }

        // ===================================================================
        // 交互处理
        // =======================================================================

        /// <summary>
        /// SubTask 10.4：背包物品点击处理。
        /// 设置选中态（Active），更新右侧装备对比面板显示候选装备属性差异。
        /// </summary>
        /// <param name="index">被点击的背包物品索引</param>
        private void OnItemClicked(int index)
        {
            if (_backpackItems_ui == null || index < 0 || index >= _backpackItems_ui.Length)
                return;

            _selectedIndex = index;

            // 更新选中态
            for (int i = 0; i < _backpackItems_ui.Length; i++)
            {
                if (_backpackItems_ui[i] != null)
                    _backpackItems_ui[i].Active = (i == index);
            }

            UpdateComparePanel();
        }

        /// <summary>
        /// 品质筛选按钮点击处理：切换 active 态并应用筛选。
        /// </summary>
        /// <param name="index">品质筛选索引（0=全部,1=传世,2=史诗,3=精良,4=普通）</param>
        private void OnQualityFilterClicked(int index)
        {
            _qualityFilter = index;

            if (_qualityFilterButtons != null)
            {
                for (int i = 0; i < _qualityFilterButtons.Length; i++)
                {
                    if (_qualityFilterButtons[i] != null)
                        _qualityFilterButtons[i].Variant = (i == index) ? InkButtonVariant.Primary : InkButtonVariant.Ghost;
                }
            }

            ApplyFilter();
        }

        /// <summary>
        /// 类型筛选按钮点击处理：切换 active 态并应用筛选。
        /// </summary>
        /// <param name="index">类型筛选索引（0=全部,1=武器,2=防具,3=配饰）</param>
        private void OnTypeFilterClicked(int index)
        {
            _typeFilter = index;

            if (_typeFilterButtons != null)
            {
                for (int i = 0; i < _typeFilterButtons.Length; i++)
                {
                    if (_typeFilterButtons[i] != null)
                        _typeFilterButtons[i].Variant = (i == index) ? InkButtonVariant.Primary : InkButtonVariant.Ghost;
                }
            }

            ApplyFilter();
        }

        /// <summary>
        /// 应用当前筛选条件，隐藏不匹配的背包物品。
        /// </summary>
        private void ApplyFilter()
        {
            if (_backpackItems_ui == null || _backpackData == null)
                return;

            for (int i = 0; i < _backpackItems_ui.Length && i < _backpackData.Length; i++)
            {
                if (_backpackItems_ui[i] == null)
                    continue;

                bool visible = true;
                var data = _backpackData[i];

                // 品质筛选（1=传世/Legendary,2=史诗/Epic,3=精良/Rare,4=普通/Common）
                if (_qualityFilter > 0)
                {
                    var expectedQuality = (InkWashTheme.InkQuality)(_qualityFilter - 1);
                    if (data.Quality != expectedQuality)
                        visible = false;
                }

                // 类型筛选（1=武器,2=防具,3=配饰）
                if (visible && _typeFilter > 0)
                {
                    bool isWeapon = data.Slot == EquipmentSlot.RightHand || data.Slot == EquipmentSlot.LeftHand;
                    bool isArmor = data.Slot == EquipmentSlot.Head || data.Slot == EquipmentSlot.Body ||
                                   data.Slot == EquipmentSlot.Back || data.Slot == EquipmentSlot.Waist;
                    bool isAccessory = data.Slot == EquipmentSlot.Neck || data.Slot == EquipmentSlot.Face;

                    if (_typeFilter == 1 && !isWeapon) visible = false;
                    else if (_typeFilter == 2 && !isArmor) visible = false;
                    else if (_typeFilter == 3 && !isAccessory) visible = false;
                }

                _backpackItems_ui[i].Visible = visible;
            }
        }

        /// <summary>
        /// 更新装备对比面板：显示当前选中装备与已装备同槽位装备的属性差异。
        /// </summary>
        private void UpdateComparePanel()
        {
            if (_selectedIndex < 0 || _backpackData == null || _selectedIndex >= _backpackData.Length)
            {
                if (_compareCandidateName != null)
                    _compareCandidateName.Text = "候选·（未选择）";
                if (_compareCandidateAttr != null)
                    _compareCandidateAttr.Text = "--";
                if (_compareDeltaLabel != null)
                    _compareDeltaLabel.Text = "";
                return;
            }

            var candidate = _backpackData[_selectedIndex];
            if (_compareCandidateName != null)
                _compareCandidateName.Text = $"候选·{candidate.Name}";

            if (_compareCandidateAttr != null)
                _compareCandidateAttr.Text = $"攻击 +{120 + candidate.Level * 3}\n暴击 +{candidate.Level % 10}%";

            if (_compareDeltaLabel != null)
                _compareDeltaLabel.Text = "+24 / -3%";

            // 同步当前装备名称（取该槽位已装备项）
            if (_compareCurrentName != null)
            {
                if (_equippedSlots != null && _equippedSlots.TryGetValue(candidate.Slot, out int equippedIdx) &&
                    equippedIdx >= 0 && equippedIdx < _backpackData.Length)
                {
                    _compareCurrentName.Text = $"当前·{_backpackData[equippedIdx].Name}";
                }
                else
                {
                    _compareCurrentName.Text = "当前·（空槽）";
                }
            }
        }

        /// <summary>
        /// SubTask 10.4：更换装备按钮点击处理。
        /// 将当前选中装备穿戴到对应装备槽，更新已装备字典与属性总览。
        /// </summary>
        /// <param name="button">触发事件的按钮（未使用）</param>
        private void OnSwapClicked(Button button)
        {
            if (_selectedIndex < 0 || _backpackData == null || _selectedIndex >= _backpackData.Length)
                return;

            var equip = _backpackData[_selectedIndex];
            var slot = equip.Slot;

            // 更新已装备字典
            if (_equippedSlots == null)
                _equippedSlots = new Dictionary<EquipmentSlot, int>();
            _equippedSlots[slot] = _selectedIndex;

            // 更新装备槽 InkCell 品质色
            int slotIndex = System.Array.IndexOf(DisplayedSlots, slot);
            if (slotIndex >= 0 && _equipmentCells != null && slotIndex < _equipmentCells.Length)
            {
                if (_equipmentCells[slotIndex] != null)
                    _equipmentCells[slotIndex].Quality = equip.Quality;
            }

            // 更新属性总览（mock：装备后属性增加）
            UpdateAttributeOverview();

            // 更新摘要
            UpdateSummary();

            // 触发导航请求（mock：通知外部装备已变更）
            try
            {
                NavigationRequested?.Invoke("equipment-changed");
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[MenuEquipmentPage] NavigationRequested 触发失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 更新属性总览数值（mock：装备后数值增加）。
        /// </summary>
        private void UpdateAttributeOverview()
        {
            if (_attrRows == null || _attrValueLabels == null)
                return;

            // mock：每次装备后攻击 +10，防御 +5
            for (int i = 0; i < _attrRows.Length; i++)
            {
                if (_attrValueLabels[i] == null)
                    continue;

                if (i == 0) // 攻击
                {
                    int v = ParseInt(_attrRows[i].Value) + 10;
                    _attrRows[i].Value = v.ToString();
                    _attrValueLabels[i].Text = _attrRows[i].Value;
                }
                else if (i == 1) // 防御
                {
                    int v = ParseInt(_attrRows[i].Value) + 5;
                    _attrRows[i].Value = v.ToString();
                    _attrValueLabels[i].Text = _attrRows[i].Value;
                }
            }
        }

        /// <summary>
        /// 更新底部摘要栏的已装备数量与套装状态。
        /// </summary>
        private void UpdateSummary()
        {
            if (_equippedSlots == null || _summaryCountLabel == null)
                return;

            int count = _equippedSlots.Count;
            _summaryCountLabel.Text = $"{count}/8";

            if (_summarySetLabel != null)
                _summarySetLabel.Text = $"套装 {count}/4 · 龙纹之力";
        }

        /// <summary>
        /// 获取指定槽位已装备装备的品质色，未装备返回 Common。
        /// </summary>
        /// <param name="slot">装备槽位</param>
        /// <returns>已装备装备的品质，未装备返回 Common</returns>
        private InkWashTheme.InkQuality GetSlotQuality(EquipmentSlot slot)
        {
            if (_equippedSlots != null && _equippedSlots.TryGetValue(slot, out int idx) &&
                _backpackData != null && idx >= 0 && idx < _backpackData.Length)
            {
                return _backpackData[idx].Quality;
            }
            return InkWashTheme.InkQuality.Common;
        }

        /// <summary>
        /// 将 EquipmentData.Quality（0-5）映射到 <see cref="InkWashTheme.InkQuality"/>（0-4），5 钳制为 Legendary。
        /// </summary>
        /// <param name="quality">装备品质（0=白,1=绿,2=蓝,3=紫,4=橙,5=红）</param>
        /// <returns>对应的 InkQuality 枚举值</returns>
        private static InkWashTheme.InkQuality MapEquipmentQuality(int quality)
        {
            int clamped = Mathf.Clamp(quality, 0, 4);
            return (InkWashTheme.InkQuality)clamped;
        }

        /// <summary>
        /// 从字符串解析整数，失败返回 0。
        /// </summary>
        /// <param name="text">待解析的文本</param>
        /// <returns>解析得到的整数，失败返回 0</returns>
        private static int ParseInt(string text)
        {
            if (string.IsNullOrEmpty(text))
                return 0;
            int result = 0;
            foreach (char c in text)
            {
                if (c >= '0' && c <= '9')
                    result = result * 10 + (c - '0');
                else if (result > 0)
                    break;
            }
            return result;
        }

        // ===================================================================
        // 内部类
        // =======================================================================

        /// <summary>
        /// 可点击的装备列表项。
        /// 继承 <see cref="InkListItem"/>，覆写 <see cref="OnMouseDown"/>/<see cref="OnMouseUp"/>
        /// 处理点击判定，暴露 <see cref="Clicked"/> 事件供页面订阅。
        /// </summary>
        private class EquipmentListItem : InkListItem
        {
            /// <summary>鼠标是否按下（用于点击释放判定）</summary>
            private bool _isMouseDown;

            /// <summary>
            /// 点击事件。鼠标左键在项范围内按下并释放时触发。
            /// </summary>
            public event Action Clicked;

            /// <summary>
            /// 绑定的装备索引（用于点击时回传到页面）。
            /// </summary>
            public int EquipmentIndex { get; set; }

            /// <inheritdoc />
            public override bool OnMouseDown(Float2 location, MouseButton button)
            {
                base.OnMouseDown(location, button);
                if (button == MouseButton.Left)
                    _isMouseDown = true;
                return true;
            }

            /// <inheritdoc />
            public override bool OnMouseUp(Float2 location, MouseButton button)
            {
                base.OnMouseUp(location, button);
                if (button == MouseButton.Left && _isMouseDown)
                {
                    _isMouseDown = false;
                    if (location.X >= 0f && location.X <= Width &&
                        location.Y >= 0f && location.Y <= Height)
                    {
                        Clicked?.Invoke();
                    }
                }
                return true;
            }
        }

        /// <summary>
        /// 纸娃娃角色预览控件。
        /// 通过覆写 <see cref="Draw"/> 绘制简化角色轮廓（头部椭圆 + 身体矩形 + 四肢），
        /// 使用 <see cref="InkWashTheme.Paper"/> 低透明度色，作为装备槽的视觉背景。
        /// </summary>
        private class PaperDollPreview : ContainerControl
        {
            /// <summary>轮廓填充色（纸色低透明度）</summary>
            private static readonly Color FillColor = new Color(
                InkWashTheme.Paper.R, InkWashTheme.Paper.G,
                InkWashTheme.Paper.B, 0.05f);

            /// <summary>
            /// 构造函数：透明背景，不裁剪子控件。
            /// </summary>
            public PaperDollPreview()
            {
                BackgroundColor = Color.Transparent;
                ClipChildren = false;
            }

            /// <inheritdoc />
            public override void Draw()
            {
                base.Draw();

                if (!Visible || Width <= 0f || Height <= 0f)
                    return;

                // 以 Width/Height 为画布，按 viewBox 200x420 比例绘制
                float sx = Width / 200f;
                float sy = Height / 420f;
                float cx = Width * 0.5f;

                // 地面阴影
                InkRenderHelper.FillCircle(
                    new Float2(cx, 340f * sy),
                    60f * sx, new Color(InkWashTheme.Abyss.R, InkWashTheme.Abyss.G, InkWashTheme.Abyss.B, 0.4f));

                // 头部（椭圆近似为圆）
                InkRenderHelper.FillCircle(
                    new Float2(cx, 32f * sy), 18f * sx, FillColor);

                // 颈部
                Render2D.FillRectangle(
                    new Rectangle(cx - 6f * sx, 52f * sy, 12f * sx, 10f * sy),
                    FillColor);

                // 肩部
                Render2D.FillRectangle(
                    new Rectangle(cx - 40f * sx, 68f * sy, 80f * sx, 14f * sy),
                    FillColor);

                // 胸部
                Render2D.FillRectangle(
                    new Rectangle(cx - 38f * sx, 82f * sy, 76f * sx, 88f * sy),
                    FillColor);

                // 左臂
                Render2D.FillRectangle(
                    new Rectangle(cx - 52f * sx, 82f * sy, 14f * sx, 83f * sy),
                    FillColor);

                // 右臂
                Render2D.FillRectangle(
                    new Rectangle(cx + 38f * sx, 82f * sy, 14f * sx, 83f * sy),
                    FillColor);

                // 腰部
                Render2D.FillRectangle(
                    new Rectangle(cx - 32f * sx, 170f * sy, 64f * sx, 25f * sy),
                    FillColor);

                // 左腿
                Render2D.FillRectangle(
                    new Rectangle(cx - 28f * sx, 195f * sy, 16f * sx, 130f * sy),
                    FillColor);

                // 右腿
                Render2D.FillRectangle(
                    new Rectangle(cx + 12f * sx, 195f * sy, 16f * sx, 130f * sy),
                    FillColor);

                // 左脚
                InkRenderHelper.FillCircle(
                    new Float2(cx - 26f * sx, 328f * sy), 12f * sx, FillColor);

                // 右脚
                InkRenderHelper.FillCircle(
                    new Float2(cx + 26f * sx, 328f * sy), 12f * sx, FillColor);
            }
        }

        /// <summary>
        /// mock 装备数据结构。
        /// </summary>
        private class MockEquipment
        {
            /// <summary>装备 ID</summary>
            public int Id;

            /// <summary>装备名称</summary>
            public string Name;

            /// <summary>品质（映射到 InkQuality）</summary>
            public InkWashTheme.InkQuality Quality;

            /// <summary>装备槽位</summary>
            public EquipmentSlot Slot;

            /// <summary>需求等级</summary>
            public int Level;

            /// <summary>类型描述（如"长刀"/"胸甲"）</summary>
            public string Type;
        }

        /// <summary>
        /// mock 属性总览行数据。
        /// </summary>
        private class AttrRow
        {
            /// <summary>属性名</summary>
            public string Name;

            /// <summary>属性值</summary>
            public string Value;

            /// <summary>差值文本</summary>
            public string Delta;

            /// <summary>
            /// 差值方向：true=上升(翡翠色)，false=下降(朱红色)，null=持平(三级灰)。
            /// </summary>
            public bool? DeltaUp;
        }

        /// <summary>
        /// mock 套装数据。
        /// </summary>
        private class MockSet
        {
            /// <summary>套装名称</summary>
            public string Name;

            /// <summary>当前件数</summary>
            public int Current;

            /// <summary>总件数</summary>
            public int Total;

            /// <summary>2 件套加成描述</summary>
            public string Bonus2;

            /// <summary>4 件套加成描述</summary>
            public string Bonus4;
        }
    }
}
