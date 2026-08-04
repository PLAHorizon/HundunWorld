using Horizon.Game.Message.Network;
using System;
using System.Text;
using System.Threading.Tasks;
using FlaxEngine;
using HundunWorld.Game.Services;
using HundunWorld.Game.UI.ErrorHandling;
using HundunWorld.Game.UI.Events;
using HundunWorld.Game.UI.Controllers;
using HundunWorld.Game.UI.States;
using Horizon.Game.Message.Enums;
using Game.Database;
using HundunWorld.Game.Network;
using Horizon.Game.Message;

namespace HundunWorld.Game.UI.Authentication
{
    /// <summary>
    /// 认证管理器 - 重构版本
    /// 专注于认证业务逻辑，与新架构集成
    /// 遵循单一职责原则，简化职责范围
    /// </summary>
    public class AuthenticationManager
    {
        private static AuthenticationManager _instance;
        public static AuthenticationManager Instance => _instance ??= new AuthenticationManager();

        public static bool IsLaunchedFromGengDi => HorizonGameIniReader.TryRead()?.IsValid == true;

        /// <summary>
        /// 重置单例实例 - 在编辑器Stop/Play之间调用，防止事件订阅和状态残留
        /// </summary>
        public static void ResetInstance()
        {
            if (_instance != null)
            {
                // 先取消UIEventBus订阅，防止事件处理器累积
                _instance._eventBus?.UnsubscribeAll("AuthenticationManager");

                _instance.LoginResponseReceived = null;
                _instance.RegisterResponseReceived = null;
                _instance.AuthenticationStateChanged = null;
                _instance._loginTcs?.TrySetCanceled();
                _instance._loginTcs = null;
                _instance._isAuthenticating = false;
                _instance._authToken = "";
            }
            _instance = null;
        }

        public LiteDataContext.PassportInfo Passport { get; internal set; }

        public string AuthToken => _authToken;
        
        /// <summary>
        /// 游戏ID
        /// </summary>
        public uint GameId { get; set; } = 1;
        
        /// <summary>
        /// 区域ID
        /// </summary>
        public uint AreaId { get; set; } = 1;
        
        /// <summary>
        /// 服务器ID
        /// </summary>
        public uint ServerId { get; set; } = 1;
        
        /// <summary>
        /// 区域ID
        /// </summary>
        public uint ZoneId { get; set; } = 1;
        private string _authToken = "";

        // 核心管理器
        private UIStateManager _stateManager;
        private UIEventBus _eventBus;
        private ErrorHandler _errorHandler;
        private AnimationController _animationController;

        // 认证状态
        private bool _isAuthenticating = false;
        private string _currentUsername = "";
        private bool _rememberPassword = false;
        private TaskCompletionSource<LoginResponse> _loginTcs;

        // 事件
        public event Action<LoginResponse> LoginResponseReceived;
        public event Action<RegisterResponse> RegisterResponseReceived;
        public event Action<bool> AuthenticationStateChanged;

        private AuthenticationManager()
        {
            InitializeManagers();
        }

        /// <summary>
        /// 初始化管理器
        /// </summary>
        private void InitializeManagers()
        {
            _stateManager = UIStateManager.Instance;
            _eventBus = UIEventBus.Instance;
            _errorHandler = ErrorHandler.Instance;
            _animationController = AnimationController.Instance;
            
            // 订阅事件
            _eventBus.Subscribe<ErrorOccurredEvent>(OnErrorOccurred, subscriberName: "AuthenticationManager");
            _eventBus.Subscribe<NetworkStateChangedEvent>(OnNetworkStateChanged, subscriberName: "AuthenticationManager");
        }

