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
    /// 排行榜消息处理器
    /// 处理排行榜查询响应
    /// </summary>
    public class RankingMessageHandler : BaseMessageHandler
    {
        public override List<MessageType> MessageTypes => new List<MessageType>
        {
            MessageType.RankingQueryResponse
        };

        public override ServiceType ServiceType => ServiceType.Game;

        /// <summary>
        /// 排行榜查询结果事件
        /// </summary>
        public event Action<RankingQueryResponseMessage> RankingResultReceived;

        public RankingMessageHandler() : base(MessageType.RankingQueryResponse)
        {
        }

        public override async Task HandleAsync(HorizonMessagePacket message)
        {
            if (message?.Body == null)
            {
                Debug.LogWarning("[RankingMessageHandler] 收到空消息体");
                return;
            }

            switch (message.Body)
            {
                case RankingQueryResponseMessage rankingResult:
                    HandleRankingResult(rankingResult);
                    break;

                default:
                    Debug.LogWarning($"[RankingMessageHandler] 未知的消息体类型: {message.Body.GetType().Name}");
                    break;
            }

            await Task.CompletedTask;
        }

        private void HandleRankingResult(RankingQueryResponseMessage rankingResult)
        {
            Debug.Log($"[RankingMessageHandler] 收到排行榜: {rankingResult.RankingName}, 条目数={rankingResult.Entries.Count}, 我的排名={rankingResult.MyRank}");
            RankingResultReceived?.Invoke(rankingResult);
        }
    }
}
