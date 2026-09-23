using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Horizon.Game.Core;
using Horizon.Game.Core.Handlers;
using Horizon.Game.Core.Interfaces;
using Horizon.Game.Core.Sim.Server;
using Horizon.Game.Core.World;
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
/// 阶段 4 — 多 Shard 水平扩展的行为测试：<br/>
/// 1) 握手响应回填 ShardId（4.1.1/4.1.2）；<br/>
/// 2) <see cref="ShardFanoutSubscriber"/> 全 shard fanout 订阅（修复原 Resolve(0) 单 shard 订阅）；<br/>
/// 3) <see cref="PlayerDespawnScheduler.RenewAllLeasesAsync"/> 按 shard 分组续约（修复原全部续约到 shard 0）。
/// </summary>
public class MultiShardScaleTests
{
    // ── 1. 路由覆盖 ──────────────────────────────────────────────────

    [Fact]
    public void ZoneBasedShardRouter_MultiShard_CoversAllShards_AndSingleShardUnchanged()
    {
        var multi = new ZoneBasedShardRouter(3);
        var resolved = Enumerable.Range(0, 30).Select(i => multi.Resolve(i)).Distinct().OrderBy(x => x).ToArray();
        Assert.Equal(new long[] { 0, 1, 2 }, resolved);
        Assert.Equal(2, multi.Resolve(44)); // 44 % 3 = 2
        Assert.Equal(0, multi.Resolve(45)); // 45 % 3 = 0

        // 单 Shard 快速路径：恒为 0（阶段 4 出口要求：单 shard 默认行为不变）
        var single = new ZoneBasedShardRouter(1);
        Assert.Equal(1, single.ShardCount);
        Assert.Equal(0, single.Resolve(42));
        Assert.Equal(0, single.Resolve(long.MaxValue));
    }

    // ── 2. 握手回填 ShardId ─────────────────────────────────────────

    [Fact]
    public async Task Handshake_Response_EchoesResolvedShardId()
    {
        var clusterClient = new Mock<IClusterClient>();
        var sessionGrain = new Mock<IPlayerSessionGrain>();
        clusterClient
            .Setup(c => c.GetGrain<IPlayerSessionGrain>(44L, It.IsAny<string>()))
            .Returns(sessionGrain.Object);
        sessionGrain
            .Setup(s => s.HandshakeAsync(It.IsAny<HandshakePacket>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<long>()))
            .ReturnsAsync(true);
        // IZoneShardGrain 未 mock（Moq 默认 null）：EnterWorldAsync/UpdateSessionPositionAsync 抛 NRE，
        // 被 handler 内 try/catch 吞掉（仅记录日志），不影响握手确认与 ShardId 回填。

        var handler = new SyncPacketHandler(
            NullLogger<MessageHandlerBase>.Instance,
            clusterClient.Object,
            new HorizonMessageAdapter(),
            new ZoneBasedShardRouter(3));
        var handshake = new HandshakePacket
        {
            LocalCharacterId = 44,
            InitialClientTick = 100,
            FormulaVersion = 3,
            ProtocolVersion = SyncProtocolVersion.Current,
        };

        var (success, response) = await handler.RouteHandlerAsync(CreateHandshakeMessage(handshake));

        Assert.True(success);
        var decoded = DecodeHandshakeResponse(response);
        Assert.IsType<HandshakePacket>(decoded);
        Assert.Equal(2, ((HandshakePacket)decoded).ShardId); // Resolve(44) = 2
        sessionGrain.Verify(
            s => s.HandshakeAsync(It.IsAny<HandshakePacket>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<long>()),
            Times.Once);
    }

    [Fact]
    public async Task Handshake_Response_SingleShard_ShardIdZero()
    {
        var clusterClient = new Mock<IClusterClient>();
        var sessionGrain = new Mock<IPlayerSessionGrain>();
        clusterClient
            .Setup(c => c.GetGrain<IPlayerSessionGrain>(42L, It.IsAny<string>()))
            .Returns(sessionGrain.Object);
        sessionGrain
            .Setup(s => s.HandshakeAsync(It.IsAny<HandshakePacket>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<long>()))
            .ReturnsAsync(true);

        // 默认路由兜底（单 Shard）
        var handler = new SyncPacketHandler(
            NullLogger<MessageHandlerBase>.Instance,
            clusterClient.Object,
            new HorizonMessageAdapter());
        var handshake = new HandshakePacket
        {
            LocalCharacterId = 42,
            InitialClientTick = 100,
            FormulaVersion = 3,
            ProtocolVersion = SyncProtocolVersion.Current,
        };

        var (success, response) = await handler.RouteHandlerAsync(CreateHandshakeMessage(handshake));

        Assert.True(success);
        var decoded = DecodeHandshakeResponse(response);
        Assert.IsType<HandshakePacket>(decoded);
        Assert.Equal(0, ((HandshakePacket)decoded).ShardId);
    }

