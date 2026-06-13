using Horizon.Game.Message;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using Horizon.Game.Core.Interfaces;
using Horizon.Orleans.Interface;
using Horizon.Share.Dtos.User;
using MemoryPack;
using Microsoft.Extensions.Logging;
using Orleans;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TouchSocket.Sockets;
using Horizon.Game.Core.Security;
using Horizon.Core.Abstract;
using Horizon.Core.Abstract.Enums;
using Horizon.Core.Security;

namespace Horizon.Game.Core.Handlers
{
    /// <summary>
    /// 认证消息处理器
    /// 处理用户登录、注册、会话管理等认证相关消息
    /// </summary>
    public class AuthenticationHandler : MessageHandlerBase
    {
        private readonly AuthenticationValidator _validator;
        private readonly SecurityManager _securityManager;
        private readonly UserAuthTokenProvider _authTokenProvider;
        private readonly ILoggerFactory _loggerFactory;
        private readonly ICharacterFingerprintService _fingerprintService;

        public AuthenticationHandler(ILogger<MessageHandlerBase> logger, IClusterClient clusterClient, 
            HorizonMessageAdapter adapter, ILoggerFactory loggerFactory = null, 
            AuthenticationValidator validator = null, SecurityManager securityManager = null,
            UserAuthTokenProvider authTokenProvider = null,
            ICharacterFingerprintService characterFingerprintService = null) 
            : base(logger, clusterClient, adapter)
        {
            _loggerFactory = loggerFactory;
            _validator = validator ?? new AuthenticationValidator(
                _loggerFactory?.CreateLogger<AuthenticationValidator>() ?? 
                logger as ILogger<AuthenticationValidator> ?? 
                new Microsoft.Extensions.Logging.Abstractions.NullLogger<AuthenticationValidator>());
            _securityManager = securityManager ?? new SecurityManager(
                _loggerFactory?.CreateLogger<SecurityManager>() ?? 
                logger as ILogger<SecurityManager> ?? 
                new Microsoft.Extensions.Logging.Abstractions.NullLogger<SecurityManager>());
            _authTokenProvider = authTokenProvider;
            _fingerprintService = characterFingerprintService;
        }

        public override List<MessageType> MessageTypes => new()
        {
            MessageType.LoginRequest,
            MessageType.LoginResponse,
            MessageType.TokenLoginRequest,
            MessageType.TokenLoginResponse,
            MessageType.RegisterRequest,
            MessageType.RegisterResponse,
            MessageType.Logout,
            MessageType.SessionInfo,
            MessageType.BuildGameUserRequest
        };

        public override ServiceType ServiceType => ServiceType.Account;

