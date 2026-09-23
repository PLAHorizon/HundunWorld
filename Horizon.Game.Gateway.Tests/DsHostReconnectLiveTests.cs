using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Horizon.Game.Core.Sim.Server;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// MMORPG 开发计划 M5.3 —— DS 上行通道的**断线/重连韧性**（进程级混沌验证）。
/// <para>
/// <b>为什么要起真进程</b>：重连与指数退避逻辑位于 <c>Horizon.DsHost/Program.RunControlPlaneAsync</c>，
/// 是宿主进程的编排代码，无法用进程内单测覆盖——而它恰恰是"网关重启/网络抖动后 DS 能否自己回来"
/// 的唯一保障。因此本用例启动真实的 <c>Horizon.DsHost.exe</c>，配一个**一次性假网关**，
/// 主动把网关打掉再拉起来，观察 DS 是否自动重连并重新注册。
/// </para>
/// <para>
/// <b>为什么用假网关而不是真网关</b>：本用例要**故意制造网关不可用**，
/// 不能拿用户/CI 环境里正在服务的真网关做故障注入。假网关只实现上行协议
/// （<see cref="DsFrameReader"/> + <see cref="DsUplinkCodec"/>），与被测的 DS 侧通过**线上字节**对接。
/// </para>
/// <para>环境门控：未设置 <c>HORIZON_DSHOST_EXE</c>（DsHost 可执行文件路径）时直接返回，不污染 CI。</para>
/// </summary>
public class DsHostReconnectLiveTests
{
    private static string? DsHostExe => Environment.GetEnvironmentVariable("HORIZON_DSHOST_EXE");
    private static bool LiveEnabled => !string.IsNullOrWhiteSpace(DsHostExe) && File.Exists(DsHostExe!);

    [Fact]
    public async Task Live_DsHost_ReconnectsAndReRegisters_AfterGatewayRestart()
    {
        if (!LiveEnabled) return;

        // 假网关占一个临时端口；DS 先连上它注册，然后我们把它打掉再拉起来。
        var gatewayPort = GetFreePort();
        var dsListenPort = GetFreePort();

        using var gateway = new FlakyFakeGateway(gatewayPort);
        gateway.Start();

        var psi = BuildStartInfo(DsHostExe!, gatewayPort, dsListenPort);
        using var proc = Process.Start(psi)!;
        try
        {
            // --- 阶段 1：初次注册 ---
            var first = await gateway.WaitForRegistrationAsync(TimeSpan.FromSeconds(30));
            Assert.NotNull(first);
            Assert.Equal(dsListenPort, first!.Port);
            Assert.Contains(0L, first.ShardIds);

            // --- 阶段 2：网关故障（模拟网关重启/网络中断）---
            gateway.Stop();
            await Task.Delay(TimeSpan.FromSeconds(3));
            Assert.False(proc.HasExited, "网关故障不应导致 DS 进程退出（必须进入重连而非崩溃）");

            // --- 阶段 3：网关恢复，DS 应自动重连并**重新注册** ---
            // 注意：必须等待注册次数"增长"，而不是"存在注册"——后者会被上一轮的旧记录立刻满足，
            // 从而让断言在 DS 根本没重连的情况下也通过（本用例第一版就踩了这个坑）。
            gateway.Start();
            var second = await gateway.WaitForNewRegistrationAsync(2, TimeSpan.FromSeconds(90));

            Assert.NotNull(second);
            Assert.Equal(first.DsId, second!.DsId);
            Assert.True(gateway.RegistrationCount >= 2,
                $"DS 应在网关恢复后重新注册（实际注册次数 {gateway.RegistrationCount}）");
        }
        finally
        {
            try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { /* 清理期忽略 */ }
            gateway.Stop();
        }
    }

