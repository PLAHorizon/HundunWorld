using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Horizon.Game.GengDi.Core.Controls;
using Horizon.Game.GengDi.Core.Services;
using Horizon.Game.GengDi.Core.Services.Database;

namespace Horizon.Game.GengDi.Core.ViewModels
{
    public class FlowerShopViewModel : ViewModelBase
    {
        private readonly FlowerShopService _shopService;
        private readonly FlowerMarketService _marketService;
        private readonly FlowerSpeciesLookupService _speciesLookup = FlowerSpeciesLookupService.Instance;
        private ObservableCollection<ShopProductItem> _products = new();
        private ObservableCollection<ShopProductItem> _filteredProducts = new();
        private string _searchText = "";
        private bool _isLoading;
        private bool _hasMoreProducts = true;
        private int _currentPage = 1;
        private int _selectedSpeciesId;
        private Guid _userId;
        private string _statusMessage = "";
        private int _sortMode;
        private long? _selectedCategoryId;
        private bool _isBatchMode;
        private int _selectedProductIndex = -1;

        public ObservableCollection<ShopProductItem> Products
        {
            get => _products;
            set => SetProperty(ref _products, value);
        }

        public ObservableCollection<ShopProductItem> FilteredProducts
        {
            get => _filteredProducts;
            set => SetProperty(ref _filteredProducts, value);
        }

        public string SearchText
        {
            get => _searchText;
            set => SetProperty(ref _searchText, value);
        }

        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        public bool HasMoreProducts
        {
            get => _hasMoreProducts;
            set => SetProperty(ref _hasMoreProducts, value);
        }

        public bool IsEmpty => !_isLoading && _filteredProducts.Count == 0;

        public int SelectedSpeciesId
        {
            get => _selectedSpeciesId;
            set
            {
                if (SetProperty(ref _selectedSpeciesId, value))
                {
                    _currentPage = 1;
                    _ = LoadProductsAsync();
                }
            }
        }

        public long? SelectedCategoryId
        {
            get => _selectedCategoryId;
            set
            {
                if (SetProperty(ref _selectedCategoryId, value))
                {
                    _currentPage = 1;
                    _ = LoadProductsAsync();
                }
            }
        }

        public int SortMode
        {
            get => _sortMode;
            set
            {
                if (SetProperty(ref _sortMode, value))
                    ApplySortAndFilter();
            }
        }

