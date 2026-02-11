using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.Components;
using HundunWorld.Game.UI.Layout;
using HundunWorld.Game.UI.StyleSystem;
using System;

namespace HundunWorld.Game.UI.Authentication
{
    /// <summary>
    /// 注册面板组件
    /// </summary>
    public class RegisterPanel : RoundedPanel
    {
        private bool _isInitialized = false;
        public bool IsInitialized => _isInitialized;
        // UI组件
        public ValidatedTextBox UsernameInput { get; private set; }
        public ValidatedTextBox PasswordInput { get; private set; }
        public ValidatedTextBox ConfirmPasswordInput { get; private set; }
        public ValidatedTextBox EmailInput { get; private set; }
        public ValidatedTextBox PhoneInput { get; private set; }
        public TextBox VerificationCodeInput { get; private set; }
        public Button RegisterButton { get; private set; }
        public Button SwitchToLoginButton { get; private set; }
        public Button SendVerificationCodeButton { get; private set; }
        public Label StatusLabel { get; private set; }
            
        // 事件
        public event Action<RegisterPanel> RegisterButtonClicked;
        public event Action<RegisterPanel> SwitchToLoginClicked;
        public event Action<RegisterPanel> SendVerificationCodeClicked;
            
        public RegisterPanel()
        {
            CornerRadius = 15f; // 设置较大的圆角
            // 同步初始化布局
            InitializeLayout();
            
            InitializeUI();
        }

        /// <summary>
        /// 同步初始化布局 - 确保在任何分辨率下居中显示
        /// </summary>
        private void InitializeLayout()
        {
            // 使用黄金比例计算优化尺寸，注册面板需要更大的垂直空间
            Size = ChineseClassicalTheme.GoldenRatioLayout.CalculateRegisterPanelSize();
            
            // 设置居中锚点和枢轴点
            AnchorMin = new Float2(0.5f, 0.5f);
            AnchorMax = new Float2(0.5f, 0.5f);
            Pivot = new Float2(0f, 0.5f);

            // 确保居中：回滚到补偿算法
            Location = new Float2(-Size.X / 2f, 0);
        }

        /// <summary>
        /// 刷新布局（用于响应尺寸变化）
        /// </summary>
        public void RefreshLayout()
        {
            InitializeLayout();
        }

