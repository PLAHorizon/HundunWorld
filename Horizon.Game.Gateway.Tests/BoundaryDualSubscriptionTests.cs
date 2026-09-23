using System.Collections.Generic;
using System.Threading.Tasks;
using Horizon.Game.Core;
using Horizon.Game.Core.Handlers;
using Horizon.Game.Core.World;
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
/// 阶段 4.3 — 跨 Shard 边界双重订阅的行为测试：<br/>
/// 1) 位置驱动订阅（<see cref="SyncWorldHandler.HandleSubscriptionUpdateAsync"/>）向视口覆盖的全部 Shard
///    建立/更新 AOI 订阅，移出边界自动退订；<br/>
/// 2) 握手路径（<see cref="SyncPacketHandler"/>）在初始位置跨边界时同时向相邻 Shard 建立订阅；<br/>
/// 3) 单 Shard / 禁用空间分片时仅订阅主 Shard（行为回归）。
/// </summary>
public class BoundaryDualSubscriptionTests
{
    private const int ChunksPerShardX = 100;
    private const int ShardCount = 3;

    // ── SyncWorldHandler 位置驱动订阅 ─────────────────────────────────
    // characterId=3（3%3=0 → 主 shard 0；非零以通过 characterId==0 拒绝检查）

    [Fact]
    public async Task PositionUpdate_AtBoundary_SubscribesBothShards()
    {
        var (cluster, grains) = CreateClusterMock();
        var sessionState = new SyncSessionState();
        var handler = CreateWorldHandler(cluster, sessionState, CreateResolver());

        // x=1440m → chunkX=90，视口 [62,118] 跨 shard 0/1 边界；主 shard = Resolve(3) = 0
        await handler.HandleSubscriptionUpdateAsync(PositionUpdate(1440f), characterId: 3);

        grains[0].Verify(g => g.UpdateSessionPositionAsync(3, 1440f, 0f, 0f), Times.Once);
        grains[1].Verify(g => g.UpdateSessionPositionAsync(3, 1440f, 0f, 0f), Times.Once);
        grains[2].Verify(g => g.UpdateSessionPositionAsync(It.IsAny<long>(), It.IsAny<float>(), It.IsAny<float>(), It.IsAny<float>()), Times.Never);

        lock (sessionState.BoundaryShardLock)
        {
            Assert.Equal(new HashSet<long> { 0, 1 }, sessionState.BoundaryShardSubscriptions[3]);
        }
    }

    [Fact]
    public async Task PositionUpdate_MoveOutOfBoundary_RemovesStaleShardSubscription()
    {
        var (cluster, grains) = CreateClusterMock();
        var sessionState = new SyncSessionState();
        var handler = CreateWorldHandler(cluster, sessionState, CreateResolver());

        // 先在边界（订阅 {0,1}）
        await handler.HandleSubscriptionUpdateAsync(PositionUpdate(1440f), characterId: 3);
        // 再移回 shard 0 深处（x=400m → chunkX=25，视口 [-3,53] → 仅 shard 0）
        await handler.HandleSubscriptionUpdateAsync(PositionUpdate(400f), characterId: 3);

        grains[1].Verify(g => g.RemoveSessionAsync(3), Times.Once); // 移出边界自动退订
        grains[0].Verify(g => g.RemoveSessionAsync(It.IsAny<long>()), Times.Never); // 主 shard 保留

        lock (sessionState.BoundaryShardLock)
        {
            Assert.Equal(new HashSet<long> { 0 }, sessionState.BoundaryShardSubscriptions[3]);
        }
    }

    [Fact]
    public async Task PositionUpdate_DisabledResolver_SubscribesOnlyPrimary()
    {
        var (cluster, grains) = CreateClusterMock();
        var handler = CreateWorldHandler(cluster, new SyncSessionState(),
            new ShardRegionResolver(new ZoneBasedShardRouter(ShardCount), 0)); // 禁用空间分片

        await handler.HandleSubscriptionUpdateAsync(PositionUpdate(1440f), characterId: 3);

        grains[0].Verify(g => g.UpdateSessionPositionAsync(3, It.IsAny<float>(), It.IsAny<float>(), It.IsAny<float>()), Times.Once);
        grains[1].Verify(g => g.UpdateSessionPositionAsync(It.IsAny<long>(), It.IsAny<float>(), It.IsAny<float>(), It.IsAny<float>()), Times.Never);
    }

