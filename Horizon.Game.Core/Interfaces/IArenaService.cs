using System.Collections.Generic;
using System.Threading.Tasks;
using Horizon.Orleans.Interface.Arena;

namespace Horizon.Game.Core.Interfaces
{
    public interface IArenaService
    {
        Task JoinMatchmakingAsync(long characterId);
        Task CancelMatchmakingAsync(long characterId);
        Task<List<ArenaRankDto>> GetTopPlayersAsync(int count);
        Task<PlayerArenaInfoDto> GetPlayerArenaInfoAsync(long characterId);
        Task<ArenaSeasonDto> GetCurrentSeasonAsync();
    }
}