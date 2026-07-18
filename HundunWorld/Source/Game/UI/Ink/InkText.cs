using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.StyleSystem;

namespace HundunWorld.Game.UI.Ink
{
    /// <summary>
    /// 水墨文本样式枚举。
    /// 对应 CSS <c>.ink-text-*</c> 系列排版类。
    /// </summary>
    public enum InkTextStyle
    {
        /// <summary>展示文字 — 马善政体 32px，品牌金色，居中，对应 .ink-text-display</summary>
        Display,

        /// <summary>标题 — 思源宋体 18px，默认文字色，左对齐，对应 .ink-text-heading</summary>
        Heading,

        /// <summary>副标题 — 思源宋体 14px，次级文字色，左对齐，对应 .ink-text-subheading</summary>
        Subheading,

        /// <summary>正文 — 思源黑体 13px，次级文字色，左对齐，对应 .ink-text-body</summary>
        Body,

        /// <summary>说明 — 思源黑体 11px，三级文字色，左对齐，对应 .ink-text-caption</summary>
        Caption,

        /// <summary>数字 — DIN 字体，品牌金色，右对齐，对应 .ink-text-number</summary>
        Number
    }

    /// <summary>
    /// 统一文本控件。
    /// 对应 CSS <c>.ink-text-*</c> 系列，继承 <see cref="Label"/>，
    /// 根据 <see cref="TextStyle"/> 自动设置字体（Font）、字号、文字色（TextColor）
    /// 与水平对齐（HorizontalAlignment）。
    /// </summary>
    public class InkTextBlock : Label
    {
        /// <summary>当前文本样式</summary>
        private InkTextStyle _textStyle = InkTextStyle.Body;

        /// <summary>
        /// 文本样式。设置时自动重新应用对应字体、字号、颜色与对齐。
        /// </summary>
        public InkTextStyle TextStyle
        {
            get => _textStyle;
            set
            {
                _textStyle = value;
                ApplyStyle();
            }
        }

        /// <summary>
        /// 构造函数：默认 Body 样式。
        /// </summary>
        public InkTextBlock()
            : this(InkTextStyle.Body)
        {
        }

        /// <summary>
        /// 构造函数：指定初始文本样式。
        /// </summary>
        /// <param name="style">文本样式</param>
        public InkTextBlock(InkTextStyle style)
        {
            _textStyle = style;
            VerticalAlignment = TextAlignment.Center;
            ApplyStyle();
        }

        /// <summary>
        /// 根据当前 <see cref="_textStyle"/> 应用字体、字号、文字色与水平对齐。
        /// </summary>
        private void ApplyStyle()
        {
            switch (_textStyle)
            {
                case InkTextStyle.Display:
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 32f);
                    TextColor = InkWashTheme.TextBrand;
                    HorizontalAlignment = TextAlignment.Center;
                    break;

                case InkTextStyle.Heading:
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 18f);
                    TextColor = InkWashTheme.TextDefault;
                    HorizontalAlignment = TextAlignment.Near;
                    break;

                case InkTextStyle.Subheading:
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 14f);
                    TextColor = InkWashTheme.TextSecondary;
                    HorizontalAlignment = TextAlignment.Near;
                    break;

                case InkTextStyle.Caption:
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f);
                    TextColor = InkWashTheme.TextTertiary;
                    HorizontalAlignment = TextAlignment.Near;
                    break;

                case InkTextStyle.Number:
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 15f);
                    TextColor = InkWashTheme.TextBrand;
                    HorizontalAlignment = TextAlignment.Far;
                    break;

                default: // Body
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f);
                    TextColor = InkWashTheme.TextSecondary;
                    HorizontalAlignment = TextAlignment.Near;
                    break;
            }
        }
    }
}