        public override async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> RouteHandlerAsync(HorizonMessagePacket message)
        {
            try
            {
                Logger.LogInformation("处理认证消息: {MessageType}", message.Header.MessageType);

                switch (message.Header.MessageType)
                {
                    case MessageType.LoginRequest:
                        return await HandleLoginRequestAsync(message);
                    
                    case MessageType.RegisterRequest:
                        return await HandleRegisterRequestAsync(message);
                    
                    case MessageType.Logout:
                        return await HandleLogoutRequestAsync(message);
                    
                    case MessageType.SessionInfo:
                        return await HandleSessionInfoAsync(message);

                    case MessageType.TokenLoginRequest:
                        return await HandleTokenLoginRequestAsync(message);

                    case MessageType.BuildGameUserRequest:
                        return await HandleBuildGameUserAsync(message);
                        
                    default:
                        Logger.LogWarning("未支持的认证消息类型: {MessageType}", message.Header.MessageType);
                        return (false, null);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理认证消息时发生异常: {MessageType}", message.Header.MessageType);
                return (false, CreateErrorResponse(message, "认证服务异常"));
            }
        }

        /// <summary>
        /// 处理登录请求
        /// </summary>
        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleLoginRequestAsync(HorizonMessagePacket message)
        {
            try
            {
                var loginRequest = MemoryPackSerializer.Deserialize<LoginRequest>(message.RawData);
                if (loginRequest == null)
                {
                    Logger.LogError("无法反序列化登录请求消息");
                    return (false, CreateErrorResponse(message, "登录请求格式错误"));
                }

                Logger.LogInformation("处理用户登录请求: {AccountName}", loginRequest.AccountName);

                // 1. 安全验证
                var securityValidation = await ValidateLoginSecurity(loginRequest);
                if (!securityValidation.IsSuccess)
                {
                    return securityValidation;
                }

                // 2. 输入验证
                var inputValidation = ValidateLoginInput(loginRequest);
                if (!inputValidation.IsSuccess)
                {
                    return inputValidation;
                }

                // 3. 创建LoginDto
                var loginDto = new LoginDto
                {
                    PassportId = loginRequest.AccountName,
                    Password = loginRequest.Password,
                    AppId = message.Header.GameId,
                    AppType = AppType.Game,
                    PassportType = PassportType.Normal
                };

                // 4. 调用PassportGrain进行认证
                var passportGrain = _clusterClient.GetGrain<IPassportGrain>(Guid.NewGuid());
                var authResult = await passportGrain.AuthenticationAsync(loginDto);

                if (authResult == null)
                {
                    Logger.LogWarning("用户认证失败: {AccountName}", loginRequest.AccountName);
                    
                    // 记录失败的登录尝试
                    _securityManager.RecordFailedLoginAttempt(loginRequest.AccountName, ""); // IP信息可以从连接中获取
                    
                    var errorResponse = new LoginResponse
                    {
                        IsSuccess = false,
                        Message = "用户名或密码错误",
                        Code = 1002
                    };
                    return (true, CreateHorizonMessage(errorResponse));
                }

                // 5. 获取角色列表
                var characterGrain = _clusterClient.GetGrain<ICharacterGrain>(0);
                var gameQueryDto = new Share.Dtos.Games.GameQueryDto
                {
                    GameUserId = (long)authResult.UserId,
                    GameId = (int)message.Header.GameId
                };
                var characters = await characterGrain.GetAllCharactersAsync(gameQueryDto);

                // 6. 生成并存储会话令牌
                var sessionToken = _securityManager.GenerateSessionToken();
                await _securityManager.StoreSessionTokenAsync(sessionToken, authResult.UserId.ToString());

                // 7. 生成用户鉴权令牌（包含登录时间、机器ID与PassportId的加密数据）
                var authToken = "";
                if (_authTokenProvider != null)
                {
                    try
                    {
                        var machineId = message.Header.MachineId ?? "";
                        authToken = _authTokenProvider.GenerateToken(authResult.PassportId, machineId);
                        Logger.LogInformation("已为用户生成鉴权令牌: {PassportId}, MachineId: {MachineId}", 
                            authResult.PassportId, machineId);
}
                    catch (Exception ex)
                    {
                        Logger.LogError(ex, "生成鉴权令牌失败: {PassportId}", authResult.PassportId);
                        return (true, CreateHorizonMessage(new LoginResponse
                        {
                            IsSuccess = false,
                            Message = "鉴权令牌生成失败，请重新登录",
                            Code = 1008
                        }));
                    }
                }

                // 8. 清除失败的登录尝试记录
                _securityManager.ClearLoginAttempts(loginRequest.AccountName, "");

                // 9. 构建成功响应
                var successResponse = new LoginResponse
                {
                    IsSuccess = true,
                    Message = "登录成功",
                    PassportId = authResult.PassportId,
                    UserId = (ulong)authResult.UserId,
                    SessionToken = sessionToken,
                    AuthToken = authToken,
                    Characters = characters,
                    Code = 0
                };

                Logger.LogInformation("用户登录成功: {AccountName}, UserId: {UserId}, 角色数量: {CharacterCount}", 
                    loginRequest.AccountName, authResult.UserId, characters.Count);

                return (true, CreateHorizonMessage(successResponse));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理登录请求时发生异常");
                return (false, CreateErrorResponse(message, "登录处理异常"));
            }
        }

        /// <summary>
        /// 处理注册请求
        /// </summary>
        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleRegisterRequestAsync(HorizonMessagePacket message)
        {
            try
            {
                var registerRequest = MemoryPackSerializer.Deserialize<RegisterRequest>(message.RawData);
                if (registerRequest == null)
                {
                    Logger.LogError("无法反序列化注册请求消息");
                    return (false, CreateErrorResponse(message, "注册请求格式错误"));
                }

                Logger.LogInformation("处理用户注册请求: {NickName}, Email: {Email}", 
                    registerRequest.NickName, registerRequest.Email);

                // 1. 输入验证
                var inputValidation = await ValidateRegistrationInput(registerRequest);
                if (!inputValidation.IsSuccess)
                {
                    return inputValidation;
                }

                // 2. 创建RegisterDto
                var registerDto = new RegisterDto
                {
                    NickName = registerRequest.NickName,
                    Password = registerRequest.Password,
                    Phone = registerRequest.PhoneNumber,
                    Email = registerRequest.Email,
                    AppId = message.Header.GameId,
                    AppType = AppType.Game,
                    PassportType = PassportType.Normal
                };

                // 3. 调用PassportGrain进行注册
                var passportGrain = _clusterClient.GetGrain<IPassportGrain>(Guid.NewGuid());
                var registerResult = await passportGrain.RegisterAsync(registerDto);

                if (registerResult == null)
                {
                    Logger.LogWarning("用户注册失败: {NickName}", registerRequest.NickName);
                    var errorResponse = new RegisterResponse
                    {
                        IsSuccess = false,
                        ErrorMessage = "注册失败，请检查信息后重试"
                    };
                    return (true, CreateHorizonMessage(errorResponse));
                }

                // 4. 构建成功响应
                var successResponse = new RegisterResponse
                {
                    IsSuccess = true,
                    ErrorMessage = "",
                    PassportId = registerResult.PassportId,
                    NickName = registerRequest.NickName,
                    RegisterTime = DateTime.UtcNow.Ticks
                };

                Logger.LogInformation("用户注册成功: {NickName}, PassportId: {PassportId}", 
                    registerRequest.NickName, registerResult.PassportId);

                return (true, CreateHorizonMessage(successResponse));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理注册请求时发生异常");
                return (false, CreateErrorResponse(message, "注册处理异常"));
            }
        }

        /// <summary>
        /// 处理登出请求
        /// </summary>
        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleLogoutRequestAsync(HorizonMessagePacket message)
        {
            try
            {
                Logger.LogInformation("处理用户登出请求");

                // 用户登出时清除该连接关联的所有角色在线指纹
                if (_fingerprintService != null && _gameClient != null)
                {
                    try
                    {
                        await _fingerprintService.ReleaseByConnectionAsync(_gameClient.Id);
                        Logger.LogInformation("登出时已清理角色指纹: ConnectionId={ConnectionId}", _gameClient.Id);
                    }
                    catch (Exception fpEx)
                    {
                        Logger.LogWarning(fpEx, "登出时清理角色指纹失败: ConnectionId={ConnectionId}", _gameClient.Id);
                    }
                }

                // 简单的成功响应
                var response = new LoginResponse
                {
                    IsSuccess = true,
                    Message = "登出成功",
                    Code = 0
                };

                return (true, CreateHorizonMessage(response));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理登出请求时发生异常");
                return (false, CreateErrorResponse(message, "登出处理异常"));
            }
        }

        /// <summary>
        /// 处理会话信息请求
        /// </summary>
        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleSessionInfoAsync(HorizonMessagePacket message)
        {
            try
            {
                var sessionInfoMessage = MemoryPackSerializer.Deserialize<SessionInfoMessage>(message.RawData);
                if (sessionInfoMessage == null)
                {
                    Logger.LogError("无法反序列化会话信息消息");
                    return (false, CreateErrorResponse(message, "会话信息格式错误"));
                }

                Logger.LogInformation("处理会话信息更新: SessionId: {SessionId}", sessionInfoMessage.SessionId);

                // 调用PassportGrain更新会话信息
                var passportGrain = _clusterClient.GetGrain<IPassportGrain>(Guid.NewGuid());
                var updateResult = await passportGrain.UpdateSessionInfoAsync(sessionInfoMessage);

                // 返回处理结果
                var response = new SessionInfoMessage
                {
                    SessionId = sessionInfoMessage.SessionId,
                    UserId = sessionInfoMessage.UserId,
                    CreateTime = sessionInfoMessage.CreateTime,
                    LastActiveTime = DateTime.UtcNow.Ticks,
                    ClientIP = sessionInfoMessage.ClientIP,
                    PlatformId = sessionInfoMessage.PlatformId,
                    DeviceId = sessionInfoMessage.DeviceId
                };

                return (true, CreateHorizonMessage(response));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理会话信息时发生异常");
                return (false, CreateErrorResponse(message, "会话信息处理异常"));
            }
        }

        /// <summary>
        /// 处理构建游戏用户请求
        /// 当客户端启动游戏时发现不存在游戏用户记录，通过网关主动创建
        /// </summary>
        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleBuildGameUserAsync(HorizonMessagePacket message)
        {
            try
            {
                var request =message.Body as  BuildGameUserRequest;
                if (request == null || string.IsNullOrWhiteSpace(request.PassportId))
                {
                    Logger.LogError("无法反序列化构建游戏用户请求消息");
                    return (false, CreateHorizonMessage(new BuildGameUserResponse
                    {
                        IsSuccess = false,
                        ErrorMessage = "请求格式错误"
                    }));
                }

                Logger.LogInformation("处理构建游戏用户请求: PassportId={PassportId}, GameId={GameId}, AreaId={AreaId}, ServerId={ServerId}",
                    request.PassportId, request.GameId, request.AreaId, request.ServerId);

                var passportGrain = _clusterClient.GetGrain<IPassportGrain>(Guid.NewGuid());
                var gameUserId = await passportGrain.BuildGameUserAsync(
                    request.PassportId,
                    request.GameId,
                    request.AreaId,
                    request.ServerId);

                if (gameUserId <= 0)
                {
                    Logger.LogWarning("构建游戏用户失败: PassportId={PassportId}", request.PassportId);
                    return (true, CreateHorizonMessage(new BuildGameUserResponse
                    {
                        IsSuccess = false,
                        ErrorMessage = "游戏用户创建失败，请检查通行证信息是否有效"
                    }));
                }

                Logger.LogInformation("构建游戏用户成功: PassportId={PassportId}, GameUserId={GameUserId}",
                    request.PassportId, gameUserId);

                return (true, CreateHorizonMessage(new BuildGameUserResponse
                {
                    IsSuccess = true,
                    GameUserId = gameUserId
                }));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理构建游戏用户请求时发生异常");
                return (false, CreateHorizonMessage(new BuildGameUserResponse
                {
                    IsSuccess = false,
                    ErrorMessage = "服务器内部错误"
                }));
            }
        }

        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleTokenLoginRequestAsync(HorizonMessagePacket message)
        {
            try
            {
                var tokenLoginRequest =message.Body as  TokenLoginRequest;
                if (tokenLoginRequest == null)
                {
                    Logger.LogError("无法反序列化Token登录请求消息");
                    return (true, CreateHorizonMessage(new TokenLoginResponse
                    {
                        IsSuccess = false,
                        Message = "Token登录请求格式错误"
                    }));
                }

                Logger.LogInformation("处理Token登录请求: PassportId={PassportId}", tokenLoginRequest.PassportId);

                if (string.IsNullOrWhiteSpace(tokenLoginRequest.AuthToken))
                {
                    Logger.LogWarning("Token登录请求中AuthToken为空: PassportId={PassportId}", tokenLoginRequest.PassportId);
                    return (true, CreateHorizonMessage(new TokenLoginResponse
                    {
                        IsSuccess = false,
                        Message = "AuthToken不能为空"
                    }));
                }

                if (_authTokenProvider == null)
                {
                    Logger.LogWarning("AuthTokenProvider未配置，无法验证Token");
                    return (true, CreateHorizonMessage(new TokenLoginResponse
                    {
                        IsSuccess = false,
                        Message = "服务端Token验证服务不可用"
                    }));
                }

                // TokenLogin 使用跳过有效期检查的验证，允许过期令牌解密出身份信息后签发新令牌
                var validationResult = _authTokenProvider.ValidateTokenWithoutExpiryCheck(
                    tokenLoginRequest.AuthToken,
                    tokenLoginRequest.PassportId,
                    tokenLoginRequest.MachineId);

                if (!validationResult.IsValid)
                {
                    Logger.LogWarning("Token验证失败: PassportId={PassportId}, 原因={Reason}",
                        tokenLoginRequest.PassportId, validationResult.ErrorMessage);
                    return (true, CreateHorizonMessage(new TokenLoginResponse
                    {
                        IsSuccess = false,
                        Message = validationResult.ErrorMessage ?? "Token验证失败"
                    }));
                }

                var tokenData = validationResult.TokenData;

                var passportGrain = _clusterClient.GetGrain<IPassportGrain>(Guid.NewGuid());
                var grainValidation = await passportGrain.ValidateUserAuthTokenAsync(
                    tokenData.PassportId, tokenData.LoginTime, null);

                if (!grainValidation)
                {
                    Logger.LogInformation("Token Grain层二次验证失败（可能会话已过期），尝试重建会话: PassportId={PassportId}", tokenData.PassportId);

                    var sessionEnsured = await passportGrain.EnsureUserSessionAsync(
                        tokenData.PassportId, tokenLoginRequest.MachineId);
                    if (!sessionEnsured)
                    {
                        Logger.LogWarning("重建会话失败: PassportId={PassportId}", tokenData.PassportId);
                        return (true, CreateHorizonMessage(new TokenLoginResponse
                        {
                            IsSuccess = false,
                            Message = "Token验证失败，请重新登录"
                        }));
                    }
                }

                var gameUserId = await passportGrain.BuildGameUserAsync(
                    tokenData.PassportId,
                    (int)message.Header.GameId,
                    areaId: 1,
                    serverId: 1);

                if (gameUserId <= 0)
                {
                    Logger.LogWarning("Token登录构建游戏用户失败: PassportId={PassportId}", tokenData.PassportId);
                    return (true, CreateHorizonMessage(new TokenLoginResponse
                    {
                        IsSuccess = false,
                        Message = "游戏用户信息获取失败"
                    }));
                }

                var characterGrain = _clusterClient.GetGrain<ICharacterGrain>(0);
                var gameQueryDto = new Share.Dtos.Games.GameQueryDto
                {
                    GameUserId = gameUserId,
                    GameId = (int)message.Header.GameId
                };
                var characters = await characterGrain.GetAllCharactersAsync(gameQueryDto);

                var sessionToken = _securityManager.GenerateSessionToken();
                await _securityManager.StoreSessionTokenAsync(sessionToken, gameUserId.ToString());

                var newAuthToken = "";
                try
                {
                    var machineId = tokenLoginRequest.MachineId ?? "";
                    newAuthToken = _authTokenProvider.GenerateToken(tokenData.PassportId, machineId);
                    Logger.LogInformation("Token登录已刷新鉴权令牌: PassportId={PassportId}", tokenData.PassportId);
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Token登录刷新鉴权令牌失败: PassportId={PassportId}", tokenData.PassportId);
                    return (true, CreateHorizonMessage(new TokenLoginResponse
                    {
                        IsSuccess = false,
                        Message = "鉴权令牌生成失败，请重新登录"
                    }));
                }

                _securityManager.ClearLoginAttempts(tokenData.PassportId, "");

                var successResponse = new TokenLoginResponse
                {
                    IsSuccess = true,
                    Message = "Token登录成功",
                    PassportId = tokenData.PassportId,
                    UserId = (ulong)gameUserId,
                    SessionToken = sessionToken,
                    AuthToken = newAuthToken
                };

                Logger.LogInformation("Token登录成功: PassportId={PassportId}, GameUserId={GameUserId}, 角色数量={CharacterCount}",
                    tokenData.PassportId, gameUserId, characters.Count);

                return (true, CreateHorizonMessage(successResponse));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理Token登录请求时发生异常");
                return (true, CreateHorizonMessage(new TokenLoginResponse
                {
                    IsSuccess = false,
                    Message = "Token登录处理异常"
                }));
            }
        }

        /// <summary>
        /// 创建错误响应消息
        /// </summary>
        private HorizonMessagePacket CreateErrorResponse(HorizonMessagePacket originalMessage, string errorMessage)
        {
            var errorResponse = new AuthenticationError
            {
                ErrorCode = 500,
                ErrorMessage = errorMessage,
                ErrorDetails = $"处理消息类型 {originalMessage.Header.MessageType} 时发生错误",
                RetryAfterSeconds = 5,
                RequiresReconnect = false
            };

            return CreateHorizonMessage(errorResponse);
        }

        #region 安全验证辅助方法

        /// <summary>
        /// 验证登录安全性
        /// </summary>
        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> ValidateLoginSecurity(LoginRequest loginRequest)
        {
            try
            {
                // 检查登录尝试频率
                if (!_securityManager.CheckLoginAttempts(loginRequest.AccountName, "")) // IP信息可以从连接中获取
                {
                    Logger.LogWarning("登录尝试过于频繁: {AccountName}", loginRequest.AccountName);
                    var errorResponse = new AuthenticationError
                    {
                        ErrorCode = 1005,
                        ErrorMessage = "登录尝试过于频繁，请稍后再试",
                        RetryAfterSeconds = 300, // 5分钟后重试
                        RequiresReconnect = false
                    };
                    return (true, CreateHorizonMessage(errorResponse));
                }

                // 检查客户端版本
                var versionValidation = _validator.ValidateClientVersion(loginRequest.ClientVersion);
                if (!versionValidation.IsValid)
                {
                    Logger.LogWarning("客户端版本验证失败: {ClientVersion}, 错误: {Error}", 
                        loginRequest.ClientVersion, versionValidation.ErrorMessage);
                    var errorResponse = new AuthenticationError
                    {
                        ErrorCode = 1006,
                        ErrorMessage = versionValidation.ErrorMessage,
                        RequiresReconnect = true
                    };
                    return (true, CreateHorizonMessage(errorResponse));
                }

                return (true, null);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "验证登录安全性时发生异常");
                return (true, null); // 异常时允许通过
            }
        }

