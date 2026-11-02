using System;
using FlaxEngine;
using FlaxEngine.GUI;
using NarrativePro.CommonUI;

namespace NarrativePro.Widgets
{
    /// <summary>
    /// 默认准星。UE5 <c>UCrosshairWidget</c> 是 Abstract + Blueprintable，具体外观由蓝图子类给出；
    /// 本端口需要一个可实例化的具体 Flax 控件（<c>WeaponItem.CrosshairWidgetPath</c> 与 HUD 都按类型创建），
    /// 因此这里落地"四向刻度 + 中心点"的默认外观，并把扩散/瞄准状态做成可调参数。
    /// </summary>
    public class NarrativeDefaultCrosshair : CrosshairWidget
    {
        /// <summary>是否在四向刻度外侧再画一圈对角短刻（提升腰射时的可读性）。</summary>
        public bool bShowDiagonalTicks = false;

        /// <summary>刻度随扩散额外增长的比例（0 表示只增大间隙）。</summary>
        public float SpreadGrowthRatio = 0.25f;

        /// <summary>
        /// 供 <see cref="NarrativeGameplayHUD"/> 与工厂按武器参数初始化的入口。
        /// 对应 UE5 蓝图准星的 "Init from weapon data"。
        /// </summary>
        public virtual void Configure(float lineLength, float lineThickness, float centerGap, float spread)
        {
            LineLength = lineLength;
            LineThickness = lineThickness;
            CenterGap = centerGap;
            SetSpread(spread);
        }

        public override void Draw()
        {
            // 基类画出四向刻度 + 中心点；这里只按扩散把刻度略微拉长
            float savedLength = LineLength;
            LineLength = savedLength + Spread * SpreadGrowthRatio;

            base.Draw();

            LineLength = savedLength;

            if (bShowDiagonalTicks)
            {
                DrawDiagonalTicks();
            }
        }

        private void DrawDiagonalTicks()
        {
            if (!bShowDiagonalTicks || Opacity <= 0.001f) return;

            Color color = CrosshairColor;
            color.A *= Opacity;

            Float2 center = new Float2(Size.X * 0.5f, Size.Y * 0.5f);
            float gap = (CenterGap + Spread) * 0.7071f;
            float length = LineLength * 0.6f * 0.7071f;

            for (int quadrant = 0; quadrant < 4; quadrant++)
            {
                float sx = (quadrant & 1) == 0 ? -1f : 1f;
                float sy = quadrant < 2 ? -1f : 1f;

                Float2 from = new Float2(center.X + sx * gap, center.Y + sy * gap);
                Float2 to = new Float2(from.X + sx * length, from.Y + sy * length);

                Render2D.DrawLine(from, to, color, Math.Max(1f, LineThickness * 0.75f));
            }
        }
    }

    /// <summary>
    /// 瞄准镜准星（机瞄/开镜态）。对应 UE5 里另一个蓝图准星子类：
    /// 在默认准星之上加外圈环 + 左右命中标尺，并按 ADS 进度收缩间隙。
    /// </summary>
    public class NarrativeAimReticleCrosshair : NarrativeDefaultCrosshair
    {
        /// <summary>外圈环半径（像素）。</summary>
        public float RingRadius = 22f;

        /// <summary>外圈段数（Flax Render2D 无圆原语，用折线段逼近，与仓库其它自绘面板一致）。</summary>
        public int RingSegments = 24;

        /// <summary>是否显示左右命中标尺。</summary>
        public bool bShowSideTicks = true;

        /// <summary>开镜进度（0 = 腰射，1 = 机瞄）。对应 UE5 蓝图里按 ADS 改间隙的做法。</summary>
        public float AimDownSightsProgress
        {
            get => _aimProgress;
            set => _aimProgress = value < 0f ? 0f : (value > 1f ? 1f : value);
        }

        private float _aimProgress;

        public override void SetSpread(float spread)
        {
            base.SetSpread(spread);
        }

        public override void Draw()
        {
            float savedGap = CenterGap;
            CenterGap = savedGap * (1f - 0.55f * _aimProgress);

            base.Draw();

            CenterGap = savedGap;

            if (!Visible || Opacity <= 0.001f) return;

            Color color = CrosshairColor;
            color.A *= Opacity * (0.35f + 0.65f * _aimProgress);

            Float2 center = new Float2(Size.X * 0.5f, Size.Y * 0.5f);
            float radius = RingRadius - _aimProgress * 6f;

            DrawRing(center, radius, color);

            if (bShowSideTicks)
            {
                float tickY = center.Y;
                float offset = radius + 4f;
                Render2D.DrawLine(new Float2(center.X - offset, tickY), new Float2(center.X - offset - 6f, tickY), color, LineThickness);
                Render2D.DrawLine(new Float2(center.X + offset, tickY), new Float2(center.X + offset + 6f, tickY), color, LineThickness);
            }
        }

        /// <summary>用折线段逼近圆（Render2D 没有填充/描边圆原语，已在 FlaxEngine.CSharp.xml 核对）。</summary>
        private void DrawRing(Float2 center, float radius, Color color)
        {
            if (radius <= 0f || RingSegments < 3) return;

            int segments = RingSegments;
            float step = (float)(Math.PI * 2.0 / segments);
            Float2 previous = PointOnCircle(center, radius, 0f);

            for (int i = 1; i <= segments; i++)
            {
                Float2 current = PointOnCircle(center, radius, i * step);
                Render2D.DrawLine(previous, current, color, 1f);
                previous = current;
            }
        }

        private static Float2 PointOnCircle(Float2 center, float radius, float angle)
        {
            return new Float2(
                center.X + (float)Math.Cos(angle) * radius,
                center.Y + (float)Math.Sin(angle) * radius);
        }
    }
}
