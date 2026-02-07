using System;
using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.Rendering;
using HundunWorld.Game.Rendering.Materials;

namespace HundunWorld.UI.MetaHuman
{
    /// <summary>
    /// MetaHuman角色外观编辑器主界面
    /// 整合预览区域、参数编辑面板和预设管理功能
    /// </summary>
    public class MetaHumanEditorUI : ContainerControl
    {
        // 主布局比例
        private const float LeftPanelWidthRatio = 0.35f;
        private const float PresetBarHeight = 50f;
        private const float TabBarHeight = 40f;
        
        // 标签页索引
        public enum EditorTab
        {
            Skin = 0,
            Eyes = 1,
            Hair = 2
        }
        
        // 子面板引用
        private SkinEditorPanel _skinPanel;
        private EyeEditorPanel _eyePanel;
        private HairEditorPanel _hairPanel;
        private PresetManagerPanel _presetPanel;
        
        // UI容器
        private Panel _leftContainer;
        private Panel _rightContainer;
        private Panel _presetBar;
        private Panel _tabBar;
        private Panel _editorContent;
        
        // 标签按钮
        private Button _skinTabButton;
        private Button _eyeTabButton;
        private Button _hairTabButton;
        
        // 3D预览控件
        private Viewport3DPreview _previewViewport;
        
        // 当前状态
        private EditorTab _currentTab = EditorTab.Skin;
        
        // 外部系统引用
        public CharacterAppearanceEditor AppearanceEditor { get; set; }
        public CharacterAppearancePreviewController PreviewController { get; set; }
        
        // 事件
        public event Action<EditorTab> OnTabChanged;
        public event Action OnEditorOpened;
        public event Action OnEditorClosed;
        
        public MetaHumanEditorUI()
        {
            AnchorPreset = AnchorPresets.StretchAll;
            BackgroundColor = new Color(0.12f, 0.12f, 0.14f, 1.0f);
        }
        
        /// <summary>
        /// 初始化编辑器UI
        /// </summary>
        public void Initialize()
        {
            // 创建主布局
            CreateMainLayout();
            
            // 创建预设管理栏
            CreatePresetBar();
            
            // 创建标签栏
            CreateTabBar();
            
            // 创建编辑面板
            CreateEditorPanels();
            
            // 创建预览区域
            CreatePreviewArea();
            
            // 绑定事件
            BindEvents();
            
            // 默认显示皮肤面板
            SwitchToTab(EditorTab.Skin);
            
            OnEditorOpened?.Invoke();
        }
        
        /// <summary>
        /// 创建主布局 - 左右分栏
        /// </summary>
        private void CreateMainLayout()
        {
            // 左侧面板容器（参数编辑区）
            _leftContainer = new Panel
            {
                Parent = this,
                AnchorPreset = AnchorPresets.VerticalStretchLeft,
                Width = Width * LeftPanelWidthRatio,
                BackgroundColor = new Color(0.15f, 0.15f, 0.17f, 1.0f)
            };
            
            // 右侧面板容器（3D预览区）
            _rightContainer = new Panel
            {
                Parent = this,
                AnchorPreset = AnchorPresets.StretchAll,
                Offsets = new Margin(Width * LeftPanelWidthRatio, 0, 0, 0),
                BackgroundColor = new Color(0.08f, 0.08f, 0.1f, 1.0f)
            };
        }
        
        /// <summary>
        /// 创建预设管理栏
        /// </summary>
        private void CreatePresetBar()
        {
            _presetBar = new Panel
            {
                Parent = _leftContainer,
                AnchorPreset = AnchorPresets.HorizontalStretchTop,
                Height = PresetBarHeight,
                BackgroundColor = new Color(0.18f, 0.18f, 0.2f, 1.0f)
            };
            
            // 创建预设管理面板
            _presetPanel = new PresetManagerPanel
            {
                Parent = _presetBar,
                AnchorPreset = AnchorPresets.StretchAll
            };
        }
        
