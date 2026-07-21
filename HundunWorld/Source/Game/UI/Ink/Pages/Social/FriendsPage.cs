using FlaxEngine;
using FlaxEngine.GUI;
using Game.Character.Attributes;
using HundunWorld.Game.UI.StyleSystem;
using System;

namespace HundunWorld.Game.UI.Ink.Pages.Social
{
    public class FriendsPage : ContainerControl, IInkPage
    {
        public event Action<string> NavigationRequested;

        public InkParticleSystem ParticleSystem { get; set; }

        public void BindCharacter(CharacterAttributesComponent component)
        {
        }

        private const float HeaderH = 56f;
        private const float SideW = 280f;

        private static Color Gold(float a) => new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, a);
        private static Color Blood(float a) => new Color(InkWashTheme.BloodPrimary.R, InkWashTheme.BloodPrimary.G, InkWashTheme.BloodPrimary.B, a);

        private class ClickPanel : Panel
        {
            public event System.Action Clicked;
            public event System.Action<bool> HoverChanged;
            public override bool OnMouseUp(Float2 location, MouseButton button)
            {
                if (button == MouseButton.Left) Clicked?.Invoke();
                return base.OnMouseUp(location, button);
            }
            public override void OnMouseEnter(Float2 location) { HoverChanged?.Invoke(true); base.OnMouseEnter(location); }
            public override void OnMouseLeave() { HoverChanged?.Invoke(false); base.OnMouseLeave(); }
        }

        private struct Entry
        {
            public string Name, Char, Lv, Sect, Loc, Title, Stars;
            public bool Online;
            public int Intim;
            public int Grade;
        }

        private static readonly Entry[] Friends =
        {
            new Entry{Name="剑客张三", Char="张", Lv="60", Sect="武当派", Loc="武当山 · 紫霄宫", Title="武林豪侠", Stars="★★★★★", Online=true,  Intim=8520, Grade=0},
            new Entry{Name="飞燕李四", Char="李", Lv="55", Sect="丐帮",   Loc="丐帮总舵",        Title="江湖游侠", Stars="★★★★☆", Online=true,  Intim=7200, Grade=1},
            new Entry{Name="狂刀赵六", Char="赵", Lv="52", Sect="明教",   Loc="光明顶",          Title="烈火使者", Stars="★★★★☆", Online=true,  Intim=6800, Grade=2},
            new Entry{Name="青衣王五", Char="王", Lv="48", Sect="峨眉",   Loc="金顶",            Title="青衣剑客", Stars="★★★☆☆", Online=true,  Intim=5500, Grade=3},
            new Entry{Name="幻影孙七", Char="孙", Lv="45", Sect="唐门",   Loc="暗器阁",          Title="暗影行者", Stars="★★☆☆☆", Online=true,  Intim=4200, Grade=4},
            new Entry{Name="药师周八", Char="周", Lv="40", Sect="少林",   Loc="藏经阁",          Title="药王谷传人", Stars="★★★☆☆", Online=false, Intim=3800, Grade=0},
            new Entry{Name="铁掌郑十", Char="郑", Lv="42", Sect="昆仑",   Loc="玉虚峰",          Title="铁掌无敌", Stars="★★★☆☆", Online=false, Intim=3500, Grade=0},
            new Entry{Name="琴音吴九", Char="吴", Lv="38", Sect="嵩山",   Loc="峻极峰",          Title="琴魔",    Stars="★★☆☆☆", Online=false, Intim=2800, Grade=0},
        };

        private int _sel;
        private int _tab;
        private readonly string[] _tabLbls = { "好友", "仇人", "黑名单" };

        // top bar
        private Panel _bar;
        private InkButton _back;
        private Label _titleLbl;
        private Label _countLbl;
        private InkButton _addBtn;

        // left column
        private Panel _left;
        private Panel _search;
        private InkButton[] _tabs;
        private ClickPanel[] _rows;
        private Panel[] _rowDots;
        private Panel[] _rowAvts;
        private Label[] _rowChars;
        private Label[] _rowNames;
        private Label[] _rowLvs;
        private Label[] _rowSects;
        private Label[] _rowStars;
        private Label _onlineHdr;
        private Label _offlineHdr;

