using System.Collections.Concurrent;

using Horizon.IM.Core;
using Horizon.IM.Core.Adapters;
using Horizon.IM.Message;
using Horizon.IM.Message.Enums;
using Horizon.Orleans.Interface;

using Microsoft.Extensions.Logging;

using Orleans;

namespace Horizon.IM.Gateway.Services;

public class IMGatewayPushService
{
    private readonly ILogger<IMGatewayPushService> _logger;
    private readonly IClusterClient _clusterClient;
    private readonly IIMConnectionManager _connectionManager;
    private readonly IMMessageAdapter _adapter;

    private readonly Guid _subscriptionId = Guid.NewGuid();

    // ⚠️ 并发安全（2026-09-20 修复）：本服务的方法**直接从 TouchSocket 接收回调调用**
    //    （IMNetworkServer.OnDataReceived → EnsureUserSessionAsync），而接收回调运行在
    //    线程池上，可被**多连接 / 多消息同时进入**。因此这里必须是并发集合。
    //
    //    原实现是普通 `HashSet<ulong>`，并发 Add/Remove 下会抛：
    //        InvalidOperationException: Operations that change non-concurrent collections
    //        must have exclusive access. A concurrent update was performed on this
    //        collection and corrupted its state.
    //    更糟的是它**不一定每次都抛**——也可能静默损坏内部桶结构，表现为丢条目或遍历死循环。
    //
    //    语义映射（与 HashSet 逐一对齐，均为返回 bool 的"是否发生变更"）：
    //        HashSet.Add(x)      → TryAdd(x, 0)
    //        HashSet.Remove(x)   → TryRemove(x, out _)
    //        HashSet.Clear()     → Clear()          （自身线程安全）
    //        HashSet.ToList()    → Keys.ToArray()   （必须先快照，不能边遍历边改）
    //    值类型用 byte 占位：ConcurrentDictionary 无 Set 语义，用"键即成员"表达集合。
    private readonly ConcurrentDictionary<ulong, byte> _subscribedUsers = new();

    // 观察者引用跨线程读写（接收回调读、Start/Stop 写），volatile 保证可见性：
    // 否则工作线程可能长期读到缓存的 null，导致 EnsureUserSessionAsync 直接 return、推送静默失效。
    private volatile IIMGatewayObserver? _observerReference;
    private GatewayObserver? _observer;

    // 保护 StartAsync 中"检查 _observerReference 为空 → 创建并赋值"的原子性。
    // 该路径同样会被并发进入：若两个线程同时通过检查，会各自创建一个 Observer，
    // 其中一个引用被覆盖后**无人释放也无人引用**，而 grain 可能恰好订阅到那个被丢弃的引用
    // → 该用户的主动推送永久失效（且无任何异常）。
    private readonly object _observerLock = new();

    public IMGatewayPushService(
        ILogger<IMGatewayPushService> logger,
        IClusterClient clusterClient,
        IIMConnectionManager connectionManager,
        IMMessageAdapter adapter)
    {
        _logger = logger;
        _clusterClient = clusterClient;
        _connectionManager = connectionManager;
        _adapter = adapter;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_observerReference != null)
        {
            return;
        }

        // 检查-创建必须原子：并发首包会同时进入本方法（见 _observerLock 注释）。
        // 锁内无 await，不存在锁跨续体的问题。
        lock (_observerLock)
        {
            if (_observerReference != null)
            {
                return;
            }

            _observer = new GatewayObserver(_logger, _connectionManager, _adapter);
            _observerReference = _clusterClient.CreateObjectReference<IIMGatewayObserver>(_observer);
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        var observerReference = _observerReference;
        if (observerReference == null)
        {
            return;
        }

        // 必须先取快照再遍历：关闭期间仍可能有在途接收回调并发 Add/Remove，
        // 直接遍历 ConcurrentDictionary.Keys 虽不抛异常，但会看到变动中的集合（重复/遗漏）。
        foreach (var userId in _subscribedUsers.Keys.ToArray())
        {
            try
            {
                await _clusterClient
                    .GetGrain<IIMUserGrain>(IMGrainKey.ToGuid(userId))
                    .UnsubscribeGatewayAsync(_subscriptionId)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "停止 IM 推送服务时取消订阅失败: UserId={UserId}", userId);
            }
        }

        _subscribedUsers.Clear();
        _observerReference = null;
        _observer = null;

        _clusterClient.DeleteObjectReference<IIMGatewayObserver>(observerReference);
    }

    public async Task EnsureUserSessionAsync(
        ulong userId,
        string nickname,
        string avatar,
        IMOnlineStatus onlineStatus,
        CancellationToken cancellationToken = default)
    {
        if (userId == 0)
        {
            return;
        }

        await StartAsync(cancellationToken).ConfigureAwait(false);

        var userGrain = _clusterClient.GetGrain<IIMUserGrain>(IMGrainKey.ToGuid(userId));
        try
        {
            await userGrain.SyncSessionAsync(nickname, avatar, onlineStatus).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "同步 IM 用户会话被瞬时取消，本次跳过并等待后续消息重试: UserId={UserId}", userId);
            return;
        }

        if (_observerReference == null)
        {
            return;
        }

        try
        {
            await userGrain.SubscribeGatewayAsync(_subscriptionId, _observerReference).ConfigureAwait(false);

            if (_subscribedUsers.TryAdd(userId, 0))
            {
                _logger.LogDebug("已为用户订阅 IM 主动推送: UserId={UserId}", userId);
            }
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "订阅 IM 主动推送被瞬时取消，本次跳过并等待后续消息重试: UserId={UserId}", userId);
        }
    }

    public async Task HandleUserDisconnectedAsync(ulong userId, CancellationToken cancellationToken = default)
    {
        if (userId == 0)
        {
            return;
        }

        var userGrain = _clusterClient.GetGrain<IIMUserGrain>(IMGrainKey.ToGuid(userId));

        try
        {
            await userGrain.SyncSessionAsync(string.Empty, string.Empty, IMOnlineStatus.Offline).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "同步用户离线状态失败: UserId={UserId}", userId);
        }

        if (_observerReference != null && _subscribedUsers.TryRemove(userId, out _))
        {
            try
            {
                await userGrain.UnsubscribeGatewayAsync(_subscriptionId).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "用户断开时取消推送订阅失败: UserId={UserId}", userId);
            }
        }
    }

    private sealed class GatewayObserver : IIMGatewayObserver
    {
        private readonly ILogger _logger;
        private readonly IIMConnectionManager _connectionManager;
        private readonly IMMessageAdapter _adapter;

        public GatewayObserver(
            ILogger logger,
            IIMConnectionManager connectionManager,
            IMMessageAdapter adapter)
        {
            _logger = logger;
            _connectionManager = connectionManager;
            _adapter = adapter;
        }

        public async Task OnMessageAsync(ulong userId, IMMessageUnion message)
        {
            ArgumentNullException.ThrowIfNull(message);

            var packet = _adapter.CreatePacket(message, userId);
            var payload = _adapter.PackPacket(packet);
            var sent = await _connectionManager.SendToUserAsync(userId, payload).ConfigureAwait(false);

            if (!sent)
            {
                _logger.LogDebug("用户当前无可用连接，跳过 IM 主动推送: UserId={UserId}, MessageType={MessageType}", userId, message.Type);
            }
        }
    }
}