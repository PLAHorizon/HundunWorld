using Game.Database;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using HundunWorld.Game.Network.Handlers;
using HundunWorld.Game.UI.Authentication;
using HundunWorld.Game.UI;
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using static Game.Database.LiteDataContext;
using AuthenticationManager = HundunWorld.Game.UI.Authentication.AuthenticationManager;

namespace ManagedHundunWorld.Network.Handlers
{
    /// <summary>
    /// 注册响应消息处理器
    /// </summary>
    public class RegisterResponseHandler : BaseMessageHandler
    {
        public override List<MessageType> MessageTypes => new List<MessageType> { MessageType.RegisterResponse };

        public override ServiceType ServiceType => ServiceType.Account;

        public event Action<RegisterResponse> RegisterSuccess;
        public event Action<string> RegisterFailed;

        public RegisterResponseHandler() : base(MessageType.RegisterResponse)
        {
        }

        public override async Task HandleAsync(HorizonMessagePacket message)
        {
            if (message.Body is RegisterResponse registerResponse)
            {
                if (registerResponse.IsSuccess)
                {
                    try
                    {
                        // 获取注册时填写的密码（如果存在）
                        // 注意：这里需要使用注册表单中填写的密码，而不是脱敏后的
                        string password = "*********"; // 默认脱敏
                        var passport = AuthenticationManager.Instance?.Passport;
                        if (passport?.Password != null && passport.Password != "*********")
                        {
                            password = passport.Password;
                        }
                        else
                        {
                            // 密码还没有被设置，尝试从数据库获取之前保存的
                            var savedPassport = await DatabaseManager.GetPassport();
                            if (savedPassport != null && savedPassport.Password != "*********")
                            {
                                password = savedPassport.Password;
                            }
                        }
                        
                        // 保存护照信息，使用服务器返回的PassportId
                        var passportInfo = new PassportInfo
                        {
                            PassportId = registerResponse.PassportId,
                            UserId = 0,
                            IsCurrentPassport = true,
                            Password = password,
                            Token = "",
                            RememberPassword = !string.IsNullOrEmpty(password) && password != "*********"
                        };
                        
                        await DatabaseManager.SetPassport(passportInfo);
                        
                        // 更新AuthenticationManager的Passport
                        if (AuthenticationManager.Instance != null)
                        {
                            AuthenticationManager.Instance.Passport = passportInfo;
                            AuthenticationManager.Instance.HandleRegisterResponse(registerResponse);
                        }
                        
                        RegisterSuccess?.Invoke(registerResponse);
                        
                        FlaxEngine.Debug.Log($"[RegisterResponseHandler] 注册成功，已保存护照信息: PassportId={registerResponse.PassportId}, 已保存密码={password != "*********"}");
                    }
                    catch (Exception ex)
                    {
                        FlaxEngine.Debug.LogError($"[RegisterResponseHandler] 保存护照信息失败: {ex.Message}");
                        RegisterSuccess?.Invoke(registerResponse); // 即使保存失败也触发成功
                    }
                }
                else
                {
                    RegisterFailed?.Invoke(registerResponse.ErrorMessage);
                }
            }
            else
            {
                RegisterFailed?.Invoke("收到无效的注册响应消息");
            }
        
            await Task.CompletedTask;
        }
    }
}
