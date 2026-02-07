using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using HundunWorld.Game.Network.Handlers;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ManagedHundunWorld.Network.Handlers
{
    /// <summary>
    /// 创建角色响应消息处理器
    /// 处理服务端返回的创建角色响应
    /// </summary>
    public class CreateCharacterHandler : BaseMessageHandler
    {
        public override List<MessageType> MessageTypes => 
            new List<MessageType> { MessageType.CreateCharacter };

        public override ServiceType ServiceType => ServiceType.Game;

        public event Action<CreateCharacterResponse> CharacterCreated;
       
        public CreateCharacterHandler() : base(MessageType.CreateCharacter)
        {
        }

        public override async Task HandleAsync(HorizonMessagePacket message)
        {
            try
            {
                if (message.Body is CreateCharacterResponse response)
                {
                    FlaxEngine.Debug.Log($"收到创建角色响应: IsSuccess={response.IsSuccess}, Message={response.Message}");
                    
                    // 触发事件通知订阅者
                    CharacterCreated?.Invoke(response);
                    
                    // 安全地在 UI 线程执行更新
                    FlaxEngine.Scripting.InvokeOnUpdate(() => {
                        if (response.IsSuccess)
                        {
                            // 通知 CharacterManager 处理创建成功
                            var characterManager = Game.UI.Character.CharacterManager.Instance;
                            if (characterManager != null)
                            {
                                characterManager.HandleCreateCharacterResponse(response);
                                FlaxEngine.Debug.Log($"角色创建成功: {response.Character?.CharacterName}");
                            }
                            
                            // 显示成功提示
                            HundunWorld.Game.UI.UIHelper.ShowSuccess($"角色 [{response.Character?.CharacterName}] 创建成功！");
                        }
                        else
                        {
                            // 显示错误提示
                            FlaxEngine.Debug.LogWarning($"创建角色失败: {response.Message}");
                            HundunWorld.Game.UI.UIHelper.ShowError(response.Message ?? "创建角色失败");
                        }
                    });
                }
                else
                {
                    FlaxEngine.Debug.LogError($"收到无效的创建角色响应消息，Body类型: {message.Body?.GetType().Name ?? "null"}");
                }
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogException(ex);
                FlaxEngine.Debug.LogError($"处理创建角色消息时发生异常: {ex.Message}");
            }

            await Task.CompletedTask;
        }
    }
}
