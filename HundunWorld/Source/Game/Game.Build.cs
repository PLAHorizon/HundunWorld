using Flax.Build;
using Flax.Build.NativeCpp;
using System;
using System.IO;

public class Game : GameModule
{
    /// <inheritdoc />
    public override void Init()
    {
        base.Init();

        // C#-only scripting
        BuildNativeCode = false;
    }

    /// <inheritdoc />
    public override void Setup(BuildOptions options)
    {
        base.Setup(options);

        options.ScriptingAPI.IgnoreMissingDocumentationWarnings = true;

        // 添加外部DLL引用
        string outputPath = AppDomain.CurrentDomain.BaseDirectory;

        string[] dlls = {
            "Microsoft.Extensions.ObjectPool.dll",
            "Microsoft.Extensions.DependencyInjection.Abstractions.dll",
            "Microsoft.Extensions.Options.dll",
            "Orleans.Serialization.dll",
            "Arch.dll",
            "System.Net.Sockets.dll",
            "System.Net.Ping.dll",
            "System.Net.NetworkInformation.dll",
            "Microsoft.Win32.Primitives.dll",
            "TouchSocket.dll",
            "TouchSocket.Core.dll",
            "MemoryPack.Core.dll",
            "Horizon.Game.Message.dll",
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
        }

        // 添加系统引用
        options.ScriptingAPI.SystemReferences.Add("System.Text.Json");
        options.ScriptingAPI.SystemReferences.Add("System.Net.Sockets");
        options.ScriptingAPI.SystemReferences.Add("System.Numerics.Vectors"); // 添加这行

        // 添加其他必要的系统引用
        options.ScriptingAPI.SystemReferences.Add("System.Runtime");
        options.ScriptingAPI.SystemReferences.Add("System.Collections");
        options.ScriptingAPI.SystemReferences.Add("System.Linq");
        options.ScriptingAPI.SystemReferences.Add("System.Numerics");
        

    }
}
