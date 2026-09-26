using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.StyleSystem;
using System;

namespace HundunWorld.Game.UI.Ink.Pages.CharacterCreation
{
    /// <summary>
    /// 创角 / 选角流程的配色。
    /// 参考图（图片/1-7.png，燕云十六声）是**浅底雾白**舞台，与全站深墨主题相反，
    /// 因此这里单独定义一套浅底常量，深墨只用于文字与键帽。
    /// </summary>
    public static class CcPalette
    {
        /// <summary>
        /// 雾白背景上沿。实测参考图顶部 rgb(173,177,205)（偏冷的浅灰蓝）。
        /// </summary>
        public static readonly Color MistTop = new Color(173f / 255f, 177f / 255f, 205f / 255f);

        /// <summary>雾白背景下沿。实测参考图底部 rgb(164,158,161)——**比顶部更暗更中性，不是更亮**。</summary>
        public static readonly Color MistBottom = new Color(164f / 255f, 158f / 255f, 161f / 255f);

        /// <summary>左上/左下压暗用的墨色</summary>
        public static readonly Color MistShade = new Color(0.06f, 0.07f, 0.09f);

        /// <summary>卡片纸面（上沿 / 下沿）</summary>
        public static readonly Color CardTop = new Color(0.92f, 0.92f, 0.93f);
        public static readonly Color CardBottom = new Color(0.78f, 0.80f, 0.83f);

        /// <summary>正文墨色（浅底上的深字）</summary>
        public static readonly Color InkText = new Color(0.13f, 0.14f, 0.16f);

        /// <summary>未选中墨字（浅底上的灰字）</summary>
        public static readonly Color InkIdle = new Color(0.36f, 0.38f, 0.42f);

        /// <summary>未选中笔触（灰白纸色）</summary>
        public static readonly Color BrushIdle = new Color(0.72f, 0.74f, 0.77f);

        /// <summary>选中笔触：实测参考图笔触金色 rgb(219,179,114)，比主题鎏金更亮。</summary>
        public static readonly Color BrushGold = new Color(219f / 255f, 179f / 255f, 114f / 255f);
        public static readonly Color TextGold = InkWashTheme.GoldDeep;

        /// <summary>键帽底与字</summary>
        public static readonly Color KeycapBg = new Color(0.10f, 0.11f, 0.13f);
        public static readonly Color KeycapText = new Color(0.94f, 0.94f, 0.95f);

        /// <summary>确认块（Space 下一步）的金色底。实测参考图金块 rgb(191,149,95)，比主题鎏金更沉稳。</summary>
        public static readonly Color ConfirmGold = new Color(191f / 255f, 149f / 255f, 95f / 255f);

        /// <summary>印章朱红</summary>
        public static readonly Color SealRed = InkWashTheme.VermilionPrimary;

        /// <summary>钳制到 0~1（Flax Mathf 无 Clamp01）。</summary>
        public static float Clamp01(float v)
        {
            return Mathf.Clamp(v, 0f, 1f);
        }
    }

    /// <summary>
    /// 流程内共用的绘制小工具。
    /// 半透明渐变一律用**整数像素边界、互不重叠**的分带来画：
    /// 多次叠加的画法（如 <see cref="InkRenderHelper.FillRoundedRectangle"/> 的十字带 + 四角圆）
    /// 会把半透明色叠两次，在边缘留下比内部更暗的一圈假边框（罗盘带上踩过这个坑）。
    /// </summary>
    internal static class CcDraw
    {
        /// <summary>竖直方向透明渐变（顶 alpha0 → 底 alpha1），分带互不重叠。</summary>
        public static void VerticalAlphaGradient(Float2 origin, float width, float height, Color rgb, float alpha0, float alpha1, int bands)
        {
            if (width <= 0f || height <= 0f || bands <= 0)
                return;

            int yPrev = (int)origin.Y;
            for (int i = 1; i <= bands; i++)
            {
                int y = (int)(origin.Y + height * i / bands);
                if (y <= yPrev)
                    continue;

                float t = CcPalette.Clamp01(((yPrev + y) * 0.5f - origin.Y) / height);
                float a = alpha0 + (alpha1 - alpha0) * t;
                if (a > 0.002f)
                    Render2D.FillRectangle(new Rectangle(origin.X, yPrev, width, y - yPrev), new Color(rgb.R, rgb.G, rgb.B, a));
                yPrev = y;
            }
        }

        /// <summary>水平方向透明渐变（左 alpha0 → 右 alpha1），分带互不重叠。</summary>
        public static void HorizontalAlphaGradient(Float2 origin, float width, float height, Color rgb, float alpha0, float alpha1, int slices)
        {
            if (width <= 0f || height <= 0f || slices <= 0)
                return;

            int xPrev = (int)origin.X;
            for (int i = 1; i <= slices; i++)
            {
                int x = (int)(origin.X + width * i / slices);
                if (x <= xPrev)
                    continue;

                float t = CcPalette.Clamp01(((xPrev + x) * 0.5f - origin.X) / width);
                float a = alpha0 + (alpha1 - alpha0) * t;
                if (a > 0.002f)
                    Render2D.FillRectangle(new Rectangle(xPrev, origin.Y, x - xPrev, height), new Color(rgb.R, rgb.G, rgb.B, a));
                xPrev = x;
            }
        }

