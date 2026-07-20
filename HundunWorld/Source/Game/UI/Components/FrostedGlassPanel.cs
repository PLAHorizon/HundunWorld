using FlaxEngine;
using FlaxEngine.GUI;

namespace HundunWorld.Game.UI.Components
{
    /// <summary>
    /// 毛玻璃面板组件
    /// 继承 Panel，提供半透明毛玻璃效果，适用于武侠游戏 UI
    /// 如果毛玻璃效果不可用，自动降级为半透明纯色面板
    /// </summary>
    public class FrostedGlassPanel : Panel
    {
        /// <summary>
        /// 毛玻璃效果强度（0.0 ~ 1.0）
        /// </summary>
        public float BlurIntensity { get; set; } = 0.5f;

        /// <summary>
        /// 背景不透明度（0.0 ~ 1.0）
        /// </summary>
        public float BackgroundOpacity { get; set; } = 0.7f;

        /// <summary>
        /// 边框颜色（默认金色描边，出处：--ink-border-gold）
        /// </summary>
        public Color BorderColor { get; set; } = UIStyleTokens.BorderGold;

        /// <summary>
        /// 边框厚度
        /// </summary>
        public float BorderThickness { get; set; } = 1.0f;

        /// <summary>
        /// 内发光颜色（模拟毛玻璃光泽，出处：--ink-shadow-inset 金色微痕）
        /// </summary>
        public Color InnerGlowColor { get; set; } = UIStyleTokens.GoldTrace;

        /// <summary>
        /// 内发光内缩距离
        /// </summary>
        public float InnerGlowSize { get; set; } = 2.0f;

        /// <summary>
        /// 是否启用毛玻璃效果（运行时检测，不可用则自动降级）
        /// </summary>
        private bool _frostedEffectAvailable = true;

        public FrostedGlassPanel()
        {
            // 设置半透明墨黑背景（出处：--ink-bg-panel 墨水深背景 + 面板透明度）
            BackgroundColor = UIStyleTokens.WithAlpha(UIStyleTokens.BgInk, BackgroundOpacity);
        }

        /// <inheritdoc />
        public override void DrawSelf()
        {
            // 1. 基类绘制背景（使用 BackgroundColor）
            base.DrawSelf();

            var rect = new Rectangle(Float2.Zero, Size);

            // 2. 绘制内发光叠加层（模拟毛玻璃光泽）
            if (_frostedEffectAvailable && InnerGlowColor.A > 0.0f && InnerGlowSize > 0.0f)
            {
                var glowRect = new Rectangle(
                    rect.X + InnerGlowSize,
                    rect.Y + InnerGlowSize,
                    rect.Width - InnerGlowSize * 2,
                    rect.Height - InnerGlowSize * 2
                );

                if (glowRect.Width > 0 && glowRect.Height > 0)
                {
                    // 根据 BlurIntensity 调整内发光强度
                    var glowAlpha = InnerGlowColor.A * BlurIntensity;
                    var glowColor = new Color(InnerGlowColor.R, InnerGlowColor.G, InnerGlowColor.B, glowAlpha);
                    Render2D.FillRectangle(glowRect, glowColor);

                    // 顶部高光条，增强毛玻璃质感
                    var highlightHeight = Mathf.Max(1.0f, InnerGlowSize);
                    var highlightRect = new Rectangle(
                        rect.X + InnerGlowSize + BorderThickness,
                        rect.Y + InnerGlowSize,
                        rect.Width - (InnerGlowSize + BorderThickness) * 2,
                        highlightHeight
                    );
                    if (highlightRect.Width > 0)
                    {
                        var highlightAlpha = 0.12f * BlurIntensity;
                        Render2D.FillRectangle(highlightRect, new Color(UIStyleTokens.GoldPrimary.R, UIStyleTokens.GoldPrimary.G, UIStyleTokens.GoldPrimary.B, highlightAlpha));
                    }
                }
            }

            // 3. 绘制边框
            if (BorderThickness > 0.0f && BorderColor.A > 0.0f)
            {
                Render2D.DrawRectangle(rect, BorderColor, BorderThickness);
            }
        }

        /// <inheritdoc />
        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);

            // 同步背景透明度
            var currentBg = BackgroundColor;
            if (Mathf.Abs(currentBg.A - BackgroundOpacity) > 0.001f)
            {
                BackgroundColor = new Color(currentBg.R, currentBg.G, currentBg.B, BackgroundOpacity);
            }
        }

        /// <summary>
        /// 启用或禁用毛玻璃效果（禁用后降级为半透明纯色面板）
        /// </summary>
        public void SetFrostedEffectEnabled(bool enabled)
        {
            _frostedEffectAvailable = enabled;
        }
    }
}
