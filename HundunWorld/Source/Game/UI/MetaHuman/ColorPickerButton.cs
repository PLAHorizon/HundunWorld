using System;
using FlaxEngine;
using FlaxEngine.GUI;

namespace HundunWorld.UI.MetaHuman
{
    public class ColorPickerButton : ContainerControl
    {
        private Panel _colorPreview;
        private Button _button;
        private Color _value = Color.White;
        private ColorPickerPopup _popup;
        
        public event Action<Color> ValueChanged;
        
        public Color Value
        {
            get => _value;
            set
            {
                _value = value;
                if (_colorPreview != null)
                    _colorPreview.BackgroundColor = value;
            }
        }
        
        public ColorPickerButton()
        {
            Height = 24;
            Width = 70;
            BackgroundColor = Color.Transparent;
            
            _colorPreview = new Panel
            {
                Parent = this,
                AnchorPreset = AnchorPresets.StretchAll,
                Offsets = new Margin(2, 2, 2, 2),
                BackgroundColor = _value
            };
            
            _button = new Button
            {
                Parent = this,
                AnchorPreset = AnchorPresets.StretchAll,
                BackgroundColor = Color.Transparent,
                Text = ""
            };
            _button.Clicked += OnButtonClick;
        }
        
        private void OnButtonClick()
        {
            if (_popup != null && _popup.Visible)
            {
                _popup.Hide();
                return;
            }
            
            _popup = new ColorPickerPopup(_value);
            _popup.ColorChanged += OnColorChanged;
            _popup.Closed += OnPopupClosed;
            
            var root = Root;
            if (root != null)
            {
                Float2 pos = PointToWindow(Float2.Zero);
                Float2 size = new Float2(220, 280);  // 增加高度以容纳HSV控件和十六进制输入
                
                if (pos.X + size.X > root.Width)
                    pos.X = root.Width - size.X - 10;
                if (pos.Y + Height + size.Y > root.Height)
                    pos.Y = pos.Y - size.Y;
                else
                    pos.Y = pos.Y + Height;
                
                _popup.Location = pos;
                _popup.Size = size;
                
                root.AddChild(_popup);
                _popup.Show();
            }
        }
        
        private void OnColorChanged(Color color)
        {
            _value = color;
            _colorPreview.BackgroundColor = _value;
            ValueChanged?.Invoke(_value);
        }
        
        private void OnPopupClosed()
        {
            _popup = null;
        }
    }
    
    public class ColorPickerPopup : ContainerControl
    {
        private Slider _hSlider;  // Hue (色相)
        private Slider _sSlider;  // Saturation (饱和度)
        private Slider _vSlider;  // Value (明度)
        private Slider _aSlider;  // Alpha (透明度)
        private Label _hValue;
        private Label _sValue;
        private Label _vValue;
        private Label _aValue;
        private Panel _colorPreview;
        private TextBox _hexInput;
        private Color _color;
        
        public event Action<Color> ColorChanged;
        public event Action Closed;
        
        public ColorPickerPopup(Color initialColor)
        {
            _color = initialColor;
            BackgroundColor = MetaHumanStyles.Colors.BackgroundMedium;
            ClipChildren = true;
            
            CreateUI();
        }
        