        /// <summary>竖直方向实色渐变（顶 → 底），分带互不重叠。</summary>
        public static void VerticalColorGradient(Float2 origin, float width, float height, Color top, Color bottom, int bands)
        {
            if (width <= 0f || height <= 0f || bands <= 0)
                return;

            int yPrev = (int)origin.Y;
            for (int i = 1; i <= bands; i++)
            {
                int y = (int)(origin.Y + height * i / bands);
                if (y <= yPrev)
                    continue;

                float t = CcPalette.Clamp01(((yPrev + y) * 0.5f - origin.Y) / height);
                Render2D.FillRectangle(new Rectangle(origin.X, yPrev, width, y - yPrev), Color.Lerp(top, bottom, t));
                yPrev = y;
            }
        }

        /// <summary>
        /// 画一条两头收尖的毛笔笔触（单次 FillTriangles，相邻四边形只共边不重叠）。
        /// </summary>
        /// <param name="x0">起点 x（笔尾）</param>
        /// <param name="x1">终点 x（笔头）</param>
        /// <param name="cy">中线 y</param>
        /// <param name="maxHalf">最粗处的半高</param>
        /// <param name="color">笔触颜色（整条只混合一次）</param>
        public static void BrushStroke(float x0, float x1, float cy, float maxHalf, Color color)
        {
            const int Segs = 28;

            float span = x1 - x0;
            if (Mathf.Abs(span) < 1f || maxHalf < 0.4f)
                return;

            var verts = new Float2[Segs * 6];
            for (int i = 0; i < Segs; i++)
            {
                float t0 = i / (float)Segs;
                float t1 = (i + 1) / (float)Segs;
                float w0 = Profile(t0) * maxHalf;
                float w1 = Profile(t1) * maxHalf;
                float a = x0 + span * t0;
                float b = x0 + span * t1;

                int k = i * 6;
                verts[k] = new Float2(a, cy - w0);
                verts[k + 1] = new Float2(a, cy + w0);
                verts[k + 2] = new Float2(b, cy - w1);

                verts[k + 3] = new Float2(a, cy + w0);
                verts[k + 4] = new Float2(b, cy + w1);
                verts[k + 5] = new Float2(b, cy - w1);
            }

            Render2D.FillTriangles(verts, color);
        }

        /// <summary>笔触粗细轮廓：两头尖、肚子略偏笔尾一侧。</summary>
        private static float Profile(float t)
        {
            return Mathf.Sin(Mathf.Pow(CcPalette.Clamp01(t), 0.78f) * Mathf.Pi);
        }

        /// <summary>
        /// 椭圆填充（Render2D 无椭圆图元，用中心三角扇近似；扇内三角形只共边不重叠）。
        /// </summary>
        public static void Ellipse(Float2 center, float rx, float ry, Color color)
        {
            const int Segs = 24;
            if (rx <= 0.4f || ry <= 0.4f)
                return;

            var verts = new Float2[Segs * 3];
            for (int i = 0; i < Segs; i++)
            {
                float a1 = i / (float)Segs * Mathf.TwoPi;
                float a2 = (i + 1) / (float)Segs * Mathf.TwoPi;
                int k = i * 3;
                verts[k] = center;
                verts[k + 1] = new Float2(center.X + Mathf.Cos(a1) * rx, center.Y + Mathf.Sin(a1) * ry);
                verts[k + 2] = new Float2(center.X + Mathf.Cos(a2) * rx, center.Y + Mathf.Sin(a2) * ry);
            }

            Render2D.FillTriangles(verts, color);
        }

        /// <summary>
        /// 水墨人像剪影（发髻 + 头 + 肩），用于面容卡在缺美术缩略图时仍读作"人像"。
        /// 全部以同一**实色**绘制：重叠区不会被二次混合，避免又画出一圈假边框
        /// （半透明叠加的坑见罗盘带）。
        /// </summary>
        public static void InkBust(Rectangle box, Color ink)
        {
            float cx = box.X + box.Width * 0.5f;

            // 肩与身：等腰梯形（顶边为颈口）
            float bodyTop = box.Y + box.Height * 0.52f;
            float bodyBottom = box.Y + box.Height * 0.96f;
            float neckHalf = box.Width * 0.09f;
            float shoulderHalf = box.Width * 0.42f;
            float slopeHalf = box.Width * 0.30f;
            float slopeY = box.Y + box.Height * 0.62f;

            var centroid = new Float2(cx, (bodyTop + bodyBottom) * 0.5f);
            var outline = new Float2[]
            {
                new Float2(cx + shoulderHalf, bodyBottom),
                new Float2(cx + slopeHalf, slopeY),
                new Float2(cx + neckHalf, bodyTop),
                new Float2(cx - neckHalf, bodyTop),
                new Float2(cx - slopeHalf, slopeY),
                new Float2(cx - shoulderHalf, bodyBottom),
            };

            var verts = new Float2[outline.Length * 3];
            for (int i = 0; i < outline.Length; i++)
            {
                int k = i * 3;
                verts[k] = centroid;
                verts[k + 1] = outline[i];
                verts[k + 2] = outline[(i + 1) % outline.Length];
            }
            Render2D.FillTriangles(verts, ink);

            // 头（略竖的椭圆）
            var headCenter = new Float2(cx, box.Y + box.Height * 0.33f);
            Ellipse(headCenter, box.Width * 0.15f, box.Height * 0.16f, ink);

            // 发髻
            Ellipse(new Float2(cx, box.Y + box.Height * 0.13f), box.Width * 0.07f, box.Height * 0.06f, ink);
        }

