using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.Ink;
using HundunWorld.Game.UI.StyleSystem;
using System;

namespace HundunWorld.Game.UI.Ink.Pages.Social
{
    /// <summary>
    /// 江湖百艺录（成就）页面 — 对应 achievement.html 设计原型。
    /// <para>
    /// 三栏式布局：
    /// <list type="bullet">
    ///   <item>顶部：返回按钮 + 标题 + 总体完成度进度条 + 成就点数</item>
    ///   <item>左侧：6 个成就分类竖向列表（江湖历练/武学修为/装备收集/生活技艺/门派贡献/特殊成就）</item>
    ///   <item>中央：成就卡片网格（每张卡片显示图标/品质/名称/描述/进度条/状态徽章）</item>
    ///   <item>右侧：选中成就详情面板（大图标/描述/完成条件/奖励列表/领取按钮）</item>
    ///   <item>底部：返回沉浸模式按钮，触发 <see cref="NavigationRequested"/> 回到 combat-hud</item>
    /// </list>
    /// 所有数据为 mock，通过 <see cref="RefreshLayout"/> 适配父容器尺寸变化。
    /// </para>
    /// </summary>
    public class AchievementPage : ContainerControl, IInkPage
    {
        // ===================================================================
        // 布局常量
        // =======================================================================

        /// <summary>屏幕外缘留白</summary>
        private const float ScreenEdge = 16f;

        /// <summary>顶部标题栏高度</summary>
        private const float HeaderHeight = 64f;

        /// <summary>底部返回栏高度</summary>
        private const float FooterHeight = 44f;

        /// <summary>栏间距</summary>
        private const float PanelGap = 8f;

        /// <summary>左侧分类栏宽度</summary>
        private const float CategoryWidth = 220f;

        /// <summary>右侧详情栏宽度</summary>
        private const float DetailWidth = 300f;

        /// <summary>每条分类项高度</summary>
        private const float CategoryItemHeight = 56f;

        /// <summary>成就卡片宽度</summary>
        private const float CardWidth = 168f;

        /// <summary>成就卡片高度</summary>
        private const float CardHeight = 156f;

        /// <summary>卡片间距</summary>
        private const float CardGap = 8f;

        /// <summary>顶部栏内边距</summary>
        private const float HeaderPadding = 12f;

        // ===================================================================
        // 子控件引用
        // =======================================================================

        /// <summary>顶部标题栏</summary>
        private InkPanel _headerPanel;

        /// <summary>返回按钮</summary>
        private InkBackButton _backButton;

        /// <summary>主标题</summary>
        private Label _titleLabel;

        /// <summary>副标题</summary>
        private Label _subtitleLabel;

        /// <summary>总体完成度标签</summary>
        private Label _overallLabel;

        /// <summary>总体完成度数值（已达成/总数）</summary>
        private Label _overallCountLabel;

        /// <summary>总体完成百分比</summary>
        private Label _overallPctLabel;

        /// <summary>总体进度条</summary>
        private InkBar _overallBar;

        /// <summary>成就点数标签</summary>
        private Label _pointsLabel;

        /// <summary>成就点数值</summary>
        private Label _pointsValueLabel;

        /// <summary>左侧分类面板</summary>
        private InkPanel _categoryPanel;

        /// <summary>左侧分类标题</summary>
        private Label _categoryTitleLabel;

        /// <summary>6 条分类项容器（每项含名称 + 完成度 + 进度条）</summary>
        private CategoryItem[] _categoryItems;

        /// <summary>中央成就列表面板</summary>
        private InkPanel _listPanel;

        /// <summary>列表标题（面包屑）</summary>
        private Label _listTitleLabel;

        /// <summary>9 张成就卡片</summary>
        private AchievementCard[] _cards;

        /// <summary>右侧详情面板</summary>
        private InkPanel _detailPanel;

        /// <summary>详情大图标</summary>
        private InkCell _detailIcon;

        /// <summary>详情品质标签</summary>
        private Label _detailTierLabel;

        /// <summary>详情名称</summary>
        private Label _detailNameLabel;

        /// <summary>详情点数标签</summary>
        private Label _detailPointsLabel;

