using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Horizon.WebApi.Services
{
    /// <summary>
    /// 花卉品种目录条目 —— 「商家->商品发布」模块品种选择器的数据源。
    /// 数据来源：平台目录 FlowerSpeciesCatalog（兜底元数据：名称/品类/产地/规格）
    ///   + 斗南花卉市场公开行情（实时价格，由 Gateway DounanMarketDataFetcher 抓取并推送至 IFlowerMarketGrain）。
    /// 该目录整体落库 Redis 并由 FlowerSpeciesCatalogRefreshService 定时刷新。
    /// </summary>
    public class FlowerSpeciesCatalogItem
    {
        /// <summary>品种ID（与平台目录 / 斗南行情映射一致，2001~2022）</summary>
        public long Id { get; set; }

        /// <summary>品种名称（如 红玫瑰 / 白菊）</summary>
        public string Name { get; set; } = "";

        /// <summary>品类（如 玫瑰 / 菊花 / 百合）</summary>
        public string Category { get; set; } = "";

        /// <summary>花源产地（如 云南斗南 / 云南晋宁）</summary>
        public string Origin { get; set; } = "";

        /// <summary>包装规格（如 18支/把 / 1把）</summary>
        public string Grade { get; set; } = "";

        /// <summary>斗南行情均价（元），无实时行情时为 0</summary>
        public decimal AvgPrice { get; set; }

        /// <summary>斗南行情最低价（元）</summary>
        public decimal MinPrice { get; set; }

        /// <summary>斗南行情最高价（元）</summary>
        public decimal MaxPrice { get; set; }

        /// <summary>成交量</summary>
        public int Volume { get; set; }

        /// <summary>成交笔数</summary>
        public int TradeCount { get; set; }

        /// <summary>行情快照时间（UTC）</summary>
        public System.DateTime SnapshotTime { get; set; }

        /// <summary>数据来源：斗南花卉市场公开行情 / 平台目录(兜底)</summary>
        public string DataSource { get; set; } = "";
    }

    /// <summary>
    /// 花卉品种目录服务（Redis 缓存 + 定时刷新）。
    /// 读取路径走 Cache-Aside：优先 Redis，未命中则从源重建并回写。
    /// </summary>
    public interface IFlowerSpeciesCatalogService
    {
        /// <summary>获取全部花卉品种（Redis 缓存优先，未命中则从源重建并回写）。</summary>
        Task<List<FlowerSpeciesCatalogItem>> GetAllAsync(CancellationToken cancellationToken = default);

        /// <summary>按品种ID获取（Redis 缓存优先）。</summary>
        Task<FlowerSpeciesCatalogItem?> GetByIdAsync(long speciesId, CancellationToken cancellationToken = default);

        /// <summary>
        /// 从源（平台目录 + 斗南公开行情 Grain）重建并写入 Redis 缓存。
        /// 由 FlowerSpeciesCatalogRefreshService 定时调用；亦可被其它模块主动触发刷新。
        /// </summary>
        Task RefreshAsync(CancellationToken cancellationToken = default);
    }
}
