using FlaxEngine;
using FlaxEngine.GUI;
using Game.Character.Attributes;
using HundunWorld.Game.UI.StyleSystem;
using System;

namespace HundunWorld.Game.UI.Ink.Pages.Social
{
    /// <summary>
    /// 江湖社交面板 — 对应设计方案 social-guild.html。
    /// 全屏布局：顶栏（江湖标题 + 好友/门派/聊天/队伍 Tab + 关闭）/
    /// 左 450px（好友列表：在线/离线分组 + 门派&帮会信息卡）/
    /// 右侧（玩家信息卡 + 聊天窗口）。
    /// 严格遵循水墨主题 Token，禁止硬编码色值。
    /// </summary>
    public class SocialGuildPage : ContainerControl, IInkPage
    {
        private const float HeaderH = 56f;
        private const float LeftW = 450f;

        public event Action<string> NavigationRequested;
        public InkParticleSystem ParticleSystem { get; set; }

        private CharacterAttributesComponent _boundCharacter;

        private ContainerControl _headerBar;
        private ContainerControl _leftPanel;
        private ContainerControl _rightPanel;
        private ContainerControl _chatMessages;
        private NavTab[] _navTabs;

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
            _headerBar.AddChild(MakeLabel("江湖", 24f, 0f, 90f, HeaderH,
                InkWashTheme.GoldPrimary, 24f, InkWashTheme.FontRole.Display, TextAlignment.Near));

            // 导航 Tab：好友(active)/门派/聊天/队伍
            string[] tabTexts = { "好友", "门派", "聊天", "队伍" };
            string[] tabDomIds = { "tab-friends", "tab-sect", "tab-chat", "tab-team" };
            _navTabs = new NavTab[tabTexts.Length];
            float tx = 140f;
            for (int i = 0; i < tabTexts.Length; i++)
            {
                bool active = i == 0;
                var tab = new NavTab(tabTexts[i], active)
                {
                    Location = new Float2(tx, 0f),
                    Size = new Float2(64f, HeaderH),
                };
                string domId = tabDomIds[i];
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
                Text = "✕",
                AnchorPreset = AnchorPresets.MiddleRight,
                Offsets = new Margin(0f, 32f, 12f, 32f),
            };
            closeBtn.ButtonClicked += (b) => NavigationRequested?.Invoke(InkPageDomIds.BackHud);
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
            var onlineHeader = new GroupHeader("在线", "(5)", InkWashTheme.JadeBright)
            {
                Location = new Float2(12f, y),
                Size = new Float2(LeftW - 24f, 32f),
            };
            _leftPanel.AddChild(onlineHeader);
            y += 36f;

            // 在线好友（5人）
            AddFriend(_leftPanel, ref y, "张", WithAlpha(InkWashTheme.GoldPrimary, 0.12f), InkWashTheme.GoldPrimary,
                "剑客张三", InkWashTheme.TextDefault, "Lv.60", WithAlpha(InkWashTheme.GoldPrimary, 0.12f), InkWashTheme.GoldPrimary,
                "武当派", InkWashTheme.TextSecondary, true, true);
            AddFriend(_leftPanel, ref y, "李", WithAlpha(InkWashTheme.JadeDeep, 0.15f), InkWashTheme.JadeBright,
                "飞燕李四", InkWashTheme.TextDefault, "Lv.55", WithAlpha(InkWashTheme.GoldPrimary, 0.08f), InkWashTheme.TextSecondary,
                "丐帮", InkWashTheme.TextSecondary, true, false);
            AddFriend(_leftPanel, ref y, "王", WithAlpha(InkWashTheme.QualityRare, 0.15f), InkWashTheme.QualityRare,
                "青衣王五", InkWashTheme.TextDefault, "Lv.48", WithAlpha(InkWashTheme.GoldPrimary, 0.08f), InkWashTheme.TextSecondary,
                "峨眉", InkWashTheme.TextSecondary, true, false);
            AddFriend(_leftPanel, ref y, "赵", WithAlpha(InkWashTheme.BloodPrimary, 0.15f), InkWashTheme.VermilionBright,
                "孤剑赵九", InkWashTheme.TextDefault, "Lv.52", WithAlpha(InkWashTheme.GoldPrimary, 0.08f), InkWashTheme.TextSecondary,
                "少林", InkWashTheme.TextSecondary, true, false);
            AddFriend(_leftPanel, ref y, "钱", WithAlpha(InkWashTheme.QualityEpic, 0.15f), InkWashTheme.QualityEpic,
                "紫霞钱十", InkWashTheme.TextDefault, "Lv.45", WithAlpha(InkWashTheme.GoldPrimary, 0.08f), InkWashTheme.TextSecondary,
                "唐门", InkWashTheme.TextSecondary, true, false);

