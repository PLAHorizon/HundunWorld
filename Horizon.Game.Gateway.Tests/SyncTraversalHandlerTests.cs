using System;
using System.Threading;
using System.Threading.Tasks;
using Horizon.Game.Core;
using Horizon.Game.Core.Handlers;
using Horizon.Game.Core.Interfaces;
using Horizon.Game.Core.World;
using Horizon.Game.Message;
using Horizon.Game.Message.Sync;
using Horizon.Game.Sim;
using Horizon.Orleans.Interface.World;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Orleans;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// v17 翻越网关路由：DS 权威优先 → 否则转 ZoneShardGrain；会话绑定校验先于任何权威；
/// 权威不可达/异常都必须回 <c>AuthorityUnavailable</c> 而不是让客户端干等。
/// <para>
/// 这里钉的是"网关只做鉴权与路由、不重复裁决"——裁决唯一实现在 SimWorld，
/// 网关若自行放行/拒绝，两端规则就会分叉。
/// </para>
/// </summary>
public class SyncTraversalHandlerTests
{
    private const long CharacterId = 4242L;
    private const long ShardId = 7L;
    private const string ConnId = "conn-1";

    private static TraversalRequestPacket Request(ulong characterId = CharacterId)
        => new TraversalRequestPacket
        {
            CharacterId = characterId,
            ClientTick = 11L,
            StartX = 0f, StartY = 0f, StartZ = 0f,
            EndX = 0.8f, EndY = 0.6f, EndZ = 0.9f,
            Duration = 0.6f,
            ArcHeight = 0.1f,
            EndYaw = 0.2f,
            CurveVersion = TraversalMath.Version,
        };

    private sealed class Fixture
    {
        public Mock<IClusterClient> Cluster { get; } = new();
        public Mock<IZoneShardGrain> Shard { get; } = new();
        public Mock<IDSyncDataPlane> Plane { get; } = new();
        public Mock<ISessionBindingValidator> Binder { get; } = new();
        public Mock<IShardRouter> Router { get; } = new();
        public SyncTraversalHandler Handler { get; }

        public Fixture()
        {
            Router.Setup(r => r.Resolve(It.IsAny<long>())).Returns(ShardId);
            Cluster.Setup(c => c.GetGrain<IZoneShardGrain>(ShardId, It.IsAny<string>()))
                .Returns(Shard.Object);
            // Moq 默认 bool 为 false——不显式放行会让每个用例都卡在会话绑定校验上，
            // 那样"DS 路由"用例其实测的是拒绝分支（负面对照必须自己造）。
            Binder.Setup(b => b.IsCharacterBoundToConnection(It.IsAny<string>(), It.IsAny<long>())).Returns(true);
            Handler = new SyncTraversalHandler(
                NullLogger<MessageHandlerBase>.Instance,
                Cluster.Object,
                Router.Object,
                Plane.Object,
                Binder.Object,
                new Horizon.Game.Core.Handlers.SyncSessionState(),
                () => ConnId);
        }
    }

