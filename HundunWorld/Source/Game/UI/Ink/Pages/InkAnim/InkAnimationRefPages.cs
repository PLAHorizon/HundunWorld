using System;
using System.Collections.Generic;
using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.Ink;
using HundunWorld.Game.UI.Ink.Data;
using HundunWorld.Game.UI.StyleSystem;

namespace HundunWorld.Game.UI.Ink.Pages.InkAnim
{
    /// <summary>
    /// 中文文案折行工具。
    /// Flax 的 <see cref="Label"/> 按单行绘制文本，长说明文案需按字数手工切成多行，
    /// 每行一个 Label 堆叠排版。
    /// </summary>
    internal static class InkAnimTextHelper
    {
        /// <summary>
        /// 按最大字数切分文本，尽量在标点处断开。
        /// </summary>
        /// <param name="text">原始文案</param>
        /// <param name="maxChars">每行最大字符数（&lt;=0 时返回原文本单行）</param>
        /// <returns>折行后的行列表</returns>
        internal static List<string> SplitLines(string text, int maxChars)
        {
            var lines = new List<string>();
            if (string.IsNullOrEmpty(text))
            {
                lines.Add(string.Empty);
                return lines;
            }

            if (maxChars <= 0 || text.Length <= maxChars)
            {
                lines.Add(text);
                return lines;
            }

            int start = 0;
            while (start < text.Length)
            {
                int len = Mathf.Min(maxChars, text.Length - start);
                int cut = len;
                if (start + len < text.Length)
                {
                    // 优先在标点处断开，避免割裂语义
                    for (int i = len - 1; i >= len / 2; i--)
                    {
                        char c = text[start + i];
                        if (c == '，' || c == '。' || c == '；' || c == '、' || c == '·' || c == ' ' || c == ':')
                        {
                            cut = i + 1;
                            break;
                        }
                    }
                }

                lines.Add(text.Substring(start, cut));
                start += cut;
            }

            return lines;
        }
    }

    // =======================================================================

    /// <summary>
    /// 水墨动画预览画布。
    /// <para>
    /// 对应设计稿 ink-anim-ref-1/2 中部的分镜演示区：以宣纸为底，按
    /// <see cref="InkAnimationSpec"/> 的时序实时播放「晕染 Spread」或「笔触 Brush」动画，
    /// 使设计稿描述的分镜在引擎中可被逐帧验证（走查用）。
    /// </para>
    /// <para>
    /// 播放循环 = 规格总时长 + 0.8s 停留，随后从头重播；
    /// <see cref="CurrentPhaseIndex"/> 供页面同步高亮底部时间轴阶段卡。
    /// </para>
    /// </summary>
    public class InkAnimationPreview : Control
    {
        /// <summary>动画播完后的停留时间（秒）</summary>
        private const float HoldSeconds = 0.8f;

        /// <summary>每段贝塞尔曲线的采样点数（笔触路径平滑度）</summary>
        private const int SamplesPerSegment = 24;

        /// <summary>分镜规格</summary>
        private readonly InkAnimationSpec _spec;

        /// <summary>笔触路径采样点（控件局部坐标）</summary>
        private readonly List<Float2> _brushPath = new List<Float2>();

        /// <summary>笔触路径累计长度（与 <see cref="_brushPath"/> 等长）</summary>
        private readonly List<float> _brushLengths = new List<float>();

        /// <summary>笔触路径总长度</summary>
        private float _brushTotalLength;

        /// <summary>路径是否已按当前尺寸生成</summary>
        private bool _pathBuilt;

        /// <summary>播放头（秒）</summary>
        private float _time;

        /// <summary>
        /// 构造函数。
        /// </summary>
        /// <param name="spec">分镜规格</param>
        public InkAnimationPreview(InkAnimationSpec spec)
        {
            _spec = spec ?? InkAnimationSpecs.Spread;
            BackgroundColor = Color.Transparent;
            AutoFocus = false;
        }

        /// <summary>当前所处时间轴阶段索引（-1 表示停留期）</summary>
        public int CurrentPhaseIndex { get; private set; }

        /// <summary>播放头位置（秒）</summary>
        public float PlayheadSeconds => _time;

        /// <summary>
        /// 从头重播。
        /// </summary>
        public void Replay()
        {
            _time = 0f;
            CurrentPhaseIndex = 0;
        }

        /// <summary>
        /// 跳转到指定阶段起点播放（对应设计稿点击时间轴阶段卡）。
        /// </summary>
        /// <param name="phaseIndex">阶段索引</param>
        public void SeekToPhase(int phaseIndex)
        {
            if (_spec.Phases == null || _spec.Phases.Count == 0)
            {
                Replay();
                return;
            }

            int idx = Mathf.Clamp(phaseIndex, 0, _spec.Phases.Count - 1);
            _time = _spec.Phases[idx].Start;
            CurrentPhaseIndex = idx;
        }

        /// <inheritdoc />
        public override void Update(float deltaTime)
        {
            if (deltaTime <= 0f)
            {
                return;
            }

            _time += deltaTime;
            float cycle = _spec.Duration + HoldSeconds;
            if (cycle <= 0f)
            {
                _time = 0f;
                return;
            }

            if (_time >= cycle)
            {
                _time -= cycle;
            }

            CurrentPhaseIndex = ResolvePhaseIndex(_time);
        }

