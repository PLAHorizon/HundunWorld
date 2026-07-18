using System;
using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI;
using HundunWorld.Game.UI.StyleSystem;

namespace HundunWorld.Game.UI.Ink.Components
{
    /// <summary>
    /// 六边形雷达图叠加控件。
    /// 在同一坐标系中叠加两组六边形数据多边形：
    /// 外层为五行（金、木、水、火、土、中和）数据，使用五行元素色与多层发光描边；
    /// 内层为关键属性（攻击、防御、气血、暴击、命中、闪避）数据，使用金色半透明填充。
    /// 绘制风格参考 <see cref="HundunWorld.Game.UI.Components.WuxingRadarChart"/>
    /// （魔兽世界金属蚀刻感 + 多层发光描边）。
    /// 支持插值动画、顶点悬停提示与顶点标签框。
    /// </summary>
    public class HexRadarChartOverlay : Control
    {
        private const int SideCount = 6;
        private const int GridLevels = 5;
        private const float Padding = 58f;
        private const float LabelOffset = 16f;
        private const float MarkerSize = 6f;
        private const float GridLineThickness = 1.2f;
        private const float OuterGlowThickness = 5f;
        private const float MidGlowThickness = 2.5f;
        private const float DataLineThickness = 2f;
        private const float InnerDataLineThickness = 1.2f;
        private const float AnimationSpeed = 8f;
        private const float InnerRadiusRatio = 0.68f;
        private const float HoverDistance = 20f;
        private const float HoverDistanceSq = HoverDistance * HoverDistance;
        private const float OuterLabelBoxW = 54f;
        private const float OuterLabelBoxH = 34f;
        private const float InnerLabelBoxW = 46f;
        private const float InnerLabelBoxH = 24f;

        private readonly float[] _targetWuxingValues = new float[SideCount];
        private readonly float[] _displayWuxingValues = new float[SideCount];
        private readonly float[] _targetKeyAttrValues = new float[SideCount];
        private readonly float[] _displayKeyAttrValues = new float[SideCount];

        private readonly Color[] _elementColors;
        private readonly string[] _wuxingNames = { "金", "木", "水", "火", "土", "中和" };
        private readonly string[] _keyAttrNames = { "攻击", "防御", "气血", "暴击", "命中", "闪避" };

        /// <summary>用于悬停检测的外层五行数据点缓存（控件本地坐标）</summary>
        private readonly Float2[] _wuxingDataPoints = new Float2[SideCount];

        /// <summary>用于悬停检测的内层关键属性数据点缓存（控件本地坐标）</summary>
        private readonly Float2[] _keyAttrDataPoints = new Float2[SideCount];

        /// <summary>雷达图最大数值，所有数据按此值归一化到 [0,1]。</summary>
        public float MaxValue { get; set; } = 100f;

        /// <summary>
        /// 顶点悬停提示请求事件。
        /// 参数1：组别（0=五行，1=关键属性）；
        /// 参数2：属性索引（0..5）；
        /// 参数3：鼠标位置（控件本地坐标）。
        /// </summary>
        public event Action<int, int, Float2> AttributeTooltipRequested;

        /// <summary>
        /// 鼠标离开控件事件（用于隐藏 Tooltip）。
        /// 当鼠标离开雷达图区域时触发，由宿主页面绑定以隐藏 Tooltip。
        /// </summary>
        public event Action TooltipEnded;

        /// <summary>
        /// 创建六边形雷达图叠加控件，默认尺寸 360x360。
        /// </summary>
        public HexRadarChartOverlay()
        {
            Width = 360f;
            Height = 360f;
            BackgroundColor = new Color(0.05f, 0.18f, 0.24f, 0.78f);
            _elementColors = new Color[SideCount]
            {
                ChineseClassicalTheme.ElementMetalColor,
                ChineseClassicalTheme.ElementWoodColor,
                ChineseClassicalTheme.ElementWaterColor,
                ChineseClassicalTheme.ElementFireColor,
                ChineseClassicalTheme.ElementEarthColor,
                new Color(0.92f, 0.88f, 0.70f, 1f), // 中和 — 淡金
            };
        }

