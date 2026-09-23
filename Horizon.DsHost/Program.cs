using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Horizon.Game.Core.Sim.Server;
using Horizon.Game.Sim;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Configuration;
using Orleans.Hosting;
using StackExchange.Redis;

namespace Horizon.DsHost;

/// <summary>
/// Horizon.DsHost —— 无头 Dedicated Server 宿主进程入口（MMORPG 开发计划 P3）。
/// <para>
/// 用法：
/// <code>
/// Horizon.DsHost.exe --ds-id=ds-local-0 --shard-ids=0 --listen-port=19870 \
///                    --gateway-host=127.0.0.1 --gateway-port=19871 \
///                    --region=local --spawn-points=1
/// </code>
/// 环境变量（优先级低于命令行）：
/// <c>HORIZON_GATEWAY_HOST</c> / <c>HORIZON_GATEWAY_DS_UPLINK_PORT</c> / <c>HORIZON_DS_CONTROL_TOKEN</c>
/// </para>
/// <para>
/// 生命周期：启动数据面 → 连接网关并注册（失败按指数退避重试）→ 30s 心跳 → 60Hz tick →
/// 收到 Ctrl+C / SIGTERM 时**先摘除注册再退出**（优雅下线，避免网关路由到已死 DS 造成黑洞）。
/// </para>
/// </summary>
public static class Program
{
    /// <summary>
    /// Orleans 客户端连接超时（毫秒）。Orleans 是**可选增强**（只用于取掉落表静态配置），
    /// 其内部重试可能耗时数秒，不该拖慢 DS 就绪。超时即回退内置掉落表。
    /// </summary>
    private const int OrleansConnectTimeoutMs = 2000;

    /// <summary>入口。</summary>
    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        var parsed = ParseArgs(args);
        if (parsed is null)
        {
            PrintUsage();
            return 2;
        }

        var options = parsed;
        void Log(string message) => Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {message}");

        var hostOptions = new DsHostOptions
        {
            DsId = options.DsId,
            ShardIds = options.ShardIds,
            ListenPort = options.ListenPort,
            ListenAddress = options.ListenAddress,
            AdvertiseHost = options.AdvertiseHost,
            Region = options.Region,
            TicksPerSecond = options.TicksPerSecond,
            SpawnPoints = BuildSpawnPoints(options),
            EnableAoiFilter = options.EnableAoiFilter,
            AoiChunkSize = options.AoiChunkSize,
            AoiRadiusChunks = options.AoiRadiusChunks,
            MaxSessionsPerDs = options.MaxSessions,
        };

        if (hostOptions.SpawnPoints.Count > 0)
        {
            Log($"已配置 {hostOptions.SpawnPoints.Count} 个刷怪点（覆盖 shard {string.Join(',', options.ShardIds)}）。");
        }

        var uplinkOptions = new DsUplinkClientOptions
        {
            DsId = options.DsId,
            ShardIds = options.ShardIds,
            Host = options.AdvertiseHost,
            ListenPort = options.ListenPort,
            Region = options.Region,
            GatewayHost = options.GatewayHost,
            GatewayPort = options.GatewayPort,
            HeartbeatSeconds = options.HeartbeatSeconds,
            Token = Environment.GetEnvironmentVariable("HORIZON_DS_CONTROL_TOKEN") ?? string.Empty,
        };

