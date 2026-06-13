using Horizon.Orleans.Interface.Arena;
using Orleans;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Horizon.Orleans.Grains.Arena
{
    public class ArenaMatchmakingGrain : Grain, IArenaMatchmakingGrain
    {
        private List<(long Id, int R, DateTime T)> _queue = new();
        
        public Task JoinQueueAsync(long id, int r) 
        { 
            _queue.Add((id, r, DateTime.Now)); 
            return Task.CompletedTask; 
        }
        
        public Task LeaveQueueAsync(long id) 
        { 
            _queue.RemoveAll(x => x.Id == id); 
            return Task.CompletedTask; 
        }
        
        public Task ProcessMatchmakingAsync()
        {
            if (_queue.Count > 1) 
            {
                var p1 = _queue[0]; 
                var p2 = _queue[1];
                _queue.RemoveRange(0, 2);
                
                var battleId = Guid.NewGuid().ToString();
                var battleGrain = GrainFactory.GetGrain<IArenaBattleGrain>(battleId);
                battleGrain.InitializeBattleAsync(p1.Id, p2.Id, 1);
            }
            return Task.CompletedTask;
        }
    }
}