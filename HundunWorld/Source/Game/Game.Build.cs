using Flax.Build;
using Flax.Build.NativeCpp;
using System;
using System.IO;

public class Game : GameModule
{
    public override void Init()
    {
        base.Init();
        BuildNativeCode = false;
    }

    public override void Setup(BuildOptions options)
    {
        base.Setup(options);

        options.ScriptingAPI.IgnoreMissingDocumentationWarnings = true;

        // NarrativePro 插件（任务/对话/地图导航）——游戏侧 UI 需直接读取其导航数据（POI/标记/瓦片）
        options.PublicDependencies.Add("NarrativePro");

        // MetaHuman 插件——创角捏脸页（CcFaceSculptPage/MetaHumanSculptPanel）需直接驱动 MetaHumanCharacter 数值会话
        options.PublicDependencies.Add("MetaHuman");

        string outputPath = AppDomain.CurrentDomain.BaseDirectory;

        // Common DLLs shared between editor and runtime builds
        // Horizon.Game.Core 是服务端 Orleans 宿主链：客户端编出的 Game.CSharp 对它零引用，
        // ECS.Arch 的 using Horizon.Game.Core.World 实际解析到 Horizon.Game.World.dll，故不入清单。
        //
        // Orleans.Serialization* 两条是"看着多余、实为必需"：客户端不跑 Orleans 序列化（走 MemoryPack），
        // 但 Orleans 源生成器已把 OrleansCodeGen.* 类型编进 Horizon.Game.Message / Horizon.Game.ECS.Arch。
        // 2026-10-07 实测（拿客户端产物目录删掉这两个 DLL 后反射）：Message 有 1328 个类型、ECS.Arch 有 1 个
        // 类型加载失败，而 HundunWorldGame.cs 用 SystemRegistry.RegisterFromAssembly(archAssembly) 的裸
        // GetTypes() 扫系统 —— 抛 ReflectionTypeLoadException 会让整批 ECS 同步系统注册失败。
        // 要摘掉这两条，得先让契约层不再被 Orleans 代码生成污染，或让扫描对 RTLE 容错。
        string[] dlls = {
             "Horizon.Game.Message.dll",
                "Horizon.Game.ECS.dll",
                "Horizon.Game.ECS.Arch.dll",
                // 星球坐标/区域/高程契约（Horizon.Planet，零外部依赖）：近景流送 spike 与后面的
                // 区域迁移都要用它。它没有任何 Flax/Orleans 引用，所以客户端与服务端共用同一份装配。
                "Horizon.Planet.dll",
                // 握手 FormulaVersion 单点来源（协议 v9 硬校验）：NetworkManager.SendSyncHandshakeAsync 引用。
                // 客户端只装 Contracts（公式/步进器/跳跃状态机/修正包/配置），不装 Horizon.Game.Sim
                // ——后者是服务端权威核（MovementValidator 反作弊、CombatCore、MonsterAiCore、
                // SpawnDirectorCore、SimWorld、金轨迹回放、任务校验，约 5.4k 行）。
                "Horizon.Game.Sim.Contracts.dll",
                // ECS.Arch 的 LocalSimulationSystem 运行期调用 WorldCoord.ToChunkMortonKey：
                // 不列入引用清单会导致运行期 FileNotFoundException（客户端 ECS Tick 异常）。
                // 同理只装 Contracts（分块换算 + Morton 编码），AOI 视野集算法留在服务端 Horizon.Game.World。
                "Horizon.Game.World.Contracts.dll",
                // 客户端只用到 AppType / PassportType 两个枚举（GengDiAuthService），
                // 故换装 Horizon.Share.Client 而不是 Horizon.Core.Abstract
                // ——后者还带 ServerToken/IdentityTokenData/RRPC/TransactionMessage/Storage 接口等
                // 约 535 行服务端载体与其 Orleans/log4net/AutoMapper 依赖。
                "Horizon.Share.Client.dll",
            "Microsoft.Extensions.ObjectPool.dll",
            // Horizon.Game.ECS.Arch 的每个 System.Update 都调用 ILogger 扩展方法（Log?.LogDebug），
            // 即使接收者为 null，JIT 仍要解析声明程序集；不在清单里就不会被复制到客户端输出目录，
            // 表现为 PIE 每帧刷 "Could not load file or assembly 'Microsoft.Extensions.Logging.Abstractions, Version=10.0.0.0'"
            "Microsoft.Extensions.Logging.Abstractions.dll",
            "Microsoft.Extensions.DependencyInjection.Abstractions.dll",
            "Microsoft.Extensions.Options.dll",
            "Microsoft.Extensions.Primitives.dll",
            "Microsoft.Extensions.DependencyModel.dll",
            "Microsoft.DotNet.PlatformAbstractions.dll",
            "System.IO.Hashing.dll",
            "Orleans.Serialization.dll",
            "Arch.dll",
            "TouchSocket.dll",
            "TouchSocket.Core.dll",
            "MemoryPack.Core.dll",
            "K4os.Compression.LZ4.dll",
            "Collections.Pooled.dll",
            "Schedulers.dll",
            "Arch.LowLevel.dll",
            "CommunityToolkit.HighPerformance.dll",
            "Orleans.Serialization.Abstractions.dll",
            "LiteDB.dll"
        };

        string projectDir = Globals.Project != null ? Globals.Project.ProjectFolderPath : outputPath;
        string repoDir = Path.GetDirectoryName(
            projectDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        var missing = new System.Collections.Generic.List<string>();

        foreach (string dll in dlls)
        {
            string found = null;

            // Horizon.* 是仓库自己的共享契约程序集，优先取刚编出来的那份：引擎 Tools 里常残留
            // 上一次构建的旧副本，名字对、内容旧，报出来的却是 CS0234/CS0246「类型不存在」
            // （与 ServerGame.Build.cs 的 V5 教训同源）。第三方 DLL 落不进这些目录，自动走引擎路径。
            if (dll.StartsWith("Horizon.", StringComparison.Ordinal) && repoDir != null)
            {
                string asmName = Path.GetFileNameWithoutExtension(dll);
                foreach (string dir in new[]
                {
                    Path.Combine(repoDir, asmName, "bin", "Debug", "net10.0"),
                    Path.Combine(repoDir, asmName, "bin", "Release", "net10.0"),
                    Path.Combine(projectDir, "Output", "Windows"),
                })
                {
                    string candidate = Path.Combine(dir, dll);
                    if (File.Exists(candidate)) { found = candidate; break; }
                }
            }

            foreach (string dir in new[] { outputPath, Path.Combine(outputPath, "Tools") })
            {
                if (found != null) break;
                string candidate = Path.Combine(dir, dll);
                if (File.Exists(candidate)) found = candidate;
            }

            if (found != null)
                options.ScriptingAPI.FileReferences.Add(found);
            else
                missing.Add(dll);
        }

        // 历史行为是「找不到就静默跳过」，于是缺一个程序集要等到运行期类型加载才炸。
        // 这里只大声警告不中断：全新克隆且尚未 dotnet build 时，引擎 Tools 那份仍能用。
        if (missing.Count > 0)
            Log.Warning($"Game：以下共享程序集未找到，已跳过引用（客户端会在运行期缺类型）：{string.Join(", ", missing)}");

        

        options.ScriptingAPI.SystemReferences.Add("System.Text.Json");
        options.ScriptingAPI.SystemReferences.Add("System.Net.Http");
        // DevTlsTrustPolicy 使用 SslPolicyErrors（TLS 证书校验回调），缺失该引用会编译失败
        options.ScriptingAPI.SystemReferences.Add("System.Net.Security");
        options.ScriptingAPI.SystemReferences.Add("System.Net.HttpListener");
        options.ScriptingAPI.SystemReferences.Add("System.Net.Sockets");
        options.ScriptingAPI.SystemReferences.Add("System.Net.Ping");
        options.ScriptingAPI.SystemReferences.Add("System.Net.NetworkInformation");
        options.ScriptingAPI.SystemReferences.Add("System.Threading");
        options.ScriptingAPI.SystemReferences.Add("Microsoft.Win32.Primitives");
        options.ScriptingAPI.SystemReferences.Add("Microsoft.Win32.Registry");
        options.ScriptingAPI.SystemReferences.Add("System.Diagnostics.Process");
        options.ScriptingAPI.SystemReferences.Add("System.Numerics.Vectors");
        options.ScriptingAPI.SystemReferences.Add("System.Runtime");
        options.ScriptingAPI.SystemReferences.Add("System.Collections");
        options.ScriptingAPI.SystemReferences.Add("System.Linq");
        options.ScriptingAPI.SystemReferences.Add("System.Numerics");
    }
}
