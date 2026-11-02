using FlaxEditor.Content;
using FlaxEngine;
using FlaxEngine.GUI;
using Game.Database;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using HundunWorld.Game.Network;
using HundunWorld.Game.Services;
using HundunWorld.Game.UI;
using HundunWorld.Game.UI.Animation;
using HundunWorld.Game.UI.Character;
using HundunWorld.Game.UI.Components;
using HundunWorld.Game.UI.Controllers;
using HundunWorld.Game.UI.ErrorHandling;
using HundunWorld.Game.UI.Guidance;
using HundunWorld.Game.UI.Layout;
using HundunWorld.Game.UI.StyleSystem;
using System;
using System.Threading.Tasks;
using static Game.Database.LiteDataContext;

namespace HundunWorld.Game.UI.Authentication
{
    /// <summary>
    /// 用户认证界面 - 重构版本
    /// 使用独立的组件类，遵循单一职责原则
    /// </summary>
    public class AuthenticationUI : Script
    {
        // 核心管理器
        private UIStateManager _stateManager;
        private AuthenticationManager _authManager;
        private UIAnimationManager _animationManager;
        private ErrorHandlingManager _errorManager;
        private UserGuidanceManager _guidanceManager;
        private NetworkManager _networkManager;

        // UI组件
        private ContainerControl _mainContainer;
        private LoginPanel _loginPanel;
        private RegisterPanel _registerPanel;
        private LoadingIndicator _loadingIndicator;

        // 状态
        /// <summary>登录/注册的重入闸门；主线程置位，await 续体（线程池）复位，故声明为 volatile。</summary>
        private volatile bool _isProcessing = false;
        private bool _isFirstLogin = true;
        private bool _isNetworkConnected = false;
        private bool _loginPagePrepared = false;
        private System.Threading.CancellationTokenSource _statusCheckCts;

        /// <summary>
        /// 主（更新）线程 ID，在 <see cref="OnStart"/> 里记录。
        /// Flax 主循环不安装 SynchronizationContext，所以 await 之后的续体、
        /// Task.Run 与网络回调都会落到线程池线程，触碰 GUI / 场景层级即抛
        /// "Editing scene hierarchy is only allowed on a main thread"。
        /// </summary>
        private int _mainThreadId = -1;

        public ContainerControl MainContainer { get => _mainContainer; set => _mainContainer = value; }
        public override void OnStart()
        {
            _mainThreadId = System.Environment.CurrentManagedThreadId;
            FlaxEngine.Debug.Log($"[AuthenticationUI] 开始初始化（主线程 ID={_mainThreadId}）");
            InitializeManagers();
            InitializeUI();
            SubscribeEvents();

            // 本组件自建的登录页默认收起：Login.scene 自带一套（AuthenticationController 管理），
            // 只有场景里没有那套时才由 ShowAuthenticationUI 把它放出来。
            if (_mainContainer != null)
            {
                _mainContainer.Visible = false;
                _mainContainer.Enabled = false;
            }
            if (_loginPanel != null) _loginPanel.Visible = false;
            if (_registerPanel != null) _registerPanel.Visible = false;

            if (!_loginPagePrepared)
            {
                _loginPagePrepared = true;
                _ = PrepareLoginPageAsync();
            }

            FlaxEngine.Debug.Log("认证界面重构版初始化完成");
        }

