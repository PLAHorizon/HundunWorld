using System;
using System.Buffers;
using System.Threading;
using System.Threading.Tasks;
using Horizon.Game.Core.Sim.Server;
using Horizon.Game.Message.Sync;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// **真网关**端到端验证：DS 上行通道（控制面 + 下行回传）跨进程实测。
/// <para>
/// <b>环境门控</b>：未设置 <c>HORIZON_DS_GATEWAY_UPLINK</c>（形如 <c>127.0.0.1:19871</c>）时直接返回，
/// 不污染 CI；设置后即可对**真实运行的网关**跑。
/// </para>
/// <para>
/// <b>验证的是本轮新增的那条链路</b>：DS 进程 → 网关 <c>DsUplinkServer</c> → <c>DsControlPlane</c>
/// → <c>DSRegistry</c>。在此之前 <c>DSRegistry</c> 从未被任何真实 DS 进程填充过（缺口 G3）——
/// 本用例首次把"设计出来却从未接线"的注册链路跑通。
/// </para>
/// <para>
/// 运行方式：
/// <code>
/// $env:HORIZON_DS_GATEWAY_UPLINK = '127.0.0.1:19871'
/// dotnet test --filter "FullyQualifiedName~DsGatewayLiveE2ETests"
/// </code>
/// （网关上需以 <c>Gateway__UseDS=true</c> + <c>Gateway__DsUplinkPort=19871</c> 启动。）
/// </para>
/// </summary>
public class DsGatewayLiveE2ETests
{
    private static string? Endpoint => Environment.GetEnvironmentVariable("HORIZON_DS_GATEWAY_UPLINK");

    private static bool LiveEnabled => !string.IsNullOrWhiteSpace(Endpoint) && Endpoint!.Contains(':');

    private static DsUplinkClientOptions BuildOptions(out int listenPort)
    {
        var parts = Endpoint!.Split(':');
        // 每个测试用独立端口，避免与并行/已在运行的 DS 抢端口（本测试不起监听，仅需注册值唯一）。
        listenPort = 19300 + (int)(DateTime.UtcNow.Ticks % 500);
        return new DsUplinkClientOptions
        {
            DsId = "ds-e2e-test-" + listenPort,
            ShardIds = new long[] { 0 },
            Host = "127.0.0.1",
            ListenPort = listenPort,
            Region = string.Empty,
            GatewayHost = parts[0],
            GatewayPort = int.Parse(parts[1]),
            Token = Environment.GetEnvironmentVariable("HORIZON_DS_CONTROL_TOKEN") ?? string.Empty,
            AckTimeoutMs = 8000,
        };
    }

    [Fact]
    public async Task Live_RegisterHeartbeatSnapshotAndUnregister_AgainstRealGateway()
    {
        if (!LiveEnabled) return;

        var options = BuildOptions(out var listenPort);
        await using var client = new DsUplinkClient(options);

        // 1) 连接真实网关的 DS 上行端口
        Assert.True(await client.ConnectAsync(), "应能连接到网关的 DS 上行端口");
        Assert.True(client.IsConnected);

        // 2) 注册 —— 这是 DSRegistry 首次被真实进程填充的路径
        Assert.True(await client.RegisterAsync(), $"注册应被受理（LastFailureReason={client.LastFailureReason}）");

        // 3) 心跳 —— 续约；失败会让注册表在 90s 后被摘除
        Assert.True(await client.HeartbeatAsync(), "心跳应被受理");

        // 4) 快照批次 —— 下行回传（单向，无应答；发送成功即表示连接可用）
        var batch = new DsSnapshotBatch
        {
            Items = new[]
            {
                new DsSnapshotItem
                {
                    SessionIds = new long[] { 987654321 }, // 不存在的会话，网关只做映射不会失败
                    Frame = EncodeSnapshot(1),
                },
            },
        };
        Assert.True(await client.SendSnapshotBatchAsync(batch), "快照批次应发送成功");
        Assert.Equal(1, client.SentBatchCount);
        Assert.Equal(0, client.DroppedItemCount);

        // 5) 优雅摘除 —— 灰度回滚/下线路径
        Assert.True(await client.UnregisterAsync(), "摘除应被受理");
    }

    [Fact]
    public async Task Live_ConcurrentDsRegistrations_AllAccepted()
    {
        if (!LiveEnabled) return;

        // 多个 DS 同时注册（多 shard 部署形态）：网关注册表应能同时接纳。
        var clients = new System.Collections.Generic.List<DsUplinkClient>();
        try
        {
            for (var i = 0; i < 3; i++)
            {
                var opts = BuildOptions(out var port);
                opts.DsId = $"ds-e2e-multi-{i}-{port}";
                var c = new DsUplinkClient(opts);
                Assert.True(await c.ConnectAsync());
                Assert.True(await c.RegisterAsync(), $"第 {i} 个 DS 注册应成功");
                clients.Add(c);
            }

            foreach (var c in clients)
            {
                Assert.True(await c.HeartbeatAsync());
            }
        }
        finally
        {
            foreach (var c in clients)
            {
                try { await c.UnregisterAsync(); } catch { /* 清理期忽略 */ }
                await c.DisposeAsync();
            }
        }
    }

    [Fact]
    public async Task Live_DeadPort_ReconnectBackoffDoesNotThrow()
    {
        if (!LiveEnabled) return;

        // 指向必然无人监听的端口：客户端必须"失败返回"而不是抛异常或永久阻塞
        // （DS 侧靠这个语义决定是否进入指数退避重连）。
        var options = new DsUplinkClientOptions
        {
            GatewayHost = "127.0.0.1",
            GatewayPort = 1,
            DsId = "ds-dead-port",
            AckTimeoutMs = 500,
        };
        await using var client = new DsUplinkClient(options);
        Assert.False(await client.ConnectAsync());
        Assert.False(client.IsConnected);
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
}
