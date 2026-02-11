using FlaxEngine;
using FlaxEngine.GUI;
using Horizon.Game.Core.Database;
using Horizon.Game.Message.Enums;
using HundunWorld.Game.UI.Components;
using HundunWorld.Game.UI.Layout;
using HundunWorld.Game.UI.StyleSystem;
using System;

namespace HundunWorld.Game.UI.Authentication
{
    /// <summary>
    /// 登录面板组件
    /// </summary>
    public class LoginPanel : RoundedPanel
    {
        // UI组件
        public ValidatedTextBox UsernameInput { get; private set; }
        public ValidatedTextBox PasswordInput { get; private set; }
        public Button LoginButton { get; private set; }
        public Button SwitchToRegisterButton { get; private set; }
        public CheckBox RememberPasswordCheckBox { get; private set; }
        public Label StatusLabel { get; private set; }
            
        // 事件
        public event Action<LoginPanel> LoginButtonClicked;
        public event Action<LoginPanel> SwitchToRegisterClicked;
        private LiteDataContext.PassportInfo _passortInfo;
        private bool _isInitialized = false;
        public bool IsInitialized => _isInitialized;
        public LoginPanel()
        {
            BackgroundColor = ChineseClassicalTheme.AccentColor;
            CornerRadius = 15f; // 设置较大的圆角
    
            // 同步初始化布局
            InitializeLayout();

            // 异步加载数据并填充UI
            InitializeUIAsync();
          HundunWorldGame.Instance.NetworkManager.ConnectionStatusChanged += OnConnectionStatusChanged;
        }
        private void OnConnectionStatusChanged(ConnectionStatus obj)
        {

            switch (obj)
            {
                case ConnectionStatus.Disconnected:
                    SetStatus("连接已断开", color: Color.Yellow);
                    DisableButtons();
                    break;
                case ConnectionStatus.Connecting:
                    SetStatus("正在连接中....", color: Color.Yellow);
                    DisableButtons();
                    break;
                case ConnectionStatus.Connected:
                    SetStatus($"{(!string.IsNullOrWhiteSpace(PasswordInput.Text) && !string.IsNullOrWhiteSpace(UsernameInput.Text) ? "" : "请入账号密码登录")}", color: Color.Green);
                    EnableButtons();
                    break;
                case ConnectionStatus.Reconnecting:
                    SetStatus("正在重连....", color: Color.Yellow);
                    DisableButtons();
                    break;
                case ConnectionStatus.Failed:
                case ConnectionStatus.Error:
                    SetStatus("网络连接失败，请检查网络", color: Color.DarkRed);
                    DisableButtons();
                    break;
                case ConnectionStatus.Unknown:
                case ConnectionStatus.GatewaySwitching:
                default:
                    break;
            }

        }
        /// <summary>
        /// 同步初始化布局 - 确保在任何分辨率下居中显示
        /// </summary>
        private void InitializeLayout()
        {
            // 计算面板尺寸
            Size = ChineseClassicalTheme.GoldenRatioLayout.CalculateLoginPanelSize();

            // 设置居中锚点和枢轴点
            AnchorMin = new Float2(0.5f, 0.5f);
            AnchorMax = new Float2(0.5f, 0.5f);
            Pivot = new Float2(0f, 0.5f);

            // 确保居中：回滚到补偿算法
            Location = new Float2(-Size.X / 2f, 0);
        }

