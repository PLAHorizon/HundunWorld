using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using HundunWorld.Game.Network.Handlers;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ManagedHundunWorld.Network.Handlers
{
    /// <summary>
    /// 进入游戏响应消息处理器
    /// 处理服务端返回的进入游戏响应
    /// </summary>
    public class EnterGameHandler : BaseMessageHandler
    {
        public override List<MessageType> MessageTypes => 
            new List<MessageType> { MessageType.EnterGame };

        public override ServiceType ServiceType => ServiceType.Game;

        public event Action<EnterGameResponse> EnterGameSuccess;
        public event Action<string> EnterGameFailed;
       
        public EnterGameHandler() : base(MessageType.EnterGame)
        {
        }

        public override async Task HandleAsync(HorizonMessagePacket message)
        {
            try
            {
                if (message.Body is EnterGameResponse response)
                {
                    FlaxEngine.Debug.Log($"收到进入游戏响应: Success={response.Success}, Message={response.Message}");
                    
                    // 安全地在 UI 线程执行更新
                    FlaxEngine.Scripting.InvokeOnUpdate(() => {
                        if (response.Success)
                        {
                            // 触发成功事件
                            EnterGameSuccess?.Invoke(response);
                            
                            FlaxEngine.Debug.Log($"角色 [{response.CharacterInfo?.CharacterName}] 成功进入游戏世界");
                            
                            // 更新 UIStateManager 的选中角色信息
                            var stateManager = HundunWorld.Game.UI.UIStateManager.Instance;
                            if (stateManager != null)
                            {
                                var currentState = stateManager.GetCurrentState();
                                currentState.SelectedCharacter = response.CharacterInfo;
                                currentState.IncrementVersion();
                                
                                // 切换到游戏世界场景
                                stateManager.TransitionToScene(SceneType.GameWorld);
                                
                                FlaxEngine.Debug.Log("已切换到游戏世界场景");
                            }
                            
                            // 显示成功提示
                            HundunWorld.Game.UI.UIHelper.ShowSuccess("欢迎进入游戏世界！");
                        }
                        else
                        {
                            // 触发失败事件
                            EnterGameFailed?.Invoke(response.Message ?? "进入游戏失败");
                            
                            // 显示错误提示
                            FlaxEngine.Debug.LogWarning($"进入游戏失败: {response.Message}");
                            HundunWorld.Game.UI.UIHelper.ShowError(response.Message ?? "进入游戏失败，请稍后重试");
                        }
                    });
                }
                else
                {
                    FlaxEngine.Debug.LogError($"收到无效的进入游戏响应消息，Body类型: {message.Body?.GetType().Name ?? "null"}");
                    
                    FlaxEngine.Scripting.InvokeOnUpdate(() => {
                        EnterGameFailed?.Invoke("收到无效的进入游戏响应");
                    });
                }
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogException(ex);
                FlaxEngine.Debug.LogError($"处理进入游戏消息时发生异常: {ex.Message}");
                
                FlaxEngine.Scripting.InvokeOnUpdate(() => {
                    EnterGameFailed?.Invoke($"系统异常: {ex.Message}");
                });
            }

            await Task.CompletedTask;
        }
    }
}
