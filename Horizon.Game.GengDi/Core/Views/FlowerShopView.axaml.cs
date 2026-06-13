using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
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

        private void OnSpeciesFilterClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tag && int.TryParse(tag, out var speciesId))
            {
                if (DataContext is FlowerShopViewModel vm)
                    vm.SelectedSpeciesId = speciesId;

                UpdateFilterButtonStyle(btn);
            }
        }

        private static void UpdateFilterButtonStyle(Button selectedBtn)
        {
            if (selectedBtn.Parent is StackPanel panel)
            {
                foreach (var child in panel.Children)
                {
                    if (child is Button childBtn)
                    {
                        childBtn.Classes.Remove("PrimaryAction");
                        if (!childBtn.Classes.Contains("QuietAction"))
                            childBtn.Classes.Add("QuietAction");
                    }
                }
                selectedBtn.Classes.Remove("QuietAction");
                if (!selectedBtn.Classes.Contains("PrimaryAction"))
                    selectedBtn.Classes.Add("PrimaryAction");
            }
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
