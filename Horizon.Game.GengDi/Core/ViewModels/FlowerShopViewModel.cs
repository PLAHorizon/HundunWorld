using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Horizon.Game.GengDi.Core.Controls;
using Horizon.Game.GengDi.Core.Helpers;
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
        private decimal _lastAvgPrice;
        private string _avgPriceText = "—";
        private string _priceChangeText = "—";
        private bool _isPriceUp;
        private string _totalSalesText = "0";
        private string _merchantCountText = "0";
        private string _updatedAtText = "";
        private string _batchSummaryText = "";
        private string _batchTotalText = "¥0.00";
        private bool _hasSelection;
        private int _viewMode;
        private string _cartCountText = "购物车";
        private string _inTransitText = "—";
        private string _headerMetaText = "";
        private readonly HashSet<long> _favoriteIds = new();

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

        public bool IsCardView => !IsBatchMode && ViewMode == 0;
        public bool IsBatchView => IsBatchMode || ViewMode == 1;

        /// <summary>视图模式：0=网格 1=列表（设计稿右上角「网格」pill）。</summary>
        public int ViewMode
        {
            get => _viewMode;
            set
            {
                if (SetProperty(ref _viewMode, value))
                {
                    OnPropertyChanged(nameof(IsGridView));
                    OnPropertyChanged(nameof(IsListView));
                    OnPropertyChanged(nameof(IsCardView));
                    OnPropertyChanged(nameof(IsBatchView));
                }
            }
        }

        public bool IsGridView => ViewMode == 0 && !IsBatchMode;
        public bool IsListView => ViewMode == 1 || IsBatchMode;

        /// <summary>视图切换分段中「列表」高亮态（批量模式下不点亮列表段）。</summary>
        public bool IsListSegActive => ViewMode == 1 && !IsBatchMode;

        public int SelectedProductIndex
        {
            get => _selectedProductIndex;
            set
            {
                if (SetProperty(ref _selectedProductIndex, value))
                {
                    UpdateHighlights();
                    OnPropertyChanged(nameof(SelectedProduct));
                }
            }
        }

        /// <summary>同步商品卡高亮态（键盘导航选中 = 设计稿 selected 变体）。</summary>
        private void UpdateHighlights()
        {
            for (int i = 0; i < _filteredProducts.Count; i++)
                _filteredProducts[i].IsHighlighted = i == _selectedProductIndex;
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
        public ICommand ClearFiltersCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand ToggleFavoriteCommand { get; }
        public ICommand HotSearchCommand { get; }
        public ICommand ClearSelectionCommand { get; }
        public ICommand SetViewModeCommand { get; }

        // ===== 行情运营条（数据均来自当前商品集合，非硬编码） =====
        public string AvgPriceText
        {
            get => _avgPriceText;
            private set => SetProperty(ref _avgPriceText, value);
        }

        public string PriceChangeText
        {
            get => _priceChangeText;
            private set => SetProperty(ref _priceChangeText, value);
        }

        public bool IsPriceUp
        {
            get => _isPriceUp;
            private set => SetProperty(ref _isPriceUp, value);
        }

        public string TotalSalesText
        {
            get => _totalSalesText;
            private set => SetProperty(ref _totalSalesText, value);
        }

        public string MerchantCountText
        {
            get => _merchantCountText;
            private set => SetProperty(ref _merchantCountText, value);
        }

        public string UpdatedAtText
        {
            get => _updatedAtText;
            private set => SetProperty(ref _updatedAtText, value);
        }

        // ===== 批量采购结算条 =====
        public string BatchSummaryText
        {
            get => _batchSummaryText;
            private set => SetProperty(ref _batchSummaryText, value);
        }

        public string BatchTotalText
        {
            get => _batchTotalText;
            private set => SetProperty(ref _batchTotalText, value);
        }

        public bool HasSelection
        {
            get => _hasSelection;
            private set => SetProperty(ref _hasSelection, value);
        }

        // ===== 页头购物车胶囊 / 热搜标签 / 头部 meta =====
        public string CartCountText
        {
            get => _cartCountText;
            private set => SetProperty(ref _cartCountText, value);
        }

        /// <summary>冷链在途（暂无数据源，待接入后替换）。</summary>
        public string InTransitText
        {
            get => _inTransitText;
            private set => SetProperty(ref _inTransitText, value);
        }

        /// <summary>页头 meta：产区数 / 上新件数 由商品集合实时计算。</summary>
        public string HeaderMetaText
        {
            get => _headerMetaText;
            private set => SetProperty(ref _headerMetaText, value);
        }

        public ObservableCollection<string> HotSearchTags { get; } = new()
        {
            "昆明玫瑰", "漳州百合", "凌源康乃馨"
        };

        /// <summary>热搜标签（设计稿 2:30：首项粉底高亮「昆明玫瑰」，其余灰胶囊）。</summary>
        public ObservableCollection<HotSearchTagItem> HotSearchItems { get; } = new()
        {
            new() { Text = "昆明玫瑰", IsAccent = true },
            new() { Text = "漳州百合" },
            new() { Text = "凌源康乃馨" }
        };

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
            ClearFiltersCommand = new RelayCommand(ClearFilters);
            RefreshCommand = new AsyncCommand(RefreshAsync);
            ToggleFavoriteCommand = new RelayCommand<long>(ToggleFavorite);
            HotSearchCommand = new RelayCommand<string>(tag =>
            {
                if (string.IsNullOrWhiteSpace(tag)) return;
                SearchText = tag;
                OnPropertyChanged(nameof(SearchText));
                _currentPage = 1;
                _ = LoadProductsAsync();
            });
            ClearSelectionCommand = new RelayCommand(() =>
            {
                foreach (var p in _filteredProducts)
                    p.IsSelected = false;
            });
            SetViewModeCommand = new RelayCommand<string>(v =>
            {
                if (int.TryParse(v, out var mode))
                    ViewMode = mode;
            });

            SpeciesFilters = new ObservableCollection<SpeciesFilterItem>
            {
                new() { SpeciesId = 0, DisplayName = "全部" }
            };

            SortModes = new ObservableCollection<SortModeItem>
            {
                new() { Mode = 0, DisplayName = "综合排序" },
                new() { Mode = 1, DisplayName = "价格从低到高" },
                new() { Mode = 2, DisplayName = "价格从高到低" },
                new() { Mode = 3, DisplayName = "销量优先" }
            };

            _ = LoadCategoriesAsync();
            _ = LoadProductsAsync();
            _ = RefreshCartCountAsync();
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
                // 品种字典（chips 名称 / 卡片封面色与产地）依赖品种缓存，先确保已加载
                await _speciesLookup.EnsureLoadedAsync().ConfigureAwait(false);
                EnsureSpeciesFiltersLoaded();

                var products = await _shopService.GetActiveProductsAsync(
                    _selectedSpeciesId, _currentPage, 20).ConfigureAwait(false);

                if (products != null && products.Count > 0)
                {
                    var items = products.Select(p =>
                    {
                        var speciesName = GetSpeciesName(p.SpeciesId);
                        var visual = FlowerSpeciesPalette.Resolve(speciesName);
                        var item = new ShopProductItem
                        {
                            ProductId = p.ProductId,
                            ProductName = p.ProductName,
                            Price = p.Price,
                            MarketPrice = p.MarketPrice ?? 0,
                            MerchantName = p.MerchantName,
                            Stock = p.Stock,
                            SpeciesId = p.SpeciesId,
                            ImageUrl = p.ImageUrl,
                            SpeciesName = speciesName,
                            IsPresale = p.IsPresale,
                            PresaleDeliveryDate = p.PresaleDeliveryDate,
                            SpeciesColor = visual.Accent,
                            SpeciesColorDeep = visual.AccentDeep,
                            OriginName = visual.Origin,
                            DeliveryEta = visual.Eta,
                            OnBatchChanged = UpdateBatchSummary
                        };

                        if (_favoriteIds.Contains(item.ProductId))
                            item.IsFavorite = true;

                        return item;
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

                    RefreshMarketStats();

                    _ = LoadForecastDataAsync(items);
                    _ = LoadLadderPriceHintsAsync(items);
                    _ = LoadImagesAsync(items);
                }
                else
                {
                    if (_currentPage == 1)
                    {
                        Products = new ObservableCollection<ShopProductItem>();

                        // 后端不可用 + 显式 --preview-mock 标志：填充演示商品供 UI 预览验证
                        if (PreviewMockEnabled)
                        {
                            LoadMockProducts();
                            RefreshMarketStats();
                        }
                    }
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

        /// <summary>开发调试：命令行含 --preview-mock 时启用演示数据（后端不可用时预览 UI）。</summary>
        public static bool PreviewMockEnabled { get; } = Environment.GetCommandLineArgs()
            .Any(a => a.Equals("--preview-mock", StringComparison.OrdinalIgnoreCase));

        /// <summary>演示商品数据：覆盖各品种/趋势/售磐/预售变体，仅供 --preview-mock 预览。</summary>
        private void LoadMockProducts()
        {
            (string name, int speciesId, decimal price, decimal market, int sold, int stock, string trend, bool presale, string ladder)[] demo
            =
            {
                ("卡罗拉红玫瑰 · 20 支装", 1, 128m, 168m, 1200, 320, "↑", false, "满 50 减 8"),
                ("白色百合 · 10 支装", 2, 88m, 118m, 860, 240, "→", false, "满 30 减 5"),
                ("康乃馨混色 · 30 支装", 3, 66m, 89m, 2400, 500, "↑", false, "满 99 减 15"),
                ("向日葵 · 10 支装", 4, 58m, 78m, 640, 180, "↑", false, "满 50 减 8"),
                ("尤加利叶 · 大扎装", 5, 32m, 42m, 3100, 620, "→", false, "满 99 减 15"),
                ("粉色郁金香 · 20 支装", 4, 168m, 208m, 320, 90, "↑", false, "满 200 减 30"),
                ("春日限定混合花束", 4, 198m, 258m, 480, 120, "→", false, "满 200 减 30"),
                ("玫瑰配草套餐 · 40 支", 1, 138m, 176m, 1500, 260, "↑", true, "满 99 减 15"),
                ("蓝色妖姬 · 9 支装", 1, 99m, 0m, 0, 0, "↓", false, ""),
                ("蜜桃花束 · 19 支装", 4, 158m, 0m, 210, 60, "↓", true, "")
            };

            var mockId = 900000L;
            var items = demo.Select(d =>
            {
                var speciesName = GetSpeciesName(d.speciesId);
                var visual = FlowerSpeciesPalette.Resolve(speciesName);
                return new ShopProductItem
                {
                    ProductId = mockId++,
                    ProductName = d.name,
                    Price = d.price,
                    MarketPrice = d.market,
                    MerchantName = "演示花田直供",
                    Stock = d.stock,
                    SaleCount = d.sold,
                    SpeciesId = d.speciesId,
                    SpeciesName = speciesName,
                    SpeciesColor = visual.Accent,
                    SpeciesColorDeep = visual.AccentDeep,
                    OriginName = visual.Origin,
                    DeliveryEta = visual.Eta,
                    ForecastTrend = d.trend,
                    IsPresale = d.presale,
                    PresaleDeliveryDate = d.presale ? DateTime.Now.AddDays(7) : null,
                    LadderPriceHint = d.ladder,
                    OnBatchChanged = UpdateBatchSummary
                };
            }).ToList();

            foreach (var item in items)
                _products.Add(item);
        }

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
            {
                ToastService.Instance.Success($"已添加「{product.ProductName}」到购物车");
                await RefreshCartCountAsync().ConfigureAwait(false);
            }
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
            {
                ToastService.Instance.Success($"已添加 {successCount} 件商品到购物车");
                await RefreshCartCountAsync().ConfigureAwait(false);
            }
            else
                ToastService.Instance.Error("批量添加失败，请重试");
        }

        /// <summary>刷新页头「购物车 · N 件」胶囊文案。</summary>
        private async Task RefreshCartCountAsync()
        {
            try
            {
                var userId = Guid.Empty;
                if (App.CurrentUser != null && Guid.TryParse(App.CurrentUser.Id, out var uid))
                    userId = uid;

                if (userId == Guid.Empty)
                {
                    CartCountText = "购物车";
                    return;
                }

                var items = await _shopService.GetCartItemsAsync(userId).ConfigureAwait(false);
                var count = items?.Sum(i => i.Quantity) ?? 0;
                CartCountText = count > 0 ? $"购物车 · {count} 件" : "购物车";
            }
            catch
            {
                CartCountText = "购物车";
            }
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

        private async Task RefreshAsync()
        {
            _currentPage = 1;
            await LoadProductsAsync();
        }

        private void ClearFilters()
        {
            SearchText = "";
            _sortMode = 0;
            _selectedSpeciesId = 0;
            _selectedCategoryId = null;

            foreach (var f in SpeciesFilters)
                f.IsSelected = f.SpeciesId == 0;
            foreach (var c in CategoryFilters)
                c.IsSelected = c.CategoryId == null;
            foreach (var s in SortModes)
                s.IsSelected = s.Mode == 0;

            OnPropertyChanged(nameof(SearchText));
            OnPropertyChanged(nameof(SortMode));

            _currentPage = 1;
            _ = LoadProductsAsync();
        }

        private void ToggleFavorite(long productId)
        {
            var item = _products.FirstOrDefault(p => p.ProductId == productId);
            if (item == null) return;

            if (_favoriteIds.Contains(productId))
            {
                _favoriteIds.Remove(productId);
                item.IsFavorite = false;
            }
            else
            {
                _favoriteIds.Add(productId);
                item.IsFavorite = true;
            }
        }

        /// <summary>
        /// 刷新行情运营条 — 全部指标由当前商品集合实时计算，不写入硬编码值。
        /// </summary>
        private void RefreshMarketStats()
        {
            if (_products.Count == 0)
            {
                AvgPriceText = "—";
                PriceChangeText = "—";
                TotalSalesText = "0";
                MerchantCountText = "0";
                UpdatedAtText = "";
                HeaderMetaText = "";
                return;
            }

            var avg = _products.Average(p => p.Price);
            AvgPriceText = $"¥ {avg:F2}";

            if (_lastAvgPrice > 0)
            {
                var diff = avg - _lastAvgPrice;
                var percent = diff / _lastAvgPrice * 100m;
                IsPriceUp = diff >= 0;
                PriceChangeText = $"{(diff >= 0 ? "+" : "")}{percent:F2}%";
            }
            else
            {
                IsPriceUp = true;
                PriceChangeText = "首次加载";
            }

            _lastAvgPrice = avg;

            TotalSalesText = _products.Sum(p => p.SaleCount).ToString("N0", CultureInfo.InvariantCulture);
            MerchantCountText = _products.Select(p => p.MerchantId).Distinct().Count()
                .ToString(CultureInfo.InvariantCulture);
            UpdatedAtText = $"今日 {DateTime.Now:HH:mm} 更新";

            // 冷链在途暂无数据源，待接入后替换
            InTransitText = "—";

            var originCount = _products.Select(p => p.OriginName)
                .Where(o => !string.IsNullOrWhiteSpace(o)).Distinct().Count();
            HeaderMetaText = $"全国 {originCount} 个产区直供 · 今日上新 {_products.Count} 件 · 24h 冷链直达";
        }

        /// <summary>刷新分类筛选 chips 的商品数量（全部 N / 红玫瑰 N ...）。</summary>
        private void RefreshSpeciesCounts()
        {
            foreach (var f in SpeciesFilters)
            {
                f.Count = f.SpeciesId == 0
                    ? _products.Count
                    : _products.Count(p => p.SpeciesId == f.SpeciesId);
            }
        }

        /// <summary>品种字典加载后补齐筛选 chips（保留当前选中态）。</summary>
        private void EnsureSpeciesFiltersLoaded()
        {
            if (SpeciesFilters.Count > 1)
                return;

            var selectedId = SpeciesFilters.FirstOrDefault(f => f.IsSelected)?.SpeciesId ?? _selectedSpeciesId;
            SpeciesFilters.Clear();
            SpeciesFilters.Add(new SpeciesFilterItem { SpeciesId = 0, DisplayName = "全部", IsSelected = selectedId == 0, Count = _products.Count });
            foreach (var kv in _speciesLookup.GetAllSpecies().OrderBy(kv => kv.Key))
            {
                SpeciesFilters.Add(new SpeciesFilterItem
                {
                    SpeciesId = kv.Key,
                    DisplayName = kv.Value,
                    IsSelected = kv.Key == selectedId
                });
            }
        }

        /// <summary>刷新批量采购结算条（选中数量、商户数、合计金额）。</summary>
        private void UpdateBatchSummary()
        {
            var selected = _filteredProducts.Where(p => p.IsSelected).ToList();
            HasSelection = selected.Count > 0;

            BatchSummaryText = HasSelection
                ? $"已选 {selected.Count} 件 · 共 {selected.Select(p => p.MerchantId).Distinct().Count()} 个商户"
                : "";

            var total = selected.Sum(p => p.Price * Math.Max(1, p.BatchQuantity));
            BatchTotalText = $"¥ {total:F2}";
        }

        private static async Task LoadImagesAsync(List<ShopProductItem> items)
        {
            foreach (var item in items)
                await item.LoadImageAsync().ConfigureAwait(false);
        }

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
            UpdateBatchSummary();
            RefreshSpeciesCounts();
            UpdateHighlights();

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
        private bool _isFavorite;
        private bool _hasImage;
        private Bitmap? _imageBitmap;
        private bool _isHighlighted;

        private static readonly IBrush FavoriteActiveBrush = new SolidColorBrush(Color.Parse("#FFFF595E"));
        private static readonly IBrush FavoriteIdleBrush = new SolidColorBrush(Color.Parse("#FFA7B1C3"));

        private static readonly IBrush TrendUpBrush = new SolidColorBrush(Color.Parse("#FF4D4F"));
        private static readonly IBrush TrendDownBrush = new SolidColorBrush(Color.Parse("#26A69A"));
        private static readonly IBrush TrendFlatBrush = new SolidColorBrush(Color.Parse("#8A93A6"));

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

        /// <summary>品种主色 / 深色，用于无图降级封面渐变。</summary>
        public Color SpeciesColor { get; set; } = Color.Parse("#5A90FF");
        public Color SpeciesColorDeep { get; set; } = Color.Parse("#1F4FD6");

        /// <summary>主产区与常规时效（来自品种产区映射表）。</summary>
        public string OriginName { get; set; } = "";
        public string DeliveryEta { get; set; } = "";

        /// <summary>选中态或批量数量变化时回调宿主 VM 刷新结算条。</summary>
        public Action? OnBatchChanged { get; set; }

        public string ForecastTrend
        {
            get => _forecastTrend;
            set
            {
                if (SetProperty(ref _forecastTrend, value))
                {
                    OnPropertyChanged(nameof(TrendBrush));
                    OnPropertyChanged(nameof(TrendLabel));
                }
            }
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
            set
            {
                if (SetProperty(ref _isSelected, value))
                    OnBatchChanged?.Invoke();
            }
        }

        public int BatchQuantity
        {
            get => _batchQuantity;
            set
            {
                if (SetProperty(ref _batchQuantity, value))
                    OnBatchChanged?.Invoke();
            }
        }

        public bool IsFavorite
        {
            get => _isFavorite;
            set
            {
                if (SetProperty(ref _isFavorite, value))
                    OnPropertyChanged(nameof(FavoriteBrush));
            }
        }

        /// <summary>收藏心形颜色（激活红 / 空闲灰）。</summary>
        public IBrush FavoriteBrush => _isFavorite ? FavoriteActiveBrush : FavoriteIdleBrush;

        /// <summary>键盘导航高亮（卡片蓝色描边，设计稿 selected 变体）。</summary>
        public bool IsHighlighted
        {
            get => _isHighlighted;
            set => SetProperty(ref _isHighlighted, value);
        }

        /// <summary>是否可加购（非售磐）。</summary>
        public bool IsAvailable => Stock > 0;

        public Bitmap? ImageBitmap
        {
            get => _imageBitmap;
            private set => SetProperty(ref _imageBitmap, value);
        }

        public bool HasImage
        {
            get => _hasImage;
            private set => SetProperty(ref _hasImage, value);
        }

        /// <summary>趋势画刷（国内习惯：涨红跌绿）。</summary>
        public IBrush TrendBrush => _forecastTrend switch
        {
            "↑" => TrendUpBrush,
            "↓" => TrendDownBrush,
            _ => TrendFlatBrush
        };

        public string TrendLabel => _forecastTrend switch
        {
            "↑" => "看涨",
            "↓" => "看跌",
            _ => "平稳"
        };

        public string TrendColor => _forecastTrend switch
        {
            "↑" => "#FFFF4D4F",
            "↓" => "#FF26A69A",
            _ => "#FF8A93A6"
        };

        public string MarketPriceDisplay => MarketPrice > 0 ? $"¥{MarketPrice:F2}" : "";

        public bool HasMarketPrice => MarketPrice > 0 && MarketPrice > Price;

        public bool IsSoldOut => Stock <= 0;

        public bool IsPresale { get; set; }

        public DateTime? PresaleDeliveryDate { get; set; }

        public string PresaleDeliveryDateText => PresaleDeliveryDate.HasValue
            ? $"预计发货: {PresaleDeliveryDate.Value:yyyy-MM-dd}"
            : "";

        public string SaleDisplay => $"已售 {SaleCount:N0} {Unit}";

        public string StockDisplay => $"库存 {Stock:N0}";

        /// <summary>「已售 x 束 · 库存 x」单行文案。</summary>
        public string SaleStockText => $"{SaleDisplay} · {StockDisplay}";

        /// <summary>「商户 · 产地 · 时效」单行文案。</summary>
        public string MerchantOriginText => $"{MerchantName} · {OriginEtaText}";

        public string OriginEtaText => string.IsNullOrWhiteSpace(OriginName)
            ? DeliveryEta
            : $"{OriginName} · {DeliveryEta}";

        /// <summary>售出占比（0-100），驱动卡片库存进度条。</summary>
        public double SalesRatio
        {
            get
            {
                var total = SaleCount + Stock;
                return total <= 0 ? 0 : Math.Clamp(SaleCount * 100.0 / total, 0, 100);
            }
        }

        /// <summary>库存进度条像素宽度（卡片内宽 321 - 16×2 padding = 289）。</summary>
        public double SalesBarWidth => Math.Round(SalesRatio * 2.89, 1);

        /// <summary>异步加载商品图；失败保持 HasImage=false，由 UI 走品种色降级。</summary>
        public async Task LoadImageAsync()
        {
            if (string.IsNullOrWhiteSpace(ImageUrl))
                return;

            var bitmap = await WebImageCache.GetAsync(ImageUrl).ConfigureAwait(false);
            if (bitmap != null)
            {
                ImageBitmap = bitmap;
                HasImage = true;
            }
        }
    }

    public class SpeciesFilterItem : ViewModelBase
    {
        private bool _isSelected;
        private int _count;

        public int SpeciesId { get; set; }
        public string DisplayName { get; set; } = "";

        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        public int Count
        {
            get => _count;
            set
            {
                if (SetProperty(ref _count, value))
                    OnPropertyChanged(nameof(DisplayText));
            }
        }

        /// <summary>chip 文案：名称 + 数量（如「红玫瑰 86」）。</summary>
        public string DisplayText => Count > 0 ? $"{DisplayName} {Count}" : DisplayName;
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

    /// <summary>行情条快捷品类胶囊（设计稿 2:30）。</summary>
    public class HotSearchTagItem
    {
        public string Text { get; set; } = "";

        /// <summary>是否高亮款（粉底粉边，如「昆明玫瑰」）。</summary>
        public bool IsAccent { get; set; }
    }
}
