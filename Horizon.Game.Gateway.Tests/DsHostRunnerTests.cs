using System;
using System.Buffers;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Horizon.DsHost;
using Horizon.Game.Core.Sim.Server;
using Horizon.Game.Message.Sync;
using Horizon.Game.Sim;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// MMORPG 开发计划 P3 —— 无头 DS 宿主链路与上行协议端到端测试。
/// <para>
/// 两层验证：
/// <list type="number">
///   <item><b>tick 链路</b>：Handshake 上行 → 世界注册 → 60Hz 推进 → 编码 → 假 sender 收到可解码批次。
///         这层不需要任何 socket，覆盖"模拟↔协议"接缝。</item>
///   <item><b>真实 socket 互通</b>：起一个只用 <see cref="DsFrameReader"/>/<see cref="DsUplinkCodec"/>
///         实现的"假网关"，让真实的 <see cref="DsUplinkClient"/> 连上去完成
///         注册/心跳/快照回传。这层覆盖"DS↔网关"接缝的**线上字节**兼容性——
///         两端不共享运行时类型，只共享协议实现，因此必须用真实字节验证。</item>
/// </list>
/// </para>
/// </summary>
public class DsHostRunnerTests
{
    private sealed class CapturingSender : IDsDownlinkSender
    {
        public List<DsSnapshotBatch> Batches { get; } = new();
        public bool Accept { get; set; } = true;
        public Task<bool> SendSnapshotBatchAsync(DsSnapshotBatch batch, CancellationToken cancellationToken = default)
        {
            if (!Accept) return Task.FromResult(false);
            Batches.Add(batch);
            return Task.FromResult(true);
        }
    }

    private static DsHostOptions MakeHostOptions(int spawnPoints = 1)
    {
        var options = new DsHostOptions
        {
            DsId = "ds-test",
            ShardIds = new long[] { 0 },
            TicksPerSecond = 60,
            ListenPort = 0, // 测试不监听 socket
        };

        for (var i = 0; i < spawnPoints; i++)
        {
            var cfg = SpawnPointConfig.Default;
            cfg.SpawnPointId = i + 1;
            cfg.X = 10f + i * 40f;
            cfg.MaxAlive = 2;
            options.SpawnPoints.Add(cfg);
        }

        return options;
    }

    private static HandshakePacket MakeHandshake(ulong characterId, float x, float y, float z)
        => new HandshakePacket
        {
            ProtocolVersion = SyncProtocolVersion.Current,
            FormulaVersion = MovementFormula.FormulaVersion,
            LocalCharacterId = characterId,
            InitialX = x,
            InitialY = y,
            InitialZ = z,
        };

    /// <summary>
    /// 构造一帧"真实客户端"输入。
    /// <para>
    /// <b>必须填 <c>PredictedEnd*</c></b>：服务端 <c>SimWorld</c> 以它作为权威回放的比对基准，
    /// 并在实体首次输入时用它对齐位置（见 <c>SimWorld.SubmitInput</c> 的"首次输入位置对齐"分支）。
    /// 留 0 等于"客户端声称自己没动"，服务端会持续下发修正——这不是服务端 bug，
    /// 而是测试在伪造一个不存在的客户端行为。
    /// </para>
    /// </summary>
    private static InputPacket MakeInput(ulong characterId, long clientTick, float moveX, float predictedEndX = 0f)
        => new InputPacket
        {
            CharacterId = characterId,
            ClientTick = clientTick,
            MoveX = moveX,
            MoveY = 0f,
            PredictedEndX = predictedEndX,
            PredictedEndY = 0f,
            PredictedEndZ = 0f,
            MaxSpeed = MovementFormula.DefaultMaxSpeed,
        };

    // ---------- tick 链路 ----------

    [Fact]
    public async Task HandshakeThenTicks_ProducesDecodableDownlinkBatch()
    {
        var sender = new CapturingSender();
        await using var runner = new DsHostRunner(MakeHostOptions(), sender);

        // 模拟网关把 Handshake 帧投递到数据面
        runner.DataPlane.Apply(MakeHandshake(2001, 3f, 4f, 0f));
        Assert.Equal(1, runner.World.PlayerCount);

        DsSnapshotBatch? snapshotBatch = null;
        for (var i = 0; i < 6; i++)
        {
            var result = await runner.RunSingleTickAsync();
            if (result.WorldSnapshot is not null) snapshotBatch = sender.Batches[sender.Batches.Count - 1];
        }

        Assert.NotNull(snapshotBatch);
        // 6 个 tick、快照间隔 3 → 恰好 2 次快照下线（tick 3 与 tick 6）。
        Assert.Equal(2, runner.BatchesSent);
        Assert.Equal(0, runner.BatchesFailed);

        // 批次里应有一帧世界快照，受众 = 在线会话
        var item = snapshotBatch!.Items[0];
        Assert.Equal(new long[] { 2001 }, item.SessionIds);

        var decoded = SyncPacketCodec.Decode(item.Frame);
        var snapshot = Assert.IsType<SnapshotPacket>(decoded);
        Assert.Contains(snapshot.Deltas, d => d.EntityId == 2001UL);
    }

