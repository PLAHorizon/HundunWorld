using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UE5ToFlaxConverter.Core.Models;
using UE5ToFlaxConverter.Core.Models.FlaxAssets;

namespace UE5ToFlaxConverter.Core.Writers;

/// <summary>
/// 模型写入器：UE5 StaticMesh/SkeletalMesh → Flax 资源描述。
///
/// 输出文件（每个资源在独立目录）：
/// 1. {AssetName}.prefab.json —— FlaxEngine.Prefab 格式（FlaxAssetFile：ID/TypeName/EngineBuild/Data）
///    含 StaticModel/AnimatedModel Actor 节点，引用 .flax Model/SkinnedModel 资源
/// 2. {AssetName}.model.json —— FlaxEngine.Model/SkinnedModel 资源元数据（描述 .flax 二进制资源的目标内容）
/// 3. import-manifest.json —— FBX 导入清单（指导 Flax Editor 从 FBX 导入生成 .flax 二进制资源）
/// 4. materials-map.json —— 材质槽映射（UE5 Material → Flax Material GUID）
/// 5. skeleton-hierarchy.json —— 骨骼层级（仅 SkeletalMesh，记录骨骼名/父索引/绑定姿态）
///
/// 注意：Flax 的 .flax 二进制资源（SkinnedModel/Model/Material）无法用纯 JSON 生成，
/// 必须通过 Flax Editor 的 ModelTool.Import API 导入 FBX 源文件。
/// </summary>
public sealed class ModelWriter
{
    private readonly string _outputRoot;

    public ModelWriter(string outputRoot)
    {
        if (string.IsNullOrWhiteSpace(outputRoot))
            throw new ArgumentException("输出根目录不能为空", nameof(outputRoot));
        _outputRoot = Path.GetFullPath(outputRoot);
    }

    /// <summary>
    /// 兼容旧 API：保留双参数构造（registry 参数被忽略，因为本类不再维护全局 registry）。
    /// </summary>
    public ModelWriter(string outputRoot, FlaxAssetRegistry registry) : this(outputRoot) { }

