using Horizon.Core.Abstract;
using Horizon.Entities;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using NetOrderStatus = Horizon.Game.Message.Network.OrderStatus;
using Horizon.Model.Flower;
using Horizon.Orleans.Interface;
using Microsoft.Extensions.Logging;
using Orleans;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Horizon.Orleans.Grains
{
    public class FlowerLogisticsMonitorGrain : Grain, IGrainWithIntegerKey
    {
        private readonly ILogger<FlowerLogisticsMonitorGrain> _logger;
        private readonly IDataContext<FlowerEntityContext, FlowerOrder, long> _orderContext;
        private readonly IDataContext<FlowerEntityContext, FlowerLogisticsTrack, long> _trackContext;

        public FlowerLogisticsMonitorGrain(
            ILogger<FlowerLogisticsMonitorGrain> logger,
            IDataContext<FlowerEntityContext, FlowerOrder, long> orderContext,
            IDataContext<FlowerEntityContext, FlowerLogisticsTrack, long> trackContext)
        {
            _logger = logger;
            _orderContext = orderContext;
            _trackContext = trackContext;
        }

        public override async Task OnActivateAsync(CancellationToken cancellationToken)
        {
            RegisterTimer(async _ => await MonitorAsync(), null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30));
            await base.OnActivateAsync(cancellationToken);
        }

        public async Task MonitorAsync()
        {
            _logger.LogInformation("物流监控检查触发: {Time}", DateTime.Now);
            try
            {
                var shippedOrders = await _orderContext.QueryAsync(o => o.Status == (int)NetOrderStatus.Shipped && o.IsValid);

                foreach (var order in shippedOrders)
                {
                    if (string.IsNullOrEmpty(order.ExpressCompanyName) || string.IsNullOrEmpty(order.ShipOrderNumber))
                        continue;

                    var logisticsGrain = GrainFactory.GetGrain<ILogisticsGrain>(order.Id);
                    await logisticsGrain.CheckAndUpdateTrackAsync(order.Id);
                }

                var now = DateTime.Now;
                var abnormalTracks = await _trackContext.QueryAsync(
                    t => t.LogisticsStatus != (int)LogisticsStatus.Signed && t.LastQueriedAt.HasValue);

                foreach (var track in abnormalTracks)
                {
                    if ((now - track.LastQueriedAt.Value).TotalHours > 48)
                    {
                        track.LogisticsStatus = (int)LogisticsStatus.Abnormal;
                        await _trackContext.UpdateAsync(track, track.Id);
                        _logger.LogWarning("物流异常: OrderId={OrderId}, Company={Company}, Number={Number}, 超过48小时无更新",
                            track.OrderId, track.ExpressCompanyName, track.ShipOrderNumber);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "物流监控检查执行失败");
            }
        }
    }
}
