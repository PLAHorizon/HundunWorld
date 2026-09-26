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
    /// 穿戴/卸下/使用物品的服务端回执消费方。
    /// <para>
    /// 网关对 <see cref="EquipItemMessage"/> 与 <see cref="UseItemRequest"/> 都以**同型消息**回包
    /// （EquipItem 响应仍是 MessageType.EquipItem，UseItem 响应是 MessageType.UseItem），
    /// 改造前客户端没有任何处理器认领这两类回包——穿戴被服务端拒绝时 UI 毫无察觉。
    /// </para>
    /// </summary>
    public class ItemOperationResponseHandler : BaseMessageHandler
    {
        public override List<MessageType> MessageTypes =>
            new List<MessageType> { MessageType.EquipItem, MessageType.UseItem, MessageType.GemSocket };

        public override ServiceType ServiceType => ServiceType.Game;

        public ItemOperationResponseHandler() : base(MessageType.EquipItem)
        {
        }

        public override Task HandleAsync(HorizonMessagePacket message)
        {
            var service = PlayerInventoryService.Instance;

            if (message.Body is EquipItemMessage equip)
            {
                // 回执链会触发快照重拉与 UI 刷新，必须回主线程。
                Scripting.InvokeOnUpdate(() => service.OnEquipResponse(equip));
            }
            else if (message.Body is UseItemResponse use)
            {
                Scripting.InvokeOnUpdate(() => service.OnUseItemResponse(use));
            }
            else if (message.Body is GemSocketResponse gem)
            {
                Scripting.InvokeOnUpdate(() => service.OnGemSocketResponse(gem));
            }

            return Task.CompletedTask;
        }
    }
}
