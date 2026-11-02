using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FlaxEngine;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using HundunWorld.Game.Network.Handlers;

namespace ManagedHundunWorld.Network.Handlers
{
    /// <summary>
    /// 商人消息处理器：三条回执（开窗 / 买 / 卖）各自一个事件。
    /// <para>
    /// 三条请求共用 <see cref="MessageType.Shop"/>，回执也用同一个类型回来，
    /// 所以这里按**报文类型**再分派一次（与网关的 <c>ShopHandler</c> 对称）。
    /// </para>
    /// </summary>
    public class ShopMessageHandler : BaseMessageHandler
    {
        public override List<MessageType> MessageTypes => new List<MessageType> { MessageType.Shop };

        public override ServiceType ServiceType => ServiceType.Trade;

        /// <summary>货架回执（含 Ok=false 的拒绝短码）。</summary>
        public event Action<ShopOpenResultMessage> ShopOpened;

        /// <summary>购买回执。</summary>
        public event Action<ShopBuyResultMessage> BuyResponded;

        /// <summary>卖出回执。</summary>
        public event Action<ShopSellResultMessage> SellResponded;

        public ShopMessageHandler() : base(MessageType.Shop)
        {
        }

        public override async Task HandleAsync(HorizonMessagePacket message)
        {
            if (message?.Body == null)
            {
                Debug.LogWarning("[ShopMessageHandler] 收到空消息体");
                return;
            }

            switch (message.Body)
            {
                case ShopOpenResultMessage open:
                    ShopOpened?.Invoke(open);
                    break;
                case ShopBuyResultMessage buy:
                    BuyResponded?.Invoke(buy);
                    break;
                case ShopSellResultMessage sell:
                    SellResponded?.Invoke(sell);
                    break;
                default:
                    Debug.LogWarning($"[ShopMessageHandler] 未识别的商人报文：{message.Body.GetType().Name}");
                    break;
            }

            await Task.CompletedTask;
        }
    }
}
