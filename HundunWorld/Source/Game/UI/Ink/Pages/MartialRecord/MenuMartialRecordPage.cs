using System;
using System.Collections.Generic;
using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.Ink;
using HundunWorld.Game.UI.Ink.Components;
using HundunWorld.Game.UI.StyleSystem;

namespace HundunWorld.Game.UI.Ink.Pages.MartialRecord
{
    public class MenuMartialRecordPage : ContainerControl, IInkPage
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

        private InkPanel _styleTabs;
        private List<InkButton> _styleButtons;

        private InkPanel _martialList;
        private Label _listTitle;
        private List<ContainerControl> _martialItems;

        private InkPanel _detailPanel;
        private Label _detailName;
        private Label _detailLevel;
        private Label _detailDescription;
        private InkBar _progressBar;
        private Label _progressLabel;

        private InkPanel _skillTree;
        private Label _skillTreeTitle;
        private List<ContainerControl> _skillNodes;

        private const float SidebarWidth = 240f;
        private const float TopHeaderHeight = 60f;

        public event Action<string> NavigationRequested;

        public MenuMartialRecordPage()
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
                BuildStyleTabs();
                BuildMartialList();
                BuildDetailPanel();
                BuildSkillTree();

                ApplyLayout();
                RefreshAllData();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[MenuMartialRecordPage] 初始化失败: {ex.Message}");
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
            string[] navItems = { "不肝", "抽卡", "活动", "武学", "属性", "奇珍阁", "设置" };
            for (int i = 0; i < navItems.Length; i++)
            {
                bool isActive = navItems[i] == "武学";
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

        private void BuildStyleTabs()
        {
            _styleTabs = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = InkWashTheme.Panel,
                Parent = _mainArea
            };

            _styleButtons = new List<InkButton>();
            string[] styles = { "剑法", "刀法", "拳法", "内功", "轻功", "暗器" };
            for (int i = 0; i < styles.Length; i++)
            {
                bool isActive = i == 0;
                InkButton btn = new InkButton
                {
                    Text = styles[i],
                    Width = 120f,
                    Height = 44f,
                    BackgroundColor = isActive ? InkWashTheme.GoldPrimary : InkWashTheme.BaseTertiary,
                    TextColor = isActive ? InkWashTheme.PaperBright : InkWashTheme.TextSecondary,
                    Parent = _styleTabs
                };
                _styleButtons.Add(btn);
            }
        }

        private void BuildMartialList()
        {
            _martialList = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = InkWashTheme.Panel,
                Parent = _mainArea
            };

