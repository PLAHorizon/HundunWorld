using Horizon.Orleans.Interface.Arena;
using Orleans;
using System.Threading.Tasks;

namespace Horizon.Orleans.Grains.Arena
{
    public class ArenaBattleGrain : Grain, IArenaBattleGrain
    {
        ArenaBattleState _state;
        long _red;
        long _blue;
        int _seasonId;

        public Task InitializeBattleAsync(long red, long blue, int s)
        {
            _red = red; 
            _blue = blue; 
            _seasonId = s;
            _state = ArenaBattleState.Preparing;
            return Task.CompletedTask;
        }

        public Task<ArenaBattleState> GetBattleStateAsync() => Task.FromResult(_state);

        public async Task ConcludeBattleAsync(long winner)
        {
            _state = ArenaBattleState.Completed;
            var redPlayer = GrainFactory.GetGrain<IArenaPlayerGrain>(_red);
            var bluePlayer = GrainFactory.GetGrain<IArenaPlayerGrain>(_blue);
            
            if (winner == _red) 
            { 
                await redPlayer.UpdateMatchResultAsync(25, true, false); 
                await bluePlayer.UpdateMatchResultAsync(-25, false, false); 
            }
            else 
            { 
                await bluePlayer.UpdateMatchResultAsync(25, true, false); 
                await redPlayer.UpdateMatchResultAsync(-25, false, false); 
            }
        }
    }
}