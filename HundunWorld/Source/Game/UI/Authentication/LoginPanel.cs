using FlaxEngine;
using FlaxEngine.GUI;
using Game.Database;
using Horizon.Game.Message.Enums;
using HundunWorld.Game.UI.Components;
using HundunWorld.Game.UI.Layout;
using HundunWorld.Game.UI.StyleSystem;
using System;

namespace HundunWorld.Game.UI.Authentication
{
    /// <summary>
    /// 登录面板组件 — 浅色「水墨青绿」全屏三栏布局。
    /// 100% 还原设计稿 hundun-login/pages/login.html：
    ///   1. 全屏水墨背景 + 遮罩；2. 左侧品牌意境区（印章/游戏名/水墨笔触/诗句/灵光粒子）；
    ///   3. 右侧毛玻璃登录卡（欢迎语/底线输入/密码显隐/记住密码+自动登录/进入江湖/注册-找回密码/状态）；
    ///   4. 底部辅助栏（服务器状态点+文案/版本号/版权）。
    /// 仅重构表现层；公共成员、事件、输入验证、网络连接回调、自动登录、注册切换等业务逻辑保持不变。
    /// </summary>
    public class LoginPanel : RoundedPanel
    {
        // ===== 业务公共成员（AuthenticationController 依赖，勿改签名） =====
        public ValidatedTextBox UsernameInput { get; private set; }
        public ValidatedTextBox PasswordInput { get; private set; }
        public Button LoginButton { get; private set; }
        public Button SwitchToRegisterButton { get; private set; }
        public CheckBox RememberPasswordCheckBox { get; private set; }
        public Label StatusLabel { get; private set; }

        public event Action<LoginPanel> LoginButtonClicked;
        public event Action<LoginPanel> SwitchToRegisterClicked;
        private LiteDataContext.PassportInfo _passortInfo;
        private bool _isInitialized = false;
        public bool IsInitialized => _isInitialized;

        // ===== 装饰/布局字段（仅表现层） =====
        private Image _bgImage;
        private LoginBackdropLayer _bgOverlay;
        private ContainerControl _content;
        private Panel _brandZone;
        private RoundedPanel _seal;
        private Label _sealText;
        private Label _gameTitle;
        private LoginInkStroke _inkStroke;
        private Label _poem;
        private LoginFloatParticle _particle1;
        private LoginFloatParticle _particle2;
        private LoginFloatParticle _particle3;
        private LoginCardShadow _cardShadow;
        private LoginCardPanel _card;
        private Label _welcomeTitle;
        private Label _welcomeSub;
        private Label _accountLabel;
        private Label _passwordLabel;
        private LoginUnderline _accountUnderline;
        private LoginUnderline _passwordUnderline;
        private Button _eyeButton;
        private CheckBox _autoLoginCheckBox;
        private Label _rememberLabel;
        private Label _autoLoginLabel;
        private Button _forgotButton;
        private Panel _footer;
        private LoginPulseDot _statusDot;
        private Label _serverLabel;
        private Label _versionLabel;
        private Label _copyrightLabel;
        private bool _passwordRevealed = false;

        private const string BgTexturePath = "Content/Textures/login-bg.flax";
        private float _cardWidth = LoginInkTokens.CardMaxWidth;
        private const float CardHeight = 470f;
        // 内容层设计基准分辨率（UI 以此坐标系布局，运行时等比缩放以“随窗口缩小”）
        private const float DesignW = 1920f;
        private const float DesignH = 1080f;

        public LoginPanel()
        {
            // 全屏容器：宣纸底色，铺满画布（不使用圆角/暗金边框，改由代码绘制三栏布局）
            BackgroundColor = LoginInkTokens.Paper;
            CornerRadius = 0f;

            // 同步构建全部子控件并铺满父画布
            BuildVisualTree();
            InitializeLayout();
            LayoutZones();

            // 异步加载护照数据并回填账号/密码/记住密码
            InitializeUIDataAsync();

            var networkManager = HundunWorldGame.Instance?.NetworkManager;
            if (networkManager != null)
            {
                networkManager.ConnectionStatusChanged += OnConnectionStatusChanged;
            }

            CheckAndUpdateButtonState();
        }

