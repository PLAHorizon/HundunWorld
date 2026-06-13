using Orleans;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace Horizon.Orleans.Interface.CrossServer
{
    public interface ICrossServerIslandGrain : IGrainWithIntegerKey
    {
        Task InitializeIslandAsync(string matchId, int maxCapacity);
        Task<bool> PlayerEnterAsync(long characterId, int sourceServerId);
        Task PlayerLeaveAsync(long characterId);
        Task<int> GetPlayerCountAsync();
        Task BroadcastGlobalMessageAsync(string message);
        Task ReportKillAsync(long killerId, long victimId);
    }
}