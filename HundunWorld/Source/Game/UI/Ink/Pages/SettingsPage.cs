using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.Ink;
using HundunWorld.Game.UI.StyleSystem;
using System;
using HundunWorld.Game.Services;
using HundunWorld.Game.Audio;

namespace HundunWorld.Game.UI.Ink.Pages
{
    /// <summary>
    /// 设置页面。
    /// 承载 3 个子区域：
    /// <list type="bullet">
    ///   <item>SubTask 12.1 顶部标题栏（<see cref="InkPanelTitle"/>，文本"设置"）</item>
    ///   <item>SubTask 12.2 左侧分类侧边栏（<see cref="InkPanel"/> + 4 个 <see cref="InkListItem"/>：画面/音效/操作/系统）</item>
    ///   <item>SubTask 12.3 右侧设置项列表（<see cref="InkPanel"/> + 6 个 <see cref="InkListItem"/>：全屏模式/分辨率/画面质量/主音量/音效音量/操作模式）</item>
    /// </list>
    /// 返回按钮由 <see cref="InkPageShell"/> 自动添加的 InkBackButton 承载，本页面不自建。
    /// 全部数据为 mock，通过 <see cref="RefreshLayout"/> 支持屏幕尺寸变化。
    /// 侧边栏点击切换 active 态、分辨率项点击循环切换 mock 文本，均通过
    /// <see cref="OnMouseDown"/> 命中检测实现，不引入额外覆盖控件。
    /// </summary>
    public class SettingsPage : ContainerControl, IInkPage
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

        /// <summary>左侧分类侧边栏 X 坐标</summary>
        private const float SidebarX = 40f;

        /// <summary>左侧分类侧边栏宽度</summary>
        private const float SidebarWidth = 200f;

        /// <summary>侧边栏列表项高度（像素）</summary>
        private const float SidebarItemHeight = 48f;

        /// <summary>右侧设置项列表 X 坐标</summary>
        private const float SettingsX = 260f;

        /// <summary>右侧设置项列表宽度公式中扣除的常量（宽度 = screenWidth - 此值）</summary>
        private const float SettingsWidthReserve = 300f;

        /// <summary>设置项高度（像素）</summary>
        private const float SettingItemHeight = 56f;

        /// <summary>设置项垂直间距（像素）</summary>
        private const float SettingItemGap = 8f;

        /// <summary>设置项数量</summary>
        private const int SettingItemCount = 6;

        /// <summary>左侧标签左边距</summary>
        private const float SettingLabelLeftMargin = 16f;

        /// <summary>左侧标签宽度</summary>
        private const float SettingLabelWidth = 120f;

        /// <summary>右侧控件右边距</summary>
        private const float SettingControlRightMargin = 16f;

        /// <summary>右侧 Slider 控件宽度</summary>
        private const float SliderWidth = 200f;

        /// <summary>右侧 Slider 控件高度</summary>
        private const float SliderHeight = 20f;

        /// <summary>右侧 CheckBox 控件尺寸（正方形）</summary>
        private const float CheckBoxSize = 20f;

        /// <summary>右侧分辨率 Label 宽度（容纳最长文本"2560×1440"）</summary>
        private const float ResolutionLabelWidth = 140f;

        /// <summary>右侧画面质量 Label 宽度</summary>
        private const float QualityLabelWidth = 80f;

        /// <summary>右侧操作模式 Label 宽度</summary>
        private const float ModeLabelWidth = 120f;

        // ===================================================================
        // mock 数据
        // =======================================================================

        /// <summary>侧边栏分类名（mock）</summary>
        private static readonly string[] Categories = { "画面", "音效", "操作", "系统" };

        /// <summary>可选分辨率列表（mock，点击分辨率项循环切换）</summary>
        private static readonly string[] Resolutions = { "1920×1080", "2560×1440", "1280×720" };

        /// <summary>分辨率宽度数组（与 <see cref="Resolutions"/> 一一对应，用于写入配置）</summary>
        private static readonly int[] ResolutionWidths = { 1920, 2560, 1280 };

        /// <summary>分辨率高度数组（与 <see cref="Resolutions"/> 一一对应，用于写入配置）</summary>
        private static readonly int[] ResolutionHeights = { 1080, 1440, 720 };

        // ===================================================================
        // 子控件引用
        // =======================================================================

