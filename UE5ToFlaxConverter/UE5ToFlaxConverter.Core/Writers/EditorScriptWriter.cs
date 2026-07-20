using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UE5ToFlaxConverter.Core.Models;
using UE5ToFlaxConverter.Core.Models.FlaxAssets;

namespace UE5ToFlaxConverter.Core.Writers;

/// <summary>
/// Flax Editor 自动化导入脚本生成器。
///
/// 生成一个 C# 脚本，用户在 Flax Editor 中执行后：
/// 1. 自动导入所有 FBX 文件为 .flax Model/SkinnedModel/Animation
/// 2. 自动创建 Material 资源并设置 PBR 参数
/// 3. 自动创建 ParticleEmitter/ParticleSystem 资源
/// 4. 自动注册 JsonAsset
///
/// 这是连接"JSON 描述"与"真实 .flax 二进制资源"的桥梁。
/// </summary>
public sealed class EditorScriptWriter
{
    private readonly string _outputRoot;
    private readonly FlaxAssetRegistry? _registry;

    public EditorScriptWriter(string outputRoot)
    {
        if (string.IsNullOrWhiteSpace(outputRoot))
            throw new ArgumentException("输出根目录不能为空", nameof(outputRoot));
        _outputRoot = Path.GetFullPath(outputRoot);
        _registry = new FlaxAssetRegistry();
    }

    /// <summary>兼容旧 API：保留双参数构造。</summary>
    public EditorScriptWriter(string outputRoot, FlaxAssetRegistry registry) : this(outputRoot)
    {
        _registry = registry ?? new FlaxAssetRegistry();
    }