        /// <summary>
        /// 创建标签栏
        /// </summary>
        private void CreateTabBar()
        {
            _tabBar = new Panel
            {
                Parent = _leftContainer,
                AnchorPreset = AnchorPresets.HorizontalStretchTop,
                Y = PresetBarHeight,
                Height = TabBarHeight,
                BackgroundColor = new Color(0.13f, 0.13f, 0.15f, 1.0f)
            };
            
            float buttonWidth = _tabBar.Width / 3;
            
            // 皮肤标签
            _skinTabButton = CreateTabButton("皮肤", 0, buttonWidth);
            _skinTabButton.Clicked += () => SwitchToTab(EditorTab.Skin);
            
            // 眼睛标签
            _eyeTabButton = CreateTabButton("眼睛", 1, buttonWidth);
            _eyeTabButton.Clicked += () => SwitchToTab(EditorTab.Eyes);
            
            // 毛发标签
            _hairTabButton = CreateTabButton("毛发", 2, buttonWidth);
            _hairTabButton.Clicked += () => SwitchToTab(EditorTab.Hair);
        }
        
        /// <summary>
        /// 创建标签按钮
        /// </summary>
        private Button CreateTabButton(string text, int index, float width)
        {
            var button = new Button
            {
                Parent = _tabBar,
                Text = text,
                X = index * width,
                Y = 2,
                Width = width - 4,
                Height = TabBarHeight - 4,
                BackgroundColor = new Color(0.2f, 0.2f, 0.22f, 1.0f),
                BorderColor = new Color(0.3f, 0.3f, 0.32f, 1.0f),
                TextColor = Color.White
            };
            return button;
        }
        
        /// <summary>
        /// 创建编辑面板
        /// </summary>
        private void CreateEditorPanels()
        {
            // 编辑内容容器
            _editorContent = new Panel
            {
                Parent = _leftContainer,
                AnchorPreset = AnchorPresets.StretchAll,
                Offsets = new Margin(0, 0, PresetBarHeight + TabBarHeight, 0),
                BackgroundColor = new Color(0.14f, 0.14f, 0.16f, 1.0f)
            };
            
            // 创建皮肤编辑面板
            _skinPanel = new SkinEditorPanel
            {
                Parent = _editorContent,
                AnchorPreset = AnchorPresets.StretchAll,
                Visible = true
            };
            
            // 创建眼睛编辑面板
            _eyePanel = new EyeEditorPanel
            {
                Parent = _editorContent,
                AnchorPreset = AnchorPresets.StretchAll,
                Visible = false
            };
            
            // 创建毛发编辑面板
            _hairPanel = new HairEditorPanel
            {
                Parent = _editorContent,
                AnchorPreset = AnchorPresets.StretchAll,
                Visible = false
            };
        }
        