    [Fact]
    public async Task PositionUpdate_Interior_SubscribesOnlyPrimary()
    {
        var (cluster, grains) = CreateClusterMock();
        var handler = CreateWorldHandler(cluster, new SyncSessionState(), CreateResolver());

        // x=800m → chunkX=50，视口全在 shard 0 内
        await handler.HandleSubscriptionUpdateAsync(PositionUpdate(800f), characterId: 3);

        grains[0].Verify(g => g.UpdateSessionPositionAsync(3, It.IsAny<float>(), It.IsAny<float>(), It.IsAny<float>()), Times.Once);
        grains[1].Verify(g => g.UpdateSessionPositionAsync(It.IsAny<long>(), It.IsAny<float>(), It.IsAny<float>(), It.IsAny<float>()), Times.Never);
    }

    [Fact]
    public async Task PositionUpdate_AtCornerOfFourShards_SubscribesAllFour()
    {
        // 2×2 网格（ShardCount=4，ShardsPerRowX=2）：角点位置视口同时覆盖四个相邻 Shard
        const int gridShardCount = 4;
        var clusterClient = new Mock<IClusterClient>();
        var grains = new Dictionary<long, Mock<IZoneShardGrain>>();
        for (long i = 0; i < gridShardCount; i++)
        {
            var grain = new Mock<IZoneShardGrain>();
            grain.Setup(g => g.UpdateSessionPositionAsync(It.IsAny<long>(), It.IsAny<float>(), It.IsAny<float>(), It.IsAny<float>()))
                .ReturnsAsync(0);
            grain.Setup(g => g.RemoveSessionAsync(It.IsAny<long>())).ReturnsAsync(0);
            grains[i] = grain;
        }
        clusterClient
            .Setup(c => c.GetGrain<IZoneShardGrain>(It.IsAny<long>(), It.IsAny<string>()))
            .Returns((long id, string _) => grains[id].Object);

        var resolver = new ShardRegionResolver(
            new ZoneBasedShardRouter(gridShardCount), ChunksPerShardX, ChunksPerShardX, shardsPerRowX: 2);
        var sessionState = new SyncSessionState();
        var handler = CreateWorldHandler(clusterClient, sessionState, resolver, gridShardCount);

        // x=z=1600m → chunk(100,100) 恰为四 Shard 角点；视口 [72,128]² 同时覆盖四个单元
        await handler.HandleSubscriptionUpdateAsync(new SubscriptionUpdatePacket
        {
            HasPosition = true,
            PositionX = 1600f,
            PositionY = 0f,
            PositionZ = 1600f,
        }, characterId: 3);

        for (long i = 0; i < gridShardCount; i++)
        {
            grains[i].Verify(g => g.UpdateSessionPositionAsync(3, 1600f, 0f, 1600f), Times.Once);
        }
        lock (sessionState.BoundaryShardLock)
        {
            Assert.Equal(new HashSet<long> { 0, 1, 2, 3 }, sessionState.BoundaryShardSubscriptions[3]);
        }
    }

    // ── 握手路径建立边界订阅 ──────────────────────────────────────────