        /// <summary>
        /// 初始化UI
        /// </summary>
        private void InitializeUI()
        {
            if (_isInitialized)
                return;

            _isInitialized = true;
            BackgroundColor = UIHelper.PanelColor;

            // 标题 - 居中显示
            var registerTitle = UIHelper.CreateTitleLabel("混沌世界 - 注册", 20);
            registerTitle.Location = new Float2(0, 20);
            registerTitle.Size = new Float2(Size.X, 30); // 将宽度设置为面板全宽
            registerTitle.HorizontalAlignment = TextAlignment.Center; // 设置文本水平居中
            AddChild(registerTitle);

            // 设置列宽和间距
            float margin = 10; // 大幅减少边距以消除左侧留白
            float columnSpacing = 25; // 保持适度的列间距
            float labelWidth = 65; // 缩减标签宽度
            float totalWidth = Size.X;
            float availableWidth = (totalWidth - margin * 2 - columnSpacing);
            float inputWidth = (availableWidth / 2) - labelWidth; // 重新计算输入框宽度
            
            float leftColumnX = margin;
            float rightColumnX = margin + (availableWidth / 2) + columnSpacing;
            
            float rowHeight = 45; // 稍微减少行高以适应大输入框
            float inputHeight = 35; // 保持输入框高度以确保操作性
            float currentY = 65; // 调整起始Y坐标
            // 第一行：用户名(左) 和 密码(右)
            // 左列：用户名标签和输入框
            var usernameLabel = UIHelper.CreateLabel("昵称:");
            usernameLabel.Location = new Float2(leftColumnX, currentY + 5);
            usernameLabel.Size = new Float2(labelWidth, 25);
            usernameLabel.HorizontalAlignment = TextAlignment.Far; 
            AddChild(usernameLabel);

            UsernameInput = ValidatedTextBox.CreateUsernameInput();
            UsernameInput.Location = new Float2(leftColumnX + labelWidth + 2, currentY);
            UsernameInput.Size = new Float2(inputWidth, inputHeight);
            AddChild(UsernameInput);

            // 右列：密码标签和输入框
            var passwordLabel = UIHelper.CreateLabel("密码:");
            passwordLabel.Location = new Float2(rightColumnX, currentY + 5);
            passwordLabel.Size = new Float2(labelWidth, 25);
            passwordLabel.HorizontalAlignment = TextAlignment.Far;
            AddChild(passwordLabel);

            PasswordInput = ValidatedTextBox.CreatePasswordInput();
            PasswordInput.Location = new Float2(rightColumnX + labelWidth + 2, currentY);
            PasswordInput.Size = new Float2(inputWidth, inputHeight);
            AddChild(PasswordInput);

            currentY += rowHeight;

            // 第二行：确认密码(左) 和 邮箱(右)
            // 左列：确认密码标签和输入框
            var confirmPasswordLabel = UIHelper.CreateLabel("确认:");
            confirmPasswordLabel.Location = new Float2(leftColumnX, currentY + 5);
            confirmPasswordLabel.Size = new Float2(labelWidth, 25);
            confirmPasswordLabel.HorizontalAlignment = TextAlignment.Far;
            AddChild(confirmPasswordLabel);

            ConfirmPasswordInput = new ValidatedTextBox
            {
                WatermarkText = "再次输入密码",
                IsPassword = true,
                Location = new Float2(leftColumnX + labelWidth + 2, currentY),
                Size = new Float2(inputWidth, inputHeight)
            };
            ConfirmPasswordInput.SetValidator(text =>
            {
                if (string.IsNullOrWhiteSpace(text))
                    return (false, "请确认密码");
                if (text != PasswordInput.Text)
                    return (false, "两次输入不一致");
                return (true, "");
            });
            AddChild(ConfirmPasswordInput);

            // 右列：邮箱标签和输入框
            var emailLabel = UIHelper.CreateLabel("邮箱:");
            emailLabel.Location = new Float2(rightColumnX, currentY + 5);
            emailLabel.Size = new Float2(labelWidth, 25);
            emailLabel.HorizontalAlignment = TextAlignment.Far;
            AddChild(emailLabel);

            EmailInput = ValidatedTextBox.CreateEmailInput();
            EmailInput.Location = new Float2(rightColumnX + labelWidth + 2, currentY);
            EmailInput.Size = new Float2(inputWidth, inputHeight);
            AddChild(EmailInput);

            currentY += rowHeight;

            // 第三行：手机号(左) 和 验证码(右)
            // 左列：手机号标签和输入框
            var phoneLabel = UIHelper.CreateLabel("手机:");
            phoneLabel.Location = new Float2(leftColumnX, currentY + 5);
            phoneLabel.Size = new Float2(labelWidth, 25);
            phoneLabel.HorizontalAlignment = TextAlignment.Far;
            AddChild(phoneLabel);

            PhoneInput = new ValidatedTextBox
            {
                WatermarkText = "可选",
                Location = new Float2(leftColumnX + labelWidth + 2, currentY),
                Size = new Float2(inputWidth, inputHeight)
            };
            PhoneInput.SetValidator(text =>
            {
                if (string.IsNullOrWhiteSpace(text))
                    return (true, ""); 
                if (text.Length != 11)
                    return (false, "应为11位");
                return (true, "");
            });
            AddChild(PhoneInput);

            // 右列：验证码标签和输入框
            var verificationCodeLabel = UIHelper.CreateLabel("验证码:");
            verificationCodeLabel.Location = new Float2(rightColumnX, currentY + 5);
            verificationCodeLabel.Size = new Float2(labelWidth, 25);
            verificationCodeLabel.HorizontalAlignment = TextAlignment.Far;
            AddChild(verificationCodeLabel);

            var verificationContainer = new RoundedPanel
            {
                Location = new Float2(rightColumnX + labelWidth + 2, currentY),
                Size = new Float2(inputWidth, inputHeight),
                BackgroundColor = Color.Transparent,
                CornerRadius = 5f
            };

            float codeInputWidth = inputWidth * 0.55f;
            VerificationCodeInput = UIHelper.CreateTextBox("验证码");
            VerificationCodeInput.Location = Float2.Zero;
            VerificationCodeInput.Size = new Float2(codeInputWidth, inputHeight);
            verificationContainer.AddChild(VerificationCodeInput);

            SendVerificationCodeButton = UIHelper.CreateButton("发送", UIHelper.InfoColor);
            SendVerificationCodeButton.Location = new Float2(codeInputWidth + 5, 0);
            SendVerificationCodeButton.Size = new Float2(inputWidth - codeInputWidth - 5, inputHeight);
            SendVerificationCodeButton.ButtonClicked += OnSendVerificationCodeClicked;
            SendVerificationCodeButton.Enabled = false; // 初始化时禁用，等待网络连接
            verificationContainer.AddChild(SendVerificationCodeButton);
            
            AddChild(verificationContainer);

            currentY += rowHeight + 15;

            // 按钮区域 - 居中排列
            float buttonWidth = totalWidth * 0.7f; // 相对宽度
            float buttonX = (totalWidth - buttonWidth) / 2;

            RegisterButton = UIHelper.CreatePrimaryButton("注册");
            RegisterButton.Location = new Float2(buttonX, currentY);
            RegisterButton.Size = new Float2(buttonWidth, 40);
            RegisterButton.BackgroundColorHighlighted = ChineseClassicalTheme.SuccessColor;
            RegisterButton.BorderColorHighlighted = ChineseClassicalTheme.InputBackgroundColor;
            RegisterButton.ButtonClicked += OnRegisterButtonClicked;
            RegisterButton.Enabled = false; // 初始化时禁用，等待网络连接
            AddChild(RegisterButton);

            currentY += 50;

            // 返回登录按钮
            SwitchToLoginButton = UIHelper.CreateSecondaryButton("已有账户？登录");
            SwitchToLoginButton.Location = new Float2(buttonX, currentY);
            SwitchToLoginButton.Size = new Float2(buttonWidth, 35);
            SwitchToLoginButton.ButtonClicked += OnSwitchToLoginClicked;
            SwitchToLoginButton.Enabled = false; // 初始化时禁用，等待网络连接
            AddChild(SwitchToLoginButton);

            currentY += 45;

            // 状态标签 - 居中显示
            StatusLabel = UIHelper.CreateLabel("请输入注册信息", Color.Yellow);
            StatusLabel.Location = new Float2(0, currentY);
            StatusLabel.Size = new Float2(totalWidth, 20);
            StatusLabel.HorizontalAlignment = TextAlignment.Center;
            AddChild(StatusLabel);
            
            // 检查当前网络连接状态并更新按钮
            CheckAndUpdateButtonState();
        }

