using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.Ink;
using HundunWorld.Game.UI.StyleSystem;
using System;
using System.Collections.Generic;

namespace HundunWorld.Game.UI.Ink.Pages.Mail
{
    public class MenuMailPage : ContainerControl, IInkPage
    {
        private Float2 _screenSize;

        private Panel _backgroundLayer;
        private Panel _vignette;

        private Panel _sidebar;
        private Label _sidebarLogo;
        private Label _sidebarLogoSub;
        private List<InkButton> _sidebarNavButtons;

        private Panel _mainArea;
        private Panel _topHeader;
        private InkButton _backButton;
        private ContainerControl _playerAvatar;
        private Label _playerNameLabel;
        private Label _playerLevelLabel;
        private Label _playerSectLabel;
        private Label _goldLabel;
        private Label _jadeLabel;

        private List<InkButton> _mailTabs;

        private Panel _mailListSection;
        private List<Panel> _mailItems;

        private Panel _mailDetailSection;
        private InkPaperPanel _letterPanel;
        private Label _senderLabel;
        private Label _titleLabel;
        private Label _dateLabel;
        private Label _contentLabel;

        private Label _attachmentTitle;
        private Label _attachmentCount;
        private List<Panel> _attachmentItems;

        private InkButton _deleteButton;
        private InkButton _claimSelectedButton;
        private InkButton _claimAllButton;

        private const float SidebarWidth = 240f;
        private const float TopHeaderHeight = 60f;
        private const float TabBarHeight = 48f;
        private const float PanelPadding = 20f;

        public event Action<string> NavigationRequested;

        private string[] _mailTitles = { "新手礼包", "任务奖励", "活动通知", "系统公告", "玩家私信" };
        private string[] _mailSenders = { "系统", "任务系统", "活动中心", "管理员", "剑随风" };
        private string[] _mailDates = { "10分钟前", "1小时前", "2小时前", "1天前", "3天前" };
        private bool[] _mailUnread = { true, true, false, false, false };
        private bool[] _mailHasAttachment = { true, true, false, false, false };
        private string[] _mailContents = {
            "欢迎来到混沌世界！为感谢您的加入，特赠新手礼包一份，祝您江湖路一帆风顺。",
            "恭喜您完成「初入江湖」任务，获得丰厚奖励。继续努力，成就大侠之路！",
            "「江湖论剑」活动即将开启，请及时参与，赢取稀有武学秘籍！",
            "本周维护公告：服务器将于本周四凌晨2点进行例行维护，预计时长2小时。",
            "兄弟，有空一起组队刷副本吗？我找到了一个不错的秘境。"
        };

        private string[] _attachmentNames = { "铜钱", "精良装备箱", "经验丹" };
        private string[] _attachmentCounts = { "x 5000", "x 1", "x 3" };
        private Color[] _attachmentColors = { InkWashTheme.QualityCommon, InkWashTheme.QualityUncommon, InkWashTheme.QualityRare };

        public MenuMailPage()
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
                BuildAtmosphere();
                BuildSidebar();
                BuildMainArea();
                BuildTopHeader();
                BuildMailTabs();
                BuildMailList();
                BuildMailDetail();

