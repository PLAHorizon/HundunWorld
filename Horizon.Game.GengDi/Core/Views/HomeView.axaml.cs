using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Horizon.Game.GengDi.Core.ViewModels;

namespace Horizon.Game.GengDi.Core.Views
{
    public partial class HomeView : UserControl
    {
        public HomeView()
        {
            InitializeComponent();
            var vm = new HomeViewModel();
            // 导航回调：快速访问等入口统一转发到顶层 MainViewModel，纳入全局返回/前进历史
            vm.NavigationRequested = NavigateInShell;
            DataContext = vm;
        }

        /// <summary>
        /// 通过主壳的 MainViewModel 执行 tag 导航（走 NavigateTo 历史记录）。
        /// </summary>
        private void NavigateInShell(string tag)
        {
            if (this.FindLogicalAncestorOfType<MainView>()?.DataContext is MainViewModel mainVm)
            {
                mainVm.NavigateTo(tag);
            }
        }

        private void OnWeatherOverlayTapped(object? sender, TappedEventArgs e)
        {
            if (DataContext is HomeViewModel vm)
            {
                vm.IsWeatherDetailOpen = false;
            }
        }

        private void OnDishOverlayTapped(object? sender, TappedEventArgs e)
        {
            if (DataContext is HomeViewModel vm)
            {
                vm.IsDishDetailOpen = false;
            }
        }

        private void OnHourlyOverlayTapped(object? sender, TappedEventArgs e)
        {
            if (DataContext is HomeViewModel vm)
            {
                vm.IsHourlyDetailOpen = false;
            }
        }

        private void OnAirQualityOverlayTapped(object? sender, TappedEventArgs e)
        {
            if (DataContext is HomeViewModel vm)
            {
                vm.IsAirQualityDetailOpen = false;
            }
        }

        private void OnLifeIndexOverlayTapped(object? sender, TappedEventArgs e)
        {
            if (DataContext is HomeViewModel vm)
            {
                vm.IsLifeIndexDetailOpen = false;
            }
        }

        private void OnNewsOverlayTapped(object? sender, TappedEventArgs e)
        {
            if (DataContext is HomeViewModel vm)
            {
                vm.IsNewsDetailOpen = false;
            }
        }

        private void OnFlowerSpeciesDetailTapped(object? sender, TappedEventArgs e)
        {
            // 走 MainViewModel 导航，纳入全局返回/前进历史（原实现直接替换 ContentControl 内容，绕过了导航栈）
            var mainView = this.FindLogicalAncestorOfType<MainView>();
            if (mainView?.DataContext is MainViewModel mainVm)
            {
                mainVm.NavigateTo("FlowerSpeciesDetail");
                return;
            }

            // 兜底：脱离主壳宿主时保留旧行为
            var detailView = new FlowerSpeciesDetailView(1);
            if (this.FindAncestorOfType<ContentControl>() is { } contentControl)
            {
                contentControl.Content = detailView;
            }
        }
    }
}
