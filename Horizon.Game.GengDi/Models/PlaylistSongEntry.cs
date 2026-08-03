using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Horizon.Game.GengDi.Models
{
    /// <summary>歌单内歌曲条目，包装序号与选中状态，支持表格展示与批量操作。</summary>
    public class PlaylistSongEntry : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

        private bool _isSelected;

        /// <summary>歌曲在歌单中的序号（从 1 开始）。</summary>
        public int Index { get; set; }

        /// <summary>关联的歌曲实体。</summary>
        public Song Song { get; set; }

        /// <summary>是否处于选中状态（用于批量操作）。</summary>
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged();
                }
            }
        }
    }
}