        /// <summary>
        /// 创建3D预览区域
        /// </summary>
        private void CreatePreviewArea()
        {
            // 预览视口
            _previewViewport = new Viewport3DPreview
            {
                Parent = _rightContainer,
                AnchorPreset = AnchorPresets.StretchAll,
                Offsets = new Margin(10, 10, 10, 60)
            };
            
            // 预览控制按钮区
            var previewControlBar = new Panel
            {
                Parent = _rightContainer,
                AnchorPreset = AnchorPresets.HorizontalStretchBottom,
                Height = 50,
                BackgroundColor = new Color(0.1f, 0.1f, 0.12f, 1.0f)
            };
            
            // 预览模式按钮
            float buttonOffset = 10;
            
            var faceButton = new Button
            {
                Parent = previewControlBar,
                Text = "面部特写",
                X = buttonOffset,
                Y = 10,
                Width = 80,
                Height = 30,
                BackgroundColor = new Color(0.25f, 0.25f, 0.27f, 1.0f)
            };
            faceButton.Clicked += () => SetPreviewMode(CharacterAppearancePreviewController.PreviewMode.FaceCloseUp);
            buttonOffset += 90;
            
            var upperButton = new Button
            {
                Parent = previewControlBar,
                Text = "上半身",
                X = buttonOffset,
                Y = 10,
                Width = 80,
                Height = 30,
                BackgroundColor = new Color(0.25f, 0.25f, 0.27f, 1.0f)
            };
            upperButton.Clicked += () => SetPreviewMode(CharacterAppearancePreviewController.PreviewMode.UpperBody);
            buttonOffset += 90;
            
            var fullButton = new Button
            {
                Parent = previewControlBar,
                Text = "全身",
                X = buttonOffset,
                Y = 10,
                Width = 80,
                Height = 30,
                BackgroundColor = new Color(0.25f, 0.25f, 0.27f, 1.0f)
            };
            fullButton.Clicked += () => SetPreviewMode(CharacterAppearancePreviewController.PreviewMode.FullBody);
            buttonOffset += 110;
            
            // 自动旋转开关
            var rotateLabel = new Label
            {
                Parent = previewControlBar,
                Text = "自动旋转",
                X = buttonOffset,
                Y = 12,
                Width = 60,
                Height = 26,
                TextColor = Color.White
            };
            var rotateToggle = new CheckBox
            {
                Parent = previewControlBar,
                X = buttonOffset + 65,
                Y = 12,
                Checked = true
            };
            rotateToggle.StateChanged += (cb) => ToggleAutoRotation(cb.Checked);
            buttonOffset += 100;
            
            // 截图按钮
            var screenshotButton = new Button
            {
                Parent = previewControlBar,
                Text = "截图",
                X = previewControlBar.Width - 90,
                Y = 10,
                Width = 80,
                Height = 30,
                BackgroundColor = new Color(0.3f, 0.5f, 0.3f, 1.0f)
            };
            screenshotButton.Clicked += CaptureScreenshot;
        }
        
        /// <summary>
        /// 绑定事件
        /// </summary>
        private void BindEvents()
        {
            // 绑定皮肤面板事件 - 使用已存在的方法
            _skinPanel.OnBaseColorChanged += (color) => AppearanceEditor?.SetSkinBaseColor(color);
            _skinPanel.OnRoughnessChanged += (value) => AppearanceEditor?.SetSkinRoughness(value);
            _skinPanel.OnSSSIntensityChanged += (value) => AppearanceEditor?.SetSkinSSSIntensity(value);
            _skinPanel.OnEpidermisColorChanged += (color) => AppearanceEditor?.SetSkinEpidermisColor(color);
            _skinPanel.OnDermisColorChanged += (color) => AppearanceEditor?.SetSkinDermisColor(color);
            _skinPanel.OnSubcutisColorChanged += (color) => AppearanceEditor?.SetSkinSubcutisColor(color);
            
            // 绑定眼睛面板事件
            _eyePanel.OnIrisColorChanged += (color) => AppearanceEditor?.SetEyeIrisColor(color);
            _eyePanel.OnPupilSizeChanged += (value) => AppearanceEditor?.SetEyePupilSize(value);
            _eyePanel.OnEyeWetnessChanged += (value) => AppearanceEditor?.SetEyeWetness(value);
            
            // 绑定毛发面板事件
            _hairPanel.OnRootColorChanged += (color) => AppearanceEditor?.SetHairRootColor(color);
            _hairPanel.OnTipColorChanged += (color) => AppearanceEditor?.SetHairTipColor(color);
            _hairPanel.OnRoughnessChanged += (value) => AppearanceEditor?.SetHairRoughness(value);
            _hairPanel.OnAnisotropyChanged += (value) => AppearanceEditor?.SetHairAnisotropyIntensity(value);
            
            // 绑定预设面板事件
            _presetPanel.OnPresetSelected += LoadPreset;
            _presetPanel.OnSaveRequested += SaveCurrentPreset;
            _presetPanel.OnQuickPresetSelected += ApplyQuickPreset;
            
            // 绑定外观编辑器事件
            if (AppearanceEditor != null)
            {
                AppearanceEditor.OnPresetLoaded += OnPresetLoadedHandler;
                AppearanceEditor.OnSkinChanged += RefreshSkinPanel;
                AppearanceEditor.OnEyeChanged += RefreshEyePanel;
                AppearanceEditor.OnHairChanged += RefreshHairPanel;
            }
        }
        
