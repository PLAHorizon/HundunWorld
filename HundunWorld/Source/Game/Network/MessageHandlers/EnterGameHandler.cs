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

                            // 设置本地玩家 ID，使 SnapshotApplySystem 能区分本地玩家和远程玩家
                            var gameInstance = HundunWorld.Game.HundunWorldGame.Instance;
                            if (gameInstance != null && response.CharacterInfo != null)
                            {
                                gameInstance.SetPlayerId(response.CharacterInfo.CharacterId);
                                FlaxEngine.Debug.Log($"[EnterGameHandler] 已设置本地玩家ID: {response.CharacterInfo.CharacterId}");

                                // 创建本地玩家 ECS 实体（服务端不发送本地玩家的 Spawn delta，由客户端自行创建）。
                                // 初始位置优先从 EnterGameResponse.CharacterInfo.Position 获取，否则默认 (0,0,0)。
                                float initX = 0f, initY = 0f, initZ = 0f;
                                var pos = response.CharacterInfo.Position;
                                if (pos != null)
                                {
                                    initX = pos.X;
                                    initY = pos.Y;
                                    initZ = pos.Z;
                                }
                                gameInstance.CreateLocalPlayerEntity(response.CharacterInfo.CharacterId, initX, initY, initZ);
                                FlaxEngine.Debug.Log($"[EnterGameHandler] 已创建本地玩家 ECS 实体: CharacterId={response.CharacterInfo.CharacterId}, Pos=({initX},{initY},{initZ})");

                                // 缓存本地玩家 Actor 创建请求，待场景切换到 GameWorld 完成后再创建。
                                // 若在场景切换前创建，Actor 会被旧场景卸载时销毁。
                                gameInstance.RequestCreateLocalPlayerActor(response.CharacterInfo.CharacterId, initX, initY, initZ);
                            }

                            FlaxEngine.Debug.Log($"角色 [{response.CharacterInfo?.CharacterName}] 成功进入游戏世界");
                            
                            // 更新 UIStateManager 的选中角色信息
                            var stateManager = HundunWorld.Game.UI.UIStateManager.Instance;
                            if (stateManager != null)
                            {
                                // [修复] 使用 SetSelectedCharacter() 更新选中角色并触发 SelectedCharacterChanged 事件，
                                // 而非直接修改 UIState 副本（副本修改不会更新 _selectedCharacter 字段，也不触发事件），
                                // 导致 GameMainUI 无法收到通知更新角色名字。
                                stateManager.SetSelectedCharacter(response.CharacterInfo);

                                // 场景切换由 CharacterManager.EnterGameAsync 或 CharacterSelectionUI 降级路径统一负责，
                                // 此处不再调用 TransitionToScene，避免与 GameSceneManager.TransitionTo 职责重叠。
                                FlaxEngine.Debug.Log("已更新 UI 选中角色信息");
                            }
                            
                            // 发送同步握手包（握手完成后才能发送 InputPacket）
                            var networkManager = HundunWorld.Game.HundunWorldGame.Instance?.NetworkManager;
                            if (networkManager != null && response.CharacterInfo != null)
                            {
                                // 从 EnterGameResponse.CharacterInfo.Position 提取初始位置，
                                // 通过握手包下发给服务端，使服务端注册实体时使用真实位置而非 (0,0,0)。
                                var handshakePos = response.CharacterInfo.Position;
                                float handshakeX = handshakePos?.X ?? 0f;
                                float handshakeY = handshakePos?.Y ?? 0f;
                                float handshakeZ = handshakePos?.Z ?? 0f;
                                _ = networkManager.SendSyncHandshakeAsync(
                                    response.CharacterInfo.CharacterId,
                                    handshakeX,
                                    handshakeY,
                                    handshakeZ);
                                FlaxEngine.Debug.Log($"[EnterGameHandler] 已发起同步握手: CharacterId={response.CharacterInfo.CharacterId}, Pos=({handshakeX},{handshakeY},{handshakeZ})");
                            }
                            else
                            {
                                FlaxEngine.Debug.LogWarning("[EnterGameHandler] 无法发送同步握手：NetworkManager或CharacterInfo为空");
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