        /// <summary>
        /// 注册按钮点击事件
        /// </summary>
        private void OnRegisterButtonClicked(Button sender)
        {
            RegisterButtonClicked?.Invoke(this);
        }

        /// <summary>
        /// 切换到登录按钮点击事件
        /// </summary>
        private void OnSwitchToLoginClicked(Button sender)
        {
            SwitchToLoginClicked?.Invoke(this);
        }

        /// <summary>
        /// 发送验证码按钮点击事件
        /// </summary>
        private void OnSendVerificationCodeClicked(Button sender)
        {
            SendVerificationCodeClicked?.Invoke(this);
        }

        /// <summary>
        /// 验证输入
        /// </summary>
        public bool ValidateInput()
        {
            return UsernameInput.IsValid && PasswordInput.IsValid &&
                   ConfirmPasswordInput.IsValid && EmailInput.IsValid &&
                   PhoneInput.IsValid;
        }

        /// <summary>
        /// 获取注册信息
        /// </summary>
        public (string username, string password, string email, string phone, string verificationCode) GetRegisterInfo()
        {
            return (
                UsernameInput.Text?.Trim(),
                PasswordInput.Text,
                EmailInput.Text?.Trim(),
                PhoneInput.Text?.Trim(),
                VerificationCodeInput.Text?.Trim()
            );
        }

