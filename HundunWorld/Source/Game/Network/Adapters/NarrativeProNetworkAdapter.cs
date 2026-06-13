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

                var horizonMessage = new NarrativeProHorizonMessage(jsonPayload)
                {
                    Type = messageType,
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

        private MessageType MapUpdateTypeToUpdateType(int updateType)
        {
            return updateType switch
            {
                0 => MessageType.AcceptQuest,
                1 => MessageType.QuestUpdate,
                2 => MessageType.QuestUpdate,
                3 => MessageType.QuestProgressNotify,
                4 => MessageType.CompleteQuest,
                5 => MessageType.QuestProgressNotify,
                _ => MessageType.QuestUpdate
            };
        }
    }

    
}
