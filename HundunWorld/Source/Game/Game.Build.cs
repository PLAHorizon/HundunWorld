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

        string outputPath = AppDomain.CurrentDomain.BaseDirectory;

        // Common DLLs shared between editor and runtime builds
        string[] dlls = {
             "Horizon.Game.Message.dll",
                "Horizon.Game.ECS.dll",
                "Horizon.Game.Core.dll",
                "Horizon.Game.ECS.Arch.dll",
                // 握手 FormulaVersion 单点来源（协议 v9 硬校验）：NetworkManager.SendSyncHandshakeAsync 引用
                "Horizon.Game.Sim.dll",
                // ECS.Arch 的 LocalSimulationSystem 运行期调用 WorldCoord.ToChunkMortonKey：
                // 不列入引用清单会导致运行期 FileNotFoundException（客户端 ECS Tick 异常）
                "Horizon.Game.World.dll",
                "Horizon.Core.Abstract.dll",
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

        foreach (string dll in dlls)
        {
            string dllPath = Path.Combine(outputPath, dll);
            if (File.Exists(dllPath))
            {
                options.ScriptingAPI.FileReferences.Add(dllPath);
            }
            else
            {
                string altPath = Path.Combine(outputPath, "Tools", dll);
                if (File.Exists(altPath))
                {
                    options.ScriptingAPI.FileReferences.Add(altPath);
                }
                
            }
        }

        

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
