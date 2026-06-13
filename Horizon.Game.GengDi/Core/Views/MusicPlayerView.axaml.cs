using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Interactivity;
using Horizon.Game.GengDi.Core.Services;
using Horizon.Game.GengDi.Core.ViewModels;
using Horizon.Game.GengDi.Models;

namespace Horizon.Game.GengDi.Core.Views
{
    public partial class MusicPlayerView : UserControl
    {
        public MusicPlayerView()
        {
            InitializeComponent();
            DataContext = new MusicPlayerViewModel();

            if (DataContext is MusicPlayerViewModel vm)
            {
                vm.LyricIndexChanged += OnLyricIndexChanged;
            }
        }

        private void OnLyricIndexChanged(object sender, int index)
        {
            if (index < 0 || LyricItemsControl == null || LyricScrollViewer == null) return;

            try
            {
                var container = LyricItemsControl.ContainerFromIndex(index);
                if (container is ContentPresenter cp)
                {
                    var element = cp.Child;
                    if (element != null)
                    {
                        var point = element.TranslatePoint(new Point(0, 0), LyricScrollViewer);
                        if (point.HasValue)
                        {
                            var targetOffset = point.Value.Y - LyricScrollViewer.Viewport.Height / 2 + element.Bounds.Height / 2;
                            targetOffset = Math.Max(0, Math.Min(targetOffset, LyricScrollViewer.Extent.Height - LyricScrollViewer.Viewport.Height));
                            LyricScrollViewer.Offset = new Vector(0, targetOffset);
                        }
                    }
                }
            }
            catch { }
        }

        private void AddToPlaylistItem_Tapped(object sender, RoutedEventArgs e)
        {
            if (sender is Border border && border.DataContext is Playlist playlist)
            {
                if (DataContext is MusicPlayerViewModel vm)
                {
                    vm.AddToPlaylistCommand.Execute(playlist);
                }
            }
        }

        private void StoryButton_Tapped(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if (DataContext is MusicPlayerViewModel vm)
            {
                var song = MusicPlayerService.Instance.CurrentSong;
                if (song != null)
                {
                    MusicStoryViewModel.Instance.OpenStoryCommand.Execute(song);
                }
            }
        }
    }
}
