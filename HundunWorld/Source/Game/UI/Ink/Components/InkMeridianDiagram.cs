using System;
using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.Ink;
using HundunWorld.Game.UI.StyleSystem;

namespace HundunWorld.Game.UI.Ink.Components
{
    /// <summary>
    /// 经脉图控件。
    /// 绘制 SVG 风格的简化人体轮廓 + 8 个可点击穴位点（含穴位名标签、金色光晕、
    /// 选中态朱红扩散环）。
    /// 穴位点位置使用归一化坐标（0-1）存储，绘制时映射到控件实际尺寸。
    /// 点击穴位触发 <see cref="AcupointClicked"/> 事件，可通过
    /// <see cref="SetActiveAcupoint"/> 高亮指定穴位。
    /// 视觉规格对齐设计稿 <c>acupoint.html</c>：
    /// <list type="bullet">
    ///   <item>普通穴位 — 金色核心点（10px）+ 金色径向光晕（36px）+ 悬停放大 1.2 倍</item>
    ///   <item>选中穴位 — 朱红核心点（14px）+ 朱红光晕 + 2s 周期扩散环（scale 0.8→1.8、opacity 0.8→0）</item>
    ///   <item>穴位名标签 — 距点 18px、宣纸色 11px 书法字、深墨底 + 金边；仅悬停或选中时显示（同设计稿 hover 显隐），
    ///         可用 <see cref="AlwaysShowLabels"/> 改为常显</item>
    /// </list>
    /// </summary>
    public class InkMeridianDiagram : ContainerControl
    {
        /// <summary>8 个穴位名称（索引 0-7 对应穴位顺序）</summary>
        public static readonly string[] AcupointNames =
        {
            "百会", "太阳", "风池", "膻中", "神阙", "合谷", "关元", "涌泉"
        };

        /// <summary>
        /// 8 个穴位的归一化坐标 (x, y)，范围 0-1（相对控件尺寸）。
        /// 取值直接对应设计稿 <c>acupoint.html</c> 各穴位点的 <c>top</c>/<c>left</c> 百分比。
        /// </summary>
        private static readonly Float2[] AcupointPositions =
        {
            new Float2(0.50f, 0.07f), // 0 百会穴  top 7%  left 50%
            new Float2(0.26f, 0.13f), // 1 太阳穴  top 13% left 26%
            new Float2(0.58f, 0.16f), // 2 风池穴  top 16% left 58%
            new Float2(0.48f, 0.31f), // 3 膻中穴  top 31% left 48%
            new Float2(0.48f, 0.43f), // 4 神阙穴  top 43% left 48%
            new Float2(0.74f, 0.46f), // 5 合谷穴  top 46% left 74%
            new Float2(0.48f, 0.51f), // 6 关元穴  top 51% left 48%
            new Float2(0.47f, 0.91f)  // 7 涌泉穴  top 91% left 47%
        };

        /// <summary>椭圆轮廓分段数</summary>
        private const int EllipseSegments = 32;

        /// <summary>人体轮廓线宽（像素）</summary>
        private const float BodyLineWidth = 1.5f;

        /// <summary>普通穴位核心半径（设计稿 .acupoint-core 10px 直径）</summary>
        private const float AcupointRadius = 5f;

        /// <summary>选中穴位核心半径（设计稿 .acupoint-core-selected 14px 直径）</summary>
        private const float SelectedCoreRadius = 7f;

        /// <summary>穴位光晕半径（设计稿 .acupoint-glow 36px 直径）</summary>
        private const float ActiveGlowRadius = 18f;

        /// <summary>选中扩散环基准半径（设计稿 .acupoint-selected-ring 26px 直径）</summary>
        private const float SelectedRingRadius = 13f;

        /// <summary>选中扩散环线宽（设计稿 1.5px）</summary>
        private const float SelectedRingThickness = 1.5f;

        /// <summary>选中扩散环脉动周期（设计稿 acupointRingPulse 2s）</summary>
        private const float RingPulsePeriod = 2f;

        /// <summary>悬停时穴位点放大系数（设计稿 .acupoint-point:hover scale(1.2)）</summary>
        private const float HoverScale = 1.2f;

        /// <summary>穴位名标签距穴位中心的水平偏移（设计稿 .acupoint-label left: 18px）</summary>
        private const float LabelOffsetX = 18f;