        // center column
        private Panel _ctr;
        private Panel _idBox;
        private Panel _idAvt;
        private Label _idAvtChar;
        private Panel _idDot;
        private Label _idName;
        private Label _idTitle;
        private Label _idLoc;
        private Label _idSta;
        private Panel _infoGrid;
        private Label[] _infoVals;
        private Panel _intiBox;
        private Panel _intiTrack;
        private Panel _intiFill;
        private Label _intiLbl;
        private Label _intiSub;
        private Panel _tagBox;
        private InkButton[] _acts;
        private Panel _equipBox;

        // right column
        private Panel _right;

        public FriendsPage()
        {
            AnchorPreset = AnchorPresets.StretchAll;
            Offsets = Margin.Zero;
            ClipChildren = false;
            AutoFocus = false;
            Build();
        }

        private Panel NewP(Color bg, ContainerControl p)
        {
            var n = new Panel { BackgroundColor = bg, Parent = p };
            return n;
        }

        private Label NewL(string t, Color tc, InkWashTheme.FontRole fr, float fs, TextAlignment ha, ContainerControl p)
        {
            return new Label
            {
                Text = t, TextColor = tc, Font = InkRenderHelper.GetFontRef(fr, fs),
                HorizontalAlignment = ha, VerticalAlignment = TextAlignment.Center, Parent = p
            };
        }

        private InkButton NewBtn(string t, InkButtonVariant v, InkButtonSize s, Color tc, Color bc, Color bg, Action cb, ContainerControl p)
        {
            var n = new InkButton { Text = t, Variant = v, ButtonSize = s, TextColor = tc, BorderColor = bc, BackgroundColor = bg, Parent = p };
            n.Clicked += cb;
            return n;
        }

        private Color GradeBorder(int g) => g switch
        {
            1 => InkWashTheme.JadeDeep, 2 => InkWashTheme.BloodBright,
            3 => InkWashTheme.QualityRare, 4 => InkWashTheme.QualityEpic,
            _ => InkWashTheme.GoldPrimary
        };

        private Color GradeAvtBg(int g) { var b = GradeBorder(g); return new Color(b.R, b.G, b.B, 0.15f); }

        // ═══════════════ BUILD ═══════════════

        private void Build()
        {
            BuildTopBar();
            BuildLeft();
            BuildCenter();
            BuildRight();
        }

        private void BuildTopBar()
        {
            _bar = NewP(new Color(InkWashTheme.BaseSecondary.R, InkWashTheme.BaseSecondary.G, InkWashTheme.BaseSecondary.B, 0.9f), this);
            _back = NewBtn("\u2190", InkButtonVariant.Ghost, InkButtonSize.Sm, InkWashTheme.GoldPrimary, InkWashTheme.BorderGold, Color.Transparent, () => NavigationRequested?.Invoke("back-hud"), _bar);
            _titleLbl = NewL("江湖交游", InkWashTheme.GoldPrimary, InkWashTheme.FontRole.Display, 20f, TextAlignment.Near, _bar);
            _countLbl = NewL("好友 12 / 50", InkWashTheme.TextTertiary, InkWashTheme.FontRole.Body, 13f, TextAlignment.Near, _bar);
            _addBtn = NewBtn("添加好友", InkButtonVariant.Ghost, InkButtonSize.Sm, InkWashTheme.GoldPrimary, InkWashTheme.BorderGold, Gold(0.12f), () => { }, _bar);
        }

