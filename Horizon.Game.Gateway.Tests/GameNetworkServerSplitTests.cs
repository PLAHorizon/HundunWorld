using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Horizon.Game.Core;
using Horizon.Game.Core.Interfaces;
using Horizon.Game.Core.Sim.Server;
using Horizon.Game.Gateway.Configuration;
using Horizon.Game.Gateway.Network;
using Horizon.Game.Gateway.Services;
using Horizon.Game.Message;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Orleans;
using TouchSocket.Core;
using TouchSocket.Sockets;
using Xunit;
using TouchSocketTcpClient = TouchSocket.Sockets.TcpClient;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 阶段 3 — GameNetworkServer 拆分后的端到端冒烟测试。<br/>
/// 用真实 TcpService（GameNetworkServer 门面）+ 真实 ConnectionManager +
/// 拆分组件（ConnectionLifecycleManager / ClientPacketPipeline）启动网关，
/// 以真实 TouchSocket 客户端建立连接并发 LoginRequest，验证：连接登记、
/// 数据流水线 Stage 1-6、handler 路由（MessageHandlerBase 生产路径）、响应下发、
/// 响应拦截（令牌写入连接）在拆分后全链路保持完整。
/// </summary>
public sealed class GameNetworkServerSplitTests : IAsyncLifetime
{
    private const string AuthToken = "split-test-token";

    private readonly HorizonMessageAdapter _adapter = new();
    private readonly int _port = ReserveTcpPort();

    private ConnectionManager? _connectionManager;
    private GameNetworkServer? _server;

    public Task InitializeAsync()
    {
        var gatewayOptions = new GatewayOptions();
        var gatewayMonitor = new StaticOptionsMonitor<GatewayOptions>(gatewayOptions);
        var networkMonitor = new StaticOptionsMonitor<NetworkOptions>(new NetworkOptions
        {
            IpAddress = "127.0.0.1",
            TcpPort = _port,
        });

        _connectionManager = new ConnectionManager(
            NullLogger<ConnectionManager>.Instance, gatewayMonitor);

        // PlayerDespawnScheduler 依赖 Orleans IClusterClient——仅构造、不调用，Moq 即可。
        var clusterClient = new Mock<IClusterClient>().Object;
        var presenceStore = new Mock<ICharacterPresenceStore>().Object;
        var fingerprintService = new Mock<ICharacterFingerprintService>().Object;
        var despawnScheduler = new PlayerDespawnScheduler(
            clusterClient, _connectionManager, presenceStore, fingerprintService,
            NullLogger<PlayerDespawnScheduler>.Instance);
        var presenceRefresh = new PresenceRefreshService(
            presenceStore, NullLogger<PresenceRefreshService>.Instance);

        var lifecycle = new ConnectionLifecycleManager(
            NullLogger<ConnectionLifecycleManager>.Instance,
            networkMonitor,
            _connectionManager,
            despawnScheduler,
            presenceRefresh,
            _adapter);

        var pipeline = new ClientPacketPipeline(
            NullLogger<ClientPacketPipeline>.Instance,
            gatewayMonitor,
            _connectionManager,
            new IMessageHandler[]
            {
                new EchoLoginHandler(NullLogger<MessageHandlerBase>.Instance, clusterClient, _adapter),
            },
            despawnScheduler,
            presenceRefresh,
            _adapter,
            lifecycle);

        _server = new GameNetworkServer(
            NullLogger<GameNetworkServer>.Instance,
            ConsoleLogger.Default,
            networkMonitor,
            lifecycle,
            pipeline);

        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (_server != null)
        {
            await _server.StopAsync();
        }
    }

    [Fact]
    public async Task LoginRequest_FullPipeline_ReturnsResponse_AndInterceptsToken()
    {
        Assert.NotNull(_server);
        await _server!.StartAsync();

        await using var client = new GatewayTestClient(_adapter, _port);
        await client.ConnectAsync();

        var loginRequest = new LoginRequest
        {
            AccountName = "split-test-user",
            Password = "secret",
        };
        var packet = _adapter.CreateHorizonMessage(loginRequest);
        packet.Header.RequireResponse = true;

        var responseTask = client.WaitForPacketAsync(
            candidate => candidate.Header.IsResponse
                && string.Equals(candidate.Header.ResponseToMessageId, packet.Header.MessageId, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));

        await client.SendAsync(_adapter.PackPacket(packet));
        var responsePacket = await responseTask;

        var loginResponse = Assert.IsType<LoginResponse>(responsePacket.Body);
        Assert.True(loginResponse.IsSuccess);
        Assert.Equal(AuthToken, loginResponse.AuthToken);

        // Stage 6 响应拦截：连接上应写入返回的令牌。
        var connections = _connectionManager!.GetAllConnections();
        var connection = Assert.Single(connections);
        Assert.Equal(AuthToken, connection.AuthToken);
    }

