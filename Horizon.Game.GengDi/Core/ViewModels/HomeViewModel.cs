using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Horizon.Game.GengDi.Core.Services;
using Horizon.Game.GengDi.Models;

namespace Horizon.Game.GengDi.Core.ViewModels
{
    public enum DrawerType { None, Weather, Dish, Hourly, AirQuality, LifeIndex, News }

    public class HomeViewModel : ViewModelBase
    {
        private readonly WeatherService _weatherService;
        private readonly FlowerMarketService _flowerMarketService;
        private WeatherForecast _weatherForecast;
        private string _weatherSummary = string.Empty;
        private bool _isLoadingWeather;
        private string _welcomeMessage = "欢迎回来";
        private string _installedGamesCount = "3 款游戏";
        private string _updateGamesCount = "0 款游戏";
        private string _onlineFriendsCount = "0 位在线";
        private string _selectedLocationId = "101010100";
        private string _selectedCityName = "北京";
        private bool _isCityPickerOpen;
        private string _citySearchText = string.Empty;
        private List<CityInfo> _filteredCities = new();
        private bool _isWeatherNewsExpanded;
        private bool _isSearchingCities;
        private string _searchMessage = string.Empty;
        private string _searchLevelHint = string.Empty;
        private bool _isLocatingByIp;
        private string _locationHint = string.Empty;
        private bool _isDishDetailOpen;
        private string _flowerAvgPrice = "--";
        private string _flowerPriceChange = "--";
        private int _flowerAlertCount;
        private bool _isFlowerDataLoaded;
        private List<LifeIndexItem> _lifeIndices = new();
        private string _lifeIndexSummary = string.Empty;
        private string _todaySunrise = "--:--";
        private string _todaySunset = "--:--";
        private string _dayLength = "--";
        private string _hourlyTemperaturePoints = string.Empty;
        private string _hourlyTemperatureFillPoints = string.Empty;
        private string _selectedMapLayer = "temperature";
        private double _mapLatitude = 39.9042;
        private double _mapLongitude = 116.4074;
        private string _mapCityName = "北京";
        private bool _hasPrecipitation;
        private bool _isWeatherDetailOpen;
        private bool _isHourlyDetailOpen;
        private bool _isAirQualityDetailOpen;
        private bool _isLifeIndexDetailOpen;
        private bool _isNewsDetailOpen;
        private string _weatherLoadError = string.Empty;
        private DrawerType _activeDrawer;

        public HomeViewModel()
        {
            _weatherService = new WeatherService();
            _flowerMarketService = new FlowerMarketService();
            LoadWeatherCommand = new AsyncCommand(LoadWeatherAsync);
            LoadFlowerDataCommand = new AsyncCommand(LoadFlowerDataAsync);
            ToggleCityPickerCommand = new SimpleRelayCommand(() => { IsCityPickerOpen = !IsCityPickerOpen; });
            SelectCityCommand = new SimpleRelayCommand(OnSelectCity);
            ToggleWeatherDetailCommand = new SimpleRelayCommand(OpenWeatherDetailDrawer);
            CloseWeatherDetailCommand = new SimpleRelayCommand(() => { IsWeatherDetailOpen = false; _activeDrawer = DrawerType.None; });
            SelectMapLayerCommand = new SimpleRelayCommand(OnSelectMapLayer);
            ToggleNewsCommand = new SimpleRelayCommand(() => { IsWeatherNewsExpanded = !IsWeatherNewsExpanded; });
            ToggleDishDetailCommand = new SimpleRelayCommand(OpenDishDetailDrawer);
            CloseDishDetailCommand = new SimpleRelayCommand(() => { IsDishDetailOpen = false; _activeDrawer = DrawerType.None; });
            ToggleHourlyDetailCommand = new SimpleRelayCommand(OpenHourlyDetailDrawer);
            CloseHourlyDetailCommand = new SimpleRelayCommand(() => { IsHourlyDetailOpen = false; _activeDrawer = DrawerType.None; });
            ToggleAirQualityDetailCommand = new SimpleRelayCommand(OpenAirQualityDetailDrawer);
            CloseAirQualityDetailCommand = new SimpleRelayCommand(() => { IsAirQualityDetailOpen = false; _activeDrawer = DrawerType.None; });
            ToggleLifeIndexDetailCommand = new SimpleRelayCommand(OpenLifeIndexDetailDrawer);
            CloseLifeIndexDetailCommand = new SimpleRelayCommand(() => { IsLifeIndexDetailOpen = false; _activeDrawer = DrawerType.None; });
            ToggleNewsDetailCommand = new SimpleRelayCommand(OpenNewsDetailDrawer);
            CloseNewsDetailCommand = new SimpleRelayCommand(() => { IsNewsDetailOpen = false; _activeDrawer = DrawerType.None; });
            CloseCityPickerCommand = new SimpleRelayCommand(() => { IsCityPickerOpen = false; CitySearchText = string.Empty; });
            NavigateToGamesCommand = new SimpleRelayCommand(OnNavigateToGames);
            NavigateToNewsCommand = new SimpleRelayCommand(OnNavigateToNews);
            NavigateToSocialCommand = new SimpleRelayCommand(OnNavigateToSocial);

            _filteredCities = CitySearchService.SearchCitiesAsync("").Result;
            UpdateSelectedCityName();
            LoadDataAsync();
        }

