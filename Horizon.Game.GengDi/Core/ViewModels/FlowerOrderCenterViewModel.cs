using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Horizon.Game.GengDi.Core.Controls;
using Horizon.Game.GengDi.Core.Services;

namespace Horizon.Game.GengDi.Core.ViewModels
{
    public class FlowerOrderCenterViewModel : ViewModelBase
    {
        private readonly FlowerOrderService _orderService;
        private ObservableCollection<OrderListItem> _orders = new();
        private ObservableCollection<OrderListItem> _filteredOrders = new();
        private bool _isLoading;
        private int _selectedStatusFilter = -1;
        private Guid _userId;
        private string _statusMessage = "";
        private int _currentPage = 1;
        private bool _hasMoreOrders = true;
        private ObservableCollection<LogisticsTrackNode> _logisticsTrack = new();
        private long _selectedOrderId;
        private long _selectedRefundId;
        private string _returnExpressCompanyName = "";
        private string _returnShipOrderNumber = "";
        private bool _showLogisticsDialog;
        private bool _isLoadingLogistics;
        private LogisticsMapDataInfo _logisticsMapData;
        private bool _showPaymentDialog;
        private decimal _paymentAmount;
        private int _selectedPaymentChannel = 1;
        private bool _isPaymentProcessing;
        private string _paymentResultMessage = "";
        private long _pendingPayOrderId;
        private bool _usingMockData;

        public ObservableCollection<OrderListItem> FilteredOrders
        {
            get => _filteredOrders;
            set => SetProperty(ref _filteredOrders, value);
        }

        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        public bool IsEmpty => !_isLoading && _filteredOrders.Count == 0;

