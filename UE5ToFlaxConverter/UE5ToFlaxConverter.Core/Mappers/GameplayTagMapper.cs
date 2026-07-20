using System.Text.RegularExpressions;
using UE5ToFlaxConverter.Core.Models;

namespace UE5ToFlaxConverter.Core.Mappers;

/// <summary>
/// UE5 GameplayTag 字符串到 HundunWorld 项目命名约定的映射器。
/// 项目 GameplayTag 根命名空间：Ability. / State. / Event. / Effect. / Item. / Character. / Dialogue. / Quest.
/// </summary>
public sealed class GameplayTagMapper
{
    // 按 key 长度降序排列，避免 Ability. 匹配前先被 Ability.Cost. 等更短前缀命中。
    // 实际使用时按需重构为静态预排序，但保持当前实例字段以支持自定义重写。
    private readonly List<KeyValuePair<string, string>> _sortedPrefixRewrites;

    public GameplayTagMapper(Dictionary<string, string>? customPrefixRewrites = null)
    {
        var src = customPrefixRewrites ?? new Dictionary<string, string>();
        _sortedPrefixRewrites = src
            .OrderByDescending(kv => kv.Key.Length)
            .ToList();
    }

    /// <summary>
    /// 将 UE5 GameplayTag 字符串映射为 HundunWorld 项目命名。
    /// 默认行为：保留原标签字符串（GameplayTag 已是层级命名空间）。
    /// 若有自定义前缀重写规则，按规则替换。
    /// </summary>
    public string Map(string? ueTag)
    {
        if (string.IsNullOrWhiteSpace(ueTag)) return string.Empty;
        foreach (var (uePrefix, flaxPrefix) in _sortedPrefixRewrites)
        {
            if (ueTag.StartsWith(uePrefix, StringComparison.OrdinalIgnoreCase))
                return flaxPrefix + ueTag[uePrefix.Length..];
        }
        return ueTag;
    }

    public List<string> MapAll(IEnumerable<string> ueTags) =>
        ueTags.Select(Map).Where(t => !string.IsNullOrEmpty(t)).ToList();

    /// <summary>
    /// 将 UE5 蓝图类名（如 BP_HeroAbility_Fireball_C）规范化为 C# 类名。
    /// </summary>
    public string NormalizeClassName(string? ueClassName)
    {
        if (string.IsNullOrWhiteSpace(ueClassName)) return "Unknown";
        var name = ueClassName;
        // 去掉蓝图后缀 _C
        if (name.EndsWith("_C", StringComparison.OrdinalIgnoreCase))
            name = name[..^2];
        // 去掉常见前缀 BP_/SK_/SM_
        name = Regex.Replace(name, @"^(BP|SK|SM|GA|GE|AS|DT|WBP)_", string.Empty);
        // 将非法 C# 标识符字符替换为下划线
        name = Regex.Replace(name, @"[^A-Za-z0-9_]", "_");
        // 处理空字符串：当原字符串仅由 _C 等组成时，上面替换可能为空。
        if (string.IsNullOrEmpty(name)) return "Unnamed";
        // 确保首字符是字母或下划线
        if (char.IsDigit(name[0])) name = "_" + name;
        return name;
    }

    /// <summary>
    /// 根据 GAS 资源父类路径推断 HundunWorld 项目对应 C# 基类。
    /// </summary>
    public string ResolveAbilityBaseClass(string? ueParentPath)
    {
        if (string.IsNullOrEmpty(ueParentPath)) return "NarrativeGameplayAbility";
        var upper = ueParentPath.Replace("\\", "/").ToUpperInvariant();
        if (upper.Contains("NARRATIVECOMBATABILITY")) return "NarrativeCombatAbility";
        if (upper.Contains("NARRATIVEINTERACTABILITY")) return "NarrativeInteractAbility";
        if (upper.Contains("NARRATIVEGAMEPLAYABILITY")) return "NarrativeGameplayAbility";
        if (upper.Contains("GAMEPLAYABILITY")) return "NarrativeGameplayAbility";
        return "NarrativeGameplayAbility";
    }

    public string ResolveEffectBaseClass(string? ueParentPath)
    {
        if (string.IsNullOrEmpty(ueParentPath)) return "GameplayEffect";
        var upper = ueParentPath.Replace("\\", "/").ToUpperInvariant();
        if (upper.Contains("NARRATIVE") && upper.Contains("EFFECT")) return "GameplayEffect";
        return "GameplayEffect";
    }

    public string ResolveAttributeSetBaseClass(string? ueParentPath)
    {
        if (string.IsNullOrEmpty(ueParentPath)) return "NarrativeAttributeSetBase";
        return "NarrativeAttributeSetBase";
    }
}