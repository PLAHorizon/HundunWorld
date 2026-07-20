using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UE5ToFlaxConverter.Core.Models;

namespace UE5ToFlaxConverter.Core.Writers;

/// <summary>
/// GAS 写入器。生成 HundunWorld NarrativePro/GAS 风格的 C# 代码 + JSON 配置。
/// 目标目录：Plugins/NarrativePro/Source/NarrativePro/GAS/
/// </summary>
public sealed class GasWriter
{
    private readonly string _outputRoot;

    public GasWriter(string outputRoot)
    {
        if (string.IsNullOrWhiteSpace(outputRoot))
            throw new ArgumentException("输出根目录不能为空", nameof(outputRoot));
        _outputRoot = Path.GetFullPath(outputRoot);
    }

    public async Task<WriterOutput> WriteAbilityAsync(GameplayAbility ability, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ability);
        if (string.IsNullOrWhiteSpace(ability.ClassName))
            throw new ArgumentException("Ability.ClassName 不能为空", nameof(ability));

        var output = new WriterOutput();
        var subDir = Path.Combine("GAS", "Abilities", SanitizeClassName(ability.ClassName));
        var targetDir = Path.Combine(_outputRoot, subDir);
        Directory.CreateDirectory(targetDir);
        output.TargetDirectory = targetDir;

        // 1. C# 代码文件
        var csPath = Path.Combine(targetDir, ability.ClassName + ".cs");
        await File.WriteAllTextAsync(csPath, GenerateAbilityCode(ability), ct);
        output.Files.Add(new OutputFile
        {
            RelativePath = Path.Combine(subDir, ability.ClassName + ".cs"),
            Kind = "GameplayAbility",
            Format = "cs"
        });

        // 2. JSON 配置（运行时加载）
        var jsonPath = Path.Combine(targetDir, ability.ClassName + ".json");
        var config = new
        {
            className = ability.ClassName,
            parentClass = ability.ParentClass,
            inputId = ability.InputId,
            abilityTags = ability.AbilityTags,
            cancelAbilitiesWithTag = ability.CancelAbilitiesWithTag,
            blockAbilitiesWithTag = ability.BlockAbilitiesWithTag,
            activationOwnedTags = ability.ActivationOwnedTags,
            activationRequiredTags = ability.ActivationRequiredTags,
            activationBlockedTags = ability.ActivationBlockedTags,
            cooldownEffectPath = ability.CooldownEffectPath,
            costEffectPath = ability.CostEffectPath,
            instancingPolicy = ability.InstancingPolicy,
            netExecutionPolicy = ability.NetExecutionPolicy,
            replicationPolicy = ability.ReplicationPolicy,
            triggers = ability.Triggers
        };
        await JsonHelper.SerializeToFileAsync(config, jsonPath, ct);
        output.Files.Add(new OutputFile
        {
            RelativePath = Path.Combine(subDir, ability.ClassName + ".json"),
            Kind = "GameplayAbilityConfig",
            Format = "json"
        });

