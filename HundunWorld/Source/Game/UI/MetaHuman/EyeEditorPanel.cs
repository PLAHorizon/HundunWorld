using System;
using FlaxEngine;
using FlaxEngine.GUI;

namespace HundunWorld.UI.MetaHuman
{
    /// <summary>
    /// 眼睛材质参数编辑面板
    /// 提供虹膜、瞳孔、巩膜、角膜等参数的编辑控件
    /// </summary>
    public class EyeEditorPanel : Panel
    {
        // UI常量
        private const float RowHeight = 35f;
        private const float LabelWidth = 140f;
        private const float SliderWidth = 200f;
        private const float ColorPickerWidth = 80f;
        private const float Padding = 10f;
        private const float SectionSpacing = 15f;
        
        // 滚动容器
        private VerticalPanel _scrollContent;
        
        // 虹膜参数控件
        private ColorPicker _irisColorPicker;
        private ColorPicker _irisSecondaryColorPicker;
        private Slider _irisRoughnessSlider;
        private Slider _irisDetailSlider;
        private Slider _irisPatternIntensitySlider;
        private Slider _irisRadiusSlider;
        private Slider _limbusIntensitySlider;
        private Slider _limbusWidthSlider;
        private ColorPicker _limbusColorPicker;
        
        // 瞳孔参数控件
        private Slider _pupilSizeSlider;
        private Slider _pupilReactivitySlider;
        private ColorPicker _pupilColorPicker;
        
        // 巩膜参数控件
        private ColorPicker _scleraColorPicker;
        private Slider _scleraRoughnessSlider;
        private Slider _scleraVeinIntensitySlider;
        private ColorPicker _scleraVeinColorPicker;
        private Slider _scleraDarkeningSlider;
        
        // 角膜参数控件
        private Slider _corneaRefractionSlider;
        private Slider _corneaRoughnessSlider;
        private Slider _corneaBumpSlider;
        private Slider _corneaThicknessSlider;
        
        // 眼球整体参数
        private Slider _eyeWetnessSlider;
        private Slider _eyeOcclusionSlider;
        private Slider _parallaxDepthSlider;
        private Slider _causticsIntensitySlider;
        
        // 事件
        public event Action<Color> OnIrisColorChanged;
        public event Action<Color> OnIrisSecondaryColorChanged;
        public event Action<float> OnIrisRoughnessChanged;
        public event Action<float> OnIrisDetailChanged;
        public event Action<float> OnIrisPatternIntensityChanged;
        public event Action<float> OnIrisRadiusChanged;
        public event Action<float> OnLimbusIntensityChanged;
        public event Action<float> OnLimbusWidthChanged;
        public event Action<Color> OnLimbusColorChanged;
        public event Action<float> OnPupilSizeChanged;
        public event Action<float> OnPupilReactivityChanged;
        public event Action<Color> OnPupilColorChanged;
        public event Action<Color> OnScleraColorChanged;
        public event Action<float> OnScleraRoughnessChanged;
        public event Action<float> OnScleraVeinIntensityChanged;
        public event Action<Color> OnScleraVeinColorChanged;
        public event Action<float> OnScleraDarkeningChanged;
        public event Action<float> OnCorneaRefractionChanged;
        public event Action<float> OnCorneaRoughnessChanged;
        public event Action<float> OnCorneaBumpChanged;
        public event Action<float> OnCorneaThicknessChanged;
        public event Action<float> OnEyeWetnessChanged;
        public event Action<float> OnEyeOcclusionChanged;
        public event Action<float> OnParallaxDepthChanged;
        public event Action<float> OnCausticsIntensityChanged;
        
        // 是否正在程序化更新
        private bool _isUpdating;
        
        public EyeEditorPanel()
        {
            BackgroundColor = new Color(0.14f, 0.14f, 0.16f, 1.0f);
            // Panel doesn't have AutoScroll in Flax 1.11
        }
        
        /// <inheritdoc/>
        public override void OnParentResized()
        {
            base.OnParentResized();
            CreateUI();
        }
        
