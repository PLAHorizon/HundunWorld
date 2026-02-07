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

        // 会话管理相关
        private readonly Dictionary<string, SessionInfo> _activeSessions = new();
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
                //string hashedPassword = HashPassword(loginDto.Password, passport.Id);
                if (passport.Password != loginDto.Password)
                {
                    _logger.LogWarning("密码验证失败: {PassportId}", loginDto.PassportId);
                    await RecordFailedLoginAttempt(loginDto.PassportId);
                    return null;
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
            var passport = await _dataContext.QueryFirstOrDefaultAsync(m => m.Id == loginDto.PassportId);
            if (passport == null) return await Task.FromResult(false);
            var user = await _userdataContext.QueryFirstOrDefaultAsync(m => m.PassportId == passport.Id &&
                                                                            m.AppId == loginDto.AppId &&
                                                                            m.AppType == loginDto.AppType &&
                                                                            m.PassportType == loginDto.PassportType &&
                                                                            m.IsValid);
            if (user != null)
            {
                user.Status = UserStatsEnum.SignOut;
                await _userdataContext.UpdateAsync(user, user.Id);
                return await Task.FromResult(true);
            }
            return await Task.FromResult(false);
        }

        public async Task<bool> ChangePasswordAsync(ChangePasswordDto loginDto)
        {
            var passport = await _dataContext.QueryFirstOrDefaultAsync(m => m.Id == loginDto.PassportId && m.Password == loginDto.OldPassword, true);
            if (passport == null) return await Task.FromResult(false);
            else
            {
                passport.Password = loginDto.NewPassword;
                await _dataContext.UpdateAsync(passport, passport.Id);
                return await Task.FromResult(true);
            }
        }
        public async Task<PassportInfoDto> WxUserAuthenticationAsync(WxLoginDto loginDto)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// 注册
        /// </summary>
        /// <param name="registerDto"></param>
        /// <returns></returns>
        public async Task<PassportInfoDto> RegisterAsync(RegisterDto registerDto)
        {
            if (registerDto == null) throw new ArgumentNullException(nameof(registerDto));
            //if (string.IsNullOrWhiteSpace(registerDto.Email) &&
            //    string.IsNullOrWhiteSpace(registerDto.Phone))
            //    throw new ArgumentNullException($"{nameof(registerDto.Phone)}或{nameof(registerDto.Email)}");

            registerDto.Phone = string.IsNullOrWhiteSpace(registerDto.Phone) ? registerDto.ID : registerDto.Phone;
            registerDto.Email = string.IsNullOrWhiteSpace(registerDto.Email) ? registerDto.ID : registerDto.Email;
            string passportId = string.Empty;


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
                    if (user == null)
                        goto NEWUSER;
                    goto RESULT;
                }

                passportId = id.Id;
                string password = Base64Decode(registerDto.Password);
                var passport = await _dataContext.AddAsync(new Passport
                {
                    Id = passportId,
                    Password = PassportHelper.SetPasportPassword(passportId, password),
                });
                id.ApplyTime = DateTime.UtcNow;
                id.IsValid = false;
                await _contextPassportIds.DbCurrent.SaveChangesAsync();

            NEWUSER:
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
                        await _gameUserContext.AddAsync(new UserEntity
                        {
                            GameUserId = userNew.Id,
                            GameId = (int)registerDto.AppId,
                            ServerId = 0,
                            AreaId = 0,
                            AccountName = registerDto.NickName,
                            Status = 0,
                            CreateTime = DateTime.Now,
                            Email = registerDto.Email,
                            PasswordHash = registerDto.Password,
                            PasswordSalt = registerDto.Password,
                            LastLoginTime = DateTime.Now,
                            Phone = registerDto.Phone,
                            LastLoginIp = registerDto.GameContext.Ip,
                            PlatformId = registerDto.GameContext.PlatformId,
                            DeviceId = registerDto.GameContext.PlatformId

                        });
                }
                catch (Exception e)
                {

                }



            RESULT:
                return await Task.FromResult(new PassportInfoDto
                {
                    AppId = registerDto.AppId,
                    AppType = registerDto.AppType,
                    PassportType = registerDto.PassportType,
                    PassportId = passportId,
                    Phone = registerDto.Phone,
                    Email = registerDto.Email,
                });
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
                Console.WriteLine("Base64Decode: 输入不是有效的 Base64 字符串");
                return string.Empty;
            }
        }
        public async Task CreatePassportIdAsync(int count)
        {
            //await Policy.TimeoutAsync(10, TimeoutStrategy.Optimistic).ExecuteAsync(async () =>
            //{
            //    using (var plock = await Cache.AcquireLockAsync(CacheConst.PASSPORTCREATINGLOCK, TimeSpan.FromSeconds(10)))
            //    {
            //        var flag = await Cache.GetAsync<PassportFlag>(CacheConst.PASSPORTFLAG);
            //        if (flag == null)
            //        {
            //            await Cache.InsertAsync(CacheConst.PASSPORTFLAG, new PassportFlag { Id = 1, IsCreating = false, IsValid = true });
            //        }
            //        else if (flag.IsCreating)
            //        {
            //            return;
            //        }
            //        else
            //        {
            //            flag.IsCreating = true;
            //            await Cache.InsertAsync(CacheConst.PASSPORTFLAG, flag);
            //            if (count > 100000) count = 100000;//每次最多允许生成 100000 个新的通行证号
            //            int total = 0;
            //            while (count > 0)
            //            {
            //                flag = await Cache.GetAsync<PassportFlag>(CacheConst.PASSPORTFLAG);
            //                if (!flag?.IsCreating ?? true) break;
            //                string repeat = string.Empty;
            //            ID: string id = PassportHelper.GetPassportID(repeat, CacheConst.PassportLengthMin, CacheConst.PassportLengthMax);
            //                var passport = await _dataContext.QueryFirstOrDefaultAsync(m => m.Id == id);
            //                var item = await _contextPassportIds.QueryFirstOrDefaultAsync(m => m.Id == id && m.IsValid);
            //                repeat = id;
            //                if (passport != null || item != null)
            //                {
            //                    repeat = id;
            //                    goto ID;
            //                }
            //                await _contextPassportIds.AddAsync(new PassportIds { IsValid = true, Id = repeat, CreatingTime = DateTime.UtcNow });
            //                count--;
            //                total++;
            //            }
            //            flag.Total += total;
            //            flag.IsCreating = false;
            //            await Cache.InsertAsync(CacheConst.PASSPORTFLAG, flag);
            //        }
            //    }
            //});

        }

        public async Task CancelCreatePassportIdAsync()
        {
            var flag = await Cache.GetAsync<PassportFlag>(CacheConst.PASSPORTFLAG);
            if (flag == null)
            {
                await Cache.InsertAsync(CacheConst.PASSPORTFLAG, new PassportFlag { Id = 1, IsCreating = false, IsValid = true });
                await Task.CompletedTask;
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
                // 处理会话信息更新逻辑
                // 这里可以添加具体的实现
                return await Task.FromResult(true);
            }
            catch (Exception ex)
            {
                // 记录日志
                return await Task.FromResult(false);
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

            await Cache.InsertAsync(key, attempts, TimeSpan.FromMinutes(LoginAttemptsWindowMinutes).Milliseconds);
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
            var sessionId = Guid.NewGuid().ToString("N");
            var sessionInfo = new SessionInfo
            {
                SessionId = sessionId,
                UserId = user.Id,
                PassportId = user.PassportId,
                AppId = loginDto.AppId,
                AppType = loginDto.AppType,
                CreateTime = DateTime.UtcNow,
                LastActiveTime = DateTime.UtcNow,
                IsActive = true
            };

            // 存储会话信息到缓存（序列化为JSON）
            var sessionKey = $"session_{sessionId}";
            var sessionJson = JsonConvert.SerializeObject(sessionInfo);
            await Cache.InsertAsync(sessionKey, sessionJson, (int)TimeSpan.FromHours(24).TotalSeconds); // 24小时有效期 (86400秒)

            // 记录活跃会话
            _activeSessions[sessionId] = sessionInfo;

            _logger.LogInformation("创建用户会话: SessionId={SessionId}, UserId={UserId}",
                sessionId, user.Id);

            return sessionId;
        }

        #endregion
    }
}
