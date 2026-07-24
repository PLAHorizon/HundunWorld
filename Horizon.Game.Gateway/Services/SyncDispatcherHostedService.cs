using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Horizon.Game.Core.Sim.Server;
using Horizon.Game.Core.World;
using Horizon.Game.Gateway.Configuration;
using Horizon.Orleans.Interface.World;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans;

namespace Horizon.Game.Gateway.Services
{
    /// <summary>
    /// 驱动 <see cref="GatewaySyncDispatcher"/> 的后台服务（P6-a 运行时连线）。<br/>
    /// 灰度关：<see cref="GatewayOptions.UseSyncPacketDispatch"/>=false 时 <see cref="StartAsync"/>
    /// 直接返回，不启动循环——保持老路径零副作用。
    /// </summary>
    /// <remarks>
    /// 工作循环：不停调用 <see cref="GatewaySyncDispatcher.RunOnceAsync"/>；dispatcher 内部用
    /// <see cref="IZoneShardFanoutSource.TryDequeueAsync"/> 自行阻塞等待，所以这里不需要额外 sleep。
    /// 当取消 token 触发时，dispatcher 返回 0，循环自然退出。
    /// </remarks>
    public sealed class SyncDispatcherHostedService : BackgroundService
    {
        /// <summary>订阅重试最大次数。</summary>
        private const int MaxRetries = 10;

        /// <summary>订阅重试初始间隔（毫秒）。</summary>
        private const int RetryBaseDelayMs = 1000;

        private readonly GatewaySyncDispatcher _dispatcher;
        private readonly IOptionsMonitor<GatewayOptions> _options;
        private readonly ILogger<SyncDispatcherHostedService> _logger;
        private readonly IClusterClient _clusterClient;
        private readonly IZoneShardFanoutObserver _fanoutObserver;
        private readonly IShardRouter _shardRouter;

        public SyncDispatcherHostedService(
            GatewaySyncDispatcher dispatcher,
            IOptionsMonitor<GatewayOptions> options,
            ILogger<SyncDispatcherHostedService> logger,
            IClusterClient clusterClient,
            IZoneShardFanoutObserver fanoutObserver,
            IShardRouter? shardRouter = null)
        {
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _clusterClient = clusterClient ?? throw new ArgumentNullException(nameof(clusterClient));
            _fanoutObserver = fanoutObserver ?? throw new ArgumentNullException(nameof(fanoutObserver));
            _shardRouter = shardRouter ?? new ZoneBasedShardRouter(1);
            _logger.LogInformation("SyncDispatcherHostedService：构造完成，等待 ExecuteAsync 启动。ShardCount={ShardCount}", _shardRouter.ShardCount);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation(
                "SyncDispatcherHostedService：ExecuteAsync 开始执行。UseSyncPacketDispatch={UseSyncPacketDispatch}",
                _options.CurrentValue.UseSyncPacketDispatch);

            if (!_options.CurrentValue.UseSyncPacketDispatch)
            {
                _logger.LogInformation(
                    "SyncDispatcherHostedService：UseSyncPacketDispatch=false，跳过 fanout 循环（保持老广播路径）。");
                return;
            }
            _dispatcher.Enabled = true;

            // 诊断：记录关键程序集加载状态
            var loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => a.GetName().Name == "Horizon.Orleans.Grains" || a.GetName().Name == "Horizon.Orleans.Interface")
                .Select(a => a.GetName().Name)
                .ToList();
            _logger.LogInformation(
                "SyncDispatcherHostedService：启动诊断 — 已加载程序集: [{Assemblies}]",
                string.Join(", ", loadedAssemblies));

            // 通过 CreateObjectReference 创建 Orleans IGrainObserver 引用
            // 直接传递 IZoneShardFanoutObserver 的 C# 对象会抛出 NotSupportedException:
            // "IGrainObserver parameters must be GrainReference or Grain"
            IZoneShardFanoutObserver? observerRef = null;
            try
            {
                observerRef = _clusterClient.CreateObjectReference<IZoneShardFanoutObserver>(_fanoutObserver);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SyncDispatcherHostedService：创建 IZoneShardFanoutObserver 引用失败，fanout 不可用。");
            }

            if (observerRef == null) return;

            // 向 ZoneShardGrain 订阅 fanout，使 BroadcastSnapshotAsync 能推送到本 gateway
            // Orleans 客户端启动后可能尚未完成 grain 元数据加载，需要重试直到成功
            var subscriptionId = Guid.NewGuid();
            var subscribed = false;
            for (var attempt = 1; attempt <= MaxRetries && !subscribed; attempt++)
            {
                try
                {
                    var zoneShard = _clusterClient.GetGrain<IZoneShardGrain>(_shardRouter.Resolve(0));
                    await zoneShard.SubscribeFanoutAsync(subscriptionId, observerRef).ConfigureAwait(false);
                    _logger.LogInformation(
                        "SyncDispatcherHostedService：已向 ZoneShard {ShardId} 订阅 fanout（SubscriptionId={SubscriptionId}，尝试={Attempt}）。",
                        _shardRouter.Resolve(0), subscriptionId, attempt);
                    subscribed = true;
                }
                catch (Exception ex) when (attempt < MaxRetries)
                {
                    _logger.LogWarning(ex,
                        "SyncDispatcherHostedService：向 ZoneShard {ShardId} 订阅 fanout 失败（尝试 {Attempt}/{MaxRetries}），{Delay}ms 后重试。",
                        _shardRouter.Resolve(0), attempt, MaxRetries, RetryBaseDelayMs * attempt);
                    try
                    {
                        await Task.Delay(RetryBaseDelayMs * attempt, stoppingToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) { break; }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "SyncDispatcherHostedService：向 ZoneShard {ShardId} 订阅 fanout 已达最大重试次数（{MaxRetries}），进入循环但可能收不到推送。",
                        _shardRouter.Resolve(0), MaxRetries);
                }
            }