        /// <summary>
        /// 确保全屏铺满：使用拉伸锚点（Min=0,0 / Max=1,1）+ 零偏移，避免构造期定位错位。
        /// </summary>
        private void InitializeLayout()
        {
            AnchorMin = Float2.Zero;
            AnchorMax = Float2.One;
            Offsets = Margin.Zero;
            Pivot = Float2.Zero;
        }

        /// <inheritdoc/>
        protected override void OnSizeChanged()
        {
            base.OnSizeChanged();
            LayoutZones();
        }

        /// <summary>
        /// 同步构建视觉树（一次）。控件定位交由 LayoutZones 依据当前尺寸完成。
        /// </summary>
        private void BuildVisualTree()
        {
            // ---------- 1. 全屏背景层 ----------
            _bgImage = UIHelper.CreateIcon(BgTexturePath);
            if (_bgImage != null)
            {
                _bgImage.KeepAspectRatio = false;
                _bgImage.Parent = this;
            }
            _bgOverlay = new LoginBackdropLayer();
            _bgOverlay.Parent = this;

            // 内容层：固定设计分辨率，运行时按窗口等比缩放并居中
            _content = new ContainerControl
            {
                BackgroundColor = Color.Transparent,
                ClipChildren = false,
                Size = new Float2(DesignW, DesignH),
                Parent = this
            };

            // ---------- 2. 品牌意境区（左 60%） ----------
            _brandZone = MakePanel(Color.Transparent);
            _brandZone.Parent = _content;

            _seal = new RoundedPanel
            {
                Size = new Float2(LoginInkTokens.SealSize, LoginInkTokens.SealSize),
                BackgroundColor = LoginInkTokens.Primary700,
                CornerRadius = LoginInkTokens.RadiusSm,
                Parent = _brandZone
            };
            _sealText = MakeLabel("混沌", LoginInkTokens.FontBodySm, LoginInkTokens.Paper, TextAlignment.Center);
            _sealText.Parent = _seal;

            _gameTitle = MakeLabel("混沌世界", LoginInkTokens.FontDisplay, LoginInkTokens.Ink900, TextAlignment.Near);
            _gameTitle.Parent = _brandZone;

            _inkStroke = new LoginInkStroke();
            _inkStroke.Parent = _brandZone;

            _poem = MakeLabel("一花一世界 · 一叶一菩提", LoginInkTokens.FontBodyLg, LoginInkTokens.Ink600, TextAlignment.Near);
            _poem.Parent = _brandZone;

            _particle1 = new LoginFloatParticle(6f, 0f);
            _particle2 = new LoginFloatParticle(8f, 1f);
            _particle3 = new LoginFloatParticle(7f, 2.5f);
            _particle1.Parent = _brandZone;
            _particle2.Parent = _brandZone;
            _particle3.Parent = _brandZone;

            // ---------- 3. 毛玻璃登录卡（右 40%）+ 柔和投影 ----------
            _cardShadow = new LoginCardShadow { Parent = _content };
            _card = new LoginCardPanel { Parent = _content };

            _welcomeTitle = MakeLabel("欢迎归来", LoginInkTokens.FontHeadingLg, LoginInkTokens.Ink800, TextAlignment.Near);
            _welcomeTitle.Parent = _card;
            _welcomeSub = MakeLabel("请登录您的账号", LoginInkTokens.FontBodySm, LoginInkTokens.Ink500, TextAlignment.Near);
            _welcomeSub.Parent = _card;

            _accountLabel = MakeLabel("账号", LoginInkTokens.FontBodySm, LoginInkTokens.Ink500, TextAlignment.Near);
            _accountLabel.Parent = _card;

            UsernameInput = ValidatedTextBox.CreateUsernameInput();
            UsernameInput.WatermarkText = "请输入账号";
            UsernameInput.InputBackgroundColor = Color.Transparent;
            UsernameInput.TextColor = LoginInkTokens.Ink800;
            UsernameInput.Parent = _card;
            _accountUnderline = new LoginUnderline { Parent = _card };
            _accountUnderline.Watch(UsernameInput);

            _passwordLabel = MakeLabel("密码", LoginInkTokens.FontBodySm, LoginInkTokens.Ink500, TextAlignment.Near);
            _passwordLabel.Parent = _card;

            PasswordInput = ValidatedTextBox.CreatePasswordInput();
            PasswordInput.WatermarkText = "请输入密码";
            PasswordInput.InputBackgroundColor = Color.Transparent;
            PasswordInput.TextColor = LoginInkTokens.Ink800;
            PasswordInput.Parent = _card;
            _passwordUnderline = new LoginUnderline { Parent = _card };
            _passwordUnderline.Watch(PasswordInput);

            // 密码显隐切换按钮（轻量近似眼睛图标，用中文「显/隐」）
            _eyeButton = new Button
            {
                Text = "显",
                BackgroundColor = Color.Transparent,
                BorderColor = Color.Transparent,
                TextColor = LoginInkTokens.Ink400,
                Font = UIHelper.SetFont(size: LoginInkTokens.FontBodySm),
                Parent = _card
            };
            _eyeButton.ButtonClicked += OnTogglePasswordClicked;

            RememberPasswordCheckBox = new CheckBox
            {
                Checked = false,
                ImageColor = LoginInkTokens.Primary500,
                Parent = _card
            };
            _rememberLabel = MakeLabel("记住密码", LoginInkTokens.FontBody, LoginInkTokens.Ink600, TextAlignment.Near);
            _rememberLabel.Parent = _card;
            // 自动登录没有任何实现（PassportInfo 里也没有对应字段）：勾选状态无处可存，
            // 保持可见但不可点，并在文案里说明未开放，避免让人以为勾了就会自动登录。
            _autoLoginCheckBox = new CheckBox
            {
                Checked = false,
                Enabled = false,
                ImageColor = LoginInkTokens.Ink400,
                Parent = _card
            };
            _autoLoginLabel = MakeLabel("自动登录·未开放", LoginInkTokens.FontBody, LoginInkTokens.Ink400, TextAlignment.Near);
            _autoLoginLabel.Parent = _card;

            // 主 CTA「进 入 江 湖」（悬停微缩放）
            LoginButton = new LoginHoverButton
            {
                Text = "进 入 江 湖",
                BackgroundColor = LoginInkTokens.Primary500,
                BackgroundColorHighlighted = LoginInkTokens.Primary600,
                BackgroundColorSelected = LoginInkTokens.Primary700,
                BorderColor = Color.Transparent,
                TextColor = LoginInkTokens.Paper,
                Font = UIHelper.SetFont(size: LoginInkTokens.FontBody),
                Parent = _card
            };
            LoginButton.ButtonClicked += OnLoginButtonClicked;

            // 注册（左，业务切换按钮） + 找回密码（右，纯表现）
            SwitchToRegisterButton = new Button
            {
                Text = "注册账号",
                BackgroundColor = Color.Transparent,
                BorderColor = Color.Transparent,
                TextColor = LoginInkTokens.Primary600,
                Font = UIHelper.SetFont(size: LoginInkTokens.FontBodySm),
                Parent = _card
            };
            SwitchToRegisterButton.ButtonClicked += OnSwitchToRegisterClicked;

            _forgotButton = new Button
            {
                Text = "找回密码",
                BackgroundColor = Color.Transparent,
                BorderColor = Color.Transparent,
                TextColor = LoginInkTokens.Primary600,
                Font = UIHelper.SetFont(size: LoginInkTokens.FontBodySm),
                Parent = _card
            };
            _forgotButton.ButtonClicked += OnForgotPasswordClicked;

            StatusLabel = MakeLabel("请输入账户信息", LoginInkTokens.FontBodySm, LoginInkTokens.Ink500, TextAlignment.Center);
            StatusLabel.Parent = _card;

            // ---------- 4. 底部辅助栏 ----------
            _footer = MakePanel(LoginInkTokens.WithAlpha(LoginInkTokens.Paper, 0.0f));
            _footer.Parent = _content;
            _statusDot = new LoginPulseDot { DotColor = LoginInkTokens.StateSuccess, Parent = _footer };
            // 服务器行只反映真实连接状态（旧文案写死"烽火连城 · 流畅"，与实际连接无关）
            _serverLabel = MakeLabel("服务器：未连接", LoginInkTokens.FontBodySm, LoginInkTokens.Ink500, TextAlignment.Near);
            _serverLabel.Parent = _footer;
            _versionLabel = MakeLabel("v1.2.0", LoginInkTokens.FontBodySm, LoginInkTokens.Ink400, TextAlignment.Center);
            _versionLabel.Parent = _footer;
            _copyrightLabel = MakeLabel("© 混沌世界", LoginInkTokens.FontBodySm, LoginInkTokens.Ink400, TextAlignment.Far);
            _copyrightLabel.Parent = _footer;
        }

