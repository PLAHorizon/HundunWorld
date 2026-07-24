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
    /// 花卉产区天气数据抓取后台服务。
    /// 定期（每30分钟）获取花卉产区天气数据。
    /// 当前使用模拟数据实现，待接入真实天气API后替换。
    /// </summary>
    public class FlowerWeatherFetcher : BackgroundService
    {
        private readonly ILogger<FlowerWeatherFetcher> _logger;
        private readonly IClusterClient _clusterClient;

        private static readonly TimeSpan FetchInterval = TimeSpan.FromMinutes(30);

        private static readonly string[] ProductionRegions =
        {
            "昆明", "斗南", "玉溪", "楚雄", "大理"
        };

        public FlowerWeatherFetcher(
            ILogger<FlowerWeatherFetcher> logger,
            IClusterClient clusterClient)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _clusterClient = clusterClient ?? throw new ArgumentNullException(nameof(clusterClient));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("花卉产区天气数据抓取服务启动，间隔: {Interval}分钟", FetchInterval.TotalMinutes);

            // 等待 Orleans Silo 就绪：客户端 GetGrain<T>() 需要从 Silo 获取 grain 类型映射，
            // Silo 未启动时会抛出 ArgumentException "Could not find an implementation for interface"。
            // 这里重试直到 Silo 可用，避免启动瞬间的批量失败日志。
            await WaitForSiloReadyAsync(stoppingToken).ConfigureAwait(false);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await FetchAndWriteAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "花卉产区天气数据抓取失败");
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
                    _ = _clusterClient.GetGrain<IFlowerDataPoolGrain>(0);
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

            _logger.LogWarning("等待 Orleans Silo 就绪超时（{Max} 次），服务继续启动但可能无法写入数据", maxAttempts);
        }

        private async Task FetchAndWriteAsync()
        {
            _logger.LogInformation("开始抓取花卉产区天气数据（模拟）");

            var random = new Random();

            foreach (var region in ProductionRegions)
            {
                var weatherData = GenerateMockWeatherData(random, region);

                try
                {
                    var dataPoolGrain = _clusterClient.GetGrain<IFlowerDataPoolGrain>(0);
                    var dataPoolEntry = new DataPoolEntry
                    {
                        DataType = DataPoolDataType.WeatherData,
                        DataSource = 0,
                        RawPayload = Convert.ToBase64String(MemoryPackSerializer.Serialize(weatherData)),
                        Timestamp = weatherData.ObservationTime,
                        ModelVersion = "",
                        Confidence = null
                    };
                    await dataPoolGrain.WriteAsync(dataPoolEntry);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "写入天气数据到DataPool失败: Region={Region}", region);
                }
            }

            _logger.LogInformation("花卉产区天气数据抓取完成，共 {Count} 个产区", ProductionRegions.Length);
        }

        private static WeatherData GenerateMockWeatherData(Random random, string region)
        {
            return new WeatherData
            {
                Region = region,
                Temperature = Math.Round(random.NextDouble() * 25 + 5, 1),
                Humidity = Math.Round(random.NextDouble() * 50 + 40, 1),
                Rainfall = Math.Round(random.NextDouble() * 30, 1),
                SunlightHours = Math.Round(random.NextDouble() * 12, 1),
                WindSpeed = Math.Round(random.NextDouble() * 8, 1),
                ObservationTime = DateTime.UtcNow
            };
        }
    }

    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class WeatherData
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public string Region { get; set; } = "";

        [MemoryPackOrder(1)]
        [Id(1)]
        public double Temperature { get; set; }

        [MemoryPackOrder(2)]
        [Id(2)]
        public double Humidity { get; set; }

        [MemoryPackOrder(3)]
        [Id(3)]
        public double Rainfall { get; set; }

        [MemoryPackOrder(4)]
        [Id(4)]
        public double SunlightHours { get; set; }

        [MemoryPackOrder(5)]
        [Id(5)]
        public double WindSpeed { get; set; }

        [MemoryPackOrder(6)]
        [Id(6)]
        public DateTime ObservationTime { get; set; }
    }
}