        private void BuildLeft()
        {
            _left = NewP(InkWashTheme.Panel, this);

            _search = NewP(InkWashTheme.PanelSolid, _left);
            NewL("搜索好友名号", InkWashTheme.TextTertiary, InkWashTheme.FontRole.Body, 13f, TextAlignment.Near, _search);

            _tabs = new InkButton[_tabLbls.Length];
            for (int i = 0; i < _tabLbls.Length; i++)
            {
                int ci = i;
                _tabs[i] = NewBtn(_tabLbls[i], InkButtonVariant.Ghost, InkButtonSize.Sm, InkWashTheme.TextSecondary, Color.Transparent, Color.Transparent, () => OnTab(ci), _left);
            }
            SelectTabStyle(0);

            int on = 0; foreach (var f in Friends) if (f.Online) on++;
            int off = Friends.Length - on;

            _onlineHdr = NewL("在线 " + on, InkWashTheme.TextSecondary, InkWashTheme.FontRole.Body, 11f, TextAlignment.Near, _left);
            _offlineHdr = NewL("离线 " + off, InkWashTheme.TextSecondary, InkWashTheme.FontRole.Body, 11f, TextAlignment.Near, _left);

            _rows = new ClickPanel[Friends.Length];
            _rowDots = new Panel[Friends.Length];
            _rowAvts = new Panel[Friends.Length];
            _rowChars = new Label[Friends.Length];
            _rowNames = new Label[Friends.Length];
            _rowLvs = new Label[Friends.Length];
            _rowSects = new Label[Friends.Length];
            _rowStars = new Label[Friends.Length];

            for (int i = 0; i < Friends.Length; i++)
            {
                var f = Friends[i];
                int ci = i;
                var row = new ClickPanel { Parent = _left };
                row.Clicked += () => SelectFriend(ci);
                row.HoverChanged += (e) => { if (ci != _sel) row.BackgroundColor = e ? Gold(0.06f) : Color.Transparent; };
                _rows[i] = row;

                _rowDots[i] = NewP(f.Online ? InkWashTheme.JadePrimary : InkWashTheme.TextTertiary, row);
                _rowAvts[i] = NewP(GradeAvtBg(f.Grade), row);
                _rowChars[i] = NewL(f.Char, f.Online ? GradeBorder(f.Grade) : InkWashTheme.TextTertiary, InkWashTheme.FontRole.Display, 14f, TextAlignment.Center, _rowAvts[i]);
                _rowChars[i].VerticalAlignment = TextAlignment.Center;
                _rowChars[i].AnchorPreset = AnchorPresets.StretchAll;

                _rowNames[i] = NewL(f.Name, f.Online ? InkWashTheme.TextDefault : InkWashTheme.TextSecondary, InkWashTheme.FontRole.Heading, 13f, TextAlignment.Near, row);
                _rowLvs[i] = NewL(f.Lv, InkWashTheme.GoldPrimary, InkWashTheme.FontRole.Number, 11f, TextAlignment.Near, row);
                _rowSects[i] = NewL(f.Sect, InkWashTheme.TextSecondary, InkWashTheme.FontRole.Body, 11f, TextAlignment.Near, row);
                _rowStars[i] = NewL(f.Stars, InkWashTheme.GoldPrimary, InkWashTheme.FontRole.Body, 10f, TextAlignment.Far, row);
            }
            SelectFriend(0);
        }

