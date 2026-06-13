using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Horizon.Game.GengDi.Data.Repositories;
using Horizon.Game.GengDi.Models;

namespace Horizon.Game.GengDi.Core.Services
{
    public class MusicLibraryService
    {
        private static MusicLibraryService _instance;
        private static readonly object _lock = new object();

        public static MusicLibraryService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new MusicLibraryService();
                        }
                    }
                }
                return _instance;
            }
        }

        private readonly SongRepository _songRepository;
        private readonly PlaylistRepository _playlistRepository;
        private readonly PlayHistoryRepository _playHistoryRepository;
        private List<Song> _cachedSongs;
        private List<Playlist> _cachedPlaylists;
        private DateTime _lastCacheUpdate = DateTime.MinValue;
        private readonly TimeSpan _cacheExpiry = TimeSpan.FromMinutes(5);

        private MusicLibraryService()
        {
            _songRepository = new SongRepository();
            _playlistRepository = new PlaylistRepository();
            _playHistoryRepository = new PlayHistoryRepository();
        }

        public async Task InitializeAsync()
        {
            await Task.CompletedTask;
        }

        public List<Song> GetAllSongs()
        {
            EnsureCache();
            return _cachedSongs.ToList();
        }

        public async Task<List<Song>> GetAllSongsAsync()
        {
            return await ClientAsyncDispatcher.RunLiteDbAsync(() => _songRepository.GetAll()).ConfigureAwait(false);
        }

        public async Task<List<Playlist>> GetAllPlaylistsAsync()
        {
            EnsureCache();
            return await Task.FromResult(_cachedPlaylists.ToList());
        }

        public Song GetSongById(string id)
        {
            EnsureCache();
            return _cachedSongs.FirstOrDefault(s => s.Id == id);
        }

        public async Task<Song> GetSongByIdAsync(string id)
        {
            return await ClientAsyncDispatcher.RunLiteDbAsync(() => _songRepository.GetById(id)).ConfigureAwait(false);
        }

        public List<Song> GetFavoriteSongs()
        {
            EnsureCache();
            return _cachedSongs.Where(s => s.IsFavorite).ToList();
        }

        public List<Song> SearchSongs(string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword)) return new List<Song>();
            EnsureCache();
            return _cachedSongs.Where(s =>
                (s.Title?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (s.ArtistName?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (s.AlbumName?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false)).ToList();
        }

        public async Task<List<Song>> SearchSongsAsync(string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword)) return new List<Song>();
            EnsureCache();
            return await Task.FromResult(_cachedSongs.Where(s =>
                (s.Title?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (s.ArtistName?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (s.AlbumName?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false)).ToList());
        }

        public List<Playlist> GetAllPlaylists()
        {
            EnsureCache();
            return _cachedPlaylists.ToList();
        }

        public Playlist GetPlaylistById(string id)
        {
            EnsureCache();
            return _cachedPlaylists.FirstOrDefault(p => p.Id == id);
        }

        public List<Playlist> SearchPlaylists(string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword)) return new List<Playlist>();
            EnsureCache();
            return _cachedPlaylists.Where(p =>
                (p.Name?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (p.Description?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false)).ToList();
        }

        public async Task<List<Playlist>> SearchPlaylistsAsync(string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword)) return new List<Playlist>();
            EnsureCache();
            return await Task.FromResult(_cachedPlaylists.Where(p =>
                (p.Name?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (p.Description?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false)).ToList());
        }

        public void AddSong(Song song)
        {
            _songRepository.Add(song);
            ClearCache();
        }

        public void UpdateSong(Song song)
        {
            _songRepository.Update(song);
            ClearCache();
        }

        public void DeleteSong(string id)
        {
            _songRepository.Delete(id);
            ClearCache();
        }

        public void AddPlaylist(Playlist playlist)
        {
            _playlistRepository.Add(playlist);
            ClearCache();
        }

        public void UpdatePlaylist(Playlist playlist)
        {
            _playlistRepository.Update(playlist);
            ClearCache();
        }

        public void DeletePlaylist(string id)
        {
            _playlistRepository.Delete(id);
            ClearCache();
        }

        public void AddSongToPlaylist(string playlistId, string songId)
        {
            var playlist = _playlistRepository.GetById(playlistId);
            if (playlist == null) return;
            var songIds = playlist.SongIds;
            if (!songIds.Contains(songId))
            {
                songIds.Add(songId);
                playlist.SongIds = songIds;
                playlist.UpdatedDate = DateTime.UtcNow;
                _playlistRepository.Update(playlist);
                ClearCache();
            }
        }

        public async Task AddSongToPlaylistAsync(string playlistId, string songId)
        {
            await ClientAsyncDispatcher.RunLiteDbAsync(() =>
            {
                var playlist = _playlistRepository.GetById(playlistId);
                if (playlist == null) return;
                var songIds = playlist.SongIds;
                if (!songIds.Contains(songId))
                {
                    songIds.Add(songId);
                    playlist.SongIds = songIds;
                    playlist.UpdatedDate = DateTime.UtcNow;
                    _playlistRepository.Update(playlist);
                    ClearCache();
                }
            });
        }

        public async Task EnsureSongInLibraryAsync(Song song)
        {
            if (song == null) return;
            await ClientAsyncDispatcher.RunLiteDbAsync(() =>
            {
                var existing = _songRepository.GetById(song.Id);
                if (existing == null)
                {
                    _songRepository.Add(song);
                    ClearCache();
                }
            });
        }

        public void RemoveSongFromPlaylist(string playlistId, string songId)
        {
            var playlist = _playlistRepository.GetById(playlistId);
            if (playlist == null) return;
            var songIds = playlist.SongIds;
            if (songIds.Contains(songId))
            {
                songIds.Remove(songId);
                playlist.SongIds = songIds;
                playlist.UpdatedDate = DateTime.UtcNow;
                _playlistRepository.Update(playlist);
                ClearCache();
            }
        }

        public List<Song> GetPlaylistSongs(string playlistId)
        {
            var playlist = _playlistRepository.GetById(playlistId);
            if (playlist == null) return new List<Song>();
            EnsureCache();
            return _cachedSongs.Where(s => playlist.SongIds.Contains(s.Id)).ToList();
        }

        public async Task<List<Song>> GetPlaylistSongsAsync(string playlistId)
        {
            var playlist = await ClientAsyncDispatcher.RunLiteDbAsync(() => _playlistRepository.GetById(playlistId)).ConfigureAwait(false);
            if (playlist == null) return new List<Song>();
            EnsureCache();
            return _cachedSongs.Where(s => playlist.SongIds.Contains(s.Id)).ToList();
        }

        public void RecordPlayHistory(string songId, string userId)
        {
            _playHistoryRepository.Add(new PlayHistoryRecord
            {
                SongId = songId,
                UserId = userId
            });
        }

        public List<PlayHistoryRecord> GetRecentHistory(string userId, int limit = 50)
        {
            return _playHistoryRepository.GetByUser(userId, limit);
        }

        public void EnsureSongInLibrary(Song song)
        {
            if (song == null) return;
            var existing = _songRepository.GetById(song.Id);
            if (existing == null)
            {
                _songRepository.Add(song);
                ClearCache();
            }
        }

        public void AddSongToRecentPlaylist(string songId)
        {
            const string recentPlaylistId = "pl_recent";
            var playlist = _playlistRepository.GetById(recentPlaylistId);
            if (playlist == null)
            {
                playlist = new Playlist
                {
                    Id = recentPlaylistId,
                    Name = "最近播放",
                    Description = "你最近听过的歌曲",
                    CreatorId = "system",
                    CreatorName = "系统",
                    CreatedDate = DateTime.UtcNow,
                    UpdatedDate = DateTime.UtcNow,
                    IsSystem = true
                };
                _playlistRepository.Add(playlist);
            }

            var songIds = playlist.SongIds;
            songIds.Remove(songId);
            songIds.Insert(0, songId);
            if (songIds.Count > 100)
            {
                songIds = songIds.Take(100).ToList();
            }
            playlist.SongIds = songIds;
            playlist.UpdatedDate = DateTime.UtcNow;
            _playlistRepository.Update(playlist);
            ClearCache();
        }

        public void IncrementPlayCount(string songId)
        {
            var song = _songRepository.GetById(songId);
            if (song == null) return;
            song.PlayCount++;
            _songRepository.Update(song);
            ClearCache();
        }

        public void ToggleSongFavorite(string songId)
        {
            var song = _songRepository.GetById(songId);
            if (song == null) return;
            song.IsFavorite = !song.IsFavorite;
            _songRepository.Update(song);

            const string favoritesPlaylistId = "pl_favorites";
            var playlist = _playlistRepository.GetById(favoritesPlaylistId);
            if (playlist == null)
            {
                playlist = new Playlist
                {
                    Id = favoritesPlaylistId,
                    Name = "我喜欢的音乐",
                    Description = "收藏的你最爱的歌曲",
                    CreatorId = "system",
                    CreatorName = "系统",
                    CreatedDate = DateTime.UtcNow,
                    UpdatedDate = DateTime.UtcNow,
                    IsSystem = true,
                    IsFavorite = true
                };
                _playlistRepository.Add(playlist);
            }

            if (song.IsFavorite)
            {
                var ids = playlist.SongIds;
                if (!ids.Contains(songId))
                {
                    ids.Add(songId);
                    playlist.SongIds = ids;
                }
            }
            else
            {
                var ids = playlist.SongIds;
                ids.Remove(songId);
                playlist.SongIds = ids;
            }
            playlist.UpdatedDate = DateTime.UtcNow;
            _playlistRepository.Update(playlist);
            ClearCache();
        }

        public List<Playlist> GetAvailablePlaylistsForSong(string songId)
        {
            EnsureCache();
            return _cachedPlaylists.Where(p => !p.SongIds.Contains(songId)).ToList();
        }

        public async Task ImportLocalSongsAsync(List<Song> songs)
        {
            if (songs == null || songs.Count == 0) return;
            await ClientAsyncDispatcher.RunLiteDbAsync(() =>
            {
                foreach (var song in songs)
                {
                    var existing = _songRepository.GetAll().FirstOrDefault(s => s.LocalFilePath == song.LocalFilePath && s.Source == "local");
                    if (existing == null)
                    {
                        _songRepository.Add(song);
                    }
                }
                ClearCache();
            });
        }

        public List<Song> GetLocalSongs()
        {
            EnsureCache();
            return _cachedSongs.Where(s => s.Source == "local").ToList();
        }

        public void DeleteLocalSong(string songId, bool deleteFile = false)
        {
            var song = _songRepository.GetById(songId);
            if (song == null) return;
            if (deleteFile && song.Source == "local" && !string.IsNullOrEmpty(song.LocalFilePath))
            {
                try
                {
                    if (System.IO.File.Exists(song.LocalFilePath))
                        System.IO.File.Delete(song.LocalFilePath);
                }
                catch { }
            }
            _songRepository.Delete(songId);
            ClearCache();
        }

        public static string GetMusicStoreDirectory()
        {
            var dir = System.IO.Path.Combine(LocalMediaStore.GetMediaRootDirectory(), "music", "originals");
            System.IO.Directory.CreateDirectory(dir);
            return dir;
        }

        public void AddSongToPlaylistByName(string playlistId, string songId)
        {
            AddSongToPlaylist(playlistId, songId);
        }

        public void ClearCache()
        {
            _cachedSongs = null;
            _cachedPlaylists = null;
            _lastCacheUpdate = DateTime.MinValue;
        }

        private void EnsureCache()
        {
            if (_cachedSongs == null || _cachedPlaylists == null || DateTime.Now - _lastCacheUpdate > _cacheExpiry)
            {
                _cachedSongs = _songRepository.GetAll();
                _cachedPlaylists = _playlistRepository.GetAll();
                _lastCacheUpdate = DateTime.Now;
            }
        }
    }
}
