using System;
using System.Collections.Generic;
using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.Ink;
using HundunWorld.Game.UI.Ink.Components;
using HundunWorld.Game.UI.StyleSystem;

namespace HundunWorld.Game.UI.Ink.Pages.Appearance
{
    public class MenuAppearancePage : ContainerControl, IInkPage
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

        private InkPanel _previewPanel;
        private Label _previewTitle;
        private ContainerControl _previewModel;

        private Panel _previewToolbar;
        private InkButton _btnRotateLeft;
        private InkButton _btnRotateRight;
        private Panel _divider1;
        private InkButton _btnZoomIn;
        private InkButton _btnZoomOut;
        private Panel _divider2;
        private InkButton _btnResetView;

        private InkPanel _appearanceList;
        private Label _listTitle;
        private List<ContainerControl> _appearanceItems;

        private InkPanel _detailPanel;
        private Label _detailName;
        private Label _detailType;
        private Label _detailDescription;
        private InkButton _equipButton;

        private const float SidebarWidth = 240f;
        private const float TopHeaderHeight = 60f;

        public event Action<string> NavigationRequested;

        public MenuAppearancePage()
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
                BuildPreviewPanel();
                BuildAppearanceList();
                BuildDetailPanel();

                ApplyLayout();
                RefreshAllData();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[MenuAppearancePage] 初始化失败: {ex.Message}");
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
            string[] navItems = { "不肝", "抽卡", "外观", "通行证", "属性", "奇珍阁", "设置" };
            for (int i = 0; i < navItems.Length; i++)
            {
                bool isActive = navItems[i] == "外观";
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
            string[] categories = { "发型", "面容", "服饰", "武器", "坐骑", "挂件" };
            for (int i = 0; i < categories.Length; i++)
            {
                bool isActive = i == 0;
                InkButton btn = new InkButton
                {
                    Text = categories[i],
                    Width = 120f,
                    Height = 44f,
                    BackgroundColor = isActive ? InkWashTheme.GoldPrimary : InkWashTheme.BaseTertiary,
                    TextColor = isActive ? InkWashTheme.PaperBright : InkWashTheme.TextSecondary,
                    Parent = _categoryTabs
                };
                _categoryButtons.Add(btn);
            }
        }

        private void BuildPreviewPanel()
        {
            _previewPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = InkWashTheme.Panel,
                Parent = _mainArea
            };