        public ICommand LoadWeatherCommand { get; }
        public ICommand LoadFlowerDataCommand { get; }
        public ICommand ToggleCityPickerCommand { get; }
        public ICommand SelectCityCommand { get; }
        public ICommand ToggleWeatherDetailCommand { get; }
        public ICommand CloseWeatherDetailCommand { get; }
        public ICommand SelectMapLayerCommand { get; }
        public ICommand ToggleNewsCommand { get; }
        public ICommand ToggleDishDetailCommand { get; }
        public ICommand CloseDishDetailCommand { get; }
        public ICommand ToggleHourlyDetailCommand { get; }
        public ICommand CloseHourlyDetailCommand { get; }
        public ICommand ToggleAirQualityDetailCommand { get; }
        public ICommand CloseAirQualityDetailCommand { get; }
        public ICommand ToggleLifeIndexDetailCommand { get; }
        public ICommand CloseLifeIndexDetailCommand { get; }
        public ICommand ToggleNewsDetailCommand { get; }
        public ICommand CloseNewsDetailCommand { get; }

        public WeatherForecast WeatherForecast
        {
            get => _weatherForecast;
            set => SetProperty(ref _weatherForecast, value);
        }

        public string WeatherSummary
        {
            get => _weatherSummary;
            set => SetProperty(ref _weatherSummary, value);
        }

        public bool IsLoadingWeather
        {
            get => _isLoadingWeather;
            set => SetProperty(ref _isLoadingWeather, value);
        }

        public string SelectedLocationId
        {
            get => _selectedLocationId;
            set => SetProperty(ref _selectedLocationId, value);
        }

        public bool IsCityPickerOpen
        {
            get => _isCityPickerOpen;
            set => SetProperty(ref _isCityPickerOpen, value);
        }

        public string CitySearchText
        {
            get => _citySearchText;
            set
            {
                if (SetProperty(ref _citySearchText, value))
                {
                    SearchCitiesAsync(value);
                }
            }
        }

        public List<CityInfo> FilteredCities
        {
            get => _filteredCities;
            set => SetProperty(ref _filteredCities, value);
        }

        public bool IsSearchingCities
        {
            get => _isSearchingCities;
            set => SetProperty(ref _isSearchingCities, value);
        }

        public string SelectedCityName
        {
            get => _selectedCityName;
            set
            {
                if (SetProperty(ref _selectedCityName, value))
                    MapCityName = value;
            }
        }

        public ICommand CloseCityPickerCommand { get; }
        public ICommand NavigateToGamesCommand { get; }
        public ICommand NavigateToNewsCommand { get; }
        public ICommand NavigateToSocialCommand { get; }

        public bool IsWeatherNewsExpanded
        {
            get => _isWeatherNewsExpanded;
            set => SetProperty(ref _isWeatherNewsExpanded, value);
        }

        public string SearchMessage
        {
            get => _searchMessage;
            set => SetProperty(ref _searchMessage, value);
        }

        public string SearchLevelHint
        {
            get => _searchLevelHint;
            set => SetProperty(ref _searchLevelHint, value);
        }

