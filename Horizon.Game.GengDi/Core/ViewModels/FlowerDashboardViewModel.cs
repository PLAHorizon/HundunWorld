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
        private decimal _priceChangeAbsolute;
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

        public decimal PriceChangeAbsolute
        {
            get => _priceChangeAbsolute;
            set => SetProperty(ref _priceChangeAbsolute, value);
        }

        public string PriceChangeDirection
        {
            get => _priceChangeDirection;
            set => SetProperty(ref _priceChangeDirection, value);
        }

        public string PriceChangeColor => _priceChangePercent >= 0 ? "#26A69A" : "#EF5350";

        /// <summary>绝对金额变化显示文本（如 +¥0.28 / -¥0.28），对应原型今日均价卡 metric-sub。</summary>
        public string PriceChangeAbsoluteDisplay => _priceChangeAbsolute >= 0
            ? $"+¥{_priceChangeAbsolute:F2}"
            : $"-¥{-_priceChangeAbsolute:F2}";

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
            InitMockData();
        }

        /// <summary>
        /// 用模拟数据初始化仪表盘（参考设计原型 flower-market-data.html Section 1）。
        /// 服务返回有效数据后会覆盖这些值。
        /// </summary>
        private void InitMockData()
        {
            AvgPrice = 12.50m;
            PriceChangePercent = 2.34m;
            PriceChangeAbsolute = 0.28m;
            AlertCount = 5;

            TopSpecies = new ObservableCollection<SpeciesPriceItem>
            {
                new SpeciesPriceItem
                {
                    SpeciesId = 1, SpeciesName = "红玫瑰", SpeciesIcon = "🌹",
                    Price = 8.50m, ChangePercent = 3.2m, IsPositive = true, ForecastTrend = "Up"
                },
                new SpeciesPriceItem
                {
                    SpeciesId = 2, SpeciesName = "百合", SpeciesIcon = "🌷",
                    Price = 15.20m, ChangePercent = -1.5m, IsPositive = false, ForecastTrend = "Down"
                },
                new SpeciesPriceItem
                {
                    SpeciesId = 3, SpeciesName = "康乃馨", SpeciesIcon = "🌸",
                    Price = 6.80m, ChangePercent = 0.8m, IsPositive = true, ForecastTrend = "Stable"
                },
                new SpeciesPriceItem
                {
                    SpeciesId = 4, SpeciesName = "混合花束", SpeciesIcon = "💐",
                    Price = 25.00m, ChangePercent = 5.6m, IsPositive = true, ForecastTrend = "Up"
                },
                new SpeciesPriceItem
                {
                    SpeciesId = 5, SpeciesName = "绿植", SpeciesIcon = "🪴",
                    Price = 18.50m, ChangePercent = -2.1m, IsPositive = false, ForecastTrend = "Down"
                },
            };

            RecentAlerts = new ObservableCollection<AlertItem>
            {
                new AlertItem
                {
                    SpeciesId = 1, SpeciesName = "红玫瑰",
                    AlertTypeDisplay = "价格突破",
                    Message = "价格跌破预警阈值，建议关注补仓时机。",
                    TriggeredValue = 8.20m, ThresholdValue = 8.50m,
                    AlertLevel = "danger", LevelIcon = "🔴",
                    TriggerLabel = "触发值", ThresholdLabel = "阈值",
                    TriggeredValueDisplay = "¥8.20", ThresholdValueDisplay = "¥8.50",
                    TriggeredValueColor = "#EF5350"
                },
                new AlertItem
                {
                    SpeciesId = 2, SpeciesName = "百合",
                    AlertTypeDisplay = "库存不足",
                    Message = "商户库存低于安全水位，可能影响供应。",
                    TriggeredValue = 120m, ThresholdValue = 500m,
                    AlertLevel = "warning", LevelIcon = "🟡",
                    TriggerLabel = "当前库存", ThresholdLabel = "安全",
                    TriggeredValueDisplay = "120", ThresholdValueDisplay = "500",
                    TriggeredValueColor = "#FF9800"
                },
                new AlertItem
                {
                    SpeciesId = 4, SpeciesName = "混合花束",
                    AlertTypeDisplay = "异常波动",
                    Message = "短时涨幅超过 5%，疑似异常交易。",
                    TriggeredValue = 5.6m, ThresholdValue = 5m,
                    AlertLevel = "danger", LevelIcon = "🔴",
                    TriggerLabel = "波动幅度", ThresholdLabel = "阈值",
                    TriggeredValueDisplay = "+5.6%", ThresholdValueDisplay = "5%",
                    TriggeredValueColor = "#26A69A"
                },
            };
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
                    PriceChangeAbsolute = overview.AvgPrice * overview.PriceChange / 100m;
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
                        alerts.Select(a =>
                        {
                            var display = GetAlertTypeDisplay(a.AlertType);
                            var item = new AlertItem
                            {
                                SpeciesId = a.SpeciesId,
                                SpeciesName = GetSpeciesName(a.SpeciesId),
                                AlertType = a.AlertType,
                                AlertTypeDisplay = display,
                                Message = a.Message,
                                TriggeredValue = a.TriggeredValue,
                                ThresholdValue = a.ThresholdValue,
                                AlertLevel = GetAlertLevel(a.AlertType),
                                LevelIcon = GetLevelIcon(a.AlertType),
                                CreatedAt = a.CreatedAt
                            };
                            // 根据预警类型设置触发值标签和颜色
                            if (display.Contains("库存"))
                            {
                                item.TriggerLabel = "当前库存";
                                item.ThresholdLabel = "安全";
                                item.TriggeredValueDisplay = a.TriggeredValue.ToString("F0");
                                item.ThresholdValueDisplay = a.ThresholdValue.ToString("F0");
                                item.TriggeredValueColor = "#FF9800";
                            }
                            else if (display.Contains("涨幅") || display.Contains("跌幅") || display.Contains("波动"))
                            {
                                item.TriggerLabel = "波动幅度";
                                item.ThresholdLabel = "阈值";
                                item.TriggeredValueDisplay = $"+{a.TriggeredValue:F1}%";
                                item.ThresholdValueDisplay = $"{a.ThresholdValue:F0}%";
                                item.TriggeredValueColor = "#26A69A";
                            }
                            else
                            {
                                item.TriggerLabel = "触发值";
                                item.ThresholdLabel = "阈值";
                                item.TriggeredValueDisplay = $"¥{a.TriggeredValue:F2}";
                                item.ThresholdValueDisplay = $"¥{a.ThresholdValue:F2}";
                                item.TriggeredValueColor = "#EF5350";
                            }
                            return item;
                        }));
                }
                // 服务无数据时保留已有模拟数据，避免界面空白
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FlowerDashboardViewModel] {nameof(LoadDashboardAsync)}: {ex.Message}");
                // 异常时保留模拟数据，避免界面空白
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
            2 => "🌷",
            3 => "🌸",
            4 => "💐",
            5 => "🪴",
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

            /// <summary>触发值标签（触发值/当前库存/波动幅度），对应原型 alert-trigger 中不同类型的标签。</summary>
            public string TriggerLabel { get; set; } = "触发值";

            /// <summary>阈值标签（阈值/安全），对应原型中不同类型的阈值标签。</summary>
            public string ThresholdLabel { get; set; } = "阈值";

            /// <summary>触发值显示文本（含 ¥ 或 % 前后缀），对应原型中带颜色的 span。</summary>
            public string TriggeredValueDisplay { get; set; } = "";

            /// <summary>阈值显示文本（含 ¥ 或 % 前后缀）。</summary>
            public string ThresholdValueDisplay { get; set; } = "";

            /// <summary>触发值颜色（text-down #EF5350 / warning #FF9800 / text-up #26A69A）。</summary>
            public string TriggeredValueColor { get; set; } = "#EF5350";
        }
    }
}
