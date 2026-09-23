using System;
using System.Threading;
using System.Threading.Tasks;
using Horizon.Game.Core.Observability;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Horizon.Orleans.Silo.Services
{
    /// <summary>
    /// 阶段 6.3 P2 — 修正计数发布服务：Silo 进程内周期把 <c>horizon.sync.corrections</c> 仪表增量
    /// 批量上送 Redis（<see cref="ICorrectionCounterStore"/>），供网关自动回滚门的「修正率」信号
    /// 跨进程汇聚（Silo→Redis→网关）。发布间隔远小于网关采样窗口（默认 30s），保证网关每窗口读到新鲜增量。
    /// </summary>
    public sealed class CorrectionPublisherHostedService : BackgroundService
    {
        private readonly ICorrectionCounterStore _store;
        private readonly ILogger<CorrectionPublisherHostedService> _logger;
        private readonly TimeSpan _interval;

        public CorrectionPublisherHostedService(
            ICorrectionCounterStore store,
            ILogger<CorrectionPublisherHostedService> logger,
            IConfiguration configuration)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            var seconds = configuration.GetValue("CorrectionsPublish:IntervalSeconds", 5);
            _interval = TimeSpan.FromSeconds(Math.Max(1, seconds));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var publisher = new CorrectionPublisher(_store, _logger);
            publisher.Start();
            _logger.LogInformation("CorrectionPublisherHostedService：启动（修正计数跨进程汇聚，发布间隔 {Interval}）。", _interval);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(_interval, stoppingToken);
                    await publisher.FlushAsync();
                }
                catch (OperationCanceledException)
                {
                    break; // 正常退出
                }
                catch (Exception ex)
                {
                    // 上送失败已把增量加回累计（CorrectionPublisher.FlushAsync），下轮重试；此处仅记录。
                    _logger.LogError(ex, "CorrectionPublisherHostedService：上送修正计数失败（下轮重试）。");
                }
            }

            // 退出前最后冲刷一次（尽力而为；容器停止时 Redis 通常仍可用）。
            try
            {
                await publisher.FlushAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "CorrectionPublisherHostedService：退出前最后冲刷失败（未上送量随进程终止丢弃）。");
            }

            _logger.LogInformation("CorrectionPublisherHostedService：退出。");
        }
    }
}