        public bool IsLocatingByIp
        {
            get => _isLocatingByIp;
            set => SetProperty(ref _isLocatingByIp, value);
        }

        public string LocationHint
        {
            get => _locationHint;
            set => SetProperty(ref _locationHint, value);
        }

        public bool IsDishDetailOpen
        {
            get => _isDishDetailOpen;
            set => SetProperty(ref _isDishDetailOpen, value);
        }

        public string WelcomeMessage
        {
            get => _welcomeMessage;
            set => SetProperty(ref _welcomeMessage, value);
        }

        public string InstalledGamesCount
        {
            get => _installedGamesCount;
            set => SetProperty(ref _installedGamesCount, value);
        }

        public string UpdateGamesCount
        {
            get => _updateGamesCount;
            set => SetProperty(ref _updateGamesCount, value);
        }

        public string OnlineFriendsCount
        {
            get => _onlineFriendsCount;
            set => SetProperty(ref _onlineFriendsCount, value);
        }

        public string FlowerAvgPrice
        {
            get => _flowerAvgPrice;
            set => SetProperty(ref _flowerAvgPrice, value);
        }

        public string FlowerPriceChange
        {
            get => _flowerPriceChange;
            set => SetProperty(ref _flowerPriceChange, value);
        }

        public int FlowerAlertCount
        {
            get => _flowerAlertCount;
            set => SetProperty(ref _flowerAlertCount, value);
        }

        public bool IsFlowerDataLoaded
        {
            get => _isFlowerDataLoaded;
            set => SetProperty(ref _isFlowerDataLoaded, value);
        }

        public List<LifeIndexItem> LifeIndices
        {
            get => _lifeIndices;
            set
            {
                if (SetProperty(ref _lifeIndices, value))
                {
                    LifeIndexSummary = value != null && value.Count > 0
                        ? $"{value[0].Name} {value[0].Level}"
                        : string.Empty;
                }
            }
        }

        public string LifeIndexSummary
        {
            get => _lifeIndexSummary;
            set => SetProperty(ref _lifeIndexSummary, value);
        }

        public string TodaySunrise
        {
            get => _todaySunrise;
            set => SetProperty(ref _todaySunrise, value);
        }

        public string TodaySunset
        {
            get => _todaySunset;
            set => SetProperty(ref _todaySunset, value);
        }

        public string DayLength
        {
            get => _dayLength;
            set => SetProperty(ref _dayLength, value);
        }

        public string HourlyTemperaturePoints
        {
            get => _hourlyTemperaturePoints;
            set => SetProperty(ref _hourlyTemperaturePoints, value);
        }

        public string HourlyTemperatureFillPoints
        {
            get => _hourlyTemperatureFillPoints;
            set => SetProperty(ref _hourlyTemperatureFillPoints, value);
        }

        public string SelectedMapLayer
        {
            get => _selectedMapLayer;
            set => SetProperty(ref _selectedMapLayer, value);
        }

        public double MapLatitude
        {
            get => _mapLatitude;
            set => SetProperty(ref _mapLatitude, value);
        }

        public double MapLongitude
        {
            get => _mapLongitude;
            set => SetProperty(ref _mapLongitude, value);
        }

        public string MapCityName
        {
            get => _mapCityName;
            set => SetProperty(ref _mapCityName, value);
        }

        public bool HasPrecipitation
        {
            get => _hasPrecipitation;
            set => SetProperty(ref _hasPrecipitation, value);
        }

        public bool IsWeatherDetailOpen
        {
            get => _isWeatherDetailOpen;
            set => SetProperty(ref _isWeatherDetailOpen, value);
        }

        public bool IsHourlyDetailOpen
        {
            get => _isHourlyDetailOpen;
            set => SetProperty(ref _isHourlyDetailOpen, value);
        }

        public bool IsAirQualityDetailOpen
        {
            get => _isAirQualityDetailOpen;
            set => SetProperty(ref _isAirQualityDetailOpen, value);
        }

        public bool IsLifeIndexDetailOpen
        {
            get => _isLifeIndexDetailOpen;
            set => SetProperty(ref _isLifeIndexDetailOpen, value);
        }

        public bool IsNewsDetailOpen
        {
            get => _isNewsDetailOpen;
            set => SetProperty(ref _isNewsDetailOpen, value);
        }

