using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Horizon.Game.GengDi.Core.Helpers;
using Horizon.Game.GengDi.Core.Services;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;

namespace Horizon.Game.GengDi.Core.ViewModels
{
    public class FlowerDataScreenViewModel : ViewModelBase, IDisposable, ICancelableViewModel
    {
        private readonly FlowerMarketService _marketService = new();
        private System.Threading.Timer? _autoRefreshTimer;
        /// <summary>导航离开后被置为 true，阻止后续 LoadDataAsync 设置属性触发 PropertyChanged。</summary>
        private volatile bool _isCancelled;

        private decimal _todayTradeAmount;
        private int _tradeCount;
        private int _activeSpeciesCount;
        private int _onlineMerchantCount;
        private ObservableCollection<RegionHeatItem> _regionHeatmapData = new();
        private ObservableCollection<SupplyDemandItem> _supplyDemandData = new();
        private ObservableCollection<TradeFlowItem> _recentTrades = new();
        private bool _isAutoRefresh;
        private bool _isLoading;
        private readonly AsyncRelayCommand _refreshCommand;

        // ==== LiveChartsCore 图表数据 ====
        private ISeries[] _priceTrendSeries = Array.Empty<ISeries>();
        private ISeries[] _screenCategoryPieSeries = Array.Empty<ISeries>();
        private ISeries[] _regionColumnSeries = Array.Empty<ISeries>();
        private ISeries[] _tradeFlowSeries = Array.Empty<ISeries>();
        private Axis[] _priceTrendXAxes = new[] { new Axis() };
        private Axis[] _priceTrendYAxes = new[] { new Axis() };
        private Axis[] _regionXAxes = new[] { new Axis() };
        private Axis[] _regionYAxes = new[] { new Axis() };
        private Axis[] _tradeFlowXAxes = new[] { new Axis() };
        private Axis[] _tradeFlowYAxes = new[] { new Axis() };

        public decimal TodayTradeAmount
        {
            get => _todayTradeAmount;
            set => SetProperty(ref _todayTradeAmount, value);
        }

        public int TradeCount
        {
            get => _tradeCount;
            set => SetProperty(ref _tradeCount, value);
        }

        public int ActiveSpeciesCount
        {
            get => _activeSpeciesCount;
            set => SetProperty(ref _activeSpeciesCount, value);
        }

        public int OnlineMerchantCount
        {
            get => _onlineMerchantCount;
            set => SetProperty(ref _onlineMerchantCount, value);
        }

        public ObservableCollection<RegionHeatItem> RegionHeatmapData
        {
            get => _regionHeatmapData;
            set => SetProperty(ref _regionHeatmapData, value);
        }

        public ObservableCollection<SupplyDemandItem> SupplyDemandData
        {
            get => _supplyDemandData;
            set => SetProperty(ref _supplyDemandData, value);
        }

        public ObservableCollection<TradeFlowItem> RecentTrades
        {
            get => _recentTrades;
            set => SetProperty(ref _recentTrades, value);
        }

