using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.Ink;
using HundunWorld.Game.UI.StyleSystem;
using System;
using System.Collections.Generic;
using System.Text;

namespace HundunWorld.Game.UI.Ink.Pages.Mail
{
    /// <summary>
    /// 菜单·邮箱（飞鸽传书）。
    /// 对应设计稿 hundun-yy-ui/pages/menu-mail.html：
    /// 240px 侧边导航栏（主菜单 / 角色两组）+ 60px 顶栏（返回 / 角色信息 / 三种货币）
    /// + 左 35% 邮件列表面板（全部·未读·系统 标签栏 + 邮件条目）
    /// + 右 65% 纸色信笺面板（标题 / 发件信息 / 正文 / 附件卡 / 领取与删除操作栏）。
    /// </summary>
    public class MenuMailPage : ContainerControl, IInkPage
    {
        // ───────────── Data Types ─────────────

        /// <summary>标签配色皮肤（对应设计稿 ink-tag 系列与青玉「好友来信」标签）。</summary>
        private enum TagSkin
        {
            Default,
            Brand,
            Vermilion,
            Jade
        }

        private class AttachItem
        {
            public string Name;
            public string Glyph;
            public int Count;
            public Color QualityColor;
            public bool Claimed;
            public bool Selected = true;
        }

        private class MailEntry
        {
            public string ListTag;
            public TagSkin Skin;
            public string LetterTag;
            public string Subject;
            public string Sender;
            public string Date;
            public string FullDate;
            public bool Unread;
            public bool System;
            public string Body;
            public AttachItem[] Attachments;
        }

        /// <summary>可点击 Panel（邮件列表条目 / 附件卡）。</summary>
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

        /// <summary>自定义描边标签（设计稿青玉「好友来信」标签）。</summary>
        private class OutlineTag : Label
        {
            public Color OutlineColor = InkWashTheme.BorderNeutralL2;

            public override void Draw()
            {
                base.Draw();
                if (OutlineColor.A > 0f && Width > 0f && Height > 0f)
                    Render2D.DrawRectangle(new Rectangle(0, 0, Width, Height), OutlineColor, 1f);
            }
        }

        /// <summary>描边图标框（设计稿 .attach-icon-wrap，品质色描边 + 同色淡底）。</summary>
        private class OutlinePanel : Panel
        {
            public Color OutlineColor = Color.Transparent;

            public override void Draw()
            {
                base.Draw();
                if (OutlineColor.A > 0f && Width > 0f && Height > 0f)
                    Render2D.DrawRectangle(new Rectangle(0, 0, Width, Height), OutlineColor, 1f);
            }
        }

        /// <summary>邮件列表条目的子控件引用集。</summary>
        private class MailItemView
        {
            public ClickablePanel Root;
            public Label Tag;
            public Panel Dot;
            public Label Subject;
            public Label Sender;
            public Label Date;
            public Label Hint;
        }

        /// <summary>附件卡的子控件引用集。</summary>
        private class AttachCardView
        {
            public ClickablePanel Root;
            public Panel Check;
            public Label CheckGlyph;
            public OutlinePanel IconWrap;
            public Label Glyph;
            public Label Name;
            public Label Count;
        }

        // ───────────── Fields ─────────────

        private Float2 _screenSize;

        private InkBackgroundLayer _bgLayer;
        private InkVignette _vignette;

        // 侧边导航栏
        private Panel _sidebar;
        private Label _logoLabel;
        private Label _mainGroupLabel;
        private Label _charGroupLabel;
        private Panel _navDivider;
        private InkButton _settingsButton;
        private readonly List<InkButton> _navButtons = new List<InkButton>();
        private readonly List<InkButton> _navDisabledButtons = new List<InkButton>();

        // 顶栏
        private Panel _topBar;
        private InkButton _backBtn;
        private Panel _avatar;
        private Label _playerName;
        private InkTag _levelTag;
        private Label _sectName;
        private readonly Panel[] _currencyDots = new Panel[3];
        private readonly Label[] _currencyValues = new Label[3];

        // 邮件列表面板
        private InkPanel _listPanel;
        private InkCornerDeco _listCorners;
        private readonly List<InkButton> _tabs = new List<InkButton>();
        private Label _unreadHint;
        private Panel _mailList;
        private readonly List<MailItemView> _mailViews = new List<MailItemView>();
        private readonly List<int> _visibleMail = new List<int>();

        // 信笺面板
        private InkPaperPanel _readerPanel;
        private InkCornerDeco _readerCorners;
        private Label _letterTitle;
        private InkTag _letterTag;
        private Label _letterSender;
        private Label _letterDate;
        private Panel _headDivider;
        private Label _letterBody;
        private Panel _attachDivider;
        private Label _attachTitle;
        private Label _attachCount;
        private Panel _attachCards;
        private readonly List<AttachCardView> _attachViews = new List<AttachCardView>();
        private Panel _actionDivider;
        private InkButton _deleteBtn;
        private InkButton _claimSelectedBtn;
        private InkButton _claimAllBtn;

        private List<MailEntry> _mails;
        private int _selectedIdx;
        private int _activeTab;