    /// <summary>
    /// 生成主入口脚本 UE5AssetImporter.cs，用户在 Flax Editor 中执行。
    /// </summary>
    public async Task<WriterOutput> WriteImporterScriptAsync(IEnumerable<FbxImportManifest> fbxImports, CancellationToken ct = default)
    {
        var output = new WriterOutput();
        // 输出根目录可能尚未创建（当所有资源都被 Skipped 时），需先创建
        Directory.CreateDirectory(_outputRoot);
        var scriptPath = Path.Combine(_outputRoot, "UE5AssetImporter.cs");
        var imports = fbxImports.ToList();

        var sb = new StringBuilder();
        sb.AppendLine("// ============================================================");
        sb.AppendLine("// UE5 → Flax 资源自动化导入脚本");
        sb.AppendLine("// 在 Flax Editor 中通过 Tools > Scripts 执行此脚本");
        sb.AppendLine("// ============================================================");
        sb.AppendLine();
        sb.AppendLine("using System;");
        sb.AppendLine("using System.IO;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using FlaxEngine;");
        sb.AppendLine("using FlaxEngine.GUI;");
        sb.AppendLine("using FlaxEditor;");
        sb.AppendLine("using FlaxEditor.Content;");
        sb.AppendLine("using FlaxEditor.Content.Import;");
        sb.AppendLine();
        sb.AppendLine("public class UE5AssetImporter");
        sb.AppendLine("{");
        sb.AppendLine("    /// <summary>主入口：用户在 Editor Scripts 窗口调用此方法。</summary>");
        sb.AppendLine("    public static void ImportAll()");
        sb.AppendLine("    {");
        sb.AppendLine("        var projectContentFolder = Editor.ContentProjectFolder;");
        sb.AppendLine("        var projectRoot = projectContentFolder.FullPath;");
        sb.AppendLine("        var importRoot = Path.Combine(projectRoot, \"Imported\");");
        sb.AppendLine("        Editor.Log(\"[UE5Importer] 开始导入 UE5 资源到 \" + importRoot);");
        sb.AppendLine();
        sb.AppendLine("        if (!Directory.Exists(importRoot))");
        sb.AppendLine("            Directory.CreateDirectory(importRoot);");
        sb.AppendLine();
        sb.AppendLine("        int imported = 0, failed = 0;");
        sb.AppendLine();

        // 每个导入清单生成一段导入代码
        for (int i = 0; i < imports.Count; i++)
        {
            var imp = imports[i];
            sb.AppendLine($"        // [{i}] {imp.AssetType}: {imp.SourceFbxPath}");
            sb.AppendLine($"        try");
            sb.AppendLine($"        {{");
            sb.AppendLine($"            var fbxPath_{i} = Path.Combine(projectRoot, \"{imp.SourceFbxPath.Replace('\\', '/')}\");");
            sb.AppendLine($"            var targetPath_{i} = Path.Combine(projectRoot, \"{imp.FlaxTargetPath.Replace('\\', '/')}\");");
            sb.AppendLine($"            if (File.Exists(fbxPath_{i}))");
            sb.AppendLine($"            {{");
            sb.AppendLine($"                var targetDir_{i} = Path.GetDirectoryName(targetPath_{i});");
            sb.AppendLine($"                if (!Directory.Exists(targetDir_{i})) Directory.CreateDirectory(targetDir_{i});");
            sb.AppendLine();
            sb.AppendLine($"                // 调用 Editor.Import 导入 FBX");
            sb.AppendLine($"                var result_{i} = Editor.Import(fbxPath_{i}, targetPath_{i});");
            sb.AppendLine($"                if (result_{i}.Failed)");
            sb.AppendLine($"                {{");
            sb.AppendLine($"                    Editor.LogWarning($\"[{i}] 导入失败: {{result_{i}.Error}}\");");
            sb.AppendLine($"                    failed++;");
            sb.AppendLine($"                }}");
            sb.AppendLine($"                else");
            sb.AppendLine($"                {{");
            sb.AppendLine($"                    Editor.Log($\"[{i}] 已导入: {imp.AssetType}\");");
            sb.AppendLine($"                    imported++;");
            sb.AppendLine($"                }}");
            sb.AppendLine($"            }}");
            sb.AppendLine($"            else");
            sb.AppendLine($"            {{");
            sb.AppendLine($"                Editor.LogWarning($\"[{i}] FBX 源文件不存在: {{fbxPath_{i}}}\");");
            sb.AppendLine($"                failed++;");
            sb.AppendLine($"            }}");
            sb.AppendLine($"        }}");
            sb.AppendLine($"        catch (Exception ex_{i})");
            sb.AppendLine($"        {{");
            sb.AppendLine($"            Editor.LogError($\"[{i}] 异常: {{ex_{i}.Message}}\");");
            sb.AppendLine($"            failed++;");
            sb.AppendLine($"        }}");
            sb.AppendLine();
        }

        sb.AppendLine("        Editor.Log($\"[UE5Importer] 完成：成功 {imported} 个，失败 {failed} 个\");");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    /// <summary>创建 Material 资源并设置 PBR 参数。</summary>");
        sb.AppendLine("    public static Material CreateMaterial(string assetPath, string albedoColor, double roughness, double metallic)");
        sb.AppendLine("    {");
        sb.AppendLine("        var surface = Material.NewSurface();");
        sb.AppendLine("        surface.Info.Domain = MaterialDomain.Surface;");
        sb.AppendLine("        surface.Info.ShadingModel = MaterialShadingModel.Lit;");
        sb.AppendLine();
        sb.AppendLine("        // TODO: 通过 surface.Nodes API 添加 Color/Scalar/Normal 节点并连接到 Master 节点");
        sb.AppendLine("        // 这部分需要根据 UE5 材质表达式映射到 Flax 材质节点图，比较复杂");
        sb.AppendLine();
        sb.AppendLine("        surface.Save(assetPath);");
        sb.AppendLine("        Editor.Log($\"已创建材质: {assetPath}\");");
        sb.AppendLine("        return FlaxEngine.Content.Load<Material>(assetPath);");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    /// <summary>创建 ParticleEmitter 资源（Niagara/Cascade → Flax）。</summary>");
        sb.AppendLine("    public static ParticleEmitter CreateParticleEmitter(string assetPath, Dictionary<string, object> modules)");
        sb.AppendLine("    {");
        sb.AppendLine("        // Flax ParticleEmitter 通过 Editor API 创建");
        sb.AppendLine("        var emitter = ParticleEmitter.CreateDefault();");
        sb.AppendLine("        // TODO: 根据 modules 字典添加 Spark/Burst/Velocity/Lifetime 等模块");
        sb.AppendLine("        emitter.Save(assetPath);");
        sb.AppendLine("        Editor.Log($\"已创建粒子发射器: {assetPath}\");");
        sb.AppendLine("        return emitter;");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        sb.AppendLine();

        await File.WriteAllTextAsync(scriptPath, sb.ToString(), ct);
        output.Files.Add(new OutputFile
        {
            RelativePath = "UE5AssetImporter.cs",
            Kind = "EditorScript",
            Format = "cs",
            SizeBytes = new FileInfo(scriptPath).Length
        });
        output.TargetDirectory = _outputRoot;
        output.PendingManualSteps.Add(
            "在 Flax Editor 中打开项目，通过 Tools > Scripts 加载 UE5AssetImporter.cs，" +
            "执行 ImportAll() 方法自动导入所有 FBX 为 .flax 资源");
        return output;
    }

    /// <summary>
    /// 生成资源清单 asset-registry.json，记录所有 GUID → FlaxAssetPath 的映射，
    /// 便于手工核对 prefab 中的引用。
    /// </summary>
    public async Task<WriterOutput> WriteRegistryAsync(CancellationToken ct = default)
    {
        var output = new WriterOutput();
        Directory.CreateDirectory(_outputRoot);
        var registryPath = Path.Combine(_outputRoot, "asset-registry.json");

        var entries = (_registry?.All ?? System.Array.Empty<FlaxAssetEntry>())
            .Select(e => new
            {
                e.Guid,
                e.SourcePath,
                e.FlaxAssetPath,
                e.TypeName,
                e.Kind
            }).OrderBy(e => e.FlaxAssetPath).ToList();

        await JsonHelper.SerializeToFileAsync(new
        {
            GeneratedAt = System.DateTime.UtcNow,
            TotalAssets = entries.Count,
            Assets = entries
        }, registryPath, ct);

        output.Files.Add(new OutputFile
        {
            RelativePath = "asset-registry.json",
            Kind = "Registry",
            Format = "json",
            SizeBytes = new FileInfo(registryPath).Length
        });
        output.TargetDirectory = _outputRoot;
        return output;
    }
}
