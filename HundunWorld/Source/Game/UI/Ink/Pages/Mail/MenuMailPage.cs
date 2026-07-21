using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.Ink;
using HundunWorld.Game.UI.StyleSystem;
using System;
using System.Collections.Generic;

namespace HundunWorld.Game.UI.Ink.Pages.Mail
{
    /// <summary>
    /// 飞鸽传书 —— 三栏邮件系统
    /// 对应 design: game-ui-system/pages/mail.html
    /// </summary>
    public class MenuMailPage : ContainerControl, IInkPage
    {
        // ───────────── Data Types ─────────────

        private class AttachItem
        {
            public string Name;
            public string Desc;
            public int Count;
            public Color QualityColor;
            public bool Claimed;
        }

        private class MailEntry
        {
            public string SenderChar;
            public string SenderName;
            public string Section;
            public Color SectionDot;
            public Color SectionText;
            public string Subject;
            public string Date;
            public bool Unread;
            public string Body;
            public AttachItem[] Attachments;
        }

        /// <summary>可点击的 Panel，用于邮件列表条目</summary>
        private class ClickablePanel : Panel
        {
            public event Action Clicked;
            public event Action<bool> HoverChanged;

            public override bool OnMouseUp(Float2 location, MouseButton button)
            {
                if (button == MouseButton.Left)
                    Clicked?.Invoke();
                return base.OnMouseUp(location, button);
            }

            public override void OnMouseEnter(Float2 location)
            {
                HoverChanged?.Invoke(true);
                base.OnMouseEnter(location);
            }

            public override void OnMouseLeave()
            {
                HoverChanged?.Invoke(false);
                base.OnMouseLeave();
            }
        }

        // ───────────── Fields ─────────────

        private Float2 _screenSize;
        private MailEntry[] _mails;
        private int _selectedIdx;

        // top bar
        private Panel _topBar;
        private InkButton _backBtn;
        private Label _titleLbl;
        private Label _unreadLbl;
        private InkButton _composeBtn;

        // left panel
        private Panel _leftPanel;
        private List<InkButton> _tabs;
        private Panel _listPanel;
        private List<ClickablePanel> _mailItems;
        private List<Label> _itemSenderLabels;

        // center panel
        private Panel _centerPanel;
        private Panel _senderHeader;
        private Label _senderName;
        private InkButton _senderTagBtn;
        private Label _senderDate;
        private Label _attachHint;
        private Label _subjectLbl;
        private Label _subjectTag;
        private InkPaperPanel _paper;
        private Label _bodyLbl;
        private Panel _rewardBox;
        private List<Panel> _rewardSlots;

        // right panel
        private Panel _rightPanel;
        private Label _rightTitle;
        private Label _rightCount;
        private Panel _attachList;
        private List<Panel> _attachSlots;
        private InkButton _claimAllBtn;
        private Label _remainLbl;
        private InkButton _replyBtn;
        private InkButton _forwardBtn;
        private InkButton _deleteBtn;

        private const float TopH = 56f;
        private const float LeftW = 300f;
        private const float RightW = 280f;

        public event Action<string> NavigationRequested;

        // ───────────── Constructor ─────────────

        public MenuMailPage()
        {
            _screenSize = new Float2(Width, Height);
            if (_screenSize.X <= 0f || _screenSize.Y <= 0f)
                _screenSize = new Float2(1920f, 1080f);

            AnchorPreset = AnchorPresets.StretchAll;
            BackgroundColor = Color.Transparent;
            ClipChildren = false;
            AutoFocus = false;
            Location = Float2.Zero;
            Size = _screenSize;

            BuildData();
            try
            {
                BuildBg();
                BuildTopBar();
                BuildLeftPanel();
                BuildCenterPanel();
                BuildRightPanel();
                SelectMail(0);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MenuMailPage] init: {ex.Message}");
            }
        }

        // ───────────── Data ─────────────

