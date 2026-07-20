using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.StyleSystem;
using System;

namespace HundunWorld.Game.UI.Ink.Pages.Map
{
    /// <summary>
    /// 世界地图页面 — 对应 world-map.html 设计原型。
    /// <para>
    /// 水墨古风世界地图界面，承担玩家俯瞰九州疆域、查阅城池门派、定位秘境与自身位置的核心入口。
    /// 整体布局沿用 HTML 原型的三栏式结构：
    /// <list type="bullet">
    ///   <item>顶部：页面标题"九州疆域图" + 当前所在区域 + 关闭按钮（返回战斗 HUD）</item>
    ///   <item>左栏：5 个区域按钮（江南/中原/塞北/蜀中/岭南），点击切换地图聚焦区域</item>
    ///   <item>中栏：自定义绘制的水墨地图画布（城池金方/门派玉三角/秘境紫圆/玩家红脉冲）</item>
    ///   <item>右栏：选中方点信息面板（名称/类型/等级/距离/传送按钮）</item>
    ///   <item>底部：返回沉浸模式按钮（→ CombatHud） + 跳转司南按钮（→ NavCompass）</item>
    /// </list>
    /// 通过 <see cref="NavigationRequested"/> 事件向路由器暴露导航请求，
    /// dom-id 为 <see cref="InkPageDomIds.NavWorldMap"/>。
    /// 当前实现全部使用 mock 数据；后续接入地图系统时，通过刷新方法替换内容即可。
    /// </para>
    /// </summary>
    public class WorldMapPage : ContainerControl, IInkPage
    {
        // ===================================================================
        // 布局常量
        // =======================================================================

        /// <summary>屏幕边距（像素）</summary>
        private const float ScreenEdge = 16f;

        /// <summary>顶部标题栏高度（像素）</summary>
        private const float HeaderHeight = 60f;

        /// <summary>底部导航栏高度（像素）</summary>
        private const float BottomNavHeight = 40f;

        /// <summary>区域间距（像素）</summary>
        private const float RegionGap = 12f;

        /// <summary>左侧区域列表面板宽度（像素）</summary>
        private const float LeftRegionWidth = 140f;

        /// <summary>右侧信息面板宽度（像素）</summary>
        private const float RightInfoWidth = 260f;

        /// <summary>区域按钮高度（像素）</summary>
        private const float RegionBtnHeight = 44f;

        /// <summary>区域按钮间距（像素）</summary>
        private const float RegionBtnGap = 8f;

        // ===================================================================
        // Mock 数据
        // =======================================================================

        /// <summary>标记点类型枚举</summary>
        private enum MarkerKind
        {
            /// <summary>城池 — 鎏金方块</summary>
            City,

            /// <summary>门派 — 翡翠三角</summary>
            Sect,

            /// <summary>秘境 — 紫色圆点</summary>
            SecretRealm,

            /// <summary>玩家位置 — 朱红脉冲圆</summary>
            Player
        }

        /// <summary>标记点数据结构</summary>
        private struct MarkerData
        {
            /// <summary>名称</summary>
            public string Name;

            /// <summary>类型</summary>
            public MarkerKind Kind;

            /// <summary>归一化 X 坐标（0~1，相对地图画布）</summary>
            public float X;

            /// <summary>归一化 Y 坐标（0~1，相对地图画布）</summary>
            public float Y;

            /// <summary>推荐等级</summary>
            public int Level;

            /// <summary>所属区域索引（0=江南，1=中原，2=塞北，3=蜀中，4=岭南）</summary>
            public int RegionIndex;
        }

        /// <summary>5 个区域名称</summary>
        private static readonly string[] MockRegions =
        {
            "江南", "中原", "塞北", "蜀中", "岭南"
        };

        /// <summary>当前所在区域名称</summary>
        private const string CurrentRegionName = "中原 · 洛阳近郊";

