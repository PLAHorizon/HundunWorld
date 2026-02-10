using FlaxEngine;
using Horizon.Game.Core.Database;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using HundunWorld.Game.Network.Handlers;
using HundunWorld.Game.UI;
using HundunWorld.Game.UI.Authentication;
using HundunWorld.Game.UI.Character;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using static Horizon.Game.Core.Database.LiteDataContext;

namespace ManagedHundunWorld.Network.Handlers
{
    /// <summary>
    /// 登录响应消息处理器
    /// </summary>
    public class LoginResponseHandler : BaseMessageHandler
    {
        public override List<MessageType> MessageTypes => new List<MessageType> { MessageType.LoginResponse, MessageType.Logout };

        public override ServiceType ServiceType => ServiceType.Account;

        public event Action<LoginResponse> LoginSuccess;
        public event Action<string> LoginFailed;

        public LoginResponseHandler() : base(MessageType.LoginResponse)
        {
        }

        public override async Task HandleAsync(HorizonMessagePacket message)
        {
            if (message.Body is LoginResponse loginResponse)
            {
                if (loginResponse.IsSuccess)
                {
                    FlaxEngine.Debug.Log($"登录成功: UserId={loginResponse.UserId}, PassportId={loginResponse.PassportId}");
                    
                    // 触发登录成功事件
                    LoginSuccess?.Invoke(loginResponse);
                    
                    // 保存会话信息
                    await DatabaseManager.SetPassport(new PassportInfo
                    {
                        PassportId = loginResponse.PassportId,
                        UserId = loginResponse.UserId,
                        IsCurrentPassport = true,
                        Password = AuthenticationManager.Instance.Passport.Password,
                        Token = loginResponse.SessionToken,
                        RememberPassword = AuthenticationManager.Instance.Passport.RememberPassword
                    });
                    
                    // 安全地在 UI 线程执行
                    Scripting.InvokeOnUpdate(() => {
                        // 调用AuthenticationManager处理登录响应（必须在UI线程执行）
                        AuthenticationManager.Instance.HandleLoginResponse(loginResponse);
                        try
                        {
                            // 1. 更新用户会话状态
                            var stateManager = UIStateManager.Instance;
                            if (stateManager != null)
                            {
                                stateManager.HandleLoginSuccess(
                                    loginResponse.PassportId,
                                    loginResponse.UserId,
                                    loginResponse.SessionToken,
                                    ""
                                );
                                FlaxEngine.Debug.Log("[LoginResponseHandler] 用户会话已更新");
                            }
                            
                            // 2. 发送角色列表查询请求
                            FlaxEngine.Debug.Log("[LoginResponseHandler] 准备查询角色列表...");
                            var characterListRequest = new CharacterListRequest
                            {
                                UserId = loginResponse.UserId,
                                ServerId = 1 // 默认服务器ID
                            };
                            
                            var messagePacket = new HorizonMessagePacket
                            {
                                Header = new MessageHeader
                                {
                                    MessageId = Guid.NewGuid().ToString(),
                                    MessageType = MessageType.CharacterList,
                                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                                },
                                ServiceType = ServiceType.Game,
                                Body = new CharacterListRequest { UserId = loginResponse.UserId }
                            };

                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    await HundunWorld.Game.HundunWorldGame.Instance.NetworkManager.SendMessageAsync(messagePacket);
                                    FlaxEngine.Debug.Log("[LoginResponseHandler] 角色列表查询请求已发送");
                                }
                                catch (Exception ex)
                                {
                                    FlaxEngine.Debug.LogError($"[LoginResponseHandler] 发送角色列表查询请求失败: {ex.Message}");
                                    FlaxEngine.Scripting.InvokeOnUpdate(() =>
                                    {
                                        UIHelper.ShowError("获取角色列表失败，请重试");
                                    });
                                }
                            });
                            
                            // 3. 使用GameSceneManager切换到角色选择场景
                            var sceneManager = GameSceneManager.GetOrCreate();
                            if (sceneManager != null)
                            {
                                FlaxEngine.Debug.Log("[LoginResponseHandler] 使用GameSceneManager切换场景");
                                
                                // 订阅场景切换完成事件，在场景加载完成后同步状态
                                void OnTransitionCompleted(SceneType from, SceneType to)
                                {
                                    sceneManager.TransitionCompleted -= OnTransitionCompleted;
                                    
                                    FlaxEngine.Debug.Log($"[LoginResponseHandler] 场景切换完成: {from} -> {to}");
                                    
                                    // 同步UIStateManager的场景状态（仅在状态不一致时）
                                    var sm = UIStateManager.Instance;
                                    if (sm != null && sm.CurrentScene != to)
                                    {
                                        sm.TransitionToScene(to, false);
                                    }
                                }
                                
                                sceneManager.TransitionCompleted += OnTransitionCompleted;
                                
                                if (!sceneManager.TransitionTo(SceneType.CharacterSelection))
                                {
                                    sceneManager.TransitionCompleted -= OnTransitionCompleted;
                                    FlaxEngine.Debug.LogError("[LoginResponseHandler] 场景切换启动失败");
                                    UIHelper.ShowError("场景切换失败，请重试");
                                }
                                else
                                {
                                    FlaxEngine.Debug.Log("[LoginResponseHandler] 场景切换已启动，等待加载完成");
                                }
                            }
                            else
                            {
                                FlaxEngine.Debug.LogError("[LoginResponseHandler] GameSceneManager未初始化");
                                UIHelper.ShowError("系统错误，请重启游戏");
                            }
                        }
                        catch (Exception ex)
                        {
                            FlaxEngine.Debug.LogException(ex);
                            FlaxEngine.Debug.LogError($"[LoginResponseHandler] 处理登录成功响应时发生异常: {ex.Message}");
                        }
                    });
                }
                else
                {
                    FlaxEngine.Debug.LogWarning($"登录失败: {loginResponse.Message}");
                    
                    Scripting.InvokeOnUpdate(() => {
                        // 调用AuthenticationManager处理登录失败响应，触发LoginResponseReceived事件以更新UI
                        AuthenticationManager.Instance.HandleLoginResponse(loginResponse);
                        
                        var stateManager = UIStateManager.Instance;
                        if (stateManager != null)
                        {
                            stateManager.SetError(loginResponse.Message);
                        }
                        var confirmDialog = UIHelper.CreateConfirmDialog("登录", loginResponse.Message, () => { }, false);
                    });
                    
                    LoginFailed?.Invoke(loginResponse.Message);
                }
            }
            else
            {
                FlaxEngine.Debug.LogError("收到无效的登录响应消息");
                LoginFailed?.Invoke("收到无效的登录响应消息");
            }

            await Task.CompletedTask;
        }
    }
}
