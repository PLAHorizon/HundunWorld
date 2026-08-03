using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using FluentAvalonia.UI.Windowing;
using Horizon.Game.GengDi.Core.ViewModels;

namespace Horizon.Game.GengDi.Core.Views
{
    public partial class MainView : UserControl
    {
        private MainViewModel _viewModel;
        private readonly StackPanel _subNavBar;
        private readonly StackPanel _titleBarNavPanel;
        private readonly Button _userButton;
        private MiniPlayerViewModel _miniPlayerVm;
        private MusicPlayerViewModel _currentPlayerVm;

        public MainView()
        {
            InitializeComponent();
            _subNavBar = this.FindControl<StackPanel>("SubNavBar");
            _titleBarNavPanel = this.FindControl<StackPanel>("TitleBarNavPanel");
            _userButton = this.FindControl<Button>("UserButton");
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            AttachViewModel(DataContext as MainViewModel);
            AttachMiniPlayer();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            DetachMiniPlayer();
            DetachCurrentPlayer();
            DetachViewModel();
            base.OnDetachedFromVisualTree(e);
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);
            AttachViewModel(DataContext as MainViewModel);
        }

        protected override void OnLoaded(Avalonia.Interactivity.RoutedEventArgs e)
        {
            base.OnLoaded(e);

            if (VisualRoot is AppWindow appWindow)
            {
                var rightInset = this.FindControl<Border>("TitleBarRightInset");
                if (rightInset != null && appWindow.TitleBar != null)
                {
                    rightInset.Width = appWindow.TitleBar.RightInset;
                }
            }

            // 兜底：OnAttachedToVisualTree 阶段 FindControl 可能尚未就绪，此处确保迷你播放器事件已订阅
            AttachMiniPlayer();
        }



        private void AttachViewModel(MainViewModel viewModel)
        {
            if (ReferenceEquals(_viewModel, viewModel))
            {
                return;
            }

            DetachViewModel();
            _viewModel = viewModel;

            if (_viewModel == null)
            {
                return;
            }

            _viewModel.PropertyChanged += ViewModel_PropertyChanged;
            UpdateSubNavSelection(_viewModel.CurrentNavigationTag);
            UpdateTitleBarNavSelection(_viewModel.CurrentNavigationTag);
            UpdateUserButtonSelection(_viewModel.CurrentNavigationTag);
        }

        private void DetachViewModel()
        {
            if (_viewModel != null)
            {
                _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
                _viewModel = null;
            }
        }

        /// <summary>
        /// 订阅迷你播放器的展开事件，点击展开按钮时导航到独立音乐播放器页面。幂等：已订阅则跳过。
        /// </summary>
        private void AttachMiniPlayer()
        {
            if (_miniPlayerVm != null) return;
            var miniPlayer = this.FindControl<MiniPlayerView>("MiniPlayer");
            if (miniPlayer?.DataContext is MiniPlayerViewModel vm)
            {
                _miniPlayerVm = vm;
                _miniPlayerVm.ExpandRequested += MiniPlayer_ExpandRequested;
            }
        }

        private void DetachMiniPlayer()
        {
            if (_miniPlayerVm != null)
            {
                _miniPlayerVm.ExpandRequested -= MiniPlayer_ExpandRequested;
                _miniPlayerVm = null;
            }
        }

        private void MiniPlayer_ExpandRequested(object sender, EventArgs e)
        {
            _viewModel?.NavigateToMusicPlayer();
        }

        /// <summary>
        /// 订阅当前音乐播放器视图的关闭（返回）事件。CurrentView 切换到 MusicPlayerView 时绑定，切换走时解绑。
        /// </summary>
        private void AttachCurrentPlayer(MusicPlayerView view)
        {
            DetachCurrentPlayer();
            if (view?.DataContext is MusicPlayerViewModel vm)
            {
                _currentPlayerVm = vm;
                _currentPlayerVm.CloseRequested += Player_CloseRequested;
            }
        }

        private void DetachCurrentPlayer()
        {
            if (_currentPlayerVm != null)
            {
                _currentPlayerVm.CloseRequested -= Player_CloseRequested;
                _currentPlayerVm = null;
            }
        }

        private void Player_CloseRequested(object sender, EventArgs e)
        {
            _viewModel?.NavigateBackFromPlayer();
        }