        /// <summary>估算文本宽度：中日韩字形约等于字号宽，拉丁字母约 0.6 倍。</summary>
        public static float MeasureText(string text, float size)
        {
            if (string.IsNullOrEmpty(text))
                return 0f;

            float w = 0f;
            for (int i = 0; i < text.Length; i++)
                w += text[i] > 0x2E80 ? size : size * 0.6f;
            return w;
        }

        /// <summary>用主题字体角色在给定矩形内画一行文本。</summary>
        public static void Text(string text, Rectangle rect, InkWashTheme.FontRole role, float size, Color color, TextAlignment align = TextAlignment.Center)
        {
            if (string.IsNullOrEmpty(text))
                return;

            var font = InkRenderHelper.GetFontRef(role, size).GetFont();
            if (font == null)
                return;

            Render2D.DrawText(font, text, rect, color, align, TextAlignment.Center, TextWrapping.NoWrap);
        }

        /// <summary>画一枚键帽（深墨底 + 纸白字），供不便实例化子控件的绘制路径复用。</summary>
        public static void Keycap(string key, float x, float y)
        {
            float w = 14f + MeasureText(key, 12f);
            InkRenderHelper.FillRoundedRectangle(new Rectangle(x, y, w, 22f), InkWashTheme.RadiusSm, CcPalette.KeycapBg);
            Text(key, new Rectangle(x, y, w, 22f), InkWashTheme.FontRole.Number, 12f, CcPalette.KeycapText);
        }

        /// <summary>键帽的建议宽度（与 <see cref="Keycap"/> 保持一致）。</summary>
        public static float KeycapWidth(string key)
        {
            return 14f + MeasureText(key, 12f);
        }
    }

    /// <summary>
    /// 创角 / 选角舞台的雾白背景：竖直渐变 + 左上、左下墨色压暗。
    /// 参考图里角色立绘偏右，左侧留白由这块背景承担。
    /// </summary>
    public class CcMistBackdrop : ContainerControl
    {
        /// <summary>
        /// 左侧压暗强度。实测参考图左上角 rgb(58,59,71)、左下角 rgb(74,71,72)，
        /// 以墨色 (15,18,23) 叠到顶部底色 (173,177,205) 上需约 0.72 才能到位。
        /// </summary>
        public float ShadeStrength = 0.72f;

        public CcMistBackdrop()
        {
            BackgroundColor = Color.Transparent;
            ClipChildren = false;
        }

        public override void Draw()
        {
            if (Width > 0f && Height > 0f)
            {
                CcDraw.VerticalColorGradient(Float2.Zero, Width, Height, CcPalette.MistTop, CcPalette.MistBottom, 40);

                if (ShadeStrength > 0.001f)
                {
                    // 一整条从左压到右的墨色渐变，覆盖左上与左下。
                    // 不要拆成上下两半分别画：两半在屏幕中线处的 alpha 不同会留下一道横向接缝
                    // （和罗盘带那圈假边框同一类叠加/接缝问题）。
                    CcDraw.HorizontalAlphaGradient(Float2.Zero, Width * 0.42f, Height,
                        CcPalette.MistShade, ShadeStrength, 0f, 36);
                }
            }

            base.Draw();
        }
    }

    /// <summary>
    /// 键帽（参考图中的 "Space" / "Q" / "V" 小方片）：深墨底 + 纸白字。
    /// </summary>
    public class CcKeycap : ContainerControl
    {
        private const float HeightPx = 22f;
        private const float FontSize = 12f;

        private string _key = string.Empty;

        /// <summary>键名文本。</summary>
        public string Key
        {
            get => _key;
            set
            {
                _key = value ?? string.Empty;
                Width = PreferredWidth;
            }
        }

        /// <summary>为 true 时不画方片，只画一个鼠标左键轮廓（参考图"返回"前的小图标）。</summary>
        public bool MouseButtonGlyph { get; set; }

        public CcKeycap()
        {
            BackgroundColor = Color.Transparent;
            ClipChildren = false;
            Height = HeightPx;
        }

        /// <summary>按内容算出的建议宽度。</summary>
        public float PreferredWidth => MouseButtonGlyph ? 16f : CcDraw.KeycapWidth(_key);

        public override void Draw()
        {
            if (MouseButtonGlyph)
                DrawMouseGlyph();
            else
                CcDraw.Keycap(_key, 0f, 0f);

            base.Draw();
        }

