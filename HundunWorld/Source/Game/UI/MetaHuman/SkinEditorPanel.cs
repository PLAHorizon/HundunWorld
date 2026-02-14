using System;
using FlaxEngine;
using FlaxEngine.GUI;

namespace HundunWorld.UI.MetaHuman
{
    public class SkinEditorPanel : ContainerControl
    {
        public event Action<Color> OnBaseColorChanged;
        public event Action<float> OnRoughnessChanged;
        public event Action<float> OnSSSIntensityChanged;
        public event Action<Color> OnEpidermisColorChanged;
        public event Action<Color> OnDermisColorChanged;
        public event Action<Color> OnSubcutisColorChanged;
        
        private Panel _scrollContent;
        private Slider _roughnessSlider;
        private Slider _sssIntensitySlider;
        private ColorPickerButton _baseColorPicker;
        private ColorPickerButton _epidermisColorPicker;
        private ColorPickerButton _dermisColorPicker;
        private ColorPickerButton _subcutisColorPicker;
        
        private Label _roughnessValueLabel;
        private Label _sssValueLabel;
        
        private const float ItemSpacing = MetaHumanStyles.Sizes.ItemSpacing;
        private const float GroupSpacing = MetaHumanStyles.Sizes.GroupSpacing;
        private const float Padding = MetaHumanStyles.Sizes.Padding;
        
        public SkinEditorPanel()
        {
            AnchorPreset = AnchorPresets.StretchAll;
            BackgroundColor = Color.Transparent;
            CreateUI();
        }
        
        private void CreateUI()
        {
            // 使用固定的面板宽度（与左侧面板宽度匹配）
            float panelWidth = 300;  // 固定宽度
            
            var scrollPanel = new Panel
            {
                Parent = this,
                AnchorPreset = AnchorPresets.StretchAll,
                BackgroundColor = Color.Transparent,
                ScrollBars = ScrollBars.Vertical
            };
            
            _scrollContent = new Panel
            {
                Parent = scrollPanel,
                X = 0,
                Y = 0,
                Width = panelWidth,
                Height = 800,
                BackgroundColor = Color.Transparent
            };
            
            float y = 0;
            
            y = CreateSectionHeader("快速预设", y);
            y = CreateQuickPresetButtons(y);
            y = CreateSeparator(y);
            
            y = CreateSectionHeader("基础皮肤", y);
            y = CreateColorRow("基础颜色", ref _baseColorPicker, y, OnBaseColorChanged);
            y = CreateRoughnessSlider(y);
            y += ItemSpacing;
            
            y = CreateSectionHeader("次表面散射 (SSS)", y);
            y = CreateSSSIntensitySlider(y);
            y += ItemSpacing;
            
            y = CreateSectionHeader("皮肤层级颜色", y);
            y = CreateColorRow("表皮层", ref _epidermisColorPicker, y, OnEpidermisColorChanged);
            y = CreateColorRow("真皮层", ref _dermisColorPicker, y, OnDermisColorChanged);
            y = CreateColorRow("皮下组织", ref _subcutisColorPicker, y, OnSubcutisColorChanged);
            
            y = CreateSectionHeader("皮肤细节", y);
            y = CreateDetailControls(y);
            
            _scrollContent.Height = y + Padding;
        }
        
        private float CreateSectionHeader(string title, float y)
        {
            var headerContainer = new Panel
            {
                Parent = _scrollContent,
                X = 0,
                Y = y,
                Width = _scrollContent.Width,
                Height = 26,  // 更紧凑的标题高度
                BackgroundColor = MetaHumanStyles.Colors.SectionHeaderBackground
            };
            
            var leftBorder = new Panel
            {
                Parent = headerContainer,
                AnchorPreset = AnchorPresets.VerticalStretchLeft,
                Width = 3,
                BackgroundColor = MetaHumanStyles.Colors.Primary
            };
            
            var headerLabel = new Label
            {
                Parent = headerContainer,
                Text = title,
                X = 10,
                Y = 0,
                Width = headerContainer.Width - 16,
                Height = headerContainer.Height,
                TextColor = MetaHumanStyles.Colors.SectionHeader,
                VerticalAlignment = TextAlignment.Center,
                HorizontalAlignment = TextAlignment.Near
            };
            
            return y + headerContainer.Height + 6;  // 更紧凑的间距
        }
        
        private float CreateSeparator(float y)
        {
            var separator = new Panel
            {
                Parent = _scrollContent,
                X = Padding,
                Y = y,
                Width = _scrollContent.Width - Padding * 2,
                Height = 1,
                BackgroundColor = MetaHumanStyles.Colors.Separator
            };
            
            return y + separator.Height + GroupSpacing;
        }
        
