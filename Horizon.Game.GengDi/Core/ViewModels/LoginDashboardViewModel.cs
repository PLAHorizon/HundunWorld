using System;
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
                }
            }
            catch
            {
                // 忽略异常
            }
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