        private void BuildData()
        {
            _mails = new MailEntry[8];
            _mails[0] = new MailEntry
            {
                SenderChar = "系", SenderName = "系统官府", Section = "系统",
                SectionDot = InkWashTheme.GoldPrimary, SectionText = InkWashTheme.GoldBright,
                Subject = "周末庆典 · 论剑豪礼", Date = "今日 辰时", Unread = true,
                Body = "少侠台鉴：\n\n适逢周末庆典，江湖豪杰齐聚论剑台。阁下于本次论剑大会中表现卓绝……\n\n—— 论剑台主办 · 系统官府",
                Attachments = new[]
                {
                    new AttachItem { Name = "灵石", Desc = "修炼资材", Count = 500, QualityColor = InkWashTheme.QualityCommon },
                    new AttachItem { Name = "玄铁精", Desc = "铸兵材料", Count = 10, QualityColor = InkWashTheme.QualityRare },
                    new AttachItem { Name = "九转还魂丹", Desc = "续命灵药", Count = 3, QualityColor = InkWashTheme.QualityEpic },
                    new AttachItem { Name = "真武剑诀·残卷", Desc = "武当秘传", Count = 1, QualityColor = InkWashTheme.QualityLegendary, Claimed = true },
                }
            };
            _mails[1] = new MailEntry { SenderChar = "系", SenderName = "系统官府", Section = "系统", SectionDot = InkWashTheme.GoldPrimary, SectionText = InkWashTheme.GoldBright, Subject = "服务器维护公告", Date = "今日 卯时", Body = "服务器维护公告……" };
            _mails[2] = new MailEntry { SenderChar = "系", SenderName = "系统官府", Section = "系统", SectionDot = InkWashTheme.GoldPrimary, SectionText = InkWashTheme.GoldBright, Subject = "充值灵石到账通知", Date = "三日前", Body = "充值到账通知……" };
            _mails[3] = new MailEntry { SenderChar = "帮", SenderName = "玄武堂", Section = "帮派", SectionDot = InkWashTheme.JadePrimary, SectionText = InkWashTheme.JadeBright, Subject = "帮派战结算奖励", Date = "昨日", Unread = true, Body = "帮派战结算……", Attachments = new[] { new AttachItem { Name = "帮派贡献", Desc = "帮贡", Count = 200, QualityColor = InkWashTheme.QualityCommon } } };
            _mails[4] = new MailEntry { SenderChar = "帮", SenderName = "朱雀堂", Section = "帮派", SectionDot = InkWashTheme.JadePrimary, SectionText = InkWashTheme.JadeBright, Subject = "每周贡献奖励", Date = "七月十二", Body = "每周贡献奖励……", Attachments = new[] { new AttachItem { Name = "帮贡", Desc = "帮派贡献", Count = 100, QualityColor = InkWashTheme.QualityCommon } } };
            _mails[5] = new MailEntry { SenderChar = "张", SenderName = "剑客张三", Section = "玩家", SectionDot = InkWashTheme.BloodPrimary, SectionText = InkWashTheme.BloodBright, Subject = "切磋之约 · 紫霄宫", Date = "昨日", Body = "兄弟，有空组队刷副本吗？", Attachments = new[] { new AttachItem { Name = "请帖", Desc = "切磋请帖", Count = 1, QualityColor = InkWashTheme.QualityCommon } } };
            _mails[6] = new MailEntry { SenderChar = "李", SenderName = "飞燕李四", Section = "玩家", SectionDot = InkWashTheme.BloodPrimary, SectionText = InkWashTheme.BloodBright, Subject = "西域奇遇相告", Date = "三日前", Unread = true, Body = "西域奇遇相告……" };
            _mails[7] = new MailEntry { SenderChar = "王", SenderName = "玄武王五", Section = "玩家", SectionDot = InkWashTheme.BloodPrimary, SectionText = InkWashTheme.BloodBright, Subject = "商队护送谢礼", Date = "七月十一", Body = "商队护送谢礼……" };
        }

        private int CalcUnread()
        {
            int c = 0;
            foreach (var m in _mails) if (m.Unread) c++;
            return c;
        }

        // ───────────── Build: Background ─────────────

        private void BuildBg()
        {
            new Panel
            {
                AnchorPreset = AnchorPresets.StretchAll,
                BackgroundColor = InkWashTheme.Void,
                Parent = this
            };
        }

