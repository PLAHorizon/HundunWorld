using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Horizon.Game.Message.Network;
using Horizon.Game.Message.Sync;
using Horizon.Game.Sim;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// **完整闭环**端到端验证（环境门控）：客户端 → 网关 → **DS** → 网关 → 客户端。
/// <para>
/// <b>如何确证是 DS 在服务、而不是回退到 Orleans</b>：本用例断言客户端收到的快照里
/// 含有**怪物实体 ID**（<c>(SpawnPointId &lt;&lt; 32) | (slot+1)</c>）。
/// Orleans 侧的 <c>ZoneShardGrain</c> 根本没有刷怪系统（缺口 G6：世界里没有怪），
/// 因此只有运行中的 DsHost 才可能下发这些实体——
/// 这是一个无法被"回退路径"伪造的判别式。
/// </para>
/// <para>环境变量：<c>HORIZON_GATEWAY_TCP</c>（形如 <c>192.168.1.78:7789</c>）。</para>
/// </summary>
public class GatewayDsFullLoopLiveTests
{
    private static string? Endpoint => Environment.GetEnvironmentVariable("HORIZON_GATEWAY_TCP");
    private static bool LiveEnabled => !string.IsNullOrWhiteSpace(Endpoint) && Endpoint!.Contains(':');

    /// <summary>本用例使用的角色 ID（与 DsWorldSession 的 sessionId == characterId 约定一致）。</summary>
    private const ulong CharacterId = 777001;

    /// <summary>DsHost 默认刷怪点 1 的 0 号槽位实体 ID（<c>(1 &lt;&lt; 32) | 1</c>）。</summary>
    private const ulong MonsterEntityIdSpawn1Slot0 = (1UL << 32) | 1UL;

    [Fact]
    public async Task Live_FullLoop_ClientReceivesMonsterEntitiesFromDs()
    {
        if (!LiveEnabled) return;

        var parts = Endpoint!.Split(':');
        using var tcp = new TcpClient { NoDelay = true };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        await tcp.ConnectAsync(parts[0], int.Parse(parts[1]), cts.Token);
        using var stream = tcp.GetStream();

        // --- 1) 握手 ---
        var handshake = new HandshakePacket
        {
            ProtocolVersion = SyncProtocolVersion.Current,
            FormulaVersion = MovementFormula.FormulaVersion,
            LocalCharacterId = CharacterId,
            InitialClientTick = 1,
            InitialX = -5f,
            InitialY = 0f,
            InitialZ = 0f,
            ShardId = 0,
        };
        await SendAsync(stream, handshake, cts.Token);

        var handshakeReply = await ReadSyncPacketAsync(stream, cts.Token);
        Assert.True(handshakeReply is HandshakePacket,
            $"握手应成功，实际收到 {handshakeReply?.Kind.ToString() ?? "无响应"}");

        // --- 2) 持续上行输入，并收集下行快照 ---
        var seenEntityIds = new HashSet<ulong>();
        var snapshotCount = 0;
        var clientTick = 1L;
        var predictedX = -5f;

        // 先给 DS 一点时间把玩家注册进世界（握手经网关转发到 DS 是异步的）
        for (var round = 0; round < 60; round++)
        {
            clientTick++;
            predictedX += MovementFormula.DefaultMaxSpeed / 60f;
            var input = new InputPacket
            {
                CharacterId = CharacterId,
                ClientTick = clientTick,
                MoveX = 0f,          // 原地：避免位移让怪物跑远
                MoveY = 0f,
                PredictedEndX = predictedX,
                PredictedEndY = 0f,
                PredictedEndZ = 0f,
                MaxSpeed = MovementFormula.DefaultMaxSpeed,
            };

            // Input 语义为 fire-and-forget（不期待同步响应）
            await SendAsync(stream, input, cts.Token, requireResponse: false);

            // 非阻塞地尽量多读几帧
            while (stream.DataAvailable)
            {
                var pkt = await ReadSyncPacketAsync(stream, cts.Token);
                if (pkt is SnapshotPacket snap)
                {
                    snapshotCount++;
                    foreach (var d in snap.Deltas) seenEntityIds.Add(d.EntityId);
                }
            }

            if (snapshotCount > 0 && seenEntityIds.Contains(MonsterEntityIdSpawn1Slot0)) break;
            await Task.Delay(200, cts.Token);
        }

        Assert.True(snapshotCount > 0, "客户端应至少收到一个快照（否则 DS 通道或 Orleans 回退都没生效）");

        // --- 3) 判别式：快照里必须出现**只有 DS 才有的**怪物实体 ---
        Assert.True(seenEntityIds.Contains(CharacterId),
            $"快照应包含本玩家实体 {CharacterId}；实际实体：{string.Join(',', seenEntityIds)}");
        Assert.True(seenEntityIds.Contains(MonsterEntityIdSpawn1Slot0),
            "快照中未出现怪物实体 —— 说明流量**回退到了 Orleans**（ZoneShardGrain 无刷怪系统），" +
            $"DS 通道未真正承载本会话。实际实体：{string.Join(',', seenEntityIds)}");
    }

    // ---------------------------------------------------------------- 线协议辅助

    private static async Task SendAsync(NetworkStream stream, SyncPacket packet, CancellationToken ct, bool requireResponse = true)
    {
        var wire = SyncHandshakeWire.Pack(packet, authToken: null, machineId: null, requireResponse: requireResponse);
        await stream.WriteAsync(wire, ct);
        await stream.FlushAsync(ct);
    }

    /// <summary>读取一个完整线路帧（8B 头 = 4B 载荷长度 LE + 1B 类型 + 1B 压缩 + 2B 校验）。</summary>
    private static async Task<SyncPacket?> ReadSyncPacketAsync(NetworkStream stream, CancellationToken ct)
    {
        var header = await ReadExactAsync(stream, SyncHandshakeWire.WireHeaderLength, ct);
        if (header is null) return null;

        var payloadLen = BitConverter.ToInt32(header, 0);
        if (payloadLen <= 0 || payloadLen > 4 * 1024 * 1024) return null;

        var payload = await ReadExactAsync(stream, payloadLen, ct);
        if (payload is null) return null;

        var frame = new byte[SyncHandshakeWire.WireHeaderLength + payloadLen];
        Buffer.BlockCopy(header, 0, frame, 0, header.Length);
        Buffer.BlockCopy(payload, 0, frame, header.Length, payload.Length);

        try
        {
            return SyncHandshakeWire.Unpack(frame);
        }
        catch (Exception)
        {
            return null; // 非同步帧（例如鉴权错误消息）——忽略
        }
    }

    private static async Task<byte[]?> ReadExactAsync(NetworkStream stream, int count, CancellationToken ct)
    {
        var buf = new byte[count];
        var read = 0;
        while (read < count)
        {
            int n;
            try
            {
                n = await stream.ReadAsync(buf.AsMemory(read, count - read), ct);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            if (n <= 0) return null;
            read += n;
        }
        return buf;
    }
}