        /// <summary>
        /// 依据当前尺寸进行三栏定位与卡片内部布局。
        /// </summary>
        private void LayoutZones()
        {
            if (_content == null || Size.X <= 0 || Size.Y <= 0)
                return;

            float W = Size.X;
            float H = Size.Y;

            // 背景铺满整个窗口（不参与缩放，保证任意比例下全出血）
            if (_bgImage != null) { _bgImage.Location = Float2.Zero; _bgImage.Size = Size; }
            _bgOverlay.Location = Float2.Zero; _bgOverlay.Size = Size;

            // 内容层按设计分辨率(1920x1080)等比缩放并居中，实现“随窗口缩小”
            _content.Size = new Float2(DesignW, DesignH);
            float s = Mathf.Min(W / DesignW, H / DesignH);
            _content.Pivot = new Float2(0.5f, 0.5f);
            _content.Scale = new Float2(s, s);
            _content.Location = new Float2((W - DesignW) * 0.5f, (H - DesignH) * 0.5f);

            LayoutContent();
        }

        /// <summary>在固定设计分辨率坐标系下排布品牌区/卡片/底部栏。</summary>
        private void LayoutContent()
        {
            float padX = LoginInkTokens.CardPadX;
            float brandW = DesignW * LoginInkTokens.BrandRatio;

            _brandZone.Location = Float2.Zero; _brandZone.Size = new Float2(brandW, DesignH);

            float brandPad = 48f;
            _seal.Location = new Float2(brandW - LoginInkTokens.SealSize - brandPad, brandPad);
            _sealText.Location = new Float2(0, 3); _sealText.Size = new Float2(LoginInkTokens.SealSize, LoginInkTokens.SealSize - 6);

            float midY = DesignH * 0.5f;
            _gameTitle.Location = new Float2(brandPad, midY - 72);
            _gameTitle.Size = new Float2(brandW - brandPad * 2, 60);
            _inkStroke.Location = new Float2(brandPad, midY + 6);
            _inkStroke.Size = new Float2(LoginInkTokens.InkStrokeWidth, LoginInkTokens.InkStrokeHeight);
            _poem.Location = new Float2(brandPad, midY + 32);
            _poem.Size = new Float2(brandW - brandPad * 2, 28);

            _particle1.Location = new Float2(brandPad - 4, DesignH * 0.80f); _particle1.Size = new Float2(14, 14);
            _particle2.Location = new Float2(brandPad + 18, DesignH * 0.80f - 6); _particle2.Size = new Float2(12, 12);
            _particle3.Location = new Float2(brandPad + 36, DesignH * 0.80f + 2); _particle3.Size = new Float2(13, 13);

            // 表单区（右 40%）— 卡片居中
            float formX = brandW;
            float formW = DesignW - brandW;
            _cardWidth = Mathf.Min(LoginInkTokens.CardMaxWidth, formW - 64f);
            float cardX = formX + (formW - _cardWidth) * 0.5f;
            float cardY = (DesignH - CardHeight) * 0.5f;
            _card.Size = new Float2(_cardWidth, CardHeight);
            _card.HomeLocation = new Float2(cardX, cardY);
            _card.Location = _card.HomeLocation;
            // 投影层比卡片四周外扩 32、向下多 8，用于容纳柔光阴影
            _cardShadow.Size = new Float2(_cardWidth + 64f, CardHeight + 64f);
            _cardShadow.HomeLocation = new Float2(cardX - 32f, cardY - 24f);
            _cardShadow.Location = _cardShadow.HomeLocation;
            LayoutCard(padX, _cardWidth - padX * 2);

            // 底部辅助栏
            float footerH = 48f;
            _footer.Location = new Float2(0, DesignH - footerH);
            _footer.Size = new Float2(DesignW, footerH);
            _statusDot.Location = new Float2(brandPad - 4, footerH * 0.5f - 8); _statusDot.Size = new Float2(16, 16);
            _serverLabel.Location = new Float2(brandPad + 14, footerH * 0.5f - 9); _serverLabel.Size = new Float2(280, 18);
            _versionLabel.Location = new Float2(0, footerH * 0.5f - 9); _versionLabel.Size = new Float2(DesignW, 18);
            _copyrightLabel.Location = new Float2(DesignW - brandPad - 200, footerH * 0.5f - 9); _copyrightLabel.Size = new Float2(200, 18);
        }

