using System;
using System.Collections.Generic;
using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.Ink;
using HundunWorld.Game.UI.Ink.Components;
using HundunWorld.Game.UI.StyleSystem;

namespace HundunWorld.Game.UI.Ink.Pages.Gacha
{
    public class MenuGachaPage : ContainerControl, IInkPage
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
        private Label _currencyCoinLabel;
        private Label _currencyIngotLabel;
        private Label _timeLabel;

        private InkPanel _bannerPanel;
        private Label _bannerTitle;
        private Label _bannerSubtitle;
        private Label _bannerRateUp;

        private InkPanel _cardPoolSelector;
        private List<InkButton> _poolTabs;

        private InkPanel _gachaArea;
        private ContainerControl _gachaCard;
        private Label _cardRarity;
        private Label _cardName;
        private Label _cardDescription;

        private InkPanel _drawButtons;
        private InkButton _singleDrawButton;
        private Label _singleDrawCost;
        private InkButton _multiDrawButton;
        private Label _multiDrawCost;

        private InkPanel _probabilityPanel;
        private Label _probTitle;
        private Label[] _probLabels;

        private InkPanel _historyPanel;
        private Label _historyTitle;
        private List<ContainerControl> _historyItems;

        private const float SidebarWidth = 240f;
        private const float TopHeaderHeight = 60f;

        public event Action<string> NavigationRequested;

        public MenuGachaPage()
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
                BuildMainArea();
                BuildTopHeader();
                BuildBanner();
                BuildCardPoolSelector();
                BuildGachaArea();
                BuildDrawButtons();
                BuildProbabilityPanel();
                BuildHistoryPanel();

