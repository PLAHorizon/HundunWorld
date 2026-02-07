using System;
using FlaxEngine;
using FlaxEngine.GUI;

namespace HundunWorld.UI.MetaHuman
{
    /// <summary>
    /// 皮肤材质参数编辑面板
    /// 提供滑块、颜色选择器等控件来调整皮肤材质属性
    /// </summary>
    public class SkinEditorPanel : Panel
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
        
        // 基础参数控件
        private ColorPicker _baseColorPicker;
        private Slider _roughnessSlider;
        private Slider _metallicSlider;
        
        // SSS参数控件
        private Slider _sssIntensitySlider;
        private ColorPicker _epidermisColorPicker;
        private ColorPicker _dermisColorPicker;
        private ColorPicker _subcutisColorPicker;
        private Slider _epidermisThicknessSlider;
        private Slider _dermisThicknessSlider;
        private Slider _subcutisThicknessSlider;
        private Slider _scatterRadiusSlider;
        private Slider _scatterFalloffSlider;
        
        // 细节参数控件
        private Slider _detailNormalStrengthSlider;
        private Slider _poreIntensitySlider;
        private Slider _poreScaleSlider;
        private Slider _wrinkleIntensitySlider;
        
        // 皮肤特征控件
        private Slider _oilIntensitySlider;
        private Slider _oilRoughnessSlider;
        private Slider _freckleIntensitySlider;
        private ColorPicker _freckleColorPicker;
        private Slider _freckleScaleSlider;
        private Slider _moleIntensitySlider;
        private Slider _veinIntensitySlider;
        private ColorPicker _veinColorPicker;
        
        // 微表面参数
        private Slider _microRoughnessSlider;
        private Slider _microNormalStrengthSlider;
        private Slider _aoIntensitySlider;
        private Slider _cavityIntensitySlider;
        
        // 事件
        public event Action<Color> OnBaseColorChanged;
        public event Action<float> OnRoughnessChanged;
        public event Action<float> OnMetallicChanged;
        public event Action<float> OnSSSIntensityChanged;
        public event Action<Color> OnEpidermisColorChanged;
        public event Action<Color> OnDermisColorChanged;
        public event Action<Color> OnSubcutisColorChanged;
        public event Action<float> OnEpidermisThicknessChanged;
        public event Action<float> OnDermisThicknessChanged;
        public event Action<float> OnSubcutisThicknessChanged;
        public event Action<float> OnScatterRadiusChanged;
        public event Action<float> OnScatterFalloffChanged;
        public event Action<float> OnDetailNormalStrengthChanged;
        public event Action<float> OnPoreIntensityChanged;
        public event Action<float> OnPoreScaleChanged;
        public event Action<float> OnWrinkleIntensityChanged;
        public event Action<float> OnOilIntensityChanged;
        public event Action<float> OnOilRoughnessChanged;
        public event Action<float> OnFreckleIntensityChanged;
        public event Action<Color> OnFreckleColorChanged;
        public event Action<float> OnFreckleScaleChanged;
        public event Action<float> OnMoleIntensityChanged;
        public event Action<float> OnVeinIntensityChanged;
        public event Action<Color> OnVeinColorChanged;
        public event Action<float> OnMicroRoughnessChanged;
        public event Action<float> OnMicroNormalStrengthChanged;
        public event Action<float> OnAOIntensityChanged;
        public event Action<float> OnCavityIntensityChanged;
        
        // 是否正在程序化更新（避免循环触发事件）
        private bool _isUpdating;
        
        public SkinEditorPanel()
        {
            BackgroundColor = new Color(0.14f, 0.14f, 0.16f, 1.0f);
            // Panel doesn't have AutoScroll in Flax 1.11
        }
        
