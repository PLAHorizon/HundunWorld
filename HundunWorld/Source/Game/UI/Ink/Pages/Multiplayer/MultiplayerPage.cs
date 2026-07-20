using System;
using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.Ink;
using HundunWorld.Game.UI.StyleSystem;

namespace HundunWorld.Game.UI.Ink.Pages.Multiplayer
{
    public class MultiplayerPage : Panel, IInkPage
    {
        private Float2 _screenSize;

        private Panel _bgLayer;

        private Panel _topBar;
        private InkButton _backButton;
        private Label _topTitle;
        private InkPanel _topSeal;
        private Label _topSealText;

        private Panel _netStatus;
        private Panel _netDot;
        private Label _netLabel;
        private Label _netValue;
        private Label _netServer;

        private Panel _currencyDisplay;
        private Label _copperIcon;
        private Label _copperValue;
        private Label _goldIcon;
        private Label _goldValue;

        private Panel _playerInfo;
        private InkPanel _playerAvatar;
        private Label _playerAvatarIcon;
        private Label _playerName;
        private Label _playerLevel;

        private InkPanel _modePanel;
        private InkButton[] _modeButtons;

        private InkPanel _contentArea;

        private InkPanel _matchBanner;
        private InkButton _matchButton;

        private InkPanel _filterBar;
        private InkButton[] _filterButtons;

        private Panel _roomList;
        private InkPanel[] _roomRows;

        public event Action BackRequested;
        public event Action StartMatchRequested;

        private readonly string[] _modeNames = { "快速匹配", "创建房间", "加入房间", "好友列表", "帮派" };
        private readonly string[] _roomNames = { "华山论剑", "帮派对战", "自由切磋", "夺宝奇兵", "江湖擂台", "阵营对抗" };
        private readonly string[] _roomHosts = { "独孤求败", "丐帮帮主", "风清扬", "摸金校尉", "东方不败", "明教教主" };
        private readonly string[] _roomModes = { "3v3", "5v5", "1v1", "4人", "1v1", "10v10" };
        private readonly int[] _roomCurrent = { 5, 8, 1, 3, 2, 12 };
        private readonly int[] _roomMax = { 6, 10, 2, 4, 2, 20 };
        private readonly bool[] _roomPlaying = { true, false, false, false, true, false };

        public MultiplayerPage()
        {
            _screenSize = FlaxEngine.Screen.Size;
            if (_screenSize.X <= 0f || _screenSize.Y <= 0f)
            {
                _screenSize = new Float2(1920f, 1080f);
            }

            AnchorPreset = AnchorPresets.StretchAll;
            BackgroundColor = InkWashTheme.BaseDefault;
            ClipChildren = false;
            Location = Float2.Zero;
            Size = _screenSize;

            try
            {
                BuildLayout();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[MultiplayerPage] 初始化失败: {ex.Message}");
            }
        }

        private void BuildLayout()
        {
            _bgLayer = new Panel
            {
                AnchorPreset = AnchorPresets.StretchAll,
                BackgroundColor = InkWashTheme.BaseSecondary,
                Parent = this
            };

            _topBar = new Panel
            {
                Width = _screenSize.X,
                Height = 60f,
                Location = new Float2(0, 0),
                BackgroundColor = new Color(InkWashTheme.BaseDefault.R, InkWashTheme.BaseDefault.G, InkWashTheme.BaseDefault.B, 0.95f),
                Parent = this
            };

            Panel topBarBorder = new Panel
            {
                Width = _screenSize.X,
                Height = 1f,
                Location = new Float2(0, 59f),
                BackgroundColor = InkWashTheme.BorderGold,
                Parent = _topBar
            };

            _backButton = new InkButton
            {
                Width = 36f,
                Height = 36f,
                Text = "<",
                Location = new Float2(20f, 12f),
                BackgroundColor = Color.Transparent,
                TextColor = InkWashTheme.PaperBright,
                Parent = _topBar
            };
            _backButton.Clicked += () => BackRequested?.Invoke();

            _topTitle = new Label
            {
                Text = "多人模式",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 20f),
                TextColor = InkWashTheme.GoldBright,
                Width = 120f,
                Height = 24f,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                Location = new Float2(64f, 18f),
                Parent = _topBar
            };