        private void ViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (!(sender is MainViewModel viewModel)) return;

            if (e.PropertyName == nameof(MainViewModel.CurrentNavigationTag))
            {
                UpdateSubNavSelection(viewModel.CurrentNavigationTag);
                UpdateTitleBarNavSelection(viewModel.CurrentNavigationTag);
                UpdateUserButtonSelection(viewModel.CurrentNavigationTag);
            }
            else if (e.PropertyName == nameof(MainViewModel.CurrentView))
            {
                AttachCurrentPlayer(viewModel.CurrentView as MusicPlayerView);
            }
        }

        /// <summary>
        /// 根据当前导航标签更新副导航条的选中高亮状态
        /// </summary>
        private void UpdateSubNavSelection(string currentTag)
        {
            if (_subNavBar == null || string.IsNullOrWhiteSpace(currentTag))
            {
                return;
            }

            foreach (var child in _subNavBar.Children)
            {
                if (child is Button button)
                {
                    var tag = button.Tag as string;
                    var classes = button.Classes;
                    if (string.Equals(tag, currentTag, StringComparison.OrdinalIgnoreCase))
                    {
                        if (!classes.Contains("SubNavSelected"))
                        {
                            classes.Add("SubNavSelected");
                        }
                    }
                    else
                    {
                        classes.Remove("SubNavSelected");
                    }
                }
            }
        }

        /// <summary>
        /// 根据当前导航标签更新标题栏主导航项的选中高亮状态
        /// </summary>
        private void UpdateTitleBarNavSelection(string currentTag)
        {
            if (_titleBarNavPanel == null)
            {
                return;
            }

            var activeNavTag = GetTitleBarNavTag(currentTag);
            foreach (var child in _titleBarNavPanel.Children)
            {
                if (child is Button button && button.Classes.Contains("TitleBarNavItem"))
                {
                    var tag = button.Tag as string;
                    var classes = button.Classes;
                    if (!string.IsNullOrWhiteSpace(tag) && string.Equals(tag, activeNavTag, StringComparison.OrdinalIgnoreCase))
                    {
                        if (!classes.Contains("TitleBarNavSelected"))
                        {
                            classes.Add("TitleBarNavSelected");
                        }
                    }
                    else
                    {
                        classes.Remove("TitleBarNavSelected");
                    }
                }
            }
        }

        /// <summary>
        /// 将当前导航标签映射到对应的标题栏主导航项标签
        /// </summary>
        private static string GetTitleBarNavTag(string navigationTag)
        {
            return navigationTag?.ToUpperInvariant() switch
            {
                "GAMES" or "HOME" or "DOWNLOADS" => "Games",
                "NEWS" => "News",
                "FLOWERSHOP" or "FLOWERDASHBOARD" or "FLOWERCART" or "FLOWERORDERCENTER"
                    or "FLOWERALERTCENTER" or "FLOWERAIASSISTANT" or "FLOWERDATASCREEN"
                    or "FLOWERMERCHANT" or "FLOWERADDRESS" or "FLOWERPROFILE"
                    or "FLOWERPLANTINGADVICE" or "FLOWERSPECIESDETAIL" or "FLOWERPRODUCTDETAIL"
                    or "FLOWERWORKBENCH" => "FlowerShop",
                "MUSICDISCOVER" or "MUSICPLAYER" or "PLAYLISTMANAGE" or "MUSICSEARCH" or "MUSICSTORY" => "Music",
                _ => null
            };
        }

        /// <summary>
        /// 根据当前导航标签更新用户按钮的选中高亮状态（社交页激活时高亮）
        /// </summary>
        private void UpdateUserButtonSelection(string currentTag)
        {
            if (_userButton == null)
            {
                return;
            }

            var classes = _userButton.Classes;
            if (string.Equals(currentTag, "Social", StringComparison.OrdinalIgnoreCase))
            {
                if (!classes.Contains("TitleBarNavSelected"))
                {
                    classes.Add("TitleBarNavSelected");
                }
            }
            else
            {
                classes.Remove("TitleBarNavSelected");
            }
        }

        private void StoryOverlay_Tapped(object sender, Avalonia.Input.TappedEventArgs e)
        {
            MusicStoryViewModel.Instance.IsOpen = false;
        }
    }
}