            _listTitle = new Label
            {
                Text = "已学武学",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 16f),
                TextColor = InkWashTheme.GoldBright,
                Parent = _martialList
            };

            _martialItems = new List<ContainerControl>();
            string[] martialNames = { "独孤九剑", "太极剑", "华山剑法", "玉女剑法", "越女剑", "玄铁剑法" };
            string[] martialLevels = { "Lv.8", "Lv.6", "Lv.10", "Lv.5", "Lv.3", "Lv.1" };
            for (int i = 0; i < martialNames.Length; i++)
            {
                ContainerControl item = new ContainerControl
                {
                    Width = 220f,
                    Height = 60f,
                    BackgroundColor = Color.Transparent,
                    Parent = _martialList
                };

                ContainerControl icon = new ContainerControl
                {
                    Size = new Float2(44, 44),
                    BackgroundColor = InkWashTheme.BaseTertiary,
                    Parent = item
                };

                Label nameLabel = new Label
                {
                    Text = martialNames[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                    TextColor = InkWashTheme.TextDefault,
                    Parent = item
                };

                Label levelLabel = new Label
                {
                    Text = martialLevels[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 10f),
                    TextColor = InkWashTheme.GoldBright,
                    Parent = item
                };

                _martialItems.Add(item);
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
                Text = "独孤九剑",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 28f),
                TextColor = InkWashTheme.GoldBright,
                Parent = _detailPanel
            };

            _detailLevel = new Label
            {
                Text = "Lv. 8",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 14f),
                TextColor = InkWashTheme.GoldBright,
                Parent = _detailPanel
            };

            _detailDescription = new Label
            {
                Text = "剑魔独孤求败所创剑法，共九式，破尽天下武学。",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.TextSecondary,
                Parent = _detailPanel
            };

            _progressBar = new InkBar
            {
                Height = 10f,
                Width = 250f,
                Parent = _detailPanel
            };
            _progressBar.Value = 0.85f;

            _progressLabel = new Label
            {
                Text = "修炼进度: 85%",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.TextSecondary,
                Parent = _detailPanel
            };
        }

        private void BuildSkillTree()
        {
            _skillTree = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = InkWashTheme.Panel,
                Parent = _mainArea
            };

            _skillTreeTitle = new Label
            {
                Text = "技能树",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 16f),
                TextColor = InkWashTheme.GoldBright,
                Parent = _skillTree
            };

            _skillNodes = new List<ContainerControl>();
            string[] skillNames = { "总诀式", "破剑式", "破刀式", "破枪式", "破鞭式", "破索式" };
            bool[] skillUnlocked = { true, true, true, true, false, false };
            for (int i = 0; i < skillNames.Length; i++)
            {
                ContainerControl node = new ContainerControl
                {
                    Width = 100f,
                    Height = 100f,
                    BackgroundColor = Color.Transparent,
                    Parent = _skillTree
                };

                ContainerControl icon = new ContainerControl
                {
                    Size = new Float2(60, 60),
                    BackgroundColor = skillUnlocked[i] ? InkWashTheme.GoldPrimary * 0.2f : InkWashTheme.BaseSecondary,
                    Parent = node
                };

                Label iconLabel = new Label
                {
                    Text = skillUnlocked[i] ? skillNames[i][0].ToString() : "?",
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 20f),
                    TextColor = skillUnlocked[i] ? InkWashTheme.GoldBright : InkWashTheme.TextDisabled,
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                    AnchorPreset = AnchorPresets.StretchAll,
                    Parent = icon
                };

                Label nameLabel = new Label
                {
                    Text = skillNames[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 10f),
                    TextColor = skillUnlocked[i] ? InkWashTheme.TextDefault : InkWashTheme.TextDisabled,
                    HorizontalAlignment = TextAlignment.Center,
                    Parent = node
                };

                _skillNodes.Add(node);
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

            if (_styleTabs != null)
            {
                _styleTabs.Location = new Float2(margin, contentTop);
                _styleTabs.Size = new Float2(sw - SidebarWidth - margin * 2f, tabHeight);
            }

            float tabCenterY = tabHeight / 2f - 22f;
            for (int i = 0; i < _styleButtons.Count; i++)
            {
                if (_styleButtons[i] != null)
                {
                    _styleButtons[i].Location = new Float2(margin * 2f + i * 130f, tabCenterY);
                }
            }

            float listTop = contentTop + tabHeight + margin;
            float listWidth = 260f;
            float listHeight = sh - listTop - margin;
            if (_martialList != null)
            {
                _martialList.Location = new Float2(margin, listTop);
                _martialList.Size = new Float2(listWidth, listHeight);
            }

            if (_listTitle != null) _listTitle.Location = new Float2(margin, margin);
            for (int i = 0; i < _martialItems.Count; i++)
            {
                if (_martialItems[i] != null)
                {
                    _martialItems[i].Location = new Float2(margin, margin + 40f + i * 65f);
                }
            }

            float detailTop = listTop;
            float detailWidth = sw - SidebarWidth - margin * 2f - listWidth - 20f;
            float detailHeight = 220f;
            if (_detailPanel != null)
            {
                _detailPanel.Location = new Float2(margin + listWidth + margin, detailTop);
                _detailPanel.Size = new Float2(detailWidth, detailHeight);
            }

            if (_detailName != null) _detailName.Location = new Float2(margin, margin);
            if (_detailLevel != null) _detailLevel.Location = new Float2(margin + 200f, margin + 8f);
            if (_detailDescription != null) _detailDescription.Location = new Float2(margin, margin + 55f);
            if (_progressBar != null) _progressBar.Location = new Float2(margin, margin + 95f);
            if (_progressLabel != null) _progressLabel.Location = new Float2(margin + 260f, margin + 92f);

            float treeTop = detailTop + detailHeight + margin;
            float treeWidth = detailWidth;
            float treeHeight = sh - treeTop - margin;
            if (_skillTree != null)
            {
                _skillTree.Location = new Float2(margin + listWidth + margin, treeTop);
                _skillTree.Size = new Float2(treeWidth, treeHeight);
            }

            if (_skillTreeTitle != null) _skillTreeTitle.Location = new Float2(margin, margin);
            float nodeGap = 20f;
            int cols = 3;
            float nodeWidth = (treeWidth - margin * 2f - nodeGap * (cols - 1)) / cols;
            for (int i = 0; i < _skillNodes.Count; i++)
            {
                if (_skillNodes[i] != null)
                {
                    int row = i / cols;
                    int col = i % cols;
                    _skillNodes[i].Location = new Float2(margin + col * (nodeWidth + nodeGap), margin + 40f + row * 110f);
                    _skillNodes[i].Size = new Float2(nodeWidth, 100f);
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