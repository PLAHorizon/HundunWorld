using Orleans;
using System.Threading.Tasks;

namespace Horizon.Orleans.Interface.Arena
{
    [GenerateSerializer]
    public class PlayerArenaInfoDto
    {
        [Id(0)]
        public int CurrentRating { get; set; }

        [Id(1)]
        public int TotalMatches { get; set; }

        [Id(2)]
        public int Wins { get; set; }
    }

    public interface IArenaPlayerGrain : IGrainWithIntegerKey
    {
        Task<PlayerArenaInfoDto> GetPlayerRecordAsync();
        Task UpdateMatchResultAsync(int ratingChange, bool isWin, bool isDraw);
        Task<int> GetCurrentRatingAsync();
        Task JoinMatchmakingAsync();
        Task CancelMatchmakingAsync();
    }
}