        /// <inheritdoc/>
        public override void OnDestroy()
        {
            base.OnDestroy();
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
            // 清除现有控件
            DisposeChildren();
            
            // 创建滚动内容容器
            _scrollContent = new VerticalPanel
            {
                Parent = this,
                AnchorPreset = AnchorPresets.HorizontalStretchTop,
                AutoSize = true,
                Spacing = 5
            };
            
            float currentY = Padding;
            
            // ===== 基础参数区 =====
            currentY = CreateSectionHeader("基础参数", currentY);
            currentY = CreateColorRow("基肤色", ref _baseColorPicker, new Color(1.0f, 0.85f, 0.75f), 
                (color) => { if (!_isUpdating) OnBaseColorChanged?.Invoke(color); }, currentY);
            currentY = CreateSliderRow("粗糙度", ref _roughnessSlider, 0f, 1f, 0.35f,
                (value) => { if (!_isUpdating) OnRoughnessChanged?.Invoke(value); }, currentY);
            currentY = CreateSliderRow("金属度", ref _metallicSlider, 0f, 1f, 0f,
                (value) => { if (!_isUpdating) OnMetallicChanged?.Invoke(value); }, currentY);
            
            currentY += SectionSpacing;
            
            // ===== SSS次表面散射区 =====
            currentY = CreateSectionHeader("次表面散射 (SSS)", currentY);
            currentY = CreateSliderRow("SSS强度", ref _sssIntensitySlider, 0f, 2f, 1f,
                (value) => { if (!_isUpdating) OnSSSIntensityChanged?.Invoke(value); }, currentY);
            currentY = CreateColorRow("表皮层颜色", ref _epidermisColorPicker, new Color(1.0f, 0.9f, 0.85f),
                (color) => { if (!_isUpdating) OnEpidermisColorChanged?.Invoke(color); }, currentY);
            currentY = CreateSliderRow("表皮层厚度", ref _epidermisThicknessSlider, 0f, 5f, 0.8f,
                (value) => { if (!_isUpdating) OnEpidermisThicknessChanged?.Invoke(value); }, currentY);
            currentY = CreateColorRow("真皮层颜色", ref _dermisColorPicker, new Color(0.9f, 0.5f, 0.4f),
                (color) => { if (!_isUpdating) OnDermisColorChanged?.Invoke(color); }, currentY);
            currentY = CreateSliderRow("真皮层厚度", ref _dermisThicknessSlider, 0f, 5f, 1.5f,
                (value) => { if (!_isUpdating) OnDermisThicknessChanged?.Invoke(value); }, currentY);
            currentY = CreateColorRow("皮下层颜色", ref _subcutisColorPicker, new Color(0.8f, 0.3f, 0.25f),
                (color) => { if (!_isUpdating) OnSubcutisColorChanged?.Invoke(color); }, currentY);
            currentY = CreateSliderRow("皮下层厚度", ref _subcutisThicknessSlider, 0f, 10f, 3f,
                (value) => { if (!_isUpdating) OnSubcutisThicknessChanged?.Invoke(value); }, currentY);
            currentY = CreateSliderRow("散射半径", ref _scatterRadiusSlider, 0f, 5f, 1.2f,
                (value) => { if (!_isUpdating) OnScatterRadiusChanged?.Invoke(value); }, currentY);
            currentY = CreateSliderRow("散射衰减", ref _scatterFalloffSlider, 0f, 2f, 0.8f,
                (value) => { if (!_isUpdating) OnScatterFalloffChanged?.Invoke(value); }, currentY);
            
            currentY += SectionSpacing;
            
            // ===== 皮肤细节区 =====
            currentY = CreateSectionHeader("皮肤细节", currentY);
            currentY = CreateSliderRow("细节法线强度", ref _detailNormalStrengthSlider, 0f, 2f, 0.5f,
                (value) => { if (!_isUpdating) OnDetailNormalStrengthChanged?.Invoke(value); }, currentY);
            currentY = CreateSliderRow("毛孔强度", ref _poreIntensitySlider, 0f, 2f, 0.6f,
                (value) => { if (!_isUpdating) OnPoreIntensityChanged?.Invoke(value); }, currentY);
            currentY = CreateSliderRow("毛孔缩放", ref _poreScaleSlider, 0.1f, 5f, 1f,
                (value) => { if (!_isUpdating) OnPoreScaleChanged?.Invoke(value); }, currentY);
            currentY = CreateSliderRow("皱纹强度", ref _wrinkleIntensitySlider, 0f, 2f, 0.3f,
                (value) => { if (!_isUpdating) OnWrinkleIntensityChanged?.Invoke(value); }, currentY);
            
            currentY += SectionSpacing;
            
            // ===== 皮肤特征区 =====
            currentY = CreateSectionHeader("皮肤特征", currentY);
            currentY = CreateSliderRow("油光强度", ref _oilIntensitySlider, 0f, 2f, 0.3f,
                (value) => { if (!_isUpdating) OnOilIntensityChanged?.Invoke(value); }, currentY);
            currentY = CreateSliderRow("油光粗糙度", ref _oilRoughnessSlider, 0f, 1f, 0.15f,
                (value) => { if (!_isUpdating) OnOilRoughnessChanged?.Invoke(value); }, currentY);
            currentY = CreateSliderRow("雀斑强度", ref _freckleIntensitySlider, 0f, 1f, 0f,
                (value) => { if (!_isUpdating) OnFreckleIntensityChanged?.Invoke(value); }, currentY);
            currentY = CreateColorRow("雀斑颜色", ref _freckleColorPicker, new Color(0.6f, 0.4f, 0.3f),
                (color) => { if (!_isUpdating) OnFreckleColorChanged?.Invoke(color); }, currentY);
            currentY = CreateSliderRow("雀斑缩放", ref _freckleScaleSlider, 0.1f, 5f, 1f,
                (value) => { if (!_isUpdating) OnFreckleScaleChanged?.Invoke(value); }, currentY);
            currentY = CreateSliderRow("痣强度", ref _moleIntensitySlider, 0f, 1f, 0f,
                (value) => { if (!_isUpdating) OnMoleIntensityChanged?.Invoke(value); }, currentY);
            currentY = CreateSliderRow("血管强度", ref _veinIntensitySlider, 0f, 1f, 0.2f,
                (value) => { if (!_isUpdating) OnVeinIntensityChanged?.Invoke(value); }, currentY);
            currentY = CreateColorRow("血管颜色", ref _veinColorPicker, new Color(0.4f, 0.3f, 0.5f),
                (color) => { if (!_isUpdating) OnVeinColorChanged?.Invoke(color); }, currentY);
            
            currentY += SectionSpacing;
            
            // ===== 微表面细节区 =====
            currentY = CreateSectionHeader("微表面细节", currentY);
            currentY = CreateSliderRow("微粗糙度", ref _microRoughnessSlider, 0f, 1f, 0.4f,
                (value) => { if (!_isUpdating) OnMicroRoughnessChanged?.Invoke(value); }, currentY);
            currentY = CreateSliderRow("微法线强度", ref _microNormalStrengthSlider, 0f, 1f, 0.3f,
                (value) => { if (!_isUpdating) OnMicroNormalStrengthChanged?.Invoke(value); }, currentY);
            currentY = CreateSliderRow("AO强度", ref _aoIntensitySlider, 0f, 2f, 1f,
                (value) => { if (!_isUpdating) OnAOIntensityChanged?.Invoke(value); }, currentY);
            currentY = CreateSliderRow("凹陷强度", ref _cavityIntensitySlider, 0f, 2f, 0.5f,
                (value) => { if (!_isUpdating) OnCavityIntensityChanged?.Invoke(value); }, currentY);
            
            currentY += SectionSpacing;
            
            // ===== 快速预设按钮区 =====
            currentY = CreateSectionHeader("快速预设", currentY);
            currentY = CreateQuickPresetButtons(currentY);
            
            // 更新滚动内容高度
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
                TextColorHighlighted = new Color(0.9f, 0.9f, 1.0f),
                Font = new FontReference(Style.Current.FontTitle)
            };
            
