using System;
using System.Collections.Generic;
using FlaxEngine;
using FlaxEngine.GUI;
using Game.Character.Attributes;
using HundunWorld.Game.Character;
using HundunWorld.Game.Equipment;
using HundunWorld.Game.Inventory;
using HundunWorld.Game.Services;
using HundunWorld.Game.UI;
using HundunWorld.Game.UI.Ink;
using HundunWorld.Game.UI.Ink.Components;
using HundunWorld.Game.UI.StyleSystem;

namespace HundunWorld.Game.UI.Ink.Pages.Appearance
{
    /// <summary>
    /// 外观页实现 —— 对应设计稿 <c>hundun-yy-ui/pages/menu-appearance.html</c>（dom-id: nav-appearance）。
    /// <para>页面结构（1920×1080 设计坐标系，整体缩放由 <see cref="InkPageShell"/> 内容层承担）：</para>
    /// <list type="bullet">
    ///   <item>左侧 240px 导航栏：混沌世界 Logo + 角色 / 江湖 / 系统 三组导航项（「外观」为当前激活项）</item>
    ///   <item>顶部 60px 角色信息条：返回战斗 + 头像「无」+ 无名侠 / Lv.42 / 逍遥派 + 铜钱 12,450 / 银两 328</item>
    ///   <item>主区左 45%：外观预览（竖排标题「外观预览」+ 人物剪影 + 左旋 / 右旋 / 放大 / 缩小 / 重置视角 工具条）</item>
    ///   <item>主区右 55%：分类页签（头饰 / 服装 / 披风 / 武器外观 / 坐骑）+ 3×2 套装卡片 + 染色区（主色 / 辅色 / 点缀 三行各 8 色 + 重置 / 应用染色）</item>
    /// </list>
    /// <para>所有颜色与字体一律取自 <see cref="InkWashTheme"/> Token，不硬编码色值；不引入外部依赖。</para>
    /// </summary>
    public class MenuAppearancePage : ContainerControl, IInkPage, IInkNavigationSource
    {
        // ==================================================================
        // 设计稿数据（唯一事实来源：hundun-yy-ui/pages/menu-appearance.html）
        // ==================================================================

        /// <summary>分类页签文案（设计稿 .tab-item 五项）</summary>
        private static readonly string[] TabNames = { "头饰", "服装", "披风", "武器外观", "坐骑" };

        /// <summary>默认激活页签索引 —— 设计稿激活「服装」</summary>
        private const int DefaultTabIndex = 1;

        /// <summary>套装卡片名称（设计稿 3×2 网格，默认选中「朱雀翎衫」）</summary>
        private static readonly string[] SetNames = { "青衫客服", "墨衣夜行", "朱雀翎衫", "玉虚道袍", "赤焰战甲", "玄武铁铠" };

        /// <summary>套装品质文案（设计稿：凡品 / 珍品 / 仙品 / 灵品 / 神品 / 仙品）</summary>
        private static readonly string[] SetQualities = { "凡品", "珍品", "仙品", "灵品", "神品", "仙品" };

        /// <summary>默认选中套装索引 —— 设计稿选中「朱雀翎衫」</summary>
        private const int DefaultSetIndex = 2;

        /// <summary>套装缩略图底色（设计稿各卡 thumb 渐变主色，映射到主题 Token）</summary>
        private static readonly Color[] SetThumbColors =
        {
            InkWashTheme.BaseElevated,       // 青衫客服
            InkWashTheme.ElementWater,       // 墨衣夜行（藏青）
            InkWashTheme.GoldDeep,           // 朱雀翎衫（鎏金深）
            InkWashTheme.JadePrimary,        // 玉虚道袍（玉色）
            InkWashTheme.VermilionFaded,     // 赤焰战甲（朱红褪色）
            InkWashTheme.GoldDeep            // 玄武铁铠
        };

        /// <summary>套装品质配色（对应设计稿 .tag-common / .tag-rare / .ink-tag-brand / .tag-jade / .ink-tag-vermilion）</summary>
        private static readonly Color[] SetQualityColors =
        {
            InkWashTheme.PaperDark,          // 凡品
            InkWashTheme.QualityRare,        // 珍品
            InkWashTheme.TextBrand,          // 仙品
            InkWashTheme.JadeBright,         // 灵品
            InkWashTheme.TextVermilion       // 神品
        };

        /// <summary>套装品质 → <see cref="SetQualityColors"/> 索引</summary>
        private static readonly int[] SetQualityColorIndexes = { 0, 1, 2, 3, 4, 2 };

        /// <summary>染色行标题（设计稿：主色 / 辅色 / 点缀）</summary>
        private static readonly string[] DyeRowTitles = { "主色", "辅色", "点缀" };

        /// <summary>染色行说明（设计稿：大面积 / 点缀面 / 纹饰）</summary>
        private static readonly string[] DyeRowCaptions = { "大面积", "点缀面", "纹饰" };

        /// <summary>染色色点名称（设计稿 title 属性，作为控件 Tooltip 落地）</summary>
        private static readonly string[][] DyeColorNames =
        {
            new[] { "朱红", "墨黑", "纸白", "金色", "玉色", "藏青", "绛紫", "青灰" },
            new[] { "赭石", "金色", "黛绿", "银灰", "茶褐", "月白", "烟灰", "胭脂" },
            new[] { "金色", "朱红", "玉色", "银白", "翠绿", "琥珀", "珊瑚", "碧蓝" }
        };

        /// <summary>染色色点色值（设计稿十六进制色映射到主题 Token，禁止硬编码）</summary>
        private static readonly Color[][] DyeColors =
        {
            new[]
            {
                InkWashTheme.VermilionPrimary,   // 朱红
                InkWashTheme.BaseSecondary,      // 墨黑
                InkWashTheme.PaperBright,        // 纸白
                InkWashTheme.GoldPrimary,        // 金色
                InkWashTheme.JadePrimary,        // 玉色
                InkWashTheme.ElementWater,       // 藏青
                InkWashTheme.QualityEpic,        // 绛紫
                InkWashTheme.Info                // 青灰
            },
            new[]
            {
                InkWashTheme.BronzePrimary,      // 赭石
                InkWashTheme.GoldPrimary,        // 金色
                InkWashTheme.JadeDeep,           // 黛绿
                InkWashTheme.PaperDark,          // 银灰
                InkWashTheme.ElementEarth,       // 茶褐
                InkWashTheme.PaperAged,          // 月白
                InkWashTheme.QualityCommon,      // 烟灰
                InkWashTheme.BloodPrimary        // 胭脂
            },
            new[]
            {
                InkWashTheme.GoldPrimary,        // 金色
                InkWashTheme.VermilionPrimary,   // 朱红
                InkWashTheme.JadePrimary,        // 玉色
                InkWashTheme.PaperBright,        // 银白
                InkWashTheme.SpringGreenPrimary, // 翠绿
                InkWashTheme.GoldDeep,           // 琥珀
                InkWashTheme.BloodBright,        // 珊瑚
                InkWashTheme.ElementWater        // 碧蓝
            }
        };

        /// <summary>染色各行默认选中索引（设计稿 dye-primary-1 / dye-secondary-2 / dye-accent-3）</summary>
        private static readonly int[] DyeDefaultIndexes = { 0, 1, 2 };

        /// <summary>左侧导航分组标题</summary>
        private static readonly string[] NavGroupTitles = { "角色", "江湖", "系统" };

        /// <summary>左侧导航各项文案（设计稿三组，顺序与设计稿一致）</summary>
        private static readonly string[][] NavGroupLabels =
        {
            new[] { "角色", "装备", "外观", "备战", "门派", "个人信息", "时间" },
            new[] { "任务", "博物志", "武林录", "营生", "组队", "邮箱", "商店" },
            new[] { "设置" }
        };

        /// <summary>左侧导航跳转目标（与 <see cref="InkPageDomIds"/> 对齐）</summary>
        private static readonly string[][] NavGroupTargets =
        {
            new[]
            {
                InkPageDomIds.NavCharacterV2, InkPageDomIds.NavEquipment, InkPageDomIds.NavAppearance,
                InkPageDomIds.NavBattlePrep, InkPageDomIds.NavSect, InkPageDomIds.NavPersonalInfo,
                InkPageDomIds.NavTime
            },
            new[]
            {
                InkPageDomIds.NavQuests, InkPageDomIds.NavBestiary, InkPageDomIds.NavMartialRecord,
                InkPageDomIds.NavLivelihood, InkPageDomIds.NavTeam, InkPageDomIds.NavMail,
                InkPageDomIds.NavShop
            },
            new[] { InkPageDomIds.NavSettings }
        };

        /// <summary>左侧导航禁用项（设计稿 nav-item-disabled：商店、设置）</summary>
        private static readonly bool[][] NavGroupDisabled =
        {
            new[] { false, false, false, false, false, false, false },
            new[] { false, false, false, false, false, false, true },
            new[] { true }
        };

        // ==================================================================
        // 布局常量 —— 与设计稿像素值一一对应
        // ==================================================================

        /// <summary>左侧导航栏宽度（设计稿 w-[240px]）</summary>
        private const float SidebarWidth = 240f;

        /// <summary>顶部角色信息条高度（设计稿 h-[60px]）</summary>
        private const float TopHeaderHeight = 60f;

        /// <summary>内容区内边距（设计稿 p-6 = 24px）</summary>
        private const float ContentPadding = 24f;

        /// <summary>左侧预览区占比（设计稿 w-[45%]）</summary>
        private const float PreviewColumnRatio = 0.45f;

        /// <summary>页签栏高度</summary>
        private const float TabBarHeight = 40f;

        /// <summary>页签激活下划线高度</summary>
        private const float TabUnderlineHeight = 2f;

        /// <summary>套装卡片高度</summary>
        private const float SetCardHeight = 72f;

        /// <summary>套装卡片间距（设计稿 gap-3）</summary>
        private const float SetCardGap = 12f;

        /// <summary>套装网格列数（设计稿 grid-cols-3）</summary>
        private const int SetCardColumns = 3;