        /// <summary>
        /// 由播放头时间解析所处阶段索引。
        /// </summary>
        private int ResolvePhaseIndex(float t)
        {
            if (_spec.Phases == null || _spec.Phases.Count == 0)
            {
                return -1;
            }

            for (int i = 0; i < _spec.Phases.Count; i++)
            {
                if (t >= _spec.Phases[i].Start && t < _spec.Phases[i].End)
                {
                    return i;
                }
            }

            return -1;
        }

        /// <inheritdoc />
        protected override void OnSizeChanged()
        {
            base.OnSizeChanged();
            // 尺寸变化后笔触路径需要按新画布重建
            _pathBuilt = false;
        }

        /// <inheritdoc />
        public override void Draw()
        {
            if (Width <= 8f || Height <= 8f)
            {
                return;
            }

            // 1. 宣纸画布（对应设计稿分镜演示区底色）
            var canvas = new Rectangle(0f, 0f, Width, Height);
            InkRenderHelper.FillRoundedRectangle(canvas, InkWashTheme.RadiusLg, InkWashTheme.PaperPanelBg);
            InkRenderHelper.DrawRoundedRectangle(canvas, InkWashTheme.RadiusLg, InkWashTheme.PaperPanelBorder, 1f);

            // 2. 四角金角装饰（.ink-corner-deco）
            DrawCornerDecos();

            // 3. 分镜动画
            if (_spec.Kind == InkAnimationKind.Brush)
            {
                DrawBrush();
            }
            else
            {
                DrawSpread();
            }
        }

        /// <summary>
        /// 绘制宣纸画布四角的 L 型金角装饰。
        /// </summary>
        private void DrawCornerDecos()
        {
            const float len = 14f;
            const float inset = 6f;
            var color = InkWashTheme.GoldDim;

            // 左上
            Render2D.DrawLine(new Float2(inset, inset), new Float2(inset + len, inset), color, 1f);
            Render2D.DrawLine(new Float2(inset, inset), new Float2(inset, inset + len), color, 1f);
            // 右上
            Render2D.DrawLine(new Float2(Width - inset, inset), new Float2(Width - inset - len, inset), color, 1f);
            Render2D.DrawLine(new Float2(Width - inset, inset), new Float2(Width - inset, inset + len), color, 1f);
            // 左下
            Render2D.DrawLine(new Float2(inset, Height - inset), new Float2(inset + len, Height - inset), color, 1f);
            Render2D.DrawLine(new Float2(inset, Height - inset), new Float2(inset, Height - inset - len), color, 1f);
            // 右下
            Render2D.DrawLine(new Float2(Width - inset, Height - inset), new Float2(Width - inset - len, Height - inset), color, 1f);
            Render2D.DrawLine(new Float2(Width - inset, Height - inset), new Float2(Width - inset, Height - inset - len), color, 1f);
        }

        /// <summary>
        /// 缓出插值（ease-out）。
        /// </summary>
        private static float EaseOut(float p)
        {
            float x = Mathf.Saturate(p);
            return 1f - Mathf.Pow(1f - x, 3f);
        }

        /// <summary>
        /// 尺寸归一系数：以 600 单位短边为基准换算墨点/笔锋尺寸。
        /// </summary>
        private float UnitScale()
        {
            return Mathf.Max(0.35f, Mathf.Min(Width, Height) / 600f);
        }

        // -------------------------------------------------------------------
        // 晕染 Spread（ink-anim-ref-1.html）
        // -------------------------------------------------------------------

        /// <summary>
        /// 绘制「水墨晕染」分镜：墨点初现 → 晕染扩散 → 淡墨层叠 → 渐隐消散。
        /// </summary>
        private void DrawSpread()
        {
            float t = _time;
            float s = UnitScale();
            var center = new Float2(Width * 0.42f, Height * 0.52f);

            // 阶段 1：墨点初现（0.0s — 0.4s）半径 0 → 40px，opacity 0 → 0.9
            float p1 = EaseOut(t / 0.4f);
            // 阶段 2：晕染扩散（0.4s — 1.2s）blur 8px → 24px，半径继续外扩
            float p2 = EaseOut((t - 0.4f) / 0.8f);
            // 阶段 3：淡墨层叠（1.2s — 1.8s）第二层淡墨，透明度 0.3
            float p3 = Mathf.Saturate((t - 1.2f) / 0.6f);
            // 阶段 4：渐隐消散（1.8s — 2.4s）opacity 0.9 → 0
            float p4 = Mathf.Saturate((t - 1.8f) / 0.6f);

            float globalAlpha = t < 1.8f ? 0.9f : Mathf.Lerp(0.9f, 0f, EaseOut(p4));

            float coreRadius = 40f * s * p1;
            float spreadRadius = Mathf.Lerp(0f, 96f * s, p2);
            float radius = coreRadius + spreadRadius;
            // blur(8px) → blur(24px)：softness 0.35 → 1.0
            float softness = Mathf.Lerp(0.35f, 1f, p2);

            // 层 1 + 层 2：主墨与晕染扩散
            DrawInkBlob(center, radius, softness, InkWashTheme.Abyss, globalAlpha, 1.7f);

            // 层 3：淡墨层叠（透明度 0.3，multiply 混合的近似）
            if (p3 > 0f)
            {
                var offset = new Float2(18f * s, -12f * s);
                DrawInkBlob(center + offset, radius * 0.82f, softness * 0.9f, InkWashTheme.BaseTertiary,
                    globalAlpha * 0.3f * p3, 4.3f);
            }
        }

