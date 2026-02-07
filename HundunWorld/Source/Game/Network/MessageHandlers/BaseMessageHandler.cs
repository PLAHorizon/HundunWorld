using FlaxEngine;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using HundunWorld.Game.Network;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace HundunWorld.Game.Network.Handlers
{
    /// <summary>
    /// 基础消息处理器
    /// </summary>
    public abstract class BaseMessageHandler : IMessageHandler
    {
        private readonly MessageType _messageType;
        public abstract List<MessageType> MessageTypes { get; }

        public abstract ServiceType ServiceType { get; }
        public BaseMessageHandler() { }
        protected BaseMessageHandler(MessageType messageType)
        {
            _messageType = messageType;
        }

        public bool CanHandle(MessageType messageType)
        {
            return messageType == _messageType;
        }

        public abstract Task HandleAsync(HorizonMessagePacket message);
        /// <summary>
        /// 验证消息
        /// </summary>
        public virtual bool ValidateMessage(HorizonMessagePacket message)
        {
            if (message == null)
            {
                FlaxEngine.Debug.LogError("Message is null");
                return false;
            }

            if (!MessageTypes.Contains(message.Header.MessageType))
            {
                Debug.LogError($"Invalid message type. Expected: [{string.Join(", ", MessageTypes)}], Actual: {message.Header.MessageType}");
                return false;
            }

            if (message.ServiceType != ServiceType)
            {
                Debug.LogError($"Invalid service type. Expected: {ServiceType}, Actual: {message.ServiceType}");
                return false;
            }

            return true;
        }
    }
}