using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Horizon.WebApi.Services
{
    /// <summary>
    /// 花卉品种目录 Redis 缓存定时刷新服务。
    ///
    /// 与 Gateway DounanMarketDataFetcher 的 30 分钟抓取节奏对齐：
    ///   - 宿主启动即刷新一次（best-effort，不阻塞启动）；
    ///   - 之后每 30 分钟从「平台目录 + 斗南公开行情」重建并落库 Redis。
    ///
    /// 这样「商家->商品发布」模块使用的花卉品种数据，既来自昆明斗南等大型交易市场的公开行情，
    /// 又通过 Redis 缓存 + 定时更新保持新鲜，Redis/行情不可用时自动降级为平台目录兜底。
    /// </summary>
    public class FlowerSpeciesCatalogRefreshService : BackgroundService
    {
        private readonly IFlowerSpeciesCatalogService _catalogService;
        private readonly ILogger<FlowerSpeciesCatalogRefreshService> _logger;

        /// <summary>刷新间隔：与 Gateway DounanMarketDataFetcher 抓取间隔一致。</summary>
        private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(30);

        public FlowerSpeciesCatalogRefreshService(
            IFlowerSpeciesCatalogService catalogService,
            ILogger<FlowerSpeciesCatalogRefreshService> logger)
        {
            _catalogService = catalogService ?? throw new ArgumentNullException(nameof(catalogService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("花卉品种目录 Redis 缓存定时刷新服务启动，间隔 {Minutes} 分钟", RefreshInterval.TotalMinutes);

            // 启动即刷新一次（best-effort，避免阻塞宿主启动）
            try
            {
                await _catalogService.RefreshAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "花卉品种目录初始刷新失败（将在下一周期重试）");
            }

            using var timer = new PeriodicTimer(RefreshInterval);
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await _catalogService.RefreshAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "花卉品种目录定时刷新失败（下一周期重试）");
                }
            }
        }
    }
}
