using Horizon.Orleans.Interface.Arena;
using Orleans;
using Orleans.Providers;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Horizon.Orleans.Grains.Arena
{
    public class ArenaRankingState
    {
         public Dictionary<long, ArenaRankDto> Rankings = new();
    }
    
    [StorageProvider(ProviderName = "Default")]
    public class ArenaRankingGrain : Grain<ArenaRankingState>, IArenaRankingGrain
    {
        public async Task UpdatePlayerRatingAsync(long characterId, string characterName, int rating)
        {
            if(!State.Rankings.ContainsKey(characterId))
            {
                 State.Rankings[characterId] = new ArenaRankDto { CharacterId = characterId, CharacterName = characterName, Rating = rating };
            }
            State.Rankings[characterId].Rating = rating;
            await WriteStateAsync();
        }
        
        public async Task RefreshRankingsAsync() 
        {
            // Sorting logic will update Rank fields.
            var sorted = State.Rankings.Values.OrderByDescending(x => x.Rating).ToList();
            for(int i = 0; i < sorted.Count; ++i) sorted[i].Rank = i + 1;
            await WriteStateAsync();
        }
        
        public Task<List<ArenaRankDto>> GetTopPlayersAsync(int count) 
        {
            return Task.FromResult(State.Rankings.Values.OrderByDescending(x=>x.Rating).Take(count).ToList());
        }
    }
}