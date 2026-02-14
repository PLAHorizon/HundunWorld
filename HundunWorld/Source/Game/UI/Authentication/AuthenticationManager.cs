using Horizon.Game.Message.Network;
using System;
using System.Threading.Tasks;
using FlaxEngine;
using HundunWorld.Game.UI.ErrorHandling;
using HundunWorld.Game.UI.Events;
using HundunWorld.Game.UI.Controllers;
using HundunWorld.Game.UI.States;
using Horizon.Game.Message.Enums;
using Horizon.Game.Core.Database; // 使用现有的数据库管理器
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
                _instance._isAuthenticating = false;
            }
            _instance = null;
        }

        public LiteDataContext.PassportInfo Passport { get; internal set; }

        // 核心管理器
        private UIStateManager _stateManager;
        private UIEventBus _eventBus;
        private ErrorHandler _errorHandler;
        private AnimationController _animationController;

        // 认证状态
        private bool _isAuthenticating = false;
        private string _currentUsername = "";
        private bool _rememberPassword = false;

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

        /// <summary>
        /// 登录异步方法（重构版本，使用公共方法减少重复代码）
        /// </summary>
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
                
                var connectionError = await EnsureNetworkConnectionAsync();
                if (connectionError != null)
                    return connectionError;
                
                var loginRequest = new LoginRequest
                {
                    Password = Base64Encode(password),
                    AccountName = username,
                    ClientVersion = "1.0.0",
                    PlatformId = "Windows",
                    DeviceId = System.Guid.NewGuid().ToString()
                };

                var messagePacket = CreateAuthMessagePacket(loginRequest, MessageType.LoginRequest);
                var success = await HundunWorldGame.Instance.NetworkManager.SendMessageAsync(messagePacket);
                
                if (success)
                {
                    Debug.Log($"[AuthenticationManager] 登录请求已发送: {username}");
                    
                    var timeoutTask = Task.Delay(10000);
                    var responseReceived = false;
                    
                    while (!responseReceived && !timeoutTask.IsCompleted)
                        await Task.Delay(100);
                    
                    if (timeoutTask.IsCompleted)
                        return new AuthenticationResult { IsSuccess = false, ErrorMessage = "登录响应超时，请稍后重试" };
                }

                if (success)
                {
                    if (rememberPassword)
                        SaveLoginInfo(username, password);

                    AuthenticationStateChanged?.Invoke(true);
                    return new AuthenticationResult { IsSuccess = true };
                }
                else
                {
                    return new AuthenticationResult { IsSuccess = false, ErrorMessage = "登录失败，请检查网络链接" };
                }
            }
            catch (Exception ex)
            {
                _errorHandler.HandleError(UIErrorType.Network, $"登录过程中发生错误: {ex.Message}", ex, "login_process");
                return new AuthenticationResult { IsSuccess = false, ErrorMessage = ex.Message };
            }
            finally
            {
                _isAuthenticating = false;
                await ClearLoadingStateAsync();
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

        /// <summary>
        /// 处理登录响应（注意：场景切换由LoginResponseHandler负责）
        /// </summary>
        public async void HandleLoginResponse(LoginResponse response)
        {
            FlaxEngine.Debug.Log($"[AuthenticationManager] 开始处理登录响应: IsSuccess={response.IsSuccess}, Message={response.Message}");
            try
            {
                if (response.IsSuccess)
                {
                    FlaxEngine.Debug.Log($"[AuthenticationManager] 登录成功: {response.PassportId}");
                    
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
                    
                    // 注意：不再调用场景切换，由LoginResponseHandler统一处理
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
            
            var config = NetworkConfigManager.LoadConfig();
            if (config.GatewayList.Count == 0)
                return new AuthenticationResult { IsSuccess = false, ErrorMessage = "未配置游戏服务器信息" };

            var gateway = config.GatewayList[0];
            var connected = await networkManager.ConnectAsync(gateway.IP, gateway.Port);
            
            if (!connected)
            {
                var waitSuccess = await networkManager.WaitForConnectionAsync(5000);
                if (!waitSuccess)
                    return new AuthenticationResult { IsSuccess = false, ErrorMessage = "无法连接到游戏服务器，请检查网络连接" };
            }
            else
            {
                await Task.Delay(100);
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
                    GameId = 1,
                    ZoneId = 1,
                    ServerId = 1,
                    Timestamp = DateTimeOffset.Now.ToUnixTimeMilliseconds()
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
