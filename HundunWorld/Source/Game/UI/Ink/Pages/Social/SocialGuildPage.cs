using FlaxEngine;
using FlaxEngine.GUI;
using Game.Character.Attributes;
using HundunWorld.Game.UI.StyleSystem;
using System;

namespace HundunWorld.Game.UI.Ink.Pages.Social
{
    public class SocialGuildPage : ContainerControl, IInkPage
    {
        // ===================================================================
        // Layout constants
        // =======================================================================

        private const float HeaderHeight = 56f;
        private const float ScreenEdge = 0f;
        private const float RegionGap = 0f;
        private const float LeftPanelWidth = 450f;

        // ===================================================================
        // Child control references — Header
        // =======================================================================

        private ContainerControl _headerBar;
        private Label _titleLabel;
        private InkButton _tabFriends;
        private InkButton _tabSect;
        private InkButton _tabChat;
        private InkButton _tabTeam;
        private InkButton _closeButton;

        // ===================================================================
        // Child control references — Left panel
        // =======================================================================

        private ContainerControl _leftPanel;
        private Label _friendsListTitle;
        private Label _searchInput;
        private Label _onlineGroupHeader;
        private Label[] _onlineFriendItems;
        private Label _offlineGroupHeader;
        private Label[] _offlineFriendItems;
        private InkPaperPanel _sectCard;
        private InkPaperPanel _guildCard;
        private InkButton _guildManageButton;

        // ===================================================================
        // Child control references — Right panel
        // =======================================================================

        private ContainerControl _rightPanel;
        private InkPaperPanel _playerCard;
        private Label _playerAvatar;
        private Label _playerName;
        private Label _playerLevelTag;
        private Label _playerSectTag;
        private Label _playerPower;
        private Label _playerOnlineStatus;
        private Label _playerLocation;
        private InkButton _btnWhisper;
        private InkButton _btnTeamInvite;
        private InkButton _btnTrade;
        private InkButton _btnDeleteFriend;

        private ContainerControl _chatWindow;
        private Label _chWorld;
        private Label _chTeam;
        private Label _chSect;
        private Label _chWhisper;
        private Label _chZone;
        private Label[] _chatMessages;
        private ContainerControl _chatInputBar;
        private Label _chatInputField;
        private InkButton _chatSendButton;

        // ===================================================================
        // Public API
        // =======================================================================

        public event Action<string> NavigationRequested;
        public InkParticleSystem ParticleSystem { get; set; }
        private CharacterAttributesComponent _boundCharacter;

        public void BindCharacter(CharacterAttributesComponent component)
        {
            _boundCharacter = component;
        }

        // ===================================================================
        // Constructor
        // =======================================================================

        public SocialGuildPage()
        {
            try
            {
                AnchorPreset = AnchorPresets.StretchAll;
                Offsets = Margin.Zero;
                BackgroundColor = InkWashTheme.Abyss;
                ClipChildren = false;
                AutoFocus = false;

                BuildHeader();
                BuildLeftPanel();
                BuildRightPanel();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SocialGuildPage] Init failed: {ex.Message}");
            }
        }

        // ===================================================================
        // Build methods
        // =======================================================================

        private void BuildHeader()
        {
            _headerBar = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = new Color(20f / 255f, 23f / 255f, 30f / 255f, 0.85f),
            };

            _titleLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(24f, 0f),
                Size = new Float2(80f, HeaderHeight),
                Text = "江湖",
                TextColor = InkWashTheme.GoldPrimary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 24f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _headerBar.AddChild(_titleLabel);

            float tabStartX = 120f;
            string[] tabLabels = { "好友", "门派", "聊天", "队伍" };
            string[] tabDomIds = { "tab-friends", "tab-sect", "tab-chat", "tab-team" };
            var tabButtons = new[] { _tabFriends, _tabSect, _tabChat, _tabTeam };

