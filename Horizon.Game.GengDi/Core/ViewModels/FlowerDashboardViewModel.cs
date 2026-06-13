using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Horizon.Game.GengDi.Core.Services;
using Horizon.Game.Message.Network;

namespace Horizon.Game.GengDi.Core.ViewModels
{
    public class FlowerDashboardViewModel : ViewModelBase
    {
        private readonly FlowerMarketService _marketService;
        private readonly FlowerAlertService _alertService;
        private readonly FlowerSpeciesLookupService _speciesLookup = FlowerSpeciesLookupService.Instance;
        private decimal _avgPrice;
        private decimal _priceChangePercent;
        private int _alertCount;
        private bool _isLoading;
        private string _priceChangeDirection = "";
        private ObservableCollection<SpeciesPriceItem> _topSpecies = new();
        private ObservableCollection<AlertItem> _recentAlerts = new();

        public decimal AvgPrice
        {
            get => _avgPrice;
            set => SetProperty(ref _avgPrice, value);
        }

        public decimal PriceChangePercent
        {
            get => _priceChangePercent;
            set
            {
                if (SetProperty(ref _priceChangePercent, value))
                {
                    PriceChangeDirection = value >= 0 ? "↑" : "↓";
                    OnPropertyChanged(nameof(PriceChangeColor));
                }
            }
        }

        public string PriceChangeDirection
        {
            get => _priceChangeDirection;
            set => SetProperty(ref _priceChangeDirection, value);
        }

        public string PriceChangeColor => _priceChangePercent >= 0 ? "#26A69A" : "#EF5350";

        public int AlertCount
        {
            get => _alertCount;
            set => SetProperty(ref _alertCount, value);
        }

        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        public ObservableCollection<SpeciesPriceItem> TopSpecies
        {
            get => _topSpecies;
            set => SetProperty(ref _topSpecies, value);
        }

        public ObservableCollection<AlertItem> RecentAlerts
        {
            get => _recentAlerts;
            set => SetProperty(ref _recentAlerts, value);
        }

        public ICommand RefreshCommand { get; }

        public FlowerDashboardViewModel()
        {
            _marketService = new FlowerMarketService();
            _alertService = new FlowerAlertService();
            RefreshCommand = new AsyncCommand(LoadDashboardAsync);
        }

        public async Task LoadDashboardAsync()
        {
            IsLoading = true;
            try
            {
                var overviewTask = _marketService.GetMarketOverviewAsync();
                var alertsTask = _alertService.GetAlertsAsync(0, 0, 10);

                await Task.WhenAll(overviewTask, alertsTask).ConfigureAwait(false);

                var overview = overviewTask.Result;
                var alerts = alertsTask.Result;

                if (overview != null)
                {
                    AvgPrice = overview.AvgPrice;
                    PriceChangePercent = overview.PriceChange;
                    AlertCount = overview.AlertCount;

                    if (overview.Snapshots != null && overview.Snapshots.Count > 0)
                    {
                        var grouped = overview.Snapshots
                            .GroupBy(s => s.SpeciesId)
                            .Select(g =>
                            {
                                var latest = g.OrderByDescending(s => s.SnapshotTime).First();
                                var previous = g.OrderByDescending(s => s.SnapshotTime).Skip(1).FirstOrDefault();
                                var change = previous != null
                                    ? (double)((latest.AvgPrice - previous.AvgPrice) / previous.AvgPrice * 100)
                                    : 0;
                                return new SpeciesPriceItem
                                {
                                    SpeciesId = latest.SpeciesId,
                                    SpeciesName = GetSpeciesName(latest.SpeciesId),
                                    SpeciesIcon = GetSpeciesIcon(latest.SpeciesId),
                                    Price = latest.AvgPrice,
                                    ChangePercent = (decimal)change,
                                    IsPositive = change >= 0,
                                    ForecastTrend = change > 10 ? "Up" : change < -10 ? "Down" : "Stable"
                                };
                            })
                            .OrderByDescending(s => Math.Abs(s.ChangePercent))
                            .ToList();

                        foreach (var item in grouped)
                        {
                            try
                            {
                                var forecast = await _marketService.GetPriceForecastAsync((int)item.SpeciesId, 7).ConfigureAwait(false);
                                if (forecast != null && forecast.PredictedPrices != null && forecast.PredictedPrices.Count > 0)
                                {
                                    var forecastPrice = forecast.PredictedPrices.Last().PredictedPrice;
                                    var currentPrice = item.Price;
                                    if (currentPrice > 0)
                                    {
                                        var forecastChange = (double)((forecastPrice - currentPrice) / currentPrice * 100);
                                        item.ForecastTrend = forecastChange > 10 ? "Up" : forecastChange < -10 ? "Down" : "Stable";
                                    }
                                }
                            }
                            catch (Exception ex) { Debug.WriteLine($"[FlowerDashboardViewModel] {nameof(LoadDashboardAsync)}-Forecast: {ex.Message}"); }
                        }

                        TopSpecies = new ObservableCollection<SpeciesPriceItem>(grouped);
                    }
                }

                if (alerts != null && alerts.Count > 0)
                {
                    RecentAlerts = new ObservableCollection<AlertItem>(
                        alerts.Select(a => new AlertItem
                        {
                            SpeciesId = a.SpeciesId,
                            SpeciesName = GetSpeciesName(a.SpeciesId),
                            AlertType = a.AlertType,
                            AlertTypeDisplay = GetAlertTypeDisplay(a.AlertType),
                            Message = a.Message,
                            TriggeredValue = a.TriggeredValue,
                            ThresholdValue = a.ThresholdValue,
                            AlertLevel = GetAlertLevel(a.AlertType),
                            LevelIcon = GetLevelIcon(a.AlertType),
                            CreatedAt = a.CreatedAt
                        }));
                }
                else
                {
                    RecentAlerts = new ObservableCollection<AlertItem>();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FlowerDashboardViewModel] {nameof(LoadDashboardAsync)}: {ex.Message}");
                TopSpecies = new ObservableCollection<SpeciesPriceItem>();
                RecentAlerts = new ObservableCollection<AlertItem>();
            }
            finally
            {
                IsLoading = false;
            }
        }