        /// <summary>
        /// 绘制一团边缘不规则的墨（多层不规则多边形叠加，模拟宣纸吸墨 + 飞白边缘）。
        /// </summary>
        /// <param name="center">墨团中心</param>
        /// <param name="radius">基准半径</param>
        /// <param name="softness">边缘柔化程度（0~1，对应 blur 半径）</param>
        /// <param name="inkColor">墨色</param>
        /// <param name="alpha">整体不透明度</param>
        /// <param name="seed">噪声种子（保证同一层形状稳定）</param>
        private void DrawInkBlob(Float2 center, float radius, float softness, Color inkColor, float alpha, float seed)
        {
            if (radius <= 0.5f || alpha <= 0.001f)
            {
                return;
            }

            const int layers = 7;
            for (int i = layers; i >= 1; i--)
            {
                float li = (float)i / layers;
                // 外层更大更淡，内层更小更浓
                float layerRadius = radius * (0.35f + 0.65f * li) * (1f + softness * 0.45f * (1f - li));
                float layerAlpha = alpha * Mathf.Pow(li, 1.7f) * 0.5f;
                // 飞白：最外层留出缺口，形成枯笔质感
                bool flyWhite = i >= layers - 1;
                var c = new Color(inkColor.R, inkColor.G, inkColor.B, layerAlpha);
                DrawJitteredBlob(center, layerRadius, c, seed + i * 2.3f, 0.16f * softness, flyWhite);
            }
        }

        /// <summary>
        /// 用不规则多边形（扇形三角带）填充一个带噪声边缘的圆形墨块。
        /// </summary>
        private void DrawJitteredBlob(Float2 center, float radius, Color color, float seed, float jitter, bool flyWhite)
        {
            if (radius <= 0.5f || color.A <= 0.001f)
            {
                return;
            }

            const int segments = 44;
            var vertices = new Float2[segments * 3];
            for (int i = 0; i < segments; i++)
            {
                float a1 = (i / (float)segments) * Mathf.TwoPi;
                float a2 = ((i + 1) / (float)segments) * Mathf.TwoPi;
                float n1 = BlobNoise(a1, seed);
                float n2 = BlobNoise(a2, seed);
                float r1 = radius * (1f + jitter * n1);
                float r2 = radius * (1f + jitter * n2);

                int idx = i * 3;
                vertices[idx] = center;
                vertices[idx + 1] = center + new Float2(Mathf.Cos(a1) * r1, Mathf.Sin(a1) * r1);
                vertices[idx + 2] = center + new Float2(Mathf.Cos(a2) * r2, Mathf.Sin(a2) * r2);
            }

            if (!flyWhite)
            {
                Render2D.FillTriangles(vertices, color);
                return;
            }

            // 飞白：跳过部分扇区，形成断续边缘
            for (int i = 0; i < segments; i++)
            {
                if ((i % 5) == 2 || (i % 7) == 3)
                {
                    continue;
                }

                var tri = new[]
                {
                    vertices[i * 3],
                    vertices[i * 3 + 1],
                    vertices[i * 3 + 2],
                };
                Render2D.FillTriangles(tri, color);
            }
        }

        /// <summary>
        /// 确定性的伪随机噪声（±1），用于墨迹边缘的不规则起伏。
        /// </summary>
        private static float BlobNoise(float angle, float seed)
        {
            return 0.55f * Mathf.Sin(3.1f * angle + seed) + 0.45f * Mathf.Sin(7.7f * angle + seed * 2.3f);
        }

        // -------------------------------------------------------------------
        // 笔触 Brush（ink-anim-ref-2.html）
        // -------------------------------------------------------------------

        /// <summary>
        /// 构建 4 段贝塞尔笔触路径（对应设计稿「笔触段数 4 段 / 路径方式 Bezier」）。
        /// </summary>
        private void BuildBrushPath()
        {
            _brushPath.Clear();
            _brushLengths.Clear();
            _brushTotalLength = 0f;

            // 4 段三次贝塞尔：归一化控制点 → 控件局部坐标
            float[][] pts =
            {
                new[] { 0.10f, 0.72f }, new[] { 0.20f, 0.24f }, new[] { 0.34f, 0.88f }, new[] { 0.45f, 0.38f },
                new[] { 0.56f, 0.06f }, new[] { 0.62f, 0.74f }, new[] { 0.72f, 0.44f },
                new[] { 0.80f, 0.20f }, new[] { 0.86f, 0.66f }, new[] { 0.92f, 0.52f },
                new[] { 0.95f, 0.44f }, new[] { 0.97f, 0.40f }, new[] { 0.99f, 0.36f },
            };

            for (int seg = 0; seg < 4; seg++)
            {
                Float2 p0 = Local(pts[seg * 3]);
                Float2 c1 = Local(pts[seg * 3 + 1]);
                Float2 c2 = Local(pts[seg * 3 + 2]);
                Float2 p3 = seg < 3 ? Local(pts[(seg + 1) * 3]) : Local(new[] { 1.02f, 0.30f });

                for (int i = 0; i <= SamplesPerSegment; i++)
                {
                    float u = i / (float)SamplesPerSegment;
                    var point = CubicBezier(p0, c1, c2, p3, u);
                    if (_brushPath.Count > 0)
                    {
                        var prev = _brushPath[_brushPath.Count - 1];
                        _brushTotalLength += Float2.Distance(prev, point);
                    }

                    _brushPath.Add(point);
                    _brushLengths.Add(_brushTotalLength);
                }
            }

            _pathBuilt = true;
        }