            for (int i = 0; i < tabLabels.Length; i++)
            {
                var tab = new InkButton
                {
                    Variant = InkButtonVariant.Ghost,
                    ButtonSize = InkButtonSize.Sm,
                    Text = tabLabels[i],
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(tabStartX + i * 80f, (HeaderHeight - 28f) * 0.5f),
                    Size = new Float2(72f, 28f),
                    TextColor = InkWashTheme.TextSecondary,
                };
                string captured = tabDomIds[i];
                tab.ButtonClicked += (b) => NavigationRequested?.Invoke(captured);
                _headerBar.AddChild(tab);
                tabButtons[i] = tab;
            }
            _tabFriends = tabButtons[0];
            _tabSect = tabButtons[1];
            _tabChat = tabButtons[2];
            _tabTeam = tabButtons[3];

            _tabSect.TextColor = InkWashTheme.GoldPrimary;

            _closeButton = new InkButton
            {
                Variant = InkButtonVariant.Ghost,
                ButtonSize = InkButtonSize.Sm,
                Text = "✕",
                AnchorPreset = AnchorPresets.TopRight,
                Size = new Float2(32f, 32f),
            };
            _closeButton.ButtonClicked += (b) => NavigationRequested?.Invoke(InkPageDomIds.CombatHud);
            _headerBar.AddChild(_closeButton);

