using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UE5ToFlaxConverter.Core.Models;
using UE5ToFlaxConverter.Core.Models.FlaxAssets;

namespace UE5ToFlaxConverter.Core.Writers;

/// <summary>
/// 动画写入器：UE5 AnimSequence/AnimMontage/BlendSpace → Flax 资源描述。
///
/// 输出文件（每个资源在独立目录）：
/// 1. {AssetName}.prefab.json —— FlaxEngine.Prefab，含 AnimationGraph 占位 Actor 引用 Animation
/// 2. {AssetName}.metadata.json —— 动画元数据 JsonAsset（Notifies/Curves/BlendSamples/TrackBoneNames）
/// 3. import-manifest.json —— FBX 导入清单（指导 Flax Editor 导入 FBX 为 .flax Animation）
/// 4. montage-sections.json —— Montage 段落（仅 AnimMontage）
/// </summary>
public sealed class AnimationWriter
{
    private readonly string _outputRoot;

    public AnimationWriter(string outputRoot)
    {
        if (string.IsNullOrWhiteSpace(outputRoot))
            throw new ArgumentException("输出根目录不能为空", nameof(outputRoot));
        _outputRoot = Path.GetFullPath(outputRoot);
    }

    /// <summary>兼容旧 API：保留双参数构造（registry 参数被忽略）。</summary>
    public AnimationWriter(string outputRoot, FlaxAssetRegistry registry) : this(outputRoot) { }

    public async Task<WriterOutput> WriteAsync(IntermediateAnimation anim, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(anim);
        if (string.IsNullOrWhiteSpace(anim.AssetName))
            throw new ArgumentException("Anim.AssetName 不能为空", nameof(anim));

        var assetName = SanitizeAssetName(anim.AssetName);
        var subDirRel = $"Animations/{assetName}";
        var targetDir = Path.Combine(_outputRoot, "Animations", assetName);
        Directory.CreateDirectory(targetDir);
        var output = new WriterOutput { TargetDirectory = targetDir };

        var animGuid = FlaxGuid.FromPath(anim.SourcePath + "#anim");
        var metadataGuid = FlaxGuid.FromPath(anim.SourcePath + "#meta");
        var animGraphGuid = FlaxGuid.FromPath(anim.SourcePath + "#animgraph");

        // 规范化 MontageSegments.End：将 -1 替换为动画总时长（与测试期望一致）
        NormalizeMontageSegments(anim);

        // 1. .prefab.json（FlaxEngine.Prefab，含 AnimationGraph 引用 Animation 资源）
        var prefabPath = Path.Combine(targetDir, assetName + ".prefab.json");
        var prefab = BuildAnimGraphPrefab(anim, animGraphGuid, animGuid);
        await JsonHelper.SerializeToFileAsync(prefab, prefabPath, ct);
        output.Files.Add(new OutputFile
        {
            RelativePath = Path.GetRelativePath(_outputRoot, prefabPath),
            Kind = "Prefab",
            Format = "json",
            SizeBytes = new FileInfo(prefabPath).Length
        });

        // 2. .metadata.json（动画元数据 JsonAsset）
        var metadataPath = Path.Combine(targetDir, assetName + ".metadata.json");
        var metadataAsset = new FlaxAssetFile
        {
            ID = metadataGuid,
            TypeName = "HundunWorld.Game.AnimationMetadata, HundunWorld.Game",
            EngineBuild = 6705,
            Data = new AnimationMetadataData
            {
                AssetName = anim.AssetName,
                SourcePath = anim.SourcePath,
                Duration = anim.DurationSeconds,
                FrameRate = anim.FrameRate,
                TotalFrames = anim.TotalFrames,
                Kind = anim.Kind.ToString(),
                SkeletonName = anim.SkeletonName,
                Notifies = anim.Notifies ?? new List<AnimNotify>(),
                MontageSegments = anim.MontageSegments ?? new List<AnimSegment>(),
                FloatCurves = anim.FloatCurves ?? new List<AnimationCurve>(),
                BlendSamples = anim.BlendSamples ?? new List<BlendSpaceSample>(),
                TrackBoneNames = anim.TrackBoneNames ?? new List<string>()
            }
        };
        await JsonHelper.SerializeToFileAsync(metadataAsset, metadataPath, ct);
        output.Files.Add(new OutputFile
        {
            RelativePath = Path.GetRelativePath(_outputRoot, metadataPath),
            Kind = "AnimationMetadata",
            Format = "json",
            SizeBytes = new FileInfo(metadataPath).Length
        });

        // 3. import-manifest.json（FBX 导入清单）
        var importManifestPath = Path.Combine(targetDir, "import-manifest.json");
        var importManifest = new FbxImportManifest
        {
            AssetName = assetName,
            AssetType = "Animation",
            SourceMeshKind = anim.Kind.ToString(),
            SourcePath = anim.SourcePath,
            SourceFbxPath = JsonHelper.ToFlaxContentPath($"{subDirRel}/{assetName}.fbx"),
            FlaxTargetPath = JsonHelper.ToFlaxContentPath($"{subDirRel}/{assetName}.flax"),
            TargetGuid = animGuid,
            ImportSettings = new FbxImportSettings
            {
                Type = "Animation",
                Enum = 2,
                Scale = 1,
                ImportMaterials = false,
                ImportTextures = false
            },
            LODs = new List<LODDescription>()
        };
        await JsonHelper.SerializeToFileAsync(importManifest, importManifestPath, ct);
        output.Files.Add(new OutputFile
        {
            RelativePath = Path.GetRelativePath(_outputRoot, importManifestPath),
            Kind = "ImportScript",
            Format = "json",
            SizeBytes = new FileInfo(importManifestPath).Length
        });

        // 4. montage-sections.json（仅 Montage）
        var montageSegments = anim.MontageSegments!;
        if (anim.Kind == AnimationKind.Montage && montageSegments.Count > 0)
        {
            var sectionsPath = Path.Combine(targetDir, "montage-sections.json");
            await JsonHelper.SerializeToFileAsync(new
            {
                assetName = assetName,
                duration = anim.DurationSeconds,
                frameRate = anim.FrameRate,
                sections = montageSegments.ConvertAll(s => new
                {
                    name = s.SectionName,
                    start = s.Start,
                    end = s.End,
                    loopCount = s.LoopCount
                })
            }, sectionsPath, ct);
            output.Files.Add(new OutputFile
            {
                RelativePath = Path.GetRelativePath(_outputRoot, sectionsPath),
                Kind = "MontageSections",
                Format = "json",
                SizeBytes = new FileInfo(sectionsPath).Length
            });
        }

        // 5. blend-space-samples.json（仅 BlendSpace）
        var blendSamples = anim.BlendSamples!;
        if (anim.Kind == AnimationKind.BlendSpace && blendSamples.Count > 0)
        {
            var samplesPath = Path.Combine(targetDir, "blend-space-samples.json");
            await JsonHelper.SerializeToFileAsync(new
            {
                assetName = assetName,
                axisCount = anim.BlendAxes.Count,
                samples = blendSamples.ConvertAll(s => new
                {
                    anim = s.AnimName,
                    x = s.Position.X,
                    y = s.Position.Y
                })
            }, samplesPath, ct);
            output.Files.Add(new OutputFile
            {
                RelativePath = Path.GetRelativePath(_outputRoot, samplesPath),
                Kind = "BlendSpaceSamples",
                Format = "json",
                SizeBytes = new FileInfo(samplesPath).Length
            });
        }

        output.PendingManualSteps.Add(
            $"导出动画 FBX 到 {JsonHelper.ToFlaxContentPath($"{subDirRel}/{assetName}.fbx")}（在 UE5 中右键 Asset → Export，保留 Skeleton），" +
            $"然后在 Flax Editor 中执行 UE5AssetImporter.ImportAll() 导入为 .flax Animation（GUID: {animGuid}）");

        return output;
    }

