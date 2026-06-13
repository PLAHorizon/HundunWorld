using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Horizon.Game.GengDi.Data.Repositories;
using Horizon.Game.GengDi.Enums;
using Horizon.Game.GengDi.Models;

namespace Horizon.Game.GengDi.Core.Services
{
    /// <summary>
    /// <see cref="UpdateService.ApplyPendingUpdatesAsync"/> 过程的综合进度：同时描述整体完成率、
    /// 当前正在下载的条目进度以及当前正在安装的条目进度。
    /// </summary>
    public sealed class UpdateProgress
    {
        public GameInfo Game { get; set; }
        public int AppliedCount { get; set; }
        public int TotalCount { get; set; }
        public string CurrentVersion { get; set; }

        /// <summary>
        /// 当前子步骤所属阶段：Download / Install。
        /// </summary>
        public UpdatePhase Phase { get; set; }

        /// <summary>0..100 的当前子步骤进度（下载% 或 安装%）。</summary>
        public double StepPercent { get; set; }

        /// <summary>0..100 的整体进度 = (已完成条目 + 当前子步骤 0..1) / 总条目 * 100。</summary>
        public double OverallPercent =>
            TotalCount <= 0
                ? 0
                : Math.Min(100.0, (AppliedCount + Math.Clamp(StepPercent, 0, 100) / 100.0) / TotalCount * 100.0);
    }

    public enum UpdatePhase
    {
        Idle,
        Download,
        Install,
        Completed,
        Failed,
        Cancelled
    }

    /// <summary>
    /// 服务端推送的更新通知来源；真实生产可由 IM Gateway / WebSocket 实现。
    /// 本项目先以接口和一个内存实现存在，便于上层订阅。
    /// </summary>
    public interface IUpdateNotificationSource
    {
        event EventHandler<string /*gameId*/> UpdateAvailable;
    }

    /// <summary>
    /// 更新通知的内存默认实现，用于开发和测试。真实实现可替换之。
    /// </summary>
    public sealed class InMemoryUpdateNotificationSource : IUpdateNotificationSource
    {
        public event EventHandler<string> UpdateAvailable;

        public void Publish(string gameId)
        {
            UpdateAvailable?.Invoke(this, gameId);
        }
    }

    /// <summary>
    /// 游戏更新服务。负责：
    /// <list type="bullet">
    ///   <item>向服务端拉取指定游戏的最新版本 / 更新链</item>
    ///   <item>与本地 <see cref="PendingUpdateRepository"/> 对比：若本地列表为空或不一致，则替换为服务端列表</item>
    ///   <item>逐条下载 + 解压，逐条 <see cref="PendingUpdateRepository.MarkApplied"/></item>
    ///   <item>提供整体 + 子步骤的复合进度，供 UI 绑定</item>
    /// </list>
    /// 服务端接口目前为 stub，可通过 <c>AppSettings</c> 配置，真实接口就绪后替换 URL 即可。
    /// </summary>
    public class UpdateService
    {
        private static readonly Lazy<UpdateService> _instance = new(() => new UpdateService());
        public static UpdateService Instance => _instance.Value;

        public event EventHandler<UpdateProgress> UpdateProgressChanged;

        private readonly HttpClient _httpClient;
        private readonly PendingUpdateRepository _repository;
        private readonly DownloadService _downloadService;
        private readonly InstallService _installService;

        private CancellationTokenSource _activeCts;

        public UpdateService()
        {
            _httpClient = new HttpClient(SslConfiguration.CreateStandardHandler());
            _repository = new PendingUpdateRepository();
            _downloadService = DownloadService.Instance;
            _installService = InstallService.Instance;
        }

        /// <summary>
        /// 从服务端拉取某游戏的最新版本信息与更新链。若配置 URL 为空或服务端不可达，返回空列表。
        /// </summary>
        public async Task<List<PendingUpdateItem>> CheckAsync(GameInfo game, CancellationToken cancellationToken = default)
        {
            if (game == null) throw new ArgumentNullException(nameof(game));

            var settings = await AppSettingsService.Instance.LoadSettingsAsync().ConfigureAwait(false);
            var baseUrl = settings.UpdateCheckUrl;
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                return new List<PendingUpdateItem>();
            }

            try
            {
                var url = $"{baseUrl.TrimEnd('/')}/{Uri.EscapeDataString(game.Id ?? string.Empty)}?from={Uri.EscapeDataString(game.Version ?? string.Empty)}";
                using var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    return new List<PendingUpdateItem>();
                }

