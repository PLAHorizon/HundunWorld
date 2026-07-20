using System;
using FlaxEngine;
using FlaxEngine.GUI;

namespace HundunWorld.Game.UI.Components
{
    /// <summary>
    /// 验证输入框组件
    /// 支持实时验证和错误提示
    /// </summary>
    public class ValidatedTextBox : ContainerControl
    {
        private TextBox _textBox;
        private Label _errorLabel;
        private Func<string, (bool isValid, string errorMessage)> _validator;

        public string Text
        {
            get => _textBox?.Text ?? "";
            set { if (_textBox != null) _textBox.Text = value; }
        }

        public string WatermarkText
        {
            get => _textBox?.WatermarkText ?? "";
            set { if (_textBox != null) _textBox.WatermarkText = value; }
        }

        public bool IsPassword
        {
            get => _textBox?.ObfuscateText ?? false;
            set { if (_textBox != null) _textBox.ObfuscateText = value; }
        }

        /// <summary>
        /// 获取或设置文本颜色
        /// </summary>
        public Color TextColor
        {
            get => _textBox?.TextColor ?? Color.White;
            set { if (_textBox != null) _textBox.TextColor = value; }
        }

        /// <summary>
        /// 获取或设置输入框背景颜色
        /// </summary>
        public Color InputBackgroundColor
        {
            get => _textBox?.BackgroundColor ?? Color.White;
            set { if (_textBox != null) _textBox.BackgroundColor = value; }
        }

        public event Action<string> TextChanged;
        public event Action<bool> ValidationChanged;

        private bool _isValid = true;
        public bool IsValid => _isValid;

        public ValidatedTextBox() : base()
        {
            BackgroundColor = Color.Transparent;
            SetupComponents();
        }

        private void SetupComponents()
        {
            // 主输入框 - 占据控件全部高度
            _textBox = new TextBox
            {
                Parent = this,
                AnchorPreset = AnchorPresets.HorizontalStretchTop,
                Offsets = Margin.Zero,
                BackgroundColor = new Color(0.1f, 0.1f, 0.12f, 0.8f),
                TextColor = new Color(0.85f, 0.85f, 0.9f),
                Height = 30,
                Font = UIHelper.DefaultFont,
            };
            _textBox.TextChanged += OnTextChanged;

            // 错误提示标签 - 在输入框下方
            _errorLabel = new Label
            {
                Parent = this,
                AnchorPreset = AnchorPresets.HorizontalStretchTop,
                Y = 32,
                Height = 18,
                TextColor = new Color(0.9f, 0.3f, 0.3f),
                HorizontalAlignment = TextAlignment.Near,
                Visible = false,
                Font = UIHelper.SetFont(size: 10),
            };

            // 默认高度：仅输入框高度
            Height = 30;
        }

        /// <summary>
        /// 当控件大小改变时调整子控件布局
        /// </summary>
        protected override void OnSizeChanged()
        {
            base.OnSizeChanged();

            if (_textBox != null)
            {
                // 输入框高度为控件高度或30（取较小值）
                float textBoxHeight = Mathf.Min(Height, 30f);
                _textBox.Height = textBoxHeight;

                // 错误标签位置
                if (_errorLabel != null)
                {
                    _errorLabel.Y = textBoxHeight + 2;
                    _errorLabel.Visible = !_isValid && !string.IsNullOrEmpty(_errorLabel.Text);
                }
            }
        }

        public void SetValidator(Func<string, (bool isValid, string errorMessage)> validator)
        {
            _validator = validator;
            ValidateInput();
        }

        private void OnTextChanged()
        {
            ValidateInput();
            TextChanged?.Invoke(Text);
        }

        private void ValidateInput()
        {
            if (_validator == null)
            {
                SetValidationState(true, "");
                return;
            }

            var result = _validator(Text);
            SetValidationState(result.isValid, result.errorMessage);
        }

        private void SetValidationState(bool isValid, string errorMessage)
        {
            if (_isValid != isValid)
            {
                _isValid = isValid;
                ValidationChanged?.Invoke(_isValid);
            }

            _errorLabel.Text = errorMessage;
            _errorLabel.Visible = !isValid && !string.IsNullOrEmpty(errorMessage);

            if (_textBox != null)
            {
                _textBox.BorderColor = isValid ? new Color(0.3f, 0.3f, 0.35f) : new Color(0.9f, 0.3f, 0.3f);
            }
        }

        public void SetValidationError(string errorMessage)
        {
            SetValidationState(false, errorMessage);
        }

        public void ClearValidationError()
        {
            SetValidationState(true, "");
        }

        public static ValidatedTextBox CreateUsernameInput()
        {
            var input = new ValidatedTextBox
            {
                WatermarkText = "请输入昵称"
            };
            input.SetValidator(text =>
            {
                if (string.IsNullOrWhiteSpace(text))
                    return (false, "昵称不能为空");
                if (text.Length < 3)
                    return (false, "昵称至少3个字符");
                if (text.Length > 20)
                    return (false, "昵称最多20个字符");
                return (true, "");
            });
            return input;
        }

        public static ValidatedTextBox CreatePasswordInput()
        {
            var input = new ValidatedTextBox
            {
                WatermarkText = "请输入密码",
                IsPassword = true,
            };
            input.SetValidator(text =>
            {
                if (string.IsNullOrWhiteSpace(text))
                    return (false, "密码不能为空");
                if (text.Length < 5)
                    return (false, "密码至少5个字符");
                if (text.Length > 32)
                    return (false, "密码最多32个字符");
                return (true, "");
            });
            return input;
        }

        public static ValidatedTextBox CreateEmailInput()
        {
            var input = new ValidatedTextBox
            {
                WatermarkText = "请输入邮箱地址"
            };
            input.SetValidator(text =>
            {
                if (string.IsNullOrWhiteSpace(text))
                    return (false, "邮箱不能为空");
                if (!text.Contains("@") || !text.Contains("."))
                    return (false, "邮箱格式不正确");
                return (true, "");
            });
            return input;
        }
    }
}
