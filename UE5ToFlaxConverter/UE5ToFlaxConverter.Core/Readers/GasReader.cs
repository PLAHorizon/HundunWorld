using Microsoft.Extensions.Logging;
using UE5ToFlaxConverter.Core.Mappers;
using UE5ToFlaxConverter.Core.Models;
using UObject = CUE4Parse.UE4.Assets.Exports.UObject;

namespace UE5ToFlaxConverter.Core.Readers;

/// <summary>
/// UE5 GAS 资源读取器。读取 GameplayAbility / GameplayEffect / AttributeSet。
/// 使用 CUE4Parse 反射式 GetOrDefault&lt;T&gt; 读取所有序列化字段。
/// </summary>
public sealed class GasReader
{
    private readonly UassetProvider _provider;
    private readonly GameplayTagMapper _tagMapper;
    private readonly ILogger<GasReader>? _logger;

    public GasReader(UassetProvider provider, GameplayTagMapper tagMapper, ILogger<GasReader>? logger = null)
    {
        _provider = provider;
        _tagMapper = tagMapper;
        _logger = logger;
    }

    public IntermediateGAS Read(string assetPath)
    {
        // 资源名优先从路径提取（避免 obj.Name 为 BlueprintGeneratedClass 等辅助对象名）
        var assetName = System.IO.Path.GetFileNameWithoutExtension(assetPath);

        UObject obj;
        string className;
        try
        {
            // GAS 蓝图的主对象可能是 BlueprintGeneratedClass / GameplayAbility / GameplayEffect / AttributeSet
            // 优先按 ExportType 精确匹配，否则使用 LoadObject 自动推断
            var allExports = _provider.LoadAllObjects(assetPath);
            UObject? matched = null;
            string matchedType = string.Empty;

            // 优先匹配具体 GAS 类型
            foreach (var ex in allExports)
            {
                var et = ex.ExportType ?? string.Empty;
                if (et == "GameplayAbility" || et == "GameplayEffect" || et == "AttributeSet")
                {
                    matched = ex; matchedType = et; break;
                }
            }
            // 退化1：BlueprintGeneratedClass（蓝图资源编译后的主对象）
            if (matched == null)
            {
                foreach (var ex in allExports)
                {
                    var et = ex.ExportType ?? string.Empty;
                    if (et == "BlueprintGeneratedClass" || et == "AnimBlueprintGeneratedClass")
                    {
                        matched = ex; matchedType = et; break;
                    }
                }
            }
            // 退化2：自动推断（跳过辅助类型）
            obj = matched ?? _provider.LoadObject(assetPath);
            className = !string.IsNullOrEmpty(matchedType) ? matchedType
                       : (obj.ExportType ?? ReflectionHelper.GetClassName(obj));
        }
        catch (Exception ex)
        {
            _logger?.LogWarning("加载 GAS 失败: {Path} -> {Msg}", assetPath, ex.Message);
            return new IntermediateGAS
            {
                SourcePath = assetPath,
                AssetName = assetName,
                Kind = GASKind.Ability
            };
        }
        _logger?.LogInformation("读取 GAS: {Name} ({Class})", assetName, className);

        var gas = new IntermediateGAS
        {
            SourcePath = assetPath,
            AssetName = assetName
        };

        // 优先根据 CUE4Parse 类名识别
        if (className.Contains("Ability", StringComparison.OrdinalIgnoreCase))
        {
            gas.Kind = GASKind.Ability;
            gas.Ability = ReadAbility(obj, assetName);
        }
        else if (className.Contains("Effect", StringComparison.OrdinalIgnoreCase))
        {
            gas.Kind = GASKind.Effect;
            gas.Effect = ReadEffect(obj, assetName);
        }
        else if (className.Contains("AttributeSet", StringComparison.OrdinalIgnoreCase))
        {
            gas.Kind = GASKind.AttributeSet;
            gas.AttributeSet = ReadAttributeSet(obj, assetName);
        }
        else
        {
            // 后备：根据 UE5 资源命名约定（GA_/GE_/AS_）推断类型
            // GA_ 蓝图加载后主对象通常是 BlueprintGeneratedClass，类名不含 "Ability"
            var upperName = assetName.ToUpperInvariant();
            if (upperName.StartsWith("GA_") || upperName.Contains("ABILITY"))
            {
                gas.Kind = GASKind.Ability;
                gas.Ability = ReadAbility(obj, assetName);
            }
            else if (upperName.StartsWith("GE_") || upperName.Contains("EFFECT"))
            {
                gas.Kind = GASKind.Effect;
                gas.Effect = ReadEffect(obj, assetName);
            }
            else if (upperName.StartsWith("AS_") || upperName.Contains("ATTRIBUTESET"))
            {
                gas.Kind = GASKind.AttributeSet;
                gas.AttributeSet = ReadAttributeSet(obj, assetName);
            }
            else
            {
                _logger?.LogWarning("未知 GAS 类: {Class}（{Name}）", className, assetName);
            }
        }

        return gas;
    }

