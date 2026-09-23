using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Horizon.Game.Core.World;
using Horizon.Orleans.Interface.World;
using Microsoft.Extensions.Logging;
using Orleans;

namespace Horizon.Game.Gateway.Services;

/// <summary>
/// 多 Shard fanout 订阅管理器（阶段 4：多 Shard 水平扩展）。<br/>
/// 向 <c>0..ShardCount-1</c> 的全部 ZoneShardGrain 订阅 fanout，初始订阅失败自动重试
/// （<see cref="MaxRetries"/> 次、间隔 <see cref="RetryBaseDelayMs"/> × 尝试次数），
/// 并支持后台定时补订阅（恢复性重订阅，跳过已订阅 shard）。<br/>
/// 单 Shard（<see cref="IShardRouter.ShardCount"/>=1）时仅订阅 shard 0，行为与旧实现一致。<br/>
/// 订阅/退订为幂等操作（<c>ZoneShardGrain.SubscribeFanoutAsync</c> 按 subscriptionId 覆盖写、
/// observer 无 TTL 驱逐），因此重订阅跳过已订阅 shard 是安全的。
/// </summary>
/// <remarks>
/// 线程安全：<see cref="_subscribed"/> 为并发字典，主线程（初始订阅/退订）与
/// 重订阅定时器线程并发访问无竞态。
/// </remarks>
public sealed class ShardFanoutSubscriber
{
    /// <summary>订阅重试最大次数。</summary>
    public const int MaxRetries = 10;

    /// <summary>订阅重试初始间隔（毫秒）。</summary>
    public const int RetryBaseDelayMs = 1000;

    /// <summary>
    /// 启动期「Grain 类型映射尚未就绪」时的重试次数上限。
    /// <para>
    /// Client 的「接口 → Grain 类」映射由 Silo 通过 ClusterManifest 异步下发，启动后约 60 秒才可用；
    /// 而普通退避窗口（<see cref="MaxRetries"/> 次递增，合计约 45 秒）覆盖不到 ——
    /// 实测会导致 fanout 订阅在每次启动时必然失败，此后收不到 shard 推送。
    /// 因此识别出该情形后放宽到 40 次 × 3 秒 ≈ 120 秒。
    /// </para>
    /// </summary>
    public const int StartupResolutionRetries = 40;

    /// <summary>启动期「Grain 类型映射尚未就绪」时的固定重试间隔（毫秒）。</summary>
    public const int StartupResolutionDelayMs = 3000;