        /// <summary>画一个 12x18 的鼠标轮廓 + 左键高亮，对应参考图"返回"前的图标。</summary>
        private void DrawMouseGlyph()
        {
            var body = new Rectangle(2f, 2f, 12f, 18f);
            InkRenderHelper.DrawRoundedRectangle(body, 6f, CcPalette.KeycapText, 1.4f);
            Render2D.DrawLine(new Float2(body.X + 6f, body.Y), new Float2(body.X + 6f, body.Y + 7f), CcPalette.KeycapText, 1.2f);
            Render2D.DrawLine(new Float2(body.X, body.Y + 7f), new Float2(body.X + body.Width, body.Y + 7f), CcPalette.KeycapText, 1.2f);
            InkRenderHelper.FillRoundedRectangle(new Rectangle(body.X + 1f, body.Y + 1f, 5f, 6f), 2f,
                new Color(CcPalette.KeycapText.R, CcPalette.KeycapText.G, CcPalette.KeycapText.B, 0.35f));
        }
    }

    /// <summary>
    /// 键位提示行：键帽 + 动作词（如「Space 下一步」「V 导入捏脸」）。
    /// </summary>
    public class CcHint : ContainerControl
    {
        private const float Gap = 8f;
        private const float FontSize = 14f;

        private readonly CcKeycap _keycap = new CcKeycap();
        private string _text = string.Empty;
        private Color _textColor = CcPalette.InkText;

        /// <summary>动作词。</summary>
        public string Text
        {
            get => _text;
            set
            {
                _text = value ?? string.Empty;
                ApplyWidth();
            }
        }

        /// <summary>键名；置 null 表示只显示文字不显示键帽。</summary>
        public string Key
        {
            get => _keycap.Key;
            set
            {
                _keycap.MouseButtonGlyph = false;
                _keycap.Key = value;
                _keycap.Visible = !string.IsNullOrEmpty(value);
                ApplyWidth();
            }
        }

        /// <summary>用鼠标左键图形代替键帽文本。</summary>
        public bool MouseButtonGlyph
        {
            get => _keycap.MouseButtonGlyph;
            set
            {
                _keycap.MouseButtonGlyph = value;
                _keycap.Visible = true;
                ApplyWidth();
            }
        }

        /// <summary>动作词颜色（金色块上用墨色，雾白底上用纸白由调用方决定）。</summary>
        public Color TextColor
        {
            get => _textColor;
            set => _textColor = value;
        }

        /// <summary>点击整行提示（如「V 导入捏脸」「天机赐名」）。</summary>
        public event Action Activated;

        private bool _pressed;

        public CcHint()
        {
            BackgroundColor = Color.Transparent;
            ClipChildren = false;
            Height = 22f;

            _keycap.Visible = false;
            AddChild(_keycap);
            ApplyWidth();
        }

        /// <summary>按内容重算自身与键帽宽度；改过 <see cref="Text"/>/<see cref="Key"/> 后需调用。</summary>
        public void ApplyWidth()
        {
            float keyW = _keycap.Visible ? _keycap.PreferredWidth + Gap : 0f;
            Width = keyW + CcDraw.MeasureText(_text, FontSize);

            _keycap.Size = new Float2(_keycap.PreferredWidth, Height);
            _keycap.Location = new Float2(0f, (Height - _keycap.Height) * 0.5f);
        }

        public override bool OnMouseDown(Float2 location, MouseButton button)
        {
            if (button != MouseButton.Left || Activated == null)
                return false;
            _pressed = true;
            return true;
        }

        public override bool OnMouseUp(Float2 location, MouseButton button)
        {
            if (!_pressed)
                return false;
            _pressed = false;

            Activated?.Invoke();
            return true;
        }

        public override void Draw()
        {
            float keyW = _keycap.Visible ? _keycap.Width + Gap : 0f;
            CcDraw.Text(_text, new Rectangle(keyW, 0f, Mathf.Max(0f, Width - keyW), Height),
                InkWashTheme.FontRole.Heading, FontSize, _textColor, TextAlignment.Near);

            base.Draw();
        }
    }

    /// <summary>
    /// 性别选择用的水墨笔触双档开关（参考图 图片/6.png、7.png）：
    /// 一条笔触分内外两段，选中侧鎏金、未选中侧纸灰；
    /// 选中档的字浮在笔触上方，未选中档的字沉在下方。
    /// </summary>
    public class CcBrushToggle : ContainerControl
    {
        private const float TransitionSeconds = 0.28f;
        private const float LabelFontSize = 22f;

        private readonly string[] _labels = { "男", "女" };
        private readonly float[] _speckleX = { 0.12f, 0.22f, 0.31f, 0.44f };
        private int _selected;
        private float _blend;
        private bool _pressed;

        /// <summary>0 = 左档，1 = 右档。</summary>
        public int Selected
        {
            get => _selected;
            set
            {
                int v = Mathf.Clamp(value, 0, 1);
                if (_selected == v)
                    return;
                _selected = v;
                SelectedChanged?.Invoke(v);
            }
        }

        /// <summary>左档文本，默认「男」。</summary>
        public string LeftLabel
        {
            get => _labels[0];
            set => _labels[0] = value;
        }

        /// <summary>右档文本，默认「女」。</summary>
        public string RightLabel
        {
            get => _labels[1];
            set => _labels[1] = value;
        }

        /// <summary>档位改变事件（参数为新的 <see cref="Selected"/>）。</summary>
        public event Action<int> SelectedChanged;

        public CcBrushToggle()
        {
            BackgroundColor = Color.Transparent;
            ClipChildren = false;
            Size = new Float2(760f, 90f);
        }

