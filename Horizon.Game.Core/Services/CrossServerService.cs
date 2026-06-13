using System.Collections.Generic;
using System.Threading.Tasks;
using Horizon.Game.Core.Interfaces;
using Horizon.Orleans.Interface.CrossServer;
using Orleans;

namespace Horizon.Game.Core.Services
{
    public class CrossServerService : ICrossServerService
    {
        private readonly IClusterClient _clusterClient;

        public CrossServerService(IClusterClient clusterClient)
        {
            _clusterClient = clusterClient;
        }

        public async Task<string> InitiateCrossServerMatchAsync(int battleType, List<int> participatingServerIds)
        {
            var director = _clusterClient.GetGrain<ICrossServerDirectorGrain>(0);
            return await director.CreateCrossServerMatchAsync(battleType, participatingServerIds);
        }

        public async Task<bool> TransferPlayerToIslandAsync(long characterId, int islandId)
        {
            var transferGrain = _clusterClient.GetGrain<ICrossServerTransferGrain>(characterId);
            return await transferGrain.RequestTransferToCrossServerAsync(characterId, islandId);
        }

        public async Task<bool> ReturnPlayerToHomeServerAsync(long characterId)
        {
            var transferGrain = _clusterClient.GetGrain<ICrossServerTransferGrain>(characterId);
            return await transferGrain.RequestTransferBackAsync(characterId);
        }

        public async Task<int> GetPlayerLocationAsync(long characterId)
        {
            var transferGrain = _clusterClient.GetGrain<ICrossServerTransferGrain>(characterId);
            return await transferGrain.GetPlayerCurrentLocationAsync(characterId);
        }
    }
}