    public async Task<WriterOutput> WriteStaticMeshAsync(IntermediateMesh mesh, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        if (string.IsNullOrWhiteSpace(mesh.AssetName))
            throw new ArgumentException("Mesh.AssetName 不能为空", nameof(mesh));

        var assetName = SanitizeAssetName(mesh.AssetName);
        var subDirRel = $"Models/{assetName}";
        var targetDir = Path.Combine(_outputRoot, "Models", assetName);
        Directory.CreateDirectory(targetDir);
        var output = new WriterOutput { TargetDirectory = targetDir };

        var prefabGuid = FlaxGuid.FromPath(mesh.SourcePath + "#prefab");
        var modelGuid = FlaxGuid.FromPath(mesh.SourcePath + "#model");

        // 1. .prefab.json（FlaxEngine.Prefab 格式，FlaxAssetFile 结构）
        var prefabPath = Path.Combine(targetDir, assetName + ".prefab.json");
        var prefab = BuildStaticModelPrefab(mesh, prefabGuid, modelGuid);
        await JsonHelper.SerializeToFileAsync(prefab, prefabPath, ct);
        output.Files.Add(new OutputFile
        {
            RelativePath = Path.GetRelativePath(_outputRoot, prefabPath),
            Kind = "Prefab",
            Format = "json",
            SizeBytes = new FileInfo(prefabPath).Length
        });

        // 2. .model.json（Model 资源元数据，描述 .flax 二进制资源的目标）
        var modelMetaPath = Path.Combine(targetDir, assetName + ".model.json");
        var modelMeta = new ModelAssetMetadata
        {
            AssetName = assetName,
            SourcePath = mesh.SourcePath,
            MeshKind = mesh.Kind.ToString(),
            FlaxAssetPath = JsonHelper.ToFlaxContentPath($"{subDirRel}/{assetName}.flax"),
            TargetGuid = modelGuid,
            TargetTypeName = "FlaxEngine.Model",
            LODs = BuildLODDescriptions(mesh),
            Bounds = new BoundsDescription(mesh.Bounds),
            Materials = BuildMaterialSlots(mesh)
        };
        await JsonHelper.SerializeToFileAsync(modelMeta, modelMetaPath, ct);
        output.Files.Add(new OutputFile
        {
            RelativePath = Path.GetRelativePath(_outputRoot, modelMetaPath),
            Kind = "Model",
            Format = "json",
            SizeBytes = new FileInfo(modelMetaPath).Length
        });

        // 3. import-manifest.json（FBX 导入清单，指导 Flax Editor 导入 FBX 生成 .flax Model）
        var importManifestPath = Path.Combine(targetDir, "import-manifest.json");
        var importManifest = new FbxImportManifest
        {
            AssetName = assetName,
            AssetType = "Model",
            SourceMeshKind = mesh.Kind.ToString(),
            SourcePath = mesh.SourcePath,
            SourceFbxPath = JsonHelper.ToFlaxContentPath($"{subDirRel}/{assetName}.fbx"),
            FlaxTargetPath = JsonHelper.ToFlaxContentPath($"{subDirRel}/{assetName}.flax"),
            TargetGuid = modelGuid,
            ImportSettings = new FbxImportSettings
            {
                Type = "Model",
                Enum = 0,
                Scale = 1,
                ImportVertexColors = false,
                ImportNormals = true,
                ImportTangents = true,
                CalculateNormals = false,
                GenerateLODs = false,
                OptimizeMeshes = false,
                MergeMeshes = false,
                ImportMaterials = false,
                ImportTextures = false
            },
            LODs = BuildLODDescriptions(mesh)
        };
        await JsonHelper.SerializeToFileAsync(importManifest, importManifestPath, ct);
        output.Files.Add(new OutputFile
        {
            RelativePath = Path.GetRelativePath(_outputRoot, importManifestPath),
            Kind = "ImportScript",
            Format = "json",
            SizeBytes = new FileInfo(importManifestPath).Length
        });

        // 4. materials-map.json（材质槽映射）
        var materialsMapPath = Path.Combine(targetDir, "materials-map.json");
        var materialsMap = new MaterialsMap
        {
            Mesh = assetName,
            Slots = BuildMaterialSlots(mesh)
        };
        await JsonHelper.SerializeToFileAsync(materialsMap, materialsMapPath, ct);
        output.Files.Add(new OutputFile
        {
            RelativePath = Path.GetRelativePath(_outputRoot, materialsMapPath),
            Kind = "MaterialMap",
            Format = "json",
            SizeBytes = new FileInfo(materialsMapPath).Length
        });

        output.PendingManualSteps.Add(
            $"使用 UE5 Editor 或 CUE4Parse-Conversion 导出 {assetName} 的 FBX 到 {JsonHelper.ToFlaxContentPath($"{subDirRel}/{assetName}.fbx")}，" +
            $"然后在 Flax Editor 中执行 UE5AssetImporter.ImportAll() 导入为 .flax Model（GUID: {modelGuid}）");

        return output;
    }

