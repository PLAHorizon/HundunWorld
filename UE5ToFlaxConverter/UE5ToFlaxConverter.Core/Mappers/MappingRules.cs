using System.Text.Json;
using UE5ToFlaxConverter.Core.Models;

namespace UE5ToFlaxConverter.Core.Mappers;

/// <summary>
/// 加载和访问 default-mapping.json 的封装。
/// 提供 Profile 不存在时的友好降级（返回 null 而非抛异常）。
/// </summary>
public sealed class MappingRules
{
    public MappingRulesData Data { get; }

    private MappingRules(MappingRulesData data) { Data = data; }

    public static MappingRules Load(string? path = null)
    {
        path ??= Path.Combine(AppContext.BaseDirectory, "Rules", "default-mapping.json");
        if (!File.Exists(path))
            throw new FileNotFoundException($"映射规则文件未找到: {path}");
        var json = File.ReadAllText(path);
        var data = JsonSerializer.Deserialize<MappingRulesData>(json, JsonOptions.Cached)
            ?? throw new InvalidDataException("映射规则文件解析失败");
        return new MappingRules(data);
    }

    public string ResolveType(string? ueType) =>
        !string.IsNullOrEmpty(ueType) && Data.TypeMapping.TryGetValue(ueType, out var t) ? t : "object";

    public string ResolveBlendMode(string? ueBlendMode) =>
        !string.IsNullOrEmpty(ueBlendMode) && Data.BlendModeMapping.TryGetValue(ueBlendMode, out var m) ? m : "Opaque";

    public string ResolveShadingModel(string? ueShadingModel) =>
        !string.IsNullOrEmpty(ueShadingModel) && Data.ShadingModelMapping.TryGetValue(ueShadingModel, out var m) ? m : "DefaultLit";

    public string ResolveMaterialProperty(string ueProp) =>
        Data.MaterialPropertyMapping.TryGetValue(ueProp, out var p) ? p : ueProp;

    public string ResolveParticleModule(string ueModuleClass) =>
        Data.ParticleModuleMapping.TryGetValue(ueModuleClass, out var m) ? m : ueModuleClass;

    /// <summary>
    /// 获取指定 Profile。不存在时抛出 KeyNotFoundException。
    /// </summary>
    public OutputProfile GetProfile(string name) =>
        Data.OutputProfiles.TryGetValue(name, out var p)
            ? p
            : throw new KeyNotFoundException($"输出 Profile '{name}' 未定义");

    /// <summary>
    /// 尝试获取指定 Profile。不存在时返回 null 而非抛异常，便于降级处理。
    /// </summary>
    public OutputProfile? TryGetProfile(string name) =>
        Data.OutputProfiles.TryGetValue(name, out var p) ? p : null;
}

internal static class JsonOptions
{
    public static readonly JsonSerializerOptions Cached = new()
    {
        PropertyNameCaseInsensitive = true
    };
}

public sealed class MappingRulesData
{
    public string Version { get; set; } = "0.1.0";
    public string Description { get; set; } = string.Empty;

    public Dictionary<string, string> TypeMapping { get; set; } = new();
    public CoordinateSystemConfig CoordinateSystem { get; set; } = new();
    public Dictionary<string, string> MaterialPropertyMapping { get; set; } = new();
    public Dictionary<string, string> BlendModeMapping { get; set; } = new();
    public Dictionary<string, string> ShadingModelMapping { get; set; } = new();
    public GasPolicyConfig GasPolicy { get; set; } = new();
    public Dictionary<string, string> ParticleModuleMapping { get; set; } = new();
    public Dictionary<string, OutputProfile> OutputProfiles { get; set; } = new();
    public List<string> GameplayTagRoots { get; set; } = new();
}

public sealed class CoordinateSystemConfig
{
    public bool Ue5ZUp { get; set; } = true;
    public bool FlaxYUp { get; set; } = true;
    public string Note { get; set; } = string.Empty;
}

public sealed class GasPolicyConfig
{
    public Dictionary<string, string> InstancingPolicy { get; set; } = new();
    public Dictionary<string, string> NetExecutionPolicy { get; set; } = new();
    public Dictionary<string, string> DurationPolicy { get; set; } = new();
    public Dictionary<string, string> ModifierOp { get; set; } = new();
}

public sealed class OutputProfile
{
    public string Description { get; set; } = string.Empty;
    public string RootPath { get; set; } = string.Empty;
    public string DirectoryLayout { get; set; } = "MirrorSource"; // MirrorSource | ByAssetType
    public bool GenerateReport { get; set; } = true;
    public bool GenerateImportScript { get; set; } = true;
    public bool BackupExisting { get; set; } = false;
    public string? BackupFolder { get; set; }
}
