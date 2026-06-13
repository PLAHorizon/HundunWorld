using Horizon.Orleans.Interface.CrossServer;
using Microsoft.Extensions.Logging;
using Orleans;
using System.Threading.Tasks;

namespace Horizon.Orleans.Grains.CrossServer
{
    public class CrossServerTransferGrain : Grain, ICrossServerTransferGrain
    {
        private readonly ILogger<CrossServerTransferGrain> _logger;
        private int _currentIslandId = 0; // 0 means home server

        public CrossServerTransferGrain(ILogger<CrossServerTransferGrain> logger)
        {
            _logger = logger;
        }

        public async Task<bool> RequestTransferToCrossServerAsync(long characterId, int targetIslandId)
        {
            if (_currentIslandId != 0) return false; // Already in a cross server

            var islandGrain = GrainFactory.GetGrain<ICrossServerIslandGrain>(targetIslandId);
            bool success = await islandGrain.PlayerEnterAsync(characterId, 1); // 1 = hardcoded source server for now
            
            if (success)
            {
                _currentIslandId = targetIslandId;
                _logger.LogInformation($"玩家 {characterId} 已成功转移至跨服岛屿 {targetIslandId}");
            }
            
            return success;
        }

        public async Task<bool> RequestTransferBackAsync(long characterId)
        {
            if (_currentIslandId == 0) return true; // Already home

            var islandGrain = GrainFactory.GetGrain<ICrossServerIslandGrain>(_currentIslandId);
            await islandGrain.PlayerLeaveAsync(characterId);
            
            _currentIslandId = 0;
            _logger.LogInformation($"玩家 {characterId} 已成功转回本服。");
            return true;
        }

        public Task<int> GetPlayerCurrentLocationAsync(long characterId)
        {
            return Task.FromResult(_currentIslandId);
        }
    }
}