using Horizon.Game.Core;
using Horizon.Game.Core.Handlers;
using Horizon.Game.Core.Interfaces;
using Horizon.Game.Message;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using Horizon.Game.Message.Sync;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Orleans;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 阶段 0.6 — 协议严格版本匹配模式（RequireStrictVersionMatch 可配置化）。
/// <para>
/// 验证 <see cref="SyncPacketHandler"/> 在宽松（默认）/严格两种策略下的行为：
/// 宽松 = 版本 &lt; MinimumSupported 拒绝、否则告警放行（开发/过渡期，向后兼容）；
/// 严格 = 任何 != Current 都拒绝（生产）。握手路径与逐包路径均生效。
/// </para>
/// </summary>
public class ProtocolVersionStrictModeTests
{
    /// <summary>
    /// 构造 handler；<paramref name="mockSessionGrain"/> 为 true 时预置 PlayerSessionGrain 握手成功
    /// （放行路径需要它返回 HandshakePacket，否则握手失败返回 false）。
    /// </summary>
    private static SyncPacketHandler CreateHandler(IProtocolVersionPolicy? policy = null, Mock<IClusterClient>? clusterClient = null, bool mockSessionGrain = false)
    {
        clusterClient ??= new Mock<IClusterClient>();
        if (mockSessionGrain)
        {
            var sessionGrain = new Mock<Horizon.Orleans.Interface.World.IPlayerSessionGrain>();
            clusterClient
                .Setup(c => c.GetGrain<Horizon.Orleans.Interface.World.IPlayerSessionGrain>(42L, It.IsAny<string>()))
                .Returns(sessionGrain.Object);
            sessionGrain
                .Setup(s => s.HandshakeAsync(It.IsAny<HandshakePacket>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<long>()))
                .ReturnsAsync(true);
        }
        var adapter = new HorizonMessageAdapter();
        return new SyncPacketHandler(
            NullLogger<MessageHandlerBase>.Instance,
            clusterClient.Object,
            adapter,
            protocolVersionPolicy: policy);
    }