        private void BuildCenter()
        {
            _ctr = NewP(InkWashTheme.Void, this);
            _idBox = NewP(Color.Transparent, _ctr);

            _idAvt = NewP(Gold(0.12f), _idBox);
            _idAvtChar = NewL("张", InkWashTheme.GoldPrimary, InkWashTheme.FontRole.Display, 28f, TextAlignment.Center, _idAvt);
            _idAvtChar.VerticalAlignment = TextAlignment.Center;
            _idAvtChar.AnchorPreset = AnchorPresets.StretchAll;

            _idDot = NewP(InkWashTheme.JadePrimary, _idBox);
            _idName = NewL("剑客张三", InkWashTheme.TextDefault, InkWashTheme.FontRole.Display, 22f, TextAlignment.Near, _idBox);
            _idTitle = NewL("武林豪侠", InkWashTheme.GoldBright, InkWashTheme.FontRole.Display, 11f, TextAlignment.Near, _idBox);
            _idLoc = NewL("武当山 · 紫霄宫", InkWashTheme.TextSecondary, InkWashTheme.FontRole.Body, 13f, TextAlignment.Near, _idBox);
            _idSta = NewL("在线", InkWashTheme.JadeBright, InkWashTheme.FontRole.Body, 13f, TextAlignment.Near, _idBox);

            _infoGrid = NewP(Color.Transparent, _ctr);
            string[] inames = { "等级", "门派", "修为", "阵营" };
            string[] ivals = { "60", "武当派", "渡劫期", "正道" };
            Color[] icols = { InkWashTheme.GoldPrimary, InkWashTheme.TextDefault, InkWashTheme.TextDefault, InkWashTheme.JadeBright };
            _infoVals = new Label[4];
            for (int i = 0; i < 4; i++)
            {
                var cell = NewP(Gold(0.04f), _infoGrid);
                NewL(inames[i], InkWashTheme.TextTertiary, InkWashTheme.FontRole.Body, 11f, TextAlignment.Center, cell);
                var role = i < 2 ? InkWashTheme.FontRole.Number : InkWashTheme.FontRole.Display;
                var sz = i < 1 ? 18f : 13f;
                _infoVals[i] = NewL(ivals[i], icols[i], role, sz, TextAlignment.Center, cell);
                _infoVals[i].VerticalAlignment = TextAlignment.Center;
            }

            _intiBox = NewP(Color.Transparent, _ctr);
            _intiLbl = NewL("亲密度  ★★★★★", InkWashTheme.TextDefault, InkWashTheme.FontRole.Body, 13f, TextAlignment.Near, _intiBox);
            _intiSub = NewL("8520 / 10000  ·  距下一阶段还需 1480 点", InkWashTheme.TextTertiary, InkWashTheme.FontRole.Number, 11f, TextAlignment.Near, _intiBox);
            _intiTrack = NewP(Gold(0.08f), _intiBox);
            _intiFill = NewP(InkWashTheme.GoldPrimary, _intiTrack);

            _tagBox = NewP(Color.Transparent, _ctr);
            string[] tagN = { "好友", "结义兄弟", "同门师兄" };
            Color[] tagC = { InkWashTheme.GoldPrimary, InkWashTheme.BloodBright, InkWashTheme.JadeBright };
            for (int i = 0; i < 3; i++)
            {
                var t = NewL(tagN[i], tagC[i], InkWashTheme.FontRole.Display, 11f, TextAlignment.Center, _tagBox);
                t.BackgroundColor = new Color(tagC[i].R, tagC[i].G, tagC[i].B, 0.12f);
            }

            _acts = new InkButton[6];
            string[] actN = { "私聊", "组队", "传送", "赠礼", "飞鸽", "删除" };
            for (int i = 0; i < 6; i++)
            {
                bool del = i == 5;
                _acts[i] = NewBtn(actN[i], InkButtonVariant.Ghost, InkButtonSize.Sm,
                    del ? InkWashTheme.TextBlood : InkWashTheme.TextDefault,
                    del ? InkWashTheme.BorderVermilion : InkWashTheme.BorderGold,
                    del ? Blood(0.08f) : Gold(0.06f), () => { }, _ctr);
            }

            _equipBox = NewP(Color.Transparent, _ctr);
            NewL("装备简览", InkWashTheme.TextDefault, InkWashTheme.FontRole.Display, 14f, TextAlignment.Near, _equipBox);
            string[] eqN = { "真武剑", "紫霄袍", "凌云冠", "青玉佩" };
            Color[] eqC = { InkWashTheme.QualityLegendary, InkWashTheme.QualityEpic, InkWashTheme.QualityRare, InkWashTheme.QualityUncommon };
            string[] eqQ = { "传说", "史诗", "稀有", "精良" };
            for (int i = 0; i < 4; i++)
            {
                var slot = NewP(new Color(eqC[i].R, eqC[i].G, eqC[i].B, 0.06f), _equipBox);
                NewL(eqN[i], eqC[i], InkWashTheme.FontRole.Display, 11f, TextAlignment.Center, slot);
                NewL(eqQ[i], InkWashTheme.TextTertiary, InkWashTheme.FontRole.Body, 10f, TextAlignment.Center, slot);
            }
        }

