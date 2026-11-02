using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Horizon.Game.Core.Sim.Server;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 尸体拾取的五帧在**真实 TCP** 上过一次：发出去的字节与收回来的回执各自解回来。
/// <para>
/// 之前的证据都停在"编解码往返"与"处理器直接调用"两段：编解码绿不代表客户端那五个发送口真的把它发出去了
/// （计数器、丢弃分支、帧种类都可能各写各的）；处理器绿也不代表上行 switch 里有那一条 case。
/// 这条把 DS 侧真正用的 <see cref="DsUplinkClient"/> 接到一个真 socket 上，逐帧看字节。
/// </para>
/// </summary>
public sealed class CorpseLootSocketIntegrationTests : IAsyncLifetime
{
    private TestGateway _gateway = null!;
    private DsUplinkClient _uplink = null!;
    private readonly ConcurrentQueue<(DsUplinkMessageKind Kind, byte[] Payload)> _frames = new();
    private readonly TaskCompletionSource<DsCorpseLootResult> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>只做"收帧 + 对 Register/Heartbeat 回 ack + 对 Forward 回一条 Taken"的最小网关。</summary>
    private sealed class TestGateway : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private readonly Action<DsUplinkMessageKind, byte[]> _onFrame;
        private NetworkStream? _stream;

        public TestGateway(Action<DsUplinkMessageKind, byte[]> onFrame)
        {
            _onFrame = onFrame;
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            _ = Task.Run(() => AcceptLoopAsync(_cts.Token));
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public async Task ReplyAsync<T>(DsUplinkMessageKind kind, T payload)
        {
            var stream = _stream ?? throw new InvalidOperationException("DS 还没连上来");
            // 泛型入参：传 object 会装箱，MemoryPack 按 object 找 provider ⇒ "Object is not registered"。
            var frame = DsUplinkCodec.EncodeFrame(kind, payload);
            await stream.WriteAsync(frame);
            await stream.FlushAsync();
        }

        private async Task AcceptLoopAsync(CancellationToken ct)
        {
            var client = await _listener.AcceptTcpClientAsync(ct);
            client.NoDelay = true;
            _stream = client.GetStream();
            var reader = new DsFrameReader();
            var buffer = new byte[16384];
            while (!ct.IsCancellationRequested)
            {
                int n;
                try { n = await _stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct); }
                catch { return; }
                if (n <= 0) return;

                reader.Append(buffer.AsSpan(0, n));
                while (reader.TryReadFrame(out var payload))
                {
                    var kind = payload.Length > 0 ? (DsUplinkMessageKind)payload[0] : DsUplinkMessageKind.Unknown;
                    _onFrame(kind, payload.ToArray());
                    if (kind is DsUplinkMessageKind.Register or DsUplinkMessageKind.Heartbeat)
                    {
                        var ack = DsUplinkCodec.EncodeFrame(DsUplinkMessageKind.ControlAck,
                            new DsControlAck { Ok = true, Reason = string.Empty });
                        await _stream.WriteAsync(ack, ct);
                        await _stream.FlushAsync(ct);
                    }

                    reader.ConsumeFrame();
                }
            }
        }

