using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;

namespace Horizon.Game.GengDi.Core.Services
{
    /// <summary>
    /// 语音消息播放器（客户端单实例）：
    /// - 远程语音(m4a)先下载到本地临时缓存（自签 https 经 SslConfiguration 处理），再以 NAudio 播放；
    /// - 同一时刻仅一条在播：切换播放时自动停止上一条（经回调复位旧消息卡片状态）；
    /// - 播放自然结束 / 播放失败 / 主动停止均触发 onStopped 回调。
    /// </summary>
    public sealed class VoiceMessagePlayer : IDisposable
    {
        private static readonly Lazy<VoiceMessagePlayer> LazyInstance = new(() => new VoiceMessagePlayer());
        public static VoiceMessagePlayer Instance => LazyInstance.Value;

        private static readonly HttpClient Http = new(SslConfiguration.CreateStandardHandler())
        {
            Timeout = TimeSpan.FromSeconds(20)
        };

        private readonly object _sync = new();
        private WaveOutEvent _output;
        private MediaFoundationReader _reader;
        private string _activeKey;
        private Action _activeStoppedCallback;
        private bool _disposed;

        /// <summary>指定消息是否正在播放</summary>
        public bool IsPlaying(string key)
        {
            lock (_sync)
            {
                return _activeKey != null && string.Equals(_activeKey, key, StringComparison.Ordinal);
            }
        }

        /// <summary>
        /// 开始播放：先停掉当前在播的（回调复位），下载（或命中缓存）后播放。
        /// 返回 false 表示地址无效或下载失败（此时不会触发 onStopped）。
        /// </summary>
        public async Task<bool> PlayAsync(string url, string key, Action onStopped)
        {
            if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            StopInternal();

            string localPath;
            try
            {
                localPath = await GetLocalCacheAsync(url).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VoiceMessagePlayer] 语音下载失败：{url}，{ex.Message}");
                return false;
            }

            lock (_sync)
            {
                if (_disposed)
                {
                    return false;
                }

                StopInternalLocked();

                try
                {
                    _reader = new MediaFoundationReader(localPath);
                    _output = new WaveOutEvent();
                    // 播放结束（含 Stop）统一走此处收尾，保证回调一定触发
                    _output.PlaybackStopped += (_, _) => StopInternal();
                    _activeKey = key;
                    _activeStoppedCallback = onStopped;
                    _output.Init(_reader);
                    _output.Play();
                    return true;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[VoiceMessagePlayer] 语音播放启动失败：{ex.Message}");
                    DisposePlaybackLocked();
                    _activeKey = null;
                    _activeStoppedCallback = null;
                    return false;
                }
            }
        }

        /// <summary>停止当前播放（无在播时为空操作）</summary>
        public void Stop()
        {
            StopInternal();
        }

        private void StopInternal()
        {
            Action callback;
            lock (_sync)
            {
                if (_activeKey == null)
                {
                    return;
                }

                callback = _activeStoppedCallback;
                StopLocked();
            }

            callback?.Invoke();
        }

        private void StopLocked()
        {
            // 注意：WaveOutEvent.Stop 会同步触发 PlaybackStopped → StopInternal，
            // 此处已持锁且 _activeKey 先清空，重入会直接返回，无死锁。
            _activeKey = null;
            _activeStoppedCallback = null;
            var output = _output;
            _output = null;
            output?.Stop();
            DisposePlaybackLocked();
        }

        private void StopInternalLocked()
        {
            if (_activeKey == null)
            {
                return;
            }

            StopLocked();
        }

        private void DisposePlaybackLocked()
        {
            _reader?.Dispose();
            _reader = null;
            if (_output != null)
            {
                try
                {
                    _output.Dispose();
                }
                catch
                {
                    // 释放异常忽略（设备已移除等场景）
                }

                _output = null;
            }
        }

        /// <summary>下载语音到本地缓存（已存在直接复用）；返回本地文件路径</summary>
        private static async Task<string> GetLocalCacheAsync(string url)
        {
            var cacheDir = Path.Combine(Path.GetTempPath(), "HorizonVoiceCache");
            Directory.CreateDirectory(cacheDir);

            var fileName = $"{HashUrl(url)}.m4a";
            var localPath = Path.Combine(cacheDir, fileName);
            if (File.Exists(localPath) && new FileInfo(localPath).Length > 0)
            {
                return localPath;
            }

            var bytes = await Http.GetByteArrayAsync(url).ConfigureAwait(false);
            await File.WriteAllBytesAsync(localPath, bytes).ConfigureAwait(false);
            return localPath;
        }

        private static string HashUrl(string url)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(url));
            return Convert.ToHexString(bytes)[..24];
        }

        public void Dispose()
        {
            lock (_sync)
            {
                _disposed = true;
                StopLocked();
            }
        }
    }
}