            // --- 离线分组 ---
            y += 8f;
            var offlineHeader = new GroupHeader("离线", "(12)", InkWashTheme.TextTertiary)
            {
                Location = new Float2(12f, y),
                Size = new Float2(LeftW - 24f, 32f),
            };
            _leftPanel.AddChild(offlineHeader);
            y += 36f;

            // 离线好友（6人）
            AddFriend(_leftPanel, ref y, "赵", WithAlpha(InkWashTheme.QualityCommon, 0.10f), InkWashTheme.TextSecondary,
                "铁掌赵六", InkWashTheme.TextSecondary, "Lv.42", WithAlpha(InkWashTheme.GoldPrimary, 0.04f), InkWashTheme.TextTertiary,
                "5小时前在线", InkWashTheme.TextTertiary, false, false);
            AddFriend(_leftPanel, ref y, "孙", WithAlpha(InkWashTheme.QualityCommon, 0.10f), InkWashTheme.TextSecondary,
                "玉面孙七", InkWashTheme.TextSecondary, "Lv.38", WithAlpha(InkWashTheme.GoldPrimary, 0.04f), InkWashTheme.TextTertiary,
                "昨天在线", InkWashTheme.TextTertiary, false, false);
            AddFriend(_leftPanel, ref y, "周", WithAlpha(InkWashTheme.QualityCommon, 0.10f), InkWashTheme.TextSecondary,
                "狂刀周八", InkWashTheme.TextSecondary, "Lv.35", WithAlpha(InkWashTheme.GoldPrimary, 0.04f), InkWashTheme.TextTertiary,
                "3天前在线", InkWashTheme.TextTertiary, false, false);
            AddFriend(_leftPanel, ref y, "吴", WithAlpha(InkWashTheme.QualityCommon, 0.10f), InkWashTheme.TextSecondary,
                "灵狐吴十", InkWashTheme.TextSecondary, "Lv.40", WithAlpha(InkWashTheme.GoldPrimary, 0.04f), InkWashTheme.TextTertiary,
                "1周前在线", InkWashTheme.TextTertiary, false, false);
            AddFriend(_leftPanel, ref y, "郑", WithAlpha(InkWashTheme.QualityCommon, 0.10f), InkWashTheme.TextSecondary,
                "破军郑二", InkWashTheme.TextSecondary, "Lv.44", WithAlpha(InkWashTheme.GoldPrimary, 0.04f), InkWashTheme.TextTertiary,
                "2周前在线", InkWashTheme.TextTertiary, false, false);
            AddFriend(_leftPanel, ref y, "冯", WithAlpha(InkWashTheme.QualityCommon, 0.10f), InkWashTheme.TextSecondary,
                "飞花冯三", InkWashTheme.TextSecondary, "Lv.37", WithAlpha(InkWashTheme.GoldPrimary, 0.04f), InkWashTheme.TextTertiary,
                "1月前在线", InkWashTheme.TextTertiary, false, false);

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
            item.Clicked += () => EmitGoldAtControl(item);
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