        /// <summary>穴位名标签字号（设计稿 .acupoint-label 11px）</summary>
        private const float LabelFontSize = 11f;

        /// <summary>穴位名标签内边距（设计稿 padding: 2px 8px）</summary>
        private const float LabelPaddingX = 8f;

        /// <summary>穴位名标签圆角半径（设计稿 var(--ink-radius-sm) = 2px）</summary>
        private const float LabelRadius = 2f;

        /// <summary>点击命中半径（像素）</summary>
        private const float HitRadius = 14f;

        /// <summary>当前活动穴位索引（-1 表示无高亮）</summary>
        private int _activeAcupoint = -1;

        /// <summary>当前悬停穴位索引（-1 表示无悬停）</summary>
        private int _hoverAcupoint = -1;

        /// <summary>选中扩散环的脉动相位（0 ~ RingPulsePeriod）</summary>
        private float _ringPhase;

        /// <summary>
        /// 是否常显全部穴位名标签。
        /// 默认 false — 与设计稿一致，仅悬停或选中时显示标签；
        /// 设为 true 时 8 个穴位名常显（无鼠标交互的平台可用）。
        /// </summary>
        public bool AlwaysShowLabels { get; set; }

        /// <summary>
        /// 穴位点击事件。参数为被点击的穴位索引（0-7）。
        /// </summary>
        public event Action<int> AcupointClicked;

        /// <summary>
        /// 构造函数：默认尺寸 200x400，透明背景。
        /// </summary>
        public InkMeridianDiagram()
        {
            BackgroundColor = Color.Transparent;
            ClipChildren = false;
            Size = new Float2(200f, 400f);
            AutoFocus = false;
        }

        /// <summary>
        /// 设置活动穴位索引（高亮指定穴位）。
        /// </summary>
        /// <param name="index">穴位索引 0-7，传 -1 取消高亮</param>
        public void SetActiveAcupoint(int index)
        {
            if (index >= -1 && index < AcupointNames.Length)
            {
                _activeAcupoint = index;
                // 切换选中穴位时重置脉动相位，使扩散环从最小态重新扩散（同设计稿 :hover/选中重置动画）。
                _ringPhase = 0f;
            }
        }

        /// <inheritdoc />
        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);

