using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Threading;
using Horizon.Game.GengDi.Core.Controls;
using Horizon.Game.GengDi.Core.Helpers;
using Horizon.Game.GengDi.Core.Services;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;

namespace Horizon.Game.GengDi.Core.ViewModels
{
    public class FlowerMerchantViewModel : ViewModelBase, ICancelableViewModel
    {
        private readonly FlowerMerchantService _merchantService;
        private readonly FlowerShopService _shopService;
        private readonly FlowerOrderService _orderService;
        private readonly FlowerMarketService _marketService;
        private readonly FlowerIoTService _iotService;
        private readonly FlowerSubscriptionService _subscriptionService;
        private readonly FlowerSpeciesLookupService _speciesLookup = FlowerSpeciesLookupService.Instance;
        private MerchantInfo _merchantInfo;
        private ObservableCollection<RelatedProduct> _products = new();
        private ObservableCollection<OrderDisplay> _orders = new();
        private ObservableCollection<RefundInfo> _refunds = new();
        private ObservableCollection<SettlementBillInfo> _settlementBills = new();
        private ObservableCollection<ShipperInfo> _shippers = new();
        private ObservableCollection<CategoryInfo> _categories = new();
        private ObservableCollection<FreightTemplateInfo> _freightTemplates = new();
        private ObservableCollection<LadderPriceItem> _ladderPrices = new();
        private ObservableCollection<OrderDisplay> _recentOrders = new();
        private ObservableCollection<CouponInfo> _coupons = new();
        private ObservableCollection<FullDiscountRuleInfo> _fullDiscountRules = new();
        private ObservableCollection<BusinessCategoryInfo> _businessCategories = new();
        private ObservableCollection<BrandInfo> _brands = new();
        private ObservableCollection<PendingSettlementInfo> _pendingSettlements = new();
        private ObservableCollection<AccountItemInfo> _accountItems = new();
        private ObservableCollection<SettlementDetailInfo> _settlementDetails = new();
        private SettlementAccountSummaryInfo _settlementAccountSummary;
        private ObservableCollection<BatchShipItem> _batchShipItems = new();

        private bool _isLoading;
        private bool _isRegistered;
        private volatile bool _isSubmitting;
        private long _merchantId;
        private int _currentTab;
        private string _statusMessage = "";

        /// <summary>
        /// 用于取消所有后台初始化任务的 CTS。
        /// 页面切换时由 MainViewModel 调用 Cancel() 触发取消，
        /// 避免后台任务在 UI 线程被占用时排队等待而导致死锁。
        /// </summary>
        private CancellationTokenSource _cts = new CancellationTokenSource();

        private int _todayOrders;
        private decimal _todayRevenue;
        private int _pendingShipCount;
        private int _totalProducts;
        private decimal _totalRevenue;
        private int _lowStockCount;
        private string _lowStockProductNames = "";

        private string _searchKeyword = "";
        private CategoryInfo _selectedCategory;
        private FreightTemplateInfo _selectedFreightTemplate;

        private string _newProductName = "";
        private decimal _newProductPrice;
        private int _newProductStock;
        private string _newProductDescription = "";
        private string _newProductUnit = "束";
        private string _newProductImages = "";
        private bool _isOpenLadder;
        private string _skuColors = "";
        private string _skuSizes = "";
        private string _skuVersions = "";

        private int _selectedSpeciesId;
        private SuggestedPriceRangeInfo _suggestedPriceRange;
        private bool _isLoadingSuggestedPrice;
        private ObservableCollection<PriceAdjustmentSuggestionInfo> _priceAdjustmentSuggestions = new();
        private bool _showPriceAdjustDialog;
        private PriceAdjustmentSuggestionInfo _selectedPriceAdjustment;

        private bool _isPresale;
        private long? _presaleRelatedBatchId;
        private DateTime? _presaleDeliveryDate;
        private ObservableCollection<PlantingBatchInfo> _availableBatches = new();
        private PlantingBatchInfo _selectedBatch;

        private bool _showEditProductDialog;
        private long _editProductId;
        private string _editProductName = "";
        private decimal _editProductPrice;
        private int _editProductStock;
        private string _editProductDescription = "";
        private string _editProductUnit = "束";
        private string _editProductImages = "";
        private long? _editProductCategoryId;
        private long? _editProductFreightTemplateId;
        private decimal _editProductMarketPrice;

        private bool _showAuditDialog;
        private long _auditProductId;
        private int _auditApproved = 1;
        private string _auditReason = "";

        private int _orderStatusFilter = -1;
        private bool _showShipDialog;
        private long _shippingOrderId;
        private string _expressCompanyName = "";
        private string _shipOrderNumber = "";
        private string _selectedExpressCompany = "";

        private bool _showMerchantLogisticsDialog;
        private bool _isLoadingMerchantLogistics;
        private LogisticsMapDataInfo _merchantLogisticsMapData;

        public ObservableCollection<string> ExpressCompanies { get; } = new ObservableCollection<string>
        {
            "顺丰速运",
            "中通快递",
            "圆通速递",
            "韵达快递",
            "申通快递",
            "极兔速递",
            "邮政快递",
            "京东物流",
            "德邦快递",
            "百世快递"
        };

        private int _refundStatusFilter = -1;

        private string _settlementBankName = "";
        private string _settlementAccountNo = "";
        private string _settlementAccountName = "";

        private string _registerShopName = "";
        private string _registerDescription = "";
        private string _registerPhone = "";
        private int _registerType;
        private bool _showRegisterForm;

        private string _editShopName = "";
        private string _editDescription = "";
        private string _editPhone = "";

        private string _newShipperTag = "";
        private string _newShipperName = "";
        private string _newShipperAddress = "";
        private string _newShipperPhone = "";

        public MerchantInfo MerchantInfo
        {
            get => _merchantInfo;
            set => SetProperty(ref _merchantInfo, value);
        }

        public ObservableCollection<RelatedProduct> Products
        {
            get => _products;
            set => SetProperty(ref _products, value);
        }

        public ObservableCollection<OrderDisplay> Orders
        {
            get => _orders;
            set => SetProperty(ref _orders, value);
        }

        public ObservableCollection<RefundInfo> Refunds
        {
            get => _refunds;
            set => SetProperty(ref _refunds, value);
        }

        public ObservableCollection<SettlementBillInfo> SettlementBills
        {
            get => _settlementBills;
            set => SetProperty(ref _settlementBills, value);
        }

        public ObservableCollection<ShipperInfo> Shippers
        {
            get => _shippers;
            set => SetProperty(ref _shippers, value);
        }

        public ObservableCollection<CategoryInfo> Categories
        {
            get => _categories;
            set => SetProperty(ref _categories, value);
        }

        public ObservableCollection<FreightTemplateInfo> FreightTemplates
        {
            get => _freightTemplates;
            set => SetProperty(ref _freightTemplates, value);
        }

        public ObservableCollection<LadderPriceItem> LadderPrices
        {
            get => _ladderPrices;
            set => SetProperty(ref _ladderPrices, value);
        }

        public ObservableCollection<OrderDisplay> RecentOrders
        {
            get => _recentOrders;
            set => SetProperty(ref _recentOrders, value);
        }

        public ObservableCollection<CouponInfo> Coupons
        {
            get => _coupons;
            set => SetProperty(ref _coupons, value);
        }

        public ObservableCollection<FullDiscountRuleInfo> FullDiscountRules
        {
            get => _fullDiscountRules;
            set => SetProperty(ref _fullDiscountRules, value);
        }

        public ObservableCollection<BusinessCategoryInfo> BusinessCategories
        {
            get => _businessCategories;
            set => SetProperty(ref _businessCategories, value);
        }

        public ObservableCollection<BrandInfo> Brands
        {
            get => _brands;
            set => SetProperty(ref _brands, value);
        }

        public ObservableCollection<PendingSettlementInfo> PendingSettlements
        {
            get => _pendingSettlements;
            set => SetProperty(ref _pendingSettlements, value);
        }

        public ObservableCollection<AccountItemInfo> AccountItems
        {
            get => _accountItems;
            set => SetProperty(ref _accountItems, value);
        }

        public ObservableCollection<SettlementDetailInfo> SettlementDetails
        {
            get => _settlementDetails;
            set => SetProperty(ref _settlementDetails, value);
        }

        public SettlementAccountSummaryInfo SettlementAccountSummary
        {
            get => _settlementAccountSummary;
            set => SetProperty(ref _settlementAccountSummary, value);
        }

        public ObservableCollection<BatchShipItem> BatchShipItems
        {
            get => _batchShipItems;
            set => SetProperty(ref _batchShipItems, value);
        }

        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        public bool IsSubmitting
        {
            get => _isSubmitting;
            set => SetProperty(ref _isSubmitting, value);
        }

        public bool IsRegistered
        {
            get => _isRegistered;
            set
            {
                if (SetProperty(ref _isRegistered, value))
                {
                    OnPropertyChanged(nameof(ShowRegisterPrompt));
                    OnPropertyChanged(nameof(ShowRegisterForm));
                    OnPropertyChanged(nameof(ShowMerchantContent));
                    OnPropertyChanged(nameof(ShowOverviewPanel));
                    OnPropertyChanged(nameof(ShowProductsPanel));
                    OnPropertyChanged(nameof(ShowPublishPanel));
                    OnPropertyChanged(nameof(ShowOrdersPanel));
                    OnPropertyChanged(nameof(ShowRefundsPanel));
                    OnPropertyChanged(nameof(ShowSettlementPanel));
                    OnPropertyChanged(nameof(ShowSettingsPanel));
                    OnPropertyChanged(nameof(ShowCouponPanel));
                    OnPropertyChanged(nameof(ShowFullDiscountPanel));
                    OnPropertyChanged(nameof(ShowFreightPanel));
                }
            }
        }

        public bool ShowRegisterPrompt => !_isRegistered && !_showRegisterForm;
        public bool ShowRegisterForm => !_isRegistered && _showRegisterForm;
        public bool ShowMerchantContent => _isRegistered;