                ApplyLayout();
                RefreshAllData();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[MenuMailPage] 初始化失败: {ex.Message}");
            }
        }

        private void BuildAtmosphere()
        {
            _backgroundLayer = new Panel
            {
                AnchorPreset = AnchorPresets.StretchAll,
                BackgroundColor = InkWashTheme.BaseDefault,
                Parent = this
            };

            _vignette = new Panel
            {
                AnchorPreset = AnchorPresets.StretchAll,
                BackgroundColor = new Color(InkWashTheme.BaseDefault.R, InkWashTheme.BaseDefault.G, InkWashTheme.BaseDefault.B, 0.4f),
                Parent = this
            };
        }

        private void BuildSidebar()
        {
            _sidebar = new Panel
            {
                BackgroundColor = InkWashTheme.BaseDefault,
                Parent = this
            };

            _sidebarLogo = new Label
            {
                Text = "燕云",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 20f),
                TextColor = InkWashTheme.GoldBright,
                Parent = _sidebar
            };

            _sidebarLogoSub = new Label
            {
                Text = "十六声",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.PaperAged,
                Parent = _sidebar
            };

            _sidebarNavButtons = new List<InkButton>();
            string[] navItems = { "任务", "博物志", "武林录", "营生", "组队", "邮箱", "商店", "门派", "装备", "设置" };
            string[] navPaths = { "menu-quests", "menu-bestiary", "menu-martial-record", "menu-livelihood", "menu-team", "menu-mail", "menu-shop", "menu-sect", "menu-equipment", "settings" };

            for (int i = 0; i < navItems.Length; i++)
            {
                bool isActive = navItems[i] == "邮箱";
                InkButton btn = new InkButton
                {
                    Text = navItems[i],
                    Height = 36,
                    BackgroundColor = isActive ? InkWashTheme.GoldPrimary * 0.1f : Color.Transparent,
                    TextColor = isActive ? InkWashTheme.GoldBright : InkWashTheme.PaperAged,
                    Parent = _sidebar
                };
                btn.Clicked += () => OnNavClicked(navPaths[i]);
                _sidebarNavButtons.Add(btn);
            }
        }

        private void BuildMainArea()
        {
            _mainArea = new Panel
            {
                BackgroundColor = Color.Transparent,
                Parent = this
            };
        }

        private void BuildTopHeader()
        {
            _topHeader = new Panel
            {
                BackgroundColor = InkWashTheme.BaseSecondary,
                Parent = _mainArea
            };

            _backButton = new InkButton
            {
                Text = "返回战场",
                Width = 100,
                Height = 36,
                BackgroundColor = Color.Transparent,
                TextColor = InkWashTheme.GoldBright,
                Parent = _topHeader
            };
            _backButton.Clicked += OnBackToHud;

            _playerAvatar = new ContainerControl
            {
                Size = new Float2(36, 36),
                BackgroundColor = InkWashTheme.GoldPrimary * 0.2f,
                Parent = _topHeader
            };
            Label avatarText = new Label
            {
                Text = "无",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 16f),
                TextColor = InkWashTheme.GoldBright,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.StretchAll,
                Parent = _playerAvatar
            };

            _playerNameLabel = new Label
            {
                Text = "无名侠",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 14f),
                TextColor = InkWashTheme.TextDefault,
                Parent = _topHeader
            };

            _playerLevelLabel = new Label
            {
                Text = "Lv.42",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.GoldBright,
                Parent = _topHeader
            };

            _playerSectLabel = new Label
            {
                Text = "逍遥派",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.PaperAged,
                Parent = _topHeader
            };

            _goldLabel = new Label
            {
                Text = "12,450",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 11f),
                TextColor = InkWashTheme.TextDefault,
                Parent = _topHeader
            };

            _jadeLabel = new Label
            {
                Text = "328",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 11f),
                TextColor = InkWashTheme.TextDefault,
                Parent = _topHeader
            };
        }

        private void BuildMailTabs()
        {
            _mailTabs = new List<InkButton>();
            string[] tabs = { "全部", "系统", "玩家", "活动" };

            for (int i = 0; i < tabs.Length; i++)
            {
                bool isActive = i == 0;
                InkButton tab = new InkButton
                {
                    Text = tabs[i],
                    Height = TabBarHeight,
                    BackgroundColor = Color.Transparent,
                    TextColor = isActive ? InkWashTheme.GoldBright : InkWashTheme.TextSecondary,
                    Parent = _mainArea
                };
                tab.Clicked += () => OnMailTabClicked(tabs[i]);
                _mailTabs.Add(tab);
            }
        }

        private void BuildMailList()
        {
            _mailListSection = new Panel
            {
                BackgroundColor = Color.Transparent,
                Parent = _mainArea
            };

            _mailItems = new List<Panel>();
            for (int i = 0; i < _mailTitles.Length; i++)
            {
                Panel item = new Panel
                {
                    BackgroundColor = i == 0 ? InkWashTheme.GoldPrimary * 0.1f : Color.Transparent,
                    Parent = _mailListSection
                };

                Panel unreadDot = null;
                if (_mailUnread[i])
                {
                    // 未读邮件用 gold-bright 强调（设计方案 §3.13）
                    unreadDot = new Panel
                    {
                        Size = new Float2(8, 8),
                        BackgroundColor = InkWashTheme.GoldBright,
                        Parent = item
                    };
                }

                Label titleLabel = new Label
                {
                    Text = _mailTitles[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 14f),
                    TextColor = _mailUnread[i] ? InkWashTheme.TextDefault : InkWashTheme.PaperAged,
                    Parent = item
                };

                Label senderLabel = new Label
                {
                    Text = _mailSenders[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                    TextColor = InkWashTheme.TextTertiary,
                    Parent = item
                };

                Label dateLabel = new Label
                {
                    Text = _mailDates[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                    TextColor = InkWashTheme.TextTertiary,
                    Parent = item
                };

                _mailItems.Add(item);
            }
        }

        private void BuildMailDetail()
        {
            _mailDetailSection = new Panel
            {
                BackgroundColor = Color.Transparent,
                Parent = _mainArea
            };

            _letterPanel = new InkPaperPanel
            {
                Parent = _mailDetailSection
            };

            _senderLabel = new Label
            {
                Text = "系统",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.TextOnPaper,
                Parent = _letterPanel
            };

            _titleLabel = new Label
            {
                Text = "新手礼包",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 14f),
                TextColor = InkWashTheme.TextOnPaper,
                Parent = _letterPanel
            };

            _dateLabel = new Label
            {
                Text = "10分钟前",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.PaperDark,
                Parent = _letterPanel
            };

            _contentLabel = new Label
            {
                Text = "欢迎来到混沌世界！为感谢您的加入，特赠新手礼包一份，祝您江湖路一帆风顺。",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.TextOnPaper,
                Parent = _letterPanel
            };

            _attachmentTitle = new Label
            {
                Text = "附件",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 14f),
                TextColor = InkWashTheme.TextOnPaper,
                Parent = _letterPanel
            };

            _attachmentCount = new Label
            {
                Text = "共3件",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.PaperDark,
                Parent = _letterPanel
            };

            _attachmentItems = new List<Panel>();
            for (int i = 0; i < _attachmentNames.Length; i++)
            {
                Panel item = new Panel
                {
                    BackgroundColor = InkWashTheme.GoldPrimary * 0.14f,
                    Parent = _letterPanel
                };

                Panel check = new Panel
                {
                    Size = new Float2(16, 16),
                    BackgroundColor = InkWashTheme.GoldPrimary,
                    Parent = item
                };

                Panel iconWrap = new Panel
                {
                    Size = new Float2(36, 36),
                    BackgroundColor = _attachmentColors[i] * 0.12f,
                    Parent = item
                };

                Label nameLabel = new Label
                {
                    Text = _attachmentNames[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                    TextColor = InkWashTheme.TextOnPaper,
                    Parent = item
                };

                Label countLabel = new Label
                {
                    Text = _attachmentCounts[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 11f),
                    TextColor = InkWashTheme.PaperDark,
                    Parent = item
                };

                _attachmentItems.Add(item);
            }

            _deleteButton = new InkButton
            {
                Text = "删除邮件",
                Width = 100,
                Height = 32,
                BackgroundColor = Color.Transparent,
                TextColor = InkWashTheme.PaperDark,
                Parent = _letterPanel
            };
            _deleteButton.Clicked += OnDeleteMailClicked;

            _claimSelectedButton = new InkButton
            {
                Text = "领取选中",
                Width = 100,
                Height = 36,
                BackgroundColor = Color.Transparent,
                TextColor = InkWashTheme.TextOnPaper,
                Parent = _letterPanel
            };
            _claimSelectedButton.Clicked += OnClaimSelectedClicked;

            _claimAllButton = new InkButton
            {
                Text = "领取全部",
                Width = 100,
                Height = 36,
                BackgroundColor = InkWashTheme.GoldBright,
                TextColor = InkWashTheme.TextOnBrand,
                Parent = _letterPanel
            };
            _claimAllButton.Clicked += OnClaimAllClicked;
        }

        private void ApplyLayout()
        {
            float sw = _screenSize.X;
            float sh = _screenSize.Y;

            if (_sidebar != null)
            {
                _sidebar.Location = Float2.Zero;
                _sidebar.Size = new Float2(SidebarWidth, sh);
            }

            float sidebarLogoY = (TopHeaderHeight - 33f) * 0.5f;
            if (_sidebarLogo != null)
            {
                _sidebarLogo.Location = new Float2(20f, sidebarLogoY);
            }
            if (_sidebarLogoSub != null)
            {
                _sidebarLogoSub.Location = new Float2(20f + 22f * 2f + 6f, sidebarLogoY + 11f);
            }

            float navY = TopHeaderHeight + 16f;
            foreach (var btn in _sidebarNavButtons)
            {
                btn.Location = new Float2(12f, navY);
                btn.Size = new Float2(SidebarWidth - 24f, 36f);
                navY += 38f;
            }

            if (_mainArea != null)
            {
                _mainArea.Location = new Float2(SidebarWidth, 0f);
                _mainArea.Size = new Float2(sw - SidebarWidth, sh);
            }

            if (_topHeader != null)
            {
                _topHeader.Location = Float2.Zero;
                _topHeader.Size = new Float2(sw - SidebarWidth, TopHeaderHeight);
            }

            float headerX = 20f;
            float headerCenterY = TopHeaderHeight * 0.5f;
            if (_backButton != null)
            {
                _backButton.Location = new Float2(headerX, headerCenterY - 18f);
            }
            headerX += 110f;
            if (_playerAvatar != null)
            {
                _playerAvatar.Location = new Float2(headerX, headerCenterY - 18f);
            }
            headerX += 46f;
            if (_playerNameLabel != null)
            {
                _playerNameLabel.Location = new Float2(headerX, headerCenterY - 20f);
            }
            if (_playerLevelLabel != null)
            {
                _playerLevelLabel.Location = new Float2(headerX + 120f, headerCenterY - 18f);
            }
            if (_playerSectLabel != null)
            {
                _playerSectLabel.Location = new Float2(headerX + 170f, headerCenterY - 16f);
            }

            float rightX = (sw - SidebarWidth) - 180f;
            if (_goldLabel != null)
            {
                _goldLabel.Location = new Float2(rightX, headerCenterY - 16f);
            }
            rightX += 80f;
            if (_jadeLabel != null)
            {
                _jadeLabel.Location = new Float2(rightX, headerCenterY - 16f);
            }

            float tabX = 0f;
            foreach (var tab in _mailTabs)
            {
                tab.Location = new Float2(tabX, TopHeaderHeight);
                tab.Size = new Float2(80f, TabBarHeight);
                tabX += 80f;
            }

            float contentY = TopHeaderHeight + TabBarHeight;
            float listW = 320f;
            float detailW = (sw - SidebarWidth) - listW - 20f;
            float contentH = sh - contentY - 32f;

            if (_mailListSection != null)
            {
                _mailListSection.Location = new Float2(20f, contentY);
                _mailListSection.Size = new Float2(listW, contentH);
            }

            float mailItemY = 0f;
            foreach (var item in _mailItems)
            {
                item.Location = new Float2(0f, mailItemY);
                item.Size = new Float2(listW, 64f);
                mailItemY += 64f;
            }

            for (int idx = 0; idx < _mailItems.Count; idx++)
            {
                var item = _mailItems[idx];
                float itemX = 16f;
                float itemCenterY = 32f;

                foreach (var child in item.Children)
                {
                    if (child is Panel dot)
                    {
                        child.Location = new Float2(itemX, itemCenterY - 4f);
                        itemX += 16f;
                    }
                    else if (child is Label label)
                    {
                        if (_mailTitles.Contains(label.Text))
                        {
                            child.Location = new Float2(itemX, itemCenterY - 20f);
                            itemX += 180f;
                        }
                        else if (_mailSenders.Contains(label.Text))
                        {
                            child.Location = new Float2(itemX, itemCenterY - 8f);
                            itemX += 80f;
                        }
                        else if (_mailDates.Contains(label.Text))
                        {
                            child.Location = new Float2(listW - 70f, itemCenterY - 8f);
                        }
                    }
                }
            }

            if (_mailDetailSection != null)
            {
                _mailDetailSection.Location = new Float2(listW + 40f, contentY);
                _mailDetailSection.Size = new Float2(detailW, contentH);
            }

            if (_letterPanel != null)
            {
                _letterPanel.Location = Float2.Zero;
                _letterPanel.Size = new Float2(detailW, contentH);
            }

            float letterX = PanelPadding;
            float letterY = PanelPadding;

            if (_senderLabel != null)
            {
                _senderLabel.Location = new Float2(letterX, letterY);
            }
            letterY += 24f;

            if (_titleLabel != null)
            {
                _titleLabel.Location = new Float2(letterX, letterY);
            }
            letterY += 32f;

            if (_dateLabel != null)
            {
                _dateLabel.Location = new Float2(letterX, letterY);
            }
            letterY += 32f;

            if (_contentLabel != null)
            {
                _contentLabel.Location = new Float2(letterX, letterY);
                _contentLabel.Size = new Float2(detailW - PanelPadding * 2f, 80f);
            }
            letterY += 100f;

            if (_attachmentTitle != null)
            {
                _attachmentTitle.Location = new Float2(letterX, letterY);
            }
            if (_attachmentCount != null)
            {
                _attachmentCount.Location = new Float2(letterX + 60f, letterY + 2f);
            }
            letterY += 32f;

            float attachX = letterX;
            float attachW = (detailW - PanelPadding * 2f - 20f) / 3f;
            foreach (var attach in _attachmentItems)
            {
                attach.Location = new Float2(attachX, letterY);
                attach.Size = new Float2(attachW, 56f);
                attachX += attachW + 10f;
            }

            for (int idx = 0; idx < _attachmentItems.Count; idx++)
            {
                var attach = _attachmentItems[idx];
                float attachChildX = 10f;
                float attachCenterY = 28f;

                foreach (var child in attach.Children)
                {
                    if (child is Panel check)
                    {
                        child.Location = new Float2(attachChildX, attachCenterY - 8f);
                        attachChildX += 24f;
                    }
                    else if (child is Panel icon)
                    {
                        child.Location = new Float2(attachChildX, attachCenterY - 18f);
                        attachChildX += 44f;
                    }
                    else if (child is Label label)
                    {
                        if (_attachmentNames.Contains(label.Text))
                        {
                            child.Location = new Float2(attachChildX, attachCenterY - 18f);
                        }
                        else if (_attachmentCounts.Contains(label.Text))
                        {
                            child.Location = new Float2(attachChildX, attachCenterY - 6f);
                        }
                    }
                }
            }

            letterY += 70f;

            if (_deleteButton != null)
            {
                _deleteButton.Location = new Float2(letterX, letterY);
            }

            if (_claimSelectedButton != null)
            {
                _claimSelectedButton.Location = new Float2(detailW - PanelPadding - 210f, letterY);
            }

            if (_claimAllButton != null)
            {
                _claimAllButton.Location = new Float2(detailW - PanelPadding - 100f, letterY);
            }
        }

        private void RefreshAllData()
        {
        }

        private void OnNavClicked(string path)
        {
            NavigationRequested?.Invoke(path);
        }

        private void OnBackToHud()
        {
            NavigationRequested?.Invoke("combat-hud");
        }

        private void OnMailTabClicked(string tab)
        {
            foreach (var t in _mailTabs)
            {
                bool isActive = t.Text == tab;
                t.TextColor = isActive ? InkWashTheme.GoldBright : InkWashTheme.TextSecondary;
                t.BorderColor = isActive ? InkWashTheme.GoldPrimary : Color.Transparent;
            }
        }

        private void OnDeleteMailClicked()
        {
            FlaxEngine.Debug.Log("[MenuMailPage] 删除邮件");
        }

        private void OnClaimSelectedClicked()
        {
            FlaxEngine.Debug.Log("[MenuMailPage] 领取选中");
        }

        private void OnClaimAllClicked()
        {
            FlaxEngine.Debug.Log("[MenuMailPage] 领取全部");
        }

        public void RefreshLayout()
        {
            _screenSize = new Float2(Width, Height);
            ApplyLayout();
        }
    }
}