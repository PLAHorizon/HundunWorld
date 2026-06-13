using Orleans;
using System.Threading.Tasks;

namespace Horizon.Orleans.Interface.Arena
{
    public interface IArenaMatchmakingGrain : IGrainWithIntegerKey
    {
        Task JoinQueueAsync(long characterId, int currentRating);
        Task LeaveQueueAsync(long characterId);
        Task ProcessMatchmakingAsync();
    }
}
