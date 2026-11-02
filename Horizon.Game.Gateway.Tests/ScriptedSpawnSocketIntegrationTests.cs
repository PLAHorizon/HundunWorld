using Horizon.DsHost;
using Horizon.Game.Core.Security;
using Horizon.Game.Core.Sim.Server;
using Horizon.Game.Message.Network;
using Horizon.Game.Message.Security;
using Horizon.Game.Message.Sync;
using Horizon.Orleans.Grains.World;
using Horizon.Orleans.Interface.World;
using MemoryPack;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Orleans.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 跨进程接缝：Grain 裁决 → **真实 TCP 上行链路** → 真实 DsHost 宿主 → 真实世界。
/// <para>
/// 分工是这样错开的：<see cref="ScriptedSpawnEndToEndTests"/> 把网关的"投递"接成了同进程方法调用，
/// <see cref="DsHostScriptedLiveTests"/> 从真实进程外部灌帧但没有 Grain，
/// <see cref="ScriptedSpawnFrameTests"/> 只验字节格式。
/// 于是这条组合路径——**Grain 亲手签发的令牌，经真实 socket 与真实 <see cref="DsUplinkClient"/> 解析，
/// 经真实 <see cref="DsHostRunner"/> 的队列在 tick 前消费，最后由真实 <see cref="DsWorldSession"/> 出怪**
/// ——从来没有人整跑过。它最容易断在一行装配代码上：
/// <c>uplink.ScriptedSpawnReceived += runner.NotifyScriptedSpawn;</c>（DsHost/Program.cs）。
/// 漏这一行不会有任何报错，只表现为"指令发出去、世界毫无动静"。
/// </para>
/// </summary>
public sealed class ScriptedSpawnSocketIntegrationTests : IAsyncLifetime
{
    private const long OperatorId = 7001;
    private const long ShardId = 5;

    private static readonly byte[] Key = Enumerable.Repeat((byte)0x4E, 32).ToArray();
    private static readonly string KeyB64 = Convert.ToBase64String(Key);

    private TestGateway _gateway = null!;
    private DsUplinkClient _uplink = null!;
    private DsHostRunner _runner = null!;

    private sealed class TestGateway : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private NetworkStream? _stream;
        public readonly List<string> Logs = new();

