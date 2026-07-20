using FlaxEngine;
using FlaxEngine.GUI;
using Game.Database;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using HundunWorld.Game.Services;
using HundunWorld.Game.UI;
using HundunWorld.Game.UI.Animation;
using HundunWorld.Game.UI.Authentication;
using HundunWorld.Game.UI.ErrorHandling;
using HundunWorld.Game.UI.Guidance;
using HundunWorld.Game.UI.States;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using static Game.Database.LiteDataContext;

namespace Game;

/// <summary>
/// AuthenticationController Script.
/// </summary>
public class AuthenticationController : Script
{
    // 核心管理器
    private HundunWorld.Game.UI.UIStateManager _stateManager;
    private AuthenticationManager _authManager;
    private UIAnimationManager _animationManager;
    private ErrorHandlingManager _errorManager;
    private UserGuidanceManager _guidanceManager;

    // UI组件
    public Panel LoginUIControl { get; set; }
    public Panel RegisterUIControl { get; set; }
    public bool ISA { get; set; }
    private LoginPanel _loginPanel { get; set; }
    private RegisterPanel _registerPanel { get; set; }
    private bool _isProcessing = false;
    private bool _isSubscribed = false;
    private bool _autoLoginAttempted = false;
    public override void OnStart()
    {

        InitializeManagers();

        SubscribeEvents();

        if (!_autoLoginAttempted)
        {
            _autoLoginAttempted = true;
            _ = TryAutoLoginAsync();
        }
    }

    private async Task TryAutoLoginAsync()
    {
        try
        {
            var result = await AuthenticationManager.Instance.TryAutoLoginAsync();
            
            if (result.IsSuccess)
            {
                FlaxEngine.Debug.Log("[AuthenticationController] 自动登录成功");
            }
            else
            {
                FlaxEngine.Debug.Log($"[AuthenticationController] 自动登录失败: {result.ErrorMessage}");
                if (AuthenticationManager.IsLaunchedFromGengDi && _loginPanel != null)
                {
                    FlaxEngine.Scripting.InvokeOnUpdate(() =>
                    {
                        _loginPanel.SetStatus("登录已过期，请重新登录", UIStyleTokens.StatusWarning);
                    });
                }
            }
        }
        catch (Exception ex)
        {
            FlaxEngine.Debug.LogError($"[AuthenticationController] 自动登录异常: {ex.Message}");
        }
    }

    /// <summary>
    /// 初始化管理器
    /// </summary>
    private void InitializeManagers()
    {
        var cons = (Actor.Parent.Parent.As<UICanvas>().GUI.Children[0] as CanvasScaler).Children;
        _loginPanel = cons[1] as LoginPanel;
        _registerPanel = cons[2] as RegisterPanel;
        _stateManager = HundunWorld.Game.UI.UIStateManager.Instance;
        _authManager = AuthenticationManager.Instance;
        _animationManager = UIAnimationManager.Instance;
        _errorManager = ErrorHandlingManager.Instance;
        _guidanceManager = UserGuidanceManager.Instance;
    }

    /// <summary>
    /// 订阅事件
    /// </summary>
    private void SubscribeEvents()
    {
        if (_isSubscribed) return;
        _isSubscribed = true;

        // 订阅认证管理器的响应事件
        FlaxEngine.Debug.Log($"[AuthenticationUI] 订阅 AuthenticationManager.LoginResponseReceived 事件");
        _authManager.LoginResponseReceived += OnLoginResponseReceived;
        _authManager.RegisterResponseReceived += OnRegisterResponseReceived;
        _loginPanel.LoginButtonClicked += OnLoginButtonClicked;
        _loginPanel.SwitchToRegisterClicked += OnSwitchToRegisterClicked;
        _registerPanel.RegisterButtonClicked += OnRegisterButtonClicked;
        _registerPanel.SendVerificationCodeClicked += OnSendVerificationCodeClicked;
        _registerPanel.SwitchToLoginClicked += OnSwitchToLoginClicked;

    }

    /// <inheritdoc/>
    public override void OnEnable()
    {
        // Here you can add code that needs to be called when script is enabled (eg. register for events)
    }

    /// <inheritdoc/>
    public override void OnDisable()
    {
        UnsubscribeEvents();
    }

    /// <inheritdoc/>
    public override void OnDestroy()
    {
        UnsubscribeEvents();
    }