        private async Task PrepareLoginPageAsync()
        {
            try
            {
                var result = await AuthenticationManager.Instance.PrepareLoginPageAsync();

                FlaxEngine.Debug.Log($"[AuthenticationUI] 登录页已就绪: {result.ErrorMessage}");

                if (AuthenticationManager.IsLaunchedFromGengDi)
                {
                    RunOnMainThread(() =>
                    {
                        if (_loginPanel != null)
                            _loginPanel.SetStatus("请登录后选择角色", UIStyleTokens.StatusWarning);
                    });
                }
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[AuthenticationUI] 登录页准备异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 把一段 GUI / 场景操作切回主线程执行：已在主线程则立即运行（行为不变），
        /// 否则排队到下一帧更新。所有可能被 await 续体或后台线程调用的入口都要经过它。
        /// </summary>
        private void RunOnMainThread(Action action)
        {
            if (action == null)
                return;

            if (_mainThreadId < 0 || System.Environment.CurrentManagedThreadId == _mainThreadId)
            {
                action();
                return;
            }

            FlaxEngine.Scripting.InvokeOnUpdate(() =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    FlaxEngine.Debug.LogError($"[AuthenticationUI] 主线程回调执行失败: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// 初始化管理器
        /// </summary>
        private void InitializeManagers()
        {
            _stateManager = UIStateManager.Instance;
            _authManager = AuthenticationManager.Instance;
            _animationManager = UIAnimationManager.Instance;
            _errorManager = ErrorHandlingManager.Instance;
            _guidanceManager = UserGuidanceManager.Instance;
            _networkManager = HundunWorldGame.Instance.NetworkManager;
        }

        /// <summary>
        /// 订阅事件
        /// </summary>
        private void SubscribeEvents()
        {
            _stateManager.SceneChanged += OnSceneChanged;
            _stateManager.LoadingStateChanged += OnLoadingStateChanged;
            _stateManager.ErrorOccurred += OnErrorOccurred;
            
            // 订阅认证管理器的响应事件
            FlaxEngine.Debug.Log($"[AuthenticationUI] 订阅 AuthenticationManager.LoginResponseReceived 事件");
            _authManager.LoginResponseReceived += OnLoginResponseReceived;
            _authManager.RegisterResponseReceived += OnRegisterResponseReceived;

            // 订阅网络连接状态变化事件
            if (_networkManager != null)
            {
                _networkManager.ConnectionStatusChanged += OnNetworkConnectionStatusChanged;
                FlaxEngine.Debug.Log($"[AuthenticationUI] 订阅 NetworkManager.ConnectionStatusChanged 事件");
                
                // 立即检查当前连接状态
                CheckAndUpdateButtonStates();
                
                // 启动定期状态检查，确保即使事件未触发也能更新UI
                StartStatusPolling();
            }
        }

        /// <summary>
        /// 启动定期状态检查
        /// </summary>
        private void StartStatusPolling()
        {
            // 取消之前的轮询
            _statusCheckCts?.Cancel();
            _statusCheckCts?.Dispose();
            _statusCheckCts = new System.Threading.CancellationTokenSource();
            
            Task.Run(async () =>
            {
                try
                {
                    while (!_statusCheckCts.Token.IsCancellationRequested)
                    {
                        await Task.Delay(500, _statusCheckCts.Token); // 每500ms检查一次
                        
                        FlaxEngine.Scripting.InvokeOnUpdate(() =>
                        {
                            if (_networkManager != null && !_statusCheckCts.Token.IsCancellationRequested)
                            {
                                var status = _networkManager.GetConnectionStatus();
                                var shouldBeConnected = (status == ConnectionStatus.Connected);
                                
                                // 只在状态发生变化时更新
                                if (_isNetworkConnected != shouldBeConnected)
                                {
                                    FlaxEngine.Debug.Log($"[AuthenticationUI] 轮询检测到状态变化: {_isNetworkConnected} -> {shouldBeConnected} (NetworkManager状态: {status})");
                                    _isNetworkConnected = shouldBeConnected;
                                    UpdateButtonStates();
                                }
                            }
                        });
                    }
                }
                catch (System.OperationCanceledException)
                {
                    // 轮询被取消，正常退出
                    FlaxEngine.Debug.Log("[AuthenticationUI] 状态轮询已停止");
                }
                catch (Exception ex)
                {
                    FlaxEngine.Debug.LogError($"[AuthenticationUI] 状态轮询异常: {ex.Message}");
                }
            }, _statusCheckCts.Token);
        }
        
        /// <summary>
        /// 停止定期状态检查
        /// </summary>
        private void StopStatusPolling()
        {
            _statusCheckCts?.Cancel();
        }

        /// <summary>
        /// 场景切换事件处理。
        /// <para>
        /// 事件源（UIStateManager / GameSceneManager）的 await 续体可能在线程池线程上触发，
        /// 因此整个处理过程先切回主线程；面板显示自身的异步取数由
        /// <see cref="ShowLoginPanelInternal"/> 负责回切。
        /// </para>
        /// </summary>
        private void OnSceneChanged(SceneType previousScene, SceneType newScene)
        {
            RunOnMainThread(() =>
            {
                FlaxEngine.Debug.Log($"[AuthenticationUI] OnSceneChanged: {previousScene} -> {newScene}");

                // 场景切换时立即停止所有可能的 UI 动画
                if (_animationManager != null && _mainContainer != null)
                {
                    _animationManager.StopAnimations(_mainContainer);
                    if (_loginPanel != null) _animationManager.StopAnimations(_loginPanel);
                    if (_registerPanel != null) _animationManager.StopAnimations(_registerPanel);
                }

                switch (newScene)
                {
                    case SceneType.Login:
                        if (_loginPanel != null) _loginPanel.SetStatus("请输入账户信息", UIStyleTokens.StatusAlert);
                        _ = ShowLoginPanel();
                        break;
                    case SceneType.Register:
                        if (_registerPanel != null) _registerPanel.SetStatus("请输入注册信息", UIStyleTokens.StatusAlert);
                        ShowRegisterPanel();
                        break;
                    case SceneType.CharacterSelection:
                        FlaxEngine.Debug.Log("[AuthenticationUI] 登录成功，立即隐藏认证UI");
                        HideAuthenticationUI();
                        break;
                    default:
                        HideAuthenticationUI();
                        break;
                }
            });
        }

        /// <summary>
        /// 加载状态变化事件处理
        /// </summary>
        private void OnLoadingStateChanged(bool isLoading)
        {
            RunOnMainThread(() =>
            {
                if (isLoading)
                {
                    _loadingIndicator.Show("正在处理请求...");
                }
                else
                {
                    _loadingIndicator.Hide();
                }
            });
        }

        /// <summary>
        /// 错误事件处理
        /// </summary>
        private void OnErrorOccurred(string errorMessage)
        {
            RunOnMainThread(() =>
            {
                if (_stateManager.CurrentScene == SceneType.Start || _stateManager.CurrentScene == SceneType.Login)
                    _loginPanel.SetStatus(errorMessage, UIStyleTokens.StatusError);
                new ToastManager().ShowError(errorMessage);
            });
        }

        /// <summary>
        /// 显示登录引导
        /// </summary>
        private void ShowLoginGuidance()
        {
            var guidance = UserGuidanceManager.CreateLoginGuidance();
            _guidanceManager.StartGuidance(guidance);
        }

        /// <summary>
        /// 网络连接状态变化处理（NetworkManager 的 IO 线程也可能触发）
        /// </summary>
        private void OnNetworkConnectionStatusChanged(ConnectionStatus status)
        {
            RunOnMainThread(() =>
            {
                FlaxEngine.Debug.Log($"[AuthenticationUI] 网络连接状态变化: {status}");
                _isNetworkConnected = (status == ConnectionStatus.Connected);

                // 更新按钮状态
                UpdateButtonStates();
            });
        }

        /// <summary>
        /// 检查并更新按钮状态
        /// </summary>
        private void CheckAndUpdateButtonStates()
        {
            if (_networkManager != null)
            {
                var status = _networkManager.GetConnectionStatus();
                _isNetworkConnected = (status == ConnectionStatus.Connected);
                FlaxEngine.Debug.Log($"[AuthenticationUI] 当前网络连接状态: {status}, 按钮状态: {(_isNetworkConnected ? "启用" : "禁用")}");
                UpdateButtonStates();
                
                // 在编辑器中，如果状态不是Connected，延迟重新检查
                // 因为NetworkManager可能正在连接过程中
                if (status != ConnectionStatus.Connected)
                {
                    FlaxEngine.Debug.Log($"[AuthenticationUI] 检测到非连接状态 {status}，将在2秒后重新检查");
                    Task.Run(async () =>
                    {
                        await Task.Delay(2000);
                        FlaxEngine.Scripting.InvokeOnUpdate(() =>
                        {
                            if (_networkManager != null)
                            {
                                var newStatus = _networkManager.GetConnectionStatus();
                                _isNetworkConnected = (newStatus == ConnectionStatus.Connected);
                                FlaxEngine.Debug.Log($"[AuthenticationUI] 延迟检查后的网络连接状态: {newStatus}, 按钮状态: {(_isNetworkConnected ? "启用" : "禁用")}");
                                UpdateButtonStates();
                            }
                        });
                    });
                }
            }
        }

        /// <summary>
        /// 更新按钮状态
        /// 修复：按钮始终保持可点击，由 AuthenticationManager 内部处理连接；
        /// 此处仅根据网络状态刷新提示文本，避免网络未就绪时按钮被禁用导致“无响应”。
        /// </summary>
        private void UpdateButtonStates()
        {
            if (_isNetworkConnected)
            {
                _loginPanel?.EnableButtons();
                _registerPanel?.EnableButtons();
                FlaxEngine.Debug.Log($"[AuthenticationUI] 网络已连接，按钮已启用");
            }
            else
            {
                // 网络未连接时仍然启用按钮，点击后会由业务逻辑尝试建立连接
                _loginPanel?.EnableButtons();
                _registerPanel?.EnableButtons();
                _loginPanel?.SetStatus($"网络未连接，点击登录将尝试连接", UIStyleTokens.StatusAlert);
                _registerPanel?.SetStatus($"网络未连接，点击注册将尝试连接", UIStyleTokens.StatusAlert);
                FlaxEngine.Debug.Log($"[AuthenticationUI] 网络未连接，但保持按钮可点击以便触发重连");
            }
        }

        /// <summary>
        /// 初始化用户界面。
        /// <para>
        /// 全程同步：这里一旦 await，续体就会落到线程池线程（Flax 主循环不安装
        /// SynchronizationContext），随后 <see cref="CreateFallbackUICanvas"/> 里的
        /// Level.SpawnActor 会抛 "Editing scene hierarchy is only allowed on a main thread"。
        /// 原先的 await Task.Delay(50) 注释称要等 "InitializeUIAsync"，该方法并不存在，属于无依据的等待，已移除。
        /// 需要异步取数的部分交给 <see cref="ShowLoginPanelInternal"/>，它自己负责回切主线程。
        /// </para>
        /// </summary>
        private void InitializeUI()
        {
            // 创建主容器 - 优化背景效果
            _mainContainer = new ContainerControl
            {
                AnchorPreset = AnchorPresets.StretchAll,
                Visible = true,
                Enabled = true
            };
            _mainContainer.SizeChanged += MainContainer_SizeChanged;
            // 使用独立的组件类创建 UI元素
            _loadingIndicator = LoadingIndicator.Create();
            _loginPanel = LoginPanel.Create();
            _registerPanel = RegisterPanel.Create();

            // 修复：初始化时即启用按钮，由业务层在点击后处理连接，避免网络未就绪时无响应。
            _loginPanel?.EnableButtons();
            _registerPanel?.EnableButtons();
            FlaxEngine.Debug.Log("[AuthenticationUI] 初始化时设置按钮为启用状态");
                    
            // 初始化加载指示器位置 - 动态居中显示
            var loadingSize = new Float2(0, 0);
            _loadingIndicator.Location = ResponsiveLayoutCalculator.CalculateCenterPosition(loadingSize);
        
            // 订阅组件事件
            SubscribeComponentEvents();
        
            // 将主容器添加到GUI
            var uiCanvas = FindUICanvas();
            if (uiCanvas?.GUI != null)
            {
                uiCanvas.GUI.AnchorPreset = AnchorPresets.StretchAll;
                uiCanvas.GUI.AddChild(_mainContainer);
                FlaxEngine.Debug.Log("成功添加认证UI主容器到GUI");
            }
            else
            {
                // 这不是故障：登录脚本可能先于场景里的 UICanvas Actor 完成注册而执行，
                // 备用方案会自行 Spawn 一个专属 Canvas（成功时由 CreateFallbackUICanvas 记录，
                // 真正失败时它自己会报 Error）。此前在这里无条件报 Error，
                // 使每次正常登录都留下一条与事实相反的"未找到UICanvas"错误。
                FlaxEngine.Debug.Log("[AuthenticationUI] 未找到现成 UICanvas，改用备用方案创建专属 Canvas");
                // 备用方案：直接使用RootControl
                TryAddToRootControl();
            }
            // 将组件添加到主容器
            _mainContainer.AddChild(_loadingIndicator);
            _mainContainer.AddChild(_loginPanel);
            _mainContainer.AddChild(_registerPanel);
        
            // 默认显示登录界面（内部方法自行处理异步取数与主线程回切）
            _ = ShowLoginPanelInternal();
        }

        private void MainContainer_SizeChanged(Control obj)
        {
            _loginPanel?.RefreshLayout();
            _registerPanel?.RefreshLayout();
        }

        /// <summary>
        /// 订阅组件事件
        /// </summary>
        private void SubscribeComponentEvents()
        {
            // 订阅登录面板事件
            if (_loginPanel != null)
            {
                _loginPanel.LoginButtonClicked += OnLoginButtonClicked;
                _loginPanel.SwitchToRegisterClicked += OnSwitchToRegisterClicked;
                TryArmSelfTestAutoLogin(_loginPanel);
            }

            // 订阅注册面板事件
            if (_registerPanel != null)
            {
                _registerPanel.RegisterButtonClicked += OnRegisterButtonClicked;
                _registerPanel.SwitchToLoginClicked += OnSwitchToLoginClicked;
                _registerPanel.SendVerificationCodeClicked += OnSendVerificationCodeClicked;
            }
        }

        /// <summary>
        /// 自测：<c>%APPDATA%/HundunWorld/auto-login.on</c> 存在时，面板建好后用预填账号走一次真实登录。
        /// 只服务自动化验证——登录页是旧式 Flax 面板，编辑器工具（ui_input/ui_invoke）点不到它，
        /// 没有这条通路就进不了选角/世界，后续页面走查全部卡在登录页。正常环境不会有这个文件。
        /// </summary>
        private async void TryArmSelfTestAutoLogin(LoginPanel panel)
        {
            try
            {
                string marker = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "HundunWorld", "auto-login.on");
                if (!System.IO.File.Exists(marker))
                    return;

                await Task.Delay(1500);
                if (panel == null || _loginPanel != panel || _isProcessing)
                    return;

                FlaxEngine.Debug.Log($"[SelfTest] auto-login fire accountLen={panel.UsernameInput.Text?.Length ?? 0}");
                OnLoginButtonClicked(panel);
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[SelfTest] auto-login failed: {ex.Message}");
            }
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
                loginPanel.SetStatus("正在尝试登录...", UIStyleTokens.StatusAlert);

                AuthenticationManager.Instance.Passport = new PassportInfo
                {
                    PassportId = loginPanel.UsernameInput.Text,
                    Password = loginPanel.PasswordInput.Text,
                    RememberPassword = loginPanel.RememberPasswordCheckBox.Checked
                };

                var result = await _authManager.LoginAsync(
                    loginPanel.UsernameInput.Text,
                    loginPanel.PasswordInput.Text,
                    loginPanel.RememberPasswordCheckBox.Checked
                );

                // 根据结果更新提示信息（仅更新状态标签，不弹窗）
                // await 之后的续体不保证在主线程，回写面板必须回切
                RunOnMainThread(() =>
                {
                    if (result.IsSuccess)
                    {
                        loginPanel.SetStatus("正在登录...", UIStyleTokens.StatusAlert);
                    }
                    else
                    {
                        loginPanel.SetStatus(result.ErrorMessage ?? "登录失败，请检查用户名和密码", UIStyleTokens.StatusError);
                    }
                });
            }
            catch (Exception ex)
            {
                RunOnMainThread(() =>
                {
                    loginPanel.SetStatus($"错误: {ex.Message}", UIStyleTokens.StatusError);
                    _errorManager.HandleError($"登录过程中发生错误: {ex.Message}", ErrorType.Unknown, ErrorSeverity.Error, "AuthenticationUI");
                });
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
                registerPanel.SetStatus("正在注册...", UIStyleTokens.StatusAlert);
                FlaxEngine.Debug.Log($"[OnRegisterButtonClicked] 状态已更新为正在注册");

                // 验证输入
                if (!registerPanel.ValidateInput())
                {
                    FlaxEngine.Debug.Log($"[OnRegisterButtonClicked] 输入验证失败");
                    registerPanel.SetStatus("请检查输入信息", UIStyleTokens.StatusError);
                    return;
                }
                FlaxEngine.Debug.Log($"[OnRegisterButtonClicked] 输入验证成功");

                var result = await _authManager.RegisterAsync(
                    registerPanel.UsernameInput.Text,
                    registerPanel.PasswordInput.Text,
                    registerPanel.EmailInput.Text,
                    registerPanel.PhoneInput.Text,
                    registerPanel.VerificationCodeInput.Text
                );
                FlaxEngine.Debug.Log($"[OnRegisterButtonClicked] 注册异步调用完成，结果: {result.IsSuccess}");

                // 根据结果更新提示信息（仅更新状态标签，不弹窗）
                // await 之后的续体不保证在主线程，读输入框与回写面板必须回切
                RunOnMainThread(() =>
                {
                    if (result.IsSuccess)
                    {
                        FlaxEngine.Debug.Log($"[OnRegisterButtonClicked] 注册成功，更新Passport信息");
                        if (AuthenticationManager.Instance.Passport == null)
                            AuthenticationManager.Instance.Passport = new();
                        AuthenticationManager.Instance.Passport.PassportId = registerPanel.UsernameInput.Text;
                        AuthenticationManager.Instance.Passport.Password = registerPanel.PasswordInput.Text;

                        registerPanel.SetStatus("注册成功！请使用新账户登录", UIStyleTokens.StatusSuccess);
                        FlaxEngine.Debug.Log($"[OnRegisterButtonClicked] 注册成功，状态已更新");
                        //切换到登录界面，将新注册的用户名自动填入登录框,待数据处理完在跳转
                        //_stateManager.TransitionToScene(SceneType.Login);
                    }
                    else
                    {
                        FlaxEngine.Debug.Log($"[OnRegisterButtonClicked] 注册失败: {result.ErrorMessage}");
                        registerPanel.SetStatus(result.ErrorMessage ?? "注册失败，请稍后重试", UIStyleTokens.StatusError);
                    }
                });
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[OnRegisterButtonClicked] 注册过程中发生异常: {ex.Message}\n{ex.StackTrace}");
                RunOnMainThread(() =>
                {
                    registerPanel.SetStatus($"错误: {ex.Message}", UIStyleTokens.StatusError);
                    _errorManager.HandleError($"注册过程中发生错误: {ex.Message}", ErrorType.Unknown, ErrorSeverity.Error, "AuthenticationUI");
                });
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
        private async void OnSendVerificationCodeClicked(RegisterPanel registerPanel)
        {
            try
            {
                await _authManager.SendVerificationCodeAsync(registerPanel.EmailInput.Text, registerPanel.PhoneInput.Text);
                RunOnMainThread(() => UIHelper.ShowInfo("验证码已发送"));
            }
            catch (Exception ex)
            {
                RunOnMainThread(() =>
                    _errorManager.HandleError($"发送验证码失败: {ex.Message}", ErrorType.Network, ErrorSeverity.Warning, "AuthenticationUI"));
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

        /// <summary>
        /// 切换到登录界面
        /// </summary>
        private void OnSwitchToLoginClicked(RegisterPanel registerPanel)
        {
            FlaxEngine.Debug.Log($"[OnSwitchToLoginClicked] 开始切换到登录界面");

            if (_animationManager != null)
            {
                // 先滑出注册面板，完成后再切换状态
                _animationManager.SlideOut(_registerPanel, new Float2(300, 0), 0.5f, EasingType.EaseOut, () =>
                {
                    _registerPanel.Visible = false;
                    _stateManager.TransitionToScene(SceneType.Login);
                });
            }
            else
            {
                _registerPanel.Visible = false;
                _stateManager.TransitionToScene(SceneType.Login);
            }
        }

        /// <summary>
        /// 处理登录响应
        /// <para>
        /// WebApi 登录路径由 AuthenticationManager.FinalizeLoginAsync 触发该事件，
        /// 而它运行在 await 之后的线程池线程上（旧 TCP 路径才预先 InvokeOnUpdate），
        /// 所以这里必须自行回切主线程。
        /// </para>
        /// </summary>
        private void OnLoginResponseReceived(LoginResponse response)
        {
            RunOnMainThread(() =>
            {
                FlaxEngine.Debug.Log($"[AuthenticationUI] 收到登录响应: IsSuccess={response.IsSuccess}");

                if (_loginPanel == null) return;

                if (response.IsSuccess)
                {
                    // 按需求移除"登录成功"提示文案：不再显示状态提示，直接禁用交互等待场景切换。
                    _loginPanel.Enabled = false; // 禁用交互，等待场景切换
                    _animationManager?.StopAnimations(_loginPanel);
                }
                else
                {
                    _loginPanel.SetStatus(response.Message ?? "登录失败，请检查账户信息", UIStyleTokens.StatusError);
                    _animationManager?.Shake(_loginPanel, 0.5f);
                    _loginPanel.Enabled = true;
                }
            });
        }

        /// <summary>
        /// 处理注册响应
        /// </summary>
        private void OnRegisterResponseReceived(RegisterResponse response)
        {
            RunOnMainThread(() =>
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
                        // 播放成功动画并切换到登录界面
                        if (_registerPanel != null && _animationManager != null)
                        {
                            _animationManager.SlideOut(_registerPanel, new Float2(300, 0), 0.5f, EasingType.EaseOut, onComplete: () =>
                            {
                                if (_stateManager != null)
                                {
                                    _stateManager.TransitionToScene(SceneType.Login);
                                }
                            });
                        }
                        else
                        {
                            FlaxEngine.Debug.LogWarning("[OnRegisterResponseReceived] 注册面板或动画管理器为空，直接转换场景");
                            if (_stateManager != null)
                            {
                                _stateManager.TransitionToScene(SceneType.Login);
                            }
                        }
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
            });
        }

        #region 面板显示控制

        /// <summary>
        /// 显示登录面板（内部异步方法）
        /// <para>
        /// await 取护照之后的续体在线程池线程，回填 GUI 的那一段必须经
        /// <see cref="RunOnMainThread"/> 回切，否则会命中主线程断言。
        /// </para>
        /// </summary>
        private async Task ShowLoginPanelInternal()
        {
            FlaxEngine.Debug.Log($"[ShowLoginPanelInternal] 开始执行, _loginPanel={((_loginPanel != null) ? "not null" : "null")}");

            // 场景自带认证页存在时，所有"显示登录页"的请求都交给它，避免两套登录卡同时出现。
            var sceneAuth = global::Game.AuthenticationController.Current;
            if (sceneAuth != null)
            {
                RunOnMainThread(() => sceneAuth.ShowAuthUI());
                return;
            }

            RunOnMainThread(() =>
            {
                if (_loginPanel == null)
                {
                    _loginPanel = LoginPanel.Create();
                    _mainContainer.AddChild(_loginPanel);
                    _loginPanel.LoginButtonClicked += OnLoginButtonClicked;
                    _loginPanel.SwitchToRegisterClicked += OnSwitchToRegisterClicked;
                }

                // 1. 停止动画并彻底重置物理状态
                _animationManager?.StopAnimations(_loginPanel);

                _registerPanel.Visible = false;
                _loginPanel.Visible = true;
                _loginPanel.Enabled = true;
                FlaxEngine.Debug.Log($"[ShowLoginPanelInternal] 设置_loginPanel.Enabled = true");

                // 2. LoginPanel 自身就是全屏三栏设计（背景铺满 + 内容层按 1920x1080 等比收敛），
                //    这里必须保持拉伸铺满；旧代码把它钉成 480x297 居中框，会让整页缩到 25% 且偏出屏幕。
                _loginPanel.AnchorMin = Float2.Zero;
                _loginPanel.AnchorMax = Float2.One;
                _loginPanel.Offsets = Margin.Zero;
                _loginPanel.Pivot = Float2.Zero;
                _loginPanel.Scale = Float2.One;
                _loginPanel.RefreshLayout();
            });

            // 3. 加载护照数据，取回后再切主线程回填
            var passport = await DatabaseManager.GetPassport();
            RunOnMainThread(() =>
            {
                if (passport != null)
                {
                    _loginPanel.UsernameInput.Text = passport.PassportId;
                    _loginPanel.PasswordInput.Text = passport.Password;
                    _loginPanel.RememberPasswordCheckBox.Checked = passport.RememberPassword;
                }

                _mainContainer.Visible = true;
                _mainContainer.Enabled = true;

                // 4. 检查并更新按钮状态
                FlaxEngine.Debug.Log($"[ShowLoginPanelInternal] 即将调用CheckAndUpdateButtonStates");
                CheckAndUpdateButtonStates();

                FlaxEngine.Debug.Log("[ShowLoginPanelInternal] 登录面板已重置并居中");

                if (_animationManager != null)
                {
                    _animationManager.SlideIn(_loginPanel, new Float2(300, 0), 0.4f);
                }
            });
        }

        /// <summary>
        /// 显示登录面板（公共同步方法）
        /// </summary>
        public async Task ShowLoginPanel()
        {
            await ShowLoginPanelInternal();
        }

        /// <summary>
        /// 显示角色选择面板
        /// </summary>
        public void ShowCharacterPanel()
        {
            RunOnMainThread(() =>
            {
                FlaxEngine.Debug.Log("[ShowCharacterPanel] 准备切换到角色选择界面");

                // 隐藏登录和注册面板，并播放淡出动画
                _animationManager.FadeOut(_mainContainer, 0.3f, EasingType.EaseOut, onComplete: () =>
                {
                    _loginPanel.Visible = false;
                    _registerPanel.Visible = false;
                    _mainContainer.Visible = false;

                    FlaxEngine.Debug.Log("[ShowCharacterPanel] 认证UI已隐藏，触发角色选择场景切换");

                    // 通过状态管理器切换到角色选择界面
                    _stateManager.TransitionToScene(SceneType.CharacterSelection);
                });
            });
        }

        /// <summary>
        /// 隐藏登录面板
        /// </summary>
        public void HideLoginPanel()
        {
            RunOnMainThread(() =>
            {
                _animationManager.FadeOut(_loginPanel, 0.3f, EasingType.EaseOut, onComplete: () =>
                {
                    _loginPanel.Visible = false;
                });
            });
        }

        /// <summary>
        /// 显示注册面板
        /// </summary>
        public void ShowRegisterPanel()
        {
            RunOnMainThread(() =>
            {
                if (_registerPanel == null)
                {
                    _registerPanel = RegisterPanel.Create();
                    _mainContainer.AddChild(_registerPanel);
                    _registerPanel.RegisterButtonClicked += OnRegisterButtonClicked;
                    _registerPanel.SwitchToLoginClicked += OnSwitchToLoginClicked;
                    _registerPanel.SendVerificationCodeClicked += OnSendVerificationCodeClicked;
                }

                // 1. 停止动画并彻底重置物理状态
                _animationManager?.StopAnimations(_registerPanel);

                if (_loginPanel != null) _loginPanel.Visible = false;
                _registerPanel.Visible = true;
                _registerPanel.Enabled = true;

                // 2. 强制中心对齐，修复可能的位置偏差
                _registerPanel.Size = ChineseClassicalTheme.GoldenRatioLayout.CalculateRegisterPanelSize(); // 确保正确的尺寸
                _registerPanel.AnchorPreset = AnchorPresets.MiddleCenter;
                _registerPanel.Pivot = new Float2(0f, 0.5f);
                _registerPanel.Location = new Float2(-_registerPanel.Size.X / 2f, 0);
                _registerPanel.Scale = Float2.One;

                _registerPanel.BackgroundColor = UIStyleTokens.BgPanel; // 墨水深背景面板（--ink-bg-panel）

                _mainContainer.Visible = true;
                _mainContainer.Enabled = true;

                // 检查并更新按钮状态
                CheckAndUpdateButtonStates();

                FlaxEngine.Debug.Log("[ShowRegisterPanel] 注册面板已重置并居中");

                if (_animationManager != null)
                {
                    _animationManager.SlideIn(_registerPanel, new Float2(300, 0), 0.4f);
                }
            });
        }

        /// <summary>
        /// 隐藏注册面板
        /// </summary>
        public void HideRegisterPanel()
        {
            RunOnMainThread(() =>
            {
                _animationManager.FadeOut(_registerPanel, 0.3f, EasingType.EaseOut, onComplete: () =>
                {
                    _registerPanel.Visible = false;
                });
            });
        }

        /// <summary>
        /// 显示认证界面
        /// </summary>
        public void ShowAuthenticationUI()
        {
            RunOnMainThread(() =>
            {
                // Login.scene 自带一套认证页（AuthenticationController 管理）。两套都显示会叠成双份登录卡，
                // 有场景那套就只让场景那套出面，本组件退化成事件订阅者。
                var sceneAuth = global::Game.AuthenticationController.Current;
                if (sceneAuth != null)
                {
                    sceneAuth.ShowAuthUI();
                    return;
                }

                _mainContainer.Visible = true;
                _mainContainer.Enabled = true;
                _ = ShowLoginPanelInternal();
            });
        }

        /// <summary>
        /// 隐藏认证界面
        /// </summary>
        public void HideAuthenticationUI()
        {
            RunOnMainThread(() =>
            {
                global::Game.AuthenticationController.Current?.HideAuthUI();
                if (_mainContainer == null) return;

                // 彻底放弃淡出动画，改为立即隐藏，确保在场景切换时无残留
                _animationManager?.StopAnimations(_mainContainer);
                if (_loginPanel != null) _animationManager?.StopAnimations(_loginPanel);
                if (_registerPanel != null) _animationManager?.StopAnimations(_registerPanel);

                _mainContainer.Visible = false;
                _mainContainer.Enabled = false;

                if (_loginPanel != null) _loginPanel.Visible = false;
                if (_registerPanel != null) _registerPanel.Visible = false;

                FlaxEngine.Debug.Log("[AuthenticationUI] 认证UI已立即隐藏");
            });
        }

        #endregion

        /// <summary>
        /// 查找UICanvas组件
        /// </summary>
        private UICanvas FindUICanvas()
        {
            // 方法1：从当前Actor查找
            var canvas = Actor.GetScript<UICanvas>();
            if (canvas != null) return canvas;

            // 方法2：从父Actor查找
            if (Actor.Parent != null)
            {
                canvas = Actor.Parent.GetScript<UICanvas>();
                if (canvas != null) return canvas;
            }

            // 方法3：从场景中查找名为UICanvas的Actor
            var uiCanvasActor = Level.FindActor("UICanvas");
            if (uiCanvasActor != null)
            {
                canvas = uiCanvasActor.GetScript<UICanvas>();
                if (canvas != null) return canvas;
            }

            // 方法4：查找所有UICanvas组件
            var allActors = Level.GetActors<Actor>();
            foreach (var actor in allActors)
            {
                canvas = actor.GetScript<UICanvas>();
                if (canvas != null) return canvas;
            }

            return null;
        }

        /// <summary>
        /// 尝试添加到根控件（备用方案）
        /// </summary>
        private void TryAddToRootControl()
        {
            try
            {
                // 在Flax Engine中，可以尝试直接使用RootControl
                // 或者创建一个新的UICanvas
                FlaxEngine.Debug.Log("尝试使用备用方案添加UI容器");

                // 检查是否可以创建新的UICanvas
                CreateFallbackUICanvas();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"备用方案也失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 创建备用UICanvas
        /// </summary>
        private void CreateFallbackUICanvas()
        {
            // 创建一个新的UICanvas Actor
            var canvasActor = new EmptyActor();
            canvasActor.Name = "MainUICanvas";

            // 添加UICanvas组件
            var canvas = canvasActor.AddChild<UICanvas>();

            // 有意常驻 RootScene（0 号场景）：主 UI Canvas 是全局共享组件，切场景不销毁。
            // 显式传场景而非依赖"无父级默认落 Scenes[0]"的隐式行为。
            Level.SpawnActor(canvasActor, Level.GetScene(0));

            // 添加UI容器
            if (canvas?.GUI != null)
            {
                // 关键修复：主UI画布应铺满全屏
                canvas.GUI.AnchorPreset = AnchorPresets.StretchAll;
                canvas.GUI.Pivot = new Float2(0.5f, 0.5f);
                canvas.GUI.Offsets = Margin.Zero;
                canvas.GUI.AddChild(_mainContainer);
                FlaxEngine.Debug.Log("成功创建并使用备用UICanvas");
            }
        }

        public override void OnDestroy()
        {
            // 停止状态轮询
            StopStatusPolling();
            _statusCheckCts?.Dispose();
            
            // 取消事件订阅
            if (_stateManager != null)
            {
                _stateManager.SceneChanged -= OnSceneChanged;
                _stateManager.LoadingStateChanged -= OnLoadingStateChanged;
                _stateManager.ErrorOccurred -= OnErrorOccurred;
            }

            if (_authManager != null)
            {
                _authManager.LoginResponseReceived -= OnLoginResponseReceived;
                _authManager.RegisterResponseReceived -= OnRegisterResponseReceived;
            }

            if (_networkManager != null)
            {
                _networkManager.ConnectionStatusChanged -= OnNetworkConnectionStatusChanged;
            }

            // 取消面板事件订阅，防止按钮激活关联的资源泄漏
            if (_loginPanel != null)
            {
                _loginPanel.LoginButtonClicked -= OnLoginButtonClicked;
                _loginPanel.SwitchToRegisterClicked -= OnSwitchToRegisterClicked;
            }

            if (_registerPanel != null)
            {
                _registerPanel.RegisterButtonClicked -= OnRegisterButtonClicked;
                _registerPanel.SwitchToLoginClicked -= OnSwitchToLoginClicked;
                _registerPanel.SendVerificationCodeClicked -= OnSendVerificationCodeClicked;
            }

            // 清理资源
            _mainContainer?.Dispose();
        }

        // 属性
        public bool IsVisible => _mainContainer?.Visible ?? false;
        public SceneType CurrentScene => _loginPanel.Visible ? SceneType.Login : SceneType.Register;
    }

}
