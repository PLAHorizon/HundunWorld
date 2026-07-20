using System;
using System.Collections.Generic;
using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.Ink;
using HundunWorld.Game.UI.Ink.Components;
using HundunWorld.Game.UI.StyleSystem;

namespace HundunWorld.Game.UI.Ink.Pages.Livelihood
{
    public class MenuLivelihoodPage : ContainerControl, IInkPage
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

        private InkPanel _skillSelector;
        private List<InkButton> _skillTabs;

        private InkPanel _skillInfo;
        private Label _skillName;
        private Label _skillLevel;
        private Label _skillDescription;
        private InkBar _expBar;
        private Label _expLabel;

        private InkPanel _craftPanel;
        private Label _craftTitle;
        private List<ContainerControl> _craftRecipes;

        private InkPanel _inventoryPanel;
        private Label _inventoryTitle;
        private List<ContainerControl> _inventoryItems;

        private const float SidebarWidth = 240f;
        private const float TopHeaderHeight = 60f;

        public event Action<string> NavigationRequested;

        public MenuLivelihoodPage()
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
                BuildSkillSelector();
                BuildSkillInfo();
                BuildCraftPanel();
                BuildInventoryPanel();

                ApplyLayout();
                RefreshAllData();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[MenuLivelihoodPage] 初始化失败: {ex.Message}");
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
            string[] navItems = { "不肝", "抽卡", "生计", "通行证", "属性", "奇珍阁", "设置" };
            for (int i = 0; i < navItems.Length; i++)
            {
                bool isActive = navItems[i] == "生计";
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

        private void BuildSkillSelector()
        {
            _skillSelector = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = InkWashTheme.Panel,
                Parent = _mainArea
            };

            _skillTabs = new List<InkButton>();
            string[] skills = { "采药", "挖矿", "钓鱼", "烹饪", "锻造", "制药" };
            for (int i = 0; i < skills.Length; i++)
            {
                bool isActive = i == 0;
                InkButton btn = new InkButton
                {
                    Text = skills[i],
                    Width = 120f,
                    Height = 44f,
                    BackgroundColor = isActive ? InkWashTheme.GoldPrimary : InkWashTheme.BaseTertiary,
                    TextColor = isActive ? InkWashTheme.PaperBright : InkWashTheme.TextSecondary,
                    Parent = _skillSelector
                };
                _skillTabs.Add(btn);
            }
        }

        private void BuildSkillInfo()
        {
            _skillInfo = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = InkWashTheme.Panel,
                Parent = _mainArea
            };