    /// <summary>
    /// 规范化 MontageSegments.End：将 -1（未初始化）替换为动画总时长。
    /// </summary>
    private static void NormalizeMontageSegments(IntermediateAnimation anim)
    {
        if (anim.MontageSegments == null || anim.MontageSegments.Count == 0) return;
        for (int i = 0; i < anim.MontageSegments.Count; i++)
        {
            if (anim.MontageSegments[i].End < 0)
                anim.MontageSegments[i].End = anim.DurationSeconds;
        }
    }

    /// <summary>
    /// 构建动画图占位 prefab（FlaxEngine.AnimationGraph 节点，引用 Animation 资源）。
    /// </summary>
    private static FlaxAssetFile BuildAnimGraphPrefab(IntermediateAnimation anim, string animGraphGuid, string animGuid)
    {
        var graphNodeId = FlaxGuid.NewGuid();

        return new FlaxAssetFile
        {
            ID = animGraphGuid,
            TypeName = "FlaxEngine.Prefab",
            EngineBuild = 6705,
            Data = new List<FlaxNode>
            {
                new()
                {
                    ID = graphNodeId,
                    TypeName = "FlaxEngine.EmptyActor",
                    ParentID = FlaxAssetFile.NullGuid,
                    Name = anim.AssetName + "_AnimGraphHolder",
                    Transform = new FlaxTransform { Translation = new FlaxVector3(0, 0, 0) },
                    ExtraFields = new Dictionary<string, object?>
                    {
                        ["AnimationGraph"] = animGraphGuid,
                        ["DefaultAnimation"] = animGuid
                    }
                }
            }
        };
    }

    private static string SanitizeAssetName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new System.Text.StringBuilder(name.Length);
        foreach (var c in name)
        {
            if (c == '.' || System.Array.IndexOf(invalid, c) >= 0 || c == ' ' || c == '/' || c == '\\')
                sb.Append('_');
            else
                sb.Append(c);
        }
        return sb.ToString();
    }
}

/// <summary>动画元数据 JsonAsset 内部 Data 结构。</summary>
public sealed class AnimationMetadataData
{
    public string AssetName { get; set; } = string.Empty;
    public string SourcePath { get; set; } = string.Empty;
    public float Duration { get; set; }
    public float FrameRate { get; set; }
    public int TotalFrames { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string SkeletonName { get; set; } = string.Empty;
    public List<AnimNotify> Notifies { get; set; } = new();
    public List<AnimSegment> MontageSegments { get; set; } = new();
    public List<AnimationCurve> FloatCurves { get; set; } = new();
    public List<BlendSpaceSample> BlendSamples { get; set; } = new();
    public List<string> TrackBoneNames { get; set; } = new();
}
