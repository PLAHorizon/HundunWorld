using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.StyleSystem;
using System;

namespace HundunWorld.Game.UI.Ink.Pages.Dungeon
{
    /// <summary>
    /// 江湖秘境入口页面 — 对应 dungeon-entry.html 设计原型。
    /// <para>
    /// 三栏布局的副本选择页面，提供：
    /// <list type="bullet">
    ///   <item>顶部：标题"江湖秘境" + 总战力/通关数/秘境积分副信息行 + 难度筛选 Tab（普通/困难/噩梦/地狱）+ 返回按钮</item>
    ///   <item>左侧（260px）：秘境分类列表（单人/组队/门派/限时活动，4 组共 9 个秘境条目）</item>
    ///   <item>中央（flex）：3 张秘境卡片（修行洞府/试炼塔/心魔幻境），含战力、通关时间、BOSS 列表、掉落、剩余次数、选择按钮</item>
    ///   <item>右侧（300px）：当前选择信息 + 队伍成员 + 战力对比 + 难度选择 + 攻略提示 + 最近通关 + 进入秘境按钮</item>
    /// </list>
    /// 通过 <see cref="NavigationRequested"/> 事件向路由器暴露导航请求，dom-id 为 <see cref="InkPageDomIds.NavDungeonEntry"/>。
    /// </para>
    /// </summary>
    public class DungeonEntryPage : ContainerControl, IInkPage
    {
        // ===================================================================
        // 布局常量
        // =======================================================================

        /// <summary>主面板宽度（对应 HTML min(100%,1400px)）</summary>
        private const float PanelWidth = 1400f;

        /// <summary>主面板高度（对应 HTML min(100%,900px)）</summary>
        private const float PanelHeight = 900f;

        /// <summary>顶部标题栏高度</summary>
        private const float HeaderHeight = 80f;

        /// <summary>左侧分类面板宽度</summary>
        private const float LeftPanelWidth = 260f;

        /// <summary>右侧队伍面板宽度</summary>
        private const float RightPanelWidth = 300f;

        /// <summary>难度筛选 Tab 宽度</summary>
        private const float DiffTabWidth = 64f;

        /// <summary>难度筛选 Tab 高度</summary>
        private const float DiffTabHeight = 28f;

        /// <summary>难度筛选 Tab 间距</summary>
        private const float DiffTabGap = 4f;

        /// <summary>返回按钮宽度</summary>
        private const float BackBtnWidth = 80f;

        /// <summary>返回按钮高度</summary>
        private const float BackBtnHeight = 32f;

        /// <summary>秘境卡片宽度（2 列网格）</summary>
        private const float CardWidth = 460f;

        /// <summary>秘境卡片高度</summary>
        private const float CardHeight = 340f;

        /// <summary>卡片间距</summary>
        private const float CardGap = 16f;

        /// <summary>屏幕边缘留白</summary>
        private const float ScreenEdge = 16f;

        // ===================================================================
        // 子控件引用
        // =======================================================================

        /// <summary>主面板（容纳所有内容）</summary>
        private InkPanel _mainPanel;

        /// <summary>顶部标题栏</summary>
        private InkPanel _header;

        /// <summary>标题"江湖秘境"</summary>
        private Label _titleLabel;

        /// <summary>副信息标签（总战力 / 通关数 / 秘境积分）</summary>
        private Label _subInfoLabel;

        /// <summary>返回按钮</summary>
        private InkButton _backButton;

        /// <summary>4 个难度筛选 Tab</summary>
        private InkButton[] _diffTabs;

        /// <summary>左侧分类面板</summary>
        private InkPanel _leftPanel;

        /// <summary>4 个分类组容器（单人/组队/门派/限时活动）</summary>
        private ContainerControl[] _catGroups;

        /// <summary>9 个分类条目按钮（3+3+1+2）</summary>
        private InkButton[] _catItems;

        /// <summary>中央卡片区域</summary>
        private InkPanel _middlePanel;

        /// <summary>3 张秘境卡片容器</summary>
        private InkPanel[] _cards;

        /// <summary>3 个选择按钮（位于卡片底部）</summary>
        private InkButton[] _selectButtons;

        /// <summary>右侧队伍面板</summary>
        private InkPanel _rightPanel;

