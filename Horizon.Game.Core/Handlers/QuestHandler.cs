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
    public class QuestHandler : MessageHandlerBase
    {
        public QuestHandler(ILogger<MessageHandlerBase> logger, IClusterClient clusterClient, HorizonMessageAdapter adapter) : base(logger, clusterClient, adapter)
        {

        }


        public override List<MessageType> MessageTypes { get; } = new List<MessageType> {
            MessageType.QuestUpdate,
            MessageType.AcceptQuest,
            MessageType.CompleteQuest
        };

        public override ServiceType ServiceType => ServiceType.Quest;



        public override async Task<(bool IsSuccess, MessageUnion? Response)> HandleAsync(ITcpSessionClient client, HorizonMessagePacket message)
        {
            return await base.HandleAsync(client, message);
        }

        public override async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> RouteHandlerAsync(HorizonMessagePacket message)
        {

            switch (message.Header.MessageType)
            {
                default:
                case MessageType.QuestUpdate:
                    return await HandleQuestUpdateAsync(message);
                case MessageType.AcceptQuest:
                    return await HandleAcceptQuestAsync(message);
                case MessageType.CompleteQuest:
                    return await HandleCompleteQuestAsync(message);
            }
        }

        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleQuestUpdateAsync(HorizonMessagePacket message)
        {
            try
            {
                QuestUpdateMessage questUpdateMessage = message.Body as QuestUpdateMessage;
                // 处理任务更新逻辑
                var response = new QuestUpdateMessage
                {
                    CharacterId = questUpdateMessage.CharacterId,
                    QuestId = questUpdateMessage.QuestId,
                    UpdatedQuest = questUpdateMessage.UpdatedQuest
                };
                var tem = CreateHorizonMessage(response);
                return (true, tem);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理任务更新消息失败");
                return (false, CreateHorizonMessage(new ErrorMessage { ErrorCode = 500, Message = "处理任务更新消息失败" }));
            }
        }

        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleAcceptQuestAsync(HorizonMessagePacket message)
        {
            try
            {
                AcceptQuestRequest acceptQuestRequest = message.Body as AcceptQuestRequest;
                // 处理接受任务逻辑
                var response = new AcceptQuestResponse
                {
                    Success = true,
                    Message = "接受任务成功",
                    AcceptedQuest = new QuestInfo()
                };
                var tem = CreateHorizonMessage(response);
                return (true, tem);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理接受任务消息失败");
                return (false, CreateHorizonMessage(new ErrorMessage { ErrorCode = 500, Message = "处理接受任务消息失败" }));
            }
        }

        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleCompleteQuestAsync(HorizonMessagePacket message)
        {
            try
            {
                CompleteQuestRequest completeQuestRequest = message.Body as CompleteQuestRequest;
                // 处理完成任务逻辑
                var response = new CompleteQuestResponse
                {
                    Success = true,
                    Message = "完成任务成功",
                    Rewards = new Dictionary<string, int>(),
                    CompletedQuestId = completeQuestRequest.QuestId
                };
                var tem = CreateHorizonMessage(response);
                return (true, tem);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理完成任务消息失败");
                return (false, CreateHorizonMessage(new ErrorMessage { ErrorCode = 500, Message = "处理完成任务消息失败" }));
            }
        }
    }
}