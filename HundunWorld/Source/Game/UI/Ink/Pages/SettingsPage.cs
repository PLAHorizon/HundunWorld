using System;
using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.Services;
using HundunWorld.Game.UI.Ink;
using HundunWorld.Game.UI.Ink.Components;
using HundunWorld.Game.UI.StyleSystem;

namespace HundunWorld.Game.UI.Ink.Pages
{
    /// <summary>
    /// 设置页（画面分类）。
    /// 设计稿：<c>hundun-yy-ui/pages/settings.html</c>（唯一事实来源）。
    /// <para>
    /// 结构（自上而下 / 自左而右）：
    /// <list type="bullet">
    ///   <item>左侧导航栏 240px：logo「混沌世界」+ 7 个导航项（任务/抽卡/通行证/角色/博物志/奇珍阁/设置）+ 版本页脚</item>
    ///   <item>顶部信息栏 60px：返回战斗 / 头像 / 江湖过客 / Lv. 42 / 异兽图鉴 / 铜钱 / 元宝 / 戌时 三刻</item>
    ///   <item>分类标签栏 200px：设置分类 + 画面(10)/音效(8)/操作(12)/账号(6)/其他(5)</item>
    ///   <item>设置列表：画面设置标题 + 画质推荐标签 + 副标题 + 搜索框，
    ///         分组「画质基础 / 视觉效果 / 高级选项 / 画面信息」共 10 个设置项 + 画面信息面板</item>
    ///   <item>底部操作栏 60px：修改提示 + 恢复默认 / 保存设置 / 应用并重启</item>
    /// </list>
    /// 全部颜色取 <see cref="InkWashTheme"/> 设计 Token（禁止硬编码色值），
    /// 布局在 <see cref="InkPageShell"/> 的 1920×1080 设计坐标系中完成，由 <see cref="RefreshLayout"/> 重算。
    /// 页面自身不创建返回按钮：外壳 <see cref="InkPageShell"/> 的左上角返回按钮即返回战斗 HUD 的既有出口。
    /// </para>
    /// </summary>
    public class SettingsPage : ContainerControl, IInkPage, IInkNavigationSource
    {
        // ===================================================================
        // 设计稿常量（对齐 settings.html 的面板尺寸与间距）
        // =======================================================================

        /// <summary>左侧导航栏宽度（设计稿 .left-nav 240px）</summary>
        private const float LeftNavWidth = 240f;

        /// <summary>顶部信息栏高度（设计稿 .top-bar 60px）</summary>
        private const float TopBarHeight = 60f;

        /// <summary>底部操作栏高度（设计稿 .action-bar 60px）</summary>
        private const float ActionBarHeight = 60f;

        /// <summary>分类标签栏宽度（设计稿 .category-tabs 200px）</summary>
        private const float CategoryTabsWidth = 200f;

        /// <summary>左侧导航项起始 Y（logo 之下）</summary>
        private const float NavRowTop = 84f;

        /// <summary>左侧导航项行高</summary>
        private const float NavRowHeight = 52f;

        /// <summary>分类标签项起始 Y</summary>
        private const float CategoryRowTop = 56f;

        /// <summary>分类标签项行高</summary>
        private const float CategoryRowHeight = 44f;

        /// <summary>设置列表左右内边距</summary>
        private const float ListPadding = 24f;

        /// <summary>设置列表头部区底边（分组起始 Y）</summary>
        private const float ListHeaderBottom = 96f;

        /// <summary>分组标题行高</summary>
        private const float SectionLabelHeight = 28f;

        /// <summary>设置项行高</summary>
        private const float RowHeight = 54f;

        /// <summary>画面质量行高（额外容纳画质预览胶囊）</summary>
        private const float QualityRowHeight = 84f;

        /// <summary>设置项之间的间距</summary>
        private const float RowGap = 6f;

        /// <summary>分组之间的间距</summary>
        private const float GroupGap = 12f;

        /// <summary>画面信息面板高度</summary>
        private const float InfoPanelHeight = 108f;   // 三列信息 + 一行"哪些项不生效"的脚注

        /// <summary>设置项内左侧内边距</summary>
        private const float RowInnerPadding = 16f;

        /// <summary>下拉触发器尺寸</summary>
        private const float DropdownWidth = 190f;
        private const float DropdownHeight = 32f;

        /// <summary>滑动条尺寸与右侧数值宽度</summary>
        private const float SliderWidth = 180f;
        private const float SliderHeight = 18f;
        private const float SliderValueWidth = 76f;

        /// <summary>开关尺寸与状态文字宽度</summary>
        private const float ToggleWidth = 48f;
        private const float ToggleHeight = 24f;
        private const float ToggleStatusWidth = 44f;

        /// <summary>画质预览胶囊尺寸</summary>
        private const float ChipWidth = 56f;
        private const float ChipHeight = 24f;

        /// <summary>搜索框尺寸</summary>
        private const float SearchBoxWidth = 220f;
        private const float SearchBoxHeight = 34f;

        /// <summary>底部按钮宽度</summary>
        private const float ActionButtonWidth = 116f;

        // ===================================================================
        // 设计稿数据（文案与选项逐条对齐 settings.html）
        // =======================================================================

        // 左侧页导航不再本页自维护：条目与分组统一由 InkSidebarNav 提供，
        // 由共用控件 InkSidebarList 渲染（原实现只列 7 项，其余功能页在设置页里没有入口）。

        /// <summary>分类标签文案（设计稿 .cat-tab）</summary>
        private static readonly string[] CategoryNames = { "画面", "音效", "操作", "账号", "其他", "路牌" };

        /// <summary>分类标签计数：画面=本页 10 个设置行，音效=音频子页 7 项；操作/账号/其他未接入，不编数字。</summary>
        private static readonly string[] CategoryCounts = { "10", "7", "—", "—", "—", "7" };

        /// <summary>音效分类索引（点击后进入独立音频设置子页 settings-audio）</summary>
        private const int AudioCategoryIndex = 1;
        private const int RoadSignCategoryIndex = 5;

        /// <summary>画面质量选项</summary>
        private static readonly string[] QualityOptions = { "流畅", "标准", "高清", "极致" };

        /// <summary>画面质量对应的配置键（传给 <see cref="GameConfigurationService"/>）</summary>
        private static readonly string[] QualityKeys = { "Low", "Medium", "High", "Ultra" };

        /// <summary>分辨率选项（设计稿 5 档）</summary>
        private static readonly string[] ResolutionOptions = { "1280 × 720", "1600 × 900", "1920 × 1080", "2560 × 1440", "3840 × 2160" };

        /// <summary>分辨率宽度数组（与 <see cref="ResolutionOptions"/> 一一对应）</summary>
        private static readonly int[] ResolutionWidths = { 1280, 1600, 1920, 2560, 3840 };

        /// <summary>分辨率高度数组（与 <see cref="ResolutionOptions"/> 一一对应）</summary>
        private static readonly int[] ResolutionHeights = { 720, 900, 1080, 1440, 2160 };

        /// <summary>特效质量选项</summary>
        private static readonly string[] EffectOptions = { "低", "中", "高", "极致" };

        /// <summary>阴影质量选项</summary>
        private static readonly string[] ShadowOptions = { "关闭", "低", "中", "高", "极致" };

        /// <summary>分组标题文案（设计稿 .setting-section-label）</summary>
        private static readonly string[] SectionTitles = { "画质基础", "视觉效果", "高级选项", "画面信息" };

        /// <summary>每个分组内的设置项数量（第 4 组为画面信息面板）</summary>
        private static readonly int[] GroupRowCounts = { 3, 3, 4 };


        // ===================================================================
        // 子控件引用
        // =======================================================================

        /// <summary>左侧导航栏</summary>
        private InkPanel _leftNav;
        private Label _navLogo;
        private InkDivider _navLogoUnderline;
        private InkSidebarList _sidebarList;
        private InkDivider _navFooterDivider;
        private Label _navVersionLabel;

        /// <summary>顶部信息栏</summary>
        private InkPanel _topBar;
        private InkButton _backHudButton;
        private InkDividerVertical _backHudDivider;
        private ContainerControl _avatarBox;
        private Label _avatarLabel;
        private Label _playerNameLabel;
        private InkItemTag _playerLevelTag;
        private InkButton _bestiaryButton;
        private Label _coinLabel;
        private Label _coinValueLabel;
        private InkDividerVertical _coinDivider;
        private Label _ingotLabel;
        private Label _ingotValueLabel;
        private InkDividerVertical _ingotDivider;
        private Label _gameTimeLabel;
        private InkDividerVertical _timeDivider;
        private TopBarItem[] _topBarRightItems;

        /// <summary>内容区（分类标签栏 + 设置列表）</summary>
        private ContainerControl _contentArea;

        /// <summary>分类标签栏</summary>
        private InkPanel _categoryPanel;
        private Label _categoryHeader;
        private InkDivider _categoryHeaderDivider;
        private InkNavRow[] _categoryRows;

        /// <summary>设置列表</summary>
        private ContainerControl _settingsList;
        private Label _listTitle;
        private InkItemTag _listTag;
        private Label _listSubtitle;
        private InkPanel _searchBox;
        private InkSearchGlyph _searchGlyph;
        private Label _searchHint;
        private InkSectionLabel[] _sectionLabels;
        private SettingRow[] _rows;

        /// <summary>画面信息面板</summary>
        private InkPanel _infoPanel;
        private Label[] _infoLabels;
        private Label[] _infoValues;
        private Label _infoFootnote;

        /// <summary>底部操作栏</summary>
        private InkPanel _actionBar;
        private Label _actionInfoLabel;
        private InkButton _resetButton;
        private InkButton _saveButton;
        private InkButton _restartButton;

        // 具名设置项引用（供交互与「恢复默认」使用）
        private SettingRow _qualityRow;
        private SettingRow _resolutionRow;
        private SettingRow _frameRateRow;
        private SettingRow _viewDistanceRow;
        private SettingRow _effectRow;
        private SettingRow _shadowRow;
        private SettingRow _antiAliasingRow;
        private SettingRow _vsyncRow;
        private SettingRow _autoHideUiRow;
        private SettingRow _powerSaveRow;

        // ===================================================================
        // 状态
        // =======================================================================

        /// <summary>当前控件尺寸（设计坐标系 1920×1080），用于布局计算</summary>
        private Float2 _screenSize;

        /// <summary>当前选中的分类标签索引</summary>
        private int _selectedCategory;

        /// <summary>
        /// 标准导航事件（对齐 Ink 页面契约，由工厂挂钩后经路由器跳转）。
        /// </summary>
        public event Action<string> NavigationRequested;

        // ===================================================================
        // 构造函数
        // =======================================================================