    private GameplayAbility ReadAbility(UObject obj, string assetName)
    {
        var ability = new GameplayAbility
        {
            ClassName = _tagMapper.NormalizeClassName(assetName),
            ParentClass = ReflectionHelper.GetClassName(obj)
        };

        ability.AbilityTags = ReadTagContainer(obj, "AbilityTags");
        ability.CancelAbilitiesWithTag = ReadTagContainer(obj, "CancelAbilitiesWithTag");
        ability.BlockAbilitiesWithTag = ReadTagContainer(obj, "BlockAbilitiesWithTag");
        ability.ActivationOwnedTags = ReadTagContainer(obj, "ActivationOwnedTags");
        ability.ActivationRequiredTags = ReadTagContainer(obj, "ActivationRequiredTags");
        ability.ActivationBlockedTags = ReadTagContainer(obj, "ActivationBlockedTags");

        var cdge = obj.GetOrDefault<object>("CooldownGameplayEffectClass");
        if (cdge != null) ability.CooldownEffectPath = ExtractSoftObjectPath(cdge);
        var cost = obj.GetOrDefault<object>("CostGameplayEffectClass");
        if (cost != null) ability.CostEffectPath = ExtractSoftObjectPath(cost);

        ability.InstancingPolicy = ReadEnum(obj, "InstancingPolicy", "InstancedPerActor");
        ability.NetExecutionPolicy = ReadEnum(obj, "NetExecutionPolicy", "LocalPredicted");
        ability.ReplicationPolicy = ReadEnum(obj, "ReplicationPolicy", "Minimal");
        ability.InputId = obj.GetOrDefault<int>("InputID").ToString();

        var triggers = obj.GetOrDefault<object[]>("AbilityTriggers");
        if (triggers != null)
        {
            foreach (var t in triggers)
            {
                var tag = ReflectionHelper.GetMember(t, "TriggerTag")?.ToString() ?? string.Empty;
                var source = ReflectionHelper.GetMember(t, "TriggerSource")?.ToString() ?? "GameplayEvent";
                var srcTag = ReflectionHelper.GetMember(t, "TriggerSourceTag")?.ToString();
                ability.Triggers.Add(new AbilityTrigger
                {
                    TriggerTag = _tagMapper.Map(tag),
                    TriggerSource = source,
                    TriggerSourceTag = srcTag
                });
            }
        }

        return ability;
    }

    private GameplayEffect ReadEffect(UObject obj, string assetName)
    {
        var effect = new GameplayEffect
        {
            ClassName = _tagMapper.NormalizeClassName(assetName),
            ParentClass = ReflectionHelper.GetClassName(obj)
        };

        effect.DurationPolicy = ReadEnum(obj, "DurationPolicy", "Instant");
        effect.DurationMagnitude = obj.GetOrDefault<float>("DurationMagnitude");
        effect.Period = obj.GetOrDefault<float>("Period");
        effect.StackingType = ReadEnum(obj, "StackingType", "None");
        effect.StackLimitCount = obj.GetOrDefault<int>("StackLimitCount", 1);

        var modifiers = obj.GetOrDefault<object[]>("Modifiers");
        if (modifiers != null)
        {
            foreach (var m in modifiers)
            {
                var attr = ReflectionHelper.GetMember(m, "Attribute");
                var attrName = attr != null
                    ? (ReflectionHelper.GetMember(attr, "AttributeName")?.ToString()
                       ?? ReflectionHelper.GetMember(attr, "Name")?.ToString()
                       ?? "Unknown")
                    : "Unknown";
                var op = ReadEnum(m, "ModifierOp", "Add");
                var magnitude = ReflectionHelper.GetMember(m, "ModifierMagnitude");
                var magValue = magnitude != null ? ReflectionHelper.GetSingle(magnitude, "Value") : 0;

                effect.Modifiers.Add(new EffectModifier
                {
                    Attribute = attrName,
                    ModifierOp = op,
                    Magnitude = magValue,
                    MagnitudeType = "ScalableFloat"
                });
            }
        }

        effect.AssetTags = ReadTagContainer(obj, "AssetTags");
        effect.GrantedTags = ReadTagContainer(obj, "InheritableOwnedTagsContainer.GrantedTags");

        var execs = obj.GetOrDefault<object[]>("Executions");
        if (execs != null)
        {
            foreach (var e in execs)
            {
                var calcClass = ReflectionHelper.GetMember(e, "CalculationClass")?.ToString() ?? string.Empty;
                effect.Executions.Add(new EffectExecution { CalculationClass = calcClass });
            }
        }

        return effect;
    }

