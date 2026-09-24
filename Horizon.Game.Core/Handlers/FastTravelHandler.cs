using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Horizon.Game.Core.World;
using Horizon.Game.Message;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using Horizon.Orleans.Interface;
using Horizon.Orleans.Interface.World;
using Microsoft.Extensions.Logging;
using Orleans;
using TouchSocket.Sockets;

namespace Horizon.Game.Core.Handlers
{
    /// <summary>
    /// 快速旅行（地图传送点）上行请求处理器。
    /// <para>
    /// 客户端只做本地预测；真正的权威位置由本处理器转发给 <see cref="IZoneShardGrain"/> 应用。
    /// 若不这样做，服务端 <c>MovementValidator</c> 会因位移超过 <c>TeleportDistanceThreshold</c>
    /// 判定为 <c>TeleportDetected</c> 并下发 CorrectionPacket 把玩家拉回。
    /// </para>
    /// <para>
    /// 位置变更通过既有快照通道下发（<c>_forceFullSnapshotNextTick</c>），因此成功时不需要专用响应包。
    /// </para>
    /// </summary>
    public class FastTravelHandler : MessageHandlerBase
    {
        private readonly IShardRouter _shardRouter;

        /// <summary>
        /// 自行保存注入的集群客户端：基类 <see cref="MessageHandlerBase"/> 的 ClusterClient 为私有，
        /// 派生类不可访问。
        /// </summary>
        private readonly IClusterClient _clusterClient;

        public FastTravelHandler(
            ILogger<MessageHandlerBase> logger,
            IClusterClient clusterClient,
            HorizonMessageAdapter adapter,
            IShardRouter shardRouter) : base(logger, clusterClient, adapter)
        {
            _clusterClient = clusterClient;
            _shardRouter = shardRouter;
        }

        public override List<MessageType> MessageTypes { get; } = new List<MessageType>
        {
            MessageType.FastTravelRequest,
        };

        public override ServiceType ServiceType => ServiceType.Game;

        public override async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> RouteHandlerAsync(HorizonMessagePacket message)
        {
            if (message.Body is not FastTravelRequestMessage request || request.EntityId == 0)
            {
                Logger.LogWarning("快速旅行请求非法（包体类型不匹配或缺少 EntityId），已拒绝");
                return (false, CreateHorizonMessage(new ErrorMessage { ErrorCode = 400, Message = "无效的快速旅行请求" }));
            }

            try
            {
                var shardId = _shardRouter.Resolve((long)request.EntityId);
                var zoneShard = _clusterClient.GetGrain<IZoneShardGrain>(shardId);

                var applied = await zoneShard.RequestFastTravelAsync(
                    request.EntityId, request.X, request.Y, request.Z, request.Yaw);

                if (!applied)
                {
                    Logger.LogWarning(
                        "快速旅行被拒绝：分片内不存在该实体。EntityId={EntityId}, ShardId={ShardId}",
                        request.EntityId, shardId);
                    return (false, CreateHorizonMessage(new ErrorMessage { ErrorCode = 404, Message = "传送目标实体不存在" }));
                }

                Logger.LogInformation(
                    "快速旅行已提交服务端权威应用。EntityId={EntityId}, Poi={Poi}, Pos=({X:F1},{Y:F1},{Z:F1}), ShardId={ShardId}",
                    request.EntityId, request.PoiTag, request.X, request.Y, request.Z, shardId);

                // 返回 null 表示不回包（位置由下一次全量快照下发）
                return (true, null!);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "快速旅行处理失败。EntityId={EntityId}, Poi={Poi}", request.EntityId, request.PoiTag);
                return (false, CreateHorizonMessage(new ErrorMessage { ErrorCode = 500, Message = "快速旅行处理失败" }));
            }
        }
    }
}
