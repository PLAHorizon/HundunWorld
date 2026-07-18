using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.Ink;
using HundunWorld.Game.UI.StyleSystem;

namespace HundunWorld.Game.UI.Ink.Components
{
    /// <summary>
    /// 技能槽网格控件。
    /// 绘制 8 个常规技能槽（圆形墨色背景 + 金色边框 + 冷却扇形遮罩 + 快捷键标签 1-8）
    /// 以及 1 个奇术槽（尺寸更大，带脉冲光晕，快捷键标签 "Q"）。
    /// 奇术槽脉冲由 <see cref="Update"/> 中 <see cref="Mathf.Sin"/> 驱动 alpha 在 0.3~0.8 之间循环。
    /// </summary>
    public class InkSkillSlotGrid : ContainerControl
    {
        /// <summary>常规槽位尺寸（像素）</summary>
        private const float SlotSize = 56f;

        /// <summary>常规槽位间距（像素）</summary>
        private const float SlotGap = 8f;

        /// <summary>奇术槽位尺寸（像素）</summary>
        private const float QSlotSize = 72f;

        /// <summary>奇术槽与常规槽之间的间距（像素）</summary>
        private const float QSlotGap = 16f;

        /// <summary>槽位边框厚度</summary>
        private const float BorderThickness = 1f;

        /// <summary>冷却扇形分段数</summary>
        private const int SectorSegments = 32;

        /// <summary>圆形边框分段数</summary>
        private const int CircleSegments = 48;

        /// <summary>脉冲角速度（弧度/秒）</summary>
        private const float PulseSpeed = 3f;

        /// <summary>脉冲 alpha 中点</summary>
        private const float PulseAlphaMid = 0.55f;

        /// <summary>脉冲 alpha 幅度</summary>
        private const float PulseAlphaAmp = 0.25f;

        /// <summary>冷却扇形半透明遮罩色（朱红深色 × 0.6 alpha）</summary>
        private static readonly Color CooldownColor = new Color(
            InkWashTheme.VermilionDeep.R,
            InkWashTheme.VermilionDeep.G,
            InkWashTheme.VermilionDeep.B,
            0.6f);

        /// <summary>9 个槽位的冷却进度（0=无冷却，1=冷却中）。索引 0-7 为常规槽，8 为奇术槽</summary>
        private float[] _cooldowns = new float[9];

        /// <summary>9 个槽位的技能图标资产（可为 null）。索引 0-7 为常规槽，8 为奇术槽</summary>
        private Texture[] _icons = new Texture[9];

        /// <summary>奇术槽脉冲累计时间</summary>
        private float _pulseTime = 0f;

        /// <summary>奇术槽当前脉冲 alpha（0.3~0.8 之间循环）</summary>
        private float _qSlotAlpha = PulseAlphaMid;

        /// <summary>
        /// 构造函数：根据布局常量自动计算控件尺寸。
        /// </summary>
        public InkSkillSlotGrid()
        {
            BackgroundColor = Color.Transparent;
            ClipChildren = false;
            AutoFocus = false;
            UpdateBounds();
        }

        /// <summary>
        /// 设置槽位冷却进度。
        /// </summary>
        /// <param name="slotIndex">槽位索引：0-7 常规槽，8 奇术槽</param>
        /// <param name="progress">冷却进度 0-1，0=无冷却，1=冷却中</param>
        public void SetCooldown(int slotIndex, float progress)
        {
            if (slotIndex < 0 || slotIndex >= _cooldowns.Length)
                return;
            _cooldowns[slotIndex] = Mathf.Clamp(progress, 0f, 1f);
        }

        /// <summary>
        /// 设置槽位技能图标。
        /// </summary>
        /// <param name="slotIndex">槽位索引：0-7 常规槽，8 奇术槽</param>
        /// <param name="icon">图标纹理资产（可为 null，传 null 清除图标）</param>
        public void SetSkillIcon(int slotIndex, Texture icon)
        {
            if (slotIndex < 0 || slotIndex >= _icons.Length)
                return;
            _icons[slotIndex] = icon;
        }

