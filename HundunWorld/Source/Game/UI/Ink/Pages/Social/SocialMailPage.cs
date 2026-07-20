using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.StyleSystem;
using System;

namespace HundunWorld.Game.UI.Ink.Pages.Social
{
    /// <summary>
    /// 飞鸽传书（社交邮件）页面 — 对应 mail.html 设计原型。
    /// <para>
    /// 水墨古风邮件管理界面，承担玩家浏览系统/帮派/玩家邮件、阅读信笺正文、
    /// 领取附件赏赐与执行邮件操作（回复/转发/删除）的核心入口。
    /// 整体布局沿用 HTML 原型的三栏式结构：
    /// <list type="bullet">
    ///   <item>顶部：标题"飞鸽传书" + 未读邮件数 + 写信按钮 + 关闭按钮</item>
    ///   <item>左栏：检索框 + 4 个分类 Tab（全部/系统/玩家/帮派）+ 邮件列表（8 封，按分组排序）</item>
    ///   <item>中栏：选中邮件的发件人信息头 + 主题 + 信纸正文 + 赏赐明细</item>
    ///   <item>右栏：信物附件标题 + 附件列表（4 件）+ 一键领取按钮 + 邮件操作按钮</item>
    ///   <item>底部：返回沉浸模式 + 跳转好友列表（NavFriends）</item>
    /// </list>
    /// 通过 <see cref="NavigationRequested"/> 事件向路由器暴露导航请求，
    /// 关闭按钮与底部"返回沉浸模式"按钮均触发 <see cref="InkPageDomIds.CombatHud"/>。
    /// </para>
    /// <para>
    /// 当前实现全部使用 mock 数据；后续接入邮件系统时，
    /// 通过刷新方法替换邮件列表与附件内容即可。
    /// </para>
    /// </summary>
    public class SocialMailPage : ContainerControl, IInkPage
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

        /// <summary>左栏邮件列表面板宽度占比（占内容区宽度）</summary>
        private const float LeftRatio = 0.22f;

        /// <summary>右栏附件面板宽度占比（占内容区宽度）</summary>
        private const float RightRatio = 0.28f;

        /// <summary>导航按钮宽度（像素）</summary>
        private const float NavBtnWidth = 140f;

        /// <summary>导航按钮间距（像素）</summary>
        private const float NavBtnGap = 8f;

        /// <summary>分类 Tab 按钮高度（像素）</summary>
        private const float TabBtnHeight = 32f;

        /// <summary>分类 Tab 按钮间距（像素）</summary>
        private const float TabBtnGap = 4f;

        /// <summary>邮件列表项高度（像素）</summary>
        private const float MailItemHeight = 56f;

        /// <summary>邮件列表项间距（像素）</summary>
        private const float MailItemGap = 4f;

        /// <summary>分组标签高度（像素）</summary>
        private const float GroupLabelHeight = 24f;

        /// <summary>附件列表项高度（像素）</summary>
        private const float AttachItemHeight = 56f;

        /// <summary>附件列表项间距（像素）</summary>
        private const float AttachItemGap = 8f;

        // ===================================================================
        // 子控件引用 — 顶部
        // =======================================================================

        /// <summary>顶部标题栏面板</summary>
        private InkPanel _headerPanel;

        /// <summary>页面标题"飞鸽传书"</summary>
        private Label _titleLabel;

        /// <summary>未读邮件数标签</summary>
        private Label _unreadCountLabel;

        /// <summary>"写信"按钮</summary>
        private InkButton _composeButton;

        /// <summary>关闭按钮</summary>
        private InkButton _closeButton;

        // ===================================================================
        // 子控件引用 — 左栏邮件列表
        // =======================================================================

        /// <summary>左栏邮件列表面板</summary>
        private InkPanel _mailListPanel;

        /// <summary>检索框容器面板（视觉装饰）</summary>
        private InkPanel _searchBoxPanel;

        /// <summary>检索框占位文字标签</summary>
        private Label _searchPlaceholderLabel;

        /// <summary>4 个分类 Tab 按钮（全部/系统/玩家/帮派）</summary>
        private InkButton[] _tabButtons;

        /// <summary>当前激活的 Tab 索引</summary>
        private int _activeTabIndex = 0;

        /// <summary>8 封邮件的列表项容器</summary>
        private InkListItem[] _mailItems;

        /// <summary>邮件未读圆点数组（未读为金色，已读为透明）</summary>
        private InkDot[] _mailDots;

        /// <summary>邮件发件人头像首字标签数组（系/帮/张/李/王 等）</summary>
        private Label[] _mailAvatarLabels;

        /// <summary>邮件发件人姓名标签数组</summary>
        private Label[] _mailSenderLabels;

        /// <summary>邮件主题标签数组</summary>
        private Label[] _mailSubjectLabels;

        /// <summary>邮件时间标签数组</summary>
        private Label[] _mailTimeLabels;

        /// <summary>邮件附件标识标签数组（显示"附"字或留空）</summary>
        private Label[] _mailAttachMarks;

        /// <summary>3 个分组标题标签（系统 / 帮派 / 玩家）</summary>
        private Label[] _groupLabels;

        /// <summary>当前选中的邮件索引</summary>
        private int _selectedMailIndex = 0;

