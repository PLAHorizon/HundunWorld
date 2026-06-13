using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Horizon.Game.GengDi.Models;
using Newtonsoft.Json.Linq;

namespace Horizon.Game.GengDi.Core.Services
{
    public class NeteaseMusicApiService : MusicSourceProviderBase
    {
        private static NeteaseMusicApiService _instance;
        private static readonly object _neteaseLock = new object();

        public static NeteaseMusicApiService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_neteaseLock)
                    {
                        if (_instance == null)
                        {
                            _instance = new NeteaseMusicApiService();
                        }
                    }
                }
                return _instance;
            }
        }

        private static readonly string[] _apiEndpoints = new[]
        {
            "https://netease-cloud-music-api.fe-mm.com",
            "https://neteasecloudmusicapi.vercel.app",
            "https://netease-cloud-music-api-five-roan-59.vercel.app"
        };

        private int _activeEndpointIndex = -1;

        public string BaseUrl
        {
            get => _baseUrl;
            set => _baseUrl = value?.TrimEnd('/') ?? "";
        }

        private NeteaseMusicApiService() { }

        private async Task<string> GetActiveBaseUrlAsync()
        {
            if (!string.IsNullOrEmpty(_baseUrl)) return _baseUrl;
            if (_activeEndpointIndex >= 0) return _apiEndpoints[_activeEndpointIndex];

            for (int i = 0; i < _apiEndpoints.Length; i++)
            {
                try
                {
                    var testUrl = $"{_apiEndpoints[i]}/top/playlist?limit=1";
                    var response = await _httpClient.GetAsync(testUrl);
                    if (response.IsSuccessStatusCode)
                    {
                        var content = await response.Content.ReadAsStringAsync();
                        if (!string.IsNullOrEmpty(content) && content.Contains("playlists"))
                        {
                            _activeEndpointIndex = i;
                            System.Diagnostics.Debug.WriteLine($"Music API active: {_apiEndpoints[i]}");
                            return _apiEndpoints[i];
                        }
                    }
                }
                catch { }
            }

            _activeEndpointIndex = 0;
            return _apiEndpoints[0];
        }

        private new async Task<string> FetchJsonAsync(string relativeUrl)
        {
            var baseUrl = await GetActiveBaseUrlAsync();
            var fullUrl = relativeUrl.StartsWith("http") ? relativeUrl : $"{baseUrl}{relativeUrl}";

            try
            {
                var response = await _httpClient.GetAsync(fullUrl);
                if (!response.IsSuccessStatusCode)
                {
                    if (_activeEndpointIndex >= 0)
                    {
                        var oldIndex = _activeEndpointIndex;
                        _activeEndpointIndex = -1;
                        var retryBaseUrl = await GetActiveBaseUrlAsync();
                        if (_activeEndpointIndex != oldIndex)
                        {
                            fullUrl = $"{retryBaseUrl}{relativeUrl}";
                            response = await _httpClient.GetAsync(fullUrl);
                        }
                    }
                    if (!response.IsSuccessStatusCode) return null;
                }
                return await response.Content.ReadAsStringAsync();
            }
            catch
            {
                return null;
            }
        }

        public override string SourceName => "netease";
        public override int Priority => 1;

        public async Task<string> FetchJsonPublicAsync(string relativeUrl)
        {
            return await FetchJsonAsync(relativeUrl);
        }

        public override async Task<List<Song>> SearchSongsAsync(string keyword, int limit = 30)
        {
            if (string.IsNullOrWhiteSpace(keyword)) return new List<Song>();
            try
            {
                var url = $"/cloudsearch?keywords={Uri.EscapeDataString(keyword)}&limit={limit}&type=1";
                var json = await FetchJsonAsync(url);
                if (json == null) return new List<Song>();
                var obj = TryParseJson(json);
                var songs = obj?["result"]?["songs"] as JArray;
                if (songs == null) return new List<Song>();
                return ParseSongs(songs).ToList();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SearchSongsAsync failed: {ex.Message}");
                return new List<Song>();
            }
        }

        private IEnumerable<Song> ParseSongs(JArray songs)
        {
            foreach (var s in songs)
            {
                yield return new Song
                {
                    Id = $"netease_{s["id"]?.Value<long>() ?? 0}",
                    Title = s["name"]?.ToString() ?? "",
                    ArtistId = s["ar"]?.FirstOrDefault()?["id"]?.ToString() ?? "",
                    ArtistName = string.Join("/", s["ar"]?.Select(a => a["name"]?.ToString()) ?? Array.Empty<string>()),
                    AlbumId = s["al"]?["id"]?.ToString() ?? "",
                    AlbumName = s["al"]?["name"]?.ToString() ?? "",
                    Duration = TimeSpan.FromMilliseconds(s["dt"]?.Value<long>() ?? 0),
                    CoverUrl = s["al"]?["picUrl"]?.ToString() ?? "",
                    AudioUrl = "",
                    Source = "netease",
                    PlayCount = (int)(s["pop"]?.Value<long>() ?? 0)
                };
            }
        }

        public override async Task<string> GetSongUrlAsync(string neteaseId)
        {
            try
            {
                var id = neteaseId;
                if (id.StartsWith("netease_")) id = id.Substring(8);

                var url = $"/song/url/v1?id={id}&level=standard";
                var json = await FetchJsonAsync(url);
                var obj = TryParseJson(json);
                var data = obj?["data"] as JArray;

                if (data != null && data.Count > 0)
                {
                    var urlResult = data[0]["url"]?.ToString() ?? "";
                    if (!string.IsNullOrEmpty(urlResult) && !urlResult.Contains("404"))
                        return urlResult;
                }

                var fallbackUrl = $"/song/url?id={id}&br=320000";
                var fallbackJson = await FetchJsonAsync(fallbackUrl);
                var fallbackObj = TryParseJson(fallbackJson);
                var fallbackData = fallbackObj?["data"] as JArray;
                if (fallbackData != null && fallbackData.Count > 0)
                    return fallbackData[0]["url"]?.ToString() ?? "";

                return "";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GetSongUrlAsync failed: {ex.Message}");
                return "";
            }
        }

        public override async Task<(string LrcLyrics, string TLyrics)> GetLyricsAsync(string neteaseId)
        {
            try
            {
                var id = neteaseId;
                if (id.StartsWith("netease_")) id = id.Substring(8);
                var url = $"/lyric?id={id}";
                var json = await FetchJsonAsync(url);
                var obj = TryParseJson(json);
                var lrc = obj?["lrc"]?["lyric"]?.ToString() ?? "";
                var tlyric = obj?["tlyric"]?["lyric"]?.ToString() ?? "";
                return (lrc, tlyric);
            }
            catch
            {
                return ("", "");
            }
        }

        public override async Task<List<Playlist>> GetTopPlaylistsAsync(int limit = 20)
        {
            try
            {
                var url = $"/top/playlist?limit={limit}";
                var json = await FetchJsonAsync(url);
                var obj = TryParseJson(json);
                var playlists = obj?["playlists"] as JArray;
                if (playlists == null) return new List<Playlist>();
                return ParsePlaylists(playlists).ToList();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GetTopPlaylistsAsync failed: {ex.Message}");
                return new List<Playlist>();
            }
        }

        private IEnumerable<Playlist> ParsePlaylists(JArray playlists)
        {
            foreach (var p in playlists)
            {
                yield return new Playlist
                {
                    Id = $"netease_pl_{p["id"]?.Value<long>() ?? 0}",
                    Name = p["name"]?.ToString() ?? "",
                    CoverUrl = p["coverImgUrl"]?.ToString() ?? "",
                    Description = p["description"]?.ToString() ?? "",
                    CreatorName = p["creator"]?["nickname"]?.ToString() ?? "",
                    PlayCount = p["playCount"]?.Value<int>() ?? 0,
                    CreatedDate = DateTimeOffset.FromUnixTimeMilliseconds(p["createTime"]?.Value<long>() ?? 0).DateTime,
                    UpdatedDate = DateTimeOffset.FromUnixTimeMilliseconds(p["updateTime"]?.Value<long>() ?? 0).DateTime
                };
            }
        }

        public override async Task<List<Song>> GetPlaylistSongsWithAudioUrlsAsync(string playlistId)
        {
            var songs = await GetPlaylistSongsAsync(playlistId);
            if (songs.Count == 0) return songs;

            var tasks = songs.Take(Math.Min(songs.Count, 10)).Select(async song =>
            {
                var audioUrl = await GetSongUrlAsync(song.Id);
                if (!string.IsNullOrEmpty(audioUrl))
                    song.AudioUrl = audioUrl;
                return song;
            }).ToList();

            var results = await Task.WhenAll(tasks);
            for (int i = 0; i < results.Length; i++)
                songs[i].AudioUrl = results[i].AudioUrl;

            return songs;
        }

        public override async Task<List<Song>> GetPlaylistSongsAsync(string playlistId)
        {
            try
            {
                var id = playlistId;
                if (id.StartsWith("netease_pl_")) id = id.Substring(11);

                var url = $"/playlist/detail?id={id}";
                var json = await FetchJsonAsync(url);
                var obj = TryParseJson(json);
                var trackIds = obj?["playlist"]?["trackIds"] as JArray;
                var tracks = obj?["playlist"]?["tracks"] as JArray;

                if (tracks != null && tracks.Count > 0)
                    return ParseSongs(tracks).ToList();

                if (trackIds != null && trackIds.Count > 0)
                {
                    var ids = trackIds.Select(t => t["id"].ToString()).Take(200);
                    var idsStr = string.Join(",", ids);
                    var detailUrl = $"/song/detail?ids=[{idsStr}]";
                    var detailJson = await FetchJsonAsync(detailUrl);
                    var detailObj = TryParseJson(detailJson);
                    var detailSongs = detailObj?["songs"] as JArray;
                    if (detailSongs != null)
                        return ParseSongs(detailSongs).ToList();
                }

                return new List<Song>();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GetPlaylistSongsAsync failed: {ex.Message}");
                return new List<Song>();
            }
        }

        public override async Task<List<Song>> GetNewSongsAsync(int limit = 30)
        {
            try
            {
                var url = $"/top/song?type=0";
                var json = await FetchJsonAsync(url);
                var obj = TryParseJson(json);
                var songs = obj?["data"] as JArray;
                if (songs == null) return new List<Song>();

                return songs.Take(limit).Select(s => new Song
                {
                    Id = $"netease_{s["id"]?.Value<long>() ?? 0}",
                    Title = s["name"]?.ToString() ?? "",
                    ArtistId = s["artists"]?.FirstOrDefault()?["id"]?.ToString() ?? "",
                    ArtistName = string.Join("/", s["artists"]?.Select(a => a["name"]?.ToString()) ?? Array.Empty<string>()),
                    AlbumId = s["album"]?["id"]?.ToString() ?? "",
                    AlbumName = s["album"]?["name"]?.ToString() ?? "",
                    Duration = TimeSpan.FromMilliseconds(s["duration"]?.Value<long>() ?? 0),
                    CoverUrl = s["album"]?["picUrl"]?.ToString() ?? "",
                    AudioUrl = "",
                    Source = "netease",
                    PlayCount = 0
                }).ToList();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GetNewSongsAsync failed: {ex.Message}");
                return new List<Song>();
            }
        }

        public override async Task<bool> IsAvailableAsync()
        {
            try
            {
                var json = await FetchJsonAsync("/top/playlist?limit=1");
                return !string.IsNullOrEmpty(json);
            }
            catch
            {
                return false;
            }
        }

        public async Task<List<Playlist>> SearchPlaylistsAsync(string keyword, int limit = 20)
        {
            if (string.IsNullOrWhiteSpace(keyword)) return new List<Playlist>();

            var results = await SearchPlaylistsByTypeAsync(keyword, limit, "1000");
            if (results.Count > 0) return results;

            results = await SearchPlaylistsByTypeAsync(keyword, limit, "1002");
            if (results.Count > 0) return results;

            var topPlaylists = await GetTopPlaylistsAsync(limit);
            return topPlaylists.Where(p =>
                (p.Name?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (p.Description?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false)
            ).Take(limit / 2).ToList();
        }

        private async Task<List<Playlist>> SearchPlaylistsByTypeAsync(string keyword, int limit, string type)
        {
            try
            {
                var url = $"/cloudsearch?keywords={Uri.EscapeDataString(keyword)}&limit={limit}&type={type}";
                var json = await FetchJsonAsync(url);
                var obj = TryParseJson(json);
                var playlists = obj?["result"]?["playlists"] as JArray;
                if (playlists == null || playlists.Count == 0) return new List<Playlist>();

                return ParsePlaylists(playlists).ToList();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SearchPlaylistsByType({type}) failed: {ex.Message}");
                return new List<Playlist>();
            }
        }
    }
}
