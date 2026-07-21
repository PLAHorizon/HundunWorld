using FlaxEngine;
using FlaxEngine.GUI;
using Game.Character.Attributes;
using HundunWorld.Game.UI.StyleSystem;
using System;

namespace HundunWorld.Game.UI.Ink.Pages.Map
{
    public class WorldMapPage : ContainerControl, IInkPage
    {
        private const float Edge = 10f;
        private const float HdrH = 56f;
        private const float SrchH = 56f;
        private const float FltrW = 188f;
        private const float InfoW = 256f;
        private const float Gp = 10f;
        private const float TabPadX = 14f;
        private const float TabH = 32f;

        private static readonly string[] Regions = { "清河", "开封", "凉州", "江南", "燕北" };

        private enum Mk { Teleport, Oddity, Chest, Npc, Monster, Player }

        private struct MData { public string Name; public Mk Kind; public float X, Y; public string Desc; }

        private static readonly MData[] Markers =
        {
            new MData { Name = "清河渡口", Kind = Mk.Teleport, X = 0.30f, Y = 0.30f, Desc = "传送点" },
            new MData { Name = "落雁驿",   Kind = Mk.Teleport, X = 0.64f, Y = 0.58f, Desc = "传送点" },
            new MData { Name = "北山关",   Kind = Mk.Teleport, X = 0.78f, Y = 0.22f, Desc = "传送点" },
            new MData { Name = "玉翅蝉",   Kind = Mk.Oddity,   X = 0.22f, Y = 0.46f, Desc = "收集品" },
            new MData { Name = "彩蛛",     Kind = Mk.Oddity,   X = 0.45f, Y = 0.36f, Desc = "收集品" },
            new MData { Name = "萤蛊",     Kind = Mk.Oddity,   X = 0.55f, Y = 0.70f, Desc = "收集品" },
            new MData { Name = "蜈蚣",     Kind = Mk.Oddity,   X = 0.72f, Y = 0.44f, Desc = "收集品" },
            new MData { Name = "蝶蛹",     Kind = Mk.Oddity,   X = 0.38f, Y = 0.62f, Desc = "收集品" },
            new MData { Name = "古墓宝箱", Kind = Mk.Chest,    X = 0.50f, Y = 0.50f, Desc = "需天赋·开锁" },
            new MData { Name = "悬崖宝箱", Kind = Mk.Chest,    X = 0.84f, Y = 0.64f, Desc = "需天赋·轻功" },
            new MData { Name = "茶馆掌柜", Kind = Mk.Npc,      X = 0.36f, Y = 0.40f, Desc = "江湖故人" },
            new MData { Name = "镖师",     Kind = Mk.Npc,      X = 0.60f, Y = 0.34f, Desc = "江湖故人" },
            new MData { Name = "渔翁",     Kind = Mk.Npc,      X = 0.48f, Y = 0.76f, Desc = "江湖故人" },
            new MData { Name = "郎中",     Kind = Mk.Npc,      X = 0.68f, Y = 0.50f, Desc = "江湖故人" },
            new MData { Name = "山贼",     Kind = Mk.Monster,  X = 0.26f, Y = 0.64f, Desc = "野怪" },
            new MData { Name = "野狼",     Kind = Mk.Monster,  X = 0.74f, Y = 0.38f, Desc = "野怪" },
            new MData { Name = "毒蛇",     Kind = Mk.Monster,  X = 0.42f, Y = 0.24f, Desc = "野怪" },
            new MData { Name = "我的位置", Kind = Mk.Player,   X = 0.52f, Y = 0.54f, Desc = "" },
        };

        private struct PLabel { public string Text; public float X, Y; }

        private static readonly PLabel[] Places =
        {
            new PLabel { Text = "清河",   X = 0.26f, Y = 0.20f },
            new PLabel { Text = "落雁峰", X = 0.60f, Y = 0.64f },
            new PLabel { Text = "北山道", X = 0.80f, Y = 0.30f },
        };