        // MMORPG 开发计划 M4.3 收尾：可选接入 Orleans，让掉落配置来自真实 LootTableGrain。
        // 未配置时保持内置确定性掉落表（零依赖、可离线跑）。
        IClusterClient? clusterClient = null;
        if (!string.IsNullOrWhiteSpace(options.OrleansConnectionString))
        {
            try
            {
                // ⚠️ 必须带超时：Orleans 客户端在集群不可达时会做多轮内部重试
                //    （实测约 4s 才放弃）。DS 的实时服务**不依赖** Orleans
                //    （掉落表只是静态配置），没理由为它拖慢启动。
                //    超时后走 catch 分支回退内置掉落表，DS 照常就绪。
                var connectTask = ConnectOrleansAsync(options, Log);
                var completed = await Task.WhenAny(connectTask, Task.Delay(OrleansConnectTimeoutMs))
                    .ConfigureAwait(false);
                if (completed != connectTask)
                {
                    throw new TimeoutException($"连接 Orleans 超过 {OrleansConnectTimeoutMs}ms 未完成");
                }
                clusterClient = await connectTask.ConfigureAwait(false);

                var lootProvider = new OrleansLootProvider(new OrleansLootTableConfigSource(clusterClient));
                var templates = new List<int>();
                foreach (var sp in hostOptions.SpawnPoints)
                {
                    if (!templates.Contains(sp.MonsterTemplateId)) templates.Add(sp.MonsterTemplateId);
                }
                var warmed = await lootProvider.WarmupAsync(templates).ConfigureAwait(false);
                Log(warmed
                    ? $"掉落表已从 LootTableGrain 预热（{lootProvider.CachedTableCount} 张，模板 {string.Join(',', templates)}）。"
                    : $"警告：部分掉落表预热失败（已缓存 {lootProvider.CachedTableCount} 张）；未命中的击杀将不掉落（可观测，非静默编造）。");
                hostOptions.LootProvider = lootProvider;
            }
            catch (Exception ex)
            {
                // 这是**预期内**的 fail-soft 路径（Orleans 是可选增强），故用"信息"而非"错误"表述：
                // 掉落在两种模式下语义明确（内置表必掉 1 件 + 30% 追加），不存在静默错数据。
                Log($"Orleans 不可达（{ex.GetType().Name}: {ex.Message}）；回退内置确定性掉落表，DS 实时服务不受影响。");
                clusterClient = null;
            }
        }
        else
        {
            Log("未配置 --orleans-silo：使用内置确定性掉落表（掉落不由 LootTableGrain 驱动）。");
        }

        await using var uplink = new DsUplinkClient(uplinkOptions, Log);
        await using var runner = new DsHostRunner(hostOptions, uplink, Log);

        // 会话结束：网关在客户端断线时经同一条上行连接下发通知，DS 据此回收玩家实体。
        // 不接线的话 DS 会永久保留已断线的玩家并持续广播快照给死会话。
        uplink.SessionEndReceived += runner.NotifySessionEnded;

