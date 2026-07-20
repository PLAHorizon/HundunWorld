using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace UE5ToFlaxConverter.Core.Models.FlaxAssets;

/// <summary>
/// Flax Engine 资源文件顶层包装（适用于 .prefab / .scene / .json）。
/// 对应真实 Flax 资源文件结构：{ ID, TypeName, EngineBuild, Data }。
/// </summary>
public sealed class FlaxAssetFile
{
    /// <summary>资源 GUID（32 位十六进制字符串，无连字符）。</summary>
    [JsonPropertyName("ID")]
    public string ID { get; set; } = string.Empty;

    /// <summary>资源类型全名（如 FlaxEngine.Prefab / FlaxEngine.SceneAsset / FlaxEngine.PhysicalMaterial）。</summary>
    [JsonPropertyName("TypeName")]
    public string TypeName { get; set; } = string.Empty;

    /// <summary>Flax 引擎构建号（项目当前使用 6705 ~ 6913）。</summary>
    [JsonPropertyName("EngineBuild")]
    public int EngineBuild { get; set; } = 6705;

    /// <summary>资源数据（Prefab/Scene 为节点数组，JsonAsset 为对象）。</summary>
    [JsonPropertyName("Data")]
    public object? Data { get; set; }

    public const string NullGuid = "00000000000000000000000000000000";
}

/// <summary>
/// Prefab/Scene 中的节点（Actor 或 Script）。
/// </summary>
public sealed class FlaxNode
{
    [JsonPropertyName("ID")]
    public string ID { get; set; } = string.Empty;

    [JsonPropertyName("TypeName")]
    public string TypeName { get; set; } = string.Empty;

    [JsonPropertyName("ParentID")]
    public string ParentID { get; set; } = FlaxAssetFile.NullGuid;

    [JsonPropertyName("Name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; set; }

    [JsonPropertyName("Transform")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public FlaxTransform? Transform { get; set; }

    [JsonPropertyName("StaticFlags")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int StaticFlags { get; set; }

    [JsonPropertyName("Tag")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Tag { get; set; }

    /// <summary>
    /// Script 节点的字段值容器（仅自定义 Script 使用，Actor 直接平铺字段）。
    /// </summary>
    [JsonPropertyName("V")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, object?>? Values { get; set; }

    /// <summary>
    /// Actor 特有字段（直接平铺在节点对象中，如 SkinnedModel/AnimationGraph/Material 等）。
    /// 键为字段名，值为字段值（GUID 引用、数值、字符串等）。
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, object?>? ExtraFields { get; set; }
}

/// <summary>
/// Flax 变换（Translation 三轴 + Orientation 四元数 + Scale 三轴）。
/// 坐标系：UE5 Z(上下) → Flax Y(上下)；UE5 Y(前后) → Flax Z(前后)。
/// </summary>
public sealed class FlaxTransform
{
    [JsonPropertyName("Translation")]
    public FlaxVector3 Translation { get; set; }

    [JsonPropertyName("Orientation")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public FlaxQuaternion? Orientation { get; set; }

    [JsonPropertyName("Scale")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public FlaxVector3? Scale { get; set; }
}

public sealed class FlaxVector3
{
    [JsonPropertyName("X")]
    public double X { get; set; }

    [JsonPropertyName("Y")]
    public double Y { get; set; }

    [JsonPropertyName("Z")]
    public double Z { get; set; }

    public FlaxVector3() { }
    public FlaxVector3(double x, double y, double z) { X = x; Y = y; Z = z; }
}

public sealed class FlaxQuaternion
{
    [JsonPropertyName("X")]
    public double X { get; set; }

    [JsonPropertyName("Y")]
    public double Y { get; set; }

    [JsonPropertyName("Z")]
    public double Z { get; set; }

    [JsonPropertyName("W")]
    public double W { get; set; } = 1.0;

    public FlaxQuaternion() { }
    public FlaxQuaternion(double x, double y, double z, double w) { X = x; Y = y; Z = z; W = w; }
}