        public int CurrentTab
        {
            get => _currentTab;
            set
            {
                if (SetProperty(ref _currentTab, value))
                {
                    OnPropertyChanged(nameof(ShowOverviewPanel));
                    OnPropertyChanged(nameof(ShowProductsPanel));
                    OnPropertyChanged(nameof(ShowPublishPanel));
                    OnPropertyChanged(nameof(ShowOrdersPanel));
                    OnPropertyChanged(nameof(ShowRefundsPanel));
                    OnPropertyChanged(nameof(ShowSettlementPanel));
                    OnPropertyChanged(nameof(ShowSettingsPanel));
                    OnPropertyChanged(nameof(ShowCouponPanel));
                    OnPropertyChanged(nameof(ShowFullDiscountPanel));
                    OnPropertyChanged(nameof(ShowFreightPanel));
                    for (int i = 0; i < 9; i++)
                        OnPropertyChanged($"Tab{i}Active");
                }
            }
        }

        public bool ShowOverviewPanel => _isRegistered && _currentTab == 0;
        public bool ShowProductsPanel => _isRegistered && _currentTab == 1;
        public bool ShowPublishPanel => _isRegistered && _currentTab == 2;
        public bool ShowOrdersPanel => _isRegistered && _currentTab == 3;
        public bool ShowRefundsPanel => _isRegistered && _currentTab == 4;
        public bool ShowSettlementPanel => _isRegistered && _currentTab == 5;
        public bool ShowSettingsPanel => _isRegistered && _currentTab == 6;
        public bool ShowFreightPanel => _isRegistered && _currentTab == 9;
        public bool ShowCouponPanel => _isRegistered && _currentTab == 7;
        public bool ShowFullDiscountPanel => _isRegistered && _currentTab == 8;

        public bool Tab0Active => _currentTab == 0;
        public bool Tab1Active => _currentTab == 1;
        public bool Tab2Active => _currentTab == 2;
        public bool Tab3Active => _currentTab == 3;
        public bool Tab4Active => _currentTab == 4;
        public bool Tab5Active => _currentTab == 5;
        public bool Tab6Active => _currentTab == 6;
        public bool Tab7Active => _currentTab == 7;
        public bool Tab8Active => _currentTab == 8;

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public int TodayOrders
        {
            get => _todayOrders;
            set => SetProperty(ref _todayOrders, value);
        }

        public decimal TodayRevenue
        {
            get => _todayRevenue;
            set => SetProperty(ref _todayRevenue, value);
        }

        public int PendingShipCount
        {
            get => _pendingShipCount;
            set => SetProperty(ref _pendingShipCount, value);
        }

        public int TotalProducts
        {
            get => _totalProducts;
            set => SetProperty(ref _totalProducts, value);
        }

        public decimal TotalRevenue
        {
            get => _totalRevenue;
            set => SetProperty(ref _totalRevenue, value);
        }

        // ===== LiveChartsCore 图表数据 =====
        private ISeries[] _revenueTrendSeries = Array.Empty<ISeries>();
        public ISeries[] RevenueTrendSeries
        {
            get => _revenueTrendSeries;
            set => SetProperty(ref _revenueTrendSeries, value);
        }

        private ISeries[] _categoryPieSeries = Array.Empty<ISeries>();
        public ISeries[] CategoryPieSeries
        {
            get => _categoryPieSeries;
            set => SetProperty(ref _categoryPieSeries, value);
        }

        private Axis[] _revenueXAxes = new[] { new Axis() };
        public Axis[] RevenueXAxes
        {
            get => _revenueXAxes;
            set => SetProperty(ref _revenueXAxes, value);
        }

        private Axis[] _revenueYAxes = new[] { new Axis() };
        public Axis[] RevenueYAxes
        {
            get => _revenueYAxes;
            set => SetProperty(ref _revenueYAxes, value);
        }

        public int LowStockCount
        {
            get => _lowStockCount;
            set => SetProperty(ref _lowStockCount, value);
        }

        public string LowStockProductNames
        {
            get => _lowStockProductNames;
            set => SetProperty(ref _lowStockProductNames, value);
        }

        public string SearchKeyword
        {
            get => _searchKeyword;
            set => SetProperty(ref _searchKeyword, value);
        }

        public CategoryInfo SelectedCategory
        {
            get => _selectedCategory;
            set => SetProperty(ref _selectedCategory, value);
        }

        public FreightTemplateInfo SelectedFreightTemplate
        {
            get => _selectedFreightTemplate;
            set => SetProperty(ref _selectedFreightTemplate, value);
        }

        public string NewProductName
        {
            get => _newProductName;
            set => SetProperty(ref _newProductName, value);
        }

        public decimal NewProductPrice
        {
            get => _newProductPrice;
            set => SetProperty(ref _newProductPrice, value);
        }

        public int NewProductStock
        {
            get => _newProductStock;
            set => SetProperty(ref _newProductStock, value);
        }

        public string NewProductDescription
        {
            get => _newProductDescription;
            set => SetProperty(ref _newProductDescription, value);
        }

        public string NewProductUnit
        {
            get => _newProductUnit;
            set => SetProperty(ref _newProductUnit, value);
        }

        public string NewProductImages
        {
            get => _newProductImages;
            set
            {
                if (SetProperty(ref _newProductImages, value))
                {
                    UpdateImageList();
                }
            }
        }

        private ObservableCollection<string> _newProductImageList = new();
        public ObservableCollection<string> NewProductImageList
        {
            get => _newProductImageList;
            set => SetProperty(ref _newProductImageList, value);
        }

        private void UpdateImageList()
        {
            _newProductImageList.Clear();
            if (!string.IsNullOrWhiteSpace(_newProductImages))
            {
                var urls = _newProductImages.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(u => u.Trim()).Where(u => !string.IsNullOrEmpty(u));
                foreach (var url in urls)
                    _newProductImageList.Add(url);
            }
            OnPropertyChanged(nameof(NewProductImageList));
        }

        public void AddImage(string imagePath)
        {
            if (string.IsNullOrWhiteSpace(imagePath)) return;
            if (string.IsNullOrWhiteSpace(_newProductImages))
                NewProductImages = imagePath;
            else
                NewProductImages = _newProductImages + "," + imagePath;
        }

        public void RemoveImage(string imagePath)
        {
            if (string.IsNullOrWhiteSpace(imagePath) || string.IsNullOrWhiteSpace(_newProductImages)) return;
            var images = _newProductImages.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(u => u.Trim()).Where(u => u != imagePath).ToList();
            NewProductImages = string.Join(",", images);
        }

        public bool IsOpenLadder
        {
            get => _isOpenLadder;
            set => SetProperty(ref _isOpenLadder, value);
        }

        public string SkuColors
        {
            get => _skuColors;
            set => SetProperty(ref _skuColors, value);
        }

        public string SkuSizes
        {
            get => _skuSizes;
            set => SetProperty(ref _skuSizes, value);
        }

        public string SkuVersions
        {
            get => _skuVersions;
            set => SetProperty(ref _skuVersions, value);
        }

        public int SelectedSpeciesId
        {
            get => _selectedSpeciesId;
            set
            {
                if (SetProperty(ref _selectedSpeciesId, value))
                {
                    _ = LoadSuggestedPriceAsync();
                }
            }
        }

        public SuggestedPriceRangeInfo SuggestedPriceRange
        {
            get => _suggestedPriceRange;
            set
            {
                if (SetProperty(ref _suggestedPriceRange, value))
                {
                    OnPropertyChanged(nameof(HasSuggestedPrice));
                    OnPropertyChanged(nameof(SuggestedPriceDisplay));
                }
            }
        }

        public bool IsLoadingSuggestedPrice
        {
            get => _isLoadingSuggestedPrice;
            set => SetProperty(ref _isLoadingSuggestedPrice, value);
        }

        public bool HasSuggestedPrice => _suggestedPriceRange != null && _suggestedPriceRange.AvgForecastPrice > 0;

        public string SuggestedPriceDisplay => _suggestedPriceRange != null && _suggestedPriceRange.AvgForecastPrice > 0
            ? $"¥{_suggestedPriceRange.MinPrice:F2} ~ ¥{_suggestedPriceRange.MaxPrice:F2}（均价 ¥{_suggestedPriceRange.AvgForecastPrice:F2}）"
            : "暂无建议价";

        public ObservableCollection<PriceAdjustmentSuggestionInfo> PriceAdjustmentSuggestions
        {
            get => _priceAdjustmentSuggestions;
            set
            {
                if (SetProperty(ref _priceAdjustmentSuggestions, value))
                {
                    OnPropertyChanged(nameof(HasPriceAdjustments));
                }
            }
        }

        public bool HasPriceAdjustments => _priceAdjustmentSuggestions.Count > 0;

        public bool ShowPriceAdjustDialog
        {
            get => _showPriceAdjustDialog;
            set => SetProperty(ref _showPriceAdjustDialog, value);
        }

        public PriceAdjustmentSuggestionInfo SelectedPriceAdjustment
        {
            get => _selectedPriceAdjustment;
            set => SetProperty(ref _selectedPriceAdjustment, value);
        }

        public bool IsPresale
        {
            get => _isPresale;
            set
            {
                if (SetProperty(ref _isPresale, value))
                {
                    OnPropertyChanged(nameof(ShowPresaleOptions));
                    if (value) _ = LoadAvailableBatchesAsync();
                }
            }
        }

        public bool ShowPresaleOptions => _isPresale;

        public long? PresaleRelatedBatchId
        {
            get => _presaleRelatedBatchId;
            set => SetProperty(ref _presaleRelatedBatchId, value);
        }

        public DateTime? PresaleDeliveryDate
        {
            get => _presaleDeliveryDate;
            set => SetProperty(ref _presaleDeliveryDate, value);
        }

        public ObservableCollection<PlantingBatchInfo> AvailableBatches
        {
            get => _availableBatches;
            set => SetProperty(ref _availableBatches, value);
        }

        public PlantingBatchInfo SelectedBatch
        {
            get => _selectedBatch;
            set
            {
                if (SetProperty(ref _selectedBatch, value))
                {
                    PresaleRelatedBatchId = value?.Id;
                    if (value?.ExpectedHarvestDate != null)
                        PresaleDeliveryDate = value.ExpectedHarvestDate;
                }
            }
        }

