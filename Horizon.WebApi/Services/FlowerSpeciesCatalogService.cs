using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Horizon.Core.Abstract;
using Horizon.Game.Message.Network;
using Horizon.Orleans.Interface;
using Horizon.WebApi.Controllers;
using Microsoft.Extensions.Logging;
using Orleans;

namespace Horizon.WebApi.Services
{
    /// <summary>
    /// 花卉品种目录服务实现。
    ///
    /// 数据源：
    ///   1) 平台目录 FlowerSpeciesCatalog —— 平台自有 22 个品种的元数据（名称/品类/产地/规格），始终可用；
    ///   2) 斗南花卉市场公开行情 —— 由 Gateway DounanMarketDataFetcher 抓取昆明市农业农村局价格行情，
    ///      推送至 IFlowerMarketGrain（DounanMarketId=2），用于补全实时价格。
    ///
    /// 落库：
    ///   整个目录与每个品种分别写入 Redis（Cache-Aside 模式），键前缀 flower:species:*。
    ///   若 Redis 未配置或行情 Grain 暂无数据，则降级为纯平台目录，保证「商品发布」模块始终可选项。
    /// </summary>
    public class FlowerSpeciesCatalogService : IFlowerSpeciesCatalogService
    {
        private readonly ICache? _cache;
        private readonly IClusterClient _clusterClient;
        private readonly ILogger<FlowerSpeciesCatalogService> _logger;

        /// <summary>Redis 缓存过期时间：与 Gateway DounanMarketDataFetcher 抓取间隔（30 分钟）对齐。</summary>
        private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(30);

        private const string CatalogKey = "flower:species:catalog:v1";
        private const string SpeciesKeyPrefix = "flower:species:v1:";

        public FlowerSpeciesCatalogService(
            ICache? cache,
            IClusterClient clusterClient,
            ILogger<FlowerSpeciesCatalogService> logger)
        {
            _cache = cache;
            _clusterClient = clusterClient ?? throw new ArgumentNullException(nameof(clusterClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<List<FlowerSpeciesCatalogItem>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_cache != null)
            {
                var cached = await _cache.GetAsync<List<FlowerSpeciesCatalogItem>>(CatalogKey);
                if (cached != null && cached.Count > 0)
                    return cached;
            }

            // 缓存未命中（或 Redis 不可用）：从源重建并回写
            var built = await BuildFromSourceAsync(cancellationToken);
            if (_cache != null)
                await _cache.InsertAsync(CatalogKey, built, (int)CacheTtl.TotalSeconds);
            return built;
        }

        public async Task<FlowerSpeciesCatalogItem?> GetByIdAsync(long speciesId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_cache != null)
            {
                var cached = await _cache.GetAsync<FlowerSpeciesCatalogItem>(SpeciesKeyPrefix + speciesId);
                if (cached != null)
                    return cached;
            }

            var all = await GetAllAsync(cancellationToken);
            return all.FirstOrDefault(x => x.Id == speciesId);
        }

        public async Task RefreshAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var items = await BuildFromSourceAsync(cancellationToken);

            if (_cache == null)
            {
                _logger.LogWarning("Redis 缓存未配置，跳过花卉品种目录缓存落库（仅运行时内存目录 + 平台目录兜底可用）");
                return;
            }

            await _cache.InsertAsync(CatalogKey, items, (int)CacheTtl.TotalSeconds);
            foreach (var item in items)
                await _cache.InsertAsync(SpeciesKeyPrefix + item.Id, item, (int)CacheTtl.TotalSeconds);

            var liveCount = items.Count(i => i.DataSource == "斗南花卉市场公开行情");
            _logger.LogInformation(
                "花卉品种目录已刷新至 Redis：共 {Count} 个品种（其中 {Live} 个含斗南实时行情），缓存 TTL {Ttl} 分钟",
                items.Count, liveCount, (int)CacheTtl.TotalMinutes);
        }

        /// <summary>
        /// 从源构建品种目录：
        ///   1) 平台目录元数据（始终可用，保证商品发布模块可选项）；
        ///   2) 斗南花卉市场公开行情补全实时价格（抓取自昆明市农业农村局，经 Gateway 推送至 Grain）。
        /// </summary>
        private async Task<List<FlowerSpeciesCatalogItem>> BuildFromSourceAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // 1) 平台目录元数据
            var dict = FlowerSpeciesCatalog.Items.ToDictionary(
                kv => kv.Key,
                kv => new FlowerSpeciesCatalogItem
                {
                    Id = kv.Value.Id,
                    Name = kv.Value.Name,
                    Category = kv.Value.Category,
                    Origin = kv.Value.Origin,
                    Grade = kv.Value.Grade
                });

            // 2) 斗南花卉市场公开行情补全实时价格
            try
            {
                var marketGrain = _clusterClient.GetGrain<IFlowerMarketGrain>(FlowerSpeciesCatalog.DounanMarketId);
                var snapshots = await marketGrain.GetMarketOverviewAsync();
                if (snapshots != null)
                {
                    foreach (var s in snapshots)
                    {
                        if (dict.TryGetValue(s.SpeciesId, out var item))
                        {
                            item.AvgPrice = s.AvgPrice;
                            item.MinPrice = s.MinPrice;
                            item.MaxPrice = s.MaxPrice;
                            item.Volume = s.Volume;
                            item.TradeCount = s.TradeCount;
                            item.SnapshotTime = s.SnapshotTime;
                            item.DataSource = "斗南花卉市场公开行情";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "从斗南行情 Grain 拉取品种价格失败，使用平台目录兜底（不含实时价格）");
            }

            // 3) 未命中实时行情的品种标记为平台目录兜底
            foreach (var item in dict.Values)
                if (string.IsNullOrEmpty(item.DataSource))
                    item.DataSource = "平台目录(兜底)";

            return dict.Values.OrderBy(x => x.Id).ToList();
        }
    }
}