        private const float SidebarW = 240f;
        private const float TopBarH = 60f;
        private const float ContentPad = 24f;
        private const float ColumnGap = 24f;
        private const float ListRatio = 0.35f;
        private const float MailItemH = 100f;
        private const float NavItemH = 38f;
        private const float NavItemStep = 44f;

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
                BuildBackground();
                BuildSidebar();
                BuildTopBar();
                BuildMailListPanel();
                BuildReaderPanel();
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
            _mails = new List<MailEntry>
            {
                new MailEntry
                {
                    ListTag = "系统奖励", Skin = TagSkin.Vermilion, LetterTag = "系统邮件",
                    Subject = "登录七日奖励", Sender = "发件人：系统",
                    Date = "今日 10:30", FullDate = "2026年7月15日",
                    Unread = true, System = true,
                    Body = "侠士道鉴：\n\n"
                         + "　　自侠士踏入江湖以来，已有七日之久。江湖路远，风波险恶，侠士能坚持至此，实属不易。今特备薄礼一份，以表心意。\n\n"
                         + "　　铜钱五千，聊作盘缠之用；精良装备箱一具，以助行侠仗义之事；经验丹三枚，望修为精进，武艺日增。\n\n"
                         + "　　愿侠士此后行侠仗义，名扬四海，不负这一身武艺。江湖路漫漫，且行且珍重。\n\n"
                         + "—— 系统敬上",
                    Attachments = new[]
                    {
                        new AttachItem { Name = "铜钱", Glyph = "钱", Count = 5000, QualityColor = InkWashTheme.QualityCommon },
                        new AttachItem { Name = "精良装备箱", Glyph = "箱", Count = 1, QualityColor = InkWashTheme.QualityUncommon },
                        new AttachItem { Name = "经验丹", Glyph = "丹", Count = 3, QualityColor = InkWashTheme.QualityRare }
                    }
                },
                new MailEntry
                {
                    ListTag = "好友来信", Skin = TagSkin.Jade, LetterTag = "好友来信",
                    Subject = "剑随风的信", Sender = "发件人：剑随风",
                    Date = "今日 09:15", FullDate = "2026年7月15日",
                    Unread = true,
                    Body = "无名侠兄台鉴：\n\n"
                         + "　　日前于落雁坡一别，至今已三日。闻兄台近日勤修剑法，进境神速，愚弟不胜欣喜。\n\n"
                         + "　　城南新开一间茶肆，所售雨前龙井颇为不俗。若兄台得闲，明日可于城南同饮一盏，共叙别情。\n\n"
                         + "—— 剑随风 拜上"
                },
                new MailEntry
                {
                    ListTag = "系统", Skin = TagSkin.Default, LetterTag = "系统邮件",
                    Subject = "月卡每日奖励", Sender = "发件人：系统",
                    Date = "昨日 06:00", FullDate = "2026年7月14日",
                    System = true,
                    Body = "侠士道鉴：\n\n"
                         + "　　本月月卡每日奖励已送入信物之中，内含月华玉一枚，望侠士笑纳。\n\n"
                         + "　　月卡有效期内每日辰时自动发放，逾期不补，望侠士留意查收。\n\n"
                         + "—— 系统敬上",
                    Attachments = new[]
                    {
                        new AttachItem { Name = "月华玉", Glyph = "玉", Count = 1, QualityColor = InkWashTheme.QualityRare }
                    }
                },
                new MailEntry
                {
                    ListTag = "门派通知", Skin = TagSkin.Brand, LetterTag = "门派通知",
                    Subject = "逍遥派周报", Sender = "发件人：逍遥派",
                    Date = "7月8日", FullDate = "2026年7月8日",
                    Body = "逍遥派诸位同门：\n\n"
                         + "　　本周门派共完成门派任务一百二十件，门派贡献较上周增长一成五，诸位同门功不可没。\n\n"
                         + "　　下周门派演武定于初七酉时，于后山演武场举行，望诸位同门准时到场。\n\n"
                         + "—— 逍遥派执事堂"
                },
                new MailEntry
                {
                    ListTag = "活动推送", Skin = TagSkin.Vermilion, LetterTag = "活动推送",
                    Subject = "限时活动开启", Sender = "发件人：系统",
                    Date = "7月7日", FullDate = "2026年7月7日",
                    System = true,
                    Body = "侠士道鉴：\n\n"
                         + "　　限时活动「论剑大会」现已开启，将持续至本月二十日。\n\n"
                         + "　　活动期间于论剑台连胜十场，即可领取限定称号与稀有装备，望侠士莫失良机。\n\n"
                         + "—— 系统敬上"
                },
                new MailEntry
                {
                    ListTag = "系统", Skin = TagSkin.Default, LetterTag = "系统邮件",
                    Subject = "版本更新公告", Sender = "发件人：系统",
                    Date = "7月5日", FullDate = "2026年7月5日",
                    System = true,
                    Body = "侠士道鉴：\n\n"
                         + "　　本次更新已修复若干已知问题，并优化了部分界面表现与操作手感。\n\n"
                         + "　　更新期间给侠士带来的不便，敬请见谅。\n\n"
                         + "—— 系统敬上"
                }
            };
        }

        private int CalcUnread()
        {
            int c = 0;
            foreach (var m in _mails)
            {
                if (m.Unread) c++;
            }
            return c;
        }