        public bool ShowEditProductDialog { get => _showEditProductDialog; set => SetProperty(ref _showEditProductDialog, value); }
        public long EditProductId { get => _editProductId; set => SetProperty(ref _editProductId, value); }
        public string EditProductName { get => _editProductName; set => SetProperty(ref _editProductName, value); }
        public decimal EditProductPrice { get => _editProductPrice; set => SetProperty(ref _editProductPrice, value); }
        public int EditProductStock { get => _editProductStock; set => SetProperty(ref _editProductStock, value); }
        public string EditProductDescription { get => _editProductDescription; set => SetProperty(ref _editProductDescription, value); }
        public string EditProductUnit { get => _editProductUnit; set => SetProperty(ref _editProductUnit, value); }
        public string EditProductImages { get => _editProductImages; set => SetProperty(ref _editProductImages, value); }
        public long? EditProductCategoryId { get => _editProductCategoryId; set => SetProperty(ref _editProductCategoryId, value); }
        public long? EditProductFreightTemplateId { get => _editProductFreightTemplateId; set => SetProperty(ref _editProductFreightTemplateId, value); }
        public decimal EditProductMarketPrice { get => _editProductMarketPrice; set => SetProperty(ref _editProductMarketPrice, value); }

        public bool ShowAuditDialog { get => _showAuditDialog; set => SetProperty(ref _showAuditDialog, value); }
        public long AuditProductId { get => _auditProductId; set => SetProperty(ref _auditProductId, value); }
        public int AuditApproved { get => _auditApproved; set => SetProperty(ref _auditApproved, value); }
        public string AuditReason { get => _auditReason; set => SetProperty(ref _auditReason, value); }

        private ObservableCollection<ProductSKUInfo> _editProductSKUs = new();
        public ObservableCollection<ProductSKUInfo> EditProductSKUs { get => _editProductSKUs; set => SetProperty(ref _editProductSKUs, value); }
        public bool HasEditSKUs => EditProductSKUs.Count > 0;

        private string _newSkuColor = "";
        private string _newSkuSize = "";
        private string _newSkuVersion = "";
        private decimal _newSkuPrice;
        private long _newSkuStock;

        public string NewSkuColor { get => _newSkuColor; set => SetProperty(ref _newSkuColor, value); }
        public string NewSkuSize { get => _newSkuSize; set => SetProperty(ref _newSkuSize, value); }
        public string NewSkuVersion { get => _newSkuVersion; set => SetProperty(ref _newSkuVersion, value); }
        public decimal NewSkuPrice { get => _newSkuPrice; set => SetProperty(ref _newSkuPrice, value); }
        public long NewSkuStock { get => _newSkuStock; set => SetProperty(ref _newSkuStock, value); }

        public int OrderStatusFilter
        {
            get => _orderStatusFilter;
            set
            {
                if (SetProperty(ref _orderStatusFilter, value))
                {
                    _ = LoadOrdersAsync();
                }
            }
        }

        public bool ShowShipDialog
        {
            get => _showShipDialog;
            set => SetProperty(ref _showShipDialog, value);
        }

        public long ShippingOrderId
        {
            get => _shippingOrderId;
            set => SetProperty(ref _shippingOrderId, value);
        }

        public string ExpressCompanyName
        {
            get => _expressCompanyName;
            set => SetProperty(ref _expressCompanyName, value);
        }

        public string ShipOrderNumber
        {
            get => _shipOrderNumber;
            set => SetProperty(ref _shipOrderNumber, value);
        }

        public string SelectedExpressCompany
        {
            get => _selectedExpressCompany;
            set
            {
                if (SetProperty(ref _selectedExpressCompany, value))
                {
                    if (!string.IsNullOrEmpty(value))
                    {
                        ExpressCompanyName = value;
                    }
                }
            }
        }

        public bool ShowMerchantLogisticsDialog
        {
            get => _showMerchantLogisticsDialog;
            set => SetProperty(ref _showMerchantLogisticsDialog, value);
        }

        public bool IsLoadingMerchantLogistics
        {
            get => _isLoadingMerchantLogistics;
            set
            {
                if (SetProperty(ref _isLoadingMerchantLogistics, value))
                {
                    OnPropertyChanged(nameof(HasMerchantLogisticsNodes));
                    OnPropertyChanged(nameof(HasNoMerchantLogistics));
                }
            }
        }

        public LogisticsMapDataInfo MerchantLogisticsMapData
        {
            get => _merchantLogisticsMapData;
            set
            {
                if (SetProperty(ref _merchantLogisticsMapData, value))
                {
                    OnPropertyChanged(nameof(HasMerchantLogisticsNodes));
                    OnPropertyChanged(nameof(HasNoMerchantLogistics));
                }
            }
        }

        public bool HasMerchantLogisticsNodes => !IsLoadingMerchantLogistics && MerchantLogisticsMapData?.Nodes?.Count > 0;

        public bool HasNoMerchantLogistics => !IsLoadingMerchantLogistics && MerchantLogisticsMapData == null;

        public int RefundStatusFilter
        {
            get => _refundStatusFilter;
            set
            {
                if (SetProperty(ref _refundStatusFilter, value))
                {
                    _ = LoadRefundsAsync();
                }
            }
        }

        public string SettlementBankName
        {
            get => _settlementBankName;
            set => SetProperty(ref _settlementBankName, value);
        }

        public string SettlementAccountNo
        {
            get => _settlementAccountNo;
            set => SetProperty(ref _settlementAccountNo, value);
        }

        public string SettlementAccountName
        {
            get => _settlementAccountName;
            set => SetProperty(ref _settlementAccountName, value);
        }

        public string RegisterShopName
        {
            get => _registerShopName;
            set => SetProperty(ref _registerShopName, value);
        }

        public string RegisterDescription
        {
            get => _registerDescription;
            set => SetProperty(ref _registerDescription, value);
        }

        public string RegisterPhone
        {
            get => _registerPhone;
            set => SetProperty(ref _registerPhone, value);
        }

        public int RegisterType
        {
            get => _registerType;
            set => SetProperty(ref _registerType, value);
        }

        public string EditShopName
        {
            get => _editShopName;
            set => SetProperty(ref _editShopName, value);
        }

        public string EditDescription
        {
            get => _editDescription;
            set => SetProperty(ref _editDescription, value);
        }

        public string EditPhone
        {
            get => _editPhone;
            set => SetProperty(ref _editPhone, value);
        }

        public string NewShipperTag
        {
            get => _newShipperTag;
            set => SetProperty(ref _newShipperTag, value);
        }

        public string NewShipperName
        {
            get => _newShipperName;
            set => SetProperty(ref _newShipperName, value);
        }

        public string NewShipperAddress
        {
            get => _newShipperAddress;
            set => SetProperty(ref _newShipperAddress, value);
        }

        public string NewShipperPhone
        {
            get => _newShipperPhone;
            set => SetProperty(ref _newShipperPhone, value);
        }

        public bool HasProducts => _products.Count > 0;
        public bool HasOrders => _orders.Count > 0;
        public bool HasRefunds => _refunds.Count > 0;
        public bool HasSettlementBills => _settlementBills.Count > 0;
        public bool HasShippers => _shippers.Count > 0;
        public bool HasRecentOrders => _recentOrders.Count > 0;

        public string MerchantTypeDisplay => _merchantInfo?.MerchantType switch
        {
            "Individual" or "0" => "个人商户",
            "Enterprise" or "1" => "企业商户",
            _ => _merchantInfo?.MerchantType ?? ""
        };

        public string ShopGradeDisplay
        {
            get
            {
                if (_merchantInfo == null) return "";
                return _merchantInfo.IsVerified ? "认证商户" : "普通商户";
            }
        }

        public ObservableCollection<SpeciesFilterItem> SpeciesOptions { get; }

        private SpeciesFilterItem _selectedSpeciesFilter;
        public SpeciesFilterItem SelectedSpeciesFilter
        {
            get => _selectedSpeciesFilter;
            set => SetProperty(ref _selectedSpeciesFilter, value);
        }

        public ICommand LoadMerchantCommand { get; }
        public ICommand SwitchTabCommand { get; }
        public ICommand CreateProductCommand { get; }
        public ICommand ToggleProductActiveCommand { get; }
        public ICommand DeleteProductCommand { get; }
        public ICommand RefreshProductsCommand { get; }
        public ICommand RefreshOrdersCommand { get; }
        public ICommand SearchProductsCommand { get; }
        public ICommand ShowRegisterFormCommand { get; }
        public ICommand RegisterMerchantCommand { get; }
        public ICommand CancelRegisterCommand { get; }
        public ICommand SaveShopSettingsCommand { get; }
        public ICommand ShowShipDialogCommand { get; }
        public ICommand ConfirmShipCommand { get; }
        public ICommand CancelShipCommand { get; }
        public ICommand ApproveRefundCommand { get; }
        public ICommand RejectRefundCommand { get; }
        public ICommand SaveSettlementAccountCommand { get; }
        public ICommand AddShipperCommand { get; }
        public ICommand DeleteShipperCommand { get; }
        public ICommand AddLadderPriceCommand { get; }
        public ICommand RemoveLadderPriceCommand { get; }
        public ICommand CreateCouponCommand { get; }
        public ICommand DeleteFullDiscountRuleCommand { get; }
        public ICommand CreateFullDiscountRuleCommand { get; }
        public ICommand ApplyBusinessCategoryCommand { get; }
        public ICommand ApplyBrandCommand { get; }
        public ICommand RequestWithdrawCommand { get; }
        public ICommand AcceptSuggestedPriceCommand { get; }
        public ICommand LoadPriceAdjustmentsCommand { get; }
        public ICommand ConfirmReturnReceivedCommand { get; }
        public ICommand BatchShipOrdersCommand { get; }
        public ICommand ViewSettlementDetailsCommand { get; }
        public ICommand ViewMerchantLogisticsCommand { get; }
        public ICommand CloseMerchantLogisticsDialogCommand { get; }

        public FlowerMerchantViewModel() : this(0) { }

