using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Horizon.Game.Core;
using Horizon.Game.Core.Adapters;
using Horizon.Game.Core.Sim.Server;
using Horizon.Game.Gateway.Services;
using Horizon.Game.Message.Sync;
using Horizon.Game.Message.Sync.Components;
using Moq;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// Step B 的 gateway runtime wiring 单测（GatewayZoneShardFanoutSource / 
/// ConnectionManagerSessionRegistry / GameConnectionPacketSink）。
/// </summary>
public class GatewaySyncWiringTests
{
    // ===== GatewayZoneShardFanoutSource =====

    [Fact]
    public async Task FanoutSource_ObserverPush_CanBeDrainedBySource()
    {
        await using var src = new GatewayZoneShardFanoutSource();
        var diff = new WorldChunkDiffPacket { ChunkMortonKey = 7, DiffSeqStart = 1, DiffSeqEnd = 1 };

        await src.OnChunkDiffAsync(diff, new long[] { 101, 202 });

        Assert.Equal(1, src.ReceivedEventCount);
        Assert.Equal(1, src.PendingCount);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var evt = await src.TryDequeueAsync(cts.Token);
        Assert.NotNull(evt);
        Assert.Same(diff, evt!.Packet);
        Assert.Equal(new long[] { 101, 202 }, evt.TargetSessionIds.ToArray());
    }

    [Fact]
    public async Task FanoutSource_EmptySessionList_Ignored()
    {
        await using var src = new GatewayZoneShardFanoutSource();
        await src.OnChunkDiffAsync(new WorldChunkDiffPacket(), Array.Empty<long>());
        Assert.Equal(0, src.ReceivedEventCount);
        Assert.Equal(0, src.PendingCount);
    }

    [Fact]
    public async Task FanoutSource_TryDequeue_Cancellation_ReturnsNull()
    {
        await using var src = new GatewayZoneShardFanoutSource();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var evt = await src.TryDequeueAsync(cts.Token);
        Assert.Null(evt);
    }

    // ===== ConnectionManagerSessionRegistry =====

    [Fact]
    public void SessionRegistry_MapsUserIdToConnectedConnection()
    {
        var conn = new FakeConnection("c1", userId: 42, isConnected: true);
        var cm = new FakeConnectionManager();
        cm.Put(conn);

        var registry = new ConnectionManagerSessionRegistry(cm.Object);
        Assert.True(registry.TryGetEndpoint(42, out var ep));
        Assert.Same(conn, ep);
    }

    [Fact]
    public void SessionRegistry_DisconnectedConnection_ReturnsFalse()
    {
        var conn = new FakeConnection("c1", userId: 42, isConnected: false);
        var cm = new FakeConnectionManager();
        cm.Put(conn);

        var registry = new ConnectionManagerSessionRegistry(cm.Object);
        Assert.False(registry.TryGetEndpoint(42, out var ep));
        Assert.Null(ep);
    }

    [Fact]
    public void SessionRegistry_UnknownUserId_ReturnsFalse()
    {
        var registry = new ConnectionManagerSessionRegistry(new FakeConnectionManager().Object);
        Assert.False(registry.TryGetEndpoint(999, out var ep));
        Assert.Null(ep);
    }

    // ===== GameConnectionPacketSink（已修复：原测试因缺少 HorizonMessageAdapter 构造参数被注释） =====

    /// <summary>
    /// 原注释原因：GameConnectionPacketSink 构造函数需要 HorizonMessageAdapter 参数，
    /// 原测试使用无参构造 new GameConnectionPacketSink() 导致编译失败。
    /// 修复：注入 new HorizonMessageAdapter()。
    /// </summary>
    [Fact]
    public void PacketSink_Send_EncodesAndForwardsToConnection()
    {
        var conn = new FakeConnection("c1", userId: 1, isConnected: true);
        var adapter = new HorizonMessageAdapter();
        var sink = new GameConnectionPacketSink(adapter);

        sink.Send(conn, new WorldChunkDiffPacket { ChunkMortonKey = 1 });
        Assert.Single(conn.Sent);
        // 线路帧至少包含 8 字节线路头 + 6 字节同步帧头
        Assert.True(conn.Sent[0].Length >= SyncPacketCodec.FrameHeaderSize);
        Assert.Equal(0, sink.FailedSendCount);
    }