        public override void Update(float dt)
        {
            base.Update(dt);

            float step = dt / TransitionSeconds;
            if (_blend < _selected)
                _blend = Mathf.Min(_selected, _blend + step);
            else if (_blend > _selected)
                _blend = Mathf.Max(_selected, _blend - step);
        }

        public override bool OnMouseDown(Float2 location, MouseButton button)
        {
            if (button != MouseButton.Left)
                return false;

            _pressed = true;
            return true;
        }

        public override bool OnMouseUp(Float2 location, MouseButton button)
        {
            if (!_pressed)
                return false;

            _pressed = false;
            Selected = location.X > Width * 0.5f ? 1 : 0;
            return true;
        }

        public override void Draw()
        {
            if (Width > 0f && Height > 0f)
            {
                float cy = Height * 0.5f + 6f;
                float gap = 28f;
                float half = (Width - gap) * 0.5f;

                float rightStrength = CcPalette.Clamp01(_blend);
                float leftStrength = 1f - rightStrength;

                DrawSide(0f, half, cy, leftStrength, rightStrength, _labels[0]);
                DrawSide(Width - half, Width, cy, rightStrength, leftStrength, _labels[1]);

                // 中央交界的墨点
                InkRenderHelper.FillCircle(new Float2(Width * 0.5f, cy), 3f, CcPalette.InkText);
            }

            base.Draw();
        }

        /// <summary>画一档的笔触 + 飞溅墨点 + 文字。</summary>
        private void DrawSide(float x0, float x1, float cy, float own, float other, string label)
        {
            const float maxHalf = 13f;   // 实测参考图笔触腹部厚约 26 设计像素

            Color stroke = Color.Lerp(CcPalette.BrushIdle, CcPalette.BrushGold, own);
            stroke.A = 0.35f + 0.6f * own;
            CcDraw.BrushStroke(x0, x1, cy, maxHalf * (0.4f + 0.6f * own), stroke);

            if (own > 0.05f)
            {
                // 选中侧笔触上方的墨点飞溅
                bool leftSide = x0 < Width * 0.5f;
                for (int i = 0; i < _speckleX.Length; i++)
                {
                    float sx = leftSide ? x0 + (x1 - x0) * _speckleX[i] : x1 - (x1 - x0) * _speckleX[i];
                    float sy = cy - 16f - (i % 2) * 7f;
                    InkRenderHelper.FillCircle(new Float2(sx, sy), 1.6f, new Color(stroke.R, stroke.G, stroke.B, own * 0.7f));
                }
            }

            // 选中档的字浮在笔触上方，未选中档沉在下方
            float labelY = own >= other ? cy - 40f : cy + 14f;
            Color labelColor = Color.Lerp(CcPalette.InkIdle, CcPalette.TextGold, own);
            CcDraw.Text(label, new Rectangle((x0 + x1) * 0.5f - 40f, labelY, 80f, 30f), InkWashTheme.FontRole.Display, LabelFontSize, labelColor);
        }
    }

    /// <summary>
    /// 面容预设卡（参考图 图片/2.png、3.png 的竖版卡与 图片/4.png、5.png 的横版卡）。
    /// 项目里目前**没有面容贴图/头像缩略资源**，卡面用一枚大号书法字 + 纸面渐变占位，
    /// 选中态打朱红印章；美术到位后把 <see cref="Thumbnail"/> 指到贴图即可换成真图。
    /// </summary>
    public class CcPresetCard : ContainerControl
    {
        private const float NameFontSize = 13f;
        private bool _pressed;

        /// <summary>预设名。</summary>
        public string Name = string.Empty;

        /// <summary>全局面容索引，供列表回查选中态用（不要与基类 <c>Control.Tag</c> 混名）。</summary>
        public int PresetIndex;

        /// <summary>选中态：鎏金描边 + 朱红印章。</summary>
        public bool Selected;

        /// <summary>面容缩略图（预留，可空）。非空时铺满卡面替代占位书法字。</summary>
        public Texture Thumbnail;

        /// <summary>横向宽卡（捏脸页左侧预设列表）；false 为竖版长卡。</summary>
        public bool Wide;

        /// <summary>点击卡片。</summary>
        public event Action Activated;

        public CcPresetCard()
        {
            BackgroundColor = Color.Transparent;
            ClipChildren = false;
            Size = new Float2(150f, 300f);
        }

        public override bool OnMouseDown(Float2 location, MouseButton button)
        {
            if (button != MouseButton.Left)
                return false;
            _pressed = true;
            return true;
        }

        public override bool OnMouseUp(Float2 location, MouseButton button)
        {
            if (!_pressed)
                return false;
            _pressed = false;

            if (location.X >= 0f && location.Y >= 0f && location.X <= Width && location.Y <= Height)
                Activated?.Invoke();
            return true;
        }

