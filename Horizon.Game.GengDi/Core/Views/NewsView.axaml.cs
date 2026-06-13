using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Horizon.Game.GengDi.Core.ViewModels;
using System;

namespace Horizon.Game.GengDi.Core.Views
{
    public partial class NewsView : UserControl
    {
        public NewsView()
        {
            InitializeComponent();
            DataContext = new NewsViewModel();
            Loaded += NewsView_Loaded;
        }

        private async void NewsView_Loaded(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as NewsViewModel;
            if (viewModel != null)
            {
                await viewModel.LoadNewsAsync();
            }
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}