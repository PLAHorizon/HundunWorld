using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Horizon.Game.GengDi.Core.ViewModels;
using Horizon.Game.GengDi.Models;

namespace Horizon.Game.GengDi.Core.Views
{
    public partial class PlaylistManageView : UserControl
    {
        public PlaylistManageView()
        {
            InitializeComponent();
            var vm = new PlaylistManageViewModel();
            DataContext = vm;
            vm.Initialize();
        }

        private void PlaylistItem_Tapped(object sender, RoutedEventArgs e)
        {
            if (sender is Border border && border.DataContext is Playlist playlist)
            {
                if (DataContext is PlaylistManageViewModel vm)
                {
                    vm.SelectPlaylistCommand.Execute(playlist);
                }
            }
        }

        private void PlaylistSong_Tapped(object sender, RoutedEventArgs e)
        {
            if (e.Source is Avalonia.Controls.Button) return;
            if (e.Source is Visual visual)
            {
                var ancestor = visual.GetVisualParent();
                while (ancestor != null)
                {
                    if (ancestor is Avalonia.Controls.Button) return;
                    ancestor = ancestor.GetVisualParent();
                }
            }

            if (sender is Border border && border.DataContext is Song song)
            {
                if (DataContext is PlaylistManageViewModel vm)
                {
                    vm.PlaySongCommand.Execute(song);
                }
            }
        }

        private void AddSongItem_Tapped(object sender, RoutedEventArgs e)
        {
            if (sender is Border border && border.DataContext is Song song)
            {
                if (DataContext is PlaylistManageViewModel vm)
                {
                    vm.AddSongToCurrentPlaylistCommand.Execute(song);
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
