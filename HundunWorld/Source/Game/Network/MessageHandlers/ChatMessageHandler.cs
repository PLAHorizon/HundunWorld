using FlaxEngine;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using HundunWorld.Game.Network.Handlers;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ManagedHundunWorld.Network.Handlers
{
    /// <summary>
    /// 聊天消息处理器
    /// 处理来自服务器的聊天消息、聊天历史和聊天通知
    /// </summary>
    public class ChatMessageHandler : BaseMessageHandler
    {
        public override List<MessageType> MessageTypes => new List<MessageType>
        {
            MessageType.Chat,
            MessageType.ChatHistory,
            MessageType.ChatNotify
        };

        public override ServiceType ServiceType => ServiceType.Chat;

        /// <summary>
        /// 收到聊天消息事件
        /// </summary>
        public event Action<ChatMessage> ChatMessageReceived;

        /// <summary>
        /// 收到聊天历史事件
        /// </summary>
        public event Action<ChatHistoryResponse> ChatHistoryReceived;

        /// <summary>
        /// 收到聊天通知事件
        /// </summary>
        public event Action<ChatNotifyMessage> ChatNotifyReceived;

        public ChatMessageHandler() : base(MessageType.Chat)
        {
        }

        public override async Task HandleAsync(HorizonMessagePacket message)
        {
            if (message?.Body == null)
            {
                Debug.LogWarning("[ChatMessageHandler] 收到空消息体");
                return;
            }

            switch (message.Body)
            {
                case ChatNotifyMessage chatNotify:
                    HandleChatNotify(chatNotify);
                    break;

                case ChatMessage chatMessage:
                    HandleChatMessage(chatMessage);
                    break;

                case ChatHistoryResponse historyResponse:
                    HandleChatHistory(historyResponse);
                    break;

                default:
                    Debug.LogWarning($"[ChatMessageHandler] 未知的消息体类型: {message.Body.GetType().Name}");
                    break;
            }

            await Task.CompletedTask;
        }

        private void HandleChatMessage(ChatMessage chatMessage)
        {
            Debug.Log($"[ChatMessageHandler] 收到聊天消息: 频道={chatMessage.ChannelType}, 发送者={chatMessage.SenderName}, 内容={chatMessage.Content}");
            ChatMessageReceived?.Invoke(chatMessage);
        }

        private void HandleChatHistory(ChatHistoryResponse historyResponse)
        {
            Debug.Log($"[ChatMessageHandler] 收到聊天历史: 消息数={historyResponse.Messages.Count}, 还有更多={historyResponse.HasMore}");
            ChatHistoryReceived?.Invoke(historyResponse);
        }

        private void HandleChatNotify(ChatNotifyMessage chatNotify)
        {
            Debug.Log($"[ChatMessageHandler] 收到聊天通知: 频道={chatNotify.Channel}, 发送者={chatNotify.SenderName}, 系统消息={chatNotify.IsSystemMessage}");
            ChatNotifyReceived?.Invoke(chatNotify);
        }
    }
}