        public FlowerMerchantViewModel(long merchantId)
        {
            DiagLog.Log($"[FlowerMerchantVM] ctor START");
            _merchantId = merchantId;
            _merchantService = new FlowerMerchantService();
            _shopService = new FlowerShopService();
            _orderService = new FlowerOrderService();
            _marketService = new FlowerMarketService();
            _iotService = new FlowerIoTService();
            _subscriptionService = new FlowerSubscriptionService();
            DiagLog.Log("[FlowerMerchantVM] services created");

            LoadMerchantCommand = new AsyncCommand(LoadMerchantAsync);
            SwitchTabCommand = new RelayCommand<int>(t => CurrentTab = t);
            CreateProductCommand = new AsyncCommand(CreateProductAsync);
            ToggleProductActiveCommand = new AsyncCommand<RelatedProduct>(ToggleProductActiveAsync);
            DeleteProductCommand = new AsyncCommand<RelatedProduct>(DeleteProductAsync);
            RefreshProductsCommand = new AsyncCommand(LoadProductsAsync);
            RefreshOrdersCommand = new AsyncCommand(LoadOrdersAsync);
            SearchProductsCommand = new AsyncCommand(SearchProductsAsync);
            ShowRegisterFormCommand = new RelayCommand(() => { _showRegisterForm = true; UpdateRegisterFormVisibility(); });
            RegisterMerchantCommand = new AsyncCommand(RegisterMerchantAsync);
            CancelRegisterCommand = new RelayCommand(() => { _showRegisterForm = false; UpdateRegisterFormVisibility(); });
            SaveShopSettingsCommand = new AsyncCommand(SaveShopSettingsAsync);
            ShowShipDialogCommand = new AsyncCommand<long>(OpenShipDialog);
            ConfirmShipCommand = new AsyncCommand(ConfirmShipAsync);
            CancelShipCommand = new RelayCommand(CancelShip);
            ApproveRefundCommand = new AsyncCommand<long>(id => AuditRefundAsync(id, true));
            RejectRefundCommand = new AsyncCommand<long>(id => AuditRefundAsync(id, false));
            SaveSettlementAccountCommand = new AsyncCommand(SaveSettlementAccountAsync);
            AddShipperCommand = new AsyncCommand(AddShipperAsync);
            DeleteShipperCommand = new AsyncCommand<long>(DeleteShipperAsync);
            AddLadderPriceCommand = new RelayCommand(AddLadderPrice);
            RemoveLadderPriceCommand = new RelayCommand<LadderPriceItem>(RemoveLadderPrice);
            CreateCouponCommand = new AsyncCommand(CreateCouponAsync);
            DeleteFullDiscountRuleCommand = new AsyncCommand<long>(DeleteFullDiscountRuleAsync);
            CreateFullDiscountRuleCommand = new AsyncCommand(CreateFullDiscountRuleAsync);
            ApplyBusinessCategoryCommand = new AsyncCommand(ApplyBusinessCategoryAsync);
            ApplyBrandCommand = new AsyncCommand(ApplyBrandAsync);
            RequestWithdrawCommand = new AsyncCommand(RequestWithdrawAsync);
            AcceptSuggestedPriceCommand = new RelayCommand(AcceptSuggestedPrice);
            LoadPriceAdjustmentsCommand = new AsyncCommand(LoadPriceAdjustmentSuggestionsAsync);
            ConfirmReturnReceivedCommand = new AsyncCommand<long>(ConfirmReturnReceivedAsync);
            BatchShipOrdersCommand = new AsyncCommand(BatchShipOrdersAsync);
            ViewSettlementDetailsCommand = new AsyncCommand<long>(ViewSettlementDetailsAsync);
            ViewMerchantLogisticsCommand = new AsyncCommand<long>(ViewMerchantLogisticsAsync);
            CloseMerchantLogisticsDialogCommand = new RelayCommand(() => ShowMerchantLogisticsDialog = false);

            SpeciesOptions = new ObservableCollection<SpeciesFilterItem>(
                _speciesLookup.GetAllSpecies()
                    .Select(kv => new SpeciesFilterItem { SpeciesId = kv.Key, DisplayName = kv.Value })
            );
            DiagLog.Log($"[FlowerMerchantVM] SpeciesOptions count={SpeciesOptions.Count}");

            // 关键修复：原实现在构造函数中 fire-and-forget 启动 InitializeAsync。
            // 这导致 DataContext 赋值触发绑定初始化期间，后台线程已完成 HTTP 请求并尝试
            // InvokeAsync 回 UI 线程，与绑定初始化竞争。现将启动推迟到 View 的 Loaded 事件，
            // 由 StartInitialization() 显式调用，确保绑定初始化完成后再开始后台加载。
            DiagLog.Log("[FlowerMerchantVM] ctor done (InitializeAsync deferred to StartInitialization)");
        }

        /// <summary>
        /// 启动后台初始化任务。应由 FlowerMerchantView 的 Loaded 事件调用，
        /// 确保 DataContext 绑定初始化已完成，避免后台线程与绑定初始化在 UI 线程上竞争。
        /// 多次调用安全：仅第一次调用生效。
        /// </summary>
        public void StartInitialization()
        {
            if (_initialized) return;
            _initialized = true;
            DiagLog.Log("[FlowerMerchantVM] StartInitialization -> InitializeAsync fire-and-forget");
            _ = InitializeAsync();
        }
        private volatile bool _initialized;

        private async Task RunOnUiThreadAsync(Action action)
        {
            if (_cts.IsCancellationRequested)
                return;

            if (Dispatcher.UIThread.CheckAccess())
            {
                if (!_cts.IsCancellationRequested)
                    action();
                return;
            }

            await Dispatcher.UIThread.InvokeAsync(action);
        }

        /// <summary>
        /// 取消所有后台初始化任务。页面切换时由 MainViewModel 调用。
        /// 实现语义：触发 CTS 取消后，未完成的 RunOnUiThreadAsync 调度将被跳过，
        /// 避免后台线程在 UI 线程被占用时排队等待而导致死锁。
        /// </summary>
        public void Cancel()
        {
            DiagLog.Log("[FlowerMerchantVM] Cancel() called");
            try
            {
                _cts?.Cancel();
            }
            catch (ObjectDisposedException) { }
        }

        private async Task InitializeAsync()
        {
            DiagLog.Log($"[FlowerMerchantVM] InitializeAsync START");
            IsLoading = true;
            try
            {
                DiagLog.Log("[FlowerMerchantVM] before GetMyMerchantAsync");
                var info = await _merchantService.GetMyMerchantAsync().ConfigureAwait(false);
                DiagLog.Log($"[FlowerMerchantVM] after GetMyMerchantAsync info={info != null}");

                if (_cts.IsCancellationRequested)
                {
                    DiagLog.Log("[FlowerMerchantVM] InitializeAsync cancelled after GetMyMerchantAsync");
                    return;
                }

                if (info != null)
                {
                    _merchantId = info.MerchantId;
                    DiagLog.Log("[FlowerMerchantVM] before RunOnUiThreadAsync(set MerchantInfo)");
                    await RunOnUiThreadAsync(() =>
                    {
                        MerchantInfo = info;
                        IsRegistered = true;
                        UpdateStatistics();
                    });
                    DiagLog.Log("[FlowerMerchantVM] after RunOnUiThreadAsync(set MerchantInfo)");

                    if (_cts.IsCancellationRequested)
                    {
                        DiagLog.Log("[FlowerMerchantVM] InitializeAsync cancelled before Task.WhenAll");
                        return;
                    }

                    DiagLog.Log("[FlowerMerchantVM] before Task.WhenAll(7 loads)");
                    await Task.WhenAll(
                        LoadProductsAsync(),
                        LoadOrdersAsync(),
                        LoadRefundsAsync(),
                        LoadCategoriesAsync(),
                        LoadFreightTemplatesAsync(),
                        LoadShippersAsync(),
                        LoadSettlementAsync()
                    );
                    DiagLog.Log("[FlowerMerchantVM] after Task.WhenAll(7 loads)");

                    await RunOnUiThreadAsync(UpdateRecentOrders);
                    DiagLog.Log("[FlowerMerchantVM] after UpdateRecentOrders");
                }
                else
                {
                    await RunOnUiThreadAsync(() => IsRegistered = false);
                }
            }
            catch (OperationCanceledException)
            {
                DiagLog.Log("[FlowerMerchantVM] InitializeAsync cancelled by CTS");
            }
            catch (Exception ex)
            {
                DiagLog.Log($"[FlowerMerchant] 初始化失败: {ex}");
                await RunOnUiThreadAsync(() => ToastService.Instance.Error($"连接服务器失败: {ex.Message}"));

                await RunOnUiThreadAsync(() => IsRegistered = false);
            }
            finally
            {
                await RunOnUiThreadAsync(() => IsLoading = false);
            }
            DiagLog.Log("[FlowerMerchantVM] InitializeAsync END");
        }

        private void UpdateRegisterFormVisibility()
        {
            OnPropertyChanged(nameof(ShowRegisterPrompt));
            OnPropertyChanged(nameof(ShowRegisterForm));
        }

        public void SetMerchantId(long merchantId)
        {
            _merchantId = merchantId;
            _ = LoadMerchantAsync();
        }

        private async Task LoadMerchantAsync()
        {
            IsLoading = true;
            try
            {
                MerchantInfo? info = null;
                if (_merchantId > 0)
                    info = await _merchantService.GetMerchantAsync(_merchantId).ConfigureAwait(false);
                else
                    info = await _merchantService.GetMyMerchantAsync().ConfigureAwait(false);

                if (info != null)
                {
                    _merchantId = info.MerchantId;
                    await RunOnUiThreadAsync(() =>
                    {
                        MerchantInfo = info;
                        IsRegistered = true;
                        UpdateStatistics();
                    });

                    // 初始化 LiveChartsCore 图表数据（营收趋势 + 品类占比）
                    RevenueTrendSeries = FlowerChartHelper.CreateRevenueTrendSeries();
                    RevenueXAxes = FlowerChartHelper.CreateLabelAxis(FlowerChartHelper.RevenueTrendLabels);
                    RevenueYAxes = FlowerChartHelper.CreateValueAxis();
                    CategoryPieSeries = FlowerChartHelper.CreateCategoryPieSeries();

                    await Task.WhenAll(
                        LoadProductsAsync(),
                        LoadOrdersAsync(),
                        LoadRefundsAsync(),
                        LoadCategoriesAsync(),
                        LoadFreightTemplatesAsync(),
                        LoadShippersAsync(),
                        LoadSettlementAsync()
                    );

                    await RunOnUiThreadAsync(UpdateRecentOrders);
                }
                else
                {
                    await RunOnUiThreadAsync(() => IsRegistered = false);
                }
            }
            catch { }
            finally { await RunOnUiThreadAsync(() => IsLoading = false); }
        }

