using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Horizon.Game.GengDi.Core.ViewModels;

namespace Horizon.Game.GengDi.Core.Views
{
    public partial class HomeView : UserControl
    {
        public HomeView()
        {
            InitializeComponent();
            DataContext = new HomeViewModel();
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
            var detailView = new FlowerSpeciesDetailView(1);
            if (this.FindAncestorOfType<ContentControl>() is { } contentControl)
            {
                contentControl.Content = detailView;
            }
        }
    }
}
