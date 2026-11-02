using System;
using FlaxEngine;
using NarrativePro.Core;

namespace NarrativePro.CommonUI
{
    /// <summary>
    /// Narrative UI 开发者设置。对应 UE5 UNarrativeUIDeveloperSettings（继承 UDeveloperSettings）。
    /// 全局 UI 主题（主色 / 反色 / 反色强调）的唯一来源，
    /// CommonUI 的所有可主题化控件在构建时都会向这里取色，而不是用设计器里设的值。
    /// <para>
    /// UE5 中为 UCLASS(config=Engine, defaultconfig)；Flax 没有 DeveloperSettings 机制，
    /// 因此用静态单例 + 可选 JSON 资产覆盖（<see cref="SettingsAssetPath"/>）实现同样的"一处配置、全局生效"。
    /// </para>
    /// </summary>
    [Serializable]
    public class NarrativeUIDeveloperSettings
    {
        /// <summary>
        /// UI 元素的主色调。对应 UE5 UIPrimaryColor。
        /// 默认值 FLinearColor(0.000607, 0.672443, 0.168269, 1.000000)。
        /// </summary>
        public Color UIPrimaryColor = new Color(0.000607f, 0.672443f, 0.168269f, 1.000000f);

        /// <summary>
        /// UI 元素的反色调。对应 UE5 UIInvertColor。
        /// 默认值 FLinearColor(0.000000, 0.009721, 0.059511, 1.000000)。
        /// </summary>
        public Color UIInvertColor = new Color(0.000000f, 0.009721f, 0.059511f, 1.000000f);

        /// <summary>
        /// UI 元素的反色强调色。对应 UE5 UIInvertAccentColor。
        /// 默认值 FLinearColor(0.000000, 0.058326, 0.178533, 1.000000)。
        /// </summary>
        public Color UIInvertAccentColor = new Color(0.000000f, 0.058326f, 0.178533f, 1.000000f);

        /// <summary>
        /// 可选的 JSON 覆盖资产路径（等价 UE5 的 config 文件）。
        /// 为空则只使用构造函数默认值。
        /// </summary>
        public string SettingsAssetPath = "Content/GUI/NarrativeUISettings";

        /// <summary>
        /// 主色是否偏亮。UE5 用主色作为前景色，因此需要按亮度决定叠在它上面的文字该用黑还是白。
        /// </summary>
        public bool bUseLightTextOnPrimary => GetLuminance(UIPrimaryColor) > 0.5f;

        /// <summary>
        /// 颜色亮度（Rec.601 加权和：0.299R + 0.587G + 0.114B）。
        /// 对应 UE5 <c>FLinearColor::Luminance</c> —— Flax 的 Color 没有该属性，这里自行计算。
        /// 主题消费方（文字配色取舍、对比度判定）统一走这里，保证判据一致。
        /// </summary>
        public static float GetLuminance(Color color)
        {
            return 0.299f * color.R + 0.587f * color.G + 0.114f * color.B;
        }

        /// <summary>单例实例。</summary>
        public static NarrativeUIDeveloperSettings Instance { get; private set; } = LoadDefault();

        /// <summary>
        /// 对应 UE5 的 GetMutableDefault&lt;UNarrativeUIDeveloperSettings&gt;()。
        /// 主题消费方（AnalogSlider / SpinBox / ComboBox / InputKeySelector 等）统一走这里取设置，
        /// 保证运行时改色能立刻被下一次控件重建看到。
        /// </summary>
        public static NarrativeUIDeveloperSettings GetMutableDefault()
        {
            if (Instance == null) Instance = LoadDefault();
            return Instance;
        }

        /// <summary>替换全局设置并让所有已构建的控件在下次重建时应用新主题。</summary>
        public static void SetInstance(NarrativeUIDeveloperSettings settings)
        {
            Instance = settings ?? LoadDefault();
        }

