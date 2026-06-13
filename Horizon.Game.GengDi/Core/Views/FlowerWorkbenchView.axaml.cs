using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Horizon.Game.GengDi.Core.ViewModels;

namespace Horizon.Game.GengDi.Core.Views
{
    public partial class FlowerWorkbenchView : UserControl
    {
        public FlowerWorkbenchView()
        {
            InitializeComponent();
            DataContext = new FlowerWorkbenchViewModel();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);
            if (DataContext is FlowerWorkbenchViewModel vm)
            {
                _ = vm.LoadWorkbenchAsync();
            }
        }

        private void ForecastQuadrant_Click(object sender, PointerPressedEventArgs e)
        {
            if (DataContext is FlowerWorkbenchViewModel vm)
                vm.NavigateToForecastCommand.Execute(null);
        }

        private void PlantingQuadrant_Click(object sender, PointerPressedEventArgs e)
        {
            if (DataContext is FlowerWorkbenchViewModel vm)
                vm.NavigateToPlantingCommand.Execute(null);
        }

        private void HarvestQuadrant_Click(object sender, PointerPressedEventArgs e)
        {
            if (DataContext is FlowerWorkbenchViewModel vm)
                vm.NavigateToHarvestCommand.Execute(null);
        }

        private void SalesQuadrant_Click(object sender, PointerPressedEventArgs e)
        {
            if (DataContext is FlowerWorkbenchViewModel vm)
                vm.NavigateToSalesCommand.Execute(null);
        }
    }
}