        /// <summary>9 个 mock 标记点（3 城 + 3 门派 + 2 秘境 + 1 玩家）</summary>
        private static readonly MarkerData[] MockMarkers =
        {
            new MarkerData { Name = "清河城",   Kind = MarkerKind.City,        X = 0.20f, Y = 0.30f, Level = 10, RegionIndex = 0 },
            new MarkerData { Name = "洛阳城",   Kind = MarkerKind.City,        X = 0.45f, Y = 0.40f, Level = 25, RegionIndex = 1 },
            new MarkerData { Name = "长安城",   Kind = MarkerKind.City,        X = 0.68f, Y = 0.32f, Level = 30, RegionIndex = 1 },
            new MarkerData { Name = "华山派",   Kind = MarkerKind.Sect,        X = 0.28f, Y = 0.55f, Level = 20, RegionIndex = 1 },
            new MarkerData { Name = "武当派",   Kind = MarkerKind.Sect,        X = 0.52f, Y = 0.62f, Level = 22, RegionIndex = 1 },
            new MarkerData { Name = "少林寺",   Kind = MarkerKind.Sect,        X = 0.72f, Y = 0.50f, Level = 28, RegionIndex = 1 },
            new MarkerData { Name = "古墓秘境", Kind = MarkerKind.SecretRealm, X = 0.18f, Y = 0.65f, Level = 35, RegionIndex = 0 },
            new MarkerData { Name = "蜀山剑阁", Kind = MarkerKind.SecretRealm, X = 0.30f, Y = 0.78f, Level = 40, RegionIndex = 3 },
            new MarkerData { Name = "我的位置", Kind = MarkerKind.Player,      X = 0.47f, Y = 0.43f, Level = 0,  RegionIndex = 1 },
        };

        // ===================================================================
        // 子控件引用
        // =======================================================================

        /// <summary>顶部标题栏面板</summary>
        private InkPanel _headerPanel;

        /// <summary>页面标题"九州疆域图"</summary>
        private Label _titleLabel;

        /// <summary>当前区域文字标签</summary>
        private Label _currentRegionLabel;

        /// <summary>关闭按钮（返回战斗 HUD）</summary>
        private InkButton _closeButton;

        /// <summary>左侧区域列表面板</summary>
        private InkPanel _regionPanel;

        /// <summary>5 个区域按钮</summary>
        private InkButton[] _regionButtons;

        /// <summary>当前激活的区域索引</summary>
        private int _activeRegionIndex = 1; // 默认中原

        /// <summary>中部地图画布（自定义绘制）</summary>
        private MapCanvas _mapCanvas;

        /// <summary>右侧信息面板</summary>
        private InkPanel _infoPanel;

        /// <summary>信息面板：名称标签</summary>
        private Label _infoNameLabel;

        /// <summary>信息面板：类型徽章标签</summary>
        private Label _infoTypeBadge;

        /// <summary>信息面板：等级数值标签</summary>
        private Label _infoLevelLabel;

        /// <summary>信息面板：距离数值标签</summary>
        private Label _infoDistanceLabel;

        /// <summary>信息面板：描述文字</summary>
        private Label _infoDescLabel;

        /// <summary>信息面板：传送按钮</summary>
        private InkButton _teleportButton;

        /// <summary>底部导航栏面板</summary>
        private InkPanel _bottomNavPanel;

        /// <summary>"返回沉浸模式"按钮</summary>
        private InkButton _returnHudButton;

        /// <summary>"跳转司南"按钮</summary>
        private InkButton _gotoCompassButton;

        // ===================================================================
        // 公共 API
        // =======================================================================

        /// <summary>
        /// 导航请求事件。由关闭按钮与底部导航按钮触发，
        /// 参数为 <see cref="InkPageDomIds"/> 中定义的 dom-id 字符串。
        /// </summary>
        public event Action<string> NavigationRequested;

        /// <summary>
        /// 粒子动效系统引用（可选，由外部注入）。
        /// 用于在按钮点击位置触发金粉爆发反馈。
        /// </summary>
        public InkParticleSystem ParticleSystem { get; set; }

        // ===================================================================
        // 构造函数
        // =======================================================================

        /// <summary>
        /// 构造函数：初始化全部子控件并填充 mock 数据。
        /// </summary>
        public WorldMapPage()
        {
            try
            {
                AnchorPreset = AnchorPresets.StretchAll;
                Offsets = Margin.Zero;
                BackgroundColor = Color.Transparent;
                ClipChildren = false;
                AutoFocus = false;

                BuildHeader();
                BuildRegionList();
                BuildMapArea();
                BuildInfoPanel();
                BuildBottomNav();

                // 默认选中"洛阳城"展示信息
                UpdateInfoPanel(1);
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[WorldMapPage] 初始化失败: {ex.Message}");
            }
        }

        // ===================================================================
        // Build 方法
        // =======================================================================

        /// <summary>
        /// 构建顶部标题栏：标题 + 当前区域 + 关闭按钮。
        /// </summary>
        private void BuildHeader()
        {
            _headerPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
            };

            // 页面标题"九州疆域图"
            _titleLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(20f, 0f),
                Size = new Float2(220f, HeaderHeight),
                Text = "九州疆域图",
                TextColor = InkWashTheme.TextGold,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 22f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _headerPanel.AddChild(_titleLabel);

