using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Horizon.Game.GengDi.Data.Repositories;
using Horizon.Game.GengDi.Enums;
using Horizon.Game.GengDi.Models;

namespace Horizon.Game.GengDi.Core.Services
{
    /// <summary>
    /// 游戏 / 客户端下载服务。负责：
    /// <list type="bullet">
    ///   <item>断点续传与 <c>.partial</c> 中间文件管理</item>
    ///   <item>线程安全的 <see cref="CancellationTokenSource"/> 管理（ConcurrentDictionary + finally Dispose）</item>
    ///   <item>可选的 SHA-256 完整性校验</item>
    ///   <item>进度 / 完成 / 失败 / 取消事件</item>
    ///   <item>应用启动时将遗留 <see cref="DownloadStatus.Downloading"/> 任务转为 <see cref="DownloadStatus.Paused"/>，以便 UI 恢复</item>
    /// </list>
    /// </summary>
    public class DownloadService
    {
        private static readonly Lazy<DownloadService> _instance = new(() => new DownloadService());

        /// <summary>
        /// 进程内共享的单例（<see cref="ActiveOperationViewModel"/> 及 <c>GameService</c> 均引用）。
        /// </summary>
        public static DownloadService Instance => _instance.Value;

        private readonly DownloadTaskRepository _repository;
        private readonly HttpClient _httpClient;
        private readonly ConcurrentDictionary<string, CancellationTokenSource> _cancellationTokens;
        private int _maxConcurrentDownloads;
        private long _downloadSpeedLimit;

        public event EventHandler<DownloadTask> DownloadProgressChanged;
        public event EventHandler<DownloadTask> DownloadCompleted;
        public event EventHandler<DownloadTask> DownloadFailed;
        public event EventHandler<DownloadTask> DownloadCancelled;

        public DownloadService()
        {
            _repository = new DownloadTaskRepository();
            _httpClient = new HttpClient(SslConfiguration.CreateStandardHandler());
            _cancellationTokens = new ConcurrentDictionary<string, CancellationTokenSource>();
            _maxConcurrentDownloads = 2;
            _downloadSpeedLimit = 0; // 0 = no limit
        }

        public int MaxConcurrentDownloads
        {
            get => _maxConcurrentDownloads;
            set => _maxConcurrentDownloads = Math.Max(1, value);
        }

        public long DownloadSpeedLimit
        {
            get => _downloadSpeedLimit;
            set => _downloadSpeedLimit = Math.Max(0, value);
        }

        /// <summary>
        /// 启动一次下载。完成 / 失败 / 取消会通过事件和返回 Task 同时通知调用方。
        /// URL 及保存路径必须由调用方完成校验，<see cref="DownloadService"/> 不再重复解析。
        /// </summary>
        public async Task<DownloadTask> StartDownloadAsync(DownloadRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (string.IsNullOrWhiteSpace(request.DownloadUrl)) throw new ArgumentException("DownloadUrl 不能为空", nameof(request));
            if (string.IsNullOrWhiteSpace(request.SavePath)) throw new ArgumentException("SavePath 不能为空", nameof(request));

            var task = new DownloadTask
            {
                Id = Guid.NewGuid().ToString(),
                GameId = request.GameId,
                GameName = request.GameName,
                TotalSize = 0,
                DownloadedSize = 0,
                Status = DownloadStatus.Pending,
                Progress = 0,
                Speed = 0,
                StartTime = DateTime.Now,
                EndTime = null,
                Kind = request.Kind,
                SourceUrl = request.DownloadUrl,
                SavePath = request.SavePath,
                TargetVersion = request.TargetVersion,
                ExpectedHash = request.ExpectedHash,
                ErrorMessage = string.Empty
            };

            await ClientAsyncDispatcher.RunLiteDbAsync(() => _repository.Add(task)).ConfigureAwait(false);

            // 在后台运行；调用方如需等待完成，可订阅 DownloadCompleted 事件。
            _ = DownloadFileAsync(task, allowResume: false);
            return task;
        }

