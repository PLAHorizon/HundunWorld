using System.Collections.Generic;
using System.Threading.Tasks;
using Horizon.Orleans.Interface.Arena;
using Horizon.Game.Core.Interfaces;
using Orleans;

namespace Horizon.Game.Core.Services
{
    public class ArenaService : IArenaService
    {
        private readonly IClusterClient _clusterClient;

        public ArenaService(IClusterClient clusterClient)
        {
            _clusterClient = clusterClient;
        }

        public async Task JoinMatchmakingAsync(long characterId)
        {
            var playerGrain = _clusterClient.GetGrain<IArenaPlayerGrain>(characterId);
            await playerGrain.JoinMatchmakingAsync();
        }

        public async Task CancelMatchmakingAsync(long characterId)
        {
            var playerGrain = _clusterClient.GetGrain<IArenaPlayerGrain>(characterId);
            await playerGrain.CancelMatchmakingAsync();
        }

        public async Task<List<ArenaRankDto>> GetTopPlayersAsync(int count)
        {
            var rankingGrain = _clusterClient.GetGrain<IArenaRankingGrain>(0);
            return await rankingGrain.GetTopPlayersAsync(count);
        }

        public async Task<PlayerArenaInfoDto> GetPlayerArenaInfoAsync(long characterId)
        {
            var playerGrain = _clusterClient.GetGrain<IArenaPlayerGrain>(characterId);
            return await playerGrain.GetPlayerRecordAsync();
        }
        
        public async Task<ArenaSeasonDto> GetCurrentSeasonAsync()
        {
            var seasonGrain = _clusterClient.GetGrain<IArenaSeasonGrain>(0);
            return await seasonGrain.GetCurrentSeasonAsync();
        }
    }
}