        /// <summary>
        /// 归一化坐标 → 控件局部坐标（留出内边距，避免笔锋贴边）。
        /// </summary>
        private Float2 Local(float[] p)
        {
            float padX = Width * 0.06f;
            float padY = Height * 0.12f;
            return new Float2(padX + p[0] * (Width - padX * 2f), padY + p[1] * (Height - padY * 2f));
        }

        /// <summary>
        /// 三次贝塞尔插值。
        /// </summary>
        private static Float2 CubicBezier(Float2 p0, Float2 p1, Float2 p2, Float2 p3, float t)
        {
            float u = 1f - t;
            float w0 = u * u * u;
            float w1 = 3f * u * u * t;
            float w2 = 3f * u * t * t;
            float w3 = t * t * t;
            return new Float2(
                w0 * p0.X + w1 * p1.X + w2 * p2.X + w3 * p3.X,
                w0 * p0.Y + w1 * p1.Y + w2 * p2.Y + w3 * p3.Y);
        }

        /// <summary>
        /// 绘制「水墨笔触」分镜：落笔蓄势 → 行笔游走 → 飞白枯笔 → 收笔回锋。
        /// </summary>
        private void DrawBrush()
        {
            if (!_pathBuilt || _brushPath.Count < 2)
            {
                BuildBrushPath();
            }

            float t = _time;
            float s = UnitScale();
            var ink = InkWashTheme.Abyss;

            // 阶段 1：落笔蓄势（0.0s — 0.3s）scale 0 → 1.2 回弹
            float p1 = Mathf.Saturate(t / 0.3f);
            float p2 = EaseOut((t - 0.3f) / 0.9f);          // 行笔游走（0.3s — 1.2s）
            float p3 = Mathf.Saturate((t - 1.2f) / 0.5f);   // 飞白枯笔（1.2s — 1.7s）
            float p4 = Mathf.Saturate((t - 1.7f) / 0.3f);   // 收笔回锋（1.7s — 2.0s）

            // 落笔：起始墨点 scale 0 → 1.2 → 1.0
            float bounce = p1 < 0.75f
                ? Mathf.Lerp(0f, 1.2f, p1 / 0.75f)
                : Mathf.Lerp(1.2f, 1f, (p1 - 0.75f) / 0.25f);
            var start = _brushPath[0];
            InkRenderHelper.FillCircle(start, Mathf.Max(0.5f, 9f * s * bounce), new Color(ink.R, ink.G, ink.B, 0.9f));

            // 行笔：沿路径展开（stroke-dashoffset 的等价实现），宽度 2 → 6 → 3px
            float drawnLength = _brushTotalLength * p2;
            Color strokeColor = p3 > 0f
                ? Color.Lerp(ink, InkWashTheme.PaperFaded, 0.35f * p3)
                : ink;

            for (int i = 1; i < _brushPath.Count; i++)
            {
                if (_brushLengths[i] > drawnLength)
                {
                    break;
                }

                float u = _brushTotalLength > 0f ? _brushLengths[i] / _brushTotalLength : 0f;
                // 飞白枯笔：末段速度加快，墨色断续
                if (p3 > 0f && u > 0.55f && ((i % 4) == 2))
                {
                    continue;
                }

                float width = StrokeWidth(u) * s;
                float alpha = 0.88f * (1f - 0.35f * p3 * Mathf.Saturate((u - 0.55f) / 0.45f));
                Render2D.DrawLine(_brushPath[i - 1], _brushPath[i],
                    new Color(strokeColor.R, strokeColor.G, strokeColor.B, alpha), Mathf.Max(0.6f, width));
            }

            // 收笔回锋：末点 opacity 0.8 → 0，半径收束至 2px
            if (p2 >= 0.999f)
            {
                var end = _brushPath[_brushPath.Count - 1];
                float endRadius = Mathf.Lerp(6f * s, 2f * s, p4);
                float endAlpha = Mathf.Lerp(0.8f, 0f, p4);
                InkRenderHelper.FillCircle(end, Mathf.Max(0.5f, endRadius),
                    new Color(ink.R, ink.G, ink.B, endAlpha));
            }
        }

        /// <summary>
        /// 笔触宽度曲线：沿路径 0→0.5 由 2px 增至 6px，0.5→1 由 6px 收至 3px。
        /// </summary>
        private static float StrokeWidth(float u)
        {
            return u <= 0.5f
                ? Mathf.Lerp(2f, 6f, u / 0.5f)
                : Mathf.Lerp(6f, 3f, (u - 0.5f) / 0.5f);
        }
    }

