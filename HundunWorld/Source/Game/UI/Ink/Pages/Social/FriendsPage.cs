using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.StyleSystem;
using System;

namespace HundunWorld.Game.UI.Ink.Pages.Social
{
    /// <summary>
    /// 江湖交游（好友列表）页面 — 对应 friends.html 设计原型。
    /// <para>
    /// 水墨古风好友管理界面，承担玩家查看好友分组、浏览在线状态、
    /// 查询好友详情与共同回忆的核心入口。
    /// 整体布局沿用 HTML 原型的三栏式结构：
    /// <list type="bullet">
    ///   <item>顶部：标题"江湖交游" + 在线好友数/总数 + 关闭按钮</item>
    ///   <item>左栏：好友分组 Tab（全部/在线/亲密/同门/仇人，5 个垂直 Tab）</item>
    ///   <item>中栏：好友列表（12 名，每条含头像/姓名/等级/门派/在线状态/亲密度进度条）</item>
    ///   <item>右栏：选中好友详情（大头像/称谓/个人信息/亲密度条/共同回忆/操作按钮）</item>
    ///   <item>底部：返回沉浸模式 + 跳转飞鸽传书（NavSocialMail）</item>
    /// </list>
    /// 通过 <see cref="NavigationRequested"/> 事件向路由器暴露导航请求，
    /// 关闭按钮与底部"返回沉浸模式"按钮均触发 <see cref="InkPageDomIds.CombatHud"/>。
    /// </para>
    /// <para>
    /// 当前实现全部使用 mock 数据；后续接入门派系统时，
    /// 通过刷新方法替换列表内容即可。
    /// </para>
    /// </summary>
    public class FriendsPage : ContainerControl, IInkPage
    {
        // ===================================================================
        // 布局常量
        // =======================================================================

        /// <summary>顶部标题栏高度（像素）</summary>
        private const float HeaderHeight = 60f;

        /// <summary>底部导航按钮栏高度（像素）</summary>
        private const float BottomNavHeight = 36f;

        /// <summary>屏幕边距（像素）</summary>
        private const float ScreenEdge = 16f;

        /// <summary>区域间距（像素）</summary>
        private const float RegionGap = 12f;

        /// <summary>左栏分组 Tab 宽度占比（占内容区宽度）</summary>
        private const float LeftRatio = 0.16f;

        /// <summary>右栏好友详情宽度占比（占内容区宽度）</summary>
        private const float RightRatio = 0.38f;

        /// <summary>导航按钮宽度（像素）</summary>
        private const float NavBtnWidth = 140f;

        /// <summary>导航按钮间距（像素）</summary>
        private const float NavBtnGap = 8f;

        /// <summary>分组 Tab 按钮高度（像素）</summary>
        private const float TabBtnHeight = 36f;

        /// <summary>分组 Tab 按钮间距（像素）</summary>
        private const float TabBtnGap = 4f;

        /// <summary>好友列表项高度（像素）</summary>
        private const float FriendItemHeight = 64f;

        /// <summary>好友列表项间距（像素）</summary>
        private const float FriendItemGap = 4f;

        // ===================================================================
        // 子控件引用 — 顶部
        // =======================================================================

        /// <summary>顶部标题栏面板</summary>
        private InkPanel _headerPanel;

        /// <summary>页面标题"江湖交游"</summary>
        private Label _titleLabel;

        /// <summary>在线好友数标签</summary>
        private Label _onlineCountLabel;

        /// <summary>"添加好友"按钮</summary>
        private InkButton _addFriendButton;

        /// <summary>关闭按钮</summary>
        private InkButton _closeButton;

        // ===================================================================
        // 子控件引用 — 左栏分组 Tab
        // =======================================================================

        /// <summary>左栏分组 Tab 面板</summary>
        private InkPanel _tabPanel;

        /// <summary>5 个分组 Tab 按钮（全部/在线/亲密/同门/仇人）</summary>
        private InkButton[] _tabButtons;

