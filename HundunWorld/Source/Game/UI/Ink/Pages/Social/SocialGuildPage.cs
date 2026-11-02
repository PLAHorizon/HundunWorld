using FlaxEngine;
using FlaxEngine.GUI;
using Game.Character.Attributes;
using HundunWorld.Game.UI.Ink.Components;
using HundunWorld.Game.UI.StyleSystem;
using System;
using System.Collections.Generic;
using ChatChannel = Horizon.Game.Message.Network.ChatChannel;
using ChatClient = HundunWorld.Game.Chat.ChatClient;
using ChatLine = HundunWorld.Game.Chat.ChatLine;

namespace HundunWorld.Game.UI.Ink.Pages.Social
{
    /// <summary>
    /// 结伴江湖（江湖社交面板）— 对应设计方案 social-guild.html。
    /// 全屏布局：顶栏（标题 + 好友/门派/聊天/队伍 Tab + 关闭）/
    /// 左 450px（好友列表：在线/离线分组 + 门派&帮会信息卡）/
    /// 右侧（玩家信息卡 + 聊天窗口）。
    /// 右栏聊天窗口与主界面左下聊天框同源：数据取自 <see cref="ChatClient"/>（按频道过滤，
    /// 滚轮上滚加载更早历史），输入框为真 TextBox，发送走同一通道，两侧看到同一份消息。
    /// 严格遵循水墨主题 Token，禁止硬编码色值。
    /// </summary>
    public class SocialGuildPage : ContainerControl, IInkPage, IInkNavigationSource
    {
        /// <summary>成员列表（互斥选中需要整体复位）。</summary>
        private readonly List<FriendItem> _memberItems = new List<FriendItem>();

        private const float HeaderH = 56f;
        private const float LeftW = 450f;

        /// <summary>聊天消息区同步节流间隔（秒），与主界面左下聊天框一致。</summary>
        private const float ChatSyncInterval = 0.4f;

        /// <summary>聊天窗口内边距与输入行高度（RefreshLayout 显式重排用）。</summary>
        private const float ChatTabRowH = 48f;
        private const float ChatInputH = 44f;

        public event Action<string> NavigationRequested;
        public InkParticleSystem ParticleSystem { get; set; }

        private CharacterAttributesComponent _boundCharacter;

        private ContainerControl _headerBar;
        private ContainerControl _leftPanel;
        private ContainerControl _rightPanel;
        private DBox _chatBox;
        private ChatArea _chatArea;
        private ContainerControl _inputBar;
        private SendBtn _sendBtn;
        private NavTab[] _navTabs;

        // 聊天窗口状态（与主界面左下聊天框同一份 ChatClient 数据）
        private readonly List<ChannelTab> _channelTabs = new();
        private TextBox _chatInput;
        private Control _whisperExitBtn;
        private ChatChannel? _chatFilter;
        private ulong _whisperTargetId;
        private string _whisperTargetName;
        private float _chatSyncTimer;
        private float _chatHistoryLoadingAt = -1f;
        private bool _chatHistoryLoading;
        private int _lastRowCount = -1;
        private long _lastRowMessageId = -1;

        public void BindCharacter(CharacterAttributesComponent component)
        {
            _boundCharacter = component;
        }

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