        private float CreateQuickPresetButtons(float y)
        {
            // 优化后的布局参数
            float buttonWidth = 60;  // 按钮更紧凑
            float buttonHeight = 28;  // 稍微矮一点
            float spacing = 8;  // 统一间距
            float contentWidth = _scrollContent.Width - Padding * 2;
            
            // 计算每行可容纳的按钮数量（4个一行）
            float totalWidth = buttonWidth * 4 + spacing * 3;
            float startX = Padding + (contentWidth - totalWidth) / 2;  // 居中
            
            float currentX = startX;
            float rowY = y + 4;  // 顶部留点空间
            
            // 第一行：肤色预设
            currentX = CreatePresetButton("亚洲", currentX, rowY, buttonWidth, buttonHeight, 
                new Color(0.9f, 0.75f, 0.6f), () => ApplyAsianSkin()) + spacing;
            
            currentX = CreatePresetButton("欧洲", currentX, rowY, buttonWidth, buttonHeight,
                new Color(1.0f, 0.85f, 0.75f), () => ApplyEuropeanSkin()) + spacing;
            
            currentX = CreatePresetButton("非洲", currentX, rowY, buttonWidth, buttonHeight,
                new Color(0.4f, 0.28f, 0.2f), () => ApplyAfricanSkin()) + spacing;
            
            CreatePresetButton("混血", currentX, rowY, buttonWidth, buttonHeight,
                new Color(0.75f, 0.55f, 0.4f), () => { });  // 占位
            
            // 第二行：肤质预设
            currentX = startX;
            rowY += buttonHeight + spacing;
            
            currentX = CreatePresetButton("年轻", currentX, rowY, buttonWidth, buttonHeight,
                new Color(1.0f, 0.92f, 0.88f), () => ApplyYoungSkin()) + spacing;
            
            currentX = CreatePresetButton("成熟", currentX, rowY, buttonWidth, buttonHeight,
                new Color(0.95f, 0.8f, 0.7f), () => ApplyMatureSkin()) + spacing;
            
            currentX = CreatePresetButton("油性", currentX, rowY, buttonWidth, buttonHeight,
                new Color(0.85f, 0.75f, 0.7f), () => ApplyOilySkin()) + spacing;
            
            CreatePresetButton("干燥", currentX, rowY, buttonWidth, buttonHeight,
                new Color(0.9f, 0.82f, 0.78f), () => ApplyDrySkin());
            
            return rowY + buttonHeight + GroupSpacing + 4;
        }
        
        private float CreatePresetButton(string text, float x, float y, float width, float height, Color accentColor, Action onClick)
        {
            var container = new Panel
            {
                Parent = _scrollContent,
                X = x,
                Y = y,
                Width = width,
                Height = height,
                BackgroundColor = MetaHumanStyles.Colors.BackgroundElevated
            };
            
            // 左侧颜色指示条
            var colorIndicator = new Panel
            {
                Parent = container,
                X = 2,
                Y = 3,
                Width = 4,
                Height = height - 6,
                BackgroundColor = accentColor
            };
            
            var button = new Button
            {
                Parent = container,
                X = 8,
                Y = 0,
                Width = width - 8,
                Height = height,
                Text = text,
                BackgroundColor = Color.Transparent,
                TextColor = MetaHumanStyles.Colors.TextSecondary
            };
            button.Clicked += onClick;
            
            return x + width;  // 返回下一个按钮的起始X位置
        }
        
        private float CreateColorRow(string label, ref ColorPickerButton colorPicker, float y, Action<Color> onChange)
        {
            float rowHeight = 32;  // 更紧凑的行高
            float colorPickerWidth = 60;
            float colorPickerHeight = 22;
            
            var rowContainer = new Panel
            {
                Parent = _scrollContent,
                X = Padding,
                Y = y,
                Width = _scrollContent.Width - Padding * 2,
                Height = rowHeight,
                BackgroundColor = MetaHumanStyles.Colors.BackgroundLight
            };
            
            var labelControl = new Label
            {
                Parent = rowContainer,
                Text = label,
                X = 10,
                Y = 0,
                Width = 80,  // 更紧凑的标签宽度
                Height = rowHeight,
                TextColor = MetaHumanStyles.Colors.TextSecondary,
                VerticalAlignment = TextAlignment.Center
            };
            
            colorPicker = new ColorPickerButton
            {
                Parent = rowContainer,
                X = rowContainer.Width - colorPickerWidth - 10,
                Y = (rowHeight - colorPickerHeight) / 2,
                Width = colorPickerWidth,
                Height = colorPickerHeight
            };
            colorPicker.ValueChanged += (color) => onChange?.Invoke(color);
            
            return y + rowHeight + 4;  // 更紧凑的间距
        }
        
