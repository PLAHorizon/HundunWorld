using System;
using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.Ink;
using HundunWorld.Game.UI.StyleSystem;

namespace HundunWorld.Game.UI.Ink.Pages.PhotoMode
{
    /// <summary>
    /// 拍照模式页面。
    /// 落地设计稿 <c>hundun-yy-ui/pages/photo-mode.html</c>：
    /// 顶部栏（退出拍照 / 拍照模式 · PHOTO MODE / 音频设置入口）、
    /// 取景框（四角金角 + 三分线 + 十字准星 + 取景框中心文字 + 底部信息条 + 顶部 HUD）、
    /// 左侧滤镜面板（原图 / 水墨 / 复古 / 明快 / 暗调 + 滚轮切换提示）、
    /// 右侧相机参数面板（FOV / 曝光 / 焦距 + 时间 / 天候 / 风向）、
    /// 底部控制栏（角度 / 快门 + 空格键提示 / 相册）与快门闪光层。
    /// 颜色与字体统一取自 <see cref="InkWashTheme"/> Token 与 Ink 组件库，不引入外部依赖。
    /// </summary>
    public class PhotoModePage : Panel, IInkPage, IInkNavigationSource
    {
        #region 设计稿常量

        /// <summary>顶部栏高度 60px</summary>
        private const float TopBarHeight = 60f;

        /// <summary>顶部栏左右内边距 32px</summary>
        private const float TopBarPadding = 32f;

        /// <summary>取景框宽度 800px</summary>
        private const float ViewfinderWidth = 800f;

        /// <summary>取景框高度 500px</summary>
        private const float ViewfinderHeight = 500f;

        /// <summary>取景框四角 L 形金角长度 28px</summary>
        private const float ViewfinderCornerLength = 28f;

        /// <summary>取景框四角 L 形金角线宽 3px</summary>
        private const float ViewfinderCornerThickness = 3f;

        /// <summary>左侧滤镜面板宽度 200px</summary>
        private const float FilterPanelWidth = 200f;

        /// <summary>左侧滤镜面板左边距 20px</summary>
        private const float FilterPanelLeft = 20f;

        /// <summary>面板内边距 16px</summary>
        private const float PanelPadding = 16f;

        /// <summary>滤镜项高度（内边距 8 + 色块 22）</summary>
        private const float FilterItemHeight = 38f;

        /// <summary>滤镜项间距 4px</summary>
        private const float FilterItemGap = 4f;

        /// <summary>滤镜色块尺寸 22px</summary>
        private const float FilterSwatchSize = 22f;

        /// <summary>右侧相机参数面板宽度 180px</summary>
        private const float CameraPanelWidth = 180f;

        /// <summary>右侧相机参数面板右边距 20px</summary>
        private const float CameraPanelRight = 20f;

        /// <summary>底部控制栏宽度（min-width 600px）</summary>
        private const float BottomBarWidth = 600f;

        /// <summary>底部控制栏高度（内边距 16 + 快门 72 + 提示 20）</summary>
        private const float BottomBarHeight = 124f;

        /// <summary>底部控制栏底边距 30px</summary>
        private const float BottomBarBottom = 30f;

        /// <summary>快门按钮尺寸 72px</summary>
        private const float ShutterSize = 72f;

        /// <summary>参数面板标题行高度</summary>
        private const float PanelHeaderHeight = 24f;

        /// <summary>参数项标签行高度</summary>
        private const float ParamLabelRowHeight = 20f;

        /// <summary>参数项滑块行高度</summary>
        private const float ParamSliderRowHeight = 16f;

        /// <summary>参数项滑块高度 4px</summary>
        private const float ParamSliderHeight = 4f;

        /// <summary>参数项间距 12px</summary>
        private const float ParamItemGap = 12f;

        /// <summary>静态信息行高度</summary>
        private const float StaticRowHeight = 20f;

        /// <summary>FOV 最小值 20°</summary>
        private const float FovMin = 20f;

        /// <summary>FOV 最大值 90°</summary>
        private const float FovMax = 90f;

        /// <summary>FOV 初值 50°</summary>
        private const float FovInitial = 50f;

        /// <summary>曝光补偿最小值 -2.0</summary>
        private const float ExposureMin = -2f;

        /// <summary>曝光补偿最大值 +2.0</summary>
        private const float ExposureMax = 2f;

        /// <summary>曝光补偿初值 +0.5</summary>
        private const float ExposureInitial = 0.5f;

        /// <summary>焦距最小值 14mm</summary>
        private const float FocalMin = 14f;

        /// <summary>焦距最大值 200mm</summary>
        private const float FocalMax = 200f;

        /// <summary>焦距初值 35mm</summary>
        private const float FocalInitial = 35f;

        /// <summary>相册照片数量 12</summary>
        private const int AlbumPhotoCount = 12;

        /// <summary>快门闪光持续 120ms（设计稿 flash-active）</summary>
        private const float FlashDuration = 0.12f;

        #endregion

        #region 设计稿数据

        /// <summary>滤镜名称（设计稿 .filter-list 五项）</summary>
        private static readonly string[] FilterNames = { "原图", "水墨", "复古", "明快", "暗调" };

        /// <summary>滤镜色块起始色（全部取自 Token）</summary>
        private static readonly Color[] FilterSwatchFrom =
        {
            InkWashTheme.QualityRare,
            InkWashTheme.BaseSecondary,
            InkWashTheme.GoldDeep,
            InkWashTheme.JadeBright,
            InkWashTheme.BaseDefault
        };

        /// <summary>滤镜色块终止色（全部取自 Token）</summary>
        private static readonly Color[] FilterSwatchTo =
        {
            Color.Lerp(InkWashTheme.QualityRare, InkWashTheme.PaperBright, 0.35f),
            InkWashTheme.TextTertiary,
            InkWashTheme.PaperAged,
            InkWashTheme.PaperBright,
            InkWashTheme.BaseTertiary
        };

        #endregion

        #region 字段

        private Float2 _screenSize;

        // 顶部栏
        private Panel _topBar;
        private InkButton _exitButton;
        private PhotoIcon _exitIcon;
        private Label _titleLabel;
        private Label _subtitleLabel;
        private InkButton _settingsButton;
        private PhotoIcon _settingsIcon;

        // 取景框
        private PhotoViewfinderFrame _viewfinderFrame;
        private PhotoHudTag _locationTag;
        private PhotoHudTag _autoFocusTag;
        private PhotoIcon _vfCenterIcon;
        private Label _vfCenterLabel;
        private InkPanel _vfInfoBar;

        // 左侧滤镜面板
        private InkPanel _filterPanel;
        private PhotoFilterItem[] _filterItems;
        private int _selectedFilter;

        // 右侧相机参数面板
        private InkPanel _cameraPanel;
        private PhotoParamSlider _fovSlider;
        private PhotoParamSlider _exposureSlider;
        private PhotoParamSlider _focalSlider;
        private Label _fovValue;
        private Label _exposureValue;
        private Label _focalValue;

        // 底部控制栏
        private InkPanel _bottomBar;
        private PhotoSideButton _angleButton;
        private PhotoShutterButton _shutterButton;
        private PhotoSideButton _albumButton;
        private PhotoIcon _shutterHintIcon;
        private Label _shutterHintLabel;

        // 快门闪光层
        private Panel _flashLayer;
        private float _flashTimer;

        // 快门截图：先隐藏全部取景 UI，等两帧再抓屏，再多等一帧恢复（避免把 UI 拍进去）
        private int _capturePhase;
        private int _captureWaitFrames;
        private string _capturePath;
        private bool[] _captureChildVisibility;

        // 取景期间接管主相机 FOV：临时停掉会每帧写 FOV 的相机脚本，退出时原样恢复
        private FlaxEngine.Camera _photoCamera;
        private ThirdPersonCamera _thirdPersonCamera;
        private DynamicCameraAdjuster _cameraAdjuster;
        private bool _thirdPersonWasEnabled;
        private bool _adjusterWasEnabled;
        private float _savedFov;
        private bool _cameraAcquired;
        private float _cameraRetryTimer;

        // 角度预设：在进入时的相机朝向基础上加俯仰角（0° / 俯 12° / 仰 12°），退出时还原
        private Vector3 _savedEuler;
        private int _anglePreset;

        // 曝光/滤镜走 PostFxVolume：进拍照且需要时创建，退出销毁（只在拍照期间影响画面）
        private PostFxVolume _photoVolume;
        private Actor _photoVolumeActor;

        // 自测（标记文件 photo-selftest.on）：运行时页面控件不在场景树里，编辑器工具点不到滑块和快门
        private float _selfTestElapsed;
        private bool _selfTestFovApplied;
        private bool _selfTestShutterFired;
        private bool _selfTestFov2Applied;
        private bool _selfTestSecondShot;
        private bool _selfTestExited;
        private bool _selfTestAngleApplied;
        private bool _selfTestFxApplied;
        private bool _selfTestThirdShot;

        #endregion

        #region 事件

        /// <summary>退出拍照（设计稿 back-hud）</summary>
        public event Action ExitRequested;

        /// <summary>快门触发</summary>
        public event Action ShutterClicked;

        /// <summary>角度切换</summary>
        public event Action AngleClicked;

        /// <summary>相册入口</summary>
        public event Action AlbumClicked;

        /// <summary>
        /// 请求隐藏/恢复常驻 UI（true=进入取景器，隐藏滚轮技能栏与五行气息指示）。
        /// 由 MainUIManager 订阅后统一控制，页面不直接持有外壳控件。
        /// </summary>
        public event Action<bool> OverlayVisibilityRequested;

        /// <summary>
        /// 统一导航请求（设计稿 link-settings-audio 等出口）。
        /// 由 <c>MainUIManager.HookNavigationRequested</c> 反射挂钩后经 InkPageRouter 跳转。
        /// </summary>
        public event Action<string> NavigationRequested;

        #endregion

        /// <summary>
        /// 构造函数：构建拍照模式全部子控件。
        /// </summary>
        public PhotoModePage()
        {
            _screenSize = FlaxEngine.Screen.Size;
            if (_screenSize.X <= 0f || _screenSize.Y <= 0f)
            {
                _screenSize = new Float2(1920f, 1080f);
            }

            AnchorPreset = AnchorPresets.StretchAll;
            // 拍照模式是取景器：页面不铺不透明底，直接透出 3D 场景（外壳也用 ImmersiveTransparent 不铺墨底）。
            BackgroundColor = Color.Transparent;
            ClipChildren = false;
            Location = Float2.Zero;
            Size = _screenSize;

            try
            {
                // 设计稿的墨晕背景是为"静态舞台"准备的；接真实场景后不再画，避免糊在画面上。
                BuildTopBar();
                BuildViewfinder();
                BuildFilterPanel();
                BuildCameraPanel();
                BuildBottomBar();
                BuildFlashLayer();
                ApplyLayout();
                SelectFilter(0);
                RefreshAllParamValues();
                AcquireCameraControl();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[PhotoModePage] 初始化失败: {ex.Message}");
            }
        }

        #region 背景层

