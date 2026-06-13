using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml;
using Horizon.Game.GengDi.Core.ViewModels;

namespace Horizon.Game.GengDi.Core.Views
{
    public partial class FlowerProductDetailView : UserControl
    {
        public FlowerProductDetailView()
        {
            InitializeComponent();
            DataContext = new FlowerProductDetailViewModel();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public void Initialize(long productId, Guid userId)
        {
            if (DataContext is FlowerProductDetailViewModel vm)
                _ = vm.InitializeAsync(productId, userId);
        }

        private void OnDecreaseQuantity(object sender, RoutedEventArgs e)
        {
            if (DataContext is FlowerProductDetailViewModel vm && vm.SelectedQuantity > 1)
                vm.SelectedQuantity--;
        }

        private void OnIncreaseQuantity(object sender, RoutedEventArgs e)
        {
            if (DataContext is FlowerProductDetailViewModel vm && vm.SelectedQuantity < vm.Stock)
                vm.SelectedQuantity++;
        }

        private async void OnAddToCart(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn)
            {
                btn.IsEnabled = false;
                try
                {
                    if (DataContext is FlowerProductDetailViewModel vm)
                        await vm.AddToCartAsync();
                }
                finally
                {
                    btn.IsEnabled = true;
                }
            }
        }

        private async void OnBuyNow(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn)
            {
                btn.IsEnabled = false;
                try
                {
                    if (DataContext is FlowerProductDetailViewModel vm)
                    {
                        var success = await vm.BuyNowAsync();
                        if (success)
                        {
                            var mainView = this.FindLogicalAncestorOfType<MainView>();
                            var mainVm = mainView?.DataContext as MainViewModel;
                            mainVm?.NavigateToFlowerOrderCenter();
                        }
                    }
                }
                finally
                {
                    btn.IsEnabled = true;
                }
            }
        }

        private void OnCompareProductSelected(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox comboBox && comboBox.SelectedItem is CompareProductOption option)
            {
                if (DataContext is FlowerProductDetailViewModel vm)
                    vm.SelectedCompareProductId = option.ProductId;
            }
        }
    }
}
