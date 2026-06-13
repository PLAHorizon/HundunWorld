using System;
using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.Rendering;
using HundunWorld.Game.Rendering.Materials;
using HundunWorld.Game.UI.Components;

namespace HundunWorld.UI.MetaHuman
{
    public class MetaHumanEditorUI : ContainerControl
    {
        private const float LeftPanelWidthRatio = MetaHumanStyles.Sizes.LeftPanelWidthRatio;
        private const float PresetBarHeight = MetaHumanStyles.Sizes.PresetBarHeight;
        private const float TabBarHeight = MetaHumanStyles.Sizes.TabBarHeight;
        
        public enum EditorTab
        {
            Skin = 0,
            Eyes = 1,
            Hair = 2
        }
        
        private SkinEditorPanel _skinPanel;
        private EyeEditorPanel _eyePanel;
        private HairEditorPanel _hairPanel;
        private PresetManagerPanel _presetPanel;
        
        private Panel _leftContainer;
        private Panel _rightContainer;
        private Panel _tabBar;
        private Panel _editorContent;
        
        private Button _skinTabButton;
        private Button _eyeTabButton;
        private Button _hairTabButton;
        
        private Viewport3DPreview _previewViewport;
        
        private EditorTab _currentTab = EditorTab.Skin;
                
        /// <summary>
        /// 设置预览视口的目标场景，确保 Actor 生成到正确的场景
        /// </summary>
        public FlaxEngine.Scene PreviewTargetScene
        {
            get => _previewViewport?.TargetScene;
            set { if (_previewViewport != null) _previewViewport.TargetScene = value; }
        }
        
        public CharacterAppearanceEditor AppearanceEditor { get; set; }
        public CharacterAppearancePreviewController PreviewController { get; set; }
        
        public event Action<EditorTab> OnTabChanged;
        public event Action OnEditorOpened;
        public event Action OnEditorClosed;
        
        public MetaHumanEditorUI()
        {
            AnchorPreset = AnchorPresets.StretchAll;
            BackgroundColor = MetaHumanStyles.Colors.BackgroundDark;
            Initialize();
        }
        
        public void Initialize()
        {
            CreateMainLayout();
            CreatePresetBar();
            CreateTabBar();
            CreateEditorPanels();
            CreatePreviewArea();
            BindEvents();
            SwitchToTab(EditorTab.Skin);
            OnEditorOpened?.Invoke();
        }
        
        private void CreateMainLayout()
        {
            Float2 screenSize = FlaxEngine.Screen.Size;
            _leftContainer = new Panel
            {
                Parent = this,
                AnchorPreset = AnchorPresets.VerticalStretchLeft,
                Width = screenSize.X * LeftPanelWidthRatio,
                BackgroundColor = MetaHumanStyles.Colors.BackgroundMedium
            };
            
            _rightContainer = new Panel
            {
                Parent = this,
                AnchorPreset = AnchorPresets.StretchAll,
                Offsets = new Margin(screenSize.X * LeftPanelWidthRatio, 0, 0, 0),
                BackgroundColor = MetaHumanStyles.Colors.BackgroundDark
            };
           
        }
        
        private void CreatePresetBar()
        {
            var presetBarContainer = new Panel
            {
                Parent = _leftContainer,
                AnchorPreset = AnchorPresets.HorizontalStretchTop,
                Height = PresetBarHeight,
                BackgroundColor = MetaHumanStyles.Colors.BackgroundElevated
            };
            
            var bottomBorder = new Panel
            {
                Parent = presetBarContainer,
                AnchorPreset = AnchorPresets.HorizontalStretchBottom,
                Height = 1,
                BackgroundColor = MetaHumanStyles.Colors.Separator
            };
            
            _presetPanel = new PresetManagerPanel
            {
                Parent = presetBarContainer,
                AnchorPreset = AnchorPresets.StretchAll,
                Offsets = new Margin(MetaHumanStyles.Sizes.PaddingSmall, MetaHumanStyles.Sizes.PaddingSmall, 
                    MetaHumanStyles.Sizes.PaddingSmall, MetaHumanStyles.Sizes.PaddingSmall + 1)
            };
        }
        