    /// <summary>
    /// 判定异常是否属于「Grain 类型映射尚未就绪」（Orleans Client 启动期现象）：
    /// <c>Could not find an implementation for interface ...</c>
    /// 或 <c>Unable to parse or load type "..."</c>。
    /// </summary>
    private static bool IsGrainResolutionPending(Exception ex)
    {
        for (var e = ex; e != null; e = e.InnerException)
        {
            if (e is ArgumentException
                && e.Message.Contains("Could not find an implementation", StringComparison.Ordinal))
                return true;

            if (e is TypeLoadException
                && e.Message.Contains("Unable to parse or load type", StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private readonly IClusterClient _clusterClient;
    private readonly IShardRouter _shardRouter;
    private readonly ILogger _logger;
    private readonly Guid _subscriptionId;
    private readonly IZoneShardFanoutObserver _observerRef;
    private readonly int _retryBaseDelayMs;

    /// <summary>已订阅成功的 shard（线程安全）。</summary>
    private readonly ConcurrentDictionary<long, byte> _subscribed = new();

    public ShardFanoutSubscriber(
        IClusterClient clusterClient,
        IShardRouter shardRouter,
        ILogger logger,
        Guid subscriptionId,
        IZoneShardFanoutObserver observerRef,
        int retryBaseDelayMs = RetryBaseDelayMs)
    {
        _clusterClient = clusterClient ?? throw new ArgumentNullException(nameof(clusterClient));
        _shardRouter = shardRouter ?? throw new ArgumentNullException(nameof(shardRouter));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _subscriptionId = subscriptionId;
        _observerRef = observerRef ?? throw new ArgumentNullException(nameof(observerRef));
        if (retryBaseDelayMs < 0)
            throw new ArgumentOutOfRangeException(nameof(retryBaseDelayMs), "重试基础间隔不能为负。");
        _retryBaseDelayMs = retryBaseDelayMs;
    }

    /// <summary>当前已订阅成功的 shard 列表（诊断用）。</summary>
    public IReadOnlyCollection<long> SubscribedShardIds => _subscribed.Keys.ToArray();

    /// <summary>本次订阅使用的 SubscriptionId（日志/退订用）。</summary>
    public Guid SubscriptionId => _subscriptionId;

    /// <summary>
    /// 订阅全部 shard 的 fanout（带重试），已订阅的自动跳过。
    /// </summary>
    /// <param name="cancellationToken">取消令牌（服务停止时中止重试）。</param>
    /// <returns>true = 全部 shard 已订阅；false = 仍有失败（达到最大重试次数或被取消）。</returns>
    public async Task<bool> SubscribeAllAsync(CancellationToken cancellationToken = default)
    {
        // 上限可动态放宽：一旦识别为「Grain 映射未就绪」（Orleans Client 启动期正常现象），
        // 改用 StartupResolutionRetries 次 × StartupResolutionDelayMs 的长窗口（约 120 秒），
        // 覆盖 Silo 下发 ClusterManifest 所需的时间。
        var maxAttempts = MaxRetries;
        var resolutionPending = false;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var pendingShards = EnumerateShardIds()
                .Where(s => !_subscribed.ContainsKey(s))
                .ToList();
            if (pendingShards.Count == 0) return true;

            var anyFailed = false;
            foreach (var shardId in pendingShards)
            {
                try
                {
                    var zoneShard = _clusterClient.GetGrain<IZoneShardGrain>(shardId);
                    await zoneShard.SubscribeFanoutAsync(_subscriptionId, _observerRef).ConfigureAwait(false);
                    var isFirstSubscribe = _subscribed.TryAdd(shardId, 0);
                    if (isFirstSubscribe)
                    {
                        _logger.LogInformation(
                            "ShardFanoutSubscriber：已向 ZoneShard {ShardId} 订阅 fanout（SubscriptionId={SubscriptionId}，尝试={Attempt}）。",
                            shardId, _subscriptionId, attempt);
                    }
                    else
                    {
                        _logger.LogDebug(
                            "ShardFanoutSubscriber：定期重新订阅 fanout 成功。ShardId={ShardId}, SubscriptionId={SubscriptionId}",
                            shardId, _subscriptionId);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return false;
                }
                catch (Exception ex)
                {
                    anyFailed = true;

                    var isResolutionPending = IsGrainResolutionPending(ex);
                    if (isResolutionPending && !resolutionPending)
                    {
                        resolutionPending = true;
                        maxAttempts = StartupResolutionRetries;
                        _logger.LogInformation(
                            "ShardFanoutSubscriber：检测到 Grain 类型映射尚未就绪（Orleans Client 启动期正常现象，需等待 Silo 下发 ClusterManifest），放宽重试窗口至 {MaxAttempts} 次 × {DelayMs}ms，期间逐次失败降为 Debug。",
                            StartupResolutionRetries, StartupResolutionDelayMs);
                    }

                    if (attempt == maxAttempts)
                    {
                        _logger.LogError(ex,
                            "ShardFanoutSubscriber：向 ZoneShard {ShardId} 订阅 fanout 已达最大重试次数（{MaxAttempts}），进入循环但可能收不到该 shard 的推送。",
                            shardId, maxAttempts);
                    }
                    else if (isResolutionPending)
                    {
                        // 启动期「Grain 映射未就绪」属正常现象（上方已 info 提示一次），
                        // 逐次失败降为 Debug，避免每 3 秒一条 Warning 刷屏；
                        // 订阅成功/彻底失败仍分别打 info/error（含尝试次数，便于确认恢复时机）。
                        _logger.LogDebug(ex,
                            "ShardFanoutSubscriber：向 ZoneShard {ShardId} 订阅 fanout 失败（尝试 {Attempt}/{MaxAttempts}），{DelayMs}ms 后重试。",
                            shardId, attempt, maxAttempts, StartupResolutionDelayMs);
                    }
                    else
                    {
                        _logger.LogWarning(ex,
                            "ShardFanoutSubscriber：向 ZoneShard {ShardId} 订阅 fanout 失败（尝试 {Attempt}/{MaxAttempts}），{DelayMs}ms 后重试。",
                            shardId, attempt, maxAttempts,
                            resolutionPending ? StartupResolutionDelayMs : _retryBaseDelayMs * attempt);
                    }
                }
            }

            if (!anyFailed) return true;
            if (attempt == maxAttempts) return false;

            try
            {
                var delayMs = resolutionPending ? StartupResolutionDelayMs : _retryBaseDelayMs * attempt;
                await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }

        return false;
    }

    /// <summary>
    /// 取消全部已订阅 shard 的 fanout（服务停止时调用）。
    /// </summary>
    public async Task UnsubscribeAllAsync()
    {
        foreach (var shardId in _subscribed.Keys)
        {
            try
            {
                var zoneShard = _clusterClient.GetGrain<IZoneShardGrain>(shardId);
                await zoneShard.UnsubscribeFanoutAsync(_subscriptionId).ConfigureAwait(false);
                _logger.LogInformation(
                    "ShardFanoutSubscriber：已取消 ZoneShard {ShardId} fanout 订阅（SubscriptionId={SubscriptionId}）。",
                    shardId, _subscriptionId);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex,
                    "ShardFanoutSubscriber：取消 fanout 订阅时异常（可忽略）。ShardId={ShardId}",
                    shardId);
            }
        }

        _subscribed.Clear();
    }

    /// <summary>
    /// 枚举全部可能的 shard ID：<c>0..ShardCount-1</c>。
    /// 各流处理器统一走 <see cref="IShardRouter.Resolve(long)"/>（单参数，取模路由返回 0..ShardCount-1），
    /// 因此该区间覆盖实际可达的 shard 空间；多 Zone 双参数路由当前无调用方，接入时需扩展本枚举。
    /// </summary>
    private IEnumerable<long> EnumerateShardIds()
    {
        for (var i = 0; i < _shardRouter.ShardCount; i++) yield return i;
    }
}
