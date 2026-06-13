using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;

namespace Horizon.Game.GengDi.Models
{
    public class WeatherForecast
    {
        public string City { get; set; } = "北京";
        public string Province { get; set; } = "";
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public CurrentWeather Current { get; set; } = new();
        public List<DailyWeather> Daily { get; set; } = new();
        public HourlyForecast[] Hourly { get; set; } = Array.Empty<HourlyForecast>();
        public WeatherType Type { get; set; }
        public DateTime LastUpdateTime { get; set; }
        public List<WeatherNews> News { get; set; } = new();
        public SolarTermInfo SolarTerm { get; set; } = new();
        public AirQualityData? AirQuality { get; set; }
        public List<PrecipitationTimelineItem> PrecipitationTimeline { get; set; } = new();
    }

    public class SolarTermInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Season { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string DietaryTip { get; set; } = string.Empty;
        public string HealthTip { get; set; } = string.Empty;
        public string RecommendedDish { get; set; } = string.Empty;
        public string DishReason { get; set; } = string.Empty;
        public string Ingredients { get; set; } = string.Empty;
        public string CookingMethod { get; set; } = string.Empty;
        public string Contraindications { get; set; } = string.Empty;
    }

    public class CityInfo
    {
        public string LocationId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Province { get; set; } = string.Empty;
        public string Admin2 { get; set; } = string.Empty;
        public string Admin3 { get; set; } = string.Empty;
        public string Admin4 { get; set; } = string.Empty;
        public string Town { get; set; } = string.Empty;
        public string Village { get; set; } = string.Empty;
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public string AdminLevel { get; set; } = string.Empty;
        public bool IsFallback { get; set; }
        public string FallbackMessage { get; set; } = string.Empty;

        public string DisplayName
        {
            get
            {
                var parts = new List<string>();
                void AddPart(string? part)
                {
                    if (string.IsNullOrWhiteSpace(part))
                        return;
                    if (!parts.Contains(part))
                        parts.Add(part);
                }

                AddPart(Province);
                AddPart(Admin2);
                AddPart(Admin3);
                AddPart(Admin4);
                AddPart(Town);
                AddPart(Village);
                if (!parts.Contains(Name))
                    AddPart(Name);
                return parts.Count > 0 ? string.Join("·", parts) : Name;
            }
        }

