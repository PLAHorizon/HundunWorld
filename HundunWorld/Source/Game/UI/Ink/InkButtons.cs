using System;
using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.StyleSystem;

namespace HundunWorld.Game.UI.Ink
{
    /// <summary>
    /// 水墨按钮视觉变体。
    /// 对应 CSS <c>.ink-btn</c> 系列变体类。
    /// </summary>
    public enum InkButtonVariant
    {
        /// <summary>默认按钮 — 深灰底 + 金线描边，对应 .ink-btn</summary>
        Default,

        /// <summary>主按钮 — 鎏金渐变底 + 金辉光，对应 .ink-btn-primary</summary>
        Primary,

        /// <summary>朱红按钮 — 朱红渐变底，战斗/危险操作，对应 .ink-btn-vermilion</summary>
        Vermilion,

        /// <summary>幽灵按钮 — 透明底 + 弱边框，对应 .ink-btn-ghost</summary>
        Ghost
    }

    /// <summary>
    /// 水墨按钮尺寸。
    /// 对应 CSS <c>.ink-btn-sm</c> / 默认 / <c>.ink-btn-lg</c>。
    /// </summary>
    public enum InkButtonSize
    {
        /// <summary>小号 — 高 28px，字号 12，对应 .ink-btn-sm</summary>
        Sm,

        /// <summary>中号 — 高 36px，字号 13（默认）</summary>
        Md,

        /// <summary>大号 — 高 44px，字号 15，对应 .ink-btn-lg</summary>
        Lg
    }

    /// <summary>
    /// 水墨按钮。
    /// 对应 CSS <c>.ink-btn</c> 系列，继承 FlaxEngine <see cref="Button"/>，
    /// 根据 <see cref="Variant"/> 设置背景色/边框色/文字色，
    /// 根据 <see cref="ButtonSize"/> 设置高度与字号。
    /// 覆写 <see cref="OnMouseEnter"/>/<see cref="OnMouseLeave"/> 实现 hover 状态色过渡。
    /// </summary>
    public class InkButton : Button
    {
        /// <summary>按钮变体</summary>
        private InkButtonVariant _variant = InkButtonVariant.Default;

        /// <summary>按钮尺寸</summary>
        private InkButtonSize _buttonSize = InkButtonSize.Md;

        // 正常态颜色缓存
        private Color _normalBg;
        private Color _normalBorder;
        private Color _normalText;

        // 悬停态颜色缓存
        private Color _hoverBg;
        private Color _hoverBorder;
        private Color _hoverText;

        /// <summary>
        /// 按钮视觉变体。设置时重新应用对应配色方案。
        /// </summary>
        public InkButtonVariant Variant
        {
            get => _variant;
            set
            {
                _variant = value;
                ApplyVariantColors();
                ApplyCurrentState();
            }
        }

        /// <summary>
        /// 按钮尺寸。设置时更新高度与字号。
        /// </summary>
        public InkButtonSize ButtonSize
        {
            get => _buttonSize;
            set
            {
                _buttonSize = value;
                ApplySize();
            }
        }

        /// <summary>
        /// 构造函数：默认 Default 变体 + Md 尺寸。
        /// </summary>
        public InkButton()
        {
            BorderThickness = 1f;
            ApplyVariantColors();
            ApplySize();
            ApplyCurrentState();
        }

        /// <summary>
        /// 根据变体设置正常态与悬停态配色。
        /// </summary>
        private void ApplyVariantColors()
        {
            switch (_variant)
            {
                case InkButtonVariant.Primary:
                    // 鎏金渐变底用 GoldPrimary 近似（CSS linear-gradient）
                    _normalBg = InkWashTheme.GoldPrimary;
                    _normalBorder = InkWashTheme.GoldPrimary;
                    _normalText = InkWashTheme.TextOnBrand;
                    _hoverBg = InkWashTheme.GoldBright;
                    _hoverBorder = InkWashTheme.GoldBright;
                    _hoverText = InkWashTheme.TextOnBrand;
                    break;

                case InkButtonVariant.Vermilion:
                    _normalBg = InkWashTheme.VermilionPrimary;
                    _normalBorder = InkWashTheme.VermilionDeep;
                    _normalText = InkWashTheme.PaperBright;
                    _hoverBg = InkWashTheme.VermilionBright;
                    _hoverBorder = InkWashTheme.VermilionBright;
                    _hoverText = InkWashTheme.PaperBright;
                    break;

                case InkButtonVariant.Ghost:
                    _normalBg = Color.Transparent;
                    _normalBorder = InkWashTheme.BorderNeutralL2;
                    _normalText = InkWashTheme.TextSecondary;
                    _hoverBg = new Color(
                        InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G,
                        InkWashTheme.GoldPrimary.B, 0.06f);
                    _hoverBorder = InkWashTheme.BorderGold;
                    _hoverText = InkWashTheme.TextBrand;
                    break;

                default: // Default
                    _normalBg = InkWashTheme.BaseTertiary;
                    _normalBorder = InkWashTheme.BorderGold;
                    _normalText = InkWashTheme.TextDefault;
                    _hoverBg = InkWashTheme.BaseElevated;
                    _hoverBorder = InkWashTheme.BorderGoldStrong;
                    _hoverText = InkWashTheme.TextBrand;
                    break;
            }
        }

