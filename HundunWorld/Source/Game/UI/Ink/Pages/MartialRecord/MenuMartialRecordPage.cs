using System;
using System.Collections.Generic;
using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.Ink;
using HundunWorld.Game.UI.StyleSystem;

namespace HundunWorld.Game.UI.Ink.Pages.MartialRecord
{
    /// <summary>
    /// 菜单·武林录 — 唯一事实来源为设计稿 hundun-yy-ui/pages/menu-martial-record.html。
    /// 结构：240px 侧边导航（品牌「燕云十六声」+ 任务/博物志/武林录/营生/组队/邮箱/商店
    /// + 角色/装备 + 设置）+ 60px 顶栏（角色信息 / 铜钱与灵石 / 返回战斗）
    /// + 主内容 3:2（武林录地图区：标题牌、已探索计数、行旅路线、六处地标、图例；
    ///   区域详情：区域名与探索度、区域成就列表、总探索度）+ 32px 底栏（服务器 / 延迟 / 版本）。
    /// 颜色与字体一律取自 <see cref="InkWashTheme"/> 设计 Token，禁止硬编码色值。
    /// </summary>
    public class MenuMartialRecordPage : ContainerControl, IInkPage
    {
        // ═════════════════════════ 数据模型 ═════════════════════════

        /// <summary>地图标记状态 — 对应设计稿 .map-marker--explored / --unexplored / --active。</summary>
        private enum MarkerState
        {
            /// <summary>已探索 — 金菱形界碑。</summary>
            Explored,

            /// <summary>未探索 — 灰色圆形 + 问号。</summary>
            Unexplored,

            /// <summary>当前位置 — 朱红圆形 + 金边。</summary>
            Current
        }

        /// <summary>成就状态 — 对应设计稿「已完成 / 进行中 / 未开始」三态。</summary>
        private enum AchState
        {
            Done,
            Doing,
            Todo
        }

        /// <summary>奖励标签皮肤 — 对应设计稿 .ink-tag / .ink-tag-brand / .tag-jade / .ink-tag-vermilion。</summary>
        private enum ChipSkin
        {
            Default,
            Brand,
            Jade,
            Vermilion
        }

        /// <summary>区域成就条目。</summary>
        private sealed class AchievementData
        {
            public string Name;
            public AchState State;
            public string StateText;
            public int Current;
            public int Total;
            public string[] Rewards;
            public ChipSkin[] Skins;
        }

        /// <summary>地标（区域）数据。</summary>
        private sealed class RegionData
        {
            public string Name;
            public string Description;
            public int Percent;
            public MarkerState State;
            public float MapX;
            public float MapY;
            public AchievementData[] Achievements;
        }

        /// <summary>成就行的子控件引用集。</summary>
        private sealed class AchRowView
        {
            public Panel Root;
            public StatusGlyph Glyph;
            public Label Name;
            public Label State;
            public InkBar Bar;
            public Panel Divider;
            public bool HasBar;
            public readonly List<TagChip> Tags = new List<TagChip>();
        }

        /// <summary>自绘奖励标签（设计稿 ink-tag 四个皮肤，含青玉 tag-jade）。</summary>
        private sealed class TagChip : Label
        {
            public Color OutlineColor = InkWashTheme.BorderNeutralL2;

            public TagChip()
            {
                AutoFocus = false;
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f);
                HorizontalAlignment = TextAlignment.Center;
                VerticalAlignment = TextAlignment.Center;
            }

            public void SetSkin(ChipSkin skin, bool dim)
            {
                float k = dim ? 0.55f : 1f;
                Color text;
                Color border;
                Color bg;

                switch (skin)
                {
                    case ChipSkin.Brand:
                        text = InkWashTheme.TextBrand;
                        border = InkWashTheme.BorderGold;
                        bg = new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.12f);
                        break;
                    case ChipSkin.Jade:
                        text = InkWashTheme.JadeBright;
                        border = InkWashTheme.JadePrimary;
                        bg = new Color(InkWashTheme.JadePrimary.R, InkWashTheme.JadePrimary.G, InkWashTheme.JadePrimary.B, 0.12f);
                        break;
                    case ChipSkin.Vermilion:
                        text = InkWashTheme.TextVermilion;
                        border = InkWashTheme.BorderVermilion;
                        bg = new Color(InkWashTheme.VermilionPrimary.R, InkWashTheme.VermilionPrimary.G, InkWashTheme.VermilionPrimary.B, 0.12f);
                        break;
                    default:
                        text = InkWashTheme.TextSecondary;
                        border = InkWashTheme.BorderNeutralL2;
                        bg = new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.08f);
                        break;
                }