    public async Task<WriterOutput> WriteSkeletalMeshAsync(IntermediateMesh mesh, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        if (string.IsNullOrWhiteSpace(mesh.AssetName))
            throw new ArgumentException("Mesh.AssetName 不能为空", nameof(mesh));

        var assetName = SanitizeAssetName(mesh.AssetName);
        var subDirRel = $"SkinnedModels/{assetName}";
        var targetDir = Path.Combine(_outputRoot, "SkinnedModels", assetName);
        Directory.CreateDirectory(targetDir);
        var output = new WriterOutput { TargetDirectory = targetDir };

        var prefabGuid = FlaxGuid.FromPath(mesh.SourcePath + "#prefab");
        var skinnedModelGuid = FlaxGuid.FromPath(mesh.SourcePath + "#skm");
        var animGraphGuid = FlaxGuid.FromPath(mesh.SourcePath + "#animgraph");

        // 1. .prefab.json（FlaxEngine.Prefab，含 AnimatedModel Actor + SkinnedModel + AnimationGraph 引用）
        var prefabPath = Path.Combine(targetDir, assetName + ".prefab.json");
        var prefab = BuildAnimatedModelPrefab(mesh, prefabGuid, skinnedModelGuid, animGraphGuid);
        await JsonHelper.SerializeToFileAsync(prefab, prefabPath, ct);
        output.Files.Add(new OutputFile
        {
            RelativePath = Path.GetRelativePath(_outputRoot, prefabPath),
            Kind = "Prefab",
            Format = "json",
            SizeBytes = new FileInfo(prefabPath).Length
        });

        // 2. .model.json（SkinnedModel 资源元数据）
        var modelMetaPath = Path.Combine(targetDir, assetName + ".model.json");
        var modelMeta = new ModelAssetMetadata
        {
            AssetName = assetName,
            SourcePath = mesh.SourcePath,
            MeshKind = mesh.Kind.ToString(),
            FlaxAssetPath = JsonHelper.ToFlaxContentPath($"{subDirRel}/{assetName}.flax"),
            TargetGuid = skinnedModelGuid,
            TargetTypeName = "FlaxEngine.SkinnedModel",
            LODs = BuildLODDescriptions(mesh),
            Bounds = new BoundsDescription(mesh.Bounds),
            Materials = BuildMaterialSlots(mesh)
        };
        await JsonHelper.SerializeToFileAsync(modelMeta, modelMetaPath, ct);
        output.Files.Add(new OutputFile
        {
            RelativePath = Path.GetRelativePath(_outputRoot, modelMetaPath),
            Kind = "Model",
            Format = "json",
            SizeBytes = new FileInfo(modelMetaPath).Length
        });

        // 3. import-manifest.json
        var importManifestPath = Path.Combine(targetDir, "import-manifest.json");
        var importManifest = new FbxImportManifest
        {
            AssetName = assetName,
            AssetType = "SkinnedModel",
            SourceMeshKind = mesh.Kind.ToString(),
            SourcePath = mesh.SourcePath,
            SourceFbxPath = JsonHelper.ToFlaxContentPath($"{subDirRel}/{assetName}.fbx"),
            FlaxTargetPath = JsonHelper.ToFlaxContentPath($"{subDirRel}/{assetName}.flax"),
            TargetGuid = skinnedModelGuid,
            ImportSettings = new FbxImportSettings
            {
                Type = "SkinnedModel",
                Enum = 1,
                Scale = 1,
                ImportVertexColors = false,
                ImportNormals = true,
                ImportTangents = true,
                CalculateNormals = false,
                GenerateLODs = false,
                OptimizeMeshes = false,
                MergeMeshes = false,
                ImportMaterials = false,
                ImportTextures = false
            },
            LODs = BuildLODDescriptions(mesh)
        };
        await JsonHelper.SerializeToFileAsync(importManifest, importManifestPath, ct);
        output.Files.Add(new OutputFile
        {
            RelativePath = Path.GetRelativePath(_outputRoot, importManifestPath),
            Kind = "ImportScript",
            Format = "json",
            SizeBytes = new FileInfo(importManifestPath).Length
        });

        // 4. materials-map.json
        var materialsMapPath = Path.Combine(targetDir, "materials-map.json");
        var materialsMap = new MaterialsMap
        {
            Mesh = assetName,
            Slots = BuildMaterialSlots(mesh)
        };
        await JsonHelper.SerializeToFileAsync(materialsMap, materialsMapPath, ct);
        output.Files.Add(new OutputFile
        {
            RelativePath = Path.GetRelativePath(_outputRoot, materialsMapPath),
            Kind = "MaterialMap",
            Format = "json",
            SizeBytes = new FileInfo(materialsMapPath).Length
        });

        // 5. skeleton-hierarchy.json（仅 SkeletalMesh）
        if (mesh.Bones.Count > 0)
        {
            var skelPath = Path.Combine(targetDir, "skeleton-hierarchy.json");
            var skeleton = new SkeletonHierarchy
            {
                Mesh = assetName,
                BoneCount = mesh.Bones.Count,
                Bones = BuildBoneDescriptions(mesh)
            };
            await JsonHelper.SerializeToFileAsync(skeleton, skelPath, ct);
            output.Files.Add(new OutputFile
            {
                RelativePath = Path.GetRelativePath(_outputRoot, skelPath),
                Kind = "SkeletonHierarchy",
                Format = "json",
                SizeBytes = new FileInfo(skelPath).Length
            });
        }

        output.PendingManualSteps.Add(
            $"导出含骨骼的 FBX 到 {JsonHelper.ToFlaxContentPath($"{subDirRel}/{assetName}.fbx")}（UE5 中右键 Asset → Export，保留 Skeleton），" +
            $"然后在 Flax Editor 中执行 UE5AssetImporter.ImportAll() 导入为 .flax SkinnedModel（GUID: {skinnedModelGuid}）");

        return output;
    }

    // ============ Prefab 构建 ============

