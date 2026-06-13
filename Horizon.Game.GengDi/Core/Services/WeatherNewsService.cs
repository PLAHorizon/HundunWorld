using System;
using System.Collections.Generic;
using Horizon.Game.GengDi.Models;

namespace Horizon.Game.GengDi.Core.Services
{
    public static class WeatherNewsService
    {
        public static List<WeatherNews> GenerateMockNews(string cityName, WeatherType weatherType, CurrentWeather current)
        {
            var news = new List<WeatherNews>();
            var now = DateTime.Now;

            var weatherAdjectives = weatherType switch
            {
                WeatherType.Rain => "降雨",
                WeatherType.Snow => "降雪",
                WeatherType.Thunder => "雷暴",
                WeatherType.Fog => "大雾",
                WeatherType.Cloudy => "多云转阴",
                _ => "晴好"
            };

            if (current.Temperature > 35)
            {
                news.Add(new WeatherNews
                {
                    Title = $"{cityName}高温预警：气温达{current.Temperature}°C，注意防暑降温",
                    Summary = $"中央气象台发布高温预警，{cityName}今日最高气温达{current.Temperature}°C。建议市民减少户外活动，注意防晒补水。",
                    Source = "中央气象台",
                    SourceIcon = "🏛️",
                    PublishTime = now.AddHours(-1),
                    Category = "预警信息"
                });
            }

            if (current.Temperature < 0)
            {
                news.Add(new WeatherNews
                {
                    Title = $"{cityName}寒潮来袭：最低气温降至{current.Temperature}°C",
                    Summary = $"受强冷空气影响，{cityName}今日最低气温将降至{current.Temperature}°C，请市民注意保暖，做好防寒准备。",
                    Source = "央视新闻",
                    SourceIcon = "📺",
                    PublishTime = now.AddHours(-2),
                    Category = "预警信息"
                });
            }

            if (weatherType == WeatherType.Rain)
            {
                news.Add(new WeatherNews
                {
                    Title = $"{cityName}{weatherAdjectives}天气持续，出行注意安全",
                    Summary = $"预计未来24小时{cityName}将持续{weatherAdjectives}天气，路面湿滑，建议市民减少自驾出行，注意交通安全。",
                    Source = "地方气象台",
                    SourceIcon = "🌤️",
                    PublishTime = now.AddHours(-3),
                    Category = "天气资讯"
                });
            }

            news.Add(new WeatherNews
            {
                Title = $"{cityName}未来一周天气趋势：{weatherAdjectives}为主",
                Summary = $"根据最新气象资料分析，未来一周{cityName}将以{weatherAdjectives}天气为主，气温波动不大，适宜户外活动。",
                Source = "中国天气网",
                SourceIcon = "🌐",
                PublishTime = now.AddHours(-5),
                Category = "天气预报"
            });

            news.Add(new WeatherNews
            {
                Title = $"穿衣指数：今日{cityName}体感温度{current.ApparentTemperature}°C，建议这样穿",
                Summary = current.ApparentTemperature > 25
                    ? "天气较热，建议穿透气短袖，注意防晒。"
                    : current.ApparentTemperature > 15
                        ? "气温适中，建议穿薄外套或长袖。"
                        : current.ApparentTemperature > 5
                            ? "天气偏凉，建议穿厚外套或薄羽绒服。"
                            : "天气寒冷，建议穿羽绒服，注意保暖。",
                Source = "小红书",
                SourceIcon = "📕",
                PublishTime = now.AddHours(-4),
                Category = "生活指南"
            });

            news.Add(new WeatherNews
            {
                Title = $"空气质量播报：{cityName}今日气象条件{GetAirQualityDesc(current)}",
                Summary = $"今日{cityName}湿度{current.Humidity}%，云量{current.CloudCover:F0}%，气压{current.Pressure:F0}hPa。",
                Source = "生态环境部",
                SourceIcon = "🏭",
                PublishTime = now.AddHours(-6),
                Category = "环境资讯"
            });

            if (weatherType == WeatherType.Sunny)
            {
                news.Add(new WeatherNews
                {
                    Title = $"紫外线指数{current.UvText}：今日{cityName}紫外线强度{current.UvIndex}",
                    Summary = current.UvIndex > 5
                        ? "紫外线较强，建议涂抹SPF30+防晒霜，佩戴太阳镜。"
                        : "紫外线较弱，可适当进行户外活动。",
                    Source = "健康时报",
                    SourceIcon = "🏥",
                    PublishTime = now.AddHours(-7),
                    Category = "健康提示"
                });
            }

            news.Sort((a, b) => b.PublishTime.CompareTo(a.PublishTime));
            return news;
        }

        private static string GetAirQualityDesc(CurrentWeather current)
        {
            if (current.CloudCover > 80) return "有利于污染物扩散";
            if (current.WindSpeed > 20) return "风力较大，空气质量较好";
            if (current.Humidity > 80) return "湿度较大，注意防潮";
            return "总体良好";
        }
    }
}
