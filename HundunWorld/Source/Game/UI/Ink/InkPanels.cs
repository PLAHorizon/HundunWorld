using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.StyleSystem;

namespace HundunWorld.Game.UI.Ink
{
    /// <summary>
    /// 水墨面板变体。
    /// </summary>
    public enum InkPanelVariant
    {
        /// <summary>默认变体 — rgba(20,23,30,0.85) 85% 不透明背景</summary>
        Default = 0,

        /// <summary>轻量变体 — rgba(20,23,30,0.50) 50% 不透明背景,供战斗 HUD 等需要场景透出的场景使用</summary>
        Lightweight = 1,
    }

    /// <summary>
    /// 半透明毛玻璃金线描边面板。
    /// 对应 CSS <c>.ink-panel</c>：半透明背景（Panel）+ 金色描边（BorderGold）。
    /// 通过基类 <see cref="ContainerControl"/> 的 BackgroundColor/BorderColor/BorderThickness
    /// 属性实现，无需自定义渲染。
    /// </summary>
    public class InkPanel : ContainerControl
    {
        /// <summary>边框颜色（ContainerControl 不支持 BorderColor，手动绘制）</summary>
        private Color _borderColor = InkWashTheme.BorderGold;

        /// <summary>边框厚度</summary>
        private float _borderThickness = 1f;

        /// <summary>面板变体</summary>
        private InkPanelVariant _variant = InkPanelVariant.Default;

        /// <summary>
        /// 面板变体。设置时根据变体更新背景色透明度。
        /// Default = rgba(20,23,30,0.85),Lightweight = rgba(20,23,30,0.50)。
        /// 边框保持 1px BorderGold 不变。
        /// </summary>
        public InkPanelVariant Variant
        {
            get => _variant;
            set
            {
                _variant = value;
                ApplyVariant();
            }
        }

        /// <summary>
        /// 构造函数：应用水墨面板默认样式。
        /// </summary>
        public InkPanel()
        {
            ApplyVariant();
            ClipChildren = true;
        }

        /// <summary>
        /// 根据当前变体应用背景色。
        /// </summary>
        private void ApplyVariant()
        {
            switch (_variant)
            {
                case InkPanelVariant.Lightweight:
                    // Lightweight:50% 不透明,场景半透可见
                    BackgroundColor = new Color(
                        InkWashTheme.Panel.R,
                        InkWashTheme.Panel.G,
                        InkWashTheme.Panel.B,
                        0.50f);
                    break;
                default:
                    // Default:85% 不透明(原状)
                    BackgroundColor = InkWashTheme.Panel;
                    break;
            }
        }

        /// <inheritdoc />
        public override void Draw()
        {
            base.Draw();

            if (Width > 0f && Height > 0f && _borderThickness > 0f && _borderColor.A > 0f)
            {
                Render2D.DrawRectangle(new Rectangle(0, 0, Width, Height), _borderColor, _borderThickness);
            }
        }
    }

    // =======================================================================

    /// <summary>
    /// 纯色面板。
    /// 对应 CSS <c>.ink-panel-solid</c>：纯色背景（PanelSolid）+ 金色描边。
    /// </summary>
    public class InkPanelSolid : ContainerControl
    {
        /// <summary>边框颜色（ContainerControl 不支持 BorderColor，手动绘制）</summary>
        private Color _borderColor = InkWashTheme.BorderGold;

        /// <summary>边框厚度</summary>
        private float _borderThickness = 1f;

        /// <summary>
        /// 构造函数：应用纯色面板默认样式。
        /// </summary>
        public InkPanelSolid()
        {
            BackgroundColor = InkWashTheme.PanelSolid;
            ClipChildren = true;
        }

        /// <inheritdoc />
        public override void Draw()
        {
            base.Draw();

            if (Width > 0f && Height > 0f && _borderThickness > 0f && _borderColor.A > 0f)
            {
                Render2D.DrawRectangle(new Rectangle(0, 0, Width, Height), _borderColor, _borderThickness);
            }
        }
    }