        private void CreateTabBar()
        {
            _tabBar = new Panel
            {
                Parent = _leftContainer,
                AnchorPreset = AnchorPresets.HorizontalStretchTop,
                Y = PresetBarHeight,
                Height = TabBarHeight,
                BackgroundColor = MetaHumanStyles.Colors.BackgroundLight
            };
            
            float buttonWidth = (_tabBar.Width - MetaHumanStyles.Sizes.Padding * 4) / 3;
            float buttonHeight = TabBarHeight - MetaHumanStyles.Sizes.Padding * 2;
            float buttonY = MetaHumanStyles.Sizes.Padding;
            
            _skinTabButton = CreateTabButton("皮肤", 0, buttonWidth, buttonHeight, buttonY);
            _skinTabButton.Clicked += () => SwitchToTab(EditorTab.Skin);
            
            _eyeTabButton = CreateTabButton("眼睛", 1, buttonWidth, buttonHeight, buttonY);
            _eyeTabButton.Clicked += () => SwitchToTab(EditorTab.Eyes);
            
            _hairTabButton = CreateTabButton("毛发", 2, buttonWidth, buttonHeight, buttonY);
            _hairTabButton.Clicked += () => SwitchToTab(EditorTab.Hair);
        }
        
        private Button CreateTabButton(string text, int index, float width, float height, float y)
        {
            var button = new Button
            {
                Parent = _tabBar,
                Text = text,
                X = MetaHumanStyles.Sizes.Padding + index * (width + MetaHumanStyles.Sizes.PaddingSmall),
                Y = y,
                Width = width,
                Height = height,
                BackgroundColor = MetaHumanStyles.Colors.TabInactive,
                TextColor = MetaHumanStyles.Colors.TextSecondary
            };
            return button;
        }
        
        private void CreateEditorPanels()
        {
            _editorContent = new Panel
            {
                Parent = _leftContainer,
                AnchorPreset = AnchorPresets.StretchAll,
                Offsets = new Margin(0, 0, PresetBarHeight + TabBarHeight, 0),
                BackgroundColor = MetaHumanStyles.Colors.BackgroundMedium
            };
            
            _skinPanel = new SkinEditorPanel
            {
                Parent = _editorContent,
                AnchorPreset = AnchorPresets.StretchAll,
                Offsets = new Margin(MetaHumanStyles.Sizes.PaddingSmall, MetaHumanStyles.Sizes.PaddingSmall, 
                    MetaHumanStyles.Sizes.PaddingSmall, MetaHumanStyles.Sizes.PaddingSmall),
                Visible = true
            };
            
            _eyePanel = new EyeEditorPanel
            {
                Parent = _editorContent,
                AnchorPreset = AnchorPresets.StretchAll,
                Offsets = new Margin(MetaHumanStyles.Sizes.PaddingSmall, MetaHumanStyles.Sizes.PaddingSmall, 
                    MetaHumanStyles.Sizes.PaddingSmall, MetaHumanStyles.Sizes.PaddingSmall),
                Visible = false
            };
            
            _hairPanel = new HairEditorPanel
            {
                Parent = _editorContent,
                AnchorPreset = AnchorPresets.StretchAll,
                Offsets = new Margin(MetaHumanStyles.Sizes.PaddingSmall, MetaHumanStyles.Sizes.PaddingSmall, 
                    MetaHumanStyles.Sizes.PaddingSmall, MetaHumanStyles.Sizes.PaddingSmall),
                Visible = false
            };
        }
        
