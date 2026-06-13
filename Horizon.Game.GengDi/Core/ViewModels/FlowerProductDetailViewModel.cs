using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Horizon.Game.GengDi.Core.Controls;
using Horizon.Game.GengDi.Core.Services;
using Horizon.Game.Message.Network;

namespace Horizon.Game.GengDi.Core.ViewModels
{
    public class FlowerProductDetailViewModel : ViewModelBase
    {
        private readonly FlowerShopService _shopService;
        private readonly FlowerOrderService _orderService;
        private readonly FlowerMerchantService _merchantService;
        private readonly FlowerMarketService _marketService;
        private long _productId;
        private Guid _userId;
        private bool _isLoading;
        private bool _hasProduct;
        private string _productName = "";
        private string _description = "";
        private decimal _price;
        private decimal _marketPrice;
        private int _stock;
        private string _unit = "束";
        private string _images = "";
        private bool _isActive;
        private string _merchantName = "";
        private long _merchantId;
        private int _selectedQuantity = 1;
        private string _selectedSkuCode = "";
        private string _selectedColor = "";
        private string _selectedSize = "";
        private string _selectedVersion = "";
        private decimal _selectedSkuPrice;
        private int _selectedSkuStock;
        private bool _hasSku;
        private ObservableCollection<SkuOption> _skuColors = new();
        private ObservableCollection<SkuOption> _skuSizes = new();
        private ObservableCollection<SkuOption> _skuVersions = new();
        private ObservableCollection<LadderPriceDisplay> _ladderPrices = new();
        private ObservableCollection<RelatedProduct> _relatedProducts = new();
        private bool _isCompareMode;
        private CompareProductInfo _compareProduct;
        private ObservableCollection<CompareProductOption> _compareProductOptions = new();
        private long _selectedCompareProductId;
        private int _speciesId;

        public bool IsLoading { get => _isLoading; set => SetProperty(ref _isLoading, value); }
        public bool HasProduct { get => _hasProduct; set => SetProperty(ref _hasProduct, value); }
        public string ProductName { get => _productName; set => SetProperty(ref _productName, value); }
        public string Description { get => _description; set => SetProperty(ref _description, value); }
        public decimal Price { get => _price; set => SetProperty(ref _price, value); }
        public decimal MarketPrice { get => _marketPrice; set => SetProperty(ref _marketPrice, value); }
        public int Stock { get => _stock; set => SetProperty(ref _stock, value); }
        public string Unit { get => _unit; set => SetProperty(ref _unit, value); }
        public string Images { get => _images; set => SetProperty(ref _images, value); }
        public bool IsActive { get => _isActive; set => SetProperty(ref _isActive, value); }
        public string MerchantName { get => _merchantName; set => SetProperty(ref _merchantName, value); }
        public long MerchantId { get => _merchantId; set => SetProperty(ref _merchantId, value); }
        public int SelectedQuantity { get => _selectedQuantity; set { if (SetProperty(ref _selectedQuantity, value)) UpdateLadderPrice(); } }
        public string SelectedSkuCode { get => _selectedSkuCode; set => SetProperty(ref _selectedSkuCode, value); }
        public string SelectedColor { get => _selectedColor; set { if (SetProperty(ref _selectedColor, value)) RefreshSkuSelection(); } }
        public string SelectedSize { get => _selectedSize; set { if (SetProperty(ref _selectedSize, value)) RefreshSkuSelection(); } }
        public string SelectedVersion { get => _selectedVersion; set { if (SetProperty(ref _selectedVersion, value)) RefreshSkuSelection(); } }
        public decimal SelectedSkuPrice { get => _selectedSkuPrice; set => SetProperty(ref _selectedSkuPrice, value); }
        public int SelectedSkuStock { get => _selectedSkuStock; set => SetProperty(ref _selectedSkuStock, value); }
        public bool HasSku { get => _hasSku; set => SetProperty(ref _hasSku, value); }
        public ObservableCollection<SkuOption> SkuColors { get => _skuColors; set => SetProperty(ref _skuColors, value); }
        public ObservableCollection<SkuOption> SkuSizes { get => _skuSizes; set => SetProperty(ref _skuSizes, value); }
        public ObservableCollection<SkuOption> SkuVersions { get => _skuVersions; set => SetProperty(ref _skuVersions, value); }
        public ObservableCollection<LadderPriceDisplay> LadderPrices { get => _ladderPrices; set => SetProperty(ref _ladderPrices, value); }
        public ObservableCollection<RelatedProduct> RelatedProducts { get => _relatedProducts; set => SetProperty(ref _relatedProducts, value); }

        public bool IsCompareMode
        {
            get => _isCompareMode;
            set
            {
                if (SetProperty(ref _isCompareMode, value))
                {
                    OnPropertyChanged(nameof(ShowSingleView));
                    OnPropertyChanged(nameof(ShowCompareView));
                    if (value && _compareProductOptions.Count == 0)
                        _ = LoadCompareOptionsAsync();
                }
            }
        }

