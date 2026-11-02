using FlaxEngine;
using FlaxEngine.GUI;
using Game.Character.Attributes;
using HundunWorld.Game.Network;
using HundunWorld.Game.UI.Ink.Components;
using HundunWorld.Game.UI.StyleSystem;
using System;
using System.Collections.Generic;

namespace HundunWorld.Game.UI.Ink.Pages.Social
{
    public class FriendsPage : ContainerControl, IInkPage, IInkNavigationSource
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
            /// <summary>服务端好友角色 id；0 = 设计稿示例条目（不可删）。</summary>
            public ulong Id;
            public string Name, Char, Lv, Sect, Loc, Title, Stars;
            public bool Online;
            /// <summary>亲密度；负数 = 服务端未下发（详情栏不要编数字）。</summary>
            public int Intim;
            public int Grade;
        }


        private int _sel;
        private int _tab;
        private readonly string[] _tabLbls = { "好友", "仇人", "黑名单" };

        // 当前显示的名单：初始为设计稿示例，服务端回包后整体替换（含空名单）
        private readonly List<Entry> _friends = new List<Entry>();
        private int _friendRevision = -1;
        private float _friendRequestTimer;

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
        private Panel[] _rowBorders;
        private Panel[] _rowDots;
        private Panel[] _rowAvts;
        private Label[] _rowChars;
        private Label[] _rowNames;
        private Label[] _rowLvs;
        private Label[] _rowSects;
        private Label[] _rowStars;
        private Label _onlineHdr;
        private Label _offlineHdr;
        /// <summary>名单超出一屏时提示剩余人数（滚动尚未接入）。</summary>
        private Label _moreHint;

        /// <summary>左栏一屏能放下的最大行数；超出部分不建控件，避免行溢出面板。</summary>
        private const int MaxVisibleRows = 12;

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

            // 不再预置演示好友：没有服务端名单时就是空列表（页面自带「暂无好友」空态）。
            // 原来这里 AddRange(DemoFriends) 会让没加过好友的玩家看到 9 个假好友（2026-10-29）。
            Build();
            // 已在缓存里的服务端名单立刻覆盖示例；没有就在构造函数里发一次请求
            RefreshFromServer(forceRequest: true);
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

            // BuildLeft 里第一次建行时详情栏还没创建（UpdateDetail 会跳过），
            // 全页构建完再真正填一次详情。
            if (_friends.Count > 0)
                SelectFriend(Mathf.Clamp(_sel, 0, Mathf.Min(_friends.Count, MaxVisibleRows) - 1));
            else
                ShowEmptyDetail();
        }

        private void BuildTopBar()
        {
            _bar = NewP(new Color(InkWashTheme.BaseSecondary.R, InkWashTheme.BaseSecondary.G, InkWashTheme.BaseSecondary.B, 0.9f), this);
            _back = NewBtn("\u2190", InkButtonVariant.Ghost, InkButtonSize.Sm, InkWashTheme.GoldPrimary, InkWashTheme.BorderGold, Color.Transparent, () => NavigationRequested?.Invoke(InkPageDomIds.BackOneLevel), _bar);
            _titleLbl = NewL("江湖交游", InkWashTheme.GoldPrimary, InkWashTheme.FontRole.Display, 20f, TextAlignment.Near, _bar);
            _countLbl = NewL("好友 12 / 50", InkWashTheme.TextTertiary, InkWashTheme.FontRole.Body, 13f, TextAlignment.Near, _bar);
            _addBtn = NewBtn("添加好友", InkButtonVariant.Ghost, InkButtonSize.Sm, InkWashTheme.GoldPrimary, InkWashTheme.BorderGold, Gold(0.12f), OnAddFriendClicked, _bar);
        }

        private void BuildLeft()
        {
            _left = NewP(InkWashTheme.Panel, this);
            _left.ClipChildren = true;

            _search = NewP(InkWashTheme.PanelSolid, _left);
            NewL("搜索好友名号", InkWashTheme.TextTertiary, InkWashTheme.FontRole.Body, 13f, TextAlignment.Near, _search);

            _tabs = new InkButton[_tabLbls.Length];
            for (int i = 0; i < _tabLbls.Length; i++)
            {
                int ci = i;
                _tabs[i] = NewBtn(_tabLbls[i], InkButtonVariant.Ghost, InkButtonSize.Sm, InkWashTheme.TextSecondary, Color.Transparent, Color.Transparent, () => OnTab(ci), _left);
            }
            SelectTabStyle(0);

            int on = 0; foreach (var f in _friends) if (f.Online) on++;
            int off = _friends.Count - on;

            _onlineHdr = NewL("在线 " + on, InkWashTheme.TextSecondary, InkWashTheme.FontRole.Body, 11f, TextAlignment.Near, _left);
            _offlineHdr = NewL("离线 " + off, InkWashTheme.TextSecondary, InkWashTheme.FontRole.Body, 11f, TextAlignment.Near, _left);
            _moreHint = NewL("", InkWashTheme.TextTertiary, InkWashTheme.FontRole.Body, 10f, TextAlignment.Near, _left);
            _moreHint.Visible = false;

            RebuildRows();
        }

        /// <summary>
        /// 按当前名单重画左侧行（服务端回包/删除后调用）。
        /// 行数可变，所以数组与控件都要重建；旧行直接 Dispose，避免叠在同一个位置上。
        /// </summary>
        private void RebuildRows()
        {
            if (_rows != null)
            {
                for (int i = 0; i < _rows.Length; i++)
                {
                    var old = _rows[i];
                    if (old == null) continue;
                    old.Parent = null;
                    old.Dispose();
                }
            }

            bool friendsTab = _tab == 0;
            int total = _friends.Count;
            // 仇人/黑名单没有服务端数据：不拿好友名单冒充，直接空列表
            int count = friendsTab ? total : 0;
            int visible = Mathf.Min(count, MaxVisibleRows);
            int online = 0;
            if (friendsTab)
            {
                foreach (var f in _friends) if (f.Online) online++;
            }

            if (_onlineHdr != null)
            {
                _onlineHdr.Visible = friendsTab;
                _onlineHdr.Text = friendsTab ? "在线 " + online : "";
            }
            if (_offlineHdr != null)
            {
                _offlineHdr.Visible = friendsTab;
                _offlineHdr.Text = friendsTab ? "离线 " + (total - online) : "";
            }
            if (_countLbl != null)
            {
                _countLbl.Text = friendsTab
                    ? $"好友 {total} / 50"
                    : (_tab == 1 ? "仇人 0 / 50" : "黑名单 0 / 50");
            }
            if (_moreHint != null)
            {
                if (!friendsTab)
                {
                    _moreHint.Visible = true;
                    _moreHint.Text = _tab == 1 ? "仇人分类暂未接入服务端" : "黑名单暂未接入服务端";
                }
                else
                {
                    _moreHint.Visible = total > visible;
                    _moreHint.Text = total > visible ? $"还有 {total - visible} 位未显示（滚动待接入）" : "";
                }
            }

            _rows = new ClickPanel[visible];
            _rowBorders = new Panel[visible];
            _rowDots = new Panel[visible];
            _rowAvts = new Panel[visible];
            _rowChars = new Label[visible];
            _rowNames = new Label[visible];
            _rowLvs = new Label[visible];
            _rowSects = new Label[visible];
            _rowStars = new Label[visible];

            for (int i = 0; i < visible; i++)
            {
                var f = _friends[i];
                int ci = i;
                var row = new ClickPanel { Parent = _left };
                row.Clicked += () => SelectFriend(ci);
                row.HoverChanged += (e) => { if (ci != _sel) row.BackgroundColor = e ? Gold(0.06f) : Color.Transparent; };
                _rows[i] = row;

                _rowBorders[i] = NewP(Color.Transparent, row);

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

            if (count == 0)
            {
                _sel = -1;
                ShowEmptyDetail();
            }
            else
            {
                SelectFriend(Mathf.Clamp(_sel, 0, visible - 1));
            }

            // BuildLeft 阶段详情栏尚不存在，等整页构建完再由 Build()/刷新路径排布
            if (_ctr != null)
                RefreshLayout();
        }

        /// <summary>名单为空时的详情栏：不保留上一个人的数据。</summary>
        private void ShowEmptyDetail()
        {
            if (_idName == null) return;

            _idAvtChar.Text = "—";
            _idAvt.BackgroundColor = Color.Transparent;
            _idAvtChar.TextColor = InkWashTheme.TextTertiary;
            _idDot.BackgroundColor = InkWashTheme.TextTertiary;
            _idName.Text = _tab == 0 ? "暂无好友" : (_tab == 1 ? "暂无仇人" : "黑名单为空");
            _idTitle.Text = "";
            _idLoc.Text = "";
            _idSta.Text = "";
            _idSta.TextColor = InkWashTheme.TextTertiary;

            if (_infoVals != null)
            {
                // 四格全清：只清前两格会把「渡劫期 / 正道」这类示例值留在空态里
                for (int i = 0; i < _infoVals.Length; i++)
                    _infoVals[i].Text = "—";
            }

            if (_acts != null)
            {
                // 没有选中好友时六个互动按钮没有对象，禁用以免误点
                for (int i = 0; i < _acts.Length; i++)
                    if (_acts[i] != null) _acts[i].Enabled = false;
            }

            _intiLbl.Text = "亲密度  —";
            _intiSub.Text = "";
            _intiFill.Size = new Float2(0f, 6f);
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
            _idName = NewL("—", InkWashTheme.TextDefault, InkWashTheme.FontRole.Display, 22f, TextAlignment.Near, _idBox);
            _idTitle = NewL("", InkWashTheme.GoldBright, InkWashTheme.FontRole.Display, 11f, TextAlignment.Near, _idBox);
            _idLoc = NewL("", InkWashTheme.TextSecondary, InkWashTheme.FontRole.Body, 13f, TextAlignment.Near, _idBox);
            _idSta = NewL("", InkWashTheme.JadeBright, InkWashTheme.FontRole.Body, 13f, TextAlignment.Near, _idBox);

            _infoGrid = NewP(Color.Transparent, _ctr);
            string[] inames = { "等级", "门派", "修为", "阵营" };
            // 初值留空：真实值由选中好友的绑定写入，空态由 ShowEmptyDetail 统一清成「—」
            string[] ivals = { "—", "—", "—", "—" };
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
            _intiLbl = NewL("亲密度  ◆◆◆◆◆", InkWashTheme.TextDefault, InkWashTheme.FontRole.Body, 13f, TextAlignment.Near, _intiBox);
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
                int actIndex = i;   // for 循环变量会被闭包共享，必须按次捕获
                // 私聊（0）→ 跨页密聊交接；飞鸽（4）→ 飞鸽传书；删除（5）→ 服务端 RemoveFriend；
                // 其余动作没有实现，给出明确提示而不是静默无反应。
                Action cb;
                if (i == 0) cb = OnWhisperSelected;
                else if (i == 4) cb = () => NavigationRequested?.Invoke(InkPageDomIds.ActionFriendsToMail);
                else if (del) cb = OnDeleteSelected;
                else cb = () => HundunWorld.Game.UI.UIHelper.ShowToast(
                    actN[actIndex] + "功能尚未接入", HundunWorld.Game.UI.ToastType.Info);
                _acts[i] = NewBtn(actN[i], InkButtonVariant.Ghost, InkButtonSize.Sm,
                    del ? InkWashTheme.TextBlood : InkWashTheme.TextDefault,
                    del ? InkWashTheme.BorderVermilion : InkWashTheme.BorderGold,
                    del ? Blood(0.08f) : Gold(0.06f), cb, _ctr);
            }

            _equipBox = NewP(Color.Transparent, _ctr);
            var equipTitle = NewL("装备简览", InkWashTheme.TextDefault, InkWashTheme.FontRole.Display, 14f, TextAlignment.Near, _equipBox);
            equipTitle.Location = new Float2(12f, 0f);
            equipTitle.Size = new Float2(120f, 22f);
            // 好友装备简览没有服务端字段：一条占位，不摆四件虚构装备
            var equipEmpty = NewL("装备未公开", InkWashTheme.TextTertiary, InkWashTheme.FontRole.Body, 11f, TextAlignment.Near, _equipBox);
            // 两行都在 (0,0) 会叠成一坨（截图里「装备简览」被另一行的字盖住）
            equipEmpty.Location = new Float2(12f, 30f);
            equipEmpty.Size = new Float2(200f, 18f);
        }

        private void BuildRight()
        {
            _right = NewP(InkWashTheme.Panel, this);

            NewL("近期互动", InkWashTheme.TextDefault, InkWashTheme.FontRole.Display, 14f, TextAlignment.Near, _right);
            // 互动记录没有服务端来源：只给一条空态，不摆虚构的五条互动
            string[][] ints = {
                new[] { "暂无互动记录", "", "" },
            };
            for (int i = 0; i < ints.Length; i++)
            {
                var it = NewP(Gold(0.04f), _right);
                NewL(ints[i][0], InkWashTheme.TextDefault, InkWashTheme.FontRole.Heading, 12f, TextAlignment.Near, it);
                NewL(ints[i][1], InkWashTheme.TextTertiary, InkWashTheme.FontRole.Number, 10f, TextAlignment.Far, it);
                NewL(ints[i][2], InkWashTheme.TextSecondary, InkWashTheme.FontRole.Body, 11f, TextAlignment.Near, it);
            }

            NewL("共同好友", InkWashTheme.TextDefault, InkWashTheme.FontRole.Display, 14f, TextAlignment.Near, _right);
            // 共同好友同理：无好友时也没有共同好友
            string[] cfs = System.Array.Empty<string>();
            string[] cfn = System.Array.Empty<string>();
            Color[] cfc = System.Array.Empty<Color>();
            for (int i = 0; i < cfs.Length; i++)
            {
                var av = NewP(new Color(cfc[i].R, cfc[i].G, cfc[i].B, 0.15f), _right);
                var ch = NewL(cfs[i], cfc[i], InkWashTheme.FontRole.Display, 14f, TextAlignment.Center, av);
                ch.VerticalAlignment = TextAlignment.Center;
                ch.AnchorPreset = AnchorPresets.StretchAll;
                NewL(cfn[i], InkWashTheme.TextSecondary, InkWashTheme.FontRole.Body, 10f, TextAlignment.Center, _right);
            }

            NewL("交游里程碑", InkWashTheme.TextDefault, InkWashTheme.FontRole.Display, 14f, TextAlignment.Near, _right);
            string[][] mils = {
                new[] { "暂无里程碑", "" },
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

                _rowBorders[i].Location = Float2.Zero;
                _rowBorders[i].Size = new Float2(2f, rowH);

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

            if (_moreHint != null && _moreHint.Visible)
            {
                _moreHint.Location = new Float2(lp, fy);
                _moreHint.Size = new Float2(SideW - lp * 2f, 18f);
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

            float pct = 0f;
            if (_sel >= 0 && _sel < _friends.Count && _friends[_sel].Intim > 0)
                pct = Mathf.Clamp(_friends[_sel].Intim / 10000f, 0f, 1f);

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
            RebuildRows();
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
            {
                _rows[_sel].BackgroundColor = Color.Transparent;
                _rowBorders[_sel].BackgroundColor = Color.Transparent;
            }
            _sel = i;
            if (i >= 0 && i < _rows.Length)
            {
                _rows[i].BackgroundColor = Gold(0.1f);
                _rowBorders[i].BackgroundColor = InkWashTheme.GoldPrimary;
                UpdateDetail();
            }
        }

        private void UpdateDetail()
        {
            if (_sel < 0 || _sel >= _friends.Count) return;
            // BuildLeft 第一次建行时详情栏还没创建
            if (_ctr == null) return;

            var f = _friends[_sel];
            _idAvtChar.Text = f.Char;
            _idAvt.BackgroundColor = GradeAvtBg(f.Grade);
            _idAvtChar.TextColor = GradeBorder(f.Grade);
            _idDot.BackgroundColor = f.Online ? InkWashTheme.JadePrimary : InkWashTheme.TextTertiary;
            _idName.Text = f.Name;
            _idTitle.Text = f.Title;
            _idLoc.Text = f.Loc;
            _idSta.Text = f.Online ? "在线" : "离线";
            _idSta.TextColor = f.Online ? InkWashTheme.JadeBright : InkWashTheme.TextTertiary;

            // 有选中对象时恢复互动按钮：空态那轮把它们全禁用了，这里不恢复就会永久点不动。
            // 私聊/飞鸽/删除是真接线；组队/传送/赠礼点了会明确提示"尚未接入"，不是假动作。
            if (_acts != null)
            {
                for (int i = 0; i < _acts.Length; i++)
                    if (_acts[i] != null) _acts[i].Enabled = true;
            }

            if (_infoVals.Length >= 4)
            {
                _infoVals[0].Text = f.Lv;
                _infoVals[1].Text = f.Sect;
            }

            if (f.Intim < 0)
            {
                // 服务端名单没有亲密度字段：显示"—"，不编数字、不画进度
                _intiLbl.Text = "亲密度  —";
                _intiSub.Text = "";
                _intiFill.Size = new Float2(0f, 6f);
                return;
            }

            _intiLbl.Text = "亲密度  " + f.Stars;
            _intiSub.Text = f.Intim + " / 10000  ·  距下一阶段还需 " + (10000 - f.Intim) + " 点";
            float pct = Mathf.Clamp(f.Intim / 10000f, 0f, 1f);
            _intiFill.Size = new Float2(_intiTrack.Width * pct, 6f);
        }

        // ═══════════════ SERVER DATA ═══════════════

        /// <summary>自测标记 <c>friends-selftest.on</c> 是否已跑过（只跑一次）。</summary>
        private bool _selfTestDone;

        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);
            if (!VisibleInHierarchy) return;

            if (!_selfTestDone && HasSelfTestMarker("friends-selftest.on"))
            {
                _selfTestDone = true;
                RunSelfTestEnablePath();
            }

            _friendRequestTimer += deltaTime;
            if (_friendRequestTimer >= 8f)
            {
                _friendRequestTimer = 0f;
                FriendClientService.Instance.RequestList();
            }

            RefreshFromServer();
        }

        /// <summary>
        /// 自测：本机好友名单为空时，"选中好友 → 互动按钮恢复"这条路径永远走不到。
        /// 这里注入一条 Id=0 的示例条目（与代码里既有的"示例好友"同一语义，真实动作仍会拒绝），
        /// 走一遍 重建行 → 选中 → UpdateDetail，并把按钮启用数写回执。
        /// </summary>
        private void RunSelfTestEnablePath()
        {
            try
            {
                if (_friends.Count == 0)
                {
                    _friends.Add(new Entry
                    {
                        Id = 0UL, Name = "自测好友", Char = "测", Lv = "—", Sect = "—",
                        Loc = "—", Title = "", Stars = "", Online = true, Intim = -1,
                    });
                }
                RebuildRows();
                SelectFriend(0);

                int enabled = 0;
                if (_acts != null)
                    for (int i = 0; i < _acts.Length; i++)
                        if (_acts[i] != null && _acts[i].Enabled) enabled++;

                AppendSelfTestReceipt($"friends-selftest enabled={enabled} of={_acts?.Length ?? 0} sel={_sel}");
            }
            catch (Exception ex)
            {
                AppendSelfTestReceipt($"friends-selftest failed {ex.GetType().Name}");
            }
        }

        private static void AppendSelfTestReceipt(string line)
        {
            try
            {
                string path = System.IO.Path.Combine(
                    System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
                    "HundunWorld", "ui-selftest.out");
                System.IO.File.AppendAllText(path, line + System.Environment.NewLine);
            }
            catch
            {
                // 回执写不进去不影响功能
            }
        }

        private static bool HasSelfTestMarker(string fileName)
        {
            try
            {
                string path = System.IO.Path.Combine(
                    System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
                    "HundunWorld", fileName);
                return System.IO.File.Exists(path);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 服务端好友名单 → 页面条目。未收到过回包时保留设计稿示例；
        /// 收到回包（哪怕空名单）后整份替换，避免"示例好友"和真实好友混在一起。
        /// </summary>
        private void RefreshFromServer(bool forceRequest = false)
        {
            try
            {
                var service = FriendClientService.Instance;
                service.EnsureAttached();
                if (forceRequest)
                    service.RequestList(true);

                if (!service.HasServerData)
                    return;
                if (service.Revision == _friendRevision)
                    return;

                _friendRevision = service.Revision;

                var list = service.GetFriends();
                _friends.Clear();
                foreach (var f in list)
                {
                    if (f == null) continue;

                    string name = string.IsNullOrWhiteSpace(f.FriendName) ? ("角色" + f.FriendId) : f.FriendName;
                    _friends.Add(new Entry
                    {
                        Id = f.FriendId,
                        Name = name,
                        Char = name.Substring(0, 1),
                        Lv = f.Level > 0 ? f.Level.ToString() : "—",
                        Sect = string.IsNullOrWhiteSpace(f.Profession) ? "—" : f.Profession,
                        // 服务端名单只带 id/名字/等级/职业/在线位：其余字段显示"—"，客户端不编
                        Loc = "—",
                        Title = "好友",
                        Stars = "",
                        Online = f.IsOnline,
                        Intim = -1,
                        Grade = 0,
                    });
                }

                // 在线优先，让左侧「在线/离线」两个分组标题与行顺序一致
                _friends.Sort((a, b) => b.Online.CompareTo(a.Online));
                RebuildRows();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogWarning($"[FriendsPage] 好友名单刷新失败: {ex.Message}");
            }
        }

        /// <summary>「私聊」：把对象写进一次性请求并回战斗 HUD，由 HUD 进入密聊会话。</summary>
        private void OnWhisperSelected()
        {
            if (_sel < 0 || _sel >= _friends.Count)
                return;

            var f = _friends[_sel];
            if (f.Id == 0UL)
            {
                HundunWorld.Game.UI.UIHelper.ShowToast("示例好友不可私聊", HundunWorld.Game.UI.ToastType.Warning);
                return;
            }

            WhisperRequest.Set(f.Id, f.Name);
            NavigationRequested?.Invoke(InkPageDomIds.CombatHud);
        }

        private void OnAddFriendClicked()
        {
            HundunWorld.Game.UI.UIHelper.ShowToast("添加好友需要先选择目标玩家，入口待接入", HundunWorld.Game.UI.ToastType.Info);
        }

        private void OnDeleteSelected()
        {
            if (_sel < 0 || _sel >= _friends.Count)
                return;

            var f = _friends[_sel];
            if (f.Id == 0UL)
            {
                HundunWorld.Game.UI.UIHelper.ShowToast("示例好友不可删除", HundunWorld.Game.UI.ToastType.Warning);
                return;
            }

            var service = FriendClientService.Instance;
            service.EnsureAttached();

            if (service.RemoveFriend(f.Id))
            {
                HundunWorld.Game.UI.UIHelper.ShowToast("已发送删除请求", HundunWorld.Game.UI.ToastType.Info);
                service.RequestList(true);
            }
            else
            {
                HundunWorld.Game.UI.UIHelper.ShowToast(
                    string.IsNullOrEmpty(service.LastNotice) ? "删除失败" : service.LastNotice,
                    HundunWorld.Game.UI.ToastType.Warning);
            }
        }

        // ═══════════════ IInkPage ═══════════════

        public override void OnParentResized()
        {
            base.OnParentResized();
            RefreshLayout();
        }
    }
}
