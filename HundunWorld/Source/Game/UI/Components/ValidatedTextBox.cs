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
        /// 获取或设置背景颜色
        /// </summary>
        public new Color BackgroundColor
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
            SetupComponents();
        }
        
        /// <summary>
        /// 创建验证文本框的静态方法
        /// </summary>
        public static ValidatedTextBox Create()
        {
            return new ValidatedTextBox();
        }
        
        private void SetupComponents()
        {
            // 主输入框
            _textBox = new TextBox
            {
                AnchorPreset = AnchorPresets.HorizontalStretchTop,
                BackgroundColor = Color.White,
                TextColor = Color.Black,
                Height = 30,
                Font=UIHelper.DefaultFont,
            };
            _textBox.TextChanged += OnTextChanged;
            AddChild(_textBox);
            
            // 错误提示标签
            _errorLabel = new Label
            {
                AnchorPreset = AnchorPresets.HorizontalStretchTop,
                Y = 35,
                Height = 20,
                TextColor = Color.Red,
                HorizontalAlignment = TextAlignment.Near,
                Visible = false,
                Font = UIHelper.DefaultFont,
            };
            AddChild(_errorLabel);
            
            Height = 60;
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
            
            _textBox.BorderColor = isValid ? Color.Gray : Color.Red;
        }
        
        /// <summary>
        /// 设置验证错误
        /// </summary>
        public void SetValidationError(string errorMessage)
        {
            SetValidationState(false, errorMessage);
        }
        
        /// <summary>
        /// 清除验证错误
        /// </summary>
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