        private async Task LoadProductsAsync()
        {
            Console.WriteLine($"[FlowerMerchant] LoadProductsAsync called: _merchantId={_merchantId}");
            if (_merchantId <= 0)
            {
                Console.WriteLine($"[FlowerMerchant] LoadProductsAsync skipped: _merchantId <= 0");
                return;
            }
            try
            {
                Console.WriteLine($"[FlowerMerchant] Calling GetMerchantProductsAsync for merchant {_merchantId}");
                var products = await _shopService.GetMerchantProductsAsync(_merchantId).ConfigureAwait(false);
                var count = products?.Count ?? 0;
                Console.WriteLine($"[FlowerMerchant] GetMerchantProductsAsync returned: {count} products");
                var orderedProducts = products != null
                    ? new ObservableCollection<RelatedProduct>(products.OrderBy(p => p.SortOrder))
                    : new ObservableCollection<RelatedProduct>();

                await RunOnUiThreadAsync(() =>
                {
                    Products = orderedProducts;
                    ReassignProductCodes();
                    TotalProducts = _products.Count;
                    PendingShipCount = _orders.Count(o => o.Status == 1);
                    var lowStockProducts = _products.Where(p => p.Stock < 100).ToList();
                    LowStockCount = lowStockProducts.Count;
                    LowStockProductNames = string.Join(" / ", lowStockProducts.Select(p => p.ProductName));
                    OnPropertyChanged(nameof(HasProducts));
                    UpdateStatistics();
                });

                Console.WriteLine($"[FlowerMerchant] LoadProductsAsync complete. HasProducts={HasProducts}, TotalProducts={TotalProducts}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FlowerMerchant] 加载商品失败: {ex.Message}");
                Console.WriteLine($"[FlowerMerchant] 堆栈: {ex.StackTrace}");
                await RunOnUiThreadAsync(() => ToastService.Instance.Error($"加载商品失败: {ex.Message}"));
            }
        }

        private async Task LoadOrdersAsync()
        {
            if (_merchantId <= 0) return;
            try
            {
                int? statusFilter = _orderStatusFilter >= 0 ? _orderStatusFilter : null;
                var orders = await _orderService.GetMerchantOrdersByStatusAsync(
                    _merchantId, statusFilter).ConfigureAwait(false);
                var orderCollection = orders != null
                    ? new ObservableCollection<OrderDisplay>(orders)
                    : new ObservableCollection<OrderDisplay>();

                await RunOnUiThreadAsync(() =>
                {
                    Orders = orderCollection;
                    TodayOrders = _orders.Count(o => o.CreatedAt.Date == DateTime.Today);
                    TodayRevenue = _orders.Where(o => o.CreatedAt.Date == DateTime.Today).Sum(o => o.TotalAmount);
                    TotalRevenue = _orders.Where(o => o.Status >= 1 && o.Status <= 4).Sum(o => o.TotalAmount);
                    OnPropertyChanged(nameof(HasOrders));
                    UpdateRecentOrders();
                    UpdateStatistics();
                });
            }
            catch { }
        }

        private async Task LoadRefundsAsync()
        {
            if (_merchantId <= 0) return;
            try
            {
                int? statusFilter = _refundStatusFilter >= 0 ? _refundStatusFilter : null;
                var refunds = await _orderService.GetMerchantRefundsAsync(
                    _merchantId, statusFilter).ConfigureAwait(false);
                var refundCollection = refunds != null
                    ? new ObservableCollection<RefundInfo>(refunds)
                    : new ObservableCollection<RefundInfo>();

                await RunOnUiThreadAsync(() =>
                {
                    Refunds = refundCollection;
                    OnPropertyChanged(nameof(HasRefunds));
                });
            }
            catch { }
        }

        private async Task LoadCategoriesAsync()
        {
            try
            {
                var categories = await _shopService.GetCategoriesAsync().ConfigureAwait(false);
                var categoryCollection = categories != null
                    ? new ObservableCollection<CategoryInfo>(categories)
                    : new ObservableCollection<CategoryInfo>();

                await RunOnUiThreadAsync(() => Categories = categoryCollection);
            }
            catch { }
        }

        private async Task LoadFreightTemplatesAsync()
        {
            if (_merchantId <= 0) return;
            try
            {
                var templates = await _shopService.GetFreightTemplatesAsync(_merchantId).ConfigureAwait(false);
                var templateCollection = templates != null
                    ? new ObservableCollection<FreightTemplateInfo>(templates)
                    : new ObservableCollection<FreightTemplateInfo>();

                await RunOnUiThreadAsync(() => FreightTemplates = templateCollection);
            }
            catch { }
        }

        private string _newTemplateName = "";
        private bool _newTemplateIsFree = true;
        private decimal _newTemplateFirstPrice;
        private decimal _newTemplateContinuePrice;
        private bool _showFreightTemplateDialog;

        public string NewTemplateName { get => _newTemplateName; set => SetProperty(ref _newTemplateName, value); }
        public bool NewTemplateIsFree { get => _newTemplateIsFree; set => SetProperty(ref _newTemplateIsFree, value); }
        public decimal NewTemplateFirstPrice { get => _newTemplateFirstPrice; set => SetProperty(ref _newTemplateFirstPrice, value); }
        public decimal NewTemplateContinuePrice { get => _newTemplateContinuePrice; set => SetProperty(ref _newTemplateContinuePrice, value); }
        public bool ShowFreightTemplateDialog { get => _showFreightTemplateDialog; set => SetProperty(ref _showFreightTemplateDialog, value); }

        public void OpenFreightTemplateDialog()
        {
            NewTemplateName = "";
            NewTemplateIsFree = true;
            NewTemplateFirstPrice = 0;
            NewTemplateContinuePrice = 0;
            ShowFreightTemplateDialog = true;
        }

        public async Task SaveFreightTemplateAsync()
        {
            if (string.IsNullOrWhiteSpace(NewTemplateName))
            {
                ToastService.Instance.Warning("请输入模板名称");
                return;
            }
            var success = await _shopService.AddFreightTemplateAsync(_merchantId, NewTemplateName, NewTemplateIsFree, NewTemplateFirstPrice, NewTemplateContinuePrice);
            if (success)
            {
                ToastService.Instance.Success("运费模板已创建");
                ShowFreightTemplateDialog = false;
                await LoadFreightTemplatesAsync();
            }
            else
            {
                ToastService.Instance.Error("创建运费模板失败");
            }
        }

        public async Task DeleteFreightTemplateAsync(long templateId)
        {
            var success = await _shopService.DeleteFreightTemplateAsync(templateId);
            if (success)
            {
                ToastService.Instance.Success("运费模板已删除");
                await LoadFreightTemplatesAsync();
            }
            else
            {
                ToastService.Instance.Error("删除运费模板失败");
            }
        }

        private async Task LoadShippersAsync()
        {
            if (_merchantId <= 0) return;
            try
            {
                var shippers = await _merchantService.GetShippersAsync(_merchantId).ConfigureAwait(false);
                var shipperCollection = shippers != null
                    ? new ObservableCollection<ShipperInfo>(shippers)
                    : new ObservableCollection<ShipperInfo>();

                await RunOnUiThreadAsync(() =>
                {
                    Shippers = shipperCollection;
                    OnPropertyChanged(nameof(HasShippers));
                });
            }
            catch { }
        }

        private async Task LoadSettlementAsync()
        {
            if (_merchantId <= 0) return;
            try
            {
                var account = await _merchantService.GetSettlementAccountAsync(_merchantId).ConfigureAwait(false);
                var bills = await _merchantService.GetSettlementBillsAsync(_merchantId).ConfigureAwait(false);
                var billCollection = bills != null
                    ? new ObservableCollection<SettlementBillInfo>(bills)
                    : new ObservableCollection<SettlementBillInfo>();

                await RunOnUiThreadAsync(() =>
                {
                    if (account != null)
                    {
                        SettlementBankName = account.BankName;
                        SettlementAccountNo = account.AccountNo;
                        SettlementAccountName = account.AccountName;
                    }

                    SettlementBills = billCollection;
                    OnPropertyChanged(nameof(HasSettlementBills));
                });
            }
            catch { }
        }

        private void UpdateRecentOrders()
        {
            var recent = _orders.OrderByDescending(o => o.CreatedAt).Take(5).ToList();
            RecentOrders = new ObservableCollection<OrderDisplay>(recent);
            OnPropertyChanged(nameof(HasRecentOrders));
        }

        private void UpdateStatistics()
        {
            OnPropertyChanged(nameof(TotalProducts));
            OnPropertyChanged(nameof(TodayOrders));
            OnPropertyChanged(nameof(TodayRevenue));
            OnPropertyChanged(nameof(PendingShipCount));
            OnPropertyChanged(nameof(TotalRevenue));
            OnPropertyChanged(nameof(LowStockCount));
            OnPropertyChanged(nameof(LowStockProductNames));
            OnPropertyChanged(nameof(MerchantTypeDisplay));
            OnPropertyChanged(nameof(ShopGradeDisplay));
        }

        private async Task SearchProductsAsync()
        {
            if (_merchantId <= 0) return;
            try
            {
                var products = await _shopService.GetMerchantProductsAsync(_merchantId).ConfigureAwait(false);
                if (products != null)
                {
                    var filtered = products.AsEnumerable();
                    if (!string.IsNullOrWhiteSpace(_searchKeyword))
                        filtered = filtered.Where(p => p.ProductName.Contains(_searchKeyword, StringComparison.OrdinalIgnoreCase));
                    if (_selectedCategory != null)
                        filtered = filtered.Where(p => p.SpeciesId == _selectedCategory.Id);
                    Products = new ObservableCollection<RelatedProduct>(filtered);
                }
                else
                {
                    Products = new ObservableCollection<RelatedProduct>();
                }
                TotalProducts = _products.Count;
                OnPropertyChanged(nameof(HasProducts));
            }
            catch { }
        }