        /// <inheritdoc />
        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);
            _pulseTime += deltaTime;
            // 用 Sin 驱动 alpha 在 0.3~0.8 之间循环（中点 0.55 + 幅度 0.25 × Sin）
            _qSlotAlpha = PulseAlphaMid + PulseAlphaAmp * Mathf.Sin(_pulseTime * PulseSpeed);
        }

        /// <inheritdoc />
        public override void Draw()
        {
            if (!Visible || Width <= 0f || Height <= 0f)
                return;

            // 1. 绘制 8 个常规槽（水平排列）
            for (int i = 0; i < 8; i++)
            {
                float x = i * (SlotSize + SlotGap);
                float y = (Height - SlotSize) * 0.5f;
                var center = new Float2(x + SlotSize * 0.5f, y + SlotSize * 0.5f);
                float radius = SlotSize * 0.5f;
                DrawSlot(center, radius, _cooldowns[i], _icons[i], (i + 1).ToString(), false, 1f);
            }

            // 2. 绘制奇术槽（第 9 个，索引 8）
            float qStartX = 8f * SlotSize + 7f * SlotGap + QSlotGap;
            float qY = (Height - QSlotSize) * 0.5f;
            var qCenter = new Float2(qStartX + QSlotSize * 0.5f, qY + QSlotSize * 0.5f);
            float qRadius = QSlotSize * 0.5f;
            DrawSlot(qCenter, qRadius, _cooldowns[8], _icons[8], "Q", true, _qSlotAlpha);
        }

        /// <summary>
        /// 绘制单个技能槽。
        /// </summary>
        /// <param name="center">槽位圆心</param>
        /// <param name="radius">槽位半径</param>
        /// <param name="cooldown">冷却进度 0-1</param>
        /// <param name="icon">图标资产（可为 null）</param>
        /// <param name="hotkey">快捷键标签文字</param>
        /// <param name="isQSlot">是否为奇术槽</param>
        /// <param name="pulseAlpha">奇术槽脉冲 alpha（非奇术槽忽略）</param>
        private void DrawSlot(Float2 center, float radius, float cooldown, Texture icon,
            string hotkey, bool isQSlot, float pulseAlpha)
        {
            if (radius <= 0f)
                return;

            // 0. 奇术槽脉冲外光晕
            if (isQSlot)
            {
                Color pulseGlow = new Color(
                    InkWashTheme.GoldBright.R,
                    InkWashTheme.GoldBright.G,
                    InkWashTheme.GoldBright.B,
                    pulseAlpha * 0.4f);
                InkRenderHelper.FillCircle(center, radius + 5f, pulseGlow);
            }

            // 1. 圆形墨色背景（BaseTertiary）
            InkRenderHelper.FillCircle(center, radius, InkWashTheme.BaseTertiary);

            // 2. 图标（居中，占槽位约 70%）
            if (icon != null && icon.IsLoaded)
            {
                var texture = icon as Texture;
                if (texture != null)
                {
                    float iconSize = radius * 1.4f;
                    var iconRect = new Rectangle(
                        center.X - iconSize * 0.5f,
                        center.Y - iconSize * 0.5f,
                        iconSize, iconSize);
                    Render2D.DrawTexture(texture, iconRect, Color.White);
                }
            }

            // 3. 冷却扇形遮罩（覆盖在图标之上）
            if (cooldown > 0f)
            {
                DrawCooldownSector(center, radius, cooldown, CooldownColor);
            }

            // 4. 金色边框（BorderGold）
            DrawCircleOutline(center, radius, InkWashTheme.BorderGold, BorderThickness);

            // 5. 快捷键标签（右下角对齐）
            var fontRef = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 12f);
            var font = fontRef.GetFont();
            if (font != null)
            {
                var labelRect = new Rectangle(
                    center.X - radius, center.Y - radius,
                    radius * 2f, radius * 2f);
                Render2D.DrawText(
                    font, hotkey, labelRect,
                    InkWashTheme.TextDefault,
                    TextAlignment.Far, TextAlignment.Far,
                    TextWrapping.NoWrap);
            }
        }

        /// <summary>
        /// 绘制冷却扇形遮罩。从正上方开始顺时针覆盖 progress 比例的扇形区域。
        /// 使用多段三角形扇近似（<see cref="Render2D.FillTriangles"/>）。
        /// </summary>
        /// <param name="center">圆心</param>
        /// <param name="radius">半径</param>
        /// <param name="progress">覆盖比例 0-1</param>
        /// <param name="color">遮罩颜色</param>
        private static void DrawCooldownSector(Float2 center, float radius, float progress, Color color)
        {
            if (progress <= 0f || radius <= 0f)
                return;

            // progress >= 1 时直接填充整圆
            if (progress >= 1f)
            {
                InkRenderHelper.FillCircle(center, radius, color);
                return;
            }

            int segments = Mathf.CeilToInt(progress * SectorSegments);
            if (segments < 1)
                segments = 1;

            // 从正上方（-π/2）开始顺时针覆盖
            float startAngle = -Mathf.Pi * 0.5f;
            float totalAngle = progress * Mathf.TwoPi;

            var vertices = new Float2[segments * 3];
            for (int i = 0; i < segments; i++)
            {
                float t1 = (float)i / segments;
                float t2 = (float)(i + 1) / segments;
                float a1 = startAngle + t1 * totalAngle;
                float a2 = startAngle + t2 * totalAngle;
                int idx = i * 3;
                vertices[idx] = center;
                vertices[idx + 1] = center + new Float2(Mathf.Cos(a1) * radius, Mathf.Sin(a1) * radius);
                vertices[idx + 2] = center + new Float2(Mathf.Cos(a2) * radius, Mathf.Sin(a2) * radius);
            }
            Render2D.FillTriangles(vertices, color);
        }

        /// <summary>
        /// 使用多段 <see cref="Render2D.DrawLine"/> 近似绘制圆形描边。
        /// </summary>
        /// <param name="center">圆心</param>
        /// <param name="radius">半径</param>
        /// <param name="color">描边颜色</param>
        /// <param name="thickness">线宽</param>
        private static void DrawCircleOutline(Float2 center, float radius, Color color, float thickness)
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
        /// 根据布局常量计算并设置控件尺寸。
        /// 总宽度 = 8 常规槽 + 7 间距 + 奇术间距 + 1 奇术槽
        /// 高度 = max(常规槽尺寸, 奇术槽尺寸)
        /// </summary>
        private void UpdateBounds()
        {
            float width = 8f * SlotSize + 7f * SlotGap + QSlotGap + QSlotSize;
            float height = Mathf.Max(SlotSize, QSlotSize);
            Size = new Float2(width, height);
        }
    }
}