        private float CreateRoughnessSlider(float y)
        {
            float rowHeight = 32;  // 更紧凑的行高
            float defaultValue = 0.5f;
            float labelWidth = 60;
            float valueWidth = 45;
            
            var rowContainer = new Panel
            {
                Parent = _scrollContent,
                X = Padding,
                Y = y,
                Width = _scrollContent.Width - Padding * 2,
                Height = rowHeight,
                BackgroundColor = MetaHumanStyles.Colors.BackgroundLight
            };
            
            var labelControl = new Label
            {
                Parent = rowContainer,
                Text = "粗糙度",
                X = 10,
                Y = 0,
                Width = labelWidth,
                Height = rowHeight,
                TextColor = MetaHumanStyles.Colors.TextSecondary,
                VerticalAlignment = TextAlignment.Center
            };
            
            _roughnessSlider = new Slider
            {
                Parent = rowContainer,
                X = labelWidth + 15,
                Y = (rowHeight - 18) / 2,
                Width = rowContainer.Width - labelWidth - valueWidth - 35,
                Height = 18,
                Minimum = 0.0f,
                Maximum = 1.0f,
                Value = defaultValue
            };
            _roughnessSlider.ValueChanged += () =>
            {
                float val = _roughnessSlider.Value;
                _roughnessValueLabel.Text = val.ToString("F2");
                OnRoughnessChanged?.Invoke(val);
            };
            
            _roughnessValueLabel = new Label
            {
                Parent = rowContainer,
                Text = defaultValue.ToString("F2"),
                X = rowContainer.Width - valueWidth - 10,
                Y = 0,
                Width = valueWidth,
                Height = rowHeight,
                TextColor = MetaHumanStyles.Colors.TextMuted,
                VerticalAlignment = TextAlignment.Center,
                HorizontalAlignment = TextAlignment.Far
            };
            
            return y + rowHeight + 4;
        }
        
        private float CreateSSSIntensitySlider(float y)
        {
            float rowHeight = 32;
            float defaultValue = 0.6f;
            float labelWidth = 70;
            float valueWidth = 45;
            
            var rowContainer = new Panel
            {
                Parent = _scrollContent,
                X = Padding,
                Y = y,
                Width = _scrollContent.Width - Padding * 2,
                Height = rowHeight,
                BackgroundColor = MetaHumanStyles.Colors.BackgroundLight
            };
            
            var labelControl = new Label
            {
                Parent = rowContainer,
                Text = "SSS强度",
                X = 10,
                Y = 0,
                Width = labelWidth,
                Height = rowHeight,
                TextColor = MetaHumanStyles.Colors.TextSecondary,
                VerticalAlignment = TextAlignment.Center
            };
            
            _sssIntensitySlider = new Slider
            {
                Parent = rowContainer,
                X = labelWidth + 15,
                Y = (rowHeight - 18) / 2,
                Width = rowContainer.Width - labelWidth - valueWidth - 35,
                Height = 18,
                Minimum = 0.0f,
                Maximum = 1.0f,
                Value = defaultValue
            };
            _sssIntensitySlider.ValueChanged += () =>
            {
                float val = _sssIntensitySlider.Value;
                _sssValueLabel.Text = val.ToString("F2");
                OnSSSIntensityChanged?.Invoke(val);
            };
            
            _sssValueLabel = new Label
            {
                Parent = rowContainer,
                Text = defaultValue.ToString("F2"),
                X = rowContainer.Width - valueWidth - 10,
                Y = 0,
                Width = valueWidth,
                Height = rowHeight,
                TextColor = MetaHumanStyles.Colors.TextMuted,
                VerticalAlignment = TextAlignment.Center,
                HorizontalAlignment = TextAlignment.Far
            };
            
            return y + rowHeight + 4;
        }
        
        private float CreateDetailControls(float y)
        {
            y = CreateDetailSlider("毛孔大小", 0.0f, 1.0f, 0.3f, y, null);
            y = CreateDetailSlider("皱纹强度", 0.0f, 1.0f, 0.0f, y, null);
            y = CreateDetailSlider("雀斑强度", 0.0f, 1.0f, 0.0f, y, null);
            
            return y + 4;
        }
        
