using Orleans;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Horizon.Orleans.Interface.Arena
{
    [GenerateSerializer]
    public class ArenaRankDto
    {
        [Id(0)]
        public long CharacterId { get; set; }

        [Id(1)]
        public string CharacterName { get; set; }

        [Id(2)]
        public int Rank { get; set; }

        [Id(3)]
        public int Rating { get; set; }
    }

    public interface IArenaRankingGrain : IGrainWithIntegerKey
    {
        Task UpdatePlayerRatingAsync(long characterId, string characterName, int rating);
        Task<List<ArenaRankDto>> GetTopPlayersAsync(int count = 100);
        Task RefreshRankingsAsync();
    }
}