    [Fact]
    public void PacketSink_DisconnectedEndpoint_Skipped()
    {
        var conn = new FakeConnection("c1", userId: 1, isConnected: false);
        var adapter = new HorizonMessageAdapter();
        var sink = new GameConnectionPacketSink(adapter);
        sink.Send(conn, new WorldChunkDiffPacket());
        Assert.Empty(conn.Sent);
    }

    [Fact]
    public void PacketSink_NonConnectionEndpoint_Skipped()
    {
        var adapter = new HorizonMessageAdapter();
        var sink = new GameConnectionPacketSink(adapter);
        sink.Send(new object(), new WorldChunkDiffPacket());
        Assert.Equal(0, sink.FailedSendCount);
    }

    /// <summary>
    /// 验证 GameConnectionPacketSink 能正确编码 InteractionSyncPacket 并发送到连接。
    /// </summary>
    [Fact]
    public void PacketSink_Send_InteractionSyncPacket_EncodesAndForwards()
    {
        var conn = new FakeConnection("c1", userId: 1, isConnected: true);
        var adapter = new HorizonMessageAdapter();
        var sink = new GameConnectionPacketSink(adapter);

        var packet = new InteractionSyncPacket
        {
            SlotIdx = 3,
            InteractableId = 100L,
            InteractorId = 200L,
            StateBits = InteractionStateBits.Start,
            ServerTick = 42L,
        };

        sink.Send(conn, packet);
        Assert.Single(conn.Sent);
        Assert.True(conn.Sent[0].Length >= SyncPacketCodec.FrameHeaderSize);
        Assert.Equal(0, sink.FailedSendCount);
    }

    // ===== End-to-end: source → dispatcher → sink =====

    /// <summary>
    /// 原注释原因：GameConnectionPacketSink 无参构造不存在 + GatewaySyncDispatcher 构造参数顺序。
    /// 修复：注入 HorizonMessageAdapter；GatewaySyncDispatcher 使用命名参数 enabled: true。
    /// </summary>
    [Fact]
    public async Task EndToEnd_DispatcherDeliversToCorrectSessions()
    {
        await using var src = new GatewayZoneShardFanoutSource();
        var cm = new FakeConnectionManager();
        var connA = new FakeConnection("a", userId: 1, isConnected: true);
        var connB = new FakeConnection("b", userId: 2, isConnected: true);
        cm.Put(connA);
        cm.Put(connB);

        var registry = new ConnectionManagerSessionRegistry(cm.Object);
        var adapter = new HorizonMessageAdapter();
        var sink = new GameConnectionPacketSink(adapter);
        var dispatcher = new GatewaySyncDispatcher(src, registry, sink, enabled: true);

        await src.OnChunkDiffAsync(new WorldChunkDiffPacket { ChunkMortonKey = 99 }, new long[] { 1, 2, 99999 });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        await dispatcher.RunOnceAsync(cts.Token);

        Assert.Equal(1, dispatcher.ProcessedEventCount);
        Assert.Equal(2, dispatcher.DeliveredPacketCount);
        Assert.Equal(1, dispatcher.DroppedOfflineCount);
        Assert.Single(connA.Sent);
        Assert.Single(connB.Sent);
    }

    /// <summary>
    /// 端到端：InteractionSyncPacket 通过 fanout source → dispatcher → sink → 连接。
    /// 验证交互同步包能完整通过 gateway wiring 链路。
    /// </summary>
    [Fact]
    public async Task EndToEnd_InteractionSyncPacket_ThroughGatewayWiring()
    {
        await using var src = new GatewayZoneShardFanoutSource();
        var cm = new FakeConnectionManager();
        var conn = new FakeConnection("c1", userId: 10, isConnected: true);
        cm.Put(conn);

        var registry = new ConnectionManagerSessionRegistry(cm.Object);
        var adapter = new HorizonMessageAdapter();
        var sink = new GameConnectionPacketSink(adapter);
        var dispatcher = new GatewaySyncDispatcher(src, registry, sink, enabled: true);

        // 构造嵌入 InteractionSyncPacket 的 WorldChunkDiffPacket
        var interactionPacket = new InteractionSyncPacket
        {
            SlotIdx = 5,
            InteractableId = 111L,
            InteractorId = 222L,
            StateBits = InteractionStateBits.Start,
            ServerTick = 999L,
        };

        var diff = new WorldChunkDiffPacket
        {
            ChunkMortonKey = 1,
            DiffSeqStart = 1,
            DiffSeqEnd = 1,
            Payload = MemoryPack.MemoryPackSerializer.Serialize(interactionPacket),
            PayloadType = WorldChunkDiffPayloadType.InteractionSync,
        };

        await src.OnChunkDiffAsync(diff, new long[] { 10 });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        await dispatcher.RunOnceAsync(cts.Token);

        Assert.Equal(1, dispatcher.ProcessedEventCount);
        Assert.Equal(1, dispatcher.DeliveredPacketCount);
        Assert.Equal(0, dispatcher.DroppedOfflineCount);
        Assert.Single(conn.Sent);
        Assert.True(conn.Sent[0].Length >= SyncPacketCodec.FrameHeaderSize);
    }