    // =======================================================================

    /// <summary>
    /// 分镜时间轴阶段卡（对应设计稿 .ref-timeline-phase）。
    /// 点击后跳转预览播放头到该阶段起点，并高亮为激活态。
    /// </summary>
    internal sealed class InkPhaseCard : ContainerControl
    {
        private readonly Label _timeLabel;
        private readonly Label _nameLabel;
        private readonly Label _descLabel;

        private bool _active;
        private bool _hovered;

        /// <summary>阶段索引</summary>
        public int Index { get; }

        /// <summary>阶段卡被点击时触发，参数为阶段索引。</summary>
        public event Action<int> Selected;

        /// <summary>是否处于激活态（当前播放阶段）。</summary>
        public bool Active
        {
            get => _active;
            set => _active = value;
        }

        /// <summary>
        /// 构造函数。
        /// </summary>
        /// <param name="index">阶段索引</param>
        /// <param name="phase">阶段数据</param>
        public InkPhaseCard(int index, InkAnimationPhase phase)
        {
            Index = index;
            BackgroundColor = Color.Transparent;
            ClipChildren = false;
            AutoFocus = false;

            _timeLabel = new Label
            {
                Text = phase != null ? phase.TimeRange : string.Empty,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 11f),
                TextColor = InkWashTheme.TextBrand,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _nameLabel = new Label
            {
                Text = phase != null ? phase.Name : string.Empty,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 14f),
                TextColor = InkWashTheme.TextDefault,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _descLabel = new Label
            {
                Text = phase != null ? phase.Description : string.Empty,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.TextTertiary,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };

            AddChild(_timeLabel);
            AddChild(_nameLabel);
            AddChild(_descLabel);
        }

        /// <inheritdoc />
        protected override void OnSizeChanged()
        {
            base.OnSizeChanged();
            const float padX = 12f;
            float contentW = Mathf.Max(0f, Width - padX * 2f);
            if (_timeLabel != null)
            {
                _timeLabel.Location = new Float2(padX, 8f);
                _timeLabel.Size = new Float2(contentW, 14f);
            }

            if (_nameLabel != null)
            {
                _nameLabel.Location = new Float2(padX, 24f);
                _nameLabel.Size = new Float2(contentW, 18f);
            }

            if (_descLabel != null)
            {
                _descLabel.Location = new Float2(padX, 44f);
                _descLabel.Size = new Float2(contentW, 16f);
            }
        }

        /// <inheritdoc />
        public override void OnMouseEnter(Float2 location)
        {
            base.OnMouseEnter(location);
            _hovered = true;
        }

        /// <inheritdoc />
        public override void OnMouseLeave()
        {
            base.OnMouseLeave();
            _hovered = false;
        }

        /// <inheritdoc />
        public override bool OnMouseDown(Float2 location, MouseButton button)
        {
            if (button == MouseButton.Left)
            {
                Selected?.Invoke(Index);
                return true;
            }

            return base.OnMouseDown(location, button);
        }

        /// <inheritdoc />
        public override void Draw()
        {
            if (Width <= 0f || Height <= 0f)
            {
                return;
            }

            var rect = new Rectangle(0f, 0f, Width, Height);
            Color bg = _active
                ? new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.12f)
                : (_hovered
                    ? new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.08f)
                    : InkWashTheme.Panel);
            Color border = _active ? InkWashTheme.BorderGoldStrong : InkWashTheme.BorderNeutralL2;

            InkRenderHelper.FillRoundedRectangle(rect, InkWashTheme.RadiusMd, bg);
            InkRenderHelper.DrawRoundedRectangle(rect, InkWashTheme.RadiusMd, border, 1f);

            // 激活态左侧金竖线（与 .ink-menu-item.active 一致的选中语言）
            if (_active)
            {
                Render2D.FillRectangle(new Rectangle(0f, 6f, 3f, Height - 12f), InkWashTheme.GoldBright);
            }