            // 当前区域
            _currentRegionLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(260f, 0f),
                Size = new Float2(280f, HeaderHeight),
                Text = "当前所在：" + CurrentRegionName,
                TextColor = InkWashTheme.TextSecondary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 13f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _headerPanel.AddChild(_currentRegionLabel);

            // 关闭按钮（右侧）
            _closeButton = new InkButton
            {
                Variant = InkButtonVariant.Ghost,
                ButtonSize = InkButtonSize.Sm,
                Text = "✕",
                AnchorPreset = AnchorPresets.TopRight,
                Size = new Float2(32f, 32f),
            };
            _closeButton.ButtonClicked += (b) => OnSystemNavButtonClicked(InkPageDomIds.CombatHud, b);
            _headerPanel.AddChild(_closeButton);

            AddChild(_headerPanel);
        }

        /// <summary>
        /// 构建左侧区域列表面板：5 个区域按钮。
        /// </summary>
        private void BuildRegionList()
        {
            _regionPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
            };

            _regionButtons = new InkButton[MockRegions.Length];
            for (int i = 0; i < MockRegions.Length; i++)
            {
                int capturedIndex = i;
                var btn = new InkButton
                {
                    Variant = InkButtonVariant.Ghost,
                    ButtonSize = InkButtonSize.Md,
                    Text = MockRegions[i],
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(12f, 12f + i * (RegionBtnHeight + RegionBtnGap)),
                    Size = new Float2(LeftRegionWidth - 24f, RegionBtnHeight),
                };
                btn.ButtonClicked += (b) => OnRegionButtonClicked(capturedIndex, b);
                _regionPanel.AddChild(btn);
                _regionButtons[i] = btn;
            }

            // 高亮初始区域
            ApplyRegionHighlight();