        public string ShortDisplayName
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(Village) && Village != Name)
                    return $"{Village}({Town})";
                if (!string.IsNullOrWhiteSpace(Town) && Town != Name)
                    return $"{Name}({Admin3})";
                if (!string.IsNullOrWhiteSpace(Admin3) && Admin3 != Name)
                    return $"{Name}({Admin2})";
                return Name;
            }
        }
    }

    public record CitySearchResult
    {
        public List<CityInfo> Cities { get; set; } = new();
        public string Query { get; set; } = string.Empty;
        public string DetectedLevel { get; set; } = string.Empty;
        public bool HasExactMatch { get; set; }
        public bool HasFallbackResults { get; set; }
        public string SearchMessage { get; set; } = string.Empty;
        public double ElapsedMs { get; set; }
    }

    public class CurrentWeather
    {
        public int Temperature { get; set; }
        public string ConditionText { get; set; } = "晴";
        public int Humidity { get; set; }
        public double WindSpeed { get; set; }
        public string WindDirection { get; set; } = "北风";
        public int ApparentTemperature { get; set; }
        public int UvIndex { get; set; }
        public double Pressure { get; set; }
        public double Visibility { get; set; }
        public double CloudCover { get; set; }
        public int DewPoint { get; set; }
        public int WmoCode { get; set; }

        public string FeelsLike => $"{ApparentTemperature}°";
        public string PressureText => $"{Pressure:F1} hPa";
        public string VisibilityText => $"{Visibility:F1} km";
        public string UvText => UvIndex switch
        {
            <= 2 => "低",
            <= 5 => "中等",
            <= 7 => "高",
            <= 10 => "极高",
            _ => "危险"
        };
        public string WindLevel => WindSpeed switch
        {
            <= 1 => "0级",
            <= 5 => "1级",
            <= 11 => "2级",
            <= 19 => "3级",
            <= 28 => "4级",
            <= 38 => "5级",
            <= 49 => "6级",
            <= 61 => "7级",
            _ => "8级+"
        };
        public string WindDescription => $"{WindDirection} {WindLevel}";
        public string ComfortLevel => ApparentTemperature switch
        {
            <= 0 => "寒冷",
            <= 10 => "偏冷",
            <= 18 => "凉爽",
            <= 25 => "舒适",
            <= 30 => "温暖",
            _ => "炎热"
        };
        public string VisibilityLevel => Visibility switch
        {
            <= 1 => "极差",
            <= 5 => "较差",
            <= 10 => "一般",
            <= 20 => "良好",
            <= 30 => "优良",
            _ => "极好"
        };
        public string PressureLevel => Pressure switch
        {
            <= 980 => "偏低",
            <= 1020 => "正常",
            _ => "偏高"
        };
    }

    public class DailyWeather
    {
        public DateTime Date { get; set; }
        public string WeekDay { get; set; } = string.Empty;
        public string Condition { get; set; } = "晴";
        public int TemperatureMax { get; set; }
        public int TemperatureMin { get; set; }
        public int WmoCode { get; set; }
        public double Precipitation { get; set; }
        public double WindSpeedMax { get; set; }
        public int Humidity { get; set; }
        public int UvIndex { get; set; }
        public string Sunrise { get; set; } = string.Empty;
        public string Sunset { get; set; } = string.Empty;

        public string ConditionDay => Condition;
        public string ConditionIconDay => WmoCode.ToString();

        public double TempBarLeft { get; set; }
        public double TempBarWidth { get; set; }
        public Thickness TempBarMargin => new Thickness(TempBarLeft, 0, 0, 0);
    }

    public class HourlyForecast
    {
        public DateTime Time { get; set; }
        public int Temperature { get; set; }
        public string Condition { get; set; } = "晴";
        public int WmoCode { get; set; }
        public int Humidity { get; set; }
        public double WindSpeed { get; set; }
        public double Precipitation { get; set; }
        public bool IsCurrentHour { get; set; }
    }

    public class WeatherNews
    {
        public string Title { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public string SourceIcon { get; set; } = string.Empty;
        public DateTime PublishTime { get; set; }
        public string Url { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;

        public string TimeText
        {
            get
            {
                var diff = DateTime.Now - PublishTime;
                if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes}分钟前";
                if (diff.TotalHours < 24) return $"{(int)diff.TotalHours}小时前";
                return $"{PublishTime:MM-dd}";
            }
        }
    }

    public class LifeIndexItem
    {
        public string Name { get; set; } = string.Empty;
        public string Icon { get; set; } = string.Empty;
        public string Level { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string AccentColor { get; set; } = "#7AA9FF";
    }

    public class AirQualityData
    {
        public int Aqi { get; set; }
        public string Level { get; set; } = "--";
        public string LevelColor { get; set; } = "#7AA9FF";
        public string MainPollutant { get; set; } = "--";
        public double Pm25 { get; set; }
        public double Pm10 { get; set; }
        public double O3 { get; set; }
        public double No2 { get; set; }
        public double So2 { get; set; }
        public double Co { get; set; }

        public static string GetLevel(int aqi) => aqi switch
        {
            <= 50 => "优",
            <= 100 => "良",
            <= 150 => "轻度污染",
            <= 200 => "中度污染",
            <= 300 => "重度污染",
            _ => "严重污染"
        };

        public static string GetLevelColor(int aqi) => aqi switch
        {
            <= 50 => "#5FD19B",
            <= 100 => "#F1C96C",
            <= 150 => "#F26E7D",
            <= 200 => "#E53935",
            <= 300 => "#9C27B0",
            _ => "#7B1FA2"
        };
    }

    public class PrecipitationTimelineItem
    {
        public DateTime Time { get; set; }
        public double Probability { get; set; }
        public string TimeLabel => Time.Hour == 0 || Time.Hour % 3 == 0
            ? Time.ToString("HH:mm")
            : string.Empty;
    }

    public enum WeatherType
    {
        Sunny,
        Cloudy,
        Rain,
        Snow,
        Thunder,
        Fog
    }

    public static class WmoWeatherMapper
    {
        public static (WeatherType Type, string Text) Map(int code, bool isNight = false)
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
    }

    public static class WeatherConditionIcons
    {
        public static string GetIcon(string conditionCode)
        {
            if (int.TryParse(conditionCode, out var code))
            {
                return code switch
                {
                    0 => "M12 7a5 5 0 110 10 5 5 0 010-10z",
                    1 or 2 => "M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm-2 15l-5-5 1.41-1.41L10 14.17l7.59-7.59L19 8l-9 9z",
                    3 => "M19.35 10.04A7.49 7.49 0 0012 4C9.11 4 6.6 5.64 5.35 8.04A5.994 5.994 0 000 14c0 3.31 2.69 6 6 6h13c2.76 0 5-2.24 5-5 0-2.64-2.05-4.78-4.65-4.96z",
                    45 or 48 => "M19.35 10.04A7.49 7.49 0 0012 4C9.11 4 6.6 5.64 5.35 8.04A5.994 5.994 0 000 14c0 3.31 2.69 6 6 6h13c2.76 0 5-2.24 5-5 0-2.64-2.05-4.78-4.65-4.96z",
                    51 or 53 or 55 or 56 or 57 or 61 or 63 or 65 or 66 or 67 or 80 or 81 or 82 => "M19.35 10.04A7.49 7.49 0 0012 4C9.11 4 6.6 5.64 5.35 8.04A5.994 5.994 0 000 14c0 3.31 2.69 6 6 6h13c2.76 0 5-2.24 5-5 0-2.64-2.05-4.78-4.65-4.96z M7 18l-2 3m6-3l-2 3m6-3l-2 3",
                    71 or 73 or 75 or 77 or 85 or 86 => "M19.35 10.04A7.49 7.49 0 0012 4C9.11 4 6.6 5.64 5.35 8.04A5.994 5.994 0 000 14c0 3.31 2.69 6 6 6h13c2.76 0 5-2.24 5-5 0-2.64-2.05-4.78-4.65-4.96z M7 18h2m4 0h2m-8 4h2m4 0h2",
                    95 or 96 or 99 => "M19.35 10.04A7.49 7.49 0 0012 4C9.11 4 6.6 5.64 5.35 8.04A5.994 5.994 0 000 14c0 3.31 2.69 6 6 6h13c2.76 0 5-2.24 5-5 0-2.64-2.05-4.78-4.65-4.96z M13 16l-4 6h3l-1 4",
                    _ => "M12 7a5 5 0 110 10 5 5 0 010-10z"
                };
            }
            return "M12 7a5 5 0 110 10 5 5 0 010-10z";
        }
    }
}
