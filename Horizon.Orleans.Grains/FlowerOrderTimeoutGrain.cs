using Horizon.Core.Abstract;
using Horizon.Entities;
using Horizon.Game.Message.Network;
using Horizon.Model.Flower;
using Horizon.Orleans.Interface;
using Microsoft.Extensions.Logging;
using Orleans;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Horizon.Orleans.Grains
{
    public class FlowerOrderTimeoutGrain : Grain, IGrainWithIntegerKey
    {
        private readonly ILogger<FlowerOrderTimeoutGrain> _logger;
        private readonly IDataContext<FlowerEntityContext, FlowerOrder, long> _orderContext;
        private readonly IGrainFactory _grainFactory;

        public FlowerOrderTimeoutGrain(
            ILogger<FlowerOrderTimeoutGrain> logger,
            IDataContext<FlowerEntityContext, FlowerOrder, long> orderContext,
            IGrainFactory grainFactory)
        {
            _logger = logger;
            _orderContext = orderContext;
            _grainFactory = grainFactory;
        }

        public override async Task OnActivateAsync(CancellationToken cancellationToken)
        {
            await base.OnActivateAsync(cancellationToken);

            RegisterTimer(async _ => await ScanAndCancelExpiredOrdersAsync(),
                null,
                TimeSpan.FromMinutes(5),
                TimeSpan.FromMinutes(5));
        }

        public async Task<int> ScanAndCancelExpiredOrdersAsync()
        {
            try
            {
                var cutoff = DateTime.Now.AddMinutes(-30);
                var pendingOrders = await _orderContext.QueryAsync(
                    o => o.Status == (int)OrderStatus.Pending && o.CreateTime < cutoff);

                var expiredOrders = pendingOrders.ToList();
                var cancelled = 0;

                foreach (var order in expiredOrders)
                {
                    try
                    {
                        var orderGrain = _grainFactory.GetGrain<IOrderGrain>(order.Id);
                        var success = await orderGrain.CancelOrderAsync("超时未支付，系统自动取消");
                        if (success) cancelled++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "超时取消订单失败: OrderId={OrderId}", order.Id);
                    }
                }

                if (cancelled > 0)
                {
                    _logger.LogInformation("超时订单自动取消完成: 已取消 {Count} 笔订单", cancelled);
                }

                return cancelled;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "扫描超时订单失败");
                return 0;
            }
        }
    }
}
