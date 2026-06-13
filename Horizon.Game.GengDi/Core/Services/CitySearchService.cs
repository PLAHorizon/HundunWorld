using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Horizon.Game.GengDi.Models;

namespace Horizon.Game.GengDi.Core.Services
{
    public class CitySearchService
    {
        private static readonly HttpClient _httpClient = new HttpClient(SslConfiguration.CreateStandardHandler())
        {
            Timeout = TimeSpan.FromSeconds(8)
        };

        private static readonly HttpClient _nominatimClient = new HttpClient(SslConfiguration.CreateStandardHandler())
        {
            Timeout = TimeSpan.FromSeconds(8)
        };

        private static readonly HttpClient _photonClient = new HttpClient(SslConfiguration.CreateStandardHandler())
        {
            Timeout = TimeSpan.FromSeconds(6)
        };

        static CitySearchService()
        {
            _nominatimClient.DefaultRequestHeaders.UserAgent.ParseAdd("HundunWorld/1.0 (hundredworld@outlook.com)");
            _nominatimClient.DefaultRequestHeaders.AcceptLanguage.ParseAdd("zh-CN,zh;q=0.9");

            int id = 100000000;
            foreach (var kvp in AllCities)
            {
                _localCache.Add(new CityInfo
                {
                    LocationId = (id++).ToString(),
                    Name = kvp.Name,
                    Province = kvp.Province,
                    Admin2 = kvp.Admin2 ?? string.Empty,
                    Admin3 = kvp.Admin3 ?? string.Empty,
                    Admin4 = kvp.Admin4 ?? string.Empty,
                    Town = kvp.Town ?? string.Empty,
                    Village = kvp.Village ?? string.Empty,
                    Latitude = kvp.Lat,
                    Longitude = kvp.Lon,
                    AdminLevel = kvp.AdminLevel ?? string.Empty
                });
            }
        }

        private const string NominatimSearchUrl = "https://nominatim.openstreetmap.org/search";
        private const string NominatimReverseUrl = "https://nominatim.openstreetmap.org/reverse";
        private const string PhotonSearchUrl = "https://photon.komoot.io/api/";
        private const string OpenMeteoGeocodeUrl = "https://geocoding-api.open-meteo.com/v1/search";

        private static readonly List<CityInfo> _localCache = new();
        private static readonly Dictionary<string, CityInfo> _apiCache = new();
        private static readonly Dictionary<string, CitySearchResult> _searchCache = new();

        private static int _nextId = 200000000;
        private static DateTime _lastNominatimRequest = DateTime.MinValue;
        private static readonly TimeSpan NominatimRateLimit = TimeSpan.FromSeconds(1.1);

        private static bool IsValidChinaCoordinate(double lat, double lon)
        {
            const double minLat = 3.0, maxLat = 55.0;
            const double minLon = 70.0, maxLon = 140.0;
            if (Math.Abs(lat) < 0.0001 && Math.Abs(lon) < 0.0001) return false;
            if (double.IsNaN(lat) || double.IsNaN(lon)) return false;
            if (lat < minLat || lat > maxLat || lon < minLon || lon > maxLon) return false;
            return true;
        }

        private enum SearchLevel { Unknown, Province, City, District, Town, Village }

        private static readonly string[] VillageSuffixes = { "村", "庄", "屯", "寨", "社区", "组", "湾", "坡", "沟", "坝", "坊", "营", "圩", "墩" };
        private static readonly string[] TownSuffixes = { "镇", "乡", "街道", "苏木", "民族乡", "区" };
        private static readonly string[] DistrictSuffixes = { "县", "旗", "自治县", "林区", "特区" };
        private static readonly string[] CitySuffixes = { "市", "盟", "自治州", "地区" };
        private static readonly string[] ProvinceSuffixes = { "省", "自治区", "特别行政区" };

        private static SearchLevel DetectSearchLevel(string keyword)
        {
            var kw = keyword.Trim();
            if (VillageSuffixes.Any(s => kw.EndsWith(s))) return SearchLevel.Village;
            if (TownSuffixes.Any(s => kw.EndsWith(s))) return SearchLevel.Town;
            if (DistrictSuffixes.Any(s => kw.EndsWith(s))) return SearchLevel.District;
            if (CitySuffixes.Any(s => kw.EndsWith(s))) return SearchLevel.City;
            if (ProvinceSuffixes.Any(s => kw.EndsWith(s))) return SearchLevel.Province;
            return SearchLevel.Unknown;
        }

        private static string SearchLevelText(SearchLevel level) => level switch
        {
            SearchLevel.Village => "村级",
            SearchLevel.Town => "乡镇级",
            SearchLevel.District => "县级",
            SearchLevel.City => "市级",
            SearchLevel.Province => "省级",
            _ => "自动检测"
        };

        private static string DetectAdminLevel(CityInfo city)
        {
            if (!string.IsNullOrWhiteSpace(city.Village)) return "村";
            if (!string.IsNullOrWhiteSpace(city.Town)) return "乡镇";
            if (!string.IsNullOrWhiteSpace(city.Admin3) && city.Admin3 != city.Name) return "县/区";
            if (!string.IsNullOrWhiteSpace(city.Admin2) && city.Admin2 != city.Name) return "市";
            if (!string.IsNullOrWhiteSpace(city.Province)) return "省";
            return "市";
        }

        private static int ScoreMatch(CityInfo city, string keyword, SearchLevel level)
        {
            var kw = keyword.Trim();
            int score = 0;

            if (city.Name == kw) score += 100;
            else if (city.Name.StartsWith(kw)) score += 60;
            else if (city.Name.Contains(kw)) score += 30;
            else if (FuzzyMatch(city.Name, kw)) score += 15;

            if (!string.IsNullOrWhiteSpace(city.Village) && city.Village.Contains(kw)) score += 50;
            if (!string.IsNullOrWhiteSpace(city.Town) && city.Town.Contains(kw)) score += 40;
            if (!string.IsNullOrWhiteSpace(city.Admin4) && city.Admin4.Contains(kw)) score += 30;
            if (!string.IsNullOrWhiteSpace(city.Admin3) && city.Admin3.Contains(kw)) score += 20;
            if (!string.IsNullOrWhiteSpace(city.Admin2) && city.Admin2.Contains(kw)) score += 10;
            if (!string.IsNullOrWhiteSpace(city.Province) && city.Province.Contains(kw)) score += 5;

            if (level == SearchLevel.Village && !string.IsNullOrWhiteSpace(city.Village) && city.Village.Contains(kw)) score += 80;
            if (level == SearchLevel.Town && !string.IsNullOrWhiteSpace(city.Town) && city.Town.Contains(kw)) score += 70;
            if (level == SearchLevel.District && !string.IsNullOrWhiteSpace(city.Admin3) && city.Admin3.Contains(kw)) score += 60;
            if (level == SearchLevel.City && !string.IsNullOrWhiteSpace(city.Admin2) && city.Admin2.Contains(kw)) score += 50;

            if (kw.Contains(' ') || kw.Contains('·'))
            {
                var parts = kw.Split(new[] { ' ', '·' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var part in parts)
                {
                    if (city.Name.Contains(part)) score += 20;
                    if (!string.IsNullOrWhiteSpace(city.Province) && city.Province.Contains(part)) score += 10;
                    if (!string.IsNullOrWhiteSpace(city.Admin2) && city.Admin2.Contains(part)) score += 10;
                    if (!string.IsNullOrWhiteSpace(city.Admin3) && city.Admin3.Contains(part)) score += 10;
                    if (!string.IsNullOrWhiteSpace(city.Town) && city.Town.Contains(part)) score += 10;
                    if (!string.IsNullOrWhiteSpace(city.Village) && city.Village.Contains(part)) score += 10;
                }
            }

            return score;
        }

        private static bool FuzzyMatch(string source, string target)
        {
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(target)) return false;
            if (source.Length < 2 || target.Length < 2) return false;

            var sClean = source.Replace("市", "").Replace("县", "").Replace("区", "").Replace("镇", "").Replace("乡", "").Replace("村", "");
            var tClean = target.Replace("市", "").Replace("县", "").Replace("区", "").Replace("镇", "").Replace("乡", "").Replace("村", "");

            if (sClean == tClean) return true;
            if (sClean.StartsWith(tClean) || tClean.StartsWith(sClean)) return true;
            if (sClean.Length >= 2 && tClean.Length >= 2)
            {
                int match = 0;
                foreach (char c in tClean)
                    if (sClean.Contains(c)) match++;
                if ((double)match / tClean.Length > 0.7) return true;
            }
            return false;
        }

