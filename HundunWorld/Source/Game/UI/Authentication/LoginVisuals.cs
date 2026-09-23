using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.Ink;
using HundunWorld.Game.UI.StyleSystem;

namespace HundunWorld.Game.UI.Authentication
{
    /// <summary>
    /// 登录页视觉层次与动效控件集合（浅色水墨青绿，逐帧自绘 + 正弦循环动画）。
    /// 对应 login.htm 的 .hd-shell-bg-overlay / .hd-shell-mist / .hd-shell-light /
    /// .hd-ink-stroke / .hd-login-card / .hd-login-input:focus / .hd-status-dot /
    /// .hd-spirit-particle 等层次与关键帧动画（breathe / drift / pulse / float）。
    /// 复用 InkRenderHelper 做圆形/径向渐变/圆角矩形绘制（Render2D 无椭圆填充 API）。
    /// </summary>
    internal static class LoginVisuals
    {
        // 统一入场淡入时长（秒）与入场上升位移（像素），供卡片与其投影共享
        public const float EntranceDuration = 0.6f;
        public const float EntranceRise = 30f;

        public static float EaseOutCubic(float t)
        {
            t = Mathf.Clamp(t, 0f, 1f);
            float inv = 1f - t;
            return 1f - inv * inv * inv;
        }

        /// <summary>把颜色 alpha 乘以系数（保留 RGB）。</summary>
        public static Color ScaleAlpha(Color c, float k)
            => new Color(c.R, c.G, c.B, c.A * k);

        /// <summary>
        /// 以「每像素单次覆盖」的逐行扫描方式填充圆角矩形。
        /// 避免 InkRenderHelper.FillRoundedRectangle 的十字带+四角圆重叠，
        /// 在半透明填充时于圆角/中心产生二次混合的亮斑接缝（圆角缺陷）。
        /// </summary>
        public static void FillRoundedRectUniform(Rectangle rect, float radius, Color color)
        {
            float w = rect.Width, h = rect.Height;
            if (w <= 0f || h <= 0f)
                return;
            float r = Mathf.Min(radius, Mathf.Min(w, h) * 0.5f);
            if (r <= 0f)
            {
                Render2D.FillRectangle(rect, color);
                return;
            }
            float x = rect.Location.X, yTop = rect.Location.Y;
            int rows = Mathf.CeilToInt(h);
            for (int i = 0; i < rows; i++)
            {
                float ry = i + 0.5f;
                float inset;
                if (ry < r)
                {
                    float dy = r - ry;
                    inset = r - Mathf.Sqrt(Mathf.Max(0f, r * r - dy * dy));
                }
                else if (ry > h - r)
                {
                    float dy = ry - (h - r);
                    inset = r - Mathf.Sqrt(Mathf.Max(0f, r * r - dy * dy));
                }
                else
                {
                    inset = 0f;
                }
                Render2D.FillRectangle(new Rectangle(x + inset, yTop + i, w - 2f * inset, 1f), color);
            }
        }
    }

    // =====================================================================
    // 背景层：垂直遮罩渐变 + 3 枚呼吸灵光 + 漂移云雾
    // =====================================================================
    /// <summary>
    /// 全屏背景装饰层，绘制在背景图之上、内容之下。含呼吸灵光与漂移云雾动画。
    /// </summary>
    public class LoginBackdropLayer : ContainerControl
    {
        private float _t;

        // 三枚灵光：相对位置、半径占比、呼吸周期、相位
        private static readonly Float2[] GlowPos =
        {
            new Float2(0.12f, 0.18f), // light--1 top-left
            new Float2(0.90f, 0.78f), // light--2 bottom-right
            new Float2(0.46f, 0.52f), // light--3 center
        };
        private static readonly float[] GlowRadiusFrac = { 0.32f, 0.27f, 0.21f };
        private static readonly float[] GlowPeriod = { 9f, 13f, 7.5f };
        private static readonly float[] GlowPhase = { 0f, 2.2f, 4.4f };
        // 灵光强度：基础 alpha / 呼吸幅度 / 缩放幅度 / 纵向漂浮（占比）
        private const float GlowAlphaBase = 0.10f;
        private const float GlowAlphaSwing = 0.14f;
        private const float GlowScaleSwing = 0.18f;
        private const float GlowBobFrac = 0.012f;
        // 云雾：alpha / 漂移周期 / 水平漂移幅度（占比）
        private const float MistAlpha = 0.18f;
        private const float MistDriftPeriod = 26f;
        private const float MistDriftFrac = 0.05f;

