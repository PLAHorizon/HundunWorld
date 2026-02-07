using System;
using FlaxEngine;
using FlaxEngine.GUI;

namespace HundunWorld.UI.MetaHuman
{
    /// <summary>
    /// 毛发材质参数编辑面板
    /// 提供发色、各向异性高光、散射等参数的编辑控件
    /// </summary>
    public class HairEditorPanel : Panel
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
        
        // 基础颜色控件
        private ColorPicker _baseColorPicker;
        private ColorPicker _tipColorPicker;
        private ColorPicker _rootColorPicker;
        private Slider _colorVariationSlider;
        private Slider _melaninSlider;
        private Slider _melaninRedSlider;
        
        // 高光参数控件
        private Slider _roughnessSlider;
        private Slider _metallicSlider;
        private Slider _anisotropySlider;
        private Float2Slider _anisotropyDirectionSlider;
        private Slider _primarySpecularSlider;
        private ColorPicker _primarySpecularColorPicker;
        private Slider _secondarySpecularSlider;
        private ColorPicker _secondarySpecularColorPicker;
        private Slider _specularShiftSlider;
        
        // 散射参数控件
        private Slider _scatterIntensitySlider;
        private ColorPicker _scatterColorPicker;
        private Slider _transmissionSlider;
        private Slider _backscatterSlider;
        
        // 细节参数控件
        private Slider _strandThicknessSlider;
        private Slider _strandRoughnessSlider;
        private Slider _aoIntensitySlider;
        private Slider _shadowIntensitySlider;
        
        // 动态效果参数
        private Slider _windResponseSlider;
        private Slider _gravitySlider;
        private Slider _stiffnessSlider;
        
        // 事件
        public event Action<Color> OnBaseColorChanged;
        public event Action<Color> OnTipColorChanged;
        public event Action<Color> OnRootColorChanged;
        public event Action<float> OnColorVariationChanged;
        public event Action<float> OnMelaninChanged;
        public event Action<float> OnMelaninRedChanged;
        public event Action<float> OnRoughnessChanged;
        public event Action<float> OnMetallicChanged;
        public event Action<float> OnAnisotropyChanged;
        public event Action<Float2> OnAnisotropyDirectionChanged;
        public event Action<float> OnPrimarySpecularChanged;
        public event Action<Color> OnPrimarySpecularColorChanged;
        public event Action<float> OnSecondarySpecularChanged;
        public event Action<Color> OnSecondarySpecularColorChanged;
        public event Action<float> OnSpecularShiftChanged;
        public event Action<float> OnScatterIntensityChanged;
        public event Action<Color> OnScatterColorChanged;
        public event Action<float> OnTransmissionChanged;
        public event Action<float> OnBackscatterChanged;
        public event Action<float> OnStrandThicknessChanged;
        public event Action<float> OnStrandRoughnessChanged;
        public event Action<float> OnAOIntensityChanged;
        public event Action<float> OnShadowIntensityChanged;
        public event Action<float> OnWindResponseChanged;
        public event Action<float> OnGravityChanged;
        public event Action<float> OnStiffnessChanged;
        
        private bool _isUpdating;
        
        public HairEditorPanel()
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
            
            // ===== 发色参数区 =====
            currentY = CreateSectionHeader("发色", currentY);
            currentY = CreateColorRow("基础发色", ref _baseColorPicker, new Color(0.15f, 0.1f, 0.08f),
                (color) => { if (!_isUpdating) OnBaseColorChanged?.Invoke(color); }, currentY);
            currentY = CreateColorRow("发梢颜色", ref _tipColorPicker, new Color(0.2f, 0.15f, 0.1f),
                (color) => { if (!_isUpdating) OnTipColorChanged?.Invoke(color); }, currentY);
            currentY = CreateColorRow("发根颜色", ref _rootColorPicker, new Color(0.1f, 0.07f, 0.05f),
                (color) => { if (!_isUpdating) OnRootColorChanged?.Invoke(color); }, currentY);
            currentY = CreateSliderRow("颜色变化", ref _colorVariationSlider, 0f, 1f, 0.1f,
                (value) => { if (!_isUpdating) OnColorVariationChanged?.Invoke(value); }, currentY);
            currentY = CreateSliderRow("黑色素", ref _melaninSlider, 0f, 1f, 0.8f,
                (value) => { if (!_isUpdating) OnMelaninChanged?.Invoke(value); }, currentY);
            currentY = CreateSliderRow("红色素", ref _melaninRedSlider, 0f, 1f, 0.2f,
                (value) => { if (!_isUpdating) OnMelaninRedChanged?.Invoke(value); }, currentY);
            