    /// <summary>
    /// 构建 StaticModel prefab（FlaxEngine.StaticModel Actor 节点）。
    /// 使用 FlaxAssetFile 结构（ID/TypeName/EngineBuild/Data）符合 Flax Engine 真实资源格式。
    /// </summary>
    private static FlaxAssetFile BuildStaticModelPrefab(IntermediateMesh mesh, string prefabGuid, string modelGuid)
    {
        var rootNodeId = FlaxGuid.NewGuid();
        var modelNodeId = FlaxGuid.NewGuid();
        var (tx, ty, tz) = ExtractTranslation(mesh);

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
                    Name = mesh.AssetName,
                    Transform = new FlaxTransform
                    {
                        Translation = CoordinateMapper.ToFlaxPosition(tx, ty, tz)
                    }
                },
                new()
                {
                    ID = modelNodeId,
                    TypeName = "FlaxEngine.StaticModel",
                    ParentID = rootNodeId,
                    Name = mesh.AssetName + "_Model",
                    Transform = new FlaxTransform { Translation = new FlaxVector3(0, 0, 0) },
                    ExtraFields = new Dictionary<string, object?>
                    {
                        ["Model"] = modelGuid,
                        ["Buffer"] = new
                        {
                            Entries = BuildMaterialEntries(mesh)
                        }
                    }
                }
            }
        };
    }

    /// <summary>
    /// 构建 AnimatedModel prefab（FlaxEngine.AnimatedModel Actor 节点 + SkinnedModel 引用 + AnimationGraph 占位）。
    /// </summary>
    private static FlaxAssetFile BuildAnimatedModelPrefab(
        IntermediateMesh mesh,
        string prefabGuid,
        string skinnedModelGuid,
        string animGraphGuid)
    {
        var rootNodeId = FlaxGuid.NewGuid();
        var animatedModelNodeId = FlaxGuid.NewGuid();
        var (tx, ty, tz) = ExtractTranslation(mesh);
        var materialEntries = BuildMaterialEntries(mesh);

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
                    Name = mesh.AssetName,
                    Transform = new FlaxTransform
                    {
                        Translation = CoordinateMapper.ToFlaxPosition(tx, ty, tz)
                    }
                },
                new()
                {
                    ID = animatedModelNodeId,
                    TypeName = "FlaxEngine.AnimatedModel",
                    ParentID = rootNodeId,
                    Name = mesh.AssetName + "_AnimatedModel",
                    Transform = new FlaxTransform { Translation = new FlaxVector3(0, 0, 0) },
                    ExtraFields = new Dictionary<string, object?>
                    {
                        ["SkinnedModel"] = skinnedModelGuid,
                        ["AnimationGraph"] = animGraphGuid,
                        ["ShadowsMode"] = 1,
                        ["Buffer"] = new { Entries = materialEntries }
                    }
                }
            }
        };
    }

    private static List<object> BuildMaterialEntries(IntermediateMesh mesh)
    {
        var entries = new List<object>();
        var slotCount = mesh.Materials?.Count ?? 0;
        if (slotCount == 0) slotCount = 1;
        for (int i = 0; i < slotCount; i++)
        {
            entries.Add(new
            {
                Material = FlaxAssetFile.NullGuid,
                ShadowsMode = 3,
                Visible = true,
                ReceiveDecals = true
            });
        }
        return entries;
    }

    private static List<LODDescription> BuildLODDescriptions(IntermediateMesh mesh)
    {
        var lods = new List<LODDescription>();
        foreach (var lod in mesh.LODs)
        {
            lods.Add(new LODDescription
            {
                LODIndex = lod.LODIndex,
                TriangleCount = lod.TriangleCount,
                VertexCount = lod.Positions.Count,
                ScreenSize = lod.ScreenSize,
                SectionCount = lod.Sections.Count
            });
        }
        return lods;
    }

    private static List<MaterialSlotDescription> BuildMaterialSlots(IntermediateMesh mesh)
    {
        var slots = new List<MaterialSlotDescription>();
        for (int i = 0; i < (mesh.Materials?.Count ?? 0); i++)
        {
            var mat = mesh.Materials![i];
            slots.Add(new MaterialSlotDescription
            {
                SlotIndex = mat.SlotIndex,
                MaterialName = mat.MaterialName,
                ImportedMaterialPath = mat.ImportedMaterialPath,
                FlaxMaterialGuid = FlaxAssetFile.NullGuid
            });
        }
        return slots;
    }

    private static List<BoneDescription> BuildBoneDescriptions(IntermediateMesh mesh)
    {
        var bones = new List<BoneDescription>();
        for (int i = 0; i < mesh.Bones.Count; i++)
        {
            var bone = mesh.Bones[i];
            var m = bone.BindPose;
            // 从 BindPose 4x4 矩阵提取平移分量（行主序：M41/M42/M43）
            // 并应用 UE5→Flax 坐标系映射（UE5 Z 上下 → Flax Y 上下；UE5 Y 前后 → Flax Z 前后）
            var flaxPos = CoordinateMapper.ToFlaxPosition(m.M41, m.M42, m.M43);
            bones.Add(new BoneDescription
            {
                Index = i,
                Name = bone.Name,
                ParentIndex = bone.ParentIndex,
                Translation = new[] { (float)flaxPos.X, (float)flaxPos.Y, (float)flaxPos.Z }
            });
        }
        return bones;
    }

    private static (double X, double Y, double Z) ExtractTranslation(IntermediateMesh mesh)
    {
        // 从 mesh 变换矩阵提取平移分量（暂返回原点）
        return (0, 0, 0);
    }

    private static string SanitizeAssetName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new System.Text.StringBuilder(name.Length);
        foreach (var c in name)
        {
            // 防止路径穿越：禁止 . 和 ..
            if (c == '.' || System.Array.IndexOf(invalid, c) >= 0 || c == ' ' || c == '/' || c == '\\')
                sb.Append('_');
            else
                sb.Append(c);
        }
        return sb.ToString();
    }
}