        // ───────────── Build: Top Bar ─────────────

        private void BuildTopBar()
        {
            _topBar = new Panel
            {
                BackgroundColor = new Color(InkWashTheme.BaseSecondary.R, InkWashTheme.BaseSecondary.G, InkWashTheme.BaseSecondary.B, 0.9f),
                Parent = this
            };

            _backBtn = new InkButton
            {
                Text = "\u2190",
                ButtonSize = InkButtonSize.Sm,
                Variant = InkButtonVariant.Ghost,
                Parent = _topBar
            };
            _backBtn.Clicked += () => NavigationRequested?.Invoke("combat-hud");

            _titleLbl = new Label
            {
                Text = "\u98DE\u9E3F\u4F20\u4E66",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 20f),
                TextColor = InkWashTheme.GoldPrimary,
                Parent = _topBar
            };

            _unreadLbl = new Label
            {
                Text = $"\u672A\u8BFB {CalcUnread()} \u5C01",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                TextColor = InkWashTheme.TextTertiary,
                Parent = _topBar
            };

            _composeBtn = new InkButton
            {
                Text = "\u5199\u4FE1",
                ButtonSize = InkButtonSize.Sm,
                Variant = InkButtonVariant.Ghost,
                Parent = _topBar
            };
            _composeBtn.Clicked += () => Debug.Log("[Mail] compose");
        }

        // ───────────── Build: Left Panel ─────────────

        private void BuildLeftPanel()
        {
            _leftPanel = new Panel
            {
                BackgroundColor = InkWashTheme.Panel,
                Parent = this
            };

            // Search box
            var searchBox = new Panel
            {
                BackgroundColor = InkWashTheme.PaperPanelBg,
                Parent = _leftPanel
            };
            new Label
            {
                Text = "\u68C0\u7D22\u4FE1\u4EF6\u5173\u952E\u8BCD",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                TextColor = InkWashTheme.TextTertiary,
                VerticalAlignment = TextAlignment.Center,
                Parent = searchBox
            };

            // Tabs
            _tabs = new List<InkButton>();
            string[] tabNames = { "\u5168\u90E8", "\u7CFB\u7EDF", "\u73A9\u5BB6", "\u5E2E\u6D3E" };
            for (int i = 0; i < tabNames.Length; i++)
            {
                int ci = i;
                var t = new InkButton
                {
                    Text = tabNames[i],
                    ButtonSize = InkButtonSize.Sm,
                    Variant = InkButtonVariant.Ghost,
                    Parent = _leftPanel
                };
                t.Clicked += () => SelectTab(ci);
                _tabs.Add(t);
            }
            SelectTab(0);

            // Mail list
            _listPanel = new Panel
            {
                BackgroundColor = Color.Transparent,
                ClipChildren = true,
                Parent = _leftPanel
            };

            _mailItems = new List<ClickablePanel>();
            _itemSenderLabels = new List<Label>();

            string curSection = null;
            foreach (var mail in _mails)
            {
                if (mail.Section != curSection)
                {
                    curSection = mail.Section;
                    int cnt = 0;
                    foreach (var m in _mails) if (m.Section == curSection) cnt++;
                    BuildSectionHeader(curSection, cnt, mail.SectionDot, mail.SectionText);
                }
                BuildMailItem(mail);
            }
        }

        private void BuildSectionHeader(string section, int count, Color dotColor, Color textColor)
        {
            var hdr = new ContainerControl
            {
                BackgroundColor = Color.Transparent,
                Parent = _listPanel
            };
            new Panel
            {
                Size = new Float2(6, 6),
                BackgroundColor = dotColor,
                Parent = hdr
            };
            new Label
            {
                Text = $"{section} {count}",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.TextSecondary,
                Parent = hdr
            };
        }

        private void BuildMailItem(MailEntry mail)
        {
            int idx = _mailItems.Count;
            var item = new ClickablePanel
            {
                BackgroundColor = Color.Transparent,
                Parent = _listPanel
            };

            // Unread dot
            new Panel
            {
                Size = new Float2(8, 8),
                BackgroundColor = mail.Unread ? InkWashTheme.GoldPrimary : Color.Transparent,
                Parent = item
            };

            // Avatar
            var avatar = new Panel
            {
                Size = new Float2(36, 36),
                BackgroundColor = mail.Section == "系统"
                    ? new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.15f)
                    : mail.Section == "帮派"
                        ? new Color(InkWashTheme.JadeDeep.R, InkWashTheme.JadeDeep.G, InkWashTheme.JadeDeep.B, 0.15f)
                        : new Color(InkWashTheme.JadeDeep.R, InkWashTheme.JadeDeep.G, InkWashTheme.JadeDeep.B, 0.06f),
                Parent = item
            };
            new Label
            {
                Text = mail.SenderChar,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 14f),
                TextColor = mail.Unread ? InkWashTheme.GoldPrimary : InkWashTheme.TextTertiary,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.StretchAll,
                Parent = avatar
            };

            // Sender
            var sender = new Label
            {
                Text = mail.SenderName,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 13f),
                TextColor = mail.Unread ? InkWashTheme.TextDefault : InkWashTheme.TextSecondary,
                Parent = item
            };
            _itemSenderLabels.Add(sender);

            // Date
            new Label
            {
                Text = mail.Date,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 11f),
                TextColor = InkWashTheme.TextTertiary,
                Parent = item
            };

            // Subject
            new Label
            {
                Text = mail.Subject,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = mail.Unread ? InkWashTheme.TextSecondary : InkWashTheme.TextTertiary,
                Parent = item
            };

            // Attachment hint
            if (mail.Attachments != null && mail.Attachments.Length > 0)
            {
                new Label
                {
                    Text = "\uD83D\uDCCE",
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 10f),
                    TextColor = InkWashTheme.GoldPrimary,
                    Parent = item
                };
            }

            item.Clicked += () => SelectMail(idx);
            item.HoverChanged += (enter) =>
            {
                if (idx != _selectedIdx)
                    item.BackgroundColor = enter ? new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.06f) : Color.Transparent;
            };
            _mailItems.Add(item);
        }

        private void SelectTab(int idx)
        {
            for (int i = 0; i < _tabs.Count; i++)
            {
                _tabs[i].BackgroundColor = i == idx ? InkWashTheme.GoldPrimary * 0.12f : Color.Transparent;
                _tabs[i].TextColor = i == idx ? InkWashTheme.GoldPrimary : InkWashTheme.TextSecondary;
                _tabs[i].BorderColor = i == idx ? InkWashTheme.BorderGold : Color.Transparent;
            }
        }

        // ───────────── Build: Center Panel ─────────────

        private void BuildCenterPanel()
        {
            _centerPanel = new Panel
            {
                BackgroundColor = InkWashTheme.Void,
                Parent = this
            };

            // Sender header
            _senderHeader = new Panel
            {
                BackgroundColor = Color.Transparent,
                Parent = _centerPanel
            };

            var avatarBig = new Panel
            {
                Size = new Float2(56, 56),
                BackgroundColor = new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.12f),
                Parent = _senderHeader
            };
            new Label
            {
                Text = "\u7CFB",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 24f),
                TextColor = InkWashTheme.GoldPrimary,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.StretchAll,
                Parent = avatarBig
            };

            _senderName = new Label
            {
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 18f),
                TextColor = InkWashTheme.TextDefault,
                Parent = _senderHeader
            };

            _senderTagBtn = new InkButton
            {
                Text = "\u7CFB\u7EDF",
                ButtonSize = InkButtonSize.Sm,
                BackgroundColor = new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.12f),
                BorderColor = InkWashTheme.BorderGold,
                TextColor = InkWashTheme.GoldBright,
                Enabled = false,
                Parent = _senderHeader
            };

            _senderDate = new Label
            {
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                TextColor = InkWashTheme.TextSecondary,
                Parent = _senderHeader
            };

            _attachHint = new Label
            {
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                TextColor = InkWashTheme.GoldPrimary,
                Parent = _senderHeader
            };

            // Subject
            _subjectLbl = new Label
            {
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 30f),
                TextColor = InkWashTheme.TextDefault,
                Parent = _centerPanel
            };

            _subjectTag = new Label
            {
                Text = "\u5E86\u5178 \u00B7 \u8BBA\u5251",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 11f),
                TextColor = InkWashTheme.GoldBright,
                BackgroundColor = new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.08f),
                Parent = _centerPanel
            };

            // Letter paper
            _paper = new InkPaperPanel
            {
                Parent = _centerPanel
            };

            _bodyLbl = new Label
            {
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                TextColor = InkWashTheme.TextOnPaper,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Near,
                Parent = _paper
            };

            // Reward box
            _rewardBox = new Panel
            {
                BackgroundColor = new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.04f),
                Parent = _centerPanel
            };
            _rewardSlots = new List<Panel>();
        }

        // ───────────── Build: Right Panel ─────────────

        private void BuildRightPanel()
        {
            _rightPanel = new Panel
            {
                BackgroundColor = InkWashTheme.Panel,
                Parent = this
            };

            _rightTitle = new Label
            {
                Text = "\u4FE1\u7269\u9644\u4EF6",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 13f),
                TextColor = InkWashTheme.TextDefault,
                Parent = _rightPanel
            };

            _rightCount = new Label
            {
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 11f),
                TextColor = InkWashTheme.GoldPrimary,
                BackgroundColor = new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.12f),
                Parent = _rightPanel
            };

            _attachList = new Panel
            {
                BackgroundColor = Color.Transparent,
                ClipChildren = true,
                Parent = _rightPanel
            };
            _attachSlots = new List<Panel>();

            _claimAllBtn = new InkButton
            {
                Text = "\u4E00\u952E\u9886\u53D6",
                ButtonSize = InkButtonSize.Lg,
                Variant = InkButtonVariant.Primary,
                Parent = _rightPanel
            };
            _claimAllBtn.Clicked += () => Debug.Log("[Mail] claim all");

            _remainLbl = new Label
            {
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.TextTertiary,
                HorizontalAlignment = TextAlignment.Center,
                Parent = _rightPanel
            };

            _replyBtn = new InkButton
            {
                Text = "\u56DE\u590D",
                ButtonSize = InkButtonSize.Sm,
                Variant = InkButtonVariant.Ghost,
                Parent = _rightPanel
            };
            _replyBtn.Clicked += () => Debug.Log("[Mail] reply");

            _forwardBtn = new InkButton
            {
                Text = "\u8F6C\u53D1",
                ButtonSize = InkButtonSize.Sm,
                Variant = InkButtonVariant.Ghost,
                Parent = _rightPanel
            };
            _forwardBtn.Clicked += () => Debug.Log("[Mail] forward");

            _deleteBtn = new InkButton
            {
                Text = "\u5220\u9664",
                ButtonSize = InkButtonSize.Sm,
                BackgroundColor = new Color(InkWashTheme.BloodPrimary.R, InkWashTheme.BloodPrimary.G, InkWashTheme.BloodPrimary.B, 0.08f),
                TextColor = InkWashTheme.BloodBright,
                BorderColor = InkWashTheme.BorderVermilion,
                Parent = _rightPanel
            };
            _deleteBtn.Clicked += () => Debug.Log("[Mail] delete");
        }

        // ───────────── Select Mail ─────────────

        private void SelectMail(int idx)
        {
            if (idx < 0 || idx >= _mails.Length) return;
            _selectedIdx = idx;
            var mail = _mails[idx];

            // Highlight list item
            for (int i = 0; i < _mailItems.Count; i++)
                _mailItems[i].BackgroundColor = i == idx ? new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.1f) : Color.Transparent;

            // Sender header
            _senderName.Text = mail.SenderName;
            _senderTagBtn.Text = mail.Section;
            _senderDate.Text = "\u4E19\u5348\u5E74 \u4E03\u6708\u5341\u4E94 \u8FB0\u65F6\u4E09\u523B";
            bool hasAtt = mail.Attachments != null && mail.Attachments.Length > 0;
            _attachHint.Text = hasAtt ? "\u9644\u6709\u8D4F\u8D50" : "";
            _attachHint.Visible = hasAtt;

            // Subject
            _subjectLbl.Text = mail.Subject;
            _subjectTag.Visible = idx == 0;

            // Body
            _bodyLbl.Text = mail.Body;

            // Reward area
            foreach (var s in _rewardSlots) s.Dispose();
            _rewardSlots.Clear();

            if (hasAtt)
            {
                _rewardBox.Visible = true;
                foreach (var a in mail.Attachments)
                {
                    var slot = new ContainerControl
                    {
                        BackgroundColor = InkWashTheme.PaperPanelBg,
                        Parent = _rewardBox
                    };
                    new Label
                    {
                        Text = a.Name,
                        Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                        TextColor = InkWashTheme.TextDefault,
                        Parent = slot
                    };
                    new Label
                    {
                        Text = $"\u00D7{a.Count}",
                        Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 13f),
                        TextColor = a.QualityColor,
                        HorizontalAlignment = TextAlignment.Far,
                        Parent = slot
                    };
                    _rewardSlots.Add(slot as Panel);
                }
            }
            else
            {
                _rewardBox.Visible = false;
            }

            // Right panel attachments
            foreach (var s in _attachSlots) s.Dispose();
            _attachSlots.Clear();

            int claimable = 0;
            if (hasAtt)
            {
                _rightCount.Text = $"{mail.Attachments.Length} \u4EF6";
                foreach (var a in mail.Attachments)
                {
                    if (!a.Claimed) claimable++;
                    var slot = new ContainerControl
                    {
                        BackgroundColor = InkWashTheme.PaperPanelBg,
                        Parent = _attachList
                    };
                    new Label
                    {
                        Text = a.Name,
                        Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 13f),
                        TextColor = a.QualityColor,
                        Parent = slot
                    };
                    new Label
                    {
                        Text = a.Desc,
                        Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                        TextColor = InkWashTheme.TextTertiary,
                        Parent = slot
                    };
                    var countLbl = new Label
                    {
                        Text = $"\u00D7{a.Count}",
                        Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 13f),
                        TextColor = a.Claimed ? InkWashTheme.TextTertiary : InkWashTheme.TextDefault,
                        HorizontalAlignment = TextAlignment.Far,
                        Parent = slot
                    };
                    if (a.Claimed)
                    {
                        new Label
                        {
                            Text = "\u2713\u5DF2\u9886\u53D6",
                            Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                            TextColor = InkWashTheme.JadeBright,
                            Parent = slot
                        };
                    }
                    _attachSlots.Add(slot as Panel);
                }
            }
            else
            {
                _rightCount.Text = "0 \u4EF6";
            }
            _remainLbl.Text = $"\u5269\u4F59\u53EF\u9886\u53D6 {claimable} \u4EF6";

            ApplyLayout();
        }

        // ───────────── Layout ─────────────

        private void ApplyLayout()
        {
            float sw = Width > 0 ? Width : _screenSize.X;
            float sh = Height > 0 ? Height : _screenSize.Y;

            // Top bar
            _topBar.Location = Float2.Zero;
            _topBar.Size = new Float2(sw, TopH);

            _backBtn.Location = new Float2(16, (TopH - 28) / 2);
            _backBtn.Size = new Float2(32, 28);

            float x = 52;
            _titleLbl.Location = new Float2(x, (TopH - 22) / 2);

            _composeBtn.Location = new Float2(sw - 76, (TopH - 28) / 2);
            _composeBtn.Size = new Float2(60, 28);

            _unreadLbl.Location = new Float2(sw - 76 - 8 - 130, (TopH - 18) / 2);

            // Left panel
            _leftPanel.Location = new Float2(0, TopH);
            _leftPanel.Size = new Float2(LeftW, sh - TopH);

            // Search box
            var search = _leftPanel.Children[0] as Panel;
            if (search != null)
            {
                search.Location = new Float2(12, 12);
                search.Size = new Float2(LeftW - 24, 36);
                if (search.Children.Count > 0)
                {
                    search.Children[0].Location = new Float2(12, 0);
                    search.Children[0].Size = new Float2(search.Width - 24, 36);
                }
            }

            // Tabs
            float tabY = 56;
            float tabW = (LeftW - 24 - 6) / 4f;
            for (int i = 0; i < _tabs.Count; i++)
            {
                _tabs[i].Location = new Float2(12 + i * (tabW + 2), tabY);
                _tabs[i].Size = new Float2(tabW, 32);
            }

            // Mail list
            float listY = tabY + 36;
            _listPanel.Location = new Float2(0, listY);
            _listPanel.Size = new Float2(LeftW, sh - TopH - listY);

            float iy = 0;
            int mi = 0;
            var children = _listPanel.Children;
            for (int ci = 0; ci < children.Count; ci++)
            {
                var child = children[ci];
                if (child is ContainerControl hdr && !(child is ClickablePanel))
                {
                    // Section header
                    hdr.Location = new Float2(0, iy);
                    hdr.Size = new Float2(LeftW, 28);
                    foreach (var sub in hdr.Children)
                    {
                        if (sub is Panel p && p.Size == new Float2(6, 6))
                            p.Location = new Float2(10, 11);
                        else if (sub is Label lbl)
                            lbl.Location = new Float2(24, 6);
                    }
                    iy += 28;
                }
                else if (child is ClickablePanel item && mi < _mailItems.Count)
                {
                    item.Location = new Float2(0, iy);
                    item.Size = new Float2(LeftW, 52);

                    float cx = 10;
                    foreach (var sub in item.Children)
                    {
                        if (sub is Panel dot && dot.Size == new Float2(8, 8))
                        {
                            dot.Location = new Float2(cx, 22);
                            cx += 14;
                        }
                        else if (sub is Panel av && av.Size == new Float2(36, 36))
                        {
                            av.Location = new Float2(cx, 8);
                            cx += 44;
                        }
                        else if (sub is Label lbl)
                        {
                            if (mi < _itemSenderLabels.Count && lbl == _itemSenderLabels[mi])
                            {
                                lbl.Location = new Float2(cx, 8);
                                lbl.Size = new Float2(LeftW - cx - 70, 18);
                            }
                            else if (lbl.Text == _mails[mi].Date)
                            {
                                lbl.Size = new Float2(60, 16);
                                lbl.Location = new Float2(LeftW - 10 - lbl.Width, 8);
                            }
                            else if (lbl.Text == _mails[mi].Subject)
                            {
                                lbl.Location = new Float2(cx, 28);
                                lbl.Size = new Float2(LeftW - cx - 30, 16);
                            }
                        }
                    }
                    mi++;
                    iy += 52;
                }
            }

            // Center panel
            float cw = sw - LeftW - RightW;
            _centerPanel.Location = new Float2(LeftW, TopH);
            _centerPanel.Size = new Float2(cw, sh - TopH);

            float cp = 24;
            float cy = 0;

            // Sender header
            _senderHeader.Location = new Float2(0, cy);
            _senderHeader.Size = new Float2(cw, 110);

            _senderHeader.Children[0].Location = new Float2(cp, 20);

            _senderName.Location = new Float2(cp + 68, 24);
            _senderName.Size = new Float2(200, 22);

            _senderTagBtn.Location = new Float2(cp + 68 + 208, 24);
            _senderTagBtn.Size = new Float2(50, 22);

            _senderDate.Location = new Float2(cp + 68, 52);
            _senderDate.Size = new Float2(200, 18);

            _attachHint.Location = new Float2(cp + 270, 52);
            _attachHint.Size = new Float2(120, 18);

            cy += 110;

            // Subject
            _subjectLbl.Location = new Float2(cp, cy);
            _subjectLbl.Size = new Float2(cw - cp * 2, 42);
            cy += 52;

            _subjectTag.Location = new Float2(cp, cy);
            _subjectTag.Size = new Float2(100, 20);
            _subjectTag.Visible = _selectedIdx == 0;
            cy += 30;

            // Letter paper
            float ph = 260;
            _paper.Location = new Float2(cp, cy);
            _paper.Size = new Float2(cw - cp * 2, ph);
            _bodyLbl.Location = new Float2(16, 16);
            _bodyLbl.Size = new Float2(_paper.Width - 32, _paper.Height - 32);
            cy += ph + 16;

            // Reward box
            var mail = _mails[_selectedIdx];
            if (mail.Attachments != null && mail.Attachments.Length > 0)
            {
                _rewardBox.Visible = true;
                float rh = _rewardSlots.Count > 0 ? 40 + ((_rewardSlots.Count + 1) / 2) * 48 : 0;
                _rewardBox.Location = new Float2(cp, cy);
                _rewardBox.Size = new Float2(cw - cp * 2, rh);

                float rx = 16, ry = 12;
                float rw = (_rewardBox.Width - 48) / 2f;
                int col = 0;
                foreach (var slot in _rewardSlots)
                {
                    slot.Location = new Float2(rx, ry);
                    slot.Size = new Float2(rw, 36);
                    foreach (var sub in slot.Children)
                    {
                        if (sub is Label l)
                        {
                            if (l.HorizontalAlignment == TextAlignment.Far)
                            {
                                l.Location = new Float2(rw - 8, 0);
                                l.Size = new Float2(60, 36);
                            }
                            else
                            {
                                l.Location = new Float2(8, 0);
                                l.Size = new Float2(rw - 70, 36);
                            }
                        }
                    }
                    rx += rw + 16;
                    col++;
                    if (col >= 2) { col = 0; rx = 16; ry += 44; }
                }
                cy += rh + 12;
            }
            else
            {
                _rewardBox.Visible = false;
            }

            // Right panel
            _rightPanel.Location = new Float2(sw - RightW, TopH);
            _rightPanel.Size = new Float2(RightW, sh - TopH);

            float pad = 12;
            _rightTitle.Location = new Float2(pad, 12);
            _rightTitle.Size = new Float2(80, 18);

            _rightCount.Location = new Float2(pad + 84, 12);
            _rightCount.Size = new Float2(50, 18);

            float at = 40;
            _attachList.Location = new Float2(0, at);
            _attachList.Size = new Float2(RightW, sh - TopH - at - 210);

            float ay = 4;
            foreach (var slot in _attachSlots)
            {
                slot.Location = new Float2(pad, ay);
                slot.Size = new Float2(RightW - pad * 2, 56);

                int labelIdx = 0;
                foreach (var sub in slot.Children)
                {
                    if (sub is Label l)
                    {
                        string txt = l.Text.ToString();
                        if (labelIdx == 0) // name
                        {
                            l.Location = new Float2(8, 6);
                            l.Size = new Float2(slot.Width - 16, 18);
                        }
                        else if (txt.Contains("\u00D7")) // count
                        {
                            l.Location = new Float2(slot.Width - 60, 30);
                            l.Size = new Float2(50, 18);
                        }
                        else if (txt == "\u2713\u5DF2\u9886\u53D6")
                        {
                            l.Location = new Float2(8, 26);
                            l.Size = new Float2(80, 16);
                        }
                        else // desc
                        {
                            l.Location = new Float2(8, 26);
                            l.Size = new Float2(slot.Width - 70, 16);
                        }
                        labelIdx++;
                    }
                }
                ay += 60;
            }

            float by = sh - TopH - 200;
            _claimAllBtn.Location = new Float2(pad, by);
            _claimAllBtn.Size = new Float2(RightW - pad * 2, 44);

            _remainLbl.Location = new Float2(pad, by + 50);
            _remainLbl.Size = new Float2(RightW - pad * 2, 18);

            float aby = by + 78;
            float abw = (RightW - pad * 2 - 8) / 3f;
            _replyBtn.Location = new Float2(pad, aby);
            _replyBtn.Size = new Float2(abw, 36);

            _forwardBtn.Location = new Float2(pad + abw + 4, aby);
            _forwardBtn.Size = new Float2(abw, 36);

            _deleteBtn.Location = new Float2(pad + (abw + 4) * 2, aby);
            _deleteBtn.Size = new Float2(abw, 36);
        }

        // ───────────── IInkPage ─────────────

        public void RefreshLayout()
        {
            _screenSize = new Float2(Width, Height);
            ApplyLayout();
        }
    }
}
