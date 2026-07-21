using FlaxEngine;
using FlaxEngine.GUI;

namespace HundunWorld.Game.UI.Ink
{
    public static class UILayout
    {
        public const float RefWidth = 1920f;
        public const float RefHeight = 1080f;

        public static float Scale { get; private set; } = 1f;
        public static float ScaleX { get; private set; } = 1f;
        public static float ScaleY { get; private set; } = 1f;

        public static void UpdateScale(Control root)
        {
            if (root == null) return;
            float w = root.Width;
            float h = root.Height;
            if (w <= 0f || h <= 0f) return;
            ScaleX = w / RefWidth;
            ScaleY = h / RefHeight;
            Scale = Mathf.Min(ScaleX, ScaleY);
        }

        public static float S(float value) => value * Scale;
        public static float SX(float value) => value * ScaleX;
        public static float Font(float size) => size * Scale;
    }
}