    /// <summary>
    /// 取消订阅事件
    /// </summary>
    private void UnsubscribeEvents()
    {
        if (!_isSubscribed) return;
        _isSubscribed = false;

        if (_authManager != null)
        {
            _authManager.LoginResponseReceived -= OnLoginResponseReceived;
            _authManager.RegisterResponseReceived -= OnRegisterResponseReceived;
        }

        if (_loginPanel != null)
        {
            _loginPanel.LoginButtonClicked -= OnLoginButtonClicked;
            _loginPanel.SwitchToRegisterClicked -= OnSwitchToRegisterClicked;
        }

        if (_registerPanel != null)
        {
            _registerPanel.RegisterButtonClicked -= OnRegisterButtonClicked;
            _registerPanel.SendVerificationCodeClicked -= OnSendVerificationCodeClicked;
            _registerPanel.SwitchToLoginClicked -= OnSwitchToLoginClicked;
        }
    }

    /// <inheritdoc/>
    public override void OnUpdate()
    {
        // Here you can add code that needs to be called every frame
    }


    /// <summary>
    /// 登录按钮点击事件
    /// </summary>
    private async void OnLoginButtonClicked(LoginPanel loginPanel)
    {
        if (_isProcessing) return;

        _isProcessing = true;

        try
        {
            // 更新状态为正在登录
            _loginPanel.SetStatus("正在尝试登录...", UIStyleTokens.StatusAlert);

            AuthenticationManager.Instance.Passport = new PassportInfo
            {
                PassportId = _loginPanel.UsernameInput.Text,
                Password = _loginPanel.PasswordInput.Text,
                RememberPassword = _loginPanel.RememberPasswordCheckBox.Checked
            };

            var result = await _authManager.LoginAsync(
                _loginPanel.UsernameInput.Text,
                _loginPanel.PasswordInput.Text,
                _loginPanel.RememberPasswordCheckBox.Checked
            );

            // 根据结果更新提示信息（仅更新状态标签，不弹窗）
            if (result.IsSuccess)
            {
                _loginPanel.SetStatus("正在登录...", UIStyleTokens.StatusAlert);
            }
            else
            {
                _loginPanel.SetStatus(result.ErrorMessage ?? "登录失败，请检查用户名和密码", UIStyleTokens.StatusError);
            }
        }
        catch (Exception ex)
        {
            _loginPanel.SetStatus($"错误: {ex.Message}", UIStyleTokens.StatusError);
            _errorManager.HandleError($"登录过程中发生错误: {ex.Message}", ErrorType.Unknown, ErrorSeverity.Error, "AuthenticationUI");
        }
        finally
        {
            _isProcessing = false;
        }
    }

    /// <summary>
    /// 注册按钮点击事件
    /// </summary>
    private async void OnRegisterButtonClicked(RegisterPanel registerPanel)
    {
        FlaxEngine.Debug.Log($"[OnRegisterButtonClicked] 开始执行注册逻辑");
        if (_isProcessing)
        {
            FlaxEngine.Debug.Log($"[OnRegisterButtonClicked] 正在处理中，忽略重复点击");
            return;
        }

        _isProcessing = true;
        FlaxEngine.Debug.Log($"[OnRegisterButtonClicked] 设置处理状态为true");

        try
        {

            // 更新状态为正在注册
            _registerPanel.SetStatus("正在注册...", UIStyleTokens.StatusAlert);
            FlaxEngine.Debug.Log($"[OnRegisterButtonClicked] 状态已更新为正在注册");

            // 验证输入
            if (!_registerPanel.ValidateInput())
            {
                FlaxEngine.Debug.Log($"[OnRegisterButtonClicked] 输入验证失败");
                _registerPanel.SetStatus("请检查输入信息", UIStyleTokens.StatusError);
                return;
            }
            FlaxEngine.Debug.Log($"[OnRegisterButtonClicked] 输入验证成功");

            var result = await _authManager.RegisterAsync(
                _registerPanel.UsernameInput.Text,
                _registerPanel.PasswordInput.Text,
                _registerPanel.EmailInput.Text,
                _registerPanel.PhoneInput.Text,
                _registerPanel.VerificationCodeInput.Text
            );
            FlaxEngine.Debug.Log($"[OnRegisterButtonClicked] 注册异步调用完成，结果: {result.IsSuccess}");

            if (result.IsSuccess)
            {
                FlaxEngine.Debug.Log($"[OnRegisterButtonClicked] 注册请求已发送，等待服务器响应");
                _registerPanel.SetStatus("注册中...", UIStyleTokens.StatusAlert);
            }
            else
            {
                FlaxEngine.Debug.Log($"[OnRegisterButtonClicked] 注册失败: {result.ErrorMessage}");
                _registerPanel.SetStatus(result.ErrorMessage ?? "注册失败，请稍后重试", UIStyleTokens.StatusError);
            }
        }
        catch (Exception ex)
        {
            FlaxEngine.Debug.LogError($"[OnRegisterButtonClicked] 注册过程中发生异常: {ex.Message}\n{ex.StackTrace}");
            _registerPanel.SetStatus($"错误: {ex.Message}", UIStyleTokens.StatusError);
            _errorManager.HandleError($"注册过程中发生错误: {ex.Message}", ErrorType.Unknown, ErrorSeverity.Error, "AuthenticationUI");
        }
        finally
        {
            _isProcessing = false;
            FlaxEngine.Debug.Log($"[OnRegisterButtonClicked] 设置处理状态为false");
        }
    }