        private void CreateUI()
        {
            float y = 10;
            float labelWidth = 40;
            float sliderWidth = 120;
            float valueWidth = 35;
            float startX = 10;
            float rowHeight = 26;
            
            var titleLabel = new Label
            {
                Parent = this,
                Text = "颜色选择器",
                X = startX,
                Y = y,
                Width = 200,
                Height = 20,
                TextColor = MetaHumanStyles.Colors.TextPrimary
            };
            y += 25;
            
            _colorPreview = new Panel
            {
                Parent = this,
                X = startX,
                Y = y,
                Width = 200,
                Height = 40,
                BackgroundColor = _color
            };
            y += 45;
            
            // 十六进制颜色输入
            var hexLabel = new Label
            {
                Parent = this,
                Text = "HEX:",
                X = startX,
                Y = y,
                Width = labelWidth,
                Height = rowHeight,
                TextColor = MetaHumanStyles.Colors.TextSecondary,
                VerticalAlignment = TextAlignment.Center
            };
            
            _hexInput = new TextBox
            {
                Parent = this,
                X = startX + labelWidth + 5,
                Y = y + 2,
                Width = sliderWidth + valueWidth + 5,
                Height = rowHeight - 4,
                Text = ColorToHex(_color),
                TextColor = MetaHumanStyles.Colors.TextPrimary,
                BackgroundColor = MetaHumanStyles.Colors.BackgroundDark
            };
            _hexInput.TextChanged += OnHexInputChanged;
            y += rowHeight + 8;
            
            // 转换RGB到HSV
            RGBToHSV(_color, out float h, out float s, out float v);
            
            _hSlider = CreateColorSlider("色相", startX, y, labelWidth, sliderWidth, valueWidth, rowHeight, out _hValue, h, 0, 360, "°");
            _hSlider.ValueChanged += UpdateColorFromHSV;
            y += rowHeight + 4;
            
            _sSlider = CreateColorSlider("饱和度", startX, y, labelWidth, sliderWidth, valueWidth, rowHeight, out _sValue, s, 0, 100, "%");
            _sSlider.ValueChanged += UpdateColorFromHSV;
            y += rowHeight + 4;
            
            _vSlider = CreateColorSlider("明度", startX, y, labelWidth, sliderWidth, valueWidth, rowHeight, out _vValue, v, 0, 100, "%");
            _vSlider.ValueChanged += UpdateColorFromHSV;
            y += rowHeight + 4;
            
            _aSlider = CreateColorSlider("透明度", startX, y, labelWidth, sliderWidth, valueWidth, rowHeight, out _aValue, _color.A, 0, 100, "%");
            _aSlider.ValueChanged += UpdateColorFromHSV;
            y += rowHeight + 12;
            
            var buttonPanel = new Panel
            {
                Parent = this,
                X = startX,
                Y = y,
                Width = 200,
                Height = 28,
                BackgroundColor = Color.Transparent
            };
            
            var confirmButton = new Button
            {
                Parent = buttonPanel,
                Text = "确定",
                X = 0,
                Y = 0,
                Width = 95,
                Height = 28,
                BackgroundColor = MetaHumanStyles.Colors.Primary,
                TextColor = MetaHumanStyles.Colors.TextPrimary
            };
            confirmButton.Clicked += Hide;
            
            var cancelButton = new Button
            {
                Parent = buttonPanel,
                Text = "取消",
                X = 105,
                Y = 0,
                Width = 95,
                Height = 28,
                BackgroundColor = MetaHumanStyles.Colors.BackgroundDark,
                TextColor = MetaHumanStyles.Colors.TextSecondary
            };
            cancelButton.Clicked += Hide;
        }
        
        private Slider CreateColorSlider(string label, float x, float y, float labelWidth, float sliderWidth, float valueWidth, float rowHeight, out Label valueLabel, float initialValue, float min, float max, string suffix)
        {
            var labelControl = new Label
            {
                Parent = this,
                Text = label,
                X = x,
                Y = y,
                Width = labelWidth,
                Height = rowHeight,
                TextColor = MetaHumanStyles.Colors.TextSecondary,
                VerticalAlignment = TextAlignment.Center
            };
            
            var slider = new Slider
            {
                Parent = this,
                X = x + labelWidth + 5,
                Y = y + 3,
                Width = sliderWidth,
                Height = rowHeight - 6,
                Minimum = min,
                Maximum = max,
                Value = initialValue
            };
            
            valueLabel = new Label
            {
                Parent = this,
                Text = $"{(int)initialValue}{suffix}",
                X = x + labelWidth + 5 + sliderWidth + 5,
                Y = y,
                Width = valueWidth,
                Height = rowHeight,
                TextColor = MetaHumanStyles.Colors.TextMuted,
                VerticalAlignment = TextAlignment.Center,
                HorizontalAlignment = TextAlignment.Far
            };
            
            return slider;
        }
        
