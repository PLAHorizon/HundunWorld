using Horizon.Game.Message;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
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
        private readonly ILoggerFactory _loggerFactory;

        public AuthenticationHandler(ILogger<MessageHandlerBase> logger, IClusterClient clusterClient, 
            HorizonMessageAdapter adapter, ILoggerFactory loggerFactory = null, 
            AuthenticationValidator validator = null, SecurityManager securityManager = null) 
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
        }

        public override List<MessageType> MessageTypes => new()
        {
            MessageType.LoginRequest,
            MessageType.LoginResponse,
            MessageType.RegisterRequest,
            MessageType.RegisterResponse,
            MessageType.Logout,
            MessageType.SessionInfo
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

                // 7. 清除失败的登录尝试记录
                _securityManager.ClearLoginAttempts(loginRequest.AccountName, "");

                // 8. 构建成功响应
                var successResponse = new LoginResponse
                {
                    IsSuccess = true,
                    Message = "登录成功",
                    PassportId = authResult.PassportId,
                    UserId = (ulong)authResult.UserId,
                    SessionToken = sessionToken,
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
                // 这里可以添加登出逻辑，比如：
                // - 清理用户会话
                // - 更新用户状态
                // - 记录登出日志
                
                Logger.LogInformation("处理用户登出请求");

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
