using System.Net;
using System.Net.Sockets;

using Horizon.IM.Core;
using Horizon.IM.Core.Adapters;
using Horizon.IM.Core.Handlers;
using Horizon.IM.Gateway.Configuration;
using Horizon.IM.Gateway.Network;
using Horizon.IM.Gateway.Services;
using Horizon.IM.Message;
using Horizon.IM.Message.Enums;
using Horizon.IM.Message.Network;
using Horizon.Orleans.Interface;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Orleans;
using Orleans.TestingHost;

using TouchSocket.Core;
using TouchSocket.Sockets;

using TouchSocketTcpClient = TouchSocket.Sockets.TcpClient;

namespace Horizon.IM.Gateway.Tests;

public sealed class IMNetworkServerConnectionBindingTests : IAsyncLifetime
{
    private readonly IMMessageAdapter _adapter = new();

    private TestCluster? _cluster;
    private IMGatewayPushService? _pushService;
    private IMConnectionManager? _connectionManager;
    private IMNetworkServer? _server;
    private int _port;

    public async Task InitializeAsync()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<IMGatewayTestSiloConfigurator>();

        _cluster = builder.Build();
        await _cluster.DeployAsync();

        var clusterClient = _cluster.GrainFactory as IClusterClient;
        if (clusterClient == null)
        {
            throw new InvalidOperationException("TestCluster GrainFactory 未实现 IClusterClient。");
        }

        _connectionManager = new IMConnectionManager(NullLogger<IMConnectionManager>.Instance);
        _pushService = new IMGatewayPushService(
            NullLogger<IMGatewayPushService>.Instance,
            clusterClient,
            _connectionManager,
            _adapter);
        await _pushService.StartAsync();

        _port = ReserveTcpPort();
        var networkOptions = new NetworkOptions
        {
            IpAddress = "127.0.0.1",
            TcpPort = _port
        };

        _server = new IMNetworkServer(
            NullLogger<IMNetworkServer>.Instance,
            ConsoleLogger.Default,
            new StaticOptionsMonitor<NetworkOptions>(networkOptions),
            _connectionManager,
            _pushService,
            CreateHandlers(clusterClient),
            _adapter);

