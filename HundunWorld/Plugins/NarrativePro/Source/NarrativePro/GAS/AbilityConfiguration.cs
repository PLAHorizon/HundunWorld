using System;
using System.Collections.Generic;

namespace NarrativePro.GAS
{
    /// <summary>
    /// 能力配置资产。对应 UE5 UAbilityConfiguration。
    /// 包含默认属性、启动效果、默认能力列表。
    /// 简化点：
    /// - TSubclassOf&lt;UGameplayEffect&gt; 替换为字符串路径
    /// - TSubclassOf&lt;UNarrativeGameplayAbility&gt; 替换为字符串路径
    /// </summary>
    [Serializable]
    public class AbilityConfiguration
    {
        /// <summary>默认属性初始化效果路径（Instant 类型，设置 BaseValue）。</summary>
        public string DefaultAttributesEffectPath = "";

        /// <summary>启动时一次性应用的效果路径列表。</summary>
        public List<string> StartupEffectPaths = new List<string>();

        /// <summary>默认授予的能力路径列表。</summary>
        public List<string> DefaultAbilityPaths = new List<string>();

        public AbilityConfiguration() { }

        /// <summary>
        /// 从 JSON 文本解析能力配置（对应 UE5 从磁盘加载 <c>UAbilityConfiguration</c> 资产）。
        /// 口径与 <see cref="NarrativeGASAssetRegistry"/> 一致：System.Text.Json、属性名大小写不敏感、
        /// 允许注释与尾逗号、字段参与序列化（本类的成员是公开字段而非属性）。
        /// 解析失败返回 null 并写警告，不抛异常。
        /// </summary>
        public static AbilityConfiguration LoadFromJson(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                return System.Text.Json.JsonSerializer.Deserialize<AbilityConfiguration>(json, _jsonOptions);
            }
            catch (Exception ex)
            {
                NarrativePro.Core.NarrativeLog.LogWarning($"[GAS] 解析 AbilityConfiguration JSON 失败: {ex.Message}");
                return null;
            }
        }

        /// <summary>从磁盘路径加载能力配置（绝对/相对工程路径均可，缺省扩展名补 <c>.json</c>）。</summary>
        public static AbilityConfiguration LoadFromFile(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            try
            {
                string resolved = path.Replace('\\', '/');
                if (!System.IO.File.Exists(resolved) && !resolved.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    string withExtension = resolved + ".json";
                    if (System.IO.File.Exists(withExtension)) resolved = withExtension;
                }
                if (!System.IO.File.Exists(resolved))
                {
                    NarrativePro.Core.NarrativeLog.LogWarning($"[GAS] 能力配置文件不存在: {path}");
                    return null;
                }
                return LoadFromJson(System.IO.File.ReadAllText(resolved));
            }
            catch (Exception ex)
            {
                NarrativePro.Core.NarrativeLog.LogWarning($"[GAS] 加载能力配置 '{path}' 失败: {ex.Message}");
                return null;
            }
        }

        private static readonly System.Text.Json.JsonSerializerOptions _jsonOptions = new System.Text.Json.JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            IncludeFields = true
        };
    }
}
