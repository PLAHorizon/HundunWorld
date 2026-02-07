using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using HundunWorld.Game.Network.Handlers;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ManagedHundunWorld.Network.Handlers
{
    /// <summary>
    /// 删除角色响应消息处理器
    /// 处理服务端返回的删除角色响应
    /// </summary>
    public class DeleteCharacterHandler : BaseMessageHandler
    {
        public override List<MessageType> MessageTypes => 
            new List<MessageType> { MessageType.CharacterDelete };

        public override ServiceType ServiceType => ServiceType.Game;

        public event Action<DeleteCharacterResponse> CharacterDeleted;
       
        public DeleteCharacterHandler() : base(MessageType.CharacterDelete)
        {
        }

        public override async Task HandleAsync(HorizonMessagePacket message)
        {
            try
            {
                if (message.Body is DeleteCharacterResponse response)
                {
                    FlaxEngine.Debug.Log($"收到删除角色响应: Success={response.Success}, CharacterId={response.CharacterId}");
                    
                    // 触发事件通知订阅者
                    CharacterDeleted?.Invoke(response);
                    
                    // 安全地在 UI 线程执行更新
                    FlaxEngine.Scripting.InvokeOnUpdate(() => {
                        if (response.Success)
                        {
                            // 通知 CharacterManager 处理删除成功
                            var characterManager = Game.UI.Character.CharacterManager.Instance;
                            if (characterManager != null)
                            {
                                characterManager.HandleDeleteCharacterResponse(response);
                                FlaxEngine.Debug.Log($"角色删除成功: CharacterId={response.CharacterId}");
                            }
                            
                            // 显示成功提示
                            HundunWorld.Game.UI.UIHelper.ShowSuccess("角色删除成功");
                        }
                        else
                        {
                            // 显示错误提示
                            FlaxEngine.Debug.LogWarning($"删除角色失败: {response.Message}");
                            HundunWorld.Game.UI.UIHelper.ShowError(response.Message ?? "删除角色失败");
                        }
                    });
                }
                else
                {
                    FlaxEngine.Debug.LogError($"收到无效的删除角色响应消息，Body类型: {message.Body?.GetType().Name ?? "null"}");
                }
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogException(ex);
                FlaxEngine.Debug.LogError($"处理删除角色消息时发生异常: {ex.Message}");
            }

            await Task.CompletedTask;
        }
    }
}