    private AttributeSetDefinition ReadAttributeSet(UObject obj, string assetName)
    {
        var set = new AttributeSetDefinition
        {
            ClassName = _tagMapper.NormalizeClassName(assetName),
            ParentClass = ReflectionHelper.GetClassName(obj)
        };

        // AttributeSet 通过 Properties 枚举所有 FGameplayAttributeData 字段。
        // CUE4Parse 的 FPropertyTag 含 Name 字段（类型 FName，有 PlainText 字符串），
        // 因此直接访问 .Name.PlainText 而非反射调用 GetMember。
        // 这里同时保留两种回退路径以兼容不同 CUE4Parse 版本。
        foreach (var prop in obj.Properties)
        {
            if (prop == null) continue;

            // 路径1：CUE4Parse 标准 FPropertyTag.Name.PlainText
            string name = TryGetPropertyName(prop);

            // 路径2：退化到反射查找 Tag/Name 字段
            if (string.IsNullOrEmpty(name))
                name = ReflectionHelper.GetMember(prop, "Name")?.ToString() ?? string.Empty;

            // 仅识别 GameplayAttributeData 字段（名称以 Attribute 结尾或类型标记为 FGameplayAttributeData）
            if (IsAttributeProperty(prop, name))
            {
                set.Attributes.Add(new AttributeDefinition
                {
                    Name = string.IsNullOrEmpty(name) ? $"Attribute_{set.Attributes.Count}" : name,
                    DefaultBaseValue = ReadAttributeDefaultValue(prop)
                });
            }
        }

        return set;
    }

    /// <summary>
    /// 尝试从 FPropertyTag 中提取属性名（兼容多个 CUE4Parse 版本）。
    /// </summary>
    private static string TryGetPropertyName(object prop)
    {
        try
        {
            // CUE4Parse 的 FPropertyTag.Name 为 FName，PlainText 即字符串值。
            var nameField = ReflectionHelper.GetMember(prop, "Name");
            if (nameField == null) return string.Empty;
            var plainText = ReflectionHelper.GetMember(nameField, "PlainText")?.ToString();
            if (!string.IsNullOrEmpty(plainText)) return plainText;
            // 部分版本直接暴露 Name 字符串
            return nameField.ToString() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static bool IsAttributeProperty(object prop, string name)
    {
        if (name.EndsWith("Attribute", StringComparison.OrdinalIgnoreCase))
            return true;

        // 检查 Tag 类型字符串是否含 GameplayAttributeData
        var tag = ReflectionHelper.GetMember(prop, "Tag")?.ToString() ?? string.Empty;
        var propertyType = ReflectionHelper.GetMember(prop, "PropertyType")?.ToString() ?? string.Empty;
        return tag.Contains("GameplayAttribute", StringComparison.OrdinalIgnoreCase)
            || propertyType.Contains("GameplayAttribute", StringComparison.OrdinalIgnoreCase);
    }

    private static float ReadAttributeDefaultValue(object prop)
    {
        // FPropertyTag 的 Value/PropertyValue 可能含 BaseValue 字段
        var value = ReflectionHelper.GetMember(prop, "Value")
                 ?? ReflectionHelper.GetMember(prop, "PropertyValue");
        if (value == null) return 0;
        return ReflectionHelper.GetSingle(value, "BaseValue");
    }

    // ============ 辅助方法 ============

    private List<string> ReadTagContainer(UObject obj, string fieldName)
    {
        var container = obj.GetOrDefault<object>(fieldName);
        if (container == null) return new List<string>();

        // 支持嵌套属性路径，如 "InheritableOwnedTagsContainer.GrantedTags"
        var tags = ResolveNestedMember(container, fieldName) as System.Collections.IEnumerable
                   ?? ReflectionHelper.GetEnumerableMember(container, "GameplayTags");
        var result = new List<string>();
        if (tags != null)
        {
            foreach (var tag in tags)
            {
                var tagName = ReflectionHelper.GetMember(tag, "TagName")?.ToString() ?? string.Empty;
                if (!string.IsNullOrEmpty(tagName))
                    result.Add(_tagMapper.Map(tagName));
            }
        }
        return result;
    }

    /// <summary>
    /// 支持 a.b.c 路径形式的成员访问。
    /// </summary>
    private static object? ResolveNestedMember(object? root, string path)
    {
        if (root == null || string.IsNullOrEmpty(path)) return null;
        var current = root;
        // 第一段已由调用方解析，从第二段开始
        var parts = path.Split('.', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 1; i < parts.Length; i++)
        {
            current = ReflectionHelper.GetMember(current, parts[i]);
            if (current == null) return null;
        }
        return current;
    }

    private static string ReadEnum(object obj, string fieldName, string defaultValue)
    {
        var value = obj is UObject uo ? uo.GetOrDefault<object>(fieldName) : ReflectionHelper.GetMember(obj, fieldName);
        if (value == null) return defaultValue;
        var str = value.ToString();
        if (string.IsNullOrEmpty(str)) return defaultValue;
        var idx = str.LastIndexOf(':');
        return idx >= 0 ? str[(idx + 1)..] : str;
    }

    private static string? ExtractSoftObjectPath(object? softObject)
    {
        if (softObject == null) return null;
        var assetPath = ReflectionHelper.GetMember(softObject, "AssetPathName");
        return assetPath?.ToString();
    }
}