        /// <summary>
        /// 构造函数：按设计稿顺序装配全部 5 个子区域并应用初始布局。
        /// </summary>
        public SettingsPage()
        {
            _screenSize = FlaxEngine.Screen.Size;
            if (_screenSize.X <= 0f || _screenSize.Y <= 0f)
            {
                _screenSize = new Float2(1920f, 1080f);
            }

            AnchorPreset = AnchorPresets.StretchAll;
            BackgroundColor = Color.Transparent;
            ClipChildren = false;
            AutoFocus = false;
            Location = Float2.Zero;
            Size = _screenSize;

            try
            {
                BuildLeftNav();
                BuildTopBar();
                BuildContentArea();
                BuildCategoryTabs();
                BuildSettingsList();
                BuildActionBar();

                // 打开设置页时把已持久化的画面项真正下发到引擎（VSync/抗锯齿/阴影/特效），
                // 否则这些值只存在 LiteDB 里，重启后画面仍是引擎默认。
                GameConfigurationService.Instance.ApplyGraphicsSettings();

                ApplyLayout();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[SettingsPage] 初始化失败: {ex.Message}");
            }
        }

        // ===================================================================
        // 左侧导航栏
        // =======================================================================

        /// <summary>
        /// 构建左侧导航栏（设计稿 .left-nav）：
        /// logo「混沌世界」+ 金色分隔线 + 7 个导航项 + 版本页脚 v1.2.0 · Build 20260715。
        /// 导航项点击后经 <see cref="NavigationRequested"/> 交由路由器跳转。
        /// </summary>
        private void BuildLeftNav()
        {
            _leftNav = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = InkWashTheme.BaseSecondary,
                Parent = this,
            };

            _navLogo = CreateLabel("混沌世界", InkWashTheme.FontRole.Display, 24f,
                InkWashTheme.GoldBright, TextAlignment.Center, TextAlignment.Center);
            _navLogo.Parent = _leftNav;

            _navLogoUnderline = new InkDivider { Parent = _leftNav };

            _sidebarList = new InkSidebarList(InkPageDomIds.NavSettings)
            {
                Parent = _leftNav,
            };
            _sidebarList.NavigationRequested += domId => RequestNavigation(domId);

            _navFooterDivider = new InkDivider { Parent = _leftNav };

            _navVersionLabel = CreateLabel("v1.2.0 · Build 20260715", InkWashTheme.FontRole.Body, 11f,
                InkWashTheme.TextTertiary, TextAlignment.Center, TextAlignment.Center);
            _navVersionLabel.Parent = _leftNav;
        }

        // ===================================================================
        // 顶部信息栏
        // =======================================================================

        /// <summary>
        /// 构建顶部信息栏（设计稿 .top-bar）：
        /// 左侧 返回战斗 / 分隔线 / 头像「客」/ 江湖过客 / Lv. 42，
        /// 右侧 异兽图鉴 / 铜钱 12,450 / 元宝 328 / 戌时 三刻。
        /// 右侧簇按「右→左」顺序在 <see cref="ApplyLayout"/> 中排布。
        /// </summary>
        private void BuildTopBar()
        {
            _topBar = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = InkWashTheme.BaseSecondary,
                Parent = this,
            };

            _backHudButton = new InkButton
            {
                Width = 96f,
                Height = 32f,
                Text = "返回战斗",
                Variant = InkButtonVariant.Ghost,
                TextColor = InkWashTheme.TextSecondary,
                Parent = _topBar,
            };
            // 返回战斗 HUD（设计稿 .back-hud-btn 出口）
            _backHudButton.Clicked += () => RequestNavigation(InkPageDomIds.BackOneLevel);

            _backHudDivider = new InkDividerVertical { Parent = _topBar };

            _avatarBox = new ContainerControl
            {
                Size = new Float2(36f, 36f),
                BackgroundColor = InkWashTheme.BaseElevated,
                Parent = _topBar,
            };

            _avatarLabel = CreateLabel("客", InkWashTheme.FontRole.Display, 15f,
                InkWashTheme.GoldBright, TextAlignment.Center, TextAlignment.Center);
            _avatarLabel.Parent = _avatarBox;
            // 锚点在挂载到父容器之后再设置，确保偏移量基于真实父容器尺寸计算
            _avatarLabel.AnchorPreset = AnchorPresets.StretchAll;
            _avatarLabel.Offsets = Margin.Zero;

            _playerNameLabel = CreateLabel("—", InkWashTheme.FontRole.Heading, 15f,
                InkWashTheme.PaperBright, TextAlignment.Near, TextAlignment.Center);
            _playerNameLabel.Parent = _topBar;

            _playerLevelTag = new InkItemTag
            {
                Text = "—",
                Accent = InkWashTheme.GoldPrimary,
                Parent = _topBar,
            };

            _bestiaryButton = new InkButton
            {
                Width = 104f,
                Height = 32f,
                Text = "异兽图鉴",
                Variant = InkButtonVariant.Ghost,
                TextColor = InkWashTheme.GoldBright,
                Parent = _topBar,
            };
            // 快捷打开异兽详情（设计稿 data-dom-id="shortcut-popup-bestiary"）
            _bestiaryButton.Clicked += () => RequestNavigation(InkPageDomIds.PopupBestiarySide);

            _coinLabel = CreateLabel("铜钱", InkWashTheme.FontRole.Body, 11f,
                InkWashTheme.TextTertiary, TextAlignment.Far, TextAlignment.Center);
            _coinLabel.Parent = _topBar;

            // 铜钱只在 ShopClientService 拿到服务端快照后才由 binder 覆盖；初值不能摆设计稿的 12,450
            _coinValueLabel = CreateLabel("—", InkWashTheme.FontRole.Number, 13f,
                InkWashTheme.PaperBright, TextAlignment.Far, TextAlignment.Center);
            _coinValueLabel.Parent = _topBar;

            _coinDivider = new InkDividerVertical { Parent = _topBar };

            _ingotLabel = CreateLabel("元宝", InkWashTheme.FontRole.Body, 11f,
                InkWashTheme.TextBrand, TextAlignment.Far, TextAlignment.Center);
            _ingotLabel.Parent = _topBar;

            // 元宝没有服务端快照（ShopClientService 只给 Gold），不摆设计稿的 328
            _ingotValueLabel = CreateLabel("—", InkWashTheme.FontRole.Number, 13f,
                InkWashTheme.GoldBright, TextAlignment.Far, TextAlignment.Center);
            _ingotValueLabel.Parent = _topBar;

            _ingotDivider = new InkDividerVertical { Parent = _topBar };

            // 世界时间同步没有接入客户端（同 MenuTimePage），不摆设计稿的时辰
            _gameTimeLabel = CreateLabel("—", InkWashTheme.FontRole.Body, 12f,
                InkWashTheme.Info, TextAlignment.Far, TextAlignment.Center);
            _gameTimeLabel.Parent = _topBar;

            _timeDivider = new InkDividerVertical { Parent = _topBar };

            // 右侧簇：按「右→左」顺序排列，GapAfter 为与左侧相邻元素之间的间距
            _topBarRightItems = new[]
            {
                new TopBarItem(_gameTimeLabel, 84f, TopBarHeight, 0f, 10f),
                new TopBarItem(_timeDivider, 1f, 24f, 18f, 10f),
                new TopBarItem(_ingotValueLabel, 50f, TopBarHeight, 0f, 6f),
                new TopBarItem(_ingotLabel, 34f, TopBarHeight, 0f, 10f),
                new TopBarItem(_ingotDivider, 1f, 24f, 18f, 10f),
                new TopBarItem(_coinValueLabel, 58f, TopBarHeight, 0f, 6f),
                new TopBarItem(_coinLabel, 34f, TopBarHeight, 0f, 10f),
                new TopBarItem(_coinDivider, 1f, 24f, 18f, 10f),
                new TopBarItem(_bestiaryButton, 104f, 32f, 14f, 0f),
            };

