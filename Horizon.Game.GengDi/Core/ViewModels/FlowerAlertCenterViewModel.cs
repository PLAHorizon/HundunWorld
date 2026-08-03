using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Horizon.Game.GengDi.Core.Controls;
using Horizon.Game.GengDi.Core.Helpers;
using Horizon.Game.GengDi.Core.Services;
using Horizon.Game.Message.Network;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;

namespace Horizon.Game.GengDi.Core.ViewModels
{
    public class FlowerAlertCenterViewModel : ViewModelBase
    {
        private readonly FlowerAlertService _alertService;
        private readonly FlowerMarketService _marketService;
        private readonly FlowerSpeciesLookupService _speciesLookup = FlowerSpeciesLookupService.Instance;
        private ObservableCollection<AlertDisplayItem> _alerts = new();
        private bool _isLoading;
        private int _pendingAlertCount;
        private int _selectedSpeciesFilter;
        private string _statusMessage = "";

        // 预警统计概览（参考原型：总预警/今日新增/已处理/待处理）
        private int _totalAlerts;
        private int _todayNewAlerts;
        private int _processedAlerts;
        private int _priceBreakthroughCount;
        private int _lowStockCount;
        private int _anomalyCount;
        private string _selectedAlertLevel = "全部";

        public ObservableCollection<AlertDisplayItem> Alerts
        {
            get => _alerts;
            set => SetProperty(ref _alerts, value);
        }

        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        public int PendingAlertCount
        {
            get => _pendingAlertCount;
            set => SetProperty(ref _pendingAlertCount, value);
        }

        /// <summary>待处理预警数（与 PendingAlertCount 同义，供概览卡组绑定）。</summary>
        public int PendingAlerts
        {
            get => _pendingAlertCount;
            set => SetProperty(ref _pendingAlertCount, value);
        }

        public bool IsEmpty => !_isLoading && _alerts.Count == 0;