        /// <summary>当前激活的 Tab 索引</summary>
        private int _activeTabIndex = 0;

        // ===================================================================
        // 子控件引用 — 中栏好友列表
        // =======================================================================

        /// <summary>中栏好友列表面板</summary>
        private InkPanel _friendListPanel;

        /// <summary>中栏标题"好友列表"</summary>
        private Label _friendListTitle;

        /// <summary>好友列表项容器数组（12 名）</summary>
        private InkListItem[] _friendItems;

        /// <summary>好友头像数组</summary>
        private InkAvatar[] _friendAvatars;

        /// <summary>好友姓名标签数组</summary>
        private Label[] _friendNames;

        /// <summary>好友等级标签数组</summary>
        private Label[] _friendLevels;

        /// <summary>好友门派标签数组</summary>
        private Label[] _friendSects;

        /// <summary>好友在线状态圆点数组</summary>
        private InkDot[] _friendDots;

        /// <summary>好友亲密度进度条数组</summary>
        private InkBar[] _friendIntimacyBars;

        /// <summary>当前选中的好友索引</summary>
        private int _selectedFriendIndex = 0;

        // ===================================================================
        // 子控件引用 — 右栏好友详情
        // =======================================================================

        /// <summary>右栏好友详情面板</summary>
        private InkPanel _detailPanel;

        /// <summary>详情大头像</summary>
        private InkAvatar _detailAvatar;

        /// <summary>详情姓名标签</summary>
        private Label _detailNameLabel;

        /// <summary>详情称谓标签</summary>
        private Label _detailTitleLabel;

        /// <summary>详情：等级数值</summary>
        private Label _detailLevelLabel;

        /// <summary>详情：门派</summary>
        private Label _detailSectLabel;

        /// <summary>详情：性别</summary>
        private Label _detailGenderLabel;

        /// <summary>详情：所在地</summary>
        private Label _detailLocationLabel;

        /// <summary>亲密度数值标签</summary>
        private Label _detailIntimacyValueLabel;

        /// <summary>亲密度进度条</summary>
        private InkBar _detailIntimacyBar;

        /// <summary>共同回忆区标题</summary>
        private Label _memoryTitle;

        /// <summary>共同回忆条目标签数组（4 条）</summary>
        private Label[] _memoryLabels;

        /// <summary>"私聊"按钮</summary>
        private InkButton _whisperButton;

        /// <summary>"组队"按钮</summary>
        private InkButton _teamButton;

        /// <summary>"邀请入派"按钮</summary>
        private InkButton _inviteSectButton;

        /// <summary>"发送邮件"按钮</summary>
        private InkButton _sendMailButton;

        /// <summary>"删除"按钮</summary>
        private InkButton _deleteButton;

        // ===================================================================
        // 子控件引用 — 底部
        // =======================================================================

        /// <summary>底部导航按钮面板</summary>
        private InkPanel _bottomNavPanel;

        /// <summary>"返回沉浸模式"按钮</summary>
        private InkButton _returnHudButton;

        /// <summary>"飞鸽传书"按钮</summary>
        private InkButton _gotoMailButton;

        // ===================================================================
        // 公共 API
        // =======================================================================

        /// <summary>
        /// 导航请求事件。由关闭按钮与底部导航按钮触发，
        /// 参数为 <see cref="InkPageDomIds"/> 中定义的 dom-id 字符串。
        /// </summary>
        public event Action<string> NavigationRequested;

        /// <summary>
        /// 粒子动效系统引用（可选，由外部注入）。
        /// 用于在按钮点击位置触发金粉爆发反馈。
        /// </summary>
        public InkParticleSystem ParticleSystem { get; set; }

        // ===================================================================
        // 构造函数
        // =======================================================================

