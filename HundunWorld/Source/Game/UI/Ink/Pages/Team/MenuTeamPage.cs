using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.Ink;
using HundunWorld.Game.UI.StyleSystem;
using System;
using System.Collections.Generic;

namespace HundunWorld.Game.UI.Ink.Pages.Team
{
    public class MenuTeamPage : ContainerControl, IInkPage
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

        private Label _pageTitle;

        private Panel _memberSection;
        private Panel _teamHeader;
        private Label _teamNameLabel;
        private Label _teamCountLabel;
        private Label _teamIdLabel;
        private Panel _memberList;
        private List<Panel> _memberCards;
        private List<InkButton> _emptySlots;

        private Panel _actionSection;
        private InkPanel _invitePanel;
        private Label _inviteTitle;
        private TextBox _searchInput;
        private InkButton _searchButton;
        private Panel _friendList;
        private List<Panel> _friendRows;

        private InkPanel _settingsPanel;
        private Label _settingsTitle;
        private List<InkButton> _teamTypeTabs;
        private InkButton _autoAcceptToggle;
        private Panel _minLevelSlider;
        private InkButton _minLevelSliderThumb;
        private Label _minLevelValueLabel;
        private List<InkButton> _lootTabs;

        private InkButton _leaveTeamButton;

        private const float SidebarWidth = 240f;
        private const float TopHeaderHeight = 60f;
        private const float PanelGap = 20f;
        private const float PanelPadding = 20f;
        private const float MemberSectionRatio = 0.55f;
        private const float ActionSectionRatio = 0.45f;

        public event Action<string> NavigationRequested;

        private string[] _teamMemberNames = { "无名侠", "剑随风", "花满楼" };
        private string[] _teamMemberAvatars = { "无", "剑", "花" };
        private string[] _teamMemberLevels = { "Lv.42", "Lv.38", "Lv.35" };
        private string[] _teamMemberSects = { "逍遥派", "少林寺", "峨眉派" };
        private bool[] _teamMemberOnline = { true, true, false };
        private bool[] _teamMemberIsLeader = { true, false, false };

        private string[] _friendNames = { "叶知秋", "柳如烟", "萧无涯" };
        private string[] _friendAvatars = { "叶", "柳", "萧" };
        private string[] _friendLevels = { "Lv.40", "Lv.37", "Lv.36" };
        private string[] _friendSects = { "丐帮", "唐门", "武当" };

        private bool _autoAcceptEnabled = true;
        private int _minLevelValue = 30;

        public MenuTeamPage()
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
                BuildPageTitle();
                BuildMemberSection();
                BuildActionSection();

                ApplyLayout();
                RefreshAllData();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[MenuTeamPage] 初始化失败: {ex.Message}");
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
                BackgroundColor = new Color(0.05f, 0.05f, 0.08f, 0.4f),
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
            string[] navItems = { "任务", "博物志", "武林录", "营生", "组队", "邮箱", "商店", "角色", "装备", "设置" };
            string[] navPaths = { "menu-quests", "menu-bestiary", "menu-martial-record", "menu-livelihood", "menu-team", "menu-mail", "menu-shop", "menu-personal-info", "menu-equipment", "settings" };

