using Avalonia.Controls;
using Horizon.Game.GengDi.Core.Animations;
using Horizon.Game.GengDi.Core.ViewModels;

namespace Horizon.Game.GengDi.Core.Views
{
    public partial class GamesView : UserControl
    {
        public GamesView()
        {
            InitializeComponent();
            Loaded += GamesView_Loaded;
        }

        private async void GamesView_Loaded(object sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            Loaded -= GamesView_Loaded;
            ImplicitContentAnimationHelper.AttachSlideAndScale(this.FindControl<TransitioningContentControl>("GameDetailsTransitionHost"));

            if (DataContext is GamesViewModel viewModel)
            {
                await viewModel.InitializeAsync();
            }
        }
    }
}