using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Horizon.Game.GengDi.Core.Services
{
    public class FlowerSpeciesLookupService
    {
        private static readonly Lazy<FlowerSpeciesLookupService> _instance =
            new(() => new FlowerSpeciesLookupService());

        public static FlowerSpeciesLookupService Instance => _instance.Value;

        private readonly Dictionary<int, string> _speciesCache = new();
        private bool _isLoaded;
        private bool _isLoading;
        private readonly object _lock = new();

        private static readonly Dictionary<int, string> DefaultSpecies = new()
        {
            { 1, "红玫瑰" },
            { 2, "百合" },
            { 3, "康乃馨" },
            { 4, "混合花束" },
            { 5, "红绿搭配" }
        };

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private FlowerSpeciesLookupService()
        {
        }

        public async Task EnsureLoadedAsync()
        {
            if (_isLoaded) return;

            lock (_lock)
            {
                if (_isLoading) return;
                _isLoading = true;
            }

            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var response = await FlowerHttpConfig.HttpClient.GetAsync($"{baseUri}FlowerSpecies/list").ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    var result = JsonSerializer.Deserialize<FlowerSpeciesListResult>(json, JsonOptions);

                    if (result?.IsSuccess == true && result.Data != null)
                    {
                        lock (_lock)
                        {
                            _speciesCache.Clear();
                            foreach (var item in result.Data)
                            {
                                _speciesCache[item.Id] = item.Name;
                            }
                            _isLoaded = true;
                            return;
                        }
                    }
                }
            }
            catch
            {
            }
            finally
            {
                _isLoading = false;
            }

            lock (_lock)
            {
                if (_speciesCache.Count == 0)
                {
                    foreach (var kvp in DefaultSpecies)
                        _speciesCache[kvp.Key] = kvp.Value;
                }
                _isLoaded = true;
            }
        }

        public string GetSpeciesName(int speciesId)
        {
            lock (_lock)
            {
                return _speciesCache.TryGetValue(speciesId, out var name) ? name : "未知品种";
            }
        }

        public int GetSpeciesId(string speciesName)
        {
            if (string.IsNullOrWhiteSpace(speciesName)) return 0;

            lock (_lock)
            {
                var kvp = _speciesCache.FirstOrDefault(x => x.Value == speciesName);
                return kvp.Key;
            }
        }

        public Dictionary<int, string> GetAllSpecies()
        {
            lock (_lock)
            {
                return new Dictionary<int, string>(_speciesCache);
            }
        }

        private sealed class FlowerSpeciesListResult
        {
            public bool IsSuccess { get; set; }
            public List<FlowerSpeciesItem> Data { get; set; }
        }

        private sealed class FlowerSpeciesItem
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
        }
    }
}