        public bool ShowSingleView => !_isCompareMode;
        public bool ShowCompareView => _isCompareMode;

        public CompareProductInfo CompareProduct
        {
            get => _compareProduct;
            set => SetProperty(ref _compareProduct, value);
        }

        public ObservableCollection<CompareProductOption> CompareProductOptions
        {
            get => _compareProductOptions;
            set => SetProperty(ref _compareProductOptions, value);
        }

        public long SelectedCompareProductId
        {
            get => _selectedCompareProductId;
            set
            {
                if (SetProperty(ref _selectedCompareProductId, value))
                    _ = LoadCompareProductAsync(value);
            }
        }

        public int SpeciesId
        {
            get => _speciesId;
            set => SetProperty(ref _speciesId, value);
        }

        public decimal CurrentPrice => HasSku && SelectedSkuPrice > 0 ? SelectedSkuPrice : Price;
        public string PriceDisplay => $"¥{CurrentPrice:F2}";
        public string MarketPriceDisplay => MarketPrice > 0 ? $"¥{MarketPrice:F2}" : "";
        public bool HasMarketPrice => MarketPrice > 0 && MarketPrice > Price;
        public bool HasLadderPrices => LadderPrices.Count > 0;

        public FlowerProductDetailViewModel()
        {
            _shopService = new FlowerShopService();
            _orderService = new FlowerOrderService();
            _merchantService = new FlowerMerchantService();
            _marketService = new FlowerMarketService();
        }