        /// <summary>
        /// 兼容旧签名：保留给尚未迁移的调用方（<c>DownloadsView</c> 列表等）。
        /// </summary>
        public Task<DownloadTask> StartDownloadAsync(string gameId, string gameName, string downloadUrl, string savePath)
        {
            var resolved = string.IsNullOrWhiteSpace(savePath) ? BuildDefaultPackageSavePath(gameName) : savePath;
            return StartDownloadAsync(new DownloadRequest
            {
                GameId = gameId,
                GameName = gameName,
                DownloadUrl = downloadUrl,
                SavePath = resolved,
                Kind = DownloadTaskKind.GameInstall
            });
        }

        /// <summary>
        /// 暂停下载。保留 <c>.partial</c> 文件以便后续续传；状态置为 <see cref="DownloadStatus.Paused"/>。
        /// </summary>
        public async Task PauseDownloadAsync(string taskId)
        {
            if (!_cancellationTokens.TryRemove(taskId, out var cts))
            {
                // 任务可能已完成或已暂停；仅尝试将 DB 状态刷新为 Paused。
                var existing = await ClientAsyncDispatcher.RunLiteDbAsync(() => _repository.GetById(taskId)).ConfigureAwait(false);
                if (existing != null && existing.Status == DownloadStatus.Downloading)
                {
                    existing.Status = DownloadStatus.Paused;
                    existing.Speed = 0;
                    await ClientAsyncDispatcher.RunLiteDbAsync(() => _repository.Update(existing)).ConfigureAwait(false);
                    DownloadProgressChanged?.Invoke(this, existing);
                }
                return;
            }

            try
            {
                cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // race: task 已结束并处置了 cts，忽略。
            }
            finally
            {
                cts.Dispose();
            }

            var task = await ClientAsyncDispatcher.RunLiteDbAsync(() => _repository.GetById(taskId)).ConfigureAwait(false);
            if (task != null && task.Status != DownloadStatus.Completed && task.Status != DownloadStatus.Failed)
            {
                task.Status = DownloadStatus.Paused;
                task.Speed = 0;
                await ClientAsyncDispatcher.RunLiteDbAsync(() => _repository.Update(task)).ConfigureAwait(false);
                DownloadProgressChanged?.Invoke(this, task);
            }
        }

        /// <summary>
        /// 恢复下载。使用任务自身持久化的 URL / SavePath（而非调用方再次传入），确保续传正确。
        /// </summary>
        public async Task<bool> ResumeDownloadAsync(string taskId)
        {
            var task = await ClientAsyncDispatcher.RunLiteDbAsync(() => _repository.GetById(taskId)).ConfigureAwait(false);
            if (task == null || task.Status != DownloadStatus.Paused)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(task.SourceUrl) || string.IsNullOrWhiteSpace(task.SavePath))
            {
                task.Status = DownloadStatus.Failed;
                task.EndTime = DateTime.Now;
                await ClientAsyncDispatcher.RunLiteDbAsync(() => _repository.Update(task)).ConfigureAwait(false);
                DownloadFailed?.Invoke(this, task);
                return false;
            }

            task.Status = DownloadStatus.Downloading;
            await ClientAsyncDispatcher.RunLiteDbAsync(() => _repository.Update(task)).ConfigureAwait(false);
            _ = DownloadFileAsync(task, allowResume: true);
            return true;
        }

        /// <summary>
        /// 旧签名兼容：忽略传入的 URL / SavePath，改用任务自身持久化的值。
        /// </summary>
        public Task ResumeDownloadAsync(string taskId, string downloadUrl, string savePath)
        {
            return ResumeDownloadAsync(taskId);
        }