        /// <summary>顶部标题栏</summary>
        private InkPanelTitle _title;

        /// <summary>左侧分类侧边栏面板</summary>
        private InkPanel _sidebarPanel;

        /// <summary>侧边栏分类项数组（4 项）</summary>
        private InkListItem[] _sidebarItems;

        /// <summary>右侧设置项列表面板</summary>
        private InkPanel _settingsPanel;

        /// <summary>设置项数组（6 项）</summary>
        private InkListItem[] _settingItems;

        /// <summary>每项右侧控件数组（与 <see cref="_settingItems"/> 一一对应）</summary>
        private Control[] _rightControls;

        /// <summary>分辨率 Label 引用（用于点击循环切换文本）</summary>
        private Label _resolutionLabel;

        /// <summary>画面质量 Label 引用（用于点击循环切换文本）</summary>
        private Label _qualityLabel;

        /// <summary>主音量 Slider 引用（用于回调中读取当前值）</summary>
        private Slider _masterVolumeSlider;

        /// <summary>音效音量 Slider 引用（用于回调中读取当前值）</summary>
        private Slider _sfxVolumeSlider;

        // ===================================================================
        // 状态
        // =======================================================================

        /// <summary>当前屏幕尺寸缓存，用于布局计算</summary>
        private Float2 _screenSize;

        /// <summary>当前选中的分辨率索引（mock）</summary>
        private int _resolutionIndex;

        /// <summary>当前选中的画质索引</summary>
        private int _qualityIndex;

        /// <summary>可选画质级别（显示文本）</summary>
        private static readonly string[] QualityLevels = { "低", "中", "高", "极高" };

        /// <summary>画质级别对应的配置键（传给 <see cref="GameConfigurationService"/>）</summary>
        private static readonly string[] QualityKeys = { "Low", "Medium", "High", "Ultra" };

        /// <summary>当前选中的分类索引（mock，默认 0 = 画面）</summary>
        private int _selectedCategory;

        // ===================================================================
        // 构造函数
        // =======================================================================

        /// <summary>
        /// 构造函数：初始化全部 3 个子区域，使用 mock 数据填充。
        /// 构造时读取 <see cref="FlaxEngine.Screen.Size"/> 计算布局，
        /// 屏幕尺寸未就绪时使用 1920x1080 兜底。
        /// </summary>
        public SettingsPage()
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

