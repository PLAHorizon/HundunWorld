using Horizon.Orleans.Interface;
using Microsoft.Extensions.Logging;
using Orleans;
using System;
using System.Threading.Tasks;

namespace Horizon.Orleans.Grains
{
    public class FlowerSettlementSchedulerGrain : Grain
    {
        private readonly ILogger<FlowerSettlementSchedulerGrain> _logger;

        public FlowerSettlementSchedulerGrain(ILogger<FlowerSettlementSchedulerGrain> logger)
        {
            _logger = logger;
        }

        public override async Task OnActivateAsync(CancellationToken cancellationToken)
        {
            RegisterTimer(async _ => await OnTimerTick(), null, TimeSpan.FromMinutes(60), TimeSpan.FromMinutes(60));
            await base.OnActivateAsync(cancellationToken);
        }

        private async Task OnTimerTick()
        {
            _logger.LogInformation("结算调度触发: {Time}", DateTime.Now);
            try
            {
                var now = DateTime.Now;
                if (now.DayOfWeek == DayOfWeek.Monday && now.Hour == 2)
                {
                    var billingGrain = GrainFactory.GetGrain<IShopBillingGrain>(0);
                    _logger.LogInformation("执行每周结算调度");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "结算调度执行失败");
            }
        }
    }
}