        public bool IsAutoRefresh
        {
            get => _isAutoRefresh;
            set
            {
                if (SetProperty(ref _isAutoRefresh, value))
                {
                    UpdateAutoRefreshTimer();
                }
            }
        }

        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                if (SetProperty(ref _isLoading, value))
                {
                    _refreshCommand.RaiseCanExecuteChanged();
                }
            }
        }

        // ==== LiveChartsCore 图表数据属性 ====
        public ISeries[] PriceTrendSeries
        {
            get => _priceTrendSeries;
            set => SetProperty(ref _priceTrendSeries, value);
        }

        public ISeries[] ScreenCategoryPieSeries
        {
            get => _screenCategoryPieSeries;
            set => SetProperty(ref _screenCategoryPieSeries, value);
        }

        public ISeries[] RegionColumnSeries
        {
            get => _regionColumnSeries;
            set => SetProperty(ref _regionColumnSeries, value);
        }

        public ISeries[] TradeFlowSeries
        {
            get => _tradeFlowSeries;
            set => SetProperty(ref _tradeFlowSeries, value);
        }

        public Axis[] PriceTrendXAxes
        {
            get => _priceTrendXAxes;
            set => SetProperty(ref _priceTrendXAxes, value);
        }

        public Axis[] PriceTrendYAxes
        {
            get => _priceTrendYAxes;
            set => SetProperty(ref _priceTrendYAxes, value);
        }

        public Axis[] RegionXAxes
        {
            get => _regionXAxes;
            set => SetProperty(ref _regionXAxes, value);
        }

        public Axis[] RegionYAxes
        {
            get => _regionYAxes;
            set => SetProperty(ref _regionYAxes, value);
        }

        public Axis[] TradeFlowXAxes
        {
            get => _tradeFlowXAxes;
            set => SetProperty(ref _tradeFlowXAxes, value);
        }

        public Axis[] TradeFlowYAxes
        {
            get => _tradeFlowYAxes;
            set => SetProperty(ref _tradeFlowYAxes, value);
        }

        public ICommand RefreshCommand => _refreshCommand;

        public FlowerDataScreenViewModel()
        {
            _refreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsLoading);
            InitMockData();
            LoadDataAsync();
        }

        /// <summary>
        /// 用模拟数据初始化数据大屏（参考设计原型 flower-market-data.html Section 2）。
        /// 服务返回有效数据后会覆盖这些值。
        /// </summary>
        private void InitMockData()
        {
            TodayTradeAmount = 1256830m;
            TradeCount = 1856;
            OnlineMerchantCount = 248;
            ActiveSpeciesCount = 32;

            // ==== LiveChartsCore 图表数据初始化 ====
            PriceTrendSeries = FlowerChartHelper.CreatePriceTrendSeries();
            PriceTrendXAxes = FlowerChartHelper.CreateLabelAxis(FlowerChartHelper.PriceTrendLabels);
            PriceTrendYAxes = FlowerChartHelper.CreateValueAxis();
            ScreenCategoryPieSeries = FlowerChartHelper.CreateScreenCategoryPieSeries();
            RegionColumnSeries = FlowerChartHelper.CreateRegionColumnSeries();
            RegionXAxes = FlowerChartHelper.CreateLabelAxis(FlowerChartHelper.RegionLabels);
            RegionYAxes = FlowerChartHelper.CreateValueAxis();
            TradeFlowSeries = FlowerChartHelper.CreateTradeFlowSeries();
            TradeFlowXAxes = FlowerChartHelper.CreateLabelAxis(FlowerChartHelper.TradeFlowLabels);
            TradeFlowYAxes = FlowerChartHelper.CreateValueAxis();

            RegionHeatmapData = new ObservableCollection<RegionHeatItem>
            {
                new RegionHeatItem { RegionName = "华北", DemandIndex = 32 },
                new RegionHeatItem { RegionName = "华东", DemandIndex = 48 },
                new RegionHeatItem { RegionName = "华南", DemandIndex = 26 },
                new RegionHeatItem { RegionName = "西南", DemandIndex = 18 },
                new RegionHeatItem { RegionName = "西北", DemandIndex = 12 },
                new RegionHeatItem { RegionName = "东北", DemandIndex = 14 },
            };

            SupplyDemandData = new ObservableCollection<SupplyDemandItem>
            {
                new SupplyDemandItem { SpeciesName = "红玫瑰", Supply = 1200, Demand = 980, SupplyDemandRatio = 1.22m },
                new SupplyDemandItem { SpeciesName = "百合", Supply = 850, Demand = 920, SupplyDemandRatio = 0.92m },
                new SupplyDemandItem { SpeciesName = "康乃馨", Supply = 600, Demand = 550, SupplyDemandRatio = 1.09m },
                new SupplyDemandItem { SpeciesName = "混合花束", Supply = 450, Demand = 680, SupplyDemandRatio = 0.66m },
                new SupplyDemandItem { SpeciesName = "绿植", Supply = 720, Demand = 480, SupplyDemandRatio = 1.50m },
            };

            RecentTrades = new ObservableCollection<TradeFlowItem>
            {
                new TradeFlowItem { TradeTime = "14:38:21", SpeciesName = "红玫瑰", Price = 8.52m, Quantity = 100, Market = "成交", ChangeAmount = 0.02m },
                new TradeFlowItem { TradeTime = "14:38:18", SpeciesName = "百合", Price = 15.18m, Quantity = 50, Market = "成交", ChangeAmount = -0.02m },
                new TradeFlowItem { TradeTime = "14:38:15", SpeciesName = "混合花束", Price = 25.10m, Quantity = 30, Market = "成交", ChangeAmount = 0.10m },
            };
        }

        private void UpdateAutoRefreshTimer()
        {
            if (_isAutoRefresh)
            {
                _autoRefreshTimer ??= new System.Threading.Timer(
                    _ => _ = LoadDataAsync(),
                    null,
                    TimeSpan.FromSeconds(30),
                    TimeSpan.FromSeconds(30));
            }
            else
            {
                _autoRefreshTimer?.Dispose();
                _autoRefreshTimer = null;
            }
        }

        private async Task LoadDataAsync()
        {
            if (_isCancelled) return;

            DiagLog.Log("[FlowerDataScreenVM] LoadDataAsync START");
            IsLoading = true;
            try
            {
                var statsTask = _marketService.GetDashboardStatsAsync();
                var regionalTask = _marketService.GetRegionalTradeDataAsync();
                var supplyDemandTask = _marketService.GetSupplyDemandDataAsync();
                var transactionsTask = _marketService.GetRecentTransactionsAsync();

                DiagLog.Log("[FlowerDataScreenVM] before Task.WhenAll");
                // 关键修复：移除 ConfigureAwait(false)，让 await 之后在 UI 线程继续。
                // 原实现中属性设置在后台线程，触发 PropertyChanged 与 Avalonia 绑定系统交互，
                // 当 UI 线程正被新页面 DataContext 赋值占用时，会导致同步阻塞死锁。
                // 回到 UI 线程后，属性设置会异步排队，不会与 DataContext 赋值竞争。
                await Task.WhenAll(statsTask, regionalTask, supplyDemandTask, transactionsTask);

                if (_isCancelled)
                {
                    DiagLog.Log("[FlowerDataScreenVM] LoadDataAsync cancelled after WhenAll");
                    return;
                }

                DiagLog.Log("[FlowerDataScreenVM] after Task.WhenAll, setting properties on UI thread");

                var stats = statsTask.Result;
                if (stats != null)
                {
                    TodayTradeAmount = stats.TodayTradeAmount;
                    TradeCount = stats.TradeCount;
                    ActiveSpeciesCount = stats.ActiveSpeciesCount;
                    OnlineMerchantCount = stats.OnlineMerchantCount;
                }

                var regional = regionalTask.Result;
                if (regional != null && regional.Count > 0)
                {
                    RegionHeatmapData = new ObservableCollection<RegionHeatItem>(
                        regional.Select(r => new RegionHeatItem
                        {
                            RegionName = r.RegionName,
                            DemandIndex = r.DemandIndex
                        }));
                }

                var supplyDemand = supplyDemandTask.Result;
                if (supplyDemand != null && supplyDemand.Count > 0)
                {
                    SupplyDemandData = new ObservableCollection<SupplyDemandItem>(
                        supplyDemand.Select(s => new SupplyDemandItem
                        {
                            SpeciesName = s.SpeciesName,
                            Supply = s.Supply,
                            Demand = s.Demand,
                            SupplyDemandRatio = s.SupplyDemandRatio
                        }));
                }

                var transactions = transactionsTask.Result;
                if (transactions != null && transactions.Count > 0)
                {
                    RecentTrades = new ObservableCollection<TradeFlowItem>(
                        transactions.Select(t => new TradeFlowItem
                        {
                            TradeTime = t.TradeTime,
                            SpeciesName = t.SpeciesName,
                            Price = t.Price,
                            Quantity = t.Quantity,
                            Market = t.Market,
                            ChangeAmount = 0
                        }));
                }
            }
            catch (Exception ex)
            {
                DiagLog.Log($"[FlowerDataScreenVM] LoadDataAsync error: {ex.Message}");
            }
            finally
            {
                if (!_isCancelled)
                    IsLoading = false;
                DiagLog.Log("[FlowerDataScreenVM] LoadDataAsync END");
            }
        }

        /// <summary>
        /// 取消后台数据加载并停止自动刷新 Timer。
        /// 页面切换时由 MainViewModel 调用，防止 Timer 回调在新页面绑定初始化期间
        /// 从后台线程触发 PropertyChanged 与 UI 线程竞争。
        /// </summary>
        public void Cancel()
        {
            _isCancelled = true;
            _autoRefreshTimer?.Dispose();
            _autoRefreshTimer = null;
        }

        private async Task RefreshAsync()
        {
            await LoadDataAsync();
        }

        public void Dispose()
        {
            _autoRefreshTimer?.Dispose();
            _autoRefreshTimer = null;
        }

        public class RegionHeatItem : ViewModelBase
        {
            private string _regionName = "";
            private double _demandIndex;

            public string RegionName
            {
                get => _regionName;
                set => SetProperty(ref _regionName, value);
            }

            public double DemandIndex
            {
                get => _demandIndex;
                set => SetProperty(ref _demandIndex, value);
            }
        }

        public class SupplyDemandItem : ViewModelBase
        {
            private string _speciesName = "";
            private int _supply;
            private int _demand;
            private decimal _supplyDemandRatio;

            public string SpeciesName
            {
                get => _speciesName;
                set => SetProperty(ref _speciesName, value);
            }

            public int Supply
            {
                get => _supply;
                set => SetProperty(ref _supply, value);
            }

            public int Demand
            {
                get => _demand;
                set => SetProperty(ref _demand, value);
            }

            public decimal SupplyDemandRatio
            {
                get => _supplyDemandRatio;
                set => SetProperty(ref _supplyDemandRatio, value);
            }
        }

        public class TradeFlowItem : ViewModelBase
        {
            private string _tradeTime = "";
            private string _speciesName = "";
            private decimal _price;
            private int _quantity;
            private string _market = "";
            private decimal _changeAmount;
            private string _changeColor = "#26A69A";

            public string TradeTime
            {
                get => _tradeTime;
                set => SetProperty(ref _tradeTime, value);
            }

            public string SpeciesName
            {
                get => _speciesName;
                set => SetProperty(ref _speciesName, value);
            }

            public decimal Price
            {
                get => _price;
                set => SetProperty(ref _price, value);
            }

            public int Quantity
            {
                get => _quantity;
                set => SetProperty(ref _quantity, value);
            }

            public string Market
            {
                get => _market;
                set => SetProperty(ref _market, value);
            }

            /// <summary>价格变动量（如 +0.02 / -0.02），对应原型 ticker-row 涨跌列。</summary>
            public decimal ChangeAmount
            {
                get => _changeAmount;
                set
                {
                    if (SetProperty(ref _changeAmount, value))
                    {
                        ChangeColor = value >= 0 ? "#26A69A" : "#EF5350";
                        OnPropertyChanged(nameof(ChangeDisplay));
                    }
                }
            }

            /// <summary>涨跌颜色（text-up #26A69A / text-down #EF5350）。</summary>
            public string ChangeColor
            {
                get => _changeColor;
                set => SetProperty(ref _changeColor, value);
            }

            /// <summary>涨跌显示文本（如 +0.02 / -0.02）。</summary>
            public string ChangeDisplay => _changeAmount >= 0 ? $"+{_changeAmount:F2}" : $"{_changeAmount:F2}";
        }
    }
}