        /// <summary>
        /// 切换到指定标签页
        /// </summary>
        public void SwitchToTab(EditorTab tab)
        {
            _currentTab = tab;
            
            // 更新面板可见性
            _skinPanel.Visible = tab == EditorTab.Skin;
            _eyePanel.Visible = tab == EditorTab.Eyes;
            _hairPanel.Visible = tab == EditorTab.Hair;
            
            // 更新标签按钮样式
            UpdateTabButtonStyles();
            
            OnTabChanged?.Invoke(tab);
        }
        
        /// <summary>
        /// 更新标签按钮样式
        /// </summary>
        private void UpdateTabButtonStyles()
        {
            var activeColor = new Color(0.3f, 0.5f, 0.7f, 1.0f);
            var inactiveColor = new Color(0.2f, 0.2f, 0.22f, 1.0f);
            
            _skinTabButton.BackgroundColor = _currentTab == EditorTab.Skin ? activeColor : inactiveColor;
            _eyeTabButton.BackgroundColor = _currentTab == EditorTab.Eyes ? activeColor : inactiveColor;
            _hairTabButton.BackgroundColor = _currentTab == EditorTab.Hair ? activeColor : inactiveColor;
        }
        
        /// <summary>
        /// 设置预览模式
        /// </summary>
        private void SetPreviewMode(CharacterAppearancePreviewController.PreviewMode mode)
        {
            PreviewController?.ApplyPreviewMode(mode);
        }
        
        /// <summary>
        /// 切换自动旋转
        /// </summary>
        private void ToggleAutoRotation(bool enabled)
        {
            if (PreviewController != null)
            {
                PreviewController.EnableAutoRotation = enabled;
            }
        }
        
        /// <summary>
        /// 截图
        /// </summary>
        private void CaptureScreenshot()
        {
            string path = $"Screenshots/MetaHuman_Capture_{System.DateTime.Now:yyyyMMdd_HHmmss}.png";
            PreviewController?.CapturePreviewImage(path);
            Debug.Log($"截图请求已发送: {path}");
        }
        
        /// <summary>
        /// 加载预设
        /// </summary>
        private void LoadPreset(string presetPath)
        {
            AppearanceEditor?.LoadPreset(presetPath);
        }
        
        /// <summary>
        /// 保存当前预设
        /// </summary>
        private void SaveCurrentPreset(string presetName)
        {
            string path = $"Content/Presets/Characters/{presetName}.json";
            AppearanceEditor?.SavePreset(path, presetName);
        }
        
        /// <summary>
        /// 应用快速预设
        /// </summary>
        private void ApplyQuickPreset(string presetType)
        {
            if (AppearanceEditor == null) return;
            
            switch (presetType.ToLower())
            {
                // 皮肤预设
                case "young_skin":
                    AppearanceEditor.ApplyYoungSkinPreset();
                    break;
                case "mature_skin":
                    AppearanceEditor.ApplyMatureSkinPreset();
                    break;
                case "oily_skin":
                    AppearanceEditor.ApplyOilySkinPreset();
                    break;
                case "dry_skin":
                    AppearanceEditor.ApplyDrySkinPreset();
                    break;
                    
                // 眼睛预设
                case "blue_eyes":
                    AppearanceEditor.ApplyBlueEyePreset();
                    break;
                case "brown_eyes":
                    AppearanceEditor.ApplyBrownEyePreset();
                    break;
                case "green_eyes":
                    AppearanceEditor.ApplyGreenEyePreset();
                    break;
                    
                // 毛发预设
                case "black_hair":
                    AppearanceEditor.ApplyBlackHairPreset();
                    break;
                case "blonde_hair":
                    AppearanceEditor.ApplyBlondeHairPreset();
                    break;
                case "brown_hair":
                    AppearanceEditor.ApplyBrownHairPreset();
                    break;
                case "red_hair":
                    AppearanceEditor.ApplyRedHairPreset();
                    break;
                case "white_hair":
                    AppearanceEditor.ApplyWhiteHairPreset();
                    break;
            }
        }
        
