using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Horizon.Game.GengDi.Core.Controls;
using Horizon.Game.GengDi.Core.Services;
using Horizon.Game.Message.Network;

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

        public ICommand RefreshCommand { get; }
        public ICommand MarkAsReadCommand { get; }
        public ICommand MarkAllReadCommand { get; }

        public FlowerAlertCenterViewModel()
        {
            _alertService = new FlowerAlertService();
            _marketService = new FlowerMarketService();
            RefreshCommand = new AsyncCommand(LoadAlertsAsync);
            MarkAsReadCommand = new AsyncCommand<AlertDisplayItem>(MarkAsReadAsync);
            MarkAllReadCommand = new AsyncCommand(MarkAllReadAsync);

            SpeciesFilters = new ObservableCollection<SpeciesFilterItem>(
                _speciesLookup.GetAllSpecies()
                    .Select(kv => new SpeciesFilterItem { SpeciesId = kv.Key, DisplayName = kv.Value })
                    .Prepend(new SpeciesFilterItem { SpeciesId = 0, DisplayName = "全部品种" })
            );

            _ = LoadAlertsAsync();
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
                        alerts.Select((a, i) => new AlertDisplayItem
                        {
                            AlertId = i + 1,
                            SpeciesId = a.SpeciesId,
                            AlertType = a.AlertType,
                            Message = a.Message,
                            TriggeredValue = a.TriggeredValue,
                            ThresholdValue = a.ThresholdValue,
                            IsRead = a.IsRead,
                            SpeciesName = GetSpeciesName(a.SpeciesId),
                            AlertTypeDisplay = GetAlertTypeDisplay(a.AlertType),
                            AlertLevel = GetAlertLevel(a.AlertType)
                        }));

                    PendingAlertCount = Alerts.Count(a => !a.IsRead);
                }
                else
                {
                    Alerts = new ObservableCollection<AlertDisplayItem>();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FlowerAlertCenterViewModel] {nameof(LoadAlertsAsync)}: {ex.Message}");
                Alerts = new ObservableCollection<AlertDisplayItem>();
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
            PendingAlertCount = Alerts.Count(a => !a.IsRead);
            ToastService.Instance.Success("已标记为已读");
            await Task.CompletedTask;
        }

        private async Task MarkAllReadAsync()
        {
            foreach (var alert in _alerts)
                alert.IsRead = true;
            PendingAlertCount = 0;
            ToastService.Instance.Success("已全部标记为已读");
            OnPropertyChanged(nameof(Alerts));
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
    }

    public class AlertDisplayItem : ViewModelBase
    {
        private bool _isRead;

        public int AlertId { get; set; }
        public long SpeciesId { get; set; }
        public AlertConditionType AlertType { get; set; }
        public string Message { get; set; } = "";
        public decimal TriggeredValue { get; set; }
        public decimal ThresholdValue { get; set; }
        public string SpeciesName { get; set; } = "";
        public string AlertTypeDisplay { get; set; } = "";
        public string AlertLevel { get; set; } = "info";

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
}