        private void BuildRight()
        {
            _right = NewP(InkWashTheme.Panel, this);

            NewL("近期互动", InkWashTheme.TextDefault, InkWashTheme.FontRole.Display, 14f, TextAlignment.Near, _right);
            string[][] ints = {
                new[] { "组队通关副本", "2时前", "幽冥幻境 · 深渊难度" },
                new[] { "赠送礼物", "昨天", "天山雪莲 ×1" },
                new[] { "私聊消息", "3天前", "\"下次副本叫上我\"" },
                new[] { "击杀世界BOSS", "1周前", "赤焰麒麟 · 组队讨伐" },
                new[] { "结义", "2周前", "桃园结义 · 义结金兰" },
            };
            for (int i = 0; i < ints.Length; i++)
            {
                var it = NewP(Gold(0.04f), _right);
                NewL(ints[i][0], InkWashTheme.TextDefault, InkWashTheme.FontRole.Heading, 12f, TextAlignment.Near, it);
                NewL(ints[i][1], InkWashTheme.TextTertiary, InkWashTheme.FontRole.Number, 10f, TextAlignment.Far, it);
                NewL(ints[i][2], InkWashTheme.TextSecondary, InkWashTheme.FontRole.Body, 11f, TextAlignment.Near, it);
            }

            NewL("共同好友", InkWashTheme.TextDefault, InkWashTheme.FontRole.Display, 14f, TextAlignment.Near, _right);
            string[] cfs = { "李", "王", "赵", "孙" };
            string[] cfn = { "飞燕李四", "青衣王五", "狂刀赵六", "幻影孙七" };
            Color[] cfc = { InkWashTheme.JadePrimary, InkWashTheme.QualityRare, InkWashTheme.BloodPrimary, InkWashTheme.QualityEpic };
            for (int i = 0; i < 4; i++)
            {
                var av = NewP(new Color(cfc[i].R, cfc[i].G, cfc[i].B, 0.15f), _right);
                var ch = NewL(cfs[i], cfc[i], InkWashTheme.FontRole.Display, 14f, TextAlignment.Center, av);
                ch.VerticalAlignment = TextAlignment.Center;
                ch.AnchorPreset = AnchorPresets.StretchAll;
                NewL(cfn[i], InkWashTheme.TextSecondary, InkWashTheme.FontRole.Body, 10f, TextAlignment.Center, _right);
            }

            NewL("交游里程碑", InkWashTheme.TextDefault, InkWashTheme.FontRole.Display, 14f, TextAlignment.Near, _right);
            string[][] mils = {
                new[] { "相识百日", "已相识 128 天" },
                new[] { "并肩作战", "共同通关 23 次副本" },
                new[] { "礼尚往来", "互赠礼物 56 件" },
                new[] { "义结金兰", "结义 14 天" },
            };
            for (int i = 0; i < mils.Length; i++)
            {
                var mi = NewP(Gold(0.04f), _right);
                NewL(mils[i][0], InkWashTheme.GoldPrimary, InkWashTheme.FontRole.Heading, 12f, TextAlignment.Near, mi);
                NewL(mils[i][1], InkWashTheme.TextTertiary, InkWashTheme.FontRole.Body, 11f, TextAlignment.Near, mi);
            }
        }

        // ═══════════════ LAYOUT ═══════════════

