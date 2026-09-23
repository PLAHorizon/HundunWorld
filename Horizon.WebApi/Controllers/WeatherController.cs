using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Horizon.Share.VMs;
using Horizon.WebApi.Configs;

namespace Horizon.WebApi.Controllers
{
    /// <summary>
    /// 天气服务：聚合 Open-Meteo 免费数据（无需 API Key），
    /// 供鸿蒙端 WebView 天气页调用，匿名可访问。
    /// </summary>
    [ApiGroup(ApiGroupName.Basic)]
    [ApiController]
    [Route("[controller]")]
    public class WeatherController : ControllerBase
    {
        private static readonly HttpClient _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

        private const string OpenMeteoBaseUrl = "https://api.open-meteo.com/v1";
        private const string GeocodingBaseUrl = "https://geocoding-api.open-meteo.com/v1/search";

        private readonly ILogger<WeatherController> _logger;

        public WeatherController(ILogger<WeatherController> logger)
        {
            _logger = logger;
        }

        // ==================== 端点 ====================

        /// <summary>
        /// 获取当前天气。
        /// GET Weather/current?city=昆明 或 Weather/current?lat=24.9&lon=102.8&name=斗南
        /// </summary>
        [HttpGet("current")]
        public async Task<ResultVM<WeatherCurrentDto>> GetCurrentAsync([FromQuery] string? city,
            [FromQuery] double? lat, [FromQuery] double? lon, [FromQuery] string? name)
        {
            var result = new ResultVM<WeatherCurrentDto>();
            try
            {
                var coords = ResolvePlace(city, lat, lon, name);
                var forecast = await FetchOpenMeteoAsync(coords.Lat, coords.Lon, 3);
                if (forecast == null)
                {
                    result.ErrorMessage = "获取天气数据失败，请稍后重试";
                    return result;
                }

                result.Data = BuildCurrentDto(coords.Name, coords.Province, forecast);
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取当前天气失败: City={City}", city);
                result.ErrorMessage = "获取天气数据失败，请稍后重试";
            }
            return result;
        }

        /// <summary>
        /// 获取天气预报（含逐小时 + 每日）。
        /// GET Weather/forecast?city=昆明&days=7 或 lat/lon 直查
        /// </summary>
        [HttpGet("forecast")]
        public async Task<ResultVM<WeatherForecastDto>> GetForecastAsync([FromQuery] string? city, [FromQuery] int days = 7,
            [FromQuery] double? lat = null, [FromQuery] double? lon = null, [FromQuery] string? name = null)
        {
            var result = new ResultVM<WeatherForecastDto>();
            try
            {
                days = Math.Clamp(days, 1, 10);
                var coords = ResolvePlace(city, lat, lon, name);
                var forecast = await FetchOpenMeteoAsync(coords.Lat, coords.Lon, days);
                if (forecast == null)
                {
                    result.ErrorMessage = "获取天气预报失败，请稍后重试";
                    return result;
                }

                result.Data = BuildForecastDto(coords.Name, coords.Province, forecast);
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取天气预报失败: City={City}", city);
                result.ErrorMessage = "获取天气预报失败，请稍后重试";
            }
            return result;
        }

        /// <summary>
        /// 获取农事提醒。
        /// GET Weather/agricultural-alerts?city=昆明
        /// </summary>
        [HttpGet("agricultural-alerts")]
        public async Task<ResultVM<WeatherAgriAlertsDto>> GetAgriAlertsAsync([FromQuery] string? city,
            [FromQuery] double? lat, [FromQuery] double? lon, [FromQuery] string? name)
        {
            var result = new ResultVM<WeatherAgriAlertsDto>();
            try
            {
                var coords = ResolvePlace(city, lat, lon, name);
                var forecast = await FetchOpenMeteoAsync(coords.Lat, coords.Lon, 3);
                if (forecast == null)
                {
                    result.ErrorMessage = "获取农事提醒失败，请稍后重试";
                    return result;
                }

                result.Data = new WeatherAgriAlertsDto
                {
                    City = coords.Name,
                    Province = coords.Province,
                    Alerts = GenerateAgriAlerts(forecast),
                    UpdateTime = DateTime.Now
                };
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取农事提醒失败: City={City}", city);
                result.ErrorMessage = "获取农事提醒失败，请稍后重试";
            }
            return result;
        }

        /// <summary>
        /// 获取支持的城市列表（供前端搜索切换）。
        /// GET Weather/cities
        /// </summary>
        [HttpGet("cities")]
        public ResultVM<List<string>> GetCitiesAsync()
        {
            var result = new ResultVM<List<string>>();
            result.Data = CityCoords.Keys.Select(k => k).ToList();
            result.IsSuccess = true;
            return result;
        }

        /// <summary>
        /// 地点搜索（支持城市 / 区县 / 乡镇 / 自然村）。
        /// 本地表（51 城市 + 云南全境区县 + 花卉村镇）与 Open-Meteo Geocoding（GeoNames 村镇库）合并。
        /// GET Weather/search?keyword=斗南
        /// </summary>
        [HttpGet("search")]
        public async Task<ResultVM<List<WeatherPlaceDto>>> SearchAsync([FromQuery] string? keyword, [FromQuery] int count = 12)
        {
            var result = new ResultVM<List<WeatherPlaceDto>>();
            try
            {
                var key = (keyword ?? string.Empty).Trim();
                if (key.Length < 2)
                {
                    result.Data = new List<WeatherPlaceDto>();
                    result.IsSuccess = true;
                    return result;
                }
                count = Math.Clamp(count, 1, 20);
                result.Data = await SearchPlacesAsync(key, count);
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "地点搜索失败: Keyword={Keyword}", keyword);
                result.ErrorMessage = "地点搜索失败，请稍后重试";
            }
            return result;
        }