        public static async Task<List<CityInfo>> SearchCitiesAsync(string keyword, int limit = 20)
        {
            var result = await SearchCitiesDetailedAsync(keyword, limit);
            return result.Cities;
        }

        public static async Task<CitySearchResult> SearchCitiesDetailedAsync(string keyword, int limit = 20)
        {
            var sw = Stopwatch.StartNew();
            var result = new CitySearchResult { Query = keyword ?? string.Empty };

            if (string.IsNullOrWhiteSpace(keyword))
            {
                result.Cities = _localCache.Take(limit).ToList();
                result.HasExactMatch = true;
                result.ElapsedMs = sw.Elapsed.TotalMilliseconds;
                return result;
            }

            var cacheKey = keyword.Trim();
            if (_searchCache.TryGetValue(cacheKey, out var cached))
            {
                var cachedResult = cached with { ElapsedMs = sw.Elapsed.TotalMilliseconds };
                return cachedResult;
            }

            var kw = keyword.Trim();
            var level = DetectSearchLevel(kw);
            result.DetectedLevel = SearchLevelText(level);

            var scored = new List<(CityInfo City, int Score)>();

            foreach (var city in _localCache)
            {
                var score = ScoreMatch(city, kw, level);
                if (score > 0)
                    scored.Add((city, score));
            }

            foreach (var city in _apiCache.Values)
            {
                var score = ScoreMatch(city, kw, level);
                if (score > 0)
                {
                    if (!scored.Any(x => x.City.Latitude == city.Latitude && x.City.Longitude == city.Longitude))
                        scored.Add((city, score));
                }
            }

            var results = scored.OrderByDescending(x => x.Score).Select(x => x.City).Take(limit).ToList();
            result.HasExactMatch = results.Any(c => c.Name == kw || c.Name.StartsWith(kw));

            if (results.Count < limit)
            {
                var remaining = limit - results.Count;
                await SearchViaExternalApisAsync(kw, remaining, results, limit, level);
            }

            if (results.Count == 0)
            {
                var fallback = ApplyHierarchicalFallback(kw, level);
                if (fallback != null)
                {
                    fallback.IsFallback = true;
                    fallback.FallbackMessage = $"未找到\"{kw}\"的精确匹配，已回退至上级区域\"{fallback.Name}\"";
                    results.Add(fallback);
                    result.HasFallbackResults = true;
                }
            }

            foreach (var city in results)
            {
                if (string.IsNullOrWhiteSpace(city.AdminLevel))
                    city.AdminLevel = DetectAdminLevel(city);
            }

            result.Cities = results.Take(limit).ToList();

            if (result.Cities.Count == 0)
                result.SearchMessage = $"未找到\"{kw}\"相关位置，请尝试更详细的名称（如添加省/市前缀）";
            else if (result.HasFallbackResults)
                result.SearchMessage = result.Cities.First().FallbackMessage;
            else if (!result.HasExactMatch && result.Cities.Count > 0)
                result.SearchMessage = $"未找到\"{kw}\"的精确匹配，显示相关结果";

            sw.Stop();
            result.ElapsedMs = sw.Elapsed.TotalMilliseconds;

            if (_searchCache.Count < 200)
                _searchCache[cacheKey] = result with { };

            return result;
        }

        private static CityInfo? ApplyHierarchicalFallback(string keyword, SearchLevel level)
        {
            var kw = keyword.Trim();
            var cleanKw = kw;
            var suffixes = new[] { "村", "庄", "屯", "寨", "社区", "镇", "乡", "街道", "苏木", "县", "区", "旗", "市", "省" };
            foreach (var s in suffixes)
            {
                if (cleanKw.EndsWith(s) && cleanKw.Length > s.Length)
                {
                    cleanKw = cleanKw[..^s.Length];
                    break;
                }
            }

            if (level == SearchLevel.Village)
            {
                var townMatch = _localCache.FirstOrDefault(c =>
                    c.Town.Contains(cleanKw) || c.Admin3.Contains(cleanKw) || c.Name.Contains(cleanKw));
                if (townMatch != null) return townMatch;

                var countyMatch = _localCache.FirstOrDefault(c =>
                    c.Admin3.Contains(cleanKw) || c.Name.Contains(cleanKw));
                if (countyMatch != null) return countyMatch;
            }

            if (level == SearchLevel.Town)
            {
                var countyMatch = _localCache.FirstOrDefault(c =>
                    c.Admin3.Contains(cleanKw) || c.Name.Contains(cleanKw));
                if (countyMatch != null) return countyMatch;
            }

            if (level == SearchLevel.District)
            {
                var cityMatch = _localCache.FirstOrDefault(c =>
                    c.Admin2.Contains(cleanKw) || c.Name.Contains(cleanKw));
                if (cityMatch != null) return cityMatch;
            }

            if (level == SearchLevel.Unknown)
            {
                var anyMatch = _localCache.FirstOrDefault(c =>
                    c.Name.Contains(cleanKw) || c.Admin2.Contains(cleanKw) || c.Admin3.Contains(cleanKw));
                if (anyMatch != null) return anyMatch;
            }

            return null;
        }

