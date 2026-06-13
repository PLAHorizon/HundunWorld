using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Horizon.Game.GengDi.Models;

namespace Horizon.Game.GengDi.Core.Services
{
    public class MusicSourceSelector
    {
        private readonly MusicSourceRegistry _registry;
        private readonly SourceHealthTracker _healthTracker;

        public MusicSourceSelector()
        {
            _registry = MusicSourceRegistry.Instance;
            _healthTracker = SourceHealthTracker.Instance;
        }

        public List<IMusicSourceProvider> GetSourcesForSong(Song song)
        {
            var providers = _registry.GetAvailableProviders();
            if (providers.Count == 0) return new List<IMusicSourceProvider>();

            if (song?.Source == "local")
            {
                var localProvider = _registry.GetProvider("local");
                if (localProvider != null)
                    return new List<IMusicSourceProvider> { localProvider };
            }

            var rankedHealth = _healthTracker.GetRankedSources();
            var sourceNames = rankedHealth.Select(r => r.Key).ToList();

            var result = providers
                .OrderByDescending(p => sourceNames.IndexOf(p.SourceName))
                .ThenBy(p => p.Priority)
                .ToList();

            if (song != null && !string.IsNullOrEmpty(song.LastPlaySuccessSource))
            {
                var lastSuccessProvider = result.FirstOrDefault(p => p.SourceName == song.LastPlaySuccessSource);
                if (lastSuccessProvider != null)
                {
                    result.Remove(lastSuccessProvider);
                    result.Insert(0, lastSuccessProvider);
                }
            }

            return result;
        }

        public async Task<string> GetSongUrlWithFallback(string songId, Song song = null, int maxRetries = 3)
        {
            if (song?.Source == "local" && !string.IsNullOrEmpty(song.LocalFilePath))
            {
                return song.LocalFilePath;
            }

            if (MusicSearchCacheService.Instance.TryGetCachedUrl(songId, out var cachedUrl))
            {
                var isValid = await MusicSearchCacheService.Instance.ValidateUrlAsync(cachedUrl);
                if (isValid)
                {
                    return cachedUrl;
                }
                MusicSearchCacheService.Instance.SetCachedUrl(songId, "", "");
            }

            var sources = GetSourcesForSong(song);
            var triedSources = new HashSet<string>();

            for (int attempt = 0; attempt < Math.Min(maxRetries, sources.Count); attempt++)
            {
                var source = sources.FirstOrDefault(s => !triedSources.Contains(s.SourceName));
                if (source == null) break;

                triedSources.Add(source.SourceName);
                var sw = System.Diagnostics.Stopwatch.StartNew();

                try
                {
                    var url = await source.GetSongUrlAsync(songId);
                    sw.Stop();

                    if (!string.IsNullOrEmpty(url))
                    {
                        _healthTracker.RecordSuccess(source.SourceName, sw.ElapsedMilliseconds);
                        if (song != null)
                        {
                            song.LastPlaySuccessSource = source.SourceName;
                            if (song.AvailableSources == null)
                                song.AvailableSources = new List<string>();
                            if (!song.AvailableSources.Contains(source.SourceName))
                                song.AvailableSources.Add(source.SourceName);
                        }
                        return url;
                    }
                    else
                    {
                        _healthTracker.RecordFailure(source.SourceName, sw.ElapsedMilliseconds);
                    }
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    _healthTracker.RecordFailure(source.SourceName, sw.ElapsedMilliseconds);
                    System.Diagnostics.Debug.WriteLine($"Source {source.SourceName} failed: {ex.Message}");
                }
            }

            return null;
        }

        public async Task<List<Song>> SearchWithAllSources(string keyword, int limit = 30)
        {
            var providers = _registry.GetAvailableProviders();
            var allSongs = new List<Song>();

            var tasks = providers.Select(async provider =>
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    var songs = await provider.SearchSongsAsync(keyword, limit);
                    sw.Stop();
                    if (songs != null && songs.Count > 0)
                    {
                        foreach (var song in songs)
                        {
                            song.Source = provider.SourceName;
                        }
                        _healthTracker.RecordSuccess(provider.SourceName, sw.ElapsedMilliseconds);
                    }
                    else
                    {
                        _healthTracker.RecordFailure(provider.SourceName, sw.ElapsedMilliseconds);
                    }
                    return songs ?? new List<Song>();
                }
                catch
                {
                    sw.Stop();
                    _healthTracker.RecordFailure(provider.SourceName, sw.ElapsedMilliseconds);
                    return new List<Song>();
                }
            });

            var results = await Task.WhenAll(tasks);

            foreach (var songs in results)
            {
                allSongs.AddRange(songs);
            }

            var deduplicatedSongs = allSongs
                .GroupBy(s => s.Title + s.ArtistName)
                .Select(g => g.First())
                .Take(limit)
                .ToList();

            return deduplicatedSongs;
        }
    }
}
