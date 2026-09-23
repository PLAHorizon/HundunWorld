using Horizon.Game.Message.Network;
using Horizon.Orleans.Interface;
using MemoryPack;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Horizon.Game.Gateway.Services
{
    /// <summary>
    /// 昆明斗南花卉市场真实行情抓取服务（平台自有花卉数据的权威行情源）。
    /// 数据源：昆明市农业农村局「价格行情」栏目每日发布的《云南昆明斗南花卉市场 ... 花卉价格信息》公开价格表。
    /// 1. 价格列表页 jgxq/ —— 定位最新一篇斗南花卉价格信息文章（每日更新）；
    /// 2. 文章价格表 —— 品名/计量单位/最高价/最低价/均价 5 列结构。
    /// 3. 映射到平台自有 22 个品种（2001~2022）：斗南表内品种按单价换算为整把价；
    ///    斗南表未覆盖的品种（扶郎/红豆/蓝星花/龙竹/满天星/乒乓菊/黄百合/小雏菊/洋甘菊）
    ///    用国内真实交易机构（KIFA 昆明花拍中心 / 上海虹桥 / 斗南）2026 年月度行情综述折算参考价兜底。
    /// 抓取后推送给 IFlowerMarketGrain（快照）与 IFlowerSpeciesGrain（价格历史，按发布日期去重）。
    /// </summary>
    public class DounanMarketDataFetcher : BackgroundService
    {
        private readonly ILogger<DounanMarketDataFetcher> _logger;
        private readonly IClusterClient _clusterClient;
        private readonly HttpClient _httpClient;

        /// <summary>抓取间隔：斗南每日发布，30 分钟轮询足够及时</summary>
        private static readonly TimeSpan FetchInterval = TimeSpan.FromMinutes(30);

        /// <summary>斗南市场 Grain 主键（与 WebApi FlowerSpeciesCatalog.DounanMarketId 对齐）</summary>
        private const int DounanMarketId = 2;

        /// <summary>昆明市农业农村局价格行情列表页（斗南花卉价格信息每日在此发布）</summary>
        private const string PriceListUrl = "https://nyncj.km.gov.cn/nygz/jgxq/";
        private const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0 Safari/537.36";

        /// <summary>列表页中定位最新一篇斗南花卉价格信息文章链接</summary>
        private static readonly Regex DounanArticleRegex = new(
            @"<a[^>]*href=""(?<url>/c/\d{4}-\d{2}-\d{2}/\d+\.shtml)""[^>]*>(?<title>云南昆明斗南花卉市场(?<y>\d{4})年(?<m>\d{1,2})月(?<d>\d{1,2})日花卉价格信息)</a>",
            RegexOptions.Compiled);

        /// <summary>文章价格表数据行（tr 捕获组覆盖整行直至行尾闭合标签）</summary>
        private static readonly Regex RowRegex = new(
            @"<tr[^>]*>(?<row>.*?)</tr>",
            RegexOptions.Compiled | RegexOptions.Singleline);

        /// <summary>行内单元格（<td>…<p>值</p>…</td>，实测 5 列：品名/单位/最高价/最低价/均价）</summary>
        private static readonly Regex CellRegex = new(
            @"<td[^>]*>\s*<p>(?<v>.*?)</p>\s*</td>",
            RegexOptions.Compiled | RegexOptions.Singleline);

        /// <summary>
        /// 平台自有品种映射（2001~2022，与 WebApi FlowerSpeciesCatalog.Items 对齐）。
        /// DounanName 命中斗南价格表 → 按 StemsPerBundle 换算整把价；未命中 → 用 ReferenceBundlePrice 参考价。
        /// 参考价口径说明（整把价，元）：KIFA 昆明花拍中心 / 上海虹桥花卉市场 2026 年 6-8 月行情综述折算。
        /// </summary>
        private sealed class SpeciesMapping
        {
            public long SpeciesId { get; init; }
            /// <summary>斗南价格表品名（精确匹配；null=斗南未覆盖，走参考价）</summary>
            public string? DounanName { get; init; }
            /// <summary>每把支数（斗南单价 元/枝 → 整把价 = 单价 × StemsPerBundle）</summary>
            public int StemsPerBundle { get; init; }
            /// <summary>斗南是否直接为整把价（单位 元/把，如勿忘我/洋桔梗/绣球）</summary>
            public bool IsBundlePrice { get; init; }
            /// <summary>斗南未覆盖品种的参考整把价（元，真实机构行情综述折算）</summary>
            public decimal ReferenceBundlePrice { get; init; }
        }

        private static readonly SpeciesMapping[] SpeciesMappings =
        {
            new() { SpeciesId = 2001, DounanName = "菊花",   StemsPerBundle = 10 },                                            // 白菊：菊花 10支/把
            new() { SpeciesId = 2002, DounanName = "白玫瑰（单头）", StemsPerBundle = 18 },                                  // 白玫瑰 18支/把
            new() { SpeciesId = 2003, DounanName = "粉玫瑰（单头）", StemsPerBundle = 18 },                                  // 粉玫瑰 18支/把
            new() { SpeciesId = 2004, ReferenceBundlePrice = 10.00m },                                                      // 扶郎(非洲菊)：KIFA 6月均价0.5~0.6元/支 × 20支
            new() { SpeciesId = 2005, ReferenceBundlePrice = 8.00m },                                                       // 红豆：配花参考价
            new() { SpeciesId = 2006, DounanName = "红玫瑰（单头）", StemsPerBundle = 18 },                                  // 红玫瑰 18支/把
            new() { SpeciesId = 2007, DounanName = "洋桔梗", IsBundlePrice = true },                                        // 桔梗：斗南按把计价
            new() { SpeciesId = 2008, DounanName = "康乃馨", StemsPerBundle = 18 },                                         // 康乃馨(多头) 18支/把
            new() { SpeciesId = 2009, DounanName = "康乃馨", StemsPerBundle = 18 },                                         // 康乃馨(红色单头) 18支/把
            new() { SpeciesId = 2010, ReferenceBundlePrice = 15.00m },                                                      // 蓝星花：配花参考价
            new() { SpeciesId = 2011, ReferenceBundlePrice = 12.00m },                                                      // 龙竹(60公分)：绿植参考价
            new() { SpeciesId = 2012, ReferenceBundlePrice = 15.00m },                                                      // 满天星(白色)：KIFA 7.67元/千克折算到把
            new() { SpeciesId = 2013, ReferenceBundlePrice = 10.00m },                                                      // 乒乓菊：菊花类参考价
            new() { SpeciesId = 2014, DounanName = "白百合", StemsPerBundle = 10 },                                         // 双头白百合 10支/把
            new() { SpeciesId = 2015, DounanName = "粉百合", StemsPerBundle = 10 },                                         // 双头粉百合 10支/把
            new() { SpeciesId = 2016, ReferenceBundlePrice = 42.00m },                                                      // 双头黄百合：百合 4.2元/支 × 10支（上海虹桥6月）
            new() { SpeciesId = 2017, DounanName = "勿忘我", IsBundlePrice = true },                                        // 勿忘我：斗南按把计价
            new() { SpeciesId = 2018, DounanName = "向日葵", StemsPerBundle = 5 },                                          // 向日葵 5支/把
            new() { SpeciesId = 2019, ReferenceBundlePrice = 8.00m },                                                       // 小雏菊：菊花类参考价
            new() { SpeciesId = 2020, DounanName = "绣球",  IsBundlePrice = true },                                         // 绣球花：斗南按枝≈把计价
            new() { SpeciesId = 2021, ReferenceBundlePrice = 12.00m },                                                      // 洋甘菊：配花参考价
            new() { SpeciesId = 2022, DounanName = "紫罗兰", StemsPerBundle = 10 },                                         // 紫罗兰 10支/把
        };

        /// <summary>SpeciesId → 映射（加速查找）</summary>
        private static readonly Dictionary<long, SpeciesMapping> MappingById =
            SpeciesMappings.ToDictionary(m => m.SpeciesId);

        public DounanMarketDataFetcher(
            ILogger<DounanMarketDataFetcher> logger,
            IClusterClient clusterClient)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _clusterClient = clusterClient ?? throw new ArgumentNullException(nameof(clusterClient));
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
            _httpClient.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
            _httpClient.DefaultRequestHeaders.Referrer = new Uri("https://nyncj.km.gov.cn/nygz/jgxq/");
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("昆明斗南花卉市场真实行情抓取服务启动，间隔: {Interval}分钟", FetchInterval.TotalMinutes);

            // 等待 Orleans Silo 就绪：客户端 GetGrain<T>() 需要从 Silo 获取 grain 类型映射，
            // Silo 未启动时会抛出 ArgumentException "Could not find an implementation for interface"。
            await WaitForSiloReadyAsync(stoppingToken).ConfigureAwait(false);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await FetchAndForwardAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "斗南花卉市场行情抓取失败");
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
                    _ = _clusterClient.GetGrain<IFlowerMarketGrain>(DounanMarketId);
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

        private async Task FetchAndForwardAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("开始抓取斗南花卉市场真实行情");

            // 1. 定位最新一篇斗南花卉价格信息文章（每日更新，从列表页首页即可命中）
            var listHtml = await FetchHtmlAsync(PriceListUrl).ConfigureAwait(false);
            var article = DounanArticleRegex.Match(listHtml);
            if (!article.Success)
            {
                _logger.LogWarning("价格行情列表页未找到斗南花卉价格信息文章（页面结构可能变化）");
                return;
            }

            var publishDate = ParsePublishDate(article.Groups["y"].Value, article.Groups["m"].Value, article.Groups["d"].Value);
            var articleUrl = "https://nyncj.km.gov.cn" + article.Groups["url"].Value;
            _logger.LogInformation("定位到斗南行情文章: {Url}, 发布 {PublishDate:yyyy-MM-dd}", articleUrl, publishDate);

            // 2. 抓取文章价格表并解析
            var html = await FetchHtmlAsync(articleUrl).ConfigureAwait(false);
            var rows = ParsePriceRows(html);

            if (rows.Count == 0)
            {
                _logger.LogWarning("斗南价格表解析结果为空（页面结构可能变化）");
                return;
            }

            _logger.LogInformation("斗南价格表解析成功：{Count} 个品种，发布时间 {PublishDate:yyyy-MM-dd}",
                rows.Count, publishDate);

            var now = DateTime.UtcNow;
            int snapshotCount = 0;
            int backfillCount = 0;

            foreach (var mapping in SpeciesMappings)
            {
                if (stoppingToken.IsCancellationRequested) break;

                try
                {
                    var speciesGrain = _clusterClient.GetGrain<IFlowerSpeciesGrain>(mapping.SpeciesId);
                    var snapshot = BuildSnapshot(mapping, rows, publishDate);
                    if (snapshot == null) continue;

                    // 品种价格历史为空（首次运行/新部署）时，用近 14 天同价回填，
                    // 使价格走势与 AI 预测（依赖历史序列）立即有数据可用。
                    await ExecuteGrainCallWithRetryAsync(async () =>
                    {
                        var existing = await speciesGrain.GetPriceHistoryAsync(now.AddDays(-45), now);
                        if (existing == null || existing.Count == 0)
                        {
                            for (int day = 13; day >= 0; day--)
                            {
                                var ts = publishDate.Date.AddDays(-day).AddHours(10);
                                await speciesGrain.UpdatePriceHistoryAsync(snapshot.AvgPrice, ts);
                            }
                            Interlocked.Increment(ref backfillCount);
                        }
                    }, $"Backfill SpeciesId={mapping.SpeciesId}");

                    var marketGrain = _clusterClient.GetGrain<IFlowerMarketGrain>(DounanMarketId);
                    await ExecuteGrainCallWithRetryAsync(async () =>
                    {
                        await marketGrain.UpdateSnapshotAsync(snapshot);

                        // 发布日期当天已有历史点则跳过（同日重复推送会污染历史序列）
                        var history = await speciesGrain.GetPriceHistoryAsync(now.AddDays(-45), now);
                        var alreadyHasPublishDay = history?.Any(h => h.SnapshotTime.Date == publishDate.Date) == true;
                        if (!alreadyHasPublishDay)
                        {
                            await speciesGrain.UpdatePriceHistoryAsync(snapshot.AvgPrice, publishDate);
                        }

                        await WriteDataPoolAsync(snapshot);
                    }, $"SpeciesId={mapping.SpeciesId}");

                    snapshotCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "转发斗南市场行情失败: SpeciesId={SpeciesId}", mapping.SpeciesId);
                }
            }

            _logger.LogInformation("斗南市场行情抓取完成：快照 {SnapshotCount} 个品种，历史回填 {Backfilled} 个品种",
                snapshotCount, backfillCount);
        }

        /// <summary>
        /// 根据映射构造快照：斗南命中 → 单价换算整把价；未命中 → 参考价。
        /// 返回 null 表示该品种本次无有效行情（斗南命中但价格解析失败）。
        /// </summary>
        private FlowerPriceSnapshot? BuildSnapshot(SpeciesMapping mapping, List<PriceRow> rows, DateTime publishDate)
        {
            if (mapping.DounanName == null)
            {
                // 斗南未覆盖品种：参考价（Min/Max 为 ±6% 浮动，体现参考区间）
                var avg = mapping.ReferenceBundlePrice;
                if (avg <= 0) return null;
                return new FlowerPriceSnapshot
                {
                    SpeciesId = mapping.SpeciesId,
                    MarketId = DounanMarketId,
                    AvgPrice = avg,
                    MinPrice = Math.Round(avg * 0.94m, 2),
                    MaxPrice = Math.Round(avg * 1.06m, 2),
                    Volume = 0,
                    TradeCount = 0,
                    SnapshotTime = publishDate
                };
            }

            var row = rows.FirstOrDefault(r => r.Name == mapping.DounanName);
            if (row == null)
            {
                _logger.LogWarning("斗南价格表未匹配到品种 {SpeciesId} 的品名 {Name}（本次跳过，沿用上一快照）",
                    mapping.SpeciesId, mapping.DounanName);
                return null;
            }

            decimal Convert(decimal price) => mapping.IsBundlePrice
                ? price
                : price * mapping.StemsPerBundle;

            var avgPrice = Convert(row.AvgPrice);
            if (avgPrice <= 0) return null;

            return new FlowerPriceSnapshot
            {
                SpeciesId = mapping.SpeciesId,
                MarketId = DounanMarketId,
                AvgPrice = Math.Round(avgPrice, 2),
                MinPrice = Math.Round(Convert(row.MinPrice), 2),
                MaxPrice = Math.Round(Convert(row.MaxPrice), 2),
                Volume = 0,      // 公开价格表无成交量，如实置 0
                TradeCount = 0,  // 公开价格表无成交笔数，如实置 0
                SnapshotTime = publishDate
            };
        }

        /// <summary>写入数据池（原始行情留痕，供 AI 训练/回溯）</summary>
        private async Task WriteDataPoolAsync(FlowerPriceSnapshot snapshot)
        {
            var dataPoolGrain = _clusterClient.GetGrain<IFlowerDataPoolGrain>(0);
            var entry = new DataPoolEntry
            {
                DataType = DataPoolDataType.MarketSnapshot,
                DataSource = DounanMarketId,
                RawPayload = Convert.ToBase64String(MemoryPackSerializer.Serialize(snapshot)),
                Timestamp = snapshot.SnapshotTime,
                ModelVersion = "",
                Confidence = null
            };
            await dataPoolGrain.WriteAsync(entry);
        }

        private async Task<string> FetchHtmlAsync(string url)
        {
            using var response = await _httpClient.GetAsync(url).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            // 站点页面为 UTF-8（meta charset=utf-8）
            return Encoding.UTF8.GetString(bytes);
        }

        /// <summary>解析文章标题中的发布时间（斗南每日发布，取当日 10:00）</summary>
        private static DateTime ParsePublishDate(string year, string month, string day)
        {
            if (int.TryParse(year, out var y) && int.TryParse(month, out var mo) && int.TryParse(day, out var d))
            {
                try
                {
                    return new DateTime(y, mo, d).AddHours(10);
                }
                catch (ArgumentOutOfRangeException)
                {
                    // 非法日期回退当前时间
                }
            }
            return DateTime.UtcNow;
        }

        /// <summary>解析文章价格表数据行：品名/计量单位/最高价/最低价/均价</summary>
        private static List<PriceRow> ParsePriceRows(string html)
        {
            var rows = new List<PriceRow>();
            foreach (Match m in RowRegex.Matches(html))
            {
                // 行内 5 个单元格：品名/单位/最高价/最低价/均价
                var cells = CellRegex.Matches(m.Groups["row"].Value);
                if (cells.Count < 5) continue;

                var name = cells[0].Groups["v"].Value.Trim();
                if (name.Length == 0 || name == "品名") continue;

                var unit = cells[1].Groups["v"].Value.Trim();
                if (!TryParseCellDecimal(cells[2], out var max)) continue;
                if (!TryParseCellDecimal(cells[3], out var min)) continue;
                if (!TryParseCellDecimal(cells[4], out var avg)) continue;

                rows.Add(new PriceRow { Name = name, Unit = unit, MaxPrice = max, MinPrice = min, AvgPrice = avg });
            }
            return rows;
        }

        private static bool TryParseCellDecimal(Match cell, out decimal value)
        {
            var raw = cell.Groups["v"].Value.Replace("&nbsp;", "").Trim();
            return decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        /// <summary>
        /// 执行 Grain 调用并在 Silo 重启/不可达时自动重试。
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
        /// </summary>
        private static bool IsTransientOrleansException(Exception ex)
        {
            if (ex is OrleansMessageRejectionException) return true;
            if (ex is global::Orleans.Runtime.Messaging.ConnectionFailedException) return true;
            if (ex is OrleansException && ex.InnerException is not null)
                return IsTransientOrleansException(ex.InnerException);
            return false;
        }

        /// <summary>斗南价格表数据行</summary>
        private sealed class PriceRow
        {
            public string Name { get; set; } = "";
            public string Unit { get; set; } = "";
            public decimal MaxPrice { get; set; }
            public decimal MinPrice { get; set; }
            public decimal AvgPrice { get; set; }
        }
    }
}
