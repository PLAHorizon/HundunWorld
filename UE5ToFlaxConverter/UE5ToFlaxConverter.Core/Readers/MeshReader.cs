using System.Numerics;
using Microsoft.Extensions.Logging;
using UE5ToFlaxConverter.Core.Models;
using UObject = CUE4Parse.UE4.Assets.Exports.UObject;

namespace UE5ToFlaxConverter.Core.Readers;

/// <summary>
/// UE5 静态/骨骼网格读取器。
/// 关键修复：USkeletalMesh.Skeleton 字段为 FSoftObjectPath（软引用），
/// 必须通过 UassetProvider.LoadObject 显式加载 USkeleton 资源，
/// 否则反射访问 ReferenceSkeleton.FinalRefBoneInfo 将返回 null（导致 boneCount=0）。
/// </summary>
public sealed class MeshReader
{
    private readonly UassetProvider _provider;
    private readonly ILogger<MeshReader>? _logger;

    public MeshReader(UassetProvider provider, ILogger<MeshReader>? logger = null)
    {
        _provider = provider;
        _logger = logger;
    }

    public IntermediateMesh ReadStaticMesh(string assetPath)
    {
        var obj = _provider.LoadObject(assetPath, "StaticMesh");
        _logger?.LogInformation("读取 StaticMesh: {Name} (ExportType={ExportType})", obj.Name, obj.ExportType);

        var mesh = new IntermediateMesh
        {
            SourcePath = assetPath,
            AssetName = obj.Name,
            Kind = MeshKind.Static
        };

        // StaticMesh.RenderData.LODs[]
        var renderData = obj.GetOrDefault<object>("RenderData");
        if (renderData != null)
        {
            var lods = ReflectionHelper.GetEnumerableMember(renderData, "LODs");
            if (lods != null)
            {
                int lodIndex = 0;
                foreach (var lod in lods)
                    mesh.LODs.Add(ExtractLOD(lod, lodIndex++));
            }
        }

        ExtractMaterials(obj, mesh);
        UpdateBounds(mesh);
        return mesh;
    }

    public IntermediateMesh ReadSkeletalMesh(string assetPath)
    {
        var obj = _provider.LoadObject(assetPath, "SkeletalMesh");
        _logger?.LogInformation("读取 SkeletalMesh: {Name} (ExportType={ExportType})", obj.Name, obj.ExportType);

        var mesh = new IntermediateMesh
        {
            SourcePath = assetPath,
            AssetName = obj.Name,
            Kind = MeshKind.Skeletal
        };

        // 关键修复：Skeleton 字段为 FSoftObjectPath（软引用），需通过 AssetPathName 显式加载
        LoadSkeletonBones(obj, mesh);

        // LODs：USkinnedAsset.SkinnedAssetRenderData.LODs[]
        var importData = obj.GetOrDefault<object>("SkinnedAssetRenderData")
                      ?? obj.GetOrDefault<object>("RenderData");
        if (importData != null)
        {
            var lods = ReflectionHelper.GetEnumerableMember(importData, "LODs");
            if (lods != null)
            {
                int lodIndex = 0;
                foreach (var lod in lods)
                    mesh.LODs.Add(ExtractLOD(lod, lodIndex++));
            }
        }

        ExtractMaterials(obj, mesh);
        UpdateBounds(mesh);
        return mesh;
    }

    /// <summary>
    /// 通过 Skeleton 软引用加载 USkeleton，从其 ReferenceSkeleton 提取骨骼层级与绑定姿态。
    /// 多重回退路径：
    ///   1) Skeleton.AssetPathName → UassetProvider.LoadObject → ReferenceSkeleton.FinalRefBoneInfo/FinalRefBonePose
    ///   2) SkinnedAssetImportData.RefBones（FBX 导入数据中的原始骨骼信息）
    /// </summary>
    private void LoadSkeletonBones(UObject skelMeshObj, IntermediateMesh mesh)
    {
        // 路径1：通过 Skeleton 软引用加载 USkeleton
        var skeletonSoftRef = skelMeshObj.GetOrDefault<object>("Skeleton");
        if (skeletonSoftRef != null)
        {
            var assetPathName = ReflectionHelper.GetMember(skeletonSoftRef, "AssetPathName")?.ToString();
            if (!string.IsNullOrEmpty(assetPathName))
            {
                try
                {
                    var skeletonObj = _provider.LoadObject(assetPathName);
                    ExtractBonesFromSkeleton(skeletonObj, mesh);
                    _logger?.LogInformation("已加载 Skeleton: {Path} (骨骼数: {Count})", assetPathName, mesh.Bones.Count);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning("加载 Skeleton 失败: {Path} -> {Msg}", assetPathName, ex.Message);
                }
            }
        }

        // 路径2：退化到 SkinnedAssetImportData.RefBones
        if (mesh.Bones.Count == 0)
        {
            var importData = skelMeshObj.GetOrDefault<object>("SkinnedAssetImportData");
            if (importData != null)
            {
                var refBones = ReflectionHelper.GetEnumerableMember(importData, "RefBones");
                if (refBones != null)
                {
                    int i = 0;
                    foreach (var bone in refBones)
                    {
                        var name = ReflectionHelper.GetMember(bone, "Name")?.ToString() ?? $"Bone_{i}";
                        var parentIndex = ReflectionHelper.GetInt32(bone, "ParentIndex", -1);
                        var bonePos = ReflectionHelper.GetMember(bone, "BonePos");
                        mesh.Bones.Add(new MeshBone
                        {
                            Name = name,
                            ParentIndex = parentIndex,
                            BindPose = ExtractMatrix(bonePos)
                        });
                        i++;
                    }
                }
            }
        }
    }

