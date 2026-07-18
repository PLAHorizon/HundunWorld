using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.Ink;
using HundunWorld.Game.UI.StyleSystem;

namespace HundunWorld.Game.UI.Ink.Components
{
    /// <summary>
    /// 小地图实体类型枚举。
    /// 用于 <see cref="InkMinimap.AddEntity"/> 标记实体阵营，
    /// 不同阵营绘制不同颜色的点位。
    /// </summary>
    public enum InkMinimapEntityType
    {
        /// <summary>玩家本体 — 鎏金主色</summary>
        Player,

        /// <summary>友方 — 翡翠亮色</summary>
        Friendly,

        /// <summary>敌方 — 朱红亮色</summary>
        Enemy,

        /// <summary>NPC — 鎏金亮色</summary>
        NPC
    }

    /// <summary>
    /// 圆形墨色边框小地图。
    /// 在 <see cref="Draw"/> 中绘制圆形背景（BaseSecondary）+ 金色边框（BorderGold），
    /// 并按 <see cref="AddEntity"/> 添加的实体点位绘制对应颜色的小圆点。
    /// 实体坐标 relativeX/relativeZ 范围为 -1~1，线性映射到小地图半径内。
    /// </summary>
    public class InkMinimap : ContainerControl
    {
        /// <summary>圆形边框的分段数（越大越圆滑）</summary>
        private const int CircleSegments = 64;

        /// <summary>边框厚度（像素）</summary>
        private const float BorderThickness = 2f;

        /// <summary>实体点位距边框的内边距，避免点位贴边</summary>
        private const float EntityPadding = 8f;

        /// <summary>常规实体点半径（像素）</summary>
        private const float EntityDotRadius = 4f;

        /// <summary>玩家实体点半径（像素），略大以突出主体</summary>
        private const float PlayerDotRadius = 5f;

        /// <summary>地形兴趣点半径（像素）</summary>
        private const float LandmarkRadius = 2.5f;

        /// <summary>实体列表（类型 + 归一化坐标 x/z，范围 -1~1）</summary>
        private System.Collections.Generic.List<(InkMinimapEntityType type, float x, float z)> _entities
            = new System.Collections.Generic.List<(InkMinimapEntityType, float, float)>();

        /// <summary>地形快照/兴趣点列表（相对坐标 + 半径 + 颜色）</summary>
        private System.Collections.Generic.List<(float x, float z, float radius, Color color)> _landmarks
            = new System.Collections.Generic.List<(float, float, float, Color)>();

        /// <summary>当前玩家朝向角度（度），0=正北，顺时针增加</summary>
        public float PlayerYaw { get; set; } = 0f;

        /// <summary>
        /// 构造函数：透明背景、不裁剪子控件。
        /// 默认尺寸 160x160，可由外部修改 <see cref="Control.Size"/>。
        /// </summary>
        public InkMinimap()
        {
            BackgroundColor = Color.Transparent;
            ClipChildren = false;
            Size = new Float2(160f, 160f);
            AutoFocus = false;
        }

        /// <summary>
        /// 添加一个实体点位。
        /// 坐标会被钳制到 -1~1 范围，绘制时线性映射到小地图半径内。
        /// </summary>
        /// <param name="type">实体类型（决定点位颜色）</param>
        /// <param name="relativeX">相对 X 坐标，-1=最左，1=最右</param>
        /// <param name="relativeZ">相对 Z 坐标，-1=最上，1=最下（映射到屏幕 Y 轴）</param>
        public void AddEntity(InkMinimapEntityType type, float relativeX, float relativeZ)
        {
            float x = Mathf.Clamp(relativeX, -1f, 1f);
            float z = Mathf.Clamp(relativeZ, -1f, 1f);
            _entities.Add((type, x, z));
        }

        /// <summary>
        /// 清除所有已添加的实体点位。
        /// </summary>
        public void ClearEntities()
        {
            _entities.Clear();
        }

        /// <summary>
        /// 添加一个地形快照/兴趣点。
        /// 用于在地图上绘制地形块（水域、山地、建筑等）。
        /// </summary>
        /// <param name="relativeX">相对 X 坐标，-1=最左，1=最右</param>
        /// <param name="relativeZ">相对 Z 坐标，-1=最上，1=最下</param>
        /// <param name="radius">相对半径（0~1），决定地形块在小地图上的大小</param>
        /// <param name="color">地形块颜色</param>
        public void AddLandmark(float relativeX, float relativeZ, float radius, Color color)
        {
            _landmarks.Add((
                Mathf.Clamp(relativeX, -1f, 1f),
                Mathf.Clamp(relativeZ, -1f, 1f),
                Mathf.Clamp(radius, 0.02f, 1f),
                color));
        }