    /// <summary>
    /// 发送验证码按钮点击事件
    /// </summary>
    public async void OnSendVerificationCodeClicked(RegisterPanel registerPanel)
    {
        try
        {
            await _authManager.SendVerificationCodeAsync(registerPanel.EmailInput.Text, registerPanel.PhoneInput.Text);
            UIHelper.ShowInfo("验证码已发送");
        }
        catch (Exception ex)
        {
            _errorManager.HandleError($"发送验证码失败: {ex.Message}", ErrorType.Network, ErrorSeverity.Warning, "AuthenticationUI");
        }
    }

    /// <summary>
    /// 切换到注册界面
    /// </summary>
    private void OnSwitchToRegisterClicked(LoginPanel loginPanel)
    {
        FlaxEngine.Debug.Log($"[OnSwitchToRegisterClicked] 开始切换到注册界面");

        if (_animationManager != null)
        {
            // 先滑出登录面板，完成后再切换状态
            _animationManager.SlideOut(_loginPanel, new Float2(300, 0), 0.5f, EasingType.EaseOut, () =>
            {
                _loginPanel.Visible = false;
                _stateManager.TransitionToScene(SceneType.Register);
                ShowRegisterPanel(); // 直接调用显示注册面板
            });
        }
        else
        {
            _loginPanel.Visible = false;
            _stateManager.TransitionToScene(SceneType.Register);
            ShowRegisterPanel(); // 直接调用显示注册面板
        }
    }
    bool _isFristShowRegisterPanel;
    private void ShowRegisterPanel()
    {
        // 1. 停止动画并彻底重置物理状态
        _animationManager?.StopAnimations(_registerPanel);
        _registerPanel.Visible = true;
        _animationManager.SlideIn(_registerPanel, new Float2(_isFristShowRegisterPanel ? 300 : 0, 0), 0.4f, EasingType.EaseOut, null);
        _isFristShowRegisterPanel = true;
    }

    /// <summary>
    /// 切换到登录界面
    /// </summary>
    public void OnSwitchToLoginClicked(RegisterPanel registerPanel)
    {
        FlaxEngine.Debug.Log($"[OnSwitchToLoginClicked] 开始切换到登录界面");

        // 使用保存的Passport信息自动填充
        string username = AuthenticationManager.Instance?.Passport?.PassportId ?? "";
        string password = AuthenticationManager.Instance?.Passport?.Password ?? "";

        PerformSwitchToLogin(username, password, false);
    }

    /// <summary>
    /// 执行切换到登录界面的完整流程（注册成功后调用）
    /// </summary>
    /// <param name="username">要自动填充的用户名</param>
    /// <param name="password">要自动填充的密码</param>
    private void PerformSwitchToLogin(string username, string password, bool isRegisted = true)
    {
        FlaxEngine.Debug.Log($"[PerformSwitchToLogin] 开始切换到登录界面，用户名: {username}");

        if (_animationManager != null && _registerPanel != null)
        {
            // 先滑出注册面板
            _animationManager.SlideOut(_registerPanel, new Float2(300, 0), 0.5f, EasingType.EaseOut, () =>
            {
                _registerPanel.Visible = false;

                // 更新状态管理器
                if (_stateManager != null)
                {
                    _stateManager.TransitionToScene(SceneType.Login);
                }

                // 显示登录面板并填充账号信息
                ShowLoginPanelWithCredentials(username, password, isRegisted);
            });
        }
        else
        {
            // 降级方案：直接切换
            _registerPanel.Visible = false;
            if (_stateManager != null)
            {
                _stateManager.TransitionToScene(SceneType.Login);
            }
            ShowLoginPanelWithCredentials(username, password, isRegisted);
        }
    }