        /// <summary>
        /// 取消下载。<paramref name="purgePartial"/> 为 true（默认）时会删除 <c>.partial</c> 文件和数据库任务记录，
        /// 实现"清除已下载数据"语义；UI 应在调用前提示用户确认。
        /// </summary>
        public async Task CancelDownloadAsync(string taskId, bool purgePartial = true)
        {
            DownloadTask task = await ClientAsyncDispatcher.RunLiteDbAsync(() => _repository.GetById(taskId)).ConfigureAwait(false);

            if (_cancellationTokens.TryRemove(taskId, out var cts))
            {
                try { cts.Cancel(); } catch (ObjectDisposedException) { }
                cts.Dispose();
            }

            if (task == null)
            {
                return;
            }

            task.Status = DownloadStatus.Cancelled;
            task.Speed = 0;
            task.EndTime = DateTime.Now;

            if (purgePartial)
            {
                await ClientAsyncDispatcher.RunBackgroundAsync(() =>
                {
                    TryDeleteFile(task.SavePath + ".partial");
                    TryDeleteFile(task.SavePath);
                }).ConfigureAwait(false);

                await ClientAsyncDispatcher.RunLiteDbAsync(() => _repository.Delete(taskId)).ConfigureAwait(false);
            }
            else
            {
                await ClientAsyncDispatcher.RunLiteDbAsync(() => _repository.Update(task)).ConfigureAwait(false);
            }

            DownloadCancelled?.Invoke(this, task);
        }

        public string GetDownloadPackageDirectory()
        {
            return Path.Combine(AppSettingsService.Instance.GetResolvedInstallDirectory(), "Packages");
        }

        /// <summary>
        /// 删除指定任务记录（仅适用于已完成 / 已取消 / 已失败的任务）。
        /// 同时尝试清理残留的 .partial 文件。
        /// </summary>
        public async Task DeleteTaskAsync(string taskId)
        {
            var task = await ClientAsyncDispatcher.RunLiteDbAsync(() => _repository.GetById(taskId)).ConfigureAwait(false);
            if (task == null) return;

            await ClientAsyncDispatcher.RunBackgroundAsync(() =>
            {
                TryDeleteFile(task.SavePath + ".partial");
            }).ConfigureAwait(false);

            await ClientAsyncDispatcher.RunLiteDbAsync(() => _repository.Delete(taskId)).ConfigureAwait(false);
        }

        public string BuildDefaultPackageSavePath(string gameName)
        {
            var normalizedName = NormalizeFileName(string.IsNullOrWhiteSpace(gameName) ? "download-package" : gameName);
            return Path.Combine(GetDownloadPackageDirectory(), $"{normalizedName}.zip");
        }

        /// <summary>
        /// 应用启动时调用：将上次进程遗留的 <see cref="DownloadStatus.Downloading"/> 任务一律置为 <see cref="DownloadStatus.Paused"/>，
        /// 以便 UI 展示为可恢复状态，而非"看似正在下载实际已经停止"。
        /// </summary>
        public async Task RestoreAsync()
        {
            var tasks = await ClientAsyncDispatcher.RunLiteDbAsync(() => _repository.GetAll()).ConfigureAwait(false);
            foreach (var task in tasks.Where(t => t.Status == DownloadStatus.Downloading || t.Status == DownloadStatus.Pending))
            {
                task.Status = DownloadStatus.Paused;
                task.Speed = 0;
                await ClientAsyncDispatcher.RunLiteDbAsync(() => _repository.Update(task)).ConfigureAwait(false);
            }
        }

        private static readonly HashSet<int> _transientStatusCodes = new HashSet<int> { 429, 502, 503, 504 };
        private const int _maxRetries = 3;