        /// <summary>
        /// 清除所有地形快照/兴趣点。
        /// </summary>
        public void ClearLandmarks()
        {
            _landmarks.Clear();
        }

        /// <inheritdoc />
        public override void Draw()
        {
            if (!Visible || Width <= 0f || Height <= 0f)
                return;

            var center = new Float2(Width * 0.5f, Height * 0.5f);
            float radius = Mathf.Min(Width, Height) * 0.5f;
            if (radius <= 0f)
                return;

            float innerRadius = Mathf.Max(0f, radius - BorderThickness);
            float entityRadius = Mathf.Max(0f, radius - BorderThickness - EntityPadding);

            // 1. 外圈阴影（突出层次感）
            InkRenderHelper.FillCircle(center, radius + 2f, new Color(0f, 0f, 0f, 0.35f));

            // 2. 圆形背景（BaseSecondary 带 90% 不透明度）
            Color bgColor = new Color(
                InkWashTheme.BaseSecondary.R,
                InkWashTheme.BaseSecondary.G,
                InkWashTheme.BaseSecondary.B,
                0.92f);
            InkRenderHelper.FillCircle(center, radius, bgColor);

            // 3. 方位标记圈（东南西北四个方位点）
            DrawCompassMarks(center, radius);

            // 4. 同心圆网格（3 圈）
            DrawRangeRings(center, innerRadius);

            // 5. 地形快照 / 兴趣点（半透明块）
            for (int i = 0; i < _landmarks.Count; i++)
            {
                var lm = _landmarks[i];
                float px = center.X + lm.x * entityRadius;
                float py = center.Y + lm.z * entityRadius;
                var pos = new Float2(px, py);
                float lmR = Mathf.Max(1f, lm.radius * entityRadius);
                InkRenderHelper.FillCircle(pos, lmR, lm.color);
            }

            // 6. 实体点位
            for (int i = 0; i < _entities.Count; i++)
            {
                var entity = _entities[i];
                float px = center.X + entity.x * entityRadius;
                float py = center.Y + entity.z * entityRadius;
                var pos = new Float2(px, py);

                Color color = GetEntityColor(entity.type);
                float dotRadius = entity.type == InkMinimapEntityType.Player
                    ? PlayerDotRadius
                    : EntityDotRadius;

                // 玩家点位附加外发光与方向三角
                if (entity.type == InkMinimapEntityType.Player)
                {
                    Color glow = new Color(color.R, color.G, color.B, 0.35f);
                    InkRenderHelper.FillCircle(pos, dotRadius + 4f, glow);
                    DrawPlayerDirection(pos, dotRadius + 2f, PlayerYaw);
                }

                InkRenderHelper.FillCircle(pos, dotRadius, color);
            }

            // 7. 金色边框（最上层，覆盖所有内容）
            DrawCircleOutline(center, radius, InkWashTheme.BorderGold, BorderThickness);

            // 8. 方向标签（N/E/S/W）
            DrawDirectionLabels(center, radius);
        }

        /// <summary>
        /// 绘制东南西北四个方位标记。
        /// </summary>
        private static void DrawCompassMarks(Float2 center, float radius)
        {
            float markRadius = radius - 6f;
            if (markRadius <= 0f)
                return;

            float markSize = 2.5f;
            Color markColor = InkWashTheme.BorderNeutralL3;

            // 北
            InkRenderHelper.FillCircle(center + new Float2(0f, -markRadius), markSize, markColor);
            // 南
            InkRenderHelper.FillCircle(center + new Float2(0f, markRadius), markSize, markColor);
            // 东
            InkRenderHelper.FillCircle(center + new Float2(markRadius, 0f), markSize, markColor);
            // 西
            InkRenderHelper.FillCircle(center + new Float2(-markRadius, 0f), markSize, markColor);
        }