        /// <summary>
        /// 异步初始化UI内容
        /// </summary>
        private async void InitializeUIAsync()
        {
            if (_isInitialized)
                return;

            _isInitialized = true;

            // 应用中式边框装饰
            ChineseClassicalTheme.ApplyChineseBorder(this, ChineseBorderStyle.Elegant);

            // 加载护照信息
            _passortInfo = await DatabaseManager.GetPassport();

            // 标题 - 居中显示，使用中式样式
            //var loginTitle = UIHelper.CreateTitleLabel("混沌世界", 20);
            var loginTitle = UIHelper.CreateIcon("Content/Textures/Logo.flax",128);
            
            loginTitle.AnchorPreset = AnchorPresets.Custom;
            loginTitle.Pivot = Float2.Zero;
            loginTitle.Location = new Float2(Size.X/2f - loginTitle.Size.X/2f, -20); ;
            
            ChineseClassicalTheme.ApplyVisualHierarchy(loginTitle, VisualHierarchy.Primary);
            AddChild(loginTitle);

            var currentY = 90f; // 调整起始垂直位置
            var inputSize = new Float2(ChineseClassicalTheme.GoldenRatioLayout.CalculateInputSize(Size.X).X, 35); // 增加高度至35px
            var spacing = 15f; // 优化间距
            var leftMargin = (Size.X - inputSize.X) / 3f; // 居中计算

            // 用户名区域 - 使用标准化间距和尺寴
            var usernameLabel = UIHelper.CreateLabel("用户名:");
            usernameLabel.Location = new Float2(leftMargin-10, currentY);
            usernameLabel.Size = new Float2(20, 30);
            usernameLabel.TextColor = ChineseClassicalTheme.TextColor;
            ChineseClassicalTheme.ApplyVisualHierarchy(usernameLabel, VisualHierarchy.Auxiliary);
            AddChild(usernameLabel);

            // currentY += 10 + 8; // 标签高度 + 小间距
            UsernameInput = ValidatedTextBox.CreateUsernameInput();
            UsernameInput.Location = new Float2(leftMargin + 46, currentY);
            UsernameInput.Size = inputSize;
            UsernameInput.BackgroundColor = ChineseClassicalTheme.InputColor;
            UsernameInput.TextColor = ChineseClassicalTheme.TextColor;
            UsernameInput.Text = _passortInfo?.PassportId;
            ChineseClassicalTheme.ApplyVisualHierarchy(UsernameInput, VisualHierarchy.Tertiary);
            AddChild(UsernameInput);

            currentY += inputSize.Y + spacing;

            // 密码区域 - 使用标准化间距和尺寴
            var passwordLabel = UIHelper.CreateLabel("密  码:");
            passwordLabel.Location = new Float2(leftMargin-10, currentY);
            passwordLabel.Size = new Float2(20, 30);
            passwordLabel.TextColor = ChineseClassicalTheme.TextColor;
            ChineseClassicalTheme.ApplyVisualHierarchy(passwordLabel, VisualHierarchy.Auxiliary);
            AddChild(passwordLabel);

            // currentY += 10 + 8;
            PasswordInput = ValidatedTextBox.CreatePasswordInput();
            PasswordInput.Location = new Float2(leftMargin + 46, currentY);
            PasswordInput.Size = inputSize;
            PasswordInput.BackgroundColor = ChineseClassicalTheme.InputColor;
            PasswordInput.TextColor = ChineseClassicalTheme.TextColor;
            PasswordInput.Text = _passortInfo?.RememberPassword ?? false ? _passortInfo.Password : "";
            ChineseClassicalTheme.ApplyVisualHierarchy(PasswordInput, VisualHierarchy.Tertiary);
            AddChild(PasswordInput);

            currentY += inputSize.Y + spacing - 8;

            // 记住密码区域 - 居中排列优化
            var rememberContainer = new RoundedPanel
            {
                Location = new Float2(leftMargin, currentY),
                Size = new Float2(inputSize.X, 30),
                BackgroundColor = Color.Transparent,
                CornerRadius = 5f
            };

            RememberPasswordCheckBox = new CheckBox
            {
                Location = new Float2(inputSize.X * 0.25f - 70, 5),
                Size = new Float2(20, 20),
                TooltipText = "记住密码",
                Checked = _passortInfo?.RememberPassword ?? false
            };

            var rememberLabel = UIHelper.CreateLabel("记住密码");
            rememberLabel.Location = new Float2(inputSize.X * 0.25f - 40, 2);
            rememberLabel.Size = new Float2(100, 25);
            rememberLabel.TextColor = ChineseClassicalTheme.TextColor;
            ChineseClassicalTheme.ApplyVisualHierarchy(rememberLabel, VisualHierarchy.Auxiliary);

            rememberContainer.AddChild(RememberPasswordCheckBox);
            rememberContainer.AddChild(rememberLabel);
            AddChild(rememberContainer);

            currentY += 10 + spacing;

            // 按钮区域 - 使用黄金比例尺寴并居中显示
            var primaryButtonSize = ChineseClassicalTheme.GoldenRatioLayout.CalculateButtonSize(ButtonType.Primary);
            var buttonX = (Size.X - primaryButtonSize.X) / 2f;

            LoginButton = UIHelper.CreatePrimaryButton("登录");
            LoginButton.Location = new Float2(buttonX - 130, currentY);
            LoginButton.Size = new Float2(primaryButtonSize.X + 120, primaryButtonSize.Y);
            LoginButton.BackgroundColor = ChineseClassicalTheme.SecondaryColor; // 古典金色
            LoginButton.BackgroundColorHighlighted = ChineseClassicalTheme.SuccessColor;
            LoginButton.BorderColorHighlighted = ChineseClassicalTheme.InputBackgroundColor;
            LoginButton.TextColor = Color.Black;
            LoginButton.ButtonClicked += OnLoginButtonClicked;
            // 不在这里设置禁用，由AuthenticationUI统一管理
            ChineseClassicalTheme.ApplyVisualHierarchy(LoginButton, VisualHierarchy.Primary);
            AddChild(LoginButton);

            //  currentY += primaryButtonSize.Y + spacing;

            // 切换到注册按钮 - 使用次要按钮样式
            var secondaryButtonSize = ChineseClassicalTheme.GoldenRatioLayout.CalculateButtonSize(ButtonType.Secondary);
            var secondaryButtonX = (Size.X - 120) / 2f; // 稍宽一些以适应文本

            SwitchToRegisterButton = UIHelper.CreateSecondaryButton("没有账户？注册");
            SwitchToRegisterButton.Location = new Float2(secondaryButtonX + 160, currentY);
            SwitchToRegisterButton.Size = new Float2(120, secondaryButtonSize.Y);
            SwitchToRegisterButton.BackgroundColor = ChineseClassicalTheme.BackgroundColor; // 墨青色
            SwitchToRegisterButton.TextColor = ChineseClassicalTheme.TextColor;
            SwitchToRegisterButton.ButtonClicked += OnSwitchToRegisterClicked;
            // 不在这里设置禁用，由AuthenticationUI统一管理
            ChineseClassicalTheme.ApplyVisualHierarchy(SwitchToRegisterButton, VisualHierarchy.Secondary);
            ChineseClassicalTheme.ApplyChineseBorder(SwitchToRegisterButton, ChineseBorderStyle.Traditional);
            AddChild(SwitchToRegisterButton);

            currentY += secondaryButtonSize.Y + spacing;

            // 状态标签 - 居中显示并使用中式样式
            StatusLabel = UIHelper.CreateLabel("请输入账户信息", ChineseClassicalTheme.SecondaryColor);
            StatusLabel.Location = new Float2(leftMargin, currentY);
            StatusLabel.Size = new Float2(inputSize.X, 30);
            StatusLabel.HorizontalAlignment = TextAlignment.Center;
            StatusLabel.TextColor = ChineseClassicalTheme.SecondaryColor;
            ChineseClassicalTheme.ApplyVisualHierarchy(StatusLabel, VisualHierarchy.Auxiliary);
            AddChild(StatusLabel);

            // 确保布局完成后重新定位
            InitializeLayout();
            
            // 检查当前网络连接状态并更新按钮
            CheckAndUpdateButtonState();
        }