        private async Task DownloadFileAsync(DownloadTask task, bool allowResume)
        {
            var partialPath = task.SavePath + ".partial";
            long resumeAt = 0;
            try
            {
                if (!allowResume)
                {
                    TryDeleteFile(partialPath);
                }
                else if (File.Exists(partialPath))
                {
                    resumeAt = new FileInfo(partialPath).Length;
                }

                task.Status = DownloadStatus.Downloading;
                task.DownloadedSize = resumeAt;
                task.ErrorMessage = string.Empty;
                await ClientAsyncDispatcher.RunLiteDbAsync(() => _repository.Update(task)).ConfigureAwait(false);

                var cts = new CancellationTokenSource();
                if (!_cancellationTokens.TryAdd(task.Id, cts))
                {
                    // 已在运行，视为重入。
                    cts.Dispose();
                    return;
                }

                HttpResponseMessage successResponse = null;
                for (int attempt = 0; attempt <= _maxRetries; attempt++)
                {
                    if (attempt > 0)
                    {
                        var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt - 1)); // 1s, 2s, 4s
                        await Task.Delay(delay, cts.Token).ConfigureAwait(false);
                    }

                    using (var request = new HttpRequestMessage(HttpMethod.Get, task.SourceUrl))
                    {
                        if (resumeAt > 0)
                        {
                            request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(resumeAt, null);
                        }

                        var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);

                        if (response.IsSuccessStatusCode)
                        {
                            successResponse = response;
                            break;
                        }

                        var statusCode = (int)response.StatusCode;
                        var reasonPhrase = response.ReasonPhrase ?? string.Empty;
                        response.Dispose();

                        if (!_transientStatusCodes.Contains(statusCode) || attempt == _maxRetries)
                        {
                            throw new HttpRequestException($"Response status code does not indicate success: {statusCode} ({reasonPhrase}).");
                        }
                    }
                }

                using (successResponse)
                {
                    var contentLength = successResponse.Content.Headers.ContentLength ?? 0;
                    var contentRangeLength = successResponse.Content.Headers.ContentRange?.Length ?? 0;
                    task.TotalSize = contentRangeLength > 0
                        ? contentRangeLength
                        : contentLength > 0 ? contentLength + resumeAt : 0;
                    await ClientAsyncDispatcher.RunLiteDbAsync(() => _repository.Update(task)).ConfigureAwait(false);

                    var directory = Path.GetDirectoryName(partialPath);
                    if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    using (var fileStream = new FileStream(partialPath, resumeAt > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None))
                    using (var contentStream = await successResponse.Content.ReadAsStreamAsync(cts.Token).ConfigureAwait(false))
                    {
                        var buffer = new byte[81920];
                        var totalRead = resumeAt;
                        var lastUpdateTime = DateTime.UtcNow;
                        var lastBytesRead = totalRead;
                        var lastPersistTime = DateTime.UtcNow;

                        while (true)
                        {
                            var bytesRead = await contentStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cts.Token).ConfigureAwait(false);
                            if (bytesRead == 0)
                            {
                                break;
                            }

                            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cts.Token).ConfigureAwait(false);
                            totalRead += bytesRead;

                            var now = DateTime.UtcNow;
                            var timeElapsed = (now - lastUpdateTime).TotalSeconds;
                            if (timeElapsed >= 0.5)
                            {
                                task.Speed = (totalRead - lastBytesRead) / timeElapsed;
                                lastUpdateTime = now;
                                lastBytesRead = totalRead;
                            }

                            task.DownloadedSize = totalRead;
                            task.Progress = task.TotalSize > 0 ? (double)totalRead / task.TotalSize * 100 : 0;
                            DownloadProgressChanged?.Invoke(this, task);

                            // 节流写库：至少 1 s 持久化一次，避免 I/O 风暴。
                            if ((now - lastPersistTime).TotalSeconds >= 1)
                            {
                                await ClientAsyncDispatcher.RunLiteDbAsync(() => _repository.Update(task)).ConfigureAwait(false);
                                lastPersistTime = now;
                            }

                            if (_downloadSpeedLimit > 0)
                            {
                                // 简单节流：按目标速率近似 sleep。
                                var expected = (double)totalRead / _downloadSpeedLimit;
                                var elapsed = (now - task.StartTime.ToUniversalTime()).TotalSeconds;
                                var wait = expected - elapsed;
                                if (wait > 0)
                                {
                                    try { await Task.Delay(TimeSpan.FromSeconds(Math.Min(wait, 1)), cts.Token).ConfigureAwait(false); }
                                    catch (OperationCanceledException) { throw; }
                                }
                            }
                        }
                    }
                }

                // 下载完毕：可选哈希校验 + 原子 rename
                if (!string.IsNullOrEmpty(task.ExpectedHash))
                {
                    var actual = await ComputeSha256Async(partialPath, cts.Token).ConfigureAwait(false);
                    if (!string.Equals(actual, task.ExpectedHash, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException($"下载包哈希校验失败（期望 {task.ExpectedHash}，实际 {actual}）。");
                    }
                }

                ValidatePackageIfNeeded(task.SavePath, partialPath);

                if (File.Exists(task.SavePath))
                {
                    TryDeleteFile(task.SavePath);
                }

                File.Move(partialPath, task.SavePath);

                task.Status = DownloadStatus.Completed;
                task.EndTime = DateTime.Now;
                task.Speed = 0;
                task.Progress = 100;
                task.DownloadedSize = task.TotalSize;
                task.ErrorMessage = string.Empty;
                await ClientAsyncDispatcher.RunLiteDbAsync(() => _repository.Update(task)).ConfigureAwait(false);
                DownloadCompleted?.Invoke(this, task);
            }
            catch (OperationCanceledException)
            {
                // 由 Pause / Cancel 触发，状态已在相应方法里写入。这里仅回退速度。
                task.Speed = 0;
            }
            catch (Exception ex)
            {
                task.Status = DownloadStatus.Failed;
                task.EndTime = DateTime.Now;
                task.Speed = 0;
                task.ErrorMessage = ex.Message;
                await ClientAsyncDispatcher.RunLiteDbAsync(() => _repository.Update(task)).ConfigureAwait(false);
                DownloadFailed?.Invoke(this, task);
            }
            finally
            {
                if (_cancellationTokens.TryRemove(task.Id, out var cts))
                {
                    cts.Dispose();
                }
            }
        }

        public async Task<List<DownloadTask>> GetAllTasksAsync()
        {
            return await ClientAsyncDispatcher.RunLiteDbAsync(() => _repository.GetAll()).ConfigureAwait(false);
        }

        public async Task<List<DownloadTask>> GetActiveTasksAsync()
        {
            return await ClientAsyncDispatcher.RunLiteDbAsync(() => _repository.GetActiveTasks()).ConfigureAwait(false);
        }

        /// <summary>
        /// 等待指定任务结束（Completed / Failed / Cancelled），用于编排下载→安装/更新流程。
        /// </summary>
        public Task<DownloadTask> WaitForCompletionAsync(string taskId, CancellationToken cancellationToken = default)
        {
            var tcs = new TaskCompletionSource<DownloadTask>(TaskCreationOptions.RunContinuationsAsynchronously);

            EventHandler<DownloadTask> onCompleted = null;
            EventHandler<DownloadTask> onFailed = null;
            EventHandler<DownloadTask> onCancelled = null;

            void Detach()
            {
                DownloadCompleted -= onCompleted;
                DownloadFailed -= onFailed;
                DownloadCancelled -= onCancelled;
            }

            onCompleted = (_, t) => { if (t.Id == taskId) { Detach(); tcs.TrySetResult(t); } };
            onFailed = (_, t) => { if (t.Id == taskId) { Detach(); tcs.TrySetResult(t); } };
            onCancelled = (_, t) => { if (t.Id == taskId) { Detach(); tcs.TrySetResult(t); } };

            DownloadCompleted += onCompleted;
            DownloadFailed += onFailed;
            DownloadCancelled += onCancelled;

            if (cancellationToken.CanBeCanceled)
            {
                cancellationToken.Register(() => { Detach(); tcs.TrySetCanceled(cancellationToken); });
            }

            return tcs.Task;
        }

        private static async Task<string> ComputeSha256Async(string path, CancellationToken ct)
        {
            using var stream = File.OpenRead(path);
            using var sha = SHA256.Create();
            var hash = await Task.Run(() => sha.ComputeHash(stream), ct).ConfigureAwait(false);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        private static void ValidatePackageIfNeeded(string savePath, string downloadedPath)
        {
            if (!string.Equals(Path.GetExtension(savePath), ".zip", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            try
            {
                using var archive = ZipFile.OpenRead(downloadedPath);
                _ = archive.Entries.Count;
            }
            catch (InvalidDataException ex)
            {
                throw new InvalidDataException("下载包已损坏或不完整，请重新下载。", ex);
            }
        }

        private static void TryDeleteFile(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return;
            }

            try
            {
                File.Delete(path);
            }
            catch
            {
                // 吞掉：文件可能被 AV 扫描暂时占用；下一次调用会重试。
            }
        }

        private static string NormalizeFileName(string fileName)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            return new string(fileName.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray());
        }
    }
}
