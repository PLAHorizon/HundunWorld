using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Horizon.Game.GengDi.Models;
using Newtonsoft.Json.Linq;

namespace Horizon.Game.GengDi.Core.Services
{
    public abstract class MusicSourceProviderBase : IMusicSourceProvider
    {
        protected readonly HttpClient _httpClient;
        protected string _baseUrl = "";
        protected readonly object _lock = new object();
        
        public abstract string SourceName { get; }
        public abstract int Priority { get; }
        
        protected MusicSourceProviderBase()
        {
            _httpClient = new HttpClient(SslConfiguration.CreateTestEnvironmentHandler()) { Timeout = TimeSpan.FromSeconds(20) };
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
        }
        
        public abstract Task<bool> IsAvailableAsync();
        public abstract Task<List<Song>> SearchSongsAsync(string keyword, int limit = 30);
        public abstract Task<string> GetSongUrlAsync(string songId);
        public abstract Task<(string LrcLyrics, string TLyrics)> GetLyricsAsync(string songId);
        public abstract Task<List<Playlist>> GetTopPlaylistsAsync(int limit = 20);
        public abstract Task<List<Song>> GetPlaylistSongsAsync(string playlistId);
        public abstract Task<List<Song>> GetPlaylistSongsWithAudioUrlsAsync(string playlistId);
        public abstract Task<List<Song>> GetNewSongsAsync(int limit = 30);
        
        protected async Task<string> FetchJsonAsync(string relativeUrl)
        {
            if (string.IsNullOrEmpty(_baseUrl)) return null;
            
            var fullUrl = relativeUrl.StartsWith("http") ? relativeUrl : $"{_baseUrl}{relativeUrl}";
            
            try
            {
                var response = await _httpClient.GetAsync(fullUrl);
                if (!response.IsSuccessStatusCode) return null;
                return await response.Content.ReadAsStringAsync();
            }
            catch
            {
                return null;
            }
        }
        
        protected JObject TryParseJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try { return JObject.Parse(json); }
            catch { return null; }
        }
        
        protected string StripSourcePrefix(string id, string prefix)
        {
            if (string.IsNullOrEmpty(id)) return id;
            return id.StartsWith(prefix) ? id.Substring(prefix.Length) : id;
        }
    }
}