        /// <summary>
        /// 卡片内部自上而下布局（y 递增向下）。
        /// </summary>
        private void LayoutCard(float padX, float iw)
        {
            float y = LoginInkTokens.CardPadY;
            const float inputH = 30f;

            _welcomeTitle.Location = new Float2(padX, y); _welcomeTitle.Size = new Float2(iw, 34);
            y += 34 + 8;
            _welcomeSub.Location = new Float2(padX, y); _welcomeSub.Size = new Float2(iw, 18);
            y += 18 + 32;

            // 账号
            _accountLabel.Location = new Float2(padX, y); _accountLabel.Size = new Float2(iw, 16);
            y += 16 + 8;
            UsernameInput.Location = new Float2(padX, y); UsernameInput.Size = new Float2(iw, inputH);
            _accountUnderline.Location = new Float2(padX, y + inputH); _accountUnderline.Size = new Float2(iw, 1);
            y += inputH + 1 + LoginInkTokens.InputGap;

            // 密码
            _passwordLabel.Location = new Float2(padX, y); _passwordLabel.Size = new Float2(iw, 16);
            y += 16 + 8;
            PasswordInput.Location = new Float2(padX, y); PasswordInput.Size = new Float2(iw, inputH);
            _passwordUnderline.Location = new Float2(padX, y + inputH); _passwordUnderline.Size = new Float2(iw, 1);
            _eyeButton.Location = new Float2(padX + iw - 30, y + 2); _eyeButton.Size = new Float2(30, inputH - 4);
            y += inputH + 1 + 20;

            // 记住密码 + 自动登录（复选框为纯方框，配独立标签）
            float boxSize = 20f;
            RememberPasswordCheckBox.Location = new Float2(padX, y); RememberPasswordCheckBox.Size = new Float2(boxSize, boxSize);
            _rememberLabel.Location = new Float2(padX + boxSize + 6, y); _rememberLabel.Size = new Float2(iw * 0.5f - boxSize - 6, boxSize);
            _autoLoginCheckBox.Location = new Float2(padX + iw * 0.5f, y); _autoLoginCheckBox.Size = new Float2(boxSize, boxSize);
            _autoLoginLabel.Location = new Float2(padX + iw * 0.5f + boxSize + 6, y); _autoLoginLabel.Size = new Float2(iw * 0.5f - boxSize - 6, boxSize);
            y += 20 + LoginInkTokens.ButtonMarginTop;

            // 登录主按钮
            LoginButton.Location = new Float2(padX, y); LoginButton.Size = new Float2(iw, LoginInkTokens.ButtonHeight);
            y += LoginInkTokens.ButtonHeight + 20;

            // 注册 / 找回密码
            SwitchToRegisterButton.Location = new Float2(padX, y); SwitchToRegisterButton.Size = new Float2(iw * 0.5f, 20);
            _forgotButton.Location = new Float2(padX + iw * 0.5f, y); _forgotButton.Size = new Float2(iw * 0.5f, 20);
            y += 20 + 4;

            // 状态标签
            StatusLabel.Location = new Float2(padX, y); StatusLabel.Size = new Float2(iw, 18);
        }

