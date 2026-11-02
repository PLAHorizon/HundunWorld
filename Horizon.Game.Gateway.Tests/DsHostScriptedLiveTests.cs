using Horizon.Game.Core.Sim.Server;
using Horizon.Game.Message.Network;
using Horizon.Game.Message.Security;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 真实 DsHost **进程**上的脚本生成通路测试（真 TCP + 真环境变量 + 真分帧读取 + 真 tick 消费）。
/// <para>
/// 其余脚本测试全部在同进程里传对象，因此下面这几条一直没有人证明：
/// <list type="bullet">
/// <item>DS 宿主到底认不认 <c>ScriptedSpawn__Enabled</c> / <c>ScriptedSpawn__SigningKeyBase64</c>
/// 这一对权威环境变量（三进程开启路径的核心）；</item>
/// <item>网关下行帧能否真的到达 <c>DsWorldSession.EnqueueScriptedCommand</c>
/// （靠 <c>uplink.ScriptedSpawnReceived += runner.NotifyScriptedSpawn</c> 这一行接线，
/// 漏接线时全部指令静默无效果）；</item>
/// <item>关闭态是否真的"拒绝一切"（灰度默认）。</item>
/// </list>
/// </para>
/// <para>
/// 观察手段是 DS 自己的 stdout：它启动打 <c>[ScriptedSpawn] DS(验签)…</c>，
/// 每条被拒指令打 <c>脚本指令被拒。Reason=…</c>。被拒的指令能出现在日志里，
/// 就等价于证明"帧→事件→队列→tick→验签"整条链是通的；
/// 而"验签通过之后"的执行语义由 <see cref="ScriptedSpawnEndToEndTests"/> 用同一个
/// <see cref="DsWorldSession"/> 钉住。两边合起来才是完整链路。
/// </para>
/// </summary>
public sealed class DsHostScriptedLiveTests
{
    private const int ShardId = 0;

    private static readonly byte[] Key = Enumerable.Repeat((byte)0x7C, 32).ToArray();
    private static readonly byte[] OtherKey = Enumerable.Repeat((byte)0xD3, 32).ToArray();

    private static string? FindDsHostExe()
    {
        var configured = Environment.GetEnvironmentVariable("HORIZON_DSHOST_EXE");
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(configured)) candidates.Add(configured!);

        var repoRoot = FindRepoRoot();
        // 旁路输出目录（Verify/BuildCheck）是本仓库编译验证用的：运行中的 DS 会锁住 bin/Debug 里的
        // exe，不能为了跑测试去动那个进程。-p:OutputPath 会替换整段路径（含 net10.0），
        // 因此两种布局都要试。
        foreach (var candidate in new[]
                 {
                     Path.Combine("bin", "Verify", "Horizon.DsHost.exe"),
                     Path.Combine("bin", "Verify", "net10.0", "Horizon.DsHost.exe"),
                     Path.Combine("bin", "BuildCheck", "Horizon.DsHost.exe"),
                     Path.Combine("bin", "BuildCheck", "net10.0", "Horizon.DsHost.exe"),
                     Path.Combine("bin", "Debug", "net10.0", "Horizon.DsHost.exe"),
                 })
        {
            candidates.Add(Path.Combine(repoRoot, "Horizon.DsHost", candidate));
        }

        foreach (var path in candidates)
        {
            if (File.Exists(path)) return path;
        }

