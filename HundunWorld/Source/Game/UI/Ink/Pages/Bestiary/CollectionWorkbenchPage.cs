using System;
using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.Ink.Components;
using HundunWorld.Game.UI.StyleSystem;

namespace HundunWorld.Game.UI.Ink.Pages.Bestiary
{
    /// <summary>
    /// 武林录/收集工作台（dom-id: nav-bestiary）——按复核报告 7.1 的列表-详情 + 筛选模板重铸。
    /// 左分类筛选、中分类列表、右详情/进度；未接入时显示“—/未接入”，不写假收集数。
    /// 分类结构来自 P16/P25 的武林录（镇守/侠迹/明暗故事/众生/万事知/据点/野外首领）。
    /// </summary>
    public class CollectionWorkbenchPage : ContainerControl, IInkPage, IInkNavigationSource
    {
        private const float ScreenEdge = 16f;
        private const float HeaderHeight = 64f;
        private const float SidebarWidth = 196f;
        private const float SidebarGap = 16f;
        private const float ContentTop = 88f;
        private const float CardHeight = 84f;
        private const float CardGap = 10f;

        private sealed class CategorySpec
        {
            public string Name;
            public string Hint;
            public Color Accent;
        }

        private static readonly CategorySpec[] Categories =
        {
            new CategorySpec { Name = "镇守", Hint = "首领挑战 · 套装 / 武学心得", Accent = InkWashTheme.GoldPrimary },
            new CategorySpec { Name = "侠迹", Hint = "江湖事迹 · 目录待接入", Accent = InkWashTheme.JadeBright },
            new CategorySpec { Name = "明暗故事", Hint = "线索 / 故事 · 目录待接入", Accent = InkWashTheme.TextSecondary },
            new CategorySpec { Name = "众生", Hint = "众生百态 · 目录待接入", Accent = InkWashTheme.SpringGreenPrimary },
            new CategorySpec { Name = "万事知", Hint = "见闻 / 知识 · 目录待接入", Accent = InkWashTheme.GoldBright },
            new CategorySpec { Name = "据点", Hint = "据点清剿 · 目录待接入", Accent = InkWashTheme.VermilionBright },
            new CategorySpec { Name = "野外首领", Hint = "世界首领 · 目录待接入", Accent = InkWashTheme.JadePrimary },
        };

        private InkBackButton _back;
        private InkTextBlock _title;
        private InkTextBlock _subtitle;
        private InkTextBlock _note;
        private InkSidebarList _sidebar;
        private InkFilterBar _filter;
        private InkActivityCard[] _cards;
        private InkPanel _detail;
        private InkTextBlock _detailTitle;
        private InkTextBlock _detailHint;
        private InkBadge _detailBadge;
        private InkStatCell _statProgress;
        private InkStatCell _statCount;
        private InkTextBlock _detailDesc;
        private InkTextBlock _footer;

        public event Action<string> NavigationRequested;

        public CollectionWorkbenchPage()
        {
            AnchorPreset = AnchorPresets.StretchAll;
            Offsets = Margin.Zero;
            BackgroundColor = InkWashTheme.BaseSecondary;
            ClipChildren = false;
            AutoFocus = false;

            BuildHeader();
            BuildSidebar();
            BuildFilter();
            BuildCards();
            BuildDetail();
            BuildFooter();

            ApplyLayout();
            SelectCategory(0);
            FlaxEngine.Debug.Log("[UI-REFORGE] CollectionWorkbenchPage built");
        }

        public void RefreshLayout() => ApplyLayout();

        protected override void OnSizeChanged()
        {
            base.OnSizeChanged();
            ApplyLayout();
        }

        private void BuildHeader()
        {
            _back = new InkBackButton { AnchorPreset = AnchorPresets.TopLeft };
            _back.Clicked += () => NavigationRequested?.Invoke(InkPageDomIds.BackOneLevel);
            AddChild(_back);

            _title = MakeText("武林录", InkTextStyle.Display, InkWashTheme.GoldPrimary, 26f);
            AddChild(_title);
            _subtitle = MakeText("区域 · 分类 · 收集进度", InkTextStyle.Caption, InkWashTheme.TextTertiary, 12f);
            AddChild(_subtitle);
            _note = MakeText("博物志服务尚未接入 · 分类结构为目录示例", InkTextStyle.Caption, InkWashTheme.TextFaint, 11f);
            _note.HorizontalAlignment = TextAlignment.Far;
            AddChild(_note);
        }

        private void BuildSidebar()
        {
            _sidebar = new InkSidebarList(InkPageDomIds.NavBestiary);
            _sidebar.NavigationRequested += id => NavigationRequested?.Invoke(id);
            AddChild(_sidebar);
        }

        private void BuildFilter()
        {
            var labels = new string[Categories.Length];
            for (var i = 0; i < Categories.Length; i++) labels[i] = Categories[i].Name;
            _filter = new InkFilterBar(labels);
            _filter.SelectedChanged += SelectCategory;
            AddChild(_filter);
        }

        private void BuildCards()
        {
            _cards = new InkActivityCard[Categories.Length];
            for (var i = 0; i < Categories.Length; i++)
            {
                var index = i;
                var spec = Categories[i];
                var card = new InkActivityCard(spec.Name, spec.Hint, "分类", "—", spec.Accent, compact: true);
                card.SetAction("查看", () => SelectCategory(index), true);
                _cards[i] = card;
                AddChild(card);
            }
        }

