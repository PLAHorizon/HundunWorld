using System;
using FlaxEngine;
using FlaxEngine.GUI;
using Game.Character.Attributes;
using HundunWorld.Game.UI;
using HundunWorld.Game.UI.StyleSystem;

namespace HundunWorld.Game.UI.Components
{
    /// <summary>
    /// 五行雷达图自定义控件（增强版）
    /// 魔兽世界风格：多层发光描边 + 渐变填充多边形 + 金属属性标签框
    /// </summary>
    public class WuxingRadarChart : Control
    {
        private const int SideCount = 5;
        private const int GridLevels = 5;
        private const float Padding = 38f;
        private const float LabelOffset = 12f;
        private const float MarkerSize = 6f;
        private const float GridLineThickness = 1f;
        private const float OuterGlowThickness = 4f;
        private const float MidGlowThickness = 2f;
        private const float DataLineThickness = 1.5f;
        private const float AnimationSpeed = 8f;

        private readonly float[] _targetValues = new float[SideCount];
        private readonly float[] _displayValues = new float[SideCount];
        private readonly Color[] _elementColors;
        private readonly string[] _elementNames = { "金", "木", "火", "土", "水" };

        public float MaxValue { get; set; } = 100f;

        public Color ChartColor { get; set; } = new Color(1.0f, 0.78f, 0.28f, 0.50f);

        public WuxingRadarChart()
        {
            Width = 220f;
            Height = 220f;
            BackgroundColor = Color.Transparent;
            _elementColors = new Color[SideCount]
            {
                ChineseClassicalTheme.ElementMetalColor,
                ChineseClassicalTheme.ElementWoodColor,
                ChineseClassicalTheme.ElementFireColor,
                ChineseClassicalTheme.ElementEarthColor,
                ChineseClassicalTheme.ElementWaterColor,
            };
        }

        public void SetValues(float metal, float wood, float water, float fire, float earth)
        {
            _targetValues[0] = metal;
            _targetValues[1] = wood;
            _targetValues[2] = fire;
            _targetValues[3] = earth;
            _targetValues[4] = water;
        }

        public void SetValues(CharacterAttributes attributes)
        {
            _targetValues[0] = attributes.Metal;
            _targetValues[1] = attributes.Wood;
            _targetValues[2] = attributes.Fire;
            _targetValues[3] = attributes.Earth;
            _targetValues[4] = attributes.Water;
        }

        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);

            bool changed = false;
            float step = AnimationSpeed * deltaTime;
            for (int i = 0; i < SideCount; i++)
            {
                float diff = _targetValues[i] - _displayValues[i];
                if (Mathf.Abs(diff) < 0.01f)
                {
                    _displayValues[i] = _targetValues[i];
                }
                else
                {
                    _displayValues[i] += diff * Mathf.Clamp(step, 0f, 1f);
                    changed = true;
                }
            }

