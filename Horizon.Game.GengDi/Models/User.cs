using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Horizon.Game.GengDi.Core.Services;
using Horizon.Game.GengDi.Enums;
using Newtonsoft.Json;

namespace Horizon.Game.GengDi.Models
{
    public class User : INotifyPropertyChanged
    {
        private string _username = string.Empty;
        private UserStatus _status;
        private int _unreadCount;
        private string _recentMessagePreview = string.Empty;
        private DateTime? _lastMessageAt;
        private string _avatar = string.Empty;
        private Bitmap _avatarBitmap;
        private Bitmap _avatarImage;
        private bool _isSelected;

        public event PropertyChangedEventHandler PropertyChanged;

        [LiteDB.BsonId]
        public string Id { get; set; }
        public string PassportId { get; set; }
        public Guid UserId { get; set; }
        public string Username
        {
            get => _username;
            set
            {
                var normalized = value ?? string.Empty;
                if (string.Equals(_username, normalized, StringComparison.Ordinal))
                {
                    return;
                }

                _username = normalized;
                OnPropertyChanged();
                OnPropertyChanged(nameof(AvatarInitial));
            }
        }

        public string Email { get; set; }
        public string PasswordHash { get; set; }

        private string _nickname = string.Empty;

        public string Nickname
        {
            get => _nickname;
            set
            {
                var normalized = value ?? string.Empty;
                if (string.Equals(_nickname, normalized, StringComparison.Ordinal))
                    return;
                _nickname = normalized;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DisplayName));
                OnPropertyChanged(nameof(AvatarInitial));
            }
        }

        private string _remarkName = string.Empty;

        public string RemarkName
        {
            get => _remarkName;
            set
            {
                var normalized = value ?? string.Empty;
                if (string.Equals(_remarkName, normalized, StringComparison.Ordinal))
                    return;
                _remarkName = normalized;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DisplayName));
            }
        }

        [LiteDB.BsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public string DisplayName => !string.IsNullOrWhiteSpace(_remarkName)
            ? _remarkName
            : !string.IsNullOrWhiteSpace(_nickname) ? _nickname : _username;

        /// <summary>
        /// 头像 URL 或本地路径。赋值后自动异步加载 <see cref="AvatarBitmap"/>。
        /// </summary>
        public string Avatar
        {
            get => _avatar;
            set
            {
                var normalized = value ?? string.Empty;
                if (string.Equals(_avatar, normalized, StringComparison.Ordinal))
                    return;
                _avatar = normalized;
                OnPropertyChanged();
                // 头像地址变更时立即清除旧 Bitmap，再异步加载新的
                AvatarBitmap = null;
                if (!string.IsNullOrWhiteSpace(normalized))
                    _ = LoadAvatarBitmapAsync(normalized);
            }
        }

        /// <summary>
        /// 异步加载完毕后的头像 Bitmap；无头像或加载失败时为 null。
        /// </summary>
        [LiteDB.BsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public Bitmap AvatarBitmap
        {
            get => _avatarBitmap;
            private set
            {
                if (ReferenceEquals(_avatarBitmap, value))
                    return;
                _avatarBitmap = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasAvatarBitmap));
            }
        }

        /// <summary>
        /// 头像 Bitmap 已加载时为 true，可直接用于 AXAML IsVisible 绑定。
        /// </summary>
        [LiteDB.BsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public bool HasAvatarBitmap => _avatarBitmap != null;

        public string Bio { get; set; }
        public string GroupName { get; set; }
        /// <summary>
        /// 头衔（可自定义的荣誉称号）
        /// </summary>
        public string Title { get; set; }
        /// <summary>
        /// 手机号
        /// </summary>
        public string Phone { get; set; }
        /// <summary>
        /// 真实姓名（实名认证用）
        /// </summary>
        public string RealName { get; set; }
        /// <summary>
        /// 身份证号（实名认证用）
        /// </summary>
        public string IdCard { get; set; }

        public UserStatus Status
        {
            get => _status;
            set
            {
                if (_status == value)
                {
                    return;
                }

                _status = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DisplayStatus));
                OnPropertyChanged(nameof(IsAvailable));
                OnPropertyChanged(nameof(SocialSummary));
            }
        }

        public string FriendsJson { get; set; } = JsonConvert.SerializeObject(new List<string>());
        public string GroupsJson { get; set; } = JsonConvert.SerializeObject(new List<string>());
        public string RecentGamesJson { get; set; } = JsonConvert.SerializeObject(new List<string>());

        [LiteDB.BsonIgnore]
        public List<string> Friends
        {
            get => JsonConvert.DeserializeObject<List<string>>(FriendsJson) ?? new List<string>();
            set => FriendsJson = JsonConvert.SerializeObject(value);
        }

        [LiteDB.BsonIgnore]
        public List<string> Groups
        {
            get => JsonConvert.DeserializeObject<List<string>>(GroupsJson) ?? new List<string>();
            set => GroupsJson = JsonConvert.SerializeObject(value);
        }

        [LiteDB.BsonIgnore]
        public List<string> RecentGames
        {
            get => JsonConvert.DeserializeObject<List<string>>(RecentGamesJson) ?? new List<string>();
            set => RecentGamesJson = JsonConvert.SerializeObject(value);
        }

        [LiteDB.BsonIgnore]
        public bool HasAvatar => !string.IsNullOrWhiteSpace(Avatar);

        [LiteDB.BsonIgnore]
        public Bitmap AvatarImage
        {
            get => _avatarImage;
            private set
            {
                if (ReferenceEquals(_avatarImage, value))
                {
                    return;
                }

                _avatarImage = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasAvatarImage));
            }
        }

        [LiteDB.BsonIgnore]
        public bool HasAvatarImage => AvatarImage != null;

        [LiteDB.BsonIgnore]
        public string AvatarInitial => string.IsNullOrWhiteSpace(DisplayName)
            ? "?"
            : DisplayName.Substring(0, 1).ToUpperInvariant();

        [LiteDB.BsonIgnore]
        public string DisplayStatus => Status switch
        {
            UserStatus.Online => "在线",
            UserStatus.Away => "暂离",
            UserStatus.Busy => "忙碌",
            UserStatus.Invisible => "隐身",
            _ => "离线"
        };

        [LiteDB.BsonIgnore]
        public bool IsAvailable => Status == UserStatus.Online || Status == UserStatus.Away || Status == UserStatus.Busy;

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
                if (string.Equals(_recentMessagePreview, normalized, System.StringComparison.Ordinal))
                {
                    return;
                }

                _recentMessagePreview = normalized;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasRecentMessagePreview));
                OnPropertyChanged(nameof(SocialSummary));
            }
        }

        [LiteDB.BsonIgnore]
        public bool HasRecentMessagePreview => !string.IsNullOrWhiteSpace(RecentMessagePreview);

        [LiteDB.BsonIgnore]
        public string SocialSummary => HasRecentMessagePreview
            ? RecentMessagePreview
            : string.IsNullOrWhiteSpace(Bio) ? DisplayStatus : Bio;

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

        [LiteDB.BsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value)
                {
                    return;
                }

                _isSelected = value;
                OnPropertyChanged();
            }
        }

        private static Bitmap LoadAvatarBitmap(string avatarPath)
        {
            if (string.IsNullOrWhiteSpace(avatarPath))
            {
                return null;
            }

            try
            {
                var normalized = avatarPath.Trim();
                if (File.Exists(normalized))
                {
                    using var fileStream = File.OpenRead(normalized);
                    return new Bitmap(fileStream);
                }

                if (Uri.TryCreate(normalized, UriKind.Absolute, out var uri)
                    && uri.IsFile
                    && File.Exists(uri.LocalPath))
                {
                    using var fileUriStream = File.OpenRead(uri.LocalPath);
                    return new Bitmap(fileUriStream);
                }
            }
            catch
            {
            }

            return null;
        }

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private async System.Threading.Tasks.Task LoadAvatarBitmapAsync(string url)
        {
            var bitmap = await PreviewImageService.Instance.LoadAsync(url).ConfigureAwait(false);
            // 在 UI 线程上再次校验地址是否仍匹配，避免快速切换时写入过期 Bitmap
            Dispatcher.UIThread.Post(() =>
            {
                if (string.Equals(_avatar, url, StringComparison.Ordinal))
                    AvatarBitmap = bitmap;
            });
        }
    }
}