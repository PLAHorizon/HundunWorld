using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Horizon.Game.GengDi.Core.ViewModels;

namespace Horizon.Game.GengDi.Core.Views
{
    public partial class FlowerPlantingAdviceView : UserControl
    {
        public FlowerPlantingAdviceView()
        {
            InitializeComponent();
            DataContext = new FlowerPlantingAdviceViewModel();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);
            if (DataContext is FlowerPlantingAdviceViewModel vm)
            {
                _ = vm.LoadDataAsync();
            }
        }
    }
}