            _previewTitle = new Label
            {
                Text = "角色预览",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 16f),
                TextColor = InkWashTheme.GoldBright,
                Parent = _previewPanel
            };

            _previewModel = new ContainerControl
            {
                Size = new Float2(300, 400),
                BackgroundColor = InkWashTheme.BaseSecondary,
                Parent = _previewPanel
            };

            BuildPreviewToolbar();
        }

        private void BuildPreviewToolbar()
        {
            _previewToolbar = new Panel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = Color.Transparent,
                Parent = _previewPanel
            };

            _btnRotateLeft = new InkButton
            {
                Variant = InkButtonVariant.Ghost,
                ButtonSize = InkButtonSize.Sm,
                Text = "↺",
                Width = 36f,
                Height = 36f,
                BackgroundColor = Color.Transparent,
                TextColor = InkWashTheme.PaperBright,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 14f),
                Parent = _previewToolbar
            };
            _btnRotateLeft.ButtonClicked += OnRotateLeftClicked;

            _btnRotateRight = new InkButton
            {
                Variant = InkButtonVariant.Ghost,
                ButtonSize = InkButtonSize.Sm,
                Text = "↻",
                Width = 36f,
                Height = 36f,
                BackgroundColor = Color.Transparent,
                TextColor = InkWashTheme.PaperBright,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 14f),
                Parent = _previewToolbar
            };
            _btnRotateRight.ButtonClicked += OnRotateRightClicked;

            _divider1 = new Panel
            {
                Width = 1f,
                Height = 20f,
                BackgroundColor = InkWashTheme.TextTertiary,
                Parent = _previewToolbar
            };

            _btnZoomIn = new InkButton
            {
                Variant = InkButtonVariant.Ghost,
                ButtonSize = InkButtonSize.Sm,
                Text = "+",
                Width = 36f,
                Height = 36f,
                BackgroundColor = Color.Transparent,
                TextColor = InkWashTheme.PaperBright,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 16f),
                Parent = _previewToolbar
            };
            _btnZoomIn.ButtonClicked += OnZoomInClicked;

            _btnZoomOut = new InkButton
            {
                Variant = InkButtonVariant.Ghost,
                ButtonSize = InkButtonSize.Sm,
                Text = "−",
                Width = 36f,
                Height = 36f,
                BackgroundColor = Color.Transparent,
                TextColor = InkWashTheme.PaperBright,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 16f),
                Parent = _previewToolbar
            };
            _btnZoomOut.ButtonClicked += OnZoomOutClicked;

            _divider2 = new Panel
            {
                Width = 1f,
                Height = 20f,
                BackgroundColor = InkWashTheme.TextTertiary,
                Parent = _previewToolbar
            };

            _btnResetView = new InkButton
            {
                Variant = InkButtonVariant.Ghost,
                ButtonSize = InkButtonSize.Sm,
                Text = "⟲",
                Width = 36f,
                Height = 36f,
                BackgroundColor = Color.Transparent,
                TextColor = InkWashTheme.PaperBright,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 14f),
                Parent = _previewToolbar
            };
            _btnResetView.ButtonClicked += OnResetViewClicked;
        }

        private void OnRotateLeftClicked(Button button)
        {
            FlaxEngine.Debug.Log("[MenuAppearancePage] 左旋");
        }

        private void OnRotateRightClicked(Button button)
        {
            FlaxEngine.Debug.Log("[MenuAppearancePage] 右旋");
        }

        private void OnZoomInClicked(Button button)
        {
            FlaxEngine.Debug.Log("[MenuAppearancePage] 放大");
        }

        private void OnZoomOutClicked(Button button)
        {
            FlaxEngine.Debug.Log("[MenuAppearancePage] 缩小");
        }

        private void OnResetViewClicked(Button button)
        {
            FlaxEngine.Debug.Log("[MenuAppearancePage] 重置视角");
        }

        private void BuildAppearanceList()
        {
            _appearanceList = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = InkWashTheme.Panel,
                Parent = _mainArea
            };

            _listTitle = new Label
            {
                Text = "外观列表",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 16f),
                TextColor = InkWashTheme.GoldBright,
                Parent = _appearanceList
            };

            _appearanceItems = new List<ContainerControl>();
            string[] itemNames = { "流云发", "墨染发", "青丝发", "金缕发", "云鬓发", "素白发" };
            bool[] itemOwned = { true, true, true, false, true, false };
            for (int i = 0; i < itemNames.Length; i++)
            {
                ContainerControl item = new ContainerControl
                {
                    Width = 140f,
                    Height = 160f,
                    BackgroundColor = Color.Transparent,
                    Parent = _appearanceList
                };

                ContainerControl icon = new ContainerControl
                {
                    Size = new Float2(100, 100),
                    BackgroundColor = itemOwned[i] ? InkWashTheme.BaseTertiary : InkWashTheme.BaseSecondary,
                    Parent = item
                };

                Label iconLabel = new Label
                {
                    Text = itemOwned[i] ? itemNames[i][0].ToString() : "?",
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 28f),
                    TextColor = itemOwned[i] ? InkWashTheme.PaperBright : InkWashTheme.TextDisabled,
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                    AnchorPreset = AnchorPresets.StretchAll,
                    Parent = icon
                };

                Label nameLabel = new Label
                {
                    Text = itemNames[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                    TextColor = itemOwned[i] ? InkWashTheme.TextDefault : InkWashTheme.TextDisabled,
                    HorizontalAlignment = TextAlignment.Center,
                    Parent = item
                };

                Label statusLabel = new Label
                {
                    Text = itemOwned[i] ? "已拥有" : "未拥有",
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 10f),
                    TextColor = itemOwned[i] ? InkWashTheme.SpringGreenPrimary : InkWashTheme.VermilionBright,
                    HorizontalAlignment = TextAlignment.Center,
                    Parent = item
                };

                _appearanceItems.Add(item);
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
                Text = "流云发",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 24f),
                TextColor = InkWashTheme.GoldBright,
                Parent = _detailPanel
            };

            _detailType = new Label
            {
                Text = "发型 · 稀有",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                TextColor = InkWashTheme.GoldBright,
                Parent = _detailPanel
            };

            _detailDescription = new Label
            {
                Text = "如流云般飘逸的发丝，随风而动，尽显侠士风范。",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.TextSecondary,
                Parent = _detailPanel
            };

            _equipButton = new InkButton
            {
                Text = "穿戴",
                Width = 120f,
                Height = 44f,
                BackgroundColor = InkWashTheme.GoldPrimary,
                TextColor = InkWashTheme.PaperBright,
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
                    _categoryButtons[i].Location = new Float2(margin * 2f + i * 130f, tabCenterY);
                }
            }

            float previewTop = contentTop + tabHeight + margin;
            float previewWidth = 340f;
            float previewHeight = 500f;
            if (_previewPanel != null)
            {
                _previewPanel.Location = new Float2(margin, previewTop);
                _previewPanel.Size = new Float2(previewWidth, previewHeight);
            }

            if (_previewTitle != null) _previewTitle.Location = new Float2(margin, margin);
            if (_previewModel != null) _previewModel.Location = new Float2(previewWidth / 2f - 150f, margin + 40f);

            if (_previewToolbar != null)
            {
                float toolbarY = previewHeight - 56f;
                _previewToolbar.Location = new Float2(0, toolbarY);
                _previewToolbar.Size = new Float2(previewWidth, 48f);

                float btnGap = 12f;
                float totalBtnWidth = 36f * 5f + 1f * 2f + btnGap * 6f;
                float toolbarCenterX = (previewWidth - totalBtnWidth) / 2f;

                float currentX = toolbarCenterX;
                if (_btnRotateLeft != null)
                {
                    _btnRotateLeft.Location = new Float2(currentX, 6f);
                    currentX += 36f + btnGap;
                }
                if (_btnRotateRight != null)
                {
                    _btnRotateRight.Location = new Float2(currentX, 6f);
                    currentX += 36f + btnGap;
                }
                if (_divider1 != null)
                {
                    _divider1.Location = new Float2(currentX, 14f);
                    currentX += 1f + btnGap;
                }
                if (_btnZoomIn != null)
                {
                    _btnZoomIn.Location = new Float2(currentX, 6f);
                    currentX += 36f + btnGap;
                }
                if (_btnZoomOut != null)
                {
                    _btnZoomOut.Location = new Float2(currentX, 6f);
                    currentX += 36f + btnGap;
                }
                if (_divider2 != null)
                {
                    _divider2.Location = new Float2(currentX, 14f);
                    currentX += 1f + btnGap;
                }
                if (_btnResetView != null)
                {
                    _btnResetView.Location = new Float2(currentX, 6f);
                }
            }

            float listTop = previewTop;
            float listWidth = sw - SidebarWidth - margin * 2f - previewWidth - 280f;
            float listHeight = previewHeight;
            if (_appearanceList != null)
            {
                _appearanceList.Location = new Float2(margin + previewWidth + margin, listTop);
                _appearanceList.Size = new Float2(listWidth, listHeight);
            }

            if (_listTitle != null) _listTitle.Location = new Float2(margin, margin);
            float itemGap = 15f;
            int cols = 4;
            float itemWidth = (listWidth - margin * 2f - itemGap * (cols - 1)) / cols;
            for (int i = 0; i < _appearanceItems.Count; i++)
            {
                if (_appearanceItems[i] != null)
                {
                    int row = i / cols;
                    int col = i % cols;
                    _appearanceItems[i].Location = new Float2(margin + col * (itemWidth + itemGap), margin + 40f + row * 175f);
                    _appearanceItems[i].Size = new Float2(itemWidth, 160f);
                }
            }

            float detailTop = previewTop;
            float detailWidth = 260f;
            float detailHeight = 220f;
            if (_detailPanel != null)
            {
                _detailPanel.Location = new Float2(margin + previewWidth + margin + listWidth + margin, detailTop);
                _detailPanel.Size = new Float2(detailWidth, detailHeight);
            }

            if (_detailName != null) _detailName.Location = new Float2(margin, margin);
            if (_detailType != null) _detailType.Location = new Float2(margin, margin + 40f);
            if (_detailDescription != null) _detailDescription.Location = new Float2(margin, margin + 70f);
            if (_equipButton != null) _equipButton.Location = new Float2(detailWidth / 2f - 60f, margin + 140f);

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