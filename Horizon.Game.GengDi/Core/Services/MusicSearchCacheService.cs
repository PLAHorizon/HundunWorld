using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Horizon.Game.GengDi.Models;

namespace Horizon.Game.GengDi.Core.Services
{
    public class CachedSearchResult
    {
        public List<Song> Songs { get; set; } = new List<Song>();
        public List<Playlist> Playlists { get; set; } = new List<Playlist>();
        public DateTime CachedAt { get; set; } = DateTime.UtcNow;
        public string Keyword { get; set; }
    }

    public class CachedPlaylistSongs
    {
        public List<Song> Songs { get; set; } = new List<Song>();
        public DateTime CachedAt { get; set; } = DateTime.UtcNow;
        public string PlaylistId { get; set; }
    }

    public class CachedUrlEntry
    {
        public string SongId { get; set; }
        public string Url { get; set; }
        public string SourceName { get; set; }
        public DateTime CachedAt { get; set; } = DateTime.UtcNow;
        public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddHours(2);
        public bool IsExpired => DateTime.UtcNow > ExpiresAt;
    }

    public class MusicSearchCacheService
    {
        private static MusicSearchCacheService _instance;
        private static readonly object _lock = new object();

        public static MusicSearchCacheService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new MusicSearchCacheService();
                        }
                    }
                }
                return _instance;
            }
        }

        private readonly ConcurrentDictionary<string, CachedSearchResult> _searchCache = new();
        private readonly ConcurrentDictionary<string, CachedPlaylistSongs> _playlistCache = new();
        private readonly ConcurrentDictionary<string, CachedUrlEntry> _urlCache = new();
        private static readonly TimeSpan CacheTTL = TimeSpan.FromHours(24);
        private static readonly TimeSpan UrlCacheTTL = TimeSpan.FromHours(2);

        public bool TryGetSearchResult(string keyword, out CachedSearchResult result)
        {
            if (string.IsNullOrWhiteSpace(keyword)) { result = null; return false; }
            var key = keyword.ToLowerInvariant().Trim();
            if (_searchCache.TryGetValue(key, out result))
            {
                if (DateTime.UtcNow - result.CachedAt < CacheTTL)
                    return true;
                _searchCache.TryRemove(key, out _);
            }
            result = null;
            return false;
        }

        public void SetSearchResult(string keyword, List<Song> songs, List<Playlist> playlists)
        {
            if (string.IsNullOrWhiteSpace(keyword)) return;
            var key = keyword.ToLowerInvariant().Trim();
            _searchCache[key] = new CachedSearchResult
            {
                Keyword = keyword,
                Songs = songs ?? new List<Song>(),
                Playlists = playlists ?? new List<Playlist>(),
                CachedAt = DateTime.UtcNow
            };
        }

        public bool TryGetPlaylistSongs(string playlistId, out List<Song> songs)
        {
            if (string.IsNullOrWhiteSpace(playlistId)) { songs = null; return false; }
            if (_playlistCache.TryGetValue(playlistId, out var cached))
            {
                if (DateTime.UtcNow - cached.CachedAt < CacheTTL)
                {
                    songs = cached.Songs;
                    return true;
                }
                _playlistCache.TryRemove(playlistId, out _);
            }
            songs = null;
            return false;
        }

        public void SetPlaylistSongs(string playlistId, List<Song> songs)
        {
            if (string.IsNullOrWhiteSpace(playlistId)) return;
            _playlistCache[playlistId] = new CachedPlaylistSongs
            {
                PlaylistId = playlistId,
                Songs = songs ?? new List<Song>(),
                CachedAt = DateTime.UtcNow
            };
        }

        public bool TryGetCachedUrl(string songId, out string url)
        {
            url = null;
            if (string.IsNullOrWhiteSpace(songId)) return false;
            var key = songId.ToLowerInvariant().Trim();
            if (_urlCache.TryGetValue(key, out var entry))
            {
                if (!entry.IsExpired)
                {
                    url = entry.Url;
                    return true;
                }
                _urlCache.TryRemove(key, out _);
            }
            return false;
        }

        public void SetCachedUrl(string songId, string url, string sourceName = "")
        {
            if (string.IsNullOrWhiteSpace(songId) || string.IsNullOrWhiteSpace(url)) return;
            var key = songId.ToLowerInvariant().Trim();
            _urlCache[key] = new CachedUrlEntry
            {
                SongId = songId,
                Url = url,
                SourceName = sourceName,
                CachedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.Add(UrlCacheTTL)
            };
        }

        public async Task<bool> ValidateUrlAsync(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            try
            {
                using var client = new System.Net.Http.HttpClient();
                client.Timeout = TimeSpan.FromSeconds(5);
                var request = new System.Net.Http.HttpMethod("HEAD");
                var response = await client.SendAsync(new System.Net.Http.HttpRequestMessage(request, url));
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        public void ClearExpiredUrls()
        {
            foreach (var kv in _urlCache.Where(kv => kv.Value.IsExpired).ToList())
                _urlCache.TryRemove(kv.Key, out _);
        }

        public void ClearExpired()
        {
            foreach (var kv in _searchCache.Where(kv => DateTime.UtcNow - kv.Value.CachedAt >= CacheTTL).ToList())
                _searchCache.TryRemove(kv.Key, out _);
            foreach (var kv in _playlistCache.Where(kv => DateTime.UtcNow - kv.Value.CachedAt >= CacheTTL).ToList())
                _playlistCache.TryRemove(kv.Key, out _);
            foreach (var kv in _urlCache.Where(kv => kv.Value.IsExpired).ToList())
                _urlCache.TryRemove(kv.Key, out _);
        }

        public void ClearAll()
        {
            _searchCache.Clear();
            _playlistCache.Clear();
            _urlCache.Clear();
        }
    }
}
