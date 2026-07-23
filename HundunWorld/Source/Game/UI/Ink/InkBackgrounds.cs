using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.StyleSystem;

namespace HundunWorld.Game.UI.Ink
{
    /// <summary>
    /// 水墨控件渲染辅助工具。
    /// 提供 FlaxEngine Render2D 不直接支持的圆形填充、径向渐变等绘制方法，
    /// 供 Ink 组件库中需要自定义渲染的控件共享使用。
    /// </summary>
    internal static class InkRenderHelper
    {
        /// <summary>圆形三角形扇分段数（越大越圆滑，32 段已足够视觉平滑）</summary>
        private const int CircleSegments = 32;

        /// <summary>
        /// 使用三角形扇填充一个圆形。
        /// FlaxEngine 的 Render2D 不提供 FillEllipse/FillCircle 方法，
        /// 因此用三角形扇近似绘制。
        /// </summary>
        /// <param name="center">圆心坐标（控件局部坐标系）</param>
        /// <param name="radius">圆半径（像素），小于等于 0 时不绘制</param>
        /// <param name="color">填充颜色</param>
        internal static void FillCircle(Float2 center, float radius, Color color)
        {
            if (radius <= 0f)
                return;

            var vertices = new Float2[CircleSegments * 3];
            for (int i = 0; i < CircleSegments; i++)
            {
                float a1 = (i / (float)CircleSegments) * Mathf.TwoPi;
                float a2 = ((i + 1) / (float)CircleSegments) * Mathf.TwoPi;
                int idx = i * 3;
                vertices[idx] = center;
                vertices[idx + 1] = center + new Float2(Mathf.Cos(a1) * radius, Mathf.Sin(a1) * radius);
                vertices[idx + 2] = center + new Float2(Mathf.Cos(a2) * radius, Mathf.Sin(a2) * radius);
            }
            Render2D.FillTriangles(vertices, color);
        }

        /// <summary>
        /// 绘制圆形描边（多段折线近似）。
        /// </summary>
        /// <param name="center">圆心坐标（控件局部坐标系）</param>
        /// <param name="radius">圆半径（像素），小于等于 0 时不绘制</param>
        /// <param name="color">描边颜色</param>
        /// <param name="thickness">线宽（像素）</param>
        internal static void DrawCircle(Float2 center, float radius, Color color, float thickness)
        {
            if (radius <= 0f)
                return;

            float step = Mathf.TwoPi / CircleSegments;
            for (int i = 0; i < CircleSegments; i++)
            {
                float a1 = i * step;
                float a2 = (i + 1) * step;
                var p1 = center + new Float2(Mathf.Cos(a1) * radius, Mathf.Sin(a1) * radius);
                var p2 = center + new Float2(Mathf.Cos(a2) * radius, Mathf.Sin(a2) * radius);
                Render2D.DrawLine(p1, p2, color, thickness);
            }
        }

        /// <summary>
        /// 绘制径向渐变圆形：圆心处使用 centerColor，边缘处使用 edgeColor，
        /// 中间通过线性插值过渡。采用从外到内逐层叠加同心圆的方式实现。
        /// </summary>
        /// <param name="center">渐变圆心</param>
        /// <param name="maxRadius">渐变最大半径</param>
        /// <param name="centerColor">圆心颜色</param>
        /// <param name="edgeColor">边缘颜色</param>
        /// <param name="steps">渐变分段数（默认 16）</param>
        internal static void FillRadialGradient(Float2 center, float maxRadius, Color centerColor, Color edgeColor, int steps = 16)
        {
            if (maxRadius <= 0f || steps <= 0)
                return;

            for (int i = steps; i >= 1; i--)
            {
                float t = (float)i / steps;
                float r = maxRadius * t;
                Color c = Color.Lerp(centerColor, edgeColor, t);
                FillCircle(center, r, c);
            }
        }

        /// <summary>
        /// 获取水墨主题字体引用，封装为 FontReference。
        /// </summary>
        /// <param name="role">字体角色</param>
        /// <param name="size">字体大小</param>
        /// <returns>FontReference 实例</returns>
        internal static FontReference GetFontRef(InkWashTheme.FontRole role, float size)
        {
            return new FontReference(InkWashTheme.GetFont(role), size);
        }