        /// <summary>
        /// 设置外层五行数值。第 6 顶点"中和"自动取 5 项平均值。
        /// </summary>
        /// <param name="metal">金</param>
        /// <param name="wood">木</param>
        /// <param name="water">水</param>
        /// <param name="fire">火</param>
        /// <param name="earth">土</param>
        public void SetWuxingValues(float metal, float wood, float water, float fire, float earth)
        {
            _targetWuxingValues[0] = metal;
            _targetWuxingValues[1] = wood;
            _targetWuxingValues[2] = water;
            _targetWuxingValues[3] = fire;
            _targetWuxingValues[4] = earth;
            _targetWuxingValues[5] = (metal + wood + water + fire + earth) / 5f;
        }

        /// <summary>
        /// 设置内层关键属性数值。
        /// </summary>
        /// <param name="values">长度 6 的数组：攻击/防御/气血/暴击/命中/闪避</param>
        public void SetKeyAttributeValues(float[] values)
        {
            if (values == null) return;
            for (int i = 0; i < SideCount && i < values.Length; i++)
            {
                _targetKeyAttrValues[i] = values[i];
            }
        }

        /// <summary>
        /// 每帧将显示值向目标值插值，实现平滑动画。
        /// </summary>
        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);

            float step = Mathf.Clamp(AnimationSpeed * deltaTime, 0f, 1f);