        public void RefreshLayout()
        {
            float w = Width;
            float h = Height;
            if (w <= 0 || h <= 0) return;

            float pad = 12f;

            // header
            _bar.Location = Float2.Zero;
            _bar.Size = new Float2(w, HeaderH);

            float bs = 32f;
            _back.Location = new Float2(pad, (HeaderH - bs) * 0.5f);
            _back.Size = new Float2(bs, bs);

            float tx = pad + bs + pad;
            _titleLbl.Location = new Float2(tx, 0);
            _titleLbl.Size = new Float2(140f, HeaderH);

            float cx = tx + 150f;
            _countLbl.Location = new Float2(cx, 0);
            _countLbl.Size = new Float2(100f, HeaderH);

            float aw = 90f;
            _addBtn.Location = new Float2(w - pad - aw, (HeaderH - bs) * 0.5f);
            _addBtn.Size = new Float2(aw, bs);

            // body
            float ct = HeaderH;
            float ch = h - ct;

            // left column
            _left.Location = new Float2(0, ct);
            _left.Size = new Float2(SideW, ch);

            float lp = pad;

            _search.Location = new Float2(lp, 12f);
            _search.Size = new Float2(SideW - lp * 2f, 36f);
            foreach (var c in _search.Children)
                if (c is Label l) { l.Location = new Float2(10f, 0); l.Size = new Float2(_search.Width - 20f, 36f); }

            float ty = 56f;
            float tw = (SideW - lp * 2f - 8f) / 3f;
            for (int i = 0; i < _tabs.Length; i++)
            {
                _tabs[i].Location = new Float2(lp + i * (tw + 4f), ty);
                _tabs[i].Size = new Float2(tw, 30f);
            }

            float fy = ty + 36f;
            float rowH = 44f;
            float rowGap = 1f;

            foreach (var child in _left.Children)
            {
                if (child == _onlineHdr)
                {
                    _onlineHdr.Location = new Float2(lp, fy);
                    _onlineHdr.Size = new Float2(SideW - lp * 2f, 18f);
                    fy += 22f;
                }
                else if (child == _offlineHdr)
                {
                    _offlineHdr.Location = new Float2(lp, fy);
                    _offlineHdr.Size = new Float2(SideW - lp * 2f, 18f);
                    fy += 22f;
                }
            }

            for (int i = 0; i < _rows.Length; i++)
            {
                var row = _rows[i];
                if (row == null) continue;
                row.Location = new Float2(lp, fy);
                row.Size = new Float2(SideW - lp * 2f, rowH);

                float dotS = 8f;
                _rowDots[i].Location = new Float2(4f, (rowH - dotS) * 0.5f);
                _rowDots[i].Size = new Float2(dotS, dotS);

                float avtS = 36f;
                _rowAvts[i].Location = new Float2(16f, (rowH - avtS) * 0.5f);
                _rowAvts[i].Size = new Float2(avtS, avtS);

                float nameX = 58f;
                float nameW = row.Width - nameX - 40f;
                _rowNames[i].Location = new Float2(nameX, 4f);
                _rowNames[i].Size = new Float2(nameW, 18f);

                _rowLvs[i].Location = new Float2(nameX + nameW + 2f, 4f);
                _rowLvs[i].Size = new Float2(24f, 18f);

                _rowSects[i].Location = new Float2(nameX, 22f);
                _rowSects[i].Size = new Float2(row.Width - nameX - 60f, 16f);

                _rowStars[i].Location = new Float2(row.Width - 60f, 22f);
                _rowStars[i].Size = new Float2(50f, 16f);

                fy += rowH + rowGap;
            }

            // center column
            float cw = w - SideW * 2f;
            _ctr.Location = new Float2(SideW, ct);
            _ctr.Size = new Float2(cw, ch);

            float cy = 0;
            float cp = pad;

            _idBox.Location = new Float2(0, cy);
            _idBox.Size = new Float2(cw, 96f);

            float avtBig = 64f;
            _idAvt.Location = new Float2(cp, 16f);
            _idAvt.Size = new Float2(avtBig, avtBig);

            _idDot.Location = new Float2(cp + avtBig - 12f, avtBig + 12f);
            _idDot.Size = new Float2(10f, 10f);

            float idX = cp + avtBig + 16f;
            _idName.Location = new Float2(idX, 16f);
            _idName.Size = new Float2(cw - idX - cp, 28f);

            _idTitle.Location = new Float2(idX, 44f);
            _idTitle.Size = new Float2(cw - idX - cp, 18f);

            _idLoc.Location = new Float2(idX, 66f);
            _idLoc.Size = new Float2(cw * 0.4f, 18f);

            _idSta.Location = new Float2(idX + cw * 0.4f + 8f, 66f);
            _idSta.Size = new Float2(80f, 18f);

            cy += 110f;

            _infoGrid.Location = new Float2(0, cy);
            _infoGrid.Size = new Float2(cw, 70f);
            float cellW = (cw - cp * 2f - 12f) / 4f;
            int ci = 0;
            foreach (var c in _infoGrid.Children)
            {
                if (c is Panel cell)
                {
                    cell.Location = new Float2(cp + ci * (cellW + 4f), 8f);
                    cell.Size = new Float2(cellW, 54f);
                    int si = 0;
                    foreach (var cc in cell.Children)
                    {
                        if (cc is Label cl)
                        {
                            cl.Location = si == 0 ? new Float2(0, 6f) : new Float2(0, 26f);
                            cl.Size = new Float2(cellW, si == 0 ? 18f : 24f);
                            si++;
                        }
                    }
                    ci++;
                }
            }
            cy += 78f;

            _intiBox.Location = new Float2(0, cy);
            _intiBox.Size = new Float2(cw, 48f);

            _intiLbl.Location = new Float2(cp, 4f);
            _intiLbl.Size = new Float2(cw - cp * 2f, 20f);

            _intiTrack.Location = new Float2(cp, 24f);
            _intiTrack.Size = new Float2(cw - cp * 2f, 6f);

            float pct = Mathf.Clamp(Friends[_sel].Intim / 10000f, 0, 1);
            _intiFill.Location = Float2.Zero;
            _intiFill.Size = new Float2(_intiTrack.Width * pct, 6f);

            _intiSub.Location = new Float2(cp, 32f);
            _intiSub.Size = new Float2(cw - cp * 2f, 16f);

            cy += 52f;

            _tagBox.Location = new Float2(0, cy);
            _tagBox.Size = new Float2(cw, 30f);
            float tagX = cp;
            foreach (var c in _tagBox.Children)
            {
                if (c is Label tl)
                {
                    tl.Location = new Float2(tagX, 4f);
                    tl.Size = new Float2(tl.Text.ToString().Length * 14f + 20f, 22f);
                    tagX += tl.Width + 6f;
                }
            }
            cy += 34f;

            float aGap = 6f;
            float aCols = 3f;
            float aW = (cw - cp * 2f - aGap * (aCols - 1)) / aCols;
            float aH = 34f;
            for (int i = 0; i < 3; i++)
            {
                _acts[i].Location = new Float2(cp + i * (aW + aGap), cy + 4f);
                _acts[i].Size = new Float2(aW, aH);
            }
            for (int i = 3; i < 6; i++)
            {
                _acts[i].Location = new Float2(cp + (i - 3) * (aW + aGap), cy + 4f + aH + aGap);
                _acts[i].Size = new Float2(aW, aH);
            }
            cy += 80f;

            _equipBox.Location = new Float2(0, cy);
            _equipBox.Size = new Float2(cw, 88f);

            bool eqTitle = false;
            float eqX = cp;
            float eqW = (cw - cp * 2f - 12f) / 4f;
            foreach (var c in _equipBox.Children)
            {
                if (c is Label el && !eqTitle)
                {
                    el.Location = new Float2(cp, 0);
                    el.Size = new Float2(120f, 22f);
                    eqTitle = true;
                }
                else if (c is Panel slot)
                {
                    slot.Location = new Float2(eqX, 26f);
                    slot.Size = new Float2(eqW, 58f);
                    int si = 0;
                    foreach (var sc in slot.Children)
                    {
                        if (sc is Label sl)
                        {
                            sl.Location = si == 0 ? new Float2(0, 10f) : new Float2(0, 36f);
                            sl.Size = si == 0 ? new Float2(eqW, 20f) : new Float2(eqW, 16f);
                            si++;
                        }
                    }
                    eqX += eqW + 4f;
                }
            }

            // right column
            _right.Location = new Float2(w - SideW, ct);
            _right.Size = new Float2(SideW, ch);

            float rp = pad;
            float ry = 12f;
            foreach (var c in _right.Children)
            {
                if (c is Label rl)
                {
                    string t = rl.Text.ToString();
                    if (t == "近期互动" || t == "共同好友" || t == "交游里程碑")
                    {
                        if (t == "共同好友" || t == "交游里程碑") ry += 8f;
                        rl.Location = new Float2(rp, ry);
                        rl.Size = new Float2(SideW - rp * 2, 22f);
                        ry += 26f;
                        continue;
                    }
                    if (c.Parent is Panel itp && itp != _right)
                    {
                        continue;
                    }
                    rl.Location = new Float2(rp, ry);
                    rl.Size = new Float2(SideW - rp * 2, 16f);
                    ry += 18f;
                }
                else if (c is Panel it && it.Parent == _right)
                {
                    it.Location = new Float2(rp, ry);
                    it.Size = new Float2(SideW - rp * 2, 48f);
                    float iy = 4f;
                    int ic = 0;
                    foreach (var sc in it.Children)
                    {
                        if (sc is Label sl)
                        {
                            if (ic == 0)
                            {
                                sl.Location = new Float2(8f, iy);
                                sl.Size = new Float2(it.Width - 60f, 18f);
                                iy += 20f;
                            }
                            else if (ic == 1)
                            {
                                sl.Location = new Float2(it.Width - 80f, 4f);
                                sl.Size = new Float2(68f, 18f);
                                sl.HorizontalAlignment = TextAlignment.Far;
                            }
                            else
                            {
                                sl.Location = new Float2(8f, iy);
                                sl.Size = new Float2(it.Width - 16f, 16f);
                            }
                            ic++;
                        }
                    }
                    ry += 52f;
                }
            }
        }