                ChatClient.Instance.EnsureAttached();
                SyncChat();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[SocialGuildPage] init failed: {ex.Message}");
            }
        }

        // ===================================================================
        // 顶栏
        // ===================================================================

        private void BuildHeader()
        {
            _headerBar = new ContainerControl
            {
                AnchorPreset = AnchorPresets.HorizontalStretchTop,
                Offsets = new Margin(0f, 0f, 0f, HeaderH),
                BackgroundColor = InkWashTheme.Panel,
                AutoFocus = false,
            };
            AddChild(_headerBar);

            // 标题
            _headerBar.AddChild(MakeLabel("结伴江湖", 24f, 0f, 150f, HeaderH,
                InkWashTheme.GoldPrimary, 24f, InkWashTheme.FontRole.Display, TextAlignment.Near));

            // 导航 Tab：好友(active)/门派/聊天/队伍
            string[] tabTexts = { "好友", "门派", "聊天", "队伍" };
            _navTabs = new NavTab[tabTexts.Length];
            float tx = 200f;
            for (int i = 0; i < tabTexts.Length; i++)
            {
                bool active = i == 0;
                var tab = new NavTab(tabTexts[i], active)
                {
                    Location = new Float2(tx, 0f),
                    Size = new Float2(64f, HeaderH),
                };
                int idx = i;
                tab.Clicked += () =>
                {
                    for (int j = 0; j < _navTabs.Length; j++) _navTabs[j].SetActive(j == idx);
                    EmitGoldAtControl(tab);
                };
                _navTabs[i] = tab;
                _headerBar.AddChild(tab);
                tx += 68f;
            }

            // 关闭按钮
            var closeBtn = new InkButton
            {
                Variant = InkButtonVariant.Ghost,
                ButtonSize = InkButtonSize.Sm,
                Text = "×",
                AnchorPreset = AnchorPresets.MiddleRight,
                Offsets = new Margin(0f, 32f, 12f, 32f),
            };
            closeBtn.ButtonClicked += (b) => NavigationRequested?.Invoke(InkPageDomIds.BackOneLevel);
            _headerBar.AddChild(closeBtn);

            // 底部金边
            _headerBar.AddChild(new HRule { Location = new Float2(0f, HeaderH - 1f), Size = new Float2(4000f, 1f), LineColor = InkWashTheme.BorderGoldSubtle });
        }

        // ===================================================================
        // 左栏：好友列表 + 门派&帮会信息
        // ===================================================================

        private void BuildLeftPanel()
        {
            _leftPanel = new ContainerControl
            {
                AnchorPreset = AnchorPresets.VerticalStretchLeft,
                Offsets = new Margin(0f, LeftW, HeaderH, 0f),
                BackgroundColor = InkWashTheme.Panel,
                AutoFocus = false,
            };
            AddChild(_leftPanel);

            // 右边缘金边
            _leftPanel.AddChild(new VRule { Location = new Float2(LeftW - 1f, 0f), Size = new Float2(1f, 2000f), LineColor = InkWashTheme.BorderGoldSubtle });

            // --- 好友列表头 ---
            _leftPanel.AddChild(MakeLabel("好友列表", 16f, 16f, 120f, 20f,
                InkWashTheme.TextSecondary, 14f, InkWashTheme.FontRole.Heading, TextAlignment.Near));
            var search = new SearchBox { Location = new Float2(LeftW - 16f - 140f, 12f), Size = new Float2(140f, 28f) };
            _leftPanel.AddChild(search);

            // --- 在线分组 ---
            float y = 56f;
            var onlineHeader = new GroupHeader("在线", "(0)", InkWashTheme.JadeBright)
            {
                Location = new Float2(12f, y),
                Size = new Float2(LeftW - 24f, 32f),
            };
            _leftPanel.AddChild(onlineHeader);
            y += 36f;

            // 好友数据没有服务端来源：两组都空，给一条明确空态而不是摆 11 个虚构好友
            _leftPanel.AddChild(MakeLabel("暂无好友 · 好友功能尚未接入服务端", 24f, y + 6f, LeftW - 48f, 20f,
                InkWashTheme.TextTertiary, 12f, InkWashTheme.FontRole.Body, TextAlignment.Near));
            y += 44f;

            // --- 分隔线 ---
            y += 8f;
            _leftPanel.AddChild(new HRule { Location = new Float2(16f, y), Size = new Float2(LeftW - 32f, 1f), LineColor = InkWashTheme.BorderGoldSubtle });
            y += 13f;

            // --- 门派信息卡 ---
            BuildSectCard(y);

            // --- 帮会信息卡 ---
            BuildGuildCard(y + 122f);
        }

        private void AddFriend(ContainerControl parent, ref float y, string glyph, Color avatarBg, Color avatarFg,
            string name, Color nameColor, string level, Color lvBg, Color lvFg,
            string sub, Color subColor, bool online, bool selected)
        {
            var item = new FriendItem(glyph, avatarBg, avatarFg, name, nameColor, level, lvBg, lvFg, sub, subColor, online, selected)
            {
                Location = new Float2(12f, y),
                Size = new Float2(LeftW - 24f, 48f),
            };
            // 原来点了只播金色粒子：成员列表看起来可点却没有任何反应。改成互斥选中。
            item.Clicked += () =>
            {
                EmitGoldAtControl(item);
                foreach (var other in _memberItems) if (other != null) other.Selected = false;
                item.Selected = true;
            };
            _memberItems.Add(item);
            parent.AddChild(item);
            y += 50f;
        }

        private void BuildSectCard(float y)
        {
            var card = new DBox(InkWashTheme.BaseTertiary, InkWashTheme.BorderGoldSubtle, 8f)
            {
                Location = new Float2(16f, y),
                Size = new Float2(LeftW - 32f, 110f),
            };
            _leftPanel.AddChild(card);

            card.AddChild(MakeLabel("山 门派信息", 16f, 12f, 160f, 22f,
                InkWashTheme.GoldPrimary, 16f, InkWashTheme.FontRole.Display, TextAlignment.Near));
            card.AddChild(InfoRow(16f, 42f, "门派", "未加入", InkWashTheme.TextDefault, InkWashTheme.FontRole.Display));
            card.AddChild(InfoRow(16f, 64f, "职位", "无", InkWashTheme.TextDefault, InkWashTheme.FontRole.Body));
            card.AddChild(InfoRow(16f, 86f, "贡献", "—", InkWashTheme.GoldBright, InkWashTheme.FontRole.Number));
        }

        private void BuildGuildCard(float y)
        {
            var card = new DBox(InkWashTheme.BaseTertiary, InkWashTheme.BorderGoldSubtle, 8f)
            {
                Location = new Float2(16f, y),
                Size = new Float2(LeftW - 32f, 132f),
            };
            _leftPanel.AddChild(card);

            card.AddChild(MakeLabel("旗 帮会信息", 16f, 12f, 160f, 22f,
                InkWashTheme.GoldPrimary, 16f, InkWashTheme.FontRole.Display, TextAlignment.Near));

            var manageBtn = new InkButton
            {
                Variant = InkButtonVariant.Secondary,
                ButtonSize = InkButtonSize.Sm,
                Text = "帮会管理",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(LeftW - 32f - 16f - 76f, 10f),
                Size = new Float2(76f, 24f),
            };
            manageBtn.Enabled = false; // 帮会管理没有服务端通道，按钮不做假动作
            card.AddChild(manageBtn);

            card.AddChild(InfoRow(16f, 44f, "帮会", "未加入", InkWashTheme.TextDefault, InkWashTheme.FontRole.Display));
            card.AddChild(InfoRow(16f, 66f, "等级", "无", InkWashTheme.TextDefault, InkWashTheme.FontRole.Body));
            card.AddChild(InfoRow(16f, 88f, "职位", "无", InkWashTheme.TextDefault, InkWashTheme.FontRole.Body));
        }

        private ContainerControl InfoRow(float x, float y, string key, string value, Color valueColor, InkWashTheme.FontRole valueRole)
        {
            var holder = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(x, y),
                Size = new Float2(LeftW - 64f, 18f),
                AutoFocus = false,
            };
            holder.AddChild(MakeLabel(key, 0f, 0f, 60f, 18f, InkWashTheme.TextTertiary, 12f, InkWashTheme.FontRole.Body, TextAlignment.Near));
            holder.AddChild(MakeLabel(value, 64f, 0f, 220f, 18f, valueColor, 12f, valueRole, TextAlignment.Near));
            return holder;
        }

        // ===================================================================
        // 右栏：玩家信息卡 + 聊天窗口
        // ===================================================================

        private void BuildRightPanel()
        {
            _rightPanel = new ContainerControl
            {
                AnchorPreset = AnchorPresets.StretchAll,
                Offsets = new Margin(LeftW, 0f, HeaderH, 0f),
                AutoFocus = false,
            };
            AddChild(_rightPanel);

            BuildPlayerCard();
            BuildChatWindow();
        }

        private void BuildPlayerCard()
        {
            var card = new DBox(InkWashTheme.BaseTertiary, InkWashTheme.BorderGoldSubtle, 8f)
            {
                AnchorPreset = AnchorPresets.HorizontalStretchTop,
                Offsets = new Margin(16f, 16f, 16f, 120f),
            };
            _rightPanel.AddChild(card);

            // 头像 56x56
            var avatar = new AvatarDisc("—", 56f, WithAlpha(InkWashTheme.GoldPrimary, 0.12f), InkWashTheme.GoldPrimary, true)
            {
                Location = new Float2(20f, 20f),
                Size = new Float2(56f, 56f),
            };
            card.AddChild(avatar);

            // 姓名 + 标签
            // 没有选中任何联系人：这张卡不摆虚构玩家（原来写死「剑客张三 / Lv.60 / 武当派 / 战力 12,345 / 点 武当山」）
            card.AddChild(MakeLabel("未选择联系人", 92f, 18f, 190f, 26f,
                InkWashTheme.GoldPrimary, 18f, InkWashTheme.FontRole.Display, TextAlignment.Near));
            var lvTag = new TagPill("—", WithAlpha(InkWashTheme.GoldPrimary, 0.15f), InkWashTheme.GoldBright, InkWashTheme.FontRole.Number)
            {
                Location = new Float2(228f, 22f),
                Size = new Float2(48f, 18f),
            };
            card.AddChild(lvTag);
            var sectTag = new TagPill("—", WithAlpha(InkWashTheme.JadePrimary, 0.12f), InkWashTheme.JadeBright, InkWashTheme.FontRole.Display)
            {
                Location = new Float2(284f, 22f),
                Size = new Float2(52f, 18f),
            };
            card.AddChild(sectTag);

            // 战力 + 在线 + 位置
            card.AddChild(MakeLabel("战力", 92f, 52f, 34f, 18f, InkWashTheme.TextTertiary, 12f, InkWashTheme.FontRole.Body, TextAlignment.Near));
            card.AddChild(MakeLabel("—", 128f, 50f, 80f, 22f, InkWashTheme.GoldBright, 14f, InkWashTheme.FontRole.Number, TextAlignment.Near));
            card.AddChild(MakeLabel("●", 216f, 52f, 14f, 18f, InkWashTheme.TextTertiary, 12f, InkWashTheme.FontRole.Body, TextAlignment.Center));
            card.AddChild(MakeLabel("—", 232f, 52f, 40f, 18f, InkWashTheme.TextSecondary, 12f, InkWashTheme.FontRole.Body, TextAlignment.Near));
            card.AddChild(MakeLabel("—", 288f, 52f, 100f, 18f, InkWashTheme.TextDefault, 12f, InkWashTheme.FontRole.Body, TextAlignment.Near));

            // 操作按钮
            var btnWhisper = new InkButton { Variant = InkButtonVariant.Brand, ButtonSize = InkButtonSize.Sm, Text = "私聊",
                AnchorPreset = AnchorPresets.TopLeft, Location = new Float2(92f, 82f), Size = new Float2(60f, 24f) };
            btnWhisper.Enabled = false; // 私聊通道未接入，按钮不做假动作
            card.AddChild(btnWhisper);
            var btnTeam = new InkButton { Variant = InkButtonVariant.Secondary, ButtonSize = InkButtonSize.Sm, Text = "组队",
                AnchorPreset = AnchorPresets.TopLeft, Location = new Float2(160f, 82f), Size = new Float2(60f, 24f) };
            btnTeam.Enabled = false;
            card.AddChild(btnTeam);
            var btnTrade = new InkButton { Variant = InkButtonVariant.Secondary, ButtonSize = InkButtonSize.Sm, Text = "交易",
                AnchorPreset = AnchorPresets.TopLeft, Location = new Float2(228f, 82f), Size = new Float2(60f, 24f) };
            btnTrade.Enabled = false;
            card.AddChild(btnTrade);
            var btnDelete = new InkButton { Variant = InkButtonVariant.Danger, ButtonSize = InkButtonSize.Sm, Text = "删除",
                AnchorPreset = AnchorPresets.TopLeft, Location = new Float2(296f, 82f), Size = new Float2(60f, 24f) };
            btnDelete.Enabled = false;
            card.AddChild(btnDelete);
        }

        /// <summary>
        /// 聊天窗口：频道 Tab（与主界面左下聊天框同一组频道与过滤语义）+ 真实消息区 + 真输入框。
        /// 几何不用锚点（构造期父尺寸未定会导致错位），统一在 <see cref="RefreshLayout"/> 显式重排。
        /// </summary>
        private void BuildChatWindow()
        {
            _chatBox = new DBox(InkWashTheme.BaseTertiary, InkWashTheme.BorderGoldSubtle, 8f)
            {
                AnchorPreset = AnchorPresets.TopLeft,
            };
            // 挂到页面根而不是 _rightPanel：几何按设计空间绝对坐标算，不受右栏锚点偏移的影响
            AddChild(_chatBox);

            // 频道 Tab
            foreach (var channel in InkChatChannelView.TabChannels)
            {
                ChatChannel ch = channel;
                var ct = new ChannelTab(InkChatChannelView.Label(ch), false);
                ct.Clicked += () => ToggleChatFilter(ch);
                _channelTabs.Add(ct);
                _chatBox.AddChild(ct);
            }

            // 消息区
            _chatArea = new ChatArea
            {
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = WithAlpha(InkWashTheme.Void, 0.50f),
            };
            _chatArea.ReachedTop += OnChatReachedTop;
            _chatBox.AddChild(_chatArea);

            // 输入行
            _inputBar = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = WithAlpha(InkWashTheme.BaseSecondary, 0.90f),
                AutoFocus = false,
            };
            _inputBar.AddChild(new HRule { Location = new Float2(0f, 0f), Size = new Float2(4000f, 1f), LineColor = InkWashTheme.BorderFaint });
            _chatBox.AddChild(_inputBar);

            _chatInput = new TextBox
            {
                AnchorPreset = AnchorPresets.TopLeft,
                WatermarkText = "输入消息，Enter 发送...",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                TextColor = InkWashTheme.TextDefault,
                BackgroundColor = InkWashTheme.Abyss,
            };
            _inputBar.AddChild(_chatInput);

            // 退出密聊钮（仅在选中密聊对象时显示）
            var whisperExit = new InkButton
            {
                Variant = InkButtonVariant.Ghost,
                ButtonSize = InkButtonSize.Sm,
                Text = "×",
                AnchorPreset = AnchorPresets.TopLeft,
                Visible = false,
            };
            whisperExit.ButtonClicked += (b) => ExitWhisper();
            _whisperExitBtn = whisperExit;
            _inputBar.AddChild(whisperExit);

            var sendBtn = new SendBtn();
            sendBtn.Clicked += SendChatInput;
            _sendBtn = sendBtn;
            _inputBar.AddChild(sendBtn);
        }

        private void ToggleChatFilter(ChatChannel channel)
        {
            _whisperTargetId = 0;
            _whisperTargetName = null;
            if (_whisperExitBtn != null) _whisperExitBtn.Visible = false;

            _chatFilter = _chatFilter == channel ? (ChatChannel?)null : channel;
            UpdateTabVisuals();
            UpdateChatInputHint();
            ForceSyncChat();
        }

        private void UpdateTabVisuals()
        {
            for (int i = 0; i < _channelTabs.Count; i++)
                _channelTabs[i].SetActive(_chatFilter != null && InkChatChannelView.TabChannels[i] == _chatFilter.Value);
        }

        private void StartWhisper(ulong characterId, string name)
        {
            _whisperTargetId = characterId;
            _whisperTargetName = string.IsNullOrEmpty(name) ? characterId.ToString() : name;
            _chatFilter = ChatChannel.Private;
            if (_whisperExitBtn != null) _whisperExitBtn.Visible = true;
            UpdateTabVisuals();
            UpdateChatInputHint();
            ForceSyncChat();
        }

        private void ExitWhisper()
        {
            _whisperTargetId = 0;
            _whisperTargetName = null;
            if (_whisperExitBtn != null) _whisperExitBtn.Visible = false;
            UpdateChatInputHint();
            ForceSyncChat();
        }

        private void UpdateChatInputHint()
        {
            if (_chatInput == null) return;
            _chatInput.WatermarkText = _whisperTargetId != 0
                ? $"密聊 {_whisperTargetName}，Enter 发送..."
                : "输入消息，Enter 发送...";
        }

        /// <summary>发送当前输入：密聊视图发给该玩家，否则发给所选频道（未选频道时发到世界）。</summary>
        private void SendChatInput()
        {
            if (_chatInput == null) return;
            var text = _chatInput.Text;
            if (string.IsNullOrWhiteSpace(text)) return;

            bool sent = _whisperTargetId != 0
                ? ChatClient.Instance.Send(ChatChannel.Private, text, _whisperTargetId)
                : ChatClient.Instance.Send(_chatFilter ?? ChatChannel.World, text);
            if (!sent) return;

            _chatInput.Text = string.Empty;
            ForceSyncChat();
        }

        /// <summary>强制重建消息区（过滤/密聊变化后、发送后）。</summary>
        private void ForceSyncChat()
        {
            _lastRowCount = -1;
            SyncChat();
        }

        /// <summary>把 ChatClient 的真实消息按当前过滤重建到消息区；内容未变则不动（保住滚动位置与悬停态）。</summary>
        private void SyncChat()
        {
            if (_chatArea == null) return;
            ChatClient.Instance.EnsureAttached();

            var lines = new List<ChatLine>();
            if (_whisperTargetId != 0)
                lines.AddRange(ChatClient.Instance.GetWhisper(_whisperTargetId));
            else if (_chatFilter != null)
                lines.AddRange(ChatClient.Instance.GetMessages(_chatFilter.Value));
            else
                foreach (var ch in InkChatChannelView.TabChannels)
                    lines.AddRange(ChatClient.Instance.GetMessages(ch));

            const int maxRows = 60;
            if (lines.Count > maxRows)
                lines.RemoveRange(0, lines.Count - maxRows);

            long lastId = lines.Count > 0 ? lines[lines.Count - 1].MessageId : 0;
            if (lines.Count == _lastRowCount && lastId == _lastRowMessageId) return;
            _lastRowCount = lines.Count;
            _lastRowMessageId = lastId;

            var me = HundunWorldGame.Instance?.PlayerId ?? 0;
            _chatArea.ClearRows();
            foreach (var line in lines)
            {
                Control row;
                if (line.IsSystem || line.Channel == ChatChannel.System)
                {
                    row = new SysMsg(line.Content);
                }
                else
                {
                    ChatLine captured = line;
                    var msg = new ChatMsg(InkChatChannelView.Label(line.Channel),
                        InkChatChannelView.Style(line.Channel).Color, line.SenderName,
                        line.SenderId == me ? InkWashTheme.GoldBright : InkWashTheme.TextDefault, line.Content);
                    msg.Clicked += () =>
                    {
                        if (captured.SenderId == 0 || captured.SenderId == me) return;
                        StartWhisper(captured.SenderId, captured.SenderName);
                    };
                    row = msg;
                }
                _chatArea.AddRow(row);
            }
            _chatArea.ScrollToBottom();

            // 正在看的频道计入已读，避免回到主界面后红点仍亮着
            if (_chatFilter != null) ChatClient.Instance.MarkRead(_chatFilter.Value);
        }

        /// <summary>滚到顶：加载更早历史（需在具体频道或密聊视图；3 秒节流）。</summary>
        private void OnChatReachedTop()
        {
            if (_chatHistoryLoading && Time.TimeSinceStartup - _chatHistoryLoadingAt < 3f) return;

            long oldest;
            bool sent;
            if (_whisperTargetId != 0)
            {
                oldest = ChatClient.Instance.GetOldestWhisperId(_whisperTargetId);
                if (oldest <= 0) return;
                sent = ChatClient.Instance.RequestHistory(ChatChannel.Private, _whisperTargetId, oldest, 30);
            }
            else if (_chatFilter != null)
            {
                oldest = ChatClient.Instance.GetOldestMessageId(_chatFilter.Value);
                if (oldest <= 0) return;
                sent = ChatClient.Instance.RequestHistory(_chatFilter.Value, 0, oldest, 30);
            }
            else
            {
                return;   // 全部频道视图不做分页（游标语义不明确）
            }

            if (sent)
            {
                _chatHistoryLoading = true;
                _chatHistoryLoadingAt = Time.TimeSinceStartup;
            }
        }

        // ===================================================================
        // RefreshLayout
        // ===================================================================

        public void RefreshLayout()
        {
            LayoutChatWindow();
        }

        /// <inheritdoc />
        public override void OnParentResized()
        {
            base.OnParentResized();
            LayoutChatWindow();
        }

        /// <inheritdoc />
        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);

            // 网络线程只写缓存，这里在主线程按节流读取并重画（与主界面聊天框同一节奏）。
            _chatSyncTimer += deltaTime;
            if (_chatSyncTimer < ChatSyncInterval) return;
            _chatSyncTimer = 0f;

            if (_chatInput != null && _chatInput.IsFocused && FlaxEngine.Input.GetKeyDown(KeyboardKeys.Return))
                SendChatInput();

            if (_chatHistoryLoading && Time.TimeSinceStartup - _chatHistoryLoadingAt >= 3f)
                _chatHistoryLoading = false;

            SyncChat();
        }

        /// <summary>
        /// 聊天窗口几何：右栏玩家卡下方占满剩余空间，内部依次为频道 Tab 行 / 消息区 / 输入行。
        /// </summary>
        private void LayoutChatWindow()
        {
            if (_chatBox == null) return;

            float w = Mathf.Max(0f, Width - LeftW - 32f);
            float top = HeaderH + 136f;
            float h = Mathf.Max(0f, Height - top - 16f);
            _chatBox.Location = new Float2(LeftW + 16f, top);
            _chatBox.Size = new Float2(w, h);

            float tx = 16f;
            foreach (var tab in _channelTabs)
            {
                tab.Location = new Float2(tx, 8f);
                tab.Size = new Float2(48f, ChatTabRowH - 16f);
                tx += 56f;
            }

            _chatArea.Location = new Float2(8f, ChatTabRowH);
            _chatArea.Size = new Float2(Mathf.Max(0f, w - 16f), Mathf.Max(0f, h - ChatTabRowH - ChatInputH));

            _inputBar.Location = new Float2(0f, Mathf.Max(0f, h - ChatInputH));
            _inputBar.Size = new Float2(w, ChatInputH);

            float sendX = Mathf.Max(0f, w - 12f - 76f);
            _sendBtn.Location = new Float2(sendX, 8f);
            _sendBtn.Size = new Float2(76f, 28f);

            float exitX = Mathf.Max(0f, sendX - 8f - 32f);
            if (_whisperExitBtn != null)
            {
                _whisperExitBtn.Location = new Float2(exitX, 8f);
                _whisperExitBtn.Size = new Float2(32f, 28f);
            }

            if (_chatInput != null)
            {
                _chatInput.Location = new Float2(12f, 8f);
                _chatInput.Size = new Float2(Mathf.Max(0f, exitX - 24f), 28f);
            }

            _chatArea.Relayout();
        }

        // ===================================================================
        // Helpers
        // ===================================================================

        private void EmitGoldAtControl(Control c)
        {
            if (ParticleSystem == null || c == null) return;
            var screen = c.PointToScreen(new Float2(c.Width * 0.5f, c.Height * 0.5f));
            var local = ParticleSystem.PointFromScreen(screen);
            ParticleSystem.EmitGoldBurst(local, 10, false);
        }

        private static Color WithAlpha(Color c, float a) => new Color(c.R, c.G, c.B, a);

        private static Label MakeLabel(string text, float x, float y, float w, float h,
            Color color, float size, InkWashTheme.FontRole role, TextAlignment hAlign)
        {
            return new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(x, y),
                Size = new Float2(w, h),
                Text = text,
                TextColor = color,
                Font = InkRenderHelper.GetFontRef(role, size),
                HorizontalAlignment = hAlign,
                VerticalAlignment = TextAlignment.Center,
                AutoFocus = false,
            };
        }

        // ===================================================================
        // 嵌套控件 — A
        // ===================================================================

        /// <summary>自绘圆角背景+边框盒。</summary>
        private sealed class DBox : ContainerControl
        {
            private readonly Color _bg;
            private Color _border;
            private readonly float _radius;
            public DBox(Color bg, Color border, float radius)
            {
                _bg = bg; _border = border; _radius = radius;
                AutoFocus = false;
                // 底色由 Draw 自绘（在子控件之前），引擎默认底色会盖住自绘层
                BackgroundColor = Color.Transparent;
            }
            public void SetBorder(Color c) { _border = c; }
            public override void Draw()
            {
                if (!Visible) return;
                var r = new Rectangle(Float2.Zero, Size);
                // 先铺底，再由基类画子控件，最后压边框——顺序反了子内容会被底色盖掉
                InkRenderHelper.FillRoundedRectangle(r, _radius, _bg);
                base.Draw();
                InkRenderHelper.DrawRoundedRectangle(r, _radius, _border, 1f);
            }
        }

        /// <summary>顶栏导航 Tab（激活=金色+28px下划线+辉光）。</summary>
        private sealed class NavTab : ContainerControl
        {
            private readonly string _text;
            private bool _active;
            private bool _hover;
            public event Action Clicked;
            public NavTab(string text, bool active)
            {
                _text = text; _active = active;
                AutoFocus = false;
            }
            public void SetActive(bool v) { _active = v; }
            public override void OnMouseEnter(Float2 location) { _hover = true; base.OnMouseEnter(location); }
            public override void OnMouseLeave() { _hover = false; base.OnMouseLeave(); }
            public override bool OnMouseUp(Float2 location, MouseButton button)
            {
                if (button == MouseButton.Left && ContainsPoint(ref location)) { Clicked?.Invoke(); return true; }
                return base.OnMouseUp(location, button);
            }
            public override void Draw()
            {
                if (!Visible) return;
                base.Draw();
                var font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 14f).GetFont();
                if (font == null) return;
                Color tc = _active ? InkWashTheme.GoldPrimary : (_hover ? InkWashTheme.TextDefault : InkWashTheme.TextSecondary);
                Render2D.DrawText(font, _text, new Rectangle(Float2.Zero, Size), tc, TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
                if (_active)
                {
                    float cx = Width * 0.5f;
                    Render2D.FillRectangle(new Rectangle(cx - 14f, Height - 5f, 28f, 2f), WithAlpha(InkWashTheme.GoldPrimary, 0.25f));
                    Render2D.FillRectangle(new Rectangle(cx - 14f, Height - 3f, 28f, 2f), InkWashTheme.GoldPrimary);
                }
            }
        }

        /// <summary>好友分组头（▶ 在线 (5)，金色4%底）。</summary>
        private sealed class GroupHeader : ContainerControl
        {
            private readonly string _title, _count;
            private readonly Color _countColor;
            public GroupHeader(string title, string count, Color countColor)
            {
                _title = title; _count = count; _countColor = countColor;
                AutoFocus = false;
            }
            public override void Draw()
            {
                if (!Visible) return;
                base.Draw();
                InkRenderHelper.FillRoundedRectangle(new Rectangle(Float2.Zero, Size), 4f, WithAlpha(InkWashTheme.GoldPrimary, 0.04f));
                var fTitle = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 14f).GetFont();
                var fNum = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 14f).GetFont();
                if (fTitle == null || fNum == null) return;
                Render2D.DrawText(fTitle, "▶", new Rectangle(12f, 0f, 16f, Height), InkWashTheme.GoldPrimary, TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
                Render2D.DrawText(fTitle, _title, new Rectangle(34f, 0f, 80f, Height), InkWashTheme.TextDefault, TextAlignment.Near, TextAlignment.Center, TextWrapping.NoWrap);
                Render2D.DrawText(fNum, _count, new Rectangle(34f + _title.Length * 14f + 6f, 0f, 60f, Height), _countColor, TextAlignment.Near, TextAlignment.Center, TextWrapping.NoWrap);
            }
        }

        /// <summary>好友项：状态点+头像+名+Lv标签+门派/上线时间。</summary>
        private sealed class FriendItem : ContainerControl
        {
            private readonly string _glyph, _name, _level, _sub;
            private readonly Color _avatarBg, _avatarFg, _nameColor, _lvBg, _lvFg, _subColor;
            private readonly bool _online;
            private bool _selected;

            /// <summary>选中态（点成员时互斥切换）。</summary>
            public bool Selected { get => _selected; set => _selected = value; }
            private bool _hover;
            public event Action Clicked;
            public FriendItem(string glyph, Color avatarBg, Color avatarFg, string name, Color nameColor,
                string level, Color lvBg, Color lvFg, string sub, Color subColor, bool online, bool selected)
            {
                _glyph = glyph; _avatarBg = avatarBg; _avatarFg = avatarFg;
                _name = name; _nameColor = nameColor;
                _level = level; _lvBg = lvBg; _lvFg = lvFg;
                _sub = sub; _subColor = subColor;
                _online = online; _selected = selected;
                AutoFocus = false;
            }
            public override void OnMouseEnter(Float2 location) { _hover = true; base.OnMouseEnter(location); }
            public override void OnMouseLeave() { _hover = false; base.OnMouseLeave(); }
            public override bool OnMouseUp(Float2 location, MouseButton button)
            {
                if (button == MouseButton.Left && ContainsPoint(ref location)) { Clicked?.Invoke(); return true; }
                return base.OnMouseUp(location, button);
            }
            public override void Draw()
            {
                if (!Visible) return;
                base.Draw();
                var r = new Rectangle(Float2.Zero, Size);
                if (_selected) InkRenderHelper.FillRoundedRectangle(r, 4f, WithAlpha(InkWashTheme.GoldPrimary, 0.10f));
                else if (_hover) InkRenderHelper.FillRoundedRectangle(r, 4f, WithAlpha(InkWashTheme.GoldPrimary, 0.06f));
                if (_selected) Render2D.FillRectangle(new Rectangle(0f, 4f, 2f, Height - 8f), InkWashTheme.GoldPrimary);
                float cy = Height * 0.5f;
                // 状态点 8px
                Color dot = _online ? InkWashTheme.JadePrimary : InkWashTheme.TextTertiary;
                if (_online && _selected) InkRenderHelper.FillCircle(new Float2(18f, cy), 7f, WithAlpha(InkWashTheme.JadePrimary, 0.25f));
                InkRenderHelper.FillCircle(new Float2(18f, cy), 4f, dot);
                // 头像 32px 圆
                InkRenderHelper.FillCircle(new Float2(48f, cy), 16f, _avatarBg);
                var fGlyph = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 14f).GetFont();
                if (fGlyph != null)
                    Render2D.DrawText(fGlyph, _glyph, new Rectangle(32f, cy - 16f, 32f, 32f), _avatarFg, TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
                // 名 + Lv 标签
                float tx = 76f;
                var fName = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 14f).GetFont();
                var fNum = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 12f).GetFont();
                var fSub = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 12f).GetFont();
                if (fName == null || fNum == null || fSub == null) return;
                Render2D.DrawText(fName, _name, new Rectangle(tx, 5f, 140f, 20f), _nameColor, TextAlignment.Near, TextAlignment.Center, TextWrapping.NoWrap);
                float lvX = tx + _name.Length * 14f + 8f;
                float lvW = _level.Length * 7f + 12f;
                InkRenderHelper.FillRoundedRectangle(new Rectangle(lvX, 7f, lvW, 16f), 3f, _lvBg);
                Render2D.DrawText(fNum, _level, new Rectangle(lvX, 7f, lvW, 16f), _lvFg, TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
                Render2D.DrawText(fSub, _sub, new Rectangle(tx, 25f, 220f, 16f), _subColor, TextAlignment.Near, TextAlignment.Center, TextWrapping.NoWrap);
            }
        }

        /// <summary>圆形头像（可选金边）。</summary>
        private sealed class AvatarDisc : ContainerControl
        {
            private readonly string _glyph;
            private readonly Color _bg, _fg;
            private readonly bool _bordered;
            public AvatarDisc(string glyph, float size, Color bg, Color fg, bool bordered)
            {
                _glyph = glyph; _bg = bg; _fg = fg; _bordered = bordered;
                AutoFocus = false;
            }
            public override void Draw()
            {
                if (!Visible) return;
                base.Draw();
                float rad = Mathf.Min(Width, Height) * 0.5f;
                var c = new Float2(Width * 0.5f, Height * 0.5f);
                InkRenderHelper.FillCircle(c, rad, _bg);
                if (_bordered) InkRenderHelper.DrawCircle(c, rad - 0.5f, InkWashTheme.BorderGold, 1f);
                var font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 22f).GetFont();
                if (font == null) return;
                Render2D.DrawText(font, _glyph, new Rectangle(Float2.Zero, Size), _fg, TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
            }
        }

        /// <summary>小圆角标签（Lv.60 / 武当派）。</summary>
        private sealed class TagPill : ContainerControl
        {
            private readonly string _text;
            private readonly Color _bg, _fg;
            private readonly InkWashTheme.FontRole _role;
            public TagPill(string text, Color bg, Color fg, InkWashTheme.FontRole role)
            {
                _text = text; _bg = bg; _fg = fg; _role = role;
                AutoFocus = false;
            }
            public override void Draw()
            {
                if (!Visible) return;
                base.Draw();
                InkRenderHelper.FillRoundedRectangle(new Rectangle(Float2.Zero, Size), 3f, _bg);
                var font = InkRenderHelper.GetFontRef(_role, 12f).GetFont();
                if (font == null) return;
                Render2D.DrawText(font, _text, new Rectangle(Float2.Zero, Size), _fg, TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
            }
        }

        /// <summary>搜索/输入框（圆角边框+占位符）。</summary>
        private sealed class SearchBox : ContainerControl
        {
            public string Placeholder = "搜索好友";
            public SearchBox() { AutoFocus = false; }
            public override void Draw()
            {
                if (!Visible) return;
                base.Draw();
                var r = new Rectangle(Float2.Zero, Size);
                InkRenderHelper.FillRoundedRectangle(r, 4f, InkWashTheme.BaseTertiary);
                InkRenderHelper.DrawRoundedRectangle(r, 4f, InkWashTheme.BorderNeutralL2, 1f);
                var font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f).GetFont();
                if (font == null) return;
                Render2D.DrawText(font, "寻 " + Placeholder, new Rectangle(8f, 0f, Width - 16f, Height), InkWashTheme.TextTertiary, TextAlignment.Near, TextAlignment.Center, TextWrapping.NoWrap);
            }
        }

        /// <summary>横线。</summary>
        private sealed class HRule : Control
        {
            public Color LineColor = InkWashTheme.BorderGold;
            public override void Draw()
            {
                if (!Visible) return;
                base.Draw();
                Render2D.FillRectangle(new Rectangle(Float2.Zero, Size), LineColor);
            }
        }

        /// <summary>竖线。</summary>
        private sealed class VRule : Control
        {
            public Color LineColor = InkWashTheme.BorderGold;
            public override void Draw()
            {
                if (!Visible) return;
                base.Draw();
                Render2D.FillRectangle(new Rectangle(Float2.Zero, Size), LineColor);
            }
        }

        // ===================================================================
        // 嵌套控件 — B
        // ===================================================================

        /// <summary>
        /// 聊天消息区：裁剪 + 滚轮上下滚动 + 到顶回调；行由页面按 <see cref="ChatClient"/> 的真实消息重建。
        /// </summary>
        private sealed class ChatArea : ContainerControl
        {
            private const float RowH = 24f;
            private const float RowPitch = 26f;
            private const float PadX = 8f;

            private readonly List<Control> _rows = new();
            private float _scroll;

            /// <summary>已在顶部继续上滚（页面据此加载更早历史）。</summary>
            public event Action ReachedTop;

            public ChatArea()
            {
                ClipChildren = true;
                AutoFocus = false;
            }

            public void ClearRows()
            {
                foreach (var row in _rows) row.Dispose();
                _rows.Clear();
            }

            public void AddRow(Control row)
            {
                AddChild(row);
                _rows.Add(row);
            }

            public void ScrollToBottom()
            {
                _scroll = MaxScroll;
                Relayout();
            }

            /// <summary>按当前宽度与滚动位置重排所有行。</summary>
            public void Relayout()
            {
                _scroll = Mathf.Clamp(_scroll, 0f, MaxScroll);
                float rowW = Mathf.Max(0f, Width - PadX * 2f);
                float y = -_scroll;
                foreach (var row in _rows)
                {
                    row.Size = new Float2(rowW, RowH);
                    row.Location = new Float2(PadX, y);
                    y += RowPitch;
                }
            }

            private float MaxScroll => Mathf.Max(0f, _rows.Count * RowPitch - Height);

            /// <inheritdoc />
            public override bool OnMouseWheel(Float2 location, float delta)
            {
                _scroll -= delta * RowPitch;
                _scroll = Mathf.Clamp(_scroll, 0f, MaxScroll);
                Relayout();

                if (_scroll <= 0.5f) ReachedTop?.Invoke();
                return true;
            }
        }

        /// <summary>聊天频道 Tab（激活=金色+下划线；点击切换该频道过滤）。</summary>
        private sealed class ChannelTab : ContainerControl
        {
            private readonly string _text;
            private bool _active;
            private bool _hover;

            public event Action Clicked;

            public ChannelTab(string text, bool active)
            {
                _text = text; _active = active;
                AutoFocus = false;
            }

            public void SetActive(bool value) { _active = value; }

            public override void OnMouseEnter(Float2 location) { _hover = true; base.OnMouseEnter(location); }
            public override void OnMouseLeave() { _hover = false; base.OnMouseLeave(); }
            public override bool OnMouseUp(Float2 location, MouseButton button)
            {
                if (button == MouseButton.Left && ContainsPoint(ref location)) { Clicked?.Invoke(); return true; }
                return base.OnMouseUp(location, button);
            }
            public override void Draw()
            {
                if (!Visible) return;
                base.Draw();
                var font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f).GetFont();
                if (font == null) return;
                Color tc = _active ? InkWashTheme.GoldPrimary : (_hover ? InkWashTheme.TextDefault : InkWashTheme.TextSecondary);
                Render2D.DrawText(font, _text, new Rectangle(Float2.Zero, Size), tc, TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
                if (_active)
                {
                    float cx = Width * 0.5f;
                    Render2D.FillRectangle(new Rectangle(cx - 12f, Height - 2f, 24f, 2f), InkWashTheme.GoldPrimary);
                }
            }
        }

        /// <summary>聊天消息：[频道] 发言人：正文。</summary>
        private sealed class ChatMsg : ContainerControl
        {
            private readonly string _channel, _speaker, _text;
            private readonly Color _chColor, _spkColor;
            private bool _hover;

            /// <summary>点击整行（页面据此发起与该发言人的密聊）。</summary>
            public event Action Clicked;

            public ChatMsg(string channel, Color chColor, string speaker, Color spkColor, string text)
            {
                _channel = channel; _chColor = chColor; _speaker = speaker; _spkColor = spkColor; _text = text;
                AutoFocus = false;
            }
            public override void OnMouseEnter(Float2 location) { _hover = true; base.OnMouseEnter(location); }
            public override void OnMouseLeave() { _hover = false; base.OnMouseLeave(); }
            public override bool OnMouseUp(Float2 location, MouseButton button)
            {
                if (button == MouseButton.Left && ContainsPoint(ref location)) { Clicked?.Invoke(); return true; }
                return base.OnMouseUp(location, button);
            }
            public override void Draw()
            {
                if (!Visible) return;
                base.Draw();
                if (_hover) InkRenderHelper.FillRoundedRectangle(new Rectangle(Float2.Zero, Size), 4f, WithAlpha(InkWashTheme.GoldPrimary, 0.04f));
                float tagW = _channel.Length * 12f + 12f;
                InkRenderHelper.FillRoundedRectangle(new Rectangle(4f, 3f, tagW, 18f), 3f, WithAlpha(_chColor, 0.15f));
                var fTag = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f).GetFont();
                var fBody = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 14f).GetFont();
                if (fTag == null || fBody == null) return;
                Render2D.DrawText(fTag, _channel, new Rectangle(4f, 3f, tagW, 18f), _chColor, TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
                float sx = 4f + tagW + 8f;
                Render2D.DrawText(fBody, _speaker, new Rectangle(sx, 0f, 120f, Height), _spkColor, TextAlignment.Near, TextAlignment.Center, TextWrapping.NoWrap);
                float bx = sx + _speaker.Length * 14f + 4f;
                Render2D.DrawText(fBody, "：" + _text, new Rectangle(bx, 0f, 1200f, Height), InkWashTheme.TextDefault, TextAlignment.Near, TextAlignment.Center, TextWrapping.NoWrap);
            }
        }

        /// <summary>系统消息（居中金色）。</summary>
        private sealed class SysMsg : ContainerControl
        {
            private readonly string _text;
            public SysMsg(string text) { _text = text; AutoFocus = false; }
            public override void Draw()
            {
                if (!Visible) return;
                base.Draw();
                var font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f).GetFont();
                if (font == null) return;
                Render2D.DrawText(font, _text, new Rectangle(Float2.Zero, Size), InkWashTheme.GoldBright, TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
            }
        }

        /// <summary>发送按钮（brand 金）。</summary>
        private sealed class SendBtn : ContainerControl
        {
            private bool _hover;
            public event Action Clicked;
            public SendBtn() { AutoFocus = false; }
            public override void OnMouseEnter(Float2 location) { _hover = true; base.OnMouseEnter(location); }
            public override void OnMouseLeave() { _hover = false; base.OnMouseLeave(); }
            public override bool OnMouseUp(Float2 location, MouseButton button)
            {
                if (button == MouseButton.Left && ContainsPoint(ref location)) { Clicked?.Invoke(); return true; }
                return base.OnMouseUp(location, button);
            }
            public override void Draw()
            {
                if (!Visible) return;
                base.Draw();
                var r = new Rectangle(Float2.Zero, Size);
                Color bg = _hover ? InkWashTheme.BrandHover : InkWashTheme.GoldPrimary;
                InkRenderHelper.FillRoundedRectangle(r, 6f, bg);
                var font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f).GetFont();
                if (font == null) return;
                Render2D.DrawText(font, "➤ 发送", r, InkWashTheme.TextInverse, TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
            }
        }
    }
}
