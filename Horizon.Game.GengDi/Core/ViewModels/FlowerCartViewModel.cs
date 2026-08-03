using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Horizon.Game.GengDi.Core.Controls;
using Horizon.Game.GengDi.Core.Services;

namespace Horizon.Game.GengDi.Core.ViewModels
{
    public class FlowerCartViewModel : ViewModelBase
    {
        private readonly FlowerShopService _shopService;
        private readonly FlowerOrderService _orderService;
        private readonly FlowerMerchantService _merchantService;
        private ObservableCollection<CartItem> _items = new();
        private bool _isLoading;
        private Guid _userId;
        private bool _isCheckingOut;
        private ShippingAddressInfo _selectedAddress;
        private ObservableCollection<ShippingAddressInfo> _addresses = new();
        private CouponInfo _selectedCoupon;
        private ObservableCollection<CouponInfo> _availableCoupons = new();
        private decimal _freightAmount;
        private decimal _discountAmount;
        private decimal _fullDiscountAmount;
        private decimal _orderTotalAmount;
        private bool _showSettlement;
        private bool _showPaymentDialog;
        private decimal _paymentAmount;
        private int _selectedPaymentChannel = 1;
        private bool _isPaymentProcessing;
        private string _paymentResultMessage;
        private long? _pendingOrderId;
        private bool _isAllSelected = true;

        public ObservableCollection<CartItem> Items { get => _items; set => SetProperty(ref _items, value); }
        public bool IsLoading { get => _isLoading; set => SetProperty(ref _isLoading, value); }
        public bool IsEmpty => !_isLoading && _items.Count == 0;
        public decimal ProductTotalAmount => _items.Sum(i => i.Price * i.Quantity);
        public int TotalCount => _items.Sum(i => i.Quantity);
        public bool IsCheckingOut { get => _isCheckingOut; set => SetProperty(ref _isCheckingOut, value); }
        public bool CanCheckout => !_isCheckingOut && _items.Count > 0 && _userId != Guid.Empty;
        public ShippingAddressInfo SelectedAddress { get => _selectedAddress; set { SetProperty(ref _selectedAddress, value); _ = RecalculateAsync(); } }
        public ObservableCollection<ShippingAddressInfo> Addresses { get => _addresses; set => SetProperty(ref _addresses, value); }
        public CouponInfo SelectedCoupon { get => _selectedCoupon; set { SetProperty(ref _selectedCoupon, value); _ = RecalculateAsync(); } }
        public ObservableCollection<CouponInfo> AvailableCoupons { get => _availableCoupons; set => SetProperty(ref _availableCoupons, value); }
        public decimal FreightAmount { get => _freightAmount; set => SetProperty(ref _freightAmount, value); }
        public decimal DiscountAmount { get => _discountAmount; set => SetProperty(ref _discountAmount, value); }
        public decimal FullDiscountAmount { get => _fullDiscountAmount; set => SetProperty(ref _fullDiscountAmount, value); }
        public decimal OrderTotalAmount { get => _orderTotalAmount; set => SetProperty(ref _orderTotalAmount, value); }
        public bool ShowSettlement { get => _showSettlement; set => SetProperty(ref _showSettlement, value); }
        public bool ShowPaymentDialog { get => _showPaymentDialog; set => SetProperty(ref _showPaymentDialog, value); }
        public decimal PaymentAmount { get => _paymentAmount; set => SetProperty(ref _paymentAmount, value); }
        public int SelectedPaymentChannel { get => _selectedPaymentChannel; set => SetProperty(ref _selectedPaymentChannel, value); }
        public bool IsPaymentProcessing { get => _isPaymentProcessing; set => SetProperty(ref _isPaymentProcessing, value); }
        public string PaymentResultMessage { get => _paymentResultMessage; set => SetProperty(ref _paymentResultMessage, value); }
        public long? PendingOrderId { get => _pendingOrderId; set => SetProperty(ref _pendingOrderId, value); }

