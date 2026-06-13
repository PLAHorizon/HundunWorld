using System.Collections.Generic;
using System.Threading.Tasks;

namespace Horizon.Game.Core.Interfaces
{
    public interface ICrossServerService
    {
        Task<string> InitiateCrossServerMatchAsync(int battleType, List<int> participatingServerIds);
        Task<bool> TransferPlayerToIslandAsync(long characterId, int islandId);
        Task<bool> ReturnPlayerToHomeServerAsync(long characterId);
        Task<int> GetPlayerLocationAsync(long characterId);
    }
}