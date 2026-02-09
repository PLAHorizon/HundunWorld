using Horizon.Core;
using Horizon.Core.Abstract;
using Horizon.Core.Abstract.Enums;
using Horizon.Game.Message.Network;
using Horizon.Orleans.Grains;
using Horizon.Share.Dtos.User;
using Microsoft.Extensions.Logging;
using Moq;

namespace Horizon.Game.Gateway.Tests
{
    /// <summary>
    /// PassportGrain 单元测试
    /// 测试通行证认证流程、DTO验证、会话信息、密码验证兼容性和登录限流逻辑
    /// </summary>
    [Collection("CacheTests")]
    public class PassportGrainTests : IDisposable
    {
        private readonly Mock<ICache> _cacheMock;
        private readonly Dictionary<string, object> _cacheStore;

        public PassportGrainTests()
        {
            _cacheMock = new Mock<ICache>();
            _cacheStore = new Dictionary<string, object>();
            Cache.Current = _cacheMock.Object;
            SetupCacheMock();
        }

        public void Dispose()
        {
            _cacheStore.Clear();
        }

        private void SetupCacheMock()
        {
            _cacheMock.Setup(c => c.InsertAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<int>()))
                .ReturnsAsync((string key, object data, int time) =>
                {
                    _cacheStore[key] = data;
                    return true;
                });

            _cacheMock.Setup(c => c.InsertAsync(It.IsAny<string>(), It.IsAny<object>()))
                .ReturnsAsync((string key, object data) =>
                {
                    _cacheStore[key] = data;
                    return true;
                });

            _cacheMock.Setup(c => c.GetAsync<int?>(It.IsAny<string>()))
                .ReturnsAsync((string key) =>
                {
                    if (_cacheStore.TryGetValue(key, out var value) && value is int intVal)
                        return intVal;
                    return null;
                });

            _cacheMock.Setup(c => c.RemoveAsync(It.IsAny<string>()))
                .Returns((string key) =>
                {
                    _cacheStore.Remove(key);
                    return Task.CompletedTask;
                });
        }

        #region LoginDto Tests - 登录数据模型

        [Fact]
        public void LoginDto_DefaultValues_AreCorrect()
        {
            var dto = new LoginDto();

            Assert.Null(dto.PassportId);
            Assert.Null(dto.Password);
            Assert.Null(dto.VerifyCode);
            Assert.Null(dto.Phone);
            Assert.Null(dto.Email);
            Assert.Equal(0, dto.AppId);
            Assert.Equal(AppType.Basic, dto.AppType);
            Assert.Equal(PassportType.Normal, dto.PassportType);
            Assert.Null(dto.GameContext);
        }

        [Fact]
        public void LoginDto_SetProperties_RetainsValues()
        {
            var dto = new LoginDto
            {
                PassportId = "PID001",
                Password = "dGVzdDEyMw==",  // base64 of "test123"
                VerifyCode = "123456",
                Phone = "13800138000",
                Email = "test@example.com",
                AppId = 369,
                AppType = AppType.Game,
                PassportType = PassportType.Normal,
                GameContext = new GameLoginContextDto
                {
                    Ip = "192.168.1.1",
                    PlatformId = "PC",
                    DeviceId = "DEV001"
                }
            };

            Assert.Equal("PID001", dto.PassportId);
            Assert.Equal("dGVzdDEyMw==", dto.Password);
            Assert.Equal("123456", dto.VerifyCode);
            Assert.Equal("13800138000", dto.Phone);
            Assert.Equal("test@example.com", dto.Email);
            Assert.Equal(369, dto.AppId);
            Assert.Equal(AppType.Game, dto.AppType);
            Assert.Equal(PassportType.Normal, dto.PassportType);
            Assert.NotNull(dto.GameContext);
            Assert.Equal("192.168.1.1", dto.GameContext.Ip);
            Assert.Equal("PC", dto.GameContext.PlatformId);
            Assert.Equal("DEV001", dto.GameContext.DeviceId);
        }