            try
            {
                BuildTitle();
                BuildSidebar();
                BuildSettingsList();

                // 应用初始布局（基于屏幕尺寸计算所有子控件位置与尺寸）
                ApplyLayout();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[SettingsPage] 初始化失败: {ex.Message}");
            }
        }

        // ===================================================================
        // SubTask 构造方法
        // =======================================================================

        /// <summary>
        /// SubTask 12.1：顶部标题栏。
        /// <see cref="InkPanelTitle"/> 文本"设置"，位置 (0, 0)，宽度铺满，高度 48。
        /// 返回按钮由 <see cref="InkPageShell"/> 自动添加，本页面不自建。
        /// </summary>
        private void BuildTitle()
        {
            _title = new InkPanelTitle
            {
                Title = "设置",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = Float2.Zero,
                Height = TitleHeight,
            };
            AddChild(_title);
        }

        /// <summary>
        /// SubTask 12.2：左侧分类侧边栏。
        /// <see cref="InkPanel"/> 容器位置 (40, 80)，尺寸 (200, screenHeight - 160)。
        /// 内含 4 个 <see cref="InkListItem"/>（垂直排列，每项高度 48）：
        /// 画面（默认 active）/音效/操作/系统。点击切换 active 态（mock 行为，
        /// 由 <see cref="OnMouseDown"/> 命中检测 + <see cref="SelectCategory"/> 实现）。
        /// </summary>
        private void BuildSidebar()
        {
            _sidebarPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
            };
            AddChild(_sidebarPanel);

            _sidebarItems = new InkListItem[Categories.Length];
            for (int i = 0; i < Categories.Length; i++)
            {
                var item = new InkListItem
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(0f, i * SidebarItemHeight),
                    Size = new Float2(SidebarWidth, SidebarItemHeight),
                    Active = (i == 0),
                };

                // 分类名 Label：Heading 字体 14px，左侧留白 16px
                var nameLabel = new Label
                {
                    Text = Categories[i],
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), 14f),
                    TextColor = InkWashTheme.TextDefault,
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(SettingLabelLeftMargin, 0f),
                    Size = new Float2(SidebarWidth - SettingLabelLeftMargin, SidebarItemHeight),
                };
                item.AddChild(nameLabel);

                _sidebarPanel.AddChild(item);
                _sidebarItems[i] = item;
            }

            _selectedCategory = 0;
        }

        /// <summary>
        /// SubTask 12.3：右侧设置项列表。
        /// <see cref="InkPanel"/> 容器位置 (260, 80)，尺寸 (screenWidth - 300, screenHeight - 160)。
        /// 内含 6 个设置项（垂直排列，每项高度 56，间距 8），每项是一个 <see cref="InkListItem"/>，
        /// 左侧为 <see cref="InkTextBlock"/> Body 样式标签，右侧为对应控件：
        /// <list type="number">
        ///   <item>全屏模式 + <see cref="CheckBox"/>（勾选，背景 <see cref="InkWashTheme.BaseTertiary"/>，边框 <see cref="InkWashTheme.BorderGold"/>）</item>
        ///   <item>分辨率 + <see cref="Label"/>（"1920×1080"，字色 <see cref="InkWashTheme.TextBrand"/>，可点击切换 mock）</item>
        ///   <item>画面质量 + <see cref="Label"/>（"高"，字色 <see cref="InkWashTheme.TextBrand"/>）</item>
        ///   <item>主音量 + <see cref="Slider"/>（0-100，值 80，背景 <see cref="InkWashTheme.BaseTertiary"/>，进度 <see cref="InkWashTheme.GoldPrimary"/>）</item>
        ///   <item>音效音量 + <see cref="Slider"/>（值 60）</item>
        ///   <item>操作模式 + <see cref="Label"/>（"键盘鼠标"，字色 <see cref="InkWashTheme.TextBrand"/>）</item>
        /// </list>
        /// </summary>
        private void BuildSettingsList()
        {
            _settingsPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
            };
            AddChild(_settingsPanel);

            _settingItems = new InkListItem[SettingItemCount];
            _rightControls = new Control[SettingItemCount];

            // 设置项标签（mock）
            string[] labels =
            {
                "全屏模式",
                "分辨率",
                "画面质量",
                "主音量",
                "音效音量",
                "操作模式",
            };

            float initialPanelWidth = _screenSize.X - SettingsWidthReserve;

            for (int i = 0; i < SettingItemCount; i++)
            {
                var item = new InkListItem
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(0f, i * (SettingItemHeight + SettingItemGap)),
                    Size = new Float2(initialPanelWidth, SettingItemHeight),
                };

                // 左侧标签：InkTextBlock Body 样式
                var labelText = new InkTextBlock(InkTextStyle.Body)
                {
                    Text = labels[i],
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(SettingLabelLeftMargin, 0f),
                    Size = new Float2(SettingLabelWidth, SettingItemHeight),
                };
                item.AddChild(labelText);

                // 右侧控件
                Control rightControl = BuildRightControl(i, initialPanelWidth);
                item.AddChild(rightControl);
                _rightControls[i] = rightControl;

                _settingsPanel.AddChild(item);
                _settingItems[i] = item;
            }
        }

        /// <summary>
        /// 构建指定设置项的右侧控件。
        /// </summary>
        /// <param name="index">设置项索引（0-5）</param>
        /// <param name="panelWidth">当前设置面板宽度，用于计算控件初始 X 坐标</param>
        /// <returns>右侧控件（CheckBox/Label/Slider）</returns>
        private Control BuildRightControl(int index, float panelWidth)
        {
            float centerY = (SettingItemHeight - CheckBoxSize) * 0.5f;

            switch (index)
            {
                case 0:
                {
                    // 全屏模式（垂直同步）：CheckBox，从 GameConfigurationService 读取初始值
                    bool initialVSync = true;
                    try
                    {
                        initialVSync = GameConfigurationService.Instance.GetVSync();
                    }
                    catch (Exception ex)
                    {
                        FlaxEngine.Debug.LogError($"[SettingsPage] 读取 VSync 失败: {ex.Message}");
                    }
                    var cb = new CheckBox
                    {
                        Checked = initialVSync,
                        BackgroundColor = InkWashTheme.BaseTertiary,
                        BorderColor = InkWashTheme.BorderGold,
                        Size = new Float2(CheckBoxSize, CheckBoxSize),
                        AnchorPreset = AnchorPresets.TopLeft,
                        Location = new Float2(panelWidth - CheckBoxSize - SettingControlRightMargin, centerY),
                    };
                    cb.StateChanged += OnVSyncStateChanged;
                    return cb;
                }

                case 1:
                {
                    // 分辨率：Label，从 GameConfigurationService 读取初始值并匹配到 mock 列表索引
                    int resIndex = 0;
                    try
                    {
                        var (w, h) = GameConfigurationService.Instance.GetResolution();
                        for (int i = 0; i < ResolutionWidths.Length; i++)
                        {
                            if (ResolutionWidths[i] == w && ResolutionHeights[i] == h)
                            {
                                resIndex = i;
                                break;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        FlaxEngine.Debug.LogError($"[SettingsPage] 读取分辨率失败: {ex.Message}");
                    }
                    _resolutionLabel = new Label
                    {
                        Text = Resolutions[resIndex],
                        Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 13f),
                        TextColor = InkWashTheme.TextBrand,
                        HorizontalAlignment = TextAlignment.Far,
                        VerticalAlignment = TextAlignment.Center,
                        AnchorPreset = AnchorPresets.TopLeft,
                        Location = new Float2(panelWidth - ResolutionLabelWidth - SettingControlRightMargin, 0f),
                        Size = new Float2(ResolutionLabelWidth, SettingItemHeight),
                    };
                    _resolutionIndex = resIndex;
                    return _resolutionLabel;
                }

                case 2:
                {
                    // 画面质量：Label，从 GameConfigurationService 读取初始值并匹配到 QualityLevels 索引
                    int qIndex = 2; // 默认"高"
                    try
                    {
                        string qualityKey = GameConfigurationService.Instance.GetGraphicsQuality();
                        for (int i = 0; i < QualityKeys.Length; i++)
                        {
                            if (string.Equals(QualityKeys[i], qualityKey, StringComparison.OrdinalIgnoreCase))
                            {
                                qIndex = i;
                                break;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        FlaxEngine.Debug.LogError($"[SettingsPage] 读取画质失败: {ex.Message}");
                    }
                    _qualityLabel = new Label
                    {
                        Text = QualityLevels[qIndex],
                        Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 13f),
                        TextColor = InkWashTheme.TextBrand,
                        HorizontalAlignment = TextAlignment.Far,
                        VerticalAlignment = TextAlignment.Center,
                        AnchorPreset = AnchorPresets.TopLeft,
                        Location = new Float2(panelWidth - QualityLabelWidth - SettingControlRightMargin, 0f),
                        Size = new Float2(QualityLabelWidth, SettingItemHeight),
                    };
                    _qualityIndex = qIndex;
                    return _qualityLabel;
                }

                case 3:
                {
                    // 主音量：Slider 0-100，从 GameConfigurationService 读取初始值
                    float masterVol = 80f;
                    try
                    {
                        masterVol = GameConfigurationService.Instance.GetMasterVolume() * 100f;
                    }
                    catch (Exception ex)
                    {
                        FlaxEngine.Debug.LogError($"[SettingsPage] 读取主音量失败: {ex.Message}");
                    }
                    float sliderY = (SettingItemHeight - SliderHeight) * 0.5f;
                    _masterVolumeSlider = new Slider
                    {
                        Minimum = 0f,
                        Maximum = 100f,
                        Value = masterVol,
                        Size = new Float2(SliderWidth, SliderHeight),
                        AnchorPreset = AnchorPresets.TopLeft,
                        Location = new Float2(panelWidth - SliderWidth - SettingControlRightMargin, sliderY),
                    };
                    _masterVolumeSlider.ValueChanged += OnMasterVolumeChanged;
                    return _masterVolumeSlider;
                }

                case 4:
                {
                    // 音效音量：Slider 0-100，从 GameConfigurationService 读取初始值
                    float sfxVol = 60f;
                    try
                    {
                        sfxVol = GameConfigurationService.Instance.GetSFXVolume() * 100f;
                    }
                    catch (Exception ex)
                    {
                        FlaxEngine.Debug.LogError($"[SettingsPage] 读取音效音量失败: {ex.Message}");
                    }
                    float sliderY = (SettingItemHeight - SliderHeight) * 0.5f;
                    _sfxVolumeSlider = new Slider
                    {
                        Minimum = 0f,
                        Maximum = 100f,
                        Value = sfxVol,
                        Size = new Float2(SliderWidth, SliderHeight),
                        AnchorPreset = AnchorPresets.TopLeft,
                        Location = new Float2(panelWidth - SliderWidth - SettingControlRightMargin, sliderY),
                    };
                    _sfxVolumeSlider.ValueChanged += OnSfxVolumeChanged;
                    return _sfxVolumeSlider;
                }

                default:
                {
                    // 操作模式：Label "键盘鼠标"，字色 TextBrand
                    var modeLabel = new Label
                    {
                        Text = "键盘鼠标",
                        Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 13f),
                        TextColor = InkWashTheme.TextBrand,
                        HorizontalAlignment = TextAlignment.Far,
                        VerticalAlignment = TextAlignment.Center,
                        AnchorPreset = AnchorPresets.TopLeft,
                        Location = new Float2(panelWidth - ModeLabelWidth - SettingControlRightMargin, 0f),
                        Size = new Float2(ModeLabelWidth, SettingItemHeight),
                    };
                    return modeLabel;
                }
            }
        }

        // ===================================================================
        // 配置变更回调
        // =======================================================================

        /// <summary>
        /// 垂直同步 CheckBox 状态变更回调：读取当前 Checked 状态并异步保存到配置服务。
        /// </summary>
        /// <param name="cb">触发事件的 CheckBox</param>
        private void OnVSyncStateChanged(CheckBox cb)
        {
            try
            {
                _ = GameConfigurationService.Instance.SetVSyncAsync(cb.Checked);
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[SettingsPage] 保存 VSync 失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 主音量 Slider 变更回调（无参数 Action）：读取当前 Slider 值，
        /// 实时应用到 <see cref="GameAudioManager.Instance.MasterVolume"/>，
        /// 并异步保存到配置服务。
        /// </summary>
        private void OnMasterVolumeChanged()
        {
            if (_masterVolumeSlider == null)
                return;
            try
            {
                float v = _masterVolumeSlider.Value / 100f;
                GameAudioManager.Instance.MasterVolume = v;
                _ = GameConfigurationService.Instance.SetMasterVolumeAsync(v);
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[SettingsPage] 保存主音量失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 音效音量 Slider 变更回调（无参数 Action）：读取当前 Slider 值，
        /// 实时应用到 <see cref="GameAudioManager.Instance.SfxVolume"/>，
        /// 并异步保存到配置服务。
        /// </summary>
        private void OnSfxVolumeChanged()
        {
            if (_sfxVolumeSlider == null)
                return;
            try
            {
                float v = _sfxVolumeSlider.Value / 100f;
                GameAudioManager.Instance.SfxVolume = v;
                _ = GameConfigurationService.Instance.SetSFXVolumeAsync(v);
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[SettingsPage] 保存音效音量失败: {ex.Message}");
            }
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

            // SubTask 12.1 标题栏：位置 (0, 0)，宽度铺满，高度 48
            if (_title != null)
            {
                _title.Location = Float2.Zero;
                _title.Size = new Float2(sw, TitleHeight);
            }

            // SubTask 12.2 左侧分类侧边栏：(40, 80)，尺寸 (200, sh - 160)
            if (_sidebarPanel != null)
            {
                _sidebarPanel.Location = new Float2(SidebarX, ContentTop);
                _sidebarPanel.Size = new Float2(SidebarWidth, sh - ContentTop - ContentBottomMargin);
            }

            // 侧边栏列表项宽度固定（= SidebarWidth），高度固定，无需随屏幕变化

            // SubTask 12.3 右侧设置项列表：(260, 80)，尺寸 (sw - 300, sh - 160)
            float settingsWidth = sw - SettingsWidthReserve;
            if (_settingsPanel != null)
            {
                _settingsPanel.Location = new Float2(SettingsX, ContentTop);
                _settingsPanel.Size = new Float2(settingsWidth, sh - ContentTop - ContentBottomMargin);
            }

            // 设置项宽度随面板宽度变化，右侧控件 X 坐标随之调整（保持右边距固定）
            if (_settingItems != null && _rightControls != null)
            {
                for (int i = 0; i < _settingItems.Length; i++)
                {
                    if (_settingItems[i] != null)
                    {
                        _settingItems[i].Size = new Float2(settingsWidth, SettingItemHeight);
                    }

                    var ctrl = _rightControls[i];
                    if (ctrl != null)
                    {
                        // 保持控件原有 Y 坐标（垂直居中位置），仅根据面板宽度重算 X
                        ctrl.Location = new Float2(
                            settingsWidth - ctrl.Width - SettingControlRightMargin,
                            ctrl.Location.Y);
                    }
                }
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
        // mock 交互
        // =======================================================================

        /// <summary>
        /// 切换侧边栏分类选中态（mock）：取消所有项 active，仅激活目标项。
        /// </summary>
        /// <param name="index">目标分类索引（0-3）</param>
        private void SelectCategory(int index)
        {
            if (_sidebarItems == null || index < 0 || index >= _sidebarItems.Length)
                return;

            for (int i = 0; i < _sidebarItems.Length; i++)
            {
                if (_sidebarItems[i] != null)
                    _sidebarItems[i].Active = (i == index);
            }
            _selectedCategory = index;
        }

        /// <summary>
        /// 鼠标按下事件处理：先交由基类路由子控件（CheckBox/Slider 等），
        /// 若子控件未处理，则检测是否命中侧边栏分类项、分辨率项或画质项，执行切换并保存配置。
        /// </summary>
        /// <param name="location">相对于本控件的鼠标坐标</param>
        /// <param name="button">鼠标按键</param>
        /// <returns>是否处理了该事件</returns>
        public override bool OnMouseDown(Float2 location, MouseButton button)
        {
            bool handled = base.OnMouseDown(location, button);
            if (handled)
                return true;

            // 1. 侧边栏分类项命中检测
            if (_sidebarPanel != null && _sidebarItems != null)
            {
                Float2 sidebarLocal = location - _sidebarPanel.Location;
                if (sidebarLocal.X >= 0f && sidebarLocal.X < _sidebarPanel.Width &&
                    sidebarLocal.Y >= 0f && sidebarLocal.Y < _sidebarPanel.Height)
                {
                    int idx = (int)(sidebarLocal.Y / SidebarItemHeight);
                    if (idx >= 0 && idx < _sidebarItems.Length)
                    {
                        SelectCategory(idx);
                        return true;
                    }
                }
            }

            // 2. 分辨率项命中检测（设置项列表第 2 项，index = 1），点击循环切换并保存配置
            if (_settingsPanel != null && _resolutionLabel != null)
            {
                Float2 settingsLocal = location - _settingsPanel.Location;
                float resItemY = 1 * (SettingItemHeight + SettingItemGap);
                if (settingsLocal.X >= 0f && settingsLocal.X < _settingsPanel.Width &&
                    settingsLocal.Y >= resItemY && settingsLocal.Y < resItemY + SettingItemHeight)
                {
                    _resolutionIndex = (_resolutionIndex + 1) % Resolutions.Length;
                    _resolutionLabel.Text = Resolutions[_resolutionIndex];
                    try
                    {
                        _ = GameConfigurationService.Instance.SetResolutionAsync(
                            ResolutionWidths[_resolutionIndex], ResolutionHeights[_resolutionIndex]);
                    }
                    catch (Exception ex)
                    {
                        FlaxEngine.Debug.LogError($"[SettingsPage] 保存分辨率失败: {ex.Message}");
                    }
                    return true;
                }
            }

            // 3. 画面质量项命中检测（设置项列表第 3 项，index = 2），点击循环切换并保存配置
            if (_settingsPanel != null && _qualityLabel != null)
            {
                Float2 settingsLocal = location - _settingsPanel.Location;
                float qualityItemY = 2 * (SettingItemHeight + SettingItemGap);
                if (settingsLocal.X >= 0f && settingsLocal.X < _settingsPanel.Width &&
                    settingsLocal.Y >= qualityItemY && settingsLocal.Y < qualityItemY + SettingItemHeight)
                {
                    _qualityIndex = (_qualityIndex + 1) % QualityLevels.Length;
                    _qualityLabel.Text = QualityLevels[_qualityIndex];
                    try
                    {
                        _ = GameConfigurationService.Instance.SetGraphicsQualityAsync(QualityKeys[_qualityIndex]);
                    }
                    catch (Exception ex)
                    {
                        FlaxEngine.Debug.LogError($"[SettingsPage] 保存画质失败: {ex.Message}");
                    }
                    return true;
                }
            }

            return false;
        }
    }
}
