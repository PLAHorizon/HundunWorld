using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Horizon.Game.GengDi.Core.Services;
using Horizon.Game.GengDi.Core.ViewModels;

namespace Horizon.Game.GengDi.Core.Views
{
    public partial class FlowerAddressView : UserControl
    {
        public FlowerAddressView()
        {
            InitializeComponent();
            DataContext = new FlowerAddressViewModel();
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            _ = InitializeOnAttachedAsync();
        }

        private async System.Threading.Tasks.Task InitializeOnAttachedAsync()
        {
            if (DataContext is FlowerAddressViewModel vm)
            {
                await vm.InitializeRegionsAsync();
                Debug.Print($"[FlowerAddressView] 区域数据初始化完成，省份数: {vm.Provinces.Count}");
            }
        }

       

        public void SetUserId(Guid userId)
        {
            if (DataContext is FlowerAddressViewModel vm)
                vm.SetUserId(userId);
        }

        private void OnAddAddressClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is FlowerAddressViewModel vm)
                vm.StartAddNew();
        }

        private void OnEditAddressClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is ShippingAddressInfo address)
            {
                if (DataContext is FlowerAddressViewModel vm)
                    vm.StartEdit(address);
            }
        }

        private void OnDeleteAddressClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is ShippingAddressInfo address)
            {
                if (DataContext is FlowerAddressViewModel vm)
                    _ = vm.DeleteAddressAsync(address.Id);
            }
        }

        private void OnSetDefaultClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is ShippingAddressInfo address)
            {
                if (DataContext is FlowerAddressViewModel vm)
                    _ = vm.SetDefaultAsync(address.Id);
            }
        }

        private async void OnMapSelectClick(object sender, RoutedEventArgs e)
        {
            var window = TopLevel.GetTopLevel(this) as Window;
            if (window == null) return;

            var dialog = new Window
            {
                Title = "选择地图服务",
                Width = 380,
                Height = 220,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Topmost = true
            };

            var panel = new StackPanel { Spacing = 16, Margin = new Thickness(24) };

            var titleText = new TextBlock
            {
                Text = "选择地图服务",
                FontSize = 15,
                FontWeight = FontWeight.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            panel.Children.Add(titleText);

            var descText = new TextBlock
            {
                Text = "请选择使用哪个地图服务来选取地址\n选点后请手动复制地址到详细地址栏",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.Parse("#999999")),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            };
            panel.Children.Add(descText);

            var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, HorizontalAlignment = HorizontalAlignment.Center };

            var baiduBtn = new Button
            {
                Content = "🔴 百度地图",
                Classes = { "PrimaryAction" },
                Padding = new Thickness(20, 8),
                CornerRadius = new CornerRadius(8),
                FontSize = 13
            };
            baiduBtn.Click += (s, ev) =>
            {
                dialog.Close();
                OpenUrl("https://api.map.baidu.com/lbsapi/creatmap/");
            };
            buttonPanel.Children.Add(baiduBtn);

            var gaodeBtn = new Button
            {
                Content = "🔵 高德地图",
                Classes = { "PrimaryAction" },
                Padding = new Thickness(20, 8),
                CornerRadius = new CornerRadius(8),
                FontSize = 13
            };
            gaodeBtn.Click += (s, ev) =>
            {
                dialog.Close();
                OpenUrl("https://lbs.amap.com/api/javascript-api/example/location/choose-location/");
            };
            buttonPanel.Children.Add(gaodeBtn);

            panel.Children.Add(buttonPanel);

            var cancelBtn = new Button
            {
                Content = "取消",
                Classes = { "QuietAction" },
                Padding = new Thickness(24, 8),
                CornerRadius = new CornerRadius(8),
                FontSize = 13,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 4, 0, 0)
            };
            cancelBtn.Click += (s, ev) => dialog.Close();
            panel.Children.Add(cancelBtn);

            dialog.Content = panel;
            await dialog.ShowDialog(window);
        }

        private static void OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch
            {
                // ignore
            }
        }

        private void OnCancelEditClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is FlowerAddressViewModel vm)
                vm.CancelEdit();
        }

        private void OnSaveEditClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is FlowerAddressViewModel vm)
                _ = vm.SaveAddressAsync();
        }
    }
}