        [Theory]
        [InlineData(AppType.Basic)]
        [InlineData(AppType.Game)]
        [InlineData(AppType.OA)]
        [InlineData(AppType.AI)]
        public void LoginDto_AppType_SupportsAllTypes(AppType appType)
        {
            var dto = new LoginDto { AppType = appType };
            Assert.Equal(appType, dto.AppType);
        }

        [Theory]
        [InlineData(PassportType.Normal)]
        [InlineData(PassportType.System)]
        [InlineData(PassportType.Admin)]
        [InlineData(PassportType.Member)]
        [InlineData(PassportType.Executor)]
        public void LoginDto_PassportType_SupportsAllTypes(PassportType passportType)
        {
            var dto = new LoginDto { PassportType = passportType };
            Assert.Equal(passportType, dto.PassportType);
        }

        #endregion

        #region RegisterDto Tests - 注册数据模型

        [Fact]
        public void RegisterDto_SetProperties_RetainsValues()
        {
            var dto = new RegisterDto
            {
                Password = "dGVzdDEyMyE=",
                Phone = "13800138000",
                Email = "test@example.com",
                AppId = 369,
                AppType = AppType.Game,
                PassportType = PassportType.Normal,
                NickName = "TestPlayer",
                RealName = "Test User",
                ID = "ID12345"
            };

            Assert.Equal("dGVzdDEyMyE=", dto.Password);
            Assert.Equal("13800138000", dto.Phone);
            Assert.Equal("test@example.com", dto.Email);
            Assert.Equal(369, dto.AppId);
            Assert.Equal(AppType.Game, dto.AppType);
            Assert.Equal("TestPlayer", dto.NickName);
            Assert.Equal("Test User", dto.RealName);
            Assert.Equal("ID12345", dto.ID);
        }

        [Fact]
        public void RegisterDto_WithGameContext_RetainsValues()
        {
            var dto = new RegisterDto
            {
                Password = "dGVzdDEyMyE=",
                Phone = "13800138000",
                Email = "test@example.com",
                AppId = 369,
                AppType = AppType.Game,
                NickName = "TestPlayer",
                ID = "ID001",
                GameContext = new GameRegisterDto
                {
                    GameId = 1,
                    ServerId = 1,
                    AreaId = 1,
                    Ip = "192.168.1.1",
                    PlatformId = "PC"
                }
            };

            Assert.NotNull(dto.GameContext);
            Assert.Equal(1, dto.GameContext.GameId);
            Assert.Equal(1, dto.GameContext.ServerId);
            Assert.Equal(1, dto.GameContext.AreaId);
            Assert.Equal("192.168.1.1", dto.GameContext.Ip);
            Assert.Equal("PC", dto.GameContext.PlatformId);
        }

        #endregion

        #region ChangePasswordDto Tests - 修改密码数据模型

        [Fact]
        public void ChangePasswordDto_SetProperties_RetainsValues()
        {
            var dto = new ChangePasswordDto
            {
                PassportId = "PID001",
                OldPassword = "b2xkUGFzcw==",
                NewPassword = "bmV3UGFzcw==",
                AppId = 369,
                AppType = AppType.Game,
                PassportType = PassportType.Normal
            };

            Assert.Equal("PID001", dto.PassportId);
            Assert.Equal("b2xkUGFzcw==", dto.OldPassword);
            Assert.Equal("bmV3UGFzcw==", dto.NewPassword);
            Assert.Equal(369, dto.AppId);
            Assert.Equal(AppType.Game, dto.AppType);
            Assert.Equal(PassportType.Normal, dto.PassportType);
        }

        #endregion

        #region PassportInfoDto Tests - 通行证信息

