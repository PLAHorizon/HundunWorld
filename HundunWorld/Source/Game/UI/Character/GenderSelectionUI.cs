using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.Components;
using HundunWorld.Game.UI.StyleSystem;
using System;
using System.Collections.Generic;

namespace HundunWorld.Game.UI.Character
{
    /// <summary>
    /// 性别选择界面 - 角色创建流程第1步
    /// 左侧性别选择器（男/女），右侧体型参数调整
    /// </summary>
    public class GenderSelectionUI : ContainerControl
    {
        /// <summary>
        /// 体型参数滑块内部数据结构
        /// </summary>
        private class BodySlider
        {
            public Panel Panel;
            public Panel Track;
            public Panel Fill;
            public Panel Handle;
            public Label ValueLabel;
            public float Value = 0.5f;
            public string Name;
            public string Format;
        }
        #region Properties
        /// <summary>
        /// 当前选择的性别: 0=男, 1=女
        /// </summary>
        public int SelectedGender { get; private set; }

        /// <summary>
        /// 点击下一步时触发
        /// </summary>
        public event Action OnNextStep;
        /// <summary>
        /// 体型参数: 身高 0.0~1.0
        /// </summary>
        public float BodyHeight => _sliderHeight?.Value ?? 0.5f;

        /// <summary>
        /// 体型参数: 体型 0.0~1.0
        /// </summary>
        public float BodyType => _sliderBody?.Value ?? 0.5f;

        /// <summary>
        /// 体型参数: 头部比例 0.0~1.0
        /// </summary>
        public float HeadSize => _sliderHead?.Value ?? 0.5f;

        /// <summary>
        /// 体型参数变化时触发
        /// </summary>
        public event Action<float, float, float> OnBodyParamsChanged;

        #endregion

        #region UI Components
        private ContainerControl _genderSelector;
        private Panel _selectorBackdrop; // 左侧竖向半透明背景带
        private Label _titleLabel;
        private Panel _maleGlowPanel; // 男性选中光晕
        private Label _maleLabel;
        private Panel _maleIndicator;
        private Panel _maleBrushLine;
        private Panel _separatorLine;
        private Panel _femaleGlowPanel; // 女性选中光晕
        private Label _femaleLabel;
        private Panel _femaleIndicator;
        private Panel _femaleBrushLine;
        private NextStepButton _nextStepButton;
        // 角色 ID 标签由 CharacterSceneController 全局维护(_globalIdLabel + _globalIdLabelShadow),
        // 样式: 金色 RGB(212,175,55) 16pt + 50% 透明黑色阴影层(偏移 1px)。
        // 不在 GenderSelectionUI 中重复创建，避免与全局 ID 标签重复显示。
        private bool _uiCreated = false;
        private bool _skipButtonCreation = false;

        // 墨水笔触动画状态
        private float _inkAnimTime = 0f;
        private bool _isInkAnimating = false;
        private float _inkAnimDuration = 0.3f;
        private Float2 _separatorOriginalSize;
        private Color _separatorOriginalColor;
        private bool _separatorOriginalCaptured = false;

        // 关联的预览面板:性别切换时同步相机距离过渡
        private CharacterPreviewPanel _previewPanel;

        // NextStepButton 挂载的根容器（GUI 根容器，而非 GenderSelectionUI 自身）
        // 解决 Z-order 问题：按钮在根容器层，不会被 CharacterPreviewPanel 遮挡
        private ContainerControl _rootGui;

        // 右侧体型参数调整面板
        private Panel _paramPanelBackdrop;
        private BodySlider _sliderHeight;
        private BodySlider _sliderBody;
        private BodySlider _sliderHead;
        private BodySlider _activeSlider;
        private BodySlider _hoveredSlider;
        private Float2 _sliderDragStart;
        #endregion

        #region Constructor
        public GenderSelectionUI()
        {
            AnchorPreset = AnchorPresets.StretchAll;
            Offsets = Margin.Zero;
            BackgroundColor = Color.Transparent;
            SelectedGender = 0;
            // 延迟创建UI，确保父控件有正确的尺寸
        }

        /// <summary>
        /// 注入预览面板,性别切换时会调用其 TransitionToNewModel 触发相机距离过渡(0.4s)。
        /// </summary>
        public void SetPreviewPanel(CharacterPreviewPanel previewPanel)
        {
            _previewPanel = previewPanel;
        }

        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);

            if (!_uiCreated && Parent != null && Parent.Width > 0 && Parent.Height > 0)
            {
                _uiCreated = true;
                CreateUI();
            }