        private void CreatePreviewArea()
        {
            const float controlBarHeight = MetaHumanStyles.Sizes.ControlBarHeight;
            const float controlBarBottomMargin = MetaHumanStyles.Sizes.Padding;
            
            var previewBorder = new Panel
            {
                Parent = _rightContainer,
                AnchorPreset = AnchorPresets.StretchAll,
                Offsets = new Margin(MetaHumanStyles.Sizes.Padding, MetaHumanStyles.Sizes.Padding, 
                    MetaHumanStyles.Sizes.Padding, controlBarHeight + controlBarBottomMargin + MetaHumanStyles.Sizes.Padding),
                BackgroundColor = MetaHumanStyles.Colors.BackgroundLight
            };
            
            // 计算预览区域大小
            Float2 screenSize = FlaxEngine.Screen.Size;
            float previewWidth = screenSize.X * (1 - LeftPanelWidthRatio) - MetaHumanStyles.Sizes.Padding * 2;
            float previewHeight = screenSize.Y - MetaHumanStyles.Sizes.Padding * 2 - controlBarHeight - controlBarBottomMargin;
            
            _previewViewport = new Viewport3DPreview(new Float2(previewWidth, previewHeight))
            {
                Parent = previewBorder,
                AnchorPreset = AnchorPresets.StretchAll,
                Offsets = new Margin(1, 1, 1, 1)
            };
            
            // 初始化视口（现在有正确的尺寸）
            _previewViewport.InitializeViewport();
            _rightContainer.AddChild(_previewViewport);
            // 模型加载已移至 LoadDefaultModel()，由外部根据需要调用
            
            var previewControlBar = new Panel
            {
                Parent = _rightContainer,
                AnchorPreset = AnchorPresets.HorizontalStretchBottom,
                Height = controlBarHeight,
                Offsets = new Margin(MetaHumanStyles.Sizes.Padding, MetaHumanStyles.Sizes.Padding, 0, controlBarBottomMargin),
                BackgroundColor = MetaHumanStyles.Colors.BackgroundElevated
            };
            
            CreatePreviewControls(previewControlBar);
        }
        
        /// <summary>
        /// 加载默认角色模型到预览视口（由外部显式调用，避免与共享预览面板重复加载）
        /// </summary>
        public void LoadDefaultModel()
        {
            // 尝试加载角色模型
            string modelPath = "Content/Character/Models/skm_uefn_mannequin.flax";

            // 使用同步加载，利用引擎内部缓存避免 registry 冲突
            var content = FlaxEngine.Content.Load(modelPath);
            if (content != null)
            {
                if (content is FlaxEngine.Prefab playerPrefab && playerPrefab.IsLoaded)
                {
                    _previewViewport.LoadFromPrefab(playerPrefab);
                    FlaxEngine.Debug.Log($"[MetaHumanEditorUI] 已从预制体加载角色模型: {modelPath}");
                    return;
                }

                if (content is FlaxEngine.Model staticModel && staticModel.IsLoaded)
                {
                    _previewViewport.LoadStaticModel(modelPath);
                    FlaxEngine.Debug.Log($"[MetaHumanEditorUI] 已加载静态模型: {modelPath}");
                    return;
                }
            }

            FlaxEngine.Debug.LogWarning("[MetaHumanEditorUI] 未找到角色模型，3D预览区域将为空");
        }
        
