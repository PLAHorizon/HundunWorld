using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using HundunWorld.Game.Network.Handlers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace ManagedHundunWorld.Network.Handlers
{
    /// <summary>
    /// 角色列表消息处理器
    /// 处理服务端返回的角色列表响应
    /// </summary>
    public class CharacterListHandler : BaseMessageHandler
    {
        public override List<MessageType> MessageTypes => new List<MessageType> { MessageType.CharacterList };

        public override ServiceType ServiceType => ServiceType.Game;

        public event Action<CharacterListResponse> CharacterListReceived;
       
        public CharacterListHandler() : base(MessageType.CharacterList)
        {
        }

        public override async Task HandleAsync(HorizonMessagePacket message)
        {
            try
            {
                if (message.Body is CharacterListResponse response)
                {
                    FlaxEngine.Debug.Log($"收到角色列表响应: IsSuccess={response.IsSuccess}, 角色数量={response.Characters?.Count ?? 0}");
                    
                    // 触发事件通知订阅者
                    CharacterListReceived?.Invoke(response);
                    
                    // 安全地在 UI 线程执行更新
                    FlaxEngine.Scripting.InvokeOnUpdate(() => {
                        if (response.IsSuccess)
                        {
                            // 通知 UIStateManager 更新角色列表
                            var stateManager = HundunWorld.Game.UI.UIStateManager.Instance;
                            if (stateManager != null)
                            {
                                var currentState = stateManager.GetCurrentState();
                                currentState.Characters = response.Characters;
                                currentState.IncrementVersion();
                                
                                FlaxEngine.Debug.Log($"已更新UIStateManager角色列表，共{currentState.Characters.Count}个角色");
                                
                                // 更新角色列表
                                stateManager.UpdateCharacterList(response.Characters);
                                
                                // 如果当前不在角色选择界面，才进行切换
                                if (stateManager.CurrentScene != SceneType.CharacterSelection)
                                {
                                    FlaxEngine.Debug.Log("当前不在角色选择界面，进行切换");
                                    stateManager.TransitionToScene(SceneType.CharacterSelection);
                                }
                                else
                                {
                                    FlaxEngine.Debug.Log("已在角色选择界面，角色列表将通过事件更新UI");
                                }
                            }
                        }
                        else
                        {
                            // 显示错误提示
                            FlaxEngine.Debug.LogWarning($"获取角色列表失败: {response.ErrorMessage}");
                            HundunWorld.Game.UI.UIHelper.ShowError(response.ErrorMessage ?? "获取角色列表失败");
                        }
                    });
                }
                else
                {
                    FlaxEngine.Debug.LogError($"收到无效的角色列表响应消息，Body类型: {message.Body?.GetType().Name ?? "null"}");
                }
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogException(ex);
                FlaxEngine.Debug.LogError($"处理角色列表消息时发生异常: {ex.Message}");
            }

            await Task.CompletedTask;
        }
    }
}