    // =======================================================================

    /// <summary>
    /// 抬升阴影面板。
    /// 对应 CSS <c>.ink-panel-elevated</c>：半透明背景 + 金色描边 + 深阴影。
    /// 阴影通过在 <see cref="Draw"/> 中绘制多层半透明黑色矩形近似 CSS
    /// <c>box-shadow: 0 8px 32px rgba(0,0,0,0.6), 0 2px 8px rgba(0,0,0,0.4)</c>。
    /// </summary>
    public class InkPanelElevated : ContainerControl
    {
        /// <summary>边框颜色（ContainerControl 不支持 BorderColor，手动绘制）</summary>
        private Color _borderColor = InkWashTheme.BorderGold;

        /// <summary>边框厚度</summary>
        private float _borderThickness = 1f;

        /// <summary>
        /// 构造函数：应用抬升面板默认样式。
        /// </summary>
        public InkPanelElevated()
        {
            BackgroundColor = InkWashTheme.Panel;
            ClipChildren = true;
        }

        /// <inheritdoc />
        public override void Draw()
        {
            // 在背景之前绘制阴影层（多层偏移半透明矩形近似模糊投影）
            var bounds = new Rectangle(0, 0, Width, Height);
            float shadowSpread = 6f;

            // 远阴影：offset (2, 8)，spread 32px，alpha 0.6 → 分层衰减
            Render2D.FillRectangle(
                new Rectangle(-shadowSpread * 0.5f, 8f, Width + shadowSpread, Height),
                new Color(0f, 0f, 0f, 0.18f));
            Render2D.FillRectangle(
                new Rectangle(-shadowSpread * 0.25f, 4f, Width + shadowSpread * 0.5f, Height),
                new Color(0f, 0f, 0f, 0.22f));

            // 近阴影：offset (0, 2)，spread 8px，alpha 0.4
            Render2D.FillRectangle(
                new Rectangle(0, 2f, Width, Height),
                new Color(0f, 0f, 0f, 0.25f));

            // 调用基类绘制背景、子控件
            base.Draw();

            // 手动绘制边框
            if (Width > 0f && Height > 0f && _borderThickness > 0f && _borderColor.A > 0f)
            {
                Render2D.DrawRectangle(bounds, _borderColor, _borderThickness);
            }
        }
    }

    // =======================================================================

    /// <summary>
    /// 纸色卷轴面板。
    /// 对应 CSS <c>.ink-paper-panel</c>：纸色背景（PaperPanelBg）+ 暗纸边框（PaperPanelBorder）。
    /// 用于卷轴、信笺、对话框等浅色场景，文字应使用 <see cref="InkWashTheme.TextOnPaper"/> 色。
    /// </summary>
    public class InkPaperPanel : ContainerControl
    {
        /// <summary>边框颜色（ContainerControl 不支持 BorderColor，手动绘制）</summary>
        private Color _borderColor = InkWashTheme.PaperPanelBorder;

        /// <summary>边框厚度</summary>
        private float _borderThickness = 1f;

        /// <summary>
        /// 建议的子控件文字色（纸色上文字），供外部读取并应用到子控件。
        /// </summary>
        public Color TextColor => InkWashTheme.TextOnPaper;

        /// <summary>
        /// 构造函数：应用纸色面板默认样式。
        /// </summary>
        public InkPaperPanel()
        {
            BackgroundColor = InkWashTheme.PaperPanelBg;
            ClipChildren = true;
        }

        /// <inheritdoc />
        public override void Draw()
        {
            base.Draw();

            if (Width > 0f && Height > 0f && _borderThickness > 0f && _borderColor.A > 0f)
            {
                Render2D.DrawRectangle(new Rectangle(0, 0, Width, Height), _borderColor, _borderThickness);
            }
        }
    }

    // =======================================================================