        public string WeatherLoadError
        {
            get => _weatherLoadError;
            set => SetProperty(ref _weatherLoadError, value);
        }

        private async void LoadDataAsync()
        {
            var located = await TryLocateByIpAsync();
            if (!located)
            {
                await LoadWeatherAsync();
            }

            _ = LoadFlowerDataAsync();
        }

        private async Task<bool> TryLocateByIpAsync()
        {
            IsLocatingByIp = true;
            LocationHint = "正在定位...";
            try
            {
                var ipLocation = await IpLocationService.LocateAsync();
                if (ipLocation != null)
                {
                    var nearestCity = CitySearchService.FindNearestCity(ipLocation.Latitude, ipLocation.Longitude);
                    if (nearestCity != null)
                    {
                        _selectedLocationId = nearestCity.LocationId;
                        SelectedCityName = nearestCity.DisplayName;
                        LocationHint = $"已定位到: {nearestCity.DisplayName}";

                        var forecast = await _weatherService.GetWeatherAsync(_selectedLocationId).ConfigureAwait(false);
                        if (forecast != null)
                        {
                            WeatherForecast = forecast;
                            MapLatitude = WeatherForecast?.Latitude ?? 39.9042;
                            MapLongitude = WeatherForecast?.Longitude ?? 116.4074;
                            MapCityName = SelectedCityName;
                            WeatherSummary = _weatherService.GetWeatherSummary(forecast);
                            ComputeDerivedProperties(forecast);
                        }
                        else
                        {
                            WeatherLoadError = "天气数据加载失败，请检查网络连接";
                        }
                        return true;
                    }
                }

                LocationHint = "定位失败，使用默认位置";
                return false;
            }
            catch
            {
                LocationHint = "定位失败，使用默认位置";
                return false;
            }
            finally
            {
                IsLocatingByIp = false;
            }
        }

        private async Task LoadWeatherAsync()
        {
            IsLoadingWeather = true;
            WeatherLoadError = string.Empty;
            try
            {
                var forecast = await _weatherService.GetWeatherAsync(_selectedLocationId).ConfigureAwait(false);
                if (forecast != null)
                {
                    WeatherForecast = forecast;
                    MapLatitude = WeatherForecast?.Latitude ?? 39.9042;
                    MapLongitude = WeatherForecast?.Longitude ?? 116.4074;
                    MapCityName = SelectedCityName;
                    WeatherSummary = _weatherService.GetWeatherSummary(forecast);
                    ComputeDerivedProperties(forecast);
                }
                else
                {
                    WeatherLoadError = "天气数据加载失败，请检查网络连接";
                }
            }
            catch (Exception ex)
            {
                WeatherLoadError = $"天气数据加载异常: {ex.Message}";
            }
            finally
            {
                IsLoadingWeather = false;
            }
        }

        private void ComputeDerivedProperties(WeatherForecast forecast)
        {
            ComputeTemperatureBars(forecast);
            ComputeSunriseSunset(forecast);
            ComputeLifeIndices(forecast);
            ComputeHourlyTemperatureCurve(forecast);
            ComputePrecipitationState(forecast);
        }

        private void ComputeTemperatureBars(WeatherForecast forecast)
        {
            if (forecast.Daily.Count == 0) return;

            var overallMin = forecast.Daily.Min(d => d.TemperatureMin);
            var overallMax = forecast.Daily.Max(d => d.TemperatureMax);
            var range = overallMax - overallMin;
            if (range <= 0) range = 1;

            const double barTotalWidth = 100.0;
            const double barPadding = 8.0;
            var usableWidth = barTotalWidth - barPadding * 2;

            foreach (var day in forecast.Daily)
            {
                var left = barPadding + (day.TemperatureMin - overallMin) / (double)range * usableWidth;
                var right = barPadding + (day.TemperatureMax - overallMin) / (double)range * usableWidth;
                day.TempBarLeft = left;
                day.TempBarWidth = Math.Max(right - left, 4);
            }
        }