        public LoginBackdropLayer()
        {
            BackgroundColor = Color.Transparent;
            ClipChildren = false;
            AutoFocus = false;
            Enabled = false; // 装饰层，不拦截鼠标
        }

        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);
            _t += deltaTime;
        }

        public override void DrawSelf()
        {
            base.DrawSelf();
            if (Width <= 0f || Height <= 0f)
                return;

            var rect = new Rectangle(Float2.Zero, Size);

            // 1. 垂直遮罩渐变：纸白 → 墨黑（细密、无重叠分带，消除横向接缝线）
            int bands = Mathf.Max(1, Mathf.CeilToInt(Height / 2f));
            float bandH = Height / bands;
            for (int i = 0; i < bands; i++)
            {
                float t = (i + 0.5f) / bands;
                var col = Color.Lerp(LoginInkTokens.OverlayTop, LoginInkTokens.OverlayBottom, t);
                Render2D.FillRectangle(new Rectangle(0, i * bandH, Width, bandH), col);
            }

            // 2. 漂移云雾：径向渐变中心随时间水平漂移（更慢、更明显）
            float drift = Mathf.Sin(_t * Mathf.TwoPi / MistDriftPeriod) * 0.5f + 0.5f; // 0..1
            var mistCenter = new Float2(Width * (0.30f + MistDriftFrac * drift), Height * 0.40f);
            float mistRadius = Mathf.Max(Width, Height) * 0.55f;
            var mistCenterCol = new Color(LoginInkTokens.Paper.R, LoginInkTokens.Paper.G, LoginInkTokens.Paper.B, MistAlpha);
            var mistEdgeCol = new Color(LoginInkTokens.Paper.R, LoginInkTokens.Paper.G, LoginInkTokens.Paper.B, 0f);
            InkRenderHelper.FillRadialGradient(mistCenter, mistRadius, mistCenterCol, mistEdgeCol, 22);
            // 第二缕云雾：右下方反向漂移，增强纵深
            float drift2 = Mathf.Sin(_t * Mathf.TwoPi / (MistDriftPeriod * 1.3f) + 1.7f) * 0.5f + 0.5f;
            var mist2Center = new Float2(Width * (0.72f - MistDriftFrac * drift2), Height * 0.66f);
            var mist2Col = new Color(LoginInkTokens.Primary100.R, LoginInkTokens.Primary100.G, LoginInkTokens.Primary100.B, MistAlpha * 0.7f);
            InkRenderHelper.FillRadialGradient(mist2Center, mistRadius * 0.8f, mist2Col,
                new Color(mist2Col.R, mist2Col.G, mist2Col.B, 0f), 20);

            // 3. 三枚呼吸灵光（hd-breathe：opacity 0.3↔0.9，scale 1↔1.15）
            foreach (var g in GlowPos)
            {
                int idx = System.Array.IndexOf(GlowPos, g);
                var center = new Float2(Width * g.X, Height * g.Y);
                float baseR = Mathf.Min(Width, Height) * GlowRadiusFrac[idx];
                float phase = (_t + GlowPhase[idx]) * Mathf.TwoPi / GlowPeriod[idx];
                float wave = Mathf.Sin(phase) * 0.5f + 0.5f; // 0..1
                float r = baseR * (1f + GlowScaleSwing * wave);
                float a = GlowAlphaBase + GlowAlphaSwing * wave;
                // 纵向轻微漂浮，增强灵光悬浮感
                var bob = new Float2(center.X, center.Y + Height * GlowBobFrac * Mathf.Sin(phase * 0.5f));
                var cCol = new Color(LoginInkTokens.Primary200.R, LoginInkTokens.Primary200.G, LoginInkTokens.Primary200.B, a);
                var eCol = new Color(cCol.R, cCol.G, cCol.B, 0f);
                InkRenderHelper.FillRadialGradient(bob, r, cCol, eCol, 20);
            }
        }
    }

    // =====================================================================
    // 卡片投影：柔和圆角阴影，与卡片共享入场淡入
    // =====================================================================
    /// <summary>卡片背后的柔和投影层（径向/多层圆角矩形近似 CSS 0 8 40 阴影）。</summary>
    public class LoginCardShadow : ContainerControl
    {
        private float _t;
        private Float2 _home;

        /// <summary>投影落位（不含入场位移）；Update 会叠加与卡片同步的上升偏移。</summary>
        public Float2 HomeLocation { get => _home; set => _home = value; }

        public LoginCardShadow()
        {
            BackgroundColor = Color.Transparent;
            ClipChildren = false;
            AutoFocus = false;
            Enabled = false;
        }

        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);
            _t += deltaTime;
            float e = LoginVisuals.EaseOutCubic(_t / LoginVisuals.EntranceDuration);
            Location = _home + new Float2(0f, (1f - e) * LoginVisuals.EntranceRise);
        }

        public override void DrawSelf()
        {
            base.DrawSelf();
            if (Width <= 0f || Height <= 0f)
                return;

            float entrance = LoginVisuals.EaseOutCubic(_t / LoginVisuals.EntranceDuration);
            var baseCol = LoginInkTokens.CardShadow;
            // 阴影区略大于卡片（卡片内缩 16px 边距），向下偏移 8px
            var shadowRect = new Rectangle(16f, 24f, Width - 32f, Height - 32f);
            const int layers = 6;
            for (int i = layers; i >= 1; i--)
            {
                float k = i / (float)layers;
                var grow = new Rectangle(
                    shadowRect.X - k * 10f,
                    shadowRect.Y - k * 6f,
                    shadowRect.Width + k * 20f,
                    shadowRect.Height + k * 12f);
                float alpha = baseCol.A * (1f - k) * 0.8f * entrance;
                InkRenderHelper.FillRoundedRectangle(grow, LoginInkTokens.RadiusLg + k * 10f,
                    new Color(baseCol.R, baseCol.G, baseCol.B, alpha));
            }
        }
    }

    // =====================================================================
    // 毛玻璃卡片：圆角底 + 亮描边 + 顶部内高光 + 入场淡入
    // =====================================================================
    /// <summary>浅色毛玻璃登录卡片（圆角、1px 亮边、顶部高光条），带入场淡入动画。</summary>
    public class LoginCardPanel : Panel
    {
        private float _t;
        private Float2 _home;

        /// <summary>卡片落位（不含入场位移）；Update 叠加上升偏移，子控件随之整体上浮。</summary>
        public Float2 HomeLocation { get => _home; set => _home = value; }

        public LoginCardPanel()
        {
            BackgroundColor = Color.Transparent; // 自绘圆角底，避免矩形底溢出圆角
            ClipChildren = false;
        }

        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);
            _t += deltaTime;
            float e = LoginVisuals.EaseOutCubic(_t / LoginVisuals.EntranceDuration);
            Location = _home + new Float2(0f, (1f - e) * LoginVisuals.EntranceRise);
        }

        public override void DrawSelf()
        {
            if (Width <= 0f || Height <= 0f)
                return;

            float entrance = LoginVisuals.EaseOutCubic(_t / LoginVisuals.EntranceDuration);
            var rect = new Rectangle(Float2.Zero, Size);

            // 圆角半透明底（毛玻璃降级为半透明纸白）
            var bg = LoginVisuals.ScaleAlpha(LoginInkTokens.CardBg, entrance);
            LoginVisuals.FillRoundedRectUniform(rect, LoginInkTokens.RadiusLg, bg);

            // 细描边（减淡，避免边框过粗）
            InkRenderHelper.DrawRoundedRectangle(rect, LoginInkTokens.RadiusLg,
                LoginVisuals.ScaleAlpha(LoginInkTokens.CardBorder, entrance), 0.8f);

            // 顶部极细高光（仅 1px、低透明，弱化边框厚重感）
            var hl = new Rectangle(16f, 1f, Width - 32f, 1f);
            InkRenderHelper.FillRoundedRectangle(hl, 0.5f,
                LoginVisuals.ScaleAlpha(new Color(1f, 1f, 1f, 0.18f), entrance));
        }
    }

    // =====================================================================
    // 水墨笔触：横向渐隐线 + 模糊高光子层
    // =====================================================================
    /// <summary>标题下的水墨分隔笔触（两端渐隐 + 上方柔光）。</summary>
    public class LoginInkStroke : ContainerControl
    {
        public LoginInkStroke()
        {
            BackgroundColor = Color.Transparent;
            ClipChildren = false;
            Enabled = false;
        }

        public override void DrawSelf()
        {
            base.DrawSelf();
            if (Width <= 0f || Height <= 0f)
                return;

            float y = Height * 0.5f;
            const int segs = 40;
            float step = Width / segs;
            // 主笔触：两端透明、中段青绿（transparent→600→500→600→transparent）
            for (int i = 0; i < segs; i++)
            {
                float t = (i + 0.5f) / segs;
                float edge = Mathf.Min(t, 1f - t) * 2f; // 0..1，两端为 0
                edge = Mathf.Clamp(edge * 1.6f, 0f, 1f);
                var core = Color.Lerp(LoginInkTokens.Primary600, LoginInkTokens.Primary500,
                        1f - Mathf.Abs(t - 0.5f) * 2f);
                var col = new Color(core.R, core.G, core.B, edge);
                float x0 = i * step;
                Render2D.DrawLine(new Float2(x0, y), new Float2(x0 + step, y), col, Height);
            }
            // 高光子层（::after 模糊亮点）+ 中心径向柔光晕
            var glow = new Color(LoginInkTokens.Primary400.R, LoginInkTokens.Primary400.G, LoginInkTokens.Primary400.B, 0.7f);
            InkRenderHelper.FillRoundedRectangle(new Rectangle(Width * 0.22f, y - 3f, Width * 0.56f, 6f), 3f, glow);
            InkRenderHelper.FillRadialGradient(new Float2(Width * 0.5f, y), Width * 0.42f,
                new Color(LoginInkTokens.Primary300.R, LoginInkTokens.Primary300.G, LoginInkTokens.Primary300.B, 0.28f),
                new Color(LoginInkTokens.Primary300.R, LoginInkTokens.Primary300.G, LoginInkTokens.Primary300.B, 0f), 14);
        }
    }

    // =====================================================================
    // 状态点脉冲（hd-pulse：opacity 1↔0.4，3s）
    // =====================================================================
    /// <summary>底部服务器状态指示圆点，带脉冲动画。</summary>
    public class LoginPulseDot : ContainerControl
    {
        private float _t;
        private Color _color = LoginInkTokens.StateSuccess;

        public Color DotColor { get => _color; set => _color = value; }

        public LoginPulseDot()
        {
            BackgroundColor = Color.Transparent;
            ClipChildren = false;
            Enabled = false;
        }

        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);
            _t += deltaTime;
        }

        public override void DrawSelf()
        {
            base.DrawSelf();
            if (Width <= 0f || Height <= 0f)
                return;
            float wave = Mathf.Sin(_t * Mathf.TwoPi / 3f) * 0.5f + 0.5f; // 0..1
            float alpha = 0.4f + 0.6f * wave;
            var center = new Float2(Width * 0.5f, Height * 0.5f);
            float r = Mathf.Min(Width, Height) * 0.5f;
            // 外圈柔光 + 实心点
            InkRenderHelper.FillCircle(center, r * 1.8f, new Color(_color.R, _color.G, _color.B, alpha * 0.25f));
            InkRenderHelper.FillCircle(center, r, new Color(_color.R, _color.G, _color.B, alpha));
        }
    }

    // =====================================================================
    // 灵光粒子浮动（hd-float：translateY -12px + opacity 0.3↔0.7）
    // =====================================================================
    /// <summary>品牌区玄幻灵光粒子，带上下浮动与明暗呼吸。</summary>
    public class LoginFloatParticle : ContainerControl
    {
        private float _t;
        private readonly float _period;
        private readonly float _phase;
        private Color _color = LoginInkTokens.Primary300;

        /// <summary>无参构造（Prefab/编辑器实例化用，默认周期与相位）</summary>
        public LoginFloatParticle()
            : this(7f, 0f)
        {
        }

        public LoginFloatParticle(float period = 7f, float phase = 0f)
        {
            _period = period;
            _phase = phase;
            BackgroundColor = Color.Transparent;
            ClipChildren = false;
            Enabled = false;
        }

        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);
            _t += deltaTime;
        }

        public override void DrawSelf()
        {
            base.DrawSelf();
            if (Width <= 0f || Height <= 0f)
                return;
            float wave = Mathf.Sin((_t + _phase) * Mathf.TwoPi / _period) * 0.5f + 0.5f;
            float yOff = -14f * wave;
            float alpha = 0.4f + 0.45f * wave;
            var center = new Float2(Width * 0.5f, Height * 0.5f + yOff);
            float r = Mathf.Min(Width, Height) * 0.5f;
            InkRenderHelper.FillCircle(center, r * 2f, new Color(_color.R, _color.G, _color.B, alpha * 0.3f));
            InkRenderHelper.FillCircle(center, r, new Color(_color.R, _color.G, _color.B, alpha));
        }
    }

    // =====================================================================
    // 底线输入下划线：常态 Ink200，聚焦渐变为 Primary400 并加粗
    // =====================================================================
    /// <summary>输入框底线下划线，随关联输入框聚焦状态平滑高亮。</summary>
    public class LoginUnderline : ContainerControl
    {
        private Control _watch;
        private float _focusK; // 0..1 平滑聚焦因子

        public LoginUnderline()
        {
            BackgroundColor = Color.Transparent;
            ClipChildren = false;
            Enabled = false;
        }

        /// <summary>设置要监视聚焦状态的控件（输入框）。</summary>
        public void Watch(Control target) => _watch = target;

        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);
            bool focused = _watch != null && (_watch.IsFocused || _watch.ContainsFocus);
            float target = focused ? 1f : 0f;
            float step = deltaTime * 6f;
            _focusK += Mathf.Clamp(target - _focusK, -step, step);
        }

        public override void DrawSelf()
        {
            base.DrawSelf();
            if (Width <= 0f || Height <= 0f)
                return;
            var col = Color.Lerp(LoginInkTokens.Ink200, LoginInkTokens.Primary400, _focusK);
            float thickness = Mathf.Lerp(1f, 2f, _focusK);
            float y = Height * 0.5f;
            Render2D.DrawLine(new Float2(0, y), new Float2(Width, y), col, thickness);
            if (_focusK > 0.01f)
            {
                var glow = new Color(LoginInkTokens.Primary400.R, LoginInkTokens.Primary400.G, LoginInkTokens.Primary400.B, 0.3f * _focusK);
                Render2D.DrawLine(new Float2(0, y + 1f), new Float2(Width, y + 1f), glow, 2f);
            }
        }
    }

    // =====================================================================
    // 主按钮悬停微交互：悬停时以中心为轴轻微放大（配合背景高亮色）
    // =====================================================================
    /// <summary>带悬停微缩放的主操作按钮。</summary>
    public class LoginHoverButton : Button
    {
        private const float HoverScale = 1.04f;
        private float _hoverK;

        public LoginHoverButton()
        {
            Pivot = new Float2(0.5f, 0.5f); // 以中心为缩放轴，避免位移
        }

        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);
            float target = IsMouseOver ? 1f : 0f;
            float step = deltaTime * 10f;
            _hoverK += Mathf.Clamp(target - _hoverK, -step, step);
            float scale = 1f + (HoverScale - 1f) * _hoverK;
            Scale = new Float2(scale, scale);
        }
    }
}