        /// <summary>
        /// 异步加载护照数据并回填（仅数据，不改视觉结构）。
        /// </summary>
        private async void InitializeUIDataAsync()
        {
            if (_isInitialized)
                return;
            _isInitialized = true;

            _passortInfo = await DatabaseManager.GetPassport();
            if (_passortInfo != null)
            {
                if (UsernameInput != null)
                    UsernameInput.Text = _passortInfo.PassportId;
                if (PasswordInput != null)
                    PasswordInput.Text = _passortInfo.RememberPassword ? _passortInfo.Password : "";
                if (RememberPasswordCheckBox != null)
                    RememberPasswordCheckBox.Checked = _passortInfo.RememberPassword;
            }
        }

        private void OnConnectionStatusChanged(ConnectionStatus obj)
        {
            UpdateServerLine(obj);

            // 按钮始终保持可点击，由 AuthenticationManager 内部处理重连；这里只更新状态提示。
            switch (obj)
            {
                case ConnectionStatus.Disconnected:
                    SetStatus("连接已断开，点击登录将尝试重连", color: UIStyleTokens.StatusAlert);
                    EnableButtons();
                    break;
                case ConnectionStatus.Connecting:
                    SetStatus("正在连接服务器...", color: UIStyleTokens.StatusAlert);
                    EnableButtons();
                    break;
                case ConnectionStatus.Connected:
                    SetStatus($"{(!string.IsNullOrWhiteSpace(PasswordInput.Text) && !string.IsNullOrWhiteSpace(UsernameInput.Text) ? "" : "请输入账号密码登录")}", color: UIStyleTokens.StatusSuccess);
                    EnableButtons();
                    break;
                case ConnectionStatus.Reconnecting:
                    SetStatus("正在重连服务器...", color: UIStyleTokens.StatusAlert);
                    EnableButtons();
                    break;
                case ConnectionStatus.Failed:
                case ConnectionStatus.Error:
                    SetStatus("网络连接失败，点击登录将尝试重连", color: UIStyleTokens.StatusError);
                    EnableButtons();
                    break;
                case ConnectionStatus.Unknown:
                case ConnectionStatus.GatewaySwitching:
                default:
                    break;
            }
        }