        public int SelectedStatusFilter
        {
            get => _selectedStatusFilter;
            set
            {
                if (SetProperty(ref _selectedStatusFilter, value))
                    ApplyFilter();
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

        public bool HasMoreOrders
        {
            get => _hasMoreOrders;
            set => SetProperty(ref _hasMoreOrders, value);
        }

        public ObservableCollection<LogisticsTrackNode> LogisticsTrack
        {
            get => _logisticsTrack;
            set => SetProperty(ref _logisticsTrack, value);
        }

        public long SelectedOrderId
        {
            get => _selectedOrderId;
            set => SetProperty(ref _selectedOrderId, value);
        }

        public long SelectedRefundId
        {
            get => _selectedRefundId;
            set => SetProperty(ref _selectedRefundId, value);
        }

        public string ReturnExpressCompanyName
        {
            get => _returnExpressCompanyName;
            set => SetProperty(ref _returnExpressCompanyName, value);
        }

        public string ReturnShipOrderNumber
        {
            get => _returnShipOrderNumber;
            set => SetProperty(ref _returnShipOrderNumber, value);
        }

        public bool ShowLogisticsDialog
        {
            get => _showLogisticsDialog;
            set => SetProperty(ref _showLogisticsDialog, value);
        }

        public bool IsLoadingLogistics
        {
            get => _isLoadingLogistics;
            set
            {
                if (SetProperty(ref _isLoadingLogistics, value))
                {
                    OnPropertyChanged(nameof(HasNoLogistics));
                    OnPropertyChanged(nameof(HasLogisticsNodes));
                }
            }
        }

        public LogisticsMapDataInfo LogisticsMapData
        {
            get => _logisticsMapData;
            set
            {
                if (SetProperty(ref _logisticsMapData, value))
                {
                    OnPropertyChanged(nameof(HasNoLogistics));
                    OnPropertyChanged(nameof(HasLogisticsNodes));
                }
            }
        }

        public bool HasNoLogistics => !IsLoadingLogistics && LogisticsMapData == null;

        public bool HasLogisticsNodes => !IsLoadingLogistics && LogisticsMapData?.Nodes?.Count > 0;

        public bool ShowPaymentDialog
        {
            get => _showPaymentDialog;
            set => SetProperty(ref _showPaymentDialog, value);
        }

        public decimal PaymentAmount
        {
            get => _paymentAmount;
            set => SetProperty(ref _paymentAmount, value);
        }

        public int SelectedPaymentChannel
        {
            get => _selectedPaymentChannel;
            set => SetProperty(ref _selectedPaymentChannel, value);
        }

        public bool IsPaymentProcessing
        {
            get => _isPaymentProcessing;
            set => SetProperty(ref _isPaymentProcessing, value);
        }

        public string PaymentResultMessage
        {
            get => _paymentResultMessage;
            set => SetProperty(ref _paymentResultMessage, value);
        }

        public long PendingPayOrderId
        {
            get => _pendingPayOrderId;
            set => SetProperty(ref _pendingPayOrderId, value);
        }

        public string PaymentChannelName => _selectedPaymentChannel switch
        {
            0 => "微信支付",
            1 => "支付宝",
            _ => "未知"
        };

        public ObservableCollection<StatusFilterItem> StatusFilters { get; }

        public ICommand RefreshCommand { get; }
        public ICommand LoadMoreCommand { get; }
        public ICommand PayOrderCommand { get; }
        public ICommand ConfirmDeliveryCommand { get; }
        public ICommand CompleteOrderCommand { get; }
        public ICommand CancelOrderCommand { get; }
        public ICommand RequestRefundCommand { get; }
        public ICommand SelectStatusFilterCommand { get; }
        public ICommand RepurchaseCommand { get; }
        public ICommand SubmitReturnShipmentCommand { get; }
        public ICommand LoadLogisticsTrackCommand { get; }
        public ICommand ViewLogisticsCommand { get; }
        public ICommand CloseLogisticsDialogCommand { get; }
        public ICommand ConfirmPaymentCommand { get; }
        public ICommand CancelPaymentCommand { get; }

        public FlowerOrderCenterViewModel()
        {
            _orderService = new FlowerOrderService();
            RefreshCommand = new AsyncCommand(LoadOrdersAsync);
            LoadMoreCommand = new AsyncCommand(LoadMoreAsync);
            PayOrderCommand = new AsyncCommand<long>(PayOrderAsync);
            ConfirmDeliveryCommand = new AsyncCommand<long>(ConfirmDeliveryAsync);
            CompleteOrderCommand = new AsyncCommand<long>(CompleteOrderAsync);
            CancelOrderCommand = new AsyncCommand<long>(CancelOrderAsync);
            RequestRefundCommand = new AsyncCommand<long>(RequestRefundAsync);
            SelectStatusFilterCommand = new RelayCommand<int>(s => SelectedStatusFilter = s);
            RepurchaseCommand = new AsyncCommand<long>(RepurchaseAsync);
            SubmitReturnShipmentCommand = new AsyncCommand(SubmitReturnShipmentAsync);
            LoadLogisticsTrackCommand = new AsyncCommand<long>(LoadLogisticsTrackAsync);
            ViewLogisticsCommand = new AsyncCommand<long>(ViewLogisticsAsync);
            CloseLogisticsDialogCommand = new RelayCommand(() => ShowLogisticsDialog = false);
            ConfirmPaymentCommand = new AsyncCommand(ConfirmPaymentAsync);
            CancelPaymentCommand = new RelayCommand(() => ShowPaymentDialog = false);

            StatusFilters = new ObservableCollection<StatusFilterItem>
            {
                new() { Status = -1, DisplayName = "全部" },
                new() { Status = 0, DisplayName = "待支付" },
                new() { Status = 1, DisplayName = "已支付" },
                new() { Status = 2, DisplayName = "已发货" },
                new() { Status = 3, DisplayName = "已签收" },
                new() { Status = 4, DisplayName = "已完成" },
                new() { Status = 5, DisplayName = "已取消" },
                new() { Status = 6, DisplayName = "退款中" }
            };

            LoadMockData();
        }

        /// <summary>
        /// 加载与设计原型一致的模拟订单数据，便于界面预览；
        /// 当真实用户身份就绪（SetUserId）后会被实际数据覆盖。
        /// </summary>
        private void LoadMockData()
        {
            _orders = new ObservableCollection<OrderListItem>
            {
                new OrderListItem
                {
                    OrderId = 20260726001L,
                    OrderNo = "ORD20260726001",
                    Status = 0,
                    TotalAmount = 204.00m,
                    OrderTotalAmount = 204.00m,
                    CreatedAt = new DateTime(2026, 7, 26, 9, 24, 18),
                    Items = new ObservableCollection<OrderItemDisplay>
                    {
                        new OrderItemDisplay { ProductName = "高原红玫瑰（20支/束）", Quantity = 2, Subtotal = 116.00m },
                        new OrderItemDisplay { ProductName = "晨曦混合花束（含康乃馨）", Quantity = 1, Subtotal = 88.00m }
                    }
                },
                new OrderListItem
                {
                    OrderId = 20260725014L,
                    OrderNo = "ORD20260725014",
                    Status = 2,
                    TotalAmount = 127.50m,
                    OrderTotalAmount = 127.50m,
                    CreatedAt = new DateTime(2026, 7, 25, 16, 42, 9),
                    ExpressCompanyName = "顺丰速运",
                    ShipOrderNumber = "SF1234567890",
                    Items = new ObservableCollection<OrderItemDisplay>
                    {
                        new OrderItemDisplay { ProductName = "雪山白百合（10支/束）", Quantity = 3, Subtotal = 127.50m }
                    }
                },
                new OrderListItem
                {
                    OrderId = 20260722008L,
                    OrderNo = "ORD20260722008",
                    Status = 4,
                    TotalAmount = 234.00m,
                    OrderTotalAmount = 234.00m,
                    CreatedAt = new DateTime(2026, 7, 22, 11, 8, 33),
                    Items = new ObservableCollection<OrderItemDisplay>
                    {
                        new OrderItemDisplay { ProductName = "晨曦混合花束（含康乃馨）", Quantity = 2, Subtotal = 176.00m },
                        new OrderItemDisplay { ProductName = "高原红玫瑰（20支/束）", Quantity = 1, Subtotal = 58.00m }
                    }
                }
            };
            _usingMockData = true;
            ApplyFilter();
        }

        public void SetUserId(Guid userId)
        {
            _userId = userId;
            if (_orders.Count == 0 || _usingMockData)
            {
                _usingMockData = false;
                _ = LoadOrdersAsync();
            }
        }

        internal async Task LoadOrdersAsync()
        {
            IsLoading = true;
            try
            {
                var orders = await _orderService.GetMyOrdersAsync(
                    _userId != Guid.Empty ? _userId : Guid.Empty, _currentPage, 20).ConfigureAwait(false);

                if (orders != null)
                {
                    var items = orders.Select(o => new OrderListItem
                    {
                        OrderId = o.OrderId,
                        OrderNo = o.OrderNo,
                        Status = o.Status,
                        TotalAmount = o.TotalAmount,
                        OrderTotalAmount = o.OrderTotalAmount,
                        CreatedAt = o.CreatedAt,
                        ExpressCompanyName = o.ExpressCompanyName ?? "",
                        ShipOrderNumber = o.ShipOrderNumber ?? "",
                        Items = new ObservableCollection<OrderItemDisplay>(
                            o.Items.Select(i => new OrderItemDisplay
                            {
                                ProductName = i.ProductName,
                                Quantity = i.Quantity,
                                Subtotal = i.Subtotal
                            }))
                    }).ToList();

                    if (_currentPage == 1)
                        _orders = new ObservableCollection<OrderListItem>(items);
                    else
                    {
                        foreach (var item in items)
                            _orders.Add(item);
                    }

                    HasMoreOrders = orders.Count >= 20;
                    ApplyFilter();
                }
                else
                {
                    if (_currentPage == 1)
                    {
                        _orders = new ObservableCollection<OrderListItem>();
                        FilteredOrders = new ObservableCollection<OrderListItem>();
                    }
                    HasMoreOrders = false;
                }
            }
            catch
            {
                HasMoreOrders = false;
            }
            finally
            {
                IsLoading = false;
                OnPropertyChanged(nameof(IsEmpty));
            }
        }

        private async Task LoadMoreAsync()
        {
            _currentPage++;
            await LoadOrdersAsync();
        }

        private void ApplyFilter()
        {
            var filtered = _selectedStatusFilter < 0
                ? _orders
                : _orders.Where(o => o.Status == _selectedStatusFilter);

            FilteredOrders = new ObservableCollection<OrderListItem>(filtered);

            foreach (var f in StatusFilters)
            {
                f.Count = f.Status < 0 ? _orders.Count : _orders.Count(o => o.Status == f.Status);
                f.IsSelected = f.Status == _selectedStatusFilter;
            }

            OnPropertyChanged(nameof(IsEmpty));
        }

        private Task PayOrderAsync(long orderId)
        {
            var order = _orders.FirstOrDefault(o => o.OrderId == orderId);
            PendingPayOrderId = orderId;
            PaymentAmount = order?.OrderTotalAmount ?? order?.TotalAmount ?? 0;
            ShowPaymentDialog = true;
            return Task.CompletedTask;
        }

        private async Task ConfirmPaymentAsync()
        {
            IsPaymentProcessing = true;
            try
            {
                var order = _orders.FirstOrDefault(o => o.OrderId == _pendingPayOrderId);
                var amount = order?.OrderTotalAmount ?? order?.TotalAmount ?? 0;
                var result = await _orderService.PayOrderAsync(_pendingPayOrderId, _selectedPaymentChannel, amount).ConfigureAwait(false);
                if (result?.Success == true)
                {
                    var transactionId = result.TransactionId;
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
                        ToastService.Instance.Success("支付成功");
                        ShowPaymentDialog = false;
                        await LoadOrdersAsync();
                    }
                    else
                    {
                        PaymentResultMessage = "支付结果确认中，请稍后在订单中心查看";
                        ToastService.Instance.Info(PaymentResultMessage);
                        ShowPaymentDialog = false;
                        await LoadOrdersAsync();
                    }
                }
                else
                {
                    PaymentResultMessage = result?.ErrorMessage ?? "支付失败，请重试";
                    ToastService.Instance.Error(PaymentResultMessage);
                }
            }
            finally
            {
                IsPaymentProcessing = false;
            }
        }

        private async Task ConfirmDeliveryAsync(long orderId)
        {
            var success = await _orderService.ConfirmDeliveryAsync(orderId).ConfigureAwait(false);
            if (success)
            {
                ToastService.Instance.Success("已确认收货");
                var order = _orders.FirstOrDefault(o => o.OrderId == orderId);
                if (order != null)
                {
                    order.Status = 3;
                    ApplyFilter();
                    OnPropertyChanged(nameof(IsEmpty));
                }
                else
                {
                    await LoadOrdersAsync();
                }
            }
            else ToastService.Instance.Error("操作失败，请重试");
        }

        private async Task CompleteOrderAsync(long orderId)
        {
            var success = await _orderService.CompleteOrderAsync(orderId).ConfigureAwait(false);
            if (success)
            {
                ToastService.Instance.Success("订单已完成");
                var order = _orders.FirstOrDefault(o => o.OrderId == orderId);
                if (order != null)
                {
                    order.Status = 4;
                    ApplyFilter();
                    OnPropertyChanged(nameof(IsEmpty));
                }
                else
                {
                    await LoadOrdersAsync();
                }
            }
            else ToastService.Instance.Error("操作失败，请重试");
        }

        private async Task CancelOrderAsync(long orderId)
        {
            var success = await _orderService.CancelOrderAsync(orderId).ConfigureAwait(false);
            if (success)
            {
                ToastService.Instance.Success("订单已取消");
                var order = _orders.FirstOrDefault(o => o.OrderId == orderId);
                if (order != null)
                {
                    order.Status = 5;
                    ApplyFilter();
                    OnPropertyChanged(nameof(IsEmpty));
                }
                else
                {
                    await LoadOrdersAsync();
                }
            }
            else ToastService.Instance.Error("取消失败，请重试");
        }

        private async Task RequestRefundAsync(long orderId)
        {
            var success = await _orderService.RequestRefundAsync(orderId, "买家申请退款").ConfigureAwait(false);
            if (success)
            {
                ToastService.Instance.Success("退款申请已提交");
                var order = _orders.FirstOrDefault(o => o.OrderId == orderId);
                if (order != null)
                {
                    order.Status = 6;
                    ApplyFilter();
                    OnPropertyChanged(nameof(IsEmpty));
                }
                else
                {
                    await LoadOrdersAsync();
                }
            }
            else ToastService.Instance.Error("申请失败，请重试");
        }

        private async Task RepurchaseAsync(long orderId)
        {
            var success = await _orderService.RepurchaseAsync(orderId).ConfigureAwait(false);
            if (success) { ToastService.Instance.Success("已加入购物车"); await LoadOrdersAsync(); }
            else ToastService.Instance.Error("再次购买失败，请重试");
        }

        private async Task SubmitReturnShipmentAsync()
        {
            if (_selectedRefundId <= 0)
            {
                ToastService.Instance.Warning("请选择退款单");
                return;
            }
            if (string.IsNullOrWhiteSpace(_returnExpressCompanyName) || string.IsNullOrWhiteSpace(_returnShipOrderNumber))
            {
                ToastService.Instance.Warning("请填写物流公司和运单号");
                return;
            }

            var success = await _orderService.SubmitReturnShipmentAsync(
                _selectedRefundId, _returnExpressCompanyName, _returnShipOrderNumber).ConfigureAwait(false);
            if (success)
            {
                ToastService.Instance.Success("退货物流已提交");
                ReturnExpressCompanyName = "";
                ReturnShipOrderNumber = "";
                await LoadOrdersAsync();
            }
            else
            {
                ToastService.Instance.Error("提交失败，请重试");
            }
        }

        private async Task LoadLogisticsTrackAsync(long orderId)
        {
            var track = await _orderService.GetLogisticsTrackAsync(orderId).ConfigureAwait(false);
            LogisticsTrack = track?.Tracks != null
                ? new ObservableCollection<LogisticsTrackNode>(track.Tracks)
                : new ObservableCollection<LogisticsTrackNode>();
        }

        private async Task ViewLogisticsAsync(long orderId)
        {
            ShowLogisticsDialog = true;
            IsLoadingLogistics = true;
            LogisticsMapData = null;
            try
            {
                var data = await _orderService.GetLogisticsMapDataCachedAsync(orderId).ConfigureAwait(false);
                LogisticsMapData = data;
            }
            catch
            {
                LogisticsMapData = null;
            }
            finally
            {
                IsLoadingLogistics = false;
            }
        }
    }

