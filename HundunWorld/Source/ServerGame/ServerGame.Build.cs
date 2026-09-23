using Flax.Build;
using Flax.Build.NativeCpp;
using System;
using System.IO;

/// <summary>
/// 无头 Dedicated Server 模块（DS 宿主）。
/// <para>
/// <b>设计要点</b>：本模块<b>不实现</b>任何游戏逻辑，只做三件事：
/// <list type="number">
///   <item>把 DS 侧托管程序集（<c>Horizon.DsHost</c> / <c>Horizon.Game.Sim</c> / <c>Horizon.Game.World</c> 等）
///   注册进 Flax 的脚本编译引用；</item>
///   <item>补齐 DS 需要的 BCL 系统引用（网络 socket、线程、诊断）；</item>
///   <item>提供一个 <c>Script</c> 承载 tick 驱动（见 <c>Server/DedicatedServerHost.cs</c>）。</item>
/// </list>
/// </para>
/// <para>
/// <b>为何不引用 Game 模块</b>：<c>Game.Build.cs</c> 里挂载了大量客户端专属 DLL 与引用
/// （渲染、输入、相机、ActorSync）。DS 无窗口、无输入，加载它们只会拖慢启动并制造
/// 无意义的状态依赖。两个模块的 DLL 清单刻意分离。
/// </para>
/// <para>
/// <b>与 UE 宿主的关系</b>（承接 <c>Horizon.DsHost.csproj</c> 的注释）：权威模拟是纯 C#
/// （<c>Horizon.Game.Sim</c>），数据面协议在 <c>Horizon.Game.Core/Sim/Server</c>。
/// Flax / UE 只是"承载进程"，切换宿主形态<b>不触碰协议与玩法代码</b>。
/// </para>
/// </summary>
public class ServerGame : GameModule
{
    public override void Init()
    {
        base.Init();

        // 保持纯 C# 托管域：DS 不需要任何原生 C++ 代码。
        BuildNativeCode = false;
    }

