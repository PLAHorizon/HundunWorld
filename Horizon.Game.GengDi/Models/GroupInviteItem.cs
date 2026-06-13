using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Horizon.Game.GengDi.Models
{
    /// <summary>
    /// 待处理的入群邀请条目（客户端 UI 模型）。
    /// </summary>
    public class GroupInviteItem : INotifyPropertyChanged
    {
        private bool _isExpired;

        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>群组 ID。</summary>
        public ulong GroupId { get; set; }

        /// <summary>群组名称。</summary>
        public string GroupName { get; set; } = "";

        /// <summary>邀请人 ID。</summary>
        public ulong InviterId { get; set; }

        /// <summary>邀请人昵称。</summary>
        public string InviterName { get; set; } = "";

        /// <summary>是否需要被邀请者同意（true=需要操作；false=已直接拉入）。</summary>
        public bool RequiresConsent { get; set; }

        /// <summary>邀请时间戳（Unix 毫秒）。</summary>
        public long Timestamp { get; set; }

        /// <summary>群组名首字母。</summary>
        public string DisplayInitial => string.IsNullOrWhiteSpace(GroupName)
            ? "#"
            : GroupName.Substring(0, 1).ToUpperInvariant();

        /// <summary>简短描述。</summary>
        public string Summary => RequiresConsent
            ? $"{InviterName} 邀请你加入群聊"
            : $"{InviterName} 已将你拉入群聊";

        /// <summary>是否已过期。</summary>
        public bool IsExpired
        {
            get => _isExpired;
            set
            {
                if (_isExpired == value) return;
                _isExpired = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsActionable));
                OnPropertyChanged(nameof(StatusText));
            }
        }

        /// <summary>是否可以执行同意/拒绝操作。</summary>
        public bool IsActionable => RequiresConsent && !IsExpired;

        /// <summary>状态文字。</summary>
        public string StatusText => IsExpired ? "已过期" : RequiresConsent ? "" : "已加入";

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