                TextColor = new Color(text.R, text.G, text.B, text.A * k);
                OutlineColor = new Color(border.R, border.G, border.B, border.A * k);
                BackgroundColor = new Color(bg.R, bg.G, bg.B, bg.A * k);
            }

            public override void Draw()
            {
                base.Draw();
                if (OutlineColor.A > 0f && Width > 0f && Height > 0f)
                {
                    Render2D.DrawRectangle(new Rectangle(0f, 0f, Width, Height), OutlineColor, 1f);
                }
            }
        }

        /// <summary>成就状态图标 — 对应设计稿 check-circle-2 / circle-dot / circle-dashed。</summary>
        private sealed class StatusGlyph : Control
        {
            public AchState State;

            public StatusGlyph()
            {
                AutoFocus = false;
                Enabled = false;
            }

            public override void Draw()
            {
                base.Draw();
                if (Width <= 0f || Height <= 0f)
                    return;

                var c = new Float2(Width * 0.5f, Height * 0.5f);
                float r = Mathf.Min(Width, Height) * 0.5f - 1f;
                if (r <= 1f)
                    return;

                if (State == AchState.Done)
                {
                    InkRenderHelper.DrawCircle(c, r, InkWashTheme.JadePrimary, 1.5f);
                    Render2D.DrawLine(c + new Float2(-r * 0.45f, 0f), c + new Float2(-r * 0.1f, r * 0.42f), InkWashTheme.JadeBright, 1.5f);
                    Render2D.DrawLine(c + new Float2(-r * 0.1f, r * 0.42f), c + new Float2(r * 0.5f, -r * 0.4f), InkWashTheme.JadeBright, 1.5f);
                }
                else if (State == AchState.Doing)
                {
                    InkRenderHelper.DrawCircle(c, r, InkWashTheme.GoldPrimary, 1.5f);
                    InkRenderHelper.FillCircle(c, r * 0.38f, InkWashTheme.GoldPrimary);
                }
                else
                {
                    for (int i = 0; i < 8; i++)
                    {
                        float a0 = i * Mathf.TwoPi / 8f;
                        float a1 = a0 + Mathf.TwoPi / 16f;
                        var p0 = c + new Float2(Mathf.Cos(a0) * r, Mathf.Sin(a0) * r);
                        var p1 = c + new Float2(Mathf.Cos(a1) * r, Mathf.Sin(a1) * r);
                        Render2D.DrawLine(p0, p1, InkWashTheme.TextTertiary, 1.5f);
                    }
                }
            }
        }

        /// <summary>图例 / 货币色块 — 菱形（已探索）与圆形（当前位置、未探索）。</summary>
        private sealed class ShapeSwatch : Control
        {
            public bool Diamond;
            public Color Tint = InkWashTheme.GoldPrimary;

            public ShapeSwatch()
            {
                AutoFocus = false;
                Enabled = false;
            }

            public override void Draw()
            {
                base.Draw();
                if (Width <= 0f || Height <= 0f)
                    return;

                var c = new Float2(Width * 0.5f, Height * 0.5f);
                float r = Mathf.Min(Width, Height) * 0.5f;
                float glow = r * 1.6f;

                InkRenderHelper.FillCircle(c, glow, new Color(Tint.R, Tint.G, Tint.B, 0.22f));
                if (Diamond)
                {
                    var v1 = c + new Float2(0f, -r);
                    var v2 = c + new Float2(r, 0f);
                    var v3 = c + new Float2(0f, r);
                    var v4 = c + new Float2(-r, 0f);
                    Render2D.FillTriangle(v1, v2, c, Tint);
                    Render2D.FillTriangle(v2, v3, c, Tint);
                    Render2D.FillTriangle(v3, v4, c, Tint);
                    Render2D.FillTriangle(v4, v1, c, Tint);
                }
                else
                {
                    InkRenderHelper.FillCircle(c, r, Tint);
                }
            }
        }

        /// <summary>顶栏头像 — 圆形金边 + 角色首字。</summary>
        private sealed class AvatarBadge : Control
        {
            public string Glyph = "无";

            public AvatarBadge()
            {
                AutoFocus = false;
                Enabled = false;
            }

            public override void Draw()
            {
                base.Draw();
                if (Width <= 0f || Height <= 0f)
                    return;

                var c = new Float2(Width * 0.5f, Height * 0.5f);
                float r = Mathf.Min(Width, Height) * 0.5f - 1f;
                InkRenderHelper.FillCircle(c, r, InkWashTheme.BaseElevated);
                InkRenderHelper.DrawCircle(c, r, InkWashTheme.GoldDeep, 2f);

                var font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 16f).GetFont();
                if (font != null)
                {
                    Render2D.DrawText(font, Glyph, new Rectangle(0f, 0f, Width, Height), InkWashTheme.PaperAged,
                        TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
                }
            }
        }

        /// <summary>地图标记 — 对应设计稿 .map-marker（圆点 + 地标名标签 + 悬停放大 + 点击选中）。</summary>
        private sealed class MapMarker : Control
        {
            public string Label;
            public MarkerState State;
            public bool Selected;
            public bool Hovered;

            public event Action Clicked;

            public MapMarker()
            {
                AutoFocus = false;
            }

            public override void Draw()
            {
                base.Draw();
                if (Width <= 0f || Height <= 0f)
                    return;

                float cx = Width * 0.5f;
                const float cy = 12f;
                float scale = Hovered ? 1.15f : 1f;
                var center = new Float2(cx, cy);

                if (State == MarkerState.Unexplored)
                {
                    InkRenderHelper.FillCircle(center, 8f * scale, InkWashTheme.BaseElevated);
                    InkRenderHelper.DrawCircle(center, 8f * scale, InkWashTheme.TextTertiary, 2f);
                }
                else if (State == MarkerState.Current)
                {
                    var vermilion = InkWashTheme.VermilionPrimary;
                    InkRenderHelper.FillCircle(center, 12f * scale, new Color(vermilion.R, vermilion.G, vermilion.B, 0.35f));
                    InkRenderHelper.FillCircle(center, 8f * scale, InkWashTheme.VermilionPrimary);
                    InkRenderHelper.DrawCircle(center, 8f * scale, InkWashTheme.GoldPrimary, 2f);
                }
                else
                {
                    var gold = InkWashTheme.GoldPrimary;
                    InkRenderHelper.FillCircle(center, 9f * scale, new Color(gold.R, gold.G, gold.B, 0.24f));
                    DrawDiamond(center, 5f * scale, InkWashTheme.GoldPrimary);
                }

                if (Selected)
                {
                    InkRenderHelper.DrawCircle(center, 15f * scale, InkWashTheme.GoldBright, 1f);
                }

                var font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 12f).GetFont();
                if (font == null)
                    return;

                float labelW = MarkerLabelWidth(Label);
                var labelRect = new Rectangle(cx - labelW * 0.5f, 24f, labelW, 22f);

                Color labelBg = InkWashTheme.Scrim;
                Color labelBorder = Color.Transparent;
                Color labelText;

                switch (State)
                {
                    case MarkerState.Unexplored:
                        labelText = InkWashTheme.TextTertiary;
                        break;
                    case MarkerState.Current:
                        labelText = InkWashTheme.PaperBright;
                        labelBg = new Color(InkWashTheme.VermilionPrimary.R, InkWashTheme.VermilionPrimary.G, InkWashTheme.VermilionPrimary.B, 0.35f);
                        labelBorder = InkWashTheme.VermilionDeep;
                        break;
                    default:
                        labelText = InkWashTheme.TextBrand;
                        break;
                }

                if (Selected)
                {
                    labelText = InkWashTheme.GoldBright;
                    labelBg = new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.18f);
                    labelBorder = InkWashTheme.BorderGold;
                }

                InkRenderHelper.FillRoundedRectangle(labelRect, InkWashTheme.RadiusSm, labelBg);
                if (labelBorder.A > 0f)
                {
                    InkRenderHelper.DrawRoundedRectangle(labelRect, InkWashTheme.RadiusSm, labelBorder, 1f);
                }

                Render2D.DrawText(font, Label, labelRect, labelText,
                    TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);

                if (State == MarkerState.Unexplored)
                {
                    var qf = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 10f).GetFont();
                    if (qf != null)
                    {
                        Render2D.DrawText(qf, "?", new Rectangle(cx - 8f, cy - 7f, 16f, 14f), InkWashTheme.TextTertiary,
                            TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
                    }
                }
            }

            public override bool OnMouseUp(Float2 location, MouseButton button)
            {
                if (button == MouseButton.Left)
                {
                    Clicked?.Invoke();
                }
                return base.OnMouseUp(location, button);
            }

            public override void OnMouseEnter(Float2 location)
            {
                Hovered = true;
                base.OnMouseEnter(location);
            }

            public override void OnMouseLeave()
            {
                Hovered = false;
                base.OnMouseLeave();
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

            private static float MarkerLabelWidth(string text)
            {
                float w = 16f;
                if (string.IsNullOrEmpty(text))
                    return w;
                foreach (char c in text)
                {
                    w += c > 0x2E80 ? 12f : 7f;
                }
                return w;
            }
        }

        /// <summary>地图底 — 水墨底 + 金铜柔光 + 远山雾霭剪影 + 行旅路线虚线。</summary>
        private sealed class MapBackdrop : Control
        {
            private static readonly Float2[] RidgeFar =
            {
                new Float2(0f, 0.75f), new Float2(0.125f, 0.40f), new Float2(0.25f, 0.60f),
                new Float2(0.50f, 0.50f), new Float2(0.75f, 0.65f), new Float2(1f, 0.45f)
            };

            private static readonly Float2[] RidgeNear =
            {
                new Float2(0f, 0.85f), new Float2(0.10f, 0.60f), new Float2(0.225f, 0.75f),
                new Float2(0.475f, 0.70f), new Float2(0.725f, 0.80f), new Float2(1f, 0.65f)
            };

            /// <summary>设计稿三条旅行路线（归一化坐标 天山→逍遥谷→洛阳城→江南）。</summary>
            private static readonly Float2[] RoutePoints =
            {
                new Float2(0.30f, 0.15f), new Float2(0.42f, 0.45f),
                new Float2(0.58f, 0.38f), new Float2(0.70f, 0.65f)
            };

            public MapBackdrop()
            {
                AutoFocus = false;
                Enabled = false;
            }

            public override void Draw()
            {
                base.Draw();
                if (Width <= 0f || Height <= 0f)
                    return;

                Render2D.FillRectangle(new Rectangle(Float2.Zero, Size), InkWashTheme.Void);

                float radius = Mathf.Max(Width, Height) * 0.5f;
                var gold = InkWashTheme.GoldPrimary;
                InkRenderHelper.FillRadialGradient(new Float2(Width * 0.3f, Height * 0.2f), radius,
                    new Color(gold.R, gold.G, gold.B, 0.06f), new Color(gold.R, gold.G, gold.B, 0f), 18);
                var bronze = InkWashTheme.BronzePrimary;
                InkRenderHelper.FillRadialGradient(new Float2(Width * 0.7f, Height * 0.8f), radius * 1.1f,
                    new Color(bronze.R, bronze.G, bronze.B, 0.04f), new Color(bronze.R, bronze.G, bronze.B, 0f), 18);

                FillRidge(RidgeFar, 0.45f,
                    new Color(InkWashTheme.BaseTertiary.R, InkWashTheme.BaseTertiary.G, InkWashTheme.BaseTertiary.B, 0.50f));
                FillRidge(RidgeNear, 0.45f,
                    new Color(InkWashTheme.Void.R, InkWashTheme.Void.G, InkWashTheme.Void.B, 0.70f));

                var routeColor = new Color(gold.R, gold.G, gold.B, 0.30f);
                for (int i = 0; i < RoutePoints.Length - 1; i++)
                {
                    DrawDashedLine(
                        new Float2(RoutePoints[i].X * Width, RoutePoints[i].Y * Height),
                        new Float2(RoutePoints[i + 1].X * Width, RoutePoints[i + 1].Y * Height),
                        routeColor, 1f, 5f, 5f);
                }
            }

            private void FillRidge(Float2[] ridge, float bandRatio, Color color)
            {
                float bandH = Height * bandRatio;
                float bandTop = Height - bandH;
                for (int i = 0; i < ridge.Length - 1; i++)
                {
                    var t0 = new Float2(ridge[i].X * Width, bandTop + ridge[i].Y * bandH);
                    var t1 = new Float2(ridge[i + 1].X * Width, bandTop + ridge[i + 1].Y * bandH);
                    var b0 = new Float2(t0.X, Height);
                    var b1 = new Float2(t1.X, Height);
                    Render2D.FillTriangle(t0, t1, b1, color);
                    Render2D.FillTriangle(t0, b1, b0, color);
                }
            }

            private static void DrawDashedLine(Float2 a, Float2 b, Color color, float thickness, float dash, float gap)
            {
                var d = b - a;
                float len = d.Length;
                if (len <= 0.001f)
                    return;

                var dir = d / len;
                float t = 0f;
                while (t < len)
                {
                    float t2 = Mathf.Min(t + dash, len);
                    Render2D.DrawLine(a + dir * t, a + dir * t2, color, thickness);
                    t = t2 + gap;
                }
            }
        }

        /// <summary>侧边导航项 — 对应设计稿 .nav-item（激活项左侧金线 + 金底）。</summary>
        private sealed class NavItemButton : InkButton
        {
            public bool NavActive;
            public Color CustomBackground = Color.Transparent;
            public Color CustomText = InkWashTheme.TextSecondary;

            /// <summary>应用设计稿配色（InkButton 悬停结束后会回到变体默认色，故需重新施加）。</summary>
            public void ApplyCustomColors()
            {
                BackgroundColor = CustomBackground;
                TextColor = CustomText;
            }

            public override void OnMouseLeave()
            {
                base.OnMouseLeave();
                ApplyCustomColors();
            }

            public override void Draw()
            {
                base.Draw();
                if (NavActive && Width > 0f && Height > 0f)
                {
                    Render2D.FillRectangle(new Rectangle(0f, (Height - 24f) * 0.5f, 2f, 24f), InkWashTheme.GoldPrimary);
                }
            }
        }

        // ═════════════════════════ 常量与字段 ═════════════════════════

        private const float SidebarW = 240f;
        private const float TopBarH = 60f;
        private const float FooterH = 32f;
        private const float MapRatio = 0.6f;
        private const float DetailPad = 24f;
        private const float MarkerW = 96f;
        private const float MarkerH = 48f;
        private const int DefaultRegionIndex = 2;

        private Float2 _screenSize;

        // 背景层
        private InkBackgroundLayer _bgLayer;
        private InkVignette _vignette;

        // 侧边导航
        private Panel _sidebar;
        private Label _sidebarBrand;
        private InkDivider _sidebarTopDivider;
        private readonly List<NavItemButton> _navMainButtons = new List<NavItemButton>();
        private InkDivider _navDivider;
        private readonly List<NavItemButton> _navSubButtons = new List<NavItemButton>();
        private InkDivider _settingsDivider;
        private NavItemButton _settingsButton;

        // 顶栏
        private Panel _topBar;
        private AvatarBadge _avatar;
        private Label _playerName;
        private InkTag _levelTag;
        private Label _sectLabel;
        private ShapeSwatch _coinSwatch;
        private Label _coinValue;
        private ShapeSwatch _gemSwatch;
        private Label _gemValue;
        private InkButton _backButton;

        // 地图区
        private Panel _mapPanel;
        private MapBackdrop _mapBackdrop;
        private InkPanel _mapTitleChip;
        private Label _mapTitleIcon;
        private Label _mapTitleLabel;
        private Panel _exploreChip;
        private Label _exploreChipLabel;
        private Label _exploreChipValue;
        private Label _exploreChipTotal;
        private Panel _legendPanel;
        private ShapeSwatch _legendSwatchExplored;
        private Label _legendLabelExplored;
        private ShapeSwatch _legendSwatchCurrent;
        private Label _legendLabelCurrent;
        private ShapeSwatch _legendSwatchUnexplored;
        private Label _legendLabelUnexplored;
        private readonly List<MapMarker> _markers = new List<MapMarker>();

        // 区域详情
        private Panel _detailPanel;
        private Panel _detailEdge;
        private Label _regionName;
        private InkTag _regionProgressTag;
        private Label _regionDesc;
        private InkBar _regionBar;
        private Label _regionExploredText;
        private Label _regionRemainText;
        private InkDivider _detailDividerTop;
        private Label _achTitle;
        private Label _achSummary;
        private readonly List<AchRowView> _achRows = new List<AchRowView>();
        private InkDivider _detailDividerBottom;
        private Label _totalTitle;
        private Label _totalValue;
        private Label _totalUnit;
        private InkBar _totalBar;

        // 底栏
        private Panel _footer;
        private Panel _footerEdge;
        private Label _serverLabel;
        private Label _pingLabel;
        private Label _versionLabel;

        private int _selectedRegion = DefaultRegionIndex;
        private int _exploredRegions = 23;
        private int _totalRegions = 50;

        /// <summary>标准导航事件（对齐 Ink 页面契约，由工厂挂钩后经路由器跳转）。</summary>
        public event Action<string> NavigationRequested;

        // ═════════════════════════ 设计稿数据 ═════════════════════════

        /// <summary>
        /// 六处地标，文案与设计稿一致（默认选中「逍遥谷」，即设计稿选中态）。
        /// 坐标为设计稿 .map-marker 的百分比定位。
        /// </summary>
        private static readonly RegionData[] Regions =
        {
            new RegionData
            {
                Name = "天山", Description = "雪岭孤峰，终年积雪，天山剑派立派于此",
                Percent = 92, State = MarkerState.Explored, MapX = 0.30f, MapY = 0.15f,
                Achievements = new[]
                {
                    new AchievementData { Name = "初登天山", State = AchState.Done, StateText = "已完成",
                        Rewards = new[] { "经验+400", "银两+150" }, Skins = new[] { ChipSkin.Brand, ChipSkin.Default } },
                    new AchievementData { Name = "雪岭寻踪", State = AchState.Done, StateText = "已完成",
                        Rewards = new[] { "经验+700", "天山雪莲x2" }, Skins = new[] { ChipSkin.Brand, ChipSkin.Jade } },
                    new AchievementData { Name = "剑冢遗刻", State = AchState.Doing, StateText = "1/2", Current = 1, Total = 2,
                        Rewards = new[] { "经验+900", "玄铁x3" }, Skins = new[] { ChipSkin.Brand, ChipSkin.Jade } }
                }
            },
            new RegionData
            {
                Name = "漠北", Description = "黄沙万里，胡笳声咽，草原与戈壁交汇之地",
                Percent = 0, State = MarkerState.Unexplored, MapX = 0.12f, MapY = 0.25f,
                Achievements = new[]
                {
                    new AchievementData { Name = "踏足漠北", State = AchState.Todo, StateText = "0/1", Current = 0, Total = 1,
                        Rewards = new[] { "经验+600", "银两+200" }, Skins = new[] { ChipSkin.Brand, ChipSkin.Default } },
                    new AchievementData { Name = "胡笳声里", State = AchState.Todo, StateText = "0/3", Current = 0, Total = 3,
                        Rewards = new[] { "经验+900", "羊皮地图x1" }, Skins = new[] { ChipSkin.Brand, ChipSkin.Jade } },
                    new AchievementData { Name = "大漠孤烟", State = AchState.Todo, StateText = "0/5", Current = 0, Total = 5,
                        Rewards = new[] { "经验+1800", "狼牙x1" }, Skins = new[] { ChipSkin.Brand, ChipSkin.Vermilion } }
                }
            },
            new RegionData
            {
                Name = "逍遥谷", Description = "逍遥派祖庭，山清水秀，灵气缭绕之地",
                Percent = 85, State = MarkerState.Current, MapX = 0.42f, MapY = 0.45f,
                Achievements = new[]
                {
                    new AchievementData { Name = "初探逍遥谷", State = AchState.Done, StateText = "已完成",
                        Rewards = new[] { "经验+500", "银两+200" }, Skins = new[] { ChipSkin.Brand, ChipSkin.Default } },
                    new AchievementData { Name = "逍遥秘境探索者", State = AchState.Done, StateText = "已完成",
                        Rewards = new[] { "经验+1000", "秘境钥匙x1" }, Skins = new[] { ChipSkin.Brand, ChipSkin.Jade } },
                    new AchievementData { Name = "逍遥谷百晓生", State = AchState.Done, StateText = "已完成",
                        Rewards = new[] { "经验+800", "武学残页x3" }, Skins = new[] { ChipSkin.Brand, ChipSkin.Jade } },
                    new AchievementData { Name = "隐秘洞穴发现", State = AchState.Doing, StateText = "2/3", Current = 2, Total = 3,
                        Rewards = new[] { "经验+600", "灵石x5" }, Skins = new[] { ChipSkin.Brand, ChipSkin.Jade } },
                    new AchievementData { Name = "逍遥谷全收集", State = AchState.Todo, StateText = "0/15", Current = 0, Total = 15,
                        Rewards = new[] { "经验+3000", "逍遥令x1" }, Skins = new[] { ChipSkin.Brand, ChipSkin.Vermilion } }
                }
            },
            new RegionData
            {
                Name = "洛阳城", Description = "九朝古都，市井繁华，江湖中人汇聚之地",
                Percent = 78, State = MarkerState.Explored, MapX = 0.58f, MapY = 0.38f,
                Achievements = new[]
                {
                    new AchievementData { Name = "初入洛阳", State = AchState.Done, StateText = "已完成",
                        Rewards = new[] { "经验+450", "银两+180" }, Skins = new[] { ChipSkin.Brand, ChipSkin.Default } },
                    new AchievementData { Name = "百家长安", State = AchState.Doing, StateText = "2/4", Current = 2, Total = 4,
                        Rewards = new[] { "经验+750", "武学残卷x2" }, Skins = new[] { ChipSkin.Brand, ChipSkin.Jade } },
                    new AchievementData { Name = "洛阳纸贵", State = AchState.Todo, StateText = "0/6", Current = 0, Total = 6,
                        Rewards = new[] { "经验+1200", "文房四宝x1" }, Skins = new[] { ChipSkin.Brand, ChipSkin.Jade } }
                }
            },
            new RegionData
            {
                Name = "江南", Description = "烟雨朦胧，小桥流水，鱼米之乡",
                Percent = 66, State = MarkerState.Explored, MapX = 0.70f, MapY = 0.65f,
                Achievements = new[]
                {
                    new AchievementData { Name = "泛舟江南", State = AchState.Done, StateText = "已完成",
                        Rewards = new[] { "经验+420", "银两+160" }, Skins = new[] { ChipSkin.Brand, ChipSkin.Default } },
                    new AchievementData { Name = "烟雨寻芳", State = AchState.Doing, StateText = "1/3", Current = 1, Total = 3,
                        Rewards = new[] { "经验+680", "苏绣x2" }, Skins = new[] { ChipSkin.Brand, ChipSkin.Jade } },
                    new AchievementData { Name = "曲水流觞", State = AchState.Todo, StateText = "0/8", Current = 0, Total = 8,
                        Rewards = new[] { "经验+1500", "江南船票x1" }, Skins = new[] { ChipSkin.Brand, ChipSkin.Jade } }
                }
            },
            new RegionData
            {
                Name = "南疆", Description = "瘴气弥漫，苗寨蛊术，人迹罕至之地",
                Percent = 0, State = MarkerState.Unexplored, MapX = 0.25f, MapY = 0.75f,
                Achievements = new[]
                {
                    new AchievementData { Name = "深入南疆", State = AchState.Todo, StateText = "0/1", Current = 0, Total = 1,
                        Rewards = new[] { "经验+650", "银两+220" }, Skins = new[] { ChipSkin.Brand, ChipSkin.Default } },
                    new AchievementData { Name = "瘴林采药", State = AchState.Todo, StateText = "0/4", Current = 0, Total = 4,
                        Rewards = new[] { "经验+950", "苗疆草药x2" }, Skins = new[] { ChipSkin.Brand, ChipSkin.Jade } },
                    new AchievementData { Name = "苗疆秘辛", State = AchState.Todo, StateText = "0/6", Current = 0, Total = 6,
                        Rewards = new[] { "经验+2000", "南疆令x1" }, Skins = new[] { ChipSkin.Brand, ChipSkin.Vermilion } }
                }
            }
        };

        // ═════════════════════════ 构造 ═════════════════════════

        public MenuMartialRecordPage()
        {
            _screenSize = FlaxEngine.Screen.Size;
            if (_screenSize.X <= 0f || _screenSize.Y <= 0f)
            {
                _screenSize = new Float2(1920f, 1080f);
            }

            AnchorPreset = AnchorPresets.StretchAll;
            BackgroundColor = Color.Transparent;
            ClipChildren = false;
            AutoFocus = false;
            Location = Float2.Zero;
            Size = _screenSize;

            try
            {
                BuildBackground();
                BuildSidebar();
                BuildTopBar();
                BuildMapPanel();
                BuildDetailPanel();
                BuildFooter();

                SelectRegion(DefaultRegionIndex);
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[MenuMartialRecordPage] 初始化失败: {ex.Message}");
            }
        }

        // ═════════════════════════ 构建：背景 ═════════════════════════

        private void BuildBackground()
        {
            _bgLayer = new InkBackgroundLayer
            {
                Parent = this
            };

            _vignette = new InkVignette
            {
                Parent = this
            };
        }

        // ═════════════════════════ 构建：侧边导航 ═════════════════════════

        private void BuildSidebar()
        {
            _sidebar = new Panel
            {
                BackgroundColor = InkWashTheme.BaseSecondary,
                Parent = this
            };

            _sidebarBrand = new Label
            {
                Text = "燕云十六声",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 20f),
                TextColor = InkWashTheme.TextBrand,
                VerticalAlignment = TextAlignment.Center,
                Parent = _sidebar
            };

            _sidebarTopDivider = new InkDivider
            {
                Parent = _sidebar
            };

            string[] mainItems = { "任务", "博物志", "武林录", "营生", "组队", "邮箱", "商店" };
            string[] mainTargets =
            {
                InkPageDomIds.NavQuests, InkPageDomIds.NavBestiary, InkPageDomIds.NavMartialRecord,
                InkPageDomIds.NavLivelihood, InkPageDomIds.NavTeam, InkPageDomIds.NavMail, InkPageDomIds.NavShop
            };

            for (int i = 0; i < mainItems.Length; i++)
            {
                bool active = mainTargets[i] == InkPageDomIds.NavMartialRecord;
                string target = mainTargets[i];
                var btn = new NavItemButton
                {
                    Text = mainItems[i],
                    NavActive = active,
                    ButtonSize = InkButtonSize.Sm,
                    Variant = InkButtonVariant.Ghost,
                    HorizontalAlignment = TextAlignment.Near,
                    CustomBackground = active
                        ? new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.10f)
                        : Color.Transparent,
                    CustomText = active ? InkWashTheme.TextBrand : InkWashTheme.TextSecondary,
                    Parent = _sidebar
                };
                btn.ApplyCustomColors();

                // 武林录为当前页，不再重复跳转；其余主菜单项按设计稿链路直达
                if (!active)
                {
                    btn.Clicked += () => NavigationRequested?.Invoke(target);
                }

                _navMainButtons.Add(btn);
            }

            _navDivider = new InkDivider
            {
                Parent = _sidebar
            };

            string[] subItems = { "角色", "装备" };
            // 角色指向已注册的角色属性页（nav-character 无工厂注册，避免死链）
            string[] subTargets = { InkPageDomIds.NavCharacterV2, InkPageDomIds.NavEquipment };

            for (int i = 0; i < subItems.Length; i++)
            {
                string target = subTargets[i];
                var btn = new NavItemButton
                {
                    Text = subItems[i],
                    ButtonSize = InkButtonSize.Sm,
                    Variant = InkButtonVariant.Ghost,
                    HorizontalAlignment = TextAlignment.Near,
                    CustomText = InkWashTheme.TextTertiary,
                    Parent = _sidebar
                };
                btn.ApplyCustomColors();
                btn.Clicked += () => NavigationRequested?.Invoke(target);
                _navSubButtons.Add(btn);
            }

            _settingsDivider = new InkDivider
            {
                Parent = _sidebar
            };

            _settingsButton = new NavItemButton
            {
                Text = "设置",
                ButtonSize = InkButtonSize.Sm,
                Variant = InkButtonVariant.Ghost,
                HorizontalAlignment = TextAlignment.Near,
                CustomText = InkWashTheme.TextTertiary,
                Parent = _sidebar
            };
            _settingsButton.ApplyCustomColors();
            _settingsButton.Clicked += () => NavigationRequested?.Invoke(InkPageDomIds.NavSettings);
        }

        // ═════════════════════════ 构建：顶栏 ═════════════════════════

        private void BuildTopBar()
        {
            _topBar = new Panel
            {
                BackgroundColor = InkWashTheme.BaseTertiary,
                Parent = this
            };

            _avatar = new AvatarBadge
            {
                Glyph = "无",
                Parent = _topBar
            };

            _playerName = new Label
            {
                Text = "无名侠",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 15f),
                TextColor = InkWashTheme.TextDefault,
                VerticalAlignment = TextAlignment.Center,
                Parent = _topBar
            };

            _levelTag = new InkTag
            {
                Text = "Lv.42",
                TagVariant = InkTagVariant.Vermilion,
                Parent = _topBar
            };

            _sectLabel = new Label
            {
                Text = "逍遥派",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                TextColor = InkWashTheme.Info,
                VerticalAlignment = TextAlignment.Center,
                Parent = _topBar
            };

            _coinSwatch = new ShapeSwatch
            {
                Diamond = false,
                Tint = InkWashTheme.PaperAged,
                Parent = _topBar
            };

            _coinValue = new Label
            {
                Text = "12,450",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 14f),
                TextColor = InkWashTheme.TextDefault,
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
                Parent = _topBar
            };

            _gemSwatch = new ShapeSwatch
            {
                Diamond = true,
                Tint = InkWashTheme.GoldPrimary,
                Parent = _topBar
            };

            _gemValue = new Label
            {
                Text = "328",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 14f),
                TextColor = InkWashTheme.TextBrand,
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
                Parent = _topBar
            };

            // 设计稿隐藏出口：返回战斗 HUD
            _backButton = new InkButton
            {
                Text = "返回战斗",
                ButtonSize = InkButtonSize.Sm,
                Variant = InkButtonVariant.Ghost,
                Parent = _topBar
            };
            _backButton.Clicked += () => NavigationRequested?.Invoke(InkPageDomIds.BackHud);
        }

        // ═════════════════════════ 构建：地图区 ═════════════════════════

        private void BuildMapPanel()
        {
            _mapPanel = new Panel
            {
                BackgroundColor = InkWashTheme.Void,
                ClipChildren = true,
                Parent = this
            };

            _mapBackdrop = new MapBackdrop
            {
                Parent = _mapPanel
            };

            // 地图标题牌：设计稿 ink-panel 金线描边 + scrim 底
            _mapTitleChip = new InkPanel
            {
                BackgroundColor = InkWashTheme.Scrim,
                Radius = InkWashTheme.RadiusMd,
                Parent = _mapPanel
            };

            _mapTitleIcon = new Label
            {
                Text = "▤",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 14f),
                TextColor = InkWashTheme.TextBrand,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                Parent = _mapTitleChip
            };

            _mapTitleLabel = new Label
            {
                Text = "武林录",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 15f),
                TextColor = InkWashTheme.TextDefault,
                VerticalAlignment = TextAlignment.Center,
                Parent = _mapTitleChip
            };

            _exploreChip = new Panel
            {
                BackgroundColor = InkWashTheme.Scrim,
                Parent = _mapPanel
            };

            _exploreChipLabel = new Label
            {
                Text = "已探索",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                TextColor = InkWashTheme.PaperAged,
                VerticalAlignment = TextAlignment.Center,
                Parent = _exploreChip
            };

            _exploreChipValue = new Label
            {
                Text = "23",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 16f),
                TextColor = InkWashTheme.TextBrand,
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
                Parent = _exploreChip
            };

            _exploreChipTotal = new Label
            {
                Text = "/50",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 12f),
                TextColor = InkWashTheme.TextTertiary,
                VerticalAlignment = TextAlignment.Center,
                Parent = _exploreChip
            };

            // 图例（左下）：已探索 / 当前位置 / 未探索
            _legendPanel = new Panel
            {
                BackgroundColor = InkWashTheme.Scrim,
                Parent = _mapPanel
            };

            _legendSwatchExplored = new ShapeSwatch
            {
                Diamond = true,
                Tint = InkWashTheme.GoldPrimary,
                Parent = _legendPanel
            };
            _legendLabelExplored = MakeLegendLabel("已探索");

            _legendSwatchCurrent = new ShapeSwatch
            {
                Diamond = false,
                Tint = InkWashTheme.VermilionPrimary,
                Parent = _legendPanel
            };
            _legendLabelCurrent = MakeLegendLabel("当前位置");

            _legendSwatchUnexplored = new ShapeSwatch
            {
                Diamond = false,
                Tint = InkWashTheme.TextTertiary,
                Parent = _legendPanel
            };
            _legendLabelUnexplored = MakeLegendLabel("未探索");

            for (int i = 0; i < Regions.Length; i++)
            {
                int index = i;
                var marker = new MapMarker
                {
                    Label = Regions[i].Name,
                    State = Regions[i].State,
                    Width = MarkerW,
                    Height = MarkerH,
                    Parent = _mapPanel
                };
                marker.Clicked += () => SelectRegion(index);
                _markers.Add(marker);
            }
        }

        private Label MakeLegendLabel(string text)
        {
            return new Label
            {
                Text = text,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.PaperAged,
                VerticalAlignment = TextAlignment.Center,
                Parent = _legendPanel
            };
        }

        // ═════════════════════════ 构建：区域详情 ═════════════════════════

        private void BuildDetailPanel()
        {
            _detailPanel = new Panel
            {
                BackgroundColor = InkWashTheme.BaseSecondary,
                Parent = this
            };

            _detailEdge = new Panel
            {
                BackgroundColor = InkWashTheme.BorderNeutralL1,
                Parent = _detailPanel
            };

            _regionName = new Label
            {
                Text = Regions[DefaultRegionIndex].Name,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 24f),
                TextColor = InkWashTheme.TextDefault,
                VerticalAlignment = TextAlignment.Center,
                Parent = _detailPanel
            };

            _regionProgressTag = new InkTag
            {
                Text = "探索度 85%",
                TagVariant = InkTagVariant.Vermilion,
                Parent = _detailPanel
            };

            _regionDesc = new Label
            {
                Text = Regions[DefaultRegionIndex].Description,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                TextColor = InkWashTheme.TextSecondary,
                VerticalAlignment = TextAlignment.Center,
                Parent = _detailPanel
            };

            _regionBar = new InkBar
            {
                Height = 8f,
                FillVariant = InkBarFillVariant.Gold,
                Parent = _detailPanel
            };

            _regionExploredText = new Label
            {
                Text = "已探索 85%",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.TextTertiary,
                VerticalAlignment = TextAlignment.Center,
                Parent = _detailPanel
            };

            _regionRemainText = new Label
            {
                Text = "剩余 15%",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.TextTertiary,
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
                Parent = _detailPanel
            };

            _detailDividerTop = new InkDivider
            {
                Parent = _detailPanel
            };

            _achTitle = new Label
            {
                Text = "区域成就",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 15f),
                TextColor = InkWashTheme.TextDefault,
                VerticalAlignment = TextAlignment.Center,
                Parent = _detailPanel
            };

            _achSummary = new Label
            {
                Text = "3/5 已完成",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 11f),
                TextColor = InkWashTheme.TextTertiary,
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
                Parent = _detailPanel
            };

            _detailDividerBottom = new InkDivider
            {
                Parent = _detailPanel
            };

            _totalTitle = new Label
            {
                Text = "总探索度",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 13f),
                TextColor = InkWashTheme.PaperAged,
                VerticalAlignment = TextAlignment.Center,
                Parent = _detailPanel
            };

            _totalValue = new Label
            {
                Text = "23",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 20f),
                TextColor = InkWashTheme.TextBrand,
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
                Parent = _detailPanel
            };

            _totalUnit = new Label
            {
                Text = "/ 50 区域",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                TextColor = InkWashTheme.TextTertiary,
                VerticalAlignment = TextAlignment.Center,
                Parent = _detailPanel
            };

            _totalBar = new InkBar
            {
                Height = 8f,
                FillVariant = InkBarFillVariant.Gold,
                Parent = _detailPanel
            };
        }

        // ═════════════════════════ 构建：底栏 ═════════════════════════

        private void BuildFooter()
        {
            _footer = new Panel
            {
                BackgroundColor = InkWashTheme.Void,
                Parent = this
            };

            _footerEdge = new Panel
            {
                BackgroundColor = InkWashTheme.BorderNeutralL1,
                Parent = _footer
            };

            _serverLabel = new Label
            {
                Text = "服务器：乾坤一柱",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.TextTertiary,
                VerticalAlignment = TextAlignment.Center,
                Parent = _footer
            };

            _pingLabel = new Label
            {
                Text = "延迟 32ms",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.TextTertiary,
                VerticalAlignment = TextAlignment.Center,
                Parent = _footer
            };

            _versionLabel = new Label
            {
                Text = "燕云十六声 v1.0.0",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.TextTertiary,
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
                Parent = _footer
            };
        }

        // ═════════════════════════ 选中与刷新 ═════════════════════════

        /// <summary>
        /// 选中一处地标：更新标记选中态、区域详情与成就列表（交互出口）。
        /// </summary>
        private void SelectRegion(int index)
        {
            if (index < 0 || index >= Regions.Length)
                return;

            _selectedRegion = index;
            for (int i = 0; i < _markers.Count; i++)
            {
                _markers[i].Selected = (i == index);
            }

            RefreshAllData();
        }

        /// <summary>按当前选中区域刷新详情面板与总探索度。</summary>
        private void RefreshAllData()
        {
            RegionData region = Regions[_selectedRegion];

            if (_regionName != null) _regionName.Text = region.Name;
            if (_regionProgressTag != null) _regionProgressTag.Text = $"探索度 {region.Percent}%";
            if (_regionDesc != null) _regionDesc.Text = region.Description;
            if (_regionBar != null) _regionBar.Value = region.Percent / 100f;
            if (_regionExploredText != null) _regionExploredText.Text = $"已探索 {region.Percent}%";
            if (_regionRemainText != null) _regionRemainText.Text = $"剩余 {100 - region.Percent}%";

            int done = 0;
            for (int i = 0; i < region.Achievements.Length; i++)
            {
                if (region.Achievements[i].State == AchState.Done)
                    done++;
            }
            if (_achSummary != null) _achSummary.Text = AchievementSummaryText(done, region.Achievements.Length);

            RefreshAchievementRows(region.Achievements);

            if (_exploreChipValue != null) _exploreChipValue.Text = $"{_exploredRegions}";
            if (_exploreChipTotal != null) _exploreChipTotal.Text = $"/{_totalRegions}";
            if (_totalValue != null) _totalValue.Text = $"{_exploredRegions}";
            if (_totalUnit != null) _totalUnit.Text = $"/ {_totalRegions} 区域";
            if (_totalBar != null) _totalBar.Value = _totalRegions > 0 ? (float)_exploredRegions / _totalRegions : 0f;

            ApplyLayout();
        }

        /// <summary>按数量补齐成就行（区域切换时复用已有行，多退少隐）。</summary>
        private void EnsureAchRows(int count)
        {
            while (_achRows.Count < count)
            {
                var root = new Panel
                {
                    BackgroundColor = Color.Transparent,
                    Parent = _detailPanel
                };

                var view = new AchRowView { Root = root };

                view.Glyph = new StatusGlyph
                {
                    Parent = root
                };

                view.Name = new Label
                {
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 14f),
                    TextColor = InkWashTheme.TextDefault,
                    VerticalAlignment = TextAlignment.Center,
                    Parent = root
                };

                view.State = new Label
                {
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 12f),
                    TextColor = InkWashTheme.GoldBright,
                    HorizontalAlignment = TextAlignment.Far,
                    VerticalAlignment = TextAlignment.Center,
                    Parent = root
                };

                view.Divider = new Panel
                {
                    BackgroundColor = InkWashTheme.Divider,
                    Parent = root
                };

                _achRows.Add(view);
            }
        }

        /// <summary>用区域成就数据填充成就行（状态图标 / 名称 / 进度 / 奖励标签）。</summary>
        private void RefreshAchievementRows(AchievementData[] achievements)
        {
            EnsureAchRows(achievements.Length);

            for (int i = 0; i < _achRows.Count; i++)
            {
                AchRowView view = _achRows[i];
                bool used = i < achievements.Length;
                view.Root.Visible = used;
                if (!used)
                    continue;

                AchievementData data = achievements[i];
                bool dim = data.State == AchState.Todo;
                bool hasBar = data.State != AchState.Done;

                view.Glyph.State = data.State;

                view.Name.Text = data.Name;
                view.Name.TextColor = dim ? InkWashTheme.TextSecondary : InkWashTheme.TextDefault;

                view.State.Text = data.StateText;
                view.State.TextColor = data.State == AchState.Done
                    ? InkWashTheme.JadeBright
                    : (data.State == AchState.Doing ? InkWashTheme.TextBrand : InkWashTheme.TextTertiary);

                if (hasBar && view.Bar == null)
                {
                    view.Bar = new InkBar
                    {
                        Height = 4f,
                        FillVariant = InkBarFillVariant.Gold,
                        Parent = view.Root
                    };
                }

                view.HasBar = hasBar;
                if (view.Bar != null)
                {
                    view.Bar.Visible = hasBar;
                    if (hasBar)
                    {
                        view.Bar.Value = data.Total > 0 ? Mathf.Clamp((float)data.Current / data.Total, 0f, 1f) : 0f;
                    }
                }

                foreach (TagChip chip in view.Tags)
                {
                    chip.Dispose();
                }
                view.Tags.Clear();

                for (int r = 0; r < data.Rewards.Length; r++)
                {
                    ChipSkin skin = (data.Skins != null && r < data.Skins.Length) ? data.Skins[r] : ChipSkin.Default;
                    var chip = new TagChip
                    {
                        Text = data.Rewards[r],
                        Width = ChipWidth(data.Rewards[r]),
                        Height = 20f,
                        Parent = view.Root
                    };
                    chip.SetSkin(skin, dim);
                    view.Tags.Add(chip);
                }
            }
        }

        /// <summary>奖励标签宽度估算（中文 11px / 西文 6.5px + 左右内边距）。</summary>
        private static float ChipWidth(string text)
        {
            float w = 16f;
            if (string.IsNullOrEmpty(text))
                return w;
            foreach (char c in text)
            {
                w += c > 0x2E80 ? 11f : 6.5f;
            }
            return w;
        }

        /// <summary>
        /// 区域成就汇总文案。设计稿定稿为「3/5 已完成」，
        /// 其余组合按同一句式展开，避免运行时拼接导致设计稿文案不可追溯。
        /// </summary>
        private static string AchievementSummaryText(int done, int total)
        {
            switch (total)
            {
                case 3:
                    if (done >= 3) return "3/3 已完成";
                    if (done == 2) return "2/3 已完成";
                    if (done == 1) return "1/3 已完成";
                    return "0/3 已完成";
                case 4:
                    if (done >= 4) return "4/4 已完成";
                    if (done == 3) return "3/4 已完成";
                    if (done == 2) return "2/4 已完成";
                    if (done == 1) return "1/4 已完成";
                    return "0/4 已完成";
                case 5:
                    if (done >= 5) return "5/5 已完成";
                    if (done == 4) return "4/5 已完成";
                    if (done == 3) return "3/5 已完成";
                    if (done == 2) return "2/5 已完成";
                    if (done == 1) return "1/5 已完成";
                    return "0/5 已完成";
                default:
                    return done + "/" + total + " 已完成";
            }
        }

        // ═════════════════════════ 布局 ═════════════════════════

        private void ApplyLayout()
        {
            float sw = Width > 0f ? Width : _screenSize.X;
            float sh = Height > 0f ? Height : _screenSize.Y;
            if (sw <= 0f || sh <= 0f)
                return;

            _bgLayer.Location = Float2.Zero;
            _bgLayer.Size = new Float2(sw, sh);
            _vignette.Location = Float2.Zero;
            _vignette.Size = new Float2(sw, sh);

            // ── 侧边导航 ──
            _sidebar.Location = Float2.Zero;
            _sidebar.Size = new Float2(SidebarW, sh);

            _sidebarBrand.Location = new Float2(20f, 0f);
            _sidebarBrand.Size = new Float2(SidebarW - 40f, 64f);

            _sidebarTopDivider.Location = new Float2(16f, 64f);
            _sidebarTopDivider.Size = new Float2(SidebarW - 32f, 1f);

            float ny = 80f;
            for (int i = 0; i < _navMainButtons.Count; i++)
            {
                _navMainButtons[i].Location = new Float2(8f, ny);
                _navMainButtons[i].Size = new Float2(SidebarW - 16f, 38f);
                ny += 42f;
            }

            ny += 6f;
            _navDivider.Location = new Float2(16f, ny);
            _navDivider.Size = new Float2(SidebarW - 32f, 1f);
            ny += 14f;

            for (int i = 0; i < _navSubButtons.Count; i++)
            {
                _navSubButtons[i].Location = new Float2(8f, ny);
                _navSubButtons[i].Size = new Float2(SidebarW - 16f, 38f);
                ny += 42f;
            }

            ny += 6f;
            _settingsDivider.Location = new Float2(16f, ny);
            _settingsDivider.Size = new Float2(SidebarW - 32f, 1f);
            ny += 14f;

            _settingsButton.Location = new Float2(8f, ny);
            _settingsButton.Size = new Float2(SidebarW - 16f, 38f);

            // ── 顶栏 ──
            float topW = sw - SidebarW;
            _topBar.Location = new Float2(SidebarW, 0f);
            _topBar.Size = new Float2(topW, TopBarH);

            _avatar.Location = new Float2(16f, 12f);
            _avatar.Size = new Float2(36f, 36f);

            _playerName.Location = new Float2(64f, 0f);
            _playerName.Size = new Float2(92f, TopBarH);

            _levelTag.Location = new Float2(160f, (TopBarH - 20f) * 0.5f);
            _levelTag.Size = new Float2(58f, 20f);

            _sectLabel.Location = new Float2(228f, 0f);
            _sectLabel.Size = new Float2(88f, TopBarH);

            float rx = topW - 16f;
            _backButton.Location = new Float2(rx - 104f, (TopBarH - 28f) * 0.5f);
            _backButton.Size = new Float2(104f, 28f);
            rx -= 104f + 24f;

            _gemValue.Location = new Float2(rx - 64f, 0f);
            _gemValue.Size = new Float2(64f, TopBarH);
            _gemSwatch.Location = new Float2(rx - 84f, (TopBarH - 12f) * 0.5f);
            _gemSwatch.Size = new Float2(12f, 12f);
            rx -= 84f + 24f;

            _coinValue.Location = new Float2(rx - 72f, 0f);
            _coinValue.Size = new Float2(72f, TopBarH);
            _coinSwatch.Location = new Float2(rx - 92f, (TopBarH - 12f) * 0.5f);
            _coinSwatch.Size = new Float2(12f, 12f);

            // ── 底栏 ──
            _footer.Location = new Float2(SidebarW, sh - FooterH);
            _footer.Size = new Float2(topW, FooterH);

            _footerEdge.Location = Float2.Zero;
            _footerEdge.Size = new Float2(topW, 1f);

            _serverLabel.Location = new Float2(16f, 0f);
            _serverLabel.Size = new Float2(180f, FooterH);

            _pingLabel.Location = new Float2(208f, 0f);
            _pingLabel.Size = new Float2(120f, FooterH);

            _versionLabel.Location = new Float2(Mathf.Max(0f, topW - 176f), 0f);
            _versionLabel.Size = new Float2(160f, FooterH);

            // ── 主内容 3:2 ──
            float contentW = sw - SidebarW;
            float contentH = Mathf.Max(200f, sh - TopBarH - FooterH);
            float mapW = contentW * MapRatio;
            float detailW = contentW - mapW;

            _mapPanel.Location = new Float2(SidebarW, TopBarH);
            _mapPanel.Size = new Float2(mapW, contentH);

            _mapBackdrop.Location = Float2.Zero;
            _mapBackdrop.Size = new Float2(mapW, contentH);

            _mapTitleChip.Location = new Float2(16f, 16f);
            _mapTitleChip.Size = new Float2(112f, 36f);
            _mapTitleIcon.Location = new Float2(10f, 0f);
            _mapTitleIcon.Size = new Float2(20f, 36f);
            _mapTitleLabel.Location = new Float2(36f, 0f);
            _mapTitleLabel.Size = new Float2(66f, 36f);

            _exploreChip.Location = new Float2(Mathf.Max(0f, mapW - 16f - 152f), 16f);
            _exploreChip.Size = new Float2(152f, 36f);
            _exploreChipLabel.Location = new Float2(12f, 0f);
            _exploreChipLabel.Size = new Float2(56f, 36f);
            _exploreChipValue.Location = new Float2(70f, 0f);
            _exploreChipValue.Size = new Float2(40f, 36f);
            _exploreChipTotal.Location = new Float2(112f, 0f);
            _exploreChipTotal.Size = new Float2(32f, 36f);

            _legendPanel.Location = new Float2(16f, Mathf.Max(0f, contentH - 16f - 38f));
            _legendPanel.Size = new Float2(272f, 38f);

            _legendSwatchExplored.Location = new Float2(12f, 14f);
            _legendSwatchExplored.Size = new Float2(10f, 10f);
            _legendLabelExplored.Location = new Float2(26f, 0f);
            _legendLabelExplored.Size = new Float2(56f, 38f);

            _legendSwatchCurrent.Location = new Float2(94f, 14f);
            _legendSwatchCurrent.Size = new Float2(10f, 10f);
            _legendLabelCurrent.Location = new Float2(108f, 0f);
            _legendLabelCurrent.Size = new Float2(68f, 38f);

            _legendSwatchUnexplored.Location = new Float2(186f, 14f);
            _legendSwatchUnexplored.Size = new Float2(10f, 10f);
            _legendLabelUnexplored.Location = new Float2(200f, 0f);
            _legendLabelUnexplored.Size = new Float2(56f, 38f);

            for (int i = 0; i < _markers.Count && i < Regions.Length; i++)
            {
                float px = Regions[i].MapX * mapW;
                float py = Regions[i].MapY * contentH;
                _markers[i].Location = new Float2(px - MarkerW * 0.5f, py - 12f);
                _markers[i].Size = new Float2(MarkerW, MarkerH);
            }

            // ── 区域详情 ──
            _detailPanel.Location = new Float2(SidebarW + mapW, TopBarH);
            _detailPanel.Size = new Float2(detailW, contentH);

            _detailEdge.Location = Float2.Zero;
            _detailEdge.Size = new Float2(1f, contentH);

            float pad = DetailPad;
            float innerW = Mathf.Max(120f, detailW - pad * 2f);
            const float tagW = 124f;

            _regionName.Location = new Float2(pad, 20f);
            _regionName.Size = new Float2(Mathf.Max(80f, innerW - tagW - 12f), 40f);

            _regionProgressTag.Location = new Float2(detailW - pad - tagW, 26f);
            _regionProgressTag.Size = new Float2(tagW, 26f);

            _regionDesc.Location = new Float2(pad, 64f);
            _regionDesc.Size = new Float2(innerW, 22f);

            _regionBar.Location = new Float2(pad, 100f);
            _regionBar.Size = new Float2(innerW, 8f);

            _regionExploredText.Location = new Float2(pad, 116f);
            _regionExploredText.Size = new Float2(140f, 18f);

            _regionRemainText.Location = new Float2(detailW - pad - 140f, 116f);
            _regionRemainText.Size = new Float2(140f, 18f);

            _detailDividerTop.Location = new Float2(pad, 150f);
            _detailDividerTop.Size = new Float2(innerW, 1f);

            _achTitle.Location = new Float2(pad, 164f);
            _achTitle.Size = new Float2(140f, 26f);

            _achSummary.Location = new Float2(detailW - pad - 140f, 164f);
            _achSummary.Size = new Float2(140f, 26f);

            float totalBlockTop = Mathf.Max(300f, contentH - 16f - 74f);
            float rowsTop = 202f;
            float rowsAvail = Mathf.Max(60f, totalBlockTop - 16f - rowsTop);
            int rowCount = 0;
            for (int i = 0; i < _achRows.Count; i++)
            {
                if (_achRows[i].Root.Visible)
                    rowCount++;
            }

            float step = 72f;
            if (rowCount > 0)
            {
                step = Mathf.Clamp(rowsAvail / rowCount, 46f, 72f);
            }

            int visibleIndex = 0;
            for (int i = 0; i < _achRows.Count; i++)
            {
                AchRowView view = _achRows[i];
                if (!view.Root.Visible)
                    continue;

                bool isLast = visibleIndex == rowCount - 1;
                view.Root.Location = new Float2(pad, rowsTop + visibleIndex * step);
                view.Root.Size = new Float2(innerW, step);
                LayoutAchRow(view, innerW, step, isLast);
                visibleIndex++;
            }

            _detailDividerBottom.Location = new Float2(pad, totalBlockTop - 10f);
            _detailDividerBottom.Size = new Float2(innerW, 1f);

            _totalTitle.Location = new Float2(pad, totalBlockTop + 4f);
            _totalTitle.Size = new Float2(140f, 28f);

            _totalValue.Location = new Float2(Mathf.Max(0f, detailW - pad - 150f), totalBlockTop);
            _totalValue.Size = new Float2(56f, 34f);

            _totalUnit.Location = new Float2(Mathf.Max(0f, detailW - pad - 92f), totalBlockTop + 8f);
            _totalUnit.Size = new Float2(92f, 22f);

            _totalBar.Location = new Float2(pad, totalBlockTop + 44f);
            _totalBar.Size = new Float2(innerW, 8f);
        }

        /// <summary>成就行内部布局：状态图标 / 名称 / 状态 / 进度条 / 奖励标签 / 分割线。</summary>
        private static void LayoutAchRow(AchRowView view, float w, float h, bool isLast)
        {
            view.Glyph.Location = new Float2(0f, 4f);
            view.Glyph.Size = new Float2(20f, 20f);

            const float stateW = 92f;
            view.Name.Location = new Float2(32f, 2f);
            view.Name.Size = new Float2(Mathf.Max(40f, w - 32f - stateW), 22f);

            view.State.Location = new Float2(Mathf.Max(0f, w - stateW), 2f);
            view.State.Size = new Float2(stateW, 22f);

            if (view.Bar != null)
            {
                view.Bar.Location = new Float2(32f, 26f);
                view.Bar.Size = new Float2(Mathf.Max(40f, w - 32f), 4f);
            }

            float tagsY = view.HasBar ? 38f : 28f;
            float tx = 32f;
            for (int i = 0; i < view.Tags.Count; i++)
            {
                view.Tags[i].Location = new Float2(tx, tagsY);
                tx += view.Tags[i].Width + 8f;
            }

            view.Divider.Location = new Float2(0f, h - 1f);
            view.Divider.Size = new Float2(w, 1f);
            view.Divider.Visible = !isLast;
        }

        // ═════════════════════════ IInkPage ═════════════════════════

        public void RefreshLayout()
        {
            _screenSize = new Float2(Width, Height);
            ApplyLayout();
        }
    }
}