    /// <summary>
    /// 多层金线飞白描边面板。
    /// 对应 CSS <c>.ink-brush-border</c>：在标准金线描边基础上，
    /// 通过 <see cref="Draw"/> 绘制多层错位半透明金线，模拟毛笔飞白效果。
    /// </summary>
    public class InkBrushBorder : ContainerControl
    {
        /// <summary>边框颜色（ContainerControl 不支持 BorderColor，手动绘制）</summary>
        private Color _borderColor = InkWashTheme.BorderGold;

        /// <summary>边框厚度</summary>
        private float _borderThickness = 1f;

        /// <summary>
        /// 构造函数：应用基础金线描边，飞白效果由 <see cref="Draw"/> 叠加。
        /// </summary>
        public InkBrushBorder()
        {
            BackgroundColor = InkWashTheme.Panel;
            ClipChildren = true;
        }

        /// <inheritdoc />
        public override void Draw()
        {
            // 基类先绘制背景 + 子控件
            base.Draw();

            if (Width <= 0f || Height <= 0f)
                return;

            // 手动绘制标准边框
            if (_borderThickness > 0f && _borderColor.A > 0f)
            {
                Render2D.DrawRectangle(new Rectangle(0, 0, Width, Height), _borderColor, _borderThickness);
            }

            // 多层错位金线，模拟飞白（CSS box-shadow inset + border-image 近似）
            // 外层 1：偏移 (1,1)，弱金
            DrawBrushLayer(1f, 1f, InkWashTheme.BorderNeutralL3);
            // 外层 2：偏移 (-1,-1)，古铜
            DrawBrushLayer(-1f, -1f, InkWashTheme.BorderBronze);
            // 高光层：偏移 (0,-1)，亮金
            DrawBrushLayer(0f, -1f, InkWashTheme.BorderGoldStrong);
        }

        /// <summary>
        /// 绘制一层偏移描边矩形。
        /// </summary>
        /// <param name="offsetX">水平偏移</param>
        /// <param name="offsetY">垂直偏移</param>
        /// <param name="color">描边颜色</param>
        private void DrawBrushLayer(float offsetX, float offsetY, Color color)
        {
            var rect = new Rectangle(offsetX, offsetY, Width, Height);
            Render2D.DrawRectangle(rect, color, 1f);
        }
    }

    // =======================================================================

    /// <summary>
    /// 四角 L 型金角装饰面板。
    /// 对应 CSS <c>.ink-corner-deco</c> 系列（tl/tr/bl/br），
    /// 在面板四角绘制 L 型金线装饰。通过 <see cref="ShowTL"/>/<see cref="ShowTR"/>/
    /// <see cref="ShowBL"/>/<see cref="ShowBR"/> 属性独立控制各角显隐。
    /// </summary>
    public class InkCornerDeco : ContainerControl
    {
        /// <summary>L 型角线长度（像素），对应 CSS 14px</summary>
        private const float CornerLength = 14f;

        /// <summary>L 型角线粗细</summary>
        private const float CornerThickness = 1f;

        /// <summary>角线颜色（金色主色 × 0.7 透明度，对应 CSS opacity: 0.7）</summary>
        private static readonly Color CornerColor = new Color(
            InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G,
            InkWashTheme.GoldPrimary.B, 0.7f);

        /// <summary>是否显示左上角装饰</summary>
        public bool ShowTL { get; set; } = true;

        /// <summary>是否显示右上角装饰</summary>
        public bool ShowTR { get; set; } = true;

        /// <summary>是否显示左下角装饰</summary>
        public bool ShowBL { get; set; } = true;

        /// <summary>是否显示右下角装饰</summary>
        public bool ShowBR { get; set; } = true;

        /// <summary>
        /// 构造函数：透明背景，默认四角全显。
        /// </summary>
        public InkCornerDeco()
        {
            BackgroundColor = Color.Transparent;
            ClipChildren = false;
        }

        /// <inheritdoc />
        public override void Draw()
        {
            base.Draw();

            if (Width <= 0f || Height <= 0f)
                return;

            if (ShowTL) DrawCornerL(0f, 0f, 1f, 1f);
            if (ShowTR) DrawCornerL(Width, 0f, -1f, 1f);
            if (ShowBL) DrawCornerL(0f, Height, 1f, -1f);
            if (ShowBR) DrawCornerL(Width, Height, -1f, -1f);
        }

