using System;
using System.Collections.Generic;
using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.Ink;
using HundunWorld.Game.UI.Ink.Components;
using HundunWorld.Game.UI.StyleSystem;

namespace HundunWorld.Game.UI.Ink.Pages.BattlePass
{
    public class MenuBattlePassPage : ContainerControl, IInkPage
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

        private InkPanel _passHeader;
        private Label _passTitle;
        private Label _passSubtitle;
        private Label _seasonInfoTime;
        private Label _seasonInfoPlayers;
        private Label _currentLevelLabel;
        private Label _progressInfoLabel;
        private InkBar _progressBar;
        private InkButton _unlockButton;
        private InkButton _upgradeButton;

        private InkPanel _unlockBanner;
        private Label _unlockTitle;
        private Label _unlockDesc;
        private InkButton _unlockActionButton;

        private InkPanel _trackSection;
        private Label _trackTitle;
        private List<ContainerControl> _paidNodes;
        private List<ContainerControl> _freeNodes;

        private const float SidebarWidth = 240f;
        private const float TopHeaderHeight = 60f;
        private const float PanelGap = 20f;

        public event Action<string> NavigationRequested;

        public MenuBattlePassPage()
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
                BuildPassHeader();
                BuildUnlockBanner();
                BuildTrackSection();

                ApplyLayout();
                RefreshAllData();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[MenuBattlePassPage] 初始化失败: {ex.Message}");
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
                bool isActive = navItems[i] == "通行证";
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

        private void BuildPassHeader()
        {
            _passHeader = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = InkWashTheme.Panel,
                Parent = _mainArea
            };

