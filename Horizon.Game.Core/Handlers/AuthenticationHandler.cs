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
        /// 处理登录请求（已废弃，登录流程已迁移至 WebApi /Account/signin）
        /// 保留此处理器用于向客户端返回明确的错误提示，引导其使用 WebApi 登录流程。
        /// </summary>
        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleLoginRequestAsync(HorizonMessagePacket message)
        {
            try
            {
                // 尝试反序列化以记录请求信息（不阻塞错误响应）
                try
                {
                    var loginRequest = MemoryPackSerializer.Deserialize<LoginRequest>(message.RawData);
                    if (loginRequest != null)
                    {
                        Logger.LogWarning("收到 TCP 登录请求（已废弃）: {AccountName}, 提示客户端使用 WebApi 登录", loginRequest.AccountName);
                    }
                }
                catch (Exception deserializeEx)
                {
                    Logger.LogWarning(deserializeEx, "TCP 登录请求反序列化失败（预期行为，客户端应走 WebApi）");
                }

                // 返回错误响应，引导客户端使用 WebApi 登录
                var errorResponse = new LoginResponse
                {
                    IsSuccess = false,
                    Message = "登录流程已迁移至 WebApi，请通过 HTTP POST /Account/signin 接口登录，并将返回的 ImAuthToken 写入 HorizonGame.ini 后使用启动器配置登录",
                    Code = 1009
                };

                return (true, CreateHorizonMessage(errorResponse));
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

        /// <summary>
        /// 处理 Token 登录请求（已废弃，登录流程已迁移至 WebApi /Account/signin）
        /// 保留此处理器用于向客户端返回明确的错误提示，引导其使用 WebApi 登录流程。
        /// </summary>
        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleTokenLoginRequestAsync(HorizonMessagePacket message)
        {
            try
            {
                Logger.LogWarning("收到 TCP Token 登录请求（已废弃），提示客户端使用 WebApi 登录");

                // 返回错误响应，引导客户端使用 WebApi 登录
                var errorResponse = new TokenLoginResponse
                {
                    IsSuccess = false,
                    Message = "Token 登录流程已迁移至 WebApi，请通过 HTTP POST /Account/signin 接口登录获取新的 ImAuthToken"
                };

                return (true, CreateHorizonMessage(errorResponse));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理 Token 登录请求时发生异常");
                return (true, CreateHorizonMessage(new TokenLoginResponse
                {
                    IsSuccess = false,
                    Message = "Token 登录处理异常"
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
