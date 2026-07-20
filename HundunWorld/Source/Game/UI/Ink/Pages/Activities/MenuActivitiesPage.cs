using System;
using System.Collections.Generic;
using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.Ink;
using HundunWorld.Game.UI.Ink.Components;
using HundunWorld.Game.UI.StyleSystem;

namespace HundunWorld.Game.UI.Ink.Pages.Activities
{
    public class MenuActivitiesPage : ContainerControl, IInkPage
    {
        private Float2 _screenSize;

        private InkPanel _leftNav;
        private InkPanel _mainArea;
        private InkPanel _topBar;
        private InkButton _backButton;
        private ContainerControl _playerAvatar;
        private Label _playerNameLabel;
        private Label _playerLevelLabel;
        private Label _currencyCoinLabel;
        private Label _currencyIngotLabel;
        private Label _timeLabel;

        private InkPanel _featuredBanner;
        private Label _bannerTitle;
        private Label _bannerSubtitle;
        private Label _bannerCountdown;
        private InkButton _bannerActionButton;

        private InkPanel _activityListPanel;
        private List<ContainerControl> _activityListItems;
        private List<InkButton> _tabButtons;

        private InkPanel _detailPanel;
        private Label _detailTitle;
        private Label _detailTime;
        private Label _detailDescription;
        private InkPanel _detailRequirements;
        private InkPanel _detailRewards;
        private InkButton _detailParticipateButton;

        private string[] _activityNames = { "武林大会", "灯会寻宝", "诗词大会", "帮派攻防", "寻宝奇缘", "江湖悬赏" };
        private string[] _activityStatuses = { "进行中", "进行中", "即将开始", "进行中", "进行中", "已结束" };
        private float[] _activityProgress = { 60f, 35f, 0f, 80f, 15f, 100f };
        private string[] _activityTimes = { "3天剩余", "5天剩余", "1天后开始", "12小时剩余", "6天剩余", "已结束" };
        private string[] _activityDescriptions = {
            "江湖各路豪杰齐聚一堂，比武论剑，争夺武林盟主之位。每日参与可获得丰厚奖励，排名前列者更可得绝世武学秘籍与稀有装备。",
            "元宵灯会，灯火辉煌，藏有无数奇珍异宝。收集灯谜碎片，兑换珍稀奖励。",
            "文人墨客齐聚，以诗会友。答对诗词题目，获得文采值与丰厚奖励。",
            "帮派之间的攻防战，守护帮派荣耀，夺取敌方资源。",
            "探索神秘藏宝图，寻找失落的宝藏，获得珍稀物品。",
            "江湖悬赏已结束，感谢各位侠士的参与。"
        };

        public MenuActivitiesPage()
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
                BuildLeftNav();
                BuildMainArea();
                BuildTopBar();
                BuildFeaturedBanner();
                BuildActivityList();
                BuildDetailPanel();

