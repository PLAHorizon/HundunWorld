namespace UE5ToFlaxConverter.Core.Mappers;

/// <summary>
/// UE5 到 Flax Engine 的类型映射表（编译期常量部分）。
/// 基于项目 project_memory.md 中已积累的映射规则。
/// </summary>
public static class TypeMap
{
    // ============ C++ / Blueprint 类型映射 ============
    public static readonly Dictionary<string, string> UClassToFlaxType = new()
    {
        // Actor
        ["AActor"] = "FlaxEngine.Actor",
        ["APawn"] = "FlaxEngine.Actor",
        ["ACharacter"] = "FlaxEngine.Actor",
        ["APlayerController"] = "FlaxEngine.PlayerController? (placeholder)",
        ["APlayerCameraManager"] = "FlaxEngine.Camera",
        ["AGameMode"] = "FlaxEngine.Scene (placeholder)",
        ["AGameState"] = "FlaxEngine.Scene (placeholder)",
        ["APlayerState"] = "Script (placeholder)",
        ["AHUD"] = "Script (no Flax equivalent, see project memory)",

        // Component
        ["UActorComponent"] = "FlaxEngine.Actor",
        ["USceneComponent"] = "FlaxEngine.Actor",
        ["UStaticMeshComponent"] = "FlaxEngine.StaticModel",
        ["USkeletalMeshComponent"] = "FlaxEngine.AnimatedModel",
        ["UParticleSystemComponent"] = "FlaxEngine.ParticleEffect",
        ["UNiagaraComponent"] = "FlaxEngine.ParticleEffect",
        ["UCapsuleComponent"] = "FlaxEngine.Collider (Capsule)",
        ["UBoxComponent"] = "FlaxEngine.BoxCollider",
        ["USphereComponent"] = "FlaxEngine.SphereCollider",
        ["UCameraComponent"] = "FlaxEngine.Camera",
        ["USpringArmComponent"] = "Script (placeholder)",

        // Asset
        ["UStaticMesh"] = "FlaxEngine.Model",
        ["USkeletalMesh"] = "FlaxEngine.SkinnedModel",
        ["UAnimSequence"] = "FlaxEngine.Animation",
        ["UAnimMontage"] = "FlaxEngine.Animation + JsonAsset",
        ["UAnimBlueprint"] = "FlaxEngine.AnimGraph (placeholder)",
        ["UBlendSpace"] = "FlaxEngine.AnimGraph (placeholder)",
        ["UMaterial"] = "FlaxEngine.Material",
        ["UMaterialInstance"] = "FlaxEngine.MaterialInstance",
        ["UTexture2D"] = "FlaxEngine.Texture",
        ["UNiagaraSystem"] = "FlaxEngine.ParticleSystem",
        ["UNiagaraEmitter"] = "FlaxEngine.ParticleEmitter",
        ["UParticleSystem"] = "FlaxEngine.ParticleSystem (legacy)",
        ["UParticleModule"] = "FlaxEngine.ParticleModule (mapped)",

        // GAS
        ["UGameplayAbility"] = "NarrativeGameplayAbility",
        ["UGameplayEffect"] = "GameplayEffect",
        ["UAttributeSet"] = "NarrativeAttributeSetBase",
        ["UAbilitySystemComponent"] = "NarrativeAbilitySystemComponent",

        // Math
        ["FVector"] = "System.Numerics.Vector3",
        ["FVector2D"] = "System.Numerics.Vector2",
        ["FVector4"] = "System.Numerics.Vector4",
        ["FQuat"] = "System.Numerics.Quaternion",
        ["FRotator"] = "FlaxEngine.Quaternion (Euler→Quaternion)",
        ["FTransform"] = "FlaxEngine.Transform",
        ["FMatrix"] = "System.Numerics.Matrix4x4",
        ["FColor"] = "FlaxEngine.Color",
        ["FLinearColor"] = "FlaxEngine.Color",
        ["FGuid"] = "System.Guid",
        ["FName"] = "System.String",
        ["FString"] = "System.String",
        ["FText"] = "System.String",

        // Container
        ["TArray"] = "System.Collections.Generic.List<>",
        ["TSet"] = "System.Collections.Generic.HashSet<>",
        ["TMap"] = "System.Collections.Generic.Dictionary<>",
        ["TSubclassOf"] = "System.Type / string (type name)",

        // Save
        ["USaveGame"] = "INarrativeSaveStateProvider"
    };

