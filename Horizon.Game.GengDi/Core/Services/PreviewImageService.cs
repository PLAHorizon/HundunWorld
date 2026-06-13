using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;

namespace Horizon.Game.GengDi.Core.Services
{
    public sealed class PreviewImageService
    {
        private static readonly Lazy<PreviewImageService> LazyInstance = new(() => new PreviewImageService());
        private static readonly HttpClient HttpClient = CreateHttpClient();

        // 缓存上限：超过后驱逐最早插入的 50% 条目
        private const int MaxCacheSize = 200;
        private const int EvictBatchSize = 100;

        private readonly ConcurrentDictionary<string, Task<Bitmap>> _cache = new(StringComparer.OrdinalIgnoreCase);
        // 按插入顺序记录键，用于 FIFO 驱逐
        private readonly ConcurrentQueue<string> _insertionOrder = new();

        public static PreviewImageService Instance => LazyInstance.Value;

        public Task<Bitmap> LoadAsync(string source)
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                return Task.FromResult<Bitmap>(null);
            }

            if (_cache.TryGetValue(source, out var cached))
            {
                return cached;
            }

            // 新条目入队并检查是否需要驱逐
            _insertionOrder.Enqueue(source);
            if (_cache.Count > MaxCacheSize)
            {
                TrimCache();
            }

            return _cache.GetOrAdd(source, LoadCoreAsync);
        }

        /// <summary>
        /// 清空整个缓存（例如退出登录时调用，释放头像等会话级图片内存）。
        /// </summary>
        public void ClearCache()
        {
            _cache.Clear();
            while (_insertionOrder.TryDequeue(out _)) { }
        }

        private void TrimCache()
        {
            var removed = 0;
            while (removed < EvictBatchSize && _insertionOrder.TryDequeue(out var key))
            {
                _cache.TryRemove(key, out _);
                removed++;
            }
        }

        private async Task<Bitmap> LoadCoreAsync(string source)
        {
            try
            {
                if (source.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
                {
                    var dataBytes = DecodeDataUriBytes(source);
                    if (dataBytes?.Length > 0)
                    {
                        using var dataStream = new MemoryStream(dataBytes);
                        return new Bitmap(dataStream);
                    }
                }

                if (File.Exists(source))
                {
                    await using var fileStream = File.OpenRead(source);
                    return new Bitmap(fileStream);
                }

                if (Uri.TryCreate(source, UriKind.Absolute, out var uri))
                {
                    if (uri.IsFile && File.Exists(uri.LocalPath))
                    {
                        await using var fileUriStream = File.OpenRead(uri.LocalPath);
                        return new Bitmap(fileUriStream);
                    }

                    if (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
                    {
                        using var request = CreateImageRequest(uri);
                        using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
                        response.EnsureSuccessStatusCode();
                        var bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                        using var memoryStream = new MemoryStream(bytes);
                        return new Bitmap(memoryStream);
                    }
                }
            }
            catch
            {
                _cache.TryRemove(source, out _);
            }

            return null;
        }

        internal static byte[] DecodeDataUriBytes(string source)
        {
            if (string.IsNullOrWhiteSpace(source) || !source.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var commaIndex = source.IndexOf(',');
            if (commaIndex < 0)
            {
                return null;
            }

            var metadata = source.Substring(5, commaIndex - 5);
            var payload = source[(commaIndex + 1)..];

            try
            {
                return metadata.Contains(";base64", StringComparison.OrdinalIgnoreCase)
                    ? Convert.FromBase64String(payload)
                    : Encoding.UTF8.GetBytes(Uri.UnescapeDataString(payload));
            }
            catch
            {
                return null;
            }
        }

        internal static HttpRequestMessage CreateImageRequest(Uri uri)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36");
            request.Headers.TryAddWithoutValidation("Accept", "image/avif,image/webp,image/apng,image/svg+xml,image/*,*/*;q=0.8");
            request.Headers.TryAddWithoutValidation("Accept-Language", "zh-CN,zh;q=0.9,en;q=0.8");

            if (!string.IsNullOrWhiteSpace(uri.Host) && Uri.TryCreate($"{uri.Scheme}://{uri.Host}/", UriKind.Absolute, out var referrerUri))
            {
                request.Headers.Referrer = referrerUri;
            }

            return request;
        }

        private static HttpClient CreateHttpClient()
        {
            return new HttpClient(SslConfiguration.CreateStandardHandler())
            {
                Timeout = TimeSpan.FromSeconds(8)
            };
        }
    }
}