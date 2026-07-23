using FlaxEngine;
using FlaxEngine.GUI;
using Game.Character.Attributes;
using HundunWorld.Game.UI.StyleSystem;
using System;

namespace HundunWorld.Game.UI.Ink.Pages.Map
{
    /// <summary>
    /// 世界地图页面 — 对应设计方案 world-map.html。
    /// 全屏网格布局（3 列 188/1fr/256 × 3 行 56/1fr/56，padding/gap 10px）：
    /// 顶栏（区域Tab+标题+单人/多人+关闭）/ 左筛选面板 / 中央地图画布 / 右区域信息 / 底搜索栏。
    /// 严格遵循水墨主题 Token，禁止硬编码色值。
    /// </summary>
    public class WorldMapPage : ContainerControl, IInkPage
    {
        private const float Edge = 10f;
        private const float Gp = 10f;
        private const float HdrH = 56f;
        private const float SrchH = 56f;
        private const float FltrW = 188f;
        private const float InfoW = 256f;

        private static readonly string[] Regions = { "清河", "开封", "凉州", "江南", "燕北" };

        public event Action<string> NavigationRequested;
        public InkParticleSystem ParticleSystem { get; set; }

        private CharacterAttributesComponent _boundChar;

        // 顶栏
        private InkPanel _hdr;
        private RegionTab[] _regionTabs;
        private int _regionIdx;
        private ModeBtn _modeS, _modeM;
        private InkButton _close;

        // 左筛选
        private InkPanel _fltr;
        private FilterCheck[] _fchecks;
        private FilterCheck[] _gchecks;

        // 中央地图
        private MapCanvas _canvas;

        // 右信息
        private InkPanel _info;
        private Label _infoRegName;

        // 底搜索
        private InkPanel _srch;
        private QuickTag[] _qtags;
        private bool[] _qtagStates = { true, true, false, false, true };

        public void BindCharacter(CharacterAttributesComponent c)
        {
            _boundChar = c;
        }

        public WorldMapPage()
        {
            AnchorPreset = AnchorPresets.StretchAll;
            Offsets = Margin.Zero;
            BackgroundColor = InkWashTheme.Void;
            ClipChildren = false;
            AutoFocus = false;

            BuildHdr();
            BuildFltr();
            BuildMap();
            BuildInfo();
            BuildSrch();
        }

        // ===================================================================
        // 顶栏：区域Tab + 标题 + 单人/多人 + 关闭
        // ===================================================================

        private void BuildHdr()
        {
            _hdr = new InkPanel { AnchorPreset = AnchorPresets.TopLeft };
            AddChild(_hdr);

            _regionTabs = new RegionTab[Regions.Length];
            for (int i = 0; i < Regions.Length; i++)
            {
                int ci = i;
                var tab = new RegionTab(Regions[i], i == 0) { AnchorPreset = AnchorPresets.TopLeft };
                tab.Clicked += () => OnRegion(ci);
                _hdr.AddChild(tab);
                _regionTabs[i] = tab;
            }
            _regionIdx = 0;

            var title = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Text = "天下舆图",
                TextColor = InkWashTheme.GoldPrimary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 22f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                AutoFocus = false,
            };
            _hdr.AddChild(title);

            // 单人/多人 切换组（金边容器 + 两个 ModeBtn）
            var modeBox = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = Color.Transparent,
                AutoFocus = false,
            };
            _hdr.AddChild(modeBox);

            _modeS = new ModeBtn("单人", true) { AnchorPreset = AnchorPresets.TopLeft };
            _modeS.Clicked += () => SetMode(true);
            modeBox.AddChild(_modeS);
            _modeM = new ModeBtn("多人", false) { AnchorPreset = AnchorPresets.TopLeft };
            _modeM.Clicked += () => SetMode(false);
            modeBox.AddChild(_modeM);
            // 金边由 ModeToggleBorder 绘制
            modeBox.AddChild(new ModeToggleBorder());

