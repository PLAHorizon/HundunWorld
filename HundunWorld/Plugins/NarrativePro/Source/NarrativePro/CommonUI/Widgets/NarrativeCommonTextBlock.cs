using System;
using FlaxEngine;
using FlaxEngine.GUI;

namespace NarrativePro.CommonUI.Widgets
{
    /// <summary>
    /// Narrative 文本块。对应 UE5 UNarrativeCommonTextBlock（继承 UCommonTextBlock）。
    /// <para>
    /// UE5 里这个类是空壳（"currently just here incase we need behavior in future"），
    /// 但它是 NarrativeCommonButtonBase 的 ButtonTextBlock 绑定类型，也是本端口所有
    /// CommonUI 文本的统一入口，因此这里以 Flax 的 <see cref="Label"/> 为基类实现，
    /// 把 UE5 的 SetText / SetJustification / SetStyle 映射到 Label 的真实属性上。
    /// </para>
    /// </summary>
    public class NarrativeCommonTextBlock : Label
    {
        /// <summary>当前文本对齐。对应 UE5 Justification（ETextJustify::Type）。</summary>
        public ETextJustify TextJustification = ETextJustify.Left;

        /// <summary>当前文本样式名。对应 UE5 TextStyleClass（FName）。</summary>
        public string Style = string.Empty;

        /// <summary>文本字号（对应 UE5 文本样式里的 size）。默认 14。</summary>
        public float TextSize = 14f;

        /// <summary>是否跟随全局主题着色（关闭后保留调用方自己设置的颜色）。</summary>
        public bool bFollowThemeColor = true;

        /// <summary>设置文本。对应 UE5 SetText。</summary>
        public void SetText(string inText)
        {
            Text = inText ?? string.Empty;
        }

        /// <summary>取当前文本。</summary>
        public string GetText()
        {
            return Text;
        }

        /// <summary>设置文本对齐。对应 UE5 SetJustification。</summary>
        public void SetJustification(ETextJustify justification)
        {
            TextJustification = justification;
            HorizontalAlignment = justification switch
            {
                ETextJustify.Center => TextAlignment.Center,
                ETextJustify.Right => TextAlignment.Far,
                _ => TextAlignment.Near
            };
        }

        /// <summary>
        /// 设置文本样式。对应 UE5 SetStyle(TextStyleClass)：
        /// UE5 的文本样式是一张样式资产表，Flax 没有等价物，因此把样式名映射到
        /// 字号/粗体/颜色三件真实可表达的属性上。
        /// </summary>
        public void SetStyle(string style)
        {
            Style = style ?? string.Empty;
            ApplyStyle();
        }

        /// <summary>把当前样式名与主题落地到 Label 属性。</summary>
        public void ApplyStyle()
        {
            float size = TextSize;
            bool bBold = false;

            switch (Style)
            {
                case "H1":
                case "Title":
                    size = 28f;
                    bBold = true;
                    break;
                case "H2":
                case "Subtitle":
                    size = 20f;
                    bBold = true;
                    break;
                case "Button":
                    size = 16f;
                    break;
                case "Small":
                case "Caption":
                    size = 12f;
                    break;
            }

            TextSize = size;

            var font = Font;
            font.Size = size;
            Font = font;

            Bold = bBold;
            ApplyThemeColor();
        }

        /// <summary>从全局设置取前景色。对应 UE5 CommonTextBlock 从样式表取色。</summary>
        public void ApplyThemeColor()
        {
            if (!bFollowThemeColor) return;

            var settings = NarrativeUIDeveloperSettings.GetMutableDefault();
            if (settings == null) return;

            // UE5 正文用主色，主色偏亮时文字本身应可读，因此高亮态用反色强调
            TextColor = settings.bUseLightTextOnPrimary ? settings.UIInvertAccentColor : Color.White;
            TextColorHighlighted = settings.UIPrimaryColor;
        }

        /// <summary>
        /// 重建控件并应用主题。对应 UE5 UWidget::RebuildWidget 的主题拉取时机。
        /// </summary>
        public virtual void RebuildWidget()
        {
            ApplyStyle();
        }

        /// <summary>加入父控件后落地样式，保证运行时改主题能生效。</summary>
        protected override void OnParentChangedInternal()
        {
            base.OnParentChangedInternal();
            ApplyStyle();
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
