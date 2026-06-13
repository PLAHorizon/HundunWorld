using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Horizon.Game.GengDi.Core.ViewModels;
using Horizon.Game.GengDi.Models;

namespace Horizon.Game.GengDi.Core.Views
{
    public partial class MusicDiscoverView : UserControl
    {
        public MusicDiscoverView()
        {
            InitializeComponent();
            var vm = new MusicDiscoverViewModel();
            DataContext = vm;
            vm.Initialize();
        }

        private void SongItem_Tapped(object? sender, TappedEventArgs e)
        {
            if (sender is Border border && border.DataContext is Song song)
            {
                if (DataContext is MusicDiscoverViewModel vm)
                {
                    vm.PlaySongCommand.Execute(song);
                }
            }
        }

        private void StoryButton_Tapped(object? sender, TappedEventArgs e)
        {
            e.Handled = true;
            if (sender is Button button && button.DataContext is Song song)
            {
                MusicStoryViewModel.Instance.OpenStoryCommand.Execute(song);
            }
        }

        private void PlaylistItem_Tapped(object? sender, TappedEventArgs e)
        {
            if (sender is Border border && border.DataContext is Playlist playlist)
            {
                if (DataContext is MusicDiscoverViewModel vm)
                {
                    vm.PlayPlaylistCommand.Execute(playlist);
                }
            }
        }
    }
}