        /// <summary>
        /// 设置状态信息
        /// </summary>
        public void SetStatus(string message, Color color)
        {
            StatusLabel.Text = message;
            StatusLabel.TextColor = color;
        }

        /// <summary>
        /// 重置表单
        /// </summary>
        public void ResetForm()
        {
            UsernameInput.Text = string.Empty;
            PasswordInput.Text = string.Empty;
            ConfirmPasswordInput.Text = string.Empty;
            EmailInput.Text = string.Empty;
            PhoneInput.Text = string.Empty;
            VerificationCodeInput.Text = string.Empty;
            SetStatus("请输入注册信息", Color.Yellow);
        }

        /// <summary>
        /// 开始验证码倒计时
        /// </summary>
        public void StartVerificationCodeCountdown()
        {
            SendVerificationCodeButton.Enabled = false;
        }

        /// <summary>
        /// 结束验证码倒计时
        /// </summary>
        public void EndVerificationCodeCountdown()
        {
            SendVerificationCodeButton.Text = "发送验证码";
            SendVerificationCodeButton.Enabled = true;
        }

        /// <summary>
        /// 更新验证码倒计时显示
        /// </summary>
        public void UpdateVerificationCodeCountdown(int seconds)
        {
            SendVerificationCodeButton.Text = $"{seconds}秒后重发";
        }

        /// <summary>
        /// 启用按钮（网络连接建立后调用）
        /// </summary>
        public void EnableButtons()
        {
            if (RegisterButton != null)
            {
                RegisterButton.Enabled = true;
            }
            if (SwitchToLoginButton != null)
            {
                SwitchToLoginButton.Enabled = true;
            }
            if (SendVerificationCodeButton != null)
            {
                SendVerificationCodeButton.Enabled = true;
            }
            SetStatus("已连接服务器，请输入注册信息", Color.Green);
        }

        /// <summary>
        /// 禁用按钮（网络断开时调用）
        /// </summary>
        public void DisableButtons()
        {
            if (RegisterButton != null)
            {
                RegisterButton.Enabled = false;
            }
            if (SwitchToLoginButton != null)
            {
                SwitchToLoginButton.Enabled = false;
            }
            if (SendVerificationCodeButton != null)
            {
                SendVerificationCodeButton.Enabled = false;
            }
            SetStatus("正在连接服务器...", Color.Yellow);
        }

        /// <summary>
        /// 检查并更新按钮状态（在按钮创建后调用）
        /// </summary>
        private void CheckAndUpdateButtonState()
        {
            var networkManager = HundunWorldGame.Instance?.NetworkManager;
            if (networkManager != null)
            {
                var status = networkManager.GetConnectionStatus();
                FlaxEngine.Debug.Log($"[RegisterPanel] 检查网络状态: {status}");
                if (status == Horizon.Game.Message.Enums.ConnectionStatus.Connected)
                {
                    EnableButtons();
                }
                else
                {
                    DisableButtons();
                }
            }
        }
        
        /// <summary>
        /// 释放资源 - 取消按钮事件订阅，清理自定义事件委托
        /// </summary>
        public override void Dispose()
        {
            // 取消按钮事件订阅，防止重复触发和内存泄漏
            if (SendVerificationCodeButton != null)
            {
                SendVerificationCodeButton.ButtonClicked -= OnSendVerificationCodeClicked;
            }
            if (RegisterButton != null)
            {
                RegisterButton.ButtonClicked -= OnRegisterButtonClicked;
            }
            if (SwitchToLoginButton != null)
            {
                SwitchToLoginButton.ButtonClicked -= OnSwitchToLoginClicked;
            }

            // 清理自定义事件委托，断开外部订阅者的引用
            RegisterButtonClicked = null;
            SwitchToLoginClicked = null;
            SendVerificationCodeClicked = null;

            base.Dispose();
        }

        /// <summary>
        /// 创建注册面板实例
        /// </summary>
        public static RegisterPanel Create()
        {
            return new RegisterPanel();
        }
    }
}
