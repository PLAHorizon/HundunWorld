using Horizon.Model.Arena;
using Horizon.Orleans.Interface.Arena;
using Orleans;
using Orleans.Providers;
using System;
using System.Threading.Tasks;

namespace Horizon.Orleans.Grains.Arena
{
    [StorageProvider(ProviderName = "Default")]
    public class ArenaPlayerGrain : Grain<ArenaPlayerRecord>, IArenaPlayerGrain
    {
        public override Task OnActivateAsync(System.Threading.CancellationToken cancellationToken)
        {
            if (State.CharacterId == 0)
            {
                State.CharacterId = this.GetPrimaryKeyLong();
                State.CurrentRating = 1000;
            }
            return base.OnActivateAsync(cancellationToken);
        }
        public Task<PlayerArenaInfoDto> GetPlayerRecordAsync() => Task.FromResult(new PlayerArenaInfoDto { CurrentRating = State.CurrentRating, TotalMatches = State.TotalMatches, Wins = State.Wins });
        public Task<int> GetCurrentRatingAsync() => Task.FromResult(State.CurrentRating);
        public async Task JoinMatchmakingAsync() => await GrainFactory.GetGrain<IArenaMatchmakingGrain>(0).JoinQueueAsync(State.CharacterId, State.CurrentRating);
        public async Task CancelMatchmakingAsync() => await GrainFactory.GetGrain<IArenaMatchmakingGrain>(0).LeaveQueueAsync(State.CharacterId);
        public async Task UpdateMatchResultAsync(int ratingChange, bool isWin, bool isDraw)
        {
            State.TotalMatches++;
            if (isDraw) State.Draws++;
            else if (isWin) State.Wins++;
            else State.Losses++;
            
            State.CurrentRating = Math.Max(0, State.CurrentRating + ratingChange);
            
            if (State.CurrentRating > State.HighestRating) 
                State.HighestRating = State.CurrentRating;
                
            await WriteStateAsync();
        }
    }
}