    [Fact]
    public async Task InputPacket_IsAppliedToWorld()
    {
        var sender = new CapturingSender();
        await using var runner = new DsHostRunner(MakeHostOptions(spawnPoints: 0), sender);

        runner.DataPlane.Apply(MakeHandshake(2001, 0f, 0f, 0f));

        // 模拟真实客户端：自己维护预测终点并随输入上报。
        const int ticks = 30;
        var predictedX = 0f;
        var stepPerTick = MovementFormula.DefaultMaxSpeed / 60f;

        for (var i = 0; i < ticks; i++)
        {
            predictedX += stepPerTick;
            runner.DataPlane.Apply(MakeInput(2001, i + 1, 1f, predictedX));
            await runner.RunSingleTickAsync();
        }

        Assert.Equal(ticks, runner.World.ServerTick);

        // 玩家应已沿 +X 前进：取最后一个含世界快照的批次（非快照 tick 的批次可能为空）。
        var snapshots = AllSnapshots(sender);
        Assert.NotEmpty(snapshots);
        var snapshot = snapshots[snapshots.Count - 1];
        var playerDelta = Assert.Single(snapshot.Deltas, d => d.EntityId == 2001UL);
        Assert.True(playerDelta.Transform!.Value.X > 0f,
            $"玩家应已移动。X={playerDelta.Transform!.Value.X} 预测X={predictedX} 批次总数={sender.Batches.Count}");
    }

    [Fact]
    public async Task Monsters_SpawnAndAppearInDownlinkedSnapshot()
    {
        var sender = new CapturingSender();
        await using var runner = new DsHostRunner(MakeHostOptions(spawnPoints: 2), sender);

        runner.DataPlane.Apply(MakeHandshake(2001, 0f, 0f, 0f));
        for (var i = 0; i < 6; i++) await runner.RunSingleTickAsync();

        Assert.True(runner.World.MonsterCount == 4,
            $"应为 2 刷怪点 × MaxAlive 2 = 4，实际 {runner.World.MonsterCount}；" +
            $"hostSpawnPoints={MakeHostOptions(spawnPoints: 2).SpawnPoints.Count} ticks={runner.TicksExecuted}");

        // 第 1 次快照（tick 3）是 Spawn 帧：携带 Identity，可据此按类型计数。
        var spawnSnapshot = DecodeFirstSnapshot(sender);
        var monsterSpawns = new List<EntityDelta>();
        foreach (var d in spawnSnapshot.Deltas)
        {
            if (d.Identity is { } id && id.EntityType == DsWorldSession.EntityTypeMonster) monsterSpawns.Add(d);
        }
        Assert.Equal(4, monsterSpawns.Count);

        // 后续快照是 Update 帧（不重复携带 Identity），但实体 ID 必须仍在——
        // 这条断言防的是"怪只出现一帧就消失"这类回归。
        var latestSnapshot = DecodeLatestSnapshot(sender);
        var monsterEntityIds = latestSnapshot.Deltas
            .Where(d => (d.EntityId >> 32) != 0UL)
            .Select(d => d.EntityId)
            .Distinct()
            .ToList();
        Assert.Equal(4, monsterEntityIds.Count);
    }

    [Fact]
    public async Task SendFailure_IsCountedNotThrown()
    {
        var sender = new CapturingSender { Accept = false };
        await using var runner = new DsHostRunner(MakeHostOptions(spawnPoints: 0), sender);

        runner.DataPlane.Apply(MakeHandshake(2001, 0f, 0f, 0f));
        for (var i = 0; i < 6; i++) await runner.RunSingleTickAsync();

        Assert.True(runner.BatchesFailed > 0);
        Assert.Equal(0, runner.BatchesSent);
    }

    [Fact]
    public async Task UnknownPackets_DoNotThrow()
    {
        await using var runner = new DsHostRunner(MakeHostOptions(spawnPoints: 0), null);

        // 未握手就来的输入、以及无关包型：都应被安静忽略，而不是让宿主崩溃。
        runner.DataPlane.Apply(MakeInput(9999, 1, 1f));
        runner.DataPlane.Apply(new InputAckPacket { LastProcessedClientTick = 1 });

        await runner.RunSingleTickAsync();
        Assert.Equal(0, runner.World.PlayerCount);
    }