    public class OrderListItem
    {
        public long OrderId { get; set; }
        public string OrderNo { get; set; } = "";
        public int Status { get; set; }
        public string StatusText => Status switch
        {
            0 => "待支付",
            1 => "已支付",
            2 => "已发货",
            3 => "已签收",
            4 => "已完成",
            5 => "已取消",
            6 => "退款中",
            _ => "未知"
        };
        public string StatusColor => Status switch
        {
            0 => "#FFA726",
            1 => "#42A5F5",
            2 => "#66BB6A",
            3 => "#AB47BC",
            4 => "#78909C",
            5 => "#EF5350",
            6 => "#FF7043",
            _ => "#888888"
        };
        public bool IsStatusWarning => Status == 0 || Status == 6;
        public bool IsStatusInfo => Status == 1 || Status == 2;
        public bool IsStatusSuccess => Status == 3 || Status == 4;
        public bool IsStatusError => Status == 5;
        public int TotalItemCount => Items?.Sum(i => i.Quantity) ?? 0;
        public string FooterSummary => Status switch
        {
            2 => !string.IsNullOrEmpty(ShipOrderNumber)
                ? $"合计 {TotalItemCount} 件 · 物流单号 {ShipOrderNumber}"
                : $"合计 {TotalItemCount} 件 · 已发货",
            3 => $"合计 {TotalItemCount} 件 · 已签收",
            4 => $"合计 {TotalItemCount} 件 · 已完成",
            5 => $"合计 {TotalItemCount} 件 · 已取消",
            6 => $"合计 {TotalItemCount} 件 · 退款处理中",
            _ => $"合计 {TotalItemCount} 件 · 创建于 {CreatedAt:yyyy-MM-dd HH:mm}"
        };
        public decimal TotalAmount { get; set; }
        public decimal OrderTotalAmount { get; set; }
        public DateTime CreatedAt { get; set; }
        public string ActionText => Status switch
        {
            0 => "去支付",
            2 => "确认收货",
            3 => "确认完成",
            6 => "查看进度",
            _ => "查看详情"
        };
        public bool CanPay => Status == 0;
        public bool CanConfirmDelivery => Status == 2;
        public bool CanComplete => Status == 3;
        public bool CanCancel => Status == 0;
        public bool CanRefund => Status == 1 || Status == 2;
        public bool CanReview => Status == 4;
        public bool CanViewLogistics => Status == 2 || Status == 3;
        public string ExpressCompanyName { get; set; } = "";
        public string ShipOrderNumber { get; set; } = "";
        public ObservableCollection<OrderItemDisplay> Items { get; set; } = new();
    }