    // ── 3. 全 shard fanout 订阅（ShardFanoutSubscriber）──────────────

    [Fact]
    public async Task ShardFanoutSubscriber_SubscribesAllShards_AndUnsubscribesAll()
    {
        var clusterClient = new Mock<IClusterClient>();
        var subscriptionId = Guid.NewGuid();
        var observer = Mock.Of<IZoneShardFanoutObserver>();
        var grains = new Dictionary<long, Mock<IZoneShardGrain>>();
        for (long i = 0; i < 3; i++)
        {
            var grain = new Mock<IZoneShardGrain>();
            grain.Setup(g => g.SubscribeFanoutAsync(subscriptionId, observer)).Returns(Task.CompletedTask);
            grain.Setup(g => g.UnsubscribeFanoutAsync(subscriptionId)).Returns(Task.CompletedTask);
            grains[i] = grain;
        }
        clusterClient
            .Setup(c => c.GetGrain<IZoneShardGrain>(It.IsAny<long>(), It.IsAny<string>()))
            .Returns((long id, string _) => grains[id].Object);

        var subscriber = new ShardFanoutSubscriber(
            clusterClient.Object,
            new ZoneBasedShardRouter(3),
            NullLogger.Instance,
            subscriptionId,
            observer);

        var ok = await subscriber.SubscribeAllAsync(CancellationToken.None);

        Assert.True(ok);
        Assert.Equal(3, subscriber.SubscribedShardIds.Count);
        Assert.Equal(subscriptionId, subscriber.SubscriptionId);
        foreach (var grain in grains.Values)
        {
            grain.Verify(g => g.SubscribeFanoutAsync(subscriptionId, observer), Times.Once);
        }

        // 幂等：再次订阅不重复调用（重订阅定时器路径）
        await subscriber.SubscribeAllAsync(CancellationToken.None);
        foreach (var grain in grains.Values)
        {
            grain.Verify(g => g.SubscribeFanoutAsync(subscriptionId, observer), Times.Once);
        }

        await subscriber.UnsubscribeAllAsync();
        foreach (var grain in grains.Values)
        {
            grain.Verify(g => g.UnsubscribeFanoutAsync(subscriptionId), Times.Once);
        }
        Assert.Empty(subscriber.SubscribedShardIds);
    }

    [Fact]
    public async Task ShardFanoutSubscriber_FailedShard_RetriedUntilSuccess()
    {
        var clusterClient = new Mock<IClusterClient>();
        var subscriptionId = Guid.NewGuid();
        var observer = Mock.Of<IZoneShardFanoutObserver>();

        // shard 1 前两次失败、第三次成功；其余 shard 一次成功
        var grain0 = new Mock<IZoneShardGrain>();
        grain0.Setup(g => g.SubscribeFanoutAsync(subscriptionId, observer)).Returns(Task.CompletedTask);
        var grain1 = new Mock<IZoneShardGrain>();
        var grain1Calls = 0;
        grain1.Setup(g => g.SubscribeFanoutAsync(subscriptionId, observer))
            .Returns(() =>
            {
                grain1Calls++;
                return grain1Calls < 3
                    ? Task.FromException(new TimeoutException("simulated"))
                    : Task.CompletedTask;
            });
        var grain2 = new Mock<IZoneShardGrain>();
        grain2.Setup(g => g.SubscribeFanoutAsync(subscriptionId, observer)).Returns(Task.CompletedTask);

        clusterClient
            .Setup(c => c.GetGrain<IZoneShardGrain>(It.IsAny<long>(), It.IsAny<string>()))
            .Returns((long id, string _) => id switch { 0 => grain0.Object, 1 => grain1.Object, _ => grain2.Object });

        var subscriber = new ShardFanoutSubscriber(
            clusterClient.Object,
            new ZoneBasedShardRouter(3),
            NullLogger.Instance,
            subscriptionId,
            observer,
            retryBaseDelayMs: 1);

        var ok = await subscriber.SubscribeAllAsync(CancellationToken.None);

        Assert.True(ok);
        Assert.Equal(3, subscriber.SubscribedShardIds.Count);
        Assert.Equal(3, grain1Calls); // 失败 2 次 + 成功 1 次
    }

    // ── 4. 按 shard 分组续约（PlayerDespawnScheduler）────────────────