    /// <summary>
    /// 端到端：WorldChunkDiffPacket + EntityDelta payload 通过 fanout source → dispatcher → sink → 连接。
    /// 验证 Update delta（移动/旋转/跳跃）能完整通过 gateway wiring 链路，
    /// 且 EntityDelta 的 Transform 坐标值在 SyncPacketCodec 编码/解码往返后保持不变。
    /// 这是"角色基础移动/旋转/跳跃网络不同步"修复的关键验证：
    /// 确认服务端 BroadcastSnapshotAsync 生成的 EntityDelta 能通过 Gateway 正确转发给客户端。
    /// </summary>
    [Fact]
    public async Task EndToEnd_EntityDeltaPacket_ThroughGatewayWiring()
    {
        await using var src = new GatewayZoneShardFanoutSource();
        var cm = new FakeConnectionManager();
        var conn = new FakeConnection("c1", userId: 10, isConnected: true);
        cm.Put(conn);

        var registry = new ConnectionManagerSessionRegistry(cm.Object);
        var adapter = new HorizonMessageAdapter();
        var sink = new GameConnectionPacketSink(adapter);
        var dispatcher = new GatewaySyncDispatcher(src, registry, sink, enabled: true);

        // 构造 EntityDelta — 模拟服务端 BroadcastSnapshotAsync 的输出
        // Transform 为 Flax Y-up 坐标（X=左右, Y=上下, Z=前后）
        // 与 ZoneShardGrain.TickAsync L650-659 的 ECS→Flax 转换一致：
        // X=entity.X(ECS X), Y=entity.Z(ECS Z→Flax Y), Z=entity.Y(ECS Y→Flax Z)
        var entityDelta = new EntityDelta
        {
            EntityId = 3001,
            Kind = EntityDeltaKind.Update,
            Transform = new AuthTransformComponent
            {
                X = 0.1f,    // ECS X = 0.1（左右位移）
                Y = 8.0f,    // ECS Z = 8.0 → Flax Y（上下）
                Z = 0.0f,    // ECS Y = 0.0 → Flax Z（前后）
                Pitch = 0f,
                Yaw = 1.57f, // 旋转 ~90度（弧度）
                Roll = 0f,
                ServerTick = 42,
            },
        };

        var diff = new WorldChunkDiffPacket
        {
            ChunkMortonKey = 1,
            DiffSeqStart = 1,
            DiffSeqEnd = 42,
            Payload = MemoryPack.MemoryPackSerializer.Serialize(new[] { entityDelta }),
            PayloadType = WorldChunkDiffPayloadType.EntityDelta,
        };

        // 1. 通过 fanout source → dispatcher → sink → 连接
        await src.OnChunkDiffAsync(diff, new long[] { 10 });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        await dispatcher.RunOnceAsync(cts.Token);

        Assert.Equal(1, dispatcher.ProcessedEventCount);
        Assert.Equal(1, dispatcher.DeliveredPacketCount);
        Assert.Equal(0, dispatcher.DroppedOfflineCount);
        Assert.Single(conn.Sent);
        Assert.True(conn.Sent[0].Length >= SyncPacketCodec.FrameHeaderSize);

        // 2. SyncPacketCodec 编码/解码往返验证
        // 确认 WorldChunkDiffPacket + EntityDelta payload 在编码后能正确解码还原
        SyncPacketCodec.Encode(diff, out var frame, out var frameLength);
        var decodedPacket = SyncPacketCodec.Decode(frame);

        Assert.NotNull(decodedPacket);
        var decodedDiff = Assert.IsType<WorldChunkDiffPacket>(decodedPacket);
        Assert.Equal(WorldChunkDiffPayloadType.EntityDelta, decodedDiff.PayloadType);
        Assert.Equal(42L, decodedDiff.DiffSeqEnd);
        Assert.Equal(1UL, decodedDiff.ChunkMortonKey);

        // 3. 反序列化 EntityDelta[] 并验证 Transform 坐标值保持不变
        Assert.NotNull(decodedDiff.Payload);
        Assert.True(decodedDiff.Payload.Length > 0);

        var decodedDeltas = MemoryPack.MemoryPackSerializer.Deserialize<EntityDelta[]>(decodedDiff.Payload);
        Assert.NotNull(decodedDeltas);
        Assert.Single(decodedDeltas);

        var delta = decodedDeltas![0];
        Assert.Equal(3001UL, delta.EntityId);
        Assert.Equal(EntityDeltaKind.Update, delta.Kind);

        // Transform 坐标值必须与原始值一致（Flax Y-up）
        Assert.True(delta.Transform.HasValue, "Update delta 必须包含 Transform");
        var t = delta.Transform.Value;
        Assert.True(MathF.Abs(t.X - 0.1f) < 0.001f, $"Transform.X 应为 0.1，实际 {t.X}");
        Assert.True(MathF.Abs(t.Y - 8.0f) < 0.001f, $"Transform.Y 应为 8.0，实际 {t.Y}");
        Assert.True(MathF.Abs(t.Z - 0.0f) < 0.001f, $"Transform.Z 应为 0.0，实际 {t.Z}");
        Assert.True(MathF.Abs(t.Yaw - 1.57f) < 0.001f, $"Transform.Yaw 应为 1.57，实际 {t.Yaw}");

        // 确认不是旧的 Y/Z 交换错误
        Assert.True(MathF.Abs(t.Y - 0f) > 1f, "Transform.Y 不应为 0（排除旧的 Y/Z 交换错误）");
        Assert.True(MathF.Abs(t.Z - 8f) > 1f, "Transform.Z 不应为 8（排除旧的 Y/Z 交换错误）");
    }

