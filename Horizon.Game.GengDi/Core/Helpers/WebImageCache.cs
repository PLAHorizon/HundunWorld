using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;

namespace Horizon.Game.GengDi.Core.Helpers
{
    /// <summary>
    /// 商品图异步加载缓存 — 内存缓存 + 同 URL 并发去重，失败返回 null 由 UI 走降级方案。
    /// </summary>
    public static class WebImageCache
    {
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(12) };
        private static readonly Dictionary<string, Bitmap> Cache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Task<Bitmap?>> Inflight = new(StringComparer.OrdinalIgnoreCase);
        private static readonly object Sync = new();

        /// <summary>异步取图；url 为空、下载或解码失败时返回 null。</summary>
        public static Task<Bitmap?> GetAsync(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return Task.FromResult<Bitmap?>(null);

            lock (Sync)
            {
                if (Cache.TryGetValue(url!, out var cached))
                    return Task.FromResult<Bitmap?>(cached);

                if (Inflight.TryGetValue(url!, out var running))
                    return running;

                var task = DownloadAsync(url!);
                Inflight[url!] = task;
                return task;
            }
        }

        /// <summary>同步取已缓存的图，未命中返回 false（用于避免闪烁）。</summary>
        public static bool TryGetCached(string? url, out Bitmap? bitmap)
        {
            bitmap = null;
            if (string.IsNullOrWhiteSpace(url))
                return false;

            lock (Sync)
                return Cache.TryGetValue(url!, out bitmap);
        }

        private static async Task<Bitmap?> DownloadAsync(string url)
        {
            try
            {
                var bytes = await Http.GetByteArrayAsync(url).ConfigureAwait(false);
                using var stream = new MemoryStream(bytes, writable: false);
                var bitmap = new Bitmap(stream);

                lock (Sync)
                    Cache[url] = bitmap;

                return bitmap;
            }
            catch
            {
                return null;
            }
            finally
            {
                lock (Sync)
                    Inflight.Remove(url);
            }
        }
    }
}