            _topSeal = new InkPanel
            {
                Width = 32f,
                Height = 20f,
                Location = new Float2(180f, 20f),
                BackgroundColor = new Color(InkWashTheme.VermilionPrimary.R, InkWashTheme.VermilionPrimary.G, InkWashTheme.VermilionPrimary.B, 0.1f),
                Parent = _topBar
            };

            _topSealText = new Label
            {
                Text = "江湖",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 11f),
                TextColor = InkWashTheme.VermilionPrimary,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.StretchAll,
                Parent = _topSeal
            };

            _netStatus = new Panel
            {
                Width = 240f,
                Height = 28f,
                Location = new Float2(_screenSize.X * 0.5f - 120f, 16f),
                BackgroundColor = new Color(InkWashTheme.BaseDefault.R, InkWashTheme.BaseDefault.G, InkWashTheme.BaseDefault.B, 0.5f),
                Parent = _topBar
            };

            _netDot = new Panel
            {
                Width = 8f,
                Height = 8f,
                Location = new Float2(12f, 10f),
                BackgroundColor = InkWashTheme.JadePrimary,
                Parent = _netStatus
            };

            _netLabel = new Label
            {
                Text = "网络延迟:",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                TextColor = InkWashTheme.PaperFaded,
                Width = 80f,
                Height = 16f,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                Location = new Float2(24f, 6f),
                Parent = _netStatus
            };

