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
    /// 区域快照回执处理器。<b>只做路由</b>：什么时候该再问、拿到之后加载什么/放下什么，
    /// 全在 <c>PlanetRegionWatchRules</c>（那边有离线判据），这一层没有。
    /// <para>注册靠 <c>NetworkManager.AddAllMessageHandlers()</c> 的程序集扫描，不再在别处登记一次
    /// ——登记两处的下场是"服务端回了但客户端毫无反应"。</para>
    /// </summary>
    public class PlanetRegionSnapshotResponseHandler : BaseMessageHandler
    {
        public override List<MessageType> MessageTypes { get; } =
            new List<MessageType> { MessageType.PlanetRegionSnapshotResult };

        public override ServiceType ServiceType => ServiceType.Game;

        public PlanetRegionSnapshotResponseHandler() : base(MessageType.PlanetRegionSnapshotResult)
        {
        }

        public override Task HandleAsync(HorizonMessagePacket message)
        {
            if (message?.Body is PlanetRegionSnapshotResponse response)
            {
                PlanetRegionWatches.OnResponse(response);
                return Task.CompletedTask;
            }

            Debug.LogWarning(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "[planet] 快照回执包体类型不符，已丢弃。header={0} body={1}",
                message?.Header.MessageType, message?.Body?.GetType().Name ?? "null"));
            return Task.CompletedTask;
        }
    }
}