            AddChild(_headerBar);
        }

        private void BuildLeftPanel()
        {
            _leftPanel = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = new Color(20f / 255f, 23f / 255f, 30f / 255f, 0.85f),
            };

            // --- Friends list section ---
            _friendsListTitle = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(16f, 16f),
                Size = new Float2(200f, 20f),
                Text = "好友列表",
                TextColor = InkWashTheme.TextSecondary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 13f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _leftPanel.AddChild(_friendsListTitle);

            _searchInput = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(LeftPanelWidth - 156f, 16f),
                Size = new Float2(140f, 28f),
                Text = " 🔍 搜索好友",
                TextColor = InkWashTheme.TextTertiary,
                BackgroundColor = InkWashTheme.BaseTertiary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _leftPanel.AddChild(_searchInput);

            // Online group header
            _onlineGroupHeader = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(16f, 56f),
                Size = new Float2(LeftPanelWidth - 32f, 32f),
                Text = "▶  在线 (5)",
                TextColor = InkWashTheme.TextDefault,
                BackgroundColor = new Color(200f / 255f, 168f / 255f, 88f / 255f, 0.04f),
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 13f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _leftPanel.AddChild(_onlineGroupHeader);

            string[] onlineNames = { "剑客张三", "飞燕李四", "青衣王五", "孤剑赵九", "紫霞钱十" };
            string[] onlineLevels = { "Lv.60", "Lv.55", "Lv.48", "Lv.52", "Lv.45" };
            string[] onlineSects = { "武当派", "丐帮", "峨眉", "少林", "唐门" };
            _onlineFriendItems = new Label[onlineNames.Length];

            for (int i = 0; i < onlineNames.Length; i++)
            {
                var item = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(16f, 92f + i * 52f),
                    Size = new Float2(LeftPanelWidth - 32f, 48f),
                    Text = $"●  {onlineNames[i]}  Lv.{onlineLevels[i].Replace("Lv.", "")}\n   {onlineSects[i]}",
                    TextColor = InkWashTheme.TextDefault,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Near,
                    BackgroundColor = i == 0 ? new Color(200f / 255f, 168f / 255f, 88f / 255f, 0.1f) : Color.Transparent,
                };
                _onlineFriendItems[i] = item;
                _leftPanel.AddChild(item);
            }

            // Offline group header
            float offlineY = 92f + onlineNames.Length * 52f + 8f;
            _offlineGroupHeader = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(16f, offlineY),
                Size = new Float2(LeftPanelWidth - 32f, 32f),
                Text = "▶  离线 (12)",
                TextColor = InkWashTheme.TextDefault,
                BackgroundColor = new Color(200f / 255f, 168f / 255f, 88f / 255f, 0.04f),
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 13f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _leftPanel.AddChild(_offlineGroupHeader);

            string[] offlineNames = { "铁掌赵六", "玉面孙七", "狂刀周八", "灵狐吴十", "破军郑二", "飞花冯三" };
            string[] offlineLevels = { "Lv.42", "Lv.38", "Lv.35", "Lv.40", "Lv.44", "Lv.37" };
            string[] offlineLastSeen = { "5小时前在线", "昨天在线", "3天前在线", "1周前在线", "2周前在线", "1月前在线" };
            _offlineFriendItems = new Label[offlineNames.Length];

            for (int i = 0; i < offlineNames.Length; i++)
            {
                var item = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(16f, offlineY + 36f + i * 48f),
                    Size = new Float2(LeftPanelWidth - 32f, 44f),
                    Text = $"○  {offlineNames[i]}  {offlineLevels[i]}\n   {offlineLastSeen[i]}",
                    TextColor = InkWashTheme.TextSecondary,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Near,
                };
                _offlineFriendItems[i] = item;
                _leftPanel.AddChild(item);
            }

            // Divider
            float dividerY = offlineY + 36f + offlineNames.Length * 48f + 8f;
            var divider = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(16f, dividerY),
                Size = new Float2(LeftPanelWidth - 32f, 1f),
                BackgroundColor = InkWashTheme.BorderNeutralL2,
            };
            _leftPanel.AddChild(divider);

            // --- Sect info card ---
            float cardY = dividerY + 16f;
            _sectCard = new InkPaperPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(16f, cardY),
                Size = new Float2(LeftPanelWidth - 32f, 120f),
            };

            var sectCardTitle = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, 10f),
                Size = new Float2(300f, 24f),
                Text = "门派信息",
                TextColor = InkWashTheme.GoldPrimary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 16f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _sectCard.AddChild(sectCardTitle);

            AddSectCardRow(_sectCard, "门派", "武当派", 38f, InkWashTheme.TextDefault);
            AddSectCardRow(_sectCard, "职位", "弟子", 58f, InkWashTheme.TextDefault);
            AddSectCardRow(_sectCard, "贡献", "2,500", 78f, InkWashTheme.GoldPrimary);
            _leftPanel.AddChild(_sectCard);

            // --- Guild info card ---
            float guildCardY = cardY + 120f + 12f;
            _guildCard = new InkPaperPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(16f, guildCardY),
                Size = new Float2(LeftPanelWidth - 32f, 140f),
            };

            var guildCardTitle = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, 10f),
                Size = new Float2(200f, 24f),
                Text = "帮会信息",
                TextColor = InkWashTheme.GoldPrimary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 16f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _guildCard.AddChild(guildCardTitle);

            _guildManageButton = new InkButton
            {
                Variant = InkButtonVariant.Default,
                ButtonSize = InkButtonSize.Sm,
                Text = "帮会管理",
                AnchorPreset = AnchorPresets.TopRight,
                Size = new Float2(80f, 28f),
            };
            _guildManageButton.ButtonClicked += (b) => NavigationRequested?.Invoke("btn-guild-manage");
            _guildCard.AddChild(_guildManageButton);

            AddSectCardRow(_guildCard, "帮会", "天下会", 38f, InkWashTheme.TextDefault);
            AddSectCardRow(_guildCard, "等级", "3级 (300/500)", 58f, InkWashTheme.TextDefault);
            AddSectCardRow(_guildCard, "职位", "精英", 78f, InkWashTheme.TextDefault);
            _leftPanel.AddChild(_guildCard);

            AddChild(_leftPanel);
        }

        private void AddSectCardRow(ContainerControl parent, string label, string value, float y, Color valueColor)
        {
            var lbl = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, y),
                Size = new Float2(80f, 20f),
                Text = label,
                TextColor = InkWashTheme.TextSecondary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            parent.AddChild(lbl);

            var val = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(200f, y),
                Size = new Float2(200f, 20f),
                Text = value,
                TextColor = valueColor,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
            };
            parent.AddChild(val);
        }

        private void BuildRightPanel()
        {
            _rightPanel = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = InkWashTheme.Void,
            };

            // --- Player info card ---
            _playerCard = new InkPaperPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, 0f),
                Size = new Float2(400f, 80f),
            };

            _playerAvatar = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(16f, 12f),
                Size = new Float2(56f, 56f),
                Text = "张",
                TextColor = InkWashTheme.GoldPrimary,
                BackgroundColor = new Color(200f / 255f, 168f / 255f, 88f / 255f, 0.12f),
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 22f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };
            _playerCard.AddChild(_playerAvatar);

            _playerName = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(84f, 12f),
                Size = new Float2(140f, 26f),
                Text = "剑客张三",
                TextColor = InkWashTheme.GoldPrimary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 18f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _playerCard.AddChild(_playerName);

            _playerLevelTag = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(230f, 14f),
                Size = new Float2(46f, 18f),
                Text = "Lv.60",
                TextColor = InkWashTheme.TextBrand,
                BackgroundColor = new Color(200f / 255f, 168f / 255f, 88f / 255f, 0.12f),
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };
            _playerCard.AddChild(_playerLevelTag);

            _playerSectTag = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(282f, 14f),
                Size = new Float2(52f, 18f),
                Text = "武当派",
                TextColor = InkWashTheme.TextSecondary,
                BackgroundColor = new Color(200f / 255f, 168f / 255f, 88f / 255f, 0.08f),
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };
            _playerCard.AddChild(_playerSectTag);

            _playerPower = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(84f, 42f),
                Size = new Float2(120f, 20f),
                Text = "战力 12,345",
                TextColor = InkWashTheme.TextSecondary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _playerCard.AddChild(_playerPower);

            _playerOnlineStatus = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(190f, 42f),
                Size = new Float2(80f, 20f),
                Text = "● 在线",
                TextColor = InkWashTheme.TextJade,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _playerCard.AddChild(_playerOnlineStatus);

            _playerLocation = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(270f, 42f),
                Size = new Float2(140f, 20f),
                Text = "所在地 武当山",
                TextColor = InkWashTheme.TextSecondary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _playerCard.AddChild(_playerLocation);

            // Action buttons
            _btnWhisper = new InkButton
            {
                Variant = InkButtonVariant.Primary,
                ButtonSize = InkButtonSize.Sm,
                Text = "私聊",
                AnchorPreset = AnchorPresets.TopRight,
                Size = new Float2(56f, 28f),
            };
            _btnWhisper.ButtonClicked += (b) => NavigationRequested?.Invoke("btn-whisper");
            _playerCard.AddChild(_btnWhisper);

            _btnTeamInvite = new InkButton
            {
                Variant = InkButtonVariant.Default,
                ButtonSize = InkButtonSize.Sm,
                Text = "组队",
                AnchorPreset = AnchorPresets.TopRight,
                Size = new Float2(56f, 28f),
            };
            _btnTeamInvite.ButtonClicked += (b) => NavigationRequested?.Invoke("btn-team-invite");
            _playerCard.AddChild(_btnTeamInvite);

            _btnTrade = new InkButton
            {
                Variant = InkButtonVariant.Default,
                ButtonSize = InkButtonSize.Sm,
                Text = "交易",
                AnchorPreset = AnchorPresets.TopRight,
                Size = new Float2(56f, 28f),
            };
            _btnTrade.ButtonClicked += (b) => NavigationRequested?.Invoke("btn-trade");
            _playerCard.AddChild(_btnTrade);

            _btnDeleteFriend = new InkButton
            {
                Variant = InkButtonVariant.Vermilion,
                ButtonSize = InkButtonSize.Sm,
                Text = "删除",
                AnchorPreset = AnchorPresets.TopRight,
                Size = new Float2(56f, 28f),
            };
            _btnDeleteFriend.ButtonClicked += (b) => NavigationRequested?.Invoke("btn-delete-friend");
            _playerCard.AddChild(_btnDeleteFriend);

            _rightPanel.AddChild(_playerCard);

            // --- Chat window ---
            _chatWindow = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
            };

            // Channel tabs
            string[] channelLabels = { "世界", "队伍", "门派", "私聊", "区域" };
            string[] channelDomIds = { "ch-world", "ch-team", "ch-sect", "ch-whisper", "ch-zone" };
            _chWorld = MakeChannelTab(0, channelLabels[0]);
            _chTeam = MakeChannelTab(1, channelLabels[1]);
            _chSect = MakeChannelTab(2, channelLabels[2]);
            _chWhisper = MakeChannelTab(3, channelLabels[3]);
            _chZone = MakeChannelTab(4, channelLabels[4]);
            _chWorld.TextColor = InkWashTheme.GoldPrimary;

            // Chat messages
            string[][] msgs = {
                new[] { "世界", "剑客张三", "大家好" },
                new[] { "系统", "", "--- 恭贺升级! ---" },
                new[] { "世界", "飞燕李四", "求组副本" },
                new[] { "门派", "师兄", "明天门派活动" },
                new[] { "世界", "紫霞钱十", "有没有人一起去刷野?" },
                new[] { "系统", "", "--- 剑客张三 获得了 玄铁剑 ---" },
                new[] { "门派", "掌门", "下周门派战,各位做好准备" },
                new[] { "世界", "青衣王五", "出售多余药材,价格优惠" },
                new[] { "区域", "铁掌赵六", "襄阳城东门集合" },
                new[] { "系统", "", "--- 今晚八点,门派试炼开启 ---" },
                new[] { "世界", "孤剑赵九", "少林寺收徒,有意者私聊" },
            };
            _chatMessages = new Label[msgs.Length];

            for (int i = 0; i < msgs.Length; i++)
            {
                bool isSystem = msgs[i][0] == "系统";
                string text = isSystem ? msgs[i][2] : $"[{msgs[i][0]}] {msgs[i][1]}: {msgs[i][2]}";

                var msg = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(16f, 48f + i * 24f),
                    Size = new Float2(500f, 22f),
                    Text = text,
                    TextColor = isSystem ? InkWashTheme.GoldBright : InkWashTheme.TextDefault,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                };
                _chatMessages[i] = msg;
                _chatWindow.AddChild(msg);
            }

            // Chat input bar
            _chatInputBar = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = new Color(20f / 255f, 23f / 255f, 30f / 255f, 1f),
            };

            _chatInputField = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, 8f),
                Size = new Float2(400f, 28f),
                Text = "输入消息,Enter发送...",
                TextColor = InkWashTheme.TextTertiary,
                BackgroundColor = InkWashTheme.BaseTertiary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _chatInputBar.AddChild(_chatInputField);

            _chatSendButton = new InkButton
            {
                Variant = InkButtonVariant.Primary,
                ButtonSize = InkButtonSize.Sm,
                Text = "发送",
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(64f, 28f),
            };
            _chatSendButton.ButtonClicked += (b) => NavigationRequested?.Invoke("btn-send-msg");
            _chatInputBar.AddChild(_chatSendButton);

            _chatWindow.AddChild(_chatInputBar);

            _rightPanel.AddChild(_chatWindow);

            AddChild(_rightPanel);
        }

        private Label MakeChannelTab(int index, string text)
        {
            var tab = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(16f + index * 72f, 8f),
                Size = new Float2(64f, 28f),
                Text = text,
                TextColor = InkWashTheme.TextSecondary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };
            _chatWindow.AddChild(tab);
            return tab;
        }

        // ===================================================================
        // IInkPage
        // =======================================================================

        public void RefreshLayout()
        {
            try
            {
                float w = Width;
                float h = Height;

                // Header bar: full width top
                if (_headerBar != null)
                {
                    _headerBar.Location = Float2.Zero;
                    _headerBar.Size = new Float2(w, HeaderHeight);

                    if (_closeButton != null)
                    {
                        _closeButton.Location = new Float2(w - 44f, (HeaderHeight - 32f) * 0.5f);
                    }
                }

                // Left panel: fixed 450px width
                if (_leftPanel != null)
                {
                    _leftPanel.Location = new Float2(0f, HeaderHeight);
                    _leftPanel.Size = new Float2(LeftPanelWidth, h - HeaderHeight);
                }

                // Right panel: fills remaining width
                if (_rightPanel != null)
                {
                    float rightX = LeftPanelWidth;
                    float rightW = w - rightX;
                    _rightPanel.Location = new Float2(rightX, HeaderHeight);
                    _rightPanel.Size = new Float2(rightW, h - HeaderHeight);

                    // Player card: full width
                    if (_playerCard != null)
                    {
                        _playerCard.Location = new Float2(16f, 16f);
                        _playerCard.Size = new Float2(rightW - 32f, 80f);

                        // Position avatar
                        if (_playerAvatar != null)
                            _playerAvatar.Location = new Float2(16f, 12f);

                        // Position action buttons on the right
                        float btnX = _playerCard.Width - 16f;
                        if (_btnDeleteFriend != null)
                        {
                            _btnDeleteFriend.Location = new Float2(btnX - 60f, 26f);
                            btnX -= 68f;
                        }
                        if (_btnTrade != null)
                        {
                            _btnTrade.Location = new Float2(btnX - 60f, 26f);
                            btnX -= 68f;
                        }
                        if (_btnTeamInvite != null)
                        {
                            _btnTeamInvite.Location = new Float2(btnX - 60f, 26f);
                            btnX -= 68f;
                        }
                        if (_btnWhisper != null)
                        {
                            _btnWhisper.Location = new Float2(btnX - 60f, 26f);
                        }
                    }

                    // Chat window: below player card
                    if (_chatWindow != null)
                    {
                        float chatY = 16f + 80f + 12f;
                        _chatWindow.Location = new Float2(16f, chatY);
                        _chatWindow.Size = new Float2(rightW - 32f, h - HeaderHeight - chatY - 16f);

                        // Channel tabs
                        float tabWidth = (rightW - 80f) / 5f;
                        if (tabWidth < 56f) tabWidth = 56f;
                        float tabStart = 16f;
                        var tabs = new[] { _chWorld, _chTeam, _chSect, _chWhisper, _chZone };
                        for (int i = 0; i < tabs.Length; i++)
                        {
                            if (tabs[i] != null)
                            {
                                tabs[i].Location = new Float2(tabStart + i * (tabWidth + 8f), 8f);
                                tabs[i].Size = new Float2(tabWidth, 28f);
                            }
                        }

                        // Chat messages area: fill available height minus input bar
                        float msgAreaH = _chatWindow.Height - 48f - 44f;
                        float msgCount = _chatMessages.Length;
                        float msgH = msgAreaH / msgCount;
                        if (msgH > 24f) msgH = 24f;
                        for (int i = 0; i < _chatMessages.Length; i++)
                        {
                            if (_chatMessages[i] != null)
                            {
                                _chatMessages[i].Location = new Float2(16f, 48f + i * msgH);
                                _chatMessages[i].Size = new Float2(_chatWindow.Width - 32f, msgH);
                            }
                        }

                        // Chat input bar: bottom
                        if (_chatInputBar != null)
                        {
                            _chatInputBar.Location = new Float2(0f, _chatWindow.Height - 44f);
                            _chatInputBar.Size = new Float2(_chatWindow.Width, 44f);

                            if (_chatInputField != null)
                            {
                                _chatInputField.Size = new Float2(_chatWindow.Width - 92f, 28f);
                            }
                            if (_chatSendButton != null)
                            {
                                _chatSendButton.Location = new Float2(_chatWindow.Width - 76f, 8f);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SocialGuildPage] RefreshLayout failed: {ex.Message}");
            }
        }

        public override void OnParentResized()
        {
            base.OnParentResized();
            RefreshLayout();
        }
    }
}
