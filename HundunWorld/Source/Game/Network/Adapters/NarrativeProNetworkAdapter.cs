using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Horizon.Game.Message;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using HundunWorld.Game.Network;
using MemoryPack;

namespace HundunWorld.Game.Network.Adapters
{
    public class NarrativeProNetworkAdapter : IMessageHandler
    {
        private readonly NetworkManager _networkManager;
        private Action<string> _onMessageReceived;

        public ServiceType ServiceType => ServiceType.Game;

        public List<MessageType> MessageTypes { get; } = new List<MessageType>
        {
            MessageType.Quest,
            MessageType.QuestUpdate,
            MessageType.AcceptQuest,
            MessageType.CompleteQuest,
            MessageType.QuestProgressNotify
        };

        public bool IsConnected => _networkManager?.CanSendMessage() ?? false;

        public NarrativeProNetworkAdapter(NetworkManager networkManager)
        {
            _networkManager = networkManager;
        }

        public void SetMessageReceivedCallback(Action<string> onMessageReceived)
        {
            _onMessageReceived = onMessageReceived;
        }

        public async Task<bool> SendNarrativeMessageAsync(string jsonPayload, int updateType)
        {
            if (_networkManager == null || !_networkManager.CanSendMessage())
                return false;

            try
            {
                var messageType = MapUpdateTypeToUpdateType(updateType);
                if (messageType == null)
                {
                    // 无对应信令的更新（对话类/None）在此丢弃，插件侧仍会在本地完成状态推进。
                    return false;
                }

                var horizonMessage = new NarrativeProHorizonMessage(jsonPayload)
                {
                    Type = messageType.Value,
                    ServiceType = ServiceType.Game
                };

                return await _networkManager.SendAsync(horizonMessage);
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[NarrativeProNetworkAdapter] Send failed: {ex.Message}");
                return false;
            }
        }

        public bool ValidateMessage(HorizonMessagePacket message)
        {
            return message?.Header != null && message.Body != null;
        }

        public bool CanHandle(MessageType messageType)
        {
            return MessageTypes.Contains(messageType);
        }

        public async Task HandleAsync(HorizonMessagePacket message)
        {
            try
            {
                if (message.Body is NarrativeProHorizonMessage narrativeMsg)
                {
                    _onMessageReceived?.Invoke(narrativeMsg.JsonPayload);
                }
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[NarrativeProNetworkAdapter] Handle failed: {ex.Message}");
            }
        }

        private MessageType? MapUpdateTypeToUpdateType(int updateType)
        {
            // 必须按插件侧 EUpdateType 的真实序号映射（Core/Enums.cs:58）：
            // None=0, CompleteTask=1, BeginQuest=2, ForgetQuest=3, RestartQuest=4,
            // QuestNewState=5, TaskProgressMade=6, DialogueExit=7, DialogueSkip=8。
            // 对话类没有对应的任务信令，宁可不上行也不能伪装成任务更新，否则服务端会按任务语义错误结算。
            return updateType switch
            {
                1 => MessageType.QuestProgressNotify, // CompleteTask
                2 => MessageType.AcceptQuest, // BeginQuest
                3 => MessageType.QuestUpdate, // ForgetQuest
                4 => MessageType.AcceptQuest, // RestartQuest
                5 => MessageType.QuestUpdate, // QuestNewState
                6 => MessageType.QuestProgressNotify, // TaskProgressMade
                _ => null // None / DialogueExit / DialogueSkip
            };
        }
    }

    
}