        public async Task CreateProductAsync()
        {
            if (_isSubmitting) return;
            Console.WriteLine($"[FlowerMerchant] CreateProductAsync called: _merchantId={_merchantId}, Name={NewProductName}, Price={NewProductPrice}, Stock={NewProductStock}");
            if (_merchantId <= 0 || string.IsNullOrWhiteSpace(NewProductName))
            {
                ToastService.Instance.Warning("请填写商品名称");
                return;
            }
            if (NewProductPrice <= 0)
            {
                ToastService.Instance.Warning("请输入有效价格");
                return;
            }
            if (NewProductStock <= 0)
            {
                ToastService.Instance.Warning("请输入有效库存");
                return;
            }

            _isSubmitting = true;
            IsSubmitting = true;
            ToastService.Instance.Info("正在发布...");
            try
            {
                Console.WriteLine($"[FlowerMerchant] Calling CreateProductAsync API with merchantId={_merchantId}");
                bool success;
                if (_isPresale)
                {
                    success = await _marketService.CreatePresaleProductAsync(
                        _merchantId, 1, NewProductName,
                        NewProductDescription, NewProductPrice, NewProductStock,
                        NewProductUnit, NewProductImages,
                        relatedBatchId: _presaleRelatedBatchId,
                        presaleDeliveryDate: _presaleDeliveryDate);
                }
                else
                {
                    success = await _shopService.CreateProductAsync(
                        _merchantId, 1, NewProductName,
                        NewProductDescription, NewProductPrice, NewProductStock);
                }
                Console.WriteLine($"[FlowerMerchant] CreateProductAsync result: {success}");
                if (success)
                {
                    ToastService.Instance.Success(_isPresale ? "预售商品发布成功！" : "商品发布成功！");
                    NewProductName = "";
                    NewProductPrice = 0;
                    NewProductStock = 0;
                    NewProductDescription = "";
                    NewProductUnit = "束";
                    NewProductImages = "";
                    IsOpenLadder = false;
                    IsPresale = false;
                    PresaleRelatedBatchId = null;
                    PresaleDeliveryDate = null;
                    SelectedBatch = null;
                    SkuColors = "";
                    SkuSizes = "";
                    SkuVersions = "";
                    LadderPrices.Clear();
                    Console.WriteLine($"[FlowerMerchant] Clearing form, now calling LoadProductsAsync...");
                    await LoadProductsAsync();
                }
                else
                {
                    ToastService.Instance.Error("发布失败，请检查服务器连接后重试");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FlowerMerchant] 发布商品失败: {ex.Message}");
                ToastService.Instance.Error($"发布失败: {ex.Message}");
            }
            finally
            {
                _isSubmitting = false;
                IsSubmitting = false;
            }
        }

        public void OpenEditProductDialog(RelatedProduct product)
        {
            if (product == null) return;
            EditProductId = product.ProductId;
            EditProductName = product.ProductName;
            EditProductPrice = product.Price;
            EditProductStock = product.Stock;
            EditProductDescription = product.Description ?? "";
            EditProductUnit = product.Unit ?? "束";
            EditProductImages = product.ImageUrl ?? "";
            EditProductMarketPrice = product.MarketPrice??0;
            EditProductCategoryId = null;
            EditProductFreightTemplateId = null;
            ShowEditProductDialog = true;
            NewSkuColor = "";
            NewSkuSize = "";
            NewSkuVersion = "";
            NewSkuPrice = 0;
            NewSkuStock = 0;
            _ = LoadEditProductSKUsAsync();
        }

        public void OpenAuditDialog(RelatedProduct product)
        {
            if (product == null) return;
            AuditProductId = product.ProductId;
            AuditApproved = 1;
            AuditReason = "";
            ShowAuditDialog = true;
        }

        public async Task ConfirmAuditAsync()
        {
            if (AuditProductId <= 0) return;

            try
            {
                await _shopService.AuditProductAsync(AuditProductId, AuditApproved == 1, AuditReason);
                ShowAuditDialog = false;

                if (_merchantId > 0)
                    await LoadProductsAsync();

                ToastService.Instance.Success(AuditApproved == 1 ? "审核通过" : "已拒绝");
            }
            catch (Exception ex)
            {
                ToastService.Instance.Error($"审核失败：{ex.Message}");
            }
        }

        private async Task LoadEditProductSKUsAsync()
        {
            if (EditProductId <= 0) return;
            var skus = await _shopService.GetProductSKUsAsync(EditProductId);
            EditProductSKUs = skus != null
                ? new ObservableCollection<ProductSKUInfo>(skus)
                : new ObservableCollection<ProductSKUInfo>();
            OnPropertyChanged(nameof(HasEditSKUs));
        }

        public async Task AddEditProductSKUAsync()
        {
            if (EditProductId <= 0) return;
            if (NewSkuPrice <= 0)
            {
                ToastService.Instance.Warning("请输入SKU价格");
                return;
            }
            if (NewSkuStock <= 0)
            {
                ToastService.Instance.Warning("请输入SKU库存");
                return;
            }

            var result = await _shopService.AddProductSKUAsync(
                EditProductId, NewSkuColor, NewSkuSize, NewSkuVersion, NewSkuPrice, NewSkuStock);
            if (result != null)
            {
                ToastService.Instance.Success("SKU已添加");
                NewSkuColor = "";
                NewSkuSize = "";
                NewSkuVersion = "";
                NewSkuPrice = 0;
                NewSkuStock = 0;
                await LoadEditProductSKUsAsync();
            }
            else
            {
                ToastService.Instance.Error("添加SKU失败");
            }
        }

        public async Task DeleteEditProductSKUAsync(long skuId)
        {
            if (EditProductId <= 0 || skuId <= 0) return;
            var success = await _shopService.DeleteProductSKUAsync(EditProductId, skuId);
            if (success)
            {
                ToastService.Instance.Success("SKU已删除");
                await LoadEditProductSKUsAsync();
            }
            else
            {
                ToastService.Instance.Error("删除SKU失败");
            }
        }

        public async Task SaveEditProductAsync()
        {
            if (_isSubmitting) return;
            if (EditProductId <= 0 || string.IsNullOrWhiteSpace(EditProductName))
            {
                ToastService.Instance.Warning("请填写商品名称");
                return;
            }
            if (EditProductPrice <= 0)
            {
                ToastService.Instance.Warning("请输入有效价格");
                return;
            }

            _isSubmitting = true;
            IsSubmitting = true;
            ToastService.Instance.Info("正在保存...");
            try
            {
                var success = await _shopService.UpdateProductAsync(
                    EditProductId, EditProductName, EditProductPrice,
                    EditProductStock, EditProductDescription, EditProductImages,
                    EditProductCategoryId, EditProductFreightTemplateId);
                if (success)
                {
                    ToastService.Instance.Success("商品已更新");
                    ShowEditProductDialog = false;
                    await LoadProductsAsync();
                }
                else
                {
                    ToastService.Instance.Error("更新失败，请重试");
                }
            }
            finally
            {
                _isSubmitting = false;
                IsSubmitting = false;
            }
        }

        private async Task ToggleProductActiveAsync(RelatedProduct product)
        {
            if (product == null) return;
            var success = await _shopService.ToggleProductActiveAsync(product.ProductId, !product.IsActive).ConfigureAwait(false);
            if (success)
            {
                ToastService.Instance.Success(product.IsActive ? "已下架" : "已上架");
                await LoadProductsAsync();
            }
            else
            {
                ToastService.Instance.Error("操作失败，请重试");
            }
        }

        public async Task ActivateProductAsync(RelatedProduct product)
        {
            if (product == null) return;
            var success = await _shopService.ToggleProductActiveAsync(product.ProductId, true);
            if (success)
            {
                ToastService.Instance.Success("已上架");
                await LoadProductsAsync();
            }
            else
            {
                ToastService.Instance.Error("上架失败，请重试");
            }
        }

        public async Task DeactivateProductAsync(RelatedProduct product)
        {
            if (product == null) return;
            var success = await _shopService.ToggleProductActiveAsync(product.ProductId, false);
            if (success)
            {
                ToastService.Instance.Success("已下架");
                await LoadProductsAsync();
            }
            else
            {
                ToastService.Instance.Error("下架失败，请重试");
            }
        }

        public async Task DeleteProductAsync(RelatedProduct product)
        {
            if (product == null) return;
            var success = await _shopService.DeleteProductAsync(product.ProductId).ConfigureAwait(false);
            if (success)
            {
                ToastService.Instance.Success("商品已删除");
                await LoadProductsAsync();
            }
            else
            {
                ToastService.Instance.Error("删除失败，请重试");
            }
        }

        public void MoveProductUp(RelatedProduct product)
        {
            if (product == null || _products == null) return;
            var index = _products.IndexOf(product);
            if (index <= 0) return;

            var item = _products[index];
            _products.RemoveAt(index);
            _products.Insert(index - 1, item);
            RefreshProductList();
            _ = UpdateProductSortOrderAsync();
        }

        public void MoveProductDown(RelatedProduct product)
        {
            if (product == null || _products == null) return;
            var index = _products.IndexOf(product);
            if (index < 0 || index >= _products.Count - 1) return;

            var item = _products[index];
            _products.RemoveAt(index);
            _products.Insert(index + 1, item);
            RefreshProductList();
            _ = UpdateProductSortOrderAsync();
        }

        private void ReassignProductCodes()
        {
            for (int i = 0; i < _products.Count; i++)
            {
                _products[i].ProductCode = $"#{i + 1}";
                _products[i].SortOrder = i + 1;
            }
        }

