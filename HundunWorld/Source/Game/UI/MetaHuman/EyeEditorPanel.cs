using System;
using FlaxEngine;
using FlaxEngine.GUI;

namespace HundunWorld.UI.MetaHuman
{
    public class EyeEditorPanel : ContainerControl
    {
        public event Action<Color> OnIrisColorChanged;
        public event Action<float> OnPupilSizeChanged;
        public event Action<float> OnEyeWetnessChanged;
        public event Action<Color> OnScleraColorChanged;
        public event Action<float> OnIrisSizeChanged;
        public event Action<float> OnCorneaRoughnessChanged;
        
        private Panel _scrollContent;
        private ColorPickerButton _irisColorPicker;
        private ColorPickerButton _scleraColorPicker;
        private Slider _pupilSizeSlider;
        private Slider _eyeWetnessSlider;
        private Slider _irisSizeSlider;
        private Slider _corneaRoughnessSlider;
        
        private Label _pupilSizeValueLabel;
        private Label _wetnessValueLabel;
        private Label _irisSizeValueLabel;
        private Label _corneaRoughnessValueLabel;
        
        private const float ItemSpacing = MetaHumanStyles.Sizes.ItemSpacing;
        private const float GroupSpacing = MetaHumanStyles.Sizes.GroupSpacing;
        private const float Padding = MetaHumanStyles.Sizes.Padding;
        
        public EyeEditorPanel()
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
                Height = 700,
                BackgroundColor = Color.Transparent
            };
            
            float y = 0;
            
            y = CreateSectionHeader("快速预设", y);
            y = CreateQuickPresetButtons(y);
            y = CreateSeparator(y);
            
            y = CreateSectionHeader("虹膜 (Iris)", y);
            y = CreateColorRow("虹膜颜色", ref _irisColorPicker, y, OnIrisColorChanged);
            y = CreateIrisSizeSlider(y);
            y += ItemSpacing;
            
            y = CreateSectionHeader("瞳孔 (Pupil)", y);
            y = CreatePupilSizeSlider(y);
            y += ItemSpacing;
            
            y = CreateSectionHeader("巩膜 (Sclera)", y);
            y = CreateColorRow("巩膜颜色", ref _scleraColorPicker, y, OnScleraColorChanged);
            y += ItemSpacing;
            
            y = CreateSectionHeader("角膜 (Cornea)", y);
            y = CreateCorneaRoughnessSlider(y);
            y = CreateEyeWetnessSlider(y);
            
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
                BackgroundColor = MetaHumanStyles.Colors.Accent
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
            
            var blueButton = CreatePresetButton("蓝色", currentX, rowY, buttonWidth, buttonHeight,
                new Color(0.3f, 0.5f, 0.8f), () => ApplyBlueEyes());
            currentX += buttonWidth + spacing;
            
            var brownButton = CreatePresetButton("棕色", currentX, rowY, buttonWidth, buttonHeight,
                new Color(0.5f, 0.35f, 0.2f), () => ApplyBrownEyes());
            currentX += buttonWidth + spacing;
            
            var greenButton = CreatePresetButton("绿色", currentX, rowY, buttonWidth, buttonHeight,
                new Color(0.3f, 0.55f, 0.35f), () => ApplyGreenEyes());
            currentX += buttonWidth + spacing;
            
            currentX = Padding;
            rowY += buttonHeight + spacing;
            
            var grayButton = CreatePresetButton("灰色", currentX, rowY, buttonWidth, buttonHeight,
                new Color(0.5f, 0.52f, 0.55f), () => ApplyGrayEyes());
            currentX += buttonWidth + spacing;
            
            var hazelButton = CreatePresetButton("琥珀", currentX, rowY, buttonWidth, buttonHeight,
                new Color(0.7f, 0.5f, 0.3f), () => ApplyHazelEyes());
            currentX += buttonWidth + spacing;
            
            var violetButton = CreatePresetButton("紫色", currentX, rowY, buttonWidth, buttonHeight,
                new Color(0.5f, 0.35f, 0.6f), () => ApplyVioletEyes());
            