            _passTitle = new Label
            {
                Text = "江湖令 · 通行证",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 28f),
                TextColor = InkWashTheme.GoldBright,
                Parent = _passHeader
            };

            _passSubtitle = new Label
            {
                Text = "江湖行 · 纵马天涯，行侠仗义",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                TextColor = InkWashTheme.TextSecondary,
                Parent = _passHeader
            };

            _seasonInfoTime = new Label
            {
                Text = "剩余时间: 28天 06:30:00",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.VermilionBright,
                Parent = _passHeader
            };

            _seasonInfoPlayers = new Label
            {
                Text = "参与人数: 1,284,567",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.TextSecondary,
                Parent = _passHeader
            };

            _currentLevelLabel = new Label
            {
                Text = "当前等级 Lv. 23",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 24f),
                TextColor = InkWashTheme.GoldBright,
                Parent = _passHeader
            };

            _progressInfoLabel = new Label
            {
                Text = "升级进度 EXP 1,950 / 3,000",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                TextColor = InkWashTheme.TextSecondary,
                Parent = _passHeader
            };

            _progressBar = new InkBar
            {
                Height = 8f,
                Width = 200f,
                Parent = _passHeader
            };
            _progressBar.Value = 0.65f;

            _unlockButton = new InkButton
            {
                Text = "解锁至尊通行证",
                Width = 160f,
                Height = 44f,
                BackgroundColor = InkWashTheme.GoldPrimary,
                TextColor = InkWashTheme.PaperBright,
                Parent = _passHeader
            };

            _upgradeButton = new InkButton
            {
                Text = "立即升级",
                Width = 120f,
                Height = 44f,
                BackgroundColor = InkWashTheme.VermilionPrimary,
                TextColor = InkWashTheme.PaperBright,
                Parent = _passHeader
            };
        }

        private void BuildUnlockBanner()
        {
            _unlockBanner = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = InkWashTheme.Panel,
                Parent = _mainArea
            };

            _unlockTitle = new Label
            {
                Text = "您尚未解锁至尊通行证",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 14f),
                TextColor = InkWashTheme.VermilionBright,
                Parent = _unlockBanner
            };

            _unlockDesc = new Label
            {
                Text = "解锁后可领取专属奖励，包含名侠碎片、稀有装备、限定外观等",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.TextSecondary,
                Parent = _unlockBanner
            };

            _unlockActionButton = new InkButton
            {
                Text = "立即解锁",
                Width = 140f,
                Height = 40f,
                BackgroundColor = InkWashTheme.GoldPrimary,
                TextColor = InkWashTheme.PaperBright,
                Parent = _unlockBanner
            };
        }

        private void BuildTrackSection()
        {
            _trackSection = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = InkWashTheme.Panel,
                Parent = _mainArea
            };

            _trackTitle = new Label
            {
                Text = "等级轨道",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 20f),
                TextColor = InkWashTheme.GoldBright,
                Parent = _trackSection
            };

            string[] rewardNames = { "寒铁剑", "名侠碎片×3", "元宝×200", "名侠碎片×5", "玄铁护腕", "元宝×300" };
            _paidNodes = new List<ContainerControl>();
            for (int i = 0; i < 6; i++)
            {
                ContainerControl node = new ContainerControl
                {
                    Width = 80f,
                    Height = 100f,
                    BackgroundColor = Color.Transparent,
                    Parent = _trackSection
                };

                Label levelLabel = new Label
                {
                    Text = $"Lv.{20 + i}",
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 10f),
                    TextColor = InkWashTheme.TextTertiary,
                    Parent = node
                };

                ContainerControl rewardIcon = new ContainerControl
                {
                    Size = new Float2(48, 48),
                    BackgroundColor = InkWashTheme.BaseTertiary,
                    Parent = node
                };

                Label rewardLabel = new Label
                {
                    Text = rewardNames[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 10f),
                    TextColor = InkWashTheme.TextSecondary,
                    Parent = node
                };

                _paidNodes.Add(node);
            }

            _freeNodes = new List<ContainerControl>();
            string[] freeRewardNames = { "铜钱×500", "经验丹×2", "铜钱×1000", "经验丹×3", "铜钱×1500", "经验丹×5" };
            for (int i = 0; i < 6; i++)
            {
                ContainerControl node = new ContainerControl
                {
                    Width = 80f,
                    Height = 100f,
                    BackgroundColor = Color.Transparent,
                    Parent = _trackSection
                };

                Label levelLabel = new Label
                {
                    Text = $"Lv.{20 + i}",
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 10f),
                    TextColor = InkWashTheme.TextTertiary,
                    Parent = node
                };

                ContainerControl rewardIcon = new ContainerControl
                {
                    Size = new Float2(48, 48),
                    BackgroundColor = InkWashTheme.BaseSecondary,
                    Parent = node
                };

                Label rewardLabel = new Label
                {
                    Text = freeRewardNames[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 10f),
                    TextColor = InkWashTheme.TextSecondary,
                    Parent = node
                };

                _freeNodes.Add(node);
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
            float headerHeight = 180f;

            if (_passHeader != null)
            {
                _passHeader.Location = new Float2(margin, contentTop);
                _passHeader.Size = new Float2(sw - SidebarWidth - margin * 2f, headerHeight);
            }

            if (_passTitle != null) _passTitle.Location = new Float2(margin * 2f, margin);
            if (_passSubtitle != null) _passSubtitle.Location = new Float2(margin * 2f, margin + 40f);
            if (_seasonInfoTime != null) _seasonInfoTime.Location = new Float2(margin * 2f, margin + 70f);
            if (_seasonInfoPlayers != null) _seasonInfoPlayers.Location = new Float2(margin * 2f + 200f, margin + 70f);

            float centerX = (sw - SidebarWidth) * 0.5f;
            if (_currentLevelLabel != null) _currentLevelLabel.Location = new Float2(centerX - 80f, margin);
            if (_progressInfoLabel != null) _progressInfoLabel.Location = new Float2(centerX - 100f, margin + 40f);
            if (_progressBar != null) _progressBar.Location = new Float2(centerX - 100f, margin + 65f);

            float rightPanelX = sw - SidebarWidth - margin - 170f;
            if (_unlockButton != null) _unlockButton.Location = new Float2(rightPanelX, margin);
            if (_upgradeButton != null) _upgradeButton.Location = new Float2(rightPanelX, margin + 54f);

            float bannerTop = contentTop + headerHeight + margin;
            float bannerHeight = 80f;
            if (_unlockBanner != null)
            {
                _unlockBanner.Location = new Float2(margin, bannerTop);
                _unlockBanner.Size = new Float2(sw - SidebarWidth - margin * 2f, bannerHeight);
            }

            if (_unlockTitle != null) _unlockTitle.Location = new Float2(margin * 2f, margin);
            if (_unlockDesc != null) _unlockDesc.Location = new Float2(margin * 2f, margin + 28f);
            if (_unlockActionButton != null) _unlockActionButton.Location = new Float2(sw - SidebarWidth - margin - 140f, bannerHeight / 2f - 20f);

            float trackTop = bannerTop + bannerHeight + margin;
            if (_trackSection != null)
            {
                _trackSection.Location = new Float2(margin, trackTop);
                _trackSection.Size = new Float2(sw - SidebarWidth - margin * 2f, sh - trackTop - margin);
            }

            if (_trackTitle != null) _trackTitle.Location = new Float2(margin, margin);

            float trackContentTop = margin + 40f;
            float nodeGap = 20f;
            float trackWidth = sw - SidebarWidth - margin * 4f;
            float nodeWidth = (trackWidth - nodeGap * 5f) / 6f;

            for (int i = 0; i < _paidNodes.Count; i++)
            {
                if (_paidNodes[i] != null)
                {
                    _paidNodes[i].Location = new Float2(margin + i * (nodeWidth + nodeGap), trackContentTop);
                    _paidNodes[i].Size = new Float2(nodeWidth, 100f);
                }
            }

            for (int i = 0; i < _freeNodes.Count; i++)
            {
                if (_freeNodes[i] != null)
                {
                    _freeNodes[i].Location = new Float2(margin + i * (nodeWidth + nodeGap), trackContentTop + 120f);
                    _freeNodes[i].Size = new Float2(nodeWidth, 100f);
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