        public async Task<AuthenticationResult> LoginAsync(string username, string password, bool rememberPassword = false)
        {
            if (_isAuthenticating) 
                return new AuthenticationResult { IsSuccess = false, ErrorMessage = "正在进行认证操作" };
            
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
                return new AuthenticationResult { IsSuccess = false, ErrorMessage = "用户名和密码不能为空" };

            _isAuthenticating = true;
            _currentUsername = username;
            _rememberPassword = rememberPassword;
            
            try
            {
                await SetLoadingStateAsync("正在登录...");
                
                var loginResult = await GengDiAuthService.Instance.LoginAsync(username, password);
                
                if (!loginResult.IsSuccess)
                    return new AuthenticationResult { IsSuccess = false, ErrorMessage = loginResult.ErrorMessage ?? "用户名或密码错误" };

                var connectionError = await EnsureNetworkConnectionAsync();
                if (connectionError != null)
                    return connectionError;

                // 服务端已废弃 TCP TokenLoginRequest，改为 WebApi /Account/signin 直接返回 ImAuthToken。
                // 将 ImAuthToken 作为 AuthToken 使用，直接完成登录后续流程。
                var loginResponse = new LoginResponse
                {
                    IsSuccess = true,
                    PassportId = username,
                    UserId = loginResult.UserId,
                    AuthToken = loginResult.ImAuthToken ?? string.Empty,
                    SessionToken = loginResult.ImAuthToken ?? string.Empty,
                    Message = "WebApi 登录成功"
                };

                Passport = new LiteDataContext.PassportInfo
                {
                    PassportId = username,
                    UserId = loginResult.UserId,
                    IsCurrentPassport = true,
                    Token = loginResult.ImAuthToken ?? string.Empty,
                    RememberPassword = rememberPassword
                };

                await FinalizeLoginAsync(loginResponse);

                if (rememberPassword)
                    SaveLoginInfo(username, password);

                return new AuthenticationResult { IsSuccess = true };
            }
            catch (Exception ex)
            {
                _loginTcs = null;
                _errorHandler.HandleError(UIErrorType.Network, $"登录过程中发生错误: {ex.Message}", ex, "login_process");
                return new AuthenticationResult { IsSuccess = false, ErrorMessage = ex.Message };
            }
            finally
            {
                _isAuthenticating = false;
                await ClearLoadingStateAsync();
            }
        }

        public async Task<AuthenticationResult> LoginWithTokenAsync(string authToken, string passportId, long userId)
        {
            if (_isAuthenticating)
                return new AuthenticationResult { IsSuccess = false, ErrorMessage = "正在进行认证操作" };

            if (string.IsNullOrEmpty(authToken))
                return new AuthenticationResult { IsSuccess = false, ErrorMessage = "AuthToken不能为空" };

            _isAuthenticating = true;
            _currentUsername = passportId ?? "";

            try
            {
                await SetLoadingStateAsync("正在使用Token登录...");

                var connectionError = await EnsureNetworkConnectionAsync();
                if (connectionError != null)
                    return connectionError;

                // 服务端已废弃 TCP TokenLoginRequest，ImAuthToken 直接作为 AuthToken 使用。
                var loginResponse = new LoginResponse
                {
                    IsSuccess = true,
                    PassportId = passportId ?? string.Empty,
                    UserId = (ulong)userId,
                    AuthToken = authToken,
                    SessionToken = authToken,
                    Message = "Token 登录成功"
                };

                Passport = new LiteDataContext.PassportInfo
                {
                    PassportId = passportId ?? string.Empty,
                    UserId = (ulong)userId,
                    IsCurrentPassport = true,
                    Token = authToken,
                    RememberPassword = true
                };

                await FinalizeLoginAsync(loginResponse);

                return new AuthenticationResult { IsSuccess = true };
            }
            catch (Exception ex)
            {
                _loginTcs = null;
                _errorHandler.HandleError(UIErrorType.Network, $"Token登录过程中发生错误: {ex.Message}", ex, "token_login_process");
                return new AuthenticationResult { IsSuccess = false, ErrorMessage = ex.Message };
            }
            finally
            {
                _isAuthenticating = false;
                await ClearLoadingStateAsync();
            }
        }

        public async Task<AuthenticationResult> TryAutoLoginAsync()
        {
            try
            {
                var iniConfig = HorizonGameIniReader.TryRead();
                if (iniConfig == null || !iniConfig.IsValid)
                {
                    Debug.Log("[AuthenticationManager] HorizonGame.ini 不存在或无效，跳过自动登录");
                    return new AuthenticationResult { IsSuccess = false, ErrorMessage = "无自动登录信息" };
                }

                Debug.Log($"[AuthenticationManager] 检测到 HorizonGame.ini，尝试Token自动登录: {iniConfig.User.PassportId}");

                Passport = new LiteDataContext.PassportInfo
                {
                    PassportId = iniConfig.User.PassportId,
                    UserId = (ulong)iniConfig.User.UserId,
                    RememberPassword = true
                };

                return await LoginWithTokenAsync(
                    iniConfig.Auth.AuthToken,
                    iniConfig.User.PassportId,
                    iniConfig.User.UserId
                );
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AuthenticationManager] 自动登录异常: {ex.Message}");
                return new AuthenticationResult { IsSuccess = false, ErrorMessage = $"自动登录失败: {ex.Message}" };
            }
        }