        /// <summary>
        /// 构造函数：初始化全部子控件并填充 mock 数据。
        /// </summary>
        public FriendsPage()
        {
            try
            {
                AnchorPreset = AnchorPresets.StretchAll;
                Offsets = Margin.Zero;
                BackgroundColor = Color.Transparent;
                ClipChildren = false;
                AutoFocus = false;

                BuildHeader();
                BuildTabPanel();
                BuildFriendList();
                BuildDetailPanel();
                BuildBottomNav();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[FriendsPage] 初始化失败: {ex.Message}");
            }
        }

        // ===================================================================
        // Build 方法
        // =======================================================================

        /// <summary>
        /// 构建顶部标题栏：标题 + 在线好友数 + 添加好友 + 关闭按钮。
        /// </summary>
        private void BuildHeader()
        {
            _headerPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
            };

            _titleLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(20f, 0f),
                Size = new Float2(160f, HeaderHeight),
                Text = "江湖交游",
                TextColor = InkWashTheme.TextGold,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 22f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _headerPanel.AddChild(_titleLabel);

            _onlineCountLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(200f, 0f),
                Size = new Float2(240f, HeaderHeight),
                Text = "在线  5 / 12",
                TextColor = InkWashTheme.TextJade,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 14f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _headerPanel.AddChild(_onlineCountLabel);

            // 添加好友按钮（右上方，RefreshLayout 中靠右定位）
            _addFriendButton = new InkButton
            {
                Variant = InkButtonVariant.Default,
                ButtonSize = InkButtonSize.Sm,
                Text = "添加好友",
                AnchorPreset = AnchorPresets.TopRight,
                Size = new Float2(96f, 32f),
            };
            _addFriendButton.ButtonClicked += (b) => EmitGoldAtButton(b);
            _headerPanel.AddChild(_addFriendButton);

            _closeButton = new InkButton
            {
                Variant = InkButtonVariant.Ghost,
                ButtonSize = InkButtonSize.Sm,
                Text = "✕",
                AnchorPreset = AnchorPresets.TopRight,
                Size = new Float2(32f, 32f),
            };
            _closeButton.ButtonClicked += (b) => OnSystemNavButtonClicked(InkPageDomIds.CombatHud, b);
            _headerPanel.AddChild(_closeButton);

            AddChild(_headerPanel);
        }

        /// <summary>
        /// 构建左栏分组 Tab 面板：5 个垂直 Tab（全部/在线/亲密/同门/仇人）。
        /// </summary>
        private void BuildTabPanel()
        {
            _tabPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
            };