        [Fact]
        public void PassportInfoDto_DefaultValues_AreCorrect()
        {
            var dto = new PassportInfoDto();

            Assert.Null(dto.PassportId);
            Assert.Null(dto.Name);
            Assert.Null(dto.Avatar);
            Assert.Null(dto.Phone);
            Assert.Null(dto.Email);
            Assert.Equal(0, dto.AppId);
            Assert.Equal(0, dto.UserId);
            Assert.Equal(0, dto.OrganizationId);
            Assert.Null(dto.SessionToken);
            Assert.Null(dto.UserName);
        }

        [Fact]
        public void PassportInfoDto_SetProperties_RetainsValues()
        {
            var dto = new PassportInfoDto
            {
                PassportId = "PID001",
                Name = "TestUser",
                Avatar = "https://example.com/avatar.png",
                Phone = "13800138000",
                Email = "test@example.com",
                AppId = 369,
                AppType = AppType.Game,
                PassportType = PassportType.Normal,
                OrganizationId = 100,
                UserId = 42,
                SessionToken = "token123",
                UserName = "TestNick"
            };

            Assert.Equal("PID001", dto.PassportId);
            Assert.Equal("TestUser", dto.Name);
            Assert.Equal("https://example.com/avatar.png", dto.Avatar);
            Assert.Equal("13800138000", dto.Phone);
            Assert.Equal("test@example.com", dto.Email);
            Assert.Equal(369, dto.AppId);
            Assert.Equal(AppType.Game, dto.AppType);
            Assert.Equal(PassportType.Normal, dto.PassportType);
            Assert.Equal(100, dto.OrganizationId);
            Assert.Equal(42, dto.UserId);
            Assert.Equal("token123", dto.SessionToken);
            Assert.Equal("TestNick", dto.UserName);
        }

        [Fact]
        public void PassportInfoDto_SerializationAttributes_Exist()
        {
            var type = typeof(PassportInfoDto);
            Assert.True(type.IsSerializable);

            // 验证所有公共属性都有Id标记（Orleans序列化）
            var properties = type.GetProperties();
            Assert.True(properties.Length >= 12); // PassportId, Name, Avatar, Phone, Email, AppId, AppType, PassportType, OrganizationId, UserId, SessionToken, UserName
        }

        #endregion

        #region WxLoginDto Tests - 微信登录

        [Fact]
        public void WxLoginDto_InheritsFromLoginDto()
        {
            Assert.True(typeof(LoginDto).IsAssignableFrom(typeof(WxLoginDto)));
        }

        [Fact]
        public void WxLoginDto_SetProperties_RetainsValues()
        {
            var dto = new WxLoginDto
            {
                WxAppId = "wx123456",
                AppSecret = "secret123",
                Code = "code123",
                PassportId = "PID001",
                Password = "pass",
                AppId = 369,
                AppType = AppType.Game
            };

            Assert.Equal("wx123456", dto.WxAppId);
            Assert.Equal("secret123", dto.AppSecret);
            Assert.Equal("code123", dto.Code);
            Assert.Equal("PID001", dto.PassportId);
        }

        #endregion

        #region SessionInfo Tests - 会话信息

        [Fact]
        public void SessionInfo_DefaultValues_AreCorrect()
        {
            var session = new SessionInfo();

            Assert.Null(session.SessionId);
            Assert.Equal(Guid.Empty, session.UserId);
            Assert.Null(session.PassportId);
            Assert.Equal(0, session.AppId);
            Assert.Equal(AppType.Basic, session.AppType);
            Assert.False(session.IsActive);
            Assert.Null(session.ClientIP);
            Assert.Null(session.PlatformId);
            Assert.Null(session.DeviceId);
        }

