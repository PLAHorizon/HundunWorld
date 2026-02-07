using Horizon.Game.Message;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using Horizon.Orleans.Interface;
using MemoryPack;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TouchSocket.Sockets;

namespace Horizon.Game.Core.Handlers
{
    public class ChatHandler : MessageHandlerBase
    {
        public ChatHandler(ILogger<MessageHandlerBase> logger, IClusterClient clusterClient, HorizonMessageAdapter adapter) : base(logger, clusterClient, adapter)
        {

        }


        public override List<MessageType> MessageTypes { get; } = new List<MessageType> {
            MessageType.Chat,
            MessageType.ChatHistory
        };

        public override ServiceType ServiceType => ServiceType.Chat;



        public override async Task<(bool IsSuccess, MessageUnion? Response)> HandleAsync(ITcpSessionClient client, HorizonMessagePacket message)
        {
            return await base.HandleAsync(client, message);
        }

        public override async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> RouteHandlerAsync(HorizonMessagePacket message)
        {

            switch (message.Header.MessageType)
            {
                default:
                case MessageType.Chat:
                    return await HandleChatAsync(message);
                case MessageType.ChatHistory:
                    return await HandleChatHistoryAsync(message);
            }
        }

        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleChatAsync(HorizonMessagePacket message)
        {
            try
            {
                ChatMessage chatMessage = message.Body as ChatMessage;
                // 处理聊天消息逻辑
                var response = new ChatMessage
                {
                    SenderId = chatMessage.SenderId,
                    SenderName = chatMessage.SenderName,
                    Content = chatMessage.Content,
                    ChannelType = chatMessage.ChannelType,
                    Timestamp = DateTime.UtcNow.Ticks
                };
                var tem = CreateHorizonMessage(response);
                return (true, tem);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理聊天消息失败");
                return (false, CreateHorizonMessage(new ErrorMessage { ErrorCode = 500, Message = "处理聊天消息失败" }));
            }
        }

        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleChatHistoryAsync(HorizonMessagePacket message)
        {
            try
            {
                ChatHistoryRequest chatHistoryRequest = message.Body as ChatHistoryRequest;
                // 处理聊天历史消息逻辑
                var response = new ChatHistoryResponse
                {
                    Messages = new List<ChatMessage>(),
                    HasMore = false
                };
                var tem = CreateHorizonMessage(response);
                return (true, tem);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理聊天历史消息失败");
                return (false, CreateHorizonMessage(new ErrorMessage { ErrorCode = 500, Message = "处理聊天历史消息失败" }));
            }
        }
    }
}