        private static async Task SearchViaExternalApisAsync(string keyword, int remaining, List<CityInfo> results, int limit, SearchLevel level)
        {
            const int apiLimit = 50;

            var nominatimTask = SearchViaNominatimAsync(keyword, apiLimit, level);
            var photonTask = SearchViaPhotonAsync(keyword, apiLimit);

            var allTasks = new List<Task<List<CityInfo>>> { nominatimTask, photonTask };
            var merged = new List<CityInfo>();

            while (allTasks.Count > 0)
            {
                var completed = await Task.WhenAny(allTasks);
                allTasks.Remove(completed);

                try
                {
                    var apiResults = await completed;
                    foreach (var city in apiResults)
                    {
                        if (!IsValidChinaCoordinate(city.Latitude, city.Longitude))
                            continue;

                        if (!merged.Any(r => r.Latitude == city.Latitude && r.Longitude == city.Longitude))
                        {
                            merged.Add(city);
                            _apiCache[city.LocationId] = city;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[CitySearch] API failed: {ex.Message}");
                }
            }

            foreach (var city in merged)
            {
                if (!results.Any(r => r.Latitude == city.Latitude && r.Longitude == city.Longitude))
                    results.Add(city);
            }

            if (results.Count < limit)
            {
                try
                {
                    var fallback = await SearchViaOpenMeteoAsync(keyword, apiLimit);
                    foreach (var city in fallback)
                    {
                        if (!IsValidChinaCoordinate(city.Latitude, city.Longitude))
                            continue;
                        if (!results.Any(r => r.Latitude == city.Latitude && r.Longitude == city.Longitude))
                        {
                            results.Add(city);
                            _apiCache[city.LocationId] = city;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[CitySearch] OpenMeteo failed: {ex.Message}");
                }
            }
        }

        public static CityInfo? GetByLocationId(string locationId)
        {
            if (string.IsNullOrEmpty(locationId))
                return _localCache.FirstOrDefault();

            var city = _localCache.FirstOrDefault(c => c.LocationId == locationId);
            if (city != null) return city;

            if (_apiCache.TryGetValue(locationId, out var apiCity))
                return apiCity;

            if (locationId.StartsWith("101") && locationId.Length == 9)
                return _localCache.FirstOrDefault(c => c.Name == "北京") ?? _localCache.FirstOrDefault();

            return _localCache.FirstOrDefault();
        }

        public static CityInfo GetDefaultCity()
        {
            return _localCache.FirstOrDefault(c => c.Name == "北京")
                ?? _localCache.FirstOrDefault()
                ?? new CityInfo { Name = "北京", Province = "", Latitude = 39.9042, Longitude = 116.4074, LocationId = "101010100" };
        }

        public static CityInfo FindNearestCity(double lat, double lon)
        {
            CityInfo? nearest = null;
            double minDist = double.MaxValue;

            foreach (var city in _localCache)
            {
                var dLat = city.Latitude - lat;
                var dLon = city.Longitude - lon;
                var dist = dLat * dLat + dLon * dLon;
                if (dist < minDist)
                {
                    minDist = dist;
                    nearest = city;
                }
            }

            foreach (var city in _apiCache.Values)
            {
                var dLat = city.Latitude - lat;
                var dLon = city.Longitude - lon;
                var dist = dLat * dLat + dLon * dLon;
                if (dist < minDist)
                {
                    minDist = dist;
                    nearest = city;
                }
            }

            return nearest ?? GetDefaultCity();
        }

        public static async Task<CityInfo?> ReverseGeocodeAsync(double lat, double lon)
        {
            var cacheKey = $"reverse_{lat:F4}_{lon:F4}";
            if (_apiCache.TryGetValue(cacheKey, out var cached))
                return cached;

            try
            {
                await EnforceNominatimRateLimitAsync();
                var url = $"{NominatimReverseUrl}?lat={lat.ToString(CultureInfo.InvariantCulture)}" +
                          $"&lon={lon.ToString(CultureInfo.InvariantCulture)}&format=json&addressdetails=1&accept-language=zh";
                var json = await _nominatimClient.GetStringAsync(url);
                var result = JsonSerializer.Deserialize<NominatimPlace>(json);

                if (result?.Address == null) return null;

                var cityInfo = ParseNominatimAddress(result, lat, lon);
                _apiCache[cacheKey] = cityInfo;
                _apiCache[cityInfo.LocationId] = cityInfo;
                return cityInfo;
            }
            catch { return null; }
        }

        private static async Task EnforceNominatimRateLimitAsync()
        {
            var elapsed = DateTime.UtcNow - _lastNominatimRequest;
            if (elapsed < NominatimRateLimit)
                await Task.Delay(NominatimRateLimit - elapsed);
            _lastNominatimRequest = DateTime.UtcNow;
        }

        private static async Task<List<CityInfo>> SearchViaNominatimAsync(string keyword, int count, SearchLevel level)
        {
            await EnforceNominatimRateLimitAsync();

            var url = $"{NominatimSearchUrl}?q={Uri.EscapeDataString(keyword)}" +
                      $"&format=json&addressdetails=1&limit={Math.Clamp(count, 1, 50)}" +
                      $"&accept-language=zh&namedetails=1";

            if (level == SearchLevel.Village)
                url += "&featuretype=settlement";
            else if (level == SearchLevel.Town)
                url += "&featuretype=settlement";

            var json = await _nominatimClient.GetStringAsync(url);
            var places = JsonSerializer.Deserialize<List<NominatimPlace>>(json);

            var results = new List<CityInfo>();
            if (places == null) return results;

            foreach (var place in places)
            {
                if (!double.TryParse(place.Lat, NumberStyles.Float, CultureInfo.InvariantCulture, out var lat)) continue;
                if (!double.TryParse(place.Lon, NumberStyles.Float, CultureInfo.InvariantCulture, out var lon)) continue;
                if (!IsValidChinaCoordinate(lat, lon)) continue;

                results.Add(ParseNominatimAddress(place, lat, lon));
            }
            return results;
        }

        private static async Task<List<CityInfo>> SearchViaPhotonAsync(string keyword, int limit)
        {
            var url = $"{PhotonSearchUrl}?q={Uri.EscapeDataString(keyword)}" +
                      $"&lang=zh&limit={Math.Clamp(limit, 1, 50)}";

            var json = await _photonClient.GetStringAsync(url);
            var response = JsonSerializer.Deserialize<PhotonResponse>(json);

            var results = new List<CityInfo>();
            if (response?.Features == null) return results;

            foreach (var feature in response.Features)
            {
                if (feature.Geometry?.Coordinates == null || feature.Geometry.Coordinates.Count < 2) continue;
                if (feature.Properties == null) continue;

                var lon = feature.Geometry.Coordinates[0];
                var lat = feature.Geometry.Coordinates[1];
                if (!IsValidChinaCoordinate(lat, lon)) continue;

                var addr = feature.Properties;
                var province = PickFirst(addr.State, addr.Country == "China" ? null : addr.Country);
                var admin2 = PickFirst(addr.City, addr.County);
                var admin3 = PickFirst(addr.District, addr.County);
                var town = PickFirst(addr.City, addr.District, addr.County);
                var village = PickFirst(addr.Hamlet, addr.Suburb, addr.Village, addr.Name);
                var name = PickFirst(addr.Hamlet, addr.Suburb, addr.Village, addr.Name, addr.City, addr.District);

                if (string.IsNullOrWhiteSpace(name))
                    name = feature.Properties.Name ?? "未知位置";

                var locationId = $"photon_{Interlocked.Increment(ref _nextId)}";
                results.Add(new CityInfo
                {
                    LocationId = locationId,
                    Name = name,
                    Province = province ?? string.Empty,
                    Admin2 = admin2 ?? string.Empty,
                    Admin3 = admin3 ?? string.Empty,
                    Admin4 = town ?? string.Empty,
                    Town = town ?? string.Empty,
                    Village = village ?? string.Empty,
                    Latitude = lat,
                    Longitude = lon
                });
            }
            return results;
        }

        private static CityInfo ParseNominatimAddress(NominatimPlace place, double lat, double lon)
        {
            var addr = place.Address!;
            var locationId = $"nomi_{Interlocked.Increment(ref _nextId)}";

            var province = PickFirst(addr.State, addr.Province);
            var admin2 = PickCity(addr);
            var admin3 = PickFirst(addr.County, addr.CityDistrict, addr.District);
            var town = PickFirst(addr.Town, addr.Township, addr.Suburb, addr.Borough);
            var village = PickFirst(addr.Village, addr.Hamlet, addr.Neighbourhood, addr.Subdivision);
            var name = PickFirst(addr.Village, addr.Hamlet, addr.Town, addr.Suburb, addr.CityDistrict, addr.County, addr.City, addr.Township, place.DisplayName.Split(',')[0].Trim());

            if (string.IsNullOrWhiteSpace(name))
                name = addr.Country ?? "未知位置";
            if (!string.IsNullOrWhiteSpace(province) && province == name)
                name = admin2 != province ? admin2 : (admin3 != province ? admin3 : name);

            return new CityInfo
            {
                LocationId = locationId,
                Name = name,
                Province = province ?? string.Empty,
                Admin2 = admin2 ?? string.Empty,
                Admin3 = admin3 ?? string.Empty,
                Admin4 = town ?? string.Empty,
                Town = town ?? string.Empty,
                Village = village ?? string.Empty,
                Latitude = lat,
                Longitude = lon
            };
        }

        private static string PickCity(NominatimAddress addr)
        {
            if (!string.IsNullOrWhiteSpace(addr.City) && IsChineseCity(addr.City)) return addr.City;
            if (!string.IsNullOrWhiteSpace(addr.Town) && IsChineseCity(addr.Town)) return addr.Town;
            if (!string.IsNullOrWhiteSpace(addr.County) && IsChineseCity(addr.County)) return addr.County;
            return PickFirst(addr.City, addr.Town, addr.County) ?? string.Empty;
        }

        private static bool IsChineseCity(string name) => name.EndsWith("市");

        private static string? PickFirst(params string?[] values)
        {
            foreach (var v in values)
                if (!string.IsNullOrWhiteSpace(v)) return v.Trim();
            return null;
        }

        private static async Task<List<CityInfo>> SearchViaOpenMeteoAsync(string keyword, int count)
        {
            var url = $"{OpenMeteoGeocodeUrl}?name={Uri.EscapeDataString(keyword)}" +
                      $"&count={count}&language=zh&format=json";
            var json = await _httpClient.GetStringAsync(url);
            var response = JsonSerializer.Deserialize<GeocodingResponse>(json);

            var results = new List<CityInfo>();
            if (response?.Results == null) return results;

            foreach (var r in response.Results)
            {
                if (!string.IsNullOrEmpty(r.Country) && r.Country != "China" && r.Country != "中国") continue;
                if (!IsValidChinaCoordinate(r.Latitude, r.Longitude)) continue;

                var province = GetProvinceName(r.Admin1, r.Country);
                results.Add(new CityInfo
                {
                    LocationId = $"ometeo_{Interlocked.Increment(ref _nextId)}",
                    Name = r.Name,
                    Province = province,
                    Admin2 = r.Admin2 ?? string.Empty,
                    Admin3 = r.Admin3 ?? string.Empty,
                    Admin4 = r.Admin4 ?? string.Empty,
                    Latitude = r.Latitude,
                    Longitude = r.Longitude
                });
            }
            return results;
        }

        private static string GetProvinceName(string? admin1, string? country)
        {
            if (string.IsNullOrEmpty(admin1) || admin1 == country) return string.Empty;

            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "Beijing", "" }, { "Shanghai", "" }, { "Tianjin", "" }, { "Chongqing", "" },
                { "Hebei", "河北" }, { "Shanxi", "山西" }, { "Liaoning", "辽宁" }, { "Jilin", "吉林" },
                { "Heilongjiang", "黑龙江" }, { "Jiangsu", "江苏" }, { "Zhejiang", "浙江" },
                { "Anhui", "安徽" }, { "Fujian", "福建" }, { "Jiangxi", "江西" }, { "Shandong", "山东" },
                { "Henan", "河南" }, { "Hubei", "湖北" }, { "Hunan", "湖南" }, { "Guangdong", "广东" },
                { "Hainan", "海南" }, { "Sichuan", "四川" }, { "Guizhou", "贵州" }, { "Yunnan", "云南" },
                { "Shaanxi", "陕西" }, { "Gansu", "甘肃" }, { "Qinghai", "青海" }, { "Taiwan", "台湾" },
                { "Guangxi", "广西" }, { "Inner Mongolia", "内蒙古" }, { "Tibet", "西藏" },
                { "Ningxia", "宁夏" }, { "Xinjiang", "新疆" },
                { "Hong Kong", "香港" }, { "Macau", "澳门" },
            };
            return map.GetValueOrDefault(admin1, admin1);
        }

        private struct CityRecord
        {
            public string Name;
            public double Lat, Lon;
            public string? Province, Admin2, Admin3, Admin4, Town, Village, AdminLevel;
        }

        private static readonly CityRecord[] AllCities = BuildCityDatabase();

        private static CityRecord[] BuildCityDatabase()
        {
            var list = new List<CityRecord>();

            list.Add(new CityRecord { Name = "北京", Lat = 39.9042, Lon = 116.4074, Province = "北京", AdminLevel = "市" });
            list.Add(new CityRecord { Name = "上海", Lat = 31.2304, Lon = 121.4737, Province = "上海", AdminLevel = "市" });
            list.Add(new CityRecord { Name = "天津", Lat = 39.3434, Lon = 117.3616, Province = "天津", AdminLevel = "市" });
            list.Add(new CityRecord { Name = "重庆", Lat = 29.5630, Lon = 106.5516, Province = "重庆", AdminLevel = "市" });

            AddProvinceCities(list, "河北",
                ("石家庄", 38.0428, 114.5149), ("唐山", 39.6305, 118.1802), ("秦皇岛", 39.9354, 119.6005),
                ("邯郸", 36.6256, 114.5389), ("邢台", 37.0706, 114.5048), ("保定", 38.8739, 115.4646),
                ("张家口", 40.8244, 114.8875), ("承德", 40.9512, 117.9631), ("沧州", 38.3045, 116.8388),
                ("廊坊", 39.5380, 116.6838), ("衡水", 37.7389, 115.6702));
            AddProvinceCities(list, "山西",
                ("太原", 37.8706, 112.5489), ("大同", 40.0768, 113.3001), ("阳泉", 37.8567, 113.5805),
                ("长治", 36.1954, 113.1165), ("晋城", 35.4907, 112.8516), ("朔州", 39.3316, 112.4326),
                ("晋中", 37.6870, 112.7527), ("运城", 35.0264, 111.0070), ("忻州", 38.4161, 112.7342),
                ("临汾", 36.0882, 111.5196), ("吕梁", 37.5193, 111.1447));
            AddProvinceCities(list, "内蒙古",
                ("呼和浩特", 40.8414, 111.7519), ("包头", 40.6578, 109.8403), ("乌海", 39.6538, 106.7942),
                ("赤峰", 42.2586, 118.8887), ("通辽", 43.6529, 122.2434), ("鄂尔多斯", 39.6084, 109.7809),
                ("呼伦贝尔", 49.2116, 119.7657), ("巴彦淖尔", 40.7430, 107.3876), ("乌兰察布", 40.9939, 113.1338));
            AddProvinceCities(list, "辽宁",
                ("沈阳", 41.8057, 123.4315), ("大连", 38.9140, 121.6147), ("鞍山", 41.1078, 122.9946),
                ("抚顺", 41.8804, 123.9573), ("本溪", 41.2941, 123.7665), ("丹东", 40.0005, 124.3549),
                ("锦州", 41.0951, 121.1270), ("营口", 40.6670, 122.2349), ("阜新", 42.0216, 121.6701),
                ("辽阳", 41.2681, 123.2369), ("盘锦", 41.1200, 122.0707), ("铁岭", 42.2862, 123.8425),
                ("朝阳", 41.5745, 120.4510), ("葫芦岛", 40.7110, 120.8370));
            AddProvinceCities(list, "吉林",
                ("长春", 43.8171, 125.3235), ("吉林", 43.8378, 126.5496), ("四平", 43.1665, 124.3504),
                ("辽源", 42.8876, 125.1447), ("通化", 41.7283, 125.9399), ("白山", 41.9410, 126.4244),
                ("松原", 45.1411, 124.8251), ("白城", 45.6196, 122.8387), ("延边", 42.8913, 129.5087));
            AddProvinceCities(list, "黑龙江",
                ("哈尔滨", 45.8038, 126.5340), ("齐齐哈尔", 47.3543, 123.9182), ("鸡西", 45.2950, 130.9693),
                ("鹤岗", 47.3499, 130.2980), ("双鸭山", 46.6465, 131.1591), ("大庆", 46.5875, 125.1031),
                ("伊春", 47.7275, 128.8405), ("佳木斯", 46.7998, 130.3188), ("七台河", 45.7706, 131.0031),
                ("牡丹江", 44.5525, 129.6324), ("黑河", 50.2451, 127.5285), ("绥化", 46.6524, 126.9683));
            AddProvinceCities(list, "江苏",
                ("南京", 32.0603, 118.7969), ("无锡", 31.4912, 120.3119), ("徐州", 34.2693, 117.1859),
                ("常州", 31.8121, 119.9692), ("苏州", 31.2989, 120.5853), ("南通", 31.9812, 120.8946),
                ("连云港", 34.5967, 119.2216), ("淮安", 33.6101, 119.0153), ("盐城", 33.3496, 120.1622),
                ("扬州", 32.3936, 119.4127), ("镇江", 32.1896, 119.4248), ("泰州", 32.4555, 119.9230),
                ("宿迁", 33.9630, 118.2754));
            AddProvinceCities(list, "浙江",
                ("杭州", 30.2741, 120.1551), ("宁波", 29.8683, 121.5440), ("温州", 28.0005, 120.6720),
                ("嘉兴", 30.7460, 120.7550), ("湖州", 30.8930, 120.0868), ("绍兴", 30.0003, 120.5820),
                ("金华", 29.0784, 119.6476), ("衢州", 28.9359, 118.8742), ("舟山", 29.9853, 122.2072),
                ("台州", 28.6556, 121.4208), ("丽水", 28.4673, 119.9228));
            AddProvinceCities(list, "安徽",
                ("合肥", 31.8206, 117.2272), ("芜湖", 31.3527, 118.4332), ("蚌埠", 32.9155, 117.3893),
                ("淮南", 32.6254, 116.9995), ("马鞍山", 31.6706, 118.5061), ("淮北", 33.9548, 116.7983),
                ("铜陵", 30.9447, 117.8116), ("安庆", 30.5429, 117.0630), ("黄山", 29.7147, 118.3387),
                ("滁州", 32.3016, 118.3168), ("阜阳", 32.8896, 115.8145), ("宿州", 33.6461, 116.9644),
                ("六安", 31.7349, 116.5218), ("亳州", 33.8446, 115.7790), ("池州", 30.6647, 117.4914),
                ("宣城", 30.9407, 118.7588));
            AddProvinceCities(list, "福建",
                ("福州", 26.0745, 119.2965), ("厦门", 24.4798, 118.0894), ("莆田", 25.4540, 119.0077),
                ("三明", 26.2638, 117.6390), ("泉州", 24.8740, 118.6761), ("漳州", 24.5139, 117.6473),
                ("南平", 26.6416, 118.1783), ("龙岩", 25.0750, 117.0173), ("宁德", 26.6657, 119.5479));
            AddProvinceCities(list, "江西",
                ("南昌", 28.6829, 115.8579), ("景德镇", 29.2688, 117.1784), ("萍乡", 27.6230, 113.8545),
                ("九江", 29.7051, 116.0014), ("新余", 27.8178, 114.9173), ("鹰潭", 28.2602, 117.0692),
                ("赣州", 25.8318, 114.9350), ("吉安", 27.1134, 114.9929), ("宜春", 27.8156, 114.4165),
                ("抚州", 27.9478, 116.3581), ("上饶", 28.4549, 117.9436));
            AddProvinceCities(list, "山东",
                ("济南", 36.6512, 117.1201), ("青岛", 36.0671, 120.3826), ("淄博", 36.8131, 118.0548),
                ("枣庄", 34.8105, 117.3220), ("东营", 37.4337, 118.6747), ("烟台", 37.4645, 121.4479),
                ("潍坊", 36.7068, 119.1618), ("济宁", 35.4146, 116.5871), ("泰安", 36.1999, 117.0884),
                ("威海", 37.5131, 122.1204), ("日照", 35.4167, 119.5269), ("临沂", 35.1047, 118.3564),
                ("德州", 37.4356, 116.3593), ("聊城", 36.4570, 115.9855), ("滨州", 37.3821, 117.9728),
                ("菏泽", 35.2338, 115.4807));
            AddProvinceCities(list, "河南",
                ("郑州", 34.7466, 113.6254), ("开封", 34.7973, 114.3077), ("洛阳", 34.6194, 112.4536),
                ("平顶山", 33.7662, 113.1927), ("安阳", 36.0976, 114.3931), ("鹤壁", 35.7470, 114.2974),
                ("新乡", 35.3037, 113.9268), ("焦作", 35.2159, 113.2420), ("濮阳", 35.7620, 115.0293),
                ("许昌", 34.0357, 113.8523), ("漯河", 33.5814, 114.0165), ("三门峡", 34.7725, 111.2003),
                ("南阳", 32.9907, 112.5285), ("商丘", 34.4142, 115.6564), ("信阳", 32.1471, 114.0912),
                ("周口", 33.6258, 114.6970), ("驻马店", 33.0114, 114.0223), ("济源", 35.0670, 112.6019));
            AddProvinceCities(list, "湖北",
                ("武汉", 30.5928, 114.3055), ("黄石", 30.1995, 115.0389), ("十堰", 32.6292, 110.7979),
                ("宜昌", 30.6919, 111.2865), ("襄阳", 32.0089, 112.1223), ("鄂州", 30.3907, 114.8949),
                ("荆门", 31.0354, 112.1991), ("孝感", 30.9249, 113.9165), ("荆州", 30.3352, 112.2397),
                ("黄冈", 30.4537, 114.8723), ("咸宁", 29.8414, 114.3225), ("随州", 31.6902, 113.3826),
                ("恩施", 30.2722, 109.4885));
            AddProvinceCities(list, "湖南",
                ("长沙", 28.2282, 112.9388), ("株洲", 27.8278, 113.1340), ("湘潭", 27.8297, 112.9441),
                ("衡阳", 26.8934, 112.5718), ("邵阳", 27.2389, 111.4677), ("岳阳", 29.3571, 113.1292),
                ("常德", 29.0316, 111.6985), ("张家界", 29.1167, 110.4784), ("益阳", 28.5539, 112.3552),
                ("郴州", 25.7706, 113.0148), ("永州", 26.4203, 111.6123), ("怀化", 27.5689, 109.9992),
                ("娄底", 27.7001, 111.9936), ("湘西", 28.3118, 109.7389));
            AddProvinceCities(list, "广东",
                ("广州", 23.1291, 113.2644), ("韶关", 24.8104, 113.5976), ("深圳", 22.5431, 114.0579),
                ("珠海", 22.2719, 113.5767), ("汕头", 23.3535, 116.6822), ("佛山", 23.0215, 113.1214),
                ("江门", 22.5787, 113.0816), ("湛江", 21.2707, 110.3593), ("茂名", 21.6630, 110.9252),
                ("肇庆", 23.0472, 112.4651), ("惠州", 23.1118, 114.4168), ("梅州", 24.2886, 116.1226),
                ("汕尾", 22.7866, 115.3753), ("河源", 23.7438, 114.7004), ("阳江", 21.8583, 111.9826),
                ("清远", 23.6818, 113.0560), ("东莞", 23.0205, 113.7518), ("中山", 22.5170, 113.3926),
                ("潮州", 23.6579, 116.6219), ("揭阳", 23.5499, 116.3727), ("云浮", 22.9153, 112.0445));
            AddProvinceCities(list, "广西",
                ("南宁", 22.8240, 108.3275), ("柳州", 24.3255, 109.4155), ("桂林", 25.2736, 110.2900),
                ("梧州", 23.4769, 111.2791), ("北海", 21.4729, 109.1199), ("防城港", 21.6869, 108.3537),
                ("钦州", 21.9797, 108.6543), ("贵港", 23.1115, 109.5989), ("玉林", 22.6545, 110.1806),
                ("百色", 23.9022, 106.6184), ("贺州", 24.4036, 111.5669), ("河池", 24.6931, 108.0854),
                ("来宾", 23.7503, 109.2224), ("崇左", 22.3768, 107.3650));
            AddProvinceCities(list, "海南",
                ("海口", 20.0440, 110.3320), ("三亚", 18.2528, 109.5120), ("三沙", 16.8310, 112.3387),
                ("儋州", 19.5209, 109.5807));
            AddProvinceCities(list, "四川",
                ("成都", 30.5728, 104.0668), ("自贡", 29.3392, 104.7784), ("攀枝花", 26.5823, 101.7186),
                ("泸州", 28.8718, 105.4424), ("德阳", 31.1266, 104.3979), ("绵阳", 31.4675, 104.6791),
                ("广元", 32.4355, 105.8436), ("遂宁", 30.5326, 105.5927), ("内江", 29.5801, 105.0584),
                ("乐山", 29.5523, 103.7657), ("南充", 30.8378, 106.1107), ("眉山", 30.0757, 103.8486),
                ("宜宾", 28.7518, 104.6435), ("广安", 30.4561, 106.6330), ("达州", 31.2095, 107.4679),
                ("雅安", 29.9805, 103.0133), ("巴中", 31.8672, 106.7475), ("资阳", 30.1287, 104.6275),
                ("阿坝", 31.8994, 102.2248), ("甘孜", 30.0495, 101.9638), ("凉山", 27.8816, 102.2677));
            AddProvinceCities(list, "贵州",
                ("贵阳", 26.6470, 106.6302), ("六盘水", 26.5935, 104.8304), ("遵义", 27.7257, 106.9272),
                ("安顺", 26.2531, 105.9476), ("毕节", 27.2985, 105.3044), ("铜仁", 27.6907, 109.1896),
                ("黔西南", 25.0899, 104.9047), ("黔东南", 26.5836, 107.9839), ("黔南", 26.2540, 107.5222));
            AddProvinceCities(list, "云南",
                ("昆明", 25.0406, 102.7125), ("曲靖", 25.4899, 103.7963), ("玉溪", 24.3518, 102.5466),
                ("保山", 25.1120, 99.1618), ("昭通", 27.3383, 103.7175), ("丽江", 26.8567, 100.2270),
                ("普洱", 22.8251, 100.9662), ("临沧", 23.8867, 100.0888), ("楚雄", 25.0450, 101.5277),
                ("红河", 23.3643, 103.3746), ("文山", 23.3863, 104.2150), ("西双版纳", 22.0090, 100.7973),
                ("大理", 25.6065, 100.2676), ("德宏", 24.4331, 98.5848), ("怒江", 25.8519, 98.8566),
                ("迪庆", 27.8190, 99.7022));
            AddProvinceCities(list, "西藏",
                ("拉萨", 29.6500, 91.1409), ("日喀则", 29.2666, 88.8802), ("昌都", 31.1422, 97.1784),
                ("林芝", 29.6541, 94.3615), ("山南", 29.2368, 91.7731), ("那曲", 31.4762, 92.0523),
                ("阿里", 32.5016, 80.1050));
            AddProvinceCities(list, "陕西",
                ("西安", 34.3416, 108.9398), ("铜川", 34.8967, 108.9451), ("宝鸡", 34.3619, 107.2377),
                ("咸阳", 34.3293, 108.7091), ("渭南", 34.4999, 109.5098), ("延安", 36.5855, 109.4898),
                ("汉中", 33.0676, 107.0237), ("榆林", 38.2852, 109.7348), ("安康", 32.6847, 109.0290),
                ("商洛", 33.8727, 109.9403));
            AddProvinceCities(list, "甘肃",
                ("兰州", 36.0611, 103.8343), ("嘉峪关", 39.7720, 98.2892), ("金昌", 38.5200, 102.1881),
                ("白银", 36.5448, 104.1378), ("天水", 34.5809, 105.7249), ("武威", 37.9282, 102.6379),
                ("张掖", 38.9259, 100.4498), ("平凉", 35.5427, 106.6652), ("酒泉", 39.7324, 98.4945),
                ("庆阳", 35.7092, 107.6431), ("定西", 35.5807, 104.6251), ("陇南", 33.4005, 104.9211),
                ("临夏", 35.6012, 103.2108), ("甘南", 34.9833, 102.9114));
            AddProvinceCities(list, "青海",
                ("西宁", 36.6171, 101.7782), ("海东", 36.5027, 102.4017), ("海北", 36.9545, 100.9010),
                ("黄南", 35.5197, 102.0154), ("海南", 36.2866, 100.6204), ("果洛", 34.4716, 100.2447),
                ("玉树", 33.0063, 97.0065), ("海西", 37.3698, 97.3704));
            AddProvinceCities(list, "宁夏",
                ("银川", 38.4872, 106.2309), ("石嘴山", 38.9841, 106.3840), ("吴忠", 37.9974, 106.1984),
                ("固原", 36.0158, 106.2426), ("中卫", 37.4999, 105.1968));
            AddProvinceCities(list, "新疆",
                ("乌鲁木齐", 43.8256, 87.6168), ("克拉玛依", 45.5790, 84.8892), ("吐鲁番", 42.9480, 89.1897),
                ("哈密", 42.8184, 93.5155), ("昌吉", 44.0112, 87.2675), ("博尔塔拉", 44.9060, 82.0747),
                ("巴音郭楞", 41.7640, 86.1450), ("阿克苏", 41.1680, 80.2606), ("克孜勒苏", 39.7146, 76.1675),
                ("喀什", 39.4677, 75.9903), ("和田", 37.1104, 79.9223), ("伊犁", 43.9172, 81.3241),
                ("塔城", 46.7522, 82.9869), ("阿勒泰", 47.8488, 88.1402));

            list.Add(new CityRecord { Name = "香港", Lat = 22.2793, Lon = 114.1630, Province = "香港", AdminLevel = "特别行政区" });
            list.Add(new CityRecord { Name = "澳门", Lat = 22.2024, Lon = 113.5499, Province = "澳门", AdminLevel = "特别行政区" });
            list.Add(new CityRecord { Name = "台北", Lat = 25.0330, Lon = 121.5654, Province = "台湾", Admin2 = "台北", AdminLevel = "市" });
            list.Add(new CityRecord { Name = "高雄", Lat = 22.6200, Lon = 120.3133, Province = "台湾", Admin2 = "高雄", AdminLevel = "市" });
            list.Add(new CityRecord { Name = "台中", Lat = 24.1373, Lon = 120.6869, Province = "台湾", Admin2 = "台中", AdminLevel = "市" });

            AddDistricts(list,
                ("北京", "北京", new[] { "顺义", "大兴", "通州", "昌平", "房山", "密云", "延庆", "怀柔", "平谷", "门头沟", "海淀", "朝阳", "丰台", "东城", "西城", "石景山" },
                 new[] { 40.1303, 39.7268, 39.9090, 40.2178, 39.7487, 40.3769, 40.4654, 40.3168, 40.1407, 39.9406, 39.9590, 39.9180, 39.8585, 39.9280, 39.9120, 39.9053 },
                 new[] { 116.6548, 116.3410, 116.6575, 116.2319, 115.9934, 116.8433, 115.9850, 116.6318, 117.1120, 116.1022, 116.2980, 116.4434, 116.2867, 116.4160, 116.3660, 116.2235 }),
                ("上海", "上海", new[] { "浦东", "闵行", "宝山", "嘉定", "松江", "徐汇", "静安", "黄浦", "长宁", "虹口", "杨浦", "普陀" },
                 new[] { 31.2450, 31.1120, 31.4050, 31.3745, 31.0320, 31.1880, 31.2280, 31.2310, 31.2200, 31.2600, 31.2690, 31.2450 },
                 new[] { 121.5450, 121.3820, 121.4890, 121.2655, 121.2260, 121.4370, 121.4550, 121.4690, 121.4210, 121.4840, 121.5260, 121.3970 }),
                ("天津", "天津", new[] { "滨海", "武清", "蓟州", "宝坻" },
                 new[] { 39.0033, 39.3840, 40.0457, 39.7172 },
                 new[] { 117.6507, 117.0443, 117.4080, 117.3095 }),
                ("重庆", "重庆", new[] { "万州", "涪陵", "黔江", "永川", "合川", "江津" },
                 new[] { 30.8077, 29.7031, 29.5332, 29.3560, 29.9723, 29.2903 },
                 new[] { 108.4087, 107.3898, 108.7714, 105.9270, 106.2761, 106.2593 })
            );

            AddCounties(list);

            return list.ToArray();
        }

        private static void AddProvinceCities(List<CityRecord> list, string province, params (string Name, double Lat, double Lon)[] cities)
        {
            foreach (var c in cities)
            {
                list.Add(new CityRecord { Name = c.Name, Lat = c.Lat, Lon = c.Lon, Province = province, Admin2 = c.Name, AdminLevel = "市" });
            }
        }

        private static void AddDistricts(List<CityRecord> list, params (string Province, string City, string[] Names, double[] Lats, double[] Lons)[] districts)
        {
            foreach (var d in districts)
            {
                for (int i = 0; i < d.Names.Length; i++)
                {
                    list.Add(new CityRecord
                    {
                        Name = d.Names[i],
                        Lat = d.Lats[i],
                        Lon = d.Lons[i],
                        Province = d.Province,
                        Admin2 = d.City,
                        Admin3 = d.Names[i],
                        AdminLevel = "区"
                    });
                }
            }
        }

        private static void AddCounties(List<CityRecord> list)
        {
            var counties = new (string Province, string City, string Name, double Lat, double Lon)[]
            {
                ("河北", "石家庄", "正定", 38.1462, 114.5712),
                ("河北", "石家庄", "赵县", 37.7499, 114.8057),
                ("河北", "保定", "涞水", 39.3935, 115.7140),
                ("河北", "保定", "高阳", 38.6829, 115.7756),
                ("河北", "邯郸", "大名", 36.2866, 115.1477),
                ("河北", "唐山", "迁西", 40.2276, 118.3155),
                ("山西", "太原", "清徐", 37.6066, 112.3577),
                ("山西", "太原", "阳曲", 38.0947, 112.6730),
                ("山西", "大同", "浑源", 39.6936, 113.6724),
                ("内蒙古", "呼和浩特", "托克托", 40.2767, 111.1860),
                ("内蒙古", "包头", "固阳", 41.0310, 110.0640),
                ("辽宁", "沈阳", "法库", 42.5038, 123.4123),
                ("辽宁", "沈阳", "康平", 42.7507, 123.3460),
                ("辽宁", "大连", "庄河", 39.6885, 122.9684),
                ("辽宁", "大连", "长海", 39.2727, 122.5878),
                ("吉林", "长春", "农安", 44.4316, 125.1856),
                ("吉林", "长春", "德惠", 44.5316, 125.7056),
                ("黑龙江", "哈尔滨", "依兰", 46.2520, 129.5684),
                ("黑龙江", "哈尔滨", "宾县", 45.5750, 127.4860),
                ("江苏", "南京", "溧水", 31.6531, 119.0287),
                ("江苏", "南京", "高淳", 31.3277, 118.8916),
                ("江苏", "苏州", "昆山", 31.3852, 120.9804),
                ("江苏", "苏州", "常熟", 31.6543, 120.7525),
                ("江苏", "苏州", "张家港", 31.8753, 120.5534),
                ("江苏", "苏州", "太仓", 31.4597, 121.1292),
                ("江苏", "无锡", "江阴", 31.9104, 120.2858),
                ("江苏", "无锡", "宜兴", 31.3405, 119.8234),
                ("江苏", "常州", "溧阳", 31.4263, 119.4842),
                ("江苏", "常州", "金坛", 31.7230, 119.5979),
                ("浙江", "杭州", "桐庐", 29.7927, 119.6447),
                ("浙江", "杭州", "淳安", 29.6088, 118.9977),
                ("浙江", "杭州", "建德", 29.4712, 119.2812),
                ("浙江", "宁波", "慈溪", 30.1696, 121.2664),
                ("浙江", "宁波", "余姚", 30.0388, 121.1557),
                ("浙江", "温州", "瑞安", 27.7788, 120.6297),
                ("浙江", "温州", "乐清", 28.1076, 120.9820),
                ("浙江", "嘉兴", "海宁", 30.5107, 120.6807),
                ("浙江", "嘉兴", "桐乡", 30.6302, 120.5648),
                ("安徽", "合肥", "肥东", 31.8808, 117.4692),
                ("安徽", "合肥", "肥西", 31.7088, 117.1662),
                ("安徽", "合肥", "长丰", 32.4784, 117.1670),
                ("安徽", "合肥", "庐江", 31.2553, 117.2890),
                ("安徽", "芜湖", "南陵", 30.9187, 118.3352),
                ("福建", "福州", "闽侯", 26.1502, 118.8492),
                ("福建", "福州", "连江", 26.2002, 119.5392),
                ("福建", "泉州", "晋江", 24.7817, 118.5524),
                ("福建", "泉州", "石狮", 24.7320, 118.6480),
                ("福建", "泉州", "南安", 24.9575, 118.3863),
                ("江西", "南昌", "南昌县", 28.5536, 115.9492),
                ("江西", "南昌", "进贤", 28.3720, 116.2400),
                ("山东", "济南", "平阴", 36.2892, 116.4562),
                ("山东", "济南", "商河", 37.3112, 117.1572),
                ("山东", "青岛", "胶州", 36.2645, 120.0335),
                ("山东", "青岛", "平度", 36.7767, 119.7598),
                ("山东", "青岛", "莱西", 36.8882, 120.5172),
                ("河南", "郑州", "中牟", 34.7192, 113.9763),
                ("河南", "郑州", "巩义", 34.7482, 112.9635),
                ("河南", "郑州", "新郑", 34.3962, 113.7398),
                ("河南", "洛阳", "栾川", 33.7830, 111.6176),
                ("湖北", "武汉", "黄陂", 30.8825, 114.3752),
                ("湖北", "武汉", "新洲", 30.8420, 114.8015),
                ("湖北", "宜昌", "秭归", 30.8260, 110.6385),
                ("湖北", "襄阳", "枣阳", 32.1288, 112.7727),
                ("湖南", "长沙", "长沙县", 28.2460, 113.0800),
                ("湖南", "长沙", "浏阳", 28.1628, 113.6382),
                ("湖南", "长沙", "宁乡", 28.2544, 112.5575),
                ("广东", "广州", "从化", 23.5494, 113.5870),
                ("广东", "广州", "增城", 23.2617, 113.8110),
                ("广东", "佛山", "顺德", 22.8054, 113.2420),
                ("广东", "佛山", "南海", 23.0288, 113.1428),
                ("广东", "江门", "开平", 22.3762, 112.6984),
                ("广东", "江门", "台山", 22.2515, 112.7938),
                ("广西", "南宁", "武鸣", 23.3750, 108.2770),
                ("海南", "海口", "澄迈", 19.7385, 110.0067),
                ("四川", "成都", "双流", 30.5744, 103.9234),
                ("四川", "成都", "都江堰", 30.9880, 103.6470),
                ("四川", "成都", "彭州", 30.9902, 103.9580),
                ("四川", "成都", "邛崃", 30.4149, 103.4617),
                ("四川", "成都", "崇州", 30.6301, 103.6730),
                ("四川", "成都", "金堂", 30.8619, 104.4120),
                ("四川", "成都", "大邑", 30.5872, 103.5218),
                ("四川", "成都", "蒲江", 30.1967, 103.5062),
                ("四川", "绵阳", "江油", 31.7780, 104.7458),
                ("四川", "绵阳", "三台", 31.0920, 105.0947),
                ("四川", "德阳", "广汉", 30.9772, 104.2824),
                ("四川", "德阳", "什邡", 31.1268, 104.1673),
                ("四川", "德阳", "绵竹", 31.3380, 104.2200),
                ("四川", "宜宾", "长宁", 28.5790, 104.9210),
                ("贵州", "贵阳", "修文", 26.8420, 106.5920),
                ("贵州", "贵阳", "息烽", 27.0920, 106.7410),
                ("云南", "昆明", "安宁", 24.9390, 102.4780),
                ("云南", "昆明", "晋宁", 24.6690, 102.5930),
                ("陕西", "西安", "蓝田", 34.1513, 109.3233),
                ("陕西", "西安", "周至", 34.1633, 108.2220),
                ("陕西", "西安", "户县", 34.1080, 108.6100),
                ("甘肃", "兰州", "榆中", 35.8430, 104.1125),
                ("青海", "西宁", "湟源", 36.6820, 101.2560),
                ("宁夏", "银川", "贺兰", 38.5540, 106.3490),
                ("宁夏", "银川", "永宁", 38.2780, 106.2520),
                ("新疆", "乌鲁木齐", "乌鲁木齐县", 43.4620, 87.6280),
            };

            foreach (var c in counties)
            {
                list.Add(new CityRecord
                {
                    Name = c.Name,
                    Lat = c.Lat,
                    Lon = c.Lon,
                    Province = c.Province,
                    Admin2 = c.City,
                    Admin3 = c.Name,
                    AdminLevel = "县"
                });
            }

            var towns = new (string Province, string City, string County, string Name, double Lat, double Lon)[]
            {
                ("北京", "北京", "海淀", "中关村街道", 39.9842, 116.3164),
                ("北京", "北京", "朝阳", "望京街道", 39.9942, 116.4702),
                ("北京", "北京", "海淀", "西北旺镇", 40.0562, 116.2355),
                ("北京", "北京", "昌平", "回龙观镇", 40.0722, 116.3425),
                ("上海", "上海", "浦东", "张江镇", 31.2032, 121.5916),
                ("上海", "上海", "闵行", "莘庄镇", 31.1112, 121.3816),
                ("上海", "上海", "浦东", "川沙镇", 31.1932, 121.6976),
                ("广东", "广州", "从化", "太平镇", 23.5794, 113.5170),
                ("广东", "深圳", "深圳", "南山区", 22.5329, 113.9307),
                ("广东", "深圳", "深圳", "宝安区", 22.5553, 113.8830),
                ("江苏", "苏州", "昆山", "玉山镇", 31.3882, 120.9854),
                ("江苏", "苏州", "常熟", "虞山镇", 31.6543, 120.7525),
                ("浙江", "杭州", "桐庐", "桐君街道", 29.7927, 119.6447),
                ("浙江", "宁波", "慈溪", "浒山街道", 30.1696, 121.2664),
                ("四川", "成都", "双流", "东升街道", 30.5744, 103.9234),
                ("四川", "成都", "都江堰", "灌口街道", 30.9880, 103.6470),
                ("湖北", "武汉", "黄陂", "前川街道", 30.8825, 114.3752),
                ("湖南", "长沙", "浏阳", "淮川街道", 28.1628, 113.6382),
                ("山东", "青岛", "胶州", "阜安街道", 36.2645, 120.0335),
                ("河南", "郑州", "新郑", "新华路街道", 34.3962, 113.7398),
            };

            foreach (var t in towns)
            {
                list.Add(new CityRecord
                {
                    Name = t.Name,
                    Lat = t.Lat,
                    Lon = t.Lon,
                    Province = t.Province,
                    Admin2 = t.City,
                    Admin3 = t.County,
                    Town = t.Name,
                    AdminLevel = "乡镇"
                });
            }
        }

        private class NominatimPlace
        {
            [JsonPropertyName("place_id")]
            public long PlaceId { get; set; }

            [JsonPropertyName("lat")]
            public string Lat { get; set; } = string.Empty;

            [JsonPropertyName("lon")]
            public string Lon { get; set; } = string.Empty;

            [JsonPropertyName("display_name")]
            public string DisplayName { get; set; } = string.Empty;

            [JsonPropertyName("address")]
            public NominatimAddress? Address { get; set; }
        }

        private class NominatimAddress
        {
            [JsonPropertyName("country")]
            public string? Country { get; set; }
            [JsonPropertyName("state")]
            public string? State { get; set; }
            [JsonPropertyName("province")]
            public string? Province { get; set; }
            [JsonPropertyName("city")]
            public string? City { get; set; }
            [JsonPropertyName("county")]
            public string? County { get; set; }
            [JsonPropertyName("city_district")]
            public string? CityDistrict { get; set; }
            [JsonPropertyName("district")]
            public string? District { get; set; }
            [JsonPropertyName("town")]
            public string? Town { get; set; }
            [JsonPropertyName("township")]
            public string? Township { get; set; }
            [JsonPropertyName("suburb")]
            public string? Suburb { get; set; }
            [JsonPropertyName("borough")]
            public string? Borough { get; set; }
            [JsonPropertyName("village")]
            public string? Village { get; set; }
            [JsonPropertyName("hamlet")]
            public string? Hamlet { get; set; }
            [JsonPropertyName("neighbourhood")]
            public string? Neighbourhood { get; set; }
            [JsonPropertyName("subdivision")]
            public string? Subdivision { get; set; }
            [JsonPropertyName("road")]
            public string? Road { get; set; }
        }

        private class PhotonResponse
        {
            [JsonPropertyName("features")]
            public List<PhotonFeature>? Features { get; set; }
        }

        private class PhotonFeature
        {
            [JsonPropertyName("geometry")]
            public PhotonGeometry? Geometry { get; set; }

            [JsonPropertyName("properties")]
            public PhotonProperties? Properties { get; set; }
        }

        private class PhotonGeometry
        {
            [JsonPropertyName("coordinates")]
            public List<double>? Coordinates { get; set; }
        }

        private class PhotonProperties
        {
            [JsonPropertyName("name")]
            public string? Name { get; set; }

            [JsonPropertyName("country")]
            public string? Country { get; set; }

            [JsonPropertyName("state")]
            public string? State { get; set; }

            [JsonPropertyName("city")]
            public string? City { get; set; }

            [JsonPropertyName("county")]
            public string? County { get; set; }

            [JsonPropertyName("district")]
            public string? District { get; set; }

            [JsonPropertyName("village")]
            public string? Village { get; set; }

            [JsonPropertyName("hamlet")]
            public string? Hamlet { get; set; }

            [JsonPropertyName("suburb")]
            public string? Suburb { get; set; }
        }

        private class GeocodingResponse
        {
            [JsonPropertyName("results")]
            public List<GeocodingResult>? Results { get; set; }
        }

        private class GeocodingResult
        {
            [JsonPropertyName("id")]
            public int Id { get; set; }
            [JsonPropertyName("name")]
            public string Name { get; set; } = string.Empty;
            [JsonPropertyName("latitude")]
            public double Latitude { get; set; }
            [JsonPropertyName("longitude")]
            public double Longitude { get; set; }
            [JsonPropertyName("admin1")]
            public string? Admin1 { get; set; }
            [JsonPropertyName("admin2")]
            public string? Admin2 { get; set; }
            [JsonPropertyName("admin3")]
            public string? Admin3 { get; set; }
            [JsonPropertyName("admin4")]
            public string? Admin4 { get; set; }
            [JsonPropertyName("country")]
            public string? Country { get; set; }
        }
    }
}