        public TestGateway()
        {
            _listener = new TcpListener(IPAddress.Loopback, GetFreePort());
            _listener.Start();
            _ = Task.Run(() => AcceptLoopAsync(_cts.Token));
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public void Push(ScriptGrantToken token)
        {
            var stream = _stream ?? throw new InvalidOperationException("DS 还没连上来");
            var frame = DsUplinkCodec.EncodeFrame(DsUplinkMessageKind.ScriptedSpawn,
                new DsScriptedSpawnEnvelope { Token = token });
            stream.Write(frame);
            stream.Flush();
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

    public async Task InitializeAsync()
    {
        _gateway = new TestGateway();
        var listenPort = GetFreePort();

        _uplink = new DsUplinkClient(new DsUplinkClientOptions
        {
            GatewayHost = "127.0.0.1",
            GatewayPort = _gateway.Port,
            DsId = "ds-in-proc",
            ShardIds = new[] { ShardId },
            Host = "127.0.0.1",
            ListenPort = listenPort,
            HeartbeatSeconds = 1,
        });

        _runner = new DsHostRunner(new DsHostOptions
        {
            DsId = "ds-in-proc",
            ShardIds = new[] { ShardId },
            ListenPort = listenPort,
            TicksPerSecond = 60,
            EnableScriptedSpawn = true,
            ScriptedSigningKeyBase64 = KeyB64,
        }, sender: _uplink, log: null);

        // 与 Horizon.DsHost/Program.cs 同一行装配。这里刻意照抄而不是各自另写：
        // 装配漏了就该在测试里红，而不是在线上静默失灵。
        _uplink.ScriptedSpawnReceived += _runner.NotifyScriptedSpawn;

        Assert.True(await _uplink.ConnectAsync(), "DS 上行客户端未能连上测试网关");
        Assert.True(await _uplink.RegisterAsync(), "DS 注册失败（控制面没通，后面的断言无意义）");

        // 快照只在有受众时构建，故放一个在线观众；回执仍定向发给操作者。
        Assert.True(_runner.World.AddPlayer(1001, 1001, 0f, 0f, 0f, out var reason), reason);
    }

    public async Task DisposeAsync()
    {
        try { await _uplink.DisposeAsync(); } catch { /* 忽略 */ }
        try { await _runner.DisposeAsync(); } catch { /* 忽略 */ }
        _gateway.Dispose();
        await Task.CompletedTask;
    }

    /// <summary>真 Grain（只把 Orleans 的持久层与位置存储换成替身）签发一枚令牌。</summary>
    private static async Task<ScriptGrantToken> IssueTokenAsync(ScriptOp op = ScriptOp.Spawn)
    {
        var state = new GmScriptAuthorityState();
        state.Operators[OperatorId] = new GmScriptGrantState
        {
            Flags = (uint)ScriptPermissionFlags.WorldDirector,
            GrantedAtUtc = DateTime.UtcNow.AddDays(-1),
            ExpiresAtUtc = DateTime.MaxValue,
            GrantedBy = OperatorId,
        };

        var authority = new Mock<IPersistentState<GmScriptAuthorityState>>();
        authority.SetupGet(a => a.State).Returns(() => state);
        authority.SetupGet(a => a.RecordExists).Returns(() => state.Operators.Count > 0);
        authority.Setup(a => a.ReadStateAsync()).Returns(Task.CompletedTask);
        authority.Setup(a => a.WriteStateAsync()).Returns(Task.CompletedTask);

        var positionStore = new Mock<ICharacterPositionStore>();
        positionStore.Setup(p => p.GetPositionAsync(OperatorId))
            .ReturnsAsync((0f, 0f, 0f, 0f));

        var grain = new GmCommandGrain(
            NullLogger<GmCommandGrain>.Instance,
            authority.Object,
            grantContext: null,
            positionStore: positionStore.Object,
            scriptOptions: new ScriptedSpawnOptions { Enabled = true, SigningKeyBase64 = KeyB64 });

        var verdict = await grain.TryConsumeScriptAsync(new GmScriptTicket
        {
            OperatorCharacterId = OperatorId,
            RequestId = 777,
            Op = op,
            Kind = ScriptedKind.Monster,
            TemplateKey = "墨麒麟",
            Count = 2,
            OperatorShardId = ShardId,
        });

        Assert.True(verdict.Allowed, verdict.Message);
        return verdict.Token!;
    }

    private static ulong[] ScriptedIds(DsTickResult result) => result.WorldSnapshot?.Deltas
        .Where(d => d.Kind == EntityDeltaKind.Spawn && (int)(d.EntityId >> 32) < 0)
        .Select(d => d.EntityId)
        .OrderBy(id => id)
        .ToArray() ?? Array.Empty<ulong>();

    [Fact]
    public async Task Grain签发的令牌经真实socket在DS里出了怪并回执真实ID()
    {
        var token = await IssueTokenAsync();
        _gateway.Push(token);

        // 事件在接收线程上触发、入队；消费发生在 tick 之前——与真实宿主同一时序。
        // 注意要**跨 tick 收集事件**：DS 快照默认每 3 tick 一份（SnapshotIntervalTicks=3），
        // 回执在"生成那一 tick"就发出去了，而实体 ID 要等下一份快照才可见。
        // 断言只看最后那个 tick 的话，会把一条正常链路误判成丢了回执。
        var ticks = new List<DsTickResult>();
        ulong[] ids = Array.Empty<ulong>();
        for (var i = 0; i < 12; i++)
        {
            var current = await _runner.RunSingleTickAsync();
            ticks.Add(current);
            ids = ScriptedIds(current);
            if (ids.Length > 0) break;
            await Task.Delay(20);
        }

        Assert.Equal(2, ids.Length);
        Assert.Equal(1, _uplink.ScriptedSpawnReceivedCount);
        Assert.Equal(1, _runner.ScriptedCommandsDrained);

        var receipt = Assert.Single(ticks.SelectMany(t => t.Events).Where(e =>
            e.Packet is EventPacket ep && ep.Events.Any(s => s.Kind == SyncEventKind.ScriptedSpawnResult)));
        Assert.Equal(new[] { OperatorId }, receipt.Audience);

        var payload = MemoryPackSerializer.Deserialize<ScriptedSpawnResultPayload>(
            ((EventPacket)receipt.Packet).Events.Single(s => s.Kind == SyncEventKind.ScriptedSpawnResult).Payload!);
        Assert.Equal(777, payload.RequestId);                 // RequestId 由 Grain 写进被签名的令牌
        Assert.Equal(token.Nonce, payload.Nonce);
        Assert.False(payload.Removed);
        Assert.Equal(ids, payload.EntityIds.OrderBy(id => id).ToArray());
    }

    [Fact]
    public async Task 令牌在途被改一个字节就什么都不会出()
    {
        var token = await IssueTokenAsync();
        token.X += 900f;   // 模拟在途篡改（真签名字段覆盖坐标）

        _gateway.Push(token);
        await _runner.RunSingleTickAsync();
        await Task.Delay(200);
        var tick = await _runner.RunSingleTickAsync();

        Assert.Empty(ScriptedIds(tick));
        Assert.Equal("signature-mismatch", _runner.World.LastScriptRejectReason);
        Assert.True(_runner.World.ScriptedRejectedCount >= 1);
    }

    [Fact]
    public async Task 关闭脚本面的DS即使收到合法令牌也不动世界()
    {
        // 灰度回退态：EnableScriptedSpawn=false 时必须一律 scripted-disabled。
        var port = GetFreePort();
        var gateway = new TestGateway();
        await using var uplink = new DsUplinkClient(new DsUplinkClientOptions
        {
            GatewayHost = "127.0.0.1",
            GatewayPort = gateway.Port,
            DsId = "ds-off",
            ShardIds = new[] { ShardId },
            Host = "127.0.0.1",
            ListenPort = port,
            HeartbeatSeconds = 1,
        });
        await using var runner = new DsHostRunner(new DsHostOptions
        {
            DsId = "ds-off",
            ShardIds = new[] { ShardId },
            ListenPort = port,
            TicksPerSecond = 60,
            EnableScriptedSpawn = false,
            ScriptedSigningKeyBase64 = KeyB64,
        }, sender: uplink, log: null);
        uplink.ScriptedSpawnReceived += runner.NotifyScriptedSpawn;

        Assert.True(await uplink.ConnectAsync());
        Assert.True(await uplink.RegisterAsync());
        Assert.True(runner.World.AddPlayer(1001, 1001, 0f, 0f, 0f, out var reason), reason);

        // 令牌还是本文件那把密钥签的（对 DS 来说完全合法），只是开关关着。
        var token = await IssueTokenAsync();
        gateway.Push(token);
        await Task.Delay(200);
        var tick = await runner.RunSingleTickAsync();

        Assert.Empty(ScriptedIds(tick));
        Assert.Equal("scripted-disabled", runner.World.LastScriptRejectReason);
        gateway.Dispose();
    }
}