            base.Draw();
        }
    }

    // =======================================================================

    /// <summary>
    /// 分镜标注卡（对应设计稿 .ref-annotation）：编号圆牌 + 标题 + 说明。
    /// </summary>
    internal sealed class InkAnnotationCard : ContainerControl
    {
        /// <summary>说明文案每行最大字符数</summary>
        private const int DescCharsPerLine = 14;

        private readonly Label _numberLabel;
        private readonly Label _titleLabel;
        private readonly List<Label> _descLabels = new List<Label>();

        /// <summary>
        /// 构造函数。
        /// </summary>
        /// <param name="annotation">标注数据</param>
        public InkAnnotationCard(InkAnimationAnnotation annotation)
        {
            BackgroundColor = Color.Transparent;
            ClipChildren = false;
            AutoFocus = false;

            _numberLabel = new Label
            {
                Text = annotation != null ? annotation.Number : string.Empty,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 12f),
                TextColor = InkWashTheme.TextOnBrand,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };
            _titleLabel = new Label
            {
                Text = annotation != null ? annotation.Title : string.Empty,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 13f),
                TextColor = InkWashTheme.TextBrand,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };

            AddChild(_numberLabel);
            AddChild(_titleLabel);

            var lines = InkAnimTextHelper.SplitLines(annotation != null ? annotation.Description : string.Empty, DescCharsPerLine);
            foreach (var line in lines)
            {
                var label = new Label
                {
                    Text = line,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                    TextColor = InkWashTheme.TextSecondary,
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                };
                _descLabels.Add(label);
                AddChild(label);
            }
        }

        /// <summary>
        /// 按内容行数计算所需高度。
        /// </summary>
        public float PreferredHeight => 52f + _descLabels.Count * 16f;

        /// <inheritdoc />
        protected override void OnSizeChanged()
        {
            base.OnSizeChanged();
            const float padX = 12f;
            const float badgeSize = 20f;

            if (_numberLabel != null)
            {
                _numberLabel.Location = new Float2(padX, 10f);
                _numberLabel.Size = new Float2(badgeSize, badgeSize);
            }

            if (_titleLabel != null)
            {
                _titleLabel.Location = new Float2(padX + badgeSize + 8f, 10f);
                _titleLabel.Size = new Float2(Mathf.Max(0f, Width - padX * 2f - badgeSize - 8f), badgeSize);
            }

            for (int i = 0; i < _descLabels.Count; i++)
            {
                _descLabels[i].Location = new Float2(padX, 38f + i * 16f);
                _descLabels[i].Size = new Float2(Mathf.Max(0f, Width - padX * 2f), 16f);
            }
        }

        /// <inheritdoc />
        public override void Draw()
        {
            if (Width <= 0f || Height <= 0f)
            {
                return;
            }

            var rect = new Rectangle(0f, 0f, Width, Height);
            InkRenderHelper.FillRoundedRectangle(rect, InkWashTheme.RadiusMd, InkWashTheme.Panel);
            InkRenderHelper.DrawRoundedRectangle(rect, InkWashTheme.RadiusMd, InkWashTheme.BorderGold, 1f);

            // 编号圆牌：鎏金底 + 反色数字
            InkRenderHelper.FillCircle(new Float2(12f + 10f, 10f + 10f), 10f, InkWashTheme.GoldPrimary);

            base.Draw();
        }
    }

    // =======================================================================

    /// <summary>
    /// 水墨动画分镜参考页基类。
    /// <para>
    /// 落地设计稿 <c>ink-anim-ref-1.html</c>（水墨晕染）与 <c>ink-anim-ref-2.html</c>（水墨笔触）：
    /// 顶部标题栏（编号 / 书法标题 / 英文副标题 / 标签组）、左侧分镜标注、
    /// 中部宣纸动画预览、右侧技术参数面板、底部时间轴阶段卡。
    /// </para>
    /// </summary>
    public abstract class InkAnimationRefPageBase : ContainerControl, IInkPage
    {
        /// <summary>标注卡宽度</summary>
        private const float AnnotationWidth = 236f;

        /// <summary>技术参数面板宽度</summary>
        private const float SpecPanelWidth = 280f;

        /// <summary>时间轴高度</summary>
        private const float TimelineHeight = 118f;

        /// <summary>分镜规格</summary>
        private readonly InkAnimationSpec _spec;

        private readonly List<InkTag> _tags = new List<InkTag>();
        private readonly List<InkPhaseCard> _phaseCards = new List<InkPhaseCard>();
        private readonly List<InkAnnotationCard> _annotationCards = new List<InkAnnotationCard>();
        private readonly List<Label> _paramLabels = new List<Label>();
        private readonly List<Label> _paramValues = new List<Label>();

        private InkAnimationPreview _preview;
        private Label _numberLabel;
        private Label _titleLabel;
        private Label _subtitleLabel;
        private Label _timelineTitleLabel;
        private Label _canvasCaptionLabel;
        private InkPanel _specPanel;
        private InkPanelTitle _specPanelTitle;
        private InkPanel _timelinePanel;

        private float _scale = 1f;

        /// <summary>
        /// 构造函数。
        /// </summary>
        /// <param name="spec">分镜规格</param>
        protected InkAnimationRefPageBase(InkAnimationSpec spec)
        {
            _spec = spec ?? InkAnimationSpecs.Spread;

            AnchorPreset = AnchorPresets.StretchAll;
            BackgroundColor = Color.Transparent;
            ClipChildren = false;
            AutoFocus = false;
            Location = Float2.Zero;

            try
            {
                BuildContent();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[{GetType().Name}] 初始化失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 构建页面全部子控件。
        /// </summary>
        private void BuildContent()
        {
            var screen = FlaxEngine.Screen.Size;
            if (screen.X <= 0f || screen.Y <= 0f)
            {
                screen = new Float2(1920f, 1080f);
            }

            Size = screen;

            // 1. 中部动画预览（宣纸画布）
            _preview = new InkAnimationPreview(_spec);
            AddChild(_preview);

            // 2. 分镜标注卡
            foreach (var annotation in _spec.Annotations)
            {
                var card = new InkAnnotationCard(annotation);
                _annotationCards.Add(card);
                AddChild(card);
            }

            // 3. 顶部标题栏
            _numberLabel = new Label
            {
                Text = _spec.Number,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 38f),
                TextColor = InkWashTheme.GoldPrimary,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _titleLabel = new Label
            {
                Text = _spec.Title,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 30f),
                TextColor = InkWashTheme.TextDefault,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _subtitleLabel = new Label
            {
                Text = _spec.Subtitle,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 11f),
                TextColor = InkWashTheme.TextTertiary,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            AddChild(_numberLabel);
            AddChild(_titleLabel);
            AddChild(_subtitleLabel);

            // 4. 标题右侧标签组
            for (int i = 0; i < _spec.Tags.Count; i++)
            {
                var tag = new InkTag
                {
                    Text = _spec.Tags[i],
                    TagVariant = i == 0 ? InkTagVariant.Brand : InkTagVariant.Default,
                };
                _tags.Add(tag);
                AddChild(tag);
            }

            // 5. 右侧技术参数面板
            _specPanel = new InkPanel
            {
                Variant = InkPanelVariant.Default,
            };
            AddChild(_specPanel);

            _specPanelTitle = new InkPanelTitle
            {
                Title = _spec.PanelTitle,
            };
            _specPanel.AddChild(_specPanelTitle);

            foreach (var param in _spec.Parameters)
            {
                var label = new Label
                {
                    Text = param.Label,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                    TextColor = InkWashTheme.TextTertiary,
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                };
                var value = new Label
                {
                    Text = param.Value,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 12f),
                    TextColor = InkWashTheme.TextBrand,
                    HorizontalAlignment = TextAlignment.Far,
                    VerticalAlignment = TextAlignment.Center,
                };
                _paramLabels.Add(label);
                _paramValues.Add(value);
                _specPanel.AddChild(label);
                _specPanel.AddChild(value);
            }

            // 6. 画布说明
            _canvasCaptionLabel = new Label
            {
                Text = _spec.Kind == InkAnimationKind.Brush ? "分镜演示 · 技能释放触发" : "分镜演示 · 章节切换触发",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.TextTertiary,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            AddChild(_canvasCaptionLabel);

            // 7. 底部时间轴
            _timelinePanel = new InkPanel
            {
                Variant = InkPanelVariant.Default,
            };
            AddChild(_timelinePanel);

            _timelineTitleLabel = new Label
            {
                Text = _spec.TimelineTitle,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 13f),
                TextColor = InkWashTheme.TextBrand,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _timelinePanel.AddChild(_timelineTitleLabel);

            for (int i = 0; i < _spec.Phases.Count; i++)
            {
                var card = new InkPhaseCard(i, _spec.Phases[i]);
                card.Selected += OnPhaseSelected;
                _phaseCards.Add(card);
                _timelinePanel.AddChild(card);
            }

            _phaseCards[0].Active = true;

            RefreshLayout();
        }

        /// <summary>
        /// 点击时间轴阶段卡：跳转播放头并刷新激活态。
        /// </summary>
        private void OnPhaseSelected(int index)
        {
            _preview?.SeekToPhase(index);
            UpdateActivePhase(index);
        }

        /// <summary>
        /// 更新阶段卡激活态。
        /// </summary>
        private void UpdateActivePhase(int activeIndex)
        {
            for (int i = 0; i < _phaseCards.Count; i++)
            {
                _phaseCards[i].Active = i == activeIndex;
            }
        }

        /// <inheritdoc />
        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);

            if (_preview == null)
            {
                return;
            }

            int phase = _preview.CurrentPhaseIndex;
            if (phase >= 0)
            {
                bool changed = false;
                for (int i = 0; i < _phaseCards.Count; i++)
                {
                    bool shouldActive = i == phase;
                    if (_phaseCards[i].Active != shouldActive)
                    {
                        changed = true;
                    }
                }

                if (changed)
                {
                    UpdateActivePhase(phase);
                }
            }
        }

        /// <inheritdoc />
        public void RefreshLayout()
        {
            float w = Width;
            float h = Height;
            if (w <= 0f || h <= 0f)
            {
                var screen = FlaxEngine.Screen.Size;
                w = screen.X > 0f ? screen.X : 1920f;
                h = screen.Y > 0f ? screen.Y : 1080f;
            }

            _scale = Mathf.Max(0.55f, Mathf.Min(w / 1920f, h / 1080f));
            float s = _scale;

            float margin = 40f * s;
            float headerTop = 22f * s;
            float headerHeight = 74f * s;

            // 顶部标题栏
            if (_numberLabel != null)
            {
                _numberLabel.Location = new Float2(margin, headerTop + 4f * s);
                _numberLabel.Size = new Float2(56f * s, 44f * s);
            }

            if (_titleLabel != null)
            {
                _titleLabel.Location = new Float2(margin + 56f * s, headerTop);
                _titleLabel.Size = new Float2(260f * s, 38f * s);
            }

            if (_subtitleLabel != null)
            {
                _subtitleLabel.Location = new Float2(margin + 56f * s, headerTop + 40f * s);
                _subtitleLabel.Size = new Float2(320f * s, 18f * s);
            }

            // 标签组（右上，自右向左排列）
            float tagW = 74f * s;
            float tagH = 22f * s;
            float tagGap = 8f * s;
            float tagRight = w - margin;
            for (int i = _tags.Count - 1; i >= 0; i--)
            {
                _tags[i].Location = new Float2(tagRight - tagW, headerTop + 10f * s);
                _tags[i].Size = new Float2(tagW, tagH);
                tagRight -= tagW + tagGap;
            }

            // 右侧技术参数面板
            float panelW = SpecPanelWidth * s;
            float panelX = w - margin - panelW;
            float rowH = 26f * s;
            float panelHeaderH = 40f * s;
            float panelH = panelHeaderH + _paramLabels.Count * rowH + 14f * s;
            if (_specPanel != null)
            {
                _specPanel.Location = new Float2(panelX, headerTop + headerHeight);
                _specPanel.Size = new Float2(panelW, panelH);
            }

            if (_specPanelTitle != null)
            {
                _specPanelTitle.Location = Float2.Zero;
                _specPanelTitle.Size = new Float2(panelW, panelHeaderH);
            }

            for (int i = 0; i < _paramLabels.Count; i++)
            {
                float y = panelHeaderH + i * rowH;
                _paramLabels[i].Location = new Float2(14f * s, y);
                _paramLabels[i].Size = new Float2(panelW * 0.5f, rowH);
                _paramValues[i].Location = new Float2(panelW * 0.5f, y);
                _paramValues[i].Size = new Float2(panelW * 0.5f - 14f * s, rowH);
            }

            // 底部时间轴
            float timelineH = TimelineHeight * s;
            float timelineY = h - margin - timelineH;
            float timelineW = w - margin * 2f;
            if (_timelinePanel != null)
            {
                _timelinePanel.Location = new Float2(margin, timelineY);
                _timelinePanel.Size = new Float2(timelineW, timelineH);
            }

            if (_timelineTitleLabel != null)
            {
                _timelineTitleLabel.Location = new Float2(14f * s, 8f * s);
                _timelineTitleLabel.Size = new Float2(timelineW - 28f * s, 18f * s);
            }

            float cardTop = 32f * s;
            float cardGap = 12f * s;
            float cardW = _phaseCards.Count > 0
                ? (timelineW - 28f * s - cardGap * (_phaseCards.Count - 1)) / _phaseCards.Count
                : 0f;
            float cardH = timelineH - cardTop - 12f * s;
            for (int i = 0; i < _phaseCards.Count; i++)
            {
                _phaseCards[i].Location = new Float2(14f * s + i * (cardW + cardGap), cardTop);
                _phaseCards[i].Size = new Float2(cardW, cardH);
            }

            // 中部动画预览画布
            float previewX = margin;
            float previewY = headerTop + headerHeight + 6f * s;
            float previewW = panelX - margin - 20f * s;
            float previewH = timelineY - previewY - 28f * s;
            if (_preview != null)
            {
                _preview.Location = new Float2(previewX, previewY);
                _preview.Size = new Float2(Mathf.Max(120f, previewW), Mathf.Max(120f, previewH));
            }

            if (_canvasCaptionLabel != null)
            {
                _canvasCaptionLabel.Location = new Float2(previewX, previewY + previewH + 4f * s);
                _canvasCaptionLabel.Size = new Float2(Mathf.Max(120f, previewW), 18f * s);
            }

            // 分镜标注卡（按设计稿 left/right + top 百分比定位）
            for (int i = 0; i < _annotationCards.Count; i++)
            {
                var data = _spec.Annotations[i];
                float cw = AnnotationWidth * s;
                float ch = _annotationCards[i].PreferredHeight * Mathf.Max(0.85f, s);
                float x = data.AnchorRight ? (w - w * data.X - cw) : (w * data.X);
                float y = h * data.Y;
                _annotationCards[i].Location = new Float2(
                    Mathf.Clamp(x, margin, Mathf.Max(margin, w - cw - margin)),
                    Mathf.Clamp(y, headerTop + headerHeight, Mathf.Max(headerTop + headerHeight, timelineY - ch - 8f * s)));
                _annotationCards[i].Size = new Float2(cw, ch);
            }
        }

        /// <inheritdoc />
        public override void OnParentResized()
        {
            base.OnParentResized();
            RefreshLayout();
        }
    }

    // =======================================================================

    /// <summary>
    /// 水墨动画分镜参考 01：水墨晕染（ink-anim-ref-1.html）。
    /// 动画类型 Spread / 总时长 2.4s / 墨色 3 层 / 混合 multiply / 触发场景 章节切换。
    /// </summary>
    public sealed class InkAnimRefSpreadPage : InkAnimationRefPageBase
    {
        /// <summary>
        /// 构造函数：使用「水墨晕染」分镜规格。
        /// </summary>
        public InkAnimRefSpreadPage()
            : base(InkAnimationSpecs.Spread)
        {
        }
    }

    // =======================================================================

    /// <summary>
    /// 水墨动画分镜参考 02：水墨笔触（ink-anim-ref-2.html）。
    /// 动画类型 Brush / 总时长 2.0s / 路径 Bezier / 笔触 4 段 / 触发场景 技能释放。
    /// </summary>
    public sealed class InkAnimRefBrushPage : InkAnimationRefPageBase
    {
        /// <summary>
        /// 构造函数：使用「水墨笔触」分镜规格。
        /// </summary>
        public InkAnimRefBrushPage()
            : base(InkAnimationSpecs.Brush)
        {
        }
    }
}
