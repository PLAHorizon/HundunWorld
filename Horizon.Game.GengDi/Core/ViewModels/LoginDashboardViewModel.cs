using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Horizon.Game.GengDi.Core.Services;
using Horizon.Game.GengDi.Models;
using Horizon.Game.Message.Network;

namespace Horizon.Game.GengDi.Core.ViewModels
{
    public class LoginDashboardViewModel : ViewModelBase
    {
        private readonly WeatherService _weatherService;
        private readonly FlowerMarketService _flowerMarketService;
        private readonly GameService _gameService;

        private WeatherForecast _currentWeather;
        private string _weatherSummary;
        private string _weatherLocation;
        private string _weatherForecastSummary;
        private FlowerPriceForecast _flowerForecast;
        private string _flowerSummary;
        private ObservableCollection<GameInfo> _recommendedGames;
        private bool _isLoading;
        private LoginViewModel _loginVm;

        public LoginViewModel LoginVm
        {
            get => _loginVm;
            set => SetProperty(ref _loginVm, value);
        }

        public WeatherForecast CurrentWeather
        {
            get => _currentWeather;
            set => SetProperty(ref _currentWeather, value);
        }

        public string WeatherSummary
        {
            get => _weatherSummary;
            set => SetProperty(ref _weatherSummary, value);
        }

        /// <summary>
        /// 天气位置文本，格式 "省·市"（设计稿 weather-location）。
        /// </summary>
        public string WeatherLocation
        {
            get => _weatherLocation;
            set => SetProperty(ref _weatherLocation, value);
        }

        /// <summary>
        /// 天气预报摘要，格式 "雷暴·降水8%·23/30°"（设计稿 weather-forecast）。
        /// </summary>
        public string WeatherForecastSummary
        {
            get => _weatherForecastSummary;
            set => SetProperty(ref _weatherForecastSummary, value);
        }

        public FlowerPriceForecast FlowerForecast
        {
            get => _flowerForecast;
            set => SetProperty(ref _flowerForecast, value);
        }

        public string FlowerSummary
        {
            get => _flowerSummary;
            set => SetProperty(ref _flowerSummary, value);
        }

        public ObservableCollection<GameInfo> RecommendedGames
        {
            get => _recommendedGames;
            set => SetProperty(ref _recommendedGames, value);
        }

        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        public LoginDashboardViewModel()
        {
            _weatherService = new WeatherService();
            _flowerMarketService = new FlowerMarketService();
            _gameService =  GameService.Instance;
            _recommendedGames = new ObservableCollection<GameInfo>();
            _loginVm = new LoginViewModel();

            // 初始化默认天气数据，确保天气卡片在数据加载前也有内容显示
            _currentWeather = new WeatherForecast
            {
                City = "加载中",
                Province = "",
                Current = new CurrentWeather
                {
                    Temperature = 0,
                    ConditionText = "--",
                    WindDirection = "",
                    WindSpeed = 0,
                    WmoCode = 0
                }
            };
            _weatherLocation = "定位中";
            _weatherForecastSummary = "--";
        }

        public async Task LoadDashboardData()
        {
            IsLoading = true;

            try
            {
                await Task.WhenAll(
                    LoadWeatherData(),
                    LoadFlowerData(),
                    LoadGameRecommendations()
                );
            }
            catch (Exception)
            {
                // 忽略异常，UI显示默认内容
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task LoadWeatherData()
        {
            try
            {
                var weather = await _weatherService.GetWeatherAsync();
                if (weather != null)
                {
                    CurrentWeather = weather;
                    WeatherSummary = _weatherService.GetWeatherSummary(weather);
                    WeatherLocation = BuildWeatherLocation(weather);
                    WeatherForecastSummary = BuildForecastSummary(weather);
                }
            }
            catch
            {
                // 忽略异常
            }
        }

        /// <summary>
        /// 构建天气位置文本，格式 "省·市"（如 "河南·郑州"）。
        /// </summary>
        private static string BuildWeatherLocation(WeatherForecast weather)
        {
            if (weather == null) return "";
            var province = weather.Province?.TrimEnd('省') ?? "";
            return string.IsNullOrEmpty(province)
                ? (weather.City ?? "")
                : $"{province}·{weather.City}";
        }

        /// <summary>
        /// 构建天气预报摘要，格式 "雷暴·降水8%·23/30°"（设计稿 weather-forecast）。
        /// </summary>
        private static string BuildForecastSummary(WeatherForecast weather)
        {
            if (weather == null || weather.Current == null) return "";

            var parts = new List<string>();

            // 天气状况
            parts.Add(weather.Current.ConditionText ?? "--");

            // 降水概率 + 温度范围（来自每日预报）
            if (weather.Daily != null && weather.Daily.Count > 0)
            {
                var today = weather.Daily[0];

                // 降水概率：若值 <= 1 视为比例，否则视为百分比
                var precip = today.Precipitation;
                var precipPercent = precip <= 1.0 ? (int)(precip * 100) : (int)precip;
                parts.Add($"降水{precipPercent}%");

                // 最低 / 最高温度
                parts.Add($"{today.TemperatureMin}/{today.TemperatureMax}°");
            }

            return string.Join("·", parts);
        }

        private async Task LoadFlowerData()
        {
            try
            {
                var forecast = await _flowerMarketService.GetPriceForecastAsync(1, 3);
                if (forecast != null)
                {
                    FlowerForecast = forecast;
                    FlowerSummary = FormatFlowerSummary(forecast);
                }
            }
            catch
            {
                // 忽略异常
            }
        }

        private async Task LoadGameRecommendations()
        {
            try
            {
                var games = await _gameService.GetAllGamesAsync();
                if (games != null && games.Count > 0)
                {
                    RecommendedGames.Clear();
                    var count = Math.Min(3, games.Count);
                    for (int i = 0; i < count; i++)
                    {
                        RecommendedGames.Add(games[i]);
                    }
                }
            }
            catch
            {
                // 忽略异常
            }
        }

        private string FormatFlowerSummary(FlowerPriceForecast forecast)
        {
            if (forecast == null || forecast.PredictedPrices == null || forecast.PredictedPrices.Count == 0)
                return "";

            var latest = forecast.PredictedPrices[0];
            return $"趋势: {(latest.PredictedPrice > 0 ? "↑" : "↓")} {latest.PredictedPrice:C}";
        }
    }
}