        /// <summary>
        /// 刷新UI（用于响应尺寸变化）
        /// </summary>
        public void RefreshLayout()
        {
            if (!_isInitialized)
                return;

            // 只重新计算布局，不重新加载数据
            InitializeLayout();
        }

        /// <summary>
        /// 登录按钮点击事件
        /// </summary>
        private void OnLoginButtonClicked(Button sender)
        {
            LoginButtonClicked?.Invoke(this);
        }

        /// <summary>
        /// 切换到注册按钮点击事件
        /// </summary>
        private void OnSwitchToRegisterClicked(Button sender)
        {
            SwitchToRegisterClicked?.Invoke(this);
        }

        /// <summary>
        /// 验证输入
        /// </summary>
        public bool ValidateInput()
        {
            return UsernameInput.IsValid && PasswordInput.IsValid;
        }

        /// <summary>
        /// 获取登录信息
        /// </summary>
        public (string username, string password, bool rememberPassword) GetLoginInfo()
        {
            return (
                UsernameInput.Text?.Trim(),
                PasswordInput.Text,
                RememberPasswordCheckBox.Checked
            );
        }

        /// <summary>
        /// 设置状态消息
        /// </summary>
        public void SetStatus(string message, Color color)
        {
            FlaxEngine.Debug.Log($"[LoginPanel] 设置状态: {message}, 颜色: {color}");
            StatusLabel.Text = message;
            StatusLabel.TextColor = color;
            // 确保状态标签可见
            StatusLabel.Visible = true;
            StatusLabel.Enabled = true;
        }

