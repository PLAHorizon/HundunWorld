using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Horizon.Game.GengDi.Core.Services;
using Horizon.Game.GengDi.Enums;
using Horizon.Game.GengDi.Models;

namespace Horizon.Game.GengDi.Core.ViewModels
{
    public sealed class ChatMessageItemViewModel : ViewModelBase
    {
        private static readonly IBrush OutgoingBackgroundBrush = new SolidColorBrush(Color.Parse("#1F6FEB"));
        private static readonly IBrush IncomingBackgroundBrush = new SolidColorBrush(Color.Parse("#2D2D30"));
        private static readonly IBrush OutgoingBorderBrush = new SolidColorBrush(Color.Parse("#2A7FFF"));
        private static readonly IBrush IncomingBorderBrush = new SolidColorBrush(Color.Parse("#3E3E42"));

        private readonly RichMessageContent _content;
        private Bitmap _previewImage;
        private List<Bitmap> _attachmentImages;
        private bool _isInlinePlayerOpen;
        private bool _isMediaViewerOpen;
        private string _activePlaybackAddress = "about:blank";
        private int _currentViewerAttachmentIndex;

        public ChatMessageItemViewModel(Horizon.Game.GengDi.Models.IMMessage message, string currentUserId, string senderDisplayName)
        {
            Message = message;
            SenderDisplayName = string.IsNullOrWhiteSpace(senderDisplayName) ? message?.SenderId ?? "成员" : senderDisplayName;
            IsOutgoing = string.Equals(message?.SenderId, currentUserId, StringComparison.Ordinal);
            _content = RichMessageContentSerializer.Deserialize(message);

            var previewSource = PrimaryPreviewSource;
            if (!string.IsNullOrWhiteSpace(previewSource))
            {
                _ = LoadPreviewImageAsync(previewSource);
            }

            if (_content.Attachments != null && _content.Attachments.Count > 0)
            {
                _ = LoadAttachmentImagesAsync();
            }
        }

        public Horizon.Game.GengDi.Models.IMMessage Message { get; }

        public string SenderDisplayName { get; }

        public bool IsOutgoing { get; }

        public HorizontalAlignment BubbleAlignment => IsOutgoing ? HorizontalAlignment.Right : HorizontalAlignment.Left;

        public Thickness BubbleMargin => IsOutgoing ? new Thickness(140, 0, 0, 14) : new Thickness(0, 0, 140, 14);

        public IBrush BubbleBackground => IsOutgoing ? OutgoingBackgroundBrush : IncomingBackgroundBrush;

        public IBrush BubbleBorder => IsOutgoing ? OutgoingBorderBrush : IncomingBorderBrush;

        public string TimestampText => Message.Timestamp.ToString("yyyy-MM-dd HH:mm");

        public string Text => _content.Text;

        public bool HasText => !string.IsNullOrWhiteSpace(Text);

        public bool HasCard => _content.Type == MessageType.Image || _content.Type == MessageType.Video || _content.Type == MessageType.LinkCard;

        public bool HasFileCard => _content.Type == MessageType.File;

        public string FileDisplayName => !string.IsNullOrWhiteSpace(_content.FileName)
            ? _content.FileName
            : System.IO.Path.GetFileName(_content.MediaUrl ?? string.Empty) ?? "未知文件";

        public string FileSizeLabel
        {
            get
            {
                if (_content.FileSize > 0)
                {
                    return FormatFileSize(_content.FileSize);
                }
                var path = _content.MediaUrl;
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                {
                    return FormatFileSize(new FileInfo(path).Length);
                }
                return "未知大小";
            }
        }

        private static string FormatFileSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
            return $"{bytes / (1024.0 * 1024 * 1024):F1} GB";
        }