        private struct FItem { public string Text; public string Sub; public bool Dot; public Color DotClr; public bool Def; }

        private static readonly FItem[] FilterItems =
        {
            new FItem { Text = "界碑",     Sub = "传送点", Def = true },
            new FItem { Text = "蹊跷",     Sub = "收集品", Def = true },
            new FItem { Text = "宝箱",     Sub = "需天赋", Def = false },
            new FItem { Text = "江湖故人", Sub = "",        Def = true },
        };

        private static readonly FItem[] GatherItems =
        {
            new FItem { Text = "草药", Dot = true, DotClr = new Color(107f / 255f, 142f / 255f, 90f / 255f, 1f), Def = true },
            new FItem { Text = "树木", Dot = true, DotClr = new Color(94f / 255f, 139f / 255f, 126f / 255f, 1f), Def = true },
            new FItem { Text = "走兽", Dot = true, DotClr = new Color(138f / 255f, 123f / 255f, 90f / 255f, 1f),  Def = false },
            new FItem { Text = "飞禽", Dot = true, DotClr = new Color(74f / 255f, 110f / 255f, 138f / 255f, 1f), Def = false },
            new FItem { Text = "矿物", Dot = true, DotClr = new Color(200f / 255f, 168f / 255f, 88f / 255f, 1f), Def = true },
        };

        private static readonly string[] QuickTags = { "草药", "树木", "走兽", "飞禽", "矿物" };
        private static readonly Color[] QuickDotClrs =
        {
            new Color(107f / 255f, 142f / 255f, 90f / 255f, 1f),
            new Color(94f / 255f, 139f / 255f, 126f / 255f, 1f),
            new Color(138f / 255f, 123f / 255f, 90f / 255f, 1f),
            new Color(74f / 255f, 110f / 255f, 138f / 255f, 1f),
            new Color(200f / 255f, 168f / 255f, 88f / 255f, 1f),
        };

        private InkPanel _hdr;
        private Label _title;
        private InkButton[] _tabs;
        private int _tabIdx;
        private InkButton _modeS, _modeM;
        private InkButton _close;
        private InkPanel _fltr;
        private FilterCheck[] _fchecks;
        private FilterCheck[] _gchecks;
        private MapCanvas _canvas;
        private InkPanel _info;
        private Label _infoRegLbl, _infoRegName, _infoCrd, _infoRep, _infoQst, _infoDsc;
        private InkPanel _srch;
        private InkButton[] _qtags;

        public event Action<string> NavigationRequested;

        public InkParticleSystem ParticleSystem { get; set; }

        private CharacterAttributesComponent _boundChar;

        public void BindCharacter(CharacterAttributesComponent c)
        {
            _boundChar = c;
        }

        public WorldMapPage()
        {
            AnchorPreset = AnchorPresets.StretchAll;
            Offsets = Margin.Zero;
            BackgroundColor = Color.Transparent;
            ClipChildren = false;
            AutoFocus = false;
            BuildHdr();
            BuildFltr();
            BuildMap();
            BuildInfo();
            BuildSrch();
        }

        private void BuildHdr()
        {
            _hdr = new InkPanel();
            _hdr.AnchorPreset = AnchorPresets.TopLeft;

            _title = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Text = "天下舆图",
                TextColor = InkWashTheme.GoldPrimary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 22f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };
            _hdr.AddChild(_title);

            _tabs = new InkButton[Regions.Length];
            for (int i = 0; i < Regions.Length; i++)
            {
                int ci = i;
                var b = new InkButton
                {
                    Variant = InkButtonVariant.Ghost,
                    ButtonSize = InkButtonSize.Sm,
                    Text = Regions[i],
                    AnchorPreset = AnchorPresets.TopLeft,
                };
                b.Clicked += () => OnTab(ci);
                _hdr.AddChild(b);
                _tabs[i] = b;
            }
            _tabIdx = 0;
            UpdateTabs();

