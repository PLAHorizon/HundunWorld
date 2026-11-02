using System;
using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.Ink.Components;
using HundunWorld.Game.UI.StyleSystem;

namespace HundunWorld.Game.UI.Ink.Pages.Activities
{
    /// <summary>
    /// 活动中心（dom-id: nav-activities）——按第二轮复核报告 7.1 重铸：
    /// 顶部「同游」四卡（侠境/试剑/止戈/闲趣）+「闯荡」多人活动列表 +「挑战」心力入口。
    /// <para>
    /// 只做目录与导航：数值/服务端未接入时显示“—/未接入”，不写设计稿假数字，也不做假交互。
    /// 卡片上的 10人/5人/PvP/多人 徽标与“缺治疗/推荐造诣”等字段，来自 11 号复核报告的对账表。
    /// </para>
    /// </summary>
    public class ActivityHubPage : ContainerControl, IInkPage, IInkNavigationSource
    {
        private const float ScreenEdge = 16f;
        private const float HeaderHeight = 64f;
        private const float SidebarWidth = 196f;
        private const float SidebarGap = 16f;
        private const float ContentTop = 88f;
        private const float StatHeight = 64f;
        private const float SectionGap = 16f;

        private InkBackButton _back;
        private InkTextBlock _title;
        private InkTextBlock _subtitle;
        private InkTextBlock _note;
        private InkSidebarList _sidebar;

        private InkStatCell[] _stats;
        private InkActivityCard[] _mainCards;
        private InkActivityCard[] _roamCards;
        private InkActivityCard[] _challengeCards;

        private InkTextBlock _roamTitle;
        private InkTextBlock _roamNote;
        private InkTextBlock _challengeTitle;
        private InkTextBlock _challengeNote;
        private InkTextBlock _footer;

        public event Action<string> NavigationRequested;

        public ActivityHubPage()
        {
            AnchorPreset = AnchorPresets.StretchAll;
            Offsets = Margin.Zero;
            BackgroundColor = InkWashTheme.BaseSecondary;
            ClipChildren = false;
            AutoFocus = false;

            BuildHeader();
            BuildSidebar();
            BuildStats();
            BuildMainCards();
            BuildRoamSection();
            BuildChallengeSection();
            BuildFooter();

            ApplyLayout();
            FlaxEngine.Debug.Log("[UI-REFORGE] ActivityHubPage built");
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

            _title = MakeText("同游", InkTextStyle.Display, InkWashTheme.GoldPrimary, 26f);
            _title.HorizontalAlignment = TextAlignment.Near;
            AddChild(_title);

            _subtitle = MakeText("侠境 · 试剑 · 止戈 · 闯荡 · 闲趣", InkTextStyle.Caption, InkWashTheme.TextTertiary, 12f);
            AddChild(_subtitle);

            _note = MakeText("活动服务尚未接入 · 卡片为目录结构", InkTextStyle.Caption, InkWashTheme.TextFaint, 11f);
            _note.HorizontalAlignment = TextAlignment.Far;
            AddChild(_note);
        }

        private void BuildSidebar()
        {
            _sidebar = new InkSidebarList(InkPageDomIds.NavActivities);
            _sidebar.NavigationRequested += id => NavigationRequested?.Invoke(id);
            AddChild(_sidebar);
        }

        private void BuildStats()
        {
            _stats = new[]
            {
                new InkStatCell("四海笺", "—", "每周获得 · 未接入", InkWashTheme.JadeBright),
                new InkStatCell("心力", "— / —", "每日恢复 · 未接入", InkWashTheme.GoldBright),
                new InkStatCell("本周目标", "—", "首通/周常 · 未接入", InkWashTheme.TextSecondary),
            };
            foreach (var s in _stats) AddChild(s);
        }

        private void BuildMainCards()
        {
            _mainCards = new[]
            {
                new InkActivityCard("侠境", "10 人 · 周本 · 智能助战", "10人", "奖励次数共享 · 待接入", InkWashTheme.GoldPrimary),
                new InkActivityCard("试剑", "5 人 · 周首通 · 可匹配", "5人", "匹配与首通 · 待接入", InkWashTheme.JadeBright),
                new InkActivityCard("止戈", "论剑 / 觉障林 / 阵营", "PvP", "对局与赛季 · 待接入", InkWashTheme.VermilionBright),
                new InkActivityCard("闲趣", "小游戏 · 可邀请", "休闲", "玩法与奖励 · 待接入", InkWashTheme.SpringGreenPrimary),
            };

            _mainCards[0].SetAction("查看", () => NavigationRequested?.Invoke(InkPageDomIds.NavDungeonEntry), true);
            _mainCards[1].SetAction("查看", () => NavigationRequested?.Invoke(InkPageDomIds.NavDungeonEntry), true);
            _mainCards[2].SetAction("查看", () => NavigationRequested?.Invoke(InkPageDomIds.NavMultiplayer), true);
            _mainCards[3].SetAction("查看", () => NavigationRequested?.Invoke(InkPageDomIds.NavCasualMode), true);

            foreach (var c in _mainCards) AddChild(c);
        }

