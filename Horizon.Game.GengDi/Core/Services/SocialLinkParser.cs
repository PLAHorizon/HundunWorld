using System;
using System.IO;
using System.Text.RegularExpressions;
using Horizon.Game.GengDi.Enums;

namespace Horizon.Game.GengDi.Core.Services
{
    public sealed class SocialLinkParseResult
    {
        public Uri SourceUri { get; init; }

        public string OriginalUrl { get; init; } = string.Empty;

        public MessageType SuggestedMessageType { get; init; }

        public string Provider { get; init; } = string.Empty;

        public string SiteName { get; init; } = string.Empty;

        public string PlaybackUrl { get; init; } = string.Empty;

        public bool SupportsInlinePlayback { get; init; }

        public string TitleFallback { get; init; } = string.Empty;

        public string SubtitleFallback { get; init; } = string.Empty;

        public string PreviewImageUrl { get; init; } = string.Empty;
    }

    public static class SocialLinkParser
    {
        private static readonly object InlinePlayerPageSyncRoot = new();
        private static readonly Regex UrlRegex = new(@"https?://[^\s]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex BilibiliRegex = new(@"/video/(?<id>BV[0-9A-Za-z]+|av\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex VimeoRegex = new(@"/(?<id>\d+)(?:$|/)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex YoukuRegex = new(@"id_(?<id>[0-9A-Za-z=]+)\.html", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex QqVideoRegex = new(@"/(?<id>[0-9A-Za-z]+)\.html", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static string _inlineVideoPlayerPageAddress = string.Empty;

        private static readonly string[] ImageExtensions =
        {
            ".png", ".jpg", ".jpeg", ".webp", ".gif", ".bmp"
        };

        private static readonly string[] VideoExtensions =
        {
            ".mp4", ".webm", ".mov", ".m4v", ".ogv"
        };

        public static bool TryExtractFirstUrl(string input, out string url, out string caption)
        {
            url = string.Empty;
            caption = input?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(input))
            {
                return false;
            }

            var match = UrlRegex.Match(input);
            if (!match.Success)
            {
                return false;
            }

            url = match.Value.Trim().TrimEnd('.', ',', ';', '!', '?', ')', ']', '}');
            caption = input.Remove(match.Index, match.Length).Trim();
            caption = Regex.Replace(caption, "\\s{2,}", " ");
            return !string.IsNullOrWhiteSpace(url);
        }

        public static bool IsImagePath(string path)
        {
            return HasSupportedExtension(path, ImageExtensions);
        }

        public static bool IsVideoPath(string path)
        {
            return HasSupportedExtension(path, VideoExtensions);
        }

        public static string BuildDirectVideoPlaybackAddress(string source)
        {
            var resolvedSource = ResolveFileOrUrl(source);
            if (string.IsNullOrWhiteSpace(resolvedSource))
            {
                return string.Empty;
            }

            // For local files, navigate the WebView directly to the file URI so the browser
            // plays it natively. The HTML wrapper page cannot load a video from a different
            // file:// origin due to CEF's same-origin policy for file:// URLs.
            if (resolvedSource.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            {
                return resolvedSource;
            }

            var playerPageAddress = EnsureInlineVideoPlayerPageAddress();
            return string.IsNullOrWhiteSpace(playerPageAddress)
                ? string.Empty
                : $"{playerPageAddress}?src={Uri.EscapeDataString(resolvedSource)}";
        }

        public static bool TryParse(string input, out SocialLinkParseResult result)
        {
            result = null;

            if (string.IsNullOrWhiteSpace(input) || !Uri.TryCreate(input.Trim(), UriKind.Absolute, out var uri))
            {
                return false;
            }

            if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (IsImagePath(uri.AbsolutePath))
            {
                result = CreateResult(uri, MessageType.Image, "image", "图片", uri.ToString(), false, GetSafeTitle(uri), uri.Host, uri.ToString());
                return true;
            }

            if (IsVideoPath(uri.AbsolutePath))
            {
                result = CreateResult(
                    uri,
                    MessageType.Video,
                    "video",
                    "视频",
                    BuildDirectVideoPlaybackAddress(uri.ToString()),
                    true,
                    GetSafeTitle(uri),
                    "直链视频",
                    string.Empty);
                return true;
            }

            if (TryCreateYoutubeResult(uri, out result) ||
                TryCreateBilibiliResult(uri, out result) ||
                TryCreateDouyinResult(uri, out result) ||
                TryCreateQqVideoResult(uri, out result) ||
                TryCreateYoukuResult(uri, out result) ||
                TryCreateVimeoResult(uri, out result) ||
                TryCreateIxiguaResult(uri, out result))
            {
                return true;
            }

            var siteName = GetSiteName(uri.Host);
            result = CreateResult(uri, MessageType.LinkCard, siteName, siteName, uri.ToString(), false, siteName, uri.Host, string.Empty);
            return true;
        }

        private static bool TryCreateYoutubeResult(Uri uri, out SocialLinkParseResult result)
        {
            result = null;
            var host = uri.Host.ToLowerInvariant();
            if (!host.Contains("youtube.com", StringComparison.Ordinal) && !host.Contains("youtu.be", StringComparison.Ordinal))
            {
                return false;
            }

            var videoId = GetQueryParameter(uri, "v");
            if (string.IsNullOrWhiteSpace(videoId) && host.Contains("youtu.be", StringComparison.Ordinal))
            {
                videoId = GetTrimmedPathSegment(uri);
            }

            if (string.IsNullOrWhiteSpace(videoId) && uri.AbsolutePath.Contains("/shorts/", StringComparison.OrdinalIgnoreCase))
            {
                videoId = GetTrimmedPathSegment(uri);
            }

            if (string.IsNullOrWhiteSpace(videoId) && uri.AbsolutePath.Contains("/embed/", StringComparison.OrdinalIgnoreCase))
            {
                videoId = GetTrimmedPathSegment(uri);
            }

            var playbackUrl = string.IsNullOrWhiteSpace(videoId)
                ? uri.ToString()
                : $"https://www.youtube.com/embed/{videoId}?autoplay=1&mute=1&playsinline=1";

            result = CreateResult(uri, MessageType.LinkCard, "youtube", "YouTube", playbackUrl, true, "YouTube 视频", uri.Host, string.Empty);
            return true;
        }

        private static bool TryCreateBilibiliResult(Uri uri, out SocialLinkParseResult result)
        {
            result = null;
            var host = uri.Host.ToLowerInvariant();
            if (!host.Contains("bilibili.com", StringComparison.Ordinal) && !host.Contains("b23.tv", StringComparison.Ordinal))
            {
                return false;
            }

            var match = BilibiliRegex.Match(uri.AbsolutePath);
            var playbackUrl = uri.ToString();
            if (match.Success)
            {
                var id = match.Groups["id"].Value;
                const string bilibiliParams = "&page=1&as_wide=1&high_quality=1&danmaku=0";
                playbackUrl = id.StartsWith("BV", StringComparison.OrdinalIgnoreCase)
                    ? $"https://player.bilibili.com/player.html?bvid={id}{bilibiliParams}"
                    : $"https://player.bilibili.com/player.html?aid={id.Replace("av", string.Empty, StringComparison.OrdinalIgnoreCase)}{bilibiliParams}";
            }

            result = CreateResult(uri, MessageType.LinkCard, "bilibili", "哔哩哔哩", playbackUrl, true, "哔哩哔哩视频", uri.Host, string.Empty);
            return true;
        }

        private static bool TryCreateDouyinResult(Uri uri, out SocialLinkParseResult result)
        {
            result = null;
            var host = uri.Host.ToLowerInvariant();
            if (!host.Contains("douyin.com", StringComparison.Ordinal) && !host.Contains("iesdouyin.com", StringComparison.Ordinal))
            {
                return false;
            }

            // Douyin does not support iFrame embedding; attempting to load the video URL inside
            // a WebView only shows the Douyin homepage. Disable inline playback so the card
            // renders as a regular link card that the user can open in an external browser.
            result = CreateResult(uri, MessageType.LinkCard, "douyin", "抖音", uri.ToString(), false, "抖音视频", uri.Host, string.Empty);
            return true;
        }

        private static bool TryCreateQqVideoResult(Uri uri, out SocialLinkParseResult result)
        {
            result = null;
            if (!uri.Host.Contains("qq.com", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var videoId = GetQueryParameter(uri, "vid");
            if (string.IsNullOrWhiteSpace(videoId))
            {
                var match = QqVideoRegex.Match(uri.AbsolutePath);
                if (match.Success)
                {
                    videoId = match.Groups["id"].Value;
                }
            }

            var playbackUrl = string.IsNullOrWhiteSpace(videoId)
                ? uri.ToString()
                : $"https://v.qq.com/iframe/player.html?vid={videoId}&tiny=0&auto=0";

            result = CreateResult(uri, MessageType.LinkCard, "qqvideo", "腾讯视频", playbackUrl, true, "腾讯视频", uri.Host, string.Empty);
            return true;
        }

        private static bool TryCreateYoukuResult(Uri uri, out SocialLinkParseResult result)
        {
            result = null;
            if (!uri.Host.Contains("youku.com", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var match = YoukuRegex.Match(uri.AbsolutePath);
            var playbackUrl = match.Success
                ? $"https://player.youku.com/embed/{match.Groups["id"].Value}?autoplay=true"
                : uri.ToString();

            result = CreateResult(uri, MessageType.LinkCard, "youku", "优酷", playbackUrl, true, "优酷视频", uri.Host, string.Empty);
            return true;
        }

        private static bool TryCreateVimeoResult(Uri uri, out SocialLinkParseResult result)
        {
            result = null;
            if (!uri.Host.Contains("vimeo.com", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var match = VimeoRegex.Match(uri.AbsolutePath);
            var playbackUrl = match.Success
                ? $"https://player.vimeo.com/video/{match.Groups["id"].Value}?autoplay=1&muted=1"
                : uri.ToString();

            result = CreateResult(uri, MessageType.LinkCard, "vimeo", "Vimeo", playbackUrl, true, "Vimeo 视频", uri.Host, string.Empty);
            return true;
        }

        private static bool TryCreateIxiguaResult(Uri uri, out SocialLinkParseResult result)
        {
            result = null;
            if (!uri.Host.Contains("ixigua.com", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            result = CreateResult(uri, MessageType.LinkCard, "ixigua", "西瓜视频", uri.ToString(), true, "西瓜视频", uri.Host, string.Empty);
            return true;
        }

        private static SocialLinkParseResult CreateResult(
            Uri uri,
            MessageType type,
            string provider,
            string siteName,
            string playbackUrl,
            bool supportsInlinePlayback,
            string titleFallback,
            string subtitleFallback,
            string previewImageUrl)
        {
            return new SocialLinkParseResult
            {
                SourceUri = uri,
                OriginalUrl = uri.ToString(),
                SuggestedMessageType = type,
                Provider = provider,
                SiteName = siteName,
                PlaybackUrl = playbackUrl ?? string.Empty,
                SupportsInlinePlayback = supportsInlinePlayback,
                TitleFallback = titleFallback ?? string.Empty,
                SubtitleFallback = subtitleFallback ?? string.Empty,
                PreviewImageUrl = previewImageUrl ?? string.Empty
            };
        }

        private static string GetSiteName(string host)
        {
            if (host.Contains("bilibili", StringComparison.OrdinalIgnoreCase) || host.Contains("b23.tv", StringComparison.OrdinalIgnoreCase))
            {
                return "哔哩哔哩";
            }

            if (host.Contains("douyin", StringComparison.OrdinalIgnoreCase))
            {
                return "抖音";
            }

            if (host.Contains("youtube", StringComparison.OrdinalIgnoreCase) || host.Contains("youtu.be", StringComparison.OrdinalIgnoreCase))
            {
                return "YouTube";
            }

            if (host.Contains("youku", StringComparison.OrdinalIgnoreCase))
            {
                return "优酷";
            }

            if (host.Contains("qq.com", StringComparison.OrdinalIgnoreCase))
            {
                return "腾讯视频";
            }

            if (host.Contains("ixigua", StringComparison.OrdinalIgnoreCase))
            {
                return "西瓜视频";
            }

            if (host.Contains("vimeo", StringComparison.OrdinalIgnoreCase))
            {
                return "Vimeo";
            }

            return host.Replace("www.", string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        private static string GetQueryParameter(Uri uri, string key)
        {
            if (string.IsNullOrWhiteSpace(uri.Query))
            {
                return string.Empty;
            }

            var pairs = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries);
            foreach (var pair in pairs)
            {
                var parts = pair.Split('=', 2, StringSplitOptions.None);
                if (parts.Length > 0 && string.Equals(parts[0], key, StringComparison.OrdinalIgnoreCase))
                {
                    return parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : string.Empty;
                }
            }

            return string.Empty;
        }

        private static string GetTrimmedPathSegment(Uri uri)
        {
            var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            return segments.Length == 0 ? string.Empty : segments[^1];
        }

        private static string GetSafeTitle(Uri uri)
        {
            var rawFileName = Path.GetFileNameWithoutExtension(Uri.UnescapeDataString(uri.AbsolutePath));
            return string.IsNullOrWhiteSpace(rawFileName) ? uri.Host : rawFileName;
        }

        private static string ResolveFileOrUrl(string source)
        {
            if (Uri.TryCreate(source, UriKind.Absolute, out var uri))
            {
                return uri.AbsoluteUri;
            }

            return new Uri(Path.GetFullPath(source)).AbsoluteUri;
        }

        private static string EnsureInlineVideoPlayerPageAddress()
        {
            lock (InlinePlayerPageSyncRoot)
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(_inlineVideoPlayerPageAddress))
                    {
                        var cachedPlayerPath = new Uri(_inlineVideoPlayerPageAddress).LocalPath;
                        if (File.Exists(cachedPlayerPath))
                        {
                            return _inlineVideoPlayerPageAddress;
                        }
                    }

                    var playerDirectory = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "HundunWorld",
                        "InlinePlayback");

                    Directory.CreateDirectory(playerDirectory);

                    var playerPagePath = Path.Combine(playerDirectory, "inline-video-player.html");
                    var playerPageContent = BuildInlineVideoPlayerPageContent();

                    if (!File.Exists(playerPagePath) ||
                        !string.Equals(File.ReadAllText(playerPagePath), playerPageContent, StringComparison.Ordinal))
                    {
                        File.WriteAllText(playerPagePath, playerPageContent);
                    }

                    _inlineVideoPlayerPageAddress = new Uri(playerPagePath).AbsoluteUri;
                    return _inlineVideoPlayerPageAddress;
                }
                catch
                {
                    return string.Empty;
                }
            }
        }

        private static string BuildInlineVideoPlayerPageContent()
        {
            return """
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <style>
        html, body {
            margin: 0;
            width: 100%;
            height: 100%;
            overflow: hidden;
            background: #02050a;
            color: #dbe7f3;
            font-family: 'Segoe UI', sans-serif;
        }

        body {
            display: flex;
            align-items: center;
            justify-content: center;
        }

        video {
            width: 100%;
            height: 100%;
            object-fit: cover;
            background: #02050a;
        }

        .status {
            position: absolute;
            inset: auto 16px 16px 16px;
            padding: 10px 12px;
            border-radius: 10px;
            background: rgba(2, 5, 10, 0.72);
            font-size: 13px;
            line-height: 1.4;
            cursor: pointer;
            user-select: none;
        }

        .hidden {
            display: none;
        }
    </style>
</head>
<body>
    <video id='player' autoplay muted loop playsinline preload='auto' controls></video>
    <div id='status' class='status hidden'>视频已载入，点击此处开始播放</div>

    <script>
        const player = document.getElementById('player');
        const status = document.getElementById('status');
        const source = new URLSearchParams(window.location.search).get('src') || '';

        const showStatus = (message) => {
            status.textContent = message;
            status.classList.remove('hidden');
        };

        const hideStatus = () => status.classList.add('hidden');

        const tryPlay = (fromUserGesture = false) => {
            if (fromUserGesture) {
                player.muted = false;
            }

            const playPromise = player.play();
            if (playPromise && typeof playPromise.catch === 'function') {
                playPromise.catch(() => showStatus('视频已载入，点击此处开始播放'));
            }
        };

        status.addEventListener('click', () => {
            hideStatus();
            tryPlay(true);
        });

        player.addEventListener('click', () => {
            if (player.paused) {
                tryPlay(true);
            }
        });

        if (!source) {
            showStatus('未找到可播放的视频源');
        } else {
            player.src = source;
            player.addEventListener('loadeddata', () => showStatus('视频已载入，点击此处开始播放'), { once: true });
            player.addEventListener('canplay', tryPlay, { once: true });
            player.addEventListener('play', hideStatus);
            player.addEventListener('error', () => showStatus('视频当前无法在卡片内播放'), { once: true });
            player.load();
            tryPlay();
        }
    </script>
</body>
</html>
""";
        }

        private static bool HasSupportedExtension(string value, string[] extensions)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            foreach (var extension in extensions)
            {
                if (value.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}