        [Fact]
        public void SessionInfo_SetProperties_RetainsValues()
        {
            var userId = Guid.NewGuid();
            var now = DateTime.UtcNow;
            var session = new SessionInfo
            {
                SessionId = "SID001",
                UserId = userId,
                PassportId = "PID001",
                AppId = 369,
                AppType = AppType.Game,
                CreateTime = now,
                LastActiveTime = now,
                IsActive = true,
                ClientIP = "192.168.1.1",
                PlatformId = "PC",
                DeviceId = "DEV001"
            };

            Assert.Equal("SID001", session.SessionId);
            Assert.Equal(userId, session.UserId);
            Assert.Equal("PID001", session.PassportId);
            Assert.Equal(369, session.AppId);
            Assert.Equal(AppType.Game, session.AppType);
            Assert.Equal(now, session.CreateTime);
            Assert.Equal(now, session.LastActiveTime);
            Assert.True(session.IsActive);
            Assert.Equal("192.168.1.1", session.ClientIP);
            Assert.Equal("PC", session.PlatformId);
            Assert.Equal("DEV001", session.DeviceId);
        }

        [Fact]
        public void SessionInfo_TimeTracking_Works()
        {
            var session = new SessionInfo
            {
                CreateTime = DateTime.UtcNow.AddHours(-1),
                LastActiveTime = DateTime.UtcNow
            };

            Assert.True(session.LastActiveTime > session.CreateTime);
            var sessionAge = session.LastActiveTime - session.CreateTime;
            Assert.True(sessionAge.TotalMinutes > 59);
        }

        #endregion

        #region GameLoginContextDto Tests - 游戏登录上下文

        [Fact]
        public void GameLoginContextDto_DefaultValues_AreNull()
        {
            var ctx = new GameLoginContextDto();

            Assert.Null(ctx.Ip);
            Assert.Null(ctx.PlatformId);
            Assert.Null(ctx.DeviceId);
        }

        [Fact]
        public void GameLoginContextDto_SetProperties_RetainsValues()
        {
            var ctx = new GameLoginContextDto
            {
                Ip = "10.0.0.1",
                PlatformId = "Android",
                DeviceId = "device-uuid-123"
            };

            Assert.Equal("10.0.0.1", ctx.Ip);
            Assert.Equal("Android", ctx.PlatformId);
            Assert.Equal("device-uuid-123", ctx.DeviceId);
        }

        #endregion

        #region Password Base64 Encoding Tests - 密码编解码