        // ═══════════════ INTERACTION ═══════════════

        private void OnTab(int i)
        {
            _tab = i;
            SelectTabStyle(i);
        }

        private void SelectTabStyle(int active)
        {
            for (int i = 0; i < _tabs.Length; i++)
            {
                bool a = i == active;
                _tabs[i].BackgroundColor = a ? Gold(0.12f) : Color.Transparent;
                _tabs[i].TextColor = a ? InkWashTheme.GoldPrimary : InkWashTheme.TextSecondary;
                _tabs[i].BorderColor = a ? InkWashTheme.BorderGold : Color.Transparent;
            }
        }

        private void SelectFriend(int i)
        {
            if (_sel >= 0 && _sel < _rows.Length)
                _rows[_sel].BackgroundColor = Color.Transparent;
            _sel = i;
            if (i >= 0 && i < _rows.Length)
            {
                _rows[i].BackgroundColor = Gold(0.1f);
                UpdateDetail();
            }
        }

        private void UpdateDetail()
        {
            var f = Friends[_sel];
            _idAvtChar.Text = f.Char;
            _idAvt.BackgroundColor = GradeAvtBg(f.Grade);
            _idAvtChar.TextColor = GradeBorder(f.Grade);
            _idDot.BackgroundColor = f.Online ? InkWashTheme.JadePrimary : InkWashTheme.TextTertiary;
            _idName.Text = f.Name;
            _idTitle.Text = f.Title;
            _idLoc.Text = f.Loc;
            _idSta.Text = f.Online ? "在线" : "离线";
            _idSta.TextColor = f.Online ? InkWashTheme.JadeBright : InkWashTheme.TextTertiary;

            if (_infoVals.Length >= 4)
            {
                _infoVals[0].Text = f.Lv;
                _infoVals[1].Text = f.Sect;
            }

            _intiLbl.Text = "亲密度  " + f.Stars;
            _intiSub.Text = f.Intim + " / 10000  ·  距下一阶段还需 " + (10000 - f.Intim) + " 点";
            float pct = Mathf.Clamp(f.Intim / 10000f, 0f, 1f);
            _intiFill.Size = new Float2(_intiTrack.Width * pct, 6f);
        }

        // ═══════════════ IInkPage ═══════════════

        public override void OnParentResized()
        {
            base.OnParentResized();
            RefreshLayout();
        }
    }
}
