namespace UE5ToFlaxConverter.Core.Models;

/// <summary>
/// 资源类型枚举（用于 Pipeline 调度）。
/// </summary>
public enum AssetType
{
    Unknown,
    StaticMesh,
    SkeletalMesh,
    AnimationSequence,
    AnimationMontage,
    BlendSpace,
    AnimationBlueprint,
    Material,
    MaterialInstance,
    Texture2D,
    NiagaraSystem,
    NiagaraEmitter,
    CascadeParticleSystem,
    GameplayAbility,
    GameplayEffect,
    AttributeSet,
    CurveTable,
    DataTable,
    Blueprint,
    Other
}

/// <summary>
/// UE5 资源扫描结果。
/// </summary>
public sealed class AssetScanResult
{
    public required string SourcePath { get; set; }
    public required string AssetName { get; set; }
    public required AssetType Type { get; set; }
    public string? UEClass { get; set; }
    public long FileSizeBytes { get; set; }
    public DateTime LastModified { get; set; }
    public bool IsSelected { get; set; } = true;
}
