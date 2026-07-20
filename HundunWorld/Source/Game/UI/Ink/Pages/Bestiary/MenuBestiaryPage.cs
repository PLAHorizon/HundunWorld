using System;
using System.Collections.Generic;
using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.Ink;
using HundunWorld.Game.UI.Ink.Components;
using HundunWorld.Game.UI.StyleSystem;

namespace HundunWorld.Game.UI.Ink.Pages.Bestiary
{
    public class MenuBestiaryPage : ContainerControl, IInkPage
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

        private InkPanel _categoryTabs;
        private List<InkButton> _categoryButtons;

        private InkPanel _collectionStats;
        private Label _statsTitle;
        private Label _monsterCount;
        private Label _characterCount;
        private Label _equipmentCount;

        private InkPanel _bestiaryGrid;
        private List<ContainerControl> _bestiaryItems;

        private InkPanel _detailPanel;
        private Label _detailName;
        private Label _detailRarity;
        private Label _detailDescription;
        private Label _detailLocation;
        private Label _detailDrop;

        private const float SidebarWidth = 240f;
        private const float TopHeaderHeight = 60f;

        public event Action<string> NavigationRequested;

        public MenuBestiaryPage()
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
                BuildCategoryTabs();
                BuildCollectionStats();
                BuildBestiaryGrid();
                BuildDetailPanel();

                ApplyLayout();
                RefreshAllData();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[MenuBestiaryPage] 初始化失败: {ex.Message}");
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
            string[] navItems = { "不肝", "抽卡", "活动", "图鉴", "属性", "奇珍阁", "设置" };
            for (int i = 0; i < navItems.Length; i++)
            {
                bool isActive = navItems[i] == "图鉴";
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

        private void BuildCategoryTabs()
        {
            _categoryTabs = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = InkWashTheme.Panel,
                Parent = _mainArea
            };

            _categoryButtons = new List<InkButton>();
            string[] categories = { "怪物图鉴", "名侠图鉴", "装备图鉴", "武学图鉴" };
            for (int i = 0; i < categories.Length; i++)
            {
                bool isActive = i == 0;
                InkButton btn = new InkButton
                {
                    Text = categories[i],
                    Width = 140f,
                    Height = 44f,
                    BackgroundColor = isActive ? InkWashTheme.GoldPrimary : InkWashTheme.BaseTertiary,
                    TextColor = isActive ? InkWashTheme.PaperBright : InkWashTheme.TextSecondary,
                    Parent = _categoryTabs
                };
                _categoryButtons.Add(btn);
            }
        }

        private void BuildCollectionStats()
        {
            _collectionStats = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = InkWashTheme.Panel,
                Parent = _mainArea
            };