        output.PendingManualSteps.Add($"将 {ability.ClassName}.cs 复制到 HundunWorld/Plugins/NarrativePro/Source/NarrativePro/GAS/Abilities/");
        output.PendingManualSteps.Add($"通过 AbilityConfiguration.cs 注册 {ability.ClassName} 到 ASC");
        return output;
    }

    public async Task<WriterOutput> WriteEffectAsync(GameplayEffect effect, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(effect);
        if (string.IsNullOrWhiteSpace(effect.ClassName))
            throw new ArgumentException("Effect.ClassName 不能为空", nameof(effect));

        var output = new WriterOutput();
        var subDir = Path.Combine("GAS", "Effects", SanitizeClassName(effect.ClassName));
        var targetDir = Path.Combine(_outputRoot, subDir);
        Directory.CreateDirectory(targetDir);
        output.TargetDirectory = targetDir;

        var csPath = Path.Combine(targetDir, effect.ClassName + ".cs");
        await File.WriteAllTextAsync(csPath, GenerateEffectCode(effect), ct);
        output.Files.Add(new OutputFile
        {
            RelativePath = Path.Combine(subDir, effect.ClassName + ".cs"),
            Kind = "GameplayEffect",
            Format = "cs"
        });

        var jsonPath = Path.Combine(targetDir, effect.ClassName + ".json");
        var config = new
        {
            className = effect.ClassName,
            parentClass = effect.ParentClass,
            durationPolicy = effect.DurationPolicy,
            durationMagnitude = effect.DurationMagnitude,
            period = effect.Period,
            stackingType = effect.StackingType,
            stackLimitCount = effect.StackLimitCount,
            modifiers = effect.Modifiers,
            grantedAbilities = effect.GrantedAbilities,
            assetTags = effect.AssetTags,
            grantedTags = effect.GrantedTags,
            sourceTags = effect.SourceTags,
            targetTags = effect.TargetTags,
            executions = effect.Executions
        };
        await JsonHelper.SerializeToFileAsync(config, jsonPath, ct);
        output.Files.Add(new OutputFile
        {
            RelativePath = Path.Combine(subDir, effect.ClassName + ".json"),
            Kind = "GameplayEffectConfig",
            Format = "json"
        });

        output.PendingManualSteps.Add($"将 {effect.ClassName}.cs 复制到 HundunWorld/Plugins/NarrativePro/Source/NarrativePro/GAS/Effects/");
        return output;
    }

    public async Task<WriterOutput> WriteAttributeSetAsync(AttributeSetDefinition set, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(set);
        if (string.IsNullOrWhiteSpace(set.ClassName))
            throw new ArgumentException("Set.ClassName 不能为空", nameof(set));

        var output = new WriterOutput();
        var subDir = Path.Combine("GAS", "AttributeSets", SanitizeClassName(set.ClassName));
        var targetDir = Path.Combine(_outputRoot, subDir);
        Directory.CreateDirectory(targetDir);
        output.TargetDirectory = targetDir;

        var csPath = Path.Combine(targetDir, set.ClassName + ".cs");
        await File.WriteAllTextAsync(csPath, GenerateAttributeSetCode(set), ct);
        output.Files.Add(new OutputFile
        {
            RelativePath = Path.Combine(subDir, set.ClassName + ".cs"),
            Kind = "AttributeSet",
            Format = "cs"
        });

        output.PendingManualSteps.Add($"将 {set.ClassName}.cs 复制到 HundunWorld/Plugins/NarrativePro/Source/NarrativePro/GAS/AttributeSets/");
        return output;
    }

    // ============ 代码生成 ============

    private string GenerateAbilityCode(GameplayAbility ability)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// 此文件由 UE5ToFlaxConverter 自动生成。");
        sb.AppendLine($"// 源资源: {ability.ParentClass}");
        sb.AppendLine("// 生成时间: " + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") + " UTC");
        sb.AppendLine();
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using NarrativePro.GAS;");
        sb.AppendLine("using NarrativePro.Items; // GameplayTag/GameplayTagContainer");
        sb.AppendLine();
        sb.AppendLine("namespace NarrativePro.GAS.Abilities");
        sb.AppendLine("{");
        sb.AppendLine($"    /// <summary>");
        sb.AppendLine($"    /// UE5 转换的 GameplayAbility：{ability.ClassName}");
        sb.AppendLine($"    /// </summary>");
        sb.AppendLine($"    public class {ability.ClassName} : NarrativeGameplayAbility");
        sb.AppendLine("    {");
        sb.AppendLine($"        public const string AbilityInputId = \"{EscapeString(ability.InputId)}\";");
        sb.AppendLine();
        sb.AppendLine($"        public {ability.ClassName}()");
        sb.AppendLine("        {");

        if (ability.AbilityTags.Count > 0)
        {
            sb.AppendLine("            AbilityTags = new GameplayTagContainer(new[]");
            sb.AppendLine("            {");
            foreach (var tag in ability.AbilityTags)
                sb.AppendLine($"                new GameplayTag(\"{EscapeString(tag)}\"),");
            sb.AppendLine("            });");
        }
        if (ability.CancelAbilitiesWithTag.Count > 0)
        {
            sb.AppendLine("            CancelAbilitiesWithTag = new GameplayTagContainer(new[]");
            sb.AppendLine("            {");
            foreach (var tag in ability.CancelAbilitiesWithTag)
                sb.AppendLine($"                new GameplayTag(\"{EscapeString(tag)}\"),");
            sb.AppendLine("            });");
        }
        if (ability.BlockAbilitiesWithTag.Count > 0)
        {
            sb.AppendLine("            BlockAbilitiesWithTag = new GameplayTagContainer(new[]");
            sb.AppendLine("            {");
            foreach (var tag in ability.BlockAbilitiesWithTag)
                sb.AppendLine($"                new GameplayTag(\"{EscapeString(tag)}\"),");
            sb.AppendLine("            });");
        }

        sb.AppendLine($"            InstancingPolicy = AbilityInstancingPolicy.{ability.InstancingPolicy};");
        sb.AppendLine($"            NetExecutionPolicy = AbilityNetExecutionPolicy.{ability.NetExecutionPolicy};");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        public override void ActivateAbility(AbilitySystemComponent asc)");
        sb.AppendLine("        {");
        sb.AppendLine("            base.ActivateAbility(asc);");
        sb.AppendLine("            // TODO: 从 UE5 蓝图逻辑迁移技能执行代码");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        public override void EndAbility(AbilitySystemComponent asc)");
        sb.AppendLine("        {");
        sb.AppendLine("            base.EndAbility(asc);");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private string GenerateEffectCode(GameplayEffect effect)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// 此文件由 UE5ToFlaxConverter 自动生成。");
        sb.AppendLine($"// 源资源: {effect.ParentClass}");
        sb.AppendLine("// 生成时间: " + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") + " UTC");
        sb.AppendLine();
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using NarrativePro.GAS;");
        sb.AppendLine("using NarrativePro.Items;");
        sb.AppendLine();
        sb.AppendLine("namespace NarrativePro.GAS.Effects");
        sb.AppendLine("{");
        sb.AppendLine($"    /// <summary>");
        sb.AppendLine($"    /// UE5 转换的 GameplayEffect：{effect.ClassName}");
        sb.AppendLine($"    /// </summary>");
        sb.AppendLine($"    public class {effect.ClassName} : GameplayEffect");
        sb.AppendLine("    {");
        sb.AppendLine($"        public {effect.ClassName}()");
        sb.AppendLine("        {");
        sb.AppendLine($"            DurationPolicy = EffectDurationPolicy.{effect.DurationPolicy};");

        if (effect.DurationPolicy == "HasDuration")
            sb.AppendLine($"            DurationMagnitude = {effect.DurationMagnitude:G}f;");

        if (effect.Period > 0)
            sb.AppendLine($"            Period = {effect.Period:G}f;");

        sb.AppendLine($"            StackingType = EffectStackingType.{effect.StackingType};");
        sb.AppendLine($"            StackLimitCount = {effect.StackLimitCount};");

        if (effect.AssetTags.Count > 0)
        {
            sb.AppendLine("            AssetTags = new GameplayTagContainer(new[]");
            sb.AppendLine("            {");
            foreach (var t in effect.AssetTags) sb.AppendLine($"                new GameplayTag(\"{EscapeString(t)}\"),");
            sb.AppendLine("            });");
        }
        if (effect.GrantedTags.Count > 0)
        {
            sb.AppendLine("            GrantedTags = new GameplayTagContainer(new[]");
            sb.AppendLine("            {");
            foreach (var t in effect.GrantedTags) sb.AppendLine($"                new GameplayTag(\"{EscapeString(t)}\"),");
            sb.AppendLine("            });");
        }

        if (effect.Modifiers.Count > 0)
        {
            sb.AppendLine("            Modifiers = new List<EffectModifier>");
            sb.AppendLine("            {");
            foreach (var m in effect.Modifiers)
            {
                sb.AppendLine($"                new EffectModifier {{ Attribute = \"{EscapeString(m.Attribute)}\", ModifierOp = ModifierOpType.{m.ModifierOp}, Magnitude = {m.Magnitude:G}f }},");
            }
            sb.AppendLine("            };");
        }
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private string GenerateAttributeSetCode(AttributeSetDefinition set)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// 此文件由 UE5ToFlaxConverter 自动生成。");
        sb.AppendLine($"// 源资源: {set.ParentClass}");
        sb.AppendLine("// 生成时间: " + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") + " UTC");
        sb.AppendLine();
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using NarrativePro.GAS;");
        sb.AppendLine();
        sb.AppendLine("namespace NarrativePro.GAS.AttributeSets");
        sb.AppendLine("{");
        sb.AppendLine($"    /// <summary>");
        sb.AppendLine($"    /// UE5 转换的 AttributeSet：{set.ClassName}");
        sb.AppendLine($"    /// </summary>");
        sb.AppendLine($"    public class {set.ClassName} : NarrativeAttributeSetBase");
        sb.AppendLine("    {");

        foreach (var attr in set.Attributes)
        {
            sb.AppendLine($"        public float {attr.Name} {{ get; set; }} = {attr.DefaultBaseValue:G}f;");
            sb.AppendLine($"        public float Max{attr.Name} {{ get; set; }} = {attr.DefaultCurrentValue:G}f;");
            sb.AppendLine();
        }

        sb.AppendLine("        public override void Initialize()");
        sb.AppendLine("        {");
        sb.AppendLine("            base.Initialize();");
        foreach (var attr in set.Attributes)
        {
            sb.AppendLine($"            RegisterAttribute(\"{EscapeString(attr.Name)}\", {attr.Name}, Max{attr.Name});");
        }
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    /// <summary>
    /// 转义 C# 字符串字面量中的特殊字符（防止注入与编译错误）。
    /// </summary>
    private static string EscapeString(string? s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n");
    }

    /// <summary>
    /// 规范化类名以安全用于目录路径（移除非法字符）。
    /// </summary>
    private static string SanitizeClassName(string name)
    {
        if (string.IsNullOrEmpty(name)) return "Unnamed";
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
            sb.Append(invalid.Contains(c) ? '_' : c);
        return sb.ToString();
    }
}