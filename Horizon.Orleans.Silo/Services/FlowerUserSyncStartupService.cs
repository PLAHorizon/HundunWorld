using System;
using System.Threading;
using System.Threading.Tasks;
using Horizon.Orleans.Grains;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Horizon.Orleans.Silo.Services
{
    public class FlowerUserSyncStartupService : IHostedService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<FlowerUserSyncStartupService> _logger;

        public FlowerUserSyncStartupService(
            IServiceProvider serviceProvider,
            ILogger<FlowerUserSyncStartupService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("开始检查花卉用户数据同步状态...");

            try
            {
                using var scope = _serviceProvider.CreateScope();
                var syncService = scope.ServiceProvider.GetService(typeof(FlowerUserDataSyncService)) as FlowerUserDataSyncService;

                if (syncService == null)
                {
                    _logger.LogWarning("FlowerUserDataSyncService 未注册，跳过用户同步");
                    return;
                }

                var flowerCount = await syncService.GetFlowerUserCountAsync();
                var basicCount = await syncService.GetBasicUserCountAsync();

                _logger.LogInformation("用户数据状态 - Basic: {BasicCount} 个用户, Flower: {FlowerCount} 个用户", basicCount, flowerCount);

                if (flowerCount == 0 && basicCount > 0)
                {
                    _logger.LogInformation("Flower_User 表为空，开始从 Basic 数据库同步用户...");
                    var synced = await syncService.SyncUsersAsync();
                    _logger.LogInformation("用户同步完成，共同步 {Count} 个用户", synced);
                }
                else if (flowerCount < basicCount)
                {
                    _logger.LogInformation("Flower_User 表用户数({FlowerCount})少于 Basic({BasicCount})，执行增量同步...", flowerCount, basicCount);
                    var synced = await syncService.SyncUsersAsync();
                    _logger.LogInformation("增量同步完成，共同步 {Count} 个用户", synced);
                }
                else
                {
                    _logger.LogInformation("Flower_User 表已包含 {Count} 个用户，无需同步", flowerCount);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "花卉用户数据同步失败，系统将继续启动");
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
