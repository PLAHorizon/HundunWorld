using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FlaxEngine;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using HundunWorld.Game.Network.Handlers;

namespace ManagedHundunWorld.Network.Handlers
{
    /// <summary>制作消息处理器：清单 / 学配方 / 制作 三条回执（共用 <see cref="MessageType.Crafting"/>）。</summary>
    public class CraftMessageHandler : BaseMessageHandler
    {
        public override List<MessageType> MessageTypes => new List<MessageType> { MessageType.Crafting };

        public override ServiceType ServiceType => ServiceType.Game;

        /// <summary>任何一条制作回执（清单/学/做）；调用方按 <c>Recipes.Count</c> 与 <c>Success</c> 分辨。</summary>
        public event Action<CraftingResponse> Responded;

        public CraftMessageHandler() : base(MessageType.Crafting)
        {
        }

        public override Task HandleAsync(HorizonMessagePacket message)
        {
            if (message?.Body is not CraftingResponse response)
            {
                Debug.LogWarning("[CraftMessageHandler] 收到非制作回执");
                return Task.CompletedTask;
            }

            Responded?.Invoke(response);
            return Task.CompletedTask;
        }
    }
}