        using var shutdown = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            Log("收到中断信号，开始优雅下线…");
            shutdown.Cancel();
        };
        AppDomain.CurrentDomain.ProcessExit += (_, _) => shutdown.Cancel();

        await runner.StartAsync(shutdown.Token).ConfigureAwait(false);

        // 控制面：连接网关 → 注册 → 周期心跳。与 tick 循环解耦（网络抖动不应阻塞模拟）。
        var controlTask = RunControlPlaneAsync(uplink, uplinkOptions, Log, shutdown.Token);

        Log($"DS 就绪。Shard={string.Join(',', options.ShardIds)} 数据面端口={options.ListenPort} 网关={options.GatewayHost}:{options.GatewayPort}");

        try
        {
            await Task.Delay(Timeout.Infinite, shutdown.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // 正常关闭路径
        }

        // 优雅下线：先摘除注册，再停模拟。
        try
        {
            if (uplink.IsConnected)
            {
                await uplink.UnregisterAsync(CancellationToken.None).ConfigureAwait(false);
                Log("已向网关摘除注册。");
            }
        }
        catch (Exception ex)
        {
            Log($"摘除注册失败（忽略）：{ex.Message}");
        }

        try { await controlTask.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); } catch { /* 忽略 */ }

        Log($"退出。累计 tick={runner.TicksExecuted} 下行批次={runner.BatchesSent} 失败={runner.BatchesFailed} 上行处理={runner.InboundProcessed}");
        return 0;
    }

    /// <summary>控制面：连接（带指数退避）→ 注册 → 周期心跳。</summary>
    private static async Task RunControlPlaneAsync(
        DsUplinkClient uplink,
        DsUplinkClientOptions options,
        Action<string> log,
        CancellationToken ct)
    {
        if (options.GatewayPort <= 0)
        {
            log("未配置网关上行端口（--gateway-port / HORIZON_GATEWAY_DS_UPLINK_PORT），控制面未启用；DS 仅本地模拟。");
            return;
        }

        var backoff = options.ReconnectBackoffMs;

        while (!ct.IsCancellationRequested)
        {
            if (!uplink.IsConnected)
            {
                if (await uplink.ConnectAsync(ct).ConfigureAwait(false))
                {
                    backoff = options.ReconnectBackoffMs;
                    if (!await uplink.RegisterAsync(ct).ConfigureAwait(false))
                    {
                        log($"注册失败（{uplink.LastFailureReason}），将在 {backoff}ms 后重试。");
                        try { await Task.Delay(backoff, ct).ConfigureAwait(false); } catch { return; }
                        backoff = Math.Min(backoff * 2, options.ReconnectBackoffMaxMs);
                        continue;
                    }
                }
                else
                {
                    log($"连接网关失败，{backoff}ms 后重试。");
                    try { await Task.Delay(backoff, ct).ConfigureAwait(false); } catch { return; }
                    backoff = Math.Min(backoff * 2, options.ReconnectBackoffMaxMs);
                    continue;
                }
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(options.HeartbeatSeconds), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (ct.IsCancellationRequested) return;

            if (!await uplink.HeartbeatAsync(ct).ConfigureAwait(false))
            {
                // ⚠️ 这里曾经写成 DisposeAsync() + return —— 那会让控制面**永久停摆**：
                //    网关重启一次之后，DS 再也不会回来（注册表最终过期摘除 → 该 shard 永远回退 Orleans）。
                //    由 M5.3 混沌用例（DsHostReconnectLiveTests）捕获。
                // 正确做法：只断开连接（保持客户端对象可复用），退避后回到循环顶部重连 + 重新注册。
                log($"心跳失败（{uplink.LastFailureReason}），断开连接，{backoff}ms 后重连。");
                uplink.Disconnect();
                try { await Task.Delay(backoff, ct).ConfigureAwait(false); } catch { return; }
                backoff = Math.Min(backoff * 2, options.ReconnectBackoffMaxMs);
                continue;
            }

            // 心跳成功 = 链路健康，退避重置（下次故障仍从最小退避开始，避免"一次抖动后永久慢重连"）。
            backoff = options.ReconnectBackoffMs;
        }
    }

    /// <summary>
    /// 建立 Orleans 客户端连接（Redis 聚类，与网关/Silo 同构）。
    /// <para>
    /// DS 连 Orleans 的**唯一目的**是取掉落表等静态配置；实时权威仍在 DS 本地。
    /// 连接失败不致命——宿主会回退内置掉落表并继续提供实时服务。
    /// </para>
    /// </summary>
    /// <summary>
    /// Orleans 客户端宿主。必须持有引用：一旦被 GC 回收/释放，客户端连接随之断开。
    /// </summary>
    private static IHost? _orleansHost;

    /// <summary>
    /// 建立 Orleans 客户端连接（Redis 聚类，与网关/Silo 同构）。
    /// <para>
    /// 使用 <c>Host.CreateApplicationBuilder() + UseOrleansClient</c> 这一 Orleans 10 的标准引导方式——
    /// 旧的 <c>new ClientBuilder()</c> 无参构造在 Orleans 10 已移除
    /// （改为要求 <c>IServiceCollection/IConfiguration</c>，实测编译报 CS7036）。
    /// </para>
    /// </summary>
    private static async Task<IClusterClient> ConnectOrleansAsync(ParsedArgs options, Action<string> log)
    {
        var redisOptions = ConfigurationOptions.Parse(options.OrleansConnectionString!);

        var builder = Host.CreateApplicationBuilder();

        // ⚠️ 日志过滤**必须显式配置**，不能指望 appsettings.json 的 "Logging" 段生效。
        //    原因：本方法用 Host.CreateApplicationBuilder() 新建了一个独立宿主，
        //    它只加载**自己**的配置源（默认读当前工作目录的 appsettings.json，
        //    而不是 DsHost 自己的那份，且 DsHostFileConfig 压根不解析 "Logging" 段）。
        //    不配置的后果实测：Orleans 连不上集群时，Host 会以 **Error 级**喷出
        //    30+ 行栈追踪（ConnectionFailedException → SocketConnectionException → …），
        //    把紧随其后的 DS 关键启动日志("数据面已监听"/"DS 就绪")整个淹没。
        //    而这是**预期内**的 fail-soft 路径（连不上就回退内置掉落表），
        //    用 ERROR + 满屏栈来表达"预期内的降级"属于噪声，排障时反而掩盖真问题。
        builder.Logging.ClearProviders();
        builder.Logging.AddSimpleConsole(o =>
        {
            o.SingleLine = true;
            o.TimestampFormat = "HH:mm:ss ";
        });
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        // Orleans / Hosting / Redis 的"连不上"在 DS 语境下是**可降级的警告**，
        // 不是错误——真正的失败由上层 catch 统一报一条摘要。
        builder.Logging.AddFilter("Orleans", LogLevel.Error);
        builder.Logging.AddFilter("Microsoft.Extensions.Hosting", LogLevel.None);
        builder.Logging.AddFilter("StackExchange.Redis", LogLevel.Error);

        builder.UseOrleansClient(client =>
        {
            client.UseRedisClustering(o => o.ConfigurationOptions = redisOptions);
            client.Configure<ClusterOptions>(o =>
            {
                o.ClusterId = options.ClusterId;
                o.ServiceId = options.ServiceId;
            });
        });

        _orleansHost = builder.Build();
        await _orleansHost.StartAsync().ConfigureAwait(false);

        log($"已连接 Orleans 集群（ClusterId={options.ClusterId}）。");
        return _orleansHost.Services.GetRequiredService<IClusterClient>();
    }

    private static List<SpawnPointConfig> BuildSpawnPoints(ParsedArgs options)
    {
        var list = new List<SpawnPointConfig>();
        if (options.SpawnPointCount <= 0) return list;

        // 开发用默认刷怪点：沿 X 轴每 60m 一个，每个 3 只怪。
        // 生产应从 GameConfigGrain / 配置表下发——此处仅提供"起得来、看得见怪"的最小配置。
        for (var i = 0; i < options.SpawnPointCount; i++)
        {
            var cfg = SpawnPointConfig.Default;
            cfg.SpawnPointId = i + 1;
            cfg.ShardId = options.ShardIds.Length > 0 ? options.ShardIds[0] : 0;
            cfg.X = 20f + i * 60f;
            cfg.Y = 0f;
            cfg.Z = 0f;
            cfg.MaxAlive = 3;
            cfg.MonsterTemplateId = 1 + (i % 3);
            list.Add(cfg);
        }

        return list;
    }

    private sealed class ParsedArgs
    {
        public string DsId = "ds-local-0";
        public long[] ShardIds = new long[] { 0 };
        public int ListenPort = 19870;
        public string ListenAddress = "0.0.0.0";
        public string AdvertiseHost = "127.0.0.1";
        public string Region = string.Empty;
        public string GatewayHost = "127.0.0.1";
        public int GatewayPort;
        public int TicksPerSecond = 60;
        public int SpawnPointCount;

        /// <summary>Orleans 集群连接串（Redis 聚类）；空 = 不连 Orleans，用内置掉落表。</summary>
        public string? OrleansConnectionString;

        /// <summary>Orleans ClusterId（须与 Silo 一致）。</summary>
        public string ClusterId = "TYMYD-Cluster-001";

        /// <summary>Orleans ServiceId（须与 Silo 一致）。</summary>
        public string ServiceId = "TYMYD-Service-001";

        /// <summary>心跳间隔（秒）。默认 30（网关 90s 判过期 = 3×）。调小可加快故障发现。</summary>
        public int HeartbeatSeconds = 30;

        /// <summary>是否启用 AOI 分桶快照裁剪（默认 false）。</summary>
        public bool EnableAoiFilter = false;

        /// <summary>AOI chunk 边长（米，默认 16）。</summary>
        public int AoiChunkSize = 16;

        /// <summary>AOI 视野半径（chunk 数，默认 28）。</summary>
        public int AoiRadiusChunks = 28;

        /// <summary>单 DS 软容量上限（会话数，默认 5000）。</summary>
        public int MaxSessions = 5000;
    }

    private static ParsedArgs? ParseArgs(string[] args)
    {
        var result = new ParsedArgs();

        // ---- 第 1 层：配置文件（appsettings.json，与 exe 同目录）----
        // 把部署参数固化在文件里，启动时无需再拼一长串命令行参数。
        var file = DsHostFileConfig.Load(AppContext.BaseDirectory);
        if (file.LoadedFrom is not null)
        {
            Console.WriteLine($"[config] 已加载配置文件：{file.LoadedFrom}");
        }
        if (file.DsId is { Length: > 0 }) result.DsId = file.DsId;
        if (file.ShardIds is { Length: > 0 }) result.ShardIds = file.ShardIds;
        if (file.ListenPort is > 0 and <= 65535) result.ListenPort = file.ListenPort.Value;
        if (file.ListenAddress is { Length: > 0 }) result.ListenAddress = file.ListenAddress;
        if (file.AdvertiseHost is { Length: > 0 }) result.AdvertiseHost = file.AdvertiseHost;
        if (file.Region is not null) result.Region = file.Region;
        if (file.GatewayHost is { Length: > 0 }) result.GatewayHost = file.GatewayHost;
        if (file.GatewayPort is >= 0 and <= 65535) result.GatewayPort = file.GatewayPort.Value;
        if (file.TicksPerSecond is > 0 and <= 240) result.TicksPerSecond = file.TicksPerSecond.Value;
        if (file.HeartbeatSeconds is >= 1 and <= 300) result.HeartbeatSeconds = file.HeartbeatSeconds.Value;
        if (file.SpawnPointCount is >= 0 and <= 1000) result.SpawnPointCount = file.SpawnPointCount.Value;
        if (file.Orleans?.ConnectionString is { Length: > 0 }) result.OrleansConnectionString = file.Orleans.ConnectionString;
        if (file.Orleans?.ClusterId is { Length: > 0 }) result.ClusterId = file.Orleans.ClusterId;
        if (file.Orleans?.ServiceId is { Length: > 0 }) result.ServiceId = file.Orleans.ServiceId;

        // ---- 第 2 层：环境变量（覆盖文件；便于容器/CI 注入而不改文件）----
        var envGatewayHost = Environment.GetEnvironmentVariable("HORIZON_GATEWAY_HOST");
        if (!string.IsNullOrWhiteSpace(envGatewayHost)) result.GatewayHost = envGatewayHost!;
        if (int.TryParse(Environment.GetEnvironmentVariable("HORIZON_GATEWAY_DS_UPLINK_PORT"), out var gp)) result.GatewayPort = gp;
        var envRedis = Environment.GetEnvironmentVariable("HORIZON_ORLEANS_REDIS");
        if (!string.IsNullOrWhiteSpace(envRedis)) result.OrleansConnectionString = envRedis;

        // ---- 第 3 层：命令行参数（覆盖以上全部）----

        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            string? Value()
            {
                var idx = a.IndexOf('=');
                if (idx >= 0) return a.Substring(idx + 1);
                if (i + 1 < args.Length) { i++; return args[i]; }
                return null;
            }

            try
            {
                if (a.StartsWith("--ds-id", StringComparison.OrdinalIgnoreCase))
                {
                    result.DsId = Value() ?? result.DsId;
                }
                else if (a.StartsWith("--shard-ids", StringComparison.OrdinalIgnoreCase))
                {
                    var v = Value();
                    if (!string.IsNullOrWhiteSpace(v))
                    {
                        var parts = v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                        var ids = new List<long>(parts.Length);
                        foreach (var p in parts)
                        {
                            if (long.TryParse(p, out var id) && id >= 0) ids.Add(id);
                        }
                        if (ids.Count > 0) result.ShardIds = ids.ToArray();
                    }
                }
                else if (a.StartsWith("--listen-port", StringComparison.OrdinalIgnoreCase))
                {
                    if (int.TryParse(Value(), out var lp) && lp > 0 && lp <= 65535) result.ListenPort = lp;
                }
                else if (a.StartsWith("--listen-address", StringComparison.OrdinalIgnoreCase))
                {
                    result.ListenAddress = Value() ?? result.ListenAddress;
                }
                else if (a.StartsWith("--advertise-host", StringComparison.OrdinalIgnoreCase))
                {
                    result.AdvertiseHost = Value() ?? result.AdvertiseHost;
                }
                else if (a.StartsWith("--region", StringComparison.OrdinalIgnoreCase))
                {
                    result.Region = Value() ?? result.Region;
                }
                else if (a.StartsWith("--gateway-host", StringComparison.OrdinalIgnoreCase))
                {
                    result.GatewayHost = Value() ?? result.GatewayHost;
                }
                else if (a.StartsWith("--gateway-port", StringComparison.OrdinalIgnoreCase))
                {
                    if (int.TryParse(Value(), out var gwp) && gwp >= 0 && gwp <= 65535) result.GatewayPort = gwp;
                }
                else if (a.StartsWith("--tick-rate", StringComparison.OrdinalIgnoreCase))
                {
                    if (int.TryParse(Value(), out var tr) && tr > 0 && tr <= 240) result.TicksPerSecond = tr;
                }
                else if (a.StartsWith("--spawn-points", StringComparison.OrdinalIgnoreCase))
                {
                    if (int.TryParse(Value(), out var sp) && sp >= 0 && sp <= 1000) result.SpawnPointCount = sp;
                }
                else if (a.StartsWith("--heartbeat-seconds", StringComparison.OrdinalIgnoreCase))
                {
                    if (int.TryParse(Value(), out var hb) && hb >= 1 && hb <= 300) result.HeartbeatSeconds = hb;
                }
                else if (a.StartsWith("--enable-aoi", StringComparison.OrdinalIgnoreCase))
                {
                    // 裸开关（--enable-aoi）视为 true；带值（--enable-aoi=true/1/false/0）按值解析。
                    // 不使用 Value() 以免裸开关误吞后一个参数。
                    var eq = a.IndexOf('=');
                    if (eq >= 0)
                    {
                        var v = a.Substring(eq + 1);
                        result.EnableAoiFilter = v.Equals("true", StringComparison.OrdinalIgnoreCase) || v == "1";
                    }
                    else
                    {
                        result.EnableAoiFilter = true;
                    }
                }
                else if (a.StartsWith("--aoi-chunk-size", StringComparison.OrdinalIgnoreCase))
                {
                    if (int.TryParse(Value(), out var cs) && cs > 0 && cs <= 4096) result.AoiChunkSize = cs;
                }
                else if (a.StartsWith("--aoi-radius", StringComparison.OrdinalIgnoreCase))
                {
                    if (int.TryParse(Value(), out var ar) && ar >= 0 && ar <= 512) result.AoiRadiusChunks = ar;
                }
                else if (a.StartsWith("--max-sessions", StringComparison.OrdinalIgnoreCase))
                {
                    if (int.TryParse(Value(), out var ms) && ms > 0 && ms <= 100000) result.MaxSessions = ms;
                }
                else if (a.StartsWith("--orleans-silo", StringComparison.OrdinalIgnoreCase))
                {
                    result.OrleansConnectionString = Value() ?? Environment.GetEnvironmentVariable("HORIZON_ORLEANS_REDIS");
                }
                else if (a.StartsWith("--orleans-cluster-id", StringComparison.OrdinalIgnoreCase))
                {
                    result.ClusterId = Value() ?? result.ClusterId;
                }
                else if (a.StartsWith("--orleans-service-id", StringComparison.OrdinalIgnoreCase))
                {
                    result.ServiceId = Value() ?? result.ServiceId;
                }
                else if (a is "--help" or "-h" or "/?")
                {
                    return null;
                }
                else
                {
                    Console.Error.WriteLine($"未知参数：{a}");
                    return null;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"参数解析失败：{a} —— {ex.Message}");
                return null;
            }
        }

        if (string.IsNullOrWhiteSpace(result.DsId))
        {
            Console.Error.WriteLine("--ds-id 不能为空。");
            return null;
        }

        return result;
    }

    private static void PrintUsage()
    {
        Console.WriteLine(@"Horizon.DsHost —— 无头 Dedicated Server 宿主（MMORPG 开发计划 P3）

用法：
  Horizon.DsHost.exe [选项]

选项：
  --ds-id=<id>            本 DS 唯一标识（默认 ds-local-0）
  --shard-ids=0,1         承载的 shard 列表（默认 0）
  --listen-port=<port>    数据面监听端口（默认 19870）
  --listen-address=<ip>   数据面监听地址（默认 0.0.0.0）
  --advertise-host=<host> 对网关可达的主机（默认 127.0.0.1）
  --region=<name>         区域标识（默认空）
  --gateway-host=<host>   网关主机（默认 127.0.0.1，或 HORIZON_GATEWAY_HOST）
  --gateway-port=<port>   网关 DS 上行端口（或 HORIZON_GATEWAY_DS_UPLINK_PORT；0=不连网关）
  --tick-rate=<hz>        模拟频率（默认 60）
  --heartbeat-seconds=<n> 心跳间隔秒数（默认 30；网关 90s 判过期）
  --spawn-points=<n>      生成 n 个开发用刷怪点（默认 0）
  --enable-aoi[=true|false] 启用 AOI 分桶快照裁剪（裸开关即 true；默认 false = 全量快照）
  --aoi-chunk-size=<m>    AOI chunk 边长（米，默认 16）
  --aoi-radius=<n>        AOI 视野半径（chunk 数，默认 28 ≈448m）
  --max-sessions=<n>      单 DS 软容量上限（会话数，默认 5000；超过拒绝新玩家，reason=shard_full）
  --orleans-silo=<redis>  Orleans 集群连接串（Redis 聚类）。配置后掉落表来自真实
                          LootTableGrain（本地确定性掷骰）；不配置则用内置掉落表
  --orleans-cluster-id=<id>   Orleans ClusterId（默认 TYMYD-Cluster-001）
  --orleans-service-id=<id>   Orleans ServiceId（默认 TYMYD-Service-001）
  -h, --help              显示本帮助

环境变量：
  HORIZON_GATEWAY_HOST           网关主机
  HORIZON_GATEWAY_DS_UPLINK_PORT 网关 DS 上行端口
  HORIZON_DS_CONTROL_TOKEN       控制面共享令牌（须与网关 DsControlToken 一致）
  HORIZON_ORLEANS_REDIS          Orleans Redis 连接串（等价 --orleans-silo）

掉落链路说明：
  配了 --orleans-silo  → 掉落内容由 LootTableGrain 配置驱动，掷骰在 DS 本地确定性完成
                        （避免在 tick 线程上做跨进程 RPC，同时保证可重放）；
                        配置未命中的击杀**不掉落**（fail-closed，计数可观测）。
  未配 --orleans-silo → 使用内置极简掉落表（必掉 1 件 + 30% 追加）。
  无论哪种，掉落与击杀事件都会经网关转交 InventoryGrain / QuestGrain 落库。
");
    }
}
