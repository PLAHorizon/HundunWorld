using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UE5ToFlaxConverter.Core.Models;
using UE5ToFlaxConverter.Core.Models.FlaxAssets;
using ParticleModule = UE5ToFlaxConverter.Core.Models.ParticleModule;

namespace UE5ToFlaxConverter.Core.Writers;

/// <summary>
/// 粒子写入器：UE5 Niagara/Cascade → Flax ParticleEmitter/ParticleSystem。
///
/// 输出文件（每个资源在独立目录）：
/// 1. Emitter_{EmitterName}.json —— FlaxEngine.ParticleEmitter 资源描述（每个 emitter 一个文件）
/// 2. {AssetName}.ParticleSystem.json —— FlaxEngine.ParticleSystem 资源描述（聚合所有 emitter 引用）
/// 3. {AssetName}.prefab.json —— FlaxEngine.Prefab，含 ParticleEffect Actor 引用 ParticleSystem
///
/// 注意：Flax 的 ParticleEmitter/ParticleSystem 是 .flax 二进制资源，
/// 需通过 Flax Editor API 创建（参考 import-manifest 中的说明）。
/// </summary>
public sealed class ParticleWriter
{
    private readonly string _outputRoot;

    public ParticleWriter(string outputRoot)
    {
        if (string.IsNullOrWhiteSpace(outputRoot))
            throw new ArgumentException("输出根目录不能为空", nameof(outputRoot));
        _outputRoot = Path.GetFullPath(outputRoot);
    }

    /// <summary>兼容旧 API：保留双参数构造（registry 参数被忽略）。</summary>
    public ParticleWriter(string outputRoot, FlaxAssetRegistry registry) : this(outputRoot) { }

    public async Task<WriterOutput> WriteAsync(IntermediateParticleSystem ps, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ps);
        if (string.IsNullOrWhiteSpace(ps.AssetName))
            throw new ArgumentException("PS.AssetName 不能为空", nameof(ps));

        var assetName = SanitizeAssetName(ps.AssetName);
        var subDirRel = $"Particles/{assetName}";
        var targetDir = Path.Combine(_outputRoot, "Particles", assetName);
        Directory.CreateDirectory(targetDir);
        var output = new WriterOutput { TargetDirectory = targetDir };

        var systemGuid = FlaxGuid.FromPath(ps.SourcePath + "#system");
        var prefabGuid = FlaxGuid.FromPath(ps.SourcePath + "#prefab");

        // 收集每个 emitter 的 GUID 与模块摘要，供 ParticleSystem 引用
        var emitterSummaries = new List<EmitterSummary>();

        // 1. 为每个 emitter 输出 Emitter_{Name}.json
        for (int i = 0; i < ps.Emitters.Count; i++)
        {
            var em = ps.Emitters[i];
            var safeName = SanitizeAssetName(string.IsNullOrEmpty(em.Name) ? $"Emitter_{i}" : em.Name);
            var emitterGuid = FlaxGuid.FromPath(ps.SourcePath + "#emitter_" + safeName);
            var emitterFlaxPath = JsonHelper.ToFlaxContentPath($"{subDirRel}/Emitter_{safeName}.flax");

            var emitterAsset = new FlaxAssetFile
            {
                ID = emitterGuid,
                TypeName = "FlaxEngine.ParticleEmitter",
                EngineBuild = 6705,
                Data = new ParticleEmitterData
                {
                    AssetName = safeName,
                    SourcePath = ps.SourcePath,
                    EmitterIndex = i,
                    Capacity = em.Capacity,
                    SimulationMode = em.SimulationMode.ToString(),
                    SimulationSpace = em.SimulationSpace.ToString(),
                    SpawnModules = em.SpawnModules ?? new(),
                    InitializeModules = em.InitializeModules ?? new(),
                    UpdateModules = em.UpdateModules ?? new(),
                    RenderModules = em.RenderModules ?? new(),
                    FlaxAssetPath = emitterFlaxPath
                }
            };

            var emitterPath = Path.Combine(targetDir, $"Emitter_{safeName}.json");
            await JsonHelper.SerializeToFileAsync(emitterAsset, emitterPath, ct);
            output.Files.Add(new OutputFile
            {
                RelativePath = Path.GetRelativePath(_outputRoot, emitterPath),
                Kind = "ParticleEmitter",
                Format = "json",
                SizeBytes = new FileInfo(emitterPath).Length
            });

            emitterSummaries.Add(new EmitterSummary
            {
                Name = safeName,
                Guid = emitterGuid,
                FlaxAssetPath = emitterFlaxPath,
                Capacity = em.Capacity,
                SimulationMode = em.SimulationMode.ToString(),
                SimulationSpace = em.SimulationSpace.ToString()
            });
        }