        private void BuildRoamSection()
        {
            _roamTitle = MakeText("闯荡 · 多人活动", InkTextStyle.Heading, InkWashTheme.GoldPrimary, 18f);
            _roamNote = MakeText("多人标签 · 每周奖励次数 · 目录结构", InkTextStyle.Caption, InkWashTheme.TextFaint, 11f);
            _roamNote.HorizontalAlignment = TextAlignment.Far;
            AddChild(_roamTitle);
            AddChild(_roamNote);

            var names = new[]
            {
                ("群力共伐", "群体首领 · 多人协作"),
                ("有因必应", "多人协作"),
                ("天泉特训", "多人训练"),
                ("福地修炼", "多人修炼"),
                ("五禽戏", "多人活动"),
                ("红炉雅集", "多人雅集"),
                ("多人会诊", "多人协作"),
                ("悬壶济世", "多人行善"),
            };
            _roamCards = new InkActivityCard[names.Length];
            for (var i = 0; i < names.Length; i++)
            {
                var card = new InkActivityCard(names[i].Item1, names[i].Item2, "多人", "未接入", InkWashTheme.JadeBright, compact: true);
                card.SetAction("未接入", null, false);
                _roamCards[i] = card;
                AddChild(card);
            }
        }

        private void BuildChallengeSection()
        {
            _challengeTitle = MakeText("挑战 · 心力", InkTextStyle.Heading, InkWashTheme.GoldPrimary, 18f);
            _challengeNote = MakeText("镇守 / 据点 · 消耗心力 · 待接入", InkTextStyle.Caption, InkWashTheme.TextFaint, 11f);
            _challengeNote.HorizontalAlignment = TextAlignment.Far;
            AddChild(_challengeTitle);
            AddChild(_challengeNote);

            _challengeCards = new[]
            {
                new InkActivityCard("镇守·挑战", "指定套装 / 武学心得", "挑战", "心力 — / —", InkWashTheme.GoldBright),
                new InkActivityCard("据点·挑战", "随机套装 / 奇术碎片", "挑战", "心力 — / —", InkWashTheme.JadeBright),
            };
            foreach (var c in _challengeCards)
            {
                c.SetAction("未接入", null, false);
                AddChild(c);
            }
        }

        private void BuildFooter()
        {
            _footer = MakeText("目录结构与人数/职责标签来自联网研究复核；数值与规则以服务端接入为准。",
                InkTextStyle.Caption, InkWashTheme.TextFaint, 11f);
            AddChild(_footer);
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
            _title.Size = new Float2(180f, 32f);
            _subtitle.Location = new Float2(64f, 38f);
            _subtitle.Size = new Float2(420f, 18f);
            _note.Location = new Float2(w - 520f, 24f);
            _note.Size = new Float2(500f, 18f);

            _sidebar.Layout(ScreenEdge, ContentTop, SidebarWidth, h - ContentTop - ScreenEdge);

            float contentX = ScreenEdge + SidebarWidth + SidebarGap;
            float contentW = Mathf.Max(0f, w - contentX - ScreenEdge);

            float statGap = 12f;
            float statW = (contentW - 2f * statGap) / 3f;
            for (var i = 0; i < _stats.Length; i++)
            {
                _stats[i].Location = new Float2(contentX + i * (statW + statGap), ContentTop);
                _stats[i].Size = new Float2(statW, StatHeight);
            }

            float cardGap = 12f;
            float cardW = (contentW - cardGap) / 2f;
            float cardH = 108f;
            float mainTop = ContentTop + StatHeight + SectionGap;
            for (var i = 0; i < _mainCards.Length; i++)
            {
                _mainCards[i].Location = new Float2(
                    contentX + (i % 2) * (cardW + cardGap),
                    mainTop + (i / 2) * (cardH + cardGap));
                _mainCards[i].Size = new Float2(cardW, cardH);
            }

            float roamTop = mainTop + 2f * cardH + cardGap + SectionGap;
            _roamTitle.Location = new Float2(contentX, roamTop);
            _roamTitle.Size = new Float2(contentW, 24f);
            _roamNote.Location = new Float2(contentX, roamTop);
            _roamNote.Size = new Float2(contentW, 24f);

            float roamGap = 10f;
            float roamW = (contentW - 3f * roamGap) / 4f;
            float roamH = 84f;
            float roamCardsTop = roamTop + 30f;
            for (var i = 0; i < _roamCards.Length; i++)
            {
                _roamCards[i].Location = new Float2(
                    contentX + (i % 4) * (roamW + roamGap),
                    roamCardsTop + (i / 4) * (roamH + roamGap));
                _roamCards[i].Size = new Float2(roamW, roamH);
            }

            float challengeTop = roamCardsTop + 2f * roamH + roamGap + SectionGap;
            _challengeTitle.Location = new Float2(contentX, challengeTop);
            _challengeTitle.Size = new Float2(contentW, 24f);
            _challengeNote.Location = new Float2(contentX, challengeTop);
            _challengeNote.Size = new Float2(contentW, 24f);

            float challengeW = (contentW - cardGap) / 2f;
            float challengeH = 84f;
            float challengeCardsTop = challengeTop + 30f;
            for (var i = 0; i < _challengeCards.Length; i++)
            {
                _challengeCards[i].Location = new Float2(
                    contentX + i * (challengeW + cardGap), challengeCardsTop);
                _challengeCards[i].Size = new Float2(challengeW, challengeH);
            }

            _footer.Location = new Float2(contentX, challengeCardsTop + challengeH + 12f);
            _footer.Size = new Float2(contentW, 18f);
        }
    }
}
