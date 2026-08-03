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

                    // 修复 BUG（Silo 重启时瞬时失败）：
                    // 原实现无重试逻辑，Silo 重启时 grain 调用抛出 OrleansMessageRejectionException
                    // （"The target silo is no longer active"），导致数据写入失败。
                    // 修复：包装 grain 调用，遇到 OrleansMessageRejectionException 时短暂延迟后重试。
                    await ExecuteGrainCallWithRetryAsync(
                        () => dataPoolGrain.WriteAsync(dataPoolEntry),
                        $"Region={region}");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "写入天气数据到DataPool失败: Region={Region}", region);
                }
            }

            _logger.LogInformation("花卉产区天气数据抓取完成，共 {Count} 个产区", ProductionRegions.Length);
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