                ApplyLayout();
                RefreshAllData();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[MenuActivitiesPage] 初始化失败: {ex.Message}");
            }
        }

        private void BuildLeftNav()
        {
            _leftNav = new InkPanel
            {
                Width = 240f,
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = InkWashTheme.BaseDefault,
                Parent = this
            };

            Label logoText = new Label
            {
                Text = "混沌世界",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 22f),
                TextColor = InkWashTheme.GoldBright,
                Parent = _leftNav
            };

            Label logoSub = new Label
            {
                Text = "HUNDUN WORLD",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.PaperDark,
                Parent = _leftNav
            };
        }

        private void BuildMainArea()
        {
            _mainArea = new InkPanel
            {
                AnchorPreset = AnchorPresets.StretchAll,
                BackgroundColor = Color.Transparent,
                Parent = this
            };
        }

        private void BuildTopBar()
        {
            _topBar = new InkPanel
            {
                Height = 60f,
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = InkWashTheme.Panel,
                Parent = _mainArea
            };

            _backButton = new InkButton
            {
                Width = 36f,
                Height = 36f,
                BackgroundColor = InkWashTheme.BaseTertiary,
                TextColor = InkWashTheme.GoldBright,
                Text = "←",
                Parent = _topBar
            };
            _backButton.Clicked += OnBackClicked;

            _playerAvatar = new ContainerControl
            {
                Size = new Float2(36, 36),
                BackgroundColor = InkWashTheme.BaseTertiary,
                Parent = _topBar
            };
            Label avatarLabel = new Label
            {
                Text = "客",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 16f),
                TextColor = InkWashTheme.PaperBright,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.StretchAll,
                Parent = _playerAvatar
            };

            _playerNameLabel = new Label
            {
                Text = "江湖过客",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 14f),
                TextColor = InkWashTheme.TextDefault,
                Parent = _topBar
            };

            _playerLevelLabel = new Label
            {
                Text = "Lv. 42",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.TextTertiary,
                Parent = _topBar
            };

            _currencyCoinLabel = new Label
            {
                Text = "铜钱 12,450",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.GoldBright,
                Parent = _topBar
            };

            _currencyIngotLabel = new Label
            {
                Text = "元宝 328",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.VermilionBright,
                Parent = _topBar
            };

            _timeLabel = new Label
            {
                Text = "戌时 三刻",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.TextSecondary,
                Parent = _topBar
            };
        }

        private void BuildFeaturedBanner()
        {
            _featuredBanner = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = InkWashTheme.Panel,
                Parent = _mainArea
            };

            _bannerTitle = new Label
            {
                Text = "江湖盛会",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 28f),
                TextColor = InkWashTheme.GoldBright,
                Parent = _featuredBanner
            };

            _bannerSubtitle = new Label
            {
                Text = "限时活动 · 武林大会",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                TextColor = InkWashTheme.TextSecondary,
                Parent = _featuredBanner
            };

            _bannerCountdown = new Label
            {
                Text = "剩余 3天 14:23:08",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                TextColor = InkWashTheme.VermilionBright,
                Parent = _featuredBanner
            };

            _bannerActionButton = new InkButton
            {
                Text = "立即参与",
                Width = 140f,
                Height = 44f,
                BackgroundColor = InkWashTheme.GoldPrimary,
                TextColor = InkWashTheme.PaperBright,
                Parent = _featuredBanner
            };
            _bannerActionButton.Clicked += OnBannerActionClicked;
        }

        private void BuildActivityList()
        {
            _activityListPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = InkWashTheme.Panel,
                Parent = _mainArea
            };

            _tabButtons = new List<InkButton>();
            string[] tabs = { "全部", "进行中", "即将开始", "已结束" };
            for (int i = 0; i < tabs.Length; i++)
            {
                InkButton tab = new InkButton
                {
                    Text = tabs[i],
                    Width = 80f,
                    Height = 36f,
                    BackgroundColor = Color.Transparent,
                    TextColor = i == 0 ? InkWashTheme.GoldBright : InkWashTheme.TextSecondary,
                    Parent = _activityListPanel
                };
                tab.Clicked += () => OnTabClicked(tabs[i]);
                _tabButtons.Add(tab);
            }

            _activityListItems = new List<ContainerControl>();
            for (int i = 0; i < _activityNames.Length; i++)
            {
                ContainerControl item = new ContainerControl
                {
                    Height = 80f,
                    BackgroundColor = i == 0 ? InkWashTheme.BaseTertiary : Color.Transparent,
                    Parent = _activityListPanel
                };

                Label nameLabel = new Label
                {
                    Text = _activityNames[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 14f),
                    TextColor = InkWashTheme.TextDefault,
                    Parent = item
                };

                Label statusLabel = new Label
                {
                    Text = _activityStatuses[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                    TextColor = GetStatusColor(_activityStatuses[i]),
                    Parent = item
                };

                Label timeLabel = new Label
                {
                    Text = _activityTimes[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                    TextColor = InkWashTheme.TextTertiary,
                    Parent = item
                };

                InkBar progressBar = new InkBar
                {
                    Height = 4f,
                    Width = 100f,
                    Parent = item
                };
                progressBar.Value = _activityProgress[i] / 100f;

                Label progressLabel = new Label
                {
                    Text = $"{_activityProgress[i]}%",
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 10f),
                    TextColor = InkWashTheme.TextTertiary,
                    Parent = item
                };

                _activityListItems.Add(item);
            }
        }

        private void BuildDetailPanel()
        {
            _detailPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopRight,
                BackgroundColor = InkWashTheme.Panel,
                Parent = _mainArea
            };

            _detailTitle = new Label
            {
                Text = "武林大会",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 24f),
                TextColor = InkWashTheme.GoldBright,
                Parent = _detailPanel
            };

            _detailTime = new Label
            {
                Text = "2026.07.08 — 2026.07.15",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                TextColor = InkWashTheme.TextSecondary,
                Parent = _detailPanel
            };

            _detailDescription = new Label
            {
                Text = _activityDescriptions[0],
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                TextColor = InkWashTheme.TextSecondary,
                Parent = _detailPanel
            };

            _detailRequirements = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = Color.Transparent,
                Parent = _detailPanel
            };

            Label reqLabel1 = new Label
            {
                Text = "✓ 等级 ≥ 30",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.JadeBright,
                Parent = _detailRequirements
            };

            Label reqLabel2 = new Label
            {
                Text = "✓ 战力 ≥ 15000",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.JadeBright,
                Parent = _detailRequirements
            };

            _detailRewards = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = Color.Transparent,
                Parent = _detailPanel
            };

            string[] rewardNames = { "元宝", "武林秘籍", "铜钱", "稀有装备" };
            string[] rewardQuantities = { "×500", "×1", "×5000", "×1" };
            for (int i = 0; i < rewardNames.Length; i++)
            {
                ContainerControl rewardItem = new ContainerControl
                {
                    Height = 32f,
                    BackgroundColor = Color.Transparent,
                    Parent = _detailRewards
                };

                Label rewardName = new Label
                {
                    Text = rewardNames[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                    TextColor = InkWashTheme.TextDefault,
                    Parent = rewardItem
                };

                Label rewardQty = new Label
                {
                    Text = rewardQuantities[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                    TextColor = InkWashTheme.GoldBright,
                    Parent = rewardItem
                };
            }

            _detailParticipateButton = new InkButton
            {
                Text = "参与活动",
                Width = 160f,
                Height = 44f,
                BackgroundColor = InkWashTheme.VermilionPrimary,
                TextColor = InkWashTheme.PaperBright,
                Parent = _detailPanel
            };
            _detailParticipateButton.Clicked += OnParticipateClicked;
        }

        private Color GetStatusColor(string status)
        {
            switch (status)
            {
                case "进行中": return InkWashTheme.JadeBright;
                case "即将开始": return InkWashTheme.GoldBright;
                case "已结束": return InkWashTheme.TextTertiary;
                default: return InkWashTheme.TextSecondary;
            }
        }

        private void ApplyLayout()
        {
            float sw = _screenSize.X;
            float sh = _screenSize.Y;

            if (_leftNav != null)
            {
                _leftNav.Location = new Float2(0, 0);
                _leftNav.Size = new Float2(240f, sh);
            }

            if (_mainArea != null)
            {
                _mainArea.Location = new Float2(240f, 0);
                _mainArea.Size = new Float2(sw - 240f, sh);
            }

            if (_topBar != null)
            {
                _topBar.Location = new Float2(0, 0);
                _topBar.Size = new Float2(sw - 240f, 60f);
            }

            float margin = 16f;
            float topBarBottom = 60f;

            if (_backButton != null) _backButton.Location = new Float2(margin, topBarBottom / 2f - 18f);
            if (_playerAvatar != null) _playerAvatar.Location = new Float2(margin + 44f, topBarBottom / 2f - 18f);
            if (_playerNameLabel != null) _playerNameLabel.Location = new Float2(margin + 88f, topBarBottom / 2f - 16f);
            if (_playerLevelLabel != null) _playerLevelLabel.Location = new Float2(margin + 88f, topBarBottom / 2f + 4f);

            float rightStart = sw - 240f - margin - 150f;
            if (_currencyCoinLabel != null) _currencyCoinLabel.Location = new Float2(rightStart - 120f, topBarBottom / 2f - 8f);
            if (_currencyIngotLabel != null) _currencyIngotLabel.Location = new Float2(rightStart, topBarBottom / 2f - 8f);
            if (_timeLabel != null) _timeLabel.Location = new Float2(rightStart + 100f, topBarBottom / 2f - 8f);

            float bannerTop = topBarBottom + margin;
            if (_featuredBanner != null)
            {
                _featuredBanner.Location = new Float2(margin, bannerTop);
                _featuredBanner.Size = new Float2(sw - 240f - margin * 2f, 120f);
            }

            float bannerContentStart = margin * 2f;
            if (_bannerTitle != null) _bannerTitle.Location = new Float2(bannerContentStart, margin);
            if (_bannerSubtitle != null) _bannerSubtitle.Location = new Float2(bannerContentStart, margin + 36f);
            if (_bannerCountdown != null) _bannerCountdown.Location = new Float2(bannerContentStart, margin + 60f);
            if (_bannerActionButton != null)
            {
                _bannerActionButton.Location = new Float2(sw - 240f - margin - 140f, margin * 2f + 10f);
            }

            float contentStart = bannerTop + 120f + margin;
            float listWidth = (sw - 240f - margin * 3f) * 0.45f;
            float detailWidth = (sw - 240f - margin * 3f) * 0.55f;

            if (_activityListPanel != null)
            {
                _activityListPanel.Location = new Float2(margin, contentStart);
                _activityListPanel.Size = new Float2(listWidth, sh - contentStart - margin);
            }

            float tabTop = margin;
            for (int i = 0; i < _tabButtons.Count; i++)
            {
                if (_tabButtons[i] != null)
                {
                    _tabButtons[i].Location = new Float2(margin + i * 88f, tabTop);
                }
            }

            float itemTop = tabTop + 44f;
            for (int i = 0; i < _activityListItems.Count; i++)
            {
                if (_activityListItems[i] != null)
                {
                    _activityListItems[i].Location = new Float2(0, itemTop + i * 88f);
                    _activityListItems[i].Size = new Float2(listWidth, 80f);
                }
            }

            if (_detailPanel != null)
            {
                _detailPanel.Location = new Float2(margin + listWidth + margin, contentStart);
                _detailPanel.Size = new Float2(detailWidth, sh - contentStart - margin);
            }

            float detailContentTop = margin;
            if (_detailTitle != null) _detailTitle.Location = new Float2(margin, detailContentTop);
            if (_detailTime != null) _detailTime.Location = new Float2(margin, detailContentTop + 36f);

            float descTop = detailContentTop + 60f;
            if (_detailDescription != null)
            {
                _detailDescription.Location = new Float2(margin, descTop);
                _detailDescription.Size = new Float2(detailWidth - margin * 2f, 80f);
            }

            float reqTop = descTop + 90f;
            if (_detailRequirements != null)
            {
                _detailRequirements.Location = new Float2(margin, reqTop);
                _detailRequirements.Size = new Float2(detailWidth - margin * 2f, 40f);
            }

            float rewardTop = reqTop + 50f;
            if (_detailRewards != null)
            {
                _detailRewards.Location = new Float2(margin, rewardTop);
                _detailRewards.Size = new Float2(detailWidth - margin * 2f, 140f);
            }

            float btnTop = sh - contentStart - margin - 52f;
            if (_detailParticipateButton != null)
            {
                _detailParticipateButton.Location = new Float2(detailWidth / 2f - 80f, btnTop);
            }
        }

        private void RefreshAllData()
        {
        }

        private void OnBackClicked()
        {
            // Close menu;
        }

        private void OnBannerActionClicked()
        {
        }

        private void OnTabClicked(string tabName)
        {
            foreach (var tab in _tabButtons)
            {
                tab.TextColor = tab.Text == tabName ? InkWashTheme.GoldBright : InkWashTheme.TextSecondary;
            }
        }

        private void OnParticipateClicked()
        {
        }

        public void RefreshLayout()
        {
            _screenSize = new Float2(Width, Height);
            ApplyLayout();
        }
    }
}