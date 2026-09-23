using System;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Horizon.Game.Core;
using Horizon.Game.Core.Handlers;
using Horizon.Game.Gateway.Configuration;
using Horizon.Game.Gateway.Services;
using Horizon.Game.Message;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using Horizon.Game.Message.Sync;
using Horizon.Orleans.Interface.World;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Orleans;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 阶段 2.2 — RouteToDSByShard 灰度数据面行为测试。
/// <para>
/// 验证：UseDS 关闭 → 完全走旧 Orleans 路径（零行为变化）；开启且 shard 有已注册 DS →
/// 原始同步帧转发到 DS（字节一致）且不触碰 Orleans grain，网关返回透传 ack；无可用 DS /
/// 转发失败 → 回退 Orleans 路径。DS 侧用环回 TCP 监听器模拟（阶段 2.4 UE 进程的替身）。
/// </para>
/// </summary>
/// <remarks>
/// 本类记录 <c>horizon.sync.ds.route.*</c> 静态仪表（开关关闭回退计数/热切换路由），
/// 与 <c>DsRoutingMetricsTests</c>/<c>DsSyncDataPlaneBreakerTests</c> 同属
/// <see cref="SyncMetricsIsolatedCollection"/> 串行集合，避免并行测量串扰。
/// </remarks>
[Collection(SyncMetricsIsolatedCollection.Name)]
public class DsRoutingTests
{
    /// <summary>环回 TCP 监听器，模拟 UE5 DS 的接收端（读取 [4B BE 长度][原始帧]）。</summary>
    private sealed class FakeDs : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly TaskCompletionSource<byte[]> _receivedTcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Port { get; }
        public Task<byte[]> ReceivedFrame => _receivedTcs.Task;

