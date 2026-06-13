using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Horizon.Game.Core.Sim.Server;
using Horizon.Game.Gateway.Services;
using Horizon.Game.Message.Sync;
using Moq;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// Step B 的 gateway runtime wiring 单测（<see cref="GatewayZoneShardFanoutSource"/> / 
/// <see cref="ConnectionManagerSessionRegistry"/> / <see cref="GameConnectionPacketSink"/>）。
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

    // ===== GameConnectionPacketSink =====

    [Fact]
    public void PacketSink_Send_EncodesAndForwardsToConnection()
    {
        var conn = new FakeConnection("c1", userId: 1, isConnected: true);
        var sink = new GameConnectionPacketSink();

        sink.Send(conn, new WorldChunkDiffPacket { ChunkMortonKey = 1 });
        Assert.Single(conn.Sent);
        Assert.True(conn.Sent[0].Length >= SyncPacketCodec.FrameHeaderSize);
        Assert.Equal(0, sink.FailedSendCount);
    }

    [Fact]
    public void PacketSink_DisconnectedEndpoint_Skipped()
    {
        var conn = new FakeConnection("c1", userId: 1, isConnected: false);
        var sink = new GameConnectionPacketSink();
        sink.Send(conn, new WorldChunkDiffPacket());
        Assert.Empty(conn.Sent);
    }

    [Fact]
    public void PacketSink_NonConnectionEndpoint_Skipped()
    {
        var sink = new GameConnectionPacketSink();
        sink.Send(new object(), new WorldChunkDiffPacket());
        Assert.Equal(0, sink.FailedSendCount);
    }

    // ===== End-to-end: source → dispatcher → sink =====

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
        var sink = new GameConnectionPacketSink();
        var dispatcher = new GatewaySyncDispatcher(src, registry, sink, enabled: true);

        await src.OnChunkDiffAsync(new WorldChunkDiffPacket { ChunkMortonKey = 99 }, new long[] { 1, 2, 99999 });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        await dispatcher.RunOnceAsync(cts.Token);

        Assert.Equal(1, dispatcher.ProcessedEventCount);
        Assert.Equal(2, dispatcher.DeliveredPacketCount);  // 只有 connA/connB 在线
        Assert.Equal(1, dispatcher.DroppedOfflineCount);   // 99999 不在线
        Assert.Single(connA.Sent);
        Assert.Single(connB.Sent);
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
        }

        public void Put(FakeConnection c)
        {
            if (c.UserId.HasValue) _byUser[c.UserId.Value] = c;
        }
    }
}