    [Fact]
    public async Task SecondLoginRequest_ReusesRegisteredConnection()
    {
        Assert.NotNull(_server);
        await _server!.StartAsync();

        await using var client = new GatewayTestClient(_adapter, _port);
        await client.ConnectAsync();

        var loginRequest = new LoginRequest { AccountName = "u", Password = "p" };

        var packet = _adapter.CreateHorizonMessage(loginRequest);
        packet.Header.RequireResponse = true;
        var responseTask = client.WaitForPacketAsync(
            candidate => candidate.Header.IsResponse
                && string.Equals(candidate.Header.ResponseToMessageId, packet.Header.MessageId, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        await client.SendAsync(_adapter.PackPacket(packet));
        await responseTask;

        // 第二次请求：连接应已被复用（不新增连接实例）。
        var connectionsBefore = _connectionManager!.GetAllConnections();
        var before = Assert.Single(connectionsBefore);

        var packet2 = _adapter.CreateHorizonMessage(loginRequest);
        packet2.Header.RequireResponse = true;
        var responseTask2 = client.WaitForPacketAsync(
            candidate => candidate.Header.IsResponse
                && string.Equals(candidate.Header.ResponseToMessageId, packet2.Header.MessageId, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        await client.SendAsync(_adapter.PackPacket(packet2));
        await responseTask2;

        var connectionsAfter = _connectionManager!.GetAllConnections();
        var after = Assert.Single(connectionsAfter);
        Assert.Equal(before.ConnectionId, after.ConnectionId);
    }

    /// <summary>生产路径测试 handler：继承 MessageHandlerBase，处理 LoginRequest。</summary>
    private sealed class EchoLoginHandler : MessageHandlerBase
    {
        public EchoLoginHandler(
            ILogger<MessageHandlerBase> logger,
            IClusterClient clusterClient,
            HorizonMessageAdapter adapter)
            : base(logger, clusterClient, adapter)
        {
        }

        public override List<MessageType> MessageTypes => new() { MessageType.LoginRequest };

        public override ServiceType ServiceType => ServiceType.Account;

        public override Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> RouteHandlerAsync(HorizonMessagePacket message)
        {
            var response = new LoginResponse
            {
                IsSuccess = true,
                AuthToken = AuthToken,
                Message = "ok",
            };
            return Task.FromResult<(bool, HorizonMessagePacket)>(
                (true, _adapter.CreateHorizonMessage(response)));
        }
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

    private static int ReserveTcpPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    /// <summary>真实 TouchSocket 测试客户端（仿 IM.Gateway.Tests 模式）。</summary>
    private sealed class GatewayTestClient : IAsyncDisposable
    {
        private readonly HorizonMessageAdapter _adapter;
        private readonly int _port;
        private readonly TouchSocketTcpClient _client = new();
        private readonly object _syncRoot = new();
        private readonly List<HorizonMessagePacket> _receivedPackets = new();
        private readonly List<PacketWaiter> _waiters = new();

        public GatewayTestClient(HorizonMessageAdapter adapter, int port)
        {
            _adapter = adapter;
            _port = port;
        }

        public Task ConnectAsync()
        {
            _client.Received = OnReceivedAsync;
            return _client
                .SetupAsync(new TouchSocketConfig()
                    .SetRemoteIPHost($"127.0.0.1:{_port}")
                    .SetTcpDataHandlingAdapter(() => new HorizonMessageAdapter()))
                .ContinueWith(_ => _client.ConnectAsync(), TaskScheduler.Default)
                .Unwrap();
        }

        public Task SendAsync(byte[] frame) => _client.SendAsync(frame);

        public async Task<HorizonMessagePacket> WaitForPacketAsync(
            Func<HorizonMessagePacket, bool> predicate, TimeSpan timeout)
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
            if (e.RequestInfo is not HorizonMessageInfo { Packet: { } packet })
            {
                return Task.CompletedTask;
            }

            TaskCompletionSource<HorizonMessagePacket>? completionSource = null;
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
            public PacketWaiter(Func<HorizonMessagePacket, bool> predicate)
            {
                Predicate = predicate;
                CompletionSource = new TaskCompletionSource<HorizonMessagePacket>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
            }

            public Func<HorizonMessagePacket, bool> Predicate { get; }

            public TaskCompletionSource<HorizonMessagePacket> CompletionSource { get; }
        }
    }
}