        /// <summary>
        /// 验证登录输入
        /// </summary>
        private (bool IsSuccess, HorizonMessagePacket MessagePacket) ValidateLoginInput(LoginRequest loginRequest)
        {
            try
            {
                // 验证账户名
                var accountValidation = _validator.ValidateAccountName(loginRequest.AccountName);
                if (!accountValidation.IsValid)
                {
                    Logger.LogWarning("账户名验证失败: {AccountName}, 错误: {Error}", 
                        loginRequest.AccountName, accountValidation.ErrorMessage);
                    var errorResponse = new LoginResponse
                    {
                        IsSuccess = false,
                        Message = accountValidation.ErrorMessage,
                        Code = 1001
                    };
                    return (true, CreateHorizonMessage(errorResponse));
                }

                // 验证密码格式（不验证具体内容）
                if (string.IsNullOrWhiteSpace(loginRequest.Password))
                {
                    var errorResponse = new LoginResponse
                    {
                        IsSuccess = false,
                        Message = "密码不能为空",
                        Code = 1002
                    };
                    return (true, CreateHorizonMessage(errorResponse));
                }

                return (true, null);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "验证登录输入时发生异常");
                return (true, null); // 异常时允许通过
            }
        }

        /// <summary>
        /// 验证注册输入
        /// </summary>
        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> ValidateRegistrationInput(RegisterRequest registerRequest)
        {
            try
            {
                // 批量验证用户信息
                var validation = await _validator.ValidateUserRegistrationAsync(
                    registerRequest.NickName,
                    registerRequest.Password,
                    registerRequest.Email,
                    registerRequest.PhoneNumber);

                if (!validation.IsValid)
                {
                    Logger.LogWarning("注册信息验证失败: {NickName}, 错误: {Error}", 
                        registerRequest.NickName, validation.ErrorMessage);
                    var errorResponse = new RegisterResponse
                    {
                        IsSuccess = false,
                        ErrorMessage = validation.ErrorMessage
                    };
                    return (true, CreateHorizonMessage(errorResponse));
                }

                return (true, null);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "验证注册输入时发生异常");
                return (true, null); // 异常时允许通过
            }
        }

        #endregion
    }
}
