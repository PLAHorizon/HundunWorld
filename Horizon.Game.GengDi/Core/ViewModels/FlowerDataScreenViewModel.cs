using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Horizon.Game.GengDi.Core.Services;

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

        public ICommand RefreshCommand => _refreshCommand;

        public FlowerDataScreenViewModel()
        {
            _refreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsLoading);
            LoadDataAsync();
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
                if (regional != null)
                {
                    RegionHeatmapData = new ObservableCollection<RegionHeatItem>(
                        regional.Select(r => new RegionHeatItem
                        {
                            RegionName = r.RegionName,
                            DemandIndex = r.DemandIndex
                        }));
                }

                var supplyDemand = supplyDemandTask.Result;
                if (supplyDemand != null)
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
                if (transactions != null)
                {
                    RecentTrades = new ObservableCollection<TradeFlowItem>(
                        transactions.Select(t => new TradeFlowItem
                        {
                            TradeTime = t.TradeTime,
                            SpeciesName = t.SpeciesName,
                            Price = t.Price,
                            Quantity = t.Quantity,
                            Market = t.Market
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
        }
    }
}
