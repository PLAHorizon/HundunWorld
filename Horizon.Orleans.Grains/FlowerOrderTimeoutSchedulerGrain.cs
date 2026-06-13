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
    public class FlowerOrderTimeoutSchedulerGrain : Grain, IGrainWithIntegerKey
    {
        private readonly ILogger<FlowerOrderTimeoutSchedulerGrain> _logger;
        private readonly IDataContext<FlowerEntityContext, FlowerOrder, long> _orderContext;

        public FlowerOrderTimeoutSchedulerGrain(
            ILogger<FlowerOrderTimeoutSchedulerGrain> logger,
            IDataContext<FlowerEntityContext, FlowerOrder, long> orderContext)
        {
            _logger = logger;
            _orderContext = orderContext;
        }

        public override async Task OnActivateAsync(CancellationToken cancellationToken)
        {
            RegisterTimer(async _ => await CheckTimeoutAsync(), null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30));
            await base.OnActivateAsync(cancellationToken);
        }

        public async Task CheckTimeoutAsync()
        {
            _logger.LogInformation("订单超时检查触发: {Time}", DateTime.Now);
            try
            {
                var now = DateTime.Now;
                var orders = await _orderContext.QueryAsync(o => o.IsValid);

                var paidTimeout = orders.Where(o => o.Status == (int)OrderStatus.Paid && o.PaymentTime.HasValue && (now - o.PaymentTime.Value).TotalHours > 24);
                foreach (var order in paidTimeout)
                {
                    _logger.LogWarning("催发货: OrderId={OrderId}, 已付款{Hours}小时未发货", order.Id, (int)(now - order.PaymentTime.Value).TotalHours);
                }

                var shippedTimeout = orders.Where(o => o.Status == (int)OrderStatus.Shipped && o.ShippingDate.HasValue && (now - o.ShippingDate.Value).TotalDays > 7);
                foreach (var order in shippedTimeout)
                {
                    var orderGrain = GrainFactory.GetGrain<IOrderGrain>(order.Id);
                    await orderGrain.DeliverOrderAsync();
                    _logger.LogInformation("自动确认收货: OrderId={OrderId}", order.Id);
                }

                var deliveredTimeout = orders.Where(o => o.Status == (int)OrderStatus.Delivered && o.DeliveredAt.HasValue && (now - o.DeliveredAt.Value).TotalDays > 7);
                foreach (var order in deliveredTimeout)
                {
                    var orderGrain = GrainFactory.GetGrain<IOrderGrain>(order.Id);
                    await orderGrain.CompleteOrderAsync();
                    _logger.LogInformation("自动完成订单: OrderId={OrderId}", order.Id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "订单超时检查执行失败");
            }
        }
    }
}