            currentY += SectionSpacing;
            
            // ===== 高光参数区 =====
            currentY = CreateSectionHeader("高光 (Specular)", currentY);
            currentY = CreateSliderRow("粗糙度", ref _roughnessSlider, 0f, 1f, 0.45f,
                (value) => { if (!_isUpdating) OnRoughnessChanged?.Invoke(value); }, currentY);
            currentY = CreateSliderRow("金属度", ref _metallicSlider, 0f, 1f, 0f,
                (value) => { if (!_isUpdating) OnMetallicChanged?.Invoke(value); }, currentY);
            currentY = CreateSliderRow("各向异性", ref _anisotropySlider, 0f, 1f, 0.85f,
                (value) => { if (!_isUpdating) OnAnisotropyChanged?.Invoke(value); }, currentY);
            currentY = CreateSliderRow("主高光强度", ref _primarySpecularSlider, 0f, 2f, 1f,
                (value) => { if (!_isUpdating) OnPrimarySpecularChanged?.Invoke(value); }, currentY);
            currentY = CreateColorRow("主高光颜色", ref _primarySpecularColorPicker, new Color(1f, 0.95f, 0.9f),
                (color) => { if (!_isUpdating) OnPrimarySpecularColorChanged?.Invoke(color); }, currentY);
            currentY = CreateSliderRow("次高光强度", ref _secondarySpecularSlider, 0f, 2f, 0.5f,
                (value) => { if (!_isUpdating) OnSecondarySpecularChanged?.Invoke(value); }, currentY);
            currentY = CreateColorRow("次高光颜色", ref _secondarySpecularColorPicker, new Color(0.9f, 0.85f, 0.75f),
                (color) => { if (!_isUpdating) OnSecondarySpecularColorChanged?.Invoke(color); }, currentY);
            currentY = CreateSliderRow("高光偏移", ref _specularShiftSlider, -1f, 1f, 0f,
                (value) => { if (!_isUpdating) OnSpecularShiftChanged?.Invoke(value); }, currentY);
            
            currentY += SectionSpacing;
            
            // ===== 散射参数区 =====
            currentY = CreateSectionHeader("散射 (Scatter)", currentY);
            currentY = CreateSliderRow("散射强度", ref _scatterIntensitySlider, 0f, 2f, 0.8f,
                (value) => { if (!_isUpdating) OnScatterIntensityChanged?.Invoke(value); }, currentY);
            currentY = CreateColorRow("散射颜色", ref _scatterColorPicker, new Color(0.8f, 0.5f, 0.3f),
                (color) => { if (!_isUpdating) OnScatterColorChanged?.Invoke(color); }, currentY);
            currentY = CreateSliderRow("透射", ref _transmissionSlider, 0f, 1f, 0.3f,
                (value) => { if (!_isUpdating) OnTransmissionChanged?.Invoke(value); }, currentY);
            currentY = CreateSliderRow("背散射", ref _backscatterSlider, 0f, 1f, 0.2f,
                (value) => { if (!_isUpdating) OnBackscatterChanged?.Invoke(value); }, currentY);
            
            currentY += SectionSpacing;
            
            // ===== 细节参数区 =====
            currentY = CreateSectionHeader("细节", currentY);
            currentY = CreateSliderRow("发丝粗细", ref _strandThicknessSlider, 0.01f, 0.2f, 0.05f,
                (value) => { if (!_isUpdating) OnStrandThicknessChanged?.Invoke(value); }, currentY);
            currentY = CreateSliderRow("发丝粗糙度", ref _strandRoughnessSlider, 0f, 1f, 0.3f,
                (value) => { if (!_isUpdating) OnStrandRoughnessChanged?.Invoke(value); }, currentY);
            currentY = CreateSliderRow("AO强度", ref _aoIntensitySlider, 0f, 2f, 1f,
                (value) => { if (!_isUpdating) OnAOIntensityChanged?.Invoke(value); }, currentY);
            currentY = CreateSliderRow("阴影强度", ref _shadowIntensitySlider, 0f, 2f, 1f,
                (value) => { if (!_isUpdating) OnShadowIntensityChanged?.Invoke(value); }, currentY);
            