        /// <summary>
        /// 注册异步方法（重构版本，使用公共方法减少重复代码）
        /// </summary>
        public async Task<AuthenticationResult> RegisterAsync(string username, string password, string email, string phone, string verificationCode)
        {
            if (_isAuthenticating) 
                return new AuthenticationResult { IsSuccess = false, ErrorMessage = "正在进行认证操作" };
            
            if (string.IsNullOrEmpty(email) && string.IsNullOrEmpty(phone))
                return new AuthenticationResult { IsSuccess = false, ErrorMessage = "邮箱和手机号至少填写一个" };

            if (string.IsNullOrEmpty(verificationCode))
                return new AuthenticationResult { IsSuccess = false, ErrorMessage = "请输入验证码" };

            _isAuthenticating = true;
            
            try
            {
                await SetLoadingStateAsync("正在注册...");
                
                var connectionError = await EnsureNetworkConnectionAsync();
                if (connectionError != null)
                    return connectionError;
                
                var registerRequest = new RegisterRequest
                {
                    NickName = username,
                    Password = Base64Encode(password),
                    Email = email,
                    PhoneNumber = phone,
                    VerificationCode = verificationCode,
                    ClientVersion = "1.0.0",
                    PlatformId = "Windows",
                    DeviceId = System.Guid.NewGuid().ToString(),
                    RealName = "",
                    ID = ""
                };

                var messagePacket = CreateAuthMessagePacket(registerRequest, MessageType.RegisterRequest);
                var success = await HundunWorldGame.Instance.NetworkManager.SendMessageAsync(messagePacket);
                
                if (success)
                {
                    AuthenticationStateChanged?.Invoke(true);
                    return new AuthenticationResult { IsSuccess = true };
                }
                else
                {
                    return new AuthenticationResult { IsSuccess = false, ErrorMessage = "注册失败，请稍后重试" };
                }
            }
            catch (Exception ex)
            {
                _errorHandler.HandleError(UIErrorType.Network, $"注册过程中发生错误: {ex.Message}", ex, "register_process");
                return new AuthenticationResult { IsSuccess = false, ErrorMessage = ex.Message };
            }
            finally
            {
                _isAuthenticating = false;
                await ClearLoadingStateAsync();
            }
        }

        /// <summary>
        /// 登出异步方法
        /// </summary>
        public async Task<AuthenticationResult> LogoutAsync()
        {
            try
            {
                // 清除用户会话
                await _stateManager.UpdateUserSessionAsync(new UserSession
                {
                    
                    UserId = 0,
                    Username = "",
                    SessionToken = "",
                    LoginTime = DateTime.MinValue
                });

                AuthenticationStateChanged?.Invoke(false);
                return new AuthenticationResult { IsSuccess = true };
            }
            catch (Exception ex)
            {
                _errorHandler.HandleError(UIErrorType.System, $"登出过程中发生错误: {ex.Message}", ex, "logout_process");
                return new AuthenticationResult { IsSuccess = false, ErrorMessage = ex.Message };
            }
        }