        // ===================================================================
        // 子控件引用 — 中栏邮件详情
        // =======================================================================

        /// <summary>中栏邮件详情面板</summary>
        private InkPanel _detailPanel;

        /// <summary>详情发件人头像首字（大）</summary>
        private Label _detailAvatarLabel;

        /// <summary>详情发件人姓名标签</summary>
        private Label _detailSenderLabel;

        /// <summary>详情邮件分类徽章标签（系统/帮派/玩家）</summary>
        private Label _detailCategoryBadge;

        /// <summary>详情发送时间标签</summary>
        private Label _detailTimeLabel;

        /// <summary>详情附件标识标签（"附有赏赐"）</summary>
        private Label _detailAttachHintLabel;

        /// <summary>详情邮件主题标签</summary>
        private Label _detailSubjectLabel;

        /// <summary>详情主题装饰横线下的标签（如"庆典 · 论剑"）</summary>
        private Label _detailTagLabel;

        /// <summary>信纸正文段落标签数组（5 段：少侠台鉴 / 正文1 / 正文2 / 正文3 / 落款）</summary>
        private Label[] _letterBodyLabels;

        /// <summary>赏赐明细区标题</summary>
        private Label _rewardTitleLabel;

        /// <summary>赏赐明细 4 个物品条目容器</summary>
        private InkPanel[] _rewardItemPanels;

        /// <summary>赏赐明细 4 个物品名称标签</summary>
        private Label[] _rewardItemNameLabels;

        /// <summary>赏赐明细 4 个物品数量标签</summary>
        private Label[] _rewardItemQtyLabels;

        // ===================================================================
        // 子控件引用 — 右栏附件面板
        // =======================================================================

        /// <summary>右栏附件面板</summary>
        private InkPanel _attachmentPanel;

        /// <summary>附件标题标签</summary>
        private Label _attachmentTitleLabel;

        /// <summary>附件数量徽章标签（如"4 件"）</summary>
        private Label _attachmentCountLabel;

        /// <summary>4 个附件条目容器</summary>
        private InkPanel[] _attachItemPanels;

        /// <summary>4 个附件图标标签（用首字代替图标）</summary>
        private Label[] _attachIconLabels;

        /// <summary>4 个附件名称标签</summary>
        private Label[] _attachNameLabels;

        /// <summary>4 个附件副标题标签（如"修炼资材"）</summary>
        private Label[] _attachSubtitleLabels;

        /// <summary>4 个附件数量标签</summary>
        private Label[] _attachQtyLabels;

        /// <summary>4 个附件领取状态标签（如"已领取"）</summary>
        private Label[] _attachClaimLabels;

        /// <summary>剩余可领取数量提示标签</summary>
        private Label _claimHintLabel;

        /// <summary>"一键领取"按钮</summary>
        private InkButton _claimAllButton;

        /// <summary>"回复"按钮</summary>
        private InkButton _replyButton;

        /// <summary>"转发"按钮</summary>
        private InkButton _forwardButton;

        /// <summary>"删除"按钮</summary>
        private InkButton _deleteButton;

        // ===================================================================
        // 子控件引用 — 底部
        // =======================================================================

        /// <summary>底部导航按钮面板</summary>
        private InkPanel _bottomNavPanel;

        /// <summary>"返回沉浸模式"按钮</summary>
        private InkButton _returnHudButton;

        /// <summary>"好友列表"按钮（跳转 NavFriends）</summary>
        private InkButton _gotoFriendsButton;

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
        public SocialMailPage()
        {
            try
            {
                AnchorPreset = AnchorPresets.StretchAll;
                Offsets = Margin.Zero;
                BackgroundColor = Color.Transparent;
                ClipChildren = false;
                AutoFocus = false;

                BuildHeader();
                BuildMailList();
                BuildDetailPanel();
                BuildAttachmentPanel();
                BuildBottomNav();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[SocialMailPage] 初始化失败: {ex.Message}");
            }
        }

        // ===================================================================
        // Build 方法
        // =======================================================================

        /// <summary>
        /// 构建顶部标题栏：标题 + 未读邮件数 + 写信按钮 + 关闭按钮。
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
                Text = "飞鸽传书",
                TextColor = InkWashTheme.TextGold,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 22f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _headerPanel.AddChild(_titleLabel);

            _unreadCountLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(200f, 0f),
                Size = new Float2(240f, HeaderHeight),
                Text = "未读  3 封",
                TextColor = InkWashTheme.TextJade,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 14f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _headerPanel.AddChild(_unreadCountLabel);

            // 写信按钮（右上方，RefreshLayout 中靠右定位）
            _composeButton = new InkButton
            {
                Variant = InkButtonVariant.Default,
                ButtonSize = InkButtonSize.Sm,
                Text = "写信",
                AnchorPreset = AnchorPresets.TopRight,
                Size = new Float2(96f, 32f),
            };
            _composeButton.ButtonClicked += (b) => EmitGoldAtButton(b);
            _headerPanel.AddChild(_composeButton);

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
        /// 构建左栏邮件列表：检索框 + 4 个分类 Tab + 3 个分组标题 + 8 封邮件条目。
        /// <para>
        /// 8 封 mock 邮件分布：
        /// <list type="bullet">
        ///   <item>系统（3 封）：周末庆典·论剑豪礼（未读+附件，选中）/ 服务器维护公告 / 充值灵石到账通知</item>
        ///   <item>帮派（2 封）：帮派战结算奖励（未读+附件）/ 每周贡献奖励（已读+附件）</item>
        ///   <item>玩家（3 封）：切磋之约·紫霄宫（已读+附件）/ 西域奇遇相告（未读）/ 商队护送谢礼（已读）</item>
        /// </list>
        /// </para>
        /// </summary>
        private void BuildMailList()
        {
            _mailListPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
            };