        /// <summary>详情状态标签</summary>
        private Label _detailStatusLabel;

        /// <summary>详情描述</summary>
        private Label _detailDescLabel;

        /// <summary>详情完成条件标题</summary>
        private Label _detailCondTitleLabel;

        /// <summary>详情完成条件数值（7/10）</summary>
        private Label _detailCondCountLabel;

        /// <summary>5 条完成条件项</summary>
        private Label[] _detailCondItems;

        /// <summary>详情进度条</summary>
        private InkBar _detailProgressBar;

        /// <summary>详情奖励标题</summary>
        private Label _detailRewardTitleLabel;

        /// <summary>4 条奖励项</summary>
        private Label[] _detailRewardItems;

        /// <summary>领取奖励按钮</summary>
        private InkButton _claimButton;

        /// <summary>底部返回沉浸模式按钮</summary>
        private InkButton _footerBackButton;

        // ===================================================================
        // 公共 API
        // =======================================================================

        /// <summary>
        /// 导航请求事件。返回按钮触发时携带 <see cref="InkPageDomIds.CombatHud"/>，
        /// 领取奖励按钮触发时携带 <see cref="InkPageDomIds.NavAchievement"/>。
        /// </summary>
        public event Action<string> NavigationRequested;

        /// <summary>
        /// 粒子动效系统引用（可选，由 MainUIManager 注入）。
        /// </summary>
        public InkParticleSystem ParticleSystem { get; set; }

        // ===================================================================
        // 构造函数
        // =======================================================================

        /// <summary>
        /// 构造函数：初始化所有子控件。
        /// </summary>
        public AchievementPage()
        {
            try
            {
                AnchorPreset = AnchorPresets.StretchAll;
                Offsets = Margin.Zero;
                BackgroundColor = Color.Transparent;
                ClipChildren = false;
                AutoFocus = false;

                BuildHeader();
                BuildCategoryPanel();
                BuildListPanel();
                BuildDetailPanel();
                BuildFooter();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[AchievementPage] 初始化失败: {ex.Message}");
            }
        }

        // ===================================================================
        // Build 方法
        // =======================================================================

        /// <summary>
        /// 构建顶部标题栏：返回按钮 + 标题 + 总体进度 + 点数。
        /// </summary>
        private void BuildHeader()
        {
            _headerPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(800f, HeaderHeight),
            };

            // 返回按钮
            _backButton = new InkBackButton
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(HeaderPadding, (HeaderHeight - 40f) * 0.5f),
                Size = new Float2(40f, 40f),
            };
            _backButton.Clicked += OnBackButtonClicked;
            _headerPanel.AddChild(_backButton);