        private void ComputeSunriseSunset(WeatherForecast forecast)
        {
            if (forecast.Daily.Count > 0)
            {
                var today = forecast.Daily[0];
                TodaySunrise = !string.IsNullOrEmpty(today.Sunrise) ? today.Sunrise : "--:--";
                TodaySunset = !string.IsNullOrEmpty(today.Sunset) ? today.Sunset : "--:--";

                if (TimeSpan.TryParse(today.Sunrise, out var rise) && TimeSpan.TryParse(today.Sunset, out var set))
                {
                    var length = set - rise;
                    DayLength = $"{(int)length.TotalHours}时{length.Minutes}分";
                }
                else
                {
                    DayLength = "--";
                }
            }
        }

        private void ComputeLifeIndices(WeatherForecast forecast)
        {
            var current = forecast.Current;
            var indices = new List<LifeIndexItem>();

            var clothingLevel = current.ApparentTemperature switch
            {
                <= 0 => "寒冷",
                <= 10 => "较冷",
                <= 18 => "凉爽",
                <= 25 => "舒适",
                <= 30 => "温暖",
                _ => "炎热"
            };
            var clothingDesc = current.ApparentTemperature switch
            {
                <= 0 => "建议穿羽绒服、厚棉衣",
                <= 10 => "建议穿厚外套、毛衣",
                <= 18 => "建议穿薄外套或长袖",
                <= 25 => "建议穿长袖或薄衫",
                <= 30 => "建议穿短袖、短裤",
                _ => "建议穿透气轻薄衣物"
            };
            var clothingColor = current.ApparentTemperature switch
            {
                <= 0 => "#64B5F6",
                <= 10 => "#7AA9FF",
                <= 18 => "#5FD19B",
                <= 25 => "#F1C96C",
                <= 30 => "#F26E7D",
                _ => "#E53935"
            };
            indices.Add(new LifeIndexItem
            {
                Name = "穿衣",
                Icon = "👕",
                Level = clothingLevel,
                Description = clothingDesc,
                AccentColor = clothingColor
            });

            var exerciseLevel = (current.ApparentTemperature, forecast.Type) switch
            {
                (<= 0, _) => "不宜",
                (>= 35, _) => "不宜",
                (_, WeatherType.Rain) => "不宜",
                (_, WeatherType.Snow) => "不宜",
                (_, WeatherType.Thunder) => "不宜",
                (<= 10, _) => "较不宜",
                (>= 30, _) => "较不宜",
                (_, WeatherType.Fog) => "较不宜",
                _ => "适宜"
            };
            var exerciseDesc = exerciseLevel switch
            {
                "不宜" => "天气条件不适合户外运动",
                "较不宜" => "建议减少户外运动时间",
                _ => "天气适宜户外运动"
            };
            var exerciseColor = exerciseLevel switch
            {
                "适宜" => "#5FD19B",
                "较不宜" => "#F1C96C",
                _ => "#F26E7D"
            };
            indices.Add(new LifeIndexItem
            {
                Name = "运动",
                Icon = "🏃",
                Level = exerciseLevel,
                Description = exerciseDesc,
                AccentColor = exerciseColor
            });

            var uvLevel = current.UvIndex switch
            {
                <= 2 => "无需",
                <= 5 => "需要",
                <= 7 => "必须",
                <= 10 => "强烈",
                _ => "极强"
            };
            var uvDesc = current.UvIndex switch
            {
                <= 2 => "紫外线弱，无需特别防护",
                <= 5 => "建议涂抹防晒霜",
                <= 7 => "建议SPF30+防晒霜+遮阳帽",
                <= 10 => "尽量避免外出，必须防护",
                _ => "严禁暴晒，全面防护"
            };
            var uvColor = current.UvIndex switch
            {
                <= 2 => "#5FD19B",
                <= 5 => "#F1C96C",
                <= 7 => "#F26E7D",
                _ => "#E53935"
            };
            indices.Add(new LifeIndexItem
            {
                Name = "防晒",
                Icon = "🧴",
                Level = uvLevel,
                Description = uvDesc,
                AccentColor = uvColor
            });

            var hasRain = forecast.Daily.Any(d => d.Precipitation > 30);
            var carWashLevel = (forecast.Type, hasRain) switch
            {
                (WeatherType.Rain, _) => "不宜",
                (WeatherType.Snow, _) => "不宜",
                (_, true) => "不宜",
                (WeatherType.Cloudy, _) => "较适宜",
                _ => "适宜"
            };
            var carWashDesc = carWashLevel switch
            {
                "适宜" => "天气晴好，适合洗车",
                "较适宜" => "近期无雨，可以洗车",
                _ => "近期有雨，不建议洗车"
            };
            var carWashColor = carWashLevel switch
            {
                "适宜" => "#5FD19B",
                "较适宜" => "#F1C96C",
                _ => "#F26E7D"
            };
            indices.Add(new LifeIndexItem
            {
                Name = "洗车",
                Icon = "🚗",
                Level = carWashLevel,
                Description = carWashDesc,
                AccentColor = carWashColor
            });

            var travelLevel = (forecast.Type, current.ApparentTemperature) switch
            {
                (WeatherType.Rain, _) => "不宜",
                (WeatherType.Snow, _) => "不宜",
                (WeatherType.Thunder, _) => "不宜",
                (WeatherType.Fog, _) => "较不宜",
                (_, <= 0) => "较不宜",
                (_, >= 38) => "不宜",
                (_, >= 35) => "较不宜",
                (WeatherType.Sunny, _) => "适宜",
                (WeatherType.Cloudy, _) => "较适宜",
                _ => "一般"
            };
            var travelDesc = travelLevel switch
            {
                "适宜" => "天气晴好，适合出行旅游",
                "较适宜" => "天气尚可，可以出游",
                "一般" => "天气一般，出行注意",
                "较不宜" => "天气欠佳，建议减少出行",
                _ => "天气恶劣，不宜外出旅游"
            };
            var travelColor = travelLevel switch
            {
                "适宜" => "#5FD19B",
                "较适宜" => "#A5D6A7",
                "一般" => "#F1C96C",
                "较不宜" => "#F26E7D",
                _ => "#E53935"
            };
            indices.Add(new LifeIndexItem
            {
                Name = "旅游",
                Icon = "🏖️",
                Level = travelLevel,
                Description = travelDesc,
                AccentColor = travelColor
            });

            var dailyTempRange = forecast.Daily.Count > 0
                ? forecast.Daily[0].TemperatureMax - forecast.Daily[0].TemperatureMin
                : 0;
            var coldLevel = (current.ApparentTemperature, current.Humidity, dailyTempRange) switch
            {
                (<= 0, _, _) => "极易",
                (<= 5, >= 70, _) => "极易",
                (<= 10, >= 80, _) => "易发",
                (_, _, >= 15) => "易发",
                (<= 10, _, _) => "较易",
                (_, >= 80, _) => "较易",
                (>= 30, _, _) => "较易",
                (<= 18, _, _) => "一般",
                _ => "不易"
            };
            var coldDesc = coldLevel switch
            {
                "极易" => "天气寒冷，极易感冒，注意保暖",
                "易发" => "温差较大或湿度高，容易感冒",
                "较易" => "天气条件较易引发感冒",
                "一般" => "感冒风险一般，适当注意",
                _ => "天气条件不易引发感冒"
            };
            var coldColor = coldLevel switch
            {
                "不易" => "#5FD19B",
                "一般" => "#F1C96C",
                "较易" => "#F26E7D",
                "易发" => "#E53935",
                _ => "#9C27B0"
            };
            indices.Add(new LifeIndexItem
            {
                Name = "感冒",
                Icon = "🤧",
                Level = coldLevel,
                Description = coldDesc,
                AccentColor = coldColor
            });

            LifeIndices = indices;
        }

