using Horizon.Core.Abstract;
using Horizon.Entities;
using Horizon.Game.Message.Network;
using Horizon.Model.Flower;
using Horizon.Orleans.Interface;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;
using MemoryPack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Horizon.Orleans.Grains
{
    /// <summary>
    /// 花卉品种Grain实现 - 负责价格预测与历史管理
    /// </summary>
    public class FlowerSpeciesGrain : Grain, IFlowerSpeciesGrain
    {
        private readonly ILogger<FlowerSpeciesGrain> _logger;
        private readonly IPersistentState<FlowerSpeciesState> _speciesState;
        private readonly IDataContext<FlowerEntityContext, FlowerPricePrediction, long> _predictionDataContext;
        private readonly IDataContext<FlowerEntityContext, FlowerPredictionModel, long> _modelDataContext;

        private const int MaxPriceHistoryEntries = 365;

        public FlowerSpeciesGrain(
            ILogger<FlowerSpeciesGrain> logger,
            [PersistentState("flowerspecies", "FlowerStore")] IPersistentState<FlowerSpeciesState> speciesState,
            IDataContext<FlowerEntityContext, FlowerPricePrediction, long> predictionDataContext,
            IDataContext<FlowerEntityContext, FlowerPredictionModel, long> modelDataContext)
        {
            _logger = logger;
            _speciesState = speciesState;
            _predictionDataContext = predictionDataContext;
            _modelDataContext = modelDataContext;
        }

        public override async Task OnActivateAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("FlowerSpeciesGrain {GrainKey} activating.", this.GetPrimaryKeyLong());

            var speciesId = this.GetPrimaryKeyLong();
            if (_speciesState.State.PriceHistory == null)
                _speciesState.State.PriceHistory = new List<FlowerPriceSnapshot>();
            if (_speciesState.State.SpeciesId == 0)
                _speciesState.State.SpeciesId = speciesId;

            await base.OnActivateAsync(cancellationToken);
        }

        public async Task<FlowerPriceForecast> PredictPriceAsync(ForecastTimeScale timeScale, int horizonDays)
        {
            try
            {
                var state = _speciesState.State;
                var speciesId = this.GetPrimaryKeyLong();
                var now = DateTime.Now;

                var festivals = FlowerFestivalCalendar.GetFestivals();
                var forecast = FlowerPredictionService.Predict(
                    state.PriceHistory, timeScale, horizonDays, festivals);

                forecast.SpeciesId = speciesId;

                var modelEntity = new FlowerPredictionModel
                {
                    SpeciesId = speciesId,
                    ModelType = "ARIMA-ES",
                    ModelVersion = forecast.ModelVersion,
                    ModelParams = $"{{\"alpha\":\"{(timeScale == ForecastTimeScale.ShortTerm ? "0.3" : "0.1")}\",\"timeScale\":\"{timeScale}\",\"horizonDays\":\"{horizonDays}\"}}",
                    TrainingDataRange = state.PriceHistory.Count > 0
                        ? $"{state.PriceHistory.First().SnapshotTime:yyyy-MM-dd}~{state.PriceHistory.Last().SnapshotTime:yyyy-MM-dd}"
                        : "",
                    Accuracy = forecast.Confidence,
                    IsActive = true,
                    IsDeleted = false,
                    Passport = "SYSTEM",
                    CreateTime = now
                };
                var modelResult = await _modelDataContext.AddAsync(modelEntity);

                foreach (var point in forecast.PredictedPrices)
                {
                    var entity = new FlowerPricePrediction
                    {
                        SpeciesId = speciesId,
                        MarketId = forecast.MarketId,
                        ModelId = modelResult.Id,
                        PredictDate = point.Date,
                        PredictedPrice = point.PredictedPrice,
                        LowerBound = point.LowerBound,
                        UpperBound = point.UpperBound,
                        Confidence = forecast.Confidence,
                        TimeScale = (int)timeScale,
                        CreatedAt = now
                    };
                    await _predictionDataContext.AddAsync(entity);
                }

                state.CurrentForecast = forecast;
                state.LastPredictionTime = now;
                await _speciesState.WriteStateAsync();

                var dataPoolGrain = GrainFactory.GetGrain<IFlowerDataPoolGrain>(0);
                var dataPoolEntry = new DataPoolEntry
                {
                    DataType = DataPoolDataType.AIOutput,
                    DataSource = (int)speciesId,
                    RawPayload = Convert.ToBase64String(MemoryPackSerializer.Serialize(forecast)),
                    Timestamp = now,
                    RelatedEntityId = speciesId.ToString(),
                    ModelVersion = forecast.ModelVersion,
                    Confidence = forecast.Confidence
                };
                await dataPoolGrain.WriteAsync(dataPoolEntry);

                _logger.LogInformation("预测价格: SpeciesId={SpeciesId}, TimeScale={TimeScale}, HorizonDays={HorizonDays}, ModelId={ModelId}, Confidence={Confidence}",
                    speciesId, timeScale, horizonDays, modelResult.Id, forecast.Confidence);

                return forecast;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "预测价格失败: SpeciesId={SpeciesId}", this.GetPrimaryKeyLong());
                throw;
            }
        }

        public async Task UpdatePriceHistoryAsync(decimal price, DateTime timestamp)
        {
            try
            {
                var state = _speciesState.State;
                var speciesId = this.GetPrimaryKeyLong();

                var snapshot = new FlowerPriceSnapshot
                {
                    SpeciesId = speciesId,
                    MarketId = 0,
                    AvgPrice = price,
                    MinPrice = price,
                    MaxPrice = price,
                    Volume = 0,
                    TradeCount = 0,
                    SnapshotTime = timestamp
                };

                state.PriceHistory.Add(snapshot);

                if (state.PriceHistory.Count > MaxPriceHistoryEntries)
                {
                    state.PriceHistory = state.PriceHistory
                        .OrderByDescending(s => s.SnapshotTime)
                        .Take(MaxPriceHistoryEntries)
                        .ToList();
                }

                await _speciesState.WriteStateAsync();

                _logger.LogInformation("更新价格历史: SpeciesId={SpeciesId}, Price={Price}, Timestamp={Timestamp}",
                    speciesId, price, timestamp);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新价格历史失败: SpeciesId={SpeciesId}", this.GetPrimaryKeyLong());
                throw;
            }
        }

        public Task<List<FlowerPriceSnapshot>> GetPriceHistoryAsync(DateTime startTime, DateTime endTime)
        {
            try
            {
                var state = _speciesState.State;

                var results = state.PriceHistory
                    .Where(s => s.SnapshotTime >= startTime && s.SnapshotTime <= endTime)
                    .OrderBy(s => s.SnapshotTime)
                    .ToList();

                return Task.FromResult(results);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取价格历史失败: SpeciesId={SpeciesId}", this.GetPrimaryKeyLong());
                throw;
            }
        }

        public async Task<PlantingSuggestion> GetPlantingSuggestionAsync()
        {
            try
            {
                var state = _speciesState.State;
                var speciesId = this.GetPrimaryKeyLong();

                if (state.PriceHistory == null || state.PriceHistory.Count < 2)
                {
                    return new PlantingSuggestion
                    {
                        SpeciesCode = state.SpeciesCode,
                        SuggestionType = PlantingSuggestionType.Normal,
                        Reason = "历史价格数据不足，无法生成种植建议",
                        PriceChangePercent = 0,
                        ForecastPrice = 0
                    };
                }

                var forecast = state.CurrentForecast;
                if (forecast == null || forecast.PredictedPrices == null || forecast.PredictedPrices.Count == 0)
                {
                    forecast = await PredictPriceAsync(ForecastTimeScale.ShortTerm, 14);
                }

                var sortedHistory = state.PriceHistory.OrderByDescending(s => s.SnapshotTime).ToList();
                var currentPrice = sortedHistory[0].AvgPrice;
                var forecastPrice = forecast.PredictedPrices.Count > 0
                    ? forecast.PredictedPrices.Last().PredictedPrice
                    : currentPrice;

                var priceChangePercent = currentPrice > 0
                    ? (forecastPrice - currentPrice) / currentPrice * 100
                    : 0;

                var suggestionType = PlantingSuggestionType.Normal;
                var reason = "";

                if (priceChangePercent > 10)
                {
                    suggestionType = PlantingSuggestionType.ExpandPlanting;
                    reason = $"预测价格上涨{priceChangePercent:F1}%，超过10%阈值，建议扩大种植规模以获取更多利润";
                }
                else if (priceChangePercent < -10)
                {
                    suggestionType = PlantingSuggestionType.EarlyHarvest;
                    reason = $"预测价格下跌{Math.Abs(priceChangePercent):F1}%，超过10%阈值，建议提前采收以减少损失";
                }
                else
                {
                    reason = $"预测价格变化{priceChangePercent:F1}%，在正常波动范围内，维持当前种植计划";
                }

                _logger.LogInformation("种植建议: SpeciesId={SpeciesId}, SuggestionType={SuggestionType}, PriceChangePercent={PriceChangePercent}%",
                    speciesId, suggestionType, priceChangePercent);

                return new PlantingSuggestion
                {
                    SpeciesCode = state.SpeciesCode,
                    SuggestionType = suggestionType,
                    Reason = reason,
                    PriceChangePercent = priceChangePercent,
                    ForecastPrice = forecastPrice
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取种植建议失败: SpeciesId={SpeciesId}", this.GetPrimaryKeyLong());
                throw;
            }
        }
    }
}
