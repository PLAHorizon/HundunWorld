using System;
using System.Net.Sockets;
using System.Threading.Tasks;
using Horizon.Game.Message.Network;
using Horizon.Game.Message.Sync;
using Horizon.Game.Sim;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// **真网关**客户端握手连通性探针（环境门控）。
/// <para>
/// 目的：确认"客户端 → 网关"这一段在真实进程上是否可被自动化驱动。
/// 若可（即握手不需要预先登录换取的 ImAuthToken），就能把完整的
/// client → gateway → DS → client 回环做成可回归用例；若不可，则说明
/// 完整回环必须先经过 WebApi 登录链路取令牌——这是**部署事实**，值得写进文档而不是靠猜。
/// </para>
/// <para>环境变量：<c>HORIZON_GATEWAY_TCP</c>（形如 <c>192.168.1.78:7789</c>）。</para>
/// </summary>
public class GatewayLiveHandshakeProbeTests
{
    private static string? Endpoint => Environment.GetEnvironmentVariable("HORIZON_GATEWAY_TCP");
    private static bool LiveEnabled => !string.IsNullOrWhiteSpace(Endpoint) && Endpoint!.Contains(':');

    [Fact]
    public async Task Live_RawHandshake_AgainstRealGateway_YieldsParseableResponse()
    {
        if (!LiveEnabled) return;

        var parts = Endpoint!.Split(':');
        using var tcp = new TcpClient { NoDelay = true };

        using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(15));
        await tcp.ConnectAsync(parts[0], int.Parse(parts[1]), cts.Token);

        using var stream = tcp.GetStream();

        // 用与 UE 客户端同源的握手打包实现（SyncHandshakeWire 单点定义，无拷贝漂移）
        var handshake = new HandshakePacket
        {
            ProtocolVersion = SyncProtocolVersion.Current,
            FormulaVersion = MovementFormula.FormulaVersion,
            LocalCharacterId = 424242,
            InitialClientTick = 1,
            InitialX = 0f,
            InitialY = 0f,
            InitialZ = 0f,
            ShardId = 0,
        };

        var wire = SyncHandshakeWire.Pack(handshake, authToken: null, machineId: null, requireResponse: true);
        await stream.WriteAsync(wire, cts.Token);
        await stream.FlushAsync(cts.Token);

        // 读回一格（网关可能回 HandshakePacket 或 HandshakeRejectPacket；也可能直接断开）
        var buffer = new byte[64 * 1024];
        var received = new System.Collections.Generic.List<byte>();
        try
        {
            while (received.Count < 8)
            {
                var n = await stream.ReadAsync(buffer, cts.Token);
                if (n <= 0) break;
                received.AddRange(new ArraySegment<byte>(buffer, 0, n));
                if (received.Count >= 8) break;
            }
        }
        catch (OperationCanceledException)
        {
            // 超时：网关既不回包也不断开 —— 记录下来，断言阶段给出清晰结论
        }

        Assert.True(received.Count > 0,
            "真网关对未认证握手**没有任何响应**（既不回包也不断开）——" +
            "说明客户端→网关这一段需要先经 WebApi 登录换取 ImAuthToken 才能自动化驱动；" +
            "完整 client→gateway→DS→client 回环必须带认证链路，不能裸连。");

        // 若能解析出握手响应，说明网关接受了这条连接（未认证即可握手）
        SyncPacket? reply = null;
        try
        {
            reply = SyncHandshakeWire.Unpack(received.ToArray());
        }
        catch (Exception)
        {
            // 响应不是握手帧（可能是鉴权错误消息）——同样是有价值的结论
        }

        Assert.True(reply is HandshakePacket or HandshakeRejectPacket,
            $"收到 {received.Count} 字节，但非可解析的握手响应（实际类型 {reply?.Kind.ToString() ?? "无法解析"}）——" +
            "说明网关在该连接上先走了鉴权/协议前置校验。");
    }
}
