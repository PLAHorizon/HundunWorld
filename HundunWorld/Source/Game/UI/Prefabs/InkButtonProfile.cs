using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.Ink;
using HundunWorld.Game.UI.StyleSystem;

namespace HundunWorld.Game.UI.Prefabs
{
    /// <summary>
    /// 水墨按钮 Prefab 参数档案（对应 <see cref="InkButton"/>，ds-btn 五变体三尺寸）。
    /// </summary>
    [ExecuteInEditMode]
    public class InkButtonProfile : UIComponentProfile
    {
        /// <summary>按钮文字</summary>
        [EditorOrder(100), EditorDisplay("按钮"), Tooltip("按钮文字")]
        public string Text = "按钮";

        /// <summary>视觉变体（Primary/Secondary/Ghost/Danger/Brand）</summary>
        [EditorOrder(101), EditorDisplay("按钮"), Tooltip("视觉变体：Primary/Secondary/Ghost/Danger/Brand")]
        public InkButtonVariant Variant = InkButtonVariant.Default;

        /// <summary>尺寸（Sm 24 / Md 28 / Lg 32）</summary>
        [EditorOrder(102), EditorDisplay("按钮")]
        public InkButtonSize ButtonSize = InkButtonSize.Md;

        /// <summary>宽度覆写，&lt;=0 保持原宽</summary>
        [EditorOrder(103), EditorDisplay("按钮"), Tooltip("宽度覆写，<=0 保持原值")]
        public float Width = 120f;

        /// <summary>高度覆写，&lt;=0 保持原高（尺寸枚举会先设定标准高）</summary>
        [EditorOrder(104), EditorDisplay("按钮"), Tooltip("高度覆写，<=0 使用尺寸枚举的标准高")]
        public float Height = -1f;

        /// <summary>锚点预设名（TopLeft/Center/Stretch 等），空表示不修改</summary>
        [EditorOrder(105), EditorDisplay("布局"), Tooltip("锚点预设名（TopLeft/Center/Stretch/BottomRight 等），空表示不修改")]
        public string AnchorPreset = string.Empty;

        /// <inheritdoc />
        protected override Control CreateDefaultControl()
        {
            return new InkButton();
        }

        /// <inheritdoc />
        protected override void ApplyParameters(Control control)
        {
            if (control is InkButton ink)
            {
                ink.Variant = Variant;
                ink.ButtonSize = ButtonSize;
            }
            if (control is Button button)
                button.Text = Text;

            UIPrefabUtils.TryApplyAnchor(control, AnchorPreset);

            // 高度：显式覆写优先，否则由尺寸枚举决定（ApplySize 内已设）
            if (Height > 0f)
                UIPrefabUtils.ApplySize(control, Width, Height);
            else if (Width > 0f)
                UIPrefabUtils.ApplySize(control, Width, -1f);
        }
    }

    /// <summary>
    /// 水墨标签 Prefab 参数档案（对应 <see cref="InkTag"/>）。
    /// </summary>
    [ExecuteInEditMode]
    public class InkTagProfile : UIComponentProfile
    {
        /// <summary>标签文字</summary>
        [EditorOrder(100), EditorDisplay("标签")]
        public string Text = "标签";

        /// <summary>视觉变体（Default/Brand/Vermilion）</summary>
        [EditorOrder(101), EditorDisplay("标签")]
        public InkTagVariant TagVariant = InkTagVariant.Default;

        /// <summary>字号</summary>
        [EditorOrder(102), EditorDisplay("标签")]
        public float FontSize = 11f;

        /// <summary>宽度覆写，&lt;=0 保持原宽</summary>
        [EditorOrder(103), EditorDisplay("标签")]
        public float Width = 60f;

        /// <summary>高度覆写，&lt;=0 保持原高</summary>
        [EditorOrder(104), EditorDisplay("标签")]
        public float Height = 22f;

        /// <summary>锚点预设名，空表示不修改</summary>
        [EditorOrder(105), EditorDisplay("布局")]
        public string AnchorPreset = string.Empty;

        /// <inheritdoc />
        protected override Control CreateDefaultControl()
        {
            return new InkTag();
        }

        /// <inheritdoc />
        protected override void ApplyParameters(Control control)
        {
            if (control is InkTag tag)
            {
                tag.TagVariant = TagVariant;
                tag.Text = Text;
                if (FontSize > 0f)
                    tag.Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, FontSize);
            }

            UIPrefabUtils.TryApplyAnchor(control, AnchorPreset);
            UIPrefabUtils.ApplySize(control, Width, Height);
        }
    }

    /// <summary>
    /// 通用文本标签 Prefab 参数档案（Flax 原生 <see cref="Label"/>，颜色走主题令牌名）。
    /// </summary>
    [ExecuteInEditMode]
    public class LabelProfile : UIComponentProfile
    {
        /// <summary>文字内容</summary>
        [EditorOrder(100), EditorDisplay("文本")]
        public string Text = "文本";

        /// <summary>文字颜色令牌名（InkWashTheme 静态字段，如 TextPrimary/TextSecondary/GoldPrimary）</summary>
        [EditorOrder(101), EditorDisplay("文本"), Tooltip("文字颜色令牌名（InkWashTheme 字段名，如 TextPrimary/GoldPrimary），留空保持默认")]
        public string ColorToken = string.Empty;

        /// <summary>字号</summary>
        [EditorOrder(102), EditorDisplay("文本")]
        public float FontSize = 14f;

        /// <summary>水平对齐（Near/Center/Far）</summary>
        [EditorOrder(103), EditorDisplay("文本")]
        public TextAlignment Horizontal = TextAlignment.Near;

        /// <summary>宽度覆写，&lt;=0 保持原宽</summary>
        [EditorOrder(104), EditorDisplay("布局")]
        public float Width = 160f;

        /// <summary>高度覆写，&lt;=0 保持原高</summary>
        [EditorOrder(105), EditorDisplay("布局")]
        public float Height = 24f;

        /// <summary>锚点预设名，空表示不修改</summary>
        [EditorOrder(106), EditorDisplay("布局")]
        public string AnchorPreset = string.Empty;

        /// <inheritdoc />
        protected override Control CreateDefaultControl()
        {
            return new Label();
        }

        /// <inheritdoc />
        protected override void ApplyParameters(Control control)
        {
            if (control is Label label)
            {
                label.Text = Text;
                label.HorizontalAlignment = Horizontal;
                if (FontSize > 0f)
                    label.Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, FontSize);
                if (UIPrefabUtils.TryResolveColorToken(ColorToken, out var color))
                    label.TextColor = color;
            }

            UIPrefabUtils.TryApplyAnchor(control, AnchorPreset);
            UIPrefabUtils.ApplySize(control, Width, Height);
        }
    }
}