        /// <summary>
        /// 背景层：左上/右下/居中水墨晕染 + 暗角晕影（设计稿 .scene-background）。
        /// </summary>
        private void BuildBackground()
        {
            var splashTL = new InkSplash
            {
                Variant = InkSplashVariant.Normal,
                Size = new Float2(420f, 420f),
                Location = new Float2(-60f, -80f),
                Parent = this
            };

            var splashBR = new InkSplash
            {
                Variant = InkSplashVariant.Elevated,
                Size = new Float2(360f, 360f),
                Parent = this
            };

            var splashCenter = new InkSplash
            {
                Variant = InkSplashVariant.Normal,
                Size = new Float2(600f, 600f),
                Parent = this
            };

            var vignette = new InkVignette
            {
                EdgeOpacity = 0.5f,
                Parent = this
            };
            _backgroundVignette = vignette;
            _backgroundSplashBR = splashBR;
            _backgroundSplashCenter = splashCenter;
            _splashTL = splashTL;
        }

        private InkSplash _splashTL;
        private InkSplash _backgroundSplashBR;
        private InkSplash _backgroundSplashCenter;
        private InkVignette _backgroundVignette;

        #endregion

        #region 顶部栏

        /// <summary>
        /// 顶部栏：左「退出拍照」（back-hud）/ 中「拍照模式 + PHOTO MODE」/ 右音频设置入口。
        /// </summary>
        private void BuildTopBar()
        {
            _topBar = new Panel
            {
                Height = TopBarHeight,
                Location = Float2.Zero,
                BackgroundColor = new Color(
                    InkWashTheme.BaseDefault.R,
                    InkWashTheme.BaseDefault.G,
                    InkWashTheme.BaseDefault.B,
                    0.8f),
                Parent = this
            };

            _exitButton = new InkButton
            {
                Text = "退出拍照",
                Variant = InkButtonVariant.Ghost,
                Width = 116f,
                Height = 36f,
                Location = new Float2(TopBarPadding, 12f),
                Parent = _topBar
            };
            // 设计稿 back-hud 出口
            _exitButton.Clicked += () =>
            {
                // 退出前先恢复常驻 UI 与相机 FOV/脚本，否则它们会一直停在取景状态
                OverlayVisibilityRequested?.Invoke(false);
                ReleaseCameraControl();
                ExitRequested?.Invoke();
            };

            _exitIcon = new PhotoIcon(PhotoIconKind.ArrowLeft, 12f)
            {
                IconColor = InkWashTheme.TextSecondary,
                Location = new Float2(TopBarPadding + 14f, 24f),
                Parent = _topBar
            };

            _titleLabel = new Label
            {
                Text = "拍照模式",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 18f),
                TextColor = InkWashTheme.TextBrand,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                Size = new Float2(240f, 24f),
                Parent = _topBar
            };

            _subtitleLabel = new Label
            {
                Text = "PHOTO MODE",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 9f),
                TextColor = InkWashTheme.PaperDark,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                Size = new Float2(240f, 14f),
                Parent = _topBar
            };

            _settingsButton = new InkButton
            {
                Width = 36f,
                Height = 36f,
                Text = string.Empty,
                Variant = InkButtonVariant.Ghost,
                Parent = _topBar
            };
            // 设计稿 link-settings-audio 出口
            _settingsButton.Clicked += () => NavigationRequested?.Invoke(InkPageDomIds.NavSettingsAudio);