            // 修复 BUG：即使初始订阅失败，也不释放 observerRef，保留以供定期重订阅使用。
            // 原实现在 !subscribed 时 DeleteObjectReference 释放 observerRef，导致引用失效，
            // 后续定期重订阅无法使用该引用，_fanoutObservers 永久为空，所有广播（包括 Despawn）
            // 永久丢失，离线角色在其他客户端永久残留。observerRef 将在 finally 块中统一释放。
            // if (!subscribed) { DeleteObjectReference(observerRef); }  // 已移除

            _logger.LogInformation(
                "SyncDispatcherHostedService：进入 ZoneShard fanout 循环。初始订阅状态: {Subscribed}",
                subscribed);

            // 修复 BUG：无论初始订阅是否成功，都启动定期重新订阅 Timer。
            // 原实现只在 subscribed==true 时创建 Timer，初始 10 次重试全部失败则永远不重试，
            // 导致 _fanoutObservers 永久为空，Despawn 广播永久丢失，离线角色永久残留。
            // 新实现：即使初始订阅失败，也每 30 秒重试一次，直到订阅成功或服务停止。
            // 这与 ZoneShardGrain.UnregisterEntityAsync 的"广播失败保留实体等待重试"机制配合，
            // 确保订阅恢复后孤儿清理定时器能重试广播 Despawn。
            var resubscribeInterval = TimeSpan.FromSeconds(30);
            var resubscribeTimer = new System.Threading.Timer(
                async _ =>
                {
                    try
                    {
                        var zoneShard = _clusterClient.GetGrain<IZoneShardGrain>(_shardRouter.Resolve(0));
                        await zoneShard.SubscribeFanoutAsync(subscriptionId, observerRef).ConfigureAwait(false);
                        if (!subscribed)
                        {
                            // 标记订阅成功，finally 中执行 Unsubscribe 清理。
                            // subscribed 是闭包捕获的局部变量，单调从 false→true，偶发竞态只导致重复日志，不影响正确性。
                            subscribed = true;
                            _logger.LogInformation(
                                "SyncDispatcherHostedService：定期重新订阅 fanout 首次成功（初始订阅失败已恢复）。SubscriptionId={SubscriptionId}",
                                subscriptionId);
                        }
                        else
                        {
                            _logger.LogDebug(
                                "SyncDispatcherHostedService：定期重新订阅 fanout 成功。SubscriptionId={SubscriptionId}",
                                subscriptionId);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex,
                            "SyncDispatcherHostedService：定期重新订阅 fanout 失败。SubscriptionId={SubscriptionId}",
                            subscriptionId);
                    }
                },
                null,
                resubscribeInterval,
                resubscribeInterval);

            try
            {
                // Task 18：多 worker 模式。Channel 多读保证每个事件只被一个 worker TryRead 成功，无丢失无重复。
                var workerCount = Math.Max(1, _options.CurrentValue.MaxDispatcherWorkers);
                var workers = new Task[workerCount];
                for (int i = 0; i < workerCount; i++)
                {
                    var workerId = i;
                    workers[i] = Task.Run(async () =>
                    {
                        while (!stoppingToken.IsCancellationRequested)
                        {
                            try
                            {
                                await _dispatcher.RunOnceAsync(stoppingToken).ConfigureAwait(false);
                            }
                            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                            {
                                break;
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "SyncDispatcherHostedService：worker {WorkerId} RunOnceAsync 异常；1s 后重试。", workerId);
                                try
                                {
                                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken).ConfigureAwait(false);
                                }
                                catch (OperationCanceledException) { break; }
                            }
                        }
                    }, stoppingToken);
                }

                try
                {
                    await Task.WhenAll(workers).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    // 正常关闭
                }
            }
            finally
            {
                resubscribeTimer?.Dispose();
                _dispatcher.Enabled = false;

                // 取消 fanout 订阅（仅在曾订阅成功时执行，避免对未订阅的 subscriptionId 调用 Unsubscribe）
                if (subscribed)
                {
                    try
                    {
                        var zoneShard = _clusterClient.GetGrain<IZoneShardGrain>(_shardRouter.Resolve(0));
                        await zoneShard.UnsubscribeFanoutAsync(subscriptionId).ConfigureAwait(false);
                        _logger.LogInformation(
                            "SyncDispatcherHostedService：已取消 ZoneShard {ShardId} fanout 订阅（SubscriptionId={SubscriptionId}）。",
                            _shardRouter.Resolve(0), subscriptionId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex,
                            "SyncDispatcherHostedService：取消 fanout 订阅时异常（可忽略）。");
                    }
                }

                // 修复 BUG：统一释放 observerRef（原实现在 !subscribed 时提前释放，导致无法重订阅）。
                // observerRef 在整个服务生命周期内被保留以供定期重订阅使用，此处服务停止时统一释放。
                if (observerRef != null)
                {
                    try { _clusterClient.DeleteObjectReference<IZoneShardFanoutObserver>(observerRef); }
                    catch { /* 忽略清理异常 */ }
                }

                _logger.LogInformation(
                    "SyncDispatcherHostedService：退出；累计 processed={Processed}, delivered={Delivered}, droppedOffline={Dropped}。",
                    _dispatcher.ProcessedEventCount,
                    _dispatcher.DeliveredPacketCount,
                    _dispatcher.DroppedOfflineCount);
            }
        }
    }
}