    /// <summary>
    /// 显示登录面板并自动填充账号信息
    /// </summary>
    /// <param name="username">用户名</param>
    /// <param name="password">密码</param>
    private void ShowLoginPanelWithCredentials(string username, string password, bool isRegisted = true)
    {
        string maskedPassword = new string('*', Mathf.Min(password.Length, 8));
        FlaxEngine.Debug.Log($"[ShowLoginPanelWithCredentials] 填充登录信息: 用户名={username}, 密码={maskedPassword}");

        // 停止动画并彻底重置物理状态
        _animationManager?.StopAnimations(_loginPanel);

        // 确保登录面板可见
        _loginPanel.Visible = true;
        _loginPanel.Enabled = true;
        if (isRegisted)
        { // 自动填充用户名和密码
            if (!string.IsNullOrEmpty(username))
            {
                _loginPanel.UsernameInput.Text = username;
                FlaxEngine.Debug.Log($"[ShowLoginPanelWithCredentials] 已填充用户名: {username}");
            }

            if (!string.IsNullOrEmpty(password))
            {
                _loginPanel.PasswordInput.Text = password;
                FlaxEngine.Debug.Log($"[ShowLoginPanelWithCredentials] 已填充密码");
            }
        }

        // 如果有记住密码选项，勾选它
        if (_loginPanel.RememberPasswordCheckBox != null)
        {
            _loginPanel.RememberPasswordCheckBox.Checked = true;
        }

        // 播放滑入动画
        _animationManager?.SlideIn(_loginPanel, new Float2(300, 0), 0.4f, EasingType.EaseOut, null);

        //// 更新状态提示
        //if (!string.IsNullOrEmpty(username))
        //{
        //    _loginPanel.SetStatus($"请登录账户: {username}", UIStyleTokens.StatusAlert);
        //}
        //else
        //{
        //    _loginPanel.SetStatus("请输入账户信息", UIStyleTokens.StatusAlert);
        //}

        FlaxEngine.Debug.Log("[ShowLoginPanelWithCredentials] 登录面板已显示并填充账号信息");
    }

    /// <summary>
    /// 显示登录面板（无自动填充，用于普通切换）
    /// </summary>
    private void ShowLoginPanel()
    {
        // 1. 停止动画并彻底重置物理状态
        _animationManager?.StopAnimations(_loginPanel);
        _loginPanel.Visible = true;
        _animationManager.SlideIn(_loginPanel, new Float2(300, 0), 0.4f, EasingType.EaseOut, null);
    }
    /// <summary>
    /// 处理登录响应
    /// </summary>
    private void OnLoginResponseReceived(LoginResponse response)
    {
        FlaxEngine.Debug.Log($"[AuthenticationUI] 收到登录响应: IsSuccess={response.IsSuccess}");

        if (_loginPanel == null) return;

        if (response.IsSuccess)
        {
            _loginPanel.SetStatus("登录成功！正在加载...", UIStyleTokens.StatusSuccess);
            _loginPanel.Enabled = false; // 禁用交互，等待场景切换
            _animationManager?.StopAnimations(_loginPanel);
        }
        else
        {
            _loginPanel.SetStatus(response.Message ?? "登录失败，请检查账户信息", UIStyleTokens.StatusError);
            _animationManager?.Shake(_loginPanel, 0.5f);
            _loginPanel.Enabled = true;
        }
    }

    /// <summary>
    /// 处理注册响应
    /// </summary>
    private void OnRegisterResponseReceived(RegisterResponse response)
    {
        try
        {
            if (response == null)
            {
                FlaxEngine.Debug.LogError("[OnRegisterResponseReceived] 注册响应为空");
                return;
            }

            if (response.IsSuccess)
            {
                FlaxEngine.Debug.Log($"[OnRegisterResponseReceived] 注册成功，PassportId: {response.PassportId}");

                // 使用注册响应中返回的PassportId
                string passportId = response.PassportId ?? "";

                // 获取之前保存的密码
                string password =  _registerPanel.PasswordInput.Text;

                // 更新Passport信息
                if (AuthenticationManager.Instance?.Passport == null)
                {
                    AuthenticationManager.Instance.Passport = new LiteDataContext.PassportInfo();
                }
                AuthenticationManager.Instance.Passport.PassportId = passportId;
                AuthenticationManager.Instance.Passport.Password = password;
                AuthenticationManager.Instance.Passport.RememberPassword = true;

                // 执行切换到登录界面并自动填充
                PerformSwitchToLogin(passportId, password);
            }
            else
            {
                // 更新状态标签显示错误信息并播放错误动画
                if (_registerPanel != null)
                {
                    _registerPanel.SetStatus(response.ErrorMessage ?? "注册失败，请稍后重试", UIStyleTokens.StatusError);
                }

                if (_animationManager != null && _registerPanel != null)
                {
                    _animationManager.Shake(_registerPanel, 0.5f);
                }
            }
        }
        catch (Exception ex)
        {
            FlaxEngine.Debug.LogError($"[OnRegisterResponseReceived] 处理注册响应异常: {ex.Message}\n{ex.StackTrace}");
            if (_errorManager != null)
            {
                _errorManager.HandleError($"处理注册响应失败: {ex.Message}", ErrorType.Unknown, ErrorSeverity.Error, "AuthenticationUI");
            }
        }
    }
}
