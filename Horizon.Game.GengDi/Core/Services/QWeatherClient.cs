using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Horizon.Game.GengDi.Models;

namespace Horizon.Game.GengDi.Core.Services
{
    public class QWeatherClient
    {
        private static readonly HttpClient _httpClient = new HttpClient(SslConfiguration.CreateStandardHandler())
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

        private const string WeatherApiBaseUrl = "https://api.weatherapi.com/v1";
        private const string OpenMeteoBaseUrl = "https://api.open-meteo.com/v1";

        private static string WeatherApiKey => AppSettingsService.Instance.CurrentSettings.WeatherApiKey;

        private static bool UseWeatherApi => !string.IsNullOrWhiteSpace(WeatherApiKey);

        public static async Task<WeatherForecast?> GetWeatherForecastAsync(string locationId = "101010100", int days = 7)
        {
            try
            {
                var cityInfo = CitySearchService.GetByLocationId(locationId)
                    ?? CitySearchService.GetDefaultCity();

                return await FetchWeatherAsync(cityInfo, days).ConfigureAwait(false);
            }
            catch
            {
                return null;
            }
        }

        public static async Task<WeatherForecast?> GetWeatherForecastByCoordsAsync(double lat, double lon, string cityName, int days = 7)
        {
            try
            {
                var cityInfo = new CityInfo
                {
                    Name = cityName,
                    Province = string.Empty,
                    Latitude = lat,
                    Longitude = lon
                };
                return await FetchWeatherAsync(cityInfo, days).ConfigureAwait(false);
            }
            catch
            {
                return null;
            }
        }

        private static async Task<WeatherForecast?> FetchWeatherAsync(CityInfo cityInfo, int days)
        {
            if (UseWeatherApi)
            {
                try
                {
                    var forecast = await FetchFromWeatherApiAsync(cityInfo, days).ConfigureAwait(false);
                    if (forecast != null) return forecast;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Weather] WeatherAPI failed: {ex.Message}");
                }
            }

