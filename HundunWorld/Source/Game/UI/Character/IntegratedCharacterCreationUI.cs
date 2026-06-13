using System;
using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.UI.MetaHuman;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using static HundunWorld.Game.UI.UIHelper;
using HundunWorld.Game.UI.Components;

namespace HundunWorld.Game.UI.Character
{
    /// <summary>
    /// 集成的角色创建界面 - 燕云十六声风格（步骤3：精细捏脸）
    /// 3D角色全屏背景 + 左侧分类导航/子分类/参数滑块 + 底部操作栏
    /// </summary>
    public class IntegratedCharacterCreationUI : ContainerControl
    {
        #region 事件
        public event Action<CharacterInfo> OnCharacterCreated;
        public event Action OnCancelled;
        public event Action OnCompleteStep;
        #endregion

        #region UI组件
        private CategorySidebar _categorySidebar;
        private SubCategoryPanel _subCategoryPanel;
        private ParameterSliderGroup _parameterPanel;
        private BottomActionBar _bottomBar;

        // 左侧面板容器（分类+子分类+参数），半透明背景
        private Panel _leftPanelContainer;

        // 3D角色预览
        private CharacterPreviewPanel _characterPreview;
        private bool _layoutInitialized = false;

        public bool IsInitialized => _layoutInitialized;

        // 相机复位按钮
        private Button _resetCameraButton;

        // 加载指示器
        private LoadingIndicator _loadingIndicator;

        // 下一步按钮
        private NextStepButton _nextStepButton;
        #endregion

        #region 数据
        private AppearanceInfo _currentAppearance;

        // 捏脸分类数据
        private static readonly string[] MainCategories = { "捏脸", "妆容", "发型", "体型" };
        private static readonly string[][] SubCategories = {
            new[] { "脸型", "额头", "颧骨", "下巴", "鼻子", "嘴唇" },
            new[] { "眉妆", "眼妆", "腮红", "唇彩" },
            new[] { "发型", "发色", "刘海" },
            new[] { "身高", "体型", "肩宽", "腰围" }
        };
        private static readonly string[][][] ParameterDefs = {
            new[] {
                new[] { "宽度", "长度", "饱满度" },
                new[] { "高度", "前后", "宽度" },
                new[] { "高度", "宽度", "突出度" },
                new[] { "长度", "宽度", "角度" },
                new[] { "高度", "宽度", "长度", "鼻翼" },
                new[] { "厚度", "宽度", "角度" }
            },
            new[] {
                new[] { "粗细", "弧度", "间距" },
                new[] { "大小", "角度", "间距" },
                new[] { "范围", "浓度", "位置" },
                new[] { "厚度", "宽度", "颜色" }
            },
            new[] {
                new[] { "长度", "蓬松度", "卷曲度" },
                new[] { "色相", "饱和度", "明度" },
                new[] { "长度", "角度", "密度" }
            },
            new[] {
                new[] { "高度" },
                new[] { "胖瘦", "肌肉", "柔韧" },
                new[] { "宽度", "角度" },
                new[] { "粗细", "曲线" }
            }
        };

        // 子分类对应的相机聚焦部位
        private static readonly string[][] SubCategoryBodyParts = {
            new[] { "脸部", "头部", "头部", "脸部", "脸部", "脸部" },
            new[] { "头部", "头部", "脸部", "脸部" },
            new[] { "发型", "发型", "发型" },
            new[] { "全身", "全身", "上半身", "全身" }
        };
        #endregion

        #region 初始化
        public IntegratedCharacterCreationUI(CharacterPreviewPanel sharedPreviewPanel = null)
        {
            AnchorPreset = AnchorPresets.StretchAll;
            Offsets = Margin.Zero;
            BackgroundColor = Color.Transparent;

            _currentAppearance = new AppearanceInfo();

            if (sharedPreviewPanel != null)
            {
                _characterPreview = sharedPreviewPanel;
            }
        }

        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);

