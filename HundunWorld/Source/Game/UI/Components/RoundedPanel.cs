using FlaxEngine;
using FlaxEngine.GUI;

namespace HundunWorld.Game.UI.Components
{
    /// <summary>
    /// 圆角面板组件
    /// </summary>
    public class RoundedPanel : Panel
    {
        /// <summary>
        /// 圆角半径
        /// </summary>
        public float CornerRadius { get; set; } = 10.0f;

        /// <inheritdoc />
        public override void Draw()
        {
            if (!Visible) return;

            var rect = new Rectangle(Vector2.Zero, Size);
            DrawRoundedBox(rect, BackgroundColor, CornerRadius);
            
            // 绘制子控件
            DrawChildren();
        }

        private void DrawRoundedBox(Rectangle rect, Color color, float radius)
        {
            if (radius <= 0)
            {
                Render2D.FillRectangle(rect, color);
                return;
            }

            radius = Mathf.Min(radius, rect.Width / 2f, rect.Height / 2f);

            // 绘制中心十字区域
            Render2D.FillRectangle(new Rectangle(rect.X + radius, rect.Y, rect.Width - radius * 2, rect.Height), color);
            Render2D.FillRectangle(new Rectangle(rect.X, rect.Y + radius, radius, rect.Height - radius * 2), color);
            Render2D.FillRectangle(new Rectangle(rect.Right - radius, rect.Y + radius, radius, rect.Height - radius * 2), color);

            // 绘制四个圆角
            DrawCorner(new Vector2(rect.X + radius, rect.Y + radius), radius, color, 180f);
            DrawCorner(new Vector2(rect.Right - radius, rect.Y + radius), radius, color, 270f);
            DrawCorner(new Vector2(rect.X + radius, rect.Bottom - radius), radius, color, 90f);
            DrawCorner(new Vector2(rect.Right - radius, rect.Bottom - radius), radius, color, 0f);
        }

        private void DrawCorner(Vector2 center, float radius, Color color, float startAngle)
        {
            const int segments = 6;
            var vertices = new Float2[(segments + 1) * 3];
            
            for (int i = 0; i < segments; i++)
            {
                float a1 = Mathf.DegreesToRadians * (startAngle + (i / (float)segments) * 90f);
                float a2 = Mathf.DegreesToRadians * (startAngle + ((i + 1) / (float)segments) * 90f);
                
                int idx = i * 3;
                vertices[idx] = center;
                vertices[idx + 1] = center + new Float2(Mathf.Cos(a1) * radius, Mathf.Sin(a1) * radius);
                vertices[idx + 2] = center + new Float2(Mathf.Cos(a2) * radius, Mathf.Sin(a2) * radius);
            }
            
            Render2D.FillTriangles(vertices, color);
        }
    }
}