            AddChild(_regionPanel);
        }

        /// <summary>
        /// 构建中部地图画布：自定义 <see cref="MapCanvas"/> 控件。
        /// </summary>
        private void BuildMapArea()
        {
            _mapCanvas = new MapCanvas
            {
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = new Color(
                    InkWashTheme.BaseSecondary.R,
                    InkWashTheme.BaseSecondary.G,
                    InkWashTheme.BaseSecondary.B,
                    0.95f),
            };
            _mapCanvas.MarkerSelected += OnMarkerSelected;
            AddChild(_mapCanvas);
        }

        /// <summary>
        /// 构建右侧信息面板：名称/类型徽章/等级/距离/描述/传送按钮。
        /// </summary>
        private void BuildInfoPanel()
        {
            _infoPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
            };

            // 标题"地点详情"
            var infoTitle = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(16f, 12f),
                Size = new Float2(RightInfoWidth - 32f, 22f),
                Text = "◆ 地点详情",
                TextColor = InkWashTheme.TextGold,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), 13f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _infoPanel.AddChild(infoTitle);

            // 名称标签
            _infoNameLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(16f, 44f),
                Size = new Float2(RightInfoWidth - 32f, 28f),
                Text = "—",
                TextColor = InkWashTheme.TextDefault,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 18f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _infoPanel.AddChild(_infoNameLabel);

            // 类型徽章
            _infoTypeBadge = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(16f, 78f),
                Size = new Float2(72f, 22f),
                Text = "—",
                TextColor = InkWashTheme.TextBrand,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 12f),
                BackgroundColor = new Color(
                    InkWashTheme.GoldPrimary.R,
                    InkWashTheme.GoldPrimary.G,
                    InkWashTheme.GoldPrimary.B,
                    0.12f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };
            _infoPanel.AddChild(_infoTypeBadge);

            // 等级标签
            _infoLevelLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(16f, 110f),
                Size = new Float2(RightInfoWidth - 32f, 22f),
                Text = "推荐等级：—",
                TextColor = InkWashTheme.TextSecondary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 13f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _infoPanel.AddChild(_infoLevelLabel);

            // 距离标签
            _infoDistanceLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(16f, 136f),
                Size = new Float2(RightInfoWidth - 32f, 22f),
                Text = "距离：—",
                TextColor = InkWashTheme.TextSecondary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 13f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _infoPanel.AddChild(_infoDistanceLabel);

            // 描述标签（多行）
            _infoDescLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(16f, 168f),
                Size = new Float2(RightInfoWidth - 32f, 160f),
                Text = "—",
                TextColor = InkWashTheme.TextSecondary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 12f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Near,
            };
            _infoPanel.AddChild(_infoDescLabel);

            // 传送按钮（底部）
            _teleportButton = new InkButton
            {
                Variant = InkButtonVariant.Default,
                ButtonSize = InkButtonSize.Md,
                Text = "传送至此",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(16f, 340f),
                Size = new Float2(RightInfoWidth - 32f, 36f),
            };
            _teleportButton.ButtonClicked += (b) => EmitGoldAtButton(b);
            _infoPanel.AddChild(_teleportButton);

            AddChild(_infoPanel);
        }

        /// <summary>
        /// 构建底部导航栏：返回沉浸模式 + 跳转司南。
        /// </summary>
        private void BuildBottomNav()
        {
            _bottomNavPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
            };

            // 返回沉浸模式按钮（左）
            _returnHudButton = new InkButton
            {
                Variant = InkButtonVariant.Default,
                ButtonSize = InkButtonSize.Md,
                Text = "返回沉浸模式",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, 4f),
                Size = new Float2(160f, 32f),
            };
            _returnHudButton.ButtonClicked += (b) => OnSystemNavButtonClicked(InkPageDomIds.CombatHud, b);
            _bottomNavPanel.AddChild(_returnHudButton);

            // 跳转司南按钮（右）
            _gotoCompassButton = new InkButton
            {
                Variant = InkButtonVariant.Ghost,
                ButtonSize = InkButtonSize.Md,
                Text = "跳转司南",
                AnchorPreset = AnchorPresets.TopRight,
                Location = new Float2(-12f - 140f, 4f),
                Size = new Float2(140f, 32f),
            };
            _gotoCompassButton.ButtonClicked += (b) => OnSystemNavButtonClicked(InkPageDomIds.NavCompass, b);
            _bottomNavPanel.AddChild(_gotoCompassButton);

            AddChild(_bottomNavPanel);
        }

        // ===================================================================
        // 事件处理
        // =======================================================================

        /// <summary>
        /// 区域按钮点击处理：切换激活区域、高亮按钮、发射金粉粒子。
        /// </summary>
        private void OnRegionButtonClicked(int regionIndex, Button sourceButton)
        {
            try
            {
                EmitGoldAtButton(sourceButton);
                _activeRegionIndex = regionIndex;
                ApplyRegionHighlight();
                // 选中该区域第一个标记点
                for (int i = 0; i < MockMarkers.Length; i++)
                {
                    if (MockMarkers[i].RegionIndex == regionIndex)
                    {
                        UpdateInfoPanel(i);
                        _mapCanvas?.SetSelectedIndex(i);
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[WorldMapPage] 区域切换失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 系统导航按钮点击处理：发射金粉粒子 + 触发导航请求。
        /// </summary>
        private void OnSystemNavButtonClicked(string domId, Button sourceButton)
        {
            try
            {
                EmitGoldAtButton(sourceButton);
                NavigationRequested?.Invoke(domId);
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError(
                    $"[WorldMapPage] NavigationRequested({domId}) 触发失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 地图标记点选中处理：更新右侧信息面板。
        /// </summary>
        private void OnMarkerSelected(int markerIndex)
        {
            try
            {
                UpdateInfoPanel(markerIndex);
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[WorldMapPage] 标记点选中失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 根据当前激活的区域索引更新所有区域按钮的视觉状态。
        /// </summary>
        private void ApplyRegionHighlight()
        {
            if (_regionButtons == null)
                return;
            for (int i = 0; i < _regionButtons.Length; i++)
            {
                if (_regionButtons[i] == null)
                    continue;
                _regionButtons[i].TextColor = (i == _activeRegionIndex)
                    ? InkWashTheme.TextGold
                    : InkWashTheme.TextSecondary;
            }
        }

        /// <summary>
        /// 更新右侧信息面板，展示指定标记点的详情。
        /// </summary>
        /// <param name="markerIndex">标记点索引</param>
        private void UpdateInfoPanel(int markerIndex)
        {
            try
            {
                if (markerIndex < 0 || markerIndex >= MockMarkers.Length)
                    return;

                var marker = MockMarkers[markerIndex];

                if (_infoNameLabel != null)
                    _infoNameLabel.Text = marker.Name;

                if (_infoTypeBadge != null)
                {
                    _infoTypeBadge.Text = marker.Kind switch
                    {
                        MarkerKind.City => "城池",
                        MarkerKind.Sect => "门派",
                        MarkerKind.SecretRealm => "秘境",
                        MarkerKind.Player => "自身",
                        _ => "—"
                    };
                }

                if (_infoLevelLabel != null)
                {
                    _infoLevelLabel.Text = marker.Kind == MarkerKind.Player
                        ? "推荐等级：—"
                        : "推荐等级：" + marker.Level;
                }

                if (_infoDistanceLabel != null)
                {
                    // 简化 mock：与"我的位置"（索引 8）的欧氏距离 * 1000 当作米
                    var player = MockMarkers[8];
                    float dx = marker.X - player.X;
                    float dy = marker.Y - player.Y;
                    float distance = Mathf.Sqrt(dx * dx + dy * dy) * 1000f;
                    _infoDistanceLabel.Text = marker.Kind == MarkerKind.Player
                        ? "距离：—"
                        : "距离：" + distance.ToString("F0") + " 米";
                }

                if (_infoDescLabel != null)
                {
                    _infoDescLabel.Text = marker.Kind switch
                    {
                        MarkerKind.City => "繁华城池，商贸兴旺，乃江湖人士往来歇脚之所。城内设驿站，可通往九州各处。",
                        MarkerKind.Sect => "江湖名门正派，门下弟子众多，武功传承有序。拜入此门可习得上乘武学。",
                        MarkerKind.SecretRealm => "隐秘之地，藏有上古机缘与凶险。需组队前往，方有一线生机。",
                        MarkerKind.Player => "少侠当前所在之处。江湖路远，前路漫漫。",
                        _ => "—"
                    };
                }

                if (_teleportButton != null)
                {
                    // 玩家自身位置不可传送
                    _teleportButton.Enabled = marker.Kind != MarkerKind.Player;
                    _teleportButton.Visible = true;
                }
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[WorldMapPage] UpdateInfoPanel 失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 在按钮中心位置触发金粉爆发粒子反馈。
        /// </summary>
        private void EmitGoldAtButton(Button button)
        {
            try
            {
                if (ParticleSystem == null || button == null)
                    return;

                var buttonCenter = new Float2(button.Width * 0.5f, button.Height * 0.5f);
                var screenPos = button.PointToScreen(buttonCenter);
                var localPos = ParticleSystem.PointFromScreen(screenPos);
                ParticleSystem.EmitGoldBurst(localPos, count: 14, isLarge: false);
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogWarning($"[WorldMapPage] EmitGoldAtButton 失败: {ex.Message}");
            }
        }

        // ===================================================================
        // IInkPage 实现
        // =======================================================================

        /// <inheritdoc />
        public void RefreshLayout()
        {
            try
            {
                float w = Width;
                float h = Height;
                float panelX = ScreenEdge;
                float panelW = w - ScreenEdge * 2f;

                // 1. 顶部标题栏：顶部全宽
                if (_headerPanel != null)
                {
                    _headerPanel.Location = new Float2(panelX, ScreenEdge);
                    _headerPanel.Size = new Float2(panelW, HeaderHeight);

                    if (_closeButton != null)
                    {
                        _closeButton.Location = new Float2(panelW - 32f - 8f, (HeaderHeight - 32f) * 0.5f);
                    }
                }

                // 2. 底部导航栏：底部全宽
                float bottomNavY = h - ScreenEdge - BottomNavHeight;
                if (_bottomNavPanel != null)
                {
                    _bottomNavPanel.Location = new Float2(panelX, bottomNavY);
                    _bottomNavPanel.Size = new Float2(panelW, BottomNavHeight);

                    if (_gotoCompassButton != null)
                    {
                        _gotoCompassButton.Location = new Float2(panelW - 140f - 12f, 4f);
                    }
                }

                // 3. 内容区：顶部标题栏下方 → 底部导航栏上方
                float contentTop = ScreenEdge + HeaderHeight + RegionGap;
                float contentBottom = bottomNavY - RegionGap;
                float contentH = contentBottom - contentTop;
                if (contentH < 100f)
                    contentH = 100f;

                // 4. 左侧区域列表面板
                if (_regionPanel != null)
                {
                    _regionPanel.Location = new Float2(panelX, contentTop);
                    _regionPanel.Size = new Float2(LeftRegionWidth, contentH);
                }

                // 5. 右侧信息面板
                if (_infoPanel != null)
                {
                    _infoPanel.Location = new Float2(panelX + panelW - RightInfoWidth, contentTop);
                    _infoPanel.Size = new Float2(RightInfoWidth, contentH);

                    // 传送按钮：距信息面板底部 16px
                    if (_teleportButton != null)
                    {
                        _teleportButton.Location = new Float2(16f, contentH - 36f - 16f);
                        _teleportButton.Size = new Float2(RightInfoWidth - 32f, 36f);
                    }

                    // 描述标签：在传送按钮上方
                    if (_infoDescLabel != null)
                    {
                        _infoDescLabel.Size = new Float2(RightInfoWidth - 32f, contentH - 168f - 36f - 32f);
                    }
                }

                // 6. 中部地图画布：在左侧面板与右侧面板之间
                if (_mapCanvas != null)
                {
                    float mapX = panelX + LeftRegionWidth + RegionGap;
                    float mapW = panelW - LeftRegionWidth - RightInfoWidth - RegionGap * 2f;
                    _mapCanvas.Location = new Float2(mapX, contentTop);
                    _mapCanvas.Size = new Float2(mapW, contentH);
                }
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[WorldMapPage] RefreshLayout 失败: {ex.Message}");
            }
        }

        /// <inheritdoc />
        public override void OnParentResized()
        {
            base.OnParentResized();
            RefreshLayout();
        }

        // ===================================================================
        // MapCanvas 内嵌类 — 自定义绘制地图与标记点
        // =======================================================================

        /// <summary>
        /// 水墨地图画布控件。
        /// <para>
        /// 在 <see cref="Draw"/> 中绘制：
        /// <list type="bullet">
        ///   <item>纸色背景与金色边框</item>
        ///   <item>东南西北方位罗盘</item>
        ///   <item>4 类标记点：城池金方块、门派玉三角、秘境紫圆、玩家红脉冲</item>
        ///   <item>选中标记点的高亮圈与名称标签</item>
        /// </list>
        /// 在 <see cref="Update"/> 中驱动玩家点脉冲动画。
        /// 在 <see cref="OnMouseDown"/> 中做命中测试，触发 <see cref="MarkerSelected"/> 事件。
        /// </para>
        /// </summary>
        private class MapCanvas : ContainerControl
        {
            /// <summary>标记点选中事件，参数为标记点索引</summary>
            public event Action<int> MarkerSelected;

            /// <summary>当前选中的标记点索引</summary>
            private int _selectedIndex = 1;

            /// <summary>玩家脉冲动画累计时间（秒）</summary>
            private float _pulseTime = 0f;

            /// <summary>城池方块半边长（像素）</summary>
            private const float CityHalfSize = 7f;

            /// <summary>门派三角外接圆半径（像素）</summary>
            private const float SectRadius = 8f;

            /// <summary>秘境圆点半径（像素）</summary>
            private const float SecretRadius = 6f;

            /// <summary>玩家点半径（像素）</summary>
            private const float PlayerRadius = 6f;

            /// <summary>标记点击命中半径（像素）</summary>
            private const float HitRadius = 14f;

            /// <summary>地图边框厚度（像素）</summary>
            private const float BorderThickness = 2f;

            /// <summary>地图内边距（像素）</summary>
            private const float MapPadding = 20f;

            /// <summary>
            /// 构造函数：透明背景、不裁剪子控件、不自动聚焦。
            /// </summary>
            public MapCanvas()
            {
                BackgroundColor = Color.Transparent;
                ClipChildren = false;
                AutoFocus = false;
            }

            /// <summary>
            /// 设置当前选中的标记点索引。
            /// </summary>
            public void SetSelectedIndex(int index)
            {
                if (index < 0 || index >= MockMarkers.Length)
                    return;
                _selectedIndex = index;
            }

            /// <inheritdoc />
            public override void Update(float deltaTime)
            {
                base.Update(deltaTime);
                _pulseTime += deltaTime;
            }

            /// <inheritdoc />
            public override void Draw()
            {
                if (!Visible || Width <= 0f || Height <= 0f)
                    return;

                // 1. 地图边框（金色描边矩形）
                var borderRect = new Rectangle(0f, 0f, Width, Height);
                Render2D.DrawRectangle(borderRect, InkWashTheme.BorderGold, BorderThickness);

                // 2. 内边距后的地图区域
                float mapX = MapPadding;
                float mapY = MapPadding;
                float mapW = Width - MapPadding * 2f;
                float mapH = Height - MapPadding * 2f;
                if (mapW <= 0f || mapH <= 0f)
                    return;

                // 3. 绘制方位罗盘（左上角）
                DrawCompass(new Float2(mapX + 24f, mapY + 24f), 18f);

                // 4. 绘制网格（淡灰色，5×5 网格）
                DrawMapGrid(mapX, mapY, mapW, mapH);

                // 5. 绘制所有标记点
                for (int i = 0; i < MockMarkers.Length; i++)
                {
                    var m = MockMarkers[i];
                    var pos = new Float2(mapX + m.X * mapW, mapY + m.Y * mapH);
                    bool isSelected = (i == _selectedIndex);
                    DrawMarker(pos, m.Kind, isSelected, i);
                }
            }

            /// <summary>
            /// 绘制地图网格（5×5 淡灰色网格线）。
            /// </summary>
            private void DrawMapGrid(float x, float y, float w, float h)
            {
                Color gridColor = new Color(
                    InkWashTheme.BorderNeutralL3.R,
                    InkWashTheme.BorderNeutralL3.G,
                    InkWashTheme.BorderNeutralL3.B,
                    0.5f);

                // 竖线
                for (int i = 1; i < 5; i++)
                {
                    float lineX = x + w * (i / 5f);
                    Render2D.DrawLine(
                        new Float2(lineX, y),
                        new Float2(lineX, y + h),
                        gridColor, 1f);
                }

                // 横线
                for (int i = 1; i < 5; i++)
                {
                    float lineY = y + h * (i / 5f);
                    Render2D.DrawLine(
                        new Float2(x, lineY),
                        new Float2(x + w, lineY),
                        gridColor, 1f);
                }
            }

            /// <summary>
            /// 绘制单个小方位罗盘（北东南西）。
            /// </summary>
            private static void DrawCompass(Float2 center, float radius)
            {
                if (radius <= 0f)
                    return;

                Color c = InkWashTheme.PaperFaded;
                // 圆环
                int segs = 24;
                for (int i = 0; i < segs; i++)
                {
                    float a1 = (i / (float)segs) * Mathf.TwoPi;
                    float a2 = ((i + 1) / (float)segs) * Mathf.TwoPi;
                    var p1 = center + new Float2(Mathf.Cos(a1) * radius, Mathf.Sin(a1) * radius);
                    var p2 = center + new Float2(Mathf.Cos(a2) * radius, Mathf.Sin(a2) * radius);
                    Render2D.DrawLine(p1, p2, c, 1f);
                }

                // 北东南西四字
                var font = InkWashTheme.GetFont(InkWashTheme.FontRole.Display);
                if (font != null)
                {
                    var fontRef = new FontReference(font, 10f);
                    float off = radius + 6f;
                    DrawTextAt(fontRef, center + new Float2(0f, -off), "北", InkWashTheme.GoldBright);
                    DrawTextAt(fontRef, center + new Float2(off, 0f), "东", c);
                    DrawTextAt(fontRef, center + new Float2(0f, off), "南", c);
                    DrawTextAt(fontRef, center + new Float2(-off, 0f), "西", c);
                }
            }

            /// <summary>
            /// 在指定中心位置绘制一个文字标签。
            /// </summary>
            private static void DrawTextAt(FontReference fontRef, Float2 center, string text, Color color)
            {
                if (fontRef == null)
                    return;
                var font = fontRef.GetFont();
                if (font == null)
                    return;
                float size = 14f;
                var rect = new Rectangle(center.X - size * 0.5f, center.Y - size * 0.5f, size, size);
                Render2D.DrawText(font, text, rect, color,
                    TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
            }

            /// <summary>
            /// 按标记点类型绘制对应图形与名称标签。
            /// </summary>
            private void DrawMarker(Float2 pos, MarkerKind kind, bool isSelected, int index)
            {
                // 选中高亮圈（在标记点下方绘制）
                if (isSelected)
                {
                    Color selColor = new Color(
                        InkWashTheme.GoldBright.R,
                        InkWashTheme.GoldBright.G,
                        InkWashTheme.GoldBright.B,
                        0.45f);
                    InkRenderHelper.FillCircle(pos, HitRadius + 4f, selColor);
                }

                switch (kind)
                {
                    case MarkerKind.City:
                        DrawCityMarker(pos);
                        break;
                    case MarkerKind.Sect:
                        DrawSectMarker(pos);
                        break;
                    case MarkerKind.SecretRealm:
                        DrawSecretMarker(pos);
                        break;
                    case MarkerKind.Player:
                        DrawPlayerMarker(pos);
                        break;
                }

                // 名称标签（玩家位置不绘制名称，避免遮挡）
                if (kind != MarkerKind.Player)
                {
                    DrawPlaceLabel(pos, MockMarkers[index].Name, kind, isSelected);
                }
            }

            /// <summary>
            /// 绘制城池标记：鎏金方块。
            /// </summary>
            private static void DrawCityMarker(Float2 pos)
            {
                Color fill = InkWashTheme.GoldPrimary;
                Color edge = InkWashTheme.GoldBright;
                var rect = new Rectangle(
                    pos.X - CityHalfSize,
                    pos.Y - CityHalfSize,
                    CityHalfSize * 2f,
                    CityHalfSize * 2f);
                Render2D.FillRectangle(rect, fill);
                Render2D.DrawRectangle(rect, edge, 1.5f);
            }

            /// <summary>
            /// 绘制门派标记：翡翠三角（描边）。
            /// </summary>
            private static void DrawSectMarker(Float2 pos)
            {
                Color edge = InkWashTheme.JadeBright;
                // 等边三角形顶点（向上）
                float r = SectRadius;
                var top = pos + new Float2(0f, -r);
                var bl = pos + new Float2(-r * 0.866f, r * 0.5f);
                var br = pos + new Float2(r * 0.866f, r * 0.5f);
                Render2D.DrawLine(top, bl, edge, 1.5f);
                Render2D.DrawLine(bl, br, edge, 1.5f);
                Render2D.DrawLine(br, top, edge, 1.5f);
            }

            /// <summary>
            /// 绘制秘境标记：紫色实心圆。
            /// </summary>
            private static void DrawSecretMarker(Float2 pos)
            {
                Color fill = new Color(0.6f, 0.4f, 0.8f, 0.9f);
                InkRenderHelper.FillCircle(pos, SecretRadius, fill);
                // 外圈描边
                Color edge = new Color(0.8f, 0.6f, 1.0f, 1.0f);
                int segs = 24;
                for (int i = 0; i < segs; i++)
                {
                    float a1 = (i / (float)segs) * Mathf.TwoPi;
                    float a2 = ((i + 1) / (float)segs) * Mathf.TwoPi;
                    var p1 = pos + new Float2(Mathf.Cos(a1) * SecretRadius, Mathf.Sin(a1) * SecretRadius);
                    var p2 = pos + new Float2(Mathf.Cos(a2) * SecretRadius, Mathf.Sin(a2) * SecretRadius);
                    Render2D.DrawLine(p1, p2, edge, 1f);
                }
            }

            /// <summary>
            /// 绘制玩家位置标记：朱红脉冲圆（带外发光）。
            /// </summary>
            private void DrawPlayerMarker(Float2 pos)
            {
                // 脉冲半径随时间在 4~10 像素之间正弦波动
                float pulse = 4f + 6f * (0.5f + 0.5f * Mathf.Sin(_pulseTime * 3f));
                Color glowColor = new Color(
                    InkWashTheme.VermilionBright.R,
                    InkWashTheme.VermilionBright.G,
                    InkWashTheme.VermilionBright.B,
                    0.35f);
                InkRenderHelper.FillCircle(pos, pulse + 4f, glowColor);
                InkRenderHelper.FillCircle(pos, PlayerRadius, InkWashTheme.VermilionBright);
            }

            /// <summary>
            /// 绘制标记点下方的名称标签。
            /// </summary>
            private static void DrawPlaceLabel(Float2 pos, string name, MarkerKind kind, bool isSelected)
            {
                var font = InkWashTheme.GetFont(InkWashTheme.FontRole.Body);
                if (font == null)
                    return;
                var fontRef = new FontReference(font, isSelected ? 12f : 11f);
                var actualFont = fontRef.GetFont();
                if (actualFont == null)
                    return;
                Color c = isSelected ? InkWashTheme.TextGold : InkWashTheme.TextSecondary;
                float labelY = pos.Y + CityHalfSize + 6f;
                var rect = new Rectangle(pos.X - 50f, labelY, 100f, 16f);
                Render2D.DrawText(actualFont, name, rect, c,
                    TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
            }

            /// <inheritdoc />
            public override bool OnMouseDown(Float2 location, MouseButton button)
            {
                if (button != MouseButton.Left)
                    return base.OnMouseDown(location, button);

                // 命中测试：找到距离最近的标记点
                float mapX = MapPadding;
                float mapY = MapPadding;
                float mapW = Width - MapPadding * 2f;
                float mapH = Height - MapPadding * 2f;
                if (mapW <= 0f || mapH <= 0f)
                    return base.OnMouseDown(location, button);

                int bestIdx = -1;
                float bestDist = HitRadius;
                for (int i = 0; i < MockMarkers.Length; i++)
                {
                    var m = MockMarkers[i];
                    var pos = new Float2(mapX + m.X * mapW, mapY + m.Y * mapH);
                    float dist = Float2.Distance(location, pos);
                    if (dist <= bestDist)
                    {
                        bestDist = dist;
                        bestIdx = i;
                    }
                }

                if (bestIdx >= 0)
                {
                    _selectedIndex = bestIdx;
                    try
                    {
                        MarkerSelected?.Invoke(bestIdx);
                    }
                    catch (Exception ex)
                    {
                        FlaxEngine.Debug.LogWarning($"[WorldMapPage.MapCanvas] MarkerSelected 事件订阅者抛出异常: {ex.Message}");
                    }
                    return true;
                }

                return base.OnMouseDown(location, button);
            }
        }
    }
}
