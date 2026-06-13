using Horizon.Core.Abstract;
using Horizon.Entities;
using Horizon.Game.Message.Network;
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
    public class FlowerRegionDemandGrain : Grain, IRegionDemandGrain
    {
        private readonly ILogger<FlowerRegionDemandGrain> _logger;
        private readonly IPersistentState<RegionDemandState> _state;

        public FlowerRegionDemandGrain(
            ILogger<FlowerRegionDemandGrain> logger,
            [PersistentState("regiondemand", "FlowerStore")] IPersistentState<RegionDemandState> state)
        {
            _logger = logger;
            _state = state;
        }

        public override async Task OnActivateAsync(CancellationToken cancellationToken)
        {
            if (_state.State.SpeciesDemandIndex == null)
                _state.State.SpeciesDemandIndex = new Dictionary<int, double>();
            if (_state.State.RegionId == 0)
                _state.State.RegionId = (int)this.GetPrimaryKeyLong();

            await base.OnActivateAsync(cancellationToken);
        }

        public async Task UpdateDemandAsync(int speciesId, double searchIndex, DateTime timestamp)
        {
            try
            {
                _state.State.SpeciesDemandIndex[speciesId] = searchIndex;
                _state.State.LastUpdateTime = DateTime.Now;
                await _state.WriteStateAsync();

                _logger.LogInformation("更新区域需求: RegionId={RegionId}, SpeciesId={SpeciesId}, SearchIndex={SearchIndex}",
                    _state.State.RegionId, speciesId, searchIndex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新区域需求失败: RegionId={RegionId}", _state.State.RegionId);
                throw;
            }
        }

        public Task<Dictionary<int, double>> GetRegionalDemandAsync(int speciesId)
        {
            try
            {
                if (_state.State.SpeciesDemandIndex.TryGetValue(speciesId, out var index))
                    return Task.FromResult(new Dictionary<int, double> { { _state.State.RegionId, index } });

                return Task.FromResult(new Dictionary<int, double>());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取区域需求失败: RegionId={RegionId}", _state.State.RegionId);
                throw;
            }
        }

        public Task<List<int>> GetHotSpeciesAsync(int topN)
        {
            try
            {
                var hotSpecies = _state.State.SpeciesDemandIndex
                    .OrderByDescending(kv => kv.Value)
                    .Take(topN)
                    .Select(kv => kv.Key)
                    .ToList();

                return Task.FromResult(hotSpecies);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取热门品种失败: RegionId={RegionId}", _state.State.RegionId);
                throw;
            }
        }
    }
}
