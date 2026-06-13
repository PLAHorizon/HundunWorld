using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;
using Horizon.Game.GengDi.Core.Controls;
using Horizon.Game.GengDi.Core.Services;

namespace Horizon.Game.GengDi.Core.ViewModels
{
    public class FlowerProfileViewModel : ViewModelBase
    {
        private readonly FlowerSubscriptionService _subscriptionService = new();
        private readonly FlowerMerchantService _merchantService = new();
        private readonly FlowerOrderService _orderService = new();

        private string _displayName = "";
        private string _phoneNumber = "";
        private string _subscriptionLevel = "Free";
        private DateTime? _expiryDate;
        private bool _isAutoRenew;
        private bool _isWebSocketEnabled = true;
        private bool _isSmsEnabled;
        private bool _isWeChatEnabled;
        private bool _isEmailEnabled = true;
        private bool _isLoading;
        private bool _isMerchant;
        private string _merchantShopName = "";
        private ObservableCollection<FrequentProductInfo> _frequentProducts = new();
        private Guid _userId;

        public string DisplayName
        {
            get => _displayName;
            set => SetProperty(ref _displayName, value);
        }

        public string PhoneNumber
        {
            get => _phoneNumber;
            set => SetProperty(ref _phoneNumber, value);
        }

        public string SubscriptionLevel
        {
            get => _subscriptionLevel;
            set
            {
                if (SetProperty(ref _subscriptionLevel, value))
                    OnPropertyChanged(nameof(SubscriptionLevelColor));
            }
        }

        public DateTime? ExpiryDate
        {
            get => _expiryDate;
            set => SetProperty(ref _expiryDate, value);
        }

        public bool IsAutoRenew
        {
            get => _isAutoRenew;
            set => SetProperty(ref _isAutoRenew, value);
        }

        public bool IsWebSocketEnabled
        {
            get => _isWebSocketEnabled;
            set => SetProperty(ref _isWebSocketEnabled, value);
        }

        public bool IsSmsEnabled
        {
            get => _isSmsEnabled;
            set => SetProperty(ref _isSmsEnabled, value);
        }

        public bool IsWeChatEnabled
        {
            get => _isWeChatEnabled;
            set => SetProperty(ref _isWeChatEnabled, value);
        }

        public bool IsEmailEnabled
        {
            get => _isEmailEnabled;
            set => SetProperty(ref _isEmailEnabled, value);
        }

        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        public bool IsMerchant
        {
            get => _isMerchant;
            set
            {
                if (SetProperty(ref _isMerchant, value))
                    OnPropertyChanged(nameof(MerchantToggleLabel));
            }
        }

        public string MerchantShopName
        {
            get => _merchantShopName;
            set => SetProperty(ref _merchantShopName, value);
        }

        public ObservableCollection<FrequentProductInfo> FrequentProducts
        {
            get => _frequentProducts;
            set => SetProperty(ref _frequentProducts, value);
        }

        public Guid UserId
        {
            get => _userId;
            set => SetProperty(ref _userId, value);
        }

        public string MerchantToggleLabel => IsMerchant ? "切换为买家视图" : "切换为商户视图";

        public string SubscriptionLevelColor => SubscriptionLevel switch
        {
            "Pro" => "#FFA726",
            "Enterprise" => "#AB47BC",
            _ => "#888888"
        };

        public ICommand UpgradeSubscriptionCommand { get; }
        public ICommand ToggleAutoRenewCommand { get; }
        public ICommand SaveNotificationSettingsCommand { get; }
        public ICommand ToggleMerchantViewCommand { get; }
        public ICommand LoadFrequentProductsCommand { get; }

        public FlowerProfileViewModel()
        {
            UpgradeSubscriptionCommand = new AsyncCommand(UpgradeSubscriptionAsync);
            ToggleAutoRenewCommand = new AsyncCommand(ToggleAutoRenewAsync);
            SaveNotificationSettingsCommand = new AsyncCommand(SaveNotificationSettingsAsync);
            ToggleMerchantViewCommand = new AsyncCommand(ToggleMerchantViewAsync);
            LoadFrequentProductsCommand = new AsyncCommand(LoadFrequentProductsAsync);

            _ = LoadProfileAsync();
        }