        // ==================== 地点搜索 ====================

        /** 用户选择过的地点缓存（地名 → 坐标），越用越全 */
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (string Province, string City, double Lat, double Lon)> PlaceCache =
            new(StringComparer.OrdinalIgnoreCase);

        private static async Task<List<WeatherPlaceDto>> SearchPlacesAsync(string key, int count)
        {
            var list = new List<WeatherPlaceDto>();

            // 1. 本地城市表：精确 > 前缀 > 包含
            foreach (var kv in CityCoords)
            {
                if (kv.Key.Equals(key, StringComparison.OrdinalIgnoreCase) ||
                    (kv.Key.Length >= 2 && (kv.Key.StartsWith(key) || key.Contains(kv.Key))))
                {
                    list.Add(new WeatherPlaceDto { Name = kv.Key, Province = kv.Value.Province, City = "", Lat = kv.Value.Lat, Lon = kv.Value.Lon, Kind = "city" });
                }
            }

            // 2. 本地区县表（云南全境）+ 村镇表
            foreach (var kv in DistrictCoords)
            {
                if (kv.Key.Equals(key, StringComparison.OrdinalIgnoreCase) ||
                    (key.Length >= 2 && (kv.Key.StartsWith(key) || key.Contains(kv.Key) || kv.Key.Contains(key))))
                {
                    list.Add(new WeatherPlaceDto { Name = kv.Key, Province = kv.Value.Province, City = kv.Value.City, Lat = kv.Value.Lat, Lon = kv.Value.Lon, Kind = "district" });
                }
            }
            foreach (var kv in VillageCoords)
            {
                if (kv.Key.Contains(key) || key.Contains(kv.Key))
                {
                    list.Add(new WeatherPlaceDto { Name = kv.Key, Province = kv.Value.Province, City = kv.Value.City, Lat = kv.Value.Lat, Lon = kv.Value.Lon, Kind = "village" });
                }
            }

            // 3. 缓存表（用户选过的地点直接命中）
            foreach (var kv in PlaceCache)
            {
                if (kv.Key.Contains(key) || key.Contains(kv.Key))
                {
                    list.Add(new WeatherPlaceDto { Name = kv.Key, Province = kv.Value.Province, City = kv.Value.City, Lat = kv.Value.Lat, Lon = kv.Value.Lon, Kind = "cache" });
                }
            }

            // 4. 远程 GeoNames（中国村镇库，覆盖本地表之外的乡镇/自然村）
            try
            {
                var remote = await FetchGeocodingAsync(key, count);
                foreach (var g in remote)
                {
                    // 与本地结果名称+坐标邻近去重
                    if (list.Any(p => p.Name == g.Name || (Math.Abs(p.Lat - g.Latitude) < 0.05 && Math.Abs(p.Lon - g.Longitude) < 0.05)))
                        continue;
                    list.Add(new WeatherPlaceDto
                    {
                        Name = g.Name,
                        Province = g.Admin1 ?? "",
                        City = g.Admin2 ?? "",
                        Lat = g.Latitude,
                        Lon = g.Longitude,
                        Kind = g.FeatureCode == "PPL" ? "village" : "place"
                    });
                }
            }
            catch
            {
                // 远程失败不影响本地结果
            }

            // 排序：精确命中 > 城市 > 区县 > 村镇；截断数量
            var ordered = list
                .OrderBy(p => p.Name.Equals(key, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(p => KindRank(p.Kind))
                .Take(count)
                .ToList();

            return ordered;
        }

        private static int KindRank(string kind) => kind switch
        {
            "city" => 0,
            "district" => 1,
            "village" => 2,
            _ => 3
        };

        private static async Task<List<GeoNameResult>> FetchGeocodingAsync(string keyword, int count)
        {
            var url = $"{GeocodingBaseUrl}?name={Uri.EscapeDataString(keyword)}&count={count}&language=zh";
            var json = await _httpClient.GetStringAsync(url);
            var resp = JsonSerializer.Deserialize<GeoSearchResponse>(json);
            var results = resp?.Results ?? new List<GeoNameResult>();
            // 只保留中国境内结果
            return results.Where(r => r.CountryCode == "CN").ToList();
        }

        private class GeoSearchResponse
        {
            [JsonPropertyName("results")]
            public List<GeoNameResult>? Results { get; set; }
        }

        private class GeoNameResult
        {
            [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
            [JsonPropertyName("latitude")] public double Latitude { get; set; }
            [JsonPropertyName("longitude")] public double Longitude { get; set; }
            [JsonPropertyName("country_code")] public string CountryCode { get; set; } = string.Empty;
            [JsonPropertyName("admin1")] public string? Admin1 { get; set; }
            [JsonPropertyName("admin2")] public string? Admin2 { get; set; }
            [JsonPropertyName("feature_code")] public string? FeatureCode { get; set; }
        }

        // ==================== 数据获取 ====================

        /// <summary>
        /// 地点解析：优先坐标直查（自然村），其次缓存表 → 城市表 → 区县表 → 村镇表 → 模糊匹配 → 默认。
        /// </summary>
        private static (string Name, string Province, double Lat, double Lon) ResolvePlace(string? city,
            double? lat, double? lon, string? name)
        {
            // 坐标直查（前端搜索选中自然村后传 lat/lon）
            if (lat is not null && lon is not null &&
                lat is >= -90 and <= 90 && lon is >= -180 and <= 180)
            {
                var displayName = string.IsNullOrWhiteSpace(name) ? "当前位置" : name!.Trim();
                var province = string.Empty;
                var city0 = string.Empty;
                // 尝试从缓存补全上下文
                if (!string.IsNullOrWhiteSpace(name) && PlaceCache.TryGetValue(name!.Trim(), out var cached))
                {
                    province = cached.Province;
                    city0 = cached.City;
                    // 缓存坐标与传入坐标明显不一致时以传入为准
                }
                if (province.Length == 0)
                {
                    province = GuessProvince(lat.Value, lon.Value);
                }
                // 记入缓存，后续按名直查
                if (!string.IsNullOrWhiteSpace(name))
                    PlaceCache[name!.Trim()] = (province, city0, lat.Value, lon.Value);
                return (displayName, province, lat.Value, lon.Value);
            }

            return ResolveCity(city);
        }

        private static (string Name, string Province, double Lat, double Lon) ResolveCity(string? city)
        {
            var name = string.IsNullOrWhiteSpace(city) ? DefaultCity : city.Trim();

            if (CityCoords.TryGetValue(name, out var coord))
                return (name, coord.Province, coord.Lat, coord.Lon);

            // 用户选过的地点（自然村/乡镇，含坐标缓存）
            if (PlaceCache.TryGetValue(name, out var cachedPlace))
                return (name, cachedPlace.Province, cachedPlace.Lat, cachedPlace.Lon);

            // 区县表精确命中（如"呈贡"、"晋宁"）
            if (DistrictCoords.TryGetValue(name, out var dist))
                return (name, dist.Province, dist.Lat, dist.Lon);

            // 村镇表精确命中（如"斗南"）
            if (VillageCoords.TryGetValue(name, out var vil))
                return (name, vil.Province, vil.Lat, vil.Lon);

            // 支持 "昆明市"、"云南昆明"、"呈贡区"、"斗南村" 等模糊匹配
            foreach (var kv in CityCoords)
            {
                if (kv.Key.Length >= 2 && name.Contains(kv.Key))
                    return (kv.Key, kv.Value.Province, kv.Value.Lat, kv.Value.Lon);
            }
            foreach (var kv in DistrictCoords)
            {
                if (name.Contains(kv.Key))
                    return (name, kv.Value.Province, kv.Value.Lat, kv.Value.Lon);
            }
            foreach (var kv in VillageCoords)
            {
                if (name.Contains(kv.Key))
                    return (name, kv.Value.Province, kv.Value.Lat, kv.Value.Lon);
            }

            var def = CityCoords[DefaultCity];
            return (name, def.Province, def.Lat, def.Lon);
        }

        /// <summary>按坐标粗略判断省份（国内主要花卉产区优先）</summary>
        private static string GuessProvince(double lat, double lon)
        {
            // 云南范围粗判（21-29N, 97-106E）
            if (lat is >= 21 and < 29.5 && lon is >= 97.5 and < 106.5)
                return "云南";
            if (lat is >= 39 and < 54 && lon is >= 73 and < 136) return "中国";
            return "";
        }

        private static async Task<OpenMeteoResponse?> FetchOpenMeteoAsync(double lat, double lon, int days)
        {
            var url = $"{OpenMeteoBaseUrl}/forecast" +
                      $"?latitude={lat.ToString(CultureInfo.InvariantCulture)}" +
                      $"&longitude={lon.ToString(CultureInfo.InvariantCulture)}" +
                      $"&current=temperature_2m,relative_humidity_2m,apparent_temperature,weather_code,wind_speed_10m,wind_direction_10m,surface_pressure,cloud_cover,is_day,uv_index" +
                      $"&hourly=temperature_2m,weather_code,relative_humidity_2m,wind_speed_10m,precipitation_probability" +
                      $"&daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_sum,wind_speed_10m_max,uv_index_max,sunrise,sunset" +
                      $"&timezone=Asia/Shanghai&forecast_days={days}";

            var json = await _httpClient.GetStringAsync(url);
            return JsonSerializer.Deserialize<OpenMeteoResponse>(json);
        }

        private static WeatherCurrentDto BuildCurrentDto(string city, string province, OpenMeteoResponse resp)
        {
            var c = resp.Current;
            var (_, conditionText) = MapWmoCode(c.WeatherCode, c.IsDay != 1);

            return new WeatherCurrentDto
            {
                City = city,
                Province = province,
                Temperature = (int)Math.Round(c.Temperature2m),
                ApparentTemperature = (int)Math.Round(c.ApparentTemperature),
                Humidity = c.RelativeHumidity2m,
                WindSpeed = c.WindSpeed10m,
                WindDirection = GetWindDirection(c.WindDirection10m),
                ConditionText = conditionText,
                WmoCode = c.WeatherCode,
                Pressure = c.SurfacePressure,
                CloudCover = c.CloudCover,
                UvIndex = c.UvIndex,
                IsDay = c.IsDay == 1,
                UpdateTime = DateTime.Now
            };
        }

        private static WeatherForecastDto BuildForecastDto(string city, string province, OpenMeteoResponse resp)
        {
            var dto = new WeatherForecastDto
            {
                City = city,
                Province = province,
                Current = BuildCurrentDto(city, province, resp),
                Daily = new List<WeatherDayDto>(),
                Hourly = new List<WeatherHourDto>(),
                UpdateTime = DateTime.Now
            };

            if (resp.Daily?.Time != null)
            {
                for (int i = 0; i < resp.Daily.Time.Length; i++)
                {
                    if (!DateTime.TryParse(resp.Daily.Time[i], out var date)) continue;
                    var (_, condition) = MapWmoCode(resp.Daily.WeatherCode[i], false);
                    dto.Daily.Add(new WeatherDayDto
                    {
                        Date = date.ToString("yyyy-MM-dd"),
                        WeekDay = GetWeekDay(date),
                        Condition = condition,
                        WmoCode = resp.Daily.WeatherCode[i],
                        TemperatureMax = (int)Math.Round(resp.Daily.Temperature2mMax[i]),
                        TemperatureMin = (int)Math.Round(resp.Daily.Temperature2mMin[i]),
                        Precipitation = resp.Daily.PrecipitationSum?[i] ?? 0,
                        WindSpeedMax = resp.Daily.WindSpeed10mMax?[i] ?? 0,
                        UvIndex = resp.Daily.UvIndexMax?.Length > i ? (int)Math.Round(resp.Daily.UvIndexMax[i]) : 0,
                        Sunrise = resp.Daily.Sunrise?.Length > i ? FormatTime(resp.Daily.Sunrise[i]) : "",
                        Sunset = resp.Daily.Sunset?.Length > i ? FormatTime(resp.Daily.Sunset[i]) : ""
                    });
                }
            }

            if (resp.Hourly?.Time != null)
            {
                var count = Math.Min(resp.Hourly.Time.Length, 48);
                for (int i = 0; i < count; i++)
                {
                    if (!DateTime.TryParse(resp.Hourly.Time[i], out var time)) continue;
                    var (_, condition) = MapWmoCode(resp.Hourly.WeatherCode[i], time.Hour < 6 || time.Hour > 18);
                    dto.Hourly.Add(new WeatherHourDto
                    {
                        Time = time.ToString("MM-dd HH:mm"),
                        Hour = time.Hour,
                        Temperature = (int)Math.Round(resp.Hourly.Temperature2m[i]),
                        Condition = condition,
                        WmoCode = resp.Hourly.WeatherCode[i],
                        Humidity = resp.Hourly.RelativeHumidity2m[i],
                        WindSpeed = resp.Hourly.WindSpeed10m[i],
                        Precipitation = resp.Hourly.PrecipitationProbability?[i] ?? 0
                    });
                }
            }

            return dto;
        }

        private static List<AgriAlertDto> GenerateAgriAlerts(OpenMeteoResponse resp)
        {
            var alerts = new List<AgriAlertDto>();

            var current = resp.Current;
            var today = resp.Daily?.Time?.Length > 0
                ? (resp.Daily.PrecipitationSum?[0] ?? 0) : 0;
            var tomorrow = resp.Daily?.Time?.Length > 1
                ? (resp.Daily.PrecipitationSum?[1] ?? 0) : 0;
            var uvToday = resp.Daily?.UvIndexMax?.Length > 0 ? resp.Daily.UvIndexMax[0] : 0;
            var windMax = resp.Daily?.WindSpeed10mMax?.Length > 0 ? resp.Daily.WindSpeed10mMax[0] : 0;
            var tempMax = resp.Daily?.Temperature2mMax?.Length > 0 ? resp.Daily.Temperature2mMax[0] : 0;
            var tempMin = resp.Daily?.Temperature2mMin?.Length > 0 ? resp.Daily.Temperature2mMin[0] : 0;
            var humidity = current.RelativeHumidity2m;

            if (uvToday >= 7)
                alerts.Add(new AgriAlertDto { Type = "sun", Level = "high", Title = "紫外线较强", Description = "中午时段注意遮阳防晒，避免花卉幼苗灼伤" });
            else if (uvToday >= 5)
                alerts.Add(new AgriAlertDto { Type = "sun", Level = "medium", Title = "紫外线中等", Description = "午后适当遮阴，浇水宜在清晨或傍晚" });

            if (tomorrow > 1)
                alerts.Add(new AgriAlertDto { Type = "rain", Level = "high", Title = "明日有降雨", Description = "提前检查排水沟渠，防止田间积水沤根" });
            else if (today > 1)
                alerts.Add(new AgriAlertDto { Type = "rain", Level = "medium", Title = "今日有降雨", Description = "露天花卉注意防倒伏，暂缓施肥作业" });

            if (humidity < 40)
                alerts.Add(new AgriAlertDto { Type = "dry", Level = "high", Title = "空气干燥", Description = "及时补水灌溉，叶面喷水增加空气湿度" });
            else if (humidity >= 80)
                alerts.Add(new AgriAlertDto { Type = "humidity", Level = "medium", Title = "湿度较高", Description = "加强通风排湿，警惕灰霉病等病害发生" });

            if (tempMax >= 35)
                alerts.Add(new AgriAlertDto { Type = "heat", Level = "high", Title = "高温预警", Description = "正午高温时段遮阳降温，避免暴晒灼伤" });
            else if (tempMin <= 5)
                alerts.Add(new AgriAlertDto { Type = "cold", Level = "high", Title = "低温预警", Description = "夜间注意防寒防冻，覆盖保温材料" });

            if (windMax >= 30)
                alerts.Add(new AgriAlertDto { Type = "wind", Level = "medium", Title = "风力较大", Description = "加固大棚与支架，暂缓喷施叶面肥" });

            if (alerts.Count == 0)
                alerts.Add(new AgriAlertDto { Type = "ok", Level = "good", Title = "天气适宜", Description = "当前气候条件适宜花卉生长，可正常进行栽培管理" });

            return alerts;
        }

        // ==================== 工具方法 ====================

        private static (WeatherType Type, string Text) MapWmoCode(int code, bool isNight = false)
        {
            return code switch
            {
                0 => (WeatherType.Sunny, isNight ? "晴夜" : "晴"),
                1 or 2 => (WeatherType.Cloudy, isNight ? "少云" : "多云"),
                3 => (WeatherType.Cloudy, "阴"),
                45 or 48 => (WeatherType.Fog, "雾"),
                51 or 53 or 55 => (WeatherType.Rain, "毛毛雨"),
                56 or 57 => (WeatherType.Rain, "冻雨"),
                61 or 63 or 65 => (WeatherType.Rain, "降雨"),
                66 or 67 => (WeatherType.Rain, "强降雨"),
                71 or 73 or 75 => (WeatherType.Snow, "降雪"),
                77 => (WeatherType.Snow, "雪粒"),
                80 or 81 or 82 => (WeatherType.Rain, "阵雨"),
                85 or 86 => (WeatherType.Snow, "阵雪"),
                95 or 96 or 99 => (WeatherType.Thunder, "雷暴"),
                _ => (WeatherType.Sunny, "晴")
            };
        }

        private static string GetWindDirection(double degrees)
        {
            var directions = new[] { "北风", "东北风", "东风", "东南风", "南风", "西南风", "西风", "西北风" };
            var index = (int)Math.Round(degrees / 45.0) % 8;
            return directions[index];
        }

        private static string FormatTime(string isoTime)
        {
            return DateTime.TryParse(isoTime, out var dt) ? dt.ToString("HH:mm") : isoTime;
        }

        private static string GetWeekDay(DateTime date)
        {
            var today = DateTime.Today;
            var diff = (date.Date - today.Date).Days;
            if (diff == 0) return "今天";
            if (diff == 1) return "明天";
            if (diff == 2) return "后天";
            var names = new[] { "周日", "周一", "周二", "周三", "周四", "周五", "周六" };
            return names[(int)date.DayOfWeek];
        }

        private enum WeatherType { Sunny, Cloudy, Rain, Snow, Thunder, Fog }

        // ==================== 城市坐标表 ====================

        private const string DefaultCity = "昆明";

        private static readonly Dictionary<string, (string Province, double Lat, double Lon)> CityCoords = new(StringComparer.OrdinalIgnoreCase)
        {
            { "北京", ("北京", 39.9042, 116.4074) },
            { "上海", ("上海", 31.2304, 121.4737) },
            { "天津", ("天津", 39.3434, 117.3616) },
            { "重庆", ("重庆", 29.5630, 106.5516) },
            { "昆明", ("云南", 25.0406, 102.7125) },
            { "曲靖", ("云南", 25.4899, 103.7963) },
            { "玉溪", ("云南", 24.3518, 102.5466) },
            { "丽江", ("云南", 26.8567, 100.2270) },
            { "大理", ("云南", 25.6065, 100.2676) },
            { "广州", ("广东", 23.1291, 113.2644) },
            { "深圳", ("广东", 22.5431, 114.0579) },
            { "珠海", ("广东", 22.2719, 113.5767) },
            { "佛山", ("广东", 23.0215, 113.1214) },
            { "东莞", ("广东", 23.0205, 113.7518) },
            { "杭州", ("浙江", 30.2741, 120.1551) },
            { "宁波", ("浙江", 29.8683, 121.5440) },
            { "温州", ("浙江", 28.0005, 120.6720) },
            { "南京", ("江苏", 32.0603, 118.7969) },
            { "苏州", ("江苏", 31.2989, 120.5853) },
            { "无锡", ("江苏", 31.4912, 120.3119) },
            { "成都", ("四川", 30.5728, 104.0668) },
            { "绵阳", ("四川", 31.4675, 104.6791) },
            { "武汉", ("湖北", 30.5928, 114.3055) },
            { "西安", ("陕西", 34.3416, 108.9398) },
            { "郑州", ("河南", 34.7466, 113.6254) },
            { "长沙", ("湖南", 28.2282, 112.9388) },
            { "合肥", ("安徽", 31.8206, 117.2272) },
            { "福州", ("福建", 26.0745, 119.2965) },
            { "厦门", ("福建", 24.4798, 118.0894) },
            { "南昌", ("江西", 28.6829, 115.8579) },
            { "济南", ("山东", 36.6512, 117.1201) },
            { "青岛", ("山东", 36.0671, 120.3826) },
            { "沈阳", ("辽宁", 41.8057, 123.4315) },
            { "大连", ("辽宁", 38.9140, 121.6147) },
            { "长春", ("吉林", 43.8171, 125.3235) },
            { "哈尔滨", ("黑龙江", 45.8038, 126.5340) },
            { "石家庄", ("河北", 38.0428, 114.5149) },
            { "太原", ("山西", 37.8706, 112.5489) },
            { "呼和浩特", ("内蒙古", 40.8414, 111.7519) },
            { "南宁", ("广西", 22.8240, 108.3275) },
            { "海口", ("海南", 20.0440, 110.3320) },
            { "三亚", ("海南", 18.2528, 109.5120) },
            { "贵阳", ("贵州", 26.6470, 106.6302) },
            { "兰州", ("甘肃", 36.0611, 103.8343) },
            { "西宁", ("青海", 36.6171, 101.7782) },
            { "银川", ("宁夏", 38.4872, 106.2309) },
            { "乌鲁木齐", ("新疆", 43.8256, 87.6168) },
            { "拉萨", ("西藏", 29.6500, 91.1409) },
            { "台北", ("台湾", 25.0330, 121.5654) },
            { "香港", ("香港", 22.2793, 114.1630) },
            { "澳门", ("澳门", 22.2024, 113.5499) }
        };

        /// <summary>云南全境区县坐标表（花卉项目核心运营区，搜索下沉到区县）</summary>
        private static readonly Dictionary<string, (string Province, string City, double Lat, double Lon)> DistrictCoords = new(StringComparer.OrdinalIgnoreCase)
        {
            // 昆明市
            { "呈贡", ("云南", "昆明市", 24.8865, 102.8014) },
            { "五华", ("云南", "昆明市", 25.0430, 102.7073) },
            { "盘龙", ("云南", "昆明市", 25.1170, 102.7500) },
            { "官渡", ("云南", "昆明市", 25.0200, 102.7430) },
            { "西山", ("云南", "昆明市", 25.0380, 102.6620) },
            { "东川", ("云南", "昆明市", 26.0820, 103.1880) },
            { "晋宁", ("云南", "昆明市", 24.6700, 102.5960) },
            { "安宁", ("云南", "昆明市", 24.9130, 102.4790) },
            { "富民", ("云南", "昆明市", 25.2200, 102.4980) },
            { "宜良", ("云南", "昆明市", 24.9200, 103.1420) },
            { "石林", ("云南", "昆明市", 24.7720, 103.2720) },
            { "嵩明", ("云南", "昆明市", 25.3280, 103.0370) },
            { "禄劝", ("云南", "昆明市", 25.5520, 102.4740) },
            { "寻甸", ("云南", "昆明市", 25.5550, 103.2570) },
            // 玉溪市（花卉主产区：红塔/通海/江川/澄江）
            { "红塔", ("云南", "玉溪市", 24.3520, 102.5470) },
            { "江川", ("云南", "玉溪市", 24.2900, 102.7550) },
            { "澄江", ("云南", "玉溪市", 24.6820, 102.9080) },
            { "通海", ("云南", "玉溪市", 24.1070, 102.7600) },
            { "华宁", ("云南", "玉溪市", 24.1930, 102.9290) },
            { "易门", ("云南", "玉溪市", 24.6710, 102.1630) },
            { "峨山", ("云南", "玉溪市", 24.1700, 102.4050) },
            { "新平", ("云南", "玉溪市", 24.0690, 101.9690) },
            { "元江", ("云南", "玉溪市", 23.5960, 101.9990) },
            // 曲靖市
            { "麒麟", ("云南", "曲靖市", 25.4970, 103.7960) },
            { "沾益", ("云南", "曲靖市", 25.6010, 103.8220) },
            { "马龙", ("云南", "曲靖市", 25.4270, 103.5780) },
            { "富源", ("云南", "曲靖市", 25.6720, 104.2570) },
            { "罗平", ("云南", "曲靖市", 24.8860, 104.3090) },
            { "师宗", ("云南", "曲靖市", 24.8280, 103.9910) },
            { "陆良", ("云南", "曲靖市", 25.0270, 103.6670) },
            { "会泽", ("云南", "曲靖市", 26.4180, 103.2970) },
            { "宣威", ("云南", "曲靖市", 26.2190, 104.1040) },
            // 大理州
            { "祥云", ("云南", "大理州", 25.4780, 100.5520) },
            { "宾川", ("云南", "大理州", 25.8280, 100.5760) },
            { "弥渡", ("云南", "大理州", 25.3440, 100.9080) },
            { "永平", ("云南", "大理州", 25.4640, 99.5410) },
            { "云龙", ("云南", "大理州", 25.9020, 99.3720) },
            { "洱源", ("云南", "大理州", 26.1120, 99.9510) },
            { "剑川", ("云南", "大理州", 26.5330, 99.9060) },
            { "鹤庆", ("云南", "大理州", 26.4580, 100.1770) },
            { "漾濞", ("云南", "大理州", 25.6650, 99.9560) },
            { "巍山", ("云南", "大理州", 25.2280, 100.3090) },
            { "南涧", ("云南", "大理州", 25.0430, 100.5110) },
            // 楚雄州
            { "楚雄", ("云南", "楚雄州", 25.0290, 101.5460) },
            { "双柏", ("云南", "楚雄州", 24.6880, 101.6420) },
            { "牟定", ("云南", "楚雄州", 25.3130, 101.5460) },
            { "南华", ("云南", "楚雄州", 25.2280, 101.4430) },
            { "姚安", ("云南", "楚雄州", 25.4040, 101.2420) },
            { "大姚", ("云南", "楚雄州", 25.7220, 101.3360) },
            { "永仁", ("云南", "楚雄州", 26.0570, 101.6710) },
            { "元谋", ("云南", "楚雄州", 25.7040, 101.8770) },
            { "武定", ("云南", "楚雄州", 25.5490, 102.4040) },
            { "禄丰", ("云南", "楚雄州", 25.1480, 102.0780) },
            // 红河州
            { "蒙自", ("云南", "红河州", 23.3660, 103.3640) },
            { "个旧", ("云南", "红河州", 23.3590, 103.1590) },
            { "开远", ("云南", "红河州", 23.7140, 103.2660) },
            { "弥勒", ("云南", "红河州", 24.4120, 103.4290) },
            { "建水", ("云南", "红河州", 23.6220, 102.8270) },
            { "石屏", ("云南", "红河州", 23.7140, 102.4860) },
            { "泸西", ("云南", "红河州", 24.5320, 103.7670) },
            { "元阳", ("云南", "红河州", 23.2220, 102.8340) },
            { "绿春", ("云南", "红河州", 22.9930, 102.3920) },
            { "金平", ("云南", "红河州", 22.7950, 103.2270) },
            { "屏边", ("云南", "红河州", 22.9840, 103.6860) },
            { "河口", ("云南", "红河州", 22.5090, 103.9390) },
            // 其它州市首府/区县
            { "昭通", ("云南", "昭通市", 27.3380, 103.7170) },
            { "水富", ("云南", "昭通市", 28.6300, 104.4160) },
            { "鲁甸", ("云南", "昭通市", 27.1920, 103.5580) },
            { "保山", ("云南", "保山市", 25.1120, 99.1620) },
            { "腾冲", ("云南", "保山市", 25.0210, 98.4970) },
            { "施甸", ("云南", "保山市", 24.6820, 99.1880) },
            { "龙陵", ("云南", "保山市", 24.5870, 98.6900) },
            { "昌宁", ("云南", "保山市", 24.8280, 99.6050) },
            { "古城区", ("云南", "丽江市", 26.8770, 100.2350) },
            { "玉龙", ("云南", "丽江市", 26.8210, 100.2290) },
            { "永胜", ("云南", "丽江市", 26.6840, 100.7510) },
            { "华坪", ("云南", "丽江市", 26.3290, 101.2660) },
            { "宁蒗", ("云南", "丽江市", 27.2810, 100.8510) },
            { "思茅", ("云南", "普洱市", 22.7890, 100.9720) },
            { "宁洱", ("云南", "普洱市", 23.0620, 101.0460) },
            { "墨江", ("云南", "普洱市", 23.4320, 101.6820) },
            { "景东", ("云南", "普洱市", 24.4470, 100.8340) },
            { "景谷", ("云南", "普洱市", 23.4970, 100.7020) },
            { "镇沅", ("云南", "普洱市", 24.0050, 101.1080) },
            { "江城", ("云南", "普洱市", 22.5850, 101.8620) },
            { "孟连", ("云南", "普洱市", 22.3900, 99.5860) },
            { "澜沧", ("云南", "普洱市", 22.5570, 99.9320) },
            { "西盟", ("云南", "普洱市", 22.5760, 99.5940) },
            { "临翔", ("云南", "临沧市", 23.8860, 100.0890) },
            { "凤庆", ("云南", "临沧市", 24.5800, 99.9280) },
            { "云县", ("云南", "临沧市", 24.4430, 100.1260) },
            { "永德", ("云南", "临沧市", 24.0270, 99.2590) },
            { "镇康", ("云南", "临沧市", 23.7620, 98.8260) },
            { "双江", ("云南", "临沧市", 23.4720, 99.7870) },
            { "耿马", ("云南", "临沧市", 23.5380, 99.3970) },
            { "沧源", ("云南", "临沧市", 23.1490, 99.2460) },
            { "景洪", ("云南", "西双版纳州", 22.0010, 100.7980) },
            { "勐海", ("云南", "西双版纳州", 21.9620, 100.4490) },
            { "勐腊", ("云南", "西双版纳州", 21.4810, 101.5670) },
            { "芒市", ("云南", "德宏州", 24.4340, 98.5890) },
            { "瑞丽", ("云南", "德宏州", 24.0120, 97.8560) },
            { "梁河", ("云南", "德宏州", 24.8060, 98.2970) },
            { "盈江", ("云南", "德宏州", 24.7050, 97.9320) },
            { "陇川", ("云南", "德宏州", 24.1830, 97.7920) },
            { "泸水", ("云南", "怒江州", 25.8500, 98.8560) },
            { "福贡", ("云南", "怒江州", 26.9020, 98.8880) },
            { "贡山", ("云南", "怒江州", 27.7390, 98.6660) },
            { "兰坪", ("云南", "怒江州", 26.4540, 99.4170) },
            { "香格里拉", ("云南", "迪庆州", 27.8260, 99.7030) },
            { "德钦", ("云南", "迪庆州", 28.4870, 98.9120) },
            { "维西", ("云南", "迪庆州", 27.1770, 99.2880) },
            { "文山", ("云南", "文山州", 23.3690, 104.2350) },
            { "砚山", ("云南", "文山州", 23.6060, 104.3370) },
            { "西畴", ("云南", "文山州", 23.4370, 104.6740) },
            { "麻栗坡", ("云南", "文山州", 23.1260, 104.7030) },
            { "马关", ("云南", "文山州", 23.0130, 104.3960) },
            { "丘北", ("云南", "文山州", 24.0420, 104.1960) },
            { "广南", ("云南", "文山州", 24.0460, 105.0660) },
            { "富宁", ("云南", "文山州", 23.6260, 105.6310) }
        };

        /// <summary>精选花卉产业村镇坐标表（自然村级，后续由搜索缓存动态扩充）</summary>
        private static readonly Dictionary<string, (string Province, string City, double Lat, double Lon)> VillageCoords = new(StringComparer.OrdinalIgnoreCase)
        {
            // 昆明呈贡·亚洲最大花卉交易市场所在地
            { "斗南", ("云南", "昆明市呈贡区", 24.9047, 102.7997) },
            { "梅子村", ("云南", "昆明市呈贡区", 24.8930, 102.8280) },
            { "龙城", ("云南", "昆明市呈贡区", 24.8700, 102.8210) },
            // 晋宁花卉基地（昆阳/宝峰/晋城）
            { "昆阳", ("云南", "昆明市晋宁区", 24.6700, 102.5960) },
            { "宝峰", ("云南", "昆明市晋宁区", 24.5580, 102.6230) },
            { "晋城", ("云南", "昆明市晋宁区", 24.6020, 102.7180) },
            // 玉溪花卉带
            { "研和", ("云南", "玉溪市红塔区", 24.2500, 102.6470) },
            { "大营街", ("云南", "玉溪市红塔区", 24.3000, 102.5080) },
            { "纳古", ("云南", "玉溪市通海县", 24.1140, 102.7300) },
            { "河西", ("云南", "玉溪市通海县", 24.1520, 102.6440) },
            { "江城镇", ("云南", "玉溪市江川区", 24.2900, 102.7830) },
            { "路居", ("云南", "玉溪市江川区", 24.4320, 102.8640) }
        };

        // ==================== Open-Meteo 响应模型 ====================

        private class OpenMeteoResponse
        {
            [JsonPropertyName("current")]
            public OpenMeteoCurrent Current { get; set; } = new();

            [JsonPropertyName("hourly")]
            public OpenMeteoHourly? Hourly { get; set; }

            [JsonPropertyName("daily")]
            public OpenMeteoDaily? Daily { get; set; }
        }

        private class OpenMeteoCurrent
        {
            [JsonPropertyName("temperature_2m")] public double Temperature2m { get; set; }
            [JsonPropertyName("relative_humidity_2m")] public int RelativeHumidity2m { get; set; }
            [JsonPropertyName("apparent_temperature")] public double ApparentTemperature { get; set; }
            [JsonPropertyName("weather_code")] public int WeatherCode { get; set; }
            [JsonPropertyName("wind_speed_10m")] public double WindSpeed10m { get; set; }
            [JsonPropertyName("wind_direction_10m")] public double WindDirection10m { get; set; }
            [JsonPropertyName("surface_pressure")] public double SurfacePressure { get; set; }
            [JsonPropertyName("cloud_cover")] public double CloudCover { get; set; }
            [JsonPropertyName("is_day")] public int IsDay { get; set; }
            [JsonPropertyName("uv_index")] public double UvIndex { get; set; }
        }

        private class OpenMeteoHourly
        {
            [JsonPropertyName("time")] public string[]? Time { get; set; }
            [JsonPropertyName("temperature_2m")] public double[]? Temperature2m { get; set; }
            [JsonPropertyName("weather_code")] public int[]? WeatherCode { get; set; }
            [JsonPropertyName("relative_humidity_2m")] public int[]? RelativeHumidity2m { get; set; }
            [JsonPropertyName("wind_speed_10m")] public double[]? WindSpeed10m { get; set; }
            [JsonPropertyName("precipitation_probability")] public int[]? PrecipitationProbability { get; set; }
        }

        private class OpenMeteoDaily
        {
            [JsonPropertyName("time")] public string[]? Time { get; set; }
            [JsonPropertyName("weather_code")] public int[]? WeatherCode { get; set; }
            [JsonPropertyName("temperature_2m_max")] public double[]? Temperature2mMax { get; set; }
            [JsonPropertyName("temperature_2m_min")] public double[]? Temperature2mMin { get; set; }
            [JsonPropertyName("precipitation_sum")] public double[]? PrecipitationSum { get; set; }
            [JsonPropertyName("wind_speed_10m_max")] public double[]? WindSpeed10mMax { get; set; }
            [JsonPropertyName("uv_index_max")] public double[]? UvIndexMax { get; set; }
            [JsonPropertyName("sunrise")] public string[]? Sunrise { get; set; }
            [JsonPropertyName("sunset")] public string[]? Sunset { get; set; }
        }
    }

    // ==================== 返回 DTO ====================

    /// <summary>当前天气</summary>
    public class WeatherCurrentDto
    {
        public string City { get; set; } = string.Empty;
        public string Province { get; set; } = string.Empty;
        public int Temperature { get; set; }
        public int ApparentTemperature { get; set; }
        public int Humidity { get; set; }
        public double WindSpeed { get; set; }
        public string WindDirection { get; set; } = string.Empty;
        public string ConditionText { get; set; } = string.Empty;
        public int WmoCode { get; set; }
        public double Pressure { get; set; }
        public double CloudCover { get; set; }
        public double UvIndex { get; set; }
        public bool IsDay { get; set; } = true;
        public DateTime UpdateTime { get; set; }
    }

    /// <summary>每日天气</summary>
    public class WeatherDayDto
    {
        public string Date { get; set; } = string.Empty;
        public string WeekDay { get; set; } = string.Empty;
        public string Condition { get; set; } = string.Empty;
        public int WmoCode { get; set; }
        public int TemperatureMax { get; set; }
        public int TemperatureMin { get; set; }
        public double Precipitation { get; set; }
        public double WindSpeedMax { get; set; }
        public int UvIndex { get; set; }
        public string Sunrise { get; set; } = string.Empty;
        public string Sunset { get; set; } = string.Empty;
    }

    /// <summary>逐小时天气</summary>
    public class WeatherHourDto
    {
        public string Time { get; set; } = string.Empty;
        public int Hour { get; set; }
        public int Temperature { get; set; }
        public string Condition { get; set; } = string.Empty;
        public int WmoCode { get; set; }
        public int Humidity { get; set; }
        public double WindSpeed { get; set; }
        public int Precipitation { get; set; }
    }

    /// <summary>天气预报</summary>
    public class WeatherForecastDto
    {
        public string City { get; set; } = string.Empty;
        public string Province { get; set; } = string.Empty;
        public WeatherCurrentDto Current { get; set; } = new();
        public List<WeatherDayDto> Daily { get; set; } = new();
        public List<WeatherHourDto> Hourly { get; set; } = new();
        public DateTime UpdateTime { get; set; }
    }

    /// <summary>农事提醒条目</summary>
    public class AgriAlertDto
    {
        public string Type { get; set; } = string.Empty;
        public string Level { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }

    /// <summary>农事提醒结果</summary>
    public class WeatherAgriAlertsDto
    {
        public string City { get; set; } = string.Empty;
        public string Province { get; set; } = string.Empty;
        public List<AgriAlertDto> Alerts { get; set; } = new();
        public DateTime UpdateTime { get; set; }
    }

    /// <summary>地点搜索结果（城市/区县/乡镇/自然村）</summary>
    public class WeatherPlaceDto
    {
        public string Name { get; set; } = string.Empty;
        public string Province { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
        public double Lat { get; set; }
        public double Lon { get; set; }
        /// <summary>city=城市 / district=区县 / village=村镇 / place=远程地点 / cache=缓存</summary>
        public string Kind { get; set; } = string.Empty;
    }
}