        /// <summary>底部「服务器」行与状态圆点跟随真实连接状态。</summary>
        private void UpdateServerLine(ConnectionStatus status)
        {
            if (_serverLabel == null)
                return;

            string text;
            Color dot;
            switch (status)
            {
                case ConnectionStatus.Connected:
                    text = "服务器：已连接";
                    dot = LoginInkTokens.StateSuccess;
                    break;
                case ConnectionStatus.Connecting:
                case ConnectionStatus.Reconnecting:
                case ConnectionStatus.GatewaySwitching:
                    text = "服务器：连接中…";
                    dot = LoginInkTokens.StateWarning;
                    break;
                case ConnectionStatus.Disconnected:
                    text = "服务器：未连接";
                    dot = LoginInkTokens.Ink400;
                    break;
                default:
                    text = "服务器：连接异常";
                    dot = LoginInkTokens.StateError;
                    break;
            }

            _serverLabel.Text = text;
            if (_statusDot != null)
                _statusDot.DotColor = dot;
        }

        /// <summary>刷新UI（响应尺寸变化）。</summary>
        public void RefreshLayout()
        {
            LayoutZones();
        }

        /// <summary>密码显隐切换（纯表现层行为）。</summary>
        private void OnTogglePasswordClicked(Button sender)
        {
            _passwordRevealed = !_passwordRevealed;
            if (PasswordInput != null)
                PasswordInput.IsPassword = !_passwordRevealed;
            if (_eyeButton != null)
                _eyeButton.Text = _passwordRevealed ? "隐" : "显";
        }

        private void OnLoginButtonClicked(Button sender)
        {
            LoginButtonClicked?.Invoke(this);
        }

        private void OnSwitchToRegisterClicked(Button sender)
        {
            SwitchToRegisterClicked?.Invoke(this);
        }

        /// <summary>找回密码：全仓没有对应的服务端接口，点开后如实提示，不弹假的输入流程。</summary>
        private void OnForgotPasswordClicked(Button sender)
        {
            SetStatus("找回密码尚未接入服务端，请联系客服重置", UIStyleTokens.StatusWarning);
        }

        /// <summary>验证输入。</summary>
        public bool ValidateInput()
        {
            return UsernameInput.IsValid && PasswordInput.IsValid;
        }

        /// <summary>获取登录信息。</summary>
        public (string username, string password, bool rememberPassword) GetLoginInfo()
        {
            return (
                UsernameInput.Text?.Trim(),
                PasswordInput.Text,
                RememberPasswordCheckBox.Checked
            );
        }