            _skillName = new Label
            {
                Text = "采药",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 24f),
                TextColor = InkWashTheme.GoldBright,
                Parent = _skillInfo
            };

            _skillLevel = new Label
            {
                Text = "Lv. 15",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 14f),
                TextColor = InkWashTheme.GoldBright,
                Parent = _skillInfo
            };

            _skillDescription = new Label
            {
                Text = "在山野间采集各种草药，可用于制药或出售。",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.TextSecondary,
                Parent = _skillInfo
            };

            _expBar = new InkBar
            {
                Height = 8f,
                Width = 200f,
                Parent = _skillInfo
            };
            _expBar.Value = 0.72f;

            _expLabel = new Label
            {
                Text = "经验值: 2,160 / 3,000",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.TextSecondary,
                Parent = _skillInfo
            };
        }

        private void BuildCraftPanel()
        {
            _craftPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = InkWashTheme.Panel,
                Parent = _mainArea
            };

            _craftTitle = new Label
            {
                Text = "配方列表",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 16f),
                TextColor = InkWashTheme.GoldBright,
                Parent = _craftPanel
            };

            _craftRecipes = new List<ContainerControl>();
            string[] recipeNames = { "当归", "人参", "何首乌", "灵芝", "鹿茸", "冬虫夏草" };
            string[] recipeLevels = { "Lv.1", "Lv.5", "Lv.10", "Lv.15", "Lv.20", "Lv.25" };
            for (int i = 0; i < recipeNames.Length; i++)
            {
                ContainerControl item = new ContainerControl
                {
                    Width = 200f,
                    Height = 50f,
                    BackgroundColor = Color.Transparent,
                    Parent = _craftPanel
                };

                ContainerControl icon = new ContainerControl
                {
                    Size = new Float2(36, 36),
                    BackgroundColor = InkWashTheme.BaseTertiary,
                    Parent = item
                };

                Label nameLabel = new Label
                {
                    Text = recipeNames[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                    TextColor = InkWashTheme.TextDefault,
                    Parent = item
                };

                Label levelLabel = new Label
                {
                    Text = recipeLevels[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 10f),
                    TextColor = InkWashTheme.TextTertiary,
                    Parent = item
                };

                InkButton craftButton = new InkButton
                {
                    Text = "采集",
                    Width = 60f,
                    Height = 28f,
                    BackgroundColor = InkWashTheme.GoldPrimary,
                    TextColor = InkWashTheme.PaperBright,
                    Parent = item
                };

                _craftRecipes.Add(item);
            }
        }

        private void BuildInventoryPanel()
        {
            _inventoryPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = InkWashTheme.Panel,
                Parent = _mainArea
            };

            _inventoryTitle = new Label
            {
                Text = "材料库存",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 16f),
                TextColor = InkWashTheme.GoldBright,
                Parent = _inventoryPanel
            };

            _inventoryItems = new List<ContainerControl>();
            string[] itemNames = { "当归", "人参", "何首乌", "灵芝", "鹿茸", "冬虫夏草", "枸杞", "山药" };
            int[] itemCounts = { 25, 12, 8, 5, 3, 2, 40, 35 };
            for (int i = 0; i < itemNames.Length; i++)
            {
                ContainerControl item = new ContainerControl
                {
                    Width = 120f,
                    Height = 60f,
                    BackgroundColor = Color.Transparent,
                    Parent = _inventoryPanel
                };

                ContainerControl icon = new ContainerControl
                {
                    Size = new Float2(40, 40),
                    BackgroundColor = InkWashTheme.BaseTertiary,
                    Parent = item
                };

                Label nameLabel = new Label
                {
                    Text = itemNames[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 10f),
                    TextColor = InkWashTheme.TextDefault,
                    Parent = item
                };

                Label countLabel = new Label
                {
                    Text = $"×{itemCounts[i]}",
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 10f),
                    TextColor = InkWashTheme.GoldBright,
                    Parent = item
                };

                _inventoryItems.Add(item);
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
            float tabHeight = 60f;

            if (_skillSelector != null)
            {
                _skillSelector.Location = new Float2(margin, contentTop);
                _skillSelector.Size = new Float2(sw - SidebarWidth - margin * 2f, tabHeight);
            }

            float tabCenterY = tabHeight / 2f - 22f;
            for (int i = 0; i < _skillTabs.Count; i++)
            {
                if (_skillTabs[i] != null)
                {
                    _skillTabs[i].Location = new Float2(margin * 2f + i * 130f, tabCenterY);
                }
            }

            float infoTop = contentTop + tabHeight + margin;
            float infoHeight = 100f;
            float infoWidth = sw - SidebarWidth - margin * 2f;
            if (_skillInfo != null)
            {
                _skillInfo.Location = new Float2(margin, infoTop);
                _skillInfo.Size = new Float2(infoWidth, infoHeight);
            }

            if (_skillName != null) _skillName.Location = new Float2(margin, margin);
            if (_skillLevel != null) _skillLevel.Location = new Float2(margin + 100f, margin + 8f);
            if (_skillDescription != null) _skillDescription.Location = new Float2(margin, margin + 45f);
            if (_expBar != null) _expBar.Location = new Float2(margin, margin + 75f);
            if (_expLabel != null) _expLabel.Location = new Float2(margin + 210f, margin + 72f);

            float craftTop = infoTop + infoHeight + margin;
            float craftWidth = sw - SidebarWidth - margin * 2f - 280f;
            float craftHeight = sh - craftTop - margin;
            if (_craftPanel != null)
            {
                _craftPanel.Location = new Float2(margin, craftTop);
                _craftPanel.Size = new Float2(craftWidth, craftHeight);
            }

            if (_craftTitle != null) _craftTitle.Location = new Float2(margin, margin);
            for (int i = 0; i < _craftRecipes.Count; i++)
            {
                if (_craftRecipes[i] != null)
                {
                    _craftRecipes[i].Location = new Float2(margin, margin + 40f + i * 55f);
                }
            }

            float invTop = craftTop;
            float invWidth = 260f;
            float invHeight = craftHeight;
            if (_inventoryPanel != null)
            {
                _inventoryPanel.Location = new Float2(margin + craftWidth + margin, invTop);
                _inventoryPanel.Size = new Float2(invWidth, invHeight);
            }

            if (_inventoryTitle != null) _inventoryTitle.Location = new Float2(margin, margin);
            float itemGap = 10f;
            int cols = 2;
            float itemWidth = (invWidth - margin * 2f - itemGap) / cols;
            for (int i = 0; i < _inventoryItems.Count; i++)
            {
                if (_inventoryItems[i] != null)
                {
                    int row = i / cols;
                    int col = i % cols;
                    _inventoryItems[i].Location = new Float2(margin + col * (itemWidth + itemGap), margin + 40f + row * 65f);
                    _inventoryItems[i].Size = new Float2(itemWidth, 60f);
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