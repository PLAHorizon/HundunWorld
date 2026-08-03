using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Controls;
using Horizon.Game.GengDi.Core.Services;
using Horizon.Game.GengDi.Core.Views;
using Horizon.Game.Message.Network;

namespace Horizon.Game.GengDi.Core.ViewModels
{
    public class FlowerWorkbenchViewModel : ViewModelBase
    {
        private readonly FlowerMarketService _marketService;
        private readonly FlowerIoTService _iotService;
        private readonly FlowerMerchantService _merchantService;
        private readonly FlowerOrderService _orderService;
        private readonly FlowerAIService _aiService;
        private readonly FlowerShopService _shopService;
        private readonly FlowerSpeciesLookupService _speciesLookup = FlowerSpeciesLookupService.Instance;

        private bool _isLoading;
        private string _userRole = "Farmer";

        private decimal _forecastAvgPrice;
        private decimal _forecastPriceChangePercent;
        private string _forecastPriceChangeDirection = "";
        private int _forecastAlertCount;
        private ObservableCollection<SpeciesPriceRankItem> _speciesPriceRanking = new();

        private int _plantingActiveBatches;
        private int _plantingPendingAdvice;
        private int _plantingSensorAlerts;
        private ObservableCollection<PlantingBatchSummaryItem> _activeBatchList = new();

        private int _harvestPendingBatches;
        private decimal _harvestSevenDayVolume;
        private int _harvestPendingListings;
        private ObservableCollection<HarvestBatchSummaryItem> _pendingHarvestList = new();

        private int _salesPendingShipments;
        private decimal _salesTodayRevenue;
        private int _salesStockAlerts;
        private ObservableCollection<SalesOrderSummaryItem> _pendingShipList = new();
        private ObservableCollection<OrderStatusDistributionItem> _orderStatusDistribution = new();

        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        public string UserRole
        {
            get => _userRole;
            set
            {
                if (SetProperty(ref _userRole, value))
                {
                    OnPropertyChanged(nameof(ShowForecastQuadrant));
                    OnPropertyChanged(nameof(ShowPlantingQuadrant));
                    OnPropertyChanged(nameof(ShowHarvestQuadrant));
                    OnPropertyChanged(nameof(ShowSalesQuadrant));
                    OnPropertyChanged(nameof(QuadrantLayout));
                }
            }
        }

        public bool ShowForecastQuadrant => _userRole == "Farmer" || _userRole == "Buyer";
        public bool ShowPlantingQuadrant => _userRole == "Farmer" || _userRole == "Merchant";
        public bool ShowHarvestQuadrant => _userRole == "Farmer" || _userRole == "Merchant";
        public bool ShowSalesQuadrant => true;

        public string QuadrantLayout
        {
            get
            {
                int visibleCount = 0;
                if (ShowForecastQuadrant) visibleCount++;
                if (ShowPlantingQuadrant) visibleCount++;
                if (ShowHarvestQuadrant) visibleCount++;
                if (ShowSalesQuadrant) visibleCount++;

                return visibleCount <= 2 ? "SingleColumn" : "TwoByTwo";
            }
        }

        public decimal ForecastAvgPrice
        {
            get => _forecastAvgPrice;
            set => SetProperty(ref _forecastAvgPrice, value);
        }

        public decimal ForecastPriceChangePercent
        {
            get => _forecastPriceChangePercent;
            set
            {
                if (SetProperty(ref _forecastPriceChangePercent, value))
                {
                    ForecastPriceChangeDirection = value >= 0 ? "↑" : "↓";
                    OnPropertyChanged(nameof(ForecastPriceChangeColor));
                }
            }
        }

        public string ForecastPriceChangeDirection
        {
            get => _forecastPriceChangeDirection;
            set => SetProperty(ref _forecastPriceChangeDirection, value);
        }

        public string ForecastPriceChangeColor => _forecastPriceChangePercent >= 0 ? "#26A69A" : "#EF5350";

        public int ForecastAlertCount
        {
            get => _forecastAlertCount;
            set => SetProperty(ref _forecastAlertCount, value);
        }

        public ObservableCollection<SpeciesPriceRankItem> SpeciesPriceRanking
        {
            get => _speciesPriceRanking;
            set => SetProperty(ref _speciesPriceRanking, value);
        }

        public int PlantingActiveBatches
        {
            get => _plantingActiveBatches;
            set => SetProperty(ref _plantingActiveBatches, value);
        }

        public int PlantingPendingAdvice
        {
            get => _plantingPendingAdvice;
            set => SetProperty(ref _plantingPendingAdvice, value);
        }

        public int PlantingSensorAlerts
        {
            get => _plantingSensorAlerts;
            set => SetProperty(ref _plantingSensorAlerts, value);
        }