            _modeS = new InkButton
            {
                Variant = InkButtonVariant.Primary,
                ButtonSize = InkButtonSize.Sm,
                Text = "单人",
                AnchorPreset = AnchorPresets.TopLeft,
            };
            _modeS.Clicked += () => SetMode(true);
            _hdr.AddChild(_modeS);

            _modeM = new InkButton
            {
                Variant = InkButtonVariant.Ghost,
                ButtonSize = InkButtonSize.Sm,
                Text = "多人",
                AnchorPreset = AnchorPresets.TopLeft,
            };
            _modeM.Clicked += () => SetMode(false);
            _hdr.AddChild(_modeM);

            _close = new InkButton
            {
                Variant = InkButtonVariant.Ghost,
                ButtonSize = InkButtonSize.Sm,
                Text = "✕",
                AnchorPreset = AnchorPresets.TopLeft,
            };
            _close.Clicked += () => NavigationRequested?.Invoke(InkPageDomIds.BackHud);
            _hdr.AddChild(_close);

            AddChild(_hdr);
        }

        private void SetMode(bool single)
        {
            if (single)
            {
                _modeS.Variant = InkButtonVariant.Primary;
                _modeM.Variant = InkButtonVariant.Ghost;
            }
            else
            {
                _modeS.Variant = InkButtonVariant.Ghost;
                _modeM.Variant = InkButtonVariant.Primary;
            }
        }

        private void UpdateTabs()
        {
            for (int i = 0; i < _tabs.Length; i++)
            {
                _tabs[i].TextColor = i == _tabIdx ? InkWashTheme.GoldBright : InkWashTheme.TextSecondary;
            }
        }

        private void OnTab(int idx)
        {
            if (_tabIdx == idx) return;
            _tabIdx = idx;
            UpdateTabs();
            if (_infoRegName != null)
                _infoRegName.Text = Regions[idx];
        }

        private void BuildFltr()
        {
            _fltr = new InkPanel();
            _fltr.AnchorPreset = AnchorPresets.TopLeft;

            var ftitle = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Text = "筛选标记",
                TextColor = InkWashTheme.GoldBright,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 14f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _fltr.AddChild(ftitle);

            _fchecks = new FilterCheck[FilterItems.Length];
            float fy = 36f;
            for (int i = 0; i < FilterItems.Length; i++)
            {
                var fi = FilterItems[i];
                var fc = new FilterCheck(fi.Text, fi.Sub, fi.Def, false, Color.Transparent);
                fc.Location = new Float2(6f, fy);
                _fltr.AddChild(fc);
                _fchecks[i] = fc;
                fy += 24f;
            }

            fy += 4f;
            var gtitle = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(10f, fy),
                Size = new Float2(FltrW - 20f, 18f),
                Text = "采集标记",
                TextColor = InkWashTheme.TextTertiary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _fltr.AddChild(gtitle);
            fy += 22f;

            _gchecks = new FilterCheck[GatherItems.Length];
            for (int i = 0; i < GatherItems.Length; i++)
            {
                var gi = GatherItems[i];
                var fc = new FilterCheck(gi.Text, null, gi.Def, gi.Dot, gi.DotClr);
                fc.Location = new Float2(18f, fy);
                _fltr.AddChild(fc);
                _gchecks[i] = fc;
                fy += 22f;
            }

            var hint = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Text = "仅显示已勾选标记",
                TextColor = InkWashTheme.TextTertiary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };
            _fltr.AddChild(hint);

