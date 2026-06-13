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
    private readonly HashSet<ulong> _subscribedUsers = new();

    private IIMGatewayObserver? _observerReference;
    private GatewayObserver? _observer;

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

        _observer = new GatewayObserver(_logger, _connectionManager, _adapter);
        _observerReference = _clusterClient.CreateObjectReference<IIMGatewayObserver>(_observer);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        var observerReference = _observerReference;
        if (observerReference == null)
        {
            return;
        }

        foreach (var userId in _subscribedUsers.ToList())
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

            if (_subscribedUsers.Add(userId))
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

        if (_observerReference != null && _subscribedUsers.Remove(userId))
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