        public Guid UserId
        {
            get => _userId;
            set => SetProperty(ref _userId, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public bool IsBatchMode
        {
            get => _isBatchMode;
            set
            {
                if (SetProperty(ref _isBatchMode, value))
                {
                    if (!value)
                    {
                        foreach (var p in _filteredProducts)
                            p.IsSelected = false;
                    }
                    OnPropertyChanged(nameof(IsCardView));
                    OnPropertyChanged(nameof(IsBatchView));
                }
            }
        }

        public bool IsCardView => !IsBatchMode;
        public bool IsBatchView => IsBatchMode;

        public int SelectedProductIndex
        {
            get => _selectedProductIndex;
            set
            {
                if (SetProperty(ref _selectedProductIndex, value))
                    OnPropertyChanged(nameof(SelectedProduct));
            }
        }

        public ShopProductItem SelectedProduct
        {
            get
            {
                if (_selectedProductIndex >= 0 && _selectedProductIndex < _filteredProducts.Count)
                    return _filteredProducts[_selectedProductIndex];
                return null;
            }
        }

        public ObservableCollection<SpeciesFilterItem> SpeciesFilters { get; }
        public ObservableCollection<CategoryFilterItem> CategoryFilters { get; } = new();
        public ObservableCollection<SortModeItem> SortModes { get; }

        public ICommand SearchCommand { get; }
        public ICommand LoadMoreCommand { get; }
        public ICommand AddToCartCommand { get; }
        public ICommand SelectSpeciesCommand { get; }
        public ICommand SelectCategoryCommand { get; }
        public ICommand SelectSortCommand { get; }
        public ICommand ToggleBatchModeCommand { get; }
        public ICommand BatchAddToCartCommand { get; }
        public ICommand NavigateUpCommand { get; }
        public ICommand NavigateDownCommand { get; }
        public ICommand ViewSelectedProductCommand { get; }

        public FlowerShopViewModel()
        {
            _shopService = new FlowerShopService();
            _marketService = new FlowerMarketService();
            SearchCommand = new AsyncCommand(SearchAsync);
            LoadMoreCommand = new AsyncCommand(LoadMoreAsync);
            AddToCartCommand = new AsyncCommand<long>(AddToCartAsync);
            SelectSpeciesCommand = new RelayCommand<int>(SelectSpecies);
            SelectCategoryCommand = new RelayCommand<long?>(c => SelectedCategoryId = c);
            SelectSortCommand = new RelayCommand<int>(s => SortMode = s);
            ToggleBatchModeCommand = new RelayCommand(() => IsBatchMode = !IsBatchMode);
            BatchAddToCartCommand = new AsyncCommand(BatchAddToCartAsync);
            NavigateUpCommand = new RelayCommand(NavigateUp);
            NavigateDownCommand = new RelayCommand(NavigateDown);
            ViewSelectedProductCommand = new RelayCommand(ViewSelectedProduct);

            SpeciesFilters = new ObservableCollection<SpeciesFilterItem>(
                _speciesLookup.GetAllSpecies()
                    .Select(kv => new SpeciesFilterItem { SpeciesId = kv.Key, DisplayName = kv.Value })
                    .Prepend(new SpeciesFilterItem { SpeciesId = 0, DisplayName = "全部" })
            );

            SortModes = new ObservableCollection<SortModeItem>
            {
                new() { Mode = 0, DisplayName = "默认排序" },
                new() { Mode = 1, DisplayName = "价格从低到高" },
                new() { Mode = 2, DisplayName = "价格从高到低" },
                new() { Mode = 3, DisplayName = "销量优先" }
            };

            _ = LoadCategoriesAsync();
            _ = LoadProductsAsync();
        }

        private async Task LoadCategoriesAsync()
        {
            var categories = await _shopService.GetCategoriesAsync();
            if (categories != null)
            {
                CategoryFilters.Clear();
                CategoryFilters.Add(new CategoryFilterItem { CategoryId = null, DisplayName = "全部分类" });
                foreach (var cat in categories)
                    CategoryFilters.Add(new CategoryFilterItem { CategoryId = cat.Id, DisplayName = cat.Name });
            }
        }

        private void SelectSpecies(int speciesId)
        {
            SelectedSpeciesId = speciesId;
            foreach (var f in SpeciesFilters)
                f.IsSelected = f.SpeciesId == speciesId;
        }

        private async Task LoadProductsAsync()
        {
            IsLoading = true;
            try
            {
                var products = await _shopService.GetActiveProductsAsync(
                    _selectedSpeciesId, _currentPage, 20).ConfigureAwait(false);

                if (products != null && products.Count > 0)
                {
                    var items = products.Select(p => new ShopProductItem
                    {
                        ProductId = p.ProductId,
                        ProductName = p.ProductName,
                        Price = p.Price,
                        MarketPrice = p.MarketPrice ?? 0,
                        MerchantName = p.MerchantName,
                        Stock = p.Stock,
                        SpeciesId = p.SpeciesId,
                        ImageUrl = p.ImageUrl,
                        SpeciesName = GetSpeciesName(p.SpeciesId),
                        IsPresale = p.IsPresale,
                        PresaleDeliveryDate = p.PresaleDeliveryDate
                    }).ToList();

                    if (_currentPage == 1)
                        Products = new ObservableCollection<ShopProductItem>(items);
                    else
                    {
                        foreach (var item in items)
                            _products.Add(item);
                    }

                    ApplySortAndFilter();
                    HasMoreProducts = products.Count >= 20;

                    _ = LoadForecastDataAsync(items);
                    _ = LoadLadderPriceHintsAsync(items);
                }
                else
                {
                    if (_currentPage == 1)
                        Products = new ObservableCollection<ShopProductItem>();
                    ApplySortAndFilter();
                    HasMoreProducts = false;
                }
            }
            catch
            {
                HasMoreProducts = false;
            }
            finally
            {
                IsLoading = false;
                OnPropertyChanged(nameof(IsEmpty));
            }
        }

        private async Task LoadForecastDataAsync(List<ShopProductItem> items)
        {
            var speciesIds = items.Select(i => i.SpeciesId).Distinct().ToList();
            foreach (var sid in speciesIds)
            {
                try
                {
                    var forecast = await _marketService.GetPriceForecastAsync(sid, 7).ConfigureAwait(false);
                    if (forecast?.PredictedPrices != null && forecast.PredictedPrices.Count >= 2)
                    {
                        var first = forecast.PredictedPrices[0].PredictedPrice;
                        var last = forecast.PredictedPrices[forecast.PredictedPrices.Count - 1].PredictedPrice;
                        var diff = last - first;
                        string trend;
                        if (diff > 0.01m) trend = "↑";
                        else if (diff < -0.01m) trend = "↓";
                        else trend = "→";

                        foreach (var item in items.Where(i => i.SpeciesId == sid))
                            item.ForecastTrend = trend;
                    }
                }
                catch { }
            }
        }

        private async Task LoadLadderPriceHintsAsync(List<ShopProductItem> items)
        {
            foreach (var item in items)
            {
                try
                {
                    var ladderPrices = await _shopService.GetProductLadderPricesAsync(item.ProductId).ConfigureAwait(false);
                    if (ladderPrices != null && ladderPrices.Count > 0)
                    {
                        var minPrice = ladderPrices.Min(lp => lp.Price);
                        if (minPrice < item.Price)
                            item.LadderPriceHint = $"阶梯低至¥{minPrice:F2}";
                    }
                }
                catch { }
            }
        }

        private string GetSpeciesName(int speciesId) => _speciesLookup.GetSpeciesName(speciesId);

        private async Task SearchAsync()
        {
            _currentPage = 1;
            await LoadProductsAsync();
        }

        private async Task LoadMoreAsync()
        {
            _currentPage++;
            await LoadProductsAsync();
        }

        private async Task AddToCartAsync(long productId)
        {
            var currentUserId = Guid.Empty;
            if (App.CurrentUser != null && Guid.TryParse(App.CurrentUser.Id, out var uid))
                currentUserId = uid;

            if (currentUserId == Guid.Empty)
            {
                ToastService.Instance.Warning("请先登录");
                return;
            }

            var product = _products.FirstOrDefault(p => p.ProductId == productId);
            if (product == null) return;

            var success = await _shopService.AddToCartAsync(
                currentUserId, productId, 1).ConfigureAwait(false);

            if (success)
                ToastService.Instance.Success($"已添加「{product.ProductName}」到购物车");
            else
                ToastService.Instance.Error("添加购物车失败，请重试");
        }

        private async Task BatchAddToCartAsync()
        {
            var currentUserId = Guid.Empty;
            if (App.CurrentUser != null && Guid.TryParse(App.CurrentUser.Id, out var uid))
                currentUserId = uid;

            if (currentUserId == Guid.Empty)
            {
                ToastService.Instance.Warning("请先登录");
                return;
            }

            var selectedItems = _filteredProducts.Where(p => p.IsSelected && p.BatchQuantity > 0).ToList();
            if (selectedItems.Count == 0)
            {
                ToastService.Instance.Warning("请选择商品并设置数量");
                return;
            }

            var successCount = 0;
            foreach (var item in selectedItems)
            {
                var success = await _shopService.AddToCartAsync(
                    currentUserId, item.ProductId, item.BatchQuantity).ConfigureAwait(false);
                if (success) successCount++;
            }

            if (successCount > 0)
                ToastService.Instance.Success($"已添加 {successCount} 件商品到购物车");
            else
                ToastService.Instance.Error("批量添加失败，请重试");
        }

        private void NavigateUp()
        {
            if (_selectedProductIndex > 0)
                SelectedProductIndex--;
        }

        private void NavigateDown()
        {
            if (_selectedProductIndex < _filteredProducts.Count - 1)
                SelectedProductIndex++;
        }

        private void ViewSelectedProduct()
        {
            if (_selectedProductIndex >= 0 && _selectedProductIndex < _filteredProducts.Count)
            {
                var product = _filteredProducts[_selectedProductIndex];
                ViewProductRequested?.Invoke(this, product);
            }
        }

        public event EventHandler<ShopProductItem> ViewProductRequested;

        private void ApplySortAndFilter()
        {
            var items = _products.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(_searchText))
            {
                var kw = _searchText.Trim().ToLower();
                items = items.Where(p => p.ProductName.ToLower().Contains(kw) || p.MerchantName.ToLower().Contains(kw));
            }

            items = _sortMode switch
            {
                1 => items.OrderBy(p => p.Price),
                2 => items.OrderByDescending(p => p.Price),
                3 => items.OrderByDescending(p => p.SaleCount),
                _ => items
            };

            FilteredProducts = new ObservableCollection<ShopProductItem>(items);
            OnPropertyChanged(nameof(IsEmpty));

            if (_selectedProductIndex >= _filteredProducts.Count)
                SelectedProductIndex = _filteredProducts.Count - 1;
        }
    }