        private async Task LoadProfileAsync()
        {
            IsLoading = true;
            try
            {
                var user = App.CurrentUser;
                if (user != null)
                {
                    DisplayName = !string.IsNullOrWhiteSpace(user.Username) ? user.Username : user.PassportId ?? "";
                    PhoneNumber = !string.IsNullOrWhiteSpace(user.Email) ? MaskString(user.Email) : "";
                }

                var subTask = _subscriptionService.GetSubscriptionInfoAsync();
                var notifTask = _subscriptionService.GetNotificationSettingsAsync();
                var merchantTask = _merchantService.GetMyMerchantAsync();

                await Task.WhenAll(subTask, notifTask, merchantTask).ConfigureAwait(false);

                var subscription = await subTask;
                if (subscription != null)
                {
                    SubscriptionLevel = subscription.Level switch
                    {
                        1 => "Pro",
                        2 => "Enterprise",
                        _ => "Free"
                    };
                    ExpiryDate = subscription.EndDate;
                    IsAutoRenew = subscription.AutoRenew;
                }
                else
                {
                    SubscriptionLevel = "Free";
                    ExpiryDate = null;
                    IsAutoRenew = false;
                }

                var settings = await notifTask;
                if (settings != null)
                {
                    IsWebSocketEnabled = settings.IsWebSocketEnabled;
                    IsSmsEnabled = settings.IsSmsEnabled;
                    IsWeChatEnabled = settings.IsWeChatEnabled;
                    IsEmailEnabled = settings.IsEmailEnabled;
                }

                var merchant = await merchantTask;
                if (merchant != null && merchant.MerchantId > 0)
                {
                    IsMerchant = true;
                    MerchantShopName = merchant.ShopName;
                }
                else
                {
                    IsMerchant = false;
                    MerchantShopName = "";
                }
            }
            catch
            {
                SubscriptionLevel = "Free";
                ExpiryDate = null;
                IsAutoRenew = false;
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task UpgradeSubscriptionAsync()
        {
            var nextLevel = SubscriptionLevel switch
            {
                "Free" => "Pro",
                "Pro" => "Enterprise",
                _ => "Pro"
            };

            IsLoading = true;
            try
            {
                var result = await _subscriptionService.UpgradeSubscriptionAsync(nextLevel).ConfigureAwait(false);
                if (result != null)
                {
                    SubscriptionLevel = result.Level switch
                    {
                        1 => "Pro",
                        2 => "Enterprise",
                        _ => "Free"
                    };
                    ExpiryDate = result.EndDate;
                    IsAutoRenew = result.AutoRenew;
                    ToastService.Instance.Success($"已升级至{SubscriptionLevel}订阅");
                }
                else
                {
                    ToastService.Instance.Success("升级订阅失败，请稍后重试");
                }
            }
            catch
            {
                ToastService.Instance.Success("升级订阅失败，请稍后重试");
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task ToggleAutoRenewAsync()
        {
            var newAutoRenew = !IsAutoRenew;
            IsLoading = true;
            try
            {
                var success = await _subscriptionService.UpdateAutoRenewAsync(newAutoRenew).ConfigureAwait(false);
                if (success)
                {
                    IsAutoRenew = newAutoRenew;
                    var status = IsAutoRenew ? "已开启" : "已关闭";
                    ToastService.Instance.Success($"自动续费{status}");
                }
                else
                {
                    ToastService.Instance.Success("自动续费设置失败");
                }
            }
            catch
            {
                ToastService.Instance.Success("自动续费设置失败");
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task SaveNotificationSettingsAsync()
        {
            IsLoading = true;
            try
            {
                var request = new NotificationSettingsRequest
                {
                    IsWebSocketEnabled = IsWebSocketEnabled,
                    IsSmsEnabled = IsSmsEnabled,
                    IsWeChatEnabled = IsWeChatEnabled,
                    IsEmailEnabled = IsEmailEnabled
                };

                var success = await _subscriptionService.UpdateNotificationSettingsAsync(request).ConfigureAwait(false);
                if (success)
                {
                    ToastService.Instance.Success("通知设置已保存");
                }
                else
                {
                    ToastService.Instance.Success("保存通知设置失败");
                }
            }
            catch
            {
                ToastService.Instance.Success("保存通知设置失败");
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task ToggleMerchantViewAsync()
        {
            if (!IsMerchant)
            {
                var merchant = await _merchantService.GetMyMerchantAsync().ConfigureAwait(false);
                if (merchant == null || merchant.MerchantId <= 0)
                {
                    ToastService.Instance.Success("您尚未注册商户，请先在商户中心注册");
                    return;
                }

                IsMerchant = true;
                MerchantShopName = merchant.ShopName;
                ToastService.Instance.Success("已切换为商户视图");
            }
            else
            {
                IsMerchant = false;
                MerchantShopName = "";
                ToastService.Instance.Success("已切换为买家视图");
            }

            if (App.MainWindow?.Content is Views.MainView mainView && mainView.DataContext is MainViewModel mainVm)
            {
                mainVm.NavigateTo(IsMerchant ? "FlowerMerchant" : "FlowerShop");
            }
        }

        private async Task LoadFrequentProductsAsync()
        {
            if (_userId == Guid.Empty) return;
            try
            {
                var products = await _orderService.GetFrequentProductsAsync(_userId).ConfigureAwait(false);
                FrequentProducts = products != null
                    ? new ObservableCollection<FrequentProductInfo>(products)
                    : new ObservableCollection<FrequentProductInfo>();
            }
            catch
            {
                FrequentProducts = new ObservableCollection<FrequentProductInfo>();
            }
        }

        public void SetUserId(Guid userId)
        {
            _userId = userId;
            _ = LoadFrequentProductsAsync();
        }

        private static string MaskString(string input)
        {
            if (string.IsNullOrEmpty(input) || input.Length <= 4)
                return input;

            var prefix = input.Substring(0, 3);
            var suffix = input.Substring(input.Length - 4);
            return $"{prefix}****{suffix}";
        }
    }
}