    /// <summary>
    /// 构造一个 Body 为编码了 <paramref name="syncPacket"/> 的 SyncFrameMessage 的 HorizonMessagePacket。
    /// 与 <see cref="HandshakeFormulaVersionTests.CreateHandshakeMessage"/> 同构。
    /// </summary>
    private static HorizonMessagePacket CreateSyncMessage(SyncPacket syncPacket)
    {
        SyncPacketCodec.Encode(syncPacket, out var frame, out var frameLength);
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
                    CharacterId = syncPacket is HandshakePacket h ? h.LocalCharacterId : 42,
                },
                Body = syncFrame,
            };
        }
        finally
        {
            SyncPacketCodec.ReturnFrame(frame);
        }
    }

    private static HandshakePacket CreateHandshake(int protocolVersion, ulong characterId = 42)
    {
        return new HandshakePacket
        {
            LocalCharacterId = characterId,
            InitialClientTick = 100,
            ProtocolVersion = protocolVersion,
            FormulaVersion = 3, // 匹配当前 FormulaVersion，隔离协议版本变量
        };
    }

    private static (bool Success, SyncPacket Decoded) Route(SyncPacketHandler handler, HorizonMessagePacket message)
    {
        var (success, response) = handler.RouteHandlerAsync(message).GetAwaiter().GetResult();
        if (!success || response?.Body is not SyncFrameMessage syncFrame)
        {
            return (success, null!);
        }
        var decoded = SyncPacketCodec.Decode(syncFrame.Frame.AsSpan(0, syncFrame.Frame.Length));
        return (success, decoded);
    }

    // ===== 宽松模式（默认，向后兼容） =====

    [Fact]
    public void Lenient_LowerThanMinimum_Rejects()
    {
        var handler = CreateHandler(); // null policy → ProtocolVersionPolicy.Instance
        var message = CreateSyncMessage(CreateHandshake(SyncProtocolVersion.MinimumSupported - 1));
        var (success, decoded) = Route(handler, message);

        Assert.True(success); // 网关层仍返回成功（错误以 Reject 包下发）
        Assert.IsType<HandshakeRejectPacket>(decoded);
        Assert.Contains("协议版本过低", ((HandshakeRejectPacket)decoded).Reason);
    }

    [Fact]
    public void Lenient_TransitionVersion_AllowsConnection()
    {
        var handler = CreateHandler(mockSessionGrain: true); // null policy → 宽松
        var message = CreateSyncMessage(CreateHandshake(SyncProtocolVersion.Current - 1)); // 过渡期版本（>= MinimumSupported）
        var (success, decoded) = Route(handler, message);

        Assert.True(success);
        // 握手继续到 PFSessionGrain → 返回 HandshakePacket（而非 Reject）
        Assert.IsType<HandshakePacket>(decoded);
    }

    [Fact]
    public void Lenient_ExactCurrent_AllowsConnection()
    {
        var handler = CreateHandler(mockSessionGrain: true);
        var message = CreateSyncMessage(CreateHandshake(SyncProtocolVersion.Current));
        var (success, decoded) = Route(handler, message);

        Assert.True(success);
        Assert.IsType<HandshakePacket>(decoded);
    }

    // ===== 严格模式（RequireStrictVersionMatch=true） =====

    [Fact]
    public void Strict_LowerThanMinimum_Rejects()
    {
        var handler = CreateHandler(StrictProtocolVersionPolicy.Instance);
        var message = CreateSyncMessage(CreateHandshake(SyncProtocolVersion.MinimumSupported - 1));
        var (success, decoded) = Route(handler, message);

        Assert.True(success);
        Assert.IsType<HandshakeRejectPacket>(decoded);
        Assert.Contains("协议版本不匹配", ((HandshakeRejectPacket)decoded).Reason);
    }

    [Fact]
    public void Strict_TransitionVersion_Rejects()
    {
        // 严格模式下过渡期版本（>= MinimumSupported 但 != Current）也拒绝——这是 0.6 的核心增量。
        var handler = CreateHandler(StrictProtocolVersionPolicy.Instance);
        var message = CreateSyncMessage(CreateHandshake(SyncProtocolVersion.Current - 1));
        var (success, decoded) = Route(handler, message);

        Assert.True(success);
        Assert.IsType<HandshakeRejectPacket>(decoded);
        var reject = (HandshakeRejectPacket)decoded;
        Assert.Contains("协议版本不匹配", reject.Reason);
        Assert.Equal(SyncProtocolVersion.Current, reject.ServerVersion);
    }

    [Fact]
    public void Strict_ExactCurrent_AllowsConnection()
    {
        var handler = CreateHandler(StrictProtocolVersionPolicy.Instance, mockSessionGrain: true);
        var message = CreateSyncMessage(CreateHandshake(SyncProtocolVersion.Current));
        var (success, decoded) = Route(handler, message);

        Assert.True(success);
        Assert.IsType<HandshakePacket>(decoded);
    }

    // ===== 逐包路径（非握手帧） =====

    [Fact]
    public void Strict_PerPacketPath_RejectsMismatchedVersion()
    {
        // 非握手帧（如 InputPacket）在严格模式下版本不匹配同样拒绝——协议不兼容的连接逐包路径也不应继续。
        var handler = CreateHandler(StrictProtocolVersionPolicy.Instance);
        var input = new InputPacket
        {
            ClientTick = 1,
            ProtocolVersion = SyncProtocolVersion.Current - 1,
            MoveX = 1.0f,
            MoveY = 0.0f,
        };
        var message = CreateSyncMessage(input);
        var (success, decoded) = Route(handler, message);

        Assert.True(success);
        Assert.IsType<HandshakeRejectPacket>(decoded); // 版本拒绝优先于正常处理
    }

    [Fact]
    public void Lenient_PerPacketPath_WarnsAndContinues()
    {
        // 宽松模式：逐包路径版本不匹配仅告警，不拒绝（对齐历史行为）。
        var handler = CreateHandler(); // 宽松
        var input = new InputPacket
        {
            ClientTick = 1,
            ProtocolVersion = SyncProtocolVersion.Current - 1,
            MoveX = 1.0f,
            MoveY = 0.0f,
        };
        var message = CreateSyncMessage(input);
        var (success, decoded) = Route(handler, message);

        Assert.True(success);
        Assert.IsNotType<HandshakeRejectPacket>(decoded); // 不拒绝，走正常处理
    }

    // ===== 策略纯逻辑 =====

    [Theory]
    [InlineData(SyncProtocolVersion.Current, true)]
    [InlineData(SyncProtocolVersion.Current - 1, false)] // 过渡期版本被严格拒绝
    [InlineData(SyncProtocolVersion.MinimumSupported - 1, false)]
    public void StrictPolicy_OnlyExactCurrent_Accepted(int clientVersion, bool expected)
    {
        Assert.Equal(expected, StrictProtocolVersionPolicy.Instance.IsVersionAccepted(clientVersion));
    }

    [Theory]
    [InlineData(SyncProtocolVersion.Current, true)]
    [InlineData(SyncProtocolVersion.Current - 1, true)] // 过渡期版本宽松放行
    [InlineData(SyncProtocolVersion.MinimumSupported - 1, false)]
    public void LenientPolicy_OnlyBelowMinimum_Rejected(int clientVersion, bool expected)
    {
        Assert.Equal(expected, ProtocolVersionPolicy.Instance.IsVersionAccepted(clientVersion));
    }
}