        /// <summary>
        /// 发送验证码（重构版本，使用公共方法减少重复代码）
        /// </summary>
        public async Task<AuthenticationResult> SendVerificationCodeAsync(string email, string phone)
        {
            if (string.IsNullOrEmpty(email) && string.IsNullOrEmpty(phone))
                return new AuthenticationResult { IsSuccess = false, ErrorMessage = "请先填写手机号或邮箱" };

            try
            {
                var connectionError = await EnsureNetworkConnectionAsync();
                if (connectionError != null)
                    return connectionError;
                
                var verificationRequest = new VerificationCodeRequest
                {
                    Email = email,
                    PhoneNumber = phone,
                    Purpose = VerificationPurpose.Register,
                    ClientIP = "127.0.0.1"
                };
                
                var messagePacket = CreateAuthMessagePacket(verificationRequest, MessageType.VerificationCodeRequest);
                var success = await HundunWorldGame.Instance.NetworkManager.SendMessageAsync(messagePacket);
                
                if (success)
                {
                    Debug.Log("验证码请求已发送");
                    return new AuthenticationResult { IsSuccess = true, ErrorMessage = "验证码已发送，请查收" };
                }
                else
                {
                    return new AuthenticationResult { IsSuccess = false, ErrorMessage = "验证码发送失败，请稍后重试" };
                }
            }
            catch (Exception ex)
            {
                _errorHandler.HandleError(UIErrorType.Network, $"发送验证码失败: {ex.Message}", ex, "verification_code");
                return new AuthenticationResult { IsSuccess = false, ErrorMessage = ex.Message };
            }
        }

        public void NotifyLoginResponse(LoginResponse response)
        {
            _loginTcs?.TrySetResult(response);
            FlaxEngine.Debug.Log($"[AuthenticationManager] NotifyLoginResponse: IsSuccess={response.IsSuccess}, _loginTcs存在={_loginTcs != null}");
        }