            _close = new InkButton
            {
                Variant = InkButtonVariant.Ghost,
                ButtonSize = InkButtonSize.Sm,
                Text = "✕",
                AnchorPreset = AnchorPresets.TopLeft,
            };
            _close.ButtonClicked += (b) => NavigationRequested?.Invoke(InkPageDomIds.BackHud);
            _hdr.AddChild(_close);
        }

        private void SetMode(bool single)
        {
            _modeS.IsActive = single;
            _modeM.IsActive = !single;
        }

        private void OnRegion(int idx)
        {
            if (_regionIdx == idx) return;
            _regionIdx = idx;
            for (int i = 0; i < _regionTabs.Length; i++)
                _regionTabs[i].IsActive = (i == idx);
            if (_infoRegName != null)
                _infoRegName.Text = Regions[idx];
        }

        // ===================================================================
        // 左筛选面板
        // ===================================================================

        private void BuildFltr()
        {
            _fltr = new InkPanel { AnchorPreset = AnchorPresets.TopLeft };
            AddChild(_fltr);

            var ftitle = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Text = "▤ 筛选标记",
                TextColor = InkWashTheme.GoldBright,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 14f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                AutoFocus = false,
            };
            _fltr.AddChild(ftitle);

            var fdefs = new (string text, string sub, bool def)[]
            {
                ("界碑", "传送点", true),
                ("蹊跷", "收集品", true),
                ("宝箱", "需天赋", false),
                ("江湖故人", "", true),
            };
            _fchecks = new FilterCheck[fdefs.Length];
            for (int i = 0; i < fdefs.Length; i++)
            {
                var fc = new FilterCheck(fdefs[i].text, fdefs[i].sub, fdefs[i].def, Color.Transparent, false)
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                };
                _fltr.AddChild(fc);
                _fchecks[i] = fc;
            }

            var gtitle = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Text = "采集标记",
                TextColor = InkWashTheme.TextTertiary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                AutoFocus = false,
            };
            _fltr.AddChild(gtitle);

            var gdefs = new (string text, Color dot, bool def)[]
            {
                ("草药", InkWashTheme.ElementWood, true),
                ("树木", InkWashTheme.JadeDeep, true),
                ("走兽", InkWashTheme.ElementEarth, false),
                ("飞禽", InkWashTheme.ElementWater, false),
                ("矿物", InkWashTheme.GoldPrimary, true),
            };
            _gchecks = new FilterCheck[gdefs.Length];
            for (int i = 0; i < gdefs.Length; i++)
            {
                var fc = new FilterCheck(gdefs[i].text, null, gdefs[i].def, gdefs[i].dot, true)
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                };
                _fltr.AddChild(fc);
                _gchecks[i] = fc;
            }

            var hint = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Text = "仅显示已勾选标记",
                TextColor = InkWashTheme.TextTertiary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                AutoFocus = false,
            };
            _fltr.AddChild(hint);
        }

        // ===================================================================
        // 中央地图
        // ===================================================================

        private void BuildMap()
        {
            _canvas = new MapCanvas { AnchorPreset = AnchorPresets.TopLeft };
            AddChild(_canvas);
        }

        // ===================================================================
        // 右区域信息
        // ===================================================================

        private void BuildInfo()
        {
            _info = new InkPanel { AnchorPreset = AnchorPresets.TopLeft };
            AddChild(_info);

            _info.AddChild(MakeInfoLabel("当前区域", InkWashTheme.TextTertiary, 11f, InkWashTheme.FontRole.Body));
            _infoRegName = MakeInfoLabel(Regions[0], InkWashTheme.GoldPrimary, 32f, InkWashTheme.FontRole.Display);
            _info.AddChild(_infoRegName);
            _info.AddChild(MakeInfoLabel("◎ 坐标    X:1234  Y:5678", InkWashTheme.TextDefault, 13f, InkWashTheme.FontRole.Body));
            _info.AddChild(MakeInfoLabel("◆ 名望等级", InkWashTheme.TextSecondary, 13f, InkWashTheme.FontRole.Body));
            _info.AddChild(MakeInfoLabel("◈ 众生任务    3 个可接", InkWashTheme.GoldBright, 13f, InkWashTheme.FontRole.Number));
            _info.AddChild(MakeInfoLabel("区域概览", InkWashTheme.TextTertiary, 11f, InkWashTheme.FontRole.Body));
            _info.AddChild(MakeInfoLabel(
                "清河渡口，水陆交汇之地。商旅往来不绝，江湖人等云集于此，暗流亦随之涌动。",
                InkWashTheme.TextSecondary, 12f, InkWashTheme.FontRole.Body));
        }

        private Label MakeInfoLabel(string text, Color color, float size, InkWashTheme.FontRole role)
        {
            return new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Text = text,
                TextColor = color,
                Font = InkRenderHelper.GetFontRef(role, size),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Near,
                AutoFocus = false,
            };
        }

        // ===================================================================
        // 底搜索栏
        // ===================================================================

        private void BuildSrch()
        {
            _srch = new InkPanel { AnchorPreset = AnchorPresets.TopLeft };
            AddChild(_srch);

            // 搜索输入框（自绘底 + 图标 + 占位文字 + 筛选图标）
            var input = new SearchInput { AnchorPreset = AnchorPresets.TopLeft };
            _srch.AddChild(input);

            var qdefs = new (string text, Color dot)[]
            {
                ("草药", InkWashTheme.ElementWood),
                ("树木", InkWashTheme.JadeDeep),
                ("走兽", InkWashTheme.ElementEarth),
                ("飞禽", InkWashTheme.ElementWater),
                ("矿物", InkWashTheme.GoldPrimary),
            };
            _qtags = new QuickTag[qdefs.Length];
            for (int i = 0; i < qdefs.Length; i++)
            {
                int ci = i;
                var q = new QuickTag(qdefs[i].text, qdefs[i].dot, _qtagStates[i])
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                };
                q.Clicked += () => ToggleTag(ci);
                _srch.AddChild(q);
                _qtags[i] = q;
            }
        }

        private void ToggleTag(int idx)
        {
            _qtagStates[idx] = !_qtagStates[idx];
            _qtags[idx].IsActive = _qtagStates[idx];
        }

        // ===================================================================
        // 布局
        // ===================================================================

        public void RefreshLayout()
        {
            try
            {
                float w = Width;
                float h = Height;
                float px = Edge;
                float pw = w - Edge * 2f;

                float contentTop = Edge + HdrH + Gp;
                float contentH = h - Edge - SrchH - Gp - contentTop;
                if (contentH < 100f) contentH = 100f;

                LayoutHdr(px, pw);
                LayoutFltr(px, contentTop, contentH);
                LayoutCanvas(px, pw, contentTop, contentH);
                LayoutInfo(px, pw, contentTop, contentH);
                LayoutSrch(px, pw, h);
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[WorldMapPage] RefreshLayout: {ex.Message}");
            }
        }

        private void LayoutHdr(float px, float pw)
        {
            if (_hdr == null) return;
            _hdr.Location = new Float2(px, Edge);
            _hdr.Size = new Float2(pw, HdrH);

            float tabX = 16f;
            for (int i = 0; i < _regionTabs.Length; i++)
            {
                _regionTabs[i].Location = new Float2(tabX, 0f);
                _regionTabs[i].Size = new Float2(56f, HdrH);
                tabX += 58f;
            }

            // 标题居中
            var title = _hdr.GetChild(5) as Label;
            if (title != null)
            {
                title.Location = new Float2((pw - 200f) * 0.5f, 0f);
                title.Size = new Float2(200f, HdrH);
            }

            // 模式切换组（右侧，关闭按钮左边）
            float closeX = pw - 16f - 32f;
            _close.Location = new Float2(closeX, (HdrH - 24f) * 0.5f);
            _close.Size = new Float2(32f, 24f);

            var modeBox = _hdr.GetChild(6) as ContainerControl;
            if (modeBox != null)
            {
                float mw = 120f, mh = 26f;
                modeBox.Location = new Float2(closeX - 12f - mw, (HdrH - mh) * 0.5f);
                modeBox.Size = new Float2(mw, mh);
                _modeS.Location = Float2.Zero;
                _modeS.Size = new Float2(mw * 0.5f, mh);
                _modeM.Location = new Float2(mw * 0.5f, 0f);
                _modeM.Size = new Float2(mw * 0.5f, mh);
            }
        }

        private void LayoutFltr(float px, float contentTop, float contentH)
        {
            if (_fltr == null) return;
            _fltr.Location = new Float2(px, contentTop);
            _fltr.Size = new Float2(FltrW, contentH);

            float fy = 14f;
            // 标题
            var ftitle = _fltr.GetChild(0) as Label;
            if (ftitle != null)
            {
                ftitle.Location = new Float2(12f, fy);
                ftitle.Size = new Float2(FltrW - 24f, 22f);
                fy += 30f;
            }

            for (int i = 0; i < _fchecks.Length; i++)
            {
                _fchecks[i].Location = new Float2(6f, fy);
                _fchecks[i].Size = new Float2(FltrW - 12f, 24f);
                fy += 26f;
            }

            fy += 6f;
            var gtitle = _fltr.GetChild(1 + _fchecks.Length) as Label;
            if (gtitle != null)
            {
                gtitle.Location = new Float2(16f, fy);
                gtitle.Size = new Float2(FltrW - 28f, 18f);
                fy += 20f;
            }

            for (int i = 0; i < _gchecks.Length; i++)
            {
                _gchecks[i].Location = new Float2(14f, fy);
                _gchecks[i].Size = new Float2(FltrW - 20f, 22f);
                fy += 23f;
            }

            var hint = _fltr.GetChild(_fltr.ChildrenCount - 1) as Label;
            if (hint != null)
            {
                hint.Location = new Float2(6f, contentH - 28f);
                hint.Size = new Float2(FltrW - 12f, 18f);
            }
        }

        private void LayoutCanvas(float px, float pw, float contentTop, float contentH)
        {
            if (_canvas == null) return;
            float mapX = px + FltrW + Gp;
            float mapW = pw - FltrW - InfoW - Gp * 2f;
            _canvas.Location = new Float2(mapX, contentTop);
            _canvas.Size = new Float2(mapW, contentH);
        }

        private void LayoutInfo(float px, float pw, float contentTop, float contentH)
        {
            if (_info == null) return;
            _info.Location = new Float2(px + pw - InfoW, contentTop);
            _info.Size = new Float2(InfoW, contentH);

            float iy = 18f;
            int n = _info.ChildrenCount;
            for (int i = 0; i < n; i++)
            {
                var lbl = _info.GetChild(i) as Label;
                if (lbl == null) continue;
                float lh;
                if (i == 1) lh = 46f;          // 区域名 32px
                else if (i == n - 1) lh = 90f; // 描述
                else lh = 22f;
                lbl.Location = new Float2(16f, iy);
                lbl.Size = new Float2(InfoW - 32f, lh);
                iy += lh + (i == 1 ? 10f : 8f);
            }
        }

        private void LayoutSrch(float px, float pw, float h)
        {
            if (_srch == null) return;
            _srch.Location = new Float2(px, h - Edge - SrchH);
            _srch.Size = new Float2(pw, SrchH);

            // 搜索输入框
            var input = _srch.GetChild(0) as SearchInput;
            if (input != null)
            {
                input.Location = new Float2(14f, (SrchH - 34f) * 0.5f);
                input.Size = new Float2(320f, 34f);
            }

            float qx = 14f + 320f + 16f;
            for (int i = 0; i < _qtags.Length; i++)
            {
                _qtags[i].Location = new Float2(qx, (SrchH - 26f) * 0.5f);
                _qtags[i].Size = new Float2(68f, 26f);
                qx += 76f;
            }
        }

        public override void OnParentResized()
        {
            base.OnParentResized();
            RefreshLayout();
        }

        // ===============================================================
        // 嵌套自绘控件
        // ===============================================================

        /// <summary>区域 Tab（15px Display，激活金亮 + 2px 金色渐变下划线）。</summary>
        private sealed class RegionTab : Control
        {
            private readonly string _text;
            private bool _isActive;
            private bool _isHovered;

            public event Action Clicked;
            public bool IsActive { get => _isActive; set => _isActive = value; }

            public RegionTab(string text, bool active)
            {
                _text = text;
                _isActive = active;
                AutoFocus = false;
            }

            public override void Draw()
            {
                base.Draw();
                if (!Visible) return;
                Color color = _isActive ? InkWashTheme.GoldBright
                    : (_isHovered ? InkWashTheme.TextDefault : InkWashTheme.TextSecondary);
                var font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 15f).GetFont();
                if (font != null)
                    Render2D.DrawText(font, _text, new Rectangle(Float2.Zero, Size), color,
                        TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
                if (_isActive)
                {
                    // 2px 金色渐变下划线（透明→金→透明，三段近似）
                    float y = Height - 3f;
                    float seg = (Width - 28f) / 3f;
                    float x0 = 14f;
                    var g = InkWashTheme.GoldPrimary;
                    var c1 = new Color(g.R, g.G, g.B, 0.25f);
                    var c2 = new Color(g.R, g.G, g.B, 1f);
                    Render2D.FillRectangle(new Rectangle(x0, y, seg, 2f), c1);
                    Render2D.FillRectangle(new Rectangle(x0 + seg, y, seg, 2f), c2);
                    Render2D.FillRectangle(new Rectangle(x0 + seg * 2f, y, seg, 2f), c1);
                }
            }

            public override void OnMouseEnter(Float2 location) { _isHovered = true; base.OnMouseEnter(location); }
            public override void OnMouseLeave() { _isHovered = false; base.OnMouseLeave(); }

            public override bool OnMouseUp(Float2 location, MouseButton button)
            {
                if (button == MouseButton.Left && ContainsPoint(ref location))
                    Clicked?.Invoke();
                return base.OnMouseUp(location, button);
            }
        }

        /// <summary>单人/多人切换按钮（激活=金底反色文）。</summary>
        private sealed class ModeBtn : Control
        {
            private readonly string _text;
            private bool _isActive;

            public event Action Clicked;
            public bool IsActive { get => _isActive; set => _isActive = value; }

            public ModeBtn(string text, bool active)
            {
                _text = text;
                _isActive = active;
                AutoFocus = false;
            }

            public override void Draw()
            {
                base.Draw();
                if (!Visible) return;
                var rect = new Rectangle(Float2.Zero, Size);
                if (_isActive)
                    Render2D.FillRectangle(rect, InkWashTheme.GoldPrimary);
                Color tc = _isActive ? InkWashTheme.TextInverse : InkWashTheme.TextSecondary;
                var font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f).GetFont();
                if (font != null)
                    Render2D.DrawText(font, _text, rect, tc,
                        TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
            }

            public override bool OnMouseUp(Float2 location, MouseButton button)
            {
                if (button == MouseButton.Left && ContainsPoint(ref location))
                    Clicked?.Invoke();
                return base.OnMouseUp(location, button);
            }
        }

        /// <summary>模式切换组金边框（radius sm）。</summary>
        private sealed class ModeToggleBorder : Control
        {
            public ModeToggleBorder()
            {
                AutoFocus = false;
                AnchorPreset = AnchorPresets.StretchAll;
                Offsets = Margin.Zero;
            }

            public override void Draw()
            {
                base.Draw();
                if (!Visible) return;
                InkRenderHelper.DrawRoundedRectangle(new Rectangle(Float2.Zero, Size), 2f, InkWashTheme.BorderGold, 1f);
            }
        }

        /// <summary>筛选复选项（14x14 复选框 + 可选色点 + 标签 + 副标）。</summary>
        private sealed class FilterCheck : Control
        {
            private readonly string _text;
            private readonly string _sub;
            private readonly Color _dot;
            private readonly bool _showDot;
            private bool _checked;
            private bool _isHovered;

            public bool IsChecked => _checked;

            public FilterCheck(string text, string sub, bool defChecked, Color dot, bool showDot)
            {
                _text = text;
                _sub = sub;
                _dot = dot;
                _showDot = showDot;
                _checked = defChecked;
                AutoFocus = false;
            }

            public override void Draw()
            {
                base.Draw();
                if (!Visible) return;
                if (_isHovered)
                    InkRenderHelper.FillRoundedRectangle(new Rectangle(Float2.Zero, Size), 2f, InkWashTheme.BgHover);

                // 复选框 14x14
                float by = (Height - 14f) * 0.5f;
                var boxRect = new Rectangle(6f, by, 14f, 14f);
                if (_checked)
                {
                    InkRenderHelper.FillRoundedRectangle(boxRect, 2f, InkWashTheme.GoldPrimary);
                    InkRenderHelper.DrawRoundedRectangle(boxRect, 2f, InkWashTheme.GoldBright, 1f);
                    // 反色对勾（两短线近似）
                    Render2D.DrawLine(new Float2(9f, by + 7f), new Float2(12f, by + 10f), InkWashTheme.TextInverse, 1.5f);
                    Render2D.DrawLine(new Float2(12f, by + 10f), new Float2(17f, by + 4f), InkWashTheme.TextInverse, 1.5f);
                }
                else
                {
                    InkRenderHelper.FillRoundedRectangle(boxRect, 2f,
                        new Color(InkWashTheme.Void.R, InkWashTheme.Void.G, InkWashTheme.Void.B, 0.70f));
                    InkRenderHelper.DrawRoundedRectangle(boxRect, 2f, InkWashTheme.GoldDim, 1f);
                }

                float lx = 28f;
                // 色点（采集项）
                if (_showDot)
                {
                    InkRenderHelper.FillCircle(new Float2(lx + 4f, Height * 0.5f), 4f, _dot);
                    lx += 14f;
                }

                // 标签
                Color tc = _checked ? (_showDot ? InkWashTheme.TextSecondary : InkWashTheme.TextDefault)
                                    : InkWashTheme.TextFaint;
                var font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, _showDot ? 12f : 13f).GetFont();
                if (font != null)
                    Render2D.DrawText(font, _text, new Rectangle(lx, 0f, Width - lx - 60f, Height), tc,
                        TextAlignment.Near, TextAlignment.Center, TextWrapping.NoWrap);

                // 副标
                if (!string.IsNullOrEmpty(_sub))
                {
                    var sf = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f).GetFont();
                    if (sf != null)
                        Render2D.DrawText(sf, _sub, new Rectangle(Width - 58f, 0f, 52f, Height), InkWashTheme.TextTertiary,
                            TextAlignment.Near, TextAlignment.Center, TextWrapping.NoWrap);
                }
            }

            public override void OnMouseEnter(Float2 location) { _isHovered = true; base.OnMouseEnter(location); }
            public override void OnMouseLeave() { _isHovered = false; base.OnMouseLeave(); }

            public override bool OnMouseUp(Float2 location, MouseButton button)
            {
                if (button == MouseButton.Left && ContainsPoint(ref location))
                    _checked = !_checked;
                return base.OnMouseUp(location, button);
            }
        }

        /// <summary>搜索输入框（深底 + 搜索图标 + 占位文字 + 筛选图标）。</summary>
        private sealed class SearchInput : Control
        {
            public SearchInput() { AutoFocus = false; }

            public override void Draw()
            {
                base.Draw();
                if (!Visible) return;
                var rect = new Rectangle(Float2.Zero, Size);
                InkRenderHelper.FillRoundedRectangle(rect, 2f, InkWashTheme.BaseDefault);
                InkRenderHelper.DrawRoundedRectangle(rect, 2f, InkWashTheme.BorderFaint, 1f);

                var iconF = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 15f).GetFont();
                if (iconF != null)
                    Render2D.DrawText(iconF, "⌕", new Rectangle(10f, 0f, 20f, Height), InkWashTheme.TextTertiary,
                        TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
                var textF = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f).GetFont();
                if (textF != null)
                    Render2D.DrawText(textF, "搜索NPC/玩法/采集...", new Rectangle(34f, 0f, Width - 64f, Height),
                        InkWashTheme.TextTertiary, TextAlignment.Near, TextAlignment.Center, TextWrapping.NoWrap);
                if (iconF != null)
                    Render2D.DrawText(iconF, "≡", new Rectangle(Width - 28f, 0f, 20f, Height), InkWashTheme.TextTertiary,
                        TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
            }
        }

        /// <summary>采集快捷标签（色点 + 文字，激活金底金边）。</summary>
        private sealed class QuickTag : Control
        {
            private readonly string _text;
            private readonly Color _dot;
            private bool _isActive;

            public event Action Clicked;
            public bool IsActive { get => _isActive; set => _isActive = value; }

            public QuickTag(string text, Color dot, bool active)
            {
                _text = text;
                _dot = dot;
                _isActive = active;
                AutoFocus = false;
            }

            public override void Draw()
            {
                base.Draw();
                if (!Visible) return;
                var rect = new Rectangle(Float2.Zero, Size);
                if (_isActive)
                {
                    InkRenderHelper.FillRoundedRectangle(rect, 2f, InkWashTheme.GoldTrace);
                    InkRenderHelper.DrawRoundedRectangle(rect, 2f, InkWashTheme.BorderGold, 1f);
                }
                else
                {
                    InkRenderHelper.DrawRoundedRectangle(rect, 2f, InkWashTheme.BorderNeutralL2, 1f);
                }

                InkRenderHelper.FillCircle(new Float2(12f, Height * 0.5f), 4f, _dot);
                Color tc = _isActive ? InkWashTheme.GoldBright : InkWashTheme.TextSecondary;
                var font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f).GetFont();
                if (font != null)
                    Render2D.DrawText(font, _text, new Rectangle(22f, 0f, Width - 26f, Height), tc,
                        TextAlignment.Near, TextAlignment.Center, TextWrapping.NoWrap);
            }

            public override bool OnMouseUp(Float2 location, MouseButton button)
            {
                if (button == MouseButton.Left && ContainsPoint(ref location))
                    Clicked?.Invoke();
                return base.OnMouseUp(location, button);
            }
        }

        /// <summary>地图画布（水墨底 + 标记 + 罗盘/缩放/图例，全部自绘）。</summary>
        private sealed class MapCanvas : Control
        {
            private enum Mk { Teleport, Oddity, Chest, Npc, Monster, Player }

            private struct MData { public Mk Kind; public float X, Y; }

            private static readonly MData[] Markers =
            {
                new MData { Kind = Mk.Teleport, X = 0.30f, Y = 0.30f },
                new MData { Kind = Mk.Teleport, X = 0.64f, Y = 0.58f },
                new MData { Kind = Mk.Teleport, X = 0.78f, Y = 0.22f },
                new MData { Kind = Mk.Oddity,   X = 0.22f, Y = 0.46f },
                new MData { Kind = Mk.Oddity,   X = 0.45f, Y = 0.36f },
                new MData { Kind = Mk.Oddity,   X = 0.55f, Y = 0.70f },
                new MData { Kind = Mk.Oddity,   X = 0.72f, Y = 0.44f },
                new MData { Kind = Mk.Oddity,   X = 0.38f, Y = 0.62f },
                new MData { Kind = Mk.Chest,    X = 0.50f, Y = 0.50f },
                new MData { Kind = Mk.Chest,    X = 0.84f, Y = 0.64f },
                new MData { Kind = Mk.Npc,      X = 0.36f, Y = 0.40f },
                new MData { Kind = Mk.Npc,      X = 0.60f, Y = 0.34f },
                new MData { Kind = Mk.Npc,      X = 0.48f, Y = 0.76f },
                new MData { Kind = Mk.Npc,      X = 0.68f, Y = 0.50f },
                new MData { Kind = Mk.Monster,  X = 0.26f, Y = 0.64f },
                new MData { Kind = Mk.Monster,  X = 0.74f, Y = 0.38f },
                new MData { Kind = Mk.Monster,  X = 0.42f, Y = 0.24f },
                new MData { Kind = Mk.Player,   X = 0.52f, Y = 0.54f },
            };

            private static readonly (string text, float x, float y)[] Places =
            {
                ("清河", 0.26f, 0.20f),
                ("落雁峰", 0.60f, 0.64f),
                ("北山道", 0.80f, 0.30f),
            };

            private float _pulse;

            public MapCanvas()
            {
                AutoFocus = false;
            }

            public override void Update(float dt)
            {
                base.Update(dt);
                _pulse += dt;
            }

            public override void Draw()
            {
                base.Draw();
                if (!Visible || Width <= 0f || Height <= 0f) return;

                DrawBase();

                float mx = 16f, my = 16f;
                float mw = Width - 32f, mh = Height - 32f;
                if (mw <= 0f || mh <= 0f) return;

                DrawPlaces(mx, my, mw, mh);
                foreach (var m in Markers)
                    DrawMarker(new Float2(mx + m.X * mw, my + m.Y * mh), m.Kind);

                DrawCompass();
                DrawZoom();
                DrawLegend();

                // 金边框（radius lg）
                InkRenderHelper.DrawRoundedRectangle(new Rectangle(Float2.Zero, Size),
                    InkWashTheme.RadiusLg, InkWashTheme.BorderGold, 1f);
            }

            /// <summary>水墨底：深渊底 + 柔光 + 晕影。</summary>
            private void DrawBase()
            {
                Render2D.FillRectangle(new Rectangle(Float2.Zero, Size), InkWashTheme.Abyss);

                // 上方金柔光
                var gold = InkWashTheme.GoldPrimary;
                InkRenderHelper.FillRadialGradient(
                    new Float2(Width * 0.5f, Height * -0.10f), Height * 0.7f,
                    new Color(gold.R, gold.G, gold.B, 0.05f), new Color(gold.R, gold.G, gold.B, 0f), 20);
                // 左下青柔光
                var jade = InkWashTheme.JadeDeep;
                InkRenderHelper.FillRadialGradient(
                    new Float2(Width * 0.0f, Height * 1.0f), Height * 0.6f,
                    new Color(jade.R, jade.G, jade.B, 0.04f), new Color(jade.R, jade.G, jade.B, 0f), 20);
                // 晕影（中心透明 → 边缘深）
                var ab = InkWashTheme.Abyss;
                InkRenderHelper.FillRadialGradient(
                    new Float2(Width * 0.5f, Height * 0.4f),
                    Mathf.Max(Width, Height) * 0.75f,
                    new Color(ab.R, ab.G, ab.B, 0f), new Color(ab.R, ab.G, ab.B, 0.55f), 24);
            }

            private void DrawPlaces(float mx, float my, float mw, float mh)
            {
                var font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 20f).GetFont();
                if (font == null) return;
                var c = new Color(InkWashTheme.TextDefault.R, InkWashTheme.TextDefault.G, InkWashTheme.TextDefault.B, 0.55f);
                foreach (var p in Places)
                {
                    float x = mx + p.x * mw;
                    float y = my + p.y * mh;
                    Render2D.DrawText(font, p.text, new Rectangle(x - 50f, y - 14f, 100f, 28f), c,
                        TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
                }
            }

            private void DrawMarker(Float2 pos, Mk kind)
            {
                switch (kind)
                {
                    case Mk.Teleport:
                        DrawTeleport(pos);
                        break;
                    case Mk.Oddity:
                        InkRenderHelper.FillCircle(pos, 5f, InkWashTheme.JadeBright);
                        InkRenderHelper.DrawCircle(pos, 5f, InkWashTheme.JadeGlow, 1f);
                        break;
                    case Mk.Chest:
                    {
                        var g = InkWashTheme.GoldPrimary;
                        InkRenderHelper.FillCircle(pos, 6f, new Color(g.R, g.G, g.B, 0.40f));
                        var lf = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 9f).GetFont();
                        if (lf != null)
                            Render2D.DrawText(lf, "锁", new Rectangle(pos.X - 10f, pos.Y - 20f, 20f, 12f),
                                InkWashTheme.BloodBright, TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
                        break;
                    }
                    case Mk.Npc:
                        InkRenderHelper.FillCircle(pos, 4.5f, InkWashTheme.GoldBright);
                        InkRenderHelper.DrawCircle(pos, 4.5f, InkWashTheme.GoldDeep, 1f);
                        break;
                    case Mk.Monster:
                        InkRenderHelper.FillCircle(pos, 4.5f, InkWashTheme.BloodBright);
                        InkRenderHelper.DrawCircle(pos, 4.5f, InkWashTheme.BloodDeep, 1f);
                        break;
                    case Mk.Player:
                        DrawPlayer(pos);
                        break;
                }
            }

            /// <summary>界碑：金色菱形（空心） + 脉冲环。</summary>
            private void DrawTeleport(Float2 pos)
            {
                // 脉冲环（2.4s 扩散淡出）
                float t = (_pulse % 2.4f) / 2.4f;
                float pr = Mathf.Lerp(5.4f, 16.2f, t);
                float pa = 0.9f * (1f - t);
                var g = InkWashTheme.GoldPrimary;
                InkRenderHelper.DrawCircle(pos, pr, new Color(g.R, g.G, g.B, pa), 1f);

                // 菱形（外金内空）
                DrawDiamond(pos, 9f, InkWashTheme.GoldPrimary);
                DrawDiamond(pos, 4.5f, InkWashTheme.Void);
            }

            private static void DrawDiamond(Float2 c, float hs, Color color)
            {
                var v1 = c + new Float2(0f, -hs);
                var v2 = c + new Float2(hs, 0f);
                var v3 = c + new Float2(0f, hs);
                var v4 = c + new Float2(-hs, 0f);
                Render2D.FillTriangle(v1, v2, c, color);
                Render2D.FillTriangle(v2, v3, c, color);
                Render2D.FillTriangle(v3, v4, c, color);
                Render2D.FillTriangle(v4, v1, c, color);
            }

            /// <summary>玩家：青玉五角星 + 朝上箭头 + 辉光脉冲。</summary>
            private void DrawPlayer(Float2 pos)
            {
                float glowR = 14f + 3f * Mathf.Sin(_pulse * 3.5f);
                var jade = InkWashTheme.JadeBright;
                InkRenderHelper.FillCircle(pos, glowR, new Color(jade.R, jade.G, jade.B, 0.20f));

                // 五角星（外半径 11 / 内半径 5）
                float outer = 11f, inner = 5f;
                var pts = new Float2[10];
                for (int i = 0; i < 10; i++)
                {
                    float angle = (i * Mathf.TwoPi / 10f) - Mathf.PiOverTwo;
                    float r = (i % 2 == 0) ? outer : inner;
                    pts[i] = pos + new Float2(Mathf.Cos(angle) * r, Mathf.Sin(angle) * r);
                }
                for (int i = 0; i < 10; i++)
                {
                    int a = i, b = (i + 1) % 10;
                    Render2D.FillTriangle(pts[a], pts[b], pos, InkWashTheme.JadeBright);
                }

                // 朝上箭头
                var a1 = pos + new Float2(-5f, -16f);
                var a2 = pos + new Float2(5f, -16f);
                var a3 = pos + new Float2(0f, -24f);
                Render2D.FillTriangle(a1, a2, a3, InkWashTheme.JadeBright);
            }

            /// <summary>罗盘（右上 46x46 圆，面板底 + 金边 + 北字 + 指针）。</summary>
            private void DrawCompass()
            {
                float cx = Width - 12f - 46f;
                float cy = 12f;
                var center = new Float2(cx + 23f, cy + 23f);
                InkRenderHelper.FillCircle(center, 23f, InkWashTheme.Panel);
                InkRenderHelper.DrawCircle(center, 23f, InkWashTheme.BorderGold, 1f);

                var nf = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 11f).GetFont();
                if (nf != null)
                    Render2D.DrawText(nf, "北", new Rectangle(cx, cy + 4f, 46f, 16f), InkWashTheme.GoldBright,
                        TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);

                // 指针（东北向三角）
                var p1 = center + new Float2(6f, 2f);
                var p2 = center + new Float2(-4f, 10f);
                var p3 = center + new Float2(2f, -2f);
                Render2D.FillTriangle(p1, p2, p3, InkWashTheme.GoldPrimary);
            }

            /// <summary>缩放控件（右下竖列：+ / 108% / -）。</summary>
            private void DrawZoom()
            {
                float zw = 40f, zh = 84f;
                float zx = Width - 12f - zw;
                float zy = Height - 12f - zh;
                var rect = new Rectangle(zx, zy, zw, zh);
                InkRenderHelper.FillRoundedRectangle(rect, 4f, InkWashTheme.Panel);
                InkRenderHelper.DrawRoundedRectangle(rect, 4f, InkWashTheme.BorderGold, 1f);

                // + 按钮
                var plusRect = new Rectangle(zx + 6f, zy + 6f, 28f, 22f);
                InkRenderHelper.DrawRoundedRectangle(plusRect, 2f, InkWashTheme.GoldTrace, 1f);
                var bf = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 14f).GetFont();
                if (bf != null)
                {
                    Render2D.DrawText(bf, "+", plusRect, InkWashTheme.TextSecondary,
                        TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
                    Render2D.DrawText(bf, "−", new Rectangle(zx + 6f, zy + zh - 28f, 28f, 22f), InkWashTheme.TextSecondary,
                        TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
                }
                var lf = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 10f).GetFont();
                if (lf != null)
                    Render2D.DrawText(lf, "108%", new Rectangle(zx, zy + 34f, zw, 16f), InkWashTheme.TextTertiary,
                        TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
            }

            /// <summary>图例（左下横排：界碑/故人/妖魔）。</summary>
            private void DrawLegend()
            {
                float lw = 200f, lh = 28f;
                float lx = 12f, ly = Height - 12f - lh;
                var rect = new Rectangle(lx, ly, lw, lh);
                InkRenderHelper.FillRoundedRectangle(rect, 4f, InkWashTheme.Panel);
                InkRenderHelper.DrawRoundedRectangle(rect, 4f, InkWashTheme.BorderGold, 1f);

                var font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f).GetFont();
                float cy = ly + lh * 0.5f;

                // 界碑（菱形）
                DrawDiamond(new Float2(lx + 16f, cy), 5f, InkWashTheme.GoldPrimary);
                if (font != null)
                    Render2D.DrawText(font, "界碑", new Rectangle(lx + 26f, ly, 40f, lh), InkWashTheme.TextSecondary,
                        TextAlignment.Near, TextAlignment.Center, TextWrapping.NoWrap);

                // 故人（金点）
                InkRenderHelper.FillCircle(new Float2(lx + 82f, cy), 4f, InkWashTheme.GoldBright);
                if (font != null)
                    Render2D.DrawText(font, "故人", new Rectangle(lx + 92f, ly, 40f, lh), InkWashTheme.TextSecondary,
                        TextAlignment.Near, TextAlignment.Center, TextWrapping.NoWrap);

                // 妖魔（血点）
                InkRenderHelper.FillCircle(new Float2(lx + 148f, cy), 4f, InkWashTheme.BloodBright);
                if (font != null)
                    Render2D.DrawText(font, "妖魔", new Rectangle(lx + 158f, ly, 40f, lh), InkWashTheme.TextSecondary,
                        TextAlignment.Near, TextAlignment.Center, TextWrapping.NoWrap);
            }
        }
    }
}
