using Horizon.Model.Arena;
using Horizon.Orleans.Interface.Arena;
using Orleans;
using Orleans.Providers;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Horizon.Orleans.Grains.Arena
{
    [StorageProvider(ProviderName = "Default")]
    public class ArenaSeasonGrain : Grain<List<ArenaSeason>>, IArenaSeasonGrain
    {
        public Task<ArenaSeasonDto> GetCurrentSeasonAsync() 
        {
            var s = State.FirstOrDefault(x => x.IsActive);
            return Task.FromResult(s == null ? null : new ArenaSeasonDto { Id = s.Id, SeasonName = s.SeasonName });
        }
        public Task<ArenaSeasonDto> GetSeasonInfoAsync(int id) 
        {
            var s = State.FirstOrDefault(x => x.Id == id);
            return Task.FromResult(s == null ? null : new ArenaSeasonDto { Id = s.Id, SeasonName = s.SeasonName });
        }
        public Task<List<ArenaSeasonDto>> GetSeasonsAsync() 
        {
            return Task.FromResult(State.Select(s => new ArenaSeasonDto { Id = s.Id, SeasonName = s.SeasonName }).ToList());
        }
    }
}
