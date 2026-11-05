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
    /// 区域迁移回执处理器。<b>只做一件事</b>：把包体交给 <see cref="PlanetRegionTransfers"/>，
    /// 不在这里判任何规则——处置规则（该不该换、要不要重新落位、要不要等时钟）全在
    /// <c>PlanetRegionTransferRules</c> 里，那边有离线判据，这边没有。
    /// <para>注册靠 <c>NetworkManager.AddAllMessageHandlers()</c> 的程序集扫描：
    /// 实现 <c>IMessageHandler</c> 的具体类会被自动接上，不需要在别处再登记一次
    /// （登记两处迟早会漏一处，那时的症状是"服务端回了但客户端毫无反应"）。</para>
    /// </summary>
    public class PlanetRegionTransferResponseHandler : BaseMessageHandler
    {
        public override List<MessageType> MessageTypes { get; } =
            new List<MessageType> { MessageType.PlanetRegionTransferResult };

        public override ServiceType ServiceType => ServiceType.Game;

        public PlanetRegionTransferResponseHandler() : base(MessageType.PlanetRegionTransferResult)
        {
        }

        public override Task HandleAsync(HorizonMessagePacket message)
        {
            if (message?.Body is PlanetRegionTransferResult result)
            {
                PlanetRegionTransfers.OnReceipt(result);
                return Task.CompletedTask;
            }

            // 包体类型不对：这条被路由到了错误的处理器上。宁可吵一句，也不要静默丢掉一次交接
            Debug.LogWarning(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "[planet] 迁移回执包体类型不符，已丢弃。header={0} body={1}",
                message?.Header.MessageType, message?.Body?.GetType().Name ?? "null"));
            return Task.CompletedTask;
        }
    }
}
