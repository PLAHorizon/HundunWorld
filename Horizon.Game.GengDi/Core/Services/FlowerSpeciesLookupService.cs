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

        /// <summary>
        /// 离线兜底品种目录：与后端 FlowerSpeciesCatalog（22 品种，2001~2022）完全对齐，
        /// /FlowerSpecies/list 请求失败时使用，保证发布表单始终有正确的品种可选。
        /// </summary>
        private static readonly Dictionary<int, string> DefaultSpecies = new()
        {
            { 2001, "白菊" },
            { 2002, "白玫瑰" },
            { 2003, "粉玫瑰" },
            { 2004, "扶郎" },
            { 2005, "红豆" },
            { 2006, "红玫瑰" },
            { 2007, "桔梗" },
            { 2008, "康乃馨(多头)" },
            { 2009, "康乃馨(红色单头)" },
            { 2010, "蓝星花" },
            { 2011, "龙竹(60公分)" },
            { 2012, "满天星(白色)" },
            { 2013, "乒乓菊" },
            { 2014, "双头白百合" },
            { 2015, "双头粉百合" },
            { 2016, "双头黄百合" },
            { 2017, "勿忘我" },
            { 2018, "向日葵" },
            { 2019, "小雏菊" },
            { 2020, "绣球花" },
            { 2021, "洋甘菊" },
            { 2022, "紫罗兰" }
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

                    // 注意：空列表也视为失败——旧版服务端会返回 200+空数组（行情 Grain 未播种时），
                    // 若当作有效结果会清空缓存且跳过兜底，导致发布表单品种下拉为空。
                    if (result?.IsSuccess == true && result.Data is { Count: > 0 })
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
