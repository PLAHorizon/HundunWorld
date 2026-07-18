using System;
using FlaxEngine;

namespace NarrativePro.CommonUI.Widgets
{
    /// <summary>
    /// Narrative 文本块占位。对应 UE5 UNarrativeCommonTextBlock（继承 UCommonTextBlock）。
    ///
    /// 移植简化点：
    /// 1. Flax 完全没有 UMG / CommonUI 控件系统。UCommonTextBlock 是 UE5 CommonUI 文本控件，
    ///    这里以 [Serializable] plain class 占位，保留 UE5 的类名与基本字段/方法。
    /// 2. UE5 中此类为空壳（"currently just here incase we need behavior in future"），
    ///    Flax 中同样保留为占位基类，供 NarrativeCommonButtonBase 等引用。
    /// 3. UI 渲染部分需用 Flax UIControl（如 Label / TextRender）重新实现。
    /// </summary>
    [Serializable]
    public class NarrativeCommonTextBlock
    {
        /// <summary>当前文本。对应 UE5 Text（FText）。</summary>
        public string Text = string.Empty;

        /// <summary>文本对齐方式。对应 UE5 Justification（ETextJustify::Type）。</summary>
        public ETextJustify TextJustification = ETextJustify.Left;

        /// <summary>当前文本样式。对应 UE5 当前 TextStyleClass（FName）。</summary>
        public string Style = string.Empty;

        /// <summary>设置文本。对应 UE5 SetText。</summary>
        public void SetText(string inText)
        {
            // Flax-不兼容: UE5 的 UMG 在 Flax 无对应物，保留占位。原文 TODO: Flax 无 UMG，需用 Flax Label 控件的 Text 属性重新实现。
            Text = inText ?? string.Empty;
        }

        /// <summary>设置文本对齐。对应 UE5 SetJustification。</summary>
        public void SetJustification(ETextJustify justification)
        {
            // Flax-不兼容: UE5 的 UMG 在 Flax 无对应物，保留占位。原文 TODO: Flax 无 UMG，需用 Flax Label 控件的对齐属性重新实现。
            TextJustification = justification;
        }

        /// <summary>设置文本样式。对应 UE5 SetStyle。</summary>
        public void SetStyle(string style)
        {
            // Flax-不兼容: UE5 的 UMG 在 Flax 无对应物，保留占位。原文 TODO: Flax 无 UMG，需用 Flax 文本样式系统重新实现。
            Style = style ?? string.Empty;
        }
    }

    /// <summary>
    /// 文本对齐方式。对应 UE5 ETextJustify::Type。
    /// </summary>
    public enum ETextJustify
    {
        /// <summary>左对齐。</summary>
        Left = 0,
        /// <summary>居中对齐。</summary>
        Center = 1,
        /// <summary>右对齐。</summary>
        Right = 2
    }
}
