using Horizon.Game.Message.Network;
using Horizon.Orleans.Interface;
using MemoryPack;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orleans;
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
                    await marketGrain.UpdateSnapshotAsync(snapshot);

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
                    await dataPoolGrain.WriteAsync(dataPoolEntry);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "转发KIFA拍卖数据失败: SpeciesId={SpeciesId}", speciesId);
                }
            }

            _logger.LogInformation("KIFA拍卖数据抓取完成，共 {Count} 个品种", FlowerSpecies.Length);
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