        /// <summary>
        /// 预设加载完成处理
        /// </summary>
        private void OnPresetLoadedHandler(string presetName)
        {
            // 刷新所有面板
            RefreshAllPanels();
            Debug.Log($"预设 '{presetName}' 已加载");
        }
        
        /// <summary>
        /// 刷新皮肤面板
        /// </summary>
        private void RefreshSkinPanel()
        {
            if (AppearanceEditor?.SkinController == null) return;
            
            var controller = AppearanceEditor.SkinController;
            _skinPanel.SetBaseColor(controller.BaseColor);
            _skinPanel.SetRoughness(controller.BaseRoughness);
            _skinPanel.SetSSSIntensity(controller.SSSIntensity);
        }
        
        /// <summary>
        /// 刷新眼睛面板
        /// </summary>
        private void RefreshEyePanel()
        {
            if (AppearanceEditor?.EyeController == null) return;
            
            var controller = AppearanceEditor.EyeController;
            _eyePanel.SetIrisColor(controller.IrisColor);
            _eyePanel.SetPupilSize(controller.PupilSize);
            _eyePanel.SetScleraColor(controller.ScleraColor);
        }
        
        /// <summary>
        /// 刷新毛发面板
        /// </summary>
        private void RefreshHairPanel()
        {
            if (AppearanceEditor?.HairController == null) return;
            
            var controller = AppearanceEditor.HairController;
            _hairPanel.SetBaseColor(controller.RootColor);
            _hairPanel.SetRoughness(controller.Roughness);
            _hairPanel.SetAnisotropy(controller.AnisotropyIntensity);
        }
        
        /// <summary>
        /// 刷新所有面板
        /// </summary>
        public void RefreshAllPanels()
        {
            RefreshSkinPanel();
            RefreshEyePanel();
            RefreshHairPanel();
        }
        
        /// <summary>
        /// 设置编辑目标角色
        /// </summary>
        public void SetTargetCharacter(Actor character)
        {
            // 加载角色模型到预览
            if (character != null)
            {
                PreviewController?.LoadPreviewCharacter(character.Name);
            }
            RefreshAllPanels();
        }
        
        /// <summary>
        /// 重置为默认值
        /// </summary>
        public void ResetToDefault()
        {
            AppearanceEditor?.ApplyPreset(
                CharacterAppearancePreset.CreateDefault());
            RefreshAllPanels();
        }
        
        /// <summary>
        /// 关闭编辑器
        /// </summary>
        public void Close()
        {
            OnEditorClosed?.Invoke();
            Dispose();
        }
        
        /// <inheritdoc/>
        public override void OnDestroy()
        {
            // 解绑事件
            if (AppearanceEditor != null)
            {
                AppearanceEditor.OnPresetLoaded -= OnPresetLoadedHandler;
                AppearanceEditor.OnSkinChanged -= RefreshSkinPanel;
                AppearanceEditor.OnEyeChanged -= RefreshEyePanel;
                AppearanceEditor.OnHairChanged -= RefreshHairPanel;
            }
            
            base.OnDestroy();
        }
        