    public override void Setup(BuildOptions options)
    {
        base.Setup(options);

        // DS 侧代码大量使用 XML 注释且不追求文档完备，避免噪音淹没真正的警告。
        options.ScriptingAPI.IgnoreMissingDocumentationWarnings = true;

        // ── 程序集搜索路径 ────────────────────────────────────────────────
        // Flax 构建时 BaseDirectory 是引擎二进制目录，托管 DLL 未必在那里。
        //
        // ⚠️ 顺序至关重要，**项目输出必须排在引擎目录之前**。实测教训（V5）：
        //    引擎的 `Binaries/Tools/` 里残留着**上一次构建复制过去的旧版** Horizon.*.dll。
        //    若把 engineDir 排前面，这些陈旧程序集会被抢先命中——构建照样成功（引用解析得到），
        //    但引用到的是**不含新类型**的旧二进制，于是出现这类迷惑性错误：
        //        DedicatedServerHost.cs: error CS0246: 'DsUplinkClient' could not be found
        //    明明 `using Horizon.Game.Core.Sim.Server;` 就在文件顶部、DLL 也"找到了"。
        //    这种"引用了名字对但内容旧的程序集"的失败模式，在日志里表现为
        //    "复制了 1 个文件"（只复制了真正新增的那个），其余静默用旧版——极难排查。
        //    因此项目自己的 Output 目录必须最优先。
        string engineDir = AppDomain.CurrentDomain.BaseDirectory;
        // 工程目录：本 Flax 版本的 BuildOptions 无 ProjectFolderPath，改用 Flax.Build.Globals.Project
        string projectDir = Globals.Project != null ? Globals.Project.ProjectFolderPath : engineDir;
        string[] searchDirs =
        {
            // ★ 项目自身构建产物优先——这些才是 Horizon.sln 刚编译出来的最新程序集。
            Path.Combine(projectDir, "Output", "Windows"),
            Path.Combine(projectDir, "Output", "Windows", "Development"),
            // 兜底：引擎目录（仅当项目输出缺该 DLL 时才会走到这里）。
            Path.Combine(engineDir, "Tools"),
            engineDir,
        };

        // ── DS 侧托管 DLL 清单 ────────────────────────────────────────────
        // 这是 Horizon.DsHost 输出目录下**实际被 DS 链路用到**的子集（按 csproj 依赖闭包裁剪）：
        //   Horizon.DsHost → { Horizon.Game.Core, Horizon.Game.Sim, Horizon.Game.World,
        //                      Horizon.Game.Message, Horizon.Core.Abstract }
        //   Horizon.Game.Core → 另需 MemoryPack / K4os / Orleans.Serialization / Microsoft.Extensions.*
        //
        // 刻意**不含**：Arch / Arch.LowLevel / Schedulers（ECS 客户端侧）、LiteDB（客户端本地持久化）、
        // TouchSocket*（网关侧监听，DS 用 BCL TcpClient/TcpListener）、
        // Collections.Pooled、CommunityToolkit.HighPerformance、Microsoft.DotNet.PlatformAbstractions
        // （Game.Build.cs 里有，但 DS 链路零引用）。少加载一个程序集就少一份启动开销与版本冲突面。
        string[] dlls =
        {
            // ── DS 宿主与权威模拟（本次改造新增的核心三项）──
            "Horizon.DsHost.dll",
            "Horizon.Game.Sim.dll",
            "Horizon.Game.World.dll",

            // ── 协议与消息契约 ──
            "Horizon.Game.Core.dll",
            "Horizon.Game.Message.dll",

            // ── 基础库 ──
            "Horizon.Core.Abstract.dll",

            // ── 序列化 / 压缩（MemoryPack 静态代码生成，无运行时反射）──
            "MemoryPack.Core.dll",
            "K4os.Compression.LZ4.dll",
            "System.IO.Hashing.dll",

            // ── Microsoft.Extensions（DS 用 DI / Options / 日志抽象）──
            "Microsoft.Extensions.ObjectPool.dll",
            "Microsoft.Extensions.DependencyInjection.Abstractions.dll",
            "Microsoft.Extensions.Options.dll",
            "Microsoft.Extensions.Primitives.dll",
            "Microsoft.Extensions.DependencyModel.dll",
            "Microsoft.Extensions.Logging.Abstractions.dll",

            // ── Orleans 序列化契约（DsGrainContracts 用 [GenerateSerializer] 标记的类型；
            //    仅序列化特性，不需要 Orleans 运行时）──
            "Orleans.Serialization.dll",
            "Orleans.Serialization.Abstractions.dll",
        };

        // ⚠️ 关键：Game.Build.cs 的写法是「找不到就静默跳过」（else 分支为空），
        //    缺 DLL 不报错、直到运行时类型加载才炸。这里刻意改为「找不到就抛异常」——
        //    DS 是无人值守进程，构建期失败远优于运行时半夜挂掉。
        var missing = new System.Collections.Generic.List<string>();
        foreach (string dll in dlls)
        {
            string? found = null;
            foreach (string dir in searchDirs)
            {
                string candidate = Path.Combine(dir, dll);
                if (File.Exists(candidate)) { found = candidate; break; }
            }

            if (found is not null)
            {
                options.ScriptingAPI.FileReferences.Add(found);
            }
            else
            {
                missing.Add(dll);
            }
        }

        if (missing.Count > 0)
        {
            // BuildException 在 Flax.Build 中为 internal，项目侧 Build.cs 只能用普通 Exception 失败构建；
            // Flax.Build 会捕获并打印消息后以非零码退出（Program.cs 179-183 行）。
            throw new Exception(
                "ServerGame：以下 DS 侧程序集未找到，构建终止（避免运行时才失败）：\n  - "
                + string.Join("\n  - ", missing)
                + "\n\n请先执行 `dotnet build Horizon.DsHost/Horizon.DsHost.csproj` 生成它们，"
                + "并确认其输出落在以下任一目录：\n  " + string.Join("\n  ", searchDirs));
        }

        // ── BCL 系统引用 ──────────────────────────────────────────────────
        // DS 只保留网络 / 线程 / 诊断 / 数值，去掉客户端要的 HttpListener、Ping、Registry 等。
        options.ScriptingAPI.SystemReferences.Add("System.Net.Sockets");     // DsUplinkClient / DsDataPlaneHost
        options.ScriptingAPI.SystemReferences.Add("System.Net.NetworkInformation");
        options.ScriptingAPI.SystemReferences.Add("System.Net.Http");        // Orleans 客户端引导
        options.ScriptingAPI.SystemReferences.Add("System.Threading");
        // ⚠️ System.Threading.ThreadPool 必须**单独**引用：自 .NET 起 ThreadPool 类型被
        //    "type forward" 到这个独立程序集，只引 System.Threading 不够。实测报错：
        //      CS1069: The type name 'ThreadPool' could not be found in the namespace
        //              'System.Threading'. This type has been forwarded to assembly
        //              'System.Threading.ThreadPool'.
        //    症状具有欺骗性——位于 System.Threading 命名空间却报"找不到"，因为它在**另一个程序集**里。
        options.ScriptingAPI.SystemReferences.Add("System.Threading.ThreadPool");
        options.ScriptingAPI.SystemReferences.Add("System.Runtime");
        options.ScriptingAPI.SystemReferences.Add("System.Collections");
        options.ScriptingAPI.SystemReferences.Add("System.Collections.Concurrent");
        options.ScriptingAPI.SystemReferences.Add("System.Linq");
        options.ScriptingAPI.SystemReferences.Add("System.Numerics");
        options.ScriptingAPI.SystemReferences.Add("System.Numerics.Vectors");
        options.ScriptingAPI.SystemReferences.Add("System.Diagnostics.Process");
        options.ScriptingAPI.SystemReferences.Add("System.Text.Json");       // 配置文件解析（DsHostFileConfig）
    }
}