        public ObservableCollection<PlantingBatchSummaryItem> ActiveBatchList
        {
            get => _activeBatchList;
            set => SetProperty(ref _activeBatchList, value);
        }

        public int HarvestPendingBatches
        {
            get => _harvestPendingBatches;
            set => SetProperty(ref _harvestPendingBatches, value);
        }

        public decimal HarvestSevenDayVolume
        {
            get => _harvestSevenDayVolume;
            set => SetProperty(ref _harvestSevenDayVolume, value);
        }

        public int HarvestPendingListings
        {
            get => _harvestPendingListings;
            set => SetProperty(ref _harvestPendingListings, value);
        }

        public ObservableCollection<HarvestBatchSummaryItem> PendingHarvestList
        {
            get => _pendingHarvestList;
            set => SetProperty(ref _pendingHarvestList, value);
        }

        public int SalesPendingShipments
        {
            get => _salesPendingShipments;
            set => SetProperty(ref _salesPendingShipments, value);
        }

        public decimal SalesTodayRevenue
        {
            get => _salesTodayRevenue;
            set => SetProperty(ref _salesTodayRevenue, value);
        }

        public int SalesStockAlerts
        {
            get => _salesStockAlerts;
            set => SetProperty(ref _salesStockAlerts, value);
        }

        public ObservableCollection<SalesOrderSummaryItem> PendingShipList
        {
            get => _pendingShipList;
            set => SetProperty(ref _pendingShipList, value);
        }

        public ObservableCollection<OrderStatusDistributionItem> OrderStatusDistribution
        {
            get => _orderStatusDistribution;
            set => SetProperty(ref _orderStatusDistribution, value);
        }

        public ICommand RefreshCommand { get; }
        public ICommand NavigateToForecastCommand { get; }
        public ICommand NavigateToPlantingCommand { get; }
        public ICommand NavigateToHarvestCommand { get; }
        public ICommand NavigateToSalesCommand { get; }
        public ICommand QuickRecordHarvestCommand { get; }
        public ICommand QuickListProductCommand { get; }
        public ICommand QuickViewAlertsCommand { get; }
        public ICommand QuickAIAssistantCommand { get; }

        public FlowerWorkbenchViewModel()
        {
            _marketService = new FlowerMarketService();
            _iotService = new FlowerIoTService();
            _merchantService = new FlowerMerchantService();
            _orderService = new FlowerOrderService();
            _aiService = new FlowerAIService();
            _shopService = new FlowerShopService();

            RefreshCommand = new AsyncCommand(LoadWorkbenchAsync);
            NavigateToForecastCommand = new RelayCommand(() => NavigateToShell("FlowerDashboard"));
            NavigateToPlantingCommand = new RelayCommand(() => NavigateToShell("FlowerPlantingAdvice"));
            NavigateToHarvestCommand = new RelayCommand(() => NavigateToShell("FlowerPlantingAdvice"));
            NavigateToSalesCommand = new RelayCommand(() => NavigateToShell("FlowerMerchant"));
            QuickRecordHarvestCommand = new RelayCommand(() => NavigateToShell("FlowerPlantingAdvice"));
            QuickListProductCommand = new RelayCommand(() => NavigateToShell("FlowerMerchant"));
            QuickViewAlertsCommand = new RelayCommand(() => NavigateToShell("FlowerAlertCenter"));
            QuickAIAssistantCommand = new RelayCommand(() => NavigateToShell("FlowerAIAssistant"));

            _ = LoadWorkbenchAsync();
        }

        public async Task LoadWorkbenchAsync()
        {
            IsLoading = true;
            try
            {
                var tasks = new List<Task>();

                if (ShowForecastQuadrant)
                    tasks.Add(LoadForecastQuadrantAsync());

                if (ShowPlantingQuadrant)
                    tasks.Add(LoadPlantingQuadrantAsync());

                if (ShowHarvestQuadrant)
                    tasks.Add(LoadHarvestQuadrantAsync());

                if (ShowSalesQuadrant)
                    tasks.Add(LoadSalesQuadrantAsync());

                if (tasks.Count > 0)
                    await Task.WhenAll(tasks).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FlowerWorkbenchViewModel] {nameof(LoadWorkbenchAsync)}: {ex.Message}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task LoadForecastQuadrantAsync()
        {
            try
            {
                var overview = await _marketService.GetMarketOverviewAsync().ConfigureAwait(false);
                if (overview != null)
                {
                    ForecastAvgPrice = overview.AvgPrice;
                    ForecastPriceChangePercent = overview.PriceChange;
                    ForecastAlertCount = overview.AlertCount;

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
                                return new SpeciesPriceRankItem
                                {
                                    SpeciesId = latest.SpeciesId,
                                    SpeciesName = GetSpeciesName(latest.SpeciesId),
                                    SpeciesIcon = GetSpeciesIcon(latest.SpeciesId),
                                    Price = latest.AvgPrice,
                                    ChangePercent = (decimal)change,
                                    IsPositive = change >= 0
                                };
                            })
                            .OrderByDescending(s => s.Price)
                            .Take(5)
                            .ToList();

                        SpeciesPriceRanking = new ObservableCollection<SpeciesPriceRankItem>(grouped);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FlowerWorkbenchViewModel] {nameof(LoadForecastQuadrantAsync)}: {ex.Message}");
                SpeciesPriceRanking = new ObservableCollection<SpeciesPriceRankItem>();
            }
        }