        public async Task InitializeAsync(long productId, Guid userId)
        {
            _productId = productId;
            _userId = userId;
            IsLoading = true;
            try
            {
                await LoadProductAsync();
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task LoadProductAsync()
        {
            var products = await _shopService.GetActiveProductsAsync();
            var product = products?.FirstOrDefault(p => p.ProductId == _productId);
            if (product == null)
            {
                HasProduct = false;
                return;
            }

            HasProduct = true;
            ProductName = product.ProductName;
            Price = product.Price;
            MarketPrice = product.MarketPrice ?? 0;
            Stock = product.Stock;
            Unit = product.Unit ?? "束";
            Description = product.Description ?? "";
            IsActive = product.IsActive;
            MerchantId = product.MerchantId;
            SpeciesId = product.SpeciesId;
            MerchantName = $"商户 {product.MerchantId}";

            var merchant = await _merchantService.GetMerchantAsync(product.MerchantId);
            if (merchant != null) MerchantName = merchant.ShopName;

            var related = await _marketService.GetRelatedProductsAsync((int)_productId);
            if (related != null) RelatedProducts = new ObservableCollection<RelatedProduct>(related);

            UpdatePriceDisplay();
        }

        private async Task LoadCompareOptionsAsync()
        {
            var products = await _shopService.GetActiveProductsAsync(_speciesId > 0 ? _speciesId : 0, 1, 50);
            if (products != null)
            {
                var options = products
                    .Where(p => p.ProductId != _productId)
                    .Select(p => new CompareProductOption { ProductId = p.ProductId, ProductName = p.ProductName })
                    .ToList();
                CompareProductOptions = new ObservableCollection<CompareProductOption>(options);
            }
        }

        private async Task LoadCompareProductAsync(long productId)
        {
            if (productId <= 0) return;

            var products = await _shopService.GetActiveProductsAsync();
            var product = products?.FirstOrDefault(p => p.ProductId == productId);
            if (product == null)
            {
                CompareProduct = null;
                return;
            }

            var merchantName = $"商户 {product.MerchantId}";
            var merchant = await _merchantService.GetMerchantAsync(product.MerchantId);
            if (merchant != null) merchantName = merchant.ShopName;

            CompareProduct = new CompareProductInfo
            {
                ProductId = product.ProductId,
                ProductName = product.ProductName,
                Price = product.Price,
                MarketPrice = product.MarketPrice ?? 0,
                Stock = product.Stock,
                Unit = product.Unit ?? "束",
                Description = product.Description ?? "",
                MerchantName = merchantName,
                SpeciesId = product.SpeciesId
            };
        }

        public async Task AddToCartAsync()
        {
            var currentUserId = Guid.Empty;
            if (App.CurrentUser != null && App.CurrentUser.UserId != Guid.Empty)
                currentUserId = App.CurrentUser.UserId;

            if (currentUserId == Guid.Empty)
            {
                ToastService.Instance.Warning("请先登录");
                return;
            }
            var success = await _shopService.AddToCartAsync(currentUserId, _productId, SelectedQuantity);
            if (success)
                ToastService.Instance.Success($"已添加「{ProductName}」到购物车");
            else
                ToastService.Instance.Error("添加购物车失败");
        }

        public async Task<bool> BuyNowAsync()
        {
            var currentUserId = Guid.Empty;
            if (App.CurrentUser != null && App.CurrentUser.UserId != Guid.Empty)
                currentUserId = App.CurrentUser.UserId;

            if (currentUserId == Guid.Empty)
            {
                ToastService.Instance.Warning("请先登录");
                return false;
            }

            var added = await _shopService.AddToCartAsync(currentUserId, _productId, SelectedQuantity);
            if (!added)
            {
                ToastService.Instance.Error("操作失败");
                return false;
            }

            ToastService.Instance.Info("正在创建订单...");

            try
            {
                var cartItem = new CartItem
                {
                    ProductId = _productId,
                    ProductName = ProductName,
                    Price = CurrentPrice,
                    Quantity = SelectedQuantity,
                    MerchantId = MerchantId,
                    MerchantName = MerchantName,
                    Stock = Stock
                };

                var address = await _shopService.GetDefaultShippingAddressAsync(currentUserId);

                decimal freight = 0;
                if (address != null)
                {
                    var templates = await _shopService.GetFreightTemplatesAsync(MerchantId);
                    if (templates != null && templates.Count > 0)
                        freight = templates[0].IsFree ? 0 : templates[0].FirstPrice;
                }

                var orderId = await _orderService.CreateOrderAsync(currentUserId, new List<CartItem> { cartItem }, address, freight, 0, 0);

                if (orderId == null || orderId <= 0)
                {
                    ToastService.Instance.Error("创建订单失败，请稍后重试");
                    return false;
                }

                ToastService.Instance.Info("订单已创建，正在支付...");

                var orderTotal = CurrentPrice * SelectedQuantity + freight;
                var payResult = await _orderService.PayOrderAsync(orderId.Value, 1, orderTotal);

                await _shopService.RemoveFromCartAsync(currentUserId, _productId);

                if (payResult?.Success == true)
                    ToastService.Instance.Success("下单成功！正在跳转订单中心...");
                else
                    ToastService.Instance.Warning(payResult?.ErrorMessage ?? "订单已创建，请在订单中心完成支付");

                return true;
            }
            catch (Exception)
            {
                ToastService.Instance.Error("下单异常，请稍后重试");
                return false;
            }
        }

        private void RefreshSkuSelection()
        {
            UpdatePriceDisplay();
        }

        private void UpdateLadderPrice()
        {
            UpdatePriceDisplay();
        }

        private void UpdatePriceDisplay()
        {
            OnPropertyChanged(nameof(CurrentPrice));
            OnPropertyChanged(nameof(PriceDisplay));
            OnPropertyChanged(nameof(MarketPriceDisplay));
            OnPropertyChanged(nameof(HasMarketPrice));
            OnPropertyChanged(nameof(HasLadderPrices));
        }
    }

    public class SkuOption
    {
        public string Value { get; set; } = "";
        public bool IsSelected { get; set; }
    }

    public class LadderPriceDisplay
    {
        public int MinBatch { get; set; }
        public int MaxBatch { get; set; }
        public decimal Price { get; set; }
        public string Display => MaxBatch > 0
            ? $"{MinBatch}-{MaxBatch} 件: ¥{Price:F2}/件"
            : $"≥{MinBatch} 件: ¥{Price:F2}/件";
    }

    public class CompareProductInfo : ViewModelBase
    {
        private long _productId;
        private string _productName = "";
        private decimal _price;
        private decimal _marketPrice;
        private int _stock;
        private string _unit = "束";
        private string _description = "";
        private string _merchantName = "";
        private int _speciesId;

        public long ProductId { get => _productId; set => SetProperty(ref _productId, value); }
        public string ProductName { get => _productName; set => SetProperty(ref _productName, value); }
        public decimal Price { get => _price; set => SetProperty(ref _price, value); }
        public decimal MarketPrice { get => _marketPrice; set => SetProperty(ref _marketPrice, value); }
        public int Stock { get => _stock; set => SetProperty(ref _stock, value); }
        public string Unit { get => _unit; set => SetProperty(ref _unit, value); }
        public string Description { get => _description; set => SetProperty(ref _description, value); }
        public string MerchantName { get => _merchantName; set => SetProperty(ref _merchantName, value); }
        public int SpeciesId { get => _speciesId; set => SetProperty(ref _speciesId, value); }

        public string PriceDisplay => $"¥{Price:F2}";
        public string MarketPriceDisplay => MarketPrice > 0 ? $"¥{MarketPrice:F2}" : "";
        public bool HasMarketPrice => MarketPrice > 0 && MarketPrice > Price;
    }

    public class CompareProductOption
    {
        public long ProductId { get; set; }
        public string ProductName { get; set; } = "";
    }
}
