using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Horizon.Game.GengDi.Core.ViewModels;
using Horizon.Game.GengDi.Models;

namespace Horizon.Game.GengDi.Core.Views
{
    public partial class MusicSearchView : UserControl
    {
        public MusicSearchView()
        {
            InitializeComponent();
            DataContext = new MusicSearchViewModel();
        }

        private void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && DataContext is MusicSearchViewModel vm)
            {
                vm.SearchCommand.Execute(null);
            }
        }

        private void SongItem_Tapped(object sender, RoutedEventArgs e)
        {
            if (sender is Border border && border.DataContext is Song song)
            {
                if (DataContext is MusicSearchViewModel vm)
                {
                    vm.PlaySongCommand.Execute(song);
                }
            }
        }

        private void PlaylistItem_Tapped(object sender, RoutedEventArgs e)
        {
            if (sender is Border border && border.DataContext is Playlist playlist)
            {
                if (DataContext is MusicSearchViewModel vm)
                {
                    vm.PlayPlaylistCommand.Execute(playlist);
                }
            }
        }

        private void AddToPlaylistItem_Tapped(object sender, RoutedEventArgs e)
        {
            if (sender is Border border && border.DataContext is Playlist playlist)
            {
                if (DataContext is MusicSearchViewModel vm)
                {
                    vm.SelectPlaylistForSongCommand.Execute(playlist);
                }
            }
        }

        private void StoryButton_Tapped(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if (sender is Button button && button.DataContext is Song song)
            {
                MusicStoryViewModel.Instance.OpenStoryCommand.Execute(song);
            }
        }
    }
}