        /// <summary>
        /// WebApi 登录/Token 登录成功后统一收尾：同步会话、查询角色列表、切换场景。
        /// 替代已废弃的 TCP TokenLoginResponse 处理路径。
        /// </summary>
        private async Task FinalizeLoginAsync(LoginResponse response)
        {
            FlaxEngine.Debug.Log($"[AuthenticationManager] 开始完成登录后续流程: UserId={response.UserId}, PassportId={response.PassportId}");

            // 同步到 NetworkManager（供心跳等后台任务使用）
            var networkManager = HundunWorldGame.Instance.NetworkManager;
            if (networkManager != null)
            {
                networkManager.GameId = GameId;
                networkManager.ZoneId = ZoneId;
                networkManager.ServerId = ServerId;
                networkManager.UserId = response.UserId;
                networkManager.AuthToken = response.AuthToken ?? "";
            }

            // 更新本地会话字段
            if (Passport != null)
                Passport.UserId = response.UserId;
            _authToken = response.AuthToken ?? "";

            // 触发登录响应事件（兼容旧订阅者）
            LoginResponseReceived?.Invoke(response);
            AuthenticationStateChanged?.Invoke(true);

            // 在 UI 线程继续：更新状态、查询角色、切换场景
            var tcs = new TaskCompletionSource<bool>();
            FlaxEngine.Scripting.InvokeOnUpdate(async () =>
            {
                try
                {
                    // 1. 更新用户会话状态
                    var stateManager = UIStateManager.Instance;
                    if (stateManager != null)
                    {
                        stateManager.HandleLoginSuccess(
                            response.PassportId,
                            response.UserId,
                            response.SessionToken ?? "",
                            response.AuthToken ?? ""
                        );
                        FlaxEngine.Debug.Log("[AuthenticationManager] 用户会话已更新");
                    }

                    // 2. 发送角色列表查询请求
                    FlaxEngine.Debug.Log("[AuthenticationManager] 准备查询角色列表...");
                    var characterListRequest = new CharacterListRequest
                    {
                        UserId = response.UserId,
                        ServerId = 1
                    };

                    var messagePacket = new HorizonMessagePacket
                    {
                        Header = new MessageHeader
                        {
                            MessageId = Guid.NewGuid().ToString(),
                            MessageType = MessageType.CharacterList,
                            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                            AuthToken = response.AuthToken ?? "",
                            GameId = 1,
                            ServerId = 1,
                            ZoneId = 1,
                            UserId = response.UserId,
                            MachineId = MachineIdentifier.GetMachineGuid()
                        },
                        ServiceType = ServiceType.Game,
                        Body = characterListRequest
                    };

                    // 发送角色列表查询请求（带重试机制，确保请求不会因瞬时状态问题静默丢失）
                    bool sendSuccess = false;
                    int maxRetries = 3;
                    for (int attempt = 1; attempt <= maxRetries; attempt++)
                    {
                        try
                        {
                            var nm = HundunWorldGame.Instance.NetworkManager;
                            if (nm == null)
                            {
                                FlaxEngine.Debug.LogError("[AuthenticationManager] NetworkManager 为空，无法发送角色列表请求");
                                break;
                            }

                            // 确保连接已就绪（ConnectAsync 返回 true 后连接状态可能还未刷新）
                            if (nm.GetConnectionStatus() != ConnectionStatus.Connected)
                            {
                                FlaxEngine.Debug.Log($"[AuthenticationManager] 连接尚未就绪，等待连接建立... (尝试 {attempt}/{maxRetries})");
                                var ready = await nm.WaitForConnectionAsync(3000);
                                if (!ready)
                                {
                                    FlaxEngine.Debug.LogWarning($"[AuthenticationManager] 等待连接超时 (尝试 {attempt}/{maxRetries})");
                                    if (attempt < maxRetries) { await Task.Delay(500); continue; }
                                    break;
                                }
                            }

                            sendSuccess = await nm.SendMessageAsync(messagePacket);
                            if (sendSuccess)
                            {
                                FlaxEngine.Debug.Log($"[AuthenticationManager] 角色列表查询请求已发送 (尝试 {attempt}/{maxRetries})");
                                break;
                            }

                            FlaxEngine.Debug.LogWarning($"[AuthenticationManager] 发送角色列表请求返回 false (尝试 {attempt}/{maxRetries})");
                            if (attempt < maxRetries) { await Task.Delay(500); }
                        }
                        catch (Exception ex)
                        {
                            FlaxEngine.Debug.LogError($"[AuthenticationManager] 发送角色列表查询请求异常 (尝试 {attempt}/{maxRetries}): {ex.Message}");
                            if (attempt < maxRetries) { await Task.Delay(500); }
                        }
                    }

                    if (!sendSuccess)
                    {
                        FlaxEngine.Debug.LogError("[AuthenticationManager] 角色列表查询请求发送失败，已达到最大重试次数");
                        UIHelper.ShowError("获取角色列表失败，请检查网络后重新登录");
                    }

                    // 3. 切换到角色选择场景
                    var sceneManager = GameSceneManager.GetOrCreate();
                    if (sceneManager != null)
                    {
                        FlaxEngine.Debug.Log("[AuthenticationManager] 使用GameSceneManager切换场景");

                        void OnTransitionCompleted(SceneType from, SceneType to)
                        {
                            sceneManager.TransitionCompleted -= OnTransitionCompleted;
                            FlaxEngine.Debug.Log($"[AuthenticationManager] 场景切换完成: {from} -> {to}");

                            var sm = UIStateManager.Instance;
                            if (sm != null && sm.CurrentScene != to)
                            {
                                sm.TransitionToScene(to, false);
                            }
                        }

                        sceneManager.TransitionCompleted += OnTransitionCompleted;

                        if (!sceneManager.TransitionTo(SceneType.CharacterSelection))
                        {
                            sceneManager.TransitionCompleted -= OnTransitionCompleted;
                            FlaxEngine.Debug.LogError("[AuthenticationManager] 场景切换启动失败");
                            UIHelper.ShowError("场景切换失败，请重试");
                        }
                        else
                        {
                            FlaxEngine.Debug.Log("[AuthenticationManager] 场景切换已启动，等待加载完成");
                        }
                    }
                    else
                    {
                        FlaxEngine.Debug.LogError("[AuthenticationManager] GameSceneManager未初始化");
                        UIHelper.ShowError("系统错误，请重启游戏");
                    }
                }
                catch (Exception ex)
                {
                    FlaxEngine.Debug.LogException(ex);
                    FlaxEngine.Debug.LogError($"[AuthenticationManager] 完成登录后续流程时发生异常: {ex.Message}");
                }
                finally
                {
                    tcs.TrySetResult(true);
                }
            });

            await tcs.Task;
            FlaxEngine.Debug.Log($"[AuthenticationManager] 登录后续流程完成: {response.PassportId}");
        }