        // 2. {AssetName}.ParticleSystem.json —— 聚合所有 emitter 引用
        var systemFlaxPath = JsonHelper.ToFlaxContentPath($"{subDirRel}/{assetName}.flax");
        var systemAsset = new FlaxAssetFile
        {
            ID = systemGuid,
            TypeName = "FlaxEngine.ParticleSystem",
            EngineBuild = 6705,
            Data = new ParticleSystemData
            {
                AssetName = assetName,
                SourcePath = ps.SourcePath,
                Kind = ps.Kind.ToString(),
                FlaxAssetPath = systemFlaxPath,
                Emitters = emitterSummaries
            }
        };
        var systemPath = Path.Combine(targetDir, assetName + ".ParticleSystem.json");
        await JsonHelper.SerializeToFileAsync(systemAsset, systemPath, ct);
        output.Files.Add(new OutputFile
        {
            RelativePath = Path.GetRelativePath(_outputRoot, systemPath),
            Kind = "ParticleSystem",
            Format = "json",
            SizeBytes = new FileInfo(systemPath).Length
        });

        // 3. {AssetName}.prefab.json —— FlaxEngine.Prefab，含 ParticleEffect Actor 引用 ParticleSystem
        var prefabPath = Path.Combine(targetDir, assetName + ".prefab.json");
        var prefab = BuildParticleSystemPrefab(assetName, prefabGuid, systemGuid);
        await JsonHelper.SerializeToFileAsync(prefab, prefabPath, ct);
        output.Files.Add(new OutputFile
        {
            RelativePath = Path.GetRelativePath(_outputRoot, prefabPath),
            Kind = "ParticlePrefab",
            Format = "json",
            SizeBytes = new FileInfo(prefabPath).Length
        });

        output.PendingManualSteps.Add(
            $"在 Flax Editor 中调用 ParticleEmitter.CreateDefault() + AddModule<>() 创建每个 Emitter 的 .flax 资源，" +
            $"再用 ParticleSystem 聚合（详见 {systemPath}），Prefab 中的 ParticleEffect.System 引用 GUID: {systemGuid}");
        return output;
    }

    /// <summary>
    /// 构建 ParticleSystem prefab（FlaxEngine.ParticleEffect Actor 节点引用 ParticleSystem）。
    /// 使用 FlaxAssetFile 结构符合 Flax Engine 真实资源格式。
    /// </summary>
    private static FlaxAssetFile BuildParticleSystemPrefab(string assetName, string prefabGuid, string systemGuid)
    {
        var rootNodeId = FlaxGuid.NewGuid();
        var effectNodeId = FlaxGuid.NewGuid();

        return new FlaxAssetFile
        {
            ID = prefabGuid,
            TypeName = "FlaxEngine.Prefab",
            EngineBuild = 6705,
            Data = new List<FlaxNode>
            {
                new()
                {
                    ID = rootNodeId,
                    TypeName = "FlaxEngine.EmptyActor",
                    ParentID = FlaxAssetFile.NullGuid,
                    Name = assetName,
                    Transform = new FlaxTransform { Translation = new FlaxVector3(0, 0, 0) }
                },
                new()
                {
                    ID = effectNodeId,
                    TypeName = "FlaxEngine.ParticleEffect",
                    ParentID = rootNodeId,
                    Name = assetName + "_Effect",
                    Transform = new FlaxTransform { Translation = new FlaxVector3(0, 0, 0) },
                    ExtraFields = new Dictionary<string, object?>
                    {
                        ["ParticleSystem"] = systemGuid,
                        ["IsPlaying"] = true
                    }
                }
            }
        };
    }

    private static string SanitizeAssetName(string name)
    {
        if (string.IsNullOrEmpty(name)) return "Unnamed";
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            // 防止路径穿越：禁止 . 和 .. 与路径分隔符
            if (c == '.' || c == '/' || c == '\\' || System.Array.IndexOf(invalid, c) >= 0 || c == ' ')
                sb.Append('_');
            else
                sb.Append(c);
        }
        return sb.ToString();
    }
}

// ============ 输出数据模型（仅用于 JSON 序列化） ============

public sealed class ParticleEmitterData
{
    public string AssetName { get; set; } = string.Empty;
    public string SourcePath { get; set; } = string.Empty;
    public int EmitterIndex { get; set; }
    public int Capacity { get; set; } = 1000;
    public string SimulationMode { get; set; } = "CPU";
    public string SimulationSpace { get; set; } = "Local";
    public List<ParticleModule> SpawnModules { get; set; } = new();
    public List<ParticleModule> InitializeModules { get; set; } = new();
    public List<ParticleModule> UpdateModules { get; set; } = new();
    public List<ParticleModule> RenderModules { get; set; } = new();
    public string FlaxAssetPath { get; set; } = string.Empty;
}

public sealed class ParticleSystemData
{
    public string AssetName { get; set; } = string.Empty;
    public string SourcePath { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty; // Niagara / Cascade
    public string FlaxAssetPath { get; set; } = string.Empty;
    public List<EmitterSummary> Emitters { get; set; } = new();
}

public sealed class EmitterSummary
{
    public string Name { get; set; } = string.Empty;
    public string Guid { get; set; } = string.Empty;
    public string FlaxAssetPath { get; set; } = string.Empty;
    public int Capacity { get; set; }
    public string SimulationMode { get; set; } = "CPU";
    public string SimulationSpace { get; set; } = "Local";
}