                var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                var dto = JsonSerializer.Deserialize<UpdateChainDto>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                game.LatestVersion = dto?.LatestVersion ?? game.LatestVersion;
                game.LastUpdateCheckUtc = DateTime.UtcNow;

                var items = new List<PendingUpdateItem>();
                if (dto?.Items == null) return items;

                for (var i = 0; i < dto.Items.Count; i++)
                {
                    var it = dto.Items[i];
                    items.Add(new PendingUpdateItem
                    {
                        Id = PendingUpdateItem.BuildId(game.Id, it.ToVersion),
                        GameId = game.Id,
                        FromVersion = it.FromVersion ?? string.Empty,
                        ToVersion = it.ToVersion ?? string.Empty,
                        DownloadUrl = it.DownloadUrl,
                        PackageHash = it.PackageHash,
                        OrderIndex = i,
                        CreatedUtc = DateTime.UtcNow
                    });
                }

                return items;
            }
            catch (Exception)
            {
                // 网络错误：返回空列表，让上层跳过更新流程而不是抛出阻塞 UI。
                return new List<PendingUpdateItem>();
            }
        }

        /// <summary>
        /// 执行完整的"更新"流程：
        /// <list type="number">
        ///   <item>读取本地待更新列表。</item>
        ///   <item>若本地为空或与服务端不一致，则以服务端列表替换本地列表（事务式）。</item>
        ///   <item>按 OrderIndex 依次下载 + 解压（已 Applied 的自动跳过）。</item>
        ///   <item>每完成一条 <see cref="PendingUpdateRepository.MarkApplied"/>，并更新 <see cref="GameInfo.Version"/>。</item>
        ///   <item>所有条目完成后清除 <see cref="GameInfo.HasPendingUpdate"/> 并清理本地列表。</item>
        /// </list>
        /// </summary>
        public async Task<bool> ApplyPendingUpdatesAsync(GameInfo game, IProgress<UpdateProgress> progress, CancellationToken cancellationToken = default)
        {
            if (game == null) throw new ArgumentNullException(nameof(game));

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _activeCts = cts;
            try
            {
                var serverItems = await CheckAsync(game, cts.Token).ConfigureAwait(false);
                var localItems = await ClientAsyncDispatcher
                    .RunLiteDbAsync(() => _repository.GetByGameIdOrdered(game.Id))
                    .ConfigureAwait(false);

                if (localItems.Count == 0 || !ChainsMatch(localItems, serverItems))
                {
                    await ClientAsyncDispatcher
                        .RunLiteDbAsync(() => _repository.ReplaceListForGame(game.Id, serverItems))
                        .ConfigureAwait(false);
                    localItems = serverItems;
                }

                if (localItems.Count == 0)
                {
                    // 本地/服务端都没有更新，则确保 HasPendingUpdate 为 false。
                    game.HasPendingUpdate = false;
                    return true;
                }

                var total = localItems.Count;
                var appliedCount = localItems.Count(i => i.Applied);

                for (var i = 0; i < localItems.Count; i++)
                {
                    cts.Token.ThrowIfCancellationRequested();

                    var item = localItems[i];
                    if (item.Applied)
                    {
                        continue;
                    }

                    // --- 下载阶段 ---
                    var savePath = _downloadService.BuildDefaultPackageSavePath($"{game.Name}-{item.ToVersion}");
                    var request = new DownloadRequest
                    {
                        GameId = game.Id,
                        GameName = game.Name,
                        DownloadUrl = item.DownloadUrl,
                        SavePath = savePath,
                        Kind = DownloadTaskKind.GameUpdate,
                        TargetVersion = item.ToVersion,
                        ExpectedHash = item.PackageHash
                    };

                    EventHandler<DownloadTask> onProgress = (_, t) =>
                    {
                        progress?.Report(new UpdateProgress
                        {
                            Game = game,
                            AppliedCount = appliedCount,
                            TotalCount = total,
                            CurrentVersion = item.ToVersion,
                            Phase = UpdatePhase.Download,
                            StepPercent = t.Progress
                        });
                        UpdateProgressChanged?.Invoke(this, new UpdateProgress
                        {
                            Game = game,
                            AppliedCount = appliedCount,
                            TotalCount = total,
                            CurrentVersion = item.ToVersion,
                            Phase = UpdatePhase.Download,
                            StepPercent = t.Progress
                        });
                    };

                    _downloadService.DownloadProgressChanged += onProgress;
                    DownloadTask finishedTask;
                    try
                    {
                        var downloadTask = await _downloadService.StartDownloadAsync(request).ConfigureAwait(false);
                        finishedTask = await _downloadService.WaitForCompletionAsync(downloadTask.Id, cts.Token).ConfigureAwait(false);
                    }
                    finally
                    {
                        _downloadService.DownloadProgressChanged -= onProgress;
                    }

                    if (finishedTask == null || finishedTask.Status != DownloadStatus.Completed)
                    {
                        RaiseFailed(progress, game, item, appliedCount, total);
                        return false;
                    }

                    // --- 安装阶段 ---
                    EventHandler<InstallProgress> onInstall = (_, ip) =>
                    {
                        progress?.Report(new UpdateProgress
                        {
                            Game = game,
                            AppliedCount = appliedCount,
                            TotalCount = total,
                            CurrentVersion = item.ToVersion,
                            Phase = UpdatePhase.Install,
                            StepPercent = ip.Percent
                        });
                        UpdateProgressChanged?.Invoke(this, new UpdateProgress
                        {
                            Game = game,
                            AppliedCount = appliedCount,
                            TotalCount = total,
                            CurrentVersion = item.ToVersion,
                            Phase = UpdatePhase.Install,
                            StepPercent = ip.Percent
                        });
                    };

                    _installService.InstallProgressChanged += onInstall;
                    bool ok;
                    try
                    {
                        ok = await _installService.InstallFromPackageAsync(game, finishedTask.SavePath, cts.Token).ConfigureAwait(false);
                    }
                    finally
                    {
                        _installService.InstallProgressChanged -= onInstall;
                    }

                    if (!ok)
                    {
                        RaiseFailed(progress, game, item, appliedCount, total);
                        return false;
                    }

                    await ClientAsyncDispatcher.RunLiteDbAsync(() => _repository.MarkApplied(item.Id)).ConfigureAwait(false);
                    appliedCount++;
                    game.Version = item.ToVersion;
                }

                // 全部应用完成：清理 pending 列表并清除待更新角标。
                await ClientAsyncDispatcher.RunLiteDbAsync(() => _repository.ClearForGame(game.Id)).ConfigureAwait(false);
                game.HasPendingUpdate = false;
                game.IsUpdatable = false;
                game.UpdateVersion = null;

                progress?.Report(new UpdateProgress
                {
                    Game = game,
                    AppliedCount = appliedCount,
                    TotalCount = total,
                    Phase = UpdatePhase.Completed,
                    StepPercent = 100
                });

                return true;
            }
            catch (OperationCanceledException)
            {
                progress?.Report(new UpdateProgress
                {
                    Game = game,
                    Phase = UpdatePhase.Cancelled
                });
                return false;
            }
            finally
            {
                _activeCts = null;
            }
        }

        /// <summary>
        /// 取消正在进行的更新流程。已经 Applied 的条目保留，不会回滚。
        /// </summary>
        public void CancelActive()
        {
            try
            {
                _activeCts?.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // already disposed
            }
        }

        private static bool ChainsMatch(List<PendingUpdateItem> local, List<PendingUpdateItem> server)
        {
            if (local.Count != server.Count) return false;
            for (var i = 0; i < local.Count; i++)
            {
                var l = local[i];
                var s = server[i];
                if (!string.Equals(l.ToVersion, s.ToVersion, StringComparison.Ordinal)) return false;
                if (!string.Equals(l.FromVersion ?? string.Empty, s.FromVersion ?? string.Empty, StringComparison.Ordinal)) return false;
                if (!string.Equals(l.DownloadUrl, s.DownloadUrl, StringComparison.Ordinal)) return false;
                if (!string.Equals(l.PackageHash ?? string.Empty, s.PackageHash ?? string.Empty, StringComparison.Ordinal)) return false;
            }
            return true;
        }

        private void RaiseFailed(IProgress<UpdateProgress> progress, GameInfo game, PendingUpdateItem item, int applied, int total)
        {
            progress?.Report(new UpdateProgress
            {
                Game = game,
                AppliedCount = applied,
                TotalCount = total,
                CurrentVersion = item?.ToVersion,
                Phase = UpdatePhase.Failed
            });
        }

        private sealed class UpdateChainDto
        {
            public string LatestVersion { get; set; }
            public List<UpdateItemDto> Items { get; set; }
        }

        private sealed class UpdateItemDto
        {
            public string FromVersion { get; set; }
            public string ToVersion { get; set; }
            public string DownloadUrl { get; set; }
            public string PackageHash { get; set; }
        }
    }
}