    /// <summary>
    /// 从 USkeleton.ReferenceSkeleton 提取 FinalRefBoneInfo + FinalRefBonePose。
    /// </summary>
    private static void ExtractBonesFromSkeleton(UObject skeletonObj, IntermediateMesh mesh)
    {
        var refSkel = ReflectionHelper.GetMember(skeletonObj, "ReferenceSkeleton");
        if (refSkel == null)
        {
            // 部分版本字段名为 RefSkel
            refSkel = ReflectionHelper.GetMember(skeletonObj, "RefSkel");
        }
        if (refSkel == null) return;

        var boneInfo = ReflectionHelper.GetEnumerableMember(refSkel, "FinalRefBoneInfo");
        var bonePose = ReflectionHelper.GetEnumerableMember(refSkel, "FinalRefBonePose");
        if (boneInfo == null || bonePose == null) return;

        var poseEnumerator = bonePose.GetEnumerator();
        int i = 0;
        foreach (var info in boneInfo)
        {
            var name = ReflectionHelper.GetMember(info, "Name")?.ToString() ?? $"Bone_{i}";
            var parent = ReflectionHelper.GetMember(info, "ParentIndex");
            poseEnumerator.MoveNext();
            mesh.Bones.Add(new MeshBone
            {
                Name = name,
                ParentIndex = parent is int p ? p : -1,
                BindPose = ExtractMatrix(poseEnumerator.Current)
            });
            i++;
        }
    }

    private MeshLOD ExtractLOD(object lod, int lodIndex)
    {
        var result = new MeshLOD { LODIndex = lodIndex };

        // 顶点：FStaticMeshRenderLOD.VertexBuffers.PositionVertexBuffer.Vertices[]
        // 或 FSkeletalMeshRenderLOD.VertexBuffers.StaticVertexBuffers.PositionVertexBuffer.Vertices[]
        var vertices = ReflectionHelper.GetEnumerableMember(lod, "Vertices");
        if (vertices != null)
        {
            foreach (var v in vertices)
            {
                result.Positions.Add(ExtractVector3(v, "Position"));
                result.Normals.Add(ExtractVector3(v, "Normal"));
                result.Tangents.Add(ExtractVector4(v, "Tangent"));
                var uv0 = ReflectionHelper.GetMember(v, "UV0");
                if (uv0 != null) result.UV0.Add(ExtractVector2(uv0));
                var uv1 = ReflectionHelper.GetMember(v, "UV1");
                if (uv1 != null) result.UV1.Add(ExtractVector2(uv1));
            }
        }

        // IndexBuffer.Indices
        var indexBuffer = ReflectionHelper.GetMember(lod, "IndexBuffer");
        if (indexBuffer != null)
        {
            var indices = ReflectionHelper.GetEnumerableMember(indexBuffer, "Indices");
            if (indices != null)
            {
                foreach (var idx in indices)
                {
                    if (idx is int ii) result.Indices.Add(ii);
                    else if (idx is uint u) result.Indices.Add((int)u);
                    else if (idx is ushort us) result.Indices.Add(us);
                }
            }
        }

        var ss = ReflectionHelper.GetMember(lod, "ScreenSize");
        if (ss is float ssf) result.ScreenSize = ssf;

        var sections = ReflectionHelper.GetEnumerableMember(lod, "Sections");
        if (sections != null)
        {
            foreach (var sec in sections)
            {
                result.Sections.Add(new MeshSection
                {
                    MaterialIndex = ReflectionHelper.GetInt32(sec, "MaterialIndex", 0),
                    FirstIndex = ReflectionHelper.GetInt32(sec, "FirstIndex", 0),
                    NumTriangles = ReflectionHelper.GetInt32(sec, "NumTriangles", 0)
                });
            }
        }

        _logger?.LogDebug("LOD{Index}: {Verts} 顶点, {Indices} 索引, {Sections} 段",
            lodIndex, result.Positions.Count, result.Indices.Count, result.Sections.Count);
        return result;
    }

