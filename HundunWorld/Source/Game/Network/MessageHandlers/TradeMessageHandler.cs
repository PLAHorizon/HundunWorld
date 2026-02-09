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
    /// 交易消息处理器
    /// 处理来自服务器的交易请求、交易响应、交易状态更新和市场搜索结果
    /// </summary>
    public class TradeMessageHandler : BaseMessageHandler
    {
        public override List<MessageType> MessageTypes => new List<MessageType>
        {
            MessageType.TradeRequest,
            MessageType.TradeResponse,
            MessageType.TradeUpdateNotify,
            MessageType.MarketSearchResponse
        };

        public override ServiceType ServiceType => ServiceType.Trade;

        /// <summary>
        /// 收到交易请求事件
        /// </summary>
        public event Action<TradeRequestMessage> TradeRequestReceived;

        /// <summary>
        /// 收到交易响应事件
        /// </summary>
        public event Action<TradeResponseMessage> TradeResponseReceived;

        /// <summary>
        /// 交易状态更新事件
        /// </summary>
        public event Action<TradeUpdateNotifyMessage> TradeUpdated;

        /// <summary>
        /// 市场搜索结果事件
        /// </summary>
        public event Action<MarketSearchResponseMessage> MarketSearchResultReceived;

        public TradeMessageHandler() : base(MessageType.TradeRequest)
        {
        }

        public override async Task HandleAsync(HorizonMessagePacket message)
        {
            if (message?.Body == null)
            {
                Debug.LogWarning("[TradeMessageHandler] 收到空消息体");
                return;
            }

            switch (message.Body)
            {
                case TradeRequestMessage tradeRequest:
                    HandleTradeRequest(tradeRequest);
                    break;

                case TradeResponseMessage tradeResponse:
                    HandleTradeResponse(tradeResponse);
                    break;

                case TradeUpdateNotifyMessage tradeUpdate:
                    HandleTradeUpdate(tradeUpdate);
                    break;

                case MarketSearchResponseMessage marketSearch:
                    HandleMarketSearchResult(marketSearch);
                    break;

                default:
                    Debug.LogWarning($"[TradeMessageHandler] 未知的消息体类型: {message.Body.GetType().Name}");
                    break;
            }

            await Task.CompletedTask;
        }

        private void HandleTradeRequest(TradeRequestMessage tradeRequest)
        {
            Debug.Log($"[TradeMessageHandler] 收到交易请求: 发起者={tradeRequest.InitiatorId}, 目标={tradeRequest.TargetName}");
            TradeRequestReceived?.Invoke(tradeRequest);
        }

        private void HandleTradeResponse(TradeResponseMessage tradeResponse)
        {
            Debug.Log($"[TradeMessageHandler] 收到交易响应: 交易ID={tradeResponse.TradeId}, 接受={tradeResponse.Accepted}");
            TradeResponseReceived?.Invoke(tradeResponse);
        }

        private void HandleTradeUpdate(TradeUpdateNotifyMessage tradeUpdate)
        {
            Debug.Log($"[TradeMessageHandler] 交易状态更新: 交易ID={tradeUpdate.TradeId}, 状态={tradeUpdate.Status}");
            TradeUpdated?.Invoke(tradeUpdate);
        }

        private void HandleMarketSearchResult(MarketSearchResponseMessage marketSearch)
        {
            Debug.Log($"[TradeMessageHandler] 市场搜索结果: 商品数={marketSearch.Listings.Count}, 总数={marketSearch.TotalCount}");
            MarketSearchResultReceived?.Invoke(marketSearch);
        }
    }
}