            // 真实玩家信息可用时覆盖设计稿示例值（元宝/时辰暂无数据源）
            InkPlayerHeaderBinder.Bind(_playerNameLabel, _playerLevelTag, _coinValueLabel);
        }

        // ===================================================================
        // 内容区 / 分类标签栏
        // =======================================================================

        /// <summary>
        /// 构建内容区容器（设计稿 .content-area）：承载左侧分类标签栏与右侧设置列表。
        /// </summary>
        private void BuildContentArea()
        {
            _contentArea = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = Color.Transparent,
                ClipChildren = true,
                Parent = this,
            };
        }

        /// <summary>
        /// 构建分类标签栏（设计稿 .category-tabs）：
        /// 「设置分类」标题 + 画面(10)/音效(8)/操作(12)/账号(6)/其他(5)。
        /// 点击切换激活态；「音效」分类跳转独立音频设置子页。
        /// </summary>
        private void BuildCategoryTabs()
        {
            _categoryPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = InkWashTheme.BaseSecondary,
                Parent = _contentArea,
            };

            _categoryHeader = CreateLabel("设置分类", InkWashTheme.FontRole.Body, 12f,
                InkWashTheme.TextTertiary, TextAlignment.Near, TextAlignment.Center);
            _categoryHeader.Parent = _categoryPanel;

            _categoryHeaderDivider = new InkDivider { Parent = _categoryPanel };

            _categoryRows = new InkNavRow[CategoryNames.Length];
            for (int i = 0; i < CategoryNames.Length; i++)
            {
                var row = new InkNavRow(CategoryNames[i], CategoryCounts[i], 16f)
                {
                    Active = (i == 0),
                    Parent = _categoryPanel,
                };

                int catIndex = i;
                row.Clicked += () => SelectCategory(catIndex);
                _categoryRows[i] = row;
            }

            _selectedCategory = 0;
        }

        // ===================================================================
        // 设置列表
        // =======================================================================

        /// <summary>
        /// 构建右侧设置列表（设计稿 .settings-list）：
        /// 头部（画面设置 / 画质推荐 / 副标题 / 搜索框）+ 4 个分组 + 10 个设置项 + 画面信息面板。
        /// </summary>
        private void BuildSettingsList()
        {
            _settingsList = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = Color.Transparent,
                ClipChildren = true,
                Parent = _contentArea,
            };

            // --- 头部 ---
            _listTitle = CreateLabel("画面设置", InkWashTheme.FontRole.Display, 28f,
                InkWashTheme.PaperBright, TextAlignment.Near, TextAlignment.Center);
            _listTitle.Parent = _settingsList;

            _listTag = new InkItemTag
            {
                Text = "画质推荐",
                Accent = InkWashTheme.GoldPrimary,
                Parent = _settingsList,
            };

            _listSubtitle = CreateLabel("调整游戏画面表现，平衡视觉效果与性能", InkWashTheme.FontRole.Body, 12f,
                InkWashTheme.TextTertiary, TextAlignment.Near, TextAlignment.Center);
            _listSubtitle.Parent = _settingsList;

            _searchBox = new InkPanel
            {
                BackgroundColor = InkWashTheme.Void,
                Radius = InkWashTheme.RadiusMd,
                Parent = _settingsList,
            };

            _searchGlyph = new InkSearchGlyph { Parent = _searchBox };

            _searchHint = CreateLabel("搜索设置项...", InkWashTheme.FontRole.Body, 13f,
                InkWashTheme.TextTertiary, TextAlignment.Near, TextAlignment.Center);
            _searchHint.Parent = _searchBox;

            // --- 分组标题 ---
            _sectionLabels = new InkSectionLabel[SectionTitles.Length];
            for (int i = 0; i < SectionTitles.Length; i++)
            {
                _sectionLabels[i] = new InkSectionLabel { Text = SectionTitles[i], Parent = _settingsList };
            }

            // --- 画质基础 ---
            _qualityRow = CreateDropdownRow("画面质量", "调整整体画面质量，影响所有画面选项的预设", "推荐", false,
                QualityOptions, ReadQualityIndex(), QualityRowHeight, "quality");
            _resolutionRow = CreateDropdownRow("分辨率", "调整画面分辨率，分辨率越高画面越清晰", null, false,
                ResolutionOptions, ReadResolutionIndex(), RowHeight, "resolution");
            _frameRateRow = CreateSliderRow("帧率上限", "设置游戏最高帧率（引擎暂无帧率上限接口，此项只记录）",
                0f, 120f, ReadFrameRate(), "fps", RowHeight, "frameRate");

            // --- 视觉效果 ---
            _viewDistanceRow = CreateSliderRow("视距", "调整视角远近，视距越大可见范围越广",
                0f, 100f, ReadViewDistance(), string.Empty, RowHeight, "viewDistance");
            _effectRow = CreateDropdownRow("特效质量", "技能特效和粒子效果质量，影响战斗视觉表现", null, false,
                EffectOptions, ReadEffectQuality(), RowHeight, "effect");
            _shadowRow = CreateDropdownRow("阴影质量", "阴影渲染质量，高质量阴影增强画面立体感", null, false,
                ShadowOptions, ReadShadowQuality(), RowHeight, "shadow");

            // --- 高级选项 ---
            _antiAliasingRow = CreateToggleRow("抗锯齿", "开启抗锯齿使画面边缘更平滑，减少锯齿感", "推荐", false,
                ReadAntiAliasing(), RowHeight, "antiAliasing");
            _vsyncRow = CreateToggleRow("垂直同步", "防止画面撕裂，开启后帧率上限将同步显示器刷新率", null, false,
                ReadVSync(), RowHeight, "vsync");
            _autoHideUiRow = CreateToggleRow("自动隐藏UI", "战斗中自动隐藏部分UI元素，沉浸式战斗体验", null, false,
                ReadAutoHideUi(), RowHeight, "autoHideUi");
            // 省电模式没有任何消费者（引擎没有运行时降档接口），描述不能承诺"降低画质"
            _powerSaveRow = CreateToggleRow("画质省电模式", "记录偏好：当前引擎无法运行时降档，此项仅保存", "实验", true,
                ReadPowerSave(), RowHeight, "powerSave");

            _rows = new[]
            {
                _qualityRow, _resolutionRow, _frameRateRow,
                _viewDistanceRow, _effectRow, _shadowRow,
                _antiAliasingRow, _vsyncRow, _autoHideUiRow, _powerSaveRow,
            };

            BuildQualityPreview(_qualityRow);

            // --- 画面信息 ---
            BuildInfoPanel();
        }

        /// <summary>
        /// 构建设置项外壳：卡片面板 + 标题 + 说明 + 可选角标。
        /// </summary>
        /// <param name="title">设置项名称</param>
        /// <param name="description">设置项说明文案</param>
        /// <param name="tagText">角标文案（可空）</param>
        /// <param name="tagIsExperimental">角标是否为「实验」样式（朱红）</param>
        /// <param name="height">行高</param>
        /// <param name="kind">右侧控件类型</param>
        /// <param name="key">设置项键（用于回调分发）</param>
        /// <returns>设置项数据结构</returns>
        private SettingRow CreateRowShell(string title, string description, string tagText, bool tagIsExperimental,
            float height, SettingRowKind kind, string key)
        {
            var row = new SettingRow
            {
                Key = key,
                Kind = kind,
                Height = height,
                Panel = new InkPanel
                {
                    BackgroundColor = InkWashTheme.Panel,
                    Radius = InkWashTheme.RadiusMd,
                    Parent = _settingsList,
                },
            };

            row.TitleLabel = CreateLabel(title, InkWashTheme.FontRole.Heading, 15f,
                InkWashTheme.PaperBright, TextAlignment.Near, TextAlignment.Center);
            row.TitleLabel.Parent = row.Panel;

            row.DescLabel = CreateLabel(description, InkWashTheme.FontRole.Body, 12f,
                InkWashTheme.TextTertiary, TextAlignment.Near, TextAlignment.Center);
            row.DescLabel.Parent = row.Panel;

            if (!string.IsNullOrEmpty(tagText))
            {
                row.Tag = new InkItemTag
                {
                    Text = tagText,
                    Accent = tagIsExperimental ? InkWashTheme.VermilionBright : InkWashTheme.JadeBright,
                    Parent = row.Panel,
                };
            }

            return row;
        }

        /// <summary>
        /// 构建下拉型设置项（点击触发器循环切换选项并写回配置）。
        /// </summary>
        private SettingRow CreateDropdownRow(string title, string description, string tagText, bool tagIsExperimental,
            string[] options, int optionIndex, float height, string key)
        {
            var row = CreateRowShell(title, description, tagText, tagIsExperimental, height, SettingRowKind.Dropdown, key);
            row.Options = options;
            row.OptionIndex = optionIndex < 0 ? 0 : (optionIndex > options.Length - 1 ? options.Length - 1 : optionIndex);

            row.Trigger = new InkDropdown
            {
                Text = options[row.OptionIndex],
                Parent = row.Panel,
            };
            row.Trigger.Clicked += () => CycleDropdown(row);
            return row;
        }

        /// <summary>
        /// 构建滑动条型设置项（右侧滑动条 + 数值文字）。
        /// </summary>
        private SettingRow CreateSliderRow(string title, string description, float min, float max, float value,
            string unit, float height, string key)
        {
            var row = CreateRowShell(title, description, null, false, height, SettingRowKind.Slider, key);
            row.Unit = unit ?? string.Empty;

            var capturedRow = row;
            row.Slider = new Slider
            {
                Minimum = min,
                Maximum = max,
                Value = value,
                BackgroundColor = InkWashTheme.BaseTertiary,
                Parent = row.Panel,
            };
            row.Slider.ValueChanged += () =>
            {
                if (capturedRow.ValueLabel != null && capturedRow.Slider != null)
                {
                    capturedRow.ValueLabel.Text = FormatSliderValue(capturedRow.Slider.Value, capturedRow.Unit);
                }
            };
            // 拖动过程中不写库（每像素一次太贵），松手时落一次
            row.Slider.SlidingEnd += () => HandleSliderCommitted(capturedRow);

            row.ValueLabel = CreateLabel(FormatSliderValue(value, row.Unit), InkWashTheme.FontRole.Number, 13f,
                InkWashTheme.PaperBright, TextAlignment.Far, TextAlignment.Center);
            row.ValueLabel.Parent = row.Panel;
            return row;
        }

        /// <summary>
        /// 构建开关型设置项（右侧 开启/关闭 状态文字 + 开关）。
        /// </summary>
        private SettingRow CreateToggleRow(string title, string description, string tagText, bool tagIsExperimental,
            bool initialOn, float height, string key)
        {
            var row = CreateRowShell(title, description, tagText, tagIsExperimental, height, SettingRowKind.Toggle, key);

            var capturedRow = row;
            row.Toggle = new InkToggle
            {
                Checked = initialOn,
                Parent = row.Panel,
            };
            row.Toggle.CheckedChanged += on =>
            {
                if (capturedRow.StatusLabel != null)
                {
                    capturedRow.StatusLabel.Text = on ? "开启" : "关闭";
                    capturedRow.StatusLabel.TextColor = on ? InkWashTheme.GoldBright : InkWashTheme.TextTertiary;
                }
                HandleToggleChanged(capturedRow, on);
            };

            row.StatusLabel = CreateLabel(initialOn ? "开启" : "关闭", InkWashTheme.FontRole.Number, 12f,
                initialOn ? InkWashTheme.GoldBright : InkWashTheme.TextTertiary, TextAlignment.Far, TextAlignment.Center);
            row.StatusLabel.Parent = row.Panel;
            return row;
        }

        /// <summary>
        /// 构建「画面质量」行下方的画质预览胶囊（流畅 / 标准 / 高清 / 极致），点击直接选中该档画质。
        /// </summary>
        /// <param name="row">画面质量设置项</param>
        private void BuildQualityPreview(SettingRow row)
        {
            if (row == null)
                return;

            var chips = new InkChip[QualityOptions.Length];
            for (int i = 0; i < QualityOptions.Length; i++)
            {
                var chip = new InkChip
                {
                    Text = QualityOptions[i],
                    Parent = row.Panel,
                };

                int optionIndex = i;
                chip.Clicked += () =>
                {
                    row.OptionIndex = optionIndex;
                    if (row.Trigger != null)
                        row.Trigger.Text = row.Options[optionIndex];
                    SyncQualityChips(row, optionIndex);
                    HandleOptionChanged(row);
                };

                chips[i] = chip;
            }

            row.Chips = chips;
            SyncQualityChips(row, row.OptionIndex);
        }

        /// <summary>
        /// 显卡型号与显存占用：来自引擎的 <c>GPUDevice.Adapter.Description</c> /
        /// <c>MemoryUsage</c>（已用）/ <c>TotalGraphicsMemory</c>（总量，字节）。
        /// 渲染设备尚未就绪时返回「—」。
        /// </summary>
        private static (string Name, string Vram) ReadGpuInfo()
        {
            try
            {
                var device = FlaxEngine.GPUDevice.Instance;
                if (device == null) return ("—", "—");

                string name = device.Adapter?.Description;
                if (string.IsNullOrWhiteSpace(name)) name = "—";
                // 适配器描述可能很长（含厂商前缀），截到 26 字符避免把三列布局顶破
                if (name.Length > 26) name = name.Substring(0, 26);

                long used = (long)device.MemoryUsage;
                long total = (long)device.TotalGraphicsMemory;
                string vram = total > 0
                    ? $"{used / (1024.0 * 1024.0 * 1024.0):F1} / {total / (1024.0 * 1024.0 * 1024.0):F0} GB"
                    : "—";
                return (name, vram);
            }
            catch
            {
                return ("—", "—");
            }
        }

        /// <summary>
        /// 构建画面信息面板（显卡 / 显存占用 / 当前帧率）。
        /// </summary>
        private void BuildInfoPanel()
        {
            _infoPanel = new InkPanel
            {
                BackgroundColor = InkWashTheme.Panel,
                Radius = InkWashTheme.RadiusMd,
                Parent = _settingsList,
            };

            string[] labels = { "显卡", "显存占用", "当前帧率" };
            var (gpuName, vramText) = ReadGpuInfo();
            // 显卡型号/显存占用没有可用的引擎读取接口，显示占位而不是写死「RTX 4070 / 4.2 GB」；
            // 当前帧率由 Update 每 0.5 秒写入真实值（Engine.FramesPerSecond）。
            string[] values = { gpuName, vramText, "—" };
            Color[] valueColors = { InkWashTheme.PaperBright, InkWashTheme.GoldBright, InkWashTheme.JadeBright };

            _infoLabels = new Label[labels.Length];
            _infoValues = new Label[values.Length];

            for (int i = 0; i < labels.Length; i++)
            {
                _infoLabels[i] = CreateLabel(labels[i], InkWashTheme.FontRole.Body, 11f,
                    InkWashTheme.TextTertiary, TextAlignment.Near, TextAlignment.Center);
                _infoLabels[i].Parent = _infoPanel;

                _infoValues[i] = CreateLabel(values[i], InkWashTheme.FontRole.Number, 13f,
                    valueColors[i], TextAlignment.Near, TextAlignment.Center);
                _infoValues[i].Parent = _infoPanel;
            }

            // 实测结论：AAQuality / ShadowsQuality / SSRQuality / VolumetricFogQuality 在运行时写入后读回不变，
            // 省电模式则没有任何消费者——这四项只保存不改变画面，页面必须说清楚，不能让人以为切了立刻生效。
            _infoFootnote = CreateLabel("※ 分辨率 / 抗锯齿 / 阴影质量 / 特效质量 / 省电模式在当前引擎版本运行时不生效，仅保存设置",
                InkWashTheme.FontRole.Body, 11f, InkWashTheme.TextDisabled, TextAlignment.Near, TextAlignment.Center);
            _infoFootnote.Parent = _infoPanel;
        }

        // ===================================================================
        // 底部操作栏
        // =======================================================================

        /// <summary>
        /// 构建底部操作栏（设计稿 .action-bar）。
        /// 设计稿那句「有 3 项设置已修改，尚未保存」是样张：本页每一项交互都即时落库（见 SetXxx 分支），
        /// 不存在"待保存"状态，故改成如实描述，也不再画那颗"未保存"红点。
        /// </summary>
        private void BuildActionBar()
        {
            _actionBar = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = InkWashTheme.BaseSecondary,
                Parent = this,
            };

            _actionInfoLabel = CreateLabel("改动即时生效并已保存", InkWashTheme.FontRole.Body, 13f,
                InkWashTheme.TextTertiary, TextAlignment.Near, TextAlignment.Center);
            _actionInfoLabel.Parent = _actionBar;

            _resetButton = new InkButton
            {
                Width = ActionButtonWidth,
                Height = 36f,
                Text = "恢复默认",
                Variant = InkButtonVariant.Ghost,
                TextColor = InkWashTheme.PaperBright,
                Parent = _actionBar,
            };
            _resetButton.Clicked += ResetToDefaults;

            _saveButton = new InkButton
            {
                Width = ActionButtonWidth,
                Height = 36f,
                Text = "保存设置",
                Variant = InkButtonVariant.Primary,
                Parent = _actionBar,
            };
            _saveButton.Clicked += SaveAllSettings;

            _restartButton = new InkButton
            {
                Width = ActionButtonWidth,
                Height = 36f,
                Text = "应用并重启",
                Variant = InkButtonVariant.Vermilion,
                Parent = _actionBar,
            };
            _restartButton.Clicked += ApplyAndRestart;
        }

        // ===================================================================
        // 交互回调
        // =======================================================================

        /// <summary>
        /// 触发导航请求（由工厂挂钩后经 <see cref="InkPageRouter"/> 跳转）。
        /// </summary>
        /// <param name="domId">目标 dom-id</param>
        private void RequestNavigation(string domId)
        {
            try
            {
                NavigationRequested?.Invoke(domId);
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[SettingsPage] 导航请求失败 ({domId}): {ex.Message}");
            }
        }

        /// <summary>
        /// 切换分类标签激活态；「音效」分类转入独立音频设置子页。
        /// </summary>
        /// <param name="index">分类索引</param>
        private void SelectCategory(int index)
        {
            if (_categoryRows == null || index < 0 || index >= _categoryRows.Length)
                return;

            // 本页只有「画面」的 10 个设置项；操作/账号/其他尚未接入。
            // 以前点它们只改高亮、内容仍是画面项，看起来像"切了但没变"。
            if (index >= 2 && index != RoadSignCategoryIndex)
            {
                HundunWorld.Game.UI.UIHelper.ShowToast("该分类的设置项尚未接入", HundunWorld.Game.UI.ToastType.Info);
                return;
            }

            for (int i = 0; i < _categoryRows.Length; i++)
            {
                if (_categoryRows[i] != null)
                    _categoryRows[i].Active = (i == index);
            }

            _selectedCategory = index;
            FlaxEngine.Debug.Log($"[SettingsPage] 分类切换: {CategoryNames[_selectedCategory]}");

            if (index == AudioCategoryIndex)
            {
                RequestNavigation(InkPageDomIds.NavSettingsAudio);
            }
            else if (index == RoadSignCategoryIndex)
            {
                RequestNavigation(InkPageDomIds.NavSettingsRoadSign);
            }
        }

        /// <summary>
        /// 下拉选项循环切换（点击触发器切换至下一档），并同步画面质量预览胶囊。
        /// </summary>
        /// <param name="row">目标设置项</param>
        private void CycleDropdown(SettingRow row)
        {
            if (row?.Options == null || row.Options.Length == 0)
                return;

            row.OptionIndex = (row.OptionIndex + 1) % row.Options.Length;
            if (row.Trigger != null)
                row.Trigger.Text = row.Options[row.OptionIndex];

            if (ReferenceEquals(row, _qualityRow))
                SyncQualityChips(row, row.OptionIndex);

            HandleOptionChanged(row);
        }

        /// <summary>
        /// 下拉选项变化后的落地处理：画面质量 / 分辨率写回 <see cref="GameConfigurationService"/>，
        /// 其余项为页面内 mock 状态。
        /// </summary>
        /// <param name="row">目标设置项</param>
        private void HandleOptionChanged(SettingRow row)
        {
            if (row?.Options == null)
                return;

            string value = row.Options[row.OptionIndex];
            try
            {
                switch (row.Key)
                {
                    case "quality":
                        _ = GameConfigurationService.Instance.SetGraphicsQualityAsync(QualityKeys[row.OptionIndex]);
                        break;
                    case "resolution":
                        _ = GameConfigurationService.Instance.SetResolutionAsync(
                            ResolutionWidths[row.OptionIndex], ResolutionHeights[row.OptionIndex]);
                        break;
                    case "effect":
                        _ = GameConfigurationService.Instance.SetEffectQualityAsync(row.OptionIndex);
                        break;
                    case "shadow":
                        _ = GameConfigurationService.Instance.SetShadowQualityAsync(row.OptionIndex);
                        break;
                }
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[SettingsPage] 保存设置失败 ({row.Key}): {ex.Message}");
            }

            FlaxEngine.Debug.Log($"[SettingsPage] {row.Key} = {value}");
        }

        /// <summary>
        /// 开关变化后的落地处理：垂直同步写回 <see cref="GameConfigurationService"/>，其余为页面内 mock 状态。
        /// </summary>
        /// <param name="row">目标设置项</param>
        /// <param name="value">开关新状态</param>
        private void HandleToggleChanged(SettingRow row, bool value)
        {
            try
            {
                switch (row?.Key)
                {
                    case "vsync":
                        _ = GameConfigurationService.Instance.SetVSyncAsync(value);
                        break;
                    case "antiAliasing":
                        _ = GameConfigurationService.Instance.SetAntiAliasingAsync(value);
                        break;
                    case "autoHideUi":
                        _ = GameConfigurationService.Instance.SetAutoHideUiAsync(value);
                        break;
                    case "powerSave":
                        _ = GameConfigurationService.Instance.SetPowerSaveAsync(value);
                        break;
                }
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[SettingsPage] 保存开关失败 ({row?.Key}): {ex.Message}");
            }

            FlaxEngine.Debug.Log($"[SettingsPage] {row?.Key} = {(value ? "开启" : "关闭")}");
        }

        /// <summary>
        /// 滑动条松手后落库：拖动期间只更新数值文字，避免每像素写一次 LiteDB。
        /// </summary>
        private void HandleSliderCommitted(SettingRow row)
        {
            if (row?.Slider == null)
                return;

            try
            {
                switch (row.Key)
                {
                    case "frameRate":
                        _ = GameConfigurationService.Instance.SetFrameRateAsync(
                            Mathf.RoundToInt(row.Slider.Value));
                        break;
                    case "viewDistance":
                        _ = GameConfigurationService.Instance.SetViewDistanceAsync(row.Slider.Value);
                        break;
                }
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[SettingsPage] 保存滑动条失败 ({row.Key}): {ex.Message}");
            }

            FlaxEngine.Debug.Log($"[SettingsPage] {row.Key} = {row.Slider.Value:0.##} (committed)");
        }

        /// <summary>
        /// 同步画质预览胶囊的激活态。
        /// </summary>
        /// <param name="row">画面质量设置项</param>
        /// <param name="activeIndex">激活档位索引</param>
        private void SyncQualityChips(SettingRow row, int activeIndex)
        {
            if (row?.Chips == null)
                return;

            for (int i = 0; i < row.Chips.Length; i++)
            {
                if (row.Chips[i] != null)
                    row.Chips[i].Active = (i == activeIndex);
            }
        }

        /// <summary>
        /// 恢复默认：把 10 个设置项恢复为设计稿初始状态并写回配置。
        /// </summary>
        private void ResetToDefaults()
        {
            try
            {
                SetDropdownSelection(_qualityRow, 2);   // 高清
                SetDropdownSelection(_resolutionRow, 2); // 1920 × 1080
                SetDropdownSelection(_effectRow, 2);     // 高
                SetDropdownSelection(_shadowRow, 2);     // 中

                SetSliderValue(_frameRateRow, 60f);
                SetSliderValue(_viewDistanceRow, 75f);

                SetToggleValue(_antiAliasingRow, true);
                SetToggleValue(_vsyncRow, false);
                SetToggleValue(_autoHideUiRow, true);
                SetToggleValue(_powerSaveRow, false);

                FlaxEngine.Debug.Log("[SettingsPage] 恢复默认");
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[SettingsPage] 恢复默认失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 保存设置：把当前画面质量 / 分辨率 / 垂直同步写回 <see cref="GameConfigurationService"/>。
        /// </summary>
        /// <summary>
        /// 保存设置：把当前所有设置项写回 <see cref="GameConfigurationService"/>。
        /// 逐项交互时已经即时落库，这里再全量刷一遍，保证"保存"确实是全量提交（含恢复默认后的值）。
        /// </summary>
        private void SaveAllSettings()
        {
            try
            {
                if (_rows != null)
                {
                    foreach (var row in _rows)
                    {
                        if (row == null) continue;

                        switch (row.Key)
                        {
                            case "quality":
                            case "resolution":
                            case "effect":
                            case "shadow":
                                HandleOptionChanged(row);
                                break;

                            case "frameRate":
                            case "viewDistance":
                                HandleSliderCommitted(row);
                                break;

                            case "vsync":
                            case "antiAliasing":
                            case "autoHideUi":
                            case "powerSave":
                                if (row.Toggle != null)
                                    HandleToggleChanged(row, row.Toggle.Checked);
                                break;
                        }
                    }
                }

                FlaxEngine.Debug.Log("[SettingsPage] 保存设置");
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[SettingsPage] 保存设置失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 应用并重启：保存当前设置后触发重启流程（当前为日志占位，重启由上层编排）。
        /// </summary>
        private void ApplyAndRestart()
        {
            SaveAllSettings();
            FlaxEngine.Debug.Log("[SettingsPage] 应用并重启");
        }

        /// <summary>
        /// 设置下拉型设置项的选中档位并同步触发器文本与预览胶囊。
        /// </summary>
        private void SetDropdownSelection(SettingRow row, int optionIndex)
        {
            if (row?.Options == null || row.Options.Length == 0)
                return;

            row.OptionIndex = optionIndex < 0
                ? 0
                : (optionIndex > row.Options.Length - 1 ? row.Options.Length - 1 : optionIndex);
            if (row.Trigger != null)
                row.Trigger.Text = row.Options[row.OptionIndex];
            if (ReferenceEquals(row, _qualityRow))
                SyncQualityChips(row, row.OptionIndex);
            HandleOptionChanged(row);
        }

        /// <summary>
        /// 设置滑动条数值并同步数值文字。
        /// </summary>
        private void SetSliderValue(SettingRow row, float value)
        {
            if (row?.Slider == null)
                return;

            row.Slider.Value = value;
            if (row.ValueLabel != null)
                row.ValueLabel.Text = FormatSliderValue(value, row.Unit);

            // 程序化赋值不会触发 SlidingEnd，恢复默认也要落库
            HandleSliderCommitted(row);
        }

        /// <summary>
        /// 设置开关状态并同步状态文字。
        /// </summary>
        private void SetToggleValue(SettingRow row, bool value)
        {
            if (row?.Toggle == null)
                return;

            row.Toggle.Checked = value;
            if (row.StatusLabel != null)
            {
                row.StatusLabel.Text = value ? "开启" : "关闭";
                row.StatusLabel.TextColor = value ? InkWashTheme.GoldBright : InkWashTheme.TextTertiary;
            }

            // Checked 的 setter 不触发 CheckedChanged，恢复默认也要落库
            HandleToggleChanged(row, value);
        }

        /// <summary>
        /// 读取当前画面质量档位（配置默认 Medium → 标准；异常时回落到设计稿的「高清」）。
        /// </summary>
        private static int ReadQualityIndex()
        {
            try
            {
                string qualityKey = GameConfigurationService.Instance.GetGraphicsQuality();
                for (int i = 0; i < QualityKeys.Length; i++)
                {
                    if (string.Equals(QualityKeys[i], qualityKey, StringComparison.OrdinalIgnoreCase))
                        return i;
                }
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[SettingsPage] 读取画质失败: {ex.Message}");
            }
            return 2;
        }

        /// <summary>
        /// 读取当前分辨率档位（未命中列表时回落到设计稿的 1920 × 1080）。
        /// </summary>
        private static int ReadResolutionIndex()
        {
            try
            {
                var (w, h) = GameConfigurationService.Instance.GetResolution();
                for (int i = 0; i < ResolutionWidths.Length; i++)
                {
                    if (ResolutionWidths[i] == w && ResolutionHeights[i] == h)
                        return i;
                }
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[SettingsPage] 读取分辨率失败: {ex.Message}");
            }
            return 2;
        }

        /// <summary>
        /// 读取垂直同步开关状态（异常时回落到设计稿的「关闭」）。
        /// </summary>
        private static bool ReadVSync()
        {
            try
            {
                return GameConfigurationService.Instance.GetVSync();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[SettingsPage] 读取垂直同步失败: {ex.Message}");
                return false;
            }
        }

        private static int ReadFrameRate()
        {
            try { return GameConfigurationService.Instance.GetFrameRate(); }
            catch (Exception ex) { FlaxEngine.Debug.LogError($"[SettingsPage] 读取帧率上限失败: {ex.Message}"); return 60; }
        }

        private static float ReadViewDistance()
        {
            try { return GameConfigurationService.Instance.GetViewDistance(); }
            catch (Exception ex) { FlaxEngine.Debug.LogError($"[SettingsPage] 读取视距失败: {ex.Message}"); return 75f; }
        }

        private static int ReadEffectQuality()
        {
            try { return GameConfigurationService.Instance.GetEffectQuality(); }
            catch (Exception ex) { FlaxEngine.Debug.LogError($"[SettingsPage] 读取特效质量失败: {ex.Message}"); return 2; }
        }

        private static int ReadShadowQuality()
        {
            try { return GameConfigurationService.Instance.GetShadowQuality(); }
            catch (Exception ex) { FlaxEngine.Debug.LogError($"[SettingsPage] 读取阴影质量失败: {ex.Message}"); return 2; }
        }

        private static bool ReadAntiAliasing()
        {
            try { return GameConfigurationService.Instance.GetAntiAliasing(); }
            catch (Exception ex) { FlaxEngine.Debug.LogError($"[SettingsPage] 读取抗锯齿失败: {ex.Message}"); return true; }
        }

        private static bool ReadAutoHideUi()
        {
            try { return GameConfigurationService.Instance.GetAutoHideUi(); }
            catch (Exception ex) { FlaxEngine.Debug.LogError($"[SettingsPage] 读取自动隐藏UI失败: {ex.Message}"); return true; }
        }

        private static bool ReadPowerSave()
        {
            try { return GameConfigurationService.Instance.GetPowerSave(); }
            catch (Exception ex) { FlaxEngine.Debug.LogError($"[SettingsPage] 读取省电模式失败: {ex.Message}"); return false; }
        }

        /// <summary>
        /// 拼接滑动条数值文本（无单位时仅显示数字）。
        /// </summary>
        private static string FormatSliderValue(float value, string unit)
        {
            int rounded = (int)Math.Round(value);
            return string.IsNullOrEmpty(unit) ? rounded.ToString() : $"{rounded} {unit}";
        }

        // ===================================================================
        // 布局
        // =======================================================================

        /// <summary>
        /// 依据 <see cref="_screenSize"/> 重新计算全部子控件的位置与尺寸。
        /// </summary>
        private void ApplyLayout()
        {
            float w = _screenSize.X;
            float h = _screenSize.Y;
            if (w <= 0f)
                w = 1920f;
            if (h <= 0f)
                h = 1080f;

            // ---- 左侧导航栏 ----
            if (_leftNav != null)
            {
                _leftNav.Location = Float2.Zero;
                _leftNav.Size = new Float2(LeftNavWidth, h);
            }

            if (_navLogo != null)
            {
                _navLogo.Location = new Float2(0f, 20f);
                _navLogo.Size = new Float2(LeftNavWidth, 42f);
            }

            if (_navLogoUnderline != null)
            {
                _navLogoUnderline.Location = new Float2((LeftNavWidth - 40f) * 0.5f, 70f);
                _navLogoUnderline.Size = new Float2(40f, 1f);
            }

            if (_sidebarList != null)
            {
                // 底部留给分隔线（h-64）与版本号
                _sidebarList.Layout(0f, NavRowTop, LeftNavWidth, Mathf.Max(0f, h - NavRowTop - 64f));
            }

            if (_navFooterDivider != null)
            {
                _navFooterDivider.Location = new Float2(16f, h - 64f);
                _navFooterDivider.Size = new Float2(LeftNavWidth - 32f, 1f);
            }

            if (_navVersionLabel != null)
            {
                _navVersionLabel.Location = new Float2(0f, h - 52f);
                _navVersionLabel.Size = new Float2(LeftNavWidth, 20f);
            }

            // ---- 顶部信息栏 ----
            float rightWidth = Mathf.Max(640f, w - LeftNavWidth);

            if (_topBar != null)
            {
                _topBar.Location = new Float2(LeftNavWidth, 0f);
                _topBar.Size = new Float2(rightWidth, TopBarHeight);
            }

            if (_backHudButton != null)
                _backHudButton.Location = new Float2(16f, (TopBarHeight - 32f) * 0.5f);

            if (_backHudDivider != null)
            {
                _backHudDivider.Location = new Float2(124f, (TopBarHeight - 24f) * 0.5f);
                _backHudDivider.Size = new Float2(1f, 24f);
            }

            if (_avatarBox != null)
                _avatarBox.Location = new Float2(140f, (TopBarHeight - 36f) * 0.5f);

            if (_playerNameLabel != null)
            {
                _playerNameLabel.Location = new Float2(184f, 0f);
                _playerNameLabel.Size = new Float2(120f, TopBarHeight);
            }

            if (_playerLevelTag != null)
            {
                _playerLevelTag.Location = new Float2(304f, (TopBarHeight - 20f) * 0.5f);
                _playerLevelTag.Size = new Float2(56f, 20f);
            }

            // 顶栏右侧簇：右→左依次排布
            if (_topBarRightItems != null)
            {
                float cursor = rightWidth - 24f;
                for (int i = 0; i < _topBarRightItems.Length; i++)
                {
                    var item = _topBarRightItems[i];
                    if (item?.Control == null)
                        continue;

                    cursor -= item.Width;
                    item.Control.Location = new Float2(cursor, item.Top);
                    item.Control.Size = new Float2(item.Width, item.Height);
                    cursor -= item.GapAfter;
                }
            }

            // ---- 内容区 ----
            float contentHeight = Mathf.Max(320f, h - TopBarHeight - ActionBarHeight);

            if (_contentArea != null)
            {
                _contentArea.Location = new Float2(LeftNavWidth, TopBarHeight);
                _contentArea.Size = new Float2(rightWidth, contentHeight);
            }

            // 分类标签栏
            if (_categoryPanel != null)
            {
                _categoryPanel.Location = Float2.Zero;
                _categoryPanel.Size = new Float2(CategoryTabsWidth, contentHeight);
            }

            if (_categoryHeader != null)
            {
                _categoryHeader.Location = new Float2(20f, 10f);
                _categoryHeader.Size = new Float2(CategoryTabsWidth - 40f, 20f);
            }

            if (_categoryHeaderDivider != null)
            {
                _categoryHeaderDivider.Location = new Float2(12f, 40f);
                _categoryHeaderDivider.Size = new Float2(CategoryTabsWidth - 24f, 1f);
            }

            if (_categoryRows != null)
            {
                for (int i = 0; i < _categoryRows.Length; i++)
                {
                    if (_categoryRows[i] == null)
                        continue;
                    _categoryRows[i].Location = new Float2(0f, CategoryRowTop + i * CategoryRowHeight);
                    _categoryRows[i].Size = new Float2(CategoryTabsWidth, CategoryRowHeight);
                }
            }

            // 设置列表
            float listWidth = Mathf.Max(560f, rightWidth - CategoryTabsWidth);
            if (_settingsList != null)
            {
                _settingsList.Location = new Float2(CategoryTabsWidth, 0f);
                _settingsList.Size = new Float2(listWidth, contentHeight);
            }

            float titleWidth = Mathf.Max(150f, (_listTitle?.Text?.ToString()?.Length ?? 4) * 30f);
            if (_listTitle != null)
            {
                _listTitle.Location = new Float2(ListPadding, 14f);
                _listTitle.Size = new Float2(titleWidth, 40f);
            }

            if (_listTag != null)
            {
                _listTag.Location = new Float2(ListPadding + titleWidth + 12f, 24f);
                _listTag.Size = new Float2(78f, 22f);
            }

            if (_listSubtitle != null)
            {
                _listSubtitle.Location = new Float2(ListPadding, 56f);
                _listSubtitle.Size = new Float2(Mathf.Max(260f, listWidth - ListPadding * 3f - SearchBoxWidth), 20f);
            }

            if (_searchBox != null)
            {
                _searchBox.Location = new Float2(listWidth - ListPadding - SearchBoxWidth, 22f);
                _searchBox.Size = new Float2(SearchBoxWidth, SearchBoxHeight);
            }

            if (_searchGlyph != null)
            {
                _searchGlyph.Location = new Float2(12f, (SearchBoxHeight - 16f) * 0.5f);
                _searchGlyph.Size = new Float2(16f, 16f);
            }

            if (_searchHint != null)
            {
                _searchHint.Location = new Float2(36f, 0f);
                _searchHint.Size = new Float2(SearchBoxWidth - 48f, SearchBoxHeight);
            }

            // 分组与设置项
            float rowWidth = Mathf.Max(360f, listWidth - ListPadding * 2f);
            float y = ListHeaderBottom;
            int rowIndex = 0;

            int groupCount = _sectionLabels?.Length ?? 0;
            for (int group = 0; group < groupCount; group++)
            {
                if (_sectionLabels[group] != null)
                {
                    _sectionLabels[group].Location = new Float2(ListPadding, y);
                    _sectionLabels[group].Size = new Float2(rowWidth, SectionLabelHeight);
                }

                y += SectionLabelHeight;

                if (group < GroupRowCounts.Length)
                {
                    for (int k = 0; k < GroupRowCounts[group]; k++)
                    {
                        if (_rows == null || rowIndex >= _rows.Length)
                            break;

                        var row = _rows[rowIndex++];
                        LayoutRow(row, ListPadding, y, rowWidth);
                        y += (row?.Height ?? RowHeight) + RowGap;
                    }

                    y += GroupGap;
                }
                else if (_infoPanel != null)
                {
                    _infoPanel.Location = new Float2(ListPadding, y);
                    _infoPanel.Size = new Float2(rowWidth, InfoPanelHeight);
                    LayoutInfoPanel(rowWidth);
                    y += InfoPanelHeight;
                }
            }

            // ---- 底部操作栏 ----
            if (_actionBar != null)
            {
                _actionBar.Location = new Float2(LeftNavWidth, h - ActionBarHeight);
                _actionBar.Size = new Float2(rightWidth, ActionBarHeight);
            }

            if (_actionInfoLabel != null)
            {
                _actionInfoLabel.Location = new Float2(24f, 0f);
                _actionInfoLabel.Size = new Float2(420f, ActionBarHeight);
            }

            float buttonY = (ActionBarHeight - 36f) * 0.5f;
            float buttonX = rightWidth - 24f;

            if (_restartButton != null)
            {
                buttonX -= ActionButtonWidth;
                _restartButton.Location = new Float2(buttonX, buttonY);
                buttonX -= 12f;
            }

            if (_saveButton != null)
            {
                buttonX -= ActionButtonWidth;
                _saveButton.Location = new Float2(buttonX, buttonY);
                buttonX -= 12f;
            }

            if (_resetButton != null)
            {
                buttonX -= ActionButtonWidth;
                _resetButton.Location = new Float2(buttonX, buttonY);
            }
        }

        /// <summary>
        /// 排布单个设置项内部的标题 / 说明 / 角标 / 右侧控件 / 预览胶囊。
        /// </summary>
        /// <param name="row">设置项</param>
        /// <param name="x">设置项 X 坐标</param>
        /// <param name="y">设置项 Y 坐标</param>
        /// <param name="width">设置项宽度</param>
        private void LayoutRow(SettingRow row, float x, float y, float width)
        {
            if (row?.Panel == null)
                return;

            row.Panel.Location = new Float2(x, y);
            row.Panel.Size = new Float2(width, row.Height);

            string title = row.TitleLabel?.Text ?? string.Empty;

            // 标题按 18px/字估算：15f Heading 的中文实宽约 17~18px，按 15 估算会让标签压到标题上（截图实证）。
            float titleWidth = Mathf.Max(60f, title.Length * 18f + 8f);
            if (row.TitleLabel != null)
            {
                row.TitleLabel.Location = new Float2(RowInnerPadding, 7f);
                row.TitleLabel.Size = new Float2(titleWidth, 22f);
            }

            if (row.Tag != null)
            {
                string tagText = row.Tag.Text ?? string.Empty;
                row.Tag.Location = new Float2(RowInnerPadding + titleWidth + 8f, 9f);
                row.Tag.Size = new Float2(Mathf.Max(34f, tagText.Length * 12f + 12f), 20f);
            }

            if (row.DescLabel != null)
            {
                row.DescLabel.Location = new Float2(RowInnerPadding, 29f);
                row.DescLabel.Size = new Float2(Mathf.Max(220f, Mathf.Min(560f, width - 300f)), 20f);
            }

            switch (row.Kind)
            {
                case SettingRowKind.Dropdown:
                    if (row.Trigger != null)
                    {
                        row.Trigger.Location = new Float2(width - RowInnerPadding - DropdownWidth, (row.Height - DropdownHeight) * 0.5f);
                        row.Trigger.Size = new Float2(DropdownWidth, DropdownHeight);
                    }
                    break;

                case SettingRowKind.Slider:
                    if (row.Slider != null)
                    {
                        row.Slider.Location = new Float2(
                            width - RowInnerPadding - SliderValueWidth - 12f - SliderWidth,
                            (row.Height - SliderHeight) * 0.5f);
                        row.Slider.Size = new Float2(SliderWidth, SliderHeight);
                    }
                    if (row.ValueLabel != null)
                    {
                        row.ValueLabel.Location = new Float2(width - RowInnerPadding - SliderValueWidth, 0f);
                        row.ValueLabel.Size = new Float2(SliderValueWidth, row.Height);
                    }
                    break;

                default:
                    if (row.Toggle != null)
                    {
                        row.Toggle.Location = new Float2(width - RowInnerPadding - ToggleWidth, (row.Height - ToggleHeight) * 0.5f);
                        row.Toggle.Size = new Float2(ToggleWidth, ToggleHeight);
                    }
                    if (row.StatusLabel != null)
                    {
                        row.StatusLabel.Location = new Float2(
                            width - RowInnerPadding - ToggleWidth - 8f - ToggleStatusWidth, 0f);
                        row.StatusLabel.Size = new Float2(ToggleStatusWidth, row.Height);
                    }
                    break;
            }

            if (row.Chips != null)
            {
                for (int i = 0; i < row.Chips.Length; i++)
                {
                    if (row.Chips[i] == null)
                        continue;
                    row.Chips[i].Location = new Float2(RowInnerPadding + i * (ChipWidth + 8f), row.Height - ChipHeight - 8f);
                    row.Chips[i].Size = new Float2(ChipWidth, ChipHeight);
                }
            }
        }

        /// <summary>
        /// 排布画面信息面板的三列（显卡 / 显存占用 / 当前帧率）。
        /// </summary>
        /// <param name="width">面板宽度</param>
        private void LayoutInfoPanel(float width)
        {
            if (_infoLabels == null || _infoValues == null)
                return;

            int count = Mathf.Min(_infoLabels.Length, _infoValues.Length);
            if (count <= 0)
                return;

            float columnWidth = width / count;
            for (int i = 0; i < count; i++)
            {
                if (_infoLabels[i] != null)
                {
                    _infoLabels[i].Location = new Float2(24f + i * columnWidth, 16f);
                    _infoLabels[i].Size = new Float2(columnWidth - 24f, 18f);
                }

                if (_infoValues[i] != null)
                {
                    _infoValues[i].Location = new Float2(24f + i * columnWidth, 40f);
                    _infoValues[i].Size = new Float2(columnWidth - 24f, 20f);
                }
            }

            if (_infoFootnote != null)
            {
                _infoFootnote.Location = new Float2(24f, 72f);
                _infoFootnote.Size = new Float2(Mathf.Max(200f, width - 48f), 18f);
            }
        }

        /// <summary>
        /// 基于当前控件实际尺寸刷新布局。
        /// 由 <see cref="InkPageShell"/> 在页面挂载后与父容器尺寸变化时调用。
        /// </summary>
        public void RefreshLayout()
        {
            float w = Width;
            float h = Height;
            if (w <= 0f || h <= 0f)
            {
                var screen = FlaxEngine.Screen.Size;
                w = screen.X;
                h = screen.Y;
            }
            if (w <= 0f)
                w = 1920f;
            if (h <= 0f)
                h = 1080f;

            _screenSize = new Float2(w, h);
            ApplyLayout();
        }

        // ===================================================================
        // 辅助
        // =======================================================================

        /// <summary>
        /// 创建统一风格的文本标签。
        /// </summary>
        /// <param name="text">文本</param>
        /// <param name="role">字体角色</param>
        /// <param name="fontSize">字号</param>
        /// <param name="color">文字色（必须取自设计 Token）</param>
        /// <param name="horizontal">水平对齐</param>
        /// <param name="vertical">垂直对齐</param>
        /// <returns>标签控件</returns>
        private static Label CreateLabel(string text, InkWashTheme.FontRole role, float fontSize, Color color,
            TextAlignment horizontal, TextAlignment vertical)
        {
            return new Label
            {
                Text = text,
                Font = InkRenderHelper.GetFontRef(role, fontSize),
                TextColor = color,
                HorizontalAlignment = horizontal,
                VerticalAlignment = vertical,
            };
        }

        // ===================================================================
        // 数据结构
        // =======================================================================

        /// <summary>设置项右侧控件类型</summary>
        private enum SettingRowKind
        {
            /// <summary>下拉选择器</summary>
            Dropdown,

            /// <summary>滑动条</summary>
            Slider,

            /// <summary>开关</summary>
            Toggle,
        }

        /// <summary>
        /// 单个设置项的数据结构（设计稿 .setting-item）。
        /// </summary>
        private sealed class SettingRow
        {
            /// <summary>设置项键（用于回调分发与持久化）</summary>
            public string Key;

            /// <summary>右侧控件类型</summary>
            public SettingRowKind Kind;

            /// <summary>行高</summary>
            public float Height = RowHeight;

            /// <summary>卡片面板</summary>
            public InkPanel Panel;

            /// <summary>设置项名称</summary>
            public Label TitleLabel;

            /// <summary>设置项说明</summary>
            public Label DescLabel;

            /// <summary>可选角标（推荐 / 实验）</summary>
            public InkItemTag Tag;

            /// <summary>下拉触发器（Dropdown）</summary>
            public InkDropdown Trigger;

            /// <summary>下拉选项（Dropdown）</summary>
            public string[] Options;

            /// <summary>下拉当前档位（Dropdown）</summary>
            public int OptionIndex;

            /// <summary>滑动条（Slider）</summary>
            public Slider Slider;

            /// <summary>滑动条数值文字（Slider）</summary>
            public Label ValueLabel;

            /// <summary>滑动条单位（Slider）</summary>
            public string Unit = string.Empty;

            /// <summary>开关（Toggle）</summary>
            public InkToggle Toggle;

            /// <summary>开关状态文字（Toggle）</summary>
            public Label StatusLabel;

            /// <summary>画质预览胶囊（仅画面质量行）</summary>
            public InkChip[] Chips;
        }

        /// <summary>
        /// 顶栏右侧簇的排布项（右→左依次摆放）。
        /// </summary>
        private sealed class TopBarItem
        {
            /// <summary>控件</summary>
            public readonly Control Control;

            /// <summary>宽度</summary>
            public readonly float Width;

            /// <summary>高度</summary>
            public readonly float Height;

            /// <summary>顶栏内的 Y 坐标</summary>
            public readonly float Top;

            /// <summary>与左侧相邻元素之间的间距</summary>
            public readonly float GapAfter;

            /// <summary>
            /// 构造排布项。
            /// </summary>
            public TopBarItem(Control control, float width, float height, float top, float gapAfter)
            {
                Control = control;
                Width = width;
                Height = height;
                Top = top;
                GapAfter = gapAfter;
            }
        }

        // ===================================================================
        // 私有控件
        // =======================================================================

        /// <summary>
        /// 侧栏导航行 / 分类标签行（设计稿 .nav-item / .cat-tab）。
        /// 激活态绘制左侧 3px 金竖线与微金底，可选右侧计数徽章。
        /// </summary>
        private sealed class InkNavRow : ContainerControl
        {
            /// <summary>图标槽中心 X 坐标</summary>
            private const float IconCenterX = 28f;

            /// <summary>文字左边距（图标之后）</summary>
            private const float TextLeft = 46f;

            /// <summary>激活态左竖线宽度</summary>
            private const float ActiveBarWidth = 3f;

            /// <summary>徽章宽度</summary>
            private const float BadgeWidth = 34f;

            private readonly Label _text;
            private readonly Label _badge;
            private bool _active;
            private bool _hovered;
            private bool _pressed;

            /// <summary>点击事件</summary>
            public event Action Clicked;

            /// <summary>是否激活</summary>
            public bool Active
            {
                get => _active;
                set
                {
                    _active = value;
                    ApplyTextColors();
                }
            }

            /// <summary>行文案</summary>
            public string Text
            {
                get => _text.Text;
                set => _text.Text = value;
            }

            /// <summary>
            /// 构造导航行。
            /// </summary>
            /// <param name="text">文案</param>
            /// <param name="badge">右侧计数徽章文案（可空）</param>
            /// <param name="fontSize">字号</param>
            public InkNavRow(string text, string badge, float fontSize)
            {
                BackgroundColor = Color.Transparent;
                ClipChildren = false;

                _text = new Label
                {
                    Text = text,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, fontSize),
                    TextColor = InkWashTheme.PaperAged,
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                };
                AddChild(_text);

                if (!string.IsNullOrEmpty(badge))
                {
                    _badge = new Label
                    {
                        Text = badge,
                        Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 11f),
                        TextColor = InkWashTheme.TextTertiary,
                        HorizontalAlignment = TextAlignment.Center,
                        VerticalAlignment = TextAlignment.Center,
                    };
                    AddChild(_badge);
                }

                ApplyTextColors();
            }

            /// <inheritdoc />
            protected override void OnSizeChanged()
            {
                base.OnSizeChanged();

                if (_text != null)
                {
                    float reserved = _badge != null ? BadgeWidth + 16f : 12f;
                    _text.Location = new Float2(TextLeft, 0f);
                    _text.Size = new Float2(Mathf.Max(0f, Width - TextLeft - reserved), Height);
                }

                if (_badge != null)
                {
                    _badge.Location = new Float2(Mathf.Max(0f, Width - BadgeWidth - 16f), (Height - 20f) * 0.5f);
                    _badge.Size = new Float2(BadgeWidth, 20f);
                }
            }

            /// <summary>
            /// 依据激活态刷新文字与徽章配色。
            /// </summary>
            private void ApplyTextColors()
            {
                if (_text != null)
                    _text.TextColor = _active ? InkWashTheme.GoldBright : InkWashTheme.PaperAged;

                if (_badge != null)
                {
                    _badge.TextColor = _active ? InkWashTheme.GoldBright : InkWashTheme.TextTertiary;
                    _badge.BackgroundColor = Color.Transparent;
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
                _pressed = false;
            }

            /// <inheritdoc />
            public override bool OnMouseDown(Float2 location, MouseButton button)
            {
                if (button == MouseButton.Left)
                {
                    _pressed = true;
                    return true;
                }
                return base.OnMouseDown(location, button);
            }

            /// <inheritdoc />
            public override bool OnMouseUp(Float2 location, MouseButton button)
            {
                if (button == MouseButton.Left && _pressed)
                {
                    _pressed = false;
                    if (ContainsPoint(ref location))
                    {
                        try
                        {
                            Clicked?.Invoke();
                        }
                        catch (Exception ex)
                        {
                            FlaxEngine.Debug.LogError($"[SettingsPage] 导航行点击失败: {ex.Message}");
                        }
                    }
                    return true;
                }
                return base.OnMouseUp(location, button);
            }

            /// <inheritdoc />
            public override void Draw()
            {
                if (!Visible || Width <= 0f || Height <= 0f)
                    return;

                // 背景（激活态微金底 / 悬停态淡金底）
                if (_active)
                {
                    Render2D.FillRectangle(new Rectangle(0f, 0f, Width, Height), InkWashTheme.GoldPrimary * 0.1f);
                }
                else if (_hovered)
                {
                    Render2D.FillRectangle(new Rectangle(0f, 0f, Width, Height), InkWashTheme.GoldPrimary * 0.05f);
                }

                // 图标槽：金色菱形轮廓（设计稿 svg 图标的等价占位）
                DrawDiamond(new Float2(IconCenterX, Height * 0.5f), 6f,
                    _active ? InkWashTheme.GoldBright : InkWashTheme.PaperDark);

                // 计数徽章底
                if (_badge != null && Width > 0f)
                {
                    InkRenderHelper.FillRoundedRectangle(
                        new Rectangle(_badge.Location.X, _badge.Location.Y, _badge.Size.X, _badge.Size.Y),
                        InkWashTheme.RadiusSm,
                        _active ? InkWashTheme.GoldPrimary * 0.12f : InkWashTheme.BaseElevated);
                }

                base.Draw();

                // 激活态左竖线
                if (_active)
                {
                    Render2D.FillRectangle(new Rectangle(0f, 0f, ActiveBarWidth, Height), InkWashTheme.GoldPrimary);
                }
            }

            /// <summary>
            /// 绘制菱形轮廓图标。
            /// </summary>
            private static void DrawDiamond(Float2 center, float radius, Color color)
            {
                var top = new Float2(center.X, center.Y - radius);
                var right = new Float2(center.X + radius, center.Y);
                var bottom = new Float2(center.X, center.Y + radius);
                var left = new Float2(center.X - radius, center.Y);

                Render2D.DrawLine(top, right, color, 1f);
                Render2D.DrawLine(right, bottom, color, 1f);
                Render2D.DrawLine(bottom, left, color, 1f);
                Render2D.DrawLine(left, top, color, 1f);
            }
        }

        /// <summary>
        /// 下拉选择触发器（设计稿 .dropdown-trigger）：左侧当前值文字 + 右侧金线雪佛龙。
        /// 点击切换至下一档选项。
        /// </summary>
        private sealed class InkDropdown : ContainerControl
        {
            private readonly Label _valueLabel;
            private bool _hovered;
            private bool _pressed;

            /// <summary>点击事件</summary>
            public event Action Clicked;

            /// <summary>当前值文字</summary>
            public string Text
            {
                get => _valueLabel.Text;
                set => _valueLabel.Text = value;
            }

            /// <summary>
            /// 构造下拉触发器。
            /// </summary>
            public InkDropdown()
            {
                BackgroundColor = Color.Transparent;
                ClipChildren = false;

                _valueLabel = new Label
                {
                    Text = string.Empty,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 13f),
                    TextColor = InkWashTheme.PaperBright,
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                    Location = new Float2(14f, 0f),
                };
                AddChild(_valueLabel);
            }

            /// <inheritdoc />
            protected override void OnSizeChanged()
            {
                base.OnSizeChanged();
                if (_valueLabel != null)
                {
                    _valueLabel.Location = new Float2(14f, 0f);
                    _valueLabel.Size = new Float2(Mathf.Max(0f, Width - 36f), Height);
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
                _pressed = false;
            }

            /// <inheritdoc />
            public override bool OnMouseDown(Float2 location, MouseButton button)
            {
                if (button == MouseButton.Left)
                {
                    _pressed = true;
                    return true;
                }
                return base.OnMouseDown(location, button);
            }

            /// <inheritdoc />
            public override bool OnMouseUp(Float2 location, MouseButton button)
            {
                if (button == MouseButton.Left && _pressed)
                {
                    _pressed = false;
                    if (ContainsPoint(ref location))
                    {
                        try
                        {
                            Clicked?.Invoke();
                        }
                        catch (Exception ex)
                        {
                            FlaxEngine.Debug.LogError($"[SettingsPage] 下拉切换失败: {ex.Message}");
                        }
                    }
                    return true;
                }
                return base.OnMouseUp(location, button);
            }

            /// <inheritdoc />
            public override void Draw()
            {
                if (!Visible || Width <= 0f || Height <= 0f)
                    return;

                var rect = new Rectangle(0f, 0f, Width, Height);
                InkRenderHelper.FillRoundedRectangle(rect, InkWashTheme.RadiusSm,
                    _hovered ? InkWashTheme.GoldTrace : InkWashTheme.BaseTertiary);
                InkRenderHelper.DrawRoundedRectangle(rect, InkWashTheme.RadiusSm,
                    _hovered ? InkWashTheme.BorderGold : InkWashTheme.BorderNeutralL2, 1f);

                base.Draw();

                // 雪佛龙（下拉指示）
                var color = _hovered ? InkWashTheme.GoldBright : InkWashTheme.TextTertiary;
                float cx = Width - 18f;
                float cy = Height * 0.5f - 2f;
                Render2D.DrawLine(new Float2(cx - 5f, cy), new Float2(cx, cy + 5f), color, 1.5f);
                Render2D.DrawLine(new Float2(cx, cy + 5f), new Float2(cx + 5f, cy), color, 1.5f);
            }
        }

        /// <summary>
        /// 开关（设计稿 .toggle-switch）：48×24 胶囊 + 圆形滑块，点击切换。
        /// </summary>
        private sealed class InkToggle : ContainerControl
        {
            private bool _checked;

            /// <summary>状态变化事件</summary>
            public event Action<bool> CheckedChanged;

            /// <summary>是否开启</summary>
            public bool Checked
            {
                get => _checked;
                set => _checked = value;
            }

            /// <summary>
            /// 构造开关。
            /// </summary>
            public InkToggle()
            {
                BackgroundColor = Color.Transparent;
                ClipChildren = false;
                AutoFocus = false;
            }

            /// <inheritdoc />
            public override bool OnMouseDown(Float2 location, MouseButton button)
            {
                if (button == MouseButton.Left)
                {
                    _checked = !_checked;
                    try
                    {
                        CheckedChanged?.Invoke(_checked);
                    }
                    catch (Exception ex)
                    {
                        FlaxEngine.Debug.LogError($"[SettingsPage] 开关切换失败: {ex.Message}");
                    }
                    return true;
                }
                return base.OnMouseDown(location, button);
            }

            /// <inheritdoc />
            public override void Draw()
            {
                if (!Visible || Width <= 0f || Height <= 0f)
                    return;

                var rect = new Rectangle(0f, 0f, Width, Height);
                float radius = Height * 0.5f;

                InkRenderHelper.FillRoundedRectangle(rect, radius,
                    _checked ? InkWashTheme.GoldPrimary * 0.3f : InkWashTheme.BaseElevated);
                InkRenderHelper.DrawRoundedRectangle(rect, radius,
                    _checked ? InkWashTheme.GoldDeep : InkWashTheme.BorderNeutralL2, 1f);

                float thumbRadius = (Height - 4f) * 0.5f;
                var center = new Float2(
                    _checked ? Width - 2f - thumbRadius : 2f + thumbRadius,
                    Height * 0.5f);
                InkRenderHelper.FillCircle(center, thumbRadius,
                    _checked ? InkWashTheme.GoldBright : InkWashTheme.PaperDark);
            }
        }

        /// <summary>
        /// 画质预览胶囊（设计稿 .quality-preview-item）。
        /// </summary>
        private sealed class InkChip : ContainerControl
        {
            private readonly Label _label;
            private bool _active;
            private bool _hovered;
            private bool _pressed;

            /// <summary>点击事件</summary>
            public event Action Clicked;

            /// <summary>是否激活</summary>
            public bool Active
            {
                get => _active;
                set
                {
                    _active = value;
                    if (_label != null)
                        _label.TextColor = value ? InkWashTheme.GoldBright : InkWashTheme.TextTertiary;
                }
            }

            /// <summary>胶囊文案</summary>
            public string Text
            {
                get => _label.Text;
                set => _label.Text = value;
            }

            /// <summary>
            /// 构造胶囊。
            /// </summary>
            public InkChip()
            {
                BackgroundColor = Color.Transparent;
                ClipChildren = false;

                _label = new Label
                {
                    Text = string.Empty,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 10f),
                    TextColor = InkWashTheme.TextTertiary,
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                    AnchorPreset = AnchorPresets.StretchAll,
                    Offsets = Margin.Zero,
                };
                AddChild(_label);
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
                _pressed = false;
            }

            /// <inheritdoc />
            public override bool OnMouseDown(Float2 location, MouseButton button)
            {
                if (button == MouseButton.Left)
                {
                    _pressed = true;
                    return true;
                }
                return base.OnMouseDown(location, button);
            }

            /// <inheritdoc />
            public override bool OnMouseUp(Float2 location, MouseButton button)
            {
                if (button == MouseButton.Left && _pressed)
                {
                    _pressed = false;
                    if (ContainsPoint(ref location))
                    {
                        try
                        {
                            Clicked?.Invoke();
                        }
                        catch (Exception ex)
                        {
                            FlaxEngine.Debug.LogError($"[SettingsPage] 画质胶囊点击失败: {ex.Message}");
                        }
                    }
                    return true;
                }
                return base.OnMouseUp(location, button);
            }

            /// <inheritdoc />
            public override void Draw()
            {
                if (!Visible || Width <= 0f || Height <= 0f)
                    return;

                var rect = new Rectangle(0f, 0f, Width, Height);
                InkRenderHelper.FillRoundedRectangle(rect, InkWashTheme.RadiusSm,
                    _active ? InkWashTheme.GoldPrimary * 0.12f : InkWashTheme.Void);
                InkRenderHelper.DrawRoundedRectangle(rect, InkWashTheme.RadiusSm,
                    _active ? InkWashTheme.GoldPrimary
                        : (_hovered ? InkWashTheme.BorderGold : InkWashTheme.BorderNeutralL2), 1f);

                base.Draw();
            }
        }

        /// <summary>
        /// 分组标题（设计稿 .setting-section-label）：金色竖条 + 标题 + 右侧渐隐金线。
        /// </summary>
        private sealed class InkSectionLabel : ContainerControl
        {
            /// <summary>金色竖条高度</summary>
            private const float BarHeight = 14f;

            /// <summary>金色竖条宽度</summary>
            private const float BarWidth = 3f;

            /// <summary>标题与金线之间的间距</summary>
            private const float GapAfterText = 24f;

            private readonly Label _text;
            private readonly InkDivider _line;

            /// <summary>标题文案</summary>
            public string Text
            {
                get => _text.Text;
                set
                {
                    _text.Text = value;
                    Relayout();
                }
            }

            /// <summary>
            /// 构造分组标题。
            /// </summary>
            public InkSectionLabel()
            {
                BackgroundColor = Color.Transparent;
                ClipChildren = false;

                _text = new Label
                {
                    Text = string.Empty,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 15f),
                    TextColor = InkWashTheme.GoldPrimary,
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                    Location = new Float2(BarWidth + 9f, 0f),
                };
                AddChild(_text);

                _line = new InkDivider();
                AddChild(_line);
            }

            /// <inheritdoc />
            protected override void OnSizeChanged()
            {
                base.OnSizeChanged();
                Relayout();
            }

            /// <summary>
            /// 依据标题文字长度重排标题与右侧金线。
            /// </summary>
            private void Relayout()
            {
                if (_text == null || _line == null)
                    return;

                float textWidth = Mathf.Max(60f, (_text.Text?.ToString()?.Length ?? 0) * 16f);

                _text.Location = new Float2(BarWidth + 9f, 0f);
                _text.Size = new Float2(textWidth, Height);

                float lineX = BarWidth + 9f + textWidth + GapAfterText;
                _line.Location = new Float2(lineX, Height * 0.5f);
                _line.Size = new Float2(Mathf.Max(0f, Width - lineX), 1f);
            }

            /// <inheritdoc />
            public override void Draw()
            {
                if (!Visible || Width <= 0f || Height <= 0f)
                    return;

                float barY = (Height - BarHeight) * 0.5f;
                Render2D.FillRectangle(new Rectangle(0f, barY, BarWidth, BarHeight * 0.5f), InkWashTheme.GoldBright);
                Render2D.FillRectangle(new Rectangle(0f, barY + BarHeight * 0.5f, BarWidth, BarHeight * 0.5f), InkWashTheme.GoldDeep);

                base.Draw();
            }
        }

        /// <summary>
        /// 设置项角标（设计稿 .setting-tag-recommend / .setting-tag-experimental）。
        /// 底色与边框由 <see cref="Accent"/> 派生，文字色即 Accent 本身。
        /// </summary>
        private sealed class InkItemTag : Label
        {
            private Color _accent = InkWashTheme.JadeBright;

            /// <summary>强调色（推荐=翡翠亮，实验=朱红亮，画质推荐=鎏金）</summary>
            public Color Accent
            {
                get => _accent;
                set
                {
                    _accent = value;
                    TextColor = value;
                }
            }

            /// <summary>
            /// 构造角标。
            /// </summary>
            public InkItemTag()
            {
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f);
                HorizontalAlignment = TextAlignment.Center;
                VerticalAlignment = TextAlignment.Center;
                TextColor = _accent;
            }

            /// <inheritdoc />
            public override void Draw()
            {
                if (!Visible || Width <= 0f || Height <= 0f)
                    return;

                var rect = new Rectangle(0f, 0f, Width, Height);
                InkRenderHelper.FillRoundedRectangle(rect, InkWashTheme.RadiusSm,
                    new Color(_accent.R, _accent.G, _accent.B, 0.12f));
                InkRenderHelper.DrawRoundedRectangle(rect, InkWashTheme.RadiusSm,
                    new Color(_accent.R, _accent.G, _accent.B, 0.55f), 1f);

                base.Draw();
            }
        }

        /// <summary>
        /// 搜索框放大镜图标（设计稿 .settings-search 的 svg 图标等价绘制）。
        /// </summary>
        private sealed class InkSearchGlyph : ContainerControl
        {
            /// <summary>
            /// 构造图标。
            /// </summary>
            public InkSearchGlyph()
            {
                BackgroundColor = Color.Transparent;
                ClipChildren = false;
            }

            /// <inheritdoc />
            public override void Draw()
            {
                if (!Visible || Width <= 0f || Height <= 0f)
                    return;

                InkRenderHelper.DrawCircle(new Float2(6.5f, 6.5f), 5f, InkWashTheme.TextTertiary, 1.4f);
                Render2D.DrawLine(new Float2(10.5f, 10.5f), new Float2(14.5f, 14.5f), InkWashTheme.TextTertiary, 1.4f);
            }
        }

        /// <summary>
        /// 底部操作栏修改提示圆点（设计稿 .change-dot，朱红带辉光）。
        /// </summary>
        private float _fpsRefreshTimer;

        /// <summary>顶栏是否已经拿到真实玩家信息（进世界瞬间构造的页面会拿不到，需自愈一次）。</summary>
        private bool _headerBound;
        private float _headerBindTimer;
        private int _headerBindAttempts;

        /// <summary>当前帧率每 0.5 秒刷一次；顺带在玩家就绪后补绑一次顶栏身份。</summary>
        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);

            if (!_headerBound)
            {
                _headerBindTimer += deltaTime;
                if (_headerBindTimer >= 0.5f && _headerBindAttempts < 20)
                {
                    _headerBindTimer = 0f;
                    _headerBindAttempts++;
                    InkPlayerHeaderBinder.Bind(_playerNameLabel, _playerLevelTag, _coinValueLabel);
                    _headerBound = _playerNameLabel != null
                        && !string.IsNullOrEmpty(_playerNameLabel.Text)
                        && _playerNameLabel.Text != "—";
                }
            }

            if (!VisibleInHierarchy || _infoValues == null || _infoValues.Length < 3 || _infoValues[2] == null)
                return;

            _fpsRefreshTimer += deltaTime;
            if (_fpsRefreshTimer < 0.5f)
                return;
            _fpsRefreshTimer = 0f;

            try
            {
                _infoValues[2].Text = $"{Engine.FramesPerSecond} fps";
                if (_infoValues[1] != null)
                    _infoValues[1].Text = ReadGpuInfo().Vram;
            }
            catch
            {
                _infoValues[2].Text = "—";
            }
        }

    }
}