        private void OnSelectMapLayer(object parameter)
        {
            if (parameter is string layer)
                SelectedMapLayer = layer;
        }

        private void ComputeHourlyTemperatureCurve(WeatherForecast forecast)
        {
            if (forecast.Hourly == null || forecast.Hourly.Length == 0)
            {
                HourlyTemperaturePoints = string.Empty;
                HourlyTemperatureFillPoints = string.Empty;
                return;
            }

            var hours = forecast.Hourly.Take(24).ToList();
            if (hours.Count < 2)
            {
                HourlyTemperaturePoints = string.Empty;
                HourlyTemperatureFillPoints = string.Empty;
                return;
            }

            var minTemp = hours.Min(h => h.Temperature);
            var maxTemp = hours.Max(h => h.Temperature);
            var range = maxTemp - minTemp;
            if (range == 0) range = 1;

            const double canvasWidth = 800.0;
            const double canvasHeight = 80.0;
            const double paddingY = 10.0;

            var points = new List<string>();
            for (int i = 0; i < hours.Count; i++)
            {
                var x = i * (canvasWidth / (hours.Count - 1));
                var y = paddingY + (1.0 - (hours[i].Temperature - minTemp) / (double)range) * (canvasHeight - paddingY * 2);
                points.Add($"{x:F1},{y:F1}");
            }

            HourlyTemperaturePoints = string.Join(" ", points);

            var fillPoints = new List<string>(points);
            fillPoints.Add($"{canvasWidth:F1},{canvasHeight:F1}");
            fillPoints.Add($"0,{canvasHeight:F1}");
            HourlyTemperatureFillPoints = string.Join(" ", fillPoints);
        }