            // 检索框（视觉装饰：占位文字标签）
            _searchBoxPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, 10f),
                Size = new Float2(280f, 36f),
            };
            _searchPlaceholderLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, 0f),
                Size = new Float2(240f, 36f),
                Text = "◆ 检索信件关键词",
                TextColor = InkWashTheme.TextSecondary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 12f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _searchBoxPanel.AddChild(_searchPlaceholderLabel);
            _mailListPanel.AddChild(_searchBoxPanel);

            // 4 个分类 Tab（全部/系统/玩家/帮派）
            string[] tabNames = { "全部", "系统", "玩家", "帮派" };
            _tabButtons = new InkButton[tabNames.Length];
            float tabY = 56f;
            for (int i = 0; i < tabNames.Length; i++)
            {
                int capturedIndex = i;
                var btn = new InkButton
                {
                    Variant = (i == _activeTabIndex) ? InkButtonVariant.Default : InkButtonVariant.Ghost,
                    ButtonSize = InkButtonSize.Sm,
                    Text = tabNames[i],
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(12f + i * (72f + TabBtnGap), tabY),
                    Size = new Float2(72f, TabBtnHeight),
                };
                btn.ButtonClicked += (b) => OnTabButtonClicked(capturedIndex, b);
                _tabButtons[i] = btn;
                _mailListPanel.AddChild(btn);
            }

            // 3 个分组标题（系统 / 帮派 / 玩家）— 位于各分组首封邮件之前
            _groupLabels = new Label[3];
            string[] groupNames = { "◆ 系统  3", "◆ 帮派  2", "◆ 玩家  3" };
            Color[] groupColors =
            {
                InkWashTheme.TextGold,
                InkWashTheme.TextJade,
                InkWashTheme.TextBrand,
            };
            for (int i = 0; i < _groupLabels.Length; i++)
            {
                _groupLabels[i] = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(16f, 100f + i * 200f),
                    Size = new Float2(280f, GroupLabelHeight),
                    Text = groupNames[i],
                    TextColor = groupColors[i],
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), 13f),
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                };
                _mailListPanel.AddChild(_groupLabels[i]);
            }

            // 8 封 mock 邮件数据
            // 字段顺序：发件人首字 / 发件人姓名 / 主题 / 时间 / 分组索引 / 是否未读 / 是否有附件
            string[][] mails =
            {
                new[] { "系", "系统官府", "周末庆典 · 论剑豪礼", "今日 辰时", "0", "1", "1" },
                new[] { "系", "系统官府", "服务器维护公告",     "今日 卯时", "0", "0", "0" },
                new[] { "系", "系统官府", "充值灵石到账通知",   "三日前",     "0", "0", "0" },
                new[] { "帮", "玄武堂",   "帮派战结算奖励",     "昨日",       "1", "1", "1" },
                new[] { "帮", "朱雀堂",   "每周贡献奖励",       "七月十二",   "1", "0", "1" },
                new[] { "张", "剑客张三", "切磋之约 · 紫霄宫",  "昨日",       "2", "0", "1" },
                new[] { "李", "飞燕李四", "西域奇遇相告",       "三日前",     "2", "1", "0" },
                new[] { "王", "玄武王五", "商队护送谢礼",       "七月十一",   "2", "0", "0" },
            };

            _mailItems = new InkListItem[mails.Length];
            _mailDots = new InkDot[mails.Length];
            _mailAvatarLabels = new Label[mails.Length];
            _mailSenderLabels = new Label[mails.Length];
            _mailSubjectLabels = new Label[mails.Length];
            _mailTimeLabels = new Label[mails.Length];
            _mailAttachMarks = new Label[mails.Length];

            // 分组起始 Y 坐标：系统 3 项 / 帮派 2 项 / 玩家 3 项
            float[] groupStartY = { 128f, 128f + 3 * (MailItemHeight + MailItemGap) + GroupLabelHeight + 4f,
                                     128f + 3 * (MailItemHeight + MailItemGap) + GroupLabelHeight + 4f
                                          + 2 * (MailItemHeight + MailItemGap) + GroupLabelHeight + 4f };
            int[] groupCounters = { 0, 0, 0 };

            for (int i = 0; i < mails.Length; i++)
            {
                int groupIdx = int.Parse(mails[i][4]);
                bool isUnread = mails[i][5] == "1";
                bool hasAttach = mails[i][6] == "1";
                int slot = groupCounters[groupIdx]++;
                float y = groupStartY[groupIdx] + slot * (MailItemHeight + MailItemGap);

                var item = new InkListItem
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(12f, y),
                    Size = new Float2(280f, MailItemHeight),
                    Active = (i == _selectedMailIndex),
                };
                _mailItems[i] = item;
                _mailListPanel.AddChild(item);

                // 未读圆点
                var dot = new InkDot
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(8f, (MailItemHeight - 8f) * 0.5f),
                    Size = new Float2(8f, 8f),
                    Online = isUnread,
                };
                _mailDots[i] = dot;
                item.AddChild(dot);

                // 头像首字（系/帮/张/李/王）
                Color avatarColor = groupIdx switch
                {
                    0 => InkWashTheme.TextGold,
                    1 => InkWashTheme.TextJade,
                    _ => InkWashTheme.TextBrand,
                };
                var avatarLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(24f, (MailItemHeight - 32f) * 0.5f),
                    Size = new Float2(32f, 32f),
                    Text = mails[i][0],
                    TextColor = avatarColor,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 16f),
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                };
                _mailAvatarLabels[i] = avatarLabel;
                item.AddChild(avatarLabel);

                // 发件人姓名（左上）
                var senderLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(64f, 8f),
                    Size = new Float2(140f, 18f),
                    Text = mails[i][1],
                    TextColor = isUnread ? InkWashTheme.TextDefault : InkWashTheme.TextSecondary,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 13f),
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                };
                _mailSenderLabels[i] = senderLabel;
                item.AddChild(senderLabel);

                // 时间（右上）
                var timeLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopRight,
                    Location = new Float2(-72f, 8f),
                    Size = new Float2(64f, 18f),
                    Text = mails[i][3],
                    TextColor = InkWashTheme.TextSecondary,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 11f),
                    HorizontalAlignment = TextAlignment.Far,
                    VerticalAlignment = TextAlignment.Center,
                };
                _mailTimeLabels[i] = timeLabel;
                item.AddChild(timeLabel);

                // 主题（左下）
                var subjectLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(64f, 28f),
                    Size = new Float2(180f, 18f),
                    Text = mails[i][2],
                    TextColor = isUnread ? InkWashTheme.TextSecondary : InkWashTheme.TextSecondary,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 12f),
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                };
                _mailSubjectLabels[i] = subjectLabel;
                item.AddChild(subjectLabel);

                // 附件标识（右下，仅 hasAttach 时显示"附"字）
                if (hasAttach)
                {
                    var attachMark = new Label
                    {
                        AnchorPreset = AnchorPresets.TopRight,
                        Location = new Float2(-24f, 28f),
                        Size = new Float2(16f, 18f),
                        Text = "附",
                        TextColor = InkWashTheme.TextGold,
                        Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 12f),
                        HorizontalAlignment = TextAlignment.Center,
                        VerticalAlignment = TextAlignment.Center,
                    };
                    _mailAttachMarks[i] = attachMark;
                    item.AddChild(attachMark);
                }
            }

            // 注：InkListItem 未内置 Clicked 事件，邮件选中态保留构造时的默认值（第 0 项激活）。
            // 后续接入邮件系统时，可通过派生子类或外部 OnMouseDown 路由实现点击切换。

            AddChild(_mailListPanel);
        }

        /// <summary>
        /// 构建中栏邮件详情：发件人信息头 + 主题 + 信纸正文 + 赏赐明细。
        /// <para>
        /// 当前 mock 内容对应选中邮件"系统官府 - 周末庆典 · 论剑豪礼"。
        /// 信纸正文包含 5 段：少侠台鉴 / 正文 1~3 段 / 落款。
        /// 赏赐明细包含 4 件物品，与右栏附件列表数据一致。
        /// </para>
        /// </summary>
        private void BuildDetailPanel()
        {
            _detailPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
            };

            // ===== 发件人信息头 =====
            _detailAvatarLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(24f, 24f),
                Size = new Float2(56f, 56f),
                Text = "系",
                TextColor = InkWashTheme.TextGold,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 28f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };
            _detailPanel.AddChild(_detailAvatarLabel);

            _detailSenderLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(92f, 24f),
                Size = new Float2(200f, 28f),
                Text = "系统官府",
                TextColor = InkWashTheme.TextDefault,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 18f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _detailPanel.AddChild(_detailSenderLabel);

            _detailCategoryBadge = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(92f + 200f + 8f, 28f),
                Size = new Float2(56f, 22f),
                Text = "系统",
                TextColor = InkWashTheme.TextGold,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 12f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };
            _detailPanel.AddChild(_detailCategoryBadge);

            _detailTimeLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(92f, 56f),
                Size = new Float2(280f, 18f),
                Text = "丙午年 七月十五 辰时三刻",
                TextColor = InkWashTheme.TextSecondary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 12f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _detailPanel.AddChild(_detailTimeLabel);

            _detailAttachHintLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(92f + 280f + 8f, 56f),
                Size = new Float2(120f, 18f),
                Text = "附有赏赐",
                TextColor = InkWashTheme.TextGold,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 12f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _detailPanel.AddChild(_detailAttachHintLabel);

            // ===== 主题区 =====
            _detailSubjectLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(24f, 100f),
                Size = new Float2(600f, 40f),
                Text = "周末庆典 · 论剑豪礼",
                TextColor = InkWashTheme.TextDefault,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 26f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _detailPanel.AddChild(_detailSubjectLabel);

            _detailTagLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(24f, 148f),
                Size = new Float2(160f, 22f),
                Text = "庆典 · 论剑",
                TextColor = InkWashTheme.TextGold,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 12f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _detailPanel.AddChild(_detailTagLabel);

            // ===== 信纸正文（5 段）=====
            string[] letterLines =
            {
                "　　少侠台鉴：",
                "　　适逢周末庆典，江湖豪杰齐聚论剑台。阁下于本次论剑大会中表现卓绝，连挫数名强敌，名列前茅，特此致书恭贺。",
                "　　依江湖规矩，凡入榜者皆有赏赐。现将论剑豪礼附于信中，望少侠查收。灵石五百以充修炼之资，九转还魂丹三枚可续命于生死之间，玄铁精十两可铸神兵，真武剑诀残卷一卷乃武当秘传，习之可窥剑道真意。",
                "　　江湖路远，望少侠珍重。下月论剑，再期相会。",
                "—— 论剑台主办 · 系统官府",
            };
            _letterBodyLabels = new Label[letterLines.Length];
            float letterY = 188f;
            float[] letterHeights = { 28f, 56f, 84f, 28f, 28f };
            for (int i = 0; i < letterLines.Length; i++)
            {
                bool isSignature = (i == letterLines.Length - 1);
                _letterBodyLabels[i] = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(40f, letterY),
                    Size = new Float2(700f, letterHeights[i]),
                    Text = letterLines[i],
                    TextColor = isSignature ? InkWashTheme.TextSecondary : InkWashTheme.TextDefault,
                    Font = new FontReference(
                        InkWashTheme.GetFont(isSignature ? InkWashTheme.FontRole.Display : InkWashTheme.FontRole.Body),
                        13f),
                    HorizontalAlignment = isSignature ? TextAlignment.Far : TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Near,
                };
                _detailPanel.AddChild(_letterBodyLabels[i]);
                letterY += letterHeights[i] + 4f;
            }

            // ===== 赏赐明细区 =====
            _rewardTitleLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(24f, letterY + 8f),
                Size = new Float2(300f, 24f),
                Text = "◆ 赏赐明细（4 件）",
                TextColor = InkWashTheme.TextBrand,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), 14f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _detailPanel.AddChild(_rewardTitleLabel);

            // 4 件赏赐物品（与右栏附件数据一致）
            string[][] rewards =
            {
                new[] { "灵石",            "×500", "common"    },
                new[] { "玄铁精",          "×10",  "rare"      },
                new[] { "九转还魂丹",      "×3",   "epic"      },
                new[] { "真武剑诀 · 残卷", "×1",   "legendary" },
            };
            _rewardItemPanels = new InkPanel[rewards.Length];
            _rewardItemNameLabels = new Label[rewards.Length];
            _rewardItemQtyLabels = new Label[rewards.Length];

            float rewardY = letterY + 40f;
            float rewardItemW = 340f;
            for (int i = 0; i < rewards.Length; i++)
            {
                int row = i / 2;
                int col = i % 2;
                float x = 24f + col * (rewardItemW + 12f);
                float y = rewardY + row * (48f + 8f);

                var panel = new InkPanel
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(x, y),
                    Size = new Float2(rewardItemW, 48f),
                };
                _rewardItemPanels[i] = panel;
                _detailPanel.AddChild(panel);

                Color nameColor = rewards[i][2] switch
                {
                    "common"    => InkWashTheme.TextDefault,
                    "rare"      => InkWashTheme.TextBrand,
                    "epic"      => InkWashTheme.TextGold,
                    "legendary" => InkWashTheme.TextGold,
                    _           => InkWashTheme.TextDefault,
                };

                _rewardItemNameLabels[i] = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(16f, 0f),
                    Size = new Float2(220f, 48f),
                    Text = rewards[i][0],
                    TextColor = nameColor,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 13f),
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                };
                panel.AddChild(_rewardItemNameLabels[i]);

                _rewardItemQtyLabels[i] = new Label
                {
                    AnchorPreset = AnchorPresets.TopRight,
                    Location = new Float2(-100f, 0f),
                    Size = new Float2(84f, 48f),
                    Text = rewards[i][1],
                    TextColor = nameColor,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 14f),
                    HorizontalAlignment = TextAlignment.Far,
                    VerticalAlignment = TextAlignment.Center,
                };
                panel.AddChild(_rewardItemQtyLabels[i]);
            }

            AddChild(_detailPanel);
        }

        /// <summary>
        /// 构建右栏附件面板：标题 + 4 个附件条目 + 一键领取按钮 + 操作按钮（回复/转发/删除）。
        /// <para>
        /// 4 件 mock 附件：
        /// <list type="bullet">
        ///   <item>灵石 ×500（common，可领取）</item>
        ///   <item>玄铁精 ×10（rare，可领取）</item>
        ///   <item>九转还魂丹 ×3（epic，可领取）</item>
        ///   <item>真武剑诀 · 残卷 ×1（legendary，已领取）</item>
        /// </list>
        /// </para>
        /// </summary>
        private void BuildAttachmentPanel()
        {
            _attachmentPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
            };

            // ===== 标题区 =====
            _attachmentTitleLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(16f, 12f),
                Size = new Float2(180f, 24f),
                Text = "◆ 信物附件",
                TextColor = InkWashTheme.TextBrand,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), 14f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _attachmentPanel.AddChild(_attachmentTitleLabel);

            _attachmentCountLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopRight,
                Location = new Float2(-72f, 12f),
                Size = new Float2(56f, 24f),
                Text = "4 件",
                TextColor = InkWashTheme.TextGold,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 12f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };
            _attachmentPanel.AddChild(_attachmentCountLabel);

            // ===== 4 个附件条目 =====
            // 字段顺序：图标首字 / 名称 / 副标题 / 数量 / 品质 / 是否已领取
            string[][] attachments =
            {
                new[] { "石", "灵石",            "修炼资材", "×500", "common",    "0" },
                new[] { "铁", "玄铁精",          "铸兵材料", "×10",  "rare",      "0" },
                new[] { "丹", "九转还魂丹",      "续命灵药", "×3",   "epic",      "0" },
                new[] { "卷", "真武剑诀 · 残卷", "武当秘传", "×1",   "legendary", "1" },
            };

            _attachItemPanels = new InkPanel[attachments.Length];
            _attachIconLabels = new Label[attachments.Length];
            _attachNameLabels = new Label[attachments.Length];
            _attachSubtitleLabels = new Label[attachments.Length];
            _attachQtyLabels = new Label[attachments.Length];
            _attachClaimLabels = new Label[attachments.Length];

            float attachStartY = 48f;
            for (int i = 0; i < attachments.Length; i++)
            {
                float y = attachStartY + i * (AttachItemHeight + AttachItemGap);
                bool isClaimed = attachments[i][5] == "1";

                var panel = new InkPanel
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(12f, y),
                    Size = new Float2(280f, AttachItemHeight),
                };
                _attachItemPanels[i] = panel;
                _attachmentPanel.AddChild(panel);

                Color iconColor = attachments[i][4] switch
                {
                    "common"    => InkWashTheme.TextSecondary,
                    "rare"      => InkWashTheme.TextBrand,
                    "epic"      => InkWashTheme.TextGold,
                    "legendary" => InkWashTheme.TextGold,
                    _           => InkWashTheme.TextDefault,
                };

                // 图标首字
                _attachIconLabels[i] = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(12f, (AttachItemHeight - 36f) * 0.5f),
                    Size = new Float2(36f, 36f),
                    Text = attachments[i][0],
                    TextColor = iconColor,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 18f),
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                };
                panel.AddChild(_attachIconLabels[i]);

                // 名称
                _attachNameLabels[i] = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(56f, 8f),
                    Size = new Float2(160f, 22f),
                    Text = attachments[i][1],
                    TextColor = iconColor,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 13f),
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                };
                panel.AddChild(_attachNameLabels[i]);

                // 副标题（领取状态）
                string subtitle = isClaimed ? "已领取" : attachments[i][2];
                Color subtitleColor = isClaimed ? InkWashTheme.TextJade : InkWashTheme.TextSecondary;
                _attachSubtitleLabels[i] = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(56f, 28f),
                    Size = new Float2(160f, 18f),
                    Text = subtitle,
                    TextColor = subtitleColor,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 11f),
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                };
                panel.AddChild(_attachSubtitleLabels[i]);

                // 已领取徽章（独立标签，便于样式区分）
                if (isClaimed)
                {
                    _attachClaimLabels[i] = new Label
                    {
                        AnchorPreset = AnchorPresets.TopLeft,
                        Location = new Float2(56f + 160f + 4f, 28f),
                        Size = new Float2(16f, 18f),
                        Text = "✓",
                        TextColor = InkWashTheme.TextJade,
                        Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 12f),
                        HorizontalAlignment = TextAlignment.Center,
                        VerticalAlignment = TextAlignment.Center,
                    };
                    panel.AddChild(_attachClaimLabels[i]);
                }

                // 数量
                Color qtyColor = isClaimed ? InkWashTheme.TextSecondary : InkWashTheme.TextDefault;
                _attachQtyLabels[i] = new Label
                {
                    AnchorPreset = AnchorPresets.TopRight,
                    Location = new Float2(-72f, (AttachItemHeight - 22f) * 0.5f),
                    Size = new Float2(60f, 22f),
                    Text = attachments[i][3],
                    TextColor = qtyColor,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 14f),
                    HorizontalAlignment = TextAlignment.Far,
                    VerticalAlignment = TextAlignment.Center,
                };
                panel.AddChild(_attachQtyLabels[i]);
            }

            // ===== 一键领取按钮 =====
            float claimBtnY = attachStartY + attachments.Length * (AttachItemHeight + AttachItemGap) + 8f;
            _claimAllButton = new InkButton
            {
                Variant = InkButtonVariant.Default,
                ButtonSize = InkButtonSize.Md,
                Text = "一键领取",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, claimBtnY),
                Size = new Float2(280f, 36f),
            };
            _claimAllButton.ButtonClicked += (b) => EmitGoldAtButton(b);
            _attachmentPanel.AddChild(_claimAllButton);

            _claimHintLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, claimBtnY + 40f),
                Size = new Float2(280f, 18f),
                Text = "剩余可领取  3 件",
                TextColor = InkWashTheme.TextSecondary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 11f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };
            _attachmentPanel.AddChild(_claimHintLabel);

            // ===== 邮件操作按钮（回复/转发/删除，三栏均分）=====
            float actionY = claimBtnY + 68f;
            float actionBtnW = (280f - 2 * 8f) / 3f;
            _replyButton = new InkButton
            {
                Variant = InkButtonVariant.Default,
                ButtonSize = InkButtonSize.Sm,
                Text = "回复",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, actionY),
                Size = new Float2(actionBtnW, 32f),
            };
            _replyButton.ButtonClicked += (b) => EmitGoldAtButton(b);
            _attachmentPanel.AddChild(_replyButton);

            _forwardButton = new InkButton
            {
                Variant = InkButtonVariant.Ghost,
                ButtonSize = InkButtonSize.Sm,
                Text = "转发",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f + actionBtnW + 8f, actionY),
                Size = new Float2(actionBtnW, 32f),
            };
            _forwardButton.ButtonClicked += (b) => EmitGoldAtButton(b);
            _attachmentPanel.AddChild(_forwardButton);

            _deleteButton = new InkButton
            {
                Variant = InkButtonVariant.Vermilion,
                ButtonSize = InkButtonSize.Sm,
                Text = "删除",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f + (actionBtnW + 8f) * 2f, actionY),
                Size = new Float2(actionBtnW, 32f),
            };
            _deleteButton.ButtonClicked += (b) => EmitGoldAtButton(b);
            _attachmentPanel.AddChild(_deleteButton);

            AddChild(_attachmentPanel);
        }

        /// <summary>
        /// 构建底部导航按钮栏：返回沉浸模式 / 好友列表。
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

            _gotoFriendsButton = new InkButton
            {
                Variant = InkButtonVariant.Default,
                ButtonSize = InkButtonSize.Md,
                Text = "好友列表",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(NavBtnWidth + NavBtnGap, 0f),
                Size = new Float2(NavBtnWidth, BottomNavHeight),
            };
            _gotoFriendsButton.ButtonClicked += (b) =>
                OnSystemNavButtonClicked(InkPageDomIds.NavFriends, b);
            _bottomNavPanel.AddChild(_gotoFriendsButton);

            AddChild(_bottomNavPanel);
        }

        // ===================================================================
        // 事件处理
        // =======================================================================

        /// <summary>
        /// 顶部分类 Tab 按钮点击处理：切换激活态并发射金粉粒子。
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
                FlaxEngine.Debug.LogError($"[SocialMailPage] Tab 切换失败: {ex.Message}");
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
                    $"[SocialMailPage] NavigationRequested({domId}) 触发失败: {ex.Message}");
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
                FlaxEngine.Debug.LogWarning($"[SocialMailPage] EmitGoldAtButton 失败: {ex.Message}");
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

                    // 写信 / 关闭按钮靠右排列
                    float rightX = panelW - 8f;
                    if (_closeButton != null)
                    {
                        _closeButton.Location = new Float2(rightX - 32f, (HeaderHeight - 32f) * 0.5f);
                        rightX -= 32f + 8f;
                    }
                    if (_composeButton != null)
                    {
                        _composeButton.Location = new Float2(rightX - 96f, (HeaderHeight - 32f) * 0.5f);
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
                if (centerW < 100f)
                    centerW = 100f;

                // 4. 左栏邮件列表
                if (_mailListPanel != null)
                {
                    _mailListPanel.Location = new Float2(panelX, contentTop);
                    _mailListPanel.Size = new Float2(leftW, contentH);

                    // 检索框宽度自适应
                    float innerW = leftW - 24f;
                    if (_searchBoxPanel != null)
                        _searchBoxPanel.Size = new Float2(innerW, 36f);
                    if (_searchPlaceholderLabel != null)
                        _searchPlaceholderLabel.Size = new Float2(Mathf.Max(120f, innerW - 24f), 36f);

                    // 4 个分类 Tab 按宽度均分
                    if (_tabButtons != null && _tabButtons.Length > 0)
                    {
                        float tabW = (innerW - (_tabButtons.Length - 1) * TabBtnGap) / _tabButtons.Length;
                        float tabY = 56f;
                        for (int i = 0; i < _tabButtons.Length; i++)
                        {
                            if (_tabButtons[i] == null)
                                continue;
                            _tabButtons[i].Location = new Float2(12f + i * (tabW + TabBtnGap), tabY);
                            _tabButtons[i].Size = new Float2(tabW, TabBtnHeight);
                        }
                    }

                    // 3 个分组标题：宽度自适应
                    if (_groupLabels != null)
                    {
                        for (int i = 0; i < _groupLabels.Length; i++)
                        {
                            if (_groupLabels[i] != null)
                                _groupLabels[i].Size = new Float2(innerW, GroupLabelHeight);
                        }
                    }

                    // 8 封邮件条目：宽度自适应
                    if (_mailItems != null)
                    {
                        float itemW = innerW;
                        for (int i = 0; i < _mailItems.Length; i++)
                        {
                            if (_mailItems[i] == null)
                                continue;
                            _mailItems[i].Size = new Float2(itemW, MailItemHeight);

                            // 头像与文字位置随宽度调整
                            float senderW = Mathf.Max(80f, itemW - 144f);
                            if (_mailSenderLabels != null && _mailSenderLabels[i] != null)
                                _mailSenderLabels[i].Size = new Float2(senderW, 18f);
                            if (_mailSubjectLabels != null && _mailSubjectLabels[i] != null)
                                _mailSubjectLabels[i].Size = new Float2(Mathf.Max(80f, itemW - 124f), 18f);
                        }
                    }
                }

                // 5. 中栏邮件详情
                if (_detailPanel != null)
                {
                    float detailX = panelX + leftW + RegionGap;
                    _detailPanel.Location = new Float2(detailX, contentTop);
                    _detailPanel.Size = new Float2(centerW, contentH);

                    float innerW = centerW - 48f;

                    // 主题宽度自适应
                    if (_detailSubjectLabel != null)
                        _detailSubjectLabel.Size = new Float2(Mathf.Max(200f, innerW), 40f);

                    // 信纸正文段落宽度自适应
                    if (_letterBodyLabels != null)
                    {
                        for (int i = 0; i < _letterBodyLabels.Length; i++)
                        {
                            if (_letterBodyLabels[i] != null)
                                _letterBodyLabels[i].Size = new Float2(Mathf.Max(200f, innerW - 16f), _letterBodyLabels[i].Height);
                        }
                    }

                    // 赏赐明细条目按列宽自适应（2 列均分）
                    if (_rewardItemPanels != null && _rewardItemPanels.Length > 0)
                    {
                        float rewardItemW = (innerW - 12f) / 2f;
                        for (int i = 0; i < _rewardItemPanels.Length; i++)
                        {
                            if (_rewardItemPanels[i] == null)
                                continue;
                            int col = i % 2;
                            float x = 24f + col * (rewardItemW + 12f);
                            _rewardItemPanels[i].Location = new Float2(x, _rewardItemPanels[i].Y);
                            _rewardItemPanels[i].Size = new Float2(rewardItemW, 48f);

                            if (_rewardItemNameLabels != null && _rewardItemNameLabels[i] != null)
                                _rewardItemNameLabels[i].Size = new Float2(Mathf.Max(80f, rewardItemW - 100f), 48f);
                            if (_rewardItemQtyLabels != null && _rewardItemQtyLabels[i] != null)
                                _rewardItemQtyLabels[i].Size = new Float2(84f, 48f);
                        }
                    }

                    // 发件人信息头宽度自适应
                    if (_detailSenderLabel != null)
                        _detailSenderLabel.Size = new Float2(Mathf.Max(120f, innerW * 0.5f - 60f), 28f);
                    if (_detailTimeLabel != null)
                        _detailTimeLabel.Size = new Float2(Mathf.Max(160f, innerW * 0.5f), 18f);
                }

                // 6. 右栏附件面板
                if (_attachmentPanel != null)
                {
                    float attachX = panelX + leftW + RegionGap + centerW + RegionGap;
                    _attachmentPanel.Location = new Float2(attachX, contentTop);
                    _attachmentPanel.Size = new Float2(rightW, contentH);

                    float innerW = rightW - 24f;

                    // 标题区宽度自适应
                    if (_attachmentTitleLabel != null)
                        _attachmentTitleLabel.Size = new Float2(Mathf.Max(120f, innerW - 60f), 24f);

                    // 4 个附件条目宽度自适应
                    if (_attachItemPanels != null)
                    {
                        for (int i = 0; i < _attachItemPanels.Length; i++)
                        {
                            if (_attachItemPanels[i] == null)
                                continue;
                            _attachItemPanels[i].Size = new Float2(innerW, AttachItemHeight);

                            if (_attachNameLabels != null && _attachNameLabels[i] != null)
                                _attachNameLabels[i].Size = new Float2(Mathf.Max(80f, innerW - 120f), 22f);
                            if (_attachSubtitleLabels != null && _attachSubtitleLabels[i] != null)
                                _attachSubtitleLabels[i].Size = new Float2(Mathf.Max(80f, innerW - 120f), 18f);
                        }
                    }

                    // 一键领取按钮宽度自适应
                    if (_claimAllButton != null)
                        _claimAllButton.Size = new Float2(innerW, 36f);
                    if (_claimHintLabel != null)
                        _claimHintLabel.Size = new Float2(innerW, 18f);

                    // 操作按钮（回复/转发/删除）三栏均分
                    if (_replyButton != null && _forwardButton != null && _deleteButton != null)
                    {
                        float actionBtnW = (innerW - 2 * 8f) / 3f;
                        _replyButton.Location = new Float2(12f, _replyButton.Y);
                        _replyButton.Size = new Float2(actionBtnW, 32f);
                        _forwardButton.Location = new Float2(12f + actionBtnW + 8f, _forwardButton.Y);
                        _forwardButton.Size = new Float2(actionBtnW, 32f);
                        _deleteButton.Location = new Float2(12f + (actionBtnW + 8f) * 2f, _deleteButton.Y);
                        _deleteButton.Size = new Float2(actionBtnW, 32f);
                    }
                }
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[SocialMailPage] RefreshLayout 失败: {ex.Message}");
            }
        }

        // ===================================================================
        // 生命周期
        // =======================================================================

        /// <inheritdoc />
        public override void OnParentResized()
        {
            base.OnParentResized();
            RefreshLayout();
        }
    }
}