        /// <summary>当前选择秘境名称标签</summary>
        private Label _selectedNameLabel;

        /// <summary>队伍战力数值标签</summary>
        private Label _partyPowerLabel;

        /// <summary>战力对比进度条</summary>
        private InkBar _powerBar;

        /// <summary>战力状态标签（达标/不足）</summary>
        private Label _powerStatusLabel;

        /// <summary>4 个难度选择按钮</summary>
        private InkButton[] _diffButtons;

        /// <summary>攻略提示文字标签</summary>
        private Label _strategyLabel;

        /// <summary>3 个最近通关条目容器</summary>
        private ContainerControl[] _historyRows;

        /// <summary>进入秘境大按钮</summary>
        private InkButton _enterButton;

        // ===================================================================
        // 公共 API
        // =======================================================================

        /// <summary>
        /// 导航请求事件。触发后由 MainUIManager 订阅并调用 InkPageRouter.NavigateTo。
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
        public DungeonEntryPage()
        {
            try
            {
                AnchorPreset = AnchorPresets.StretchAll;
                Offsets = Margin.Zero;
                BackgroundColor = InkWashTheme.Scrim;
                ClipChildren = false;
                AutoFocus = false;

                BuildMainPanel();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[DungeonEntryPage] 初始化失败: {ex.Message}");
            }
        }

        // ===================================================================
        // Build 方法
        // =======================================================================

        /// <summary>
        /// 构建主面板：包含 header + body（3 列）。
        /// </summary>
        private void BuildMainPanel()
        {
            _mainPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(PanelWidth, PanelHeight),
            };

            BuildHeader();
            BuildLeftPanel();
            BuildMiddlePanel();
            BuildRightPanel();

            AddChild(_mainPanel);
        }

        /// <summary>
        /// 构建顶部标题栏：江湖秘境 + 副信息 + 难度 Tab + 返回按钮。
        /// </summary>
        private void BuildHeader()
        {
            _header = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = Float2.Zero,
                Size = new Float2(PanelWidth, HeaderHeight),
            };