        public FakeDs()
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _ = AcceptLoopAsync();
        }

        private async Task AcceptLoopAsync()
        {
            try
            {
                using var client = await _listener.AcceptTcpClientAsync();
                using var stream = client.GetStream();
                var lenBuf = new byte[4];
                await stream.ReadExactlyAsync(lenBuf);
                var len = BinaryPrimitives.ReadInt32BigEndian(lenBuf);
                var body = new byte[len];
                await stream.ReadExactlyAsync(body);
                _receivedTcs.TrySetResult(body);
            }
            catch (Exception ex)
            {
                _receivedTcs.TrySetException(ex);
            }
            finally
            {
                _listener.Stop();
            }
        }

        public ValueTask DisposeAsync()
        {
            _listener.Stop();
            return ValueTask.CompletedTask;
        }
    }

    private static SyncPacketHandler CreateHandler(
        Mock<IClusterClient> clusterClient,
        Horizon.Game.Core.Interfaces.IDSyncDataPlane? plane)
    {
        var adapter = new HorizonMessageAdapter();
        return new SyncPacketHandler(
            NullLogger<MessageHandlerBase>.Instance,
            clusterClient.Object,
            adapter,
            dsDataPlane: plane);
    }

    private static Mock<IClusterClient> CreateClusterClientWithSessionGrain(
        long characterId, Mock<IPlayerSessionGrain>? sessionGrain = null)
    {
        sessionGrain ??= new Mock<IPlayerSessionGrain>();
        var clusterClient = new Mock<IClusterClient>();
        clusterClient
            .Setup(c => c.GetGrain<IPlayerSessionGrain>(characterId, It.IsAny<string>()))
            .Returns(sessionGrain.Object);
        return clusterClient;
    }

    private static HorizonMessagePacket CreateSyncMessage(SyncPacket packet)
    {
        SyncPacketCodec.Encode(packet, out var frame, out var frameLength);
        try
        {
            var syncFrame = new SyncFrameMessage();
            syncFrame.Frame = frame.AsSpan(0, frameLength).ToArray();
            return new HorizonMessagePacket
            {
                Header = new MessageHeader
                {
                    MessageType = MessageType.SyncPacket,
                    ServiceType = ServiceType.Game,
                    CharacterId = packet is InputPacket input ? input.CharacterId : 0,
                },
                Body = syncFrame,
            };
        }
        finally
        {
            SyncPacketCodec.ReturnFrame(frame);
        }
    }

    private static SyncPacket DecodeResponse(HorizonMessagePacket response)
    {
        var syncFrame = Assert.IsType<SyncFrameMessage>(response.Body);
        return SyncPacketCodec.Decode(syncFrame.Frame.AsSpan(0, syncFrame.Frame.Length));
    }

    private static byte[] EncodeFrame(SyncPacket packet)
    {
        SyncPacketCodec.Encode(packet, out var frame, out var frameLength);
        try
        {
            return frame.AsSpan(0, frameLength).ToArray();
        }
        finally
        {
            SyncPacketCodec.ReturnFrame(frame);
        }
    }

    // ---------- 开关关闭：零行为变化，完全走 Orleans ----------

    [Fact]
    public async Task UseDS_False_Handshake_FallsBackToOrleans()
    {
        var sessionGrain = new Mock<IPlayerSessionGrain>();
        sessionGrain
            .Setup(s => s.HandshakeAsync(It.IsAny<HandshakePacket>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<long>()))
            .ReturnsAsync(true);
        var clusterClient = CreateClusterClientWithSessionGrain(42, sessionGrain);
        var plane = new DsSyncDataPlane(new DSRegistry(), NullLogger<DsSyncDataPlane>.Instance,
            new MutableOptionsMonitor(new GatewayOptions { UseDS = false }));
        var handler = CreateHandler(clusterClient, plane);

        var handshake = new HandshakePacket
        {
            LocalCharacterId = 42,
            InitialClientTick = 100,
            ProtocolVersion = SyncProtocolVersion.Current,
            FormulaVersion = 3,
        };
        var (success, response) = await handler.RouteHandlerAsync(CreateSyncMessage(handshake));

        Assert.True(success);
        // 旧路径照常：握手到达 PlayerSessionGrain
        sessionGrain.Verify(
            s => s.HandshakeAsync(It.IsAny<HandshakePacket>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<long>()),
            Times.Once);
        Assert.IsType<HandshakePacket>(DecodeResponse(response));
    }

    // ---------- 阶段 6.3 指标：开关关闭 → 回退 reason=disabled ----------

    [Fact]
    public async Task UseDS_False_Handshake_RecordsFallbackDisabled()
    {
        using var capture = new SyncMetricsCapture();
        var sessionGrain = new Mock<IPlayerSessionGrain>();
        sessionGrain
            .Setup(s => s.HandshakeAsync(It.IsAny<HandshakePacket>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<long>()))
            .ReturnsAsync(true);
        var clusterClient = CreateClusterClientWithSessionGrain(42, sessionGrain);
        var plane = new DsSyncDataPlane(new DSRegistry(), NullLogger<DsSyncDataPlane>.Instance,
            new MutableOptionsMonitor(new GatewayOptions { UseDS = false }));
        var handler = CreateHandler(clusterClient, plane);

        var handshake = new HandshakePacket
        {
            LocalCharacterId = 42,
            InitialClientTick = 100,
            ProtocolVersion = SyncProtocolVersion.Current,
            FormulaVersion = 3,
        };
        var (success, _) = await handler.RouteHandlerAsync(CreateSyncMessage(handshake));
        Assert.True(success);

        // 开关关闭：handler 门记录 fallback:disabled（未尝试路由，attempts=0）——
        // 与开启后的 no_ds/breaker/timeout/transport_error 共同构成「进 DS 比例 vs 回退比例」。
        lock (capture.Counters)
        {
            Assert.Contains(capture.Counters,
                m => m.Name == "horizon.sync.ds.route.fallback" && m.Reason == "disabled");
            Assert.DoesNotContain(capture.Counters,
                m => m.Name == "horizon.sync.ds.route.attempts");
        }
    }

    // ---------- 阶段 6.3 按比例灰度：ratio=0 全排除（有 DS 也不路由） ----------

    [Fact]
    public async Task UseDS_RatioZero_Handshake_FallsBackToOrleans_DespiteAvailableDs()
    {
        await using var fakeDs = new FakeDs();
        var registry = new DSRegistry();
        registry.RegisterOrUpdate("ds-1", new long[] { 0 }, "127.0.0.1", fakeDs.Port);

        var sessionGrain = new Mock<IPlayerSessionGrain>();
        sessionGrain
            .Setup(s => s.HandshakeAsync(It.IsAny<HandshakePacket>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<long>()))
            .ReturnsAsync(true);
        var clusterClient = CreateClusterClientWithSessionGrain(42, sessionGrain);
        var plane = new DsSyncDataPlane(registry, NullLogger<DsSyncDataPlane>.Instance,
            new MutableOptionsMonitor(new GatewayOptions { UseDS = true, DsGrayRatio = 0 }));
        var handler = CreateHandler(clusterClient, plane);

        var handshake = new HandshakePacket
        {
            LocalCharacterId = 42,
            InitialClientTick = 100,
            ProtocolVersion = SyncProtocolVersion.Current,
            FormulaVersion = 3,
        };
        var (success, _) = await handler.RouteHandlerAsync(CreateSyncMessage(handshake));

        Assert.True(success);
        // ratio=0：即使 shard 有已注册 DS，会话被灰度排除 → 完全走 Orleans。
        sessionGrain.Verify(
            s => s.HandshakeAsync(It.IsAny<HandshakePacket>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<long>()),
            Times.Once);
        Assert.False(fakeDs.ReceivedFrame.IsCompleted); // DS 未被触碰
    }

    // ---------- 热切换（阶段 6.3）：IOptionsMonitor 运行时读取，不重启网关 ----------

    [Fact]
    public async Task UseDS_HotSwitch_ConfigFlipAtRuntime_TogglesRoutingWithoutRestart()
    {
        await using var fakeDs = new FakeDs();
        var registry = new DSRegistry();
        registry.RegisterOrUpdate("ds-1", new long[] { 0 }, "127.0.0.1", fakeDs.Port);
        var monitor = new MutableOptionsMonitor(new GatewayOptions { UseDS = false });
        var plane = new DsSyncDataPlane(registry, NullLogger<DsSyncDataPlane>.Instance, monitor);

        var sessionGrain = new Mock<IPlayerSessionGrain>();
        sessionGrain
            .Setup(s => s.HandshakeAsync(It.IsAny<HandshakePacket>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<long>()))
            .ReturnsAsync(true);
        var clusterClient = new Mock<IClusterClient>();
        clusterClient
            .Setup(c => c.GetGrain<IPlayerSessionGrain>(It.IsAny<long>(), It.IsAny<string>()))
            .Returns(sessionGrain.Object);
        var handler = CreateHandler(clusterClient, plane);

        // 每阶段用不同 characterId：避免握手幂等短路（同 characterId+connectionId 重复握手
        // 直接回显、不触碰 DS），确保各阶段真实走目标路由。
        var handshake42 = new HandshakePacket
        {
            LocalCharacterId = 42,
            InitialClientTick = 100,
            ProtocolVersion = SyncProtocolVersion.Current,
            FormulaVersion = 3,
        };
        var handshake43 = new HandshakePacket
        {
            LocalCharacterId = 43,
            InitialClientTick = 100,
            ProtocolVersion = SyncProtocolVersion.Current,
            FormulaVersion = 3,
        };
        var handshake44 = new HandshakePacket
        {
            LocalCharacterId = 44,
            InitialClientTick = 100,
            ProtocolVersion = SyncProtocolVersion.Current,
            FormulaVersion = 3,
        };

        // ① 关（构造时 UseDS=false）：完全走 Orleans，不触碰 DS。
        Assert.False(plane.IsEnabled);
        var (success1, _) = await handler.RouteHandlerAsync(CreateSyncMessage(handshake42));
        Assert.True(success1);
        sessionGrain.Verify(
            s => s.HandshakeAsync(It.IsAny<HandshakePacket>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<long>()),
            Times.Once);
        Assert.False(fakeDs.ReceivedFrame.IsCompleted); // DS 未被触碰

        // ② 热开（仅改配置，不重建 plane/handler）：下一包立即转发 DS。
        monitor.CurrentValue = new GatewayOptions { UseDS = true };
        Assert.True(plane.IsEnabled);
        var (success2, _) = await handler.RouteHandlerAsync(CreateSyncMessage(handshake43));
        Assert.True(success2);
        var received = await fakeDs.ReceivedFrame.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(EncodeFrame(handshake43), received);
        sessionGrain.Verify(
            s => s.HandshakeAsync(It.IsAny<HandshakePacket>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<long>()),
            Times.Once); // 未再触碰 Orleans

        // ③ 热关（再改配置）：回到 Orleans。
        monitor.CurrentValue = new GatewayOptions { UseDS = false };
        Assert.False(plane.IsEnabled);
        var (success3, _) = await handler.RouteHandlerAsync(CreateSyncMessage(handshake44));
        Assert.True(success3);
        sessionGrain.Verify(
            s => s.HandshakeAsync(It.IsAny<HandshakePacket>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<long>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task NoPlane_Handshake_FallsBackToOrleans()
    {
        // 未注册数据面（null 兜底）＝ 开关关闭：与旧路径零差异
        var sessionGrain = new Mock<IPlayerSessionGrain>();
        sessionGrain
            .Setup(s => s.HandshakeAsync(It.IsAny<HandshakePacket>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<long>()))
            .ReturnsAsync(true);
        var clusterClient = CreateClusterClientWithSessionGrain(42, sessionGrain);
        var handler = CreateHandler(clusterClient, plane: null);

        var handshake = new HandshakePacket
        {
            LocalCharacterId = 42,
            InitialClientTick = 100,
            ProtocolVersion = SyncProtocolVersion.Current,
            FormulaVersion = 3,
        };
        var (success, response) = await handler.RouteHandlerAsync(CreateSyncMessage(handshake));

        Assert.True(success);
        sessionGrain.Verify(
            s => s.HandshakeAsync(It.IsAny<HandshakePacket>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<long>()),
            Times.Once);
        Assert.IsType<HandshakePacket>(DecodeResponse(response));
    }

    // ---------- 开关开启：转发 DS、不触碰 Orleans ----------

    [Fact]
    public async Task UseDS_Handshake_RoutesToDs_AndSkipsOrleans()
    {
        await using var fakeDs = new FakeDs();
        var registry = new DSRegistry();
        registry.RegisterOrUpdate("ds-1", new long[] { 0 }, "127.0.0.1", fakeDs.Port);

        var sessionGrain = new Mock<IPlayerSessionGrain>();
        var clusterClient = CreateClusterClientWithSessionGrain(42, sessionGrain);
        var plane = new DsSyncDataPlane(registry, NullLogger<DsSyncDataPlane>.Instance,
            new MutableOptionsMonitor(new GatewayOptions { UseDS = true }));
        var handler = CreateHandler(clusterClient, plane);

        var handshake = new HandshakePacket
        {
            LocalCharacterId = 42,
            InitialClientTick = 100,
            ProtocolVersion = SyncProtocolVersion.Current,
            FormulaVersion = 3,
        };
        var (success, response) = await handler.RouteHandlerAsync(CreateSyncMessage(handshake));

        Assert.True(success);
        // DS 收到的是原始编码帧，逐字节一致
        var received = await fakeDs.ReceivedFrame.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(EncodeFrame(handshake), received);
        // 不触碰 Orleans 移动权威
        sessionGrain.Verify(
            s => s.HandshakeAsync(It.IsAny<HandshakePacket>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<long>()),
            Times.Never);
        // 透传 ack 回显握手
        var decoded = DecodeResponse(response);
        var echo = Assert.IsType<HandshakePacket>(decoded);
        Assert.Equal(42UL, echo.LocalCharacterId);
        Assert.Equal(100L, echo.InitialClientTick);
    }

    [Fact]
    public async Task UseDS_Input_RoutesToDs_ReturnsInputAck()
    {
        await using var fakeDs = new FakeDs();
        var registry = new DSRegistry();
        registry.RegisterOrUpdate("ds-1", new long[] { 0 }, "127.0.0.1", fakeDs.Port);

        var sessionGrain = new Mock<IPlayerSessionGrain>();
        var clusterClient = CreateClusterClientWithSessionGrain(42, sessionGrain);
        var plane = new DsSyncDataPlane(registry, NullLogger<DsSyncDataPlane>.Instance,
            new MutableOptionsMonitor(new GatewayOptions { UseDS = true }));
        var handler = CreateHandler(clusterClient, plane);

        var input = new InputPacket
        {
            CharacterId = 42,
            ClientTick = 500,
            MoveX = 1f,
            MoveY = 0f,
            MaxSpeed = 6f,
            ProtocolVersion = SyncProtocolVersion.Current,
        };
        var (success, response) = await handler.RouteHandlerAsync(CreateSyncMessage(input));

        Assert.True(success);
        var received = await fakeDs.ReceivedFrame.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(EncodeFrame(input), received);
        sessionGrain.Verify(
            s => s.ReceiveInputAndForwardAsync(It.IsAny<InputPacket>(), It.IsAny<long>(), It.IsAny<float>(), It.IsAny<float>(), It.IsAny<float>()),
            Times.Never);
        var ack = Assert.IsType<InputAckPacket>(DecodeResponse(response));
        Assert.Equal(500L, ack.EchoClientTick);
        Assert.Equal(500L, ack.LastProcessedClientTick);
    }

    [Fact]
    public async Task UseDS_Reconnect_RoutesToDs_ReturnsHandshakeConfirmation()
    {
        await using var fakeDs = new FakeDs();
        var registry = new DSRegistry();
        registry.RegisterOrUpdate("ds-1", new long[] { 0 }, "127.0.0.1", fakeDs.Port);

        var sessionGrain = new Mock<IPlayerSessionGrain>();
        var clusterClient = CreateClusterClientWithSessionGrain(42, sessionGrain);
        var plane = new DsSyncDataPlane(registry, NullLogger<DsSyncDataPlane>.Instance,
            new MutableOptionsMonitor(new GatewayOptions { UseDS = true }));
        var handler = CreateHandler(clusterClient, plane);

        var resume = new ReconnectResumePacket
        {
            LocalCharacterId = 42,
            BaselineVersion = 1,
            WorldPatchVersion = 1,
            LastAppliedDiffSeq = 10,
            ProtocolVersion = SyncProtocolVersion.Current,
        };
        var (success, response) = await handler.RouteHandlerAsync(CreateSyncMessage(resume));

        Assert.True(success);
        var received = await fakeDs.ReceivedFrame.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(EncodeFrame(resume), received);
        sessionGrain.Verify(
            s => s.ResumeAsync(It.IsAny<ReconnectResumePacket>(), It.IsAny<long>(), It.IsAny<int>()),
            Times.Never);
        // 与 Orleans 可恢复决策一致的握手确认语义
        var confirmation = Assert.IsType<HandshakePacket>(DecodeResponse(response));
        Assert.Equal(42UL, confirmation.LocalCharacterId);
    }

    // ---------- 开关开启但无可用 DS：回退 Orleans ----------

    [Fact]
    public async Task UseDS_NoDsRegistered_FallsBackToOrleans()
    {
        // 注册表为空（DS 尚未注册/心跳过期）→ TryRouteAsync 返回 false → 回退 Orleans
        var sessionGrain = new Mock<IPlayerSessionGrain>();
        sessionGrain
            .Setup(s => s.HandshakeAsync(It.IsAny<HandshakePacket>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<long>()))
            .ReturnsAsync(true);
        var clusterClient = CreateClusterClientWithSessionGrain(42, sessionGrain);
        var plane = new DsSyncDataPlane(new DSRegistry(), NullLogger<DsSyncDataPlane>.Instance,
            new MutableOptionsMonitor(new GatewayOptions { UseDS = true }));
        var handler = CreateHandler(clusterClient, plane);

        var handshake = new HandshakePacket
        {
            LocalCharacterId = 42,
            InitialClientTick = 100,
            ProtocolVersion = SyncProtocolVersion.Current,
            FormulaVersion = 3,
        };
        var (success, response) = await handler.RouteHandlerAsync(CreateSyncMessage(handshake));

        Assert.True(success);
        sessionGrain.Verify(
            s => s.HandshakeAsync(It.IsAny<HandshakePacket>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<long>()),
            Times.Once);
        Assert.IsType<HandshakePacket>(DecodeResponse(response));
    }

    [Fact]
    public async Task UseDS_DsConnectionRefused_FallsBackToOrleans()
    {
        // DS 已注册但端口无人监听 → 连接拒绝 → 回退 Orleans（灰度期不因 DS 故障断服务）
        var registry = new DSRegistry();
        registry.RegisterOrUpdate("ds-down", new long[] { 0 }, "127.0.0.1", 1); // port 1 几乎必拒

        var sessionGrain = new Mock<IPlayerSessionGrain>();
        sessionGrain
            .Setup(s => s.HandshakeAsync(It.IsAny<HandshakePacket>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<long>()))
            .ReturnsAsync(true);
        var clusterClient = CreateClusterClientWithSessionGrain(42, sessionGrain);
        var plane = new DsSyncDataPlane(registry, NullLogger<DsSyncDataPlane>.Instance,
            new MutableOptionsMonitor(new GatewayOptions { UseDS = true }));
        var handler = CreateHandler(clusterClient, plane);

        var handshake = new HandshakePacket
        {
            LocalCharacterId = 42,
            InitialClientTick = 100,
            ProtocolVersion = SyncProtocolVersion.Current,
            FormulaVersion = 3,
        };
        var (success, response) = await handler.RouteHandlerAsync(CreateSyncMessage(handshake));

        Assert.True(success);
        sessionGrain.Verify(
            s => s.HandshakeAsync(It.IsAny<HandshakePacket>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<long>()),
            Times.Once);
        Assert.IsType<HandshakePacket>(DecodeResponse(response));
    }
}