        private void CreatePreviewControls(Panel parent)
        {
            float buttonY = (parent.Height - MetaHumanStyles.Sizes.ButtonHeight) / 2;
            float currentX = MetaHumanStyles.Sizes.Padding;
            
            var faceButton = MetaHumanStyles.CreateStyledButton("面部", MetaHumanStyles.Sizes.ButtonWidth, MetaHumanStyles.Sizes.ButtonHeight, ButtonStyle.Ghost);
            faceButton.Parent = parent;
            faceButton.X = currentX;
            faceButton.Y = buttonY;
            faceButton.Clicked += () => SetPreviewMode(CharacterAppearancePreviewController.PreviewMode.FaceCloseUp);
            currentX += MetaHumanStyles.Sizes.ButtonWidth + MetaHumanStyles.Sizes.PaddingSmall;
            
            var upperButton = MetaHumanStyles.CreateStyledButton("上半身", MetaHumanStyles.Sizes.ButtonWidth + 10, MetaHumanStyles.Sizes.ButtonHeight, ButtonStyle.Ghost);
            upperButton.Parent = parent;
            upperButton.X = currentX;
            upperButton.Y = buttonY;
            upperButton.Clicked += () => SetPreviewMode(CharacterAppearancePreviewController.PreviewMode.UpperBody);
            currentX += MetaHumanStyles.Sizes.ButtonWidth + 10 + MetaHumanStyles.Sizes.PaddingSmall;
            
            var fullButton = MetaHumanStyles.CreateStyledButton("全身", MetaHumanStyles.Sizes.ButtonWidth, MetaHumanStyles.Sizes.ButtonHeight, ButtonStyle.Ghost);
            fullButton.Parent = parent;
            fullButton.X = currentX;
            fullButton.Y = buttonY;
            fullButton.Clicked += () => SetPreviewMode(CharacterAppearancePreviewController.PreviewMode.FullBody);
            currentX += MetaHumanStyles.Sizes.ButtonWidth + MetaHumanStyles.Sizes.PaddingLarge;
            
            var rotateLabel = new Label
            {
                Parent = parent,
                Text = "自动旋转",
                X = currentX,
                Y = buttonY + 6,
                Width = 65,
                Height = 20,
                TextColor = MetaHumanStyles.Colors.TextSecondary,
                HorizontalAlignment = TextAlignment.Near
            };
            
            var rotateToggle = new CheckBox
            {
                Parent = parent,
                X = currentX + 68,
                Y = buttonY + 4,
                Checked = true
            };
            rotateToggle.StateChanged += (cb) => ToggleAutoRotation(cb.Checked);
            currentX += 100;
            
            var separator = new Panel
            {
                Parent = parent,
                X = currentX,
                Y = buttonY - 4,
                Width = 1,
                Height = MetaHumanStyles.Sizes.ButtonHeight + 8,
                BackgroundColor = MetaHumanStyles.Colors.Separator
            };
            currentX += MetaHumanStyles.Sizes.Padding;
            
            var screenshotButton = MetaHumanStyles.CreateStyledButton("截图", MetaHumanStyles.Sizes.ButtonWidth + 10, MetaHumanStyles.Sizes.ButtonHeight, ButtonStyle.Accent);
            screenshotButton.Parent = parent;
            screenshotButton.X = parent.Width - MetaHumanStyles.Sizes.ButtonWidth - 10 - MetaHumanStyles.Sizes.Padding;
            screenshotButton.Y = buttonY;
            screenshotButton.Clicked += CaptureScreenshot;
        }
        
        private void BindEvents()
        {
            _skinPanel.OnBaseColorChanged += (color) => AppearanceEditor?.SetSkinBaseColor(color);
            _skinPanel.OnRoughnessChanged += (value) => AppearanceEditor?.SetSkinRoughness(value);
            _skinPanel.OnSSSIntensityChanged += (value) => AppearanceEditor?.SetSkinSSSIntensity(value);
            _skinPanel.OnEpidermisColorChanged += (color) => AppearanceEditor?.SetSkinEpidermisColor(color);
            _skinPanel.OnDermisColorChanged += (color) => AppearanceEditor?.SetSkinDermisColor(color);
            _skinPanel.OnSubcutisColorChanged += (color) => AppearanceEditor?.SetSkinSubcutisColor(color);
            
            _eyePanel.OnIrisColorChanged += (color) => AppearanceEditor?.SetEyeIrisColor(color);
            _eyePanel.OnPupilSizeChanged += (value) => AppearanceEditor?.SetEyePupilSize(value);
            _eyePanel.OnEyeWetnessChanged += (value) => AppearanceEditor?.SetEyeWetness(value);
            
            _hairPanel.OnRootColorChanged += (color) => AppearanceEditor?.SetHairRootColor(color);
            _hairPanel.OnTipColorChanged += (color) => AppearanceEditor?.SetHairTipColor(color);
            _hairPanel.OnRoughnessChanged += (value) => AppearanceEditor?.SetHairRoughness(value);
            _hairPanel.OnAnisotropyChanged += (value) => AppearanceEditor?.SetHairAnisotropyIntensity(value);
            
            _presetPanel.OnPresetSelected += LoadPreset;
            _presetPanel.OnSaveRequested += SaveCurrentPreset;
            _presetPanel.OnQuickPresetSelected += ApplyQuickPreset;
            
            if (AppearanceEditor != null)
            {
                AppearanceEditor.OnPresetLoaded += OnPresetLoadedHandler;
                AppearanceEditor.OnSkinChanged += RefreshSkinPanel;
                AppearanceEditor.OnEyeChanged += RefreshEyePanel;
                AppearanceEditor.OnHairChanged += RefreshHairPanel;
            }
        }
        
