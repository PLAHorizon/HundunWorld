using Horizon.Game.Message.Network;
using Horizon.Orleans.Interface;
using MemoryPack;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Horizon.Game.Gateway.Services
{
    /// <summary>
    /// KIFA拍卖数据抓取后台服务。
    /// 定期（每5分钟）从KIFA API获取拍卖数据。
    /// 当前使用模拟数据实现，待接入真实API后替换。
    /// </summary>
    public class KifaMarketDataFetcher : BackgroundService
    {
        private readonly ILogger<KifaMarketDataFetcher> _logger;
        private readonly IClusterClient _clusterClient;

        private static readonly TimeSpan FetchInterval = TimeSpan.FromMinutes(5);
        private const int KifaMarketId = 1;

        private static readonly (int SpeciesId, decimal BasePrice)[] FlowerSpecies =
        {
            (1001, 2.50m), (1002, 3.80m), (1003, 1.20m), (1004, 5.60m), (1005, 4.20m),
            (1006, 8.00m), (1007, 6.50m), (1008, 3.00m), (1009, 2.00m), (1010, 7.50m)
        };

        public KifaMarketDataFetcher(
            ILogger<KifaMarketDataFetcher> logger,
            IClusterClient clusterClient)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _clusterClient = clusterClient ?? throw new ArgumentNullException(nameof(clusterClient));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("KIFA拍卖数据抓取服务启动，间隔: {Interval}分钟", FetchInterval.TotalMinutes);

            // 等待 Orleans Silo 就绪：客户端 GetGrain<T>() 需要从 Silo 获取 grain 类型映射，
            // Silo 未启动时会抛出 ArgumentException "Could not find an implementation for interface"。
            // 这里重试直到 Silo 可用，避免启动瞬间的批量失败日志。
            await WaitForSiloReadyAsync(stoppingToken).ConfigureAwait(false);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await FetchAndForwardAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "KIFA拍卖数据抓取失败");
                }

                try
                {
                    await Task.Delay(FetchInterval, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        /// <summary>
        /// 等待 Orleans Silo 就绪，确保 GetGrain&lt;T&gt;() 能成功解析 grain 接口。
        /// Silo 未启动时客户端无法获取 grain 类型映射，会抛出 ArgumentException。
        /// </summary>
        private async Task WaitForSiloReadyAsync(CancellationToken stoppingToken)
        {
            const int maxAttempts = 60;
            const int retryDelayMs = 2000;

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                if (stoppingToken.IsCancellationRequested) return;

                try
                {
                    _ = _clusterClient.GetGrain<IFlowerMarketGrain>(0);
                    if (attempt > 1)
                    {
                        _logger.LogInformation("Orleans Silo 已就绪（等待 {Attempt} 次后成功）", attempt);
                    }
                    return;
                }
                catch (ArgumentException)
                {
                    _logger.LogDebug("等待 Orleans Silo 就绪（尝试 {Attempt}/{Max}）", attempt, maxAttempts);
                    try
                    {
                        await Task.Delay(retryDelayMs, stoppingToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                }
            }

            _logger.LogWarning("等待 Orleans Silo 就绪超时（{Max} 次），服务继续启动但可能无法转发数据", maxAttempts);
        }

        private async Task FetchAndForwardAsync()
        {
            _logger.LogInformation("开始抓取KIFA拍卖数据（模拟）");

            var random = new Random();

            foreach (var (speciesId, basePrice) in FlowerSpecies)
            {
                var snapshot = GenerateMockSnapshot(random, speciesId, basePrice);

                try
                {
                    var marketGrain = _clusterClient.GetGrain<IFlowerMarketGrain>(KifaMarketId);
                    var dataPoolGrain = _clusterClient.GetGrain<IFlowerDataPoolGrain>(0);
                    var dataPoolEntry = new DataPoolEntry
                    {
                        DataType = DataPoolDataType.MarketSnapshot,
                        DataSource = KifaMarketId,
                        RawPayload = Convert.ToBase64String(MemoryPackSerializer.Serialize(snapshot)),
                        Timestamp = snapshot.SnapshotTime,
                        ModelVersion = "",
                        Confidence = null
                    };

                    // 修复 BUG（Silo 重启时瞬时失败）：
                    // 原实现无重试逻辑，Silo 重启时 grain 调用抛出 OrleansMessageRejectionException
                    // （"The target silo is no longer active"），导致整批数据丢失。
                    // 修复：包装 grain 调用，遇到 OrleansMessageRejectionException 时短暂延迟后重试。
                    await ExecuteGrainCallWithRetryAsync(async () =>
                    {
                        await marketGrain.UpdateSnapshotAsync(snapshot);
                        await dataPoolGrain.WriteAsync(dataPoolEntry);
                    }, $"SpeciesId={speciesId}");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "转发KIFA拍卖数据失败: SpeciesId={SpeciesId}", speciesId);
                }
            }

            _logger.LogInformation("KIFA拍卖数据抓取完成，共 {Count} 个品种", FlowerSpecies.Length);
        }

        /// <summary>
        /// 执行 Grain 调用并在 Silo 重启/不可达时自动重试。<br/>
        /// 修复 BUG（Silo 重启时瞬时失败）：
        /// Silo 重启过程中，grain 调用会抛出 <see cref="OrleansMessageRejectionException"/>，
        /// 提示 "The target silo is no longer active"。这是瞬时故障，短暂等待后重试即可成功。<br/>
        /// 修复 BUG（Silo 完全不可达时无重试）：
        /// 当 Silo 进程崩溃或网络中断时，grain 调用抛出 <c>ConnectionFailedException</c>
        /// （"Unable to connect to S..."），原实现未捕获此异常导致整批数据丢失。<br/>
        /// 重试策略：最多 3 次，指数退避（2s → 4s → 8s）。
        /// </summary>
        private async Task ExecuteGrainCallWithRetryAsync(Func<Task> action, string context)
        {
            const int maxRetries = 3;
            var retryDelay = TimeSpan.FromSeconds(2);

            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    await action();
                    return;
                }
                catch (Exception ex) when (attempt < maxRetries && IsTransientOrleansException(ex))
                {
                    _logger.LogWarning(
                        "Grain 调用失败（Silo 可能正在重启/不可达），{Delay} 秒后重试。Context={Context}, Attempt={Attempt}/{Max}, Error={Error}",
                        retryDelay.TotalSeconds, context, attempt + 1, maxRetries, ex.Message);
                    await Task.Delay(retryDelay).ConfigureAwait(false);
                    retryDelay = TimeSpan.FromTicks(retryDelay.Ticks * 2);
                }
            }
        }

        /// <summary>
        /// 判断异常是否为 Orleans 瞬时故障（可重试）。
        /// 覆盖：Silo 重启（OrleansMessageRejectionException）、Silo 不可达（ConnectionFailedException）、
        /// 以及它们的内部异常包装形式。
        /// </summary>
        private static bool IsTransientOrleansException(Exception ex)
        {
            if (ex is OrleansMessageRejectionException) return true;
            if (ex is global::Orleans.Runtime.Messaging.ConnectionFailedException) return true;
            // 检查内部异常（Orleans 有时将瞬时故障包装在 OrleansException 中）
            if (ex is OrleansException && ex.InnerException is not null)
                return IsTransientOrleansException(ex.InnerException);
            return false;
        }

        private static FlowerPriceSnapshot GenerateMockSnapshot(Random random, int speciesId, decimal basePrice)
        {
            var fluctuation = (decimal)(random.NextDouble() * 0.4 - 0.2);
            var avgPrice = Math.Round(basePrice * (1 + fluctuation), 2);
            var minPrice = Math.Round(avgPrice * (decimal)(0.7 + random.NextDouble() * 0.1), 2);
            var maxPrice = Math.Round(avgPrice * (decimal)(1.1 + random.NextDouble() * 0.2), 2);
            var volume = random.Next(500, 5000);
            var tradeCount = random.Next(50, 500);

            return new FlowerPriceSnapshot
            {
                SpeciesId = speciesId,
                MarketId = KifaMarketId,
                AvgPrice = avgPrice,
                MinPrice = minPrice,
                MaxPrice = maxPrice,
                Volume = volume,
                TradeCount = tradeCount,
                SnapshotTime = DateTime.UtcNow
            };
        }
    }
}