        /// <summary>
        /// 填充圆角矩形（十字带 + 四角圆形拼合）。
        /// </summary>
        /// <param name="rect">矩形区域</param>
        /// <param name="radius">圆角半径，超过半边时自动钳制</param>
        /// <param name="color">填充颜色</param>
        internal static void FillRoundedRectangle(Rectangle rect, float radius, Color color)
        {
            if (rect.Size.X <= 0f || rect.Size.Y <= 0f)
                return;
            if (radius <= 0f)
            {
                Render2D.FillRectangle(rect, color);
                return;
            }

            float r = Mathf.Min(radius, Mathf.Min(rect.Size.X, rect.Size.Y) * 0.5f);
            float x = rect.Location.X, y = rect.Location.Y, w = rect.Size.X, h = rect.Size.Y;

            // 水平带（去掉上下圆角区域）
            Render2D.FillRectangle(new Rectangle(x, y + r, w, h - 2f * r), color);
            // 垂直带（去掉左右圆角区域）
            Render2D.FillRectangle(new Rectangle(x + r, y, w - 2f * r, h), color);
            // 四角圆形
            FillCircle(new Float2(x + r, y + r), r, color);
            FillCircle(new Float2(x + w - r, y + r), r, color);
            FillCircle(new Float2(x + r, y + h - r), r, color);
            FillCircle(new Float2(x + w - r, y + h - r), r, color);
        }

        /// <summary>
        /// 绘制圆角矩形描边（四边线段 + 四角弧线）。
        /// </summary>
        /// <param name="rect">矩形区域</param>
        /// <param name="radius">圆角半径</param>
        /// <param name="color">描边颜色</param>
        /// <param name="thickness">线宽</param>
        internal static void DrawRoundedRectangle(Rectangle rect, float radius, Color color, float thickness)
        {
            if (rect.Size.X <= 0f || rect.Size.Y <= 0f)
                return;
            if (radius <= 0f)
            {
                Render2D.DrawRectangle(rect, color, thickness);
                return;
            }

            float r = Mathf.Min(radius, Mathf.Min(rect.Size.X, rect.Size.Y) * 0.5f);
            float x = rect.Location.X, y = rect.Location.Y, w = rect.Size.X, h = rect.Size.Y;

            // 四边（缩短圆角长度）
            Render2D.DrawLine(new Float2(x + r, y), new Float2(x + w - r, y), color, thickness);           // 上
            Render2D.DrawLine(new Float2(x + r, y + h), new Float2(x + w - r, y + h), color, thickness);   // 下
            Render2D.DrawLine(new Float2(x, y + r), new Float2(x, y + h - r), color, thickness);           // 左
            Render2D.DrawLine(new Float2(x + w, y + r), new Float2(x + w, y + h - r), color, thickness);   // 右

            // 四角弧线（每角 8 段折线近似）
            const int arcSegs = 8;
            DrawArc(new Float2(x + r, y + r), r, Mathf.Pi, Mathf.Pi * 1.5f, arcSegs, color, thickness);         // 左上
            DrawArc(new Float2(x + w - r, y + r), r, Mathf.Pi * 1.5f, Mathf.TwoPi, arcSegs, color, thickness);  // 右上
            DrawArc(new Float2(x + r, y + h - r), r, Mathf.Pi * 0.5f, Mathf.Pi, arcSegs, color, thickness);     // 左下
            DrawArc(new Float2(x + w - r, y + h - r), r, 0f, Mathf.Pi * 0.5f, arcSegs, color, thickness);       // 右下
        }

        /// <summary>
        /// 绘制圆弧（折线近似）。
        /// </summary>
        private static void DrawArc(Float2 center, float radius, float startAngle, float endAngle, int segments, Color color, float thickness)
        {
            Float2 prev = center + new Float2(Mathf.Cos(startAngle) * radius, Mathf.Sin(startAngle) * radius);
            for (int i = 1; i <= segments; i++)
            {
                float a = Mathf.Lerp(startAngle, endAngle, i / (float)segments);
                var cur = center + new Float2(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius);
                Render2D.DrawLine(prev, cur, color, thickness);
                prev = cur;
            }
        }
    }

    // =======================================================================

    /// <summary>
    /// 全局水墨背景层。
    /// 对应 CSS <c>.ink-bg-layer</c>，绘制三层径向渐变：
    /// 鎏金（左上）+ 古铜（右下）+ 深墨黑（底部）叠加在 BaseDefault 底色之上。
    /// 该控件无子控件，不接收鼠标事件（<see cref="Control.Enabled"/> 设为 false 仅影响交互，
    /// 渲染仍由 <see cref="Draw"/> 驱动）。
    /// </summary>
    public class InkBackgroundLayer : ContainerControl
    {
        /// <summary>鎏金光晕颜色（左上角渐变中心色）</summary>
        private static readonly Color GoldTint = new Color(
            InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G,
            InkWashTheme.GoldPrimary.B, 0.06f);

        /// <summary>古铜光晕颜色（右下角渐变中心色）</summary>
        private static readonly Color BronzeTint = new Color(
            InkWashTheme.BronzePrimary.R, InkWashTheme.BronzePrimary.G,
            InkWashTheme.BronzePrimary.B, 0.04f);