        /// <summary>
        /// 创建UI
        /// </summary>
        private void CreateUI()
        {
            DisposeChildren();
            
            _scrollContent = new VerticalPanel
            {
                Parent = this,
                AnchorPreset = AnchorPresets.HorizontalStretchTop,
                AutoSize = true,
                Spacing = 5
            };
            
            float currentY = Padding;
            
            // ===== 虹膜参数区 =====
            currentY = CreateSectionHeader("虹膜 (Iris)", currentY);
            currentY = CreateColorRow("虹膜主色", ref _irisColorPicker, new Color(0.3f, 0.5f, 0.7f),
                (color) => { if (!_isUpdating) OnIrisColorChanged?.Invoke(color); }, currentY);
            currentY = CreateColorRow("虹膜次色", ref _irisSecondaryColorPicker, new Color(0.2f, 0.35f, 0.5f),
                (color) => { if (!_isUpdating) OnIrisSecondaryColorChanged?.Invoke(color); }, currentY);
            currentY = CreateSliderRow("虹膜粗糙度", ref _irisRoughnessSlider, 0f, 1f, 0.2f,
                (value) => { if (!_isUpdating) OnIrisRoughnessChanged?.Invoke(value); }, currentY);
            currentY = CreateSliderRow("虹膜细节", ref _irisDetailSlider, 0f, 2f, 1f,
                (value) => { if (!_isUpdating) OnIrisDetailChanged?.Invoke(value); }, currentY);
            currentY = CreateSliderRow("纹理强度", ref _irisPatternIntensitySlider, 0f, 2f, 1f,
                (value) => { if (!_isUpdating) OnIrisPatternIntensityChanged?.Invoke(value); }, currentY);
            currentY = CreateSliderRow("虹膜半径", ref _irisRadiusSlider, 0.3f, 0.6f, 0.45f,
                (value) => { if (!_isUpdating) OnIrisRadiusChanged?.Invoke(value); }, currentY);
            
            currentY += SectionSpacing;
            
            // ===== 角膜缘参数区 =====
            currentY = CreateSectionHeader("角膜缘 (Limbus)", currentY);
            currentY = CreateSliderRow("角膜缘强度", ref _limbusIntensitySlider, 0f, 2f, 1f,
                (value) => { if (!_isUpdating) OnLimbusIntensityChanged?.Invoke(value); }, currentY);
            currentY = CreateSliderRow("角膜缘宽度", ref _limbusWidthSlider, 0f, 0.1f, 0.02f,
                (value) => { if (!_isUpdating) OnLimbusWidthChanged?.Invoke(value); }, currentY);
            currentY = CreateColorRow("角膜缘颜色", ref _limbusColorPicker, new Color(0.1f, 0.1f, 0.12f),
                (color) => { if (!_isUpdating) OnLimbusColorChanged?.Invoke(color); }, currentY);
            
            currentY += SectionSpacing;
            
            // ===== 瞳孔参数区 =====
            currentY = CreateSectionHeader("瞳孔 (Pupil)", currentY);
            currentY = CreateSliderRow("瞳孔大小", ref _pupilSizeSlider, 0.1f, 0.8f, 0.35f,
                (value) => { if (!_isUpdating) OnPupilSizeChanged?.Invoke(value); }, currentY);
            currentY = CreateSliderRow("瞳孔反应性", ref _pupilReactivitySlider, 0f, 1f, 0.5f,
                (value) => { if (!_isUpdating) OnPupilReactivityChanged?.Invoke(value); }, currentY);
            currentY = CreateColorRow("瞳孔颜色", ref _pupilColorPicker, new Color(0.02f, 0.02f, 0.02f),
                (color) => { if (!_isUpdating) OnPupilColorChanged?.Invoke(color); }, currentY);
            
            currentY += SectionSpacing;
            
            // ===== 巩膜参数区 =====
            currentY = CreateSectionHeader("巩膜 (Sclera)", currentY);
            currentY = CreateColorRow("巩膜颜色", ref _scleraColorPicker, new Color(1.0f, 0.98f, 0.95f),
                (color) => { if (!_isUpdating) OnScleraColorChanged?.Invoke(color); }, currentY);
            currentY = CreateSliderRow("巩膜粗糙度", ref _scleraRoughnessSlider, 0f, 1f, 0.3f,
                (value) => { if (!_isUpdating) OnScleraRoughnessChanged?.Invoke(value); }, currentY);
            currentY = CreateSliderRow("血丝强度", ref _scleraVeinIntensitySlider, 0f, 2f, 0.3f,
                (value) => { if (!_isUpdating) OnScleraVeinIntensityChanged?.Invoke(value); }, currentY);
            currentY = CreateColorRow("血丝颜色", ref _scleraVeinColorPicker, new Color(0.8f, 0.2f, 0.15f),
                (color) => { if (!_isUpdating) OnScleraVeinColorChanged?.Invoke(color); }, currentY);
            currentY = CreateSliderRow("边缘暗化", ref _scleraDarkeningSlider, 0f, 1f, 0.2f,
                (value) => { if (!_isUpdating) OnScleraDarkeningChanged?.Invoke(value); }, currentY);
            
            currentY += SectionSpacing;
            
            // ===== 角膜参数区 =====
            currentY = CreateSectionHeader("角膜 (Cornea)", currentY);
            currentY = CreateSliderRow("折射率", ref _corneaRefractionSlider, 1.0f, 1.5f, 1.376f,
                (value) => { if (!_isUpdating) OnCorneaRefractionChanged?.Invoke(value); }, currentY);
            currentY = CreateSliderRow("角膜粗糙度", ref _corneaRoughnessSlider, 0f, 0.3f, 0.02f,
                (value) => { if (!_isUpdating) OnCorneaRoughnessChanged?.Invoke(value); }, currentY);
            currentY = CreateSliderRow("表面凹凸", ref _corneaBumpSlider, 0f, 1f, 0.1f,
                (value) => { if (!_isUpdating) OnCorneaBumpChanged?.Invoke(value); }, currentY);
            currentY = CreateSliderRow("角膜厚度", ref _corneaThicknessSlider, 0f, 0.1f, 0.05f,
                (value) => { if (!_isUpdating) OnCorneaThicknessChanged?.Invoke(value); }, currentY);
            
            currentY += SectionSpacing;
            
            // ===== 眼球整体参数区 =====
            currentY = CreateSectionHeader("眼球效果", currentY);
            currentY = CreateSliderRow("湿润度", ref _eyeWetnessSlider, 0f, 1f, 0.8f,
                (value) => { if (!_isUpdating) OnEyeWetnessChanged?.Invoke(value); }, currentY);
            currentY = CreateSliderRow("遮蔽强度", ref _eyeOcclusionSlider, 0f, 1f, 0.5f,
                (value) => { if (!_isUpdating) OnEyeOcclusionChanged?.Invoke(value); }, currentY);
            currentY = CreateSliderRow("视差深度", ref _parallaxDepthSlider, 0f, 0.5f, 0.15f,
                (value) => { if (!_isUpdating) OnParallaxDepthChanged?.Invoke(value); }, currentY);
            currentY = CreateSliderRow("焦散强度", ref _causticsIntensitySlider, 0f, 1f, 0.3f,
                (value) => { if (!_isUpdating) OnCausticsIntensityChanged?.Invoke(value); }, currentY);
            
            currentY += SectionSpacing;
            
            // ===== 快速预设按钮区 =====
            currentY = CreateSectionHeader("快速预设", currentY);
            currentY = CreateQuickPresetButtons(currentY);
            
            _scrollContent.Height = currentY + Padding;
        }
        