// ============ 输出数据模型（仅用于 JSON 序列化） ============

public sealed class FbxImportManifest
{
    public string AssetName { get; set; } = string.Empty;
    public string AssetType { get; set; } = string.Empty;
    public string SourceMeshKind { get; set; } = string.Empty;
    public string SourcePath { get; set; } = string.Empty;
    public string SourceFbxPath { get; set; } = string.Empty;
    public string FlaxTargetPath { get; set; } = string.Empty;
    public string TargetGuid { get; set; } = string.Empty;
    public FbxImportSettings ImportSettings { get; set; } = new();
    public List<LODDescription> LODs { get; set; } = new();
}

public sealed class FbxImportSettings
{
    public string Type { get; set; } = "Model";
    public int Enum { get; set; }
    public double Scale { get; set; } = 1;
    public bool ImportVertexColors { get; set; }
    public bool ImportNormals { get; set; } = true;
    public bool ImportTangents { get; set; } = true;
    public bool CalculateNormals { get; set; }
    public bool GenerateLODs { get; set; }
    public bool OptimizeMeshes { get; set; }
    public bool MergeMeshes { get; set; }
    public bool ImportMaterials { get; set; }
    public bool ImportTextures { get; set; }
}

public sealed class ModelAssetMetadata
{
    public string AssetName { get; set; } = string.Empty;
    public string SourcePath { get; set; } = string.Empty;
    public string MeshKind { get; set; } = string.Empty;
    public string FlaxAssetPath { get; set; } = string.Empty;
    public string TargetGuid { get; set; } = string.Empty;
    public string TargetTypeName { get; set; } = string.Empty;
    public List<LODDescription> LODs { get; set; } = new();
    public BoundsDescription? Bounds { get; set; }
    public List<MaterialSlotDescription> Materials { get; set; } = new();
}

public sealed class LODDescription
{
    public int LODIndex { get; set; }
    public int TriangleCount { get; set; }
    public int VertexCount { get; set; }
    public float ScreenSize { get; set; }
    public int SectionCount { get; set; }
}

public sealed class BoundsDescription
{
    public BoundsDescription() { }
    public BoundsDescription(BoundingBox b)
    {
        Min = new[] { b.Min.X, b.Min.Y, b.Min.Z };
        Max = new[] { b.Max.X, b.Max.Y, b.Max.Z };
        Center = new[] { b.Center.X, b.Center.Y, b.Center.Z };
        Size = new[] { b.Size.X, b.Size.Y, b.Size.Z };
    }
    public float[] Min { get; set; } = new float[3];
    public float[] Max { get; set; } = new float[3];
    public float[] Center { get; set; } = new float[3];
    public float[] Size { get; set; } = new float[3];
}

public sealed class MaterialSlotDescription
{
    public int SlotIndex { get; set; }
    public string MaterialName { get; set; } = string.Empty;
    public string? ImportedMaterialPath { get; set; }
    public string FlaxMaterialGuid { get; set; } = FlaxAssetFile.NullGuid;
}

public sealed class MaterialsMap
{
    public string Mesh { get; set; } = string.Empty;
    public List<MaterialSlotDescription> Slots { get; set; } = new();
}

public sealed class SkeletonHierarchy
{
    public string Mesh { get; set; } = string.Empty;
    public int BoneCount { get; set; }
    public List<BoneDescription> Bones { get; set; } = new();
}

public sealed class BoneDescription
{
    public int Index { get; set; }
    public string Name { get; set; } = string.Empty;
    public int ParentIndex { get; set; } = -1;
    public float[] Translation { get; set; } = new float[3];
}
