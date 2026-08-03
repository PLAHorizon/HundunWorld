using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
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

            // 订阅 ViewModel 属性变化以同步左侧歌单选中高亮
            vm.PropertyChanged += OnViewModelPropertyChanged;
        }

        private void OnViewModelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PlaylistManageViewModel.SelectedPlaylist))
            {
                // SelectedPlaylist 变化时容器已存在，可直接刷新高亮
                UpdateSelectionHighlight();
            }
            else if (e.PropertyName == nameof(PlaylistManageViewModel.Playlists))
            {
                // Playlists 重新加载后容器尚未生成，延迟到布局完成后刷新
                Dispatcher.UIThread.Post(UpdateSelectionHighlight, DispatcherPriority.Background);
            }
        }

        /// <summary>根据 SelectedPlaylist 更新左侧歌单列表项的选中高亮（is-active 样式类）。</summary>
        private void UpdateSelectionHighlight()
        {
            if (DataContext is not PlaylistManageViewModel vm) return;
            if (this.FindControl<ItemsControl>("PlaylistListControl") is not ItemsControl ic) return;

            foreach (var descendant in ic.GetVisualDescendants())
            {
                if (descendant is Border border
                    && border.Classes.Contains("playlist-card")
                    && border.DataContext is Playlist pl)
                {
                    var isActive = vm.SelectedPlaylist != null && vm.SelectedPlaylist.Id == pl.Id;
                    if (isActive)
                        border.Classes.Add("is-active");
                    else
                        border.Classes.Remove("is-active");
                }
            }
        }

        private void PlaylistItem_Tapped(object sender, RoutedEventArgs e)
        {
            if (sender is Border border && border.DataContext is Playlist playlist)
            {
                if (DataContext is PlaylistManageViewModel vm)
                {
                    vm.SelectPlaylistCommand.Execute(playlist);
                }
                // 立即刷新选中高亮
                UpdateSelectionHighlight();
            }
        }

        /// <summary>歌曲行点击：取 PlaylistSongEntry.Song 进行播放，排除按钮与复选框区域。</summary>
        private void PlaylistSongEntry_Tapped(object sender, RoutedEventArgs e)
        {
            // 点击操作按钮（音乐故事/收藏/移除）时不触发播放
            if (e.Source is Button) return;
            if (e.Source is Visual visual)
            {
                var ancestor = visual.GetVisualParent();
                while (ancestor != null)
                {
                    if (ancestor is Button) return;
                    // 点击复选框时不触发播放
                    if (ancestor is CheckBox) return;
                    ancestor = ancestor.GetVisualParent();
                }
            }

            if (sender is Border border && border.DataContext is PlaylistSongEntry entry)
            {
                if (DataContext is PlaylistManageViewModel vm)
                {
                    vm.PlaySongCommand.Execute(entry.Song);
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
            // 歌曲行 DataContext 为 PlaylistSongEntry，取其 Song 打开音乐故事
            if (sender is Button button && button.DataContext is PlaylistSongEntry entry)
            {
                MusicStoryViewModel.Instance.OpenStoryCommand.Execute(entry.Song);
            }
            else if (sender is Button btn && btn.DataContext is Song song)
            {
                MusicStoryViewModel.Instance.OpenStoryCommand.Execute(song);
            }
        }
    }
}