        private void RefreshProductList()
        {
            // Create new list with reassigned codes, replacing the entire collection
            // to force UI re-render since RelatedProduct doesn't implement INotifyPropertyChanged
            var newList = new List<RelatedProduct>();
            for (int i = 0; i < _products.Count; i++)
            {
                var p = _products[i];
                p.ProductCode = $"#{i + 1}";
                p.SortOrder = i + 1;
                newList.Add(p);
            }
            // Reassign reference to trigger OnPropertyChanged
            _products = new ObservableCollection<RelatedProduct>(newList);
            OnPropertyChanged(nameof(Products));
        }

        private async Task UpdateProductSortOrderAsync()
        {
            try
            {
                for (int i = 0; i < _products.Count; i++)
                {
                    var product = _products[i];
                    if (product.SortOrder == i + 1 && product.ProductCode == $"#{i + 1}") continue;
                    await _shopService.UpdateProductSortOrderAsync(product.ProductId, i + 1, $"#{i + 1}").ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FlowerMerchant] 更新排序失败: {ex.Message}");
            }
        }

        private async Task OpenShipDialog(long orderId)
        {
            ShippingOrderId = orderId;
            ExpressCompanyName = "";
            SelectedExpressCompany = "";
            ShipOrderNumber = "";
            ShowShipDialog = true;
        }

        private void CancelShip()
        {
            ExpressCompanyName = "";
            SelectedExpressCompany = "";
            ShipOrderNumber = "";
            ShowShipDialog = false;
        }

        private async Task ConfirmShipAsync()
        {
            if (string.IsNullOrWhiteSpace(ExpressCompanyName) || string.IsNullOrWhiteSpace(ShipOrderNumber))
            {
                ToastService.Instance.Warning("请填写物流公司和运单号");
                return;
            }

            ToastService.Instance.Info("正在发货...");
            var success = await _orderService.ShipOrderAsync(
                ShippingOrderId, ExpressCompanyName, ShipOrderNumber).ConfigureAwait(false);
            if (success)
            {
                ToastService.Instance.Success("发货成功！");
                ShowShipDialog = false;
                ExpressCompanyName = "";
                SelectedExpressCompany = "";
                ShipOrderNumber = "";
                await LoadOrdersAsync();
            }
            else
            {
                ToastService.Instance.Error("发货失败，请重试");
            }
        }

        private async Task AuditRefundAsync(long refundId, bool approved)
        {
            var success = await _orderService.AuditRefundAsync(
                refundId, approved, approved ? "同意退款" : "拒绝退款").ConfigureAwait(false);
            if (success)
            {
                ToastService.Instance.Success(approved ? "已同意退款" : "已拒绝退款");
                await LoadRefundsAsync();
            }
            else
            {
                ToastService.Instance.Error("操作失败，请重试");
            }
        }

        private async Task SaveSettlementAccountAsync()
        {
            if (string.IsNullOrWhiteSpace(SettlementBankName))
            {
                ToastService.Instance.Warning("请输入银行名称");
                return;
            }
            if (string.IsNullOrWhiteSpace(SettlementAccountNo))
            {
                ToastService.Instance.Warning("请输入银行账号");
                return;
            }
            if (string.IsNullOrWhiteSpace(SettlementAccountName))
            {
                ToastService.Instance.Warning("请输入户名");
                return;
            }

            ToastService.Instance.Info("正在保存...");
            var result = await _merchantService.SaveSettlementAccountAsync(
                _merchantId, SettlementBankName, SettlementAccountNo, SettlementAccountName).ConfigureAwait(false);
            if (result != null)
            {
                ToastService.Instance.Success("结算账户已保存");
            }
            else
            {
                ToastService.Instance.Error("保存失败，请重试");
            }
        }

        private async Task SaveShopSettingsAsync()
        {
            if (string.IsNullOrWhiteSpace(EditShopName))
            {
                ToastService.Instance.Warning("店铺名称不能为空");
                return;
            }

            ToastService.Instance.Info("正在保存...");
            var info = await _merchantService.UpdateMerchantAsync(
                _merchantId, EditShopName, EditDescription, EditPhone).ConfigureAwait(false);

            if (info != null)
            {
                MerchantInfo = info;
                ToastService.Instance.Success("店铺信息已更新");
                OnPropertyChanged(nameof(MerchantTypeDisplay));
                OnPropertyChanged(nameof(ShopGradeDisplay));
            }
            else
            {
                ToastService.Instance.Error("更新失败，请重试");
            }
        }

        private async Task AddShipperAsync()
        {
            if (string.IsNullOrWhiteSpace(NewShipperName))
            {
                ToastService.Instance.Warning("请输入发货点名称");
                return;
            }
            if (string.IsNullOrWhiteSpace(NewShipperAddress))
            {
                ToastService.Instance.Warning("请输入发货地址");
                return;
            }

            ToastService.Instance.Info("正在添加...");
            var result = await _merchantService.AddShipperAsync(
                _merchantId, NewShipperTag, NewShipperName, 0,
                NewShipperAddress, NewShipperPhone, false).ConfigureAwait(false);
            if (result != null)
            {
                ToastService.Instance.Success("发货点已添加");
                NewShipperTag = "";
                NewShipperName = "";
                NewShipperAddress = "";
                NewShipperPhone = "";
                await LoadShippersAsync();
            }
            else
            {
                ToastService.Instance.Error("添加失败，请重试");
            }
        }

        private async Task DeleteShipperAsync(long shipperId)
        {
            var success = await _merchantService.DeleteShipperAsync(_merchantId, shipperId).ConfigureAwait(false);
            if (success)
            {
                ToastService.Instance.Success("发货点已删除");
                await LoadShippersAsync();
            }
            else
            {
                ToastService.Instance.Error("删除失败，请重试");
            }
        }

        private void AddLadderPrice()
        {
            LadderPrices.Add(new LadderPriceItem { MinBatch = 1, MaxBatch = 10, Price = 0 });
        }

        private void RemoveLadderPrice(LadderPriceItem item)
        {
            if (item != null)
                LadderPrices.Remove(item);
        }

        public async Task RegisterMerchantAsync()
        {
            if (_isSubmitting) return;
            if (string.IsNullOrWhiteSpace(RegisterShopName))
            {
                ToastService.Instance.Warning("请输入店铺名称");
                return;
            }
            if (string.IsNullOrWhiteSpace(RegisterPhone))
            {
                ToastService.Instance.Warning("请输入联系电话");
                return;
            }

            _isSubmitting = true;
            IsSubmitting = true;
            ToastService.Instance.Info("正在注册...");
            try
            {
                var merchantTypeStr = RegisterType == 1 ? "Enterprise" : "Individual";
                var info = await _merchantService.RegisterMerchantAsync(
                    RegisterShopName, RegisterDescription, RegisterPhone, merchantTypeStr);

                if (info != null)
                {
                    _merchantId = info.MerchantId;
                    MerchantInfo = info;
                    IsRegistered = true;
                    _showRegisterForm = false;
                    ToastService.Instance.Success("注册成功！欢迎入驻花卉市场");
                    await Task.WhenAll(LoadProductsAsync(), LoadOrdersAsync());
                }
                else
                {
                    ToastService.Instance.Error("注册失败，请检查服务器连接后重试");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FlowerMerchant] 注册失败: {ex.Message}");
                ToastService.Instance.Error($"注册失败: {ex.Message}");
            }
            finally
            {
                _isSubmitting = false;
                IsSubmitting = false;
            }
        }

        public void StartEditShop()
        {
            if (_merchantInfo == null) return;
            EditShopName = _merchantInfo.ShopName;
            EditDescription = _merchantInfo.Description;
            EditPhone = _merchantInfo.ContactPhone;
        }

        private string _newCouponName = "";
        public string NewCouponName { get => _newCouponName; set => SetProperty(ref _newCouponName, value); }

        private int _newCouponType;
        public int NewCouponType { get => _newCouponType; set => SetProperty(ref _newCouponType, value); }

        private decimal _newCouponDenomination;
        public decimal NewCouponDenomination { get => _newCouponDenomination; set => SetProperty(ref _newCouponDenomination, value); }

        private decimal _newCouponUseCondition;
        public decimal NewCouponUseCondition { get => _newCouponUseCondition; set => SetProperty(ref _newCouponUseCondition, value); }

        private int _newCouponTotalCount = 100;
        public int NewCouponTotalCount { get => _newCouponTotalCount; set => SetProperty(ref _newCouponTotalCount, value); }

        private string _newFullDiscountRuleName = "";
        public string NewFullDiscountRuleName { get => _newFullDiscountRuleName; set => SetProperty(ref _newFullDiscountRuleName, value); }

        private decimal _newFullDiscountLimitValue;
        public decimal NewFullDiscountLimitValue { get => _newFullDiscountLimitValue; set => SetProperty(ref _newFullDiscountLimitValue, value); }

        private decimal _newFullDiscountDiscountValue;
        public decimal NewFullDiscountDiscountValue { get => _newFullDiscountDiscountValue; set => SetProperty(ref _newFullDiscountDiscountValue, value); }

        private long _applyBrandId;
        public long ApplyBrandId { get => _applyBrandId; set => SetProperty(ref _applyBrandId, value); }

        private long _applyCategoryId;
        public long ApplyCategoryId { get => _applyCategoryId; set => SetProperty(ref _applyCategoryId, value); }

        private decimal _withdrawAmount;
        public decimal WithdrawAmount { get => _withdrawAmount; set => SetProperty(ref _withdrawAmount, value); }

        private async Task LoadCouponsAsync()
        {
            if (_merchantId <= 0) return;
            var coupons = await _merchantService.GetShopCouponsAsync(_merchantId).ConfigureAwait(false);
            Coupons = new ObservableCollection<CouponInfo>(coupons ?? new());
        }

        private async Task LoadFullDiscountRulesAsync()
        {
            if (_merchantId <= 0) return;
            var rules = await _merchantService.GetShopFullDiscountRulesAsync(_merchantId).ConfigureAwait(false);
            FullDiscountRules = new ObservableCollection<FullDiscountRuleInfo>(rules ?? new());
        }

        private async Task LoadBrandsAsync()
        {
            var brands = await _merchantService.GetBrandsAsync().ConfigureAwait(false);
            Brands = new ObservableCollection<BrandInfo>(brands ?? new());
        }

