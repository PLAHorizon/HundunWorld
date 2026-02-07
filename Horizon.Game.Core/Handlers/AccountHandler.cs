using Horizon.Core;
using Horizon.Core.Abstract;
using Horizon.Core.Abstract.Enums;
using Horizon.Core.Helper;
using Horizon.Game.Message;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using Horizon.Orleans.Interface;
using Horizon.Share.Dtos.User;
using MemoryPack;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TouchSocket.Sockets;

namespace Horizon.Game.Core.Handlers
{
    /// <summary>
    /// 账户消息处理器
    /// 处理与用户账户相关的消息，包括登录、注册、角色管理等
    /// </summary>
    public class AccountHandler : MessageHandlerBase
    {
        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="logger">日志记录器</param>
        /// <param name="clusterClient">Orleans集群客户端</param>
        public AccountHandler(ILogger<MessageHandlerBase> logger, IClusterClient clusterClient, HorizonMessageAdapter adapter) : base(logger, clusterClient, adapter)
        {
        }

        /// <summary>
        /// 获取此处理器支持的消息类型列表
        /// </summary>
        public override List<MessageType> MessageTypes { get; } = new List<MessageType> {
            MessageType.LoginRequest,
            MessageType.RegisterRequest,
            MessageType.LoginResponse,
            MessageType.Logout
        };

        /// <summary>
        /// 获取此处理器支持的服务类型
        /// </summary>
        public override ServiceType ServiceType => ServiceType.Account;

        /// <summary>
        /// 路由消息处理
        /// 根据消息类型将消息路由到相应的处理方法
        /// </summary>
        /// <param name="message">消息包</param>
        /// <returns>处理结果和响应消息包</returns>
        public override async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> RouteHandlerAsync(HorizonMessagePacket message)
        {
            try
            {
                Logger.LogInformation("处理账户消息: {MessageType}", message.Header.MessageType);

                // 根据消息类型处理不同的账户消息
                switch (message.Header.MessageType)
                {
                    case MessageType.LoginRequest:
                        return await HandleLoginRequestAsync(message);
                    case MessageType.RegisterRequest:
                        return await HandleRegisterRequestAsync(message);
                    case MessageType.Logout:
                        return await HandleLogoutAsync(message);
                    default:
                        Logger.LogWarning("不支持的账户消息类型: {MessageType}", message.Header.MessageType);
                        return (false, message);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理账户消息时发生错误");
                return (false, message);
            }
        }
        private string Base64Encode(string plainText)
        {
            if (plainText == null) return string.Empty;
            var bytes = System.Text.Encoding.UTF8.GetBytes(plainText);
            return System.Convert.ToBase64String(bytes);
        }

        // 反向函数：将 Base64 字符串解码为明文
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
                Logger.LogWarning("Base64Decode: 输入不是有效的 Base64 字符串");
                return string.Empty;
            }
        }
        /// <summary>
        /// 处理登录请求消息
        /// </summary>
        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleLoginRequestAsync(HorizonMessagePacket request)
        {
            try
            {
                var loginRequest = request.Body as LoginRequest;
                if (loginRequest == null)
                {
                    Logger.LogWarning("登录请求消息体为空或类型不正确");
                    var errorResponse = CreateErrorResponse("登录请求消息体为空或类型不正确", request);
                    return (false, errorResponse);
                }

                Logger.LogInformation("处理用户登录请求: {AccountName}", loginRequest.AccountName);

                // 创建Orleans通行证Grain
                var passportId = Guid.NewGuid(); // 实际应用中应该从数据库获取
                var passportGrain = _clusterClient.GetGrain<IPassportGrain>(passportId);

                var pas = Base64Decode(loginRequest.Password);
                // 创建登录DTO
                var loginDto = new LoginDto
                {
                    PassportId = loginRequest.AccountName,
                    Password = PassportHelper.SetPasportPassword(loginRequest.AccountName, pas),
                    AppId = request.Header.GameId,
                    PassportType = PassportType.Normal,
                    AppType = AppType.Game // 假设是游戏应用
                };

                // 调用Orleans Grain进行认证
                var passportInfo = await passportGrain.AuthenticationAsync(loginDto);

                // 创建登录响应消息
                var loginResponse = new LoginResponse
                {
                    IsSuccess = passportInfo != null,
                    Message = passportInfo != null ? "登录成功" : "登录失败，用户名或密码错误",
                    PassportId = passportInfo?.PassportId ?? "",
                    UserId = (ulong)(passportInfo?.UserId ?? 0),
                    SessionToken = Guid.NewGuid().ToString(), // 实际应用中应该生成安全的会话令牌
                    Code = passportInfo != null ? 0 : 1, // 0表示成功，非0表示错误码
                    UserName = passportInfo?.UserName ?? ""
                };

                // 如果登录成功，添加角色列表和服务器列表
                if (passportInfo != null)
                {
                    loginResponse.Characters = new List<CharacterInfo>();
                    loginResponse.ServerList = new List<ServerInfo>();
                }

                // 创建响应消息包
                var responsePacket = CreateHorizonMessage(loginResponse);

                // 设置响应消息的头部信息，保持与请求一致的GameId, ZoneId, ServerId
                responsePacket.Header.GameId = request.Header.GameId;
                responsePacket.Header.ZoneId = request.Header.ZoneId;
                responsePacket.Header.ServerId = request.Header.ServerId;
                responsePacket.Header.ResponseToMessageId = request.Header.MessageId;
                responsePacket.Header.IsResponse = true;
                responsePacket.Header.RequireResponse = false;

                Logger.LogInformation("登录请求处理完成: {AccountName}, 结果: {IsSuccess}",
                    loginRequest.AccountName, loginResponse.IsSuccess);

                return (true, responsePacket);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理登录请求时发生错误");
                var errorResponse = CreateErrorResponse($"处理登录请求时发生错误: {ex.Message}", request);
                return (false, errorResponse);
            }
        }