        public void Dispose()
        {
            try { _cts.Cancel(); } catch { /* 忽略 */ }
            try { _listener.Stop(); } catch { /* 忽略 */ }
        }
    }

    private static int GetFreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    public Task InitializeAsync()
    {
        _gateway = new TestGateway((kind, payload) => _frames.Enqueue((kind, payload)));
        _uplink = new DsUplinkClient(new DsUplinkClientOptions
        {
            GatewayHost = "127.0.0.1",
            GatewayPort = _gateway.Port,
            DsId = "ds-corpse-loot",
            ShardIds = new[] { 5L },
            Host = "127.0.0.1",
            ListenPort = GetFreePort(),
            HeartbeatSeconds = 300,   // 心跳别插进这批帧里（它会让"等第一个 Forward"读到心跳）
        });
        _uplink.CorpseLootResultReceived += r => _result.TrySetResult(r);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        try { await _uplink.DisposeAsync(); } catch { /* 忽略 */ }
        _gateway.Dispose();
        await Task.CompletedTask;
    }

    private async Task<(bool Found, DsUplinkMessageKind Kind, byte[] Payload)> WaitFrameAsync(
        DsUplinkMessageKind kind, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            // 等的是**内容**（某种帧出现了），不是"计数器涨了"：计数涨一格可能来自别的帧。
            foreach (var f in _frames.ToArray())
                if (f.Kind == kind) return (true, f.Kind, f.Payload);
            await Task.Delay(10);
        }
        return (false, kind, Array.Empty<byte>());
    }

    [Fact]
    public async Task 登记转发作废三帧在真实TCP上来回一趟()
    {
        Assert.True(await _uplink.ConnectAsync(), "上行客户端没连上测试网关");
        Assert.True(await _uplink.RegisterAsync(), "控制面注册失败（后面的帧都没意义）");

        // ① 登记：参与者名单必须活着过线（`[Id(4)]` 那一格丢掉的症状是"名单到对岸变空 ⇒ 谁都能取"）
        var register = new DsCorpseLootRegister
        {
            CorpseEntityId = 42UL,
            ExpireTick = 1200,
            PickupDistanceMeters = 5.5f,
            Slots = new[] { new CorpseLootSlotWire { ItemId = 555, Count = 2 } },
            Contributors = new[] { 2001UL },
        };
        Assert.True(await _uplink.SendCorpseLootRegisterAsync(register));
        var (regSeen, _, regPayload) = await WaitFrameAsync(DsUplinkMessageKind.CorpseLootRegister, TimeSpan.FromSeconds(3));
        Assert.True(regSeen, "网关没收到登记帧");
        Assert.True(DsUplinkCodec.TryDecode<DsCorpseLootRegister>(regPayload, out _, out var regBack));
        Assert.Equal(42UL, regBack!.CorpseEntityId);
        Assert.Equal(1200L, regBack.ExpireTick);
        Assert.Equal(5.5f, regBack.PickupDistanceMeters);
        Assert.Single(regBack.Slots);
        Assert.Equal(555, regBack.Slots[0].ItemId);
        Assert.Equal(new[] { 2001UL }, regBack.Contributors);
        Assert.Equal(1L, _uplink.SentCorpseLootRegisterCount);

        // ② 转发：请求号与取的人必须原样到对岸；回执按请求号回来，客户端事件要拿到它
        Assert.True(await _uplink.SendCorpseLootForwardAsync(new DsCorpseLootForward
        {
            SessionId = 2001, CorpseEntityId = 42UL, SlotIndex = -1, RequestId = 41,
            ServerTick = 700, TakerCharacterId = 2001UL,
        }));
        var (fwdSeen, _, fwdPayload) = await WaitFrameAsync(DsUplinkMessageKind.CorpseLootForward, TimeSpan.FromSeconds(3));
        Assert.True(fwdSeen, "网关没收到转发帧");
        Assert.True(DsUplinkCodec.TryDecode<DsCorpseLootForward>(fwdPayload, out _, out var fwdBack));
        Assert.Equal(41L, fwdBack!.RequestId);
        Assert.Equal(-1, fwdBack.SlotIndex);
        Assert.Equal(2001UL, fwdBack.TakerCharacterId);
        Assert.Equal(700L, fwdBack.ServerTick);

        await _gateway.ReplyAsync(DsUplinkMessageKind.CorpseLootResult, new DsCorpseLootResult
        {
            RequestId = 41, SessionId = 2001, SlotIndex = 3,
            Status = (byte)CorpseLootTakeStatus.Taken, ItemId = 777, Count = 2,
        });
        var reply = await Task.WhenAny(_result.Task, Task.Delay(TimeSpan.FromSeconds(3))) == _result.Task
            ? await _result.Task
            : null;
        Assert.NotNull(reply);
        Assert.Equal(41L, reply!.RequestId);
        Assert.Equal(3, reply.SlotIndex);
        Assert.Equal((byte)CorpseLootTakeStatus.Taken, reply.Status);
        Assert.Equal(777, reply.ItemId);
        Assert.Equal(2, reply.Count);
        Assert.Equal(1L, _uplink.CorpseLootResultReceivedCount);

        // ③ 作废：也真的发出去（只在 DS 本地删掉登记，权威侧会挂着一具幽灵尸体）
        Assert.True(await _uplink.SendCorpseLootGoneAsync(new DsCorpseLootGone { CorpseEntityId = 42UL }));
        var (goneSeen, _, gonePayload) = await WaitFrameAsync(DsUplinkMessageKind.CorpseLootGone, TimeSpan.FromSeconds(3));
        Assert.True(goneSeen, "网关没收到作废帧");
        Assert.True(DsUplinkCodec.TryDecode<DsCorpseLootGone>(gonePayload, out _, out var goneBack));
        Assert.Equal(42UL, goneBack!.CorpseEntityId);
        Assert.Equal(1L, _uplink.SentCorpseLootGoneCount);
    }
}