    // ---------- 真实 socket：DsUplinkClient ↔ 假网关 ----------

    [Fact]
    public async Task UplinkClient_RegisterHeartbeatAndBatch_RoundTripsOverRealSockets()
    {
        using var server = new FakeGatewayUplinkServer();
        await server.StartAsync();

        var options = new DsUplinkClientOptions
        {
            DsId = "ds-socket-test",
            ShardIds = new long[] { 0, 3 },
            Host = "127.0.0.1",
            ListenPort = 19999,
            Region = "local",
            GatewayHost = "127.0.0.1",
            GatewayPort = server.Port,
            Token = "tok",
            AckTimeoutMs = 5000,
        };

        await using var client = new DsUplinkClient(options);

        Assert.True(await client.ConnectAsync());
        Assert.True(await client.RegisterAsync());

        var registered = await server.WaitForRegistrationAsync();
        Assert.Equal("ds-socket-test", registered.DsId);
        Assert.Equal(new long[] { 0, 3 }, registered.ShardIds);
        Assert.Equal(19999, registered.Port);
        Assert.Equal("local", registered.Region);
        Assert.Equal("tok", registered.Token);

        Assert.True(await client.HeartbeatAsync());
        Assert.Equal(1, await server.WaitForHeartbeatCountAsync(1));

        // 快照批次：单向下行，网关侧应能解码出目标会话与帧
        var batch = new DsSnapshotBatch
        {
            Items = new[]
            {
                new DsSnapshotItem
                {
                    SessionIds = new long[] { 2001, 2002 },
                    Frame = EncodeSnapshot(1234),
                },
            },
        };

        Assert.True(await client.SendSnapshotBatchAsync(batch));
        var received = await server.WaitForBatchAsync();
        Assert.Single(received.Items);
        Assert.Equal(new long[] { 2001, 2002 }, received.Items[0].SessionIds);

        var decoded = Assert.IsType<SnapshotPacket>(SyncPacketCodec.Decode(received.Items[0].Frame));
        Assert.Equal(1234, decoded.ServerTick);

        Assert.True(await client.UnregisterAsync());
        Assert.True(await server.WaitForUnregisterAsync());
    }

    [Fact]
    public async Task UplinkClient_NoGatewayPort_FailsFastWithoutThrowing()
    {
        var options = new DsUplinkClientOptions { GatewayPort = 0, DsId = "ds-test" };
        await using var client = new DsUplinkClient(options);
        Assert.False(await client.ConnectAsync());
        Assert.False(client.IsConnected);
    }

    [Fact]
    public async Task UplinkClient_ConnectFailure_ReturnsFalse()
    {
        // 指向一个必然无人监听的端口：客户端必须"失败返回"而不是抛异常或永久阻塞。
        var options = new DsUplinkClientOptions
        {
            GatewayHost = "127.0.0.1",
            GatewayPort = 1,
            DsId = "ds-test",
            AckTimeoutMs = 500,
        };
        await using var client = new DsUplinkClient(options);
        Assert.False(await client.ConnectAsync());
    }


    /// <summary>
    /// 把捕获到的所有批次**逐项**解出世界快照（按发送顺序）。
    /// <para>为什么不能只看每项的 <c>Items[0]</c>：同一个批次里除了快照还会带事件包
    /// （施法条起手/伤害/Despawn 全广播），条目顺序随现场变化——硬转第一个会在
    /// "事件排在快照前面"的现场抛 <c>InvalidCastException</c>。
    /// 实测：单跑这条永远绿，全量并行跑时偶发红，就是这个坏尺造成的假故障。</para>
    /// </summary>
    private static List<SnapshotPacket> AllSnapshots(CapturingSender sender)
    {
        var list = new List<SnapshotPacket>();
        foreach (var batch in sender.Batches)
        {
            foreach (var item in batch.Items)
            {
                if (SyncPacketCodec.Decode(item.Frame) is SnapshotPacket snapshot) list.Add(snapshot);
            }
        }
        return list;
    }

    /// <summary>取第一个含世界快照的批次（= Spawn 帧）。</summary>
    private static SnapshotPacket DecodeFirstSnapshot(CapturingSender sender)
    {
        var snapshots = AllSnapshots(sender);
        if (snapshots.Count == 0) throw new InvalidOperationException("未捕获到任何世界快照批次。");
        return snapshots[0];
    }