        /// <summary>
        /// 处理注册请求消息
        /// </summary>
        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleRegisterRequestAsync(HorizonMessagePacket request)
        {
            try
            {
                var registerRequest = request.Body as RegisterRequest;
                if (registerRequest == null)
                {
                    Logger.LogWarning("注册请求消息体为空或类型不正确");
                    var errorResponse = CreateErrorResponse("注册请求消息体为空或类型不正确", request);
                    return (false, errorResponse);
                }

                Logger.LogInformation("处理用户注册请求: {NickName}", registerRequest.NickName);

                // 创建Orleans通行证Grain
                var passportId = Guid.NewGuid();
                var passportGrain = _clusterClient.GetGrain<IPassportGrain>(passportId);
                //var pas = Base64Decode(registerRequest.Password);
                // 创建注册DTO
                var registerDto = new RegisterDto
                {
                    NickName = registerRequest.NickName,
                    Password = registerRequest.Password,
                    Phone = registerRequest.PhoneNumber,
                    Email = registerRequest.Email,
                    RealName = registerRequest.RealName,
                    ID = registerRequest.ID,
                    AppId = request.Header.GameId,
                    AppType = AppType.Game, // 假设是游戏应用
                    PassportType = PassportType.Normal
                };

                // 调用Orleans Grain进行注册
                var passportInfo = await passportGrain.RegisterAsync(registerDto);

                // 创建注册响应消息
                var registerResponse = new RegisterResponse
                {
                    IsSuccess = passportInfo != null,
                    ErrorMessage = passportInfo != null ? "" : "注册失败",
                    PassportId = passportInfo?.PassportId ?? "",
                    RegisterTime = DateTimeOffset.Now.ToUnixTimeSeconds()
                };

                // 创建响应消息包
                var responsePacket = CreateHorizonMessage(registerResponse);

                // 设置响应消息的头部信息，保持与请求一致的GameId, ZoneId, ServerId
                responsePacket.Header.GameId = request.Header.GameId;
                responsePacket.Header.ZoneId = request.Header.ZoneId;
                responsePacket.Header.ServerId = request.Header.ServerId;
                responsePacket.Header.ResponseToMessageId = request.Header.MessageId;
                responsePacket.Header.IsResponse = true;
                responsePacket.Header.RequireResponse = false;

                Logger.LogInformation("注册请求处理完成: {NickName}, 结果: {IsSuccess}",
                    registerRequest.NickName, registerResponse.IsSuccess);

                return (true, responsePacket);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理注册请求时发生错误");
                var errorResponse = CreateErrorResponse($"处理注册请求时发生错误: {ex.Message}", request);
                return (false, errorResponse);
            }
        }

        /// <summary>
        /// 处理登出请求消息
        /// </summary>
        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleLogoutAsync(HorizonMessagePacket request)
        {
            try
            {
                // 处理登出逻辑
                var response = new LoginResponse
                {
                    IsSuccess = true,
                    Message = "登出成功",
                    ServiceType = ServiceType.Account,
                    Type = MessageType.LoginResponse
                };

                var responsePacket = CreateHorizonMessage(response);
                return (true, responsePacket);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理登出消息失败");
                return (false, CreateErrorResponse("处理登出消息失败", request));
            }
        }



        /// <summary>
        /// 创建错误响应消息
        /// </summary>
        private HorizonMessagePacket CreateErrorResponse(string errorMessage, HorizonMessagePacket request)
        {
            var errorResponse = new ErrorMessage
            {
                ErrorCode = 500,
                Message = errorMessage,
                Timestamp = DateTimeOffset.Now.ToUnixTimeMilliseconds(),
                RelatedMessageId = request.Header.MessageId
            };

            var responsePacket = CreateHorizonMessage(errorResponse);
            // 设置响应消息的头部信息，保持与请求一致的GameId, ZoneId, ServerId
            responsePacket.Header.GameId = request.Header.GameId;
            responsePacket.Header.ZoneId = request.Header.ZoneId;
            responsePacket.Header.ServerId = request.Header.ServerId;
            responsePacket.Header.ResponseToMessageId = request.Header.MessageId;
            responsePacket.Header.IsResponse = true;
            responsePacket.Header.RequireResponse = false;

            return responsePacket;
        }
    }
}