        /// <summary>
        /// 处理登录响应（主要由旧 TCP 登录路径调用；WebApi 登录路径使用 FinalizeLoginAsync）
        /// </summary>
        public async void HandleLoginResponse(LoginResponse response)
        {
            FlaxEngine.Debug.Log($"[AuthenticationManager] 开始处理登录响应: IsSuccess={response.IsSuccess}, Message={response.Message}");
            try
            {
                if (response.IsSuccess)
                {
                    FlaxEngine.Debug.Log($"[AuthenticationManager] 登录成功: {response.PassportId}");
                    
                    // 同步 GameId/ZoneId/ServerId/UserId/AuthToken 到 NetworkManager（供心跳等后台任务使用）
                    var networkManager = HundunWorldGame.Instance.NetworkManager;
                    if (networkManager != null)
                    {
                        networkManager.GameId = GameId;
                        networkManager.ZoneId = ZoneId;
                        networkManager.ServerId = ServerId;
                        networkManager.UserId = response.UserId;
                        networkManager.AuthToken = response.AuthToken ?? "";
                    }
                    
                    // 更新本地会话字段
                    Passport.UserId = response.UserId;
                    _authToken = response.AuthToken ?? "";
                    
                    // 更新用户会话
                    var userSession = new UserSession
                    {
                        UserId = response.UserId,
                        Username = response.PassportId ?? _currentUsername,
                        SessionToken = response.SessionToken ?? "",
                        LoginTime = DateTime.Now
                    };
                    await _stateManager.UpdateUserSessionAsync(userSession);
                    
                    // 发布事件
                    await _eventBus.PublishAsync(new UserSessionChangedEvent
                    {
                        OldSession = null,
                        NewSession = userSession
                    });
                    
                    // 触发登录响应事件
                    FlaxEngine.Debug.Log($"[AuthenticationManager] 触发 LoginResponseReceived 事件，订阅者数量: {(LoginResponseReceived != null ? LoginResponseReceived.GetInvocationList().Length : 0)}");
                    LoginResponseReceived?.Invoke(response);
                    AuthenticationStateChanged?.Invoke(true);
                    
                    Debug.Log($"[AuthenticationManager] 登录成功处理完成: {response.PassportId}");
                }
                else
                {
                    // 处理登录失败
                    FlaxEngine.Debug.LogWarning($"[AuthenticationManager] 登录失败: {response.Message}");
                    _errorHandler.HandleError(UIErrorType.Authentication, response.Message ?? "登录失败", null, "login_response");
                    LoginResponseReceived?.Invoke(response);
                    AuthenticationStateChanged?.Invoke(false);
                }
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[AuthenticationManager] 处理登录响应时发生异常: {ex.Message}");
                FlaxEngine.Debug.LogError($"[AuthenticationManager] 异常堆栈: {ex.StackTrace}");
                _errorHandler.HandleError(UIErrorType.System, $"处理登录响应失败: {ex.Message}", ex, "login_response");
            }
        }

        /// <summary>
        /// 处理注册响应（注册成功后切换到登录界面并自动填充）
        /// </summary>
        public async void HandleRegisterResponse(RegisterResponse response)
        {
            try
            {
                if (response.IsSuccess)
                {
                    FlaxEngine.Debug.Log($"[HandleRegisterResponse] 注册成功，PassportId: {response.PassportId}");
                    
                    // 获取已保存的密码
                    string password = Passport?.Password ?? "";
                    
                    // 触发事件
                    RegisterResponseReceived?.Invoke(response);
                    AuthenticationStateChanged?.Invoke(true);
                    
                    // 发布成功事件
                    await _eventBus.PublishAsync(new ConfigurationChangedEvent
                    {
                        Key = "registration_status",
                        OldValue = false,
                        NewValue = true
                    });
                    
                    // 切换到登录界面并自动填充（通过UISwitchController）
                }
                else
                {
                    _errorHandler.HandleError(UIErrorType.Validation, response.ErrorMessage ?? "注册失败", null, "registration");
                    RegisterResponseReceived?.Invoke(response);
                }
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[HandleRegisterResponse] 处理注册响应异常: {ex.Message}");
                _errorHandler.HandleError(UIErrorType.System, $"处理注册响应失败: {ex.Message}", ex, "register_response");
            }
        }

        #region 辅助方法