            if (!_layoutInitialized && Width > 0 && Height > 0)
            {
                _layoutInitialized = true;
                CreateUI();
                InitializeAppearanceDefaults();
            }
        }

        public void ForceInitialize()
        {
            if (!_layoutInitialized)
            {
                _layoutInitialized = true;
                CreateUI();
                InitializeAppearanceDefaults();
                PerformLayout();
            }
        }

        public void CreateUIImmediate()
        {
            _layoutInitialized = true;
            CreateUI();
            InitializeAppearanceDefaults();
            PerformLayout();
        }

        private void CreateUI()
        {
            try { CreateLeftPanel(); }
            catch (Exception ex) { Debug.LogError($"[IntegratedCharacterCreationUI] 创建左侧面板失败: {ex.Message}"); }

            try { CreateBottomBar(); }
            catch (Exception ex) { Debug.LogError($"[IntegratedCharacterCreationUI] 创建底部操作栏失败: {ex.Message}"); }

            try { CreateLoadingIndicator(); }
            catch (Exception ex) { Debug.LogError($"[IntegratedCharacterCreationUI] 创建加载指示器失败: {ex.Message}"); }

            try { CreateResetCameraButton(); }
            catch (Exception ex) { Debug.LogError($"[IntegratedCharacterCreationUI] 创建相机复位按钮失败: {ex.Message}"); }

            try
            {
                _nextStepButton = new NextStepButton { Parent = this };
                _nextStepButton.OnClicked += OnNextStepClicked;
            }
            catch (Exception ex) { Debug.LogError($"[IntegratedCharacterCreationUI] 创建下一步按钮失败: {ex.Message}"); }
        }

        private void CreateLeftPanel()
        {
            // 左侧面板容器 — 更紧凑的布局，参考图片中左侧面板更窄
            _leftPanelContainer = new Panel
            {
                Parent = this,
                AnchorPreset = AnchorPresets.VerticalStretchLeft,
                Offsets = new Margin(16, 100, 16, 70),
                Width = 400,
                BackgroundColor = new Color(0.03f, 0.03f, 0.05f, 0.72f)
            };

            _categorySidebar = new CategorySidebar
            {
                Parent = _leftPanelContainer,
                AnchorPreset = AnchorPresets.VerticalStretchLeft,
                Offsets = new Margin(8, 0, 8, 8),
                Width = 110
            };
            _categorySidebar.SetCategories(MainCategories);
            _categorySidebar.OnCategoryChanged += OnMainCategoryChanged;

            _subCategoryPanel = new SubCategoryPanel
            {
                Parent = _leftPanelContainer,
                AnchorPreset = AnchorPresets.VerticalStretchLeft,
                Offsets = new Margin(126, 0, 8, 8),
                Width = 130
            };
            _subCategoryPanel.OnSubCategoryChanged += OnSubCategoryChanged;
            _subCategoryPanel.SetSubCategories(SubCategories[0]);

            _parameterPanel = new ParameterSliderGroup
            {
                Parent = _leftPanelContainer,
                AnchorPreset = AnchorPresets.VerticalStretchRight,
                Offsets = new Margin(264, 0, 8, 8)
            };
            _parameterPanel.OnParameterChanged += OnParameterChanged;
            UpdateParameterPanel(0, 0);
        }

        private void CreateBottomBar()
        {
            _bottomBar = new BottomActionBar
            {
                Parent = this,
                AnchorPreset = AnchorPresets.HorizontalStretchBottom
            };
            _bottomBar.SetButtons(
                new string[] { "返回", "随机" },
                new BottomActionBar.ButtonStyle[] {
                    BottomActionBar.ButtonStyle.Ghost,
                    BottomActionBar.ButtonStyle.Default
                }
            );
            _bottomBar.OnButtonClicked += OnBottomBarButtonClicked;
        }

        private void OnBottomBarButtonClicked(string buttonName)
        {
            switch (buttonName)
            {
                case "返回":
                    OnCancelClicked();
                    break;
                case "随机":
                    OnRandomClicked();
                    break;
            }
        }

        private void CreateLoadingIndicator()
        {
            _loadingIndicator = UIHelper.CreateLoadingIndicator();
            _loadingIndicator.Parent = this;
            _loadingIndicator.Visible = false;
        }

        private void CreateResetCameraButton()
        {
            _resetCameraButton = new Button
            {
                Parent = this,
                Text = "\u21BA",
                TextColor = Color.White,
                Font = UIHelper.SetFont(size: 20),
                BackgroundColor = new Color(0.05f, 0.05f, 0.08f, 0.75f),
                BorderColor = new Color(212f / 255f, 175f / 255f, 55f / 255f, 0.5f),
                BorderThickness = 1.5f,
                AnchorPreset = AnchorPresets.BottomRight,
                Offsets = new Margin(0, 295, 0, 85),
                Size = new Float2(46, 46),
                TooltipText = "复位相机视角"
            };
            _resetCameraButton.Clicked += OnResetCameraClicked;
        }

        private void OnResetCameraClicked()
        {
            _characterPreview?.ResetView();
        }

        private void InitializeAppearanceDefaults()
        {
            _currentAppearance.HairModel = 0;
            _currentAppearance.HairStyle = 0;
            _currentAppearance.HairColor = 0;
            _currentAppearance.FaceModel = 0;
            _currentAppearance.SkinColor = 0;
        }
        #endregion

        #region 分类切换逻辑 + 相机联动
        private void OnMainCategoryChanged(int index, string name)
        {
            if (index >= 0 && index < SubCategories.Length)
            {
                _subCategoryPanel.SetSubCategories(SubCategories[index]);
                UpdateParameterPanel(index, 0);
            }

            _characterPreview?.FocusOnCategory(name);
        }

        private void OnSubCategoryChanged(int subIndex, string subName)
        {
            int mainIndex = _categorySidebar.SelectedIndex;
            UpdateParameterPanel(mainIndex, subIndex);

            if (_characterPreview != null && mainIndex >= 0 && mainIndex < SubCategoryBodyParts.Length)
            {
                var bodyParts = SubCategoryBodyParts[mainIndex];
                if (subIndex >= 0 && subIndex < bodyParts.Length)
                {
                    _characterPreview.FocusOnBodyPart(bodyParts[subIndex]);
                }
            }
        }

        private void UpdateParameterPanel(int mainIndex, int subIndex)
        {
            _parameterPanel.ClearAll();

            if (mainIndex >= 0 && mainIndex < ParameterDefs.Length &&
                subIndex >= 0 && subIndex < ParameterDefs[mainIndex].Length)
            {
                var paramNames = ParameterDefs[mainIndex][subIndex];
                _parameterPanel.AddDimension("调节", paramNames);
            }
        }

        private void OnParameterChanged(string paramName, float value)
        {
            ApplyPreviewParameter(paramName, value);
        }

        private void ApplyPreviewParameter(string paramName, float value)
        {
            var actor = _characterPreview?.CharacterActor;
            if (actor == null) return;

            float normalized = value - 0.5f;
            float modelScale = _characterPreview.ModelScale;
            var scale = new Vector3(modelScale, modelScale, modelScale);
            var position = Vector3.Zero;

            switch (paramName)
            {
                case "高度":
                    scale.Y = modelScale * (1.0f + normalized * 0.18f);
                    break;
                case "宽度":
                case "肩宽":
                    scale.X = modelScale * (1.0f + normalized * 0.18f);
                    break;
                case "长度":
                    scale.Z = modelScale * (1.0f + normalized * 0.14f);
                    break;
                case "前后":
                case "突出度":
                    position.Z = normalized * 0.08f;
                    break;
                case "角度":
                case "弧度":
                    actor.EulerAngles = new Vector3(0.0f, normalized * 8.0f, 0.0f);
                    break;
                default:
                    float subtle = 1.0f + normalized * 0.06f;
                    scale = new Vector3(modelScale * subtle, modelScale * subtle, modelScale * subtle);
                    break;
            }

            actor.Scale = scale;
            actor.Position = position;
        }
        #endregion

        #region 事件处理
        private void OnRandomClicked()
        {
            RandomizeAllParameters();
        }

        private void RandomizeAllParameters()
        {
            var random = new Random();
            int mainIndex = _categorySidebar.SelectedIndex;
            int subIndex = _subCategoryPanel.SelectedIndex;

            if (mainIndex >= 0 && mainIndex < ParameterDefs.Length &&
                subIndex >= 0 && subIndex < ParameterDefs[mainIndex].Length)
            {
                var paramNames = ParameterDefs[mainIndex][subIndex];
                foreach (var paramName in paramNames)
                {
                    float randomValue = (float)(random.NextDouble());
                    _parameterPanel.SetParameterValue(paramName, randomValue);
                }
            }
        }

        private void OnCancelClicked()
        {
            OnCancelled?.Invoke();
        }

        private void OnNextStepClicked()
        {
            OnCompleteStep?.Invoke();
        }
        #endregion

        #region 鼠标交互 - 3D预览区域
        public override bool OnMouseDown(Float2 location, MouseButton button)
        {
            if (_resetCameraButton != null && _resetCameraButton.Visible)
            {
                var btnBounds = new Rectangle(_resetCameraButton.Location, _resetCameraButton.Size);
                if (btnBounds.Contains(location))
                {
                    return base.OnMouseDown(location, button);
                }
            }

            if (_characterPreview != null && IsInPreviewArea(location))
            {
                Float2 previewLocal = location - _characterPreview.Location;
                return _characterPreview.OnMouseDown(previewLocal, button);
            }
            return base.OnMouseDown(location, button);
        }

        public override bool OnMouseUp(Float2 location, MouseButton button)
        {
            if (_characterPreview != null && IsInPreviewArea(location))
            {
                Float2 previewLocal = location - _characterPreview.Location;
                return _characterPreview.OnMouseUp(previewLocal, button);
            }
            return base.OnMouseUp(location, button);
        }

        public override void OnMouseMove(Float2 location)
        {
            base.OnMouseMove(location);

            if (_characterPreview != null)
            {
                Float2 previewLocal = location - _characterPreview.Location;
                _characterPreview.OnMouseMove(previewLocal);
            }
        }

        public override bool OnMouseWheel(Float2 location, float delta)
        {
            if (_characterPreview != null)
            {
                Float2 previewLocal = location - _characterPreview.Location;
                return _characterPreview.OnMouseWheel(previewLocal, delta);
            }
            return base.OnMouseWheel(location, delta);
        }

        private bool IsInPreviewArea(Float2 location)
        {
            if (_leftPanelContainer != null && IsPointInControl(_leftPanelContainer, location))
                return false;
            if (_bottomBar != null && IsPointInControl(_bottomBar, location))
                return false;
            return true;
        }

        private bool IsPointInControl(Control control, Float2 point)
        {
            if (control == null) return false;
            var bounds = new Rectangle(control.Location, control.Size);
            return bounds.Contains(point);
        }
        #endregion

        #region 公共方法
        public void Cleanup()
        {
            if (_categorySidebar != null) _categorySidebar.OnCategoryChanged -= OnMainCategoryChanged;
            if (_subCategoryPanel != null) _subCategoryPanel.OnSubCategoryChanged -= OnSubCategoryChanged;
            if (_bottomBar != null) _bottomBar.OnButtonClicked -= OnBottomBarButtonClicked;
            if (_parameterPanel != null) _parameterPanel.OnParameterChanged -= OnParameterChanged;
            if (_resetCameraButton != null) _resetCameraButton.Clicked -= OnResetCameraClicked;
            if (_nextStepButton != null) _nextStepButton.OnClicked -= OnNextStepClicked;
        }

        public void SetTargetScene(FlaxEngine.Scene scene)
        {
            if (_characterPreview != null)
                _characterPreview.TargetScene = scene;
        }

        public void SetPreviewPanel(CharacterPreviewPanel previewPanel)
        {
            _characterPreview = previewPanel;
        }

        public CharacterPreviewPanel GetPreviewPanel()
        {
            return _characterPreview;
        }

        public void Show()
        {
            Visible = true;
            Reset();

            _characterPreview?.FocusOnCategory("捏脸");
        }

        public void Hide()
        {
            Visible = false;
        }

        /// <summary>
        /// 隐藏内部 NextStepButton（由控制器级按钮替代）
        /// </summary>
        public void HideExternalButton()
        {
            if (_nextStepButton != null)
                _nextStepButton.Parent = null;
        }

        public void SetStepData(StepData data)
        {
            if (data == null) return;

            if (data.FaceParameters != null && data.FaceParameters.Count > 0)
            {
                foreach (var kvp in data.FaceParameters)
                {
                    _parameterPanel?.SetParameterValue(kvp.Key, kvp.Value);
                }
            }
        }

        public void Reset()
        {
            InitializeAppearanceDefaults();

            if (_categorySidebar != null)
            {
                _categorySidebar.SelectCategory(0);
            }
        }
        #endregion
    }
}
