using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Game.GengDi.Core.Services
{
    /// <summary>
    /// 共享文件服务：聊天中的文件/媒体（远程 URL）下载到本地缓存后，
    /// 以系统默认关联程序打开（查看/编辑）。
    /// - 下载保留原始文件名与扩展名，确保 Windows 按扩展名匹配关联程序；
    /// - 已缓存的文件直接复用（按 URL 内容哈希去重）；
    /// - 自签 https 经 SslConfiguration 处理（IM 媒体服务）。
    /// </summary>
    public static class SharedFileService
    {
        private static readonly HttpClient Http = new(SslConfiguration.CreateStandardHandler())
        {
            Timeout = TimeSpan.FromSeconds(60)
        };

        private static string CacheDir => Path.Combine(Path.GetTempPath(), "HorizonSharedFiles");

        /// <summary>
        /// 打开共享文件：本地路径直接打开；远程 URL 先下载到缓存再打开。
        /// 返回错误消息（失败时），成功返回 null。
        /// </summary>
        public static async Task<string> OpenAsync(string url, string fileName)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return "文件地址无效。";
            }

            try
            {
                var localPath = await EnsureLocalAsync(url, fileName).ConfigureAwait(false);
                using var _ = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(localPath)
                {
                    UseShellExecute = true
                });
                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SharedFileService] 打开文件失败：{url}，{ex.Message}");
                return $"打开文件失败：{ex.Message}";
            }
        }

        /// <summary>
        /// 确保文件在本地可用：本地存在直接返回；远程 URL 下载到缓存（文件名=哈希+原始名，保留扩展名）。
        /// </summary>
        public static async Task<string> EnsureLocalAsync(string url, string fileName)
        {
            // 本地路径：存在直接用；不存在报错（历史会话中文件可能已被清理）
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
                || uri.IsFile
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                if (File.Exists(url))
                {
                    return url;
                }

                throw new FileNotFoundException("本地文件不存在（可能已被清理）。", url);
            }

            Directory.CreateDirectory(CacheDir);
            var safeName = BuildSafeFileName(fileName, uri);
            var localPath = Path.Combine(CacheDir, safeName);

            if (File.Exists(localPath) && new FileInfo(localPath).Length > 0)
            {
                return localPath;
            }

            var bytes = await Http.GetByteArrayAsync(uri).ConfigureAwait(false);
            await File.WriteAllBytesAsync(localPath, bytes).ConfigureAwait(false);
            return localPath;
        }

        /// <summary>查找远程文件的本地缓存副本（未下载返回 null），供大小显示等复用</summary>
        public static string FindCachedLocalCopy(string url, string fileName)
        {
            if (string.IsNullOrWhiteSpace(url)
                || !Uri.TryCreate(url, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                return null;
            }

            try
            {
                var localPath = Path.Combine(CacheDir, BuildSafeFileName(fileName, uri));
                return File.Exists(localPath) && new FileInfo(localPath).Length > 0 ? localPath : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>构造缓存文件名：{url哈希8位}_{清洗后的原始文件名}；原始名缺失时回退 URL 末段</summary>
        private static string BuildSafeFileName(string fileName, Uri uri)
        {
            var raw = !string.IsNullOrWhiteSpace(fileName)
                ? fileName
                : Path.GetFileName(uri.LocalPath);

            if (string.IsNullOrWhiteSpace(raw) || raw == "/")
            {
                raw = $"file_{HashUrl(uri.AbsoluteUri)[..8]}";
            }

            // 清洗非法字符（保留中文与常见符号）
            var invalid = Path.GetInvalidFileNameChars();
            var cleaned = new StringBuilder(raw.Length);
            foreach (var c in raw)
            {
                cleaned.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            }

            var name = cleaned.ToString();

            // 无扩展名时从 URL 末段回退补全（Windows 按扩展名匹配关联程序，缺失会打不开）
            if (string.IsNullOrWhiteSpace(Path.GetExtension(name)))
            {
                var urlExt = Path.GetExtension(uri.LocalPath);
                if (!string.IsNullOrWhiteSpace(urlExt))
                {
                    name += urlExt;
                }
            }

            return $"{HashUrl(uri.AbsoluteUri)[..8]}_{name}";
        }

        private static string HashUrl(string url)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(url));
            return Convert.ToHexString(bytes);
        }
    }
}
