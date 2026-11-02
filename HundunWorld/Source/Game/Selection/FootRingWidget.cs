using FlaxEngine;
using FlaxEngine.GUI;

namespace HundunWorld.Game.Selection
{
    /// <summary>
    /// 脚底光圈控件：把目标脚下的**世界圆环**逐点投影到屏幕后描边。
    /// <para>
    /// <b>为什么不用固定压扁比的椭圆</b>：地面圆在任何透视下投影成椭圆，短轴比取决于相机俯角与
    /// 目标相对光轴的位置，写死一个 <c>(1, 0.4)</c> 之类只能在某个固定机位看着对。这里按角度采样
    /// 世界圆上的点、逐个 <c>Camera.ProjectPoint</c>，得到的多边形天然就是正确形状，任意机位都对。
    /// </para>
    /// <para>
    /// <b>为什么画在 GUI 而不是用 DebugDraw</b>：工程里旧的目标高亮用 <c>DebugDraw.DrawCircle</c>，
    /// 那是调试绘制通道，打包（非 Debug）构建下不保证出现，而选中反馈属于玩法 UI。
    /// </para>
    /// <para>
    /// <b>控件尺寸收缩到环的外接矩形</b>：光圈挂在 <see cref="UI.GameMain.NameplateHost"/>
    /// （与头顶名牌同层、不参与 1920×1080 设计空间缩放，所以投影像素能 1:1 当本地坐标用），
    /// 但没有理由让一个全屏透明容器压在场景输入上方——那正是"是不是它在吞鼠标"这种怀疑的来源。
    /// </para>
    /// </summary>
    public class FootRingWidget : ContainerControl
    {
        /// <summary>圆环采样段数；24 段在半径几十像素时已看不出折角。需与调用方的点列长度一致。</summary>
        private const int Segments = 24;

        /// <summary>包围盒在点列外的留白（像素），容纳描边宽度与呼吸余量。</summary>
        private const float BoundsPadding = 8f;

        private readonly Float2[] _localPoints = new Float2[Segments];
        private readonly Float2[] _localPulse = new Float2[Segments];
        private int _pointCount;
        private int _pulseCount;
        private Color _color = Color.Transparent;
        private Color _pulseColor = Color.Transparent;
        private float _thickness = 2f;

        public FootRingWidget()
        {
            // TopLeft 锚 + 每帧显式给 Location/Size：非 TopLeft 锚会在构造期按父尺寸 0 重算位置。
            AnchorPreset = AnchorPresets.TopLeft;
            BackgroundColor = Color.Transparent;
            ClipChildren = false;
        }

        /// <summary>
        /// 更新圆环几何。传入的点必须是屏幕像素坐标，控件自行换算为本地坐标并收缩包围盒。
        /// </summary>
        /// <param name="points">主环点列。</param>
        /// <param name="pointCount">主环有效点数（&lt;3 视为不可见）。</param>
        /// <param name="pulsePoints">脉动副环点列；<paramref name="pulseCount"/> 为 0 表示不画副环。</param>
        /// <param name="pulseCount">副环有效点数。</param>
        /// <param name="color">主环颜色。</param>
        /// <param name="pulseColor">副环颜色。</param>
        /// <param name="thickness">线宽（像素）。</param>
        public void SetRing(Float2[] points, int pointCount, Float2[] pulsePoints, int pulseCount,
            Color color, Color pulseColor, float thickness)
        {
            if (points == null || pointCount < 3 || pointCount > Segments)
            {
                HideRing();
                return;
            }

            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;

            // 包围盒把副环一起框住，否则脉动外扩的那半边会被控件边界裁掉。
            for (var i = 0; i < pointCount; i++)
                Accumulate(points[i]);
            if (pulsePoints != null)
            {
                for (var i = 0; i < pulseCount; i++)
                    Accumulate(pulsePoints[i]);
            }

            void Accumulate(Float2 p)
            {
                if (p.X < minX) minX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.X > maxX) maxX = p.X;
                if (p.Y > maxY) maxY = p.Y;
            }

            var origin = new Float2(minX - BoundsPadding, minY - BoundsPadding);
            Location = origin;
            Size = new Float2(maxX - minX + BoundsPadding * 2f, maxY - minY + BoundsPadding * 2f);

            for (var i = 0; i < pointCount; i++)
                _localPoints[i] = points[i] - origin;
            if (pulsePoints != null)
            {
                for (var i = 0; i < pulseCount; i++)
                    _localPulse[i] = pulsePoints[i] - origin;
            }

            _pointCount = pointCount;
            _pulseCount = pulseCount;
            _color = color;
            _pulseColor = pulseColor;
            _thickness = thickness;
            Visible = true;
        }

        public void HideRing()
        {
            _pointCount = 0;
            _pulseCount = 0;
            Visible = false;
        }

        public override void Draw()
        {
            base.Draw();

            if (!Visible || _pointCount < 3)
                return;

            Stroke(_localPoints, _pointCount, _color, _thickness);

            if (_pulseCount >= 3)
                Stroke(_localPulse, _pulseCount, _pulseColor, _thickness * 0.7f);
        }

        /// <summary>首尾相连描一条闭合折线。</summary>
        private static void Stroke(Float2[] pts, int count, Color color, float thickness)
        {
            for (var i = 0; i < count; i++)
            {
                var a = pts[i];
                var b = pts[(i + 1) % count];
                Render2D.DrawLine(a, b, color, thickness);
            }
        }
    }
}
