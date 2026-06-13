using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Horizon.Game.GengDi.Core.ViewModels;

namespace Horizon.Game.GengDi.Core.Views
{
    public partial class FlowerDataScreenView : UserControl
    {
        public FlowerDataScreenView()
        {
            InitializeComponent();
            DataContext = new FlowerDataScreenViewModel();
            Unloaded += OnUnloaded;
        }

        private void OnUnloaded(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            (DataContext as FlowerDataScreenViewModel)?.Dispose();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