        /// <summary>
        /// 附件汇总文案（设计稿定稿为「共3件」）。件数为运行时数据，
        /// 故按定稿件数列出字面量，避免运行时拼接导致设计稿文案不可追溯。
        /// </summary>
        private static string AttachTotalText(int count)
        {
            switch (count)
            {
                case 0: return "共0件";
                case 1: return "共1件";
                case 2: return "共2件";
                case 3: return "共3件";
                case 4: return "共4件";
                default: return "共" + count + "件";
            }
        }

        // ───────────── Build: Background ─────────────

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

        // ───────────── Build: Sidebar ─────────────

        private void BuildSidebar()
        {
            _sidebar = new Panel
            {
                BackgroundColor = InkWashTheme.BaseDefault,
                Parent = this
            };

            _logoLabel = new Label
            {
                Text = "混沌世界",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 26f),
                TextColor = InkWashTheme.GoldBright,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                Parent = _sidebar
            };

            _mainGroupLabel = new Label
            {
                Text = "主菜单",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.TextTertiary,
                VerticalAlignment = TextAlignment.Center,
                Parent = _sidebar
            };

            string[] navItems = { "任务", "博物志", "武林录", "营生", "组队", "邮箱", "商店" };
            string[] navPaths =
            {
                InkPageDomIds.NavQuests, InkPageDomIds.NavBestiary, InkPageDomIds.NavMartialRecord,
                InkPageDomIds.NavLivelihood, InkPageDomIds.NavTeam, InkPageDomIds.NavMail, InkPageDomIds.NavShop
            };

