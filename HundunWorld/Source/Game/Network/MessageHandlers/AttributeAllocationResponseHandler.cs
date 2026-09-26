using FlaxEngine;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using HundunWorld.Game.Network.Handlers;
using HundunWorld.Game.Services;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace HundunWorld.Game.Network.MessageHandlers
{
    /// <summary>
    /// 属性加点回执消费方：把服务端权威分配交回 <see cref="AttributeAllocationService"/>。
    /// 回执链会写属性组件并触发 UI 刷新，必须回主线程执行。
    /// </summary>
    public class AttributeAllocationResponseHandler : BaseMessageHandler
    {
        public override List<MessageType> MessageTypes =>
            new List<MessageType> { MessageType.AttributeAllocate };

        public override ServiceType ServiceType => ServiceType.Game;

        public AttributeAllocationResponseHandler() : base(MessageType.AttributeAllocate)
        {
        }

        public override Task HandleAsync(HorizonMessagePacket message)
        {
            if (message.Body is AttributeAllocateResponse response)
                Scripting.InvokeOnUpdate(() => AttributeAllocationService.Instance.OnAllocateResponse(response));

            return Task.CompletedTask;
        }
    }
}