        /// <summary>
        /// 保存登录信息
        /// </summary>
        private void SaveLoginInfo(string username, string password)
        {
            try
            {
                // 使用加密方式保存密码（简单示例，实际应用中应该使用更强的加密）
                var encryptedPassword = Base64Encode(password);
                
                // 保存到本地存储（使用DatabaseManager替代PlayerPrefs）
                DatabaseManager.SetConfig("SavedUsername", username, "Authentication");
                DatabaseManager.SetConfig("SavedPassword", encryptedPassword, "Authentication");
                DatabaseManager.SetConfig("RememberPassword", true, "Authentication");
                
                Debug.Log("登录信息已安全保存");
            }
            catch (Exception ex)
            {
                Debug.LogError($"保存登录信息失败: {ex.Message}");
                // 即使保存失败也不影响登录流程
            }
        }

        /// <summary>
        /// 加载保存的登录信息
        /// </summary>
        public (string username, string password, bool remember) LoadSavedLoginInfo()
        {
            try
            {
                var remember = DatabaseManager.GetConfig("RememberPassword", "Authentication", false);
                if (remember)
                {
                    var username = DatabaseManager.GetConfig("SavedUsername", "Authentication", "");
                    var encryptedPassword = DatabaseManager.GetConfig("SavedPassword", "Authentication", "");
                    var password = Base64Decode(encryptedPassword);
                    
                    return (username, password, remember);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"加载登录信息失败: {ex.Message}");
            }
            
            return ("", "", false);
        }

        /// <summary>
        /// 清除保存的登录信息
        /// </summary>
        public void ClearSavedLoginInfo()
        {
            try
            {
                DatabaseManager.SetConfig("SavedUsername", "", "Authentication");
                DatabaseManager.SetConfig("SavedPassword", "", "Authentication");
                DatabaseManager.SetConfig("RememberPassword", false, "Authentication");
                
                Debug.Log("已清除保存的登录信息");
            }
            catch (Exception ex)
            {
                Debug.LogError($"清除登录信息失败: {ex.Message}");
            }
        }

