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
        private readonly AutoCompleteBox _searchBox;
        private readonly StackPanel _subNavBar;
        private readonly StackPanel _titleBarNavPanel;
        private readonly Button _userButton;

        public MainView()
        {
            InitializeComponent();
            _searchBox = this.FindControl<AutoCompleteBox>("SearchBox");
            _subNavBar = this.FindControl<StackPanel>("SubNavBar");
            _titleBarNavPanel = this.FindControl<StackPanel>("TitleBarNavPanel");
            _userButton = this.FindControl<Button>("UserButton");
            if (_searchBox != null)
            {
                _searchBox.KeyUp += SearchBox_KeyUp;
            }
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            AttachViewModel(DataContext as MainViewModel);
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
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
        }

        private void SearchBox_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter || DataContext is not MainViewModel vm)
            {
                return;
            }

            if (_searchBox?.SelectedItem is NavigationSearchItem selectedItem)
            {
                NavigateToSearchItem(selectedItem, vm);
            }
            else
            {
                var matchedItem = vm.FindSearchItem(_searchBox?.Text);
                if (matchedItem != null)
                {
                    NavigateToSearchItem(matchedItem, vm);
                }
            }

            e.Handled = true;
        }

        private void NavigateToSearchItem(NavigationSearchItem item, MainViewModel viewModel)
        {
            if (!viewModel.NavigateTo(item.Tag))
            {
                return;
            }

            if (_searchBox != null)
            {
                _searchBox.Text = string.Empty;
                _searchBox.SelectedItem = null;
            }
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

        private void ViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainViewModel.CurrentNavigationTag) && sender is MainViewModel viewModel)
            {
                UpdateSubNavSelection(viewModel.CurrentNavigationTag);
                UpdateTitleBarNavSelection(viewModel.CurrentNavigationTag);
                UpdateUserButtonSelection(viewModel.CurrentNavigationTag);
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
