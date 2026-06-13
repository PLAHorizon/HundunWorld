using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Horizon.Game.GengDi.Core.Services;
using Horizon.Game.GengDi.Models;

namespace Horizon.Game.GengDi.Core.Services
{
    public class LocalMusicSourceProvider : IMusicSourceProvider
    {
        public string SourceName => "local";
        public int Priority => 0;

        public Task<bool> IsAvailableAsync()
        {
            return Task.FromResult(true);
        }

        public Task<List<Song>> SearchSongsAsync(string keyword, int limit = 30)
        {
            if (string.IsNullOrWhiteSpace(keyword)) return Task.FromResult(new List<Song>());
            var library = MusicLibraryService.Instance;
            var allSongs = library.GetAllSongs();
            var results = allSongs
                .Where(s => s.Source == "local")
                .Where(s =>
                    (s.Title?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (s.ArtistName?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (s.AlbumName?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false))
                .Take(limit)
                .ToList();
            return Task.FromResult(results);
        }

        public Task<string> GetSongUrlAsync(string songId)
        {
            var song = MusicLibraryService.Instance.GetSongById(songId);
            if (song != null && song.IsLocal && !string.IsNullOrEmpty(song.LocalFilePath))
                return Task.FromResult(song.LocalFilePath);
            return Task.FromResult<string>(null);
        }

        public Task<(string LrcLyrics, string TLyrics)> GetLyricsAsync(string songId)
        {
            return Task.FromResult<(string, string)>((null, null));
        }

        public Task<List<Playlist>> GetTopPlaylistsAsync(int limit = 20)
        {
            return Task.FromResult(new List<Playlist>());
        }

        public Task<List<Song>> GetPlaylistSongsAsync(string playlistId)
        {
            return Task.FromResult(new List<Song>());
        }

        public Task<List<Song>> GetPlaylistSongsWithAudioUrlsAsync(string playlistId)
        {
            return Task.FromResult(new List<Song>());
        }

        public Task<List<Song>> GetNewSongsAsync(int limit = 30)
        {
            return Task.FromResult(new List<Song>());
        }
    }
}
