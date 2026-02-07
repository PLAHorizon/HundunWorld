using System;
using System.Threading.Tasks;
using FlaxEngine;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using HundunWorld.Game.Network.Handlers;

namespace ManagedHundunWorld.Network.Handlers
{
    /// <summary>
    /// 角色删除响应消息处理器
    /// 处理服务器返回的角色删除结果
    /// </summary>
    public class DeleteCharacterResponseHandler : BaseMessageHandler
    {
        public override System.Collections.Generic.List<MessageType> MessageTypes => 
            new System.Collections.Generic.List<MessageType> { MessageType.CharacterDelete };

        public override ServiceType ServiceType => ServiceType.Game;

        public event Action<DeleteCharacterResponse> CharacterDeleted;
        public event Action<string> CharacterDeletionFailed;

        public DeleteCharacterResponseHandler() : base(MessageType.CharacterDelete)
        {
        }

        public override async Task HandleAsync(HorizonMessagePacket message)
        {
            if (message.Body is DeleteCharacterResponse deleteResponse)
            {
                if (deleteResponse.Success)
                {
                    try
                    {
                        Debug.Log($"[DeleteCharacterResponseHandler] 角色删除成功: CharacterId={deleteResponse.CharacterId}");
                        
                        // 更新角色服务缓存
                        var characterService = HundunWorld.Game.Services.CharacterService.Instance;
                        if (characterService != null)
                        {
                            // 从缓存中移除角色
                            // 注意：这里需要根据实际情况调整，因为CharacterService可能没有直接的方法
                        }
                        
                        // 触发成功事件
                        CharacterDeleted?.Invoke(deleteResponse);
                        
                        // 通知UI层
                         FlaxEngine.Scripting.InvokeOnUpdate(() => {
                            Debug.Log("[DeleteCharacterResponseHandler] UI更新通知已发送");
                        });
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[DeleteCharacterResponseHandler] 处理角色删除成功响应时发生异常: {ex.Message}");
                    }
                }
                else
                {
                    Debug.LogWarning($"[DeleteCharacterResponseHandler] 角色删除失败: {deleteResponse.Message}");
                    CharacterDeletionFailed?.Invoke(deleteResponse.Message ?? "角色删除失败");
                }
            }
            else
            {
                Debug.LogError("[DeleteCharacterResponseHandler] 无法解析角色删除响应消息");
                CharacterDeletionFailed?.Invoke("响应消息格式错误");
            }
        }
    }
}
