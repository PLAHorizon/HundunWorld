using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;

namespace Horizon.Game.GengDi.Models
{
    public class Group : INotifyPropertyChanged
    {
        private int _unreadCount;
        private string _recentMessagePreview = string.Empty;
        private DateTime? _lastMessageAt;

        public event PropertyChangedEventHandler PropertyChanged;

        [LiteDB.BsonId]
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string Icon { get; set; }
        public string CreatorId { get; set; }
        public string MembersJson { get; set; } = JsonConvert.SerializeObject(new List<string>());
        public string AdminsJson { get; set; } = JsonConvert.SerializeObject(new List<string>());
        public string ChannelsJson { get; set; } = JsonConvert.SerializeObject(new List<string>());
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// 群组是否已被解散（服务端标记）。解散后群不可再收发新消息，
        /// 但本地仍保留成员与消息缓存，用户如不主动删除群则仍可查看历史消息。
        /// </summary>
        public bool IsDisbanded
        {
            get => _isDisbanded;
            set
            {
                if (_isDisbanded == value) return;
                _isDisbanded = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(MemberSummary));
            }
        }
        private bool _isDisbanded;

        [LiteDB.BsonIgnore]
        public List<string> Members
        {
            get => JsonConvert.DeserializeObject<List<string>>(MembersJson) ?? new List<string>();
            set => MembersJson = JsonConvert.SerializeObject(value);
        }

        [LiteDB.BsonIgnore]
        public List<string> Admins
        {
            get => JsonConvert.DeserializeObject<List<string>>(AdminsJson) ?? new List<string>();
            set => AdminsJson = JsonConvert.SerializeObject(value);
        }

        [LiteDB.BsonIgnore]
        public List<string> Channels
        {
            get => JsonConvert.DeserializeObject<List<string>>(ChannelsJson) ?? new List<string>();
            set => ChannelsJson = JsonConvert.SerializeObject(value);
        }

        [LiteDB.BsonIgnore]
        public string DisplayInitial => string.IsNullOrWhiteSpace(Name)
            ? "#"
            : Name.Substring(0, 1).ToUpperInvariant();

        [LiteDB.BsonIgnore]
        public string MemberSummary => IsDisbanded ? "已解散" : $"{(_serverMemberCount > 0 ? _serverMemberCount : Members.Count)} 名成员";

        private int _serverMemberCount;

        [LiteDB.BsonIgnore]
        public int ServerMemberCount
        {
            get => _serverMemberCount;
            set
            {
                if (_serverMemberCount == value) return;
                _serverMemberCount = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(MemberSummary));
                OnPropertyChanged(nameof(GroupSummary));
            }
        }

        /// <summary>
        /// 左滑动作文字：已解散群为"删除"，活跃群中群主为"解散"，非群主为"退出"。
        /// 运行时由 SocialViewModel.CurrentUserId 决定，通过 <see cref="RefreshRemoveGroupActionText"/> 触发刷新。
        /// </summary>
        [LiteDB.BsonIgnore]
        public string RemoveGroupActionText
        {
            get
            {
                if (_isDisbanded) return "删除";
                return _isOwner ? "解散" : "退出";
            }
        }

        private bool _isOwner;

        /// <summary>
        /// 根据当前登录用户刷新左滑动作文字（群主=解散，非群主=退出）。
        /// </summary>
        public void RefreshRemoveGroupActionText(string currentUserId)
        {
            var newValue = !string.IsNullOrWhiteSpace(currentUserId)
                           && string.Equals(CreatorId, currentUserId, System.StringComparison.Ordinal);
            if (_isOwner == newValue) return;
            _isOwner = newValue;
            OnPropertyChanged(nameof(RemoveGroupActionText));
        }

        [LiteDB.BsonIgnore]
        public int UnreadCount
        {
            get => _unreadCount;
            set
            {
                if (_unreadCount == value)
                {
                    return;
                }

                _unreadCount = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasUnreadMessages));
                OnPropertyChanged(nameof(HasNoUnreadMessages));
                OnPropertyChanged(nameof(UnreadBadgeText));
                OnPropertyChanged(nameof(UnreadStatusText));
            }
        }

        [LiteDB.BsonIgnore]
        public bool HasUnreadMessages => UnreadCount > 0;

        [LiteDB.BsonIgnore]
        public bool HasNoUnreadMessages => !HasUnreadMessages;

        [LiteDB.BsonIgnore]
        public string UnreadBadgeText => UnreadCount > 99 ? "99+" : UnreadCount.ToString();

        [LiteDB.BsonIgnore]
        public string UnreadStatusText => $"未读 {UnreadBadgeText}";

        [LiteDB.BsonIgnore]
        public string RecentMessagePreview
        {
            get => _recentMessagePreview;
            set
            {
                var normalized = value ?? string.Empty;
                if (string.Equals(_recentMessagePreview, normalized, StringComparison.Ordinal))
                {
                    return;
                }

                _recentMessagePreview = normalized;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasRecentMessagePreview));
                OnPropertyChanged(nameof(GroupSummary));
            }
        }

        [LiteDB.BsonIgnore]
        public bool HasRecentMessagePreview => !string.IsNullOrWhiteSpace(RecentMessagePreview);

        [LiteDB.BsonIgnore]
        public string GroupSummary => HasRecentMessagePreview
            ? RecentMessagePreview
            : string.IsNullOrWhiteSpace(Description) ? MemberSummary : Description;

        [LiteDB.BsonIgnore]
        public DateTime? LastMessageAt
        {
            get => _lastMessageAt;
            set
            {
                if (_lastMessageAt == value)
                {
                    return;
                }

                _lastMessageAt = value;
                OnPropertyChanged();
            }
        }

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}