    private void ExtractMaterials(UObject obj, IntermediateMesh mesh)
    {
        // 优先使用 StaticMaterials（FStaticMaterial[]，含 MaterialSlot/UVChannelData 等元数据）
        // 退化到 Materials（ResolvedObject[] 或 FPackageIndex[]，仅含材质引用）
        var mats = obj.GetOrDefault<object[]>("StaticMaterials");
        if (mats == null)
        {
            // 退化到 Materials 字段
            var rawMats = obj.GetOrDefault<object[]>("Materials");
            if (rawMats != null)
            {
                for (int i = 0; i < rawMats.Length; i++)
                {
                    var matName = ExtractObjectName(rawMats[i]) ?? $"Material_{i}";
                    mesh.Materials.Add(new MeshMaterial
                    {
                        SlotIndex = i,
                        MaterialName = matName
                    });
                }
            }
            return;
        }

        for (int i = 0; i < mats.Length; i++)
        {
            var mat = mats[i];
            // FStaticMaterial.MaterialInterface / Material
            var matInterface = ReflectionHelper.GetMember(mat, "MaterialInterface")
                            ?? ReflectionHelper.GetMember(mat, "Material");
            var matName = ExtractObjectName(matInterface) ?? $"Material_{i}";
            // FStaticMaterial.MaterialSlotName
            var slotName = ReflectionHelper.GetMember(mat, "MaterialSlotName")?.ToString();
            mesh.Materials.Add(new MeshMaterial
            {
                SlotIndex = i,
                MaterialName = matName,
                MaterialSlotName = slotName ?? matName
            });
        }
    }

    private static void UpdateBounds(IntermediateMesh mesh)
    {
        if (mesh.LODs.Count == 0 || mesh.LODs[0].Positions.Count == 0) return;
        var first = mesh.LODs[0];
        var min = first.Positions[0];
        var max = first.Positions[0];
        foreach (var p in first.Positions)
        {
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
        }
        mesh.Bounds = new BoundingBox(min, max);
    }

    // ============ 反射辅助（基于 ReflectionHelper 实现） ============

    private static Vector3 ExtractVector3(object? obj, string fieldName)
    {
        var v = ReflectionHelper.GetMember(obj, fieldName);
        if (v == null) return Vector3.Zero;
        return new Vector3(
            ReflectionHelper.GetSingle(v, "X"),
            ReflectionHelper.GetSingle(v, "Y"),
            ReflectionHelper.GetSingle(v, "Z"));
    }

    private static Vector4 ExtractVector4(object? obj, string fieldName)
    {
        var v = ReflectionHelper.GetMember(obj, fieldName);
        if (v == null) return Vector4.Zero;
        return new Vector4(
            ReflectionHelper.GetSingle(v, "X"),
            ReflectionHelper.GetSingle(v, "Y"),
            ReflectionHelper.GetSingle(v, "Z"),
            ReflectionHelper.GetSingle(v, "W", 1f));
    }

    private static Vector2 ExtractVector2(object? v)
    {
        if (v == null) return Vector2.Zero;
        return new Vector2(
            ReflectionHelper.GetSingle(v, "X"),
            ReflectionHelper.GetSingle(v, "Y"));
    }

    private static Matrix4x4 ExtractMatrix(object? transform)
    {
        if (transform == null) return Matrix4x4.Identity;
        var rot = ReflectionHelper.GetMember(transform, "Rotation");
        var trans = ReflectionHelper.GetMember(transform, "Translation");
        var scale = ReflectionHelper.GetMember(transform, "Scale3D");

        var q = new Quaternion(
            ReflectionHelper.GetSingle(rot, "X"),
            ReflectionHelper.GetSingle(rot, "Y"),
            ReflectionHelper.GetSingle(rot, "Z"),
            ReflectionHelper.GetSingle(rot, "W", 1f));
        var t = new Vector3(
            ReflectionHelper.GetSingle(trans, "X"),
            ReflectionHelper.GetSingle(trans, "Y"),
            ReflectionHelper.GetSingle(trans, "Z"));
        var s = new Vector3(
            ReflectionHelper.GetSingle(scale, "X", 1f),
            ReflectionHelper.GetSingle(scale, "Y", 1f),
            ReflectionHelper.GetSingle(scale, "Z", 1f));
        return Matrix4x4.CreateScale(s) * Matrix4x4.CreateFromQuaternion(q) * Matrix4x4.CreateTranslation(t);
    }

    /// <summary>
    /// 从一个 UObject/ResolvingObject 引用中提取其 Name 字段，避免 ToString 返回类名而非资源名。
    /// </summary>
    private static string? ExtractObjectName(object? obj)
    {
        if (obj == null) return null;
        var name = ReflectionHelper.GetMember(obj, "Name")?.ToString();
        if (!string.IsNullOrEmpty(name)) return name;
        var alt = ReflectionHelper.GetMember(obj, "AssetPathName")?.ToString()
               ?? ReflectionHelper.GetMember(obj, "ObjectName")?.ToString();
        return string.IsNullOrEmpty(alt) ? obj.ToString() : alt;
    }
}