    // --- Fakes ---

    private sealed class FakeConnection : IGameConnection
    {
        public List<byte[]> Sent { get; } = new();
        public string ConnectionId { get; }
        public long? UserId { get; set; }
        public string RemoteAddress => "127.0.0.1";
        public DateTime ConnectedTime { get; } = DateTime.UtcNow;
        public DateTime LastActiveTime { get; set; } = DateTime.UtcNow;
        public bool IsConnected { get; private set; }
        public bool IsAuthenticated { get; set; }
        public string AuthToken { get; set; } = string.Empty;
        public Dictionary<string, object> Properties { get; } = new();
        public event EventHandler<ConnectionClosedEventArgs>? Closed;

        public FakeConnection(string id, long userId, bool isConnected)
        {
            ConnectionId = id; UserId = userId; IsConnected = isConnected;
        }
        public void SetProperty(string k, object v) => Properties[k] = v;
        public object? GetProperty(string k) => Properties.TryGetValue(k, out var v) ? v : null;
        public bool RemoveProperty(string k) => Properties.Remove(k);
        public Task SendAsync(byte[] data) { Sent.Add(data); return Task.CompletedTask; }
        public Task CloseAsync(string reason = "")
        {
            IsConnected = false;
            Closed?.Invoke(this, new ConnectionClosedEventArgs(ConnectionId, reason));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeConnectionManager : Mock<IConnectionManager>
    {
        private readonly ConcurrentDictionary<long, IGameConnection> _byUser = new();

        public FakeConnectionManager()
        {
            Setup(m => m.GetConnectionByUserId(It.IsAny<long>()))
                .Returns<long>(uid => _byUser.TryGetValue(uid, out var c) ? c : null);
            Setup(m => m.GetConnectionByCharacterId(It.IsAny<long>()))
                .Returns<long>(_ => null);
        }

        public void Put(FakeConnection c)
        {
            if (c.UserId.HasValue) _byUser[c.UserId.Value] = c;
        }
    }
}
