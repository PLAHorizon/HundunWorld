using Horizon.Game.Core;
using Horizon.Game.Message;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Orleans;
using TouchSocket.Sockets;

namespace Horizon.Game.Gateway.Tests;

public class MessageHandlerBaseResponseTests
{
    [Fact]
    public async Task HandleAsync_ResponsePacketPreservesRequestCorrelationAndRoutingHeaders()
    {
        var adapter = new HorizonMessageAdapter();
        var clusterClient = new Mock<IClusterClient>();
        var handler = new TestMessageHandler(clusterClient.Object, adapter)
        {
            RouteHandlerFunc = request =>
            {
                var response = new HeartbeatResponse
                {
                    Timestamp = 100,
                    ServerTime = 200,
                    Latency = 12,
                };

                return Task.FromResult((true, adapter.CreateHorizonMessage(response)));
            }
        };

        var sentFrames = new List<byte[]>();
        var client = CreateClientMock(sentFrames);

        var request = adapter.CreateHorizonMessage(new HeartbeatMessage
        {
            Timestamp = 1,
            ClientTime = 2,
            ServerTime = 0,
        });
        request.Header.GameId = 77;
        request.Header.ZoneId = 88;
        request.Header.ServerId = 99;
        request.Header.MessageId = "req-001";

        var (isSuccess, response) = await handler.HandleAsync(client.Object, request);

        Assert.True(isSuccess);
        Assert.IsType<HeartbeatResponse>(response);
        Assert.Single(sentFrames);

        var sentPacket = adapter.UnpackMessage(sentFrames[0]);
        Assert.True(sentPacket.Header.IsResponse);
        Assert.Equal("req-001", sentPacket.Header.ResponseToMessageId);
        Assert.Equal((uint)77, sentPacket.Header.GameId);
        Assert.Equal((uint)88, sentPacket.Header.ZoneId);
        Assert.Equal((uint)99, sentPacket.Header.ServerId);
        Assert.IsType<HeartbeatResponse>(sentPacket.Body);
    }

    [Fact]
    public async Task HandleAsync_WhenRouteHandlerThrows_SendsFallbackErrorResponse()
    {
        var adapter = new HorizonMessageAdapter();
        var clusterClient = new Mock<IClusterClient>();
        var handler = new TestMessageHandler(clusterClient.Object, adapter)
        {
            RouteHandlerFunc = _ => throw new InvalidOperationException("boom")
        };

        var sentFrames = new List<byte[]>();
        var client = CreateClientMock(sentFrames);

        var request = adapter.CreateHorizonMessage(new HeartbeatMessage
        {
            Timestamp = 10,
            ClientTime = 20,
            ServerTime = 0,
        });
        request.Header.MessageId = "req-err-001";
        request.Header.GameId = 5;
        request.Header.ZoneId = 6;
        request.Header.ServerId = 7;

        var (isSuccess, response) = await handler.HandleAsync(client.Object, request);

        Assert.False(isSuccess);
        var errorResponse = Assert.IsType<ErrorMessage>(response);
        Assert.Equal("req-err-001", errorResponse.RelatedMessageId);
        Assert.Single(sentFrames);

        var sentPacket = adapter.UnpackMessage(sentFrames[0]);
        var sentError = Assert.IsType<ErrorMessage>(sentPacket.Body);
        Assert.True(sentPacket.Header.IsResponse);
        Assert.Equal("req-err-001", sentPacket.Header.ResponseToMessageId);
        Assert.Equal("req-err-001", sentError.RelatedMessageId);
        Assert.Contains("服务器处理请求时发生异常", sentError.Message);
    }

    private static Mock<ITcpSessionClient> CreateClientMock(List<byte[]> sentFrames)
    {
        var client = new Mock<ITcpSessionClient>();
        client.Setup(c => c.SendAsync(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
            .Callback<ReadOnlyMemory<byte>, CancellationToken>((buffer, _) => sentFrames.Add(buffer.ToArray()))
            .Returns(Task.CompletedTask);
        return client;
    }

    private sealed class TestMessageHandler(IClusterClient clusterClient, HorizonMessageAdapter adapter)
        : MessageHandlerBase(NullLogger<MessageHandlerBase>.Instance, clusterClient, adapter)
    {
        public Func<HorizonMessagePacket, Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)>> RouteHandlerFunc { get; init; } =
            _ => Task.FromResult((true, default(HorizonMessagePacket)!));

        public override List<MessageType> MessageTypes => new() { MessageType.Heartbeat };

        public override ServiceType ServiceType => ServiceType.System;

        public HorizonMessagePacket CreatePacket<T>(T message) where T : MessageUnion, INetworkMessage
        {
            return CreateHorizonMessage(message);
        }

        public override Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> RouteHandlerAsync(HorizonMessagePacket message)
        {
            return RouteHandlerFunc(message);
        }
    }
}