            // 分隔线
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
            // 标签
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
            
            // 滑块
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
            
            // 数值显示
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
            
            // 更新数值显示
            slider.ValueChanged += () => valueLabel.Text = slider.Value.ToString("F2");
            
            return y + RowHeight;
        }
        
        /// <summary>
        /// 创建颜色选择行
        /// </summary>
        private float CreateColorRow(string label, ref ColorPicker colorPickerRef, Color defaultColor,
            Action<Color> onChanged, float y)
        {
            // 标签
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
            
            // 简化的颜色选择器
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
            
            // 十六进制值显示
            var hexLabel = new Label
            {
                Parent = _scrollContent,
                X = Padding + LabelWidth + ColorPickerWidth + 10,
                Y = y,
                Width = 80,
                Height = RowHeight,
                Text = ColorToHex(defaultColor),
                TextColor = new Color(0.7f, 0.7f, 0.7f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center
            };
            
            colorPicker.ValueChanged += () => hexLabel.Text = ColorToHex(colorPicker.Value);
            
            return y + RowHeight;
        }
        
        /// <summary>
        /// 创建快速预设按钮
        /// </summary>
        private float CreateQuickPresetButtons(float y)
        {
            float buttonWidth = 80;
            float buttonHeight = 30;
            float spacing = 10;
            float currentX = Padding;
            
            // 亚洲皮肤
            var asianButton = new Button
            {
                Parent = _scrollContent,
                Text = "亚洲",
                X = currentX,
                Y = y,
                Width = buttonWidth,
                Height = buttonHeight,
                BackgroundColor = new Color(0.9f, 0.75f, 0.6f)
            };
            asianButton.Clicked += () => ApplyAsianSkin();
            currentX += buttonWidth + spacing;
            
            // 欧洲皮肤
            var europeanButton = new Button
            {
                Parent = _scrollContent,
                Text = "欧洲",
                X = currentX,
                Y = y,
                Width = buttonWidth,
                Height = buttonHeight,
                BackgroundColor = new Color(1.0f, 0.85f, 0.75f)
            };
            europeanButton.Clicked += () => ApplyEuropeanSkin();
            currentX += buttonWidth + spacing;
            
            // 年轻皮肤
            var youngButton = new Button
            {
                Parent = _scrollContent,
                Text = "年轻",
                X = currentX,
                Y = y,
                Width = buttonWidth,
                Height = buttonHeight,
                BackgroundColor = new Color(1.0f, 0.9f, 0.85f)
            };
            youngButton.Clicked += () => ApplyYoungSkin();
            currentX += buttonWidth + spacing;
            
            // 成熟皮肤
            var matureButton = new Button
            {
                Parent = _scrollContent,
                Text = "成熟",
                X = currentX,
                Y = y,
                Width = buttonWidth,
                Height = buttonHeight,
                BackgroundColor = new Color(0.95f, 0.8f, 0.7f)
            };
            matureButton.Clicked += () => ApplyMatureSkin();
            
            return y + buttonHeight + spacing;
        }
        
        /// <summary>
        /// 颜色转十六进制
        /// </summary>
        private string ColorToHex(Color color)
        {
            int r = (int)(color.R * 255);
            int g = (int)(color.G * 255);
            int b = (int)(color.B * 255);
            return $"#{r:X2}{g:X2}{b:X2}";
        }
        
        // ===== 公开设置方法 =====
        
        public void SetBaseColor(Color color)
        {
            _isUpdating = true;
            if (_baseColorPicker != null) _baseColorPicker.Value = color;
            _isUpdating = false;
        }
        
        public void SetRoughness(float value)
        {
            _isUpdating = true;
            if (_roughnessSlider != null) _roughnessSlider.Value = value;
            _isUpdating = false;
        }
        
        public void SetSSSIntensity(float value)
        {
            _isUpdating = true;
            if (_sssIntensitySlider != null) _sssIntensitySlider.Value = value;
            _isUpdating = false;
        }
        
        public void SetEpidermisColor(Color color)
        {
            _isUpdating = true;
            if (_epidermisColorPicker != null) _epidermisColorPicker.Value = color;
            _isUpdating = false;
        }
        
        public void SetDermisColor(Color color)
        {
            _isUpdating = true;
            if (_dermisColorPicker != null) _dermisColorPicker.Value = color;
            _isUpdating = false;
        }
        
        public void SetSubcutisColor(Color color)
        {
            _isUpdating = true;
            if (_subcutisColorPicker != null) _subcutisColorPicker.Value = color;
            _isUpdating = false;
        }
        
        public void SetDetailNormalStrength(float value)
        {
            _isUpdating = true;
            if (_detailNormalStrengthSlider != null) _detailNormalStrengthSlider.Value = value;
            _isUpdating = false;
        }
        
        public void SetPoreIntensity(float value)
        {
            _isUpdating = true;
            if (_poreIntensitySlider != null) _poreIntensitySlider.Value = value;
            _isUpdating = false;
        }
        
        public void SetOilIntensity(float value)
        {
            _isUpdating = true;
            if (_oilIntensitySlider != null) _oilIntensitySlider.Value = value;
            _isUpdating = false;
        }
        
        public void SetFreckleIntensity(float value)
        {
            _isUpdating = true;
            if (_freckleIntensitySlider != null) _freckleIntensitySlider.Value = value;
            _isUpdating = false;
        }
        
        // ===== 快速预设应用 =====
        
        private void ApplyAsianSkin()
        {
            _isUpdating = true;
            SetBaseColor(new Color(0.92f, 0.78f, 0.65f));
            SetRoughness(0.38f);
            SetSSSIntensity(1.0f);
            SetEpidermisColor(new Color(0.95f, 0.85f, 0.75f));
            SetDermisColor(new Color(0.85f, 0.55f, 0.45f));
            SetSubcutisColor(new Color(0.75f, 0.35f, 0.28f));
            SetPoreIntensity(0.5f);
            SetOilIntensity(0.35f);
            _isUpdating = false;
            
            // 触发事件
            OnBaseColorChanged?.Invoke(new Color(0.92f, 0.78f, 0.65f));
        }
        
        private void ApplyEuropeanSkin()
        {
            _isUpdating = true;
            SetBaseColor(new Color(1.0f, 0.87f, 0.78f));
            SetRoughness(0.32f);
            SetSSSIntensity(1.1f);
            SetEpidermisColor(new Color(1.0f, 0.92f, 0.88f));
            SetDermisColor(new Color(0.92f, 0.55f, 0.42f));
            SetSubcutisColor(new Color(0.82f, 0.32f, 0.25f));
            SetPoreIntensity(0.65f);
            SetOilIntensity(0.25f);
            _isUpdating = false;
            
            OnBaseColorChanged?.Invoke(new Color(1.0f, 0.87f, 0.78f));
        }
        
        private void ApplyYoungSkin()
        {
            _isUpdating = true;
            SetBaseColor(new Color(1.0f, 0.9f, 0.82f));
            SetRoughness(0.28f);
            SetSSSIntensity(1.15f);
            SetPoreIntensity(0.4f);
            SetOilIntensity(0.4f);
            if (_wrinkleIntensitySlider != null) _wrinkleIntensitySlider.Value = 0.1f;
            _isUpdating = false;
            
            OnBaseColorChanged?.Invoke(new Color(1.0f, 0.9f, 0.82f));
        }
        
        private void ApplyMatureSkin()
        {
            _isUpdating = true;
            SetBaseColor(new Color(0.95f, 0.82f, 0.72f));
            SetRoughness(0.42f);
            SetSSSIntensity(0.9f);
            SetPoreIntensity(0.75f);
            SetOilIntensity(0.2f);
            if (_wrinkleIntensitySlider != null) _wrinkleIntensitySlider.Value = 0.6f;
            _isUpdating = false;
            
            OnBaseColorChanged?.Invoke(new Color(0.95f, 0.82f, 0.72f));
        }
    }
    
    /// <summary>
    /// 颜色选择器控件（简化版）
    /// </summary>
    public class ColorPicker : ContainerControl
    {
        private Color _value;
        private Panel _colorPreview;
        
        public Color Value
        {
            get => _value;
            set
            {
                if (_value != value)
                {
                    _value = value;
                    UpdatePreview();
                    ValueChanged?.Invoke();
                }
            }
        }
        
        public event Action ValueChanged;
        
        public ColorPicker()
        {
            _colorPreview = new Panel
            {
                Parent = this,
                AnchorPreset = AnchorPresets.StretchAll,
                BackgroundColor = _value
            };
        }
        
        private void UpdatePreview()
        {
            if (_colorPreview != null)
            {
                _colorPreview.BackgroundColor = _value;
            }
        }
        
        public override bool OnMouseUp(Float2 location, MouseButton button)
        {
            if (button == MouseButton.Left)
            {
                // 打开颜色选择对话框
                ShowColorDialog();
                return true;
            }
            return base.OnMouseUp(location, button);
        }
        
        private void ShowColorDialog()
        {
            // 创建简单的颜色选择弹窗
            var dialog = new ColorPickerDialog(_value);
            dialog.ColorSelected += (color) => Value = color;
            dialog.Show(Root);
        }
    }
    
    /// <summary>
    /// 颜色选择对话框（使用Panel实现浮动对话框）
    /// </summary>
    public class ColorPickerDialog : Panel
    {
        private Color _selectedColor;
        private Slider _rSlider;
        private Slider _gSlider;
        private Slider _bSlider;
        private Panel _previewPanel;
        
        public event Action<Color> ColorSelected;
        
        public ColorPickerDialog(Color initialColor)
        {
            _selectedColor = initialColor;
            Width = 300;
            Height = 200;
            BackgroundColor = new Color(0.18f, 0.18f, 0.2f, 1.0f);
            CreateUI();
        }
        
        private void CreateUI()
        {
            // 标题
            var titleLabel = new Label
            {
                Parent = this,
                Text = "选择颜色",
                X = 10,
                Y = 5,
                Width = 280,
                Height = 20,
                TextColor = Color.White
            };
            
            // R滑块
            var rLabel = new Label { Parent = this, Text = "R", X = 10, Y = 35, Width = 20, Height = 25, TextColor = Color.Red };
            _rSlider = new Slider { Parent = this, X = 35, Y = 35, Width = 180, Height = 25, Minimum = 0, Maximum = 1, Value = _selectedColor.R };
            _rSlider.ValueChanged += UpdateColor;
            
            // G滑块
            var gLabel = new Label { Parent = this, Text = "G", X = 10, Y = 70, Width = 20, Height = 25, TextColor = Color.Green };
            _gSlider = new Slider { Parent = this, X = 35, Y = 70, Width = 180, Height = 25, Minimum = 0, Maximum = 1, Value = _selectedColor.G };
            _gSlider.ValueChanged += UpdateColor;
            
            // B滑块
            var bLabel = new Label { Parent = this, Text = "B", X = 10, Y = 105, Width = 20, Height = 25, TextColor = Color.Blue };
            _bSlider = new Slider { Parent = this, X = 35, Y = 105, Width = 180, Height = 25, Minimum = 0, Maximum = 1, Value = _selectedColor.B };
            _bSlider.ValueChanged += UpdateColor;
            
            // 预览区
            _previewPanel = new Panel { Parent = this, X = 230, Y = 35, Width = 60, Height = 95, BackgroundColor = _selectedColor };
            
            // 确定按钮
            var okButton = new Button { Parent = this, Text = "确定", X = 70, Y = 145, Width = 70, Height = 30 };
            okButton.Clicked += () =>
            {
                ColorSelected?.Invoke(_selectedColor);
                Close();
            };
            
            // 取消按钮
            var cancelButton = new Button { Parent = this, Text = "取消", X = 160, Y = 145, Width = 70, Height = 30 };
            cancelButton.Clicked += Close;
        }
        
        private void UpdateColor()
        {
            _selectedColor = new Color(_rSlider.Value, _gSlider.Value, _bSlider.Value);
            if (_previewPanel != null)
            {
                _previewPanel.BackgroundColor = _selectedColor;
            }
        }
        
        public void Show(ContainerControl parent)
        {
            if (parent != null)
            {
                Parent = parent;
                // 居中显示
                X = (parent.Width - Width) / 2;
                Y = (parent.Height - Height) / 2;
                Visible = true;
                // Move to front by re-parenting (triggers re-add at end)
                if (parent is ContainerControl container)
                {
                    var index = container.Children.IndexOf(this);
                    if (index >= 0 && index < container.Children.Count - 1)
                    {
                        // Remove and re-add to move to front
                        container.Children.RemoveAt(index);
                        container.Children.Add(this);
                    }
                }
            }
        }
        
        public void Close()
        {
            Dispose();
        }
    }
}
