using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Horizon.Game.GengDi.Core.ViewModels;

namespace Horizon.Game.GengDi.Core.Views
{
    public partial class FlowerSpeciesDetailView : UserControl
    {
        public FlowerSpeciesDetailView()
        {
            InitializeComponent();
        }

        public FlowerSpeciesDetailView(int speciesId)
        {
            InitializeComponent();
            DataContext = new FlowerSpeciesDetailViewModel(speciesId);
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);
            if (DataContext is FlowerSpeciesDetailViewModel vm)
            {
                _ = vm.LoadDataAsync();
            }
        }
    }
}
