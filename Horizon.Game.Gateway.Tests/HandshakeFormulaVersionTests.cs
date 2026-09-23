using Horizon.Game.Core;
using Horizon.Game.Core.Handlers;
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
/// 阶段 0.5 — 握手公式版本校验的行为测试。
/// <para>
/// 验证 <see cref="SyncPacketHandler.HandleHandshakeAsync"/> 中的公式版本检查能正确拒绝不兼容连接，
/// 而兼容版本能正常通过路由到 <see cref="IPlayerSessionGrain.HandshakeAsync"/>。
/// </para>
/// </summary>
public class HandshakeFormulaVersionTests
{
    private static SyncPacketHandler CreateHandler(Mock<IClusterClient>? clusterClient = null)
    {
        clusterClient ??= new Mock<IClusterClient>();
        var adapter = new HorizonMessageAdapter();
        return new SyncPacketHandler(NullLogger<MessageHandlerBase>.Instance, clusterClient.Object, adapter);
    }

    /// <summary>
    /// 构造一个 HorizonMessagePacket，其 Body 为编码了 <paramref name="handshake"/> 的 SyncFrameMessage。
    /// 模拟客户端发送握手包的网络路径。
    /// </summary>
    private static HorizonMessagePacket CreateHandshakeMessage(HandshakePacket handshake)
    {
        SyncPacketCodec.Encode(handshake, out var frame, out var frameLength);
        try
        {
            var syncFrame = new SyncFrameMessage();
            syncFrame.Frame = frame.AsSpan(0, frameLength).ToArray();
            // PacketKind 由接收端的 SyncFrameMessage header 提供，不在本测试范围内
            return new HorizonMessagePacket
            {
                Header = new MessageHeader
                {
                    MessageType = MessageType.SyncPacket,
                    ServiceType = ServiceType.Game,
                    CharacterId = handshake.LocalCharacterId,
                },
                Body = syncFrame,
            };
        }
        finally
        {
            SyncPacketCodec.ReturnFrame(frame);
        }
    }

    [Fact]
    public async Task FormulaVersion_Unset_ReturnsHandshakeReject()
    {
        var handler = CreateHandler();
        var handshake = new HandshakePacket
        {
            LocalCharacterId = 42,
            InitialClientTick = 100,
            ProtocolVersion = SyncProtocolVersion.Current, // v9
            FormulaVersion = 0, // 未设置（旧客户端行为）
        };
        var message = CreateHandshakeMessage(handshake);
        var (success, response) = await handler.RouteHandlerAsync(message);

        Assert.True(success);
        Assert.NotNull(response);
        Assert.NotNull(response.Body);
        Assert.IsType<SyncFrameMessage>(response.Body);
        var syncFrame = (SyncFrameMessage)response.Body;
        var decoded = SyncPacketCodec.Decode(syncFrame.Frame.AsSpan(0, syncFrame.Frame.Length));
        Assert.IsType<HandshakeRejectPacket>(decoded);
        var reject = (HandshakeRejectPacket)decoded!;
        Assert.Contains("公式版本", reject.Reason);
        Assert.Contains("3", reject.Reason);
    }

    /// <summary>
    /// 旧版本客户端（1.3.4 之前发布，携带 FormulaVersion=2）在握手阶段被拒绝——
    /// 公式版本是确定性回放语义（跨 tick 跳跃状态持久化演进），硬拒绝而非软过渡。
    /// </summary>
    [Fact]
    public async Task FormulaVersion_OldVersion_ReturnsHandshakeReject()
    {
        var handler = CreateHandler();
        var handshake = new HandshakePacket
        {
            LocalCharacterId = 42,
            InitialClientTick = 100,
            ProtocolVersion = SyncProtocolVersion.Current,
            FormulaVersion = 2, // 旧版本（FormulaVersion 2→3 升级前的客户端）
        };
        var message = CreateHandshakeMessage(handshake);
        var (success, response) = await handler.RouteHandlerAsync(message);

        Assert.True(success);
        Assert.NotNull(response);
        Assert.NotNull(response.Body);
        Assert.IsType<SyncFrameMessage>(response.Body);
        var syncFrame = (SyncFrameMessage)response.Body;
        var decoded = SyncPacketCodec.Decode(syncFrame.Frame.AsSpan(0, syncFrame.Frame.Length));
        Assert.IsType<HandshakeRejectPacket>(decoded);
        var reject = (HandshakeRejectPacket)decoded!;
        Assert.Contains("公式版本", reject.Reason);
        Assert.Contains("3", reject.Reason);
    }

    [Fact]
    public async Task FormulaVersion_Current_ProceedsToSessionGrain()
    {
        var clusterClient = new Mock<IClusterClient>();
        var sessionGrain = new Mock<IPlayerSessionGrain>();
        clusterClient
            .Setup(c => c.GetGrain<IPlayerSessionGrain>(42L, It.IsAny<string>()))
            .Returns(sessionGrain.Object);
        sessionGrain
            .Setup(s => s.HandshakeAsync(It.IsAny<HandshakePacket>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<long>()))
            .ReturnsAsync(true);

        var handler = CreateHandler(clusterClient);
        var handshake = new HandshakePacket
        {
            LocalCharacterId = 42,
            InitialClientTick = 100,
            ProtocolVersion = SyncProtocolVersion.Current,
            FormulaVersion = 3, // 匹配当前 FormulaVersion
        };
        var message = CreateHandshakeMessage(handshake);
        var (success, response) = await handler.RouteHandlerAsync(message);

        Assert.True(success);
        Assert.NotNull(response);
        Assert.NotNull(response.Body);
        Assert.IsType<SyncFrameMessage>(response.Body);
        var syncFrame = (SyncFrameMessage)response.Body;
        var decoded = SyncPacketCodec.Decode(syncFrame.Frame.AsSpan(0, syncFrame.Frame.Length));
        Assert.IsType<HandshakePacket>(decoded);
        sessionGrain.Verify(
            s => s.HandshakeAsync(It.IsAny<HandshakePacket>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<long>()),
            Times.Once);
    }
}