            AddChild(_fltr);
        }

        private void BuildMap()
        {
            _canvas = new MapCanvas();
            _canvas.AnchorPreset = AnchorPresets.TopLeft;
            AddChild(_canvas);
        }

        private void BuildInfo()
        {
            _info = new InkPanel();
            _info.AnchorPreset = AnchorPresets.TopLeft;

            _infoRegLbl = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Text = "当前区域",
                TextColor = InkWashTheme.TextTertiary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _info.AddChild(_infoRegLbl);

            _infoRegName = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Text = Regions[0],
                TextColor = InkWashTheme.GoldPrimary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 32f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _info.AddChild(_infoRegName);

            var div1 = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Text = "",
                BackgroundColor = new Color(200f / 255f, 168f / 255f, 88f / 255f, 0.5f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _info.AddChild(div1);

            _infoCrd = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Text = "X:1234  Y:5678",
                TextColor = InkWashTheme.TextDefault,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _info.AddChild(_infoCrd);

            _infoRep = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Text = "名望等级：友善",
                TextColor = InkWashTheme.TextSecondary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _info.AddChild(_infoRep);

            _infoQst = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Text = "众生任务：3 个可接",
                TextColor = InkWashTheme.GoldBright,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 13f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _info.AddChild(_infoQst);

            var div2 = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Text = "",
                BackgroundColor = new Color(200f / 255f, 168f / 255f, 88f / 255f, 0.5f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _info.AddChild(div2);

            var sub = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Text = "区域概览",
                TextColor = InkWashTheme.TextTertiary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _info.AddChild(sub);

            _infoDsc = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Text = "清河渡口，水陆交汇之地。商旅往来不绝，江湖人等云集于此，暗流亦随之涌动。",
                TextColor = InkWashTheme.TextSecondary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Near,
            };
            _info.AddChild(_infoDsc);

            AddChild(_info);
        }

        private void BuildSrch()
        {
            _srch = new InkPanel();
            _srch.AnchorPreset = AnchorPresets.TopLeft;

            var inputBg = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = InkWashTheme.BaseDefault,
            };
            _srch.AddChild(inputBg);

            var searchIcon = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Text = "⌕",
                TextColor = InkWashTheme.TextTertiary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 15f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };
            _srch.AddChild(searchIcon);

            var inputText = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Text = "搜索NPC/玩法/采集...",
                TextColor = InkWashTheme.TextTertiary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _srch.AddChild(inputText);

            _qtags = new InkButton[QuickTags.Length];
            for (int i = 0; i < QuickTags.Length; i++)
            {
                int ci = i;
                var q = new InkButton
                {
                    Variant = InkButtonVariant.Ghost,
                    ButtonSize = InkButtonSize.Sm,
                    Text = QuickTags[i],
                    AnchorPreset = AnchorPresets.TopLeft,
                };
                q.Clicked += () => ToggleTag(ci);
                _srch.AddChild(q);
                _qtags[i] = q;
            }

            AddChild(_srch);
        }

        private bool[] _qtagStates;

        private void ToggleTag(int idx)
        {
            if (_qtagStates == null)
            {
                _qtagStates = new bool[] { true, true, false, false, true };
            }
            _qtagStates[idx] = !_qtagStates[idx];
            var q = _qtags[idx];
            if (_qtagStates[idx])
            {
                q.BackgroundColor = new Color(200f / 255f, 168f / 255f, 88f / 255f, 0.08f);
                q.BorderColor = InkWashTheme.BorderGold;
                q.TextColor = InkWashTheme.GoldBright;
            }
            else
            {
                q.BackgroundColor = Color.Transparent;
                q.BorderColor = InkWashTheme.BorderNeutralL2;
                q.TextColor = InkWashTheme.TextSecondary;
            }
        }

        public void RefreshLayout()
        {
            try
            {
                float w = Width;
                float h = Height;
                float px = Edge;
                float pw = w - Edge * 2f;

                float top = Edge;

                if (_hdr != null)
                {
                    _hdr.Location = new Float2(px, top);
                    _hdr.Size = new Float2(pw, HdrH);

                    float tabX = 16f;
                    for (int i = 0; i < _tabs.Length; i++)
                    {
                        _tabs[i].Location = new Float2(tabX, (HdrH - TabH) * 0.5f);
                        _tabs[i].Size = new Float2(52f, TabH);
                        tabX += 56f;
                    }

                    _title.Location = new Float2((pw - 120f) * 0.5f, 0f);
                    _title.Size = new Float2(120f, HdrH);

                    float modeX = pw - 32f - 8f - 80f;
                    _modeM.Location = new Float2(modeX + 40f, (HdrH - 24f) * 0.5f);
                    _modeM.Size = new Float2(40f, 24f);
                    _modeS.Location = new Float2(modeX, (HdrH - 24f) * 0.5f);
                    _modeS.Size = new Float2(40f, 24f);

                    _close.Location = new Float2(pw - 32f - 4f, (HdrH - 24f) * 0.5f);
                    _close.Size = new Float2(32f, 24f);
                }

                float contentTop = top + HdrH + Gp;
                float contentBot = h - Edge - SrchH - Gp;
                float contentH = contentBot - contentTop;
                if (contentH < 100f) contentH = 100f;

                if (_fltr != null)
                {
                    _fltr.Location = new Float2(px, contentTop);
                    _fltr.Size = new Float2(FltrW, contentH);

                    float fy = 10f;
                    var ftitle = _fltr.GetChild(0) as Label;
                    if (ftitle != null)
                    {
                        ftitle.Location = new Float2(10f, fy);
                        ftitle.Size = new Float2(FltrW - 20f, 22f);
                        fy += 28f;
                    }

                    for (int i = 0; i < _fchecks.Length; i++)
                    {
                        if (_fchecks[i] != null)
                        {
                            _fchecks[i].Location = new Float2(6f, fy);
                            fy += 24f;
                        }
                    }

                    fy += 6f;
                    if (_fltr.GetChild(5) is Label gt)
                    {
                        gt.Location = new Float2(10f, fy);
                        fy += 22f;
                    }

                    for (int i = 0; i < _gchecks.Length; i++)
                    {
                        if (_gchecks[i] != null)
                        {
                            _gchecks[i].Location = new Float2(18f, fy);
                            fy += 22f;
                        }
                    }

                    var hint = _fltr.GetChild(_fltr.ChildrenCount - 1) as Label;
                    if (hint != null)
                    {
                        hint.Location = new Float2(6f, contentH - 24f);
                        hint.Size = new Float2(FltrW - 12f, 18f);
                    }
                }

                if (_canvas != null)
                {
                    float mapX = px + FltrW + Gp;
                    float mapW = pw - FltrW - InfoW - Gp * 2f;
                    _canvas.Location = new Float2(mapX, contentTop);
                    _canvas.Size = new Float2(mapW, contentH);
                }

                if (_info != null)
                {
                    _info.Location = new Float2(px + pw - InfoW, contentTop);
                    _info.Size = new Float2(InfoW, contentH);

                    float iy = 18f;
                    if (_infoRegLbl != null)
                    {
                        _infoRegLbl.Location = new Float2(16f, iy);
                        _infoRegLbl.Size = new Float2(InfoW - 32f, 18f);
                        iy += 22f;
                    }
                    if (_infoRegName != null)
                    {
                        _infoRegName.Location = new Float2(16f, iy);
                        _infoRegName.Size = new Float2(InfoW - 32f, 46f);
                        iy += 50f;
                    }

                    var d1 = _info.GetChild(2) as Label;
                    if (d1 != null)
                    {
                        d1.Location = new Float2(16f, iy);
                        d1.Size = new Float2(InfoW - 32f, 1f);
                        iy += 12f;
                    }

                    if (_infoCrd != null)
                    {
                        _infoCrd.Location = new Float2(16f, iy);
                        _infoCrd.Size = new Float2(InfoW - 32f, 26f);
                        iy += 28f;
                    }
                    if (_infoRep != null)
                    {
                        _infoRep.Location = new Float2(16f, iy);
                        _infoRep.Size = new Float2(InfoW - 32f, 26f);
                        iy += 28f;
                    }
                    if (_infoQst != null)
                    {
                        _infoQst.Location = new Float2(16f, iy);
                        _infoQst.Size = new Float2(InfoW - 32f, 26f);
                        iy += 30f;
                    }

                    var d2 = _info.GetChild(6) as Label;
                    if (d2 != null)
                    {
                        d2.Location = new Float2(16f, iy);
                        d2.Size = new Float2(InfoW - 32f, 1f);
                        iy += 12f;
                    }

                    var subt = _info.GetChild(7) as Label;
                    if (subt != null)
                    {
                        subt.Location = new Float2(16f, iy);
                        subt.Size = new Float2(InfoW - 32f, 18f);
                        iy += 22f;
                    }

                    if (_infoDsc != null)
                    {
                        float dscH = contentH - iy - 18f;
                        if (dscH < 40f) dscH = 40f;
                        _infoDsc.Location = new Float2(16f, iy);
                        _infoDsc.Size = new Float2(InfoW - 32f, dscH);
                    }
                }

                if (_srch != null)
                {
                    _srch.Location = new Float2(px, h - Edge - SrchH);
                    _srch.Size = new Float2(pw, SrchH);

                    float sx = 14f;
                    if (_srch.ChildrenCount > 0 && _srch.GetChild(0) is ContainerControl ibg)
                    {
                        ibg.Location = new Float2(sx, (SrchH - 34f) * 0.5f);
                        ibg.Size = new Float2(320f, 34f);
                    }

                    if (_srch.ChildrenCount > 1)
                    {
                        var sic = _srch.GetChild(1) as Label;
                        if (sic != null)
                        {
                            sic.Location = new Float2(sx + 10f, (SrchH - 34f) * 0.5f);
                            sic.Size = new Float2(18f, 34f);
                        }
                    }
                    if (_srch.ChildrenCount > 2)
                    {
                        var sit = _srch.GetChild(2) as Label;
                        if (sit != null)
                        {
                            sit.Location = new Float2(sx + 32f, (SrchH - 34f) * 0.5f);
                            sit.Size = new Float2(280f, 34f);
                        }
                    }

                    float qx = sx + 340f;
                    for (int i = 0; i < _qtags.Length; i++)
                    {
                        if (_qtags[i] != null)
                        {
                            _qtags[i].Location = new Float2(qx, (SrchH - 26f) * 0.5f);
                            _qtags[i].Size = new Float2(64f, 26f);
                            qx += 72f;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[WorldMapPage] RefreshLayout: {ex.Message}");
            }
        }

        public override void OnParentResized()
        {
            base.OnParentResized();
            RefreshLayout();
        }

        private class FilterCheck : ContainerControl
        {
            private bool _checked;
            private readonly ContainerControl _box;
            private readonly Label _label;
            private readonly Label _dot;

            public bool IsChecked => _checked;

            public FilterCheck(string text, string sub, bool defChecked, bool showDot, Color dotClr)
            {
                _checked = defChecked;
                Size = new Float2(170f, 20f);
                AutoFocus = false;

                _box = new ContainerControl
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(0f, 3f),
                    Size = new Float2(14f, 14f),
                    BackgroundColor = _checked ? InkWashTheme.GoldPrimary : new Color(14f / 255f, 16f / 255f, 22f / 255f, 0.7f),
                };
                AddChild(_box);

                float lx = showDot ? 34f : 20f;

                if (showDot)
                {
                    _dot = new Label
                    {
                        AnchorPreset = AnchorPresets.TopLeft,
                        Location = new Float2(18f, 6f),
                        Size = new Float2(8f, 8f),
                        BackgroundColor = dotClr,
                        HorizontalAlignment = TextAlignment.Center,
                        VerticalAlignment = TextAlignment.Center,
                    };
                    AddChild(_dot);
                }
                else
                {
                    _dot = null;
                }

                _label = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(lx, 0f),
                    Size = new Float2(150f, 20f),
                    Text = text,
                    TextColor = _checked ? InkWashTheme.TextDefault : new Color(240f / 255f, 237f / 255f, 228f / 255f, 0.5f),
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                };
                AddChild(_label);

                if (!string.IsNullOrEmpty(sub))
                {
                    var subLbl = new Label
                    {
                        AnchorPreset = AnchorPresets.TopLeft,
                        Location = new Float2(lx + 60f, 0f),
                        Size = new Float2(90f, 20f),
                        Text = sub,
                        TextColor = InkWashTheme.TextTertiary,
                        Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                        HorizontalAlignment = TextAlignment.Near,
                        VerticalAlignment = TextAlignment.Center,
                    };
                    AddChild(subLbl);
                }
            }

            public override bool OnMouseDown(Float2 location, MouseButton button)
            {
                if (button == MouseButton.Left)
                {
                    _checked = !_checked;
                    _box.BackgroundColor = _checked ? InkWashTheme.GoldPrimary : new Color(14f / 255f, 16f / 255f, 22f / 255f, 0.7f);
                    _label.TextColor = _checked ? InkWashTheme.TextDefault : new Color(240f / 255f, 237f / 255f, 228f / 255f, 0.5f);
                    return true;
                }
                return base.OnMouseDown(location, button);
            }
        }

        private class MapCanvas : ContainerControl
        {
            private const float Pad = 16f;

            private float _pulse;

            public MapCanvas()
            {
                BackgroundColor = new Color(200f / 255f, 168f / 255f, 88f / 255f, 0.08f);
                ClipChildren = false;
                AutoFocus = false;
            }

            public override void Update(float dt)
            {
                base.Update(dt);
                _pulse += dt;
            }

            public override void Draw()
            {
                if (!Visible || Width <= 0f || Height <= 0f) return;

                var bgColor = new Color(10f / 255f, 11f / 255f, 16f / 255f, 1f);
                Render2D.FillRectangle(new Rectangle(0f, 0f, Width, Height), bgColor);

                Render2D.DrawRectangle(new Rectangle(0f, 0f, Width, Height), InkWashTheme.BorderGold, 1f);

                float mx = Pad;
                float my = Pad;
                float mw = Width - Pad * 2f;
                float mh = Height - Pad * 2f;
                if (mw <= 0f || mh <= 0f) return;

                DrawCompass(new Float2(mx + 24f, my + 24f));

                DrawLegend(new Float2(mx + 12f, my + mh - 32f));

                foreach (var m in Markers)
                {
                    var pos = new Float2(mx + m.X * mw, my + m.Y * mh);
                    DrawMarker(pos, m.Kind, m.Name);
                }

                foreach (var p in Places)
                {
                    var pos = new Float2(mx + p.X * mw, my + p.Y * mh);
                    DrawPlace(pos, p.Text);
                }
            }

            private void DrawCompass(Float2 center)
            {
                float r = 18f;
                int segs = 24;
                var c = InkWashTheme.PaperFaded;
                for (int i = 0; i < segs; i++)
                {
                    float a1 = (i / (float)segs) * Mathf.TwoPi;
                    float a2 = ((i + 1) / (float)segs) * Mathf.TwoPi;
                    var p1 = center + new Float2(Mathf.Cos(a1) * r, Mathf.Sin(a1) * r);
                    var p2 = center + new Float2(Mathf.Cos(a2) * r, Mathf.Sin(a2) * r);
                    Render2D.DrawLine(p1, p2, c, 1f);
                }

                var font = InkWashTheme.GetFont(InkWashTheme.FontRole.Display);
                if (font != null)
                {
                    var fr = new FontReference(font, 11f);
                    float off = r + 6f;
                    var af = fr.GetFont();
                    if (af != null)
                    {
                        var rectN = new Rectangle(center.X - 7f, center.Y - off - 7f, 14f, 14f);
                        Render2D.DrawText(af, "北", rectN, InkWashTheme.GoldBright, TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
                    }
                }
            }

            private void DrawLegend(Float2 pos)
            {
                var font = InkWashTheme.GetFont(InkWashTheme.FontRole.Body);
                if (font == null) return;
                var fr = new FontReference(font, 10f);
                var af = fr.GetFont();
                if (af == null) return;

                var items = new[] { "界碑", "故人", "妖魔" };
                float x = pos.X;
                foreach (var item in items)
                {
                    var rect = new Rectangle(x, pos.Y, 60f, 16f);
                    Render2D.DrawText(af, item, rect, InkWashTheme.TextSecondary, TextAlignment.Near, TextAlignment.Center, TextWrapping.NoWrap);
                    x += 56f;
                }
            }

            private void DrawMarker(Float2 pos, Mk kind, string name)
            {
                switch (kind)
                {
                    case Mk.Teleport:
                        DrawDiamond(pos, InkWashTheme.GoldPrimary);
                        break;
                    case Mk.Oddity:
                        InkRenderHelper.FillCircle(pos, 4f, InkWashTheme.JadeBright);
                        break;
                    case Mk.Chest:
                        InkRenderHelper.FillCircle(pos, 5f, new Color(200f / 255f, 168f / 255f, 88f / 255f, 0.4f));
                        break;
                    case Mk.Npc:
                        InkRenderHelper.FillCircle(pos, 3.5f, InkWashTheme.GoldBright);
                        break;
                    case Mk.Monster:
                        InkRenderHelper.FillCircle(pos, 3.5f, InkWashTheme.BloodBright);
                        break;
                    case Mk.Player:
                        DrawPlayer(pos);
                        break;
                }
            }

            private static void DrawDiamond(Float2 center, Color color)
            {
                float hs = 7f;
                var v1 = center + new Float2(0f, -hs);
                var v2 = center + new Float2(hs, 0f);
                var v3 = center + new Float2(0f, hs);
                var v4 = center + new Float2(-hs, 0f);
                Render2D.FillTriangle(v1, v2, center, color);
                Render2D.FillTriangle(v2, v3, center, color);
                Render2D.FillTriangle(v3, v4, center, color);
                Render2D.FillTriangle(v4, v1, center, color);
                var inner = center + new Float2(0f, -hs * 0.5f);
                var innerR = center + new Float2(hs * 0.5f, 0f);
                var innerB = center + new Float2(0f, hs * 0.5f);
                var innerL = center + new Float2(-hs * 0.5f, 0f);
                Render2D.FillTriangle(inner, innerR, center, InkWashTheme.Void);
                Render2D.FillTriangle(innerR, innerB, center, InkWashTheme.Void);
                Render2D.FillTriangle(innerB, innerL, center, InkWashTheme.Void);
                Render2D.FillTriangle(innerL, inner, center, InkWashTheme.Void);
            }

            private void DrawPlayer(Float2 pos)
            {
                float pulse = 4f + 6f * (0.5f + 0.5f * Mathf.Sin(_pulse * 3f));
                var glowClr = new Color(126f / 255f, 171f / 255f, 158f / 255f, 0.35f);
                InkRenderHelper.FillCircle(pos, pulse + 4f, glowClr);

                float hs = 8f;
                var pts = new Float2[10];
                for (int i = 0; i < 10; i++)
                {
                    float angle = (i * 2f * (float)Math.PI / 10f) - Mathf.PiOverTwo;
                    float r = (i % 2 == 0) ? hs : hs * 0.45f;
                    pts[i] = pos + new Float2(Mathf.Cos(angle) * r, Mathf.Sin(angle) * r);
                }
                var starClr = InkWashTheme.JadeBright;
                for (int i = 0; i < 10; i++)
                {
                    int next = (i + 1) % 10;
                    Render2D.DrawLine(pts[i], pts[next], starClr, 1.5f);
                }
            }

            private static void DrawPlace(Float2 pos, string text)
            {
                var font = InkWashTheme.GetFont(InkWashTheme.FontRole.Display);
                if (font == null) return;
                var fr = new FontReference(font, 18f);
                var af = fr.GetFont();
                if (af == null) return;
                var c = new Color(240f / 255f, 237f / 255f, 228f / 255f, 0.55f);
                var rect = new Rectangle(pos.X - 40f, pos.Y - 12f, 80f, 24f);
                Render2D.DrawText(af, text, rect, c, TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
            }
        }
    }
}