            // 标题"江湖秘境"
            _titleLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(16f, 12f),
                Size = new Float2(160f, 28f),
                Text = "江湖秘境",
                TextColor = InkWashTheme.TextGold,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 22f),
                HorizontalAlignment = TextAlignment.Near,
            };
            _header.AddChild(_titleLabel);

            // 副信息行（总战力 / 通关数 / 秘境积分）
            _subInfoLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(16f, 44f),
                Size = new Float2(700f, 24f),
                Text = "总战力 32,450  |  通关数 47  |  秘境积分 1,280  |  辰时三刻",
                TextColor = InkWashTheme.TextSecondary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 13f),
                HorizontalAlignment = TextAlignment.Near,
            };
            _header.AddChild(_subInfoLabel);

            // 返回按钮（右上角）
            _backButton = new InkButton
            {
                Variant = InkButtonVariant.Default,
                ButtonSize = InkButtonSize.Sm,
                Text = "返回",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(PanelWidth - BackBtnWidth - 16f, 12f),
                Size = new Float2(BackBtnWidth, BackBtnHeight),
            };
            _backButton.ButtonClicked += (b) => OnSystemNavButtonClicked(InkPageDomIds.CombatHud, b);
            _header.AddChild(_backButton);

            // 难度筛选 Tab（4 个，返回按钮左侧）
            _diffTabs = new InkButton[4];
            string[] diffNames = { "普通", "困难", "噩梦", "地狱" };
            InkButtonVariant[] diffVariants =
            {
                InkButtonVariant.Default,
                InkButtonVariant.Primary,
                InkButtonVariant.Vermilion,
                InkButtonVariant.Ghost,
            };

            float tabsTotalWidth = 4f * DiffTabWidth + 3f * DiffTabGap;
            float tabsStartX = PanelWidth - BackBtnWidth - 16f - 16f - tabsTotalWidth;
            for (int i = 0; i < 4; i++)
            {
                var tab = new InkButton
                {
                    Variant = diffVariants[i],
                    ButtonSize = InkButtonSize.Sm,
                    Text = diffNames[i],
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(tabsStartX + i * (DiffTabWidth + DiffTabGap), 12f),
                    Size = new Float2(DiffTabWidth, DiffTabHeight),
                };
                _diffTabs[i] = tab;
                _header.AddChild(tab);
            }

            // header 底部分割线
            var divider = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, HeaderHeight - 1f),
                Size = new Float2(PanelWidth, 1f),
                BackgroundColor = InkWashTheme.BorderGold,
            };
            _header.AddChild(divider);

            _mainPanel.AddChild(_header);
        }

        /// <summary>
        /// 构建左侧分类面板：4 组秘境分类 + 9 个条目。
        /// </summary>
        private void BuildLeftPanel()
        {
            _leftPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, HeaderHeight),
                Size = new Float2(LeftPanelWidth, PanelHeight - HeaderHeight),
            };

            // 分类标题
            var catTitle = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(16f, 12f),
                Size = new Float2(LeftPanelWidth - 32f, 20f),
                Text = "秘境分类",
                TextColor = InkWashTheme.TextSecondary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 13f),
                HorizontalAlignment = TextAlignment.Near,
            };
            _leftPanel.AddChild(catTitle);

            // 4 个分类组
            _catGroups = new ContainerControl[4];
            _catItems = new InkButton[9];

            string[] catNames = { "单人秘境", "组队秘境", "门派秘境", "限时活动" };
            string[] catCounts = { "3", "3", "1", "2" };
            // 9 个条目：3+3+1+2
            string[][] itemData =
            {
                new[] { "修行洞府|普通|日 3/3", "试炼塔|困难|日 2/5", "心魔幻境|噩梦|周 1/3" },
                new[] { "幽冥洞|困难|日 2/5", "天劫阵|噩梦|日 1/3", "龙渊秘境|地狱|周 0/2" },
                new[] { "太虚阁|噩梦|周 1/2" },
                new[] { "古墓探秘|双倍|剩2时", "中秋灯会|节日|剩3日" },
            };

            float cursorY = 40f;
            int itemIdx = 0;
            for (int g = 0; g < 4; g++)
            {
                var group = new ContainerControl
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(0f, cursorY),
                    Size = new Float2(LeftPanelWidth, 28f + itemData[g].Length * 44f),
                    BackgroundColor = Color.Transparent,
                };

                // 分类头部
                var headerLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(16f, 6f),
                    Size = new Float2(LeftPanelWidth - 60f, 18f),
                    Text = catNames[g],
                    TextColor = (g == 0) ? InkWashTheme.TextGold : InkWashTheme.TextTertiary,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 13f),
                    HorizontalAlignment = TextAlignment.Near,
                };
                group.AddChild(headerLabel);

                // 分类计数
                var countLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(LeftPanelWidth - 48f, 6f),
                    Size = new Float2(32f, 18f),
                    Text = catCounts[g],
                    TextColor = InkWashTheme.TextTertiary,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 11f),
                    HorizontalAlignment = TextAlignment.Center,
                };
                group.AddChild(countLabel);

                // 条目
                for (int i = 0; i < itemData[g].Length; i++)
                {
                    var parts = itemData[g][i].Split('|');
                    string itemName = parts[0];
                    string itemTag = parts[1];
                    string itemRemain = parts[2];

                    bool isActive = (g == 0 && i == 1); // 试炼塔默认选中

                    var itemBtn = new InkButton
                    {
                        Variant = isActive ? InkButtonVariant.Primary : InkButtonVariant.Ghost,
                        ButtonSize = InkButtonSize.Sm,
                        Text = itemName,
                        AnchorPreset = AnchorPresets.TopLeft,
                        Location = new Float2(16f, 28f + i * 44f),
                        Size = new Float2(LeftPanelWidth - 32f, 40f),
                    };
                    group.AddChild(itemBtn);
                    _catItems[itemIdx] = itemBtn;
                    itemIdx++;
                }

                _catGroups[g] = group;
                _leftPanel.AddChild(group);
                cursorY += 28f + itemData[g].Length * 44f + 8f;
            }

            _mainPanel.AddChild(_leftPanel);
        }

        /// <summary>
        /// 构建中央卡片区域：3 张秘境卡片。
        /// </summary>
        private void BuildMiddlePanel()
        {
            float middleX = LeftPanelWidth + 1f;
            float middleW = PanelWidth - LeftPanelWidth - RightPanelWidth - 2f;

            _middlePanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(middleX, HeaderHeight),
                Size = new Float2(middleW, PanelHeight - HeaderHeight),
            };

            // 中部标题
            var middleTitle = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(20f, 12f),
                Size = new Float2(300f, 20f),
                Text = "单人秘境 · 共 3 处秘境",
                TextColor = InkWashTheme.TextGold,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 14f),
                HorizontalAlignment = TextAlignment.Near,
            };
            _middlePanel.AddChild(middleTitle);

            // 3 张秘境卡片
            _cards = new InkPanel[3];
            _selectButtons = new InkButton[3];

            // 卡片数据
            string[] cardNames = { "修行洞府", "试炼塔", "心魔幻境" };
            string[] cardEngNames = { "CULTIVATION CAVE", "TRIAL PAGODA", "INNER DEMON" };
            string[] cardDiffs = { "普通", "困难", "噩梦" };
            Color[] cardDiffColors =
            {
                InkWashTheme.QualityCommon,
                InkWashTheme.QualityRare,
                InkWashTheme.QualityEpic,
            };
            string[] cardPowers = { "12,000", "25,000", "45,000" };
            string[] cardTimes = { "~8分钟", "~15分钟", "~20分钟" };
            string[] cardDescs =
            {
                "隐于深山的修行之地，内有前辈留下的武学残卷，可助修行者参悟心法奥义。",
                "七层试炼之塔，每层皆有不同考验，登顶者可获心法秘籍与上古遗宝。",
                "直面内心深处的魔障，唯有心志坚定者方可破幻而出，超脱凡尘。",
            };
            string[][] cardBosses =
            {
                new[] { "石傀儡", "玄蛇长老", "洞府守灵" },
                new[] { "铁掌门人", "幻影剑客", "塔灵", "千面书生" },
                new[] { "贪念之魔", "嗔怒之魔", "痴念之魔", "心魔本相" },
            };
            string[] cardRewards = { "普通 → 稀有", "稀有 → 史诗", "史诗 → 传说" };
            string[] cardRemains = { "今日 3/3", "今日 2/5", "本周 1/3" };
            bool[] cardSelected = { false, true, false };

            float cardStartY = 44f;
            for (int i = 0; i < 3; i++)
            {
                var card = new InkPanel
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(20f, cardStartY + i * (CardHeight + CardGap)),
                    Size = new Float2(CardWidth, CardHeight),
                    BackgroundColor = cardSelected[i]
                        ? new Color(
                            InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G,
                            InkWashTheme.GoldPrimary.B, 0.08f)
                        : InkWashTheme.PanelSolid,
                };

                // 卡片标题行：名称 + 难度徽章
                var nameLabels = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(14f, 10f),
                    Size = new Float2(280f, 24f),
                    Text = cardNames[i],
                    TextColor = InkWashTheme.TextDefault,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 18f),
                    HorizontalAlignment = TextAlignment.Near,
                };
                card.AddChild(nameLabels);

                // 英文名
                var engLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(14f, 34f),
                    Size = new Float2(280f, 14f),
                    Text = cardEngNames[i],
                    TextColor = InkWashTheme.TextTertiary,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 10f),
                    HorizontalAlignment = TextAlignment.Near,
                };
                card.AddChild(engLabel);

                // 难度徽章（右上）
                var diffBadge = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(CardWidth - 70f, 14f),
                    Size = new Float2(56f, 22f),
                    Text = cardDiffs[i],
                    TextColor = cardDiffColors[i],
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 11f),
                    BackgroundColor = new Color(
                        cardDiffColors[i].R, cardDiffColors[i].G,
                        cardDiffColors[i].B, 0.15f),
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                };
                card.AddChild(diffBadge);

                // 战力 + 通关时间
                var statsLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(14f, 58f),
                    Size = new Float2(CardWidth - 28f, 18f),
                    Text = $"推荐战力 {cardPowers[i]}    通关时间 {cardTimes[i]}",
                    TextColor = InkWashTheme.TextSecondary,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 12f),
                    HorizontalAlignment = TextAlignment.Near,
                };
                card.AddChild(statsLabel);

                // 描述
                var descLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(14f, 80f),
                    Size = new Float2(CardWidth - 28f, 48f),
                    Text = cardDescs[i],
                    TextColor = InkWashTheme.TextSecondary,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 12f),
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Near,
                };
                card.AddChild(descLabel);

                // BOSS 列表
                var bossTitleLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(14f, 134f),
                    Size = new Float2(CardWidth - 28f, 16f),
                    Text = "守关首领",
                    TextColor = InkWashTheme.TextTertiary,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 11f),
                    HorizontalAlignment = TextAlignment.Near,
                };
                card.AddChild(bossTitleLabel);

                var bossLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(14f, 152f),
                    Size = new Float2(CardWidth - 28f, 32f),
                    Text = string.Join("  ", cardBosses[i]),
                    TextColor = InkWashTheme.TextDefault,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 12f),
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Near,
                };
                card.AddChild(bossLabel);

                // 奖励
                var rewardTitleLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(14f, 188f),
                    Size = new Float2(CardWidth - 28f, 16f),
                    Text = "可能掉落",
                    TextColor = InkWashTheme.TextTertiary,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 11f),
                    HorizontalAlignment = TextAlignment.Near,
                };
                card.AddChild(rewardTitleLabel);

                var rewardLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(14f, 206f),
                    Size = new Float2(CardWidth - 28f, 18f),
                    Text = cardRewards[i],
                    TextColor = InkWashTheme.TextBrand,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 12f),
                    HorizontalAlignment = TextAlignment.Near,
                };
                card.AddChild(rewardLabel);

                // 分割线
                var cardDivider = new InkPanel
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(0f, CardHeight - 56f),
                    Size = new Float2(CardWidth, 1f),
                    BackgroundColor = InkWashTheme.Divider,
                };
                card.AddChild(cardDivider);

                // 剩余次数标签
                var remainLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(14f, CardHeight - 44f),
                    Size = new Float2(200f, 32f),
                    Text = cardRemains[i],
                    TextColor = InkWashTheme.TextSecondary,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 13f),
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                };
                card.AddChild(remainLabel);

                // 选择按钮
                var selectBtn = new InkButton
                {
                    Variant = cardSelected[i] ? InkButtonVariant.Primary : InkButtonVariant.Default,
                    ButtonSize = InkButtonSize.Sm,
                    Text = cardSelected[i] ? "已选" : "选择",
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(CardWidth - 90f - 14f, CardHeight - 40f),
                    Size = new Float2(90f, 30f),
                };
                card.AddChild(selectBtn);
                _selectButtons[i] = selectBtn;

                _cards[i] = card;
                _middlePanel.AddChild(card);
            }

            _mainPanel.AddChild(_middlePanel);
        }

        /// <summary>
        /// 构建右侧队伍配置面板：当前选择 + 队伍成员 + 战力对比 + 难度选择 + 攻略 + 通关历史 + 进入按钮。
        /// </summary>
        private void BuildRightPanel()
        {
            float rightX = PanelWidth - RightPanelWidth;

            _rightPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(rightX, HeaderHeight),
                Size = new Float2(RightPanelWidth, PanelHeight - HeaderHeight),
            };

            // 队伍配置标题
            var partyTitle = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(16f, 12f),
                Size = new Float2(RightPanelWidth - 32f, 20f),
                Text = "队伍配置",
                TextColor = InkWashTheme.TextSecondary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 13f),
                HorizontalAlignment = TextAlignment.Near,
            };
            _rightPanel.AddChild(partyTitle);

            // 当前选择
            var selectedTitleLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(16f, 40f),
                Size = new Float2(RightPanelWidth - 32f, 16f),
                Text = "当前选择：困难",
                TextColor = InkWashTheme.TextTertiary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 11f),
                HorizontalAlignment = TextAlignment.Near,
            };
            _rightPanel.AddChild(selectedTitleLabel);

            _selectedNameLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(16f, 58f),
                Size = new Float2(RightPanelWidth - 32f, 24f),
                Text = "试炼塔",
                TextColor = InkWashTheme.TextGold,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 16f),
                HorizontalAlignment = TextAlignment.Near,
            };
            _rightPanel.AddChild(_selectedNameLabel);

            // 队伍成员标题
            var memberTitleLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(16f, 92f),
                Size = new Float2(RightPanelWidth - 32f, 16f),
                Text = "队伍成员 (1/1)",
                TextColor = InkWashTheme.TextTertiary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 12f),
                HorizontalAlignment = TextAlignment.Near,
            };
            _rightPanel.AddChild(memberTitleLabel);

            // 队长信息行
            var memberRow = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(16f, 112f),
                Size = new Float2(RightPanelWidth - 32f, 44f),
                BackgroundColor = Color.Transparent,
            };

            // 头像占位
            var avatarLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, 4f),
                Size = new Float2(36f, 36f),
                Text = "游",
                TextColor = InkWashTheme.TextBrand,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 14f),
                BackgroundColor = InkWashTheme.BaseTertiary,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };
            memberRow.AddChild(avatarLabel);

            var memberInfoLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(44f, 0f),
                Size = new Float2(RightPanelWidth - 32f - 44f, 44f),
                Text = "游侠 (队长)\n剑客 · 32,450",
                TextColor = InkWashTheme.TextDefault,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 12f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            memberRow.AddChild(memberInfoLabel);
            _rightPanel.AddChild(memberRow);

            // 战力对比标题
            var powerTitleLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(16f, 168f),
                Size = new Float2(RightPanelWidth - 32f, 16f),
                Text = "战力对比",
                TextColor = InkWashTheme.TextTertiary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 12f),
                HorizontalAlignment = TextAlignment.Near,
            };
            _rightPanel.AddChild(powerTitleLabel);

            // 队伍战力数值
            _partyPowerLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(16f, 186f),
                Size = new Float2(RightPanelWidth - 32f, 18f),
                Text = "队伍战力 32,450 / 推荐 25,000",
                TextColor = InkWashTheme.TextBrand,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 13f),
                HorizontalAlignment = TextAlignment.Near,
            };
            _rightPanel.AddChild(_partyPowerLabel);

            // 战力对比进度条
            _powerBar = new InkBar
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(16f, 208f),
                Size = new Float2(RightPanelWidth - 32f, 10f),
                Value = 1.0f,
                FillVariant = InkBarFillVariant.Gold,
            };
            _rightPanel.AddChild(_powerBar);

            // 战力状态标签
            _powerStatusLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(16f, 222f),
                Size = new Float2(RightPanelWidth - 32f, 16f),
                Text = "✓ 战力达标",
                TextColor = InkWashTheme.JadeBright,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 11f),
                HorizontalAlignment = TextAlignment.Near,
            };
            _rightPanel.AddChild(_powerStatusLabel);

            // 难度选择标题
            var diffTitleLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(16f, 248f),
                Size = new Float2(RightPanelWidth - 32f, 16f),
                Text = "难度选择",
                TextColor = InkWashTheme.TextTertiary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 12f),
                HorizontalAlignment = TextAlignment.Near,
            };
            _rightPanel.AddChild(diffTitleLabel);

            // 4 个难度按钮（2x2 网格）
            _diffButtons = new InkButton[4];
            string[] diffNames = { "普通", "困难", "噩梦", "地狱" };
            bool[] diffActive = { false, true, false, false };
            float diffBtnW = (RightPanelWidth - 32f - 8f) * 0.5f;
            float diffBtnH = 30f;
            for (int i = 0; i < 4; i++)
            {
                int row = i / 2;
                int col = i % 2;
                var diffBtn = new InkButton
                {
                    Variant = diffActive[i] ? InkButtonVariant.Primary : InkButtonVariant.Default,
                    ButtonSize = InkButtonSize.Sm,
                    Text = diffNames[i],
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(16f + col * (diffBtnW + 8f), 268f + row * (diffBtnH + 6f)),
                    Size = new Float2(diffBtnW, diffBtnH),
                };
                _diffButtons[i] = diffBtn;
                _rightPanel.AddChild(diffBtn);
            }

            // 攻略提示标题
            var strategyTitleLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(16f, 340f),
                Size = new Float2(RightPanelWidth - 32f, 16f),
                Text = "攻略提示",
                TextColor = InkWashTheme.TextTertiary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 12f),
                HorizontalAlignment = TextAlignment.Near,
            };
            _rightPanel.AddChild(strategyTitleLabel);

            // 攻略文字
            _strategyLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(16f, 358f),
                Size = new Float2(RightPanelWidth - 32f, 56f),
                Text = "第三层塔灵会施展群体封印，建议携带解封符箓。千面书生形态切换时有三秒破绽窗口，把握时机连招可速通。",
                TextColor = InkWashTheme.TextSecondary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 11f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Near,
            };
            _rightPanel.AddChild(_strategyLabel);

            // 最近通关标题
            var historyTitleLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(16f, 424f),
                Size = new Float2(RightPanelWidth - 32f, 16f),
                Text = "最近通关",
                TextColor = InkWashTheme.TextTertiary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 12f),
                HorizontalAlignment = TextAlignment.Near,
            };
            _rightPanel.AddChild(historyTitleLabel);

            // 3 个最近通关条目
            _historyRows = new ContainerControl[3];
            string[] histIcons = { "噩", "普", "困" };
            string[] histNames = { "心魔幻境", "修行洞府", "试炼塔" };
            string[] histMetas = { "噩梦 · 20分12秒", "普通 · 7分35秒", "困难 · 14分08秒" };
            string[] histTimes = { "07-14", "07-14", "07-13" };
            Color[] histColors =
            {
                InkWashTheme.QualityEpic,
                InkWashTheme.QualityCommon,
                InkWashTheme.QualityRare,
            };

            for (int i = 0; i < 3; i++)
            {
                var row = new ContainerControl
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(16f, 444f + i * 36f),
                    Size = new Float2(RightPanelWidth - 32f, 32f),
                    BackgroundColor = Color.Transparent,
                };

                // 图标方块
                var iconLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(0f, 4f),
                    Size = new Float2(24f, 24f),
                    Text = histIcons[i],
                    TextColor = histColors[i],
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 12f),
                    BackgroundColor = new Color(
                        histColors[i].R, histColors[i].G,
                        histColors[i].B, 0.15f),
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                };
                row.AddChild(iconLabel);

                // 名称 + 元信息
                var infoLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(32f, 0f),
                    Size = new Float2(RightPanelWidth - 32f - 56f - 32f, 32f),
                    Text = $"{histNames[i]}\n{histMetas[i]}",
                    TextColor = InkWashTheme.TextDefault,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 11f),
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                };
                row.AddChild(infoLabel);

                // 时间
                var timeLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(RightPanelWidth - 32f - 56f, 0f),
                    Size = new Float2(56f, 32f),
                    Text = histTimes[i],
                    TextColor = InkWashTheme.TextTertiary,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 11f),
                    HorizontalAlignment = TextAlignment.Far,
                    VerticalAlignment = TextAlignment.Center,
                };
                row.AddChild(timeLabel);

                _historyRows[i] = row;
                _rightPanel.AddChild(row);
            }

            // 进入秘境大按钮（底部固定）
            _enterButton = new InkButton
            {
                Variant = InkButtonVariant.Primary,
                ButtonSize = InkButtonSize.Lg,
                Text = "进入秘境",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(16f, PanelHeight - HeaderHeight - 60f),
                Size = new Float2(RightPanelWidth - 32f, 44f),
            };
            _rightPanel.AddChild(_enterButton);

            // 提示文字
            var tipLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(16f, PanelHeight - HeaderHeight - 14f),
                Size = new Float2(RightPanelWidth - 32f, 14f),
                Text = "进入后将消耗今日次数",
                TextColor = InkWashTheme.TextTertiary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 10f),
                HorizontalAlignment = TextAlignment.Center,
            };
            _rightPanel.AddChild(tipLabel);

            _mainPanel.AddChild(_rightPanel);
        }

        // ===================================================================
        // 事件处理
        // =======================================================================

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
                FlaxEngine.Debug.LogError($"[DungeonEntryPage] NavigationRequested({domId}) 触发失败: {ex.Message}");
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
                FlaxEngine.Debug.LogWarning($"[DungeonEntryPage] EmitGoldAtButton 失败: {ex.Message}");
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

                // 主面板：屏幕居中
                if (_mainPanel != null)
                {
                    _mainPanel.Location = new Float2(
                        sw * 0.5f - PanelWidth * 0.5f,
                        sh * 0.5f - PanelHeight * 0.5f);
                }
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[DungeonEntryPage] RefreshLayout 失败: {ex.Message}");
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