        /// <summary>深墨黑渐变颜色（底部渐变中心色）</summary>
        private static readonly Color AbyssTint = new Color(
            InkWashTheme.Abyss.R, InkWashTheme.Abyss.G,
            InkWashTheme.Abyss.B, 0.8f);

        /// <summary>透明色（渐变边缘终止色）</summary>
        private static readonly Color Transparent = new Color(0f, 0f, 0f, 0f);

        /// <summary>
        /// Scrim 半透明遮罩 alpha 值（默认 0.72）。
        /// 控制 Draw 中第②步全屏暗色遮罩的不透明度，值越小越透出 3D 场景。
        /// </summary>
        public float ScrimOpacity { get; set; } = 0.72f;

        /// <summary>
        /// 是否绘制 BaseDefault 不透明底色（默认 false）。
        /// 仅加载页等需要纯黑底色的场景设为 true；常规菜单场景设为 false 以透出 3D 场景。
        /// </summary>
        public bool DrawBaseFill { get; set; } = false;

        /// <summary>
        /// 构造函数：初始化为透明、不裁剪子控件的全屏背景层。
        /// </summary>
        public InkBackgroundLayer()
        {
            BackgroundColor = Color.Transparent;
            ClipChildren = false;
            AutoFocus = false;
        }

        /// <inheritdoc />
        public override void Draw()
        {
            if (!Visible || Width <= 0f || Height <= 0f)
                return;

            // 1. 可选：填充 BaseDefault 不透明底色（加载页等纯黑场景才启用）
            if (DrawBaseFill)
            {
                Render2D.FillRectangle(new Rectangle(0, 0, Width, Height), InkWashTheme.BaseDefault);
            }

            // 2. Scrim 全屏半透明遮罩 — rgba(8, 9, 14, ScrimOpacity)
            var scrimColor = new Color(8f / 255f, 9f / 255f, 14f / 255f, ScrimOpacity);
            Render2D.FillRectangle(new Rectangle(0, 0, Width, Height), scrimColor);

            // 3. 鎏金径向渐变 — 左上角 (20%, 30%)，半径 50%
            var goldCenter = new Float2(Width * 0.2f, Height * 0.3f);
            float goldRadius = Mathf.Min(Width, Height) * 0.5f;
            InkRenderHelper.FillRadialGradient(goldCenter, goldRadius, GoldTint, Transparent);

            // 4. 古铜径向渐变 — 右下角 (80%, 70%)，半径 55%
            var bronzeCenter = new Float2(Width * 0.8f, Height * 0.7f);
            float bronzeRadius = Mathf.Min(Width, Height) * 0.55f;
            InkRenderHelper.FillRadialGradient(bronzeCenter, bronzeRadius, BronzeTint, Transparent);

            // 5. 深墨黑径向渐变 — 底部中心 (50%, 100%)，半径 60%
            var abyssCenter = new Float2(Width * 0.5f, Height);
            float abyssRadius = Mathf.Min(Width, Height) * 0.6f;
            InkRenderHelper.FillRadialGradient(abyssCenter, abyssRadius, AbyssTint, Transparent);
        }
    }

    // =======================================================================

    /// <summary>
    /// 暗角晕影层。
    /// 对应 CSS <c>.ink-vignette</c>，绘制中心透明、边缘半黑（rgba(0,0,0,0.55)）的径向渐变，
    /// 营造画面聚焦效果。通常作为最顶层的装饰层叠加在内容之上。
    /// </summary>
    public class InkVignette : ContainerControl
    {
        /// <summary>晕影中心色（完全透明）</summary>
        private static readonly Color VignetteCenter = new Color(0f, 0f, 0f, 0f);

        /// <summary>
        /// 晕影边缘 alpha 值（默认 0.30，从原静态值 0.55 减弱）。
        /// 控制 Draw 中径向渐变边缘的不透明度。
        /// </summary>
        public float EdgeOpacity { get; set; } = 0.30f;

        /// <summary>
        /// 构造函数：初始化为透明、不裁剪的晕影层。
        /// </summary>
        public InkVignette()
        {
            BackgroundColor = Color.Transparent;
            ClipChildren = false;
            AutoFocus = false;
            Enabled = false; // 装饰性控件，禁用交互避免拦截鼠标事件
        }

        /// <inheritdoc />
        public override void Draw()
        {
            if (!Visible || Width <= 0f || Height <= 0f)
                return;

            var center = new Float2(Width * 0.5f, Height * 0.5f);
            float radius = Mathf.Max(Width, Height) * 0.6f;
            var edgeColor = new Color(0f, 0f, 0f, EdgeOpacity);
            InkRenderHelper.FillRadialGradient(center, radius, VignetteCenter, edgeColor, 20);
        }
    }

    // =======================================================================

