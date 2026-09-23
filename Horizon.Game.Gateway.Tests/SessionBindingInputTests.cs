using System.Threading.Tasks;
using Horizon.Game.Core;
using Horizon.Game.Core.Handlers;
using Horizon.Game.Core.Interfaces;
using Horizon.Game.Message;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using Horizon.Game.Message.Sync;
using Horizon.Game.Message.Sync.Server;
using Horizon.Orleans.Interface.World;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Orleans;
using TouchSocket.Sockets;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 阶段 5.2.2 — 输入热路径会话绑定校验。<br/>
/// 原会话绑定校验未覆盖 InputPacket 热路径（最高频上行），伪造 characterId 可冒充他人移动。
/// 本测试验证：绑定校验开启且 characterId 与该连接不一致时，输入在触碰任何 grain 前被拦截
/// （返回拒绝 ack，PlayerSessionGrain 零调用）；绑定一致时正常走 Orleans 路径。
/// </summary>
public class SessionBindingInputTests
{
    private const string ConnId = "conn-1";
    private const long CharacterId = 42;

    private static Mock<ISessionBindingValidator> CreateValidator(bool enabled, bool bound)
    {
        var validator = new Mock<ISessionBindingValidator>();
        validator.Setup(v => v.IsEnabled).Returns(enabled);
        validator.Setup(v => v.IsCharacterBoundToConnection(ConnId, CharacterId)).Returns(bound);
        return validator;
    }

    private static Mock<IClusterClient> CreateClusterClient(Mock<IPlayerSessionGrain> sessionGrain)
    {
        var clusterClient = new Mock<IClusterClient>();
        clusterClient
            .Setup(c => c.GetGrain<IPlayerSessionGrain>(CharacterId, It.IsAny<string>()))
            .Returns(sessionGrain.Object);
        clusterClient
            .Setup(c => c.GetGrain<IPlayerSessionGrain>(CharacterId))
            .Returns(sessionGrain.Object);
        return clusterClient;
    }

    private static Mock<ITcpSessionClient> CreateClient()
    {
        var client = new Mock<ITcpSessionClient>();
        client.SetupGet(c => c.Id).Returns(ConnId);
        client.SetupGet(c => c.Online).Returns(true);
        client.Setup(c => c.SendAsync(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<System.Threading.CancellationToken>()))
            .Returns(System.Threading.Tasks.Task.CompletedTask);
        return client;
    }

    private static HorizonMessagePacket CreateInputMessage(long clientTick)
    {
        SyncPacketCodec.Encode(new InputPacket
        {
            CharacterId = (ulong)CharacterId,
            ClientTick = clientTick,
            MoveX = 1f,
            MoveY = 0f,
            MaxSpeed = 6f,
            ProtocolVersion = SyncProtocolVersion.Current,
        }, out var frame, out var frameLength);
        try
        {
            return new HorizonMessagePacket
            {
                Header = new MessageHeader
                {
                    MessageType = MessageType.SyncPacket,
                    ServiceType = ServiceType.Game,
                    CharacterId = (ulong)CharacterId,
                },
                Body = new SyncFrameMessage
                {
                    Frame = frame.AsSpan(0, frameLength).ToArray(),
                },
            };
        }
        finally
        {
            SyncPacketCodec.ReturnFrame(frame);
        }
    }

    private static SyncPacket DecodeAck(MessageUnion? response)
    {
        var syncFrame = Assert.IsType<SyncFrameMessage>(response);
        return SyncPacketCodec.Decode(syncFrame.Frame.AsSpan(0, syncFrame.Frame.Length));
    }