        /// <summary>全选状态</summary>
        public bool IsAllSelected
        {
            get => _isAllSelected;
            set
            {
                if (SetProperty(ref _isAllSelected, value))
                {
                    foreach (var item in _items) item.IsSelected = value;
                }
            }
        }

        /// <summary>合计金额别名，等价于 ProductTotalAmount</summary>
        public decimal TotalAmount => ProductTotalAmount;
        public string FreightDisplay => FreightAmount > 0 ? $"¥{FreightAmount:F2}" : "免运费";
        public string DiscountDisplay => DiscountAmount > 0 ? $"-¥{DiscountAmount:F2}" : "¥0.00";
        public string FullDiscountDisplay => FullDiscountAmount > 0 ? $"-¥{FullDiscountAmount:F2}" : "¥0.00";
        public string OrderTotalDisplay => $"¥{OrderTotalAmount:F2}";
        public string ProductTotalDisplay => $"¥{ProductTotalAmount:F2}";
        public bool HasAddresses => Addresses.Count > 0;
        public bool HasCoupons => AvailableCoupons.Count > 0;

        public ICommand RemoveItemCommand { get; }
        public ICommand IncreaseQuantityCommand { get; }
        public ICommand DecreaseQuantityCommand { get; }
        public ICommand CheckoutCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand ClearCartCommand { get; }
        public ICommand ShowSettlementCommand { get; }
        public ICommand HideSettlementCommand { get; }
        public ICommand ConfirmPaymentCommand { get; }
        public ICommand CancelPaymentCommand { get; }
        public ICommand SelectAllCommand { get; }

        public FlowerCartViewModel() : this(Guid.Empty) { }

        public FlowerCartViewModel(Guid userId)
        {
            _userId = userId;
            _shopService = new FlowerShopService();
            _orderService = new FlowerOrderService();
            _merchantService = new FlowerMerchantService();
            RemoveItemCommand = new AsyncCommand<long>(RemoveItemAsync);
            IncreaseQuantityCommand = new AsyncCommand<long>(id => UpdateQuantityAsync(id, 1));
            DecreaseQuantityCommand = new AsyncCommand<long>(id => UpdateQuantityAsync(id, -1));
            CheckoutCommand = new AsyncCommand(CheckoutAsync);
            RefreshCommand = new AsyncCommand(LoadCartAsync);
            ClearCartCommand = new AsyncCommand(ClearCartAsync);
            ShowSettlementCommand = new AsyncCommand(OpenSettlementAsync);
            HideSettlementCommand = new AsyncCommand(() => { ShowSettlement = false; return Task.CompletedTask; });
            ConfirmPaymentCommand = new AsyncCommand(ConfirmPaymentAsync);
            CancelPaymentCommand = new RelayCommand(CancelPayment);
            SelectAllCommand = new RelayCommand(() => IsAllSelected = !IsAllSelected);
            _ = LoadCartAsync();
        }

        public void SetUserId(Guid userId)
        {
            _userId = userId;
            _ = LoadCartAsync();
        }

        private async Task LoadCartAsync()
        {
            if (_userId == Guid.Empty)
            {
                // 设计期/演示用模拟数据（参考原型：红玫瑰 / 百合 / 混合花束）
                Items = new ObservableCollection<CartItem>(GetMockCartItems());
                NotifyTotalsChanged();
                await Task.CompletedTask;
                return;
            }
            IsLoading = true;
            try
            {
                var items = await _shopService.GetCartItemsAsync(_userId).ConfigureAwait(false);
                Items = items != null
                    ? new ObservableCollection<CartItem>(items)
                    : new ObservableCollection<CartItem>();
                NotifyTotalsChanged();
            }
            catch { }
            finally { IsLoading = false; }
        }

