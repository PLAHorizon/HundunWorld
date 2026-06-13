using System.Collections.Generic;
using System.Threading.Tasks;
using Horizon.Game.GengDi.Models;

namespace Horizon.Game.GengDi.Core.Services
{
    public interface IMusicSourceProvider
    {
        string SourceName { get; }
        int Priority { get; }
        
        Task<bool> IsAvailableAsync();
        Task<List<Song>> SearchSongsAsync(string keyword, int limit = 30);
        Task<string> GetSongUrlAsync(string songId);
        Task<(string LrcLyrics, string TLyrics)> GetLyricsAsync(string songId);
        Task<List<Playlist>> GetTopPlaylistsAsync(int limit = 20);
        Task<List<Song>> GetPlaylistSongsAsync(string playlistId);
        Task<List<Song>> GetPlaylistSongsWithAudioUrlsAsync(string playlistId);
        Task<List<Song>> GetNewSongsAsync(int limit = 30);
    }
}
