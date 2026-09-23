using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml;
using Horizon.Game.GengDi.Core.ViewModels;
using Horizon.Game.Message.Network;

namespace Horizon.Game.GengDi.Core.Views
{
    public partial class FlowerShopView : UserControl
    {
        public FlowerShopView()
        {
            InitializeComponent();
            DataContext = new FlowerShopViewModel();

            if (DataContext is FlowerShopViewModel vm)
                vm.ViewProductRequested += OnViewProductRequested;
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        /// <summary>页头「购物车 · N 件」胶囊 → 跳转购物车页。</summary>
        private void OnCartClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            var mainView = this.FindLogicalAncestorOfType<MainView>();
            if (mainView?.DataContext is MainViewModel mainVm)
                mainVm.NavigateTo("FlowerCart");
        }

        /// <summary>收藏按钮：阻断事件冒泡，避免触发卡片 Tapped 跳转详情。</summary>
        private void OnFavoriteTapped(object? sender, TappedEventArgs e)
        {
            e.Handled = true;
        }

        /// <summary>
        /// 卡片内交互控件（加入购物车 / 批量勾选 / 数量框）通用处理：
        /// 阻断 Tapped 冒泡，避免点击这些控件时被卡片的 OnProductTapped 捕获而跳转详情页。
        /// </summary>
        private void OnInnerControlTapped(object? sender, TappedEventArgs e)
        {
            e.Handled = true;
        }

        private void OnProductTapped(object sender, TappedEventArgs e)
        {
            if (sender is Border border && border.DataContext is ShopProductItem product)
            {
                NavigateToProductDetail(product);
            }
        }

        private void OnViewProductRequested(object sender, ShopProductItem product)
        {
            NavigateToProductDetail(product);
        }

        private void NavigateToProductDetail(ShopProductItem product)
        {
            var mainView = this.FindLogicalAncestorOfType<MainView>();
            var mainVm = mainView?.DataContext as MainViewModel;
            if (mainVm != null)
            {
                var userId = Guid.Empty;
                if (App.CurrentUser != null && Guid.TryParse(App.CurrentUser.PassportId, out var uid))
                    userId = uid;
                var view = new FlowerProductDetailView();
                view.DataContext = new FlowerProductDetailViewModel();
                view.Initialize(product.ProductId, userId);
                mainVm.CurrentView = view;
            }
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (DataContext is not FlowerShopViewModel vm) return;

            if (e.Key == Key.Up)
            {
                vm.NavigateUpCommand.Execute(null);
                e.Handled = true;
            }
            else if (e.Key == Key.Down)
            {
                vm.NavigateDownCommand.Execute(null);
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                vm.ViewSelectedProductCommand.Execute(null);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                vm.SelectedProductIndex = -1;
                e.Handled = true;
            }
            else if (e.Key == Key.K && e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                var searchBox = this.FindControl<TextBox>("SearchBox");
                searchBox?.Focus();
                e.Handled = true;
            }
        }
    }
}