            for (int i = 0; i < navItems.Length; i++)
            {
                bool isActive = navItems[i] == "组队";
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

            _playerAvatar = new InkAvatar
            {
                Size = new Float2(36, 36),
                Parent = _topHeader
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
                TextColor = InkWashTheme.VermilionPrimary,
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

        private void BuildPageTitle()
        {
            _pageTitle = new Label
            {
                Text = "组队管理",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 14f),
                TextColor = InkWashTheme.GoldBright,
                Parent = _mainArea
            };
        }

        private void BuildMemberSection()
        {
            _memberSection = new Panel
            {
                BackgroundColor = Color.Transparent,
                Parent = _mainArea
            };

            _teamHeader = new Panel
            {
                BackgroundColor = Color.Transparent,
                Parent = _memberSection
            };

            _teamNameLabel = new Label
            {
                Text = "逍遥小队",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 14f),
                TextColor = InkWashTheme.TextDefault,
                Parent = _teamHeader
            };

            _teamCountLabel = new Label
            {
                Text = "3/5",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.GoldBright,
                Parent = _teamHeader
            };

            _teamIdLabel = new Label
            {
                Text = "编号 #2048",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.TextTertiary,
                Parent = _teamHeader
            };

            _memberList = new Panel
            {
                BackgroundColor = Color.Transparent,
                Parent = _memberSection
            };

            _memberCards = new List<Panel>();
            for (int i = 0; i < _teamMemberNames.Length; i++)
            {
                Panel card = new Panel
                {
                    BackgroundColor = InkWashTheme.BaseSecondary,
                    Parent = _memberList
                };

                ContainerControl avatar = new ContainerControl
                {
                    Size = new Float2(44, 44),
                    BackgroundColor = _teamMemberOnline[i] ? InkWashTheme.BaseTertiary : InkWashTheme.BaseElevated,
                    Parent = card
                };
                Label avatarLabel = new Label
                {
                    Text = _teamMemberAvatars[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 18f),
                    TextColor = InkWashTheme.PaperBright,
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                    AnchorPreset = AnchorPresets.StretchAll,
                    Parent = avatar
                };

                Label nameLabel = new Label
                {
                    Text = _teamMemberNames[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 14f),
                    TextColor = _teamMemberOnline[i] ? InkWashTheme.TextDefault : InkWashTheme.PaperAged,
                    Parent = card
                };

                Label leaderLabel = null;
                if (_teamMemberIsLeader[i])
                {
                    leaderLabel = new Label
                    {
                        Text = "队长",
                        Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                        TextColor = InkWashTheme.VermilionPrimary,
                        Parent = card
                    };
                }

                Label levelLabel = new Label
                {
                    Text = _teamMemberLevels[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 11f),
                    TextColor = InkWashTheme.PaperAged,
                    Parent = card
                };

                Label sectLabel = new Label
                {
                    Text = _teamMemberSects[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                    TextColor = InkWashTheme.PaperAged,
                    Parent = card
                };

                Panel statusDot = new Panel
                {
                    Size = new Float2(8, 8),
                    BackgroundColor = _teamMemberOnline[i] ? InkWashTheme.JadePrimary : InkWashTheme.TextTertiary,
                    Parent = card
                };

                Label statusLabel = new Label
                {
                    Text = _teamMemberOnline[i] ? "在线" : "离线",
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                    TextColor = _teamMemberOnline[i] ? InkWashTheme.JadeBright : InkWashTheme.TextTertiary,
                    Parent = card
                };

                _memberCards.Add(card);
            }

            _emptySlots = new List<InkButton>();
            for (int i = 0; i < 2; i++)
            {
                InkButton slot = new InkButton
                {
                    Text = "邀请加入",
                    Height = 48,
                    BackgroundColor = Color.Transparent,
                    TextColor = InkWashTheme.TextTertiary,
                    Parent = _memberList
                };
                slot.Clicked += () => OnInviteSlotClicked();
                _emptySlots.Add(slot);
            }
        }

        private void BuildActionSection()
        {
            _actionSection = new Panel
            {
                BackgroundColor = Color.Transparent,
                Parent = _mainArea
            };

            _invitePanel = new InkPanel
            {
                Parent = _actionSection
            };

            _inviteTitle = new Label
            {
                Text = "邀请入队",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 14f),
                TextColor = InkWashTheme.TextBrand,
                Parent = _invitePanel
            };

            _searchInput = new TextBox
            {
                BackgroundColor = InkWashTheme.BaseDefault,
                TextColor = InkWashTheme.TextDefault,
                Height = 36,
                Parent = _invitePanel
            };

            _searchButton = new InkButton
            {
                Text = "搜索",
                Width = 60,
                Height = 36,
                BackgroundColor = InkWashTheme.VermilionPrimary,
                TextColor = InkWashTheme.PaperBright,
                Parent = _invitePanel
            };
            _searchButton.Clicked += OnSearchClicked;

            _friendList = new Panel
            {
                BackgroundColor = Color.Transparent,
                Parent = _invitePanel
            };

            _friendRows = new List<Panel>();
            for (int i = 0; i < _friendNames.Length; i++)
            {
                Panel row = new Panel
                {
                    BackgroundColor = InkWashTheme.BaseDefault,
                    Parent = _friendList
                };

                ContainerControl avatar = new ContainerControl
                {
                    Size = new Float2(32, 32),
                    BackgroundColor = InkWashTheme.BaseTertiary,
                    Parent = row
                };
                Label avatarLabel = new Label
                {
                    Text = _friendAvatars[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 14f),
                    TextColor = InkWashTheme.PaperBright,
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                    AnchorPreset = AnchorPresets.StretchAll,
                    Parent = avatar
                };

                Label nameLabel = new Label
                {
                    Text = _friendNames[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                    TextColor = InkWashTheme.TextDefault,
                    Parent = row
                };

                Label levelLabel = new Label
                {
                    Text = _friendLevels[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 11f),
                    TextColor = InkWashTheme.PaperAged,
                    Parent = row
                };

                Label sectLabel = new Label
                {
                    Text = _friendSects[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                    TextColor = InkWashTheme.PaperAged,
                    Parent = row
                };

                InkButton inviteBtn = new InkButton
                {
                    Text = "邀请",
                    Width = 50,
                    Height = 28,
                    BackgroundColor = Color.Transparent,
                    TextColor = InkWashTheme.GoldBright,
                    Parent = row
                };
                inviteBtn.Clicked += () => OnInviteFriendClicked(_friendNames[i]);

                _friendRows.Add(row);
            }

            _settingsPanel = new InkPanel
            {
                Parent = _actionSection
            };

            _settingsTitle = new Label
            {
                Text = "队伍设置",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 14f),
                TextColor = InkWashTheme.TextBrand,
                Parent = _settingsPanel
            };

            _teamTypeTabs = new List<InkButton>();
            string[] teamTypes = { "普通", "精英", "副本" };
            for (int i = 0; i < teamTypes.Length; i++)
            {
                bool isActive = i == 0;
                InkButton tab = new InkButton
                {
                    Text = teamTypes[i],
                    Width = 60,
                    Height = 28,
                    BackgroundColor = isActive ? InkWashTheme.GoldPrimary * 0.12f : Color.Transparent,
                    TextColor = isActive ? InkWashTheme.TextBrand : InkWashTheme.PaperAged,
                    Parent = _settingsPanel
                };
                tab.Clicked += () => OnTeamTypeTabClicked(teamTypes[i]);
                _teamTypeTabs.Add(tab);
            }

            _autoAcceptToggle = new InkButton
            {
                Text = _autoAcceptEnabled ? "开启" : "关闭",
                Width = 50,
                Height = 24,
                BackgroundColor = _autoAcceptEnabled ? InkWashTheme.JadePrimary * 0.15f : InkWashTheme.BaseTertiary,
                TextColor = _autoAcceptEnabled ? InkWashTheme.JadeBright : InkWashTheme.PaperAged,
                Parent = _settingsPanel
            };
            _autoAcceptToggle.Clicked += OnAutoAcceptToggleClicked;

            _minLevelSlider = new Panel
            {
                BackgroundColor = InkWashTheme.BaseDefault,
                Parent = _settingsPanel
            };

            _minLevelSliderThumb = new InkButton
            {
                Width = 16,
                Height = 16,
                BackgroundColor = InkWashTheme.GoldPrimary,
                Parent = _minLevelSlider
            };
            _minLevelSliderThumb.Clicked += OnMinLevelSliderClicked;

            _minLevelValueLabel = new Label
            {
                Text = $"Lv.{_minLevelValue}",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 11f),
                TextColor = InkWashTheme.TextBrand,
                Parent = _settingsPanel
            };

            _lootTabs = new List<InkButton>();
            string[] lootTypes = { "自由", "轮流", "队长分配" };
            for (int i = 0; i < lootTypes.Length; i++)
            {
                bool isActive = i == 0;
                InkButton tab = new InkButton
                {
                    Text = lootTypes[i],
                    Width = 70,
                    Height = 28,
                    BackgroundColor = isActive ? InkWashTheme.GoldPrimary * 0.12f : Color.Transparent,
                    TextColor = isActive ? InkWashTheme.TextBrand : InkWashTheme.PaperAged,
                    Parent = _settingsPanel
                };
                tab.Clicked += () => OnLootTabClicked(lootTypes[i]);
                _lootTabs.Add(tab);
            }

            _leaveTeamButton = new InkButton
            {
                Text = "退出队伍",
                Width = 120,
                Height = 36,
                BackgroundColor = Color.Transparent,
                TextColor = InkWashTheme.VermilionBright,
                Parent = _actionSection
            };
            _leaveTeamButton.Clicked += OnLeaveTeamClicked;
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

            float contentY = TopHeaderHeight + 32f;
            if (_pageTitle != null)
            {
                _pageTitle.Location = new Float2(32f, contentY);
            }
            contentY += 50f;

            float memberW = (sw - SidebarWidth - PanelGap * 2f) * MemberSectionRatio;
            float actionW = (sw - SidebarWidth - PanelGap * 2f) * ActionSectionRatio;
            float memberX = 32f;
            float actionX = memberX + memberW + PanelGap;
            float contentH = sh - contentY - 32f;

            if (_memberSection != null)
            {
                _memberSection.Location = new Float2(memberX, contentY);
                _memberSection.Size = new Float2(memberW, contentH);
            }

            if (_teamHeader != null)
            {
                _teamHeader.Location = Float2.Zero;
                _teamHeader.Size = new Float2(memberW, 40f);
            }
            if (_teamNameLabel != null)
            {
                _teamNameLabel.Location = new Float2(0f, 8f);
            }
            if (_teamCountLabel != null)
            {
                _teamCountLabel.Location = new Float2(120f, 12f);
            }
            if (_teamIdLabel != null)
            {
                _teamIdLabel.Location = new Float2(memberW - 100f, 12f);
            }

            if (_memberList != null)
            {
                _memberList.Location = new Float2(0f, 48f);
                _memberList.Size = new Float2(memberW, contentH - 48f);
            }

            float memberCardY = 0f;
            foreach (var card in _memberCards)
            {
                card.Location = new Float2(0f, memberCardY);
                card.Size = new Float2(memberW, 64f);
                memberCardY += 68f;
            }
            foreach (var slot in _emptySlots)
            {
                slot.Location = new Float2(0f, memberCardY);
                slot.Size = new Float2(memberW, 48f);
                memberCardY += 52f;
            }

            foreach (var card in _memberCards)
            {
                float cardX = 12f;
                float cardCenterY = 32f;

                foreach (var child in card.Children)
                {
                    if (child is InkAvatar)
                    {
                        child.Location = new Float2(cardX, cardCenterY - 22f);
                        cardX += 54f;
                    }
                    else if (child is Label label)
                    {
                        if (label.Text == "队长")
                        {
                            child.Location = new Float2(cardX + 80f, cardCenterY - 24f);
                        }
                        else if (label.TextColor == InkWashTheme.TextDefault || label.TextColor == InkWashTheme.PaperAged)
                        {
                            if (cardX == 66f)
                            {
                                child.Location = new Float2(cardX, cardCenterY - 24f);
                                cardX += 80f;
                            }
                            else
                            {
                                child.Location = new Float2(cardX, cardCenterY - 10f);
                                cardX += 60f;
                            }
                        }
                        else if (label.Text == "在线" || label.Text == "离线")
                        {
                            child.Location = new Float2(memberW - 60f, cardCenterY - 10f);
                        }
                    }
                    else if (child is Panel dot)
                    {
                        child.Location = new Float2(memberW - 80f, cardCenterY - 4f);
                    }
                }
            }

            if (_actionSection != null)
            {
                _actionSection.Location = new Float2(actionX, contentY);
                _actionSection.Size = new Float2(actionW, contentH);
            }

            if (_invitePanel != null)
            {
                _invitePanel.Location = Float2.Zero;
                _invitePanel.Size = new Float2(actionW, 260f);
            }

            if (_inviteTitle != null)
            {
                _inviteTitle.Location = new Float2(PanelPadding, PanelPadding);
            }

            if (_searchInput != null)
            {
                _searchInput.Location = new Float2(PanelPadding, PanelPadding + 32f);
                _searchInput.Size = new Float2(actionW - PanelPadding * 2f - 70f, 36f);
            }
            if (_searchButton != null)
            {
                _searchButton.Location = new Float2(actionW - PanelPadding - 60f, PanelPadding + 32f);
            }

            if (_friendList != null)
            {
                _friendList.Location = new Float2(PanelPadding, PanelPadding + 80f);
                _friendList.Size = new Float2(actionW - PanelPadding * 2f, 150f);
            }

            float friendRowY = 0f;
            foreach (var row in _friendRows)
            {
                row.Location = new Float2(0f, friendRowY);
                row.Size = new Float2(actionW - PanelPadding * 2f, 44f);
                friendRowY += 48f;
            }

            foreach (var row in _friendRows)
            {
                float rowX = 8f;
                float rowCenterY = 22f;

                foreach (var child in row.Children)
                {
                    if (child is InkAvatar)
                    {
                        child.Location = new Float2(rowX, rowCenterY - 16f);
                        rowX += 40f;
                    }
                    else if (child is Label label)
                    {
                        if (rowX == 48f)
                        {
                            child.Location = new Float2(rowX, rowCenterY - 14f);
                            rowX += 100f;
                        }
                        else
                        {
                            child.Location = new Float2(rowX, rowCenterY - 12f);
                            rowX += 50f;
                        }
                    }
                    else if (child is InkButton)
                    {
                        child.Location = new Float2(actionW - PanelPadding * 2f - 58f, rowCenterY - 14f);
                    }
                }
            }

            if (_settingsPanel != null)
            {
                _settingsPanel.Location = new Float2(0f, 270f);
                _settingsPanel.Size = new Float2(actionW, 200f);
            }

            if (_settingsTitle != null)
            {
                _settingsTitle.Location = new Float2(PanelPadding, PanelPadding);
            }

            float settingsY = PanelPadding + 32f;
            float settingsLabelW = 80f;
            float settingsValueX = settingsLabelW + 16f;

            foreach (var tab in _teamTypeTabs)
            {
                tab.Location = new Float2(settingsValueX + tab.Width * _teamTypeTabs.IndexOf(tab) + 8f * _teamTypeTabs.IndexOf(tab), settingsY);
            }
            settingsY += 44f;

            if (_autoAcceptToggle != null)
            {
                _autoAcceptToggle.Location = new Float2(actionW - PanelPadding - 60f, settingsY);
            }
            settingsY += 44f;

            float sliderWidth = actionW - settingsValueX - PanelPadding - 60f;
            if (_minLevelSlider != null)
            {
                _minLevelSlider.Location = new Float2(settingsValueX, settingsY + 8f);
                _minLevelSlider.Size = new Float2(sliderWidth, 4f);
            }
            if (_minLevelSliderThumb != null)
            {
                float thumbX = ((_minLevelValue - 1) / 59f) * (sliderWidth - 16f);
                _minLevelSliderThumb.Location = new Float2(thumbX, -6f);
            }
            if (_minLevelValueLabel != null)
            {
                _minLevelValueLabel.Location = new Float2(actionW - PanelPadding - 50f, settingsY);
            }
            settingsY += 44f;

            foreach (var tab in _lootTabs)
            {
                tab.Location = new Float2(settingsValueX + tab.Width * _lootTabs.IndexOf(tab) + 8f * _lootTabs.IndexOf(tab), settingsY);
            }

            if (_leaveTeamButton != null)
            {
                _leaveTeamButton.Location = new Float2(actionW - 130f, contentH - 44f);
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

        private void OnInviteSlotClicked()
        {
            FlaxEngine.Debug.Log("[MenuTeamPage] 邀请入队");
        }

        private void OnSearchClicked()
        {
            FlaxEngine.Debug.Log($"[MenuTeamPage] 搜索: {_searchInput.Text}");
        }

        private void OnInviteFriendClicked(string friendName)
        {
            FlaxEngine.Debug.Log($"[MenuTeamPage] 邀请好友: {friendName}");
        }

        private void OnTeamTypeTabClicked(string type)
        {
            foreach (var tab in _teamTypeTabs)
            {
                bool isActive = tab.Text == type;
                tab.BackgroundColor = isActive ? InkWashTheme.GoldPrimary * 0.12f : Color.Transparent;
                tab.TextColor = isActive ? InkWashTheme.TextBrand : InkWashTheme.PaperAged;
                tab.BorderColor = isActive ? InkWashTheme.GoldPrimary : InkWashTheme.BorderNeutralL1;
            }
        }

        private void OnAutoAcceptToggleClicked()
        {
            _autoAcceptEnabled = !_autoAcceptEnabled;
            _autoAcceptToggle.Text = _autoAcceptEnabled ? "开启" : "关闭";
            _autoAcceptToggle.BackgroundColor = _autoAcceptEnabled ? InkWashTheme.JadePrimary * 0.15f : InkWashTheme.BaseTertiary;
            _autoAcceptToggle.TextColor = _autoAcceptEnabled ? InkWashTheme.JadeBright : InkWashTheme.PaperAged;
            _autoAcceptToggle.BorderColor = _autoAcceptEnabled ? InkWashTheme.JadePrimary : InkWashTheme.BorderNeutralL1;
            FlaxEngine.Debug.Log($"[MenuTeamPage] 自动接受: {_autoAcceptEnabled}");
        }

        private void OnMinLevelSliderClicked()
        {
            _minLevelValue = (_minLevelValue % 59) + 1;
            _minLevelValueLabel.Text = $"Lv.{_minLevelValue}";
            float sliderWidth = _minLevelSlider.Width;
            float thumbX = ((_minLevelValue - 1) / 59f) * (sliderWidth - 16f);
            _minLevelSliderThumb.Location = new Float2(thumbX, -6f);
            FlaxEngine.Debug.Log($"[MenuTeamPage] 最低等级: {_minLevelValue}");
        }

        private void OnLootTabClicked(string type)
        {
            foreach (var tab in _lootTabs)
            {
                bool isActive = tab.Text == type;
                tab.BackgroundColor = isActive ? InkWashTheme.GoldPrimary * 0.12f : Color.Transparent;
                tab.TextColor = isActive ? InkWashTheme.TextBrand : InkWashTheme.PaperAged;
                tab.BorderColor = isActive ? InkWashTheme.GoldPrimary : InkWashTheme.BorderNeutralL1;
            }
        }

        private void OnLeaveTeamClicked()
        {
            FlaxEngine.Debug.Log("[MenuTeamPage] 退出队伍");
        }

        public void RefreshLayout()
        {
            _screenSize = new Float2(Width, Height);
            ApplyLayout();
        }
    }
}