    public class OrderItemDisplay
    {
        public string ProductName { get; set; } = "";
        public int Quantity { get; set; }
        public decimal Subtotal { get; set; }
    }

    public class StatusFilterItem : ViewModelBase
    {
        private bool _isSelected;
        private int _count;

        public int Status { get; set; }
        public string DisplayName { get; set; } = "";

        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        public int Count
        {
            get => _count;
            set => SetProperty(ref _count, value);
        }
    }

    public class ReviewOrderViewModel : ViewModelBase
    {
        private readonly FlowerOrderService _orderService;
        private long _orderId;
        private Guid _userId;
        private long _shopId;
        private int _descriptionScore = 5;
        private int _serviceScore = 5;
        private int _logisticsScore = 5;
        private string _content = "";
        private bool _isAnonymous;
        private bool _isSubmitting;

        public long OrderId { get => _orderId; set => SetProperty(ref _orderId, value); }
        public Guid UserId { get => _userId; set => SetProperty(ref _userId, value); }
        public long ShopId { get => _shopId; set => SetProperty(ref _shopId, value); }
        public int DescriptionScore { get => _descriptionScore; set => SetProperty(ref _descriptionScore, value); }
        public int ServiceScore { get => _serviceScore; set => SetProperty(ref _serviceScore, value); }
        public int LogisticsScore { get => _logisticsScore; set => SetProperty(ref _logisticsScore, value); }
        public string Content { get => _content; set => SetProperty(ref _content, value); }
        public bool IsAnonymous { get => _isAnonymous; set => SetProperty(ref _isAnonymous, value); }
        public bool IsSubmitting { get => _isSubmitting; set => SetProperty(ref _isSubmitting, value); }
        public string ScoreDescription => DescriptionScore switch { 1 => "很差", 2 => "较差", 3 => "一般", 4 => "满意", 5 => "非常满意", _ => "" };
        public string ScoreService => ServiceScore switch { 1 => "很差", 2 => "较差", 3 => "一般", 4 => "满意", 5 => "非常满意", _ => "" };
        public string ScoreLogistics => LogisticsScore switch { 1 => "很差", 2 => "较差", 3 => "一般", 4 => "满意", 5 => "非常满意", _ => "" };

        public ReviewOrderViewModel()
        {
            _orderService = new FlowerOrderService();
        }

        public void Initialize(long orderId, Guid userId, long shopId)
        {
            OrderId = orderId;
            UserId = userId;
            ShopId = shopId;
        }

        public async Task<bool> SubmitReviewAsync()
        {
            if (IsSubmitting) return false;
            if (string.IsNullOrWhiteSpace(Content))
            {
                ToastService.Instance.Warning("请输入评价内容");
                return false;
            }

            IsSubmitting = true;
            try
            {
                var success = await _orderService.SubmitTradeCommentAsync(OrderId, UserId, ShopId, DescriptionScore, ServiceScore, LogisticsScore, Content, IsAnonymous);
                if (success)
                {
                    ToastService.Instance.Success("评价提交成功");
                    return true;
                }
                else
                {
                    ToastService.Instance.Error("评价提交失败");
                    return false;
                }
            }
            finally
            {
                IsSubmitting = false;
            }
        }
    }
}