        private void BuildDetail()
        {
            _detail = new InkPanel { Radius = 10f };
            AddChild(_detail);

            _detailTitle = MakeText("—", InkTextStyle.Heading, InkWashTheme.TextDefault, 20f);
            _detail.AddChild(_detailTitle);

            _detailBadge = new InkBadge("未接入", InkWashTheme.TextTertiary);
            _detail.AddChild(_detailBadge);

            _detailHint = MakeText("分类目录待接入", InkTextStyle.Caption, InkWashTheme.TextTertiary, 12f);
            _detail.AddChild(_detailHint);

            _statProgress = new InkStatCell("完成度", "—", "收集服务未接入", InkWashTheme.GoldBright);
            _detail.AddChild(_statProgress);

            _statCount = new InkStatCell("收集数", "— / —", "条目服务未接入", InkWashTheme.JadeBright);
            _detail.AddChild(_statCount);

            _detailDesc = MakeText("选择左侧分类查看说明。布局与导航已就绪，条目与奖励待接入。",
                InkTextStyle.Body, InkWashTheme.TextSecondary, 13f);
            _detailDesc.VerticalAlignment = TextAlignment.Near;
            _detail.AddChild(_detailDesc);
        }

        private void BuildFooter()
        {
            _footer = MakeText("分类结构来自联网研究复核（P16/P25）；数值与条目以服务端接入为准。",
                InkTextStyle.Caption, InkWashTheme.TextFaint, 11f);
            AddChild(_footer);
        }

        private void SelectCategory(int index)
        {
            if (Categories.Length == 0) return;
            index = Mathf.Clamp(index, 0, Categories.Length - 1);
            var spec = Categories[index];
            _filter?.Select(index, notify: false);
            if (_detailTitle != null) _detailTitle.Text = spec.Name;
            if (_detailHint != null) _detailHint.Text = spec.Hint;
            if (_detailBadge != null)
            {
                _detailBadge.Text = "未接入";
                _detailBadge.SetAccent(spec.Accent);
            }
            if (_statProgress != null) _statProgress.SetValue("—", "收集服务未接入");
            if (_statCount != null) _statCount.SetValue("— / —", "条目服务未接入");
            if (_detailDesc != null)
                _detailDesc.Text = $"{spec.Name}：{spec.Hint}。布局与导航已就绪，条目待接入。";
        }

        private static InkTextBlock MakeText(string text, InkTextStyle style, Color color, float size)
        {
            var label = new InkTextBlock(style)
            {
                Text = text,
                TextColor = color,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = Color.Transparent,
            };
            label.Font = InkRenderHelper.GetFontRef(
                style == InkTextStyle.Display ? InkWashTheme.FontRole.Display :
                style == InkTextStyle.Heading ? InkWashTheme.FontRole.Heading : InkWashTheme.FontRole.Body, size);
            return label;
        }

        private void ApplyLayout()
        {
            if (_back == null) return;

            float w = Width > 0f ? Width : 1920f;
            float h = Height > 0f ? Height : 1080f;

            _back.Location = new Float2(ScreenEdge, ScreenEdge);
            _title.Location = new Float2(64f, 8f);
            _title.Size = new Float2(220f, 32f);
            _subtitle.Location = new Float2(64f, 38f);
            _subtitle.Size = new Float2(420f, 18f);
            _note.Location = new Float2(w - 560f, 24f);
            _note.Size = new Float2(540f, 18f);

            _sidebar.Layout(ScreenEdge, ContentTop, SidebarWidth, h - ContentTop - ScreenEdge);

            float contentX = ScreenEdge + SidebarWidth + SidebarGap;
            float contentW = Mathf.Max(0f, w - contentX - ScreenEdge);
            _filter.Location = new Float2(contentX, ContentTop);
            _filter.Size = new Float2(contentW, 28f);

            float listTop = ContentTop + 44f;
            float detailW = Mathf.Min(560f, contentW * 0.38f);
            float listW = Mathf.Max(0f, contentW - detailW - 16f);
            float listH = h - listTop - ScreenEdge;

            for (var i = 0; i < _cards.Length; i++)
            {
                _cards[i].Location = new Float2(contentX, listTop + i * (CardHeight + CardGap));
                _cards[i].Size = new Float2(listW, CardHeight);
            }

            _detail.Location = new Float2(contentX + listW + 16f, listTop);
            _detail.Size = new Float2(detailW, Mathf.Min(listH, 620f));

            float pad = 20f;
            _detailTitle.Location = new Float2(pad, 18f);
            _detailTitle.Size = new Float2(Mathf.Max(0f, detailW - pad * 2f - 80f), 30f);
            _detailBadge.Location = new Float2(detailW - 92f, 18f);
            _detailBadge.Size = new Float2(72f, 22f);
            _detailHint.Location = new Float2(pad, 50f);
            _detailHint.Size = new Float2(Mathf.Max(0f, detailW - pad * 2f), 18f);

            float statW = Mathf.Max(0f, (detailW - pad * 2f - 12f) / 2f);
            _statProgress.Location = new Float2(pad, 82f);
            _statProgress.Size = new Float2(statW, 64f);
            _statCount.Location = new Float2(pad + statW + 12f, 82f);
            _statCount.Size = new Float2(statW, 64f);

            _detailDesc.Location = new Float2(pad, 162f);
            _detailDesc.Size = new Float2(Mathf.Max(0f, detailW - pad * 2f), 120f);

            _footer.Location = new Float2(contentX, h - 28f);
            _footer.Size = new Float2(contentW, 16f);
        }
    }
}
