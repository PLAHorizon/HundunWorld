using System;
using System.Threading;
using System.Threading.Tasks;
using Horizon.Orleans.Interface;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;

namespace Horizon.Orleans.Silo.Services
{
    /// <summary>
    /// 订单超时处理启动服务
    /// 在 Silo 启动时激活订单超时相关的定时任务 Grain
    /// </summary>
    public class OrderTimeoutStartupService : IHostedService
    {
        private readonly IGrainFactory _grainFactory;
        private readonly ILogger<OrderTimeoutStartupService> _logger;

        public OrderTimeoutStartupService(
            IGrainFactory grainFactory,
            ILogger<OrderTimeoutStartupService> logger)
        {
            _grainFactory = grainFactory;
            _logger = logger;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("开始激活订单超时定时任务...");

            try
            {
                // 激活订单超时扫描 Grain（负责取消超时未支付订单）
                // 使用 key=1 激活 FlowerOrderTimeoutGrain，OnActivateAsync 会注册定时器。
                // 修复 BUG：GetGrain<T> 的 T 必须是 Grain 接口——原实现传入实现类
                // FlowerOrderTimeoutGrain/SchedulerGrain，运行时报
                // "Argument type must be an interface"，导致两个订单超时任务从未激活。
                var timeoutGrain = _grainFactory.GetGrain<IFlowerOrderTimeoutGrain>(1);
                // 调用一次方法确保 Grain 被激活并注册定时器
                _ = timeoutGrain.ScanAndCancelExpiredOrdersAsync().ConfigureAwait(false);
                _logger.LogInformation("FlowerOrderTimeoutGrain 已激活，将每5分钟扫描一次超时未支付订单");

                // 激活订单超时调度 Grain（负责催发货、自动确认收货、自动完成订单）
                // 使用 key=1 激活 FlowerOrderTimeoutSchedulerGrain
                var schedulerGrain = _grainFactory.GetGrain<IFlowerOrderTimeoutSchedulerGrain>(1);
                // 调用一次方法确保 Grain 被激活并注册定时器
                _ = schedulerGrain.CheckTimeoutAsync().ConfigureAwait(false);
                _logger.LogInformation("FlowerOrderTimeoutSchedulerGrain 已激活，将每30分钟检查一次订单超时");

                _logger.LogInformation("订单超时定时任务激活完成");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "激活订单超时定时任务失败");
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