    /// <summary>
    /// 水墨晕染装饰变体。
    /// 对应 CSS <c>.ink-splash-1</c> / <c>.ink-splash-2</c> / <c>.ink-splash-vermilion</c> / <c>.ink-splash-spring</c>。
    /// </summary>
    public enum InkSplashVariant
    {
        /// <summary>标准晕染 — 300x300，BaseTertiary 色，对应 .ink-splash-1</summary>
        Normal,

        /// <summary>抬升晕染 — 200x200，BaseElevated 色，对应 .ink-splash-2</summary>
        Elevated,

        /// <summary>朱红晕染 — 250x250，朱红辉光色，对应 .ink-splash-vermilion</summary>
        Vermilion,

        /// <summary>春色晕染 — 320x320，芽绿辉光色，对应 .ink-splash-spring</summary>
        Spring
    }

    /// <summary>
    /// 水墨晕染装饰。
    /// 对应 CSS <c>.ink-splash</c>，绘制带模糊效果的圆形径向渐变装饰。
    /// 通过 <see cref="InkSplashVariant"/> 属性切换三种变体：
    /// Normal（鎏金灰）/ Elevated（抬升灰）/ Vermilion（朱红）。
    /// CSS 中的 <c>filter: blur(15px)</c> 和 <c>opacity: 0.3</c>
    /// 通过增加渐变分段数和降低整体 Alpha 近似实现。
    /// </summary>
    public class InkSplash : ContainerControl
    {
        /// <summary>晕染变体</summary>
        private InkSplashVariant _variant = InkSplashVariant.Normal;

        /// <summary>整体不透明度（对应 CSS opacity: 0.3）</summary>
        private float _opacity = 0.15f;

        /// <summary>
        /// 晕染变体。设置时同步更新控件尺寸与颜色参数。
        /// </summary>
        public InkSplashVariant Variant
        {
            get => _variant;
            set
            {
                _variant = value;
                ApplyVariant();
            }
        }

        /// <summary>
        /// 整体不透明度（0.0~1.0），默认 0.3 对应 CSS opacity。
        /// </summary>
        public float Opacity
        {
            get => _opacity;
            set => _opacity = Mathf.Clamp(value, 0f, 1f);
        }

        /// <summary>
        /// 构造函数：默认 Normal 变体。
        /// </summary>
        public InkSplash()
        {
            BackgroundColor = Color.Transparent;
            ClipChildren = false;
            AutoFocus = false;
            Enabled = false; // 装饰性控件，禁用交互避免拦截鼠标事件
            ApplyVariant();
        }

        /// <summary>
        /// 根据当前变体设置控件尺寸。
        /// </summary>
        private void ApplyVariant()
        {
            switch (_variant)
            {
                case InkSplashVariant.Normal:
                    Size = new Float2(300f, 300f);
                    break;
                case InkSplashVariant.Elevated:
                    Size = new Float2(200f, 200f);
                    break;
                case InkSplashVariant.Vermilion:
                    Size = new Float2(250f, 250f);
                    break;
                case InkSplashVariant.Spring:
                    Size = new Float2(320f, 320f);
                    break;
            }
        }

        /// <inheritdoc />
        public override void Draw()
        {
            if (!Visible || Width <= 0f || Height <= 0f)
                return;

            var center = new Float2(Width * 0.5f, Height * 0.5f);
            float radius = Mathf.Min(Width, Height) * 0.5f;

            Color centerColor, edgeColor;
            switch (_variant)
            {
                case InkSplashVariant.Elevated:
                    centerColor = ScaleAlpha(InkWashTheme.BaseElevated, _opacity);
                    break;
                case InkSplashVariant.Vermilion:
                    centerColor = new Color(
                        InkWashTheme.VermilionPrimary.R,
                        InkWashTheme.VermilionPrimary.G,
                        InkWashTheme.VermilionPrimary.B,
                        0.15f * _opacity);
                    break;
                case InkSplashVariant.Spring:
                    centerColor = new Color(
                        InkWashTheme.SpringGreenPrimary.R,
                        InkWashTheme.SpringGreenPrimary.G,
                        InkWashTheme.SpringGreenPrimary.B,
                        0.18f * _opacity);
                    break;
                default:
                    centerColor = ScaleAlpha(InkWashTheme.BaseTertiary, _opacity);
                    break;
            }
            edgeColor = new Color(centerColor.R, centerColor.G, centerColor.B, 0f);

            // 使用更多分段模拟 blur(15px) 的柔和边缘
            InkRenderHelper.FillRadialGradient(center, radius, centerColor, edgeColor, 24);
        }

        /// <summary>
        /// 将颜色的 Alpha 通道乘以指定系数。
        /// </summary>
        private static Color ScaleAlpha(Color color, float scale)
        {
            return new Color(color.R, color.G, color.B, color.A * scale);
        }
    }
}
