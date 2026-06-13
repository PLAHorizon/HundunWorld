using Orleans;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Horizon.Orleans.Interface.Arena
{
    [GenerateSerializer]
    public class ArenaSeasonDto
    {
        [Id(0)]
        public int Id { get; set; }

        [Id(1)]
        public string SeasonName { get; set; }
    }

    public interface IArenaSeasonGrain : IGrainWithIntegerKey
    {
        Task<ArenaSeasonDto> GetCurrentSeasonAsync();
        Task<ArenaSeasonDto> GetSeasonInfoAsync(int seasonId);
        Task<List<ArenaSeasonDto>> GetSeasonsAsync();
    }
}