            // 分组标题
            var groupTitle = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, 10f),
                Size = new Float2(160f, 20f),
                Text = "◆ 好友分组",
                TextColor = InkWashTheme.TextBrand,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), 14f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _tabPanel.AddChild(groupTitle);

            // 5 个分组 Tab
            string[] tabNames = { "全部", "在线", "亲密", "同门", "仇人" };
            _tabButtons = new InkButton[tabNames.Length];
            float tabY = 40f;
            for (int i = 0; i < tabNames.Length; i++)
            {
                int capturedIndex = i;
                var btn = new InkButton
                {
                    Variant = (i == _activeTabIndex) ? InkButtonVariant.Default : InkButtonVariant.Ghost,
                    ButtonSize = InkButtonSize.Md,
                    Text = tabNames[i],
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(12f, tabY + i * (TabBtnHeight + TabBtnGap)),
                    Size = new Float2(160f, TabBtnHeight),
                };
                btn.ButtonClicked += (b) => OnTabButtonClicked(capturedIndex, b);
                _tabButtons[i] = btn;
                _tabPanel.AddChild(btn);
            }

            AddChild(_tabPanel);
        }

        /// <summary>
        /// 构建中栏好友列表：12 名好友，每条含头像/姓名/等级/门派/在线圆点/亲密度进度条。
        /// </summary>
        private void BuildFriendList()
        {
            _friendListPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
            };

            _friendListTitle = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(16f, 10f),
                Size = new Float2(300f, 20f),
                Text = "◆ 好友列表（12）",
                TextColor = InkWashTheme.TextBrand,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), 14f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _friendListPanel.AddChild(_friendListTitle);

            // 12 名好友 mock 数据：姓名 / 等级 / 门派 / 在线 / 亲密度(0~1)
            string[][] friends =
            {
                new[] { "剑客张三", "60", "武当派", "1", "0.95" },
                new[] { "飞燕李四", "55", "丐帮",   "1", "0.80" },
                new[] { "狂刀赵六", "52", "明教",   "1", "0.75" },
                new[] { "青衣王五", "48", "峨眉",   "1", "0.65" },
                new[] { "幻影孙七", "45", "唐门",   "1", "0.55" },
                new[] { "药师周八", "40", "少林",   "0", "0.60" },
                new[] { "铁掌郑十", "42", "昆仑",   "0", "0.50" },
                new[] { "琴音吴九", "38", "嵩山",   "0", "0.40" },
                new[] { "冰心钱十一","36", "天山",   "0", "0.30" },
                new[] { "紫霞钱十", "45", "唐门",   "1", "0.45" },
                new[] { "孤剑赵九", "52", "少林",   "1", "0.70" },
                new[] { "玉面孙七", "38", "峨眉",   "0", "0.35" },
            };

            _friendItems = new InkListItem[friends.Length];
            _friendAvatars = new InkAvatar[friends.Length];
            _friendNames = new Label[friends.Length];
            _friendLevels = new Label[friends.Length];
            _friendSects = new Label[friends.Length];
            _friendDots = new InkDot[friends.Length];
            _friendIntimacyBars = new InkBar[friends.Length];

            float listStartY = 40f;
            for (int i = 0; i < friends.Length; i++)
            {
                bool isOnline = friends[i][3] == "1";
                float intimacy = float.Parse(friends[i][4]);

                var item = new InkListItem
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(12f, listStartY + i * (FriendItemHeight + FriendItemGap)),
                    Size = new Float2(360f, FriendItemHeight),
                    Active = (i == _selectedFriendIndex),
                };
                _friendItems[i] = item;
                _friendListPanel.AddChild(item);

                // 在线圆点
                var dot = new InkDot
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(8f, (FriendItemHeight - 8f) * 0.5f),
                    Size = new Float2(8f, 8f),
                    Online = isOnline,
                };
                _friendDots[i] = dot;
                item.AddChild(dot);

                // 头像
                var avatar = new InkAvatar
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(24f, (FriendItemHeight - 36f) * 0.5f),
                    Size = new Float2(36f, 36f),
                };
                _friendAvatars[i] = avatar;
                item.AddChild(avatar);

                // 姓名 + 等级
                var nameLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(70f, 8f),
                    Size = new Float2(180f, 20f),
                    Text = friends[i][0] + "  Lv." + friends[i][1],
                    TextColor = isOnline ? InkWashTheme.TextDefault : InkWashTheme.TextTertiary,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), 13f),
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                };
                _friendNames[i] = nameLabel;
                item.AddChild(nameLabel);

                // 门派
                var sectLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(70f, 28f),
                    Size = new Float2(180f, 16f),
                    Text = friends[i][2],
                    TextColor = InkWashTheme.TextSecondary,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 11f),
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                };
                _friendSects[i] = sectLabel;
                item.AddChild(sectLabel);

                // 等级（单独存放便于刷新）
                var levelLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(0f, 0f),
                    Size = new Float2(0f, 0f),
                    Text = friends[i][1],
                    Visible = false,
                };
                _friendLevels[i] = levelLabel;
                item.AddChild(levelLabel);

                // 亲密度进度条
                var intimacyBar = new InkBar
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(70f, 46f),
                    Size = new Float2(260f, 6f),
                    Value = intimacy,
                    FillVariant = InkBarFillVariant.Jade,
                };
                _friendIntimacyBars[i] = intimacyBar;
                item.AddChild(intimacyBar);
            }

            AddChild(_friendListPanel);
        }

        /// <summary>
        /// 构建右栏好友详情：大头像 + 称谓 + 个人信息 + 亲密度条 + 共同回忆 + 操作按钮。
        /// </summary>
        private void BuildDetailPanel()
        {
            _detailPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
            };

            // 大头像（72×72）
            _detailAvatar = new InkAvatar
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(20f, 16f),
                Size = new Float2(72f, 72f),
            };
            _detailPanel.AddChild(_detailAvatar);

            // 姓名
            _detailNameLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(108f, 16f),
                Size = new Float2(220f, 28f),
                Text = "剑客张三",
                TextColor = InkWashTheme.TextGold,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 20f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _detailPanel.AddChild(_detailNameLabel);

            // 称谓
            _detailTitleLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(108f, 48f),
                Size = new Float2(220f, 20f),
                Text = "称谓：武林豪侠",
                TextColor = InkWashTheme.TextBrand,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), 13f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _detailPanel.AddChild(_detailTitleLabel);

            // 在线状态
            var statusLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(108f, 70f),
                Size = new Float2(220f, 18f),
                Text = "● 在线",
                TextColor = InkWashTheme.TextJade,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 12f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _detailPanel.AddChild(statusLabel);

            // 个人信息区标题
            var infoTitle = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(20f, 100f),
                Size = new Float2(360f, 20f),
                Text = "◆ 个人信息",
                TextColor = InkWashTheme.TextBrand,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), 13f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _detailPanel.AddChild(infoTitle);

            // 4 项个人信息：等级 / 门派 / 性别 / 所在地
            _detailLevelLabel = CreateInfoLabel(20f, 124f, "等级", "Lv.60");
            _detailSectLabel = CreateInfoLabel(200f, 124f, "门派", "武当派");
            _detailGenderLabel = CreateInfoLabel(20f, 148f, "性别", "男");
            _detailLocationLabel = CreateInfoLabel(200f, 148f, "所在地", "武当山 · 紫霄宫");
            _detailPanel.AddChild(_detailLevelLabel);
            _detailPanel.AddChild(_detailSectLabel);
            _detailPanel.AddChild(_detailGenderLabel);
            _detailPanel.AddChild(_detailLocationLabel);

            // 亲密度区
            var intimacyTitle = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(20f, 178f),
                Size = new Float2(360f, 20f),
                Text = "◆ 亲密度",
                TextColor = InkWashTheme.TextBrand,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), 13f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _detailPanel.AddChild(intimacyTitle);

            _detailIntimacyValueLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(20f, 202f),
                Size = new Float2(360f, 18f),
                Text = "亲密度  950 / 1000  （★★★★★）",
                TextColor = InkWashTheme.TextGold,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 12f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _detailPanel.AddChild(_detailIntimacyValueLabel);

            _detailIntimacyBar = new InkBar
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(20f, 224f),
                Size = new Float2(360f, 8f),
                Value = 0.95f,
                FillVariant = InkBarFillVariant.Gold,
            };
            _detailPanel.AddChild(_detailIntimacyBar);

            // 共同回忆区
            _memoryTitle = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(20f, 246f),
                Size = new Float2(360f, 20f),
                Text = "◆ 共同回忆",
                TextColor = InkWashTheme.TextBrand,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), 13f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _detailPanel.AddChild(_memoryTitle);

            string[] memories =
            {
                "三日 ▸ 同闯少林藏经阁，得《易筋经》残卷",
                "上周 ▸ 共赴武当论剑，败于紫虚真人",
                "今晨 ▸ 切磋三百招，胜负各半",
                "上月 ▸ 同立襄阳城退敌之约",
            };
            _memoryLabels = new Label[memories.Length];
            for (int i = 0; i < memories.Length; i++)
            {
                var memLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(24f, 270f + i * 20f),
                    Size = new Float2(360f, 18f),
                    Text = memories[i],
                    TextColor = InkWashTheme.TextSecondary,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 12f),
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                };
                _memoryLabels[i] = memLabel;
                _detailPanel.AddChild(memLabel);
            }

            // 操作按钮区（5 个按钮：私聊/组队/邀请入派/发送邮件/删除）
            float btnY = 270f + memories.Length * 20f + 16f;
            float btnWidth = 110f;
            float btnGap = 6f;

            _whisperButton = new InkButton
            {
                Variant = InkButtonVariant.Primary,
                ButtonSize = InkButtonSize.Sm,
                Text = "私聊",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(20f, btnY),
                Size = new Float2(btnWidth, 32f),
            };
            _whisperButton.ButtonClicked += (b) => EmitGoldAtButton(b);
            _detailPanel.AddChild(_whisperButton);

            _teamButton = new InkButton
            {
                Variant = InkButtonVariant.Default,
                ButtonSize = InkButtonSize.Sm,
                Text = "组队",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(20f + (btnWidth + btnGap) * 1f, btnY),
                Size = new Float2(btnWidth, 32f),
            };
            _teamButton.ButtonClicked += (b) => EmitGoldAtButton(b);
            _detailPanel.AddChild(_teamButton);

            _inviteSectButton = new InkButton
            {
                Variant = InkButtonVariant.Default,
                ButtonSize = InkButtonSize.Sm,
                Text = "邀请入派",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(20f + (btnWidth + btnGap) * 2f, btnY),
                Size = new Float2(btnWidth, 32f),
            };
            _inviteSectButton.ButtonClicked += (b) => EmitGoldAtButton(b);
            _detailPanel.AddChild(_inviteSectButton);

            _sendMailButton = new InkButton
            {
                Variant = InkButtonVariant.Default,
                ButtonSize = InkButtonSize.Sm,
                Text = "发送邮件",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(20f, btnY + 36f),
                Size = new Float2(btnWidth, 32f),
            };
            _sendMailButton.ButtonClicked += (b) =>
                OnSystemNavButtonClicked(InkPageDomIds.NavSocialMail, b);
            _detailPanel.AddChild(_sendMailButton);

            _deleteButton = new InkButton
            {
                Variant = InkButtonVariant.Vermilion,
                ButtonSize = InkButtonSize.Sm,
                Text = "删除好友",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(20f + (btnWidth + btnGap) * 1f, btnY + 36f),
                Size = new Float2(btnWidth, 32f),
            };
            _deleteButton.ButtonClicked += (b) => EmitGoldAtButton(b);
            _detailPanel.AddChild(_deleteButton);

            AddChild(_detailPanel);
        }

        /// <summary>
        /// 构建底部导航按钮栏：返回沉浸模式 / 飞鸽传书。
        /// </summary>
        private void BuildBottomNav()
        {
            _bottomNavPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.BottomLeft,
            };

            _returnHudButton = new InkButton
            {
                Variant = InkButtonVariant.Ghost,
                ButtonSize = InkButtonSize.Md,
                Text = "返回沉浸模式",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, 0f),
                Size = new Float2(NavBtnWidth, BottomNavHeight),
            };
            _returnHudButton.ButtonClicked += (b) => OnSystemNavButtonClicked(InkPageDomIds.CombatHud, b);
            _bottomNavPanel.AddChild(_returnHudButton);

            _gotoMailButton = new InkButton
            {
                Variant = InkButtonVariant.Default,
                ButtonSize = InkButtonSize.Md,
                Text = "飞鸽传书",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(NavBtnWidth + NavBtnGap, 0f),
                Size = new Float2(NavBtnWidth, BottomNavHeight),
            };
            _gotoMailButton.ButtonClicked += (b) =>
                OnSystemNavButtonClicked(InkPageDomIds.NavSocialMail, b);
            _bottomNavPanel.AddChild(_gotoMailButton);

            AddChild(_bottomNavPanel);
        }

        // ===================================================================
        // 辅助构建方法
        // =======================================================================

        /// <summary>
        /// 创建一个个人信息键值标签（左标签 + 右数值，共占一行）。
        /// </summary>
        private Label CreateInfoLabel(float x, float y, string key, string value)
        {
            return new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(x, y),
                Size = new Float2(180f, 20f),
                Text = key + "：  " + value,
                TextColor = InkWashTheme.TextDefault,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 12f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
        }

        // ===================================================================
        // 事件处理
        // =======================================================================

        /// <summary>
        /// 顶部分组 Tab 按钮点击处理：切换激活态并发射金粉粒子。
        /// </summary>
        private void OnTabButtonClicked(int tabIndex, Button sourceButton)
        {
            try
            {
                EmitGoldAtButton(sourceButton);
                _activeTabIndex = tabIndex;
                ApplyTabHighlight();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[FriendsPage] Tab 切换失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 根据当前激活的 Tab 索引更新所有 Tab 按钮的视觉状态。
        /// </summary>
        private void ApplyTabHighlight()
        {
            if (_tabButtons == null)
                return;
            for (int i = 0; i < _tabButtons.Length; i++)
            {
                if (_tabButtons[i] == null)
                    continue;
                _tabButtons[i].Variant = (i == _activeTabIndex)
                    ? InkButtonVariant.Default
                    : InkButtonVariant.Ghost;
            }
        }

        /// <summary>
        /// 系统导航按钮点击处理：发射金粉粒子 + 触发导航请求。
        /// </summary>
        private void OnSystemNavButtonClicked(string domId, Button sourceButton)
        {
            try
            {
                EmitGoldAtButton(sourceButton);
                NavigationRequested?.Invoke(domId);
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError(
                    $"[FriendsPage] NavigationRequested({domId}) 触发失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 在按钮中心位置触发金粉爆发粒子反馈。
        /// </summary>
        private void EmitGoldAtButton(Button button)
        {
            try
            {
                if (ParticleSystem == null || button == null)
                    return;

                var buttonCenter = new Float2(button.Width * 0.5f, button.Height * 0.5f);
                var screenPos = button.PointToScreen(buttonCenter);
                var localPos = ParticleSystem.PointFromScreen(screenPos);
                ParticleSystem.EmitGoldBurst(localPos, count: 14, isLarge: false);
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogWarning($"[FriendsPage] EmitGoldAtButton 失败: {ex.Message}");
            }
        }

        // ===================================================================
        // IInkPage 实现
        // =======================================================================

        /// <inheritdoc />
        public void RefreshLayout()
        {
            try
            {
                float w = Width;
                float h = Height;
                float panelX = ScreenEdge;
                float panelW = w - ScreenEdge * 2f;

                // 1. 顶部标题栏：顶部全宽
                if (_headerPanel != null)
                {
                    _headerPanel.Location = new Float2(panelX, ScreenEdge);
                    _headerPanel.Size = new Float2(panelW, HeaderHeight);

                    // 添加好友 / 关闭按钮靠右排列
                    float rightX = panelW - 8f;
                    if (_closeButton != null)
                    {
                        _closeButton.Location = new Float2(rightX - 32f, (HeaderHeight - 32f) * 0.5f);
                        rightX -= 32f + 8f;
                    }
                    if (_addFriendButton != null)
                    {
                        _addFriendButton.Location = new Float2(rightX - 96f, (HeaderHeight - 32f) * 0.5f);
                    }
                }

                // 2. 底部导航栏：底部全宽
                float bottomNavY = h - ScreenEdge - BottomNavHeight;
                if (_bottomNavPanel != null)
                {
                    _bottomNavPanel.Location = new Float2(panelX, bottomNavY);
                    _bottomNavPanel.Size = new Float2(panelW, BottomNavHeight);
                }

                // 3. 内容区：顶部下方 → 底部上方
                float contentTop = ScreenEdge + HeaderHeight + RegionGap;
                float contentBottom = bottomNavY - RegionGap;
                float contentH = contentBottom - contentTop;
                if (contentH < 100f)
                    contentH = 100f;

                float leftW = panelW * LeftRatio;
                float rightW = panelW * RightRatio;
                float centerW = panelW - leftW - rightW - RegionGap * 2f;

                // 4. 左栏分组 Tab
                if (_tabPanel != null)
                {
                    _tabPanel.Location = new Float2(panelX, contentTop);
                    _tabPanel.Size = new Float2(leftW, contentH);

                    // Tab 按钮宽度按列宽自适应
                    float tabW = leftW - 24f;
                    if (_tabButtons != null)
                    {
                        float tabY = 40f;
                        for (int i = 0; i < _tabButtons.Length; i++)
                        {
                            if (_tabButtons[i] == null)
                                continue;
                            _tabButtons[i].Location = new Float2(12f, tabY + i * (TabBtnHeight + TabBtnGap));
                            _tabButtons[i].Size = new Float2(tabW, TabBtnHeight);
                        }
                    }
                }

                // 5. 中栏好友列表
                if (_friendListPanel != null)
                {
                    float listX = panelX + leftW + RegionGap;
                    _friendListPanel.Location = new Float2(listX, contentTop);
                    _friendListPanel.Size = new Float2(centerW, contentH);

                    // 好友列表项按列宽重新布局
                    float itemW = centerW - 24f;
                    float innerW = itemW - 90f;
                    if (_friendItems != null)
                    {
                        float listStartY = 40f;
                        for (int i = 0; i < _friendItems.Length; i++)
                        {
                            if (_friendItems[i] == null)
                                continue;
                            _friendItems[i].Location = new Float2(12f, listStartY + i * (FriendItemHeight + FriendItemGap));
                            _friendItems[i].Size = new Float2(itemW, FriendItemHeight);

                            // 亲密度进度条按宽度自适应
                            if (_friendIntimacyBars != null && _friendIntimacyBars[i] != null)
                                _friendIntimacyBars[i].Size = new Float2(Mathf.Max(120f, innerW - 20f), 6f);
                            if (_friendNames != null && _friendNames[i] != null)
                                _friendNames[i].Size = new Float2(Mathf.Max(150f, innerW), 20f);
                            if (_friendSects != null && _friendSects[i] != null)
                                _friendSects[i].Size = new Float2(Mathf.Max(150f, innerW), 16f);
                        }
                    }
                }

                // 6. 右栏好友详情
                if (_detailPanel != null)
                {
                    float detailX = panelX + leftW + RegionGap + centerW + RegionGap;
                    _detailPanel.Location = new Float2(detailX, contentTop);
                    _detailPanel.Size = new Float2(rightW, contentH);

                    // 内部信息行宽度自适应
                    float innerW = rightW - 40f;
                    if (_detailNameLabel != null)
                        _detailNameLabel.Size = new Float2(Mathf.Max(160f, innerW - 112f), 28f);
                    if (_detailTitleLabel != null)
                        _detailTitleLabel.Size = new Float2(Mathf.Max(160f, innerW - 112f), 20f);
                    if (_detailIntimacyValueLabel != null)
                        _detailIntimacyValueLabel.Size = new Float2(innerW, 18f);
                    if (_detailIntimacyBar != null)
                        _detailIntimacyBar.Size = new Float2(innerW, 8f);
                    if (_memoryLabels != null)
                    {
                        for (int i = 0; i < _memoryLabels.Length; i++)
                        {
                            if (_memoryLabels[i] != null)
                                _memoryLabels[i].Size = new Float2(innerW, 18f);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[FriendsPage] RefreshLayout 失败: {ex.Message}");
            }
        }

        /// <inheritdoc />
        public override void OnParentResized()
        {
            base.OnParentResized();
            RefreshLayout();
        }
    }
}