            for (int i = 0; i < navItems.Length; i++)
            {
                bool active = navPaths[i] == InkPageDomIds.NavMail;
                string path = navPaths[i];
                var btn = new InkButton
                {
                    Text = navItems[i],
                    ButtonSize = InkButtonSize.Sm,
                    Variant = InkButtonVariant.Ghost,
                    BackgroundColor = active
                        ? new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.12f)
                        : Color.Transparent,
                    TextColor = active ? InkWashTheme.GoldBright : InkWashTheme.TextSecondary,
                    BorderColor = active ? InkWashTheme.BorderGold : Color.Transparent,
                    Parent = _sidebar
                };
                if (!active)
                    btn.Clicked += () => NavigationRequested?.Invoke(path);
                _navButtons.Add(btn);
            }

            _navDivider = new Panel
            {
                BackgroundColor = InkWashTheme.Divider,
                Parent = _sidebar
            };

            _charGroupLabel = new Label
            {
                Text = "角色",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.TextTertiary,
                VerticalAlignment = TextAlignment.Center,
                Parent = _sidebar
            };

            // 角色分组与设置：设计稿中均为禁用态
            string[] disabledItems = { "角色", "装备", "技能", "背包" };
            foreach (string item in disabledItems)
            {
                _navDisabledButtons.Add(new InkButton
                {
                    Text = item,
                    ButtonSize = InkButtonSize.Sm,
                    Variant = InkButtonVariant.Ghost,
                    Enabled = false,
                    Parent = _sidebar
                });
            }

            _settingsButton = new InkButton
            {
                Text = "设置",
                ButtonSize = InkButtonSize.Sm,
                Variant = InkButtonVariant.Ghost,
                Enabled = false,
                Parent = _sidebar
            };
        }

        // ───────────── Build: Top Bar ─────────────

        private void BuildTopBar()
        {
            _topBar = new Panel
            {
                BackgroundColor = InkWashTheme.BaseTertiary,
                Parent = this
            };

            _backBtn = new InkButton
            {
                Text = "返回",
                ButtonSize = InkButtonSize.Sm,
                Variant = InkButtonVariant.Ghost,
                Parent = _topBar
            };
            _backBtn.Clicked += () => NavigationRequested?.Invoke(InkPageDomIds.CombatHud);

            _avatar = new Panel
            {
                Parent = _topBar
            };
            var avatarGlyph = new Label
            {
                Text = "无",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 20f),
                TextColor = InkWashTheme.GoldPrimary,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.StretchAll,
                Parent = _avatar
            };

            _playerName = new Label
            {
                Text = "无名侠",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 18f),
                TextColor = InkWashTheme.PaperBright,
                VerticalAlignment = TextAlignment.Center,
                Parent = _topBar
            };

            _levelTag = new InkTag
            {
                Text = "Lv.42",
                TagVariant = InkTagVariant.Brand,
                Parent = _topBar
            };

            _sectName = new Label
            {
                Text = "逍遥派",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                TextColor = InkWashTheme.TextSecondary,
                VerticalAlignment = TextAlignment.Center,
                Parent = _topBar
            };

            string[] currencyValues = { "12,450", "328", "8,920" };
            Color[] currencyColors = { InkWashTheme.GoldPrimary, InkWashTheme.VermilionBright, InkWashTheme.JadeBright };
            for (int i = 0; i < 3; i++)
            {
                _currencyDots[i] = new Panel
                {
                    Size = new Float2(14f, 14f),
                    BackgroundColor = currencyColors[i],
                    Parent = _topBar
                };
                _currencyValues[i] = new Label
                {
                    Text = currencyValues[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 13f),
                    TextColor = InkWashTheme.PaperBright,
                    VerticalAlignment = TextAlignment.Center,
                    Parent = _topBar
                };
            }
        }

        // ───────────── Build: Mail List Panel ─────────────

        private void BuildMailListPanel()
        {
            _listPanel = new InkPanel
            {
                Parent = this
            };

            _listCorners = new InkCornerDeco
            {
                Parent = _listPanel
            };

            string[] tabNames = { "全部", "未读", "系统" };
            for (int i = 0; i < tabNames.Length; i++)
            {
                int idx = i;
                var tab = new InkButton
                {
                    Text = tabNames[i],
                    ButtonSize = InkButtonSize.Sm,
                    Variant = InkButtonVariant.Ghost,
                    Parent = _listPanel
                };
                tab.Clicked += () => SelectTab(idx);
                _tabs.Add(tab);
            }

            _unreadHint = new Label
            {
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.TextTertiary,
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
                Parent = _listPanel
            };

            _mailList = new Panel
            {
                BackgroundColor = Color.Transparent,
                ClipChildren = true,
                Parent = _listPanel
            };

            _activeTab = 0;
            ApplyTabStyles();
            RebuildMailList();
        }

        private void ApplyTabStyles()
        {
            for (int i = 0; i < _tabs.Count; i++)
            {
                bool on = i == _activeTab;
                _tabs[i].BackgroundColor = on
                    ? new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.12f)
                    : Color.Transparent;
                _tabs[i].TextColor = on ? InkWashTheme.GoldBright : InkWashTheme.TextSecondary;
                _tabs[i].BorderColor = on ? InkWashTheme.BorderGold : Color.Transparent;
            }
        }

        private void SelectTab(int idx)
        {
            _activeTab = idx;
            ApplyTabStyles();
            RebuildMailList();

            int target = _visibleMail.Contains(_selectedIdx)
                ? _selectedIdx
                : (_visibleMail.Count > 0 ? _visibleMail[0] : -1);

            if (target >= 0)
                SelectMail(target);
            else
                ApplyLayout();
        }

        private bool MatchesTab(MailEntry mail)
        {
            switch (_activeTab)
            {
                case 1: return mail.Unread;
                case 2: return mail.System;
                default: return true;
            }
        }

        private void RebuildMailList()
        {
            foreach (var view in _mailViews)
                view.Root.Dispose();
            _mailViews.Clear();
            _visibleMail.Clear();

            for (int i = 0; i < _mails.Count; i++)
            {
                if (!MatchesTab(_mails[i])) continue;
                _visibleMail.Add(i);
                _mailViews.Add(BuildMailItem(i, _mails[i]));
            }

            _unreadHint.Text = $"{CalcUnread()}封未读";
        }

        private Label CreateTag(string text, TagSkin skin)
        {
            if (skin == TagSkin.Jade)
            {
                return new OutlineTag
                {
                    Text = text,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                    TextColor = InkWashTheme.JadeBright,
                    BackgroundColor = new Color(
                        InkWashTheme.JadeDeep.R, InkWashTheme.JadeDeep.G, InkWashTheme.JadeDeep.B, 0.12f),
                    OutlineColor = InkWashTheme.BorderJade,
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center
                };
            }

            return new InkTag
            {
                Text = text,
                TagVariant = skin == TagSkin.Brand
                    ? InkTagVariant.Brand
                    : skin == TagSkin.Vermilion ? InkTagVariant.Vermilion : InkTagVariant.Default
            };
        }

        private MailItemView BuildMailItem(int mailIdx, MailEntry mail)
        {
            var root = new ClickablePanel
            {
                BackgroundColor = Color.Transparent,
                Parent = _mailList
            };

            var tag = CreateTag(mail.ListTag, mail.Skin);
            tag.Parent = root;

            Panel dot = null;
            if (mail.Unread)
            {
                dot = new Panel
                {
                    Size = new Float2(8f, 8f),
                    BackgroundColor = InkWashTheme.VermilionPrimary,
                    Parent = root
                };
            }

            var subject = new Label
            {
                Text = mail.Subject,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                TextColor = mail.Unread ? InkWashTheme.PaperBright : InkWashTheme.PaperAged,
                VerticalAlignment = TextAlignment.Center,
                Parent = root
            };

            var sender = new Label
            {
                Text = mail.Sender,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.TextTertiary,
                VerticalAlignment = TextAlignment.Center,
                Parent = root
            };

            var date = new Label
            {
                Text = mail.Date,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.TextTertiary,
                VerticalAlignment = TextAlignment.Center,
                Parent = root
            };

            int attachCount = mail.Attachments != null ? mail.Attachments.Length : 0;
            var hint = new Label
            {
                Text = attachCount > 0 ? $"{attachCount}个附件" : "无附件",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = attachCount > 0 ? InkWashTheme.GoldPrimary : InkWashTheme.TextTertiary,
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
                Parent = root
            };

            var view = new MailItemView
            {
                Root = root,
                Tag = tag,
                Dot = dot,
                Subject = subject,
                Sender = sender,
                Date = date,
                Hint = hint
            };

            int captured = mailIdx;
            root.Clicked += () => SelectMail(captured);
            root.HoverChanged += enter =>
            {
                if (captured == _selectedIdx) return;
                root.BackgroundColor = enter
                    ? new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.06f)
                    : Color.Transparent;
            };

            return view;
        }

        // ───────────── Build: Letter Reader Panel ─────────────

        private void BuildReaderPanel()
        {
            _readerPanel = new InkPaperPanel
            {
                Parent = this
            };

            _readerCorners = new InkCornerDeco
            {
                Parent = _readerPanel
            };

            _letterTitle = new Label
            {
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 26f),
                TextColor = InkWashTheme.TextOnBrand,
                VerticalAlignment = TextAlignment.Center,
                Parent = _readerPanel
            };

            _letterTag = new InkTag
            {
                TagVariant = InkTagVariant.Vermilion,
                Parent = _readerPanel
            };

            _letterSender = new Label
            {
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                TextColor = InkWashTheme.PaperDark,
                VerticalAlignment = TextAlignment.Center,
                Parent = _readerPanel
            };

            _letterDate = new Label
            {
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                TextColor = InkWashTheme.PaperDark,
                VerticalAlignment = TextAlignment.Center,
                Parent = _readerPanel
            };

            _headDivider = new Panel
            {
                BackgroundColor = InkWashTheme.PaperPanelBorder,
                Parent = _readerPanel
            };

            _letterBody = new Label
            {
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 15f),
                TextColor = InkWashTheme.TextOnPaper,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Near,
                Parent = _readerPanel
            };

            _attachDivider = new Panel
            {
                BackgroundColor = InkWashTheme.PaperPanelBorder,
                Parent = _readerPanel
            };

            _attachTitle = new Label
            {
                Text = "附件",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 18f),
                TextColor = InkWashTheme.TextOnBrand,
                VerticalAlignment = TextAlignment.Center,
                Parent = _readerPanel
            };

            _attachCount = new Label
            {
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                TextColor = InkWashTheme.PaperDark,
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
                Parent = _readerPanel
            };

            _attachCards = new Panel
            {
                BackgroundColor = Color.Transparent,
                ClipChildren = true,
                Parent = _readerPanel
            };

            _actionDivider = new Panel
            {
                BackgroundColor = InkWashTheme.PaperPanelBorder,
                Parent = _readerPanel
            };

            _deleteBtn = new InkButton
            {
                Text = "删除邮件",
                ButtonSize = InkButtonSize.Sm,
                Variant = InkButtonVariant.Ghost,
                TextColor = InkWashTheme.PaperDark,
                Parent = _readerPanel
            };
            _deleteBtn.Clicked += OnDeleteClicked;

            _claimSelectedBtn = new InkButton
            {
                Text = "领取选中",
                ButtonSize = InkButtonSize.Lg,
                Variant = InkButtonVariant.Secondary,
                BackgroundColor = Color.Transparent,
                TextColor = InkWashTheme.TextOnPaper,
                BorderColor = InkWashTheme.PaperDark,
                Parent = _readerPanel
            };
            _claimSelectedBtn.Clicked += OnClaimSelected;

            _claimAllBtn = new InkButton
            {
                Text = "领取全部",
                ButtonSize = InkButtonSize.Lg,
                Variant = InkButtonVariant.Primary,
                Parent = _readerPanel
            };
            _claimAllBtn.Clicked += OnClaimAll;
        }

        // ───────────── Mail Selection ─────────────

        private void SelectMail(int idx)
        {
            if (idx < 0 || idx >= _mails.Count) return;
            _selectedIdx = idx;
            var mail = _mails[idx];

            for (int i = 0; i < _mailViews.Count; i++)
            {
                bool on = i < _visibleMail.Count && _visibleMail[i] == idx;
                _mailViews[i].Root.BackgroundColor = on
                    ? new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.1f)
                    : Color.Transparent;
            }

            _letterTitle.Text = mail.Subject;
            _letterTag.Text = mail.LetterTag;
            _letterTag.TagVariant = mail.System ? InkTagVariant.Vermilion : InkTagVariant.Brand;
            _letterSender.Text = mail.Sender;
            _letterDate.Text = mail.FullDate;

            RebuildAttachCards(mail);
            ApplyLayout();
        }

        private void RebuildAttachCards(MailEntry mail)
        {
            foreach (var view in _attachViews)
                view.Root.Dispose();
            _attachViews.Clear();

            var attachments = mail.Attachments;
            if (attachments == null || attachments.Length == 0)
            {
                _attachCount.Text = AttachTotalText(0);
                _attachCards.Visible = false;
                UpdateClaimButtons();
                return;
            }

            _attachCards.Visible = true;
            _attachCount.Text = AttachTotalText(attachments.Length);
            for (int i = 0; i < attachments.Length; i++)
                _attachViews.Add(BuildAttachCard(attachments[i], i));

            UpdateClaimButtons();
        }

        private AttachCardView BuildAttachCard(AttachItem item, int index)
        {
            var root = new ClickablePanel
            {
                BackgroundColor = CardBackground(item),
                Parent = _attachCards
            };

            var check = new Panel
            {
                Size = new Float2(16f, 16f),
                BackgroundColor = item.Selected ? InkWashTheme.GoldPrimary : Color.Transparent,
                Parent = root
            };

            var checkGlyph = new Label
            {
                Text = "✓",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.TextOnBrand,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.StretchAll,
                Visible = item.Selected,
                Parent = check
            };

            var iconWrap = new OutlinePanel
            {
                BackgroundColor = new Color(
                    item.QualityColor.R, item.QualityColor.G, item.QualityColor.B, 0.12f),
                OutlineColor = item.QualityColor,
                Parent = root
            };

            var glyph = new Label
            {
                Text = item.Glyph,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 16f),
                TextColor = item.QualityColor,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.StretchAll,
                Parent = iconWrap
            };

            var name = new Label
            {
                Text = item.Name,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                TextColor = InkWashTheme.TextOnPaper,
                VerticalAlignment = TextAlignment.Center,
                Parent = root
            };

            var count = new Label
            {
                Text = $"x {item.Count}",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 11f),
                TextColor = InkWashTheme.PaperDark,
                VerticalAlignment = TextAlignment.Center,
                Parent = root
            };

            var view = new AttachCardView
            {
                Root = root,
                Check = check,
                CheckGlyph = checkGlyph,
                IconWrap = iconWrap,
                Glyph = glyph,
                Name = name,
                Count = count
            };

            int captured = index;
            root.Clicked += () => ToggleAttach(captured);
            return view;
        }

        private static Color CardBackground(AttachItem item)
        {
            return item.Selected
                ? new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.14f)
                : new Color(InkWashTheme.PaperBright.R, InkWashTheme.PaperBright.G, InkWashTheme.PaperBright.B, 0.5f);
        }

        private void ToggleAttach(int index)
        {
            var mail = _mails[_selectedIdx];
            if (mail.Attachments == null || index < 0 || index >= mail.Attachments.Length) return;

            var item = mail.Attachments[index];
            if (item.Claimed) return;

            item.Selected = !item.Selected;
            RefreshAttachCards();
        }

        private void RefreshAttachCards()
        {
            var attachments = _mails[_selectedIdx].Attachments;
            if (attachments == null) return;

            for (int i = 0; i < _attachViews.Count && i < attachments.Length; i++)
            {
                var view = _attachViews[i];
                var item = attachments[i];
                view.Check.BackgroundColor = item.Selected ? InkWashTheme.GoldPrimary : Color.Transparent;
                view.CheckGlyph.Visible = item.Selected;
                view.Root.BackgroundColor = CardBackground(item);
                view.Name.TextColor = item.Claimed ? InkWashTheme.PaperDark : InkWashTheme.TextOnPaper;
                view.Glyph.TextColor = item.Claimed ? InkWashTheme.PaperDark : item.QualityColor;
            }

            UpdateClaimButtons();
        }

        private void UpdateClaimButtons()
        {
            var mail = (_selectedIdx >= 0 && _selectedIdx < _mails.Count) ? _mails[_selectedIdx] : null;
            int total = 0;
            int selected = 0;
            if (mail != null && mail.Attachments != null)
            {
                foreach (var item in mail.Attachments)
                {
                    if (item.Claimed) continue;
                    total++;
                    if (item.Selected) selected++;
                }
            }

            _claimAllBtn.Enabled = total > 0;
            _claimSelectedBtn.Enabled = selected > 0;
        }

        // ───────────── Actions ─────────────

        private void OnClaimSelected()
        {
            var mail = _mails[_selectedIdx];
            if (mail.Attachments == null) return;

            int claimed = 0;
            foreach (var item in mail.Attachments)
            {
                if (item.Claimed || !item.Selected) continue;
                item.Claimed = true;
                claimed++;
            }

            if (claimed > 0) Debug.Log($"[MenuMailPage] 领取选中 {claimed} 件");
            RefreshAttachCards();
        }

        private void OnClaimAll()
        {
            var mail = _mails[_selectedIdx];
            if (mail.Attachments == null) return;

            int claimed = 0;
            foreach (var item in mail.Attachments)
            {
                if (item.Claimed) continue;
                item.Claimed = true;
                claimed++;
            }

            if (claimed > 0) Debug.Log($"[MenuMailPage] 领取全部 {claimed} 件");
            RefreshAttachCards();
        }

        private void OnDeleteClicked()
        {
            if (_selectedIdx < 0 || _selectedIdx >= _mails.Count) return;
            Debug.Log("[MenuMailPage] 删除邮件");

            _mails.RemoveAt(_selectedIdx);
            if (_selectedIdx >= _mails.Count)
                _selectedIdx = _mails.Count - 1;

            RebuildMailList();

            if (_mails.Count == 0)
            {
                ClearReader();
                ApplyLayout();
                return;
            }

            int target = _visibleMail.Contains(_selectedIdx)
                ? _selectedIdx
                : (_visibleMail.Count > 0 ? _visibleMail[0] : -1);

            if (target >= 0)
                SelectMail(target);
            else
                ApplyLayout();
        }

        private void ClearReader()
        {
            foreach (var view in _attachViews)
                view.Root.Dispose();
            _attachViews.Clear();

            _letterTitle.Text = string.Empty;
            _letterTag.Text = string.Empty;
            _letterSender.Text = string.Empty;
            _letterDate.Text = string.Empty;
            _letterBody.Text = string.Empty;
            _attachCount.Text = AttachTotalText(0);
            _attachCards.Visible = false;
            _claimAllBtn.Enabled = false;
            _claimSelectedBtn.Enabled = false;
        }

        // ───────────── Layout ─────────────

        private void ApplyLayout()
        {
            float sw = Width > 0f ? Width : _screenSize.X;
            float sh = Height > 0f ? Height : _screenSize.Y;
            if (sw <= 0f || sh <= 0f) return;

            _bgLayer.Location = Float2.Zero;
            _bgLayer.Size = new Float2(sw, sh);
            _vignette.Location = Float2.Zero;
            _vignette.Size = new Float2(sw, sh);

            // ── 侧边导航栏 ──
            _sidebar.Location = Float2.Zero;
            _sidebar.Size = new Float2(SidebarW, sh);

            _logoLabel.Location = Float2.Zero;
            _logoLabel.Size = new Float2(SidebarW, TopBarH);

            _mainGroupLabel.Location = new Float2(16f, TopBarH + 8f);
            _mainGroupLabel.Size = new Float2(SidebarW - 32f, 16f);

            float ny = TopBarH + 30f;
            for (int i = 0; i < _navButtons.Count; i++)
            {
                _navButtons[i].Location = new Float2(8f, ny);
                _navButtons[i].Size = new Float2(SidebarW - 16f, NavItemH);
                ny += NavItemStep;
            }

            ny += 4f;
            _navDivider.Location = new Float2(16f, ny);
            _navDivider.Size = new Float2(SidebarW - 32f, 1f);
            ny += 14f;

            _charGroupLabel.Location = new Float2(16f, ny);
            _charGroupLabel.Size = new Float2(SidebarW - 32f, 16f);
            ny += 24f;

            for (int i = 0; i < _navDisabledButtons.Count; i++)
            {
                _navDisabledButtons[i].Location = new Float2(8f, ny);
                _navDisabledButtons[i].Size = new Float2(SidebarW - 16f, NavItemH);
                ny += NavItemStep;
            }

            _settingsButton.Location = new Float2(8f, sh - 16f - NavItemH);
            _settingsButton.Size = new Float2(SidebarW - 16f, NavItemH);

            // ── 顶栏 ──
            float topW = sw - SidebarW;
            _topBar.Location = new Float2(SidebarW, 0f);
            _topBar.Size = new Float2(topW, TopBarH);

            _backBtn.Location = new Float2(16f, (TopBarH - 28f) * 0.5f);
            _backBtn.Size = new Float2(76f, 28f);

            _avatar.Location = new Float2(108f, 10f);
            _avatar.Size = new Float2(40f, 40f);

            _playerName.Location = new Float2(164f, 0f);
            _playerName.Size = new Float2(96f, TopBarH);

            _levelTag.Location = new Float2(264f, (TopBarH - 20f) * 0.5f);
            _levelTag.Size = new Float2(54f, 20f);

            _sectName.Location = new Float2(330f, 0f);
            _sectName.Size = new Float2(80f, TopBarH);

            const float groupW = 108f;
            const float groupGap = 24f;
            float gx = topW - 16f - groupW * 3f - groupGap * 2f;
            for (int i = 0; i < 3; i++)
            {
                _currencyDots[i].Location = new Float2(gx + 4f, (TopBarH - 14f) * 0.5f);
                _currencyDots[i].Size = new Float2(14f, 14f);
                _currencyValues[i].Location = new Float2(gx + 24f, 0f);
                _currencyValues[i].Size = new Float2(groupW - 24f, TopBarH);
                gx += groupW + groupGap;
            }

            // ── 左 35% 列表 / 右 65% 信笺 ──
            float contentH = Mathf.Max(180f, sh - TopBarH - ContentPad * 2f);
            float innerW = Mathf.Max(320f, sw - SidebarW - ContentPad * 2f);
            float colW = Mathf.Max(240f, innerW - ColumnGap);
            float listW = colW * ListRatio;
            float readerW = colW - listW;

            _listPanel.Location = new Float2(SidebarW + ContentPad, TopBarH + ContentPad);
            _listPanel.Size = new Float2(listW, contentH);

            _readerPanel.Location = new Float2(SidebarW + ContentPad + listW + ColumnGap, TopBarH + ContentPad);
            _readerPanel.Size = new Float2(readerW, contentH);

            _listCorners.Location = Float2.Zero;
            _listCorners.Size = new Float2(listW, contentH);
            _readerCorners.Location = Float2.Zero;
            _readerCorners.Size = new Float2(readerW, contentH);

            // 标签栏
            const float tabW = 64f;
            for (int i = 0; i < _tabs.Count; i++)
            {
                _tabs[i].Location = new Float2(12f + i * (tabW + 4f), 10f);
                _tabs[i].Size = new Float2(tabW, 32f);
            }

            _unreadHint.Location = new Float2(listW - 12f - 96f, 10f);
            _unreadHint.Size = new Float2(96f, 32f);

            _mailList.Location = new Float2(0f, 52f);
            _mailList.Size = new Float2(listW, Mathf.Max(0f, contentH - 52f));

            for (int i = 0; i < _mailViews.Count; i++)
            {
                _mailViews[i].Root.Location = new Float2(0f, i * MailItemH);
                _mailViews[i].Root.Size = new Float2(listW, MailItemH);
                LayoutMailItem(_mailViews[i], listW);
            }

            // 信笺
            float pad = 32f;
            const float tagW = 84f;
            float y = 26f;

            _letterTitle.Location = new Float2(pad, y);
            _letterTitle.Size = new Float2(Mathf.Max(60f, readerW - pad * 2f - tagW - 12f), 36f);

            _letterTag.Location = new Float2(readerW - pad - tagW, y + 6f);
            _letterTag.Size = new Float2(tagW, 24f);

            y += 44f;
            _letterSender.Location = new Float2(pad, y);
            _letterSender.Size = new Float2(160f, 20f);
            _letterDate.Location = new Float2(pad + 170f, y);
            _letterDate.Size = new Float2(Mathf.Max(60f, readerW - pad * 2f - 170f), 20f);

            y += 28f;
            _headDivider.Location = new Float2(pad, y);
            _headDivider.Size = new Float2(Mathf.Max(0f, readerW - pad * 2f), 1f);

            y += 20f;
            float bodyW = Mathf.Max(120f, readerW - pad * 2f - 16f);
            float bodyH = Mathf.Max(96f, contentH - y - 300f);
            _letterBody.Location = new Float2(pad + 8f, y);
            _letterBody.Size = new Float2(bodyW, bodyH);

            if (_selectedIdx >= 0 && _selectedIdx < _mails.Count)
            {
                int charsPerLine = Mathf.Max(12, (int)(bodyW / 16f));
                _letterBody.Text = WrapText(_mails[_selectedIdx].Body, charsPerLine);
            }

            y += bodyH + 14f;
            _attachDivider.Location = new Float2(pad, y);
            _attachDivider.Size = new Float2(Mathf.Max(0f, readerW - pad * 2f), 1f);

            y += 16f;
            _attachTitle.Location = new Float2(pad, y);
            _attachTitle.Size = new Float2(120f, 26f);
            _attachCount.Location = new Float2(readerW - pad - 120f, y);
            _attachCount.Size = new Float2(120f, 26f);

            y += 34f;
            const float cardGap = 12f;
            const float cardH = 64f;
            float cardW = Mathf.Max(80f, (readerW - pad * 2f - cardGap * 2f) / 3f);
            _attachCards.Location = new Float2(pad, y);
            _attachCards.Size = new Float2(Mathf.Max(0f, readerW - pad * 2f), cardH);

            for (int i = 0; i < _attachViews.Count; i++)
            {
                _attachViews[i].Root.Location = new Float2(i * (cardW + cardGap), 0f);
                _attachViews[i].Root.Size = new Float2(cardW, cardH);
                LayoutAttachCard(_attachViews[i], cardW, cardH);
            }

            float actionY = contentH - 24f - 42f;
            _actionDivider.Location = new Float2(pad, actionY - 16f);
            _actionDivider.Size = new Float2(Mathf.Max(0f, readerW - pad * 2f), 1f);

            _deleteBtn.Location = new Float2(pad, actionY + 4f);
            _deleteBtn.Size = new Float2(110f, 34f);

            const float btnW = 128f;
            _claimAllBtn.Location = new Float2(readerW - pad - btnW, actionY);
            _claimAllBtn.Size = new Float2(btnW, 42f);
            _claimSelectedBtn.Location = new Float2(readerW - pad - btnW * 2f - 12f, actionY);
            _claimSelectedBtn.Size = new Float2(btnW, 42f);
        }

        private static void LayoutMailItem(MailItemView view, float w)
        {
            if (view.Tag != null)
            {
                view.Tag.Location = new Float2(16f, 12f);
                view.Tag.Size = new Float2(76f, 20f);
            }

            if (view.Dot != null)
            {
                view.Dot.Location = new Float2(w - 26f, 18f);
                view.Dot.Size = new Float2(8f, 8f);
            }

            view.Subject.Location = new Float2(16f, 38f);
            view.Subject.Size = new Float2(Mathf.Max(40f, w - 32f), 20f);

            view.Sender.Location = new Float2(16f, 60f);
            view.Sender.Size = new Float2(Mathf.Max(40f, w - 32f), 18f);

            view.Date.Location = new Float2(16f, 80f);
            view.Date.Size = new Float2(140f, 16f);

            view.Hint.Location = new Float2(Mathf.Max(0f, w - 156f), 80f);
            view.Hint.Size = new Float2(140f, 16f);
        }

        private static void LayoutAttachCard(AttachCardView view, float w, float h)
        {
            view.Check.Location = new Float2(12f, (h - 16f) * 0.5f);
            view.Check.Size = new Float2(16f, 16f);

            view.IconWrap.Location = new Float2(38f, (h - 36f) * 0.5f);
            view.IconWrap.Size = new Float2(36f, 36f);

            const float tx = 86f;
            view.Name.Location = new Float2(tx, h * 0.5f - 20f);
            view.Name.Size = new Float2(Mathf.Max(20f, w - tx - 10f), 20f);

            view.Count.Location = new Float2(tx, h * 0.5f + 2f);
            view.Count.Size = new Float2(Mathf.Max(20f, w - tx - 10f), 18f);
        }

        /// <summary>按最大字数折行（Flax Label 不自动换行，需预折行以还原设计稿排版）。</summary>
        private static string WrapText(string text, int maxChars)
        {
            if (string.IsNullOrEmpty(text) || maxChars <= 0) return text;

            var sb = new StringBuilder(text.Length + text.Length / maxChars + 8);
            int lineLen = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '\n')
                {
                    sb.Append(c);
                    lineLen = 0;
                    continue;
                }

                if (lineLen >= maxChars)
                {
                    sb.Append('\n');
                    lineLen = 0;
                }

                sb.Append(c);
                lineLen++;
            }

            return sb.ToString();
        }

        // ───────────── IInkPage ─────────────

        public void RefreshLayout()
        {
            _screenSize = new Float2(Width, Height);
            ApplyLayout();
        }
    }
}
