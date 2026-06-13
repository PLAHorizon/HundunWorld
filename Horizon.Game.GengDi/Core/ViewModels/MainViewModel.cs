using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Horizon.Game.GengDi.Core.Services;
using Horizon.Game.GengDi.Core.Views;
using Horizon.Game.GengDi.Tools.ExcelProcessor.Views;

namespace Horizon.Game.GengDi.Core.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        private readonly GameService _gameService;
        private readonly NavigationService _navigationService;
        private readonly FlowerSpeciesLookupService _speciesLookup = FlowerSpeciesLookupService.Instance;
        private readonly Dictionary<string, Func<Control>> _viewFactories;
        // SocialView 持有 IM 长连接，必须在同一会话内复用同一实例，
        // 否则每次导航都会销毁旧连接、建立新连接，导致聊天变成"邮箱消息"。
        private readonly SocialView _socialView;
        private SocialViewModel _socialViewModel;
        // 保存已订阅 PropertyChanged 的 User 实例，以便精确取消订阅（非 readonly，允许在用户切换时更新）
        private Models.User _subscribedUser;
        private AppThemePreference _themePreference;
        private Control _currentView;
        private string _currentNavigationTag;
        private string _gameStatus = "Ready";

        public MainViewModel(GameService gameService, NavigationService navigationService, string initialTag = "Home")
        {
            _gameService = gameService;
            _navigationService = navigationService;
            _socialView = new SocialView();
            _socialViewModel = _socialView.DataContext as SocialViewModel;
            if (_socialViewModel != null)
            {
                _socialViewModel.PropertyChanged += SocialViewModel_PropertyChanged;
            }

            // 订阅当前用户属性变更，以便头像等信息实时同步到标题栏
            _subscribedUser = App.CurrentUser;
            if (_subscribedUser != null)
                _subscribedUser.PropertyChanged += CurrentUser_PropertyChanged;

            _viewFactories = new Dictionary<string, Func<Control>>(StringComparer.OrdinalIgnoreCase)
            {
                ["Home"] = () => new HomeView(),
                ["Games"] = () => new GamesView(),
                ["News"] = () => new NewsView(),
                ["Social"] = () => _socialView,
                ["Downloads"] = () => new DownloadsView(),
                ["Notification"] = () => new NotificationView(),
                ["Profile"] = () => new ProfileView(),
                ["Security"] = () => new SecurityView(),
                ["Settings"] = () => new SettingsView(),
                ["ExcelProcessor"] = () => new ExcelProcessorView(),
                ["MusicDiscover"] = () => new MusicDiscoverView(),
                ["MusicPlayer"] = () => new MusicPlayerView(),
                ["PlaylistManage"] = () => new PlaylistManageView(),
                ["MusicSearch"] = () => new MusicSearchView(),
                //["MusicStory"] = () => new MusicStoryView(),
                ["FlowerShop"] = () => new FlowerShopView(),
                ["FlowerOrderCenter"] = () => new FlowerOrderCenterView(),
                ["FlowerAlertCenter"] = () => new FlowerAlertCenterView(),
                ["FlowerAIAssistant"] = () => new FlowerAIAssistantView(),
                ["FlowerCart"] = () => new FlowerCartView(),
                ["FlowerMerchant"] = () => new FlowerMerchantView(),
                ["FlowerProductDetail"] = () => new FlowerProductDetailView(),
                ["FlowerAddress"] = () => new FlowerAddressView(),
                ["FlowerSpeciesDetail"] = () => new FlowerSpeciesDetailView(),
                ["FlowerDashboard"] = () => new FlowerDashboardView(),
                ["FlowerWorkbench"] = () => new FlowerWorkbenchView(),
                ["FlowerPlantingAdvice"] = () => new FlowerPlantingAdviceView(),
                ["FlowerProfile"] = () => new FlowerProfileView(),
                ["FlowerDataScreen"] = () => new FlowerDataScreenView(),
            };

            SearchItems = new List<NavigationSearchItem>
            {
                new("Home", "主页", "总览与快速入口"),
                new("Games", "游戏", "游戏库、详情与安装"),
                new("News", "新闻", "内容流与公告"),
                new("Social", "社交", "好友、群组与聊天"),
                new("Downloads", "下载", "任务队列与历史"),
                new("Notification", "通知", "系统与业务提醒"),
                new("Profile", "个人设置", "资料与账户信息"),
                new("Security", "安全设置", "密码与账号安全"),
                new("Settings", "应用设置", "主题与下载目录"),
                new("ExcelProcessor", "Excel工具", "Excel 数据处理与合并"),
                new("MusicDiscover", "发现音乐", "推荐、排行榜与分类浏览"),
                new("PlaylistManage", "歌单", "创建、编辑与收藏歌单"),
                new("MusicSearch", "音乐搜索", "搜索歌曲、艺术家、专辑与歌单"),
                new("MusicPlayer", "播放器", "全屏播放与歌词"),
                new("MusicStory", "音乐故事", "探索音乐背后的创作故事"),
                new("FlowerShop", "花卉市场", "花卉品类、实时价格与行情走势"),
                new("FlowerOrderCenter", "花卉订单", "订单管理与交易记录"),
                new("FlowerAlertCenter", "花卉预警", "价格预警与市场异常提醒"),
                new("FlowerAIAssistant", "AI助手", "花卉市场智能分析与预测"),
                new("FlowerCart", "购物车", "花卉商品购物车与结算"),
                new("FlowerMerchant", "商家管理", "店铺管理、商品发布与订单处理"),
                new("FlowerDashboard", "行情仪表盘", "核心数据卡片、走势迷你图与实时预警"),
                new("FlowerWorkbench", "花卉工作台", "统一工作台整合行情、种植、采收与销售"),
                new("FlowerPlantingAdvice", "种植建议", "温室环境监测与种植优化建议"),
                new("FlowerProfile", "个人中心", "账户信息、订阅服务与消息设置"),
                new("FlowerDataScreen", "数据大屏", "实时交易看板、供需关系与区域热力图"),
                new("FlowerSpeciesDetail", "品种详情", "花卉品种详细信息与历史价格"),
                new("FlowerProductDetail", "商品详情", "花卉商品详情与购买选项"),
            };

            StartGameCommand = new RelayCommand(StartGame);
            PauseGameCommand = new RelayCommand(PauseGame);
            ResumeGameCommand = new RelayCommand(ResumeGame);
            EndGameCommand = new RelayCommand(EndGame);
            NavigateToHomeCommand = new RelayCommand(NavigateToHome);
            NavigateToGamesCommand = new RelayCommand(NavigateToGames);
            NavigateToNewsCommand = new RelayCommand(NavigateToNews);
            NavigateToNotificationCommand = new RelayCommand(NavigateToNotification);
            NavigateToDownloadsCommand = new RelayCommand(NavigateToDownloads);
            NavigateToSettingsCommand = new RelayCommand(NavigateToSettings);
            NavigateToProfileCommand = new RelayCommand(NavigateToProfile);
            NavigateToSecurityCommand = new RelayCommand(NavigateToSecurity);
            NavigateToSocialCommand = new RelayCommand(NavigateToSocial);
            NavigateToExcelProcessorCommand = new RelayCommand(NavigateToExcelProcessor);
            NavigateToMusicDiscoverCommand = new RelayCommand(NavigateToMusicDiscover);
            NavigateToMusicPlayerCommand = new RelayCommand(NavigateToMusicPlayer);
            NavigateToPlaylistManageCommand = new RelayCommand(NavigateToPlaylistManage);
            NavigateToMusicSearchCommand = new RelayCommand(NavigateToMusicSearch);
            NavigateToMusicStoryCommand = new RelayCommand(NavigateToMusicStory);
            NavigateToFlowerShopCommand = new RelayCommand(NavigateToFlowerShop);
            NavigateToFlowerOrderCenterCommand = new RelayCommand(NavigateToFlowerOrderCenter);
            NavigateToFlowerAlertCenterCommand = new RelayCommand(NavigateToFlowerAlertCenter);
            NavigateToFlowerAIAssistantCommand = new RelayCommand(NavigateToFlowerAIAssistant);
            NavigateToFlowerCartCommand = new RelayCommand(NavigateToFlowerCart);
            NavigateToFlowerMerchantCommand = new RelayCommand(NavigateToFlowerMerchant);
            NavigateToFlowerAddressCommand = new RelayCommand(NavigateToFlowerAddress);
            NavigateToFlowerDashboardCommand = new RelayCommand(NavigateToFlowerDashboard);
            NavigateToFlowerWorkbenchCommand = new RelayCommand(NavigateToFlowerWorkbench);
            NavigateToFlowerPlantingAdviceCommand = new RelayCommand(NavigateToFlowerPlantingAdvice);
            NavigateToFlowerProfileCommand = new RelayCommand(NavigateToFlowerProfile);
            NavigateToFlowerDataScreenCommand = new RelayCommand(NavigateToFlowerDataScreen);
            LogoutCommand = new RelayCommand(Logout);

            SetSystemThemeCommand = new RelayCommand(SetSystemTheme);
            SetLightThemeCommand = new RelayCommand(SetLightTheme);
            SetDarkThemeCommand = new RelayCommand(SetDarkTheme);

            App.CurrentUserChanged += OnAppCurrentUserChanged;
            NavigateTo(initialTag);
            _ = EnsureSocialBackgroundSessionAsync();
            _ = LoadThemePreferenceAsync();
        }

        public Control CurrentView
        {
            get => _currentView;
            set => SetProperty(ref _currentView, value);
        }

        public string CurrentNavigationTag
        {
            get => _currentNavigationTag;
            private set => SetProperty(ref _currentNavigationTag, value);
        }

        public IReadOnlyList<NavigationSearchItem> SearchItems { get; }

        /// <summary>
        /// 当前活动操作（下载 / 安装 / 更新）的聚合视图模型，供 <c>MainView</c> 底部进度条绑定。
        /// </summary>
        public ActiveOperationViewModel ActiveOperation => ActiveOperationViewModel.Instance;

        public string GameStatus
        {
            get => _gameStatus;
            set => SetProperty(ref _gameStatus, value);
        }

        /// <summary>
        /// 标题栏显示的用户名
        /// </summary>
        public string UserDisplayName =>
            App.CurrentUser?.Username ?? "访客";

        /// <summary>
        /// 标题栏显示的通行证号
        /// </summary>
        public string UserPassportId =>
            string.IsNullOrWhiteSpace(App.CurrentUser?.PassportId)
                ? string.Empty
                : App.CurrentUser.PassportId;

        /// <summary>
        /// 标题栏显示的用户头像首字母
        /// </summary>
        public string UserAvatarInitial =>
            App.CurrentUser?.AvatarInitial ?? "?";

        /// <summary>
        /// 标题栏显示的用户头像 Bitmap（异步加载完成后不为 null）。
        /// </summary>
        public Bitmap UserAvatarBitmap => App.CurrentUser?.AvatarBitmap;

        /// <summary>
        /// 用户头像 Bitmap 已就绪时为 true，可用于 AXAML IsVisible 绑定。
        /// </summary>
        public bool UserHasAvatarBitmap => UserAvatarBitmap != null;

        /// <summary>
        /// 标题栏显示的用户状态文本
        /// </summary>
        public string UserStatusText =>
            App.CurrentUser?.DisplayStatus ?? "离线";

        /// <summary>
        /// 当前待处理的入群邀请数。
        /// </summary>
        public int PendingSocialInviteCount =>
            (_socialViewModel?.PendingGroupInviteCount ?? 0) +
            (_socialViewModel?.PendingInviteApprovalCount ?? 0);

        /// <summary>
        /// 是否存在待处理的入群邀请。
        /// </summary>
        public bool HasPendingSocialInvites => PendingSocialInviteCount > 0;

        /// <summary>
        /// 标题栏社交入口的邀请徽标文案。
        /// </summary>
        public string SocialInviteBadgeText => PendingSocialInviteCount > 99 ? "99+" : PendingSocialInviteCount.ToString();

        /// <summary>
        /// 标题栏社交入口提示文字。
        /// </summary>
        public string SocialEntryToolTip => HasPendingSocialInvites
            ? $"社交（{PendingSocialInviteCount} 条待处理通知）"
            : "社交";

        public bool IsSystemThemeActive => _themePreference == AppThemePreference.System;
        public bool IsLightThemeActive => _themePreference == AppThemePreference.Light;
        public bool IsDarkThemeActive => _themePreference == AppThemePreference.Dark;

        public ICommand StartGameCommand { get; }
        public ICommand PauseGameCommand { get; }
        public ICommand ResumeGameCommand { get; }
        public ICommand EndGameCommand { get; }
        public ICommand NavigateToHomeCommand { get; }
        public ICommand NavigateToGamesCommand { get; }
        public ICommand NavigateToNewsCommand { get; }
        public ICommand NavigateToNotificationCommand { get; }
        public ICommand NavigateToDownloadsCommand { get; }
        public ICommand NavigateToSettingsCommand { get; }
        public ICommand NavigateToProfileCommand { get; }
        public ICommand NavigateToSecurityCommand { get; }
        public ICommand NavigateToSocialCommand { get; }
        public ICommand NavigateToExcelProcessorCommand { get; }
        public ICommand NavigateToMusicDiscoverCommand { get; }
        public ICommand NavigateToMusicPlayerCommand { get; }
        public ICommand NavigateToPlaylistManageCommand { get; }
        public ICommand NavigateToMusicSearchCommand { get; }
        public ICommand NavigateToMusicStoryCommand { get; }
        public ICommand NavigateToFlowerShopCommand { get; }
        public ICommand NavigateToFlowerOrderCenterCommand { get; }
        public ICommand NavigateToFlowerAlertCenterCommand { get; }
        public ICommand NavigateToFlowerAIAssistantCommand { get; }
        public ICommand NavigateToFlowerCartCommand { get; }
        public ICommand NavigateToFlowerMerchantCommand { get; }
        public ICommand NavigateToFlowerAddressCommand { get; }
        public ICommand NavigateToFlowerDashboardCommand { get; }
        public ICommand NavigateToFlowerWorkbenchCommand { get; }
        public ICommand NavigateToFlowerPlantingAdviceCommand { get; }
        public ICommand NavigateToFlowerProfileCommand { get; }
        public ICommand NavigateToFlowerDataScreenCommand { get; }
        public ICommand LogoutCommand { get; }
        public ICommand SetSystemThemeCommand { get; }
        public ICommand SetLightThemeCommand { get; }
        public ICommand SetDarkThemeCommand { get; }

        public bool NavigateTo(string tag)
        {
            if (!_viewFactories.TryGetValue(tag, out var factory))
            {
                return false;
            }

            CurrentNavigationTag = tag;
            CurrentView = factory();
            return true;
        }

        public NavigationSearchItem FindSearchItem(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return null;
            }

            return SearchItems.FirstOrDefault(item =>
                string.Equals(item.Title, query, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(item.Tag, query, StringComparison.OrdinalIgnoreCase));
        }

        public void StartGame()
        {
            _gameService.StartGame();
            GameStatus = "Running";
        }

        public void PauseGame()
        {
            _gameService.PauseGame();
            GameStatus = "Paused";
        }

        public void ResumeGame()
        {
            _gameService.ResumeGame();
            GameStatus = "Running";
        }

        public void EndGame()
        {
            _gameService.EndGame();
            GameStatus = "Ready";
        }

        public void NavigateToHome()
        {
            NavigateTo("Home");
        }

        public void NavigateToGames()
        {
            NavigateTo("Games");
        }

        public void NavigateToDownloads()
        {
            NavigateTo("Downloads");
        }

        public void NavigateToSettings()
        {
            NavigateTo("Settings");
        }

        public void NavigateToProfile()
        {
            NavigateTo("Profile");
        }

        public void NavigateToSecurity()
        {
            NavigateTo("Security");
        }

        public void NavigateToSocial()
        {
            NavigateTo("Social");
        }

        public void NavigateToNews()
        {
            NavigateTo("News");
        }

        public void NavigateToNotification()
        {
            NavigateTo("Notification");
        }

        public void NavigateToExcelProcessor()
        {
            NavigateTo("ExcelProcessor");
        }

        public void NavigateToMusicDiscover()
        {
            NavigateTo("MusicDiscover");
        }

        public void NavigateToMusicPlayer()
        {
            NavigateTo("MusicPlayer");
        }

        public void NavigateToPlaylistManage()
        {
            NavigateTo("PlaylistManage");
        }

        public void NavigateToMusicSearch()
        {
            NavigateTo("MusicSearch");
        }

        public void NavigateToMusicStory()
        {
            NavigateTo("MusicStory");
        }

        public void NavigateToFlowerShop()
        {
            NavigateTo("FlowerShop");
        }

        public void NavigateToFlowerOrderCenter()
        {
            NavigateTo("FlowerOrderCenter");
            if (CurrentView is FlowerOrderCenterView view && view.DataContext is FlowerOrderCenterViewModel vm)
            {
                vm.SetUserId(App.CurrentUser.UserId);
            }
        }

        public void NavigateToFlowerAlertCenter()
        {
            NavigateTo("FlowerAlertCenter");
        }

        public void NavigateToFlowerAIAssistant()
        {
            NavigateTo("FlowerAIAssistant");
        }

        public void NavigateToFlowerCart()
        {
            NavigateTo("FlowerCart");
            if (CurrentView is FlowerCartView view && view.DataContext is FlowerCartViewModel vm)
            {
               
                vm.SetUserId(App.CurrentUser.UserId);
            }
        }

        public void NavigateToFlowerMerchant()
        {
            NavigateTo("FlowerMerchant");
        }

        public void NavigateToFlowerAddress()
        {
            NavigateTo("FlowerAddress");
            if (CurrentView is FlowerAddressView view)
            {
                view.SetUserId(App.CurrentUser.UserId);
            }
        }

        public void NavigateToFlowerDashboard()
        {
            NavigateTo("FlowerDashboard");
        }

        public void NavigateToFlowerWorkbench()
        {
            NavigateTo("FlowerWorkbench");
        }

        public void NavigateToFlowerPlantingAdvice()
        {
            NavigateTo("FlowerPlantingAdvice");
        }

        public void NavigateToFlowerPlantingAdviceWithSpecies(int speciesId)
        {
            NavigateTo("FlowerPlantingAdvice");
            if (CurrentView is FlowerPlantingAdviceView view && view.DataContext is FlowerPlantingAdviceViewModel vm)
            {
                vm.SpeciesFilter = GetSpeciesName(speciesId);
                _ = vm.LoadAdviceForSpeciesAsync(speciesId);
            }
        }

        private string GetSpeciesName(int speciesId) => _speciesLookup.GetSpeciesName(speciesId);

        public void NavigateToFlowerProfile()
        {
            NavigateTo("FlowerProfile");
        }

        public void NavigateToFlowerDataScreen()
        {
            NavigateTo("FlowerDataScreen");
        }

        public async void Logout()
        {
            try
            {
                if (_subscribedUser != null)
                    _subscribedUser.PropertyChanged -= CurrentUser_PropertyChanged;
                await _socialView.ShutdownAsync();
                PreviewImageService.Instance.ClearCache();
                App.CurrentUser = null;
                NavigationService.Instance.NavigateToLogin();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MainViewModel] Logout error: {ex.Message}");
            }
        }

        public async void SetSystemTheme() => await SetThemePreferenceAsync(AppThemePreference.System);
        public async void SetLightTheme() => await SetThemePreferenceAsync(AppThemePreference.Light);
        public async void SetDarkTheme() => await SetThemePreferenceAsync(AppThemePreference.Dark);

        private async Task SetThemePreferenceAsync(AppThemePreference preference)
        {
            if (_themePreference == preference) return;

            _themePreference = preference;
            RaiseThemeStateChanged();

            var settingsService = AppSettingsService.Instance;
            var settings = await settingsService.LoadSettingsAsync();
            settings.ThemePreference = preference;
            await settingsService.SaveSettingsAsync(settings);
            await settingsService.ApplyThemePreferenceAsync();
        }

        private void RaiseThemeStateChanged()
        {
            OnPropertyChanged(nameof(IsSystemThemeActive));
            OnPropertyChanged(nameof(IsLightThemeActive));
            OnPropertyChanged(nameof(IsDarkThemeActive));
        }

        private async Task LoadThemePreferenceAsync()
        {
            var settings = await AppSettingsService.Instance.LoadSettingsAsync();
            _themePreference = settings.ThemePreference;
            RaiseThemeStateChanged();
        }

        private void CurrentUser_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (string.IsNullOrEmpty(e.PropertyName)
                || e.PropertyName == nameof(Models.User.Username)
                || e.PropertyName == nameof(Models.User.Status)
                || e.PropertyName == nameof(Models.User.Avatar)
                || e.PropertyName == nameof(Models.User.AvatarBitmap))
            {
                RaiseCurrentUserStateChanged();
            }
        }

        private void RaiseCurrentUserStateChanged()
        {
            OnPropertyChanged(nameof(UserDisplayName));
            OnPropertyChanged(nameof(UserPassportId));
            OnPropertyChanged(nameof(UserAvatarInitial));
            OnPropertyChanged(nameof(UserAvatarBitmap));
            OnPropertyChanged(nameof(UserHasAvatarBitmap));
            OnPropertyChanged(nameof(UserStatusText));
        }

        private void SocialViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (string.IsNullOrEmpty(e.PropertyName)
                || e.PropertyName == nameof(SocialViewModel.PendingGroupInviteCount)
                || e.PropertyName == nameof(SocialViewModel.HasPendingGroupInvites)
                || e.PropertyName == nameof(SocialViewModel.PendingInviteApprovalCount)
                || e.PropertyName == nameof(SocialViewModel.HasPendingInviteApprovals))
            {
                RaiseSocialInviteStateChanged();
            }
        }

        private void RaiseSocialInviteStateChanged()
        {
            OnPropertyChanged(nameof(PendingSocialInviteCount));
            OnPropertyChanged(nameof(HasPendingSocialInvites));
            OnPropertyChanged(nameof(SocialInviteBadgeText));
            OnPropertyChanged(nameof(SocialEntryToolTip));
        }

        private async Task EnsureSocialBackgroundSessionAsync()
        {
            try
            {
                await _socialView.EnsureBackgroundSessionAsync();
                _socialViewModel = _socialView.DataContext as SocialViewModel;
                if (_socialViewModel != null)
                {
                    _socialViewModel.PropertyChanged += SocialViewModel_PropertyChanged;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MainViewModel] 后台启动社交会话失败：{ex.Message}");
            }
        }

        /// <summary>
        /// 当 App.CurrentUser 被替换时，更新对用户对象的直接订阅，防止旧实例泄漏。
        /// </summary>
        private void OnAppCurrentUserChanged(object sender, EventArgs e)
        {
            if (_subscribedUser != null)
                _subscribedUser.PropertyChanged -= CurrentUser_PropertyChanged;

            _subscribedUser = App.CurrentUser;
            if (_subscribedUser != null)
                _subscribedUser.PropertyChanged += CurrentUser_PropertyChanged;

            RaiseCurrentUserStateChanged();
            RaiseSocialInviteStateChanged();

            _ = ReinitializeSocialForCurrentUserAsync();
        }

        private async Task ReinitializeSocialForCurrentUserAsync()
        {
            try
            {
                if (_socialViewModel == null)
                    return;

                var passportId = ImIdentity.ResolvePassportId(App.CurrentUser) ?? string.Empty;
                _socialViewModel.CurrentUserId = passportId;
                await _socialView.EnsureBackgroundSessionAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MainViewModel] 重新初始化社交会话失败：{ex.Message}");
            }
        }
    }

    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool> _canExecute;

        public RelayCommand(Action execute)
        {
            _execute = execute;
        }

        public RelayCommand(Action execute, Func<bool> canExecute)
        {
            _execute = execute;
            _canExecute = canExecute;
        }

        public event EventHandler CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object parameter)
        {
            return _canExecute?.Invoke() ?? true;
        }

        public void Execute(object parameter)
        {
            _execute();
        }
    }

    public class RelayCommand<T> : ICommand
    {
        private readonly Action<T> _execute;

        public RelayCommand(Action<T> execute)
        {
            _execute = execute;
        }

        public event EventHandler CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object parameter)
        {
            return true;
        }

        public void Execute(object parameter)
        {
            _execute((T)parameter);
        }
    }

    public sealed class NavigationSearchItem
    {
        public NavigationSearchItem(string tag, string title, string description)
        {
            Tag = tag;
            Title = title;
            Description = description;
        }

        public string Tag { get; }

        public string Title { get; }

        public string Description { get; }
    }
}