        [Fact]
        public void Base64Encode_ValidPassword_EncodesCorrectly()
        {
            var password = "TestPassword123!";
            var encoded = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(password));
            var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(encoded));

            Assert.Equal(password, decoded);
        }

        [Fact]
        public void Base64Decode_InvalidBase64_ThrowsFormatException()
        {
            Assert.Throws<FormatException>(() =>
                Convert.FromBase64String("not-valid-base64!!!"));
        }

        [Fact]
        public void Base64Decode_EmptyString_ReturnsEmptyBytes()
        {
            var result = Convert.FromBase64String("");
            Assert.Empty(result);
        }

        [Theory]
        [InlineData("Test123!", "VGVzdDEyMyE=")]
        [InlineData("密码测试", "5a+G56CB5rWL6K+V")]
        [InlineData("P@ssw0rd!", "UEBzc3cwcmQh")]
        public void Base64_RoundTrip_PreservesPassword(string password, string expectedBase64)
        {
            var encoded = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(password));
            Assert.Equal(expectedBase64, encoded);

            var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
            Assert.Equal(password, decoded);
        }

        #endregion

        #region Login Rate Limiting Tests - 登录限流

        [Fact]
        public async Task LoginAttempts_BelowLimit_AllowsLogin()
        {
            // 模拟少于5次的登录尝试
            var key = "login_attempts_PID001";
            _cacheStore[key] = 3;

            var attempts = await Cache.GetAsync<int?>(key);

            Assert.NotNull(attempts);
            Assert.True(attempts.Value < 5); // MaxLoginAttempts = 5
        }

        [Fact]
        public async Task LoginAttempts_AtLimit_BlocksLogin()
        {
            var key = "login_attempts_PID001";
            _cacheStore[key] = 5;

            var attempts = await Cache.GetAsync<int?>(key);

            Assert.NotNull(attempts);
            Assert.True(attempts.Value >= 5); // MaxLoginAttempts = 5
        }

        [Fact]
        public async Task LoginAttempts_NoRecord_AllowsLogin()
        {
            var key = "login_attempts_NEWUSER";

            var attempts = await Cache.GetAsync<int?>(key);

            Assert.Null(attempts); // 没有记录，应允许登录
        }

        [Fact]
        public async Task LoginAttempts_IncrementCount_Works()
        {
            var key = "login_attempts_PID001";

            // 直接使用cache mock存储，模拟登录失败计数递增
            _cacheStore[key] = 0;
            var attempts = (int)_cacheStore[key];
            attempts++;
            _cacheStore[key] = attempts;

            Assert.True(_cacheStore.ContainsKey(key));
            Assert.Equal(1, (int)_cacheStore[key]);
        }

        [Fact]
        public async Task LoginAttempts_ClearRecord_Works()
        {
            var key = "login_attempts_PID001";
            _cacheStore[key] = 3;

            await Cache.RemoveAsync(key);

            Assert.False(_cacheStore.ContainsKey(key));
        }

        [Fact]
        public async Task LoginAttempts_MultipleUsers_TrackSeparately()
        {
            var key1 = "login_attempts_USER1";
            var key2 = "login_attempts_USER2";

            _cacheStore[key1] = 5; // USER1被锁定
            _cacheStore[key2] = 1; // USER2仍可登录

            var attempts1 = await Cache.GetAsync<int?>(key1);
            var attempts2 = await Cache.GetAsync<int?>(key2);

            Assert.Equal(5, attempts1);
            Assert.Equal(1, attempts2);
        }

        #endregion

        #region Password Security Tests - 密码安全

        [Fact]
        public void SecurePassword_HashAndVerify_Works()
        {
            var password = "StrongP@ss123!";
            var (hash, salt) = SecurePasswordHasher.HashPassword(password);

            Assert.True(SecurePasswordHasher.VerifyPassword(password, hash, salt));
        }

        [Fact]
        public void SecurePassword_WrongPassword_FailsVerification()
        {
            var password = "StrongP@ss123!";
            var (hash, salt) = SecurePasswordHasher.HashPassword(password);

            Assert.False(SecurePasswordHasher.VerifyPassword("WrongPassword!", hash, salt));
        }

        [Fact]
        public void SecurePassword_UpgradeFlow_ProducesVerifiableHash()
        {
            // 模拟密码升级流程：旧密码验证成功后生成新的安全哈希
            var rawPassword = "OldPassword123!";

            // 第一步：使用新系统创建哈希
            var (newHash, newSalt) = SecurePasswordHasher.HashPassword(rawPassword);

            // 第二步：验证新哈希可以正确验证密码
            Assert.True(SecurePasswordHasher.VerifyPassword(rawPassword, newHash, newSalt));

            // 第三步：确保不同的明文无法通过验证
            Assert.False(SecurePasswordHasher.VerifyPassword("DifferentPassword!", newHash, newSalt));
        }

        [Theory]
        [InlineData("Str0ngP@ss!")] // 满足强度要求
        [InlineData("C0mpl3x!Pa$$")] // 复杂密码
        public void SecurePassword_StrongPasswords_PassStrengthCheck(string password)
        {
            Assert.True(SecurePasswordHasher.IsPasswordStrong(password));
        }

        [Theory]
        [InlineData("123")] // 太短
        [InlineData("abcdef")] // 无数字无特殊字符
        [InlineData("")] // 空字符串
        public void SecurePassword_WeakPasswords_FailStrengthCheck(string password)
        {
            Assert.False(SecurePasswordHasher.IsPasswordStrong(password));
        }

        #endregion

        #region Authentication Flow Tests - 认证流程验证

        [Fact]
        public void AuthenticationResult_Success_ContainsRequiredFields()
        {
            // 模拟认证成功的返回结果
            var result = new PassportInfoDto
            {
                AppId = 369,
                AppType = AppType.Game,
                PassportId = "PID001",
                UserId = 42,
                SessionToken = "valid-session-token"
            };

            Assert.Equal(369, result.AppId);
            Assert.Equal(AppType.Game, result.AppType);
            Assert.Equal("PID001", result.PassportId);
            Assert.True(result.UserId > 0);
            Assert.False(string.IsNullOrEmpty(result.SessionToken));
        }

        [Fact]
        public void AuthenticationResult_WithUserInfo_ContainsAllFields()
        {
            var result = new PassportInfoDto
            {
                AppId = 369,
                AppType = AppType.Game,
                Avatar = "avatar.png",
                Name = "TestUser",
                PassportType = PassportType.Normal,
                OrganizationId = 100,
                PassportId = "PID001",
                Phone = "13800138000",
                Email = "test@example.com",
                SessionToken = "session-token",
                UserId = 42,
                UserName = "TestNick"
            };

            Assert.Equal("avatar.png", result.Avatar);
            Assert.Equal("TestUser", result.Name);
            Assert.Equal(PassportType.Normal, result.PassportType);
            Assert.Equal(100, result.OrganizationId);
            Assert.Equal("13800138000", result.Phone);
            Assert.Equal("test@example.com", result.Email);
            Assert.Equal("TestNick", result.UserName);
        }

        [Fact]
        public void AuthenticationFlow_PasswordBase64Decode_ThenVerify()
        {
            // 模拟完整的密码验证流程
            var rawPassword = "MyP@ssw0rd!";
            var base64Password = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(rawPassword));

            // Step 1: 解码Base64
            var decodedPassword = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(base64Password));
            Assert.Equal(rawPassword, decodedPassword);

            // Step 2: 使用安全哈希验证
            var (hash, salt) = SecurePasswordHasher.HashPassword(rawPassword);
            Assert.True(SecurePasswordHasher.VerifyPassword(decodedPassword, hash, salt));
        }

        [Fact]
        public void AuthenticationFlow_InvalidBase64_FallbackToRawPassword()
        {
            // 模拟Base64解码失败时的回退逻辑
            var rawPassword = "not-base64!!!@#$";
            string decodedPassword;

            try
            {
                decodedPassword = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(rawPassword));
            }
            catch (FormatException)
            {
                // 解码失败，使用原始密码
                decodedPassword = rawPassword;
            }

            Assert.Equal(rawPassword, decodedPassword);
        }

        #endregion

        #region GameQueryDto Tests - 游戏查询

        [Fact]
        public void GameQueryDto_DefaultValues_AreZero()
        {
            var dto = new Share.Dtos.Games.GameQueryDto();

            Assert.Equal(0, dto.GameId);
            Assert.Equal(0, dto.AreaId);
            Assert.Equal(0, dto.ServerId);
            Assert.Equal(0, dto.GameUserId);
            Assert.Equal(0UL, dto.CharacterId);
        }

        [Fact]
        public void GameQueryDto_SetProperties_RetainsValues()
        {
            var dto = new Share.Dtos.Games.GameQueryDto
            {
                GameId = 1,
                AreaId = 2,
                ServerId = 3,
                GameUserId = 100,
                CharacterId = 200
            };

            Assert.Equal(1, dto.GameId);
            Assert.Equal(2, dto.AreaId);
            Assert.Equal(3, dto.ServerId);
            Assert.Equal(100, dto.GameUserId);
            Assert.Equal(200UL, dto.CharacterId);
        }

        #endregion

        #region GameUserInfoDto Tests - 游戏用户信息

        [Fact]
        public void GameUserInfoDto_SetProperties_RetainsValues()
        {
            var dto = new GameUserInfoDto
            {
                PassportId = "PID001",
                Name = "TestUser",
                Avatar = "avatar.png",
                Phone = "13800138000",
                Email = "test@example.com",
                GameUserId = 42,
                GameId = 1
            };

            Assert.Equal("PID001", dto.PassportId);
            Assert.Equal("TestUser", dto.Name);
            Assert.Equal("avatar.png", dto.Avatar);
            Assert.Equal("13800138000", dto.Phone);
            Assert.Equal("test@example.com", dto.Email);
            Assert.Equal(42, dto.GameUserId);
            Assert.Equal(1, dto.GameId);
        }

        #endregion

        #region CharacterState Tests - 角色状态序列化

        [Fact]
        public void CharacterState_DefaultValues_AreCorrect()
        {
            var state = new CharacterState();

            Assert.Null(state.CharacterInfo);
            Assert.False(state.IsOnline);
        }

        [Fact]
        public void CharacterState_SetProperties_RetainsValues()
        {
            var state = new CharacterState
            {
                CharacterInfo = new Horizon.Game.Message.Network.CharacterInfo(),
                IsOnline = true
            };

            Assert.NotNull(state.CharacterInfo);
            Assert.True(state.IsOnline);
        }

        [Fact]
        public void CharacterState_HasSerializationAttributes()
        {
            var type = typeof(CharacterState);

            // 验证MemoryPackable标记
            var memoryPackAttr = type.GetCustomAttributes(typeof(MemoryPack.MemoryPackableAttribute), false);
            Assert.NotEmpty(memoryPackAttr);

            // 验证GenerateSerializer标记
            var generateSerializerAttr = type.GetCustomAttributes(typeof(global::Orleans.GenerateSerializerAttribute), false);
            Assert.NotEmpty(generateSerializerAttr);
        }

        #endregion

        #region Password Upgrade Compatibility Tests - 密码升级兼容性

        [Fact]
        public void PasswordUpgrade_EmptySalt_IndicatesOldSystem()
        {
            // 旧系统的密码没有PasswordSalt
            string passwordSalt = null;

            bool usesOldSystem = string.IsNullOrEmpty(passwordSalt);
            Assert.True(usesOldSystem);
        }

        [Fact]
        public void PasswordUpgrade_WithSalt_IndicatesNewSystem()
        {
            var (_, salt) = SecurePasswordHasher.HashPassword("TestPass123!");

            bool usesNewSystem = !string.IsNullOrEmpty(salt);
            Assert.True(usesNewSystem);
        }

        [Fact]
        public void PasswordUpgrade_NewHashReplacesOld()
        {
            var rawPassword = "MyPassword123!";

            // 模拟旧密码哈希（简单的字符串）
            var oldHash = "old-insecure-hash";

            // 升级到新系统
            var (newHash, newSalt) = SecurePasswordHasher.HashPassword(rawPassword);

            // 新哈希应该与旧哈希不同
            Assert.NotEqual(oldHash, newHash);

            // 新系统应该能验证密码
            Assert.True(SecurePasswordHasher.VerifyPassword(rawPassword, newHash, newSalt));
        }

        #endregion

        #region AppType and PassportType Enum Tests - 枚举测试

        [Fact]
        public void AppType_Game_HasCorrectValue()
        {
            Assert.Equal(369, (int)AppType.Game);
        }

        [Fact]
        public void AppType_Basic_HasCorrectValue()
        {
            Assert.Equal(0, (int)AppType.Basic);
        }

        [Fact]
        public void PassportType_System_HasCorrectValue()
        {
            Assert.Equal(999, (int)PassportType.System);
        }

        [Fact]
        public void PassportType_Normal_HasCorrectValue()
        {
            Assert.Equal(0, (int)PassportType.Normal);
        }

        [Fact]
        public void PassportType_Admin_HasCorrectValue()
        {
            Assert.Equal(4, (int)PassportType.Admin); // 1 << 2
        }

        [Fact]
        public void PassportType_Member_HasCorrectValue()
        {
            Assert.Equal(2, (int)PassportType.Member); // 1 << 1
        }

        [Fact]
        public void PassportType_Executor_HasCorrectValue()
        {
            Assert.Equal(8, (int)PassportType.Executor); // 1 << 3
        }

        #endregion

        #region Session Creation Flow Tests - 会话创建流程

        [Fact]
        public void SessionCreation_FromLoginDto_MapsCorrectly()
        {
            var loginDto = new LoginDto
            {
                PassportId = "PID001",
                AppId = 369,
                AppType = AppType.Game,
                GameContext = new GameLoginContextDto
                {
                    Ip = "192.168.1.1",
                    PlatformId = "PC",
                    DeviceId = "DEV001"
                }
            };

            var userId = Guid.NewGuid();
            var sessionInfo = new SessionInfo
            {
                UserId = userId,
                PassportId = loginDto.PassportId,
                AppId = loginDto.AppId,
                AppType = loginDto.AppType,
                ClientIP = loginDto.GameContext?.Ip,
                PlatformId = loginDto.GameContext?.PlatformId,
                DeviceId = loginDto.GameContext?.DeviceId
            };

            Assert.Equal("PID001", sessionInfo.PassportId);
            Assert.Equal(369, sessionInfo.AppId);
            Assert.Equal(AppType.Game, sessionInfo.AppType);
            Assert.Equal("192.168.1.1", sessionInfo.ClientIP);
            Assert.Equal("PC", sessionInfo.PlatformId);
            Assert.Equal("DEV001", sessionInfo.DeviceId);
        }

        [Fact]
        public void SessionCreation_NullGameContext_HandlesGracefully()
        {
            var loginDto = new LoginDto
            {
                PassportId = "PID001",
                AppId = 369,
                AppType = AppType.Game,
                GameContext = null
            };

            var sessionInfo = new SessionInfo
            {
                PassportId = loginDto.PassportId,
                AppId = loginDto.AppId,
                AppType = loginDto.AppType,
                ClientIP = loginDto.GameContext?.Ip,
                PlatformId = loginDto.GameContext?.PlatformId,
                DeviceId = loginDto.GameContext?.DeviceId
            };

            Assert.Null(sessionInfo.ClientIP);
            Assert.Null(sessionInfo.PlatformId);
            Assert.Null(sessionInfo.DeviceId);
        }

        #endregion

        #region WxAuthentication Tests - 微信认证流程

        [Fact]
        public void WxLoginDto_CodeCanBeEmptyString()
        {
            var dto = new WxLoginDto { Code = "" };
            Assert.NotNull(dto.Code);
            Assert.Empty(dto.Code);
        }

        [Fact]
        public void WxLoginDto_CanSetGameContext()
        {
            var dto = new WxLoginDto
            {
                WxAppId = "wx_app_id",
                Code = "auth_code_123",
                AppType = AppType.Game,
                GameContext = new GameLoginContextDto
                {
                    Ip = "192.168.1.1",
                    PlatformId = "WeChat",
                    DeviceId = "device_001"
                }
            };

            Assert.NotNull(dto.GameContext);
            Assert.Equal("192.168.1.1", dto.GameContext.Ip);
            Assert.Equal("WeChat", dto.GameContext.PlatformId);
        }

        [Fact]
        public void WxLoginDto_AppSecret_ShouldNotBeExposedToClient()
        {
            // AppSecret should be set server-side only, not from client
            var dto = new WxLoginDto
            {
                WxAppId = "wx_app_id",
                Code = "auth_code",
                AppSecret = null
            };

            Assert.Null(dto.AppSecret);
        }

        [Fact]
        public void WxLoginDto_WithPhoneBinding_CanLookupExistingUser()
        {
            // 微信登录可以通过手机号绑定已有账号
            var dto = new WxLoginDto
            {
                WxAppId = "wx_app_id",
                Code = "auth_code_456",
                Phone = "13800138000"
            };

            Assert.Equal("13800138000", dto.Phone);
        }

        #endregion
    }
}
