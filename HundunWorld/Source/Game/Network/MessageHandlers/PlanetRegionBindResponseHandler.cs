using System.Collections.Generic;
using System.Threading.Tasks;
using FlaxEngine;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using HundunWorld.Game.Network.Handlers;
using HundunWorld.Game.Planet;

namespace ManagedHundunWorld.Network.Handlers
{
    /// <summary>
    /// 星球落位回执处理器。<b>只做一件事</b>：把包体交给 <see cref="PlanetRegionTransfers"/>。
    /// <para>为什么落位值得一条单独的处理器：这条上行此前根本不存在，客户端迁移永远撞
    /// <c>UnknownEntity</c> 然后按"重新落位"处置——而重新落位没有上行，屏幕上就是一直重连。
    /// 回执的 accepted 与理由要能被回执文件念出来，否则下一次还是只能靠猜。</para>
    /// <para>注册靠 <c>NetworkManager.AddAllMessageHandlers()</c> 的程序集扫描，不需要别处登记。</para>
    /// </summary>
    public class PlanetRegionBindResponseHandler : BaseMessageHandler
    {
        public override List<MessageType> MessageTypes { get; } =
            new List<MessageType> { MessageType.PlanetRegionBindResult };

        public override ServiceType ServiceType => ServiceType.Game;

        public PlanetRegionBindResponseHandler() : base(MessageType.PlanetRegionBindResult)
        {
        }

        public override Task HandleAsync(HorizonMessagePacket message)
        {
            if (message?.Body is PlanetRegionBindResult result)
            {
                PlanetRegionTransfers.OnBindReceipt(result);
                return Task.CompletedTask;
            }

            Debug.LogWarning(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "[planet] 落位回执包体类型不符，已丢弃。header={0} body={1}",
                message?.Header?.MessageId ?? "?", message?.Body?.GetType().Name ?? "null"));
            return Task.CompletedTask;
        }
    }
}