        private void ComputePrecipitationState(WeatherForecast forecast)
        {
            HasPrecipitation = forecast.PrecipitationTimeline != null
                && forecast.PrecipitationTimeline.Any(p => p.Probability > 0);
        }

        private void OpenWeatherDetailDrawer()
        {
            if (_activeDrawer == DrawerType.Weather)
            {
                IsWeatherDetailOpen = false;
                _activeDrawer = DrawerType.None;
            }
            else
            {
                IsDishDetailOpen = false;
                IsHourlyDetailOpen = false;
                IsAirQualityDetailOpen = false;
                IsLifeIndexDetailOpen = false;
                IsNewsDetailOpen = false;
                IsWeatherDetailOpen = true;
                _activeDrawer = DrawerType.Weather;
            }
        }

        private void OpenDishDetailDrawer()
        {
            if (_activeDrawer == DrawerType.Dish)
            {
                IsDishDetailOpen = false;
                _activeDrawer = DrawerType.None;
            }
            else
            {
                IsWeatherDetailOpen = false;
                IsHourlyDetailOpen = false;
                IsAirQualityDetailOpen = false;
                IsLifeIndexDetailOpen = false;
                IsNewsDetailOpen = false;
                IsDishDetailOpen = true;
                _activeDrawer = DrawerType.Dish;
            }
        }

        private void OpenHourlyDetailDrawer()
        {
            if (_activeDrawer == DrawerType.Hourly)
            {
                IsHourlyDetailOpen = false;
                _activeDrawer = DrawerType.None;
            }
            else
            {
                IsWeatherDetailOpen = false;
                IsDishDetailOpen = false;
                IsAirQualityDetailOpen = false;
                IsLifeIndexDetailOpen = false;
                IsNewsDetailOpen = false;
                IsHourlyDetailOpen = true;
                _activeDrawer = DrawerType.Hourly;
            }
        }

        private void OpenAirQualityDetailDrawer()
        {
            if (_activeDrawer == DrawerType.AirQuality)
            {
                IsAirQualityDetailOpen = false;
                _activeDrawer = DrawerType.None;
            }
            else
            {
                IsWeatherDetailOpen = false;
                IsDishDetailOpen = false;
                IsHourlyDetailOpen = false;
                IsLifeIndexDetailOpen = false;
                IsNewsDetailOpen = false;
                IsAirQualityDetailOpen = true;
                _activeDrawer = DrawerType.AirQuality;
            }
        }

        private void OpenLifeIndexDetailDrawer()
        {
            if (_activeDrawer == DrawerType.LifeIndex)
            {
                IsLifeIndexDetailOpen = false;
                _activeDrawer = DrawerType.None;
            }
            else
            {
                IsWeatherDetailOpen = false;
                IsDishDetailOpen = false;
                IsHourlyDetailOpen = false;
                IsAirQualityDetailOpen = false;
                IsNewsDetailOpen = false;
                IsLifeIndexDetailOpen = true;
                _activeDrawer = DrawerType.LifeIndex;
            }
        }

        private void OpenNewsDetailDrawer()
        {
            if (_activeDrawer == DrawerType.News)
            {
                IsNewsDetailOpen = false;
                _activeDrawer = DrawerType.None;
            }
            else
            {
                IsWeatherDetailOpen = false;
                IsDishDetailOpen = false;
                IsHourlyDetailOpen = false;
                IsAirQualityDetailOpen = false;
                IsLifeIndexDetailOpen = false;
                IsNewsDetailOpen = true;
                _activeDrawer = DrawerType.News;
            }
        }

