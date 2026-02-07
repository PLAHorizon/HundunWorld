using System;
using System.Threading.Tasks;
using FlaxEngine;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using HundunWorld.Game.Network.Handlers;

namespace ManagedHundunWorld.Network.Handlers
{
    /// <summary>
    /// 角色创建响应消息处理器
    /// 处理服务器返回的角色创建结果
    /// </summary>
    public class CreateCharacterResponseHandler : BaseMessageHandler
    {
        public override System.Collections.Generic.List<MessageType> MessageTypes => 
            new System.Collections.Generic.List<MessageType> { MessageType.CreateCharacter };

        public override ServiceType ServiceType => ServiceType.Game;

        public event Action<CreateCharacterResponse> CharacterCreated;
        public event Action<string> CharacterCreationFailed;

        public CreateCharacterResponseHandler() : base(MessageType.CreateCharacter)
        {
        }

        public override async Task HandleAsync(HorizonMessagePacket message)
        {
            if (message.Body is CreateCharacterResponse createResponse)
            {
                if (createResponse.IsSuccess)
                {
                    try
                    {
                        Debug.Log($"[CreateCharacterResponseHandler] 角色创建成功: {createResponse.Character?.CharacterName}");
                        
                        // 更新角色服务缓存
                        var characterService = HundunWorld.Game.Services.CharacterService.Instance;
                        if (characterService != null && createResponse.Character != null)
                        {
                            characterService.AddCharacterToCache(createResponse.Character);
                        }
                        
                        // 触发成功事件
                        CharacterCreated?.Invoke(createResponse);
                        
                        // 通知UI层
                         FlaxEngine.Scripting.InvokeOnUpdate(() => {
                            // 可以在这里调用UI更新方法
                            Debug.Log("[CreateCharacterResponseHandler] UI更新通知已发送");
                        });
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[CreateCharacterResponseHandler] 处理角色创建成功响应时发生异常: {ex.Message}");
                    }
                }
                else
                {
                    Debug.LogWarning($"[CreateCharacterResponseHandler] 角色创建失败: {createResponse.Message}");
                    CharacterCreationFailed?.Invoke(createResponse.Message ?? "角色创建失败");
                }
            }
            else
            {
                Debug.LogError("[CreateCharacterResponseHandler] 无法解析角色创建响应消息");
                CharacterCreationFailed?.Invoke("响应消息格式错误");
            }
        }
    }
}