        /// <summary>
        /// 重置表单
        /// </summary>
        public void ResetForm()
        {
            UsernameInput.Text = string.Empty;
            PasswordInput.Text = string.Empty;
            RememberPasswordCheckBox.Checked = false;
            SetStatus("请输入账户信息", Color.Yellow);
        }

        /// <summary>
        /// 启用按钮（网络连接建立后调用）
        /// </summary>
        public void EnableButtons()
        {
            FlaxEngine.Debug.Log($"[LoginPanel.EnableButtons] 开始执行, LoginButton={(LoginButton != null ? "not null" : "null")}, SwitchToRegisterButton={(SwitchToRegisterButton != null ? "not null" : "null")}");
            
            if (LoginButton != null)
            {
                FlaxEngine.Debug.Log($"[LoginPanel.EnableButtons] LoginButton当前状态: Enabled={LoginButton.Enabled}, Visible={LoginButton.Visible}");
                LoginButton.Enabled = true;
                LoginButton.Visible = true; // 强制可见
                FlaxEngine.Debug.Log($"[LoginPanel.EnableButtons] LoginButton设置后状态: Enabled={LoginButton.Enabled}, Visible={LoginButton.Visible}");
            }
            else
            {
                FlaxEngine.Debug.LogWarning("[LoginPanel.EnableButtons] LoginButton为null，无法启用");
            }
            
            if (SwitchToRegisterButton != null)
            {
                FlaxEngine.Debug.Log($"[LoginPanel.EnableButtons] SwitchToRegisterButton当前状态: Enabled={SwitchToRegisterButton.Enabled}, Visible={SwitchToRegisterButton.Visible}");
                SwitchToRegisterButton.Enabled = true;
                SwitchToRegisterButton.Visible = true; // 强制可见
                FlaxEngine.Debug.Log($"[LoginPanel.EnableButtons] SwitchToRegisterButton设置后状态: Enabled={SwitchToRegisterButton.Enabled}, Visible={SwitchToRegisterButton.Visible}");
            }
            else
            {
                FlaxEngine.Debug.LogWarning("[LoginPanel.EnableButtons] SwitchToRegisterButton为null，无法启用");
            }
            
            SetStatus("已连接服务器，请输入账户信息", Color.Green);
        }

        /// <summary>
        /// 禁用按钮（网络断开时调用）
        /// </summary>
        public void DisableButtons()
        {
            FlaxEngine.Debug.Log($"[LoginPanel.DisableButtons] 开始执行, LoginButton={(LoginButton != null ? "not null" : "null")}, SwitchToRegisterButton={(SwitchToRegisterButton != null ? "not null" : "null")}");
            
            if (LoginButton != null)
            {
                FlaxEngine.Debug.Log($"[LoginPanel.DisableButtons] LoginButton当前状态: Enabled={LoginButton.Enabled}");
                LoginButton.Enabled = false;
                FlaxEngine.Debug.Log($"[LoginPanel.DisableButtons] LoginButton设置后状态: Enabled={LoginButton.Enabled}");
            }
            else
            {
                FlaxEngine.Debug.LogWarning("[LoginPanel.DisableButtons] LoginButton为null，无法禁用");
            }
            
            if (SwitchToRegisterButton != null)
            {
                FlaxEngine.Debug.Log($"[LoginPanel.DisableButtons] SwitchToRegisterButton当前状态: Enabled={SwitchToRegisterButton.Enabled}");
                SwitchToRegisterButton.Enabled = false;
                FlaxEngine.Debug.Log($"[LoginPanel.DisableButtons] SwitchToRegisterButton设置后状态: Enabled={SwitchToRegisterButton.Enabled}");
            }
            else
            {
                FlaxEngine.Debug.LogWarning("[LoginPanel.DisableButtons] SwitchToRegisterButton为null，无法禁用");
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
                FlaxEngine.Debug.Log($"[LoginPanel] 检查网络状态: {status}");
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
        public override void OnDestroy()
        {
            
            // 清理自定义事件委托，断开外部订阅者的引用
            LoginButtonClicked = null;
            SwitchToRegisterClicked = null;

            
        }

        /// <summary>
        /// 创建登录面板实例
        /// </summary>
        public static LoginPanel Create()
        {
            return new LoginPanel();
        }
    }
}
