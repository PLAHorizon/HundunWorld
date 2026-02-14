using System;
using FlaxEngine;
using FlaxEngine.GUI;

namespace HundunWorld.UI.MetaHuman
{
    public class HairEditorPanel : ContainerControl
    {
        public event Action<Color> OnRootColorChanged;
        public event Action<Color> OnTipColorChanged;
        public event Action<float> OnRoughnessChanged;
        public event Action<float> OnAnisotropyChanged;
        public event Action<float> OnScatterIntensityChanged;
        public event Action<Color> OnScatterColorChanged;
        public event Action<float> OnDetailStrengthChanged;
        
        private Panel _scrollContent;
        private ColorPickerButton _rootColorPicker;
        private ColorPickerButton _tipColorPicker;
        private ColorPickerButton _scatterColorPicker;
        private Slider _roughnessSlider;
        private Slider _anisotropySlider;
        private Slider _scatterIntensitySlider;
        private Slider _detailStrengthSlider;
        
        private Label _roughnessValueLabel;
        private Label _anisotropyValueLabel;
        private Label _scatterIntensityValueLabel;
        private Label _detailStrengthValueLabel;
        
        private const float ItemSpacing = MetaHumanStyles.Sizes.ItemSpacing;
        private const float GroupSpacing = MetaHumanStyles.Sizes.GroupSpacing;
        private const float Padding = MetaHumanStyles.Sizes.Padding;
        
        public HairEditorPanel()
        {
            AnchorPreset = AnchorPresets.StretchAll;
            BackgroundColor = Color.Transparent;
            CreateUI();
        }
        
        private void CreateUI()
        {
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
            
            y = CreateSectionHeader("基础颜色", y);
            y = CreateColorRow("发根颜色", ref _rootColorPicker, y, OnRootColorChanged);
            y = CreateColorRow("发梢颜色", ref _tipColorPicker, y, OnTipColorChanged);
            y += ItemSpacing;
            
            y = CreateSectionHeader("材质属性", y);
            y = CreateRoughnessSlider(y);
            y = CreateAnisotropySlider(y);
            y += ItemSpacing;
            
            y = CreateSectionHeader("散射效果", y);
            y = CreateColorRow("散射颜色", ref _scatterColorPicker, y, OnScatterColorChanged);
            y = CreateScatterIntensitySlider(y);
            y += ItemSpacing;
            
            y = CreateSectionHeader("细节控制", y);
            y = CreateDetailStrengthSlider(y);
            
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
                Height = 32,
                BackgroundColor = MetaHumanStyles.Colors.SectionHeaderBackground
            };
            
            var leftBorder = new Panel
            {
                Parent = headerContainer,
                AnchorPreset = AnchorPresets.VerticalStretchLeft,
                Width = 3,
                BackgroundColor = MetaHumanStyles.Colors.Warning
            };
            
            var headerLabel = new Label
            {
                Parent = headerContainer,
                Text = title,
                X = 12,
                Y = 0,
                Width = headerContainer.Width - 16,
                Height = headerContainer.Height,
                TextColor = MetaHumanStyles.Colors.SectionHeader,
                VerticalAlignment = TextAlignment.Center,
                HorizontalAlignment = TextAlignment.Near
            };
            
            return y + headerContainer.Height + ItemSpacing;
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
            float buttonWidth = 72;
            float buttonHeight = MetaHumanStyles.Sizes.ButtonHeight;
            float spacing = ItemSpacing;
            float currentX = Padding;
            float rowY = y;
            
            var blackButton = CreatePresetButton("黑色", currentX, rowY, buttonWidth, buttonHeight,
                new Color(0.05f, 0.05f, 0.05f), Color.White, () => ApplyBlackHair());
            currentX += buttonWidth + spacing;
            
            var blondeButton = CreatePresetButton("金色", currentX, rowY, buttonWidth, buttonHeight,
                new Color(0.9f, 0.75f, 0.5f), MetaHumanStyles.Colors.TextSecondary, () => ApplyBlondeHair());
            currentX += buttonWidth + spacing;
            
            var brownButton = CreatePresetButton("棕色", currentX, rowY, buttonWidth, buttonHeight,
                new Color(0.4f, 0.25f, 0.15f), MetaHumanStyles.Colors.TextSecondary, () => ApplyBrownHair());
            currentX += buttonWidth + spacing;
            
            currentX = Padding;
            rowY += buttonHeight + spacing;
            
            var redButton = CreatePresetButton("红色", currentX, rowY, buttonWidth, buttonHeight,
                new Color(0.6f, 0.2f, 0.1f), MetaHumanStyles.Colors.TextSecondary, () => ApplyRedHair());
            currentX += buttonWidth + spacing;
            
