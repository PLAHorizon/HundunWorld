using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

/// <summary>
/// 读取装配体元数据，找出所有名为 LZ4Pickler 的 TypeReference 及其所属程序集/命名空间。
/// 目的：判定客户端 Game.CSharp.dll 到底绑定到哪一份 LZ4Pickler。
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        var path = args.Length > 0
            ? args[0]
            : @"C:\Works\GitHubProjects\HundunWorld\HundunWorld\Binaries\GameEditorTarget\Windows\x64\Development\Game.CSharp.dll";

        Console.WriteLine($"目标装配体: {path}");
        if (!File.Exists(path))
        {
            Console.WriteLine("[FATAL] 文件不存在");
            return 1;
        }

        using var fs = File.OpenRead(path);
        using var pe = new PEReader(fs);
        var md = pe.GetMetadataReader();

        var hits = new List<string>();
        var scopeNames = new Dictionary<int, string>();

        foreach (var handle in md.TypeReferences)
        {
            var tr = md.GetTypeReference(handle);
            var name = md.GetString(tr.Name);
            if (!string.Equals(name, "LZ4Pickler", StringComparison.Ordinal))
                continue;

            var ns = md.GetString(tr.Namespace);
            var scope = DescribeScope(md, tr.ResolutionScope, scopeNames);
            hits.Add($"namespace='{ns}'  scope='{scope}'");
        }

        Console.WriteLine();
        if (hits.Count == 0)
        {
            Console.WriteLine("未找到任何 LZ4Pickler 类型引用。");
        }
        else
        {
            Console.WriteLine($"找到 {hits.Count} 处 LZ4Pickler 类型引用：");
            foreach (var h in hits)
                Console.WriteLine("  " + h);
        }

        // 顺带列出所有被引用的程序集（便于确认 K4os 是否在引用表里）
        Console.WriteLine();
        Console.WriteLine("引用的程序集：");
        foreach (var h in md.AssemblyReferences)
        {
            var ar = md.GetAssemblyReference(h);
            Console.WriteLine("  " + md.GetString(ar.Name));
        }

        return 0;
    }

    private static string DescribeScope(MetadataReader md, EntityHandle scope, Dictionary<int, string> cache)
    {
        if (scope.IsNil)
            return "(nil)";

        int key = MetadataTokens.GetToken(scope);
        if (cache.TryGetValue(key, out var cached))
            return cached;

        string result;
        switch (scope.Kind)
        {
            case HandleKind.AssemblyReference:
                result = md.GetString(md.GetAssemblyReference((AssemblyReferenceHandle)scope).Name);
                break;
            case HandleKind.TypeReference:
                result = "(嵌套类型) " + md.GetString(md.GetTypeReference((TypeReferenceHandle)scope).Name);
                break;
            case HandleKind.ModuleDefinition:
                result = "(本模块)";
                break;
            default:
                result = scope.Kind.ToString();
                break;
        }

        cache[key] = result;
        return result;
    }
}