            UpdateInkAnimation(deltaTime);
        }

        private void UpdateInkAnimation(float deltaTime)
        {
            if (!_isInkAnimating || _separatorLine == null) return;

            _inkAnimTime += deltaTime;
            if (_inkAnimTime >= _inkAnimDuration)
            {
                _inkAnimTime = _inkAnimDuration;
                _isInkAnimating = false;
            }

            float t = _inkAnimTime / _inkAnimDuration;
            if (t < 0f) t = 0f;
            if (t > 1f) t = 1f;

            // 宽度由 0 渐变到原始宽度(墨水笔触展开)
            _separatorLine.Width = _separatorOriginalSize.X * t;
            // 颜色 alpha 由 0 渐变到 1(墨水由淡变浓)
            float alpha = _separatorOriginalColor.A * t;
            _separatorLine.BackgroundColor = new Color(
                _separatorOriginalColor.R,
                _separatorOriginalColor.G,
                _separatorOriginalColor.B,
                alpha);
        }
        #endregion

        #region UI Creation
        private void CreateUI()
        {
            // 缓存根 GUI 容器引用（Parent 是 CharacterSceneController 的 gui 容器）
            _rootGui = Parent as ContainerControl;

            CreateGenderSelector();
            if (!_skipButtonCreation)
                CreateNextStepButton();
            CreateBodyParamPanel();
        }

        private void CreateGenderSelector()
        {
            // 性别选择器容器 — 横向布局，位于屏幕左中偏上区域
            float screenH = Parent?.Height ?? 1080f;
            _genderSelector = new ContainerControl
            {
                Parent = this,
                AnchorPreset = AnchorPresets.TopLeft,
                Offsets = new Margin(0, 0, 0, 0),
                Location = new Float2(80, screenH * 0.35f - 40),
                Size = new Float2(520, 140),
                BackgroundColor = Color.Transparent
            };

            // === 男性区域 ===
            // 男性标签（默认选中，金色，56px超大字）
            _maleLabel = new Label
            {
                Text = "男",
                Size = new Float2(110, 75),
                Location = new Float2(0, 0),
                Font = UIHelper.SetFont(size: 56),
                TextColor = ChineseClassicalTheme.SecondaryColor,
                HorizontalAlignment = TextAlignment.Center
            };
            _genderSelector.AddChild(_maleLabel);

            // 男性金色水墨笔触下划线 — 更长更薄，模拟有机流动
            _maleBrushLine = new Panel
            {
                Size = new Float2(260, 5),
                Location = new Float2(-30, 68),
                BackgroundColor = ChineseClassicalTheme.SecondaryColor
            };
            _genderSelector.AddChild(_maleBrushLine);

            // 男性选中光晕背景（笔触周围的柔和光晕，更大范围）
            _maleGlowPanel = new Panel
            {
                Size = new Float2(300, 25),
                Location = new Float2(-40, 62),
                BackgroundColor = ChineseClassicalTheme.SecondaryColorWithAlpha(0.08f)
            };
            _genderSelector.AddChild(_maleGlowPanel);

            // 男性选中指示器（小竖条，位于"男"字右侧）
            _maleIndicator = new Panel
            {
                Size = new Float2(3, 18),
                Location = new Float2(105, 22),
                BackgroundColor = ChineseClassicalTheme.SecondaryColor
            };
            _genderSelector.AddChild(_maleIndicator);

            // === 分隔线（极短竖线，连接男女） ===
            _separatorLine = new Panel
            {
                Size = new Float2(2, 30),
                Location = new Float2(240, 35),
                BackgroundColor = ChineseClassicalTheme.SecondaryColorWithAlpha(0.5f)
            };
            _genderSelector.AddChild(_separatorLine);
            _separatorOriginalSize = _separatorLine.Size;
            _separatorOriginalColor = _separatorLine.BackgroundColor;
            _separatorOriginalCaptured = true;

            // === 女性区域 ===
            // 女性标签（未选中，暗色，30px小字，偏右下）
            _femaleLabel = new Label
            {
                Text = "女",
                Size = new Float2(70, 45),
                Location = new Float2(260, 35),
                Font = UIHelper.SetFont(size: 30),
                TextColor = new Color(1, 1, 1, 0.30f),
                HorizontalAlignment = TextAlignment.Center
            };
            _genderSelector.AddChild(_femaleLabel);

            // 女性水墨笔触下划线（未选中时透明，更短更细）
            _femaleBrushLine = new Panel
            {
                Size = new Float2(100, 3),
                Location = new Float2(245, 80),
                BackgroundColor = ChineseClassicalTheme.SecondaryColorWithAlpha(0f)
            };
            _genderSelector.AddChild(_femaleBrushLine);

            // 女性选中光晕背景（未选中时透明）
            _femaleGlowPanel = new Panel
            {
                Size = new Float2(140, 20),
                Location = new Float2(235, 74),
                BackgroundColor = ChineseClassicalTheme.SecondaryColorWithAlpha(0f)
            };
            _genderSelector.AddChild(_femaleGlowPanel);

            // 女性选中指示器（未选中，极暗）
            _femaleIndicator = new Panel
            {
                Size = new Float2(3, 18),
                Location = new Float2(330, 45),
                BackgroundColor = new Color(1, 1, 1, 0.10f)
            };
            _genderSelector.AddChild(_femaleIndicator);
        }

        private void CreateNextStepButton()
        {
            // ★ 关键修复：将 NextStepButton 挂载到根 GUI 容器，而非 GenderSelectionUI 自身
            // 这样按钮和 CharacterPreviewPanel 是同级控件，Z-order 不会被预览面板遮挡
            var rootContainer = _rootGui ?? (Parent as ContainerControl);
            _nextStepButton = new NextStepButton
            {
                Parent = rootContainer
            };
            // 确保按钮在最顶层
            if (rootContainer != null)
            {
                _nextStepButton.IndexInParent = rootContainer.ChildrenCount - 1;
            }
            _nextStepButton.OnClicked += () => OnNextStep?.Invoke();
            Debug.Log($"[GenderSelectionUI] NextStepButton 挂载到根容器, Z-Order={_nextStepButton.IndexInParent}, Parent={rootContainer?.GetType().Name}");
        }

        // CreateIdLabel 已移除:角色 ID 标签由 CharacterSceneController._globalIdLabel 全局维护,
        // 样式: 金色 RGB(212,175,55) 16pt + 50% 透明黑色阴影层(_globalIdLabelShadow, 偏移 1px)。
        // GenderSelectionUI 不再创建重复标签。

        /// <summary>
        /// 创建右侧体型参数调整面板：身高/体型/头部比例 3个金色滑块
        /// </summary>
        private void CreateBodyParamPanel()
        {
            float W = Width > 0 ? Width : (Parent?.Width ?? 1920);
            float H = Height > 0 ? Height : (Parent?.Height ?? 1080);

            // ★ 右侧半透明深色背景带 - 挂载到 this (GenderSelectionUI)，确保鼠标事件正确路由
            _paramPanelBackdrop = new Panel
            {
                Parent = this,
                Location = new Float2(W - 300, 120),
                Size = new Float2(260, 320),
                BackgroundColor = new Color(0.02f, 0.02f, 0.04f, 0.40f)
            };

            // 面板标题
            var panelTitle = new Label
            {
                Parent = _paramPanelBackdrop,
                Text = "体型调整",
                Location = new Float2(0, 12),
                Size = new Float2(260, 28),
                Font = UIHelper.SetFont(size: 18),
                TextColor = new Color(1, 1, 1, 0.6f),
                HorizontalAlignment = TextAlignment.Center
            };

            // 金色装饰线
            var titleLine = new Panel
            {
                Parent = _paramPanelBackdrop,
                Location = new Float2(30, 42),
                Size = new Float2(200, 2),
                BackgroundColor = ChineseClassicalTheme.SecondaryColorWithAlpha(0.5f)
            };

            // 三个体型参数滑块（初始值为男性默认）
            _sliderHeight = CreateBodySlider(_paramPanelBackdrop, "身高", 60, 0.55f, "中等");
            _sliderBody   = CreateBodySlider(_paramPanelBackdrop, "体型", 140, 0.55f, "中等");
            _sliderHead   = CreateBodySlider(_paramPanelBackdrop, "头部比例", 220, 0.50f, "标准");

            Debug.Log($"[GenderSelectionUI] 体型参数面板创建完成, 位置=({W - 300},{120})");
        }

        /// <summary>
        /// 创建单个体型参数滑块: 标签 + 滑轨 + 填充 + 手柄 + 数值
        /// </summary>
        private BodySlider CreateBodySlider(Panel parent, string name, float y, float defaultValue, string defaultFormat)
        {
            var slider = new BodySlider { Value = defaultValue, Format = defaultFormat, Name = name };
            float trackW = 200f;
            float trackH = 4f;

            slider.Panel = new Panel
            {
                Parent = parent,
                Location = new Float2(0, y),
                Size = new Float2(260, 60),
                BackgroundColor = Color.Transparent
            };

            // 参数名称标签
            new Label
            {
                Parent = slider.Panel,
                Text = name,
                Location = new Float2(25, 0),
                Size = new Float2(120, 22),
                Font = UIHelper.SetFont(size: 14),
                TextColor = new Color(1, 1, 1, 0.75f),
                HorizontalAlignment = TextAlignment.Near
            };

            // 数值标签（右侧）
            slider.ValueLabel = new Label
            {
                Parent = slider.Panel,
                Text = defaultFormat,
                Location = new Float2(140, 0),
                Size = new Float2(90, 22),
                Font = UIHelper.SetFont(size: 12),
                TextColor = ChineseClassicalTheme.SecondaryColor,
                HorizontalAlignment = TextAlignment.Far
            };

            // 滑轨背景
            slider.Track = new Panel
            {
                Parent = slider.Panel,
                Location = new Float2(30, 28),
                Size = new Float2(trackW, trackH),
                BackgroundColor = new Color(0.25f, 0.25f, 0.28f, 0.8f)
            };

            // 滑轨填充（金色）
            slider.Fill = new Panel
            {
                Parent = slider.Panel,
                Location = new Float2(30, 28),
                Size = new Float2(trackW * defaultValue, trackH),
                BackgroundColor = ChineseClassicalTheme.SecondaryColorWithAlpha(0.9f)
            };

            // 滑动手柄（金色圆形效果）
            slider.Handle = new Panel
            {
                Parent = slider.Panel,
                Location = new Float2(30 + trackW * defaultValue - 7, 22),
                Size = new Float2(14, 14),
                BackgroundColor = new Color(225f / 255f, 185f / 255f, 75f / 255f, 1f)
            };

            return slider;
        }

        /// <summary>
        /// 根据滑块值生成文本标签（身高/体型/头部比例 分别有语义化文本）
        /// </summary>
        private static string FormatSliderValue(string sliderName, float value)
        {
            if (sliderName == "身高")
            {
                if (value < 0.25f) return "娇小";
                if (value < 0.45f) return "纤细";
                if (value < 0.65f) return "中等";
                if (value < 0.85f) return "高挑";
                return "修长";
            }
            if (sliderName == "体型")
            {
                if (value < 0.25f) return "纤细";
                if (value < 0.45f) return "清瘦";
                if (value < 0.65f) return "标准";
                if (value < 0.85f) return "健壮";
                return "魁梧";
            }
            // 头部比例
            {
                if (value < 0.25f) return "较小";
                if (value < 0.45f) return "偏小";
                if (value < 0.65f) return "标准";
                if (value < 0.85f) return "偏大";
                return "较大";
            }
        }

        /// <summary>
        /// 更新滑块的填充和手柄位置
        /// </summary>
        private void UpdateSliderVisuals(BodySlider slider)
        {
            if (slider?.Fill == null) return;
            float trackW = 200f;
            slider.Fill.Width = trackW * slider.Value;
            slider.Handle.Location = new Float2(30 + trackW * slider.Value - 7, 22);

            if (slider.ValueLabel != null)
                slider.ValueLabel.Text = FormatSliderValue(slider.Name, slider.Value);
        }

        /// <summary>
        /// 公开方法: 设置指定滑块的数值并刷新视觉
        /// </summary>
        public void SetSliderValue(string paramName, float value)
        {
            BodySlider target = null;
            if (paramName == "身高" || paramName == "BodyHeight") target = _sliderHeight;
            else if (paramName == "体型" || paramName == "BodyType") target = _sliderBody;
            else if (paramName == "头部比例" || paramName == "HeadSize") target = _sliderHead;

            if (target != null)
            {
                target.Value = Mathf.Clamp(value, 0f, 1f);
                UpdateSliderVisuals(target);
            }
        }

        /// <summary>
        /// 重置所有滑块到性别默认值
        /// </summary>
        public void ResetSlidersToGenderDefaults(int gender)
        {
            if (gender == 0) // 男性
            {
                SetSliderValue("BodyHeight", 0.55f);
                SetSliderValue("BodyType", 0.55f);
                SetSliderValue("HeadSize", 0.50f);
            }
            else // 女性
            {
                SetSliderValue("BodyHeight", 0.45f);
                SetSliderValue("BodyType", 0.38f);
                SetSliderValue("HeadSize", 0.48f);
            }
            OnBodyParamsChanged?.Invoke(BodyHeight, BodyType, HeadSize);
        }

        /// <summary>
        /// 根据鼠标位置计算滑块数值（使用GenderSelectionUI本地坐标）
        /// </summary>
        private void UpdateSliderFromMouseLocal(BodySlider slider, Float2 localPos)
        {
            if (slider?.Track == null || _paramPanelBackdrop == null) return;
            float trackScreenX = _paramPanelBackdrop.Location.X + slider.Panel.Location.X + slider.Track.Location.X;
            float trackW = slider.Track.Width;
            float x = localPos.X - trackScreenX;
            slider.Value = Mathf.Clamp(x / trackW, 0f, 1f);
            UpdateSliderVisuals(slider);
            OnBodyParamsChanged?.Invoke(BodyHeight, BodyType, HeadSize);
        }

        /// <summary>
        /// 检测鼠标是否点击了某个滑块的手柄或轨道区域（使用GenderSelectionUI本地坐标）
        /// </summary>
        private BodySlider HitTestSlider(Float2 localPos)
        {
            if (_paramPanelBackdrop == null) return null;
            BodySlider[] sliders = { _sliderHeight, _sliderBody, _sliderHead };
            foreach (var s in sliders)
            {
                if (s?.Panel == null) continue;
                float panelX = _paramPanelBackdrop.Location.X + s.Panel.Location.X;
                float panelY = _paramPanelBackdrop.Location.Y + s.Panel.Location.Y;
                // 检测手柄和轨道区域 (y: 20~42, x: 20~240)
                if (localPos.X >= panelX + 20 && localPos.X <= panelX + 240 &&
                    localPos.Y >= panelY + 20 && localPos.Y <= panelY + 42)
                    return s;
            }
            return null;
        }
        #endregion

        #region Mouse Input
        /// <summary>
        /// 检测性别选择器区域内的点击，切换男/女性别
        /// 男性区域: X 0-200, Y 0-100, 女性区域: X 220-380, Y 20-100
        /// </summary>
        public override bool OnMouseDown(Float2 location, MouseButton button)
        {
            // 优先检测滑块拖拽（location 已是 GenderSelectionUI 本地坐标）
            if (button == MouseButton.Left)
            {
                var hit = HitTestSlider(location);
                if (hit != null)
                {
                    _activeSlider = hit;
                    _sliderDragStart = location;
                    UpdateSliderFromMouseLocal(hit, location);
                    return true;
                }
            }

            // 性别选择器点击（横向布局）
            if (_genderSelector != null && button == MouseButton.Left)
            {
                Float2 localPos = location - _genderSelector.Location;
                // 男性区域：左侧
                if (localPos.X >= 0 && localPos.X <= 220 && localPos.Y >= 0 && localPos.Y <= 90)
                {
                    SwitchGender(0);
                    return true;
                }
                // 女性区域：右侧
                if (localPos.X >= 240 && localPos.X <= 380 && localPos.Y >= 20 && localPos.Y <= 90)
                {
                    SwitchGender(1);
                    return true;
                }
            }
            return base.OnMouseDown(location, button);
        }

        /// <summary>
        /// 滑块拖拽过程中更新数值 + 手柄hover放大效果
        /// </summary>
        public override void OnMouseMove(Float2 location)
        {
            base.OnMouseMove(location);
            if (_activeSlider != null)
            {
                UpdateSliderFromMouseLocal(_activeSlider, location);
            }
            else
            {
                // 手柄 hover 放大: 14px -> 18px
                var hovered = HitTestSlider(location);
                if (hovered != _hoveredSlider)
                {
                    if (_hoveredSlider?.Handle != null)
                        _hoveredSlider.Handle.Size = new Float2(14, 14);
                    _hoveredSlider = hovered;
                    if (_hoveredSlider?.Handle != null)
                        _hoveredSlider.Handle.Size = new Float2(18, 18);
                }
            }
        }

        /// <summary>
        /// 鼠标释放时结束滑块拖拽
        /// </summary>
        public override bool OnMouseUp(Float2 location, MouseButton button)
        {
            if (_activeSlider != null && button == MouseButton.Left)
            {
                _activeSlider = null;
                return true;
            }
            return base.OnMouseUp(location, button);
        }

        /// <summary>
        /// 切换性别并同步通知预览面板触发相机过渡
        /// </summary>
        private void SwitchGender(int gender)
        {
            if (SelectedGender == gender)
                return;

            SelectedGender = gender;
            UpdateGenderVisuals();
            TriggerInkAnimation();

            // 重置体型参数到性别默认值
            ResetSlidersToGenderDefaults(gender);

            // 通知预览面板:启动相机距离过渡
            if (_previewPanel != null)
            {
                _previewPanel.TransitionToNewModel(null, 0.4f);
            }
        }
        #endregion

        #region Visual Update
        /// <summary>
        /// 更新性别选择器视觉效果（横向布局）
        /// 选中项: 金色大字 + 金色笔触 + 光晕
        /// 未选中项: 暗色小字 + 透明笔触
        /// </summary>
        private void UpdateGenderVisuals()
        {
            if (_maleLabel == null || _femaleLabel == null)
                return;

            Color gold = ChineseClassicalTheme.SecondaryColor;
            Color dimmedText = new Color(1, 1, 1, 0.35f);
            Color dimmedIndicator = new Color(1, 1, 1, 0.15f);
            Color goldBrush = ChineseClassicalTheme.SecondaryColor;
            Color dimmedBrush = ChineseClassicalTheme.SecondaryColorWithAlpha(0f);
            Color goldGlow = ChineseClassicalTheme.SecondaryColorWithAlpha(0.10f);
            Color noGlow = ChineseClassicalTheme.SecondaryColorWithAlpha(0f);

            if (SelectedGender == 0)
            {
                // 男性选中
                _maleLabel.TextColor = gold;
                _maleLabel.Font = UIHelper.SetFont(size: 56);
                _maleIndicator.BackgroundColor = gold;
                _maleBrushLine.BackgroundColor = goldBrush;
                _maleBrushLine.Size = new Float2(260, 5);
                _maleGlowPanel.BackgroundColor = goldGlow;
                // 女性未选中
                _femaleLabel.TextColor = dimmedText;
                _femaleLabel.Font = UIHelper.SetFont(size: 30);
                _femaleIndicator.BackgroundColor = dimmedIndicator;
                _femaleBrushLine.BackgroundColor = dimmedBrush;
                _femaleBrushLine.Size = new Float2(100, 3);
                _femaleGlowPanel.BackgroundColor = noGlow;
            }
            else
            {
                // 男性未选中
                _maleLabel.TextColor = dimmedText;
                _maleLabel.Font = UIHelper.SetFont(size: 30);
                _maleIndicator.BackgroundColor = dimmedIndicator;
                _maleBrushLine.BackgroundColor = dimmedBrush;
                _maleBrushLine.Size = new Float2(100, 3);
                _maleGlowPanel.BackgroundColor = noGlow;
                // 女性选中
                _femaleLabel.TextColor = gold;
                _femaleLabel.Font = UIHelper.SetFont(size: 56);
                _femaleIndicator.BackgroundColor = gold;
                _femaleBrushLine.BackgroundColor = goldBrush;
                _femaleBrushLine.Size = new Float2(260, 5);
                _femaleGlowPanel.BackgroundColor = goldGlow;
            }
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// 触发分隔线墨水笔触动画(性别切换时由 UI 框架调用)
        /// </summary>
        public void TriggerInkAnimation()
        {
            if (_separatorLine == null || !_separatorOriginalCaptured) return;
            _isInkAnimating = true;
            _inkAnimTime = 0f;
            // 立即重置为目标线段(动画从 0 宽度开始)
            _separatorLine.Width = 0f;
            _separatorLine.BackgroundColor = new Color(
                _separatorOriginalColor.R,
                _separatorOriginalColor.G,
                _separatorOriginalColor.B,
                0f);
        }

        /// <summary>
        /// 显示性别选择界面，重置为默认男性选中
        /// </summary>
        public void Show()
        {
            Visible = true;
            SelectedGender = 0;
            UpdateGenderVisuals();
            // 同步显示根容器层的 NextStepButton
            if (_nextStepButton != null) _nextStepButton.Visible = true;
        }

        /// <summary>
        /// 隐藏内部 NextStepButton（由控制器级按钮替代）
        /// 在 CreateUI 之前调用则记录标志，之后调用则立即移除
        /// </summary>
        public void HideExternalButton()
        {
            if (_nextStepButton != null)
                _nextStepButton.Parent = null;
            _skipButtonCreation = true;
        }

        /// <summary>
        /// 隐藏性别选择界面
        /// </summary>
        public void Hide()
        {
            Visible = false;
            // 同步隐藏根容器层的 NextStepButton
            if (_nextStepButton != null) _nextStepButton.Visible = false;
        }
        #endregion
    }
}