    [Fact]
    public async Task UnboundCharacter_InputIsRejected_BeforeTouchingOrleans()
    {
        var sessionGrain = new Mock<IPlayerSessionGrain>();
        var validator = CreateValidator(enabled: true, bound: false);
        var handler = new SyncPacketHandler(
            NullLogger<MessageHandlerBase>.Instance,
            CreateClusterClient(sessionGrain).Object,
            new HorizonMessageAdapter(),
            sessionBindingValidator: validator.Object);

        var (isSuccess, response) = await handler.HandleAsync(CreateClient().Object, CreateInputMessage(clientTick: 100));

        Assert.True(isSuccess);
        var ack = Assert.IsType<InputAckPacket>(DecodeAck(response));
        // 拒绝 ack：LastProcessedClientTick=0 表示未处理（区别于正常确认）
        Assert.Equal(0, ack.LastProcessedClientTick);
        Assert.Equal(100, ack.EchoClientTick);

        // 伪造角色输入在触碰任何 grain 前被拦
        sessionGrain.Verify(
            s => s.ReceiveInputAndForwardAsync(It.IsAny<InputPacket>(), It.IsAny<long>(), It.IsAny<float>(), It.IsAny<float>(), It.IsAny<float>()),
            Times.Never);
        sessionGrain.Verify(
            s => s.BuildInputAckAsync(It.IsAny<long>()),
            Times.Never);
    }

    [Fact]
    public async Task BoundCharacter_InputFlowsToOrleansNormally()
    {
        var sessionGrain = new Mock<IPlayerSessionGrain>();
        sessionGrain
            .Setup(s => s.ReceiveInputAndForwardAsync(
                It.IsAny<InputPacket>(), It.IsAny<long>(), It.IsAny<float>(), It.IsAny<float>(), It.IsAny<float>()))
            .ReturnsAsync(new InputForwardResult
            {
                Result = InputAcceptResult.Accepted,
                Ack = new InputAckPacket
                {
                    LastProcessedClientTick = 100,
                    ServerTick = 1000,
                    EchoClientTick = 100,
                },
            });

        var validator = CreateValidator(enabled: true, bound: true);
        var handler = new SyncPacketHandler(
            NullLogger<MessageHandlerBase>.Instance,
            CreateClusterClient(sessionGrain).Object,
            new HorizonMessageAdapter(),
            sessionBindingValidator: validator.Object);

        var (isSuccess, response) = await handler.HandleAsync(CreateClient().Object, CreateInputMessage(clientTick: 100));

        Assert.True(isSuccess);
        var ack = Assert.IsType<InputAckPacket>(DecodeAck(response));
        Assert.Equal(100, ack.LastProcessedClientTick);

        // 绑定一致 → 正常转发到会话 grain
        sessionGrain.Verify(
            s => s.ReceiveInputAndForwardAsync(
                It.Is<InputPacket>(p => p.ClientTick == 100), It.IsAny<long>(), It.IsAny<float>(), It.IsAny<float>(), It.IsAny<float>()),
            Times.Once);
    }

    [Fact]
    public async Task ValidatorDisabled_FallsBackToOrleansPath()
    {
        var sessionGrain = new Mock<IPlayerSessionGrain>();
        sessionGrain
            .Setup(s => s.ReceiveInputAndForwardAsync(
                It.IsAny<InputPacket>(), It.IsAny<long>(), It.IsAny<float>(), It.IsAny<float>(), It.IsAny<float>()))
            .ReturnsAsync(new InputForwardResult
            {
                Result = InputAcceptResult.Accepted,
                Ack = new InputAckPacket { LastProcessedClientTick = 100, ServerTick = 1000, EchoClientTick = 100 },
            });

        // 灰度开关关闭（默认）：即使绑定不匹配也不拦截，完全走旧路径（零行为变化）
        var validator = CreateValidator(enabled: false, bound: false);
        var handler = new SyncPacketHandler(
            NullLogger<MessageHandlerBase>.Instance,
            CreateClusterClient(sessionGrain).Object,
            new HorizonMessageAdapter(),
            sessionBindingValidator: validator.Object);

        var (isSuccess, response) = await handler.HandleAsync(CreateClient().Object, CreateInputMessage(clientTick: 100));

        Assert.True(isSuccess);
        var ack = Assert.IsType<InputAckPacket>(DecodeAck(response));
        Assert.Equal(100, ack.LastProcessedClientTick);
        sessionGrain.Verify(
            s => s.ReceiveInputAndForwardAsync(It.IsAny<InputPacket>(), It.IsAny<long>(), It.IsAny<float>(), It.IsAny<float>(), It.IsAny<float>()),
            Times.Once);
    }
}
