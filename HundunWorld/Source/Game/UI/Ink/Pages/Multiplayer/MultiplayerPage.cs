using System;
using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.Ink;
using HundunWorld.Game.UI.Ink.Components;
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

        /// <summary>匹配横幅 meta 行（预计等待 / 匹配范围 / 当前匹配，设计稿 .match-banner-meta）</summary>
        private Panel _matchMeta;

        /// <summary>匹配按钮下方提示“点击进入匹配队列”（设计稿 .match-btn-hint）</summary>
        private Label _matchBtnHint;

        /// <summary>模式面板底部提示“选择模式进入对应大厅”（设计稿 .mode-panel-footer）</summary>
        private Label _modeFooterHint;

        /// <summary>模式面板底部提示图标</summary>
        private Label _modeFooterHintIcon;

        private InkPanel _filterBar;
        private InkButton[] _filterButtons;

        /// <summary>筛选栏“排序”分组标签（设计稿 .filter-group 第三组）</summary>
        private Label _sortGroupLabel;

        /// <summary>筛选栏“排序”分组按钮（人数 / 房间名）</summary>
        private InkButton[] _sortButtons;

        /// <summary>筛选栏右侧刷新按钮（设计稿 #btn-refresh）</summary>
        private InkButton _refreshButton;

        private Panel _roomList;
        private InkPanel[] _roomRows;

        /// <summary>房间列表页脚（共 N 个房间 · 已加载全部 + 加载更多，设计稿 .room-list-footer）</summary>
        private Panel _roomListFooter;

        /// <summary>房间列表页脚“加载更多”按钮（设计稿 #btn-load-more）</summary>
        private InkButton _loadMoreButton;

        public event Action BackRequested;

        private readonly string[] _modeNames = { "快速匹配", "创建房间", "加入房间", "好友列表", "帮派" };
        private readonly string[] _roomNames = Array.Empty<string>();
        private readonly string[] _roomHosts = Array.Empty<string>();
        private readonly string[] _roomModes = Array.Empty<string>();
        private readonly int[] _roomCurrent = Array.Empty<int>();
        private readonly int[] _roomMax = Array.Empty<int>();
        private readonly bool[] _roomPlaying = Array.Empty<bool>();

        public MultiplayerPage()
        {
            // 优先使用自身尺寸（挂载到 InkPageShell 设计坐标系后为 1920×1080）；未就绪时回退屏幕尺寸。
            _screenSize = new Float2(Width > 0f ? Width : FlaxEngine.Screen.Size.X,
                                     Height > 0f ? Height : FlaxEngine.Screen.Size.Y);
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
                // 印章 32×20：StretchAll 会被撑成 132×40，字被裁
                AnchorPreset = AnchorPresets.TopLeft,
                Location = Float2.Zero,
                Size = new Vector2(32f, 20f),
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
                Text = "网络延迟：",
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
                Text = "--",
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
                Text = "服务器：未连接",
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
                Text = "—",
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
                Text = "—",
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
                // 头像框 36×36：同上，StretchAll 实测 136×56
                AnchorPreset = AnchorPresets.TopLeft,
                Location = Float2.Zero,
                Size = new Vector2(36f, 36f),
                Parent = _playerAvatar
            };

            _playerName = new Label
            {
                Text = "—",
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
                Text = "◆ Lv.—",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 11f),
                TextColor = InkWashTheme.GoldBright,
                Width = 60f,
                Height = 16f,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                Location = new Float2(44f, 22f),
                Parent = _playerInfo
            };

            // 真实玩家信息可用时覆盖设计稿示例值（元宝/网络状态等仍为示例）
            InkPlayerHeaderBinder.Bind(_playerName, _playerLevel, _copperValue, levelPrefix: "◆ ");

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
                    BackgroundColor = Color.Transparent,
                    TextColor = InkWashTheme.PaperAged,
                    Enabled = false, // 匹配/大厅服务未接入，模式按钮不做假切换
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
                Text = "—",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 12f),
                TextColor = InkWashTheme.JadeBright,
                Width = 60f,
                Height = 16f,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                Location = new Float2(100f, _modePanel.Height - 56f),
                Parent = _modePanel
            };

            // 模式面板页脚提示：图标 + “选择模式进入对应大厅”（设计稿 .mode-panel-footer）
            _modeFooterHintIcon = new Label
            {
                Text = "◆",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.GoldPrimary,
                Width = 14f,
                Height = 16f,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                Location = new Float2(16f, _modePanel.Height - 32f),
                Parent = _modePanel
            };

            _modeFooterHint = new Label
            {
                Text = "大厅服务尚未开放",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.PaperDark,
                Width = modePanelWidth - 48f,
                Height = 16f,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                Location = new Float2(34f, _modePanel.Height - 32f),
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
            };            InkPanel matchTag = new InkPanel
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
                AnchorPreset = AnchorPresets.TopLeft,
                Location = Float2.Zero,
                Size = new Vector2(48f, 20f),
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
                Text = "匹配系统尚未开放",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 14f),
                Location = new Float2(contentWidth - 164f, 40f),
                BackgroundColor = InkWashTheme.VermilionPrimary,
                TextColor = InkWashTheme.PaperBright,
                Enabled = false, // 没有匹配队列可进，按钮不做"跳回战斗 HUD"的假动作
                Parent = _matchBanner
            };

            // 匹配横幅下方提示（设计稿 .match-btn-hint，与按钮同宽居中）
            _matchBtnHint = new Label
            {
                Text = "匹配队列尚未接入",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.TextTertiary,
                Width = 140f,
                Height = 14f,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                Location = new Float2(contentWidth - 164f, 86f),
                Parent = _matchBanner
            };

            // 匹配横幅 meta 行：预计等待 / 匹配范围 / 当前匹配（设计稿 .match-banner-meta）
            _matchMeta = new Panel
            {
                Width = 480f,
                Height = 16f,
                Location = new Float2(24f, 76f),
                BackgroundColor = Color.Transparent,
                Parent = _matchBanner
            };

            CreateMatchMetaItem(_matchMeta, 0f, "◆", "预计等待", "—");
            CreateMatchMetaDivider(_matchMeta, 150f);
            CreateMatchMetaItem(_matchMeta, 166f, "◆", "匹配范围", "—");
            CreateMatchMetaDivider(_matchMeta, 316f);
            CreateMatchMetaItem(_matchMeta, 332f, "◆", "当前匹配", "—");

            _filterBar = new InkPanel
            {
                Width = contentWidth,
                Height = 56f,
                Location = new Float2(0, 132f),
                BackgroundColor = InkWashTheme.BaseTertiary,
                Parent = _contentArea
            };

            // 设计稿筛选栏分为三个分组（模式 / 状态 / 排序），每组 = 分组标签 + 药丸按钮，
            // 右侧另有刷新按钮（#btn-refresh）。分组标签沿用页面既有 12px 正文色。
            Label modeGroupLabel = new Label
            {
                Text = "模式",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                TextColor = InkWashTheme.PaperFaded,
                Width = 40f,
                Height = 28f,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                Location = new Float2(24f, 14f),
                Parent = _filterBar
            };

            Label statusGroupLabel = new Label
            {
                Text = "状态",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                TextColor = InkWashTheme.PaperFaded,
                Width = 40f,
                Height = 28f,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                Location = new Float2(452f, 14f),
                Parent = _filterBar
            };

            _filterButtons = new InkButton[8];
            string[] filterNames = { "全部", "1v1", "3v3", "5v5", "自由", "全部", "等待中", "进行中" };
            float filterX = 68f;

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

            filterX = 496f;

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

            // 第三分组：排序（人数 / 房间名），位置由 LayoutFilterBarRight 按实际内容宽度贴右排布
            _sortGroupLabel = new Label
            {
                Text = "排序",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                TextColor = InkWashTheme.PaperFaded,
                Width = 40f,
                Height = 28f,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                Parent = _filterBar
            };

            string[] sortNames = { "人数", "房间名" };
            _sortButtons = new InkButton[sortNames.Length];
            for (int i = 0; i < sortNames.Length; i++)
            {
                _sortButtons[i] = new InkButton
                {
                    Width = 60f,
                    Height = 28f,
                    Text = sortNames[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                    BackgroundColor = i == 0 ? new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.12f) : Color.Transparent,
                    TextColor = i == 0 ? InkWashTheme.GoldBright : InkWashTheme.PaperAged,
                    Parent = _filterBar
                };
            }

            _refreshButton = new InkButton
            {
                Width = 36f,
                Height = 28f,
                Text = "↻",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 14f),
                BackgroundColor = Color.Transparent,
                TextColor = InkWashTheme.PaperFaded,
                Parent = _filterBar
            };
            _refreshButton.Clicked += ApplyNetworkStatus;

            LayoutFilterBarRight(contentWidth);

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

            _roomRows = new InkPanel[_roomNames.Length];
            for (int i = 0; i < _roomNames.Length; i++)
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
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = Float2.Zero,
                    Size = new Vector2(50f, 22f),
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

            // 房间列表没有服务端来源：空列表时给一条明确空态，不留一块空白让人以为是没加载出来
            if (_roomNames.Length == 0)
            {
                _ = new Label
                {
                    Text = "暂无房间 · 大厅服务尚未开放",
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 14f),
                    TextColor = InkWashTheme.PaperAged,
                    Width = 360f,
                    Height = 40f,
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                    Location = new Float2(24f, 208f),
                    Parent = _contentArea
                };
            }

            // 房间列表页脚（设计稿 .room-list-footer）：左侧统计信息 + 右侧“加载更多”按钮
            _roomListFooter = new Panel
            {
                Width = contentWidth,
                Height = 32f,
                Location = new Float2(0f, _roomList.Height - 44f),
                BackgroundColor = Color.Transparent,
                Parent = _roomList
            };

            Label footerIcon = new Label
            {
                Text = "◆",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.GoldPrimary,
                Width = 14f,
                Height = 20f,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                Location = new Float2(0f, 6f),
                Parent = _roomListFooter
            };

            Label footerCount = new Label
            {
                Text = $"共 {_roomNames.Length} 个房间",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                TextColor = InkWashTheme.TextTertiary,
                Width = 100f,
                Height = 20f,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                Location = new Float2(18f, 6f),
                Parent = _roomListFooter
            };

            Label footerDot = new Label
            {
                Text = "·",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                TextColor = InkWashTheme.TextTertiary,
                Width = 12f,
                Height = 20f,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                Location = new Float2(122f, 6f),
                Parent = _roomListFooter
            };

            Label footerLoaded = new Label
            {
                Text = "已加载全部",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                TextColor = InkWashTheme.TextTertiary,
                Width = 100f,
                Height = 20f,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                Location = new Float2(136f, 6f),
                Parent = _roomListFooter
            };

            _loadMoreButton = new InkButton
            {
                Width = 100f,
                Height = 28f,
                Text = "加载更多",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                Location = new Float2(contentWidth - 116f, 2f),
                BackgroundColor = Color.Transparent,
                TextColor = InkWashTheme.PaperFaded,
                Enabled = false, // 没有房间数据源，分页按钮不做假加载
                Parent = _roomListFooter
            };
        }

        /// <summary>
        /// 创建匹配横幅 meta 项（设计稿 .meta-item：图标 + 标签 + 数值）。
        /// </summary>
        /// <param name="parent">承载的父控件（匹配横幅 meta 行）</param>
        /// <param name="x">项左边界 X</param>
        /// <param name="icon">图标字形（设计稿 lucide 图标的引擎侧近似）</param>
        /// <param name="label">标签文字（预计等待 / 匹配范围 / 当前匹配）</param>
        /// <param name="value">数值文字（15秒 / Lv. 38-46 / 326人）</param>
        private void CreateMatchMetaItem(Panel parent, float x, string icon, string label, string value)
        {
            Label metaIcon = new Label
            {
                Text = icon,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.Info,
                Width = 14f,
                Height = 16f,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                Location = new Float2(x, 0f),
                Parent = parent
            };

            Label metaLabel = new Label
            {
                Text = label,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                TextColor = InkWashTheme.PaperFaded,
                Width = 56f,
                Height = 16f,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                Location = new Float2(x + 18f, 0f),
                Parent = parent
            };

            Label metaValue = new Label
            {
                Text = value,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 12f),
                TextColor = InkWashTheme.PaperBright,
                Width = 72f,
                Height = 16f,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                Location = new Float2(x + 76f, 0f),
                Parent = parent
            };
        }

        /// <summary>
        /// 创建匹配横幅 meta 项之间的分隔线（设计稿 .meta-divider：1px × 12px）。
        /// </summary>
        /// <param name="parent">承载的父控件（匹配横幅 meta 行）</param>
        /// <param name="x">分隔线 X 坐标</param>
        private void CreateMatchMetaDivider(Panel parent, float x)
        {
            Panel metaDivider = new Panel
            {
                Width = 1f,
                Height = 12f,
                Location = new Float2(x, 2f),
                BackgroundColor = InkWashTheme.TextTertiary,
                Parent = parent
            };
        }

        /// <summary>
        /// 按内容区实际宽度贴右排布筛选栏第三分组（排序）与刷新按钮。
        /// 排序分组宽度 = 标签 40 + 两枚 60px 药丸（间距 12），与刷新按钮间隔 16px。
        /// </summary>
        /// <param name="contentWidth">内容区当前宽度</param>
        private void LayoutFilterBarRight(float contentWidth)
        {
            if (_refreshButton == null)
                return;

            float refreshX = contentWidth - 24f - _refreshButton.Width;
            _refreshButton.Location = new Float2(refreshX, 14f);

            const float sortWidth = 40f + 60f + 12f + 60f;
            float sortX = refreshX - 16f - sortWidth;

            if (_sortGroupLabel != null)
                _sortGroupLabel.Location = new Float2(sortX, 14f);

            if (_sortButtons != null)
            {
                for (int i = 0; i < _sortButtons.Length; i++)
                    _sortButtons[i].Location = new Float2(sortX + 40f + i * 72f, 14f);
            }
        }

        public void RefreshLayout()
        {
            // 分辨率适配修复：以页面自身尺寸（InkPageShell 内容层 = 1920×1080 设计空间）为准，
            // 不再无条件使用窗口尺寸——否则内容层等比缩放后页面坐标系将与窗口坐标系错位。
            _screenSize = new Float2(Width > 0f ? Width : FlaxEngine.Screen.Size.X,
                                     Height > 0f ? Height : FlaxEngine.Screen.Size.Y);
            if (_screenSize.X <= 0f || _screenSize.Y <= 0f)
            {
                _screenSize = new Float2(1920f, 1080f);
            }
            Size = _screenSize;

            ApplyNetworkStatus();

            _bgLayer.Size = _screenSize;
            _topBar.Size = new Float2(_screenSize.X, 60f);

            // 网络状态条原来只在构造期按当时的窗口宽度定位：页面尺寸变化（Ink 内容层 = 1920×1080）
            // 后它不会重排，会压到左上角的标题上。这里按真实页面宽度重新居中。
            if (_netStatus != null)
                _netStatus.Location = new Float2((_screenSize.X - _netStatus.Width) * 0.5f, 16f);

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
                LayoutFilterBarRight(contentWidth);
            }

            if (_modeFooterHint != null)
            {
                _modeFooterHint.Location = new Float2(34f, _modePanel.Height - 32f);
            }

            if (_modeFooterHintIcon != null)
            {
                _modeFooterHintIcon.Location = new Float2(16f, _modePanel.Height - 32f);
            }

            if (_roomList != null)
            {
                _roomList.Size = new Float2(contentWidth, _screenSize.Y - 260f);
            }

            if (_roomListFooter != null)
            {
                _roomListFooter.Size = new Float2(contentWidth, 32f);
                _roomListFooter.Location = new Float2(0f, _roomList.Height - 44f);
            }

            if (_loadMoreButton != null)
            {
                _loadMoreButton.Location = new Float2(contentWidth - 116f, 2f);
            }

            if (_matchBtnHint != null)
            {
                _matchBtnHint.Location = new Float2(contentWidth - 164f, 86f);
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

        private float _netRefreshTimer;

        /// <summary>顶栏时延每秒刷新一次（实时 RTT 由同步链路逐包更新，不刷就停在进页那一刻）。</summary>
        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);
            if (!VisibleInHierarchy) return;
            _netRefreshTimer += deltaTime;
            if (_netRefreshTimer < 1f) return;
            _netRefreshTimer = 0f;
            ApplyNetworkStatus();
        }

        /// <summary>
        /// 顶栏网络状态取当前网关的真实信息（区域名 + 选路时测得的时延）。
        /// 没有网关或时延未测得时保持「—」，不摆设计稿的「华东一区 / 45ms」。
        /// </summary>
        private void ApplyNetworkStatus()
        {
            try
            {
                InkPlayerHeaderBinder.GetGatewayText(out string region, out string latency);

                if (_netServer != null)
                    _netServer.Text = "服务器：" + region;

                // 有实时 RTT 就用实时的（同步链路每包都会更新）；没有才回落到选路时测得的静态值
                float rtt = HundunWorld.Game.Network.ClientSyncMetrics.EstimatedRttMs;
                if (_netValue != null)
                {
                    // 单位列用 ASCII「--」占位：11–13f 下「—」没有字形，会显示成空
                    _netValue.Text = rtt > 0f
                        ? $"{Mathf.RoundToInt(rtt)} ms"
                        : (latency == "未测得" ? "--" : latency);
                }
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogWarning($"[MultiplayerPage] 网络状态读取失败: {ex.Message}");
            }
        }
    }
}