    [Fact]
    public async Task DsAuthoritative_RoutesToDs_AndReturnsNull()
    {
        var f = new Fixture();
        f.Plane.Setup(p => p.IsEnabled).Returns(true);
        f.Plane.Setup(p => p.IsSessionDsAuthoritative(ShardId, CharacterId)).Returns(true);
        f.Plane.Setup(p => p.TryRouteAsync(ShardId, CharacterId, It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var response = await f.Handler.HandleTraversalRequestAsync(Request());

        // 结果包由 DS 直接下行；网关不得再合成一份（否则客户端收到两次裁决）。
        Assert.Null(response);
        f.Plane.Verify(p => p.TryRouteAsync(ShardId, CharacterId, It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()), Times.Once);
        f.Cluster.Verify(c => c.GetGrain<IZoneShardGrain>(It.IsAny<long>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task DsRouteFails_FallsBackToGrain()
    {
        var f = new Fixture();
        f.Plane.Setup(p => p.IsEnabled).Returns(true);
        f.Plane.Setup(p => p.IsSessionDsAuthoritative(ShardId, CharacterId)).Returns(true);
        f.Plane.Setup(p => p.TryRouteAsync(ShardId, CharacterId, It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        f.Shard.Setup(s => s.SubmitTraversalRequestAsync(It.IsAny<TraversalRequestPacket>()))
            .ReturnsAsync(new TraversalResultPacket
            {
                CharacterId = CharacterId,
                ClientTick = 11L,
                Accepted = true,
                Status = 0,
                EndX = 0.8f, EndY = 0.6f, EndZ = 0.9f,
                ServerTick = 100L,
            });

        var response = await f.Handler.HandleTraversalRequestAsync(Request());

        var result = Assert.IsType<TraversalResultPacket>(response);
        Assert.True(result.Accepted);
        Assert.Equal(0, result.Status);
        f.Shard.Verify(s => s.SubmitTraversalRequestAsync(It.IsAny<TraversalRequestPacket>()), Times.Once);
    }

    [Fact]
    public async Task DsNotAuthoritativeForSession_UsesGrainPath()
    {
        var f = new Fixture();
        f.Plane.Setup(p => p.IsEnabled).Returns(true);
        f.Plane.Setup(p => p.IsSessionDsAuthoritative(ShardId, CharacterId)).Returns(false);
        f.Shard.Setup(s => s.SubmitTraversalRequestAsync(It.IsAny<TraversalRequestPacket>()))
            .ReturnsAsync(new TraversalResultPacket
            {
                CharacterId = CharacterId,
                ClientTick = 11L,
                Accepted = false,
                Status = (byte)TraversalRejectReason.Cooldown,
            });

        var response = await f.Handler.HandleTraversalRequestAsync(Request());

        var result = Assert.IsType<TraversalResultPacket>(response);
        Assert.False(result.Accepted);
        Assert.Equal((byte)TraversalRejectReason.Cooldown, result.Status);
        f.Plane.Verify(p => p.TryRouteAsync(It.IsAny<long>(), It.IsAny<long>(), It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SessionBindingMismatch_RejectedBeforeAnyAuthority()
    {
        var f = new Fixture();
        f.Binder.Setup(b => b.IsCharacterBoundToConnection(ConnId, CharacterId)).Returns(false);

        var response = await f.Handler.HandleTraversalRequestAsync(Request());

        var result = Assert.IsType<TraversalResultPacket>(response);
        Assert.False(result.Accepted);
        Assert.Equal((byte)TraversalRejectReason.AuthorityUnavailable, result.Status);
        f.Plane.Verify(p => p.TryRouteAsync(It.IsAny<long>(), It.IsAny<long>(), It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()), Times.Never);
        f.Cluster.Verify(c => c.GetGrain<IZoneShardGrain>(It.IsAny<long>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ZeroCharacterId_RejectedWithoutTouchingAnything()
    {
        var f = new Fixture();
        var response = await f.Handler.HandleTraversalRequestAsync(Request(characterId: 0UL));

        var result = Assert.IsType<TraversalResultPacket>(response);
        Assert.False(result.Accepted);
        f.Router.Verify(r => r.Resolve(It.IsAny<long>()), Times.Never);
        f.Cluster.Verify(c => c.GetGrain<IZoneShardGrain>(It.IsAny<long>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task GrainThrows_ReturnsAuthorityUnavailable_InsteadOfPropagating()
    {
        var f = new Fixture();
        f.Shard.Setup(s => s.SubmitTraversalRequestAsync(It.IsAny<TraversalRequestPacket>()))
            .ThrowsAsync(new InvalidOperationException("grain down"));

        var response = await f.Handler.HandleTraversalRequestAsync(Request());

        var result = Assert.IsType<TraversalResultPacket>(response);
        Assert.False(result.Accepted);
        Assert.Equal((byte)TraversalRejectReason.AuthorityUnavailable, result.Status);
        Assert.Equal(CharacterId, (long)result.CharacterId);
        Assert.Equal(11L, result.ClientTick);
    }
}