        private void OnHexInputChanged()
        {
            string hex = _hexInput.Text.Trim();
            if (hex.StartsWith("#"))
                hex = hex.Substring(1);
            
            if (hex.Length == 6 || hex.Length == 8)
            {
                try
                {
                    int r = Convert.ToInt32(hex.Substring(0, 2), 16);
                    int g = Convert.ToInt32(hex.Substring(2, 2), 16);
                    int b = Convert.ToInt32(hex.Substring(4, 2), 16);
                    int a = hex.Length == 8 ? Convert.ToInt32(hex.Substring(6, 2), 16) : 255;
                    
                    _color = new Color(r / 255f, g / 255f, b / 255f, a / 255f);
                    _colorPreview.BackgroundColor = _color;
                    
                    // 更新HSV滑块
                    RGBToHSV(_color, out float h, out float s, out float v);
                    _hSlider.Value = h;
                    _sSlider.Value = s;
                    _vSlider.Value = v;
                    _aSlider.Value = a * 100f / 255f;
                    
                    UpdateValueLabels();
                    ColorChanged?.Invoke(_color);
                }
                catch
                {
                    // 无效的十六进制输入，忽略
                }
            }
        }
        
        private void UpdateColorFromHSV()
        {
            float h = _hSlider.Value;
            float s = _sSlider.Value / 100f;
            float v = _vSlider.Value / 100f;
            float a = _aSlider.Value / 100f;
            
            _color = HSVToRGB(h, s, v, a);
            _colorPreview.BackgroundColor = _color;
            
            // 更新十六进制输入
            _hexInput.Text = ColorToHex(_color);
            
            UpdateValueLabels();
            ColorChanged?.Invoke(_color);
        }
        
        private void UpdateValueLabels()
        {
            _hValue.Text = $"{(int)_hSlider.Value}°";
            _sValue.Text = $"{(int)_sSlider.Value}%";
            _vValue.Text = $"{(int)_vSlider.Value}%";
            _aValue.Text = $"{(int)_aSlider.Value}%";
        }
        
        private string ColorToHex(Color color)
        {
            int r = (int)(color.R * 255);
            int g = (int)(color.G * 255);
            int b = (int)(color.B * 255);
            int a = (int)(color.A * 255);
            return a < 255 ? $"#{r:X2}{g:X2}{b:X2}{a:X2}" : $"#{r:X2}{g:X2}{b:X2}";
        }
        
        // RGB转HSV
        private void RGBToHSV(Color color, out float h, out float s, out float v)
        {
            float r = color.R;
            float g = color.G;
            float b = color.B;
            
            float max = Math.Max(r, Math.Max(g, b));
            float min = Math.Min(r, Math.Min(g, b));
            float delta = max - min;
            
            // Hue
            if (delta == 0)
                h = 0;
            else if (max == r)
                h = 60 * (((g - b) / delta) % 6);
            else if (max == g)
                h = 60 * (((b - r) / delta) + 2);
            else
                h = 60 * (((r - g) / delta) + 4);
            
            if (h < 0) h += 360;
            
            // Saturation
            s = max == 0 ? 0 : (delta / max) * 100;
            
            // Value
            v = max * 100;
        }
        
        // HSV转RGB
        private Color HSVToRGB(float h, float s, float v, float a)
        {
            float c = v * s;
            float x = c * (1 - Math.Abs((h / 60) % 2 - 1));
            float m = v - c;
            
            float r, g, b;
            
            if (h < 60)
            {
                r = c; g = x; b = 0;
            }
            else if (h < 120)
            {
                r = x; g = c; b = 0;
            }
            else if (h < 180)
            {
                r = 0; g = c; b = x;
            }
            else if (h < 240)
            {
                r = 0; g = x; b = c;
            }
            else if (h < 300)
            {
                r = x; g = 0; b = c;
            }
            else
            {
                r = c; g = 0; b = x;
            }
            
            return new Color(r + m, g + m, b + m, a);
        }
        
        public void Show()
        {
            Visible = true;
            Focus();
        }
        
        public void Hide()
        {
            Visible = false;
            Closed?.Invoke();
            Dispose();
        }
        
        public override bool OnMouseDown(Float2 location, MouseButton button)
        {
            // 点击在弹窗内部，正常处理事件
            if (ContainsPoint(ref location))
            {
                return base.OnMouseDown(location, button);
            }
            
            // 点击在弹窗外部，隐藏弹窗
            Hide();
            return true;  // 消费事件，防止传递给下层控件
        }
        
        public override void OnLostFocus()
        {
            // 不要在失去焦点时立即隐藏，因为子控件（如滑块）获得焦点时会触发此事件
            // 用户需要能够正常操作滑块
            base.OnLostFocus();
        }
    }
}