    public class ShopProductItem : ViewModelBase
    {
        private string _forecastTrend = "→";
        private string _speciesName = "";
        private string _ladderPriceHint = "";
        private bool _isSelected;
        private int _batchQuantity = 1;

        public long ProductId { get; set; }
        public string ProductName { get; set; } = "";
        public decimal Price { get; set; }
        public decimal MarketPrice { get; set; }
        public string MerchantName { get; set; } = "";
        public int Stock { get; set; }
        public int SpeciesId { get; set; }
        public string ImageUrl { get; set; } = "";
        public int SaleCount { get; set; }
        public string Unit { get; set; } = "束";
        public long MerchantId { get; set; }

        public string ForecastTrend
        {
            get => _forecastTrend;
            set => SetProperty(ref _forecastTrend, value);
        }

        public string SpeciesName
        {
            get => _speciesName;
            set => SetProperty(ref _speciesName, value);
        }

        public string LadderPriceHint
        {
            get => _ladderPriceHint;
            set => SetProperty(ref _ladderPriceHint, value);
        }

        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        public int BatchQuantity
        {
            get => _batchQuantity;
            set => SetProperty(ref _batchQuantity, value);
        }

        public string TrendColor => _forecastTrend switch
        {
            "↑" => "#FF26A69A",
            "↓" => "#FFEF5350",
            _ => "#FF787B86"
        };

        public string MarketPriceDisplay => MarketPrice > 0 ? $"¥{MarketPrice:F2}" : "";

        public bool HasMarketPrice => MarketPrice > 0 && MarketPrice > Price;

        public bool IsPresale { get; set; }

        public DateTime? PresaleDeliveryDate { get; set; }

        public string PresaleDeliveryDateText => PresaleDeliveryDate.HasValue
            ? $"预计发货: {PresaleDeliveryDate.Value:yyyy-MM-dd}"
            : "";
    }

    public class SpeciesFilterItem : ViewModelBase
    {
        private bool _isSelected;

        public int SpeciesId { get; set; }
        public string DisplayName { get; set; } = "";

        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }
    }

    public class CategoryFilterItem : ViewModelBase
    {
        private bool _isSelected;
        public long? CategoryId { get; set; }
        public string DisplayName { get; set; } = "";
        public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }
    }

    public class SortModeItem : ViewModelBase
    {
        private bool _isSelected;
        public int Mode { get; set; }
        public string DisplayName { get; set; } = "";
        public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }
    }
}