            // 主标题
            _titleLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(HeaderPadding + 48f, 8f),
                Size = new Float2(200f, 24f),
                Text = "江湖百艺录",
                TextColor = InkWashTheme.TextBrand,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 20f),
                HorizontalAlignment = TextAlignment.Near,
            };
            _headerPanel.AddChild(_titleLabel);

            // 副标题
            _subtitleLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(HeaderPadding + 48f, 34f),
                Size = new Float2(200f, 16f),
                Text = "百艺通鉴 · 武学考工",
                TextColor = InkWashTheme.TextTertiary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 11f),
                HorizontalAlignment = TextAlignment.Near,
            };
            _headerPanel.AddChild(_subtitleLabel);

            // 总体完成度标签
            _overallLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(280f, 10f),
                Size = new Float2(80f, 16f),
                Text = "总体完成度",
                TextColor = InkWashTheme.TextSecondary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 12f),
                HorizontalAlignment = TextAlignment.Near,
            };
            _headerPanel.AddChild(_overallLabel);

            // 总体数值（已达成/总数）
            _overallCountLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(360f, 8f),
                Size = new Float2(110f, 18f),
                Text = "238 / 520",
                TextColor = InkWashTheme.TextDefault,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 14f),
                HorizontalAlignment = TextAlignment.Near,
            };
            _headerPanel.AddChild(_overallCountLabel);

            // 总体百分比
            _overallPctLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(478f, 8f),
                Size = new Float2(48f, 18f),
                Text = "45%",
                TextColor = InkWashTheme.TextGold,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 14f),
                HorizontalAlignment = TextAlignment.Near,
            };
            _headerPanel.AddChild(_overallPctLabel);

            // 总体进度条
            _overallBar = new InkBar
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(280f, 36f),
                Size = new Float2(246f, 10f),
                Value = 0.45f,
                FillVariant = InkBarFillVariant.Gold,
            };
            _headerPanel.AddChild(_overallBar);

            // 成就点数标签
            _pointsLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(560f, 10f),
                Size = new Float2(60f, 16f),
                Text = "成就点",
                TextColor = InkWashTheme.TextSecondary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 12f),
                HorizontalAlignment = TextAlignment.Near,
            };
            _headerPanel.AddChild(_pointsLabel);

            // 成就点数值
            _pointsValueLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(620f, 8f),
                Size = new Float2(80f, 18f),
                Text = "2,380",
                TextColor = InkWashTheme.TextGold,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 16f),
                HorizontalAlignment = TextAlignment.Near,
            };
            _headerPanel.AddChild(_pointsValueLabel);

            // 筛选按钮
            var filterButton = new InkButton
            {
                Variant = InkButtonVariant.Default,
                ButtonSize = InkButtonSize.Sm,
                Text = "筛选",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(720f, 16f),
                Size = new Float2(56f, 32f),
            };
            filterButton.ButtonClicked += (b) => EmitGoldAtButton(b);
            _headerPanel.AddChild(filterButton);

            AddChild(_headerPanel);
        }

        /// <summary>
        /// 构建左侧分类面板（6 个分类项）。
        /// </summary>
        private void BuildCategoryPanel()
        {
            _categoryPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(CategoryWidth, 600f),
            };

            // 分类标题
            _categoryTitleLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, 8f),
                Size = new Float2(CategoryWidth - 24f, 20f),
                Text = "◆ 成就分类",
                TextColor = InkWashTheme.TextGold,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), 14f),
                HorizontalAlignment = TextAlignment.Near,
            };
            _categoryPanel.AddChild(_categoryTitleLabel);

            // 6 条分类项
            _categoryItems = new CategoryItem[6];
            string[] catNames =
            {
                "江湖历练", "武学修为", "装备收集",
                "生活技艺", "门派贡献", "特殊成就",
            };
            string[] catCounts =
            {
                "45/120", "32/85", "28/90",
                "56/110", "41/75", "36/40",
            };
            float[] catProgress = { 0.38f, 0.38f, 0.31f, 0.51f, 0.55f, 0.90f };
            InkBarFillVariant[] catVariants =
            {
                InkBarFillVariant.Blood,
                InkBarFillVariant.Jade,
                InkBarFillVariant.Jade,
                InkBarFillVariant.Gold,
                InkBarFillVariant.Gold,
                InkBarFillVariant.Vermilion,
            };

            for (int i = 0; i < 6; i++)
            {
                var item = new CategoryItem
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(8f, 36f + i * CategoryItemHeight),
                    Size = new Float2(CategoryWidth - 16f, CategoryItemHeight - 4f),
                    Name = catNames[i],
                    Count = catCounts[i],
                    Progress = catProgress[i],
                    FillVariant = catVariants[i],
                };
                _categoryItems[i] = item;
                _categoryPanel.AddChild(item);
            }

            AddChild(_categoryPanel);
        }

        /// <summary>
        /// 构建中央成就卡片网格（3x3 共 9 张卡片）。
        /// </summary>
        private void BuildListPanel()
        {
            _listPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(600f, 600f),
            };

            // 面包屑标题
            _listTitleLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, 8f),
                Size = new Float2(560f, 20f),
                Text = "江湖历练 ▸ 战斗   12 / 24",
                TextColor = InkWashTheme.TextSecondary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), 13f),
                HorizontalAlignment = TextAlignment.Near,
            };
            _listPanel.AddChild(_listTitleLabel);

            // 9 张成就卡片
            _cards = new AchievementCard[9];
            string[] cardNames =
            {
                "初出茅庐", "百战之将", "武林盟主",
                "嗜血修罗", "剑气长江", "一击必杀",
                "不动如山", "连斩之鬼", "团战先锋",
            };
            string[] cardDescs =
            {
                "完成你的第一场战斗",
                "累计胜利一百场战斗",
                "击败十大门派掌门，问鼎武林",
                "单场战斗击杀五十名敌人",
                "??????????",
                "造成十万点单次伤害",
                "累计格挡一千次攻击",
                "??????????",
                "参与五十场帮派战役",
            };
            InkWashTheme.InkQuality[] cardQualities =
            {
                InkWashTheme.InkQuality.Common,
                InkWashTheme.InkQuality.Epic,
                InkWashTheme.InkQuality.Legendary,
                InkWashTheme.InkQuality.Rare,
                InkWashTheme.InkQuality.Common,
                InkWashTheme.InkQuality.Rare,
                InkWashTheme.InkQuality.Uncommon,
                InkWashTheme.InkQuality.Common,
                InkWashTheme.InkQuality.Uncommon,
            };
            // 0=已解锁 1=进行中 2=未解锁
            int[] cardStates = { 0, 0, 1, 0, 2, 1, 0, 2, 0 };
            float[] cardProgress = { 1f, 1f, 0.70f, 1f, 0f, 0.73f, 1f, 0f, 1f };
            string[] cardProgressText =
            {
                "2025.03.12", "2025.05.08", "7 / 10",
                "2025.06.21", "剑法等级达到 80 级", "73%",
                "2025.04.15", "单次连击达到 999", "2025.07.02",
            };

            for (int i = 0; i < 9; i++)
            {
                int col = i % 3;
                int row = i / 3;
                var card = new AchievementCard
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(12f + col * (CardWidth + CardGap), 36f + row * (CardHeight + CardGap)),
                    Size = new Float2(CardWidth, CardHeight),
                    Name = cardNames[i],
                    Description = cardDescs[i],
                    Quality = cardQualities[i],
                    State = cardStates[i],
                    Progress = cardProgress[i],
                    ProgressText = cardProgressText[i],
                };
                _cards[i] = card;
                _listPanel.AddChild(card);
            }

            AddChild(_listPanel);
        }

        /// <summary>
        /// 构建右侧详情面板（大图标 + 名称 + 描述 + 完成条件 + 奖励 + 领取按钮）。
        /// </summary>
        private void BuildDetailPanel()
        {
            _detailPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(DetailWidth, 600f),
            };

            // 大图标
            _detailIcon = new InkCell
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(16f, 16f),
                Size = new Float2(64f, 64f),
                Quality = InkWashTheme.InkQuality.Legendary,
            };
            _detailPanel.AddChild(_detailIcon);

            // 品质标签
            _detailTierLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(90f, 16f),
                Size = new Float2(190f, 16f),
                Text = "传说成就",
                TextColor = InkWashTheme.QualityTextColor(InkWashTheme.InkQuality.Legendary),
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 12f),
                HorizontalAlignment = TextAlignment.Near,
            };
            _detailPanel.AddChild(_detailTierLabel);

            // 名称
            _detailNameLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(90f, 34f),
                Size = new Float2(190f, 22f),
                Text = "武林盟主",
                TextColor = InkWashTheme.TextDefault,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 18f),
                HorizontalAlignment = TextAlignment.Near,
            };
            _detailPanel.AddChild(_detailNameLabel);

            // 点数标签
            _detailPointsLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(90f, 58f),
                Size = new Float2(190f, 16f),
                Text = "成就点 +120",
                TextColor = InkWashTheme.TextGold,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 12f),
                HorizontalAlignment = TextAlignment.Near,
            };
            _detailPanel.AddChild(_detailPointsLabel);

            // 状态标签
            _detailStatusLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(232f, 16f),
                Size = new Float2(60f, 20f),
                Text = "进行中",
                TextColor = InkWashTheme.TextJade,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 11f),
                HorizontalAlignment = TextAlignment.Far,
            };
            _detailPanel.AddChild(_detailStatusLabel);

            // 描述文本
            _detailDescLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(16f, 92f),
                Size = new Float2(DetailWidth - 32f, 56f),
                Text = "击败天下十大门派掌门，证明自身武学造诣已臻化境，可号令群雄，问鼎武林至尊之位。",
                TextColor = InkWashTheme.TextSecondary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 12f),
                HorizontalAlignment = TextAlignment.Near,
            };
            _detailPanel.AddChild(_detailDescLabel);

            // 完成条件标题
            _detailCondTitleLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(16f, 156f),
                Size = new Float2(160f, 18f),
                Text = "◆ 完成条件",
                TextColor = InkWashTheme.TextGold,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), 13f),
                HorizontalAlignment = TextAlignment.Near,
            };
            _detailPanel.AddChild(_detailCondTitleLabel);

            // 完成条件数值
            _detailCondCountLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(DetailWidth - 76f, 156f),
                Size = new Float2(60f, 18f),
                Text = "7 / 10",
                TextColor = InkWashTheme.TextBrand,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 12f),
                HorizontalAlignment = TextAlignment.Far,
            };
            _detailPanel.AddChild(_detailCondCountLabel);

            // 完成进度条
            _detailProgressBar = new InkBar
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(16f, 178f),
                Size = new Float2(DetailWidth - 32f, 8f),
                Value = 0.70f,
                FillVariant = InkBarFillVariant.Gold,
            };
            _detailPanel.AddChild(_detailProgressBar);

            // 5 条完成条件项
            _detailCondItems = new Label[5];
            string[] condTexts =
            {
                "✓ 击败少林方丈",
                "✓ 击败武当掌门",
                "✓ 击败峨眉掌门",
                "○ 击败丐帮帮主",
                "○ 击败昆仑掌门",
            };
            for (int i = 0; i < 5; i++)
            {
                var condLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(20f, 196f + i * 18f),
                    Size = new Float2(DetailWidth - 40f, 16f),
                    Text = condTexts[i],
                    TextColor = i < 3 ? InkWashTheme.TextJade : InkWashTheme.TextTertiary,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 11f),
                    HorizontalAlignment = TextAlignment.Near,
                };
                _detailCondItems[i] = condLabel;
                _detailPanel.AddChild(condLabel);
            }

            // 奖励标题
            _detailRewardTitleLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(16f, 296f),
                Size = new Float2(200f, 18f),
                Text = "◆ 奖励预览",
                TextColor = InkWashTheme.TextGold,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), 13f),
                HorizontalAlignment = TextAlignment.Near,
            };
            _detailPanel.AddChild(_detailRewardTitleLabel);

            // 4 条奖励项
            _detailRewardItems = new Label[4];
            string[] rewardTexts =
            {
                "经验   +50,000",
                "银两   +10,000",
                "称号   武林盟主",
                "道具   龙吟剑 · 传说",
            };
            for (int i = 0; i < 4; i++)
            {
                var rewardLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(20f, 320f + i * 20f),
                    Size = new Float2(DetailWidth - 40f, 18f),
                    Text = rewardTexts[i],
                    TextColor = InkWashTheme.TextDefault,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 12f),
                    HorizontalAlignment = TextAlignment.Near,
                };
                _detailRewardItems[i] = rewardLabel;
                _detailPanel.AddChild(rewardLabel);
            }

            // 领取奖励按钮
            _claimButton = new InkButton
            {
                Variant = InkButtonVariant.Primary,
                ButtonSize = InkButtonSize.Md,
                Text = "领取奖励",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(16f, 416f),
                Size = new Float2(DetailWidth - 32f, 36f),
            };
            _claimButton.ButtonClicked += (b) => OnClaimButtonClicked(b);
            _detailPanel.AddChild(_claimButton);

            AddChild(_detailPanel);
        }

        /// <summary>
        /// 构建底部返回沉浸模式按钮栏。
        /// </summary>
        private void BuildFooter()
        {
            _footerBackButton = new InkButton
            {
                Variant = InkButtonVariant.Ghost,
                ButtonSize = InkButtonSize.Md,
                Text = "返回沉浸模式",
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(180f, 36f),
            };
            _footerBackButton.ButtonClicked += (b) => OnBackButtonClicked();
            AddChild(_footerBackButton);
        }

        // ===================================================================
        // 事件处理
        // =======================================================================

        /// <summary>
        /// 返回按钮点击处理：触发金粉粒子 + 导航回 combat-hud。
        /// </summary>
        private void OnBackButtonClicked()
        {
            try
            {
                if (_backButton != null)
                    EmitGoldAtButton(_backButton);
                NavigationRequested?.Invoke(InkPageDomIds.CombatHud);
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[AchievementPage] OnBackButtonClicked 失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 领取奖励按钮点击处理：触发金粉粒子 + 通知外部刷新。
        /// </summary>
        private void OnClaimButtonClicked(Button sourceButton)
        {
            try
            {
                EmitGoldAtButton(sourceButton);
                NavigationRequested?.Invoke(InkPageDomIds.NavAchievement);
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[AchievementPage] OnClaimButtonClicked 失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 在按钮中心位置触发金粉爆发粒子反馈。
        /// </summary>
        private void EmitGoldAtButton(Control button)
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
                FlaxEngine.Debug.LogWarning($"[AchievementPage] EmitGoldAtButton 失败: {ex.Message}");
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
                float sw = Width;
                float sh = Height;

                // 顶部标题栏
                if (_headerPanel != null)
                {
                    _headerPanel.Location = new Float2(ScreenEdge, ScreenEdge);
                    _headerPanel.Size = new Float2(sw - 2f * ScreenEdge, HeaderHeight);
                }

                // 底部返回按钮（底部居中）
                if (_footerBackButton != null)
                {
                    _footerBackButton.Location = new Float2(
                        sw * 0.5f - 90f,
                        sh - ScreenEdge - 36f);
                }

                // 中间内容区域：顶部下方至底部按钮上方
                float contentTop = ScreenEdge + HeaderHeight + PanelGap;
                float contentBottom = sh - ScreenEdge - 36f - PanelGap;
                float contentHeight = contentBottom - contentTop;

                // 左侧分类面板
                if (_categoryPanel != null)
                {
                    _categoryPanel.Location = new Float2(ScreenEdge, contentTop);
                    _categoryPanel.Size = new Float2(CategoryWidth, contentHeight);
                }

                // 右侧详情面板
                if (_detailPanel != null)
                {
                    _detailPanel.Location = new Float2(sw - ScreenEdge - DetailWidth, contentTop);
                    _detailPanel.Size = new Float2(DetailWidth, contentHeight);
                }

                // 中央成就列表面板
                if (_listPanel != null)
                {
                    float listX = ScreenEdge + CategoryWidth + PanelGap;
                    float listWidth = sw - 2f * ScreenEdge - CategoryWidth - DetailWidth - 2f * PanelGap;
                    _listPanel.Location = new Float2(listX, contentTop);
                    _listPanel.Size = new Float2(listWidth, contentHeight);
                }
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[AchievementPage] RefreshLayout 失败: {ex.Message}");
            }
        }

        /// <inheritdoc />
        public override void OnParentResized()
        {
            base.OnParentResized();
            RefreshLayout();
        }

        // ===================================================================
        // 内部嵌套类型
        // =======================================================================

        /// <summary>
        /// 成就分类项：左侧栏一行（名称 + 完成度计数 + 进度条）。
        /// </summary>
        private class CategoryItem : ContainerControl
        {
            /// <summary>分类名称标签</summary>
            private Label _nameLabel;

            /// <summary>完成度计数标签</summary>
            private Label _countLabel;

            /// <summary>进度条</summary>
            private InkBar _progressBar;

            /// <summary>
            /// 设置或获取分类名称（同步更新标签）。
            /// </summary>
            public string Name
            {
                get => _nameLabel?.Text ?? string.Empty;
                set
                {
                    if (_nameLabel != null)
                        _nameLabel.Text = value;
                }
            }

            /// <summary>
            /// 设置或获取完成度计数文字。
            /// </summary>
            public string Count
            {
                get => _countLabel?.Text ?? string.Empty;
                set
                {
                    if (_countLabel != null)
                        _countLabel.Text = value;
                }
            }

            /// <summary>
            /// 设置或获取进度值（0.0~1.0）。
            /// </summary>
            public float Progress
            {
                get => _progressBar?.Value ?? 0f;
                set
                {
                    if (_progressBar != null)
                        _progressBar.Value = value;
                }
            }

            /// <summary>
            /// 设置或获取进度条填充变体。
            /// </summary>
            public InkBarFillVariant FillVariant
            {
                set
                {
                    if (_progressBar != null)
                        _progressBar.FillVariant = value;
                }
            }

            /// <summary>
            /// 构造函数：初始化子控件。
            /// </summary>
            public CategoryItem()
            {
                BackgroundColor = Color.Transparent;
                ClipChildren = false;

                _nameLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(8f, 4f),
                    Size = new Float2(140f, 18f),
                    TextColor = InkWashTheme.TextDefault,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), 13f),
                    HorizontalAlignment = TextAlignment.Near,
                };
                AddChild(_nameLabel);

                _countLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(150f, 4f),
                    Size = new Float2(54f, 18f),
                    TextColor = InkWashTheme.TextGold,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 12f),
                    HorizontalAlignment = TextAlignment.Far,
                };
                AddChild(_countLabel);

                _progressBar = new InkBar
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(8f, 28f),
                    Size = new Float2(196f, 6f),
                    Value = 0f,
                    FillVariant = InkBarFillVariant.Gold,
                };
                AddChild(_progressBar);
            }

            /// <inheritdoc />
            public override void Draw()
            {
                if (!Visible || Width <= 0f || Height <= 0f)
                    return;

                // 底部分割线
                Render2D.FillRectangle(
                    new Rectangle(0, Height - 1f, Width, 1f),
                    InkWashTheme.Divider);

                base.Draw();
            }
        }

        // =======================================================================

        /// <summary>
        /// 成就卡片：中央网格中一张成就展示卡。
        /// </summary>
        private class AchievementCard : ContainerControl
        {
            /// <summary>卡片图标</summary>
            private InkCell _iconCell;

            /// <summary>品质标签</summary>
            private Label _tierLabel;

            /// <summary>成就名称</summary>
            private Label _nameLabel;

            /// <summary>成就描述</summary>
            private Label _descLabel;

            /// <summary>底部状态/进度文本</summary>
            private Label _footLabel;

            /// <summary>进度条（进行中态显示）</summary>
            private InkBar _progressBar;

            /// <summary>当前品质</summary>
            private InkWashTheme.InkQuality _quality = InkWashTheme.InkQuality.Common;

            /// <summary>当前状态：0=已解锁 1=进行中 2=未解锁</summary>
            private int _state;

            /// <summary>
            /// 设置或获取成就名称。
            /// </summary>
            public string Name
            {
                get => _nameLabel?.Text ?? string.Empty;
                set
                {
                    if (_nameLabel != null)
                        _nameLabel.Text = value;
                }
            }

            /// <summary>
            /// 设置或获取成就描述。
            /// </summary>
            public string Description
            {
                get => _descLabel?.Text ?? string.Empty;
                set
                {
                    if (_descLabel != null)
                        _descLabel.Text = value;
                }
            }

            /// <summary>
            /// 设置或获取品质等级（更新图标边框与品质文字色）。
            /// </summary>
            public InkWashTheme.InkQuality Quality
            {
                get => _quality;
                set
                {
                    _quality = value;
                    if (_iconCell != null)
                        _iconCell.Quality = value;
                    if (_tierLabel != null)
                        _tierLabel.TextColor = InkWashTheme.QualityTextColor(value);
                }
            }

            /// <summary>
            /// 设置或获取状态：0=已解锁 1=进行中 2=未解锁。
            /// </summary>
            public int State
            {
                get => _state;
                set
                {
                    _state = value;
                    ApplyStateStyle();
                }
            }

            /// <summary>
            /// 设置或获取进度值（0.0~1.0）。
            /// </summary>
            public float Progress
            {
                set
                {
                    if (_progressBar != null)
                        _progressBar.Value = value;
                }
            }

            /// <summary>
            /// 设置或获取底部状态文本（日期/进度文字/解锁条件）。
            /// </summary>
            public string ProgressText
            {
                set
                {
                    if (_footLabel != null)
                        _footLabel.Text = value;
                }
            }

            /// <summary>
            /// 构造函数：初始化子控件。
            /// </summary>
            public AchievementCard()
            {
                BackgroundColor = new Color(
                    InkWashTheme.BaseSecondary.R,
                    InkWashTheme.BaseSecondary.G,
                    InkWashTheme.BaseSecondary.B,
                    0.6f);
                ClipChildren = false;

                _iconCell = new InkCell
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(8f, 8f),
                    Size = new Float2(40f, 40f),
                    Quality = InkWashTheme.InkQuality.Common,
                };
                AddChild(_iconCell);

                _tierLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(56f, 10f),
                    Size = new Float2(104f, 16f),
                    Text = "普通",
                    TextColor = InkWashTheme.QualityTextColor(InkWashTheme.InkQuality.Common),
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 11f),
                    HorizontalAlignment = TextAlignment.Far,
                };
                AddChild(_tierLabel);

                _nameLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(8f, 54f),
                    Size = new Float2(152f, 20f),
                    Text = string.Empty,
                    TextColor = InkWashTheme.TextDefault,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), 14f),
                    HorizontalAlignment = TextAlignment.Near,
                };
                AddChild(_nameLabel);

                _descLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(8f, 76f),
                    Size = new Float2(152f, 32f),
                    Text = string.Empty,
                    TextColor = InkWashTheme.TextSecondary,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 11f),
                    HorizontalAlignment = TextAlignment.Near,
                };
                AddChild(_descLabel);

                _progressBar = new InkBar
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(8f, 116f),
                    Size = new Float2(152f, 6f),
                    Value = 0f,
                    FillVariant = InkBarFillVariant.Jade,
                };
                AddChild(_progressBar);

                _footLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(8f, 126f),
                    Size = new Float2(152f, 16f),
                    Text = string.Empty,
                    TextColor = InkWashTheme.TextTertiary,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 11f),
                    HorizontalAlignment = TextAlignment.Near,
                };
                AddChild(_footLabel);
            }

            /// <summary>
            /// 根据当前状态应用样式（已解锁=翡翠徽章 / 进行中=显示进度条 / 未解锁=灰显）。
            /// </summary>
            private void ApplyStateStyle()
            {
                if (_progressBar == null || _footLabel == null || _nameLabel == null || _descLabel == null)
                    return;

                switch (_state)
                {
                    case 0: // 已解锁
                        _progressBar.Visible = false;
                        _progressBar.Value = 1f;
                        _footLabel.TextColor = InkWashTheme.TextJade;
                        _nameLabel.TextColor = InkWashTheme.TextDefault;
                        _descLabel.TextColor = InkWashTheme.TextSecondary;
                        break;
                    case 1: // 进行中
                        _progressBar.Visible = true;
                        _progressBar.FillVariant = InkBarFillVariant.Jade;
                        _footLabel.TextColor = InkWashTheme.TextBrand;
                        _nameLabel.TextColor = InkWashTheme.TextDefault;
                        _descLabel.TextColor = InkWashTheme.TextSecondary;
                        break;
                    default: // 未解锁
                        _progressBar.Visible = false;
                        _footLabel.TextColor = InkWashTheme.TextDisabled;
                        _nameLabel.TextColor = InkWashTheme.TextTertiary;
                        _descLabel.TextColor = InkWashTheme.TextTertiary;
                        break;
                }
            }

            /// <inheritdoc />
            public override void Draw()
            {
                if (!Visible || Width <= 0f || Height <= 0f)
                    return;

                var bounds = new Rectangle(0, 0, Width, Height);

                // 卡片背景
                Render2D.FillRectangle(bounds, BackgroundColor);

                // 品质色边框
                Render2D.DrawRectangle(bounds, InkWashTheme.QualityColor(_quality), 1f);

                // 绘制子控件
                base.Draw();
            }
        }
    }
}
