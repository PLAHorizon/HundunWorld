using Horizon.Core.Security;
using Horizon.Game.GengDi.Data.Repositories;
using Horizon.Game.GengDi.Enums;
using Horizon.Game.GengDi.Models;
using Microsoft.Win32;
using Newtonsoft.Json;
using System;
using System.Buffers;
using System.Buffers.Text;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Game.GengDi.Core.Services
{
    public class AccountService
    {
        private const long DefaultAppId = 1;
        private const int DefaultAppType = 0;
        private const int DefaultPassportType = 0;

        private static readonly HttpClient HttpClient = CreateHttpClient();
        private static readonly object SessionLock = new();

        private static string _accessToken;
        private static string _refreshToken;
        private static DateTime _accessTokenExpiresAtUtc;
        private static string _imAuthToken;
        private static string _passportId;

        private readonly UserRepository _userRepository;
        private string _lastErrorMessage = string.Empty;

        public AccountService()
        {
            _userRepository = new UserRepository();
        }

        public string LastErrorMessage => _lastErrorMessage;

        public User Login(string username, string password)
        {
            return LoginAsync(username, password).GetAwaiter().GetResult();
        }
       
        public async Task<User> LoginAsync(string username, string password)
        {
            ClearLastError();

            var passportId = username?.Trim();
            if (string.IsNullOrWhiteSpace(passportId) || string.IsNullOrWhiteSpace(password))
            {
                SetLastError("请输入通行证和密码");
                return null;
            }

			var loginResult = await PostJsonAsync<LoginResultEnvelope>(
				"Account/signin",
				new
				{
					PassportId = passportId,
					Password = password,
					VerifyCode = string.Empty,
					Phone = string.Empty,
					Email = string.Empty,
					AppId = DefaultAppId,
					AppType = DefaultAppType,
					PassportType = DefaultPassportType,
					MachineId = MachineIdentifier.GetMachineGuid()
				}).ConfigureAwait(false);

            if (loginResult?.IsSuccess != true || loginResult.Data == null || string.IsNullOrWhiteSpace(loginResult.Data.AccessToken))
            {
                SetLastError(FirstNonEmpty(loginResult?.ErrorMessage, "登录失败，服务端未返回有效登录信息。"));
                return null;
            }

            SetSession(loginResult.Data);

            // 从 AccessToken 中提取 UserId 声明
            var userIdFromToken = TryExtractUserIdFromToken(loginResult.Data.AccessToken);

            // 登录成功后立即从 WebApi 拉取最新的 IM/Game 网关列表，供后续聊天、游戏连接使用。
            _ = GatewayDiscoveryService.RefreshAsync(force: true);

            var profile = await TryGetCurrentUserAsync().ConfigureAwait(false);
            // 优先使用 Token 中的 UserId，其次使用 RemoteUserEnvelope 中的 UserId
            if (profile != null && profile.UserId == Guid.Empty && userIdFromToken != Guid.Empty)
            {
                profile.UserId = userIdFromToken.Value;
            }
            var user = UpsertAuthenticatedUser(profile, passportId, password, string.Empty, passportId);
            SetCurrentPassportId(string.IsNullOrWhiteSpace(user?.PassportId) ? passportId : user.PassportId);
            
            return user;
        }

        public User Register(string username, string email, string password)
        {
            return RegisterAsync(username, email, password).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 获取机器唯一标识符（MachineGuid）。仅在 Windows 平台可用，其他平台返回 "UNSUPPORTED_PLATFORM"。
        /// </summary>
        /// <returns></returns>
        public static string GetMachineGuid()
        {
            // 如果不是 Windows 系统，直接返回后备方案
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return "UNSUPPORTED_PLATFORM";
            }

            try
            {
                // 打开注册表路径
                using RegistryKey registryKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");

                if (registryKey != null)
                {
                    // 读取 MachineGuid 的值
                    object value = registryKey.GetValue("MachineGuid");
                    if (value != null)
                    {
                        return value.ToString();
                    }
                }

                return "MACHINE_GUID_NOT_FOUND";
            }
            catch (UnauthorizedAccessException)
            {
                // 普通用户权限可能在某些特殊安全策略下无法读取 HKLM，这是一种兜底处理
                return "ACCESS_DENIED";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"获取 MachineGuid 失败: {ex.Message}");
                return "ERROR_GETTING_GUID";
            }
        }
        public async Task<User> RegisterAsync(string username, string email, string password)
        {
            ClearLastError();

            var displayName = username?.Trim();
            var normalizedEmail = email?.Trim();
            if (string.IsNullOrWhiteSpace(displayName) || string.IsNullOrWhiteSpace(normalizedEmail) || string.IsNullOrWhiteSpace(password))
            {
                SetLastError("请输入完整的注册信息");
                return null;
            }

			var registerResult = await PostJsonAsync<LoginResultEnvelope>(
				"Account/register",
				new
				{
					Password = password,
					Phone = string.Empty,
					Email = normalizedEmail,
					AppId = DefaultAppId,
					AppType = DefaultAppType,
					PassportType = DefaultPassportType,
					NickName = displayName,
					GameContext = new
					{
						GameId = (int)DefaultAppId,
						ServerId = 1,
						AreaId = 1,
						Ip = "192.168.1.78",
						PlatformId = Environment.OSVersion.Platform.ToString()
					},
					RealName = displayName,
					ID = Guid.NewGuid().ToString("N"),
					MachineId = MachineIdentifier.GetMachineGuid()
				}).ConfigureAwait(false);

            if (registerResult?.IsSuccess != true || registerResult.Data == null || string.IsNullOrWhiteSpace(registerResult.Data.AccessToken))
            {
                SetLastError(FirstNonEmpty(registerResult?.ErrorMessage, "注册失败，服务端未返回有效登录信息。"));
                return null;
            }

            SetSession(registerResult.Data);

            var profile = await TryGetCurrentUserAsync().ConfigureAwait(false);
            var registeredUser = UpsertAuthenticatedUser(profile, profile?.PassportId ?? displayName, password, normalizedEmail, displayName);
            SetCurrentPassportId(registeredUser.PassportId ?? string.Empty);
            return registeredUser;
        }

        public bool ChangePassword(string userId, string oldPassword, string newPassword)
        {
            return ChangePasswordAsync(userId, oldPassword, newPassword).GetAwaiter().GetResult();
        }

        public async Task<bool> ChangePasswordAsync(string userId, string oldPassword, string newPassword)
        {
            ClearLastError();

            var user = _userRepository.GetById(userId);
            if (user == null)
            {
                SetLastError("未找到当前用户");
                return false;
            }

            if (HasActiveSession())
            {
                var changePasswordResult = await PostJsonAsync<bool>(
                    "Account/changepassword",
                    new
                    {
                        PassportId = ResolvePassportId(user),
                        OldPassword = oldPassword,
                        NewPassword = newPassword,
                        AppId = DefaultAppId,
                        AppType = DefaultAppType,
                        PassportType = DefaultPassportType
                    },
                    requiresAuthentication: true).ConfigureAwait(false);

                if (changePasswordResult?.IsSuccess == true && changePasswordResult.Data)
                {
                    user.PasswordHash = HashPassword(newPassword);
                    _userRepository.Update(user);
                    return true;
                }

                SetLastError(FirstNonEmpty(changePasswordResult?.ErrorMessage, "修改密码失败"));
            }

            if (!VerifyPassword(oldPassword, user.PasswordHash))
            {
                SetLastError("旧密码不正确");
                return false;
            }

            user.PasswordHash = HashPassword(newPassword);
            _userRepository.Update(user);
            return true;
        }

        public bool UpdateProfile(string userId, string nickName, string bio, string avatar)
        {
            var user = _userRepository.GetById(userId);
            if (user == null)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(nickName))
            {
                user.Username = nickName.Trim();
            }

            user.Bio = bio;
            user.Avatar = avatar;
            _userRepository.Update(user);
            return true;
        }

        public Task<bool> UpdateProfileAsync(string userId, string nickName, string bio, string avatar)
        {
            return ClientAsyncDispatcher.RunLiteDbAsync(() => UpdateProfile(userId, nickName, bio, avatar));
        }

        /// <summary>
        /// 扩展版资料更新：同时保存性别、生日、地区、邮箱、手机号。
        /// </summary>
        public bool UpdateProfile(string userId, string nickName, string bio, string avatar,
            string gender, DateTime? birthday, string province, string city, string email, string phone)
        {
            var user = _userRepository.GetById(userId);
            if (user == null)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(nickName))
            {
                user.Username = nickName.Trim();
            }

            user.Bio = bio;
            user.Avatar = avatar;
            user.Gender = gender;
            user.Birthday = birthday;
            user.Province = province;
            user.City = city;
            user.Email = email;
            user.Phone = phone;
            _userRepository.Update(user);
            return true;
        }

        /// <summary>
        /// 扩展版资料更新（异步）。
        /// </summary>
        public Task<bool> UpdateProfileAsync(string userId, string nickName, string bio, string avatar,
            string gender, DateTime? birthday, string province, string city, string email, string phone)
        {
            return ClientAsyncDispatcher.RunLiteDbAsync(() =>
                UpdateProfile(userId, nickName, bio, avatar, gender, birthday, province, city, email, phone));
        }

        public bool UpdateTitle(string userId, string title)
        {
            var user = _userRepository.GetById(userId);
            if (user == null)
            {
                return false;
            }

            user.Title = title?.Trim() ?? string.Empty;
            _userRepository.Update(user);
            return true;
        }

        public Task<bool> UpdateTitleAsync(string userId, string title)
        {
            return ClientAsyncDispatcher.RunLiteDbAsync(() => UpdateTitle(userId, title));
        }

        public bool UpdateContactInfo(string userId, string phone, string email)
        {
            var user = _userRepository.GetById(userId);
            if (user == null)
            {
                return false;
            }

            user.Phone = phone?.Trim() ?? string.Empty;
            user.Email = email?.Trim() ?? string.Empty;
            _userRepository.Update(user);
            return true;
        }

        public Task<bool> UpdateContactInfoAsync(string userId, string phone, string email)
        {
            return ClientAsyncDispatcher.RunLiteDbAsync(() => UpdateContactInfo(userId, phone, email));
        }

        public bool UpdateRealName(string userId, string realName, string idCard)
        {
            var user = _userRepository.GetById(userId);
            if (user == null)
            {
                return false;
            }

            user.RealName = realName?.Trim() ?? string.Empty;
            user.IdCard = idCard?.Trim() ?? string.Empty;
            _userRepository.Update(user);
            return true;
        }

        public Task<bool> UpdateRealNameAsync(string userId, string realName, string idCard)
        {
            return ClientAsyncDispatcher.RunLiteDbAsync(() => UpdateRealName(userId, realName, idCard));
        }

        public User GetUserById(string userId)
        {
            return _userRepository.GetById(userId);
        }

        public Task<User> GetUserByIdAsync(string userId)
        {
            return ClientAsyncDispatcher.RunLiteDbAsync(() => GetUserById(userId));
        }

        public User GetUserByUsername(string username)
        {
            return _userRepository.GetByUsername(username);
        }

        public Task<User> GetUserByUsernameAsync(string username)
        {
            return ClientAsyncDispatcher.RunLiteDbAsync(() => GetUserByUsername(username));
        }

        public User GetUserByEmail(string email)
        {
            return _userRepository.GetByEmail(email);
        }

        public Task<User> GetUserByEmailAsync(string email)
        {
            return ClientAsyncDispatcher.RunLiteDbAsync(() => GetUserByEmail(email));
        }

        private async Task<ApiResultEnvelope<T>> PostJsonAsync<T>(string relativePath, object payload, bool requiresAuthentication = false)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, BuildApiUri(relativePath));
            request.Content = new StringContent(
                payload == null ? "{}" : JsonConvert.SerializeObject(payload),
                Encoding.UTF8,
                "application/json");

            if (requiresAuthentication)
            {
                var token = GetAccessToken();
                if (string.IsNullOrWhiteSpace(token))
                {
                    return new ApiResultEnvelope<T> { IsSuccess = false, ErrorMessage = "当前会话未认证" };
                }

                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }

            HttpResponseMessage response;
            try
            {
                System.Diagnostics.Debug.WriteLine($"[AccountService] 发送请求: {request.RequestUri}");
                response = await HttpClient.SendAsync(request).ConfigureAwait(false);
            }
            catch (HttpRequestException ex) when (ex.InnerException != null)
            {
                var innerEx = ex.InnerException;
                var detailedMessage = $"网络连接失败: {ex.Message}";
                
                if (innerEx is System.Security.Authentication.AuthenticationException authEx)
                {
                    detailedMessage = $"SSL/TLS认证失败: {authEx.Message}";
                    System.Diagnostics.Debug.WriteLine($"[AccountService] SSL认证失败详情:");
                    System.Diagnostics.Debug.WriteLine($"  - 请求URL: {request.RequestUri}");
                    System.Diagnostics.Debug.WriteLine($"  - 认证错误: {authEx.Message}");
                    System.Diagnostics.Debug.WriteLine($"  - 内部异常: {authEx.InnerException?.Message}");
                    System.Diagnostics.Debug.WriteLine($"  - 跳过SSL验证模式: {SslConfiguration.ShouldBypassSslValidation}");
                    
                    detailedMessage += "\n\n诊断信息:";
                    detailedMessage += "\n- 可能是测试环境使用了自签名证书";
                    detailedMessage += "\n- 请确认服务器证书配置正确";
                    detailedMessage += "\n- 当前已启用开发模式SSL跳过验证";
                }
                else if (innerEx is System.Net.Sockets.SocketException socketEx)
                {
                    detailedMessage = $"Socket连接失败: {socketEx.Message} (错误码: {socketEx.ErrorCode})";
                    System.Diagnostics.Debug.WriteLine($"[AccountService] Socket错误详情:");
                    System.Diagnostics.Debug.WriteLine($"  - 请求URL: {request.RequestUri}");
                    System.Diagnostics.Debug.WriteLine($"  - Socket错误码: {socketEx.ErrorCode}");
                    System.Diagnostics.Debug.WriteLine($"  - 错误消息: {socketEx.Message}");
                }
                
                System.Diagnostics.Debug.WriteLine($"[AccountService] 请求异常: {detailedMessage}");
                return new ApiResultEnvelope<T> { IsSuccess = false, ErrorMessage = detailedMessage };
            }
            catch (HttpRequestException ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AccountService] HTTP请求异常: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"  - 请求URL: {request.RequestUri}");
                return new ApiResultEnvelope<T> { IsSuccess = false, ErrorMessage = $"HTTP请求失败: {ex.Message}" };
            }
            catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
            {
                System.Diagnostics.Debug.WriteLine($"[AccountService] 请求超时: {request.RequestUri}");
                return new ApiResultEnvelope<T> { IsSuccess = false, ErrorMessage = "请求超时，请检查网络连接后重试" };
            }
            catch (TaskCanceledException)
            {
                System.Diagnostics.Debug.WriteLine($"[AccountService] 请求被取消: {request.RequestUri}");
                throw;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AccountService] 未知异常: {ex.GetType().Name} - {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"  - 请求URL: {request.RequestUri}");
                System.Diagnostics.Debug.WriteLine($"  - 堆栈跟踪: {ex.StackTrace}");
                return new ApiResultEnvelope<T> { IsSuccess = false, ErrorMessage = $"请求失败: {ex.Message}" };
            }

            using (response)
            {
                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    if (response.StatusCode == HttpStatusCode.Unauthorized && requiresAuthentication)
                    {
                        var refreshed = await RefreshTokenAsync().ConfigureAwait(false);
                        if (refreshed)
                        {
                            using var retryRequest = new HttpRequestMessage(HttpMethod.Post, BuildApiUri(relativePath));
                            retryRequest.Content = new StringContent(
                                payload == null ? "{}" : JsonConvert.SerializeObject(payload),
                                Encoding.UTF8,
                                "application/json");
                            var newToken = GetAccessToken();
                            if (!string.IsNullOrWhiteSpace(newToken))
                            {
                                retryRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", newToken);
                            }

                            HttpResponseMessage retryResponse;
                            try
                            {
                                retryResponse = await HttpClient.SendAsync(retryRequest).ConfigureAwait(false);
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"[AccountService] 刷新Token后重试请求异常: {ex.Message}");
                                return new ApiResultEnvelope<T>
                                {
                                    IsSuccess = false,
                                    ErrorMessage = TryExtractErrorMessage(body) ?? response.ReasonPhrase
                                };
                            }

                            using (retryResponse)
                            {
                                var retryBody = await retryResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
                                if (retryResponse.IsSuccessStatusCode)
                                {
                                    if (string.IsNullOrWhiteSpace(retryBody))
                                    {
                                        return new ApiResultEnvelope<T>
                                        {
                                            Code = (int)retryResponse.StatusCode,
                                            IsSuccess = true
                                        };
                                    }
                                    return DeserializeApiResult<T>(retryBody, (int)retryResponse.StatusCode);
                                }

                                return new ApiResultEnvelope<T>
                                {
                                    IsSuccess = false,
                                    ErrorMessage = TryExtractErrorMessage(body) ?? response.ReasonPhrase
                                };
                            }
                        }
                    }

                    if (response.StatusCode == HttpStatusCode.BadRequest
                        || response.StatusCode == HttpStatusCode.Unauthorized
                        || response.StatusCode == HttpStatusCode.Forbidden)
                    {
                        return new ApiResultEnvelope<T>
                        {
                            IsSuccess = false,
                            ErrorMessage = TryExtractErrorMessage(body) ?? response.ReasonPhrase
                        };
                    }

                    throw new HttpRequestException($"请求 {relativePath} 失败: {(int)response.StatusCode} {response.ReasonPhrase}");
                }

                if (string.IsNullOrWhiteSpace(body))
                {
                    return new ApiResultEnvelope<T>
                    {
                        Code = (int)response.StatusCode,
                        IsSuccess = true
                    };
                }

                return DeserializeApiResult<T>(body, (int)response.StatusCode);
            }
        }

        private async Task<RemoteUserEnvelope> TryGetCurrentUserAsync()
        {
            try
            {
                var result = await PostJsonAsync<RemoteUserEnvelope>("Account/User", null, requiresAuthentication: true).ConfigureAwait(false);
                return result?.IsSuccess == true ? result.Data : null;
            }
            catch(Exception ex)
            {
                // Formal login has already succeeded if we reach here. Treat profile fetch as
                // best-effort so temporary /User contract issues do not block sign-in.
                return null;
            }
        }

        private User UpsertAuthenticatedUser(RemoteUserEnvelope profile, string fallbackPassportId, string password, string fallbackEmail, string preferredUsername)
        {
            var passportId = !string.IsNullOrWhiteSpace(profile?.PassportId)
                ? profile.PassportId
                : fallbackPassportId;

            var email = !string.IsNullOrWhiteSpace(profile?.Email)
                ? profile.Email
                : fallbackEmail ?? string.Empty;

            var username = FirstNonEmpty(profile?.NickName, profile?.Name, preferredUsername, passportId);

            var user = _userRepository.GetByPassportId(passportId);
            if (user == null && !string.IsNullOrWhiteSpace(email))
            {
                user = _userRepository.GetByEmail(email);
            }

            if (user == null && !string.IsNullOrWhiteSpace(username))
            {
                user = _userRepository.GetByUsername(username);
            }

            if (user == null)
            {
                user = new User
                {
                    Id = Guid.NewGuid().ToString(),
                    FriendsJson = "[]",
                    GroupsJson = "[]",
                    RecentGamesJson = "[]"
                };
            }

            user.PassportId = passportId;
            user.UserId = (profile != null && profile.UserId != Guid.Empty) ? profile.UserId : Guid.NewGuid();

            // Preserve a custom username the user has already set locally.
            // Only accept the server-supplied NickName when the local record has no custom username
            // (i.e. empty or still equals the passportId default), so a client-side rename is not
            // silently overwritten on every login.
            var localHasCustomUsername = !string.IsNullOrWhiteSpace(user.Username)
                && !string.Equals(user.Username.Trim(), passportId, StringComparison.OrdinalIgnoreCase);
            if (!localHasCustomUsername)
            {
                user.Username = username;
            }

            user.Email = email;
            user.PasswordHash = HashPassword(password);
            user.Avatar = profile?.Avatar ?? user.Avatar ?? string.Empty;
            user.Bio = !string.IsNullOrWhiteSpace(user.Bio)
                ? user.Bio
                : profile?.Name ?? string.Empty;
            user.Status = UserStatus.Online;

            if (_userRepository.GetById(user.Id) == null)
            {
                _userRepository.Add(user);
            }
            else
            {
                _userRepository.Update(user);
            }

            return user;
        }

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }

            return string.Empty;
        }

        private void ClearLastError()
        {
            _lastErrorMessage = string.Empty;
        }

        private void SetLastError(string message)
        {
            _lastErrorMessage = message?.Trim() ?? string.Empty;
        }

        private static Uri BuildApiUri(string relativePath)
        {
            return new Uri(GetBaseUri(), relativePath);
        }

        private static Uri GetBaseUri()
        {
            var baseUrl = GatewayDiscoveryService.GetWebApiBaseUrl();
            return new Uri(baseUrl, UriKind.Absolute);
        }

        private static HttpClient CreateHttpClient()
        {
            return new HttpClient(SslConfiguration.CreateTestEnvironmentHandler())
            {
                Timeout = TimeSpan.FromSeconds(60)
            };
        }

        private static void SetSession(LoginResultEnvelope loginResult)
        {
            lock (SessionLock)
            {
                _accessToken = loginResult.AccessToken;
                _refreshToken = loginResult.RefreshToken;
                _accessTokenExpiresAtUtc = loginResult.ExpiresTime == default
                    ? DateTime.UtcNow.AddSeconds(Math.Max(loginResult.ExpiresIn, 60))
                    : loginResult.ExpiresTime.ToUniversalTime();
                _imAuthToken = loginResult.ImAuthToken ?? string.Empty;
            }
        }

        private static bool HasActiveSession()
        {
            lock (SessionLock)
            {
                return !string.IsNullOrWhiteSpace(_accessToken)
                    && _accessTokenExpiresAtUtc > DateTime.UtcNow.AddMinutes(-1);
            }
        }

        public static string GetAccessToken()
        {
            lock (SessionLock)
            {
                return HasActiveSession() ? _accessToken : string.Empty;
            }
        }

        public static string GetImAuthToken()
        {
            lock (SessionLock)
            {
                return _imAuthToken ?? string.Empty;
            }
        }

        public static async Task<bool> RefreshTokenAsync()
        {
            var refreshToken = GetRefreshToken();
            if (string.IsNullOrWhiteSpace(refreshToken))
            {
                System.Diagnostics.Debug.WriteLine("[AccountService] RefreshTokenAsync失败: 没有可用的RefreshToken");
                return false;
            }

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, BuildApiUri("Account/GetRefreshToken"));
                request.Content = new StringContent(refreshToken, Encoding.UTF8, "application/json");

                using var response = await HttpClient.SendAsync(request).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    System.Diagnostics.Debug.WriteLine($"[AccountService] RefreshTokenAsync失败: HTTP {(int)response.StatusCode}");
                    return false;
                }

                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = DeserializeApiResult<LoginResultEnvelope>(body, (int)response.StatusCode);
                if (result?.IsSuccess != true || result.Data == null || string.IsNullOrWhiteSpace(result.Data.AccessToken))
                {
                    System.Diagnostics.Debug.WriteLine($"[AccountService] RefreshTokenAsync失败: {result?.ErrorMessage ?? "响应无效"}");
                    return false;
                }

                SetSession(result.Data);
                System.Diagnostics.Debug.WriteLine("[AccountService] RefreshTokenAsync成功: Token已刷新");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AccountService] RefreshTokenAsync异常: {ex.Message}");
                return false;
            }
        }

        private static string GetRefreshToken()
        {
            lock (SessionLock)
            {
                return _refreshToken ?? string.Empty;
            }
        }

        /// <summary>
        /// 返回已登录用户的通行证 ID。
        /// </summary>
        public static string GetPassportId()
        {
            lock (SessionLock)
            {
                return _passportId ?? string.Empty;
            }
        }

        /// <summary>
        /// 返回用于网关鉴权的通行证验证令牌（即 ImAuthToken）。
        /// </summary>
        public static string GetGameAuthToken()
        {
            return GetImAuthToken();
        }

        /// <summary>
        /// 获取指定游戏下当前用户的游戏内 UserId。
        /// 若服务端尚无记录则自动注册并返回新分配的 UserId。
        /// 发生错误时返回 0。
        /// </summary>
        public static async Task<long> GetOrRegisterGameUserIdAsync(int gameId, int areaId, int serverId)
        {
            var passportId = GetPassportId();
            if (string.IsNullOrWhiteSpace(passportId))
            {
                return 0;
            }

            // 1. 尝试查询已存在的游戏用户
            var queryResult = await QueryGameUserAsync(passportId, gameId).ConfigureAwait(false);
            if (queryResult > 0)
            {
                return queryResult;
            }

            // 2. 未找到则注册新的游戏用户
            return await RegisterGameUserAsync(gameId, areaId, serverId).ConfigureAwait(false);
        }

        private static async Task<long> QueryGameUserAsync(string passportId, int gameId)
        {
            var token = GetAccessToken();
            if (string.IsNullOrWhiteSpace(token))
            {
                return 0;
            }

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, BuildApiUri("GameUserRole/gameuser"));
                request.Content = new StringContent(
                    JsonConvert.SerializeObject(new { PassportId = passportId, GameId = gameId }),
                    Encoding.UTF8,
                    "application/json");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

                using var response = await HttpClient.SendAsync(request).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    return 0;
                }

                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonConvert.DeserializeObject<ApiResultEnvelope<GameUserInfoEnvelope>>(body);
                if (result?.IsSuccess == true && result.Data != null)
                {
                    return result.Data.GameUserId;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AccountService] QueryGameUserAsync失败: {ex.Message}");
            }

            return 0;
        }

        private static async Task<long> RegisterGameUserAsync(int gameId, int areaId, int serverId)
        {
            var token = GetAccessToken();
            if (string.IsNullOrWhiteSpace(token))
            {
                return 0;
            }

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, BuildApiUri("GameUserRole/register"));
                request.Content = new StringContent(
                    JsonConvert.SerializeObject(new
                    {
                        GameId = gameId,
                        ServerId = serverId,
                        AreaId = areaId,
                        Ip = "127.0.0.1",
                        PlatformId = Environment.OSVersion.Platform.ToString()
                    }),
                    Encoding.UTF8,
                    "application/json");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

                using var response = await HttpClient.SendAsync(request).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    return 0;
                }

                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonConvert.DeserializeObject<ApiResultEnvelope<GameUserInfoEnvelope>>(body);
                if (result?.IsSuccess == true && result.Data != null)
                {
                    return result.Data.GameUserId;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AccountService] RegisterGameUserAsync失败: {ex.Message}");
            }

            return 0;
        }

        private static void SetCurrentPassportId(string passportId)
        {
            lock (SessionLock)
            {
                _passportId = passportId?.Trim() ?? string.Empty;
                
            }
        }

        private static string TryExtractErrorMessage(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
            {
                return null;
            }

            try
            {
                var result = JsonConvert.DeserializeObject<ApiResultEnvelope<object>>(body);
                if (!string.IsNullOrWhiteSpace(result?.ErrorMessage))
                {
                    return result.ErrorMessage;
                }
            }
            catch
            {
            }

            return body;
        }

        private static ApiResultEnvelope<T> DeserializeApiResult<T>(string body, int statusCode)
        {
            try
            {
                var envelope = JsonConvert.DeserializeObject<ApiResultEnvelope<T>>(body);
                if (IsRecognizedEnvelope(envelope))
                {
                    return NormalizeApiResult(envelope, statusCode);
                }
            }
            catch
            {
            }

            try
            {
                var data = JsonConvert.DeserializeObject<T>(body);
                if (HasUsablePayload(data))
                {
                    return new ApiResultEnvelope<T>
                    {
                        Code = statusCode,
                        IsSuccess = true,
                        Data = data
                    };
                }
            }
            catch
            {
            }

            return new ApiResultEnvelope<T>
            {
                Code = statusCode,
                IsSuccess = false,
                ErrorMessage = "服务端返回了无法识别的数据格式"
            };
        }

        private static ApiResultEnvelope<T> NormalizeApiResult<T>(ApiResultEnvelope<T> result, int statusCode)
        {
            result ??= new ApiResultEnvelope<T>();
            if (result.Code == 0)
            {
                result.Code = statusCode;
            }

            if (!result.IsSuccess
                && string.IsNullOrWhiteSpace(result.ErrorMessage)
                && HasUsablePayload(result.Data))
            {
                result.IsSuccess = true;
            }

            return result;
        }

        private static bool IsRecognizedEnvelope<T>(ApiResultEnvelope<T> result)
        {
            return result != null
                && (result.Code != 0
                    || result.IsSuccess
                    || !string.IsNullOrWhiteSpace(result.ErrorMessage)
                    || result.Data != null);
        }

        private static bool HasUsablePayload<T>(T data)
        {
            if (data == null)
            {
                return false;
            }

            if (data is bool successFlag)
            {
                return successFlag;
            }

            if (data is string text)
            {
                return !string.IsNullOrWhiteSpace(text);
            }

            return true;
        }

        private static string ResolvePassportId(User user)
        {
            return FirstNonEmpty(user?.PassportId, user?.Email, user?.Username, user?.Id);
        }

        private string HashPassword(string password)
        {
            using var sha256 = SHA256.Create();
            var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
            var builder = new StringBuilder();
            foreach (var currentByte in bytes)
            {
                builder.Append(currentByte.ToString("x2"));
            }

            return builder.ToString();
        }

        private bool VerifyPassword(string password, string hash)
        {
            return HashPassword(password) == hash;
        }

        /// <summary>
        /// 从 JWT AccessToken 中提取 UserId 声明（不验证签名，仅解码 payload）。
        /// 服务端在 ResourceOwnerPasswordValidator 中将 PUId (Guid) 写入 UserId 声明。
        /// </summary>
        private static Guid? TryExtractUserIdFromToken(string accessToken)
        {
            if (string.IsNullOrWhiteSpace(accessToken))
                return null;

            var parts = accessToken.Split('.');
            if (parts.Length < 2)
                return null;

            try
            {
                var payload = parts[1];
                // Base64Url 解码
                var padded = payload.Replace('-', '+').Replace('_', '/');
                var padCount = 4 - padded.Length % 4;
                if (padCount < 4) padded += new string('=', padCount);
                
                var bytes = Convert.FromBase64String(padded);
                var json = Encoding.UTF8.GetString(bytes);
                
                // 简单解析 JSON 提取 UserId 字段值
                // JWT payload 格式: {"passportid":"xxx","userid":"guid-string",...}
                var userIdKey = "\"userid\"";
                var idx = json.IndexOf(userIdKey, StringComparison.OrdinalIgnoreCase);
                if (idx < 0)
                    return null;

                var valueStart = json.IndexOf('"', idx + userIdKey.Length + 1);
                if (valueStart < 0)
                    return null;
                
                var valueEnd = json.IndexOf('"', valueStart + 1);
                if (valueEnd < 0)
                    return null;

                var userIdStr = json.Substring(valueStart + 1, valueEnd - valueStart - 1);
                if (Guid.TryParse(userIdStr, out var userId))
                    return userId;
            }
            catch
            {
                // 解码失败时静默返回 null
            }

            return null;
        }

        private sealed class ApiResultEnvelope<T>
        {
            public int Code { get; set; }
            public string ErrorMessage { get; set; }
            public bool IsSuccess { get; set; }
            public T Data { get; set; }
        }

        private sealed class LoginResultEnvelope
        {
            public string AccessToken { get; set; }
            public string RefreshToken { get; set; }
            public long ExpiresIn { get; set; }
            public DateTime ExpiresTime { get; set; }
            public string ImAuthToken { get; set; }
        }

        private sealed class RemoteUserEnvelope
        {
            public string PassportId { get; set; }
            public Guid UserId { get; set; }
            public string Name { get; set; }
            public string NickName { get; set; }
            public string Avatar { get; set; }
            public string Email { get; set; }
        }

        private sealed class GameUserInfoEnvelope
        {
            public string PassportId { get; set; }
            public long GameUserId { get; set; }
            public int GameId { get; set; }
        }
    }
}