        /// <summary>
        /// 根据尺寸设置按钮高度与字号。
        /// </summary>
        private void ApplySize()
        {
            float height;
            float fontSize;
            switch (_buttonSize)
            {
                case InkButtonSize.Sm:
                    height = InkWashTheme.ControlHSm;
                    fontSize = 12f;
                    break;
                case InkButtonSize.Lg:
                    height = InkWashTheme.ControlHLg;
                    fontSize = 15f;
                    break;
                default:
                    height = InkWashTheme.ControlHMd;
                    fontSize = 13f;
                    break;
            }
            Height = height;
            Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, fontSize);
        }

        /// <summary>
        /// 应用当前鼠标状态对应的配色（正常态）。
        /// </summary>
        private void ApplyCurrentState()
        {
            BackgroundColor = _normalBg;
            BorderColor = _normalBorder;
            TextColor = _normalText;
        }

        /// <inheritdoc />
        public override void OnMouseEnter(Float2 location)
        {
            base.OnMouseEnter(location);
            BackgroundColor = _hoverBg;
            BorderColor = _hoverBorder;
            TextColor = _hoverText;
        }

        /// <inheritdoc />
        public override void OnMouseLeave()
        {
            base.OnMouseLeave();
            ApplyCurrentState();
        }
    }

    // =======================================================================

    /// <summary>
    /// 水墨标签视觉变体。
    /// 对应 CSS <c>.ink-tag</c> / <c>.ink-tag-brand</c> / <c>.ink-tag-vermilion</c>。
    /// </summary>
    public enum InkTagVariant
    {
        /// <summary>默认标签 — 弱金底 + 中性边框，对应 .ink-tag</summary>
        Default,

        /// <summary>品牌标签 — 金底 + 金边 + 品牌文字色，对应 .ink-tag-brand</summary>
        Brand,

        /// <summary>朱红标签 — 朱红底 + 朱红边 + 朱红文字色，对应 .ink-tag-vermilion</summary>
        Vermilion
    }

    /// <summary>
    /// 水墨标签。
    /// 对应 CSS <c>.ink-tag</c> 系列，继承 <see cref="Label"/>，
    /// 根据 <see cref="TagVariant"/> 设置背景色/边框色/文字色。
    /// 用于行内状态标记、品质徽章等。
    /// </summary>
    public class InkTag : Label
    {
        /// <summary>标签变体</summary>
        private InkTagVariant _tagVariant = InkTagVariant.Default;

        /// <summary>边框颜色（Label 不支持 BorderColor，手动绘制）</summary>
        private Color _borderColor = InkWashTheme.BorderNeutralL2;

        /// <summary>边框厚度</summary>
        private float _borderThickness = 1f;

        /// <summary>
        /// 标签视觉变体。设置时重新应用配色。
        /// </summary>
        public InkTagVariant TagVariant
        {
            get => _tagVariant;
            set
            {
                _tagVariant = value;
                ApplyVariantStyle();
            }
        }

        /// <summary>
        /// 构造函数：默认 Default 变体。
        /// </summary>
        public InkTag()
        {
            HorizontalAlignment = TextAlignment.Center;
            VerticalAlignment = TextAlignment.Center;
            Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f);
            ApplyVariantStyle();
        }

        /// <summary>
        /// 根据变体应用背景色、边框色、文字色。
        /// </summary>
        private void ApplyVariantStyle()
        {
            switch (_tagVariant)
            {
                case InkTagVariant.Brand:
                    BackgroundColor = new Color(
                        InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G,
                        InkWashTheme.GoldPrimary.B, 0.12f);
                    _borderColor = InkWashTheme.BorderGold;
                    TextColor = InkWashTheme.TextBrand;
                    break;

                case InkTagVariant.Vermilion:
                    BackgroundColor = new Color(
                        InkWashTheme.VermilionPrimary.R, InkWashTheme.VermilionPrimary.G,
                        InkWashTheme.VermilionPrimary.B, 0.12f);
                    _borderColor = InkWashTheme.BorderVermilion;
                    TextColor = InkWashTheme.TextVermilion;
                    break;

                default: // Default
                    BackgroundColor = new Color(
                        InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G,
                        InkWashTheme.GoldPrimary.B, 0.08f);
                    _borderColor = InkWashTheme.BorderNeutralL2;
                    TextColor = InkWashTheme.TextSecondary;
                    break;
            }
        }

        /// <inheritdoc />
        public override void Draw()
        {
            base.Draw();

            if (_borderThickness > 0f && _borderColor.A > 0f && Width > 0f && Height > 0f)
            {
                Render2D.DrawRectangle(new Rectangle(0, 0, Width, Height), _borderColor, _borderThickness);
            }
        }
    }

    // =======================================================================

    /// <summary>
    /// 左上角返回按钮。
    /// 对应 CSS <c>.ink-back-btn</c>：40x40 圆角矩形 + 金线描边 + 金色左箭头图标。
    /// 继承 <see cref="ContainerControl"/>，通过 <see cref="Draw"/> 自定义渲染箭头，
    /// 通过 <see cref="OnMouseDown"/>/<see cref="OnMouseUp"/> 处理点击，
    /// 暴露 <see cref="Clicked"/> 事件供外部订阅。
    /// </summary>
    public class InkBackButton : ContainerControl
    {
        /// <summary>箭头尺寸（从中心到端点的像素长度）</summary>
        private const float ArrowExtent = 8f;

        /// <summary>箭头头部斜线长度</summary>
        private const float ArrowHeadSize = 5f;

        /// <summary>箭头线条粗细</summary>
        private const float ArrowThickness = 2f;

        /// <summary>鼠标是否按下（用于点击判定）</summary>
        private bool _isMouseDown;

        /// <summary>是否处于悬停态</summary>
        private bool _isHovered;

        /// <summary>边框颜色（ContainerControl 不支持 BorderColor，手动绘制）</summary>
        private Color _borderColor = InkWashTheme.BorderGold;

        /// <summary>边框厚度</summary>
        private float _borderThickness = 1f;

        /// <summary>
        /// 点击事件。鼠标左键在控件范围内按下并释放时触发。
        /// </summary>
        public event Action Clicked;

        /// <summary>
        /// 构造函数：初始化 40x40 返回按钮。
        /// </summary>
        public InkBackButton()
        {
            Size = new Float2(40f, 40f);
            BackgroundColor = InkWashTheme.Panel;
            ClipChildren = false;
            AutoFocus = true;
        }

        /// <inheritdoc />
        public override void OnMouseEnter(Float2 location)
        {
            base.OnMouseEnter(location);
            _isHovered = true;
            BackgroundColor = new Color(
                InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G,
                InkWashTheme.GoldPrimary.B, 0.12f);
            _borderColor = InkWashTheme.BorderGoldStrong;
        }

        /// <inheritdoc />
        public override void OnMouseLeave()
        {
            base.OnMouseLeave();
            _isHovered = false;
            _isMouseDown = false;
            BackgroundColor = InkWashTheme.Panel;
            _borderColor = InkWashTheme.BorderGold;
        }

        /// <inheritdoc />
        public override bool OnMouseDown(Float2 location, MouseButton button)
        {
            base.OnMouseDown(location, button);
            if (button == MouseButton.Left)
                _isMouseDown = true;
            return true;
        }

        /// <inheritdoc />
        public override bool OnMouseUp(Float2 location, MouseButton button)
        {
            base.OnMouseUp(location, button);
            if (button == MouseButton.Left && _isMouseDown)
            {
                _isMouseDown = false;
                // 判定释放点是否仍在按钮范围内
                if (location.X >= 0f && location.X <= Width &&
                    location.Y >= 0f && location.Y <= Height)
                {
                    Clicked?.Invoke();
                }
            }
            return true;
        }

        /// <inheritdoc />
        public override void Draw()
        {
            // 基类绘制背景 + 子控件
            base.Draw();

            if (Width <= 0f || Height <= 0f)
                return;

            // 手动绘制边框（ContainerControl 不支持 BorderColor/BorderThickness）
            if (_borderThickness > 0f && _borderColor.A > 0f)
            {
                Render2D.DrawRectangle(new Rectangle(0, 0, Width, Height), _borderColor, _borderThickness);
            }

            // 绘制金色左箭头（←），居中
            float cx = Width * 0.5f;
            float cy = Height * 0.5f;
            var arrowColor = _isHovered ? InkWashTheme.GoldBright : InkWashTheme.GoldPrimary;

            // 水平主干
            Render2D.DrawLine(
                new Float2(cx - ArrowExtent, cy),
                new Float2(cx + ArrowExtent, cy),
                arrowColor, ArrowThickness);

            // 上箭头斜线
            Render2D.DrawLine(
                new Float2(cx - ArrowExtent, cy),
                new Float2(cx - ArrowExtent + ArrowHeadSize, cy - ArrowHeadSize),
                arrowColor, ArrowThickness);

            // 下箭头斜线
            Render2D.DrawLine(
                new Float2(cx - ArrowExtent, cy),
                new Float2(cx - ArrowExtent + ArrowHeadSize, cy + ArrowHeadSize),
                arrowColor, ArrowThickness);
        }
    }
}
