using System.Numerics;
using System.Text.Json.Serialization;

namespace UE5ToFlaxConverter.Core.Models;

/// <summary>
/// UE5 静态/骨骼网格的中间表示，与 UE5/Flax 无关。
/// </summary>
public sealed class IntermediateMesh
{
    public string SourcePath { get; set; } = string.Empty;
    public string AssetName { get; set; } = string.Empty;
    public MeshKind Kind { get; set; } = MeshKind.Static;

    public List<MeshLOD> LODs { get; set; } = new();
    public List<MeshMaterial> Materials { get; set; } = new();
    public List<MeshBone> Bones { get; set; } = new();        // 仅 Skeletal
    public List<BoneInfluence> SkinInfluences { get; set; } = new(); // 仅 Skeletal
    public List<MorphTarget> MorphTargets { get; set; } = new();      // 仅 Skeletal

    public BoundingBox Bounds { get; set; }
    public Vector3 PivotTranslation { get; set; }
}

public enum MeshKind { Static, Skeletal }

public sealed class MeshLOD
{
    public int LODIndex { get; set; }
    public List<Vector3> Positions { get; set; } = new();
    public List<Vector3> Normals { get; set; } = new();
    public List<Vector2> UV0 { get; set; } = new();
    public List<Vector2> UV1 { get; set; } = new();
    public List<Vector4> Tangents { get; set; } = new();
    public List<Color> VertexColors { get; set; } = new();
    public List<int> Indices { get; set; } = new();
    public List<MeshSection> Sections { get; set; } = new();
    public float ScreenSize { get; set; }

    /// <summary>
    /// 计算此 LOD 的三角形数（优先使用 Sections，否则按 Indices/3 估算）。
    /// </summary>
    public int TriangleCount => Sections.Count > 0
        ? Sections.Sum(s => s.NumTriangles)
        : Indices.Count / 3;
}

public sealed class MeshSection
{
    public int MaterialIndex { get; set; }
    public int FirstIndex { get; set; }
    public int NumTriangles { get; set; }
    public bool CastShadow { get; set; } = true;
    public bool Visible { get; set; } = true;
}

public sealed class MeshMaterial
{
    public int SlotIndex { get; set; }
    public string MaterialName { get; set; } = string.Empty;
    public string? MaterialSlotName { get; set; }
    public string? ImportedMaterialPath { get; set; }
    public List<MaterialTexture> Textures { get; set; } = new();
    public Color DiffuseColor { get; set; }
    public Color EmissiveColor { get; set; }
    public float Metallic { get; set; }
    public float Roughness { get; set; }
    public float Opacity { get; set; } = 1.0f;
    public BlendMode BlendMode { get; set; } = BlendMode.Opaque;
    public ShadingModel ShadingModel { get; set; } = ShadingModel.DefaultLit;
}

public sealed class MaterialTexture
{
    public string Slot { get; set; } = string.Empty; // BaseColor, Normal, ORM, Emissive, Metallic, Roughness
    public string TexturePath { get; set; } = string.Empty;
    public string? ImportedTexturePath { get; set; }
    public bool sRGB { get; set; } = true;
    public Vector2 UVScale { get; set; } = Vector2.One;
    public Vector2 UVOffset { get; set; } = Vector2.Zero;
}

public sealed class MeshBone
{
    public string Name { get; set; } = string.Empty;
    public int ParentIndex { get; set; } = -1;
    public System.Numerics.Matrix4x4 BindPose { get; set; }
}

public sealed class BoneInfluence
{
    public int VertexIndex { get; set; }
    public List<BoneWeight> Weights { get; set; } = new();
}

public sealed class BoneWeight
{
    public int BoneIndex { get; set; }
    public float Weight { get; set; }
}

public sealed class MorphTarget
{
    public string Name { get; set; } = string.Empty;
    public List<MorphDelta> Deltas { get; set; } = new();
}

public sealed class MorphDelta
{
    public int VertexIndex { get; set; }
    public Vector3 PositionDelta { get; set; }
    public Vector3 NormalDelta { get; set; }
    public Vector3 TangentDelta { get; set; }
}

public readonly record struct BoundingBox(Vector3 Min, Vector3 Max)
{
    public Vector3 Center => (Min + Max) * 0.5f;
    public Vector3 Size => Max - Min;
}

public readonly record struct Color(byte R, byte G, byte B, byte A)
{
    public static Color White => new(255, 255, 255, 255);
    public static Color Black => new(0, 0, 0, 255);
}

public enum BlendMode { Opaque, Masked, Translucent, Additive, Modulate }
public enum ShadingModel { Unlit, DefaultLit, Subsurface, Cloth, Hair, Eye }