        /// <summary>染色面板内边距（设计稿 padding: 20px）</summary>
        private const float DyePanelPadding = 20f;

        /// <summary>染色行标签列宽（设计稿 w-16）</summary>
        private const float DyeRowLabelWidth = 64f;

        /// <summary>染色色点直径（设计稿 .dye-dot 28px）</summary>
        private const float DyeDotSize = 28f;

        /// <summary>染色色点水平间距（设计稿 gap-2）</summary>
        private const float DyeDotGap = 8f;

        /// <summary>染色行高</summary>
        private const float DyeRowHeight = 44f;

        /// <summary>染色行间距</summary>
        private const float DyeRowGap = 14f;

        /// <summary>预览工具条高度</summary>
        private const float PreviewToolbarHeight = 48f;

        /// <summary>左侧导航项高度（设计稿 h-[40px]）</summary>
        private const float NavItemHeight = 40f;

        // ==================================================================
        // 控件字段
        // ==================================================================

        private Float2 _screenSize;

        private InkPanelSolid _sidebar;
        private Label _sidebarLogo;
        private InkDivider _sidebarLogoDivider;
        private readonly List<SidebarRow> _sidebarRows = new List<SidebarRow>();

        private Panel _mainArea;
        private Panel _topHeader;
        private InkDivider _topHeaderDivider;
        private InkButton _backButton;
        private InkAvatar _playerAvatar;
        private Label _playerNameLabel;
        private InkTag _playerLevelTag;
        private Label _playerSectLabel;
        private InkColorDot _coinDot;
        private InkColorDot _ingotDot;
        private Label _currencyCoinLabel;
        private Label _currencyIngotLabel;

        private InkPanelSolid _previewPanel;
        private InkVerticalTitle _previewTitle;
        private CharacterPreview3D _preview3D;
        private Panel _previewToolbar;
        private InkButton _btnRotateLeft;
        private InkButton _btnRotateRight;
        private Panel _previewDivider1;
        private InkButton _btnZoomIn;
        private InkButton _btnZoomOut;
        private Panel _previewDivider2;
        private InkButton _btnResetView;

        private Panel _tabBar;
        private InkDivider _tabBarDivider;
        private readonly List<AppearanceTabButton> _tabButtons = new List<AppearanceTabButton>();
        private readonly List<Panel> _tabUnderlines = new List<Panel>();
        private int _activeTabIndex = DefaultTabIndex;

        private Panel _setGrid;
        private readonly List<SetCard> _setCards = new List<SetCard>();
        private int _selectedSetIndex = DefaultSetIndex;

        /// <summary>每张套装卡对应的外观装备模板 ID；0 = 「默认外观（卸下）」卡。</summary>
        private readonly List<int> _appearanceCardIds = new List<int>();

        /// <summary>当前筛出的外观装备（按角色性别）。</summary>
        private readonly List<EquipmentData> _appearanceItems = new List<EquipmentData>();

        private InkPanel _dyePanel;
        private Label _dyeTitleLabel;
        private Label _dyeSeparatorLabel;
        private Label _dyeTargetLabel;
        private Label _dyeHintLabel;
        private readonly List<Label> _dyeRowTitleLabels = new List<Label>();
        private readonly List<Label> _dyeRowCaptionLabels = new List<Label>();
        private readonly List<InkColorDot> _dyeDots = new List<InkColorDot>();
        private readonly List<InkDivider> _dyeRowDividers = new List<InkDivider>();
        private readonly int[] _dyeSelectedIndexes = new int[DyeRowTitles.Length];
        private InkDivider _dyeFooterDivider;
        private InkButton _btnResetDye;
        private InkButton _btnApplyDye;

        /// <summary>
        /// 导航跳转请求。由 <c>MainUIManager.HookNavigationRequested</c> 反射订阅后转交 <see cref="InkPageRouter"/>。
        /// </summary>
        public event Action<string> NavigationRequested;

        /// <summary>
        /// 构造函数：构建全部子控件并应用布局与初始数据。
        /// </summary>
        public MenuAppearancePage()
        {
            _screenSize = FlaxEngine.Screen.Size;
            if (_screenSize.X <= 0f || _screenSize.Y <= 0f)
            {
                _screenSize = new Float2(UILayout.RefWidth, UILayout.RefHeight);
            }

            AnchorPreset = AnchorPresets.StretchAll;
            BackgroundColor = Color.Transparent;
            ClipChildren = false;
            AutoFocus = false;
            Location = Float2.Zero;
            Size = _screenSize;

            // 染色默认选中项（朱红 / 金色 / 玉色）
            for (int i = 0; i < _dyeSelectedIndexes.Length; i++)
            {
                _dyeSelectedIndexes[i] = DyeDefaultIndexes[i];
            }

            try
            {
                BuildBackground();
                BuildSidebar();
                BuildMainArea();
                BuildTopHeader();
                BuildPreviewPanel();
                BuildTabBar();
                BuildSetGrid();
                BuildDyePanel();

                ApplyLayout();
                RefreshAllData();

                // 顶栏身份接真实数据
                InkPlayerHeaderBinder.Bind(_playerNameLabel, _playerLevelTag);
                InkPlayerHeaderBinder.BindSect(_playerSectLabel);

                // 外观穿戴以服务端权威背包/装备快照为准：快照一到就重建卡片并标注已穿戴。
                PlayerInventoryService.Instance.Changed += OnInventoryChanged;
                PlayerInventoryService.Instance.OperationFinished += OnAppearanceOperationFinished;
                CharacterEquipmentManager.AppearanceChanged += OnAppearanceChanged;
                if (!PlayerInventoryService.Instance.HasServerData)
                    PlayerInventoryService.Instance.RequestFullRefresh();

                BindPreview();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[MenuAppearancePage] 初始化失败: {ex.Message}");
            }
        }

        /// <inheritdoc />
        public override void OnDestroy()
        {
            PlayerInventoryService.Instance.Changed -= OnInventoryChanged;
            PlayerInventoryService.Instance.OperationFinished -= OnAppearanceOperationFinished;
            CharacterEquipmentManager.AppearanceChanged -= OnAppearanceChanged;
            base.OnDestroy();
        }

        // ==================================================================
        // 背景层
        // ==================================================================

        /// <summary>
        /// 构建全局背景层与暗角晕影层（设计稿 .ink-bg-layer / .ink-vignette）。
        /// </summary>
        private void BuildBackground()
        {
            // 背景层/暗角由外壳统一提供（本页为沉浸式全屏页），页面不再自绘：
            // 原实现（不透明底 BaseDefault + 全屏 Scrim）与外壳层叠加会得到双份墨底 + 双层暗角。
        }

        // ==================================================================
        // 左侧导航栏
        // ==================================================================

        /// <summary>
        /// 构建左侧导航栏：Logo + 三组导航（角色 / 江湖 / 系统），「外观」为激活项，商店与设置禁用。
        /// </summary>
        private void BuildSidebar()
        {
            _sidebar = new InkPanelSolid
            {
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = InkWashTheme.BaseSecondary,
                Radius = InkWashTheme.RadiusNone,
                Parent = this
            };

            _sidebarLogo = new Label
            {
                Text = "混沌世界",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 20f),
                TextColor = InkWashTheme.GoldPrimary,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                Parent = _sidebar
            };

            _sidebarLogoDivider = new InkDivider
            {
                Height = 1f,
                Parent = _sidebar
            };

            for (int g = 0; g < NavGroupTitles.Length; g++)
            {
                if (g > 0)
                {
                    _sidebarRows.Add(new SidebarRow
                    {
                        Kind = SidebarRowKind.Divider,
                        Element = new InkDivider { Height = 1f, Parent = _sidebar },
                        GapBefore = InkWashTheme.Space4,
                        Step = InkWashTheme.Space4 + 1f
                    });
                }

                _sidebarRows.Add(new SidebarRow
                {
                    Kind = SidebarRowKind.GroupTitle,
                    Element = new Label
                    {
                        Text = NavGroupTitles[g],
                        Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                        TextColor = InkWashTheme.TextTertiary,
                        HorizontalAlignment = TextAlignment.Near,
                        VerticalAlignment = TextAlignment.Center,
                        Parent = _sidebar
                    },
                    GapBefore = g == 0 ? 0f : InkWashTheme.Space2,
                    Step = 24f
                });

                string[] labels = NavGroupLabels[g];
                string[] targets = NavGroupTargets[g];
                bool[] disabled = NavGroupDisabled[g];
                for (int i = 0; i < labels.Length; i++)
                {
                    bool isActive = targets[i] == InkPageDomIds.NavAppearance;
                    SidebarNavItem item = new SidebarNavItem(labels[i], isActive, disabled[i])
                    {
                        Parent = _sidebar
                    };

                    if (!isActive && !disabled[i])
                    {
                        string target = targets[i];
                        item.Clicked += () => OnNavigationItemClicked(target);
                    }

                    _sidebarRows.Add(new SidebarRow
                    {
                        Kind = SidebarRowKind.NavItem,
                        Element = item,
                        GapBefore = 0f,
                        Step = NavItemHeight + InkWashTheme.Space1
                    });
                }
            }
        }

        /// <summary>
        /// 左侧导航项点击处理：经 <see cref="NavigationRequested"/> 交由路由器跳转。
        /// </summary>
        /// <param name="domId">目标页面 dom-id</param>
        private void OnNavigationItemClicked(string domId)
        {
            try
            {
                NavigationRequested?.Invoke(domId);
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[MenuAppearancePage] NavigationRequested({domId}) 触发失败: {ex.Message}");
            }
        }

        // ==================================================================
        // 主区 / 顶部角色信息条
        // ==================================================================