        private async Task LoadPlantingQuadrantAsync()
        {
            try
            {
                var batches = await _iotService.GetPlantingBatchesAsync("default").ConfigureAwait(false);
                if (batches != null)
                {
                    var activeBatches = batches.Where(b => b.Status == "Planted" || b.Status == "Growing").ToList();
                    PlantingActiveBatches = activeBatches.Count;

                    ActiveBatchList = new ObservableCollection<PlantingBatchSummaryItem>(
                        activeBatches.Take(5).Select(b => new PlantingBatchSummaryItem
                        {
                            BatchName = b.BatchName,
                            SpeciesName = b.SpeciesName,
                            Status = b.Status,
                            PlantingDate = b.PlantingDate,
                            ExpectedHarvestDate = b.ExpectedHarvestDate
                        }));

                    int totalPending = 0;
                    foreach (var batch in activeBatches.Take(3))
                    {
                        var advices = await _aiService.GetActiveAdviceAsync(batch.Id).ConfigureAwait(false);
                        if (advices != null)
                            totalPending += advices.Count(a => a.Status == "Pending");
                    }
                    PlantingPendingAdvice = totalPending;
                }

                var devices = await _iotService.GetIoTDevicesAsync("default").ConfigureAwait(false);
                if (devices != null && devices.Count > 0)
                {
                    var onlineDevice = devices.FirstOrDefault(d => d.OnlineStatus == "Online");
                    if (onlineDevice != null)
                    {
                        var reading = await _iotService.GetLatestSensorReadingAsync(onlineDevice.DeviceCode).ConfigureAwait(false);
                        if (reading != null)
                        {
                            int sensorAlerts = 0;
                            if (reading.SoilMoisture < 40) sensorAlerts++;
                            if (reading.Co2Level > 500) sensorAlerts++;
                            if (reading.Temperature > 35) sensorAlerts++;
                            PlantingSensorAlerts = sensorAlerts;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FlowerWorkbenchViewModel] {nameof(LoadPlantingQuadrantAsync)}: {ex.Message}");
                ActiveBatchList = new ObservableCollection<PlantingBatchSummaryItem>();
            }
        }

        private async Task LoadHarvestQuadrantAsync()
        {
            try
            {
                var batches = await _iotService.GetPlantingBatchesAsync("default").ConfigureAwait(false);
                if (batches != null)
                {
                    var harvestReady = batches.Where(b => b.Status == "HarvestReady" || b.Status == "Growing").ToList();
                    HarvestPendingBatches = harvestReady.Count;

                    PendingHarvestList = new ObservableCollection<HarvestBatchSummaryItem>(
                        harvestReady.Take(5).Select(b => new HarvestBatchSummaryItem
                        {
                            BatchName = b.BatchName,
                            SpeciesName = b.SpeciesName,
                            ExpectedHarvestDate = b.ExpectedHarvestDate,
                            Progress = b.Status == "HarvestReady" ? 85 : 60,
                            ProgressColor = b.Status == "HarvestReady" ? "#26A69A" : "#2962FF"
                        }));

                    decimal totalYield = 0;
                    foreach (var batch in batches.Take(5))
                    {
                        var yields = await _iotService.GetYieldRecordsAsync(batch.Id).ConfigureAwait(false);
                        if (yields != null)
                        {
                            var recentYields = yields.Where(y => y.HarvestDate >= DateTime.Now.AddDays(-7)).ToList();
                            totalYield += recentYields.Sum(y => y.Quantity);
                        }
                    }
                    HarvestSevenDayVolume = totalYield;

                    var merchant = await _merchantService.GetMyMerchantAsync().ConfigureAwait(false);
                    if (merchant != null)
                    {
                        var listings = await _iotService.GetHarvestListingsAsync(merchant.MerchantId, 0).ConfigureAwait(false);
                        if (listings != null)
                            HarvestPendingListings = listings.Count;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FlowerWorkbenchViewModel] {nameof(LoadHarvestQuadrantAsync)}: {ex.Message}");
                PendingHarvestList = new ObservableCollection<HarvestBatchSummaryItem>();
            }
        }

        private async Task LoadSalesQuadrantAsync()
        {
            try
            {
                var merchant = await _merchantService.GetMyMerchantAsync().ConfigureAwait(false);
                if (merchant != null)
                {
                    var orders = await _orderService.GetMerchantOrdersByStatusAsync(merchant.MerchantId, 1).ConfigureAwait(false);
                    SalesPendingShipments = orders?.Count ?? 0;

                    if (orders != null && orders.Count > 0)
                    {
                        PendingShipList = new ObservableCollection<SalesOrderSummaryItem>(
                            orders.Take(5).Select(o => new SalesOrderSummaryItem
                            {
                                OrderNo = o.OrderNo,
                                TotalAmount = o.TotalAmount,
                                CreatedAt = o.CreatedAt
                            }));
                    }

                    var allOrders = await _orderService.GetMerchantOrdersByStatusAsync(merchant.MerchantId, null).ConfigureAwait(false);
                    if (allOrders != null)
                    {
                        SalesTodayRevenue = allOrders.Where(o => o.CreatedAt.Date == DateTime.Today).Sum(o => o.TotalAmount);

                        var pendingShip = allOrders.Count(o => o.Status == 1);
                        var shipped = allOrders.Count(o => o.Status == 2);
                        var completed = allOrders.Count(o => o.Status == 3);

                        OrderStatusDistribution = new ObservableCollection<OrderStatusDistributionItem>
                        {
                            new() { StatusName = "待发货", Count = pendingShip, DotColor = "#FF9800" },
                            new() { StatusName = "已发货", Count = shipped, DotColor = "#2962FF" },
                            new() { StatusName = "已完成", Count = completed, DotColor = "#26A69A" }
                        };
                    }

                    var products = await _shopService.GetMerchantProductsAsync(merchant.MerchantId).ConfigureAwait(false);
                    if (products != null)
                    {
                        SalesStockAlerts = products.Count(p => p.Stock < 10);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FlowerWorkbenchViewModel] {nameof(LoadSalesQuadrantAsync)}: {ex.Message}");
                PendingShipList = new ObservableCollection<SalesOrderSummaryItem>();
                OrderStatusDistribution = new ObservableCollection<OrderStatusDistributionItem>();
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

        private static void NavigateToShell(string tag)
        {
            if (App.MainWindow?.Content is MainView mainView && mainView.DataContext is MainViewModel viewModel)
            {
                viewModel.NavigateTo(tag);
            }
        }
    }

    public class SpeciesPriceRankItem
    {
        public long SpeciesId { get; set; }
        public string SpeciesName { get; set; } = "";
        public string SpeciesIcon { get; set; } = "";
        public decimal Price { get; set; }
        public decimal ChangePercent { get; set; }
        public bool IsPositive { get; set; }
        public string ChangeColor => IsPositive ? "#26A69A" : "#EF5350";
        public string ChangeArrow => IsPositive ? "↑" : "↓";
    }

    public class PlantingBatchSummaryItem
    {
        public string BatchName { get; set; } = "";
        public string SpeciesName { get; set; } = "";
        public string Status { get; set; } = "";
        public DateTime PlantingDate { get; set; }
        public DateTime? ExpectedHarvestDate { get; set; }
        public string StatusDisplay => Status switch
        {
            "Planted" => "已种植",
            "Growing" => "生长中",
            "HarvestReady" => "待采收",
            "Harvested" => "已采收",
            _ => Status
        };
        public string StatusColor => Status switch
        {
            "Planted" => "#42A5F5",
            "Growing" => "#66BB6A",
            "HarvestReady" => "#FFA726",
            "Harvested" => "#78909C",
            _ => "#888888"
        };
    }

    public class HarvestBatchSummaryItem
    {
        public string BatchName { get; set; } = "";
        public string SpeciesName { get; set; } = "";
        public DateTime? ExpectedHarvestDate { get; set; }
        public string ExpectedDateDisplay => ExpectedHarvestDate?.ToString("MM/dd") ?? "-";
        public int Progress { get; set; } = 50;
        public string ProgressColor { get; set; } = "#26A69A";
    }

    public class SalesOrderSummaryItem
    {
        public string OrderNo { get; set; } = "";
        public decimal TotalAmount { get; set; }
        public DateTime CreatedAt { get; set; }
        public string CreatedDisplay => CreatedAt.ToString("MM/dd HH:mm");
    }

    public class OrderStatusDistributionItem
    {
        public string StatusName { get; set; } = "";
        public int Count { get; set; }
        public string DotColor { get; set; } = "#FF9800";
    }
}
