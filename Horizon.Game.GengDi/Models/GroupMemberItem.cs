using System;
using System.ComponentModel;
using Avalonia.Media.Imaging;

using Horizon.IM.Message.Enums;
using Horizon.IM.Message.Network;

namespace Horizon.Game.GengDi.Models
{
    public class GroupMemberItem : INotifyPropertyChanged, IDisposable
    {
        private string _nickname = string.Empty;
        private string _avatar = string.Empty;
        private Bitmap _avatarBitmap;
        private IMOnlineStatus _onlineStatus;

        public string UserId { get; }

        public ulong NumericUserId { get; }

        public IMGroupMemberRole Role { get; }

        public string RoleLabel => Role switch
        {
            IMGroupMemberRole.Owner => "群主",
            IMGroupMemberRole.Admin => "管理员",
            _ => string.Empty
        };

        public bool IsOwner => Role == IMGroupMemberRole.Owner;

        public bool IsAdmin => Role == IMGroupMemberRole.Admin;

        public string Nickname
        {
            get => _nickname;
            set
            {
                var normalized = value ?? string.Empty;
                if (string.Equals(_nickname, normalized, StringComparison.Ordinal))
                    return;
                _nickname = normalized;
                OnPropertyChanged(nameof(Nickname));
                OnPropertyChanged(nameof(DisplayName));
                OnPropertyChanged(nameof(AvatarInitial));
            }
        }

        public string GroupNickname { get; set; } = string.Empty;

        public string DisplayName => !string.IsNullOrWhiteSpace(GroupNickname)
            ? GroupNickname
            : !string.IsNullOrWhiteSpace(_nickname) ? _nickname : UserId;

        public string AvatarInitial => string.IsNullOrWhiteSpace(DisplayName)
            ? "?"
            : DisplayName.Substring(0, 1).ToUpperInvariant();

        public string Avatar
        {
            get => _avatar;
            set
            {
                var normalized = value ?? string.Empty;
                if (string.Equals(_avatar, normalized, StringComparison.Ordinal))
                    return;
                _avatar = normalized;
                OnPropertyChanged(nameof(Avatar));
                AvatarBitmap = null;
                if (!string.IsNullOrWhiteSpace(normalized))
                    _ = LoadAvatarBitmapAsync(normalized);
            }
        }

        public Bitmap AvatarBitmap
        {
            get => _avatarBitmap;
            private set
            {
                if (ReferenceEquals(_avatarBitmap, value))
                    return;
                _avatarBitmap = value;
                OnPropertyChanged(nameof(AvatarBitmap));
                OnPropertyChanged(nameof(HasAvatarBitmap));
            }
        }

        public bool HasAvatarBitmap => _avatarBitmap != null;

        public IMOnlineStatus OnlineStatus
        {
            get => _onlineStatus;
            set
            {
                if (_onlineStatus == value)
                    return;
                _onlineStatus = value;
                OnPropertyChanged(nameof(OnlineStatus));
                OnPropertyChanged(nameof(IsOnline));
                OnPropertyChanged(nameof(StatusDotColor));
            }
        }

        public bool IsOnline => _onlineStatus == IMOnlineStatus.Online;

        public string StatusDotColor => _onlineStatus switch
        {
            IMOnlineStatus.Online => "#4CAF50",
            IMOnlineStatus.Away => "#FF9800",
            IMOnlineStatus.Busy => "#F44336",
            _ => "#757575"
        };

        public GroupMemberItem(IMGroupMemberInfo memberInfo)
        {
            NumericUserId = memberInfo.UserId;
            UserId = memberInfo.UserId.ToString();
            Role = memberInfo.Role;
            Nickname = memberInfo.Nickname ?? string.Empty;
            GroupNickname = memberInfo.GroupNickname ?? string.Empty;
            Avatar = memberInfo.Avatar ?? string.Empty;
            OnlineStatus = memberInfo.OnlineStatus;
        }

        private async System.Threading.Tasks.Task LoadAvatarBitmapAsync(string avatarPath)
        {
            try
            {
                var bitmap = await System.Threading.Tasks.Task.Run(() => LoadAvatarBitmap(avatarPath));
                AvatarBitmap = bitmap;
            }
            catch
            {
                AvatarBitmap = null;
            }
        }

        private static Bitmap LoadAvatarBitmap(string avatarPath)
        {
            if (string.IsNullOrWhiteSpace(avatarPath))
                return null;

            try
            {
                var normalized = avatarPath.Trim();
                if (System.IO.File.Exists(normalized))
                    return new Bitmap(normalized);

                if (Uri.TryCreate(normalized, UriKind.Absolute, out var uri)
                    && (uri.Scheme == "http" || uri.Scheme == "https"))
                {
                    using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                    var bytes = client.GetByteArrayAsync(uri).GetAwaiter().GetResult();
                    return new Bitmap(new System.IO.MemoryStream(bytes));
                }
            }
            catch { }

            return null;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(string propertyName)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        public void Dispose()
        {
            _avatarBitmap?.Dispose();
        }
    }
}
