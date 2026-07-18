using System;
using System.Collections.Generic;
using FlaxEngine;

namespace NarrativePro.CommonUI.Widgets
{
    /// <summary>
    /// Narrative 字符串下拉框占位。对应 UE5 UNarrativeComboBoxString（继承 UComboBoxString）。
    ///
    /// 移植简化点：
    /// 1. Flax 完全没有 UMG / Slate 控件系统。UComboBoxString 是 UE5 Slate 控件，
    ///    这里以 [Serializable] plain class 占位，保留 UE5 的类名、字段定义、方法签名。
    /// 2. RebuildWidget 在 UE5 中返回 TSharedRef&lt;SWidget&gt;，Flax 中无对应概念，
    ///    实现体以占位形式保留，UI 渲染需用 Flax UIControl（如 Dropdown / ComboBox）重新实现。
    /// 3. UE5 中控件从 NarrativeUIDeveloperSettings 拉取前景色，这里保留逻辑结构。
    /// </summary>
    [Serializable]
    public class NarrativeComboBoxString
    {
        /// <summary>下拉框可选项列表。对应 UE5 ComboBoxString 的 DefaultOptions。</summary>
        public List<string> DefaultOptions = new List<string>();

        /// <summary>当前选中项。对应 UE5 ComboBoxString 的 SelectedOption。</summary>
        public string SelectedOption = string.Empty;

        /// <summary>前景色（从 NarrativeUIDeveloperSettings.UIPrimaryColor 拉取）。对应 UE5 ForegroundColor。</summary>
        public Color ForegroundColor = Color.White;

        /// <summary>构造函数。对应 UE5 UNarrativeComboBoxString 构造函数。</summary>
        public NarrativeComboBoxString()
        {
            // Flax-不兼容: UE5 的 UMG 在 Flax 无对应物，保留占位。原文 TODO: Flax 无 UMG，需用 Flax Dropdown 控件重新实现构造逻辑。
        }

        /// <summary>
        /// 重建控件。对应 UE5 RebuildWidget（UWidget 接口）。
        /// UE5 中从 NarrativeUIDeveloperSettings 拉取前景色后调用 Super::RebuildWidget。
        /// </summary>
        public virtual void RebuildWidget()
        {
            // Flax-不兼容: UE5 的 UMG 在 Flax 无对应物，保留占位。原文 TODO: Flax 无 UMG，需用 Flax UIControl（如 Dropdown）重新实现控件构建。
            // 从开发者设置拉取前景色（与 UE5 行为一致）。
            var settings = NarrativePro.CommonUI.NarrativeUIDeveloperSettings.Instance;
            if (settings != null)
            {
                InitForegroundColor(settings.UIPrimaryColor);
            }
        }

        /// <summary>初始化前景色。对应 UE5 InitForegroundColor。</summary>
        public void InitForegroundColor(Color color)
        {
            // Flax-不兼容: UE5 的 UMG 在 Flax 无对应物，保留占位。原文 TODO: Flax 无 UMG，前景色应用需用 Flax UIControl 的 Color 属性重新实现。
            ForegroundColor = color;
        }
    }
}