            return rowY + buttonHeight + GroupSpacing;
        }
        
        private Button CreatePresetButton(string text, float x, float y, float width, float height, Color accentColor, Action onClick)
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
                TextColor = MetaHumanStyles.Colors.TextSecondary
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
        
        private float CreateIrisSizeSlider(float y)
        {
            float rowHeight = MetaHumanStyles.Sizes.RowHeight;
            float defaultValue = 1.0f;
            
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
                Text = "虹膜大小",
                X = 8,
                Y = 0,
                Width = MetaHumanStyles.Sizes.LabelWidth,
                Height = rowHeight,
                TextColor = MetaHumanStyles.Colors.TextSecondary,
                VerticalAlignment = TextAlignment.Center
            };
            
            _irisSizeSlider = new Slider
            {
                Parent = rowContainer,
                X = MetaHumanStyles.Sizes.LabelWidth + 8,
                Y = (rowHeight - 20) / 2,
                Width = rowContainer.Width - MetaHumanStyles.Sizes.LabelWidth - MetaHumanStyles.Sizes.ValueLabelWidth - 24,
                Height = 20,
                Minimum = 0.5f,
                Maximum = 1.5f,
                Value = defaultValue
            };
            _irisSizeSlider.ValueChanged += () =>
            {
                float val = _irisSizeSlider.Value;
                _irisSizeValueLabel.Text = val.ToString("F2");
                OnIrisSizeChanged?.Invoke(val);
            };
            
            _irisSizeValueLabel = new Label
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
        
        private float CreatePupilSizeSlider(float y)
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
                Text = "瞳孔大小",
                X = 8,
                Y = 0,
                Width = MetaHumanStyles.Sizes.LabelWidth,
                Height = rowHeight,
                TextColor = MetaHumanStyles.Colors.TextSecondary,
                VerticalAlignment = TextAlignment.Center
            };
            
            _pupilSizeSlider = new Slider
            {
                Parent = rowContainer,
                X = MetaHumanStyles.Sizes.LabelWidth + 8,
                Y = (rowHeight - 20) / 2,
                Width = rowContainer.Width - MetaHumanStyles.Sizes.LabelWidth - MetaHumanStyles.Sizes.ValueLabelWidth - 24,
                Height = 20,
                Minimum = 0.2f,
                Maximum = 1.0f,
                Value = defaultValue
            };
            _pupilSizeSlider.ValueChanged += () =>
            {
                float val = _pupilSizeSlider.Value;
                _pupilSizeValueLabel.Text = val.ToString("F2");
                OnPupilSizeChanged?.Invoke(val);
            };
            
            _pupilSizeValueLabel = new Label
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
        
        private float CreateCorneaRoughnessSlider(float y)
        {
            float rowHeight = MetaHumanStyles.Sizes.RowHeight;
            float defaultValue = 0.1f;
            
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
                Text = "角膜粗糙度",
                X = 8,
                Y = 0,
                Width = MetaHumanStyles.Sizes.LabelWidth,
                Height = rowHeight,
                TextColor = MetaHumanStyles.Colors.TextSecondary,
                VerticalAlignment = TextAlignment.Center
            };
            
            _corneaRoughnessSlider = new Slider
            {
                Parent = rowContainer,
                X = MetaHumanStyles.Sizes.LabelWidth + 8,
                Y = (rowHeight - 20) / 2,
                Width = rowContainer.Width - MetaHumanStyles.Sizes.LabelWidth - MetaHumanStyles.Sizes.ValueLabelWidth - 24,
                Height = 20,
                Minimum = 0.0f,
                Maximum = 0.5f,
                Value = defaultValue
            };
            _corneaRoughnessSlider.ValueChanged += () =>
            {
                float val = _corneaRoughnessSlider.Value;
                _corneaRoughnessValueLabel.Text = val.ToString("F2");
                OnCorneaRoughnessChanged?.Invoke(val);
            };
            
            _corneaRoughnessValueLabel = new Label
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
        
        private float CreateEyeWetnessSlider(float y)
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
                Text = "眼睛湿润度",
                X = 8,
                Y = 0,
                Width = MetaHumanStyles.Sizes.LabelWidth,
                Height = rowHeight,
                TextColor = MetaHumanStyles.Colors.TextSecondary,
                VerticalAlignment = TextAlignment.Center
            };
            
            _eyeWetnessSlider = new Slider
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
            _eyeWetnessSlider.ValueChanged += () =>
            {
                float val = _eyeWetnessSlider.Value;
                _wetnessValueLabel.Text = val.ToString("F2");
                OnEyeWetnessChanged?.Invoke(val);
            };
            
            _wetnessValueLabel = new Label
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
        
        private void ApplyBlueEyes()
        {
            _irisColorPicker.Value = new Color(0.3f, 0.5f, 0.8f);
            _scleraColorPicker.Value = new Color(0.95f, 0.95f, 0.97f);
            OnIrisColorChanged?.Invoke(_irisColorPicker.Value);
            OnScleraColorChanged?.Invoke(_scleraColorPicker.Value);
        }
        
        private void ApplyBrownEyes()
        {
            _irisColorPicker.Value = new Color(0.5f, 0.35f, 0.2f);
            _scleraColorPicker.Value = new Color(0.95f, 0.95f, 0.97f);
            OnIrisColorChanged?.Invoke(_irisColorPicker.Value);
            OnScleraColorChanged?.Invoke(_scleraColorPicker.Value);
        }
        
        private void ApplyGreenEyes()
        {
            _irisColorPicker.Value = new Color(0.3f, 0.55f, 0.35f);
            _scleraColorPicker.Value = new Color(0.95f, 0.95f, 0.97f);
            OnIrisColorChanged?.Invoke(_irisColorPicker.Value);
            OnScleraColorChanged?.Invoke(_scleraColorPicker.Value);
        }
        
        private void ApplyGrayEyes()
        {
            _irisColorPicker.Value = new Color(0.5f, 0.52f, 0.55f);
            _scleraColorPicker.Value = new Color(0.95f, 0.95f, 0.97f);
            OnIrisColorChanged?.Invoke(_irisColorPicker.Value);
            OnScleraColorChanged?.Invoke(_scleraColorPicker.Value);
        }
        
        private void ApplyHazelEyes()
        {
            _irisColorPicker.Value = new Color(0.7f, 0.5f, 0.3f);
            _scleraColorPicker.Value = new Color(0.95f, 0.95f, 0.97f);
            OnIrisColorChanged?.Invoke(_irisColorPicker.Value);
            OnScleraColorChanged?.Invoke(_scleraColorPicker.Value);
        }
        
        private void ApplyVioletEyes()
        {
            _irisColorPicker.Value = new Color(0.5f, 0.35f, 0.6f);
            _scleraColorPicker.Value = new Color(0.95f, 0.95f, 0.97f);
            OnIrisColorChanged?.Invoke(_irisColorPicker.Value);
            OnScleraColorChanged?.Invoke(_scleraColorPicker.Value);
        }
        
        public void SetIrisColor(Color color)
        {
            if (_irisColorPicker != null)
                _irisColorPicker.Value = color;
        }
        
        public void SetPupilSize(float value)
        {
            if (_pupilSizeSlider != null)
                _pupilSizeSlider.Value = value;
        }
        
        public void SetEyeWetness(float value)
        {
            if (_eyeWetnessSlider != null)
                _eyeWetnessSlider.Value = value;
        }
    }
}