        /// <inheritdoc/>
        protected override void OnSizeChanged()
        {
            base.OnSizeChanged();
            
            // 更新左侧面板宽度
            if (_leftContainer != null)
            {
                _leftContainer.Width = Width * LeftPanelWidthRatio;
            }
            
            // 更新右侧面板偏移
            if (_rightContainer != null)
            {
                _rightContainer.Offsets = new Margin(Width * LeftPanelWidthRatio, 0, 0, 0);
            }
        }
    }
    
    /// <summary>
    /// 3D预览视口控件 - 简化版，使用Panel作为基类
    /// </summary>
    public class Viewport3DPreview : Panel
    {
        private SceneRenderTask _renderTask;
        private Camera _camera;
        private Actor _previewActor;
        
        // 相机控制
        private bool _isDragging;
        private Float2 _lastMousePos;
        private float _cameraYaw;
        private float _cameraPitch;
        private float _cameraDistance = 2.0f;
        private Float3 _cameraTarget = Float3.Zero;
        
        public Viewport3DPreview()
        {
            AnchorPreset = AnchorPresets.StretchAll;
            BackgroundColor = new Color(0.1f, 0.1f, 0.12f, 1.0f);
        }
        
        /// <summary>
        /// 初始化预览视口
        /// </summary>
        public void Initialize(SceneRenderTask renderTask, Camera camera)
        {
            _renderTask = renderTask;
            _camera = camera;
        }
        
        /// <summary>
        /// 设置预览Actor
        /// </summary>
        public void SetPreviewActor(Actor actor)
        {
            _previewActor = actor;
        }
        
        /// <summary>
        /// 设置相机位置
        /// </summary>
        public void SetCameraPosition(float distance, float yaw, float pitch)
        {
            _cameraDistance = distance;
            _cameraYaw = yaw;
            _cameraPitch = pitch;
            UpdateCameraTransform();
        }
        
        /// <summary>
        /// 设置相机目标点
        /// </summary>
        public void SetCameraTarget(Float3 target)
        {
            _cameraTarget = target;
            UpdateCameraTransform();
        }
        
        /// <inheritdoc/>
        public override bool OnMouseDown(Float2 location, MouseButton button)
        {
            if (button == MouseButton.Left || button == MouseButton.Middle)
            {
                _isDragging = true;
                _lastMousePos = location;
                return true;
            }
            return base.OnMouseDown(location, button);
        }
        
        /// <inheritdoc/>
        public override void OnMouseMove(Float2 location)
        {
            if (_isDragging)
            {
                var delta = location - _lastMousePos;
                _cameraYaw += delta.X * 0.5f;
                _cameraPitch = Mathf.Clamp(_cameraPitch - delta.Y * 0.5f, -80f, 80f);
                _lastMousePos = location;
                UpdateCameraTransform();
            }
            base.OnMouseMove(location);
        }
        
        /// <inheritdoc/>
        public override bool OnMouseUp(Float2 location, MouseButton button)
        {
            if (button == MouseButton.Left || button == MouseButton.Middle)
            {
                _isDragging = false;
                return true;
            }
            return base.OnMouseUp(location, button);
        }
        
        /// <inheritdoc/>
        public override bool OnMouseWheel(Float2 location, float delta)
        {
            _cameraDistance = Mathf.Clamp(_cameraDistance - delta * 0.1f, 0.5f, 10f);
            UpdateCameraTransform();
            return true;
        }
        
        /// <summary>
        /// 更新相机变换
        /// </summary>
        private void UpdateCameraTransform()
        {
            if (_camera == null) return;
            
            float yawRad = _cameraYaw * Mathf.DegreesToRadians;
            float pitchRad = _cameraPitch * Mathf.DegreesToRadians;
            
            var offset = new Float3(
                Mathf.Cos(pitchRad) * Mathf.Sin(yawRad),
                Mathf.Sin(pitchRad),
                Mathf.Cos(pitchRad) * Mathf.Cos(yawRad)
            ) * _cameraDistance;
            
            _camera.Position = _cameraTarget + offset;
            _camera.LookAt(_cameraTarget);
        }
    }
}
