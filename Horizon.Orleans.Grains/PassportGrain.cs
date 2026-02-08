using Horizon.Core;
using Horizon.Core.Abstract;
using Horizon.Core.Abstract.Enums;
using Horizon.Entities;
using Horizon.Game.Message;
using Horizon.Game.Message.Network;
using Horizon.Model;
using Horizon.Model.Basic;
using Horizon.Model.GameModel;
using Horizon.Orleans.Interface;
using Horizon.Share.Dtos.User;
using MemoryPack;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace Horizon.Orleans.Grains
{
    /// <summary>
    /// 会话信息的内部数据结构
    /// </summary>
    public class SessionInfo
    {
        public string SessionId { get; set; }
        public Guid UserId { get; set; }
        public string PassportId { get; set; }
        public long AppId { get; set; }
        public AppType AppType { get; set; }
        public DateTime CreateTime { get; set; }
        public DateTime LastActiveTime { get; set; }
        public bool IsActive { get; set; }
        public string ClientIP { get; set; }
        public string PlatformId { get; set; }
        public string DeviceId { get; set; }
    }
    public class PassportGrain : Grain, IPassportGrain
    {
        private readonly string _passportCreating = nameof(_passportCreating);
        private readonly IDataContext<BasicEntityContext, Passport, string> _dataContext;
        private readonly IDataContext<BasicEntityContext, PassportIds, string> _contextPassportIds;
        private readonly IDataContext<BasicEntityContext, User, Guid> _userdataContext;
        private readonly IDataContext<BasicEntityContext, PassportFlag, int> _creating;
        private readonly IDataContext<GameEntityContext, UserEntity, long> _gameUserContext;
        private readonly ILogger<PassportGrain> _logger;
        private readonly SessionManager _sessionManager;

        // 登录尝试限制相关
        private readonly Dictionary<string, DateTime> _loginAttempts = new();
        private const int MaxLoginAttempts = 5;
        private const int LoginAttemptsWindowMinutes = 15;



        public PassportGrain(IClusterClient client,
            IDataContext<BasicEntityContext, Passport, string> dataContext,
            IDataContext<BasicEntityContext, PassportIds, string> contextPassportIds,
            IDataContext<BasicEntityContext, User, Guid> userdataContext,
            IDataContext<BasicEntityContext, PassportFlag, int> creating,
            IDataContext<GameEntityContext, UserEntity, long> gameUserContext,
            ILogger<PassportGrain> logger
            )
        {
            _dataContext = dataContext;
            _contextPassportIds = contextPassportIds;
            _userdataContext = userdataContext;
            _creating = creating;
            _gameUserContext = gameUserContext;
            _logger = logger;
            _sessionManager = new SessionManager(logger);
        }

        public async Task<PassportInfoDto> AuthenticationAsync(LoginDto loginDto)
        {
            try
            {
                _logger.LogInformation("开始用户认证，PassportId: {PassportId}, AppType: {AppType}",
                    loginDto.PassportId, loginDto.AppType);

                // 1. 登录频率检查
                if (!await CheckLoginAttempts(loginDto.PassportId))
                {
                    _logger.LogWarning("用户 {PassportId} 登录尝试过于频繁", loginDto.PassportId);
                    return null;
                }

                // 2. 验证用户凭据
                var passport = await _dataContext.QueryFirstOrDefaultAsync(
                    m => m.Id == loginDto.PassportId && m.IsValid);

                if (passport == null)
                {
                    _logger.LogWarning("通行证不存在或已被禁用: {PassportId}", loginDto.PassportId);
                    await RecordFailedLoginAttempt(loginDto.PassportId);
                    return null;
                }

                // 3. 验证密码
                // 解码客户端Base64编码的密码
                string decodedPassword;
                try
                {
                    decodedPassword = Encoding.UTF8.GetString(Convert.FromBase64String(loginDto.Password));
                }
                catch (FormatException)
                {
                    // 如果解码失败，尝试直接使用原密码
                    _logger.LogDebug("密码Base64解码失败，使用原始密码: {PassportId}", loginDto.PassportId);
                    decodedPassword = loginDto.Password;
                }

                bool isPasswordValid = false;
                bool needsPasswordUpgrade = false;

                // 首先尝试新的安全验证（如果有盐值）
                if (!string.IsNullOrEmpty(passport.PasswordSalt))
                {
                    isPasswordValid = SecurePasswordHasher.VerifyPassword(
                        decodedPassword,
                        passport.Password,
                        passport.PasswordSalt);
                }
                else
                {
                    // 向后兼容：使用旧的密码验证方法
                    _logger.LogInformation("使用旧密码系统验证: {PassportId}", loginDto.PassportId);
                    
                    string oldEncryptedPassword = PassportHelper.SetPasportPassword(passport.Id, decodedPassword);
                    isPasswordValid = (passport.Password == oldEncryptedPassword);
                    needsPasswordUpgrade = isPasswordValid; // 标记需要升级
                }

                if (!isPasswordValid)
                {
                    _logger.LogWarning("密码验证失败: {PassportId}", loginDto.PassportId);
                    await RecordFailedLoginAttempt(loginDto.PassportId);
                    return null;
                }

                // 如果使用旧密码登录成功，立即升级到新的安全系统
                if (needsPasswordUpgrade)
                {
                    try
                    {
                        _logger.LogInformation("自动升级密码到安全哈希系统: {PassportId}", loginDto.PassportId);
                        var (newHash, newSalt) = SecurePasswordHasher.HashPassword(decodedPassword);
                        
                        passport.Password = newHash;
                        passport.PasswordSalt = newSalt;
                        passport.UpdateTime = DateTime.UtcNow;
                        
                        await _dataContext.UpdateAsync(passport, passport.Id);
                        _logger.LogInformation("密码升级成功: {PassportId}", loginDto.PassportId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "密码升级失败: {PassportId}", loginDto.PassportId);
                        // 继续登录流程，下次登录时再尝试升级
                    }
                }

                // 4. 获取或创建用户信息
                var user = await GetOrCreateUser(passport, loginDto);
                if (user == null)
                {
                    _logger.LogError("用户信息获取失败: {PassportId}", loginDto.PassportId);
                    return null;
                }

                // 5. 更新用户登录信息
                await UpdateUserLoginInfo(user);

                // 6. 为游戏类型应用创建游戏用户记录
                var gameUser = await HandleGameUserCreation(user, loginDto);

                // 7. 创建会话
                var sessionToken = await CreateUserSession(user, loginDto);

                // 8. 构建返回结果
                var result = new PassportInfoDto
                {
                    AppId = loginDto.AppId,
                    AppType = loginDto.AppType,
                    Avatar = user?.Avatar,
                    Name = user?.Name,
                    PassportType = user?.PassportType ?? PassportType.Normal,
                    OrganizationId = user?.OrganizationId ?? 0,
                    PassportId = loginDto.PassportId,
                    Phone = user?.Phone,
                    Email = user?.Email,
                    SessionToken = sessionToken,
                    UserId = (gameUser?.Id ?? 0),
                    UserName = user?.NickName
                };

                if (gameUser != null)
                {
                    result.UserId = gameUser.Id;
                }

                _logger.LogInformation("用户认证成功: {PassportId}, UserId: {UserId}",
                    loginDto.PassportId, result.UserId);

                // 清除失败的登录尝试记录
                await ClearFailedLoginAttempts(loginDto.PassportId);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "用户认证过程中发生异常: {PassportId}", loginDto.PassportId);
                return null;
            }
        }

        public async Task<bool> SignOutAsync(LoginDto loginDto)
        {
            try
            {
                var passport = await _dataContext.QueryFirstOrDefaultAsync(m => m.Id == loginDto.PassportId);
                if (passport == null)
                {
                    return false;
                }

                var user = await _userdataContext.QueryFirstOrDefaultAsync(m => m.PassportId == passport.Id &&
                                                                                m.AppId == loginDto.AppId &&
                                                                                m.AppType == loginDto.AppType &&
                                                                                m.PassportType == loginDto.PassportType &&
                                                                                m.IsValid);
                if (user != null)
                {
                    user.Status = UserStatsEnum.SignOut;
                    await _userdataContext.UpdateAsync(user, user.Id);

                    // 终止用户的所有活跃会话
                    await _sessionManager.TerminateUserSessionsAsync(loginDto.PassportId);

                    _logger.LogInformation("用户登出成功: PassportId={PassportId}", loginDto.PassportId);
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "用户登出失败: PassportId={PassportId}", loginDto.PassportId);
                return false;
            }
        }

        public async Task<bool> ChangePasswordAsync(ChangePasswordDto loginDto)
        {
            try
            {
                _logger.LogInformation("开始修改密码: {PassportId}", loginDto.PassportId);

                // 1. 获取通行证
                var passport = await _dataContext.QueryFirstOrDefaultAsync(
                    m => m.Id == loginDto.PassportId && m.IsValid, 
                    isTracking: true);

                if (passport == null)
                {
                    _logger.LogWarning("通行证不存在: {PassportId}", loginDto.PassportId);
                    return false;
                }

                // 2. 解码密码
                string decodedOldPassword;
                string decodedNewPassword;
                try
                {
                    decodedOldPassword = Encoding.UTF8.GetString(Convert.FromBase64String(loginDto.OldPassword));
                    decodedNewPassword = Encoding.UTF8.GetString(Convert.FromBase64String(loginDto.NewPassword));
                }
                catch (FormatException)
                {
                    _logger.LogDebug("密码Base64解码失败，使用原始密码: {PassportId}", loginDto.PassportId);
                    decodedOldPassword = loginDto.OldPassword;
                    decodedNewPassword = loginDto.NewPassword;
                }

                // 3. 验证旧密码
                bool isOldPasswordValid = SecurePasswordHasher.VerifyPassword(
                    decodedOldPassword,
                    passport.Password,
                    passport.PasswordSalt ?? string.Empty);

                if (!isOldPasswordValid)
                {
                    _logger.LogWarning("旧密码验证失败: {PassportId}", loginDto.PassportId);
                    return false;
                }

                // 4. 验证新密码强度
                if (!SecurePasswordHasher.IsPasswordStrong(decodedNewPassword))
                {
                    _logger.LogWarning("新密码强度不足: {PassportId}", loginDto.PassportId);
                    return false;
                }

                // 5. 生成新密码的哈希和盐值
                var (newHash, newSalt) = SecurePasswordHasher.HashPassword(decodedNewPassword);

                // 6. 更新数据库
                passport.Password = newHash;
                passport.PasswordSalt = newSalt;
                passport.UpdateTime = DateTime.UtcNow;

                await _dataContext.UpdateAsync(passport, passport.Id);

                _logger.LogInformation("密码修改成功: {PassportId}", loginDto.PassportId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "修改密码过程中发生异常: {PassportId}", loginDto.PassportId);
                return false;
            }
        }
        public async Task<PassportInfoDto> WxUserAuthenticationAsync(WxLoginDto loginDto)
        {
            try
            {
                if (loginDto == null)
                {
                    _logger.LogWarning("微信登录参数为空");
                    return null;
                }

                if (string.IsNullOrEmpty(loginDto.Code))
                {
                    _logger.LogWarning("微信登录Code为空");
                    return null;
                }

                _logger.LogInformation("开始微信用户认证, WxAppId: {WxAppId}", loginDto.WxAppId);

                // 1. 登录频率检查（使用微信Code作为标识）
                var rateLimitKey = $"wx_{loginDto.WxAppId}_{loginDto.Code}";
                if (!await CheckLoginAttempts(rateLimitKey))
                {
                    _logger.LogWarning("微信登录尝试过于频繁, WxAppId: {WxAppId}", loginDto.WxAppId);
                    return null;
                }

                // 2. 查找已绑定的通行证（通过手机号或邮箱匹配）
                Passport passport = null;
                if (!string.IsNullOrEmpty(loginDto.PassportId))
                {
                    passport = await _dataContext.QueryFirstOrDefaultAsync(
                        m => m.Id == loginDto.PassportId && m.IsValid);
                }

                if (passport == null && !string.IsNullOrEmpty(loginDto.Phone))
                {
                    // 通过手机号查找用户，再找到对应的通行证
                    var user = await _userdataContext.QueryFirstOrDefaultAsync(
                        m => m.Phone == loginDto.Phone && m.IsValid);
                    if (user != null)
                    {
                        passport = await _dataContext.QueryFirstOrDefaultAsync(
                            m => m.Id == user.PassportId && m.IsValid);
                    }
                }

                // 3. 如果没有找到通行证，自动创建新账号
                if (passport == null)
                {
                    _logger.LogInformation("微信用户无绑定账号，自动创建新通行证, WxAppId: {WxAppId}", loginDto.WxAppId);

                    // 获取可用的通行证ID
                    var passportIds = await _contextPassportIds.QueryFirstOrDefaultAsync(m => m.IsValid, isTracking: true);
                    if (passportIds == null)
                    {
                        await CreatePassportIdAsync(10);
                        passportIds = await _contextPassportIds.QueryFirstOrDefaultAsync(m => m.IsValid, isTracking: true);
                    }

                    if (passportIds == null)
                    {
                        _logger.LogError("无法生成通行证ID");
                        return null;
                    }

                    // 生成随机密码并创建通行证（使用完整GUID确保足够熵值）
                    var randomPassword = Guid.NewGuid().ToString("N");
                    var (passwordHash, passwordSalt) = SecurePasswordHasher.HashPassword(randomPassword);

                    passport = await _dataContext.AddAsync(new Passport
                    {
                        Id = passportIds.Id,
                        Password = passwordHash,
                        PasswordSalt = passwordSalt,
                        CreateTime = DateTime.UtcNow,
                        UpdateTime = DateTime.UtcNow,
                        IsValid = true
                    });

                    passportIds.ApplyTime = DateTime.UtcNow;
                    passportIds.IsValid = false;
                    await _contextPassportIds.DbCurrent.SaveChangesAsync();

                    _logger.LogInformation("微信用户自动创建通行证成功: PassportId={PassportId}", passport.Id);
                }

                // 4. 获取或创建用户信息
                var wxLoginDto = new LoginDto
                {
                    PassportId = passport.Id,
                    AppId = loginDto.AppId,
                    AppType = loginDto.AppType,
                    Phone = loginDto.Phone,
                    Email = loginDto.Email,
                    GameContext = loginDto.GameContext
                };

                var user2 = await GetOrCreateUser(passport, wxLoginDto);
                if (user2 == null)
                {
                    // 为微信用户创建新用户记录
                    user2 = await _userdataContext.AddAsync(new User
                    {
                        AppId = loginDto.AppId,
                        AppType = loginDto.AppType,
                        PassportId = passport.Id,
                        Phone = loginDto.Phone ?? "",
                        Email = loginDto.Email ?? "",
                        Name = loginDto.PassportId ?? "",
                        PassportType = PassportType.Normal,
                        NickName = loginDto.PassportId ?? $"WxUser_{(passport.Id.Length >= 8 ? passport.Id[..8] : passport.Id)}",
                        Status = UserStatsEnum.Normal
                    });

                    _logger.LogInformation("为微信用户创建用户记录: UserId={UserId}", user2.Id);
                }

                // 5. 更新用户登录信息
                await UpdateUserLoginInfo(user2);

                // 6. 为游戏类型应用创建游戏用户记录
                var gameUser = await HandleGameUserCreation(user2, wxLoginDto);

                // 7. 创建会话
                var sessionToken = await CreateUserSession(user2, wxLoginDto);

                // 8. 构建返回结果
                var result = new PassportInfoDto
                {
                    AppId = loginDto.AppId,
                    AppType = loginDto.AppType,
                    Avatar = user2?.Avatar,
                    Name = user2?.Name,
                    PassportType = user2?.PassportType ?? PassportType.Normal,
                    OrganizationId = user2?.OrganizationId ?? 0,
                    PassportId = passport.Id,
                    Phone = user2?.Phone,
                    Email = user2?.Email,
                    SessionToken = sessionToken,
                    UserId = (gameUser?.Id ?? 0),
                    UserName = user2?.NickName
                };

                if (gameUser != null)
                {
                    result.UserId = gameUser.Id;
                }

                _logger.LogInformation("微信用户认证成功: PassportId={PassportId}, UserId={UserId}",
                    passport.Id, result.UserId);

                // 清除失败的登录尝试记录
                await ClearFailedLoginAttempts(rateLimitKey);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "微信用户认证过程中发生异常, WxAppId: {WxAppId}", loginDto?.WxAppId);
                return null;
            }
        }

        /// <summary>
        /// 注册
        /// </summary>
        /// <param name="registerDto"></param>
        /// <returns></returns>
        public async Task<PassportInfoDto> RegisterAsync(RegisterDto registerDto)
        {
            if (registerDto == null) throw new ArgumentNullException(nameof(registerDto));
            registerDto.Phone = string.IsNullOrWhiteSpace(registerDto.Phone) ? registerDto.ID : registerDto.Phone;
            registerDto.Email = string.IsNullOrWhiteSpace(registerDto.Email) ? registerDto.ID : registerDto.Email;
            string passportId = string.Empty;
            string passwordHash = string.Empty;
            string passwordSalt = string.Empty;
            bool needCreateUser = true;

            using (var plock = await Cache.AcquireLockAsync(CacheConst.PASSPORTREGISTERLOCK, TimeSpan.FromSeconds(3)))
            {
                var id = await _contextPassportIds.QueryFirstOrDefaultAsync(m => m.IsValid, isTracking: true);
                if (id == null)
                {
                    await CreatePassportIdAsync(10);
                }

                var users = await _userdataContext.QueryAsync(m => m.IsValid && (m.Phone == registerDto.Phone || m.Email == registerDto.Email),
                                                                   m => new { m.PassportId, m.AppId, m.AppType, m.PassportType });
                if (users.Count > 0)
                {
                    passportId = users.First().PassportId;
                    var user = users.FirstOrDefault(m => m.AppId == registerDto.AppId && m.AppType == registerDto.AppType && m.PassportType == registerDto.PassportType);
                    if (user != null)
                    {
                        needCreateUser = false;
                    }
                }
                else
                {
                    passportId = id.Id;
                    
                    // 解码Base64编码的密码
                    string decodedPassword = Encoding.UTF8.GetString(Convert.FromBase64String(registerDto.Password));
                    
                    // 验证密码强度
                    if (!SecurePasswordHasher.IsPasswordStrong(decodedPassword))
                    {
                        _logger.LogWarning("注册密码强度不足: {PassportId}", passportId);
                        return null;
                    }
                    
                    // 生成安全的密码哈希和盐值
                    (passwordHash, passwordSalt) = SecurePasswordHasher.HashPassword(decodedPassword);
                    
                    var passport = await _dataContext.AddAsync(new Passport
                    {
                        Id = passportId,
                        Password = passwordHash,
                        PasswordSalt = passwordSalt,
                        CreateTime = DateTime.UtcNow,
                        UpdateTime = DateTime.UtcNow,
                        IsValid = true
                    });
                    id.ApplyTime = DateTime.UtcNow;
                    id.IsValid = false;
                    await _contextPassportIds.DbCurrent.SaveChangesAsync();
                }

                if (needCreateUser)
                {
                    var userNew = await _userdataContext.AddAsync(new User
                    {
                        AppId = registerDto.AppId,
                        AppType = registerDto.AppType,
                        PassportId = passportId,
                        Phone = registerDto.Phone,
                        Email = registerDto.Email,
                        IdCard = registerDto.ID,
                        Name = registerDto.RealName,
                        PassportType = PassportType.Normal,
                        NickName = registerDto.NickName,
                        Status = UserStatsEnum.Normal
                    });

                    try
                    {
                        if (registerDto.GameContext != null)
                        {
                            var gameUserEntity = new UserEntity
                            {
                                GameUserId = userNew.Id,
                                GameId = (int)registerDto.AppId,
                                ServerId = 0,
                                AreaId = 0,
                                AccountName = registerDto.NickName,
                                Status = 0,
                                CreateTime = DateTime.Now,
                                Email = registerDto.Email,
                                PasswordHash = passwordHash,
                                PasswordSalt = passwordSalt,
                                LastLoginTime = DateTime.Now,
                                Phone = registerDto.Phone,
                                LastLoginIp = registerDto.GameContext.Ip,
                                PlatformId = registerDto.GameContext.PlatformId,
                                DeviceId = registerDto.GameContext.PlatformId
                            };

                            await _gameUserContext.AddAsync(gameUserEntity);
                            _logger.LogInformation("游戏用户创建成功: {GameUserId}", userNew.Id);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "创建游戏用户失败: {UserId}", userNew.Id);
                        // 不影响注册流程，继续执行
                    }
                }

                return new PassportInfoDto
                {
                    AppId = registerDto.AppId,
                    AppType = registerDto.AppType,
                    PassportType = registerDto.PassportType,
                    PassportId = passportId,
                    Phone = registerDto.Phone,
                    Email = registerDto.Email,
                };
            }
        }

        private string Base64Decode(string base64Text)
        {
            if (string.IsNullOrEmpty(base64Text)) return string.Empty;
            try
            {
                var bytes = System.Convert.FromBase64String(base64Text);
                return System.Text.Encoding.UTF8.GetString(bytes);
            }
            catch (FormatException)
            {
                _logger.LogDebug("Base64Decode: 输入不是有效的 Base64 字符串");
                return string.Empty;
            }
        }
        public async Task CreatePassportIdAsync(int count)
        {
            if (count <= 0)
            {
                _logger.LogWarning("批量生成通行证ID数量无效: Count={Count}", count);
                return;
            }

            _logger.LogInformation("开始批量生成通行证ID: Count={Count}", count);

            var passportIds = new List<PassportIds>(count);
            for (int i = 0; i < count; i++)
            {
                passportIds.Add(new PassportIds
                {
                    Id = Guid.NewGuid().ToString("N"),
                    CreatingTime = DateTime.UtcNow,
                    IsValid = true
                });
            }

            await _contextPassportIds.AddRangeAsync(passportIds);
            _logger.LogInformation("批量生成通行证ID完成: Count={Count}", count);
        }

        public async Task CancelCreatePassportIdAsync()
        {
            var flag = await Cache.GetAsync<PassportFlag>(CacheConst.PASSPORTFLAG);
            if (flag == null)
            {
                await Cache.InsertAsync(CacheConst.PASSPORTFLAG, new PassportFlag { Id = 1, IsCreating = false, IsValid = true });
                return;
            }
            if (flag.IsCreating) flag.IsCreating = false;
            await Cache.InsertAsync(CacheConst.PASSPORTFLAG, flag);
        }

        /// <summary>
        /// 注销账号
        /// </summary>
        /// <param name="passportId"></param>
        /// <returns></returns>
        public async Task<bool> CancelPassportAsync(string passportId)
        {
            try
            {
                var passport = await _dataContext.QueryFirstOrDefaultAsync(m => m.IsValid && m.Id == passportId, isTracking: true);
                if (passport == null)
                {
                    _logger.LogWarning("注销失败，通行证不存在: PassportId={PassportId}", passportId);
                    return false;
                }
                passport.IsValid = false;
                var users = await _userdataContext.QueryAsync(m => m.IsValid && m.PassportId == passportId, isTracking: true);
                foreach (var item in users)
                {
                    item.IsValid = false;
                }
                await _dataContext.DbCurrent.SaveChangesAsync();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "注销账号失败: PassportId={PassportId}", passportId);
                return false;
            }
            finally
            {
                _dataContext.DbConnection.Dispose();
            }

        }

        /// <summary>
        /// 更新用户会话信息
        /// </summary>
        /// <param name="sessionInfo">会话信息</param>
        /// <returns>操作成功返回 true，失败返回 false。</returns>
        public async Task<bool> UpdateSessionInfoAsync(SessionInfoMessage sessionInfo)
        {
            try
            {
                if (sessionInfo == null || string.IsNullOrEmpty(sessionInfo.SessionId))
                {
                    return false;
                }

                // 获取现有会话
                var existingSession = await _sessionManager.GetSessionAsync(sessionInfo.SessionId);
                if (existingSession == null)
                {
                    _logger.LogWarning("会话不存在: SessionId={SessionId}", sessionInfo.SessionId);
                    return false;
                }

                // 更新会话信息
                existingSession.LastActiveTime = DateTime.UtcNow;
                
                return await _sessionManager.UpdateSessionAsync(existingSession);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新用户会话信息失败: SessionId={SessionId}", sessionInfo?.SessionId);
                return false;
            }
        }

        /// <summary>
        /// 获取所有角色信息
        /// </summary>
        /// <param name="gameQueryDto">游戏查询数据传输对象</param>
        /// <returns>角色信息列表</returns>
        public async Task<List<CharacterInfo>> GetAllCharactersAsync(Share.Dtos.Games.GameQueryDto gameQueryDto)
        {
            try
            {
                _logger.LogInformation("获取用户角色列表: UserId={UserId}, GameId={GameId}",
                    gameQueryDto.GameUserId, gameQueryDto.GameId);

                // 通过CharacterGrain获取角色列表
                var characterGrain = GrainFactory.GetGrain<ICharacterGrain>(0); // 使用通用的角色查询Grain
                var characters = await characterGrain.GetAllCharactersAsync(gameQueryDto);

                _logger.LogInformation("成功获取到 {Count} 个角色", characters.Count);
                return characters;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取角色列表时发生异常: UserId={UserId}", gameQueryDto.GameUserId);
                return new List<CharacterInfo>();
            }
        }

        #region 私有辅助方法

        /// <summary>
        /// 检查登录尝试频率
        /// </summary>
        private async Task<bool> CheckLoginAttempts(string passportId)
        {
            var key = $"login_attempts_{passportId}";
            var attempts = await Cache.GetAsync<int?>(key);

            if (attempts.HasValue && attempts.Value >= MaxLoginAttempts)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// 记录失败的登录尝试
        /// </summary>
        private async Task RecordFailedLoginAttempt(string passportId)
        {
            var key = $"login_attempts_{passportId}";
            var attempts = await Cache.GetAsync<int?>(key) ?? 0;
            attempts++;

            await Cache.InsertAsync(key, attempts, LoginAttemptsWindowMinutes);
        }

        /// <summary>
        /// 清除失败的登录尝试记录
        /// </summary>
        private async Task ClearFailedLoginAttempts(string passportId)
        {
            var key = $"login_attempts_{passportId}";
            await Cache.RemoveAsync(key);
        }

        /// <summary>
        /// 哈希密码
        /// </summary>
        private string HashPassword(string password, string salt)
        {
            if (string.IsNullOrEmpty(password))
                return string.Empty;

            // 解码Base64密码
            var decodedPassword = Base64Decode(password);
            if (string.IsNullOrEmpty(decodedPassword))
                return string.Empty;

            // 使用PassportHelper的现有方法
            return PassportHelper.SetPasportPassword(salt, decodedPassword);
        }

        /// <summary>
        /// 获取或创建用户信息
        /// </summary>
        private async Task<User> GetOrCreateUser(Passport passport, LoginDto loginDto)
        {
            var user = await _userdataContext.QueryFirstOrDefaultAsync(m =>
                m.PassportId == passport.Id &&
                m.AppId == loginDto.AppId &&
                m.AppType == loginDto.AppType &&
                m.IsValid);

            if (user != null)
            {
                return user;
            }

            // 查找同一通行证下的其他用户作为模板
            var templateUser = await _userdataContext.QueryFirstOrDefaultAsync(
                m => m.PassportId == passport.Id && m.IsValid);

            if (templateUser != null)
            {
                // 克隆用户信息到新的应用类型
                var cloneUser = templateUser.ObjectTo<User, User>();
                cloneUser.Id = Guid.NewGuid();
                cloneUser.AppId = loginDto.AppId;
                cloneUser.AppType = loginDto.AppType;
                cloneUser.PassportType = PassportType.Normal;
                cloneUser.CreateDate = DateTime.UtcNow;

                return await _userdataContext.AddAsync(cloneUser);
            }

            return null;
        }

        /// <summary>
        /// 更新用户登录信息
        /// </summary>
        private async Task UpdateUserLoginInfo(User user)
        {
            user.LoginNumber++;
            user.LastLoginDate = DateTime.UtcNow;
            user.Status = UserStatsEnum.Signin;
            await _userdataContext.UpdateAsync(user, user.Id);
        }

        /// <summary>
        /// 处理游戏用户创建
        /// </summary>
        private async Task<UserEntity> HandleGameUserCreation(User user, LoginDto loginDto)
        {
            if (loginDto.AppType != AppType.Game || user.PassportId != loginDto.PassportId)
                return null;

            var gameUser = await _gameUserContext.QueryFirstOrDefaultAsync(
                m => m.GameUserId == user.Id &&
                     m.GameId == (int)loginDto.AppId &&
                     m.IsValid && !m.IsDeleted);

            if (gameUser == null)
            {
                gameUser = await _gameUserContext.AddAsync(new UserEntity
                {
                    GameUserId = user.Id,
                    GameId = (int)loginDto.AppId,
                    ServerId = 0,
                    AreaId = 0,
                    AccountName = user.NickName ?? user.PassportId,
                    Status = 0,
                    CreateTime = DateTime.UtcNow,
                    Email = user.Email,
                    PasswordHash = HashPassword(loginDto.Password, user.PassportId),
                    PasswordSalt = user.PassportId,
                    LastLoginTime = DateTime.UtcNow,
                    Phone = user.Phone,
                    LastLoginIp = "",
                    PlatformId = "",
                    DeviceId = ""
                });

                _logger.LogInformation("为用户创建游戏账户: PassportId={PassportId}, GameUserId={GameUserId}",
                    loginDto.PassportId, gameUser.Id);
            }
            else
            {
                // 更新最后登录时间
                gameUser.LastLoginTime = DateTime.UtcNow;
                await _gameUserContext.UpdateAsync(gameUser, gameUser.Id);
            }

            return gameUser;
        }

        /// <summary>
        /// 创建用户会话
        /// </summary>
        private async Task<string> CreateUserSession(User user, LoginDto loginDto)
        {
            var sessionInfo = new SessionInfo
            {
                UserId = user.Id,
                PassportId = user.PassportId,
                AppId = loginDto.AppId,
                AppType = loginDto.AppType,
                ClientIP = loginDto.GameContext?.Ip,
                PlatformId = loginDto.GameContext?.PlatformId,
                DeviceId = loginDto.GameContext?.DeviceId
            };

            // 使用SessionManager创建持久化会话
            string sessionId = await _sessionManager.CreateSessionAsync(sessionInfo);

            _logger.LogInformation("创建用户会话: SessionId={SessionId}, UserId={UserId}",
                sessionId, user.Id);

            return sessionId;
        }

        #endregion
    }
}