        private async Task LoadBusinessCategoriesAsync()
        {
            if (_merchantId <= 0) return;
            var cats = await _merchantService.GetShopBusinessCategoriesAsync(_merchantId).ConfigureAwait(false);
            BusinessCategories = new ObservableCollection<BusinessCategoryInfo>(cats ?? new());
        }

        private async Task LoadPendingSettlementsAsync()
        {
            if (_merchantId <= 0) return;
            var pendings = await _merchantService.GetPendingSettlementsAsync(_merchantId).ConfigureAwait(false);
            PendingSettlements = new ObservableCollection<PendingSettlementInfo>(pendings ?? new());
        }

        private async Task LoadAccountItemsAsync()
        {
            if (_merchantId <= 0) return;
            var items = await _merchantService.GetShopAccountItemsAsync(_merchantId).ConfigureAwait(false);
            AccountItems = new ObservableCollection<AccountItemInfo>(items ?? new());
        }

        private async Task CreateCouponAsync()
        {
            if (string.IsNullOrWhiteSpace(NewCouponName))
            {
                ToastService.Instance.Warning("请输入优惠券名称");
                return;
            }
            ToastService.Instance.Info("正在创建优惠券...");
            var success = await _merchantService.CreateCouponAsync(
                _merchantId, NewCouponName, NewCouponType, NewCouponDenomination,
                NewCouponUseCondition, DateTime.UtcNow, DateTime.UtcNow.AddYears(1),
                NewCouponTotalCount).ConfigureAwait(false);
            ToastService.Instance.Show(success ? "优惠券创建成功" : "优惠券创建失败", success ? ToastType.Success : ToastType.Error);
            if (success) await LoadCouponsAsync();
        }

        private async Task DeleteFullDiscountRuleAsync(long ruleId)
        {
            var success = await _merchantService.DeleteFullDiscountRuleAsync(ruleId).ConfigureAwait(false);
            ToastService.Instance.Show(success ? "满减规则已删除" : "删除失败", success ? ToastType.Success : ToastType.Error);
            if (success) await LoadFullDiscountRulesAsync();
        }

        private async Task CreateFullDiscountRuleAsync()
        {
            if (string.IsNullOrWhiteSpace(NewFullDiscountRuleName))
            {
                ToastService.Instance.Warning("请输入规则名称");
                return;
            }
            ToastService.Instance.Info("正在创建满减规则...");
            var success = await _merchantService.CreateFullDiscountRuleAsync(
                _merchantId, NewFullDiscountRuleName, NewFullDiscountLimitValue,
                NewFullDiscountDiscountValue, DateTime.UtcNow, DateTime.UtcNow.AddYears(1)
            ).ConfigureAwait(false);
            ToastService.Instance.Show(success ? "满减规则创建成功" : "创建失败", success ? ToastType.Success : ToastType.Error);
            if (success) await LoadFullDiscountRulesAsync();
        }

        private async Task ApplyBusinessCategoryAsync()
        {
            if (ApplyCategoryId <= 0)
            {
                ToastService.Instance.Warning("请选择经营类目");
                return;
            }
            ToastService.Instance.Info("正在申请经营类目...");
            var success = await _merchantService.ApplyBusinessCategoryAsync(_merchantId, ApplyCategoryId, 0.05m).ConfigureAwait(false);
            ToastService.Instance.Show(success ? "经营类目申请已提交" : "申请失败", success ? ToastType.Success : ToastType.Error);
            if (success) await LoadBusinessCategoriesAsync();
        }

        private async Task ApplyBrandAsync()
        {
            if (ApplyBrandId <= 0)
            {
                ToastService.Instance.Warning("请选择品牌");
                return;
            }
            var brand = Brands?.FirstOrDefault(b => b.Id == ApplyBrandId);
            if (brand == null) return;
            ToastService.Instance.Info("正在申请品牌...");
            var success = await _merchantService.ApplyBrandAsync(_merchantId, brand.Name, "").ConfigureAwait(false);
            ToastService.Instance.Show(success ? "品牌申请已提交" : "申请失败", success ? ToastType.Success : ToastType.Error);
        }

        private async Task RequestWithdrawAsync()
        {
            if (WithdrawAmount <= 0)
            {
                ToastService.Instance.Warning("请输入提现金额");
                return;
            }
            ToastService.Instance.Info("正在申请提现...");
            try
            {
                var success = await _merchantService.RequestWithdrawAsync(
                    _merchantId, WithdrawAmount,
                    SettlementBankName, SettlementAccountNo, SettlementAccountName
                ).ConfigureAwait(false);
                ToastService.Instance.Show(success ? "提现申请已提交" : "提现申请失败", success ? ToastType.Success : ToastType.Error);
                if (success) WithdrawAmount = 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FlowerMerchant] 提现失败: {ex.Message}");
                ToastService.Instance.Error($"提现失败: {ex.Message}");
            }
        }

        private async Task LoadSuggestedPriceAsync()
        {
            if (_selectedSpeciesId <= 0)
            {
                SuggestedPriceRange = null;
                return;
            }

            IsLoadingSuggestedPrice = true;
            try
            {
                var result = await _marketService.GetSuggestedPriceAsync(_selectedSpeciesId).ConfigureAwait(false);
                SuggestedPriceRange = result;
            }
            catch
            {
                SuggestedPriceRange = null;
            }
            finally
            {
                IsLoadingSuggestedPrice = false;
            }
        }

        private void AcceptSuggestedPrice()
        {
            if (_suggestedPriceRange != null && _suggestedPriceRange.AvgForecastPrice > 0)
            {
                NewProductPrice = _suggestedPriceRange.AvgForecastPrice;
                ToastService.Instance.Success("已采纳建议价格");
            }
        }

        public async Task LoadPriceAdjustmentSuggestionsAsync()
        {
            if (_merchantId <= 0) return;
            try
            {
                var suggestions = await _marketService.GetPriceAdjustmentSuggestionsAsync(_merchantId).ConfigureAwait(false);
                PriceAdjustmentSuggestions = suggestions != null
                    ? new ObservableCollection<PriceAdjustmentSuggestionInfo>(suggestions)
                    : new ObservableCollection<PriceAdjustmentSuggestionInfo>();
            }
            catch
            {
                PriceAdjustmentSuggestions = new ObservableCollection<PriceAdjustmentSuggestionInfo>();
            }
        }

        public void OpenPriceAdjustDialog(PriceAdjustmentSuggestionInfo suggestion)
        {
            if (suggestion == null) return;
            SelectedPriceAdjustment = suggestion;
            ShowPriceAdjustDialog = true;
        }

        public async Task ConfirmPriceAdjustAsync()
        {
            if (_selectedPriceAdjustment == null) return;

            var products = await _shopService.GetMerchantProductsAsync(_merchantId).ConfigureAwait(false);
            var product = products?.FirstOrDefault(p => p.ProductId == _selectedPriceAdjustment.ProductId);
            if (product == null)
            {
                ToastService.Instance.Error("商品不存在，请重试");
                return;
            }

            var success = await _shopService.UpdateProductAsync(
                _selectedPriceAdjustment.ProductId,
                product.ProductName,
                _selectedPriceAdjustment.SuggestedPrice,
                product.Stock,
                product.Description,
                product.ImageUrl,
                null, null
            ).ConfigureAwait(false);

            if (success)
            {
                ToastService.Instance.Success("价格已调整");
                ShowPriceAdjustDialog = false;
                await LoadProductsAsync();
                await LoadPriceAdjustmentSuggestionsAsync();
            }
            else
            {
                ToastService.Instance.Error("调价失败，请重试");
            }
        }

        private async Task ConfirmReturnReceivedAsync(long refundId)
        {
            var success = await _orderService.ConfirmReturnReceivedAsync(refundId).ConfigureAwait(false);
            if (success)
            {
                ToastService.Instance.Success("已确认收货");
                await LoadRefundsAsync();
            }
            else
            {
                ToastService.Instance.Error("操作失败，请重试");
            }
        }

        private async Task BatchShipOrdersAsync()
        {
            if (_batchShipItems.Count == 0)
            {
                ToastService.Instance.Warning("请添加发货信息");
                return;
            }

            ToastService.Instance.Info("正在批量发货...");
            var request = new BatchShipRequest { Items = _batchShipItems.ToList() };
            var success = await _orderService.BatchShipOrdersAsync(request).ConfigureAwait(false);
            if (success)
            {
                ToastService.Instance.Success("批量发货成功！");
                BatchShipItems = new ObservableCollection<BatchShipItem>();
                await LoadOrdersAsync();
            }
            else
            {
                ToastService.Instance.Error("批量发货失败，请重试");
            }
        }

        private async Task ViewSettlementDetailsAsync(long settlementBillId)
        {
            try
            {
                var details = await _orderService.GetSettlementDetailsAsync(settlementBillId).ConfigureAwait(false);
                SettlementDetails = details != null
                    ? new ObservableCollection<SettlementDetailInfo>(details)
                    : new ObservableCollection<SettlementDetailInfo>();

                var summary = await _orderService.GetSettlementAccountSummaryAsync(_merchantId).ConfigureAwait(false);
                if (summary != null)
                {
                    SettlementAccountSummary = summary;
                }
            }
            catch { }
        }

        private async Task ViewMerchantLogisticsAsync(long orderId)
        {
            ShowMerchantLogisticsDialog = true;
            IsLoadingMerchantLogistics = true;
            MerchantLogisticsMapData = null;
            try
            {
                var data = await _orderService.GetLogisticsMapDataCachedAsync(orderId).ConfigureAwait(false);
                MerchantLogisticsMapData = data;
            }
            catch
            {
                MerchantLogisticsMapData = null;
            }
            finally
            {
                IsLoadingMerchantLogistics = false;
            }
        }

        private async Task LoadAvailableBatchesAsync()
        {
            try
            {
                var batches = await _iotService.GetPlantingBatchesAsync("", "Planted").ConfigureAwait(false);
                AvailableBatches = batches != null
                    ? new ObservableCollection<PlantingBatchInfo>(batches)
                    : new ObservableCollection<PlantingBatchInfo>();
            }
            catch
            {
                AvailableBatches = new ObservableCollection<PlantingBatchInfo>();
            }
        }
    }
}