        /// <summary>
        /// 返回原型中的 3 个模拟购物车项（红玫瑰 / 百合 / 混合花束）。
        /// </summary>
        private static List<CartItem> GetMockCartItems()
        {
            return new List<CartItem>
            {
                new CartItem
                {
                    CartItemId = 1,
                    ProductId = 1001,
                    ProductName = "高原红玫瑰（20支/束）",
                    Price = 58.00m,
                    Quantity = 2,
                    MerchantName = "云端花田直供",
                    MerchantId = 1,
                    Stock = 1280,
                    IconEmoji = "🌹",
                    Spec = "20支/束",
                    IsSelected = true
                },
                new CartItem
                {
                    CartItemId = 2,
                    ProductId = 1002,
                    ProductName = "雪山白百合（10支/束）",
                    Price = 42.50m,
                    Quantity = 1,
                    MerchantName = "春田花卉农场",
                    MerchantId = 2,
                    Stock = 860,
                    IconEmoji = "🌸",
                    Spec = "10支/束",
                    IsSelected = true
                },
                new CartItem
                {
                    CartItemId = 3,
                    ProductId = 1003,
                    ProductName = "晨曦混合花束（含康乃馨）",
                    Price = 88.00m,
                    Quantity = 3,
                    MerchantName = "花语工坊",
                    MerchantId = 3,
                    Stock = 320,
                    IconEmoji = "🌻",
                    Spec = "含康乃馨",
                    IsSelected = true
                }
            };
        }

        private async Task RemoveItemAsync(long productId)
        {
            if (_userId == Guid.Empty) return;
            var item = _items.FirstOrDefault(i => i.ProductId == productId);
            var success = await _shopService.RemoveFromCartAsync(_userId, productId).ConfigureAwait(false);
            if (success)
            {
                ToastService.Instance.Success(item != null ? $"已移除「{item.ProductName}」" : "已移除");
                await LoadCartAsync();
            }
        }

        private async Task UpdateQuantityAsync(long productId, int delta)
        {
            if (_userId == Guid.Empty) return;
            var item = _items.FirstOrDefault(i => i.ProductId == productId);
            if (item == null) return;

            var newQty = item.Quantity + delta;
            if (newQty <= 0)
            {
                await RemoveItemAsync(productId);
                return;
            }

            if (newQty > item.Stock)
            {
                ToastService.Instance.Warning($"库存不足，最多可购买 {item.Stock} 件");
                return;
            }

            var success = await _shopService.UpdateCartItemAsync(_userId, productId, newQty).ConfigureAwait(false);
            if (success)
            {
                item.Quantity = newQty;
                NotifyTotalsChanged();
                if (ShowSettlement) await RecalculateAsync();
            }
        }

        private async Task OpenSettlementAsync()
        {
            if (_userId == Guid.Empty || _items.Count == 0) return;

            var outOfStock = _items.Where(i => i.Quantity > i.Stock).ToList();
            if (outOfStock.Count > 0)
            {
                var names = string.Join("、", outOfStock.Select(i => i.ProductName));
                ToastService.Instance.Warning($"以下商品库存不足：{names}，请调整数量");
                return;
            }

            ShowSettlement = true;

            var addresses = await _shopService.GetUserAddressesAsync(_userId);
            Addresses = addresses != null
                ? new ObservableCollection<ShippingAddressInfo>(addresses)
                : new ObservableCollection<ShippingAddressInfo>();
            SelectedAddress = Addresses.FirstOrDefault(a => a.IsDefault) ?? Addresses.FirstOrDefault();
            OnPropertyChanged(nameof(HasAddresses));

            var coupons = await _merchantService.GetUserCouponsAsync(_userId);
            if (coupons != null)
            {
                var productTotal = ProductTotalAmount;
                AvailableCoupons = new ObservableCollection<CouponInfo>(
                    coupons.Where(c => c.Status == 0 && productTotal >= c.UseCondition));
                SelectedCoupon = AvailableCoupons.FirstOrDefault();
            }
            OnPropertyChanged(nameof(HasCoupons));

            await RecalculateAsync();
        }