        public override void Draw()
        {
            if (Width > 0f && Height > 0f)
            {
                CcDraw.VerticalColorGradient(Float2.Zero, Width, Height, CcPalette.CardTop, CcPalette.CardBottom, 24);

                if (Thumbnail != null)
                    Render2D.DrawTexture(Thumbnail, new Rectangle(0f, 0f, Width, Height), Color.White);
                else if (Wide)
                    CcDraw.InkBust(new Rectangle(Width * 0.60f, Height * 0.08f, Height * 0.84f, Height * 0.84f), CcPalette.InkIdle);
                else
                    CcDraw.InkBust(new Rectangle(0f, Height * 0.06f, Width, Height * 0.76f), CcPalette.InkIdle);

                if (!Selected)
                    Render2D.FillRectangle(new Rectangle(0f, 0f, Width, Height), new Color(0.10f, 0.11f, 0.13f, 0.32f));

                CcDraw.Text(Name, new Rectangle(10f, Height - 26f, Width - 20f, 18f), InkWashTheme.FontRole.Heading, NameFontSize,
                    Selected ? CcPalette.TextGold : Color.White, TextAlignment.Near);

                var border = new Rectangle(0f, 0f, Width - 1f, Height - 1f);
                InkRenderHelper.DrawRoundedRectangle(border, InkWashTheme.RadiusSm,
                    Selected ? CcPalette.BrushGold : new Color(1f, 1f, 1f, 0.25f), Selected ? 1.6f : 1f);

                if (Selected)
                    DrawSeal();
            }

            base.Draw();
        }

        /// <summary>右上角朱红印章（参考图选中卡的红印）。</summary>
        private void DrawSeal()
        {
            const float size = 22f;
            var rect = new Rectangle(Width - size - 8f, 8f, size, size);
            Render2D.FillRectangle(rect, new Color(CcPalette.SealRed.R, CcPalette.SealRed.G, CcPalette.SealRed.B, 0.88f));
            CcDraw.Text("印", new Rectangle(rect.X, rect.Y, rect.Width, rect.Height), InkWashTheme.FontRole.Display, 14f, Color.White);
        }
    }

    /// <summary>
    /// 顶部标签行（参考图「捏脸 / 智能捏脸」与「风雅 / 写实」），可带左右两侧的切换键帽。
    /// </summary>
    public class CcTabRow : ContainerControl
    {
        /// <summary>最多支持的标签数（命中测试数组长度）。</summary>
        private const int MaxTabs = 8;

        private const float TabGap = 44f;
        private const float FontSize = 18f;
        private const float UnderlineY = 36f;

        private string[] _tabs = Array.Empty<string>();
        private readonly float[] _tabX = new float[MaxTabs];
        private readonly float[] _tabW = new float[MaxTabs];
        private int _pressedIndex = -1;

        /// <summary>标签文本（超出 <see cref="MaxTabs"/> 的部分不显示）。</summary>
        public string[] Tabs
        {
            get => _tabs;
            set => _tabs = value ?? Array.Empty<string>();
        }

        /// <summary>当前选中标签下标。</summary>
        public int Selected { get; set; }

        /// <summary>左侧键帽文本（如 Q），为空则不画。</summary>
        public string LeftKey;

        /// <summary>右侧键帽文本（如 E），为空则不画。</summary>
        public string RightKey;

        /// <summary>标签点击事件。</summary>
        public event Action<int> TabClicked;

        /// <summary>以代码方式选中标签（键盘快捷键用），与点击等效。</summary>
        public void SelectTab(int index)
        {
            int count = Mathf.Min(_tabs.Length, MaxTabs);
            if (index < 0 || index >= count || index == Selected)
                return;

            Selected = index;
            TabClicked?.Invoke(index);
        }

        public CcTabRow()
        {
            BackgroundColor = Color.Transparent;
            ClipChildren = false;
            Size = new Float2(520f, 44f);
        }

        public override bool OnMouseDown(Float2 location, MouseButton button)
        {
            if (button != MouseButton.Left)
                return false;

            _pressedIndex = HitTest(location);
            return _pressedIndex >= 0;
        }

        public override bool OnMouseUp(Float2 location, MouseButton button)
        {
            if (_pressedIndex < 0)
                return false;

            int idx = _pressedIndex;
            _pressedIndex = -1;

            if (HitTest(location) == idx)
            {
                Selected = idx;
                TabClicked?.Invoke(idx);
            }
            return true;
        }

        private int HitTest(Float2 location)
        {
            int count = Mathf.Min(_tabs.Length, MaxTabs);
            for (int i = 0; i < count; i++)
            {
                if (_tabW[i] > 0f && location.X >= _tabX[i] && location.X <= _tabX[i] + _tabW[i] && location.Y <= Height)
                    return i;
            }
            return -1;
        }