        private string GetSpeciesName(long speciesId) => _speciesLookup.GetSpeciesName((int)speciesId);

        private static string GetSpeciesIcon(long speciesId) => speciesId switch
        {
            1 => "🌹",
            2 => "百合",
            3 => "🌸",
            4 => "💐",
            5 => "🌿",
            _ => "🌺"
        };

        private static string GetAlertTypeDisplay(AlertConditionType alertType) => alertType switch
        {
            AlertConditionType.PriceAbove => "价格超上限",
            AlertConditionType.PriceBelow => "价格低于下限",
            AlertConditionType.PriceChangeAbove => "涨幅超阈值",
            AlertConditionType.PriceChangeBelow => "跌幅超阈值",
            _ => alertType.ToString()
        };

        private static string GetAlertLevel(AlertConditionType alertType) => alertType switch
        {
            AlertConditionType.PriceAbove => "danger",
            AlertConditionType.PriceBelow => "danger",
            AlertConditionType.PriceChangeAbove => "warning",
            AlertConditionType.PriceChangeBelow => "warning",
            _ => "info"
        };

        private static string GetLevelIcon(AlertConditionType alertType) => alertType switch
        {
            AlertConditionType.PriceAbove => "🔴",
            AlertConditionType.PriceBelow => "🔴",
            AlertConditionType.PriceChangeAbove => "🟡",
            AlertConditionType.PriceChangeBelow => "🟡",
            _ => "🔵"
        };

        public class SpeciesPriceItem
        {
            public long SpeciesId { get; set; }
            public string SpeciesName { get; set; } = "";
            public string SpeciesIcon { get; set; } = "";
            public decimal Price { get; set; }
            public decimal ChangePercent { get; set; }
            public bool IsPositive { get; set; }
            public string ChangeColor => IsPositive ? "#26A69A" : "#EF5350";
            public string ForecastTrend { get; set; } = "Stable";
            public bool IsExpandSuggestion => ForecastTrend == "Up";
            public bool IsEarlyHarvestSuggestion => ForecastTrend == "Down";
            public string ForecastTrendDisplay => ForecastTrend switch
            {
                "Up" => "↑",
                "Down" => "↓",
                _ => "→"
            };
        }

        public class AlertItem
        {
            public long SpeciesId { get; set; }
            public string SpeciesName { get; set; } = "";
            public AlertConditionType AlertType { get; set; }
            public string AlertTypeDisplay { get; set; } = "";
            public string Message { get; set; } = "";
            public decimal TriggeredValue { get; set; }
            public decimal ThresholdValue { get; set; }
            public string AlertLevel { get; set; } = "info";
            public string LevelIcon { get; set; } = "🔵";
            public DateTime CreatedAt { get; set; }
        }
    }
}