        private async Task RecalculateAsync()
        {
            var productTotal = ProductTotalAmount;

            FreightAmount = 0;
            if (SelectedAddress != null && _items.Count > 0)
            {
                var firstMerchantId = _items.FirstOrDefault()?.MerchantId ?? 0;
                var templates = await _shopService.GetFreightTemplatesAsync(firstMerchantId);
                if (templates != null && templates.Count > 0)
                {
                    FreightAmount = templates.First().IsFree ? 0 : templates.First().FirstPrice;
                }
            }

            DiscountAmount = 0;
            if (SelectedCoupon != null)
            {
                if (productTotal >= SelectedCoupon.UseCondition)
                {
                    DiscountAmount = SelectedCoupon.CouponType == 0
                        ? SelectedCoupon.Denomination
                        : Math.Round(productTotal * (1 - SelectedCoupon.Denomination / 100), 2);
                    if (DiscountAmount > productTotal) DiscountAmount = productTotal;
                }
            }

            FullDiscountAmount = 0;
            if (_items.Count > 0)
            {
                var firstMerchantId = _items.FirstOrDefault()?.MerchantId ?? 0;
                var rules = await _merchantService.GetShopFullDiscountRulesAsync(firstMerchantId);
                if (rules != null)
                {
                    var activeRule = rules.FirstOrDefault(r => r.IsActive && productTotal >= r.LimitValue);
                    if (activeRule != null)
                    {
                        FullDiscountAmount = activeRule.DiscountValue;
                    }
                }
            }

            OrderTotalAmount = productTotal + FreightAmount - DiscountAmount - FullDiscountAmount;
            if (OrderTotalAmount < 0) OrderTotalAmount = 0;

            OnPropertyChanged(nameof(FreightDisplay));
            OnPropertyChanged(nameof(DiscountDisplay));
            OnPropertyChanged(nameof(FullDiscountDisplay));
            OnPropertyChanged(nameof(OrderTotalDisplay));
            OnPropertyChanged(nameof(ProductTotalDisplay));
        }

        private async Task CheckoutAsync()
        {
            if (_userId == Guid.Empty || _items.Count == 0) return;

            IsCheckingOut = true;
            OnPropertyChanged(nameof(CanCheckout));
            try
            {
                ToastService.Instance.Info("正在创建订单...");

                var orderId = await _orderService.CreateOrderAsync(
                    _userId, _items.ToList(), SelectedAddress,
                    FreightAmount, DiscountAmount, FullDiscountAmount).ConfigureAwait(false);

                if (orderId == null || orderId <= 0)
                {
                    ToastService.Instance.Error("创建订单失败，请稍后重试");
                    return;
                }

                await _shopService.ClearCartAsync().ConfigureAwait(false);
                _items.Clear();
                NotifyTotalsChanged();

                PendingOrderId = orderId.Value;
                PaymentAmount = OrderTotalAmount;
                ShowPaymentDialog = true;
            }
            catch (Exception)
            {
                ToastService.Instance.Error("结算异常，请稍后重试");
            }
            finally
            {
                IsCheckingOut = false;
                OnPropertyChanged(nameof(CanCheckout));
            }
        }

