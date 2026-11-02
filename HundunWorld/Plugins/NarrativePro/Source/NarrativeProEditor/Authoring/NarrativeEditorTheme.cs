#if FLAX_EDITOR
using FlaxEngine;
using FlaxEngine.GUI;

namespace NarrativePro.EditorTools
{
    /// <summary>
    /// 叙事资产编辑器的自绘皮肤。全部用固定色板，不依赖编辑器主题选项，
    /// 保证任务图（彩色节点）、校验列表（红/黄）、选中态在深浅两套主题下都可读。
    /// </summary>
    internal static class NarrativeEditorTheme
    {
        public static readonly Color Canvas = new Color(0.098f, 0.102f, 0.112f);
        public static readonly Color CanvasGrid = new Color(0.15f, 0.155f, 0.17f);
        public static readonly Color Panel = new Color(0.135f, 0.14f, 0.15f);
        public static readonly Color PanelAlt = new Color(0.165f, 0.17f, 0.18f);
        public static readonly Color PanelHover = new Color(0.21f, 0.215f, 0.23f);
        public static readonly Color Border = new Color(0.235f, 0.24f, 0.26f);
        public static readonly Color Text = new Color(0.86f, 0.87f, 0.89f);
        public static readonly Color TextDim = new Color(0.56f, 0.58f, 0.62f);
        public static readonly Color Accent = new Color(0.33f, 0.74f, 0.80f);
        public static readonly Color Selection = new Color(0.96f, 0.78f, 0.32f);
        public static readonly Color Success = new Color(0.38f, 0.80f, 0.50f);
        public static readonly Color Failure = new Color(0.88f, 0.40f, 0.40f);
        public static readonly Color Regular = new Color(0.46f, 0.63f, 0.86f);
        public static readonly Color Player = new Color(0.38f, 0.57f, 0.82f);
        public static readonly Color Warning = new Color(0.95f, 0.76f, 0.36f);

        public static Font FontSmall => FlaxEngine.GUI.Style.Current.FontSmall;

        public static Font FontMedium => FlaxEngine.GUI.Style.Current.FontMedium;

        public static Font FontLarge => FlaxEngine.GUI.Style.Current.FontLarge;

        public static void Fill(Rectangle rect, Color color)
        {
            if (rect.Width > 0f && rect.Height > 0f) Render2D.FillRectangle(rect, color);
        }

        public static void Outline(Rectangle rect, Color color, float thickness = 1f)
        {
            if (rect.Width > 0f && rect.Height > 0f) Render2D.DrawRectangle(rect, color, thickness);
        }

        public static void DrawText(string text, Rectangle rect, Color color, Font font = null,
                                TextAlignment horizontal = TextAlignment.Near, TextAlignment vertical = TextAlignment.Center,
                                TextWrapping wrapping = TextWrapping.NoWrap, float scale = 1f)
        {
            if (string.IsNullOrEmpty(text) || rect.Width <= 0f || rect.Height <= 0f) return;
            Render2D.DrawText(font ?? FontMedium, text, rect, color, horizontal, vertical, wrapping, 1f, scale);
        }

        public static float Measure(string text, Font font = null)
        {
            if (string.IsNullOrEmpty(text)) return 0f;
            return (font ?? FontMedium).MeasureText(text).X;
        }

        /// <summary>按宽度截断并补省略号（自制，CJK 也按像素量）。</summary>
        public static string Fit(string text, float width, Font font = null)
        {
            if (string.IsNullOrEmpty(text)) return "";
            if (Measure(text, font) <= width) return text;

            const string tail = "…";
            for (int length = text.Length - 1; length > 0; length--)
            {
                string candidate = text.Substring(0, length) + tail;
                if (Measure(candidate, font) <= width) return candidate;
            }
            return tail;
        }

        /// <summary>把节点色压成卡片底色（保留一点色相，又不至于盖住文字）。</summary>
        public static Color Tint(Color accent, float amount)
        {
            return new Color(
                Canvas.R * (1f - amount) + accent.R * amount,
                Canvas.G * (1f - amount) + accent.G * amount,
                Canvas.B * (1f - amount) + accent.B * amount);
        }
    }
}
#endif
