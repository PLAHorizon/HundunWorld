using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Horizon.Game.GengDi.Models
{
    /// <summary>
    /// 群主待审批的入群邀请条目（非群主成员发起的邀请，等待群主同意或拒绝后才会送达被邀请者）。
    /// </summary>
    public class GroupInviteApprovalItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>群组 ID。</summary>
        public ulong GroupId { get; set; }

        /// <summary>群组名称。</summary>
        public string GroupName { get; set; } = "";

        /// <summary>发起邀请的成员 ID（非群主）。</summary>
        public ulong InviterId { get; set; }

        /// <summary>发起邀请的成员昵称。</summary>
        public string InviterName { get; set; } = "";

        /// <summary>被邀请用户 ID。</summary>
        public ulong InviteeId { get; set; }

        /// <summary>发起时间戳（Unix 毫秒）。</summary>
        public long Timestamp { get; set; }

        /// <summary>群组名首字母。</summary>
        public string DisplayInitial => string.IsNullOrWhiteSpace(GroupName)
            ? "#"
            : GroupName.Substring(0, 1).ToUpperInvariant();

        /// <summary>简短描述（UI 展示）。</summary>
        public string Summary => $"{InviterName} 申请邀请用户 {InviteeId} 加入群聊";

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