            if (changed)
            {
                // 驱动重绘
            }
        }

        public override void Draw()
        {
            base.Draw();

            if (!Visible) return;

            try
            {
                var center = new Float2(Width * 0.5f, Height * 0.5f);
                float maxRadius = Mathf.Min(Width, Height) * 0.5f - Padding;
                if (maxRadius <= 0f) return;

                // === 1. 外层光环（多层同心五边形） ===
                DrawOuterGlowRing(center, maxRadius + 8f, new Color(0.04f, 0.05f, 0.07f, 0.9f));
                DrawOuterGlowRing(center, maxRadius + 5f, new Color(0.1f, 0.09f, 0.06f, 0.85f));
                DrawOuterGlowRing(center, maxRadius + 2f, new Color(0.18f, 0.14f, 0.08f, 0.6f));

                // === 2. 同心五边形网格（金属蚀刻感） ===
                DrawGrid(center, maxRadius);

                // === 3. 计算数据点 ===
                Float2[] dataPoints = new Float2[SideCount];
                for (int i = 0; i < SideCount; i++)
                {
                    float angle = -Mathf.PiOverTwo + i * (Mathf.TwoPi / SideCount);
                    float ratio = Mathf.Clamp(_displayValues[i] / MaxValue, 0f, 1f);
                    float r = maxRadius * ratio;
                    dataPoints[i] = new Float2(
                        center.X + Mathf.Cos(angle) * r,
                        center.Y + Mathf.Sin(angle) * r
                    );
                }

                // === 4. 数据多边形填充（三角剖分，每三角用元素色带透明度渐变） ===
                DrawFillPolygon(center, dataPoints);

                // === 5. 多层发光描边（从粗到细叠加，辉光效果） ===
                // 5.1 最外层柔光
                for (int i = 0; i < SideCount; i++)
                {
                    int next = (i + 1) % SideCount;
                    Render2D.DrawLine(dataPoints[i], dataPoints[next], new Color(1f, 0.78f, 0.28f, 0.18f), OuterGlowThickness);
                }
                // 5.2 中层金色
                for (int i = 0; i < SideCount; i++)
                {
                    int next = (i + 1) % SideCount;
                    Render2D.DrawLine(dataPoints[i], dataPoints[next], ChineseClassicalTheme.MetalBorderHighlightColor, MidGlowThickness);
                }
                // 5.3 最内层实线
                for (int i = 0; i < SideCount; i++)
                {
                    int next = (i + 1) % SideCount;
                    Render2D.DrawLine(dataPoints[i], dataPoints[next], ChartColor, DataLineThickness);
                }

                // === 6. 中心点装饰（金色发光核心） ===
                Render2D.FillRectangle(new Rectangle(center.X - 5f, center.Y - 5f, 10f, 10f), new Color(1f, 0.82f, 0.36f, 0.55f));
                Render2D.FillRectangle(new Rectangle(center.X - 3f, center.Y - 3f, 6f, 6f), new Color(1f, 0.95f, 0.72f, 1f));

                // === 7. 顶点标记（圆角发光点 + 内部高光） ===
                for (int i = 0; i < SideCount; i++)
                {
                    DrawMarker(dataPoints[i], _elementColors[i]);
                }

                // === 8. 属性标签框（金属边框 + 元素名 + 数值） ===
                for (int i = 0; i < SideCount; i++)
                {
                    DrawLabel(center, maxRadius, i, _displayValues[i]);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[WuxingRadarChart] 绘制失败: {ex.Message}");
            }
        }

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

        private void DrawGrid(Float2 center, float maxRadius)
        {
            // 同心五边形（越外层越亮，金属蚀刻感）
            for (int level = 1; level <= GridLevels; level++)
            {
                float ratio = level / (float)GridLevels;
                float r = maxRadius * ratio;
                Float2[] points = new Float2[SideCount];
                for (int i = 0; i < SideCount; i++)
                {
                    float angle = -Mathf.PiOverTwo + i * (Mathf.TwoPi / SideCount);
                    points[i] = new Float2(
                        center.X + Mathf.Cos(angle) * r,
                        center.Y + Mathf.Sin(angle) * r
                    );
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
                Float2 outer = new Float2(
                    center.X + Mathf.Cos(angle) * maxRadius,
                    center.Y + Mathf.Sin(angle) * maxRadius
                );
                Render2D.DrawLine(center, outer, ChineseClassicalTheme.MetalDarkShade, GridLineThickness);
            }
        }

        private void DrawFillPolygon(Float2 center, Float2[] dataPoints)
        {
            for (int i = 0; i < SideCount; i++)
            {
                int next = (i + 1) % SideCount;
                Color ec = _elementColors[i];
                Color fillColor = new Color(
                    ec.R * 0.75f + 0.1f,
                    ec.G * 0.75f + 0.1f,
                    ec.B * 0.75f + 0.1f,
                    0.35f
                );
                Render2D.FillTriangle(center, dataPoints[i], dataPoints[next], fillColor);
            }
        }

        private void DrawMarker(Float2 point, Color color)
        {
            // 外发光
            Render2D.FillRectangle(
                new Rectangle(point.X - MarkerSize, point.Y - MarkerSize, MarkerSize * 2f, MarkerSize * 2f),
                new Color(color.R, color.G, color.B, 0.35f)
            );
            // 主体
            Render2D.FillRectangle(
                new Rectangle(point.X - MarkerSize * 0.5f, point.Y - MarkerSize * 0.5f, MarkerSize, MarkerSize),
                color
            );
            // 内部高光
            Render2D.FillRectangle(
                new Rectangle(point.X - 1.5f, point.Y - 1.5f, 3f, 3f),
                new Color(1f, 1f, 1f, 0.85f)
            );
        }

        private void DrawLabel(Float2 center, float maxRadius, int index, float value)
        {
            float angle = -Mathf.PiOverTwo + index * (Mathf.TwoPi / SideCount);
            Float2 labelCenter = new Float2(
                center.X + Mathf.Cos(angle) * (maxRadius + LabelOffset + 18f),
                center.Y + Mathf.Sin(angle) * (maxRadius + LabelOffset + 18f)
            );

            Font font = UIHelper.SetFont(size: 11)?.GetFont();
            if (font == null) return;

            const float boxW = 58f;
            const float boxH = 34f;
            float bx = labelCenter.X - boxW * 0.5f;
            float by = labelCenter.Y - boxH * 0.5f;

            // 外层深色底
            Render2D.FillRectangle(new Rectangle(bx, by, boxW, boxH), ChineseClassicalTheme.DarkStoneBackgroundColor);
            // 金属外边框
            Render2D.DrawRectangle(new Rectangle(bx, by, boxW, boxH), ChineseClassicalTheme.MetalBorderColor, 2f);
            // 内描边（高亮）
            Render2D.DrawRectangle(new Rectangle(bx + 1f, by + 1f, boxW - 2f, boxH - 2f), ChineseClassicalTheme.MetalBorderHighlightColor, 1f);
            // 顶部细金线
            Render2D.FillRectangle(new Rectangle(bx + 2f, by + 2f, boxW - 4f, 1f), ChineseClassicalTheme.MetalBorderSoftHighlightColor);

            // 属性名
            var nameRect = new Rectangle(bx, by + 2f, boxW, 16f);
            Render2D.DrawText(font, _elementNames[index], nameRect, _elementColors[index], TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);

            // 分隔线
            Render2D.FillRectangle(new Rectangle(bx + 6f, by + 18f, boxW - 12f, 1f), ChineseClassicalTheme.WowDividerColor);

            // 数值
            var valueRect = new Rectangle(bx, by + 19f, boxW, 14f);
            Render2D.DrawText(font, value.ToString("F0"), valueRect, ChineseClassicalTheme.WowNumberTextColor, TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
        }
    }
}