        private float CreateDetailSlider(string label, float min, float max, float defaultValue, float y, Action<float> onChange)
        {
            float rowHeight = 32;  // 统一行高
            float labelWidth = 70;
            float valueWidth = 45;
            
            var rowContainer = new Panel
            {
                Parent = _scrollContent,
                X = Padding,
                Y = y,
                Width = _scrollContent.Width - Padding * 2,
                Height = rowHeight,
                BackgroundColor = MetaHumanStyles.Colors.BackgroundLight
            };
            
            var labelControl = new Label
            {
                Parent = rowContainer,
                Text = label,
                X = 10,
                Y = 0,
                Width = labelWidth,
                Height = rowHeight,
                TextColor = MetaHumanStyles.Colors.TextSecondary,
                VerticalAlignment = TextAlignment.Center
            };
            
            var slider = new Slider
            {
                Parent = rowContainer,
                X = labelWidth + 15,
                Y = (rowHeight - 18) / 2,
                Width = rowContainer.Width - labelWidth - valueWidth - 35,
                Height = 18,
                Minimum = min,
                Maximum = max,
                Value = defaultValue
            };
            
            var valueLabel = new Label
            {
                Parent = rowContainer,
                Text = defaultValue.ToString("F2"),
                X = rowContainer.Width - valueWidth - 10,
                Y = 0,
                Width = valueWidth,
                Height = rowHeight,
                TextColor = MetaHumanStyles.Colors.TextMuted,
                VerticalAlignment = TextAlignment.Center,
                HorizontalAlignment = TextAlignment.Far
            };
            
            slider.ValueChanged += () =>
            {
                float val = slider.Value;
                valueLabel.Text = val.ToString("F2");
                onChange?.Invoke(val);
            };
            
            return y + rowHeight + 4;  // 更紧凑的间距
        }
        
        private void ApplyAsianSkin()
        {
            _baseColorPicker.Value = new Color(0.9f, 0.75f, 0.6f);
            _roughnessSlider.Value = 0.45f;
            _sssIntensitySlider.Value = 0.55f;
            OnBaseColorChanged?.Invoke(_baseColorPicker.Value);
            OnRoughnessChanged?.Invoke(_roughnessSlider.Value);
            OnSSSIntensityChanged?.Invoke(_sssIntensitySlider.Value);
        }
        
        private void ApplyEuropeanSkin()
        {
            _baseColorPicker.Value = new Color(1.0f, 0.85f, 0.75f);
            _roughnessSlider.Value = 0.5f;
            _sssIntensitySlider.Value = 0.5f;
            OnBaseColorChanged?.Invoke(_baseColorPicker.Value);
            OnRoughnessChanged?.Invoke(_roughnessSlider.Value);
            OnSSSIntensityChanged?.Invoke(_sssIntensitySlider.Value);
        }
        
        private void ApplyAfricanSkin()
        {
            _baseColorPicker.Value = new Color(0.4f, 0.28f, 0.2f);
            _roughnessSlider.Value = 0.35f;
            _sssIntensitySlider.Value = 0.7f;
            OnBaseColorChanged?.Invoke(_baseColorPicker.Value);
            OnRoughnessChanged?.Invoke(_roughnessSlider.Value);
            OnSSSIntensityChanged?.Invoke(_sssIntensitySlider.Value);
        }
        
        private void ApplyYoungSkin()
        {
            _roughnessSlider.Value = 0.3f;
            _sssIntensitySlider.Value = 0.7f;
            OnRoughnessChanged?.Invoke(_roughnessSlider.Value);
            OnSSSIntensityChanged?.Invoke(_sssIntensitySlider.Value);
        }
        
        private void ApplyMatureSkin()
        {
            _roughnessSlider.Value = 0.55f;
            _sssIntensitySlider.Value = 0.4f;
            OnRoughnessChanged?.Invoke(_roughnessSlider.Value);
            OnSSSIntensityChanged?.Invoke(_sssIntensitySlider.Value);
        }
        
        private void ApplyOilySkin()
        {
            _roughnessSlider.Value = 0.2f;
            OnRoughnessChanged?.Invoke(_roughnessSlider.Value);
        }
        
        private void ApplyDrySkin()
        {
            _roughnessSlider.Value = 0.65f;
            OnRoughnessChanged?.Invoke(_roughnessSlider.Value);
        }
        
        public void SetBaseColor(Color color)
        {
            if (_baseColorPicker != null)
                _baseColorPicker.Value = color;
        }
        
        public void SetRoughness(float value)
        {
            if (_roughnessSlider != null)
                _roughnessSlider.Value = value;
        }
        
        public void SetSSSIntensity(float value)
        {
            if (_sssIntensitySlider != null)
                _sssIntensitySlider.Value = value;
        }
    }
}
