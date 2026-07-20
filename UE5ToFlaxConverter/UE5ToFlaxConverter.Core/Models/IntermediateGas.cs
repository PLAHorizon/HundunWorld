namespace UE5ToFlaxConverter.Core.Models;

/// <summary>
/// UE5 GAS（GameplayAbilitySystem）资源中间表示。
/// 输出目标：HundunWorld Plugins/NarrativePro/GAS 下的 C# 代码 + JSON 配置。
/// </summary>
public sealed class IntermediateGAS
{
    public string SourcePath { get; set; } = string.Empty;
    public string AssetName { get; set; } = string.Empty;
    public GASKind Kind { get; set; } = GASKind.Ability;

    public GameplayAbility? Ability { get; set; }
    public GameplayEffect? Effect { get; set; }
    public AttributeSetDefinition? AttributeSet { get; set; }
}

public enum GASKind { Ability, Effect, AttributeSet, CurveTable }

public sealed class GameplayAbility
{
    public string ClassName { get; set; } = string.Empty;
    public string? ParentClass { get; set; } // UE5 蓝图父类，如 NarrativeCombatAbility
    public string InputId { get; set; } = string.Empty;
    public List<string> AbilityTags { get; set; } = new();
    public List<string> CancelAbilitiesWithTag { get; set; } = new();
    public List<string> BlockAbilitiesWithTag { get; set; } = new();
    public List<string> ActivationOwnedTags { get; set; } = new();
    public List<string> ActivationRequiredTags { get; set; } = new();
    public List<string> ActivationBlockedTags { get; set; } = new();
    public string? CooldownEffectPath { get; set; }
    public string? CostEffectPath { get; set; }
    public string InstancingPolicy { get; set; } = "InstancedPerActor";
    public string NetExecutionPolicy { get; set; } = "LocalPredicted";
    public string ReplicationPolicy { get; set; } = "Minimal";
    public List<AbilityTrigger> Triggers { get; set; } = new();
    public Dictionary<string, object?> DefaultProperties { get; set; } = new();
}

public sealed class AbilityTrigger
{
    public string TriggerTag { get; set; } = string.Empty;
    public string TriggerSource { get; set; } = "GameplayEvent"; // Event, Tag, Custom
    public string? TriggerSourceTag { get; set; }
}

public sealed class GameplayEffect
{
    public string ClassName { get; set; } = string.Empty;
    public string? ParentClass { get; set; }
    public string DurationPolicy { get; set; } = "Instant"; // Instant, HasDuration, Infinite
    public float DurationMagnitude { get; set; }
    public float Period { get; set; }
    public string StackingType { get; set; } = "None";
    public int StackLimitCount { get; set; } = 1;
    public List<EffectModifier> Modifiers { get; set; } = new();
    public List<string> GrantedAbilities { get; set; } = new();
    public List<string> AssetTags { get; set; } = new();
    public List<string> GrantedTags { get; set; } = new();
    public List<string> SourceTags { get; set; } = new();
    public List<string> TargetTags { get; set; } = new();
    public List<EffectExecution> Executions { get; set; } = new();
    public Dictionary<string, object?> DefaultProperties { get; set; } = new();
}

public sealed class EffectModifier
{
    public string Attribute { get; set; } = string.Empty; // 如 "Health"
    public string ModifierOp { get; set; } = "Add"; // Add, Multiply, Divide, Override
    public float Magnitude { get; set; }
    public string MagnitudeType { get; set; } = "ScalableFloat"; // ScalableFloat, AttributeBased, CustomCalculationClass
    public string? ScalableFloatCurve { get; set; } // CurveTable 路径
    public string? CustomCalculationClass { get; set; }
}

public sealed class EffectExecution
{
    public string CalculationClass { get; set; } = string.Empty;
    public List<string> CalculationTags { get; set; } = new();
}

public sealed class AttributeSetDefinition
{
    public string ClassName { get; set; } = string.Empty;
    public string? ParentClass { get; set; }
    public List<AttributeDefinition> Attributes { get; set; } = new();
}

public sealed class AttributeDefinition
{
    public string Name { get; set; } = string.Empty; // Health
    public string BaseValueTypeName { get; set; } = "float";
    public float DefaultBaseValue { get; set; }
    public float DefaultCurrentValue { get; set; }
    public List<string> MetaTags { get; set; } = new(); // 如 "Health.Min=0"
}
