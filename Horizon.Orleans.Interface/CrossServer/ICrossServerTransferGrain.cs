using Orleans;
using System.Threading.Tasks;

namespace Horizon.Orleans.Interface.CrossServer
{
    public interface ICrossServerTransferGrain : IGrainWithIntegerKey
    {
        Task<bool> RequestTransferToCrossServerAsync(long characterId, int targetIslandId);
        Task<bool> RequestTransferBackAsync(long characterId);
        Task<int> GetPlayerCurrentLocationAsync(long characterId);
    }
}