    [Fact]
    public async Task RenewAllLeases_GroupsEntitiesByShard()
    {
        // ShardCount=3：42→0, 43→1, 44→2, 45→0
        var connectionManager = new Mock<IConnectionManager>();
        connectionManager.Setup(m => m.GetAllCharacterIds()).Returns(new List<long> { 42, 43, 44, 45 });
        connectionManager
            .Setup(m => m.GetConnectionByCharacterId(It.IsAny<long>()))
            .Returns((long _) => ConnectedConnection());

        var clusterClient = new Mock<IClusterClient>();
        var grains = new Dictionary<long, Mock<IZoneShardGrain>>();
        for (long i = 0; i < 3; i++)
        {
            var grain = new Mock<IZoneShardGrain>();
            grain.Setup(g => g.RenewLeaseAsync(It.IsAny<ulong[]>()))
                .Returns((ulong[] ids) => Task.FromResult(ids.Length));
            grains[i] = grain;
        }
        clusterClient
            .Setup(c => c.GetGrain<IZoneShardGrain>(It.IsAny<long>(), It.IsAny<string>()))
            .Returns((long id, string _) => grains[id].Object);

        var scheduler = new PlayerDespawnScheduler(
            clusterClient.Object,
            connectionManager.Object,
            Mock.Of<ICharacterPresenceStore>(),
            Mock.Of<ICharacterFingerprintService>(),
            NullLogger<PlayerDespawnScheduler>.Instance,
            new ZoneBasedShardRouter(3));

        await scheduler.RenewAllLeasesAsync();

        // 修复 BUG：原实现全部续约到 Resolve(0)，多 Shard 下其他 shard 的实体租约过期被孤儿清理。
        grains[0].Verify(g => g.RenewLeaseAsync(It.Is<ulong[]>(ids => ids.SequenceEqual(new ulong[] { 42, 45 }))), Times.Once);
        grains[1].Verify(g => g.RenewLeaseAsync(It.Is<ulong[]>(ids => ids.SequenceEqual(new ulong[] { 43 }))), Times.Once);
        grains[2].Verify(g => g.RenewLeaseAsync(It.Is<ulong[]>(ids => ids.SequenceEqual(new ulong[] { 44 }))), Times.Once);

        // 全部续约成功 → 不触发缺失实体检测
        foreach (var grain in grains.Values)
        {
            grain.Verify(g => g.GetRegisteredEntityIdsAsync(), Times.Never);
        }
    }

    [Fact]
    public async Task RenewAllLeases_SingleShard_StillRenewsOnShardZero()
    {
        // 单 Shard 回归：全部实体续约到 shard 0（行为与改造前一致）
        var connectionManager = new Mock<IConnectionManager>();
        connectionManager.Setup(m => m.GetAllCharacterIds()).Returns(new List<long> { 42, 43 });
        connectionManager
            .Setup(m => m.GetConnectionByCharacterId(It.IsAny<long>()))
            .Returns((long _) => ConnectedConnection());

        var clusterClient = new Mock<IClusterClient>();
        var grain0 = new Mock<IZoneShardGrain>();
        grain0.Setup(g => g.RenewLeaseAsync(It.IsAny<ulong[]>()))
            .Returns((ulong[] ids) => Task.FromResult(ids.Length));
        clusterClient
            .Setup(c => c.GetGrain<IZoneShardGrain>(It.IsAny<long>(), It.IsAny<string>()))
            .Returns(grain0.Object);

        var scheduler = new PlayerDespawnScheduler(
            clusterClient.Object,
            connectionManager.Object,
            Mock.Of<ICharacterPresenceStore>(),
            Mock.Of<ICharacterFingerprintService>(),
            NullLogger<PlayerDespawnScheduler>.Instance);

        await scheduler.RenewAllLeasesAsync();

        grain0.Verify(g => g.RenewLeaseAsync(It.Is<ulong[]>(ids => ids.SequenceEqual(new ulong[] { 42, 43 }))), Times.Once);
    }

    // ── 辅助 ─────────────────────────────────────────────────────────

    private static IGameConnection ConnectedConnection()
    {
        var conn = new Mock<IGameConnection>();
        conn.SetupGet(c => c.IsConnected).Returns(true);
        return conn.Object;
    }

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

    /// <summary>解码握手响应 Body（SyncFrameMessage → SyncPacket）。</summary>
    private static SyncPacket DecodeHandshakeResponse(HorizonMessagePacket? response)
    {
        Assert.NotNull(response);
        Assert.NotNull(response.Body);
        Assert.IsType<SyncFrameMessage>(response.Body);
        var syncFrame = (SyncFrameMessage)response.Body;
        var decoded = SyncPacketCodec.Decode(syncFrame.Frame.AsSpan(0, syncFrame.Frame.Length));
        Assert.NotNull(decoded);
        return decoded;
    }
}