            _settingsIcon = new PhotoIcon(PhotoIconKind.Settings, 16f)
            {
                IconColor = InkWashTheme.GoldPrimary,
                Parent = _topBar
            };
        }

        #endregion

        #region 取景框

        /// <summary>
        /// 取景框：四角金角 + 三分线 + 十字准星 + 中心文字 + 顶部 HUD + 底部信息条。
        /// </summary>
        private void BuildViewfinder()
        {
            _viewfinderFrame = new PhotoViewfinderFrame
            {
                Width = ViewfinderWidth,
                Height = ViewfinderHeight,
                Parent = this
            };

            // 顶部 HUD：左「燕云州 · 落霞峰」，右「自动对焦」
            _locationTag = new PhotoHudTag("燕云州 · 落霞峰", PhotoIconKind.MapPin, false)
            {
                Width = 132f,
                Location = new Float2(12f, 12f),
                Parent = _viewfinderFrame
            };

            _autoFocusTag = new PhotoHudTag("自动对焦", PhotoIconKind.Sparkles, true)
            {
                Width = 84f,
                Parent = _viewfinderFrame
            };

            // 中心文字「取景框」
            _vfCenterIcon = new PhotoIcon(PhotoIconKind.Camera, 24f)
            {
                IconColor = new Color(
                    InkWashTheme.PaperFaded.R,
                    InkWashTheme.PaperFaded.G,
                    InkWashTheme.PaperFaded.B,
                    0.4f),
                Location = new Float2(ViewfinderWidth * 0.5f - 12f, ViewfinderHeight * 0.5f + 7f),
                Parent = _viewfinderFrame
            };

            _vfCenterLabel = new Label
            {
                Text = "取景框",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 14f),
                TextColor = new Color(
                    InkWashTheme.PaperFaded.R,
                    InkWashTheme.PaperFaded.G,
                    InkWashTheme.PaperFaded.B,
                    0.5f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                Size = new Float2(120f, 18f),
                Location = new Float2(ViewfinderWidth * 0.5f - 60f, ViewfinderHeight * 0.5f + 35f),
                Parent = _viewfinderFrame
            };

            BuildViewfinderInfoBar();
        }

        /// <summary>
        /// 取景框底部信息条：f/2.8 · 1/125s · ISO 400（居中，1px 金线分隔）。
        /// </summary>
        private void BuildViewfinderInfoBar()
        {
            _vfInfoBar = new InkPanel
            {
                Height = 28f,
                Radius = InkWashTheme.RadiusMd,
                BackgroundColor = new Color(
                    InkWashTheme.BaseDefault.R,
                    InkWashTheme.BaseDefault.G,
                    InkWashTheme.BaseDefault.B,
                    0.6f),
                Parent = _viewfinderFrame
            };

            string[] values = { "f/2.8", "1/125s", "ISO 400" };
            PhotoIconKind[] icons = { PhotoIconKind.Aperture, PhotoIconKind.Timer, PhotoIconKind.Iso };
            float[] widths = { 34f, 46f, 52f };

            float x = PanelPadding;
            for (int i = 0; i < values.Length; i++)
            {
                if (i > 0)
                {
                    var divider = new InkDivider
                    {
                        Width = 1f,
                        Height = 10f,
                        Location = new Float2(x, 9f),
                        Parent = _vfInfoBar
                    };
                    x += 1f + PanelPadding;
                }

                var icon = new PhotoIcon(icons[i], 12f)
                {
                    IconColor = InkWashTheme.GoldPrimary,
                    Location = new Float2(x, 8f),
                    Parent = _vfInfoBar
                };

                var value = new Label
                {
                    Text = values[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 11f),
                    TextColor = InkWashTheme.PaperAged,
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                    Size = new Float2(widths[i], 28f),
                    Location = new Float2(x + 16f, 0f),
                    Parent = _vfInfoBar
                };

                x += 16f + widths[i] + PanelPadding;
            }

            _vfInfoBar.Width = x;
            _vfInfoBar.Location = new Float2(
                (ViewfinderWidth - x) * 0.5f,
                ViewfinderHeight - 12f - _vfInfoBar.Height);
        }

        #endregion

        #region 左侧滤镜面板

        /// <summary>
        /// 左侧滤镜面板：标题 + 5 个滤镜项 + 滚轮提示。
        /// </summary>
        private void BuildFilterPanel()
        {
            _filterPanel = new InkPanel
            {
                Width = FilterPanelWidth,
                BackgroundColor = new Color(
                    InkWashTheme.BaseSecondary.R,
                    InkWashTheme.BaseSecondary.G,
                    InkWashTheme.BaseSecondary.B,
                    0.92f),
                Parent = this
            };

            var headerIcon = new PhotoIcon(PhotoIconKind.Palette, 16f)
            {
                IconColor = InkWashTheme.GoldPrimary,
                Location = new Float2(PanelPadding, PanelPadding + 4f),
                Parent = _filterPanel
            };

            var headerLabel = new Label
            {
                Text = "滤镜",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 16f),
                TextColor = InkWashTheme.TextBrand,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                Size = new Float2(120f, PanelHeaderHeight),
                Location = new Float2(PanelPadding + 16f + 8f, PanelPadding),
                Parent = _filterPanel
            };

            var headerDivider = new InkDivider
            {
                Width = FilterPanelWidth - PanelPadding * 2f,
                Height = 1f,
                Location = new Float2(PanelPadding, PanelPadding + PanelHeaderHeight + 8f),
                Parent = _filterPanel
            };

            float contentWidth = FilterPanelWidth - PanelPadding * 2f;
            float y = PanelPadding + PanelHeaderHeight + 16f;

            _filterItems = new PhotoFilterItem[FilterNames.Length];
            for (int i = 0; i < FilterNames.Length; i++)
            {
                var item = new PhotoFilterItem(FilterNames[i], FilterSwatchFrom[i], FilterSwatchTo[i])
                {
                    Width = contentWidth,
                    Location = new Float2(PanelPadding, y),
                    Parent = _filterPanel
                };

                int index = i;
                item.Clicked += () => SelectFilter(index);
                item.WheelScrolled += delta => SelectFilter(_selectedFilter + (delta > 0f ? -1 : 1));
                _filterItems[i] = item;

                y += FilterItemHeight + FilterItemGap;
            }

            y -= FilterItemGap;

            var footerDivider = new InkDivider
            {
                Width = contentWidth,
                Height = 1f,
                Location = new Float2(PanelPadding, y + 8f),
                Parent = _filterPanel
            };

            var footerHint = new Label
            {
                Text = "滑动滚轮切换",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 10f),
                TextColor = InkWashTheme.TextTertiary,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                Size = new Float2(contentWidth, 14f),
                Location = new Float2(PanelPadding, y + 16f),
                Parent = _filterPanel
            };

            _filterPanel.Height = y + 16f + 14f + PanelPadding;
        }

        /// <summary>
        /// 切换滤镜选中项（滚轮 / 点击）。
        /// </summary>
        /// <param name="index">目标索引（自动环绕）</param>
        private void SelectFilter(int index)
        {
            if (_filterItems == null || _filterItems.Length == 0)
                return;

            int count = _filterItems.Length;
            int wrapped = ((index % count) + count) % count;
            _selectedFilter = wrapped;

            for (int i = 0; i < count; i++)
            {
                if (_filterItems[i] != null)
                {
                    _filterItems[i].Active = i == wrapped;
                }
            }

            ApplyFilter();
        }

        #endregion

        #region 右侧相机参数面板

        /// <summary>
        /// 右侧相机参数面板：FOV / 曝光 / 焦距 + 时间 / 天候 / 风向。
        /// </summary>
        private void BuildCameraPanel()
        {
            _cameraPanel = new InkPanel
            {
                Width = CameraPanelWidth,
                BackgroundColor = new Color(
                    InkWashTheme.BaseSecondary.R,
                    InkWashTheme.BaseSecondary.G,
                    InkWashTheme.BaseSecondary.B,
                    0.92f),
                Parent = this
            };

            var headerIcon = new PhotoIcon(PhotoIconKind.Sliders, 16f)
            {
                IconColor = InkWashTheme.GoldPrimary,
                Location = new Float2(PanelPadding, PanelPadding + 4f),
                Parent = _cameraPanel
            };

            var headerLabel = new Label
            {
                Text = "相机参数",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 16f),
                TextColor = InkWashTheme.TextBrand,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                Size = new Float2(140f, PanelHeaderHeight),
                Location = new Float2(PanelPadding + 16f + 8f, PanelPadding),
                Parent = _cameraPanel
            };

            var headerDivider = new InkDivider
            {
                Width = CameraPanelWidth - PanelPadding * 2f,
                Height = 1f,
                Location = new Float2(PanelPadding, PanelPadding + PanelHeaderHeight + 8f),
                Parent = _cameraPanel
            };

            float contentWidth = CameraPanelWidth - PanelPadding * 2f;
            float y = PanelPadding + PanelHeaderHeight + 16f;

            _fovValue = BuildParamItem(ref y, contentWidth, "FOV", out _fovSlider, "20°", "90°", FovInitial, FovMin, FovMax);
            _fovSlider.Dragged += _ => { RefreshFovLabel(); ApplyFovFromSlider(); };

            _exposureValue = BuildParamItem(ref y, contentWidth, "曝光", out _exposureSlider, "-2.0", "+2.0", ExposureInitial, ExposureMin, ExposureMax);
            _exposureSlider.Dragged += _ => { RefreshExposureLabel(); ApplyExposure(); };

            _focalValue = BuildParamItem(ref y, contentWidth, "焦距", out _focalSlider, "14mm", "200mm", FocalInitial, FocalMin, FocalMax);
            _focalSlider.Dragged += _ => { RefreshFocalLabel(); ApplyFovFromFocal(); };

            var divider = new InkDivider
            {
                Width = contentWidth,
                Height = 1f,
                Location = new Float2(PanelPadding, y + 16f),
                Parent = _cameraPanel
            };

            y += 16f + 1f + 8f;

            // 世界时间 / 天候 / 风向都没有数据源（时辰同步未接入，天候风向无服务端字段），不摆样张文案
            BuildStaticRow(ref y, "时间", "—", PhotoIconKind.Clock);
            BuildStaticRow(ref y, "天候", "—", PhotoIconKind.Sun);
            BuildStaticRow(ref y, "风向", "—", PhotoIconKind.Wind);

            _cameraPanel.Height = y + PanelPadding;
        }

        /// <summary>
        /// 构建一个相机参数项（标签 + 数值 + 滑块 + 上下限），返回数值 Label。
        /// </summary>
        private Label BuildParamItem(
            ref float y,
            float contentWidth,
            string label,
            out PhotoParamSlider slider,
            string minText,
            string maxText,
            float initialValue,
            float min,
            float max)
        {
            var nameLabel = new Label
            {
                Text = label,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                TextColor = InkWashTheme.PaperAged,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                Size = new Float2(80f, ParamLabelRowHeight),
                Location = new Float2(PanelPadding, y),
                Parent = _cameraPanel
            };

            var valueLabel = new Label
            {
                Text = string.Empty,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 13f),
                TextColor = InkWashTheme.TextBrand,
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
                Size = new Float2(80f, ParamLabelRowHeight),
                Location = new Float2(PanelPadding + contentWidth - 80f, y),
                Parent = _cameraPanel
            };

            float sliderRowY = y + ParamLabelRowHeight + 4f;
            float minMaxWidth = 26f;
            float sliderWidth = Mathf.Max(40f, contentWidth - minMaxWidth * 2f - 8f);

            slider = new PhotoParamSlider
            {
                Width = sliderWidth,
                Height = ParamSliderHeight,
                Location = new Float2(PanelPadding, sliderRowY + (ParamSliderRowHeight - ParamSliderHeight) * 0.5f),
                Value = Mathf.Clamp((initialValue - min) / (max - min), 0f, 1f),
                Parent = _cameraPanel
            };

            var minLabel = new Label
            {
                Text = minText,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 9f),
                TextColor = InkWashTheme.TextTertiary,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                Size = new Float2(minMaxWidth, ParamSliderRowHeight),
                Location = new Float2(PanelPadding + sliderWidth + 4f, sliderRowY),
                Parent = _cameraPanel
            };

            var maxLabel = new Label
            {
                Text = maxText,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 9f),
                TextColor = InkWashTheme.TextTertiary,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                Size = new Float2(minMaxWidth, ParamSliderRowHeight),
                Location = new Float2(PanelPadding + sliderWidth + 4f + minMaxWidth, sliderRowY),
                Parent = _cameraPanel
            };

            y = sliderRowY + ParamSliderRowHeight + ParamItemGap;
            return valueLabel;
        }

        /// <summary>
        /// 构建一行静态环境信息（时间 / 天候 / 风向）。
        /// </summary>
        private void BuildStaticRow(ref float y, string label, string value, PhotoIconKind icon)
        {
            float contentWidth = CameraPanelWidth - PanelPadding * 2f;

            var rowIcon = new PhotoIcon(icon, 12f)
            {
                IconColor = InkWashTheme.Info,
                Location = new Float2(PanelPadding, y + 4f),
                Parent = _cameraPanel
            };

            var nameLabel = new Label
            {
                Text = label,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.PaperAged,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                Size = new Float2(60f, StaticRowHeight),
                Location = new Float2(PanelPadding + 20f, y),
                Parent = _cameraPanel
            };

            var valueLabel = new Label
            {
                Text = value,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 13f),
                TextColor = InkWashTheme.PaperFaded,
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
                Size = new Float2(90f, StaticRowHeight),
                Location = new Float2(PanelPadding + contentWidth - 90f, y),
                Parent = _cameraPanel
            };

            y += StaticRowHeight + 8f;
        }

        /// <summary>
        /// 刷新三个参数数值文案（设计稿初值：50° / +0.5 / 35mm）。
        /// </summary>
        private void RefreshAllParamValues()
        {
            RefreshFovLabel();
            RefreshExposureLabel();
            RefreshFocalLabel();
        }

        /// <summary>刷新 FOV 数值文案。</summary>
        private void RefreshFovLabel()
        {
            if (_fovValue == null || _fovSlider == null)
                return;

            int fov = Mathf.RoundToInt(FovMin + _fovSlider.Value * (FovMax - FovMin));
            _fovValue.Text = fov + "°";
        }

        /// <summary>刷新曝光数值文案（带正号）。</summary>
        private void RefreshExposureLabel()
        {
            if (_exposureValue == null || _exposureSlider == null)
                return;

            float exposure = ExposureMin + _exposureSlider.Value * (ExposureMax - ExposureMin);
            _exposureValue.Text = (exposure >= 0f ? "+" : string.Empty) + exposure.ToString("F1");
        }

        /// <summary>刷新焦距数值文案。</summary>
        private void RefreshFocalLabel()
        {
            if (_focalValue == null || _focalSlider == null)
                return;

            int focal = Mathf.RoundToInt(FocalMin + _focalSlider.Value * (FocalMax - FocalMin));
            _focalValue.Text = focal + "mm";
        }

        /// <summary>
        /// 接管主相机 FOV：停掉会每帧写 FOV 的相机脚本（第三人称相机 / 动态 FOV），
        /// 记下原 FOV，退出或页面销毁时原样恢复。
        /// </summary>
        private void AcquireCameraControl()
        {
            var cam = FlaxEngine.Camera.MainCamera;
            string via = "MainCamera";

            if (cam == null)
            {
                // PIE 里主视口相机不一定注册成 MainCamera。本项目相机与 ThirdPersonCamera /
                // DynamicCameraAdjuster 挂在同一个 Actor 上（见 DynamicCameraAdjuster.OnStart 的 Actor.As<Camera>()），
                // 所以退到这两处按 Actor 取相机；都拿不到就由 Update 定时重试。
                var tpc = FlaxEngine.Level.FindScript<ThirdPersonCamera>();
                if (tpc != null)
                {
                    cam = tpc.Actor.As<FlaxEngine.Camera>() ?? tpc.Actor.GetChild<FlaxEngine.Camera>();
                    via = "ThirdPersonCamera.Actor";
                }
            }

            if (cam == null)
            {
                var adjuster = FlaxEngine.Level.FindScript<DynamicCameraAdjuster>();
                if (adjuster != null)
                {
                    cam = adjuster.Actor.As<FlaxEngine.Camera>() ?? adjuster.Actor.GetChild<FlaxEngine.Camera>();
                    via = "DynamicCameraAdjuster.Actor";
                }
            }

            if (cam == null) return;

            _photoCamera = cam;
            _savedFov = cam.FieldOfView;
            _savedEuler = cam.EulerAngles;
            _anglePreset = 0;

            _thirdPersonCamera = cam.GetScript<ThirdPersonCamera>()
                ?? FlaxEngine.Level.FindScript<ThirdPersonCamera>();
            if (_thirdPersonCamera != null)
            {
                _thirdPersonWasEnabled = _thirdPersonCamera.Enabled;
                _thirdPersonCamera.Enabled = false;
            }

            _cameraAdjuster = cam.GetScript<DynamicCameraAdjuster>()
                ?? FlaxEngine.Level.FindScript<DynamicCameraAdjuster>();
            if (_cameraAdjuster != null)
            {
                _adjusterWasEnabled = _cameraAdjuster.Enabled;
                _cameraAdjuster.Enabled = false;
            }

            _cameraAcquired = true;
            if (HasSelfTestMarker("photo-selftest.on"))
                AppendSelfTestReceipt($"camera via={via} savedFov={_savedFov:F1} instance={GetHashCode():X8} tpc={(_thirdPersonCamera == null ? "null" : "off")} adjuster={(_cameraAdjuster == null ? "null" : "off")}");
            ApplyFovFromSlider();
            // 曝光滑块初值 0.5 ⇒ 0 EV（中性），进页面就对齐，避免"标签有值画面没变"
            ApplyExposure();
        }

        private void ReleaseCameraControl()
        {
            if (!_cameraAcquired) return;

            float restored = _photoCamera != null ? _savedFov : -1f;
            if (_photoCamera != null)
            {
                _photoCamera.FieldOfView = _savedFov;
                if (_anglePreset != 0)
                    _photoCamera.EulerAngles = _savedEuler;
            }
            if (_thirdPersonCamera != null)
                _thirdPersonCamera.Enabled = _thirdPersonWasEnabled;
            if (_cameraAdjuster != null)
                _cameraAdjuster.Enabled = _adjusterWasEnabled;

            if (HasSelfTestMarker("photo-selftest.on"))
                AppendSelfTestReceipt($"release instance={GetHashCode():X8} restoredFov={restored:F1} adjuster={(_cameraAdjuster == null ? "null" : _adjusterWasEnabled.ToString())}");

            if (_photoCamera != null)
                _photoCamera.FieldOfView = _savedFov;
            if (_thirdPersonCamera != null)
                _thirdPersonCamera.Enabled = _thirdPersonWasEnabled;
            if (_cameraAdjuster != null)
                _cameraAdjuster.Enabled = _adjusterWasEnabled;

            _photoCamera = null;
            _thirdPersonCamera = null;
            _cameraAdjuster = null;
            _cameraAcquired = false;
            DestroyPhotoVolume();
        }

        /// <summary>角度按钮：在进入时朝向的基础上循环 平视 → 俯视 12° → 仰视 12°。</summary>
        private void OnAngleClicked()
        {
            if (!_cameraAcquired || _photoCamera == null)
                return;

            _anglePreset = (_anglePreset + 1) % 3;
            ApplyAnglePreset();
            AngleClicked?.Invoke();
        }

        private void ApplyAnglePreset()
        {
            if (!_cameraAcquired || _photoCamera == null)
                return;

            float pitch = _anglePreset switch
            {
                1 => 12f,
                2 => -12f,
                _ => 0f
            };
            _photoCamera.EulerAngles = _savedEuler + new Vector3(pitch, 0f, 0f);
            if (HasSelfTestMarker("photo-selftest.on"))
                AppendSelfTestReceipt($"angle preset={_anglePreset} pitch={pitch:F1} euler={_photoCamera.EulerAngles}");
        }

        /// <summary>
        /// 拍照期间的曝光/调色载体。没有现成体积就临时建一个，退出时销毁——
        /// 只在取景与拍照时影响画面，不污染正常游戏渲染。
        /// </summary>
        private PostFxVolume EnsurePhotoVolume()
        {
            if (_photoVolume != null)
                return _photoVolume;

            try
            {
                // PostFxVolume 本身就是 Actor（和 Camera 一样），直接 new + Spawn
                _photoVolume = new PostFxVolume { Name = "PhotoModePostFx" };
                Level.SpawnActor(_photoVolume);
                _photoVolume.BlendWeight = 1f;
                _photoVolume.BlendRadius = 0f;
                _photoVolumeActor = _photoVolume;
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogWarning($"[PhotoModePage] 创建曝光/调色体积失败: {ex.Message}");
                _photoVolume = null;
            }

            return _photoVolume;
        }

        /// <summary>曝光滑块 → 相机后处理曝光补偿（-2 ~ +2 EV）。</summary>
        private void ApplyExposure()
        {
            if (_exposureSlider == null)
                return;

            var vol = EnsurePhotoVolume();
            if (vol == null)
                return;

            float ev = ExposureMin + _exposureSlider.Value * (ExposureMax - ExposureMin);
            var eye = vol.EyeAdaptation;
            eye.PostExposure = ev;
            eye.OverrideFlags = EyeAdaptationSettingsOverride.All;
            vol.EyeAdaptation = eye;

            if (HasSelfTestMarker("photo-selftest.on"))
                AppendSelfTestReceipt($"exposure ev={ev:F1}");
        }

        /// <summary>滤镜 → 调色（饱和度 + 色偏），选「原图」时恢复中性。</summary>
        private void ApplyFilter()
        {
            var vol = EnsurePhotoVolume();
            if (vol == null)
                return;

            var cg = vol.ColorGrading;
            cg.OverrideFlags = ColorGradingSettingsOverride.All;
            switch (_selectedFilter)
            {
                case 1: // 水墨
                    cg.ColorSaturation = new Float4(0.25f);
                    cg.ColorGain = new Float4(1.02f, 1.0f, 0.96f, 1f);
                    break;
                case 2: // 复古
                    cg.ColorSaturation = new Float4(0.7f);
                    cg.ColorGain = new Float4(1.08f, 1.0f, 0.88f, 1f);
                    break;
                case 3: // 明快
                    cg.ColorSaturation = new Float4(1.18f);
                    cg.ColorGain = new Float4(1.02f, 1.02f, 1.02f, 1f);
                    break;
                case 4: // 暗调
                    cg.ColorSaturation = new Float4(0.85f);
                    cg.ColorGain = new Float4(0.92f, 0.92f, 0.96f, 1f);
                    break;
                default: // 原图
                    cg.ColorSaturation = new Float4(1f);
                    cg.ColorGain = new Float4(1f);
                    break;
            }
            vol.ColorGrading = cg;

            if (HasSelfTestMarker("photo-selftest.on"))
                AppendSelfTestReceipt($"filter index={_selectedFilter}");
        }

        private void DestroyPhotoVolume()
        {
            if (_photoVolumeActor == null)
                return;

            try
            {
                FlaxEngine.Object.Destroy(_photoVolumeActor);
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogWarning($"[PhotoModePage] 销毁曝光/调色体积失败: {ex.Message}");
            }

            _photoVolumeActor = null;
            _photoVolume = null;
        }

        private void ApplyFovFromSlider()
        {
            if (!_cameraAcquired || _photoCamera == null || _fovSlider == null) return;

            float fov = FovMin + _fovSlider.Value * (FovMax - FovMin);
            _photoCamera.FieldOfView = Mathf.Clamp(fov, 10f, 120f);
        }

        /// <summary>
        /// 焦距 → FOV：设计稿范围 14~200mm 对应视野 90°~20°。
        /// 工程里没有机身光学参数可查，这里按设计稿区间做线性映射，不假装物理换算。
        /// </summary>
        private void ApplyFovFromFocal()
        {
            if (_focalSlider == null) return;

            float fov = FovMax - _focalSlider.Value * (FovMax - FovMin);

            if (_fovSlider != null)
            {
                float range = FovMax - FovMin;
                _fovSlider.Value = range <= 0f ? 0f : Mathf.Clamp((fov - FovMin) / range, 0f, 1f);
            }
            RefreshFovLabel();

            if (_cameraAcquired && _photoCamera != null)
                _photoCamera.FieldOfView = Mathf.Clamp(fov, 10f, 120f);
        }

        #endregion

        #region 底部控制栏

        /// <summary>
        /// 底部控制栏：角度 / 快门（含空格键提示）/ 相册（含数量角标）。
        /// </summary>
        private void BuildBottomBar()
        {
            _bottomBar = new InkPanel
            {
                Width = BottomBarWidth,
                Height = BottomBarHeight,
                BackgroundColor = new Color(
                    InkWashTheme.BaseSecondary.R,
                    InkWashTheme.BaseSecondary.G,
                    InkWashTheme.BaseSecondary.B,
                    0.95f),
                Parent = this
            };

            float sideButtonY = (BottomBarHeight - 56f) * 0.5f;

            _angleButton = new PhotoSideButton(PhotoIconKind.Rotate, "角度")
            {
                Location = new Float2(TopBarPadding, sideButtonY),
                Parent = _bottomBar
            };
            _angleButton.Clicked += OnAngleClicked;

            _albumButton = new PhotoSideButton(PhotoIconKind.Images, "相册")
            {
                Location = new Float2(BottomBarWidth - TopBarPadding - 72f, sideButtonY),
                Parent = _bottomBar
            };
            _albumButton.Count = AlbumPhotoCount;
            _albumButton.Clicked += () => AlbumClicked?.Invoke();

            _shutterButton = new PhotoShutterButton
            {
                Location = new Float2((BottomBarWidth - ShutterSize) * 0.5f, PanelPadding),
                Parent = _bottomBar
            };
            _shutterButton.Clicked += OnShutterClicked;

            _shutterHintIcon = new PhotoIcon(PhotoIconKind.SpaceKey, 12f)
            {
                IconColor = InkWashTheme.TextTertiary,
                Parent = _bottomBar
            };

            _shutterHintLabel = new Label
            {
                Text = "按空格键拍照",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.PaperFaded,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                Size = new Float2(80f, 16f),
                Parent = _bottomBar
            };
        }

        #endregion

        #region 闪光层与快门

        /// <summary>
        /// 快门闪光层（设计稿 .flash-layer，120ms 白色闪光）。
        /// </summary>
        private void BuildFlashLayer()
        {
            _flashLayer = new Panel
            {
                Size = _screenSize,
                Location = Float2.Zero,
                BackgroundColor = new Color(
                    InkWashTheme.PaperBright.R,
                    InkWashTheme.PaperBright.G,
                    InkWashTheme.PaperBright.B,
                    0f),
                Visible = false,
                Enabled = false,
                Parent = this
            };
        }

        /// <summary>
        /// 快门被触发：派发事件并播放闪光（点击 / 空格键共用）。
        /// </summary>
        private void OnShutterClicked()
        {
            ShutterClicked?.Invoke();
            TriggerFlash();
            BeginCapture();
        }

        /// <summary>开始一次截图：隐藏取景 UI（含闪光层），由 <see cref="Update"/> 分帧完成抓屏与恢复。</summary>
        private void BeginCapture()
        {
            if (_capturePhase != 0) return;

            _capturePath = BuildCapturePath();
            if (HasSelfTestMarker("photo-selftest.on"))
                AppendSelfTestReceipt($"capture path={_capturePath}");

            int count = Children.Count;
            _captureChildVisibility = new bool[count];
            for (int i = 0; i < count; i++)
            {
                var child = Children[i];
                _captureChildVisibility[i] = child != null && child.Visible;
                if (child != null) child.Visible = false;
            }

            _capturePhase = 1;
            _captureWaitFrames = 2;
        }

        private static string BuildCapturePath()
        {
            string dir = System.IO.Path.Combine(Globals.ProjectFolder, "Screenshots");
            if (!System.IO.Directory.Exists(dir))
                System.IO.Directory.CreateDirectory(dir);
            return System.IO.Path.Combine(dir, $"Photo_{System.DateTime.Now:yyyyMMdd_HHmmss}.png");
        }

        private void RestoreCaptureUi()
        {
            if (_captureChildVisibility != null)
            {
                int count = Math.Min(Children.Count, _captureChildVisibility.Length);
                for (int i = 0; i < count; i++)
                {
                    var child = Children[i];
                    if (child != null) child.Visible = _captureChildVisibility[i];
                }
                _captureChildVisibility = null;
            }
        }

        /// <summary>
        /// 播放一次快门闪光。
        /// </summary>
        private void TriggerFlash()
        {
            if (_flashLayer == null)
                return;

            _flashTimer = FlashDuration;
            _flashLayer.Visible = true;
            _flashLayer.BackgroundColor = new Color(
                InkWashTheme.PaperBright.R,
                InkWashTheme.PaperBright.G,
                InkWashTheme.PaperBright.B,
                1f);
        }

        /// <summary>
        /// 自测：<c>%APPDATA%/HundunWorld/photo-selftest.on</c> 存在时，进页面依次
        /// ① 1.5s 把 FOV 滑块拨到中间 → 3s 拍照；② 5s 拨到最小 → 6.5s 再拍一张。
        /// 两张同机位不同 FOV 的成片可直接比对，证明滑块真的写到了相机上（运行时控件点不到）。
        /// </summary>
        private void RunSelfTest(float deltaTime)
        {
            if (_selfTestExited || !HasSelfTestMarker("photo-selftest.on"))
                return;

            _selfTestElapsed += deltaTime;

            if (!_selfTestFovApplied && _selfTestElapsed >= 1.5f && _cameraAcquired && _photoCamera != null)
            {
                _selfTestFovApplied = true;
                SetSelfTestFov(0.5f, "0.5");
            }

            if (!_selfTestShutterFired && _selfTestElapsed >= 3f)
            {
                _selfTestShutterFired = true;
                FlaxEngine.Debug.Log("[SelfTest] photo 触发快门 #1");
                OnShutterClicked();
            }

            if (_selfTestFovApplied && !_selfTestFov2Applied && _selfTestElapsed >= 5f && _cameraAcquired && _photoCamera != null)
            {
                _selfTestFov2Applied = true;
                SetSelfTestFov(0f, "0");
            }

            if (_selfTestShutterFired && !_selfTestAngleApplied && _selfTestElapsed >= 4.2f && _cameraAcquired && _photoCamera != null)
            {
                _selfTestAngleApplied = true;
                OnAngleClicked();
            }

            if (_selfTestShutterFired && !_selfTestSecondShot && _selfTestElapsed >= 6.5f)
            {
                _selfTestSecondShot = true;
                FlaxEngine.Debug.Log("[SelfTest] photo 触发快门 #2");
                OnShutterClicked();
            }

            if (_selfTestSecondShot && !_selfTestFxApplied && _selfTestElapsed >= 8f && _cameraAcquired && _photoCamera != null)
            {
                _selfTestFxApplied = true;
                // 曝光拉满 + 水墨滤镜，用来验证 PostFx 真的进了渲染（不是只改标签）
                if (_exposureSlider != null) _exposureSlider.Value = 1f;
                RefreshExposureLabel();
                ApplyExposure();
                SelectFilter(1);
            }

            if (_selfTestFxApplied && !_selfTestThirdShot && _selfTestElapsed >= 9.5f)
            {
                _selfTestThirdShot = true;
                FlaxEngine.Debug.Log("[SelfTest] photo 触发快门 #3（曝光/滤镜）");
                OnShutterClicked();
            }

            if (_selfTestThirdShot && !_selfTestExited && _selfTestElapsed >= 11.5f)
            {
                _selfTestExited = true;
                FlaxEngine.Debug.Log("[SelfTest] photo 走退出路径");
                // 与顶部返回钮同一套收尾（常驻 UI + 相机 + 页面导航）
                OverlayVisibilityRequested?.Invoke(false);
                ReleaseCameraControl();
                ExitRequested?.Invoke();
            }
        }

        private void SetSelfTestFov(float sliderValue, string label)
        {
            if (_fovSlider == null)
                return;

            float before = _photoCamera != null ? _photoCamera.FieldOfView : 0f;
            _fovSlider.Value = sliderValue;
            RefreshFovLabel();
            ApplyFovFromSlider();
            float after = _photoCamera != null ? _photoCamera.FieldOfView : 0f;
            FlaxEngine.Debug.Log($"[SelfTest] photo FOV {before:F1} -> {after:F1}（滑块 {label}）");
            AppendSelfTestReceipt($"fov slider={label} before={before:F1} after={after:F1} acquired={_cameraAcquired} cam={(_photoCamera == null ? "null" : "ok")} instance={GetHashCode():X8}");
        }

        /// <summary>自测用的落盘回执（运行日志在这台机器上不落盘，只能用文件取证）。</summary>
        private static void AppendSelfTestReceipt(string line)
        {
            try
            {
                string path = System.IO.Path.Combine(
                    System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
                    "HundunWorld", "photo-selftest.out");
                System.IO.File.AppendAllText(path, line + System.Environment.NewLine);
            }
            catch
            {
                // 自测回执写不进去不影响功能
            }
        }

        private static bool HasSelfTestMarker(string fileName)
        {
            try
            {
                string path = System.IO.Path.Combine(
                    System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
                    "HundunWorld", fileName);
                return System.IO.File.Exists(path);
            }
            catch
            {
                return false;
            }
        }

        /// <inheritdoc />
        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);

            // 相机还没拿到（页面创建时主视口相机可能尚未就绪）：每 0.5 秒重试，拿到就接管 FOV。
            // 另外，切场景/重生会把相机 Actor 重建掉，旧引用会变成 null 而 _cameraAcquired 仍是 true——
            // 这时要作废重拿，否则 FOV 滑块静默无效（2026-10-29 自测回执：acquired=True cam=null）。
            if (_cameraAcquired && _photoCamera == null)
            {
                _cameraAcquired = false;
                _thirdPersonCamera = null;
                _cameraAdjuster = null;
            }

            if (!_cameraAcquired)
            {
                _cameraRetryTimer -= deltaTime;
                if (_cameraRetryTimer <= 0f)
                {
                    _cameraRetryTimer = 0.5f;
                    AcquireCameraControl();
                }
            }

            // 截图分帧：隐藏 UI 两帧后抓屏，再多等一帧恢复，避免截图里带上取景界面
            if (_capturePhase != 0)
            {
                _captureWaitFrames--;
                if (_capturePhase == 1 && _captureWaitFrames <= 0)
                {
                    try
                    {
                        Screenshot.Capture(_capturePath);
                    }
                    catch (Exception ex)
                    {
                        FlaxEngine.Debug.LogError($"[PhotoModePage] 截图失败: {ex.Message}");
                    }

                    _capturePhase = 2;
                    _captureWaitFrames = 1;
                }
                else if (_capturePhase == 2 && _captureWaitFrames <= 0)
                {
                    RestoreCaptureUi();
                    _capturePhase = 0;
                    HundunWorld.Game.UI.UIHelper.ShowToast(
                        "照片已保存到 Screenshots 文件夹", HundunWorld.Game.UI.ToastType.Success);
                }
            }

            // 设计稿：空格键拍照（document keydown Space）
            if (FlaxEngine.Input.GetKeyDown(KeyboardKeys.Spacebar))
            {
                OnShutterClicked();
            }

            RunSelfTest(deltaTime);

            // 闪光衰减（120ms）
            if (_flashLayer != null && _flashLayer.Visible)
            {
                _flashTimer -= deltaTime;
                float alpha = Mathf.Clamp(_flashTimer / FlashDuration, 0f, 1f);
                _flashLayer.BackgroundColor = new Color(
                    InkWashTheme.PaperBright.R,
                    InkWashTheme.PaperBright.G,
                    InkWashTheme.PaperBright.B,
                    alpha);

                if (_flashTimer <= 0f)
                {
                    _flashTimer = 0f;
                    _flashLayer.Visible = false;
                }
            }
        }

        /// <inheritdoc />
        public override bool OnMouseWheel(Float2 location, float delta)
        {
            // 设计稿：滚轮切换滤镜（.filter-hint 滑动滚轮切换）
            if (Mathf.Abs(delta) > 0.0001f)
            {
                SelectFilter(_selectedFilter + (delta > 0f ? -1 : 1));
                return true;
            }

            return base.OnMouseWheel(location, delta);
        }

        #endregion

        #region 布局

        /// <summary>
        /// 按当前屏幕尺寸重排子控件位置。
        /// </summary>
        private void ApplyLayout()
        {
            float sw = _screenSize.X;
            float sh = _screenSize.Y;

            if (_topBar != null)
            {
                _topBar.Location = Float2.Zero;
                _topBar.Size = new Float2(sw, TopBarHeight);
            }

            if (_exitButton != null)
            {
                _exitButton.Location = new Float2(TopBarPadding, 12f);
            }

            if (_exitIcon != null)
            {
                _exitIcon.Location = new Float2(TopBarPadding + 14f, 24f);
            }

            if (_titleLabel != null)
            {
                _titleLabel.Location = new Float2(sw * 0.5f - 120f, 8f);
            }

            if (_subtitleLabel != null)
            {
                _subtitleLabel.Location = new Float2(sw * 0.5f - 120f, 32f);
            }

            if (_settingsButton != null)
            {
                _settingsButton.Location = new Float2(sw - TopBarPadding - 36f, 12f);
            }

            if (_settingsIcon != null)
            {
                _settingsIcon.Location = new Float2(sw - TopBarPadding - 26f, 22f);
            }

            if (_viewfinderFrame != null)
            {
                _viewfinderFrame.Location = new Float2(
                    sw * 0.5f - ViewfinderWidth * 0.5f,
                    sh * 0.5f - ViewfinderHeight * 0.5f);
            }

            if (_autoFocusTag != null)
            {
                _autoFocusTag.Location = new Float2(ViewfinderWidth - 12f - _autoFocusTag.Width, 12f);
            }

            if (_filterPanel != null)
            {
                _filterPanel.Location = new Float2(FilterPanelLeft, sh * 0.5f - _filterPanel.Height * 0.5f);
            }

            if (_cameraPanel != null)
            {
                _cameraPanel.Location = new Float2(
                    sw - CameraPanelRight - CameraPanelWidth,
                    sh * 0.5f - _cameraPanel.Height * 0.5f);
            }

            if (_bottomBar != null)
            {
                _bottomBar.Location = new Float2(
                    sw * 0.5f - BottomBarWidth * 0.5f,
                    sh - BottomBarBottom - BottomBarHeight);
            }

            if (_shutterHintLabel != null)
            {
                float hintWidth = 76f;
                _shutterHintLabel.Size = new Float2(hintWidth, 16f);
                _shutterHintLabel.Location = new Float2(
                    (BottomBarWidth - hintWidth) * 0.5f + 8f,
                    PanelPadding + ShutterSize + 4f);
            }

            if (_shutterHintIcon != null)
            {
                _shutterHintIcon.Location = new Float2(
                    (BottomBarWidth - 88f) * 0.5f,
                    PanelPadding + ShutterSize + 6f);
            }

            if (_splashTL != null)
            {
                _splashTL.Location = new Float2(-60f, -80f);
            }

            if (_backgroundSplashBR != null)
            {
                _backgroundSplashBR.Location = new Float2(sw - 320f, sh - 300f);
            }

            if (_backgroundSplashCenter != null)
            {
                _backgroundSplashCenter.Location = new Float2(sw * 0.5f - 300f, sh * 0.5f - 300f);
            }

            if (_backgroundVignette != null)
            {
                _backgroundVignette.Location = Float2.Zero;
                _backgroundVignette.Size = new Float2(sw, sh);
            }

            if (_flashLayer != null)
            {
                _flashLayer.Location = Float2.Zero;
                _flashLayer.Size = new Float2(sw, sh);
            }
        }

        /// <summary>
        /// 基于当前控件实际尺寸刷新布局（IInkPage 契约）。
        /// </summary>
        /// <inheritdoc />
        public override void OnDestroy()
        {
            // 页面被路由销毁（不只点退出）时也要恢复常驻 UI 与相机
            OverlayVisibilityRequested?.Invoke(false);
            ReleaseCameraControl();
            base.OnDestroy();
        }

        public void RefreshLayout()
        {
            if (Width > 0f && Height > 0f)
            {
                _screenSize = new Float2(Width, Height);
            }
            else
            {
                _screenSize = FlaxEngine.Screen.Size;
            }

            if (_screenSize.X <= 0f || _screenSize.Y <= 0f)
            {
                _screenSize = new Float2(1920f, 1080f);
            }

            ApplyLayout();
        }

        /// <summary>
        /// 构建界面（页面已随构造函数完成构建，保留工厂调用契约）。
        /// </summary>
        public void BuildUI()
        {
        }

        /// <summary>
        /// 刷新绑定数据（拍照模式为纯表现页，无数值绑定）。
        /// </summary>
        public void RefreshBoundData()
        {
        }

        #endregion

        #region 图标

        /// <summary>
        /// 设计稿 Lucide 图标种类（用矢量绘制近似，避免引入图标资源依赖）。
        /// </summary>
        private enum PhotoIconKind
        {
            ArrowLeft,
            Camera,
            Aperture,
            Timer,
            Iso,
            MapPin,
            Sparkles,
            Palette,
            Sliders,
            Clock,
            Sun,
            Wind,
            Rotate,
            Images,
            SpaceKey,
            Check,
            Settings
        }

        /// <summary>
        /// 矢量图标控件。<see cref="Control.Enabled"/> 恒为 false，仅作装饰，不拦截鼠标事件。
        /// </summary>
        private sealed class PhotoIcon : Control
        {
            /// <summary>图标种类</summary>
            private readonly PhotoIconKind _kind;

            /// <summary>图标颜色（默认鎏金主色）</summary>
            public Color IconColor { get; set; } = InkWashTheme.GoldPrimary;

            /// <summary>
            /// 构造函数。
            /// </summary>
            /// <param name="kind">图标种类</param>
            /// <param name="size">图标边长（像素）</param>
            public PhotoIcon(PhotoIconKind kind, float size)
            {
                _kind = kind;
                Size = new Float2(size, size);
                BackgroundColor = Color.Transparent;
                Enabled = false;
                AutoFocus = false;
            }

            /// <inheritdoc />
            public override void Draw()
            {
                if (!Visible || Width <= 0f || Height <= 0f)
                    return;

                float w = Width;
                float h = Height;
                float cx = w * 0.5f;
                float cy = h * 0.5f;
                float s = Mathf.Min(w, h);
                float t = Mathf.Max(1f, s * 0.09f);
                Color c = IconColor;

                switch (_kind)
                {
                    case PhotoIconKind.ArrowLeft:
                        Render2D.DrawLine(new Float2(w * 0.18f, cy), new Float2(w * 0.82f, cy), c, t);
                        Render2D.DrawLine(new Float2(w * 0.18f, cy), new Float2(w * 0.46f, cy - h * 0.24f), c, t);
                        Render2D.DrawLine(new Float2(w * 0.18f, cy), new Float2(w * 0.46f, cy + h * 0.24f), c, t);
                        break;

                    case PhotoIconKind.Camera:
                        InkRenderHelper.DrawRoundedRectangle(
                            new Rectangle(w * 0.08f, h * 0.26f, w * 0.84f, h * 0.56f), s * 0.12f, c, t);
                        Render2D.FillRectangle(new Rectangle(w * 0.34f, h * 0.14f, w * 0.26f, h * 0.12f), c);
                        InkRenderHelper.DrawCircle(new Float2(cx, h * 0.56f), s * 0.17f, c, t);
                        break;

                    case PhotoIconKind.Aperture:
                        InkRenderHelper.DrawCircle(new Float2(cx, cy), s * 0.36f, c, t);
                        Render2D.DrawLine(
                            new Float2(cx - s * 0.36f, cy + s * 0.14f),
                            new Float2(cx + s * 0.26f, cy - s * 0.26f), c, t);
                        Render2D.DrawLine(
                            new Float2(cx - s * 0.14f, cy - s * 0.36f),
                            new Float2(cx + s * 0.36f, cy + s * 0.10f), c, t);
                        Render2D.DrawLine(
                            new Float2(cx - s * 0.26f, cy + s * 0.26f),
                            new Float2(cx + s * 0.06f, cy + s * 0.36f), c, t);
                        break;

                    case PhotoIconKind.Timer:
                        InkRenderHelper.DrawCircle(new Float2(cx, cy + s * 0.04f), s * 0.36f, c, t);
                        Render2D.DrawLine(
                            new Float2(cx, cy + s * 0.04f), new Float2(cx, cy - s * 0.18f), c, t);
                        Render2D.DrawLine(
                            new Float2(cx, cy + s * 0.04f), new Float2(cx + s * 0.16f, cy + s * 0.14f), c, t);
                        Render2D.FillRectangle(new Rectangle(cx - s * 0.08f, h * 0.06f, s * 0.16f, s * 0.10f), c);
                        break;

                    case PhotoIconKind.Iso:
                        InkRenderHelper.DrawCircle(new Float2(cx, cy), s * 0.38f, c, t);
                        InkRenderHelper.FillCircle(new Float2(cx, cy), s * 0.13f, c);
                        break;

                    case PhotoIconKind.MapPin:
                        InkRenderHelper.DrawCircle(new Float2(cx, h * 0.36f), s * 0.28f, c, t);
                        Render2D.DrawLine(
                            new Float2(cx - s * 0.20f, h * 0.50f), new Float2(cx, h * 0.88f), c, t);
                        Render2D.DrawLine(
                            new Float2(cx + s * 0.20f, h * 0.50f), new Float2(cx, h * 0.88f), c, t);
                        InkRenderHelper.FillCircle(new Float2(cx, h * 0.36f), s * 0.09f, c);
                        break;

                    case PhotoIconKind.Sparkles:
                        Render2D.DrawLine(new Float2(cx, h * 0.10f), new Float2(cx, h * 0.64f), c, t);
                        Render2D.DrawLine(new Float2(w * 0.12f, h * 0.37f), new Float2(w * 0.74f, h * 0.37f), c, t);
                        Render2D.DrawLine(new Float2(w * 0.68f, h * 0.60f), new Float2(w * 0.68f, h * 0.94f), c, t);
                        Render2D.DrawLine(new Float2(w * 0.51f, h * 0.77f), new Float2(w * 0.85f, h * 0.77f), c, t);
                        break;

                    case PhotoIconKind.Palette:
                        InkRenderHelper.DrawCircle(new Float2(cx, cy), s * 0.42f, c, t);
                        InkRenderHelper.FillCircle(new Float2(cx - s * 0.16f, cy - s * 0.10f), s * 0.07f, c);
                        InkRenderHelper.FillCircle(new Float2(cx + s * 0.08f, cy - s * 0.22f), s * 0.07f, c);
                        InkRenderHelper.FillCircle(new Float2(cx + s * 0.18f, cy + s * 0.12f), s * 0.07f, c);
                        break;

                    case PhotoIconKind.Sliders:
                        Render2D.DrawLine(new Float2(w * 0.10f, h * 0.34f), new Float2(w * 0.90f, h * 0.34f), c, t);
                        Render2D.DrawLine(new Float2(w * 0.10f, h * 0.68f), new Float2(w * 0.90f, h * 0.68f), c, t);
                        InkRenderHelper.FillCircle(new Float2(w * 0.34f, h * 0.34f), s * 0.11f, c);
                        InkRenderHelper.FillCircle(new Float2(w * 0.66f, h * 0.68f), s * 0.11f, c);
                        break;

                    case PhotoIconKind.Clock:
                        InkRenderHelper.DrawCircle(new Float2(cx, cy), s * 0.38f, c, t);
                        Render2D.DrawLine(new Float2(cx, cy), new Float2(cx, cy - s * 0.22f), c, t);
                        Render2D.DrawLine(new Float2(cx, cy), new Float2(cx + s * 0.18f, cy + s * 0.10f), c, t);
                        break;

                    case PhotoIconKind.Sun:
                        InkRenderHelper.DrawCircle(new Float2(cx, cy), s * 0.23f, c, t);
                        Render2D.DrawLine(new Float2(cx, h * 0.06f), new Float2(cx, h * 0.22f), c, t);
                        Render2D.DrawLine(new Float2(cx, h * 0.78f), new Float2(cx, h * 0.94f), c, t);
                        Render2D.DrawLine(new Float2(w * 0.06f, cy), new Float2(w * 0.22f, cy), c, t);
                        Render2D.DrawLine(new Float2(w * 0.78f, cy), new Float2(w * 0.94f, cy), c, t);
                        break;

                    case PhotoIconKind.Wind:
                        Render2D.DrawLine(new Float2(w * 0.10f, h * 0.32f), new Float2(w * 0.74f, h * 0.32f), c, t);
                        Render2D.DrawLine(new Float2(w * 0.10f, h * 0.52f), new Float2(w * 0.90f, h * 0.52f), c, t);
                        Render2D.DrawLine(new Float2(w * 0.10f, h * 0.72f), new Float2(w * 0.62f, h * 0.72f), c, t);
                        break;

                    case PhotoIconKind.Rotate:
                    {
                        var center = new Float2(cx, cy);
                        float radius = s * 0.34f;
                        const int segments = 12;
                        var prev = center + new Float2(radius, 0f);
                        for (int i = 1; i <= segments; i++)
                        {
                            float angle = (i / (float)segments) * Mathf.Pi * 1.5f;
                            var cur = center + new Float2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius);
                            Render2D.DrawLine(prev, cur, c, t);
                            prev = cur;
                        }

                        Render2D.DrawLine(prev, new Float2(prev.X - s * 0.18f, prev.Y - s * 0.10f), c, t);
                        Render2D.DrawLine(prev, new Float2(prev.X + s * 0.02f, prev.Y - s * 0.20f), c, t);
                        break;
                    }

                    case PhotoIconKind.Images:
                        InkRenderHelper.DrawRoundedRectangle(
                            new Rectangle(w * 0.08f, h * 0.18f, w * 0.62f, h * 0.50f), s * 0.08f, c, t);
                        InkRenderHelper.DrawRoundedRectangle(
                            new Rectangle(w * 0.30f, h * 0.36f, w * 0.60f, h * 0.48f), s * 0.08f, c, t);
                        break;

                    case PhotoIconKind.SpaceKey:
                        InkRenderHelper.DrawRoundedRectangle(
                            new Rectangle(w * 0.06f, h * 0.22f, w * 0.88f, h * 0.56f), s * 0.12f, c, t);
                        Render2D.DrawLine(new Float2(w * 0.26f, h * 0.62f), new Float2(w * 0.74f, h * 0.62f), c, t);
                        break;

                    case PhotoIconKind.Check:
                        Render2D.DrawLine(new Float2(w * 0.16f, h * 0.52f), new Float2(w * 0.40f, h * 0.76f), c, t);
                        Render2D.DrawLine(new Float2(w * 0.40f, h * 0.76f), new Float2(w * 0.84f, h * 0.26f), c, t);
                        break;

                    default: // Settings — 齿轮近似
                        InkRenderHelper.DrawCircle(new Float2(cx, cy), s * 0.24f, c, t);
                        Render2D.DrawLine(new Float2(cx, h * 0.06f), new Float2(cx, h * 0.22f), c, t);
                        Render2D.DrawLine(new Float2(cx, h * 0.78f), new Float2(cx, h * 0.94f), c, t);
                        Render2D.DrawLine(new Float2(w * 0.06f, cy), new Float2(w * 0.22f, cy), c, t);
                        Render2D.DrawLine(new Float2(w * 0.78f, cy), new Float2(w * 0.94f, cy), c, t);
                        break;
                }
            }
        }

        #endregion

        #region 取景框控件

        /// <summary>
        /// 取景框：深墨半透明底 + 2px 金深描边 + 四角 L 形亮金角 + 三分线 + 十字准星。
        /// 对应设计稿 .viewfinder-frame / .vf-corner / .vf-grid / .vf-crosshair。
        /// </summary>
        private sealed class PhotoViewfinderFrame : ContainerControl
        {
            /// <summary>
            /// 构造函数：透明背景（背景由 Draw 手动绘制），不裁剪子控件。
            /// </summary>
            public PhotoViewfinderFrame()
            {
                BackgroundColor = Color.Transparent;
                ClipChildren = false;
                AutoFocus = false;
            }

            /// <inheritdoc />
            public override void Draw()
            {
                if (!Visible || Width <= 0f || Height <= 0f)
                    return;

                var bounds = new Rectangle(0, 0, Width, Height);

                // 1. 取景框底：深墨 45%
                Render2D.FillRectangle(bounds, new Color(
                    InkWashTheme.BaseDefault.R,
                    InkWashTheme.BaseDefault.G,
                    InkWashTheme.BaseDefault.B,
                    0.45f));

                // 2. 三分线（1px 金 10%）
                var gridColor = new Color(
                    InkWashTheme.GoldPrimary.R,
                    InkWashTheme.GoldPrimary.G,
                    InkWashTheme.GoldPrimary.B,
                    0.1f);
                Render2D.FillRectangle(new Rectangle(Width / 3f, 0f, 1f, Height), gridColor);
                Render2D.FillRectangle(new Rectangle(Width * 2f / 3f, 0f, 1f, Height), gridColor);
                Render2D.FillRectangle(new Rectangle(0f, Height / 3f, Width, 1f), gridColor);
                Render2D.FillRectangle(new Rectangle(0f, Height * 2f / 3f, Width, 1f), gridColor);

                // 3. 十字准星（40px 金 40% + 6px 圆环）
                var crossColor = new Color(
                    InkWashTheme.GoldPrimary.R,
                    InkWashTheme.GoldPrimary.G,
                    InkWashTheme.GoldPrimary.B,
                    0.4f);
                var center = new Float2(Width * 0.5f, Height * 0.5f);
                Render2D.FillRectangle(new Rectangle(center.X - 20f, center.Y, 40f, 1f), crossColor);
                Render2D.FillRectangle(new Rectangle(center.X, center.Y - 20f, 1f, 40f), crossColor);
                InkRenderHelper.DrawCircle(center, 3f, InkWashTheme.GoldPrimary, 1f);

                // 4. 子控件（中心文字 / HUD / 信息条）
                base.Draw();

                // 5. 2px 金深描边
                Render2D.DrawRectangle(bounds, InkWashTheme.GoldDeep, 2f);

                // 6. 四角 L 形金角
                DrawCorner(0f, 0f, 1f, 1f);
                DrawCorner(Width, 0f, -1f, 1f);
                DrawCorner(0f, Height, 1f, -1f);
                DrawCorner(Width, Height, -1f, -1f);
            }

            /// <summary>
            /// 绘制一个 L 形金角。
            /// </summary>
            private static void DrawCorner(float x, float y, float dirX, float dirY)
            {
                Render2D.DrawLine(
                    new Float2(x, y),
                    new Float2(x + dirX * ViewfinderCornerLength, y),
                    InkWashTheme.GoldBright,
                    ViewfinderCornerThickness);
                Render2D.DrawLine(
                    new Float2(x, y),
                    new Float2(x, y + dirY * ViewfinderCornerLength),
                    InkWashTheme.GoldBright,
                    ViewfinderCornerThickness);
            }
        }

        /// <summary>
        /// 取景框 HUD 标签：半透明深墨底 + 1px 描边 + 图标 + 文案。
        /// 对应设计稿 .vf-hud-tag / .vf-hud-tag-gold。
        /// </summary>
        private sealed class PhotoHudTag : ContainerControl
        {
            /// <summary>是否金色强调变体</summary>
            private readonly bool _gold;

            /// <summary>
            /// 构造函数。
            /// </summary>
            /// <param name="text">文案</param>
            /// <param name="icon">图标种类</param>
            /// <param name="gold">是否金色强调（.vf-hud-tag-gold）</param>
            public PhotoHudTag(string text, PhotoIconKind icon, bool gold)
            {
                _gold = gold;
                Height = 22f;
                BackgroundColor = Color.Transparent;
                ClipChildren = false;
                AutoFocus = false;
                Enabled = false;

                var tagIcon = new PhotoIcon(icon, 12f)
                {
                    IconColor = gold ? InkWashTheme.GoldPrimary : InkWashTheme.Info,
                    Location = new Float2(8f, 5f),
                    Parent = this
                };

                var label = new Label
                {
                    Text = text,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                    TextColor = gold ? InkWashTheme.TextBrand : InkWashTheme.PaperAged,
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                    Size = new Float2(100f, 22f),
                    Location = new Float2(24f, 0f),
                    Parent = this
                };
            }

            /// <inheritdoc />
            public override void Draw()
            {
                if (!Visible || Width <= 0f || Height <= 0f)
                    return;

                var bounds = new Rectangle(0, 0, Width, Height);
                InkRenderHelper.FillRoundedRectangle(bounds, InkWashTheme.RadiusSm, new Color(
                    InkWashTheme.BaseDefault.R,
                    InkWashTheme.BaseDefault.G,
                    InkWashTheme.BaseDefault.B,
                    0.6f));

                base.Draw();

                InkRenderHelper.DrawRoundedRectangle(
                    bounds,
                    InkWashTheme.RadiusSm,
                    _gold ? InkWashTheme.GoldDeep : InkWashTheme.BorderNeutralL2,
                    1f);
            }
        }

        #endregion

        #region 滤镜项

        /// <summary>
        /// 滤镜项：色块 + 名称 + 选中勾选，支持悬停 / 点击 / 滚轮。
        /// 对应设计稿 .filter-item / .filter-active / .filter-swatch / .filter-name / .filter-check。
        /// </summary>
        private sealed class PhotoFilterItem : ContainerControl
        {
            /// <summary>色块起始色</summary>
            private readonly Color _swatchFrom;

            /// <summary>色块终止色</summary>
            private readonly Color _swatchTo;

            /// <summary>名称标签</summary>
            private readonly Label _nameLabel;

            /// <summary>选中勾选图标</summary>
            private readonly PhotoIcon _check;

            /// <summary>是否选中</summary>
            private bool _active;

            /// <summary>是否悬停</summary>
            private bool _hovered;

            /// <summary>点击事件</summary>
            public event Action Clicked;

            /// <summary>滚轮事件（delta 为正表示向上滚动）</summary>
            public event Action<float> WheelScrolled;

            /// <summary>
            /// 是否选中。选中态：微金底 + 金深边框 + 品牌文字 + 勾选图标。
            /// </summary>
            public bool Active
            {
                get => _active;
                set
                {
                    _active = value;
                    if (_check != null)
                        _check.Visible = value;
                    if (_nameLabel != null)
                        _nameLabel.TextColor = value ? InkWashTheme.TextBrand : InkWashTheme.PaperAged;
                }
            }

            /// <summary>
            /// 构造函数。
            /// </summary>
            /// <param name="name">滤镜名称</param>
            /// <param name="swatchFrom">色块起始色</param>
            /// <param name="swatchTo">色块终止色</param>
            public PhotoFilterItem(string name, Color swatchFrom, Color swatchTo)
            {
                _swatchFrom = swatchFrom;
                _swatchTo = swatchTo;
                Height = FilterItemHeight;
                BackgroundColor = Color.Transparent;
                ClipChildren = false;
                AutoFocus = false;

                _nameLabel = new Label
                {
                    Text = name,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 13f),
                    TextColor = InkWashTheme.PaperAged,
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                    Size = new Float2(90f, FilterItemHeight),
                    Location = new Float2(42f, 0f),
                    Parent = this
                };

                _check = new PhotoIcon(PhotoIconKind.Check, 14f)
                {
                    IconColor = InkWashTheme.TextBrand,
                    Visible = false,
                    Parent = this
                };
            }

            /// <inheritdoc />
            protected override void OnSizeChanged()
            {
                base.OnSizeChanged();

                if (_check != null)
                {
                    _check.Location = new Float2(Mathf.Max(0f, Width - 26f), (Height - 14f) * 0.5f);
                }
            }

            /// <inheritdoc />
            public override void OnMouseEnter(Float2 location)
            {
                _hovered = true;
                if (!_active && _nameLabel != null)
                    _nameLabel.TextColor = InkWashTheme.PaperBright;
                base.OnMouseEnter(location);
            }

            /// <inheritdoc />
            public override void OnMouseLeave()
            {
                _hovered = false;
                if (!_active && _nameLabel != null)
                    _nameLabel.TextColor = InkWashTheme.PaperAged;
                base.OnMouseLeave();
            }

            /// <inheritdoc />
            public override bool OnMouseDown(Float2 location, MouseButton button)
            {
                if (button == MouseButton.Left)
                {
                    Clicked?.Invoke();
                    return true;
                }

                return base.OnMouseDown(location, button);
            }

            /// <inheritdoc />
            public override bool OnMouseWheel(Float2 location, float delta)
            {
                WheelScrolled?.Invoke(delta);
                return true;
            }

            /// <inheritdoc />
            public override void Draw()
            {
                if (!Visible || Width <= 0f || Height <= 0f)
                    return;

                var bounds = new Rectangle(0, 0, Width, Height);

                // 1. 背景（选中 > 悬停 > 默认）
                Color bg = _active
                    ? new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.08f)
                    : _hovered
                        ? new Color(InkWashTheme.PaperBright.R, InkWashTheme.PaperBright.G, InkWashTheme.PaperBright.B, 0.06f)
                        : new Color(InkWashTheme.BaseSecondary.R, InkWashTheme.BaseSecondary.G, InkWashTheme.BaseSecondary.B, 0.6f);
                InkRenderHelper.FillRoundedRectangle(bounds, InkWashTheme.RadiusMd, bg);

                // 2. 色块（双色近似斜向渐变）
                float swatchY = (Height - FilterSwatchSize) * 0.5f;
                var swatchBounds = new Rectangle(12f, swatchY, FilterSwatchSize, FilterSwatchSize);
                InkRenderHelper.FillRoundedRectangle(swatchBounds, InkWashTheme.RadiusSm, _swatchFrom);
                InkRenderHelper.FillRoundedRectangle(
                    new Rectangle(swatchBounds.X + FilterSwatchSize * 0.5f, swatchBounds.Y, FilterSwatchSize * 0.5f, FilterSwatchSize),
                    InkWashTheme.RadiusSm,
                    new Color(_swatchTo.R, _swatchTo.G, _swatchTo.B, 0.75f));
                InkRenderHelper.DrawRoundedRectangle(swatchBounds, InkWashTheme.RadiusSm, InkWashTheme.TextTertiary, 1f);

                // 3. 子控件（名称 + 勾选）
                base.Draw();

                // 4. 边框
                Color border = _active
                    ? InkWashTheme.GoldDeep
                    : _hovered
                        ? InkWashTheme.TextTertiary
                        : Color.Transparent;
                if (border.A > 0f)
                {
                    InkRenderHelper.DrawRoundedRectangle(bounds, InkWashTheme.RadiusMd, border, 1f);
                }
            }
        }

        #endregion

        #region 参数滑块

        /// <summary>
        /// 相机参数滑块：金渐变填充 + 圆形滑块，支持拖拽与滚轮微调。
        /// 对应设计稿 .param-slider（4px 轨道 + 14px 圆形滑块）。
        /// </summary>
        private sealed class PhotoParamSlider : InkBar
        {
            /// <summary>滑块半径 7px（直径 14px）</summary>
            private const float ThumbRadius = 7f;

            /// <summary>滚轮微调步长</summary>
            private const float WheelStep = 0.02f;

            /// <summary>是否正在拖拽</summary>
            private bool _dragging;

            /// <summary>数值变化事件（0~1 归一化值）</summary>
            public event Action<float> Dragged;

            /// <summary>
            /// 构造函数：鎏金填充、透明背景。
            /// </summary>
            public PhotoParamSlider()
            {
                Height = ParamSliderHeight;
                FillVariant = InkBarFillVariant.Gold;
                BackgroundColor = Color.Transparent;
                ClipChildren = false;
                AutoFocus = false;
            }

            /// <inheritdoc />
            public override bool OnMouseDown(Float2 location, MouseButton button)
            {
                if (button != MouseButton.Left)
                    return base.OnMouseDown(location, button);

                _dragging = true;
                ApplyLocation(location.X);
                return true;
            }

            /// <inheritdoc />
            public override void OnMouseMove(Float2 location)
            {
                base.OnMouseMove(location);

                if (_dragging)
                {
                    ApplyLocation(location.X);
                }
            }

            /// <inheritdoc />
            public override bool OnMouseUp(Float2 location, MouseButton button)
            {
                base.OnMouseUp(location, button);

                if (button == MouseButton.Left && _dragging)
                {
                    _dragging = false;
                    ApplyLocation(location.X);
                    return true;
                }

                return false;
            }

            /// <inheritdoc />
            public override bool OnMouseWheel(Float2 location, float delta)
            {
                if (Mathf.Abs(delta) < 0.0001f)
                    return false;

                Value = Mathf.Clamp(Value + (delta > 0f ? WheelStep : -WheelStep), 0f, 1f);
                Dragged?.Invoke(Value);
                return true;
            }

            /// <summary>
            /// 按鼠标横坐标更新数值。
            /// </summary>
            private void ApplyLocation(float x)
            {
                float value = Width > 0f ? Mathf.Clamp(x / Width, 0f, 1f) : 0f;
                Value = value;
                Dragged?.Invoke(value);
            }

            /// <inheritdoc />
            public override void Draw()
            {
                base.Draw();

                if (!Visible || Width <= 0f || Height <= 0f)
                    return;

                float usableWidth = Mathf.Max(1f, Width);
                float thumbX = Mathf.Clamp(Value * usableWidth, ThumbRadius, Mathf.Max(ThumbRadius, usableWidth - ThumbRadius));
                var center = new Float2(thumbX, Height * 0.5f);

                InkRenderHelper.FillCircle(center, ThumbRadius, InkWashTheme.GoldPrimary);
                InkRenderHelper.DrawCircle(center, ThumbRadius, InkWashTheme.BaseDefault, 2f);
            }
        }

        #endregion

        #region 底部栏控件

        /// <summary>
        /// 底部侧按钮：图标 + 文案 + 可选数量角标（角度 / 相册）。
        /// 对应设计稿 .bottom-side-btn / .bottom-btn-label / .album-count。
        /// </summary>
        private sealed class PhotoSideButton : ContainerControl
        {
            /// <summary>图标宽度</summary>
            private const float ButtonWidth = 72f;

            /// <summary>按钮高度</summary>
            private const float ButtonHeight = 56f;

            /// <summary>图标控件</summary>
            private readonly PhotoIcon _icon;

            /// <summary>文案标签</summary>
            private readonly Label _label;

            /// <summary>数量角标</summary>
            private readonly PhotoCountBadge _badge;

            /// <summary>点击事件</summary>
            public event Action Clicked;

            /// <summary>
            /// 数量角标（0 表示不显示）。
            /// </summary>
            public int Count
            {
                get => _badge != null ? _badge.Count : 0;
                set
                {
                    if (_badge == null)
                        return;

                    _badge.Count = value;
                    _badge.Visible = value > 0;
                }
            }

            /// <summary>
            /// 构造函数。
            /// </summary>
            /// <param name="kind">图标种类</param>
            /// <param name="text">按钮文案</param>
            public PhotoSideButton(PhotoIconKind kind, string text)
            {
                Size = new Float2(ButtonWidth, ButtonHeight);
                BackgroundColor = Color.Transparent;
                ClipChildren = false;
                AutoFocus = false;

                _icon = new PhotoIcon(kind, 20f)
                {
                    IconColor = InkWashTheme.PaperAged,
                    Location = new Float2((ButtonWidth - 20f) * 0.5f, 4f),
                    Parent = this
                };

                _label = new Label
                {
                    Text = text,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 11f),
                    TextColor = InkWashTheme.PaperAged,
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                    Size = new Float2(ButtonWidth, 18f),
                    Location = new Float2(0f, 28f),
                    Parent = this
                };

                _badge = new PhotoCountBadge
                {
                    Location = new Float2(ButtonWidth - 26f, 0f),
                    Visible = false,
                    Parent = this
                };
            }

            /// <inheritdoc />
            public override void OnMouseEnter(Float2 location)
            {
                if (_icon != null)
                    _icon.IconColor = InkWashTheme.TextBrand;
                if (_label != null)
                    _label.TextColor = InkWashTheme.PaperBright;
                base.OnMouseEnter(location);
            }

            /// <inheritdoc />
            public override void OnMouseLeave()
            {
                if (_icon != null)
                    _icon.IconColor = InkWashTheme.PaperAged;
                if (_label != null)
                    _label.TextColor = InkWashTheme.PaperAged;
                base.OnMouseLeave();
            }

            /// <inheritdoc />
            public override bool OnMouseDown(Float2 location, MouseButton button)
            {
                if (button == MouseButton.Left)
                {
                    Clicked?.Invoke();
                    return true;
                }

                return base.OnMouseDown(location, button);
            }
        }

        /// <summary>
        /// 数量角标：金色胶囊 + 深墨数字（设计稿 .album-count）。
        /// </summary>
        private sealed class PhotoCountBadge : ContainerControl
        {
            /// <summary>角标宽度</summary>
            private const float BadgeWidth = 18f;

            /// <summary>角标高度</summary>
            private const float BadgeHeight = 16f;

            /// <summary>数量</summary>
            private int _count;

            /// <summary>
            /// 数量（同步刷新角标文字）。
            /// </summary>
            public int Count
            {
                get => _count;
                set => _count = value;
            }

            /// <summary>
            /// 构造函数：装饰性控件，不拦截鼠标事件。
            /// </summary>
            public PhotoCountBadge()
            {
                Size = new Float2(BadgeWidth, BadgeHeight);
                BackgroundColor = Color.Transparent;
                Enabled = false;
                AutoFocus = false;
            }

            /// <inheritdoc />
            public override void Draw()
            {
                if (!Visible || Width <= 0f || Height <= 0f)
                    return;

                var bounds = new Rectangle(0, 0, Width, Height);
                InkRenderHelper.FillRoundedRectangle(bounds, Height * 0.5f, InkWashTheme.GoldPrimary);

                var fontRef = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 9f);
                var font = fontRef.GetFont();
                if (font != null)
                {
                    Render2D.DrawText(
                        font,
                        _count.ToString(),
                        bounds,
                        InkWashTheme.BaseSecondary,
                        TextAlignment.Center,
                        TextAlignment.Center,
                        TextWrapping.NoWrap);
                }
            }
        }

        /// <summary>
        /// 快门按钮：外圈金环 + 内芯金渐变圆 + 呼吸脉冲环。
        /// 对应设计稿 .shutter-btn / .shutter-ring / .shutter-core / .shutter-pulse。
        /// </summary>
        private sealed class PhotoShutterButton : ContainerControl
        {
            /// <summary>内芯半径 28px（直径 56px）</summary>
            private const float CoreRadius = 28f;

            /// <summary>外圈半径 36px（直径 72px）</summary>
            private const float RingRadius = 36f;

            /// <summary>脉冲周期 2.5s</summary>
            private const float PulsePeriod = 2.5f;

            /// <summary>是否悬停</summary>
            private bool _hovered;

            /// <summary>是否按下</summary>
            private bool _pressed;

            /// <summary>脉冲计时</summary>
            private float _pulseTime;

            /// <summary>内芯相机图标颜色</summary>
            private readonly PhotoIcon _coreIcon;

            /// <summary>点击事件</summary>
            public event Action Clicked;

            /// <summary>
            /// 构造函数。
            /// </summary>
            public PhotoShutterButton()
            {
                Size = new Float2(ShutterSize, ShutterSize);
                BackgroundColor = Color.Transparent;
                ClipChildren = false;
                AutoFocus = false;

                _coreIcon = new PhotoIcon(PhotoIconKind.Camera, 26f)
                {
                    IconColor = InkWashTheme.BaseDefault,
                    Location = new Float2((ShutterSize - 26f) * 0.5f, (ShutterSize - 26f) * 0.5f),
                    Parent = this
                };
            }

            /// <inheritdoc />
            public override void Update(float deltaTime)
            {
                base.Update(deltaTime);

                _pulseTime += deltaTime;
                if (_pulseTime > PulsePeriod)
                {
                    _pulseTime -= PulsePeriod;
                }
            }

            /// <inheritdoc />
            public override void OnMouseEnter(Float2 location)
            {
                _hovered = true;
                base.OnMouseEnter(location);
            }

            /// <inheritdoc />
            public override void OnMouseLeave()
            {
                _hovered = false;
                _pressed = false;
                base.OnMouseLeave();
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
                base.OnMouseUp(location, button);

                if (button == MouseButton.Left && _pressed)
                {
                    _pressed = false;
                    Clicked?.Invoke();
                    return true;
                }

                return false;
            }

            /// <inheritdoc />
            public override void Draw()
            {
                if (!Visible || Width <= 0f || Height <= 0f)
                    return;

                var center = new Float2(Width * 0.5f, Height * 0.5f);
                float scale = _pressed ? 0.92f : _hovered ? 1.05f : 1f;
                float coreRadius = CoreRadius * scale * (Mathf.Min(Width, Height) / ShutterSize);
                float ringRadius = RingRadius * scale * (Mathf.Min(Width, Height) / ShutterSize);

                // 1. 呼吸脉冲环（2.5s 循环，缩放 1 → 1.4，透明度 0.4 → 0）
                float pulseT = Mathf.Clamp(_pulseTime / PulsePeriod, 0f, 1f);
                float pulseRadius = ringRadius * Mathf.Lerp(1f, 1.4f, pulseT);
                float pulseAlpha = Mathf.Lerp(0.4f, 0f, pulseT);
                InkRenderHelper.DrawCircle(
                    center,
                    pulseRadius,
                    new Color(
                        InkWashTheme.GoldPrimary.R,
                        InkWashTheme.GoldPrimary.G,
                        InkWashTheme.GoldPrimary.B,
                        pulseAlpha),
                    2f);

                // 2. 外圈金环
                InkRenderHelper.DrawCircle(
                    center,
                    ringRadius,
                    _hovered ? InkWashTheme.GoldBright : InkWashTheme.GoldDeep,
                    2f);

                // 3. 内芯金渐变（外层深金 + 内层亮金近似径向渐变）
                InkRenderHelper.FillCircle(center, coreRadius, InkWashTheme.GoldDeep);
                InkRenderHelper.FillCircle(
                    new Float2(center.X - coreRadius * 0.18f, center.Y - coreRadius * 0.18f),
                    coreRadius * 0.86f,
                    InkWashTheme.GoldPrimary);

                // 4. 内芯暗描边
                InkRenderHelper.DrawCircle(center, coreRadius, InkWashTheme.BaseDefault, 2f);

                // 5. 相机图标
                base.Draw();
            }
        }

        #endregion
    }
}
