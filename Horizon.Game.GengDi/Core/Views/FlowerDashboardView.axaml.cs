using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Horizon.Game.GengDi.Core.ViewModels;

namespace Horizon.Game.GengDi.Core.Views
{
    public partial class FlowerDashboardView : UserControl
    {
        public FlowerDashboardView()
        {
            InitializeComponent();
            DataContext = new FlowerDashboardViewModel();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);
            if (DataContext is FlowerDashboardViewModel vm)
            {
                _ = vm.LoadDashboardAsync();
            }
        }
    }
}
