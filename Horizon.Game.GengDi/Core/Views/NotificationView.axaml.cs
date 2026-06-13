using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Horizon.Game.GengDi.Core.ViewModels;
using System;

namespace Horizon.Game.GengDi.Core.Views
{
    public partial class NotificationView : UserControl
    {
        public NotificationView()
        {
            InitializeComponent();
            DataContext = new NotificationViewModel();
            Loaded += NotificationView_Loaded;
        }

        private async void NotificationView_Loaded(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as NotificationViewModel;
            if (viewModel != null)
            {
                // 这里使用模拟的用户ID，实际应用中应该从登录状态获取
                await viewModel.LoadNotificationsAsync("user123");
            }
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}