        public async Task<long?> QuickCheckoutAsync(CartItem item)
        {
            if (_userId == Guid.Empty || item == null) return null;

            try
            {
                ToastService.Instance.Info("正在快速下单...");

                var address = await _shopService.GetDefaultShippingAddressAsync(_userId);

                decimal freight = 0;
                if (address != null)
                {
                    var templates = await _shopService.GetFreightTemplatesAsync(item.MerchantId);
                    if (templates != null && templates.Count > 0)
                        freight = templates[0].IsFree ? 0 : templates[0].FirstPrice;
                }

                var orderId = await _orderService.CreateOrderAsync(
                    _userId, new List<CartItem> { item }, address, freight, 0, 0);

                if (orderId == null || orderId <= 0)
                {
                    ToastService.Instance.Error("创建订单失败");
                    return null;
                }

                ToastService.Instance.Info("订单已创建，正在支付...");

                var orderTotal = item.Price * item.Quantity + freight;
                var payResult = await _orderService.PayOrderAsync(orderId.Value, 1, orderTotal);

                if (payResult?.Success == true)
                {
                    ToastService.Instance.Success("快速结算成功！订单已创建");
                    await _shopService.RemoveFromCartAsync(_userId, item.ProductId);
                    await LoadCartAsync();
                }
                else
                {
                    ToastService.Instance.Warning(payResult?.ErrorMessage ?? "订单已创建但支付未完成，请在订单中心完成支付");
                }

                return orderId;
            }
            catch (Exception)
            {
                ToastService.Instance.Error("快速下单异常，请稍后重试");
                return null;
            }
        }

        private async Task ClearCartAsync()
        {
            if (_userId == Guid.Empty || _items.Count == 0) return;
            foreach (var item in _items.ToList())
                await _shopService.RemoveFromCartAsync(_userId, item.ProductId).ConfigureAwait(false);
            ToastService.Instance.Success("购物车已清空");
            await LoadCartAsync();
        }

        private void NotifyTotalsChanged()
        {
            OnPropertyChanged(nameof(ProductTotalAmount));
            OnPropertyChanged(nameof(TotalAmount));
            OnPropertyChanged(nameof(TotalCount));
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(CanCheckout));
            OnPropertyChanged(nameof(ProductTotalDisplay));
            OnPropertyChanged(nameof(IsAllSelected));
        }

        private async Task ConfirmPaymentAsync()
        {
            if (PendingOrderId == null || _userId == Guid.Empty) return;

            IsPaymentProcessing = true;
            try
            {
                var channelName = GetPaymentChannelName(SelectedPaymentChannel);
                ToastService.Instance.Info($"正在使用{channelName}支付...");

                var payResult = await _orderService.PayOrderAsync(PendingOrderId.Value, SelectedPaymentChannel, PaymentAmount).ConfigureAwait(false);

                if (payResult?.Success == true)
                {
                    var transactionId = payResult.TransactionId;
                    bool paid = false;
                    for (int i = 0; i < 20; i++)
                    {
                        await Task.Delay(3000).ConfigureAwait(false);
                        var statusResult = await _orderService.QueryPaymentStatusAsync(transactionId).ConfigureAwait(false);
                        if (statusResult?.Success == true && statusResult.Status == 1)
                        {
                            paid = true;
                            break;
                        }
                    }

                    if (paid)
                    {
                        PaymentResultMessage = "支付成功！";
                        ToastService.Instance.Success("支付成功！订单已完成");
                        ShowPaymentDialog = false;
                        ShowSettlement = false;
                        PendingOrderId = null;
                        await LoadCartAsync();
                    }
                    else
                    {
                        PaymentResultMessage = "支付结果确认中，请稍后在订单中心查看";
                        ToastService.Instance.Info(PaymentResultMessage);
                        ShowPaymentDialog = false;
                        ShowSettlement = false;
                        PendingOrderId = null;
                    }
                }
                else
                {
                    PaymentResultMessage = payResult?.ErrorMessage ?? "支付失败，请重试或更换支付方式";
                    ToastService.Instance.Warning(PaymentResultMessage);
                }
            }
            catch (Exception)
            {
                PaymentResultMessage = "支付异常，请稍后重试";
                ToastService.Instance.Error("支付异常，请稍后重试");
            }
            finally
            {
                IsPaymentProcessing = false;
            }
        }

        private void CancelPayment()
        {
            ShowPaymentDialog = false;
            PendingOrderId = null;
            PaymentResultMessage = null;
        }

        private static string GetPaymentChannelName(int channel)
        {
            return channel switch
            {
                0 => "微信支付",
                1 => "支付宝",
                _ => "未知渠道"
            };
        }
    }
}