        /// <summary>
        /// 构建主内容容器（位于左侧导航栏右侧）。
        /// </summary>
        private void BuildMainArea()
        {
            _mainArea = new Panel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = Color.Transparent,
                Parent = this
            };
        }

        /// <summary>
        /// 构建顶部角色信息条：返回战斗 + 头像 + 无名侠 / Lv.42 / 逍遥派 + 铜钱 / 银两。
        /// </summary>
        private void BuildTopHeader()
        {
            _topHeader = new Panel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = InkWashTheme.BaseTertiary,
                Parent = _mainArea
            };

            _topHeaderDivider = new InkDivider
            {
                Height = 1f,
                Parent = _topHeader
            };

            _backButton = new InkButton
            {
                Text = "← 返回战斗",
                TooltipText = "返回战斗",
                Variant = InkButtonVariant.Ghost,
                Width = 88f,
                Height = 36f,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                Parent = _topHeader
            };
            _backButton.Clicked += OnBackButtonClicked;

            _playerAvatar = new InkAvatar
            {
                Parent = _topHeader
            };
            new Label
            {
                Text = "无",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 16f),
                TextColor = InkWashTheme.GoldPrimary,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                // 头像固定 36×36：StretchAll 实测仍是 136×56，字会被裁掉
                Location = Float2.Zero,
                Size = new Vector2(36f, 36f),
                Parent = _playerAvatar
            };

            _playerNameLabel = new Label
            {
                Text = "—",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 16f),
                TextColor = InkWashTheme.PaperBright,
                VerticalAlignment = TextAlignment.Center,
                Parent = _topHeader
            };

            _playerLevelTag = new InkTag
            {
                Text = "—",
                TagVariant = InkTagVariant.Vermilion,
                Parent = _topHeader
            };

            _playerSectLabel = new Label
            {
                Text = "—",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                TextColor = InkWashTheme.PaperAged,
                VerticalAlignment = TextAlignment.Center,
                Parent = _topHeader
            };

            _coinDot = new InkColorDot(16f)
            {
                DotColor = InkWashTheme.BronzeBright,
                RingColor = InkWashTheme.BronzePrimary,
                Enabled = false,
                Parent = _topHeader
            };

            _currencyCoinLabel = new Label
            {
                Text = "铜钱 —",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 13f),
                TextColor = InkWashTheme.PaperBright,
                VerticalAlignment = TextAlignment.Center,
                Parent = _topHeader
            };

            _ingotDot = new InkColorDot(16f)
            {
                DotColor = InkWashTheme.PaperAged,
                RingColor = InkWashTheme.PaperDark,
                Enabled = false,
                Parent = _topHeader
            };

            _currencyIngotLabel = new Label
            {
                // 银两/元宝没有服务端快照（ShopClientService 只给 Gold），不摆设计稿的 328
                Text = "—",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 13f),
                TextColor = InkWashTheme.PaperBright,
                VerticalAlignment = TextAlignment.Center,
                Parent = _topHeader
            };
        }

        /// <summary>
        /// 返回战斗按钮点击处理：触发 <see cref="NavigationRequested"/>("back-hud")。
        /// </summary>
        private void OnBackButtonClicked()
        {
            OnNavigationItemClicked(InkPageDomIds.BackOneLevel);
        }

        // ==================================================================
        // 左侧预览区
        // ==================================================================

        /// <summary>
        /// 构建外观预览面板：竖排标题「外观预览」+ 人物剪影 + 底部视角工具条。
        /// </summary>
        private void BuildPreviewPanel()
        {
            _previewPanel = new InkPanelSolid
            {
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = InkWashTheme.BaseTertiary,
                Radius = InkWashTheme.RadiusLg,
                Parent = _mainArea
            };

            _previewTitle = new InkVerticalTitle
            {
                Text = "外观预览",
                FontSize = 18f,
                Parent = _previewPanel
            };

            // 真实 3D 预览：读本地玩家 Actor 当前的 SkinnedModel（外观换的是整套模型，
            // 所以这里能直接看到外形变化，而不是设计稿里的静态剪影）。
            _preview3D = new CharacterPreview3D
            {
                Parent = _previewPanel
            };

            BuildPreviewToolbar();
        }

        /// <summary>
        /// 构建预览工具条：左旋 / 右旋 | 放大 / 缩小 | 重置视角（设计稿 .preview-ctrl 六控件）。
        /// </summary>
        private void BuildPreviewToolbar()
        {
            _previewToolbar = new Panel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = Color.Transparent,
                Parent = _previewPanel
            };

            _btnRotateLeft = CreateToolbarButton("↺", "左旋", OnRotateLeftClicked);
            _btnRotateRight = CreateToolbarButton("↻", "右旋", OnRotateRightClicked);
            _previewDivider1 = CreateToolbarDivider();
            _btnZoomIn = CreateToolbarButton("+", "放大", OnZoomInClicked);
            _btnZoomOut = CreateToolbarButton("−", "缩小", OnZoomOutClicked);
            _previewDivider2 = CreateToolbarDivider();
            _btnResetView = CreateToolbarButton("⟲", "重置视角", OnResetViewClicked);
        }

        /// <summary>
        /// 创建工具条图标按钮（36×36 幽灵按钮 + 设计稿 title 文案作为 Tooltip）。
        /// </summary>
        /// <param name="glyph">按钮字形</param>
        /// <param name="tooltip">悬停提示（设计稿 title 属性）</param>
        /// <param name="handler">点击回调</param>
        /// <returns>按钮控件</returns>
        private InkButton CreateToolbarButton(string glyph, string tooltip, Action handler)
        {
            InkButton button = new InkButton
            {
                Text = glyph,
                TooltipText = tooltip,
                Variant = InkButtonVariant.Ghost,
                ButtonSize = InkButtonSize.Sm,
                Width = 36f,
                Height = 36f,
                Parent = _previewToolbar
            };
            button.Clicked += handler;
            return button;
        }

        /// <summary>
        /// 创建工具条分组竖线（设计稿 w-px h-5）。
        /// </summary>
        /// <returns>竖线面板</returns>
        private Panel CreateToolbarDivider()
        {
            return new Panel
            {
                Width = 1f,
                Height = 20f,
                BackgroundColor = InkWashTheme.TextTertiary,
                Parent = _previewToolbar
            };
        }

        /// <summary>左旋按钮点击处理。</summary>
        private void OnRotateLeftClicked()
        {
            _preview3D?.RotateByDegrees(-15f);
        }

        /// <summary>右旋按钮点击处理。</summary>
        private void OnRotateRightClicked()
        {
            _preview3D?.RotateByDegrees(15f);
        }

        /// <summary>放大按钮点击处理。</summary>
        private void OnZoomInClicked()
        {
            _preview3D?.ZoomBy(0.88f);
        }

        /// <summary>缩小按钮点击处理。</summary>
        private void OnZoomOutClicked()
        {
            _preview3D?.ZoomBy(1f / 0.88f);
        }

        /// <summary>重置视角按钮点击处理。</summary>
        private void OnResetViewClicked()
        {
            _preview3D?.ResetView();
        }

        // ==================================================================
        // 分类页签
        // ==================================================================

        /// <summary>
        /// 构建分类页签栏（头饰 / 服装 / 披风 / 武器外观 / 坐骑），默认激活「服装」。
        /// </summary>
        private void BuildTabBar()
        {
            _tabBar = new Panel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = Color.Transparent,
                Parent = _mainArea
            };

            for (int i = 0; i < TabNames.Length; i++)
            {
                int index = i;
                AppearanceTabButton tab = new AppearanceTabButton
                {
                    Text = TabNames[i],
                    Variant = InkButtonVariant.Ghost,
                    Height = TabBarHeight - TabUnderlineHeight,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 14f),
                    Parent = _tabBar
                };
                tab.IsActive = index == _activeTabIndex;
                tab.Clicked += () => OnTabClicked(index);
                _tabButtons.Add(tab);

                _tabUnderlines.Add(new Panel
                {
                    Height = TabUnderlineHeight,
                    BackgroundColor = InkWashTheme.GoldPrimary,
                    Parent = _tabBar
                });
            }

            _tabBarDivider = new InkDivider
            {
                Height = 1f,
                Parent = _tabBar
            };
        }

        /// <summary>
        /// 页签点击处理：切换激活页签并刷新页签样式。
        /// </summary>
        /// <param name="index">页签索引</param>
        private void OnTabClicked(int index)
        {
            _activeTabIndex = index;
            RefreshTabStyles();
            RebuildSetCards();
        }

        /// <summary>
        /// 刷新页签激活态（激活项品牌金字 + 底部 2px 金线，未激活项褪色纸色）。
        /// </summary>
        private void RefreshTabStyles()
        {
            for (int i = 0; i < _tabButtons.Count; i++)
            {
                bool isActive = i == _activeTabIndex;
                _tabButtons[i].IsActive = isActive;
                if (i < _tabUnderlines.Count)
                {
                    _tabUnderlines[i].Visible = isActive;
                }
            }
        }

        // ==================================================================
        // 套装卡片网格
        // ==================================================================

        /// <summary>
        /// 构建套装卡片网格：数据来自 <see cref="EquipmentDatabase.GetAppearanceEquipments"/>，
        /// 末尾追加一张「默认外观（卸下）」卡。卡片点击即穿戴/卸下，不再使用设计稿的静态套装名。
        /// </summary>
        private void BuildSetGrid()
        {
            _setGrid = new Panel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = Color.Transparent,
                Parent = _mainArea
            };

            RebuildSetCards();
        }

        /// <summary>
        /// 按当前角色性别重建外观卡片，并标注「已穿戴 / 未拥有 / 品质」。
        /// 服务端快照变化会走这里，因此不缓存任何 item 实例。
        /// </summary>
        private void RebuildSetCards()
        {
            if (_setGrid == null) return;

            int gender = ResolvePlayerGender();
            _appearanceItems.Clear();
            _appearanceItems.AddRange(EquipmentDatabase.GetAppearanceEquipments(gender));

            _setGrid.DisposeChildren();
            _setCards.Clear();
            _appearanceCardIds.Clear();

            // 当前只有整套外观资产（头/身/披风一体），因此数据只挂在「服装」页签；
            // 其余页签给一张提示卡，避免把同一套衣服重复列在头饰/披风/武器/坐骑下。
            if (_activeTabIndex != DefaultTabIndex)
            {
                var placeholder = new SetCard(
                    0,
                    $"{TabNames[_activeTabIndex]}外观",
                    "暂未开放",
                    InkWashTheme.TextTertiary,
                    InkWashTheme.BaseElevated)
                {
                    Parent = _setGrid
                };
                _setCards.Add(placeholder);
                _appearanceCardIds.Add(0);
                RefreshSetSelection();
                ApplyLayout();
                return;
            }

            int equippedId = GetEquippedAppearanceId();

            for (int i = 0; i < _appearanceItems.Count; i++)
            {
                var item = _appearanceItems[i];
                bool equipped = equippedId == item.Id;
                bool owned = IsAppearanceOwned(item.Id);
                string status = equipped ? "已穿戴" : (owned ? QualityName(item.Quality) : "未拥有");
                Color statusColor = equipped
                    ? InkWashTheme.GoldBright
                    : (owned ? QualityColor(item.Quality) : InkWashTheme.TextTertiary);

                var card = new SetCard(
                    i,
                    item.Name,
                    status,
                    statusColor,
                    ThumbColorFor(item))
                {
                    Parent = _setGrid
                };
                card.Selected += OnSetCardSelected;
                _setCards.Add(card);
                _appearanceCardIds.Add(item.Id);
            }

            // 末位：卸下外观，回到性别默认身体。
            var unequipCard = new SetCard(
                _setCards.Count,
                "默认外观",
                equippedId == 0 ? "已穿戴" : "卸下",
                equippedId == 0 ? InkWashTheme.GoldBright : InkWashTheme.QualityCommon,
                InkWashTheme.BaseElevated)
            {
                Parent = _setGrid
            };
            unequipCard.Selected += OnSetCardSelected;
            _setCards.Add(unequipCard);
            _appearanceCardIds.Add(0);

            RefreshSetSelection();
            ApplyLayout();
        }

        /// <summary>
        /// 让 3D 预览读取本地玩家 Actor 当前的整套模型；Actor 未就绪时按性别显示默认身体。
        /// </summary>
        private void BindPreview()
        {
            if (_preview3D == null) return;

            try
            {
                var component = HundunWorldGame.Instance?.LocalPlayerActor?
                    .GetScript<CharacterAttributesComponent>();
                if (component != null)
                    _preview3D.SetCharacter(component);
                else
                    _preview3D.SetBodyGender(ResolvePlayerGender());
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogWarning($"[MenuAppearancePage] 绑定 3D 预览失败: {ex.Message}");
            }
        }

        private void OnAppearanceChanged()
        {
            BindPreview();
        }

        /// <summary>当前玩家性别：优先看已穿外观的限定性别，其次从身体模型路径推断（Character_2 = 女）。</summary>
        private static int ResolvePlayerGender()
        {
            try
            {
                // 外观模型可能是中性的傀儡资产，路径里没有性别信息；已穿外观自己的 RequiredGender
                // 是最可靠的回退（例如女性穿着「流云霓裳」时再打开外观页）。
                int equippedId = GetEquippedAppearanceId();
                if (equippedId > 0)
                {
                    var equipped = EquipmentDatabase.GetEquipment(equippedId);
                    if (equipped != null && equipped.RequiredGender >= 0)
                        return equipped.RequiredGender;
                }

                var local = HundunWorldGame.Instance?.LocalPlayerActor;
                var model = local?.FindActor<AnimatedModel>() ?? local?.GetChild<AnimatedModel>();
                var path = model?.SkinnedModel?.Path;
                if (!string.IsNullOrEmpty(path) &&
                    path.IndexOf("Character_2", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return 1;
                }
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogWarning($"[MenuAppearancePage] 推断角色性别失败，按男性处理: {ex.Message}");
            }
            return 0;
        }

        /// <summary>当前外观槽已穿的模板 ID；未穿返回 0。</summary>
        private static int GetEquippedAppearanceId()
        {
            return PlayerInventoryService.Instance.TryGetEquipped(EquipmentSlot.Appearance, out var entry)
                ? entry.TemplateId
                : 0;
        }

        /// <summary>外观是否在背包里（当前已穿戴的也算拥有，卸下按钮单独处理）。</summary>
        private static bool IsAppearanceOwned(int templateId)
        {
            if (templateId <= 0) return true;
            if (GetEquippedAppearanceId() == templateId) return true;

            foreach (var item in PlayerInventoryService.Instance.Items)
            {
                if (item.TemplateId == templateId && item.Count > 0)
                    return true;
            }
            return false;
        }

        private static Color ThumbColorFor(EquipmentData item)
        {
            if (item == null) return InkWashTheme.BaseElevated;
            if (item.RequiredGender == 0) return InkWashTheme.ElementWater;
            if (item.RequiredGender == 1) return InkWashTheme.JadePrimary;
            return InkWashTheme.GoldDeep;
        }

        private static string QualityName(int quality) => quality switch
        {
            0 => "凡品",
            1 => "珍品",
            2 => "仙品",
            3 => "灵品",
            4 => "神品",
            _ => "神品"
        };

        private static Color QualityColor(int quality) => quality switch
        {
            0 => InkWashTheme.PaperDark,
            1 => InkWashTheme.QualityRare,
            2 => InkWashTheme.TextBrand,
            3 => InkWashTheme.JadeBright,
            _ => InkWashTheme.TextVermilion
        };

        /// <summary>
        /// 卡片点击：穿/卸外观。全部走 <see cref="PlayerInventoryService"/> 的服务端权威链路，
        /// 成功后由 Changed/OperationFinished 事件重建卡片；失败只提示，不改本地状态。
        /// </summary>
        /// <param name="index">卡片索引</param>
        private void OnSetCardSelected(int index)
        {
            if (index < 0 || index >= _appearanceCardIds.Count) return;

            int templateId = _appearanceCardIds[index];
            var service = PlayerInventoryService.Instance;
            int equippedId = GetEquippedAppearanceId();

            if (templateId <= 0)
            {
                if (equippedId == 0)
                {
                    UIHelper.ShowToast("当前已是默认外观", ToastType.Info);
                    return;
                }
                _ = service.RequestUnequipAsync(EquipmentSlot.Appearance);
                return;
            }

            if (equippedId == templateId)
            {
                _ = service.RequestUnequipAsync(EquipmentSlot.Appearance);
                return;
            }

            if (!IsAppearanceOwned(templateId))
            {
                UIHelper.ShowToast("尚未拥有该外观", ToastType.Warning);
                return;
            }

            _ = service.RequestEquipByTemplateAsync(templateId);
        }

        private void OnInventoryChanged()
        {
            BindPreview();   // Actor 晚于页面创建时，第一次快照到达后补绑
            RebuildSetCards();
        }

        private void OnAppearanceOperationFinished(ItemOperationResult result)
        {
            if (result.Operation != ItemOperationKind.Equip && result.Operation != ItemOperationKind.Unequip)
                return;

            if (!string.IsNullOrEmpty(result.Message))
                UIHelper.ShowToast(result.Message, result.Success ? ToastType.Success : ToastType.Warning);

            RebuildSetCards();
        }

        /// <summary>
        /// 刷新套装卡片选中态与染色区目标名称（选中卡 = 当前已穿戴外观）。
        /// </summary>
        private void RefreshSetSelection()
        {
            int equippedId = GetEquippedAppearanceId();
            _selectedSetIndex = _appearanceCardIds.IndexOf(equippedId);
            if (_selectedSetIndex < 0)
                _selectedSetIndex = _appearanceCardIds.Count - 1;   // 未穿外观 → 末位「默认外观」

            for (int i = 0; i < _setCards.Count; i++)
            {
                _setCards[i].IsSelected = i == _selectedSetIndex;
            }

            if (_dyeTargetLabel != null)
            {
                string name = equippedId > 0
                    ? EquipmentDatabase.GetEquipment(equippedId)?.Name
                    : null;
                _dyeTargetLabel.Text = string.IsNullOrEmpty(name) ? "默认外观" : name;
            }
        }

        // ==================================================================
        // 染色区
        // ==================================================================

        /// <summary>
        /// 构建染色区：标题行（染色 · 当前套装 + 操作提示）+ 三行色点（主色 / 辅色 / 点缀）+ 底部按钮。
        /// </summary>
        private void BuildDyePanel()
        {
            _dyePanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = InkWashTheme.Panel,
                Radius = InkWashTheme.RadiusLg,
                Parent = _mainArea
            };

            _dyeTitleLabel = new Label
            {
                Text = "染色",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 16f),
                TextColor = InkWashTheme.PaperBright,
                VerticalAlignment = TextAlignment.Center,
                Parent = _dyePanel
            };

            _dyeSeparatorLabel = new Label
            {
                Text = "·",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                TextColor = InkWashTheme.PaperAged,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                Parent = _dyePanel
            };

            _dyeTargetLabel = new Label
            {
                Text = "默认外观",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                TextColor = InkWashTheme.GoldBright,
                VerticalAlignment = TextAlignment.Center,
                Parent = _dyePanel
            };

            _dyeHintLabel = new Label
            {
                Text = "选择颜色后点击应用",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                TextColor = InkWashTheme.TextTertiary,
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
                Parent = _dyePanel
            };

            for (int row = 0; row < DyeRowTitles.Length; row++)
            {
                _dyeRowTitleLabels.Add(new Label
                {
                    Text = DyeRowTitles[row],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 14f),
                    TextColor = InkWashTheme.PaperBright,
                    VerticalAlignment = TextAlignment.Center,
                    Parent = _dyePanel
                });

                _dyeRowCaptionLabels.Add(new Label
                {
                    Text = DyeRowCaptions[row],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                    TextColor = InkWashTheme.TextTertiary,
                    VerticalAlignment = TextAlignment.Center,
                    Parent = _dyePanel
                });

                Color[] rowColors = DyeColors[row];
                string[] rowNames = DyeColorNames[row];
                for (int col = 0; col < rowColors.Length; col++)
                {
                    InkColorDot dot = new InkColorDot(DyeDotSize)
                    {
                        Row = row,
                        Column = col,
                        DotColor = rowColors[col],
                        Selected = _dyeSelectedIndexes[row] == col,
                        TooltipText = rowNames[col],
                        Parent = _dyePanel
                    };
                    dot.Clicked += OnDyeDotClicked;
                    _dyeDots.Add(dot);
                }

                if (row < DyeRowTitles.Length - 1)
                {
                    _dyeRowDividers.Add(new InkDivider
                    {
                        Height = 1f,
                        Parent = _dyePanel
                    });
                }
            }

            _dyeFooterDivider = new InkDivider
            {
                Height = 1f,
                Parent = _dyePanel
            };

            _btnResetDye = new InkButton
            {
                Text = "↺ 重置",
                Variant = InkButtonVariant.Ghost,
                ButtonSize = InkButtonSize.Md,
                Parent = _dyePanel
            };
            _btnResetDye.Clicked += OnResetDyeClicked;

            _btnApplyDye = new InkButton
            {
                Text = "√ 应用染色",
                Variant = InkButtonVariant.Vermilion,
                ButtonSize = InkButtonSize.Md,
                Parent = _dyePanel
            };
            _btnApplyDye.Clicked += OnApplyDyeClicked;
        }

        /// <summary>
        /// 色点点击处理：同行单选，刷新选中环。
        /// </summary>
        /// <param name="dot">被点击的色点</param>
        private void OnDyeDotClicked(InkColorDot dot)
        {
            if (dot == null || dot.Row < 0 || dot.Row >= _dyeSelectedIndexes.Length)
            {
                return;
            }

            _dyeSelectedIndexes[dot.Row] = dot.Column;
            RefreshDyeSelection();
        }

        /// <summary>
        /// 刷新染色色点选中态。
        /// </summary>
        private void RefreshDyeSelection()
        {
            for (int i = 0; i < _dyeDots.Count; i++)
            {
                InkColorDot dot = _dyeDots[i];
                if (dot.Row >= 0 && dot.Row < _dyeSelectedIndexes.Length)
                {
                    dot.Selected = dot.Column == _dyeSelectedIndexes[dot.Row];
                }
            }
        }

        /// <summary>
        /// 「重置」按钮点击处理：三行色点恢复设计稿默认选择。
        /// </summary>
        private void OnResetDyeClicked()
        {
            for (int i = 0; i < _dyeSelectedIndexes.Length; i++)
            {
                _dyeSelectedIndexes[i] = DyeDefaultIndexes[i];
            }

            RefreshDyeSelection();
        }

        /// <summary>
        /// 「应用染色」按钮点击处理：应用当前三行选色到目标套装。
        /// </summary>
        private void OnApplyDyeClicked()
        {
            string target = _selectedSetIndex >= 0 && _selectedSetIndex < SetNames.Length
                ? SetNames[_selectedSetIndex]
                : _dyeTargetLabel?.Text;

            FlaxEngine.Debug.Log($"[MenuAppearancePage] 应用染色 → {target}: " +
                $"{DyeColorNames[0][_dyeSelectedIndexes[0]]} / " +
                $"{DyeColorNames[1][_dyeSelectedIndexes[1]]} / " +
                $"{DyeColorNames[2][_dyeSelectedIndexes[2]]}");
        }

        // ==================================================================
        // 布局
        // ==================================================================

        /// <summary>
        /// 按当前尺寸重算全部子控件布局（1920×1080 设计坐标系）。
        /// </summary>
        private void ApplyLayout()
        {
            float sw = _screenSize.X;
            float sh = _screenSize.Y;
            if (sw <= 0f || sh <= 0f)
            {
                return;
            }

            ApplySidebarLayout(sh);

            float areaWidth = sw - SidebarWidth;
            if (_mainArea != null)
            {
                _mainArea.Location = new Float2(SidebarWidth, 0f);
                _mainArea.Size = new Float2(areaWidth, sh);
            }

            if (_topHeader != null)
            {
                _topHeader.Location = Float2.Zero;
                _topHeader.Size = new Float2(areaWidth, TopHeaderHeight);
            }

            ApplyTopHeaderLayout(areaWidth);
            ApplyContentLayout(areaWidth, sh);
        }

        /// <summary>
        /// 左侧导航栏布局：Logo 条 + 按行依次排布分组标题 / 分隔线 / 导航项。
        /// </summary>
        /// <param name="sh">屏幕高度</param>
        private void ApplySidebarLayout(float sh)
        {
            if (_sidebar != null)
            {
                _sidebar.Location = Float2.Zero;
                _sidebar.Size = new Float2(SidebarWidth, sh);
            }

            if (_sidebarLogo != null)
            {
                _sidebarLogo.Location = Float2.Zero;
                _sidebarLogo.Size = new Float2(SidebarWidth, TopHeaderHeight);
            }

            if (_sidebarLogoDivider != null)
            {
                _sidebarLogoDivider.Location = new Float2(0f, TopHeaderHeight - 1f);
                _sidebarLogoDivider.Size = new Float2(SidebarWidth, 1f);
            }

            float rowY = TopHeaderHeight + InkWashTheme.Space4;
            for (int i = 0; i < _sidebarRows.Count; i++)
            {
                SidebarRow row = _sidebarRows[i];
                if (row == null || row.Element == null)
                {
                    continue;
                }

                rowY += row.GapBefore;
                switch (row.Kind)
                {
                    case SidebarRowKind.NavItem:
                        row.Element.Location = new Float2(InkWashTheme.Space3, rowY);
                        row.Element.Size = new Float2(SidebarWidth - InkWashTheme.Space3 * 2f, NavItemHeight);
                        break;

                    case SidebarRowKind.Divider:
                        row.Element.Location = new Float2(InkWashTheme.Space4, rowY);
                        row.Element.Size = new Float2(SidebarWidth - InkWashTheme.Space4 * 2f, 1f);
                        break;

                    default:
                        row.Element.Location = new Float2(InkWashTheme.Space4, rowY);
                        row.Element.Size = new Float2(SidebarWidth - InkWashTheme.Space4 * 2f, 18f);
                        break;
                }

                rowY += row.Step;
            }
        }

        /// <summary>
        /// 顶部角色信息条布局：左侧返回 / 头像 / 姓名 / 等级 / 门派，右侧铜钱与银两。
        /// </summary>
        /// <param name="areaWidth">主区宽度</param>
        private void ApplyTopHeaderLayout(float areaWidth)
        {
            float centerY = TopHeaderHeight * 0.5f;

            if (_topHeaderDivider != null)
            {
                _topHeaderDivider.Location = new Float2(0f, TopHeaderHeight - 1f);
                _topHeaderDivider.Size = new Float2(areaWidth, 1f);
            }

            if (_backButton != null)
            {
                _backButton.Location = new Float2(ContentPadding, centerY - _backButton.Height * 0.5f);
            }

            if (_playerAvatar != null)
            {
                _playerAvatar.Location = new Float2(ContentPadding + 100f, centerY - 18f);
            }

            if (_playerNameLabel != null)
            {
                _playerNameLabel.Location = new Float2(ContentPadding + 148f, centerY - 14f);
                _playerNameLabel.Size = new Float2(64f, 28f);
            }

            if (_playerLevelTag != null)
            {
                _playerLevelTag.Location = new Float2(ContentPadding + 216f, centerY - 11f);
                _playerLevelTag.Size = new Float2(52f, 22f);
            }

            if (_playerSectLabel != null)
            {
                _playerSectLabel.Location = new Float2(ContentPadding + 280f, centerY - 14f);
                _playerSectLabel.Size = new Float2(90f, 28f);
            }

            if (_currencyIngotLabel != null)
            {
                _currencyIngotLabel.Location = new Float2(areaWidth - ContentPadding - 96f, centerY - 14f);
                _currencyIngotLabel.Size = new Float2(96f, 28f);
            }

            if (_ingotDot != null)
            {
                _ingotDot.Location = new Float2(areaWidth - ContentPadding - 96f - 24f, centerY - 8f);
            }

            if (_currencyCoinLabel != null)
            {
                _currencyCoinLabel.Location = new Float2(areaWidth - ContentPadding - 96f - 24f - 20f - 116f, centerY - 14f);
                _currencyCoinLabel.Size = new Float2(116f, 28f);
            }

            if (_coinDot != null)
            {
                _coinDot.Location = new Float2(areaWidth - ContentPadding - 96f - 24f - 20f - 116f - 24f, centerY - 8f);
            }
        }

        /// <summary>
        /// 内容区布局：左 45% 外观预览，右 55% 页签 + 套装网格 + 染色区。
        /// </summary>
        /// <param name="areaWidth">主区宽度</param>
        /// <param name="sh">屏幕高度</param>
        private void ApplyContentLayout(float areaWidth, float sh)
        {
            float contentTop = TopHeaderHeight + ContentPadding;
            float contentHeight = sh - contentTop - ContentPadding;

            float leftSectionWidth = areaWidth * PreviewColumnRatio;
            float rightSectionWidth = areaWidth - leftSectionWidth;

            float previewX = ContentPadding;
            float previewWidth = leftSectionWidth - ContentPadding * 2f;
            float previewHeight = contentHeight;

            float rightX = leftSectionWidth + ContentPadding;
            float rightWidth = rightSectionWidth - ContentPadding * 2f;

            // ---- 外观预览 ----
            if (_previewPanel != null)
            {
                _previewPanel.Location = new Float2(previewX, contentTop);
                _previewPanel.Size = new Float2(previewWidth, previewHeight);
            }

            float titleWidth = 24f;
            if (_previewTitle != null)
            {
                _previewTitle.Location = new Float2(previewWidth - ContentPadding - titleWidth, ContentPadding);
                _previewTitle.Size = new Float2(titleWidth, 96f);
            }

            if (_preview3D != null)
            {
                float figureHeight = Mathf.Min(previewHeight * 0.72f, 560f);
                float figureWidth = figureHeight * 0.5f;
                _preview3D.Location = new Float2((previewWidth - figureWidth) * 0.5f, ContentPadding + 16f);
                _preview3D.Size = new Float2(figureWidth, figureHeight);
                _preview3D.RefreshLayout();
            }

            ApplyPreviewToolbarLayout(previewWidth, previewHeight);

            // ---- 分类页签 ----
            if (_tabBar != null)
            {
                _tabBar.Location = new Float2(rightX, contentTop);
                _tabBar.Size = new Float2(rightWidth, TabBarHeight);
            }

            if (_tabBarDivider != null)
            {
                _tabBarDivider.Location = new Float2(0f, TabBarHeight - 1f);
                _tabBarDivider.Size = new Float2(rightWidth, 1f);
            }

            float tabX = 0f;
            for (int i = 0; i < _tabButtons.Count; i++)
            {
                float tabWidth = InkWashTheme.Space6 + TabNames[i].Length * 14f;
                _tabButtons[i].Location = new Float2(tabX, 0f);
                _tabButtons[i].Size = new Float2(tabWidth, TabBarHeight - TabUnderlineHeight);
                if (i < _tabUnderlines.Count)
                {
                    _tabUnderlines[i].Location = new Float2(tabX, TabBarHeight - TabUnderlineHeight);
                    _tabUnderlines[i].Size = new Float2(tabWidth, TabUnderlineHeight);
                }

                tabX += tabWidth + InkWashTheme.Space1;
            }

            // ---- 套装卡片网格 ----
            float gridY = contentTop + TabBarHeight + InkWashTheme.Space4;
            float gridHeight = SetCardHeight * 2f + SetCardGap;

            if (_setGrid != null)
            {
                _setGrid.Location = new Float2(rightX, gridY);
                _setGrid.Size = new Float2(rightWidth, gridHeight);
            }

            float cardWidth = (rightWidth - SetCardGap * (SetCardColumns - 1)) / SetCardColumns;
            for (int i = 0; i < _setCards.Count; i++)
            {
                int row = i / SetCardColumns;
                int col = i % SetCardColumns;
                _setCards[i].Location = new Float2(col * (cardWidth + SetCardGap), row * (SetCardHeight + SetCardGap));
                _setCards[i].Size = new Float2(cardWidth, SetCardHeight);
            }

            // ---- 染色区 ----
            float dyeY = gridY + gridHeight + InkWashTheme.Space4;
            float dyeHeight = sh - ContentPadding - dyeY;
            if (_dyePanel != null)
            {
                _dyePanel.Location = new Float2(rightX, dyeY);
                _dyePanel.Size = new Float2(rightWidth, dyeHeight);
            }

            ApplyDyePanelLayout(rightWidth, dyeHeight);
        }

        /// <summary>
        /// 预览工具条布局：六控件水平居中排布。
        /// </summary>
        /// <param name="previewWidth">预览面板宽度</param>
        /// <param name="previewHeight">预览面板高度</param>
        private void ApplyPreviewToolbarLayout(float previewWidth, float previewHeight)
        {
            if (_previewToolbar == null)
            {
                return;
            }

            float toolbarY = previewHeight - ContentPadding - PreviewToolbarHeight;
            _previewToolbar.Location = new Float2(0f, toolbarY);
            _previewToolbar.Size = new Float2(previewWidth, PreviewToolbarHeight);

            float buttonGap = InkWashTheme.Space3;
            float totalWidth = 36f * 5f + 2f + buttonGap * 6f;
            float currentX = (previewWidth - totalWidth) * 0.5f;
            float buttonY = (PreviewToolbarHeight - 36f) * 0.5f;

            if (_btnRotateLeft != null)
            {
                _btnRotateLeft.Location = new Float2(currentX, buttonY);
                currentX += 36f + buttonGap;
            }

            if (_btnRotateRight != null)
            {
                _btnRotateRight.Location = new Float2(currentX, buttonY);
                currentX += 36f + buttonGap;
            }

            if (_previewDivider1 != null)
            {
                _previewDivider1.Location = new Float2(currentX, (PreviewToolbarHeight - 20f) * 0.5f);
                currentX += 1f + buttonGap;
            }

            if (_btnZoomIn != null)
            {
                _btnZoomIn.Location = new Float2(currentX, buttonY);
                currentX += 36f + buttonGap;
            }

            if (_btnZoomOut != null)
            {
                _btnZoomOut.Location = new Float2(currentX, buttonY);
                currentX += 36f + buttonGap;
            }

            if (_previewDivider2 != null)
            {
                _previewDivider2.Location = new Float2(currentX, (PreviewToolbarHeight - 20f) * 0.5f);
                currentX += 1f + buttonGap;
            }

            if (_btnResetView != null)
            {
                _btnResetView.Location = new Float2(currentX, buttonY);
            }
        }

        /// <summary>
        /// 染色区布局：标题行 / 三行色点（行标签 + 8 色点 + 分隔线）/ 底部按钮行。
        /// </summary>
        /// <param name="panelWidth">染色面板宽度</param>
        /// <param name="panelHeight">染色面板高度</param>
        private void ApplyDyePanelLayout(float panelWidth, float panelHeight)
        {
            if (_dyePanel == null || panelWidth <= 0f || panelHeight <= 0f)
            {
                return;
            }

            float innerWidth = panelWidth - DyePanelPadding * 2f;

            // 标题行（设计稿 flex items-center justify-between mb-4）
            if (_dyeTitleLabel != null)
            {
                _dyeTitleLabel.Location = new Float2(DyePanelPadding, DyePanelPadding);
                _dyeTitleLabel.Size = new Float2(72f, 24f);
            }

            if (_dyeSeparatorLabel != null)
            {
                _dyeSeparatorLabel.Location = new Float2(DyePanelPadding + 72f, DyePanelPadding);
                _dyeSeparatorLabel.Size = new Float2(16f, 24f);
            }

            if (_dyeTargetLabel != null)
            {
                _dyeTargetLabel.Location = new Float2(DyePanelPadding + 90f, DyePanelPadding);
                _dyeTargetLabel.Size = new Float2(140f, 24f);
            }

            if (_dyeHintLabel != null)
            {
                _dyeHintLabel.Location = new Float2(DyePanelPadding + innerWidth - 180f, DyePanelPadding);
                _dyeHintLabel.Size = new Float2(180f, 24f);
            }

            // 三行色点
            float rowStart = DyePanelPadding + 24f + InkWashTheme.Space4;
            float rowStep = DyeRowHeight + DyeRowGap;
            float dotStartX = DyePanelPadding + DyeRowLabelWidth + InkWashTheme.Space4;

            for (int row = 0; row < DyeRowTitles.Length; row++)
            {
                float rowY = rowStart + row * rowStep;

                if (row < _dyeRowTitleLabels.Count)
                {
                    _dyeRowTitleLabels[row].Location = new Float2(DyePanelPadding, rowY);
                    _dyeRowTitleLabels[row].Size = new Float2(DyeRowLabelWidth, 20f);
                }

                if (row < _dyeRowCaptionLabels.Count)
                {
                    _dyeRowCaptionLabels[row].Location = new Float2(DyePanelPadding, rowY + 20f);
                    _dyeRowCaptionLabels[row].Size = new Float2(DyeRowLabelWidth, 18f);
                }

                for (int i = 0; i < _dyeDots.Count; i++)
                {
                    InkColorDot dot = _dyeDots[i];
                    if (dot.Row != row)
                    {
                        continue;
                    }

                    dot.Location = new Float2(dotStartX + dot.Column * (DyeDotSize + DyeDotGap), rowY + 6f);
                    dot.Size = new Float2(DyeDotSize, DyeDotSize);
                }

                if (row < _dyeRowDividers.Count)
                {
                    _dyeRowDividers[row].Location = new Float2(DyePanelPadding, rowY + DyeRowHeight + DyeRowGap * 0.5f);
                    _dyeRowDividers[row].Size = new Float2(innerWidth, 1f);
                }
            }

            // 底部按钮行（设计稿 border-top + mt-4 pt-4 + 右对齐）
            float buttonY = panelHeight - DyePanelPadding - InkWashTheme.ControlHMd;

            if (_dyeFooterDivider != null)
            {
                _dyeFooterDivider.Location = new Float2(DyePanelPadding, buttonY - InkWashTheme.Space3);
                _dyeFooterDivider.Size = new Float2(innerWidth, 1f);
            }

            if (_btnApplyDye != null)
            {
                _btnApplyDye.Location = new Float2(DyePanelPadding + innerWidth - 140f, buttonY);
                _btnApplyDye.Size = new Float2(140f, InkWashTheme.ControlHMd);
            }

            if (_btnResetDye != null)
            {
                _btnResetDye.Location = new Float2(DyePanelPadding + innerWidth - 140f - 12f - 110f, buttonY);
                _btnResetDye.Size = new Float2(110f, InkWashTheme.ControlHMd);
            }
        }

        // ==================================================================
        // 数据刷新
        // ==================================================================

        /// <summary>
        /// 刷新页面数据：页签样式、套装选中态与染色区默认选择。
        /// </summary>
        private void RefreshAllData()
        {
            RefreshTabStyles();
            RefreshSetSelection();
            RefreshDyeSelection();
        }

        /// <summary>
        /// 基于当前控件实际尺寸刷新布局（由 <see cref="InkPageShell"/> 在挂载与尺寸变化时调用）。
        /// </summary>
        public void RefreshLayout()
        {
            _screenSize = new Float2(Width, Height);
            if (_screenSize.X <= 0f || _screenSize.Y <= 0f)
            {
                _screenSize = FlaxEngine.Screen.Size;
            }

            ApplyLayout();
        }

        // ==================================================================
        // 私有辅助控件
        // ==================================================================

        /// <summary>
        /// 分类页签按钮 —— 对应设计稿 <c>.tab-item</c> / <c>.tab-item-active</c>。
        /// <see cref="InkButton"/> 的悬停态配色会在 <c>OnMouseLeave</c> 时覆盖手动设置的激活色，
        /// 故此处重写鼠标事件，保证激活页签的金字与底部金线常驻。
        /// </summary>
        private sealed class AppearanceTabButton : InkButton
        {
            private bool _active;

            /// <summary>是否为当前激活页签。</summary>
            public bool IsActive
            {
                get => _active;
                set
                {
                    _active = value;
                    ApplyTabColors();
                }
            }

            /// <inheritdoc />
            public override void OnMouseEnter(Float2 location)
            {
                base.OnMouseEnter(location);
                ApplyTabColors();
                if (!_active)
                {
                    TextColor = InkWashTheme.PaperBright;
                }
            }

            /// <inheritdoc />
            public override void OnMouseLeave()
            {
                base.OnMouseLeave();
                ApplyTabColors();
            }

            /// <summary>
            /// 应用页签正常态配色（激活：品牌金字 + 微金底；未激活：褪色纸色 + 透明底）。
            /// </summary>
            private void ApplyTabColors()
            {
                TextColor = _active ? InkWashTheme.GoldBright : InkWashTheme.PaperAged;
                BackgroundColor = _active ? InkWashTheme.GoldTrace : Color.Transparent;
            }
        }

        /// <summary>左侧导航行类型。</summary>
        private enum SidebarRowKind
        {
            /// <summary>分组标题（角色 / 江湖 / 系统）</summary>
            GroupTitle,

            /// <summary>分组分隔线</summary>
            Divider,

            /// <summary>导航项</summary>
            NavItem
        }

        /// <summary>
        /// 左侧导航行的布局描述（按行顺序统一排布，避免硬编码 Y 坐标）。
        /// </summary>
        private sealed class SidebarRow
        {
            /// <summary>行类型</summary>
            public SidebarRowKind Kind;

            /// <summary>行控件</summary>
            public Control Element;

            /// <summary>放置前额外下移距离</summary>
            public float GapBefore;

            /// <summary>放置后下移距离</summary>
            public float Step;
        }

        /// <summary>
        /// 左侧导航项 —— 对应设计稿 <c>.nav-item</c> / <c>.nav-item-active</c> / <c>.nav-item-disabled</c>。
        /// 激活态绘制 2px 金竖线 + 微金底 + 品牌金文字；悬停态微金底；禁用态三级色文字。
        /// 自带点击事件，供页面经 <see cref="NavigationRequested"/> 转发跳转。
        /// </summary>
        private sealed class SidebarNavItem : ContainerControl
        {
            /// <summary>激活态底色（rgba(200,168,88,0.12)）</summary>
            private static readonly Color ActiveBackground = new Color(
                InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G,
                InkWashTheme.GoldPrimary.B, 0.12f);

            /// <summary>悬停态底色（rgba(200,168,88,0.06)）</summary>
            private static readonly Color HoverBackground = new Color(
                InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G,
                InkWashTheme.GoldPrimary.B, 0.06f);

            /// <summary>激活态左竖线宽度（设计稿 2px）</summary>
            private const float ActiveBarWidth = 2f;

            private readonly bool _active;
            private readonly bool _disabled;
            private readonly Label _label;
            private bool _hovered;

            /// <summary>导航项点击事件。</summary>
            public event Action Clicked;

            /// <summary>
            /// 构造函数。
            /// </summary>
            /// <param name="text">导航项文案</param>
            /// <param name="active">是否为当前页激活项</param>
            /// <param name="disabled">是否禁用（设计稿 nav-item-disabled）</param>
            public SidebarNavItem(string text, bool active, bool disabled)
            {
                _active = active;
                _disabled = disabled;
                BackgroundColor = Color.Transparent;
                ClipChildren = false;
                AutoFocus = false;
                Enabled = !disabled;

                _label = new Label
                {
                    Text = text,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 14f),
                    TextColor = disabled
                        ? InkWashTheme.TextTertiary
                        : (active ? InkWashTheme.GoldBright : InkWashTheme.PaperAged),
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                    Parent = this
                };
            }

            /// <inheritdoc />
            protected override void OnSizeChanged()
            {
                base.OnSizeChanged();

                if (_label != null)
                {
                    _label.Location = new Float2(InkWashTheme.Space3, 0f);
                    _label.Size = new Float2(Mathf.Max(0f, Width - InkWashTheme.Space3), Height);
                }
            }

            /// <inheritdoc />
            public override void OnMouseEnter(Float2 location)
            {
                base.OnMouseEnter(location);
                if (_disabled)
                {
                    return;
                }

                _hovered = true;
                if (_label != null && !_active)
                {
                    _label.TextColor = InkWashTheme.PaperBright;
                }
            }

            /// <inheritdoc />
            public override void OnMouseLeave()
            {
                base.OnMouseLeave();
                _hovered = false;

                if (_label != null && !_active && !_disabled)
                {
                    _label.TextColor = InkWashTheme.PaperAged;
                }
            }

            /// <inheritdoc />
            public override bool OnMouseDown(Float2 location, MouseButton button)
            {
                base.OnMouseDown(location, button);
                if (button != MouseButton.Left || _disabled)
                {
                    return false;
                }

                Clicked?.Invoke();
                return true;
            }

            /// <inheritdoc />
            public override void Draw()
            {
                if (Width <= 0f || Height <= 0f)
                {
                    return;
                }

                var bounds = new Rectangle(0f, 0f, Width, Height);

                if (_active)
                {
                    InkRenderHelper.FillRoundedRectangle(bounds, InkWashTheme.RadiusMd, ActiveBackground);
                }
                else if (_hovered && !_disabled)
                {
                    InkRenderHelper.FillRoundedRectangle(bounds, InkWashTheme.RadiusMd, HoverBackground);
                }

                var savedBackground = BackgroundColor;
                BackgroundColor = Color.Transparent;
                base.Draw();
                BackgroundColor = savedBackground;

                if (_active)
                {
                    Render2D.FillRectangle(
                        new Rectangle(0f, 0f, ActiveBarWidth, Height),
                        InkWashTheme.GoldPrimary);
                }
            }
        }

        /// <summary>
        /// 圆形色点 —— 对应设计稿 <c>.dye-dot</c> / <c>.dye-dot-selected</c>（28px 圆形 + 2px 描边 + 选中金色光环）。
        /// 同时复用于顶部信息条的铜钱 / 银两色点（<see cref="Enabled"/> = false 即为纯展示）。
        /// </summary>
        private sealed class InkColorDot : Control
        {
            /// <summary>默认描边色（设计稿 var(--text-tertiary)）</summary>
            private static readonly Color DefaultBorderColor = InkWashTheme.TextTertiary;

            /// <summary>选中光环色（设计稿 var(--ink-gold-bright)）</summary>
            private static readonly Color SelectedRingColor = new Color(
                InkWashTheme.GoldBright.R, InkWashTheme.GoldBright.G,
                InkWashTheme.GoldBright.B, 0.3f);

            private bool _selected;
            private bool _hovered;

            /// <summary>色点填充色</summary>
            public Color DotColor = InkWashTheme.GoldPrimary;

            /// <summary>常规态描边色（不选中且未悬停时使用）</summary>
            public Color RingColor = DefaultBorderColor;

            /// <summary>所属染色行（0=主色 / 1=辅色 / 2=点缀）。</summary>
            public int Row;

            /// <summary>行内列索引。</summary>
            public int Column;

            /// <summary>是否选中（绘制金色描边 + 外光环）。</summary>
            public bool Selected
            {
                get => _selected;
                set => _selected = value;
            }

            /// <summary>色点点击事件。</summary>
            public event Action<InkColorDot> Clicked;

            /// <summary>
            /// 构造函数。
            /// </summary>
            /// <param name="diameter">色点直径（像素）</param>
            public InkColorDot(float diameter)
            {
                Size = new Float2(diameter, diameter);
                BackgroundColor = Color.Transparent;
                AutoFocus = false;
            }

            /// <inheritdoc />
            public override void OnMouseEnter(Float2 location)
            {
                base.OnMouseEnter(location);
                _hovered = true;
            }

            /// <inheritdoc />
            public override void OnMouseLeave()
            {
                base.OnMouseLeave();
                _hovered = false;
            }

            /// <inheritdoc />
            public override bool OnMouseDown(Float2 location, MouseButton button)
            {
                base.OnMouseDown(location, button);
                if (button != MouseButton.Left)
                {
                    return false;
                }

                Clicked?.Invoke(this);
                return true;
            }

            /// <inheritdoc />
            public override void Draw()
            {
                if (Width <= 0f || Height <= 0f)
                {
                    return;
                }

                var center = new Float2(Width * 0.5f, Height * 0.5f);
                float radius = Mathf.Min(Width, Height) * 0.5f;

                // 选中态外光环（设计稿 box-shadow: 0 0 0 2px rgba(200,168,88,0.3)）
                if (_selected)
                {
                    InkRenderHelper.FillCircle(center, radius, SelectedRingColor);
                    radius -= 2f;
                }

                InkRenderHelper.FillCircle(center, radius, DotColor);

                Color border = _selected
                    ? InkWashTheme.GoldBright
                    : (_hovered && Enabled ? InkWashTheme.PaperAged : RingColor);
                InkRenderHelper.DrawCircle(center, Mathf.Max(0f, radius - 1f), border, 2f);
            }
        }

        /// <summary>
        /// 套装卡片 —— 对应设计稿 <c>.set-card</c> / <c>.set-card-selected</c>：
        /// 48px 缩略图 + 套装名 + 品质标签；选中态金色描边与微金渐变底，悬停态褪色纸色描边。
        /// </summary>
        private sealed class SetCard : ContainerControl
        {
            /// <summary>选中态底色（微金渐变近似）</summary>
            private static readonly Color SelectedBackground = Color.Lerp(
                InkWashTheme.GoldPrimary, InkWashTheme.BaseTertiary, 0.92f);

            /// <summary>悬停选中态底色</summary>
            private static readonly Color SelectedHoverBackground = Color.Lerp(
                InkWashTheme.GoldPrimary, InkWashTheme.BaseElevated, 0.88f);

            /// <summary>缩略图边长（设计稿 48px）</summary>
            private const float ThumbSize = 48f;

            private readonly Panel _thumb;
            private readonly Label _nameLabel;
            private readonly QualityTag _qualityTag;
            private bool _selected;
            private bool _hovered;

            /// <summary>卡片索引（对应设计稿 card-set-N）。</summary>
            public int Index { get; }

            /// <summary>卡片选中事件。</summary>
            public event Action<int> Selected;

            /// <summary>是否选中。</summary>
            public bool IsSelected
            {
                get => _selected;
                set => _selected = value;
            }

            /// <summary>
            /// 构造函数。
            /// </summary>
            /// <param name="index">卡片索引</param>
            /// <param name="name">套装名称</param>
            /// <param name="quality">品质文案</param>
            /// <param name="qualityColor">品质配色（主题 Token）</param>
            /// <param name="thumbColor">缩略图底色（主题 Token）</param>
            public SetCard(int index, string name, string quality, Color qualityColor, Color thumbColor)
            {
                Index = index;
                BackgroundColor = Color.Transparent;
                ClipChildren = false;
                AutoFocus = false;

                _thumb = new Panel
                {
                    BackgroundColor = thumbColor,
                    Parent = this
                };

                new Label
                {
                    Text = "衣",
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 24f),
                    TextColor = InkWashTheme.PaperDark,
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                    // 缩略图固定 48×48：StretchAll 实测 148×68，字会落到框外
                    Location = Float2.Zero,
                    Size = new Vector2(ThumbSize, ThumbSize),
                    Parent = _thumb
                };

                _nameLabel = new Label
                {
                    Text = name,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 14f),
                    TextColor = InkWashTheme.PaperBright,
                    VerticalAlignment = TextAlignment.Center,
                    Parent = this
                };

                _qualityTag = new QualityTag(quality, qualityColor)
                {
                    Parent = this
                };
            }

            /// <inheritdoc />
            protected override void OnSizeChanged()
            {
                base.OnSizeChanged();

                if (_thumb != null)
                {
                    _thumb.Location = new Float2(InkWashTheme.Space3, (Height - ThumbSize) * 0.5f);
                    _thumb.Size = new Float2(ThumbSize, ThumbSize);
                }

                float textX = InkWashTheme.Space3 + ThumbSize + InkWashTheme.Space3;
                float textWidth = Mathf.Max(0f, Width - textX - InkWashTheme.Space3);

                if (_nameLabel != null)
                {
                    _nameLabel.Location = new Float2(textX, 12f);
                    _nameLabel.Size = new Float2(textWidth, 22f);
                }

                if (_qualityTag != null)
                {
                    _qualityTag.Location = new Float2(textX, 38f);
                    _qualityTag.Size = new Float2(48f, 20f);
                }
            }

            /// <inheritdoc />
            public override void OnMouseEnter(Float2 location)
            {
                base.OnMouseEnter(location);
                _hovered = true;
            }

            /// <inheritdoc />
            public override void OnMouseLeave()
            {
                base.OnMouseLeave();
                _hovered = false;
            }

            /// <inheritdoc />
            public override bool OnMouseDown(Float2 location, MouseButton button)
            {
                base.OnMouseDown(location, button);
                if (button != MouseButton.Left)
                {
                    return false;
                }

                Selected?.Invoke(Index);
                return true;
            }

            /// <inheritdoc />
            public override void Draw()
            {
                if (Width <= 0f || Height <= 0f)
                {
                    return;
                }

                var bounds = new Rectangle(0f, 0f, Width, Height);

                Color background;
                Color border;
                if (_selected)
                {
                    background = _hovered ? SelectedHoverBackground : SelectedBackground;
                    border = _hovered ? InkWashTheme.GoldBright : InkWashTheme.GoldPrimary;
                }
                else
                {
                    background = _hovered ? InkWashTheme.BaseElevated : InkWashTheme.BaseTertiary;
                    border = _hovered ? InkWashTheme.PaperFaded : InkWashTheme.BorderNeutralL1;
                }

                InkRenderHelper.FillRoundedRectangle(bounds, InkWashTheme.RadiusMd, background);

                // 选中态套装名转为品牌金字（设计稿 color: var(--ink-gold-bright)）
                if (_nameLabel != null)
                {
                    _nameLabel.TextColor = _selected ? InkWashTheme.GoldBright : InkWashTheme.PaperBright;
                }

                var savedBackground = BackgroundColor;
                BackgroundColor = Color.Transparent;
                base.Draw();
                BackgroundColor = savedBackground;

                InkRenderHelper.DrawRoundedRectangle(bounds, InkWashTheme.RadiusMd, border, 1f);
            }
        }

        /// <summary>
        /// 品质标签 —— 对应设计稿 <c>.tag-common</c> / <c>.tag-rare</c> / <c>.tag-jade</c> /
        /// <c>.ink-tag-brand</c> / <c>.ink-tag-vermilion</c>：凡品 / 珍品 / 仙品 / 灵品 / 神品 各自一色。
        /// <see cref="InkTag"/> 仅覆盖 Brand / Vermilion 两种变体，故此处按设计稿品质色统一绘制。
        /// </summary>
        private sealed class QualityTag : Label
        {
            private readonly Color _accentColor;

            /// <summary>
            /// 构造函数。
            /// </summary>
            /// <param name="text">品质文案</param>
            /// <param name="accentColor">品质强调色（主题 Token）</param>
            public QualityTag(string text, Color accentColor)
            {
                _accentColor = accentColor;
                Text = text;
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f);
                TextColor = accentColor;
                BackgroundColor = new Color(accentColor.R, accentColor.G, accentColor.B, 0.1f);
                HorizontalAlignment = TextAlignment.Center;
                VerticalAlignment = TextAlignment.Center;
            }

            /// <inheritdoc />
            public override void Draw()
            {
                base.Draw();

                if (Width > 0f && Height > 0f)
                {
                    Render2D.DrawRectangle(new Rectangle(0f, 0f, Width, Height), _accentColor, 1f);
                }
            }
        }

        /// <summary>
        /// 外观预览人物剪影 —— 对应设计稿预览区 SVG（头 / 发髻 / 颈 / 长袍 / 双袖 / 腰带 / 背后长剑）
        /// 与两处 <c>.ink-splash</c> 水墨晕染底纹。所有颜色取自主题 Token。
        /// </summary>
        private sealed class AppearancePreviewFigure : ContainerControl
        {
            /// <summary>设计稿 SVG viewBox 尺寸。</summary>
            private static readonly Float2 ViewBox = new Float2(200f, 400f);

            /// <summary>
            /// 构造函数。
            /// </summary>
            public AppearancePreviewFigure()
            {
                BackgroundColor = Color.Transparent;
                ClipChildren = false;
                AutoFocus = false;
            }

            /// <inheritdoc />
            public override void Draw()
            {
                if (Width <= 0f || Height <= 0f)
                {
                    return;
                }

                float scale = Mathf.Min(Width / ViewBox.X, Height / ViewBox.Y);
                float offsetX = (Width - ViewBox.X * scale) * 0.5f;
                float offsetY = (Height - ViewBox.Y * scale) * 0.5f;

                Float2 P(float x, float y)
                {
                    return new Float2(offsetX + x * scale, offsetY + y * scale);
                }

                Rectangle R(float left, float top, float right, float bottom)
                {
                    Float2 min = P(left, top);
                    Float2 max = P(right, bottom);
                    return new Rectangle(min.X, min.Y, max.X - min.X, max.Y - min.Y);
                }

                // 1. 水墨晕染底纹（设计稿 .ink-splash 两处径向渐变）
                InkRenderHelper.FillRadialGradient(
                    P(40f, 60f), 120f * scale,
                    WithAlpha(InkWashTheme.BaseElevated, 0.3f), Color.Transparent, 12);
                InkRenderHelper.FillRadialGradient(
                    P(160f, 340f), 90f * scale,
                    WithAlpha(InkWashTheme.BaseTertiary, 0.3f), Color.Transparent, 12);

                Color robeColor = WithAlpha(InkWashTheme.PaperAged, 0.12f);
                Color sleeveColor = WithAlpha(InkWashTheme.PaperAged, 0.1f);
                Color hairColor = WithAlpha(InkWashTheme.BaseElevated, 0.2f);

                // 2. 头部与发髻
                InkRenderHelper.FillCircle(P(100f, 50f), 24f * scale, robeColor);
                InkRenderHelper.FillCircle(P(100f, 28f), 8f * scale, hairColor);

                // 3. 颈部
                Render2D.FillRectangle(R(92f, 72f, 108f, 84f), sleeveColor);

                // 4. 长袍主体
                InkRenderHelper.FillRoundedRectangle(R(58f, 84f, 142f, 392f), 22f * scale, robeColor);

                // 5. 双袖
                InkRenderHelper.FillRoundedRectangle(R(32f, 100f, 66f, 172f), 12f * scale, sleeveColor);
                InkRenderHelper.FillRoundedRectangle(R(134f, 100f, 168f, 172f), 12f * scale, sleeveColor);

                // 6. 腰带
                Render2D.FillRectangle(R(68f, 195f, 132f, 205f), WithAlpha(InkWashTheme.GoldPrimary, 0.1f));

                // 7. 长袍中缝
                Render2D.DrawLine(
                    P(100f, 90f), P(100f, 390f),
                    WithAlpha(InkWashTheme.GoldPrimary, 0.15f), 1f);

                // 8. 背后长剑（双线近似）
                Render2D.DrawLine(
                    P(70f, 80f), P(140f, 250f),
                    WithAlpha(InkWashTheme.GoldPrimary, 0.08f), 2f);
                Render2D.DrawLine(
                    P(68f, 82f), P(138f, 252f),
                    WithAlpha(InkWashTheme.GoldPrimary, 0.05f), 1f);
            }

            /// <summary>
            /// 以指定透明度重建颜色（保持 Token 色相不变）。
            /// </summary>
            /// <param name="color">主题 Token 颜色</param>
            /// <param name="alpha">目标透明度</param>
            /// <returns>带透明度的颜色</returns>
            private static Color WithAlpha(Color color, float alpha)
            {
                return new Color(color.R, color.G, color.B, alpha);
            }
        }
    }
}