        /// <summary>设置状态消息。</summary>
        public void SetStatus(string message, Color color)
        {
            FlaxEngine.Debug.Log($"[LoginPanel] 设置状态: {message}, 颜色: {color}");
            StatusLabel.Text = message;
            StatusLabel.TextColor = color;
            StatusLabel.Visible = true;
            StatusLabel.Enabled = true;
        }

        /// <summary>重置表单。</summary>
        public void ResetForm()
        {
            UsernameInput.Text = string.Empty;
            PasswordInput.Text = string.Empty;
            RememberPasswordCheckBox.Checked = false;
            SetStatus("请输入账户信息", UIStyleTokens.StatusAlert);
        }

        /// <summary>启用按钮（网络连接建立后调用）。</summary>
        public void EnableButtons()
        {
            if (LoginButton != null)
            {
                LoginButton.Enabled = true;
                LoginButton.Visible = true;
            }
            else
            {
                FlaxEngine.Debug.LogWarning("[LoginPanel.EnableButtons] LoginButton为null，无法启用");
            }

            if (SwitchToRegisterButton != null)
            {
                SwitchToRegisterButton.Enabled = true;
                SwitchToRegisterButton.Visible = true;
            }
            else
            {
                FlaxEngine.Debug.LogWarning("[LoginPanel.EnableButtons] SwitchToRegisterButton为null，无法启用");
            }

            SetStatus("已连接服务器，请输入账户信息", UIStyleTokens.StatusSuccess);
        }

        /// <summary>禁用按钮（网络断开时调用）。</summary>
        public void DisableButtons()
        {
            if (LoginButton != null)
                LoginButton.Enabled = false;
            if (SwitchToRegisterButton != null)
                SwitchToRegisterButton.Enabled = false;

            SetStatus("正在连接服务器...", UIStyleTokens.StatusAlert);
        }

        /// <summary>检查并更新按钮状态（默认启用，由业务层处理连接）。</summary>
        private void CheckAndUpdateButtonState()
        {
            var networkManager = HundunWorldGame.Instance?.NetworkManager;
            if (networkManager != null)
            {
                var status = networkManager.GetConnectionStatus();
                FlaxEngine.Debug.Log($"[LoginPanel] 检查网络状态: {status}");
                UpdateServerLine(status);
                if (status == Horizon.Game.Message.Enums.ConnectionStatus.Connected)
                {
                    SetStatus("已连接服务器，请输入账户信息", UIStyleTokens.StatusSuccess);
                }
                else
                {
                    SetStatus($"网络状态: {status}，点击登录将尝试连接", UIStyleTokens.StatusAlert);
                }
            }

            EnableButtons();
        }

        /// <summary>释放资源。</summary>
        public override void OnDestroy()
        {
            var networkManager = HundunWorldGame.Instance?.NetworkManager;
            if (networkManager != null)
            {
                networkManager.ConnectionStatusChanged -= OnConnectionStatusChanged;
            }

            if (LoginButton != null)
                LoginButton.ButtonClicked -= OnLoginButtonClicked;
            if (SwitchToRegisterButton != null)
                SwitchToRegisterButton.ButtonClicked -= OnSwitchToRegisterClicked;
            if (_eyeButton != null)
                _eyeButton.ButtonClicked -= OnTogglePasswordClicked;
            if (_forgotButton != null)
                _forgotButton.ButtonClicked -= OnForgotPasswordClicked;

            LoginButtonClicked = null;
            SwitchToRegisterClicked = null;
        }

        /// <summary>创建登录面板实例。</summary>
        public static LoginPanel Create()
        {
            return new LoginPanel();
        }

        // ================= 轻量构造辅助（仅表现层） =================
        private static Panel MakePanel(Color bg)
        {
            return new Panel { BackgroundColor = bg };
        }

        private static RoundedPanel MakeParticle(Color color, float size)
        {
            return new RoundedPanel
            {
                Size = new Float2(size, size),
                BackgroundColor = LoginInkTokens.WithAlpha(color, 0.5f),
                CornerRadius = size
            };
        }

        private static Label MakeLabel(string text, float size, Color color, TextAlignment align)
        {
            return new Label
            {
                Text = text,
                TextColor = color,
                Font = UIHelper.SetFont(size: size),
                HorizontalAlignment = align
            };
        }
    }
}