            _statsTitle = new Label
            {
                Text = "收集进度",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 16f),
                TextColor = InkWashTheme.GoldBright,
                Parent = _collectionStats
            };

            _monsterCount = new Label
            {
                Text = "怪物图鉴: 48 / 120",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.TextSecondary,
                Parent = _collectionStats
            };

            _characterCount = new Label
            {
                Text = "名侠图鉴: 15 / 45",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.TextSecondary,
                Parent = _collectionStats
            };

            _equipmentCount = new Label
            {
                Text = "装备图鉴: 72 / 180",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.TextSecondary,
                Parent = _collectionStats
            };
        }

        private void BuildBestiaryGrid()
        {
            _bestiaryGrid = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = InkWashTheme.Panel,
                Parent = _mainArea
            };

            _bestiaryItems = new List<ContainerControl>();
            string[] monsterNames = { "野猪", "灰狼", "山贼", "猛虎", "毒蛇", "蝙蝠", "熊", "狐狸", "蜘蛛", "蝎子", "狼蛛", "蜈蚣" };
            bool[] monsterDiscovered = { true, true, true, true, true, false, true, false, false, true, false, false };
            for (int i = 0; i < monsterNames.Length; i++)
            {
                ContainerControl item = new ContainerControl
                {
                    Width = 120f,
                    Height = 140f,
                    BackgroundColor = Color.Transparent,
                    Parent = _bestiaryGrid
                };

                ContainerControl icon = new ContainerControl
                {
                    Size = new Float2(80, 80),
                    BackgroundColor = monsterDiscovered[i] ? InkWashTheme.BaseTertiary : InkWashTheme.BaseSecondary,
                    Parent = item
                };

                Label iconLabel = new Label
                {
                    Text = monsterDiscovered[i] ? monsterNames[i][0].ToString() : "?",
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 24f),
                    TextColor = monsterDiscovered[i] ? InkWashTheme.PaperBright : InkWashTheme.TextDisabled,
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                    AnchorPreset = AnchorPresets.StretchAll,
                    Parent = icon
                };

                Label nameLabel = new Label
                {
                    Text = monsterDiscovered[i] ? monsterNames[i] : "???",
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                    TextColor = monsterDiscovered[i] ? InkWashTheme.TextDefault : InkWashTheme.TextDisabled,
                    HorizontalAlignment = TextAlignment.Center,
                    Parent = item
                };

                _bestiaryItems.Add(item);
            }
        }

        private void BuildDetailPanel()
        {
            _detailPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = InkWashTheme.Panel,
                Parent = _mainArea
            };

            _detailName = new Label
            {
                Text = "野猪",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 24f),
                TextColor = InkWashTheme.GoldBright,
                Parent = _detailPanel
            };

            _detailRarity = new Label
            {
                Text = "普通怪物",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                TextColor = InkWashTheme.TextSecondary,
                Parent = _detailPanel
            };

            _detailDescription = new Label
            {
                Text = "山野间常见的野兽，性情暴躁，冲撞力强。",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.TextSecondary,
                Parent = _detailPanel
            };

            _detailLocation = new Label
            {
                Text = "出没地点: 新手村外、野猪林",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.TextSecondary,
                Parent = _detailPanel
            };

            _detailDrop = new Label
            {
                Text = "掉落物品: 野猪皮、野猪牙、兽肉",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.TextSecondary,
                Parent = _detailPanel
            };
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
            float tabHeight = 60f;

            if (_categoryTabs != null)
            {
                _categoryTabs.Location = new Float2(margin, contentTop);
                _categoryTabs.Size = new Float2(sw - SidebarWidth - margin * 2f, tabHeight);
            }

            float tabCenterY = tabHeight / 2f - 22f;
            for (int i = 0; i < _categoryButtons.Count; i++)
            {
                if (_categoryButtons[i] != null)
                {
                    _categoryButtons[i].Location = new Float2(margin * 2f + i * 150f, tabCenterY);
                }
            }

            float statsTop = contentTop + tabHeight + margin;
            float statsHeight = 80f;
            float statsWidth = sw - SidebarWidth - margin * 2f;
            if (_collectionStats != null)
            {
                _collectionStats.Location = new Float2(margin, statsTop);
                _collectionStats.Size = new Float2(statsWidth, statsHeight);
            }

            if (_statsTitle != null) _statsTitle.Location = new Float2(margin, margin);
            if (_monsterCount != null) _monsterCount.Location = new Float2(margin, margin + 35f);
            if (_characterCount != null) _characterCount.Location = new Float2(margin + 200f, margin + 35f);
            if (_equipmentCount != null) _equipmentCount.Location = new Float2(margin + 400f, margin + 35f);

            float gridTop = statsTop + statsHeight + margin;
            float gridWidth = sw - SidebarWidth - margin * 2f - 280f;
            float gridHeight = sh - gridTop - margin;
            if (_bestiaryGrid != null)
            {
                _bestiaryGrid.Location = new Float2(margin, gridTop);
                _bestiaryGrid.Size = new Float2(gridWidth, gridHeight);
            }

            float itemGap = 15f;
            int cols = 6;
            float itemWidth = (gridWidth - itemGap * (cols - 1)) / cols;
            for (int i = 0; i < _bestiaryItems.Count; i++)
            {
                if (_bestiaryItems[i] != null)
                {
                    int row = i / cols;
                    int col = i % cols;
                    _bestiaryItems[i].Location = new Float2(col * (itemWidth + itemGap), margin + row * 155f);
                    _bestiaryItems[i].Size = new Float2(itemWidth, 140f);
                }
            }

            float detailTop = gridTop;
            float detailWidth = 260f;
            float detailHeight = 300f;
            if (_detailPanel != null)
            {
                _detailPanel.Location = new Float2(margin + gridWidth + margin, detailTop);
                _detailPanel.Size = new Float2(detailWidth, detailHeight);
            }

            if (_detailName != null) _detailName.Location = new Float2(margin, margin);
            if (_detailRarity != null) _detailRarity.Location = new Float2(margin, margin + 40f);
            if (_detailDescription != null) _detailDescription.Location = new Float2(margin, margin + 70f);
            if (_detailLocation != null) _detailLocation.Location = new Float2(margin, margin + 110f);
            if (_detailDrop != null) _detailDrop.Location = new Float2(margin, margin + 140f);

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