            var whiteButton = CreatePresetButton("白色", currentX, rowY, buttonWidth, buttonHeight,
                new Color(0.95f, 0.95f, 0.95f), MetaHumanStyles.Colors.TextSecondary, () => ApplyWhiteHair());
            currentX += buttonWidth + spacing;
            
            var grayButton = CreatePresetButton("灰色", currentX, rowY, buttonWidth, buttonHeight,
                new Color(0.5f, 0.5f, 0.52f), MetaHumanStyles.Colors.TextSecondary, () => ApplyGrayHair());
            
            return rowY + buttonHeight + GroupSpacing;
        }
        
        private Button CreatePresetButton(string text, float x, float y, float width, float height, Color accentColor, Color textColor, Action onClick)
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
            
            var colorIndicator = new Panel
            {
                Parent = container,
                X = 4,
                Y = 4,
                Width = 6,
                Height = height - 8,
                BackgroundColor = accentColor
            };
            
            var button = new Button
            {
                Parent = container,
                X = 12,
                Y = 0,
                Width = width - 12,
                Height = height,
                Text = text,
                BackgroundColor = Color.Transparent,
                TextColor = textColor
            };
            button.Clicked += onClick;
            
            return button;
        }
        
        private float CreateColorRow(string label, ref ColorPickerButton colorPicker, float y, Action<Color> onChange)
        {
            float rowHeight = MetaHumanStyles.Sizes.RowHeight;
            
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
                X = 8,
                Y = 0,
                Width = MetaHumanStyles.Sizes.LabelWidth,
                Height = rowHeight,
                TextColor = MetaHumanStyles.Colors.TextSecondary,
                VerticalAlignment = TextAlignment.Center
            };
            
            colorPicker = new ColorPickerButton
            {
                Parent = rowContainer,
                X = rowContainer.Width - MetaHumanStyles.Sizes.ColorPickerWidth - 8,
                Y = (rowHeight - 24) / 2,
                Width = MetaHumanStyles.Sizes.ColorPickerWidth,
                Height = 24
            };
            colorPicker.ValueChanged += (color) => onChange?.Invoke(color);
            
            return y + rowHeight + ItemSpacing;
        }
        
        private float CreateRoughnessSlider(float y)
        {
            float rowHeight = MetaHumanStyles.Sizes.RowHeight;
            float defaultValue = 0.4f;
            
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
                X = 8,
                Y = 0,
                Width = MetaHumanStyles.Sizes.LabelWidth,
                Height = rowHeight,
                TextColor = MetaHumanStyles.Colors.TextSecondary,
                VerticalAlignment = TextAlignment.Center
            };
            
            _roughnessSlider = new Slider
            {
                Parent = rowContainer,
                X = MetaHumanStyles.Sizes.LabelWidth + 8,
                Y = (rowHeight - 20) / 2,
                Width = rowContainer.Width - MetaHumanStyles.Sizes.LabelWidth - MetaHumanStyles.Sizes.ValueLabelWidth - 24,
                Height = 20,
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
                X = rowContainer.Width - MetaHumanStyles.Sizes.ValueLabelWidth - 8,
                Y = 0,
                Width = MetaHumanStyles.Sizes.ValueLabelWidth,
                Height = rowHeight,
                TextColor = MetaHumanStyles.Colors.TextMuted,
                VerticalAlignment = TextAlignment.Center,
                HorizontalAlignment = TextAlignment.Far
            };
            
            return y + rowHeight + ItemSpacing;
        }
        
        private float CreateAnisotropySlider(float y)
        {
            float rowHeight = MetaHumanStyles.Sizes.RowHeight;
            float defaultValue = 0.6f;
            
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
                Text = "各向异性",
                X = 8,
                Y = 0,
                Width = MetaHumanStyles.Sizes.LabelWidth,
                Height = rowHeight,
                TextColor = MetaHumanStyles.Colors.TextSecondary,
                VerticalAlignment = TextAlignment.Center
            };
            
            _anisotropySlider = new Slider
            {
                Parent = rowContainer,
                X = MetaHumanStyles.Sizes.LabelWidth + 8,
                Y = (rowHeight - 20) / 2,
                Width = rowContainer.Width - MetaHumanStyles.Sizes.LabelWidth - MetaHumanStyles.Sizes.ValueLabelWidth - 24,
                Height = 20,
                Minimum = 0.0f,
                Maximum = 1.0f,
                Value = defaultValue
            };
            _anisotropySlider.ValueChanged += () =>
            {
                float val = _anisotropySlider.Value;
                _anisotropyValueLabel.Text = val.ToString("F2");
                OnAnisotropyChanged?.Invoke(val);
            };
            
            _anisotropyValueLabel = new Label
            {
                Parent = rowContainer,
                Text = defaultValue.ToString("F2"),
                X = rowContainer.Width - MetaHumanStyles.Sizes.ValueLabelWidth - 8,
                Y = 0,
                Width = MetaHumanStyles.Sizes.ValueLabelWidth,
                Height = rowHeight,
                TextColor = MetaHumanStyles.Colors.TextMuted,
                VerticalAlignment = TextAlignment.Center,
                HorizontalAlignment = TextAlignment.Far
            };
            
            return y + rowHeight + ItemSpacing;
        }
        
        private float CreateScatterIntensitySlider(float y)
        {
            float rowHeight = MetaHumanStyles.Sizes.RowHeight;
            float defaultValue = 0.3f;
            
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
                Text = "散射强度",
                X = 8,
                Y = 0,
                Width = MetaHumanStyles.Sizes.LabelWidth,
                Height = rowHeight,
                TextColor = MetaHumanStyles.Colors.TextSecondary,
                VerticalAlignment = TextAlignment.Center
            };
            
            _scatterIntensitySlider = new Slider
            {
                Parent = rowContainer,
                X = MetaHumanStyles.Sizes.LabelWidth + 8,
                Y = (rowHeight - 20) / 2,
                Width = rowContainer.Width - MetaHumanStyles.Sizes.LabelWidth - MetaHumanStyles.Sizes.ValueLabelWidth - 24,
                Height = 20,
                Minimum = 0.0f,
                Maximum = 1.0f,
                Value = defaultValue
            };
            _scatterIntensitySlider.ValueChanged += () =>
            {
                float val = _scatterIntensitySlider.Value;
                _scatterIntensityValueLabel.Text = val.ToString("F2");
                OnScatterIntensityChanged?.Invoke(val);
            };
            
            _scatterIntensityValueLabel = new Label
            {
                Parent = rowContainer,
                Text = defaultValue.ToString("F2"),
                X = rowContainer.Width - MetaHumanStyles.Sizes.ValueLabelWidth - 8,
                Y = 0,
                Width = MetaHumanStyles.Sizes.ValueLabelWidth,
                Height = rowHeight,
                TextColor = MetaHumanStyles.Colors.TextMuted,
                VerticalAlignment = TextAlignment.Center,
                HorizontalAlignment = TextAlignment.Far
            };
            
            return y + rowHeight + ItemSpacing;
        }
        
        private float CreateDetailStrengthSlider(float y)
        {
            float rowHeight = MetaHumanStyles.Sizes.RowHeight;
            float defaultValue = 0.5f;
            
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
                Text = "细节强度",
                X = 8,
                Y = 0,
                Width = MetaHumanStyles.Sizes.LabelWidth,
                Height = rowHeight,
                TextColor = MetaHumanStyles.Colors.TextSecondary,
                VerticalAlignment = TextAlignment.Center
            };
            
            _detailStrengthSlider = new Slider
            {
                Parent = rowContainer,
                X = MetaHumanStyles.Sizes.LabelWidth + 8,
                Y = (rowHeight - 20) / 2,
                Width = rowContainer.Width - MetaHumanStyles.Sizes.LabelWidth - MetaHumanStyles.Sizes.ValueLabelWidth - 24,
                Height = 20,
                Minimum = 0.0f,
                Maximum = 1.0f,
                Value = defaultValue
            };
            _detailStrengthSlider.ValueChanged += () =>
            {
                float val = _detailStrengthSlider.Value;
                _detailStrengthValueLabel.Text = val.ToString("F2");
                OnDetailStrengthChanged?.Invoke(val);
            };
            
            _detailStrengthValueLabel = new Label
            {
                Parent = rowContainer,
                Text = defaultValue.ToString("F2"),
                X = rowContainer.Width - MetaHumanStyles.Sizes.ValueLabelWidth - 8,
                Y = 0,
                Width = MetaHumanStyles.Sizes.ValueLabelWidth,
                Height = rowHeight,
                TextColor = MetaHumanStyles.Colors.TextMuted,
                VerticalAlignment = TextAlignment.Center,
                HorizontalAlignment = TextAlignment.Far
            };
            
            return y + rowHeight + ItemSpacing;
        }
        
        private void ApplyBlackHair()
        {
            _rootColorPicker.Value = new Color(0.05f, 0.05f, 0.05f);
            _tipColorPicker.Value = new Color(0.1f, 0.1f, 0.1f);
            _roughnessSlider.Value = 0.35f;
            _anisotropySlider.Value = 0.7f;
            OnRootColorChanged?.Invoke(_rootColorPicker.Value);
            OnTipColorChanged?.Invoke(_tipColorPicker.Value);
            OnRoughnessChanged?.Invoke(_roughnessSlider.Value);
            OnAnisotropyChanged?.Invoke(_anisotropySlider.Value);
        }
        
        private void ApplyBlondeHair()
        {
            _rootColorPicker.Value = new Color(0.7f, 0.55f, 0.35f);
            _tipColorPicker.Value = new Color(0.9f, 0.75f, 0.5f);
            _roughnessSlider.Value = 0.45f;
            _anisotropySlider.Value = 0.5f;
            OnRootColorChanged?.Invoke(_rootColorPicker.Value);
            OnTipColorChanged?.Invoke(_tipColorPicker.Value);
            OnRoughnessChanged?.Invoke(_roughnessSlider.Value);
            OnAnisotropyChanged?.Invoke(_anisotropySlider.Value);
        }
        
        private void ApplyBrownHair()
        {
            _rootColorPicker.Value = new Color(0.25f, 0.15f, 0.08f);
            _tipColorPicker.Value = new Color(0.4f, 0.25f, 0.15f);
            _roughnessSlider.Value = 0.4f;
            _anisotropySlider.Value = 0.6f;
            OnRootColorChanged?.Invoke(_rootColorPicker.Value);
            OnTipColorChanged?.Invoke(_tipColorPicker.Value);
            OnRoughnessChanged?.Invoke(_roughnessSlider.Value);
            OnAnisotropyChanged?.Invoke(_anisotropySlider.Value);
        }
        
        private void ApplyRedHair()
        {
            _rootColorPicker.Value = new Color(0.45f, 0.15f, 0.08f);
            _tipColorPicker.Value = new Color(0.6f, 0.2f, 0.1f);
            _roughnessSlider.Value = 0.5f;
            _anisotropySlider.Value = 0.4f;
            _scatterColorPicker.Value = new Color(0.8f, 0.3f, 0.2f);
            _scatterIntensitySlider.Value = 0.5f;
            OnRootColorChanged?.Invoke(_rootColorPicker.Value);
            OnTipColorChanged?.Invoke(_tipColorPicker.Value);
            OnRoughnessChanged?.Invoke(_roughnessSlider.Value);
            OnAnisotropyChanged?.Invoke(_anisotropySlider.Value);
            OnScatterColorChanged?.Invoke(_scatterColorPicker.Value);
            OnScatterIntensityChanged?.Invoke(_scatterIntensitySlider.Value);
        }
        
        private void ApplyWhiteHair()
        {
            _rootColorPicker.Value = new Color(0.85f, 0.85f, 0.88f);
            _tipColorPicker.Value = new Color(0.95f, 0.95f, 0.95f);
            _roughnessSlider.Value = 0.5f;
            _anisotropySlider.Value = 0.55f;
            OnRootColorChanged?.Invoke(_rootColorPicker.Value);
            OnTipColorChanged?.Invoke(_tipColorPicker.Value);
            OnRoughnessChanged?.Invoke(_roughnessSlider.Value);
            OnAnisotropyChanged?.Invoke(_anisotropySlider.Value);
        }
        
        private void ApplyGrayHair()
        {
            _rootColorPicker.Value = new Color(0.35f, 0.35f, 0.38f);
            _tipColorPicker.Value = new Color(0.5f, 0.5f, 0.52f);
            _roughnessSlider.Value = 0.48f;
            _anisotropySlider.Value = 0.5f;
            OnRootColorChanged?.Invoke(_rootColorPicker.Value);
            OnTipColorChanged?.Invoke(_tipColorPicker.Value);
            OnRoughnessChanged?.Invoke(_roughnessSlider.Value);
            OnAnisotropyChanged?.Invoke(_anisotropySlider.Value);
        }
        
        public void SetRootColor(Color color)
        {
            if (_rootColorPicker != null)
                _rootColorPicker.Value = color;
        }
        
        public void SetTipColor(Color color)
        {
            if (_tipColorPicker != null)
                _tipColorPicker.Value = color;
        }
        
        public void SetRoughness(float value)
        {
            if (_roughnessSlider != null)
                _roughnessSlider.Value = value;
        }
        
        public void SetAnisotropy(float value)
        {
            if (_anisotropySlider != null)
                _anisotropySlider.Value = value;
        }
    }
}