        private async Task LoadFlowerDataAsync()
        {
            try
            {
                var overview = await _flowerMarketService.GetMarketOverviewAsync().ConfigureAwait(false);
                if (overview != null)
                {
                    FlowerAvgPrice = overview.AvgPrice > 0 ? $"¥{overview.AvgPrice:F2}" : "--";
                    FlowerPriceChange = overview.PriceChange != 0
                        ? $"{(overview.PriceChange > 0 ? "+" : "")}{overview.PriceChange:F2}%"
                        : "0.00%";
                    FlowerAlertCount = overview.AlertCount;
                    IsFlowerDataLoaded = true;
                }
            }
            catch
            {
                IsFlowerDataLoaded = false;
            }
        }

        private void OnSelectCity(object parameter)
        {
            var locationId = parameter as string;
            if (!string.IsNullOrEmpty(locationId))
            {
                SelectedLocationId = locationId;
                IsCityPickerOpen = false;
                CitySearchText = string.Empty;
                UpdateSelectedCityName();
                LoadWeatherAsync();
            }
        }

        private async void SearchCitiesAsync(string keyword)
        {
            IsSearchingCities = true;
            SearchMessage = string.Empty;
            SearchLevelHint = string.Empty;
            try
            {
                var result = await CitySearchService.SearchCitiesDetailedAsync(keyword);
                FilteredCities = result.Cities;
                SearchMessage = result.SearchMessage;
                if (!string.IsNullOrWhiteSpace(result.DetectedLevel) && result.DetectedLevel != "自动检测")
                    SearchLevelHint = $"检测为{result.DetectedLevel}搜索";
            }
            finally
            {
                IsSearchingCities = false;
            }
        }

        private void OnNavigateToGames()
        {
            // TODO: Navigate to games page
        }

        private void OnNavigateToNews()
        {
            // TODO: Navigate to news page
        }

        private void OnNavigateToSocial()
        {
            // TODO: Navigate to social page
        }

        private void UpdateSelectedCityName()
        {
            var city = CitySearchService.GetByLocationId(_selectedLocationId);
            if (city != null)
            {
                SelectedCityName = city.DisplayName;
            }
        }
    }

    internal class SimpleRelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Action<object> _executeParam;

        public SimpleRelayCommand(Action execute) { _execute = execute; _executeParam = null; }
        public SimpleRelayCommand(Action<object> executeParam) { _executeParam = executeParam; _execute = null; }

        public bool CanExecute(object parameter) => true;
        public void Execute(object parameter)
        {
            if (_executeParam != null)
                _executeParam(parameter);
            else if (_execute != null)
                _execute();
        }
        public event EventHandler CanExecuteChanged;
    }

    internal class AsyncCommand : ICommand
    {
        private readonly Func<Task> _execute;
        private bool _isExecuting;

        public AsyncCommand(Func<Task> execute) { _execute = execute; }

        public bool CanExecute(object parameter) => !_isExecuting;
        public async void Execute(object parameter)
        {
            if (_isExecuting) return;
            try
            {
                _isExecuting = true;
                OnCanExecuteChanged();
                await _execute();
            }
            finally
            {
                _isExecuting = false;
                OnCanExecuteChanged();
            }
        }
        public event EventHandler CanExecuteChanged;
        protected void OnCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }

    internal class AsyncCommand<T> : ICommand
    {
        private readonly Func<T, Task> _execute;
        private bool _isExecuting;

        public AsyncCommand(Func<T, Task> execute) { _execute = execute; }

        public bool CanExecute(object parameter) => !_isExecuting;
        public async void Execute(object parameter)
        {
            if (_isExecuting) return;
            try
            {
                _isExecuting = true;
                OnCanExecuteChanged();
                if (parameter is T typed)
                    await _execute(typed);
                else if (parameter == null && default(T) == null)
                    await _execute(default);
            }
            finally
            {
                _isExecuting = false;
                OnCanExecuteChanged();
            }
        }
        public event EventHandler CanExecuteChanged;
        protected void OnCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