        /// <summary>
        /// 绘制同心圆范围网格。
        /// </summary>
        private static void DrawRangeRings(Float2 center, float radius)
        {
            if (radius <= 0f)
                return;

            Color ringColor = InkWashTheme.BorderNeutralL3;

            for (int i = 1; i <= 3; i++)
            {
                float r = radius * (i / 4f);
                DrawCircleOutline(center, r, ringColor, 1f);
            }

            // 十字方向线
            Render2D.DrawLine(center + new Float2(-radius, 0f), center + new Float2(radius, 0f), ringColor, 1f);
            Render2D.DrawLine(center + new Float2(0f, -radius), center + new Float2(0f, radius), ringColor, 1f);
        }

        /// <summary>
        /// 绘制玩家朝向三角。
        /// </summary>
        private static void DrawPlayerDirection(Float2 center, float radius, float yawDeg)
        {
            if (radius <= 0f)
                return;

            float angleRad = Mathf.DegreesToRadians * yawDeg;
            float dirX = Mathf.Sin(angleRad);
            float dirY = -Mathf.Cos(angleRad);

            float tipLen = radius + 3f;
            float baseHalf = radius * 0.6f;

            var tip = center + new Float2(dirX * tipLen, dirY * tipLen);
            var baseCenter = center + new Float2(dirX * radius * 0.3f, dirY * radius * 0.3f);
            var perp = new Float2(-dirY, dirX) * baseHalf;
            var base1 = baseCenter + perp;
            var base2 = baseCenter - perp;

            var vertices = new Float2[] { tip, base1, base2 };
            Render2D.FillTriangles(vertices, InkWashTheme.GoldBright);
        }

        /// <summary>
        /// 绘制北/东/南/西方位文字标签。
        /// </summary>
        private static void DrawDirectionLabels(Float2 center, float radius)
        {
            if (radius <= 24f)
                return;

            var font = InkWashTheme.GetFont(InkWashTheme.FontRole.Heading);
            if (font == null)
                return;

            var fontRef = new FontReference(font, 9f);
            float labelOffset = radius - 10f;
            Color color = InkWashTheme.TextBrand;

            DrawLabel(fontRef, center + new Float2(0f, -labelOffset), "北", color);
            DrawLabel(fontRef, center + new Float2(0f, labelOffset), "南", color);
            DrawLabel(fontRef, center + new Float2(labelOffset, 0f), "东", color);
            DrawLabel(fontRef, center + new Float2(-labelOffset, 0f), "西", color);
        }

        private static void DrawLabel(FontReference fontRef, Float2 center, string text, Color color)
        {
            float size = 12f;
            var rect = new Rectangle(center.X - size * 0.5f, center.Y - size * 0.5f, size, size);
            var font = fontRef.GetFont();
            if (font != null)
            {
                Render2D.DrawText(font, text, rect, color,
                    TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
            }
        }

        /// <summary>
        /// 根据实体类型返回对应点位颜色。
        /// </summary>
        /// <param name="type">实体类型</param>
        /// <returns>对应主题色</returns>
        private static Color GetEntityColor(InkMinimapEntityType type)
        {
            return type switch
            {
                InkMinimapEntityType.Player => InkWashTheme.GoldPrimary,
                InkMinimapEntityType.Friendly => InkWashTheme.JadeBright,
                InkMinimapEntityType.Enemy => InkWashTheme.VermilionBright,
                InkMinimapEntityType.NPC => InkWashTheme.GoldBright,
                _ => InkWashTheme.GoldBright
            };
        }

        /// <summary>
        /// 使用多段 <see cref="Render2D.DrawLine"/> 近似绘制圆环描边。
        /// </summary>
        /// <param name="center">圆心</param>
        /// <param name="radius">圆半径</param>
        /// <param name="color">描边颜色</param>
        /// <param name="thickness">线宽</param>
        private static void DrawCircleOutline(Float2 center, float radius, Color color, float thickness)
        {
            if (radius <= 0f)
                return;

            float angleStep = Mathf.TwoPi / CircleSegments;
            for (int i = 0; i < CircleSegments; i++)
            {
                float a1 = i * angleStep;
                float a2 = (i + 1) * angleStep;
                var p1 = center + new Float2(Mathf.Cos(a1) * radius, Mathf.Sin(a1) * radius);
                var p2 = center + new Float2(Mathf.Cos(a2) * radius, Mathf.Sin(a2) * radius);
                Render2D.DrawLine(p1, p2, color, thickness);
            }
        }
    }
}