                ApplyLayout();
                RefreshAllData();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[MenuGachaPage] 初始化失败: {ex.Message}");
            }
        }

        private void BuildBackground()
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
                BackgroundColor = new Color(0f, 0f, 0f, 0.55f),
                Parent = this
            };
        }

        private void BuildSidebar()
        {
            _sidebar = new Panel
            {
                Width = SidebarWidth,
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = InkWashTheme.BaseDefault,
                Parent = this
            };

            _sidebarLogo = new Label
            {
                Text = "混沌世界",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 22f),
                TextColor = InkWashTheme.GoldBright,
                Parent = _sidebar
            };

            _sidebarLogoSub = new Label
            {
                Text = "HUNDUN WORLD",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.PaperDark,
                Parent = _sidebar
            };

            _sidebarNavButtons = new List<InkButton>();
            string[] navItems = { "不肝", "抽卡", "活动", "通行证", "属性", "奇珍阁", "设置" };
            for (int i = 0; i < navItems.Length; i++)
            {
                bool isActive = navItems[i] == "抽卡";
                InkButton btn = new InkButton
                {
                    Text = navItems[i],
                    Height = 48f,
                    BackgroundColor = isActive ? InkWashTheme.GoldPrimary * 0.1f : Color.Transparent,
                    TextColor = isActive ? InkWashTheme.GoldBright : InkWashTheme.PaperAged,
                    Parent = _sidebar
                };
                _sidebarNavButtons.Add(btn);
            }
        }

        private void BuildMainArea()
        {
            _mainArea = new Panel
            {
                AnchorPreset = AnchorPresets.StretchAll,
                BackgroundColor = Color.Transparent,
                Parent = this
            };
        }

        private void BuildTopHeader()
        {
            _topHeader = new Panel
            {
                Height = TopHeaderHeight,
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = InkWashTheme.Panel,
                Parent = _mainArea
            };

            _backButton = new InkButton
            {
                Width = 80f,
                Height = 36f,
                BackgroundColor = Color.Transparent,
                TextColor = InkWashTheme.GoldBright,
                Text = "← 返回战斗",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                Parent = _topHeader
            };

            _playerAvatar = new ContainerControl
            {
                Size = new Float2(36, 36),
                BackgroundColor = InkWashTheme.BaseTertiary,
                Parent = _topHeader
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
                Parent = _topHeader
            };

            _playerLevelLabel = new Label
            {
                Text = "Lv. 42",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.GoldBright,
                Parent = _topHeader
            };

            _currencyCoinLabel = new Label
            {
                Text = "铜钱 12,450",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.GoldBright,
                Parent = _topHeader
            };

            _currencyIngotLabel = new Label
            {
                Text = "元宝 328",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.VermilionBright,
                Parent = _topHeader
            };

            _timeLabel = new Label
            {
                Text = "戌时 三刻",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.TextSecondary,
                Parent = _topHeader
            };
        }

        private void BuildBanner()
        {
            _bannerPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = InkWashTheme.Panel,
                Parent = _mainArea
            };

            _bannerTitle = new Label
            {
                Text = "名侠招募 · 限时UP",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 28f),
                TextColor = InkWashTheme.GoldBright,
                Parent = _bannerPanel
            };

            _bannerSubtitle = new Label
            {
                Text = "江湖风云，英雄辈出",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                TextColor = InkWashTheme.TextSecondary,
                Parent = _bannerPanel
            };

            _bannerRateUp = new Label
            {
                Text = "★★★★★ 限定角色「令狐冲」概率UP!",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 14f),
                TextColor = InkWashTheme.VermilionBright,
                Parent = _bannerPanel
            };
        }

        private void BuildCardPoolSelector()
        {
            _cardPoolSelector = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = InkWashTheme.Panel,
                Parent = _mainArea
            };

            _poolTabs = new List<InkButton>();
            string[] pools = { "名侠招募", "武学宝库", "装备工坊" };
            for (int i = 0; i < pools.Length; i++)
            {
                bool isActive = i == 0;
                InkButton btn = new InkButton
                {
                    Text = pools[i],
                    Width = 140f,
                    Height = 44f,
                    BackgroundColor = isActive ? InkWashTheme.GoldPrimary : InkWashTheme.BaseTertiary,
                    TextColor = isActive ? InkWashTheme.PaperBright : InkWashTheme.TextSecondary,
                    Parent = _cardPoolSelector
                };
                _poolTabs.Add(btn);
            }
        }

        private void BuildGachaArea()
        {
            _gachaArea = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = InkWashTheme.Panel,
                Parent = _mainArea
            };

            _gachaCard = new ContainerControl
            {
                Size = new Float2(280, 380),
                BackgroundColor = InkWashTheme.BaseSecondary,
                Parent = _gachaArea
            };

            _cardRarity = new Label
            {
                Text = "★★★★★",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 18f),
                TextColor = InkWashTheme.VermilionBright,
                Parent = _gachaCard
            };

            _cardName = new Label
            {
                Text = "令狐冲",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 24f),
                TextColor = InkWashTheme.GoldBright,
                Parent = _gachaCard
            };

            _cardDescription = new Label
            {
                Text = "华山派大弟子，剑法卓绝，潇洒不羁。",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                TextColor = InkWashTheme.TextSecondary,
                Parent = _gachaCard
            };
        }

        private void BuildDrawButtons()
        {
            _drawButtons = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = InkWashTheme.Panel,
                Parent = _mainArea
            };

            _singleDrawButton = new InkButton
            {
                Text = "招募一次",
                Width = 160f,
                Height = 56f,
                BackgroundColor = InkWashTheme.BaseTertiary,
                TextColor = InkWashTheme.TextDefault,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 14f),
                Parent = _drawButtons
            };

            _singleDrawCost = new Label
            {
                Text = "消耗: 元宝 168",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.VermilionBright,
                Parent = _drawButtons
            };

            _multiDrawButton = new InkButton
            {
                Text = "招募十次",
                Width = 160f,
                Height = 56f,
                BackgroundColor = InkWashTheme.GoldPrimary,
                TextColor = InkWashTheme.PaperBright,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 14f),
                Parent = _drawButtons
            };

            _multiDrawCost = new Label
            {
                Text = "消耗: 元宝 1680",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.VermilionBright,
                Parent = _drawButtons
            };
        }

        private void BuildProbabilityPanel()
        {
            _probabilityPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = InkWashTheme.Panel,
                Parent = _mainArea
            };

            _probTitle = new Label
            {
                Text = "获取概率",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 16f),
                TextColor = InkWashTheme.GoldBright,
                Parent = _probabilityPanel
            };

            _probLabels = new Label[5];
            string[] probTexts = { "★★★★★ 名侠: 0.5%", "★★★★ 豪侠: 4.5%", "★★★ 侠客: 15%", "★★ 剑客: 40%", "★ 武徒: 40%" };
            for (int i = 0; i < probTexts.Length; i++)
            {
                _probLabels[i] = new Label
                {
                    Text = probTexts[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                    TextColor = InkWashTheme.TextSecondary,
                    Parent = _probabilityPanel
                };
            }
        }

        private void BuildHistoryPanel()
        {
            _historyPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = InkWashTheme.Panel,
                Parent = _mainArea
            };

            _historyTitle = new Label
            {
                Text = "招募记录",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 16f),
                TextColor = InkWashTheme.GoldBright,
                Parent = _historyPanel
            };

            _historyItems = new List<ContainerControl>();
            string[] historyNames = { "令狐冲", "任盈盈", "岳灵珊", "林平之", "风清扬" };
            string[] historyRarities = { "★★★★★", "★★★★", "★★★", "★★★", "★★★★★" };
            for (int i = 0; i < historyNames.Length; i++)
            {
                ContainerControl item = new ContainerControl
                {
                    Width = 150f,
                    Height = 40f,
                    BackgroundColor = Color.Transparent,
                    Parent = _historyPanel
                };

                Label rarityLabel = new Label
                {
                    Text = historyRarities[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 10f),
                    TextColor = historyRarities[i].Contains("★★★★★") ? InkWashTheme.VermilionBright : InkWashTheme.GoldBright,
                    Parent = item
                };

                Label nameLabel = new Label
                {
                    Text = historyNames[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                    TextColor = InkWashTheme.TextDefault,
                    Parent = item
                };

                _historyItems.Add(item);
            }
        }

        private void ApplyLayout()
        {
            float sw = _screenSize.X;
            float sh = _screenSize.Y;

            if (_sidebar != null)
            {
                _sidebar.Location = new Float2(0, 0);
                _sidebar.Size = new Float2(SidebarWidth, sh);
            }

            if (_mainArea != null)
            {
                _mainArea.Location = new Float2(SidebarWidth, 0);
                _mainArea.Size = new Float2(sw - SidebarWidth, sh);
            }

            if (_topHeader != null)
            {
                _topHeader.Location = new Float2(0, 0);
                _topHeader.Size = new Float2(sw - SidebarWidth, TopHeaderHeight);
            }

            float margin = 20f;

            if (_backButton != null) _backButton.Location = new Float2(margin, TopHeaderHeight / 2f - 18f);
            if (_playerAvatar != null) _playerAvatar.Location = new Float2(margin + 90f, TopHeaderHeight / 2f - 18f);
            if (_playerNameLabel != null) _playerNameLabel.Location = new Float2(margin + 134f, TopHeaderHeight / 2f - 14f);
            if (_playerLevelLabel != null) _playerLevelLabel.Location = new Float2(margin + 134f, TopHeaderHeight / 2f + 4f);

            float rightStart = sw - SidebarWidth - margin - 200f;
            if (_currencyCoinLabel != null) _currencyCoinLabel.Location = new Float2(rightStart - 120f, TopHeaderHeight / 2f - 8f);
            if (_currencyIngotLabel != null) _currencyIngotLabel.Location = new Float2(rightStart, TopHeaderHeight / 2f - 8f);
            if (_timeLabel != null) _timeLabel.Location = new Float2(rightStart + 100f, TopHeaderHeight / 2f - 8f);

            float contentTop = TopHeaderHeight + margin;
            float bannerHeight = 100f;

            if (_bannerPanel != null)
            {
                _bannerPanel.Location = new Float2(margin, contentTop);
                _bannerPanel.Size = new Float2(sw - SidebarWidth - margin * 2f, bannerHeight);
            }

            if (_bannerTitle != null) _bannerTitle.Location = new Float2(margin * 2f, margin);
            if (_bannerSubtitle != null) _bannerSubtitle.Location = new Float2(margin * 2f, margin + 40f);
            if (_bannerRateUp != null) _bannerRateUp.Location = new Float2(margin * 2f, margin + 70f);

            float poolTop = contentTop + bannerHeight + margin;
            float poolHeight = 60f;
            if (_cardPoolSelector != null)
            {
                _cardPoolSelector.Location = new Float2(margin, poolTop);
                _cardPoolSelector.Size = new Float2(sw - SidebarWidth - margin * 2f, poolHeight);
            }

            float poolCenterY = poolHeight / 2f - 22f;
            for (int i = 0; i < _poolTabs.Count; i++)
            {
                if (_poolTabs[i] != null)
                {
                    _poolTabs[i].Location = new Float2(margin * 2f + i * 150f, poolCenterY);
                }
            }

            float gachaTop = poolTop + poolHeight + margin;
            float gachaWidth = sw - SidebarWidth - margin * 2f;
            float gachaHeight = 420f;
            if (_gachaArea != null)
            {
                _gachaArea.Location = new Float2(margin, gachaTop);
                _gachaArea.Size = new Float2(gachaWidth, gachaHeight);
            }

            if (_gachaCard != null)
            {
                _gachaCard.Location = new Float2(gachaWidth / 2f - 140f, margin);
            }

            if (_cardRarity != null) _cardRarity.Location = new Float2(10f, 10f);
            if (_cardName != null) _cardName.Location = new Float2(10f, 40f);
            if (_cardDescription != null) _cardDescription.Location = new Float2(10f, 75f);

            float drawTop = gachaTop + gachaHeight + margin;
            float drawHeight = 80f;
            if (_drawButtons != null)
            {
                _drawButtons.Location = new Float2(margin, drawTop);
                _drawButtons.Size = new Float2(gachaWidth, drawHeight);
            }

            float drawCenterY = drawHeight / 2f - 28f;
            float drawCenterX = gachaWidth / 2f;
            if (_singleDrawButton != null) _singleDrawButton.Location = new Float2(drawCenterX - 175f, drawCenterY);
            if (_singleDrawCost != null) _singleDrawCost.Location = new Float2(drawCenterX - 175f, drawCenterY + 48f);
            if (_multiDrawButton != null) _multiDrawButton.Location = new Float2(drawCenterX + 15f, drawCenterY);
            if (_multiDrawCost != null) _multiDrawCost.Location = new Float2(drawCenterX + 15f, drawCenterY + 48f);

            float probTop = drawTop + drawHeight + margin;
            float probHeight = 120f;
            if (_probabilityPanel != null)
            {
                _probabilityPanel.Location = new Float2(margin, probTop);
                _probabilityPanel.Size = new Float2(gachaWidth * 0.4f, probHeight);
            }

            if (_probTitle != null) _probTitle.Location = new Float2(margin, margin);
            for (int i = 0; i < _probLabels.Length; i++)
            {
                if (_probLabels[i] != null)
                {
                    _probLabels[i].Location = new Float2(margin, margin + 28f + i * 20f);
                }
            }

            float historyTop = probTop;
            float historyWidth = gachaWidth * 0.55f;
            float historyHeight = 120f;
            if (_historyPanel != null)
            {
                _historyPanel.Location = new Float2(margin + gachaWidth * 0.45f, historyTop);
                _historyPanel.Size = new Float2(historyWidth, historyHeight);
            }

            if (_historyTitle != null) _historyTitle.Location = new Float2(margin, margin);
            for (int i = 0; i < _historyItems.Count; i++)
            {
                if (_historyItems[i] != null)
                {
                    _historyItems[i].Location = new Float2(margin + (i % 3) * 160f, margin + 28f + (i / 3) * 45f);
                }
            }

            float logoTop = margin * 3f;
            if (_sidebarLogo != null) _sidebarLogo.Location = new Float2(margin * 2f, logoTop);
            if (_sidebarLogoSub != null) _sidebarLogoSub.Location = new Float2(margin * 2f, logoTop + 30f);

            float navTop = logoTop + 80f;
            for (int i = 0; i < _sidebarNavButtons.Count; i++)
            {
                if (_sidebarNavButtons[i] != null)
                {
                    _sidebarNavButtons[i].Location = new Float2(0, navTop + i * 48f);
                    _sidebarNavButtons[i].Width = SidebarWidth;
                }
            }
        }

        private void RefreshAllData()
        {
        }

        public void RefreshLayout()
        {
            _screenSize = new Float2(Width, Height);
            ApplyLayout();
        }
    }
}