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

        /// <summary>
        /// 点击故事卡片：取 Song 传给 VM 的 OpenStoryDetailCommand，显示内联详情。
        /// 同时保留抽屉打开能力（通过单例 MusicStoryViewModel）。
        /// </summary>
        private void StoryCard_Tapped(object sender, RoutedEventArgs e)
        {
            if (sender is Border border && border.DataContext is Song song
                && DataContext is MusicStoryListViewModel vm)
            {
                // 显示内联详情视图
                vm.OpenStoryDetailCommand.Execute(song);
            }
        }

        /// <summary>详情播放按钮：播放当前故事歌曲（占位，可接入播放服务）</summary>
        private void DetailPlay_Click(object sender, RoutedEventArgs e)
        {
            // 播放逻辑占位：此处可接入 MusicPlayerService 播放 vm.SelectedStorySong
        }

        /// <summary>详情下载按钮：下载当前故事歌曲（占位）</summary>
        private void DetailDownload_Click(object sender, RoutedEventArgs e)
        {
            // 下载逻辑占位：此处可接入下载服务
        }

        /// <summary>关联歌曲推荐-播放：切换到该歌曲的故事详情</summary>
        private void RelatedPlay_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is Song song
                && DataContext is MusicStoryListViewModel vm)
            {
                // 切换内联详情到该关联歌曲
                vm.OpenStoryDetailCommand.Execute(song);
            }
        }

        /// <summary>关联歌曲推荐-添加：添加到播放列表（占位）</summary>
        private void RelatedAdd_Click(object sender, RoutedEventArgs e)
        {
            // 添加到播放列表逻辑占位
        }
    }
}