        public int SelectedSpeciesFilter
        {
            get => _selectedSpeciesFilter;
            set
            {
                if (SetProperty(ref _selectedSpeciesFilter, value))
                    _ = LoadAlertsAsync();
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public ObservableCollection<SpeciesFilterItem> SpeciesFilters { get; }

        // ===== 预警统计概览属性 =====
        public int TotalAlerts
        {
            get => _totalAlerts;
            set => SetProperty(ref _totalAlerts, value);
        }

        public int TodayNewAlerts
        {
            get => _todayNewAlerts;
            set => SetProperty(ref _todayNewAlerts, value);
        }

        public int ProcessedAlerts
        {
            get => _processedAlerts;
            set => SetProperty(ref _processedAlerts, value);
        }

        public int PriceBreakthroughCount
        {
            get => _priceBreakthroughCount;
            set => SetProperty(ref _priceBreakthroughCount, value);
        }

        public int LowStockCount
        {
            get => _lowStockCount;
            set => SetProperty(ref _lowStockCount, value);
        }

        public int AnomalyCount
        {
            get => _anomalyCount;
            set => SetProperty(ref _anomalyCount, value);
        }

        public string SelectedAlertLevel
        {
            get => _selectedAlertLevel;
            set => SetProperty(ref _selectedAlertLevel, value);
        }

        // ===== LiveChartsCore 图表数据（预警趋势柱状图） =====
        private ISeries[] _alertTrendSeries = Array.Empty<ISeries>();
        public ISeries[] AlertTrendSeries
        {
            get => _alertTrendSeries;
            set => SetProperty(ref _alertTrendSeries, value);
        }

        private Axis[] _alertXAxes = new[] { new Axis() };
        public Axis[] AlertXAxes
        {
            get => _alertXAxes;
            set => SetProperty(ref _alertXAxes, value);
        }

        private Axis[] _alertYAxes = new[] { new Axis() };
        public Axis[] AlertYAxes
        {
            get => _alertYAxes;
            set => SetProperty(ref _alertYAxes, value);
        }

        /// <summary>预警等级筛选 Tab（全部/紧急/重要/一般/低）。</summary>
        public ObservableCollection<AlertTabItem> AlertTabs { get; }

        public ICommand RefreshCommand { get; }
        public ICommand MarkAsReadCommand { get; }
        public ICommand MarkAllReadCommand { get; }

        // ===== 新增命令 =====
        public ICommand ProcessAlertCommand { get; }
        public ICommand DismissAlertCommand { get; }
        public ICommand FilterAlertsCommand { get; }

        public FlowerAlertCenterViewModel()
        {
            _alertService = new FlowerAlertService();
            _marketService = new FlowerMarketService();
            RefreshCommand = new AsyncCommand(LoadAlertsAsync);
            MarkAsReadCommand = new AsyncCommand<AlertDisplayItem>(MarkAsReadAsync);
            MarkAllReadCommand = new AsyncCommand(MarkAllReadAsync);
            ProcessAlertCommand = new AsyncCommand<AlertDisplayItem>(ProcessAlertAsync);
            DismissAlertCommand = new AsyncCommand<AlertDisplayItem>(DismissAlertAsync);
            FilterAlertsCommand = new AsyncCommand<string>(FilterAlertsByLevelAsync);

            SpeciesFilters = new ObservableCollection<SpeciesFilterItem>(
                _speciesLookup.GetAllSpecies()
                    .Select(kv => new SpeciesFilterItem { SpeciesId = kv.Key, DisplayName = kv.Value })
                    .Prepend(new SpeciesFilterItem { SpeciesId = 0, DisplayName = "全部品种" })
            );

            // 预警等级筛选 Tab（模拟数据，参考原型 badge 数量）
            AlertTabs = new ObservableCollection<AlertTabItem>
            {
                new AlertTabItem { TabName = "全部", Count = 12, Level = "全部", IsActive = true },
                new AlertTabItem { TabName = "紧急", Count = 3, Level = "danger", IsActive = false },
                new AlertTabItem { TabName = "重要", Count = 4, Level = "warning", IsActive = false },
                new AlertTabItem { TabName = "一般", Count = 3, Level = "info", IsActive = false },
                new AlertTabItem { TabName = "低", Count = 2, Level = "low", IsActive = false },
            };

            // 模拟统计概览数据（参考原型：待处理5 / 价格突破2 / 库存不足2 / 异常波动1）
            TotalAlerts = 12;
            TodayNewAlerts = 5;
            ProcessedAlerts = 7;
            PendingAlertCount = 5;
            PriceBreakthroughCount = 2;
            LowStockCount = 2;
            AnomalyCount = 1;

            // 模拟预警列表数据（参考原型 alert-card，包含价格波动/库存不足/异常波动）
            InitMockAlerts();

            _ = LoadAlertsAsync();
        }

        /// <summary>
        /// 用模拟数据初始化预警列表（参考设计原型第 856-951 行的 5 条 alert-card）。
        /// </summary>
        private void InitMockAlerts()
        {
            Alerts = new ObservableCollection<AlertDisplayItem>
            {
                new AlertDisplayItem
                {
                    AlertId = 1,
                    SpeciesId = 1,
                    AlertType = AlertConditionType.PriceBelow,
                    AlertTypeDisplay = "价格突破",
                    AlertIcon = "📉",
                    AlertLevel = "danger",
                    SpeciesName = "红玫瑰",
                    Message = "红玫瑰价格跌破预警下限，建议关注补仓时机或调整采购策略。",
                    TriggeredValue = 8.20m,
                    ThresholdValue = 8.50m,
                    Timestamp = "14:32:08",
                    Status = "pending",
                    StatusText = "待处理",
                    IsProcessed = false,
                    IsRead = false,
                    TriggerLabel = "触发值",
                    ThresholdLabel = "阈值",
                    TriggeredValueDisplay = "¥8.20",
                    ThresholdValueDisplay = "¥8.50",
                    TriggeredValueColor = "#EF5350"
                },
                new AlertDisplayItem
                {
                    AlertId = 2,
                    SpeciesId = 2,
                    AlertType = AlertConditionType.PriceBelow,
                    AlertTypeDisplay = "库存不足",
                    AlertIcon = "📦",
                    AlertLevel = "warning",
                    SpeciesName = "百合",
                    Message = "百合商户库存低于安全水位，预计 2 小时内可能影响订单供应。",
                    TriggeredValue = 120m,
                    ThresholdValue = 500m,
                    Timestamp = "14:28:51",
                    Status = "pending",
                    StatusText = "待处理",
                    IsProcessed = false,
                    IsRead = false,
                    TriggerLabel = "当前库存",
                    ThresholdLabel = "安全",
                    TriggeredValueDisplay = "120",
                    ThresholdValueDisplay = "500",
                    TriggeredValueColor = "#FF9800"
                },
                new AlertDisplayItem
                {
                    AlertId = 3,
                    SpeciesId = 4,
                    AlertType = AlertConditionType.PriceChangeAbove,
                    AlertTypeDisplay = "异常波动",
                    AlertIcon = "📊",
                    AlertLevel = "danger",
                    SpeciesName = "混合花束",
                    Message = "混合花束短时涨幅超过 5%，疑似异常交易或刷单行为，请核查。",
                    TriggeredValue = 5.6m,
                    ThresholdValue = 5m,
                    Timestamp = "14:25:33",
                    Status = "pending",
                    StatusText = "待处理",
                    IsProcessed = false,
                    IsRead = false,
                    TriggerLabel = "波动幅度",
                    ThresholdLabel = "阈值",
                    TriggeredValueDisplay = "+5.6%",
                    ThresholdValueDisplay = "5%",
                    TriggeredValueColor = "#26A69A"
                },
                new AlertDisplayItem
                {
                    AlertId = 4,
                    SpeciesId = 3,
                    AlertType = AlertConditionType.PriceBelow,
                    AlertTypeDisplay = "库存不足",
                    AlertIcon = "📦",
                    AlertLevel = "warning",
                    SpeciesName = "康乃馨",
                    Message = "康乃馨部分商户库存告急，建议通知商户及时补货。",
                    TriggeredValue = 85m,
                    ThresholdValue = 300m,
                    Timestamp = "14:18:02",
                    Status = "pending",
                    StatusText = "待处理",
                    IsProcessed = false,
                    IsRead = false,
                    TriggerLabel = "当前库存",
                    ThresholdLabel = "安全",
                    TriggeredValueDisplay = "85",
                    ThresholdValueDisplay = "300",
                    TriggeredValueColor = "#FF9800"
                },
                new AlertDisplayItem
                {
                    AlertId = 5,
                    SpeciesId = 5,
                    AlertType = AlertConditionType.PriceAbove,
                    AlertTypeDisplay = "价格突破",
                    AlertIcon = "📈",
                    AlertLevel = "danger",
                    SpeciesName = "绿植",
                    Message = "绿植价格突破预警上限，建议关注高位出货时机。",
                    TriggeredValue = 18.80m,
                    ThresholdValue = 18.50m,
                    Timestamp = "14:10:47",
                    Status = "pending",
                    StatusText = "待处理",
                    IsProcessed = false,
                    IsRead = false,
                    TriggerLabel = "触发值",
                    ThresholdLabel = "阈值",
                    TriggeredValueDisplay = "¥18.80",
                    ThresholdValueDisplay = "¥18.50",
                    TriggeredValueColor = "#26A69A"
                }
            };
            PendingAlertCount = Alerts.Count;

            // 初始化 LiveChartsCore 预警趋势图表数据（近 7 日预警分布柱状图）
            AlertTrendSeries = FlowerChartHelper.CreateAlertTrendSeries();
            AlertXAxes = FlowerChartHelper.CreateLabelAxis(FlowerChartHelper.AlertTrendLabels);
            AlertYAxes = FlowerChartHelper.CreateValueAxis();
        }

        private async Task LoadAlertsAsync()
        {
            IsLoading = true;
            try
            {
                var overview = await _marketService.GetMarketOverviewAsync().ConfigureAwait(false);
                if (overview != null)
                {
                    PendingAlertCount = overview.AlertCount;
                }

                var alerts = await _alertService.GetAlertsAsync(
                    _selectedSpeciesFilter, 0, 50).ConfigureAwait(false);

                if (alerts != null && alerts.Count > 0)
                {
                    Alerts = new ObservableCollection<AlertDisplayItem>(
                        alerts.Select((a, i) =>
                        {
                            var display = GetAlertTypeDisplay(a.AlertType);
                            var item = new AlertDisplayItem
                            {
                                AlertId = i + 1,
                                SpeciesId = a.SpeciesId,
                                AlertType = a.AlertType,
                                Message = a.Message,
                                TriggeredValue = a.TriggeredValue,
                                ThresholdValue = a.ThresholdValue,
                                IsRead = a.IsRead,
                                SpeciesName = GetSpeciesName(a.SpeciesId),
                                AlertTypeDisplay = display,
                                AlertLevel = GetAlertLevel(a.AlertType),
                                AlertIcon = GetAlertIcon(a.AlertType),
                                Timestamp = DateTime.Now.AddMinutes(-i * 7).ToString("HH:mm:ss"),
                                Status = a.IsRead ? "processed" : "pending",
                                StatusText = a.IsRead ? "已处理" : "待处理",
                                IsProcessed = a.IsRead
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

                    PendingAlertCount = Alerts.Count(a => !a.IsRead);
                    TotalAlerts = Alerts.Count;
                    TodayNewAlerts = Alerts.Count;
                    ProcessedAlerts = Alerts.Count(a => a.IsRead);
                }
                else
                {
                    // 服务无数据时保留已有模拟数据，避免界面空白
                    OnPropertyChanged(nameof(IsEmpty));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FlowerAlertCenterViewModel] {nameof(LoadAlertsAsync)}: {ex.Message}");
                // 异常时保留模拟数据
            }
            finally
            {
                IsLoading = false;
                OnPropertyChanged(nameof(IsEmpty));
            }
        }

        private async Task MarkAsReadAsync(AlertDisplayItem alert)
        {
            if (alert == null || alert.IsRead) return;
            alert.IsRead = true;
            alert.Status = "processed";
            alert.StatusText = "已处理";
            alert.IsProcessed = true;
            PendingAlertCount = Alerts.Count(a => !a.IsRead);
            ProcessedAlerts = Alerts.Count(a => a.IsRead);
            ToastService.Instance.Success("已标记为已读");
            await Task.CompletedTask;
        }

        private async Task MarkAllReadAsync()
        {
            foreach (var alert in _alerts)
            {
                alert.IsRead = true;
                alert.Status = "processed";
                alert.StatusText = "已处理";
                alert.IsProcessed = true;
            }
            PendingAlertCount = 0;
            ProcessedAlerts = _alerts.Count;
            ToastService.Instance.Success("已全部标记为已读");
            OnPropertyChanged(nameof(Alerts));
            await Task.CompletedTask;
        }

        /// <summary>处理单条预警（标记为已处理）。</summary>
        private async Task ProcessAlertAsync(AlertDisplayItem alert)
        {
            if (alert == null) return;
            alert.IsProcessed = true;
            alert.Status = "processed";
            alert.StatusText = "已处理";
            alert.IsRead = true;
            PendingAlertCount = Alerts.Count(a => !a.IsRead);
            ProcessedAlerts = Alerts.Count(a => a.IsRead);
            ToastService.Instance.Success("预警已处理");
            await Task.CompletedTask;
        }

        /// <summary>忽略并移除单条预警。</summary>
        private async Task DismissAlertAsync(AlertDisplayItem alert)
        {
            if (alert == null) return;
            Alerts.Remove(alert);
            PendingAlertCount = Alerts.Count(a => !a.IsRead);
            TotalAlerts = Alerts.Count;
            ToastService.Instance.Success("预警已忽略");
            await Task.CompletedTask;
        }

        /// <summary>按预警等级筛选（全部/紧急/重要/一般/低）。</summary>
        private async Task FilterAlertsByLevelAsync(string level)
        {
            SelectedAlertLevel = string.IsNullOrEmpty(level) ? "全部" : level;
            foreach (var tab in AlertTabs)
                tab.IsActive = tab.Level == SelectedAlertLevel;
            await Task.CompletedTask;
        }

        private string GetSpeciesName(long speciesId) => _speciesLookup.GetSpeciesName((int)speciesId);

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

        private static string GetAlertIcon(AlertConditionType alertType) => alertType switch
        {
            AlertConditionType.PriceAbove => "📈",
            AlertConditionType.PriceBelow => "📉",
            AlertConditionType.PriceChangeAbove => "📊",
            AlertConditionType.PriceChangeBelow => "📊",
            _ => "🔔"
        };
    }

    /// <summary>预警列表展示项（对应原型 alert-card）。</summary>
    public class AlertDisplayItem : ViewModelBase
    {
        private bool _isRead;
        private bool _isProcessed;
        private string _statusText = "待处理";

        public int AlertId { get; set; }
        public long SpeciesId { get; set; }
        public AlertConditionType AlertType { get; set; }
        public string Message { get; set; } = "";
        public decimal TriggeredValue { get; set; }
        public decimal ThresholdValue { get; set; }
        public string SpeciesName { get; set; } = "";
        public string AlertTypeDisplay { get; set; } = "";
        public string AlertLevel { get; set; } = "info";

        /// <summary>业务图标（价格突破📉/库存不足📦/异常波动📊）。</summary>
        public string AlertIcon { get; set; } = "🔔";

        /// <summary>触发时间（如 14:32:08）。</summary>
        public string Timestamp { get; set; } = "";

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

        /// <summary>状态码（pending/processed）。</summary>
        public string Status { get; set; } = "pending";

        /// <summary>状态文字（待处理/已处理）。</summary>
        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        /// <summary>是否已处理。</summary>
        public bool IsProcessed
        {
            get => _isProcessed;
            set => SetProperty(ref _isProcessed, value);
        }

        public bool IsRead
        {
            get => _isRead;
            set => SetProperty(ref _isRead, value);
        }

        public string LevelIcon => AlertLevel switch
        {
            "danger" => "🔴",
            "warning" => "🟡",
            _ => "🔵"
        };

        public string ReadIcon => IsRead ? "✓" : "●";
    }

    /// <summary>预警等级筛选 Tab 项（全部/紧急/重要/一般/低，带数量 badge）。</summary>
    public class AlertTabItem : ViewModelBase
    {
        private bool _isActive;

        public string TabName { get; set; } = "";
        public int Count { get; set; }
        public string Level { get; set; } = "";

        public bool IsActive
        {
            get => _isActive;
            set => SetProperty(ref _isActive, value);
        }
    }
}