    [Fact]
    public async Task Handshake_AtBoundary_EstablishesDualSubscription()
    {
        var clusterClient = new Mock<IClusterClient>();
        var sessionGrain = new Mock<IPlayerSessionGrain>();
        clusterClient
            .Setup(c => c.GetGrain<IPlayerSessionGrain>(3L, It.IsAny<string>()))
            .Returns(sessionGrain.Object);
        sessionGrain
            .Setup(s => s.HandshakeAsync(It.IsAny<HandshakePacket>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<long>()))
            .ReturnsAsync(true);

        var grains = new Dictionary<long, Mock<IZoneShardGrain>>();
        for (long i = 0; i < ShardCount; i++)
        {
            var grain = new Mock<IZoneShardGrain>();
            grain.Setup(g => g.EnterWorldAsync(
                    It.IsAny<long>(), It.IsAny<ulong>(), It.IsAny<float>(), It.IsAny<float>(), It.IsAny<float>(),
                    It.IsAny<ulong[]>(), It.IsAny<float>()))
                .Returns(Task.CompletedTask);
            grain.Setup(g => g.UpdateSessionPositionAsync(It.IsAny<long>(), It.IsAny<float>(), It.IsAny<float>(), It.IsAny<float>()))
                .ReturnsAsync(0);
            grains[i] = grain;
        }
        clusterClient
            .Setup(c => c.GetGrain<IZoneShardGrain>(It.IsAny<long>(), It.IsAny<string>()))
            .Returns((long id, string _) => grains[id].Object);

        var handler = new SyncPacketHandler(
            NullLogger<MessageHandlerBase>.Instance,
            clusterClient.Object,
            new HorizonMessageAdapter(),
            new ZoneBasedShardRouter(ShardCount),
            regionResolver: CreateResolver());

        var handshake = new HandshakePacket
        {
            LocalCharacterId = 3,
            InitialClientTick = 100,
            FormulaVersion = 3,
            ProtocolVersion = SyncProtocolVersion.Current,
            InitialX = 1440f, InitialY = 0f, InitialZ = 0f, // 边界位置 → shard [0,1]；主 shard = 3%3 = 0
        };

        var (success, response) = await handler.RouteHandlerAsync(CreateHandshakeMessage(handshake));

        Assert.True(success);
        Assert.NotNull(response);
        // 权威实体仅注册在主 shard
        grains[0].Verify(g => g.EnterWorldAsync(3, (ulong)3, 1440f, 0f, 0f, It.IsAny<ulong[]>(), It.IsAny<float>()), Times.Once);
        grains[1].Verify(g => g.EnterWorldAsync(It.IsAny<long>(), It.IsAny<ulong>(), It.IsAny<float>(), It.IsAny<float>(), It.IsAny<float>(), It.IsAny<ulong[]>(), It.IsAny<float>()), Times.Never);
        // AOI 订阅向主 shard + 边界 shard 同时建立
        grains[0].Verify(g => g.UpdateSessionPositionAsync(3, 1440f, 0f, 0f), Times.Once);
        grains[1].Verify(g => g.UpdateSessionPositionAsync(3, 1440f, 0f, 0f), Times.Once);
        grains[2].Verify(g => g.UpdateSessionPositionAsync(It.IsAny<long>(), It.IsAny<float>(), It.IsAny<float>(), It.IsAny<float>()), Times.Never);
    }

    // ── 辅助 ───────────────────────────────────────────────────────────

    private static ShardRegionResolver CreateResolver()
        // 单行网格（ShardsPerRowX = ShardCount）= 1D 带状划分，仅沿 X 轴分片
        => new(new ZoneBasedShardRouter(ShardCount), ChunksPerShardX, ChunksPerShardX, ShardCount);

    private static SyncWorldHandler CreateWorldHandler(
        Mock<IClusterClient> clusterClient,
        SyncSessionState sessionState,
        IShardRegionResolver resolver,
        int shardCount = ShardCount)
    {
        return new SyncWorldHandler(
            NullLogger<MessageHandlerBase>.Instance,
            clusterClient.Object,
            new ZoneBasedShardRouter(shardCount),
            dsDataPlane: null,
            sessionBindingValidator: null,
            sessionState,
            () => "conn-1",
            resolver);
    }

    private static (Mock<IClusterClient> Cluster, Dictionary<long, Mock<IZoneShardGrain>> Grains) CreateClusterMock()
    {
        var clusterClient = new Mock<IClusterClient>();
        var grains = new Dictionary<long, Mock<IZoneShardGrain>>();
        for (long i = 0; i < ShardCount; i++)
        {
            var grain = new Mock<IZoneShardGrain>();
            grain.Setup(g => g.UpdateSessionPositionAsync(It.IsAny<long>(), It.IsAny<float>(), It.IsAny<float>(), It.IsAny<float>()))
                .ReturnsAsync(0);
            grain.Setup(g => g.RemoveSessionAsync(It.IsAny<long>())).ReturnsAsync(0);
            grains[i] = grain;
        }
        clusterClient
            .Setup(c => c.GetGrain<IZoneShardGrain>(It.IsAny<long>(), It.IsAny<string>()))
            .Returns((long id, string _) => grains[id].Object);
        return (clusterClient, grains);
    }

    private static SubscriptionUpdatePacket PositionUpdate(float x) => new()
    {
        HasPosition = true,
        PositionX = x,
        PositionY = 0f,
        PositionZ = 0f,
    };

    /// <summary>构造 Body 为编码了 <paramref name="handshake"/> 的 SyncFrameMessage 的 HorizonMessagePacket。</summary>
    private static HorizonMessagePacket CreateHandshakeMessage(HandshakePacket handshake)
    {
        SyncPacketCodec.Encode(handshake, out var frame, out var frameLength);
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
}