        public void SwitchToTab(EditorTab tab)
        {
            _currentTab = tab;
            
            _skinPanel.Visible = tab == EditorTab.Skin;
            _eyePanel.Visible = tab == EditorTab.Eyes;
            _hairPanel.Visible = tab == EditorTab.Hair;
            
            UpdateTabButtonStyles();
            OnTabChanged?.Invoke(tab);
        }
        
        private void UpdateTabButtonStyles()
        {
            UpdateSingleTabStyle(_skinTabButton, _currentTab == EditorTab.Skin);
            UpdateSingleTabStyle(_eyeTabButton, _currentTab == EditorTab.Eyes);
            UpdateSingleTabStyle(_hairTabButton, _currentTab == EditorTab.Hair);
        }
        
        private void UpdateSingleTabStyle(Button button, bool isActive)
        {
            if (isActive)
            {
                button.BackgroundColor = MetaHumanStyles.Colors.TabActive;
                button.TextColor = MetaHumanStyles.Colors.TextPrimary;
            }
            else
            {
                button.BackgroundColor = MetaHumanStyles.Colors.TabInactive;
                button.TextColor = MetaHumanStyles.Colors.TextSecondary;
            }
        }
        
        private void SetPreviewMode(CharacterAppearancePreviewController.PreviewMode mode)
        {
            PreviewController?.ApplyPreviewMode(mode);
        }
        
        private void ToggleAutoRotation(bool enabled)
        {
            if (PreviewController != null)
            {
                PreviewController.EnableAutoRotation = enabled;
            }
        }
        
        private void CaptureScreenshot()
        {
            string path = $"Screenshots/MetaHuman_Capture_{System.DateTime.Now:yyyyMMdd_HHmmss}.png";
            PreviewController?.CapturePreviewImage(path);
            Debug.Log($"截图已保存: {path}");
        }
        
        private void LoadPreset(string presetPath)
        {
            AppearanceEditor?.LoadPreset(presetPath);
        }
        
        private void SaveCurrentPreset(string presetName)
        {
            string path = $"Content/Presets/Characters/{presetName}.json";
            AppearanceEditor?.SavePreset(path, presetName);
        }
        
        private void ApplyQuickPreset(string presetType)
        {
            if (AppearanceEditor == null) return;
            
            switch (presetType.ToLower())
            {
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
                case "blue_eyes":
                    AppearanceEditor.ApplyBlueEyePreset();
                    break;
                case "brown_eyes":
                    AppearanceEditor.ApplyBrownEyePreset();
                    break;
                case "green_eyes":
                    AppearanceEditor.ApplyGreenEyePreset();
                    break;
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
        
        private void OnPresetLoadedHandler(string presetName)
        {
            RefreshAllPanels();
            Debug.Log($"预设 '{presetName}' 已加载");
        }
        
        private void RefreshSkinPanel()
        {
            if (AppearanceEditor?.SkinController == null) return;
            
        }
        
        private void RefreshEyePanel()
        {
            if (AppearanceEditor?.EyeController == null) return;
            
        }
        
        private void RefreshHairPanel()
        {
            if (AppearanceEditor?.HairController == null) return;
            
        }
        
        public void RefreshAllPanels()
        {
            RefreshSkinPanel();
            RefreshEyePanel();
            RefreshHairPanel();
        }
        
        public void SetTargetCharacter(Actor character)
        {
            if (character != null)
            {
                PreviewController?.LoadPreviewCharacter(character.Name);
            }
            RefreshAllPanels();
        }
        
        public void ResetToDefault()
        {
            AppearanceEditor?.ResetToDefault();
            RefreshAllPanels();
        }
        
        public void Close()
        {
            OnEditorClosed?.Invoke();
            Dispose();
        }
    }
}