        /// <summary>
        /// 绘制一个 L 型角装饰。
        /// </summary>
        /// <param name="x">角点 X 坐标</param>
        /// <param name="y">角点 Y 坐标</param>
        /// <param name="dirX">水平方向（1 = 向右，-1 = 向左）</param>
        /// <param name="dirY">垂直方向（1 = 向下，-1 = 向上）</param>
        private void DrawCornerL(float x, float y, float dirX, float dirY)
        {
            // 水平线
            Render2D.DrawLine(
                new Float2(x, y),
                new Float2(x + dirX * CornerLength, y),
                CornerColor, CornerThickness);
            // 垂直线
            Render2D.DrawLine(
                new Float2(x, y),
                new Float2(x, y + dirY * CornerLength),
                CornerColor, CornerThickness);
        }
    }

    // =======================================================================

    /// <summary>
    /// 面板标题栏。
    /// 对应 CSS <c>.ink-panel-title</c>：左侧金色竖线（鎏金亮→鎏金深渐变）+
    /// 宋体字标题 + 底部分割线。构造时创建内部 <see cref="Label"/> 子控件用于显示文字。
    /// </summary>
    public class InkPanelTitle : ContainerControl
    {
        /// <summary>金竖线宽度（像素），对应 CSS width: 3px</summary>
        private const float BarWidth = 3f;

        /// <summary>金竖线高度（像素），对应 CSS height: 16px</summary>
        private const float BarHeight = 16f;

        /// <summary>金竖线左边距</summary>
        private const float BarLeftMargin = 12f;

        /// <summary>标题标签</summary>
        private readonly Label _titleLabel;

        /// <summary>
        /// 标题文字。设置时同步更新内部 Label。
        /// </summary>
        public string Title
        {
            get => _titleLabel?.Text ?? string.Empty;
            set
            {
                if (_titleLabel != null)
                    _titleLabel.Text = value;
            }
        }

        /// <summary>
        /// 构造函数：创建标题 Label 子控件并应用默认样式。
        /// </summary>
        public InkPanelTitle()
        {
            BackgroundColor = Color.Transparent;
            ClipChildren = false;
            Height = 44f;

            _titleLabel = new Label
            {
                Text = string.Empty,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 15f),
                TextColor = InkWashTheme.TextBrand,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                Location = new Float2(BarLeftMargin + BarWidth + 8f, 0f),
                Size = new Float2(200f, Height),
            };
            AddChild(_titleLabel);
        }

        /// <inheritdoc />
        protected override void OnSizeChanged()
        {
            base.OnSizeChanged();
            if (_titleLabel != null)
            {
                _titleLabel.Size = new Float2(
                    Mathf.Max(0f, Width - BarLeftMargin - BarWidth - 8f),
                    Height);
            }
        }

        /// <inheritdoc />
        public override void Draw()
        {
            // 1. 先绘制金色竖线（在背景之前，背景透明所以可见）
            float barY = (Height - BarHeight) * 0.5f;
            if (barY > 0f && Height > 0f)
            {
                // 上半段：鎏金亮色
                Render2D.FillRectangle(
                    new Rectangle(BarLeftMargin, barY, BarWidth, BarHeight * 0.5f),
                    InkWashTheme.GoldBright);
                // 下半段：鎏金深色
                Render2D.FillRectangle(
                    new Rectangle(BarLeftMargin, barY + BarHeight * 0.5f, BarWidth, BarHeight * 0.5f),
                    InkWashTheme.GoldDeep);
            }

            // 2. 绘制背景（透明）+ 子控件（标题文字）
            base.Draw();

            // 3. 绘制底部分割线
            if (Height > 0f)
            {
                Render2D.FillRectangle(
                    new Rectangle(0, Height - 1f, Width, 1f),
                    InkWashTheme.Divider);
            }
        }
    }
}