        return null;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Horizon.DsHost", "Horizon.DsHost.csproj")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? Directory.GetCurrentDirectory();
    }

    /// <summary>只监听控制面的假网关：能应答注册/心跳，并保留连接以便真的往 DS 推一帧脚本指令。</summary>
    private sealed class ScriptedFakeGateway : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private readonly ConcurrentQueue<DsRegisterRequest> _registrations = new();
        private volatile NetworkStream? _dsStream;

        public ScriptedFakeGateway()
        {
            var port = GetFreePort();
            Port = port;
            _listener = new TcpListener(IPAddress.Loopback, port);
            _listener.Start();
            _ = Task.Run(() => AcceptLoopAsync(_cts.Token));
        }

        public int Port { get; }

        public void SendScriptedToken(ScriptGrantToken token)
        {
            var stream = _dsStream ?? throw new InvalidOperationException("DS 尚未连上网关");
            var frame = DsUplinkCodec.EncodeFrame(DsUplinkMessageKind.ScriptedSpawn,
                new DsScriptedSpawnEnvelope { Token = token });
            stream.Write(frame);
            stream.Flush();
        }

        public async Task WaitForRegistrationAsync(TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (!_registrations.IsEmpty) return;
                await Task.Delay(100);
            }

            throw new TimeoutException("DS 未向测试网关注册（控制面没通，后面的断言无意义）");
        }

        private async Task AcceptLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                TcpClient client;
                try { client = await _listener.AcceptTcpClientAsync(ct); }
                catch { return; }

                _ = Task.Run(() => ServeAsync(client, ct), CancellationToken.None);
            }
        }

        private async Task ServeAsync(TcpClient client, CancellationToken ct)
        {
            var reader = new DsFrameReader();
            var buffer = new byte[16384];
            try
            {
                client.NoDelay = true;
                using var stream = client.GetStream();
                _dsStream = stream;

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
                            _registrations.Enqueue(req);
                        }

                        if (kind is DsUplinkMessageKind.Register or DsUplinkMessageKind.Heartbeat or DsUplinkMessageKind.Unregister)
                        {
                            var ack = DsUplinkCodec.EncodeFrame(DsUplinkMessageKind.ControlAck,
                                new DsControlAck { Ok = true, Reason = string.Empty });
                            await stream.WriteAsync(ack, ct);
                            await stream.FlushAsync(ct);
                        }

                        reader.ConsumeFrame();
                    }
                }
            }
            catch { /* Stop/Dispose 打断 */ }
            finally
            {
                _dsStream = null;
                try { client.Dispose(); } catch { /* 忽略 */ }
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

    private sealed class LiveDs : IAsyncDisposable
    {
        public required Process Process;
        public readonly ConcurrentQueue<string> Lines = new();
        public required ScriptedFakeGateway Gateway;

        public async Task<string> WaitForLineAsync(string pattern, TimeSpan timeout)
        {
            var regex = new Regex(pattern, RegexOptions.IgnoreCase);
            var deadline = DateTime.UtcNow + timeout;
            var seen = new List<string>();
            while (DateTime.UtcNow < deadline)
            {
                foreach (var line in Lines.ToArray())
                {
                    seen.Add(line);
                    if (regex.IsMatch(line)) return line;
                }
                await Task.Delay(200);
            }

            throw new TimeoutException(
                $"未在 DS 日志里看到 /{pattern}/（超时 {timeout.TotalSeconds:F0}s）。已捕获行：\n" +
                string.Join("\n", seen.TakeLast(25)));
        }

        public int CountMatching(string pattern)
            => Lines.Count(l => Regex.IsMatch(l, pattern, RegexOptions.IgnoreCase));

        public async ValueTask DisposeAsync()
        {
            try { if (!Process.HasExited) Process.Kill(entireProcessTree: true); } catch { /* 忽略 */ }
            Process.Dispose();
            Gateway.Dispose();
            await Task.CompletedTask;
        }
    }

    private static async Task<LiveDs> StartDsAsync(
        IReadOnlyDictionary<string, string?> env,
        int tickRate = 60,
        int spawnPoints = 0)
    {
        var exe = FindDsHostExe()
            ?? throw new InvalidOperationException(
                "找不到 Horizon.DsHost.exe：先跑 dotnet build Horizon.DsHost/Horizon.DsHost.csproj -p:OutputPath=bin/Verify/");

        var listenPort = GetFreePort();
        var gateway = new ScriptedFakeGateway();
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            WorkingDirectory = Path.GetDirectoryName(exe)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            // DS 把中文日志写成 UTF-8；.NET 读重定向子进程stdout默认跟随控制台代码页（本机是 GBK 936），
            // 于是"DS(验签)"变成"DS(楠岀)"、断言必然匹配不上。显式按 UTF-8 解码。
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            ArgumentList =
            {
                $"--ds-id=ds-scripted-live-{listenPort}",
                $"--shard-ids={ShardId}",
                $"--listen-port={listenPort}",
                "--advertise-host=127.0.0.1",
                "--gateway-host=127.0.0.1",
                $"--gateway-port={gateway.Port}",
                $"--spawn-points={spawnPoints}",
                $"--tick-rate={tickRate}",
                "--heartbeat-seconds=2",
                // 命令行是第 3 层（覆盖 appsettings 里的 Orleans:ConnectionString），空值 = 不连集群。
                // 不带这条，测试 DS 会真的加入开发中的 Orleans 集群（实测日志"已连接 Orleans 集群
                // ClusterId=dev"）——测试不该碰用户的运行环境，而且它只需要内置掉落表。
                "--orleans-silo=",
            },
        };

        // 权威变量必须**显式移除**再按需赋值：测试进程可能从 shell 继承了 ScriptedSpawn__*，
        // 那样"关闭态"用例就会被环境带着跑成开启态。
        foreach (var name in new[]
                 {
                     "ScriptedSpawn__Enabled", "ScriptedSpawn__SigningKeyBase64",
                     "HZN_SCRIPTED_SPAWN", "HZN_SCRIPT_SIGNING_KEY_B64",
                 })
        {
            psi.Environment.Remove(name);
        }
        foreach (var pair in env)
        {
            if (pair.Value is null) continue;
            psi.Environment[pair.Key] = pair.Value;
        }

        var process = Process.Start(psi)!;
        var live = new LiveDs { Process = process, Gateway = gateway };
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) live.Lines.Enqueue(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) live.Lines.Enqueue(e.Data); };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await live.Gateway.WaitForRegistrationAsync(TimeSpan.FromSeconds(60));
        return live;
    }

    private static ScriptGrantToken SignedToken(byte[] key, long nonce)
    {
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var token = new ScriptGrantToken
        {
            Nonce = nonce,
            OperatorCharacterId = 7001,
            Op = ScriptOp.Spawn,
            Kind = ScriptedKind.Monster,
            TemplateId = 1,
            TargetShardId = ShardId,
            X = 2f,
            Y = 2f,
            Z = 0f,
            Count = 2,
            RequestId = nonce,
        };
        ScriptGrantTokenSigner.Sign(token, key, nowMs, 30);
        return token;
    }

    [Fact]
    public async Task Live_DS权威环境变量开启脚本面并拒绝错密钥令牌()
    {
        await using var ds = await StartDsAsync(new Dictionary<string, string?>
        {
            ["ScriptedSpawn__Enabled"] = "1",
            ["ScriptedSpawn__SigningKeyBase64"] = Convert.ToBase64String(Key),
        });

        // 1) 启动自证：开关与密钥指纹由环境变量决定（这正是三进程开启路径最容易抄漏的一处）。
        var expectedFingerprint = ScriptGrantTokenSigner.KeyFingerprint(Convert.ToBase64String(Key));
        var startup = await ds.WaitForLineAsync(@"\[ScriptedSpawn\] DS\(验签\)", TimeSpan.FromSeconds(20));
        Assert.Contains("Enabled=True", startup, StringComparison.Ordinal);
        Assert.Contains(expectedFingerprint, startup, StringComparison.Ordinal);

        // 2) 用另一把密钥签发的合法格式令牌 → 真实进程里必须报 signature-mismatch。
        //    这行日志出现，就意味着 帧→事件→队列→tick→验签 整条接线在真进程里是通的。
        ds.Gateway.SendScriptedToken(SignedToken(OtherKey, 9001));
        var rejected = await ds.WaitForLineAsync("Reason=signature-mismatch", TimeSpan.FromSeconds(20));
        Assert.Contains("脚本指令被拒", rejected, StringComparison.Ordinal);

        // 3) 正确密钥签发的令牌**不应**出现在拒绝日志里。
        var rejectsBefore = ds.CountMatching("Reason=");
        ds.Gateway.SendScriptedToken(SignedToken(Key, 9002));
        await Task.Delay(TimeSpan.FromSeconds(3));   // 60Hz：3s 远超"入队 + 下一 tick 消费"
        Assert.Equal(rejectsBefore, ds.CountMatching("Reason="));
    }

    [Fact]
    public async Task Live_未开启开关时DS拒绝一切脚本指令()
    {
        // 灰度默认态：不开开关，即便密钥正确也必须一律拒绝（scripted-disabled）。
        await using var ds = await StartDsAsync(new Dictionary<string, string?>
        {
            ["ScriptedSpawn__SigningKeyBase64"] = Convert.ToBase64String(Key),
        });

        var startup = await ds.WaitForLineAsync(@"\[ScriptedSpawn\] DS\(验签\)", TimeSpan.FromSeconds(20));
        Assert.Contains("Enabled=False", startup, StringComparison.Ordinal);

        ds.Gateway.SendScriptedToken(SignedToken(Key, 9101));
        var rejected = await ds.WaitForLineAsync("Reason=scripted-disabled", TimeSpan.FromSeconds(20));
        Assert.Contains("Operator=7001", rejected, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Live_旧环境变量名仍然生效()
    {
        // 既有启动脚本用的是 HZN_* 这一对；回退链断掉的话，那些机器上的 DS 会静默关闭脚本面。
        await using var ds = await StartDsAsync(new Dictionary<string, string?>
        {
            ["HZN_SCRIPTED_SPAWN"] = "1",
            ["HZN_SCRIPT_SIGNING_KEY_B64"] = Convert.ToBase64String(Key),
        });

        var startup = await ds.WaitForLineAsync(@"\[ScriptedSpawn\] DS\(验签\)", TimeSpan.FromSeconds(20));
        Assert.Contains("Enabled=True", startup, StringComparison.Ordinal);
        Assert.Contains(ScriptGrantTokenSigner.KeyFingerprint(Convert.ToBase64String(Key)), startup, StringComparison.Ordinal);

        ds.Gateway.SendScriptedToken(SignedToken(OtherKey, 9201));
        await ds.WaitForLineAsync("Reason=signature-mismatch", TimeSpan.FromSeconds(20));
    }

    [Fact]
    public async Task Live_密钥缺失时DS照常启动但拒绝一切()
    {
        // 缺密钥是"配置笔误"而非"跳过校验"的许可：DS 必须活着、并且 fail-closed。
        await using var ds = await StartDsAsync(new Dictionary<string, string?>
        {
            ["ScriptedSpawn__Enabled"] = "1",
        });

        var startup = await ds.WaitForLineAsync(@"\[ScriptedSpawn\] DS\(验签\)", TimeSpan.FromSeconds(20));
        Assert.Contains("Enabled=True", startup, StringComparison.Ordinal);
        Assert.Contains("(missing)", startup, StringComparison.Ordinal);

        ds.Gateway.SendScriptedToken(SignedToken(Key, 9301));
        await ds.WaitForLineAsync("Reason=missing-signing-key", TimeSpan.FromSeconds(20));
        Assert.False(ds.Process.HasExited, "缺密钥不应让 DS 进程退出");
    }
}
