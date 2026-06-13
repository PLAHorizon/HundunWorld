using Orleans;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Horizon.Orleans.Interface.CrossServer
{
    public interface ICrossServerDirectorGrain : IGrainWithIntegerKey
    {
        Task<string> CreateCrossServerMatchAsync(int battleType, List<int> serverIds);
        Task<bool> EndMatchAsync(string matchId, int winnerServerId);
        Task<List<string>> GetActiveMatchesAsync();
    }
}