        /// <summary>
        /// 创建区域标题
        /// </summary>
        private float CreateSectionHeader(string title, float y)
        {
            var header = new Label
            {
                Parent = _scrollContent,
                Text = title,
                X = Padding,
                Y = y,
                Width = Width - Padding * 2,
                Height = 25,
                TextColor = new Color(0.8f, 0.8f, 0.9f),
                Font = new FontReference(Style.Current.FontTitle)
            };
            
            var separator = new Panel
            {
                Parent = _scrollContent,
                X = Padding,
                Y = y + 22,
                Width = Width - Padding * 2,
                Height = 1,
                BackgroundColor = new Color(0.3f, 0.3f, 0.35f)
            };
            
            return y + 30;
        }
        
        /// <summary>
        /// 创建滑块行
        /// </summary>
        private float CreateSliderRow(string label, ref Slider sliderRef, float min, float max, float defaultValue,
            Action<float> onChanged, float y)
        {
            var labelControl = new Label
            {
                Parent = _scrollContent,
                Text = label,
                X = Padding,
                Y = y,
                Width = LabelWidth,
                Height = RowHeight,
                TextColor = Color.White,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center
            };
            
            var slider = new Slider
            {
                Parent = _scrollContent,
                X = Padding + LabelWidth,
                Y = y + 5,
                Width = SliderWidth,
                Height = RowHeight - 10,
                Minimum = min,
                Maximum = max,
                Value = defaultValue
            };
            sliderRef = slider;
            slider.ValueChanged += () => onChanged?.Invoke(slider.Value);
            
            var valueLabel = new Label
            {
                Parent = _scrollContent,
                X = Padding + LabelWidth + SliderWidth + 10,
                Y = y,
                Width = 50,
                Height = RowHeight,
                Text = defaultValue.ToString("F2"),
                TextColor = new Color(0.7f, 0.7f, 0.7f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center
            };
            
            slider.ValueChanged += () => valueLabel.Text = slider.Value.ToString("F2");
            
            return y + RowHeight;
        }
        
        /// <summary>
        /// 创建颜色选择行
        /// </summary>
        private float CreateColorRow(string label, ref ColorPicker colorPickerRef, Color defaultColor,
            Action<Color> onChanged, float y)
        {
            var labelControl = new Label
            {
                Parent = _scrollContent,
                Text = label,
                X = Padding,
                Y = y,
                Width = LabelWidth,
                Height = RowHeight,
                TextColor = Color.White,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center
            };
            
            var colorPicker = new ColorPicker
            {
                Parent = _scrollContent,
                X = Padding + LabelWidth,
                Y = y + 5,
                Width = ColorPickerWidth,
                Height = RowHeight - 10,
                Value = defaultColor
            };
            colorPickerRef = colorPicker;
            colorPicker.ValueChanged += () => onChanged?.Invoke(colorPicker.Value);
            
            return y + RowHeight;
        }
        
        /// <summary>
        /// 创建快速预设按钮
        /// </summary>
        private float CreateQuickPresetButtons(float y)
        {
            float buttonWidth = 70;
            float buttonHeight = 30;
            float spacing = 8;
            float currentX = Padding;
            
            // 蓝色眼睛
            var blueButton = new Button
            {
                Parent = _scrollContent,
                Text = "蓝色",
                X = currentX,
                Y = y,
                Width = buttonWidth,
                Height = buttonHeight,
                BackgroundColor = new Color(0.3f, 0.5f, 0.8f)
            };
            blueButton.Clicked += () => ApplyBlueEyes();
            currentX += buttonWidth + spacing;
            
            // 棕色眼睛
            var brownButton = new Button
            {
                Parent = _scrollContent,
                Text = "棕色",
                X = currentX,
                Y = y,
                Width = buttonWidth,
                Height = buttonHeight,
                BackgroundColor = new Color(0.5f, 0.35f, 0.2f)
            };
            brownButton.Clicked += () => ApplyBrownEyes();
            currentX += buttonWidth + spacing;
            
            // 绿色眼睛
            var greenButton = new Button
            {
                Parent = _scrollContent,
                Text = "绿色",
                X = currentX,
                Y = y,
                Width = buttonWidth,
                Height = buttonHeight,
                BackgroundColor = new Color(0.3f, 0.55f, 0.35f)
            };
            greenButton.Clicked += () => ApplyGreenEyes();
            currentX += buttonWidth + spacing;
            
            // 灰色眼睛
            var grayButton = new Button
            {
                Parent = _scrollContent,
                Text = "灰色",
                X = currentX,
                Y = y,
                Width = buttonWidth,
                Height = buttonHeight,
                BackgroundColor = new Color(0.5f, 0.52f, 0.55f)
            };
            grayButton.Clicked += () => ApplyGrayEyes();
            
            return y + buttonHeight + spacing;
        }
        
        // ===== 公开设置方法 =====
        
        public void SetIrisColor(Color color)
        {
            _isUpdating = true;
            if (_irisColorPicker != null) _irisColorPicker.Value = color;
            _isUpdating = false;
        }
        
        public void SetPupilSize(float value)
        {
            _isUpdating = true;
            if (_pupilSizeSlider != null) _pupilSizeSlider.Value = value;
            _isUpdating = false;
        }
        
        public void SetScleraColor(Color color)
        {
            _isUpdating = true;
            if (_scleraColorPicker != null) _scleraColorPicker.Value = color;
            _isUpdating = false;
        }
        
        public void SetIrisRoughness(float value)
        {
            _isUpdating = true;
            if (_irisRoughnessSlider != null) _irisRoughnessSlider.Value = value;
            _isUpdating = false;
        }
        
        public void SetCorneaRefraction(float value)
        {
            _isUpdating = true;
            if (_corneaRefractionSlider != null) _corneaRefractionSlider.Value = value;
            _isUpdating = false;
        }
        
        public void SetLimbusIntensity(float value)
        {
            _isUpdating = true;
            if (_limbusIntensitySlider != null) _limbusIntensitySlider.Value = value;
            _isUpdating = false;
        }
        
        // ===== 快速预设应用 =====
        
        private void ApplyBlueEyes()
        {
            _isUpdating = true;
            SetIrisColor(new Color(0.25f, 0.45f, 0.75f));
            if (_irisSecondaryColorPicker != null) _irisSecondaryColorPicker.Value = new Color(0.15f, 0.3f, 0.55f);
            if (_irisDetailSlider != null) _irisDetailSlider.Value = 1.0f;
            if (_limbusIntensitySlider != null) _limbusIntensitySlider.Value = 1.2f;
            _isUpdating = false;
            
            OnIrisColorChanged?.Invoke(new Color(0.25f, 0.45f, 0.75f));
        }
        
        private void ApplyBrownEyes()
        {
            _isUpdating = true;
            SetIrisColor(new Color(0.45f, 0.28f, 0.15f));
            if (_irisSecondaryColorPicker != null) _irisSecondaryColorPicker.Value = new Color(0.3f, 0.18f, 0.08f);
            if (_irisDetailSlider != null) _irisDetailSlider.Value = 0.8f;
            if (_limbusIntensitySlider != null) _limbusIntensitySlider.Value = 0.8f;
            _isUpdating = false;
            
            OnIrisColorChanged?.Invoke(new Color(0.45f, 0.28f, 0.15f));
        }
        
        private void ApplyGreenEyes()
        {
            _isUpdating = true;
            SetIrisColor(new Color(0.28f, 0.52f, 0.32f));
            if (_irisSecondaryColorPicker != null) _irisSecondaryColorPicker.Value = new Color(0.35f, 0.42f, 0.25f);
            if (_irisDetailSlider != null) _irisDetailSlider.Value = 1.1f;
            if (_limbusIntensitySlider != null) _limbusIntensitySlider.Value = 1.0f;
            _isUpdating = false;
            
            OnIrisColorChanged?.Invoke(new Color(0.28f, 0.52f, 0.32f));
        }
        
        private void ApplyGrayEyes()
        {
            _isUpdating = true;
            SetIrisColor(new Color(0.48f, 0.5f, 0.53f));
            if (_irisSecondaryColorPicker != null) _irisSecondaryColorPicker.Value = new Color(0.38f, 0.4f, 0.45f);
            if (_irisDetailSlider != null) _irisDetailSlider.Value = 0.7f;
            if (_limbusIntensitySlider != null) _limbusIntensitySlider.Value = 0.9f;
            _isUpdating = false;
            
            OnIrisColorChanged?.Invoke(new Color(0.48f, 0.5f, 0.53f));
        }
    }
}