            currentY += SectionSpacing;
            
            // ===== 动态效果参数区 =====
            currentY = CreateSectionHeader("动态效果", currentY);
            currentY = CreateSliderRow("风力响应", ref _windResponseSlider, 0f, 2f, 1f,
                (value) => { if (!_isUpdating) OnWindResponseChanged?.Invoke(value); }, currentY);
            currentY = CreateSliderRow("重力影响", ref _gravitySlider, 0f, 2f, 1f,
                (value) => { if (!_isUpdating) OnGravityChanged?.Invoke(value); }, currentY);
            currentY = CreateSliderRow("刚度", ref _stiffnessSlider, 0f, 1f, 0.5f,
                (value) => { if (!_isUpdating) OnStiffnessChanged?.Invoke(value); }, currentY);
            
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
            
            // 黑色头发
            var blackButton = new Button
            {
                Parent = _scrollContent,
                Text = "黑色",
                X = currentX,
                Y = y,
                Width = buttonWidth,
                Height = buttonHeight,
                BackgroundColor = new Color(0.05f, 0.05f, 0.05f),
                TextColor = Color.White
            };
            blackButton.Clicked += () => ApplyBlackHair();
            currentX += buttonWidth + spacing;
            
            // 金色头发
            var blondeButton = new Button
            {
                Parent = _scrollContent,
                Text = "金色",
                X = currentX,
                Y = y,
                Width = buttonWidth,
                Height = buttonHeight,
                BackgroundColor = new Color(0.9f, 0.75f, 0.5f)
            };
            blondeButton.Clicked += () => ApplyBlondeHair();
            currentX += buttonWidth + spacing;
            
            // 棕色头发
            var brownButton = new Button
            {
                Parent = _scrollContent,
                Text = "棕色",
                X = currentX,
                Y = y,
                Width = buttonWidth,
                Height = buttonHeight,
                BackgroundColor = new Color(0.4f, 0.25f, 0.15f)
            };
            brownButton.Clicked += () => ApplyBrownHair();
            currentX += buttonWidth + spacing;
            
            // 红色头发
            var redButton = new Button
            {
                Parent = _scrollContent,
                Text = "红色",
                X = currentX,
                Y = y,
                Width = buttonWidth,
                Height = buttonHeight,
                BackgroundColor = new Color(0.6f, 0.2f, 0.1f)
            };
            redButton.Clicked += () => ApplyRedHair();
            