        public override void Draw()
        {
            if (Width <= 0f || Height <= 0f)
            {
                base.Draw();
                return;
            }

            float cy = Height * 0.5f;
            float x = 0f;

            if (!string.IsNullOrEmpty(LeftKey))
            {
                CcDraw.Keycap(LeftKey, x, cy - 11f);
                x += CcDraw.KeycapWidth(LeftKey) + 20f;
            }

            int count = Mathf.Min(_tabs.Length, MaxTabs);
            for (int i = 0; i < count; i++)
            {
                float w = CcDraw.MeasureText(_tabs[i], FontSize);
                _tabX[i] = x;
                _tabW[i] = w;

                bool active = i == Selected;
                CcDraw.Text(_tabs[i], new Rectangle(x, 0f, w, Height - 8f), InkWashTheme.FontRole.Display, FontSize,
                    active ? CcPalette.TextGold : CcPalette.InkIdle);

                if (active)
                    CcDraw.BrushStroke(x - 6f, x + w + 6f, UnderlineY, 2.4f,
                        new Color(CcPalette.BrushGold.R, CcPalette.BrushGold.G, CcPalette.BrushGold.B, 0.85f));

                x += w + TabGap;
            }

            if (!string.IsNullOrEmpty(RightKey))
                CcDraw.Keycap(RightKey, Width - CcDraw.KeycapWidth(RightKey), cy - 11f);

            // 标签行下方的细分隔线
            Render2D.DrawLine(new Float2(0f, Height - 1f), new Float2(Width, Height - 1f),
                new Color(CcPalette.InkText.R, CcPalette.InkText.G, CcPalette.InkText.B, 0.18f), 1f);

            base.Draw();
        }
    }

    /// <summary>
    /// 右侧竖排功能入口（参考图「万相集 / 分享」与右下三个圆形钮）：字形图标 + 下方说明。
    /// </summary>
    public class CcIconEntry : ContainerControl
    {
        private const float GlyphFontSize = 20f;
        private const float CaptionFontSize = 12f;
        private bool _pressed;

        /// <summary>图标字形（暂用汉字/符号代替矢量图标）。</summary>
        public string Glyph = string.Empty;

        /// <summary>下方说明文本；为空表示只有圆形图标钮。</summary>
        public string Caption = string.Empty;

        /// <summary>是否画成实色圆底（参考图右下三个圆钮）。</summary>
        public bool FilledCircle;

        /// <summary>点击事件。</summary>
        public event Action Activated;

        public CcIconEntry()
        {
            BackgroundColor = Color.Transparent;
            ClipChildren = false;
            Size = new Float2(56f, 66f);
        }

        public override bool OnMouseDown(Float2 location, MouseButton button)
        {
            if (button != MouseButton.Left)
                return false;
            _pressed = true;
            return true;
        }

        public override bool OnMouseUp(Float2 location, MouseButton button)
        {
            if (!_pressed)
                return false;
            _pressed = false;

            if (location.X >= 0f && location.Y >= 0f && location.X <= Width && location.Y <= Height)
                Activated?.Invoke();
            return true;
        }

        public override void Draw()
        {
            if (Width > 0f && Height > 0f)
            {
                float circleR = FilledCircle ? 17f : 15f;
                var center = new Float2(Width * 0.5f, circleR + 2f);

                if (FilledCircle)
                    InkRenderHelper.FillCircle(center, circleR, new Color(0.16f, 0.17f, 0.20f, 0.85f));

                CcDraw.Text(Glyph, new Rectangle(center.X - circleR, center.Y - circleR, circleR * 2f, circleR * 2f),
                    InkWashTheme.FontRole.Display, GlyphFontSize, FilledCircle ? Color.White : CcPalette.TextGold);

                if (!string.IsNullOrEmpty(Caption))
                    CcDraw.Text(Caption, new Rectangle(-14f, circleR * 2f + 6f, Width + 28f, 16f), InkWashTheme.FontRole.Body, CaptionFontSize, CcPalette.InkText);
            }

            base.Draw();
        }
    }

    /// <summary>
    /// 一组圆形色板（发色 / 瞳色 / 肤色），选中项带鎏金描边。
    /// 值以离散下标写入 <c>AppearanceInfo</c> 既有的 HairColor / EyeColor / SkinColor 字段
    /// （旧创建流程这三个字段恒传 0，等于从来没带过值）。
    /// </summary>
    public class CcSwatchRow : ContainerControl
    {
        private const float DotR = 11f;
        private const float DotGap = 8f;
        private const float LabelFontSize = 13f;

        /// <summary>行标题（如「发色」）。</summary>
        public string Label = string.Empty;

        /// <summary>可选颜色。</summary>
        public Color[] Options = Array.Empty<Color>();

        /// <summary>当前选中下标。</summary>
        public int Selected { get; set; }

        /// <summary>选中变化事件。</summary>
        public event Action<int> SelectedChanged;

        public CcSwatchRow()
        {
            BackgroundColor = Color.Transparent;
            ClipChildren = false;
            Size = new Float2(360f, 30f);
        }

        private float LabelW => CcDraw.MeasureText(Label, LabelFontSize) + 16f;

        private Float2 CenterAt(int index)
        {
            return new Float2(LabelW + DotR + index * (DotR * 2f + DotGap), Height * 0.5f);
        }

        /// <summary>整行建议宽度（含所有色板）。</summary>
        public float PreferredWidth => LabelW + Options.Length * (DotR * 2f + DotGap);

        public override bool OnMouseDown(Float2 location, MouseButton button)
        {
            if (button != MouseButton.Left)
                return false;

            for (int i = 0; i < Options.Length; i++)
            {
                var center = CenterAt(i);
                float dx = location.X - center.X;
                float dy = location.Y - center.Y;
                float reach = DotR + 3f;

                if (dx * dx + dy * dy <= reach * reach)
                {
                    Selected = i;
                    SelectedChanged?.Invoke(i);
                    return true;
                }
            }
            return false;
        }