    // ============ 坐标系映射 ============
    // HundunWorld 项目约定：UE5 Z(上下) → Flax Y(上下)；UE5 Y(前后) → Flax Z(前后)
    public static System.Numerics.Vector3 UeToFVector(System.Numerics.Vector3 v) =>
        new(v.X, v.Z, v.Y);

    public static System.Numerics.Quaternion UeToFQuat(System.Numerics.Quaternion q) =>
        new(q.X, q.Z, q.Y, -q.W); // UE 右手 → Flax 左手近似

    // ============ Material 属性映射 ============
    public static readonly Dictionary<string, string> UeMaterialPropertyToFlax = new()
    {
        ["BaseColor"] = "BaseColor",
        ["Metallic"] = "Metalness",
        ["Specular"] = "Specular",
        ["Roughness"] = "Roughness",
        ["EmissiveColor"] = "Emissive",
        ["Normal"] = "Normal",
        ["Opacity"] = "Opacity",
        ["OpacityMask"] = "OpacityMask",
        ["AmbientOcclusion"] = "AmbientOcclusion",
        ["WorldPositionOffset"] = "WorldPositionOffset",
        ["SubsurfaceColor"] = "SubsurfaceColor",
        ["Tangent"] = "Tangent"
    };

    public static readonly Dictionary<string, string> UeBlendModeToFlax = new()
    {
        ["BLEND_Opaque"] = "Opaque",
        ["BLEND_Masked"] = "Masked",
        ["BLEND_Translucent"] = "Transparent",
        ["BLEND_Additive"] = "Additive",
        ["BLEND_Modulate"] = "Modulate",
        ["BLEND_AlphaComposite"] = "Alpha"
    };

    public static readonly Dictionary<string, string> UeShadingModelToFlax = new()
    {
        ["MSM_Unlit"] = "Unlit",
        ["MSM_DefaultLit"] = "DefaultLit",
        ["MSM_Subsurface"] = "Subsurface",
        ["MSM_PreintegratedSkin"] = "Subsurface",
        ["MSM_SubsurfaceProfile"] = "Subsurface",
        ["MSM_ClearCoat"] = "DefaultLit",
        ["MSM_Cloth"] = "Cloth",
        ["MSM_Hair"] = "Hair",
        ["MSM_Eye"] = "Eye"
    };

    // ============ GAS 标签前缀映射（基于项目 GameplayTags 命名约定） ============
    public static readonly Dictionary<string, string> GameplayTagPrefixMap = new()
    {
        ["Ability."] = "Ability.",
        ["Ability.Cooldown."] = "Ability.Cooldown.",
        ["Ability.Cost."] = "Ability.Cost.",
        ["State."] = "State.",
        ["Event."] = "Event.",
        ["Effect."] = "Effect."
    };

    // ============ GAS InstancingPolicy 映射 ============
    public static readonly Dictionary<string, string> InstancingPolicyMap = new()
    {
        ["NonInstanced"] = "NonInstanced",
        ["InstancedPerActor"] = "InstancedPerActor",
        ["InstancedPerExecution"] = "InstancedPerExecution"
    };

    public static readonly Dictionary<string, string> NetExecutionPolicyMap = new()
    {
        ["LocalPredicted"] = "LocalPredicted",
        ["ServerInitiated"] = "ServerInitiated",
        ["ServerOnly"] = "ServerOnly"
    };

    public static readonly Dictionary<string, string> DurationPolicyMap = new()
    {
        ["Instant"] = "Instant",
        ["HasDuration"] = "HasDuration",
        ["Infinite"] = "Infinite"
    };

    public static readonly Dictionary<string, string> ModifierOpMap = new()
    {
        ["Add"] = "Add",
        ["Multiply"] = "Multiply",
        ["Divide"] = "Divide",
        ["Override"] = "Override"
    };

    public static string Resolve(string ueType, string defaultValue = "object")
    {
        if (string.IsNullOrEmpty(ueType)) return defaultValue;
        return UClassToFlaxType.TryGetValue(ueType, out var flaxType) ? flaxType : defaultValue;
    }
}