        await _server.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_server != null)
        {
            await _server.StopAsync();
        }

        if (_pushService != null)
        {
            await _pushService.StopAsync();
        }

        if (_cluster != null)
        {
            await _cluster.StopAllSilosAsync();
            _cluster.Dispose();
        }
    }

    [Fact]
    public async Task PrivateChatRequest_ReturnsAck_AndReceiverHeartbeatClientGetsPush()
    {
        var (senderId, receiverId) = NewDistinctUserIds();

        Assert.NotNull(_cluster);

        var senderGrain = _cluster!.GrainFactory.GetGrain<IIMUserGrain>(IMGrainKey.ToGuid(senderId));
        var receiverGrain = _cluster.GrainFactory.GetGrain<IIMUserGrain>(IMGrainKey.ToGuid(receiverId));

        await senderGrain.SyncSessionAsync("Sender", string.Empty, IMOnlineStatus.Online);
        await receiverGrain.SyncSessionAsync("Receiver", string.Empty, IMOnlineStatus.Online);
        await MakeFriendsAsync(senderGrain, receiverGrain, senderId, receiverId);

        await using var receiverClient = new GatewayTestClient(_adapter, _port);
        await receiverClient.ConnectAsync();
        await receiverClient.SendHeartbeatAsync(receiverId, "Receiver", IMOnlineStatus.Online, TimeSpan.FromSeconds(5));

        await using var senderClient = new GatewayTestClient(_adapter, _port);
        await senderClient.ConnectAsync();

        var ack = await senderClient.SendRequestAsync<IMChatAckMessage>(
            new IMPrivateChatSendMessage
            {
                SenderId = senderId,
                SenderName = "Sender",
                SenderAvatar = string.Empty,
                ReceiverId = receiverId,
                Content = "network ack works",
                ContentType = IMContentType.Text,
                ClientMessageId = Guid.NewGuid().ToString("N"),
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            },
            senderId,
            "Sender",
            IMOnlineStatus.Online,
            TimeSpan.FromSeconds(5));

        Assert.False(string.IsNullOrWhiteSpace(ack.AckedMessageId));

        var pushPacket = await receiverClient.WaitForPacketAsync(
            packet => packet.Header.MessageType == IMMessageType.PrivateChatNotify,
            TimeSpan.FromSeconds(5));

        var notify = Assert.IsType<IMPrivateChatNotifyMessage>(pushPacket.Body);
        Assert.Equal(senderId, notify.SenderId);
        Assert.Equal(receiverId, notify.ReceiverId);
        Assert.Equal("network ack works", notify.Content);
        Assert.Equal(ack.AckedMessageId, notify.ServerMessageId);
    }

    [Fact]
    public async Task TransientRequestConnection_DoesNotDisplaceActiveHeartbeatSubscription()
    {
        var (senderId, receiverId) = NewDistinctUserIds();

        Assert.NotNull(_cluster);

        var senderGrain = _cluster!.GrainFactory.GetGrain<IIMUserGrain>(IMGrainKey.ToGuid(senderId));
        var receiverGrain = _cluster.GrainFactory.GetGrain<IIMUserGrain>(IMGrainKey.ToGuid(receiverId));

        await senderGrain.SyncSessionAsync("Sender", string.Empty, IMOnlineStatus.Online);
        await receiverGrain.SyncSessionAsync("Receiver", string.Empty, IMOnlineStatus.Online);
        await MakeFriendsAsync(senderGrain, receiverGrain, senderId, receiverId);

        await using var receiverNotificationClient = new GatewayTestClient(_adapter, _port);
        await receiverNotificationClient.ConnectAsync();
        await receiverNotificationClient.SendHeartbeatAsync(receiverId, "Receiver", IMOnlineStatus.Online, TimeSpan.FromSeconds(5));

        await using (var transientClient = new GatewayTestClient(_adapter, _port))
        {
            await transientClient.ConnectAsync();
            var contacts = await transientClient.SendRequestAsync<IMContactListResponse>(
                new IMContactListRequest
                {
                    UserId = receiverId,
                    Offset = 0,
                    Limit = 20,
                    OnlineOnly = false
                },
                receiverId,
                "Receiver",
                IMOnlineStatus.Online,
                TimeSpan.FromSeconds(5));

            Assert.Contains(contacts.Contacts, contact => contact.UserId == senderId);
        }

        var serverMessageId = await senderGrain.SendPrivateMessageAsync(new IMPrivateChatSendMessage
        {
            SenderId = senderId,
            SenderName = "Sender",
            SenderAvatar = string.Empty,
            ReceiverId = receiverId,
            Content = "subscription survives transient request",
            ContentType = IMContentType.Text,
            ClientMessageId = Guid.NewGuid().ToString("N"),
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        });

        Assert.False(string.IsNullOrWhiteSpace(serverMessageId));

        var pushPacket = await receiverNotificationClient.WaitForPacketAsync(
            packet => packet.Header.MessageType == IMMessageType.PrivateChatNotify,
            TimeSpan.FromSeconds(5));

        var notify = Assert.IsType<IMPrivateChatNotifyMessage>(pushPacket.Body);
        Assert.Equal(serverMessageId, notify.ServerMessageId);
        Assert.Equal("subscription survives transient request", notify.Content);
    }

    [Fact]
    public async Task HeartbeatClient_CanReconnect_AndReceiveSubsequentPush()
    {
        var (senderId, receiverId) = NewDistinctUserIds();

        Assert.NotNull(_cluster);

        var senderGrain = _cluster!.GrainFactory.GetGrain<IIMUserGrain>(IMGrainKey.ToGuid(senderId));
        var receiverGrain = _cluster.GrainFactory.GetGrain<IIMUserGrain>(IMGrainKey.ToGuid(receiverId));

        await senderGrain.SyncSessionAsync("Sender", string.Empty, IMOnlineStatus.Online);
        await receiverGrain.SyncSessionAsync("Receiver", string.Empty, IMOnlineStatus.Online);
        await MakeFriendsAsync(senderGrain, receiverGrain, senderId, receiverId);

        await using (var firstClient = new GatewayTestClient(_adapter, _port))
        {
            await firstClient.ConnectAsync();
            await firstClient.SendHeartbeatAsync(receiverId, "Receiver", IMOnlineStatus.Online, TimeSpan.FromSeconds(5));

            var activeConnectionId = _connectionManager?.GetConnectionByUser(receiverId)?.Id;
            Assert.False(string.IsNullOrWhiteSpace(activeConnectionId));

            await _connectionManager!.RemoveConnectionAsync(activeConnectionId!);
            await _pushService!.HandleUserDisconnectedAsync(receiverId);
        }

        await using var reconnectedClient = new GatewayTestClient(_adapter, _port);
        await reconnectedClient.ConnectAsync();
        await reconnectedClient.SendHeartbeatAsync(receiverId, "Receiver", IMOnlineStatus.Online, TimeSpan.FromSeconds(5));

        var serverMessageId = await senderGrain.SendPrivateMessageAsync(new IMPrivateChatSendMessage
        {
            SenderId = senderId,
            SenderName = "Sender",
            SenderAvatar = string.Empty,
            ReceiverId = receiverId,
            Content = "reconnect restores push",
            ContentType = IMContentType.Text,
            ClientMessageId = Guid.NewGuid().ToString("N"),
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        });

        Assert.False(string.IsNullOrWhiteSpace(serverMessageId));

        var pushPacket = await reconnectedClient.WaitForPacketAsync(
            packet => packet.Header.MessageType == IMMessageType.PrivateChatNotify,
            TimeSpan.FromSeconds(5));

        var notify = Assert.IsType<IMPrivateChatNotifyMessage>(pushPacket.Body);
        Assert.Equal(serverMessageId, notify.ServerMessageId);
        Assert.Equal("reconnect restores push", notify.Content);
    }

    private IEnumerable<IIMMessageHandler> CreateHandlers(IClusterClient clusterClient)
    {
        var handlerLogger = NullLogger<IMMessageHandlerBase>.Instance;
        return new IIMMessageHandler[]
        {
            new IMChatHandler(handlerLogger, clusterClient, _adapter),
            new IMContactHandler(handlerLogger, clusterClient, _adapter),
            new IMGroupHandler(handlerLogger, clusterClient, _adapter),
            new IMGatewayHandler(handlerLogger, clusterClient, _adapter)
        };
    }

    private static async Task MakeFriendsAsync(
        IIMUserGrain senderGrain,
        IIMUserGrain receiverGrain,
        ulong senderId,
        ulong receiverId)
    {
        var addResponse = await senderGrain.AddContactAsync(new IMContactAddRequest
        {
            UserId = senderId,
            TargetUserId = receiverId,
            VerifyMessage = "hello",
            RequesterName = "Sender",
            RequesterAvatar = string.Empty,
            Source = "integration-test"
        });

        Assert.True(addResponse.Success);
        Assert.Equal(IMContactRelation.PendingRequest, addResponse.Relation);

        var accepted = await receiverGrain.HandleContactRequestAsync(senderId, accept: true);
        Assert.True(accepted);
    }

    private static (ulong first, ulong second) NewDistinctUserIds()
    {
        var first = IMGrainKey.NewUInt64Id();
        var second = IMGrainKey.NewUInt64Id();
        while (second == first)
        {
            second = IMGrainKey.NewUInt64Id();
        }

        return (first, second);
    }

    private static int ReserveTcpPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private sealed class StaticOptionsMonitor<TOptions> : IOptionsMonitor<TOptions>
        where TOptions : class
    {
        public StaticOptionsMonitor(TOptions currentValue)
        {
            CurrentValue = currentValue;
        }

        public TOptions CurrentValue { get; }

        public TOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<TOptions, string?> listener) => null;
    }

    private sealed class GatewayTestClient : IAsyncDisposable
    {
        private readonly IMMessageAdapter _adapter;
        private readonly int _port;
        private readonly TouchSocketTcpClient _client = new();
        private readonly object _syncRoot = new();
        private readonly List<IMMessagePacket> _receivedPackets = new();
        private readonly List<PacketWaiter> _waiters = new();

        public GatewayTestClient(IMMessageAdapter adapter, int port)
        {
            _adapter = adapter;
            _port = port;
        }

        public async Task ConnectAsync()
        {
            _client.Received = OnReceivedAsync;
            await _client
                .SetupAsync(new TouchSocketConfig()
                    .SetRemoteIPHost($"127.0.0.1:{_port}")
                    .SetTcpDataHandlingAdapter(() => new IMMessageAdapter()))
                .ConfigureAwait(false);

            await _client.ConnectAsync().ConfigureAwait(false);
        }

        public Task<IMHeartbeatResponse> SendHeartbeatAsync(
            ulong userId,
            string nickname,
            IMOnlineStatus onlineStatus,
            TimeSpan timeout)
        {
            return SendRequestAsync<IMHeartbeatResponse>(
                new IMHeartbeatMessage
                {
                    UserId = userId,
                    ClientTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                },
                userId,
                nickname,
                onlineStatus,
                timeout);
        }

        public async Task<TResponse> SendRequestAsync<TResponse>(
            IMMessageUnion message,
            ulong userId,
            string nickname,
            IMOnlineStatus onlineStatus,
            TimeSpan timeout)
            where TResponse : IMMessageUnion
        {
            var packet = _adapter.CreatePacket(message, userId);
            packet.Header.RequireResponse = true;
            packet.Header.ExtensionData[IMSessionHeaderKeys.Nickname] = nickname;
            packet.Header.ExtensionData[IMSessionHeaderKeys.Avatar] = string.Empty;
            packet.Header.ExtensionData[IMSessionHeaderKeys.OnlineStatus] = onlineStatus.ToString();

            var responseTask = WaitForPacketAsync(
                candidate => candidate.Header.IsResponse
                    && string.Equals(candidate.Header.ResponseToMessageId, packet.Header.MessageId, StringComparison.Ordinal),
                timeout);

            await _client.SendAsync(_adapter.PackPacket(packet)).ConfigureAwait(false);
            var responsePacket = await responseTask.ConfigureAwait(false);

            if (responsePacket.Body is IMErrorMessage error)
            {
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(error.Message) ? error.Details : error.Message);
            }

            return Assert.IsType<TResponse>(responsePacket.Body);
        }

        public async Task<IMMessagePacket> WaitForPacketAsync(Func<IMMessagePacket, bool> predicate, TimeSpan timeout)
        {
            PacketWaiter waiter;
            lock (_syncRoot)
            {
                var index = _receivedPackets.FindIndex(packet => predicate(packet));
                if (index >= 0)
                {
                    var packet = _receivedPackets[index];
                    _receivedPackets.RemoveAt(index);
                    return packet;
                }

                waiter = new PacketWaiter(predicate);
                _waiters.Add(waiter);
            }

            return await waiter.CompletionSource.Task.WaitAsync(timeout).ConfigureAwait(false);
        }

        public ValueTask DisposeAsync()
        {
            _client.Dispose();
            return ValueTask.CompletedTask;
        }

        private Task OnReceivedAsync(ITcpClient client, ReceivedDataEventArgs e)
        {
            if (e.RequestInfo is not IMMessageInfo { Packet: { } packet })
            {
                return Task.CompletedTask;
            }

            TaskCompletionSource<IMMessagePacket>? completionSource = null;
            lock (_syncRoot)
            {
                var waiterIndex = _waiters.FindIndex(waiter => waiter.Predicate(packet));
                if (waiterIndex >= 0)
                {
                    completionSource = _waiters[waiterIndex].CompletionSource;
                    _waiters.RemoveAt(waiterIndex);
                }
                else
                {
                    _receivedPackets.Add(packet);
                }
            }

            completionSource?.TrySetResult(packet);
            return Task.CompletedTask;
        }

        private sealed class PacketWaiter
        {
            public PacketWaiter(Func<IMMessagePacket, bool> predicate)
            {
                Predicate = predicate;
                CompletionSource = new TaskCompletionSource<IMMessagePacket>(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            public Func<IMMessagePacket, bool> Predicate { get; }

            public TaskCompletionSource<IMMessagePacket> CompletionSource { get; }
        }
    }
}