using Orleans;
using System.Threading.Tasks;

namespace Horizon.Orleans.Interface.Arena
{
    public enum ArenaBattleState { Created, Preparing, InProgress, Completed }

    public interface IArenaBattleGrain : IGrainWithStringKey
    {
        Task InitializeBattleAsync(long redId, long blueId, int seasonId);
        Task<ArenaBattleState> GetBattleStateAsync();
        Task ConcludeBattleAsync(long winnerId);
    }
}