        /// <summary>构造函数。对应 UE5 UNarrativeUIDeveloperSettings 构造函数的默认颜色值。</summary>
        public NarrativeUIDeveloperSettings()
        {
            // 默认颜色值已在字段初始化器中设置，与 UE5 构造函数一致。
        }

        /// <summary>
        /// 从 JSON 资产读取三项颜色覆盖（存在且字段齐全时生效），否则返回构造函数默认值。
        /// 对应 UE5 的 config=Engine 加载。
        /// </summary>
        public static NarrativeUIDeveloperSettings LoadDefault()
        {
            var settings = new NarrativeUIDeveloperSettings();

            if (string.IsNullOrEmpty(settings.SettingsAssetPath))
            {
                return settings;
            }

            try
            {
                var json = Content.Load<JsonAsset>(settings.SettingsAssetPath);
                // Flax 的 JsonAsset 用 JsonAssetBase.Data 暴露原始 JSON 文本（没有 Text 属性）
                if (json != null && ApplyJson(settings, json.Data))
                {
                    NarrativeLog.Log("NarrativeUIDeveloperSettings 已从 JSON 资产加载：" + settings.SettingsAssetPath);
                    return settings;
                }
            }
            catch
            {
                // 资产不存在或格式不符，回落到默认值（与 UE5 defaultconfig 的行为一致）
            }

            NarrativeLog.Log("NarrativeUIDeveloperSettings 已使用默认值初始化。");
            return settings;
        }

        /// <summary>
        /// 解析 JSON 中的 UIPrimaryColor / UIInvertColor / UIInvertAccentColor
        /// （支持 "R,G,B,A" 字符串与 [R,G,B,A] 数组两种写法）。
        /// </summary>
        private static bool ApplyJson(NarrativeUIDeveloperSettings settings, string json)
        {
            if (string.IsNullOrEmpty(json)) return false;

            bool bAny = false;
            if (TryReadColor(json, "UIPrimaryColor", out var primary)) { settings.UIPrimaryColor = primary; bAny = true; }
            if (TryReadColor(json, "UIInvertColor", out var invert)) { settings.UIInvertColor = invert; bAny = true; }
            if (TryReadColor(json, "UIInvertAccentColor", out var accent)) { settings.UIInvertAccentColor = accent; bAny = true; }

            return bAny;
        }

        private static bool TryReadColor(string json, string key, out Color color)
        {
            color = Color.White;
            int index = json.IndexOf("\"" + key + "\"", StringComparison.Ordinal);
            if (index < 0) return false;

            int colon = json.IndexOf(':', index + key.Length + 2);
            if (colon < 0) return false;

            // 数组形式： "Key": [r, g, b, a]
            int open = json.IndexOf('[', colon);
            int firstQuote = json.IndexOf('"', colon);
            if (open >= 0 && (firstQuote < 0 || open < firstQuote))
            {
                int close = json.IndexOf(']', open);
                if (close < 0) return false;

                string[] parts = json.Substring(open + 1, close - open - 1).Split(',');
                if (parts.Length < 3) return false;

                return TryBuildColor(parts, out color);
            }

            // 字符串形式： "Key": "r,g,b,a"
            if (firstQuote < 0) return false;
            int secondQuote = json.IndexOf('"', firstQuote + 1);
            if (secondQuote < 0) return false;

            string[] values = json.Substring(firstQuote + 1, secondQuote - firstQuote - 1).Split(',');
            if (values.Length < 3) return false;

            return TryBuildColor(values, out color);
        }

        private static bool TryBuildColor(string[] parts, out Color color)
        {
            color = Color.White;
            float r, g, b, a = 1f;
            if (!float.TryParse(parts[0].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out r)) return false;
            if (!float.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out g)) return false;
            if (!float.TryParse(parts[2].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out b)) return false;
            if (parts.Length > 3)
            {
                float.TryParse(parts[3].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out a);
            }

            color = new Color(r, g, b, a);
            return true;
        }
    }
}
