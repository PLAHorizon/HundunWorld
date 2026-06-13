using Avalonia.Controls;
using Avalonia.Interactivity;
using Horizon.Game.GengDi.Core.ViewModels;
using Horizon.Game.GengDi.Models;

namespace Horizon.Game.GengDi.Core.Views
{
    public partial class MusicStoryView : UserControl
    {
        public MusicStoryView()
        {
            InitializeComponent();
            DataContext = new MusicStoryListViewModel();
        }

        private void SongStory_Tapped(object sender, RoutedEventArgs e)
        {
            if (sender is Border border && border.DataContext is Song song)
            {
                MusicStoryViewModel.Instance.OpenStoryCommand.Execute(song);
            }
        }
    }
}