    /// <summary>取最后一个含世界快照的批次。</summary>
    private static SnapshotPacket DecodeLatestSnapshot(CapturingSender sender)
    {
        var snapshots = AllSnapshots(sender);
        if (snapshots.Count == 0) throw new InvalidOperationException("未捕获到任何世界快照批次。");
        return snapshots[snapshots.Count - 1];
    }

    private static byte[] EncodeSnapshot(long serverTick)
    {
        var packet = new SnapshotPacket
        {
            ProtocolVersion = SyncProtocolVersion.Current,
            ServerTick = serverTick,
            BaselineTick = 0,
            Deltas = Array.Empty<EntityDelta>(),
        };

        SyncPacketCodec.Encode(packet, out var rented, out var length);
        try
        {
            var exact = new byte[length];
            Buffer.BlockCopy(rented, 0, exact, 0, length);
            return exact;
        }
        finally
        {
            SyncPacketCodec.ReturnFrame(rented);
        }
    }

    /// <summary>
    /// 最小"假网关"：只用协议实现（<see cref="DsFrameReader"/> + <see cref="DsUplinkCodec"/>）
    /// 收发 DS 上行，不依赖 TouchSocket 与网关 DI。它验证的是**线上字节**契约。
    /// </summary>
    private sealed class FakeGatewayUplinkServer : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _cts = new();
        private readonly TaskCompletionSource<DsRegisterRequest> _registration = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<DsSnapshotBatch> _batch = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _unregistered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _heartbeats;
        private Task? _loop;

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public Task StartAsync()
        {
            _listener.Start();
            _loop = Task.Run(() => AcceptLoopAsync(_cts.Token));
            return Task.CompletedTask;
        }

        private async Task AcceptLoopAsync(CancellationToken ct)
        {
            try
            {
                using var client = await _listener.AcceptTcpClientAsync(ct);
                client.NoDelay = true;
                using var stream = client.GetStream();
                var reader = new DsFrameReader();
                var buffer = ArrayPool<byte>.Shared.Rent(16384);

                try
                {
                    while (!ct.IsCancellationRequested)
                    {
                        var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct);
                        if (read <= 0) break;

                        reader.Append(buffer.AsSpan(0, read));
                        while (reader.TryReadFrame(out var payload))
                        {
                            Handle(payload, stream);
                            reader.ConsumeFrame();
                        }
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }
            }
            catch (Exception)
            {
                // 测试关闭期忽略
            }
        }

        private void Handle(ReadOnlySpan<byte> payload, NetworkStream stream)
        {
            if (payload.Length < 1) return;
            var kind = (DsUplinkMessageKind)payload[0];

            switch (kind)
            {
                case DsUplinkMessageKind.Register:
                    if (DsUplinkCodec.TryDecode<DsRegisterRequest>(payload, out _, out var req) && req is not null)
                    {
                        _registration.TrySetResult(req);
                    }
                    SendAck(stream, ok: true, reason: string.Empty);
                    break;

                case DsUplinkMessageKind.Heartbeat:
                    Interlocked.Increment(ref _heartbeats);
                    SendAck(stream, ok: true, reason: string.Empty);
                    break;

                case DsUplinkMessageKind.Unregister:
                    _unregistered.TrySetResult(true);
                    SendAck(stream, ok: true, reason: string.Empty);
                    break;

                case DsUplinkMessageKind.SnapshotBatch:
                    if (DsUplinkCodec.TryDecode<DsSnapshotBatch>(payload, out _, out var batch) && batch is not null)
                    {
                        _batch.TrySetResult(batch);
                    }
                    break;
            }
        }

        private static void SendAck(NetworkStream stream, bool ok, string reason)
        {
            var frame = DsUplinkCodec.EncodeFrame(
                DsUplinkMessageKind.ControlAck,
                new DsControlAck { Ok = ok, Reason = reason });
            stream.Write(frame, 0, frame.Length);
            stream.Flush();
        }

        public Task<DsRegisterRequest> WaitForRegistrationAsync() => _registration.Task.WaitAsync(TimeSpan.FromSeconds(10));
        public Task<DsSnapshotBatch> WaitForBatchAsync() => _batch.Task.WaitAsync(TimeSpan.FromSeconds(10));
        public Task<bool> WaitForUnregisterAsync() => _unregistered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        public async Task<int> WaitForHeartbeatCountAsync(int expected)
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < deadline)
            {
                var n = Volatile.Read(ref _heartbeats);
                if (n >= expected) return n;
                await Task.Delay(20);
            }
            return Volatile.Read(ref _heartbeats);
        }

        public void Dispose()
        {
            try { _cts.Cancel(); } catch { /* 忽略 */ }
            try { _listener.Stop(); } catch { /* 忽略 */ }
            _cts.Dispose();
        }
    }
}
