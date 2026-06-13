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

        string outputPath = AppDomain.CurrentDomain.BaseDirectory;

        // Common DLLs shared between editor and runtime builds
        string[] dlls = {
             "Horizon.Game.Message.dll",
                "Horizon.Game.ECS.dll",
                "Horizon.Game.ECS.Arch.dll",
            "Microsoft.Extensions.ObjectPool.dll",
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
                else
                {
                    throw new FileNotFoundException($"关键DLL缺失: {dll}，在 {outputPath} 和 {altPath} 均未找到。请确保所有依赖DLL已放置到正确位置。");
                }
            }
        }

        

        options.ScriptingAPI.SystemReferences.Add("System.Text.Json");
        options.ScriptingAPI.SystemReferences.Add("System.Net.Http");
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