            try
            {
                return await FetchFromOpenMeteoAsync(cityInfo, days).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Weather] Open-Meteo failed: {ex.Message}");
                return null;
            }
        }

        private static async Task<WeatherForecast?> FetchFromWeatherApiAsync(CityInfo cityInfo, int days)
        {
            var q = $"{cityInfo.Latitude.ToString(CultureInfo.InvariantCulture)},{cityInfo.Longitude.ToString(CultureInfo.InvariantCulture)}";
            var url = $"{WeatherApiBaseUrl}/forecast.json?key={Uri.EscapeDataString(WeatherApiKey)}" +
                      $"&q={q}&days={Math.Clamp(days, 1, 7)}&lang=zh&aqi=yes&alerts=no";

            var json = await _httpClient.GetStringAsync(url).ConfigureAwait(false);
            var response = JsonSerializer.Deserialize<WeatherApiResponse>(json);

            if (response?.Current == null || response.Forecast?.ForecastDays == null)
                return null;

            var current = BuildCurrentWeather(response);
            var (weatherType, _) = MapWeatherApiCode(response.Current.Condition?.Code ?? 0);

            var dailyList = new List<DailyWeather>();
            foreach (var fd in response.Forecast.ForecastDays)
            {
                if (!DateTime.TryParse(fd.Date, out var date)) continue;

                var day = fd.Day;
                if (day == null) continue;

                var (_, dayCondition) = MapWeatherApiCode(day.Condition?.Code ?? 0);

                dailyList.Add(new DailyWeather
                {
                    Date = date,
                    WeekDay = GetWeekDay(date),
                    Condition = dayCondition,
                    TemperatureMax = (int)Math.Round(day.MaxTempC),
                    TemperatureMin = (int)Math.Round(day.MinTempC),
                    WmoCode = day.Condition?.Code ?? 0,
                    Precipitation = day.TotalPrecipMm,
                    WindSpeedMax = day.MaxWindKph,
                    Humidity = day.AvgHumidity,
                    UvIndex = (int)Math.Round(day.Uv),
                    Sunrise = fd.Astro?.Sunrise ?? string.Empty,
                    Sunset = fd.Astro?.Sunset ?? string.Empty
                });
            }

            var hourlyList = new List<HourlyForecast>();
            foreach (var fd in response.Forecast.ForecastDays)
            {
                if (fd.Hours == null) continue;

                foreach (var h in fd.Hours)
                {
                    if (!DateTime.TryParse(h.Time, out var time)) continue;

                    var (_, hourCondition) = MapWeatherApiCode(h.Condition?.Code ?? 0);
                    hourlyList.Add(new HourlyForecast
                    {
                        Time = time,
                        Temperature = (int)Math.Round(h.TempC),
                        Condition = hourCondition,
                        WmoCode = h.Condition?.Code ?? 0,
                        Humidity = h.Humidity,
                        WindSpeed = h.WindKph,
                        Precipitation = h.PrecipMm
                    });
                }
            }

            var now = DateTime.Now;
            var closestWeatherApi = hourlyList
                .OrderBy(h => Math.Abs((h.Time - now).TotalHours))
                .FirstOrDefault();
            if (closestWeatherApi != null)
                closestWeatherApi.IsCurrentHour = true;

            var precipTimeline = hourlyList
                .Where(h => h.Time >= now && h.Time <= now.AddHours(24))
                .Select(h => new PrecipitationTimelineItem
                {
                    Time = h.Time,
                    Probability = h.Precipitation
                })
                .ToList();

            var forecast = new WeatherForecast
            {
                City = cityInfo.Name,
                Province = cityInfo.Province,
                Latitude = cityInfo.Latitude,
                Longitude = cityInfo.Longitude,
                Current = current,
                Daily = dailyList,
                Hourly = hourlyList.ToArray(),
                Type = weatherType,
                LastUpdateTime = DateTime.Now,
                News = WeatherNewsService.GenerateMockNews(cityInfo.Name, weatherType, current),
                PrecipitationTimeline = precipTimeline
            };

            if (response.Current?.AirQuality != null)
            {
                var aq = response.Current.AirQuality;
                var aqiVal = aq.UsEpaIndex * 50;
                forecast.AirQuality = new AirQualityData
                {
                    Aqi = aqiVal,
                    Level = AirQualityData.GetLevel(aqiVal),
                    LevelColor = AirQualityData.GetLevelColor(aqiVal),
                    MainPollutant = "--",
                    Pm25 = aq.Pm25,
                    Pm10 = aq.Pm10,
                    O3 = aq.O3,
                    No2 = aq.No2,
                    So2 = aq.So2,
                    Co = aq.Co
                };
            }

            return forecast;
        }

        private static async Task<WeatherForecast?> FetchFromOpenMeteoAsync(CityInfo cityInfo, int days)
        {
            var url = $"{OpenMeteoBaseUrl}/forecast?latitude={cityInfo.Latitude.ToString(CultureInfo.InvariantCulture)}" +
                     $"&longitude={cityInfo.Longitude.ToString(CultureInfo.InvariantCulture)}" +
                     $"&current=temperature_2m,relative_humidity_2m,apparent_temperature,weather_code,wind_speed_10m,wind_direction_10m,surface_pressure,cloud_cover" +
                     $"&hourly=temperature_2m,weather_code,relative_humidity_2m,wind_speed_10m,precipitation,precipitation_probability" +
                     $"&daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_sum,wind_speed_10m_max,uv_index_max,sunrise,sunset" +
                     $"&timezone=Asia/Shanghai&forecast_days={days}";

            var json = await _httpClient.GetStringAsync(url).ConfigureAwait(false);
            var response = JsonSerializer.Deserialize<OpenMeteoResponse>(json);

            if (response == null) return null;

            var now = DateTime.Now;
            var isNight = now.Hour < 6 || now.Hour > 18;
            var (weatherType, conditionText) = WmoWeatherMapper.Map(response.Current.WeatherCode, isNight);

            var currentWeather = new CurrentWeather
            {
                Temperature = (int)Math.Round(response.Current.Temperature2m),
                ConditionText = conditionText,
                Humidity = response.Current.RelativeHumidity2m,
                WindSpeed = response.Current.WindSpeed10m,
                WindDirection = GetWindDirection(response.Current.WindDirection10m),
                ApparentTemperature = (int)Math.Round(response.Current.ApparentTemperature),
                UvIndex = response.Daily?.UvIndexMax?.Length > 0 ? (int)Math.Round(response.Daily.UvIndexMax[0]) : 0,
                Pressure = response.Current.SurfacePressure,
                Visibility = 10.0,
                CloudCover = response.Current.CloudCover,
                DewPoint = (int)Math.Round(response.Current.Temperature2m - (100 - response.Current.RelativeHumidity2m) / 5.0),
                WmoCode = response.Current.WeatherCode
            };

            var dailyWeathers = new List<DailyWeather>();
            if (response.Daily?.Time != null)
            {
                for (int i = 0; i < response.Daily.Time.Length; i++)
                {
                    if (DateTime.TryParse(response.Daily.Time[i], out var date))
                    {
                        var (_, dayCondition) = WmoWeatherMapper.Map(response.Daily.WeatherCode[i]);
                        dailyWeathers.Add(new DailyWeather
                        {
                            Date = date,
                            WeekDay = GetWeekDay(date),
                            Condition = dayCondition,
                            TemperatureMax = (int)Math.Round(response.Daily.Temperature2mMax[i]),
                            TemperatureMin = (int)Math.Round(response.Daily.Temperature2mMin[i]),
                            WmoCode = response.Daily.WeatherCode[i],
                            Precipitation = response.Daily.PrecipitationSum?[i] ?? 0,
                            WindSpeedMax = response.Daily.WindSpeed10mMax?[i] ?? 0,
                            Humidity = 0,
                            UvIndex = response.Daily.UvIndexMax?.Length > i ? (int)Math.Round(response.Daily.UvIndexMax[i]) : 0,
                            Sunrise = response.Daily.Sunrise?.Length > i ? FormatTime(response.Daily.Sunrise[i]) : "",
                            Sunset = response.Daily.Sunset?.Length > i ? FormatTime(response.Daily.Sunset[i]) : ""
                        });
                    }
                }
            }

            var hourlyForecasts = new List<HourlyForecast>();
            if (response.Hourly?.Time != null)
            {
                var count = Math.Min(response.Hourly.Time.Length, 48);
                for (int i = 0; i < count; i++)
                {
                    if (DateTime.TryParse(response.Hourly.Time[i], out var time))
                    {
                        var (_, hourCondition) = WmoWeatherMapper.Map(response.Hourly.WeatherCode[i]);
                        hourlyForecasts.Add(new HourlyForecast
                        {
                            Time = time,
                            Temperature = (int)Math.Round(response.Hourly.Temperature2m[i]),
                            Condition = hourCondition,
                            WmoCode = response.Hourly.WeatherCode[i],
                            Humidity = response.Hourly.RelativeHumidity2m[i],
                            WindSpeed = response.Hourly.WindSpeed10m[i],
                            Precipitation = response.Hourly.PrecipitationProbability?[i] ?? 0
                        });
                    }
                }
            }

            var nowOpenMeteo = DateTime.Now;
            var closestOpenMeteo = hourlyForecasts
                .OrderBy(h => Math.Abs((h.Time - nowOpenMeteo).TotalHours))
                .FirstOrDefault();
            if (closestOpenMeteo != null)
                closestOpenMeteo.IsCurrentHour = true;

            var precipTimeline = hourlyForecasts
                .Where(h => h.Time >= nowOpenMeteo && h.Time <= nowOpenMeteo.AddHours(24))
                .Select(h => new PrecipitationTimelineItem
                {
                    Time = h.Time,
                    Probability = h.Precipitation
                })
                .ToList();

            return new WeatherForecast
            {
                City = cityInfo.Name,
                Province = cityInfo.Province,
                Latitude = cityInfo.Latitude,
                Longitude = cityInfo.Longitude,
                Current = currentWeather,
                Daily = dailyWeathers,
                Hourly = hourlyForecasts.ToArray(),
                Type = weatherType,
                LastUpdateTime = DateTime.Now,
                News = WeatherNewsService.GenerateMockNews(cityInfo.Name, weatherType, currentWeather),
                PrecipitationTimeline = precipTimeline
            };
        }

        private static CurrentWeather BuildCurrentWeather(WeatherApiResponse response)
        {
            var c = response.Current!;
            var windKph = c.WindKph;
            return new CurrentWeather
            {
                Temperature = (int)Math.Round(c.TempC),
                ConditionText = c.Condition?.Text ?? "晴",
                Humidity = c.Humidity,
                WindSpeed = windKph,
                WindDirection = c.WindDir ?? "北风",
                ApparentTemperature = (int)Math.Round(c.FeelsLikeC),
                UvIndex = (int)Math.Round(c.Uv),
                Pressure = c.PressureMb,
                Visibility = c.VisKm,
                CloudCover = c.Cloud,
                DewPoint = (int)Math.Round(c.DewPointC),
                WmoCode = c.Condition?.Code ?? 0
            };
        }

        private static (WeatherType Type, string Text) MapWeatherApiCode(int code)
        {
            return code switch
            {
                1000 => (WeatherType.Sunny, "晴"),
                1003 => (WeatherType.Cloudy, "多云"),
                1006 or 1009 => (WeatherType.Cloudy, "阴"),
                1030 or 1135 or 1147 => (WeatherType.Fog, "雾"),
                1063 or 1150 or 1153 or 1168 or 1171 or 1180 or 1183 or 1186 or 1189
                    or 1192 or 1195 or 1198 or 1201 or 1240 or 1243 or 1246 or 1273 or 1276
                    => (WeatherType.Rain, "降雨"),
                1066 or 1069 or 1072 or 1114 or 1117 or 1210 or 1213 or 1216 or 1219
                    or 1222 or 1225 or 1237 or 1255 or 1258 or 1261 or 1264
                    => (WeatherType.Snow, "降雪"),
                1087 or 1279 or 1282 => (WeatherType.Thunder, "雷暴"),
                _ => (WeatherType.Sunny, "晴")
            };
        }

        private static string FormatTime(string isoTime)
        {
            if (DateTime.TryParse(isoTime, out var dt))
                return dt.ToString("HH:mm");
            return isoTime;
        }

        private static string GetWindDirection(double degrees)
        {
            var directions = new[] { "北风", "东北风", "东风", "东南风", "南风", "西南风", "西风", "西北风" };
            var index = (int)Math.Round(degrees / 45.0) % 8;
            return directions[index];
        }

        private static string GetWeekDay(DateTime date)
        {
            var today = DateTime.Today;
            var diff = (date - today).Days;

            if (diff == 0) return "今天";
            if (diff == 1) return "明天";
            if (diff == 2) return "后天";

            var dayNames = new[] { "周日", "周一", "周二", "周三", "周四", "周五", "周六" };
            return dayNames[(int)date.DayOfWeek];
        }

        private class WeatherApiResponse
        {
            [JsonPropertyName("current")]
            public WeatherApiCurrent? Current { get; set; }

            [JsonPropertyName("forecast")]
            public WeatherApiForecast? Forecast { get; set; }
        }

        private class WeatherApiForecast
        {
            [JsonPropertyName("forecastday")]
            public List<WeatherApiForecastDay>? ForecastDays { get; set; }
        }

        private class WeatherApiForecastDay
        {
            [JsonPropertyName("date")]
            public string Date { get; set; } = string.Empty;

            [JsonPropertyName("day")]
            public WeatherApiDay? Day { get; set; }

            [JsonPropertyName("astro")]
            public WeatherApiAstro? Astro { get; set; }

            [JsonPropertyName("hour")]
            public List<WeatherApiHour>? Hours { get; set; }
        }

        private class WeatherApiCurrent
        {
            [JsonPropertyName("temp_c")]
            public double TempC { get; set; }

            [JsonPropertyName("condition")]
            public WeatherApiCondition? Condition { get; set; }

            [JsonPropertyName("wind_kph")]
            public double WindKph { get; set; }

            [JsonPropertyName("wind_dir")]
            public string? WindDir { get; set; }

            [JsonPropertyName("humidity")]
            public int Humidity { get; set; }

            [JsonPropertyName("feelslike_c")]
            public double FeelsLikeC { get; set; }

            [JsonPropertyName("uv")]
            public double Uv { get; set; }

            [JsonPropertyName("pressure_mb")]
            public double PressureMb { get; set; }

            [JsonPropertyName("vis_km")]
            public double VisKm { get; set; }

            [JsonPropertyName("cloud")]
            public double Cloud { get; set; }

            [JsonPropertyName("dewpoint_c")]
            public double DewPointC { get; set; }

            [JsonPropertyName("air_quality")]
            public WeatherApiAirQuality? AirQuality { get; set; }
        }

        private class WeatherApiAirQuality
        {
            [JsonPropertyName("co")]
            public double Co { get; set; }
            [JsonPropertyName("no2")]
            public double No2 { get; set; }
            [JsonPropertyName("o3")]
            public double O3 { get; set; }
            [JsonPropertyName("so2")]
            public double So2 { get; set; }
            [JsonPropertyName("pm2_5")]
            public double Pm25 { get; set; }
            [JsonPropertyName("pm10")]
            public double Pm10 { get; set; }
            [JsonPropertyName("us-epa-index")]
            public int UsEpaIndex { get; set; }
            [JsonPropertyName("gb-defra-index")]
            public int GbDefraIndex { get; set; }
        }

        private class WeatherApiDay
        {
            [JsonPropertyName("maxtemp_c")]
            public double MaxTempC { get; set; }

            [JsonPropertyName("mintemp_c")]
            public double MinTempC { get; set; }

            [JsonPropertyName("condition")]
            public WeatherApiCondition? Condition { get; set; }

            [JsonPropertyName("totalprecip_mm")]
            public double TotalPrecipMm { get; set; }

            [JsonPropertyName("maxwind_kph")]
            public double MaxWindKph { get; set; }

            [JsonPropertyName("avghumidity")]
            public int AvgHumidity { get; set; }

            [JsonPropertyName("uv")]
            public double Uv { get; set; }
        }

        private class WeatherApiAstro
        {
            [JsonPropertyName("sunrise")]
            public string? Sunrise { get; set; }

            [JsonPropertyName("sunset")]
            public string? Sunset { get; set; }
        }

        private class WeatherApiHour
        {
            [JsonPropertyName("time")]
            public string Time { get; set; } = string.Empty;

            [JsonPropertyName("temp_c")]
            public double TempC { get; set; }

            [JsonPropertyName("condition")]
            public WeatherApiCondition? Condition { get; set; }

            [JsonPropertyName("humidity")]
            public int Humidity { get; set; }

            [JsonPropertyName("wind_kph")]
            public double WindKph { get; set; }

            [JsonPropertyName("precip_mm")]
            public double PrecipMm { get; set; }
        }

        private class WeatherApiCondition
        {
            [JsonPropertyName("text")]
            public string? Text { get; set; }

            [JsonPropertyName("code")]
            public int Code { get; set; }
        }

        private class OpenMeteoResponse
        {
            [JsonPropertyName("current")]
            public CurrentData Current { get; set; } = new();

            [JsonPropertyName("hourly")]
            public HourlyData? Hourly { get; set; }

            [JsonPropertyName("daily")]
            public DailyData? Daily { get; set; }
        }

        private class CurrentData
        {
            [JsonPropertyName("temperature_2m")]
            public double Temperature2m { get; set; }

            [JsonPropertyName("relative_humidity_2m")]
            public int RelativeHumidity2m { get; set; }

            [JsonPropertyName("apparent_temperature")]
            public double ApparentTemperature { get; set; }

            [JsonPropertyName("weather_code")]
            public int WeatherCode { get; set; }

            [JsonPropertyName("wind_speed_10m")]
            public double WindSpeed10m { get; set; }

            [JsonPropertyName("wind_direction_10m")]
            public double WindDirection10m { get; set; }

            [JsonPropertyName("surface_pressure")]
            public double SurfacePressure { get; set; }

            [JsonPropertyName("cloud_cover")]
            public double CloudCover { get; set; }
        }

        private class HourlyData
        {
            [JsonPropertyName("time")]
            public string[]? Time { get; set; }

            [JsonPropertyName("temperature_2m")]
            public double[]? Temperature2m { get; set; }

            [JsonPropertyName("weather_code")]
            public int[]? WeatherCode { get; set; }

            [JsonPropertyName("relative_humidity_2m")]
            public int[]? RelativeHumidity2m { get; set; }

            [JsonPropertyName("wind_speed_10m")]
            public double[]? WindSpeed10m { get; set; }

            [JsonPropertyName("precipitation")]
            public double[]? Precipitation { get; set; }

            [JsonPropertyName("precipitation_probability")]
            public int[]? PrecipitationProbability { get; set; }
        }

        private class DailyData
        {
            [JsonPropertyName("time")]
            public string[]? Time { get; set; }

            [JsonPropertyName("weather_code")]
            public int[]? WeatherCode { get; set; }

            [JsonPropertyName("temperature_2m_max")]
            public double[]? Temperature2mMax { get; set; }

            [JsonPropertyName("temperature_2m_min")]
            public double[]? Temperature2mMin { get; set; }

            [JsonPropertyName("precipitation_sum")]
            public double[]? PrecipitationSum { get; set; }

            [JsonPropertyName("wind_speed_10m_max")]
            public double[]? WindSpeed10mMax { get; set; }

            [JsonPropertyName("uv_index_max")]
            public double[]? UvIndexMax { get; set; }

            [JsonPropertyName("sunrise")]
            public string[]? Sunrise { get; set; }

            [JsonPropertyName("sunset")]
            public string[]? Sunset { get; set; }
        }
    }
}