            _netValue = new Label
            {
                Text = "45ms",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 13f),
                TextColor = InkWashTheme.JadeBright,
                Width = 40f,
                Height = 16f,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                Location = new Float2(100f, 6f),
                Parent = _netStatus
            };

            _netServer = new Label
            {
                Text = "服务器: 华东一区",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                TextColor = InkWashTheme.PaperAged,
                Width = 120f,
                Height = 16f,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                Location = new Float2(148f, 6f),
                Parent = _netStatus
            };

            _currencyDisplay = new Panel
            {
                Width = 140f,
                Height = 24f,
                Location = new Float2(_screenSize.X - 360f, 18f),
                Parent = _topBar
            };

            _copperIcon = new Label
            {
                Text = "¤",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 14f),
                TextColor = InkWashTheme.GoldPrimary,
                Width = 16f,
                Height = 20f,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                Location = new Float2(0, 2f),
                Parent = _currencyDisplay
            };

            _copperValue = new Label
            {
                Text = "12,830",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 13f),
                TextColor = InkWashTheme.PaperBright,
                Width = 60f,
                Height = 20f,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                Location = new Float2(20f, 2f),
                Parent = _currencyDisplay
            };

            _goldIcon = new Label
            {
                Text = "◆",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 14f),
                TextColor = InkWashTheme.GoldBright,
                Width = 16f,
                Height = 20f,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                Location = new Float2(88f, 2f),
                Parent = _currencyDisplay
            };

            _goldValue = new Label
            {
                Text = "328",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 13f),
                TextColor = InkWashTheme.PaperBright,
                Width = 40f,
                Height = 20f,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                Location = new Float2(108f, 2f),
                Parent = _currencyDisplay
            };

            _playerInfo = new Panel
            {
                Width = 140f,
                Height = 40f,
                Location = new Float2(_screenSize.X - 200f, 10f),
                Parent = _topBar
            };

            _playerAvatar = new InkPanel
            {
                Width = 36f,
                Height = 36f,
                Location = new Float2(0, 2f),
                BackgroundColor = InkWashTheme.BaseElevated,
                Parent = _playerInfo
            };

            _playerAvatarIcon = new Label
            {
                Text = "◎",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 18f),
                TextColor = InkWashTheme.GoldBright,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.StretchAll,
                Parent = _playerAvatar
            };

            _playerName = new Label
            {
                Text = "江湖过客",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 13f),
                TextColor = InkWashTheme.PaperBright,
                Width = 90f,
                Height = 16f,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                Location = new Float2(44f, 4f),
                Parent = _playerInfo
            };

            _playerLevel = new Label
            {
                Text = "★ Lv. 42",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 11f),
                TextColor = InkWashTheme.GoldBright,
                Width = 60f,
                Height = 16f,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                Location = new Float2(44f, 22f),
                Parent = _playerInfo
            };

            float panelWidth = Math.Min(_screenSize.X - 64f, 1400f);
            float modePanelWidth = 200f;
            float contentWidth = panelWidth - modePanelWidth - 24f;

            _modePanel = new InkPanel
            {
                Width = modePanelWidth,
                Height = _screenSize.Y - 100f,
                Location = new Float2(32f, 72f),
                Parent = this
            };

            Label modeHeaderText = new Label
            {
                Text = "模式选择",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 13f),
                TextColor = InkWashTheme.PaperAged,
                Width = 100f,
                Height = 20f,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                Location = new Float2(16f, 16f),
                Parent = _modePanel
            };

            InkDivider modeDivider = new InkDivider
            {
                Width = modePanelWidth - 32f,
                Height = 1f,
                Location = new Float2(16f, 40f),
                Parent = _modePanel
            };

            _modeButtons = new InkButton[5];
            for (int i = 0; i < 5; i++)
            {
                _modeButtons[i] = new InkButton
                {
                    Width = modePanelWidth - 32f,
                    Height = 44f,
                    Text = _modeNames[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 15f),
                    Location = new Float2(16f, 56f + i * 48f),
                    BackgroundColor = i == 0 ? new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.1f) : Color.Transparent,
                    TextColor = i == 0 ? InkWashTheme.GoldBright : InkWashTheme.PaperAged,
                    Parent = _modePanel
                };
            }

            Label onlineText = new Label
            {
                Text = "当前在线",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                TextColor = InkWashTheme.PaperFaded,
                Width = 80f,
                Height = 16f,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                Location = new Float2(28f, _modePanel.Height - 56f),
                Parent = _modePanel
            };

            Label onlineCount = new Label
            {
                Text = "1,247",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 12f),
                TextColor = InkWashTheme.JadeBright,
                Width = 60f,
                Height = 16f,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                Location = new Float2(100f, _modePanel.Height - 56f),
                Parent = _modePanel
            };

            _contentArea = new InkPanel
            {
                Width = contentWidth,
                Height = _screenSize.Y - 100f,
                Location = new Float2(32f + modePanelWidth + 24f, 72f),
                BackgroundColor = Color.Transparent,
                Parent = this
            };

            _matchBanner = new InkPanel
            {
                Width = contentWidth,
                Height = 120f,
                Location = new Float2(0, 0),
                BackgroundColor = InkWashTheme.BaseTertiary,
                Parent = _contentArea
            };

            Label matchTitle = new Label
            {
                Text = "快速匹配",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 20f),
                TextColor = InkWashTheme.PaperBright,
                Width = 120f,
                Height = 24f,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                Location = new Float2(24f, 20f),
                Parent = _matchBanner
            };

            InkPanel matchTag = new InkPanel
            {
                Width = 48f,
                Height = 20f,
                Location = new Float2(150f, 22f),
                BackgroundColor = new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.12f),
                Parent = _matchBanner
            };

            Label matchTagText = new Label
            {
                Text = "推荐",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.GoldBright,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.StretchAll,
                Parent = matchTag
            };

            Label matchDesc = new Label
            {
                Text = "自动匹配实力相当的对手，畅享江湖对决",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                TextColor = InkWashTheme.PaperAged,
                Width = 300f,
                Height = 16f,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                Location = new Float2(24f, 52f),
                Parent = _matchBanner
            };

            _matchButton = new InkButton
            {
                Width = 140f,
                Height = 40f,
                Text = "开始匹配",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 14f),
                Location = new Float2(contentWidth - 164f, 40f),
                BackgroundColor = InkWashTheme.VermilionPrimary,
                TextColor = InkWashTheme.PaperBright,
                Parent = _matchBanner
            };
            _matchButton.Clicked += () => StartMatchRequested?.Invoke();

            _filterBar = new InkPanel
            {
                Width = contentWidth,
                Height = 56f,
                Location = new Float2(0, 132f),
                BackgroundColor = InkWashTheme.BaseTertiary,
                Parent = _contentArea
            };

            _filterButtons = new InkButton[8];
            string[] filterNames = { "全部", "1v1", "3v3", "5v5", "自由", "全部", "等待中", "进行中" };
            float filterX = 24f;

            for (int i = 0; i < 5; i++)
            {
                _filterButtons[i] = new InkButton
                {
                    Width = 60f,
                    Height = 28f,
                    Text = filterNames[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                    Location = new Float2(filterX, 14f),
                    BackgroundColor = i == 0 ? new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.12f) : Color.Transparent,
                    TextColor = i == 0 ? InkWashTheme.GoldBright : InkWashTheme.PaperAged,
                    Parent = _filterBar
                };
                filterX += 72f;
            }

            filterX += 20f;

            for (int i = 5; i < 8; i++)
            {
                _filterButtons[i] = new InkButton
                {
                    Width = 60f,
                    Height = 28f,
                    Text = filterNames[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                    Location = new Float2(filterX, 14f),
                    BackgroundColor = i == 5 ? new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.12f) : Color.Transparent,
                    TextColor = i == 5 ? InkWashTheme.GoldBright : InkWashTheme.PaperAged,
                    Parent = _filterBar
                };
                filterX += 72f;
            }

            _roomList = new Panel
            {
                Width = contentWidth,
                Height = _screenSize.Y - 260f,
                Location = new Float2(0, 200f),
                Parent = _contentArea
            };

            Label roomHeaderName = new Label
            {
                Text = "房间名",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 12f),
                TextColor = InkWashTheme.TextTertiary,
                Width = 200f,
                Height = 24f,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                Location = new Float2(0, 0),
                Parent = _roomList
            };

            Label roomHeaderMode = new Label
            {
                Text = "模式",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 12f),
                TextColor = InkWashTheme.TextTertiary,
                Width = 80f,
                Height = 24f,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                Location = new Float2(200f, 0),
                Parent = _roomList
            };

            Label roomHeaderPlayers = new Label
            {
                Text = "人数",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 12f),
                TextColor = InkWashTheme.TextTertiary,
                Width = 120f,
                Height = 24f,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                Location = new Float2(280f, 0),
                Parent = _roomList
            };

            Label roomHeaderStatus = new Label
            {
                Text = "状态",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 12f),
                TextColor = InkWashTheme.TextTertiary,
                Width = 80f,
                Height = 24f,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                Location = new Float2(400f, 0),
                Parent = _roomList
            };

            Label roomHeaderAction = new Label
            {
                Text = "操作",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 12f),
                TextColor = InkWashTheme.TextTertiary,
                Width = 100f,
                Height = 24f,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                Location = new Float2(contentWidth - 100f, 0),
                Parent = _roomList
            };

            _roomRows = new InkPanel[6];
            for (int i = 0; i < 6; i++)
            {
                _roomRows[i] = new InkPanel
                {
                    Width = contentWidth,
                    Height = 60f,
                    Location = new Float2(0, 32f + i * 68f),
                    BackgroundColor = InkWashTheme.BaseSecondary,
                    Parent = _roomList
                };

                Label roomName = new Label
                {
                    Text = _roomNames[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 14f),
                    TextColor = InkWashTheme.PaperBright,
                    Width = 120f,
                    Height = 20f,
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                    Location = new Float2(24f, 12f),
                    Parent = _roomRows[i]
                };

                Label roomHost = new Label
                {
                    Text = $"房主: {_roomHosts[i]}",
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                    TextColor = InkWashTheme.PaperFaded,
                    Width = 120f,
                    Height = 16f,
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                    Location = new Float2(24f, 34f),
                    Parent = _roomRows[i]
                };

                InkPanel roomModeTag = new InkPanel
                {
                    Width = 50f,
                    Height = 22f,
                    Location = new Float2(220f, 19f),
                    BackgroundColor = new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.1f),
                    Parent = _roomRows[i]
                };

                Label roomModeText = new Label
                {
                    Text = _roomModes[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                    TextColor = InkWashTheme.GoldBright,
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                    AnchorPreset = AnchorPresets.StretchAll,
                    Parent = roomModeTag
                };

                Label playerCount = new Label
                {
                    Text = $"{_roomCurrent[i]}/{_roomMax[i]}",
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 12f),
                    TextColor = InkWashTheme.PaperBright,
                    Width = 60f,
                    Height = 16f,
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                    Location = new Float2(310f, 14f),
                    Parent = _roomRows[i]
                };

                Panel countBar = new Panel
                {
                    Width = 80f,
                    Height = 4f,
                    Location = new Float2(290f, 36f),
                    BackgroundColor = new Color(0, 0, 0, 0.4f),
                    Parent = _roomRows[i]
                };

                Panel countBarFill = new Panel
                {
                    Width = (float)_roomCurrent[i] / _roomMax[i] * 80f,
                    Height = 4f,
                    Location = new Float2(0, 0),
                    BackgroundColor = InkWashTheme.GoldPrimary,
                    Parent = countBar
                };

                Label statusText = new Label
                {
                    Text = _roomPlaying[i] ? "进行中" : "等待中",
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                    TextColor = _roomPlaying[i] ? InkWashTheme.VermilionBright : InkWashTheme.JadeBright,
                    Width = 60f,
                    Height = 16f,
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                    Location = new Float2(410f, 22f),
                    Parent = _roomRows[i]
                };

                InkButton actionButton = new InkButton
                {
                    Width = 70f,
                    Height = 30f,
                    Text = _roomPlaying[i] ? "观战" : "加入",
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                    Location = new Float2(contentWidth - 90f, 15f),
                    BackgroundColor = _roomPlaying[i] ? Color.Transparent : InkWashTheme.GoldPrimary,
                    TextColor = _roomPlaying[i] ? InkWashTheme.PaperAged : InkWashTheme.TextOnBrand,
                    Parent = _roomRows[i]
                };
            }
        }

        public void RefreshLayout()
        {
            _screenSize = FlaxEngine.Screen.Size;
            if (_screenSize.X <= 0f || _screenSize.Y <= 0f)
            {
                _screenSize = new Float2(1920f, 1080f);
            }
            Size = _screenSize;

            _bgLayer.Size = _screenSize;
            _topBar.Size = new Float2(_screenSize.X, 60f);

            float panelWidth = Math.Min(_screenSize.X - 64f, 1400f);
            float modePanelWidth = 200f;
            float contentWidth = panelWidth - modePanelWidth - 24f;

            if (_modePanel != null)
            {
                _modePanel.Size = new Float2(modePanelWidth, _screenSize.Y - 100f);
            }

            if (_contentArea != null)
            {
                _contentArea.Location = new Float2(32f + modePanelWidth + 24f, 72f);
                _contentArea.Size = new Float2(contentWidth, _screenSize.Y - 100f);
            }

            if (_matchBanner != null)
            {
                _matchBanner.Size = new Float2(contentWidth, 120f);
                _matchButton.Location = new Float2(contentWidth - 164f, 40f);
            }

            if (_filterBar != null)
            {
                _filterBar.Size = new Float2(contentWidth, 56f);
            }

            if (_roomList != null)
            {
                _roomList.Size = new Float2(contentWidth, _screenSize.Y - 260f);
            }

            if (_roomRows != null)
            {
                for (int i = 0; i < _roomRows.Length; i++)
                {
                    _roomRows[i].Size = new Float2(contentWidth, 60f);
                    var actionBtn = _roomRows[i].Children[5] as InkButton;
                    if (actionBtn != null)
                    {
                        actionBtn.Location = new Float2(contentWidth - 90f, 15f);
                    }
                }
            }
        }

        public void BuildUI() { }

        public void RefreshBoundData() { }
    }
}