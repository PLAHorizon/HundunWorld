using FlaxEngine;
using FlaxEngine.GUI;

namespace HundunWorld.Game.UI.Components
{
    /// <summary>
    /// 角色投影圆盘
    /// 在 3D 角色脚下绘制一个半透明水平椭圆阴影,营造角色"站立"在地面上的视觉效果。
    /// 形状通过 32 条垂直短线沿水平轴采样绘出,无需外部贴图。
    /// </summary>
    public class CharacterGroundDisc : Panel
    {
        /// <summary>
        /// 椭圆外圈颜色(默认深灰,30% 透明)
        /// </summary>
        public Color DiscColor { get; set; } = new Color(0f, 0f, 0f, 0.3f);

        public CharacterGroundDisc()
        {
            BackgroundColor = Color.Transparent;
            Size = new Float2(120f, 30f);
        }

        /// <summary>
        /// 设置控件的屏幕位置(相对于父控件)
        /// </summary>
        /// <param name="screenPos">屏幕坐标(父控件本地坐标系)</param>
        public void SetScreenPosition(Float2 screenPos)
        {
            // 让圆盘中心对齐到指定位置(以椭圆中心为锚点)
            Location = new Float2(
                screenPos.X - Size.X * 0.5f,
                screenPos.Y - Size.Y * 0.5f);
        }

        /// <inheritdoc />
        public override void DrawSelf()
        {
            base.DrawSelf();

            // 用多条垂直短线沿水平轴铺出椭圆阴影(避免使用不存在的 FillEllipse)
            const int sampleCount = 32;
            float cx = Size.X * 0.5f;
            float cy = Size.Y * 0.5f;
            float rx = cx;
            float ry = cy;
            for (int i = 0; i < sampleCount; i++)
            {
                float t = (i + 0.5f) / sampleCount;
                float x = -rx + t * Size.X;
                // 椭圆方程: y_radius = ry * sqrt(1 - (x/rx)^2)
                float ratio = x / rx;
                if (ratio > 1f) ratio = 1f;
                if (ratio < -1f) ratio = -1f;
                float halfHeight = ry * Mathf.Sqrt(Mathf.Max(0f, 1f - ratio * ratio));
                var top = new Float2(x, cy - halfHeight);
                var bottom = new Float2(x, cy + halfHeight);
                Render2D.DrawLine(top, bottom, DiscColor, 1.5f);
            }
        }
    }
}
