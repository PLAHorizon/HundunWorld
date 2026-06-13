using Horizon.Core.Abstract;
using Horizon.Entities;
using Horizon.Game.Message.Network;
using Horizon.Model.Flower;
using Horizon.Orleans.Interface;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Horizon.Orleans.Grains
{
    public class FlowerForecastSchedulerGrain : Grain, IForecastSchedulerGrain
    {
        private readonly ILogger<FlowerForecastSchedulerGrain> _logger;
        private readonly IPersistentState<ForecastSchedulerState> _schedulerState;
        private readonly IDataContext<FlowerEntityContext, FlowerSpecies, long> _speciesContext;

        private const int MaxTaskHistoryEntries = 100;

        public FlowerForecastSchedulerGrain(
            ILogger<FlowerForecastSchedulerGrain> logger,
            [PersistentState("forecastscheduler", "FlowerStore")] IPersistentState<ForecastSchedulerState> schedulerState,
            IDataContext<FlowerEntityContext, FlowerSpecies, long> speciesContext)
        {
            _logger = logger;
            _schedulerState = schedulerState;
            _speciesContext = speciesContext;
        }

        public override async Task OnActivateAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("FlowerForecastSchedulerGrain {GrainKey} activating.", this.GetPrimaryKeyLong());

            if (_schedulerState.State.TaskHistory == null)
                _schedulerState.State.TaskHistory = new List<string>();

            await base.OnActivateAsync(cancellationToken);
        }

        public async Task TriggerDailyForecastAsync()
        {
            try
            {
                var state = _schedulerState.State;
                var now = DateTime.Now;

                var activeSpeciesIds = await GetActiveSpeciesIdsAsync();

                foreach (var speciesId in activeSpeciesIds)
                {
                    try
                    {
                        var speciesGrain = GrainFactory.GetGrain<IFlowerSpeciesGrain>(speciesId);
                        await speciesGrain.PredictPriceAsync(ForecastTimeScale.MediumTerm, 30);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "每日预测品种失败: SpeciesId={SpeciesId}", speciesId);
                    }
                }

                state.LastDailyForecastTime = now;
                state.ActiveTaskCount = activeSpeciesIds.Count;

                var historyEntry = $"DailyForecast:{now:O}:SpeciesCount={activeSpeciesIds.Count}";
                state.TaskHistory.Add(historyEntry);

                if (state.TaskHistory.Count > MaxTaskHistoryEntries)
                {
                    state.TaskHistory = state.TaskHistory
                        .TakeLast(MaxTaskHistoryEntries)
                        .ToList();
                }

                await _schedulerState.WriteStateAsync();

                _logger.LogInformation("触发每日预测: SpeciesCount={SpeciesCount}, Time={Time}", activeSpeciesIds.Count, now);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "触发每日预测失败");
                throw;
            }
        }

        public async Task TriggerHourlyAggregationAsync()
        {
            try
            {
                var state = _schedulerState.State;
                var now = DateTime.Now;

                state.LastHourlyAggregationTime = now;

                var historyEntry = $"HourlyAggregation:{now:O}";
                state.TaskHistory.Add(historyEntry);

                if (state.TaskHistory.Count > MaxTaskHistoryEntries)
                {
                    state.TaskHistory = state.TaskHistory
                        .TakeLast(MaxTaskHistoryEntries)
                        .ToList();
                }

                await _schedulerState.WriteStateAsync();

                _logger.LogInformation("触发每小时聚合: Time={Time}", now);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "触发每小时聚合失败");
                throw;
            }
        }

        public Task<DateTime> GetLastRunTimeAsync(string taskName)
        {
            try
            {
                var state = _schedulerState.State;

                var result = taskName?.ToLowerInvariant() switch
                {
                    "dailyforecast" => state.LastDailyForecastTime,
                    "hourlyaggregation" => state.LastHourlyAggregationTime,
                    _ => DateTime.MinValue
                };

                return Task.FromResult(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取上次运行时间失败: TaskName={TaskName}", taskName);
                throw;
            }
        }

        private async Task<List<int>> GetActiveSpeciesIdsAsync()
        {
            try
            {
                var species = await _speciesContext.QueryAsync(s => !s.IsDeleted);
                var activeIds = species.Select(s => (int)s.Id).OrderBy(id => id).ToList();

                if (activeIds.Count == 0)
                {
                    _logger.LogWarning("未找到活跃品种，使用默认品种ID");
                    return new List<int> { 1, 2, 3, 4, 5 };
                }

                return activeIds;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取活跃品种ID失败，使用默认值");
                return new List<int> { 1, 2, 3, 4, 5 };
            }
        }
    }
}