            return y + buttonHeight + spacing;
        }
        
        // ===== 公开设置方法 =====
        
        public void SetBaseColor(Color color)
        {
            _isUpdating = true;
            if (_baseColorPicker != null) _baseColorPicker.Value = color;
            _isUpdating = false;
        }
        
        public void SetTipColor(Color color)
        {
            _isUpdating = true;
            if (_tipColorPicker != null) _tipColorPicker.Value = color;
            _isUpdating = false;
        }
        
        public void SetRootColor(Color color)
        {
            _isUpdating = true;
            if (_rootColorPicker != null) _rootColorPicker.Value = color;
            _isUpdating = false;
        }
        
        public void SetRoughness(float value)
        {
            _isUpdating = true;
            if (_roughnessSlider != null) _roughnessSlider.Value = value;
            _isUpdating = false;
        }
        
        public void SetMetallic(float value)
        {
            _isUpdating = true;
            if (_metallicSlider != null) _metallicSlider.Value = value;
            _isUpdating = false;
        }
        
        public void SetAnisotropy(float value)
        {
            _isUpdating = true;
            if (_anisotropySlider != null) _anisotropySlider.Value = value;
            _isUpdating = false;
        }
        
        public void SetScatterIntensity(float value)
        {
            _isUpdating = true;
            if (_scatterIntensitySlider != null) _scatterIntensitySlider.Value = value;
            _isUpdating = false;
        }
        
        // ===== 快速预设应用 =====
        
        private void ApplyBlackHair()
        {
            _isUpdating = true;
            SetBaseColor(new Color(0.03f, 0.03f, 0.03f));
            SetTipColor(new Color(0.05f, 0.05f, 0.05f));
            SetRootColor(new Color(0.02f, 0.02f, 0.02f));
            if (_melaninSlider != null) _melaninSlider.Value = 0.95f;
            if (_melaninRedSlider != null) _melaninRedSlider.Value = 0.1f;
            SetRoughness(0.5f);
            SetAnisotropy(0.8f);
            SetScatterIntensity(0.6f);
            _isUpdating = false;
            
            OnBaseColorChanged?.Invoke(new Color(0.03f, 0.03f, 0.03f));
        }
        
        private void ApplyBlondeHair()
        {
            _isUpdating = true;
            SetBaseColor(new Color(0.85f, 0.7f, 0.45f));
            SetTipColor(new Color(0.9f, 0.78f, 0.55f));
            SetRootColor(new Color(0.7f, 0.55f, 0.35f));
            if (_melaninSlider != null) _melaninSlider.Value = 0.2f;
            if (_melaninRedSlider != null) _melaninRedSlider.Value = 0.4f;
            SetRoughness(0.4f);
            SetAnisotropy(0.9f);
            SetScatterIntensity(1.2f);
            _isUpdating = false;
            
            OnBaseColorChanged?.Invoke(new Color(0.85f, 0.7f, 0.45f));
        }
        
        private void ApplyBrownHair()
        {
            _isUpdating = true;
            SetBaseColor(new Color(0.35f, 0.2f, 0.12f));
            SetTipColor(new Color(0.4f, 0.25f, 0.15f));
            SetRootColor(new Color(0.25f, 0.15f, 0.08f));
            if (_melaninSlider != null) _melaninSlider.Value = 0.7f;
            if (_melaninRedSlider != null) _melaninRedSlider.Value = 0.3f;
            SetRoughness(0.45f);
            SetAnisotropy(0.85f);
            SetScatterIntensity(0.8f);
            _isUpdating = false;
            
            OnBaseColorChanged?.Invoke(new Color(0.35f, 0.2f, 0.12f));
        }
        
        private void ApplyRedHair()
        {
            _isUpdating = true;
            SetBaseColor(new Color(0.55f, 0.18f, 0.08f));
            SetTipColor(new Color(0.65f, 0.25f, 0.12f));
            SetRootColor(new Color(0.4f, 0.12f, 0.05f));
            if (_melaninSlider != null) _melaninSlider.Value = 0.4f;
            if (_melaninRedSlider != null) _melaninRedSlider.Value = 0.85f;
            SetRoughness(0.42f);
            SetAnisotropy(0.88f);
            SetScatterIntensity(1.0f);
            _isUpdating = false;
            
            OnBaseColorChanged?.Invoke(new Color(0.55f, 0.18f, 0.08f));
        }
    }
    
    /// <summary>
    /// Float2滑块控件（用于方向参数）
    /// </summary>
    public class Float2Slider : ContainerControl
    {
        private Slider _xSlider;
        private Slider _ySlider;
        private Float2 _value;
        
        public Float2 Value
        {
            get => _value;
            set
            {
                _value = value;
                if (_xSlider != null) _xSlider.Value = value.X;
                if (_ySlider != null) _ySlider.Value = value.Y;
            }
        }
        
        public event Action<Float2> ValueChanged;
        
        public Float2Slider()
        {
            Height = 50;
            
            _xSlider = new Slider
            {
                Parent = this,
                X = 0,
                Y = 0,
                Width = 100,
                Height = 20,
                Minimum = -1,
                Maximum = 1,
                Value = 0
            };
            _xSlider.ValueChanged += OnSliderChanged;
            
            _ySlider = new Slider
            {
                Parent = this,
                X = 0,
                Y = 25,
                Width = 100,
                Height = 20,
                Minimum = -1,
                Maximum = 1,
                Value = 0
            };
            _ySlider.ValueChanged += OnSliderChanged;
        }
        
        private void OnSliderChanged()
        {
            _value = new Float2(_xSlider.Value, _ySlider.Value);
            ValueChanged?.Invoke(_value);
        }
    }
}