        private static readonly (string[] Extensions, string Icon)[] FileIconMap =
        {
            (new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".svg", ".ico", ".tiff" }, "🖼️"),
            (new[] { ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".flv", ".webm", ".m4v" }, "🎬"),
            (new[] { ".mp3", ".wav", ".flac", ".aac", ".ogg", ".wma", ".m4a", ".opus" }, "🎵"),
            (new[] { ".zip", ".rar", ".7z", ".tar", ".gz", ".bz2", ".xz" }, "📦"),
            (new[] { ".doc", ".docx", ".rtf", ".odt", ".wps" }, "📝"),
            (new[] { ".xls", ".xlsx", ".csv", ".ods", ".tsv" }, "📊"),
            (new[] { ".ppt", ".pptx", ".odp", ".key" }, "📑"),
            (new[] { ".pdf" }, "📕"),
            (new[] { ".txt", ".md", ".log", ".ini", ".cfg", ".conf", ".properties" }, "📄"),
            (new[] { ".exe", ".msi", ".app", ".dmg", ".deb", ".rpm" }, "⚙️"),
            (new[] { ".html", ".htm", ".css", ".js", ".ts", ".py", ".cs", ".java", ".cpp", ".c", ".h", ".json", ".xml", ".yaml", ".yml", ".sql" }, "💻"),
            (new[] { ".psd", ".ai", ".sketch", ".fig" }, "🎨"),
            (new[] { ".torrent" }, "🧲"),
        };

        public string FileIcon
        {
            get
            {
                var ext = System.IO.Path.GetExtension(FileDisplayName).ToLowerInvariant();
                if (string.IsNullOrWhiteSpace(ext)) return "📄";

                foreach (var (extensions, icon) in FileIconMap)
                {
                    foreach (var e in extensions)
                    {
                        if (ext == e) return icon;
                    }
                }
                return "📄";
            }
        }

        public bool IsEmoji => _content.Type == MessageType.Emoji;

        public string PreviewTitle => !string.IsNullOrWhiteSpace(_content.Title)
            ? _content.Title
            : SenderDisplayName;

        public string PreviewSubtitle => !string.IsNullOrWhiteSpace(_content.Subtitle)
            ? _content.Subtitle
            : _content.OriginalUrl;

        public bool HasPreviewSubtitle => !string.IsNullOrWhiteSpace(PreviewSubtitle);

        public string CardKindLabel => _content.Type switch
        {
            MessageType.Image => "图片",
            MessageType.Video => "视频",
            MessageType.LinkCard => "链接卡片",
            _ => "媒体"
        };

        public string CardProviderLabel => !string.IsNullOrWhiteSpace(_content.SiteName)
            ? $"{_content.SiteName} · {CardKindLabel}"
            : CardKindLabel;

        public string CardHint => CanInlinePlay ? "悬停预载，点击画面播放" : "静态预览";

        public bool CanInlinePlay => _content.SupportsInlinePlayback && !string.IsNullOrWhiteSpace(_content.PlaybackUrl);

        public bool IsInlinePlayerOpen
        {
            get => _isInlinePlayerOpen;
            private set
            {
                if (SetProperty(ref _isInlinePlayerOpen, value))
                {
                    OnPropertyChanged(nameof(IsPreviewVisible));
                }
            }
        }

        public bool IsPreviewVisible => !IsInlinePlayerOpen;

        public string PlaybackAddress => _content.PlaybackUrl;

        public string ActivePlaybackAddress
        {
            get => _activePlaybackAddress;
            private set => SetProperty(ref _activePlaybackAddress, value);
        }

        public Bitmap PreviewImage
        {
            get => _previewImage;
            private set
            {
                if (SetProperty(ref _previewImage, value))
                {
                    OnPropertyChanged(nameof(HasPreviewImage));
                }
            }
        }

        public bool HasPreviewImage => PreviewImage != null;

        public int AttachmentCount => _content.Attachments?.Count ?? 0;

        public bool HasMultipleImages => AttachmentCount > 1;

        public IReadOnlyList<Bitmap> AttachmentImages => _attachmentImages ?? (IReadOnlyList<Bitmap>)Array.Empty<Bitmap>();

        public int AttachmentGridColumns
        {
            get
            {
                var count = AttachmentCount;
                if (count <= 1) return 1;
                if (count == 2) return 2;
                if (count <= 4) return 2;
                return 3;
            }
        }

        public IReadOnlyList<string> AttachmentMediaUrls => _content.Attachments?
            .Select(a => !string.IsNullOrWhiteSpace(a.PreviewImageUrl) ? a.PreviewImageUrl : a.MediaUrl)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList() ?? (IReadOnlyList<string>)Array.Empty<string>();

        public int CurrentViewerAttachmentIndex
        {
            get => _currentViewerAttachmentIndex;
            set => SetProperty(ref _currentViewerAttachmentIndex, value);
        }

        public bool CanGoToPreviousAttachment => _currentViewerAttachmentIndex > 0;

        public bool CanGoToNextAttachment => _content.Attachments != null && _currentViewerAttachmentIndex < _content.Attachments.Count - 1;

        public void GoToPreviousAttachment()
        {
            if (CanGoToPreviousAttachment)
            {
                CurrentViewerAttachmentIndex--;
                OnPropertyChanged(nameof(CanGoToPreviousAttachment));
                OnPropertyChanged(nameof(CanGoToNextAttachment));
            }
        }

        public void GoToNextAttachment()
        {
            if (CanGoToNextAttachment)
            {
                CurrentViewerAttachmentIndex++;
                OnPropertyChanged(nameof(CanGoToPreviousAttachment));
                OnPropertyChanged(nameof(CanGoToNextAttachment));
            }
        }

        public bool IsMediaViewable => _content.Type == MessageType.Image || _content.Type == MessageType.Video;

        public bool IsFile => _content.Type == MessageType.File;

        public bool IsFileViewable => IsFile && !string.IsNullOrWhiteSpace(_content.MediaUrl);

        public bool IsMediaViewerOpen
        {
            get => _isMediaViewerOpen;
            set
            {
                if (SetProperty(ref _isMediaViewerOpen, value))
                {
                    OnPropertyChanged(nameof(IsMediaViewerClosed));
                }
            }
        }

        public bool IsMediaViewerClosed => !IsMediaViewerOpen;

        public string MediaUrl => _content.MediaUrl;

        public string OriginalUrl => _content.OriginalUrl;

        public bool CanForward => HasCard || HasText;

        public bool CanCopyContent => HasText
            || _content.Type == MessageType.Image
            || !string.IsNullOrWhiteSpace(_content.OriginalUrl);

        // 仅当 MediaUrl 是存在的本地文件路径时才允许保存（http/https URL 不可直接保存到磁盘）
        public bool CanSaveMedia => (_content.Type == MessageType.Image || _content.Type == MessageType.Video)
            && IsLocalFilePath(_content.MediaUrl);

        public string CopyableText
        {
            get
            {
                if (HasText)
                {
                    return Text;
                }

                if (!string.IsNullOrWhiteSpace(_content.OriginalUrl))
                {
                    return _content.OriginalUrl;
                }

                if (!string.IsNullOrWhiteSpace(_content.MediaUrl))
                {
                    return _content.MediaUrl;
                }

                return string.Empty;
            }
        }

        /// <summary>
        /// 转发时使用的完整序列化消息内容（保留原始消息类型和卡片格式）。
        /// </summary>
        public string SerializedContent => RichMessageContentSerializer.Serialize(_content);

        public string SaveableMediaPath
        {
            get
            {
                var url = _content.MediaUrl;
                // 仅返回实际存在的本地文件路径，http/https URL 不能当作本地路径使用
                return IsLocalFilePath(url) ? url : string.Empty;
            }
        }

        /// <summary>
        /// 判断给定路径是否为存在的本地文件（排除 http/https URL）。
        /// </summary>
        private static bool IsLocalFilePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;
            if (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return false;
            return File.Exists(path);
        }

        public void OpenMediaViewer()
        {
            if (IsMediaViewable)
            {
                IsMediaViewerOpen = true;
            }
        }

        public void CloseMediaViewer()
        {
            IsMediaViewerOpen = false;
        }

        public void StartInlinePlayback()
        {
            if (!CanInlinePlay)
            {
                return;
            }

            ActivePlaybackAddress = _content.PlaybackUrl;
            IsInlinePlayerOpen = true;
        }

        public void StopInlinePlayback()
        {
            if (!IsInlinePlayerOpen)
            {
                return;
            }

            IsInlinePlayerOpen = false;
            ActivePlaybackAddress = "about:blank";
        }

        private string PrimaryPreviewSource => !string.IsNullOrWhiteSpace(_content.PreviewImageUrl)
            ? _content.PreviewImageUrl
            : _content.Type == MessageType.Image
                ? _content.MediaUrl
                : string.Empty;

        private async Task LoadPreviewImageAsync(string previewSource)
        {
            var image = await PreviewImageService.Instance.LoadAsync(previewSource).ConfigureAwait(false);
            if (image == null)
            {
                return;
            }

            Dispatcher.UIThread.Post(() => PreviewImage = image);
        }

        private async Task LoadAttachmentImagesAsync()
        {
            var images = new List<Bitmap>();
            foreach (var attachment in _content.Attachments)
            {
                var source = !string.IsNullOrWhiteSpace(attachment.PreviewImageUrl)
                    ? attachment.PreviewImageUrl
                    : attachment.MediaUrl;
                if (string.IsNullOrWhiteSpace(source)) continue;
                var image = await PreviewImageService.Instance.LoadAsync(source).ConfigureAwait(false);
                if (image != null)
                {
                    images.Add(image);
                }
            }

            if (images.Count > 0)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    _attachmentImages = images;
                    OnPropertyChanged(nameof(AttachmentImages));
                    OnPropertyChanged(nameof(AttachmentCount));
                    OnPropertyChanged(nameof(HasMultipleImages));
                    OnPropertyChanged(nameof(AttachmentGridColumns));
                });
            }
        }
    }
}