            card.AddChild(MakeLabel("⛰ 门派信息", 16f, 12f, 160f, 22f,
                InkWashTheme.GoldPrimary, 16f, InkWashTheme.FontRole.Display, TextAlignment.Near));
            card.AddChild(InfoRow(16f, 42f, "门派", "武当派", InkWashTheme.TextDefault, InkWashTheme.FontRole.Display));
            card.AddChild(InfoRow(16f, 64f, "职位", "弟子", InkWashTheme.TextDefault, InkWashTheme.FontRole.Body));
            card.AddChild(InfoRow(16f, 86f, "贡献", "2,500", InkWashTheme.GoldBright, InkWashTheme.FontRole.Number));
        }

        private void BuildGuildCard(float y)
        {
            var card = new DBox(InkWashTheme.BaseTertiary, InkWashTheme.BorderGoldSubtle, 8f)
            {
                Location = new Float2(16f, y),
                Size = new Float2(LeftW - 32f, 132f),
            };
            _leftPanel.AddChild(card);

            card.AddChild(MakeLabel("🚩 帮会信息", 16f, 12f, 160f, 22f,
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
            manageBtn.ButtonClicked += (b) => EmitGoldAtControl(manageBtn);
            card.AddChild(manageBtn);

            card.AddChild(InfoRow(16f, 44f, "帮会", "天下会", InkWashTheme.TextDefault, InkWashTheme.FontRole.Display));
            card.AddChild(InfoRow(16f, 66f, "等级", "3级 (300/500)", InkWashTheme.TextDefault, InkWashTheme.FontRole.Body));
            card.AddChild(InfoRow(16f, 88f, "职位", "精英", InkWashTheme.TextDefault, InkWashTheme.FontRole.Body));
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
            var avatar = new AvatarDisc("张", 56f, WithAlpha(InkWashTheme.GoldPrimary, 0.12f), InkWashTheme.GoldPrimary, true)
            {
                Location = new Float2(20f, 20f),
                Size = new Float2(56f, 56f),
            };
            card.AddChild(avatar);

            // 姓名 + 标签
            card.AddChild(MakeLabel("剑客张三", 92f, 18f, 130f, 26f,
                InkWashTheme.GoldPrimary, 18f, InkWashTheme.FontRole.Display, TextAlignment.Near));
            var lvTag = new TagPill("Lv.60", WithAlpha(InkWashTheme.GoldPrimary, 0.15f), InkWashTheme.GoldBright, InkWashTheme.FontRole.Number)
            {
                Location = new Float2(228f, 22f),
                Size = new Float2(48f, 18f),
            };
            card.AddChild(lvTag);
            var sectTag = new TagPill("武当派", WithAlpha(InkWashTheme.JadePrimary, 0.12f), InkWashTheme.JadeBright, InkWashTheme.FontRole.Display)
            {
                Location = new Float2(284f, 22f),
                Size = new Float2(52f, 18f),
            };
            card.AddChild(sectTag);

            // 战力 + 在线 + 位置
            card.AddChild(MakeLabel("战力", 92f, 52f, 34f, 18f, InkWashTheme.TextTertiary, 12f, InkWashTheme.FontRole.Body, TextAlignment.Near));
            card.AddChild(MakeLabel("12,345", 128f, 50f, 80f, 22f, InkWashTheme.GoldBright, 14f, InkWashTheme.FontRole.Number, TextAlignment.Near));
            card.AddChild(MakeLabel("●", 216f, 52f, 14f, 18f, InkWashTheme.JadePrimary, 12f, InkWashTheme.FontRole.Body, TextAlignment.Center));
            card.AddChild(MakeLabel("在线", 232f, 52f, 40f, 18f, InkWashTheme.TextSecondary, 12f, InkWashTheme.FontRole.Body, TextAlignment.Near));
            card.AddChild(MakeLabel("📍 武当山", 288f, 52f, 100f, 18f, InkWashTheme.TextDefault, 12f, InkWashTheme.FontRole.Body, TextAlignment.Near));

            // 操作按钮
            var btnWhisper = new InkButton { Variant = InkButtonVariant.Brand, ButtonSize = InkButtonSize.Sm, Text = "私聊",
                AnchorPreset = AnchorPresets.TopLeft, Location = new Float2(92f, 82f), Size = new Float2(60f, 24f) };
            btnWhisper.ButtonClicked += (b) => EmitGoldAtControl(btnWhisper);
            card.AddChild(btnWhisper);
            var btnTeam = new InkButton { Variant = InkButtonVariant.Secondary, ButtonSize = InkButtonSize.Sm, Text = "组队",
                AnchorPreset = AnchorPresets.TopLeft, Location = new Float2(160f, 82f), Size = new Float2(60f, 24f) };
            btnTeam.ButtonClicked += (b) => EmitGoldAtControl(btnTeam);
            card.AddChild(btnTeam);
            var btnTrade = new InkButton { Variant = InkButtonVariant.Secondary, ButtonSize = InkButtonSize.Sm, Text = "交易",
                AnchorPreset = AnchorPresets.TopLeft, Location = new Float2(228f, 82f), Size = new Float2(60f, 24f) };
            btnTrade.ButtonClicked += (b) => EmitGoldAtControl(btnTrade);
            card.AddChild(btnTrade);
            var btnDelete = new InkButton { Variant = InkButtonVariant.Danger, ButtonSize = InkButtonSize.Sm, Text = "删除",
                AnchorPreset = AnchorPresets.TopLeft, Location = new Float2(296f, 82f), Size = new Float2(60f, 24f) };
            btnDelete.ButtonClicked += (b) => EmitGoldAtControl(btnDelete);
            card.AddChild(btnDelete);
        }

        private void BuildChatWindow()
        {
            var chat = new DBox(InkWashTheme.BaseTertiary, InkWashTheme.BorderGoldSubtle, 8f)
            {
                AnchorPreset = AnchorPresets.StretchAll,
                Offsets = new Margin(16f, 16f, 152f, 16f),
            };
            _rightPanel.AddChild(chat);

            // 频道 Tab
            string[] channels = { "世界", "队伍", "门派", "私聊", "区域" };
            float cx = 16f;
            for (int i = 0; i < channels.Length; i++)
            {
                var ct = new ChannelTab(channels[i], i == 0)
                {
                    Location = new Float2(cx, 8f),
                    Size = new Float2(48f, 32f),
                };
                chat.AddChild(ct);
                cx += 56f;
            }

            // 消息区
            _chatMessages = new ContainerControl
            {
                AnchorPreset = AnchorPresets.StretchAll,
                Offsets = new Margin(0f, 0f, 44f, 52f),
                BackgroundColor = WithAlpha(InkWashTheme.Void, 0.50f),
                AutoFocus = false,
            };
            chat.AddChild(_chatMessages);

            float my = 8f;
            AddChatMsg(ref my, "世界", InkWashTheme.GoldPrimary, "剑客张三", InkWashTheme.GoldBright, "有人组队刷试炼塔吗？");
            AddChatMsg(ref my, "门派", InkWashTheme.JadeBright, "师兄", InkWashTheme.TextDefault, "门派任务刷新了，记得去做");
            AddSysMsg(ref my, "———  恭贺 剑客张三 升级至 Lv.60！  ———");
            AddChatMsg(ref my, "世界", InkWashTheme.GoldPrimary, "飞燕李四", InkWashTheme.JadeBright, "丐帮招人，有意加入！");
            AddChatMsg(ref my, "区域", InkWashTheme.TextSecondary, "紫霞钱十", InkWashTheme.QualityEpic, "新装备打造中，还差两个材料");
            AddChatMsg(ref my, "门派", InkWashTheme.JadeBright, "掌门", InkWashTheme.GoldPrimary, "今晚亥时门派聚会，诸位准时参加");
            AddChatMsg(ref my, "世界", InkWashTheme.GoldPrimary, "铁掌赵六", InkWashTheme.TextSecondary, "收购铁掌秘籍，价格面议");
            AddChatMsg(ref my, "区域", InkWashTheme.TextSecondary, "青衣王五", InkWashTheme.QualityRare, "峨眉山下风景独好");
            AddChatMsg(ref my, "世界", InkWashTheme.GoldPrimary, "孤剑赵九", InkWashTheme.VermilionBright, "切磋一场，谁来应战？");

            // 输入区
            var inputBar = new ContainerControl
            {
                AnchorPreset = AnchorPresets.HorizontalStretchBottom,
                Offsets = new Margin(0f, 0f, 0f, 44f),
                BackgroundColor = WithAlpha(InkWashTheme.BaseSecondary, 0.90f),
                AutoFocus = false,
            };
            chat.AddChild(inputBar);
            inputBar.AddChild(new HRule { Location = new Float2(0f, 0f), Size = new Float2(4000f, 1f), LineColor = InkWashTheme.BorderFaint });

            var inputField = new SearchBox { Location = new Float2(12f, 8f), Size = new Float2(0f, 28f), Placeholder = "输入消息，Enter发送..." };
            inputField.AnchorPreset = AnchorPresets.HorizontalStretchTop;
            inputField.Offsets = new Margin(12f, 96f, 8f, 28f);
            inputBar.AddChild(inputField);

            var sendBtn = new SendBtn { Location = new Float2(0f, 8f), Size = new Float2(76f, 28f) };
            sendBtn.AnchorPreset = AnchorPresets.TopRight;
            sendBtn.Offsets = new Margin(0f, 76f, 8f, 28f);
            sendBtn.Clicked += () => EmitGoldAtControl(sendBtn);
            inputBar.AddChild(sendBtn);
        }

        private void AddChatMsg(ref float y, string channel, Color chColor, string speaker, Color spkColor, string text)
        {
            var msg = new ChatMsg(channel, chColor, speaker, spkColor, text)
            {
                Location = new Float2(8f, y),
                Size = new Float2(2000f, 24f),
            };
            _chatMessages.AddChild(msg);
            y += 26f;
        }

        private void AddSysMsg(ref float y, string text)
        {
            var msg = new SysMsg(text)
            {
                Location = new Float2(8f, y),
                Size = new Float2(2000f, 24f),
            };
            _chatMessages.AddChild(msg);
            y += 26f;
        }

        // ===================================================================
        // RefreshLayout
        // ===================================================================

        public void RefreshLayout()
        {
            // 全屏 StretchAll，子控件通过 Anchor 自适应，无需手动重算。
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
            }
            public void SetBorder(Color c) { _border = c; }
            public override void Draw()
            {
                if (!Visible) return;
                base.Draw();
                var r = new Rectangle(Float2.Zero, Size);
                InkRenderHelper.FillRoundedRectangle(r, _radius, _bg);
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
            private readonly bool _online, _selected;
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
                Render2D.DrawText(font, "🔍 " + Placeholder, new Rectangle(8f, 0f, Width - 16f, Height), InkWashTheme.TextTertiary, TextAlignment.Near, TextAlignment.Center, TextWrapping.NoWrap);
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

        /// <summary>聊天频道 Tab（激活=金色+下划线）。</summary>
        private sealed class ChannelTab : ContainerControl
        {
            private readonly string _text;
            private readonly bool _active;
            private bool _hover;
            public ChannelTab(string text, bool active)
            {
                _text = text; _active = active;
                AutoFocus = false;
            }
            public override void OnMouseEnter(Float2 location) { _hover = true; base.OnMouseEnter(location); }
            public override void OnMouseLeave() { _hover = false; base.OnMouseLeave(); }
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
            public ChatMsg(string channel, Color chColor, string speaker, Color spkColor, string text)
            {
                _channel = channel; _chColor = chColor; _speaker = speaker; _spkColor = spkColor; _text = text;
                AutoFocus = false;
            }
            public override void OnMouseEnter(Float2 location) { _hover = true; base.OnMouseEnter(location); }
            public override void OnMouseLeave() { _hover = false; base.OnMouseLeave(); }
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