            for (int i = 0; i < SideCount; i++)
            {
                float diff = _targetWuxingValues[i] - _displayWuxingValues[i];
                if (Mathf.Abs(diff) < 0.01f)
                    _displayWuxingValues[i] = _targetWuxingValues[i];
                else
                    _displayWuxingValues[i] += diff * step;

                diff = _targetKeyAttrValues[i] - _displayKeyAttrValues[i];
                if (Mathf.Abs(diff) < 0.01f)
                    _displayKeyAttrValues[i] = _targetKeyAttrValues[i];
                else
                    _displayKeyAttrValues[i] += diff * step;
            }
        }

        /// <summary>
        /// 绘制六边形雷达图叠加控件。
        /// 绘制顺序：外层光环 → 网格 → 外层五行填充 → 外层五行描边 →
        /// 内层关键属性填充 → 内层关键属性描边 → 中心点装饰 → 顶点标记 → 标签框。
        /// </summary>
        public override void Draw()
        {
            base.Draw();
            if (!Visible) return;

            try
            {
                Float2 center = new Float2(Width * 0.5f, Height * 0.5f);
                float maxRadius = Mathf.Min(Width, Height) * 0.5f - Padding;
                if (maxRadius <= 0f) return;
                float innerRadius = maxRadius * InnerRadiusRatio;

                // === 1. 外层光环（多层半透明青蓝色同心六边形） ===
                DrawOuterGlowRing(center, maxRadius + 8f, new Color(0.04f, 0.16f, 0.22f, 0.82f));
                DrawOuterGlowRing(center, maxRadius + 5f, new Color(0.06f, 0.22f, 0.30f, 0.75f));
                DrawOuterGlowRing(center, maxRadius + 2f, new Color(0.08f, 0.28f, 0.36f, 0.55f));

                // === 2. 同心六边形网格（金属蚀刻感） ===
                DrawGrid(center, maxRadius);

                // === 3. 计算外层五行数据点并缓存（供悬停检测使用） ===
                ComputeDataPoints(center, maxRadius, _displayWuxingValues, _wuxingDataPoints);

                // === 4. 外层五行多边形填充（半透明天蓝色） ===
                DrawFillPolygon(center, _wuxingDataPoints, new Color(0.35f, 0.75f, 1.0f, 0.32f));

                // === 5. 外层五行多层发光描边 ===
                // 5.1 最外层柔光
                DrawPolygonOutline(_wuxingDataPoints, new Color(0.4f, 0.8f, 1.0f, 0.22f), OuterGlowThickness);
                // 5.2 中层青色
                DrawPolygonOutline(_wuxingDataPoints, new Color(0.45f, 0.85f, 1.0f, 0.65f), MidGlowThickness);
                // 5.3 最内层实线
                DrawPolygonOutline(_wuxingDataPoints, new Color(0.55f, 0.90f, 1.0f, 0.55f), DataLineThickness);

                // === 6. 计算内层关键属性数据点并缓存 ===
                ComputeDataPoints(center, innerRadius, _displayKeyAttrValues, _keyAttrDataPoints);

                // === 7. 内层关键属性多边形填充（半透明春天绿） ===
                DrawFillPolygon(center, _keyAttrDataPoints, new Color(0.45f, 0.95f, 0.55f, 0.26f));

                // === 8. 内层关键属性描边（细线） ===
                DrawPolygonOutline(_keyAttrDataPoints, new Color(0.55f, 1.0f, 0.65f, 0.70f), InnerDataLineThickness);

                // === 9. 中心点装饰（金色发光核心） ===
                Render2D.FillRectangle(new Rectangle(center.X - 5f, center.Y - 5f, 10f, 10f), new Color(1f, 0.82f, 0.36f, 0.55f));
                Render2D.FillRectangle(new Rectangle(center.X - 3f, center.Y - 3f, 6f, 6f), new Color(1f, 0.95f, 0.72f, 1f));

                // === 10. 顶点标记（外层元素色 + 内层金色） ===
                for (int i = 0; i < SideCount; i++)
                {
                    DrawMarker(_wuxingDataPoints[i], _elementColors[i]);
                    DrawMarker(_keyAttrDataPoints[i], ChineseClassicalTheme.MetalBorderHighlightColor);
                }

                // === 11. 顶点标签框 ===
                Font wuxingFont = UIHelper.SetFont(size: 9)?.GetFont();
                Font keyAttrFont = UIHelper.SetFont(size: 7)?.GetFont();
                for (int i = 0; i < SideCount; i++)
                {
                    if (wuxingFont != null)
                        DrawWuxingLabel(center, maxRadius, i, _displayWuxingValues[i], wuxingFont);
                    if (keyAttrFont != null)
                        DrawKeyAttrLabel(center, innerRadius, i, _displayKeyAttrValues[i], keyAttrFont);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[HexRadarChartOverlay] 绘制失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 鼠标移动时检测顶点悬停。距离任一数据点小于 20px 时触发
        /// <see cref="AttributeTooltipRequested"/> 事件。
        /// </summary>
        /// <param name="location">鼠标位置（控件本地坐标）</param>
        public override void OnMouseMove(Float2 location)
        {
            base.OnMouseMove(location);

            if (MaxValue <= 0f) return;

            float radius = Mathf.Min(Width, Height) * 0.5f - Padding;
            if (radius <= 0f) return;

            Float2 center = new Float2(Width * 0.5f, Height * 0.5f);
            float innerRadius = radius * InnerRadiusRatio;

            // 检测外层五行顶点（组别 0）
            for (int i = 0; i < SideCount; i++)
            {
                float angle = -Mathf.PiOverTwo + i * (Mathf.TwoPi / SideCount);
                float ratio = Mathf.Clamp(_displayWuxingValues[i] / MaxValue, 0f, 1f);
                float r = radius * ratio;
                float dx = location.X - (center.X + Mathf.Cos(angle) * r);
                float dy = location.Y - (center.Y + Mathf.Sin(angle) * r);
                if (dx * dx + dy * dy < HoverDistanceSq)
                {
                    AttributeTooltipRequested?.Invoke(0, i, location);
                    return;
                }
            }

            // 检测内层关键属性顶点（组别 1）
            for (int i = 0; i < SideCount; i++)
            {
                float angle = -Mathf.PiOverTwo + i * (Mathf.TwoPi / SideCount);
                float ratio = Mathf.Clamp(_displayKeyAttrValues[i] / MaxValue, 0f, 1f);
                float r = innerRadius * ratio;
                float dx = location.X - (center.X + Mathf.Cos(angle) * r);
                float dy = location.Y - (center.Y + Mathf.Sin(angle) * r);
                if (dx * dx + dy * dy < HoverDistanceSq)
                {
                    AttributeTooltipRequested?.Invoke(1, i, location);
                    return;
                }
            }
        }

        /// <summary>
        /// 鼠标离开控件时触发 <see cref="TooltipEnded"/> 事件，
        /// 由宿主页面绑定以隐藏 Tooltip。
        /// </summary>
        public override void OnMouseLeave()
        {
            base.OnMouseLeave();
            try
            {
                TooltipEnded?.Invoke();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[HexRadarChartOverlay] OnMouseLeave 触发失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 绘制外层光环（从中心填充到六边形边界的三角扇）。
        /// </summary>
        private void DrawOuterGlowRing(Float2 center, float radius, Color color)
        {
            Float2[] pts = new Float2[SideCount];
            for (int i = 0; i < SideCount; i++)
            {
                float angle = -Mathf.PiOverTwo + i * (Mathf.TwoPi / SideCount);
                pts[i] = new Float2(center.X + Mathf.Cos(angle) * radius, center.Y + Mathf.Sin(angle) * radius);
            }
            for (int i = 0; i < SideCount; i++)
            {
                int next = (i + 1) % SideCount;
                Render2D.FillTriangle(center, pts[i], pts[next], color);
            }
        }

        /// <summary>
        /// 绘制同心六边形网格与中心射线（金属蚀刻色）。
        /// </summary>
        private void DrawGrid(Float2 center, float maxRadius)
        {
            for (int level = 1; level <= GridLevels; level++)
            {
                float ratio = level / (float)GridLevels;
                float r = maxRadius * ratio;
                Float2[] points = new Float2[SideCount];
                for (int i = 0; i < SideCount; i++)
                {
                    float angle = -Mathf.PiOverTwo + i * (Mathf.TwoPi / SideCount);
                    points[i] = new Float2(center.X + Mathf.Cos(angle) * r, center.Y + Mathf.Sin(angle) * r);
                }

                Color lineColor = level == GridLevels
                    ? ChineseClassicalTheme.MetalBorderHighlightColor
                    : new Color(ChineseClassicalTheme.MetalDarkShade.R, ChineseClassicalTheme.MetalDarkShade.G, ChineseClassicalTheme.MetalDarkShade.B, 0.75f);
                for (int i = 0; i < SideCount; i++)
                {
                    int next = (i + 1) % SideCount;
                    Render2D.DrawLine(points[i], points[next], lineColor, GridLineThickness);
                }
            }

            // 从中心到每个顶点的射线
            for (int i = 0; i < SideCount; i++)
            {
                float angle = -Mathf.PiOverTwo + i * (Mathf.TwoPi / SideCount);
                Float2 outer = new Float2(center.X + Mathf.Cos(angle) * maxRadius, center.Y + Mathf.Sin(angle) * maxRadius);
                Render2D.DrawLine(center, outer, ChineseClassicalTheme.MetalDarkShade, GridLineThickness);
            }
        }

        /// <summary>
        /// 根据数值与半径计算六边形数据点坐标。
        /// </summary>
        private void ComputeDataPoints(Float2 center, float radius, float[] values, Float2[] output)
        {
            float invMax = MaxValue > 0f ? 1f / MaxValue : 0f;
            for (int i = 0; i < SideCount; i++)
            {
                float angle = -Mathf.PiOverTwo + i * (Mathf.TwoPi / SideCount);
                float ratio = Mathf.Clamp(values[i] * invMax, 0f, 1f);
                float r = radius * ratio;
                output[i] = new Float2(center.X + Mathf.Cos(angle) * r, center.Y + Mathf.Sin(angle) * r);
            }
        }

        /// <summary>
        /// 以每三角不同元素色填充多边形（三角扇剖分）。
        /// </summary>
        private void DrawFillPolygon(Float2 center, Float2[] dataPoints, Color[] elementColors, float alpha)
        {
            for (int i = 0; i < SideCount; i++)
            {
                int next = (i + 1) % SideCount;
                Color ec = elementColors[i];
                Color fillColor = new Color(ec.R * 0.75f + 0.1f, ec.G * 0.75f + 0.1f, ec.B * 0.75f + 0.1f, alpha);
                Render2D.FillTriangle(center, dataPoints[i], dataPoints[next], fillColor);
            }
        }

        /// <summary>
        /// 以单一颜色填充多边形（三角扇剖分）。
        /// </summary>
        private void DrawFillPolygon(Float2 center, Float2[] dataPoints, Color fillColor)
        {
            for (int i = 0; i < SideCount; i++)
            {
                int next = (i + 1) % SideCount;
                Render2D.FillTriangle(center, dataPoints[i], dataPoints[next], fillColor);
            }
        }

        /// <summary>
        /// 绘制多边形闭合描边。
        /// </summary>
        private void DrawPolygonOutline(Float2[] dataPoints, Color color, float thickness)
        {
            for (int i = 0; i < SideCount; i++)
            {
                int next = (i + 1) % SideCount;
                Render2D.DrawLine(dataPoints[i], dataPoints[next], color, thickness);
            }
        }

        /// <summary>
        /// 绘制顶点标记（外发光 + 主体 + 内部高光）。
        /// </summary>
        private void DrawMarker(Float2 point, Color color)
        {
            Render2D.FillRectangle(
                new Rectangle(point.X - MarkerSize, point.Y - MarkerSize, MarkerSize * 2f, MarkerSize * 2f),
                new Color(color.R, color.G, color.B, 0.35f)
            );
            Render2D.FillRectangle(
                new Rectangle(point.X - MarkerSize * 0.5f, point.Y - MarkerSize * 0.5f, MarkerSize, MarkerSize),
                color
            );
            Render2D.FillRectangle(
                new Rectangle(point.X - 1.5f, point.Y - 1.5f, 3f, 3f),
                new Color(1f, 1f, 1f, 0.85f)
            );
        }

        /// <summary>
        /// 绘制外层五行顶点标签框（金属边框 + 元素色名 + 数值 + 引线）。
        /// 标签沿顶点射线方向外移，与内层标签保持足够间距，避免视觉遮挡。
        /// </summary>
        private void DrawWuxingLabel(Float2 center, float maxRadius, int index, float value, Font font)
        {
            float angle = -Mathf.PiOverTwo + index * (Mathf.TwoPi / SideCount);
            Float2 dataPoint = new Float2(
                center.X + Mathf.Cos(angle) * maxRadius,
                center.Y + Mathf.Sin(angle) * maxRadius
            );
            Float2 labelCenter = new Float2(
                center.X + Mathf.Cos(angle) * (maxRadius + LabelOffset + 16f),
                center.Y + Mathf.Sin(angle) * (maxRadius + LabelOffset + 16f)
            );

            float bx = labelCenter.X - OuterLabelBoxW * 0.5f;
            float by = labelCenter.Y - OuterLabelBoxH * 0.5f;
            var boxRect = new Rectangle(bx, by, OuterLabelBoxW, OuterLabelBoxH);

            // 引线：从数据点连到标签框内侧，明确标注归属
            Float2 leaderTarget = ClampPointToRectangleEdge(dataPoint, boxRect);
            Render2D.DrawLine(dataPoint, leaderTarget, new Color(_elementColors[index].R, _elementColors[index].G, _elementColors[index].B, 0.5f), 1f);

            // 背景与边框
            Render2D.FillRectangle(boxRect, new Color(0.04f, 0.04f, 0.04f, 0.92f));
            Render2D.DrawRectangle(boxRect, ChineseClassicalTheme.MetalBorderColor, 2f);
            Render2D.DrawRectangle(new Rectangle(bx + 1f, by + 1f, OuterLabelBoxW - 2f, OuterLabelBoxH - 2f), ChineseClassicalTheme.MetalBorderHighlightColor, 1f);

            // 顶部元素色高亮条，强化五行归属
            Render2D.FillRectangle(new Rectangle(bx + 2f, by + 2f, OuterLabelBoxW - 4f, 2f), _elementColors[index]);
            // 顶部细金线
            Render2D.FillRectangle(new Rectangle(bx + 2f, by + 4f, OuterLabelBoxW - 4f, 1f), ChineseClassicalTheme.MetalBorderSoftHighlightColor);

            // 属性名
            var nameRect = new Rectangle(bx, by + 4f, OuterLabelBoxW, 14f);
            Render2D.DrawText(font, _wuxingNames[index], nameRect, _elementColors[index], TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);

            // 分隔线
            Render2D.FillRectangle(new Rectangle(bx + 8f, by + 18f, OuterLabelBoxW - 16f, 1f), ChineseClassicalTheme.WowDividerColor);

            // 数值
            var valueRect = new Rectangle(bx, by + 19f, OuterLabelBoxW, 12f);
            Render2D.DrawText(font, value.ToString("F0"), valueRect, ChineseClassicalTheme.WowNumberTextColor, TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
        }

        /// <summary>
        /// 绘制内层关键属性顶点标签框（小型金属边框 + 金色名 + 数值 + 引线）。
        /// 位于内圈外侧，与外层五行标签保持径向间距，避免视觉遮挡与误读。
        /// </summary>
        private void DrawKeyAttrLabel(Float2 center, float innerRadius, int index, float value, Font font)
        {
            float angle = -Mathf.PiOverTwo + index * (Mathf.TwoPi / SideCount);
            Float2 dataPoint = new Float2(
                center.X + Mathf.Cos(angle) * innerRadius,
                center.Y + Mathf.Sin(angle) * innerRadius
            );
            Float2 labelCenter = new Float2(
                center.X + Mathf.Cos(angle) * (innerRadius + 14f),
                center.Y + Mathf.Sin(angle) * (innerRadius + 14f)
            );

            float bx = labelCenter.X - InnerLabelBoxW * 0.5f;
            float by = labelCenter.Y - InnerLabelBoxH * 0.5f;
            var boxRect = new Rectangle(bx, by, InnerLabelBoxW, InnerLabelBoxH);

            // 引线：从数据点连到标签框内侧
            Float2 leaderTarget = ClampPointToRectangleEdge(dataPoint, boxRect);
            Render2D.DrawLine(dataPoint, leaderTarget, new Color(1f, 0.78f, 0.28f, 0.45f), 1f);

            // 背景与边框
            Render2D.FillRectangle(boxRect, new Color(0.04f, 0.04f, 0.04f, 0.9f));
            Render2D.DrawRectangle(boxRect, ChineseClassicalTheme.MetalBorderColor, 1.5f);
            Render2D.DrawRectangle(new Rectangle(bx + 1f, by + 1f, InnerLabelBoxW - 2f, InnerLabelBoxH - 2f), ChineseClassicalTheme.MetalBorderHighlightColor, 1f);

            // 左侧金色高亮条，区分于外层元素色
            Render2D.FillRectangle(new Rectangle(bx + 2f, by + 2f, 2f, InnerLabelBoxH - 4f), ChineseClassicalTheme.WowTitleColor);

            // 属性名
            var nameRect = new Rectangle(bx + 2f, by + 1f, InnerLabelBoxW - 4f, 11f);
            Render2D.DrawText(font, _keyAttrNames[index], nameRect, ChineseClassicalTheme.WowTitleColor, TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);

            // 数值
            var valueRect = new Rectangle(bx, by + 12f, InnerLabelBoxW, 11f);
            Render2D.DrawText(font, value.ToString("F0"), valueRect, ChineseClassicalTheme.WowNumberTextColor, TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
        }

        /// <summary>
        /// 将外部点投影到矩形最近边缘上的点，用于绘制标签引线。
        /// </summary>
        private static Float2 ClampPointToRectangleEdge(Float2 point, Rectangle rect)
        {
            float cx = rect.X + rect.Width * 0.5f;
            float cy = rect.Y + rect.Height * 0.5f;
            float dx = point.X - cx;
            float dy = point.Y - cy;
            float absDx = Mathf.Abs(dx);
            float absDy = Mathf.Abs(dy);

            float edgeX, edgeY;
            if (absDx * rect.Height > absDy * rect.Width)
            {
                // 与左右边相交
                edgeX = dx > 0 ? rect.X + rect.Width : rect.X;
                float t = (edgeX - cx) / (dx + 0.0001f);
                edgeY = cy + dy * t;
                edgeY = Mathf.Clamp(edgeY, rect.Y, rect.Y + rect.Height);
            }
            else
            {
                // 与上下边相交
                edgeY = dy > 0 ? rect.Y + rect.Height : rect.Y;
                float t = (edgeY - cy) / (dy + 0.0001f);
                edgeX = cx + dx * t;
                edgeX = Mathf.Clamp(edgeX, rect.X, rect.X + rect.Width);
            }

            return new Float2(edgeX, edgeY);
        }
    }
}