        public override void Draw()
        {
            if (Width <= 0f)
            {
                base.Draw();
                return;
            }

            CcDraw.Text(Label, new Rectangle(0f, 0f, LabelW, Height), InkWashTheme.FontRole.Body, LabelFontSize, CcPalette.InkText, TextAlignment.Near);

            for (int i = 0; i < Options.Length; i++)
            {
                var center = CenterAt(i);
                InkRenderHelper.FillCircle(center, DotR, Options[i]);

                if (i == Selected)
                    InkRenderHelper.DrawCircle(center, DotR + 3f, CcPalette.BrushGold, 1.8f);
                else
                    InkRenderHelper.DrawCircle(center, DotR, new Color(CcPalette.InkText.R, CcPalette.InkText.G, CcPalette.InkText.B, 0.35f), 1f);
            }

            base.Draw();
        }
    }

    /// <summary>
    /// 右上角「«」返回箭头（参考图 图片/2-5.png 每帧右上角都有）：双折角 + 一条右延短尾与墨点。
    /// </summary>
    public class CcChevronBack : ContainerControl
    {
        private bool _pressed;

        /// <summary>点击返回。</summary>
        public event Action Activated;

        public CcChevronBack()
        {
            BackgroundColor = Color.Transparent;
            ClipChildren = false;
            Size = new Float2(56f, 28f);
        }

        public override bool OnMouseDown(Float2 location, MouseButton button)
        {
            if (button != MouseButton.Left)
                return false;
            _pressed = true;
            return true;
        }

        public override bool OnMouseUp(Float2 location, MouseButton button)
        {
            if (!_pressed)
                return false;
            _pressed = false;
            Activated?.Invoke();
            return true;
        }

        public override void Draw()
        {
            if (Width > 0f)
            {
                var ink = CcPalette.InkText;
                float cy = Height * 0.5f;

                for (int i = 0; i < 2; i++)
                {
                    float x = 8f + i * 9f;
                    Render2D.DrawLine(new Float2(x + 9f, cy - 8f), new Float2(x, cy), ink, 2f);
                    Render2D.DrawLine(new Float2(x, cy), new Float2(x + 9f, cy + 8f), ink, 2f);
                }

                Render2D.DrawLine(new Float2(30f, cy), new Float2(Width - 4f, cy), ink, 1.5f);
                InkRenderHelper.FillCircle(new Float2(Width - 3f, cy), 2f, ink);
            }

            base.Draw();
        }
    }

    /// <summary>
    /// 右下角鎏金确认块（参考图「Space 下一步」）：金底 + 深墨键帽 + 墨色动作词。
    /// </summary>
    public class CcConfirmBlock : ContainerControl
    {
        private const float FontSize = 16f;
        private const float PadX = 18f;
        private const float KeyGap = 12f;
        private bool _pressed;

        /// <summary>快捷键名（默认 Space）。</summary>
        public string Key = "Space";

        /// <summary>动作词（默认 下一步）。</summary>
        public string Text = "下一步";

        /// <summary>点击确认。</summary>
        public event Action Activated;

        public CcConfirmBlock()
        {
            BackgroundColor = Color.Transparent;
            ClipChildren = false;
            Size = new Float2(210f, 54f);
        }

        public override bool OnMouseDown(Float2 location, MouseButton button)
        {
            if (button != MouseButton.Left)
                return false;
            _pressed = true;
            return true;
        }

        public override bool OnMouseUp(Float2 location, MouseButton button)
        {
            if (!_pressed)
                return false;
            _pressed = false;

            if (location.X >= 0f && location.Y >= 0f && location.X <= Width && location.Y <= Height)
                Activated?.Invoke();
            return true;
        }

        public override void Draw()
        {
            if (Width > 0f && Height > 0f)
            {
                Render2D.FillRectangle(new Rectangle(0f, 0f, Width, Height), CcPalette.ConfirmGold);

                float x = PadX;
                CcDraw.Keycap(Key, x, Height * 0.5f - 11f);

                x += CcDraw.KeycapWidth(Key) + KeyGap;
                CcDraw.Text(Text, new Rectangle(x, 0f, Mathf.Max(0f, Width - x - PadX), Height),
                    InkWashTheme.FontRole.Heading, FontSize, CcPalette.InkText, TextAlignment.Near);
            }

            base.Draw();
        }
    }

    /// <summary>
    /// 左下角 ID 水印（参考图「ID: 0126998214」）。
    /// </summary>
    public class CcWatermark : ContainerControl
    {
        private const float FontSize = 15f;

        /// <summary>水印文本，建议形如 <c>ID: 0126998214</c>。</summary>
        public string Text = string.Empty;

        public CcWatermark()
        {
            BackgroundColor = Color.Transparent;
            ClipChildren = false;
            Size = new Float2(240f, 22f);
        }

        public override void Draw()
        {
            if (Width > 0f)
                CcDraw.Text(Text, new Rectangle(0f, 0f, Width, Height), InkWashTheme.FontRole.Number, FontSize,
                    new Color(CcPalette.InkText.R, CcPalette.InkText.G, CcPalette.InkText.B, 0.75f), TextAlignment.Near);

            base.Draw();
        }
    }
}
