using Horizon.Game.Core;
using Horizon.Game.Core.Handlers;
using Horizon.Game.Message.Sync;
using Horizon.Orleans.Interface.World;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Orleans;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// Task 10.2: SyncPacketHandler.HandleInteractionIntent 单元测试。
/// </summary>
public class HandleInteractionIntentTests
{
    private static (SyncPacketHandler handler, Mock<IZoneShardGrain> zoneShardMock) CreateHandler()
    {
        var adapter = new HorizonMessageAdapter();
        var clusterClient = new Mock<IClusterClient>();
        var zoneShardMock = new Mock<IZoneShardGrain>();
        clusterClient.Setup(c => c.GetGrain<IZoneShardGrain>(0L, It.IsAny<string>())).Returns(zoneShardMock.Object);
        zoneShardMock.Setup(z => z.GenerateInteractionSync(It.IsAny<int>(), It.IsAny<long>(), It.IsAny<long>(), It.IsAny<byte>(), It.IsAny<long>())).Returns(Task.CompletedTask);
        var handler = new SyncPacketHandler(NullLogger<MessageHandlerBase>.Instance, clusterClient.Object, adapter);
        return (handler, zoneShardMock);
    }

    [Fact]
    public async Task Rejects_InteractorIdZero()
    {
        var (handler, zoneShardMock) = CreateHandler();
        var result = await handler.HandleInteractionIntent(0, 100L, 1, SyncEventKind.InteractStart);
        Assert.False(result);
        zoneShardMock.Verify(z => z.GenerateInteractionSync(It.IsAny<int>(), It.IsAny<long>(), It.IsAny<long>(), It.IsAny<byte>(), It.IsAny<long>()), Times.Never);
    }

    [Fact]
    public async Task Rejects_InteractableIdZero()
    {
        var (handler, zoneShardMock) = CreateHandler();
        var result = await handler.HandleInteractionIntent(100L, 0, 1, SyncEventKind.InteractStart);
        Assert.False(result);
        zoneShardMock.Verify(z => z.GenerateInteractionSync(It.IsAny<int>(), It.IsAny<long>(), It.IsAny<long>(), It.IsAny<byte>(), It.IsAny<long>()), Times.Never);
    }

    [Fact]
    public async Task Rejects_InvalidIntentType()
    {
        var (handler, zoneShardMock) = CreateHandler();
        var result = await handler.HandleInteractionIntent(100L, 200L, 1, SyncEventKind.Unknown);
        Assert.False(result);
        zoneShardMock.Verify(z => z.GenerateInteractionSync(It.IsAny<int>(), It.IsAny<long>(), It.IsAny<long>(), It.IsAny<byte>(), It.IsAny<long>()), Times.Never);
    }

    [Fact]
    public async Task Rejects_NegativeSlotIdx()
    {
        var (handler, zoneShardMock) = CreateHandler();
        var result = await handler.HandleInteractionIntent(100L, 200L, -1, SyncEventKind.InteractStart);
        Assert.False(result);
        zoneShardMock.Verify(z => z.GenerateInteractionSync(It.IsAny<int>(), It.IsAny<long>(), It.IsAny<long>(), It.IsAny<byte>(), It.IsAny<long>()), Times.Never);
    }

    [Fact]
    public async Task StateBits_Mapping_Start()
    {
        var (handler, zoneShardMock) = CreateHandler();
        var result = await handler.HandleInteractionIntent(100L, 200L, 1, SyncEventKind.InteractStart);
        Assert.True(result);
        zoneShardMock.Verify(z => z.GenerateInteractionSync(1, 200L, 100L, InteractionStateBits.Start, It.IsAny<long>()), Times.Once);
    }

    [Fact]
    public async Task StateBits_Mapping_End()
    {
        var (handler, zoneShardMock) = CreateHandler();
        var result = await handler.HandleInteractionIntent(100L, 200L, 1, SyncEventKind.InteractEnd);
        Assert.True(result);
        zoneShardMock.Verify(z => z.GenerateInteractionSync(1, 200L, 100L, InteractionStateBits.End, It.IsAny<long>()), Times.Once);
    }

    [Fact]
    public async Task StateBits_Mapping_Stolen()
    {
        var (handler, zoneShardMock) = CreateHandler();
        var result = await handler.HandleInteractionIntent(100L, 200L, 1, SyncEventKind.InteractStolen);
        Assert.True(result);
        zoneShardMock.Verify(z => z.GenerateInteractionSync(1, 200L, 100L, InteractionStateBits.Stolen, It.IsAny<long>()), Times.Once);
    }

    [Fact]
    public async Task RateLimit_Enforced()
    {
        var (handler, zoneShardMock) = CreateHandler();
        var results = new List<bool>();
        for (var i = 0; i < 11; i++)
        {
            var r = await handler.HandleInteractionIntent(500L, 600L, 0, SyncEventKind.InteractStart);
            results.Add(r);
        }
        Assert.True(results[0]);
        Assert.All(results.Skip(1), r => Assert.False(r));
        zoneShardMock.Verify(z => z.GenerateInteractionSync(It.IsAny<int>(), It.IsAny<long>(), It.IsAny<long>(), It.IsAny<byte>(), It.IsAny<long>()), Times.Once);
    }

    [Fact]
    public async Task RateLimit_Isolated_PerInteractorId()
    {
        var (handler, _) = CreateHandler();
        var r1 = await handler.HandleInteractionIntent(500L, 600L, 0, SyncEventKind.InteractStart);
        var r2 = await handler.HandleInteractionIntent(501L, 600L, 0, SyncEventKind.InteractStart);
        Assert.True(r1);
        Assert.True(r2);
    }

    [Fact]
    public async Task GrainException_ReturnsFalse()
    {
        var adapter = new HorizonMessageAdapter();
        var clusterClient = new Mock<IClusterClient>();
        var zoneShardMock = new Mock<IZoneShardGrain>();
        clusterClient.Setup(c => c.GetGrain<IZoneShardGrain>(0L, It.IsAny<string>())).Returns(zoneShardMock.Object);
        zoneShardMock.Setup(z => z.GenerateInteractionSync(It.IsAny<int>(), It.IsAny<long>(), It.IsAny<long>(), It.IsAny<byte>(), It.IsAny<long>())).ThrowsAsync(new InvalidOperationException("grain unavailable"));
        var handler = new SyncPacketHandler(NullLogger<MessageHandlerBase>.Instance, clusterClient.Object, adapter);
        var result = await handler.HandleInteractionIntent(100L, 200L, 1, SyncEventKind.InteractStart);
        Assert.False(result);
    }
}
