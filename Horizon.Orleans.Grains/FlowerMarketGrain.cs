using Horizon.Core.Abstract;
using Horizon.Entities;
using Horizon.Game.Message.Network;
using Horizon.Model.Flower;
using Horizon.Orleans.Interface;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;
using Orleans.Streams;
using MemoryPack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Horizon.Core;

namespace Horizon.Orleans.Grains
{
    /// <summary>
    /// 花卉市场Grain实现 - 负责市场价格快照管理
    /// </summary>
    public class FlowerMarketGrain : Grain, IFlowerMarketGrain
    {
        private readonly ILogger<FlowerMarketGrain> _logger;
        private readonly IPersistentState<FlowerMarketState> _marketState;
        private readonly IDataContext<FlowerEntityContext, FlowerMarketSnapshot, long> _dataContext;

        public FlowerMarketGrain(
            ILogger<FlowerMarketGrain> logger,
            [PersistentState("flowermarket", "FlowerStore")] IPersistentState<FlowerMarketState> marketState,
            IDataContext<FlowerEntityContext, FlowerMarketSnapshot, long> dataContext)
        {
            _logger = logger;
            _marketState = marketState;
            _dataContext = dataContext;
        }

        public override async Task OnActivateAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("FlowerMarketGrain {GrainKey} activating.", this.GetPrimaryKeyLong());

            if (_marketState.State.LatestSnapshots == null)
                _marketState.State.LatestSnapshots = new Dictionary<long, FlowerPriceSnapshot>();
            if (_marketState.State.ActiveSpeciesCount == 0)
                _marketState.State.ActiveSpeciesCount = 0;

            await base.OnActivateAsync(cancellationToken);
        }

        public async Task UpdateSnapshotAsync(FlowerPriceSnapshot snapshot)
        {
            try
            {
                if (snapshot == null)
                {
                    _logger.LogWarning("更新价格快照无效: snapshot is null");
                    return;
                }

                var marketId = this.GetPrimaryKeyLong();
                var state = _marketState.State;

                bool priceChanged = false;
                decimal previousPrice = 0m;
                if (state.LatestSnapshots.TryGetValue(snapshot.SpeciesId, out var previousSnapshot))
                {
                    previousPrice = previousSnapshot.AvgPrice;
                    priceChanged = previousSnapshot.AvgPrice != snapshot.AvgPrice;
                }
                else
                {
                    priceChanged = true;
                }

                var entity = new FlowerMarketSnapshot
                {
                    SpeciesId = snapshot.SpeciesId,
                    MarketId = snapshot.MarketId,
                    AvgPrice = snapshot.AvgPrice,
                    MinPrice = snapshot.MinPrice,
                    MaxPrice = snapshot.MaxPrice,
                    Volume = snapshot.Volume,
                    TradeCount = snapshot.TradeCount,
                    SnapshotTime = snapshot.SnapshotTime,
                    DataSource = 0
                };

                var result = await _dataContext.AddAsync(entity);
                if (result == null)
                {
                    _logger.LogError("更新价格快照失败: 数据库保存返回null, SpeciesId={SpeciesId}", snapshot.SpeciesId);
                    return;
                }

                state.LatestSnapshots[snapshot.SpeciesId] = snapshot;
                state.LastUpdateTime = DateTime.Now;
                state.ActiveSpeciesCount = state.LatestSnapshots.Count;

                await _marketState.WriteStateAsync();

                var dataPoolGrain = GrainFactory.GetGrain<IFlowerDataPoolGrain>(0);
                var dataPoolEntry = new DataPoolEntry
                {
                    DataType = DataPoolDataType.MarketSnapshot,
                    DataSource = (int)snapshot.MarketId,
                    RawPayload = Convert.ToBase64String(MemoryPackSerializer.Serialize(snapshot)),
                    Timestamp = snapshot.SnapshotTime,
                    RelatedEntityId = result.Id.ToString(),
                    ModelVersion = "",
                    Confidence = null
                };
                await dataPoolGrain.WriteAsync(dataPoolEntry);

                if (priceChanged)
                {
                    var streamProvider = this.GetStreamProvider(OrleansConst.CommonMessageStreamProvider);
                    var streamId = StreamId.Create("FlowerMarketPriceChange", marketId);
                    var stream = streamProvider.GetStream<FlowerPriceSnapshot>(streamId);
                    await stream.OnNextAsync(snapshot);

                    _logger.LogInformation("价格变更推送: MarketId={MarketId}, SpeciesId={SpeciesId}, PreviousPrice={PreviousPrice}, NewPrice={NewPrice}",
                        marketId, snapshot.SpeciesId, previousPrice, snapshot.AvgPrice);
                }

                _logger.LogInformation("更新价格快照: MarketId={MarketId}, SpeciesId={SpeciesId}, AvgPrice={AvgPrice}",
                    marketId, snapshot.SpeciesId, snapshot.AvgPrice);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新价格快照失败: SpeciesId={SpeciesId}", snapshot?.SpeciesId);
                throw;
            }
        }

        public async Task<FlowerPriceSnapshot> GetLatestSnapshotAsync(int speciesId)
        {
            try
            {
                var state = _marketState.State;

                if (state.LatestSnapshots.TryGetValue(speciesId, out var snapshot))
                {
                    return snapshot;
                }

                var marketId = this.GetPrimaryKeyLong();
                var dbSnapshot = await _dataContext.QueryFirstOrDefaultAsync(
                    e => e.SpeciesId == speciesId && e.MarketId == marketId,
                    e => new FlowerPriceSnapshot
                    {
                        SpeciesId = e.SpeciesId,
                        MarketId = e.MarketId,
                        AvgPrice = e.AvgPrice,
                        MinPrice = e.MinPrice,
                        MaxPrice = e.MaxPrice,
                        Volume = e.Volume,
                        TradeCount = e.TradeCount,
                        SnapshotTime = e.SnapshotTime
                    });

                return dbSnapshot;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取最新价格快照失败: SpeciesId={SpeciesId}", speciesId);
                throw;
            }
        }

        public Task<List<FlowerPriceSnapshot>> GetMarketOverviewAsync()
        {
            try
            {
                var state = _marketState.State;
                var snapshots = state.LatestSnapshots.Values.ToList();
                return Task.FromResult(snapshots);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取市场概览失败");
                throw;
            }
        }

        public async Task<decimal> GetAveragePriceAsync(int speciesId)
        {
            try
            {
                var cacheKey = CacheConst.FLOWER_PRICE_HOT + speciesId;
                var result = await Cache.Current.GetOrSetAsync(
                    cacheKey,
                    async () =>
                    {
                        var state = _marketState.State;

                        if (state.LatestSnapshots.TryGetValue(speciesId, out var snapshot))
                        {
                            return snapshot.AvgPrice;
                        }

                        _logger.LogWarning("品种不存在于最新快照: SpeciesId={SpeciesId}", speciesId);
                        return 0m;
                    },
                    TimeSpan.FromMinutes(5),
                    cacheNullValue: true);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取平均价格失败: SpeciesId={SpeciesId}", speciesId);
                throw;
            }
        }
    }
}