            // 推进选中扩散环相位（设计稿 acupointRingPulse 2s 无限循环）。
            if (_activeAcupoint >= 0 && RingPulsePeriod > 0f && deltaTime > 0f)
            {
                _ringPhase += deltaTime;
                while (_ringPhase >= RingPulsePeriod)
                    _ringPhase -= RingPulsePeriod;
            }
        }

        /// <inheritdoc />
        public override void Draw()
        {
            if (!Visible || Width <= 0f || Height <= 0f)
                return;

            // 1. 绘制人体轮廓
            DrawBodyOutline();

            // 2. 绘制穴位点（光晕 → 核心 → 扩散环 → 穴位名标签）
            for (int i = 0; i < AcupointPositions.Length; i++)
            {
                var pos = new Float2(
                    AcupointPositions[i].X * Width,
                    AcupointPositions[i].Y * Height);

                bool isActive = (i == _activeAcupoint);
                bool isHovered = (i == _hoverAcupoint);
                float scale = isHovered ? HoverScale : 1f;

                if (isActive)
                {
                    // 朱红径向光晕（设计稿 .acupoint-glow-vermilion：rgba(192,57,43,0.65) → transparent）
                    var halo = new Color(
                        InkWashTheme.VermilionPrimary.R,
                        InkWashTheme.VermilionPrimary.G,
                        InkWashTheme.VermilionPrimary.B,
                        0.65f);
                    InkRenderHelper.FillRadialGradient(pos, ActiveGlowRadius * scale, halo, Color.Transparent);

                    // 扩散环（scale 0.8→1.8、opacity 0.8→0）
                    float t = RingPulsePeriod > 0f ? _ringPhase / RingPulsePeriod : 0f;
                    var ringColor = new Color(
                        InkWashTheme.VermilionPrimary.R,
                        InkWashTheme.VermilionPrimary.G,
                        InkWashTheme.VermilionPrimary.B,
                        Mathf.Lerp(0.8f, 0f, t));
                    InkRenderHelper.DrawCircle(
                        pos,
                        SelectedRingRadius * Mathf.Lerp(0.8f, 1.8f, t),
                        ringColor,
                        SelectedRingThickness);

                    // 朱红核心点（14px + 1px 朱红描边）
                    InkRenderHelper.FillCircle(pos, SelectedCoreRadius * scale, InkWashTheme.VermilionBright);
                    InkRenderHelper.DrawCircle(pos, SelectedCoreRadius * scale, InkWashTheme.VermilionPrimary, 1f);
                }
                else
                {
                    // 金色径向光晕（设计稿 .acupoint-glow：rgba(200,168,88,0.55) → transparent，悬停加深）
                    var halo = new Color(
                        InkWashTheme.GoldPrimary.R,
                        InkWashTheme.GoldPrimary.G,
                        InkWashTheme.GoldPrimary.B,
                        isHovered ? 0.55f : 0.35f);
                    InkRenderHelper.FillRadialGradient(pos, ActiveGlowRadius * scale, halo, Color.Transparent);

                    // 金色核心点（10px + 1px 金边）
                    InkRenderHelper.FillCircle(pos, AcupointRadius * scale, InkWashTheme.GoldBright);
                    InkRenderHelper.DrawCircle(pos, AcupointRadius * scale, InkWashTheme.GoldPrimary, 1f);
                }

                // 穴位名标签：默认仅悬停/选中显示（设计稿 .acupoint-label opacity 0 → hover/active 1）
                if (AlwaysShowLabels || isActive || isHovered)
                    DrawAcupointLabel(pos, AcupointNames[i] + "穴", isActive);
            }
        }

        /// <summary>
        /// 绘制穴位名标签。
        /// 对应设计稿 <c>.acupoint-label</c>：位于穴位点右侧 18px、垂直居中，
        /// 深墨底（rgba(10,11,16,0.82)，选中 0.9）+ 金色 1px 描边 + 2px 圆角，
        /// 普通态宣纸做旧色、选中态朱红亮色。
        /// </summary>
        /// <param name="pointPos">穴位点中心坐标（控件局部坐标系）</param>
        /// <param name="text">穴位名（如 "太阳穴"）</param>
        /// <param name="isActive">是否为当前选中穴位（决定标签配色）</param>
        private void DrawAcupointLabel(Float2 pointPos, string text, bool isActive)
        {
            if (string.IsNullOrEmpty(text))
                return;

            var font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, LabelFontSize).GetFont();
            if (font == null)
                return;

            // 标签尺寸：CJK 字宽约等于字号，加左右内边距
            float w = LabelFontSize * text.Length + LabelPaddingX * 2f;
            float h = LabelFontSize + 4f;
            var rect = new Rectangle(pointPos.X + LabelOffsetX, pointPos.Y - h * 0.5f, w, h);

            var background = new Color(10f / 255f, 11f / 255f, 16f / 255f, isActive ? 0.9f : 0.82f);
            var border = isActive ? InkWashTheme.VermilionDeep : InkWashTheme.BorderGold;
            var textColor = isActive ? InkWashTheme.VermilionBright : InkWashTheme.PaperAged;

            InkRenderHelper.FillRoundedRectangle(rect, LabelRadius, background);
            InkRenderHelper.DrawRoundedRectangle(rect, LabelRadius, border, 1f);
            Render2D.DrawText(
                font,
                text,
                rect,
                textColor,
                TextAlignment.Center,
                TextAlignment.Center,
                TextWrapping.NoWrap);
        }

        /// <summary>
        /// 绘制简化人体轮廓（头/颈/躯干/四肢）。
        /// 颜色使用 <see cref="InkWashTheme.TextTertiary"/>，线宽 <see cref="BodyLineWidth"/>。
        /// </summary>
        private void DrawBodyOutline()
        {
            Color lineColor = InkWashTheme.TextTertiary;

            // 头部：椭圆轮廓
            var headCenter = new Float2(0.5f * Width, 0.13f * Height);
            float headRx = 0.07f * Width;
            float headRy = 0.05f * Height;
            DrawEllipseOutline(headCenter, headRx, headRy, lineColor, BodyLineWidth);

            // 颈部
            DrawBodyLine(0.5f, 0.18f, 0.5f, 0.22f, lineColor);
            // 脊柱（躯干主干）
            DrawBodyLine(0.5f, 0.22f, 0.5f, 0.58f, lineColor);
            // 肩部
            DrawBodyLine(0.38f, 0.24f, 0.62f, 0.24f, lineColor);
            // 左臂（从左肩斜向下）
            DrawBodyLine(0.38f, 0.24f, 0.28f, 0.42f, lineColor);
            // 右臂（从右肩斜向下）
            DrawBodyLine(0.62f, 0.24f, 0.72f, 0.42f, lineColor);
            // 髋部
            DrawBodyLine(0.42f, 0.58f, 0.58f, 0.58f, lineColor);
            // 左腿（从左髋向下）
            DrawBodyLine(0.45f, 0.58f, 0.42f, 0.95f, lineColor);
            // 右腿（从右髋向下）
            DrawBodyLine(0.55f, 0.58f, 0.58f, 0.95f, lineColor);
        }

        /// <summary>
        /// 按归一化坐标绘制一段人体轮廓线。
        /// </summary>
        /// <param name="x1">起点归一化 X</param>
        /// <param name="y1">起点归一化 Y</param>
        /// <param name="x2">终点归一化 X</param>
        /// <param name="y2">终点归一化 Y</param>
        /// <param name="color">线颜色</param>
        private void DrawBodyLine(float x1, float y1, float x2, float y2, Color color)
        {
            var p1 = new Float2(x1 * Width, y1 * Height);
            var p2 = new Float2(x2 * Width, y2 * Height);
            Render2D.DrawLine(p1, p2, color, BodyLineWidth);
        }

        /// <summary>
        /// 使用多段 <see cref="Render2D.DrawLine"/> 近似绘制椭圆轮廓。
        /// </summary>
        /// <param name="center">椭圆中心</param>
        /// <param name="rx">水平半径</param>
        /// <param name="ry">垂直半径</param>
        /// <param name="color">描边颜色</param>
        /// <param name="thickness">线宽</param>
        private static void DrawEllipseOutline(Float2 center, float rx, float ry, Color color, float thickness)
        {
            if (rx <= 0f || ry <= 0f)
                return;

            float step = Mathf.TwoPi / EllipseSegments;
            for (int i = 0; i < EllipseSegments; i++)
            {
                float a1 = i * step;
                float a2 = (i + 1) * step;
                var p1 = center + new Float2(Mathf.Cos(a1) * rx, Mathf.Sin(a1) * ry);
                var p2 = center + new Float2(Mathf.Cos(a2) * rx, Mathf.Sin(a2) * ry);
                Render2D.DrawLine(p1, p2, color, thickness);
            }
        }

        /// <inheritdoc />
        public override bool OnMouseDown(Float2 location, MouseButton button)
        {
            base.OnMouseDown(location, button);

            if (button != MouseButton.Left)
                return false;

            // 检测点击命中哪个穴位点（距离平方比较，避免开方）
            float hitRadiusSq = HitRadius * HitRadius;
            for (int i = 0; i < AcupointPositions.Length; i++)
            {
                var pos = new Float2(
                    AcupointPositions[i].X * Width,
                    AcupointPositions[i].Y * Height);
                float dx = location.X - pos.X;
                float dy = location.Y - pos.Y;
                if (dx * dx + dy * dy <= hitRadiusSq)
                {
                    AcupointClicked?.Invoke(i);
                    return true;
                }
            }

            return false;
        }

        /// <inheritdoc />
        public override void OnMouseMove(Float2 location)
        {
            base.OnMouseMove(location);

            // 悬停探测：命中穴位点则显示其名称标签并放大 1.2 倍（设计稿 .acupoint-point:hover）
            int hovered = HitTestAcupoint(location);
            if (hovered != _hoverAcupoint)
                _hoverAcupoint = hovered;
        }

        /// <inheritdoc />
        public override void OnMouseLeave()
        {
            base.OnMouseLeave();
            _hoverAcupoint = -1;
        }

        /// <summary>
        /// 命中测试：返回位于 <paramref name="location"/> 命中半径内的穴位索引。
        /// </summary>
        /// <param name="location">控件局部坐标</param>
        /// <returns>命中的穴位索引；未命中返回 -1</returns>
        private int HitTestAcupoint(Float2 location)
        {
            float hitRadiusSq = HitRadius * HitRadius;
            for (int i = 0; i < AcupointPositions.Length; i++)
            {
                var pos = new Float2(
                    AcupointPositions[i].X * Width,
                    AcupointPositions[i].Y * Height);
                float dx = location.X - pos.X;
                float dy = location.Y - pos.Y;
                if (dx * dx + dy * dy <= hitRadiusSq)
                    return i;
            }

            return -1;
        }
    }
}