    [Fact]
    public async Task Live_DsHost_KeepsRunning_WhenGatewayNeverAvailable()
    {
        if (!LiveEnabled) return;

        // 指向一个没人监听的端口：DS 必须"活着并重试"，而不是启动失败退出。
        var deadPort = GetFreePort();
        var dsListenPort = GetFreePort();

        var psi = BuildStartInfo(DsHostExe!, deadPort, dsListenPort);
        using var proc = Process.Start(psi)!;
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(12));
            Assert.False(proc.HasExited,
                "网关不可达时 DS 必须保持运行并指数退避重试（这是灰度期可回切的前提）");
        }
        finally
        {
            try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { /* 忽略 */ }
        }
    }

    // ---------------------------------------------------------------- 辅助

    private static int GetFreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    private static ProcessStartInfo BuildStartInfo(string exe, int gatewayPort, int dsListenPort)
        => new ProcessStartInfo
        {
            FileName = exe,
            WorkingDirectory = Path.GetDirectoryName(exe)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            ArgumentList =
            {
                $"--ds-id=ds-chaos-{dsListenPort}",
                "--shard-ids=0",
                $"--listen-port={dsListenPort}",
                "--advertise-host=127.0.0.1",
                $"--gateway-host=127.0.0.1",
                $"--gateway-port={gatewayPort}",
                "--spawn-points=0",
                "--tick-rate=60",
                // 缩短心跳间隔，让"网关故障 → 发现 → 重连"在测试时限内可观测（默认 30s 太慢）。
                "--heartbeat-seconds=3",
            },
        };

    /// <summary>
    /// 可反复停/启的假网关：只实现 DS 上行协议（分帧 + 控制面应答），
    /// 足以让真实 DsHost 完成"连接 → 注册 → 心跳"，并容忍被反复打掉。
    /// </summary>
    private sealed class FlakyFakeGateway : IDisposable
    {
        private readonly int _port;
        private readonly object _lock = new();
        private TcpListener? _listener;
        private CancellationTokenSource? _cts;
        private Task? _accept;
        private readonly List<DsRegisterRequest> _registrations = new();

        public FlakyFakeGateway(int port) => _port = port;

        public int RegistrationCount { get { lock (_lock) return _registrations.Count; } }

        public void Start()
        {
            lock (_lock)
            {
                if (_listener is not null) return;
                _listener = new TcpListener(IPAddress.Loopback, _port);
                _listener.Start();
                _cts = new CancellationTokenSource();
                _accept = Task.Run(() => AcceptLoopAsync(_listener, _cts.Token));
            }
        }

        public void Stop()
        {
            TcpListener? l;
            CancellationTokenSource? cts;
            Task? acc;
            lock (_lock)
            {
                l = _listener; cts = _cts; acc = _accept;
                _listener = null; _cts = null; _accept = null;
            }
            try { cts?.Cancel(); } catch { /* 忽略 */ }
            try { l?.Stop(); } catch { /* 忽略 */ }
            try { acc?.Wait(TimeSpan.FromSeconds(2)); } catch { /* 忽略 */ }
            cts?.Dispose();
        }

        private async Task AcceptLoopAsync(TcpListener listener, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                TcpClient client;
                try { client = await listener.AcceptTcpClientAsync(ct); }
                catch { return; }
                _ = Task.Run(() => ServeAsync(client, ct), CancellationToken.None);
            }
        }

        private async Task ServeAsync(TcpClient client, CancellationToken ct)
        {
            var reader = new DsFrameReader();
            var buffer = ArrayPool<byte>.Shared.Rent(16384);
            try
            {
                client.NoDelay = true;
                using var stream = client.GetStream();
                while (!ct.IsCancellationRequested)
                {
                    var n = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct);
                    if (n <= 0) break;
                    reader.Append(buffer.AsSpan(0, n));

                    while (reader.TryReadFrame(out var payload))
                    {
                        var kind = payload.Length > 0 ? (DsUplinkMessageKind)payload[0] : DsUplinkMessageKind.Unknown;
                        if (kind == DsUplinkMessageKind.Register &&
                            DsUplinkCodec.TryDecode<DsRegisterRequest>(payload, out _, out var req) && req is not null)
                        {
                            lock (_lock) _registrations.Add(req);
                        }

                        if (kind is DsUplinkMessageKind.Register or DsUplinkMessageKind.Heartbeat or DsUplinkMessageKind.Unregister)
                        {
                            var frame = DsUplinkCodec.EncodeFrame(
                                DsUplinkMessageKind.ControlAck,
                                new DsControlAck { Ok = true, Reason = string.Empty });
                            await stream.WriteAsync(frame, ct);
                            await stream.FlushAsync(ct);
                        }

                        reader.ConsumeFrame();
                    }
                }
            }
            catch { /* 被 Stop 打断 */ }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
                try { client.Dispose(); } catch { /* 忽略 */ }
            }
        }

        public async Task<DsRegisterRequest?> WaitForRegistrationAsync(TimeSpan timeout)
            => await WaitForNewRegistrationAsync(1, timeout);

        /// <summary>等待注册次数达到 <paramref name="atLeast"/>（**必须比"非零"更强**，否则旧记录会让断言假绿）。</summary>
        public async Task<DsRegisterRequest?> WaitForNewRegistrationAsync(int atLeast, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                lock (_lock)
                {
                    if (_registrations.Count >= atLeast) return _registrations[^1];
                }
                await Task.Delay(200);
            }
            return null;
        }

        public void Dispose() => Stop();
    }
}
