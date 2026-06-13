using Avalonia.Controls;
using Avalonia.Interactivity;
using Horizon.Game.GengDi.Core.ViewModels;

namespace Horizon.Game.GengDi.Core.Views
{
    public partial class DownloadsView : UserControl
    {
        public DownloadsView()
        {
            InitializeComponent();
            DataContext = new DownloadsViewModel();
            Loaded += DownloadsView_Loaded;
        }

        private async void DownloadsView_Loaded(object sender, RoutedEventArgs e)
        {
            Loaded -= DownloadsView_Loaded;
            if (DataContext is DownloadsViewModel viewModel)
            {
                await viewModel.InitializeAsync();
            }
        }
    }
}