        /// <summary>
        /// Base64解码
        /// </summary>
        private string Base64Decode(string base64Text)
        {
            if (string.IsNullOrEmpty(base64Text)) return string.Empty;
            try
            {
                var bytes = System.Convert.FromBase64String(base64Text);
                return System.Text.Encoding.UTF8.GetString(bytes);
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Base64编码
        /// </summary>
        private string Base64Encode(string plainText)
        {
            if (plainText == null) return string.Empty;
            var bytes = System.Text.Encoding.UTF8.GetBytes(plainText);
            return System.Convert.ToBase64String(bytes);
        }

        #endregion

        #region 公共辅助方法

        /// <summary>
        /// 确保网络连接已建立（提取公共方法，避免重复代码）
        /// </summary>
        /// <returns>连接结果</returns>
        private async Task<AuthenticationResult> EnsureNetworkConnectionAsync()
        {
            var networkManager = HundunWorldGame.Instance.NetworkManager;
            var connectionStatus = networkManager.GetConnectionStatus();
            
            if (connectionStatus == ConnectionStatus.Connected)
                return null;

            Debug.Log("网络未连接，尝试建立连接...");

            // [连接精简治理 spec 5.1.1] 经单连接协调器编排登录建连：
            // 返回 false 表示连接已在线被复用或另有路径在建连，直接复用/等待，不再发起第二次 TCP 建连。
            var coordinator = HundunWorldGame.Instance.ConnectionCoordinator;
            if (coordinator != null)
            {
                var acquired = await coordinator.RequestConnectAsync(ClientConnectionRequestKind.Login);
                if (!acquired)
                {
                    // 已由其他路径建连或连接已在线：等待连接就绪后复用。
                    if (networkManager.GetConnectionStatus() != ConnectionStatus.Connected)
                        await networkManager.WaitForConnectionAsync(5000);
                    return networkManager.GetConnectionStatus() == ConnectionStatus.Connected
                        ? null
                        : new AuthenticationResult { IsSuccess = false, ErrorMessage = "无法连接到游戏服务器，请检查网络连接" };
                }
                // 实际执行建连成功：调用方发送登录认证首包。
                if (networkManager.GetConnectionStatus() != ConnectionStatus.Connected)
                    await networkManager.WaitForConnectionAsync(3000);
                coordinator.MarkFirstPacketSent();
                return null;
            }

            var currentGateway = networkManager.GetCurrentGateway();
            if (currentGateway != null)
            {
                var connected = await networkManager.ConnectAsync(currentGateway.IP, currentGateway.Port);
                if (!connected)
                {
                    var waitSuccess = await networkManager.WaitForConnectionAsync(5000);
                    if (!waitSuccess)
                        return new AuthenticationResult { IsSuccess = false, ErrorMessage = "无法连接到游戏服务器，请检查网络连接" };
                }
                else
                {
                    // ConnectAsync 返回 true 后连接状态可能还未刷新到 Connected，
                    // 需等待状态就绪后再返回，避免后续 SendMessageAsync 因状态判断失败而拒绝发送
                    if (networkManager.GetConnectionStatus() != ConnectionStatus.Connected)
                        await networkManager.WaitForConnectionAsync(3000);
                }
                return null;
            }

            var config = NetworkConfigManager.LoadConfig();
            if (config.GatewayList.Count == 0)
                return new AuthenticationResult { IsSuccess = false, ErrorMessage = "未配置游戏服务器信息" };

            var gateway = config.GatewayList[0];
            var fallbackConnected = await networkManager.ConnectAsync(gateway.IP, gateway.Port);

            if (!fallbackConnected)
            {
                var waitSuccess = await networkManager.WaitForConnectionAsync(5000);
                if (!waitSuccess)
                    return new AuthenticationResult { IsSuccess = false, ErrorMessage = "无法连接到游戏服务器，请检查网络连接" };
            }
            else
            {
                // ConnectAsync 返回 true 后连接状态可能还未刷新到 Connected，
                // 需等待状态就绪后再返回，避免后续 SendMessageAsync 因状态判断失败而拒绝发送
                if (networkManager.GetConnectionStatus() != ConnectionStatus.Connected)
                    await networkManager.WaitForConnectionAsync(3000);
            }

            return null;
        }

        /// <summary>
        /// 设置加载状态（提取公共方法）
        /// </summary>
        private async Task SetLoadingStateAsync(string message)
        {
            var currentState = _stateManager.GetCurrentState();
            await _stateManager.UpdateCurrentSceneAsync(new SceneState
            {
                SceneType = currentState.CurrentScene,
                LoadTime = DateTime.Now,
                IsLoading = true,
                LoadingMessage = message
            });
        }

        /// <summary>
        /// 清除加载状态（提取公共方法）
        /// </summary>
        private async Task ClearLoadingStateAsync()
        {
            var currentState = _stateManager.GetCurrentState();
            await _stateManager.UpdateCurrentSceneAsync(new SceneState
            {
                SceneType = currentState.CurrentScene,
                LoadTime = DateTime.Now,
                IsLoading = false
            });
        }

        /// <summary>
        /// 创建认证消息包（提取公共方法）
        /// </summary>
        private HorizonMessagePacket CreateAuthMessagePacket<T>(T body, MessageType messageType) where T :MessageUnion
        {
            return new HorizonMessagePacket(body)
            {
                ServiceType = ServiceType.Account,
                Header = new MessageHeader
                {
                    MessageType = messageType,
                    GameId = GameId,
                    ZoneId = ZoneId,
                    ServerId = ServerId,
                    MachineId = MachineIdentifier.GetMachineGuid(),
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                }
            };
        }

        #endregion

        #region 事件处理

        /// <summary>
        /// 错误事件处理
        /// </summary>
        private void OnErrorOccurred(ErrorOccurredEvent eventData)
        {
            Debug.LogError($"[AuthenticationManager] 错误发生: {eventData.ErrorMessage}");
        }

        /// <summary>
        /// 网络状态变更事件处理
        /// </summary>
        private void OnNetworkStateChanged(NetworkStateChangedEvent eventData)
        {
            Debug.Log($"[AuthenticationManager] 网络状态: {eventData.IsConnected}");
            
            if (!eventData.IsConnected && _isAuthenticating)
            {
                _errorHandler.HandleError(UIErrorType.Network, "网络连接断开", null, "network_disconnected");
            }
        }

        #endregion
    }

    /// <summary>
    /// 认证结果
    /// </summary>
    public class AuthenticationResult
    {
        public bool IsSuccess { get; set; }
        public string ErrorMessage { get; set; }
    }
}
